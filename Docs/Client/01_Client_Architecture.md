# Client Architecture

**V5.0 — Server-Authoritative Position with Dead-Reckoning**

The client follows a LoL-style approach: the server is authoritative for all gameplay state. The client renders from server snapshots using dead-reckoning and snap smoothing.

---

## Design Philosophy

- No client-side prediction of position
- No rollback/replay of physics
- No reconciliation of mispredictions
- Server is authoritative for position, velocity, health, abilities
- Client renders from snapshots with dead-reckoning for smoothness
- ~50ms input-to-visual latency (acceptable for MOBA gameplay)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     CLIENT ARCHITECTURE                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  User Input                                                      │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  InputCollector (Update - every frame)                     │   │
│  │  - WASD/ZQSD → MoveDir (reads layout from Settings)       │   │
│  │  - Right-click ground → MoveTo                             │   │
│  │  - Right-click enemy → AttackTarget                        │   │
│  │  - Ability keys → CastController → GameCommand             │   │
│  │  - Tab/LClick → TargetingSystem                            │   │
│  └─────────────────────────┬─────────────────────────────────┘   │
│                             │                                    │
│                             ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  IntentBuilder (rate-limited 120Hz for WASD)              │   │
│  │  + CastController (NormalCast / QuickCast / QCWI)         │   │
│  │  + TargetingSystem (Tab-target, lock/cycle/clear)          │   │
│  └─────────────────────────┬─────────────────────────────────┘   │
│                             │                                    │
│                             ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  NetworkClient (120Hz send rate)                           │   │
│  │  - Queues InputIntent → InputPacket (14 bytes UDP)         │   │
│  │  - Queues GameCommand → InputBuffer (redundancy)           │   │
│  └─────────────────────────┬─────────────────────────────────┘   │
│                             │                                    │
│                        Server Processing                         │
│                             │                                    │
│                             ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  NetworkClient.OnSnapshotReceived (60Hz)                   │   │
│  │  - Dispatches pos/vel/rot to each PlayerView               │   │
│  │  - Updates local SimWorld (cooldowns, health)              │   │
│  │  - Acknowledges event commands                             │   │
│  └─────────────────────────┬─────────────────────────────────┘   │
│                             │                                    │
│                             ▼                                    │
│  ┌───────────────────────────────────────────────────────────┐   │
│  │  EntityView / PlayerView (Update - every frame)            │   │
│  │  - Dead-reckoning: pos = serverPos + vel * dt              │   │
│  │  - Snap smoothing: lerp to dead-reckoned pos (k=18)        │   │
│  │  - Snap immediately when stopped (vel ≈ 0)                 │   │
│  │  - Animations derived from server velocity                 │   │
│  └───────────────────────────────────────────────────────────┘   │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
```

**Key Files:**

| File | Purpose |
|------|---------|
| `Scripts/Core/NetworkClient.cs` | Central network hub: connect, send, receive, spawn |
| `Scripts/Client/View/Entities/EntityView.cs` | Base rendering: dead-reckoning + snap smoothing |
| `Scripts/Client/View/Entities/PlayerView.cs` | Player-specific: class controller, health bar, animations |
| `Scripts/Client/Input/InputCollector.cs` | Raw input capture, routes to subsystems |
| `Scripts/Client/Input/IntentBuilder.cs` | Rate-limited intent construction |
| `Scripts/Client/Casting/CastController.cs` | Cast mode logic (QuickCast, NormalCast, QCWI) |
| `Scripts/Client/Targeting/TargetingSystem.cs` | Tab-target: lock, cycle, clear |

---

## NetworkClient

### Purpose

Central hub for client-side networking. Handles:
1. Connection to server via FishNetAdapter
2. Sending input packets (120Hz)
3. Receiving snapshots and reliable events
4. Entity spawning/despawning (with deferred spawn for class selection)
5. Local SimWorld for cooldown/health tracking

**File:** `Scripts/Core/NetworkClient.cs`
**Namespace:** `MOBANet.UnityView.Core`

---

### Initialization

```csharp
// NetworkClient.cs:129
void Awake()
{
    _simWorld = new SimWorld { IsServer = false };
    _eventBuffer = new InputBuffer();
    _inputSendInterval = 1f / _inputSendRate;

    // Auto-setup ping measurement via reflection (avoids requiring
    // manual Inspector wiring — sets private _networkClient field)
    var pingMeasure = GetComponent<NetworkPingMeasure>();
    if (pingMeasure == null)
        pingMeasure = gameObject.AddComponent<NetworkPingMeasure>();
    var field = typeof(NetworkPingMeasure).GetField("_networkClient",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    if (field != null)
        field.SetValue(pingMeasure, this);
}
```

**Key state:**
- `_simWorld` — Client-side SimWorld for cooldown/health tracking (not prediction)
- `_eventBuffer` — Ring buffer for event commands (UDP redundancy)
- `_inputSendInterval` — 1/120 = 8.3ms between input packets
- `_playerViews` — Dictionary<uint, PlayerView> mapping entityId → view
- `_localSpawnDeferred` — True when waiting for class selection

---

### Update Loop

```csharp
// NetworkClient.cs:179
void Update()
{
    if (!_isConnected) return;
    if (_pendingLocalPlayerInit) TryInitializeLocalPlayer();
    SendInputUpdate();  // Rate-limited to 120Hz
}

// NetworkClient.cs:193
void LateUpdate()
{
    if (!_isConnected) return;

    // Flush outgoing packets once per frame (frame guard prevents double-flush)
    int currentFrame = Time.frameCount;
    if (currentFrame != _lastOutgoingFlushFrame)
    {
        _netAdapter.ForceIterateOutgoing();
        _lastOutgoingFlushFrame = currentFrame;
    }
}
```

---

### Input Sending

```csharp
// NetworkClient.cs:216
private void SendInputUpdate()
{
    if (_localEntityId == 0) return;

    _inputSendAccumulator += Time.deltaTime;
    if (_inputSendAccumulator < _inputSendInterval) return;
    _inputSendAccumulator -= _inputSendInterval;

    // Include recent event commands for UDP redundancy
    var eventCommands = _eventBuffer.GetRecentCommands(NetcodeConstants.INPUT_REDUNDANCY_COUNT);

    if (_pendingIntent.HasValue) _movementSeq++;

    var packet = CreateInputPacketFromIntent(_pendingIntent, eventCommands);
    _netAdapter.SendInputPacket(packet);
    _pendingIntent = null;
}
```

**Flow:**
1. Rate-limit check (accumulator >= 8.3ms)
2. Get last 3 event commands for UDP redundancy
3. Increment `_movementSeq` if intent present
4. Build `InputPacket` from intent + events
5. Send via FishNetAdapter (unreliable UDP)
6. Clear `_pendingIntent`

---

### Snapshot Reception

```csharp
// NetworkClient.cs (OnSnapshotReceived)
private void OnSnapshotReceived(int _, SnapshotDelta snapshot)
{
    for (int i = 0; i < snapshot.EntityCount; i++)
    {
        ref readonly var state = ref snapshot.Entities[i];

        if (_playerViews.TryGetValue(state.EntityId, out var view))
        {
            // Unified path: both local and remote use dead-reckoning
            view.OnSnapshotReceived(state.Position, state.Velocity, state.Rotation);

            // Update SimPlayer state (for cooldowns, health display, targeting)
            var simPlayer = _simWorld.GetEntity<SimPlayer>(state.EntityId);
            if (simPlayer != null)
                simPlayer.ApplyNetworkState(state.Position, state.Velocity, state.Rotation, state);
        }
    }
    _eventBuffer.AcknowledgeUpTo(snapshot.AckInputSeq);
}
```

**Processing:**
1. Iterate all entities in snapshot
2. Call `view.OnSnapshotReceived(pos, vel, rot)` — stores state for dead-reckoning
3. Update `SimPlayer` in local SimWorld (health, cooldowns)
4. Acknowledge event commands (remove from redundancy buffer)

---

### Event Reception

```csharp
// NetworkClient.cs:382
private void OnEventReceived(int _, ReliableEvent evt)
{
    switch (evt.Type)
    {
        case EntitySpawn:    OnEntitySpawn(evt);                           break;
        case EntityDeath:    OnEntityDeath(evt.EntityId);                  break;
        case ClassAssign:    OnClassAssign(evt);                           break;
        case DamageDealt:    OnDamageDealt(evt);                           break;
        case EntityRespawn:  OnEntityRespawn(evt);                         break;
        case AbilityUsed:    OnAbilityUsed(evt);                           break;
        case Ping:           GetComponent<NetworkPingMeasure>()
                                 ?.OnPongReceived(evt.Data1);              break;
    }
}
```

**Handled events:**
- `EntitySpawn` — Spawn SimPlayer + PlayerView (deferred for local player until class selected)
- `EntityDeath` — Deactivate entity, hide health bar, play death animation
- `EntityRespawn` — Teleport to spawn position, reactivate, play respawn animation
- `ClassAssign` — Set class on PlayerView (activates animation layer + controller)
- `DamageDealt` — Floating damage text, update health
- `AbilityUsed` — Trigger ability animation on PlayerView
- `Ping` — Forward to NetworkPingMeasure for RTT calculation

---

### Entity Lifecycle: Deferred Spawn

The local player spawn is deferred until class selection:

```
1. Server sends EntitySpawn (reliable)
2. Client creates SimPlayer in local SimWorld
3. Local player: sets _localSpawnDeferred = true, no PlayerView yet
4. ClassSelectionUI appears (IsWaitingForClassSelection == true)
5. Player picks class → SendEventCommand(ClassSelect)
6. Server sends ClassAssign event
7. Client calls SpawnPlayerView() → creates PlayerView
8. InputCollector._networkClient is set via System.Reflection (field injection)
```

**Remote players** spawn immediately with a default PlayerView. Their class is set later via `ClassAssign` event.

---

## Visual Rendering (V5.0)

### Dead-Reckoning + Snap Smoothing

**File:** `Scripts/Client/View/Entities/EntityView.cs`

```csharp
// EntityView.cs:201
protected virtual void UpdatePosition()
{
    if (!_hasSnapshot) return;

    // Dead-reckoning: extrapolate from last snapshot
    float dt = Time.time - _lastSnapshotTime;
    Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

    if (_lastServerVel.sqrMagnitude < 0.01f)
    {
        // Stopped: snap directly (prevents sliding)
        _visualPos = deadReckonedPos;
    }
    else
    {
        // Moving: smooth lerp toward dead-reckoned position
        _visualPos = Vector3.Lerp(_visualPos, deadReckonedPos,
            NetcodeConstants.VISUAL_SMOOTHING_SPEED * Time.deltaTime);
    }

    transform.position = _visualPos;
    transform.rotation = Quaternion.Euler(0, _lastServerRotY, 0);
}
```

**VISUAL_SMOOTHING_SPEED = 18** (`NetcodeConstants.cs:92`). Higher = faster convergence.

**Key behaviors:**
- **Moving:** Lerp toward dead-reckoned position (smooth visual movement)
- **Stopped:** Snap immediately (prevents sliding after stop command)
- **First snapshot:** Initialize `_visualPos` directly (no lerp from origin)

---

### OnSnapshotReceived

```csharp
// EntityView.cs:136
public virtual void OnSnapshotReceived(Vector3 position, Vector3 velocity, float rotationY)
{
    if (!_hasSnapshot) _visualPos = position;  // First snapshot: no smoothing

    _lastServerPos = position;
    _lastServerVel = velocity;
    _lastServerRotY = rotationY;
    _lastSnapshotTime = Time.time;
    _hasSnapshot = true;
}
```

Stores server state for dead-reckoning. Called at 60Hz from `NetworkClient.OnSnapshotReceived()`.

---

## PlayerView

### Purpose

Extends `EntityView` with player-specific features: class animation controllers, health bar, combat events.

**File:** `Scripts/Client/View/Entities/PlayerView.cs`
**Inherits:** `EntityView`

---

### Class System

PlayerView creates a class-specific animation controller based on `SimPlayer.ClassId`:

```csharp
// PlayerView.cs:193
private ICharacterClassController CreateClassController(CharacterClassType classType)
{
    switch (classType)
    {
        case CharacterClassType.Archer:   return new ArcherController();
        case CharacterClassType.Mage:     return new MageController();
        case CharacterClassType.Fighter:  return new FighterController();
        case CharacterClassType.Assassin: return new AssassinController();
        case CharacterClassType.Tank:     return new TankController();
        case CharacterClassType.Healer:   return new HealerController();
        case CharacterClassType.Summoner: return new SummonerController();
        case CharacterClassType.Warrior:  return new WarriorController();
        case CharacterClassType.None:
        default:
            Debug.LogWarning($"[PlayerView] No class controller for ClassId: {classType}");
            return null;
    }
}
```

**8 classes**, each with unique ability animations. Class can change at runtime via `SetClass()` (triggers layer switch + new controller).

---

### Animation

```csharp
// PlayerView.cs:154
private void UpdateBaseLayerAnimations()
{
    Vector3 velocity = _simPlayer.Transform.EffectiveVelocity;
    float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
    bool isMoving = horizontalSpeed > 0.1f;
    bool isGrounded = _simPlayer.Transform.IsGrounded;
    float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);

    _animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
    _animator.SetBool(IsMovingHash, isMoving);
    _animator.SetBool(IsGroundedHash, isGrounded);

    // Jump/fall detection from vertical velocity
    bool isJumping = !isGrounded && velocity.y > 0.5f;
    bool isFalling = !isGrounded && velocity.y < -0.5f;
    _animator.SetBool(IsJumpingHash, isJumping);
    _animator.SetBool(IsFallingHash, isFalling);
}
```

**Animator parameters (derived from server state):**

| Parameter | Source | Description |
|-----------|--------|-------------|
| `Speed` | `velocity.magnitude / 8` | Normalized 0-1, for blend tree |
| `IsMoving` | `horizontalSpeed > 0.1` | Idle ↔ Run transition |
| `IsGrounded` | `SimPlayer.Transform.IsGrounded` | Ground contact |
| `IsJumping` | `!grounded && velY > 0.5` | Ascending |
| `IsFalling` | `!grounded && velY < -0.5` | Descending |

No `isMoving` flag is sent over the network — derived from velocity locally.

---

### Health Bar

Each PlayerView creates a world-space `HealthBarUI`:

```csharp
// PlayerView.cs:98
var healthBarGO = new GameObject("HealthBar");
healthBarGO.transform.SetParent(transform);
_healthBar = healthBarGO.AddComponent<HealthBarUI>();
_healthBar.Initialize(transform, isLocal, simPlayer.TeamId, localTeamId);
```

Updated every frame from `_simPlayer.Stats.HealthPercent`. Hidden on death, shown on respawn.

---

### Combat Events

```csharp
// PlayerView.cs:337
public override void OnDamage(int amount)
{
    base.OnDamage(amount);  // trigger "Hit" animation
    FloatingDamageText.Spawn(amount, transform.position + Vector3.up * 2f);
}
```

- `OnDamage(amount)` — Floating damage text + hit animation
- `OnDeath()` — Death animation, hide health bar
- `OnRespawn(position)` — Teleport, respawn animation, show health bar
- `TriggerAbility(slot)` — Route to class controller (e.g. `ArcherController.TriggerShoot()`)

---

### Rendering: Local vs Remote

```csharp
// PlayerView.cs:110
protected override void UpdatePosition()
{
    // Offline fallback: use SimPlayer directly (editor / no network)
    if (!_hasSnapshot && _simPlayer != null)
    {
        transform.position = _simPlayer.Transform.Position;
        transform.rotation = Quaternion.Euler(0f, _simPlayer.Transform.RotationY, 0f);
        return;
    }

    // Unified path: both local and remote use base dead-reckoning + snap smoothing
    base.UpdatePosition();

    // LOG: Final render position (local player only, for pipeline tracing)
    if (_isLocalPlayer)
    {
        MovementCycleLogger.LogRenderPosition(transform.position);
    }
}
```

**V5.0: Same rendering path for local and remote players.** No prediction, no special local handling.

---

## Time Synchronization

**V5.0 has no dedicated time sync component.** No `TimeSync.cs`, no client-side tick clock, no buffer delay calculation.

Dead-reckoning uses only:
- `Time.time` — Unity's local time
- `_lastSnapshotTime` — When the last snapshot arrived
- Extrapolation: `serverPos + serverVel * (Time.time - _lastSnapshotTime)`

The `TickClock.TickToTime(snapshot.ServerTick)` is used for debug logging only.

---

## Event Commands (Abilities, Jump)

### UDP Redundancy System

```csharp
// NetworkClient.cs
public void SendEventCommand(GameCommand cmd)
{
    if (!_isConnected) return;
    _eventBuffer.Add(cmd);
}
```

**How it works:**
1. Client adds command to `_eventBuffer` (ring buffer)
2. Every `InputPacket` includes last N commands (`INPUT_REDUNDANCY_COUNT = 3`)
3. Server acknowledges via `snapshot.AckInputSeq`
4. Client removes acknowledged commands from buffer

**Redundancy math:** With 1% packet loss, 3 redundant sends = 0.0001% delivery failure.

---

## Configuration

```csharp
// NetworkClient.cs:36-43
[SerializeField] private FishNetAdapter _netAdapter;
[SerializeField] private string _serverAddress = "127.0.0.1";
[SerializeField] private ushort _serverPort = 7777;
[SerializeField] private bool _autoConnect = false;
[SerializeField] private float _inputSendRate = 120f; // Hz
```

---

## Debugging

### Movement Cycle Logging

Full input-to-visual trace via `MovementCycleLogger`:

```
[INPUT START] dir=(0.71,0.71)
[INTENT CREATED] MoveDir seq=45 vec=(0.71,0.00,0.71) [direction]
[INTENT SENT] seq=45 type=MoveDir payload=(90,90)
[SNAPSHOT RECV] tick=1234 pos=(10.5,0.0,20.3) vel=(5.7,0.0,5.7)
[RENDER POS] pos=(10.6,0.0,20.4)
```

Match `seq` values to trace an input through the entire pipeline.

### Visual Position

```csharp
// NetworkClient.cs:101
public Vector3 GetVisualPosition() => _localPlayerView?.GetVisualPosition() ?? Vector3.zero;
```

Returns the smoothed dead-reckoned position from `EntityView._visualPos`. Used by camera follow, ability targeting, and UI.

---

## Related Documentation

- **[02_Input_System.md](02_Input_System.md)** — Input capture, intents, casting, targeting
- **[Server/01_Server_Loop.md](../Server/01_Server_Loop.md)** — Server-side input processing
- **[Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)** — Full system overview

---

**Last Updated:** 2026-02-18
**Version:** V5.0 (Dead-reckoning, no prediction)

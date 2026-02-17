# Client Architecture

**LoL-Style Client - Server-Authoritative Position with Visual Smoothing**

The client in Ryvax follows League of Legends' approach: server is authoritative for position, client renders with dead-reckoning and snap smoothing for visual continuity.

---

## Design Philosophy

### No Client-Side Prediction

**Unlike FPS games (Quake, Source, etc.):**
- ❌ No rollback/replay of physics
- ❌ No client-side collision detection
- ❌ No reconciliation of mispredictions

**Like MOBA games (LoL, Dota 2):**
- ✅ Server is authoritative for position
- ✅ Client renders from snapshots
- ✅ Dead-reckoning for smoothness
- ✅ Immediate feedback via rotation/animation

**Why?**
- MOBAs have lower movement precision requirements than FPS
- Avoiding rollback complexity reduces bugs
- Simpler architecture, easier to maintain
- ~50ms input→visual latency is acceptable for MOBA gameplay

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                     CLIENT ARCHITECTURE                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  User Input                                                      │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  InputCollector (Update - 60-120 FPS)                   │    │
│  │  - WASD → MoveDir                                        │    │
│  │  - Click → MoveTo                                        │    │
│  │  - S key → Stop                                          │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  IntentBuilder (rate-limited to 120Hz)                  │    │
│  │  - Converts raw input to InputIntent                    │    │
│  │  - Last-input-wins per frame                            │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  NetworkClient (120Hz send rate)                        │    │
│  │  - Queues InputIntent                                   │    │
│  │  - Sends InputPacket (14 bytes UDP)                     │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│                      Server Processing                           │
│                           │                                     │
│                           ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  NetworkClient.OnSnapshotReceived (60Hz)                │    │
│  │  - Receives SnapshotDelta                               │    │
│  │  - Extracts position, velocity, rotation                │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  EntityView/PlayerView (Update - 60-120 FPS)            │    │
│  │  - Dead-reckoning: pos = serverPos + vel * dt           │    │
│  │  - Snap smoothing: lerp to dead-reckoned pos            │    │
│  │  - Snap immediately when stopped (vel ≈ 0)              │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│                   Unity Rendering                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Files:**
- `Assets/Scripts/Core/NetworkClient.cs:482` - Network sync, snapshot handling
- `Assets/Scripts/Client/View/Entities/EntityView.cs:286` - Base rendering logic
- `Assets/Scripts/Client/View/Entities/PlayerView.cs:119` - Player-specific rendering
- `Assets/Scripts/Client/Input/InputCollector.cs:342` - Input capture
- `Assets/Scripts/Client/Input/IntentBuilder.cs:202` - Intent construction

---

## NetworkClient

### Purpose

Central hub for client-side networking. Handles:
1. Connection to server
2. Sending input packets
3. Receiving snapshots
4. Entity spawning/despawning
5. Event handling

**File:** `Assets/Scripts/Core/NetworkClient.cs`

---

### Initialization

```csharp
// NetworkClient.cs:105-123
void Awake()
{
    _simWorld = new SimWorld { IsServer = false };
    _eventBuffer = new InputBuffer();
    _inputSendInterval = 1f / _inputSendRate;

    // METRIC B: Auto-setup ping measurement
    var pingMeasure = GetComponent<NetworkPingMeasure>();
    if (pingMeasure == null)
    {
        pingMeasure = gameObject.AddComponent<NetworkPingMeasure>();
    }
}
```

**Key state:**
- `_simWorld` - Local copy of simulation (for remote players, not local player)
- `_eventBuffer` - Redundant event command buffer (UDP reliability)
- `_inputSendInterval` - Rate limit for input packets (120Hz = 8.3ms)

---

### Input Sending

```csharp
// NetworkClient.cs:192-213
private void SendInputUpdate()
{
    if (_localEntityId == 0) return;

    _inputSendAccumulator += Time.deltaTime;
    if (_inputSendAccumulator < _inputSendInterval) return;
    _inputSendAccumulator -= _inputSendInterval;

    var eventCommands = _eventBuffer.GetRecentCommands(NetcodeConstants.INPUT_REDUNDANCY_COUNT);

    if (_pendingIntent.HasValue)
    {
        _movementSeq++;
    }

    var packet = CreateInputPacketFromIntent(_pendingIntent, eventCommands);
    _netAdapter.SendInputPacket(packet);

    MovementCycleLogger.LogIntentSent(packet.MovementSeq, packet.IntentType, packet.Payload0, packet.Payload1);

    _pendingIntent = null;
}
```

**Called from:** `Update()` at 120Hz (line 166)

**Flow:**
1. Rate-limit check (accumulator >= 8.3ms)
2. Get recent event commands for UDP redundancy
3. Increment `_movementSeq` if intent present
4. Create `InputPacket` from intent
5. Send via FishNet adapter (unreliable UDP)
6. Clear `_pendingIntent`

**Why 120Hz?**
- Matches `IntentBuilder` rate (see Input_System.md)
- Higher frequency = lower input latency
- Minimal bandwidth cost (14 bytes/packet)

---

### Snapshot Reception

```csharp
// NetworkClient.cs:317-348
private void OnSnapshotReceived(int _, SnapshotDelta snapshot)
{
    float serverTime = TickClock.TickToTime(snapshot.ServerTick);

    for (int i = 0; i < snapshot.EntityCount; i++)
    {
        ref readonly var state = ref snapshot.Entities[i];

        if (_playerViews.TryGetValue(state.EntityId, out var view))
        {
            // Unified path: both local and remote use the same rendering
            view.OnSnapshotReceived(state.Position, state.Velocity, state.Rotation);

            // Update sim state for remote players
            if (state.EntityId != _localEntityId)
            {
                var simPlayer = _simWorld.GetEntity(state.EntityId) as SimPlayer;
                if (simPlayer != null)
                {
                    simPlayer.ApplyNetworkState(state.Position, state.Velocity, state.Rotation);
                }
            }
            else
            {
                // Log local player snapshots for debugging
                MovementCycleLogger.LogSnapshotReceived(snapshot.ServerTick, state.Position, state.Velocity, serverTime);
            }
        }
    }

    _eventBuffer.AcknowledgeUpTo(snapshot.AckInputSeq);
}
```

**Called from:** FishNet adapter when snapshot arrives (60Hz)

**Processing:**
1. Iterate all entities in snapshot
2. Find corresponding `PlayerView`
3. Call `view.OnSnapshotReceived(pos, vel, rot)` → stores for dead-reckoning
4. Update `SimPlayer` state for remote players (for AI/targeting)
5. Log snapshot for local player (debugging)
6. Acknowledge event commands (UDP reliability)

**V5.0 Change:**
- Both local and remote players use **unified rendering path**
- No separate prediction/interpolation systems
- `EntityView.OnSnapshotReceived()` handles all entities identically

---

## Visual Rendering (V5.0)

### Dead-Reckoning Formula

**Basic concept:**
```
visualPos = lastServerPos + lastServerVel * timeSinceSnapshot
```

**With snap smoothing:**
```
deadReckonedPos = lastServerPos + lastServerVel * dt
visualPos = lerp(visualPos, deadReckonedPos, smoothingSpeed * dt)

if (velocity ≈ 0) {
    visualPos = deadReckonedPos  // Snap immediately when stopped
}
```

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:190-213`

---

### EntityView.UpdatePosition()

```csharp
// EntityView.cs:190-213
protected virtual void UpdatePosition()
{
    if (!_hasSnapshot) return;

    // Dead-reckoning: extrapolate from last known position
    float dt = Time.time - _lastSnapshotTime;
    Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

    // Snap smoothing: lerp towards dead-reckoned position
    // BUT snap immediately when stopped (velocity ≈ 0) to avoid sliding
    if (_lastServerVel.sqrMagnitude < 0.01f)
    {
        // Stopped: snap directly to server position (no sliding)
        _visualPos = deadReckonedPos;
    }
    else
    {
        // Moving: smooth towards dead-reckoned position
        _visualPos = Vector3.Lerp(_visualPos, deadReckonedPos, NetcodeConstants.VISUAL_SMOOTHING_SPEED * Time.deltaTime);
    }

    transform.position = _visualPos;
    transform.rotation = Quaternion.Euler(0, _lastServerRotY, 0);
}
```

**Called from:** `Update()` every frame (60-120 FPS)

**Logic:**
1. **Calculate dead-reckoned position:**
   - Extrapolate from last snapshot using velocity
   - Assumes constant velocity between snapshots

2. **Apply snap smoothing:**
   - **If moving:** Lerp towards dead-reckoned position (smooth visual movement)
   - **If stopped:** Snap immediately (prevents sliding after stop)

3. **Update transform:**
   - Set Unity `transform.position` to `_visualPos`
   - Set rotation from server

**Smoothing constant:**
```csharp
// NetcodeConstants.cs
public const float VISUAL_SMOOTHING_SPEED = 10f;  // Higher = faster convergence
```

---

### OnSnapshotReceived()

```csharp
// EntityView.cs:125-138
public virtual void OnSnapshotReceived(Vector3 position, Vector3 velocity, float rotationY)
{
    // Initialize visual position on first snapshot (no smoothing from origin)
    if (!_hasSnapshot)
    {
        _visualPos = position;
    }

    _lastServerPos = position;
    _lastServerVel = velocity;
    _lastServerRotY = rotationY;
    _lastSnapshotTime = Time.time;
    _hasSnapshot = true;
}
```

**Called from:** `NetworkClient.OnSnapshotReceived()` at 60Hz

**Purpose:**
- Store server state for dead-reckoning
- Initialize `_visualPos` on first snapshot (no lerp from origin)

---

## PlayerView Specifics

### Animation System

```csharp
// PlayerView.cs:90-106
protected override void UpdateAnimation()
{
    if (_animator == null || _simPlayer == null) return;

    // Use velocity magnitude to determine if moving
    Vector3 velocity = _simPlayer.Transform.Velocity;
    float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
    bool isMoving = horizontalSpeed > 0.1f;
    bool isGrounded = _simPlayer.Transform.IsGrounded;

    float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);

    _animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
    _animator.SetBool(IsMovingHash, isMoving);
    _animator.SetBool(IsGroundedHash, isGrounded);
}
```

**Key parameters:**
- `Speed` - Normalized velocity (0-1 range, 8 u/s = 1.0)
- `IsMoving` - Derived from velocity magnitude (> 0.1 u/s)
- `IsGrounded` - Ground contact state

**Why derive from velocity?**
- No `isMoving` flag sent over network (bandwidth savings)
- Velocity is already sent for dead-reckoning
- Client computes derived states locally

---

### Local vs Remote Rendering

```csharp
// PlayerView.cs:66-84
protected override void UpdatePosition()
{
    // Fallback when not connected (offline/editor testing)
    if (!_hasSnapshot && _simPlayer != null)
    {
        transform.position = _simPlayer.Transform.Position;
        transform.rotation = Quaternion.Euler(0f, _simPlayer.Transform.RotationY, 0f);
        return;
    }

    // Unified path: both local and remote use base dead-reckoning + snap smoothing
    base.UpdatePosition();

    // LOG: Final render position for local player
    if (_isLocalPlayer)
    {
        MovementCycleLogger.LogRenderPosition(transform.position);
    }
}
```

**V5.0 Simplification:**
- **Local player:** Uses `EntityView.UpdatePosition()` (dead-reckoning)
- **Remote players:** Uses `EntityView.UpdatePosition()` (dead-reckoning)
- **Same rendering path for both!**

**Removed in V5.0:**
- ❌ Prediction system
- ❌ Reconciliation logic
- ❌ Visual offset correction
- ❌ Local intent feedback (position)

**Kept in V5.0:**
- ✅ Rotation/animation feedback (instant response)
- ✅ Dead-reckoning for smoothness
- ✅ Snap smoothing to avoid sliding

---

## Time Synchronization

### Current Status

**V5.0 Removed dedicated time sync:**
- No `TimeSync.cs` component
- No client-side tick clock
- No buffer delay calculation

**Why?**
- Dead-reckoning doesn't require synchronized time
- Snapshots include absolute positions (not deltas)
- Simplified architecture reduces complexity

---

### Snapshot Timing

```csharp
// NetworkClient.cs:319
float serverTime = TickClock.TickToTime(snapshot.ServerTick);
```

**Used for:**
- Debug logging only
- Not used in rendering calculations

**Rendering uses:**
- `Time.time` - Unity's local time
- `_lastSnapshotTime` - Time when snapshot received
- Dead-reckoning extrapolates from this point

---

## Entity Lifecycle

### Spawning

```csharp
// NetworkClient.cs:374-433
private void OnEntitySpawn(ReliableEvent evt)
{
    int ownerClientId = ReliableEvent.DecodeOwnerClientId(evt);
    byte teamId = ReliableEvent.DecodeTeamId(evt);
    bool isLocal = ownerClientId == LocalClientId;

    // Spawn SimPlayer in local SimWorld
    var simPlayer = _simWorld.SpawnPlayer(evt.EntityId, ownerClientId, Vector3.zero, teamId);

    // Instantiate Unity GameObject
    GameObject prefab = GameManager.Instance.PlayerPrefab;
    var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
    go.name = isLocal ? "Player_Local" : $"Player_Remote_{evt.EntityId}";

    // Initialize PlayerView
    var view = go.GetComponent<PlayerView>();
    if (view != null)
    {
        view.Initialize(simPlayer, isLocal: isLocal, networkClient: this);
        _playerViews[evt.EntityId] = view;
    }

    _spawnedEntities[evt.EntityId] = go;

    if (isLocal)
    {
        _localEntityId = evt.EntityId;
        _localPlayerView = view;

        // Initialize InputCollector
        _localInputCollector = go.GetComponent<InputCollector>();
        // ... reflection magic to inject NetworkClient reference

        GameManager.Instance?.OnNetworkPlayerSpawned(go, isLocalPlayer: true);
    }
}
```

**Called from:** `OnEventReceived()` when receiving `ReliableEvent.EntitySpawn`

**Flow:**
1. Decode event data (ownerClientId, teamId)
2. Spawn `SimPlayer` in local `SimWorld` (for remote players)
3. Instantiate Unity GameObject from prefab
4. Initialize `PlayerView` with `SimPlayer` reference
5. Store in dictionaries for lookup
6. If local player: Setup input collector and camera

---

### Despawning

```csharp
// NetworkClient.cs:435-438
private void OnEntityDeath(uint entityId)
{
    // TODO: Death handling
}
```

**Status:** Not fully implemented (V5.0 focuses on movement)

**Planned:**
- Destroy GameObject
- Remove from dictionaries
- Play death animation
- Respawn after delay

---

## Local Feedback (V3.0 System - Deprecated in V5.0)

### Rotation/Animation Feedback

**V3.0 approach (current code, but not actively used in V5.0):**

```csharp
// InputCollector.cs:176-186
if (_currentIsMoving)
{
    Vector3 moveDir3D = new Vector3(dir.x, 0f, dir.y);
    _networkClient.SetLocalMoveIntent(moveDir3D);
}
else
{
    _networkClient.ClearLocalMoveIntent();
}
```

**Stubs in NetworkClient:**

```csharp
// NetworkClient.cs:98-103
public void SetLocalMoveIntent(Vector3 direction) { }
public void ClearLocalMoveIntent() { }
```

**V5.0 Status:**
- Stubs remain for compatibility
- No actual local feedback implementation
- Rotation/animation follow server state from snapshots

**Why removed?**
- Simplified architecture (fewer code paths)
- ~50ms latency acceptable for MOBA gameplay
- Dead-reckoning provides sufficient smoothness

---

## Event Commands (Abilities, Jump)

### Redundant UDP Reliability

```csharp
// NetworkClient.cs:248-263
public void SendEventCommand(GameCommand cmd)
{
    if (!_isConnected) return;
    _eventBuffer.Add(cmd);
}

public void SendCommand(GameCommand cmd)
{
    if (cmd.Category == CommandCategory.Movement)
    {
        return;  // Movement handled by intents
    }
    SendEventCommand(cmd);
}
```

**Event commands include:**
- Jump
- Abilities (Q/W/E/R)
- Item usage
- Ping/emotes

**Redundancy system:**
```csharp
// NetworkClient.cs:200
var eventCommands = _eventBuffer.GetRecentCommands(NetcodeConstants.INPUT_REDUNDANCY_COUNT);
```

**How it works:**
1. Client sends event command in `InputPacket`
2. Includes last N commands (default: 3) in each packet
3. Server acknowledges via `snapshot.AckInputSeq`
4. Client clears acknowledged commands from buffer

**Why?**
- UDP packets can be lost
- Redundancy ensures delivery without TCP overhead
- Critical for abilities (must not be lost)

---

## Configuration

### Network Settings

```csharp
// NetworkClient.cs:34-40
[Header("Network")]
[SerializeField] private FishNetAdapter _netAdapter;
[SerializeField] private string _serverAddress = "127.0.0.1";
[SerializeField] private ushort _serverPort = 7777;
[SerializeField] private bool _autoConnect = false;

[Header("Input")]
[SerializeField] private float _inputSendRate = 120f; // Hz
```

**Tunable parameters:**
- `_inputSendRate` - Input packet send frequency (default: 120Hz)
- `_serverAddress` - Server IP (default: localhost)
- `_serverPort` - Server port (default: 7777)
- `_autoConnect` - Auto-connect on start (useful for testing)

---

## Debugging

### Movement Cycle Logging

**Full input→visual cycle:**

```csharp
// InputCollector.cs:168
MovementCycleLogger.LogInputStart(dir);

// IntentBuilder.cs:156
MovementCycleLogger.LogIntentCreated("MoveDir", _seqId, new Vector3(normalizedDir.x, 0, normalizedDir.y), isDirection: true);

// NetworkClient.cs:210
MovementCycleLogger.LogIntentSent(packet.MovementSeq, packet.IntentType, packet.Payload0, packet.Payload1);

// NetworkClient.cs:342
MovementCycleLogger.LogSnapshotReceived(snapshot.ServerTick, state.Position, state.Velocity, serverTime);

// PlayerView.cs:82
MovementCycleLogger.LogRenderPosition(transform.position);
```

**Output format:**
```
[INPUT START] dir=(0.71,0.71)
[INTENT CREATED] MoveDir seq=45 vec=(0.71,0.00,0.71) [direction]
[INTENT SENT] seq=45 type=MoveDir payload=(0.71,0.71)
[SNAPSHOT RECV] tick=1234 pos=(10.5,0.0,20.3) vel=(5.7,0.0,5.7) serverTime=20.567
[RENDER POS] pos=(10.6,0.0,20.4)
```

**Correlation:**
- Match `seq` values to trace input through pipeline
- Compare timestamps to measure latency
- Verify position progression (input → server → render)

---

### Visual Position Retrieval

```csharp
// NetworkClient.cs:77-82
public Vector3 GetVisualPosition() => _localPlayerView?.GetVisualPosition() ?? Vector3.zero;
public bool HasVisualPosition => _localPlayerView != null;
```

**Used by:**
- Camera follow scripts
- UI position indicators
- Ability targeting

**Returns:**
- `_visualPos` from `EntityView` (smoothed dead-reckoned position)
- Not `transform.position` directly (returns intermediate state)

---

## Performance Considerations

### Frame Rate Independence

**Update() vs FixedUpdate():**
```csharp
// NetworkClient.cs:155-167
void Update()
{
    if (!_isConnected) return;

    // Deferred local player initialization
    if (_pendingLocalPlayerInit && LocalClientId >= 0 && _localEntityId == 0)
    {
        TryInitializeLocalPlayer();
    }

    // Send pending intent and event commands
    SendInputUpdate();
}
```

**Rendering happens in Update():**
- `EntityView.Update()` → `UpdatePosition()` at 60-120 FPS
- Frame-rate independent lerp: `smoothingSpeed * Time.deltaTime`
- Higher FPS = smoother visual movement (no stutter)

---

### Packet Flushing

```csharp
// NetworkClient.cs:169-180
void LateUpdate()
{
    if (!_isConnected) return;

    // Flush outgoing packets
    int currentFrame = Time.frameCount;
    if (currentFrame != _lastOutgoingFlushFrame)
    {
        _netAdapter.ForceIterateOutgoing();
        _lastOutgoingFlushFrame = currentFrame;
    }
}
```

**Why LateUpdate()?**
- Ensures all input processed before sending
- Collects all changes from current frame
- Reduces packet count (batch updates)

**Frame guard:**
- Prevents multiple flushes per frame
- Important if multiple systems call LateUpdate()

---

## Related Documentation

- **[Input_System.md](Input_System.md)** - Input capture and intent building
- **[Server/Server_Loop.md](../Server/Server_Loop.md)** - Server-side processing
- **[Network/03_Data_Flow.md](../Network/03_Data_Flow.md)** - Complete network flow
- **[Network/04_Performance_Metrics.md](../Network/04_Performance_Metrics.md)** - Latency measurements

---

**Last Updated:** 2026-02-03
**Version:** V5.0 (Simplified - Dead-reckoning only)

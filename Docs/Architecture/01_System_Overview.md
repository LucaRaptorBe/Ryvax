# System Overview

**High-Level Architecture for Ryvax Netcode (V5.0)**

This document provides a bird's-eye view of the complete multiplayer architecture, showing how components interact from input to rendering.

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              CLIENT (Unity)                              │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌────────────────┐    ┌─────────────────┐    ┌──────────────────┐      │
│  │ InputCollector  │───▶│  IntentBuilder   │───▶│  NetworkClient   │     │
│  │ (WASD/Click)    │    │  (rate-limit)    │    │  (120Hz send)    │     │
│  └────────────────┘    └─────────────────┘    └────────┬─────────┘      │
│                                                         │                │
│                                                    ┌────▼──────────┐     │
│                                                    │ FishNetAdapter │    │
│                                                    │  (UDP send)    │    │
│                                                    └────────┬───────┘    │
│                                                             │            │
└─────────────────────────────────────────────────────────────┼────────────┘
                                                              │
                                    InputPacket (14b header)  │
                                    MovementSeq, Intent       │
                                                              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                                 SERVER                                   │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌────────────────┐    ┌─────────────────┐    ┌──────────────────┐      │
│  │ FishNetAdapter  │───▶│ InputBuffer     │───▶│ ServerGameLoop   │     │
│  │  (UDP recv)     │    │ (ConcurrentQueue│    │  (FixedUpdate)   │     │
│  └────────────────┘    │  last-input-wins)│    └────────┬─────────┘     │
│                         └─────────────────┘             │                │
│                                                    ┌────▼─────────┐     │
│                                                    │   SimWorld    │     │
│                                                    │   .Step()     │     │
│                                                    └────────┬──────┘     │
│                                                             │            │
│         ┌───────────────────────────────────────────────────┘            │
│         │                                                                │
│    ┌────▼─────────┐         ┌──────────────────┐                        │
│    │ AOIManager    │────────▶│ BroadcastSnapshot│                       │
│    │ (culling)     │         │  (60Hz default)  │                       │
│    └──────────────┘         └────────┬─────────┘                        │
│                                      │                                   │
└──────────────────────────────────────┼───────────────────────────────────┘
                                       │
                        SnapshotDelta (14b header + 30b/entity)
                        ServerTick, EntityState[]
                                       ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                          CLIENT (Receive & Render)                       │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌────────────────┐    ┌──────────────────────────────────────────┐     │
│  │ NetworkClient   │───▶│         EntityView (base class)         │     │
│  │OnSnapshotRecv   │    │                                         │     │
│  └────────────────┘    │  OnSnapshotReceived(pos, vel, rotY)      │     │
│                         │    → store server state                  │     │
│                         │                                         │     │
│                         │  UpdatePosition() [every frame]         │     │
│                         │    → deadReckonedPos = serverPos         │     │
│                         │                       + serverVel * dt   │     │
│                         │    → if stopped: snap to deadReckonedPos │     │
│                         │    → if moving:  lerp toward it          │     │
│                         │    → transform.position = visualPos      │     │
│                         └─────────────────┬───────────────────────┘     │
│                                           │                              │
│                                  ┌────────▼──────────┐                  │
│                                  │    PlayerView      │                  │
│                                  │ (class, anims, HP) │                  │
│                                  └───────────────────┘                  │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Layer Breakdown

### 1. Client Input Layer
**Purpose:** Capture user input and convert to network intents

| Component | File | Responsibility |
|-----------|------|----------------|
| InputCollector | `Scripts/Client/Input/InputCollector.cs` | Reads WASD/mouse, routes to IntentBuilder or CastController |
| IntentBuilder | `Scripts/Client/Input/IntentBuilder.cs` | Rate-limits intents (120Hz for WASD), creates InputIntent |
| InputIntent | `Scripts/Client/Input/InputIntent.cs` | Value type: MoveDir, MoveTo, Stop, Follow |
| InputBuffer | `Scripts/Client/Input/InputBuffer.cs` | Ring buffer for event commands (UDP redundancy) |
| NetworkClient | `Scripts/Core/NetworkClient.cs` | Queues intents, sends InputPackets at 120Hz |

**Key Flow:**
```
WASD press → InputCollector → IntentBuilder.OnKeyboardMove()
  → InputIntent.MoveDir (rate-limited 120Hz)
  → NetworkClient.SendInputIntent(intent)
  → SendInputUpdate() builds InputPacket
  → FishNetAdapter.SendInputPacket() [UDP unreliable]
```

**Local Feedback (V5.0):**
- Rotation: driven by server snapshot (no local prediction)
- Animation: derived from server velocity in `EntityView.UpdateAnimation()`
- Position: NO local prediction (server-authoritative, dead-reckoned between snapshots)

---

### 2. Network Adapter Layer
**Purpose:** Abstract transport details, handle message serialization

| Component | File | Responsibility |
|-----------|------|----------------|
| FishNetAdapter | `Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs` | Only file importing FishNet; wraps NetworkManager behind INetAdapter |
| InputPacket | `Scripts/Network/NetAdapter/Messages/InputPacket.cs` | Client→Server: intent + event commands |
| SnapshotDelta | `Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs` | Server→Client: entity states (unreliable) |
| ReliableEvent | `Scripts/Network/NetAdapter/Messages/ReliableEvent.cs` | Server→Client: lifecycle events (reliable) |
| GameCommand | `Scripts/Network/NetAdapter/Messages/GameCommand.cs` | Client→Server: class select, ability cast, ping |
| MatchConfigBroadcast | `Scripts/Network/NetAdapter/Messages/MatchConfigBroadcast.cs` | Server→Client: match configuration (reliable, sent on connect) |

**InputPacket (14 bytes header + variable events):**
```
ClientTick (4) | MovementSeq (4) | IntentType (1) | Payload0 (2) | Payload1 (2) | CommandCount (1)
+ GameCommand[] (redundancy buffer for UDP reliability)
```

**SnapshotDelta (14 bytes header + 30 bytes/entity):**
```
Header: ServerTick (4) | AckInputSeq (4) | AckMovementSeq (4) | EntityCount (2)
Per entity (30 bytes):
  EntityId (4) | EntityType (1) | Flags (1)
  PosX/Y/Z (6, 16-bit quantized) | RotY (2)
  Health (2) | State (1)
  VelX/Y/Z (6, 16-bit quantized) | SpeedQ (2)
  EventFlags (1) | Cd0-Cd3 (4)
```

**ReliableEvent types (TCP-equivalent):**
- `EntitySpawn`, `EntityDeath`, `EntityRespawn`, `EntityDespawn`
- `EntityEnterAOI`, `EntityLeaveAOI`
- `ClassAssign`, `AbilityUsed`, `DamageDealt`
- `Ping` (RTT measurement)

---

### 3. GameSim Layer (Simulation)
**Purpose:** Authoritative game simulation (server-side primary, client-side for local state tracking)

| Component | File | Responsibility |
|-----------|------|----------------|
| SimWorld | `GameSim/Core/SimWorld.cs` | Entity storage, `Step()` loop, `ExecuteCommand()` |
| SimConfig | `GameSim/Core/SimConfig.cs` | All gameplay constants (speed, HP, cooldowns, physics) |
| TickClock | `GameSim/Core/TickClock.cs` | Tick accumulator + timing |
| MovementEngine | `GameSim/Core/MovementEngine.cs` | Movement math (shared) |
| CommandDispatcher | `GameSim/Commands/CommandDispatcher.cs` | Routes SimCommand to ICommandHandler |
| MovementHandler | `GameSim/Commands/Handlers/MovementHandler.cs` | Applies MoveDir/MoveTo/Stop/Follow |
| AbilityHandler | `GameSim/Commands/Handlers/AbilityHandler.cs` | ICommandHandler: cast validation, SimProjectile spawn, raises AbilityUsed SimEvent |
| SystemHandler | `GameSim/Commands/Handlers/SystemHandler.cs` | ICommandHandler: ClassSelect, raises ClassAssign SimEvent |
| SimPlayer | `GameSim/Entities/SimPlayer.cs` | Player entity with component states |
| SimProjectile | `GameSim/Entities/SimProjectile.cs` | Server-only projectile (NOT a SimEntity — not in entity dict, not in snapshots). Clients get cosmetic visuals via AbilityUsed event |

**SimPlayer State Architecture (component model):**
```
SimPlayer : SimEntity
├── TransformState Transform    — Position, Velocity, RotationY, IsGrounded, MoveDirection
├── StatsState Stats            — Health, MaxHealth, Level, MoveSpeedModifier
├── AbilityState Abilities      — Cooldowns[6], Levels[6], Charges[6], CastState
└── CombatState Combat          — AttackTargetId, IsAutoAttacking, AttackCooldown, AttackSpeed, AttackRange, BaseDamage, IsAttackMoving, TimeSinceLastAttack
```

**Tick Flow:**
```csharp
// ServerGameLoop.cs:286
void RunSimulation()
{
    int ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime);
    for (int i = 0; i < ticksToRun; i++)
    {
        DrainInputQueueForTick(currentTick);      // Drain ConcurrentQueue → per-client map
        FlushPendingMovementInputs(currentTick);   // Apply last-input-wins per client
        _simWorld.Step();                           // Physics, entities, projectiles, collisions
        DrainAndBroadcastSimEvents();              // SimEvent → ReliableEvent (DamageDealt, EntityDeath)
    }
}

// OnCommandReceived: Ability/System commands
var simCmd = CommandHelper.ToSimCommand(cmd, serverTick);
_simWorld.ExecuteCommand(clientId, simCmd);          // Handler raises SimEvents
DrainAndBroadcastSimEvents();                        // SimEvent → ReliableEvent (AbilityUsed, ClassAssign)

```

**Key Properties:**
- **Pure C# class** (not MonoBehaviour) — uses `UnityEngine` only for `Vector3`/`Mathf`
- **Server-authoritative:** Only the server runs the full simulation
- **Client holds a SimWorld** too, for local state tracking (cooldowns, health display)

---

### 4. Server Layer
**Purpose:** Network hosting, input buffering, snapshot broadcasting

| Component | File | Responsibility |
|-----------|------|----------------|
| ServerGameLoop | `Scripts/Server/ServerGameLoop.cs` | Main server loop (FixedUpdate) |
| AOIManager | `Scripts/Network/NetAdapter/AOI/AOIManager.cs` | Per-client visibility sets, enter/leave events |
| SpatialHashGrid | `Scripts/Network/NetAdapter/AOI/SpatialHashGrid.cs` | Spatial partitioning for O(1) visibility queries |
| ServerCommandBuffer | `Scripts/Network/NetAdapter/Buffers/ServerCommandBuffer.cs` | Command ack tracking per client |

**FixedUpdate Loop:**
```csharp
// ServerGameLoop.cs:249
void FixedUpdate()
{
    RunSimulation();          // Accumulate + tick SimWorld
    TickWatchdog(dt);         // Force-stop if no input received for 300ms
    TickRespawns();           // Process 5s respawn queue
    BroadcastSnapshots();     // Send AOI-filtered snapshots
}
```

**Input Buffering:**
- Inputs arrive via `ConcurrentQueue` (thread-safe, network thread → main thread)
- `DrainInputQueueForTick()` sorts inputs into per-client map with `ApplyTick = RecvTick + 1`
- `FlushPendingMovementInputs()` applies **last-input-wins** per client per tick
- Watchdog: forces `StopMoving()` if no input received for 300ms (`MOVE_WATCHDOG_TIMEOUT`, `ServerGameLoop.cs:98`)

**AOI System:**
- `SpatialHashGrid` with configurable cell size (default 20 units)
- Vision radius: 50 units (configurable), with 5-unit hysteresis
- `SnapshotHelper.CreateFilteredSnapshot()` only includes visible entities
- Server sends `EntityEnterAOI`/`EntityLeaveAOI` reliable events
- **TODO:** Client-side `NetworkClient.OnEventReceived()` does not handle `EntityEnterAOI`/`EntityLeaveAOI` events (they are silently ignored). Snapshot filtering works (bandwidth is reduced), but entities that leave AOI remain as frozen ghosts on the client instead of being despawned. Not visible in testing because the 50-unit vision radius covers typical test distances.

---

### 5. Client Rendering Layer
**Purpose:** Receive snapshots, dead-reckon between them, render with smoothing

| Component | File | Responsibility |
|-----------|------|----------------|
| EntityView | `Scripts/Client/View/Entities/EntityView.cs` | Base class: dead-reckoning + snap smoothing |
| PlayerView | `Scripts/Client/View/Entities/PlayerView.cs` | Player-specific: class controller, health bar, animations |
| ProjectileView | `Scripts/Client/View/Entities/ProjectileView.cs` | Cosmetic-only projectile rendering |

**V5.0 Rendering (Dead-Reckoning + Snap Smoothing):**

There is no interpolation buffer, no visual offset system, no TimeSync. The approach is simpler:

```csharp
// EntityView.cs:201
void UpdatePosition()
{
    // Dead-reckoning: extrapolate from last snapshot
    float dt = Time.time - _lastSnapshotTime;
    Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

    if (_lastServerVel.sqrMagnitude < 0.01f)
    {
        // Stopped: snap directly (no sliding)
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

**PlayerView adds:**
- Class-specific animation controllers (Archer, Mage, Fighter, etc.)
- Health bar (world-space `HealthBarUI`)
- Base layer animation: `Speed`, `IsMoving`, `IsGrounded`, `IsJumping`, `IsFalling`
- Ability animation triggers via `TriggerAbility(slot)` from network events

---

### 6. Gameplay Systems (New in V5.0)

These systems were added after the original netcode implementation:

| System | Key Files | Description |
|--------|-----------|-------------|
| **Character Classes** | `GameSim/Data/CharacterClass.cs`, `Client/View/Animation/` | 8 classes: Archer, Mage, Fighter, Assassin, Tank, Healer, Summoner, Warrior |
| **Abilities** | `GameSim/Data/AbilityDefinition.cs`, `GameSim/Commands/Handlers/AbilityHandler.cs` | ScriptableObject definitions, server-side execution, cooldown tracking |
| **Casting** | `Scripts/Client/Casting/CastController.cs`, `SkillshotIndicator.cs` | NormalCast / QuickCast / QuickCastWithIndicator modes |
| **Targeting** | `Scripts/Client/Targeting/TargetingSystem.cs`, `TargetIndicator.cs` | Tab-target: lock, cycle, clear |
| **Projectiles** | `GameSim/Entities/SimProjectile.cs`, `Client/View/Entities/ProjectileView.cs` | Server-authoritative projectile physics + client cosmetic view |
| **HUD** | `Scripts/Client/View/UI/HUD/` | AbilityBarUI (4 slots), HealthBarUI, TargetInfoUI |
| **Settings** | `Scripts/Client/View/UI/Settings/` | Keybinds, cast mode, movement mode (configurable) |
| **Class Selection** | `Scripts/Client/View/UI/Bootstrap/ClassSelectionUI.cs` | Deferred spawn: player entity waits for class choice before creating PlayerView |

---

## Data Flow Summary

### Input → Server (Client to Server)

```
1. User presses W (or right-clicks)                          [0ms]
2. InputCollector detects input                                [0ms]
3. IntentBuilder creates MoveDir/MoveTo (rate-limited 120Hz)  [0ms]
4. NetworkClient.SendInputIntent() queues intent               [0ms]
5. SendInputUpdate() builds InputPacket at 120Hz              [0ms]
6. FishNetAdapter sends UDP packet                             [0ms]
7. LateUpdate() flushes outgoing via ForceIterateOutgoing()   [0ms]
   ── network ──
8. Server receives InputPacket                                [+RTT/2]
9. BufferMovementInput() → ConcurrentQueue                    [0ms]
10. FixedUpdate → DrainInputQueueForTick() + FlushPendingMovementInputs()
11. SimWorld.Step() applies movement                          [0ms]
```

### Simulation → Visual (Server to Client)

```
12. BroadcastSnapshots() creates AOI-filtered SnapshotDelta  [0ms]
13. FishNetAdapter sends UDP packet                           [0ms]
    ── network ──
14. Client receives SnapshotDelta                             [+RTT/2]
15. NetworkClient.OnSnapshotReceived() dispatches to views    [0ms]
16. PlayerView.OnSnapshotReceived(pos, vel, rotY)             [0ms]
17. EntityView.UpdatePosition() [every frame]:
      deadReckonedPos = serverPos + serverVel * dt
      visualPos = Lerp(visualPos, deadReckonedPos, k * dt)
      transform.position = visualPos                          [0ms]
```

---

## Key Design Principles

### 1. Server Authoritative
- Server is single source of truth for all gameplay
- Client sends only **intents** (MoveDir, MoveTo, Stop, Follow), never state
- Client displays server state with dead-reckoning smoothing

### 2. Dead-Reckoning (Not Interpolation)
- V5.0 removed the interpolation buffer/visual offset system
- Client extrapolates: `pos = lastServerPos + lastServerVel * timeSinceSnapshot`
- Lerp smoothing when moving, direct snap when stopped
- `VISUAL_SMOOTHING_SPEED = 18` controls lerp rate

### 3. No Client-Side Prediction
- Client does NOT predict future positions
- Client does NOT rollback or replay inputs
- Position updates come only from server snapshots

### 4. Immediate Server Feedback
- Rotation: from server snapshot (applied via `_lastServerRotY`)
- Animation: derived from server velocity (`_lastServerVel.magnitude`)
- Position: dead-reckoned from server state

---

## Configuration Constants

| Constant | Value | File | Purpose |
|----------|-------|------|---------|
| `TICK_RATE` | 60 Hz | `NetcodeConstants.cs:23` | Server simulation rate |
| `SNAPSHOT_RATE` | 60 Hz (= TICK_RATE) | `NetcodeConstants.cs:29` | Snapshot broadcast rate |
| `_inputSendRate` | 120 Hz | `NetworkClient.cs:43` | Client input send rate |
| `INTENT_SEND_INTERVAL` | 8.3ms (120Hz) | `IntentBuilder.cs:37` | WASD rate-limit |
| `VISUAL_SMOOTHING_SPEED` | 18 | `NetcodeConstants.cs:92` | Dead-reckoning lerp rate |
| `PLAYER_SPEED` | 8 u/s | `NetcodeConstants.cs:34` | Default movement speed |
| `INPUT_REDUNDANCY_COUNT` | 3 | `NetcodeConstants.cs:45` | Event commands per packet |
| `PlayerMaxHealth` | 100 | `SimConfig.cs:37` | Default max health |
| `PlayerRotationSpeed` | 720 deg/s | `SimConfig.cs:28` | Server-side rotation speed |
| `RespawnTime` | 5s | `SimConfig.cs:57` | Death → respawn delay |

---

## Namespace Map

| Namespace | Location | Purpose |
|-----------|----------|---------|
| `MOBANet.GameSim.Core` | `Assets/GameSim/Core/` | SimWorld, SimConfig, TickClock, MovementEngine |
| `MOBANet.GameSim.Entities` | `Assets/GameSim/Entities/` | SimEntity, SimPlayer, SimProjectile |
| `MOBANet.GameSim.States` | `Assets/GameSim/States/` | TransformState, StatsState, AbilityState, CombatState |
| `MOBANet.GameSim.Commands` | `Assets/GameSim/Commands/` | SimCommand, CommandDispatcher, ICommandHandler |
| `MOBANet.GameSim.Commands.Handlers` | `Assets/GameSim/Commands/Handlers/` | MovementHandler, AbilityHandler, SystemHandler |
| `MOBANet.GameSim.Events` | `Assets/GameSim/Events/` | SimEvent, SimEventType |
| `MOBANet.GameSim.Data` | `Assets/GameSim/Data/` | AbilityDefinition, CharacterClass, AbilityTargetType |
| `MOBANet.NetAdapter` | `Assets/Scripts/Network/NetAdapter/` | SnapshotHelper, CommandHelper |
| `MOBANet.NetAdapter.FishNet` | `Assets/Scripts/Network/NetAdapter/FishNet/` | FishNetAdapter (only FishNet import) |
| `MOBANet.NetAdapter.Messages` | `Assets/Scripts/Network/NetAdapter/Messages/` | SnapshotDelta, InputPacket, ReliableEvent, GameCommand, MatchConfigBroadcast |
| `MOBANet.NetAdapter.AOI` | `Assets/Scripts/Network/NetAdapter/AOI/` | AOIManager, SpatialHashGrid |
| `MOBANet.NetAdapter.Buffers` | `Assets/Scripts/Network/NetAdapter/Buffers/` | ServerCommandBuffer ~~(ClientSnapshotBuffer exists but is unused dead code from V4.x)~~ |
| `MOBANet.NetAdapter.Metrics` | `Assets/Scripts/Network/NetAdapter/Metrics/` | IPingTracker |
| `MOBANet.Shared` | `Assets/Scripts/Network/Shared/` | NetcodeConstants, CommandTypes, Constants |
| `MOBANet.Server` | `Assets/Scripts/Server/` | ServerGameLoop, ServerLauncher |
| `MOBANet.UnityView.Core` | `Assets/Scripts/Core/` | NetworkClient |
| `MOBANet.UnityView.Entities` | `Assets/Scripts/Client/View/Entities/` | EntityView, PlayerView, ProjectileView |
| `MOBANet.UnityView.Input` | `Assets/Scripts/Client/Input/` | InputCollector, InputBuffer |
| `MOBANet.Client.Input` | `Assets/Scripts/Client/Input/` | IntentBuilder, InputIntent |
| `MOBANet.Client.Animation` | `Assets/Scripts/Client/View/Animation/` | Class controllers (Archer, Mage, ...) |
| `MOBANet.Client.Casting` | `Assets/Scripts/Client/Casting/` | CastController, SkillshotIndicator |
| `MOBANet.Client.Targeting` | `Assets/Scripts/Client/Targeting/` | TargetingSystem, TargetIndicator |
| `MOBANet.Client.HUD` | `Assets/Scripts/Client/View/UI/HUD/` | HealthBarUI, TargetInfoUI |
| `MOBANet.Client.UI` | `Assets/Scripts/Client/View/UI/` | ClassSelectionUI, AbilityBarUI, AbilitySlotUI, HUDManager |
| `MOBANet.Client.Settings` | `Assets/Scripts/Client/View/UI/Settings/` | GameSettings, SettingsManager |
| `MOBANet.GameSim.Types` | `Assets/GameSim/Types/` | Team |
| `MOBANet.GameSim.Interfaces` | `Assets/GameSim/Interfaces/` | IDamageable |
| `MOBANet.Core` | `Assets/Scripts/Client/View/Core/` | DebugLogger, DebugSettings |
| `MOBANet.UnityView.Core` | `Assets/Scripts/Client/View/UI/Bootstrap/` | GameBootstrap |
| *(no namespace)* | `Assets/Scripts/Client/View/Core/` | GameManager (singleton, wires prefabs/references) |
| `MOBANet.Diagnostics` | `Assets/Scripts/Debug/` | MovementDebugger, MovementCycleLogger, NetworkPingMeasure |

---

## Performance Characteristics

### Bandwidth Usage

**Per client upload (120Hz input):**
- Packet header: 14 bytes
- With 3 redundant event commands: ~14 + 3*12 = ~50 bytes
- Rate: ~50 * 120 = 6,000 bytes/sec = **~6 KB/s** (with events)
- Idle (no intent): still sends at 120Hz but smaller packets

**Per client download (60Hz snapshots, 1 entity visible):**
- Header: 14 bytes + 1 entity * 30 bytes = 44 bytes
- Rate: 44 * 60 = 2,640 bytes/sec = **~2.6 KB/s**
- Scales with visible entity count (AOI culling limits this)

### Latency Budget (Localhost)

| Stage | Time | Cumulative |
|-------|------|------------|
| Input capture → socket send | 0ms | 0ms |
| Network → server receive | ~17ms | ~17ms |
| Server process → broadcast | ~16ms | ~33ms |
| Network → client receive | ~17ms | **~50ms** |

Dead-reckoning adds no perceivable delay (extrapolates forward from last snapshot).

---

## Trade-offs

### Why This Architecture?

**Advantages:**
- Anti-cheat: Client can't manipulate position/velocity
- Consistency: What you see happened on server (no rollback artifacts)
- Simplicity: No prediction/reconciliation, no interpolation buffer
- Determinism: Server-only simulation enables replays

**Disadvantages:**
- Perceived latency: ~50ms+ delay for position changes
- High-ping penalty: Visible rubber-banding if ping > 200ms
- No local prediction: Movement feels "heavier" than FPS-style

**Why LoL-style over FPS-style?**

Optimal for:
- Top-down MOBA/action-RPG
- Ability-based combat (not twitch shooting)
- Anti-cheat priority
- Server-side complexity (pathfinding, abilities, RNG)

---

## Next Steps

1. **Dive into components:**
   - [GameSim/01_GameSim_Overview.md](../GameSim/01_GameSim_Overview.md) - Pure simulation
   - [Client/01_Client_Architecture.md](../Client/01_Client_Architecture.md) - Client systems
   - [Server/01_Server_Loop.md](../Server/01_Server_Loop.md) - Server tick loop

---

**Last Updated:** 2026-02-18

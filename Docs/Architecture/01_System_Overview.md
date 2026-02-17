# System Overview

**High-Level Architecture for Ryvax Netcode**

This document provides a bird's-eye view of the complete multiplayer architecture, showing how components interact from input to rendering.

---

## Architecture Diagram

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              CLIENT (Unity)                               │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────┐    ┌─────────────────┐    ┌──────────────────┐      │
│  │ InputCollector │───▶│  IntentBuilder  │───▶│ NetworkClient    │      │
│  │ (WASD/Click)   │    │   (120Hz)       │    │  (120Hz send)    │      │
│  └────────────────┘    └─────────────────┘    └────────┬─────────┘      │
│         │                                               │                │
│         └─► Local Intent (rotation/animation feedback)  │                │
│                                                         │                │
│                                                    ┌────▼─────────┐      │
│                                                    │ FishNetAdapter│     │
│                                                    │  (UDP send)   │     │
│                                                    └────────┬──────┘      │
│                                                             │             │
└─────────────────────────────────────────────────────────────┼─────────────┘
                                                              │
                                    InputPacket (14 bytes)   │
                                    MovementSeq, Intent      │
                                                              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                                 SERVER                                    │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────┐    ┌─────────────────┐    ┌──────────────────┐      │
│  │ FishNetAdapter │───▶│ InputBuffer     │───▶│ ServerGameLoop   │      │
│  │  (UDP recv)    │    │  (queue)        │    │   (60Hz tick)    │      │
│  └────────────────┘    └─────────────────┘    └────────┬─────────┘      │
│                                                         │                │
│                                                    ┌────▼─────────┐      │
│                                                    │   SimWorld   │      │
│                                                    │  (GameSim)   │      │
│                                                    └────────┬──────┘      │
│                                                             │             │
│         ┌───────────────────────────────────────────────────┘             │
│         │                                                                 │
│    ┌────▼─────────┐         ┌──────────────────┐                         │
│    │ AOIManager   │────────▶│ SnapshotBroadcast│                         │
│    │ (culling)    │         │    (60Hz)        │                         │
│    └──────────────┘         └────────┬─────────┘                         │
│                                      │                                   │
└──────────────────────────────────────┼───────────────────────────────────┘
                                       │
                        SnapshotDelta (40+ bytes)
                        ServerTick, Entities[]
                                       ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                          CLIENT (Receive & Render)                        │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌────────────────┐    ┌─────────────────┐    ┌──────────────────┐      │
│  │ NetworkClient  │───▶│  TimeSync       │───▶│BaseInterpolator  │      │
│  │OnSnapshotRecv  │    │ (buffer ~100ms) │    │  (lerp snaps)    │      │
│  └────────────────┘    └─────────────────┘    └────────┬─────────┘      │
│                                                         │                │
│                                                    ┌────▼─────────┐      │
│                                                    │VisualPosition│      │
│                                                    │   Manager    │      │
│                                                    └────────┬──────┘      │
│                                                             │             │
│                                                    visualPos = basePos    │
│                                                          +  offset        │
│                                                             │             │
│                                                    ┌────────▼──────┐      │
│                                                    │  PlayerView   │      │
│                                                    │ transform.pos │      │
│                                                    └───────────────┘      │
│                                                                           │
└──────────────────────────────────────────────────────────────────────────┘
```

---

## Layer Breakdown

### 1. Client Input Layer
**Purpose:** Capture user input and convert to network intents

| Component | File | Responsibility |
|-----------|------|----------------|
| InputCollector | `Assets/Scripts/Client/Input/InputCollector.cs:94` | Reads keyboard/mouse input each frame |
| IntentBuilder | `Assets/Scripts/Client/Input/IntentBuilder.cs` | Converts raw input to intents (MoveDir/MoveTo/Stop) at 120Hz |
| NetworkClient | `Assets/Scripts/Core/NetworkClient.cs:256` | Queues and sends InputPackets |

**Key Flow:**
```
WASD press → InputCollector.HandleWASDInput() → IntentBuilder.OnKeyboardMove()
→ NetworkClient.SendInputIntent() → FishNetAdapter.SendInputPacket()
```

**Local Feedback:**
- Rotation: Immediate turn toward input direction (PlayerView.cs:158-169)
- Animation: Instant "run" animation start (PlayerView.cs:203-214)
- Position: NO local prediction (server-authoritative)

---

### 2. Network Adapter Layer
**Purpose:** Abstract transport details, handle message serialization

| Component | File | Responsibility |
|-----------|------|----------------|
| FishNetAdapter | `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs` | Bridges GameSim ↔ FishNet transport |
| InputPacket | `Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs` | Input message format (14 bytes) |
| SnapshotDelta | `Assets/Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs` | Snapshot message format (40+ bytes) |
| GameCommand | `Assets/Scripts/Network/NetAdapter/Messages/GameCommand.cs` | Event messages (Jump, CastAbility, etc.) |

**Message Formats:**

**InputPacket (14 bytes):**
```
ClientTick (4) | MovementSeq (4) | IntentType (1) | Payload0 (2) | Payload1 (2) | CommandCount (1)
```

**SnapshotDelta (40+ bytes for 1 entity):**
```
ServerTick (4) | AckInputSeq (4) | AckMovementSeq (4) | EntityCount (2) | Entities[] (26+ each)
```

---

### 3. GameSim Layer (Simulation)
**Purpose:** Deterministic, authoritative game simulation (server-side)

| Component | File | Responsibility |
|-----------|------|----------------|
| SimWorld | `Assets/GameSim/Core/SimWorld.cs` | Simulation container, tick management |
| SimPlayer | `Assets/GameSim/Entities/SimPlayer.cs` | Player entity with movement/stats |
| CommandDispatcher | `Assets/GameSim/Commands/CommandDispatcher.cs` | Routes commands to handlers |
| MovementHandler | `Assets/GameSim/Commands/Handlers/MovementHandler.cs:14` | Applies movement intents to entities |
| TransformState | `Assets/GameSim/States/TransformState.cs` | Position/rotation component |
| StatsState | `Assets/GameSim/States/StatsState.cs` | Health/speed/status component |

**Tick Flow:**
```
ServerGameLoop.FixedUpdate() → SimWorld.Tick(deltaTime)
→ CommandDispatcher.ProcessCommands() → MovementHandler.Execute()
→ SimPlayer.Transform.Position updated
```

**Key Properties:**
- **Deterministic:** Same inputs → same outputs (fixed timestep)
- **Minimal Unity coupling:** Uses UnityEngine for Vector3/Mathf only, no MonoBehaviour
- **Server-authoritative:** Only the server runs full simulation

---

### 4. Server Layer
**Purpose:** Network hosting, input buffering, snapshot broadcasting

| Component | File | Responsibility |
|-----------|------|----------------|
| ServerGameLoop | `Assets/Scripts/Server/ServerGameLoop.cs:909` | Main server tick loop (60Hz) |
| AOIManager | `Assets/Scripts/Server/AOI/AOIManager.cs` | Area of Interest culling |
| SpatialHash | `Assets/Scripts/Server/AOI/SpatialHash.cs` | Spatial partitioning for visibility |

**Input Buffering:**
Server buffers incoming inputs and applies them at the **start** of FixedUpdate, before simulation runs. This prevents inputs from arriving mid-tick.

```csharp
// ServerGameLoop.cs:909
void FixedUpdate()
{
    FlushPendingMovementInputs();  // Apply buffered inputs
    _simWorld.Tick(Time.fixedDeltaTime);
    BroadcastSnapshots();
}
```

**AOI System:**
- Culls entities outside player view range
- Reduces bandwidth (only send visible entities)
- Spatial hashing for O(1) visibility queries

---

### 5. Client Sync Layer
**Purpose:** Receive snapshots, interpolate, smooth visual rendering

| Component | File | Responsibility |
|-----------|------|----------------|
| NetworkClient | `Assets/Scripts/Core/NetworkClient.cs:439` | Receives snapshots, manages sync |
| TimeSync | `Assets/Scripts/Client/Timing/TimeSync.cs` | Adaptive buffer (80-160ms) |
| BaseInterpolator | `Assets/Scripts/Client/Interpolation/BaseInterpolator.cs:228` | Interpolates basePos from snapshots |
| VisualOffsetCorrector | `Assets/Scripts/Client/Prediction/VisualOffsetCorrector.cs` | Smooths visual discontinuities |
| VisualPositionManager | `Assets/Scripts/Client/Prediction/VisualPositionManager.cs:158` | Orchestrates basePos + offset |

**Interpolation Flow:**
```
Snapshot arrives → TimeSync.OnSnapshotReceived() → BaseInterpolator.AddSnapshot()
→ Update() → renderTime = serverTime - buffer
→ basePos = Lerp(snapshotA, snapshotB, alpha)
→ visualPos = basePos + visualOffset
```

**Visual Formula:**
```
visualPos = basePos + visualOffset
```
- `basePos`: Interpolated server truth (delayed ~100ms for smoothness)
- `visualOffset`: Absorption offset (corrects jumps, always → 0)
- `visualPos`: Final rendered position

---

### 6. Client View Layer
**Purpose:** Apply synced state to Unity GameObjects

| Component | File | Responsibility |
|-----------|------|----------------|
| PlayerView | `Assets/Scripts/Client/View/PlayerView.cs:128` | Renders player entity |
| EntityView | `Assets/Scripts/Client/View/EntityView.cs` | Base class for all entity views |
| LocalIntentFeedback | (inline in PlayerView) | Immediate rotation/animation feedback |

**Rendering:**
```csharp
// PlayerView.cs:154
transform.position = _networkClient.GetVisualPosition();  // basePos + offset

// Rotation: Immediate local feedback
if (HasLocalMoveIntent())
    transform.rotation = LocalIntentFeedback.GetSmoothedRotationY();
else
    transform.rotation = _networkClient.GetVisualRotationY();  // Server rotation
```

---

## Data Flow Summary

### Input → Server (Client to Server)

```
1. Keyboard press (0ms)
2. InputCollector detects input (0ms)
3. IntentBuilder creates MoveDir intent (0ms, rate-limited to 120Hz)
4. NetworkClient queues packet (0ms)
5. FishNetAdapter sends UDP packet (0ms, double-flush in LateUpdate)
6. Server receives packet (+17ms network + server poll)
7. ServerGameLoop buffers input (0ms)
8. FixedUpdate applies input to SimWorld (+0ms, same tick)
```

**Client-side latency:** 0ms (all in same frame)
**Network latency:** ~17ms (localhost)

### Simulation → Visual (Server to Client)

```
9. Server simulates tick (+16ms, 60Hz tick interval)
10. ServerGameLoop broadcasts snapshot (0ms)
11. Client receives snapshot (+17ms network + client poll)
12. NetworkClient processes snapshot (0ms)
13. TimeSync calculates renderTime (0ms)
14. BaseInterpolator interpolates basePos (0ms)
15. VisualPositionManager adds offset (0ms)
16. PlayerView applies transform.position (0ms)
```

**Server processing:** ~16ms (tick interval)
**Client rendering:** 0ms + ~100ms interpolation buffer (for smoothness)

**Total measured latency (localhost):** ~50ms input → visual position change

See [Network/04_Performance_Metrics.md](../Network/04_Performance_Metrics.md) for detailed breakdown.

---

## Key Design Principles

### 1. Server Authoritative
- Server is single source of truth for all gameplay
- Client sends only **intents**, never state (position/velocity)
- Client corrections are **passive** (smooth toward server state)

### 2. Deterministic Simulation
- Fixed timestep (60Hz)
- Same inputs → same outputs
- No floating-point non-determinism (quantized network messages)

### 3. Visual Smoothing (Not Prediction)
- Client does NOT predict future positions
- Client interpolates between server snapshots (delayed truth)
- Visual offset absorbs basePos jumps for continuity

### 4. Immediate Feedback Where Safe
- **Rotation:** Immediate (can't desync, purely visual)
- **Animation:** Immediate (can't desync, purely visual)
- **Position:** Server-authoritative (can desync, must wait for server)

---

## Configuration Constants

Key constants controlling system behavior:

| Constant | Value | File | Purpose |
|----------|-------|------|---------|
| `TICK_RATE` | 60 Hz | `SimConfig.cs` | Server simulation rate |
| `SNAPSHOT_RATE` | 60 Hz | `NetcodeConstants.cs:50` | Snapshot broadcast rate |
| `INPUT_SEND_RATE` | 120 Hz | `NetworkClient.cs:54` | Client input send rate |
| `INTENT_SEND_INTERVAL` | 8.3ms | `IntentBuilder.cs:37` | Intent rate-limit |
| `ADAPTIVE_BUFFER_TARGET` | 100ms | `NetcodeConstants.cs:381` | Interpolation buffer target |
| `ADAPTIVE_BUFFER_MIN` | 80ms | `NetcodeConstants.cs:382` | Minimum buffer |
| `ADAPTIVE_BUFFER_MAX` | 160ms | `NetcodeConstants.cs:383` | Maximum buffer |
| `PLAYER_SPEED` | 8 u/s | `NetcodeConstants.cs:56` | Default movement speed |

---

## Performance Characteristics

### Bandwidth Usage

**Per client upload (120Hz input):**
- Packet size: 14 bytes (MoveDir)
- Continuous input: 14 × 120 = 1,680 bytes/sec = **1.6 KB/s**
- Actual (with rate-limiting): ~1.5 KB/s

**Per client download (60Hz snapshots, 1 entity visible):**
- Packet size: ~40 bytes (1 entity)
- Rate: 40 × 60 = 2,400 bytes/sec = **2.4 KB/s**
- Scales with visible entity count (AOI culling helps)

**100 player server:**
- Upload: 100 × 1.6 = 160 KB/s
- Download per client: ~2.4 KB/s (AOI-culled)
- Download total: 100 × 2.4 = 240 KB/s

### Latency Budget (Localhost)

| Stage | Time | Cumulative |
|-------|------|------------|
| Input capture → socket send | 0ms | 0ms |
| Network → server receive | 17ms | 17ms |
| Server process → broadcast | 16ms | 33ms |
| Network → client receive | 17ms | **50ms** |

**Note:** Adaptive buffer adds ~100ms perceived delay for visual smoothness (hidden by interpolation).

---

## Trade-offs

### Why This Architecture?

**Advantages:**
- ✅ **Anti-cheat:** Client can't manipulate position/velocity
- ✅ **Consistency:** What you see happened on server (no rollback)
- ✅ **Determinism:** Replays, debugging, server-side verification
- ✅ **Simplicity:** No complex client-side prediction/reconciliation

**Disadvantages:**
- ❌ **Perceived latency:** ~50-100ms delay for position changes
- ❌ **High-ping penalty:** Visible rubber-banding if ping > 200ms
- ❌ **Feels "heavy":** Less responsive than FPS client-prediction

**Why LoL-style over FPS-style?**

This architecture is optimal for:
- Top-down MOBA/RTS games
- Turn-based or ability-based combat (not twitch shooting)
- Anti-cheat priority
- Deterministic replay requirements

See [Architecture/03_Design_Rationale.md](03_Design_Rationale.md) for detailed comparison.

---

## Next Steps

1. **Understand the philosophy:** Read [03_Design_Rationale.md](03_Design_Rationale.md)
2. **Learn LoL-style netcode:** Read [02_LoL_Style_Netcode.md](02_LoL_Style_Netcode.md)
3. **Dive into components:**
   - [GameSim/GameSim_Overview.md](../GameSim/GameSim_Overview.md) - Pure simulation
   - [Network/01_Network_Architecture.md](../Network/01_Network_Architecture.md) - Message flow
   - [Client/Client_Architecture.md](../Client/Client_Architecture.md) - Sync system
   - [Server/Server_Loop.md](../Server/Server_Loop.md) - Server tick loop

---

**Last Updated:** 2026-02-03

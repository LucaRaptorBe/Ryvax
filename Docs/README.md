# Ryvax Netcode Documentation

**Authoritative Server Multiplayer for Unity - LoL-Style Architecture**

This documentation covers the complete network architecture for Ryvax, implementing League of Legends-style authoritative server netcode with client-side visual smoothing.

## Quick Start

**For new developers:**
1. Start here → Read [Architecture/01_System_Overview.md](Architecture/01_System_Overview.md) for high-level understanding
2. Dive into specific components → Navigate folders below

**Current Performance:** ~50ms input-to-visual latency (localhost, 120Hz input rate)

---

## Documentation Structure

### 📐 Architecture
High-level system design and architectural decisions.

- **[01_System_Overview.md](Architecture/01_System_Overview.md)** - Component diagram, namespaces, data flow, design rationale

### 🎮 GameSim
Simulation layer (authoritative, deterministic, depends on UnityEngine for Vector3/Mathf).

- **[01_GameSim_Overview.md](GameSim/01_GameSim_Overview.md)** - SimWorld, tick system, determinism
- **[02_Command_System.md](GameSim/02_Command_System.md)** - CommandDispatcher, handlers, command flow
- **[03_Entity_Model.md](GameSim/03_Entity_Model.md)** - SimPlayer, SimEntity, state components

### 🌐 Network
FishNet adapter layer, message formats, and data flow.

- **[01_Network_Architecture.md](Network/01_Network_Architecture.md)** - FishNet adapter pattern, polling
- **[02_Message_Specifications.md](Network/02_Message_Specifications.md)** - InputPacket, SnapshotDelta formats
- **[03_Data_Flow.md](Network/03_Data_Flow.md)** - Complete input→visual flow with file references
- **[04_Performance_Metrics.md](Network/04_Performance_Metrics.md)** - Current 50ms latency achievement

### 🖥️ Server
Server-side game loop and optimization systems.

- **[01_Server_Loop.md](Server/01_Server_Loop.md)** - ServerGameLoop, tick rate, input buffering
- **[02_AOI_System.md](Server/02_AOI_System.md)** - Area of Interest, visibility culling, bandwidth optimization

### 💻 Client
Client-side architecture and input handling.

- **[01_Client_Architecture.md](Client/01_Client_Architecture.md)** - NetworkClient, sync, visual smoothing
- **[02_Input_System.md](Client/02_Input_System.md)** - InputCollector, IntentBuilder, input flow

### 🔧 Development
Debugging, testing, and instrumentation guides.

- **[01_Debugging_Guide.md](Development/01_Debugging_Guide.md)** - How to debug netcode issues
- **[02_Instrumentation.md](Development/02_Instrumentation.md)** - Log formats, sequence correlation
- **[03_Testing_Checklist.md](Development/03_Testing_Checklist.md)** - Manual testing procedures

---

## Key Concepts

### Authoritative Server
- Server is single source of truth
- Client sends only **intents** (MoveTo, Stop, Follow), never position/velocity
- All simulation (collisions, combat, RNG) happens server-side

### LoL-Style Netcode (V5.0)
- **No client-side prediction** of position (unlike FPS games)
- **Dead-reckoning** from server snapshots (not interpolation buffer)
- **Snap smoothing** via `Vector3.Lerp` toward dead-reckoned position
- Rotation and animation derived from server velocity (no local intent feedback)

### Visual Formula
```
deadReckonedPos = lastServerPos + lastServerVel * timeSinceSnapshot
visualPos = Lerp(visualPos, deadReckonedPos, VISUAL_SMOOTHING_SPEED * dt)
```
- `deadReckonedPos`: Extrapolated from last server snapshot
- `visualPos`: Smoothly approaches dead-reckoned position (snaps directly when stopped)
- `VISUAL_SMOOTHING_SPEED = 18` (`NetcodeConstants.cs:92`)

---

## File References

### Critical Paths

**GameSim Layer:**
```
Assets/GameSim/Core/SimWorld.cs                    - Simulation container
Assets/GameSim/Entities/SimPlayer.cs               - Player entity
Assets/GameSim/Commands/CommandDispatcher.cs       - Command routing
Assets/GameSim/Commands/Handlers/MovementHandler.cs - Movement execution
Assets/GameSim/States/TransformState.cs            - Position/rotation state
Assets/GameSim/States/StatsState.cs                - Health/speed state
```

**Network Layer:**
```
Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs - FishNet integration
Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs   - Input format (14 bytes)
Assets/Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs - Snapshot format
Assets/Scripts/Network/Shared/NetcodeConstants.cs           - Network constants
```

**Server:**
```
Assets/Scripts/Server/ServerGameLoop.cs            - Authoritative simulation loop
Assets/Scripts/Server/ServerLauncher.cs            - Server initialization
Assets/Scripts/Network/NetAdapter/AOI/AOIManager.cs - Visibility culling
```

**Client:**
```
Assets/Scripts/Core/NetworkClient.cs               - Client sync, snapshot dispatch
Assets/Scripts/Client/Input/InputCollector.cs      - Input capture
Assets/Scripts/Client/Input/IntentBuilder.cs       - Intent construction (120Hz)
Assets/Scripts/Client/View/Entities/EntityView.cs  - Dead-reckoning rendering
Assets/Scripts/Client/View/Entities/PlayerView.cs  - Player-specific rendering
```

---

## Current Configuration

| Parameter | Value | File |
|-----------|-------|------|
| Server tick rate | 60Hz (16.67ms) | NetcodeConstants.cs:23 |
| Snapshot rate | 60Hz (= TICK_RATE) | NetcodeConstants.cs:29 |
| Input send rate | 120Hz (8.3ms) | NetworkClient.cs:43 |
| Intent rate-limit | 120Hz (8.3ms) | IntentBuilder.cs:37 |
| Player speed | 8 u/s | NetcodeConstants.cs:34 |
| Visual smoothing | 18 | NetcodeConstants.cs:92 |
| Input→visual latency | ~50ms (localhost) | See Network/04_Performance_Metrics.md |

---

## Version History

- **V5.0 (Current):** Pure LoL-style, removed prediction/reconciliation, 50ms latency
- **V4.0:** Local intent feedback (rotation/animation immediate)
- **V3.0:** Intent-based input (MoveDir/MoveTo/Stop)
- **V2.x:** Visual offset correction system
- **V1.0:** Initial authoritative server implementation

---

## Related Documentation

- [TROUBLESHOOTING.md](/TROUBLESHOOTING.md) - Common issues and solutions
- [claude.md](/claude.md) - Claude AI assistant rules for this project

---

**Last Updated:** 2026-02-18
**Maintained By:** Ryvax Development Team

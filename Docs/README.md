# Ryvax Netcode Documentation

**Authoritative Server Multiplayer for Unity - LoL-Style Architecture**

This documentation covers the complete network architecture for Ryvax, implementing League of Legends-style authoritative server netcode with client-side visual smoothing.

## Quick Start

**For new developers:**
1. Start here → Read [Architecture/01_System_Overview.md](Architecture/01_System_Overview.md) for high-level understanding
2. Understand the philosophy → [Architecture/03_Design_Rationale.md](Architecture/03_Design_Rationale.md)
3. Dive into specific components → Navigate folders below

**Current Performance:** ~50ms input-to-visual latency (localhost, 120Hz input rate)

---

## Documentation Structure

### 📐 Architecture
High-level system design and architectural decisions.

- **[01_System_Overview.md](Architecture/01_System_Overview.md)** - Component diagram and file references
- **[02_LoL_Style_Netcode.md](Architecture/02_LoL_Style_Netcode.md)** - Complete LoL-style implementation guide
- **[03_Design_Rationale.md](Architecture/03_Design_Rationale.md)** - Why this architecture? (vs FPS-style)

### 🎮 GameSim
Simulation layer (authoritative, deterministic, depends on UnityEngine for Vector3/Mathf).

- **[GameSim_Overview.md](GameSim/GameSim_Overview.md)** - SimWorld, tick system, determinism
- **[Command_System.md](GameSim/Command_System.md)** - CommandDispatcher, handlers, command flow
- **[Entity_Model.md](GameSim/Entity_Model.md)** - SimPlayer, SimEntity, state components

### 🌐 Network
FishNet adapter layer, message formats, and data flow.

- **[01_Network_Architecture.md](Network/01_Network_Architecture.md)** - FishNet adapter pattern, polling
- **[02_Message_Specifications.md](Network/02_Message_Specifications.md)** - InputPacket, SnapshotDelta formats
- **[03_Data_Flow.md](Network/03_Data_Flow.md)** - Complete input→visual flow with file references
- **[04_Performance_Metrics.md](Network/04_Performance_Metrics.md)** - Current 50ms latency achievement

### 🖥️ Server
Server-side game loop and optimization systems.

- **[Server_Loop.md](Server/Server_Loop.md)** - ServerGameLoop, tick rate, input buffering
- **[AOI_System.md](Server/AOI_System.md)** - Area of Interest, visibility culling, bandwidth optimization

### 💻 Client
Client-side architecture and input handling.

- **[Client_Architecture.md](Client/Client_Architecture.md)** - NetworkClient, sync, visual smoothing
- **[Input_System.md](Client/Input_System.md)** - InputCollector, IntentBuilder, input flow

### 🔧 Development
Debugging, testing, and instrumentation guides.

- **[Debugging_Guide.md](Development/Debugging_Guide.md)** - How to debug netcode issues
- **[Instrumentation.md](Development/Instrumentation.md)** - Log formats, sequence correlation
- **[Testing_Checklist.md](Development/Testing_Checklist.md)** - Manual testing procedures

### 📦 Archive
Historical debugging documents (for reference only).

Contains investigative docs from latency optimization work. See [Archive/README.md](Archive/README.md) for details:
- FLUSH_DELAY_INSTRUMENTATION.md
- TUGBOAT_POLL_CHAIN.md
- FIX_INCOMING_PROCESSING.md
- FIX_CLIENT_OUTGOING.md
- FIX_LATEUPDATE_FLUSH.md
- CLIENT_OUTGOING_CHAIN.md
- NETCODE_DIAGRAM.md
- ENHANCED_SOCKET_LOGS.md
- LOL_STYLE_NETCODE.md (V2.3)
- LOL.md
- InputCommandSpec.md
- FLUX_DIAGRAM_VISUEL.md
- NETCODE_FLOW_AS_IS.md
- NETCODE_INPUT_LATENCY.md

---

## Key Concepts

### Authoritative Server
- Server is single source of truth
- Client sends only **intents** (MoveTo, Stop, Follow), never position/velocity
- All simulation (collisions, combat, RNG) happens server-side

### LoL-Style Netcode
- **No client-side prediction** of position (unlike FPS games)
- **Interpolation-based** rendering from server snapshots
- **Visual smoothing** via offset correction (not rollback/replay)
- **Immediate feedback** via rotation/animation (local intent)

### Visual Formula
```
visualPos = basePos + visualOffset
```
- `basePos`: Interpolated from server snapshots (delayed truth)
- `visualOffset`: Absorption offset for visual continuity (corrects toward 0)
- `visualPos`: Final rendered position

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
Assets/Scripts/Server/ServerGameLoop.cs:909    - Authoritative simulation
Assets/Scripts/Server/AOI/AOIManager.cs        - Visibility culling
```

**Client:**
```
Assets/Scripts/Core/NetworkClient.cs:482           - Client sync
Assets/Scripts/Client/Input/InputCollector.cs      - Input capture
Assets/Scripts/Client/Input/IntentBuilder.cs       - Intent construction (120Hz)
Assets/Scripts/Client/View/PlayerView.cs           - Entity rendering
```

---

## Current Configuration

| Parameter | Value | File |
|-----------|-------|------|
| Server tick rate | 60Hz (16.67ms) | SimConfig.cs |
| Snapshot rate | 60Hz | NetcodeConstants.cs:50 |
| Input send rate | 120Hz (8.3ms) | NetworkClient.cs:54 |
| Intent rate-limit | 120Hz (8.3ms) | IntentBuilder.cs:37 |
| Player speed | 8 u/s | NetcodeConstants.cs:56 |
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

**Last Updated:** 2026-02-03
**Maintained By:** Ryvax Development Team

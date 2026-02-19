# Network Architecture

> **Status:** Production
> **Version:** 5.0 (Pure LoL-style)
> **Last Updated:** 2026-02-18

## Overview

The Ryvax network layer implements a **server-authoritative** architecture using the **FishNet adapter pattern**. All game state resides on the server, with clients receiving snapshots for interpolation-based rendering.

**Architecture Pattern:** Adapter-based abstraction
**Transport:** UDP via FishNet Tugboat
**Style:** League of Legends (no client-side prediction)
**Tick Rate:** 60Hz server, 120Hz client input

---

## FishNet Adapter Pattern

### Design Philosophy

The network layer is **library-agnostic** through the `INetAdapter` interface. FishNet is used as the underlying library, but only `FishNetAdapter.cs` contains FishNet-specific code.

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                        │
│  (NetworkClient.cs, ServerGameLoop.cs)                      │
├─────────────────────────────────────────────────────────────┤
│                   INetAdapter Interface                     │
│  • SendToServer(), SendToClient(), SendToAll()              │
│  • RegisterHandler(), Events (OnClientConnected, etc.)      │
├─────────────────────────────────────────────────────────────┤
│                    FishNetAdapter.cs                        │
│  • ONLY file that imports FishNet namespaces                │
│  • Translates INetAdapter calls to FishNet broadcasts       │
│  • Handles connection lifecycle, message routing            │
├─────────────────────────────────────────────────────────────┤
│                     FishNet Library                         │
│  • NetworkManager, TransportManager                         │
│  • Tugboat UDP Transport                                    │
└─────────────────────────────────────────────────────────────┘
```

**Migration Path:** To switch to another library (Mirror, Netcode for GameObjects), only replace `FishNetAdapter.cs`.

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs`

---

## Transport Layer

### UDP via FishNet Tugboat

Ryvax uses **unreliable UDP** for all time-sensitive data (inputs, snapshots) to avoid TCP head-of-line blocking.

**Transport:** Tugboat (LiteNetLib wrapper)
**Protocol:** UDP with optional reliability per message
**Default Port:** 7777

#### Channel Selection

| Message Type | Channel | Reliability | Why |
|--------------|---------|-------------|-----|
| `InputPacket` | Unreliable | No | Redundancy handles loss, TCP blocking bad for inputs |
| `SnapshotDelta` | Unreliable | No | 60Hz rate makes packet loss acceptable |
| `ReliableEvent` | Reliable | Yes | Critical events (spawn, death) need guaranteed delivery |
| `MatchConfig` | Reliable | Yes | One-time setup must arrive |

**Key Implementation:** `FishNetAdapter.cs:221-322`

```csharp
// Unreliable for inputs (avoid head-of-line blocking)
_networkManager.ClientManager.Broadcast(broadcast, Channel.Unreliable);

// Reliable for critical events (targeted to specific client)
_networkManager.ServerManager.Broadcast(conn, broadcast, false, Channel.Reliable);
```

---

## Double-Flush Mechanism

### Problem: FishNet Packet Batching

FishNet batches packets in a `PacketBundle` and only sends them during `IterateOutgoing()` calls, which default to network tick rate (~30Hz = 33ms delay).

For **low-latency inputs**, we need to flush packets **immediately** within the same frame.

### Solution: Manual Polling

**File:** `FishNetAdapter.cs:593-630`

```csharp
public void ForceIterateOutgoing()
{
    // Flush server outgoing (to clients)
    if (_networkManager.IsServerStarted)
    {
        // STEP 1: Flush PacketBundle → Transport
        _networkManager.TransportManager.IterateOutgoing(asServer: true);
        // STEP 2: Flush Transport → Socket
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: true);
    }

    // Flush client outgoing (to server)
    if (_networkManager.IsClientStarted)
    {
        _networkManager.TransportManager.IterateOutgoing(asServer: false);
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
    }
}
```

**Why Both Steps?**

1. `TransportManager.IterateOutgoing()` - Moves packets from internal bundle to transport
2. `Transport.IterateOutgoing()` - Moves packets from transport to OS socket

Without both calls, packets wait for the next tick cycle (+16-33ms latency).

**Caller:** `NetworkClient.cs` calls `ForceIterateOutgoing()` in `LateUpdate()` after sending inputs.

**Result:** Input packets are sent in the **same frame** they're created (0ms client-side delay).

**Performance Impact:** Negligible - only flushes when packets are actually queued.

---

## Message Types

### Core Network Messages

All messages implement `INetMessage` interface with versioning.

| Message | ID | Version | Direction | Channel | Purpose |
|---------|----|---------|-----------|---------|-|
| `InputPacket` | 10 | 4 | C→S | Unreliable | Player inputs with intent + events |
| `SnapshotDelta` | 2 | 1 | S→C | Unreliable | World state (entities, physics) |
| `GameCommand` | 4 | 1 | C→S | Unreliable | Discrete events (jump, cast) |
| `ReliableEvent` | 3 | 1 | S→C | Reliable | Critical events (spawn, death) |
| `MatchConfig` | — | — | S→C | Reliable | Match initialization (direct IBroadcast, not INetMessage) |

**Files:**
- `Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs`
- `Assets/Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs`
- `Assets/Scripts/Network/NetAdapter/Messages/GameCommand.cs`
- `Assets/Scripts/Network/NetAdapter/Messages/ReliableEvent.cs`
- `Assets/Scripts/Network/NetAdapter/Messages/MatchConfigBroadcast.cs`

---

## Polling System

### Manual Network Polling

By default, FishNet polls incoming/outgoing data during `TimeManager.OnTick()` (~30Hz). This creates **phase misalignment** between Unity's `Update()/FixedUpdate()` and network data arrival.

**Problem:** Inputs arriving DURING `FixedUpdate()` but AFTER simulation ticks miss the current frame.

### Solution: Manual Polling at Fixed Points

**File:** `FishNetAdapter.cs:571-585`

```csharp
public void ForceIterateIncoming()
{
    // Poll server incoming (from clients)
    if (_networkManager.IsServerStarted)
        _networkManager.TransportManager.Transport.IterateIncoming(asServer: true);

    // Poll client incoming (from server)
    if (_networkManager.IsClientStarted)
        _networkManager.TransportManager.Transport.IterateIncoming(asServer: false);
}
```

**Usage:**

- **Server:** Called in **`Update()`** (every frame, 60-120 FPS) to receive inputs as early as possible, decoupled from simulation tick rate. See `ServerGameLoop.cs:237-243`.
- **Client:** Not called explicitly — client relies on FishNet's default incoming polling. Client only calls `ForceIterateOutgoing()` in `LateUpdate()`.

**Benefit:** Server receives inputs at frame rate rather than tick rate, reducing input latency.

---

## Bandwidth Optimization

### Input Redundancy vs Bandwidth

**Challenge:** UDP packet loss requires redundancy, but sending full history is wasteful.

**Solution:** Rolling window of recent commands in `InputPacket`.

**Configuration:** `NetcodeConstants.cs`

```csharp
public const int INPUT_REDUNDANCY_COUNT = 3;  // Last 3 commands
public const int INPUT_BUFFER_SIZE = 8;       // Max history size
```

**How It Works:**

1. Client maintains ring buffer of last 8 commands
2. Each `InputPacket` includes last 3 commands
3. Server tracks last processed sequence per client
4. Duplicate sequences are skipped (dedup)

**Packet Loss Tolerance:** Can lose 2 consecutive packets before data loss.

**Bandwidth Cost:**

```
Base intent packet: 14 bytes (no event commands)
+ event commands (if any): 3 redundant × 14 bytes = 42 bytes
Max packet: 14 + 42 = 56 bytes (only when events are queued)
Typical (movement only): 14 bytes × 120/sec = ~1.7 KB/s upload
Worst case (continuous events): 56 bytes × 120/sec = ~6.7 KB/s upload
```

For 10 clients (typical): ~17 KB/s server incoming (negligible for modern servers).

### Snapshot Compression

**Quantization:** 16-bit for position, velocity (see Message Specifications doc)

**Snapshot Size Calculation:**

```
Header: 14 bytes (ServerTick + AckInputSeq + AckMovementSeq + EntityCount)
+ 10 entities × 30 bytes = 314 bytes per snapshot
× 60 snapshots/sec = ~18.8 KB/s download per client
```

EntityState (30 bytes): EntityId(4) + EntityType(1) + Flags(1) + Pos XYZ(6) + RotY(2) + Health(2) + State(1) + Vel XYZ(6) + SpeedQ(2) + EventFlags(1) + Cd0-Cd3(4)

For 10 clients: ~188 KB/s server outgoing.

**Future Optimization:** Delta compression (send only changed entities).

---

## Version Compatibility

### Protocol Versioning

Each message has a `MSG_VERSION` constant. Mismatched versions are rejected.

**Example:** `InputPacket.cs:50`

```csharp
public const ushort MSG_VERSION = 4;  // v4: Intent-based with payloads
```

**Server Validation:** `FishNetAdapter.cs:448-452`

```csharp
if (packet.Version < InputPacket.MSG_VERSION)
{
    Debug.LogWarning($"Rejected InputPacket v{packet.Version} (expected v{InputPacket.MSG_VERSION})");
    return;
}
```

> **Note (currently inert):** `InputPacket.Version` is a read-only property that always returns the compile-time constant `MSG_VERSION` (see `InputPacket.cs:53`: `public ushort Version => MSG_VERSION`). Because sender and receiver share the same binary, `packet.Version < InputPacket.MSG_VERSION` is always `false` and the guard never triggers. The check becomes meaningful only when a client running an older build connects to a server built with a newer version — a scenario that does not occur in the current single-binary setup. When client and server are shipped as separate binaries this check must be revisited.

**Policy:** Clean break - no legacy path for old versions.

**Rationale:** In development, version mismatches indicate stale clients. Better to fail fast than support broken protocols.

---

## Connection Lifecycle

### Server

```
StartServer(port)
    ↓
NetworkManager.ServerManager.StartConnection()
    ↓
OnServerConnectionState(Started)
    ↓
OnServerStarted event → ServerGameLoop.OnServerStarted()
    ↓
[Server Running - accepting clients]
```

### Client

```
StartClient(address, port)
    ↓
NetworkManager.ClientManager.StartConnection()
    ↓
OnClientConnectionState(Started)
    ↓
OnClientStarted event → NetworkClient.OnClientStarted()
    ↓
[Client Connected - sending inputs, receiving snapshots]
```

### Remote Client Connection (Server-Side)

```
Remote client connects
    ↓
OnRemoteConnectionState(Started)
    ↓
Initialize sequence tracking: _lastProcessedSeq[clientId] = 0
    ↓
OnClientConnected(clientId) event → ServerGameLoop.OnClientConnected()
    ↓
ServerGameLoop sends MatchConfig to client
    ↓
ServerGameLoop spawns player entity for clientId
```

**File:** `FishNetAdapter.cs:365-419`

---

## Network Roles

The adapter supports three roles:

| Role | Description | Use Case |
|------|-------------|----------|
| `Server` | Dedicated server, no local client | Production multiplayer |
| `Client` | Remote client only | Players connecting to server |
| `Host` | Server + local client in same process | Singleplayer, LAN host |

**Host Mode Complexity:** `LocalClientId` requires special handling (client isn't in server's client list).

**File:** `FishNetAdapter.cs:41-61, 188-198`

---

## Adapter Interface

### INetAdapter Contract

**File:** `Assets/Scripts/Network/NetAdapter/Interfaces/INetAdapter.cs`

```csharp
public interface INetAdapter
{
    // State properties
    NetworkRole Role { get; }
    bool IsConnected { get; }
    int LocalClientId { get; }

    // Lifecycle
    void StartServer(ushort port);
    void StartClient(string address, ushort port);
    void StartHost(ushort port);
    void Shutdown();

    // Messaging
    void SendToServer<T>(T message, bool reliable) where T : struct, INetMessage;
    void SendToClient<T>(int clientId, T message, bool reliable) where T : struct, INetMessage;
    void SendToAll<T>(T message, bool reliable) where T : struct, INetMessage;
    void SendMatchConfigToClient(int clientId, Vector3[] team1Spawns, Vector3[] team2Spawns);

    // Handlers
    void RegisterHandler<T>(Action<int, T> handler) where T : struct, INetMessage;
    void UnregisterHandler<T>() where T : struct, INetMessage;

    // Events
    event Action<int> OnClientConnected;
    event Action<int> OnClientDisconnected;
    event Action OnServerStarted;
    event Action OnClientStarted;
    event Action OnDisconnected;
}
```

**Note:** `ForceIterateIncoming()`, `ForceIterateOutgoing()`, `SendInputPacket()`, and intent-specific events (`OnMoveDirReceived`, `OnMoveToReceived`, `OnStopReceived`, `OnFollowReceived`) are **FishNetAdapter-specific** methods, not part of the `INetAdapter` interface. Application code (`NetworkClient`, `ServerGameLoop`) uses the concrete `FishNetAdapter` type to access these.

---

## Broadcast Wrappers

FishNet requires broadcast types to implement `IBroadcast`. The adapter wraps messages:

**File:** `FishNetAdapter.cs:672-707`

```csharp
public struct SnapshotBroadcast : IBroadcast
{
    public SnapshotDelta Snapshot;
}

public struct InputPacketBroadcast : IBroadcast
{
    public InputPacket Packet;
}

// etc.
```

These wrappers are **internal to FishNetAdapter** - application code works with raw messages.

---

## Input Deduplication

### Problem: UDP Redundancy → Duplicate Processing

`InputPacket` includes the last 3 commands for reliability. Without deduplication, server processes the same command multiple times.

### Solution: Sequence Tracking

**File:** `FishNetAdapter.cs:432, 484-517`

```csharp
private readonly Dictionary<int, uint> _lastProcessedSeq = new();

// In HandleServerReceiveInputPacket:
for (int i = 0; i < packet.Commands.Length; i++)
{
    var cmd = packet.Commands[i];

    // Skip if already processed
    if (cmd.Sequence <= _lastProcessedSeq[clientId])
        continue;

    // Process command...
    _lastProcessedSeq[clientId] = cmd.Sequence;
}
```

**Scope:** Only applies to **discrete event commands** (jump, spell). Movement intents use last-input-wins (see Data Flow doc).

---

## Timestamp Capture

### Monotonic Timestamps for Latency Tracking

To measure **true latency** (not wall-clock time affected by system clock adjustments), we use `Stopwatch.GetTimestamp()`.

**File:** `FishNetAdapter.cs:445`

```csharp
private void HandleServerReceiveInputPacket(...)
{
    // Capture monotonic timestamp at earliest possible point (socket receive)
    long recvSocketTicks = System.Diagnostics.Stopwatch.GetTimestamp();

    // Pass to handlers for latency metrics
    OnMoveDirReceived?.Invoke(clientId, packet.MovementSeq, direction, recvSocketTicks);
}
```

**Why Monotonic?**

- `DateTime.Now` can jump forward/backward (NTP, DST)
- `Time.time` is frame-based, not precise enough
- `Stopwatch` uses OS high-precision timer (QueryPerformanceCounter on Windows)

**Conversion to Milliseconds:**

```csharp
double ms = (ticks2 - ticks1) * 1000.0 / Stopwatch.Frequency;
```

**Usage:** Server calculates intra-server delay (`serverRecv - serverApply`) for performance metrics.

---

## Configuration

### Runtime Configuration

**File:** `Assets/Scripts/Network/Shared/NetcodeConstants.cs`

```csharp
public const int TICK_RATE = 60;              // Server simulation rate
public const int SNAPSHOT_RATE = 60;          // Snapshot broadcast rate
public const float PLAYER_SPEED = 8f;         // Movement speed (units/sec)

public const int INPUT_REDUNDANCY_COUNT = 3;  // Commands per packet
public const int INPUT_BUFFER_SIZE = 8;       // Max command history
```

**Derived Constants:** Tick delta, snapshot interval calculated automatically.

### FishNet Inspector Settings

**Component:** `NetworkManager` in Bootstrap scene

| Setting | Value | Purpose |
|---------|-------|---------|
| TimeManager Tick Rate | 60 | Match `TICK_RATE` (though manual polling bypasses this) |
| Transport | Tugboat | UDP transport |
| Server Port | 7777 | Default port |
| Client Address | 127.0.0.1 | Localhost for testing |

**Note:** Even though manual polling is used, TimeManager settings should match for consistency.

---

## Performance Characteristics

### Measured Latency (Localhost)

**Test:** Key press to snapshot receive containing input result.

| Stage | Cumulative Time | Delta |
|-------|----------------|-------|
| INPUT START | 0ms | - |
| INTENT (rate-limited 120Hz) | 0ms | 0ms |
| SEND ENQUEUE | 0ms | 0ms |
| SOCKET SEND (double-flush) | 0ms | 0ms |
| SERVER RECV | +17ms | 17ms |
| SERVER APPLY | +17ms | 0ms |
| SERVER BROADCAST | +33ms | 16ms |
| SNAPSHOT RECV | **+50ms** | 17ms |

**Total:** ~50ms input-to-visual latency (localhost, 60Hz server).

**Breakdown:**
- Client-side: 0ms (fully optimized)
- Server poll delay: 17ms (room for improvement)
- Server tick processing: 16ms (inherent at 60Hz)
- Network return: 17ms (inherent latency)

**See:** `/Docs/Network/04_Performance_Metrics.md` for full analysis.

### Bandwidth Usage

**Per Client (Continuous Input):**

- **Upload (movement only):** ~1.7 KB/s (120 packets/sec × 14 bytes)
- **Upload (with events):** ~6.7 KB/s (120 packets/sec × 56 bytes max)
- **Download:** ~18.8 KB/s (60 snapshots/sec × 314 bytes for 10 entities)

**Server Total (10 Clients, typical):**

- **Incoming:** ~17 KB/s (movement only) to ~67 KB/s (worst case)
- **Outgoing:** ~188 KB/s

**Negligible for modern connections.** Typical broadband can handle 100+ clients.

---

## Debugging Tools

### Enable Detailed Logging

Logs are active in `UNITY_EDITOR` and `DEVELOPMENT_BUILD` (see code comments for conditional compilation).

**Key Log Points:**

1. **Input Send:** `[SEND ENQUEUE]` - Client queues packet
2. **Socket Send:** `[SOCKET SEND]` - Packet flushed to socket
3. **Server Receive:** `[SERVER RECV]` - Server receives packet
4. **Server Apply:** `[SERVER APPLY]` - Input applied to simulation
5. **Snapshot Broadcast:** `[SNAPSHOT]` - Snapshot sent

**Example Log Sequence:**

```
[1.283] [INPUT START] dir=(1.00, 0.00) speed=8.0
[1.283] [INTENT] +0ms MoveDir seq=1 dir=(1.00,0.00)
[1.283] [SEND ENQUEUE] C→S seq=1 intent=MoveDir
[1.283] [SOCKET SEND] C→S size=18
[1.300] [SERVER RECV] MoveDir seq=1 dir=(1.00,0.00)
[1.300] [SERVER APPLY] MoveDir seq=1 intraServerMs=17.32
[1.333] [SNAPSHOT] +50ms tick=22 vel=(8.00, -2.00, 0.00)
```

**File:** Search for `Debug.Log` in `FishNetAdapter.cs`, `NetworkClient.cs`.

---

## Known Limitations

### FishNet-Specific Constraints

1. **Broadcast Overhead:** Each message requires a wrapper struct implementing `IBroadcast`.
2. **No Direct Socket Access:** Cannot implement custom reliability/ordering per message.
3. **Manual Polling Required:** Default polling is tied to tick rate, requires workaround.

### General Network Limitations

1. **No Delta Compression:** Snapshots send full entity states (future optimization).
2. **No Lag Compensation:** Server doesn't rewind for hit detection (acceptable for MOBA).

> **Note:** AOI (Area of Interest) filtering is now implemented via `AOIManager` in `ServerGameLoop`. Each client receives only entities within their vision radius. See `ServerGameLoop.cs:52-53, 394-424`.

---

## Migration Considerations

### Switching to Another Library

To replace FishNet:

1. **Keep:** All message types (`InputPacket`, `SnapshotDelta`, etc.)
2. **Keep:** `INetAdapter` interface
3. **Replace:** Only `FishNetAdapter.cs`
4. **Implement:** New adapter wrapping the target library

**Example for Mirror:**

```csharp
public class MirrorAdapter : MonoBehaviour, INetAdapter
{
    // Translate INetAdapter methods to Mirror's NetworkClient/NetworkServer
    public void SendToServer<T>(T message, bool reliable)
    {
        NetworkClient.Send(message, reliable ? Channel.Reliable : Channel.Unreliable);
    }
    // etc.
}
```

**Effort:** ~1-2 days for experienced developer.

---

## References

### Related Documentation

- [Message Specifications](/Docs/Network/02_Message_Specifications.md) - Packet formats, quantization
- [Data Flow](/Docs/Network/03_Data_Flow.md) - Input-to-visual pipeline
- [Performance Metrics](/Docs/Network/04_Performance_Metrics.md) - Latency breakdown, optimization

### External Resources

- [FishNet Documentation](https://fish-networking.gitbook.io/docs/)
- [Overwatch GDC 2017](https://www.youtube.com/watch?v=W3aieHjyNvw) - Gameplay architecture
- [Valorant Netcode](https://technology.riotgames.com/news/valorants-128-tick-servers) - High-rate servers

### Key Files

```
Assets/Scripts/Network/NetAdapter/
├── FishNet/
│   └── FishNetAdapter.cs          # FishNet implementation (ONLY FishNet code)
├── Interfaces/
│   └── INetAdapter.cs             # Core adapter interface
├── Messages/
│   ├── InputPacket.cs             # Client input messages
│   ├── SnapshotDelta.cs           # Server state snapshots
│   ├── GameCommand.cs             # Discrete event commands
│   ├── ReliableEvent.cs           # Critical server→client events
│   └── MatchConfigBroadcast.cs    # Match initialization broadcast
├── AOI/
│   └── AOIManager.cs              # Area of Interest filtering
├── Buffers/
│   └── ServerCommandBuffer.cs     # Server-side command acknowledgment
├── CommandHelper.cs               # Network ↔ Sim conversion
└── SnapshotHelper.cs              # Snapshot creation/application

Assets/Scripts/Network/Shared/
├── NetcodeConstants.cs            # Configuration constants
└── CommandTypes.cs                # Shared enums
```

---

#rules-verified

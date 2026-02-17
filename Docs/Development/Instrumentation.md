# Instrumentation

**Enhanced Logging and Correlation for Netcode Debugging**

This document describes the instrumented logging system used to trace network packets from client send through server processing and back to client rendering.

---

## Overview

The instrumentation system adds detailed logging at every network layer, allowing perfect correlation of packets via sequence numbers and timestamps.

**Key Features:**
- **Direction tracking:** Clear labels for C→S (client to server) and S→C (server to client)
- **Sequence correlation:** Track individual packets through their entire lifecycle
- **Timestamp precision:** High-resolution Stopwatch ticks for microsecond-level timing
- **Payload preview:** Hex dump of packet contents for verification
- **Delivery method:** Reliable vs Unreliable transport indication

---

## Log Format

### Direction Labels

| Label | Meaning | Usage |
|-------|---------|-------|
| `C→S` | Client → Server | Input packets (MoveDir, MoveTo, Stop) |
| `S→C{id}` | Server → Specific Client | Unicast snapshots/events |
| `S→ALL` | Server → All Clients | Broadcast snapshots |

### Delivery Method

| Label | Transport | Typical Use |
|-------|-----------|-------------|
| `Reliable` | DeliveryMethod.ReliableOrdered | Events, pings, critical commands |
| `Unreliable` | DeliveryMethod.Unreliable (UDP) | Movement inputs, snapshots |

### Message Identifiers

Depending on message type, logs include:

| Identifier | Type | Description |
|------------|------|-------------|
| `seq={N}` | uint | Movement sequence number (from InputPacket) |
| `tick={N}` | uint | Server tick (from SnapshotDelta) |
| `preview=XXXX` | hex | First 16 bytes of payload (if decode fails) |
| `intent={Type}` | enum | Intent type (MoveDir, MoveTo, Stop, Follow) |

---

## Client → Server Flow

### 1. Enqueue (Client-Side)

**Location:** `NetworkClient.cs` (when intent is created)

**Format:**
```
[SEND ENQUEUE] C→S seq={seq} intent={type} ticks={timestamp}
```

**Example:**
```
[SEND ENQUEUE] C→S seq=1 intent=MoveDir ticks=138737033227
```

**Fields:**
- `seq`: Movement sequence number (monotonically increasing)
- `intent`: MoveDir, MoveTo, Stop, or Follow
- `ticks`: Stopwatch.GetTimestamp() when intent was created

### 2. Socket Send (Client-Side)

**Location:** `FishNetAdapter.cs` (when packet is actually sent via FishNet)

**Format:**
```
[SOCKET SEND] C→S ticks={timestamp} {DeliveryMethod} size={bytes} seq={seq} preview={hex}
```

**Example:**
```
[SOCKET SEND] C→S ticks=138737354168 Unreliable size=32 seq=1 preview=0001000000010000...
```

**Fields:**
- `ticks`: Stopwatch.GetTimestamp() when socket send occurred
- `DeliveryMethod`: Reliable or Unreliable
- `size`: Packet size in bytes
- `seq`: Decoded sequence number (if successful)
- `preview`: First 16 bytes in hex (fallback if decode fails)

### 3. Transport Receive (Server-Side)

**Location:** `FishNetAdapter.cs` (when server receives packet)

**Format:**
```
[TRANSPORT RECV] C→S seq={seq} intent={type} socketTicks={timestamp}
```

**Example:**
```
[TRANSPORT RECV] C→S seq=1 intent=MoveDir socketTicks=138737686512
```

**Fields:**
- `seq`: Decoded sequence number
- `intent`: Decoded intent type
- `socketTicks`: Stopwatch.GetTimestamp() when received

---

## Server → Client Flow

### 1. Server Broadcast (Server-Side)

**Location:** `ServerGameLoop.cs` (when snapshot is generated)

**Format:**
```
[SERVER BROADCAST] tick={tick} vel=({x},{y})
```

**Example:**
```
[SERVER BROADCAST] tick=26 vel=(8.0,0.0)
```

**Fields:**
- `tick`: Server tick number
- `vel`: Example entity velocity (for verification)

### 2. Socket Send (Server-Side)

**Location:** `FishNetAdapter.cs` (when server sends snapshot)

**Format:**
```
[SERVER SOCKET SEND] S→C{clientId} ticks={timestamp} {DeliveryMethod} size={bytes} tick={tick} preview={hex}
```

**Example:**
```
[SERVER SOCKET SEND] S→C0 ticks=138738018622 Unreliable size=38 tick=26 preview=0002001A0000...
```

**Fields:**
- `clientId`: Target client ID (or "ALL" for broadcast)
- `ticks`: Stopwatch.GetTimestamp() when sent
- `DeliveryMethod`: Reliable or Unreliable
- `size`: Packet size in bytes
- `tick`: Decoded server tick
- `preview`: First 16 bytes in hex

### 3. Client Receive (Client-Side)

**Location:** `NetworkClient.cs` (when snapshot is processed)

**Format:**
```
[CLIENT RECV] S→C tick={tick} entities={count} ticks={timestamp}
```

**Example:**
```
[CLIENT RECV] S→C tick=26 entities=1 ticks=138738350982
```

**Fields:**
- `tick`: Server tick from snapshot
- `entities`: Number of entities in snapshot
- `ticks`: Stopwatch.GetTimestamp() when processed

---

## Sequence Correlation

### Tracking an Input Packet End-to-End

**Goal:** Measure exact latency from input creation to server processing

**Log pattern:**
```
[1.455] [SEND ENQUEUE] C→S seq=1 intent=MoveDir ticks=T1
[1.489] [SOCKET SEND] C→S seq=1 Unreliable ticks=T2
[1.522] [TRANSPORT RECV] C→S seq=1 intent=MoveDir ticks=T3
```

**Calculations:**

1. **Client flush delay:** `(T2 - T1) * 1000 / Stopwatch.Frequency` milliseconds
   - How long between intent creation and socket send
   - Target: <2ms (same frame)
   - High values indicate IterateOutgoing delay

2. **Network + server poll:** `(T3 - T2) * 1000 / Stopwatch.Frequency` milliseconds
   - Network latency + server IterateIncoming delay
   - Target: <20ms (network + 1 tick @ 60Hz)
   - High values indicate network latency or server poll delay

### Tracking a Snapshot End-to-End

**Goal:** Measure snapshot generation to client processing delay

**Log pattern:**
```
[2.100] [SERVER BROADCAST] tick=26 vel=(8.0,0.0)
[2.101] [SERVER SOCKET SEND] S→C0 ticks=T4 tick=26
[2.118] [CLIENT RECV] S→C tick=26 ticks=T5
```

**Calculation:**

**Network + client poll:** `(T5 - T4) * 1000 / Stopwatch.Frequency` milliseconds
- Network latency + client IterateIncoming delay
- Target: <20ms
- High values indicate network issues or client processing delay

---

## Payload Decoding

### InputPacket Decoding

**Method:** Extract sequence number from raw bytes

```csharp
// Skip 2 bytes (FishNet header)
// Read uint32 at offset +0 → ClientTick
// Read uint32 at offset +4 → MovementSeq ✅
```

**Sanity Check:**
```csharp
if (seq > 0 && seq < 10000)
    // Probably valid
else
    // Use hex preview instead
```

**Example:**
```
Bytes: 00 01 00 00 00 01 00 00 01 ...
           ↑ClientTick  ↑MovementSeq
Decoded: seq=1
```

### SnapshotDelta Decoding

**Method:** Extract server tick from raw bytes

```csharp
// Skip 2 bytes (FishNet header)
// Read uint32 at offset +0 → ServerTick ✅
```

**Example:**
```
Bytes: 00 02 00 1A 00 00 ...
           ↑ServerTick
Decoded: tick=26
```

---

## Timing Analysis

### Converting Stopwatch Ticks to Milliseconds

```csharp
long deltaTicks = ticksEnd - ticksStart;
double milliseconds = (deltaTicks * 1000.0) / Stopwatch.Frequency;
```

**Stopwatch.Frequency:** Varies by platform
- Windows: ~10,000,000 Hz (100ns resolution)
- MacOS: ~1,000,000,000 Hz (1ns resolution)
- Linux: ~1,000,000,000 Hz (1ns resolution)

### Expected Timings (Localhost)

| Stage | Expected Duration | Alarm Threshold |
|-------|-------------------|-----------------|
| Client flush delay (T2-T1) | <2ms | >5ms |
| Network (one-way) | ~1-5ms | >20ms |
| Server poll delay | ~16ms (1 tick) | >33ms |
| Client poll delay | ~16ms (1 tick) | >33ms |

---

## Instrumentation Points

### Where Logs Are Added

**Client-side:**
```
Assets/Scripts/Core/NetworkClient.cs
- SendInputIntent() → [SEND ENQUEUE]
- OnSnapshotReceived() → [CLIENT RECV]

Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs
- SendInputPacket() → [SOCKET SEND] C→S
```

**Server-side:**
```
Assets/Scripts/Server/ServerGameLoop.cs
- BroadcastSnapshots() → [SERVER BROADCAST]

Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs
- OnInputPacketReceived() → [TRANSPORT RECV] C→S
- BroadcastSnapshot() → [SERVER SOCKET SEND] S→C
```

---

## Enabling/Disabling Logs

### Compile-Time Flags

Add conditional compilation symbols to control verbosity:

```csharp
#if NETCODE_INSTRUMENTATION
    Debug.Log($"[SOCKET SEND] C→S seq={seq}");
#endif
```

**Enable in Unity:**
- Edit → Project Settings → Player → Other Settings → Scripting Define Symbols
- Add `NETCODE_INSTRUMENTATION`

### Runtime Toggles

Use a static flag for runtime control:

```csharp
// In NetcodeDebug.cs (create new file)
public static class NetcodeDebug
{
    public static bool EnableSocketLogs = false;
    public static bool EnableSequenceCorrelation = true;
    public static bool EnableTimingAnalysis = false;
}

// In FishNetAdapter.cs:
if (NetcodeDebug.EnableSocketLogs)
    Debug.Log($"[SOCKET SEND] ...");
```

---

## Analysis Examples

### Example 1: Measuring Total Input Latency

**Goal:** Find how long from key press to visual position change

**Steps:**

1. **Enable logs:**
   - Client: SEND ENQUEUE, SOCKET SEND
   - Server: TRANSPORT RECV, SERVER BROADCAST, SERVER SOCKET SEND
   - Client: CLIENT RECV

2. **Press movement key**

3. **Collect log sequence:**
   ```
   [1.455] [SEND ENQUEUE] C→S seq=1 ticks=T1
   [1.457] [SOCKET SEND] C→S seq=1 ticks=T2
   [1.474] [TRANSPORT RECV] C→S seq=1 ticks=T3
   [1.491] [SERVER BROADCAST] tick=26 (seq=1 processed this tick)
   [1.492] [SERVER SOCKET SEND] S→C0 tick=26 ticks=T4
   [1.509] [CLIENT RECV] S→C tick=26 ticks=T5
   ```

4. **Calculate stages:**
   ```
   Client flush:   T2-T1 =  2ms
   Network up:     T3-T2 = 17ms
   Server tick:    (included in next step)
   Server flush:   T4-T3 = 18ms (includes tick processing)
   Network down:   T5-T4 = 17ms
   ────────────────────────────
   Total:                  54ms
   ```

**Interpretation:** 54ms total latency is within expected range (<100ms target).

### Example 2: Diagnosing High Latency

**Scenario:** Input feels sluggish

**Observed logs:**
```
[1.000] [SEND ENQUEUE] C→S seq=1 ticks=1000000
[1.034] [SOCKET SEND] C→S seq=1 ticks=1340000  ← 34ms delay!
[1.051] [TRANSPORT RECV] C→S seq=1 ticks=1510000
```

**Analysis:**
- Client flush delay: 34ms (should be <2ms)
- **Problem:** IterateOutgoing not running every frame or queued behind other work

**Fix:** Verify FishNetAdapter.LateUpdate() is being called each frame.

### Example 3: Detecting Packet Loss

**Scenario:** Choppy movement

**Observed logs:**
```
[1.000] [CLIENT RECV] S→C tick=20
[1.017] [CLIENT RECV] S→C tick=21
[1.034] [CLIENT RECV] S→C tick=23  ← tick 22 missing!
[1.051] [CLIENT RECV] S→C tick=24
```

**Analysis:**
- Tick 22 snapshot was lost (UDP packet drop)
- Client fell back to extrapolation between tick 21 and 23

**Fix:** Check network stability, consider increasing adaptive buffer.

---

## Advanced Correlation

### Correlating Intent with Server Acknowledgment

**Client sends:**
```
[1.000] [SEND ENQUEUE] C→S seq=5 intent=MoveDir
```

**Server processes (later snapshot):**
```
[1.100] [SERVER BROADCAST] tick=30 lastProcessedSeq=5
```

**Client receives:**
```
[1.117] [CLIENT RECV] S→C tick=30 ackSeq=5
```

**Calculation:**
- Input lag: 117ms from send to acknowledgment
- Server processed seq=5 at tick=30
- Client can use this to estimate server processing delay

---

## Best Practices

### 1. Use Structured Logs

**Good:**
```csharp
Debug.Log($"[SOCKET SEND] C→S seq={seq} ticks={ticks} size={size}");
```

**Bad:**
```csharp
Debug.Log("Sending packet " + seq);
```

### 2. Include Timestamps

Always include `Stopwatch.GetTimestamp()` for correlation:

```csharp
long ticks = Stopwatch.GetTimestamp();
Debug.Log($"[EVENT] ticks={ticks}");
```

### 3. Rate-Limit Verbose Logs

For logs that fire every frame, add throttling:

```csharp
if (_frameCount % 60 == 0) // Once per second @ 60fps
    Debug.Log($"[STATS] {stats}");
```

### 4. Use Consistent Prefixes

Stick to the established format:
- `[SEND ENQUEUE]`
- `[SOCKET SEND]`
- `[TRANSPORT RECV]`
- `[SERVER BROADCAST]`
- `[CLIENT RECV]`

### 5. Decode When Possible

Always try to decode sequence/tick numbers:

```csharp
try
{
    uint seq = DecodeSequence(data);
    Debug.Log($"seq={seq}");
}
catch
{
    Debug.Log($"preview={ToHex(data)}");
}
```

---

## Troubleshooting with Logs

### Problem: Can't Correlate Sequence Numbers

**Symptoms:**
- Logs show `preview=XXXX` instead of `seq={N}`
- Sequence numbers don't match between logs

**Causes:**
1. Decoding offset is wrong (check FishNet header size)
2. Byte order mismatch (endianness)
3. Packet structure changed

**Fix:**
- Verify decoding logic matches current packet format (see `Network/02_Message_Specifications.md`)
- Add hex dump of full packet for manual inspection
- Check FishNet version for header changes

### Problem: Timestamp Deltas Are Negative

**Symptoms:**
- `T2 - T1 < 0` (negative duration)

**Causes:**
1. Logs are out of order (async processing)
2. Stopwatch wrapped (extremely rare)
3. Logs from different machines (client/server clocks not synced)

**Fix:**
- Ensure logs are from same run/machine
- Sort logs by timestamp before analysis
- Use sequence numbers for ordering, not timestamps

---

## Related Documentation

- [Debugging_Guide.md](Debugging_Guide.md) - General debugging techniques
- [Network/02_Message_Specifications.md](../Network/02_Message_Specifications.md) - Packet format details
- [Network/03_Data_Flow.md](../Network/03_Data_Flow.md) - Complete network flow diagram

---

**Last Updated:** 2026-02-03

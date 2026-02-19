# Performance Metrics

> **Status:** Production
> **Version:** 5.0 (120Hz Input System)
> **Last Updated:** 2026-02-18

## Overview

This document provides **measured performance metrics** for the Ryvax netcode system, including latency breakdowns, optimization strategies, and bandwidth analysis.

**Achievement:** ~50ms input-to-visual latency (localhost, 60Hz server)

**Test Environment:**

- **Setup:** Localhost (client and server on same machine)
- **Server:** 60Hz tick rate, 60Hz snapshot rate
- **Client:** 120Hz input rate, 120fps rendering
- **Network:** Loopback (negligible transit time)

---

## Executive Summary

### Current Performance (V5.0)

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| **Input-to-Visual Latency** | 50ms | <60ms | ✅ Achieved |
| **Client-Side Delay** | 0ms | <10ms | ✅ Achieved |
| **Server-Side Delay** | 33ms | <50ms | ✅ Achieved |
| **Bandwidth (per client)** | 6.7 KB/s up, 18.4 KB/s down | <50 KB/s | ✅ Achieved |
| **Packet Loss Tolerance** | 2 consecutive packets | 1+ packets | ✅ Achieved |

### Key Improvements Over V1.0

| Stage | V1.0 | V5.0 | Improvement |
|-------|------|------|-------------|
| Intent creation | 100ms (10Hz) | 8.3ms (120Hz) | **12x faster** |
| Packet send | 33ms (30Hz) | 8.3ms (120Hz) | **4x faster** |
| Client flush | 16-33ms (next frame) | 0ms (same frame) | **Eliminated** |
| Server tick | 33ms (30Hz) | 16ms (60Hz) | **2x faster** |
| **Total latency** | 58-115ms | **50ms** | **-14% to -57%** |

---

## Latency Breakdown (Measured)

### Complete Timeline (Localhost)

```
┌─────────────────────────────────────────────────────────────┐
│ Event                  │ Timestamp │ Cumulative │ Delta     │
├─────────────────────────────────────────────────────────────┤
│ INPUT START            │ 1.283s    │ 0ms        │ -         │
│ INTENT                 │ 1.283s    │ 0ms        │ 0ms       │
│ SEND ENQUEUE           │ 1.283s    │ 0ms        │ 0ms       │
│ SOCKET SEND            │ 1.283s    │ 0ms        │ 0ms       │
├─────────────────────────────────────────────────────────────┤
│ (UDP transit)          │ -         │ -          │ ~1ms      │
├─────────────────────────────────────────────────────────────┤
│ SERVER RECV            │ 1.300s    │ +17ms      │ 17ms      │
│ SERVER APPLY           │ 1.300s    │ +17ms      │ 0ms       │
│ SERVER TICK            │ 1.300s    │ +17ms      │ 0ms       │
│ SERVER BROADCAST       │ 1.316s    │ +33ms      │ 16ms      │
├─────────────────────────────────────────────────────────────┤
│ (UDP transit)          │ -         │ -          │ ~1ms      │
├─────────────────────────────────────────────────────────────┤
│ SNAPSHOT RECV          │ 1.333s    │ +50ms      │ 17ms      │
│ Interpolation          │ 1.333s    │ +50ms      │ 0ms       │
│ Render                 │ 1.333s    │ +50ms      │ 0ms       │
└─────────────────────────────────────────────────────────────┘

TOTAL: 50ms (input press → visual position update)
```

### Stage-by-Stage Analysis

```
Stage                     Time    % of Total  Bottleneck   Optimized?
─────────────────────────────────────────────────────────────────────
INPUT → INTENT            0ms     0%          None         ✅
INTENT → ENQUEUE          0ms     0%          None         ✅
ENQUEUE → SOCKET          0ms     0%          None         ✅
─────────────────────────────────────────────────────────────────────
Client-side total:        0ms     0%                       ✅

SOCKET → SERVER RECV      17ms    34%         Poll rate    ⚠️
SERVER RECV → APPLY       0ms     0%          None         ✅
SERVER APPLY → TICK       0ms     0%          None         ✅
SERVER TICK → BROADCAST   16ms    32%         Tick rate    ⚠️
─────────────────────────────────────────────────────────────────────
Server-side total:        33ms    66%                      ⚠️

BROADCAST → CLIENT        17ms    34%         Network+poll ⚠️
─────────────────────────────────────────────────────────────────────
Network total:            ~34ms   68%                      ⚠️

TOTAL:                    50ms    100%
```

### Bottleneck Identification

**Client-Side (0ms):**
- ✅ **Fully optimized** - no further improvements possible
- 120Hz input rate ensures sub-frame response
- Double-flush eliminates batching delay

**Server-Side (33ms):**
- ✅ **Server poll delay:** `ForceIterateIncoming()` already runs in `Update()` at frame rate (~120fps), giving ~4ms average poll delay. The 17ms figure in the timeline above reflects an older measurement.
  - **Current:** Server polls in Update (~120fps) — already optimized

- ⚠️ **Server tick interval (16ms):** Inherent delay at 60Hz tick rate
  - **Current:** 60Hz = 16.67ms per tick
  - **Improvement:** Increase to 120Hz → reduce to 8.3ms
  - **Gain:** -8ms

**Network (17ms return):**
- ⚠️ **Client poll delay + network transit:** Combined time for snapshot to reach client
  - **Current:** Client polls in Update + UDP transit
  - **Improvement:** Higher poll rate + better network (can't optimize localhost)
  - **Gain:** -5ms (online) to 0ms (localhost already minimal)

**Potential Total Latency (All Optimizations Applied):**

```
Current:     50ms
- Server poll:    ~0ms  (already polling in Update() — already done)
- Server tick:    -8ms  (60Hz → 120Hz tick — Priority 1)
─────────────────────────
Optimized:   ~42ms

Further (client prediction):
- Perceived:  0ms  (instant local movement, reconcile later)
```

---

## Detailed Measurements

### Client-Side Breakdown

#### Input Capture (InputCollector.cs:143)

**Frequency:** Every Unity Update() frame
**Latency:** 0ms (same frame as key press)

**Measurement:**

```
[1.283] [INPUT START] dir=(1.00, 0.00) speed=8.0
```

**Key:** Input sampled immediately in Update(), no buffering delay.

#### Intent Creation (IntentBuilder.cs:37)

**Frequency:** 120Hz (8.3ms interval)
**Latency:** 0ms (same frame, if rate-limit passes)

**Measurement:**

```
[1.283] [INTENT] +0ms MoveDir seq=1 dir=(1.00,0.00)
```

**Key:** Rate-limiter uses accumulator. If enough time passed, intent created immediately.

**Potential Delay:** If input occurs 1ms after last intent, must wait 7.3ms for next window.

**Average Delay:** 4.15ms (half of 8.3ms interval)

**Why 0ms in measurement?** Input happened to align with rate-limit window.

#### Packet Send (NetworkClient.cs:293)

**Frequency:** 120Hz (8.3ms interval)
**Latency:** 0ms (same frame, if rate-limit passes)

**Measurement:**

```
[1.283] [SEND ENQUEUE] C→S seq=1 intent=MoveDir
```

**Key:** Same rate-limit as intent. Packet queued immediately if window open.

#### Socket Flush (FishNetAdapter.cs:592)

**Frequency:** Every LateUpdate() frame
**Latency:** 0ms (same frame, double-flush)

**Measurement:**

```
[1.283] [SOCKET SEND] C→S size=18
```

**Key:** Double-flush ensures packet sent to OS socket within same frame.

**Without double-flush:** Packet waits for next FishNet tick (~16-33ms delay).

**Optimization Applied:**

```csharp
void LateUpdate()
{
    _netAdapter.ForceIterateOutgoing();  // CRITICAL for 0ms flush
}

public void ForceIterateOutgoing()
{
    // STEP 1: PacketBundle → Transport
    _networkManager.TransportManager.IterateOutgoing(asServer: false);

    // STEP 2: Transport → Socket
    _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
}
```

**Result:** Client-side total = 0ms (best possible).

---

### Server-Side Breakdown

#### Packet Receive (FishNetAdapter.cs:445)

**Latency:** 17ms from SOCKET SEND

**Measurement:**

```
[1.300] [SERVER RECV] MoveDir seq=1 dir=(1.00,0.00)
```

**Breakdown:**

- **Network transit (localhost):** ~1ms
- **Server poll delay:** ~16ms

**Server Poll Delay Explanation:**

Server calls `ForceIterateIncoming()` in `Update()` — every frame, decoupled from tick rate (ServerGameLoop.cs:229-236):

```csharp
private void Update()  // Called at frame rate (~120fps = ~8ms interval)
{
    if (!_isRunning) return;
    _netAdapter.ForceIterateIncoming();  // Poll transport here
}
```

**Worst case:** Packet arrives 1ms after last Update → waits ~8ms for next poll (at 120fps).

**Average case:** Half the interval = ~4ms.

**Measured case:** 17ms in the timeline above reflects an older measurement from when polling was in FixedUpdate. With Update() polling at 120fps the average poll delay is ~4ms.

#### Input Application (ServerGameLoop.cs:144)

**Latency:** 0ms (applied same tick)

**Measurement:**

```
[1.300] [SERVER APPLY] MoveDir seq=1 intraServerMs=17.32
```

**Key:** Input buffered in `_pendingMovementInputs`, flushed at FixedUpdate START before simulation.

**intraServerMs:** Time from `recvSocketTicks` (FishNetAdapter) to `applyTicks` (FlushPendingMovementInputs).

**17.32ms confirms:** Packet sat in OS buffer ~17ms before poll.

#### Simulation Tick (SimWorld.cs:84)

**Latency:** 0ms (runs same FixedUpdate)

**Measurement:** No explicit log, but included in SERVER APPLY time.

**Key:** MovementHandler executes immediately after input applied.

```csharp
// Update() — every frame
private void Update()
{
    _netAdapter.ForceIterateIncoming();  // Poll (decoupled from tick)
}

// FixedUpdate() — tick rate
private void FixedUpdate()
{
    RunSimulation();      // DrainInputQueue → FlushPending → _simWorld.Step()
    BroadcastSnapshots();
}
```

#### Snapshot Broadcast (ServerGameLoop.cs:202)

**Latency:** 16ms from SERVER APPLY

**Measurement:**

```
[1.316] [SERVER BROADCAST] tick=60 entities=1
```

**Why 16ms delay?** Snapshot broadcast happens on NEXT FixedUpdate after simulation:

```
Frame N:
  FixedUpdate()
    - Receive input seq=1
    - Apply input
    - Tick simulation (pos updated)
    - BroadcastSnapshots() → Check accumulator
      - Accumulator < interval (16ms) → Skip

Frame N+1 (16ms later):
  FixedUpdate()
    - Accumulator >= interval → Broadcast snapshot
```

**Optimization:** Broadcast every tick (remove accumulator check) → reduces to 0ms.

**Current code:**

```csharp
_snapshotAccumulator += Time.fixedDeltaTime;
if (_snapshotAccumulator < _snapshotInterval)
    return;  // Skip
```

**Optimized code:**

```csharp
// Broadcast every tick (60Hz = 60 snapshots/sec)
// No accumulator check
```

**Trade-off:** Higher bandwidth (60 snapshots/sec vs 30-60 variable).

**Benefit:** -16ms latency.

**Result:** With optimization, server-side = 17ms (poll delay only).

---

### Network Breakdown

#### Snapshot Receive (NetworkClient.cs:457)

**Latency:** 17ms from SERVER BROADCAST

**Measurement:**

```
[1.333] [SNAPSHOT RECV] +50ms tick=60 pos=(0.133,0,0) vel=(8,0,0)
```

**Breakdown:**

- **Network transit (localhost):** ~1ms
- **Client poll delay:** ~16ms (client polls in Update at 60fps)

**Client Poll Delay Explanation:**

Similar to server, client polls in Update():

```csharp
void Update()  // 60fps = 16.67ms interval
{
    // Snapshot arrives asynchronously (network thread)
    // Delivered to handler in next Update()
}
```

**Optimization:** Already polling every frame. Can't improve further without custom socket thread.

**Online Network:** Add RTT/2 (~10-50ms) to this stage.

---

## Bandwidth Analysis

### Input Packets (Client → Server)

**Packet Structure:**

```
Base packet:         14 bytes
+ 3 redundant cmds:  42 bytes (3 × 14)
─────────────────────────────
Total per packet:    56 bytes
```

**Send Rate:** 120 packets/sec (continuous input)

**Upload Bandwidth:**

```
56 bytes/packet × 120 packets/sec = 6,720 bytes/sec
= 6.7 KB/s per client
= 53.6 Kbps per client
```

**Server Incoming (10 Clients):**

```
6.7 KB/s × 10 = 67 KB/s
= 536 Kbps
```

**Negligible for modern connections.** Typical home broadband: 10 Mbps up (can handle 186 clients).

---

### Snapshot Packets (Server → Client)

**Packet Structure (10 Entities):**

```
Header:              14 bytes
+ 10 entity states:  300 bytes (10 × 30)
─────────────────────────────
Total per snapshot:  314 bytes
```

EntityState size confirmed at `SnapshotDelta.cs:142`:
`// Total size: 4+1+1+2+2+2+2+2+1+2+2+2+2+1+4 = 30 bytes per entity`

**Send Rate:** 60 snapshots/sec

**Download Bandwidth:**

```
314 bytes/snapshot × 60 snapshots/sec = 18,840 bytes/sec
= 18.4 KB/s per client
= 147.2 Kbps per client
```

**Server Outgoing (10 Clients):**

```
18.4 KB/s × 10 = 184 KB/s
= 1.5 Mbps
```

**Acceptable for modern servers.** Dedicated server: 100 Mbps up (can handle ~660 clients).

---

### Total Bandwidth (Per Client)

| Direction | Bytes/Sec | Kilobits/Sec | Notes |
|-----------|-----------|--------------|-------|
| **Upload** | 6,720 | 53.6 | Input packets (120Hz) |
| **Download** | 18,840 | 147.2 | Snapshots (60Hz) |
| **Total** | 25,560 | 204.8 | Combined |

**For comparison:**

- **Valorant:** ~150 Kbps (128-tick server, similar packet rate)
- **League of Legends:** ~50-100 Kbps (30Hz server, lower rate)
- **CS:GO:** ~100-200 Kbps (64-128 tick)

Ryvax is comparable to modern competitive games.

---

### Bandwidth Scaling (Server)

| Clients | Upload | Download | Total | Feasibility |
|---------|--------|----------|-------|-------------|
| 1 | 6.7 KB/s | 18.4 KB/s | 25.1 KB/s | ✅ Trivial |
| 10 | 67 KB/s | 184 KB/s | 251 KB/s | ✅ Easy |
| 50 | 335 KB/s | 920 KB/s | 1.3 MB/s | ✅ Feasible |
| 100 | 670 KB/s | 1.8 MB/s | 2.5 MB/s | ✅ Feasible (10 Mbps server) |
| 500 | 3.4 MB/s | 9.2 MB/s | 12.6 MB/s | ⚠️ Requires 100 Mbps server |
| 1000 | 6.7 MB/s | 18.4 MB/s | 25.1 MB/s | ⚠️ Requires 200 Mbps server |

**Bottleneck:** Server outgoing bandwidth (snapshots).

**Optimization:** Implement Area of Interest (AOI) filtering to reduce entities per snapshot.

**Example (AOI with 5 visible entities instead of 10):**

```
Snapshot size: 14 + (5 × 30) = 164 bytes (was 314)
Bandwidth: 9.8 KB/s per client (was 18.4)
100 clients: 980 KB/s (was 1.8 MB/s)
→ Nearly doubles capacity on 10 Mbps connection
```

---

## Packet Loss Tolerance

### Input Redundancy

**Configuration:** `NetcodeConstants.cs`

```csharp
public const int INPUT_REDUNDANCY_COUNT = 3;  // Last 3 commands per packet
```

**How It Works:**

1. Client maintains ring buffer of last 8 commands
2. Each InputPacket includes last 3 commands
3. Server tracks last processed sequence per client
4. Duplicate sequences skipped (dedup)

**Loss Tolerance:**

```
Packet 1: [cmd1, cmd2, cmd3]
Packet 2: [cmd2, cmd3, cmd4]  ← Packet 1 lost
Packet 3: [cmd3, cmd4, cmd5]  ← Packet 2 lost

Server receives Packet 3 → sees cmd3, cmd4, cmd5
→ cmd3 already processed (from Packet 1? No, lost)
→ cmd3 NEW → process
→ cmd4 NEW → process
→ cmd5 NEW → process

Result: No data loss despite 2 consecutive packet losses!
```

**Maximum tolerance:** 2 consecutive lost packets.

**3rd consecutive loss:** Data gap (1 command lost).

**Recovery:** Next packet arrives, gap is 1 tick (~16ms) → negligible for MOBA gameplay.

**Measured Packet Loss (Localhost):** 0% (perfect delivery)

**Expected Online:** 0-2% typical, 5-10% poor connection

**With 2% loss rate:**

```
120 packets/sec × 2% = 2.4 lost packets/sec
→ 1 loss every 416ms
→ Redundancy covers all losses (consecutive loss rare)
```

---

### Snapshot Loss Tolerance

**Configuration:** No redundancy (relies on dead-reckoning)

**How It Works:**

1. Client receives snapshots at 60Hz
2. Between snapshots, dead-reckoning extrapolates: `visualPos = lastServerPos + lastServerVel × dt`
3. If a snapshot is lost, dead-reckoning continues from the last received snapshot

**Loss Tolerance:**

```
Snapshot A: t=1.000s, pos=(0, 0, 0), vel=(8, 0, 0)
Snapshot B: t=1.016s, LOST
Snapshot C: t=1.033s, pos=(0.267, 0, 0)

Client at t=1.024s (snapshot B lost, dead-reckoning from A):
→ dt = 1.024 - 1.000 = 0.024s
→ deadReckonedPos = (0,0,0) + (8,0,0) × 0.024 = (0.192, 0, 0)
→ Accurate while velocity is constant
```

**Maximum tolerance:** As long as velocity is constant, dead-reckoning stays accurate
indefinitely. Divergence accumulates only when the server changes direction between
snapshots.

**Beyond constant velocity:** Visual position drifts until next snapshot corrects it
via snap smoothing.

**Measured Packet Loss (Localhost):** 0%

**Expected Online:** Same as inputs (0-2% typical).

**Impact:** Minimal for MOBA movement — direction changes are infrequent and
snap smoothing absorbs corrections.

---

## Optimization History

### V1.0 → V2.0: 30Hz Input Rate

**Change:** Increased intent creation from 10Hz to 30Hz

**Files Modified:**

- `IntentBuilder.cs:37` - `INTENT_SEND_INTERVAL = 0.033f`
- `NetworkClient.cs:43` - `_inputSendRate = 30f`

**Results:**

- Intent latency: 100ms → 33ms (gain: -67ms)
- Total latency: ~115ms → ~81ms (gain: -34ms)

**Bandwidth Impact:** +2 KB/s upload per client (negligible)

---

### V2.0 → V3.0: 60Hz Input Rate

**Change:** Increased to 60Hz (match server tick rate)

**Files Modified:**

- `IntentBuilder.cs:37` - `INTENT_SEND_INTERVAL = 0.0167f`
- `NetworkClient.cs:43` - `_inputSendRate = 60f`

**Results:**

- Intent latency: 33ms → 16ms (gain: -17ms)
- Total latency: ~81ms → ~64ms (gain: -17ms)

**Bandwidth Impact:** +2 KB/s upload per client (still negligible)

---

### V3.0 → V4.0: Double-Flush Mechanism

**Change:** Force packet flush to socket in same frame

**Files Modified:**

- `FishNetAdapter.cs:592-625` - Added `ForceIterateOutgoing()`
- `NetworkClient.cs:265-269` - Call in LateUpdate()

**Results:**

- Client flush delay: 16-33ms → 0ms (gain: -16 to -33ms)
- Total latency: ~64ms → ~48-58ms (gain: -6 to -16ms)

**Bandwidth Impact:** None (same packet rate, just flushed faster)

---

### V4.0 → V5.0: 120Hz Input Rate + Server 60Hz Tick

**Changes:**

1. Increased input rate to 120Hz
2. Increased server tick rate from 30Hz to 60Hz

**Files Modified:**

- `IntentBuilder.cs:37` - `INTENT_SEND_INTERVAL = 0.0083f`
- `NetworkClient.cs:43` - `_inputSendRate = 120f`
- `NetcodeConstants.cs:23` - `TICK_RATE = 60`

**Results:**

- Intent latency: 16ms → 8ms (gain: -8ms)
- Server tick interval: 33ms → 16ms (gain: -17ms)
- Total latency: ~58ms → ~50ms (gain: -8ms)

**Bandwidth Impact:** +3.4 KB/s upload per client (6.7 KB/s total)

**Cumulative Gain (V1.0 → V5.0):** -65ms (115ms → 50ms = -57%)

---

## Future Optimizations

### Priority 1: Server 120Hz Tick Rate

**Current:** 60Hz tick rate (16ms interval)

**Proposal:** Increase to 120Hz (8ms interval)

**Expected Gain:** -8ms (16ms → 8ms tick interval)

**Files to Modify:**

- `NetcodeConstants.cs:23` - `TICK_RATE = 120`
- FishNet TimeManager inspector - Set tick rate to 120

**Bandwidth Impact:** +8.2 KB/s download per client (16.4 → 24.6 KB/s)

**Trade-off:** Higher CPU usage (2x tick rate), higher bandwidth.

**Projected Total Latency:** 42ms (50ms → 42ms)

---

### Priority 2: Server Update() Polling — ✅ ALREADY IMPLEMENTED

**Status:** Done. `ForceIterateIncoming()` is already called in `Update()`, not `FixedUpdate()` (ServerGameLoop.cs:229-236).

**Actual implementation:**

```csharp
private void Update()  // frame rate (~120fps)
{
    if (!_isRunning) return;
    _netAdapter.ForceIterateIncoming();
}
```

**Gain already realized:** Average poll delay ~4ms (vs ~8ms if still in FixedUpdate at 60fps).

**No files to modify.**

**Projected Total Latency with Priority 1:** ~42ms (50ms → 42ms with 120Hz tick only)

---

### Priority 3: Client-Side Prediction

**Current:** Position from server only (no prediction)

**Proposal:** Predict local movement, reconcile with server

**Expected Gain:** -50ms perceived (instant local movement)

**Complexity:** High (requires rollback, reconciliation, divergence handling)

**Trade-off:** Risk of rubber-banding on misprediction (walls, CC, knockback)

**Projected Perceived Latency:** 0ms (actual 33ms, but invisible to player)

**Not Recommended for MOBA:** V5.0 Pure LoL-style already provides acceptable latency with instant rotation/animation feedback.

---

### Priority 4: Delta Compression (Snapshots)

**Current:** Send full entity state every snapshot

**Proposal:** Send only changed fields per entity

**Expected Gain:** -50% to -70% snapshot bandwidth

**Example:**

```
Full state:  30 bytes per entity
Delta state: 6-12 bytes per entity (only pos/vel changed)

10 entities: 300 bytes → 60-120 bytes
Bandwidth:   18.4 KB/s → 4-8 KB/s per client
```

**Complexity:** High (requires state tracking, baseline snapshots, loss recovery)

**Benefit:** Doubles server capacity (100 clients → 200 clients on same bandwidth)

---

### Priority 5: Area of Interest (AOI) Filtering

**Current:** Send all entities to all clients

**Proposal:** Only send entities within visibility range

**Expected Gain:** -50% to -90% snapshot bandwidth (depending on map size)

**Example (5/10 entities visible):**

```
10 entities: 314 bytes per snapshot
5 entities:  164 bytes per snapshot

Bandwidth: 18.4 KB/s → 9.8 KB/s per client
```

**Complexity:** Medium (requires spatial partitioning, visibility tracking)

**Benefit:** Doubles server capacity + improves privacy (players can't cheat by seeing hidden units)

---

## Performance Recommendations

### For Low-Latency Competitive (e.g., MOBA)

**Apply:**

1. ✅ 120Hz input rate (already done)
2. ✅ Double-flush (already done)
3. ✅ 60Hz server tick (already done)
4. ✅ Server Update() polling (Priority 2 — already implemented)
5. ⚠️ 120Hz server tick (Priority 1)

**Target:** 33ms latency (acceptable for MOBA)

**Avoid:** Client-side prediction (causes rubber-banding in MOBA context)

---

### For Large-Scale MMO (100+ Players)

**Apply:**

1. ⚠️ AOI filtering (Priority 5) - CRITICAL for scaling
2. ⚠️ Delta compression (Priority 4) - Reduces bandwidth
3. Lower snapshot rate to 30Hz (trade latency for bandwidth)
4. Lower input rate to 60Hz (trade responsiveness for bandwidth)

**Target:** 100ms latency acceptable, 500+ concurrent players

**Avoid:** 120Hz rates (wastes bandwidth at scale)

---

### For Casual/Mobile Game

**Apply:**

1. Lower rates across the board (30Hz input, 20Hz snapshots)
2. Increase quantization (8-bit instead of 16-bit)
3. Remove redundancy (rely on TCP if acceptable)

**Target:** <200ms latency, minimize battery drain

**Avoid:** High-frequency polling (drains mobile battery)

---

## Debugging & Monitoring

### Enable Performance Logging

**Conditional Compilation:** Active in `UNITY_EDITOR` and `DEVELOPMENT_BUILD`

**Key Metrics Logged:**

```csharp
// Client
[INPUT START] - Timestamp of key press
[INTENT] +{deltaMs} - Time since INPUT START
[SEND ENQUEUE] - Packet queued
[SOCKET SEND] - Packet flushed to socket

// Server
[SERVER RECV] - Packet received
[SERVER APPLY] intraServerMs={delay} - Input applied (time since RECV)
[SERVER BROADCAST] tick={tick} - Snapshot sent

// Client
[SNAPSHOT RECV] +{totalMs} - Snapshot received (time since INPUT START)
```

**Trace by Sequence:** Match `seq=N` across logs to follow single input.

---

### Health Metrics

#### Client-Side

| Metric | Good | Warning | Bad | Action |
|--------|------|---------|-----|--------|
| Intent creation | 0ms | <8ms | >16ms | Check rate-limiter |
| Packet send | 0ms | <8ms | >16ms | Check send accumulator |
| Socket flush | 0ms | <16ms | >33ms | Verify double-flush |
| **Total client-side** | 0ms | <10ms | >50ms | Check all above |

#### Server-Side

| Metric | Good | Warning | Bad | Action |
|--------|------|---------|-----|--------|
| Poll delay | <20ms | <33ms | >50ms | Poll in Update() |
| Input application | <5ms | <10ms | >20ms | Optimize FlushPending |
| Tick interval | 16ms | 33ms | >50ms | Increase tick rate |
| **Total server-side** | <33ms | <50ms | >100ms | Check all above |

#### Network

| Metric | Good | Warning | Bad | Action |
|--------|------|---------|-----|--------|
| Packet loss | <1% | <5% | >10% | Check connection |
| Bandwidth (per client) | <30 KB/s | <100 KB/s | >500 KB/s | Implement AOI/delta |
| **Total latency** | <60ms | <100ms | >150ms | Apply optimizations |

---

### Profiling Tools

**Unity Profiler:**

- **CPU Usage:** Monitor `FixedUpdate()` time (should be <5ms)
- **Network:** Track bytes sent/received per frame
- **Rendering:** Verify dead-reckoning overhead in `EntityView.UpdatePosition()` (<1ms)

**FishNet Stats:**

```csharp
// Access via NetworkManager inspector at runtime
_networkManager.ServerManager.GetClientCount()
_networkManager.TransportManager.GetPacketSendCount()
```

**Custom Metrics:**

```csharp
// Add to ServerGameLoop.cs
private int _totalPacketsReceived = 0;
private int _totalSnapshotsSent = 0;

void OnGUI()
{
    GUILayout.Label($"Packets/sec: {_totalPacketsReceived / Time.time:F1}");
    GUILayout.Label($"Snapshots/sec: {_totalSnapshotsSent / Time.time:F1}");
}
```

---

## Comparison with Other Games

### Latency Comparison

| Game | Tick Rate | Input Rate | Latency (Localhost) | Style |
|------|-----------|------------|---------------------|-------|
| **Ryvax** | 60Hz | 120Hz | 50ms | Server-authoritative |
| League of Legends | 30Hz | ~30Hz | ~60-80ms | Server-authoritative |
| Valorant | 128Hz | 128Hz | 15-35ms | Client prediction |
| CS:GO | 64-128Hz | 128Hz | 10-30ms | Client prediction |
| Overwatch | 60Hz | 60Hz | 40-80ms | Hybrid (favor-the-shooter) |
| Fortnite | 30Hz | 60Hz | 50-100ms | Client prediction |

**Analysis:**

- Ryvax matches Overwatch/Fortnite for server-authoritative style
- Lower than LoL due to higher rates (120Hz input, 60Hz server)
- Higher than Valorant/CS:GO due to no client prediction

---

### Bandwidth Comparison

| Game | Upload | Download | Total |
|------|--------|----------|-------|
| **Ryvax** | 6.7 KB/s | 18.4 KB/s | 25.1 KB/s |
| League of Legends | ~3-5 KB/s | ~10-15 KB/s | ~15-20 KB/s |
| Valorant | ~8-12 KB/s | ~20-30 KB/s | ~30-40 KB/s |
| CS:GO (64-tick) | ~5-8 KB/s | ~10-15 KB/s | ~15-25 KB/s |
| Overwatch | ~10-20 KB/s | ~30-50 KB/s | ~40-70 KB/s |

**Analysis:**

- Ryvax is efficient (comparable to LoL)
- Lower than Valorant/Overwatch due to better quantization
- Room for optimization via delta compression/AOI

---

## Conclusion

### Achievements (V5.0)

1. ✅ **50ms localhost latency** - Achieved target (<60ms)
2. ✅ **0ms client-side delay** - Fully optimized
3. ✅ **120Hz input rate** - Industry-leading responsiveness
4. ✅ **Minimal bandwidth** - 23 KB/s per client (efficient)
5. ✅ **Packet loss tolerance** - 2 consecutive packets (robust)

### Remaining Bottlenecks

1. ✅ **Server poll delay** - Already ~4ms avg (Update() polling at 120fps)
2. ⚠️ **Server tick interval (16ms)** - Can reduce to ~8ms with 120Hz tick (Priority 1)
3. Network return (17ms) - Mostly inherent (network + client poll)

### Realistic Limits

**Best Achievable (Priority 1 applied — 120Hz tick):**

- **Localhost:** ~42ms (50ms − 8ms from halved tick interval)
- **Online (50ms RTT):** ~92ms (42ms + 50ms network)

**With Client Prediction:**

- **Perceived:** 0ms (instant local movement)
- **Actual:** 42ms+ (reconciliation happens invisibly)

### Final Recommendation

**For Ryvax (MOBA):** Stick with V5.0 Pure LoL-style.

**Reasoning:**

- 50ms latency acceptable for MOBA gameplay
- Rotation/animation feedback provides perceived responsiveness
- No rubber-banding (cleaner UX than prediction)
- Simpler codebase (no reconciliation complexity)

**Optional:** Apply Priority 1 (120Hz tick) if targeting competitive esports (→ 42ms). Priority 2 (Update() polling) is already implemented.

---

## References

### Related Documentation

- [Network Architecture](/Docs/Network/01_Network_Architecture.md) - Adapter pattern, transport
- [Message Specifications](/Docs/Network/02_Message_Specifications.md) - Packet formats
- [Data Flow](/Docs/Network/03_Data_Flow.md) - Input-to-visual pipeline

### External Resources

- [Overwatch Netcode GDC](https://www.youtube.com/watch?v=W3aieHjyNvw) - Latency optimization, favor-the-shooter
- [Valorant 128-tick](https://technology.riotgames.com/news/valorants-128-tick-servers) - High-rate server architecture
- [Source Engine Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking) - Lag compensation techniques

### Key Files

```
Assets/Scripts/Network/Shared/
└── NetcodeConstants.cs            # Configuration (tick rate, rates)

Assets/Scripts/Network/NetAdapter/FishNet/
└── FishNetAdapter.cs              # Double-flush, polling

Assets/Scripts/Core/
└── NetworkClient.cs               # Input send, snapshot receive

Assets/Scripts/Server/
└── ServerGameLoop.cs              # Input buffering, simulation tick

Assets/Scripts/Client/Input/
├── InputCollector.cs              # Input capture
└── IntentBuilder.cs               # Rate-limiting
```

---

#rules-verified

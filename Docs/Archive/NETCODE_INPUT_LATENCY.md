# Input Latency Optimization (120Hz System)

## Overview

This document describes the low-latency input pipeline optimized for competitive gameplay (LoL-style netcode).

**Current Result: ~50ms input-to-visual latency (localhost)**

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         CLIENT (120Hz Input Pipeline)                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐               │
│  │InputCollector│───▶│ IntentBuilder│───▶│NetworkClient │               │
│  │  (Update)    │    │   (120Hz)    │    │   (120Hz)    │               │
│  └──────────────┘    └──────────────┘    └──────────────┘               │
│         │                   │                    │                       │
│     [INPUT START]      [INTENT]           [SEND ENQUEUE]                │
│      t=0ms              t=0ms               t=0ms                        │
│                                                  │                       │
│                                                  ▼                       │
│                                         ┌──────────────┐                │
│                                         │FishNetAdapter│                │
│                                         │  Broadcast() │                │
│                                         └──────────────┘                │
│                                                  │                       │
│                          ┌───────────────────────┘                       │
│                          ▼                                               │
│                 ┌─────────────────┐                                      │
│                 │ ForceIterateOut │ (LateUpdate)                         │
│                 │  ├─ TransportMgr.IterateOutgoing() ◀── Flush Bundle   │
│                 │  └─ Transport.IterateOutgoing()    ◀── Flush Socket   │
│                 └─────────────────┘                                      │
│                          │                                               │
│                    [SOCKET SEND]                                         │
│                       t=0ms                                              │
│                          │                                               │
└──────────────────────────┼───────────────────────────────────────────────┘
                           │ UDP Packet
                           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                              SERVER                                      │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐               │
│  │ Transport    │───▶│ InputBuffer  │───▶│  SimWorld    │               │
│  │ IterateIn()  │    │  (buffered)  │    │   (apply)    │               │
│  └──────────────┘    └──────────────┘    └──────────────┘               │
│         │                                        │                       │
│   [SERVER RECV]                            [SERVER APPLY]               │
│     t=+17ms                                  t=+17ms                     │
│                                                  │                       │
│                                                  ▼                       │
│                                         ┌──────────────┐                │
│                                         │   Snapshot   │                │
│                                         │  Broadcast   │                │
│                                         └──────────────┘                │
│                                                  │                       │
│                                           [SERVER SEND]                  │
│                                             t=+33ms                      │
│                                                  │                       │
└──────────────────────────────────────────────────┼───────────────────────┘
                                                   │
                                                   ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         CLIENT (Receive)                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                              [SNAPSHOT RECV]                             │
│                                 t=+50ms                                  │
│                                    │                                     │
│                                    ▼                                     │
│                           Visual Update Applied                          │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

## Measured Results (Localhost)

### Timeline from Actual Logs

```
[1.283] INPUT START      ─┐
[1.283] INTENT             │ Frame 70 - All in same frame!
[1.283] SEND ENQUEUE       │ Client-side: 0ms
[1.283] SOCKET SEND      ─┘

[1.300] SERVER RECV      ─┐ +17ms from input
[1.300] SERVER APPLY     ─┘ intraServerMs=17.32ms

[1.316] SERVER BROADCAST   +33ms from input

[1.333] SNAPSHOT RECV      +50ms from input (vel changes visible)
```

### Latency Breakdown (Cumulative)

| Stage | Time | From Start | Notes |
|-------|------|------------|-------|
| INPUT START | 1.283s | 0ms | Keyboard detected |
| INTENT | 1.283s | +0ms | Intent created (same frame) |
| SEND ENQUEUE | 1.283s | +0ms | Packet queued (same frame) |
| SOCKET SEND | 1.283s | +0ms | UDP sent (same frame) |
| SERVER RECV | 1.300s | +17ms | Server polls incoming |
| SERVER APPLY | 1.300s | +17ms | Applied to simulation |
| SERVER BROADCAST | 1.316s | +33ms | Snapshot sent to clients |
| SNAPSHOT RECV | 1.333s | **+50ms** | Client receives new velocity |

### Delta Between Each Stage

```
┌─────────────────┐
│  INPUT START    │ ◀── User presses key
└────────┬────────┘
         │ 0ms (same frame, 120Hz rate OK)
         ▼
┌─────────────────┐
│     INTENT      │ ◀── IntentBuilder creates MoveDir
└────────┬────────┘
         │ 0ms (same frame, 120Hz rate OK)
         ▼
┌─────────────────┐
│  SEND ENQUEUE   │ ◀── NetworkClient queues packet
└────────┬────────┘
         │ 0ms (same frame, double-flush works)
         ▼
┌─────────────────┐
│   SOCKET SEND   │ ◀── UDP packet leaves client
└────────┬────────┘
         │
         │ +17ms ◀── Network RTT + Server Update() poll interval
         │           (could be reduced with faster server poll)
         ▼
┌─────────────────┐
│   SERVER RECV   │ ◀── Server receives packet
└────────┬────────┘
         │ 0ms (buffered, applied same tick)
         ▼
┌─────────────────┐
│  SERVER APPLY   │ ◀── Input applied to SimWorld
└────────┬────────┘
         │
         │ +16ms ◀── Server tick interval (30Hz = 33ms, actual ~16ms)
         │           (could be reduced with 60Hz tick rate)
         ▼
┌─────────────────┐
│SERVER BROADCAST │ ◀── Snapshot with new velocity sent
└────────┬────────┘
         │
         │ +17ms ◀── Network RTT + Client poll interval
         │
         ▼
┌─────────────────┐
│  SNAPSHOT RECV  │ ◀── Client receives updated state
└─────────────────┘

TOTAL: 50ms
```

### Stage-by-Stage Delta Summary

| From | To | Delta | Bottleneck | Optimization |
|------|----|-------|------------|--------------|
| INPUT START | INTENT | **0ms** | ✅ None | - |
| INTENT | SEND ENQUEUE | **0ms** | ✅ None | - |
| SEND ENQUEUE | SOCKET SEND | **0ms** | ✅ None | - |
| SOCKET SEND | SERVER RECV | **17ms** | Server poll rate | ForceIterateIncoming in Update |
| SERVER RECV | SERVER APPLY | **0ms** | ✅ None | - |
| SERVER APPLY | SERVER BROADCAST | **16ms** | Server tick rate (30Hz) | Increase to 60Hz |
| SERVER BROADCAST | SNAPSHOT RECV | **17ms** | Client poll + network | Already optimized |
| **TOTAL** | | **50ms** | | |

### Where Time Is Spent

```
Client-side (INPUT → SOCKET SEND):     0ms  ( 0%)  ✅ Fully optimized
Server poll (SOCKET SEND → RECV):     17ms  (34%)  ⚠️ Could improve
Server processing (RECV → APPLY):      0ms  ( 0%)  ✅ Fully optimized
Server tick (APPLY → BROADCAST):      16ms  (32%)  ⚠️ Could improve (60Hz)
Network return (BROADCAST → CLIENT):  17ms  (34%)  ⚠️ Inherent latency
─────────────────────────────────────────────────
TOTAL:                                50ms (100%)
```

## Configuration

### IntentBuilder.cs (line 37)
```csharp
// Rate limiting aligned with network send rate
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz (8.3ms)
```

### NetworkClient.cs (line 54)
```csharp
[SerializeField] private float _inputSendRate = 120f;  // 120Hz
```

### Key Files

| File | Purpose |
|------|---------|
| `Assets/Scripts/Client/Input/InputCollector.cs` | Detects keyboard/mouse input |
| `Assets/Scripts/Client/Input/IntentBuilder.cs` | Creates intents at 120Hz |
| `Assets/Scripts/Core/NetworkClient.cs` | Sends packets at 120Hz |
| `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs` | Double-flush implementation |

## Comparison: Before vs After

| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Intent rate | 10Hz (100ms) | 120Hz (8.3ms) | 12x faster |
| Send rate | 30Hz (33ms) | 120Hz (8.3ms) | 4x faster |
| Client flush | ~17ms (next frame) | 0ms (same frame) | Eliminated |
| **Total latency** | ~58-115ms | **~50ms** | -14% to -57% |

## Double-Flush Mechanism

FishNet batches packets in `PacketBundle` and only sends them during `IterateOutgoing()` calls. By default, this happens once per network tick (~30Hz).

To achieve frame-rate flushing, we call both layers in `LateUpdate`:

```csharp
public void ForceIterateOutgoing()
{
    // STEP 1: Flush PacketBundle → Transport queue
    _networkManager.TransportManager.IterateOutgoing(asServer: false);

    // STEP 2: Flush Transport queue → Socket
    _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
}
```

**Why both steps?**
- `TransportManager.IterateOutgoing()` moves packets from PacketBundle to Transport
- `Transport.IterateOutgoing()` moves packets from Transport queue to OS socket

Without both, packets wait for the next tick cycle (adds ~17ms).

## Bandwidth Usage

### 120Hz Input Rate

| Metric | Value |
|--------|-------|
| Packet size | 14-18 bytes |
| Packets/sec | 120 (max, continuous input) |
| Bandwidth | ~2.2 KB/s per client (upload) |

This is negligible for modern connections.

## Debugging

### Enable Logging

Logs are active in `UNITY_EDITOR` and `DEVELOPMENT_BUILD`:

```
[1.283] [INPUT START] dir=(1.00, 0.00) speed=8.0
[1.283] [INTENT] +0ms MoveDir seq=1 dir=(1.00,0.00)
[1.283] [SEND ENQUEUE] C→S seq=1 intent=MoveDir
[1.283] [SOCKET SEND] C→S size=18
[1.300] [SERVER RECV] MoveDir seq=1 dir=(1.00,0.00)
[1.300] [SERVER APPLY] MoveDir seq=1 intraServerMs=17.32
[1.333] [SNAPSHOT] +50ms tick=22 vel=(8.00, -2.00, 0.00)
```

### Health Metrics

| Metric | Good | Warning | Bad |
|--------|------|---------|-----|
| INPUT → SOCKET SEND | 0ms (same frame) | <8ms | >16ms |
| SERVER RECV → APPLY | <20ms | <33ms | >50ms |
| Total input→snapshot | <60ms | <100ms | >150ms |

## Troubleshooting

### Symptom: Large delay between INPUT START and SOCKET SEND

**Cause:** Rate limiters not aligned or ForceIterateOutgoing not called
**Fix:**
1. Verify `INTENT_SEND_INTERVAL = 0.0083f` in IntentBuilder.cs
2. Verify `_inputSendRate = 120f` in NetworkClient.cs
3. Verify `ForceIterateOutgoing()` is called in LateUpdate

### Symptom: SOCKET SEND happens 1 frame after SEND ENQUEUE

**Cause:** PacketBundle not flushed properly
**Fix:** Verify double-flush calls both TransportManager AND Transport

### Symptom: High SERVER RECV → SERVER APPLY delay

**Cause:** Server tick rate too low
**Fix:** Consider increasing server tick rate to 60Hz

---

## Future Optimizations

The remaining ~50ms latency breaks down as:
- **Server poll delay:** ~17ms (server Update() timing)
- **IntraServer delay:** ~17ms (tick-based processing)
- **Snapshot delivery:** ~16ms (tick-based broadcast)

### Priority 1: Server Tick Rate 60Hz (Expected gain: -16ms)

Current server tick is 30Hz (33ms interval). Increasing to 60Hz would:
- Reduce intraServer delay: 17ms → 8ms
- Reduce snapshot broadcast delay: 16ms → 8ms
- **New total: ~34ms** (gain of -16ms)

**Files to modify:**
- `SimConfig.cs`: `TICK_RATE` constant
- `TimeManager` in FishNet NetworkManager inspector

### Priority 2: Client-Side Prediction (Expected gain: -50ms perceived)

Implement prediction so client sees movement immediately:
- Apply input locally before server confirmation
- Reconcile when server snapshot arrives
- **Perceived latency: 0ms** (actual still 50ms, but invisible)

**Complexity:** High - requires reconciliation logic and rollback

### Priority 3: Dedicated Input Socket (Expected gain: -10ms)

Bypass FishNet entirely for inputs:
- Raw UDP socket for input packets only
- Minimal protocol: seq + intent type + payload
- Keep FishNet for snapshots/events

**Trade-off:** More complexity, separate reliability handling

### Priority 4: Server-Side Input Prediction (Expected gain: variable)

Server predicts next input based on current velocity:
- Reduces impact of network jitter
- Smoother movement on high-latency connections

**Complexity:** Medium - requires prediction + correction logic

---

## Summary

| What We Achieved | Status |
|------------------|--------|
| 120Hz intent creation | ✅ Done |
| 120Hz network send | ✅ Done |
| Same-frame client flush | ✅ Done |
| Double-flush mechanism | ✅ Done |
| **50ms localhost latency** | ✅ Achieved |

| Future Work | Priority |
|-------------|----------|
| Server 60Hz tick rate | High |
| Client-side prediction | Medium |
| Dedicated input socket | Low |
| Server input prediction | Low |

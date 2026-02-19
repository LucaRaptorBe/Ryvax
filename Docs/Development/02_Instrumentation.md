# Instrumentation

**Logging and Diagnostics for Netcode Debugging (V5.0)**

This document describes the actual logging infrastructure in V5.0: which log points exist, whether they are active or commented out, and how to use them for end-to-end tracing.

---

## Overview

V5.0 has two distinct diagnostic systems:

| System | Class | Status | Purpose |
|--------|-------|--------|---------|
| Movement cycle logger | `MovementCycleLogger` | Active (calls exist), logs commented out | Traces the input → send → snapshot → render cycle |
| Triggered window logger | `MovementDebugger` | Active (logs unconditional when window open) | 200ms burst log on key release for stop-response diagnosis |
| RTT measurement | `NetworkPingMeasure` | Active (ping sends), result log commented out | Application-level round-trip time |
| Low-level socket traces | `FishNetAdapter`, `ServerGameLoop` | Commented out | Per-packet timestamps for latency analysis |

**Important:** All `Debug.Log` calls for the detailed packet-level traces are commented out in the source. They are not disabled by a runtime flag — the lines must be manually uncommented to activate them. Re-comment them before committing.

---

## MovementCycleLogger

**File:** `Assets/Scripts/Debug/MovementCycleLogger.cs`

A static class that traces the full movement lifecycle. Call sites are wired into the production code; the `Debug.Log` lines inside each method are commented out.

### Enable Flags

```csharp
MovementCycleLogger.Enabled = true;    // Master switch
MovementCycleLogger.LogInput    = true;  // Key press events
MovementCycleLogger.LogIntent   = true;  // Intent creation
MovementCycleLogger.LogNetwork  = true;  // Intent sent to server
MovementCycleLogger.LogSnapshot = true;  // Snapshot received
MovementCycleLogger.LogRender   = false; // Visual pos each frame (very spammy)
```

All flags default to the values above. The master switch `Enabled` guards every method.

### Active Call Sites

**`[INPUT START]`**
- **Called from:** `InputCollector` (key press detection)
- **Method:** `MovementCycleLogger.LogInputStart(Vector2 dir)`
- **Log line:** `[{Time.time:F3}] [INPUT START] dir={dir}` — **active** (not commented out)

**`[INTENT]`**
- **Called from:** Intent creation in `InputCollector`
- **Method:** `MovementCycleLogger.LogIntentCreated(...)`
- **Log line:** commented out inside method body (`MovementCycleLogger.cs:75`)

**`[SEND]`**
- **Called from:** `NetworkClient.SendInputUpdate()` (`NetworkClient.cs:234`) — called unconditionally every send interval
- **Method:** `MovementCycleLogger.LogIntentSent(uint movementSeq, PacketIntentType intentType, short payload0, short payload1)`
- **Log line:** commented out inside method body (`MovementCycleLogger.cs:93`)
- **Format when uncommented:** `[{time}] [SEND] {CycleTime} {intentType} seq={movementSeq} {payloadStr}`

**`[SNAPSHOT]`**
- **Called from:** `NetworkClient.OnSnapshotReceived()` (`NetworkClient.cs:374`) — local player only
- **Method:** `MovementCycleLogger.LogSnapshotReceived(uint serverTick, Vector3 serverPos, Vector3 serverVel, float serverTime)`
- **Log line:** commented out inside method body (`MovementCycleLogger.cs:103`)
- **Format when uncommented:** `[{time}] [SNAPSHOT] {CycleTime} tick={serverTick} pos={serverPos:F2} vel={serverVel:F2}`

**`[RENDER]`**
- **Called from:** rendering code
- **Method:** `MovementCycleLogger.LogRenderPosition(Vector3 visualPos)`
- **Log line:** commented out inside method body (`MovementCycleLogger.cs:116`)
- **Throttled:** only fires when `Time.frameCount % 60 == 0`

### Deprecated Methods (no-ops)

The following methods exist but have empty bodies and are marked `[Obsolete]`. Do not call them:

- `LogPrediction` — V4.x prediction system (removed)
- `LogAbsorb` — V4.x offset absorption (removed)
- `LogDecay` — V4.x offset decay (removed)
- `LogSnap` — V4.x snap events (removed)
- `LogCycleSummary` — V4.x cycle summary (removed)

---

## MovementDebugger

**File:** `Assets/Scripts/Debug/MovementDebugger.cs`

A triggered 200ms logging window. Unlike `MovementCycleLogger`, when the window is open, `MovementDebugger.Log()` calls `Debug.Log` unconditionally — no commented-out lines.

### Usage

```csharp
// 1. Open the window on key release (e.g., in InputCollector):
MovementDebugger.TriggerOnRelease(movementSeq, currentTick);

// 2. In any per-frame code during the window:
MovementDebugger.Log($"[RENDER] visualPos={_visualPos} serverPos={_lastServerPos} vel={_lastServerVel}");

// 3. Check expiry at the end of each frame or tick:
MovementDebugger.CheckExpiry();
```

### Output Format

```
=== MOVEMENT DEBUG START === seq=42 tick=310 time=5.123 ===
[RENDER] visualPos=(3.10, 0.00, 2.70) serverPos=(3.00, 0.00, 2.70) vel=(0.00, 0.00, 0.00)
... (repeats for ~200ms / ~12 ticks at 60Hz)
=== MOVEMENT DEBUG END === duration=0.2s ===
```

### State Fields

```csharp
MovementDebugger.IsLogging   // bool — true while window is open
MovementDebugger.LogEndTime  // float — Time.time when window closes
MovementDebugger.ReleaseSeq  // uint — movement seq at trigger
MovementDebugger.ReleaseTick // uint — server tick at trigger
```

---

## NetworkPingMeasure

**File:** `Assets/Scripts/Debug/NetworkPingMeasure.cs`

Measures application-level RTT. Automatically added to the `NetworkClient` GameObject in `Awake` (`NetworkClient.cs:136-146`).

### Mechanism

1. Every `_pingInterval` seconds (default 1s), sends a `GameCommand` with `Category = Ping, Action = Request` via `NetworkClient.SendEventCommand()`.
2. The server echoes it back as a `ReliableEvent` with `Type = Ping`.
3. `NetworkClient.OnEventReceived()` routes the pong to `NetworkPingMeasure.OnPongReceived()` (`NetworkClient.cs:404-409`).
4. RTT is computed as `(nowTicks - _lastPingSentTicks) * 1000.0 / Stopwatch.Frequency`.

### Activating the RTT Log

Uncomment line 74 in `NetworkPingMeasure.cs`:

```csharp
Debug.Log($"[METRIC B] [RTT] seq={pongSequence} rttMs={rttMs:F2}");
```

Expected output on localhost:
```
[METRIC B] [RTT] seq=1 rttMs=2.14
[METRIC B] [RTT] seq=2 rttMs=1.87
```

---

## Low-Level Socket Traces (FishNetAdapter / ServerGameLoop)

These are the most detailed logs — per-packet timestamps using `Stopwatch.GetTimestamp()`. All are commented out. Activate only for a short debugging session, never commit them uncommented.

### [SEND ENQUEUE] — Client side, input enqueue moment

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:244-247`

```csharp
// if (packet.MovementSeq > 0)
// {
//     Debug.Log($"[{UnityEngine.Time.time:F3}] [SEND ENQUEUE] C→S seq={packet.MovementSeq} intent={packet.IntentType} ticks={enqueueTicks}");
// }
```

Records `Stopwatch.GetTimestamp()` at the moment `SendInputPacket()` is called — before FishNet queues the broadcast. This is `T1` in latency calculations.

### [TRANSPORT RECV] — Server side, input receive moment

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:455-458`

```csharp
// if (packet.MovementSeq > 0)
// {
//     Debug.Log($"[{UnityEngine.Time.time:F3}] [TRANSPORT RECV] C→S seq={packet.MovementSeq} intent={packet.IntentType} socketTicks={recvSocketTicks}");
// }
```

Records `Stopwatch.GetTimestamp()` at the earliest possible point on the server (`recvSocketTicks` captured at line 445, before the version check). This is `T3` in latency calculations.

### [SERVER BROADCAST] — Server side, snapshot generation

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:353-358`

```csharp
// var firstPlayer = _simWorld.AllPlayers.GetEnumerator();
// if (firstPlayer.MoveNext())
// {
//     var vel = firstPlayer.Current.Transform.Velocity;
//     Debug.Log($"[{Time.time:F3}] [SERVER BROADCAST] tick={_simWorld.Clock.CurrentTick} vel=({vel.x:F1},{vel.z:F1})");
// }
```

Logs when a snapshot broadcast cycle begins. Uses Unity `Time.time`, not `Stopwatch`, so it cannot be directly correlated with `Stopwatch`-based timestamps but is sufficient for coarse timing.

Note: `[CLIENT RECV]` does **not** exist in `NetworkClient.cs`. There is no commented-out log in `OnSnapshotReceived()` for this label. Use `MovementCycleLogger.LogSnapshotReceived()` instead.

---

## Log Format Reference

### Direction Labels

| Label | Meaning |
|-------|---------|
| `C→S` | Client to Server (input packets) |
| `S→C{id}` | Server to specific client (snapshots/events) |

### Delivery Method

| Label | Transport |
|-------|-----------|
| `Unreliable` | UDP — movement inputs, snapshots |
| `Reliable` | TCP/ordered — events, pings, class assign |

### Timestamp Types

| Field | Source | Resolution |
|-------|--------|-----------|
| `ticks={N}` | `Stopwatch.GetTimestamp()` | ~100ns (Windows) |
| `[{time}]` prefix | `Time.time` | Frame resolution |

---

## Sequence Correlation

### Tracing an Input Packet End-to-End

Uncomment `[SEND ENQUEUE]` (FishNetAdapter) and `[TRANSPORT RECV]` (FishNetAdapter server-side).

```
[1.455] [SEND ENQUEUE] C→S seq=1 intent=MoveDir ticks=T1
[1.489] [TRANSPORT RECV] C→S seq=1 intent=MoveDir socketTicks=T3
```

**Client-to-server latency:** `(T3 - T1) * 1000 / Stopwatch.Frequency` ms

Note: there is no `[SOCKET SEND]` log in V5.0 (it was removed). T1 (enqueue) and T3 (server receive) are the available endpoints.

### Tracing a Snapshot End-to-End

Uncomment `[SERVER BROADCAST]` in `ServerGameLoop.cs`. Enable the `[SNAPSHOT]` log in `MovementCycleLogger`.

```
[2.100] [SERVER BROADCAST] tick=26 vel=(8.0, 0.0)
[2.117] [SNAPSHOT] tick=26 pos=(x, y, z) vel=(vx, vy, vz)
```

**Server-to-client latency:** delta between `Time.time` values (~17ms on localhost is one tick interval plus network).

### Detecting Missing Snapshots

With `[SNAPSHOT]` uncommented in `MovementCycleLogger`:

```
[1.000] [SNAPSHOT] tick=20 ...
[1.017] [SNAPSHOT] tick=21 ...
[1.034] [SNAPSHOT] tick=23 ...   ← tick 22 missing (UDP drop)
```

A gap of more than 1 tick indicates a dropped snapshot packet. The client continues dead-reckoning from `_lastServerPos + _lastServerVel * dt` until the next snapshot arrives.

---

## Timing Reference (Localhost)

| Measurement | Expected | Alarm Threshold |
|------------|---------|----------------|
| RTT (NetworkPingMeasure) | 1–5ms | >20ms |
| Input enqueue to server receive | ~16ms (1 tick) | >33ms (2 ticks) |
| Server broadcast to client snapshot log | ~16ms | >33ms |
| Snapshot interval | 16.7ms (1/60Hz) | >25ms (dropped tick) |

---

## Enabling Logs — Procedure

1. Open the relevant file.
2. Uncomment the `Debug.Log` line(s).
3. Run in Editor, reproduce the issue.
4. Copy the relevant log lines from the Console.
5. Re-comment the lines before committing.

Do not use `#if` guards or runtime flags for temporary investigation logs. The comment/uncomment approach is faster and leaves no production overhead.

---

## Related Documentation

- [01_Debugging_Guide.md](01_Debugging_Guide.md) — symptom-based debugging procedures
- [Network/02_Message_Specifications.md](../Network/02_Message_Specifications.md) — packet field layout
- [Network/03_Data_Flow.md](../Network/03_Data_Flow.md) — full input-to-visual pipeline

---

**Last Updated:** 2026-02-18

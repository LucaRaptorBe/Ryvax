# Debugging Guide

**Troubleshooting Netcode Issues in Ryvax (V5.0)**

This guide covers common netcode problems, debugging techniques, and tools for diagnosing network-related issues in the V5.0 simplified architecture.

---

## V5.0 Architecture Summary

Before debugging, understand what V5.0 actually does:

- **No client-side prediction.** The client has no shadow simulation.
- **No interpolation buffer.** There is no snapshot queue or `BaseInterpolator`.
- **Dead-reckoning only.** `EntityView.UpdatePosition()` extrapolates from the last received snapshot using `pos + vel * dt`.
- **Snap-on-stop.** When server velocity is ~0, `_visualPos` snaps directly to `_lastServerPos` to prevent sliding.
- **`VISUAL_SMOOTHING_SPEED`** (`NetcodeConstants.cs:92`, value `18f`) is the only tuning knob for smoothing while moving.
- **No CC tracking on the client.** `NetworkClient.IsImmobilized()` always returns `false` (`NetworkClient.cs:117`).

The full rendering path for any entity is:

```
Server tick → SnapshotDelta → NetworkClient.OnSnapshotReceived()
           → EntityView.OnSnapshotReceived() stores _lastServerPos, _lastServerVel, _lastSnapshotTime
           → EntityView.UpdatePosition() each frame: dead-reckoning + lerp
           → transform.position = _visualPos
```

---

## Common Problems

### Rubber-Banding

**Symptoms:**
- Character snaps backward or jumps to a different position
- Visual position appears unstable during movement
- Character stutters immediately after a direction change

**Cause in V5.0:**
Rubber-banding is almost always caused by `VISUAL_SMOOTHING_SPEED` being too low relative to the snapshot rate, or by the server position diverging significantly from where the client has extrapolated to.

Dead-reckoning code (`EntityView.cs:206-222`):
```csharp
float dt = Time.time - _lastSnapshotTime;
Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

if (_lastServerVel.sqrMagnitude < 0.01f)
    _visualPos = deadReckonedPos;   // Snap when stopped
else
    _visualPos = Vector3.Lerp(_visualPos, deadReckonedPos,
        NetcodeConstants.VISUAL_SMOOTHING_SPEED * Time.deltaTime);
```

**Debugging steps:**

1. **Check `VISUAL_SMOOTHING_SPEED`** (`NetcodeConstants.cs:92`). The default is `18f`. Lower values cause `_visualPos` to lag further behind the dead-reckoned position, making corrections after a snapshot more visible. Raise it to reduce lag; lower it for smoother (but slower) corrections.

2. **Check snapshot rate.** The server broadcasts at `SNAPSHOT_RATE = TICK_RATE = 60` Hz (`NetcodeConstants.cs:23-29`). If snapshots arrive less frequently (e.g., server under load), `dt` grows and the dead-reckoned position diverges more before the next correction.

3. **Add a temporary log in `EntityView.UpdatePosition()`:**
   ```csharp
   float gap = Vector3.Distance(_visualPos, deadReckonedPos);
   if (gap > 1.0f)
       Debug.LogWarning($"[EntityView] Large gap: {gap:F2} units, dt={dt:F3}s, vel={_lastServerVel}");
   ```

4. **Enable `MovementDebugger`** at the moment of a suspected rubber-band:
   ```csharp
   MovementDebugger.TriggerOnRelease(_movementSeq, tick);
   // Then in EntityView.UpdatePosition():
   MovementDebugger.Log($"[RENDER] visualPos={_visualPos} deadReck={deadReckonedPos} gap={gap:F2}");
   ```

**Solutions:**
- Rubber-banding on stop: expected and correct — snap-on-stop is intentional.
- Rubber-banding during movement: increase `VISUAL_SMOOTHING_SPEED`.
- Rubber-banding after direction change: the server corrected the entity; verify `MovementHandler.cs` is processing Stop intents promptly.

---

### Entity Stuck / Not Moving

**Symptoms:**
- Character does not move after right-click or WASD
- Snapshots arrive but `_visualPos` does not update

**Debugging steps:**

1. **Check `_hasSnapshot`** in `EntityView`. No snapshot has been received yet if the entity spawns before the first snapshot arrives. The guard at `EntityView.cs:203` silently no-ops until `_hasSnapshot` is true.

2. **Verify `OnSnapshotReceived` is being called.** Add a temporary log:
   ```csharp
   // In NetworkClient.OnSnapshotReceived() (NetworkClient.cs:351)
   Debug.Log($"[NetworkClient] Snapshot tick={snapshot.ServerTick} entities={snapshot.EntityCount}");
   ```

3. **Check server watchdog.** In `ServerGameLoop.cs`, if the server stops ticking (tick rate drops to 0), no snapshots are broadcast. The OnGUI overlay (`ServerGameLoop.cs:1021-1044`) shows the current tick number — verify it is incrementing.

4. **Check the `MovementCycleLogger`** output. `LogIntentSent` is called unconditionally at `NetworkClient.cs:234`. Enable the log inside `MovementCycleLogger.LogIntentSent()` by uncommenting line 93. Confirm intents are being sent.

---

### High Input Latency

**Symptoms:**
- Input feels sluggish (>200ms perceived delay)
- Character starts moving noticeably late after a click

**V5.0 latency budget:**
```
Input send (120Hz interval)    ~8ms max queue
Network up (localhost)         ~1ms
Server tick processing         ~16ms (1 tick @ 60Hz)
Snapshot broadcast             ~16ms (next broadcast after tick)
Network down (localhost)       ~1ms
Dead-reckoning extrapolation   ~0ms (happens next frame)
─────────────────────────────────────────────────────
Total (localhost)              ~42ms typical
```

**Debugging steps:**

1. **Verify input send rate.** `_inputSendRate` is `120f` Hz in `NetworkClient.cs:43`. The accumulator at line 220-222 gates sends. If `Time.deltaTime` spikes (frame drops), the next send is delayed by up to one frame.

2. **Check flush timing.** `NetworkClient.LateUpdate()` calls `_netAdapter.ForceIterateOutgoing()` once per frame (`NetworkClient.cs:199-203`). If LateUpdate is not called (script disabled, execution order issue), packets queue until FishNet's own flush.

3. **Measure RTT using `NetworkPingMeasure`.** The component auto-attaches to the `NetworkClient` GameObject (`NetworkClient.cs:136-146`). Uncomment the log at `NetworkPingMeasure.cs:74`:
   ```csharp
   Debug.Log($"[METRIC B] [RTT] seq={pongSequence} rttMs={rttMs:F2}");
   ```
   This gives application-level round-trip time. On localhost, expect <5ms. Over a real network, expect RTT + processing.

4. **Enable `[SEND ENQUEUE]` and `[TRANSPORT RECV]` logs.** See [02_Instrumentation.md](02_Instrumentation.md) for exact locations. Measure the delta between the two timestamps.

---

### Choppy Movement / Missing Snapshots

**Symptoms:**
- Entity teleports in discrete steps instead of moving smoothly
- `_lastSnapshotTime` shows large gaps between updates

**Cause:** UDP packet loss or server snapshot rate drop.

**Debugging steps:**

1. **Detect snapshot gaps.** Add a temporary field to track the last received tick:
   ```csharp
   // In NetworkClient:
   private uint _lastReceivedTick;

   // In OnSnapshotReceived, for local entity:
   if (state.EntityId == _localEntityId)
   {
       int gap = (int)snapshot.ServerTick - (int)_lastReceivedTick;
       if (gap > 1)
           Debug.LogWarning($"[NetworkClient] Missing {gap - 1} snapshots (last={_lastReceivedTick}, got={snapshot.ServerTick})");
       _lastReceivedTick = snapshot.ServerTick;
   }
   ```

2. **Check the server broadcast rate.** The `ServerGameLoop.OnGUI()` overlay (`ServerGameLoop.cs:1030`) displays `Snapshot Rate: {_snapshotRate} Hz`. Confirm it matches `SNAPSHOT_RATE` (60 Hz).

3. **Use `MovementDebugger` for a short window after a visible stutter:**
   ```csharp
   // Trigger when gap detected:
   MovementDebugger.TriggerOnRelease(seq, tick);
   MovementDebugger.Log($"[SNAPSHOT GAP] was {gap-1} ticks, dt={dt:F3}s");
   ```

---

## Debugging Tools

### ServerGameLoop.OnGUI Overlay

The server displays a live HUD overlay during play (`ServerGameLoop.cs:1021-1044`). It shows:

- Current server tick number
- Connected player count
- Snapshot broadcast rate in Hz
- AOI status: enabled/disabled, vision radius, average entity count per snapshot, AOI events per frame

This overlay runs only when the server is active (`_isRunning`). It is the fastest way to verify the server is ticking correctly.

---

### EntityView.OnDrawGizmos

`EntityView.OnDrawGizmos()` (`EntityView.cs:285-292`) draws in the Scene view:

```csharp
Gizmos.color = _isLocalPlayer ? Color.green : Color.blue;
Gizmos.DrawWireSphere(_targetPosition, 0.3f);
Gizmos.DrawLine(transform.position, _targetPosition);
```

- **Wire sphere at `_targetPosition`:** green for the local player, blue for remote players.
- **Line from `transform.position` to `_targetPosition`:** shows how far the rendered position is from the target.

Note: `_targetPosition` is set by subclasses (e.g., `PlayerView`). The base `EntityView` uses `_visualPos` as `transform.position`. To also visualize the raw dead-reckoned position, add a temporary log or a second gizmo sphere at `_lastServerPos + _lastServerVel * (Time.time - _lastSnapshotTime)`.

---

### MovementCycleLogger

`MovementCycleLogger` (`Assets/Scripts/Debug/MovementCycleLogger.cs`) is a static class with per-category enable flags. All log calls are present in the codebase but the `Debug.Log` lines inside each method are commented out. To activate them, uncomment the relevant line in the method body.

**Active call sites:**
- `MovementCycleLogger.LogIntentSent(...)` — called unconditionally in `NetworkClient.SendInputUpdate()` (`NetworkClient.cs:234`)
- `MovementCycleLogger.LogSnapshotReceived(...)` — called for the local entity in `NetworkClient.OnSnapshotReceived()` (`NetworkClient.cs:374`)

**Categories and flags:**

| Flag | Default | What it logs |
|------|---------|--------------|
| `MovementCycleLogger.LogInput` | true | Key press start (`[INPUT START]`) |
| `MovementCycleLogger.LogIntent` | true | Intent creation (`[INTENT]`) — commented out |
| `MovementCycleLogger.LogNetwork` | true | Intent sent to server (`[SEND]`) — commented out |
| `MovementCycleLogger.LogSnapshot` | true | Snapshot received (`[SNAPSHOT]`) — commented out |
| `MovementCycleLogger.LogRender` | false | Visual position each frame (`[RENDER]`) — very spammy, commented out |

To enable a category, set the flag and uncomment the `Debug.Log` line inside the corresponding method.

**Deprecated methods** (marked `[Obsolete]`, do nothing):
- `LogPrediction`, `LogAbsorb`, `LogDecay`, `LogSnap`, `LogCycleSummary` — these are V4.x remnants with empty bodies.

---

### MovementDebugger

`MovementDebugger` (`Assets/Scripts/Debug/MovementDebugger.cs`) activates a 200ms logging window triggered on key release. Use it to diagnose residual movement or stop-response issues.

**Usage:**
```csharp
// Trigger when Stop intent is sent (e.g., in InputCollector on key release):
MovementDebugger.TriggerOnRelease(_movementSeq, currentTick);

// In any per-frame code (EntityView.UpdatePosition, NetworkClient.OnSnapshotReceived):
MovementDebugger.Log($"[RENDER] visualPos={_visualPos} serverPos={_lastServerPos} vel={_lastServerVel}");

// At the end of each frame/tick:
MovementDebugger.CheckExpiry();
```

The window is 200ms (~12 server ticks at 60Hz). Output:
```
=== MOVEMENT DEBUG START === seq=42 tick=310 time=5.123 ===
[RENDER] visualPos=(3.1, 0.0, 2.7) serverPos=(3.0, 0.0, 2.7) vel=(0.0, 0.0, 0.0)
...
=== MOVEMENT DEBUG END === duration=0.2s ===
```

---

### NetworkPingMeasure

`NetworkPingMeasure` (`Assets/Scripts/Debug/NetworkPingMeasure.cs`) measures application-level RTT using `Stopwatch`. It auto-attaches to the `NetworkClient` GameObject on `Awake` (`NetworkClient.cs:136-146`).

- Sends a `Ping` command via the event buffer every `_pingInterval` seconds (default 1s).
- Receives the pong via `ReliableEvent.Ping` in `NetworkClient.OnEventReceived()` (`NetworkClient.cs:404-409`).
- Calculates RTT as `(nowTicks - _lastPingSentTicks) * 1000.0 / Stopwatch.Frequency`.
- The result log at `NetworkPingMeasure.cs:74` is commented out — uncomment to see it.

This is independent of FishNet's `TimeManager` and measures the full round-trip through the application stack.

---

## Profiling

**Unity Profiler markers to add for hotspot analysis:**

```csharp
// In ServerGameLoop.FixedUpdate tick loop:
using (new Unity.Profiling.ProfilerMarker("ServerGameLoop.Tick").Auto())
{
    _simWorld.Tick(Time.fixedDeltaTime);
}

// In EntityView.UpdatePosition():
using (new Unity.Profiling.ProfilerMarker("EntityView.UpdatePosition").Auto())
{
    // ... existing code
}
```

**Targets:**
- `ServerGameLoop.Tick`: <10ms (60Hz budget is 16.67ms; target leaves headroom)
- `EntityView.UpdatePosition`: <0.1ms per entity

---

## Common Fixes Reference

| Problem | Root Cause | Where to Look |
|---------|-----------|---------------|
| Rubber-banding during movement | `VISUAL_SMOOTHING_SPEED` too low | `NetcodeConstants.cs:92` |
| Character slides after stop | Snap-on-stop not triggering | `EntityView.cs:211` — verify `sqrMagnitude < 0.01f` |
| Entity stuck after spawn | First snapshot not yet received | `EntityView._hasSnapshot` — check `OnSnapshotReceived` is called |
| High input latency | Frame drops delaying send accumulator | `NetworkClient.cs:220-222`, check FPS |
| LateUpdate flush not firing | Script execution order or disabled component | `NetworkClient.LateUpdate` (`NetworkClient.cs:193-204`) |
| Choppy movement (packet loss) | UDP drops, no dead-reckoning for gaps >1 tick | Add snapshot-gap detection log in `NetworkClient.OnSnapshotReceived` |
| Server tick visible in OnGUI | Verify tick counter increments | `ServerGameLoop.OnGUI` (`ServerGameLoop.cs:1028`) |

---

## Troubleshooting Workflow

1. **Identify the symptom.** Rubber-banding? Stuck? High latency? Choppy?
2. **Check the server OnGUI overlay.** Confirm tick is advancing and snapshot rate is correct.
3. **Enable `NetworkPingMeasure` log.** Establish a baseline RTT.
4. **Narrow the layer.** Is the problem in input sending, network transport, snapshot processing, or `EntityView` rendering?
5. **Add a targeted temporary log** at the suspected location. Follow the data flow:
   `InputCollector → NetworkClient.SendInputUpdate → FishNetAdapter.SendInputPacket → [network] → FishNetAdapter.OnInputPacketReceived → ServerGameLoop → BroadcastSnapshots → NetworkClient.OnSnapshotReceived → EntityView.OnSnapshotReceived → EntityView.UpdatePosition`
6. **Use `MovementDebugger`** for stop/start residual issues.
7. **Remove all temporary logs** once the issue is confirmed fixed.

---

**Last Updated:** 2026-02-18

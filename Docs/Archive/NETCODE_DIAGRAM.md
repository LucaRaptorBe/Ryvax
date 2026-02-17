# Netcode Architecture Diagram

## Overview
This document describes the actual client-server netcode flow based on code analysis.

---

## Key Timing Parameters

**From code analysis:**
- **Unity FixedUpdate**: 50Hz (20ms) - `ProjectSettings/TimeManager.asset:Fixed Timestep: 0.02`
- **Server Tick Rate**: 30Hz (33.33ms) - `NetcodeConstants.TICK_RATE = 30`
- **Snapshot Rate**: 30Hz (33.33ms) - `NetcodeConstants.SNAPSHOT_RATE = 30`
- **Client Input Send Rate**: 30Hz (33.33ms) - `NetworkClient.cs:52 _inputSendRate = 30f`

**Critical Finding:**
- Server runs in Unity FixedUpdate (50Hz) but accumulates time to run simulation ticks at 30Hz
- TickClock.Accumulate() determines how many ticks to run each FixedUpdate
- On average: 1 tick every ~1.67 FixedUpdates

---

## Client-Server Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ CLIENT (NetworkClient.cs)                                               │
└─────────────────────────────────────────────────────────────────────────┘

  User presses W
       │
       ↓
  InputCollector.Update()
  - Detects input change
  - Calls IntentBuilder.BuildIntent()
       │
       ↓
  InputIntent created (MoveTo/Stop/Follow)
  - Type: MoveTo
  - WorldPos: target position
  - SendInputIntent() queues intent
       │
       ↓ (Rate limited to 30Hz)
  SendInputUpdate() - NetworkClient.cs:247
  - _inputSendAccumulator += Time.deltaTime
  - if (_inputSendAccumulator >= _inputSendInterval):
       │
       ↓
  CreateInputPacketFromIntent() - NetworkClient.cs:280
  - Converts intent to InputPacket
  - MovementSeq++ (monotonic)
  - MoveX/MoveZ = target position * 10 (quantized)
       │
       ↓
  _netAdapter.SendInputPacket(packet)
  - UDP unreliable send
  - Immediate transmission (no buffering)
       │
       ↓ Network (ping ~1ms localhost)
       ↓

┌─────────────────────────────────────────────────────────────────────────┐
│ SERVER (ServerGameLoop.cs)                                              │
└─────────────────────────────────────────────────────────────────────────┘

  InputPacket arrives (network callback - async)
       │
       ↓
  OnMovementStateReceived() - ServerGameLoop.cs:457
  - Called IMMEDIATELY when packet arrives
  - Sequence validation (monotonic check)
  - player.SetMoveDirection(dir3D)  ← Applied INSTANTLY
  - Does NOT wait for next tick!
       │
       │ (Player direction is set, but position not updated yet)
       ↓
  [Waiting for next simulation tick...]
       │
       ↓
  FixedUpdate() - runs at 50Hz (20ms intervals)
  ├─→ RunSimulation() - ServerGameLoop.cs:203
  │   ├─→ ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime)
  │   │   - Accumulates 0.02s (20ms)
  │   │   - Returns 1 tick when accumulator >= 0.0333s (33.33ms)
  │   │   - Average: 1 tick every ~1.67 FixedUpdates
  │   │
  │   └─→ for (i = 0; i < ticksToRun; i++):
  │       └─→ _simWorld.Step()
  │           - Applies movement (position += velocity * dt)
  │           - Uses the direction set by OnMovementStateReceived()
  │           - Clock.Advance() increments tick
  │
  ├─→ TickWatchdog(dt)
  │   - Force stop if no movement for 300ms
  │
  └─→ BroadcastSnapshots() - ServerGameLoop.cs:250
      - _snapshotAccumulator += Time.fixedDeltaTime
      - if (_snapshotAccumulator >= _snapshotInterval):
           │
           ↓
      SnapshotHelper.CreateSnapshot()
      - Collects all entity states
      - Current tick, positions, velocities
      - movementSeq (last processed from client)
           │
           ↓
      _netAdapter.SendToClient(clientId, snapshot)
      - UDP unreliable send
           │
           ↓ Network (ping ~1ms localhost)
           ↓

┌─────────────────────────────────────────────────────────────────────────┐
│ CLIENT (NetworkClient.cs)                                               │
└─────────────────────────────────────────────────────────────────────────┘

  OnSnapshotReceived() - NetworkClient.cs:445
       │
       ↓
  For local player:
  - Convert EntityState → SnapshotState
  - _visualPositionManager.OnSnapshotReceived()
       │
       ↓
  VisualPositionManager processes snapshot:
  - Updates basePos (interpolated server position)
  - Calculates gap = visualPos - basePos
  - Applies correction (smooth/accel/snap based on gap size)
       │
       ↓
  Update() renders at monitor refresh rate (60Hz+)
  - _visualPositionManager.Update(deltaTime)
  - Smooth visual interpolation
  - visualPos = basePos + visualOffset
```

---

## Timing Analysis: Why 117ms?

**Based on logs and code:**

```
Time    Event                           Location                           Notes
────────────────────────────────────────────────────────────────────────────────
+0ms    INTENT CREATED                  InputCollector                     User presses W
+0ms    NETWORK SEND (seq=1)           NetworkClient.SendInputUpdate()    Sent immediately
+1ms    Server receives packet          FishNetAdapter (network callback)  Ping = 1ms
+1ms    OnMovementStateReceived()       ServerGameLoop.cs:457              Direction applied!
        [Direction is set but position unchanged - waiting for tick]

        FixedUpdate Loop (50Hz = 20ms intervals):
+0ms    FixedUpdate #1                  accumulator = 0.02, no tick
+20ms   FixedUpdate #2                  accumulator = 0.04, TICK! (position updates)
+40ms   FixedUpdate #3                  accumulator = 0.0067, no tick
+60ms   FixedUpdate #4                  accumulator = 0.0267, no tick
+80ms   FixedUpdate #5                  accumulator = 0.0467, TICK!
+100ms  FixedUpdate #6                  accumulator = 0.0133, no tick
+117ms  Snapshot broadcast              BroadcastSnapshots()               Snapshot sent
+118ms  Client receives                 NetworkClient.OnSnapshotReceived() 117ms total!
```

**Root cause:**
1. Input applied immediately at +1ms (OnMovementStateReceived)
2. But position only updates when _simWorld.Step() runs (at 30Hz ticks)
3. TickClock accumulator means ticks are irregular relative to FixedUpdate
4. Snapshot broadcast happens AFTER tick completes
5. Total delay = input arrival → next tick → snapshot broadcast → network → client

**Why not faster:**
- Server tick rate is 30Hz (33.33ms intervals)
- Input can arrive at any point in the tick cycle
- Worst case: arrives just after a tick (wait 33ms)
- Average case: arrives mid-tick (wait ~16ms)
- Plus snapshot broadcast timing + network RTT

---

## Code References

### Server Timing
- `ServerGameLoop.cs:159` - FixedUpdate() runs at 50Hz
- `ServerGameLoop.cs:206` - TickClock.Accumulate(Time.fixedDeltaTime)
- `TickClock.cs:67-88` - Accumulate() implementation
- `ServerGameLoop.cs:457` - OnMovementStateReceived() - IMMEDIATE application
- `ServerGameLoop.cs:212` - _simWorld.Step() - position update

### Client Timing
- `NetworkClient.cs:247` - SendInputUpdate() - 30Hz rate limiting
- `NetworkClient.cs:280` - CreateInputPacketFromIntent()
- `NetworkClient.cs:445` - OnSnapshotReceived()

### Constants
- `NetcodeConstants.cs:24` - TICK_RATE = 30
- `NetcodeConstants.cs:31` - SNAPSHOT_RATE = 30
- `ProjectSettings/TimeManager.asset` - Fixed Timestep: 0.02 (50Hz)

---

## Notes

**TARGET_INPUTS_IN_FLIGHT is NOT used in LoL-style architecture:**
- `ClientClockSync.cs` exists but is never instantiated in NetworkClient.cs
- Designed for FPS-style architecture with client-side prediction
- Would synchronize: CLIENT tick clock (if it existed) with SERVER
- How: Adjust client tick rate (0.8x-1.2x) to maintain ~2 inputs in flight
- Why not used: LoL-style has NO client simulation (NetworkClient.cs:226-230 FixedUpdate empty)
- Current client: Only renders visually (Update() at 60Hz+), no local ticks
- Input send: Simple 30Hz rate limiter (not clock sync)

**Semi-Stateless Movement:**
- Movement state applied immediately when received (ServerGameLoop.cs:482)
- Not buffered or delayed
- Monotonic sequence prevents out-of-order packets (line 470)
- Direction is "current truth", position updated next tick

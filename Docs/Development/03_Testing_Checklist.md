# Testing Checklist

**Manual Testing Procedures for LoL-Style Netcode (V5.0)**

This checklist provides step-by-step verification procedures for testing the netcode implementation. Use this after making changes or before releasing a new build.

---

## Quick Start

**Test Environment:**
- Run server and client on same machine (localhost)
- Use Unity Editor for both, or build + editor
- Monitor Console logs for errors/warnings

**Required Setup:**
1. FishNet configured (60Hz tick rate)
2. Server scene loaded (`Bootstrap.unity`)
3. Client connects successfully
4. At least one player spawned and visible

---

## Basic Verification

### Input Handling

**Test 1: Right-Click Movement**

**Steps:**
1. Right-click on ground — character should turn toward click position
2. Wait ~100ms — character should begin moving toward clicked position
3. Character should move smoothly along a straight line
4. Right-click a new position mid-movement — character should change course

**Expected:**
- ✅ Immediate rotation toward click point (local feedback)
- ✅ Position starts updating within 100ms
- ✅ Smooth movement, no stuttering
- ✅ Direction changes are smooth

**Failure Modes:**
- ❌ No rotation — Input not captured by `InputCollector`
- ❌ No movement — Server not processing `MoveTo` intent
- ❌ Stuttering — Snapshot loss or `VISUAL_SMOOTHING_SPEED` too low
- ❌ Snapping on direction change — `_visualPos` lerp not converging fast enough; check `NetcodeConstants.VISUAL_SMOOTHING_SPEED` (`NetcodeConstants.cs:92`, default `18f`)

---

**Test 2: WASD Movement**

**Steps:**
1. Press W — character should face forward and begin moving
2. Hold W for 2 seconds — character should continue moving smoothly
3. Release W — character should stop within ~100ms
4. Press A/S/D — character should turn and move in the new direction

**Expected:**
- ✅ Immediate rotation (local feedback)
- ✅ Continuous movement while held
- ✅ Clean stop response (<100ms)
- ✅ Direction changes are fluid

**Failure Modes:**
- ❌ Delayed rotation — `IntentBuilder` not producing local rotation feedback
- ❌ Choppy movement — `MoveDir` intents not sending at 120Hz; check `_inputSendRate` (`NetworkClient.cs:43`)
- ❌ Doesn't stop — `Stop` intent not sent on key release
- ❌ Slides after release — snap-on-stop not triggering; verify `_lastServerVel.sqrMagnitude < 0.01f` check at `EntityView.cs:211`

---

**Test 3: Input Spam**

**Steps:**
1. Rapidly press W-S-A-D in random sequence for 5 seconds
2. Spam right-click on different positions
3. Alternate between WASD and click movement

**Expected:**
- ✅ No crashes or errors
- ✅ Character follows most recent input
- ✅ No runaway movement (character eventually stops)
- ✅ Frame rate remains stable (>50fps)

**Failure Modes:**
- ❌ Crash — Input rate-limiting failure
- ❌ Runaway — Stop intents not sent on input release
- ❌ FPS drop — Too many packets or processing overhead

---

## Visual Rendering

### Dead-Reckoning and Snap Smoothing

**Test 4: Movement Smoothness**

**Steps:**
1. Right-click far away — character begins moving
2. Open Scene view — observe the wire sphere at `_targetPosition` (green for local player, blue for remote) drawn by `EntityView.OnDrawGizmos()` (`EntityView.cs:285-292`)
3. Observe the line from `transform.position` to `_targetPosition`
4. Verify the line stays short during normal movement

**Expected:**
- ✅ Wire sphere closely tracks the character's rendered position
- ✅ Line between rendered position and target position stays short (<1 unit during smooth movement)
- ✅ No visible "rubber-banding" during continuous movement
- ✅ Position updates are smooth (60fps perception)

**Notes on the gizmo:**
- `_targetPosition` is updated by subclasses (e.g., `PlayerView`). It represents where the entity is heading, not a separate "base position" from an interpolation buffer.
- `transform.position` is `_visualPos` — the lerped dead-reckoned position updated every frame in `EntityView.UpdatePosition()`.
- There is **no** green "base position" sphere and no blue "visual position" sphere as separate gizmos. The single sphere is color-coded by player ownership.

**Failure Modes:**
- ❌ Sphere far from character — `_targetPosition` not being updated
- ❌ Long line visible — `_visualPos` lerp lagging; increase `VISUAL_SMOOTHING_SPEED`
- ❌ Choppy position updates — Snapshot loss or server tick dropping

---

**Test 5: Stop Behavior (Snap-On-Stop)**

**V5.0 behavior:** When `_lastServerVel.sqrMagnitude < 0.01f`, `_visualPos` is set directly to the dead-reckoned position with no lerp (`EntityView.cs:211-214`). This prevents the character sliding past the stop point.

**Steps:**
1. Hold W and release cleanly
2. Observe the character stopping
3. Verify the character stops at the server position without overshooting

**Expected:**
- ✅ Character stops promptly (within the next frame after server confirms velocity = 0)
- ✅ No sliding past the intended stop position
- ✅ No visible "rubber-band back" to the server position

**Failure Modes:**
- ❌ Character slides after release — Server velocity not yet 0 in the snapshot; wait for the Stop intent to reach the server and be processed (up to ~50ms on localhost)
- ❌ Character snaps backward on stop — `_visualPos` was ahead of `_lastServerPos` due to dead-reckoning; snap-on-stop is working correctly but the gap was large. Reduce `VISUAL_SMOOTHING_SPEED` if this is consistently jarring, or investigate why `_lastServerVel` was non-zero for an extended time after the key was released

---

## Special Cases

### Crowd Control (CC)

**Test 6: Immobilize Effects**

**Steps:**
1. Trigger root/stun on a character (via server command or ability)
2. Try to move via WASD or right-click
3. Observe character state

**Expected:**
- ✅ Character stops moving (server stops applying velocity)
- ✅ Position stabilizes at server position (snap-on-stop fires because server velocity becomes 0)

**Known V5.0 Limitation:**
- `NetworkClient.IsImmobilized()` always returns `false` (`NetworkClient.cs:117`). CC tracking was removed in V5.0. There is no client-side immobilize lock. The `IntentBuilder` does not suppress movement inputs during CC — the server simply ignores them.
- The visible result is still correct: the server stops the entity and the client dead-reckoning settles to the stopped position. But the client will continue sending movement intents during a CC; they will be dropped by the server.

**Failure Modes:**
- ❌ Character continues moving visually — Server is not applying CC; check `ServerGameLoop`/`MovementHandler` CC logic
- ❌ Snapshots show non-zero velocity during CC — Server bug; not a client issue

---

### Discontinuities

**Test 7: Teleport**

**Steps:**
1. Trigger teleport (move character 50+ units instantly via server command)
2. Observe client behavior

**Expected:**
- ✅ Character instantly jumps to the new position
- ✅ No interpolation through intermediate positions
- ✅ Dead-reckoning restarts correctly from the new position

**V5.0 behavior:** `EntityView.Teleport()` (`EntityView.cs:172-183`) hard-sets `transform.position`, `_lastServerPos`, `_lastServerVel = zero`, and `_lastSnapshotTime = Time.time`. `_visualPos` is not directly set by `Teleport()` — it will converge on the next `UpdatePosition()` call, which immediately snaps because velocity is 0.

**Failure Modes:**
- ❌ Character interpolates through old positions — `Teleport()` not being called; verify the `EntityRespawn` / teleport event reaches `EntityView`
- ❌ Character appears at wrong position — Position encoding mismatch; check `ReliableEvent` decode in `NetworkClient.OnEntityRespawn()` (`NetworkClient.cs:643-661`)

---

**Test 8: Blink/Dash Ability**

**Steps:**
1. Trigger a blink/dash ability (short-range instant position change)
2. Verify the visual snaps to the new position

**Expected:**
- ✅ Instant position change visible on client
- ✅ No rubber-band back to pre-blink position
- ✅ Movement resumes correctly from new position

**V5.0 behavior:** No special discontinuity handling is needed. The next snapshot from the server carries the post-blink position and velocity. Because velocity is typically 0 immediately after a blink, snap-on-stop fires and `_visualPos` moves directly to the server position.

**Failure Modes:**
- ❌ Rubber-band back — Next snapshot has position before blink; the server may not have processed the blink yet
- ❌ Interpolates during blink — `VISUAL_SMOOTHING_SPEED` too low; the lerp hasn't converged before the next snapshot

---

### Network Conditions

**Test 9: Simulated Packet Loss**

**Steps:**
1. Enable FishNet's packet loss simulation (5–10%) in the FishNet NetworkManager inspector
2. Move character continuously
3. Observe movement smoothness

**Expected:**
- ✅ Movement remains reasonably smooth (dead-reckoning fills gaps)
- ✅ Occasional visible correction when snapshot arrives after a drop
- ✅ No freezing or hard stops during normal movement

**V5.0 behavior:** There is no extrapolation quality flag or `BasePosQuality` enum. The client simply continues `pos + vel * dt` for as long as no new snapshot arrives. If velocity was non-zero when the snapshot was dropped, the character continues in the same direction until corrected.

**Failure Modes:**
- ❌ Character freezes during packet loss — Dead-reckoning not running; verify `_hasSnapshot` is true and `UpdatePosition()` is being called
- ❌ Large snap on recovery — The dead-reckoned position diverged from the server position; this is expected for high loss rates or low `VISUAL_SMOOTHING_SPEED`

---

**Test 10: High Latency**

**Steps:**
1. Enable FishNet's latency simulation (150ms added delay)
2. Move character via WASD
3. Measure perceived delay from key press to visual movement

**Expected:**
- ✅ Input feels delayed (~150ms + 1 tick processing delay)
- ✅ Movement is still smooth after it starts (dead-reckoning continues between snapshots)
- ✅ No stuttering due to latency alone

**V5.0 behavior:** There is no adaptive buffer and no `TimeSync` system. Latency directly increases the delay from input to server response. The client dead-reckons continuously, so smoothness between snapshots is not affected by latency — only the round-trip time before movement begins.

**Failure Modes:**
- ❌ Stuttering — Check snapshot receipt rate; high latency should not cause stuttering on its own
- ❌ Rubber-banding — Large corrections due to dead-reckoning divergence over long RTT; increase `VISUAL_SMOOTHING_SPEED` or accept as expected at high latency

---

**Test 11: High Jitter**

**Steps:**
1. Enable FishNet's jitter simulation (±50ms variance)
2. Move character in circles
3. Observe visual smoothness

**Expected:**
- ✅ Movement remains reasonably smooth (dead-reckoning bridges variable-interval snapshots)
- ✅ Occasional visible corrections when jitter causes a large gap between snapshots

**V5.0 behavior:** There is no adaptive buffer, no `TimeSync`, and no jitter compensation. Dead-reckoning handles jitter naturally up to the point where `dt` becomes large enough that the extrapolated position diverges significantly. The higher the jitter, the more visible the corrections.

**Failure Modes:**
- ❌ Choppy movement — Verify `VISUAL_SMOOTHING_SPEED` is not too low; low values cause slow convergence after each corrected snapshot

---

## Performance Testing

### Bandwidth

**Test 12: Upload Bandwidth**

**Steps:**
1. Move character continuously for 30 seconds
2. Monitor network stats (via FishNet profiler or Wireshark)
3. Calculate bytes/second

**Expected:**
- ✅ Upload: ~1.5–2 KB/s (120Hz × ~14 bytes per input packet)
- ✅ No burst spikes
- ✅ Rate-limited to 120Hz (`_inputSendRate` in `NetworkClient.cs:43`)

**Failure Modes:**
- ❌ >5 KB/s — Sending too frequently or duplicate packets
- ❌ Bursts — Send accumulator not working; check `NetworkClient.SendInputUpdate()` (`NetworkClient.cs:216-237`)

---

**Test 13: Download Bandwidth**

**Steps:**
1. Stand still with 5 other moving entities visible
2. Monitor download bandwidth
3. Calculate bytes/second

**Expected:**
- ✅ Download: ~15–20 KB/s (60Hz × 5 entities × ~50 bytes per snapshot entity state)
- ✅ Scales linearly with visible entity count
- ✅ AOI culling reduces load when entities are far (verify AOI is ON in `ServerGameLoop.OnGUI` overlay)

**Failure Modes:**
- ❌ >50 KB/s — Not culling invisible entities; check AOI system in `ServerGameLoop`
- ❌ No scaling — AOI not working

---

### Frame Rate

**Test 14: Client Frame Rate**

**Steps:**
1. Run client with 10+ entities visible
2. Monitor FPS (Stats window)
3. Move around, change camera angle

**Expected:**
- ✅ Client FPS >60 (should be 100+)
- ✅ No frame drops during movement
- ✅ Consistent frame time

**Failure Modes:**
- ❌ <50 FPS — Rendering bottleneck or too much per-frame processing in `EntityView.Update()`
- ❌ Frame drops — GC spikes (check for allocations in hot paths) or sync stalls

---

**Test 15: Server Tick Performance**

**Steps:**
1. Run server with 10+ simulated players
2. Monitor server tick time (via Unity Profiler or `ServerGameLoop.OnGUI` overlay)
3. Check for tick overruns

**Expected:**
- ✅ Tick time <10ms (60Hz budget is 16.67ms; leave headroom)
- ✅ No warnings about tick overruns
- ✅ Consistent tick intervals
- ✅ `ServerGameLoop.OnGUI` shows tick counter incrementing at ~60/s

**Failure Modes:**
- ❌ >16ms — Simulation too slow; profile `SimWorld.Tick()` to find bottleneck
- ❌ Tick counter stops incrementing — Server stopped; check for exceptions in Console

---

## Acceptance Criteria

Before merging or releasing:

### Critical (Must Pass)
- [ ] All basic input tests pass (Tests 1–3)
- [ ] Movement smoothness is acceptable (Test 4)
- [ ] Stop behavior is clean with no sliding (Test 5)
- [ ] No crashes under input spam (Test 3)
- [ ] Client FPS >60 with 10 entities (Test 14)
- [ ] Server tick time <16ms with 10 players (Test 15)

### Important (Should Pass)
- [ ] WASD movement feels responsive (Test 2)
- [ ] Teleport snaps correctly (Test 7)
- [ ] Packet loss handled by dead-reckoning (Test 9)
- [ ] Bandwidth within expected range (Tests 12–13)

### Nice to Have (May Fail)
- [ ] High latency still playable (Test 10)
- [ ] Jitter produces minimal visible artifacts (Test 11)
- [ ] Blink/dash snaps cleanly (Test 8)

---

## Regression Testing

After any change to:

**GameSim (`SimWorld`, `MovementHandler`):**
- Re-run Tests 1, 2, 15

**Network (`FishNetAdapter`, Messages):**
- Re-run Tests 1, 9, 12, 13

**Client rendering (`EntityView`, `PlayerView`):**
- Re-run Tests 4, 5, 7, 9, 10, 11

**Input (`InputCollector`, `IntentBuilder`):**
- Re-run Tests 1, 2, 3

---

## Debugging Failed Tests

When a test fails:

1. **Check the `ServerGameLoop.OnGUI` overlay** — confirm tick is advancing and snapshot rate is correct
2. **Enable `NetworkPingMeasure` log** — establish baseline RTT (uncomment `NetworkPingMeasure.cs:74`)
3. **Enable `MovementCycleLogger`** category logs as needed (uncomment inside method bodies in `MovementCycleLogger.cs`)
4. **Use `MovementDebugger`** for stop/start response issues — `MovementDebugger.TriggerOnRelease(seq, tick)`
5. **Add targeted temporary log** at the suspected location in `EntityView.UpdatePosition()` or `NetworkClient.OnSnapshotReceived()`
6. **Profile with Unity Profiler** if performance-related
7. **Remove all temporary logs** before committing

See [01_Debugging_Guide.md](01_Debugging_Guide.md) for symptom-specific procedures and [02_Instrumentation.md](02_Instrumentation.md) for log activation instructions.

---

## Automated Testing (Future)

These manual tests should eventually be automated:

**Unit Tests:**
- `InputIntent` serialization/deserialization
- `InputPacket` creation from each `InputIntentType`
- `NetcodeConstants` derived value calculations

**Integration Tests:**
- Simulated client-server snapshot round-trip
- Dead-reckoning accuracy over N ticks with known velocity
- Sequence number acknowledgment via `_eventBuffer`

**Performance Tests:**
- Bandwidth usage under various entity counts
- Server tick time scaling with player count
- Client frame rate with stress load

---

**Last Updated:** 2026-02-18

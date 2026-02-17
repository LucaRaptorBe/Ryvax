# Testing Checklist

**Manual Testing Procedures for LoL-Style Netcode**

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
1. Right-click on ground → character should turn toward click position
2. Wait ~100ms → character should begin moving toward clicked position
3. Character should move smoothly along straight line
4. Right-click new position mid-movement → character should change course

**Expected:**
- ✅ Immediate rotation toward click point (local feedback)
- ✅ Position starts updating within 100ms
- ✅ Smooth movement, no stuttering
- ✅ Direction changes are smooth, not snapping

**Failure Modes:**
- ❌ No rotation → Input not captured
- ❌ No movement → Server not processing MoveTo intent
- ❌ Stuttering → Snapshot loss or interpolation issues
- ❌ Snapping on direction change → visualOffset not smooth

---

**Test 2: WASD Movement**

**Steps:**
1. Press W → character should face forward and begin moving
2. Hold W for 2 seconds → character should continue moving smoothly
3. Release W → character should stop within 100ms
4. Press A/S/D → character should turn and move in new direction

**Expected:**
- ✅ Immediate rotation (local feedback)
- ✅ Continuous movement while held
- ✅ Quick stop response (<100ms)
- ✅ Direction changes are fluid

**Failure Modes:**
- ❌ Delayed rotation → IntentBuilder not feeding local feedback
- ❌ Choppy movement → MoveDir intents not sending at 120Hz
- ❌ Doesn't stop → Stop intent not sent
- ❌ Movement continues after release → Server not processing Stop

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
- ❌ Crash → Input rate-limiting failure
- ❌ Runaway → Stop intents not sent on input release
- ❌ FPS drop → Too many packets or processing overhead

---

## Visual Correction

### Smooth Interpolation

**Test 4: Normal Movement Interpolation**

**Steps:**
1. Right-click far away → character begins moving
2. Open Scene view → observe basePos (green sphere) and visualPos (blue sphere)
3. Verify offset line (yellow) remains short (<20 units)

**Expected:**
- ✅ basePos interpolates smoothly between snapshots
- ✅ visualOffset remains small (gap <20 units)
- ✅ No visible "rubber-banding"
- ✅ Position updates are smooth (60fps perception)

**Failure Modes:**
- ❌ Choppy basePos → Snapshot loss or buffer underrun
- ❌ Large offset (>80 units) → Server rejecting intents or collision corrections
- ❌ Visible snapping → Snap threshold too low

---

**Test 5: Offset Correction Tiers**

**Test Small Gap (<20 units):**

**Steps:**
1. Move character at normal speed
2. Check visualOffset in Scene view → should be <20 units
3. Observe correction → should be invisible (smooth exponential decay)

**Expected:**
- ✅ Offset decays smoothly (k=6, ~115ms half-life)
- ✅ No visible correction artifacts
- ✅ Character appears to move naturally

**Test Medium Gap (20-80 units):**

**Steps:**
1. Simulate larger offset (or trigger via collision/server correction)
2. Offset should be 20-80 units
3. Observe faster correction

**Expected:**
- ✅ Offset decays faster (k=15, ~46ms half-life)
- ✅ Slight "pull" visible but smooth
- ✅ Converges within 1 second

**Test Large Gap (>80 units):**

**Steps:**
1. Force large offset (e.g., via teleport or major desync)
2. Offset exceeds 80 units (or MAX_LARGE_GAP=250)
3. Should snap immediately

**Expected:**
- ✅ Instant snap (offset → 0)
- ✅ Character jumps to basePos immediately
- ✅ Warning logged: `[SNAP] gap={gap}`

**Failure Modes:**
- ❌ Slow correction on large gap → Not hitting snap threshold
- ❌ Visible stuttering on small gap → k value too high
- ❌ Oscillation → Correction overshoot (shouldn't happen)

---

## Special Cases

### Crowd Control (CC)

**Test 6: Immobilize Effects**

**Steps:**
1. Trigger root/stun on character (via server command or ability)
2. Try to move via WASD or right-click
3. Observe character state

**Expected:**
- ✅ Character snaps to server position (offset=0)
- ✅ Movement inputs are ignored (or send Stop at 2Hz)
- ✅ Character remains locked in place
- ✅ After CC ends, movement resumes normally

**Failure Modes:**
- ❌ Character still moves → IntentBuilder not respecting immobilize lock
- ❌ Visual drift during CC → offset not locked
- ❌ Spam of Stop intents → Rate-limiting not working (should be 2Hz)

---

### Discontinuities

**Test 7: Teleport**

**Steps:**
1. Trigger teleport (move character 100+ units instantly via server)
2. Observe snap behavior

**Expected:**
- ✅ Character instantly jumps to new position
- ✅ No interpolation through old positions
- ✅ Snapshot buffer purged before discontinuity
- ✅ basePrev reset (no absorb on next frame)
- ✅ Log: `[SNAP HARD] discontinuity at time={time}`

**Failure Modes:**
- ❌ Interpolates through teleport → Discontinuity flag not set
- ❌ Large absorb after teleport → basePrev not reset
- ❌ Multiple snaps → Buffer not purged correctly

---

**Test 8: Blink/Dash**

**Steps:**
1. Trigger blink/dash ability (short-range instant movement)
2. Verify snap behavior

**Expected:**
- ✅ Instant position change
- ✅ Selective buffer purge (only snapshots before blink)
- ✅ Movement resumes after blink
- ✅ No rubber-banding post-blink

**Failure Modes:**
- ❌ Rubber-band back → Blink not marked as discontinuity
- ❌ Interpolates during blink → Flag not processed

---

### Network Conditions

**Test 9: Simulated Packet Loss**

**Steps:**
1. Enable FishNet's packet loss simulation (5-10%)
2. Move character normally
3. Observe extrapolation behavior

**Expected:**
- ✅ Occasional extrapolation (Quality=Extrapolated in logs)
- ✅ Movement still reasonably smooth
- ✅ Snaps back when snapshot arrives
- ✅ Extrapolation <5% of total time

**Failure Modes:**
- ❌ Frequent freezing → Not extrapolating (HORIZON_HARD too low)
- ❌ Constant extrapolation → Snapshots not arriving
- ❌ Large snaps → Extrapolation drift too high

---

**Test 10: High Latency**

**Steps:**
1. Enable FishNet's latency simulation (150ms)
2. Move character via WASD
3. Measure perceived delay

**Expected:**
- ✅ Input feels delayed (~150ms + buffer)
- ✅ Movement is still smooth (no stuttering)
- ✅ Adaptive buffer increases (80-160ms range)
- ✅ No crashes or errors

**Failure Modes:**
- ❌ Stuttering → Buffer not adapting to jitter
- ❌ Rubber-banding → Large corrections due to prediction mismatch (shouldn't happen - we don't predict)
- ❌ Freezing → Buffer underrun

---

**Test 11: High Jitter**

**Steps:**
1. Enable FishNet's jitter simulation (50ms variance)
2. Move character in circles
3. Monitor adaptive buffer

**Expected:**
- ✅ Buffer size adapts (increases toward MAX=160ms)
- ✅ Movement remains smooth despite jitter
- ✅ No frequent buffer underruns
- ✅ Log: `Adaptive buffer: {bufferMs}ms`

**Failure Modes:**
- ❌ Choppy movement → Buffer not large enough
- ❌ Buffer doesn't adapt → TimeSync not working
- ❌ Excessive latency → Buffer too large (should cap at 160ms)

---

## Performance Testing

### Bandwidth

**Test 12: Upload Bandwidth**

**Steps:**
1. Move character continuously for 30 seconds
2. Monitor network stats (via FishNet profiler or Wireshark)
3. Calculate bytes/second

**Expected:**
- ✅ Upload: ~1.5-2 KB/s (120Hz × 14 bytes)
- ✅ No burst spikes
- ✅ Rate-limited to 120Hz

**Failure Modes:**
- ❌ >5 KB/s → Sending too frequently or duplicate packets
- ❌ Bursts → Not rate-limited properly

---

**Test 13: Download Bandwidth**

**Steps:**
1. Stand still, observe 5 other moving entities
2. Monitor download bandwidth
3. Calculate bytes/second

**Expected:**
- ✅ Download: ~15-20 KB/s (60Hz × 5 entities × ~50 bytes)
- ✅ Scales linearly with visible entity count
- ✅ AOI culling reduces load when entities are far

**Failure Modes:**
- ❌ >50 KB/s → Not culling invisible entities
- ❌ No scaling → AOI system not working

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
- ❌ <50 FPS → Rendering bottleneck or too much processing
- ❌ Frame drops → GC spikes or sync stalls

---

**Test 15: Server Tick Performance**

**Steps:**
1. Run server with 10+ simulated players
2. Monitor server tick time (via profiler)
3. Check for tick overruns

**Expected:**
- ✅ Tick time <10ms (target 16.67ms @ 60Hz)
- ✅ No warnings about tick overruns
- ✅ Consistent tick intervals

**Failure Modes:**
- ❌ >16ms → Simulation too slow, will drop ticks
- ❌ Variable times → Non-deterministic processing

---

## Acceptance Criteria

Before merging or releasing:

### Critical (Must Pass)
- [ ] All basic input tests pass (Tests 1-3)
- [ ] Small gap correction is invisible (Test 5)
- [ ] Teleports snap correctly (Test 7)
- [ ] No crashes under input spam (Test 3)
- [ ] Client FPS >60 with 10 entities (Test 14)
- [ ] Server tick time <16ms with 10 players (Test 15)

### Important (Should Pass)
- [ ] WASD movement feels responsive (Test 2)
- [ ] Offset correction tiers work correctly (Test 5)
- [ ] CC immobilize locks movement (Test 6)
- [ ] Packet loss handled gracefully (Test 9)
- [ ] Bandwidth within expected range (Tests 12-13)

### Nice to Have (May Fail)
- [ ] High latency still playable (Test 10)
- [ ] Jitter adaptation smooth (Test 11)
- [ ] Blink/dash behavior perfect (Test 8)

---

## Regression Testing

After any change to:

**GameSim (SimWorld, MovementHandler):**
- Re-run Tests 1, 2, 15

**Network (FishNetAdapter, Messages):**
- Re-run Tests 1, 9, 12, 13

**Client Sync (BaseInterpolator, VisualPositionManager):**
- Re-run Tests 4, 5, 7, 9, 10, 11

**Input (InputCollector, IntentBuilder):**
- Re-run Tests 1, 2, 3, 6

---

## Debugging Failed Tests

When a test fails:

1. **Enable instrumentation logs** (see [Instrumentation.md](Instrumentation.md))
2. **Capture full log sequence** during test failure
3. **Check metrics:**
   - Visual offset gap
   - Extrapolation ratio
   - Input acknowledgment latency
4. **Profile with Unity Profiler** if performance-related
5. **Compare with expected timings** from [Debugging_Guide.md](Debugging_Guide.md)

---

## Automated Testing (Future)

These manual tests should eventually be automated:

**Unit Tests:**
- InputIntent serialization/deserialization
- TimeSync adaptive buffer logic
- VisualOffsetCorrector tier calculations

**Integration Tests:**
- Simulated client-server interaction
- Snapshot interpolation accuracy
- Sequence number correlation

**Performance Tests:**
- Bandwidth usage under various entity counts
- Server tick time scaling
- Client frame rate with stress load

---

**Last Updated:** 2026-02-03

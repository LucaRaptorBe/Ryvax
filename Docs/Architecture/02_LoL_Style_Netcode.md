# LoL-Style Netcode Architecture (V5.0)

**Intent-Based Movement with Server Authority and Visual Smoothing**

This document describes the complete implementation of League of Legends-inspired netcode, fundamentally different from FPS-style prediction + reconciliation.

---

## Core Principles

### 1. Inputs Are Intents
Client sends **intentions** (`MoveTo`, `Stop`, `Follow`), not raw directions or state.

**Not this (FPS-style):**
```csharp
// Client sends state
SendToServer(position, velocity, rotation);
```

**But this (LoL-style):**
```csharp
// Client sends intent
SendToServer(new InputIntent { Type = MoveTo, WorldPos = clickedPos });
```

### 2. No Rollback/Replay
Server is absolute authority. Client does **visual anticipation**, not structured prediction.

**Not this (FPS-style):**
```csharp
// Client predicts
localPos += velocity * dt;
// Server corrects
if (serverPos != localPos)
    Rollback(serverTick);
    ReplayInputs(serverTick → now);
```

**But this (LoL-style):**
```csharp
// Client never predicts position
// Visual smoothing corrects offset toward 0
visualPos = basePos + visualOffset;
visualOffset = Lerp(visualOffset, Vector3.zero, k * dt);
```

### 3. Interpolation First
`basePos` comes from interpolated server snapshots, NOT from local prediction.

```csharp
// Find two bracketing snapshots
FindBracketingSnapshots(renderTime, out A, out B, out alpha);
basePos = Lerp(A.Position, B.Position, alpha);  // Delayed truth
```

### 4. Offset Correction
We correct `visualOffset` toward 0, not `visualPos` toward `serverPos`.

**Visual Formula:**
```
visualPos = basePos + visualOffset
```

- `basePos`: Interpolated from server snapshots (delayed truth)
- `visualOffset`: Visual displacement that corrects toward 0
- `visualPos`: Final rendered position

---

## How It Works

### Input Intent System

**Client sends intents, not state:**

| Intent Type | Payload | Meaning |
|-------------|---------|---------|
| `MoveDir` | `Vector2 direction` | Move in direction (WASD) |
| `MoveTo` | `Vector3 worldPos` | Move toward position (right-click) |
| `Stop` | none | Stop movement |
| `Follow` | `uint entityId` | Follow entity |

**Example flow:**
```
User presses 'W'
→ InputCollector detects input
→ IntentBuilder creates MoveDir(forward) @ 120Hz
→ NetworkClient sends InputPacket
→ Server receives, applies to SimPlayer
→ Server broadcasts SnapshotDelta with new position
→ Client interpolates basePos from snapshots
→ PlayerView renders at visualPos
```

**Crucially:** Client does NOT move locally. Position updates only come from server.

---

### Visual Smoothing (Not Prediction)

#### The Problem
Server snapshots arrive every ~16ms (60Hz), but are delayed by ~100ms (adaptive buffer). If we snap directly to each snapshot, movement looks stuttery.

#### The Solution
**Two-layer rendering:**

1. **basePos** = Lerp between server snapshots (smooth delayed truth)
2. **visualOffset** = Absorption offset for visual continuity (corrects toward 0)

**Formula:**
```
visualPos = basePos + visualOffset
```

**When basePos jumps** (e.g., server corrects position due to collision):
```csharp
// Absorb the jump into offset
visualOffset += (basePrev - baseNow);
// Now visualPos stays continuous, offset will smooth toward 0
```

**Over time:**
```csharp
// Exponential decay toward 0
if (gap < smallGap)
    visualOffset *= exp(-6 * dt);  // Smooth (~115ms half-life)
else if (gap < largeGap)
    visualOffset *= exp(-15 * dt);  // Accelerated (~46ms half-life)
else
    visualOffset = 0;  // Snap (too far)
```

---

## Architecture Components

### Client-Side

| Component | File | Purpose |
|-----------|------|---------|
| **InputIntent** | `Client/Input/InputIntent.cs` | Intent data structure |
| **IntentBuilder** | `Client/Input/IntentBuilder.cs` | Converts input → intents @ 120Hz |
| **TimeSync** | `Client/Timing/TimeSync.cs` | Adaptive buffer (80-160ms) |
| **BaseInterpolator** | `Client/Interpolation/BaseInterpolator.cs:228` | Interpolates basePos from snapshots |
| **VisualOffsetCorrector** | `Client/Prediction/VisualOffsetCorrector.cs` | Corrects offset → 0 (3 tiers) |
| **VisualPositionManager** | `Client/Prediction/VisualPositionManager.cs:158` | Orchestrates basePos + offset |

### Network Messages

| Message | Size | Purpose |
|---------|------|---------|
| **InputPacket** | 14 bytes | Client → Server (intents) |
| **SnapshotDelta** | 40+ bytes | Server → Client (entity states) |
| **GameCommand** | Variable | Events (Jump, CastAbility, etc.) |

### Server-Side

| Component | File | Purpose |
|-----------|------|---------|
| **MovementHandler** | `GameSim/Commands/Handlers/MovementHandler.cs:14` | Applies movement intents |
| **ServerGameLoop** | `Server/ServerGameLoop.cs:909` | Authoritative tick loop (60Hz) |

---

## Data Flow

### Complete Flow (V5.0)

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. INPUT CAPTURE                                                 │
├─────────────────────────────────────────────────────────────────┤
│ User presses 'W'                                                 │
│ → InputCollector.HandleWASDInput()                               │
│ → _networkClient.SetLocalMoveIntent(forward)  ← ROTATION STARTS │
│ → _intentBuilder.OnKeyboardMove(dir, dt, tick)                   │
│   → Returns InputIntent.MoveDir if rate-limit OK (120Hz)         │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. INTENT QUEUING & SEND                                         │
├─────────────────────────────────────────────────────────────────┤
│ NetworkClient.SendInputIntent(intent)                            │
│ → _pendingIntent = intent                                        │
│ → SendInputUpdate() @ 120Hz                                      │
│   → InputPacket.CreateMoveDir(tick, seq, dir, events)            │
│   → _netAdapter.SendInputPacket(packet)  [UDP unreliable]        │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼ (Network ~17ms)
┌─────────────────────────────────────────────────────────────────┐
│ 3. SERVER RECEIVE & SIMULATE                                     │
├─────────────────────────────────────────────────────────────────┤
│ ServerGameLoop receives packet                                   │
│ → BufferMovementInput(MoveDir, dir)                              │
│ → FixedUpdate():                                                 │
│   → FlushPendingMovementInputs()  ← Apply buffered inputs        │
│   → SimWorld.Tick(dt)                                            │
│     → MovementHandler.Execute()                                  │
│       → SimPlayer.SetMoveDirection(dir)                          │
│       → Transform.Position += velocity * dt                      │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. SNAPSHOT BROADCAST                                            │
├─────────────────────────────────────────────────────────────────┤
│ ServerGameLoop.BroadcastSnapshots() @ 60Hz                       │
│ → SnapshotDelta {                                                │
│     ServerTick,                                                  │
│     Entities: [{ Position, Velocity, Rotation, Speed, ... }]     │
│   }                                                              │
│ → Send via UDP unreliable                                        │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼ (Network ~17ms)
┌─────────────────────────────────────────────────────────────────┐
│ 5. CLIENT RECEIVE SNAPSHOT                                       │
├─────────────────────────────────────────────────────────────────┤
│ NetworkClient.OnSnapshotReceived()                               │
│ → _timeSync.OnSnapshotReceived(serverTime)                       │
│   → Calculates jitter, adjusts adaptiveBuffer (80-160ms)         │
│ → _visualPositionManager.OnSnapshotReceived(state, serverTime)   │
│   → _interpolator.OnSnapshotReceived()                           │
│     → _buffer.Add(state, serverTime)  // Add to ring buffer      │
│   → _corrector.OnImmobilizeCC(state.HasImmobilizeCC)             │
│   → If blink/teleport/reset:                                     │
│       _corrector.SnapHard(serverTime)                            │
│       _basePrevValid = false                                     │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. VISUAL UPDATE (Every Frame)                                   │
├─────────────────────────────────────────────────────────────────┤
│ NetworkClient.Update()                                           │
│ → _visualPositionManager.Update(dt)                              │
│   → _timeSync.Update(dt)                                         │
│     → _perceivedServerTime += dt                                 │
│   → renderTime = _timeSync.RenderTime                            │
│     → = _perceivedServerTime - adaptiveBuffer (~100ms delay)     │
│   → _basePos = _interpolator.GetBasePos(renderTime)              │
│     → FindBracketingSnapshots(renderTime, out A, out B, out α)   │
│     → If interpolation possible:                                 │
│         return Lerp(A.Position, B.Position, α)  ← SMOOTH TRUTH   │
│     → Else: extrapolation or freeze                              │
│   → If HadDiscontinuity:                                         │
│       _corrector.SnapHard(discontinuityTime)                     │
│       _basePrev = _basePos                                       │
│   → Else if _basePrevValid AND Quality == Interpolated:          │
│       _corrector.AbsorbBasePosJump(_basePrev, _basePos)          │
│       → _visualOffset += (_basePrev - _basePos)  ← CONTINUITY    │
│   → _basePrev = _basePos                                         │
│   → _corrector.Update(dt, _currentSpeed)                         │
│     → gap = _visualOffset.magnitude                              │
│     → If gap < smallGap:                                         │
│         _visualOffset *= exp(-6 * dt)  // Smooth correction      │
│       Else if gap < largeGap:                                    │
│         _visualOffset *= exp(-15 * dt)  // Accelerated           │
│       Else:                                                      │
│         _visualOffset = 0  // Snap                               │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. RENDER                                                        │
├─────────────────────────────────────────────────────────────────┤
│ PlayerView.UpdatePosition()                                      │
│ → transform.position = _networkClient.GetVisualPosition()        │
│   → return _basePos + _corrector.VisualOffset                    │
│ → transform.rotation = LocalIntentFeedback (immediate feedback)  │
│   → OR _networkClient.GetVisualRotationY() (server rotation)     │
│ → _animator.SetBool("IsMoving", HasLocalMoveIntent())            │
│   → Animation responds immediately to input!                     │
└─────────────────────────────────────────────────────────────────┘
```

---

## Offset Correction Tiers

### Dynamic Thresholds

Thresholds scale with player speed for adaptive correction:

```csharp
// NetcodeConstants.cs
smallGap = max(20, speed * 0.06);  // Smooth threshold
largeGap = clamp(max(80, speed * 0.25), 80, 250);  // Snap threshold (capped)
```

**At speed = 8 u/s:**
- `smallGap = max(20, 8 * 0.06) = 20 units`
- `largeGap = clamp(max(80, 8 * 0.25), 80, 250) = 80 units`

### Three Correction Tiers

| Gap | Behavior | Rate | Half-Life | Use Case |
|-----|----------|------|-----------|----------|
| < 0.5 | Clamp to 0 | Instant | - | Epsilon |
| < 20 | Smooth | k=6 | ~115ms | Normal jitter |
| < 80 | Accelerated | k=15 | ~46ms | Collision correction |
| ≥ 80 | Snap | Instant | - | Large desync |

**Exponential decay:**
```csharp
offset *= exp(-k * dt);
```
- k=6: `half-life = ln(2)/6 ≈ 115ms`
- k=15: `half-life = ln(2)/15 ≈ 46ms`

---

## Special Cases

### Immobilizing CC (Root/Stun)

When server applies immobilize (root/stun):

```csharp
// VisualOffsetCorrector.cs
if (state.HasImmobilizeCC)
{
    _visualOffset = Vector3.zero;  // Snap to server position
    _isImmobilized = true;  // Lock offset until CC ends
}
```

**IntentBuilder behavior:**
```csharp
// IntentBuilder.cs
if (_isImmobilizeLocked)
{
    // Emit Stop @ 2Hz (rate-limited, prevents spam)
    return InputIntent.Stop(seqId++, clientTick);
}
```

### Blink/Teleport Events

When server applies blink or teleport:

```csharp
// VisualPositionManager.cs
if (state.HasBlinkEvent || state.HasTeleportEvent || state.StateReset)
{
    _corrector.SnapHard(serverTime);  // offset=0, purge snapshots before event
    _basePrevValid = false;  // Skip absorb next frame (prevent huge offset)
}
```

**SnapHard vs Snap:**

| Method | Action | Use Case |
|--------|--------|----------|
| `Snap()` | offset=0 | Gap too large (visual correction) |
| `SnapHard(time)` | offset=0 + purge snapshots before `time` | Discontinuity (teleport/blink) |
| `SnapHard()` | offset=0 + purge all | State reset |

### BasePosQuality Tracking

BaseInterpolator tracks quality of basePos:

```csharp
public enum BasePosQuality
{
    Interpolated,   // Normal: two snapshots available
    Extrapolated,   // Fallback: dead-reckoning (< 150ms)
    Frozen          // No valid data (> 150ms)
}
```

**Absorption rule:**
```csharp
// Only absorb jumps during valid interpolation
if (_interpolator.Quality == BasePosQuality.Interpolated)
{
    _corrector.AbsorbBasePosJump(_basePrev, _basePos);
}
```

**Why?** During extrapolation, basePos is uncertain. Absorbing jumps would inject artificial offsets.

---

## Constants (NetcodeConstants.cs)

### Correction Thresholds

| Constant | Value | Description |
|----------|-------|-------------|
| `LOL_BASE_SMALL_GAP` | 20 | Smooth threshold (world units) |
| `LOL_BASE_LARGE_GAP` | 80 | Snap min threshold (world units) |
| `MAX_LARGE_GAP` | 250 | Snap max threshold cap |
| `LOL_SMALL_GAP_TIME` | 0.06s | Speed multiplier for smallGap |
| `LOL_LARGE_GAP_TIME` | 0.25s | Speed multiplier for largeGap |
| `LOL_SMOOTH_CORRECTION_K` | 6 | Smooth correction rate |
| `LOL_ACCEL_CORRECTION_K` | 15 | Accelerated correction rate |
| `LOL_OFFSET_EPSILON` | 0.5 | Epsilon for snap to 0 |

### Extrapolation

| Constant | Value | Description |
|----------|-------|-------------|
| `HORIZON_SOFT` | 100ms | Dead-reckoning normal |
| `HORIZON_HARD` | 150ms | Dead-reckoning max (was 200ms in V2.3) |

### Adaptive Buffer

| Constant | Value | Description |
|----------|-------|-------------|
| `ADAPTIVE_BUFFER_TARGET` | 100ms | Buffer target |
| `ADAPTIVE_BUFFER_MIN` | 80ms | Buffer minimum |
| `ADAPTIVE_BUFFER_MAX` | 160ms | Buffer maximum |
| `INTENT_SEND_RATE` | 10Hz | Intent send rate (legacy, now 120Hz) |
| `IMMOBILIZE_STOP_RATE` | 2Hz | Stop rate during CC |

---

## Server Recommendations

### Anti-Spam Protection (Critical)

Even if the client is correct, a modified client can spam. Server **must**:

1. **Rate limit intents:** max 120-150/s per client
2. **Input coalescing:** Apply only the **last** intent received before each tick
3. **Validate seqId:** Drop intents that are too old or out-of-order

```csharp
// ServerGameLoop.cs (pseudo-code)
void FlushPendingMovementInputs()
{
    foreach (var client in _clients)
    {
        // Get highest seq intent (last-input-wins)
        var intent = client.GetLatestIntent();
        if (intent.Seq <= client.LastProcessedSeq)
            continue;  // Reject old/duplicate

        client.LastProcessedSeq = intent.Seq;
        ApplyIntent(client, intent);
    }
}
```

### Dynamic Collisions

Without pathfinding, collisions with other entities are handled server-side. Client cannot predict them, so offsets may increase. Accelerated tier (80-250 units) tolerates this.

### lastProcessedSeq

Snapshots **must** include `lastProcessedSeq` (last seqId of intent processed):

**Benefits:**
- Measure input lag: `currentSeq - lastProcessedSeq`
- Correlate corrections with inputs
- Debug sync issues

---

## Comparison: LoL-Style vs FPS-Style

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **Input** | Raw direction (WASD) | Intents (MoveTo, Stop) |
| **Prediction** | Full client-side + rollback | Visual anticipation (no rollback) |
| **Reconciliation** | Replay inputs after misprediction | Smooth offset correction |
| **Authority** | Client predicts, server validates | Server decides, client displays |
| **Perceived Latency** | Immediate (prediction) | Delayed (~100ms buffer) |
| **Rubber-Banding** | Visible on misprediction | Handled by smooth correction |
| **Anti-Cheat** | Client can manipulate prediction | Client can't affect position |
| **Complexity** | High (rollback, replay) | Medium (interpolation, smoothing) |

---

## Manual Testing Checklist

### Basic Verification

- [ ] Right-click → MoveTo sent immediately
- [ ] WASD → MoveDir sent @ 120Hz toward direction
- [ ] Release WASD → Stop sent
- [ ] Smooth interpolation between snapshots
- [ ] Short extrapolation if snapshots delayed (< 150ms)

### Visual Correction

- [ ] Gap < 20: Correction invisible
- [ ] 20 < gap < 80: Rubber-band perceptible but smooth
- [ ] Gap ≥ 80 (or 250 max): Snap immediate

### Special Cases (V5.0)

- [ ] Immobilizing CC (root/stun): Snap + offset locked + Stop @ 2Hz
- [ ] Blink/teleport: SnapHard + selective purge
- [ ] High jitter: Buffer adapts (80-160ms)
- [ ] After teleport: basePrev reset, no huge absorb
- [ ] During extrapolation: No absorb (Quality != Interpolated)

---

## Debugging

### Available Information

```csharp
// NetworkClient
float gap = networkClient.GetCurrentGap();
Vector3 basePos = networkClient.GetBasePosition();
Vector3 offset = networkClient.GetVisualOffset();
Vector3 visualPos = networkClient.GetVisualPosition();
float speed = networkClient.GetCurrentSpeed();
bool immobilized = networkClient.IsImmobilized();

// VisualPositionManager
string debug = visualPositionManager.GetDebugInfo();
bool isImmob = visualPositionManager.IsImmobilized;

// IntentBuilder
bool locked = intentBuilder.IsImmobilizeLocked;

// BaseInterpolator
BasePosQuality quality = interpolator.Quality;
```

### Metrics to Monitor

1. **Average gap:** Should stay < 20 in normal gameplay
2. **Adaptive buffer:** 80-160ms based on jitter
3. **Snap frequency:** Should be rare (< 1% of frames)
4. **Extrapolation:** Should be rare (< 5% of time)
5. **Immobilize lock active:** Verify Stop emitted @ 2Hz during CC
6. **BasePosQuality:** Monitor Interpolated vs Extrapolated ratio

---

## Version History

- **V5.0 (Current):** Pure LoL-style, removed prediction, 50ms latency
- **V4.0:** Local intent feedback (rotation/animation immediate)
- **V3.0:** Intent-based input (MoveDir/MoveTo/Stop)
- **V2.3:** Reduced horizon (150ms), BasePosQuality tracking, conditional absorb
- **V2.2:** Bug fixes (basePrev reset, selective purge, immobilize lock, largeGap cap)
- **V2.1:** Edge cases (CC, blink, teleport)
- **V2.0:** Architecture basePos + visualOffset
- **V1.0:** Initial VisualExtrapolator implementation

---

**Last Updated:** 2026-02-03

**Key Files:**
- `Client/Input/IntentBuilder.cs` - Intent creation
- `Client/Interpolation/BaseInterpolator.cs:228` - Interpolation
- `Client/Prediction/VisualOffsetCorrector.cs` - Offset correction
- `Client/Prediction/VisualPositionManager.cs:158` - Orchestration
- `Server/ServerGameLoop.cs:909` - Authoritative tick
- `GameSim/Commands/Handlers/MovementHandler.cs:14` - Movement execution

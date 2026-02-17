# Visual Smoothing System (V4.0 Pure LoL-Style)

## Overview

The visual smoothing system provides **instant visual feedback** while maintaining **100% server authority** for position. Players see immediate rotation and animation response when pressing keys, while position follows the server (with ~66ms latency on localhost).

**Core Formula (V4.0):**
```
visualPosition = basePosition
```

Where:
- `basePosition` = interpolated server position (the "truth")

**V4.0 Change:** Removed the offset system entirely. No absorption, no decay. Position is pure server authority interpolated.

---

## How Feedback Works (Pure LoL Style)

### The Challenge: 66ms Round-Trip

```
[Frame 0]    Player presses W
[Frame 0]    Client sends MoveDir intent
[+16ms]      Server receives (Update polling)
[+33ms]      Server applies input (FixedUpdate)
[+33ms]      Server broadcasts snapshot
[+16ms]      Client receives snapshot
─────────────────────────────────────────
Total: ~66ms before server confirms movement
```

Without visual feedback, the player would feel the game is unresponsive.

### The Solution: Immediate Rotation + Animation

Instead of predicting position (which can diverge), we provide instant feedback through:

```
[Frame 0] Player presses W
          └─ SetLocalMoveIntent(forward)
          └─ Rotation turns IMMEDIATELY towards input direction
          └─ Animation "run" starts IMMEDIATELY
          └─ Position = unchanged (waiting for server)

[Frame 1-3] Player holds W
          └─ Rotation/animation remain active
          └─ Server processes the movement

[Frame ~4] Server snapshot arrives (~66ms)
          └─ basePosition starts moving (interpolated)
          └─ Player sees character moving (with rotation/anim already active!)
```

**Result:** Player sees instant rotation + animation. Position follows server authority.

---

## Architecture

### Component Pipeline

```
┌─────────────────────────────────────────────────────────────────┐
│                    InputCollector.Update()                      │
│                    [ExecutionOrder = -100]                      │
│                                                                 │
│  1. Sample keyboard input (WASD)                                │
│  2. if (isMoving):                                              │
│       SetLocalMoveIntent(moveDir)  ◄── IMMEDIATE FEEDBACK       │
│       └─ Stores direction for rotation/animation                │
│       └─ Does NOT modify position                               │
│  3. else:                                                       │
│       ClearLocalMoveIntent()                                    │
│  4. Build MoveDir intent (rate-limited 10Hz)                    │
│  5. Send to server                                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                   NetworkClient.Update()                        │
│                                                                 │
│  1. TimeSync.Update(dt)                                         │
│       └─ PLL advances perceivedServerTime                       │
│       └─ renderTime = perceivedServerTime - adaptiveBuffer      │
│                                                                 │
│  2. VisualPositionManager.Update(dt)                            │
│       └─ BaseInterpolator.GetBasePos(renderTime)                │
│       └─ Interpolate between two snapshots                      │
│       └─ Or extrapolate if no bracketing pair                   │
│                                                                 │
│  3. VisualPosition = basePos  ◄── V4.0: Direct, no offset!      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    PlayerView.Update()                          │
│                                                                 │
│  POSITION: Always from server (interpolated)                    │
│    transform.position = NetworkClient.GetVisualPosition()       │
│                                                                 │
│  ROTATION: Uses local intent for immediate feedback             │
│    if (HasLocalMoveIntent()):                                   │
│      targetRotY = Atan2(intent.x, intent.z)                     │
│      transform.rotation = LerpAngle(current, target, dt * 15f)  │
│    else:                                                        │
│      transform.rotation = server rotation (interpolated)        │
│                                                                 │
│  ANIMATION: Uses local intent for immediate feedback            │
│    IsMoving = HasLocalMoveIntent()                              │
│    Speed = HasLocalMoveIntent() ? 1.0 : 0.0                     │
└─────────────────────────────────────────────────────────────────┘
```

### Key Files

| File | Role |
|------|------|
| `Client/Input/InputCollector.cs` | Sets local move intent for feedback |
| `Client/Prediction/VisualPositionManager.cs` | Orchestrates visual position (V4.0: no offset) |
| `Client/Interpolation/BaseInterpolator.cs` | Interpolates server snapshots |
| `Client/Timing/TimeSync.cs` | **Unified PLL timing** for all players (local + remote) |
| `Core/NetworkClient.cs` | Integration layer, owns TimeSync + VisualPositionManager + local intent |
| `Client/View/Entities/PlayerView.cs` | Renders position + applies rotation/animation feedback |

**Deprecated (V4.0):**
| File | Status |
|------|--------|
| `Client/Prediction/VisualOffsetCorrector.cs` | DEPRECATED - no longer used |

---

## Local Intent API

The local intent system provides immediate visual feedback without predicting position:

```csharp
// NetworkClient.cs

// Called by InputCollector when player inputs movement
public void SetLocalMoveIntent(Vector3 direction)
{
    _localMoveIntent = direction.normalized;
    _hasLocalMoveIntent = true;
}

// Called when player stops inputting movement
public void ClearLocalMoveIntent()
{
    _hasLocalMoveIntent = false;
}

// Used by PlayerView for rotation
public Vector3 GetLocalMoveIntent() => _hasLocalMoveIntent ? _localMoveIntent : Vector3.zero;

// Used by PlayerView for animation
public bool HasLocalMoveIntent() => _hasLocalMoveIntent;
```

### PlayerView Usage

```csharp
// PlayerView.cs - UpdatePosition()

// Rotation: immediate feedback via local intent
if (_networkClient.HasLocalMoveIntent())
{
    Vector3 intent = _networkClient.GetLocalMoveIntent();
    float targetRotY = Mathf.Atan2(intent.x, intent.z) * Mathf.Rad2Deg;
    float smoothedRotY = Mathf.LerpAngle(currentRotY, targetRotY, Time.deltaTime * 15f);
    transform.rotation = Quaternion.Euler(0f, smoothedRotY, 0f);
}
else
{
    // No local intent: use server rotation
    transform.rotation = Quaternion.Euler(0f, _networkClient.GetVisualRotationY(), 0f);
}

// PlayerView.cs - UpdateAnimation()

// Animation: immediate feedback via local intent
bool localIsMoving = _networkClient.HasLocalMoveIntent();
_animator.SetBool(IsMovingHash, localIsMoving);
_animator.SetFloat(SpeedHash, localIsMoving ? 1f : 0f, 0.05f, Time.deltaTime);
```

---

## Unified TimeSync (PLL)

The timing system uses a **Phase-Locked Loop (PLL)** to maintain smooth perceived server time. This serves both local and remote players.

### The Problem: Discrete Snapshots

```
Server sends snapshots at 30Hz (every 33ms)
But they arrive with jitter: 28ms, 38ms, 31ms, 42ms...

Without PLL:
- Client time advances at fixed 1:1 rate
- Mismatch causes buffer underrun (stutter) or overrun (latency)
```

### The Solution: PLL Time Correction

```csharp
// TimeSync.cs - Update() with PLL

// Target: maintain adaptiveBuffer between newest snapshot and perceived time
float currentOffset = _newestSnapshotTime - _perceivedServerTime;
float offsetError = currentOffset - _adaptiveBuffer;

// Adjust playback rate: speed up if behind, slow down if ahead
float playbackRate = 1.0f + PLL_GAIN * offsetError;  // PLL_GAIN = 0.2
playbackRate = Clamp(playbackRate, 0.9f, 1.1f);

// Advance with corrected rate
_perceivedServerTime += dt * playbackRate;
```

### PLL Behavior

| Situation | offsetError | playbackRate | Effect |
|-----------|-------------|--------------|--------|
| Buffer growing (behind) | > 0 | 1.02 - 1.1 | Speed up to catch up |
| Buffer shrinking (ahead) | < 0 | 0.9 - 0.98 | Slow down to wait |
| Perfect | = 0 | 1.0 | Normal speed |

### Architecture: Single Shared TimeSync

```
┌─────────────────────────────────────────────────────────────────┐
│                      NetworkClient                               │
│                                                                 │
│  _timeSync ◄─── ONE TimeSync for everything                    │
│      │                                                          │
│      ├─► OnSnapshotReceived() feeds ALL snapshots               │
│      ├─► Update() advances PLL every frame                      │
│      │                                                          │
│      ├─────────────────────────┬────────────────────────────────┤
│      │                         │                                │
│      ▼                         ▼                                │
│  ┌──────────────────┐   ┌──────────────────┐                   │
│  │ Local Player     │   │ Remote Players   │                   │
│  │                  │   │                  │                   │
│  │ VisualPosition   │   │ EntityView       │                   │
│  │ Manager uses     │   │ uses             │                   │
│  │ _timeSync        │   │ _timeSync        │                   │
│  │ .RenderTime      │   │ .PerceivedServer │                   │
│  │                  │   │  Time            │                   │
│  └──────────────────┘   └──────────────────┘                   │
└─────────────────────────────────────────────────────────────────┘
```

### Adaptive Buffer

The buffer adjusts based on network jitter:

```csharp
// On each snapshot, estimate jitter
float jitterSample = Abs(actualDelta - expectedDelta);
_estimatedJitter = Lerp(_estimatedJitter, jitterSample, 0.1f);  // EMA

// Adaptive buffer: target + jitter compensation
float targetBuffer = TARGET_BUFFER + JITTER_GAIN * _estimatedJitter;
_adaptiveBuffer = Clamp(targetBuffer, MIN_BUFFER, MAX_BUFFER);
```

| Network Condition | Jitter | Adaptive Buffer |
|-------------------|--------|-----------------|
| Stable (LAN) | ~5ms | 80-100ms |
| Normal | ~20ms | 100-120ms |
| Unstable | ~50ms | 140-160ms |

---

## Comparison: V3.0 vs V4.0

| Aspect | V3.0 (Offset System) | V4.0 (Pure LoL) |
|--------|----------------------|-----------------|
| Formula | `visualPos = basePos + offset` | `visualPos = basePos` |
| Absorption | Yes (offset absorbs basePos jumps) | No (direct server) |
| Decay | Yes (offset decays to zero) | No |
| Complexity | Medium | Simple |
| Position source | Server + offset smoothing | Pure server |

### Why V4.0?

The offset/absorption/decay system was designed for a prediction model. Without local position prediction:
- Absorption has nothing to absorb (no predicted offset to reconcile)
- Decay has nothing to decay
- The system just added complexity without benefit

V4.0 simplifies to: **position = what the server says, interpolated smoothly.**

---

## Edge Cases

### Extrapolation

When no bracketing snapshots exist (network hiccup), the system extrapolates:

| Horizon | Duration | Behavior |
|---------|----------|----------|
| Soft | 0-100ms | Dead-reckoning at last velocity |
| Hard | 100-150ms | Continue extrapolating |
| Freeze | >150ms | Hold last position |

### Discontinuities (Teleport/Blink)

For teleports, the interpolation buffer is purged:

```csharp
// VisualPositionManager.OnSnapshotReceived()
if (state.HasBlinkEvent || state.HasTeleportEvent || state.HasStateReset)
{
    _interpolator.PurgeSnapshotsBefore(serverTime);
}
```

Player teleports instantly to new position.

### Crowd Control (Root/Stun)

During immobilizing CC:
- Movement intents suppressed
- Local intent cleared (rotation/animation stop)
- `IntentBuilder` emits Stop at 2Hz

---

## Constants Reference

From `TimeSync.cs` (V4.0 Unified PLL):

```csharp
// Buffer range
TARGET_BUFFER = 0.1f;              // 100ms target
MIN_BUFFER = 0.08f;                // 80ms minimum
MAX_BUFFER = 0.16f;                // 160ms maximum
JITTER_GAIN = 0.5f;                // Jitter → buffer scaling

// PLL tuning
PLL_GAIN = 0.2f;                   // Correction speed
PLAYBACK_MIN = 0.9f;               // Min playback rate (90%)
PLAYBACK_MAX = 1.1f;               // Max playback rate (110%)

// Jitter estimation
JITTER_SMOOTHING = 0.1f;           // EMA alpha for jitter
```

From `NetcodeConstants.cs`:

```csharp
// Tick rate
TICK_RATE = 30;                    // 33.33ms per tick
TICK_DELTA = 1f / 30f;             // 0.0333s

// Interpolation
INTERPOLATION_BUFFER_TICKS = 2;    // 66ms buffer
ADAPTIVE_BUFFER_TARGET = 0.1f;     // 100ms target
ADAPTIVE_BUFFER_MIN = 0.08f;       // 80ms minimum
ADAPTIVE_BUFFER_MAX = 0.16f;       // 160ms maximum
```

---

## Debugging

Enable logging in `MovementCycleLogger.cs`:

```csharp
MovementCycleLogger.Enabled = true;
MovementCycleLogger.LogInput = true;
MovementCycleLogger.LogIntent = true;
MovementCycleLogger.LogSnapshot = true;
```

Sample output:
```
[0.123] [INPUT START] dir=(1,0) speed=8.0
[0.123] [INTENT] MoveDir seq=1 dir=(1.00,0.00)
[0.189] [SNAPSHOT] tick=42 pos=(0.40,0,0) vel=(8,0,0)
[0.256] [SNAPSHOT] tick=43 pos=(0.67,0,0) vel=(8,0,0)
```

---

## Why This Works (Like League of Legends)

1. **Rotation Feedback**: Character turns towards input direction instantly
   - Player sees their input was recognized
   - Feels responsive even without position moving

2. **Animation Feedback**: "Run" animation starts immediately
   - Confirms the action visually
   - Matches player expectation

3. **Position Authority**: Character moves where server says
   - Never inside walls
   - Never rubber-bands
   - What you see = what actually happened

4. **Acceptable Latency**: ~66ms for a MOBA is normal
   - Less noticeable than expected because rotation/animation are instant
   - Brain focuses on the immediate visual feedback

5. **Consistency**: No divergence between client and server
   - No "I thought I dodged that skillshot"
   - No "Why am I in the wall?"

---

## Summary

The V4.0 Pure LoL visual smoothing system provides responsive feel while maintaining server authority:

1. **Unified TimeSync**: Single PLL-based timing system for all players (local + remote)
2. **Instant Rotation**: `SetLocalMoveIntent()` enables immediate rotation towards input direction
3. **Instant Animation**: `HasLocalMoveIntent()` triggers run animation without delay
4. **Server Position**: `basePosition` is interpolated from server snapshots (~66ms latency)
5. **No Offset System**: V4.0 removed absorption/decay for simplicity

The player experiences responsive controls (rotation + animation) while the server maintains 100% authoritative position.

### System Flow (V4.0)

```
Snapshots ──► TimeSync (PLL) ──► RenderTime ──► BaseInterpolator ──► basePos = visualPos
                  │
                  ▼
           PerceivedServerTime
                  │
                  ▼
           Remote Players (EntityView)
```

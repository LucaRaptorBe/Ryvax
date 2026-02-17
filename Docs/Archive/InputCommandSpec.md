# Input Command Specification v2.0

## Architecture Overview

Intent-based movement system with distinct command types for WASD and click-to-move.
Server authoritative, client sends only commands (never position/velocity/deltaTime as simulation state).

---

## Command Types

### Movement Commands

| Command | Payload | Source | Description |
|---------|---------|--------|-------------|
| `MoveDir` | `dirX, dirZ` (normalized, quantized ±127) | WASD | Continuous directional movement |
| `MoveTo` | `targetX, targetZ` (world pos, quantized ×10) | Right-click | Move towards world position |
| `Stop` | none | Key release / S key | Stop all movement |

### Event Commands (unchanged)

| Command | Payload | Description |
|---------|---------|-------------|
| `Jump` | `none` | Vertical impulse |
| `CastAbility` | `slot, targetPos, targetEntity` | Ability cast |
| `Attack` | `targetEntity` | Auto-attack target |

---

## Network Packet Format

### InputPacket (14 bytes base)

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Description                │
├─────────────────────────────────────────────────────────────┤
│ ClientTick     │ uint   │ 4    │ Client tick for lag comp   │
│ MovementSeq    │ uint   │ 4    │ Monotonic sequence         │
│ IntentType     │ byte   │ 1    │ 0=MoveDir, 1=MoveTo, 2=Stop│
│ Payload0       │ short  │ 2    │ Type-dependent (see below) │
│ Payload1       │ short  │ 2    │ Type-dependent (see below) │
│ CommandCount   │ byte   │ 1    │ Event commands count       │
│ Commands[]     │ var    │ var  │ Event commands array       │
└─────────────────────────────────────────────────────────────┘
```

### Payload Interpretation by IntentType

| IntentType | Payload0 | Payload1 |
|------------|----------|----------|
| `MoveDir` (0) | `dirX × 127` (clamped -127..127) | `dirZ × 127` |
| `MoveTo` (1) | `targetX × 10` (0.1 unit precision) | `targetZ × 10` |
| `Stop` (2) | 0 | 0 |
| `Follow` (3) | `entityId & 0xFFFF` | `entityId >> 16` |

---

## Quantization

### Direction (MoveDir)
```csharp
// Encode: float [-1, 1] → short [-127, 127]
short QuantizeDir(float v) => (short)(Mathf.Clamp(v, -1f, 1f) * 127f);

// Decode: short → float [-1, 1]
float DequantizeDir(short v) => v / 127f;
```

### Position (MoveTo)
```csharp
// Encode: float → short (0.1 unit precision, ±3276 range)
short QuantizePos(float v) => (short)Mathf.Clamp(v * 10f, -32767, 32767);

// Decode: short → float
float DequantizePos(short v) => v / 10f;
```

---

## Client Flow

### WASD Input
```
[Frame N] Keyboard sampled
    ↓
[InputCollector] dir = normalize(WASD vector)
    ↓
[IntentBuilder] Rate-limit 10Hz, create MoveDir(dir)
    ↓
[NetworkClient] Encode: Payload0 = dir.x × 127, Payload1 = dir.z × 127
    ↓
[FishNet UDP] Send at 30Hz
```

### Click Input
```
[Frame N] Right-click detected
    ↓
[InputCollector] worldPos = Raycast to ground
    ↓
[IntentBuilder] Immediate MoveTo(worldPos)
    ↓
[NetworkClient] Encode: Payload0 = pos.x × 10, Payload1 = pos.z × 10
    ↓
[FishNet UDP] Send immediately
```

---

## Server Flow

```
[FishNet] Receive InputPacket (async, in Update or network callback)
    ↓
[FishNetAdapter] Check version (reject < 4), decode based on IntentType
    ↓
[ServerGameLoop] BUFFER input (don't apply yet)
    - OnMoveDirReceived → BufferMovementInput(MoveDir, dir)
    - OnMoveToReceived  → BufferMovementInput(MoveTo, target)
    - OnStopReceived    → BufferMovementInput(Stop)
    ↓
[FixedUpdate START] FlushPendingMovementInputs()
    - Apply only LAST input per client (highest Seq)
    - Reject out-of-order (seq <= lastSeq)
    ↓
[FixedUpdate] RunSimulation() → BroadcastSnapshots()
```

### Input Buffering

FishNet delivers packets asynchronously. Without buffering, inputs arriving DURING
FixedUpdate but AFTER RunSimulation would miss the current tick's snapshot.

```
WITHOUT BUFFER:
  FixedUpdate tick=11
    RunSimulation()      ← Input not yet arrived
    BroadcastSnapshots() ← Snapshot with old state
  [async] Input arrives  ← Applied but too late!

WITH BUFFER:
  [async] Input arrives  → Buffered
  FixedUpdate tick=12
    FlushPending()       ← Input applied NOW
    RunSimulation()      ← Sees fresh input
    BroadcastSnapshots() ← Snapshot reflects input
```

**Last-Input-Wins Rule**: If multiple inputs arrive between ticks, only the
highest Seq is applied. Example: MoveDir seq=5, MoveDir seq=6, Stop seq=7
→ Only Stop seq=7 is applied.

### SimPlayer.SetMoveTarget (new method)
```csharp
// Keeps MoveTo semantic separate from MoveDir
// For now: simple steering. Later: pathfinding entry point.
public void SetMoveTarget(Vector3 target)
{
    _moveTarget = target;
    Vector3 toTarget = (target - Transform.Position);
    toTarget.y = 0;
    if (toTarget.sqrMagnitude > 0.01f)
        SetMoveDirection(toTarget.normalized);
}
```

---

## Server-Side Validation

```csharp
// MoveDir: Always normalize/clamp defensively
void OnMoveDirReceived(int clientId, uint seq, float dirX, float dirZ)
{
    Vector3 dir = new Vector3(dirX, 0, dirZ);
    if (dir.sqrMagnitude > 1.01f)  // Tolerance for quantization error
        dir = dir.normalized;
    player.SetMoveDirection(dir);
}

// MoveTo: Validate position is within map bounds
void OnMoveToReceived(int clientId, uint seq, float targetX, float targetZ)
{
    Vector3 target = new Vector3(targetX, 0, targetZ);
    target = ClampToMapBounds(target);
    player.SetMoveTarget(target);  // For now: direction towards target
}
```

---

## Files to Modify

| File | Changes |
|------|---------|
| `InputIntent.cs` | Add `MoveDir` type, add `Direction` field |
| `IntentBuilder.cs` | WASD → `MoveDir(dir)` instead of `MoveTo(lookahead)` |
| `InputPacket.cs` | Add `IntentType` field, rename fields, fix accessors |
| `NetworkClient.cs` | Encode by type: MoveDir vs MoveTo |
| `FishNetAdapter.cs` | Decode by type, call appropriate event |
| `ServerGameLoop.cs` | Handle `MoveDir` vs `MoveTo` separately |

---

## Invariants

1. **Client never sends**: position, velocity, deltaTime as simulation state
2. **MoveDir payload**: always normalized direction (magnitude ≤ 1), **MoveDir(0,0) is valid**
3. **MoveTo payload**: always world position (0.1 unit precision)
4. **Server always normalizes**: MoveDir defensively clamped
5. **MovementSeq is global**: Monotonic across ALL movement commands (MoveDir, MoveTo, Stop). Single counter.
6. **Stop explicit**: sent on key release, not inferred
7. **MoveDir(0,0) ≠ Stop**:
   - **MoveDir(0,0)** = valid command meaning "no directional input this tick" (maintains current mode)
   - **Stop** = explicit command that cancels current order (including MoveTo) and forces stop
8. **Mode exclusivity**: MoveDir and MoveTo are mutually exclusive per mode
   - Receiving MoveDir switches player to WASD mode (directional control)
   - Receiving MoveTo switches player to Click mode (target-based movement)
   - Stop cancels both modes and clears state
9. **Semantic separation**:
   - `MoveDir` → `SetMoveDirection(dir)` (direction-based)
   - `MoveTo` → `SetMoveTarget(pos)` (target-based, future pathfinding)
10. **Input buffering**: Inputs are buffered and applied at START of FixedUpdate, before simulation
11. **Last-input-wins**: Only the highest Seq per client per tick is applied (prevents spam)

---

## Version Compatibility

- `InputPacket.MSG_VERSION = 4`
- Packets with `version < 4` are **rejected** (log warning, don't process)
- No legacy path — clean break from buggy v3 quantization

# Input System

**Intent-Based Input Architecture - WASD/Click-to-Move with 120Hz Rate Limiting**

The input system converts raw user input (keyboard/mouse) into high-level movement intents (MoveDir, MoveTo, Stop, Follow) that are sent to the server. This intent-based approach is inspired by League of Legends' netcode.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        INPUT PIPELINE                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Raw Input (60-120 FPS)                                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  Unity Input System                                     │    │
│  │  - Keyboard.current (WASD/ZQSD)                         │    │
│  │  - Mouse.current (clicks, position)                     │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  InputCollector.Update()                                │    │
│  │  - HandleMovementInput() - WASD/click handling          │    │
│  │  - HandleDiscreteEvents() - Jump/abilities              │    │
│  └────────────┬────────────────────────┬───────────────────┘    │
│               │                        │                        │
│               ▼                        ▼                        │
│  ┌────────────────────┐   ┌────────────────────────────┐        │
│  │  IntentBuilder     │   │  Event Commands            │        │
│  │  (120Hz)           │   │  (on key press)            │        │
│  │                    │   │                            │        │
│  │ WASD → MoveDir     │   │ Space → Jump               │        │
│  │ Click → MoveTo     │   │ Q/W/E/R → Abilities        │        │
│  │ S key → Stop       │   │                            │        │
│  └────────────┬────────┘   └────────────┬───────────────┘        │
│               │                        │                        │
│               ▼                        ▼                        │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  NetworkClient                                          │    │
│  │  - SendInputIntent() - queues intent                    │    │
│  │  - SendEventCommand() - queues event                    │    │
│  │  - SendInputUpdate() - sends InputPacket (120Hz)        │    │
│  └────────────────────────┬────────────────────────────────┘    │
│                           │                                     │
│                           ▼                                     │
│                   InputPacket (14 bytes UDP)                     │
│                   → Server Processing                            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Files:**
- `Assets/Scripts/Client/Input/InputCollector.cs:342` - Raw input capture
- `Assets/Scripts/Client/Input/IntentBuilder.cs:202` - Intent construction, rate-limiting
- `Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs` - Network message format

---

## InputCollector

### Purpose

Captures raw keyboard/mouse input each frame and converts to high-level intents. Handles both movement (intents) and discrete events (abilities, jump).

**File:** `Assets/Scripts/Client/Input/InputCollector.cs`

**Execution Order:** `DefaultExecutionOrder(-100)` - Runs before most other scripts

---

### Update Loop

```csharp
// InputCollector.cs:94-104
void Update()
{
    if (_networkClient == null || !_networkClient.IsConnected) return;
    if (_networkClient.LocalEntityId == 0) return;

    // 1. Handle movement input (creates intents)
    HandleMovementInput();

    // 2. Handle discrete events (jump, abilities)
    HandleDiscreteEvents();
}
```

**Order matters:**
1. **Movement first** - Updates `_pendingIntent` for this frame
2. **Events second** - Adds to `_eventBuffer` for redundancy

---

## Movement Input

### WASD/ZQSD Keyboard Input

```csharp
// InputCollector.cs:143-192
private InputIntent? HandleWASDInput()
{
    var kb = Keyboard.current;
    if (kb == null) return null;

    // Sample keyboard direction
    Vector2 dir = Vector2.zero;
    // Forward: W (QWERTY) or Z (AZERTY)
    if (kb.wKey.isPressed || kb.zKey.isPressed) dir.y += 1;
    // Backward: S (both layouts)
    if (kb.sKey.isPressed) dir.y -= 1;
    // Right: D (both layouts)
    if (kb.dKey.isPressed) dir.x += 1;
    // Left: A (QWERTY) or Q (AZERTY)
    if (kb.aKey.isPressed || kb.qKey.isPressed) dir.x -= 1;

    if (dir.sqrMagnitude > 1f) dir = dir.normalized;

    // Update state for animation/debug
    _currentMoveInput = dir;
    _currentIsMoving = dir.sqrMagnitude > 0.01f;

    // Debug: detect movement state transitions
    if (_currentIsMoving && !_wasMovingLastFrame)
    {
        MovementCycleLogger.LogInputStart(dir);
    }
    else if (!_currentIsMoving && _wasMovingLastFrame)
    {
        MovementCycleLogger.LogInputStop();
    }
    _wasMovingLastFrame = _currentIsMoving;

    // V3.0: Set local move intent for immediate rotation/animation feedback
    if (_currentIsMoving)
    {
        Vector3 moveDir3D = new Vector3(dir.x, 0f, dir.y);
        _networkClient.SetLocalMoveIntent(moveDir3D);  // No-op in V5.0
    }
    else
    {
        _networkClient.ClearLocalMoveIntent();  // No-op in V5.0
    }

    // Build MoveDir intent from keyboard input (rate-limited to 120Hz)
    uint clientTick = _networkClient.GetCurrentTick();
    return _intentBuilder.OnKeyboardMove(dir, Time.deltaTime, clientTick);
}
```

**Key points:**
1. **Multi-layout support:** Handles both QWERTY (WASD) and AZERTY (ZQSD)
2. **Normalization:** Diagonal movement normalized to magnitude 1.0
3. **State tracking:** Detects start/stop transitions for logging
4. **Local feedback:** Calls `SetLocalMoveIntent()` for rotation/animation (V5.0: no-op)
5. **Intent creation:** Calls `IntentBuilder.OnKeyboardMove()` with rate-limiting

**Returns:**
- `InputIntent.MoveDir` if rate-limit passed (every 8.3ms)
- `InputIntent.Stop` if keys released
- `null` if rate-limit not reached

---

### Click-to-Move Input

```csharp
// InputCollector.cs:199-247
private InputIntent? HandleClickToMoveInput()
{
    var mouse = Mouse.current;
    if (mouse == null) return null;

    // Right-click to move
    if (mouse.rightButton.wasPressedThisFrame)
    {
        Vector3 worldPos = GetMouseWorldPosition();
        if (worldPos != Vector3.zero)
        {
            _currentIsMoving = true;
            _lastClickTarget = worldPos;
            uint clientTick = _networkClient.GetCurrentTick();
            return _intentBuilder.OnClickToMove(worldPos, clientTick);
        }
    }

    // V3.0: Set local move intent for rotation/animation feedback while moving
    if (_currentIsMoving && _lastClickTarget != Vector3.zero)
    {
        Vector3 currentPos = _networkClient.GetVisualPosition();
        Vector3 toTarget = _lastClickTarget - currentPos;
        toTarget.y = 0;  // XZ plane only

        if (toTarget.sqrMagnitude > 1f)  // Not yet arrived
        {
            Vector3 moveDir = toTarget.normalized;
            _networkClient.SetLocalMoveIntent(moveDir);  // No-op in V5.0
        }
        else
        {
            _networkClient.ClearLocalMoveIntent();  // No-op in V5.0
        }
    }

    // S key to stop (common in MOBAs)
    var kb = Keyboard.current;
    if (kb != null && kb.sKey.wasPressedThisFrame)
    {
        _currentIsMoving = false;
        _lastClickTarget = Vector3.zero;
        _networkClient.ClearLocalMoveIntent();
        uint clientTick = _networkClient.GetCurrentTick();
        return _intentBuilder.CreateStop(clientTick);
    }

    return null;
}
```

**Key points:**
1. **Right-click to move:** Standard MOBA control scheme
2. **Ground raycast:** Converts screen position to world position
3. **Stop key (S):** Explicit stop command (common in LoL/Dota)
4. **Local feedback:** Continuous direction update while moving to clicked target (V5.0: no-op)

**Returns:**
- `InputIntent.MoveTo` if clicked ground
- `InputIntent.Stop` if pressed S key
- `null` otherwise

---

### Mouse World Position

```csharp
// InputCollector.cs:304-317
private Vector3 GetMouseWorldPosition()
{
    if (Camera.main == null || Mouse.current == null) return Vector3.zero;

    Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
    int groundLayer = LayerMask.GetMask("Ground");

    if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
    {
        return hit.point;
    }

    return Vector3.zero;
}
```

**Requirements:**
- Ground plane must be on "Ground" layer
- Collider on ground plane for raycasting
- Camera must be tagged "MainCamera"

**Returns:**
- World position where mouse ray hits ground
- `Vector3.zero` if no hit (ignored by caller)

---

## IntentBuilder

### Purpose

Converts raw input to `InputIntent` with rate-limiting. Ensures intents are sent at controlled frequency (120Hz) to avoid spamming server.

**File:** `Assets/Scripts/Client/Input/IntentBuilder.cs`

---

### Rate Limiting

```csharp
// IntentBuilder.cs:36-38
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz (8.3ms)
private float _accumulator;
```

**Why 120Hz?**
- Matches `NetworkClient._inputSendRate` (see Client_Architecture.md)
- Higher frequency = lower input latency
- Lower frequency = less bandwidth, but sluggish controls
- 120Hz is sweet spot for responsive MOBA gameplay

**How it works:**
```csharp
// IntentBuilder.cs:114-147
public InputIntent? OnKeyboardMove(Vector2 inputDir, float dt, uint clientTick = 0)
{
    _accumulator += dt;

    // Check for Stop: no input direction
    if (inputDir.sqrMagnitude < 0.01f)
    {
        if (_wasMoving)
        {
            _wasMoving = false;
            _accumulator = 0;
            var stopIntent = InputIntent.Stop(++_seqId, clientTick);
            return stopIntent;
        }
        return null;
    }

    // Rate limit: 120Hz
    if (_accumulator < INTENT_SEND_INTERVAL)
        return null;

    _accumulator = 0;
    _wasMoving = true;

    // Send normalized direction
    Vector2 normalizedDir = inputDir.normalized;
    var intent = InputIntent.MoveDir(normalizedDir, ++_seqId, clientTick);
    return intent;
}
```

**Flow:**
1. Accumulate time since last intent
2. If keys released: Immediate `Stop` intent (no rate limit)
3. If moving: Check rate limit (8.3ms)
4. If rate limit passed: Create `MoveDir` intent with incremented `_seqId`
5. Otherwise: Return `null` (no intent this frame)

---

### Click-to-Move (Immediate)

```csharp
// IntentBuilder.cs:89-99
public InputIntent? OnClickToMove(Vector3 worldPos, uint clientTick = 0)
{
    // V2.2: Suppress movement during immobilize
    if (_isImmobilizeLocked)
        return null;

    _wasMoving = true;
    var intent = InputIntent.MoveTo(worldPos, ++_seqId, clientTick);
    MovementCycleLogger.LogIntentCreated("MoveTo (click)", _seqId, worldPos);
    return intent;
}
```

**Key difference from WASD:**
- **No rate limiting** - Click is discrete event, sent immediately
- **Resets `_wasMoving`** - Subsequent WASD input starts new movement cycle
- **V2.2:** Respects immobilize lock (CC/stun/root)

---

### Immobilize Lock (V2.2)

**Purpose:** Suppress movement during crowd control (root, stun, etc.)

```csharp
// IntentBuilder.cs:70-79
public void SetImmobilizeLock(bool locked)
{
    if (locked && !_isImmobilizeLocked)
    {
        // Just became locked: reset accumulators
        _immobilizeStopAccumulator = 0;
        _wasMoving = false;
    }
    _isImmobilizeLocked = locked;
}
```

**Usage:**
```csharp
// InputCollector.cs:114-116
bool isImmobilized = _networkClient.IsImmobilized();
_intentBuilder.SetImmobilizeLock(isImmobilized);
```

**Behavior when locked:**
- All movement intents return `null`
- Emits `Stop` intent at 2Hz (prevents server timeout)
- Local intent feedback cleared

**V5.0 Note:** CC system not fully implemented, always returns `false`.

---

### Sequence Tracking

```csharp
// IntentBuilder.cs:33-34
private uint _seqId;
```

**Purpose:**
- Monotonically increasing sequence number
- Server uses for last-input-wins logic
- Client uses for reconciliation (V5.0: not used)

**Incremented when:**
- Creating any intent (`++_seqId`)
- Never decreases, never resets (across entire session)

**Server handling:**
```csharp
// ServerGameLoop.cs:772-785
private bool CheckAndUpdateMovementSeq(int clientId, uint movementSeq)
{
    if (!_lastMovementSeq.TryGetValue(clientId, out uint lastSeq))
    {
        lastSeq = 0;
        _lastMovementSeq[clientId] = 0;
    }

    if (movementSeq <= lastSeq)
        return false;  // Reject out-of-order input

    _lastMovementSeq[clientId] = movementSeq;
    return true;
}
```

---

## Discrete Events

### Event Command Types

```csharp
// InputCollector.cs:252-303
private void HandleDiscreteEvents()
{
    // Jump
    if (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
    {
        HandleJumpInput();
    }

    // Abilities (E/R only when in WASD mode, Q/W used for movement)
    HandleAbilityInput();
}

private void HandleJumpInput()
{
    const float GRAVITY = -20f;
    const float JUMP_HEIGHT = 1.5f;
    float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(GRAVITY) * JUMP_HEIGHT);

    var cmd = GameCommand.Launch(++_commandSequence, Vector3.up * jumpVelocity);
    _networkClient.SendEventCommand(cmd);
}

private void HandleAbilityInput()
{
    var kb = Keyboard.current;
    if (kb == null) return;

    // In WASD mode, only E and R are abilities (Q/W used for movement)
    // In ClickToMove mode, all Q/W/E/R are abilities
    if (_movementMode == MovementMode.ClickToMove)
    {
        if (kb.qKey.wasPressedThisFrame) CastAbility(0);
        else if (kb.wKey.wasPressedThisFrame) CastAbility(1);
    }

    if (kb.eKey.wasPressedThisFrame) CastAbility(2);
    else if (kb.rKey.wasPressedThisFrame) CastAbility(3);
}

private void CastAbility(byte slot)
{
    Vector3 targetPos = GetMouseWorldPosition();
    var cmd = GameCommand.CastAbility(++_commandSequence, slot, targetPos);
    _networkClient.SendEventCommand(cmd);
}
```

**Key points:**
1. **Mode-aware:** WASD mode reserves Q/W for movement (AZERTY layout)
2. **Immediate send:** No rate limiting (discrete events)
3. **Target position:** Abilities include mouse world position (for skillshots)
4. **Separate sequence:** `_commandSequence` (not `_movementSeq`)

---

### Event Buffering

**UDP Redundancy System:**

```csharp
// NetworkClient.cs:248-263
public void SendEventCommand(GameCommand cmd)
{
    if (!_isConnected) return;
    _eventBuffer.Add(cmd);
}
```

**How it works:**
1. Client adds command to `_eventBuffer`
2. Every `InputPacket` includes last N commands (default: 3)
3. Server acknowledges via `snapshot.AckInputSeq`
4. Client removes acknowledged commands from buffer

**Benefits:**
- Reliable delivery without TCP overhead
- Minimal bandwidth cost (commands are small)
- Critical for abilities (must not be lost)

**See:** `Assets/Scripts/Network/NetAdapter/Buffers/InputBuffer.cs`

---

## InputPacket Format

### Structure

```csharp
// InputPacket.cs
public struct InputPacket
{
    public uint ClientTick;           // 4 bytes
    public uint MovementSeq;          // 4 bytes
    public PacketIntentType IntentType; // 1 byte
    public float Payload0;            // 4 bytes (x or entityId)
    public float Payload1;            // 4 bytes (y)
    public byte EventCount;           // 1 byte
    public GameCommand[] Events;      // Variable (max 16 bytes each)
}
```

**Total size:**
- Base: 18 bytes (without events)
- With 1 event: ~34 bytes
- With 3 events: ~66 bytes

**Bandwidth:**
- 120Hz send rate
- Typical: ~34 bytes/packet = 4.08 KB/s
- Max (with 3 events): ~66 bytes/packet = 7.92 KB/s

---

### Intent Types

```csharp
public enum PacketIntentType : byte
{
    None = 0,
    MoveDir = 1,    // WASD keyboard (Payload0=x, Payload1=y)
    MoveTo = 2,     // Click-to-move (Payload0=x, Payload1=y)
    Stop = 3,       // Stop movement (no payload)
    Follow = 4      // Follow entity (Payload0=entityId)
}
```

**Semantic separation:**
- `MoveDir` - Direction vector (normalized), server applies speed
- `MoveTo` - Target position (world coordinates), server handles pathfinding
- `Stop` - Explicit stop, clears movement state
- `Follow` - Target entity ID, server updates target each tick

---

### Factory Methods

```csharp
// InputPacket.cs
public static InputPacket CreateMoveDir(uint clientTick, uint movementSeq, Vector2 direction, GameCommand[] events = null)
{
    return new InputPacket
    {
        ClientTick = clientTick,
        MovementSeq = movementSeq,
        IntentType = PacketIntentType.MoveDir,
        Payload0 = direction.x,
        Payload1 = direction.y,
        EventCount = (byte)(events?.Length ?? 0),
        Events = events
    };
}

public static InputPacket CreateMoveTo(uint clientTick, uint movementSeq, Vector3 worldPos, GameCommand[] events = null)
{
    return new InputPacket
    {
        ClientTick = clientTick,
        MovementSeq = movementSeq,
        IntentType = PacketIntentType.MoveTo,
        Payload0 = worldPos.x,
        Payload1 = worldPos.z,  // Y is always 0 (ground plane)
        EventCount = (byte)(events?.Length ?? 0),
        Events = events
    };
}

public static InputPacket CreateStop(uint clientTick, uint movementSeq, GameCommand[] events = null)
{
    return new InputPacket
    {
        ClientTick = clientTick,
        MovementSeq = movementSeq,
        IntentType = PacketIntentType.Stop,
        Payload0 = 0,
        Payload1 = 0,
        EventCount = (byte)(events?.Length ?? 0),
        Events = events
    };
}
```

**Usage:**
```csharp
// NetworkClient.cs:215-243
private InputPacket CreateInputPacketFromIntent(InputIntent? intent, GameCommand[] eventCommands)
{
    uint clientTick = _simWorld?.Clock.CurrentTick ?? 0;

    if (!intent.HasValue)
    {
        return InputPacket.CreateEventsOnly(clientTick, eventCommands);
    }

    var i = intent.Value;

    switch (i.Type)
    {
        case InputIntentType.MoveDir:
            return InputPacket.CreateMoveDir(clientTick, _movementSeq, i.Direction, eventCommands);

        case InputIntentType.MoveTo:
            return InputPacket.CreateMoveTo(clientTick, _movementSeq, i.WorldPos, eventCommands);

        case InputIntentType.Stop:
            return InputPacket.CreateStop(clientTick, _movementSeq, eventCommands);

        case InputIntentType.Follow:
            return InputPacket.CreateFollow(clientTick, _movementSeq, i.TargetEntityId, eventCommands);

        default:
            return InputPacket.CreateEventsOnly(clientTick, eventCommands);
    }
}
```

---

## Performance Tuning

### Input Send Rate

**Current:** 120Hz (8.3ms interval)

**To change:**

```csharp
// NetworkClient.cs:40
[SerializeField] private float _inputSendRate = 120f;

// IntentBuilder.cs:37
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 1 / 120
```

**Trade-offs:**

| Rate | Latency | Bandwidth | Responsiveness |
|------|---------|-----------|----------------|
| 60Hz | ~16ms avg | 2 KB/s | Acceptable |
| 120Hz (default) | ~8ms avg | 4 KB/s | Smooth |
| 240Hz | ~4ms avg | 8 KB/s | Overkill |

**Recommendation:** 120Hz is optimal for MOBA gameplay.

---

### Event Redundancy Count

**Current:** 3 commands per packet

```csharp
// NetcodeConstants.cs
public const int INPUT_REDUNDANCY_COUNT = 3;
```

**Trade-offs:**

| Count | Reliability | Bandwidth Overhead |
|-------|-------------|-------------------|
| 1 | UDP only (lossy) | None |
| 3 (default) | Very reliable | ~32 bytes/packet |
| 5 | Extremely reliable | ~64 bytes/packet |

**Formula:**
- Packet loss rate: 1% typical, 5% worst-case
- 3 redundant sends: 0.01^3 = 0.0001% loss probability
- 5 redundant sends: 0.01^5 = 0.00000001% loss probability (overkill)

---

## Debugging

### Movement Cycle Logging

**Full input pipeline trace:**

```csharp
// Enable in InputCollector.cs, IntentBuilder.cs, NetworkClient.cs
MovementCycleLogger.LogInputStart(dir);           // Raw input detected
MovementCycleLogger.LogIntentCreated(...);        // Intent created
MovementCycleLogger.LogIntentSent(...);           // Packet sent
```

**Output format:**
```
[08:23:45.123] [INPUT START] dir=(0.71,0.71)
[08:23:45.123] [INTENT CREATED] MoveDir seq=45 vec=(0.71,0.00,0.71) [direction]
[08:23:45.131] [INTENT SENT] seq=45 type=MoveDir payload=(0.71,0.71)
```

**Correlation:**
- Match timestamps to measure intent creation latency
- Match `seq` values to trace intent through pipeline
- Compare with server logs to measure network latency

---

### Input State Inspection

**Public properties for debugging:**

```csharp
// InputCollector.cs:66-86
public bool IsMoving => _currentIsMoving;
public Vector2 MoveDirection => _currentMoveInput;
public uint CommandSequence => _commandSequence;
public uint IntentSequence => _intentBuilder?.CurrentSeqId ?? 0;
```

**Usage:**
```csharp
// In Unity Inspector or debug script
var collector = player.GetComponent<InputCollector>();
Debug.Log($"Moving: {collector.IsMoving}, Dir: {collector.MoveDirection}, Seq: {collector.IntentSequence}");
```

---

## Related Documentation

- **[Client_Architecture.md](Client_Architecture.md)** - Client sync and rendering
- **[Server/Server_Loop.md](../Server/Server_Loop.md)** - Server input processing
- **[Network/02_Message_Specifications.md](../Network/02_Message_Specifications.md)** - InputPacket format details
- **[Network/03_Data_Flow.md](../Network/03_Data_Flow.md)** - Complete input→visual flow

---

**Last Updated:** 2026-02-03
**Version:** V3.0 (Intent-based with 120Hz rate limiting)

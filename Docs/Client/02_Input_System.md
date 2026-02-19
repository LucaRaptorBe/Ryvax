# Input System

**V3.1 — Intent-Based Input with Configurable Keybinds, Cast Modes, and Targeting**

The input system converts raw user input into high-level movement intents and game commands sent to the server. Keybinds, movement mode, and cast mode are configurable via the Settings system.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                          INPUT PIPELINE                               │
├──────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  Raw Input (every frame)                                              │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │  Unity Input System (Keyboard.current, Mouse.current)          │   │
│  └──────────────────────────┬─────────────────────────────────────┘   │
│                              │                                        │
│                              ▼                                        │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │  InputCollector.Update()                                       │   │
│  │  ├─ HandleMovementInput()                                      │   │
│  │  │   ├─ WASD mode: HandleWASDInput() → IntentBuilder           │   │
│  │  │   └─ Click mode: HandleClickToMoveInput() → IntentBuilder   │   │
│  │  ├─ CastController.UpdateAiming() (if aiming)                  │   │
│  │  ├─ HandleDiscreteEvents()                                     │   │
│  │  │   ├─ Jump (Space)                                           │   │
│  │  │   └─ Abilities → CastController.TryCastAbility()            │   │
│  │  └─ HandleTargetingInput()                                     │   │
│  │      ├─ Tab → CycleTarget                                     │   │
│  │      ├─ LClick enemy → LockTarget                              │   │
│  │      ├─ LClick nothing → ClearTarget                           │   │
│  │      └─ RClick enemy → LockTarget + AttackTarget command       │   │
│  └──────────┬──────────────────────────────┬─────────────────────┘   │
│              │                              │                         │
│              ▼                              ▼                         │
│  ┌─────────────────────┐    ┌──────────────────────────────────┐     │
│  │  NetworkClient      │    │  NetworkClient                    │     │
│  │  .SendInputIntent() │    │  .SendEventCommand()              │     │
│  │  (movement intents) │    │  (abilities, jump, attack)        │     │
│  └──────────┬──────────┘    └──────────────┬───────────────────┘     │
│              │                              │                         │
│              └──────────┬───────────────────┘                         │
│                         ▼                                             │
│              InputPacket (14 bytes header + events)                   │
│              → UDP unreliable → Server                                │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

**Key Files:**

| File | Purpose |
|------|---------|
| `Scripts/Client/Input/InputCollector.cs` | Raw input capture, routes to subsystems |
| `Scripts/Client/Input/IntentBuilder.cs` | Rate-limited intent creation (120Hz) |
| `Scripts/Client/Input/InputIntent.cs` | Intent value type (MoveDir, MoveTo, Stop, Follow) |
| `Scripts/Client/Input/InputBuffer.cs` | Ring buffer for event command redundancy |
| `Scripts/Client/Casting/CastController.cs` | Cast mode logic |
| `Scripts/Client/Casting/SkillshotIndicator.cs` | Visual aiming indicator |
| `Scripts/Client/Targeting/TargetingSystem.cs` | Tab-target system |
| `Scripts/Client/Targeting/TargetIndicator.cs` | Visual ring under targeted entity |
| `Scripts/Client/View/UI/Settings/GameSettings.cs` | Serializable keybind/cast mode settings |
| `Scripts/Network/NetAdapter/Messages/InputPacket.cs` | Network message format |

---

## InputCollector

### Purpose

Captures raw keyboard/mouse input each frame. Routes movement to IntentBuilder, abilities to CastController, and targeting to TargetingSystem. Reads keybinds and modes from SettingsManager.

**File:** `Scripts/Client/Input/InputCollector.cs`
**Namespace:** `MOBANet.UnityView.Input`
**Execution Order:** `DefaultExecutionOrder(-100)` — runs before most scripts

---

### Update Loop

```csharp
// InputCollector.cs:110
void Update()
{
    if (_networkClient == null || !_networkClient.IsConnected) return;
    if (_networkClient.LocalEntityId == 0) return;

    // Lazy-init targeting system
    if (!_targetingSystemInitialized) { _targetingSystem.Initialize(_networkClient); ... }

    // Block all input when settings UI is open
    if (SettingsManager.IsUIBlockingInput) return;

    HandleMovementInput();           // 1. Movement intents
    _castController?.UpdateAiming(); // 2. Aiming indicator update
    HandleDiscreteEvents();          // 3. Jump, abilities
    HandleTargetingInput();          // 4. Tab-target, click-select
}
```

**Order matters:** Movement first, then abilities (so CastController can override movement if aiming).

---

## Movement Input

### WASD Mode (V3.1)

```csharp
// InputCollector.cs:248
private InputIntent? HandleWASDInput()
{
    var kb = Keyboard.current;
    Vector2 dir = Vector2.zero;

    // V3.1: Layout from settings (AZERTY vs QWERTY)
    bool azerty = GetSettings()?.keyboardLayout == KeyboardLayout.AZERTY;

    if (azerty)
    {   // ZQSD
        if (kb.zKey.isPressed) dir.y += 1;
        if (kb.sKey.isPressed) dir.y -= 1;
        if (kb.dKey.isPressed) dir.x += 1;
        if (kb.qKey.isPressed) dir.x -= 1;
    }
    else
    {   // WASD
        if (kb.wKey.isPressed) dir.y += 1;
        if (kb.sKey.isPressed) dir.y -= 1;
        if (kb.dKey.isPressed) dir.x += 1;
        if (kb.aKey.isPressed) dir.x -= 1;
    }

    if (dir.sqrMagnitude > 1f) dir = dir.normalized;

    // V3.0: SetLocalMoveIntent (no-op in V5.0)
    _networkClient.SetLocalMoveIntent(new Vector3(dir.x, 0f, dir.y));

    // Build MoveDir intent (rate-limited 120Hz by IntentBuilder)
    return _intentBuilder.OnKeyboardMove(dir, Time.deltaTime, clientTick);
}
```

**Returns:**
- `InputIntent.MoveDir` if rate-limit passed (every 8.3ms)
- `InputIntent.Stop` if keys released (immediate, no rate-limit)
- `null` if rate-limit not reached

---

### Click-to-Move Mode

```csharp
// InputCollector.cs:314
private InputIntent? HandleClickToMoveInput()
{
    // Right-click on enemy: handled by HandleTargetingInput(), skip movement
    if (mouse.rightButton.wasPressedThisFrame)
    {
        var enemy = RaycastEnemy();
        if (enemy == null)
        {
            // Right-click on ground: MoveTo
            Vector3 worldPos = GetMouseWorldPosition();
            return _intentBuilder.OnClickToMove(worldPos, clientTick);
        }
    }

    // S key: explicit stop
    if (kb.sKey.wasPressedThisFrame)
    {
        return _intentBuilder.CreateStop(clientTick);
    }

    return null;
}
```

**Key difference from old version:** Right-click on enemy now routes to `AttackTarget` command instead of MoveTo.

---

### Mouse World Position

```csharp
// InputCollector.cs:577
private Vector3 GetMouseWorldPosition()
{
    Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
    if (Physics.Raycast(ray, out RaycastHit hit, 1000f, LayerMask.GetMask("Ground")))
        return hit.point;
    return Vector3.zero;
}
```

**Requirements:** Ground plane on "Ground" layer with a Collider.

---

## IntentBuilder

### Purpose

Converts raw input to `InputIntent` with rate-limiting. Ensures WASD intents are sent at 120Hz max.

**File:** `Scripts/Client/Input/IntentBuilder.cs`
**Namespace:** `MOBANet.Client.Input`

---

### Rate Limiting

```csharp
// IntentBuilder.cs:37
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz (8.3ms)
```

**Keyboard Move (rate-limited):**

```csharp
// IntentBuilder.cs:114
public InputIntent? OnKeyboardMove(Vector2 inputDir, float dt, uint clientTick)
{
    // During immobilize: emit Stop at 2Hz instead
    if (_isImmobilizeLocked) { ... return Stop at 2Hz ... }

    _accumulator += dt;

    // Released keys: immediate Stop (no rate-limit)
    if (inputDir.sqrMagnitude < 0.01f)
    {
        if (_wasMoving) { _wasMoving = false; return InputIntent.Stop(++_seqId, clientTick); }
        return null;
    }

    // Rate limit: 120Hz
    if (_accumulator < INTENT_SEND_INTERVAL) return null;
    _accumulator = 0;
    _wasMoving = true;

    return InputIntent.MoveDir(inputDir.normalized, ++_seqId, clientTick);
}
```

**Click-to-Move (immediate):**

```csharp
// IntentBuilder.cs:89
public InputIntent? OnClickToMove(Vector3 worldPos, uint clientTick)
{
    if (_isImmobilizeLocked) return null;
    _wasMoving = true;
    return InputIntent.MoveTo(worldPos, ++_seqId, clientTick);
}
```

No rate-limiting for clicks — they are discrete events.

---

### Sequence Tracking

```csharp
private uint _seqId;  // Monotonic, never resets
```

Server uses `_seqId` for:
- `CheckAndUpdateMovementSeq()` — reject out-of-order inputs
- `AckMovementSeq` in SnapshotDelta — acknowledge processed input

---

### Immobilize Lock (V2.2)

During CC (root/stun), movement intents are suppressed. Instead, `Stop` is emitted at 2Hz to prevent server watchdog timeout.

```csharp
// IntentBuilder.cs:70
public void SetImmobilizeLock(bool locked) { ... }
```

**V5.0 Note:** `NetworkClient.IsImmobilized()` always returns `false` — CC system not yet wired.

---

## Ability System

### Cast Modes

Three cast modes, configurable per player in Settings:

| Mode | Behavior |
|------|----------|
| **QuickCast** | Key press → fire immediately at mouse position |
| **NormalCast** | Key press → show indicator → 2nd press/LClick confirms, RMB cancels |
| **QuickCastWithIndicator (QCWI)** | Key press → show indicator → key release confirms, RMB cancels |

**File:** `Scripts/Client/Casting/CastController.cs`

---

### Ability Input Flow

```csharp
// InputCollector.cs:399
private void HandleAbilityInput()
{
    // If currently aiming, handle confirm/cancel instead
    if (_castController.IsAiming) { HandleAimingInput(); return; }

    // Read keybinds from GameSettings
    var settings = GetSettings();
    for (byte slot = 0; slot < 4; slot++)
    {
        KeyCode key = settings.GetAbilityKey(slot);
        if (GetMovementMode() == WASD && IsMovementKey(key)) continue;  // Skip conflicts
        if (IsKeyPressed(key))
        {
            TryCastAbility(slot, settings);
            return;
        }
    }
}
```

**TryCastAbility flow:**
1. Check cooldown (`NetworkClient.IsAbilityOnCooldown(slot)`)
2. Get ability definition (`IAbilityDefinition`) for target type and range
3. If NormalCast/QCWI + Skillshot: `CastController.TryStartCast()` → show indicator
4. If QuickCast: `FireAbilityImmediate()` → send GameCommand immediately

---

### Aiming Input

```csharp
// InputCollector.cs:434
private void HandleAimingInput()
{
    // RMB or Escape: cancel aiming
    // NormalCast: LClick or same key = confirm
    // QCWI: key release = confirm
    // Different ability key: cancel current + start new
}

private void ConfirmAndSendCast()
{
    var result = _castController.ConfirmCast();  // returns nullable tuple
    if (result.HasValue)
    {
        var cmd = GameCommand.CastAbility(0, result.Value.slot,
            new Vector2(result.Value.targetPos.x, result.Value.targetPos.z));
        _networkClient.SendEventCommand(cmd);
    }
}
```

---

### Fallback Ability Input

When `SettingsManager` is not yet initialized (e.g. very first frame before settings load), `HandleAbilityInput` calls a hardcoded fallback:

```csharp
// InputCollector.cs:544
private void HandleAbilityInputFallback()
{
    var kb = Keyboard.current;
    if (kb == null) return;

    // Fallback: Q/E/R (skips W/A which are movement keys in WASD mode)
    if (kb.qKey.wasPressedThisFrame) FireAbilityImmediate(0);
    else if (kb.eKey.wasPressedThisFrame) FireAbilityImmediate(2);
    else if (kb.rKey.wasPressedThisFrame) FireAbilityImmediate(3);
}
```

All three fire via `FireAbilityImmediate` (QuickCast, no indicator). Slot 1 (`W`) is intentionally skipped to avoid WASD conflict.

---

### SkillshotIndicator

**File:** `Scripts/Client/Casting/SkillshotIndicator.cs`

Draws a directional arrow from the player toward the mouse cursor up to ability range. Implemented as a 7-point `LineRenderer` (start → tip → left wing → tip → right wing → tip → start), rendered in cyan at Y+0.05 to sit just above the ground plane. Created as a child of the player GameObject; shown while aiming, hidden otherwise. Only arrow shape is implemented — no cone or circle variants exist.

---

## Targeting System

### Purpose

Tab-target system for selecting enemies. Integrates with abilities (targeted spells) and auto-attack.

**File:** `Scripts/Client/Targeting/TargetingSystem.cs`
**Namespace:** `MOBANet.Client.Targeting`

---

### Input Handling

```csharp
// InputCollector.cs:596
private void HandleTargetingInput()
{
    // Tab: cycle through nearby enemies
    if (kb.tabKey.wasPressedThisFrame) _targetingSystem.CycleTarget();

    // Escape: clear current target (only if one exists; otherwise SettingsManager handles it)
    if (kb.escapeKey.wasPressedThisFrame && _targetingSystem.HasTarget)
    {
        _targetingSystem.ClearTarget();
        return;
    }

    // LClick enemy: select/lock target
    // LClick nothing: clear target
    if (mouse.leftButton.wasPressedThisFrame)
    {
        var enemy = RaycastEnemy();
        if (enemy != null) _targetingSystem.LockTarget(enemy.EntityId);
        else _targetingSystem.ClearTarget();
    }

    // RClick enemy: lock target + attack order
    if (mouse.rightButton.wasPressedThisFrame)
    {
        var enemy = RaycastEnemy();
        if (enemy != null)
        {
            _targetingSystem.LockTarget(enemy.EntityId);
            var cmd = GameCommand.AttackTarget(0, enemy.EntityId);
            _networkClient.SendEventCommand(cmd);
        }
    }
}
```

**RaycastEnemy()** — Raycasts from camera through mouse position, checks if hit is a non-local `PlayerView` on a different team.

### Escape Key Ownership

Escape is handled at two levels depending on state:

| Condition | Handler | Effect |
|-----------|---------|--------|
| `_castController.IsAiming` | `HandleAimingInput` | `CancelCast()`, returns |
| `_targetingSystem.HasTarget` | `HandleTargetingInput` | `ClearTarget()`, returns |
| Neither | `SettingsManager` | Opens/closes settings UI |

`InputCollector` consumes Escape with `return` in both cases, so `SettingsManager` only sees it when there is nothing to dismiss in-game.

---

## InputPacket Format

### Structure (actual wire format)

```csharp
public struct InputPacket
{
    public uint ClientTick;               // 4 bytes
    public uint MovementSeq;              // 4 bytes
    public PacketIntentType IntentType;   // 1 byte (enum)
    public short Payload0;                // 2 bytes (quantized)
    public short Payload1;                // 2 bytes (quantized)
    public byte CommandCount;             // 1 byte
    // Header total: 14 bytes
    public GameCommand[] Commands;        // Variable length (redundancy buffer)
}
```

**Payload quantization:**
- `MoveDir`: `Payload0 = dir.x * 127`, `Payload1 = dir.z * 127` (short, ~0.008 precision)
- `MoveTo`: `Payload0 = pos.x * 10`, `Payload1 = pos.z * 10` (short, 0.1 unit precision)
- `Follow`: `Payload0 = entityId low 16 bits`, `Payload1 = entityId high 16 bits`
- `Stop/None`: both 0

### Intent Types

```csharp
public enum PacketIntentType : byte
{
    None = 0,      // Only event commands, no movement
    MoveDir = 1,   // WASD: normalized direction
    MoveTo = 2,    // Click: world position
    Stop = 3,      // Explicit stop
    Follow = 4     // Follow entity
}
```

### Bandwidth

| Scenario | Packet size | Rate | Bandwidth |
|----------|-------------|------|-----------|
| Movement only | 14 bytes | 120Hz | 1.6 KB/s |
| With 3 redundant events | ~50 bytes | 120Hz | ~6 KB/s |
| Idle (no intent) | 14 bytes | 120Hz | 1.6 KB/s |

---

## Settings Integration

### GameSettings

**File:** `Scripts/Client/View/UI/Settings/GameSettings.cs`

```csharp
public class GameSettings
{
    public MovementMode movementMode;        // WASD or ClickToMove
    public KeyboardLayout keyboardLayout;    // QWERTY or AZERTY
    public CastMode defaultCastMode;         // QuickCast, NormalCast, QCWI
    public KeyCode ability1Key, ability2Key, ability3Key, ability4Key;
    // ...
}
```

**Accessed via:** `SettingsManager.Instance.CurrentSettings`

### Movement Key Conflict Detection

In WASD mode, ability keys that conflict with movement keys are skipped:

```csharp
// InputCollector.cs:558
private bool IsMovementKey(KeyCode kc)
{
    bool azerty = GetSettings()?.keyboardLayout == KeyboardLayout.AZERTY;
    if (azerty) return kc is Z or Q or S or D;
    else        return kc is W or A or S or D;
}
```

### UI Blocking

```csharp
if (SettingsManager.IsUIBlockingInput) return;  // Skip all game input
```

When the Settings UI is open, all game input is suppressed.

---

## Performance Tuning

### Input Send Rate

| Rate | Avg latency | Bandwidth | Recommendation |
|------|-------------|-----------|----------------|
| 60Hz | ~16ms | ~1 KB/s | Acceptable |
| **120Hz (default)** | **~8ms** | **~1.6 KB/s** | **Optimal for MOBA** |
| 240Hz | ~4ms | ~3.2 KB/s | Diminishing returns |

**To change:** `NetworkClient._inputSendRate` (Inspector) and `IntentBuilder.INTENT_SEND_INTERVAL` (code).

### Event Redundancy

```csharp
public const int INPUT_REDUNDANCY_COUNT = 3;  // NetcodeConstants.cs:45
```

With 1% packet loss: `0.01^3 = 0.0001%` delivery failure. 3 is the sweet spot.

---

## Debugging

### Movement Cycle Logging

Trace an input through the entire pipeline by matching `seq` values:

```
[INPUT START] dir=(0.71,0.71)
[INTENT CREATED] MoveDir seq=45 vec=(0.71,0.00,0.71) [direction]
[INTENT SENT] seq=45 type=MoveDir payload=(90,90)     ← quantized shorts
[SNAPSHOT RECV] tick=1234 pos=(10.5,0.0,20.3) vel=(5.7,0.0,5.7)
[RENDER POS] pos=(10.6,0.0,20.4)
```

### Public Properties

```csharp
// InputCollector
public bool IsMoving => _currentIsMoving;
public Vector2 MoveDirection => _currentMoveInput;
public uint IntentSequence => _intentBuilder?.CurrentSeqId ?? 0;
public TargetingSystem TargetingSystem => _targetingSystem;
```

---

## Related Documentation

- **[01_Client_Architecture.md](01_Client_Architecture.md)** — Client sync, rendering, NetworkClient
- **[Server/01_Server_Loop.md](../Server/01_Server_Loop.md)** — Server-side input processing
- **[Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)** — Full system overview

---

**Last Updated:** 2026-02-18
**Version:** V3.1 (Configurable keybinds, cast modes, targeting)

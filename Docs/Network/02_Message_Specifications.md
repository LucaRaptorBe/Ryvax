# Message Specifications

> **Status:** Production
> **Version:** 4.0 (Intent-based)
> **Last Updated:** 2026-02-18

## Overview

This document specifies the **byte-level format** of all network messages in Ryvax, including quantization algorithms and payload interpretation rules.

**Design Principles:**

- **Compact:** Use smallest data type that preserves required precision
- **Blittable:** Struct layout enables zero-copy serialization
- **Versioned:** Each message has version field to detect mismatches
- **Intent-Based:** Movement uses distinct types (MoveDir, MoveTo) instead of raw state

---

## Message Type Summary

| Message | Size | Direction | Frequency | Purpose |
|---------|------|-----------|-----------|---------|
| `InputPacket` | ~14-56 bytes | C→S | 120Hz (continuous input) | Player inputs + events |
| `SnapshotDelta` | ~44-300+ bytes | S→C | 60Hz | World state (all entities) |
| `GameCommand` | 14 bytes | C→S | On-demand | Discrete actions (jump, cast) |
| `ReliableEvent` | 17 bytes | S→C | On-demand | Critical events (spawn, death) |
| `MatchConfig` | Variable | S→C | Once per connect | Match initialization (IBroadcast) |

---

## InputPacket (Client → Server)

### Purpose

Bundles player inputs for UDP transport with redundancy. Contains:

1. **Movement Intent:** One of MoveDir, MoveTo, Stop, Follow
2. **Event Commands:** Discrete actions (jump, spell, attack)

**File:** `Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs`

### Packet Structure (v4)

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Offset │ Description       │
├─────────────────────────────────────────────────────────────┤
│ ClientTick     │ uint   │ 4    │ 0      │ Client tick stamp │
│ MovementSeq    │ uint   │ 4    │ 4      │ Monotonic seq     │
│ IntentType     │ byte   │ 1    │ 8      │ Movement intent   │
│ Payload0       │ short  │ 2    │ 9      │ Type-dependent    │
│ Payload1       │ short  │ 2    │ 11     │ Type-dependent    │
│ CommandCount   │ byte   │ 1    │ 13     │ Event count       │
│ Commands[]     │ var    │ var  │ 14     │ GameCommand array │
└─────────────────────────────────────────────────────────────┘

Base size: 14 bytes (no event commands)
With 3 commands: 14 + (3 × 14) = 56 bytes
```

### Intent Types

```csharp
public enum PacketIntentType : byte
{
    None = 0,      // No movement (only events)
    MoveDir = 1,   // WASD directional movement
    MoveTo = 2,    // Click-to-move to world position
    Stop = 3,      // Stop moving
    Follow = 4     // Follow an entity
}
```

### Payload Interpretation Table

| IntentType | Payload0 | Payload1 | Purpose |
|------------|----------|----------|---------|
| `MoveDir` (1) | `dirX × 127` | `dirZ × 127` | Normalized direction (WASD) |
| `MoveTo` (2) | `posX × 10` | `posZ × 10` | World position (click) |
| `Stop` (3) | 0 | 0 | Unused |
| `Follow` (4) | `entityId & 0xFFFF` | `entityId >> 16` | Target entity ID |

**Critical Rule:** Never use wrong accessor for intent type! Calling `GetDirection()` on a `MoveTo` packet will decode position as direction (wrong scale).

### Quantization Algorithms

#### Direction (MoveDir)

**Range:** -1.0 to +1.0 (normalized vector)
**Precision:** 1/127 ≈ 0.0079 per axis
**Encoding:**

```csharp
short QuantizeDirection(float v)
{
    return (short)(Mathf.Clamp(v, -1f, 1f) * 127f);
}
```

**Decoding:**

```csharp
float DequantizeDirection(short v)
{
    return v / 127f;
}
```

**Example:**

```
Input:  dir = (1.0, 0.0)       // Right
Encode: Payload0 = 127, Payload1 = 0
Decode: dir = (1.0, 0.0)       // Exact
```

**Error:** Max error ±0.0079 (negligible for input direction).

#### Position (MoveTo)

**Range:** ±3276.7 units (sufficient for 200×200 map + margin)
**Precision:** 0.1 units
**Encoding:**

```csharp
short QuantizePosition(float v)
{
    return (short)Mathf.Clamp(v * 10f, short.MinValue, short.MaxValue);
}
```

**Decoding:**

```csharp
float DequantizePosition(short v)
{
    return v / 10f;
}
```

**Example:**

```
Input:  pos = (42.7, -15.3)
Encode: Payload0 = 427, Payload1 = -153
Decode: pos = (42.7, -15.3)    // Exact
```

**Error:** Max error ±0.05 units (imperceptible in gameplay).

### Factory Methods

#### MoveDir (WASD Movement)

```csharp
var packet = InputPacket.CreateMoveDir(
    clientTick: 1234,
    movementSeq: 42,
    direction: new Vector2(1f, 0f),  // Must be normalized!
    eventCommands: null              // Optional events
);
```

**Wire Format:**

```
ClientTick    = 1234
MovementSeq   = 42
IntentType    = 1 (MoveDir)
Payload0      = 127    (dirX = 1.0 × 127)
Payload1      = 0      (dirZ = 0.0 × 127)
CommandCount  = 0
```

#### MoveTo (Click Movement)

```csharp
var packet = InputPacket.CreateMoveTo(
    clientTick: 1234,
    movementSeq: 43,
    targetPos: new Vector2(50f, 25.5f),
    eventCommands: null
);
```

**Wire Format:**

```
ClientTick    = 1234
MovementSeq   = 43
IntentType    = 2 (MoveTo)
Payload0      = 500    (posX = 50.0 × 10)
Payload1      = 255    (posZ = 25.5 × 10)
CommandCount  = 0
```

#### Stop (Explicit Stop)

```csharp
var packet = InputPacket.CreateStop(
    clientTick: 1234,
    movementSeq: 44
);
```

**Wire Format:**

```
IntentType    = 3 (Stop)
Payload0      = 0
Payload1      = 0
```

#### Follow (Entity Tracking)

```csharp
var packet = InputPacket.CreateFollow(
    clientTick: 1234,
    movementSeq: 45,
    targetEntityId: 0x12345678
);
```

**Wire Format:**

```
IntentType    = 4 (Follow)
Payload0      = 0x5678  (low 16 bits)
Payload1      = 0x1234  (high 16 bits)
```

**Entity ID Reconstruction:**

```csharp
uint entityId = (uint)((ushort)Payload0 | ((ushort)Payload1 << 16));
// = 0x12345678
```

### Event Commands (Redundancy Array)

**Field:** `Commands[]` - Array of `GameCommand` structs
**Count:** `CommandCount` - Number of commands in array

**Purpose:** Include last N commands for UDP redundancy (tolerates packet loss).

**Example with 3 redundant commands:**

```
CommandCount = 3
Commands[0] = { Seq=40, Category=Movement, Action=Start, ... }
Commands[1] = { Seq=41, Category=Movement, Action=Change, ... }
Commands[2] = { Seq=42, Category=Ability, Action=CastQ, ... }
```

**Server Dedup:** Server tracks `_lastProcessedSeq[clientId]` and skips `cmd.Sequence <= lastSeq`.

**Bandwidth:** Each command adds 14 bytes. With 3 commands: 14 + 42 = 56 bytes total.

### Accessors (Decode Helpers)

**Critical:** Only use the accessor matching the intent type!

```csharp
// For MoveDir packets ONLY
Vector2 dir = packet.GetDirection();

// For MoveTo packets ONLY
Vector2 pos = packet.GetTargetPosition();

// For Follow packets ONLY
uint targetId = packet.GetFollowTargetId();

// Event commands (any packet)
uint maxSeq = packet.GetMaxEventSequence();
```

**Violation Example (Wrong!):**

```csharp
// Packet is MoveDir with Payload0=127 (dirX=1.0)
Vector2 pos = packet.GetTargetPosition();  // WRONG ACCESSOR!
// Result: pos.x = 127 / 10 = 12.7 (interpreted as position instead of direction!)
```

### Version Compatibility

```csharp
public const ushort MSG_VERSION = 4;  // v4: Intent-based
```

**Changelog:**

- **v1-v3:** Legacy (deprecated, rejected by server)
- **v4:** Intent-based with type-specific payloads (current)

**Server Validation:**

```csharp
if (packet.Version < 4)
{
    Debug.LogWarning($"Rejected old InputPacket v{packet.Version}");
    return;
}
```

> **Note (currently inert):** `InputPacket.Version` is a read-only property that always returns the compile-time constant `MSG_VERSION` (see `InputPacket.cs:53`: `public ushort Version => MSG_VERSION`). Because client and server share the same binary, `packet.Version < InputPacket.MSG_VERSION` is always `false` and the guard never fires. The check becomes meaningful only when a client built against an older version connects to a newer server — a scenario that does not occur in the current single-binary setup. When client and server are shipped as separate binaries this must be revisited.

---

## SnapshotDelta (Server → Client)

### Purpose

Contains **full world state** for a given server tick. Sent unreliable at 60Hz.

**File:** `Assets/Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs`

### Packet Structure

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Offset │ Description       │
├─────────────────────────────────────────────────────────────┤
│ ServerTick     │ uint   │ 4    │ 0      │ Simulation tick   │
│ AckInputSeq    │ uint   │ 4    │ 4      │ Last input ACK    │
│ AckMovementSeq │ uint   │ 4    │ 8      │ Last movement ACK │
│ EntityCount    │ ushort │ 2    │ 12     │ Entity count      │
│ Entities[]     │ var    │ var  │ 14     │ EntityState array │
└─────────────────────────────────────────────────────────────┘

Header size: 14 bytes
Per entity: 30 bytes
Total (10 entities): 14 + (10 × 30) = 314 bytes
```

### EntityState Structure

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Offset │ Description       │
├─────────────────────────────────────────────────────────────┤
│ EntityId       │ uint   │ 4    │ 0      │ Unique ID         │
│ EntityType     │ byte   │ 1    │ 4      │ Player/Minion/etc │
│ Flags          │ byte   │ 1    │ 5      │ Alive/Spawned/etc │
│ PosX           │ ushort │ 2    │ 6      │ X position (quant)│
│ PosY           │ ushort │ 2    │ 8      │ Y height (quant)  │
│ PosZ           │ ushort │ 2    │ 10     │ Z position (quant)│
│ RotY           │ ushort │ 2    │ 12     │ Y rotation (quant)│
│ Health         │ ushort │ 2    │ 14     │ Current HP        │
│ State          │ byte   │ 1    │ 16     │ Idle/Moving/etc   │
│ VelX           │ short  │ 2    │ 17     │ X velocity (quant)│
│ VelZ           │ short  │ 2    │ 19     │ Z velocity (quant)│
│ VelY           │ short  │ 2    │ 21     │ Y velocity (quant)│
│ SpeedQ         │ ushort │ 2    │ 23     │ Speed (quant)     │
│ EventFlags     │ byte   │ 1    │ 25     │ CC/blink/dash     │
│ Cd0            │ byte   │ 1    │ 26     │ Ability 0 cooldown│
│ Cd1            │ byte   │ 1    │ 27     │ Ability 1 cooldown│
│ Cd2            │ byte   │ 1    │ 28     │ Ability 2 cooldown│
│ Cd3            │ byte   │ 1    │ 29     │ Ability 3 cooldown│
└─────────────────────────────────────────────────────────────┘

Total: 30 bytes per entity
```

### Position Quantization (16-bit)

**Map Size:** 200×200 units
**Height Range:** 0-20 units
**Precision:** 200 / 65535 ≈ 0.003 units per axis

**Encoding (X/Z):**

```csharp
ushort QuantizePosition(float value, float mapScale = 200f)
{
    float normalized = (value + mapScale / 2f) / mapScale;  // Map to [0, 1]
    return (ushort)(Mathf.Clamp01(normalized) * 65535f);
}
```

**Decoding (X/Z):**

```csharp
float DequantizePosition(ushort value, float mapScale = 200f)
{
    return (value / 65535f) * mapScale - (mapScale / 2f);
}
```

**Example:**

```
Input:  pos.x = 50.0 (world space)
Encode: normalized = (50 + 100) / 200 = 0.75
        PosX = 0.75 × 65535 = 49151
Decode: pos.x = (49151 / 65535) × 200 - 100 = 50.0  (exact)
```

**Encoding (Y - Height):**

```csharp
ushort QuantizeHeight(float value, float heightScale = 20f)
{
    float normalized = value / heightScale;  // Map to [0, 1]
    return (ushort)(Mathf.Clamp01(normalized) * 65535f);
}
```

**Example:**

```
Input:  pos.y = 5.0
Encode: normalized = 5.0 / 20 = 0.25
        PosY = 0.25 × 65535 = 16383
Decode: pos.y = (16383 / 65535) × 20 = 5.0  (exact)
```

**Error:** Max error ±0.0015 units (sub-pixel precision).

### Rotation Quantization (16-bit)

**Range:** 0-360 degrees
**Precision:** 360 / 65535 ≈ 0.0055 degrees

**Encoding:**

```csharp
ushort QuantizeRotation(float degrees)
{
    float normalized = ((degrees % 360f) + 360f) % 360f / 360f;  // Wrap to [0, 1]
    return (ushort)(normalized * 65535f);
}
```

**Decoding:**

```csharp
float DequantizeRotation(ushort value)
{
    return (value / 65535f) * 360f;
}
```

**Example:**

```
Input:  rotY = 90.0°
Encode: normalized = 90 / 360 = 0.25
        RotY = 0.25 × 65535 = 16383
Decode: rotY = (16383 / 65535) × 360 = 90.0°  (exact)
```

### Velocity Quantization (signed 16-bit)

**Range:** ±327.67 units/sec (sufficient for 8 u/s base speed + buffs)
**Precision:** 0.01 units/sec

**Encoding:**

```csharp
short QuantizeVelocity(float value)
{
    return (short)Mathf.Clamp(value * 100f, short.MinValue, short.MaxValue);
}
```

**Decoding:**

```csharp
float DequantizeVelocity(short value)
{
    return value / 100f;
}
```

**Example:**

```
Input:  vel.x = 8.0 u/s
Encode: VelX = 8.0 × 100 = 800
Decode: vel.x = 800 / 100 = 8.0  (exact)

Input:  vel.y = -9.8 (gravity)
Encode: VelY = -9.8 × 100 = -980
Decode: vel.y = -980 / 100 = -9.8  (exact)
```

**Max Velocity:** ±327.67 u/s (far beyond gameplay needs).

### Speed Quantization (unsigned 16-bit)

**Range:** 0-6553.5 units/sec
**Precision:** 0.1 units/sec

**Encoding:**

```csharp
ushort QuantizeSpeed(float value)
{
    return (ushort)Mathf.Clamp(value * 10f, 0, ushort.MaxValue);
}
```

**Decoding:**

```csharp
float DequantizeSpeed(ushort value)
{
    return value / 10f;
}
```

**Example:**

```
Input:  speed = 8.0 u/s
Encode: SpeedQ = 8.0 × 10 = 80
Decode: speed = 80 / 10 = 8.0  (exact)
```

### Cooldown Quantization (8-bit)

**Range:** 0-60 seconds
**Precision:** 60 / 255 ≈ 0.24 seconds
**Encoding:** Each ability slot (0-3) is quantized to a byte.

```csharp
byte QuantizeCooldown(float[] cooldowns, int slot)
{
    if (cooldowns == null || slot >= cooldowns.Length) return 0;
    float cd = cooldowns[slot];
    if (cd <= 0f) return 0;
    return (byte)Mathf.Clamp(cd / 60f * 255f, 1, 255);
}
```

**Decoding:**

```csharp
float GetCooldown(int slot) => slot switch
{
    0 => Cd0 / 255f * 60f,
    1 => Cd1 / 255f * 60f,
    2 => Cd2 / 255f * 60f,
    3 => Cd3 / 255f * 60f,
    _ => 0f
};
```

**Example:**

```
Input:  cooldown = 10.0 seconds
Encode: Cd0 = 10.0 / 60.0 × 255 = 42
Decode: cd = 42 / 255 × 60 = 9.88s  (±0.12s, acceptable for UI)
```

**Note:** Cooldowns > 60s are clamped. Value 0 = ability ready, value 1-255 = on cooldown.

### Entity Flags

```csharp
[Flags]
public enum EntityFlags : byte
{
    None = 0,
    Alive = 1 << 0,        // Entity is alive
    Changed = 1 << 1,      // State changed this tick
    Spawned = 1 << 2,      // Just spawned
    Destroyed = 1 << 3,    // Just destroyed
    Owned = 1 << 4,        // Owned by receiving client
    Relevant = 1 << 5,     // Relevant to client (AOI)
    // Bits 6-7 reserved
}
```

**Usage:**

```csharp
bool isAlive = (state.Flags & EntityFlags.Alive) != 0;
bool justSpawned = (state.Flags & EntityFlags.Spawned) != 0;
```

### Event Flags

```csharp
[Flags]
public enum EntityEventFlags : byte
{
    None = 0,
    ImmobilizeCC = 1 << 0,    // Root/stun active
    BlinkEvent = 1 << 1,      // Just blinked (short teleport)
    TeleportEvent = 1 << 2,   // Just teleported (long distance)
    StateReset = 1 << 3,      // State reset (respawn)
    Dashing = 1 << 4,         // Currently dashing
    // Bits 5-7 reserved
}
```

**Usage:** Client checks event flags to trigger special interpolation behavior (e.g., snap hard on blink).

```csharp
if (state.HasBlinkEvent)
{
    _corrector.SnapHard(serverTime);  // Don't interpolate teleport
}
```

### Accessors (Helper Properties)

```csharp
// Dequantized values
Vector3 position = state.Position;
float rotation = state.Rotation;
Vector3 velocity = state.Velocity;
float speed = state.Speed;

// Flags
bool isAlive = state.IsAlive;
bool justSpawned = state.JustSpawned;
bool hasBlinkEvent = state.HasBlinkEvent;
bool isImmobilized = state.HasImmobilizeCC;
```

---

## GameCommand (Client → Server)

### Purpose

Discrete event commands (jump, spell cast, attack). Sent within `InputPacket.Commands[]` array for redundancy.

**File:** `Assets/Scripts/Network/NetAdapter/Messages/GameCommand.cs`

### Structure

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Offset │ Description       │
├─────────────────────────────────────────────────────────────┤
│ Sequence       │ uint   │ 4    │ 0      │ Command seq       │
│ Category       │ byte   │ 1    │ 4      │ Command category  │
│ Action         │ byte   │ 1    │ 5      │ Action type       │
│ Data0          │ short  │ 2    │ 6      │ Generic field 0   │
│ Data1          │ short  │ 2    │ 8      │ Generic field 1   │
│ Data2          │ uint   │ 4    │ 10     │ Generic field 2   │
└─────────────────────────────────────────────────────────────┘

Total: 14 bytes (compact, blittable)
```

### Command Categories

```csharp
public enum CommandCategory : byte
{
    None = 0,
    Movement = 1,        // Movement actions
    Attack = 20,         // Auto-attack
    Ability = 21,        // Spells (Q/W/E/R)
    Item = 40,           // Item usage
    Shop = 41,           // Shop interaction
    Ping = 60,           // Map pings
    Emote = 61,          // Emotes
    System = 200,        // System commands
}
```

### Data Field Interpretation

| Category | Data0 | Data1 | Data2 | Purpose |
|----------|-------|-------|-------|---------|
| **Movement** | dirX (quant) | dirZ (quant) | - | Direction vector |
| **Attack** | targetX (quant) | targetZ (quant) | targetEntityId | Attack-move or target |
| **Ability** | targetX (quant) | targetZ (quant) | targetEntityId | Spell target |
| **Item** | slot | secondarySlot | targetEntityId | Item slot(s) |

**Same Quantization as InputPacket:**

- Direction: ×127 (short)
- Position: ×10 (short)

### Factory Methods

#### Movement Commands

```csharp
// Start moving in direction
var cmd = GameCommand.MoveStart(seq: 42, direction: new Vector2(1, 0));

// Change direction
var cmd = GameCommand.MoveChange(seq: 43, direction: new Vector2(0, 1));

// Stop moving
var cmd = GameCommand.MoveStop(seq: 44);

// Jump (discrete event — server computes velocity from SimConfig)
var cmd = GameCommand.Jump(seq: 45);
```

#### Ability Commands

```csharp
// Cast Q at position
var cmd = GameCommand.CastAbility(
    seq: 50,
    abilitySlot: AbilityAction.CastQ,
    targetPos: new Vector2(50, 25),
    targetEntityId: 0
);

// Cast W on target entity
var cmd = GameCommand.CastAbility(
    seq: 51,
    abilitySlot: AbilityAction.CastW,
    targetPos: Vector2.zero,
    targetEntityId: 123
);

// Jump (discrete movement event, server-authoritative velocity)
var cmd = GameCommand.Jump(seq: 52);
```

#### Attack Commands

```csharp
// Auto-attack (nearest enemy)
var cmd = GameCommand.AttackStart(seq: 60);

// Attack specific target
var cmd = GameCommand.AttackTarget(seq: 61, targetEntityId: 123);

// Attack-move to position
var cmd = GameCommand.AttackMove(seq: 62, position: new Vector2(50, 25));

// Stop attacking
var cmd = GameCommand.AttackStop(seq: 63);
```

#### Item Commands

```csharp
// Use item in slot 1
var cmd = GameCommand.UseItem(seq: 70, slot: 1, targetEntityId: 0);

// Use item on target
var cmd = GameCommand.UseItem(seq: 71, slot: 2, targetEntityId: 123);

// Drop item
var cmd = GameCommand.DropItem(seq: 72, slot: 3);

// Swap items between slots
var cmd = GameCommand.SwapItems(seq: 73, fromSlot: 1, toSlot: 3);
```

---

## ReliableEvent (Server → Client)

### Purpose

Critical game state changes requiring guaranteed delivery: spawns, deaths, ability casts, respawns, AOI transitions.

**File:** `Assets/Scripts/Network/NetAdapter/Messages/ReliableEvent.cs`

### Structure

```
┌─────────────────────────────────────────────────────────────┐
│ Field          │ Type   │ Size │ Offset │ Description       │
├─────────────────────────────────────────────────────────────┤
│ ServerTick     │ uint   │ 4    │ 0      │ Tick when occurred│
│ Type           │ byte   │ 1    │ 4      │ EventType enum    │
│ EntityId       │ uint   │ 4    │ 5      │ Primary entity    │
│ Data1          │ uint   │ 4    │ 9      │ Context-dependent │
│ Data2          │ uint   │ 4    │ 13     │ Context-dependent │
└─────────────────────────────────────────────────────────────┘

Total: 17 bytes (fixed size)
```

### Event Types

| Type | Value | Data1 | Data2 | Purpose |
|------|-------|-------|-------|---------|
| `EntitySpawn` | 10 | Encoded position (X<<16\|Z) | ClientId<<8 \| TeamId | Player spawned |
| `EntityDeath` | 11 | KillerId | — | Entity died |
| `EntityRespawn` | 12 | SpawnX (×100) | SpawnZ (×100) | Entity respawned |
| `ClassAssign` | 26 | ClassId (byte) | — | Class selected |
| `AbilityUsed` | 40 | Slot \| DirX<<16 | DirZ | Ability cast |
| `DamageDealt` | 43 | AttackerId | Damage amount | Damage applied |
| `EntityEnterAOI` | 14 | OwnerClientId | TeamId | Entered visibility |
| `EntityLeaveAOI` | 15 | — | — | Left visibility |
| `Ping` | 101 | Echo sequence | — | RTT measurement |

### Data Encoding Examples

**EntitySpawn:** Position encoded as 16-bit signed (0.01 unit precision)
```csharp
// Encode: X in high 16 bits, Z in low 16 bits of Data1
short posX = (short)(spawnPos.x * 100f);
short posZ = (short)(spawnPos.z * 100f);
uint encodedPos = (uint)(((posX & 0xFFFF) << 16) | (posZ & 0xFFFF));

// Data2: ClientId (24 bits) + TeamId (8 bits)
uint data2 = ((uint)ownerClientId << 8) | teamId;
```

**AbilityUsed:** Slot in low byte, direction quantized (×127)
```csharp
// Encode
uint data1 = (uint)((slot & 0xFF) | (((ushort)(dir.x * 127f)) << 16));
uint data2 = (uint)(ushort)(dir.z * 127f);

// Decode
byte slot = (byte)(evt.Data1 & 0xFF);
short dirX = (short)(evt.Data1 >> 16);
short dirZ = (short)(evt.Data2 & 0xFFFF);
```

---

## Quantization Summary Table

| Data Type | Range | Precision | Encoding | Storage |
|-----------|-------|-----------|----------|---------|
| **Direction** | -1.0 to +1.0 | 1/127 (0.0079) | ×127 | short (2 bytes) |
| **Position (XZ)** | ±3276.7 units | 0.1 units | ×10 | short (2 bytes) |
| **Position (Map)** | 200×200 units | 0.003 units | 16-bit normalized | ushort (2 bytes) |
| **Height (Y)** | 0-20 units | 0.0003 units | 16-bit normalized | ushort (2 bytes) |
| **Rotation** | 0-360° | 0.0055° | 16-bit normalized | ushort (2 bytes) |
| **Velocity** | ±327.67 u/s | 0.01 u/s | ×100 | short (2 bytes) |
| **Speed** | 0-6553.5 u/s | 0.1 u/s | ×10 | ushort (2 bytes) |
| **Cooldown** | 0-60 seconds | ~0.24 seconds | ÷60 ×255 | byte (1 byte) |

**Design Notes:**

- **Direction uses ×127** (not 128) to leave room for -127 to +127 (symmetric range).
- **Position uses ×10** for 0.1 unit precision (sufficient for MOBA gameplay).
- **Map position uses 16-bit** for extreme precision (0.003 units) in snapshots.
- **Velocity precision (0.01)** exceeds display needs but ensures accurate physics sync.

---

## Bandwidth Calculations

### InputPacket Bandwidth (Continuous Input)

**Base packet (movement only):** 14 bytes
**With 3 redundant event commands:** 14 + (3 × 14) = 56 bytes
**Send rate:** 120 packets/sec (max, continuous input)

```
Typical (movement only): 14 × 120 = 1,680 bytes/sec ≈ 1.7 KB/s per client
Worst case (with events):  56 × 120 = 6,720 bytes/sec ≈ 6.7 KB/s per client
```

### SnapshotDelta Bandwidth (10 Entities)

**Header:** 14 bytes
**Entities:** 10 × 30 = 300 bytes
**Total:** 314 bytes
**Send rate:** 60 snapshots/sec
**Download:** 314 × 60 = 18,840 bytes/sec ≈ **18.8 KB/s per client**

### Server Total (10 Clients)

**Incoming:** 1.7–6.7 KB/s × 10 = **17–67 KB/s**
**Outgoing:** 18.8 KB/s × 10 = **188 KB/s** (1.5 Mbps)

**Negligible for modern connections.** Typical home broadband (10 Mbps up / 100 Mbps down) can handle 100+ clients.

---

## Version History

### InputPacket

| Version | Date | Changes |
|---------|------|---------|
| v1 | 2025-12 | Initial implementation (movement only) |
| v2 | 2026-01 | Added event commands array |
| v3 | 2026-01 | Buggy quantization (do not use) |
| v4 | 2026-02 | Intent-based with type-specific payloads (current) |

### SnapshotDelta

| Version | Date | Changes |
|---------|------|---------|
| v1 | 2025-12 | Initial implementation (current) |

### GameCommand

| Version | Date | Changes |
|---------|------|---------|
| v1 | 2025-12 | Initial implementation (current) |

---

## Validation Rules

### Server-Side Input Validation

All inputs received from clients must be validated to prevent cheating and bugs.

#### Direction Validation (MoveDir)

**File:** `ServerGameLoop.cs:748-773`

```csharp
Vector3 dir3D = new Vector3(input.Payload.x, 0f, input.Payload.y);

// Normalize if magnitude exceeds 1.0 (tolerance for quantization error)
if (dir3D.sqrMagnitude > 1.01f)
    dir3D = dir3D.normalized;

// Reject zero direction (client should send Stop instead)
if (dir3D.sqrMagnitude < 0.01f)
{
    Debug.LogWarning($"MoveDir with zero direction from client {clientId} - ignoring");
    return false;
}

player.SetMoveDirection(dir3D);
```

**Why tolerance?** Quantization can produce magnitude slightly > 1.0 (e.g., 1.0079).
**Why reject zero?** Prevents ghost movement — client should use `Stop` intent instead.

#### Position Validation (MoveTo)

> **Note:** Position clamping is NOT currently implemented. `ServerGameLoop.ApplyMoveTo` passes the position directly to `player.SetMoveTarget()` without bounds checking. This is a TODO for anti-cheat hardening.

```csharp
// Current implementation (no validation):
Vector3 target = new Vector3(input.Payload.x, 0f, input.Payload.y);
player.SetMoveTarget(target);
```

#### Sequence Validation

```csharp
// Reject out-of-order movement intents
if (movementSeq <= _lastMovementSeq[clientId])
{
    Debug.LogWarning($"Out-of-order input seq={movementSeq} (last={_lastMovementSeq[clientId]})");
    return;
}

_lastMovementSeq[clientId] = movementSeq;
```

**Why reject?** Late-arriving packets from UDP reordering should not override newer state.

---

## Testing & Debugging

### Hex Dump Example

**InputPacket (MoveDir right):**

```
Offset  Hex                              ASCII          Field
------  -------------------------------  -------------  ----------------
0x00    D2 04 00 00                      ....           ClientTick = 1234
0x04    2A 00 00 00                      *...           MovementSeq = 42
0x08    01                               .              IntentType = 1 (MoveDir)
0x09    7F 00                            ..             Payload0 = 127 (dirX=1.0)
0x0B    00 00                            ..             Payload1 = 0 (dirZ=0.0)
0x0D    00                               .              CommandCount = 0
```

**Total: 14 bytes**

### Logging Recommendations

**Client-side:**

```csharp
Debug.Log($"[SEND] InputPacket seq={packet.MovementSeq} intent={packet.IntentType} " +
          $"payload=({packet.Payload0},{packet.Payload1}) cmds={packet.CommandCount}");
```

**Server-side:**

```csharp
Debug.Log($"[RECV] Client={clientId} seq={packet.MovementSeq} intent={packet.IntentType} " +
          $"dir={dir} timestamp={recvSocketTicks}");
```

**Match logs by sequence number** to trace packet journey.

---

## References

### Related Documentation

- [Network Architecture](/Docs/Network/01_Network_Architecture.md) - Adapter pattern, transport
- [Data Flow](/Docs/Network/03_Data_Flow.md) - Input-to-visual pipeline
- [Performance Metrics](/Docs/Network/04_Performance_Metrics.md) - Latency analysis

### External Resources

- [Overwatch Netcode](https://www.youtube.com/watch?v=W3aieHjyNvw) - Snapshot interpolation, lag compensation
- [Quake 3 Networking](https://fabiensanglard.net/quake3/network.php) - Delta compression techniques
- [Valorant 128-tick](https://technology.riotgames.com/news/valorants-128-tick-servers) - High-rate server architecture

### Key Files

```
Assets/Scripts/Network/NetAdapter/Messages/
├── InputPacket.cs              # Client input format + quantization
├── SnapshotDelta.cs            # Server snapshot format + EntityState
├── GameCommand.cs              # Discrete event format
├── ReliableEvent.cs            # Critical server→client events
└── MatchConfigBroadcast.cs     # Match initialization broadcast

Assets/Scripts/Network/Shared/
└── CommandTypes.cs             # Shared enums (categories, actions)
```

---

#rules-verified

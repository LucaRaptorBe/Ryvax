# Entity Model

**Component-Based State Architecture - League of Legends Inspired, Replication-Optimized**

The Entity Model defines how game entities (players, minions, towers) store and replicate their state. Inspired by League of Legends' architecture, entities use **component-based state** with **separate replication frequencies** for bandwidth optimization.

---

## Design Philosophy

### 1. Component-Based State

Instead of monolithic entity classes, state is split into focused components:

```csharp
// SimPlayer.cs:19-46
public class SimPlayer : SimEntity
{
    public TransformState Transform;  // Position, rotation, velocity
    public StatsState Stats;          // HP, mana, level, resistances
    public AbilityState Abilities;    // Cooldowns, casts, levels
    public CombatState Combat;        // Attack, target, damage
}
```

**Benefits:**
- **Frequency separation:** High-frequency (Transform) vs low-frequency (Stats)
- **Delta compression:** Only send changed components
- **Modularity:** Add/remove components without affecting others
- **Testability:** Components are pure data (easy to test)

### 2. Replication Strategy

Components replicate at **different frequencies** based on change rate:

| Component | Frequency | Bandwidth | Reason |
|-----------|-----------|-----------|--------|
| **TransformState** | 60 Hz (every tick) | High | Position changes constantly |
| **StatsState** | ~1-10 Hz (delta) | Low | HP changes occasionally |
| **AbilityState** | ~1-5 Hz (delta) | Low | Cooldowns change on cast |
| **CombatState** | ~1-5 Hz (delta) | Low | Target changes occasionally |

**Example:**
```
Tick 100: Player casts ability
    → Snapshot includes: Transform + AbilityState (cooldown started)

Tick 101-199: Player moves (no ability changes)
    → Snapshot includes: Transform only (saves bandwidth)

Tick 200: Cooldown expires
    → Snapshot includes: Transform + AbilityState (cooldown ready)
```

**Bandwidth savings: ~67%** (see [GameSim_Overview.md](GameSim_Overview.md) for calculation)

### 3. Pure Data Structures

Components are **pure data** (no logic):

```csharp
// StatsState.cs:11 - Pure data, no methods
public struct StatsState
{
    public int Health;
    public int MaxHealth;
    public byte Level;
    // ... only data fields, no logic
}
```

**Why pure data?**
- **Serialization:** Easy to serialize/deserialize for network
- **Testability:** No hidden state or side effects
- **Determinism:** No logic means no non-deterministic behavior
- **Performance:** Structs are stack-allocated (cache-friendly)

**Logic lives in SimPlayer methods:**
```csharp
// SimPlayer.cs:375-384
public void TakeDamage(int damage, uint attackerId)
{
    Stats.Health -= damage;  // Modify component
    if (Stats.Health <= 0)
    {
        Stats.Health = 0;
        Die(attackerId);
    }
}
```

---

## Entity Hierarchy

### SimEntity - Base Class

**Location:** `Assets/GameSim/Entities/SimEntity.cs`

All entities inherit from SimEntity (players, minions, towers, projectiles):

```csharp
// SimEntity.cs (conceptual - verify actual implementation)
public abstract class SimEntity
{
    public uint Id { get; }           // Unique entity ID
    public EntityType Type { get; }   // Player/Minion/Tower/Projectile
    public int TeamId { get; set; }   // Team (0=Blue, 1=Red)

    public abstract void Tick(float dt, SimConfig config);
}
```

### SimPlayer - Player Entity

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:19`

SimPlayer extends SimEntity with player-specific components.

---

## TransformState - Movement & Position

**Location:** `Assets/GameSim/States/TransformState.cs:13`

TransformState stores position, rotation, and movement physics.

### Structure

```csharp
// TransformState.cs:13-69
public struct TransformState
{
    // === Position & Rotation ===
    public Vector3 Position;    // World position (3D)
    public float RotationY;     // Y rotation in degrees (0-360)

    // === Velocity ===
    public Vector3 Velocity;    // Velocity in 3D space (includes horizontal XZ and vertical Y)

    // === Ground State ===
    public bool IsGrounded;     // Is entity on the ground?
    public bool IsAirborne => !IsGrounded;  // Is entity airborne (jumping, falling, launched)?

    // === Movement State ===
    public Vector3 MoveDirection;  // Current movement direction (normalized, XZ plane)
    public bool IsMoving;          // Is entity currently moving?
}
```

### Replication

**Frequency:** 60 Hz (every tick)

**Quantization (network):**
```
Position:  Vector3 → 3 × short (0.1 unit precision) = 6 bytes
Velocity:  Vector3 → 3 × short (0.1 unit precision) = 6 bytes
RotationY: float → ushort (1° precision) = 2 bytes
Flags:     IsGrounded, IsMoving → 1 byte
Total: 15 bytes (vs 40 bytes uncompressed)
```

### Physics Integration

```csharp
// SimPlayer.cs:171-203
private void TickMovement(float dt, SimConfig config)
{
    // Apply horizontal movement velocity (preserve vertical component)
    var vel = Transform.Velocity;

    if (Transform.IsMoving && Transform.MoveDirection.sqrMagnitude > 0.01f)
    {
        float moveSpeed = config.PlayerMoveSpeed * Stats.MoveSpeedModifier;
        Vector3 horizontalVel = Transform.MoveDirection * moveSpeed;
        vel.x = horizontalVel.x;
        vel.z = horizontalVel.z;
        // vel.y is preserved (gravity/jump)
    }
    else
    {
        vel.x = 0f;
        vel.z = 0f;
        // vel.y is preserved (gravity/jump)
    }

    Transform.Velocity = vel;

    // Apply gravity & physics...
    ApplyGravity(dt, config);

    // Update rotation (smooth turn)
    if (Transform.MoveDirection.sqrMagnitude > 0.01f)
    {
        float targetRotation = Mathf.Atan2(Transform.MoveDirection.x, Transform.MoveDirection.z) * Mathf.Rad2Deg;
        float maxDelta = config.PlayerRotationSpeed * dt;
        Transform.RotationY = Mathf.MoveTowardsAngle(Transform.RotationY, targetRotation, maxDelta);
    }
}
```

**Key design:**
- Horizontal (XZ) and vertical (Y) velocity are **independent**
- Gravity affects Y, movement affects XZ
- Rotation smoothly turns towards movement direction

### Gravity System

```csharp
// SimPlayer.cs:205-257
private void ApplyGravity(float dt, SimConfig config)
{
    if (Transform.IsGrounded)
    {
        // Grounded movement - apply pull down force
        var vel = Transform.Velocity;
        vel.y = config.GroundedPullDown;  // e.g., -1.0 (keeps grounded)
        Transform.Velocity = vel;

        Vector3 movement = Transform.Velocity * dt;
        Transform.Position += movement;

        // Check if still grounded
        if (Transform.Position.y <= 0f)
        {
            Transform.Position = new Vector3(Transform.Position.x, 0f, Transform.Position.z);
            Transform.IsGrounded = true;
        }
        else
        {
            Transform.IsGrounded = false;
        }
    }
    else
    {
        // Falling - apply gravity
        var vel = Transform.Velocity;
        vel.y += config.Gravity * dt;  // e.g., -20.0 (acceleration)
        Transform.Velocity = vel;

        Vector3 movement = Transform.Velocity * dt;
        Transform.Position += movement;

        // Check landing
        if (Transform.Position.y <= 0f)
        {
            Transform.Position = new Vector3(Transform.Position.x, 0f, Transform.Position.z);
            vel = Transform.Velocity;
            vel.y = 0f;
            Transform.Velocity = vel;
            Transform.IsGrounded = true;
        }
    }

    // Clamp to arena bounds
    float halfWidth = config.ArenaWidth / 2f;
    float halfHeight = config.ArenaHeight / 2f;
    Transform.Position = new Vector3(
        Mathf.Clamp(Transform.Position.x, -halfWidth, halfWidth),
        Mathf.Max(Transform.Position.y, 0f),
        Mathf.Clamp(Transform.Position.z, -halfHeight, halfHeight)
    );
}
```

**Why separate grounded/airborne?**
- **Grounded:** Apply pull-down force (prevents floating)
- **Airborne:** Apply gravity acceleration (realistic falling)
- Enables jump/launch abilities (set IsGrounded=false, vel.y=jumpSpeed)

---

## StatsState - Health & Attributes

**Location:** `Assets/GameSim/States/StatsState.cs:11`

StatsState stores health, level, experience, and modifiers.

### Structure

```csharp
// StatsState.cs:11-68
public struct StatsState
{
    // === Health ===
    public int Health;        // Current health
    public int MaxHealth;     // Maximum health

    // === Level & Experience ===
    public byte Level;        // Current level (1-18 in LoL)
    public int Experience;    // Current experience points

    // === Modifiers ===
    public float MoveSpeedModifier;  // Move speed modifier (1.0 = normal, 1.5 = +50%, 0.5 = -50%)

    // === Helpers ===
    public bool IsAlive => Health > 0;
    public bool IsDead => Health <= 0;
    public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
}
```

### Replication

**Frequency:** Delta only (when changed)

**Quantization (network):**
```
Health:     int → ushort (0-65535 range) = 2 bytes
MaxHealth:  int → ushort = 2 bytes
Level:      byte = 1 byte
Experience: int → ushort (0-65535) = 2 bytes
MoveSpeedModifier: float → byte (0.01 precision, 0-2.55 range) = 1 byte
Total: 8 bytes (vs 13 bytes uncompressed)
```

**Delta detection:**
```csharp
// Server snapshot generation (conceptual)
if (player.Stats.Health != _lastSnapshot.Health ||
    player.Stats.MaxHealth != _lastSnapshot.MaxHealth ||
    player.Stats.Level != _lastSnapshot.Level)
{
    snapshot.IncludeStatsState(player.Stats);
}
```

### Usage in Gameplay

```csharp
// SimPlayer.cs:104-125 - Initialization
private void InitializeStates()
{
    Stats = new StatsState
    {
        Health = 100,
        MaxHealth = 100,
        Level = 1,
        Experience = 0,
        MoveSpeedModifier = 1.0f  // Normal speed
    };
}

// SimPlayer.cs:375-393 - Damage & Healing
public void TakeDamage(int damage, uint attackerId)
{
    Stats.Health -= damage;
    if (Stats.Health <= 0)
    {
        Stats.Health = 0;
        Die(attackerId);
    }
}

public void Heal(int amount)
{
    Stats.Health = Mathf.Min(Stats.Health + amount, Stats.MaxHealth);
}
```

---

## AbilityState - Cooldowns & Casts

**Location:** `Assets/GameSim/States/AbilityState.cs:13`

AbilityState stores ability cooldowns, levels, charges, and casting state.

### Structure

```csharp
// AbilityState.cs:13-110
public struct AbilityState
{
    public const int SLOT_COUNT = 6;  // Q, W, E, R, Summoner1, Summoner2

    // === Cooldowns ===
    public float[] Cooldowns;  // Cooldowns for each ability slot (seconds remaining)
                               // Index: 0=Q, 1=W, 2=E, 3=R, 4=Summoner1, 5=Summoner2

    // === Levels ===
    public byte[] Levels;  // Ability levels (0 = not learned, 1-5 = levels)
                           // In LoL: Q/W/E max at level 5, R max at level 3

    // === Charges (ex: Corki W, Teemo R) ===
    public byte[] Charges;             // Current charges for each ability (most have 1, some have 2-3)
    public float[] ChargeRecoveryTime; // Time until next charge recovery (seconds)

    // === Current Cast ===
    public CastState CurrentCast;  // Current cast state (if casting/channeling)

    // === Helpers ===
    public bool CanCast(int slot);     // Can cast ability in slot?
    public bool IsLearned(int slot);   // Is ability learned?
}
```

### CastState - Active Casting

```csharp
// AbilityState.cs:115-183
public struct CastState
{
    // === Cast State ===
    public bool IsCasting;       // Is currently casting/channeling?
    public byte CastingSlot;     // Which ability slot is being cast (0-5)

    // === Cast Timing ===
    public float CastTime;       // Total cast time (seconds) - Ex: Lux R = 1.0s
    public float CastProgress;   // Time elapsed since cast started (seconds)

    // === Cast Type ===
    public bool IsChanneling;    // Is this a channel (interruptible, ex: Katarina R)?
                                 // If false, it's a cast (non-interruptible after windup)

    // === Cast Target ===
    public Vector3 TargetPosition;  // Target position (for skillshots, ground-targeted)
    public uint TargetEntityId;     // Target entity ID (for targeted abilities, 0 = none)

    // === Helpers ===
    public float Progress => CastTime > 0 ? CastProgress / CastTime : 1f;  // 0.0 - 1.0
    public bool IsComplete => CastProgress >= CastTime;
}
```

### Replication

**Frequency:** Delta only (when changed)

**Typical changes:**
- Ability cast: Cooldowns, CurrentCast
- Cooldown tick: Cooldowns (but can batch updates every 0.5s)
- Level up: Levels

**Quantization (network):**
```
Cooldowns: 6 × float → 6 × ushort (0.1s precision) = 12 bytes
Levels:    6 × byte = 6 bytes
Charges:   6 × byte = 6 bytes
CurrentCast: ~10 bytes (flags + timing)
Total: ~34 bytes (only sent when changed)
```

### Cooldown Management

```csharp
// SimPlayer.cs:259-289 - Tick Abilities
private void TickAbilities(float dt)
{
    // Decrease cooldowns
    for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
    {
        if (Abilities.Cooldowns[i] > 0)
        {
            Abilities.Cooldowns[i] -= dt;
            if (Abilities.Cooldowns[i] < 0)
                Abilities.Cooldowns[i] = 0;
        }
    }

    // Update charge recovery
    for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
    {
        if (Abilities.ChargeRecoveryTime[i] > 0)
        {
            Abilities.ChargeRecoveryTime[i] -= dt;
            if (Abilities.ChargeRecoveryTime[i] <= 0)
            {
                // Restore one charge
                byte maxCharges = GetMaxCharges(i);
                if (Abilities.Charges[i] < maxCharges)
                {
                    Abilities.Charges[i]++;
                }
            }
        }
    }

    // Update current cast
    if (Abilities.CurrentCast.IsCasting)
    {
        Abilities.CurrentCast.CastProgress += dt;

        if (Abilities.CurrentCast.IsComplete)
        {
            // Cast finished
            Abilities.CurrentCast.IsCasting = false;
            // TODO: Execute ability effect
        }
    }
}
```

### Initialization

```csharp
// AbilityState.cs:69-82
public void Initialize()
{
    Cooldowns = new float[SLOT_COUNT];
    Levels = new byte[SLOT_COUNT];
    Charges = new byte[SLOT_COUNT];
    ChargeRecoveryTime = new float[SLOT_COUNT];
    CurrentCast = new CastState();

    // Default: 1 charge per ability
    for (int i = 0; i < SLOT_COUNT; i++)
    {
        Charges[i] = 1;
    }
}

// SimPlayer.cs:127-129
Abilities = new AbilityState();
Abilities.Initialize();
```

---

## CombatState - Auto-Attack & Targeting

**Location:** `Assets/GameSim/States/CombatState.cs:13`

CombatState stores auto-attack state, targeting, and attack stats.

### Structure

```csharp
// CombatState.cs:13-97
public struct CombatState
{
    // === Target ===
    public uint AttackTargetId;     // Current auto-attack target entity ID (0 = no target)
    public bool IsAutoAttacking;    // Is currently auto-attacking?

    // === Attack Stats ===
    public float AttackCooldown;    // Cooldown before next auto-attack can be issued (seconds)
    public float AttackSpeed;       // Attacks per second (ex: 0.658 in LoL)
    public float AttackRange;       // Auto-attack range (units) - Ex: 125 = melee, 550 = ADC
    public float BaseDamage;        // Base attack damage (before items/buffs)

    // === Attack-Move ===
    public bool IsAttackMoving;     // Is doing attack-move (attack enemies while moving)?
    public Vector3 AttackMoveTarget; // Attack-move target position

    // === Timing ===
    public float TimeSinceLastAttack; // Time since last auto-attack (seconds) - for animation timing

    // === Helpers ===
    public bool CanAttack => AttackCooldown <= 0f;
    public float AttackPeriod => AttackSpeed > 0 ? 1f / AttackSpeed : 1f;  // Seconds per attack
    public bool HasTarget => AttackTargetId != 0;
}
```

### Replication

**Frequency:** Delta only (when changed)

**Typical changes:**
- Attack target changed
- Attack started/stopped
- Attack cooldown (can batch updates)

**Quantization (network):**
```
AttackTargetId: uint = 4 bytes
Flags: IsAutoAttacking, IsAttackMoving = 1 byte
AttackCooldown: float → byte (0.01s precision) = 1 byte
Total: 6 bytes (only sent when changed)
```

### Combat Loop

```csharp
// SimPlayer.cs:304-315 - Tick Combat
private void TickCombat(float dt, SimConfig config)
{
    // Decrease attack cooldown
    if (Combat.AttackCooldown > 0)
    {
        Combat.AttackCooldown -= dt;
    }

    Combat.TimeSinceLastAttack += dt;

    // TODO: Auto-attack logic
}
```

### Initialization

```csharp
// SimPlayer.cs:131-143
Combat = new CombatState
{
    AttackTargetId = 0,
    IsAutoAttacking = false,
    AttackCooldown = 0f,
    AttackSpeed = 0.658f,  // LoL default
    AttackRange = 125f,    // Melee range
    BaseDamage = 50f,
    IsAttackMoving = false,
    TimeSinceLastAttack = 0f
};
```

---

## Network Reconciliation

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:63-79`

SimPlayer tracks network reconciliation state for lag compensation.

### Structure

```csharp
// SimPlayer.cs:63-79
public uint LastCommandSeq { get; set; }        // Last processed command sequence (for reconciliation)
public float TimeSinceLastMoveCmd { get; set; } // Time since last movement command (for watchdog)
public float LastMoveStopTime { get; set; }     // Time of last MoveStop command (for debug logging)
```

### Usage

**Server watchdog:**
```csharp
// Server stops player if no commands for 1 second (anti-lag)
TimeSinceLastMoveCmd += dt;
if (TimeSinceLastMoveCmd > 1.0f && Transform.IsMoving)
{
    StopMoving();  // Auto-stop on timeout
}
```

**Client reconciliation:**
```csharp
// Client receives snapshot
SnapshotDelta {
    Entities: [
        { Id: 1, LastCommandSeq: 123, Position: (10, 0, 0) }
    ]
}

// Client: "Server processed up to command 123"
// → Rewind to that command, replay commands 124-130
```

**See [../Architecture/02_LoL_Style_Netcode.md](../Architecture/02_LoL_Style_Netcode.md) for reconciliation details.**

---

## Snapshot Delta Encoding

### High-Frequency State (Every Tick)

**TransformState:**
```csharp
// Always included in snapshot (60 Hz)
snapshot.AddEntity(new SnapshotEntity
{
    Id = player.Id,
    Position = player.Transform.Position,
    Velocity = player.Transform.Velocity,
    RotationY = player.Transform.RotationY,
    // ... other Transform fields
});
```

### Low-Frequency State (Delta Only)

**StatsState, AbilityState, CombatState:**
```csharp
// Only included if changed since last snapshot
if (HasChanged(player.Stats, _lastSnapshot.Stats))
{
    snapshot.AddStatsState(player.Id, player.Stats);
}

if (HasChanged(player.Abilities, _lastSnapshot.Abilities))
{
    snapshot.AddAbilityState(player.Id, player.Abilities);
}

if (HasChanged(player.Combat, _lastSnapshot.Combat))
{
    snapshot.AddCombatState(player.Id, player.Combat);
}
```

**Delta detection methods:**
```csharp
bool HasChanged(StatsState current, StatsState last)
{
    return current.Health != last.Health ||
           current.MaxHealth != last.MaxHealth ||
           current.Level != last.Level ||
           current.MoveSpeedModifier != last.MoveSpeedModifier;
}

bool HasChanged(AbilityState current, AbilityState last)
{
    // Compare arrays element-wise
    for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
    {
        if (current.Cooldowns[i] != last.Cooldowns[i]) return true;
        if (current.Levels[i] != last.Levels[i]) return true;
    }
    if (current.CurrentCast.IsCasting != last.CurrentCast.IsCasting) return true;
    return false;
}
```

---

## Component Lifecycle

### Initialization

```csharp
// SimPlayer.cs:104-143
private void InitializeStates()
{
    // Initialize TransformState
    Transform = new TransformState
    {
        Position = Vector3.zero,
        RotationY = 0f,
        Velocity = Vector3.zero,
        IsGrounded = true,
        IsMoving = false,
        MoveDirection = Vector3.zero
    };

    // Initialize StatsState
    Stats = new StatsState
    {
        Health = 100,
        MaxHealth = 100,
        Level = 1,
        Experience = 0,
        MoveSpeedModifier = 1.0f
    };

    // Initialize AbilityState
    Abilities = new AbilityState();
    Abilities.Initialize();

    // Initialize CombatState
    Combat = new CombatState
    {
        AttackTargetId = 0,
        IsAutoAttacking = false,
        AttackCooldown = 0f,
        AttackSpeed = 0.658f,
        AttackRange = 125f,
        BaseDamage = 50f,
        IsAttackMoving = false,
        TimeSinceLastAttack = 0f
    };
}
```

### Update

```csharp
// SimPlayer.cs:153-169
public override void Tick(float dt, SimConfig config)
{
    if (Stats.IsDead)
    {
        // TODO: Handle respawn
        return;
    }

    // Update movement
    TickMovement(dt, config);

    // Update abilities
    TickAbilities(dt);

    // Update combat
    TickCombat(dt, config);
}
```

### Serialization (Network)

```csharp
// Conceptual - actual implementation in SnapshotDelta.cs
void SerializeTransform(BitWriter writer, TransformState transform)
{
    writer.WriteVector3Quantized(transform.Position, 0.1f);  // 0.1 unit precision
    writer.WriteVector3Quantized(transform.Velocity, 0.1f);
    writer.WriteUShort((ushort)transform.RotationY);  // 1° precision
    writer.WriteBool(transform.IsGrounded);
    writer.WriteBool(transform.IsMoving);
}

void SerializeStats(BitWriter writer, StatsState stats)
{
    writer.WriteUShort((ushort)stats.Health);
    writer.WriteUShort((ushort)stats.MaxHealth);
    writer.WriteByte(stats.Level);
    writer.WriteByte((byte)(stats.MoveSpeedModifier * 100f));  // 0.01 precision
}
```

---

## Performance Characteristics

### Memory Usage

| Component | Size | Notes |
|-----------|------|-------|
| TransformState | 40 bytes | 3 × Vector3 + float + 2 × bool |
| StatsState | 13 bytes | 2 × int + byte + int + float |
| AbilityState | ~100 bytes | 6 × arrays + CastState |
| CombatState | 32 bytes | uint + 5 × float + bool + Vector3 |
| **Total per player** | ~185 bytes | |

**100 players: ~18.5 KB** (minimal overhead)

### Network Bandwidth

**High-frequency (60 Hz):**
```
TransformState: 15 bytes/tick × 60 Hz = 900 bytes/sec per player
100 players: 90 KB/sec
```

**Low-frequency (delta, ~1 Hz average):**
```
StatsState: 8 bytes × 1 Hz = 8 bytes/sec
AbilityState: 34 bytes × 2 Hz = 68 bytes/sec (cooldowns change)
CombatState: 6 bytes × 1 Hz = 6 bytes/sec
Total: ~82 bytes/sec per player
100 players: 8.2 KB/sec
```

**Total bandwidth: ~98 KB/sec** (100 players, server → client)

Compare to full replication:
- 185 bytes × 60 Hz × 100 players = 1.11 MB/sec
- **Savings: 91%**

---

## Extending the Entity Model

### Adding a New Component

**Example: Add "BuffState" component**

#### 1. Define component struct

```csharp
// GameSim/States/BuffState.cs
public struct BuffState
{
    public const int MAX_BUFFS = 32;

    public struct Buff
    {
        public uint BuffId;        // Buff type ID
        public float Duration;     // Remaining duration (seconds)
        public byte Stacks;        // Number of stacks
        public uint CasterId;      // Who applied this buff
    }

    public Buff[] ActiveBuffs;  // Active buffs (fixed array)
    public byte BuffCount;      // Number of active buffs

    public void Initialize()
    {
        ActiveBuffs = new Buff[MAX_BUFFS];
        BuffCount = 0;
    }

    public void AddBuff(uint buffId, float duration, uint casterId)
    {
        if (BuffCount >= MAX_BUFFS) return;
        ActiveBuffs[BuffCount++] = new Buff
        {
            BuffId = buffId,
            Duration = duration,
            Stacks = 1,
            CasterId = casterId
        };
    }
}
```

#### 2. Add to SimPlayer

```csharp
// SimPlayer.cs
public class SimPlayer : SimEntity
{
    public TransformState Transform;
    public StatsState Stats;
    public AbilityState Abilities;
    public CombatState Combat;
    public BuffState Buffs;  // Add new component
}
```

#### 3. Initialize in constructor

```csharp
// SimPlayer.cs - InitializeStates()
Buffs = new BuffState();
Buffs.Initialize();
```

#### 4. Update in Tick()

```csharp
// SimPlayer.cs - Tick()
TickBuffs(dt);

private void TickBuffs(float dt)
{
    for (int i = 0; i < Buffs.BuffCount; i++)
    {
        Buffs.ActiveBuffs[i].Duration -= dt;
        if (Buffs.ActiveBuffs[i].Duration <= 0)
        {
            RemoveBuff(i);
        }
    }
}
```

#### 5. Add to snapshot (delta only)

```csharp
// Server snapshot generation
if (HasChanged(player.Buffs, _lastSnapshot.Buffs))
{
    snapshot.AddBuffState(player.Id, player.Buffs);
}
```

**Done!** New component works with existing infrastructure.

---

## Related Documentation

- **[GameSim_Overview.md](GameSim_Overview.md)** - Overall GameSim architecture
- **[Command_System.md](Command_System.md)** - Command routing and handlers
- **[../Network/Snapshot_System.md](../Network/Snapshot_System.md)** - Snapshot encoding/decoding
- **[../Architecture/02_LoL_Style_Netcode.md](../Architecture/02_LoL_Style_Netcode.md)** - Broader netcode context

---

**Last Updated:** 2026-02-03

#rules-verified

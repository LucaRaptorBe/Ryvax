# Entity Model

**Component-Based State Architecture - League of Legends Inspired, Replication-Optimized**

The Entity Model defines how game entities (players, minions, towers) store and replicate their state. Entities use **component-based state** with **separate replication frequencies** for bandwidth optimization.

---

## Design Philosophy

### 1. Component-Based State

State is split into focused component structs:

```csharp
// SimPlayer.cs:19-46
public class SimPlayer : SimEntity
{
    public TransformState Transform;  // Position, rotation, velocity, ground state
    public StatsState Stats;          // HP, level, experience, speed modifier
    public AbilityState Abilities;    // Cooldowns, levels, charges, cast state
    public CombatState Combat;        // Attack target, speed, range, damage
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

### 3. Pure Data Structures

Components are **pure data** (no logic):

```csharp
// StatsState.cs:11 - Pure data, computed properties only
public struct StatsState
{
    public int Health;
    public int MaxHealth;
    // ... only data fields
    public bool IsAlive => Health > 0;  // Computed, not stored
}
```

**Logic lives in SimPlayer methods:**
```csharp
// SimPlayer.cs:288-296
public void TakeDamage(int damage, uint attackerId)
{
    Stats.Health -= damage;
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

**Location:** `Assets/GameSim/Entities/SimEntity.cs:45`

All entities inherit from SimEntity:

```csharp
// SimEntity.cs:45-169
public abstract class SimEntity
{
    // Static ID management (auto-increment from 1)
    private static uint _nextId = 1;
    public static void ResetIdCounter();

    public uint Id { get; }                          // Unique entity ID
    public EntityType Type { get; protected set; }   // Player/Projectile/Minion/Tower/etc.
    public EntityState State { get; set; }           // Idle/Moving/Casting/Dead/Dashing/etc.
    public byte TeamId { get; set; }                 // Team (0 = neutral)
    public uint SpawnTick { get; set; }              // Tick when spawned
    public bool IsMarkedForRemoval { get; set; }     // Deferred removal flag

    public abstract void Tick(float dt, SimConfig config);

    public bool IsHostileTo(SimEntity other);   // Different non-zero teams
    public bool IsFriendlyTo(SimEntity other);  // Same non-zero team
}
```

### EntityType & EntityState Enums

```csharp
// SimEntity.cs:13-38
public enum EntityType : byte
{
    None = 0, Player = 1, Projectile = 2, Minion = 3,
    Tower = 4, Objective = 5, Pickup = 6, Effect = 7,
}

public enum EntityState : byte
{
    Idle = 0, Moving = 1, Attacking = 2, Casting = 3,
    Stunned = 4, Dead = 5, Spawning = 6, Despawning = 7, Dashing = 8,
}
```

### SimPlayer - Player Entity

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:19`

SimPlayer extends SimEntity with player-specific component states.

---

## TransformState - Movement & Position

**Location:** `Assets/GameSim/States/TransformState.cs:13`

### Structure

```csharp
// TransformState.cs:13-77
public struct TransformState
{
    // === Position & Rotation ===
    public Vector3 Position;          // World position (3D)
    public float RotationY;           // Y rotation in degrees (0-360)

    // === Velocity ===
    public Vector3 Velocity;          // Impulse velocity only (knockback, dash, jump)
                                      // Decays via MovementEngine friction. Internal to physics.
    public Vector3 EffectiveVelocity; // Total velocity (impulse + input). Written by MovementEngine.Tick().
                                      // Used by snapshots, animation, and dead-reckoning.

    // === Ground State ===
    public bool IsGrounded;           // Is entity on the ground?
    public bool IsAirborne => !IsGrounded;  // Computed: is airborne?

    // === Movement State ===
    public Vector3 MoveDirection;     // Current movement direction (normalized, XZ plane)
    public bool IsMoving;             // Is entity currently moving?
}
```

**Velocity vs EffectiveVelocity:** `Velocity` stores only impulses (knockback, jump) and decays via friction. `EffectiveVelocity` = `Velocity` + input velocity, and is what snapshots send to clients for dead-reckoning. When grounded, `EffectiveVelocity.y` is zeroed to prevent client drift from `GroundedPullDown`.

### Replication

**Frequency:** 60 Hz (every tick). Snapshots use `EffectiveVelocity` (not `Velocity`).

### Physics Integration

`SimPlayer.TickMovement()` delegates entirely to `MovementEngine`:

```csharp
// SimPlayer.cs:172-176
private void TickMovement(float dt, SimConfig config)
{
    float moveSpeed = config.PlayerMoveSpeed * Stats.MoveSpeedModifier;
    MovementEngine.Tick(ref Transform, moveSpeed, config, dt, useTurnSlowdown: true);
}
```

All physics logic lives in `MovementEngine.Tick()` (`Assets/GameSim/Core/MovementEngine.cs:28`):

```csharp
public static void Tick(ref TransformState transform, float moveSpeed, SimConfig config, float dt, bool useTurnSlowdown = false)
{
    // 1. Calculate input velocity from MoveDirection (with optional turn slowdown)
    Vector3 inputVelocity = CalculateInputVelocity(ref transform, moveSpeed, config, useTurnSlowdown);

    // 2. Apply gravity (GroundedPullDown when grounded, freefall otherwise)
    ApplyGravity(ref transform, config, dt);

    // 3. Apply exponential friction to impulse velocity (horizontal only)
    ApplyFriction(ref transform, config, dt);

    // 4. Apply air control if airborne and moving
    if (transform.IsAirborne && transform.IsMoving)
        ApplyAirControl(ref transform, transform.MoveDirection, config, dt);

    // 5. Combine and move
    Vector3 totalVelocity = transform.Velocity + inputVelocity;
    transform.EffectiveVelocity = totalVelocity;
    transform.Position += totalVelocity * dt;

    // 6. Ground check + arena bounds
    ApplyGroundCheck(ref transform, config);
    ClampToArenaBounds(ref transform, config);

    // 7. Zero vertical EffectiveVelocity when grounded (prevents dead-reckoning drift)
    if (transform.IsGrounded)
        transform.EffectiveVelocity = new Vector3(transform.EffectiveVelocity.x, 0f, transform.EffectiveVelocity.z);

    // 8. Smooth rotation toward MoveDirection (720°/s)
    if (transform.MoveDirection.sqrMagnitude > 0.01f)
    {
        float targetRotation = Mathf.Atan2(transform.MoveDirection.x, transform.MoveDirection.z) * Mathf.Rad2Deg;
        transform.RotationY = Mathf.MoveTowardsAngle(transform.RotationY, targetRotation, config.PlayerRotationSpeed * dt);
    }
}
```

**Key design:**
- All physics in `MovementEngine`, shared by players/minions/monsters
- `Velocity` (impulse) and input velocity are **independent**, combined into `EffectiveVelocity`
- Gravity affects Y only, input velocity affects XZ only
- Rotation smoothly turns towards movement direction

### Turn Slowdown (MOBA-style)

Sharp direction changes reduce speed (`MovementEngine.cs:112-132`):
- Angle < `TurnSlowdownAngle` (90°) → 100% speed
- Angle = 180° → `TurnSlowdownMultiplier` (20%) speed
- Linear lerp between thresholds
- Only applied to players (`useTurnSlowdown: true`)

### Friction System

Exponential drag on impulse velocity (`MovementEngine.cs:173-190`):
- `v *= e^(-drag * dt)`
- Ground drag: 15 (impulses fade in ~0.2s)
- Air drag: 0.5 (momentum preserved longer)
- Velocities < 0.01 are zeroed to prevent drift

### Air Control

Limited directional influence while airborne (`MovementEngine.cs:283-294`):
- Strength: `AirControlStrength` (5 u/s²)
- Applied as impulse addition, not direct velocity
- Only when airborne AND moving

### Gravity System

```csharp
// MovementEngine.cs:141-163
private static void ApplyGravity(ref TransformState transform, SimConfig config, float dt)
{
    if (transform.IsGrounded && vel.y <= 0f)
        vel.y = config.GroundedPullDown;    // -2 m/s pull-down to maintain contact
    else
    {
        vel.y += config.Gravity * dt;       // -20 m/s² freefall
        clamp to config.TerminalVelocity;   // -50 m/s max
    }
}
```

---

## StatsState - Health & Attributes

**Location:** `Assets/GameSim/States/StatsState.cs:11`

### Structure

```csharp
// StatsState.cs:11-69
public struct StatsState
{
    // === Health ===
    public int Health;              // Current health
    public int MaxHealth;           // Maximum health

    // === Level & Experience ===
    public byte Level;              // Current level (1-18)
    public int Experience;          // Current experience points

    // === Modifiers ===
    public float MoveSpeedModifier; // Move speed modifier (1.0 = normal, 0.5 = -50%, 1.5 = +50%)

    // === Computed Properties ===
    public bool IsAlive => Health > 0;
    public bool IsDead => Health <= 0;
    public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
}
```

### Replication

**Frequency:** Delta only (when changed)

### Initialization & Usage

```csharp
// SimPlayer.cs:119-126
Stats = new StatsState
{
    Health = 100,
    MaxHealth = 100,
    Level = 1,
    Experience = 0,
    MoveSpeedModifier = 1.0f
};

// SimPlayer.cs:288-304
public void TakeDamage(int damage, uint attackerId)
{
    Stats.Health -= damage;
    if (Stats.Health <= 0) { Stats.Health = 0; Die(attackerId); }
}

public void Heal(int amount)
{
    Stats.Health = Mathf.Min(Stats.Health + amount, Stats.MaxHealth);
}
```

---

## AbilityState - Cooldowns & Casts

**Location:** `Assets/GameSim/States/AbilityState.cs:13`

### Structure

```csharp
// AbilityState.cs:13-111
public struct AbilityState
{
    public const int SLOT_COUNT = 6;  // Q, W, E, R, Summoner1, Summoner2

    // === Cooldowns ===
    public float[] Cooldowns;           // Seconds remaining per slot (0=Q, 1=W, 2=E, 3=R, 4=Sum1, 5=Sum2)

    // === Levels ===
    public byte[] Levels;              // 0 = not learned, 1-5 = levels (R max at 3)

    // === Charges (ex: Corki W, Teemo R) ===
    public byte[] Charges;             // Current charges per ability (most have 1, some 2-3)
    public float[] ChargeRecoveryTime; // Time until next charge recovery (seconds)

    // === Current Cast ===
    public CastState CurrentCast;      // Active cast/channel state

    // === Helpers ===
    public bool CanCast(int slot);     // Checks: learned, off cooldown, has charges, not already casting
    public bool IsLearned(int slot);
}
```

### CastState - Active Casting

```csharp
// AbilityState.cs:116-185
public struct CastState
{
    public bool IsCasting;          // Is currently casting/channeling?
    public byte CastingSlot;        // Which ability slot (0-5)

    public float CastTime;          // Total cast time (seconds)
    public float CastProgress;      // Time elapsed since cast started

    public bool IsChanneling;       // Channel (interruptible) vs cast (non-interruptible)

    public Vector3 TargetPosition;  // Target position (skillshots, ground-targeted)
    public uint TargetEntityId;     // Target entity (targeted abilities, 0 = none)

    // === Computed ===
    public float Progress => CastTime > 0 ? CastProgress / CastTime : 1f;
    public bool IsComplete => CastProgress >= CastTime;
}
```

### Initialization

```csharp
// AbilityState.cs:69-83
public void Initialize()
{
    Cooldowns = new float[SLOT_COUNT];
    Levels = new byte[SLOT_COUNT];
    Charges = new byte[SLOT_COUNT];
    ChargeRecoveryTime = new float[SLOT_COUNT];
    CurrentCast = new CastState();

    // Default: 1 charge per ability, all abilities learned at level 1
    for (int i = 0; i < SLOT_COUNT; i++)
    {
        Charges[i] = 1;
        Levels[i] = 1;
    }
}
```

### Cooldown & Charge Tick

```csharp
// SimPlayer.cs:178-221
private void TickAbilities(float dt)
{
    // 1. Decrease cooldowns
    for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
    {
        if (Abilities.Cooldowns[i] > 0)
        {
            Abilities.Cooldowns[i] -= dt;
            if (Abilities.Cooldowns[i] < 0) Abilities.Cooldowns[i] = 0;
        }
    }

    // 2. Update charge recovery (restore charge when timer expires)
    for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
    {
        if (Abilities.ChargeRecoveryTime[i] > 0)
        {
            Abilities.ChargeRecoveryTime[i] -= dt;
            if (Abilities.ChargeRecoveryTime[i] <= 0)
                if (Abilities.Charges[i] < maxCharges)
                    Abilities.Charges[i]++;
        }
    }

    // 3. Progress current cast
    if (Abilities.CurrentCast.IsCasting)
    {
        Abilities.CurrentCast.CastProgress += dt;
        if (Abilities.CurrentCast.IsComplete)
            Abilities.CurrentCast.IsCasting = false;
    }
}
```

---

## CombatState - Auto-Attack & Targeting

**Location:** `Assets/GameSim/States/CombatState.cs:13`

### Structure

```csharp
// CombatState.cs:13-98
public struct CombatState
{
    // === Target ===
    public uint AttackTargetId;      // Current target entity ID (0 = no target)
    public bool IsAutoAttacking;     // Is currently auto-attacking?

    // === Attack Stats ===
    public float AttackCooldown;     // Seconds before next auto-attack
    public float AttackSpeed;        // Attacks per second (default: 0.658)
    public float AttackRange;        // Auto-attack range (125 = melee, 550 = ADC)
    public float BaseDamage;         // Base attack damage (default: 50)

    // === Attack-Move ===
    public bool IsAttackMoving;      // Attack enemies while moving to position?
    public Vector3 AttackMoveTarget; // Attack-move target position

    // === Timing ===
    public float TimeSinceLastAttack; // For animation timing

    // === Computed ===
    public bool CanAttack => AttackCooldown <= 0f;
    public float AttackPeriod => AttackSpeed > 0 ? 1f / AttackSpeed : 1f;
    public bool HasTarget => AttackTargetId != 0;
}
```

### Initialization

```csharp
// SimPlayer.cs:133-143
Combat = new CombatState
{
    AttackTargetId = 0,
    IsAutoAttacking = false,
    AttackCooldown = 0f,
    AttackSpeed = 0.658f,    // LoL default
    AttackRange = 125f,      // Melee range
    BaseDamage = 50f,
    IsAttackMoving = false,
    TimeSinceLastAttack = 0f
};
```

### Combat Tick

```csharp
// SimPlayer.cs:223-234
private void TickCombat(float dt, SimConfig config)
{
    if (Combat.AttackCooldown > 0)
        Combat.AttackCooldown -= dt;

    Combat.TimeSinceLastAttack += dt;

    // TODO: Auto-attack logic
}
```

---

## Network Reconciliation

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:64-79`

SimPlayer tracks network reconciliation state for lag compensation.

```csharp
public uint LastCommandSeq { get; set; }        // Last processed command seq (sent in snapshots)
public float TimeSinceLastMoveCmd { get; set; } // Watchdog: auto-stop after 1s of no commands
public float LastMoveStopTime { get; set; }     // Debug logging on server
```

### Network State Application

```csharp
// SimPlayer.cs:335-360
public void ApplyNetworkState(Vector3 position, Vector3 velocity, float rotationY);
public void ApplyNetworkState(Vector3 position, Vector3 velocity, float rotationY,
    in MOBANet.NetAdapter.Messages.EntityState netState);
// Second overload also syncs: Stats.Health, Abilities.Cooldowns[0..3]
```

---

## Component Lifecycle

### Initialization

```csharp
// SimPlayer.cs:105-144
private void InitializeStates()
{
    Transform = new TransformState
    {
        Position = Vector3.zero,
        RotationY = 0f,
        Velocity = Vector3.zero,
        IsGrounded = true,
        IsMoving = false,
        MoveDirection = Vector3.zero
    };

    Stats = new StatsState
    {
        Health = 100, MaxHealth = 100,
        Level = 1, Experience = 0,
        MoveSpeedModifier = 1.0f
    };

    Abilities = new AbilityState();
    Abilities.Initialize();

    Combat = new CombatState
    {
        AttackTargetId = 0, IsAutoAttacking = false,
        AttackCooldown = 0f, AttackSpeed = 0.658f,
        AttackRange = 125f, BaseDamage = 50f,
        IsAttackMoving = false, TimeSinceLastAttack = 0f
    };
}
```

### Update (Per Tick)

```csharp
// SimPlayer.cs:154-170
public override void Tick(float dt, SimConfig config)
{
    if (Stats.IsDead) return;

    TickMovement(dt, config);
    TickAbilities(dt);
    TickCombat(dt, config);
}
```

---

## Performance Characteristics

### Memory Usage

| Component | Size | Notes |
|-----------|------|-------|
| TransformState | ~40 bytes | 3 x Vector3 + float + 2 x bool |
| StatsState | ~13 bytes | 2 x int + byte + int + float |
| AbilityState | ~100 bytes | 6 x arrays + CastState |
| CombatState | ~36 bytes | uint + 5 x float + bool + Vector3 + bool |
| **Total per player** | ~189 bytes | |

**100 players: ~18.9 KB** (minimal)

### Network Bandwidth

**High-frequency (60 Hz):**
```
TransformState: ~15 bytes/tick x 60 Hz = 900 bytes/sec per player
100 players: 90 KB/sec
```

**Low-frequency (delta, ~1 Hz average):**
```
StatsState + AbilityState + CombatState: ~82 bytes/sec per player
100 players: 8.2 KB/sec
```

**Total: ~98 KB/sec** (100 players, server -> client)

---

## Extending the Entity Model

### Adding a New Component

1. Define component struct in `GameSim/States/`
2. Add field to SimPlayer
3. Initialize in `InitializeStates()`
4. Update in `Tick()` (add `TickNewComponent(dt)`)
5. Add to snapshot delta (in SnapshotHelper)

---

## Related Documentation

- **[01_GameSim_Overview.md](01_GameSim_Overview.md)** - Overall GameSim architecture
- **[02_Command_System.md](02_Command_System.md)** - Command routing and handlers
- **[../Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)** - Broader architecture context

---

**Last Updated:** 2026-02-18

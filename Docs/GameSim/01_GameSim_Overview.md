# GameSim Overview

**Simulation Layer - Server-Authoritative, Component-Based (depends on UnityEngine for Vector3/Mathf)**

GameSim is the core simulation engine for Ryvax, inspired by League of Legends' architecture. It runs on both server (authoritative) and client (prediction), with a dependency on UnityEngine for math primitives (Vector3, Mathf, Quaternion) and ScriptableObject (data assets like AbilityDefinition, CharacterClass).

---

## Design Philosophy

### 1. Simulation Logic (UnityEngine for math + data)

GameSim uses UnityEngine for:
- `Vector3`, `Vector2` (math primitives)
- `Mathf` (math functions)
- `Quaternion` (rotation math)
- `ScriptableObject` (data assets: `AbilityDefinition`, `CharacterClass`)

**No Unity runtime dependencies:**
- No `MonoBehaviour`
- No `GameObject`
- No `Transform`
- No `Rigidbody`
- No Physics/Collision (uses custom collision)

### 2. Component-Based Entities

Inspired by League of Legends, entities store state in separate component structs:

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
- **Replication frequency:** High-frequency (Transform every tick) vs low-frequency (Stats on change only)
- **Delta compression:** Only send changed components
- **Modularity:** Add/remove components without affecting others

---

## Architecture

### Core Classes

| Class | File | Purpose |
|-------|------|---------|
| **SimWorld** | `GameSim/Core/SimWorld.cs:19` | Simulation container, tick loop, entity management |
| **TickClock** | `GameSim/Core/TickClock.cs:13` | Fixed timestep clock, tick counting |
| **SimConfig** | `GameSim/Core/SimConfig.cs:14` | All gameplay constants (speeds, physics, arena, etc.) |
| **MovementEngine** | `GameSim/Core/MovementEngine.cs:15` | Physics engine (gravity, friction, impulses, air control) |
| **SimEntity** | `GameSim/Entities/SimEntity.cs:45` | Abstract base entity class |
| **SimPlayer** | `GameSim/Entities/SimPlayer.cs:19` | Player entity with component states |
| **SimProjectile** | `GameSim/Entities/SimProjectile.cs:10` | Server-side projectile (not a networked entity) |
| **CommandDispatcher** | `GameSim/Commands/CommandDispatcher.cs:18` | Routes commands to handlers by category |
| **ICommandHandler** | `GameSim/Commands/ICommandHandler.cs:13` | Handler interface |
| **MovementHandler** | `GameSim/Commands/Handlers/MovementHandler.cs:14` | Handles movement commands |
| **AbilityHandler** | `GameSim/Commands/Handlers/AbilityHandler.cs:10` | Server-side ability execution |
| **SimCommand** | `GameSim/Commands/SimCommand.cs:14` | Simulation-side command struct |

### Entity Types & States

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

---

## SimWorld - Simulation Container

**Location:** `Assets/GameSim/Core/SimWorld.cs:19`

SimWorld owns all entities and orchestrates the simulation tick.

### Properties

```csharp
// SimWorld.cs:23-44
public class SimWorld
{
    public TickClock Clock { get; }               // Simulation tick counter
    public SimConfig Config { get; }              // Configuration constants
    public bool IsServer { get; set; }            // Server vs client world
    public CommandDispatcher CommandDispatcher { get; } // Command routing
}
```

### Entity Storage

```csharp
// SimWorld.cs:47-52
private readonly Dictionary<uint, SimEntity> _entities = new();
private readonly Dictionary<int, SimPlayer> _playersByClient = new();
private readonly List<uint> _entitiesToRemove = new();
private readonly List<SimEntity> _entitiesToAdd = new();
private readonly List<SimProjectile> _projectiles = new();
private readonly List<SimEvent> _eventQueue = new();
```

### Events

```csharp
// SimWorld.cs:60-92
public event Action<SimEntity> OnEntitySpawned;
public event Action<uint, EntityType> OnEntityDestroyed;

// Simulation events (damage, death, abilities, respawn) are NOT C# events.
// They are queued as SimEvent structs and drained by ServerGameLoop after each Step().
public void RaiseEvent(SimEvent evt);           // Enqueue a SimEvent
public List<SimEvent> DrainEvents();            // Drain and clear queue; returns null if empty
```

### Entity Management

```csharp
// SimWorld.cs:111-216
SimPlayer SpawnPlayer(int clientId, Vector3 position, byte teamId);       // Auto-ID
SimPlayer SpawnPlayer(uint entityId, int clientId, Vector3 position, byte teamId); // Explicit ID
void DestroyEntity(uint entityId);        // Deferred removal (end of tick)
SimPlayer GetPlayerByClient(int clientId);
SimEntity GetEntity(uint entityId);
T GetEntity<T>(uint entityId) where T : SimEntity;
bool HasEntity(uint entityId);
IEnumerable<SimEntity> AllEntities;
IEnumerable<SimPlayer> AllPlayers;
```

### Tick Flow (Command-Based)

The main simulation method is `Step()` (command-based). Commands are applied separately via `ExecuteCommand()` as they arrive from the network, not during the tick.

```csharp
// SimWorld.cs:257-293 - Step() (command-based)
public void Step()
{
    // 1. Tick all entities (movement, abilities, combat)
    foreach (var entity in _entities.Values)
        if (!entity.IsMarkedForRemoval)
            entity.Tick(Clock.TickDelta, Config);

    // 2. Process player-vs-player collisions (push-apart)
    ProcessCollisions();

    // 3. Tick projectiles (move + hit detection, server-side)
    TickProjectiles(Clock.TickDelta);

    // 4. Add pending entities
    foreach (var entity in _entitiesToAdd) { ... }
    _entitiesToAdd.Clear();

    // 5. Remove destroyed entities
    ProcessRemovals();

    // 6. Advance tick counter
    Clock.Advance();
}
```

### Command Execution

Commands are applied **immediately** when received, not queued per tick:

```csharp
// SimWorld.cs:243-251
public bool ExecuteCommand(int clientId, in SimCommand cmd)
{
    if (_playersByClient.TryGetValue(clientId, out var player))
    {
        player.LastCommandSeq = cmd.Sequence;
        return CommandDispatcher.Execute(player, cmd);
    }
    return false;
}
```

### Respawn & World Management

```csharp
// SimWorld.cs:433 - Called by ServerGameLoop when respawn timer expires
public void RespawnPlayer(uint entityId);
// Calls player.Respawn(spawnPos) and raises SimEvent(EntityRespawn) with quantized position.

// SimWorld.cs:460 - Reset all entities, queues, and clock
public void Clear();

// SimWorld.cs:477 - Inject spawn positions (server config or MatchConfig event on client)
public void SetSpawnPositions(Vector3[] team1Spawns, Vector3[] team2Spawns);

// SimWorld.cs:487 - Returns spawn position for a team/playerIndex.
// Uses injected arrays if set; falls back to algorithmic positions from SimConfig.
public Vector3 GetSpawnPosition(byte teamId, int playerIndex);
```

### Collision Processing

Brute-force player-vs-player push-apart (TODO: spatial partitioning for 50v50):

```csharp
// SimWorld.cs:316-346
private void ProcessCollisions()
{
    // O(n^2) brute force for prototype
    // Uses 0.5f player radius
    // Simple push-apart: overlap split equally between both players
}
```

### Projectile System

Projectiles are server-side only, not networked entities. Clients receive cosmetic visuals via `AbilityUsed` events.

```csharp
// SimWorld.cs:225-228
public void SpawnProjectile(SimProjectile proj)
{
    _projectiles.Add(proj);
}

// SimWorld.cs:352-400 - TickProjectiles
// Move projectiles, check collision vs enemy players,
// apply damage via player.TakeDamage(), raise SimEvent(DamageDealt) and SimEvent(EntityDeath)
```

---

## SimPlayer - Player Entity

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:19`

### Component States

| Component | Fields | Frequency |
|-----------|--------|-----------|
| **TransformState** | Position, RotationY, Velocity, IsGrounded, MoveDirection, IsMoving | Every tick (60 Hz) |
| **StatsState** | Health, MaxHealth, Level, Experience, MoveSpeedModifier | Delta only |
| **AbilityState** | Cooldowns[6], Levels[6], Charges[6], ChargeRecoveryTime[6], CurrentCast | Delta only |
| **CombatState** | AttackTargetId, IsAutoAttacking, AttackCooldown, AttackSpeed, AttackRange, BaseDamage, IsAttackMoving, AttackMoveTarget, TimeSinceLastAttack | Delta only |

### Identity & Network

```csharp
// SimPlayer.cs:54-79
public int OwnerClientId { get; }        // Client ID that owns this player
public int ClassId { get; set; }         // Character class (1=Archer, 2=Mage, 3=Fighter)
public uint LastCommandSeq { get; set; } // Last processed command seq (for reconciliation)
public float TimeSinceLastMoveCmd { get; set; } // Watchdog timer
public float LastMoveStopTime { get; set; }     // Debug logging
```

### Tick Loop

```csharp
// SimPlayer.cs:154-170
public override void Tick(float dt, SimConfig config)
{
    if (Stats.IsDead) return;

    TickMovement(dt, config);   // Apply velocity, gravity, rotation, arena bounds
    TickAbilities(dt);          // Decrease cooldowns, recover charges, progress casts
    TickCombat(dt, config);     // Decrease attack cooldown, track timing
}
```

### Movement Methods

```csharp
// SimPlayer.cs:249, 259, 279
public void SetMoveDirection(Vector3 direction);  // WASD movement (normalizes input)
public void SetMoveTarget(Vector3 target);        // Click-to-move (computes direction)
public void StopMoving();                         // Stop horizontal, preserve vertical velocity
```

### Combat Methods

```csharp
// SimPlayer.cs:288, 301, 318
public void TakeDamage(int damage, uint attackerId);
public void Heal(int amount);
public void Respawn(Vector3 position);  // Full HP, reset transform
```

---

## SimProjectile - Server-Side Projectile

**Location:** `Assets/GameSim/Entities/SimProjectile.cs:10`

SimProjectile is NOT a SimEntity. It's stored separately in `SimWorld._projectiles` and never appears in snapshots. Clients get cosmetic visuals via reliable events.

```csharp
// SimProjectile.cs:10-44
public class SimProjectile
{
    public uint OwnerEntityId { get; }
    public byte TeamId { get; }
    public Vector3 Position { get; private set; }
    public Vector3 Direction { get; }
    public float Speed { get; }
    public float Damage { get; }
    public float MaxRange { get; }
    public float Radius { get; }
    public float DistanceTraveled { get; private set; }
    public bool IsExpired => DistanceTraveled >= MaxRange;

    public void Tick(float dt)
    {
        float distance = Speed * dt;
        Position += Direction * distance;
        DistanceTraveled += distance;
    }
}
```

---

## MovementEngine - Physics Engine

**Location:** `Assets/GameSim/Core/MovementEngine.cs:15`

Static physics engine handling gravity, friction, impulses, and air control. Used by entities that need physics-based movement.

```csharp
// MovementEngine.cs:28-73 - Main tick
public static void Tick(ref TransformState transform, float moveSpeed, SimConfig config, float dt,
    bool useTurnSlowdown = false)
{
    // 1. Calculate input velocity from move direction (with optional turn slowdown)
    // 2. Apply gravity (grounded pull-down or freefall)
    // 3. Apply friction (exponential drag: ground=high, air=low)
    // 4. Apply air control (limited directional influence when airborne)
    // 5. Combine impulse + input velocity, move position; store as EffectiveVelocity
    // 6. Ground check (Y=0 plane) and arena bounds clamp
    // 7. Zero out vertical EffectiveVelocity when grounded (prevents client dead-reckoning drift)
    // 8. Update rotation toward move direction
}

// Impulse API
public static void ApplyImpulse(ref TransformState transform, Vector3 impulse);
public static void ApplyJump(ref TransformState transform, float moveSpeed, SimConfig config);
public static void ApplyAirControl(ref TransformState transform, Vector3 direction, SimConfig config, float dt);
```

`SimPlayer.TickMovement()` (line 172) delegates directly to `MovementEngine.Tick()` — there is no inline physics in SimPlayer.

---

## TickClock - Simulation Time

**Location:** `Assets/GameSim/Core/TickClock.cs:13`

Fixed timestep clock. Tick rate and delta are sourced from `NetcodeConstants`.

```csharp
// TickClock.cs:13-169
public class TickClock
{
    public const int TICK_RATE = NetcodeConstants.TICK_RATE;    // 60 Hz
    public const float TICK_DELTA = NetcodeConstants.TICK_DELTA; // 0.01667s
    public const float TICK_DELTA_MS = NetcodeConstants.TICK_DELTA_MS;

    public uint CurrentTick { get; private set; }  // Monotonic tick counter
    public float TickDelta => TICK_DELTA;
    public int TickRate => TICK_RATE;

    // Accumulate frame time, return ticks to simulate (caps at 5 to prevent spiral of death)
    public int Accumulate(float deltaTime);

    public void Advance();              // Increment by 1
    public void Advance(int count);     // Increment by N (fast-forward)
    public void SetTick(uint tick);     // Reset to specific tick (reconciliation)
    public void Reset(uint startTick = 0);

    // Utilities
    public float InterpolationAlpha => _accumulator / TICK_DELTA;  // 0-1 for visual smoothing
    public float SimulationTime => CurrentTick * TICK_DELTA;
    public static float TickToTime(uint tick);
    public static uint TimeToTick(float time);
    public static int GetTickDifference(uint a, uint b);  // Handles wrap-around
    public static bool IsAfter(uint a, uint b);
    public static bool IsBefore(uint a, uint b);
}
```

---

## SimConfig - Configuration

**Location:** `Assets/GameSim/Core/SimConfig.cs:14`

Holds all gameplay-affecting constants. Uses properties (not fields) for all values.

```csharp
// SimConfig.cs:14-273
public class SimConfig
{
    public static SimConfig Default => new SimConfig();

    // Movement
    public float PlayerMoveSpeed { get; set; } = NetcodeConstants.PLAYER_SPEED; // 8 u/s
    public float PlayerRotationSpeed { get; set; } = 720f;

    // Combat
    public float PlayerMaxHealth { get; set; } = 100f;
    public float PlayerBaseDamage { get; set; } = 10f;
    public float PlayerAttackRange { get; set; } = 2.0f;
    public float PlayerAttackCooldown { get; set; } = 0.8f;
    public float RespawnTime { get; set; } = 5f;

    // Projectiles
    public float ProjectileSpeed { get; set; } = 20f;
    public float ProjectileLifetime { get; set; } = 5f;
    public float ProjectileRadius { get; set; } = 0.25f;
    public float PiercingShotCooldown { get; set; } = 6f;
    public float PiercingShotDamage { get; set; } = 30f;
    public float PiercingShotRange { get; set; } = 16f;

    // Arena
    public float ArenaWidth { get; set; } = 200f;
    public float ArenaHeight { get; set; } = 200f;
    public float MaxHeight { get; set; } = 20f;

    // Physics
    public float Gravity { get; set; } = -20f;
    public float TerminalVelocity { get; set; } = -50f;
    public float JumpHeight { get; set; } = 1.5f;
    public float AirControlStrength { get; set; } = 5f;
    public float GroundedPullDown { get; set; } = -2f;
    public float GroundDrag { get; set; } = 15f;       // Exponential decay, quick stop
    public float AirDrag { get; set; } = 0.5f;         // Low drag, momentum preserved
    public float TurnSlowdownAngle { get; set; } = 90f;
    public float TurnSlowdownMultiplier { get; set; } = 0.2f;

    // Teams
    public float Team1SpawnX { get; set; } = -80f;
    public float Team2SpawnX { get; set; } = 80f;
    public float SpawnSpread { get; set; } = 10f;

    // Network
    public int InputBufferSize { get; set; } = 64;
    public int SnapshotBufferSize { get; set; } = 32;
    public float InterpolationDelay { get; set; } = 0.1f;
    public float ReconciliationThreshold { get; set; } = 0.1f;

    public SimConfig Clone(); // Deep copy
}
```

---

## AbilityHandler - Server-Side Ability Execution

**Location:** `Assets/GameSim/Commands/Handlers/AbilityHandler.cs:10`

Concrete `ICommandHandler` for ability execution. Currently implements Piercing Shot (Archer Q).

```csharp
// AbilityHandler.cs:16
public class AbilityHandler : ICommandHandler
{
    // Public API: called by CommandDispatcher (line 18)
    public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
    {
        byte slot = cmd.Action;
        bool success = TryCastAbility(player, slot, cmd.TargetPosition, world, config);
        if (success)
        {
            world.RaiseEvent(new SimEvent { Type = SimEventType.AbilityUsed, EntityId = player.Id, Data1 = slot, ... });
        }
    }

    // Private helper: validates and executes the cast (line 41)
    private static bool TryCastAbility(SimPlayer player, byte slot, Vector3 targetPos,
        SimWorld world, SimConfig config)
    {
        if (!player.Abilities.CanCast(slot)) return false;
        if (slot != 0) return false; // Only slot 0 (Piercing Shot) for now

        // Set cooldown, spawn projectile
        player.Abilities.Cooldowns[slot] = config.PiercingShotCooldown;
        world.SpawnProjectile(new SimProjectile(...));
        return true;
    }
}
```

AbilityHandler is registered in CommandDispatcher. On success it raises `SimEventType.AbilityUsed`, which ServerGameLoop drains and broadcasts as a `ReliableEvent`.

---

## SimEntity - Base Class

**Location:** `Assets/GameSim/Entities/SimEntity.cs:45`

Abstract base class for all simulation entities.

```csharp
// SimEntity.cs:45-169
public abstract class SimEntity
{
    // Static ID management (auto-increment from 1)
    private static uint _nextId = 1;
    public static void ResetIdCounter();

    // Properties
    public uint Id { get; }
    public EntityType Type { get; protected set; }
    public EntityState State { get; set; }
    public byte TeamId { get; set; }         // 0 = neutral
    public uint SpawnTick { get; set; }
    public bool IsMarkedForRemoval { get; set; }

    // Constructors
    protected SimEntity();           // Auto-generated ID
    protected SimEntity(uint id);    // Specific ID (ensures _nextId stays ahead)

    // Simulation
    public abstract void Tick(float dt, SimConfig config);

    // Team utilities
    public bool IsHostileTo(SimEntity other);   // Different non-zero teams
    public bool IsFriendlyTo(SimEntity other);  // Same non-zero team
}
```

---

## Command System

Commands are the **only** way to modify SimWorld state. See [02_Command_System.md](02_Command_System.md) for details.

**Quick overview:**

```
Client input changes → GameCommand (14 bytes, quantized) → Network
→ Server receives → CommandHelper.ToSimCommand() → SimCommand (Vector3, interpreted)
→ SimWorld.ExecuteCommand() → CommandDispatcher.Execute() → Handler modifies entity state
```

```csharp
// Actual flow in SimWorld.cs:243-251
var simCmd = CommandHelper.ToSimCommand(gameCmd, _simWorld.Clock.CurrentTick);
_simWorld.ExecuteCommand(clientId, simCmd);
    // → player.LastCommandSeq = cmd.Sequence;
    // → CommandDispatcher.Execute(player, cmd);
    //   → MovementHandler: player.SetMoveDirection(cmd.Direction)
```

---

## Entity Lifecycle

### Spawning

```csharp
// SimWorld.cs:111-129
var player = simWorld.SpawnPlayer(clientId: 42, position: spawnPos, teamId: 1);
player.ClassId = 2; // Mage

// Event fired: OnEntitySpawned(player)
// Server broadcasts position in next snapshot
```

### Updating

```csharp
// Each tick (60 Hz) via SimWorld.Step()
foreach (var entity in _entities.Values)
    if (!entity.IsMarkedForRemoval)
        entity.Tick(Clock.TickDelta, Config);
// SimPlayer.Tick → TickMovement → TickAbilities → TickCombat
```

### Destruction

```csharp
// SimWorld.cs:156-164
simWorld.DestroyEntity(entityId);
// Sets IsMarkedForRemoval = true, adds to _entitiesToRemove
// Event fired: OnEntityDestroyed(id, type)
// Actual removal happens at end of tick via ProcessRemovals()
```

---

## State Replication Strategy

### High-Frequency State (Every Tick)

**TransformState:**
- Position, Velocity, RotationY, IsGrounded, IsMoving
- **Frequency:** 60 Hz (every snapshot)

### Low-Frequency State (Delta Only)

**StatsState, AbilityState, CombatState:**
- Only sent when changed
- **Frequency:** ~1-10 Hz (depending on gameplay)

**Bandwidth estimate (100 players):**
```
High-frequency: 100 entities x 60 Hz x ~15 bytes = ~90 KB/s
Low-frequency:  occasional delta updates            = ~10 KB/s
Total:                                               ~100 KB/s
```

---

## Testing GameSim

### Unit Testing

GameSim has no MonoBehaviour dependencies, so you can unit test with Unity's Test Runner:

```csharp
[Test]
public void TestPlayerMovement()
{
    var config = new SimConfig { PlayerMoveSpeed = 10f };
    var world = new SimWorld(config);
    var player = world.SpawnPlayer(clientId: 1, Vector3.zero, teamId: 1);

    player.SetMoveDirection(Vector3.right);  // Move right
    world.Step();  // Simulate one tick

    Assert.Greater(player.Transform.Position.x, 0f);
}
```

---

## Related Documentation

- **[02_Command_System.md](02_Command_System.md)** - Command routing and handlers
- **[03_Entity_Model.md](03_Entity_Model.md)** - Detailed entity state components
- **[../Server/01_Server_Loop.md](../Server/01_Server_Loop.md)** - How GameSim integrates with Unity server
- **[../Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)** - Broader architecture context

---

**Last Updated:** 2026-02-18

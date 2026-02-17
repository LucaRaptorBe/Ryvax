# GameSim Overview

**Simulation Layer - Deterministic, Server-Authoritative (depends on UnityEngine for Vector3/Mathf)**

GameSim is the core simulation engine for Ryvax, inspired by League of Legends' architecture. It runs identically on server and client, with a dependency on UnityEngine for math primitives (Vector3, Mathf) and data assets (ScriptableObject).

---

## Design Philosophy

### 1. Simulation Logic (UnityEngine for math primitives)

GameSim uses UnityEngine **only** for:
- `Vector3`, `Vector2` (math primitives)
- `Mathf` (math functions)
- `Quaternion` (rotation math)

**No Unity-specific dependencies:**
- ❌ No `MonoBehaviour`
- ❌ No `GameObject`
- ❌ No `Transform`
- ❌ No `Rigidbody`
- ❌ No Physics/Collision (uses custom collision)

**Why?**
- **Testable:** Can unit test outside Unity
- **Portable:** Could run on headless server
- **Deterministic:** No hidden Unity state
- **Performance:** No GameObject overhead

---

### 2. Deterministic Simulation

GameSim produces **identical results** given identical inputs, critical for:
- **Replay system:** Re-run simulation for eSports
- **Debugging:** Reproduce bugs from logs
- **Validation:** Verify server/client match

**Requirements:**
- Fixed timestep (60 Hz)
- No floating-point non-determinism (use quantization)
- No random sources (use seeded RNG)
- No hidden state (all state in components)

---

### 3. Component-Based Entities

Inspired by **League of Legends' ECS architecture**, entities store state in separate components:

```csharp
// SimPlayer.cs:20-46
public class SimPlayer : SimEntity
{
    public TransformState Transform;  // Position, rotation, velocity
    public StatsState Stats;          // HP, mana, resistances
    public AbilityState Abilities;    // Cooldowns, casts, levels
    public CombatState Combat;        // Attack, target, damage
}
```

**Benefits:**
- **Replication frequency:** High-frequency (Transform) vs low-frequency (Stats)
- **Delta compression:** Only send changed components
- **Modularity:** Add/remove components without affecting others

---

## Architecture

### Core Classes

| Class | File | Purpose |
|-------|------|---------|
| **SimWorld** | `Assets/GameSim/Core/SimWorld.cs:21` | Simulation container, tick loop |
| **TickClock** | `Assets/GameSim/Core/TickClock.cs` | Manages simulation tick count |
| **SimConfig** | `Assets/GameSim/Core/SimConfig.cs` | Configuration constants |
| **SimEntity** | `Assets/GameSim/Entities/SimEntity.cs` | Base entity class |
| **SimPlayer** | `Assets/GameSim/Entities/SimPlayer.cs:19` | Player entity with components |
| **CommandDispatcher** | `Assets/GameSim/Commands/CommandDispatcher.cs:18` | Routes commands to handlers |

---

## SimWorld - Simulation Container

**Location:** `Assets/GameSim/Core/SimWorld.cs:21`

SimWorld is the main container for the entire game state. It owns all entities and orchestrates the simulation tick.

### Key Properties

```csharp
// SimWorld.cs:23-43
public class SimWorld
{
    public TickClock Clock { get; }               // Simulation tick counter
    public SimConfig Config { get; }              // Configuration constants
    public bool IsServer { get; set; }            // Server vs client world
    public CommandDispatcher CommandDispatcher { get; } // Command routing
}
```

### Entity Management

```csharp
// SimWorld.cs:49-58
private Dictionary<uint, SimEntity> _entities;           // All entities by ID
private Dictionary<int, SimPlayer> _playersByClient;     // Players by client ID
private List<uint> _entitiesToRemove;                    // Deferred removal queue
private List<SimEntity> _entitiesToAdd;                  // Deferred spawn queue
```

**Why deferred add/remove?**
- Prevents modifying collections during iteration
- Entities removed at end of tick (deterministic order)

### Tick Flow

```csharp
// Pseudo-code from SimWorld.cs
public void Tick(float deltaTime)
{
    // 1. Add pending entities
    ProcessPendingAdds();

    // 2. Process commands via dispatcher
    CommandDispatcher.ProcessPendingCommands();

    // 3. Update entity physics/movement
    foreach (var entity in _entities.Values)
        entity.Update(deltaTime);

    // 4. Remove destroyed entities
    ProcessPendingRemovals();

    // 5. Increment tick counter
    Clock.AdvanceTick();
}
```

**Call site:** `Server/ServerGameLoop.cs:909` (server) or `NetworkClient` (client debugging)

---

## SimPlayer - Player Entity

**Location:** `Assets/GameSim/Entities/SimPlayer.cs:19`

SimPlayer represents a player-controlled character with component-based state.

### Component Architecture

#### 1. TransformState (High-Frequency)

```csharp
// GameSim/States/TransformState.cs
public struct TransformState
{
    public Vector3 Position;    // World position
    public float RotationY;     // Y-axis rotation (degrees)
    public Vector3 Velocity;    // Movement velocity
    public float Speed;         // Movement speed (u/s)
}
```

**Replication:** Every tick (~60 Hz), because position changes frequently.

#### 2. StatsState (Low-Frequency)

```csharp
// GameSim/States/StatsState.cs
public struct StatsState
{
    public int Health;          // Current HP
    public int MaxHealth;       // Maximum HP
    public int Mana;            // Current mana
    public int MaxMana;         // Maximum mana
    public float Armor;         // Physical resistance
    public float MagicResist;   // Magic resistance
    public bool IsAlive;        // Alive flag
}
```

**Replication:** Only when changed (delta compression).

#### 3. AbilityState (Low-Frequency)

```csharp
// GameSim/States/AbilityState.cs (planned)
public struct AbilityState
{
    public float[] Cooldowns;   // Cooldown timers
    public int[] Levels;        // Ability levels
    public uint CastingAbility; // Currently casting
}
```

**Replication:** Only when changed.

#### 4. CombatState (Low-Frequency)

```csharp
// GameSim/States/CombatState.cs (planned)
public struct CombatState
{
    public uint TargetEntityId; // Auto-attack target
    public float AttackTimer;   // Time until next attack
    public int LastDamage;      // Last damage dealt
}
```

**Replication:** Only when changed.

### Player Identity

```csharp
// SimPlayer.cs:49-60
public int OwnerClientId { get; }    // Client ID that owns this player
public int ClassId { get; set; }     // Character class (1=Archer, 2=Mage, 3=Fighter, etc.)
```

### Network Reconciliation

```csharp
// SimPlayer.cs:63-79
public uint LastCommandSeq { get; set; }        // Last processed command seq
public float TimeSinceLastMoveCmd { get; set; } // Watchdog timer
public float LastMoveStopTime { get; set; }     // Debug logging
```

**Purpose:**
- `LastCommandSeq`: Sent in snapshots for lag measurement
- `TimeSinceLastMoveCmd`: Server stops player if no commands for 1 second
- `LastMoveStopTime`: Debug logging for movement commands

---

## TickClock - Simulation Time

**Location:** `Assets/GameSim/Core/TickClock.cs`

TickClock tracks simulation ticks for deterministic replay.

```csharp
public class TickClock
{
    public uint CurrentTick { get; private set; }  // Monotonic tick counter

    public void AdvanceTick()
    {
        CurrentTick++;
    }

    public void Reset()
    {
        CurrentTick = 0;
    }
}
```

**Usage:**
```csharp
// Server tick loop
void FixedUpdate()
{
    _simWorld.Tick(Time.fixedDeltaTime);  // Increments Clock.CurrentTick
    BroadcastSnapshots(_simWorld.Clock.CurrentTick);
}
```

**Tick → Time conversion:**
```csharp
// NetcodeConstants.cs
public const int TICK_RATE = 60;  // 60 Hz
public const float TICK_INTERVAL = 1f / TICK_RATE;  // 0.01667s

float TickToTime(uint tick) => tick * TICK_INTERVAL;
uint TimeToTick(float time) => (uint)(time / TICK_INTERVAL);
```

---

## SimConfig - Configuration

**Location:** `Assets/GameSim/Core/SimConfig.cs`

SimConfig holds simulation constants (speeds, ranges, costs).

```csharp
public class SimConfig
{
    public static SimConfig Default { get; } = new SimConfig();

    // Movement
    public float PlayerSpeed = 8f;           // Default movement speed (u/s)
    public float PlayerRotationSpeed = 720f; // Rotation speed (deg/s)

    // Combat
    public float AttackRange = 2f;           // Auto-attack range
    public float AttackSpeed = 1f;           // Attacks per second

    // Physics
    public float CollisionRadius = 0.5f;     // Player collision radius
}
```

**Dependency injection:**
```csharp
// Server
var config = new SimConfig { PlayerSpeed = 10f };
var world = new SimWorld(config);

// All entities use world.Config.PlayerSpeed
```

---

## Command System

Commands are the **only** way to modify SimWorld state. See [Command_System.md](Command_System.md) for details.

**Quick overview:**

```
Client sends InputIntent → Server converts to SimCommand
→ CommandDispatcher routes to handler → Handler modifies entity state
```

**Command flow:**
```csharp
// Server receives input
var cmd = new SimCommand
{
    Category = CommandCategory.Movement,
    Type = (byte)MovementCommandType.MoveDir,
    SeqId = intent.SeqId,
    Payload0 = EncodeDirection(intent.Direction)
};

// Dispatch to handler
CommandDispatcher.Execute(player, cmd);

// MovementHandler applies direction
player.Transform.Velocity = direction * player.Transform.Speed;
```

---

## Entity Lifecycle

### Spawning

```csharp
// Server spawns player
var player = new SimPlayer(ownerClientId: 42)
{
    ClassId = 2,  // Mage
    Transform = { Position = spawnPos, Speed = 8f }
};
simWorld.AddEntity(player);

// Event fired: OnEntitySpawned
```

**Server broadcasts:**
```
SnapshotDelta {
    Entities: [
        { Id = 1, Type = Player, Position = (0, 0, 0), ... }
    ]
}
```

### Updating

```csharp
// Each tick (60 Hz)
simWorld.Tick(0.01667f);
    → foreach entity: entity.Update(dt)
        → Apply velocity: Position += Velocity * dt
        → Apply gravity, collisions, etc.
```

### Destruction

```csharp
// Server destroys entity (e.g., player dies)
simWorld.RemoveEntity(player.Id);

// Event fired: OnEntityDestroyed(id, type)
```

**Server broadcasts:**
```
SnapshotDelta {
    Entities: []  // Entity no longer in snapshot
}
```

**Client removes view when entity disappears from snapshots.**

---

## State Replication Strategy

### High-Frequency State (Every Tick)

**TransformState:**
- Position, Velocity, Rotation, Speed
- **Size:** 26 bytes (quantized)
- **Frequency:** 60 Hz (every snapshot)

### Low-Frequency State (Delta Only)

**StatsState, AbilityState, CombatState:**
- Only sent when changed
- **Size:** Variable (delta compression)
- **Frequency:** ~1-10 Hz (depending on gameplay)

**Example:**
```
Tick 100: Player casts ability
    → SnapshotDelta includes AbilityState (cooldown started)
Tick 101-199: No ability changes
    → SnapshotDelta omits AbilityState (saves bandwidth)
Tick 200: Cooldown expires
    → SnapshotDelta includes AbilityState (cooldown ready)
```

**Bandwidth savings:**
```
Without delta: 100 entities × 60 Hz × 100 bytes = 600 KB/s
With delta:    100 entities × 60 Hz × 26 bytes  = 156 KB/s (Transform only)
              +occasional low-frequency state     = ~200 KB/s total
Savings: 67%
```

---

## Determinism Checklist

To maintain determinism, GameSim **must**:

✅ **Fixed timestep:** Always 0.01667s (60 Hz)
```csharp
// Unity FixedUpdate guarantees this
Time.fixedDeltaTime = 1f / 60f;
```

✅ **No hidden state:** All state in components
```csharp
// Bad: static float cachedValue;  // Hidden state!
// Good: player.Transform.Speed;   // Explicit state
```

✅ **Seeded RNG:** Use deterministic random
```csharp
// Good: var rng = new System.Random(seed);
// Bad:  var rng = new System.Random();  // Time-based seed!
```

✅ **No time-based logic:** Use tick counter
```csharp
// Bad:  if (Time.time > 10f) ...  // Non-deterministic!
// Good: if (Clock.CurrentTick > 600) ...  // Deterministic
```

✅ **Quantize floats:** Network messages must be deterministic
```csharp
// Encode position with fixed precision
ushort QuantizePos(float v) => (ushort)((v + 100f) / 200f * 65535);
float DequantizePos(ushort v) => v / 65535f * 200f - 100f;
```

✅ **Entity update order:** Same order every tick
```csharp
// Iterate entities by sorted ID
foreach (var id in _entities.Keys.OrderBy(x => x))
    _entities[id].Update(dt);
```

---

## Testing GameSim

### Unit Testing (Outside Unity)

GameSim has no MonoBehaviour dependencies, so you can unit test with Unity's Test Runner:

```csharp
[Test]
public void TestPlayerMovement()
{
    var config = new SimConfig { PlayerSpeed = 10f };
    var world = new SimWorld(config);
    var player = new SimPlayer(1);

    world.AddEntity(player);
    player.Transform.Velocity = new Vector3(1, 0, 0);  // Move right

    world.Tick(0.1f);  // Simulate 100ms

    Assert.AreEqual(new Vector3(1, 0, 0), player.Transform.Position);
}
```

### Replay Testing

Record inputs, replay deterministically:

```csharp
// Record session
List<(uint tick, SimCommand cmd)> recording;
recording.Add((tick, cmd));

// Replay session
var replayWorld = new SimWorld(originalConfig);
foreach (var (tick, cmd) in recording)
{
    while (replayWorld.Clock.CurrentTick < tick)
        replayWorld.Tick(TICK_INTERVAL);
    replayWorld.CommandDispatcher.Execute(player, cmd);
}

// Assert: replayWorld state == original state
```

---

## Performance Characteristics

### Memory Usage

| Component | Size per Entity | Notes |
|-----------|-----------------|-------|
| SimPlayer | ~200 bytes | Including all state components |
| TransformState | 40 bytes | Vector3 × 2 + floats |
| StatsState | 24 bytes | Ints + floats |
| AbilityState | 32 bytes | Arrays |
| CombatState | 16 bytes | Uint + floats |

**100 players:** ~20 KB (minimal)

### CPU Usage (Estimated)

| Operation | Cost per Entity | 100 Entities @ 60Hz |
|-----------|-----------------|---------------------|
| Update position | ~0.01ms | 1ms |
| Collision check | ~0.05ms | 5ms (with spatial hash) |
| Command dispatch | ~0.005ms | 0.5ms |
| **Total** | ~0.065ms | **6.5ms** |

**Budget:** 16.67ms per tick (60 Hz) → ~40% utilization with 100 entities.

---

## Future Enhancements

### 1. Full ECS Migration

Currently hybrid (OOP with component structs). Could migrate to pure ECS:
```csharp
// Pure ECS approach
ComponentArray<TransformState> transforms;
ComponentArray<StatsState> stats;

// System iteration
foreach (var entityId in entities)
    transforms[entityId].Position += transforms[entityId].Velocity * dt;
```

**Benefits:** Cache-friendly iteration, better performance at scale (1000+ entities).

### 2. Multithreading

Parallelize entity updates:
```csharp
Parallel.ForEach(entities, entity => entity.Update(dt));
```

**Challenge:** Determinism requires **sorted** updates (same order every tick).

### 3. Server-Side Pathfinding

Integrate A* pathfinding for `MoveTo` commands:
```csharp
// MovementHandler.cs
if (cmd.Type == MovementCommandType.MoveTo)
{
    Vector3 target = DecodePosition(cmd.Payload0, cmd.Payload1);
    Vector3[] path = _pathfinder.FindPath(player.Transform.Position, target);
    player.SetPath(path);
}
```

---

## Related Documentation

- **[Command_System.md](Command_System.md)** - Command routing and handlers
- **[Entity_Model.md](Entity_Model.md)** - Detailed entity state components
- **[Server/Server_Loop.md](../Server/Server_Loop.md)** - How GameSim integrates with Unity server
- **[Architecture/02_LoL_Style_Netcode.md](../Architecture/02_LoL_Style_Netcode.md)** - Broader netcode context

---

**Last Updated:** 2026-02-03

#rules-verified

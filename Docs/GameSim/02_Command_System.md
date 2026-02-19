# Command System

**Event-Based Command Dispatch - Category-Driven, Extensible, Server-Authoritative**

The Command System is Ryvax's event-based input handling architecture, inspired by League of Legends. Commands are sent only when input state changes (not every tick), routed by category to specialized handlers, and executed in the simulation.

---

## Design Philosophy

### 1. Event-Based (Not Per-Tick)

**Traditional approach (per-tick input):**
```
Every tick: Send InputState { WASD, MousePos, Abilities }
-> 60 Hz x 14 bytes = 840 bytes/sec per player
```

**Ryvax approach (event-based commands):**
```
Only when input changes: Send GameCommand { Category, Action, Data }
-> ~5-10 commands/sec x 14 bytes = 70-140 bytes/sec per player
```

**Savings: 83-91% bandwidth reduction**

### 2. Category-Based Dispatch

Commands are routed by **category** to specialized handlers:

```csharp
// CommandTypes.cs:10-31
public enum CommandCategory : byte
{
    None = 0,

    // === Movement (1-19) ===
    Movement = 1,

    // === Combat (20-39) ===
    Attack = 20,
    Ability = 21,

    // === Items (40-59) ===
    Item = 40,
    Shop = 41,

    // === Social (60-79) ===
    Ping = 60,
    Emote = 61,

    // === System (200+) ===
    System = 200,
}
```

**Extensibility:** New features only require:
1. Add category to `CommandCategory` enum
2. Create handler implementing `ICommandHandler`
3. Register handler in `CommandDispatcher`

### 3. Network/Simulation Layer Separation

**Two command structs:**
- **GameCommand** (`NetAdapter/Messages/GameCommand.cs:18`): Network layer, 14 bytes, blittable
- **SimCommand** (`GameSim/Commands/SimCommand.cs:14`): Simulation layer, interpreted data

**Conversion layer:**
- `CommandHelper.ToSimCommand()`: Network -> Simulation
- `CommandHelper.ToGameCommand()`: Simulation -> Network (for replay/debug)

**Why separate?**
- Network layer uses quantized shorts (bandwidth efficiency)
- Simulation layer uses Vector3/floats (precision, readability)
- Decoupling allows independent evolution of layers

---

## Architecture

### Core Types

| Type | File | Purpose |
|------|------|---------|
| **GameCommand** | `NetAdapter/Messages/GameCommand.cs:18` | Network message (14 bytes) |
| **SimCommand** | `GameSim/Commands/SimCommand.cs:14` | Simulation command |
| **CommandCategory** | `Network/Shared/CommandTypes.cs:10` | Category enum |
| **CommandDispatcher** | `GameSim/Commands/CommandDispatcher.cs:18` | Routes commands to handlers |
| **ICommandHandler** | `GameSim/Commands/ICommandHandler.cs:13` | Handler interface |
| **MovementHandler** | `GameSim/Commands/Handlers/MovementHandler.cs:14` | Movement command implementation |
| **AbilityHandler** | `GameSim/Commands/Handlers/AbilityHandler.cs:16` | Ability execution (ICommandHandler, raises SimEvent) |
| **SystemHandler** | `GameSim/Commands/Handlers/SystemHandler.cs:15` | System commands: ClassSelect (ICommandHandler, raises SimEvent) |
| **SimEvent** | `GameSim/Events/SimEvent.cs:13` | Lightweight event struct raised during simulation |
| **SimEventType** | `GameSim/Events/SimEventType.cs:10` | Event type enum (AbilityUsed, DamageDealt, EntityDeath, etc.) |
| **CommandHelper** | `NetAdapter/CommandHelper.cs:14` | GameCommand <-> SimCommand conversion |

---

## GameCommand - Network Layer

**Location:** `Assets/Scripts/Network/NetAdapter/Messages/GameCommand.cs:18`

GameCommand is the network message sent from client to server.

### Structure (14 bytes, blittable)

```csharp
// GameCommand.cs:18-63
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GameCommand : INetMessage
{
    public uint Sequence;           // 4 bytes - Command sequence number
    public CommandCategory Category; // 1 byte  - Movement/Ability/Item/etc.
    public byte Action;             // 1 byte  - MoveStart/CastQ/UseItem/etc.
    public short Data0;             // 2 bytes - Generic field 0
    public short Data1;             // 2 bytes - Generic field 1
    public uint Data2;              // 4 bytes - Generic field 2
}
// Total: 14 bytes
```

### Data Field Interpretation

Data fields are **polymorphic** - meaning changes based on Category+Action:

| Category | Action | Data0 | Data1 | Data2 |
|----------|--------|-------|-------|-------|
| Movement | Start/Change | Direction X (quantized) | Direction Z (quantized) | - |
| Movement | Stop | - | - | - |
| Movement | Jump | - | - | - |
| Ability | Cast | Target Pos X | Target Pos Z | Target Entity ID |
| Attack | Target | - | - | Target Entity ID |
| Attack | Move | Target Pos X | Target Pos Z | - |
| Item | Use | Slot | - | Target Entity ID |
| Item | Swap | From Slot | To Slot | - |
| System | ClassSelect | Class ID | - | - |

### Factory Methods

```csharp
// GameCommand.cs:70-81 - Movement Start
public static GameCommand MoveStart(uint seq, Vector2 direction)
{
    return new GameCommand
    {
        Sequence = seq,
        Category = CommandCategory.Movement,
        Action = MovementAction.Start,
        Data0 = QuantizeDirection(direction.x),  // -1 to 1 -> -127 to 127
        Data1 = QuantizeDirection(direction.y),
        Data2 = 0
    };
}

// GameCommand.cs:138-149 - Ability Cast
public static GameCommand CastAbility(uint seq, byte abilitySlot, Vector2 targetPos, uint targetEntityId = 0)
{
    return new GameCommand
    {
        Sequence = seq,
        Category = CommandCategory.Ability,
        Action = abilitySlot,  // CastQ=1, CastW=2, etc.
        Data0 = QuantizePosition(targetPos.x),  // World pos -> short (0.1 precision)
        Data1 = QuantizePosition(targetPos.y),
        Data2 = targetEntityId
    };
}
```

### Quantization

Quantization reduces float precision to fit in 16-bit shorts:

```csharp
// GameCommand.cs:314-341
// Direction: -1.0 to 1.0 -> -127 to 127 (7-bit precision)
public static short QuantizeDirection(float v)
    => (short)(Mathf.Clamp(v, -1f, 1f) * 127f);

public static float DequantizeDirection(short v)
    => v / 127f;

// Position: World coordinates -> short with 0.1 unit precision (+/-3276 range)
public static short QuantizePosition(float v)
    => (short)Mathf.Clamp(v * 10f, short.MinValue, short.MaxValue);

public static float DequantizePosition(short v)
    => v / 10f;
```

**Trade-offs:**
- Direction: +/-0.008 precision loss (imperceptible)
- Position: +/-0.05 unit precision (0.1 after rounding)
- **Savings:** 8 bytes (2 floats) -> 4 bytes (2 shorts) = 50% reduction

---

## SimCommand - Simulation Layer

**Location:** `Assets/GameSim/Commands/SimCommand.cs:14`

SimCommand is the interpreted command used by GameSim handlers.

### Structure

```csharp
// SimCommand.cs:14-64
public struct SimCommand
{
    // === Identity ===
    public uint Sequence;             // Command sequence number
    public CommandCategory Category;  // Movement/Ability/Item/etc.
    public byte Action;               // MoveStart/CastQ/UseItem/etc.
    public uint IssuedTick;           // Tick when command was issued (lag compensation)

    // === Interpreted Data (no quantization) ===
    public Vector3 Direction;         // Direction vector (movement, dash, directional abilities)
    public Vector3 TargetPosition;    // Target position (abilities, attack-move)
    public uint TargetEntityId;       // Target entity ID (targeted abilities, attacks)
    public byte Slot;                 // Slot number (items, abilities)
    public byte SecondarySlot;        // Secondary slot (item swap)
}
```

### Category & Movement Helpers

```csharp
// SimCommand.cs:66-117
public bool IsMovement => Category == CommandCategory.Movement;
public bool IsAbility => Category == CommandCategory.Ability;
public bool IsAttack => Category == CommandCategory.Attack;
public bool IsItem => Category == CommandCategory.Item;
public bool IsValid => Category != CommandCategory.None;

public bool IsMoveStart => IsMovement && (Action == MovementAction.Start || Action == MovementAction.Change);
public bool IsMoveStop => IsMovement && Action == MovementAction.Stop;
public bool IsJump => IsMovement && Action == MovementAction.Jump;
```

---

## CommandHelper - Conversion Layer

**Location:** `Assets/Scripts/Network/NetAdapter/CommandHelper.cs:14`

CommandHelper converts between GameCommand (network) and SimCommand (simulation).

### GameCommand -> SimCommand

```csharp
// CommandHelper.cs:20-55
public static SimCommand ToSimCommand(in GameCommand cmd, uint currentTick = 0)
{
    var simCmd = new SimCommand
    {
        Sequence = cmd.Sequence,
        Category = cmd.Category,
        Action = cmd.Action,
        IssuedTick = currentTick
    };

    switch (cmd.Category)
    {
        case CommandCategory.Movement:
            InterpretMovementCommand(ref simCmd, cmd);  break;
        case CommandCategory.Attack:
            InterpretAttackCommand(ref simCmd, cmd);    break;
        case CommandCategory.Ability:
            InterpretAbilityCommand(ref simCmd, cmd);   break;
        case CommandCategory.Item:
            InterpretItemCommand(ref simCmd, cmd);      break;
        case CommandCategory.System:
            InterpretSystemCommand(ref simCmd, cmd);    break;
    }

    return simCmd;
}
```

### Interpretation Examples

```csharp
// CommandHelper.cs:57-62 - Movement
private static void InterpretMovementCommand(ref SimCommand simCmd, in GameCommand cmd)
{
    Vector2 dir2D = cmd.GetDirection();  // Dequantize shorts -> floats
    simCmd.Direction = new Vector3(dir2D.x, 0f, dir2D.y);
}

// CommandHelper.cs:79-93 - Ability
private static void InterpretAbilityCommand(ref SimCommand simCmd, in GameCommand cmd)
{
    simCmd.Slot = cmd.Action;  // CastQ=1, CastW=2, etc.

    Vector2 pos2D = cmd.GetTargetPosition();  // Dequantize position
    simCmd.TargetPosition = new Vector3(pos2D.x, 0f, pos2D.y);

    simCmd.TargetEntityId = cmd.Data2;  // Target entity (0 = none)
}

// CommandHelper.cs:106-110 - System
private static void InterpretSystemCommand(ref SimCommand simCmd, in GameCommand cmd)
{
    // ClassSelect: class ID is stored in Data0
    simCmd.Slot = (byte)cmd.Data0;
}
```

---

## CommandDispatcher - Routing

**Location:** `Assets/GameSim/Commands/CommandDispatcher.cs:18`

CommandDispatcher routes commands to appropriate handlers using the **Strategy pattern**.

### Architecture

```csharp
// CommandDispatcher.cs:18-33
public class CommandDispatcher
{
    private readonly SimWorld _world;
    private readonly SimConfig _config;

    // Handlers registered by category (Strategy pattern)
    private readonly Dictionary<CommandCategory, ICommandHandler> _handlers = new();

    public CommandDispatcher(SimWorld world, SimConfig config)
    {
        _world = world;
        _config = config;
        RegisterDefaultHandlers();
    }
}
```

### Handler Registration

```csharp
// CommandDispatcher.cs:39-47
protected virtual void RegisterDefaultHandlers()
{
    RegisterHandler(CommandCategory.Movement, new MovementHandler());
    RegisterHandler(CommandCategory.Ability, new AbilityHandler());
    RegisterHandler(CommandCategory.System, new SystemHandler());
    // TODO: RegisterHandler(CommandCategory.Attack, new AttackHandler());
}

// CommandDispatcher.cs:53-56
public void RegisterHandler(CommandCategory category, ICommandHandler handler)
{
    _handlers[category] = handler;  // Replaces existing if present
}

// CommandDispatcher.cs:61-64
public bool UnregisterHandler(CommandCategory category)
{
    return _handlers.Remove(category);
}

// CommandDispatcher.cs:69-72
public bool HasHandler(CommandCategory category)
{
    return _handlers.ContainsKey(category);
}
```

### SimEvent Queue Pattern

Handlers raise `SimEvent` structs on `SimWorld` instead of directly broadcasting network messages. This keeps GameSim pure (no network dependency).

```
Handler.Execute()
  → world.RaiseEvent(new SimEvent { Type = AbilityUsed, ... })

ServerGameLoop.DrainAndBroadcastSimEvents()
  → foreach SimEvent: convert to ReliableEvent → SendToAll
```

**Event types:** `AbilityUsed`, `DamageDealt`, `EntityDeath`, `EntityRespawn`, `ClassAssign`

**Drain points:**
- After `_simWorld.ExecuteCommand()` in `OnCommandReceived()` — for command-driven events (AbilityUsed, ClassAssign)
- After `_simWorld.Step()` in `RunSimulation()` — for physics-driven events (DamageDealt, EntityDeath from projectile hits)
- After `_simWorld.RespawnPlayer()` in `TickRespawns()` — for respawn events

### Command Execution

```csharp
// CommandDispatcher.cs:80-94
public bool Execute(SimPlayer player, in SimCommand cmd)
{
    if (player == null || !cmd.IsValid)
        return false;

    if (_handlers.TryGetValue(cmd.Category, out var handler))
    {
        handler.Execute(player, cmd, _world, _config);
        return true;
    }

    UnityEngine.Debug.LogWarning($"[CommandDispatcher] No handler for category: {cmd.Category}");
    return false;
}
```

---

## ICommandHandler - Handler Interface

**Location:** `Assets/GameSim/Commands/ICommandHandler.cs:13`

```csharp
// ICommandHandler.cs:13-23
public interface ICommandHandler
{
    void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config);
}
```

**Parameters:**
- `player`: The player entity executing the command
- `cmd`: The command to execute (passed by `in` readonly reference)
- `world`: Simulation world (for entity queries, spawning, etc.)
- `config`: Simulation config (for speeds, ranges, cooldowns, etc.)

---

## MovementHandler - Implementation

**Location:** `Assets/GameSim/Commands/Handlers/MovementHandler.cs:14`

```csharp
// MovementHandler.cs:14-41
public class MovementHandler : ICommandHandler
{
    public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
    {
        if (!player.Stats.IsAlive) return;

        switch (cmd.Action)
        {
            case MovementAction.Start:
            case MovementAction.Change:
                player.SetMoveDirection(cmd.Direction);
                break;

            case MovementAction.Stop:
                player.StopMoving();
                break;

            case MovementAction.Jump:
                float moveSpeed = config.PlayerMoveSpeed * player.Stats.MoveSpeedModifier;
                MovementEngine.ApplyJump(ref player.Transform, moveSpeed, config);
                break;
        }
    }
}
```

**Key design:**
- Commands set **intent** (MoveDirection, IsMoving)
- `SimPlayer.Tick()` applies **physics** via `MovementEngine.Tick()`
- Separation of concerns: Handler = intent, Tick = physics

> **Note:** `MovementHandler` is registered in `CommandDispatcher` and **is used** by the
> server game loop for Jump commands, which are discrete events routed through `CommandDispatcher`.
> Continuous movement commands (Start, Change, Stop) bypass `CommandDispatcher` entirely and use
> a dedicated buffered intent path instead (see "Movement Flow" below).

---

## Command Flow - Two Paths

Movement and discrete commands (abilities, items, system) follow **separate paths** by design.

### Movement Flow (Buffered Intent Path)

Movement uses a dedicated, rate-limited, last-input-wins pipeline that bypasses `CommandDispatcher`.

#### 1. Client: Collect Input

```csharp
// InputCollector.cs detects WASD / click-to-move
// IntentBuilder rate-limits to 120Hz and creates InputIntent
InputIntent intent = _intentBuilder.OnKeyboardMove(inputDir, dt, clientTick);
// → MoveDir, MoveTo, Stop, or Follow
```

#### 2. Network: Transmit

```
Client -> [InputPacket with intent type + payload] -> Server (UDP, 120Hz)
```

Movement arrives as type-specific FishNet events (not `GameCommand`):
`OnMoveDirReceived`, `OnMoveToReceived`, `OnStopReceived`, `OnFollowReceived`.

#### 3. Server: Buffer

```csharp
// ServerGameLoop.cs:619 - Thread-safe enqueue
private void BufferMovementInput(clientId, seq, type, payload)
{
    uint applyTick = _simWorld.Clock.CurrentTick + 1;  // Bounded latency: +1 tick
    _inputQueue.Enqueue(new PendingMovementInput { ... });
}
```

#### 4. Server: Drain & Flush (start of each FixedUpdate tick)

```csharp
// ServerGameLoop.cs:300-301
DrainInputQueueForTick(currentTick);    // ConcurrentQueue → per-client Dictionary (last-input-wins)
FlushPendingMovementInputs(currentTick); // Apply each client's latest input
```

#### 5. Apply: Modify Entity State

```csharp
// ServerGameLoop.cs:707-736 - ApplyMovementInput()
switch (input.Type)
{
    case PacketIntentType.MoveDir:  player.SetMoveDirection(dir3D);   break;
    case PacketIntentType.MoveTo:   player.SetMoveTarget(target);     break;
    case PacketIntentType.Stop:     player.StopMoving();              break;
    case PacketIntentType.Follow:   player.SetMoveTarget(targetPos);  break;
}
```

#### 6. Simulation: Apply Physics

```csharp
// SimPlayer.TickMovement() delegates to MovementEngine
float moveSpeed = config.PlayerMoveSpeed * Stats.MoveSpeedModifier;
MovementEngine.Tick(ref Transform, moveSpeed, config, dt, useTurnSlowdown: true);
// → input velocity, gravity, friction, air control, EffectiveVelocity, position, rotation
```

#### 7. Network: Replicate

```
Server broadcasts snapshot (60Hz):
SnapshotDelta { Tick: 1234, Entities: [{ Position, EffectiveVelocity, RotationY, ... }] }
Client dead-reckons: visualPos = serverPos + EffectiveVelocity * dt
```

**Why a separate path?** Movement intents arrive at high frequency (~120Hz) and need
last-input-wins deduplication, bounded latency (+1 tick), and thread-safe buffering.
Discrete commands (abilities, items) are event-based with ack/retry and go through
`CommandDispatcher` (see below).

### Discrete Command Flow (CommandDispatcher Path)

Abilities, system commands, attacks, and items go through `CommandDispatcher`:

```
Client -> GameCommand -> ServerGameLoop.OnCommandReceived()
  → if (Movement && not Jump) → skip, ack only (handled by buffered path above)
  → if (Movement.Jump) → CommandHelper.ToSimCommand() → CommandDispatcher → MovementHandler
  → if (Ping) → echo immediately
  → else → CommandHelper.ToSimCommand() → _simWorld.ExecuteCommand()
           → CommandDispatcher.Execute() → AbilityHandler / SystemHandler / etc.
```

---

## Command Categories - Reference

### Movement Commands

```csharp
// CommandTypes.cs:36-42
public static class MovementAction
{
    public const byte Start = 1;   // Start moving in direction (continuous, buffered path)
    public const byte Change = 2;  // Change direction while moving (continuous, buffered path)
    public const byte Stop = 3;    // Stop moving (continuous, buffered path)
    public const byte Jump = 5;    // Jump (discrete event, through CommandDispatcher)
}
```

> **Discrete vs Continuous:** Start/Change/Stop are continuous movement intents handled by
> the buffered intent path (120Hz, last-input-wins). Jump is a discrete event routed through
> `CommandDispatcher` → `MovementHandler` like abilities.

### Ability Commands

```csharp
// CommandTypes.cs:47-55
public static class AbilityAction
{
    public const byte CastQ = 1;
    public const byte CastW = 2;
    public const byte CastE = 3;
    public const byte CastR = 4;
    public const byte CastSummoner1 = 10;
    public const byte CastSummoner2 = 11;
    public const byte Cancel = 99;
}
```

### Attack Commands

```csharp
// CommandTypes.cs:71-77
public static class AttackAction
{
    public const byte Start = 1;   // Start auto-attacking (nearest enemy)
    public const byte Stop = 2;    // Stop auto-attacking
    public const byte Target = 3;  // Attack specific target
    public const byte Move = 4;    // Attack-move (attack enemies on the way)
}
```

### Item Commands

```csharp
// CommandTypes.cs:62-66
public static class ItemAction
{
    public const byte Use = 1;
    public const byte Drop = 2;
    public const byte Swap = 3;
}
```

### System Commands

```csharp
// CommandTypes.cs:91-94
public static class SystemAction
{
    public const byte ClassSelect = 1;
}
```

### Ping Commands

```csharp
// CommandTypes.cs:82-86
public static class PingAction
{
    public const byte Request = 1;   // Client -> Server ping request
    public const byte Response = 2;  // Server -> Client pong response (via ReliableEvent)
}
```

---

## Extending the Command System

### Adding a New Command Category

**Example: Add "Emote" category**

#### 1. Define actions

```csharp
// CommandTypes.cs
public static class EmoteAction
{
    public const byte Dance = 1;
    public const byte Taunt = 2;
    public const byte Laugh = 3;
}
```

#### 2. Add factory method to GameCommand

```csharp
public static GameCommand PlayEmote(uint seq, byte emoteType)
{
    return new GameCommand
    {
        Sequence = seq, Category = CommandCategory.Emote,
        Action = emoteType, Data0 = 0, Data1 = 0, Data2 = 0
    };
}
```

#### 3. Add interpretation to CommandHelper

```csharp
private static void InterpretEmoteCommand(ref SimCommand simCmd, in GameCommand cmd)
{
    simCmd.Slot = cmd.Action;  // Emote type
}
```

#### 4. Create handler

```csharp
public class EmoteHandler : ICommandHandler
{
    public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
    {
        // Set entity state for animation
        player.State = EntityState.Casting; // or custom emote state
    }
}
```

#### 5. Register handler

```csharp
// CommandDispatcher.cs
RegisterHandler(CommandCategory.Emote, new EmoteHandler());
```

---

## Performance Characteristics

### Bandwidth

**Per command: 14 bytes**

**Typical command rate: 5-10 commands/second per player**
- Movement: 2-3/sec (start, change, stop)
- Abilities: 1-2/sec (casts)
- Attacks: 1-2/sec (target changes)

**Bandwidth: 70-140 bytes/sec per player**

Compare to per-tick input:
- 60 Hz x 14 bytes = 840 bytes/sec
- **Savings: 83-91%**

### CPU

**CommandDispatcher.Execute(): ~0.005ms**
- Dictionary lookup: ~0.001ms
- Handler execution: ~0.004ms

---

## Related Documentation

- **[01_GameSim_Overview.md](01_GameSim_Overview.md)** - Overall GameSim architecture
- **[03_Entity_Model.md](03_Entity_Model.md)** - Entity state components
- **[../Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)** - Broader architecture context

---

**Last Updated:** 2026-02-18

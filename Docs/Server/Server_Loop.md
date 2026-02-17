# Server Loop

**Authoritative Game Loop - Input Buffering, Tick Execution, Snapshot Broadcasting**

The server game loop is the heart of the authoritative simulation, running at 60Hz and processing inputs, advancing physics, and broadcasting snapshots to clients.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      SERVER GAME LOOP (60Hz)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Update() [60-120 FPS]                                          │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ForceIterateIncoming()                                    │   │
│  │ → Network thread enqueues inputs to ConcurrentQueue      │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
│  FixedUpdate() [60Hz / 16.67ms]                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ 1. DrainInputQueueForTick()                               │   │
│  │    → Drain ConcurrentQueue into _pendingNextTick          │   │
│  │    → Last-input-wins per client (highest Seq)             │   │
│  │                                                            │   │
│  │ 2. FlushPendingMovementInputs()                           │   │
│  │    → Apply buffered inputs at tick start                  │   │
│  │    → ApplyMovementInput() modifies SimWorld state         │   │
│  │                                                            │   │
│  │ 3. SimWorld.Step()                                        │   │
│  │    → Physics, movement, combat, abilities                 │   │
│  │    → Clock.Advance() increments tick                      │   │
│  │                                                            │   │
│  │ 4. TickWatchdog()                                         │   │
│  │    → Force stop stale movement (300ms timeout)            │   │
│  │                                                            │   │
│  │ 5. BroadcastSnapshots()                                   │   │
│  │    → AOI filtering per client                             │   │
│  │    → Send SnapshotDelta (unreliable UDP)                  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**File:** `Assets/Scripts/Server/ServerGameLoop.cs`

---

## Input Buffering System

### Design Philosophy

The server uses a **buffered input system** to ensure:
1. **Deterministic ordering** - All inputs received between tick N-1 and N are applied at tick N start
2. **Low latency** - Network polling in `Update()` at frame rate (60-120 FPS), not tick rate
3. **Last-input-wins** - Only the latest input per client (by `Seq`) is kept per tick

### V2 Architecture (Current)

**Key Changes from V1:**
- `ConcurrentQueue` instead of `ConcurrentDictionary` (thread-safe enqueue, deterministic dequeue)
- `ApplyTick` field for bounded latency (input received at tick T → applied at tick T+1)
- Per-tick input map (`_pendingNextTick`) instead of per-frame

**File References:**
- Input buffering types: `ServerGameLoop.cs:104-122`
- Buffer logic: `ServerGameLoop.cs:569-650`

---

## Input Lifecycle

### 1. Input Reception (Network Thread)

**Async Event Handlers:**

```csharp
// ServerGameLoop.cs:795-826
private void OnMoveDirReceived(int clientId, uint movementSeq, Vector2 direction, long recvSocketTicks)
{
    BufferMovementInput(clientId, movementSeq, PacketIntentType.MoveDir, direction, recvSocketTicks: recvSocketTicks);
}
```

**What happens:**
- FishNet network thread receives `InputPacket` from client
- Calls type-specific handler (`OnMoveDirReceived`, `OnMoveToReceived`, `OnStopReceived`, `OnFollowReceived`)
- Handler calls `BufferMovementInput()` which enqueues to `ConcurrentQueue`

**Thread-Safety:** Network callbacks can fire on any thread, so `ConcurrentQueue` is used for lock-free enqueue.

---

### 2. Input Buffering (Thread-Safe)

```csharp
// ServerGameLoop.cs:569-588
private void BufferMovementInput(int clientId, uint seq, PacketIntentType type, Vector2 payload = default, uint targetEntityId = 0, long recvSocketTicks = 0)
{
    uint currentTick = _simWorld?.Clock.CurrentTick ?? 0;

    var input = new PendingMovementInput
    {
        ClientId = clientId,
        Seq = seq,
        Type = type,
        Payload = payload,
        TargetEntityId = targetEntityId,
        ReceivedTime = Time.time,
        RecvServerTick = currentTick,
        ApplyTick = currentTick + 1,  // FIX: Bounded latency - apply next tick
        RecvStopwatchTicks = recvSocketTicks > 0 ? recvSocketTicks : Stopwatch.GetTimestamp()
    };

    _inputQueue.Enqueue(input);  // Thread-safe
}
```

**Key Fields:**
- `ApplyTick = RecvServerTick + 1` - Guarantees 1-tick delay (predictable, bounded)
- `RecvStopwatchTicks` - High-precision timestamp for latency metrics

---

### 3. Input Draining (Main Thread)

```csharp
// ServerGameLoop.cs:595-618
private void DrainInputQueueForTick(uint currentTick)
{
    // Drain all pending inputs from queue
    while (_inputQueue.TryDequeue(out var input))
    {
        // Last-input-wins per client: keep highest Seq
        if (_pendingNextTick.TryGetValue(input.ClientId, out var existing))
        {
            if (input.Seq > existing.Seq)
            {
                _pendingNextTick[input.ClientId] = input;
            }
            // else: discard older input
        }
        else
        {
            _pendingNextTick[input.ClientId] = input;
        }
    }
}
```

**Called from:** `FixedUpdate()` → `RunSimulation()` → `DrainInputQueueForTick()` (line 283)

**Purpose:**
- Drain `ConcurrentQueue` into deterministic `Dictionary`
- Apply "last-input-wins" rule per client (discard stale inputs)
- Prevents ConcurrentDictionary enumeration order issues

---

### 4. Input Application (Deterministic)

```csharp
// ServerGameLoop.cs:625-650
private void FlushPendingMovementInputs(uint currentTick)
{
    if (_pendingNextTick.Count == 0) return;

    int appliedCount = 0;

    foreach (var kvp in _pendingNextTick)
    {
        var input = kvp.Value;

        if (ApplyMovementInput(input, currentTick))
            appliedCount++;
    }

    // Clear for next tick
    _pendingNextTick.Clear();
}
```

**Called from:** `FixedUpdate()` → `RunSimulation()` → `FlushPendingMovementInputs()` (line 284)

**Purpose:**
- Apply all buffered inputs for the current tick
- Clear buffer after application (per-tick, not per-frame)

---

### 5. Movement Execution

```csharp
// ServerGameLoop.cs:657-686
private bool ApplyMovementInput(PendingMovementInput input, uint applyTick)
{
    var player = _simWorld.GetPlayerByClient(input.ClientId);
    if (player == null) return false;

    // Check monotonic sequence
    if (!CheckAndUpdateMovementSeq(input.ClientId, input.Seq))
        return false;

    // Calculate tick delta for diagnostics
    uint tickDelta = applyTick - input.RecvServerTick;

    switch (input.Type)
    {
        case PacketIntentType.MoveDir:
            return ApplyMoveDir(player, input, applyTick, tickDelta);
        // ... other cases
    }
}
```

**Intent Handlers:**
- `ApplyMoveDir()` - WASD keyboard input (normalized direction)
- `ApplyMoveTo()` - Click-to-move (target position)
- `ApplyStop()` - Stop movement
- `ApplyFollow()` - Follow target entity

**Example: MoveDir**

```csharp
// ServerGameLoop.cs:688-713
private bool ApplyMoveDir(SimPlayer player, PendingMovementInput input, uint applyTick, uint tickDelta)
{
    Vector3 dir3D = new Vector3(input.Payload.x, 0f, input.Payload.y);

    // Defensive normalization
    if (dir3D.sqrMagnitude > 1.01f)
        dir3D = dir3D.normalized;

    // Reject zero direction (client should send Stop instead)
    if (dir3D.sqrMagnitude < 0.01f)
    {
        Debug.LogWarning($"[ServerGameLoop] MoveDir with zero direction from client {input.ClientId} - ignoring");
        return false;
    }

    // METRIC A: Calculate intra-server delay (socket recv → apply)
    long applyStopwatchTicks = Stopwatch.GetTimestamp();
    double intraServerMs = (applyStopwatchTicks - input.RecvStopwatchTicks) * 1000.0 / Stopwatch.Frequency;

    player.SetMoveDirection(dir3D);  // Modifies SimPlayer state
    player.TimeSinceLastMoveCmd = 0f;
    return true;
}
```

**Server-Side Movement:**
- `player.SetMoveDirection(dir3D)` → `SimPlayer.cs:110`
- Physics executed in `SimWorld.Step()` → `MovementHandler.cs:45`

---

## Simulation Tick

### FixedUpdate Execution

```csharp
// ServerGameLoop.cs:225-232
private void FixedUpdate()
{
    if (!_isRunning) return;

    RunSimulation();
    TickWatchdog(Time.fixedDeltaTime);
    BroadcastSnapshots();
}
```

**Fixed timestep:** 60Hz (16.67ms) - configured in Unity Project Settings

---

### RunSimulation()

```csharp
// ServerGameLoop.cs:268-308
private void RunSimulation()
{
    // Accumulate time and run ticks
    int ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime);

    for (int i = 0; i < ticksToRun; i++)
    {
        uint currentTick = _simWorld.Clock.CurrentTick;

        // FIX #2 & #3: Drain queue into per-client map, then apply based on ApplyTick
        DrainInputQueueForTick(currentTick);
        FlushPendingMovementInputs(currentTick);

        // Step advances physics/state, then Clock.Advance()
        _simWorld.Step();

        uint tickAfter = _simWorld.Clock.CurrentTick;
    }

    // Update AOI positions after simulation
    if (_enableAOI)
    {
        foreach (var entity in _simWorld.AllEntities)
        {
            if (entity is SimPlayer player)
            {
                _aoiManager.UpdateEntityPosition(entity.Id, player.Transform.Position);
            }
        }
    }
}
```

**Order of operations:**
1. Input buffering (drain + flush)
2. Simulation step (physics, movement, combat)
3. AOI update (spatial grid for visibility culling)

**Why this order?**
- Inputs must be applied **before** physics to affect current tick
- AOI updates **after** simulation to reflect new positions

---

## Watchdog System

### Purpose

Prevents "ghost runs" when `Stop` packet is lost. If no input received for 300ms, force stop.

```csharp
// ServerGameLoop.cs:314-328
private void TickWatchdog(float dt)
{
    foreach (var player in _simWorld.AllPlayers)
    {
        player.TimeSinceLastMoveCmd += dt;

        // Use MoveDirection directly, not derived IsMoving state
        if (player.Transform.MoveDirection.sqrMagnitude > 0f &&
            player.TimeSinceLastMoveCmd > MOVE_WATCHDOG_TIMEOUT)
        {
            player.StopMoving();
        }
    }
}
```

**Timeout:** `MOVE_WATCHDOG_TIMEOUT = 0.3f` (300ms) - line 90

**Why needed?**
- Movement intents sent via unreliable UDP
- `Stop` packet might be lost
- Without watchdog, player continues moving indefinitely

---

## Snapshot Broadcasting

### Overview

```csharp
// ServerGameLoop.cs:330-425
private void BroadcastSnapshots()
{
    _snapshotAccumulator += Time.fixedDeltaTime;

    if (_snapshotAccumulator < _snapshotInterval) return;
    _snapshotAccumulator -= _snapshotInterval;

    // Send personalized snapshot to each client (with their ack seq)
    foreach (var player in _simWorld.AllPlayers)
    {
        int clientId = player.OwnerClientId;
        uint ackSeq = _commandBuffer.GetLastAckSeq(clientId);
        uint movementSeq = _lastMovementSeq.TryGetValue(clientId, out uint seq) ? seq : 0;

        SnapshotDelta snapshot;

        if (_enableAOI)
        {
            // AOI filtering (see AOI_System.md)
            var (entered, left) = _aoiManager.UpdateClientAOI(clientId, player.Transform.Position, _simWorld);
            // ... send enter/leave events
            var visible = _aoiManager.GetVisibleEntities(clientId);
            snapshot = SnapshotHelper.CreateFilteredSnapshot(_simWorld, visible, ackSeq, player.Id, movementSeq);
        }
        else
        {
            // No AOI - send all entities
            snapshot = SnapshotHelper.CreateSnapshot(_simWorld, clientId, ackSeq, movementSeq);
        }

        _netAdapter.SendToClient(clientId, snapshot, reliable: false);
    }
}
```

**Snapshot Rate:** 60Hz (configurable via `_snapshotRate` field, line 48)

**Personalized Snapshots:**
Each client receives a snapshot containing:
- `ServerTick` - Current authoritative tick
- `AckInputSeq` - Last event command sequence acknowledged
- `AckMovementSeq` - Last movement sequence acknowledged (for reconciliation)
- `EntityCount` + `Entities[]` - Filtered by AOI (if enabled)

---

### Movement Acknowledgment

```csharp
// ServerGameLoop.cs:357-365
uint movementSeq = _lastMovementSeq.TryGetValue(clientId, out uint seq) ? seq : 0;

if (movementSeq > 0)
{
    DebugLogger.LogThrottled(DebugLogger.Category.Network,
        $"[SERVER SNAPSHOT] to client {clientId} | ackMovementSeq={movementSeq} | tick={_simWorld.Clock.CurrentTick}",
        frameInterval: 60);
}
```

**Purpose:**
- Server tracks last applied `movementSeq` per client (line 93, 783)
- Includes `ackMovementSeq` in snapshots
- Client uses this for reconciliation (visual offset correction)

**Updated when:**
- `CheckAndUpdateMovementSeq()` called during `ApplyMovementInput()` (line 772)
- Monotonic sequence check ensures no out-of-order inputs

---

## Tick Rate Configuration

### Current Settings

| Parameter | Value | Location |
|-----------|-------|----------|
| **Server tick rate** | 60Hz (16.67ms) | Unity Project Settings → Time → Fixed Timestep |
| **Snapshot rate** | 60Hz | `_snapshotRate` field (line 48) |
| **Input poll rate** | 60-120 FPS | `Update()` calls `ForceIterateIncoming()` (line 219) |

**Why separate poll and tick rates?**
- **Input polling at frame rate (60-120 FPS):** Minimizes input latency, processes packets ASAP
- **Simulation at fixed 60Hz:** Deterministic physics, stable netcode
- **Snapshots at 60Hz:** Matches simulation, one snapshot per tick

---

### Changing Tick Rate

**To change server tick rate:**

1. **Unity Project Settings:**
   - Edit → Project Settings → Time
   - Set "Fixed Timestep" to `1 / desiredHz`
   - Example: 30Hz = `0.033333`, 60Hz = `0.0166667`, 120Hz = `0.00833333`

2. **SimConfig.cs:**
   ```csharp
   // Assets/GameSim/Core/SimConfig.cs
   public const int TICK_RATE = 60;  // Match Unity Fixed Timestep
   public const float FIXED_DELTA_TIME = 1f / TICK_RATE;
   ```

3. **NetcodeConstants.cs:**
   ```csharp
   // Assets/Scripts/Network/Shared/NetcodeConstants.cs
   public const int TICK_RATE = 60;  // Match SimConfig
   public const float SNAPSHOT_RATE = 60f;  // Usually matches tick rate
   ```

**Trade-offs:**
- **Higher tick rate (120Hz):** More responsive, higher CPU/bandwidth cost
- **Lower tick rate (30Hz):** Lower CPU/bandwidth, less responsive movement

---

## Performance Considerations

### Input Buffering Overhead

**V1 Issues (Resolved):**
- ❌ `ConcurrentDictionary` enumeration non-deterministic
- ❌ Inputs applied in next tick (2-tick delay)
- ❌ Last-input-wins per frame, not per tick

**V2 Improvements:**
- ✅ `ConcurrentQueue` for thread-safe enqueue, deterministic dequeue
- ✅ `ApplyTick` field for bounded 1-tick latency
- ✅ Last-input-wins per tick (cleared after `FlushPendingMovementInputs`)

### Network Polling Strategy

**Why `ForceIterateIncoming()` in `Update()`?**

```csharp
// ServerGameLoop.cs:213-220
private void Update()
{
    if (!_isRunning) return;

    // Process incoming packets at frame rate (not tick rate)
    _netAdapter.ForceIterateIncoming();
}
```

**Alternative (Naive):**
- Poll in `FixedUpdate()` → Waits up to 16.67ms for next tick
- Result: **2-tick delay** (input waits for tick boundary)

**Current (Optimized):**
- Poll in `Update()` → Processes immediately at 60-120 FPS
- Result: **1-tick delay** (input applied next tick via `ApplyTick`)

---

## Debugging

### Key Log Points

**Input Reception:**
```csharp
// Uncomment at ServerGameLoop.cs:797
Debug.Log($"[{Time.time:F3}] [SERVER RECV] MoveDir seq={movementSeq} dir=({direction.x:F2},{direction.y:F2}) (buffered)");
```

**Input Application:**
```csharp
// Uncomment at ServerGameLoop.cs:708
Debug.Log($"[{Time.time:F3}] [SERVER APPLY] MoveDir seq={input.Seq} dir=({input.Payload.x:F2},{input.Payload.y:F2}) recvTick={input.RecvServerTick} applyTick={applyTick} Δtick={tickDelta}");
```

**Tick Execution:**
```csharp
// Uncomment at ServerGameLoop.cs:292
Debug.Log($"[{Time.time:F3}] [SERVER TICK] simulated={currentTick} → now={tickAfter}");
```

**Snapshot Broadcast:**
```csharp
// Uncomment at ServerGameLoop.cs:342
Debug.Log($"[{Time.time:F3}] [SERVER BROADCAST] tick={_simWorld.Clock.CurrentTick} vel=({vel.x:F1},{vel.z:F1})");
```

### Movement Cycle Correlation

To trace input→simulation→snapshot for a single movement:

1. **Client sends input:** Look for `[CLIENT INPUT]` logs with `movementSeq=X`
2. **Server receives input:** Look for `[SERVER RECV]` with matching `seq=X`
3. **Server applies input:** Look for `[SERVER APPLY]` with matching `seq=X`, note `applyTick=Y`
4. **Server broadcasts snapshot:** Look for `[SERVER BROADCAST]` at `tick=Y`

**Expected latency:**
- `RecvTick → ApplyTick`: 1 tick (bounded by `ApplyTick = RecvTick + 1`)
- `ApplyTick → BroadcastTick`: 0-1 ticks (snapshot rate matches tick rate)

---

## Related Documentation

- **[AOI_System.md](AOI_System.md)** - Area of Interest visibility culling
- **[Network/03_Data_Flow.md](../Network/03_Data_Flow.md)** - Complete input→visual flow
- **[GameSim/GameSim_Overview.md](../GameSim/GameSim_Overview.md)** - Simulation layer architecture

---

**Last Updated:** 2026-02-03
**Version:** V2 (Input Buffering with bounded latency)

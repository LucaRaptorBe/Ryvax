# Data Flow

> **Status:** Production
> **Version:** 5.0 (Pure LoL-style Netcode)
> **Last Updated:** 2026-02-18

## Overview

This document traces the **complete input-to-visual flow** through the Ryvax netcode pipeline, from keyboard press to pixel on screen.

**Architecture:** Server-authoritative, no client-side prediction
**Style:** League of Legends (position from server, rotation/animation from local intent)
**Latency:** ~50ms localhost, ~66-100ms typical online

---

## Flow Diagram Summary

```
┌─────────────────────────────────────────────────────────────┐
│                         CLIENT INPUT                        │
├─────────────────────────────────────────────────────────────┤
│  Keyboard Press                                             │
│      ↓ (0ms)                                                │
│  InputCollector.Update()    ── Samples input at frame rate  │
│      ↓ (0ms)                                                │
│  IntentBuilder.OnKeyboardMove() ── Rate-limit 120Hz         │
│      ↓ (0ms)                                                │
│  NetworkClient.SendInputIntent() ── Queue intent            │
│      ↓ (0ms)                                                │
│  NetworkClient.SendInputUpdate() ── Create InputPacket      │
│      ↓ (0ms)                                                │
│  FishNetAdapter.SendInputPacket() ── Broadcast (UDP)        │
│      ↓ (0ms)                                                │
│  ForceIterateOutgoing() ── Double-flush to socket           │
│      ↓ (0ms client-side total)                              │
└─────────────────────────────────────────────────────────────┘
                             │
                             │ UDP Packet (~1-50ms network)
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                          SERVER                             │
├─────────────────────────────────────────────────────────────┤
│  ForceIterateIncoming() ── Poll transport (Update)          │
│      ↓ (+17ms from input)                                   │
│  FishNetAdapter.HandleServerReceiveInputPacket()            │
│      ↓ (0ms)                                                │
│  ServerGameLoop.OnMoveDirReceived() ── Buffer input         │
│      ↓ (0ms)                                                │
│  FixedUpdate START → FlushPendingMovementInputs()           │
│      ↓ (0ms - applied immediately)                          │
│  SimWorld.Step() ── Apply input to simulation               │
│      ↓ (0ms)                                                │
│  MovementHandler.Execute() ── Update position/velocity      │
│      ↓ (+16ms for next tick)                                │
│  ServerGameLoop.BroadcastSnapshots() ── Create snapshot     │
│      ↓ (0ms)                                                │
│  SnapshotHelper.CreateSnapshot() ── Serialize entities      │
│      ↓ (0ms)                                                │
│  FishNetAdapter.SendToAll() ── Broadcast (UDP)              │
│      ↓ (+33ms from input total)                             │
└─────────────────────────────────────────────────────────────┘
                             │
                             │ UDP Packet (~1-50ms network)
                             ▼
┌─────────────────────────────────────────────────────────────┐
│                    CLIENT RECEIVE                           │
├─────────────────────────────────────────────────────────────┤
│  FishNetAdapter.HandleClientReceiveSnapshot()               │
│      ↓ (+50ms from input)                                   │
│  NetworkClient.OnSnapshotReceived()                         │
│      ↓ (0ms)                                                │
│  PlayerView.OnSnapshotReceived() ── Store pos/vel/rot       │
│      ↓ (every frame in Update)                              │
│  EntityView.UpdatePosition() ── Dead-reckoning              │
│      ↓ (0ms)                                                │
│  extrapolate: visualPos = serverPos + serverVel × Δt        │
│      ↓ (0ms)                                                │
│  Snap smoothing: lerp towards dead-reckoned pos             │
│      ↓ (0ms)                                                │
│  transform.position = visualPos                             │
│      ↓ (0ms)                                                │
│  Unity Render ── Pixels on screen!                          │
└─────────────────────────────────────────────────────────────┘
```

**Total Latency:** ~50ms (localhost), ~66-100ms (typical online)

---

## Stage-by-Stage Breakdown

### 1. Input Capture (Client)

**File:** `Assets/Scripts/Client/Input/InputCollector.cs:110-136`

**Trigger:** Unity `Update()` - every frame (~16ms at 60fps, ~8ms at 120fps)

```csharp
void Update()
{
    HandleMovementInput();  // Line 126
}

private void HandleMovementInput()
{
    // Propagate immobilize lock to IntentBuilder
    bool isImmobilized = _networkClient.IsImmobilized();
    _intentBuilder.SetImmobilizeLock(isImmobilized);

    InputIntent? intent = null;

    switch (GetMovementMode())
    {
        case MovementMode.WASD:
            intent = HandleWASDInput();  // Line 228
            break;
        case MovementMode.ClickToMove:
            intent = HandleClickToMoveInput();
            break;
    }

    if (intent.HasValue)
        _networkClient.SendInputIntent(intent.Value);
}

private InputIntent? HandleWASDInput()
{
    // Sample keyboard direction (WASD or ZQSD depending on layout)
    Vector2 dir = Vector2.zero;
    if (kb.wKey.isPressed) dir.y += 1;
    if (kb.sKey.isPressed) dir.y -= 1;
    if (kb.dKey.isPressed) dir.x += 1;
    if (kb.aKey.isPressed) dir.x -= 1;

    // V3.0: Set local move intent (no-op in V5.0 - kept for API compatibility)
    if (dir.sqrMagnitude > 0.01f)
        _networkClient.SetLocalMoveIntent(new Vector3(dir.x, 0f, dir.y));
    else
        _networkClient.ClearLocalMoveIntent();

    // Build rate-limited MoveDir intent
    uint clientTick = _networkClient.GetCurrentTick();
    return _intentBuilder.OnKeyboardMove(dir, Time.deltaTime, clientTick);
}
```

**Key Points:**

- **`SetLocalMoveIntent` is a no-op in V5.0** - rotation/animation come from server velocity
- **No local position change** - position comes 100% from server
- Input sampled at frame rate (60-120 fps), but rate-limited by IntentBuilder to 120Hz

**Output:** `InputIntent` (if rate-limit passes)

---

### 2. Intent Building (Client)

**File:** `Assets/Scripts/Client/Input/IntentBuilder.cs:37-158`

**Rate Limit:** 120Hz (8.3ms interval)

```csharp
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz - matches _inputSendRate

public InputIntent OnKeyboardMove(Vector2 dir, float dt, uint clientTick)
{
    _accumulator += dt;

    // Rate limit: only send intent every 8.3ms
    if (_accumulator < INTENT_SEND_INTERVAL)
        return null;

    _accumulator -= INTENT_SEND_INTERVAL;

    // Check immobilize lock (CC prevents movement)
    if (_isImmobilizeLocked)
        return InputIntent.Stop(++_seqId, clientTick);

    // Create MoveDir intent
    if (dir.sqrMagnitude > 0.01f)
    {
        return InputIntent.MoveDir(dir.normalized, ++_seqId, clientTick);
    }

    return null;  // No movement
}
```

**Key Points:**

- **Rate limiting** prevents spam (caps at 120 intents/sec)
- **Sequence ID** incremented per intent (for dedup)
- **Immobilize lock** forces Stop intent if CC active

**Output:** `InputIntent.MoveDir(direction, seqId, tick)` or `null`

---

### 3. Network Queuing (Client)

**File:** `Assets/Scripts/Core/NetworkClient.cs:256-287`

```csharp
public void SendInputIntent(InputIntent intent)
{
    _pendingIntent = intent;  // Queue for next send
}

void Update()
{
    SendInputUpdate();  // Called every frame
}

private void SendInputUpdate()
{
    _inputSendAccumulator += Time.deltaTime;

    // Rate limit: 120Hz
    if (_inputSendAccumulator < _inputSendInterval)
        return;

    _inputSendAccumulator -= _inputSendInterval;

    // Create InputPacket from pending intent
    InputPacket packet = CreateInputPacketFromIntent(_pendingIntent, _eventCommands);

    // Send via adapter (UDP unreliable)
    _netAdapter.SendInputPacket(packet);  // Line 319

    _pendingIntent = null;
}
```

**Key Points:**

- **Pending intent** held until next send interval
- **Event commands** bundled with movement intent
- **120Hz send rate** ensures low latency

**Output:** `InputPacket` (14-56 bytes)

---

### 4. Packet Creation & Quantization (Client)

**File:** `Assets/Scripts/Core/NetworkClient.cs:323-359`

```csharp
private InputPacket CreateInputPacketFromIntent(InputIntent intent, GameCommand[] events)
{
    switch (intent.Type)
    {
        case InputIntentType.MoveDir:
            return InputPacket.CreateMoveDir(
                clientTick: _currentTick,
                movementSeq: intent.SeqId,
                direction: intent.Direction,  // Normalized Vector2
                eventCommands: events
            );

        case InputIntentType.MoveTo:
            return InputPacket.CreateMoveTo(
                clientTick: _currentTick,
                movementSeq: intent.SeqId,
                targetPos: intent.TargetPosition,
                eventCommands: events
            );

        case InputIntentType.Stop:
            return InputPacket.CreateStop(
                clientTick: _currentTick,
                movementSeq: intent.SeqId,
                eventCommands: events
            );
    }
}
```

**Inside InputPacket.CreateMoveDir():**

```csharp
return new InputPacket
{
    ClientTick = clientTick,
    MovementSeq = movementSeq,
    IntentType = PacketIntentType.MoveDir,
    Payload0 = GameCommand.QuantizeDirection(direction.x),  // ×127
    Payload1 = GameCommand.QuantizeDirection(direction.y),  // ×127
    CommandCount = (byte)(eventCommands?.Length ?? 0),
    Commands = eventCommands ?? Array.Empty<GameCommand>()
};
```

**Quantization:** `direction (1.0, 0.0)` → `Payload0 = 127, Payload1 = 0`

**Output:** `InputPacket` ready for serialization

---

### 5. Transport Send (Client)

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:237-267`

```csharp
public void SendInputPacket(InputPacket packet)
{
    var broadcast = new InputPacketBroadcast { Packet = packet };

    // ALWAYS use Unreliable (UDP) to avoid head-of-line blocking
    _networkManager.ClientManager.Broadcast(broadcast, Channel.Unreliable);

    // Packet is queued in PacketBundle, not sent yet
}
```

**At this point:** Packet is in FishNet's internal `PacketBundle`, **not on wire yet**.

---

### 6. Double-Flush Mechanism (Client)

**File:** `Assets/Scripts/Core/NetworkClient.cs:265-269`

```csharp
void LateUpdate()
{
    // Force flush packets to socket (same frame as creation)
    _netAdapter.ForceIterateOutgoing();
}
```

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:592-625`

```csharp
public void ForceIterateOutgoing()
{
    if (_networkManager.IsClientStarted)
    {
        // STEP 1: Flush PacketBundle → Transport queue
        _networkManager.TransportManager.IterateOutgoing(asServer: false);

        // STEP 2: Flush Transport queue → OS socket
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
    }
}
```

**Result:** Packet sent to network within **same frame** (0ms client-side delay).

**Without double-flush:** Packet waits for next FishNet tick (~33ms at 30Hz).

---

### 7. Network Transit (UDP)

**Latency:** 1-50ms depending on network conditions

- **Localhost:** ~1ms
- **LAN:** ~1-5ms
- **Internet (same region):** ~20-50ms
- **Internet (cross-region):** 100-200ms

**Packet can be lost** (UDP unreliable), but redundancy in next packet recovers.

---

### 8. Server Receive (Server)

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:229-236` (Update — every frame)

```csharp
// Update() polls at frame rate (60-120 fps) — decoupled from tick rate.
// This eliminates the 33ms delay from waiting for the next FixedUpdate.
private void Update()
{
    if (!_isRunning) return;

    // Process incoming packets at frame rate (not tick rate)
    _netAdapter.ForceIterateIncoming();
}
```

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:570-585`

```csharp
public void ForceIterateIncoming()
{
    if (_networkManager.IsServerStarted)
        _networkManager.TransportManager.Transport.IterateIncoming(asServer: true);
}
```

**Result:** Packet delivered to FishNet's receive queue, triggers handler.

---

### 9. Input Decoding & Buffering (Server)

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:439-483`

```csharp
private void HandleServerReceiveInputPacket(NetworkConnection conn, InputPacketBroadcast broadcast, Channel channel)
{
    var packet = broadcast.Packet;
    int clientId = conn.ClientId;

    // METRIC: Capture monotonic timestamp (earliest point)
    long recvSocketTicks = System.Diagnostics.Stopwatch.GetTimestamp();

    // Version check
    if (packet.Version < InputPacket.MSG_VERSION)
    {
        Debug.LogWarning($"Rejected old packet v{packet.Version}");
        return;
    }

    // Dispatch by intent type (invoke buffering events)
    switch (packet.IntentType)
    {
        case PacketIntentType.MoveDir:
            OnMoveDirReceived?.Invoke(clientId, packet.MovementSeq, packet.GetDirection(), recvSocketTicks);
            break;

        case PacketIntentType.MoveTo:
            OnMoveToReceived?.Invoke(clientId, packet.MovementSeq, packet.GetTargetPosition(), recvSocketTicks);
            break;

        case PacketIntentType.Stop:
            OnStopReceived?.Invoke(clientId, packet.MovementSeq, recvSocketTicks);
            break;
    }

    // Process event commands (with dedup)...
}
```

**Dequantization:** `Payload0 = 127` → `direction.x = 127 / 127.0 = 1.0`

**Output:** Event invoked with `(clientId, seq, direction, timestamp)`

---

### 10. Input Buffering (Server)

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:234-260`

**Problem:** Inputs arrive asynchronously (during Update, network callbacks). If applied immediately, they can miss the current simulation tick.

**Solution:** Buffer inputs, apply at START of FixedUpdate.

```csharp
// Subscribed to FishNetAdapter events
void OnMoveDirReceived(int clientId, uint seq, Vector2 dir, long timestamp)
{
    // Buffer input (don't apply yet)
    _pendingMovementInputs[clientId] = new PendingMovementInput
    {
        Type = MovementInputType.MoveDir,
        Sequence = seq,
        Direction = new Vector3(dir.x, 0, dir.y),
        RecvTimestamp = timestamp
    };
}
```

**Key Point:** Input is **stored**, not applied. Application happens in FixedUpdate START.

---

### 11. Input Application (Server)

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:241-249` (FixedUpdate)

```csharp
// Update() (every frame): ForceIterateIncoming() — see Stage 8
// FixedUpdate() (tick rate): simulation only

private void FixedUpdate()
{
    if (!_isRunning) return;

    RunSimulation();       // DrainInputQueue → FlushPending → Step
    TickWatchdog(Time.fixedDeltaTime);
    TickRespawns();
    BroadcastSnapshots();
}

private void FlushPendingMovementInputs()
{
    foreach (var kvp in _pendingMovementInputs)
    {
        int clientId = kvp.Key;
        var input = kvp.Value;

        // Validate sequence (reject out-of-order)
        if (input.Sequence <= _lastMovementSeq[clientId])
            continue;

        _lastMovementSeq[clientId] = input.Sequence;

        // Get player entity
        var player = GetPlayerEntity(clientId);
        if (player == null) continue;

        // Apply input based on type
        switch (input.Type)
        {
            case MovementInputType.MoveDir:
                player.SetMoveDirection(input.Direction);  // Apply direction
                break;

            case MovementInputType.Stop:
                player.SetMoveDirection(Vector3.zero);     // Stop
                break;
        }
    }

    _pendingMovementInputs.Clear();  // Clear buffer for next frame
}
```

**Key Points:**

- **Last-input-wins:** Only highest sequence per client is applied
- **Out-of-order rejection:** Late packets are ignored
- **Validation:** Direction normalized/clamped defensively

**Output:** Player's internal state updated (direction set)

---

### 12. Simulation Tick (Server)

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:286-323`

```csharp
private void RunSimulation()
{
    int ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime);

    for (int i = 0; i < ticksToRun; i++)
    {
        uint currentTick = _simWorld.Clock.CurrentTick;

        DrainInputQueueForTick(currentTick);      // Pull from ConcurrentQueue
        FlushPendingMovementInputs(currentTick);  // Apply to SimPlayers

        _simWorld.Step();  // Advance physics/state, then Clock.Advance()

        DrainAndBroadcastSimEvents();  // Projectile hits, deaths, etc.
    }
}
```

**File:** `Assets/GameSim/Core/SimWorld.cs` — method `Step()` (not `Tick()`)

```csharp
public void Step()
{
    // Advances physics/state, then increments clock
}
```

**File:** `Assets/GameSim/Commands/Handlers/MovementHandler.cs:26-58`

```csharp
public void Execute(SimWorld world, SimCommand cmd, SimEntity entity)
{
    if (entity is SimPlayer player)
    {
        // Use player's current direction (set by SetMoveDirection)
        Vector3 direction = player.GetMoveDirection();

        // Calculate velocity
        float speed = world.Config.PlayerMoveSpeed * player.Stats.MoveSpeedModifier;
        Vector3 velocity = direction * speed;

        // Update position
        player.Transform.Velocity = velocity;
        player.Transform.Position += velocity * world.Clock.DeltaTime;

        // Rotation follows velocity
        if (velocity.sqrMagnitude > 0.01f)
        {
            float rotY = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
            player.Transform.RotationY = rotY;
        }
    }
}
```

**Key Points:**

- **Position updated** based on velocity × deltaTime
- **Velocity calculated** from direction × speed
- **Rotation follows** movement direction

**Example:**

```
Before: pos = (0, 0, 0), velocity = (0, 0, 0)
Input:  direction = (1, 0, 0)  (right)
After:  velocity = (8, 0, 0)   (8 u/s right)
        pos = (0.133, 0, 0)    (0.133 units at 60Hz tick)
        rotY = 90°              (facing right)
```

---

### 13. Snapshot Creation (Server)

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:345-440`

```csharp
private void BroadcastSnapshots()
{
    _snapshotAccumulator += Time.fixedDeltaTime;

    if (_snapshotAccumulator < _snapshotInterval) return;
    _snapshotAccumulator -= _snapshotInterval;

    // Iterate SimWorld.AllPlayers — not a GetConnectedClients() call
    foreach (var player in _simWorld.AllPlayers)
    {
        int clientId = player.OwnerClientId;
        uint ackSeq = _commandBuffer.GetLastAckSeq(clientId);
        uint movementSeq = _lastMovementSeq.TryGetValue(clientId, out uint seq) ? seq : 0;

        SnapshotDelta snapshot;
        if (_enableAOI)
        {
            // AOI: filtered to entities visible by this client
            var visible = _aoiManager.GetVisibleEntities(clientId);
            snapshot = SnapshotHelper.CreateFilteredSnapshot(
                _simWorld, visible, ackSeq, player.Id, movementSeq);
        }
        else
        {
            // No AOI: all entities
            snapshot = SnapshotHelper.CreateSnapshot(
                _simWorld, clientId, ackSeq, movementSeq);
        }

        _netAdapter.SendToClient(clientId, snapshot, reliable: false);
    }
}
```

**File:** `Assets/Scripts/Network/NetAdapter/SnapshotHelper.cs:22-40`

```csharp
public static SnapshotDelta CreateSnapshot(SimWorld world, int forClientId, uint ackInputSeq, uint ackMovementSeq)
{
    var entities = new List<EntityState>();

    foreach (var entity in world.AllEntities)
    {
        entities.Add(EntityStateFromSimEntity(entity, world.Config.PlayerMoveSpeed));
    }

    return new SnapshotDelta
    {
        ServerTick = world.Clock.CurrentTick,
        AckInputSeq = ackInputSeq,
        AckMovementSeq = ackMovementSeq,
        EntityCount = (ushort)entities.Count,
        Entities = entities.ToArray()
    };
}
```

**Quantization:** `position (0.133, 0, 0)` → 16-bit normalized values

**Output:** `SnapshotDelta` (40-300 bytes depending on entity count)

---

### 14. Snapshot Broadcast (Server)

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:269-303`

```csharp
public void SendToClient<T>(int clientId, T message, bool reliable)
{
    var conn = GetConnection(clientId);

    if (message is SnapshotDelta snapshot)
    {
        var broadcast = new SnapshotBroadcast { Snapshot = snapshot };
        _networkManager.ServerManager.Broadcast(conn, broadcast, false, Channel.Unreliable);
    }
}
```

**Key Points:**

- **Unreliable channel** (UDP) - packet loss acceptable
- **Per-client broadcast** - each client gets personalized ACKs
- **60Hz rate** - new snapshot every ~16ms

---

### 15. Client Snapshot Receive (Client)

**File:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:527-538`

```csharp
private void HandleClientReceiveSnapshot(SnapshotBroadcast broadcast, Channel channel)
{
    if (_handlers.TryGetValue(typeof(SnapshotDelta), out var handler))
    {
        ((Action<int, SnapshotDelta>)handler)(-1, broadcast.Snapshot);
    }
}
```

**File:** `Assets/Scripts/Core/NetworkClient.cs:351-380`

```csharp
private void OnSnapshotReceived(int _, SnapshotDelta snapshot)
{
    float serverTime = TickClock.TickToTime(snapshot.ServerTick);

    for (int i = 0; i < snapshot.EntityCount; i++)
    {
        ref readonly var state = ref snapshot.Entities[i];

        if (_playerViews.TryGetValue(state.EntityId, out var view))
        {
            // All players (local and remote) use the same unified path
            view.OnSnapshotReceived(state.Position, state.Velocity, state.Rotation);

            // Update SimPlayer state in client-side SimWorld
            var simPlayer = _simWorld.GetEntity(state.EntityId) as SimPlayer;
            if (simPlayer != null)
                simPlayer.ApplyNetworkState(state.Position, state.Velocity, state.Rotation, state);
        }
    }

    _eventBuffer.AcknowledgeUpTo(snapshot.AckInputSeq);
}
```

**Key Points:**

- **No `SnapshotState`, no `_timeSync`, no `_visualPositionManager`** — these do not exist in V5.0
- **All entities processed** (local and remote) through the same unified path
- **Dequantization** already applied inside `EntityState.Position/Velocity/Rotation` getters
- Position is forwarded directly to `EntityView.OnSnapshotReceived()` for dead-reckoning

**Output:** `PlayerView.OnSnapshotReceived(position, velocity, rotationY)` called per entity

---

### 16. Dead-Reckoning State Store (Client)

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:136-148`

```csharp
// Called for every entity in every snapshot (local and remote)
public virtual void OnSnapshotReceived(Vector3 position, Vector3 velocity, float rotationY)
{
    // Initialize visual position on first snapshot (no smoothing from origin)
    if (!_hasSnapshot)
        _visualPos = position;

    _lastServerPos = position;
    _lastServerVel = velocity;
    _lastServerRotY = rotationY;
    _lastSnapshotTime = Time.time;
    _hasSnapshot = true;
}
```

**Key Points:**

- Stores the three values needed for dead-reckoning: `position`, `velocity`, `rotationY`
- First snapshot initializes `_visualPos` directly (no lerp from world origin)
- No interpolation buffer, no ring buffer, no time-sync

---

### 17. Frame Update Loop (Client)

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:189-224`

```csharp
protected virtual void Update()
{
    if (!_isInitialized) return;

    UpdatePosition();   // Dead-reckoning + snap smoothing
    UpdateAnimation();  // Derive from server velocity
}
```

**`UpdatePosition()` is the core rendering step** — see Stage 18.

---

### 18. Dead-Reckoning & Snap Smoothing (Client)

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:197-224`

```csharp
protected virtual void UpdatePosition()
{
    if (!_hasSnapshot) return;

    // Dead-reckoning: extrapolate from last known server position
    float dt = Time.time - _lastSnapshotTime;
    Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

    // Snap smoothing: lerp towards dead-reckoned position
    // BUT snap immediately when stopped to avoid sliding
    if (_lastServerVel.sqrMagnitude < 0.01f)
    {
        // Stopped: snap directly to server position
        _visualPos = deadReckonedPos;
    }
    else
    {
        // Moving: smooth towards dead-reckoned position
        _visualPos = Vector3.Lerp(_visualPos, deadReckonedPos,
            NetcodeConstants.VISUAL_SMOOTHING_SPEED * Time.deltaTime);
    }

    transform.position = _visualPos;
    transform.rotation = Quaternion.Euler(0, _lastServerRotY, 0);
}
```

**Example:**

```
Snapshot received: serverPos=(0,0,0), serverVel=(8,0,0), rotY=90°, time=1.000s

Frame at t=1.008s (8ms later):
  dt = 1.008 - 1.000 = 0.008s
  deadReckonedPos = (0,0,0) + (8,0,0) × 0.008 = (0.064, 0, 0)
  _visualPos = Lerp(_visualPos, (0.064,0,0), smoothingSpeed × 0.008)
```

**Key Points:**

- **No interpolation buffer** — no snapshots are queued; only the latest is retained
- **Dead-reckoning** bridges the gap between snapshots (60Hz = 16ms between)
- **Snap when stopped** prevents sliding after the player releases keys
- **Rotation** applied directly from server (`_lastServerRotY`) — no local intent in V5.0

---

### 19. Animation Update (Client)

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:234-245`

```csharp
protected virtual void UpdateAnimation()
{
    if (_animator == null) return;

    _animator.SetInteger(StateHash, _currentState);

    // Derive speed from server velocity — no local intent used
    float speed = _lastServerVel.magnitude;
    _animator.SetFloat(SpeedHash, speed);

    bool isGrounded = transform.position.y <= 0.1f;
    _animator.SetBool(IsGroundedHash, isGrounded);
}
```

**Key Points:**

- **Speed derived from `_lastServerVel.magnitude`** — not from local intent
- **`HasLocalMoveIntent()` / `GetLocalMoveIntent()` do not exist** in V5.0 `NetworkClient`. `SetLocalMoveIntent()` and `ClearLocalMoveIntent()` exist but are explicit no-ops (NetworkClient.cs:122-127)
- Animation lags ~16ms behind input (one snapshot interval) — acceptable for MOBA

---

### 20. Visual Rendering (Client)

**File:** `Assets/Scripts/Client/View/Entities/EntityView.cs:189-224`

The full per-frame rendering path for all entities (local and remote) is identical in V5.0:

```
Every Update():
  1. UpdatePosition()
       deadReckonedPos = _lastServerPos + _lastServerVel × (Time.time - _lastSnapshotTime)
       _visualPos = Lerp or snap towards deadReckonedPos
       transform.position = _visualPos
       transform.rotation = Euler(0, _lastServerRotY, 0)

  2. UpdateAnimation()
       speed = _lastServerVel.magnitude
       animator.SetFloat("Speed", speed)
       animator.SetInteger("State", _currentState)
```

**Visual Position Access:**

```csharp
// In NetworkClient.cs:101
public Vector3 GetVisualPosition()
    => _localPlayerView?.GetVisualPosition() ?? Vector3.zero;

// In EntityView.cs:229
public Vector3 GetVisualPosition() => _visualPos;
```

**Key Points:**

- **Position:** 100% from server (dead-reckoned from last snapshot)
- **Rotation:** From server (`_lastServerRotY`) — no local-intent rotation in V5.0
- **Animation:** From server velocity magnitude — no local-intent animation in V5.0
- **Same code path for local and remote** — no special-casing

---

## V5.0 Pure LoL-Style Specifics

### What is "Pure LoL-Style"?

**Definition:** Client position comes **100% from server snapshots** (dead-reckoned between arrivals), with **no local intent feedback** for rotation or animation in V5.0.

**No Client-Side Prediction:** Unlike FPS games (e.g., Valorant, CS:GO), the client does NOT move its character locally and reconcile with server. This eliminates rubber-banding but adds ~50ms perceived latency for position.

**V5.0 Rendering (Dead-Reckoning):**

- **Position:** Dead-reckoned from last server snapshot (`serverPos + serverVel × dt`)
- **Rotation:** From server `rotationY` field in snapshot
- **Animation:** Derived from server velocity magnitude
- **Local intent (`SetLocalMoveIntent`) is a no-op** — kept for API compatibility only

**Why This Works for MOBA:**

1. **Movement is predictable** — no complex physics, just direction × speed
2. **Dead-reckoning** provides smooth visuals between 60Hz snapshots
3. **50ms latency** is acceptable for strategic gameplay (vs twitch shooter)
4. **Zero rubber-banding** — what you see is ground truth

### Comparison: Prediction vs Pure LoL

| Aspect | Client Prediction (FPS) | Pure LoL V5.0 (MOBA) |
|--------|-------------------------|----------------------|
| **Position** | Local prediction + reconciliation | Server dead-reckoning |
| **Rotation** | Server or local | Server (from snapshot) |
| **Animation** | Server or local | Server velocity magnitude |
| **Latency (perceived)** | 0ms (position) | ~50ms (position) |
| **Rubber-banding** | Frequent (on misprediction) | Never |
| **Divergence Risk** | High (walls, CC, knockback) | Zero |
| **Complexity** | High (rollback, reconciliation) | Low (dead-reckoning only) |

### Dead-Reckoning Flow

```
Frame 0: Snapshot arrives (serverPos, serverVel, rotY)
    ↓
    _lastServerPos = serverPos
    _lastServerVel = serverVel        ◄── Stored for extrapolation
    _lastServerRotY = rotY
    _lastSnapshotTime = Time.time

Frame 1 (~8ms later, no new snapshot yet):
    ↓
    dt = Time.time - _lastSnapshotTime = 0.008s
    deadReckonedPos = serverPos + serverVel × 0.008
    _visualPos = Lerp(_visualPos, deadReckonedPos, smoothing)
    transform.position = _visualPos   ◄── SMOOTH (no discrete jumps)
    transform.rotation = Euler(0, rotY, 0)
    speed = serverVel.magnitude
    animator.SetFloat("Speed", speed)

Frame ~4 (after 16ms): Next snapshot arrives
    ↓
    New serverPos/serverVel stored, dead-reckoning restarts
```

**Result:** Smooth visual movement between 60Hz snapshots with zero client-side prediction complexity.

### Discontinuity Handling

**Problem:** Respawn creates a position jump that must not be smoothed over.

**Solution:** `EntityView.Teleport()` snaps `_visualPos` directly and resets dead-reckoning state.

```csharp
// EntityView.cs:172-183
public virtual void Teleport(Vector3 position, float rotationY)
{
    transform.position = position;
    _lastServerPos = position;
    _lastServerVel = Vector3.zero;
    _lastServerRotY = rotationY;
    _lastSnapshotTime = Time.time;
    _visualPos = position;  // No lerp from old position
    _hasSnapshot = true;
}
```

**Without teleport snap:** Client would lerp smoothly from death position to respawn position (looks wrong).

**With teleport snap:** Client jumps instantly to new position (matches visual expectation).

---

## Timing Metrics (Localhost, 60Hz Server)

### Complete Timeline (Measured)

| Event | Timestamp | Cumulative | Delta |
|-------|-----------|------------|-------|
| **Client Input** | | | |
| INPUT START | 1.283s | 0ms | - |
| INTENT (rate-limited 120Hz) | 1.283s | 0ms | 0ms |
| SEND ENQUEUE | 1.283s | 0ms | 0ms |
| SOCKET SEND (double-flush) | 1.283s | 0ms | 0ms |
| **Network** | | | |
| (UDP transit) | - | - | ~1ms |
| **Server** | | | |
| SERVER RECV | 1.300s | +17ms | 17ms |
| SERVER APPLY | 1.300s | +17ms | 0ms |
| SERVER TICK | 1.300s | +17ms | 0ms |
| SERVER BROADCAST | 1.316s | +33ms | 16ms |
| **Network** | | | |
| (UDP transit) | - | - | ~1ms |
| **Client Visual** | | | |
| SNAPSHOT RECV | 1.333s | +50ms | 17ms |
| Interpolation | 1.333s | +50ms | 0ms |
| Render | 1.333s | +50ms | 0ms |

**Total: 50ms** (input press to visual position update)

### Breakdown by Stage

```
Client-side:     0ms  ( 0%) ✅ Fully optimized
Network (up):   ~1ms  ( 2%)
Server receive: 17ms  (34%) ✅ Already optimized (Update polling)
Server tick:    16ms  (32%) ⚠️ Inherent at 60Hz
Network (down): ~1ms  ( 2%)
Client receive: 15ms  (30%) Frame interval (next Update)
────────────────────────────
Total:          50ms (100%)
```

### Where Time Is Spent

1. **Server receive (17ms):** Worst-case frame interval (polling in `Update()` at ~60fps). Already optimized — no further reduction without higher frame rate.
2. **Server tick (16ms):** Inherent at 60Hz. To reduce, increase tick rate to 120Hz (halves to 8ms).
3. **Client receive (15ms):** Frame interval until next `Update()` processes the snapshot. No interpolation buffer in V5.0.

---

## Optimizations Applied

### Client-Side

1. **120Hz Input Rate:** Intent created every 8.3ms (vs 100ms in v1.0)
2. **120Hz Send Rate:** Packets sent every 8.3ms (vs 33ms in v1.0)
3. **Double-Flush:** Packets sent same frame (0ms delay vs 16-33ms in v1.0)
4. **No local intent feedback in V5.0** — rotation/animation derived from server velocity

**Result:** 0ms client-side delay for packet creation (was ~50-100ms in v1.0)

### Server-Side

1. **Manual Incoming Poll:** Polls in Update() at frame rate (~120fps), decoupled from tick rate
2. **Input Buffering:** ConcurrentQueue ensures thread-safe enqueue; applied at FixedUpdate tick boundary
3. **60Hz Tick Rate:** Reduced from 30Hz (16ms tick vs 33ms)

**Result:** Consistent 16-17ms server processing (was 33-66ms in v1.0)

### Network

1. **UDP Unreliable:** Avoids TCP head-of-line blocking
2. **Redundancy:** Last 3 commands in each packet (tolerates loss)
3. **Quantization:** 14-56 byte packets (vs 100+ bytes uncompressed)

**Result:** Minimal bandwidth, no blocking delays

---

## Debugging Tools

### Enable Detailed Logging

**Conditional Compilation:** Logs active in `UNITY_EDITOR` and `DEVELOPMENT_BUILD`.

**Key Log Points:**

```csharp
// Client
Debug.Log($"[{Time.time:F3}] [INPUT START] dir={dir} speed={speed}");
Debug.Log($"[{Time.time:F3}] [INTENT] +{delta}ms {intentType} seq={seq}");
Debug.Log($"[{Time.time:F3}] [SEND ENQUEUE] C→S seq={seq}");
Debug.Log($"[{Time.time:F3}] [SOCKET SEND] C→S size={bytes}");

// Server
Debug.Log($"[{Time.time:F3}] [SERVER RECV] {intentType} seq={seq} dir={dir}");
Debug.Log($"[{Time.time:F3}] [SERVER APPLY] seq={seq} intraServerMs={delay}");
Debug.Log($"[{Time.time:F3}] [SNAPSHOT] tick={tick} entities={count}");

// Client
Debug.Log($"[{Time.time:F3}] [SNAPSHOT RECV] +{totalDelay}ms tick={tick} pos={pos}");
```

**Search for:** `[INPUT START]`, `[SERVER RECV]`, `[SNAPSHOT]` in console.

### Log Example (MoveDir Right)

```
[1.283] [INPUT START] dir=(1.00, 0.00) speed=8.0
[1.283] [INTENT] +0ms MoveDir seq=1 dir=(1.00,0.00)
[1.283] [SEND ENQUEUE] C→S seq=1 intent=MoveDir
[1.283] [SOCKET SEND] C→S size=18

[1.300] [SERVER RECV] MoveDir seq=1 dir=(1.00,0.00)
[1.300] [SERVER APPLY] MoveDir seq=1 intraServerMs=17.32

[1.316] [SERVER BROADCAST] tick=60 entities=1

[1.333] [SNAPSHOT RECV] +50ms tick=60 pos=(0.133,0,0) vel=(8,0,0)
```

**Trace packet by sequence:** Match `seq=1` across logs.

---

## Common Issues & Solutions

### Symptom: High input-to-visual latency (>100ms)

**Possible Causes:**

1. **Double-flush not working:** Verify `ForceIterateOutgoing()` called in LateUpdate
2. **Rate limits too low:** Check `INTENT_SEND_INTERVAL` and `_inputSendRate` are 120Hz
3. **Server tick rate too low:** Increase from 30Hz to 60Hz

**Diagnosis:** Check logs for delta between `[SEND ENQUEUE]` and `[SOCKET SEND]`. Should be 0ms.

### Symptom: Rubber-banding (position jumps back)

**Cause:** This should NEVER happen in V5.0 Pure LoL (no prediction).

**Possible Causes (if it does happen):**

1. **Dead-reckoning overshoot:** `_lastServerVel` is stale — check that snapshots are arriving at 60Hz
2. **Snap smoothing too slow:** `NetcodeConstants.VISUAL_SMOOTHING_SPEED` may be too low

**Diagnosis:** Log `_lastServerPos`, `_lastServerVel`, and `_visualPos` in `EntityView.UpdatePosition()`. The gap between `deadReckonedPos` and `_visualPos` should stay under ~0.5 units.

### Symptom: Animation lags behind input

**Cause:** In V5.0 animation speed is derived from `_lastServerVel.magnitude` (EntityView.cs:240). There is ~16ms lag (one snapshot interval) by design.

**If lag is much longer (>100ms):** Snapshots may not be arriving. Check `[SNAPSHOT RECV]` logs.

```csharp
// V5.0: Animation driven by server velocity — this is correct
float speed = _lastServerVel.magnitude;
_animator.SetFloat(SpeedHash, speed);

// NOTE: HasLocalMoveIntent() / GetLocalMoveIntent() do NOT exist in V5.0.
// SetLocalMoveIntent() / ClearLocalMoveIntent() are no-ops (NetworkClient.cs:122-127).
```

### Symptom: Character slides after stopping

**Cause:** `_lastServerVel` is non-zero when player stops but no new snapshot has arrived yet.

**Fix:** This is handled by the snap-when-stopped branch in `EntityView.UpdatePosition()` (line 211). Verify `_lastServerVel.sqrMagnitude < 0.01f` check is not bypassed.

---

## References

### Related Documentation

- [Network Architecture](/Docs/Network/01_Network_Architecture.md) - Adapter pattern, transport
- [Message Specifications](/Docs/Network/02_Message_Specifications.md) - Packet formats
- [Performance Metrics](/Docs/Network/04_Performance_Metrics.md) - Latency breakdown

### External Resources

- [Overwatch GDC 2017](https://www.youtube.com/watch?v=W3aieHjyNvw) - Favor-the-shooter, lag compensation
- [Rocket League Netcode](https://www.youtube.com/watch?v=ueEmiDM94IE) - Client prediction in physics game
- [League of Legends Netcode](https://technology.riotgames.com/news/peeking-valorants-netcode) - Server-authoritative MOBA

### Key Files

```
Client Input:
Assets/Scripts/Client/Input/
├── InputCollector.cs           # Keyboard sampling, mode switch (WASD/click)
└── IntentBuilder.cs            # Rate-limiting (120Hz), intent creation

Client Network:
Assets/Scripts/Core/
└── NetworkClient.cs            # Packet creation, send/receive, snapshot dispatch

Client Visual (V5.0 dead-reckoning):
Assets/Scripts/Client/View/Entities/
├── EntityView.cs               # Base: UpdatePosition() dead-reckoning + snap smoothing
└── PlayerView.cs               # Derived: class visuals, ability triggers

Server:
Assets/Scripts/Server/
└── ServerGameLoop.cs           # Update() polling, FixedUpdate() tick, BroadcastSnapshots()

Simulation:
Assets/GameSim/Core/
└── SimWorld.cs                 # Step() — advances physics + clock
Assets/GameSim/Commands/Handlers/
└── MovementHandler.cs          # Position/velocity update per tick

Network Layer:
Assets/Scripts/Network/NetAdapter/
├── FishNet/FishNetAdapter.cs   # Transport implementation, ForceIterate*()
├── Messages/InputPacket.cs     # Input message format
├── Messages/SnapshotDelta.cs   # Snapshot message format (EntityState = 30 bytes)
└── SnapshotHelper.cs           # Snapshot creation (CreateSnapshot, CreateFilteredSnapshot)
```

---

#rules-verified

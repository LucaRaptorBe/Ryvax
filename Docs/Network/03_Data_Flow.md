# Data Flow

> **Status:** Production
> **Version:** 5.0 (Pure LoL-style Netcode)
> **Last Updated:** 2026-02-03

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
│  ForceIterateIncoming() ── Poll transport (FixedUpdate)     │
│      ↓ (+17ms from input)                                   │
│  FishNetAdapter.HandleServerReceiveInputPacket()            │
│      ↓ (0ms)                                                │
│  ServerGameLoop.OnMoveDirReceived() ── Buffer input         │
│      ↓ (0ms)                                                │
│  FixedUpdate START → FlushPendingMovementInputs()           │
│      ↓ (0ms - applied immediately)                          │
│  SimWorld.Tick() ── Apply input to simulation               │
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
│  VisualPositionManager.OnSnapshotReceived()                 │
│      ↓ (0ms)                                                │
│  BaseInterpolator.OnSnapshotReceived() ── Add to buffer     │
│      ↓ (every frame in Update)                              │
│  VisualPositionManager.Update()                             │
│      ↓ (0ms)                                                │
│  BaseInterpolator.GetBasePos() ── Interpolate snapshots     │
│      ↓ (0ms)                                                │
│  VisualOffsetCorrector.Update() ── Smooth discontinuities   │
│      ↓ (0ms)                                                │
│  PlayerView.UpdatePosition() ── Apply to transform          │
│      ↓ (0ms)                                                │
│  Unity Render ── Pixels on screen!                          │
└─────────────────────────────────────────────────────────────┘
```

**Total Latency:** ~50ms (localhost), ~66-100ms (typical online)

---

## Stage-by-Stage Breakdown

### 1. Input Capture (Client)

**File:** `Assets/Scripts/Client/Input/InputCollector.cs:94-192`

**Trigger:** Unity `Update()` - every frame (~16ms at 60fps, ~8ms at 120fps)

```csharp
void Update()
{
    HandleMovementInput();  // Line 112
}

void HandleMovementInput()
{
    HandleWASDInput();      // Line 143
}

void HandleWASDInput()
{
    // Sample keyboard state
    Vector2 dir = Vector2.zero;
    if (Keyboard.current.wKey.isPressed) dir.y += 1;  // Forward
    if (Keyboard.current.sKey.isPressed) dir.y -= 1;  // Back
    if (Keyboard.current.aKey.isPressed) dir.x -= 1;  // Left
    if (Keyboard.current.dKey.isPressed) dir.x += 1;  // Right

    bool isMoving = dir.sqrMagnitude > 0.01f;

    // V5.0 PURE LOL: Set local intent (for rotation/animation feedback)
    if (isMoving)
    {
        Vector3 moveDir3D = new Vector3(dir.x, 0, dir.y).normalized;
        _networkClient.SetLocalMoveIntent(moveDir3D);  // Line 182
    }
    else
    {
        _networkClient.ClearLocalMoveIntent();
    }

    // Send intent to server
    var intent = _intentBuilder.OnKeyboardMove(dir, Time.deltaTime, currentTick);
    if (intent != null)
        _networkClient.SendInputIntent(intent);
}
```

**Key Points:**

- **Local intent set immediately** for instant rotation/animation feedback
- **No local position change** - position comes 100% from server
- Input sampled at frame rate (60-120 fps), but rate-limited by IntentBuilder

**Output:** `InputIntent` (if rate-limit passes)

---

### 2. Intent Building (Client)

**File:** `Assets/Scripts/Client/Input/IntentBuilder.cs:37-120`

**Rate Limit:** 120Hz (8.3ms interval)

```csharp
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz

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

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:117-120` (FixedUpdate START)

```csharp
void FixedUpdate()
{
    // Poll transport BEFORE simulation tick
    _netAdapter.ForceIterateIncoming();

    // Now process buffered inputs...
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

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:144-189` (FixedUpdate START)

```csharp
void FixedUpdate()
{
    _netAdapter.ForceIterateIncoming();  // Poll first

    FlushPendingMovementInputs();        // Apply buffered inputs

    RunSimulation();                     // Tick simulation
    BroadcastSnapshots();                // Send results
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

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:191-200`

```csharp
private void RunSimulation()
{
    _simWorld.Tick();  // Advance simulation by 1 tick
}
```

**File:** `Assets/GameSim/Core/SimWorld.cs:84-104`

```csharp
public void Tick()
{
    _clock.Tick();  // Increment tick counter

    _commandProcessor.ExecuteCommands(this);  // Process movement/abilities
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

**File:** `Assets/Scripts/Server/ServerGameLoop.cs:202-230`

```csharp
private void BroadcastSnapshots()
{
    _snapshotAccumulator += Time.fixedDeltaTime;

    // Broadcast at snapshot rate (60Hz = every tick)
    if (_snapshotAccumulator < _snapshotInterval)
        return;

    _snapshotAccumulator -= _snapshotInterval;

    foreach (var clientId in _netAdapter.GetConnectedClients())
    {
        // Get last ACKed sequences
        uint ackInputSeq = _lastProcessedSeq[clientId];
        uint ackMovementSeq = _lastMovementSeq[clientId];

        // Create snapshot for this client
        var snapshot = SnapshotHelper.CreateSnapshot(
            _simWorld,
            forClientId: clientId,
            ackInputSeq: ackInputSeq,
            ackMovementSeq: ackMovementSeq
        );

        // Send unreliable (UDP)
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

**File:** `Assets/Scripts/Core/NetworkClient.cs:457-510`

```csharp
private void OnSnapshotReceived(int senderId, SnapshotDelta snapshot)
{
    // Convert server tick to time
    double serverTime = TickToTime(snapshot.ServerTick);

    // Update time sync
    _timeSync.OnSnapshotReceived(serverTime);

    // Find local player's entity in snapshot
    for (int i = 0; i < snapshot.EntityCount; i++)
    {
        var entityState = snapshot.Entities[i];

        if (entityState.EntityId == _localPlayerEntityId)
        {
            // Create SnapshotState from EntityState
            var state = new SnapshotState
            {
                Position = entityState.Position,         // Dequantized
                Velocity = entityState.Velocity,         // Dequantized
                RotationY = entityState.Rotation,        // Dequantized
                Speed = entityState.Speed,               // Dequantized
                HasBlinkEvent = entityState.HasBlinkEvent,
                HasTeleportEvent = entityState.HasTeleportEvent,
                HasImmobilizeCC = entityState.HasImmobilizeCC
            };

            // Pass to visual position manager
            _visualPositionManager.OnSnapshotReceived(state, serverTime);
            break;
        }
    }
}
```

**Key Points:**

- **Dequantization** converts 16-bit values back to floats
- **ServerTime calculated** from tick number
- **Only local player processed** (remote players use different path)

**Output:** `SnapshotState` with dequantized values

---

### 16. Interpolation Buffer (Client)

**File:** `Assets/Scripts/Client/Prediction/VisualPositionManager.cs:126-153`

```csharp
public void OnSnapshotReceived(SnapshotState state, double serverTime)
{
    // Update time sync (adjusts adaptive buffer)
    _timeSync.OnSnapshotReceived(serverTime);

    // Add snapshot to interpolation buffer
    _interpolator.OnSnapshotReceived(state, serverTime);

    // Update current speed for corrector thresholds
    _currentSpeed = state.Speed;

    // Update adaptive buffer in corrector
    _corrector.SetAdaptiveBuffer(_timeSync.AdaptiveBuffer);

    // Handle CC (immobilize snaps offset to zero)
    _corrector.OnImmobilizeCC(state.HasImmobilizeCC);

    // Handle discontinuities (blink, teleport)
    if (state.HasBlinkEvent || state.HasTeleportEvent || state.HasStateReset)
    {
        _corrector.SnapHard(serverTime);
        _basePrevValid = false;  // Don't absorb jump
    }
}
```

**File:** `Assets/Scripts/Client/Interpolation/BaseInterpolator.cs:186-204`

```csharp
public void OnSnapshotReceived(SnapshotState state, double serverTime)
{
    _buffer.Add(state, serverTime);  // Add to ring buffer
}
```

**Buffer:** Ring buffer holds last ~8-10 snapshots for interpolation.

---

### 17. Frame Update Loop (Client)

**File:** `Assets/Scripts/Core/NetworkClient.cs:224-251`

```csharp
void Update()
{
    // Update time sync (advances perceived server time)
    _timeSync.Update(Time.deltaTime);

    // Update visual position (interpolates between snapshots)
    _visualPositionManager.Update(Time.deltaTime);

    // Send inputs...
}
```

**File:** `Assets/Scripts/Client/Prediction/VisualPositionManager.cs:158-205`

```csharp
public void Update(float dt)
{
    // Update time sync
    _timeSync.Update(dt);

    // Get render time (server time - adaptive buffer)
    double renderTime = _timeSync.RenderTime;

    // Get interpolated base position
    var result = _interpolator.GetBasePos(renderTime);
    _basePos = result.Position;

    // Detect discontinuities
    if (result.HadDiscontinuity)
    {
        _corrector.SnapHard();
        _basePrevValid = false;
    }

    // Absorb basePos jumps with visual offset
    if (_basePrevValid && result.Quality == InterpolationQuality.Interpolated)
    {
        _corrector.AbsorbBasePosJump(_basePrev, _basePos);
    }

    _basePrev = _basePos;
    _basePrevValid = true;

    // Update corrector (exponential decay of offset)
    _corrector.Update(dt, _currentSpeed);
}
```

**Key Points:**

- **RenderTime** = ServerTime - AdaptiveBuffer (~80-160ms behind)
- **BasePos** = Lerp between two snapshots at renderTime
- **VisualOffset** absorbs jumps to maintain continuity

---

### 18. Interpolation (Client)

**File:** `Assets/Scripts/Client/Interpolation/BaseInterpolator.cs:206-260`

```csharp
public InterpolationResult GetBasePos(double renderTime)
{
    // Find two snapshots bracketing renderTime
    bool found = FindBracketingSnapshots(renderTime, out var A, out var B, out float alpha);

    if (!found)
    {
        // Extrapolate or freeze if no snapshots available
        // ...
        return new InterpolationResult
        {
            Position = _buffer.GetLatest().Position,
            Quality = InterpolationQuality.Frozen
        };
    }

    // Interpolate position
    Vector3 position = Vector3.Lerp(A.Position, B.Position, alpha);

    return new InterpolationResult
    {
        Position = position,
        RotationY = Mathf.LerpAngle(A.RotationY, B.RotationY, alpha),
        Quality = InterpolationQuality.Interpolated,
        HadDiscontinuity = false
    };
}
```

**Example:**

```
Snapshot A: time=1.000s, pos=(0, 0, 0)
Snapshot B: time=1.016s, pos=(0.133, 0, 0)
RenderTime: 1.008s
Alpha: (1.008 - 1.000) / (1.016 - 1.000) = 0.5

Interpolated: Lerp((0,0,0), (0.133,0,0), 0.5) = (0.066, 0, 0)
```

**Result:** Smooth position between snapshots, not discrete jumps.

---

### 19. Visual Offset Correction (Client)

**File:** `Assets/Scripts/Client/Prediction/VisualOffsetCorrector.cs:78-136`

**Purpose:** Absorb basePos jumps (from interpolation discontinuities) to maintain visual continuity.

```csharp
public void AbsorbBasePosJump(Vector3 basePrev, Vector3 basePos)
{
    if (_isImmobilized) return;  // Skip during CC

    // Add delta to visual offset (keeps visual position continuous)
    Vector3 delta = basePrev - basePos;
    _visualOffset += delta;
}

public void Update(float dt, float currentSpeed)
{
    if (_isImmobilized)
    {
        _visualOffset = Vector3.zero;  // Snap during CC
        return;
    }

    float gap = _visualOffset.magnitude;

    // Too small - snap to zero
    if (gap < 0.5f)
    {
        _visualOffset = Vector3.zero;
        return;
    }

    // Dynamic thresholds based on speed
    float smallGap = Mathf.Max(20f, currentSpeed * 0.06f);
    float largeGap = Mathf.Clamp(Mathf.Max(80f, currentSpeed * 0.25f), 80f, 250f);

    // Exponential decay (smooth correction)
    if (gap < smallGap)
    {
        _visualOffset *= Mathf.Exp(-6f * dt);  // Gentle
    }
    else if (gap < largeGap)
    {
        _visualOffset *= Mathf.Exp(-15f * dt);  // Aggressive
    }
    else
    {
        _visualOffset = Vector3.zero;  // Snap (too far)
    }
}
```

**Key Points:**

- **Absorb jumps** to avoid visual pops
- **Exponential decay** smooths correction over time
- **Speed-based thresholds** adapt to movement velocity
- **CC handling** snaps offset to zero (no smoothing during root/stun)

---

### 20. Visual Rendering (Client)

**File:** `Assets/Scripts/Client/View/Entities/PlayerView.cs:128-214`

```csharp
protected override void Update()
{
    base.Update();

    if (_isLocalPlayer)
    {
        UpdateLocalPlayerView();
    }
    else
    {
        UpdateRemotePlayerView();
    }
}

private void UpdateLocalPlayerView()
{
    // POSITION: Always from server (interpolated)
    if (_networkClient.HasVisualPosition)
    {
        transform.position = _networkClient.GetVisualPosition();
        // = _basePos + _corrector.VisualOffset
    }

    // ROTATION: V5.0 Pure LoL - Use local intent for immediate feedback
    if (_networkClient.HasLocalMoveIntent())
    {
        Vector3 intent = _networkClient.GetLocalMoveIntent();
        float targetRotY = Mathf.Atan2(intent.x, intent.z) * Mathf.Rad2Deg;

        // Smooth rotation towards input direction
        float smoothedRotY = Mathf.LerpAngle(
            transform.eulerAngles.y,
            targetRotY,
            Time.deltaTime * 15f  // Fast response
        );

        transform.rotation = Quaternion.Euler(0, smoothedRotY, 0);
    }
    else
    {
        // No local intent - use server rotation
        float serverRotY = _networkClient.GetVisualRotationY();
        transform.rotation = Quaternion.Euler(0, serverRotY, 0);
    }

    // ANIMATION: V5.0 Pure LoL - Use local intent for immediate feedback
    bool localIsMoving = _networkClient.HasLocalMoveIntent();
    _animator.SetBool("IsMoving", localIsMoving);
    _animator.SetFloat("Speed", localIsMoving ? 1f : 0f);
}
```

**Key Points:**

- **Position:** 100% from server (via interpolation)
- **Rotation:** From local intent (instant feedback) OR server (if no intent)
- **Animation:** From local intent (instant "run" animation)

**Visual Position Calculation:**

```csharp
// In NetworkClient.cs
public Vector3 GetVisualPosition()
{
    return _visualPositionManager.VisualPosition;
    // = _basePos + _corrector.VisualOffset
}
```

---

## V5.0 Pure LoL-Style Specifics

### What is "Pure LoL-Style"?

**Definition:** Client position comes **100% from server snapshots** (interpolated), with **local intent feedback** only for rotation and animation.

**No Client-Side Prediction:** Unlike FPS games (e.g., Valorant, CS:GO), the client does NOT move its character locally and reconcile with server. This eliminates rubber-banding but adds ~50-100ms perceived latency for position.

**Instant Feedback via Local Intent:**

- **Rotation:** Character turns towards input direction immediately
- **Animation:** "Run" animation starts immediately
- **Position:** Arrives ~50-100ms later (from server)

**Why This Works for MOBA:**

1. **Movement is predictable** - no complex physics, just direction × speed
2. **Rotation feedback** gives illusion of responsiveness
3. **50-100ms latency** is acceptable for strategic gameplay (vs twitch shooter)
4. **Zero rubber-banding** - what you see is ground truth

### Comparison: Prediction vs Pure LoL

| Aspect | Client Prediction (FPS) | Pure LoL (MOBA) |
|--------|-------------------------|-----------------|
| **Position** | Local prediction + reconciliation | Server interpolation only |
| **Rotation** | Server or local | Local intent (instant) |
| **Animation** | Server or local | Local intent (instant) |
| **Latency (perceived)** | 0ms (position) | 50-100ms (position) |
| **Rubber-banding** | Frequent (on misprediction) | Never |
| **Divergence Risk** | High (walls, CC, knockback) | Zero |
| **Complexity** | High (rollback, reconciliation) | Low (interpolation only) |

### Local Intent Feedback Flow

```
Frame 0: Player presses 'W'
    ↓
    SetLocalMoveIntent(forward)  ◄── Stored immediately
    ↓
    Rotation: Turn towards 'forward' (LerpAngle ×15)  ◄── INSTANT
    Animation: IsMoving=true, Speed=1.0               ◄── INSTANT
    Position: Unchanged (waits for server)            ◄── DELAYED
    ↓
    Send InputPacket(MoveDir, forward) to server

Frame 1-3: Player holds 'W'
    ↓
    Rotation/Animation active (local intent maintained)
    Position still waiting...

Frame ~4 (after ~50-100ms): First snapshot arrives
    ↓
    BasePos advances (via interpolation)
    Position STARTS moving
    Rotation/Animation ALREADY active since Frame 0!
```

**Result:** Player SEES instant response (rotation/animation), FEELS acceptable latency (position).

### Discontinuity Handling

**Problem:** Certain events create position jumps that shouldn't be interpolated (blink, teleport, respawn).

**Solution:** Event flags in snapshot trigger special handling.

```csharp
// Server marks blink event
entityState.EventFlags |= EntityEventFlags.BlinkEvent;

// Client detects and snaps
if (state.HasBlinkEvent)
{
    _corrector.SnapHard(serverTime);   // Zero out visual offset
    _basePrevValid = false;             // Don't absorb this jump
}
```

**Event Flags:**

- `BlinkEvent` - Short teleport (dash ability)
- `TeleportEvent` - Long teleport (recall, summoner spell)
- `StateReset` - Respawn, forced snap

**Without snap:** Client would interpolate smoothly from old position to blink destination (looks wrong).

**With snap:** Client jumps instantly to new position (matches visual expectation).

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
Server receive: 17ms  (34%) ⚠️ Could improve with faster poll
Server tick:    16ms  (32%) ⚠️ Inherent at 60Hz
Network (down): ~1ms  ( 2%)
Client receive: 15ms  (30%) ⚠️ Interpolation buffer delay
────────────────────────────
Total:          50ms (100%)
```

### Where Time Is Spent

1. **Server poll delay (17ms):** Time between packet arriving at OS and server reading it. Could be reduced with higher poll rate.
2. **Server tick (16ms):** Inherent at 60Hz. To reduce, increase tick rate to 120Hz (halves to 8ms).
3. **Client interpolation buffer (15ms):** Adaptive buffer to smooth jitter. Required for smooth visuals.

---

## Optimizations Applied

### Client-Side

1. **120Hz Input Rate:** Intent created every 8.3ms (vs 100ms in v1.0)
2. **120Hz Send Rate:** Packets sent every 8.3ms (vs 33ms in v1.0)
3. **Double-Flush:** Packets sent same frame (0ms delay vs 16-33ms in v1.0)
4. **Local Intent Feedback:** Rotation/animation respond instantly (0ms perceived)

**Result:** 0ms client-side delay (was ~50-100ms in v1.0)

### Server-Side

1. **Manual Incoming Poll:** Polls at FixedUpdate START (deterministic timing)
2. **Input Buffering:** Ensures inputs applied at correct tick boundary
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

1. **Client applying inputs locally:** Verify `SetLocalMoveIntent()` does NOT modify position
2. **Visual offset too large:** Check `_visualOffset.magnitude` in VisualOffsetCorrector

**Diagnosis:** Log `_basePos` and `_visualOffset` separately. Offset should stay < 1 unit.

### Symptom: Animation lags behind input

**Cause:** Animation triggered from server snapshot instead of local intent.

**Fix:** Verify `PlayerView.cs:203-214` uses `HasLocalMoveIntent()` not server state.

```csharp
// WRONG
bool isMoving = _networkClient.GetServerIsMoving();  // Delayed

// RIGHT
bool isMoving = _networkClient.HasLocalMoveIntent();  // Instant
```

### Symptom: Rotation snaps instead of smoothing

**Cause:** Using server rotation instead of local intent.

**Fix:** Verify rotation uses `LerpAngle()` with local intent.

```csharp
// WRONG
transform.rotation = Quaternion.Euler(0, serverRotY, 0);  // Snap

// RIGHT
float smoothedRotY = Mathf.LerpAngle(current, targetRotY, dt * 15f);  // Smooth
```

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
├── InputCollector.cs           # Keyboard sampling, local intent
├── IntentBuilder.cs            # Rate-limiting, intent creation

Client Network:
Assets/Scripts/Core/
└── NetworkClient.cs            # Packet creation, send/receive

Client Visual:
Assets/Scripts/Client/Prediction/
├── VisualPositionManager.cs    # Orchestrates interpolation + correction
├── BaseInterpolator.cs         # Lerp between snapshots
└── VisualOffsetCorrector.cs    # Smooth discontinuities

Server:
Assets/Scripts/Server/
└── ServerGameLoop.cs           # Input buffering, simulation tick

Simulation:
Assets/GameSim/Commands/Handlers/
└── MovementHandler.cs          # Position/velocity update

Network Layer:
Assets/Scripts/Network/NetAdapter/
├── FishNet/FishNetAdapter.cs   # Transport implementation
├── Messages/InputPacket.cs     # Input message format
├── Messages/SnapshotDelta.cs   # Snapshot message format
└── SnapshotHelper.cs           # Snapshot creation
```

---

#rules-verified

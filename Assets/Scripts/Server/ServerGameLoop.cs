// ServerGameLoop.cs - Server-side game loop
// Runs authoritative simulation and broadcasts snapshots
//
// INPUT BUFFERING:
// Movement inputs are buffered and applied at the START of each FixedUpdate.
// This ensures deterministic ordering: all inputs received between tick N-1 and N
// are applied at the beginning of tick N, before RunSimulation().
//
// Only the LAST input per client (by Seq) is kept - earlier inputs in the same
// tick are discarded. This prevents input spam from affecting performance.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using MOBANet.Core;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Commands;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter;
using MOBANet.NetAdapter.AOI;
using MOBANet.NetAdapter.Buffers;
using MOBANet.NetAdapter.Messages;
using MOBANet.NetAdapter.FishNet;
using MOBANet.Shared;

// Alias to avoid ambiguity with UnityEngine.EventType
using NetEventType = MOBANet.NetAdapter.Messages.EventType;

namespace MOBANet.Server
{
    /// <summary>
    /// Server-side game loop.
    /// Handles authoritative simulation and network broadcasting.
    /// </summary>
    public class ServerGameLoop : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Network")]
        [SerializeField] private FishNetAdapter _netAdapter;
        [SerializeField] private ushort _port = 7777;
        [SerializeField] private bool _autoStart = true;

        [Header("Simulation")]
        [SerializeField] private float _snapshotRate = NetcodeConstants.SNAPSHOT_RATE;

        [Header("AOI (Area of Interest)")]
        [SerializeField] private bool _enableAOI = true;
        [SerializeField] private float _aoiVisionRadius = 50f; // Increased to cover spawn distance (40 units)
        [SerializeField] private float _aoiHysteresis = 5f;
        [SerializeField] private float _aoiCellSize = 20f;

        [Header("Spawning")]
        [SerializeField] private Vector3[] _team1Spawns = {
            new(-20, 0, 0),
            new(-20, 0, 5),
            new(-20, 0, -5),
            new(-20, 0, 10),
            new(-20, 0, -10)
        };
        [SerializeField] private Vector3[] _team2Spawns = {
            new(20, 0, 0),
            new(20, 0, 5),
            new(20, 0, -5),
            new(20, 0, 10),
            new(20, 0, -10)
        };

        #endregion

        #region Private Fields

        private SimWorld _simWorld;
        private ServerCommandBuffer _commandBuffer;
        private AOIManager _aoiManager;
        private float _snapshotAccumulator;
        private float _snapshotInterval;
        private int _team1Count;
        private int _team2Count;
        private bool _isRunning;

        // AOI statistics
        private int _lastAOIEntityCount;
        private int _aoiEventsThisFrame;

        // Movement watchdog
        private const float MOVE_WATCHDOG_TIMEOUT = 0.3f; // 300ms

        // Movement state sequence tracking (per client)
        private readonly Dictionary<int, uint> _lastMovementSeq = new();

        // INPUT BUFFERING V2: ConcurrentQueue for thread-safe enqueue + Dictionary for per-tick processing
        // Network thread enqueues, main thread dequeues and processes
        private readonly ConcurrentQueue<PendingMovementInput> _inputQueue = new();

        // Pending input for next tick (one per client, last-input-wins PER TICK)
        private readonly Dictionary<int, PendingMovementInput> _pendingNextTick = new();

        #endregion

        #region Input Buffering Types

        /// <summary>
        /// Buffered movement input waiting to be applied.
        /// V2: Includes ApplyTick for bounded latency.
        /// </summary>
        private struct PendingMovementInput
        {
            public int ClientId;
            public uint Seq;
            public PacketIntentType Type;
            public Vector2 Payload;       // Direction (MoveDir) or Position (MoveTo)
            public uint TargetEntityId;   // For Follow intent
            public float ReceivedTime;    // For debug/latency tracking
            public uint RecvServerTick;   // Server tick when input was received
            public uint ApplyTick;        // Target tick for application (RecvServerTick + 1)
            public long RecvStopwatchTicks; // High-precision timing for diagnostics
        }

        #endregion

        #region Properties

        /// <summary>
        /// Simulation world
        /// </summary>
        public SimWorld SimWorld => _simWorld;

        /// <summary>
        /// Is server running?
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Current simulation tick
        /// </summary>
        public uint CurrentTick => _simWorld?.Clock.CurrentTick ?? 0;

        /// <summary>
        /// Number of connected players
        /// </summary>
        public int PlayerCount => _simWorld?.PlayerCount ?? 0;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            _simWorld = new SimWorld { IsServer = true };

            // Inject spawn positions into SimWorld
            _simWorld.SetSpawnPositions(_team1Spawns, _team2Spawns);

            _commandBuffer = new ServerCommandBuffer();
            _aoiManager = new AOIManager(_aoiVisionRadius, _aoiHysteresis, _aoiCellSize);
            _snapshotInterval = 1f / _snapshotRate;

            // Find adapter if not assigned
            if (_netAdapter == null)
            {
                _netAdapter = FindAnyObjectByType<FishNetAdapter>();
            }
        }

        private void Start()
        {
            if (_autoStart && _netAdapter != null)
            {
                StartServer();
            }
        }

        private void OnEnable()
        {
            if (_netAdapter != null)
            {
                _netAdapter.OnClientConnected += OnClientConnected;
                _netAdapter.OnClientDisconnected += OnClientDisconnected;
                _netAdapter.OnServerStarted += OnServerStarted;
                // V4: Type-specific movement events
                _netAdapter.OnMoveDirReceived += OnMoveDirReceived;
                _netAdapter.OnMoveToReceived += OnMoveToReceived;
                _netAdapter.OnStopReceived += OnStopReceived;
                _netAdapter.OnFollowReceived += OnFollowReceived;
                _netAdapter.RegisterHandler<GameCommand>(OnCommandReceived);
            }
        }

        private void OnDisable()
        {
            if (_netAdapter != null)
            {
                _netAdapter.OnClientConnected -= OnClientConnected;
                _netAdapter.OnClientDisconnected -= OnClientDisconnected;
                _netAdapter.OnServerStarted -= OnServerStarted;
                // V4: Type-specific movement events
                _netAdapter.OnMoveDirReceived -= OnMoveDirReceived;
                _netAdapter.OnMoveToReceived -= OnMoveToReceived;
                _netAdapter.OnStopReceived -= OnStopReceived;
                _netAdapter.OnFollowReceived -= OnFollowReceived;
                _netAdapter.UnregisterHandler<GameCommand>();
            }
        }

        /// <summary>
        /// Process incoming network packets every frame (60-120 FPS).
        /// Decoupled from simulation tick rate (30 Hz) to minimize input latency.
        /// </summary>
        private void Update()
        {
            if (!_isRunning) return;

            // Process incoming packets at frame rate (not tick rate)
            // This eliminates the 33ms delay from waiting for next tick
            _netAdapter.ForceIterateIncoming();
        }

        /// <summary>
        /// Run authoritative simulation at fixed 30 Hz tick rate.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_isRunning) return;

            RunSimulation();
            TickWatchdog(Time.fixedDeltaTime);
            BroadcastSnapshots();
        }

        #endregion

        #region Server Control

        /// <summary>
        /// Start the server
        /// </summary>
        public void StartServer()
        {
            if (_netAdapter == null)
            {
                Debug.LogError("[ServerGameLoop] NetAdapter not assigned!");
                return;
            }

            _netAdapter.StartServer(_port);
            // Debug.Log($"[ServerGameLoop] Starting server on port {_port}");
        }

        /// <summary>
        /// Stop the server
        /// </summary>
        public void StopServer()
        {
            _netAdapter?.Shutdown();
            _isRunning = false;
            _simWorld.Clear();
            _commandBuffer.Clear();
            // Debug.Log("[ServerGameLoop] Server stopped");
        }

        #endregion

        #region Simulation

        private void RunSimulation()
        {
            // Accumulate time and run ticks
            int ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime);

            for (int i = 0; i < ticksToRun; i++)
            {
                uint currentTick = _simWorld.Clock.CurrentTick;

                // FIX #2 & #3: Drain queue into per-client map, then apply based on ApplyTick
                // This ensures:
                // - Deterministic ordering (not ConcurrentDictionary enumeration)
                // - Last-input-wins PER TICK, not per frame
                // - Bounded latency via ApplyTick
                DrainInputQueueForTick(currentTick);
                FlushPendingMovementInputs(currentTick);

                // Step advances physics/state, then Clock.Advance()
                _simWorld.Step();

                uint tickAfter = _simWorld.Clock.CurrentTick;

                // LOG: Track tick execution
                // Debug.Log($"[{Time.time:F3}] [SERVER TICK] simulated={currentTick} → now={tickAfter}");
            }

            // Update AOI positions after simulation
            if (_enableAOI)
            {
                foreach (var entity in _simWorld.AllEntities)
                {
                    // Component-based states: extract position from specific entity type
                    if (entity is SimPlayer player)
                    {
                        _aoiManager.UpdateEntityPosition(entity.Id, player.Transform.Position);
                    }
                    // TODO: Add other entity types when needed
                }
            }
        }

        /// <summary>
        /// Check for stale movement state and force stop if needed.
        /// Prevents "ghost runs" if MoveStop packet is lost.
        /// </summary>
        private void TickWatchdog(float dt)
        {
            foreach (var player in _simWorld.AllPlayers)
            {
                player.TimeSinceLastMoveCmd += dt;

                // Use MoveDirection directly, not derived IsMoving state
                if (player.Transform.MoveDirection.sqrMagnitude > 0f &&
                    player.TimeSinceLastMoveCmd > MOVE_WATCHDOG_TIMEOUT)
                {
                    // Debug.Log($"[ServerGameLoop] Watchdog: Forcing stop for player {player.Id}");
                    player.StopMoving();
                }
            }
        }

        private void BroadcastSnapshots()
        {
            _snapshotAccumulator += Time.fixedDeltaTime;

            if (_snapshotAccumulator < _snapshotInterval) return;
            _snapshotAccumulator -= _snapshotInterval;

            // LOG: Track when snapshot is broadcast
            // var firstPlayer = _simWorld.AllPlayers.GetEnumerator();
            // if (firstPlayer.MoveNext())
            // {
            //     var vel = firstPlayer.Current.Transform.Velocity;
            //     Debug.Log($"[{Time.time:F3}] [SERVER BROADCAST] tick={_simWorld.Clock.CurrentTick} vel=({vel.x:F1},{vel.z:F1})");
            // }

            _aoiEventsThisFrame = 0;
            int totalAOIEntities = 0;

            // Send personalized snapshot to each client (with their ack seq)
            foreach (var player in _simWorld.AllPlayers)
            {
                int clientId = player.OwnerClientId;
                uint ackSeq = _commandBuffer.GetLastAckSeq(clientId);

                SnapshotDelta snapshot;

                // Get movement sequence for this client (for movement reconciliation)
                uint movementSeq = _lastMovementSeq.TryGetValue(clientId, out uint seq) ? seq : 0;

                // Debug: Log movement ack being sent (throttled)
                if (movementSeq > 0)
                {
                    DebugLogger.LogThrottled(DebugLogger.Category.Network,
                        $"[SERVER SNAPSHOT] to client {clientId} | ackMovementSeq={movementSeq} | tick={_simWorld.Clock.CurrentTick}",
                        frameInterval: 60);
                }

                if (_enableAOI)
                {
                    // Update AOI and get enter/leave events
                    var (entered, left) = _aoiManager.UpdateClientAOI(clientId, player.Transform.Position, _simWorld);

                    // Send AOI events
                    foreach (var entityId in entered)
                    {
                        var entity = _simWorld.GetEntity(entityId);
                        if (entity is SimPlayer enteredPlayer)
                        {
                            var evt = ReliableEvent.EntityEnterAOI(
                                _simWorld.Clock.CurrentTick,
                                entityId,
                                enteredPlayer.OwnerClientId,
                                enteredPlayer.TeamId
                            );
                            _netAdapter.SendToClient(clientId, evt, reliable: true);
                            _aoiEventsThisFrame++;
                        }
                    }

                    foreach (var entityId in left)
                    {
                        var evt = ReliableEvent.EntityLeaveAOI(_simWorld.Clock.CurrentTick, entityId);
                        _netAdapter.SendToClient(clientId, evt, reliable: true);
                        _aoiEventsThisFrame++;
                    }

                    // Create filtered snapshot with only visible entities
                    var visible = _aoiManager.GetVisibleEntities(clientId);
                    snapshot = SnapshotHelper.CreateFilteredSnapshot(_simWorld, visible, ackSeq, player.Id, movementSeq);
                    totalAOIEntities += snapshot.EntityCount;
                }
                else
                {
                    // No AOI - send all entities
                    snapshot = SnapshotHelper.CreateSnapshot(_simWorld, clientId, ackSeq, movementSeq);
                    totalAOIEntities += snapshot.EntityCount;
                }

                // Debug: log snapshot positions (throttled - génère beaucoup de logs!)
                if (snapshot.EntityCount > 0 && snapshot.Entities != null)
                {
                    for (int i = 0; i < snapshot.EntityCount && i < 2; i++)
                    {
                        var e = snapshot.Entities[i];
                        DebugLogger.LogThrottled(DebugLogger.Category.Snapshot,
                            $"To client {clientId}: Entity {e.EntityId} Pos={e.Position:F2}", frameInterval: 60);
                    }
                }

                _netAdapter.SendToClient(clientId, snapshot, reliable: false);
            }

            // Update stats
            int playerCount = _simWorld.PlayerCount;
            _lastAOIEntityCount = playerCount > 0 ? totalAOIEntities / playerCount : 0;
        }

        #endregion

        #region Network Events

        private void OnServerStarted()
        {
            _isRunning = true;
            // Debug.Log($"[ServerGameLoop] Server started. SimTick={NetcodeConstants.TICK_RATE}Hz, Snapshot={_snapshotRate}Hz, FixedDT={Time.fixedDeltaTime*1000:F1}ms");
        }

        private void OnClientConnected(int clientId)
        {
            // Debug.Log($"[ServerGameLoop] Client {clientId} connected");

            // Assign team (simple alternating)
            byte teamId = (byte)(_team1Count <= _team2Count ? 1 : 2);
            int playerIndex = teamId == 1 ? _team1Count : _team2Count;

            // Get spawn position from SimWorld (uses injected arrays)
            Vector3 spawnPos = _simWorld.GetSpawnPosition(teamId, playerIndex);

            // Increment team counter
            if (teamId == 1)
                _team1Count++;
            else
                _team2Count++;

            // Spawn player in simulation
            var player = _simWorld.SpawnPlayer(clientId, spawnPos, teamId);

            uint spawnTick = _simWorld.Clock.CurrentTick;

            // Register in command buffer
            _commandBuffer.RegisterClient(clientId);

            // Register in AOI system
            _aoiManager.RegisterClient(clientId);
            _aoiManager.UpdateEntityPosition(player.Id, spawnPos);

            // Send match configuration first (spawn positions, etc.)
            _netAdapter.SendMatchConfigToClient(clientId, _team1Spawns, _team2Spawns);

            // Delay spawn events slightly to ensure client handlers are ready (especially in Host mode)
            StartCoroutine(SendSpawnEventsDelayed(clientId, player.Id, teamId));

            // Debug.Log($"[ServerGameLoop] SERVER SPAWN: Player {player.Id}, ClientId={clientId}, Team={teamId}, Pos={spawnPos:F3}, ServerTick={spawnTick}, SimPlayer.Pos={player.Transform.Position:F3}");
        }

        private IEnumerator SendSpawnEventsDelayed(int clientId, uint playerId, byte teamId)
        {
            // Wait one frame to ensure client broadcast handlers are registered
            yield return null;

            // Get actual spawn position from simulation
            var spawnedPlayer = _simWorld.GetEntity<SimPlayer>(playerId);
            Vector3 actualSpawnPos = spawnedPlayer?.Transform.Position ?? Vector3.zero;

            // Broadcast spawn event to all clients with actual position
            var spawnEvent = ReliableEvent.EntitySpawn(
                _simWorld.Clock.CurrentTick,
                playerId,
                clientId,
                teamId,
                actualSpawnPos
            );
            _netAdapter.SendToAll(spawnEvent, reliable: true);

            // Send existing players to new client
            foreach (var existingPlayer in _simWorld.AllPlayers)
            {
                if (existingPlayer.Id != playerId)
                {
                    var existingSpawn = ReliableEvent.EntitySpawn(
                        _simWorld.Clock.CurrentTick,
                        existingPlayer.Id,
                        existingPlayer.OwnerClientId,
                        existingPlayer.TeamId,
                        existingPlayer.Transform.Position
                    );
                    _netAdapter.SendToClient(clientId, existingSpawn, reliable: true);
                }
            }

            // Debug.Log($"[ServerGameLoop] Sent spawn events for player {playerId} to client {clientId}");
        }

        private void OnClientDisconnected(int clientId)
        {
            // Debug.Log($"[ServerGameLoop] Client {clientId} disconnected");

            var player = _simWorld.GetPlayerByClient(clientId);
            if (player != null)
            {
                // Remove from AOI spatial grid
                _aoiManager.RemoveEntity(player.Id);

                // Broadcast death/despawn event
                var deathEvent = ReliableEvent.EntityDeath(
                    _simWorld.Clock.CurrentTick,
                    player.Id
                );
                _netAdapter.SendToAll(deathEvent, reliable: true);

                // Remove from simulation
                _simWorld.DestroyEntity(player.Id);
            }

            // Unregister from command buffer, AOI, and movement seq tracking
            _commandBuffer.UnregisterClient(clientId);
            _aoiManager.UnregisterClient(clientId);
            _lastMovementSeq.Remove(clientId);
        }

        #region Movement Intent Handlers (V4 - Buffered)

        // ============================================================================
        // INPUT BUFFERING SYSTEM V2
        // ============================================================================
        // FIXES for 2-tick delay:
        //
        // 1. ForceIterateIncoming() called at START of FixedUpdate
        //    → Network data available before simulation, not after
        //
        // 2. ConcurrentQueue instead of ConcurrentDictionary
        //    → Network thread enqueues, main thread dequeues
        //    → Deterministic processing order
        //    → No expensive enumeration of concurrent collection
        //
        // 3. ApplyTick for bounded latency
        //    → Input received at tick T gets ApplyTick = T+1
        //    → Guarantees max 1 tick delay, stable and predictable
        //
        // 4. Last-input-wins PER TICK, not per frame
        //    → _pendingNextTick holds latest input per client for current tick
        //    → Cleared after each tick, not after each frame
        // ============================================================================

        /// <summary>
        /// Buffer a movement input for application at next tick.
        /// Thread-safe: can be called from FishNet network callbacks.
        /// V2: Enqueues to ConcurrentQueue with ApplyTick = currentTick + 1.
        /// </summary>
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

            // Thread-safe enqueue (no blocking, no enumeration)
            _inputQueue.Enqueue(input);
        }

        /// <summary>
        /// Drain ConcurrentQueue into per-client Dictionary for the given tick.
        /// Last-input-wins PER TICK: only keeps highest Seq per client.
        /// Called before FlushPendingMovementInputs at each tick.
        /// </summary>
        private void DrainInputQueueForTick(uint currentTick)
        {
            // Drain all pending inputs from queue
            while (_inputQueue.TryDequeue(out var input))
            {
                // Only consider inputs whose ApplyTick <= currentTick
                // Inputs with ApplyTick > currentTick stay in their "slot" for later
                // (but since we use queue, they're already dequeued - we apply them now)

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

        /// <summary>
        /// Apply all pending inputs for the current tick.
        /// V2: Applies inputs immediately since ApplyTick is already bounded at buffer time.
        /// Clears _pendingNextTick after processing.
        /// </summary>
        private void FlushPendingMovementInputs(uint currentTick)
        {
            if (_pendingNextTick.Count == 0) return;

            int appliedCount = 0;

            foreach (var kvp in _pendingNextTick)
            {
                var input = kvp.Value;

                // Since we set ApplyTick = RecvTick + 1 at buffer time,
                // and we drain before each tick, inputs are ready to apply immediately.
                // The bounded latency is already guaranteed by the +1 offset.
                if (ApplyMovementInput(input, currentTick))
                    appliedCount++;
            }

            // Clear for next tick
            _pendingNextTick.Clear();

            // Debug: log flush stats
            // if (appliedCount > 0)
            // {
            //     Debug.Log($"[{Time.time:F3}] [SERVER FLUSH] Applied {appliedCount} inputs at tick={currentTick}");
            // }
        }

        /// <summary>
        /// Apply a single buffered movement input.
        /// V2: Takes applyTick for diagnostics.
        /// Returns true if successfully applied, false if rejected.
        /// </summary>
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

                case PacketIntentType.MoveTo:
                    return ApplyMoveTo(player, input, applyTick, tickDelta);

                case PacketIntentType.Stop:
                    return ApplyStop(player, input, applyTick, tickDelta);

                case PacketIntentType.Follow:
                    return ApplyFollow(player, input, applyTick, tickDelta);

                default:
                    return false;
            }
        }

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

            // V2: Log with tick delta + intra-server latency
            // Debug.Log($"[{Time.time:F3}] [SERVER APPLY] MoveDir seq={input.Seq} dir=({input.Payload.x:F2},{input.Payload.y:F2}) recvTick={input.RecvServerTick} applyTick={applyTick} Δtick={tickDelta} intraServerMs={intraServerMs:F2}");

            player.SetMoveDirection(dir3D);
            player.TimeSinceLastMoveCmd = 0f;
            return true;
        }

        private bool ApplyMoveTo(SimPlayer player, PendingMovementInput input, uint applyTick, uint tickDelta)
        {
            Vector3 target = new Vector3(input.Payload.x, 0f, input.Payload.y);

            // METRIC A: Calculate intra-server delay
            long applyStopwatchTicks = Stopwatch.GetTimestamp();
            double intraServerMs = (applyStopwatchTicks - input.RecvStopwatchTicks) * 1000.0 / Stopwatch.Frequency;

            // Debug.Log($"[{Time.time:F3}] [SERVER APPLY] MoveTo seq={input.Seq} target=({input.Payload.x:F1},{input.Payload.y:F1}) recvTick={input.RecvServerTick} applyTick={applyTick} Δtick={tickDelta} intraServerMs={intraServerMs:F2}");

            player.SetMoveTarget(target);
            player.TimeSinceLastMoveCmd = 0f;
            return true;
        }

        private bool ApplyStop(SimPlayer player, PendingMovementInput input, uint applyTick, uint tickDelta)
        {
            // METRIC A: Calculate intra-server delay
            long applyStopwatchTicks = Stopwatch.GetTimestamp();
            double intraServerMs = (applyStopwatchTicks - input.RecvStopwatchTicks) * 1000.0 / Stopwatch.Frequency;

            // Debug.Log($"[{Time.time:F3}] [SERVER APPLY] Stop seq={input.Seq} recvTick={input.RecvServerTick} applyTick={applyTick} Δtick={tickDelta} intraServerMs={intraServerMs:F2}");

            if (player.Transform.IsMoving)
            {
                player.LastMoveStopTime = Time.time;
            }
            player.StopMoving();
            player.TimeSinceLastMoveCmd = 0f;
            return true;
        }

        private bool ApplyFollow(SimPlayer player, PendingMovementInput input, uint applyTick, uint tickDelta)
        {
            var targetPlayer = _simWorld.GetEntity<SimPlayer>(input.TargetEntityId);
            if (targetPlayer == null)
            {
                Debug.LogWarning($"[ServerGameLoop] Follow target entity {input.TargetEntityId} not found");
                player.StopMoving();
                return false;
            }

            // METRIC A: Calculate intra-server delay
            long applyStopwatchTicks = Stopwatch.GetTimestamp();
            double intraServerMs = (applyStopwatchTicks - input.RecvStopwatchTicks) * 1000.0 / Stopwatch.Frequency;

            // Debug.Log($"[{Time.time:F3}] [SERVER APPLY] Follow seq={input.Seq} target={input.TargetEntityId} recvTick={input.RecvServerTick} applyTick={applyTick} Δtick={tickDelta} intraServerMs={intraServerMs:F2}");

            player.SetMoveTarget(targetPlayer.Transform.Position);
            player.TimeSinceLastMoveCmd = 0f;
            return true;
        }

        /// <summary>
        /// Check monotonic sequence and update tracking.
        /// Returns false if input should be rejected (out-of-order).
        /// </summary>
        private bool CheckAndUpdateMovementSeq(int clientId, uint movementSeq)
        {
            if (!_lastMovementSeq.TryGetValue(clientId, out uint lastSeq))
            {
                lastSeq = 0;
                _lastMovementSeq[clientId] = 0;
            }

            if (movementSeq <= lastSeq)
                return false;

            _lastMovementSeq[clientId] = movementSeq;
            return true;
        }

        // ============================================================================
        // NETWORK EVENT HANDLERS (called by FishNet - may be async)
        // These just buffer the input, actual application is in FlushPendingMovementInputs
        // ============================================================================

        /// <summary>
        /// Handle MoveDir intent from network. Buffers for next FixedUpdate.
        /// </summary>
        private void OnMoveDirReceived(int clientId, uint movementSeq, Vector2 direction, long recvSocketTicks)
        {
            // Debug.Log($"[{Time.time:F3}] [SERVER RECV] MoveDir seq={movementSeq} dir=({direction.x:F2},{direction.y:F2}) (buffered)");
            BufferMovementInput(clientId, movementSeq, PacketIntentType.MoveDir, direction, recvSocketTicks: recvSocketTicks);
        }

        /// <summary>
        /// Handle MoveTo intent from network. Buffers for next FixedUpdate.
        /// </summary>
        private void OnMoveToReceived(int clientId, uint movementSeq, Vector2 targetPos, long recvSocketTicks)
        {
            // Debug.Log($"[{Time.time:F3}] [SERVER RECV] MoveTo seq={movementSeq} target=({targetPos.x:F1},{targetPos.y:F1}) (buffered)");
            BufferMovementInput(clientId, movementSeq, PacketIntentType.MoveTo, targetPos, recvSocketTicks: recvSocketTicks);
        }

        /// <summary>
        /// Handle Stop intent from network. Buffers for next FixedUpdate.
        /// </summary>
        private void OnStopReceived(int clientId, uint movementSeq, long recvSocketTicks)
        {
            // Debug.Log($"[{Time.time:F3}] [SERVER RECV] Stop seq={movementSeq} (buffered)");
            BufferMovementInput(clientId, movementSeq, PacketIntentType.Stop, recvSocketTicks: recvSocketTicks);
        }

        /// <summary>
        /// Handle Follow intent from network. Buffers for next FixedUpdate.
        /// </summary>
        private void OnFollowReceived(int clientId, uint movementSeq, uint targetEntityId, long recvSocketTicks)
        {
            // Debug.Log($"[{Time.time:F3}] [SERVER RECV] Follow seq={movementSeq} target={targetEntityId} (buffered)");
            BufferMovementInput(clientId, movementSeq, PacketIntentType.Follow, targetEntityId: targetEntityId, recvSocketTicks: recvSocketTicks);
        }

        #endregion

        /// <summary>
        /// Handle incoming game command.
        /// DISCRETE EVENTS ONLY: Jump, spell, attack, items, ping.
        /// Movement is handled by OnMovementStateReceived (semi-stateless).
        /// </summary>
        private void OnCommandReceived(int clientId, GameCommand cmd)
        {
            uint serverTick = _simWorld.Clock.CurrentTick;

            // SEMI-STATELESS: Skip movement commands - handled by OnMovementStateReceived
            if (cmd.Category == CommandCategory.Movement)
            {
                // Still ack the command for client reconciliation
                _commandBuffer.AckCommand(clientId, cmd.Sequence);
                return;
            }

            // METRIC B: Handle ping request - echo back immediately
            if (cmd.Category == CommandCategory.Ping && cmd.Action == PingAction.Request)
            {
                var pongEvent = new ReliableEvent
                {
                    ServerTick = serverTick,
                    Type = NetEventType.Ping, // Reuse Ping event type for pong
                    EntityId = 0,
                    Data1 = cmd.Sequence, // Echo back the ping sequence
                    Data2 = 0
                };
                _netAdapter.SendToClient(clientId, pongEvent, reliable: true);
                // Debug.Log($"[METRIC B] [PONG SEND] seq={cmd.Sequence} to client {clientId}");
                _commandBuffer.AckCommand(clientId, cmd.Sequence);
                return;
            }

            var player = _simWorld.GetPlayerByClient(clientId);

            DebugLogger.LogThrottled(DebugLogger.Category.Network,
                $"Event from client {clientId}: Cat={cmd.Category}, Action={cmd.Action}, Seq={cmd.Sequence}, ServerTick={serverTick}",
                frameInterval: 30);

            // Convert to SimCommand and execute
            var simCmd = CommandHelper.ToSimCommand(cmd, serverTick);
            _simWorld.ExecuteCommand(clientId, simCmd);

            // Track for acknowledgment
            _commandBuffer.AckCommand(clientId, cmd.Sequence);
        }

        #endregion

        #region Debug

        private void OnGUI()
        {
            if (!_isRunning) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = 20 };

            GUILayout.BeginArea(new Rect(10, 10, 400, 350));
            GUILayout.Label($"[SERVER] Tick: {CurrentTick}", style);
            GUILayout.Label($"Players: {PlayerCount}", style);
            GUILayout.Label($"Snapshot Rate: {_snapshotRate} Hz", style);

            // AOI stats
            if (_enableAOI)
            {
                GUILayout.Label($"AOI: ON (r={_aoiVisionRadius})", style);
                GUILayout.Label($"Avg Entities/Snapshot: {_lastAOIEntityCount}", style);
                GUILayout.Label($"AOI Events/Frame: {_aoiEventsThisFrame}", style);
            }
            else
            {
                GUILayout.Label("AOI: OFF", style);
            }
            GUILayout.EndArea();
        }

        #endregion
    }
}

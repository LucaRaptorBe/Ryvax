// ServerGameLoop.cs - Server-side game loop
// Runs authoritative simulation and broadcasts snapshots

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
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

namespace MOBANet.UnityView.Core
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
                _netAdapter.UnregisterHandler<GameCommand>();
            }
        }

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
            Debug.Log($"[ServerGameLoop] Starting server on port {_port}");
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
            Debug.Log("[ServerGameLoop] Server stopped");
        }

        #endregion

        #region Simulation

        private void RunSimulation()
        {
            // Accumulate time and run ticks
            int ticksToRun = _simWorld.Clock.Accumulate(Time.fixedDeltaTime);

            for (int i = 0; i < ticksToRun; i++)
            {
                // Command-based: Commands are applied immediately via ExecuteCommand()
                // Step just advances physics/state
                _simWorld.Step();
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
                    Debug.Log($"[ServerGameLoop] Watchdog: Forcing stop for player {player.Id}");
                    player.StopMoving();
                }
            }
        }

        private void BroadcastSnapshots()
        {
            _snapshotAccumulator += Time.fixedDeltaTime;

            if (_snapshotAccumulator < _snapshotInterval) return;
            _snapshotAccumulator -= _snapshotInterval;

            _aoiEventsThisFrame = 0;
            int totalAOIEntities = 0;

            // Send personalized snapshot to each client (with their ack seq)
            foreach (var player in _simWorld.AllPlayers)
            {
                int clientId = player.OwnerClientId;
                uint ackSeq = _commandBuffer.GetLastAckSeq(clientId);

                SnapshotDelta snapshot;

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
                    snapshot = SnapshotHelper.CreateFilteredSnapshot(_simWorld, visible, ackSeq, player.Id);
                    totalAOIEntities += snapshot.EntityCount;
                }
                else
                {
                    // No AOI - send all entities
                    snapshot = SnapshotHelper.CreateSnapshot(_simWorld, clientId, ackSeq);
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
            Debug.Log($"[ServerGameLoop] Server started. SimTick={NetcodeConstants.TICK_RATE}Hz, Snapshot={_snapshotRate}Hz, FixedDT={Time.fixedDeltaTime*1000:F1}ms");
        }

        private void OnClientConnected(int clientId)
        {
            Debug.Log($"[ServerGameLoop] Client {clientId} connected");

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

            Debug.Log($"[ServerGameLoop] SERVER SPAWN: Player {player.Id}, ClientId={clientId}, Team={teamId}, Pos={spawnPos:F3}, ServerTick={spawnTick}, SimPlayer.Pos={player.Transform.Position:F3}");
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

            Debug.Log($"[ServerGameLoop] Sent spawn events for player {playerId} to client {clientId}");
        }

        private void OnClientDisconnected(int clientId)
        {
            Debug.Log($"[ServerGameLoop] Client {clientId} disconnected");

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

            // Unregister from command buffer and AOI
            _commandBuffer.UnregisterClient(clientId);
            _aoiManager.UnregisterClient(clientId);
        }

        /// <summary>
        /// Handle incoming game command.
        /// Commands are applied immediately to the simulation.
        /// </summary>
        private void OnCommandReceived(int clientId, GameCommand cmd)
        {
            var player = _simWorld.GetPlayerByClient(clientId);
            uint serverTick = _simWorld.Clock.CurrentTick;

            var dir = cmd.GetDirection();

            // Never trust client: clamp direction magnitude to 1
            if (dir.sqrMagnitude > 1f)
            {
                dir = dir.normalized;
            }

            DebugLogger.LogThrottled(DebugLogger.Category.Network,
                $"Command from client {clientId}: Cat={cmd.Category}, Action={cmd.Action}, Seq={cmd.Sequence}, ServerTick={serverTick}",
                frameInterval: 30);

            // Convert to SimCommand and execute
            var simCmd = CommandHelper.ToSimCommand(cmd, serverTick);
            _simWorld.ExecuteCommand(clientId, simCmd);

            // Track for acknowledgment
            _commandBuffer.AckCommand(clientId, cmd.Sequence);

            // Reset movement watchdog timer
            if (player != null && cmd.Category == CommandCategory.Movement)
            {
                player.TimeSinceLastMoveCmd = 0f;
            }
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

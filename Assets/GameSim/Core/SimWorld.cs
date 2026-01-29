// SimWorld.cs - Main simulation world containing all entities
// PURE C# - No FishNet dependencies
// Supports both input-based (legacy) and command-based simulation

using System;
using System.Collections.Generic;
using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.GameSim.Input;
using MOBANet.GameSim.Commands;


namespace MOBANet.GameSim.Core
{
    /// <summary>
    /// Main simulation world.
    /// Contains all entities and handles simulation stepping.
    /// Used by both server (authoritative) and client (prediction).
    /// Supports both legacy input-based and new command-based systems.
    /// </summary>
    public class SimWorld
    {
        #region Properties

        /// <summary>
        /// Simulation tick clock
        /// </summary>
        public TickClock Clock { get; }

        /// <summary>
        /// Simulation configuration
        /// </summary>
        public SimConfig Config { get; }

        /// <summary>
        /// Is this the authoritative (server) world?
        /// </summary>
        public bool IsServer { get; set; }

        /// <summary>
        /// Command dispatcher for command-based system
        /// </summary>
        public CommandDispatcher CommandDispatcher { get; }

        #endregion

        #region Entity Storage

        private readonly Dictionary<uint, SimEntity> _entities = new();
        private readonly Dictionary<int, SimPlayer> _playersByClient = new();
        private readonly List<uint> _entitiesToRemove = new();
        private readonly List<SimEntity> _entitiesToAdd = new();

        // Spawn positions (injected from server or received from network)
        private Vector3[] _team1Spawns;
        private Vector3[] _team2Spawns;

        #endregion

        #region Events

        /// <summary>
        /// Fired when an entity is spawned
        /// </summary>
        public event Action<SimEntity> OnEntitySpawned;

        /// <summary>
        /// Fired when an entity is destroyed
        /// </summary>
        public event Action<uint, EntityType> OnEntityDestroyed;

        /// <summary>
        /// Fired when a player takes damage
        /// </summary>
#pragma warning disable 0067 // Event never used (reserved for future combat system)
        public event Action<SimPlayer, int, uint> OnPlayerDamaged; // player, damage, sourceId

        /// <summary>
        /// Fired when a player dies
        /// </summary>
        public event Action<SimPlayer, uint> OnPlayerDied; // player, killerId
#pragma warning restore 0067

        #endregion

        #region Constructor

        /// <summary>
        /// Create a new simulation world
        /// </summary>
        public SimWorld(SimConfig config = null)
        {
            Config = config ?? SimConfig.Default;
            Clock = new TickClock();
            CommandDispatcher = new CommandDispatcher(this, Config);
        }

        #endregion

        #region Entity Management

        /// <summary>
        /// Spawn a new player
        /// </summary>
        public SimPlayer SpawnPlayer(int clientId, Vector3 position, byte teamId)
        {
            // Auto-generate entity ID (server-side spawning)
            var player = new SimPlayer(clientId)
            {
                TeamId = teamId,
                SpawnTick = Clock.CurrentTick
            };

            // Set initial position via Transform state
            player.Transform.Position = position;

            _entities[player.Id] = player;
            _playersByClient[clientId] = player;

            OnEntitySpawned?.Invoke(player);

            return player;
        }

        /// <summary>
        /// Spawn a player with specific ID (for client-side sync)
        /// </summary>
        public SimPlayer SpawnPlayer(uint entityId, int clientId, Vector3 position, byte teamId)
        {
            var player = new SimPlayer(entityId, clientId)
            {
                TeamId = teamId,
                SpawnTick = Clock.CurrentTick
            };

            // Set initial position via Transform state
            player.Transform.Position = position;

            _entities[player.Id] = player;
            _playersByClient[clientId] = player;

            OnEntitySpawned?.Invoke(player);

            return player;
        }

        /// <summary>
        /// Mark an entity for removal (deferred until end of tick)
        /// </summary>
        public void DestroyEntity(uint entityId)
        {
            if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.IsMarkedForRemoval = true;
                _entitiesToRemove.Add(entityId);
                OnEntityDestroyed?.Invoke(entityId, entity.Type);
            }
        }

        /// <summary>
        /// Get player by client ID
        /// </summary>
        public SimPlayer GetPlayerByClient(int clientId)
        {
            return _playersByClient.TryGetValue(clientId, out var player) ? player : null;
        }

        /// <summary>
        /// Get entity by ID
        /// </summary>
        public SimEntity GetEntity(uint entityId)
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity : null;
        }

        /// <summary>
        /// Get entity as specific type
        /// </summary>
        public T GetEntity<T>(uint entityId) where T : SimEntity
        {
            return _entities.TryGetValue(entityId, out var entity) ? entity as T : null;
        }

        /// <summary>
        /// Check if entity exists
        /// </summary>
        public bool HasEntity(uint entityId)
        {
            return _entities.ContainsKey(entityId);
        }

        /// <summary>
        /// All entities in the world
        /// </summary>
        public IEnumerable<SimEntity> AllEntities => _entities.Values;

        /// <summary>
        /// All players in the world
        /// </summary>
        public IEnumerable<SimPlayer> AllPlayers => _playersByClient.Values;

        /// <summary>
        /// Number of entities
        /// </summary>
        public int EntityCount => _entities.Count;

        /// <summary>
        /// Number of players
        /// </summary>
        public int PlayerCount => _playersByClient.Count;

        #endregion

        #region Simulation - Server

        /// <summary>
        /// SERVER: Step simulation with all client inputs.
        /// This is the authoritative simulation.
        /// </summary>
        public void Step(Dictionary<int, SimInput> clientInputs)
        {
            // Pre-tick (store previous state)
            foreach (var entity in _entities.Values)
            {
                // PreTick removed - no longer needed in component-based architecture;
            }

            // Input is now applied via command handlers (MovementHandler, etc.)
            // ApplyInput() removed - input state managed through GameCommand system

            // Tick all entities
            foreach (var entity in _entities.Values)
            {
                if (!entity.IsMarkedForRemoval)
                {
                    entity.Tick(Clock.TickDelta, Config);

                    // Auto-attack is now ticked inside SimPlayer.Tick() via TickCombat()
                    // TickAutoAttack() removed - combat logic integrated in component-based architecture
                }
            }

            // Process collisions/interactions
            ProcessCollisions();

            // Add pending entities
            foreach (var entity in _entitiesToAdd)
            {
                _entities[entity.Id] = entity;
                OnEntitySpawned?.Invoke(entity);
            }
            _entitiesToAdd.Clear();

            // Remove destroyed entities
            ProcessRemovals();

            // Advance tick
            Clock.Advance();
        }

        #endregion

        #region Simulation - Client (Prediction)

        /// <summary>
        /// CLIENT: Step with local input only (for prediction).
        /// Only simulates the local player, other entities use interpolation.
        /// </summary>
        public void StepLocal(int localClientId, in SimInput input)
        {
            // Pre-tick
            foreach (var entity in _entities.Values)
            {
                // PreTick removed - no longer needed in component-based architecture;
            }

            // Input is now applied via command handlers
            // ApplyInput() removed - input state managed through GameCommand system

            // Tick all entities (for physics consistency)
            foreach (var entity in _entities.Values)
            {
                if (!entity.IsMarkedForRemoval)
                {
                    entity.Tick(Clock.TickDelta, Config);
                }
            }

            // Process removals
            ProcessRemovals();

            // Advance tick
            Clock.Advance();
        }

        /// <summary>
        /// CLIENT: Step prediction for local player only (minimal version)
        /// </summary>
        public void StepPrediction(int localClientId, in SimInput input)
        {
            if (_playersByClient.TryGetValue(localClientId, out var localPlayer))
            {
                // PreTick and ApplyInput removed - input handled via commands
                localPlayer.Tick(Clock.TickDelta, Config);
            }

            Clock.Advance();
        }

        #endregion

        #region Command-Based Simulation

        /// <summary>
        /// Execute a command for a player.
        /// Called when a GameCommand is received from the network.
        /// Commands are applied immediately (not queued per tick).
        /// </summary>
        public bool ExecuteCommand(int clientId, in SimCommand cmd)
        {
            if (_playersByClient.TryGetValue(clientId, out var player))
            {
                player.LastCommandSeq = cmd.Sequence;
                return CommandDispatcher.Execute(player, cmd);
            }
            return false;
        }

        /// <summary>
        /// COMMAND-BASED: Step simulation without per-tick inputs.
        /// Commands are applied via ExecuteCommand() as they arrive.
        /// This just advances the physics/state.
        /// </summary>
        public void Step()
        {
            // Pre-tick (store previous state)
            foreach (var entity in _entities.Values)
            {
                // PreTick removed - no longer needed in component-based architecture;
            }

            // Tick all entities (they use their internal state from commands)
            foreach (var entity in _entities.Values)
            {
                if (!entity.IsMarkedForRemoval)
                {
                    entity.Tick(Clock.TickDelta, Config);
                }
            }

            // Process collisions/interactions
            ProcessCollisions();

            // Add pending entities
            foreach (var entity in _entitiesToAdd)
            {
                _entities[entity.Id] = entity;
                OnEntitySpawned?.Invoke(entity);
            }
            _entitiesToAdd.Clear();

            // Remove destroyed entities
            ProcessRemovals();

            // Advance tick
            Clock.Advance();
        }

        /// <summary>
        /// CLIENT COMMAND-BASED: Step prediction for local player only.
        /// Used after applying local command prediction.
        /// </summary>
        public void StepPrediction(int localClientId)
        {
            if (_playersByClient.TryGetValue(localClientId, out var localPlayer))
            {
                // PreTick removed - no longer needed in component-based architecture
                localPlayer.Tick(Clock.TickDelta, Config);
            }

            Clock.Advance();
        }

        #endregion

        #region Collision Processing

        /// <summary>
        /// Process entity collisions (simplified)
        /// </summary>
        private void ProcessCollisions()
        {
            // TODO: Implement spatial partitioning for 50v50 scale
            // For now, brute force for prototype

            // Example: Player vs Player collision
            var players = new List<SimPlayer>(_playersByClient.Values);
            for (int i = 0; i < players.Count; i++)
            {
                for (int j = i + 1; j < players.Count; j++)
                {
                    var a = players[i];
                    var b = players[j];

                    if (!a.Stats.IsAlive || !b.Stats.IsAlive) continue;

                    // Calculate distance using Transform state
                    float dist = Vector3.Distance(a.Transform.Position, b.Transform.Position);
                    const float playerRadius = 0.5f;

                    if (dist < playerRadius * 2)
                    {
                        // Simple push-apart
                        Vector3 dir = (b.Transform.Position - a.Transform.Position).normalized;
                        float overlap = (playerRadius * 2) - dist;
                        a.Transform.Position -= dir * (overlap * 0.5f);
                        b.Transform.Position += dir * (overlap * 0.5f);
                    }
                }
            }
        }

        #endregion

        #region Entity Removal

        private void ProcessRemovals()
        {
            foreach (var id in _entitiesToRemove)
            {
                if (_entities.TryGetValue(id, out var entity))
                {
                    if (entity is SimPlayer player)
                    {
                        _playersByClient.Remove(player.OwnerClientId);
                    }
                    _entities.Remove(id);
                }
            }
            _entitiesToRemove.Clear();
        }

        #endregion

        // Note: Snapshot creation/application moved to NetAdapter layer
        // See: MOBANet.NetAdapter.SnapshotHelper

        #region World Management

        /// <summary>
        /// Clear all entities and reset world
        /// </summary>
        public void Clear()
        {
            _entities.Clear();
            _playersByClient.Clear();
            _entitiesToRemove.Clear();
            _entitiesToAdd.Clear();
            Clock.Reset();
            SimEntity.ResetIdCounter();
        }

        /// <summary>
        /// Set custom spawn positions for teams.
        /// Call this on server to configure spawn points.
        /// Call this on client after receiving MatchConfig event.
        /// </summary>
        public void SetSpawnPositions(Vector3[] team1Spawns, Vector3[] team2Spawns)
        {
            _team1Spawns = team1Spawns;
            _team2Spawns = team2Spawns;
        }

        /// <summary>
        /// Get spawn position for a team.
        /// Uses custom spawn arrays if configured, otherwise falls back to algorithmic positions.
        /// </summary>
        public Vector3 GetSpawnPosition(byte teamId, int playerIndex)
        {
            // Use custom spawn positions if configured
            if (_team1Spawns != null && _team2Spawns != null)
            {
                var spawns = teamId == 1 ? _team1Spawns : _team2Spawns;
                if (spawns.Length > 0)
                {
                    return spawns[playerIndex % spawns.Length];
                }
            }

            // Fallback to algorithmic spawn positions (legacy)
            float baseX = teamId == 1 ? Config.Team1SpawnX : Config.Team2SpawnX;
            float offsetZ = (playerIndex % 5 - 2) * Config.SpawnSpread;
            return new Vector3(baseX, 0, offsetZ);
        }

        #endregion
    }
}

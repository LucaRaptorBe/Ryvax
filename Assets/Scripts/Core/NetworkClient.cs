// NetworkClient.cs - Gère connexion réseau, snapshots, spawning (V5.0 Simplified)
// Architecture: Network layer (UnityView)
//
// V5.0: Simplified architecture - no interpolation/prediction
// - Server authoritative: position = last snapshot position
// - Dead-reckoning between snapshots for smooth movement

using System.Collections.Generic;
using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter;
using MOBANet.NetAdapter.Messages;
using MOBANet.NetAdapter.FishNet;
using MOBANet.Shared;
using MOBANet.UnityView.Entities;
using MOBANet.UnityView.Input;
using MOBANet.Client.Input;
using MOBANet.GameSim.Commands;
using MOBANet.Diagnostics;
using MOBANet.Client.Animation;

// Alias to avoid ambiguity with UnityEngine.EventType
using NetEventType = MOBANet.NetAdapter.Messages.EventType;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// Gère connexion réseau, snapshots, spawning.
    /// V5.0: Simplified - no interpolation system, direct server positions.
    /// </summary>
    public class NetworkClient : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private FishNetAdapter _netAdapter;
        [SerializeField] private string _serverAddress = "127.0.0.1";
        [SerializeField] private ushort _serverPort = 7777;
        [SerializeField] private bool _autoConnect = false;

        [Header("Input")]
        [SerializeField] private float _inputSendRate = 120f; // Hz

        // State
        private SimWorld _simWorld;
        private Dictionary<uint, GameObject> _spawnedEntities = new();
        private Dictionary<uint, PlayerView> _playerViews = new();
        private Dictionary<uint, int> _entityOwners = new();
        private uint _localEntityId;
        private bool _isConnected;
        private bool _pendingLocalPlayerInit;

        // Local player input
        private InputCollector _localInputCollector;
        private float _inputSendAccumulator;
        private float _inputSendInterval;
        private uint _movementSeq;

        // Pending intent to send (from InputCollector)
        private InputIntent? _pendingIntent;

        // Input buffer for event commands (UDP redundancy)
        private InputBuffer _eventBuffer;
        private uint _eventSeq; // Centralized monotonic sequence for ALL event commands

        // Outgoing flush guard
        private int _lastOutgoingFlushFrame = -1;

        // V5.0: Local player view reference (dead-reckoning is now in EntityView)
        private PlayerView _localPlayerView;

        // Deferred spawn: local player waits for class selection before creating PlayerView
        private bool _localSpawnDeferred;

        public bool IsConnected => _isConnected;
        public int LocalClientId => _netAdapter?.LocalClientId ?? -1;
        public uint LocalEntityId => _localEntityId;
        public PlayerView LocalPlayerView => _localPlayerView;

        /// <summary>
        /// True when the local player entity exists but is waiting for class selection.
        /// The UI should show class selection when this becomes true.
        /// </summary>
        public bool IsWaitingForClassSelection => _localSpawnDeferred;

        /// <summary>
        /// Get the visual position for the local player.
        /// V5.0: Delegates to PlayerView (unified rendering path).
        /// </summary>
        public Vector3 GetVisualPosition() => _localPlayerView?.GetVisualPosition() ?? Vector3.zero;

        /// <summary>
        /// Whether we have received at least one snapshot.
        /// </summary>
        public bool HasVisualPosition => _localPlayerView != null;

        /// <summary>
        /// Get current simulation tick.
        /// </summary>
        public uint GetCurrentTick() => _simWorld?.Clock.CurrentTick ?? 0;

        /// <summary>
        /// Whether the local player is immobilized (CC).
        /// V5.0: Always returns false (CC tracking removed).
        /// </summary>
        public bool IsImmobilized() => false;

        /// <summary>
        /// Set local move intent (V5.0: no-op, feedback removed).
        /// </summary>
        public void SetLocalMoveIntent(Vector3 direction) { }

        /// <summary>
        /// Clear local move intent (V5.0: no-op, feedback removed).
        /// </summary>
        public void ClearLocalMoveIntent() { }

        void Awake()
        {
            _simWorld = new SimWorld { IsServer = false };
            _eventBuffer = new InputBuffer();
            _inputSendInterval = 1f / _inputSendRate;

            // METRIC B: Auto-setup ping measurement if not present
            var pingMeasure = GetComponent<NetworkPingMeasure>();
            if (pingMeasure == null)
            {
                pingMeasure = gameObject.AddComponent<NetworkPingMeasure>();
            }
            var field = typeof(NetworkPingMeasure).GetField("_networkClient",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(pingMeasure, this);
            }
        }

        void Start()
        {
            if (_autoConnect && _netAdapter != null)
            {
                Connect();
            }
        }

        void OnEnable()
        {
            if (_netAdapter != null)
            {
                _netAdapter.RegisterHandler<SnapshotDelta>(OnSnapshotReceived);
                _netAdapter.RegisterHandler<ReliableEvent>(OnEventReceived);
                _netAdapter.OnClientStarted += OnConnected;
                _netAdapter.OnDisconnected += OnDisconnected;
            }
        }

        void OnDisable()
        {
            if (_netAdapter != null)
            {
                _netAdapter.UnregisterHandler<SnapshotDelta>();
                _netAdapter.UnregisterHandler<ReliableEvent>();
                _netAdapter.OnClientStarted -= OnConnected;
                _netAdapter.OnDisconnected -= OnDisconnected;
            }
        }

        void Update()
        {
            if (!_isConnected) return;

            // Deferred local player initialization
            if (_pendingLocalPlayerInit && LocalClientId >= 0 && _localEntityId == 0)
            {
                TryInitializeLocalPlayer();
            }

            // Send pending intent and event commands
            SendInputUpdate();
        }

        void LateUpdate()
        {
            if (!_isConnected) return;

            // Flush outgoing packets
            int currentFrame = Time.frameCount;
            if (currentFrame != _lastOutgoingFlushFrame)
            {
                _netAdapter.ForceIterateOutgoing();
                _lastOutgoingFlushFrame = currentFrame;
            }
        }

        #region Intent-Based Movement

        /// <summary>
        /// Queue an InputIntent to be sent to the server.
        /// </summary>
        public void SendInputIntent(InputIntent intent)
        {
            _pendingIntent = intent;
        }

        private void SendInputUpdate()
        {
            if (_localEntityId == 0) return;

            _inputSendAccumulator += Time.deltaTime;
            if (_inputSendAccumulator < _inputSendInterval) return;
            _inputSendAccumulator -= _inputSendInterval;

            var eventCommands = _eventBuffer.GetRecentCommands(NetcodeConstants.INPUT_REDUNDANCY_COUNT);

            if (_pendingIntent.HasValue)
            {
                _movementSeq++;
            }

            var packet = CreateInputPacketFromIntent(_pendingIntent, eventCommands);
            _netAdapter.SendInputPacket(packet);

            MovementCycleLogger.LogIntentSent(packet.MovementSeq, packet.IntentType, packet.Payload0, packet.Payload1);

            _pendingIntent = null;
        }

        private InputPacket CreateInputPacketFromIntent(InputIntent? intent, GameCommand[] eventCommands)
        {
            uint clientTick = _simWorld?.Clock.CurrentTick ?? 0;

            if (!intent.HasValue)
            {
                return InputPacket.CreateEventsOnly(clientTick, eventCommands);
            }

            var i = intent.Value;

            switch (i.Type)
            {
                case InputIntentType.MoveDir:
                    return InputPacket.CreateMoveDir(clientTick, _movementSeq, i.Direction, eventCommands);

                case InputIntentType.MoveTo:
                    return InputPacket.CreateMoveTo(clientTick, _movementSeq, i.WorldPos, eventCommands);

                case InputIntentType.Stop:
                    return InputPacket.CreateStop(clientTick, _movementSeq, eventCommands);

                case InputIntentType.Follow:
                    return InputPacket.CreateFollow(clientTick, _movementSeq, i.TargetEntityId, eventCommands);

                default:
                    return InputPacket.CreateEventsOnly(clientTick, eventCommands);
            }
        }

        #endregion

        #region Event Commands

        public uint SendEventCommand(GameCommand cmd)
        {
            if (!_isConnected)
            {
                Debug.LogWarning($"[NetworkClient] SendEventCommand DROPPED (not connected): Cat={cmd.Category}, Action={cmd.Action}");
                return 0;
            }
            _eventSeq++;
            cmd.Sequence = _eventSeq;
            _eventBuffer.Add(cmd);
            Debug.Log($"[NetworkClient] SendEventCommand queued: Cat={cmd.Category}, Action={cmd.Action}, Seq={_eventSeq}, localEntityId={_localEntityId}");
            return _eventSeq;
        }

        public void SendCommand(GameCommand cmd)
        {
            if (cmd.Category == CommandCategory.Movement)
            {
                return;
            }
            SendEventCommand(cmd);
        }

        #endregion

        #region Connection

        public void Connect()
        {
            Connect(_serverAddress, _serverPort);
        }

        public void Connect(string address, ushort port)
        {
            if (_netAdapter == null)
            {
                Debug.LogError("[NetworkClient] NetAdapter not assigned!");
                return;
            }

            _serverAddress = address;
            _serverPort = port;
            _netAdapter.StartClient(address, port);
        }

        public void Disconnect()
        {
            _netAdapter?.Shutdown();
        }

        private void OnConnected()
        {
            _isConnected = true;
        }

        private void OnDisconnected()
        {
            _isConnected = false;
            _localEntityId = 0;
            _localInputCollector = null;
            _localPlayerView = null;
            _localSpawnDeferred = false;
            _eventSeq = 0;

            foreach (var go in _spawnedEntities.Values)
            {
                if (go != null) Destroy(go);
            }
            _spawnedEntities.Clear();
            _playerViews.Clear();
            _entityOwners.Clear();
            _simWorld.Clear();
        }

        #endregion

        #region Network Events

        private void OnSnapshotReceived(int _, SnapshotDelta snapshot)
        {
            float serverTime = TickClock.TickToTime(snapshot.ServerTick);

            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                ref readonly var state = ref snapshot.Entities[i];

                if (_playerViews.TryGetValue(state.EntityId, out var view))
                {
                    // Unified path: all players use the same logic (server-authoritative)
                    view.OnSnapshotReceived(state.Position, state.Velocity, state.Rotation);

                    // Update SimPlayer state for all players (local and remote)
                    var simPlayer = _simWorld.GetEntity(state.EntityId) as SimPlayer;
                    if (simPlayer != null)
                    {
                        simPlayer.ApplyNetworkState(state.Position, state.Velocity, state.Rotation);
                    }

                    // Log snapshots for debugging (local player only for clarity)
                    if (state.EntityId == _localEntityId)
                    {
                        MovementCycleLogger.LogSnapshotReceived(snapshot.ServerTick, state.Position, state.Velocity, serverTime);
                    }
                }
            }

            _eventBuffer.AcknowledgeUpTo(snapshot.AckInputSeq);
        }

        private void OnEventReceived(int _, ReliableEvent evt)
        {
            Debug.Log($"[NetworkClient] OnEventReceived: Type={evt.Type}, EntityId={evt.EntityId}, Data1={evt.Data1}");
            switch (evt.Type)
            {
                case NetEventType.EntitySpawn:
                    OnEntitySpawn(evt);
                    break;
                case NetEventType.EntityDeath:
                    OnEntityDeath(evt.EntityId);
                    break;
                case NetEventType.ClassAssign:
                    OnClassAssign(evt);
                    break;
                case NetEventType.Ping:
                    var pingMeasure = GetComponent<MOBANet.Diagnostics.NetworkPingMeasure>();
                    if (pingMeasure != null)
                    {
                        pingMeasure.OnPongReceived(evt.Data1);
                    }
                    break;
            }
        }

        #endregion

        #region Entity Management

        private void OnEntitySpawn(ReliableEvent evt)
        {
            int ownerClientId = ReliableEvent.DecodeOwnerClientId(evt);
            byte teamId = ReliableEvent.DecodeTeamId(evt);
            bool localIdKnown = LocalClientId >= 0;
            bool isLocal = localIdKnown && ownerClientId == LocalClientId;

            if (_playerViews.ContainsKey(evt.EntityId))
            {
                _entityOwners[evt.EntityId] = ownerClientId;
                if (!localIdKnown) _pendingLocalPlayerInit = true;
                return;
            }

            _entityOwners[evt.EntityId] = ownerClientId;

            var simPlayer = _simWorld.SpawnPlayer(evt.EntityId, ownerClientId, Vector3.zero, teamId);

            // LocalClientId not yet available: don't spawn any view, wait for TryInitializeLocalPlayer
            if (!localIdKnown)
            {
                _pendingLocalPlayerInit = true;
                return;
            }

            // Local player: defer PlayerView creation until class is selected
            if (isLocal)
            {
                _localEntityId = evt.EntityId;
                _localSpawnDeferred = true;
                Debug.Log("[NetworkClient] Local player entity created, waiting for class selection...");
                return;
            }

            // Remote players: instantiate immediately
            SpawnPlayerView(evt.EntityId, simPlayer, isLocal: false);
        }

        /// <summary>
        /// Instantiate the PlayerView prefab and wire everything up.
        /// Called immediately for remote players, deferred for local player until class is selected.
        /// </summary>
        private void SpawnPlayerView(uint entityId, SimPlayer simPlayer, bool isLocal)
        {
            if (GameManager.Instance == null || GameManager.Instance.PlayerPrefab == null)
            {
                Debug.LogError("[NetworkClient] No player prefab assigned in GameManager!");
                return;
            }

            GameObject prefab = GameManager.Instance.PlayerPrefab;
            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            go.name = isLocal ? "Player_Local" : $"Player_Remote_{entityId}";

            var view = go.GetComponent<PlayerView>();
            if (view != null)
            {
                view.Initialize(simPlayer, isLocal: isLocal, networkClient: this);
                _playerViews[entityId] = view;
            }

            _spawnedEntities[entityId] = go;

            if (isLocal)
            {
                _localPlayerView = view;

                _localInputCollector = go.GetComponent<InputCollector>();
                if (_localInputCollector != null)
                {
                    var field = typeof(InputCollector).GetField("_networkClient",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(_localInputCollector, this);
                    }
                }

                _localSpawnDeferred = false;
                GameManager.Instance?.OnNetworkPlayerSpawned(go, isLocalPlayer: true);
            }
        }

        private void OnClassAssign(ReliableEvent evt)
        {
            byte classId = ReliableEvent.DecodeClassId(evt);
            var simPlayer = _simWorld.GetEntity<SimPlayer>(evt.EntityId);
            Debug.Log($"[NetworkClient] OnClassAssign: entityId={evt.EntityId}, classId={classId}, simPlayer={simPlayer != null}");
            if (simPlayer == null)
            {
                Debug.LogWarning($"[NetworkClient] OnClassAssign ABORTED: simPlayer not found for entityId={evt.EntityId}");
                return;
            }

            simPlayer.ClassId = classId;

            bool isLocal = _entityOwners.TryGetValue(evt.EntityId, out int ownerId)
                           && ownerId == LocalClientId;

            Debug.Log($"[NetworkClient] OnClassAssign: isLocal={isLocal}, deferred={_localSpawnDeferred}, hasView={_playerViews.ContainsKey(evt.EntityId)}, ownerId={ownerId}, LocalClientId={LocalClientId}");

            // Local player with deferred spawn: now create the PlayerView
            if (isLocal && _localSpawnDeferred && !_playerViews.ContainsKey(evt.EntityId))
            {
                Debug.Log($"[NetworkClient] Class assigned ({classId}), spawning local PlayerView");
                SpawnPlayerView(evt.EntityId, simPlayer, isLocal: true);
                return;
            }

            // Existing player (remote or local already spawned): update class
            if (_playerViews.TryGetValue(evt.EntityId, out var view))
            {
                view.SetClass((CharacterClassType)classId);
            }
        }

        private void OnEntityDeath(uint entityId)
        {
            // TODO: Death handling
        }

        private void TryInitializeLocalPlayer()
        {
            // Find which entity belongs to us
            uint localId = 0;
            foreach (var kvp in _entityOwners)
            {
                if (kvp.Value == LocalClientId)
                {
                    localId = kvp.Key;
                    break;
                }
            }

            if (localId == 0) return; // Our entity not found yet

            _localEntityId = localId;
            _pendingLocalPlayerInit = false;

            // Local player: defer until class selected (don't spawn view)
            if (!_playerViews.ContainsKey(localId))
            {
                _localSpawnDeferred = true;
                Debug.Log("[NetworkClient] Local player identified (deferred), waiting for class selection...");
            }

            // Spawn views for remote players that were also delayed
            foreach (var kvp in _entityOwners)
            {
                uint entityId = kvp.Key;
                if (entityId == localId) continue;
                if (_playerViews.ContainsKey(entityId)) continue;

                var simPlayer = _simWorld.GetEntity<SimPlayer>(entityId);
                if (simPlayer != null)
                {
                    SpawnPlayerView(entityId, simPlayer, isLocal: false);
                }
            }
        }

        #endregion
    }
}

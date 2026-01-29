// NetworkClient.cs - Gère connexion réseau, snapshots, spawning
// AUCUN input - délégué à InputCollector
// Architecture: Network layer (UnityView)

using System.Collections.Generic;
using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter;
using MOBANet.NetAdapter.Messages;
using MOBANet.NetAdapter.FishNet;
using MOBANet.Shared;
using MOBANet.UnityView.Views;
using MOBANet.UnityView.Input;

// Alias to avoid ambiguity with UnityEngine.EventType
using NetEventType = MOBANet.NetAdapter.Messages.EventType;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// Gère connexion réseau, snapshots, spawning.
    /// Responsabilité: UNIQUEMENT réseau + spawning
    /// Architecture: Network layer (UnityView)
    /// </summary>
    public class NetworkClient : MonoBehaviour
    {
        [Header("Network")]
        [SerializeField] private FishNetAdapter _netAdapter;
        [SerializeField] private string _serverAddress = "127.0.0.1";
        [SerializeField] private ushort _serverPort = 7777;
        [SerializeField] private bool _autoConnect = false;

        [Header("Debug")]
        [SerializeField] private bool _directInterpolation = false;

        // State
        private SimWorld _simWorld;
        private Dictionary<uint, GameObject> _spawnedEntities = new();
        private Dictionary<uint, PlayerView> _playerViews = new();
        private Dictionary<uint, int> _entityOwners = new();
        private uint _localEntityId;
        private bool _isConnected;
        private bool _pendingLocalPlayerInit;

        // Network timing (PLL - Phase-Locked Loop)
        private float _perceivedServerTime = 0f;
        private float _newestSnapshotTime = 0f;

        public bool IsConnected => _isConnected;
        public int LocalClientId => _netAdapter?.LocalClientId ?? -1;
        public uint LocalEntityId => _localEntityId;
        public float GetPerceivedServerTime() => _perceivedServerTime;

        void Awake()
        {
            _simWorld = new SimWorld { IsServer = false };

            if (_netAdapter == null)
            {
                _netAdapter = FindAnyObjectByType<FishNetAdapter>();
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

            // PLL (Phase-Locked Loop) pour interpolation
            UpdatePerceivedServerTime();
        }

        private void UpdatePerceivedServerTime()
        {
            // Asservissement PLL - maintient offset constant entre perceived time et newest snapshot
            float offsetTarget = NetcodeConstants.INTERPOLATION_BUFFER_TICKS * TickClock.TICK_DELTA;
            float currentOffset = _newestSnapshotTime - _perceivedServerTime;
            float offsetError = currentOffset - offsetTarget;

            float playbackRate = 1.0f + NetcodeConstants.INTERPOLATION_PLL_GAIN * offsetError;
            playbackRate = Mathf.Clamp(playbackRate,
                NetcodeConstants.INTERPOLATION_PLAYBACK_MIN,
                NetcodeConstants.INTERPOLATION_PLAYBACK_MAX);

            _perceivedServerTime += Time.deltaTime * playbackRate;
        }

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

            Debug.Log($"[NetworkClient] Connecting to {address}:{port}");
        }

        public void Disconnect()
        {
            _netAdapter?.Shutdown();
        }

        private void OnConnected()
        {
            _isConnected = true;
            Debug.Log($"[NetworkClient] Connected as client {LocalClientId}");

            if (LocalClientId < 0)
            {
                Debug.LogWarning($"[NetworkClient] LocalClientId is INVALID ({LocalClientId})! Deferred initialization will retry.");
            }
        }

        private void OnDisconnected()
        {
            _isConnected = false;
            _localEntityId = 0;

            // Cleanup
            foreach (var go in _spawnedEntities.Values)
            {
                if (go != null) Destroy(go);
            }
            _spawnedEntities.Clear();
            _playerViews.Clear();
            _entityOwners.Clear();
            _simWorld.Clear();

            Debug.Log("[NetworkClient] Disconnected");
        }

        #endregion

        #region Commands

        public void SendCommand(GameCommand cmd)
        {
            if (!_isConnected) return;
            _netAdapter.SendToServer(cmd, reliable: true);
        }

        #endregion

        #region Network Events

        private void OnSnapshotReceived(int _, SnapshotDelta snapshot)
        {
            // Update timing
            _newestSnapshotTime = TickClock.TickToTime(snapshot.ServerTick);

            if (_perceivedServerTime == 0f)
            {
                float offsetTarget = NetcodeConstants.INTERPOLATION_BUFFER_TICKS * TickClock.TICK_DELTA;
                _perceivedServerTime = _newestSnapshotTime - offsetTarget;
            }

            // Feed interpolation buffers
            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                ref readonly var state = ref snapshot.Entities[i];

                if (_playerViews.TryGetValue(state.EntityId, out var view))
                {
                    view.AddInterpolationState(snapshot.ServerTick, state.Position, state.Rotation);

                    // Update SimPlayer
                    var simPlayer = _simWorld.GetEntity(state.EntityId) as SimPlayer;
                    if (simPlayer != null)
                    {
                        simPlayer.ApplyNetworkState(state.Position, state.Velocity, state.Rotation);
                    }
                }
            }
        }

        private void OnEventReceived(int _, ReliableEvent evt)
        {
            switch (evt.Type)
            {
                case NetEventType.EntitySpawn:
                    OnEntitySpawn(evt);
                    break;
                case NetEventType.EntityDeath:
                    OnEntityDeath(evt.EntityId);
                    break;
                // ... autres events
            }
        }

        #endregion

        #region Entity Management

        private void OnEntitySpawn(ReliableEvent evt)
        {
            int ownerClientId = ReliableEvent.DecodeOwnerClientId(evt);
            byte teamId = ReliableEvent.DecodeTeamId(evt);
            bool isLocal = ownerClientId == LocalClientId;

            Debug.Log($"[NetworkClient] Spawning entity {evt.EntityId} for client {ownerClientId}, team {teamId}, LocalClientId={LocalClientId}, isLocal={isLocal}");

            // Skip if already exists
            if (_playerViews.ContainsKey(evt.EntityId))
            {
                Debug.Log($"[NetworkClient] Entity {evt.EntityId} already exists, skipping spawn");
                _entityOwners[evt.EntityId] = ownerClientId;
                if (LocalClientId < 0) _pendingLocalPlayerInit = true;
                return;
            }

            // Track ownership
            _entityOwners[evt.EntityId] = ownerClientId;

            if (LocalClientId < 0)
            {
                _pendingLocalPlayerInit = true;
            }

            // Create SimPlayer
            var simPlayer = _simWorld.SpawnPlayer(evt.EntityId, ownerClientId, Vector3.zero, teamId);

            Debug.Log($"[NetworkClient] SimPlayer spawned: Entity {evt.EntityId}, IsLocal={isLocal}");

            // Get prefab from GameManager
            if (GameManager.Instance == null || GameManager.Instance.PlayerPrefab == null)
            {
                Debug.LogError("[NetworkClient] No player prefab assigned in GameManager!");
                return;
            }

            GameObject prefab = GameManager.Instance.PlayerPrefab;

            // Create GameObject
            var go = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            go.name = isLocal ? "Player_Local" : $"Player_Remote_{evt.EntityId}";

            // Initialize PlayerView
            var view = go.GetComponent<PlayerView>();
            if (view != null)
            {
                view.Initialize(simPlayer, isLocal: isLocal, networkClient: this);
                _playerViews[evt.EntityId] = view;
            }

            _spawnedEntities[evt.EntityId] = go;

            if (isLocal)
            {
                _localEntityId = evt.EntityId;

                // Assign NetworkClient to InputCollector for local player
                var inputCollector = go.GetComponent<InputCollector>();
                if (inputCollector != null)
                {
                    // Use reflection to set private field
                    var field = typeof(InputCollector).GetField("_networkClient",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(inputCollector, this);
                        Debug.Log("[NetworkClient] InputCollector assigned to local player");
                    }
                }

                GameManager.Instance?.OnNetworkPlayerSpawned(go, isLocalPlayer: true);
                Debug.Log($"[NetworkClient] Local player initialized: Entity {evt.EntityId}");
            }
        }

        private void OnEntityDeath(uint entityId)
        {
            Debug.Log($"[NetworkClient] Entity {entityId} died");
            // TODO: Death handling
        }

        private void TryInitializeLocalPlayer()
        {
            Debug.Log($"[NetworkClient] TryInitializeLocalPlayer - LocalClientId={LocalClientId}, _localEntityId={_localEntityId}");

            foreach (var kvp in _entityOwners)
            {
                uint entityId = kvp.Key;
                int ownerClientId = kvp.Value;

                if (ownerClientId == LocalClientId && _localEntityId == 0)
                {
                    Debug.Log($"[NetworkClient] Found local player entity {entityId}");

                    _localEntityId = entityId;

                    if (_playerViews.TryGetValue(entityId, out var view))
                    {
                        var simPlayer = _simWorld.GetEntity<SimPlayer>(entityId);
                        if (simPlayer != null)
                        {
                            view.Initialize(simPlayer, isLocal: true, networkClient: this);

                            // Assign NetworkClient to InputCollector
                            var inputCollector = view.GetComponent<InputCollector>();
                            if (inputCollector != null)
                            {
                                var field = typeof(InputCollector).GetField("_networkClient",
                                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (field != null)
                                {
                                    field.SetValue(inputCollector, this);
                                    Debug.Log("[NetworkClient] InputCollector assigned to local player (deferred)");
                                }
                            }

                            if (GameManager.Instance != null)
                            {
                                GameManager.Instance.OnNetworkPlayerSpawned(view.gameObject, isLocalPlayer: true);
                            }
                        }
                    }

                    _pendingLocalPlayerInit = false;
                    break;
                }
            }
        }

        #endregion
    }
}

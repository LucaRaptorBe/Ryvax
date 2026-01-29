// FishNetAdapter.cs - FishNet implementation of INetAdapter
// This is the ONLY file that should import FishNet namespaces
// To migrate to another library, only this file needs to be replaced

using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using MOBANet.Core;
using MOBANet.NetAdapter.Messages;

namespace MOBANet.NetAdapter.FishNet
{
    /// <summary>
    /// FishNet implementation of INetAdapter.
    /// Wraps FishNet's NetworkManager and provides abstracted networking.
    /// </summary>
    public class FishNetAdapter : MonoBehaviour, INetAdapter
    {
        #region Fields

        [Header("References")]
        [SerializeField] private NetworkManager _networkManager;

        private readonly Dictionary<Type, Delegate> _handlers = new();

        #endregion

        #region INetAdapter Properties

        public NetworkRole Role { get; private set; } = NetworkRole.None;

        public bool IsConnected => _networkManager != null &&
            (_networkManager.IsServerStarted || _networkManager.IsClientStarted);

        public int LocalClientId
        {
            get
            {
                if (_networkManager == null) return -1;

                // For Host mode, find the local client from server's client list
                if (Role == NetworkRole.Host && _networkManager.IsServerStarted)
                {
                    foreach (var client in _networkManager.ServerManager.Clients.Values)
                    {
                        if (client.IsLocalClient)
                            return client.ClientId;
                    }
                }

                // For regular client
                return _networkManager.ClientManager?.Connection?.ClientId ?? -1;
            }
        }

        #endregion

        #region Events

        public event Action<int> OnClientConnected;
        public event Action<int> OnClientDisconnected;
        public event Action OnServerStarted;
        public event Action OnClientStarted;
        public event Action OnDisconnected;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Find NetworkManager if not assigned
            if (_networkManager == null)
            {
                _networkManager = GetComponent<NetworkManager>();
            }
            if (_networkManager == null)
            {
                _networkManager = FindAnyObjectByType<NetworkManager>();
            }

            if (_networkManager == null)
            {
                Debug.LogError("[FishNetAdapter] NetworkManager not found! Please assign or add to scene.");
                return;
            }

            InitializeFishNet();
        }

        private void OnDestroy()
        {
            CleanupFishNet();
        }

        #endregion

        #region FishNet Initialization

        private void InitializeFishNet()
        {
            // Subscribe to FishNet connection events
            _networkManager.ServerManager.OnServerConnectionState += HandleServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;

            // Register broadcast handlers
            _networkManager.ServerManager.RegisterBroadcast<GameCommandBroadcast>(HandleServerReceiveCommand);
            _networkManager.ClientManager.RegisterBroadcast<SnapshotBroadcast>(HandleClientReceiveSnapshot);
            _networkManager.ClientManager.RegisterBroadcast<ReliableEventBroadcast>(HandleClientReceiveEvent);
            _networkManager.ClientManager.RegisterBroadcast<MatchConfigBroadcast>(HandleClientReceiveMatchConfig);

            Debug.Log("[FishNetAdapter] Initialized");
        }

        private void CleanupFishNet()
        {
            if (_networkManager == null) return;

            _networkManager.ServerManager.OnServerConnectionState -= HandleServerConnectionState;
            _networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;

            _networkManager.ServerManager.UnregisterBroadcast<GameCommandBroadcast>(HandleServerReceiveCommand);
            _networkManager.ClientManager.UnregisterBroadcast<SnapshotBroadcast>(HandleClientReceiveSnapshot);
            _networkManager.ClientManager.UnregisterBroadcast<ReliableEventBroadcast>(HandleClientReceiveEvent);
        }

        #endregion

        #region INetAdapter Lifecycle

        public void StartServer(ushort port)
        {
            if (_networkManager == null) return;

            Role = NetworkRole.Server;
            _networkManager.TransportManager.Transport.SetPort(port);
            _networkManager.ServerManager.StartConnection();

            Debug.Log($"[FishNetAdapter] Starting server on port {port}");
        }

        public void StartClient(string address, ushort port)
        {
            if (_networkManager == null) return;

            Role = NetworkRole.Client;
            _networkManager.TransportManager.Transport.SetClientAddress(address);
            _networkManager.TransportManager.Transport.SetPort(port);
            _networkManager.ClientManager.StartConnection();

            Debug.Log($"[FishNetAdapter] Connecting to {address}:{port}");
        }

        public void StartHost(ushort port)
        {
            if (_networkManager == null) return;

            Role = NetworkRole.Host;
            _networkManager.TransportManager.Transport.SetPort(port);
            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();

            Debug.Log($"[FishNetAdapter] Starting host on port {port}");
        }

        public void Shutdown()
        {
            if (_networkManager == null) return;

            if (_networkManager.IsServerStarted)
            {
                _networkManager.ServerManager.StopConnection(true);
            }
            if (_networkManager.IsClientStarted)
            {
                _networkManager.ClientManager.StopConnection();
            }

            Role = NetworkRole.None;
            Debug.Log("[FishNetAdapter] Shutdown");
        }

        #endregion

        #region INetAdapter Messaging

        public void SendToServer<T>(T message, bool reliable) where T : struct, INetMessage
        {
            if (!_networkManager.IsClientStarted) return;

            var channel = reliable ? Channel.Reliable : Channel.Unreliable;

            if (message is GameCommand gameCmd)
            {
                var broadcast = new GameCommandBroadcast { Command = gameCmd };
                _networkManager.ClientManager.Broadcast(broadcast, channel);
            }
        }

        public void SendToClient<T>(int clientId, T message, bool reliable) where T : struct, INetMessage
        {
            if (!_networkManager.IsServerStarted) return;

            var conn = GetConnection(clientId);
            if (conn == null)
            {
                // In Host mode, local client might not be in Clients dictionary
                // Check if this is the local client and we're in Host mode
                if (Role == NetworkRole.Host && clientId == LocalClientId)
                {
                    conn = _networkManager.ClientManager.Connection;
                }

                if (conn == null)
                {
                    Debug.LogWarning($"[FishNetAdapter] SendToClient: Connection not found for client {clientId}");
                    return;
                }
            }

            var channel = reliable ? Channel.Reliable : Channel.Unreliable;

            if (message is SnapshotDelta snapshot)
            {
                var broadcast = new SnapshotBroadcast { Snapshot = snapshot };
                _networkManager.ServerManager.Broadcast(conn, broadcast, false, channel);
            }
            else if (message is ReliableEvent evt)
            {
                var broadcast = new ReliableEventBroadcast { Event = evt };
                _networkManager.ServerManager.Broadcast(conn, broadcast, false, Channel.Reliable);
            }
        }

        public void SendToAll<T>(T message, bool reliable) where T : struct, INetMessage
        {
            if (!_networkManager.IsServerStarted) return;

            var channel = reliable ? Channel.Reliable : Channel.Unreliable;

            if (message is SnapshotDelta snapshot)
            {
                var broadcast = new SnapshotBroadcast { Snapshot = snapshot };
                _networkManager.ServerManager.Broadcast(broadcast, false, channel);
            }
            else if (message is ReliableEvent evt)
            {
                DebugLogger.Log(DebugLogger.Category.Network,
                    $"Server sending event to all: Type={evt.Type}, EntityId={evt.EntityId}");
                var broadcast = new ReliableEventBroadcast { Event = evt };
                _networkManager.ServerManager.Broadcast(broadcast, false, Channel.Reliable);
            }
        }

        public void RegisterHandler<T>(Action<int, T> handler) where T : struct, INetMessage
        {
            _handlers[typeof(T)] = handler;
        }

        public void UnregisterHandler<T>() where T : struct, INetMessage
        {
            _handlers.Remove(typeof(T));
        }

        #endregion

        #region Match Configuration (Broadcast)

        /// <summary>
        /// Send match configuration to a specific client (e.g., on connect)
        /// </summary>
        public void SendMatchConfigToClient(int clientId, Vector3[] team1Spawns, Vector3[] team2Spawns)
        {
            if (!_networkManager.IsServerStarted) return;

            var conn = GetConnection(clientId);
            if (conn == null)
            {
                Debug.LogWarning($"[FishNetAdapter] Cannot send MatchConfig: client {clientId} not found");
                return;
            }

            var broadcast = new MatchConfigBroadcast(team1Spawns, team2Spawns);
            _networkManager.ServerManager.Broadcast(conn, broadcast, true, Channel.Reliable);
        }

        /// <summary>
        /// Event fired when match configuration is received (client-side)
        /// </summary>
        public event Action<Vector3[], Vector3[]> OnMatchConfigReceived;

        #endregion

        #region FishNet Event Handlers

        private void HandleServerConnectionState(ServerConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    Debug.Log("[FishNetAdapter] Server started");
                    OnServerStarted?.Invoke();
                    break;

                case LocalConnectionState.Stopped:
                    Debug.Log("[FishNetAdapter] Server stopped");
                    if (Role == NetworkRole.Server)
                    {
                        OnDisconnected?.Invoke();
                    }
                    break;
            }
        }

        private void HandleClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Started:
                    Debug.Log($"[FishNetAdapter] Client connected, ID: {LocalClientId}");
                    OnClientStarted?.Invoke();
                    break;

                case LocalConnectionState.Stopped:
                    Debug.Log("[FishNetAdapter] Client disconnected");
                    if (Role == NetworkRole.Client)
                    {
                        OnDisconnected?.Invoke();
                    }
                    break;
            }
        }

        private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case RemoteConnectionState.Started:
                    Debug.Log($"[FishNetAdapter] Remote client {conn.ClientId} connected");
                    OnClientConnected?.Invoke(conn.ClientId);
                    break;

                case RemoteConnectionState.Stopped:
                    Debug.Log($"[FishNetAdapter] Remote client {conn.ClientId} disconnected");
                    OnClientDisconnected?.Invoke(conn.ClientId);
                    break;
            }
        }

        private void HandleServerReceiveCommand(NetworkConnection conn, GameCommandBroadcast broadcast, Channel channel)
        {
            if (_handlers.TryGetValue(typeof(GameCommand), out var handler))
            {
                ((Action<int, GameCommand>)handler)(conn.ClientId, broadcast.Command);
            }
        }

        private void HandleClientReceiveSnapshot(SnapshotBroadcast broadcast, Channel channel)
        {
            // ⚠️ TRÈS FRÉQUENT: 30 logs/sec! Throttlé à 1 toutes les 120 frames (~2 sec)
            DebugLogger.LogThrottled(DebugLogger.Category.Snapshot,
                $"Client received snapshot: Tick={broadcast.Snapshot.ServerTick}, Entities={broadcast.Snapshot.EntityCount}",
                frameInterval: 120);

            if (_handlers.TryGetValue(typeof(SnapshotDelta), out var handler))
            {
                ((Action<int, SnapshotDelta>)handler)(-1, broadcast.Snapshot);
            }
        }

        private void HandleClientReceiveEvent(ReliableEventBroadcast broadcast, Channel channel)
        {
            DebugLogger.Log(DebugLogger.Category.Network,
                $"Client received event: Type={broadcast.Event.Type}, EntityId={broadcast.Event.EntityId}");

            if (_handlers.TryGetValue(typeof(ReliableEvent), out var handler))
            {
                ((Action<int, ReliableEvent>)handler)(-1, broadcast.Event);
            }
            else
            {
                Debug.LogWarning("[FishNetAdapter] No handler registered for ReliableEvent!");
            }
        }

        private void HandleClientReceiveMatchConfig(MatchConfigBroadcast broadcast, Channel channel)
        {
            Debug.Log($"[FishNetAdapter] Client received MatchConfig: Team1Spawns={broadcast.Team1Spawns?.Length ?? 0}, Team2Spawns={broadcast.Team2Spawns?.Length ?? 0}");
            OnMatchConfigReceived?.Invoke(broadcast.Team1Spawns, broadcast.Team2Spawns);
        }

        #endregion

        #region Helpers

        private NetworkConnection GetConnection(int clientId)
        {
            if (_networkManager?.ServerManager?.Clients == null) return null;

            foreach (var conn in _networkManager.ServerManager.Clients.Values)
            {
                if (conn.ClientId == clientId)
                {
                    return conn;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all connected client IDs
        /// </summary>
        public IEnumerable<int> GetConnectedClients()
        {
            if (_networkManager?.ServerManager?.Clients == null)
                yield break;

            foreach (var conn in _networkManager.ServerManager.Clients.Values)
            {
                yield return conn.ClientId;
            }
        }

        /// <summary>
        /// Get number of connected clients
        /// </summary>
        public int ConnectedClientCount =>
            _networkManager?.ServerManager?.Clients?.Count ?? 0;

        #endregion
    }

    #region FishNet Broadcast Types

    /// <summary>
    /// FishNet broadcast wrapper for SnapshotDelta
    /// </summary>
    public struct SnapshotBroadcast : IBroadcast
    {
        public SnapshotDelta Snapshot;
    }

    /// <summary>
    /// FishNet broadcast wrapper for ReliableEvent
    /// </summary>
    public struct ReliableEventBroadcast : IBroadcast
    {
        public ReliableEvent Event;
    }

    /// <summary>
    /// FishNet broadcast wrapper for GameCommand (command-based system)
    /// </summary>
    public struct GameCommandBroadcast : IBroadcast
    {
        public GameCommand Command;
    }

    #endregion
}

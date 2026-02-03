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
using FishNet.Serializing;
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

        /// <summary>
        /// Called when MoveDir intent received (WASD movement).
        /// Parameters: clientId, movementSeq, direction (normalized XZ as Vector2), recvSocketTicks (monotonic timestamp)
        /// </summary>
        public event Action<int, uint, Vector2, long> OnMoveDirReceived;

        /// <summary>
        /// Called when MoveTo intent received (click movement).
        /// Parameters: clientId, movementSeq, targetPosition (world XZ as Vector2), recvSocketTicks (monotonic timestamp)
        /// </summary>
        public event Action<int, uint, Vector2, long> OnMoveToReceived;

        /// <summary>
        /// Called when Stop intent received.
        /// Parameters: clientId, movementSeq, recvSocketTicks (monotonic timestamp)
        /// </summary>
        public event Action<int, uint, long> OnStopReceived;

        /// <summary>
        /// Called when Follow intent received.
        /// Parameters: clientId, movementSeq, targetEntityId, recvSocketTicks (monotonic timestamp)
        /// </summary>
        public event Action<int, uint, uint, long> OnFollowReceived;

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
            _networkManager.ServerManager.RegisterBroadcast<InputPacketBroadcast>(HandleServerReceiveInputPacket);
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
            _networkManager.ServerManager.UnregisterBroadcast<InputPacketBroadcast>(HandleServerReceiveInputPacket);
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

        /// <summary>
        /// Send InputPacket to server via UDP unreliable.
        /// This is the preferred method for sending inputs to avoid TCP head-of-line blocking.
        /// </summary>
        public void SendInputPacket(InputPacket packet)
        {
            if (!_networkManager.IsClientStarted) return;

            // LOG A: Enqueue moment (before flush)
            long enqueueTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            // if (packet.MovementSeq > 0)
            // {
            //     Debug.Log($"[{UnityEngine.Time.time:F3}] [SEND ENQUEUE] C→S seq={packet.MovementSeq} intent={packet.IntentType} ticks={enqueueTicks}");
            // }

            var broadcast = new InputPacketBroadcast { Packet = packet };

            // DEBUG: Log BEFORE Broadcast call
            // if (packet.MovementSeq > 0)
            // {
            //     Debug.Log($"[{UnityEngine.Time.time:F3}] [BEFORE BROADCAST] seq={packet.MovementSeq} frame={UnityEngine.Time.frameCount}");
            // }

            // ALWAYS use Unreliable (UDP) for inputs to avoid head-of-line blocking
            _networkManager.ClientManager.Broadcast(broadcast, Channel.Unreliable);

            // DEBUG: Log AFTER Broadcast call
            // if (packet.MovementSeq > 0)
            // {
            //     Debug.Log($"[{UnityEngine.Time.time:F3}] [AFTER BROADCAST] seq={packet.MovementSeq} frame={UnityEngine.Time.frameCount}");
            // }

            // Note: Actual socket send happens later during IterateOutgoing (called by TimeManager)
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
                    _lastProcessedSeq[conn.ClientId] = 0; // Initialize sequence tracking
                    OnClientConnected?.Invoke(conn.ClientId);
                    break;

                case RemoteConnectionState.Stopped:
                    Debug.Log($"[FishNetAdapter] Remote client {conn.ClientId} disconnected");
                    _lastProcessedSeq.Remove(conn.ClientId); // Cleanup sequence tracking
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

        /// <summary>
        /// Track last processed sequence per client to skip duplicates from UDP redundancy.
        /// </summary>
        private readonly Dictionary<int, uint> _lastProcessedSeq = new();

        /// <summary>
        /// Handle InputPacket from client.
        /// V4: Decodes by IntentType and invokes type-specific events.
        /// Discrete events (jump, spell) are processed as commands with dedup.
        /// </summary>
        private void HandleServerReceiveInputPacket(NetworkConnection conn, InputPacketBroadcast broadcast, Channel channel)
        {
            var packet = broadcast.Packet;
            int clientId = conn.ClientId;

            // METRIC A: Capture monotonic timestamp at socket receive (earliest possible point)
            long recvSocketTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            // Version check: reject old packets
            if (packet.Version < InputPacket.MSG_VERSION)
            {
                Debug.LogWarning($"[FishNetAdapter] Rejected InputPacket v{packet.Version} from client {clientId} (expected v{InputPacket.MSG_VERSION})");
                return;
            }

            // LOG: Track exact moment FishNet delivers packet to handler
            // if (packet.MovementSeq > 0)
            // {
            //     Debug.Log($"[{UnityEngine.Time.time:F3}] [TRANSPORT RECV] C→S seq={packet.MovementSeq} intent={packet.IntentType} socketTicks={recvSocketTicks}");
            // }

            // Dispatch by IntentType with correct payload interpretation + monotonic timestamp
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

                case PacketIntentType.Follow:
                    OnFollowReceived?.Invoke(clientId, packet.MovementSeq, packet.GetFollowTargetId(), recvSocketTicks);
                    break;

                case PacketIntentType.None:
                    // No movement intent - only event commands
                    break;
            }

            // Get last processed sequence for this client (for discrete events only)
            if (!_lastProcessedSeq.TryGetValue(clientId, out uint lastSeq))
            {
                lastSeq = 0;
                _lastProcessedSeq[clientId] = 0;
            }

            // Process discrete event commands (jump, spell, attack) in sequence order
            if (packet.Commands == null || packet.Commands.Length == 0) return;

            int processedCount = 0;
            for (int i = 0; i < packet.Commands.Length; i++)
            {
                var cmd = packet.Commands[i];

                // Skip if already processed (due to UDP redundancy)
                if (cmd.Sequence <= lastSeq)
                {
                    continue;
                }

                // Process this command (only discrete events now)
                if (_handlers.TryGetValue(typeof(GameCommand), out var handler))
                {
                    ((Action<int, GameCommand>)handler)(clientId, cmd);
                }

                // Update last processed sequence
                if (cmd.Sequence > _lastProcessedSeq[clientId])
                {
                    _lastProcessedSeq[clientId] = cmd.Sequence;
                }
                processedCount++;
            }

            if (processedCount > 0)
            {
                DebugLogger.LogThrottled(DebugLogger.Category.Network,
                    $"InputPacket from client {clientId}: processed {processedCount}/{packet.CommandCount} events, lastSeq={_lastProcessedSeq[clientId]}",
                    frameInterval: 60);
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

        #region Manual Network Polling

        /// <summary>
        /// Force immediate polling of incoming network data.
        /// Call this at the START of FixedUpdate to ensure network data is available
        /// before simulation ticks, avoiding the Update/FixedUpdate phase mismatch.
        /// </summary>
        public void ForceIterateIncoming()
        {
            if (_networkManager == null) return;

            // Poll server incoming (from clients)
            if (_networkManager.IsServerStarted)
            {
                _networkManager.TransportManager.Transport.IterateIncoming(asServer: true);
            }

            // Poll client incoming (from server) - for Host mode
            if (_networkManager.IsClientStarted)
            {
                _networkManager.TransportManager.Transport.IterateIncoming(asServer: false);
            }
        }

        /// <summary>
        /// Force immediate flush of outgoing network data.
        /// Call this in Update to flush packets at frame rate instead of tick rate,
        /// eliminating the 30Hz tick gating delay (33ms → ~0ms).
        /// </summary>
        public void ForceIterateOutgoing()
        {
            // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Called frame={UnityEngine.Time.frameCount}");

            if (_networkManager == null)
            {
                // Debug.LogWarning($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] NetworkManager is NULL");
                return;
            }

            // Flush server outgoing (to clients)
            if (_networkManager.IsServerStarted)
            {
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Server - STEP 1: TransportManager.IterateOutgoing");
                // STEP 1: Flush PacketBundle → Transport
                _networkManager.TransportManager.IterateOutgoing(asServer: true);
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Server - STEP 2: Transport.IterateOutgoing");
                // STEP 2: Flush Transport → Socket
                _networkManager.TransportManager.Transport.IterateOutgoing(asServer: true);
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Server - COMPLETE");
            }

            // Flush client outgoing (to server)
            if (_networkManager.IsClientStarted)
            {
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Client - STEP 1: TransportManager.IterateOutgoing");
                // STEP 1: Flush PacketBundle → Transport
                _networkManager.TransportManager.IterateOutgoing(asServer: false);
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Client - STEP 2: Transport.IterateOutgoing");
                // STEP 2: Flush Transport → Socket
                _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
                // Debug.Log($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Client - COMPLETE");
            }
            else
            {
                // Debug.LogWarning($"[{UnityEngine.Time.time:F3}] [FORCE OUTGOING] Client NOT started");
            }
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

    /// <summary>
    /// FishNet broadcast wrapper for InputPacket (UDP with redundancy)
    /// </summary>
    public struct InputPacketBroadcast : IBroadcast
    {
        public InputPacket Packet;
    }

    #endregion
}

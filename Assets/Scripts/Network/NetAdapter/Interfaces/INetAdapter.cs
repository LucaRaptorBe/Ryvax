// INetAdapter.cs - Core network adapter interface
// This interface abstracts the networking library (FishNet/Mirror/NGO/etc.)
// GameSim and UnityView should ONLY depend on this interface, never on concrete implementations

using System;
using UnityEngine;

namespace MOBANet.NetAdapter
{
    /// <summary>
    /// Network role for the current instance
    /// </summary>
    public enum NetworkRole
    {
        None = 0,
        Server = 1,
        Client = 2,
        Host = 3  // Server + Client combined
    }

    /// <summary>
    /// Marker interface for network messages.
    /// All network messages must implement this for type-safe serialization.
    /// </summary>
    public interface INetMessage
    {
        /// <summary>
        /// Unique message type identifier (1-255)
        /// </summary>
        byte MessageId { get; }

        /// <summary>
        /// Message version for backwards compatibility
        /// </summary>
        ushort Version { get; }
    }

    /// <summary>
    /// Core network adapter interface.
    /// Abstracts all networking operations to allow library migration.
    /// </summary>
    public interface INetAdapter
    {
        #region State Properties

        /// <summary>
        /// Current network role (Server/Client/Host/None)
        /// </summary>
        NetworkRole Role { get; }

        /// <summary>
        /// True if connected (server started or client connected)
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Local client ID. -1 if server-only or not connected.
        /// </summary>
        int LocalClientId { get; }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Start as dedicated server on specified port
        /// </summary>
        void StartServer(ushort port);

        /// <summary>
        /// Connect to server as client
        /// </summary>
        void StartClient(string address, ushort port);

        /// <summary>
        /// Start as host (server + local client)
        /// </summary>
        void StartHost(ushort port);

        /// <summary>
        /// Shutdown all connections
        /// </summary>
        void Shutdown();

        #endregion

        #region Messaging

        /// <summary>
        /// Send message from client to server
        /// </summary>
        void SendToServer<T>(T message, bool reliable) where T : struct, INetMessage;

        /// <summary>
        /// Send message from server to specific client
        /// </summary>
        void SendToClient<T>(int clientId, T message, bool reliable) where T : struct, INetMessage;

        /// <summary>
        /// Broadcast message from server to all clients
        /// </summary>
        void SendToAll<T>(T message, bool reliable) where T : struct, INetMessage;

        /// <summary>
        /// Send match configuration to a specific client (spawn positions, etc.)
        /// Called when a client connects to synchronize match settings.
        /// </summary>
        void SendMatchConfigToClient(int clientId, Vector3[] team1Spawns, Vector3[] team2Spawns);

        #endregion

        #region Events

        /// <summary>
        /// Fired when a client connects (server-side). Param: clientId
        /// </summary>
        event Action<int> OnClientConnected;

        /// <summary>
        /// Fired when a client disconnects (server-side). Param: clientId
        /// </summary>
        event Action<int> OnClientDisconnected;

        /// <summary>
        /// Fired when server has started
        /// </summary>
        event Action OnServerStarted;

        /// <summary>
        /// Fired when client has connected to server
        /// </summary>
        event Action OnClientStarted;

        /// <summary>
        /// Fired when disconnected (client or server shutdown)
        /// </summary>
        event Action OnDisconnected;

        #endregion

        #region Message Handlers

        /// <summary>
        /// Register handler for specific message type.
        /// Handler receives (senderClientId, message).
        /// senderClientId is -1 for server->client messages.
        /// </summary>
        void RegisterHandler<T>(Action<int, T> handler) where T : struct, INetMessage;

        /// <summary>
        /// Unregister handler for specific message type
        /// </summary>
        void UnregisterHandler<T>() where T : struct, INetMessage;

        #endregion
    }
}

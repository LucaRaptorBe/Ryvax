// ServerCommandBuffer.cs - Tracks command acknowledgment for clients

using System;
using System.Collections.Generic;

namespace MOBANet.NetAdapter.Buffers
{
    /// <summary>
    /// Tracks the last acknowledged command sequence for each client.
    /// Used by server to include AckCommandSeq in snapshots.
    /// Clients use this to know which commands have been processed.
    /// </summary>
    public class ServerCommandBuffer
    {
        private readonly Dictionary<int, uint> _lastAckSeq = new();

        /// <summary>
        /// Register a new client
        /// </summary>
        public void RegisterClient(int clientId)
        {
            _lastAckSeq[clientId] = 0;
        }

        /// <summary>
        /// Unregister a client
        /// </summary>
        public void UnregisterClient(int clientId)
        {
            _lastAckSeq.Remove(clientId);
        }

        /// <summary>
        /// Acknowledge a command sequence for a client.
        /// Only updates if the new sequence is higher (handles out-of-order).
        /// </summary>
        public void AckCommand(int clientId, uint seq)
        {
            if (_lastAckSeq.TryGetValue(clientId, out uint current))
            {
                _lastAckSeq[clientId] = Math.Max(current, seq);
            }
        }

        /// <summary>
        /// Get the last acknowledged command sequence for a client.
        /// </summary>
        public uint GetLastAckSeq(int clientId)
        {
            return _lastAckSeq.TryGetValue(clientId, out var seq) ? seq : 0;
        }

        /// <summary>
        /// Check if a client is registered
        /// </summary>
        public bool HasClient(int clientId)
        {
            return _lastAckSeq.ContainsKey(clientId);
        }

        /// <summary>
        /// Clear all clients
        /// </summary>
        public void Clear()
        {
            _lastAckSeq.Clear();
        }

        /// <summary>
        /// Number of registered clients
        /// </summary>
        public int ClientCount => _lastAckSeq.Count;
    }
}

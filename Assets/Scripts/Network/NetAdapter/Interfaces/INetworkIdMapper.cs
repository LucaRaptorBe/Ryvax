// INetworkIdMapper.cs - Maps between simulation entity IDs and network IDs
// Needed because FishNet (and other libs) use their own ID system

using System.Collections.Generic;

namespace MOBANet.NetAdapter
{
    /// <summary>
    /// Maps between simulation entity IDs and network-specific IDs.
    /// Required because networking libraries use their own ID systems.
    /// </summary>
    public interface INetworkIdMapper
    {
        /// <summary>
        /// Get network ID for a simulation entity
        /// </summary>
        /// <param name="simEntityId">Simulation entity ID</param>
        /// <returns>Network ID, or 0 if not found</returns>
        uint SimIdToNetId(uint simEntityId);

        /// <summary>
        /// Get simulation entity ID for a network object
        /// </summary>
        /// <param name="networkId">Network object ID</param>
        /// <returns>Simulation entity ID, or 0 if not found</returns>
        uint NetIdToSimId(uint networkId);

        /// <summary>
        /// Register a mapping between simulation and network IDs
        /// </summary>
        void RegisterMapping(uint simEntityId, uint networkId);

        /// <summary>
        /// Remove mapping for a simulation entity
        /// </summary>
        void UnregisterMapping(uint simEntityId);

        /// <summary>
        /// Clear all mappings
        /// </summary>
        void Clear();

        /// <summary>
        /// Check if a simulation entity ID has a mapping
        /// </summary>
        bool HasSimId(uint simEntityId);

        /// <summary>
        /// Check if a network ID has a mapping
        /// </summary>
        bool HasNetId(uint networkId);
    }

    /// <summary>
    /// Default implementation using dictionaries.
    /// Thread-safe for read operations.
    /// </summary>
    public class NetworkIdMapper : INetworkIdMapper
    {
        private readonly Dictionary<uint, uint> _simToNet = new();
        private readonly Dictionary<uint, uint> _netToSim = new();
        private readonly object _lock = new();

        public uint SimIdToNetId(uint simEntityId)
        {
            lock (_lock)
            {
                return _simToNet.TryGetValue(simEntityId, out var netId) ? netId : 0;
            }
        }

        public uint NetIdToSimId(uint networkId)
        {
            lock (_lock)
            {
                return _netToSim.TryGetValue(networkId, out var simId) ? simId : 0;
            }
        }

        public void RegisterMapping(uint simEntityId, uint networkId)
        {
            lock (_lock)
            {
                _simToNet[simEntityId] = networkId;
                _netToSim[networkId] = simEntityId;
            }
        }

        public void UnregisterMapping(uint simEntityId)
        {
            lock (_lock)
            {
                if (_simToNet.TryGetValue(simEntityId, out var networkId))
                {
                    _simToNet.Remove(simEntityId);
                    _netToSim.Remove(networkId);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _simToNet.Clear();
                _netToSim.Clear();
            }
        }

        public bool HasSimId(uint simEntityId)
        {
            lock (_lock)
            {
                return _simToNet.ContainsKey(simEntityId);
            }
        }

        public bool HasNetId(uint networkId)
        {
            lock (_lock)
            {
                return _netToSim.ContainsKey(networkId);
            }
        }
    }
}

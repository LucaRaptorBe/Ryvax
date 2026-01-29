// AOIManager.cs - Area of Interest management for 30v30 scale

using System;
using System.Collections.Generic;
using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;

namespace MOBANet.NetAdapter.AOI
{
    /// <summary>
    /// Manages Area of Interest for each client.
    /// Tracks which entities are visible to each player.
    /// Uses hysteresis to prevent flickering at boundaries.
    ///
    /// For 30v30: Reduces entities per snapshot from 60 to ~20.
    /// </summary>
    public class AOIManager
    {
        private readonly SpatialHashGrid _grid;
        private readonly float _visionRadius;
        private readonly float _hysteresis;
        private readonly Dictionary<int, HashSet<uint>> _clientVisible = new();
        private readonly List<uint> _queryBuffer = new(128);

        /// <summary>
        /// Create an AOI manager.
        /// </summary>
        /// <param name="visionRadius">Radius at which entities become visible.</param>
        /// <param name="hysteresis">Extra radius before entities leave visibility (prevents flicker).</param>
        /// <param name="cellSize">Spatial grid cell size (should be > visionRadius).</param>
        public AOIManager(float visionRadius = 30f, float hysteresis = 5f, float cellSize = 20f)
        {
            _visionRadius = visionRadius;
            _hysteresis = hysteresis;
            _grid = new SpatialHashGrid(cellSize);
        }

        /// <summary>
        /// Register a new client for AOI tracking.
        /// </summary>
        public void RegisterClient(int clientId)
        {
            _clientVisible[clientId] = new HashSet<uint>();
        }

        /// <summary>
        /// Unregister a client.
        /// </summary>
        public void UnregisterClient(int clientId)
        {
            _clientVisible.Remove(clientId);
        }

        /// <summary>
        /// Update an entity's position in the spatial grid.
        /// </summary>
        public void UpdateEntityPosition(uint entityId, Vector3 position)
        {
            _grid.UpdateEntity(entityId, position);
        }

        /// <summary>
        /// Remove an entity from the spatial grid.
        /// </summary>
        public void RemoveEntity(uint entityId)
        {
            _grid.RemoveEntity(entityId);

            // Remove from all clients' visible sets
            foreach (var visible in _clientVisible.Values)
            {
                visible.Remove(entityId);
            }
        }

        /// <summary>
        /// Update AOI for a client and return entities that entered/left visibility.
        /// </summary>
        /// <param name="clientId">The client to update.</param>
        /// <param name="viewerPos">The viewer's current position.</param>
        /// <param name="world">Simulation world to get entity data.</param>
        /// <returns>Lists of entity IDs that entered and left visibility.</returns>
        public (List<uint> entered, List<uint> left) UpdateClientAOI(
            int clientId, Vector3 viewerPos, SimWorld world)
        {
            var entered = new List<uint>();
            var left = new List<uint>();

            if (!_clientVisible.TryGetValue(clientId, out var visible))
                return (entered, left);

            // Query spatial grid for nearby entities (with hysteresis margin)
            _grid.GetEntitiesInRadius(viewerPos, _visionRadius + _hysteresis, _queryBuffer);

            // Calculate which entities are now visible
            var nowVisible = new HashSet<uint>();
            float innerRadiusSq = _visionRadius * _visionRadius;
            float outerRadiusSq = (_visionRadius + _hysteresis) * (_visionRadius + _hysteresis);

            foreach (var entityId in _queryBuffer)
            {
                var entity = world.GetEntity(entityId);
                if (entity == null) continue;

                // Component-based states: extract position from specific entity type
                Vector3 entityPos = Vector3.zero;
                if (entity is SimPlayer player)
                {
                    entityPos = player.Transform.Position;
                }
                else
                {
                    // TODO: Add other entity types when needed
                    continue;
                }

                float distSq = (entityPos - viewerPos).sqrMagnitude;
                bool wasVisible = visible.Contains(entityId);

                // Hysteresis logic:
                // - To ENTER visibility: must be within inner radius
                // - To LEAVE visibility: must be outside outer radius
                bool isVisible;
                if (wasVisible)
                {
                    // Already visible - keep visible until outside outer radius
                    isVisible = distSq <= outerRadiusSq;
                }
                else
                {
                    // Not visible - only become visible within inner radius
                    isVisible = distSq <= innerRadiusSq;
                }

                if (isVisible)
                {
                    nowVisible.Add(entityId);
                    if (!wasVisible)
                    {
                        entered.Add(entityId);
                    }
                }
            }

            // Find entities that left visibility
            foreach (var entityId in visible)
            {
                if (!nowVisible.Contains(entityId))
                {
                    left.Add(entityId);
                }
            }

            // Update visible set
            _clientVisible[clientId] = nowVisible;

            return (entered, left);
        }

        /// <summary>
        /// Get all entities currently visible to a client.
        /// </summary>
        public IReadOnlyCollection<uint> GetVisibleEntities(int clientId)
        {
            return _clientVisible.TryGetValue(clientId, out var set)
                ? set
                : (IReadOnlyCollection<uint>)Array.Empty<uint>();
        }

        /// <summary>
        /// Check if an entity is visible to a client.
        /// </summary>
        public bool IsEntityVisible(int clientId, uint entityId)
        {
            return _clientVisible.TryGetValue(clientId, out var set) && set.Contains(entityId);
        }

        /// <summary>
        /// Clear all AOI data.
        /// </summary>
        public void Clear()
        {
            _grid.Clear();
            _clientVisible.Clear();
        }

        /// <summary>
        /// Number of registered clients.
        /// </summary>
        public int ClientCount => _clientVisible.Count;

        /// <summary>
        /// Vision radius.
        /// </summary>
        public float VisionRadius => _visionRadius;

        /// <summary>
        /// Hysteresis margin.
        /// </summary>
        public float Hysteresis => _hysteresis;
    }
}

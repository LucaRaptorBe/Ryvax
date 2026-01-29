// SpatialHashGrid.cs - Spatial partitioning for efficient AOI queries

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MOBANet.NetAdapter.AOI
{
    /// <summary>
    /// Spatial hash grid for efficient entity lookup by position.
    /// Used by AOIManager to find entities in a radius.
    /// Optimized for large numbers of entities (60+).
    /// </summary>
    public class SpatialHashGrid
    {
        private readonly float _cellSize;
        private readonly Dictionary<int, HashSet<uint>> _cells = new();
        private readonly Dictionary<uint, int> _entityCells = new();

        /// <summary>
        /// Create a spatial hash grid.
        /// </summary>
        /// <param name="cellSize">Size of each cell. Should be ~2x vision radius for efficiency.</param>
        public SpatialHashGrid(float cellSize = 20f)
        {
            _cellSize = cellSize;
        }

        /// <summary>
        /// Update an entity's position in the grid.
        /// </summary>
        public void UpdateEntity(uint entityId, Vector3 position)
        {
            int newCell = GetCellKey(position.x, position.z);

            if (_entityCells.TryGetValue(entityId, out int oldCell))
            {
                // Already in grid - check if cell changed
                if (oldCell == newCell) return;

                // Remove from old cell
                if (_cells.TryGetValue(oldCell, out var oldSet))
                {
                    oldSet.Remove(entityId);
                    // Don't remove empty cells - they'll be reused
                }
            }

            // Add to new cell
            if (!_cells.TryGetValue(newCell, out var newSet))
            {
                newSet = new HashSet<uint>();
                _cells[newCell] = newSet;
            }
            newSet.Add(entityId);
            _entityCells[entityId] = newCell;
        }

        /// <summary>
        /// Remove an entity from the grid.
        /// </summary>
        public void RemoveEntity(uint entityId)
        {
            if (_entityCells.TryGetValue(entityId, out int cell))
            {
                if (_cells.TryGetValue(cell, out var set))
                {
                    set.Remove(entityId);
                }
                _entityCells.Remove(entityId);
            }
        }

        /// <summary>
        /// Get all entities within a radius of a position.
        /// Results are added to the provided list (cleared first).
        /// </summary>
        public void GetEntitiesInRadius(Vector3 center, float radius, List<uint> result)
        {
            result.Clear();

            // Calculate cell range to check
            int cellRadius = (int)Math.Ceiling(radius / _cellSize);
            int cx = (int)Math.Floor(center.x / _cellSize);
            int cz = (int)Math.Floor(center.z / _cellSize);

            // Check all cells in range
            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dz = -cellRadius; dz <= cellRadius; dz++)
                {
                    int key = HashCoords(cx + dx, cz + dz);
                    if (_cells.TryGetValue(key, out var entities))
                    {
                        result.AddRange(entities);
                    }
                }
            }
        }

        /// <summary>
        /// Check if an entity is in the grid.
        /// </summary>
        public bool HasEntity(uint entityId)
        {
            return _entityCells.ContainsKey(entityId);
        }

        /// <summary>
        /// Get the cell key for a world position.
        /// </summary>
        private int GetCellKey(float x, float z)
        {
            return HashCoords(
                (int)Math.Floor(x / _cellSize),
                (int)Math.Floor(z / _cellSize)
            );
        }

        /// <summary>
        /// Hash 2D cell coordinates to a single int key.
        /// Uses large primes for good distribution.
        /// </summary>
        private static int HashCoords(int x, int z)
        {
            // Large primes for spatial hashing
            return x * 73856093 ^ z * 19349663;
        }

        /// <summary>
        /// Clear all entities from the grid.
        /// </summary>
        public void Clear()
        {
            _cells.Clear();
            _entityCells.Clear();
        }

        /// <summary>
        /// Number of entities in the grid.
        /// </summary>
        public int EntityCount => _entityCells.Count;

        /// <summary>
        /// Number of active cells in the grid.
        /// </summary>
        public int CellCount => _cells.Count;
    }
}

// SimEntity.cs - Base class for all simulation entities
// PURE C# - Uses UnityEngine only for Vector3/Mathf (could be replaced)

using UnityEngine;
using MOBANet.GameSim.Core;


namespace MOBANet.GameSim.Entities
{
    /// <summary>
    /// Entity type enumeration
    /// </summary>
    public enum EntityType : byte
    {
        None = 0,
        Player = 1,
        Projectile = 2,
        Minion = 3,
        Tower = 4,
        Objective = 5,
        Pickup = 6,
        Effect = 7,
    }

    /// <summary>
    /// Entity state enumeration
    /// </summary>
    public enum EntityState : byte
    {
        Idle = 0,
        Moving = 1,
        Attacking = 2,
        Casting = 3,
        Stunned = 4,
        Dead = 5,
        Spawning = 6,
        Despawning = 7,
        Dashing = 8,
    }

    /// <summary>
    /// Base class for all simulation entities.
    /// Contains common properties and methods for all entity types.
    /// </summary>
    public abstract class SimEntity
    {
        #region Static ID Management

        private static uint _nextId = 1;

        /// <summary>
        /// Reset the ID counter (call when starting a new match)
        /// </summary>
        public static void ResetIdCounter()
        {
            _nextId = 1;
        }

        /// <summary>
        /// Get and increment next available ID
        /// </summary>
        protected static uint GetNextId()
        {
            return _nextId++;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Unique entity identifier (assigned at creation)
        /// </summary>
        public uint Id { get; }

        /// <summary>
        /// Entity type
        /// </summary>
        public EntityType Type { get; protected set; }

        /// <summary>
        /// Current entity state
        /// </summary>
        public EntityState State { get; set; }

        /// <summary>
        /// Team ID (0 = neutral)
        /// </summary>
        public byte TeamId { get; set; }

        /// <summary>
        /// Tick when this entity was spawned
        /// </summary>
        public uint SpawnTick { get; set; }

        /// <summary>
        /// Is this entity marked for removal?
        /// </summary>
        public bool IsMarkedForRemoval { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Create a new entity with auto-generated ID
        /// </summary>
        protected SimEntity()
        {
            Id = GetNextId();
        }

        /// <summary>
        /// Create entity with specific ID (used for network sync)
        /// </summary>
        protected SimEntity(uint id)
        {
            Id = id;
            // Ensure next ID is higher than this
            if (id >= _nextId)
            {
                _nextId = id + 1;
            }
        }

        #endregion

        #region Simulation

        /// <summary>
        /// Simulate one tick. Override in derived classes.
        /// </summary>
        /// <param name="dt">Delta time (fixed)</param>
        /// <param name="config">Simulation configuration</param>
        public abstract void Tick(float dt, SimConfig config);

        #endregion

        // State Synchronization removed - each entity type implements its own

        // Combat methods removed - each entity type implements its own using states

        #region Utility

        /// <summary>
        /// Check if this entity is hostile to another
        /// </summary>
        public bool IsHostileTo(SimEntity other)
        {
            if (TeamId == 0 || other.TeamId == 0) return false; // Neutral
            return TeamId != other.TeamId;
        }

        /// <summary>
        /// Check if this entity is friendly to another
        /// </summary>
        public bool IsFriendlyTo(SimEntity other)
        {
            if (TeamId == 0 || other.TeamId == 0) return false;
            return TeamId == other.TeamId;
        }

        public override string ToString()
        {
            return $"{Type}[{Id}] Team:{TeamId} State:{State}";
        }

        #endregion
    }
}

// SimEvent.cs - Simulation event raised by handlers and physics
// PURE C# - No FishNet dependencies
// ServerTick is NOT stored here; it's known at drain time by ServerGameLoop.

using UnityEngine;

namespace MOBANet.GameSim.Events
{
    /// <summary>
    /// Lightweight event raised during simulation execution.
    /// Queued on SimWorld, drained by ServerGameLoop after command execution or Step().
    /// </summary>
    public struct SimEvent
    {
        /// <summary>
        /// Event type
        /// </summary>
        public SimEventType Type;

        /// <summary>
        /// Primary entity (caster, damaged player, respawned player, etc.)
        /// </summary>
        public uint EntityId;

        /// <summary>
        /// Context-dependent: attacker ID, class ID, ability slot, etc.
        /// </summary>
        public uint Data1;

        /// <summary>
        /// Context-dependent: damage amount, spawn position encoded, etc.
        /// </summary>
        public uint Data2;

        /// <summary>
        /// Direction vector (for AbilityUsed direction)
        /// </summary>
        public Vector3 Direction;
    }
}

// TransformState.cs - Movement & position state
// Pure data structure, no logic

using UnityEngine;

namespace MOBANet.GameSim.States
{
    /// <summary>
    /// État de transformation et mouvement.
    /// Répliqué chaque tick (haute fréquence).
    /// Inspiré de League of Legends architecture.
    /// </summary>
    public struct TransformState
    {
        #region Position & Rotation

        /// <summary>
        /// World position (3D)
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// Y rotation in degrees (0-360)
        /// </summary>
        public float RotationY;

        #endregion

        #region Velocity

        /// <summary>
        /// Impulse velocity (knockback, dash, jump). Decays via MovementEngine friction.
        /// Internal to physics — do not use for animation or snapshots.
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// Total velocity (impulse + input). Written by MovementEngine.Tick() each tick.
        /// Used by snapshots, animation, and dead-reckoning.
        /// </summary>
        public Vector3 EffectiveVelocity;

        #endregion

        #region Ground State

        /// <summary>
        /// Is entity on the ground?
        /// </summary>
        public bool IsGrounded;

        /// <summary>
        /// Is entity airborne (jumping, falling, launched)?
        /// </summary>
        public bool IsAirborne => !IsGrounded;

        #endregion

        #region Movement State

        /// <summary>
        /// Current movement direction (normalized, XZ plane)
        /// </summary>
        public Vector3 MoveDirection;

        /// <summary>
        /// Is entity currently moving?
        /// </summary>
        public bool IsMoving;

        #endregion

        #region Launch State (Dash, Knockup)

        #endregion
    }
}

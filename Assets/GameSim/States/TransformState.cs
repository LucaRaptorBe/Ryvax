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
        /// Velocity in 3D space (includes horizontal XZ and vertical Y components)
        /// </summary>
        public Vector3 Velocity;

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

        /// <summary>
        /// Is entity in launched state (dash, knockup, etc.)?
        /// </summary>
        public bool IsLaunched;

        /// <summary>
        /// Launch velocity (horizontal component)
        /// </summary>
        public Vector3 LaunchVelocity;

        #endregion
    }
}

// MovementEngine.cs - Physics-based movement for all entities
// PURE C# - No Unity dependencies except Vector3/Mathf
// Inspired by DreamGame CharacterMotor

using UnityEngine;
using MOBANet.GameSim.States;

namespace MOBANet.GameSim.Core
{
    /// <summary>
    /// Physics engine for entity movement.
    /// Handles gravity, friction, impulses, turn slowdown.
    /// Used by all entities (players, minions, monsters).
    /// </summary>
    public static class MovementEngine
    {
        #region Main Tick

        /// <summary>
        /// Process one tick of movement physics.
        /// Call this every simulation tick (30Hz).
        /// </summary>
        /// <param name="transform">Entity's transform state (modified in place)</param>
        /// <param name="moveSpeed">Entity's current move speed (from stats)</param>
        /// <param name="config">Simulation config</param>
        /// <param name="dt">Delta time</param>
        /// <param name="useTurnSlowdown">Apply turn slowdown (typically for players only)</param>
        public static void Tick(ref TransformState transform, float moveSpeed, SimConfig config, float dt, bool useTurnSlowdown = false)
        {
            // 1. Calculate input velocity from move direction
            Vector3 inputVelocity = CalculateInputVelocity(ref transform, moveSpeed, config, useTurnSlowdown);

            // 2. Apply gravity
            ApplyGravity(ref transform, config, dt);

            // 3. Apply friction to impulse velocity (horizontal only)
            ApplyFriction(ref transform, config, dt);

            // 4. Apply air control after friction (so it's not immediately reduced)
            if (transform.IsAirborne && transform.IsMoving)
            {
                ApplyAirControl(ref transform, transform.MoveDirection, config, dt);
            }

            // 5. Combine impulse velocity + input velocity and move
            Vector3 totalVelocity = transform.Velocity + inputVelocity;
            transform.Position += totalVelocity * dt;

            // 6. Ground check and arena bounds
            ApplyGroundCheck(ref transform, config);
            ClampToArenaBounds(ref transform, config);

            // 7. Update rotation toward move direction
            if (transform.MoveDirection.sqrMagnitude > 0.01f)
            {
                float targetRotation = Mathf.Atan2(transform.MoveDirection.x, transform.MoveDirection.z) * Mathf.Rad2Deg;
                float maxDelta = config.PlayerRotationSpeed * dt;
                transform.RotationY = Mathf.MoveTowardsAngle(transform.RotationY, targetRotation, maxDelta);
            }
        }

        #endregion

        #region Input Velocity

        /// <summary>
        /// Calculate input velocity from move direction.
        /// Applies turn slowdown if enabled.
        /// </summary>
        private static Vector3 CalculateInputVelocity(ref TransformState transform, float moveSpeed, SimConfig config, bool useTurnSlowdown)
        {
            if (!transform.IsMoving || transform.MoveDirection.sqrMagnitude < 0.01f)
            {
                return Vector3.zero;
            }

            // Air control: reduced influence when airborne
            if (transform.IsAirborne)
            {
                // Air control applies as impulse, not direct velocity
                return Vector3.zero;
            }

            float speedMultiplier = 1f;

            // Turn slowdown: penalize sharp turns (MOBA-style)
            if (useTurnSlowdown)
            {
                speedMultiplier = CalculateTurnSlowdown(transform, config);
            }

            return transform.MoveDirection * moveSpeed * speedMultiplier;
        }

        /// <summary>
        /// Calculate turn slowdown multiplier based on angle between
        /// current facing and desired move direction.
        /// </summary>
        private static float CalculateTurnSlowdown(TransformState transform, SimConfig config)
        {
            // Current facing direction from rotation
            float radians = transform.RotationY * Mathf.Deg2Rad;
            Vector3 facing = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

            // Angle between facing and move direction
            float dot = Vector3.Dot(facing, transform.MoveDirection);
            float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * Mathf.Rad2Deg;

            // No slowdown below threshold
            if (angle <= config.TurnSlowdownAngle)
            {
                return 1f;
            }

            // Linear interpolation from 1.0 to TurnSlowdownMultiplier
            // as angle goes from TurnSlowdownAngle to 180
            float t = (angle - config.TurnSlowdownAngle) / (180f - config.TurnSlowdownAngle);
            return Mathf.Lerp(1f, config.TurnSlowdownMultiplier, t);
        }

        #endregion

        #region Gravity

        /// <summary>
        /// Apply gravity to vertical velocity.
        /// </summary>
        private static void ApplyGravity(ref TransformState transform, SimConfig config, float dt)
        {
            var vel = transform.Velocity;

            if (transform.IsGrounded && vel.y <= 0f)
            {
                // Pull down to maintain ground contact
                vel.y = config.GroundedPullDown;
            }
            else
            {
                // Apply gravity acceleration
                vel.y += config.Gravity * dt;

                // Clamp to terminal velocity
                if (vel.y < config.TerminalVelocity)
                {
                    vel.y = config.TerminalVelocity;
                }
            }

            transform.Velocity = vel;
        }

        #endregion

        #region Friction

        /// <summary>
        /// Apply exponential friction/drag to horizontal velocity.
        /// Ground has high friction (quick stop), air has low friction (momentum preserved).
        /// </summary>
        private static void ApplyFriction(ref TransformState transform, SimConfig config, float dt)
        {
            var vel = transform.Velocity;

            // Select drag based on grounded state
            float drag = transform.IsGrounded ? config.GroundDrag : config.AirDrag;

            // Exponential decay: v *= e^(-drag * dt)
            float dragFactor = Mathf.Exp(-drag * dt);
            vel.x *= dragFactor;
            vel.z *= dragFactor;

            // Zero out very small velocities to avoid drift
            if (Mathf.Abs(vel.x) < 0.01f) vel.x = 0f;
            if (Mathf.Abs(vel.z) < 0.01f) vel.z = 0f;

            transform.Velocity = vel;
        }

        #endregion

        #region Ground Check

        /// <summary>
        /// Check if entity has landed and handle landing.
        /// </summary>
        private static void ApplyGroundCheck(ref TransformState transform, SimConfig config)
        {
            // Simple ground plane at Y=0
            // TODO: Support terrain heightmap
            if (transform.Position.y <= 0f)
            {
                // Land on ground
                transform.Position = new Vector3(transform.Position.x, 0f, transform.Position.z);

                // If was falling, reset velocity on landing
                if (!transform.IsGrounded || transform.Velocity.y < 0f)
                {
                    var vel = transform.Velocity;
                    vel.y = 0f;
                    // Reset horizontal impulse velocity on landing (instant control)
                    vel.x = 0f;
                    vel.z = 0f;
                    transform.Velocity = vel;
                }

                transform.IsGrounded = true;
            }
            else
            {
                transform.IsGrounded = false;
            }
        }

        /// <summary>
        /// Clamp position to arena bounds.
        /// </summary>
        private static void ClampToArenaBounds(ref TransformState transform, SimConfig config)
        {
            float halfWidth = config.ArenaWidth / 2f;
            float halfHeight = config.ArenaHeight / 2f;

            transform.Position = new Vector3(
                Mathf.Clamp(transform.Position.x, -halfWidth, halfWidth),
                Mathf.Clamp(transform.Position.y, 0f, config.MaxHeight),
                Mathf.Clamp(transform.Position.z, -halfHeight, halfHeight)
            );
        }

        #endregion

        #region Impulses

        /// <summary>
        /// Apply an impulse (jump, dash, knockback).
        /// Adds to current velocity.
        /// </summary>
        public static void ApplyImpulse(ref TransformState transform, Vector3 impulse)
        {
            transform.Velocity += impulse;
        }

        /// <summary>
        /// Apply a jump impulse.
        /// Calculates jump velocity from desired height and gravity.
        /// Preserves horizontal momentum from move direction.
        /// </summary>
        public static void ApplyJump(ref TransformState transform, float moveSpeed, SimConfig config)
        {
            if (!transform.IsGrounded) return;

            // Calculate jump velocity: v = sqrt(2 * |g| * h)
            float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(config.Gravity) * config.JumpHeight);

            // Horizontal momentum from current move direction
            Vector3 horizontalMomentum = transform.IsMoving
                ? transform.MoveDirection * moveSpeed
                : Vector3.zero;

            // Combine into jump impulse
            Vector3 jumpImpulse = horizontalMomentum + Vector3.up * jumpVelocity;

            transform.Velocity = jumpImpulse;
            transform.IsGrounded = false;
        }

        /// <summary>
        /// Apply air control (limited directional influence while airborne).
        /// Call this when player inputs direction while in air.
        /// </summary>
        public static void ApplyAirControl(ref TransformState transform, Vector3 direction, SimConfig config, float dt)
        {
            if (transform.IsGrounded) return;
            if (direction.sqrMagnitude < 0.01f) return;

            Vector3 airControlImpulse = direction.normalized * config.AirControlStrength * dt;

            var vel = transform.Velocity;
            vel.x += airControlImpulse.x;
            vel.z += airControlImpulse.z;
            transform.Velocity = vel;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Set move direction and update IsMoving flag.
        /// </summary>
        public static void SetMoveDirection(ref TransformState transform, Vector3 direction)
        {
            transform.MoveDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            transform.IsMoving = transform.MoveDirection.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Stop moving (clears direction, does NOT clear velocity).
        /// </summary>
        public static void StopMoving(ref TransformState transform)
        {
            transform.MoveDirection = Vector3.zero;
            transform.IsMoving = false;
        }

        /// <summary>
        /// Get horizontal speed (XZ plane).
        /// </summary>
        public static float GetHorizontalSpeed(TransformState transform)
        {
            return new Vector2(transform.Velocity.x, transform.Velocity.z).magnitude;
        }

        #endregion
    }
}

// SimConfig.cs - Simulation configuration parameters
// PURE C# - No Unity/FishNet dependencies
// All gameplay-affecting constants should be here for easy tuning

using MOBANet.Shared;

namespace MOBANet.GameSim.Core
{
    /// <summary>
    /// Simulation configuration parameters.
    /// Contains all gameplay-affecting constants.
    /// Should be synchronized between server and clients.
    /// </summary>
    public class SimConfig
    {
        #region Movement

        /// <summary>
        /// Base player movement speed (units per second).
        /// Matches NetcodeConstants.PLAYER_SPEED for reconciliation calculations.
        /// </summary>
        public float PlayerMoveSpeed { get; set; } = NetcodeConstants.PLAYER_SPEED;

        /// <summary>
        /// Player rotation speed in degrees per second.
        /// 720°/s = 2 full rotations per second (0.25s for 180° turn).
        /// </summary>
        public float PlayerRotationSpeed { get; set; } = 720f;

        #endregion

        #region Combat

        /// <summary>
        /// Base player max health
        /// </summary>
        public float PlayerMaxHealth { get; set; } = 100f;

        /// <summary>
        /// Base player attack damage
        /// </summary>
        public float PlayerBaseDamage { get; set; } = 10f;

        /// <summary>
        /// Player attack range in units
        /// </summary>
        public float PlayerAttackRange { get; set; } = 2.0f;

        /// <summary>
        /// Base attack cooldown in seconds
        /// </summary>
        public float PlayerAttackCooldown { get; set; } = 0.8f;

        /// <summary>
        /// Respawn time in seconds
        /// </summary>
        public float RespawnTime { get; set; } = 5f;

        #endregion

        #region Projectiles

        /// <summary>
        /// Default projectile speed
        /// </summary>
        public float ProjectileSpeed { get; set; } = 20f;

        /// <summary>
        /// Default projectile lifetime in seconds
        /// </summary>
        public float ProjectileLifetime { get; set; } = 5f;

        /// <summary>
        /// Projectile collision radius
        /// </summary>
        public float ProjectileRadius { get; set; } = 0.25f;

        #endregion

        #region Arena

        /// <summary>
        /// Arena width in world units
        /// </summary>
        public float ArenaWidth { get; set; } = 200f;

        /// <summary>
        /// Arena height (depth) in world units
        /// </summary>
        public float ArenaHeight { get; set; } = 200f;

        /// <summary>
        /// Maximum Y height for entities
        /// </summary>
        public float MaxHeight { get; set; } = 20f;

        #endregion

        #region Physics (DreamGame-style)

        /// <summary>
        /// Gravity acceleration (negative = downward)
        /// </summary>
        public float Gravity { get; set; } = -20f;

        /// <summary>
        /// Terminal velocity (max falling speed)
        /// </summary>
        public float TerminalVelocity { get; set; } = -50f;

        /// <summary>
        /// Jump height in units
        /// </summary>
        public float JumpHeight { get; set; } = 1.5f;

        /// <summary>
        /// Air control strength during aerial movement
        /// </summary>
        public float AirControlStrength { get; set; } = 5f;

        /// <summary>
        /// Pull-down velocity when grounded to maintain contact
        /// </summary>
        public float GroundedPullDown { get; set; } = -2f;

        /// <summary>
        /// Ground friction/drag (high = quick stop after impulse)
        /// Exponential decay: v *= e^(-drag * dt)
        /// 15 = impulses fade in ~0.2s
        /// </summary>
        public float GroundDrag { get; set; } = 15f;

        /// <summary>
        /// Air friction/drag (low = momentum preserved)
        /// 0.5 = momentum preserved longer in air
        /// </summary>
        public float AirDrag { get; set; } = 0.5f;

        /// <summary>
        /// Angle threshold before turn slowdown kicks in (degrees)
        /// </summary>
        public float TurnSlowdownAngle { get; set; } = 90f;

        /// <summary>
        /// Speed multiplier at 180° turn (0.2 = 20% speed)
        /// </summary>
        public float TurnSlowdownMultiplier { get; set; } = 0.2f;

        #endregion

        #region Teams

        /// <summary>
        /// Team 1 spawn X position
        /// </summary>
        public float Team1SpawnX { get; set; } = -80f;

        /// <summary>
        /// Team 2 spawn X position
        /// </summary>
        public float Team2SpawnX { get; set; } = 80f;

        /// <summary>
        /// Spawn spread (randomization range)
        /// </summary>
        public float SpawnSpread { get; set; } = 10f;

        #endregion

        #region Network

        /// <summary>
        /// Input buffer size (inputs to keep for reconciliation)
        /// </summary>
        public int InputBufferSize { get; set; } = 64;

        /// <summary>
        /// Snapshot buffer size (for interpolation)
        /// </summary>
        public int SnapshotBufferSize { get; set; } = 32;

        /// <summary>
        /// Interpolation delay in seconds (for remote entities)
        /// </summary>
        public float InterpolationDelay { get; set; } = 0.1f;

        /// <summary>
        /// Reconciliation threshold (distance before triggering reconciliation)
        /// </summary>
        public float ReconciliationThreshold { get; set; } = 0.1f;

        #endregion

        #region Factory

        /// <summary>
        /// Default configuration
        /// </summary>
        public static SimConfig Default => new SimConfig();

        /// <summary>
        /// Create a copy of this config
        /// </summary>
        public SimConfig Clone()
        {
            return new SimConfig
            {
                // Movement
                PlayerMoveSpeed = this.PlayerMoveSpeed,
                PlayerRotationSpeed = this.PlayerRotationSpeed,

                // Combat
                PlayerMaxHealth = this.PlayerMaxHealth,
                PlayerBaseDamage = this.PlayerBaseDamage,
                PlayerAttackRange = this.PlayerAttackRange,
                PlayerAttackCooldown = this.PlayerAttackCooldown,
                RespawnTime = this.RespawnTime,

                // Projectiles
                ProjectileSpeed = this.ProjectileSpeed,
                ProjectileLifetime = this.ProjectileLifetime,
                ProjectileRadius = this.ProjectileRadius,

                // Arena
                ArenaWidth = this.ArenaWidth,
                ArenaHeight = this.ArenaHeight,
                MaxHeight = this.MaxHeight,

                // Teams
                Team1SpawnX = this.Team1SpawnX,
                Team2SpawnX = this.Team2SpawnX,
                SpawnSpread = this.SpawnSpread,

                // Network
                InputBufferSize = this.InputBufferSize,
                SnapshotBufferSize = this.SnapshotBufferSize,
                InterpolationDelay = this.InterpolationDelay,
                ReconciliationThreshold = this.ReconciliationThreshold,

                // Physics
                Gravity = this.Gravity,
                TerminalVelocity = this.TerminalVelocity,
                JumpHeight = this.JumpHeight,
                AirControlStrength = this.AirControlStrength,
                GroundedPullDown = this.GroundedPullDown,
                GroundDrag = this.GroundDrag,
                AirDrag = this.AirDrag,
                TurnSlowdownAngle = this.TurnSlowdownAngle,
                TurnSlowdownMultiplier = this.TurnSlowdownMultiplier,
            };
        }

        #endregion
    }
}

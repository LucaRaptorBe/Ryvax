// SimPlayer.cs - Player entity with component-based state
// Inspiré de League of Legends architecture
// PURE C# - Uses UnityEngine only for Vector3/Mathf

using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.States;

namespace MOBANet.GameSim.Entities
{
    /// <summary>
    /// Player entity with component-based state.
    /// Architecture inspirée de League of Legends:
    /// - TransformState: position, rotation, velocity (high-frequency)
    /// - StatsState: HP, mana, resistances (low-frequency)
    /// - AbilityState: cooldowns, casts, levels (low-frequency)
    /// - CombatState: attack, target, damage (low-frequency)
    /// </summary>
    public class SimPlayer : SimEntity
    {
        #region States (Component-based)

        /// <summary>
        /// Transformation & movement state.
        /// High-frequency replication (every tick).
        /// </summary>
        public TransformState Transform;

        /// <summary>
        /// Player stats (HP, mana, resistances).
        /// Low-frequency replication (only when changed).
        /// </summary>
        public StatsState Stats;

        /// <summary>
        /// Ability state (cooldowns, casts, levels).
        /// Low-frequency replication (only when changed).
        /// </summary>
        public AbilityState Abilities;

        /// <summary>
        /// Combat state (auto-attack, target, damage).
        /// Low-frequency replication (only when changed).
        /// </summary>
        public CombatState Combat;

        #endregion

        #region Identity

        /// <summary>
        /// Client ID that owns this player
        /// </summary>
        public int OwnerClientId { get; }

        /// <summary>
        /// Champion ID (0=Mage, 1=Warrior, etc.)
        /// </summary>
        public int ChampionId { get; set; }

        #endregion

        #region Network Reconciliation

        /// <summary>
        /// Last processed command sequence (for reconciliation)
        /// </summary>
        public uint LastCommandSeq { get; set; }

        /// <summary>
        /// Time since last movement command (for watchdog)
        /// </summary>
        public float TimeSinceLastMoveCmd { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Create player with auto-generated ID (for server-side spawning)
        /// </summary>
        public SimPlayer(int ownerClientId) : base()
        {
            OwnerClientId = ownerClientId;
            Type = EntityType.Player;
            InitializeStates();
        }

        /// <summary>
        /// Create player with specific ID (for network sync)
        /// </summary>
        public SimPlayer(uint id, int ownerClientId) : base(id)
        {
            OwnerClientId = ownerClientId;
            Type = EntityType.Player;
            InitializeStates();
        }

        private void InitializeStates()
        {
            // Initialize TransformState
            Transform = new TransformState
            {
                Position = Vector3.zero,
                RotationY = 0f,
                Velocity = Vector3.zero, // Includes vertical component (y)
                IsGrounded = true,
                IsMoving = false,
                MoveDirection = Vector3.zero,
                IsLaunched = false,
                LaunchVelocity = Vector3.zero
            };

            // Initialize StatsState
            Stats = new StatsState
            {
                Health = 100,
                MaxHealth = 100,
                Mana = 100,
                MaxMana = 100,
                Level = 1,
                Experience = 0,
                Armor = 30f,
                MagicResist = 30f,
                MoveSpeedModifier = 1.0f
            };

            // Initialize AbilityState
            Abilities = new AbilityState();
            Abilities.Initialize();

            // Initialize CombatState
            Combat = new CombatState
            {
                AttackTargetId = 0,
                IsAutoAttacking = false,
                AttackCooldown = 0f,
                AttackSpeed = 0.658f, // LoL default
                AttackRange = 125f,   // Melee range
                BaseDamage = 50f,
                IsAttackMoving = false,
                TimeSinceLastAttack = 0f
            };
        }

        #endregion

        #region Simulation (called by SimWorld.Tick)

        /// <summary>
        /// Tick simulation (30Hz).
        /// Called by SimWorld.Tick().
        /// </summary>
        public override void Tick(float dt, SimConfig config)
        {
            if (Stats.IsDead)
            {
                // TODO: Handle respawn
                return;
            }

            // Update movement
            TickMovement(dt, config);

            // Update abilities
            TickAbilities(dt);

            // Update combat
            TickCombat(dt, config);
        }

        private void TickMovement(float dt, SimConfig config)
        {
            // Apply horizontal movement velocity (preserve vertical component)
            var vel = Transform.Velocity;

            if (Transform.IsMoving && Transform.MoveDirection.sqrMagnitude > 0.01f)
            {
                float moveSpeed = config.PlayerMoveSpeed * Stats.MoveSpeedModifier;
                Vector3 horizontalVel = Transform.MoveDirection * moveSpeed;
                vel.x = horizontalVel.x;
                vel.z = horizontalVel.z;
                // vel.y is preserved (gravity/jump)
            }
            else
            {
                vel.x = 0f;
                vel.z = 0f;
                // vel.y is preserved (gravity/jump)
            }

            Transform.Velocity = vel;

            // Apply gravity & physics
            ApplyGravity(dt, config);

            // Update rotation (smooth turn)
            if (Transform.MoveDirection.sqrMagnitude > 0.01f)
            {
                float targetRotation = Mathf.Atan2(Transform.MoveDirection.x, Transform.MoveDirection.z) * Mathf.Rad2Deg;
                float maxDelta = config.PlayerRotationSpeed * dt;
                Transform.RotationY = Mathf.MoveTowardsAngle(Transform.RotationY, targetRotation, maxDelta);
            }
        }

        private void ApplyGravity(float dt, SimConfig config)
        {
            if (Transform.IsLaunched)
            {
                // Launched state (dash, knockup, etc.)
                // Update vertical velocity with gravity
                var vel = Transform.Velocity;
                vel.y += config.Gravity * dt;
                Transform.Velocity = vel;

                Vector3 movement = new Vector3(
                    Transform.LaunchVelocity.x * dt,
                    Transform.Velocity.y * dt,
                    Transform.LaunchVelocity.z * dt
                );
                Transform.Position += movement;

                // Check landing
                if (Transform.Position.y <= 0f && Transform.Velocity.y <= 0f)
                {
                    Transform.Position = new Vector3(Transform.Position.x, 0f, Transform.Position.z);
                    vel = Transform.Velocity;
                    vel.y = 0f;
                    Transform.Velocity = vel;
                    Transform.IsGrounded = true;
                    Transform.IsLaunched = false;
                    Transform.LaunchVelocity = Vector3.zero;
                }
            }
            else if (Transform.IsGrounded)
            {
                // Grounded movement - apply pull down force
                var vel = Transform.Velocity;
                vel.y = config.GroundedPullDown;
                Transform.Velocity = vel;

                Vector3 movement = Transform.Velocity * dt;
                Transform.Position += movement;

                // Check if still grounded
                if (Transform.Position.y <= 0f)
                {
                    Transform.Position = new Vector3(Transform.Position.x, 0f, Transform.Position.z);
                    Transform.IsGrounded = true;
                }
                else
                {
                    Transform.IsGrounded = false;
                }
            }
            else
            {
                // Falling - apply gravity
                var vel = Transform.Velocity;
                vel.y += config.Gravity * dt;
                Transform.Velocity = vel;

                Vector3 movement = Transform.Velocity * dt;
                Transform.Position += movement;

                // Check landing
                if (Transform.Position.y <= 0f)
                {
                    Transform.Position = new Vector3(Transform.Position.x, 0f, Transform.Position.z);
                    vel = Transform.Velocity;
                    vel.y = 0f;
                    Transform.Velocity = vel;
                    Transform.IsGrounded = true;
                }
            }

            // Clamp to arena bounds
            float halfWidth = config.ArenaWidth / 2f;
            float halfHeight = config.ArenaHeight / 2f;
            Transform.Position = new Vector3(
                Mathf.Clamp(Transform.Position.x, -halfWidth, halfWidth),
                Mathf.Max(Transform.Position.y, 0f),
                Mathf.Clamp(Transform.Position.z, -halfHeight, halfHeight)
            );
        }

        private void TickAbilities(float dt)
        {
            // Decrease cooldowns
            for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
            {
                if (Abilities.Cooldowns[i] > 0)
                {
                    Abilities.Cooldowns[i] -= dt;
                    if (Abilities.Cooldowns[i] < 0)
                        Abilities.Cooldowns[i] = 0;
                }
            }

            // Update charge recovery
            for (int i = 0; i < AbilityState.SLOT_COUNT; i++)
            {
                if (Abilities.ChargeRecoveryTime[i] > 0)
                {
                    Abilities.ChargeRecoveryTime[i] -= dt;
                    if (Abilities.ChargeRecoveryTime[i] <= 0)
                    {
                        // Restore one charge
                        byte maxCharges = GetMaxCharges(i);
                        if (Abilities.Charges[i] < maxCharges)
                        {
                            Abilities.Charges[i]++;
                        }
                    }
                }
            }

            // Update current cast
            if (Abilities.CurrentCast.IsCasting)
            {
                Abilities.CurrentCast.CastProgress += dt;

                if (Abilities.CurrentCast.IsComplete)
                {
                    // Cast finished
                    Abilities.CurrentCast.IsCasting = false;
                    // TODO: Execute ability effect
                }
            }
        }

        private void TickCombat(float dt, SimConfig config)
        {
            // Decrease attack cooldown
            if (Combat.AttackCooldown > 0)
            {
                Combat.AttackCooldown -= dt;
            }

            Combat.TimeSinceLastAttack += dt;

            // TODO: Auto-attack logic
        }

        private byte GetMaxCharges(int slot)
        {
            // TODO: Lire depuis ability config
            return 1; // Par défaut 1 charge
        }

        #endregion

        #region Commands (called by CommandHandlers)

        /// <summary>
        /// Start moving in direction.
        /// </summary>
        public void SetMoveDirection(Vector3 direction)
        {
            Transform.MoveDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            Transform.IsMoving = Transform.MoveDirection.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Stop moving (horizontal only, preserves vertical velocity).
        /// </summary>
        public void StopMoving()
        {
            Transform.MoveDirection = Vector3.zero;

            // Stop horizontal movement but preserve vertical velocity (gravity/jump)
            var vel = Transform.Velocity;
            vel.x = 0f;
            vel.z = 0f;
            // vel.y is preserved
            Transform.Velocity = vel;

            Transform.IsMoving = false;
        }

        /// <summary>
        /// Apply launch velocity (dash, jump, knockup).
        /// </summary>
        public void ApplyLaunchVelocity(Vector3 velocity)
        {
            Transform.LaunchVelocity = new Vector3(velocity.x, 0f, velocity.z);
            var vel = Transform.Velocity;
            vel.y = velocity.y;
            Transform.Velocity = vel;
            Transform.IsLaunched = true;
            Transform.IsGrounded = false;
        }

        /// <summary>
        /// Take damage.
        /// </summary>
        public void TakeDamage(int damage, uint attackerId)
        {
            Stats.Health -= damage;
            if (Stats.Health <= 0)
            {
                Stats.Health = 0;
                Die(attackerId);
            }
        }

        /// <summary>
        /// Heal.
        /// </summary>
        public void Heal(int amount)
        {
            Stats.Health = Mathf.Min(Stats.Health + amount, Stats.MaxHealth);
        }

        /// <summary>
        /// Die.
        /// </summary>
        private void Die(uint killerId)
        {
            Debug.Log($"[SimPlayer] Player {Id} killed by {killerId}");
            // TODO: Death logic
        }

        #endregion

        #region Network Sync

        /// <summary>
        /// Apply state from network snapshot.
        /// </summary>
        public void ApplyNetworkState(Vector3 position, Vector3 velocity, float rotationY)
        {
            Transform.Position = position;
            Transform.RotationY = rotationY;

            // Derive movement state from velocity
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            Transform.IsMoving = horizontalSpeed > 0.1f;

            if (Transform.IsMoving)
            {
                Transform.MoveDirection = new Vector3(velocity.x, 0f, velocity.z).normalized;
                Transform.Velocity = velocity;
            }
            else
            {
                Transform.MoveDirection = Vector3.zero;
                Transform.Velocity = Vector3.zero;
            }
        }

        #endregion


        public override string ToString()
        {
            return $"Player[{Id}] Client:{OwnerClientId} Team:{TeamId} HP:{Stats.Health}/{Stats.MaxHealth} Pos:{Transform.Position:F1}";
        }
    }
}

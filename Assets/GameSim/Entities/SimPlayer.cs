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
        /// Class ID (1=Archer, 2=Mage, 3=Fighter, etc.)
        /// Maps to CharacterClass enum in animation system.
        /// </summary>
        public int ClassId { get; set; }

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

        /// <summary>
        /// Time of last MoveStop command (for debug logging on server)
        /// </summary>
        public float LastMoveStopTime { get; set; }

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
                Velocity = Vector3.zero,
                IsGrounded = true,
                IsMoving = false,
                MoveDirection = Vector3.zero
            };

            // Initialize StatsState
            Stats = new StatsState
            {
                Health = 100,
                MaxHealth = 100,
                Level = 1,
                Experience = 0,
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
        /// Tick simulation (60Hz, matches NetcodeConstants.TICK_RATE).
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
            float moveSpeed = config.PlayerMoveSpeed * Stats.MoveSpeedModifier;
            MovementEngine.Tick(ref Transform, moveSpeed, config, dt, useTurnSlowdown: true);
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
        /// Start moving in direction (WASD/MoveDir).
        /// </summary>
        public void SetMoveDirection(Vector3 direction)
        {
            Transform.MoveDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector3.zero;
            Transform.IsMoving = Transform.MoveDirection.sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Move towards target position (click/MoveTo).
        /// Computes direction internally - keeps semantic separation for future pathfinding.
        /// </summary>
        public void SetMoveTarget(Vector3 target)
        {
            Vector3 toTarget = target - Transform.Position;
            toTarget.y = 0; // XZ plane only

            if (toTarget.sqrMagnitude > 0.01f)
            {
                SetMoveDirection(toTarget.normalized);
            }
            else
            {
                // Already at target
                StopMoving();
            }
        }

        /// <summary>
        /// Stop moving. Clears direction and IsMoving flag.
        /// Does NOT clear Velocity — impulses (knockback, dash) decay via MovementEngine friction.
        /// </summary>
        public void StopMoving()
        {
            Transform.MoveDirection = Vector3.zero;
            Transform.IsMoving = false;
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
            StopMoving();
        }

        /// <summary>
        /// Respawn at position with full HP.
        /// </summary>
        public void Respawn(Vector3 position)
        {
            Stats.Health = Stats.MaxHealth;
            Transform.Position = position;
            Transform.Velocity = Vector3.zero;
            Transform.EffectiveVelocity = Vector3.zero;
            Transform.IsMoving = false;
            Transform.MoveDirection = Vector3.zero;
        }

        #endregion

        #region Network Sync

        /// <summary>
        /// Apply state from network snapshot.
        /// </summary>
        public void ApplyNetworkState(Vector3 position, Vector3 velocity, float rotationY)
        {
            ApplyTransformState(position, velocity, rotationY);
        }

        /// <summary>
        /// Apply state from network snapshot, including cooldowns.
        /// Overload that accepts an EntityState for cooldown data.
        /// </summary>
        public void ApplyNetworkState(Vector3 position, Vector3 velocity, float rotationY,
            in MOBANet.NetAdapter.Messages.EntityState netState)
        {
            ApplyTransformState(position, velocity, rotationY);

            // Sync health from snapshot
            Stats.Health = netState.Health;

            // Apply cooldowns from snapshot
            if (Abilities.Cooldowns != null)
            {
                for (int i = 0; i < 4 && i < Abilities.Cooldowns.Length; i++)
                {
                    Abilities.Cooldowns[i] = netState.GetCooldown(i);
                }
            }
        }

        private void ApplyTransformState(Vector3 position, Vector3 velocity, float rotationY)
        {
            Transform.Position = position;
            Transform.RotationY = rotationY;

            // velocity from snapshot is EffectiveVelocity (impulse + input)
            Transform.EffectiveVelocity = velocity;

            // Derive movement state from effective velocity
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            Transform.IsMoving = horizontalSpeed > 0.1f;

            if (Transform.IsMoving)
            {
                Transform.MoveDirection = new Vector3(velocity.x, 0f, velocity.z).normalized;
            }
            else
            {
                Transform.MoveDirection = Vector3.zero;
            }
        }

        #endregion


        public override string ToString()
        {
            return $"Player[{Id}] Client:{OwnerClientId} Team:{TeamId} HP:{Stats.Health}/{Stats.MaxHealth} Pos:{Transform.Position:F1}";
        }
    }
}

// CombatState.cs - Combat state (auto-attack, target, damage)
// Pure data structure, no logic

using UnityEngine;

namespace MOBANet.GameSim.States
{
    /// <summary>
    /// État de combat (auto-attack, target, etc.).
    /// Répliqué uniquement quand changement (basse fréquence).
    /// Inspiré de League of Legends architecture.
    /// </summary>
    public struct CombatState
    {
        #region Target

        /// <summary>
        /// Current auto-attack target entity ID (0 = no target)
        /// </summary>
        public uint AttackTargetId;

        /// <summary>
        /// Is currently auto-attacking?
        /// </summary>
        public bool IsAutoAttacking;

        #endregion

        #region Attack Stats

        /// <summary>
        /// Cooldown before next auto-attack can be issued (seconds)
        /// </summary>
        public float AttackCooldown;

        /// <summary>
        /// Attacks per second (ex: 0.658 in LoL)
        /// Inverse = seconds per attack
        /// </summary>
        public float AttackSpeed;

        /// <summary>
        /// Auto-attack range (units)
        /// Ex: 125 = melee, 550 = ADC
        /// </summary>
        public float AttackRange;

        /// <summary>
        /// Base attack damage (before items/buffs)
        /// </summary>
        public float BaseDamage;

        #endregion

        #region Attack-Move

        /// <summary>
        /// Is doing attack-move (attack enemies while moving to position)?
        /// </summary>
        public bool IsAttackMoving;

        /// <summary>
        /// Attack-move target position
        /// </summary>
        public Vector3 AttackMoveTarget;

        #endregion

        #region Timing

        /// <summary>
        /// Time since last auto-attack (seconds)
        /// Used for animation timing
        /// </summary>
        public float TimeSinceLastAttack;

        #endregion

        #region Helpers

        /// <summary>
        /// Can auto-attack now?
        /// </summary>
        public bool CanAttack => AttackCooldown <= 0f;

        /// <summary>
        /// Seconds per attack (inverse of attack speed)
        /// </summary>
        public float AttackPeriod => AttackSpeed > 0 ? 1f / AttackSpeed : 1f;

        /// <summary>
        /// Has valid target?
        /// </summary>
        public bool HasTarget => AttackTargetId != 0;

        #endregion
    }
}

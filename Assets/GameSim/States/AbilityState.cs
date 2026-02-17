// AbilityState.cs - Ability state (cooldowns, casts, levels)
// Pure data structure, no logic

using UnityEngine;

namespace MOBANet.GameSim.States
{
    /// <summary>
    /// État des sorts du joueur.
    /// Répliqué uniquement quand changement (basse fréquence).
    /// Inspiré de League of Legends architecture.
    /// </summary>
    public struct AbilityState
    {
        /// <summary>
        /// Number of ability slots (Q, W, E, R, Summoner1, Summoner2)
        /// </summary>
        public const int SLOT_COUNT = 6;

        #region Cooldowns

        /// <summary>
        /// Cooldowns for each ability slot (seconds remaining)
        /// Index: 0=Q, 1=W, 2=E, 3=R, 4=Summoner1, 5=Summoner2
        /// </summary>
        public float[] Cooldowns;

        #endregion

        #region Levels

        /// <summary>
        /// Ability levels (0 = not learned, 1-5 = levels)
        /// In LoL: Q/W/E max at level 5, R max at level 3
        /// </summary>
        public byte[] Levels;

        #endregion

        #region Charges (ex: Corki W, Teemo R)

        /// <summary>
        /// Current charges for each ability
        /// Most abilities have 1 charge, some have 2-3
        /// </summary>
        public byte[] Charges;

        /// <summary>
        /// Time until next charge recovery (seconds)
        /// </summary>
        public float[] ChargeRecoveryTime;

        #endregion

        #region Current Cast

        /// <summary>
        /// Current cast state (if casting/channeling)
        /// </summary>
        public CastState CurrentCast;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize arrays (call in constructor)
        /// </summary>
        public void Initialize()
        {
            Cooldowns = new float[SLOT_COUNT];
            Levels = new byte[SLOT_COUNT];
            Charges = new byte[SLOT_COUNT];
            ChargeRecoveryTime = new float[SLOT_COUNT];
            CurrentCast = new CastState();

            // Default: 1 charge per ability, all abilities learned at level 1
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                Charges[i] = 1;
                Levels[i] = 1;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Can cast ability in slot?
        /// </summary>
        public bool CanCast(int slot)
        {
            if (slot < 0 || slot >= SLOT_COUNT) return false;
            if (Levels[slot] == 0) return false; // Not learned
            if (Cooldowns[slot] > 0) return false; // On cooldown
            if (Charges[slot] == 0) return false; // No charges
            if (CurrentCast.IsCasting) return false; // Already casting
            return true;
        }

        /// <summary>
        /// Is ability learned?
        /// </summary>
        public bool IsLearned(int slot)
        {
            return slot >= 0 && slot < SLOT_COUNT && Levels[slot] > 0;
        }

        #endregion
    }

    /// <summary>
    /// État d'un cast en cours (channeling, cast time, etc.)
    /// </summary>
    public struct CastState
    {
        #region Cast State

        /// <summary>
        /// Is currently casting/channeling?
        /// </summary>
        public bool IsCasting;

        /// <summary>
        /// Which ability slot is being cast (0-5)
        /// </summary>
        public byte CastingSlot;

        #endregion

        #region Cast Timing

        /// <summary>
        /// Total cast time (seconds)
        /// Ex: Lux R = 1.0s cast time
        /// </summary>
        public float CastTime;

        /// <summary>
        /// Time elapsed since cast started (seconds)
        /// </summary>
        public float CastProgress;

        #endregion

        #region Cast Type

        /// <summary>
        /// Is this a channel (interruptible, ex: Katarina R)?
        /// If false, it's a cast (non-interruptible after windup)
        /// </summary>
        public bool IsChanneling;

        #endregion

        #region Cast Target

        /// <summary>
        /// Target position (for skillshots, ground-targeted)
        /// </summary>
        public Vector3 TargetPosition;

        /// <summary>
        /// Target entity ID (for targeted abilities, 0 = none)
        /// </summary>
        public uint TargetEntityId;

        #endregion

        #region Helpers

        /// <summary>
        /// Cast completion percentage (0.0 - 1.0)
        /// </summary>
        public float Progress => CastTime > 0 ? CastProgress / CastTime : 1f;

        /// <summary>
        /// Is cast complete?
        /// </summary>
        public bool IsComplete => CastProgress >= CastTime;

        #endregion
    }
}

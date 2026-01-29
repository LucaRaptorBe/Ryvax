// StatsState.cs - Player stats (HP, mana, resistances)
// Pure data structure, no logic

namespace MOBANet.GameSim.States
{
    /// <summary>
    /// Stats du joueur (HP, mana, resistances).
    /// Répliqué uniquement quand changement (basse fréquence).
    /// Inspiré de League of Legends architecture.
    /// </summary>
    public struct StatsState
    {
        #region Health

        /// <summary>
        /// Current health
        /// </summary>
        public int Health;

        /// <summary>
        /// Maximum health
        /// </summary>
        public int MaxHealth;

        #endregion

        #region Mana/Energy

        /// <summary>
        /// Current mana/energy
        /// </summary>
        public int Mana;

        /// <summary>
        /// Maximum mana/energy
        /// </summary>
        public int MaxMana;

        #endregion

        #region Level & Experience

        /// <summary>
        /// Current level (1-18 in LoL)
        /// </summary>
        public byte Level;

        /// <summary>
        /// Current experience points
        /// </summary>
        public int Experience;

        #endregion

        #region Resistances

        /// <summary>
        /// Armor (physical damage reduction)
        /// </summary>
        public float Armor;

        /// <summary>
        /// Magic resistance
        /// </summary>
        public float MagicResist;

        #endregion

        #region Modifiers

        /// <summary>
        /// Move speed modifier (1.0 = normal, 1.5 = +50%, 0.5 = -50%)
        /// </summary>
        public float MoveSpeedModifier;

        #endregion

        #region Helpers

        /// <summary>
        /// Is entity alive?
        /// </summary>
        public bool IsAlive => Health > 0;

        /// <summary>
        /// Is entity dead?
        /// </summary>
        public bool IsDead => Health <= 0;

        /// <summary>
        /// Health percentage (0.0 - 1.0)
        /// </summary>
        public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;

        /// <summary>
        /// Mana percentage (0.0 - 1.0)
        /// </summary>
        public float ManaPercent => MaxMana > 0 ? (float)Mana / MaxMana : 0f;

        #endregion
    }
}

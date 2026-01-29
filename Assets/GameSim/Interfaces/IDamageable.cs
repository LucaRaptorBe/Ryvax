// IDamageable.cs - Interface for entities that can take damage
// Pure C# interface (no Unity dependencies)

namespace MOBANet.GameSim.Interfaces
{
    /// <summary>
    /// Interface pour les objets qui peuvent recevoir des dégâts et être soignés.
    /// Règle métier : Définit comment les entités interagissent avec les dégâts/soins.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// Inflige des dégâts à l'entité.
        /// </summary>
        void TakeDamage(float amount);

        /// <summary>
        /// Soigne l'entité.
        /// </summary>
        void Heal(float amount);
    }
}

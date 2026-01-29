// Team.cs - Team enumeration
// Pure C# type (no Unity dependencies)

namespace MOBANet.GameSim.Types
{
    /// <summary>
    /// Équipe d'appartenance d'une entité dans le jeu.
    /// Règle métier : Détermine qui peut attaquer qui, visibility, scoring.
    /// </summary>
    public enum Team
    {
        /// <summary>Équipe rouge (spawn côté gauche)</summary>
        Red,

        /// <summary>Équipe bleue (spawn côté droit)</summary>
        Blue,

        /// <summary>Entité neutre (mobs, objectifs)</summary>
        Neutral
    }
}

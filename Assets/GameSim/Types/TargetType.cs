// TargetType.cs - Target type enumeration
// Pure C# type (no Unity dependencies)

namespace MOBANet.GameSim.Types
{
    /// <summary>
    /// Type de cibles qu'un sort peut affecter.
    /// Règle métier : Détermine qui est touché par les effets et les dégâts.
    /// </summary>
    public enum TargetType
    {
        /// <summary>Équipe opposée uniquement</summary>
        Enemies,

        /// <summary>Même équipe uniquement</summary>
        Allies,

        /// <summary>Tout le monde (friendly fire)</summary>
        Both
    }
}

using UnityEngine;

namespace MOBANet.GameSim.Data
{
    /// <summary>
    /// Common interface for ability and item definitions.
    /// Implemented by ScriptableObjects (ArrowDefinition, FireballDefinition, etc.)
    /// </summary>
    public interface IAbilityDefinition
    {
        // Identity
        string Name { get; }
        string Description { get; }
        Sprite Icon { get; }

        // Base stats
        float BaseDamage { get; }
        float BaseCooldown { get; }
        float BaseRange { get; }

        // Targeting
        AbilityTargetType TargetType { get; }
        TargetFilter TargetFilter { get; }

        // Specializations
        string Spec1Name { get; }
        string Spec1 { get; }
        string Spec2Name { get; }
        string Spec2 { get; }
        string Spec3Name { get; }
        string Spec3 { get; }
    }
}

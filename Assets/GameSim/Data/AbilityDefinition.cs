using UnityEngine;

namespace MOBANet.GameSim.Data
{
    /// <summary>
    /// Base ScriptableObject for ability definitions.
    /// Create via Assets > Create > Ryvax > Ability Definition
    /// </summary>
    [CreateAssetMenu(fileName = "NewAbility", menuName = "Ryvax/Ability Definition")]
    public class AbilityDefinition : ScriptableObject, IAbilityDefinition
    {
        [Header("Identity")]
        [SerializeField] private string abilityName = "New Ability";
        [SerializeField, TextArea(2, 4)] private string description = "";
        [SerializeField] private Sprite icon;

        [Header("Base Stats")]
        [SerializeField] private float baseDamage = 50f;
        [SerializeField] private float baseCooldown = 5f;
        [SerializeField] private float baseRange = 5f;

        [Header("Targeting")]
        [SerializeField] private AbilityTargetType targetType = AbilityTargetType.Targeted;
        [SerializeField] private TargetFilter targetFilter = TargetFilter.Enemies;

        [Header("Specializations")]
        [SerializeField] private string spec1Name = "";
        [SerializeField, TextArea(1, 3)] private string spec1 = "";
        [SerializeField] private string spec2Name = "";
        [SerializeField, TextArea(1, 3)] private string spec2 = "";
        [SerializeField] private string spec3Name = "";
        [SerializeField, TextArea(1, 3)] private string spec3 = "";

        // IAbilityDefinition implementation
        public string Name => abilityName;
        public string Description => description;
        public Sprite Icon => icon;
        public float BaseDamage => baseDamage;
        public float BaseCooldown => baseCooldown;
        public float BaseRange => baseRange;
        public AbilityTargetType TargetType => targetType;
        public TargetFilter TargetFilter => targetFilter;
        public string Spec1Name => spec1Name;
        public string Spec1 => spec1;
        public string Spec2Name => spec2Name;
        public string Spec2 => spec2;
        public string Spec3Name => spec3Name;
        public string Spec3 => spec3;
    }
}

using UnityEngine;
using MOBANet.Client.Animation;

namespace MOBANet.GameSim.Data
{
    /// <summary>
    /// Defines a character class with its 4 abilities.
    /// Create via: Assets > Create > Ryvax > Character Class
    /// </summary>
    [CreateAssetMenu(fileName = "NewClass", menuName = "Ryvax/Character Class")]
    public class CharacterClass : ScriptableObject
    {
        [Header("Class Info")]
        public string className = "New Class";
        public Sprite classIcon;
        public CharacterClassType classType;

        [Header("Passive")]
        [SerializeField] private ScriptableObject passive;

        [Header("Abilities (4 Slots)")]
        [Tooltip("Slot Q")]
        [SerializeField] private ScriptableObject ability1;

        [Tooltip("Slot W")]
        [SerializeField] private ScriptableObject ability2;

        [Tooltip("Slot E")]
        [SerializeField] private ScriptableObject ability3;

        [Tooltip("Slot R")]
        [SerializeField] private ScriptableObject ability4;

        public ScriptableObject GetAbility(int slotIndex)
        {
            return slotIndex switch
            {
                0 => ability1,
                1 => ability2,
                2 => ability3,
                3 => ability4,
                _ => null
            };
        }

        public IAbilityDefinition GetAbilityDefinition(int slotIndex)
        {
            return GetAbility(slotIndex) as IAbilityDefinition;
        }

        public IAbilityDefinition GetPassive()
        {
            return passive as IAbilityDefinition;
        }

        public ScriptableObject[] GetAllAbilities()
        {
            return new[] { ability1, ability2, ability3, ability4 };
        }
    }
}

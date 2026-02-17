using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Manages Animator layer activation for character classes.
    /// Ensures only one class layer is active at a time.
    /// </summary>
    public class AnimatorLayerManager
    {
        private readonly Animator _animator;
        private CharacterClassType _activeClass;

        // Cache layer indices for performance
        private readonly int _archerLayerIndex;
        private readonly int _mageLayerIndex;
        private readonly int _fighterLayerIndex;
        private readonly int _assassinLayerIndex;
        private readonly int _tankLayerIndex;
        private readonly int _healerLayerIndex;
        private readonly int _summonerLayerIndex;
        private readonly int _warriorLayerIndex;

        public AnimatorLayerManager(Animator animator)
        {
            _animator = animator;
            _activeClass = CharacterClassType.None;

            // Cache layer indices (avoid string lookups at runtime)
            _archerLayerIndex = animator.GetLayerIndex("Archer Layer");
            _mageLayerIndex = animator.GetLayerIndex("Mage Layer");
            _fighterLayerIndex = animator.GetLayerIndex("Fighter Layer");
            _assassinLayerIndex = animator.GetLayerIndex("Assassin Layer");
            _tankLayerIndex = animator.GetLayerIndex("Tank Layer");
            _healerLayerIndex = animator.GetLayerIndex("Healer Layer");
            _summonerLayerIndex = animator.GetLayerIndex("Summoner Layer");
            _warriorLayerIndex = animator.GetLayerIndex("Warrior Layer");
        }

        /// <summary>
        /// Activate the layer for the specified class, deactivate all others.
        /// </summary>
        /// <param name="classType">The class to activate</param>
        public void SetActiveClass(CharacterClassType classType)
        {
            if (_activeClass == classType)
                return; // Already active

            // Deactivate all class layers
            DeactivateAllClassLayers();

            // Activate the selected class layer
            int layerIndex = GetLayerIndex(classType);
            if (layerIndex >= 0)
            {
                _animator.SetLayerWeight(layerIndex, 1f);
                _activeClass = classType;
                Debug.Log($"[AnimatorLayerManager] Activated {classType} Layer (index: {layerIndex})");
            }
            else
            {
                Debug.LogWarning($"[AnimatorLayerManager] Layer not found for class: {classType}");
            }
        }

        /// <summary>
        /// Get the currently active class.
        /// </summary>
        public CharacterClassType GetActiveClass()
        {
            return _activeClass;
        }

        /// <summary>
        /// Deactivate all class layers (set weight to 0).
        /// Base layer (index 0) is never deactivated.
        /// </summary>
        private void DeactivateAllClassLayers()
        {
            // Set all class layers (indices 1-8) to weight 0
            if (_archerLayerIndex >= 0) _animator.SetLayerWeight(_archerLayerIndex, 0f);
            if (_mageLayerIndex >= 0) _animator.SetLayerWeight(_mageLayerIndex, 0f);
            if (_fighterLayerIndex >= 0) _animator.SetLayerWeight(_fighterLayerIndex, 0f);
            if (_assassinLayerIndex >= 0) _animator.SetLayerWeight(_assassinLayerIndex, 0f);
            if (_tankLayerIndex >= 0) _animator.SetLayerWeight(_tankLayerIndex, 0f);
            if (_healerLayerIndex >= 0) _animator.SetLayerWeight(_healerLayerIndex, 0f);
            if (_summonerLayerIndex >= 0) _animator.SetLayerWeight(_summonerLayerIndex, 0f);
            if (_warriorLayerIndex >= 0) _animator.SetLayerWeight(_warriorLayerIndex, 0f);
        }

        /// <summary>
        /// Get the cached layer index for a character class.
        /// </summary>
        /// <param name="classType">The class type</param>
        /// <returns>Layer index, or -1 if not found</returns>
        private int GetLayerIndex(CharacterClassType classType)
        {
            return classType switch
            {
                CharacterClassType.Archer => _archerLayerIndex,
                CharacterClassType.Mage => _mageLayerIndex,
                CharacterClassType.Fighter => _fighterLayerIndex,
                CharacterClassType.Assassin => _assassinLayerIndex,
                CharacterClassType.Tank => _tankLayerIndex,
                CharacterClassType.Healer => _healerLayerIndex,
                CharacterClassType.Summoner => _summonerLayerIndex,
                CharacterClassType.Warrior => _warriorLayerIndex,
                _ => -1
            };
        }
    }
}

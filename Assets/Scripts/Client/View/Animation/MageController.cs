using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Mage class.
    /// Handles spell casting animations (Fireball, Meteor, Heal).
    /// </summary>
    public class MageController : ICharacterClassController
    {
        private static readonly int IsCastingHash = Animator.StringToHash("IsCasting");
        private static readonly int CastFireballHash = Animator.StringToHash("CastFireball");
        private static readonly int CastMeteorHash = Animator.StringToHash("CastMeteor");
        private static readonly int CastHealHash = Animator.StringToHash("CastHeal");

        private Animator _animator;
        private bool _isCasting;

        public CharacterClassType Class => CharacterClassType.Mage;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _isCasting = false;
        }

        public void UpdateAnimations()
        {
            // Currently no per-frame updates needed
            // Future: Update cast direction, spell charge level, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[MageController] Activated");
        }

        public void OnDeactivated()
        {
            // Reset state when switching classes
            _isCasting = false;
            _animator?.SetBool(IsCastingHash, false);
            Debug.Log("[MageController] Deactivated");
        }

        // Public API for controlling mage animations

        /// <summary>
        /// Trigger Fireball spell cast animation.
        /// Called when server confirms ability use.
        /// </summary>
        public void CastFireball()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(CastFireballHash);
            Debug.Log("[MageController] Casting Fireball");
        }

        /// <summary>
        /// Trigger Meteor spell cast animation.
        /// Called when server confirms ability use.
        /// </summary>
        public void CastMeteor()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(CastMeteorHash);
            Debug.Log("[MageController] Casting Meteor");
        }

        /// <summary>
        /// Trigger Heal spell cast animation.
        /// Called when server confirms ability use.
        /// </summary>
        public void CastHeal()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(CastHealHash);
            Debug.Log("[MageController] Casting Heal");
        }

        // Animation event callbacks (called from animation clips)

        public void OnCastStart()
        {
            // Called from animation event when cast animation starts
            Debug.Log("[MageController] Cast started (animation event)");
        }

        public void OnSpellRelease()
        {
            // Called from animation event at spell release frame
            // Spawn spell VFX here
            Debug.Log("[MageController] Spell released (animation event)");
        }

        public void OnCastComplete()
        {
            // Called from animation event when cast animation completes
            _isCasting = false;
            _animator?.SetBool(IsCastingHash, false);
            Debug.Log("[MageController] Cast complete (animation event)");
        }
    }
}

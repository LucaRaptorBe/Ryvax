using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Healer class.
    /// Handles heal spell casting animations (single, AOE, resurrect).
    /// </summary>
    public class HealerController : ICharacterClassController
    {
        private static readonly int IsCastingHash = Animator.StringToHash("IsCasting");
        private static readonly int HealSingleHash = Animator.StringToHash("HealSingle");
        private static readonly int HealAOEHash = Animator.StringToHash("HealAOE");
        private static readonly int ResurrectHash = Animator.StringToHash("Resurrect");

        private Animator _animator;
        private bool _isCasting;

        public CharacterClassType Class => CharacterClassType.Healer;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _isCasting = false;
        }

        public void UpdateAnimations()
        {
            // Future: Update heal target, cast progress, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[HealerController] Activated");
        }

        public void OnDeactivated()
        {
            _isCasting = false;
            _animator?.SetBool(IsCastingHash, false);
            Debug.Log("[HealerController] Deactivated");
        }

        // Public API

        public void CastHealSingle()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(HealSingleHash);
        }

        public void CastHealAOE()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(HealAOEHash);
        }

        public void CastResurrect()
        {
            _isCasting = true;
            _animator?.SetBool(IsCastingHash, true);
            _animator?.SetTrigger(ResurrectHash);
        }

        public void OnCastComplete()
        {
            _isCasting = false;
            _animator?.SetBool(IsCastingHash, false);
        }
    }
}

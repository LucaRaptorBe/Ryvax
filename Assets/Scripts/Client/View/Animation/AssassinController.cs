using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Assassin class.
    /// Handles stealth, quick strikes, backstab, and dash animations.
    /// </summary>
    public class AssassinController : ICharacterClassController
    {
        private static readonly int StealthHash = Animator.StringToHash("Stealth");
        private static readonly int QuickStrikeHash = Animator.StringToHash("QuickStrike");
        private static readonly int BackstabAttackHash = Animator.StringToHash("BackstabAttack");
        private static readonly int DashHash = Animator.StringToHash("Dash");

        private Animator _animator;
        private bool _inStealth;

        public CharacterClassType Class => CharacterClassType.Assassin;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _inStealth = false;
        }

        public void UpdateAnimations()
        {
            // Future: Update stealth state, dash direction, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[AssassinController] Activated");
        }

        public void OnDeactivated()
        {
            _inStealth = false;
            _animator?.SetBool(StealthHash, false);
            Debug.Log("[AssassinController] Deactivated");
        }

        // Public API

        public void SetStealth(bool inStealth)
        {
            _inStealth = inStealth;
            _animator?.SetBool(StealthHash, inStealth);
        }

        public void TriggerQuickStrike()
        {
            _animator?.SetTrigger(QuickStrikeHash);
        }

        public void TriggerBackstab()
        {
            _animator?.SetTrigger(BackstabAttackHash);
        }

        public void TriggerDash()
        {
            _animator?.SetTrigger(DashHash);
        }
    }
}

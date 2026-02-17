using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Fighter class.
    /// Handles melee combo attacks and heavy attacks.
    /// </summary>
    public class FighterController : ICharacterClassController
    {
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");

        private Animator _animator;
        private int _currentCombo;
        private const int MaxComboCount = 3;

        public CharacterClassType Class => CharacterClassType.Fighter;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _currentCombo = 0;
        }

        public void UpdateAnimations()
        {
            // Currently no per-frame updates needed
            // Future: Track combo timing window, auto-reset combo after timeout
        }

        public void OnActivated()
        {
            Debug.Log("[FighterController] Activated");
            ResetCombo();
        }

        public void OnDeactivated()
        {
            // Reset state when switching classes
            ResetCombo();
            Debug.Log("[FighterController] Deactivated");
        }

        // Public API for controlling fighter animations

        /// <summary>
        /// Trigger next attack in combo chain.
        /// Called when server confirms attack ability.
        /// </summary>
        public void TriggerAttack()
        {
            _animator?.SetInteger(ComboIndexHash, _currentCombo);
            _animator?.SetTrigger(AttackHash);

            Debug.Log($"[FighterController] Attack {_currentCombo + 1} triggered");

            // Increment combo (wraps back to 0 after max)
            _currentCombo = (_currentCombo + 1) % MaxComboCount;
        }

        /// <summary>
        /// Trigger heavy attack animation.
        /// Called when server confirms heavy attack ability.
        /// Resets combo chain.
        /// </summary>
        public void TriggerHeavyAttack()
        {
            _animator?.SetTrigger(HeavyAttackHash);
            Debug.Log("[FighterController] Heavy Attack triggered");

            // Heavy attack resets combo
            ResetCombo();
        }

        /// <summary>
        /// Reset combo chain to first attack.
        /// Called when combo times out or heavy attack is used.
        /// </summary>
        public void ResetCombo()
        {
            _currentCombo = 0;
            _animator?.SetInteger(ComboIndexHash, 0);
        }

        // Animation event callbacks (called from animation clips)

        public void OnAttackHit()
        {
            // Called from animation event at impact frame
            // Apply damage/knockback here
            Debug.Log("[FighterController] Attack hit (animation event)");
        }

        public void OnComboWindowStart()
        {
            // Called from animation event when combo input window opens
            Debug.Log("[FighterController] Combo window started (animation event)");
        }

        public void OnComboWindowEnd()
        {
            // Called from animation event when combo input window closes
            // If no input received, reset combo
            Debug.Log("[FighterController] Combo window ended (animation event)");
        }

        public void OnAttackComplete()
        {
            // Called from animation event when attack animation completes
            Debug.Log("[FighterController] Attack complete (animation event)");
        }
    }
}

using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Tank class.
    /// Handles blocking, shield bash, taunt, and ground slam animations.
    /// </summary>
    public class TankController : ICharacterClassController
    {
        private static readonly int IsBlockingHash = Animator.StringToHash("IsBlocking");
        private static readonly int ShieldBashHash = Animator.StringToHash("ShieldBash");
        private static readonly int TauntHash = Animator.StringToHash("Taunt");
        private static readonly int GroundSlamHash = Animator.StringToHash("GroundSlam");

        private Animator _animator;
        private bool _isBlocking;

        public CharacterClassType Class => CharacterClassType.Tank;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _isBlocking = false;
        }

        public void UpdateAnimations()
        {
            // Future: Update block direction, shield durability, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[TankController] Activated");
        }

        public void OnDeactivated()
        {
            _isBlocking = false;
            _animator?.SetBool(IsBlockingHash, false);
            Debug.Log("[TankController] Deactivated");
        }

        // Public API

        public void SetBlocking(bool isBlocking)
        {
            _isBlocking = isBlocking;
            _animator?.SetBool(IsBlockingHash, isBlocking);
        }

        public void TriggerShieldBash()
        {
            _animator?.SetTrigger(ShieldBashHash);
        }

        public void TriggerTaunt()
        {
            _animator?.SetTrigger(TauntHash);
        }

        public void TriggerGroundSlam()
        {
            _animator?.SetTrigger(GroundSlamHash);
        }
    }
}

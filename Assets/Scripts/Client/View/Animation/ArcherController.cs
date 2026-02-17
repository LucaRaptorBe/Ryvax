using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Archer class.
    /// Handles aiming, shooting, and reloading animations.
    /// </summary>
    public class ArcherController : ICharacterClassController
    {
        private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int ReloadHash = Animator.StringToHash("Reload");

        private Animator _animator;
        private bool _isAiming;

        public CharacterClassType Class => CharacterClassType.Archer;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _isAiming = false;
        }

        public void UpdateAnimations()
        {
            // Currently no per-frame updates needed
            // Future: Update aim direction, arrow draw strength, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[ArcherController] Activated");
        }

        public void OnDeactivated()
        {
            // Reset state when switching classes
            _isAiming = false;
            _animator?.SetBool(IsAimingHash, false);
            Debug.Log("[ArcherController] Deactivated");
        }

        // Public API for controlling archer animations

        /// <summary>
        /// Set aiming state (hold/release right mouse button).
        /// </summary>
        public void SetAiming(bool isAiming)
        {
            _isAiming = isAiming;
            _animator?.SetBool(IsAimingHash, isAiming);
        }

        /// <summary>
        /// Trigger shoot animation (fire arrow).
        /// Called when server confirms ability use.
        /// </summary>
        public void TriggerShoot()
        {
            if (!_isAiming)
            {
                Debug.LogWarning("[ArcherController] Cannot shoot while not aiming");
                return;
            }
            _animator?.SetTrigger(ShootHash);
        }

        /// <summary>
        /// Trigger reload animation.
        /// Called when server confirms reload ability.
        /// </summary>
        public void TriggerReload()
        {
            _animator?.SetTrigger(ReloadHash);
        }

        // Animation event callbacks (called from animation clips)

        public void OnArrowRelease()
        {
            // Called from animation event at arrow release frame
            // Spawn visual arrow projectile here
            Debug.Log("[ArcherController] Arrow released (animation event)");
        }

        public void OnShootComplete()
        {
            // Called from animation event when shoot animation completes
            Debug.Log("[ArcherController] Shoot complete (animation event)");
        }

        public void OnReloadComplete()
        {
            // Called from animation event when reload completes
            Debug.Log("[ArcherController] Reload complete (animation event)");
        }
    }
}

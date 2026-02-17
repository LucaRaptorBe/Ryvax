using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Warrior class.
    /// Handles charge, whirlwind, execute, and battle shout animations.
    /// </summary>
    public class WarriorController : ICharacterClassController
    {
        private static readonly int ChargeHash = Animator.StringToHash("Charge");
        private static readonly int WhirlwindHash = Animator.StringToHash("Whirlwind");
        private static readonly int ExecuteHash = Animator.StringToHash("Execute");
        private static readonly int BattleShoutHash = Animator.StringToHash("BattleShout");

        private Animator _animator;

        public CharacterClassType Class => CharacterClassType.Warrior;

        public void Initialize(Animator animator)
        {
            _animator = animator;
        }

        public void UpdateAnimations()
        {
            // Future: Update charge direction, whirlwind speed, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[WarriorController] Activated");
        }

        public void OnDeactivated()
        {
            Debug.Log("[WarriorController] Deactivated");
        }

        // Public API

        public void TriggerCharge()
        {
            _animator?.SetTrigger(ChargeHash);
        }

        public void TriggerWhirlwind()
        {
            _animator?.SetTrigger(WhirlwindHash);
        }

        public void TriggerExecute()
        {
            _animator?.SetTrigger(ExecuteHash);
        }

        public void TriggerBattleShout()
        {
            _animator?.SetTrigger(BattleShoutHash);
        }
    }
}

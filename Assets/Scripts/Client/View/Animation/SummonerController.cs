using UnityEngine;

namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Animation controller for the Summoner class.
    /// Handles summoning animations (minion, elemental, demon).
    /// </summary>
    public class SummonerController : ICharacterClassController
    {
        private static readonly int IsSummoningHash = Animator.StringToHash("IsSummoning");
        private static readonly int SummonMinionHash = Animator.StringToHash("SummonMinion");
        private static readonly int SummonElementalHash = Animator.StringToHash("SummonElemental");
        private static readonly int SummonDemonHash = Animator.StringToHash("SummonDemon");

        private Animator _animator;
        private bool _isSummoning;

        public CharacterClassType Class => CharacterClassType.Summoner;

        public void Initialize(Animator animator)
        {
            _animator = animator;
            _isSummoning = false;
        }

        public void UpdateAnimations()
        {
            // Future: Update summon position, summon type, etc.
        }

        public void OnActivated()
        {
            Debug.Log("[SummonerController] Activated");
        }

        public void OnDeactivated()
        {
            _isSummoning = false;
            _animator?.SetBool(IsSummoningHash, false);
            Debug.Log("[SummonerController] Deactivated");
        }

        // Public API

        public void SummonMinion()
        {
            _isSummoning = true;
            _animator?.SetBool(IsSummoningHash, true);
            _animator?.SetTrigger(SummonMinionHash);
        }

        public void SummonElemental()
        {
            _isSummoning = true;
            _animator?.SetBool(IsSummoningHash, true);
            _animator?.SetTrigger(SummonElementalHash);
        }

        public void SummonDemon()
        {
            _isSummoning = true;
            _animator?.SetBool(IsSummoningHash, true);
            _animator?.SetTrigger(SummonDemonHash);
        }

        public void OnSummonComplete()
        {
            _isSummoning = false;
            _animator?.SetBool(IsSummoningHash, false);
        }
    }
}

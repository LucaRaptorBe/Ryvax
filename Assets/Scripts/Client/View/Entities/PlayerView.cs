// PlayerView.cs - Player-specific view (V5.0 Simplified)
// Both local and remote players use the same rendering path:
// - OnSnapshotReceived() stores server state
// - UpdatePosition() applies dead-reckoning + snap smoothing
// See EntityView.cs header for full data flow documentation.

using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.UnityView.Core;
using MOBANet.Diagnostics;
using MOBANet.Client.Animation;
using MOBANet.Client.HUD;

namespace MOBANet.UnityView.Entities
{
    /// <summary>
    /// Player-specific view that extends EntityView.
    /// V5.0: Unified rendering for local and remote players.
    /// </summary>
    public class PlayerView : EntityView
    {
        #region Animator Hashes

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
        private static readonly int IsFallingHash = Animator.StringToHash("IsFalling");

        #endregion

        #region Player-Specific Fields

        private SimPlayer _simPlayer;
        private AnimatorLayerManager _layerManager;
        private ICharacterClassController _classController;
        private CharacterClassType _currentClass;
        private HealthBarUI _healthBar;

        #endregion

        #region Properties

        public SimPlayer SimPlayer => _simPlayer;

        #endregion

        #region Initialization

        protected virtual void Awake()
        {
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (_animator != null)
            {
                _layerManager = new AnimatorLayerManager(_animator);
            }
        }

        /// <summary>
        /// Initialize PlayerView with SimPlayer reference.
        /// </summary>
        public void Initialize(SimPlayer simPlayer, bool isLocal, NetworkClient networkClient)
        {
            _simPlayer = simPlayer;
            _currentClass = (CharacterClassType)simPlayer.ClassId;

            // Activate the appropriate class layer
            if (_layerManager != null)
            {
                _layerManager.SetActiveClass(_currentClass);
            }

            // Create class-specific controller
            _classController = CreateClassController(_currentClass);
            if (_classController != null && _animator != null)
            {
                _classController.Initialize(_animator);
                _classController.OnActivated();
            }

            transform.position = simPlayer.Transform.Position;

            // Ensure a collider exists for mouse targeting raycasts
            if (GetComponentInChildren<Collider>() == null)
            {
                var col = gameObject.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 1f, 0f);
                col.radius = 0.5f;
                col.height = 2f;
            }

            base.Initialize(simPlayer.Id, isLocal, networkClient);

            // Create health bar
            var healthBarGO = new GameObject("HealthBar");
            healthBarGO.transform.SetParent(transform);
            _healthBar = healthBarGO.AddComponent<HealthBarUI>();
            byte localTeamId = networkClient != null ? networkClient.GetLocalTeamId() : (byte)0;
            _healthBar.Initialize(transform, isLocal, simPlayer.TeamId, localTeamId);
        }

        #endregion

        #region Position Update

        protected override void UpdatePosition()
        {
            // Fallback when not connected (offline/editor testing)
            if (!_hasSnapshot && _simPlayer != null)
            {
                transform.position = _simPlayer.Transform.Position;
                transform.rotation = Quaternion.Euler(0f, _simPlayer.Transform.RotationY, 0f);
                return;
            }

            // Unified path: both local and remote use base dead-reckoning + snap smoothing
            base.UpdatePosition();

            // LOG: Final render position for local player
            if (_isLocalPlayer)
            {
                MovementCycleLogger.LogRenderPosition(transform.position);
            }
        }

        #endregion

        #region Animation

        protected override void UpdateAnimation()
        {
            if (_simPlayer == null) return;

            // Update health bar
            if (_healthBar != null)
            {
                _healthBar.UpdateHealth(_simPlayer.Stats.HealthPercent);
                _healthBar.SetVisible(_simPlayer.Stats.IsAlive);
            }

            if (_animator == null) return;

            // Update base layer animations (locomotion, jump, fall)
            UpdateBaseLayerAnimations();

            // Update class-specific animations
            _classController?.UpdateAnimations();
        }

        private void UpdateBaseLayerAnimations()
        {
            Vector3 velocity = _simPlayer.Transform.EffectiveVelocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            bool isMoving = horizontalSpeed > 0.1f;
            // TODO: IsGrounded is never synced in snapshots — always true client-side.
            // Wire it up once jump is functional (needs IsGrounded in EntityState + ApplyTransformState).
            bool isGrounded = _simPlayer.Transform.IsGrounded;
            // TODO: Replace 8f with the entity's effective move speed once per-entity stats exist
            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);

            // Update locomotion parameters
            _animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
            _animator.SetBool(IsMovingHash, isMoving);
            _animator.SetBool(IsGroundedHash, isGrounded);

            // Debug: Log speed values (uncomment when needed)
            // if (_isLocalPlayer && Time.frameCount % 60 == 0)
            // {
            //     Debug.Log($"[PlayerView Animation] Speed: {normalizedSpeed:F2} (Raw: {horizontalSpeed:F2} u/s) | " +
            //               $"Velocity: {velocity} | Moving: {isMoving} | Grounded: {isGrounded}");
            // }

            // Update jump/fall parameters based on vertical velocity
            float verticalVelocity = velocity.y;
            bool isJumping = !isGrounded && verticalVelocity > 0.5f;
            bool isFalling = !isGrounded && verticalVelocity < -0.5f;

            _animator.SetBool(IsJumpingHash, isJumping);
            _animator.SetBool(IsFallingHash, isFalling);
        }

        #endregion

        #region Class System

        /// <summary>
        /// Create the appropriate class controller based on ClassId.
        /// </summary>
        private ICharacterClassController CreateClassController(CharacterClassType classType)
        {
            switch (classType)
            {
                case CharacterClassType.Archer:
                    return new ArcherController();
                case CharacterClassType.Mage:
                    return new MageController();
                case CharacterClassType.Fighter:
                    return new FighterController();
                case CharacterClassType.Assassin:
                    return new AssassinController();
                case CharacterClassType.Tank:
                    return new TankController();
                case CharacterClassType.Healer:
                    return new HealerController();
                case CharacterClassType.Summoner:
                    return new SummonerController();
                case CharacterClassType.Warrior:
                    return new WarriorController();
                case CharacterClassType.None:
                default:
                    Debug.LogWarning($"[PlayerView] No class controller for ClassId: {classType}");
                    return null;
            }
        }

        /// <summary>
        /// Change class at runtime (e.g. from ClassAssign event).
        /// Deactivates old controller, creates new one, updates animation layer.
        /// </summary>
        public void SetClass(CharacterClassType classType)
        {
            if (classType == _currentClass) return;

            // Deactivate old controller
            _classController?.OnDeactivated();

            _currentClass = classType;

            // Update animation layer
            _layerManager?.SetActiveClass(_currentClass);

            // Create new controller
            _classController = CreateClassController(_currentClass);
            if (_classController != null && _animator != null)
            {
                _classController.Initialize(_animator);
                _classController.OnActivated();
            }
        }

        /// <summary>
        /// Trigger ability animation from network event.
        /// Called by NetworkClient when server confirms ability use.
        /// </summary>
        public void TriggerAbility(byte abilityIndex)
        {
            if (_classController == null)
            {
                Debug.LogWarning($"[PlayerView] No class controller to trigger ability {abilityIndex}");
                return;
            }

            // Route to appropriate class controller
            switch (_classController)
            {
                case ArcherController archer:
                    switch (abilityIndex)
                    {
                        case 0: archer.TriggerShoot(); break;
                        case 1: archer.TriggerReload(); break;
                    }
                    break;

                case MageController mage:
                    switch (abilityIndex)
                    {
                        case 0: mage.CastFireball(); break;
                        case 1: mage.CastMeteor(); break;
                        case 2: mage.CastHeal(); break;
                    }
                    break;

                case FighterController fighter:
                    switch (abilityIndex)
                    {
                        case 0: fighter.TriggerAttack(); break;
                        case 1: fighter.TriggerHeavyAttack(); break;
                    }
                    break;

                case AssassinController assassin:
                    switch (abilityIndex)
                    {
                        case 0: assassin.TriggerQuickStrike(); break;
                        case 1: assassin.TriggerDash(); break;
                        case 2: assassin.TriggerBackstab(); break;
                    }
                    break;

                case TankController tank:
                    switch (abilityIndex)
                    {
                        case 0: tank.TriggerShieldBash(); break;
                        case 1: tank.TriggerTaunt(); break;
                        case 2: tank.TriggerGroundSlam(); break;
                    }
                    break;

                case HealerController healer:
                    switch (abilityIndex)
                    {
                        case 0: healer.CastHealSingle(); break;
                        case 1: healer.CastHealAOE(); break;
                        case 2: healer.CastResurrect(); break;
                    }
                    break;

                case SummonerController summoner:
                    switch (abilityIndex)
                    {
                        case 0: summoner.SummonMinion(); break;
                        case 1: summoner.SummonElemental(); break;
                        case 2: summoner.SummonDemon(); break;
                    }
                    break;

                case WarriorController warrior:
                    switch (abilityIndex)
                    {
                        case 0: warrior.TriggerCharge(); break;
                        case 1: warrior.TriggerWhirlwind(); break;
                        case 2: warrior.TriggerExecute(); break;
                        case 3: warrior.TriggerBattleShout(); break;
                    }
                    break;
            }
        }

        #endregion

        #region Combat Events

        public override void OnDamage(int amount)
        {
            base.OnDamage(amount); // trigger Hit animation
            FloatingDamageText.Spawn(amount, transform.position + Vector3.up * 2f);
        }

        public override void OnDeath()
        {
            base.OnDeath();
            if (_healthBar != null)
                _healthBar.SetVisible(false);
        }

        public override void OnRespawn(Vector3 position)
        {
            base.OnRespawn(position);
            if (_healthBar != null)
                _healthBar.SetVisible(true);
        }

        #endregion

        #region Cleanup

        protected virtual void OnDestroy()
        {
            _classController?.OnDeactivated();
            _classController = null;
            _simPlayer = null;
        }

        #endregion
    }
}

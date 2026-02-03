// PlayerView.cs - Player-specific view (V5.0 Simplified)
// Both local and remote players use the same rendering path:
// - OnSnapshotReceived() stores server state
// - UpdatePosition() applies dead-reckoning + snap smoothing
// See EntityView.cs header for full data flow documentation.

using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.UnityView.Core;
using MOBANet.Diagnostics;

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

        #endregion

        #region Player-Specific Fields

        private SimPlayer _simPlayer;

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
        }

        /// <summary>
        /// Initialize PlayerView with SimPlayer reference.
        /// </summary>
        public void Initialize(SimPlayer simPlayer, bool isLocal, NetworkClient networkClient)
        {
            _simPlayer = simPlayer;

            transform.position = simPlayer.Transform.Position;

            base.Initialize(simPlayer.Id, isLocal, networkClient);
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
            if (_animator == null || _simPlayer == null) return;

            // Use velocity magnitude to determine if moving
            Vector3 velocity = _simPlayer.Transform.Velocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            bool isMoving = horizontalSpeed > 0.1f;
            bool isGrounded = _simPlayer.Transform.IsGrounded;

            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);

            _animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
            _animator.SetBool(IsMovingHash, isMoving);
            _animator.SetBool(IsGroundedHash, isGrounded);
        }

        #endregion

        #region Cleanup

        protected virtual void OnDestroy()
        {
            _simPlayer = null;
        }

        #endregion
    }
}

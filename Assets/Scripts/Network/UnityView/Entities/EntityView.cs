// EntityView.cs - Visual representation of a simulation entity
// Handles interpolation for remote entities and prediction follow for local

using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.UnityView.Interpolation;
using MOBANet.NetAdapter.Messages;
using MOBANet.Shared;

// Aliases to avoid ambiguity between GameSim.Entities.EntityState (enum) and NetAdapter.Messages.EntityState (struct)
using SimEntityState = MOBANet.GameSim.Entities.EntityState;
using NetEntityState = MOBANet.NetAdapter.Messages.EntityState;

namespace MOBANet.UnityView.Entities
{
    /// <summary>
    /// Visual representation of a simulation entity.
    /// Handles smooth visual updates via interpolation or prediction.
    /// </summary>
    public class EntityView : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Entity Info")]
        [SerializeField] private uint _entityId;
        [SerializeField] private EntityType _entityType;
        [SerializeField] private bool _isLocalPlayer;

        [Header("Interpolation Settings")]
        [Tooltip("Exponential decay rate for position (k value). Higher = faster convergence.")]
        [SerializeField] private float _positionSmoothingK = NetcodeConstants.POSITION_SMOOTHING_K;
        [Tooltip("Exponential decay rate for rotation (k value). Higher = faster convergence.")]
        [SerializeField] private float _rotationSmoothingK = NetcodeConstants.ROTATION_SMOOTHING_K;

        [Header("Visual References")]
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Animator _animator;

        #endregion

        #region Private Fields

        private InterpolationBuffer _interpolationBuffer;
        private Vector3 _targetPosition;
        private float _targetRotationY;
        private byte _currentState;
        private bool _isInitialized;

        // Reference to NetworkClient for perceived server time (network time)
        private MOBANet.UnityView.Core.NetworkClient _networkClient;

        #endregion

        #region Properties

        /// <summary>
        /// Simulation entity ID
        /// </summary>
        public uint EntityId => _entityId;

        /// <summary>
        /// Entity type
        /// </summary>
        public EntityType EntityType => _entityType;

        /// <summary>
        /// Is this the local player?
        /// </summary>
        public bool IsLocalPlayer => _isLocalPlayer;

        /// <summary>
        /// Is the entity initialized?
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Current entity state
        /// </summary>
        public byte CurrentState => _currentState;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the entity view
        /// </summary>
        /// <param name="entityId">Entity ID</param>
        /// <param name="type">Entity type</param>
        /// <param name="isLocalPlayer">Is this the local player?</param>
        /// <param name="gameLoop">NetworkClient reference for network time (required for remote entities)</param>
        public void Initialize(uint entityId, EntityType type, bool isLocalPlayer, MOBANet.UnityView.Core.NetworkClient gameLoop = null)
        {
            _entityId = entityId;
            _entityType = type;
            _isLocalPlayer = isLocalPlayer;
            _networkClient = gameLoop;

            if (!isLocalPlayer)
            {
                _interpolationBuffer = new InterpolationBuffer();

                if (_networkClient == null)
                {
                    Debug.LogWarning($"[EntityView] Remote entity {entityId} initialized without NetworkClient reference! Interpolation may not work correctly.");
                }
            }

            _targetPosition = transform.position;
            _targetRotationY = transform.eulerAngles.y;
            _isInitialized = true;
        }

        /// <summary>
        /// Initialize with spawn position
        /// </summary>
        public void Initialize(uint entityId, EntityType type, bool isLocalPlayer, Vector3 spawnPosition, MOBANet.UnityView.Core.NetworkClient gameLoop = null)
        {
            transform.position = spawnPosition;
            _targetPosition = spawnPosition;
            Initialize(entityId, type, isLocalPlayer, gameLoop);
        }

        #endregion

        #region State Updates

        /// <summary>
        /// Called when receiving network state (for remote entities)
        /// </summary>
        public void OnStateReceived(uint tick, in NetEntityState state)
        {
            if (_isLocalPlayer)
            {
                // Local player uses prediction, not interpolation
                // But we can use server state for visual corrections if needed
                return;
            }

            _interpolationBuffer?.AddFromEntityState(tick, state);
            _currentState = state.State;
        }

        /// <summary>
        /// Set predicted state (for local player)
        /// </summary>
        public void SetPredictedState(Vector3 position, float rotationY)
        {
            if (!_isLocalPlayer) return;

            _targetPosition = position;
            _targetRotationY = rotationY;
        }

        /// <summary>
        /// Set state directly (for local player with state)
        /// </summary>
        public void SetPredictedState(Vector3 position, float rotationY, byte state)
        {
            SetPredictedState(position, rotationY);
            _currentState = state;
        }

        /// <summary>
        /// Teleport to position (no interpolation)
        /// </summary>
        public void Teleport(Vector3 position, float rotationY)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0, rotationY, 0);
            _targetPosition = position;
            _targetRotationY = rotationY;
            _interpolationBuffer?.Clear();
        }

        #endregion

        #region Unity Lifecycle

        private void Update()
        {
            if (!_isInitialized) return;

            if (_isLocalPlayer)
            {
                UpdateLocalPlayer();
            }
            else
            {
                UpdateRemoteEntity();
            }

            UpdateAnimation();
        }

        private void UpdateLocalPlayer()
        {
            // Framerate-independent exponential smoothing for position
            // Formula: lerp(current, target, 1 - exp(-k * dt))
            // This produces the same result regardless of framerate
            float dt = Time.deltaTime;
            float posT = 1f - Mathf.Exp(-_positionSmoothingK * dt);
            transform.position = Vector3.Lerp(transform.position, _targetPosition, posT);

            // Framerate-independent exponential smoothing for rotation
            float rotT = 1f - Mathf.Exp(-_rotationSmoothingK * dt);
            Quaternion targetRot = Quaternion.Euler(0, _targetRotationY, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotT);
        }

        private void UpdateRemoteEntity()
        {
            if (_interpolationBuffer == null)
            {
                Debug.LogWarning($"[EntityView] Entity {_entityId}: InterpolationBuffer is NULL!");
                return;
            }

            if (_networkClient == null)
            {
                // Pas de référence au NetworkClient, impossible d'interpoler correctement
                return;
            }

            // Utiliser network time (continu) au lieu de wall-clock
            float perceivedServerTime = _networkClient.GetPerceivedServerTime();

            if (_interpolationBuffer.TryInterpolate(perceivedServerTime, out var pos, out var rotY, out var state))
            {
                transform.position = pos;
                transform.rotation = Quaternion.Euler(0, rotY, 0);
                _currentState = state;
            }
        }

        private void UpdateAnimation()
        {
            if (_animator == null) return;

            // Update animator based on state
            // This is a simple example - extend as needed
            _animator.SetInteger("State", _currentState);

            // Calculate movement speed for blend
            float speed = (_targetPosition - transform.position).magnitude / Time.deltaTime;
            _animator.SetFloat("Speed", speed);

            // Update grounded state (derived from Y position)
            // Threshold of 0.1f to handle minor floating point errors
            bool isGrounded = transform.position.y <= 0.1f;
            _animator.SetBool("IsGrounded", isGrounded);

            // DEBUG: Log grounded state changes
            if (_isLocalPlayer && Time.frameCount % 30 == 0) // Log every 30 frames
            {
                Debug.Log($"[EntityView] IsGrounded={isGrounded}, Y={transform.position.y:F2}");
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Called when entity dies
        /// </summary>
        public virtual void OnDeath()
        {
            _currentState = (byte)SimEntityState.Dead;

            if (_animator != null)
            {
                _animator.SetTrigger("Death");
            }

            // Optionally disable or play death animation
            // gameObject.SetActive(false);
        }

        /// <summary>
        /// Called when entity respawns
        /// </summary>
        public virtual void OnRespawn(Vector3 position)
        {
            Teleport(position, 0);
            _currentState = (byte)SimEntityState.Idle;
            gameObject.SetActive(true);

            if (_animator != null)
            {
                _animator.SetTrigger("Respawn");
            }
        }

        /// <summary>
        /// Called when entity takes damage
        /// </summary>
        public virtual void OnDamage(int amount)
        {
            if (_animator != null)
            {
                _animator.SetTrigger("Hit");
            }

            // Could spawn damage VFX here
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get interpolation buffer (for debugging)
        /// </summary>
        public InterpolationBuffer GetInterpolationBuffer()
        {
            return _interpolationBuffer;
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            // Draw target position
            Gizmos.color = _isLocalPlayer ? Color.green : Color.blue;
            Gizmos.DrawWireSphere(_targetPosition, 0.3f);

            // Draw line to target
            Gizmos.DrawLine(transform.position, _targetPosition);
        }

        #endregion
    }
}

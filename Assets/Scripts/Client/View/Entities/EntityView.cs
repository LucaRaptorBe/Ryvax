// EntityView.cs - Visual representation of a simulation entity (V5.0 Simplified)
// Base class for all entity views (players, NPCs, projectiles, etc.)
//
// ═══════════════════════════════════════════════════════════════════════════════
// DATA FLOW (Network → Visual)
// ═══════════════════════════════════════════════════════════════════════════════
//
// Client Input        Server                    Snapshot                View
// ─────────────────────────────────────────────────────────────────────────────
// InputPacket:        Processes intent,         EntityState:            Derives:
// - IntentType        calculates physics        - Position (quantized)  - isMoving
// - Direction/Pos     - Position                - Velocity (quantized)  - animations
// - MovementSeq       - Velocity                - State (byte)          - visual pos
//
// KEY POINTS:
// - Server is authoritative: Position & Velocity come from snapshots
// - Client derives visual states (isMoving, animations) from Velocity
// - No "isMoving" flag is sent over network - it's derived from vel.magnitude
// - Dead-reckoning: visualPos = serverPos + serverVel * timeSinceSnapshot
// - Snap smoothing: lerp towards dead-reckoned pos, snap when velocity ≈ 0
// ═══════════════════════════════════════════════════════════════════════════════

using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter.Messages;
using MOBANet.UnityView.Core;
using MOBANet.Shared;

// Aliases to avoid ambiguity
using SimEntityState = MOBANet.GameSim.Entities.EntityState;
using NetEntityState = MOBANet.NetAdapter.Messages.EntityState;

namespace MOBANet.UnityView.Entities
{
    /// <summary>
    /// Base class for visual representation of simulation entities.
    /// V5.0: Simplified - no interpolation, direct server positions with dead-reckoning.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class EntityView : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Entity Info")]
        [SerializeField] protected uint _entityId;
        [SerializeField] protected EntityType _entityType;
        [SerializeField] protected bool _isLocalPlayer;

        [Header("Visual References")]
        [SerializeField] protected Transform _visualRoot;
        [SerializeField] protected Animator _animator;

        #endregion

        #region Protected Fields

        protected Vector3 _targetPosition;
        protected float _targetRotationY;
        protected byte _currentState;
        protected bool _isInitialized;
        protected NetworkClient _networkClient;

        // V5.0: Dead-reckoning state (same for local and remote players)
        protected Vector3 _lastServerPos;
        protected Vector3 _lastServerVel;
        protected float _lastServerRotY;
        protected float _lastSnapshotTime;
        protected bool _hasSnapshot;

        // V5.0: Smoothed visual position (snap smoothing)
        protected Vector3 _visualPos;

        #endregion

        #region Properties

        public uint EntityId => _entityId;
        public EntityType EntityType => _entityType;
        public bool IsLocalPlayer => _isLocalPlayer;
        public bool IsInitialized => _isInitialized;
        public byte CurrentState => _currentState;

        #endregion

        #region Initialization

        public virtual void Initialize(uint entityId, bool isLocalPlayer, NetworkClient networkClient)
        {
            _entityId = entityId;
            _isLocalPlayer = isLocalPlayer;
            _networkClient = networkClient;

            _targetPosition = transform.position;
            _targetRotationY = transform.eulerAngles.y;
            _isInitialized = true;

            OnInitialized();
        }

        public virtual void Initialize(uint entityId, EntityType type, bool isLocalPlayer, NetworkClient networkClient)
        {
            _entityType = type;
            Initialize(entityId, isLocalPlayer, networkClient);
        }

        public virtual void Initialize(uint entityId, EntityType type, bool isLocalPlayer, Vector3 spawnPosition, NetworkClient networkClient)
        {
            transform.position = spawnPosition;
            _targetPosition = spawnPosition;
            Initialize(entityId, type, isLocalPlayer, networkClient);
        }

        protected virtual void OnInitialized()
        {
        }

        #endregion

        #region State Updates

        /// <summary>
        /// Called when receiving snapshot (both local and remote players).
        /// V5.0: Stores state for dead-reckoning + snap smoothing.
        /// </summary>
        public virtual void OnSnapshotReceived(Vector3 position, Vector3 velocity, float rotationY)
        {
            // Initialize visual position on first snapshot (no smoothing from origin)
            if (!_hasSnapshot)
            {
                _visualPos = position;
            }

            _lastServerPos = position;
            _lastServerVel = velocity;
            _lastServerRotY = rotationY;
            _lastSnapshotTime = Time.time;
            _hasSnapshot = true;
        }

        /// <summary>
        /// Legacy method for compatibility.
        /// </summary>
        public virtual void AddInterpolationState(uint tick, Vector3 position, float rotationY, byte state = 0)
        {
            OnSnapshotReceived(position, Vector3.zero, rotationY);
            _currentState = state;
        }

        /// <summary>
        /// Called when receiving network state (for all entities)
        /// </summary>
        public virtual void OnStateReceived(uint tick, in NetEntityState state)
        {
            OnSnapshotReceived(state.Position, state.Velocity, state.Rotation);
            _currentState = state.State;
        }

        /// <summary>
        /// Teleport to position (no interpolation)
        /// </summary>
        public virtual void Teleport(Vector3 position, float rotationY)
        {
            transform.position = position;
            transform.rotation = Quaternion.Euler(0, rotationY, 0);
            _targetPosition = position;
            _targetRotationY = rotationY;
            _lastServerPos = position;
            _lastServerVel = Vector3.zero;
            _lastServerRotY = rotationY;
            _lastSnapshotTime = Time.time;
            _hasSnapshot = true;
        }

        #endregion

        #region Unity Lifecycle

        protected virtual void Update()
        {
            if (!_isInitialized) return;

            UpdatePosition();
            UpdateAnimation();
        }

        /// <summary>
        /// Update visual position.
        /// V5.0: Dead-reckoning + snap smoothing.
        /// </summary>
        protected virtual void UpdatePosition()
        {
            if (!_hasSnapshot) return;

            // Dead-reckoning: extrapolate from last known position
            float dt = Time.time - _lastSnapshotTime;
            Vector3 deadReckonedPos = _lastServerPos + _lastServerVel * dt;

            // Snap smoothing: lerp towards dead-reckoned position
            // BUT snap immediately when stopped (velocity ≈ 0) to avoid sliding
            if (_lastServerVel.sqrMagnitude < 0.01f)
            {
                // Stopped: snap directly to server position (no sliding)
                _visualPos = deadReckonedPos;
            }
            else
            {
                // Moving: smooth towards dead-reckoned position
                _visualPos = Vector3.Lerp(_visualPos, deadReckonedPos, NetcodeConstants.VISUAL_SMOOTHING_SPEED * Time.deltaTime);
            }

            transform.position = _visualPos;
            transform.rotation = Quaternion.Euler(0, _lastServerRotY, 0);
        }

        /// <summary>
        /// Get the current smoothed visual position.
        /// </summary>
        public Vector3 GetVisualPosition() => _visualPos;

        /// <summary>
        /// Update animator parameters.
        /// </summary>
        protected virtual void UpdateAnimation()
        {
            if (_animator == null) return;

            _animator.SetInteger("State", _currentState);

            float speed = _lastServerVel.magnitude;
            _animator.SetFloat("Speed", speed);

            bool isGrounded = transform.position.y <= 0.1f;
            _animator.SetBool("IsGrounded", isGrounded);
        }

        #endregion

        #region Events

        public virtual void OnDeath()
        {
            _currentState = (byte)SimEntityState.Dead;

            if (_animator != null)
            {
                _animator.SetTrigger("Death");
            }
        }

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

        public virtual void OnDamage(int amount)
        {
            if (_animator != null)
            {
                _animator.SetTrigger("Hit");
            }
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            Gizmos.color = _isLocalPlayer ? Color.green : Color.blue;
            Gizmos.DrawWireSphere(_targetPosition, 0.3f);
            Gizmos.DrawLine(transform.position, _targetPosition);
        }

        #endregion
    }
}

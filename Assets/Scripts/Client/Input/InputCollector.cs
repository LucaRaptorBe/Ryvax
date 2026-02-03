// InputCollector.cs - Collecte input utilisateur (V3.0)
// LOL-STYLE: Converts raw input to InputIntent (MoveTo, Stop, Follow)
// Uses IntentBuilder for rate-limiting and lookahead calculation
//
// V2.2 Changes:
// - Propagates immobilize lock from NetworkClient to IntentBuilder
//
// V3.0 Changes:
// - Sets local move intent for instant rotation/animation feedback
// - Position follows server, only rotation/animation respond immediately

using UnityEngine;
using UnityEngine.InputSystem;
using MOBANet.NetAdapter.Messages;
using MOBANet.UnityView.Core;
using MOBANet.Client.Input;
using MOBANet.Diagnostics;

namespace MOBANet.UnityView.Input
{
    /// <summary>
    /// Collecte input utilisateur.
    ///
    /// LOL-STYLE ARCHITECTURE:
    /// - Raw input (WASD, clicks) is converted to InputIntent
    /// - IntentBuilder handles rate-limiting (~10Hz for keyboard)
    /// - Click-to-move sends immediate MoveTo intent
    /// - WASD sends periodic MoveTo towards lookahead point
    ///
    /// DISCRETE EVENTS:
    /// - Jump, spells, attacks remain event-based
    /// - Sent via SendEventCommand() with ack/retry
    ///
    /// V2.2: When immobilized (CC), movement intents are suppressed
    /// and IntentBuilder emits Stop at 2Hz instead.
    ///
    /// V3.0: Pure LoL style - sets local move intent for rotation/animation
    /// feedback. Position follows server, no local prediction.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class InputCollector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkClient _networkClient;

        [Header("Settings")]
        [SerializeField] private MovementMode _movementMode = MovementMode.WASD;

        // Intent builder for converting input to intentions
        private IntentBuilder _intentBuilder;

        // Current movement input (for animation/debug)
        private Vector2 _currentMoveInput;
        private bool _currentIsMoving;

        // V3.0: Click-to-move target for intent feedback
        private Vector3 _lastClickTarget;

        // Event commands sequence
        private uint _commandSequence;

        // Debug: track movement state transitions
        private bool _wasMovingLastFrame;

        #region Public Properties

        /// <summary>
        /// Current movement state: true if player is pressing movement keys.
        /// </summary>
        public bool IsMoving => _currentIsMoving;

        /// <summary>
        /// Current move direction (normalized). Only valid when IsMoving is true.
        /// </summary>
        public Vector2 MoveDirection => _currentMoveInput;

        /// <summary>
        /// Current command sequence number (for event commands).
        /// </summary>
        public uint CommandSequence => _commandSequence;

        /// <summary>
        /// Current intent sequence number.
        /// </summary>
        public uint IntentSequence => _intentBuilder?.CurrentSeqId ?? 0;

        #endregion

        void Awake()
        {
            _intentBuilder = new IntentBuilder();
        }

        void Update()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;
            if (_networkClient.LocalEntityId == 0) return;

            // 1. Handle movement input (creates intents)
            HandleMovementInput();

            // 2. Handle discrete events (jump, abilities)
            HandleDiscreteEvents();
        }

        #region Movement Input (Intent-Based)

        /// <summary>
        /// Handle movement input and create InputIntent.
        /// V2.2: Propagates immobilize lock to IntentBuilder.
        /// </summary>
        private void HandleMovementInput()
        {
            // V2.2: Propagate immobilize lock from NetworkClient to IntentBuilder
            bool isImmobilized = _networkClient.IsImmobilized();
            _intentBuilder.SetImmobilizeLock(isImmobilized);

            InputIntent? intent = null;

            switch (_movementMode)
            {
                case MovementMode.WASD:
                    intent = HandleWASDInput();
                    break;
                case MovementMode.ClickToMove:
                    intent = HandleClickToMoveInput();
                    break;
            }

            // Send intent if one was created
            if (intent.HasValue)
            {
                _networkClient.SendInputIntent(intent.Value);
            }
        }

        /// <summary>
        /// Handle WASD/ZQSD keyboard input.
        /// Creates MoveTo intent at 10Hz towards lookahead point.
        /// Creates Stop intent when releasing keys.
        /// V3.0: Sets local move intent for instant rotation/animation feedback.
        /// </summary>
        private InputIntent? HandleWASDInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return null;

            // Sample keyboard direction
            Vector2 dir = Vector2.zero;
            // Forward: W (QWERTY) or Z (AZERTY)
            if (kb.wKey.isPressed || kb.zKey.isPressed) dir.y += 1;
            // Backward: S (both layouts)
            if (kb.sKey.isPressed) dir.y -= 1;
            // Right: D (both layouts)
            if (kb.dKey.isPressed) dir.x += 1;
            // Left: A (QWERTY) or Q (AZERTY)
            if (kb.aKey.isPressed || kb.qKey.isPressed) dir.x -= 1;

            if (dir.sqrMagnitude > 1f) dir = dir.normalized;

            // Update state for animation/debug
            _currentMoveInput = dir;
            _currentIsMoving = dir.sqrMagnitude > 0.01f;

            // Debug: detect movement state transitions
            if (_currentIsMoving && !_wasMovingLastFrame)
            {
                MovementCycleLogger.LogInputStart(dir);
            }
            else if (!_currentIsMoving && _wasMovingLastFrame)
            {
                MovementCycleLogger.LogInputStop();
            }
            _wasMovingLastFrame = _currentIsMoving;

            // V3.0: Set local move intent for immediate rotation/animation feedback
            // Position follows server - only rotation/animation use this intent
            if (_currentIsMoving)
            {
                Vector3 moveDir3D = new Vector3(dir.x, 0f, dir.y);
                _networkClient.SetLocalMoveIntent(moveDir3D);
            }
            else
            {
                _networkClient.ClearLocalMoveIntent();
            }

            // V3.0: Build MoveDir intent from keyboard input (rate-limited to 10Hz)
            // No longer need basePos/visualOffset/speed - server handles movement
            uint clientTick = _networkClient.GetCurrentTick();
            return _intentBuilder.OnKeyboardMove(dir, Time.deltaTime, clientTick);
        }

        /// <summary>
        /// Handle click-to-move input.
        /// Creates immediate MoveTo intent to clicked position.
        /// V3.0: Sets local move intent for instant rotation/animation feedback.
        /// </summary>
        private InputIntent? HandleClickToMoveInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return null;

            // Right-click to move
            if (mouse.rightButton.wasPressedThisFrame)
            {
                Vector3 worldPos = GetMouseWorldPosition();
                if (worldPos != Vector3.zero)
                {
                    _currentIsMoving = true;
                    _lastClickTarget = worldPos;  // V3.0: Store for intent feedback
                    uint clientTick = _networkClient.GetCurrentTick();
                    return _intentBuilder.OnClickToMove(worldPos, clientTick);
                }
            }

            // V3.0: Set local move intent for rotation/animation feedback while moving
            if (_currentIsMoving && _lastClickTarget != Vector3.zero)
            {
                Vector3 currentPos = _networkClient.GetVisualPosition();
                Vector3 toTarget = _lastClickTarget - currentPos;
                toTarget.y = 0;  // XZ plane only

                if (toTarget.sqrMagnitude > 1f)  // Not yet arrived
                {
                    Vector3 moveDir = toTarget.normalized;
                    _networkClient.SetLocalMoveIntent(moveDir);
                }
                else
                {
                    _networkClient.ClearLocalMoveIntent();
                }
            }

            // S key to stop (common in MOBAs)
            var kb = Keyboard.current;
            if (kb != null && kb.sKey.wasPressedThisFrame)
            {
                _currentIsMoving = false;
                _lastClickTarget = Vector3.zero;  // V3.0: Clear target
                _networkClient.ClearLocalMoveIntent();
                uint clientTick = _networkClient.GetCurrentTick();
                return _intentBuilder.CreateStop(clientTick);
            }

            return null;
        }

        #endregion

        #region Discrete Events (Jump, Abilities)

        /// <summary>
        /// Handle discrete event inputs (not movement).
        /// These remain event-based with ack/retry.
        /// </summary>
        private void HandleDiscreteEvents()
        {
            // Jump
            if (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
            {
                HandleJumpInput();
            }

            // Abilities (E/R only when in WASD mode, Q/W used for movement)
            // In ClickToMove mode, Q/W/E/R are all abilities
            HandleAbilityInput();
        }

        private void HandleJumpInput()
        {
            const float GRAVITY = -20f;
            const float JUMP_HEIGHT = 1.5f;
            float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(GRAVITY) * JUMP_HEIGHT);

            var cmd = GameCommand.Launch(++_commandSequence, Vector3.up * jumpVelocity);
            _networkClient.SendEventCommand(cmd);
        }

        private void HandleAbilityInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // In WASD mode, only E and R are abilities (Q/W used for movement)
            // In ClickToMove mode, all Q/W/E/R are abilities
            if (_movementMode == MovementMode.ClickToMove)
            {
                if (kb.qKey.wasPressedThisFrame) CastAbility(0);
                else if (kb.wKey.wasPressedThisFrame) CastAbility(1);
            }

            if (kb.eKey.wasPressedThisFrame) CastAbility(2);
            else if (kb.rKey.wasPressedThisFrame) CastAbility(3);
        }

        private void CastAbility(byte slot)
        {
            Vector3 targetPos = GetMouseWorldPosition();
            var cmd = GameCommand.CastAbility(++_commandSequence, slot, targetPos);
            _networkClient.SendEventCommand(cmd);
        }

        private Vector3 GetMouseWorldPosition()
        {
            if (Camera.main == null || Mouse.current == null) return Vector3.zero;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            int groundLayer = LayerMask.GetMask("Ground");

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            {
                return hit.point;
            }

            return Vector3.zero;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Reset intent builder state (e.g., on respawn).
        /// </summary>
        public void ResetIntentBuilder()
        {
            _intentBuilder?.Reset();
            _currentMoveInput = Vector2.zero;
            _currentIsMoving = false;
        }

        #endregion
    }

    public enum MovementMode
    {
        WASD,
        ClickToMove
    }
}

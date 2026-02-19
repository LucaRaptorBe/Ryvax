// InputCollector.cs - Collecte input utilisateur (V3.1)
// LOL-STYLE: Converts raw input to InputIntent (MoveTo, Stop, Follow)
// Uses IntentBuilder for rate-limiting and lookahead calculation
//
// V2.2 Changes:
// - Propagates immobilize lock from NetworkClient to IntentBuilder
//
// V3.0 Changes:
// - Sets local move intent for instant rotation/animation feedback
// - Position follows server, only rotation/animation respond immediately
//
// V3.1 Changes:
// - Reads keybinds + movement mode from SettingsManager (configurable)
// - Blocks game input when settings UI is open

using UnityEngine;
using UnityEngine.InputSystem;
using MOBANet.NetAdapter.Messages;
using MOBANet.UnityView.Core;
using MOBANet.Client.Input;
using MOBANet.Client.Settings;
using MOBANet.Client.Casting;
using MOBANet.Client.Targeting;
using MOBANet.GameSim.Data;
using MOBANet.Diagnostics;

namespace MOBANet.UnityView.Input
{
    /// <summary>
    /// Collecte input utilisateur.
    ///
    /// LOL-STYLE ARCHITECTURE:
    /// - Raw input (WASD, clicks) is converted to InputIntent
    /// - IntentBuilder handles rate-limiting (120Hz for keyboard)
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
    ///
    /// V3.1: Keybinds and movement mode read from SettingsManager.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class InputCollector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkClient _networkClient;

        // Intent builder for converting input to intentions
        private IntentBuilder _intentBuilder;

        // Current movement input (for animation/debug)
        private Vector2 _currentMoveInput;
        private bool _currentIsMoving;

        // V3.0: Click-to-move target for intent feedback
        private Vector3 _lastClickTarget;

        // Debug: track movement state transitions
        private bool _wasMovingLastFrame;

        // Cast indicator system
        private CastController _castController;
        private SkillshotIndicator _skillshotIndicator;

        // Targeting system
        private TargetingSystem _targetingSystem;
        private bool _targetingSystemInitialized;

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
        /// Current intent sequence number.
        /// </summary>
        public uint IntentSequence => _intentBuilder?.CurrentSeqId ?? 0;

        #endregion

        /// <summary>
        /// Expose the targeting system for HUD integration (TargetInfoUI).
        /// </summary>
        public TargetingSystem TargetingSystem => _targetingSystem;

        void Awake()
        {
            _intentBuilder = new IntentBuilder();
            CreateCastController();

            // Create targeting system
            _targetingSystem = gameObject.AddComponent<TargetingSystem>();
        }

        void Update()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;
            if (_networkClient.LocalEntityId == 0) return;

            // Lazy-init targeting system when NetworkClient is available
            if (_targetingSystem != null && !_targetingSystemInitialized)
            {
                _targetingSystem.Initialize(_networkClient);
                _targetingSystemInitialized = true;
            }

            // V3.1: Block all game input when settings UI is open
            if (SettingsManager.IsUIBlockingInput) return;

            // 1. Handle movement input (creates intents)
            HandleMovementInput();

            // 2. Update aiming indicator every frame if active
            _castController?.UpdateAiming();

            // 3. Handle discrete events (jump, abilities)
            HandleDiscreteEvents();

            // 4. Handle targeting input (Tab, Escape)
            HandleTargetingInput();
        }

        #region Settings Helpers

        /// <summary>
        /// Get current movement mode from settings, fallback to WASD.
        /// </summary>
        private MovementMode GetMovementMode()
        {
            return SettingsManager.Instance?.CurrentSettings?.movementMode ?? MovementMode.WASD;
        }

        /// <summary>
        /// Get current game settings, or null if SettingsManager not yet initialized.
        /// </summary>
        private GameSettings GetSettings()
        {
            return SettingsManager.Instance?.CurrentSettings;
        }

        /// <summary>
        /// Check if a KeyCode was pressed this frame using the New Input System.
        /// Converts KeyCode to InputSystem Key and checks wasPressedThisFrame.
        /// </summary>
        private bool IsKeyPressed(KeyCode keyCode)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            var key = KeyCodeToKey(keyCode);
            return key.HasValue && kb[key.Value].wasPressedThisFrame;
        }

        /// <summary>
        /// Check if a KeyCode was released this frame (for QuickCastWithIndicator).
        /// </summary>
        private bool IsKeyReleased(KeyCode keyCode)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            var key = KeyCodeToKey(keyCode);
            return key.HasValue && kb[key.Value].wasReleasedThisFrame;
        }

        /// <summary>
        /// Convert legacy KeyCode to New Input System Key.
        /// Covers the most common keys used for MOBA keybinds.
        /// </summary>
        private static Key? KeyCodeToKey(KeyCode kc) => kc switch
        {
            KeyCode.A => Key.A, KeyCode.B => Key.B, KeyCode.C => Key.C, KeyCode.D => Key.D,
            KeyCode.E => Key.E, KeyCode.F => Key.F, KeyCode.G => Key.G, KeyCode.H => Key.H,
            KeyCode.I => Key.I, KeyCode.J => Key.J, KeyCode.K => Key.K, KeyCode.L => Key.L,
            KeyCode.M => Key.M, KeyCode.N => Key.N, KeyCode.O => Key.O, KeyCode.P => Key.P,
            KeyCode.Q => Key.Q, KeyCode.R => Key.R, KeyCode.S => Key.S, KeyCode.T => Key.T,
            KeyCode.U => Key.U, KeyCode.V => Key.V, KeyCode.W => Key.W, KeyCode.X => Key.X,
            KeyCode.Y => Key.Y, KeyCode.Z => Key.Z,
            KeyCode.Alpha0 => Key.Digit0, KeyCode.Alpha1 => Key.Digit1,
            KeyCode.Alpha2 => Key.Digit2, KeyCode.Alpha3 => Key.Digit3,
            KeyCode.Alpha4 => Key.Digit4, KeyCode.Alpha5 => Key.Digit5,
            KeyCode.Alpha6 => Key.Digit6, KeyCode.Alpha7 => Key.Digit7,
            KeyCode.Alpha8 => Key.Digit8, KeyCode.Alpha9 => Key.Digit9,
            KeyCode.Space => Key.Space, KeyCode.Tab => Key.Tab,
            KeyCode.LeftShift => Key.LeftShift, KeyCode.RightShift => Key.RightShift,
            KeyCode.LeftControl => Key.LeftCtrl, KeyCode.RightControl => Key.RightCtrl,
            KeyCode.LeftAlt => Key.LeftAlt, KeyCode.RightAlt => Key.RightAlt,
            KeyCode.BackQuote => Key.Backquote, KeyCode.Minus => Key.Minus,
            KeyCode.Equals => Key.Equals,
            KeyCode.F1 => Key.F1, KeyCode.F2 => Key.F2, KeyCode.F3 => Key.F3,
            KeyCode.F4 => Key.F4, KeyCode.F5 => Key.F5, KeyCode.F6 => Key.F6,
            _ => null
        };

        #endregion

        #region Movement Input (Intent-Based)

        /// <summary>
        /// Handle movement input and create InputIntent.
        /// V2.2: Propagates immobilize lock to IntentBuilder.
        /// V3.1: Reads movement mode from settings.
        /// </summary>
        private void HandleMovementInput()
        {
            // V2.2: Propagate immobilize lock from NetworkClient to IntentBuilder
            bool isImmobilized = _networkClient.IsImmobilized();
            _intentBuilder.SetImmobilizeLock(isImmobilized);

            InputIntent? intent = null;

            switch (GetMovementMode())
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
        /// Creates MoveDir intent at 120Hz towards movement direction.
        /// Creates Stop intent when releasing keys.
        /// V3.0: Sets local move intent for instant rotation/animation feedback.
        /// </summary>
        private InputIntent? HandleWASDInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return null;

            // Sample keyboard direction based on layout setting
            Vector2 dir = Vector2.zero;
            bool azerty = GetSettings()?.keyboardLayout == KeyboardLayout.AZERTY;

            if (azerty)
            {
                // AZERTY: ZQSD
                if (kb.zKey.isPressed) dir.y += 1;
                if (kb.sKey.isPressed) dir.y -= 1;
                if (kb.dKey.isPressed) dir.x += 1;
                if (kb.qKey.isPressed) dir.x -= 1;
            }
            else
            {
                // QWERTY: WASD
                if (kb.wKey.isPressed) dir.y += 1;
                if (kb.sKey.isPressed) dir.y -= 1;
                if (kb.dKey.isPressed) dir.x += 1;
                if (kb.aKey.isPressed) dir.x -= 1;
            }

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

            // V3.0: Build MoveDir intent from keyboard input (rate-limited to 120Hz)
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

            // Right-click: move to ground OR attack enemy (handled by HandleTargetingInput)
            if (mouse.rightButton.wasPressedThisFrame)
            {
                // If RClick hit an enemy, targeting already handled it — don't move
                var enemy = RaycastEnemy();
                if (enemy == null)
                {
                    Vector3 worldPos = GetMouseWorldPosition();
                    if (worldPos != Vector3.zero)
                    {
                        _currentIsMoving = true;
                        _lastClickTarget = worldPos;
                        uint clientTick = _networkClient.GetCurrentTick();
                        return _intentBuilder.OnClickToMove(worldPos, clientTick);
                    }
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

            // V3.1: Abilities use configurable keybinds from settings
            HandleAbilityInput();
        }

        private void HandleJumpInput()
        {
            var cmd = GameCommand.Jump(0);
            _networkClient.SendEventCommand(cmd);
        }

        /// <summary>
        /// V3.1: Reads ability keybinds from GameSettings.
        /// V4.0: Checks cast mode — may enter aiming state instead of immediate cast.
        /// </summary>
        private void HandleAbilityInput()
        {
            // If currently aiming, handle confirm/cancel inputs instead
            if (_castController != null && _castController.IsAiming)
            {
                HandleAimingInput();
                return;
            }

            var settings = GetSettings();
            if (settings == null)
            {
                HandleAbilityInputFallback();
                return;
            }

            // Check all 4 ability keys, skipping any that conflict with movement
            for (byte slot = 0; slot < 4; slot++)
            {
                KeyCode key = settings.GetAbilityKey(slot);
                if (key == KeyCode.None) continue;
                if (GetMovementMode() == MovementMode.WASD && IsMovementKey(key)) continue;
                if (IsKeyPressed(key))
                {
                    TryCastAbility(slot, settings);
                    return;
                }
            }
        }

        /// <summary>
        /// Handle input while the cast controller is in Aiming state.
        /// NormalCast: 2nd press of same key or LClick = confirm, RMB = cancel.
        /// QuickCastWithIndicator: key release = confirm, RMB = cancel.
        /// </summary>
        private void HandleAimingInput()
        {
            var mouse = Mouse.current;
            var settings = GetSettings();
            CastMode mode = _castController.ActiveMode;
            byte activeSlot = _castController.ActiveSlot;

            // RMB cancels in all modes
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                _castController.CancelCast();
                return;
            }

            // Escape cancels
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _castController.CancelCast();
                return;
            }

            if (mode == CastMode.NormalCast)
            {
                // LClick confirms
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    ConfirmAndSendCast();
                    return;
                }

                // 2nd press of same ability key confirms
                KeyCode activeKey = settings != null ? settings.GetAbilityKey(activeSlot) : KeyCode.None;
                if (activeKey != KeyCode.None && IsKeyPressed(activeKey))
                {
                    ConfirmAndSendCast();
                    return;
                }

                // Pressing a different ability key: cancel current + start new
                if (settings != null)
                {
                    for (byte slot = 0; slot < 4; slot++)
                    {
                        if (slot == activeSlot) continue;
                        KeyCode key = settings.GetAbilityKey(slot);
                        if (key != KeyCode.None && IsKeyPressed(key))
                        {
                            _castController.CancelCast();
                            TryCastAbility(slot, settings);
                            return;
                        }
                    }
                }
            }
            else if (mode == CastMode.QuickCastWithIndicator)
            {
                // Key release confirms
                KeyCode activeKey = settings != null ? settings.GetAbilityKey(activeSlot) : KeyCode.None;
                if (activeKey != KeyCode.None && IsKeyReleased(activeKey))
                {
                    ConfirmAndSendCast();
                    return;
                }
            }
        }

        /// <summary>
        /// Confirm the aiming cast and send the command to the server.
        /// </summary>
        private void ConfirmAndSendCast()
        {
            var result = _castController.ConfirmCast();
            if (result.HasValue)
            {
                var cmd = GameCommand.CastAbility(0, result.Value.slot,
                    new Vector2(result.Value.targetPos.x, result.Value.targetPos.z));
                _networkClient.SendEventCommand(cmd);
            }
        }

        /// <summary>
        /// Try to cast an ability, respecting the current cast mode.
        /// If NormalCast or QCWI and ability is a Skillshot, enters aiming state.
        /// Otherwise fires immediately (QuickCast behavior).
        /// </summary>
        private void TryCastAbility(byte slot, GameSettings settings)
        {
            // Don't show indicator or cast if ability is on cooldown
            if (_networkClient != null && _networkClient.IsAbilityOnCooldown(slot))
                return;

            CastMode castMode = settings.defaultCastMode;

            // Try to get ability data to determine targeting type
            IAbilityDefinition abilityDef = _networkClient?.GetLocalPlayerAbility(slot);
            AbilityTargetType targetType = abilityDef?.TargetType ?? AbilityTargetType.Skillshot;
            float range = abilityDef?.BaseRange ?? 16f;

            // Attempt to start aiming (returns true if aiming started)
            if (_castController != null && _castController.TryStartCast(slot, castMode, targetType, range))
                return; // Aiming started, don't fire yet

            // QuickCast or unsupported target type: fire immediately
            FireAbilityImmediate(slot);
        }

        /// <summary>
        /// Fallback for when SettingsManager is not yet initialized.
        /// Uses the original hardcoded keys with QuickCast behavior.
        /// </summary>
        private void HandleAbilityInputFallback()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // Fallback: Q/E/R (skip W/A in WASD mode since they're movement)
            if (kb.qKey.wasPressedThisFrame) FireAbilityImmediate(0);
            else if (kb.eKey.wasPressedThisFrame) FireAbilityImmediate(2);
            else if (kb.rKey.wasPressedThisFrame) FireAbilityImmediate(3);
        }

        /// <summary>
        /// Check if a KeyCode conflicts with movement keys for the current layout.
        /// </summary>
        private bool IsMovementKey(KeyCode kc)
        {
            bool azerty = GetSettings()?.keyboardLayout == KeyboardLayout.AZERTY;
            if (azerty)
                return kc == KeyCode.Z || kc == KeyCode.Q || kc == KeyCode.S || kc == KeyCode.D;
            else
                return kc == KeyCode.W || kc == KeyCode.A || kc == KeyCode.S || kc == KeyCode.D;
        }

        /// <summary>
        /// Fire an ability immediately (QuickCast style, no indicator).
        /// </summary>
        private void FireAbilityImmediate(byte slot)
        {
            Vector3 targetPos = GetMouseWorldPosition();
            var cmd = GameCommand.CastAbility(0, slot, new Vector2(targetPos.x, targetPos.z));
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

        #region Targeting Input

        private void HandleTargetingInput()
        {
            if (_targetingSystem == null) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // Tab: cycle through enemies
            if (kb != null && kb.tabKey.wasPressedThisFrame)
            {
                _targetingSystem.CycleTarget();
                return;
            }

            // Escape: clear target
            if (kb != null && kb.escapeKey.wasPressedThisFrame && _targetingSystem.HasTarget)
            {
                _targetingSystem.ClearTarget();
                return;
            }

            // Don't process mouse targeting while aiming a spell
            if (_castController != null && _castController.IsAiming) return;

            // LClick on enemy: select/lock target. LClick on nothing: clear.
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var enemy = RaycastEnemy();
                if (enemy != null)
                    _targetingSystem.LockTarget(enemy.EntityId);
                else
                    _targetingSystem.ClearTarget();
            }

            // RClick on enemy: lock target + auto-attack order
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                var enemy = RaycastEnemy();
                if (enemy != null)
                {
                    _targetingSystem.LockTarget(enemy.EntityId);
                    var cmd = GameCommand.AttackTarget(0, enemy.EntityId);
                    _networkClient.SendEventCommand(cmd);
                }
            }
        }

        /// <summary>
        /// Raycast to find an enemy PlayerView under the mouse cursor.
        /// </summary>
        private MOBANet.UnityView.Entities.PlayerView RaycastEnemy()
        {
            if (Camera.main == null || Mouse.current == null) return null;

            Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                var playerView = hit.collider.GetComponentInParent<MOBANet.UnityView.Entities.PlayerView>();
                if (playerView != null && !playerView.IsLocalPlayer)
                {
                    var simPlayer = _networkClient.GetSimPlayer(playerView.EntityId);
                    byte localTeamId = _networkClient.GetLocalTeamId();
                    if (simPlayer != null && simPlayer.TeamId != localTeamId)
                        return playerView;
                }
            }
            return null;
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
            _castController?.CancelCast();
        }

        /// <summary>
        /// Create the SkillshotIndicator GameObject and the CastController.
        /// </summary>
        private void CreateCastController()
        {
            var indicatorGO = new GameObject("SkillshotIndicator");
            indicatorGO.transform.SetParent(transform);
            _skillshotIndicator = indicatorGO.AddComponent<SkillshotIndicator>();

            _castController = new CastController(
                _skillshotIndicator,
                () => _networkClient != null ? _networkClient.GetVisualPosition() : Vector3.zero,
                () => GetMouseWorldPosition()
            );
        }

        #endregion
    }
}

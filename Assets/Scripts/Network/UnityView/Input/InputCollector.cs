// InputCollector.cs - Collecte input utilisateur et envoie commandes réseau
// AUCUNE logique métier - juste conversion input → GameCommand

using UnityEngine;
using UnityEngine.InputSystem;
using MOBANet.NetAdapter.Messages;
using MOBANet.UnityView.Core;

namespace MOBANet.UnityView.Input
{
    /// <summary>
    /// Collecte input utilisateur et envoie commandes réseau.
    /// Responsabilité: UNIQUEMENT input → GameCommand
    /// Architecture: Input layer (UnityView)
    /// </summary>
    public class InputCollector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkClient _networkClient;

        [Header("Settings")]
        [SerializeField] private MovementMode _movementMode = MovementMode.WASD;

        private uint _commandSequence;
        private Vector2 _lastMoveInput;
        private bool _wasMoving;
        private float _lastMoveCommandTime;
        private const float MOVE_HEARTBEAT_INTERVAL = 0.2f;

        void Update()
        {
            if (_networkClient == null || !_networkClient.IsConnected) return;
            if (_networkClient.LocalEntityId == 0) return;

            // 1. Mouvement
            HandleMovementInput();

            // 2. Saut
            if (Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)
            {
                HandleJumpInput();
            }

            // 3. Sorts (Q/W/E/R)
            HandleAbilityInput();
        }

        private void HandleMovementInput()
        {
            Vector2 input = _movementMode switch
            {
                MovementMode.WASD => SampleWASD(),
                MovementMode.ClickToMove => SampleClickToMove(),
                _ => Vector2.zero
            };

            bool isMoving = input.sqrMagnitude > 0.01f;

            // Envoyer commande si:
            // - État change (started/stopped)
            // - Direction change significativement
            // - Heartbeat (pour maintenir connexion vivante)
            bool stateChanged = isMoving != _wasMoving;
            bool directionChanged = isMoving && Vector2.Distance(input.normalized, _lastMoveInput) > 0.1f;
            bool heartbeatNeeded = isMoving && (Time.time - _lastMoveCommandTime > MOVE_HEARTBEAT_INTERVAL);

            if (stateChanged || directionChanged || heartbeatNeeded)
            {
                var cmd = isMoving
                    ? GameCommand.MoveStart(++_commandSequence, input)
                    : GameCommand.MoveStop(++_commandSequence);

                _networkClient.SendCommand(cmd);
                _lastMoveInput = input.normalized;
                _wasMoving = isMoving;
                _lastMoveCommandTime = Time.time;

                Debug.Log($"[InputCollector] Sent command: {(isMoving ? "MoveStart" : "MoveStop")}, seq={_commandSequence}, input={input}");
            }
        }

        private void HandleJumpInput()
        {
            // Calcul vélocité saut (même formule que SimPlayer)
            const float GRAVITY = -20f;
            const float JUMP_HEIGHT = 1.5f;
            float jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(GRAVITY) * JUMP_HEIGHT);

            var cmd = GameCommand.Launch(++_commandSequence, Vector3.up * jumpVelocity);
            _networkClient.SendCommand(cmd);

            Debug.Log($"[InputCollector] Jump command sent");
        }

        private void HandleAbilityInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // Q/W/E/R
            if (kb.qKey.wasPressedThisFrame)
                CastAbility(0);
            else if (kb.wKey.wasPressedThisFrame)
                CastAbility(1);
            else if (kb.eKey.wasPressedThisFrame)
                CastAbility(2);
            else if (kb.rKey.wasPressedThisFrame)
                CastAbility(3);
        }

        private void CastAbility(byte slot)
        {
            // Raycast pour obtenir position cible
            Vector3 targetPos = GetMouseWorldPosition();

            // Envoyer commande (serveur calculera rotation + effet)
            var cmd = GameCommand.CastAbility(++_commandSequence, slot, targetPos);
            _networkClient.SendCommand(cmd);

            Debug.Log($"[InputCollector] Cast ability slot {slot} at {targetPos}");
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

        private Vector2 SampleWASD()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            Vector2 dir = Vector2.zero;
            if (kb.wKey.isPressed) dir.y += 1;
            if (kb.sKey.isPressed) dir.y -= 1;
            if (kb.dKey.isPressed) dir.x += 1;
            if (kb.aKey.isPressed) dir.x -= 1;

            return dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }

        private Vector2 SampleClickToMove()
        {
            // TODO: Implémenter click-to-move si nécessaire
            return Vector2.zero;
        }
    }

    public enum MovementMode
    {
        WASD,
        ClickToMove
    }
}

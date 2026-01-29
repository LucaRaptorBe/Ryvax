// PlayerView.cs - Affiche état SimPlayer (pure view, read-only)
// Anciennement NetPlayerController.cs
// Architecture: View layer (UnityView)

using UnityEngine;
using MOBANet.GameSim.Entities;
using MOBANet.UnityView.Interpolation;
using MOBANet.UnityView.Core;

namespace MOBANet.UnityView.Views
{
    /// <summary>
    /// Affiche l'état SimPlayer (pure view, read-only).
    /// Responsabilité: UNIQUEMENT affichage (position, rotation, animation)
    /// Architecture: View layer (UnityView)
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator animator;

        [Header("Interpolation Settings")]
        [SerializeField] private float positionSmoothSpeed = 20f;

        // Animator parameter hashes
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");

        // Simulation link (read-only)
        private SimPlayer _simPlayer;
        private uint _entityId;
        private bool _isLocalPlayer;

        // Interpolation
        private InterpolationBuffer _interpolationBuffer;
        private NetworkClient _networkClient;
        private bool _useDirectInterpolation;

        // Previous position for velocity calculation
        private Vector3 _previousPosition;

        #region Properties

        public uint EntityId => _entityId;
        public bool IsLocalPlayer => _isLocalPlayer;
        public SimPlayer SimPlayer => _simPlayer;

        #endregion

        #region Initialization

        private void Awake()
        {
            if (animator == null)
            {
                // Try to find animator in children (should be on "Visual")
                animator = GetComponentInChildren<Animator>();

                if (animator == null)
                {
                    Debug.LogError($"[PlayerView] No Animator found on {gameObject.name} or its children!");
                }
                else
                {
                    Debug.Log($"[PlayerView] Animator found on: {animator.gameObject.name}");

                    if (animator.runtimeAnimatorController == null)
                    {
                        Debug.LogError($"[PlayerView] Animator on {animator.gameObject.name} has NO controller assigned!");
                    }
                    else
                    {
                        Debug.Log($"[PlayerView] Animator controller: {animator.runtimeAnimatorController.name}");
                    }
                }
            }
        }

        /// <summary>
        /// Initialize view with SimPlayer.
        /// </summary>
        public void Initialize(SimPlayer simPlayer, bool isLocal, NetworkClient networkClient)
        {
            _simPlayer = simPlayer;
            _entityId = simPlayer.Id;
            _isLocalPlayer = isLocal;
            _networkClient = networkClient;

            // Position initiale
            transform.position = _simPlayer.Transform.Position;
            _previousPosition = _simPlayer.Transform.Position;

            // Tous les joueurs utilisent interpolation (pas de prédiction locale)
            _interpolationBuffer = new InterpolationBuffer();
            _useDirectInterpolation = true; // LoL-style

            // Vérifier les paramètres de l'animator
            if (animator != null)
            {
                var controller = animator.runtimeAnimatorController;
                if (controller != null)
                {
                    bool hasSpeed = false;
                    bool hasIsMoving = false;
                    bool hasIsGrounded = false;

                    foreach (var param in animator.parameters)
                    {
                        if (param.name == "Speed") hasSpeed = true;
                        if (param.name == "IsMoving") hasIsMoving = true;
                        if (param.name == "IsGrounded") hasIsGrounded = true;
                    }

                    Debug.Log($"[PlayerView] Animator parameters check:\n" +
                              $"  Speed: {hasSpeed}\n" +
                              $"  IsMoving: {hasIsMoving}\n" +
                              $"  IsGrounded: {hasIsGrounded}");

                    if (!hasSpeed || !hasIsMoving || !hasIsGrounded)
                    {
                        Debug.LogWarning($"[PlayerView] Missing animator parameters! Speed={hasSpeed}, IsMoving={hasIsMoving}, IsGrounded={hasIsGrounded}");
                    }
                }
            }

            Debug.Log($"[PlayerView] Entity={_entityId} initialized: isLocal={isLocal}");
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            if (_simPlayer == null) return;
            if (_interpolationBuffer == null) return;

            UpdatePosition();
            UpdateAnimator();
        }

        private void UpdatePosition()
        {
            if (_networkClient == null)
            {
                // Fallback: afficher position SimPlayer directement
                transform.position = _simPlayer.Transform.Position;
                transform.rotation = Quaternion.Euler(0f, _simPlayer.Transform.RotationY, 0f);
                return;
            }

            // Obtenir temps serveur perçu
            float perceivedServerTime = _networkClient.GetPerceivedServerTime();

            // Interpoler entre snapshots
            if (!_interpolationBuffer.TryInterpolate(perceivedServerTime, out Vector3 targetPosition, out float targetRotation))
            {
                // Pas assez de données, attendre plus de snapshots
                return;
            }

            if (_useDirectInterpolation)
            {
                // LoL-style: interpolation directe, pas de lissage supplémentaire
                transform.position = targetPosition;
                transform.rotation = Quaternion.Euler(0f, targetRotation, 0f);
            }
            else
            {
                // Avec lissage supplémentaire (exponential smoothing)
                Vector3 currentPosition = transform.position;
                float error = Vector3.Distance(currentPosition, targetPosition);

                if (error > 2.0f) // Threshold
                {
                    // Téléportation directe (trop loin)
                    transform.position = targetPosition;
                }
                else
                {
                    // Interpolation smooth
                    float t = 1f - Mathf.Exp(-Time.deltaTime * positionSmoothSpeed);
                    transform.position = Vector3.Lerp(currentPosition, targetPosition, t);
                }

                // Rotation lissée
                float currentRotation = transform.eulerAngles.y;
                float tRot = 1f - Mathf.Exp(-Time.deltaTime * positionSmoothSpeed);
                float newRotation = Mathf.LerpAngle(currentRotation, targetRotation, tRot);
                transform.rotation = Quaternion.Euler(0f, newRotation, 0f);
            }

            _previousPosition = transform.position;
        }

        private void UpdateAnimator()
        {
            if (animator == null)
            {
                Debug.LogWarning($"[PlayerView] Entity={_entityId}: Animator is NULL!");
                return;
            }

            if (_simPlayer == null)
            {
                Debug.LogWarning($"[PlayerView] Entity={_entityId}: SimPlayer is NULL!");
                return;
            }

            // Lire état depuis SimPlayer (vient du serveur via snapshots)
            Vector3 velocity = _simPlayer.Transform.Velocity;
            float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            bool isMoving = _simPlayer.Transform.IsMoving;
            bool isGrounded = _simPlayer.Transform.IsGrounded;

            // Normaliser la vitesse horizontale par rapport à la vitesse de base (5.0 unités/s)
            float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);

            // Debug logs (throttled)
            if (Time.frameCount % 60 == 0) // Log every 60 frames (~1 second)
            {
                string currentStateName = "UNKNOWN";
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                    currentStateName = $"Hash={stateInfo.shortNameHash}, NormalizedTime={stateInfo.normalizedTime:F2}";
                }

                Debug.Log($"[PlayerView] Entity={_entityId} Animator Update:\n" +
                          $"  Velocity: {velocity}\n" +
                          $"  HorizontalSpeed: {horizontalSpeed:F2}\n" +
                          $"  NormalizedSpeed: {normalizedSpeed:F2}\n" +
                          $"  IsMoving: {isMoving}\n" +
                          $"  IsGrounded: {isGrounded}\n" +
                          $"  Animator: {(animator != null ? animator.name : "NULL")}\n" +
                          $"  Controller: {(animator?.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "NULL")}\n" +
                          $"  Enabled: {(animator != null ? animator.enabled : false)}\n" +
                          $"  CurrentState: {currentStateName}");
            }

            // Mettre à jour les paramètres de l'animator
            animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
            animator.SetBool(IsMovingHash, isMoving);
            animator.SetBool(IsGroundedHash, isGrounded);

            // Note: VerticalVelocity (Velocity.y) est disponible si besoin pour des animations avancées
            // animator.SetFloat("VerticalVelocity", velocity.y);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Ajoute un état pour l'interpolation.
        /// Appelé par NetworkClient quand snapshot reçu.
        /// </summary>
        public void AddInterpolationState(uint tick, Vector3 position, float rotationY)
        {
            _interpolationBuffer?.AddState(tick, position, rotationY, 0);
        }

        #endregion

        #region Cleanup

        private void OnDestroy()
        {
            _simPlayer = null;
            _interpolationBuffer = null;
        }

        #endregion
    }
}

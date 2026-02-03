using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Caméra style MOBA avec vue isométrique fixe.
/// Peut être déplacée avec les bords de l'écran.
/// Les inputs sont gérés par PlayerController.
/// </summary>
public class PlayerCamera : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("Si désactivé, la caméra ne suit plus le joueur")]
    [SerializeField] private bool enableFollowTarget = false;

    [Header("Camera Settings")]
    [SerializeField] private float height = 10f;
    [SerializeField] private float distance = 8f;
    [SerializeField] private float angle = 45f;

    [Header("Edge Panning")]
    [Tooltip("Active le déplacement de la caméra avec les bords de l'écran")]
    [SerializeField] private bool enableEdgePanning = true;
    [Tooltip("Taille de la zone de détection sur les bords (en pixels)")]
    [Range(5f, 50f)]
    [SerializeField] private float edgeSize = 20f;
    [Tooltip("Vitesse de déplacement de la caméra")]
    [Range(5f, 50f)]
    [SerializeField] private float panSpeed = 20f;
    [Tooltip("Distance max horizontale (gauche/droite) du edge panning")]
    [Range(5f, 100f)]
    [SerializeField] private float maxEdgePanningX = 30f;
    [Tooltip("Distance max en profondeur (avant/arrière) du edge panning")]
    [Range(5f, 100f)]
    [SerializeField] private float maxEdgePanningZ = 30f;

    [Header("Dash Camera Offset")]
    [Tooltip("Décalage de la caméra dans la direction du dash")]
    [Range(0f, 50f)]
    [SerializeField] private float dashCameraOffset = 5f;
    [Tooltip("Vitesse de retour de la caméra après un dash")]
    [Range(0.1f, 50f)]
    [SerializeField] private float dashOffsetRecoverySpeed = 3f;

    [Header("Smoothing")]
    [Tooltip("Temps de lissage du mouvement de la caméra (plus bas = plus réactif)")]
    [Range(0.01f, 2f)]
    [SerializeField] private float smoothTime = 0.1f;

    [Header("Look Ahead")]
    [Tooltip("Décalage horizontal (gauche/droite) de la caméra selon le mouvement")]
    [Range(0f, 15f)]
    [SerializeField] private float lookAheadDistanceX = 2f;
    [Tooltip("Décalage en profondeur (avant/arrière) de la caméra selon le mouvement")]
    [Range(0f, 15f)]
    [SerializeField] private float lookAheadDistanceZ = 2f;
    [Tooltip("Vitesse de transition du look ahead")]
    [Range(1f, 20f)]
    [SerializeField] private float lookAheadSpeed = 5f;
    [Tooltip("Durée après un cast où le look ahead est désactivé (pour ne pas bouger en combat)")]
    [Range(0f, 10f)]
    [SerializeField] private float lookAheadCombatCooldown = 2f;

    // État
    private Vector3 panOffset = Vector3.zero;
    private Vector3 dashOffset = Vector3.zero;
    private Vector3 lookAheadOffset = Vector3.zero;
    private Vector3 lastTargetPosition;
    private Vector3 currentVelocity = Vector3.zero;
    private float combatTimer = 0f;

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                target = player.transform;
        }

        if (target != null)
            lastTargetPosition = target.position;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        HandleEdgePanning();
        RecoverDashOffset();
        UpdateLookAhead();
        UpdateCameraPosition();
    }

    private void HandleEdgePanning()
    {
        if (!enableEdgePanning) return;
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;

        // Vérifie que la souris est dans la fenêtre de jeu
        if (mousePos.x < 0 || mousePos.x > screenWidth || mousePos.y < 0 || mousePos.y > screenHeight)
            return;

        Vector3 panDirection = Vector3.zero;

        // Bord gauche
        if (mousePos.x <= edgeSize)
            panDirection.x -= 1f;
        // Bord droit
        if (mousePos.x >= screenWidth - edgeSize)
            panDirection.x += 1f;
        // Bord bas
        if (mousePos.y <= edgeSize)
            panDirection.z -= 1f;
        // Bord haut
        if (mousePos.y >= screenHeight - edgeSize)
            panDirection.z += 1f;

        if (panDirection != Vector3.zero)
        {
            panDirection.Normalize();
            panOffset += panDirection * panSpeed * Time.deltaTime;

            // Limite séparée en X et Z
            panOffset.x = Mathf.Clamp(panOffset.x, -maxEdgePanningX, maxEdgePanningX);
            panOffset.z = Mathf.Clamp(panOffset.z, -maxEdgePanningZ, maxEdgePanningZ);
        }
    }

    private void RecoverDashOffset()
    {
        // Le dash offset revient progressivement à zéro
        if (dashOffset.magnitude > 0.01f)
        {
            dashOffset = Vector3.Lerp(dashOffset, Vector3.zero, dashOffsetRecoverySpeed * Time.deltaTime);
        }
        else
        {
            dashOffset = Vector3.zero;
        }
    }

    private void UpdateLookAhead()
    {
        // Décrémente le timer de combat
        if (combatTimer > 0f)
        {
            combatTimer -= Time.deltaTime;
        }

        // Calcule la direction de mouvement du joueur
        Vector3 movement = target.position - lastTargetPosition;
        lastTargetPosition = target.position;

        // Ignore le mouvement vertical
        Vector3 horizontalMovement = new Vector3(movement.x, 0f, movement.z);

        // Calcule le look ahead cible seulement si pas en combat
        Vector3 targetLookAhead = Vector3.zero;
        bool isMoving = horizontalMovement.magnitude > 0.001f;
        if (isMoving && combatTimer <= 0f)
        {
            Vector3 normalizedDir = horizontalMovement.normalized;
            targetLookAhead = new Vector3(
                normalizedDir.x * lookAheadDistanceX,
                0f,
                normalizedDir.z * lookAheadDistanceZ
            );
        }

        // Transition fluide vers le look ahead cible
        lookAheadOffset = Vector3.Lerp(lookAheadOffset, targetLookAhead, lookAheadSpeed * Time.deltaTime);
    }

    private void UpdateCameraPosition()
    {
        // Si le suivi est désactivé, la caméra reste statique
        if (!enableFollowTarget)
            return;

        // Position de base derrière le joueur
        Vector3 baseOffset = new Vector3(0f, height, -distance);

        // Calcul de l'offset effectif
        Vector3 effectiveOffset;

        // Si en dash, le dash override tout
        if (dashOffset.magnitude > 0.01f)
        {
            effectiveOffset = dashOffset;
        }
        else
        {
            // Sinon, pan + look ahead, limité au max edge panning
            effectiveOffset = panOffset + lookAheadOffset;
            effectiveOffset.x = Mathf.Clamp(effectiveOffset.x, -maxEdgePanningX, maxEdgePanningX);
            effectiveOffset.z = Mathf.Clamp(effectiveOffset.z, -maxEdgePanningZ, maxEdgePanningZ);
        }

        Vector3 targetPosition = target.position + baseOffset + effectiveOffset;

        // Mouvement fluide vers la position cible
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);
        transform.rotation = Quaternion.Euler(angle, 0f, 0f);
    }

    #region Public API (appelé par PlayerController)

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    /// <summary>
    /// Recentre la caméra sur le joueur (reset le pan offset).
    /// </summary>
    public void Recenter()
    {
        panOffset = Vector3.zero;
    }

    /// <summary>
    /// Appelé lors d'un dash pour décaler la caméra dans la direction du dash.
    /// Permet de voir devant soi pendant le dash.
    /// </summary>
    /// <param name="dashDirection">Direction du dash (normalisée)</param>
    public void OnDash(Vector3 dashDirection)
    {
        // Recentre d'abord
        panOffset = Vector3.zero;

        // Décale dans la direction du dash (pour voir devant soi)
        Vector3 horizontalDir = new Vector3(dashDirection.x, 0f, dashDirection.z).normalized;
        dashOffset = horizontalDir * dashCameraOffset;
    }

    /// <summary>
    /// Appelé lors d'un cast de sort pour désactiver temporairement le look ahead.
    /// Empêche la caméra de bouger pendant le combat.
    /// </summary>
    public void OnSpellCast()
    {
        combatTimer = lookAheadCombatCooldown;
    }

    #endregion
}

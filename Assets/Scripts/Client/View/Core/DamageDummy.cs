using UnityEngine;
using MOBANet.GameSim.Interfaces;

/// <summary>
/// Mannequin de test pour recevoir des dégâts.
/// Affiche les dégâts reçus en texte flottant. Immortel (pas de HP).
/// </summary>
public class DamageDummy : MonoBehaviour, IDamageable
{
    [Header("Floating Text Settings")]
    [Tooltip("Prefab du texte flottant (doit avoir FloatingDamageText)")]
    [SerializeField] private GameObject floatingTextPrefab;
    [Tooltip("Offset vertical pour le spawn du texte")]
    [SerializeField] private float textSpawnHeight = 2f;
    [Tooltip("Variation horizontale aléatoire")]
    [SerializeField] private float textSpawnRandomX = 0.5f;

    [Header("Visual Feedback")]
    [Tooltip("Durée du flash de couleur")]
    [SerializeField] private float flashDuration = 0.1f;
    [Tooltip("Couleur du flash dégâts")]
    [SerializeField] private Color damageFlashColor = Color.red;
    [Tooltip("Couleur du flash soin")]
    [SerializeField] private Color healFlashColor = Color.green;
    [Tooltip("Couleur du texte de soin")]
    [SerializeField] private Color healTextColor = Color.green;

    [Header("Statistics")]
    [SerializeField] private float totalDamageReceived;
    [SerializeField] private int hitCount;
    [SerializeField] private float totalHealReceived;
    [SerializeField] private int healCount;

    private Renderer dummyRenderer;
    private Color originalColor;
    private Coroutine flashCoroutine;

    private void Awake()
    {
        dummyRenderer = GetComponentInChildren<Renderer>();
        if (dummyRenderer != null)
        {
            originalColor = dummyRenderer.material.color;
        }
    }

    /// <summary>
    /// Reçoit des dégâts et affiche un texte flottant.
    /// </summary>
    public void TakeDamage(float amount)
    {
        // Statistiques
        totalDamageReceived += amount;
        hitCount++;

        // Spawn du texte flottant
        SpawnFloatingText(amount, null);

        // Flash visuel
        PlayFlash(damageFlashColor);

        Debug.Log($"[DamageDummy] Hit #{hitCount}: -{amount:F1} damage (Total: {totalDamageReceived:F1})");
    }

    /// <summary>
    /// Reçoit des soins et affiche un texte flottant vert.
    /// </summary>
    public void Heal(float amount)
    {
        // Statistiques
        totalHealReceived += amount;
        healCount++;

        // Spawn du texte flottant (vert)
        SpawnFloatingText(amount, healTextColor);

        // Flash visuel
        PlayFlash(healFlashColor);

        Debug.Log($"[DamageDummy] Heal #{healCount}: +{amount:F1} (Total: {totalHealReceived:F1})");
    }

    private void SpawnFloatingText(float value, Color? color)
    {
        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[DamageDummy] floatingTextPrefab non assigné !");
            return;
        }

        // Position avec variation aléatoire
        Vector3 spawnPos = transform.position;
        spawnPos.y += textSpawnHeight;
        spawnPos.x += Random.Range(-textSpawnRandomX, textSpawnRandomX);

        GameObject textObj = Instantiate(floatingTextPrefab, spawnPos, Quaternion.identity);
        FloatingDamageText floatingText = textObj.GetComponent<FloatingDamageText>();
        if (floatingText != null)
        {
            if (color.HasValue)
            {
                floatingText.Initialize(value, color.Value);
            }
            else
            {
                floatingText.Initialize(value);
            }
        }
    }

    private void PlayFlash(Color color)
    {
        if (dummyRenderer != null)
        {
            if (flashCoroutine != null)
                StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(FlashRoutine(color));
        }
    }

    private System.Collections.IEnumerator FlashRoutine(Color color)
    {
        dummyRenderer.material.color = color;
        yield return new WaitForSeconds(flashDuration);
        dummyRenderer.material.color = originalColor;
        flashCoroutine = null;
    }

    /// <summary>
    /// Remet les statistiques à zéro.
    /// </summary>
    public void ResetStats()
    {
        totalDamageReceived = 0f;
        hitCount = 0;
        totalHealReceived = 0f;
        healCount = 0;
        Debug.Log("[DamageDummy] Stats reset.");
    }

    /// <summary>
    /// Total des dégâts reçus depuis le début ou le dernier reset.
    /// </summary>
    public float TotalDamageReceived => totalDamageReceived;

    /// <summary>
    /// Nombre de fois que le dummy a été touché.
    /// </summary>
    public int HitCount => hitCount;

    /// <summary>
    /// Total des soins reçus depuis le début ou le dernier reset.
    /// </summary>
    public float TotalHealReceived => totalHealReceived;

    /// <summary>
    /// Nombre de fois que le dummy a été soigné.
    /// </summary>
    public int HealCount => healCount;
}

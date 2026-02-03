using UnityEngine;
using TMPro;

/// <summary>
/// Texte flottant qui monte et disparaît progressivement.
/// Affiche les dégâts infligés au-dessus des ennemis.
/// </summary>
public class FloatingDamageText : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Vitesse de montée du texte")]
    [SerializeField] private float riseSpeed = 2f;
    [Tooltip("Durée de vie du texte")]
    [SerializeField] private float lifetime = 1f;

    [Header("Appearance")]
    [Tooltip("Taille de départ (multiplicateur)")]
    [SerializeField] private float startScale = 0.5f;
    [Tooltip("Taille maximale (multiplicateur)")]
    [SerializeField] private float maxScale = 1.2f;
    [Tooltip("Durée du grossissement")]
    [SerializeField] private float scaleUpDuration = 0.15f;

    [Header("Color")]
    [Tooltip("Couleur du texte")]
    [SerializeField] private Color textColor = Color.yellow;

    private TextMeshPro textMesh;
    private float spawnTime;
    private Vector3 baseScale;
    private Camera mainCamera;

    private void Awake()
    {
        textMesh = GetComponent<TextMeshPro>();
        if (textMesh == null)
        {
            textMesh = gameObject.AddComponent<TextMeshPro>();
        }

        // Configuration par défaut du TextMeshPro
        textMesh.alignment = TextAlignmentOptions.Center;
        textMesh.fontSize = 8;
        textMesh.fontStyle = FontStyles.Bold;
        textMesh.color = textColor;

        baseScale = Vector3.one;
        mainCamera = Camera.main;
    }

    /// <summary>
    /// Initialise le texte avec la valeur de dégâts.
    /// </summary>
    public void Initialize(float damage)
    {
        Initialize(damage, textColor);
    }

    /// <summary>
    /// Initialise le texte avec une valeur et une couleur personnalisée.
    /// </summary>
    public void Initialize(float value, Color color)
    {
        spawnTime = Time.time;

        // Affiche la valeur (arrondi si >= 1, sinon 1 décimale)
        if (value >= 1f)
        {
            textMesh.text = Mathf.RoundToInt(value).ToString();
        }
        else
        {
            textMesh.text = value.ToString("F1");
        }

        textMesh.color = color;
        transform.localScale = baseScale * startScale;
    }

    private void Update()
    {
        float elapsed = Time.time - spawnTime;

        // Destruction après lifetime
        if (elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Montée constante
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        // Scale animation (grossit puis reste stable)
        if (elapsed < scaleUpDuration)
        {
            float t = elapsed / scaleUpDuration;
            // Easing out
            t = 1f - (1f - t) * (1f - t);
            float scale = Mathf.Lerp(startScale, maxScale, t);
            transform.localScale = baseScale * scale;
        }
        else
        {
            transform.localScale = baseScale * maxScale;
        }

        // Fade out sur la dernière moitié de la vie
        float fadeStart = lifetime * 0.5f;
        if (elapsed > fadeStart)
        {
            float fadeProgress = (elapsed - fadeStart) / (lifetime - fadeStart);
            Color c = textMesh.color;
            c.a = 1f - fadeProgress;
            textMesh.color = c;
        }

        // Billboard : fait face à la caméra
        if (mainCamera != null)
        {
            transform.rotation = mainCamera.transform.rotation;
        }
    }
}

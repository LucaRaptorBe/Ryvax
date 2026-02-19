// FloatingDamageText.cs - Floating damage numbers above hit targets
// Rises and fades out over lifetime

using UnityEngine;
using TMPro;

namespace MOBANet.Client.HUD
{
    public class FloatingDamageText : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float riseSpeed = 2f;
        [SerializeField] private float lifetime = 1f;

        [Header("Appearance")]
        [SerializeField] private float startScale = 0.5f;
        [SerializeField] private float maxScale = 1.2f;
        [SerializeField] private float scaleUpDuration = 0.15f;

        [Header("Color")]
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

            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.fontSize = 8;
            textMesh.fontStyle = FontStyles.Bold;
            textMesh.color = textColor;

            baseScale = Vector3.one;
            mainCamera = Camera.main;
        }

        /// <summary>
        /// Static factory: spawn a floating damage text at a position.
        /// </summary>
        public static void Spawn(int damage, Vector3 position)
        {
            var go = new GameObject("DmgText");
            go.transform.position = position + new Vector3(
                Random.Range(-0.3f, 0.3f), 0f, 0f);
            var fdt = go.AddComponent<FloatingDamageText>();
            fdt.Initialize(damage);
        }

        public void Initialize(float damage)
        {
            Initialize(damage, textColor);
        }

        public void Initialize(float value, Color color)
        {
            spawnTime = Time.time;

            if (value >= 1f)
                textMesh.text = Mathf.RoundToInt(value).ToString();
            else
                textMesh.text = value.ToString("F1");

            textMesh.color = color;
            transform.localScale = baseScale * startScale;
        }

        private void Update()
        {
            float elapsed = Time.time - spawnTime;

            if (elapsed >= lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Rise
            transform.position += Vector3.up * riseSpeed * Time.deltaTime;

            // Scale up (ease-out quad)
            if (elapsed < scaleUpDuration)
            {
                float t = elapsed / scaleUpDuration;
                t = 1f - (1f - t) * (1f - t);
                float scale = Mathf.Lerp(startScale, maxScale, t);
                transform.localScale = baseScale * scale;
            }
            else
            {
                transform.localScale = baseScale * maxScale;
            }

            // Fade out (second half of lifetime)
            float fadeStart = lifetime * 0.5f;
            if (elapsed > fadeStart)
            {
                float fadeProgress = (elapsed - fadeStart) / (lifetime - fadeStart);
                Color c = textMesh.color;
                c.a = 1f - fadeProgress;
                textMesh.color = c;
            }

            // Billboard
            if (mainCamera != null)
            {
                transform.rotation = mainCamera.transform.rotation;
            }
        }
    }
}

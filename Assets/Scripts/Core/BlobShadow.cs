using UnityEngine;

namespace MOBA.Core
{
    /// <summary>
    /// Projette une ombre circulaire (blob shadow) au sol sous l'objet.
    /// Performant et lisible, idéal pour les MOBAs avec beaucoup d'entités.
    /// </summary>
    public class BlobShadow : MonoBehaviour
    {
        [Header("Shadow Settings")]
        [SerializeField] private float baseSize = 1f;
        [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.5f);
        [SerializeField] private float maxDistance = 10f;
        [SerializeField] private float groundOffset = 0.01f;

        [Header("Size Scaling")]
        [Tooltip("Si activé, l'ombre rétrécit quand l'objet s'éloigne du sol")]
        [SerializeField] private bool scaleWithHeight = true;
        [SerializeField] private float minScale = 0.3f;
        [SerializeField] private float maxScale = 1f;

        [Header("Fade Settings")]
        [Tooltip("Si activé, l'ombre devient transparente quand l'objet s'éloigne du sol")]
        [SerializeField] private bool fadeWithHeight = true;
        [SerializeField] private float fadeStartDistance = 2f;
        [SerializeField] private float fadeEndDistance = 8f;

        [Header("Raycast Settings")]
        [SerializeField] private LayerMask groundLayer = ~0;

        [Header("References (Auto-generated)")]
        [SerializeField] private GameObject shadowQuad;
        [SerializeField] private MeshRenderer shadowRenderer;
        [SerializeField] private Material shadowMaterial;

        private static readonly int ColorProperty = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock propertyBlock;
        private Vector3 originalScale;

        private void Awake()
        {
            if (shadowQuad == null)
            {
                CreateShadowQuad();
            }

            propertyBlock = new MaterialPropertyBlock();
            originalScale = shadowQuad.transform.localScale;
        }

        private void LateUpdate()
        {
            UpdateShadowPosition();
        }

        private void UpdateShadowPosition()
        {
            // Raycast vers le sol
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, maxDistance, groundLayer))
            {
                // Activer l'ombre si elle était désactivée
                if (!shadowQuad.activeSelf)
                {
                    shadowQuad.SetActive(true);
                }

                // Positionner l'ombre au sol
                shadowQuad.transform.position = hit.point + hit.normal * groundOffset;
                shadowQuad.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);

                float distanceToGround = hit.distance;

                // Calculer l'échelle selon la hauteur
                if (scaleWithHeight)
                {
                    float t = Mathf.InverseLerp(0f, maxDistance, distanceToGround);
                    float scaleFactor = Mathf.Lerp(maxScale, minScale, t);
                    shadowQuad.transform.localScale = originalScale * scaleFactor;
                }

                // Calculer l'opacité selon la hauteur
                if (fadeWithHeight)
                {
                    float alpha = shadowColor.a;
                    if (distanceToGround > fadeStartDistance)
                    {
                        float t = Mathf.InverseLerp(fadeStartDistance, fadeEndDistance, distanceToGround);
                        alpha = Mathf.Lerp(shadowColor.a, 0f, t);
                    }

                    Color currentColor = shadowColor;
                    currentColor.a = alpha;

                    shadowRenderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetColor(ColorProperty, currentColor);
                    shadowRenderer.SetPropertyBlock(propertyBlock);
                }
            }
            else
            {
                // Désactiver l'ombre si pas de sol
                if (shadowQuad.activeSelf)
                {
                    shadowQuad.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Crée le quad d'ombre enfant avec le material approprié.
        /// </summary>
        public void CreateShadowQuad()
        {
            // Supprimer l'ancien quad si existant
            if (shadowQuad != null)
            {
                if (Application.isPlaying)
                    Destroy(shadowQuad);
                else
                    DestroyImmediate(shadowQuad);
            }

            // Créer le GameObject du quad
            shadowQuad = new GameObject("BlobShadow");
            shadowQuad.transform.SetParent(transform);
            shadowQuad.transform.localPosition = Vector3.zero;
            shadowQuad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shadowQuad.transform.localScale = Vector3.one * baseSize;

            // Ajouter le MeshFilter avec un quad
            MeshFilter meshFilter = shadowQuad.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = CreateQuadMesh();

            // Ajouter le MeshRenderer
            shadowRenderer = shadowQuad.AddComponent<MeshRenderer>();
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;

            // Créer le material
            CreateShadowMaterial();
            shadowRenderer.sharedMaterial = shadowMaterial;

            originalScale = shadowQuad.transform.localScale;
        }

        private Mesh CreateQuadMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "BlobShadowQuad";

            Vector3[] vertices = new Vector3[4]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f)
            };

            int[] triangles = new int[6]
            {
                0, 2, 1,
                2, 3, 1
            };

            Vector2[] uvs = new Vector2[4]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();

            return mesh;
        }

        private void CreateShadowMaterial()
        {
            // Utiliser le shader URP Unlit avec transparence
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                // Fallback pour Built-in RP
                shader = Shader.Find("Unlit/Transparent");
            }

            shadowMaterial = new Material(shader);
            shadowMaterial.name = "BlobShadow_Runtime";

            // Configurer pour la transparence
            shadowMaterial.SetFloat("_Surface", 1); // Transparent
            shadowMaterial.SetFloat("_Blend", 0); // Alpha
            shadowMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            shadowMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            shadowMaterial.SetFloat("_ZWrite", 0);
            shadowMaterial.renderQueue = 3000;

            // Créer une texture procédurale pour l'ombre (gradient circulaire)
            Texture2D shadowTexture = CreateShadowTexture(64);
            shadowMaterial.SetTexture("_BaseMap", shadowTexture);
            shadowMaterial.SetColor("_BaseColor", shadowColor);

            shadowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        private Texture2D CreateShadowTexture(int resolution)
        {
            Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            texture.name = "BlobShadow_Texture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            float center = resolution / 2f;
            float maxRadius = resolution / 2f;

            Color[] pixels = new Color[resolution * resolution];

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float normalizedDistance = distance / maxRadius;

                    // Gradient circulaire avec bords doux
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalizedDistance);
                    pixels[y * resolution + x] = new Color(0f, 0f, 0f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            return texture;
        }

        /// <summary>
        /// Configure la taille de base de l'ombre.
        /// </summary>
        public void SetSize(float size)
        {
            baseSize = size;
            if (shadowQuad != null)
            {
                shadowQuad.transform.localScale = Vector3.one * baseSize;
                originalScale = shadowQuad.transform.localScale;
            }
        }

        /// <summary>
        /// Configure la couleur de l'ombre.
        /// </summary>
        public void SetColor(Color color)
        {
            shadowColor = color;
            if (shadowMaterial != null)
            {
                shadowMaterial.SetColor("_BaseColor", shadowColor);
            }
        }

        /// <summary>
        /// Configure le layer mask pour le raycast.
        /// </summary>
        public void SetGroundLayer(LayerMask layer)
        {
            groundLayer = layer;
        }

        #region Properties
        public float BaseSize => baseSize;
        public Color ShadowColor => shadowColor;
        public float MaxDistance => maxDistance;
        public bool ScaleWithHeight => scaleWithHeight;
        public bool FadeWithHeight => fadeWithHeight;
        #endregion

        private void OnDestroy()
        {
            // Nettoyer les ressources créées à runtime
            if (Application.isPlaying)
            {
                if (shadowMaterial != null)
                {
                    Destroy(shadowMaterial);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Recréer le quad si les paramètres changent dans l'éditeur
            if (shadowQuad != null && !Application.isPlaying)
            {
                shadowQuad.transform.localScale = Vector3.one * baseSize;
                if (shadowMaterial != null)
                {
                    shadowMaterial.SetColor("_BaseColor", shadowColor);
                }
            }
        }
#endif
    }
}

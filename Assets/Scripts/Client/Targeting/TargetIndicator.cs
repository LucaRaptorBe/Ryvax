// TargetIndicator.cs - Ground ring indicator on targeted entity
// Added dynamically to the target's GameObject.
// Creates a flat ring mesh at the target's feet that works with any shader.

using UnityEngine;

namespace MOBANet.Client.Targeting
{
    public class TargetIndicator : MonoBehaviour
    {
        private static readonly Color ENEMY_COLOR = new Color(1f, 0.2f, 0.2f, 0.8f);
        private static readonly Color ALLY_COLOR = new Color(0.2f, 1f, 0.2f, 0.8f);

        private const float RING_RADIUS = 1.0f;
        private const float RING_WIDTH = 0.08f;
        private const int SEGMENTS = 48;
        private const float ROTATE_SPEED = 30f; // degrees per second
        private const float PULSE_SPEED = 2f;
        private const float PULSE_MIN = 0.7f;

        private GameObject _ringGO;
        private MeshRenderer _ringRenderer;
        private Material _ringMat;
        private Color _baseColor;

        public void Show(bool isEnemy)
        {
            _baseColor = isEnemy ? ENEMY_COLOR : ALLY_COLOR;
            CreateRing();
        }

        public void Hide()
        {
            if (_ringGO != null)
            {
                Destroy(_ringGO);
                _ringGO = null;
            }
        }

        private void CreateRing()
        {
            _ringGO = new GameObject("TargetRing");
            _ringGO.transform.SetParent(transform, false);
            _ringGO.transform.localPosition = new Vector3(0f, 0.05f, 0f); // Slightly above ground
            _ringGO.transform.localRotation = Quaternion.identity;

            var meshFilter = _ringGO.AddComponent<MeshFilter>();
            _ringRenderer = _ringGO.AddComponent<MeshRenderer>();

            meshFilter.mesh = GenerateRingMesh();

            // Unlit transparent material
            _ringMat = new Material(Shader.Find("Sprites/Default"));
            _ringMat.color = _baseColor;
            _ringRenderer.material = _ringMat;
            _ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ringRenderer.receiveShadows = false;
        }

        private Mesh GenerateRingMesh()
        {
            var mesh = new Mesh();
            mesh.name = "TargetRingMesh";

            int vertCount = SEGMENTS * 2;
            var vertices = new Vector3[vertCount];
            var colors = new Color[vertCount];
            var triangles = new int[SEGMENTS * 6];

            float innerRadius = RING_RADIUS - RING_WIDTH;
            float outerRadius = RING_RADIUS + RING_WIDTH;

            for (int i = 0; i < SEGMENTS; i++)
            {
                float angle = (float)i / SEGMENTS * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                // Inner vertex
                vertices[i * 2] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
                colors[i * 2] = _baseColor;

                // Outer vertex
                vertices[i * 2 + 1] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                colors[i * 2 + 1] = _baseColor;

                // Triangles (two per segment)
                int next = (i + 1) % SEGMENTS;
                int ti = i * 6;
                triangles[ti + 0] = i * 2;
                triangles[ti + 1] = next * 2;
                triangles[ti + 2] = i * 2 + 1;
                triangles[ti + 3] = next * 2;
                triangles[ti + 4] = next * 2 + 1;
                triangles[ti + 5] = i * 2 + 1;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            return mesh;
        }

        void Update()
        {
            if (_ringGO == null) return;

            // Slow rotation
            _ringGO.transform.Rotate(Vector3.up, ROTATE_SPEED * Time.deltaTime, Space.Self);

            // Pulse alpha
            if (_ringMat != null)
            {
                float pulse = Mathf.Lerp(PULSE_MIN, 1f,
                    (Mathf.Sin(Time.time * PULSE_SPEED) + 1f) * 0.5f);
                Color c = _baseColor;
                c.a *= pulse;
                _ringMat.color = c;
            }
        }

        void OnDestroy()
        {
            if (_ringMat != null)
                Destroy(_ringMat);
            if (_ringGO != null)
                Destroy(_ringGO);
        }
    }
}

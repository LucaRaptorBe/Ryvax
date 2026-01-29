// TargetMarker.cs - Visual marker for selected target
using UnityEngine;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// Marqueur visuel pour la cible sélectionnée.
    /// Affiche un cercle jaune au sol sous l'entité sélectionnée.
    /// </summary>
    public class TargetMarker : MonoBehaviour
    {
        [Header("Visual Settings")]
        [SerializeField] private Color _markerColor = Color.yellow;
        [SerializeField] private float _radius = 1.0f;
        [SerializeField] private float _thickness = 0.1f;
        [SerializeField] private int _segments = 32;
        [SerializeField] private float _heightOffset = 0.05f; // Légèrement au-dessus du sol

        private GameObject _markerObject;
        private LineRenderer _lineRenderer;

        private void Awake()
        {
            CreateMarker();
        }

        /// <summary>
        /// Crée le GameObject du marqueur avec LineRenderer.
        /// </summary>
        private void CreateMarker()
        {
            _markerObject = new GameObject("TargetMarker_Circle");
            _markerObject.transform.SetParent(transform, false);
            _markerObject.transform.localPosition = Vector3.up * _heightOffset;

            _lineRenderer = _markerObject.AddComponent<LineRenderer>();
            _lineRenderer.positionCount = _segments + 1;
            _lineRenderer.useWorldSpace = false;
            _lineRenderer.startWidth = _thickness;
            _lineRenderer.endWidth = _thickness;
            _lineRenderer.loop = true;

            // Material - utilise un material par défaut ou crée-en un simple
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _lineRenderer.startColor = _markerColor;
            _lineRenderer.endColor = _markerColor;

            // Génère les points du cercle
            UpdateCirclePoints();

            // Cache par défaut
            _markerObject.SetActive(false);
        }

        /// <summary>
        /// Met à jour les points du cercle.
        /// </summary>
        private void UpdateCirclePoints()
        {
            if (_lineRenderer == null) return;

            float angleStep = 360f / _segments;
            for (int i = 0; i <= _segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * _radius;
                float z = Mathf.Sin(angle) * _radius;
                _lineRenderer.SetPosition(i, new Vector3(x, 0, z));
            }
        }

        /// <summary>
        /// Affiche le marqueur.
        /// </summary>
        public void Show()
        {
            if (_markerObject != null)
            {
                _markerObject.SetActive(true);
            }
        }

        /// <summary>
        /// Cache le marqueur.
        /// </summary>
        public void Hide()
        {
            if (_markerObject != null)
            {
                _markerObject.SetActive(false);
            }
        }

        /// <summary>
        /// Configure la couleur du marqueur.
        /// </summary>
        public void SetColor(Color color)
        {
            _markerColor = color;
            if (_lineRenderer != null)
            {
                _lineRenderer.startColor = color;
                _lineRenderer.endColor = color;
            }
        }

        /// <summary>
        /// Configure le rayon du marqueur.
        /// </summary>
        public void SetRadius(float radius)
        {
            _radius = radius;
            UpdateCirclePoints();
        }
    }
}

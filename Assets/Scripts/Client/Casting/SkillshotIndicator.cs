using UnityEngine;

namespace MOBANet.Client.Casting
{
    /// <summary>
    /// Visual skillshot indicator using LineRenderer.
    /// Draws an arrow-shaped line from origin to max range.
    /// 7-point arrow: start → end → left wing → end → right wing → end → start.
    /// </summary>
    public class SkillshotIndicator : MonoBehaviour
    {
        private LineRenderer _line;
        private float _range;
        private float _width;

        private const float ARROW_HEAD_LENGTH = 0.6f;
        private const float LINE_WIDTH = 0.15f;
        private const float Y_OFFSET = 0.05f;
        private static readonly Color INDICATOR_COLOR = new Color(0f, 1f, 1f, 0.7f); // Cyan

        void Awake()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = false;
            _line.positionCount = 7;
            _line.startWidth = LINE_WIDTH;
            _line.endWidth = LINE_WIDTH;

            // Default sprite material (unlit, supports vertex colors)
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.startColor = INDICATOR_COLOR;
            _line.endColor = INDICATOR_COLOR;

            _line.enabled = false;
        }

        public void Initialize(float range, float width = 0.5f)
        {
            _range = range;
            _width = width;
        }

        public void Show(Vector3 origin, Vector3 direction)
        {
            _line.enabled = true;
            UpdateIndicator(origin, direction);
        }

        public void UpdateIndicator(Vector3 origin, Vector3 direction)
        {
            if (!_line.enabled) return;

            // Flatten to XZ plane
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;
            direction.Normalize();

            Vector3 yOff = new Vector3(0f, Y_OFFSET, 0f);
            Vector3 start = origin + yOff;
            Vector3 end = origin + direction * _range + yOff;

            // Arrowhead geometry
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            Vector3 arrowBack = end - direction * ARROW_HEAD_LENGTH;
            float wingSpread = ARROW_HEAD_LENGTH * 0.577f; // tan(30°)
            Vector3 arrowLeft = arrowBack + right * wingSpread;
            Vector3 arrowRight = arrowBack - right * wingSpread;

            // 7-point arrow shape
            _line.SetPosition(0, start);
            _line.SetPosition(1, end);
            _line.SetPosition(2, arrowLeft);
            _line.SetPosition(3, end);
            _line.SetPosition(4, arrowRight);
            _line.SetPosition(5, end);
            _line.SetPosition(6, start);
        }

        public void Hide()
        {
            if (_line != null)
                _line.enabled = false;
        }
    }
}

// HealthBarUI.cs - World-space health bar above player head
// Programmatically constructed (no prefab needed)

using UnityEngine;
using UnityEngine.UI;

namespace MOBANet.Client.HUD
{
    public class HealthBarUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Image _fillImage;
        private Transform _target;
        private float _yOffset = 2.5f;

        private static readonly Color ALLY_COLOR = new Color(0.2f, 0.8f, 0.2f, 1f);
        private static readonly Color ENEMY_COLOR = new Color(0.9f, 0.2f, 0.2f, 1f);

        public void Initialize(Transform target, bool isLocal, byte teamId, byte localTeamId)
        {
            _target = target;

            // Create world-space canvas
            var canvasGO = new GameObject("HealthBarCanvas");
            canvasGO.transform.SetParent(transform);
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5;

            var rectTransform = canvasGO.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(80, 12);
            rectTransform.localScale = Vector3.one * 0.01f;

            // Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgImage = bgGO.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.6f);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Fill
            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(canvasGO.transform, false);
            _fillImage = fillGO.AddComponent<Image>();
            _fillImage.type = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = 0;
            _fillImage.fillAmount = 1f;

            bool isAlly = teamId == localTeamId;
            _fillImage.color = isAlly ? ALLY_COLOR : ENEMY_COLOR;

            var fillRect = fillGO.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1, 1);
            fillRect.offsetMax = new Vector2(-1, -1);
        }

        public void UpdateHealth(float percent)
        {
            if (_fillImage != null)
                _fillImage.fillAmount = Mathf.Clamp01(percent);
        }

        public void SetVisible(bool visible)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(visible);
        }

        void LateUpdate()
        {
            if (_target == null || _canvas == null) return;

            _canvas.transform.position = _target.position + Vector3.up * _yOffset;

            // Billboard: face camera
            if (Camera.main != null)
                _canvas.transform.forward = Camera.main.transform.forward;
        }
    }
}

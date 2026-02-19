// TargetInfoUI.cs - HUD panel showing targeted enemy's name and HP
// Screen-space overlay, fades in/out when target is locked/cleared

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MOBANet.Client.Targeting;
using MOBANet.UnityView.Core;
using MOBANet.GameSim.Entities;

namespace MOBANet.Client.HUD
{
    public class TargetInfoUI : MonoBehaviour
    {
        private CanvasGroup _canvasGroup;
        private TextMeshProUGUI _nameText;
        private Image _healthFill;
        private TargetingSystem _targetingSystem;
        private NetworkClient _networkClient;
        private RectTransform _panel;

        private static readonly Color ENEMY_BAR_COLOR = new Color(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color ALLY_BAR_COLOR = new Color(0.2f, 0.8f, 0.2f, 1f);

        public void Initialize(TargetingSystem targetingSystem, NetworkClient networkClient, Transform hudCanvas)
        {
            _targetingSystem = targetingSystem;
            _networkClient = networkClient;

            // Build UI under existing HUD canvas
            BuildUI(hudCanvas);
            SetVisible(false);
        }

        private void BuildUI(Transform parent)
        {
            // Panel container
            var panelGO = new GameObject("TargetInfoPanel");
            panelGO.transform.SetParent(parent, false);
            _panel = panelGO.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(0.5f, 1f);
            _panel.anchorMax = new Vector2(0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = new Vector2(0f, -10f);
            _panel.sizeDelta = new Vector2(200f, 50f);

            _canvasGroup = panelGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            // Background
            var bgImage = panelGO.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.7f);

            // Name text
            var nameGO = new GameObject("TargetName");
            nameGO.transform.SetParent(panelGO.transform, false);
            _nameText = nameGO.AddComponent<TextMeshProUGUI>();
            _nameText.text = "";
            _nameText.fontSize = 14;
            _nameText.fontStyle = FontStyles.Bold;
            _nameText.color = Color.white;
            _nameText.alignment = TextAlignmentOptions.Center;
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(5f, 0f);
            nameRect.offsetMax = new Vector2(-5f, -2f);

            // Health bar background
            var hpBgGO = new GameObject("HealthBarBg");
            hpBgGO.transform.SetParent(panelGO.transform, false);
            var hpBgImage = hpBgGO.AddComponent<Image>();
            hpBgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var hpBgRect = hpBgGO.GetComponent<RectTransform>();
            hpBgRect.anchorMin = new Vector2(0.05f, 0.1f);
            hpBgRect.anchorMax = new Vector2(0.95f, 0.45f);
            hpBgRect.offsetMin = Vector2.zero;
            hpBgRect.offsetMax = Vector2.zero;

            // Health bar fill
            var hpFillGO = new GameObject("HealthBarFill");
            hpFillGO.transform.SetParent(hpBgGO.transform, false);
            _healthFill = hpFillGO.AddComponent<Image>();
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.fillOrigin = 0;
            _healthFill.fillAmount = 1f;
            _healthFill.color = ENEMY_BAR_COLOR;
            var hpFillRect = hpFillGO.GetComponent<RectTransform>();
            hpFillRect.anchorMin = Vector2.zero;
            hpFillRect.anchorMax = Vector2.one;
            hpFillRect.offsetMin = new Vector2(1f, 1f);
            hpFillRect.offsetMax = new Vector2(-1f, -1f);
        }

        void Update()
        {
            if (_targetingSystem == null || _networkClient == null) return;

            bool hasTarget = _targetingSystem.HasTarget;
            SetVisible(hasTarget);

            if (!hasTarget) return;

            var simPlayer = _networkClient.GetSimPlayer(_targetingSystem.CurrentTargetId);
            if (simPlayer == null)
            {
                SetVisible(false);
                return;
            }

            _nameText.text = $"Player {simPlayer.Id}";
            _healthFill.fillAmount = simPlayer.Stats.HealthPercent;

            byte localTeamId = _networkClient.GetLocalTeamId();
            _healthFill.color = simPlayer.TeamId == localTeamId ? ALLY_BAR_COLOR : ENEMY_BAR_COLOR;
        }

        private void SetVisible(bool visible)
        {
            if (_canvasGroup == null) return;
            float target = visible ? 1f : 0f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, target, Time.deltaTime * 8f);
        }
    }
}

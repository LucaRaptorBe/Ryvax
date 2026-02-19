using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Main settings panel. Hosts tab toggles, content area, and action buttons.
    /// Edits a clone of GameSettings (non-destructive until Apply/Save).
    /// </summary>
    public class SettingsUI : MonoBehaviour
    {
        private GameSettings _pendingSettings;
        private GameSettings _originalSettings;

        // Tabs
        private KeybindsTabUI _keybindsTab;
        private MovementTabUI _movementTab;

        // Tab toggle buttons
        private Button _keybindsToggle;
        private Button _movementToggle;
        private Image _keybindsToggleBg;
        private Image _movementToggleBg;

        // Root panel
        private GameObject _panelRoot;
        private RectTransform _panelRect;

        private static readonly Color ActiveTabColor = new Color(0.25f, 0.25f, 0.4f, 1f);
        private static readonly Color InactiveTabColor = new Color(0.15f, 0.15f, 0.2f, 0.8f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.2f, 0.35f, 0.9f);
        private static readonly Color ButtonHover = new Color(0.3f, 0.3f, 0.5f, 1f);
        private static readonly Color ButtonPressed = new Color(0.15f, 0.15f, 0.25f, 1f);

        public bool IsVisible => _panelRoot != null && _panelRoot.activeSelf;

        /// <summary>
        /// Build the entire settings UI hierarchy under the given canvas.
        /// Called once by SettingsManager.
        /// </summary>
        public void BuildUI(RectTransform canvasRect)
        {
            Debug.Log("[SettingsUI] BuildUI start");

            // Panel root (centered window)
            _panelRoot = gameObject;
            _panelRect = EnsureRectTransform(_panelRoot);
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(450, 480);
            _panelRect.anchoredPosition = Vector2.zero;

            // Background
            var bg = _panelRoot.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.15f, 0.95f);

            // Vertical layout for the whole panel
            var mainLayout = _panelRoot.AddComponent<VerticalLayoutGroup>();
            mainLayout.spacing = 0;
            mainLayout.padding = new RectOffset(0, 0, 0, 0);
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = false;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childForceExpandHeight = false;

            // --- Drag Handle / Title Bar ---
            BuildTitleBar();
            Debug.Log("[SettingsUI] BuildUI: TitleBar done");

            // --- Tab Buttons ---
            BuildTabButtons();
            Debug.Log("[SettingsUI] BuildUI: TabButtons done");

            // --- Content Area ---
            BuildContentArea();
            Debug.Log($"[SettingsUI] BuildUI: ContentArea done, _keybindsTab={_keybindsTab != null}, _movementTab={_movementTab != null}");

            // --- Action Buttons ---
            BuildActionButtons();

            _panelRoot.SetActive(false);
            Debug.Log("[SettingsUI] BuildUI complete");
        }

        /// <summary>
        /// Show the settings panel with a clone of the given settings for editing.
        /// </summary>
        public void Show(GameSettings currentSettings)
        {
            Debug.Log($"[SettingsUI] Show: _keybindsTab={_keybindsTab != null}, _movementTab={_movementTab != null}, settings={currentSettings != null}");

            _originalSettings = currentSettings;
            _pendingSettings = currentSettings.Clone();

            // Apply current settings to UI
            _keybindsTab.ApplyToUI(_pendingSettings);
            _movementTab.ApplyToUI(_pendingSettings);

            // Show keybinds tab by default
            ShowTab(0);

            _panelRoot.SetActive(true);
            SettingsManager.BlockGameInput();
        }

        /// <summary>
        /// Hide the settings panel without applying changes.
        /// </summary>
        public void Hide()
        {
            _panelRoot.SetActive(false);
            SettingsManager.UnblockGameInput();
        }

        #region UI Construction

        private void BuildTitleBar()
        {
            var titleBar = CreateUIObject("TitleBar", transform);
            titleBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);

            var titleBg = titleBar.AddComponent<Image>();
            titleBg.color = new Color(0.12f, 0.12f, 0.18f, 1f);

            var titleLe = titleBar.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 40;

            // Draggable
            titleBar.AddComponent<DraggableWindow>();

            // Title text
            var titleTextGo = CreateUIObject("TitleText", titleBar.transform);
            var titleTextRect = titleTextGo.GetComponent<RectTransform>();
            titleTextRect.anchorMin = Vector2.zero;
            titleTextRect.anchorMax = Vector2.one;
            titleTextRect.offsetMin = new Vector2(15, 0);
            titleTextRect.offsetMax = new Vector2(-40, 0);

            var titleTmp = titleTextGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Settings";
            titleTmp.fontSize = 20;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;
            titleTmp.raycastTarget = false;

            // Close button (X)
            CreateActionButton(titleBar.transform, "X", 30, 30, () => Hide(),
                new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0));
        }

        private void BuildTabButtons()
        {
            var tabBar = CreateUIObject("TabBar", transform);
            tabBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 35);

            var tabLe = tabBar.AddComponent<LayoutElement>();
            tabLe.preferredHeight = 35;

            var tabLayout = tabBar.AddComponent<HorizontalLayoutGroup>();
            tabLayout.spacing = 2;
            tabLayout.padding = new RectOffset(5, 5, 2, 0);
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childForceExpandHeight = true;

            // Keybinds tab
            _keybindsToggle = CreateTabButton(tabBar.transform, "Keybinds", out _keybindsToggleBg);
            _keybindsToggle.onClick.AddListener(() => ShowTab(0));

            // Movement tab
            _movementToggle = CreateTabButton(tabBar.transform, "Movement", out _movementToggleBg);
            _movementToggle.onClick.AddListener(() => ShowTab(1));
        }

        private void BuildContentArea()
        {
            var content = CreateUIObject("Content", transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.sizeDelta = new Vector2(0, 340);

            var contentLe = content.AddComponent<LayoutElement>();
            contentLe.preferredHeight = 340;
            contentLe.flexibleHeight = 1;

            // Keybinds tab content
            var keybindsGo = CreateUIObject("KeybindsTab", content.transform);
            var keybindsRect = keybindsGo.GetComponent<RectTransform>();
            keybindsRect.anchorMin = Vector2.zero;
            keybindsRect.anchorMax = Vector2.one;
            keybindsRect.offsetMin = Vector2.zero;
            keybindsRect.offsetMax = Vector2.zero;

            _keybindsTab = keybindsGo.AddComponent<KeybindsTabUI>();
            _keybindsTab.Build();

            // Movement tab content
            var movementGo = CreateUIObject("MovementTab", content.transform);
            var movementRect = movementGo.GetComponent<RectTransform>();
            movementRect.anchorMin = Vector2.zero;
            movementRect.anchorMax = Vector2.one;
            movementRect.offsetMin = Vector2.zero;
            movementRect.offsetMax = Vector2.zero;

            _movementTab = movementGo.AddComponent<MovementTabUI>();
            _movementTab.Build();
        }

        private void BuildActionButtons()
        {
            var btnBar = CreateUIObject("ButtonBar", transform);
            btnBar.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 45);

            var btnLe = btnBar.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 45;

            var btnLayout = btnBar.AddComponent<HorizontalLayoutGroup>();
            btnLayout.spacing = 8;
            btnLayout.padding = new RectOffset(10, 10, 5, 10);
            btnLayout.childAlignment = TextAnchor.MiddleCenter;
            btnLayout.childControlWidth = false;
            btnLayout.childControlHeight = true;
            btnLayout.childForceExpandWidth = false;
            btnLayout.childForceExpandHeight = true;

            // Apply
            CreateBarButton(btnBar.transform, "Apply", 90, OnApply);

            // Save & Close
            CreateBarButton(btnBar.transform, "Save & Close", 120, OnSaveAndClose);

            // Revert
            CreateBarButton(btnBar.transform, "Revert", 90, OnRevert);
        }

        #endregion

        #region Tab Switching

        private void ShowTab(int index)
        {
            // Collect current tab values before switching
            CollectAll();

            _keybindsTab.gameObject.SetActive(index == 0);
            _movementTab.gameObject.SetActive(index == 1);

            _keybindsToggleBg.color = index == 0 ? ActiveTabColor : InactiveTabColor;
            _movementToggleBg.color = index == 1 ? ActiveTabColor : InactiveTabColor;
        }

        #endregion

        #region Actions

        private void OnApply()
        {
            CollectAll();
            SettingsManager.Instance.ApplySettings(_pendingSettings);
            // Keep menu open, refresh UI from applied settings
            _pendingSettings = SettingsManager.Instance.CurrentSettings.Clone();
        }

        private void OnSaveAndClose()
        {
            CollectAll();
            SettingsManager.Instance.ApplyAndSaveSettings(_pendingSettings);
            Hide();
        }

        private void OnRevert()
        {
            _pendingSettings = _originalSettings.Clone();
            _keybindsTab.ApplyToUI(_pendingSettings);
            _movementTab.ApplyToUI(_pendingSettings);
        }

        private void CollectAll()
        {
            _keybindsTab.CollectFromUI(_pendingSettings);
            _movementTab.CollectFromUI(_pendingSettings);
        }

        #endregion

        #region UI Helpers

        private static GameObject CreateUIObject(string name, Transform parent)
            => UIHelper.CreateUIObject(name, parent);

        private static RectTransform EnsureRectTransform(GameObject go)
            => UIHelper.EnsureRectTransform(go);

        private Button CreateTabButton(Transform parent, string label, out Image bgImage)
        {
            var go = CreateUIObject($"Tab_{label}", parent);

            bgImage = go.AddComponent<Image>();
            bgImage.color = InactiveTabColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            colors.normalColor = Color.white;
            btn.colors = colors;
            btn.targetGraphic = bgImage;

            var textGo = CreateUIObject("Text", go.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 15;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            return btn;
        }

        private void CreateBarButton(Transform parent, string label, float width, UnityEngine.Events.UnityAction onClick)
        {
            var go = CreateUIObject($"Btn_{label.Replace(" ", "")}", parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, 30);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            var image = go.AddComponent<Image>();
            image.color = ButtonColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = ButtonHover;
            colors.pressedColor = ButtonPressed;
            btn.colors = colors;
            btn.onClick.AddListener(onClick);

            var textGo = CreateUIObject("Text", go.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 14;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        private void CreateActionButton(Transform parent, string label, float w, float h,
            UnityEngine.Events.UnityAction onClick, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos)
        {
            var go = CreateUIObject($"Btn_{label}", parent);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = anchoredPos;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.15f, 0.15f, 0.9f);

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var textGo = CreateUIObject("Text", go.transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
        }

        #endregion
    }
}

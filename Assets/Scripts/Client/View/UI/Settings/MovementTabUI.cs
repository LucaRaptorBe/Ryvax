using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Movement settings tab. Shows movement mode dropdown (WASD / Click-to-Move).
    /// </summary>
    public class MovementTabUI : MonoBehaviour
    {
        private TMP_Dropdown _movementDropdown;
        private TMP_Dropdown _layoutDropdown;

        /// <summary>
        /// Build all UI elements as children of this GameObject.
        /// </summary>
        public void Build()
        {
            UIHelper.EnsureRectTransform(gameObject);

            // Vertical layout
            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Header
            CreateLabel("Movement Mode", 18, FontStyles.Bold);

            // Description
            CreateLabel("Choose how your character moves.", 14, FontStyles.Normal, new Color(0.7f, 0.7f, 0.7f));

            // Dropdown row
            CreateMovementDropdown();

            // Spacer
            var spacer = UIHelper.CreateUIObject("Spacer", transform);
            spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 15);
            spacer.AddComponent<LayoutElement>().preferredHeight = 15;

            // Keyboard layout
            CreateLabel("Keyboard Layout", 18, FontStyles.Bold);
            CreateLabel("AZERTY uses ZQSD for movement instead of WASD.", 14, FontStyles.Normal, new Color(0.7f, 0.7f, 0.7f));
            CreateLayoutDropdown();
        }

        /// <summary>
        /// Populate UI from settings (settings -> UI).
        /// </summary>
        public void ApplyToUI(GameSettings settings)
        {
            if (_movementDropdown != null)
                _movementDropdown.value = (int)settings.movementMode;
            if (_layoutDropdown != null)
                _layoutDropdown.value = (int)settings.keyboardLayout;
        }

        /// <summary>
        /// Collect UI values into settings (UI -> settings).
        /// </summary>
        public void CollectFromUI(GameSettings settings)
        {
            if (_movementDropdown != null)
                settings.movementMode = (MovementMode)_movementDropdown.value;
            if (_layoutDropdown != null)
                settings.keyboardLayout = (KeyboardLayout)_layoutDropdown.value;
        }

        private void CreateMovementDropdown()
        {
            var rowGo = UIHelper.CreateUIObject("MovementRow", transform);
            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);

            _movementDropdown = CreateTMPDropdown(rowGo.transform, "MovementDropdown",
                new[] { "WASD (Keyboard)", "Click-to-Move" },
                250f, 36f);
        }

        private void CreateLayoutDropdown()
        {
            var rowGo = UIHelper.CreateUIObject("LayoutRow", transform);
            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);

            _layoutDropdown = CreateTMPDropdown(rowGo.transform, "LayoutDropdown",
                new[] { "QWERTY (WASD)", "AZERTY (ZQSD)" },
                250f, 36f);
        }

        private TMP_Dropdown CreateTMPDropdown(Transform parent, string name, string[] options, float width, float height)
        {
            var ddGo = UIHelper.CreateUIObject(name, parent);
            ddGo.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

            var ddImage = ddGo.AddComponent<Image>();
            ddImage.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

            var dropdown = ddGo.AddComponent<TMP_Dropdown>();

            // Label
            var labelGo = UIHelper.CreateUIObject("Label", ddGo.transform);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10, 0);
            labelRect.offsetMax = new Vector2(-25, 0);

            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            labelTmp.fontSize = 14;
            labelTmp.color = Color.white;
            labelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            dropdown.captionText = labelTmp;

            // Arrow
            var arrowGo = UIHelper.CreateUIObject("Arrow", ddGo.transform);
            var arrowRect = arrowGo.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0);
            arrowRect.anchorMax = new Vector2(1, 1);
            arrowRect.offsetMin = new Vector2(-25, 5);
            arrowRect.offsetMax = new Vector2(-5, -5);

            var arrowTmp = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowTmp.text = "\u25BC";
            arrowTmp.fontSize = 12;
            arrowTmp.color = Color.white;
            arrowTmp.alignment = TextAlignmentOptions.Center;
            arrowTmp.raycastTarget = false;

            // Template
            var templateGo = UIHelper.CreateUIObject("Template", ddGo.transform);
            var templateRect = templateGo.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0, 0);
            templateRect.anchorMax = new Vector2(1, 0);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.sizeDelta = new Vector2(0, 80);

            var templateImage = templateGo.AddComponent<Image>();
            templateImage.color = new Color(0.15f, 0.15f, 0.2f, 0.95f);

            var scrollRect = templateGo.AddComponent<ScrollRect>();

            // Viewport
            var viewportGo = UIHelper.CreateUIObject("Viewport", templateGo.transform);
            var viewportRect = viewportGo.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            viewportGo.AddComponent<Image>().color = Color.clear;
            var mask = viewportGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            scrollRect.viewport = viewportRect;

            // Content
            var contentGo = UIHelper.CreateUIObject("Content", viewportGo.transform);
            var contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            scrollRect.content = contentRect;

            // Item
            var itemGo = UIHelper.CreateUIObject("Item", contentGo.transform);
            var itemRect = itemGo.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0, 30);
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);

            var itemToggle = itemGo.AddComponent<Toggle>();

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

            var checkGo = UIHelper.CreateUIObject("Item Checkmark", itemGo.transform);
            var checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            var checkImage = checkGo.AddComponent<Image>();
            checkImage.color = new Color(0.3f, 0.3f, 0.5f, 0.5f);

            itemToggle.graphic = checkImage;
            itemToggle.targetGraphic = itemBg;

            var itemLabelGo = UIHelper.CreateUIObject("Item Label", itemGo.transform);
            var itemLabelRect = itemLabelGo.GetComponent<RectTransform>();
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(10, 0);
            itemLabelRect.offsetMax = new Vector2(-10, 0);

            var itemLabelTmp = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabelTmp.fontSize = 14;
            itemLabelTmp.color = Color.white;
            itemLabelTmp.alignment = TextAlignmentOptions.MidlineLeft;

            dropdown.itemText = itemLabelTmp;
            dropdown.template = templateRect;

            templateGo.SetActive(false);

            // Add options
            dropdown.ClearOptions();
            var optionList = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
            foreach (var opt in options)
                optionList.Add(new TMP_Dropdown.OptionData(opt));
            dropdown.AddOptions(optionList);

            return dropdown;
        }

        private void CreateLabel(string text, float fontSize, FontStyles style, Color? color = null)
        {
            var go = UIHelper.CreateUIObject("Label_" + text.Replace(" ", ""), transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 24);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 24;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color ?? Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
        }
    }
}

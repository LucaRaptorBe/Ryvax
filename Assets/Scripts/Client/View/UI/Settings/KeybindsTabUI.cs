using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Keybinds settings tab. Shows 4 ability rebind buttons + cast mode dropdown.
    /// </summary>
    public class KeybindsTabUI : MonoBehaviour
    {
        private KeybindButtonUI[] _abilityButtons = new KeybindButtonUI[4];
        private TMP_Dropdown _castModeDropdown;

        private static readonly string[] AbilityLabels = { "Ability 1", "Ability 2", "Ability 3", "Ability 4" };

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

            // Section header
            CreateLabel("Ability Keybinds", 18, FontStyles.Bold);

            // 4 keybind rows
            for (int i = 0; i < 4; i++)
            {
                CreateKeybindRow(i);
            }

            // Spacer
            CreateSpacer(15f);

            // Cast mode section
            CreateLabel("Default Cast Mode", 18, FontStyles.Bold);
            CreateCastModeDropdown();
        }

        /// <summary>
        /// Populate UI from settings (settings -> UI).
        /// </summary>
        public void ApplyToUI(GameSettings settings)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_abilityButtons[i] != null)
                    _abilityButtons[i].SetKey(settings.GetAbilityKey(i));
            }

            if (_castModeDropdown != null)
                _castModeDropdown.value = (int)settings.defaultCastMode;
        }

        /// <summary>
        /// Collect UI values into settings (UI -> settings).
        /// </summary>
        public void CollectFromUI(GameSettings settings)
        {
            for (int i = 0; i < 4; i++)
            {
                if (_abilityButtons[i] != null)
                    settings.SetAbilityKey(i, _abilityButtons[i].CurrentKey);
            }

            if (_castModeDropdown != null)
                settings.defaultCastMode = (CastMode)_castModeDropdown.value;
        }

        private void CreateKeybindRow(int slot)
        {
            var rowGo = UIHelper.CreateUIObject($"KeybindRow_{slot}", transform);
            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);

            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            // Label
            var labelGo = UIHelper.CreateUIObject("Label", rowGo.transform);
            labelGo.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 36);
            var le = labelGo.AddComponent<LayoutElement>();
            le.preferredWidth = 120;

            var tmp = labelGo.AddComponent<TextMeshProUGUI>();
            tmp.text = AbilityLabels[slot];
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            // Keybind button
            var btnGo = UIHelper.CreateUIObject($"KeybindBtn_{slot}", rowGo.transform);

            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredWidth = 100;
            btnLe.preferredHeight = 36;

            var keybindBtn = btnGo.AddComponent<KeybindButtonUI>();
            keybindBtn.Build(100, 36);
            _abilityButtons[slot] = keybindBtn;
        }

        private void CreateCastModeDropdown()
        {
            var rowGo = UIHelper.CreateUIObject("CastModeRow", transform);
            rowGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);

            _castModeDropdown = CreateTMPDropdown(rowGo.transform, "CastModeDropdown",
                new[] { "Quick Cast", "Quick Cast + Indicator", "Normal Cast" },
                250f, 36f);
        }

        private TMP_Dropdown CreateTMPDropdown(Transform parent, string name, string[] options, float width, float height)
        {
            // Dropdown root
            var ddGo = UIHelper.CreateUIObject(name, parent);
            ddGo.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);

            var ddImage = ddGo.AddComponent<Image>();
            ddImage.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

            var dropdown = ddGo.AddComponent<TMP_Dropdown>();

            // Label (selected value text)
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

            // Arrow indicator
            var arrowGo = UIHelper.CreateUIObject("Arrow", ddGo.transform);
            var arrowRect = arrowGo.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1, 0);
            arrowRect.anchorMax = new Vector2(1, 1);
            arrowRect.offsetMin = new Vector2(-25, 5);
            arrowRect.offsetMax = new Vector2(-5, -5);

            var arrowTmp = arrowGo.AddComponent<TextMeshProUGUI>();
            arrowTmp.text = "\u25BC"; // down arrow
            arrowTmp.fontSize = 12;
            arrowTmp.color = Color.white;
            arrowTmp.alignment = TextAlignmentOptions.Center;
            arrowTmp.raycastTarget = false;

            // Template (dropdown list)
            var templateGo = UIHelper.CreateUIObject("Template", ddGo.transform);
            var templateRect = templateGo.GetComponent<RectTransform>();
            // Anchor to top + pivot at bottom → dropdown list opens upward
            templateRect.anchorMin = new Vector2(0, 1);
            templateRect.anchorMax = new Vector2(1, 1);
            templateRect.pivot = new Vector2(0.5f, 0f);
            templateRect.sizeDelta = new Vector2(0, 120);

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

            viewportGo.AddComponent<Image>().color = Color.white;
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

            // Item template
            var itemGo = UIHelper.CreateUIObject("Item", contentGo.transform);
            var itemRect = itemGo.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0, 30);
            itemRect.anchorMin = new Vector2(0, 0.5f);
            itemRect.anchorMax = new Vector2(1, 0.5f);

            var itemToggle = itemGo.AddComponent<Toggle>();

            // Item background
            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

            // Item checkmark (highlight background)
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

            // Item label
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

        private void CreateLabel(string text, float fontSize, FontStyles style = FontStyles.Normal)
        {
            var go = UIHelper.CreateUIObject("Label_" + text.Replace(" ", ""), transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 28);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 28;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.raycastTarget = false;
        }

        private void CreateSpacer(float height)
        {
            var go = UIHelper.CreateUIObject("Spacer", transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }
    }

}

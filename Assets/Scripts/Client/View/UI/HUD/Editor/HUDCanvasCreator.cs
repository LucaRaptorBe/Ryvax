// HUDCanvasCreator.cs - Editor tool to create the HUD Canvas prefab
// Menu: Ryvax > Create HUD Canvas

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using MOBANet.Client.UI;
using MOBANet.Client.Animation;
using MOBANet.GameSim.Data;
using MOBANet.UnityView.Core;

/// <summary>
/// Creates the HUD_Canvas GameObject hierarchy in the scene and optionally saves it as a prefab.
/// Menu: Ryvax > Create HUD Canvas
/// </summary>
public class HUDCanvasCreator : Editor
{
    private const int SLOT_COUNT = 4;
    private const int SLOT_SIZE = 70;
    private const int BAR_SPACING = 10;
    private const int BAR_Y_OFFSET = 30;
    private static readonly string[] SlotNames = { "Slot_1", "Slot_2", "Slot_3", "Slot_4" };

    [MenuItem("Ryvax/Create HUD Canvas")]
    public static void CreateHUDCanvas()
    {
        // ── Canvas ──
        var canvasGo = new GameObject("HUD_Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // ── HUDManager ──
        var hudManager = canvasGo.AddComponent<HUDManager>();

        // ── ClassSelectionUI panel ──
        var classSelGo = CreateClassSelectionPanel(canvasGo.transform);
        var classSelUI = classSelGo.GetComponent<ClassSelectionUI>();

        // ── AbilityBar panel ──
        var barGo = CreatePanel("AbilityBar", canvasGo.transform);
        var barRect = barGo.GetComponent<RectTransform>();

        // Anchor bottom-center
        barRect.anchorMin = new Vector2(0.5f, 0f);
        barRect.anchorMax = new Vector2(0.5f, 0f);
        barRect.pivot = new Vector2(0.5f, 0f);

        float barWidth = SLOT_COUNT * SLOT_SIZE + (SLOT_COUNT - 1) * BAR_SPACING;
        barRect.sizeDelta = new Vector2(barWidth, SLOT_SIZE);
        barRect.anchoredPosition = new Vector2(0f, BAR_Y_OFFSET);

        // Transparent background
        var barImage = barGo.GetComponent<Image>();
        barImage.color = new Color(0f, 0f, 0f, 0f);

        // HorizontalLayoutGroup
        var layout = barGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = BAR_SPACING;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        // AbilityBarUI component
        var abilityBar = barGo.AddComponent<AbilityBarUI>();

        // ── 4 Slots ──
        AbilitySlotUI[] slotComponents = new AbilitySlotUI[SLOT_COUNT];

        for (int i = 0; i < SLOT_COUNT; i++)
        {
            var slotGo = CreateSlot(SlotNames[i], barGo.transform);
            slotComponents[i] = slotGo.GetComponent<AbilitySlotUI>();
        }

        // Wire serialized references via SerializedObject
        WireAbilityBar(abilityBar, slotComponents);
        WireHUDManager(hudManager, abilityBar, classSelUI);

        // Select the new canvas
        Selection.activeGameObject = canvasGo;
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create HUD Canvas");

        Debug.Log("[HUDCanvasCreator] HUD_Canvas created — CharacterClasses and NetworkClient auto-wired.");
    }

    [MenuItem("Ryvax/Create HUD Canvas (Save as Prefab)")]
    public static void CreateHUDCanvasAndSavePrefab()
    {
        CreateHUDCanvas();

        var canvasGo = GameObject.Find("HUD_Canvas");
        if (canvasGo == null) return;

        string prefabDir = "Assets/Prefabs/UI";
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder(prefabDir))
            AssetDatabase.CreateFolder("Assets/Prefabs", "UI");

        string prefabPath = $"{prefabDir}/HUD_Canvas.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(canvasGo, prefabPath, InteractionMode.UserAction);

        Debug.Log($"[HUDCanvasCreator] Prefab saved to {prefabPath}");
    }

    // ── Slot Creation ──

    private static GameObject CreateSlot(string name, Transform parent)
    {
        var slotGo = new GameObject(name);
        slotGo.transform.SetParent(parent, false);

        var slotRect = slotGo.AddComponent<RectTransform>();
        slotRect.sizeDelta = new Vector2(SLOT_SIZE, SLOT_SIZE);

        // Background (dark frame)
        var bg = slotGo.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);

        // AbilitySlotUI component
        var slotUI = slotGo.AddComponent<AbilitySlotUI>();

        // ── Icon (stretch fill) ──
        var iconGo = CreateChildImage("Icon", slotGo.transform);
        var iconRect = iconGo.GetComponent<RectTransform>();
        StretchFill(iconRect, 2f); // 2px padding
        var iconImage = iconGo.GetComponent<Image>();
        iconImage.color = Color.white;
        iconImage.preserveAspect = true;

        // ── CooldownOverlay (radial fill, on top of icon) ──
        var overlayGo = CreateChildImage("CooldownOverlay", slotGo.transform);
        var overlayRect = overlayGo.GetComponent<RectTransform>();
        StretchFill(overlayRect);
        var overlayImage = overlayGo.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.7f);
        overlayImage.type = Image.Type.Filled;
        overlayImage.fillMethod = Image.FillMethod.Radial360;
        overlayImage.fillOrigin = (int)Image.Origin360.Top;
        overlayImage.fillClockwise = false;
        overlayImage.fillAmount = 0f;
        overlayImage.enabled = false;

        // ── KeyText (top-left corner, e.g. "Q") ──
        string keyLabel = name.Replace("Slot_", "");
        var keyGo = CreateTMPText("KeyText", slotGo.transform, keyLabel, 14,
            TextAlignmentOptions.TopLeft, FontStyles.Bold);
        var keyRect = keyGo.GetComponent<RectTransform>();
        StretchFill(keyRect, 4f);

        // ── CooldownText (center, large, hidden by default) ──
        var cdGo = CreateTMPText("CooldownText", slotGo.transform, "", 24,
            TextAlignmentOptions.Center, FontStyles.Bold);
        var cdRect = cdGo.GetComponent<RectTransform>();
        StretchFill(cdRect);
        var cdTmp = cdGo.GetComponent<TextMeshProUGUI>();
        cdTmp.enabled = false;

        // Wire slot serialized references
        WireSlot(slotUI, iconImage, overlayImage,
                 keyGo.GetComponent<TextMeshProUGUI>(),
                 cdTmp);

        return slotGo;
    }

    // ── Helpers ──

    private static GameObject CreatePanel(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>();
        return go;
    }

    private static GameObject CreateChildImage(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<Image>();
        return go;
    }

    private static GameObject CreateTMPText(string name, Transform parent, string text,
        float fontSize, TextAlignmentOptions alignment, FontStyles style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.enableAutoSizing = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;

        // Add outline for readability
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Color.black;

        return go;
    }

    private static void StretchFill(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    // ── Wiring SerializedFields ──

    private static void WireSlot(AbilitySlotUI slot, Image icon, Image overlay,
        TextMeshProUGUI keyText, TextMeshProUGUI cdText)
    {
        var so = new SerializedObject(slot);
        so.FindProperty("iconImage").objectReferenceValue = icon;
        so.FindProperty("cooldownOverlay").objectReferenceValue = overlay;
        so.FindProperty("keyText").objectReferenceValue = keyText;
        so.FindProperty("cooldownText").objectReferenceValue = cdText;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireAbilityBar(AbilityBarUI bar, AbilitySlotUI[] slots)
    {
        var so = new SerializedObject(bar);
        var slotsProperty = so.FindProperty("slots");
        slotsProperty.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
        {
            slotsProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void WireHUDManager(HUDManager manager, AbilityBarUI bar, ClassSelectionUI classSel)
    {
        var so = new SerializedObject(manager);
        so.FindProperty("abilityBar").objectReferenceValue = bar;
        so.FindProperty("classSelection").objectReferenceValue = classSel;

        // Auto-discover NetworkClient in scene
        var networkClient = Object.FindFirstObjectByType<NetworkClient>();
        if (networkClient != null)
        {
            so.FindProperty("networkClient").objectReferenceValue = networkClient;
            Debug.Log($"[HUDCanvasCreator] Auto-wired NetworkClient: {networkClient.gameObject.name}");
        }
        else
        {
            Debug.LogWarning("[HUDCanvasCreator] No NetworkClient found in scene — assign manually.");
        }

        // Auto-discover CharacterClass assets and build array indexed by classType
        var guids = AssetDatabase.FindAssets("t:CharacterClass");
        int maxIndex = 0;
        var classAssets = new System.Collections.Generic.List<(int index, CharacterClass asset)>();
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var cc = AssetDatabase.LoadAssetAtPath<CharacterClass>(path);
            if (cc != null)
            {
                int idx = (int)cc.classType;
                if (idx > maxIndex) maxIndex = idx;
                classAssets.Add((idx, cc));
                Debug.Log($"[HUDCanvasCreator] Found CharacterClass: {cc.className} (type={cc.classType}, index={idx})");
            }
        }

        if (classAssets.Count > 0)
        {
            var prop = so.FindProperty("characterClasses");
            prop.arraySize = maxIndex + 1;
            // Clear all slots first
            for (int i = 0; i <= maxIndex; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = null;
            // Assign each class at its enum index
            foreach (var (idx, asset) in classAssets)
                prop.GetArrayElementAtIndex(idx).objectReferenceValue = asset;

            Debug.Log($"[HUDCanvasCreator] Auto-wired {classAssets.Count} CharacterClass asset(s) (array size={maxIndex + 1})");
        }
        else
        {
            Debug.LogWarning("[HUDCanvasCreator] No CharacterClass assets found in project.");
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Class Selection Panel ──

    private static GameObject CreateClassSelectionPanel(Transform parent)
    {
        // Root panel (full-screen overlay)
        var panelGo = new GameObject("ClassSelection");
        panelGo.transform.SetParent(parent, false);

        var panelRect = panelGo.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = panelGo.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.75f);

        // ClassSelectionUI component
        var classSelUI = panelGo.AddComponent<ClassSelectionUI>();

        // Title text
        var titleGo = CreateTMPText("Title", panelGo.transform, "Select Your Class",
            36, TextAlignmentOptions.Center, FontStyles.Bold);
        var titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 1f);
        titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(600f, 60f);
        titleRect.anchoredPosition = new Vector2(0f, -60f);

        // Button container (centered grid)
        var containerGo = new GameObject("ButtonContainer");
        containerGo.transform.SetParent(panelGo.transform, false);

        var containerRect = containerGo.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.sizeDelta = new Vector2(800f, 400f);
        containerRect.anchoredPosition = Vector2.zero;

        // Grid layout for buttons
        var grid = containerGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(180f, 50f);
        grid.spacing = new Vector2(15f, 15f);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;

        // Wire serialized fields on ClassSelectionUI
        var so = new SerializedObject(classSelUI);
        so.FindProperty("panel").objectReferenceValue = panelGo;
        so.FindProperty("buttonContainer").objectReferenceValue = containerGo.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Start hidden (will be shown by HUDManager when needed)
        panelGo.SetActive(false);

        return panelGo;
    }
}

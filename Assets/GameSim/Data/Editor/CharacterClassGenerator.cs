using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Linq;
using MOBANet.GameSim.Data;
using MOBANet.Client.Animation;
using MOBANet.Client.UI;

/// <summary>
/// Editor tool to generate CharacterClass and AbilityDefinition assets.
/// Menu: Ryvax > Generate Character Classes
/// </summary>
public class CharacterClassGenerator : EditorWindow
{
    [MenuItem("Ryvax/Generate Character Classes")]
    public static void ShowWindow()
    {
        GetWindow<CharacterClassGenerator>("Class Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Character Class Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This will generate CharacterClass and AbilityDefinition assets.\n" +
            "Assets are created in Assets/Character/Class/<ClassName>/",
            MessageType.Info);

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Generate Archer Class", GUILayout.Height(30)))
        {
            GenerateArcherClass();
        }
    }

    private static void GenerateArcherClass()
    {
        var passive = new AbilityData(
            "Lethal Focus",
            "Each basic attack grants 1 Focus stack. At 5 stacks, the next basic attack is guaranteed to critically strike and consumes all stacks.",
            AbilityTargetType.Self, TargetFilter.Allies, 0f, 0f, 600f
        );

        var abilities = new AbilityData[]
        {
            new AbilityData(
                "Piercing Shot",
                "Projectile skillshot. Range 1200, width 100, speed 2000, cast time 0.25s. Deals 80/120/160 physical damage.",
                AbilityTargetType.Skillshot, TargetFilter.Enemies, 80f, 6f, 1200f,
                "Relentless Shot", "Hitting an enemy champion resets Bloodrush cooldown.",
                "Tactical Shot", "Hitting an enemy champion resets Evasive Roll cooldown."
            ),
            new AbilityData(
                "Bloodrush",
                "Self buff for 4s granting 25/35/45% attack speed. Next basic attack is empowered dealing 40/70/100 bonus physical damage. Recast refreshes empowered attack and extends duration by 4s (max 8s).",
                AbilityTargetType.Self, TargetFilter.Allies, 40f, 12f, 0f,
                "Marked Prey", "Empowered attack marks target until Bloodrush ends. Basic attacks against marked target gain +200 range.",
                "Ricochet Mark", "Empowered attack marks target until Bloodrush ends. Basic attacks against marked target bounce to up to 3 nearby enemies dealing 60% damage."
            ),
            new AbilityData(
                "Evasive Roll",
                "Dash a short distance in the target direction.",
                AbilityTargetType.Self, TargetFilter.Allies, 0f, 10f, 0f,
                "Fleet Escape", "After the dash, gain 30% movement speed for 2.5s.",
                "Shadow Roll", "Become invisible for 1s after the dash. Attacking or casting breaks invisibility."
            ),
            new AbilityData(
                "Deadwind",
                "Long wind-up, fires a high-velocity shot at the chosen enemy champion. Deals 200/350/500 physical damage.",
                AbilityTargetType.Targeted, TargetFilter.Enemies, 200f, 50f, 0f,
                "Inevitable Shot", "Projectile cannot be intercepted or blocked. Single target only.",
                "Detonating Verdict", "On hit, explodes in a small area dealing damage to nearby enemies."
            ),
        };

        var classAsset = GenerateClass("Archer", CharacterClassType.Archer, passive, abilities);
        AutoWireHUDManager(classAsset, CharacterClassType.Archer);
        Debug.Log("Archer class generated and wired to HUDManager!");
    }

    private static CharacterClass GenerateClass(string className, CharacterClassType classType,
        AbilityData? passiveData, AbilityData[] abilities)
    {
        string basePath = $"Assets/Character/Class/{className}";

        // Create directory if needed
        if (!AssetDatabase.IsValidFolder(basePath))
        {
            string parentPath = "Assets/Character/Class";
            if (!AssetDatabase.IsValidFolder("Assets/Character"))
                AssetDatabase.CreateFolder("Assets", "Character");
            if (!AssetDatabase.IsValidFolder(parentPath))
                AssetDatabase.CreateFolder("Assets/Character", "Class");
            AssetDatabase.CreateFolder(parentPath, className);
        }

        // Create Abilities subfolder
        string abilitiesPath = $"{basePath}/Abilities";
        if (!AssetDatabase.IsValidFolder(abilitiesPath))
            AssetDatabase.CreateFolder(basePath, "Abilities");

        // Create passive
        AbilityDefinition passiveAsset = null;
        if (passiveData.HasValue)
        {
            passiveAsset = CreateAbilityAsset(passiveData.Value, $"{abilitiesPath}/{className}_00_Passive_{SanitizeName(passiveData.Value.name)}.asset");
        }

        // Create abilities
        AbilityDefinition[] abilityAssets = new AbilityDefinition[4];
        string[] slotNames = { "01", "02", "03", "04" };

        for (int i = 0; i < abilities.Length && i < 4; i++)
        {
            string path = $"{abilitiesPath}/{className}_{slotNames[i]}_{SanitizeName(abilities[i].name)}.asset";
            abilityAssets[i] = CreateAbilityAsset(abilities[i], path);
        }

        // Create class asset
        var classAsset = ScriptableObject.CreateInstance<CharacterClass>();
        var classSO = new SerializedObject(classAsset);
        classSO.FindProperty("className").stringValue = className;
        classSO.FindProperty("classType").enumValueIndex = (int)classType;

        if (passiveAsset != null) classSO.FindProperty("passive").objectReferenceValue = passiveAsset;
        if (abilityAssets[0] != null) classSO.FindProperty("ability1").objectReferenceValue = abilityAssets[0];
        if (abilityAssets[1] != null) classSO.FindProperty("ability2").objectReferenceValue = abilityAssets[1];
        if (abilityAssets[2] != null) classSO.FindProperty("ability3").objectReferenceValue = abilityAssets[2];
        if (abilityAssets[3] != null) classSO.FindProperty("ability4").objectReferenceValue = abilityAssets[3];

        classSO.ApplyModifiedPropertiesWithoutUndo();

        string classPath = $"{basePath}/{className}Class.asset";
        if (File.Exists(classPath))
            AssetDatabase.DeleteAsset(classPath);
        AssetDatabase.CreateAsset(classAsset, classPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return classAsset;
    }

    private static void AutoWireHUDManager(CharacterClass classAsset, CharacterClassType classType)
    {
        // Find HUDManager in the current scene
        var hudManager = Object.FindFirstObjectByType<HUDManager>();
        if (hudManager == null)
        {
            Debug.LogWarning("[ClassGenerator] No HUDManager found in scene. Open Bootstrap scene and re-run.");
            return;
        }

        var hudSO = new SerializedObject(hudManager);
        int classIndex = (int)classType; // Archer=1

        // Resize characterClasses array if needed
        var classesProp = hudSO.FindProperty("characterClasses");
        if (classesProp.arraySize <= classIndex)
        {
            classesProp.arraySize = classIndex + 1;
        }

        // Set the class asset at the correct index
        classesProp.GetArrayElementAtIndex(classIndex).objectReferenceValue = classAsset;

        // Auto-wire AbilityBarUI if not already set
        var abilityBarProp = hudSO.FindProperty("abilityBar");
        if (abilityBarProp.objectReferenceValue == null)
        {
            var abilityBar = Object.FindFirstObjectByType<AbilityBarUI>();
            if (abilityBar != null)
            {
                abilityBarProp.objectReferenceValue = abilityBar;
                Debug.Log("[ClassGenerator] Auto-wired AbilityBarUI to HUDManager.");
            }
            else
            {
                Debug.LogWarning("[ClassGenerator] No AbilityBarUI found in scene.");
            }
        }

        hudSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(hudManager);
        EditorSceneManager.MarkSceneDirty(hudManager.gameObject.scene);

        Debug.Log($"[ClassGenerator] Wired {classType} to HUDManager.characterClasses[{classIndex}]");
    }

    private static AbilityDefinition CreateAbilityAsset(AbilityData data, string path)
    {
        var ability = ScriptableObject.CreateInstance<AbilityDefinition>();

        var so = new SerializedObject(ability);
        so.FindProperty("abilityName").stringValue = data.name;
        so.FindProperty("description").stringValue = data.description;
        so.FindProperty("baseDamage").floatValue = data.damage;
        so.FindProperty("baseCooldown").floatValue = data.cooldown;
        so.FindProperty("baseRange").floatValue = data.range;
        so.FindProperty("targetType").enumValueIndex = (int)data.targetType;
        so.FindProperty("targetFilter").enumValueIndex = (int)data.targetFilter;
        so.FindProperty("spec1Name").stringValue = data.spec1Name;
        so.FindProperty("spec1").stringValue = data.spec1;
        so.FindProperty("spec2Name").stringValue = data.spec2Name;
        so.FindProperty("spec2").stringValue = data.spec2;
        so.FindProperty("spec3Name").stringValue = data.spec3Name;
        so.FindProperty("spec3").stringValue = data.spec3;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (File.Exists(path))
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(ability, path);

        return ability;
    }

    private static string SanitizeName(string name)
    {
        return name.Replace(" ", "");
    }

    private struct AbilityData
    {
        public string name;
        public string description;
        public AbilityTargetType targetType;
        public TargetFilter targetFilter;
        public float damage;
        public float cooldown;
        public float range;
        public string spec1Name;
        public string spec1;
        public string spec2Name;
        public string spec2;
        public string spec3Name;
        public string spec3;

        public AbilityData(string name, string desc, AbilityTargetType target, TargetFilter filter,
            float damage, float cooldown, float range,
            string spec1Name = "", string spec1 = "",
            string spec2Name = "", string spec2 = "",
            string spec3Name = "", string spec3 = "")
        {
            this.name = name;
            this.description = desc;
            this.targetType = target;
            this.targetFilter = filter;
            this.damage = damage;
            this.cooldown = cooldown;
            this.range = range;
            this.spec1Name = spec1Name;
            this.spec1 = spec1;
            this.spec2Name = spec2Name;
            this.spec2 = spec2;
            this.spec3Name = spec3Name;
            this.spec3 = spec3;
        }
    }
}

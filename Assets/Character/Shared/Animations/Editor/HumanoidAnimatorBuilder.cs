using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Builder pour créer automatiquement le HumanoidAnimatorController avec tous les layers de classes
/// Usage: Unity Menu > Character/Build Animator Controller
/// </summary>
public class HumanoidAnimatorBuilder : EditorWindow
{
    private const string CONTROLLER_PATH = "Assets/Character/Shared/Animations/HumanoidAnimatorController.controller";
    private const string UPPER_BODY_MASK_PATH = "Assets/Character/Shared/Animations/UpperBodyMask.mask";
    private const string FULL_BODY_MASK_PATH = "Assets/Character/Shared/Animations/FullBodyMask.mask";

    private AnimatorController controller;
    private AvatarMask upperBodyMask;
    private AvatarMask fullBodyMask;

    // Classes disponibles
    private enum CharacterClass
    {
        Archer,
        Mage,
        Fighter,
        Assassin,
        Tank,
        Healer,
        Summoner,
        Warrior
    }

    private bool[] selectedClasses = new bool[System.Enum.GetValues(typeof(CharacterClass)).Length];
    private Vector2 scrollPos;

    [MenuItem("Character/Build Animator Controller")]
    public static void ShowWindow()
    {
        var window = GetWindow<HumanoidAnimatorBuilder>("Animator Builder");
        window.minSize = new Vector2(400, 600);
    }

    void OnEnable()
    {
        // Par défaut: activer Archer, Mage, Fighter
        selectedClasses[0] = true; // Archer
        selectedClasses[1] = true; // Mage
        selectedClasses[2] = true; // Fighter
    }

    void OnGUI()
    {
        try
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label("Humanoid Animator Controller Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Load existing assets
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH);
            upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UPPER_BODY_MASK_PATH);
            fullBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(FULL_BODY_MASK_PATH);

            EditorGUILayout.LabelField("Assets", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);
            EditorGUILayout.ObjectField("Upper Body Mask", upperBodyMask, typeof(AvatarMask), false);
            EditorGUILayout.ObjectField("Full Body Mask", fullBodyMask, typeof(AvatarMask), false);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Classes à Générer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Sélectionnez les classes à inclure dans le controller", MessageType.Info);

            // Checkboxes pour chaque classe
            var classNames = System.Enum.GetNames(typeof(CharacterClass));
            for (int i = 0; i < classNames.Length; i++)
            {
                selectedClasses[i] = EditorGUILayout.ToggleLeft(classNames[i], selectedClasses[i]);
            }

            EditorGUILayout.Space(10);

            // Bouton de génération
            if (GUILayout.Button("🔨 Build Complete Controller", GUILayout.Height(50)))
            {
                EditorApplication.delayCall += BuildController;
            }

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Clear All Layers (Keep Base)", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += ClearAllLayers;
            }
        }
        finally
        {
            EditorGUILayout.EndScrollView();
        }
    }

    void BuildController()
    {
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Error", "Controller not found at:\n" + CONTROLLER_PATH, "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Rebuild Controller",
            "This will recreate the entire animator controller structure.\n\nContinue?",
            "Yes", "Cancel"))
        {
            return;
        }

        Debug.Log("🔨 Building Humanoid Animator Controller...");

        // 1. Remove all sub-assets (clean slate)
        RemoveAllSubAssets();

        // 2. Setup parameters
        SetupParameters();

        // 3. Clear and rebuild base layer
        RebuildBaseLayer();

        // 4. Clear existing class layers
        ClearClassLayers();

        // 5. Create selected class layers
        CreateClassLayers();

        // 6. Save
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("✅ Animator Controller built successfully!");
        EditorUtility.DisplayDialog("Success", "Animator Controller built successfully!", "OK");
    }

    void RemoveAllSubAssets()
    {
        Debug.Log("Removing existing sub-assets...");

        // Get the base layer state machine (we need to keep it)
        var baseLayerSM = controller.layers.Length > 0 ? controller.layers[0].stateMachine : null;

        // Get all sub-assets
        var subAssets = AssetDatabase.LoadAllAssetsAtPath(CONTROLLER_PATH);

        foreach (var asset in subAssets)
        {
            // Don't destroy the controller itself
            if (asset is AnimatorController) continue;

            // Don't destroy the base layer state machine
            if (asset == baseLayerSM) continue;

            // Destroy all other sub-assets (StateMachines, BlendTrees, etc.)
            if (asset != null && AssetDatabase.IsSubAsset(asset))
            {
                Object.DestroyImmediate(asset, true);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    void SetupParameters()
    {
        Debug.Log("Setting up parameters...");

        // Clear existing parameters
        controller.parameters = new AnimatorControllerParameter[0];

        // Base parameters
        AddParameter("Speed", AnimatorControllerParameterType.Float);
        AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        AddParameter("IsJumping", AnimatorControllerParameterType.Bool);
        AddParameter("IsFalling", AnimatorControllerParameterType.Bool);
        AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        AddParameter("Die", AnimatorControllerParameterType.Trigger);
        AddParameter("GetHit", AnimatorControllerParameterType.Trigger);
        AddParameter("Block", AnimatorControllerParameterType.Bool);

        // Archer parameters
        if (selectedClasses[(int)CharacterClass.Archer])
        {
            AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
            AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
            AddParameter("Reload", AnimatorControllerParameterType.Trigger);
        }

        // Mage parameters
        if (selectedClasses[(int)CharacterClass.Mage])
        {
            AddParameter("IsCasting", AnimatorControllerParameterType.Bool);
            AddParameter("CastFireball", AnimatorControllerParameterType.Trigger);
            AddParameter("CastMeteor", AnimatorControllerParameterType.Trigger);
            AddParameter("CastHeal", AnimatorControllerParameterType.Trigger);
        }

        // Fighter parameters
        if (selectedClasses[(int)CharacterClass.Fighter])
        {
            AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
            AddParameter("HeavyAttack", AnimatorControllerParameterType.Trigger);
        }

        // Assassin parameters
        if (selectedClasses[(int)CharacterClass.Assassin])
        {
            AddParameter("Backstab", AnimatorControllerParameterType.Trigger);
            AddParameter("Stealth", AnimatorControllerParameterType.Bool);
            AddParameter("DashAttack", AnimatorControllerParameterType.Trigger);
        }

        // Tank parameters
        if (selectedClasses[(int)CharacterClass.Tank])
        {
            AddParameter("ShieldBlock", AnimatorControllerParameterType.Bool);
            AddParameter("ShieldBash", AnimatorControllerParameterType.Trigger);
            AddParameter("Taunt", AnimatorControllerParameterType.Trigger);
        }

        // Healer parameters
        if (selectedClasses[(int)CharacterClass.Healer])
        {
            AddParameter("Heal", AnimatorControllerParameterType.Trigger);
            AddParameter("AreaHeal", AnimatorControllerParameterType.Trigger);
            AddParameter("Buff", AnimatorControllerParameterType.Trigger);
        }

        // Summoner parameters
        if (selectedClasses[(int)CharacterClass.Summoner])
        {
            AddParameter("Summon", AnimatorControllerParameterType.Trigger);
            AddParameter("CommandMinion", AnimatorControllerParameterType.Trigger);
        }

        // Warrior parameters
        if (selectedClasses[(int)CharacterClass.Warrior])
        {
            AddParameter("MeleeAttack", AnimatorControllerParameterType.Trigger);
            AddParameter("Charge", AnimatorControllerParameterType.Trigger);
            AddParameter("Whirlwind", AnimatorControllerParameterType.Trigger);
        }
    }

    void RebuildBaseLayer()
    {
        Debug.Log("Rebuilding Base Layer...");

        var baseLayer = controller.layers[0];
        var rootSM = baseLayer.stateMachine;

        // Clear existing
        ClearStateMachine(rootSM);

        // Create Locomotion SubStateMachine (AddStateMachine auto-adds as sub-asset)
        var locomotionSM = rootSM.AddStateMachine("Locomotion", new Vector3(50, 120, 0));
        CreateLocomotionStates(locomotionSM);

        // Create Combat SubStateMachine (AddStateMachine auto-adds as sub-asset)
        var combatSM = rootSM.AddStateMachine("Combat", new Vector3(50, 280, 0));
        CreateCombatStates(combatSM);

        // Create Death state
        var deathState = rootSM.AddState("Death", new Vector3(600, 120, 0));

        // Any State -> Death
        var deathTransition = rootSM.AddAnyStateTransition(deathState);
        deathTransition.AddCondition(AnimatorConditionMode.If, 0, "Die");
        deathTransition.duration = 0.25f;

        // Set default state
        rootSM.defaultState = locomotionSM.states.Length > 0 ? locomotionSM.states[0].state : null;
    }

    void CreateLocomotionStates(AnimatorStateMachine sm)
    {
        // Single Locomotion state with Idle → Run blend tree
        var locomotion = sm.AddState("Locomotion", new Vector3(300, 120, 0));
        var jump = sm.AddState("Jump", new Vector3(300, 240, 0));
        var jumpLoop = sm.AddState("Jump Loop", new Vector3(550, 240, 0));
        var fall = sm.AddState("Fall", new Vector3(300, 360, 0));
        var land = sm.AddState("Land", new Vector3(550, 360, 0));

        // Create blend tree: Idle (0.0) → Run (1.0)
        var blendTree = new BlendTree
        {
            name = "Idle-Run Blend",
            blendParameter = "Speed",
            blendType = BlendTreeType.Simple1D,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(blendTree, controller);
        locomotion.motion = blendTree;

        // Note: Children will be added manually in Unity or via future code
        // Expected structure:
        //   - Idle animation at threshold 0.0
        //   - Run animation at threshold 1.0

        sm.defaultState = locomotion;

        // Transitions from Locomotion
        var locomotionToJump = locomotion.AddTransition(jump);
        locomotionToJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        locomotionToJump.duration = 0.1f;

        // Jump cycle
        var jumpToLoop = jump.AddTransition(jumpLoop);
        jumpToLoop.hasExitTime = true;
        jumpToLoop.exitTime = 0.9f;
        jumpToLoop.duration = 0.1f;

        var loopToFall = jumpLoop.AddTransition(fall);
        loopToFall.AddCondition(AnimatorConditionMode.If, 0, "IsFalling");
        loopToFall.duration = 0.1f;

        var fallToLand = fall.AddTransition(land);
        fallToLand.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
        fallToLand.duration = 0.1f;

        var landToLocomotion = land.AddTransition(locomotion);
        landToLocomotion.hasExitTime = true;
        landToLocomotion.exitTime = 0.9f;
        landToLocomotion.duration = 0.1f;
    }

    void CreateCombatStates(AnimatorStateMachine sm)
    {
        var combatIdle = sm.AddState("Combat Idle", new Vector3(300, 120, 0));
        var getHit = sm.AddState("Get Hit", new Vector3(550, 120, 0));
        var block = sm.AddState("Block", new Vector3(300, 240, 0));

        sm.defaultState = combatIdle;

        var idleToHit = combatIdle.AddTransition(getHit);
        idleToHit.AddCondition(AnimatorConditionMode.If, 0, "GetHit");
        idleToHit.duration = 0.1f;

        var hitToIdle = getHit.AddTransition(combatIdle);
        hitToIdle.hasExitTime = true;
        hitToIdle.exitTime = 0.9f;
        hitToIdle.duration = 0.2f;

        var idleToBlock = combatIdle.AddTransition(block);
        idleToBlock.AddCondition(AnimatorConditionMode.If, 0, "Block");
        idleToBlock.duration = 0.1f;

        var blockToIdle = block.AddTransition(combatIdle);
        blockToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Block");
        blockToIdle.duration = 0.2f;
    }

    void ClearClassLayers()
    {
        Debug.Log("Clearing class layers...");

        var layers = new List<AnimatorControllerLayer> { controller.layers[0] }; // Keep base layer
        controller.layers = layers.ToArray();
    }

    void CreateClassLayers()
    {
        Debug.Log("Creating class layers...");

        var classNames = System.Enum.GetNames(typeof(CharacterClass));
        for (int i = 0; i < classNames.Length; i++)
        {
            if (selectedClasses[i])
            {
                var className = (CharacterClass)i;
                Debug.Log($"  + Creating {className} Layer");
                CreateClassLayer(className);
            }
        }
    }

    void CreateClassLayer(CharacterClass classType)
    {
        var layers = controller.layers.ToList();

        // Determine mask
        AvatarMask mask = null;
        switch (classType)
        {
            case CharacterClass.Archer:
            case CharacterClass.Mage:
            case CharacterClass.Healer:
            case CharacterClass.Summoner:
                mask = upperBodyMask;
                break;
            case CharacterClass.Fighter:
            case CharacterClass.Assassin:
            case CharacterClass.Tank:
            case CharacterClass.Warrior:
                mask = fullBodyMask;
                break;
        }

        var layer = new AnimatorControllerLayer
        {
            name = $"{classType} Layer",
            stateMachine = new AnimatorStateMachine
            {
                name = $"{classType} Layer",
                hideFlags = HideFlags.HideInHierarchy
            },
            avatarMask = mask,
            defaultWeight = 0f,
            blendingMode = AnimatorLayerBlendingMode.Override
        };

        AssetDatabase.AddObjectToAsset(layer.stateMachine, controller);
        layers.Add(layer);
        controller.layers = layers.ToArray();

        // Create states based on class type
        switch (classType)
        {
            case CharacterClass.Archer:
                CreateArcherStates(layer.stateMachine);
                break;
            case CharacterClass.Mage:
                CreateMageStates(layer.stateMachine);
                break;
            case CharacterClass.Fighter:
                CreateFighterStates(layer.stateMachine);
                break;
            case CharacterClass.Assassin:
                CreateAssassinStates(layer.stateMachine);
                break;
            case CharacterClass.Tank:
                CreateTankStates(layer.stateMachine);
                break;
            case CharacterClass.Healer:
                CreateHealerStates(layer.stateMachine);
                break;
            case CharacterClass.Summoner:
                CreateSummonerStates(layer.stateMachine);
                break;
            case CharacterClass.Warrior:
                CreateWarriorStates(layer.stateMachine);
                break;
        }
    }

    void CreateArcherStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Archer Idle", new Vector3(300, 120, 0));
        var aimStart = sm.AddState("Aim Start", new Vector3(300, 240, 0));
        var aimIdle = sm.AddState("Aim Idle", new Vector3(550, 240, 0));
        var shoot = sm.AddState("Shoot", new Vector3(550, 360, 0));
        var aimEnd = sm.AddState("Aim End", new Vector3(300, 360, 0));
        var reload = sm.AddState("Reload", new Vector3(300, 480, 0));

        sm.defaultState = idle;

        // Transitions
        var idleToAimStart = idle.AddTransition(aimStart);
        idleToAimStart.AddCondition(AnimatorConditionMode.If, 0, "IsAiming");
        idleToAimStart.duration = 0.1f;

        var aimStartToIdle = aimStart.AddTransition(aimIdle);
        aimStartToIdle.hasExitTime = true;
        aimStartToIdle.exitTime = 0.9f;
        aimStartToIdle.duration = 0.1f;

        var aimIdleToShoot = aimIdle.AddTransition(shoot);
        aimIdleToShoot.AddCondition(AnimatorConditionMode.If, 0, "Shoot");
        aimIdleToShoot.duration = 0.05f;

        var shootToAimIdle = shoot.AddTransition(aimIdle);
        shootToAimIdle.hasExitTime = true;
        shootToAimIdle.exitTime = 0.9f;
        shootToAimIdle.duration = 0.1f;

        var aimIdleToEnd = aimIdle.AddTransition(aimEnd);
        aimIdleToEnd.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAiming");
        aimIdleToEnd.duration = 0.1f;

        var aimEndToIdle = aimEnd.AddTransition(idle);
        aimEndToIdle.hasExitTime = true;
        aimEndToIdle.exitTime = 0.9f;
        aimEndToIdle.duration = 0.1f;

        var idleToReload = idle.AddTransition(reload);
        idleToReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
        idleToReload.duration = 0.1f;

        var reloadToIdle = reload.AddTransition(idle);
        reloadToIdle.hasExitTime = true;
        reloadToIdle.exitTime = 0.9f;
        reloadToIdle.duration = 0.2f;
    }

    void CreateMageStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Mage Idle", new Vector3(300, 120, 0));
        var castStart = sm.AddState("Cast Start", new Vector3(300, 240, 0));
        var castLoop = sm.AddState("Cast Loop", new Vector3(550, 240, 0));
        var castEnd = sm.AddState("Cast End", new Vector3(300, 360, 0));
        var fireball = sm.AddState("Cast Fireball", new Vector3(550, 360, 0));
        var meteor = sm.AddState("Cast Meteor", new Vector3(550, 480, 0));
        var heal = sm.AddState("Cast Heal", new Vector3(550, 600, 0));

        sm.defaultState = idle;

        // Basic casting flow
        var idleToCastStart = idle.AddTransition(castStart);
        idleToCastStart.AddCondition(AnimatorConditionMode.If, 0, "IsCasting");
        idleToCastStart.duration = 0.1f;

        var startToLoop = castStart.AddTransition(castLoop);
        startToLoop.hasExitTime = true;
        startToLoop.exitTime = 0.9f;

        var loopToEnd = castLoop.AddTransition(castEnd);
        loopToEnd.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCasting");

        var endToIdle = castEnd.AddTransition(idle);
        endToIdle.hasExitTime = true;
        endToIdle.exitTime = 0.9f;

        // Spell transitions
        AddSpellTransition(idle, fireball, "CastFireball");
        AddSpellTransition(idle, meteor, "CastMeteor");
        AddSpellTransition(idle, heal, "CastHeal");
    }

    void CreateFighterStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Fighter Idle", new Vector3(300, 120, 0));
        var attack1 = sm.AddState("Attack 1", new Vector3(550, 120, 0));
        var attack2 = sm.AddState("Attack 2", new Vector3(550, 240, 0));
        var attack3 = sm.AddState("Attack 3", new Vector3(550, 360, 0));
        var heavyAttack = sm.AddState("Heavy Attack", new Vector3(300, 360, 0));

        sm.defaultState = idle;

        // Combo chain
        var idleTo1 = idle.AddTransition(attack1);
        idleTo1.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        idleTo1.duration = 0.05f;

        var attack1To2 = attack1.AddTransition(attack2);
        attack1To2.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        attack1To2.hasExitTime = true;
        attack1To2.exitTime = 0.7f;

        var attack2To3 = attack2.AddTransition(attack3);
        attack2To3.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        attack2To3.hasExitTime = true;
        attack2To3.exitTime = 0.7f;

        // Back to idle
        AddExitToIdle(attack1, idle);
        AddExitToIdle(attack2, idle);
        AddExitToIdle(attack3, idle);

        // Heavy attack
        var idleToHeavy = idle.AddTransition(heavyAttack);
        idleToHeavy.AddCondition(AnimatorConditionMode.If, 0, "HeavyAttack");
        AddExitToIdle(heavyAttack, idle);
    }

    void CreateAssassinStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Assassin Idle", new Vector3(300, 120, 0));
        var stealth = sm.AddState("Stealth", new Vector3(550, 120, 0));
        var backstab = sm.AddState("Backstab", new Vector3(300, 240, 0));
        var dashAttack = sm.AddState("Dash Attack", new Vector3(550, 240, 0));

        sm.defaultState = idle;

        var idleToStealth = idle.AddTransition(stealth);
        idleToStealth.AddCondition(AnimatorConditionMode.If, 0, "Stealth");

        var stealthToIdle = stealth.AddTransition(idle);
        stealthToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "Stealth");

        AddSpellTransition(idle, backstab, "Backstab");
        AddSpellTransition(idle, dashAttack, "DashAttack");
    }

    void CreateTankStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Tank Idle", new Vector3(300, 120, 0));
        var shieldBlock = sm.AddState("Shield Block", new Vector3(550, 120, 0));
        var shieldBash = sm.AddState("Shield Bash", new Vector3(300, 240, 0));
        var taunt = sm.AddState("Taunt", new Vector3(550, 240, 0));

        sm.defaultState = idle;

        var idleToBlock = idle.AddTransition(shieldBlock);
        idleToBlock.AddCondition(AnimatorConditionMode.If, 0, "ShieldBlock");

        var blockToIdle = shieldBlock.AddTransition(idle);
        blockToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "ShieldBlock");

        AddSpellTransition(idle, shieldBash, "ShieldBash");
        AddSpellTransition(idle, taunt, "Taunt");
    }

    void CreateHealerStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Healer Idle", new Vector3(300, 120, 0));
        var heal = sm.AddState("Heal", new Vector3(550, 120, 0));
        var areaHeal = sm.AddState("Area Heal", new Vector3(300, 240, 0));
        var buff = sm.AddState("Buff", new Vector3(550, 240, 0));

        sm.defaultState = idle;

        AddSpellTransition(idle, heal, "Heal");
        AddSpellTransition(idle, areaHeal, "AreaHeal");
        AddSpellTransition(idle, buff, "Buff");
    }

    void CreateSummonerStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Summoner Idle", new Vector3(300, 120, 0));
        var summon = sm.AddState("Summon", new Vector3(550, 120, 0));
        var command = sm.AddState("Command Minion", new Vector3(300, 240, 0));

        sm.defaultState = idle;

        AddSpellTransition(idle, summon, "Summon");
        AddSpellTransition(idle, command, "CommandMinion");
    }

    void CreateWarriorStates(AnimatorStateMachine sm)
    {
        var idle = sm.AddState("Warrior Idle", new Vector3(300, 120, 0));
        var melee = sm.AddState("Melee Attack", new Vector3(550, 120, 0));
        var charge = sm.AddState("Charge", new Vector3(300, 240, 0));
        var whirlwind = sm.AddState("Whirlwind", new Vector3(550, 240, 0));

        sm.defaultState = idle;

        AddSpellTransition(idle, melee, "MeleeAttack");
        AddSpellTransition(idle, charge, "Charge");
        AddSpellTransition(idle, whirlwind, "Whirlwind");
    }

    // Helper methods
    void AddSpellTransition(AnimatorState from, AnimatorState to, string trigger)
    {
        var toSpell = from.AddTransition(to);
        toSpell.AddCondition(AnimatorConditionMode.If, 0, trigger);
        toSpell.duration = 0.05f;

        var toIdle = to.AddTransition(from);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.9f;
        toIdle.duration = 0.2f;
    }

    void AddExitToIdle(AnimatorState from, AnimatorState idle)
    {
        var toIdle = from.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.9f;
        toIdle.duration = 0.2f;
    }

    void AddParameter(string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name))
            return;

        controller.AddParameter(name, type);
    }

    void ClearStateMachine(AnimatorStateMachine sm)
    {
        sm.states = new ChildAnimatorState[0];
        sm.stateMachines = new ChildAnimatorStateMachine[0];
        sm.anyStateTransitions = new AnimatorStateTransition[0];
        sm.entryTransitions = new AnimatorTransition[0];
    }

    void ClearAllLayers()
    {
        if (controller == null) return;

        if (!EditorUtility.DisplayDialog("Clear Layers",
            "This will remove all class layers (Base Layer will be kept).\n\nContinue?",
            "Yes", "Cancel"))
        {
            return;
        }

        var layers = new List<AnimatorControllerLayer> { controller.layers[0] };
        controller.layers = layers.ToArray();

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ All class layers cleared");
        EditorUtility.DisplayDialog("Success", "All class layers have been removed", "OK");
    }
}

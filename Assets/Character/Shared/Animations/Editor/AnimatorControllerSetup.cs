using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

/// <summary>
/// Script d'édition pour configurer automatiquement le HumanoidAnimatorController
/// Utilisation: Unity Menu > Character/Setup Animator Controller
/// </summary>
public class AnimatorControllerSetup : EditorWindow
{
    private AnimatorController controller;
    private AvatarMask upperBodyMask;
    private AvatarMask fullBodyMask;

    // Animation clips (à assigner dans l'interface)
    [Header("Base Animations (Required)")]
    private AnimationClip idleClip;
    private AnimationClip walkClip;
    private AnimationClip runClip;
    private AnimationClip jumpStartClip;
    private AnimationClip jumpLoopClip;
    private AnimationClip jumpLandClip;
    private AnimationClip fallClip;

    [Header("Combat Animations")]
    private AnimationClip combatIdleClip;
    private AnimationClip getHitClip;
    private AnimationClip blockClip;
    private AnimationClip deathClip;

    [Header("Archer Animations")]
    private AnimationClip aimStartClip;
    private AnimationClip aimIdleClip;
    private AnimationClip aimEndClip;
    private AnimationClip shootClip;
    private AnimationClip reloadClip;

    private Vector2 scrollPos;

    [MenuItem("Character/Setup Animator Controller")]
    public static void ShowWindow()
    {
        GetWindow<AnimatorControllerSetup>("Animator Setup");
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Humanoid Animator Controller Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Load assets
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Character/Shared/Animations/HumanoidAnimatorController.controller");
        upperBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            "Assets/Character/Shared/Animations/UpperBodyMask.mask");
        fullBodyMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(
            "Assets/Character/Shared/Animations/FullBodyMask.mask");

        EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);
        EditorGUILayout.ObjectField("Upper Body Mask", upperBodyMask, typeof(AvatarMask), false);
        EditorGUILayout.ObjectField("Full Body Mask", fullBodyMask, typeof(AvatarMask), false);

        EditorGUILayout.Space();
        GUILayout.Label("Base Animations", EditorStyles.boldLabel);
        idleClip = EditorGUILayout.ObjectField("Idle", idleClip, typeof(AnimationClip), false) as AnimationClip;
        walkClip = EditorGUILayout.ObjectField("Walk", walkClip, typeof(AnimationClip), false) as AnimationClip;
        runClip = EditorGUILayout.ObjectField("Run", runClip, typeof(AnimationClip), false) as AnimationClip;
        jumpStartClip = EditorGUILayout.ObjectField("Jump Start", jumpStartClip, typeof(AnimationClip), false) as AnimationClip;
        jumpLoopClip = EditorGUILayout.ObjectField("Jump Loop", jumpLoopClip, typeof(AnimationClip), false) as AnimationClip;
        jumpLandClip = EditorGUILayout.ObjectField("Jump Land", jumpLandClip, typeof(AnimationClip), false) as AnimationClip;
        fallClip = EditorGUILayout.ObjectField("Fall", fallClip, typeof(AnimationClip), false) as AnimationClip;

        EditorGUILayout.Space();
        GUILayout.Label("Combat Animations", EditorStyles.boldLabel);
        combatIdleClip = EditorGUILayout.ObjectField("Combat Idle", combatIdleClip, typeof(AnimationClip), false) as AnimationClip;
        getHitClip = EditorGUILayout.ObjectField("Get Hit", getHitClip, typeof(AnimationClip), false) as AnimationClip;
        blockClip = EditorGUILayout.ObjectField("Block", blockClip, typeof(AnimationClip), false) as AnimationClip;
        deathClip = EditorGUILayout.ObjectField("Death", deathClip, typeof(AnimationClip), false) as AnimationClip;

        EditorGUILayout.Space();
        GUILayout.Label("Archer Animations", EditorStyles.boldLabel);
        aimStartClip = EditorGUILayout.ObjectField("Aim Start", aimStartClip, typeof(AnimationClip), false) as AnimationClip;
        aimIdleClip = EditorGUILayout.ObjectField("Aim Idle", aimIdleClip, typeof(AnimationClip), false) as AnimationClip;
        aimEndClip = EditorGUILayout.ObjectField("Aim End", aimEndClip, typeof(AnimationClip), false) as AnimationClip;
        shootClip = EditorGUILayout.ObjectField("Shoot", shootClip, typeof(AnimationClip), false) as AnimationClip;
        reloadClip = EditorGUILayout.ObjectField("Reload", reloadClip, typeof(AnimationClip), false) as AnimationClip;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Note: Les animations sont optionnelles pour ce setup initial. " +
            "Le script configurera la structure complète. Vous pourrez assigner les clips plus tard.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Setup Controller (Without Animations)", GUILayout.Height(40)))
        {
            SetupControllerStructure();
        }

        EditorGUI.BeginDisabledGroup(!HasRequiredAnimations());
        if (GUILayout.Button("Setup Controller (With Animations)", GUILayout.Height(40)))
        {
            SetupControllerWithAnimations();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();

        if (GUILayout.Button("Add Mage Layer", GUILayout.Height(30)))
        {
            AddMageLayer();
        }

        if (GUILayout.Button("Add Fighter Layer", GUILayout.Height(30)))
        {
            AddFighterLayer();
        }

        EditorGUILayout.EndScrollView();
    }

    bool HasRequiredAnimations()
    {
        return idleClip != null && walkClip != null && runClip != null;
    }

    void SetupControllerStructure()
    {
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Error", "Controller not found!", "OK");
            return;
        }

        Debug.Log("Setting up Animator Controller structure...");

        // Get Base Layer
        var baseLayer = controller.layers[0];
        var rootStateMachine = baseLayer.stateMachine;

        // Clear existing states (keep only what we need)
        rootStateMachine.states = new ChildAnimatorState[0];
        rootStateMachine.stateMachines = new ChildAnimatorStateMachine[0];

        // Create Locomotion SubStateMachine
        var locomotionSM = CreateLocomotionStateMachine(rootStateMachine);

        // Create Combat SubStateMachine
        var combatSM = CreateCombatStateMachine(rootStateMachine);

        // Create Death state
        var deathState = CreateState(rootStateMachine, "Death", deathClip, new Vector3(600, 120, 0));

        // Any State -> Death transition
        var deathTransition = rootStateMachine.AddAnyStateTransition(deathState);
        deathTransition.AddCondition(AnimatorConditionMode.If, 0, "Die");
        deathTransition.duration = 0.25f;

        // Setup Archer Layer
        SetupArcherLayer();

        // Assign Avatar Masks
        AssignAvatarMasks();

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ Controller structure setup complete!");
        EditorUtility.DisplayDialog("Success", "Animator Controller structure configured successfully!\n\nYou can now assign animation clips to each state.", "OK");
    }

    void SetupControllerWithAnimations()
    {
        SetupControllerStructure();
        Debug.Log("✅ Controller setup with animations complete!");
    }

    AnimatorStateMachine CreateLocomotionStateMachine(AnimatorStateMachine parent)
    {
        var locomotionSM = parent.AddStateMachine("Locomotion", new Vector3(50, 120, 0));

        // Create states
        var idle = CreateState(locomotionSM, "Idle", idleClip, new Vector3(300, 120, 0));
        var walkRun = CreateBlendTreeState(locomotionSM, "Walk/Run", new Vector3(550, 120, 0));
        var jump = CreateState(locomotionSM, "Jump", jumpStartClip, new Vector3(300, 240, 0));
        var jumpLoop = CreateState(locomotionSM, "Jump Loop", jumpLoopClip, new Vector3(550, 240, 0));
        var fall = CreateState(locomotionSM, "Fall", fallClip, new Vector3(300, 360, 0));
        var land = CreateState(locomotionSM, "Land", jumpLandClip, new Vector3(550, 360, 0));

        locomotionSM.defaultState = idle;

        // Transitions
        // Idle <-> Walk/Run
        var idleToWalk = idle.AddTransition(walkRun);
        idleToWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        idleToWalk.duration = 0.2f;

        var walkToIdle = walkRun.AddTransition(idle);
        walkToIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        walkToIdle.duration = 0.2f;

        // Idle -> Jump
        var idleToJump = idle.AddTransition(jump);
        idleToJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        idleToJump.duration = 0.1f;

        // Jump -> Jump Loop
        var jumpToLoop = jump.AddExitTransition();
        jumpToLoop.duration = 0.1f;
        jumpToLoop.hasExitTime = true;
        jumpToLoop.exitTime = 0.9f;

        var loopEntry = locomotionSM.AddEntryTransition(jumpLoop);

        // Jump Loop -> Fall (when not grounded)
        var loopToFall = jumpLoop.AddTransition(fall);
        loopToFall.AddCondition(AnimatorConditionMode.IfNot, 0, "IsGrounded");
        loopToFall.duration = 0.1f;

        // Fall -> Land (when grounded)
        var fallToLand = fall.AddTransition(land);
        fallToLand.AddCondition(AnimatorConditionMode.If, 0, "IsGrounded");
        fallToLand.duration = 0.1f;

        // Land -> Idle
        var landToIdle = land.AddExitTransition();
        landToIdle.duration = 0.1f;
        landToIdle.hasExitTime = true;
        landToIdle.exitTime = 0.9f;

        return locomotionSM;
    }

    AnimatorStateMachine CreateCombatStateMachine(AnimatorStateMachine parent)
    {
        var combatSM = parent.AddStateMachine("Combat", new Vector3(50, 280, 0));

        // Create states
        var combatIdle = CreateState(combatSM, "Combat Idle", combatIdleClip, new Vector3(300, 120, 0));
        var getHit = CreateState(combatSM, "Get Hit", getHitClip, new Vector3(550, 120, 0));
        var block = CreateState(combatSM, "Block", blockClip, new Vector3(300, 240, 0));

        combatSM.defaultState = combatIdle;

        // Transitions
        // Combat Idle -> Get Hit
        var idleToHit = combatIdle.AddTransition(getHit);
        idleToHit.AddCondition(AnimatorConditionMode.If, 0, "GetHit");
        idleToHit.duration = 0.1f;

        // Get Hit -> Combat Idle
        var hitToIdle = getHit.AddTransition(combatIdle);
        hitToIdle.duration = 0.2f;
        hitToIdle.hasExitTime = true;
        hitToIdle.exitTime = 0.9f;

        // Combat Idle -> Block
        var idleToBlock = combatIdle.AddTransition(block);
        idleToBlock.AddCondition(AnimatorConditionMode.If, 0, "Block");
        idleToBlock.duration = 0.1f;

        // Block -> Combat Idle
        var blockToIdle = block.AddTransition(combatIdle);
        blockToIdle.duration = 0.2f;
        blockToIdle.hasExitTime = true;
        blockToIdle.exitTime = 0.9f;

        return combatSM;
    }

    void SetupArcherLayer()
    {
        if (controller.layers.Length < 2) return;

        var archerLayer = controller.layers[1];
        var archerSM = archerLayer.stateMachine;

        // Clear existing
        archerSM.states = new ChildAnimatorState[0];
        archerSM.stateMachines = new ChildAnimatorStateMachine[0];

        // Create Archer Idle
        var archerIdle = CreateState(archerSM, "Archer Idle", null, new Vector3(300, 120, 0));
        archerSM.defaultState = archerIdle;

        // Create Aiming SubStateMachine
        var aimingSM = archerSM.AddStateMachine("Aiming", new Vector3(50, 240, 0));

        var aimStart = CreateState(aimingSM, "Aim Start", aimStartClip, new Vector3(300, 120, 0));
        var aimIdle = CreateState(aimingSM, "Aim Idle", aimIdleClip, new Vector3(550, 120, 0));
        var aimEnd = CreateState(aimingSM, "Aim End", aimEndClip, new Vector3(300, 240, 0));

        aimingSM.defaultState = aimStart;

        // Transitions within Aiming
        var startToIdle = aimStart.AddTransition(aimIdle);
        startToIdle.hasExitTime = true;
        startToIdle.exitTime = 0.9f;
        startToIdle.duration = 0.1f;

        var idleToEnd = aimIdle.AddTransition(aimEnd);
        idleToEnd.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAiming");
        idleToEnd.duration = 0.1f;

        // Archer Idle <-> Aiming
        var idleToAiming = archerIdle.AddTransition(aimingSM);
        idleToAiming.AddCondition(AnimatorConditionMode.If, 0, "IsAiming");
        idleToAiming.duration = 0.1f;

        var aimEndExit = aimEnd.AddExitTransition();
        aimEndExit.hasExitTime = true;
        aimEndExit.exitTime = 0.9f;
        aimEndExit.duration = 0.1f;

        // Create Shoot state
        var shoot = CreateState(archerSM, "Shoot", shootClip, new Vector3(550, 240, 0));

        // Aim Idle -> Shoot
        var aimToShoot = aimIdle.AddTransition(shoot);
        aimToShoot.AddCondition(AnimatorConditionMode.If, 0, "Shoot");
        aimToShoot.duration = 0.05f;

        // Shoot -> Aim Idle
        var shootToAim = shoot.AddTransition(aimIdle);
        shootToAim.hasExitTime = true;
        shootToAim.exitTime = 0.9f;
        shootToAim.duration = 0.1f;

        // Create Reload state
        var reload = CreateState(archerSM, "Reload", reloadClip, new Vector3(300, 360, 0));

        // Archer Idle -> Reload
        var idleToReload = archerIdle.AddTransition(reload);
        idleToReload.AddCondition(AnimatorConditionMode.If, 0, "Reload");
        idleToReload.duration = 0.1f;

        // Reload -> Archer Idle
        var reloadToIdle = reload.AddTransition(archerIdle);
        reloadToIdle.hasExitTime = true;
        reloadToIdle.exitTime = 0.9f;
        reloadToIdle.duration = 0.2f;
    }

    void AddMageLayer()
    {
        if (controller == null) return;

        // Add parameters
        AddParameter("IsCasting", AnimatorControllerParameterType.Bool);
        AddParameter("CastFireball", AnimatorControllerParameterType.Trigger);
        AddParameter("CastMeteor", AnimatorControllerParameterType.Trigger);
        AddParameter("CastHeal", AnimatorControllerParameterType.Trigger);

        // Create layer
        var layers = controller.layers.ToList();
        var mageLayer = new AnimatorControllerLayer
        {
            name = "Mage Layer",
            stateMachine = new AnimatorStateMachine
            {
                name = "Mage Layer",
                hideFlags = HideFlags.HideInHierarchy
            },
            avatarMask = upperBodyMask,
            defaultWeight = 0f,
            blendingMode = AnimatorLayerBlendingMode.Override
        };

        AssetDatabase.AddObjectToAsset(mageLayer.stateMachine, controller);
        layers.Add(mageLayer);
        controller.layers = layers.ToArray();

        var mageSM = mageLayer.stateMachine;

        // Create states
        var mageIdle = CreateState(mageSM, "Mage Idle", null, new Vector3(300, 120, 0));
        mageSM.defaultState = mageIdle;

        // Casting SubStateMachine
        var castingSM = mageSM.AddStateMachine("Casting", new Vector3(50, 240, 0));
        AssetDatabase.AddObjectToAsset(castingSM, controller);

        var castStart = CreateState(castingSM, "Cast Start", null, new Vector3(300, 120, 0));
        var castLoop = CreateState(castingSM, "Cast Loop", null, new Vector3(550, 120, 0));
        var castEnd = CreateState(castingSM, "Cast End", null, new Vector3(300, 240, 0));

        castingSM.defaultState = castStart;

        var startToLoop = castStart.AddTransition(castLoop);
        startToLoop.hasExitTime = true;
        startToLoop.exitTime = 0.9f;

        var loopToEnd = castLoop.AddTransition(castEnd);
        loopToEnd.AddCondition(AnimatorConditionMode.IfNot, 0, "IsCasting");

        var endExit = castEnd.AddExitTransition();
        endExit.hasExitTime = true;
        endExit.exitTime = 0.9f;

        // Spells
        var fireball = CreateState(mageSM, "Cast Fireball", null, new Vector3(550, 240, 0));
        var meteor = CreateState(mageSM, "Cast Meteor", null, new Vector3(550, 360, 0));
        var heal = CreateState(mageSM, "Cast Heal", null, new Vector3(550, 480, 0));

        // Transitions
        var idleToFireball = mageIdle.AddTransition(fireball);
        idleToFireball.AddCondition(AnimatorConditionMode.If, 0, "CastFireball");

        var fireballToIdle = fireball.AddTransition(mageIdle);
        fireballToIdle.hasExitTime = true;
        fireballToIdle.exitTime = 0.9f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ Mage Layer added!");
        EditorUtility.DisplayDialog("Success", "Mage Layer added successfully!", "OK");
    }

    void AddFighterLayer()
    {
        if (controller == null) return;

        // Add parameters
        AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
        AddParameter("HeavyAttack", AnimatorControllerParameterType.Trigger);

        // Create layer
        var layers = controller.layers.ToList();
        var fighterLayer = new AnimatorControllerLayer
        {
            name = "Fighter Layer",
            stateMachine = new AnimatorStateMachine
            {
                name = "Fighter Layer",
                hideFlags = HideFlags.HideInHierarchy
            },
            avatarMask = fullBodyMask,
            defaultWeight = 0f,
            blendingMode = AnimatorLayerBlendingMode.Override
        };

        AssetDatabase.AddObjectToAsset(fighterLayer.stateMachine, controller);
        layers.Add(fighterLayer);
        controller.layers = layers.ToArray();

        var fighterSM = fighterLayer.stateMachine;

        // Create states
        var fighterIdle = CreateState(fighterSM, "Fighter Idle", null, new Vector3(300, 120, 0));
        fighterSM.defaultState = fighterIdle;

        // Combo states
        var attack1 = CreateState(fighterSM, "Attack 1", null, new Vector3(550, 120, 0));
        var attack2 = CreateState(fighterSM, "Attack 2", null, new Vector3(550, 240, 0));
        var attack3 = CreateState(fighterSM, "Attack 3", null, new Vector3(550, 360, 0));
        var heavyAttack = CreateState(fighterSM, "Heavy Attack", null, new Vector3(300, 360, 0));

        // Combo transitions
        var idleTo1 = fighterIdle.AddTransition(attack1);
        idleTo1.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        idleTo1.AddCondition(AnimatorConditionMode.Equals, 1, "ComboIndex");

        var attack1To2 = attack1.AddTransition(attack2);
        attack1To2.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        attack1To2.AddCondition(AnimatorConditionMode.Equals, 2, "ComboIndex");

        var attack2To3 = attack2.AddTransition(attack3);
        attack2To3.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        attack2To3.AddCondition(AnimatorConditionMode.Equals, 3, "ComboIndex");

        // Back to idle
        var attack1ToIdle = attack1.AddTransition(fighterIdle);
        attack1ToIdle.hasExitTime = true;
        attack1ToIdle.exitTime = 0.9f;

        var attack3ToIdle = attack3.AddTransition(fighterIdle);
        attack3ToIdle.hasExitTime = true;
        attack3ToIdle.exitTime = 0.9f;

        // Heavy attack
        var idleToHeavy = fighterIdle.AddTransition(heavyAttack);
        idleToHeavy.AddCondition(AnimatorConditionMode.If, 0, "HeavyAttack");

        var heavyToIdle = heavyAttack.AddTransition(fighterIdle);
        heavyToIdle.hasExitTime = true;
        heavyToIdle.exitTime = 0.9f;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ Fighter Layer added!");
        EditorUtility.DisplayDialog("Success", "Fighter Layer added successfully!", "OK");
    }

    void AssignAvatarMasks()
    {
        if (controller.layers.Length < 2) return;

        var layers = controller.layers;

        // Archer Layer
        layers[1].avatarMask = upperBodyMask;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;

        controller.layers = layers;
    }

    AnimatorState CreateState(AnimatorStateMachine stateMachine, string name, AnimationClip clip, Vector3 position)
    {
        var state = stateMachine.AddState(name, position);
        if (clip != null)
        {
            state.motion = clip;
        }
        return state;
    }

    AnimatorState CreateBlendTreeState(AnimatorStateMachine stateMachine, string name, Vector3 position)
    {
        var state = stateMachine.AddState(name, position);

        var blendTree = new BlendTree
        {
            name = name,
            blendParameter = "Speed",
            blendType = BlendTreeType.Simple1D,
            hideFlags = HideFlags.HideInHierarchy
        };

        AssetDatabase.AddObjectToAsset(blendTree, controller);

        if (walkClip != null)
        {
            blendTree.AddChild(walkClip, 1.0f);
        }

        if (runClip != null)
        {
            blendTree.AddChild(runClip, 2.0f);
        }

        state.motion = blendTree;

        return state;
    }

    void AddParameter(string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name))
        {
            Debug.Log($"Parameter '{name}' already exists, skipping.");
            return;
        }

        controller.AddParameter(name, type);
    }
}

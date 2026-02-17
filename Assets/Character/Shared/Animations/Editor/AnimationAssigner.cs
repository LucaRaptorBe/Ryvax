using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

/// <summary>
/// Assigne automatiquement les animations aux états du controller en se basant sur les noms
/// Utilisation: Unity Menu > Character/Auto-Assign Animations
/// </summary>
public class AnimationAssigner : EditorWindow
{
    private AnimatorController controller;
    private string animationsPath = "Assets/Character/Shared/Animations/Clips";
    private bool includeClassSpecific = true;

    [MenuItem("Character/Auto-Assign Animations")]
    public static void ShowWindow()
    {
        GetWindow<AnimationAssigner>("Auto-Assign Animations");
    }

    void OnGUI()
    {
        GUILayout.Label("Animation Auto-Assigner", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // Load controller
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/Character/Shared/Animations/HumanoidAnimatorController.controller");

        EditorGUILayout.ObjectField("Controller", controller, typeof(AnimatorController), false);

        EditorGUILayout.Space();
        animationsPath = EditorGUILayout.TextField("Animations Path", animationsPath);
        includeClassSpecific = EditorGUILayout.Toggle("Include Class-Specific Anims", includeClassSpecific);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Ce script va automatiquement assigner les animations aux états du controller.\n\n" +
            "Convention de nommage attendue:\n" +
            "- Idle.anim → État 'Idle'\n" +
            "- Walk.anim → BlendTree 'Walk/Run'\n" +
            "- Run.anim → BlendTree 'Walk/Run'\n" +
            "- Jump.anim ou JumpStart.anim → État 'Jump'\n" +
            "- AimIdle.anim → État 'Aim Idle' (Archer)\n" +
            "- Shoot.anim → État 'Shoot' (Archer)\n" +
            "etc.",
            MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Auto-Assign All Animations", GUILayout.Height(40)))
        {
            AutoAssignAnimations();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Show Animation Mapping", GUILayout.Height(30)))
        {
            ShowAnimationMapping();
        }
    }

    void AutoAssignAnimations()
    {
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Error", "Controller not found!", "OK");
            return;
        }

        int assignedCount = 0;

        // Get all animation clips in the specified path
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip", new[] { animationsPath });

        if (includeClassSpecific)
        {
            // Also search in class-specific folders
            string[] archerGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Character/Class/Archer/Animations/Clips" });
            string[] mageGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Character/Class/Mage/Animations/Clips" });
            string[] fighterGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Character/Class/Fighter/Animations/Clips" });

            guids = guids.Concat(archerGuids).Concat(mageGuids).Concat(fighterGuids).ToArray();
        }

        Debug.Log($"Found {guids.Length} animation clips");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

            if (clip == null) continue;

            string clipName = clip.name;
            Debug.Log($"Processing: {clipName}");

            // Try to assign to matching state
            bool assigned = TryAssignToState(clipName, clip);
            if (assigned)
            {
                assignedCount++;
                Debug.Log($"✅ Assigned {clipName} to matching state");
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        string message = $"Successfully assigned {assignedCount} animations!\n\n" +
                        $"Total clips found: {guids.Length}\n" +
                        $"Assigned: {assignedCount}\n" +
                        $"Skipped: {guids.Length - assignedCount}";

        Debug.Log($"✅ {message}");
        EditorUtility.DisplayDialog("Success", message, "OK");
    }

    bool TryAssignToState(string clipName, AnimationClip clip)
    {
        // Normalize clip name for matching
        string normalized = clipName.ToLower().Replace("_", "").Replace(" ", "");

        // Search in all layers
        foreach (var layer in controller.layers)
        {
            if (TryAssignInStateMachine(layer.stateMachine, normalized, clip))
            {
                return true;
            }
        }

        return false;
    }

    bool TryAssignInStateMachine(AnimatorStateMachine stateMachine, string normalized, AnimationClip clip)
    {
        // Check all states in this state machine
        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            string stateName = state.name.ToLower().Replace("_", "").Replace(" ", "");

            // Direct name match
            if (stateName == normalized || stateName.Contains(normalized) || normalized.Contains(stateName))
            {
                if (state.motion == null)
                {
                    state.motion = clip;
                    Debug.Log($"  → Assigned to state: {state.name}");
                    return true;
                }
            }

            // BlendTree handling
            if (state.motion is BlendTree blendTree)
            {
                if (TryAssignToBlendTree(blendTree, normalized, clip))
                {
                    return true;
                }
            }
        }

        // Recursively check sub-state machines
        foreach (var childMachine in stateMachine.stateMachines)
        {
            if (TryAssignInStateMachine(childMachine.stateMachine, normalized, clip))
            {
                return true;
            }
        }

        return false;
    }

    bool TryAssignToBlendTree(BlendTree blendTree, string normalized, AnimationClip clip)
    {
        // Check if this clip should be added to the blend tree
        if (normalized.Contains("walk") || normalized.Contains("run"))
        {
            // Check if Walk/Run blend tree
            if (blendTree.name.ToLower().Contains("walk") || blendTree.name.ToLower().Contains("run"))
            {
                var children = blendTree.children;

                if (normalized.Contains("walk"))
                {
                    // Add walk at threshold 1.0
                    if (!children.Any(c => c.motion == clip))
                    {
                        blendTree.AddChild(clip, 1.0f);
                        Debug.Log($"  → Added {clip.name} to BlendTree '{blendTree.name}' at threshold 1.0");
                        return true;
                    }
                }
                else if (normalized.Contains("run"))
                {
                    // Add run at threshold 2.0
                    if (!children.Any(c => c.motion == clip))
                    {
                        blendTree.AddChild(clip, 2.0f);
                        Debug.Log($"  → Added {clip.name} to BlendTree '{blendTree.name}' at threshold 2.0");
                        return true;
                    }
                }
            }
        }

        return false;
    }

    void ShowAnimationMapping()
    {
        if (controller == null) return;

        Debug.Log("=== ANIMATION MAPPING ===");

        foreach (var layer in controller.layers)
        {
            Debug.Log($"\n--- Layer: {layer.name} ---");
            ShowStateMachineMapping(layer.stateMachine, "  ");
        }

        EditorUtility.DisplayDialog("Animation Mapping",
            "Animation mapping printed to Console.\n\nCheck the Console window for details.",
            "OK");
    }

    void ShowStateMachineMapping(AnimatorStateMachine stateMachine, string indent)
    {
        foreach (var childState in stateMachine.states)
        {
            var state = childState.state;
            string motionInfo = state.motion != null ? state.motion.name : "[NO ANIMATION]";

            if (state.motion is BlendTree blendTree)
            {
                Debug.Log($"{indent}{state.name} → BlendTree:");
                foreach (var child in blendTree.children)
                {
                    string childMotion = child.motion != null ? child.motion.name : "[EMPTY]";
                    Debug.Log($"{indent}  - {childMotion} (threshold: {child.threshold})");
                }
            }
            else
            {
                Debug.Log($"{indent}{state.name} → {motionInfo}");
            }
        }

        foreach (var childMachine in stateMachine.stateMachines)
        {
            Debug.Log($"{indent}[{childMachine.stateMachine.name}]");
            ShowStateMachineMapping(childMachine.stateMachine, indent + "  ");
        }
    }
}

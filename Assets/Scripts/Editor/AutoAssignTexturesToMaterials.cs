using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Script pour assigner automatiquement les textures aux matériaux
/// en se basant sur les noms de fichiers.
/// Menu: Tools > Auto Assign Textures to Materials
/// </summary>
public class AutoAssignTexturesToMaterials : EditorWindow
{
    private string materialsFolder = "Assets/Models/Character/Soul_Eater_Model/Materials";
    private string texturesFolder = "Assets/Models/Character/Soul_Eater_Model/Textures";

    [MenuItem("Tools/Auto Assign Textures to Materials")]
    public static void ShowWindow()
    {
        GetWindow<AutoAssignTexturesToMaterials>("Auto Assign Textures");
    }

    private void OnGUI()
    {
        GUILayout.Label("Auto Assign Textures to Materials", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "Ce script va automatiquement assigner les textures aux matériaux " +
            "en fonction des noms de fichiers.\n\n" +
            "Convention:\n" +
            "- nom_Diffuse.png → Base Map\n" +
            "- nom_Normal.png → Normal Map\n" +
            "- nom_Glow.png → Emission Map\n" +
            "- nom_Opacity.png → Alpha (si transparent)",
            MessageType.Info);

        GUILayout.Space(10);

        materialsFolder = EditorGUILayout.TextField("Materials Folder:", materialsFolder);
        texturesFolder = EditorGUILayout.TextField("Textures Folder:", texturesFolder);

        GUILayout.Space(10);

        if (GUILayout.Button("Assign Textures"))
        {
            AssignTextures();
        }
    }

    private void AssignTextures()
    {
        // Vérifier que les dossiers existent
        if (!AssetDatabase.IsValidFolder(materialsFolder))
        {
            EditorUtility.DisplayDialog("Erreur", $"Le dossier '{materialsFolder}' n'existe pas!", "OK");
            return;
        }

        if (!AssetDatabase.IsValidFolder(texturesFolder))
        {
            EditorUtility.DisplayDialog("Erreur", $"Le dossier '{texturesFolder}' n'existe pas!", "OK");
            return;
        }

        // Trouver tous les matériaux
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { materialsFolder });

        if (materialGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("Info", "Aucun matériau trouvé dans le dossier.", "OK");
            return;
        }

        int assignedCount = 0;
        int totalMaterials = materialGuids.Length;

        foreach (string guid in materialGuids)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (mat == null) continue;

            // Extraire le nom de base du matériau (sans extension ni suffixe _mi)
            string matName = Path.GetFileNameWithoutExtension(materialPath);
            string baseName = matName.Replace("_mi", "");

            Debug.Log($"[AutoAssign] Processing material: {matName}");

            bool assigned = false;

            // Chercher les textures correspondantes
            // 1. Base Map (Diffuse/Albedo)
            Texture2D diffuse = FindTexture(baseName, "_Diffuse");
            if (diffuse != null)
            {
                mat.SetTexture("_BaseMap", diffuse);
                Debug.Log($"  ✓ Base Map: {diffuse.name}");
                assigned = true;
            }

            // 2. Normal Map
            Texture2D normal = FindTexture(baseName, "_Normal");
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
                Debug.Log($"  ✓ Normal Map: {normal.name}");
                assigned = true;
            }

            // 3. Emission Map (Glow)
            Texture2D glow = FindTexture(baseName, "_Glow");
            if (glow != null)
            {
                mat.SetTexture("_EmissionMap", glow);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white);
                Debug.Log($"  ✓ Emission Map: {glow.name}");
                assigned = true;
            }

            // 4. Opacity Map (pour transparence)
            Texture2D opacity = FindTexture(baseName, "_Opacity");
            if (opacity != null)
            {
                // Pour URP, utiliser Alpha Clipping
                mat.SetFloat("_AlphaClip", 1);
                mat.SetTexture("_BaseMap", opacity); // Ou autre selon le setup
                Debug.Log($"  ✓ Opacity Map: {opacity.name}");
                assigned = true;
            }

            if (assigned)
            {
                EditorUtility.SetDirty(mat);
                assignedCount++;
            }
            else
            {
                Debug.LogWarning($"  ⚠️ Aucune texture trouvée pour {matName}");
            }
        }

        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Succès",
            $"Textures assignées!\n\n" +
            $"Matériaux traités: {assignedCount}/{totalMaterials}\n\n" +
            "Vérifiez la Console pour les détails.",
            "OK");
    }

    private Texture2D FindTexture(string baseName, string suffix)
    {
        // Chercher avec _mi dans le nom (convention Soul Eater)
        string searchName1 = baseName + "_mi" + suffix;
        string searchName2 = baseName + suffix;

        // Chercher dans le dossier textures
        string[] textureGuids = AssetDatabase.FindAssets($"{baseName} t:Texture2D", new[] { texturesFolder });

        foreach (string guid in textureGuids)
        {
            string texturePath = AssetDatabase.GUIDToAssetPath(guid);
            string textureName = Path.GetFileNameWithoutExtension(texturePath);

            // Vérifier si le nom correspond
            if (textureName == searchName1 || textureName == searchName2)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                return texture;
            }
        }

        // Chercher aussi les .tga.png (glow files)
        if (suffix == "_Glow")
        {
            foreach (string guid in textureGuids)
            {
                string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                if (texturePath.Contains("Glow.tga.png"))
                {
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    return texture;
                }
            }
        }

        return null;
    }
}

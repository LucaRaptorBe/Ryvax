using UnityEngine;
using MOBANet.Core;

/// <summary>
/// Active les logs de debug au démarrage pour diagnostiquer le netcode.
/// S'exécute automatiquement au lancement (pas besoin de l'ajouter à la scène).
/// TEMPORAIRE - à supprimer après debug.
/// </summary>
public class DebugActivator
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnGameStart()
    {
        Debug.Log("=== DEBUG ACTIVATOR STARTING ===");

        // CRITIQUE: Vérifier et corriger fixedDeltaTime si nécessaire
        const float EXPECTED_FIXED_DELTA = 1f / 30f; // 0.0333... pour 30Hz
        float currentFixedDelta = Time.fixedDeltaTime;
        float difference = Mathf.Abs(currentFixedDelta - EXPECTED_FIXED_DELTA);

        if (difference > 0.001f) // Tolérance 1ms
        {
            Debug.LogWarning($"⚠️ FixedDeltaTime INCORRECT: {currentFixedDelta * 1000f:F2}ms (expected: {EXPECTED_FIXED_DELTA * 1000f:F2}ms)");
            Debug.LogWarning("⚠️ AUTO-CORRECTING to 30Hz...");
            Time.fixedDeltaTime = EXPECTED_FIXED_DELTA;
            Debug.Log($"✓ FixedDeltaTime corrected to: {Time.fixedDeltaTime * 1000f:F2}ms");
        }
        else
        {
            Debug.Log($"✓ FixedDeltaTime OK: {currentFixedDelta * 1000f:F2}ms (30Hz)");
        }

        // Activer toutes les catégories importantes
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Network, true);
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Input, true);
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Prediction, true);
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Reconciliation, true);
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.AOI, true);

        // Ne pas activer Snapshot/Interpolation (trop verbeux)
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Snapshot, false);
        DebugLogger.SetCategoryEnabled(DebugLogger.Category.Interpolation, false);

        DebugLogger.PrintStatus();

        Debug.Log("=== DEBUG LOGS ACTIVATED ===");
    }
}

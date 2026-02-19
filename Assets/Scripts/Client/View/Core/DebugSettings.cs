// DebugSettings.cs - ScriptableObject pour configuration des logs dans l'Editor
// Optionnel: permet de modifier les flags depuis l'Inspector

using UnityEngine;

namespace MOBANet.Core
{
    /// <summary>
    /// Configuration des flags de debug.
    /// Créer via: Create > MOBANet > Debug Settings
    ///
    /// NOTE: Actuellement optionnel. Les flags peuvent aussi être modifiés
    /// programmatiquement via DebugLogger.SetCategoryEnabled().
    /// </summary>
    [CreateAssetMenu(fileName = "DebugSettings", menuName = "MOBANet/Debug Settings", order = 100)]
    public class DebugSettings : ScriptableObject
    {
        [Header("Sampling Global")]
        [Tooltip("1 = tous les logs, 60 = 1 log toutes les 60 frames")]
        [Range(1, 120)]
        public int globalSamplingRate = 1;

        [Header("Catégories de Logs")]
        [Tooltip("Messages réseau (commands, snapshots envoyés/reçus)")]
        public bool networkLogs = true;

        [Tooltip("Prédiction client-side et réconciliation")]
        public bool predictionLogs = true;

        [Tooltip("⚠️ OFF par défaut - Simulation de jeu (boucle à 60Hz)")]
        public bool gameSimLogs = false;

        [Tooltip("⚠️ OFF par défaut - Génération de snapshots (2000+ logs/sec à 100 joueurs!)")]
        public bool snapshotLogs = false;

        [Tooltip("Area of Interest (AOI) enter/leave")]
        public bool aoiLogs = true;

        [Tooltip("Réconciliation client/serveur (corrections)")]
        public bool reconciliationLogs = true;

        [Tooltip("⚠️ OFF par défaut - Interpolation des remote players (chaque frame)")]
        public bool interpolationLogs = false;

        [Tooltip("Gestion des inputs (WASD, click-to-move)")]
        public bool inputLogs = true;

        /// <summary>
        /// Applique ces settings au DebugLogger.
        /// Appeler au démarrage ou quand les settings changent.
        /// </summary>
        public void Apply()
        {
            DebugLogger.SetGlobalSampling(globalSamplingRate);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Network, networkLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Prediction, predictionLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.GameSim, gameSimLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Snapshot, snapshotLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.AOI, aoiLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Reconciliation, reconciliationLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Interpolation, interpolationLogs);
            DebugLogger.SetCategoryEnabled(DebugLogger.Category.Input, inputLogs);

            Debug.Log("[DebugSettings] Appliqué au DebugLogger");
            DebugLogger.PrintStatus();
        }

        private void OnValidate()
        {
            // Auto-apply quand modifié dans l'Inspector (Editor only)
            #if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Apply();
            }
            #endif
        }
    }
}

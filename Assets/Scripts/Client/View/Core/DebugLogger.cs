// DebugLogger.cs - Performant debug logging system
// Logs are compiled out in Release builds (zero overhead)

using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace MOBANet.Core
{
    /// <summary>
    /// Système de logging performant avec flags par catégorie.
    /// Les logs sont compilés UNIQUEMENT en mode DEVELOPMENT_BUILD.
    /// En mode Release: zéro overhead, zéro allocation.
    ///
    /// Usage:
    ///   DebugLogger.Log(Category.Network, "Message");
    ///   DebugLogger.LogThrottled(Category.Snapshot, "High-frequency message", frameInterval: 60);
    /// </summary>
    public static class DebugLogger
    {
        /// <summary>
        /// Catégories de logs disponibles.
        /// </summary>
        public enum Category
        {
            Network,        // Messages réseau (commands, snapshots)
            Prediction,     // Prédiction client-side
            GameSim,        // Simulation de jeu
            Snapshot,       // Génération de snapshots (très fréquent!)
            AOI,            // Area of Interest
            Reconciliation, // Réconciliation client/serveur
            Interpolation,  // Interpolation pour remote players
            Input           // Gestion des inputs
        }

        // ⚠️ CRITICAL: Flags par défaut
        // Snapshot et GameSim sont OFF par défaut car ils génèrent BEAUCOUP de logs
        private static readonly Dictionary<Category, bool> _flags = new()
        {
            { Category.Network, true },
            { Category.Prediction, true },
            { Category.GameSim, false },        // OFF: boucle de simulation
            { Category.Snapshot, false },       // OFF: génération snapshots (2000+ logs/sec!)
            { Category.AOI, true },
            { Category.Reconciliation, true },
            { Category.Interpolation, false },  // OFF: interpolation chaque frame
            { Category.Input, true }
        };

        // ⚠️ CRITIQUE: Sampling global pour éviter de noyer le diagnostic
        // Permet de limiter TOUS les logs à 1/N frames même si category=true
        // Utile pendant les phases de développement pour voir les patterns sans saturer
        private static int _globalSamplingRate = 1; // 1 = tous les logs, 60 = 1 log toutes les 60 frames

        /// <summary>
        /// Définit le taux de sampling global (1 = tous les logs, 60 = 1/60 frames).
        /// Appliqué AVANT les flags de catégorie.
        /// Utile pour diagnostiquer sans être noyé de logs.
        /// </summary>
        public static void SetGlobalSampling(int frameInterval)
        {
            _globalSamplingRate = Mathf.Max(1, frameInterval);
        }

        /// <summary>
        /// Active ou désactive une catégorie de logs.
        /// </summary>
        public static void SetCategoryEnabled(Category category, bool enabled)
        {
            if (_flags.ContainsKey(category))
            {
                _flags[category] = enabled;
            }
        }

        /// <summary>
        /// Vérifie si une catégorie est activée.
        /// </summary>
        public static bool IsCategoryEnabled(Category category)
        {
            return _flags.TryGetValue(category, out bool enabled) && enabled;
        }

        /// <summary>
        /// Log un message si la catégorie est activée.
        /// Compilé UNIQUEMENT en DEVELOPMENT_BUILD (zéro overhead en Release).
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Log(Category category, string message)
        {
            // Sampling global
            if (_globalSamplingRate > 1 && Time.frameCount % _globalSamplingRate != 0)
                return;

            // Flag de catégorie
            if (_flags.TryGetValue(category, out bool enabled) && enabled)
            {
                UnityEngine.Debug.Log($"[{category}] {message}");
            }
        }

        /// <summary>
        /// Log un message avec throttling (1 fois toutes les N frames).
        /// Utile pour les logs à haute fréquence (ex: dans une boucle).
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        public static void LogThrottled(Category category, string message, int frameInterval = 60)
        {
            if (Time.frameCount % frameInterval != 0)
                return;

            Log(category, message);
        }

        /// <summary>
        /// Log un warning (toujours affiché, même en Release si UNITY_ASSERTIONS).
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_ASSERTIONS")]
        public static void LogWarning(Category category, string message)
        {
            if (_flags.TryGetValue(category, out bool enabled) && enabled)
            {
                UnityEngine.Debug.LogWarning($"[{category}] {message}");
            }
        }

        /// <summary>
        /// Log une erreur (toujours affiché).
        /// </summary>
        public static void LogError(Category category, string message)
        {
            UnityEngine.Debug.LogError($"[{category}] {message}");
        }

        /// <summary>
        /// Affiche l'état actuel des flags (debug).
        /// </summary>
        [Conditional("DEVELOPMENT_BUILD")]
        public static void PrintStatus()
        {
            UnityEngine.Debug.Log("=== DebugLogger Status ===");
            UnityEngine.Debug.Log($"Global Sampling: 1/{_globalSamplingRate} frames");
            foreach (var kvp in _flags)
            {
                UnityEngine.Debug.Log($"  {kvp.Key}: {(kvp.Value ? "ON" : "OFF")}");
            }
        }
    }
}

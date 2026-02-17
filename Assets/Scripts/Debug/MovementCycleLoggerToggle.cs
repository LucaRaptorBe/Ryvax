// MovementCycleLoggerToggle.cs - Inspector toggle for movement cycle logging (V4.0 Pure LoL)
// Attach to any GameObject to control logging via Inspector
//
// V4.0 Changes:
// - Removed LogPrediction, LogAbsorb, LogDecay (no longer used in Pure LoL model)

using UnityEngine;

namespace MOBANet.Diagnostics
{
    /// <summary>
    /// Inspector toggle for MovementCycleLogger (V4.0 Pure LoL).
    /// Attach to any GameObject to control logging settings at runtime.
    /// </summary>
    public class MovementCycleLoggerToggle : MonoBehaviour
    {
        [Header("Master Toggle")]
        [Tooltip("Enable/disable all movement cycle logging")]
        public bool EnableLogging = true;

        [Header("Log Categories")]
        [Tooltip("Log input start/stop events")]
        public bool LogInput = true;

        [Tooltip("Log intent creation (MoveTo/Stop)")]
        public bool LogIntent = true;

        [Tooltip("Log network send")]
        public bool LogNetwork = true;

        [Tooltip("Log snapshot received")]
        public bool LogSnapshot = true;

        [Tooltip("Log render position (throttled)")]
        public bool LogRender = false;

        void Update()
        {
            // Sync Inspector values to static logger
            MovementCycleLogger.Enabled = EnableLogging;
            MovementCycleLogger.LogInput = LogInput;
            MovementCycleLogger.LogIntent = LogIntent;
            MovementCycleLogger.LogNetwork = LogNetwork;
            MovementCycleLogger.LogSnapshot = LogSnapshot;
            MovementCycleLogger.LogRender = LogRender;
        }
    }
}

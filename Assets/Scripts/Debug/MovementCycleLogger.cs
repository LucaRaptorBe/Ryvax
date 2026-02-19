// MovementCycleLogger.cs - Logs the movement cycle for debugging (V4.0 Pure LoL)
// Tracks: Input → Intent → Server → Snapshot → Render
// V4.0: Removed prediction/absorb/decay logs (no longer used in Pure LoL model)

using UnityEngine;
using MOBANet.NetAdapter.Messages;

namespace MOBANet.Diagnostics
{
    /// <summary>
    /// Logs the movement cycle with timestamps (V4.0 Pure LoL).
    /// Enable via Inspector to see detailed flow.
    ///
    /// V4.0 Changes:
    /// - Removed LogPrediction (no local prediction)
    /// - Removed LogAbsorb (no offset absorption)
    /// - Removed LogDecay (no offset decay)
    /// - Simplified to: Input → Intent → Snapshot → Render
    /// </summary>
    public static class MovementCycleLogger
    {
        // Enable/disable all logging
        public static bool Enabled = true;

        // Log categories
        public static bool LogInput = true;
        public static bool LogIntent = true;
        public static bool LogNetwork = true;
        public static bool LogSnapshot = true;
        public static bool LogRender = false;  // Very spammy, off by default

        // Track cycle start time
        private static float _cycleStartTime;

        private static string Timestamp => $"[{Time.time:F3}]";
        private static string CycleTime => _cycleStartTime > 0 ? $"+{(Time.time - _cycleStartTime) * 1000:F0}ms" : "";

        /// <summary>
        /// Log when movement key is first pressed.
        /// </summary>
        public static void LogInputStart(Vector2 dir)
        {
            if (!Enabled || !LogInput) return;

            _cycleStartTime = Time.time;

            Debug.Log($"{Timestamp} [INPUT START] dir={dir}");
        }

        /// <summary>
        /// Log when movement key is released.
        /// </summary>
        public static void LogInputStop()
        {
            if (!Enabled || !LogInput) return;

            // Debug.Log($"{Timestamp} [INPUT STOP] {CycleTime}");
        }

        /// <summary>
        /// Log when an intent is created by IntentBuilder.
        /// </summary>
        public static void LogIntentCreated(string intentType, uint seqId, Vector3? payload = null, bool isDirection = false)
        {
            if (!Enabled || !LogIntent) return;

            string payloadStr = "";
            if (payload.HasValue)
            {
                if (isDirection)
                    payloadStr = $" dir=({payload.Value.x:F2},{payload.Value.z:F2})";
                else
                    payloadStr = $" target=({payload.Value.x:F1},{payload.Value.z:F1})";
            }
            // Debug.Log($"{Timestamp} [INTENT] {CycleTime} {intentType} seq={seqId}{payloadStr}");
        }

        /// <summary>
        /// Log when intent is sent to server.
        /// </summary>
        public static void LogIntentSent(uint movementSeq, PacketIntentType intentType, short payload0, short payload1)
        {
            if (!Enabled || !LogNetwork) return;

            string payloadStr = intentType switch
            {
                PacketIntentType.MoveDir => $"dir=({payload0 / 127f:F2},{payload1 / 127f:F2})",
                PacketIntentType.MoveTo => $"target=({payload0 / 10f:F1},{payload1 / 10f:F1})",
                PacketIntentType.Follow => $"entity={((uint)(ushort)payload0 | ((uint)(ushort)payload1 << 16))}",
                PacketIntentType.Stop => "stop",
                _ => "none"
            };
            // Debug.Log($"{Timestamp} [SEND] {CycleTime} {intentType} seq={movementSeq} {payloadStr}");
        }

        /// <summary>
        /// Log when snapshot is received from server.
        /// </summary>
        public static void LogSnapshotReceived(uint serverTick, Vector3 serverPos, Vector3 serverVel, float serverTime)
        {
            if (!Enabled || !LogSnapshot) return;

            //Debug.Log($"{Timestamp} [SNAPSHOT] {CycleTime} tick={serverTick} pos={serverPos:F2} vel={serverVel:F2}");
        }

        /// <summary>
        /// Log final visual position (throttled).
        /// </summary>
        public static void LogRenderPosition(Vector3 visualPos)
        {
            if (!Enabled || !LogRender) return;

            // Only log occasionally to avoid spam
            if (Time.frameCount % 60 != 0) return;

            // Debug.Log($"{Timestamp} [RENDER] pos={visualPos:F2}");
        }

        /// <summary>
        /// Reset cycle tracking.
        /// </summary>
        public static void ResetCycle()
        {
            _cycleStartTime = 0;
        }

        // =====================================================
        // DEPRECATED METHODS (V4.0 Pure LoL - no offset system)
        // =====================================================

        [System.Obsolete("V4.0: No prediction in Pure LoL model")]
        public static void LogPredictionApplied(Vector3 delta, Vector3 offsetBefore, Vector3 offsetAfter, float speed) { }

        [System.Obsolete("V4.0: No absorption in Pure LoL model")]
        public static void LogAbsorbEvent(Vector3 oldBase, Vector3 newBase, Vector3 offsetBefore, Vector3 offsetAfter) { }

        [System.Obsolete("V4.0: No decay in Pure LoL model")]
        public static void LogDecayUpdate(Vector3 offsetBefore, Vector3 offsetAfter, float gap, string tier, float speed) { }

        [System.Obsolete("V4.0: No snap in Pure LoL model")]
        public static void LogSnap(float gapBefore, string reason) { }

        [System.Obsolete("V4.0: Use LogRenderPosition(Vector3) instead")]
        public static void LogRenderPosition(Vector3 basePos, Vector3 offset, Vector3 visualPos)
        {
            LogRenderPosition(visualPos);
        }

        [System.Obsolete("V4.0: No cycle summary needed")]
        public static void LogCycleSummary(float totalTime, Vector3 finalBasePos, Vector3 finalOffset) { }
    }
}

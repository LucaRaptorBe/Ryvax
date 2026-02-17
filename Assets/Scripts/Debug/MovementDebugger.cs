// MovementDebugger.cs - Triggered logging system for movement residual diagnosis
// Activates on key release and logs for 200ms (~6 ticks at 30Hz)

using UnityEngine;

namespace MOBANet.Diagnostics
{
    /// <summary>
    /// Triggered logging system for diagnosing residual movement.
    /// Activates on key release and logs for 200ms (~6 ticks).
    /// </summary>
    public static class MovementDebugger
    {
        private const float LOG_DURATION = 0.2f; // 200ms = ~6 ticks at 30Hz

        public static bool IsLogging { get; private set; }
        public static float LogEndTime { get; private set; }
        public static uint ReleaseSeq { get; private set; }
        public static uint ReleaseTick { get; private set; }

        /// <summary>
        /// Trigger logging at the moment of release.
        /// </summary>
        public static void TriggerOnRelease(uint seq, uint tick)
        {
            IsLogging = true;
            LogEndTime = Time.time + LOG_DURATION;
            ReleaseSeq = seq;
            ReleaseTick = tick;
            UnityEngine.Debug.Log($"=== MOVEMENT DEBUG START === seq={seq} tick={tick} time={Time.time:F3} ===");
        }

        /// <summary>
        /// Conditional log (only if in logging period).
        /// </summary>
        public static void Log(string msg)
        {
            if (IsLogging && Time.time <= LogEndTime)
            {
                UnityEngine.Debug.Log(msg);
            }
        }

        /// <summary>
        /// Check if logging period has expired.
        /// Call every frame/tick.
        /// </summary>
        public static void CheckExpiry()
        {
            if (IsLogging && Time.time > LogEndTime)
            {
                UnityEngine.Debug.Log($"=== MOVEMENT DEBUG END === duration={LOG_DURATION}s ===");
                IsLogging = false;
            }
        }
    }
}

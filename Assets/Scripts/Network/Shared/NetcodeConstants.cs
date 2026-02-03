// NetcodeConstants.cs - Centralized netcode configuration (V5.0 Simplified)
// All timing parameters are CALCULATED from base values, not magic numbers

using System;

namespace MOBANet.Shared
{
    /// <summary>
    /// Centralized netcode constants.
    /// V5.0: Simplified - removed interpolation/prediction constants.
    /// </summary>
    public static class NetcodeConstants
    {
        // ============================================
        // BASE CONSTANTS (the only values you tune)
        // ============================================

        /// <summary>
        /// Simulation tick rate in Hz.
        /// Higher = more responsive but more CPU/bandwidth.
        /// Standard: 20-30 for MOBA, 60-128 for FPS.
        /// </summary>
        public const int TICK_RATE = 60;

        /// <summary>
        /// Snapshot send rate in Hz.
        /// For simplicity, we use the same rate as tick rate.
        /// </summary>
        public const int SNAPSHOT_RATE = TICK_RATE;

        /// <summary>
        /// Player movement speed in units/second.
        /// </summary>
        public const float PLAYER_SPEED = 8f;

        // ============================================
        // INPUT TRANSPORT (UDP with redundancy)
        // ============================================

        /// <summary>
        /// Number of recent commands to include in each input packet.
        /// Provides redundancy against packet loss.
        /// 3 = tolerates 2 consecutive lost packets.
        /// </summary>
        public const int INPUT_REDUNDANCY_COUNT = 3;

        /// <summary>
        /// Maximum commands to buffer for redundancy.
        /// Should be >= INPUT_REDUNDANCY_COUNT.
        /// </summary>
        public const int INPUT_BUFFER_SIZE = 8;

        // ============================================
        // DERIVED CONSTANTS (calculated automatically)
        // ============================================

        /// <summary>
        /// Time between simulation ticks in seconds.
        /// </summary>
        public const float TICK_DELTA = 1f / TICK_RATE;

        /// <summary>
        /// Time between simulation ticks in milliseconds.
        /// </summary>
        public const float TICK_DELTA_MS = TICK_DELTA * 1000f;

        /// <summary>
        /// Time between snapshots in seconds.
        /// </summary>
        public const float SNAPSHOT_INTERVAL = 1f / SNAPSHOT_RATE;

        /// <summary>
        /// Time between snapshots in milliseconds.
        /// </summary>
        public const float SNAPSHOT_INTERVAL_MS = SNAPSHOT_INTERVAL * 1000f;

        /// <summary>
        /// Distance a player moves per tick.
        /// At 8 units/sec and 60Hz = 0.133 units/tick.
        /// </summary>
        public const float DISTANCE_PER_TICK = PLAYER_SPEED * TICK_DELTA;

        // ============================================
        // VISUAL SMOOTHING
        // ============================================

        /// <summary>
        /// Smoothing speed for visual position (1/second).
        /// Higher = snappier, Lower = smoother but can feel floaty.
        /// 15-20 is a good balance for MOBAs.
        /// </summary>
        public const float VISUAL_SMOOTHING_SPEED = 18f;

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Get a formatted summary of all constants for debugging.
        /// </summary>
        public static string GetSummary()
        {
            return $@"=== Netcode Constants (V5.0) ===
Tick Rate: {TICK_RATE} Hz ({TICK_DELTA_MS:F1}ms/tick)
Snapshot Rate: {SNAPSHOT_RATE} Hz ({SNAPSHOT_INTERVAL_MS:F1}ms/snapshot)
Player Speed: {PLAYER_SPEED} units/sec ({DISTANCE_PER_TICK:F3} units/tick)";
        }
    }
}

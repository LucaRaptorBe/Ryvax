// NetcodeConstants.cs - Centralized netcode configuration
// All timing parameters are CALCULATED from base values, not magic numbers
// Reference: Industry standards for MOBA netcode (LoL-like games)

using System;

namespace MOBANet.Shared
{
    /// <summary>
    /// Centralized netcode constants.
    /// All derived values are calculated from base constants.
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
        public const int TICK_RATE = 30;

        /// <summary>
        /// Snapshot send rate in Hz.
        /// For simplicity, we use the same rate as tick rate.
        /// This ensures one snapshot per tick, simplifying interpolation.
        /// </summary>
        public const int SNAPSHOT_RATE = TICK_RATE;

        /// <summary>
        /// Player movement speed in units/second.
        /// Used to calculate reconciliation thresholds.
        /// </summary>
        public const float PLAYER_SPEED = 8f;

        /// <summary>
        /// Number of snapshot intervals to buffer for interpolation.
        /// Industry standard: 2 ticks (cl_interp_ratio = 2 in Source engine).
        /// At 30Hz: 2 ticks = 66ms. Protects against 1 dropped packet.
        /// Increase to 3 for connections with packet loss.
        /// Reference: Valve Source Multiplayer Networking, Glenn Fiedler.
        /// </summary>
        public const int INTERPOLATION_BUFFER_TICKS = 2;

        /// <summary>
        /// PLL gain for interpolation time asservissement.
        /// Contrôle la vitesse de correction de l'offset d'interpolation.
        /// Valeurs typiques: 0.1-0.5 (plus bas = plus smooth, plus haut = correction plus rapide)
        /// </summary>
        public const float INTERPOLATION_PLL_GAIN = 0.2f;

        /// <summary>
        /// Playback rate clamp min (pour éviter de freeze)
        /// </summary>
        public const float INTERPOLATION_PLAYBACK_MIN = 0.9f;

        /// <summary>
        /// Playback rate clamp max (pour éviter fast-forward trop rapide)
        /// </summary>
        public const float INTERPOLATION_PLAYBACK_MAX = 1.1f;

        /// <summary>
        /// Soft reconciliation threshold in ticks.
        /// Below this: smooth correction (lerp toward server).
        /// This catches jitter/quantization errors without snapping.
        /// </summary>
        public const int SOFT_RECONCILE_TICKS = 1;

        /// <summary>
        /// Hard reconciliation threshold in ticks.
        /// Above this: full reconciliation (snap + replay).
        /// This catches real desyncs that need immediate correction.
        /// </summary>
        public const int HARD_RECONCILE_TICKS = 4;

        /// <summary>
        /// Exponential smoothing decay rate for position.
        /// Used in formula: lerp(current, target, 1 - exp(-k * dt))
        /// Higher = faster convergence. 20+ recommended for 30Hz tick rate.
        /// </summary>
        public const float POSITION_SMOOTHING_K = 20f;

        /// <summary>
        /// Exponential smoothing decay rate for rotation.
        /// Slightly slower than position for visual comfort.
        /// </summary>
        public const float ROTATION_SMOOTHING_K = 10f;

        // ============================================
        // CLOCK SYNCHRONIZATION
        // ============================================
        // Professional clock sync: client adjusts tick rate to keep
        // a target number of inputs "in flight" (sent but not acked).
        // Reference: Overwatch GDC 2017, Valorant netcode talks.

        /// <summary>
        /// Target inputs in flight (sent but not yet acknowledged).
        /// This provides jitter protection while minimizing latency.
        /// 2 inputs = ~66ms buffer at 30Hz. Industry standard.
        /// </summary>
        public const int TARGET_INPUTS_IN_FLIGHT = 2;

        /// <summary>
        /// Maximum deviation from target before adjusting tick rate.
        /// Allows some variance without constant adjustment.
        /// </summary>
        public const int INPUT_BUFFER_TOLERANCE = 1;

        /// <summary>
        /// How aggressively to adjust tick rate (0-1).
        /// Higher = faster convergence but more jitter.
        /// 0.1 = smooth adjustment over ~10 snapshots.
        /// </summary>
        public const float CLOCK_SYNC_AGGRESSION = 0.1f;

        /// <summary>
        /// Maximum tick rate adjustment factor.
        /// Prevents runaway adjustment. 0.2 = ±20% max.
        /// </summary>
        public const float MAX_TICK_RATE_ADJUSTMENT = 0.2f;

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
        /// Interpolation delay in seconds.
        /// Calculated as: INTERPOLATION_BUFFER_TICKS × snapshot interval.
        /// At 30Hz with 2 ticks = 66ms (industry standard).
        /// </summary>
        public const float INTERPOLATION_DELAY = INTERPOLATION_BUFFER_TICKS * SNAPSHOT_INTERVAL;

        /// <summary>
        /// Interpolation delay in milliseconds.
        /// </summary>
        public const float INTERPOLATION_DELAY_MS = INTERPOLATION_DELAY * 1000f;

        /// <summary>
        /// Distance a player moves per tick.
        /// At 8 units/sec and 30Hz = 0.267 units/tick.
        /// </summary>
        public const float DISTANCE_PER_TICK = PLAYER_SPEED * TICK_DELTA;

        /// <summary>
        /// Soft reconciliation threshold in world units.
        /// Errors below this are smoothly corrected without full reconciliation.
        /// At 30Hz, 8 speed: 1 tick = 0.267 units.
        /// </summary>
        public const float SOFT_RECONCILE_THRESHOLD = SOFT_RECONCILE_TICKS * DISTANCE_PER_TICK;

        /// <summary>
        /// Hard reconciliation threshold in world units.
        /// Errors above this trigger full server snap + input replay.
        /// At 30Hz, 8 speed: 4 ticks = 1.07 units.
        /// </summary>
        public const float HARD_RECONCILE_THRESHOLD = HARD_RECONCILE_TICKS * DISTANCE_PER_TICK;

        // Legacy alias for compatibility
        public const float RECONCILE_THRESHOLD = HARD_RECONCILE_THRESHOLD;

        /// <summary>
        /// Remote player soft correction threshold (plus permissif que local).
        /// Les corrections sur remote players sont moins visibles car périphériques.
        /// ~2 ticks de tolérance pour absorber le jitter réseau.
        /// </summary>
        public const float REMOTE_SOFT_THRESHOLD = 0.5f;

        /// <summary>
        /// Remote player hard snap threshold (plus permissif que local).
        /// Les snaps sur remote players sont moins critiques que sur local player.
        /// ~7 ticks de tolérance avant téléportation.
        /// </summary>
        public const float REMOTE_HARD_THRESHOLD = 2.0f;

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Framerate-independent exponential smoothing.
        /// Use instead of lerp(a, b, t * dt) which is framerate-dependent.
        /// </summary>
        /// <param name="current">Current value</param>
        /// <param name="target">Target value</param>
        /// <param name="k">Decay rate (higher = faster)</param>
        /// <param name="dt">Delta time</param>
        /// <returns>Smoothed value</returns>
        public static float ExpSmooth(float current, float target, float k, float dt)
        {
            // Formula: lerp(current, target, 1 - exp(-k * dt))
            // This is framerate-independent: same result regardless of dt
            float t = 1f - (float)Math.Exp(-k * dt);
            return current + (target - current) * t;
        }

        /// <summary>
        /// Get a formatted summary of all constants for debugging.
        /// </summary>
        public static string GetSummary()
        {
            return $@"=== Netcode Constants ===
Tick Rate: {TICK_RATE} Hz ({TICK_DELTA_MS:F1}ms/tick)
Snapshot Rate: {SNAPSHOT_RATE} Hz ({SNAPSHOT_INTERVAL_MS:F1}ms/snapshot)
Player Speed: {PLAYER_SPEED} units/sec ({DISTANCE_PER_TICK:F3} units/tick)

Interpolation Delay: {INTERPOLATION_DELAY_MS:F0}ms ({INTERPOLATION_BUFFER_TICKS} snapshots)
Soft Reconcile: {SOFT_RECONCILE_THRESHOLD:F2} units ({SOFT_RECONCILE_TICKS} tick)
Hard Reconcile: {HARD_RECONCILE_THRESHOLD:F2} units ({HARD_RECONCILE_TICKS} ticks)
Position Smoothing K: {POSITION_SMOOTHING_K:F1}
Rotation Smoothing K: {ROTATION_SMOOTHING_K:F1}";
        }
    }
}

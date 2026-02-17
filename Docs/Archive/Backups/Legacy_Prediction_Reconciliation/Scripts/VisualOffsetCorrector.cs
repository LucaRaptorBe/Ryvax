// VisualOffsetCorrector.cs - DEPRECATED (V4.0)
//
// ┌─────────────────────────────────────────────────────────────────┐
// │  DEPRECATED: This class is no longer used in V4.0 Pure LoL.    │
// │  Kept for reference only. Will be removed in future version.   │
// │                                                                 │
// │  V4.0 Pure LoL: visualPos = basePos (no offset system)         │
// │  See VisualPositionManager.cs for the new simplified model.    │
// └─────────────────────────────────────────────────────────────────┘
//
// Historical purpose:
// - Used with prediction model where offset absorbed basePos jumps
// - ApplyLocalPrediction pushed offset forward (removed in V3.0)
// - AbsorbBasePosJump absorbed server confirmation (removed in V4.0)
// - Decay corrected offset towards zero (removed in V4.0)

using System;
using UnityEngine;
using MOBANet.Diagnostics;

namespace MOBANet.Client.Prediction
{
    [System.Obsolete("V4.0 Pure LoL: No offset system. Use VisualPositionManager directly.")]
    /// <summary>
    /// Corrects the visual offset towards zero.
    ///
    /// Key principle: We correct the OFFSET, not the position.
    /// visualPos = basePos + visualOffset
    ///
    /// When basePos jumps (new snapshot), we absorb the jump into the offset
    /// to maintain visual continuity. Then we smoothly correct the offset to zero.
    ///
    /// Correction tiers based on gap (distance from visual to base):
    /// - gap &lt; smallGap: Smooth correction (k=6, ~115ms half-life)
    /// - gap &lt; largeGap: Accelerated correction (k=15, ~46ms half-life)
    /// - gap >= largeGap: Snap (offset = 0 immediately, no purge)
    ///
    /// Thresholds are dynamic:
    /// - smallGap = max(20, speed * 0.06)
    /// - largeGap = clamp(max(80, speed * max(0.25, buffer*2)), 80, 250)
    /// </summary>
    public class VisualOffsetCorrector
    {
        // Base thresholds in world units
        private const float BASE_SMALL_GAP = 20f;
        private const float BASE_LARGE_GAP = 80f;
        private const float MAX_LARGE_GAP = 250f;  // V2.2: Cap to prevent masking real desync

        // Time-based threshold factors (in seconds of movement)
        private const float SMALL_GAP_TIME = 0.06f;   // ~2 ticks @ 30Hz
        private const float LARGE_GAP_TIME = 0.25f;   // ~7-8 ticks

        // Correction rates (exponential decay)
        // k=6: half-life = ln(2)/6 ~ 115ms
        // k=15: half-life = ln(2)/15 ~ 46ms
        private const float SMOOTH_K = 6f;
        private const float ACCEL_K = 15f;

        // Epsilon to avoid micro-oscillations
        private const float EPSILON = 0.5f;

        // State
        private Vector3 _visualOffset;
        private bool _isImmobilized;
        private float _adaptiveBuffer;

        /// <summary>
        /// Current visual offset (added to basePos to get visualPos).
        /// </summary>
        public Vector3 VisualOffset => _visualOffset;

        /// <summary>
        /// Whether entity is currently under immobilizing CC.
        /// IntentBuilder should check this to suppress movement intents.
        /// </summary>
        public bool IsImmobilized => _isImmobilized;

        /// <summary>
        /// Event fired when entire interpolation buffer should be purged.
        /// Used by SnapHard.
        /// </summary>
        public event Action OnPurgeInterpolationBuffer;

        /// <summary>
        /// Event fired when snapshots before a specific time should be purged.
        /// Used after discontinuity detection.
        /// Parameter: time before which to purge.
        /// </summary>
        public event Action<float> OnPurgeSnapshotsBefore;

        /// <summary>
        /// Set the adaptive buffer value (used for dynamic largeGap).
        /// Call this when TimeSync updates.
        /// Buffer must be in SECONDS (not milliseconds).
        /// </summary>
        public void SetAdaptiveBuffer(float bufferSeconds)
        {
            _adaptiveBuffer = bufferSeconds;
        }

        /// <summary>
        /// Absorb a basePos jump into the offset for visual continuity.
        /// Formula: visualOffset += (oldBase - newBase)
        ///
        /// This maintains visualPos continuity when basePos changes:
        ///   Before: visualPos = oldBase + oldOffset
        ///   After:  visualPos = newBase + (oldOffset + (oldBase - newBase))
        ///                     = newBase + oldOffset + oldBase - newBase
        ///                     = oldBase + oldOffset (same as before!)
        ///
        /// NOTE: Do NOT call this if discontinuity (teleport/blink) - use SnapHard instead.
        /// </summary>
        public void AbsorbBasePosJump(Vector3 oldBase, Vector3 newBase)
        {
            if (_isImmobilized) return;
            Vector3 offsetBefore = _visualOffset;
            _visualOffset += oldBase - newBase;
            MovementCycleLogger.LogAbsorbEvent(oldBase, newBase, offsetBefore, _visualOffset);
        }

        /// <summary>
        /// Soft snap: set offset to zero immediately.
        /// Used when gap is too large (visual correction only).
        /// Does NOT purge the interpolation buffer.
        /// </summary>
        public void Snap()
        {
            float gapBefore = _visualOffset.magnitude;
            _visualOffset = Vector3.zero;
            MovementCycleLogger.LogSnap(gapBefore, "gap >= largeGap");
        }

        /// <summary>
        /// Hard snap: set offset to zero + purge interpolation buffer.
        /// Used for teleport, blink, state reset (discontinuities).
        /// </summary>
        /// <param name="discontinuityTime">Time of the discontinuity.
        /// If > 0, purges snapshots before this time instead of clearing all.</param>
        public void SnapHard(float discontinuityTime = 0f)
        {
            _visualOffset = Vector3.zero;

            if (discontinuityTime > 0f)
            {
                // Selective purge: only snapshots before the discontinuity
                OnPurgeSnapshotsBefore?.Invoke(discontinuityTime);
            }
            else
            {
                // Full purge
                OnPurgeInterpolationBuffer?.Invoke();
            }
        }

        /// <summary>
        /// Calculate dynamic thresholds based on speed and buffer.
        /// V2.2: Added cap to largeGap (MAX_LARGE_GAP).
        /// </summary>
        private void GetDynamicThresholds(float speed, out float smallGap, out float largeGap)
        {
            smallGap = Mathf.Max(BASE_SMALL_GAP, speed * SMALL_GAP_TIME);

            // largeGap includes adaptive buffer consideration
            // If buffer = 120ms (0.12s), we accept more gap before snap
            // V2.2: Cap at MAX_LARGE_GAP to prevent masking real desync at high speed
            float bufferFactor = Mathf.Max(LARGE_GAP_TIME, _adaptiveBuffer * 2.0f);
            float uncappedLargeGap = Mathf.Max(BASE_LARGE_GAP, speed * bufferFactor);
            largeGap = Mathf.Clamp(uncappedLargeGap, BASE_LARGE_GAP, MAX_LARGE_GAP);
        }

        /// <summary>
        /// Get the current large gap threshold (for IntentBuilder).
        /// </summary>
        public float GetLargeGapThreshold(float speed)
        {
            GetDynamicThresholds(speed, out _, out float largeGap);
            return largeGap;
        }

        /// <summary>
        /// Handle immobilizing CC (root/stun).
        /// During CC: offset=0, movement locked
        /// End CC: unlock with offset=0
        /// </summary>
        public void OnImmobilizeCC(bool isImmobilized)
        {
            if (isImmobilized)
            {
                // Start or during CC: hard snap
                _visualOffset = Vector3.zero;
                _isImmobilized = true;
            }
            else if (_isImmobilized)
            {
                // End CC: unlock and reset
                _isImmobilized = false;
                _visualOffset = Vector3.zero;  // Clean reset
            }
        }

        /// <summary>
        /// Update: decay the offset towards zero based on gap tier.
        /// Formula: offset *= exp(-k * dt)
        /// With epsilon clamp to avoid micro-oscillations.
        /// </summary>
        /// <param name="dt">Delta time</param>
        /// <param name="currentSpeed">Current player speed</param>
        public void Update(float dt, float currentSpeed)
        {
            if (_isImmobilized)
            {
                _visualOffset = Vector3.zero;
                return;
            }

            float gap = _visualOffset.magnitude;

            // Epsilon clamp: if very small, snap to zero
            if (gap < EPSILON)
            {
                if (gap > 0.01f) // Only log if there was something to snap
                {
                    MovementCycleLogger.LogSnap(gap, "epsilon < 0.5");
                }
                _visualOffset = Vector3.zero;
                return;
            }

            GetDynamicThresholds(currentSpeed, out float smallGap, out float largeGap);

            float k;
            string tier;
            if (gap < smallGap)
            {
                k = SMOOTH_K;  // Half-life ~115ms
                tier = "Smooth";
            }
            else if (gap < largeGap)
            {
                k = ACCEL_K;   // Half-life ~46ms
                tier = "Accel";
            }
            else
            {
                // Snap (soft): gap too large, but not a discontinuity
                // Just reset offset, don't purge buffer
                Snap();
                return;
            }

            // Exponential decay towards zero
            Vector3 offsetBefore = _visualOffset;
            _visualOffset *= Mathf.Exp(-k * dt);
            MovementCycleLogger.LogDecayUpdate(offsetBefore, _visualOffset, gap, tier, currentSpeed);
        }

        /// <summary>
        /// Reset all state.
        /// </summary>
        public void Reset()
        {
            _visualOffset = Vector3.zero;
            _isImmobilized = false;
            _adaptiveBuffer = 0.1f;
        }
    }
}

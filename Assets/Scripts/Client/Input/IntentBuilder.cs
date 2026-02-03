// IntentBuilder.cs - Converts raw input (click/WASD) to InputIntent (V3.0)
// Rate-limited to ~10Hz for network efficiency
// Click -> MoveTo (world position), WASD -> MoveDir (normalized direction)
//
// V3.0 Changes:
// - WASD now generates MoveDir (direction) instead of MoveTo (lookahead position)
// - Removed lookahead calculation for WASD - server handles movement
// - Clear semantic separation: MoveDir = direction, MoveTo = target position
//
// V2.2 Changes:
// - Added immobilize lock: suppresses movement during CC, emits Stop at 2Hz

using UnityEngine;
using MOBANet.Diagnostics;

namespace MOBANet.Client.Input
{
    /// <summary>
    /// Converts raw input (mouse clicks, keyboard) into InputIntent.
    ///
    /// Click-to-move: Immediate MoveTo to clicked position (world coordinates)
    /// WASD/ZQSD: Periodic MoveDir at 10Hz with normalized direction
    ///
    /// SEMANTIC SEPARATION:
    /// - MoveDir = direction vector (WASD) - server applies speed
    /// - MoveTo = target position (click) - server handles pathfinding
    ///
    /// V2.2: When immobilized (root/stun), movement intents are suppressed
    /// and replaced with Stop at 2Hz to avoid spamming the server.
    /// </summary>
    public class IntentBuilder
    {
        // Sequence counter (monotonic)
        private uint _seqId;

        // Rate limiting for keyboard input - aligned with NetworkClient send rate (120Hz)
        private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz (8.3ms) - matches _inputSendRate
        private float _accumulator;

        // V2.2: Rate limiting for Stop during immobilize
        private const float IMMOBILIZE_STOP_INTERVAL = 0.5f;  // ~2Hz
        private float _immobilizeStopAccumulator;

        // State tracking for Stop detection
        private bool _wasMoving;

        // V2.2: Immobilize lock state
        private bool _isImmobilizeLocked;

        // Lookahead configuration (kept for MoveTo/click, not used for MoveDir/WASD)
        private const float LOOKAHEAD_TIME = 0.25f;   // seconds ahead
        private const float MIN_LOOKAHEAD = 50f;      // world units min
        private const float MAX_LOOKAHEAD = 200f;     // world units max

        /// <summary>
        /// Current sequence ID (read-only)
        /// </summary>
        public uint CurrentSeqId => _seqId;

        /// <summary>
        /// Whether movement intents are currently locked (during CC).
        /// </summary>
        public bool IsImmobilizeLocked => _isImmobilizeLocked;

        /// <summary>
        /// Set immobilize lock state.
        /// When locked, movement intents are suppressed and Stop is emitted at 2Hz.
        /// Call this when CC state changes.
        /// </summary>
        public void SetImmobilizeLock(bool locked)
        {
            if (locked && !_isImmobilizeLocked)
            {
                // Just became locked: reset accumulators
                _immobilizeStopAccumulator = 0;
                _wasMoving = false;
            }
            _isImmobilizeLocked = locked;
        }

        /// <summary>
        /// Handle click-to-move input.
        /// Returns MoveTo intent immediately to the clicked position.
        /// V2.2: Returns null if immobilized.
        /// </summary>
        /// <param name="worldPos">World position that was clicked</param>
        /// <param name="clientTick">Current client tick (optional)</param>
        /// <returns>MoveTo intent, or null if immobilized</returns>
        public InputIntent? OnClickToMove(Vector3 worldPos, uint clientTick = 0)
        {
            // V2.2: Suppress movement during immobilize
            if (_isImmobilizeLocked)
                return null;

            _wasMoving = true;
            var intent = InputIntent.MoveTo(worldPos, ++_seqId, clientTick);
            MovementCycleLogger.LogIntentCreated("MoveTo (click)", _seqId, worldPos);
            return intent;
        }

        /// <summary>
        /// Handle keyboard movement input (WASD/ZQSD).
        /// Rate-limited to ~10Hz. Sends MoveDir with normalized direction.
        ///
        /// V3.0: Returns MoveDir (direction) instead of MoveTo (position).
        /// Server applies speed and handles simulation.
        ///
        /// V2.2: When immobilized, returns Stop at 2Hz instead of MoveDir.
        /// </summary>
        /// <param name="inputDir">Input direction (X=right, Y=forward), will be normalized</param>
        /// <param name="dt">Delta time since last call</param>
        /// <param name="clientTick">Current client tick (optional)</param>
        /// <returns>InputIntent if ready to send, null otherwise</returns>
        public InputIntent? OnKeyboardMove(Vector2 inputDir, float dt, uint clientTick = 0)
        {
            // V2.2: During immobilize lock, emit Stop at 2Hz
            if (_isImmobilizeLocked)
            {
                _immobilizeStopAccumulator += dt;
                if (_immobilizeStopAccumulator >= IMMOBILIZE_STOP_INTERVAL)
                {
                    _immobilizeStopAccumulator = 0;
                    return InputIntent.Stop(++_seqId, clientTick);
                }
                return null;
            }

            _accumulator += dt;

            // Check for Stop: no input direction
            // IMPORTANT: MoveDir(0,0) is invalid - must send explicit Stop
            if (inputDir.sqrMagnitude < 0.01f)
            {
                if (_wasMoving)
                {
                    _wasMoving = false;
                    _accumulator = 0;
                    var stopIntent = InputIntent.Stop(++_seqId, clientTick);
                    MovementCycleLogger.LogIntentCreated("Stop", _seqId);
                    return stopIntent;
                }
                return null;
            }

            // Rate limit: ~10Hz
            if (_accumulator < INTENT_SEND_INTERVAL)
                return null;

            _accumulator = 0;
            _wasMoving = true;

            // V3.0: Send normalized direction, not position
            // Server applies speed and handles simulation
            Vector2 normalizedDir = inputDir.normalized;
            var intent = InputIntent.MoveDir(normalizedDir, ++_seqId, clientTick);
            MovementCycleLogger.LogIntentCreated("MoveDir", _seqId, new Vector3(normalizedDir.x, 0, normalizedDir.y), isDirection: true);
            return intent;
        }

        /// <summary>
        /// Create a Follow intent for targeting an entity.
        /// V2.2: Returns null if immobilized.
        /// </summary>
        /// <param name="targetEntityId">Entity to follow</param>
        /// <param name="clientTick">Current client tick (optional)</param>
        /// <returns>Follow intent, or null if immobilized</returns>
        public InputIntent? CreateFollow(uint targetEntityId, uint clientTick = 0)
        {
            // V2.2: Suppress movement during immobilize
            if (_isImmobilizeLocked)
                return null;

            _wasMoving = true;
            return InputIntent.Follow(targetEntityId, ++_seqId, clientTick);
        }

        /// <summary>
        /// Force a Stop intent (used for explicit stop commands).
        /// </summary>
        /// <param name="clientTick">Current client tick (optional)</param>
        /// <returns>Stop intent</returns>
        public InputIntent CreateStop(uint clientTick = 0)
        {
            _wasMoving = false;
            _accumulator = 0;
            return InputIntent.Stop(++_seqId, clientTick);
        }

        /// <summary>
        /// Reset state (e.g., on respawn or teleport).
        /// </summary>
        public void Reset()
        {
            _accumulator = 0;
            _immobilizeStopAccumulator = 0;
            _wasMoving = false;
            _isImmobilizeLocked = false;
            // Don't reset _seqId - it should be monotonic across session
        }
    }
}

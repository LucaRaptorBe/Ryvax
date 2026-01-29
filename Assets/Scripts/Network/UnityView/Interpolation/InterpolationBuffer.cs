// InterpolationBuffer.cs - Per-entity interpolation state buffer
// Ring buffer storing recent states for smooth visual interpolation

using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.NetAdapter.Messages;
using MOBANet.Shared;

namespace MOBANet.UnityView.Interpolation
{
    /// <summary>
    /// State snapshot for interpolation
    /// </summary>
    public struct InterpolationState
    {
        public uint Tick;
        public float Timestamp;
        public Vector3 Position;
        public float RotationY;
        public byte EntityState;
        public bool IsValid;
    }

    /// <summary>
    /// Per-entity interpolation buffer.
    /// Stores recent states and provides smooth interpolation.
    /// </summary>
    public class InterpolationBuffer
    {
        #region Fields

        private readonly InterpolationState[] _buffer;
        private readonly int _bufferSize;
        private int _writeIndex;
        private int _count;

        #endregion

        #region Properties

        /// <summary>
        /// Number of states in buffer
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Buffer capacity
        /// </summary>
        public int Capacity => _bufferSize;

        #endregion

        #region Constructor

        /// <summary>
        /// Create a new interpolation buffer
        /// </summary>
        /// <param name="bufferSize">Number of states to buffer</param>
        public InterpolationBuffer(int bufferSize = 32)
        {
            _bufferSize = bufferSize;
            _buffer = new InterpolationState[bufferSize];
        }

        #endregion

        #region State Management

        /// <summary>
        /// Add a new state to the buffer.
        /// Timestamp est maintenant calculé depuis le tick (network time, pas wall-clock).
        /// </summary>
        public void AddState(uint tick, Vector3 position, float rotationY, byte state)
        {
            _buffer[_writeIndex] = new InterpolationState
            {
                Tick = tick,
                Timestamp = TickClock.TickToTime(tick), // ← Network time, pas Time.time!
                Position = position,
                RotationY = rotationY,
                EntityState = state,
                IsValid = true
            };

            _writeIndex = (_writeIndex + 1) % _bufferSize;
            if (_count < _bufferSize) _count++;
        }

        /// <summary>
        /// Add state from network EntityState
        /// </summary>
        public void AddFromEntityState(uint tick, in EntityState entityState)
        {
            // Log supprimé - trop fréquent (ajout de state à chaque snapshot)
            AddState(
                tick,
                entityState.Position,
                entityState.Rotation,
                entityState.State
            );
        }

        #endregion

        #region Interpolation

        /// <summary>
        /// Try to interpolate position and rotation at render time.
        /// Utilise le temps serveur perçu (continu) pour éviter la quantification.
        /// </summary>
        /// <param name="perceivedServerTime">Temps serveur perçu (continu, pas quantifié)</param>
        /// <param name="position">Position interpolée (out)</param>
        /// <param name="rotationY">Rotation interpolée (out)</param>
        public bool TryInterpolate(float perceivedServerTime, out Vector3 position, out float rotationY)
        {
            position = Vector3.zero;
            rotationY = 0f;

            if (_count < 2)
            {
                // Not enough data for interpolation
                if (_count == 1)
                {
                    // Use single state as fallback
                    int idx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
                    if (_buffer[idx].IsValid)
                    {
                        position = _buffer[idx].Position;
                        rotationY = _buffer[idx].RotationY;
                        return true;
                    }
                }
                // Pas assez de données (< 2 states)
                return false;
            }

            // Calculer renderTime directement depuis le temps perçu (continu, pas quantifié)
            // Évite la quantification qui cause alpha=1.0 permanent
            float renderTime = perceivedServerTime - (NetcodeConstants.INTERPOLATION_BUFFER_TICKS * TickClock.TICK_DELTA);

            // Find two states to interpolate between
            InterpolationState from = default;
            InterpolationState to = default;
            bool found = false;

            for (int i = 0; i < _count - 1; i++)
            {
                int idx1 = GetBufferIndex(i);
                int idx2 = GetBufferIndex(i + 1);

                var s1 = _buffer[idx1];
                var s2 = _buffer[idx2];

                if (s1.IsValid && s2.IsValid &&
                    s1.Timestamp <= renderTime && s2.Timestamp >= renderTime)
                {
                    from = s1;
                    to = s2;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Render time is outside buffer range
                // Use latest state (extrapolation fallback)
                int latestIdx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
                if (_buffer[latestIdx].IsValid)
                {
                    position = _buffer[latestIdx].Position;
                    rotationY = _buffer[latestIdx].RotationY;
                    return true;
                }
                return false;
            }

            // Calculate interpolation factor
            float duration = to.Timestamp - from.Timestamp;
            float t = duration > 0.0001f ? (renderTime - from.Timestamp) / duration : 0f;
            t = Mathf.Clamp01(t);

            // DIAGNOSTIC: Logger les détails de l'interpolation
            // if (Time.frameCount % 60 == 0)
            // {
            //     Debug.Log($"[Buffer] renderTime={renderTime:F3} from.time={from.Timestamp:F3} to.time={to.Timestamp:F3} alpha={t:F3} fromPos={from.Position:F2} toPos={to.Position:F2}");
            // }

            // Interpolate position linearly
            position = Vector3.Lerp(from.Position, to.Position, t);

            // Interpolate rotation (handle wrap-around)
            rotationY = Mathf.LerpAngle(from.RotationY, to.RotationY, t);

            return true;
        }

        /// <summary>
        /// Try to get interpolated state including entity state
        /// </summary>
        public bool TryInterpolate(float perceivedServerTime, out Vector3 position, out float rotationY, out byte entityState)
        {
            entityState = 0;

            if (!TryInterpolate(perceivedServerTime, out position, out rotationY))
            {
                return false;
            }

            // Use latest entity state
            int latestIdx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
            if (_buffer[latestIdx].IsValid)
            {
                entityState = _buffer[latestIdx].EntityState;
            }

            return true;
        }

        #endregion

        #region Utility

        private int GetBufferIndex(int offset)
        {
            return (_writeIndex - _count + offset + _bufferSize) % _bufferSize;
        }

        /// <summary>
        /// Clear the buffer
        /// </summary>
        public void Clear()
        {
            _writeIndex = 0;
            _count = 0;

            for (int i = 0; i < _bufferSize; i++)
            {
                _buffer[i] = default;
            }
        }

        /// <summary>
        /// Get the latest state
        /// </summary>
        public bool TryGetLatest(out InterpolationState state)
        {
            if (_count == 0)
            {
                state = default;
                return false;
            }

            int idx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
            state = _buffer[idx];
            return state.IsValid;
        }

        /// <summary>
        /// Get buffered time span
        /// </summary>
        public float GetBufferedTimeSpan()
        {
            if (_count < 2) return 0;

            int oldestIdx = GetBufferIndex(0);
            int newestIdx = GetBufferIndex(_count - 1);

            return _buffer[newestIdx].Timestamp - _buffer[oldestIdx].Timestamp;
        }

        #endregion
    }
}

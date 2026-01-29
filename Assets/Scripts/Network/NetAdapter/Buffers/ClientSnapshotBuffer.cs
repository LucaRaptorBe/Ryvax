// ClientSnapshotBuffer.cs - Client-side snapshot buffer for interpolation
// Ring buffer storing recent snapshots for smooth visual interpolation

using MOBANet.NetAdapter.Messages;

namespace MOBANet.NetAdapter.Buffers
{
    /// <summary>
    /// Client-side snapshot buffer.
    /// Stores recent snapshots for interpolation of remote entities.
    /// </summary>
    public class ClientSnapshotBuffer
    {
        #region Fields

        private readonly SnapshotDelta[] _buffer;
        private readonly float[] _timestamps;
        private readonly int _bufferSize;
        private int _writeIndex;
        private int _count;

        #endregion

        #region Properties

        /// <summary>
        /// Number of snapshots in buffer
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Buffer capacity
        /// </summary>
        public int Capacity => _bufferSize;

        /// <summary>
        /// Most recent snapshot tick
        /// </summary>
        public uint LatestTick
        {
            get
            {
                if (_count == 0) return 0;
                int idx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
                return _buffer[idx].ServerTick;
            }
        }

        /// <summary>
        /// Most recent acknowledged input sequence
        /// </summary>
        public uint LatestAckSeq
        {
            get
            {
                if (_count == 0) return 0;
                int idx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
                return _buffer[idx].AckInputSeq;
            }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Create a new snapshot buffer
        /// </summary>
        /// <param name="bufferSize">Number of snapshots to buffer</param>
        public ClientSnapshotBuffer(int bufferSize = 32)
        {
            _bufferSize = bufferSize;
            _buffer = new SnapshotDelta[bufferSize];
            _timestamps = new float[bufferSize];
            _writeIndex = 0;
            _count = 0;
        }

        #endregion

        #region Buffer Operations

        /// <summary>
        /// Add a snapshot to the buffer
        /// </summary>
        /// <param name="snapshot">Snapshot to add</param>
        /// <param name="timestamp">Receive timestamp (Time.time)</param>
        public void AddSnapshot(in SnapshotDelta snapshot, float timestamp)
        {
            // Check for out-of-order snapshots
            if (_count > 0)
            {
                int lastIdx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
                if (snapshot.ServerTick <= _buffer[lastIdx].ServerTick)
                {
                    // Out of order or duplicate - ignore
                    return;
                }
            }

            _buffer[_writeIndex] = snapshot;
            _timestamps[_writeIndex] = timestamp;

            _writeIndex = (_writeIndex + 1) % _bufferSize;
            if (_count < _bufferSize) _count++;
        }

        /// <summary>
        /// Get the most recent snapshot
        /// </summary>
        public bool TryGetLatest(out SnapshotDelta snapshot, out float timestamp)
        {
            if (_count == 0)
            {
                snapshot = default;
                timestamp = 0;
                return false;
            }

            int idx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
            snapshot = _buffer[idx];
            timestamp = _timestamps[idx];
            return true;
        }

        /// <summary>
        /// Find two snapshots for interpolation at given render time
        /// </summary>
        /// <param name="renderTime">Target render time</param>
        /// <param name="from">Earlier snapshot</param>
        /// <param name="to">Later snapshot</param>
        /// <param name="t">Interpolation factor (0-1)</param>
        /// <returns>True if interpolation is possible</returns>
        public bool TryGetInterpolationPair(
            float renderTime,
            out SnapshotDelta from,
            out SnapshotDelta to,
            out float t)
        {
            from = default;
            to = default;
            t = 0;

            if (_count < 2) return false;

            // Find two snapshots that bracket renderTime
            for (int i = 0; i < _count - 1; i++)
            {
                int idx1 = (_writeIndex - _count + i + _bufferSize) % _bufferSize;
                int idx2 = (_writeIndex - _count + i + 1 + _bufferSize) % _bufferSize;

                float t1 = _timestamps[idx1];
                float t2 = _timestamps[idx2];

                if (t1 <= renderTime && t2 >= renderTime)
                {
                    from = _buffer[idx1];
                    to = _buffer[idx2];

                    float duration = t2 - t1;
                    t = duration > 0 ? (renderTime - t1) / duration : 0f;
                    t = UnityEngine.Mathf.Clamp01(t);

                    return true;
                }
            }

            // Render time is ahead of all snapshots - use latest for extrapolation
            int latestIdx = (_writeIndex - 1 + _bufferSize) % _bufferSize;
            from = _buffer[latestIdx];
            to = _buffer[latestIdx];
            t = 0;

            return true;
        }

        /// <summary>
        /// Get snapshot at specific index (0 = oldest)
        /// </summary>
        public bool TryGetAt(int index, out SnapshotDelta snapshot, out float timestamp)
        {
            if (index < 0 || index >= _count)
            {
                snapshot = default;
                timestamp = 0;
                return false;
            }

            int idx = (_writeIndex - _count + index + _bufferSize) % _bufferSize;
            snapshot = _buffer[idx];
            timestamp = _timestamps[idx];
            return true;
        }

        /// <summary>
        /// Find entity state in a snapshot
        /// </summary>
        public bool TryFindEntity(
            in SnapshotDelta snapshot,
            uint entityId,
            out EntityState state)
        {
            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                if (snapshot.Entities[i].EntityId == entityId)
                {
                    state = snapshot.Entities[i];
                    return true;
                }
            }

            state = default;
            return false;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Clear the buffer
        /// </summary>
        public void Clear()
        {
            _writeIndex = 0;
            _count = 0;
        }

        /// <summary>
        /// Get time span of buffered snapshots
        /// </summary>
        public float GetBufferedTimeSpan()
        {
            if (_count < 2) return 0;

            int oldestIdx = (_writeIndex - _count + _bufferSize) % _bufferSize;
            int newestIdx = (_writeIndex - 1 + _bufferSize) % _bufferSize;

            return _timestamps[newestIdx] - _timestamps[oldestIdx];
        }

        #endregion
    }
}

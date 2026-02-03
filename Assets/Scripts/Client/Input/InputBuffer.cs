// InputBuffer.cs - Client-side input buffer for UDP redundancy
// Maintains recent commands for inclusion in InputPacket.
// When a packet is sent, the last N commands are included for redundancy.

using System;
using MOBANet.NetAdapter.Messages;
using MOBANet.Shared;

namespace MOBANet.UnityView.Input
{
    /// <summary>
    /// Ring buffer for recent input commands.
    /// Used to provide redundancy in UDP input packets.
    ///
    /// When sending an InputPacket:
    /// 1. Add new command to buffer
    /// 2. Get last N commands via GetRecentCommands()
    /// 3. Send InputPacket with all recent commands
    ///
    /// On snapshot received:
    /// 1. Call AcknowledgeUpTo(ackSeq) to mark commands as delivered
    /// 2. Acknowledged commands are kept for redundancy but marked
    /// </summary>
    public class InputBuffer
    {
        private readonly GameCommand[] _buffer;
        private readonly int _capacity;
        private int _writeIndex;
        private int _count;
        private uint _lastAckedSequence;

        /// <summary>
        /// Number of commands currently in buffer.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Last acknowledged sequence number.
        /// </summary>
        public uint LastAckedSequence => _lastAckedSequence;

        /// <summary>
        /// Create a new input buffer.
        /// </summary>
        /// <param name="capacity">Buffer capacity (should be >= INPUT_REDUNDANCY_COUNT)</param>
        public InputBuffer(int capacity = 0)
        {
            _capacity = capacity > 0 ? capacity : NetcodeConstants.INPUT_BUFFER_SIZE;
            _buffer = new GameCommand[_capacity];
            _writeIndex = 0;
            _count = 0;
            _lastAckedSequence = 0;
        }

        /// <summary>
        /// Add a command to the buffer.
        /// </summary>
        public void Add(GameCommand cmd)
        {
            _buffer[_writeIndex] = cmd;
            _writeIndex = (_writeIndex + 1) % _capacity;

            if (_count < _capacity)
                _count++;
        }

        /// <summary>
        /// Get the N most recent commands for inclusion in InputPacket.
        /// Commands are returned in sequence order (oldest first).
        /// </summary>
        /// <param name="maxCount">Maximum number of commands to return</param>
        /// <returns>Array of recent commands (may be fewer than maxCount)</returns>
        public GameCommand[] GetRecentCommands(int maxCount = 0)
        {
            if (maxCount <= 0)
                maxCount = NetcodeConstants.INPUT_REDUNDANCY_COUNT;

            int toReturn = Math.Min(_count, maxCount);
            if (toReturn == 0)
                return Array.Empty<GameCommand>();

            var result = new GameCommand[toReturn];

            // Read from oldest to newest within the range we want
            int startIndex = (_writeIndex - toReturn + _capacity) % _capacity;
            for (int i = 0; i < toReturn; i++)
            {
                int idx = (startIndex + i) % _capacity;
                result[i] = _buffer[idx];
            }

            return result;
        }

        /// <summary>
        /// Get unacknowledged commands only (sequence > lastAckedSequence).
        /// Used when we only want to send commands the server hasn't seen.
        /// </summary>
        public GameCommand[] GetUnackedCommands(int maxCount = 0)
        {
            if (maxCount <= 0)
                maxCount = NetcodeConstants.INPUT_REDUNDANCY_COUNT;

            // First pass: count unacked commands
            int unackedCount = 0;
            for (int i = 0; i < _count && unackedCount < maxCount; i++)
            {
                int idx = (_writeIndex - _count + i + _capacity) % _capacity;
                if (_buffer[idx].Sequence > _lastAckedSequence)
                    unackedCount++;
            }

            if (unackedCount == 0)
                return Array.Empty<GameCommand>();

            // Second pass: collect unacked commands
            var result = new GameCommand[unackedCount];
            int resultIdx = 0;
            for (int i = 0; i < _count && resultIdx < unackedCount; i++)
            {
                int idx = (_writeIndex - _count + i + _capacity) % _capacity;
                if (_buffer[idx].Sequence > _lastAckedSequence)
                    result[resultIdx++] = _buffer[idx];
            }

            return result;
        }

        /// <summary>
        /// Mark commands up to this sequence as acknowledged by server.
        /// Called when snapshot is received with AckInputSeq.
        /// </summary>
        public void AcknowledgeUpTo(uint sequence)
        {
            if (sequence > _lastAckedSequence)
                _lastAckedSequence = sequence;
        }

        /// <summary>
        /// Check if there are any unacknowledged commands.
        /// </summary>
        public bool HasUnackedCommands()
        {
            for (int i = 0; i < _count; i++)
            {
                int idx = (_writeIndex - _count + i + _capacity) % _capacity;
                if (_buffer[idx].Sequence > _lastAckedSequence)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Get the highest sequence number in the buffer.
        /// </summary>
        public uint GetMaxSequence()
        {
            if (_count == 0) return 0;

            uint max = 0;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_writeIndex - _count + i + _capacity) % _capacity;
                if (_buffer[idx].Sequence > max)
                    max = _buffer[idx].Sequence;
            }
            return max;
        }

        /// <summary>
        /// Clear the buffer.
        /// </summary>
        public void Clear()
        {
            _writeIndex = 0;
            _count = 0;
            _lastAckedSequence = 0;
        }

        /// <summary>
        /// Debug string.
        /// </summary>
        public override string ToString()
        {
            return $"InputBuffer[Count={_count}, MaxSeq={GetMaxSequence()}, AckedSeq={_lastAckedSequence}]";
        }
    }
}

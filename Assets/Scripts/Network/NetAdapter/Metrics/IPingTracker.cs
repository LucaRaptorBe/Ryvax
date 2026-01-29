// IPingTracker.cs - Interface for RTT/ping tracking
// Implemented by NetworkMetrics, used by UnityView without circular dependency

using System;

namespace MOBANet.NetAdapter.Metrics
{
    /// <summary>
    /// Static ping tracking events.
    /// Allows UnityView to report ping/pong without referencing Debug assembly.
    /// NetworkMetrics subscribes to these events.
    /// </summary>
    public static class PingTracker
    {
        /// <summary>
        /// Called when input is sent to server (ping).
        /// Parameter: input sequence number.
        /// </summary>
        public static Action<uint> OnPingSent;

        /// <summary>
        /// Called when snapshot is received with ack sequence (pong).
        /// Parameter: acknowledged sequence number.
        /// </summary>
        public static Action<uint> OnPongReceived;

        /// <summary>
        /// Record a ping sent (convenience method).
        /// </summary>
        public static void RecordPingSent(uint sequence)
        {
            OnPingSent?.Invoke(sequence);
        }

        /// <summary>
        /// Record a pong received (convenience method).
        /// </summary>
        public static void RecordPongReceived(uint ackedSequence)
        {
            OnPongReceived?.Invoke(ackedSequence);
        }
    }
}

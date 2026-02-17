// NetworkPingMeasure.cs - Measures application-level RTT ping
// METRIC B: End-to-end latency measurement using Stopwatch

using System.Diagnostics;
using UnityEngine;
using MOBANet.UnityView.Core;
using MOBANet.NetAdapter.Messages;
using Debug = UnityEngine.Debug;

namespace MOBANet.Diagnostics
{
    /// <summary>
    /// Measures network RTT using application-level ping.
    /// Attach to NetworkClient GameObject to enable.
    /// </summary>
    public class NetworkPingMeasure : MonoBehaviour
    {
        [Header("Ping Settings")]
        [SerializeField] private bool _enablePing = true;
        [SerializeField] private float _pingInterval = 1.0f; // Send ping every second

        [Header("References")]
        [SerializeField] private NetworkClient _networkClient;

        private float _pingAccumulator;
        private uint _lastPingSeq;
        private long _lastPingSentTicks;
        private bool _waitingForPong;

        private void Update()
        {
            if (!_enablePing || _networkClient == null || !_networkClient.IsConnected)
                return;

            _pingAccumulator += Time.deltaTime;

            if (_pingAccumulator >= _pingInterval && !_waitingForPong)
            {
                _pingAccumulator = 0f;
                SendPing();
            }
        }

        private void SendPing()
        {
            _lastPingSentTicks = Stopwatch.GetTimestamp();
            _waitingForPong = true;

            // Create ping command (client → server)
            // Sequence=0 placeholder; NetworkClient.SendEventCommand assigns the real monotonic seq
            var pingCmd = new GameCommand
            {
                Sequence = 0,
                Category = MOBANet.Shared.CommandCategory.Ping,
                Action = MOBANet.Shared.PingAction.Request,
                Data1 = 0,
                Data2 = 0
            };

            _lastPingSeq = _networkClient.SendEventCommand(pingCmd);
        }

        /// <summary>
        /// Call this when pong is received from server.
        /// </summary>
        public void OnPongReceived(uint pongSequence)
        {
            if (!_waitingForPong || pongSequence != _lastPingSeq)
                return;

            long nowTicks = Stopwatch.GetTimestamp();
            double rttMs = (nowTicks - _lastPingSentTicks) * 1000.0 / Stopwatch.Frequency;

            // Debug.Log($"[METRIC B] [RTT] seq={pongSequence} rttMs={rttMs:F2}");

            _waitingForPong = false;
        }
    }
}

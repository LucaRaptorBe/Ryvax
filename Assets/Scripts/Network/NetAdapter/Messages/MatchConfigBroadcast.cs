// MatchConfigBroadcast.cs - Match configuration sent to clients on connect
// Contains spawn positions and other match-specific settings

using FishNet.Broadcast;
using UnityEngine;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Match configuration broadcast sent to clients when they connect.
    /// Contains spawn positions for both teams.
    /// </summary>
    public struct MatchConfigBroadcast : IBroadcast
    {
        /// <summary>
        /// Team 1 spawn positions
        /// </summary>
        public Vector3[] Team1Spawns;

        /// <summary>
        /// Team 2 spawn positions
        /// </summary>
        public Vector3[] Team2Spawns;

        public MatchConfigBroadcast(Vector3[] team1Spawns, Vector3[] team2Spawns)
        {
            Team1Spawns = team1Spawns;
            Team2Spawns = team2Spawns;
        }
    }
}

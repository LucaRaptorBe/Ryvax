// ReliableEvent.cs - Reliable events for critical game state changes
// Sent reliably - guaranteed delivery for spawns, deaths, match events

using System.Runtime.InteropServices;
using UnityEngine;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Reliable event for critical game state changes.
    /// Always sent with guaranteed delivery.
    /// Used for: spawns, deaths, match start/end, abilities.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ReliableEvent : INetMessage
    {
        public const byte MSG_ID = 3;
        public const ushort MSG_VERSION = 1;

        public byte MessageId => MSG_ID;
        public ushort Version => MSG_VERSION;

        /// <summary>
        /// Server tick when this event occurred
        /// </summary>
        public uint ServerTick;

        /// <summary>
        /// Event type
        /// </summary>
        public EventType Type;

        /// <summary>
        /// Primary entity involved (if any)
        /// </summary>
        public uint EntityId;

        /// <summary>
        /// Context-dependent data field 1
        /// </summary>
        public uint Data1;

        /// <summary>
        /// Context-dependent data field 2
        /// </summary>
        public uint Data2;

        // Total size: 4 + 1 + 4 + 4 + 4 = 17 bytes

        #region Factory Methods

        /// <summary>
        /// Create entity spawn event.
        /// Encodes spawn position in Data1 for guaranteed synchronization.
        /// </summary>
        /// <param name="tick">Server tick</param>
        /// <param name="entityId">Entity ID</param>
        /// <param name="ownerClientId">Client ID that owns this entity</param>
        /// <param name="teamId">Team ID (1 or 2)</param>
        /// <param name="spawnPos">Spawn position (encoded in Data1 with 0.01 unit precision)</param>
        public static ReliableEvent EntitySpawn(uint tick, uint entityId, int ownerClientId, byte teamId, Vector3 spawnPos)
        {
            // Encode position: 16 bits for X, 16 bits for Z (precision: 0.01 unit, range: -327.68 to +327.67)
            short posX = (short)(spawnPos.x * 100f);
            short posZ = (short)(spawnPos.z * 100f);
            uint encodedPos = (uint)(((posX & 0xFFFF) << 16) | (posZ & 0xFFFF));

            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.EntitySpawn,
                EntityId = entityId,
                Data1 = encodedPos,  // Spawn position (X << 16 | Z)
                Data2 = ((uint)ownerClientId << 8) | teamId  // ClientId (24 bits) + TeamId (8 bits)
            };
        }

        /// <summary>
        /// Decode spawn position from EntitySpawn event
        /// </summary>
        public static Vector3 DecodeSpawnPosition(ReliableEvent evt)
        {
            // Decode: extract 16-bit X and Z from Data1
            short posX = (short)((evt.Data1 >> 16) & 0xFFFF);
            short posZ = (short)(evt.Data1 & 0xFFFF);

            // Convert back to float (divide by 100 for precision)
            return new Vector3(posX / 100f, 0f, posZ / 100f);
        }

        /// <summary>
        /// Decode owner client ID from EntitySpawn event
        /// </summary>
        public static int DecodeOwnerClientId(ReliableEvent evt)
        {
            return (int)(evt.Data2 >> 8);
        }

        /// <summary>
        /// Decode team ID from EntitySpawn event
        /// </summary>
        public static byte DecodeTeamId(ReliableEvent evt)
        {
            return (byte)(evt.Data2 & 0xFF);
        }

        /// <summary>
        /// Create entity death event
        /// </summary>
        public static ReliableEvent EntityDeath(uint tick, uint entityId, uint killerId = 0)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.EntityDeath,
                EntityId = entityId,
                Data1 = killerId,
                Data2 = 0
            };
        }

        /// <summary>
        /// Create entity respawn event
        /// </summary>
        public static ReliableEvent EntityRespawn(uint tick, uint entityId, ushort spawnX, ushort spawnZ)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.EntityRespawn,
                EntityId = entityId,
                Data1 = spawnX,
                Data2 = spawnZ
            };
        }

        /// <summary>
        /// Create match start event
        /// </summary>
        public static ReliableEvent MatchStart(uint tick, uint matchId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.MatchStart,
                EntityId = 0,
                Data1 = matchId,
                Data2 = 0
            };
        }

        /// <summary>
        /// Create match end event
        /// </summary>
        public static ReliableEvent MatchEnd(uint tick, byte winningTeam, uint score1, uint score2)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.MatchEnd,
                EntityId = winningTeam,
                Data1 = score1,
                Data2 = score2
            };
        }

        /// <summary>
        /// Create player joined event
        /// </summary>
        public static ReliableEvent PlayerJoined(uint tick, int clientId, byte teamId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.PlayerJoined,
                EntityId = 0,
                Data1 = (uint)clientId,
                Data2 = teamId
            };
        }

        /// <summary>
        /// Create player left event
        /// </summary>
        public static ReliableEvent PlayerLeft(uint tick, int clientId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.PlayerLeft,
                EntityId = 0,
                Data1 = (uint)clientId,
                Data2 = 0
            };
        }

        /// <summary>
        /// Create entity entered AOI event.
        /// Sent to client when an entity becomes visible.
        /// </summary>
        public static ReliableEvent EntityEnterAOI(uint tick, uint entityId, int ownerClientId, byte teamId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.EntityEnterAOI,
                EntityId = entityId,
                Data1 = (uint)ownerClientId,
                Data2 = teamId
            };
        }

        /// <summary>
        /// Create entity left AOI event.
        /// Sent to client when an entity leaves visibility.
        /// </summary>
        public static ReliableEvent EntityLeaveAOI(uint tick, uint entityId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.EntityLeaveAOI,
                EntityId = entityId,
                Data1 = 0,
                Data2 = 0
            };
        }

        /// <summary>
        /// Create class assign event.
        /// Sent when a player selects/changes their class.
        /// </summary>
        public static ReliableEvent ClassAssign(uint tick, uint entityId, byte classId)
        {
            return new ReliableEvent
            {
                ServerTick = tick,
                Type = EventType.ClassAssign,
                EntityId = entityId,
                Data1 = classId,
                Data2 = 0
            };
        }

        /// <summary>
        /// Decode class ID from ClassAssign event
        /// </summary>
        public static byte DecodeClassId(ReliableEvent evt)
        {
            return (byte)evt.Data1;
        }

        #endregion
    }

    /// <summary>
    /// Event types for ReliableEvent
    /// </summary>
    public enum EventType : byte
    {
        None = 0,

        // Match lifecycle (1-9)
        MatchStart = 1,
        MatchEnd = 2,
        RoundStart = 3,
        RoundEnd = 4,
        MatchPaused = 5,
        MatchResumed = 6,

        // Entity lifecycle (10-19)
        EntitySpawn = 10,
        EntityDeath = 11,
        EntityRespawn = 12,
        EntityDespawn = 13,
        EntityEnterAOI = 14,  // Entity entered client's area of interest
        EntityLeaveAOI = 15,  // Entity left client's area of interest

        // Player events (20-29)
        PlayerJoined = 20,
        PlayerLeft = 21,
        PlayerTeamAssigned = 22,
        PlayerReady = 23,
        PlayerDisconnected = 24,
        PlayerReconnected = 25,
        ClassAssign = 26,

        // Objective events (30-39)
        ObjectiveCaptured = 30,
        ObjectiveLost = 31,
        ObjectiveContested = 32,
        ObjectiveNeutralized = 33,

        // Combat events (40-49)
        AbilityUsed = 40,
        AbilityHit = 41,
        AbilityCanceled = 42,
        DamageDealt = 43,
        HealingReceived = 44,
        StatusEffectApplied = 45,
        StatusEffectRemoved = 46,

        // Economy events (50-59)
        GoldEarned = 50,
        ItemPurchased = 51,
        ItemSold = 52,

        // Structure events (60-69)
        TowerDestroyed = 60,
        InhibitorDestroyed = 61,
        NexusDestroyed = 62,

        // Special events (100+)
        Chat = 100,
        Ping = 101,
        Emote = 102,
    }
}

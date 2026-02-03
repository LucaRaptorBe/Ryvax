// InputPacket.cs - Bundled input commands for UDP transport with redundancy
// Solves TCP head-of-line blocking by sending multiple commands per packet.
// Reference: Overwatch GDC 2017, Valorant netcode.
//
// INTENT-BASED MOVEMENT v2.0:
// Each packet contains an IntentType with type-specific payload.
// - MoveDir: normalized direction (WASD) - Payload = dir * 127
// - MoveTo: world position (click) - Payload = pos * 10
// - Stop: no payload
// - Follow: entity ID split across payloads
//
// See /Docs/InputCommandSpec.md for full specification.

using System;
using UnityEngine;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Intent type for movement packets.
    /// Each type has distinct payload semantics - do not mix.
    /// </summary>
    public enum PacketIntentType : byte
    {
        /// <summary>No movement intent (only event commands)</summary>
        None = 0,
        /// <summary>Move in direction (WASD) - Payload: normalized dir * 127</summary>
        MoveDir = 1,
        /// <summary>Move to world position (click) - Payload: pos * 10</summary>
        MoveTo = 2,
        /// <summary>Stop moving - no payload</summary>
        Stop = 3,
        /// <summary>Follow an entity - Payload: entity ID split</summary>
        Follow = 4
    }

    /// <summary>
    /// Bundled input packet containing intent and event commands for UDP redundancy.
    /// Each packet includes the last N commands, so packet loss is recovered
    /// on the next packet arrival (typically 33ms at 30Hz).
    ///
    /// CRITICAL: IntentType determines how Payload0/Payload1 are interpreted.
    /// - MoveDir: DequantizeDirection (÷127)
    /// - MoveTo: DequantizePosition (÷10)
    /// Never use wrong accessor for intent type.
    /// </summary>
    public struct InputPacket : INetMessage
    {
        public const byte MSG_ID = 10;
        public const ushort MSG_VERSION = 4; // v4: Intent-based with type-specific payloads

        public byte MessageId => MSG_ID;
        public ushort Version => MSG_VERSION;

        /// <summary>
        /// Client tick when this packet was sent.
        /// Used for lag compensation on server.
        /// </summary>
        public uint ClientTick;

        /// <summary>
        /// Monotonic sequence for movement state.
        /// Global across all intent types (MoveDir, MoveTo, Stop).
        /// Server uses this to reject out-of-order packets.
        /// </summary>
        public uint MovementSeq;

        #region Intent-Based Movement

        /// <summary>
        /// Movement intent type. Determines payload interpretation.
        /// </summary>
        public PacketIntentType IntentType;

        /// <summary>
        /// Payload field 0. Interpretation depends on IntentType:
        /// - MoveDir: dirX * 127 (quantized direction)
        /// - MoveTo: posX * 10 (quantized position)
        /// - Follow: entityId low 16 bits
        /// - Stop/None: unused (0)
        /// </summary>
        public short Payload0;

        /// <summary>
        /// Payload field 1. Interpretation depends on IntentType:
        /// - MoveDir: dirZ * 127 (quantized direction)
        /// - MoveTo: posZ * 10 (quantized position)
        /// - Follow: entityId high 16 bits
        /// - Stop/None: unused (0)
        /// </summary>
        public short Payload1;

        #endregion

        /// <summary>
        /// Number of discrete event commands in this packet.
        /// Movement is handled via IntentType+Payload, not commands.
        /// </summary>
        public byte CommandCount;

        /// <summary>
        /// Discrete event commands (jump, spell, attack, etc.).
        /// Movement is EXCLUDED - movement is intent-based.
        /// Contains recent unacked commands for redundancy.
        /// </summary>
        public GameCommand[] Commands;

        #region Factory Methods

        /// <summary>
        /// Create a MoveDir packet (WASD movement).
        /// Direction must be normalized before calling.
        /// </summary>
        public static InputPacket CreateMoveDir(uint clientTick, uint movementSeq, Vector2 direction, GameCommand[] eventCommands = null)
        {
            return new InputPacket
            {
                ClientTick = clientTick,
                MovementSeq = movementSeq,
                IntentType = PacketIntentType.MoveDir,
                Payload0 = GameCommand.QuantizeDirection(direction.x),
                Payload1 = GameCommand.QuantizeDirection(direction.y),
                CommandCount = (byte)(eventCommands?.Length ?? 0),
                Commands = eventCommands ?? Array.Empty<GameCommand>()
            };
        }

        /// <summary>
        /// Create a MoveTo packet (click movement).
        /// Position is world coordinates.
        /// </summary>
        public static InputPacket CreateMoveTo(uint clientTick, uint movementSeq, Vector2 targetPos, GameCommand[] eventCommands = null)
        {
            return new InputPacket
            {
                ClientTick = clientTick,
                MovementSeq = movementSeq,
                IntentType = PacketIntentType.MoveTo,
                Payload0 = GameCommand.QuantizePosition(targetPos.x),
                Payload1 = GameCommand.QuantizePosition(targetPos.y),
                CommandCount = (byte)(eventCommands?.Length ?? 0),
                Commands = eventCommands ?? Array.Empty<GameCommand>()
            };
        }

        /// <summary>
        /// Create a Stop packet.
        /// </summary>
        public static InputPacket CreateStop(uint clientTick, uint movementSeq, GameCommand[] eventCommands = null)
        {
            return new InputPacket
            {
                ClientTick = clientTick,
                MovementSeq = movementSeq,
                IntentType = PacketIntentType.Stop,
                Payload0 = 0,
                Payload1 = 0,
                CommandCount = (byte)(eventCommands?.Length ?? 0),
                Commands = eventCommands ?? Array.Empty<GameCommand>()
            };
        }

        /// <summary>
        /// Create a Follow packet.
        /// </summary>
        public static InputPacket CreateFollow(uint clientTick, uint movementSeq, uint targetEntityId, GameCommand[] eventCommands = null)
        {
            return new InputPacket
            {
                ClientTick = clientTick,
                MovementSeq = movementSeq,
                IntentType = PacketIntentType.Follow,
                Payload0 = (short)(targetEntityId & 0xFFFF),
                Payload1 = (short)((targetEntityId >> 16) & 0xFFFF),
                CommandCount = (byte)(eventCommands?.Length ?? 0),
                Commands = eventCommands ?? Array.Empty<GameCommand>()
            };
        }

        /// <summary>
        /// Create a packet with only event commands (no movement intent).
        /// </summary>
        public static InputPacket CreateEventsOnly(uint clientTick, GameCommand[] eventCommands)
        {
            return new InputPacket
            {
                ClientTick = clientTick,
                MovementSeq = 0,
                IntentType = PacketIntentType.None,
                Payload0 = 0,
                Payload1 = 0,
                CommandCount = (byte)(eventCommands?.Length ?? 0),
                Commands = eventCommands ?? Array.Empty<GameCommand>()
            };
        }

        #endregion

        #region Accessors

        /// <summary>
        /// Get direction for MoveDir intent.
        /// ONLY call when IntentType == MoveDir.
        /// </summary>
        public Vector2 GetDirection()
        {
            return new Vector2(
                GameCommand.DequantizeDirection(Payload0),
                GameCommand.DequantizeDirection(Payload1)
            );
        }

        /// <summary>
        /// Get target position for MoveTo intent.
        /// ONLY call when IntentType == MoveTo.
        /// </summary>
        public Vector2 GetTargetPosition()
        {
            return new Vector2(
                GameCommand.DequantizePosition(Payload0),
                GameCommand.DequantizePosition(Payload1)
            );
        }

        /// <summary>
        /// Get target entity ID for Follow intent.
        /// ONLY call when IntentType == Follow.
        /// </summary>
        public uint GetFollowTargetId()
        {
            return (uint)((ushort)Payload0 | ((ushort)Payload1 << 16));
        }

        /// <summary>
        /// Get the highest sequence number in event commands.
        /// Used by server to track acknowledgment.
        /// </summary>
        public uint GetMaxEventSequence()
        {
            if (Commands == null || Commands.Length == 0) return 0;

            uint max = 0;
            for (int i = 0; i < Commands.Length; i++)
            {
                if (Commands[i].Sequence > max)
                    max = Commands[i].Sequence;
            }
            return max;
        }

        #endregion

        public override string ToString()
        {
            string payloadStr = IntentType switch
            {
                PacketIntentType.MoveDir => $"dir=({GetDirection().x:F2},{GetDirection().y:F2})",
                PacketIntentType.MoveTo => $"target=({GetTargetPosition().x:F1},{GetTargetPosition().y:F1})",
                PacketIntentType.Follow => $"follow={GetFollowTargetId()}",
                PacketIntentType.Stop => "stop",
                _ => "none"
            };
            return $"InputPacket[Tick={ClientTick}, Seq={MovementSeq}, {IntentType}: {payloadStr}, Events={CommandCount}]";
        }
    }
}

// InputIntent.cs - LoL-style input intentions
// Input is not raw WASD or click, it's an intention (MoveTo, Stop, Follow)
// Server processes intentions, not raw directions

using UnityEngine;

namespace MOBANet.Client.Input
{
    /// <summary>
    /// Type of input intention.
    /// Each type has distinct semantics and payload format.
    /// </summary>
    public enum InputIntentType : byte
    {
        /// <summary>Move in a direction (WASD) - payload: normalized dir XZ</summary>
        MoveDir = 0,
        /// <summary>Move towards a world position (click) - payload: target pos XZ</summary>
        MoveTo = 1,
        /// <summary>Stop moving immediately - no payload</summary>
        Stop = 2,
        /// <summary>Follow an entity - payload: entity ID</summary>
        Follow = 3
    }

    /// <summary>
    /// LoL-style input intention.
    /// Represents what the player wants to do, not how they input it.
    /// The server decides how to execute the intention (pathfinding, collision, etc.)
    /// </summary>
    public struct InputIntent
    {
        /// <summary>Type of intention (MoveDir, MoveTo, Stop, Follow)</summary>
        public InputIntentType Type;

        /// <summary>Normalized direction for MoveDir (XZ plane)</summary>
        public Vector2 Direction;

        /// <summary>Target world position for MoveTo</summary>
        public Vector2 WorldPos;

        /// <summary>Target entity ID for Follow</summary>
        public uint TargetEntityId;

        /// <summary>Monotonic sequence number (global across all movement types)</summary>
        public uint SeqId;

        /// <summary>Client tick when intent was created (for debug/lag compensation)</summary>
        public uint ClientTick;

        /// <summary>Create a MoveDir intent (WASD movement)</summary>
        public static InputIntent MoveDir(Vector2 direction, uint seqId, uint clientTick = 0)
        {
            return new InputIntent
            {
                Type = InputIntentType.MoveDir,
                Direction = direction.sqrMagnitude > 0.01f ? direction.normalized : Vector2.zero,
                WorldPos = Vector2.zero,
                TargetEntityId = 0,
                SeqId = seqId,
                ClientTick = clientTick
            };
        }

        /// <summary>Create a MoveTo intent (click movement)</summary>
        public static InputIntent MoveTo(Vector2 worldPos, uint seqId, uint clientTick = 0)
        {
            return new InputIntent
            {
                Type = InputIntentType.MoveTo,
                WorldPos = worldPos,
                TargetEntityId = 0,
                SeqId = seqId,
                ClientTick = clientTick
            };
        }

        /// <summary>Create a MoveTo intent from Vector3 (uses X and Z)</summary>
        public static InputIntent MoveTo(Vector3 worldPos, uint seqId, uint clientTick = 0)
        {
            return MoveTo(new Vector2(worldPos.x, worldPos.z), seqId, clientTick);
        }

        /// <summary>Create a Stop intent</summary>
        public static InputIntent Stop(uint seqId, uint clientTick = 0)
        {
            return new InputIntent
            {
                Type = InputIntentType.Stop,
                WorldPos = Vector2.zero,
                TargetEntityId = 0,
                SeqId = seqId,
                ClientTick = clientTick
            };
        }

        /// <summary>Create a Follow intent</summary>
        public static InputIntent Follow(uint targetEntityId, uint seqId, uint clientTick = 0)
        {
            return new InputIntent
            {
                Type = InputIntentType.Follow,
                WorldPos = Vector2.zero,
                TargetEntityId = targetEntityId,
                SeqId = seqId,
                ClientTick = clientTick
            };
        }

        public override string ToString()
        {
            return Type switch
            {
                InputIntentType.MoveDir => $"MoveDir({Direction.x:F2},{Direction.y:F2}) seq={SeqId}",
                InputIntentType.MoveTo => $"MoveTo({WorldPos.x:F1},{WorldPos.y:F1}) seq={SeqId}",
                InputIntentType.Stop => $"Stop seq={SeqId}",
                InputIntentType.Follow => $"Follow({TargetEntityId}) seq={SeqId}",
                _ => $"Unknown({Type}) seq={SeqId}"
            };
        }
    }
}

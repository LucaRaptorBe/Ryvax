// GameCommand.cs - Unified network message for all game commands
// Supports Movement, Abilities, Items, Attacks with extensible category system

using System.Runtime.InteropServices;
using UnityEngine;
using MOBANet.Shared;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Unified game command sent from client to server.
    /// Replaces per-tick InputCmd with event-based commands.
    /// Only sent when input state changes (not every tick).
    ///
    /// Size: 14 bytes (blittable, network-efficient)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GameCommand : INetMessage
    {
        public const byte MSG_ID = 4;
        public const ushort MSG_VERSION = 1;

        public byte MessageId => MSG_ID;
        public ushort Version => MSG_VERSION;

        /// <summary>
        /// Sequence number for ordering and acknowledgment
        /// </summary>
        public uint Sequence;

        /// <summary>
        /// Command category (Movement, Ability, Item, etc.)
        /// </summary>
        public CommandCategory Category;

        /// <summary>
        /// Action within category (MoveStart, CastQ, UsePotion, etc.)
        /// </summary>
        public byte Action;

        /// <summary>
        /// Generic data field 0 - interpretation depends on Category+Action
        /// Movement: Direction X (quantized)
        /// Ability: Target position X
        /// Item: Slot number
        /// </summary>
        public short Data0;

        /// <summary>
        /// Generic data field 1 - interpretation depends on Category+Action
        /// Movement: Direction Z (quantized)
        /// Ability: Target position Z
        /// </summary>
        public short Data1;

        /// <summary>
        /// Generic data field 2 - interpretation depends on Category+Action
        /// Attack/Ability: Target entity ID
        /// Item: Item ID or target entity ID
        /// </summary>
        public uint Data2;

        // Total size: 4 + 1 + 1 + 2 + 2 + 4 = 14 bytes

        #region Factory Methods - Movement

        /// <summary>
        /// Create a movement start command with direction
        /// </summary>
        public static GameCommand MoveStart(uint seq, Vector2 direction)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Movement,
                Action = MovementAction.Start,
                Data0 = QuantizeDirection(direction.x),
                Data1 = QuantizeDirection(direction.y),
                Data2 = 0
            };
        }

        /// <summary>
        /// Create a movement direction change command
        /// </summary>
        public static GameCommand MoveChange(uint seq, Vector2 direction)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Movement,
                Action = MovementAction.Change,
                Data0 = QuantizeDirection(direction.x),
                Data1 = QuantizeDirection(direction.y),
                Data2 = 0
            };
        }

        /// <summary>
        /// Create a movement stop command
        /// </summary>
        public static GameCommand MoveStop(uint seq)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Movement,
                Action = MovementAction.Stop,
                Data0 = 0,
                Data1 = 0,
                Data2 = 0
            };
        }

        /// <summary>
        /// Create a jump command (discrete event, no data — server computes velocity from SimConfig)
        /// </summary>
        public static GameCommand Jump(uint seq)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Movement,
                Action = MovementAction.Jump,
                Data0 = 0,
                Data1 = 0,
                Data2 = 0
            };
        }

        #endregion

        #region Factory Methods - Abilities

        /// <summary>
        /// Create an ability cast command
        /// </summary>
        public static GameCommand CastAbility(uint seq, byte abilitySlot, Vector2 targetPos, uint targetEntityId = 0)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Ability,
                Action = abilitySlot,
                Data0 = QuantizePosition(targetPos.x),
                Data1 = QuantizePosition(targetPos.y),
                Data2 = targetEntityId
            };
        }

        /// <summary>
        /// Cancel current ability cast
        /// </summary>
        public static GameCommand CancelAbility(uint seq)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Ability,
                Action = AbilityAction.Cancel,
                Data0 = 0,
                Data1 = 0,
                Data2 = 0
            };
        }

        #endregion

        #region Factory Methods - Attack

        /// <summary>
        /// Start auto-attacking (nearest enemy)
        /// </summary>
        public static GameCommand AttackStart(uint seq)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Attack,
                Action = AttackAction.Start,
                Data0 = 0,
                Data1 = 0,
                Data2 = 0
            };
        }

        /// <summary>
        /// Stop auto-attacking
        /// </summary>
        public static GameCommand AttackStop(uint seq)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Attack,
                Action = AttackAction.Stop,
                Data0 = 0,
                Data1 = 0,
                Data2 = 0
            };
        }

        /// <summary>
        /// Attack a specific target
        /// </summary>
        public static GameCommand AttackTarget(uint seq, uint targetEntityId)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Attack,
                Action = AttackAction.Target,
                Data0 = 0,
                Data1 = 0,
                Data2 = targetEntityId
            };
        }

        /// <summary>
        /// Attack-move to position (attack enemies on the way)
        /// </summary>
        public static GameCommand AttackMove(uint seq, Vector2 position)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Attack,
                Action = AttackAction.Move,
                Data0 = QuantizePosition(position.x),
                Data1 = QuantizePosition(position.y),
                Data2 = 0
            };
        }

        #endregion

        #region Factory Methods - Items

        /// <summary>
        /// Use an item from inventory slot
        /// </summary>
        public static GameCommand UseItem(uint seq, byte slot, uint targetEntityId = 0)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Item,
                Action = ItemAction.Use,
                Data0 = slot,
                Data1 = 0,
                Data2 = targetEntityId
            };
        }

        /// <summary>
        /// Drop an item from inventory slot
        /// </summary>
        public static GameCommand DropItem(uint seq, byte slot)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Item,
                Action = ItemAction.Drop,
                Data0 = slot,
                Data1 = 0,
                Data2 = 0
            };
        }

        /// <summary>
        /// Swap items between two slots
        /// </summary>
        public static GameCommand SwapItems(uint seq, byte fromSlot, byte toSlot)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.Item,
                Action = ItemAction.Swap,
                Data0 = fromSlot,
                Data1 = toSlot,
                Data2 = 0
            };
        }

        #endregion

        #region Factory Methods - System

        /// <summary>
        /// Create a class selection command
        /// </summary>
        public static GameCommand ClassSelect(uint seq, byte classId)
        {
            return new GameCommand
            {
                Sequence = seq,
                Category = CommandCategory.System,
                Action = SystemAction.ClassSelect,
                Data0 = classId,
                Data1 = 0,
                Data2 = 0
            };
        }

        #endregion

        #region Quantization Helpers

        /// <summary>
        /// Quantize direction component (-1 to 1) to short (-127 to 127)
        /// </summary>
        public static short QuantizeDirection(float v)
        {
            return (short)(Mathf.Clamp(v, -1f, 1f) * 127f);
        }

        /// <summary>
        /// Dequantize direction component from short to float
        /// </summary>
        public static float DequantizeDirection(short v)
        {
            return v / 127f;
        }

        /// <summary>
        /// Quantize world position to short (0.1 unit precision, ±3276 range)
        /// </summary>
        public static short QuantizePosition(float v)
        {
            return (short)Mathf.Clamp(v * 10f, short.MinValue, short.MaxValue);
        }

        /// <summary>
        /// Dequantize position from short to float
        /// </summary>
        public static float DequantizePosition(short v)
        {
            return v / 10f;
        }

        #endregion

        #region Accessors

        /// <summary>
        /// Get direction from Data0/Data1 (for movement commands)
        /// </summary>
        public Vector2 GetDirection()
        {
            return new Vector2(
                DequantizeDirection(Data0),
                DequantizeDirection(Data1)
            );
        }

        /// <summary>
        /// Get target position from Data0/Data1 (for ability/attack-move commands)
        /// </summary>
        public Vector2 GetTargetPosition()
        {
            return new Vector2(
                DequantizePosition(Data0),
                DequantizePosition(Data1)
            );
        }

        /// <summary>
        /// Get target entity ID from Data2
        /// </summary>
        public uint GetTargetEntityId() => Data2;

        /// <summary>
        /// Get item/ability slot from Data0
        /// </summary>
        public byte GetSlot() => (byte)Data0;

        #endregion

        public override string ToString()
        {
            return $"Cmd[{Sequence}] {Category}.{Action} D0={Data0} D1={Data1} D2={Data2}";
        }
    }
}

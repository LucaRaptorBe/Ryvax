// SimCommand.cs - Simulation-side command structure
// Decoupled from network layer, dispatched to handlers by category

using UnityEngine;
using MOBANet.Shared;

namespace MOBANet.GameSim.Commands
{
    /// <summary>
    /// Simulation command - decoupled from network.
    /// Created from GameCommand and dispatched to appropriate handler.
    /// Contains interpreted data fields for each command type.
    /// </summary>
    public struct SimCommand
    {
        /// <summary>
        /// Sequence number for ordering and ack
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
        /// Tick when this command was issued (for lag compensation)
        /// </summary>
        public uint IssuedTick;

        /// <summary>
        /// Direction vector (for movement, dash, directional abilities)
        /// </summary>
        public Vector3 Direction;

        /// <summary>
        /// Target position in world space (for abilities, attack-move)
        /// </summary>
        public Vector3 TargetPosition;

        /// <summary>
        /// Target entity ID (for targeted abilities, attacks)
        /// </summary>
        public uint TargetEntityId;

        /// <summary>
        /// Slot number (for items, abilities)
        /// </summary>
        public byte Slot;

        /// <summary>
        /// Secondary slot (for item swap)
        /// </summary>
        public byte SecondarySlot;

        /// <summary>
        /// Empty/null command
        /// </summary>
        public static readonly SimCommand Empty = new SimCommand { Category = CommandCategory.None };

        #region Category Checks

        /// <summary>
        /// Is this a movement command?
        /// </summary>
        public bool IsMovement => Category == CommandCategory.Movement;

        /// <summary>
        /// Is this an ability command?
        /// </summary>
        public bool IsAbility => Category == CommandCategory.Ability;

        /// <summary>
        /// Is this an attack command?
        /// </summary>
        public bool IsAttack => Category == CommandCategory.Attack;

        /// <summary>
        /// Is this an item command?
        /// </summary>
        public bool IsItem => Category == CommandCategory.Item;

        /// <summary>
        /// Is this a valid (non-empty) command?
        /// </summary>
        public bool IsValid => Category != CommandCategory.None;

        #endregion

        #region Movement Helpers

        /// <summary>
        /// Is this a move start or change command?
        /// </summary>
        public bool IsMoveStart => IsMovement && (Action == MovementAction.Start || Action == MovementAction.Change);

        /// <summary>
        /// Is this a move stop command?
        /// </summary>
        public bool IsMoveStop => IsMovement && Action == MovementAction.Stop;

        /// <summary>
        /// Is this a jump command?
        /// </summary>
        public bool IsJump => IsMovement && Action == MovementAction.Jump;

        #endregion

        public override string ToString()
        {
            return $"SimCmd[{Sequence}] {Category}.{Action} Dir={Direction:F2} Pos={TargetPosition:F1} Target={TargetEntityId}";
        }
    }
}

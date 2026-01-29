// CommandHelper.cs - Conversion between network and simulation commands

using UnityEngine;
using MOBANet.Shared;
using MOBANet.NetAdapter.Messages;
using MOBANet.GameSim.Commands;

namespace MOBANet.NetAdapter
{
    /// <summary>
    /// Helper class for converting between GameCommand (network) and SimCommand (simulation).
    /// Keeps network and simulation layers decoupled.
    /// </summary>
    public static class CommandHelper
    {
        /// <summary>
        /// Convert a network GameCommand to a simulation SimCommand.
        /// Called on both client (for prediction) and server (for authoritative sim).
        /// </summary>
        public static SimCommand ToSimCommand(in GameCommand cmd, uint currentTick = 0)
        {
            var simCmd = new SimCommand
            {
                Sequence = cmd.Sequence,
                Category = cmd.Category,
                Action = cmd.Action,
                IssuedTick = currentTick
            };

            // Interpret data fields based on category
            switch (cmd.Category)
            {
                case CommandCategory.Movement:
                    InterpretMovementCommand(ref simCmd, cmd);
                    break;

                case CommandCategory.Attack:
                    InterpretAttackCommand(ref simCmd, cmd);
                    break;

                case CommandCategory.Ability:
                    InterpretAbilityCommand(ref simCmd, cmd);
                    break;

                case CommandCategory.Item:
                    InterpretItemCommand(ref simCmd, cmd);
                    break;
            }

            return simCmd;
        }

        private static void InterpretMovementCommand(ref SimCommand simCmd, in GameCommand cmd)
        {
            // Direction is stored in Data0 (X) and Data1 (Z)
            Vector2 dir2D = cmd.GetDirection();
            simCmd.Direction = new Vector3(dir2D.x, 0f, dir2D.y);
        }

        private static void InterpretAttackCommand(ref SimCommand simCmd, in GameCommand cmd)
        {
            switch (cmd.Action)
            {
                case AttackAction.Target:
                    simCmd.TargetEntityId = cmd.Data2;
                    break;

                case AttackAction.Move:
                    Vector2 pos2D = cmd.GetTargetPosition();
                    simCmd.TargetPosition = new Vector3(pos2D.x, 0f, pos2D.y);
                    break;
            }
        }

        private static void InterpretAbilityCommand(ref SimCommand simCmd, in GameCommand cmd)
        {
            // Slot is the action itself (CastQ=1, CastW=2, etc.)
            simCmd.Slot = cmd.Action;

            // Special case: Launch command stores velocity in Direction
            if (cmd.Action == AbilityAction.Launch)
            {
                simCmd.Direction = cmd.GetLaunchVelocity();
                return;
            }

            // Target position from Data0/Data1
            Vector2 pos2D = cmd.GetTargetPosition();
            simCmd.TargetPosition = new Vector3(pos2D.x, 0f, pos2D.y);

            // Target entity from Data2
            simCmd.TargetEntityId = cmd.Data2;

            // Direction can be computed from target position relative to player
            // (done at execution time when we have player position)
        }

        private static void InterpretItemCommand(ref SimCommand simCmd, in GameCommand cmd)
        {
            simCmd.Slot = cmd.GetSlot();
            simCmd.TargetEntityId = cmd.Data2;

            if (cmd.Action == ItemAction.Swap)
            {
                simCmd.SecondarySlot = (byte)cmd.Data1;
            }
        }

        /// <summary>
        /// Convert a SimCommand back to a GameCommand (if needed for replay/debug).
        /// </summary>
        public static GameCommand ToGameCommand(in SimCommand simCmd)
        {
            var cmd = new GameCommand
            {
                Sequence = simCmd.Sequence,
                Category = simCmd.Category,
                Action = simCmd.Action
            };

            switch (simCmd.Category)
            {
                case CommandCategory.Movement:
                    cmd.Data0 = GameCommand.QuantizeDirection(simCmd.Direction.x);
                    cmd.Data1 = GameCommand.QuantizeDirection(simCmd.Direction.z);
                    break;

                case CommandCategory.Attack:
                    if (simCmd.Action == AttackAction.Move)
                    {
                        cmd.Data0 = GameCommand.QuantizePosition(simCmd.TargetPosition.x);
                        cmd.Data1 = GameCommand.QuantizePosition(simCmd.TargetPosition.z);
                    }
                    else
                    {
                        cmd.Data2 = simCmd.TargetEntityId;
                    }
                    break;

                case CommandCategory.Ability:
                    if (simCmd.Action == AbilityAction.Launch)
                    {
                        // Launch: velocity stored in Direction
                        cmd.Data0 = GameCommand.QuantizePosition(simCmd.Direction.x);
                        cmd.Data1 = GameCommand.QuantizePosition(simCmd.Direction.z);
                        cmd.Data2 = (uint)(ushort)GameCommand.QuantizePosition(simCmd.Direction.y);
                    }
                    else
                    {
                        cmd.Data0 = GameCommand.QuantizePosition(simCmd.TargetPosition.x);
                        cmd.Data1 = GameCommand.QuantizePosition(simCmd.TargetPosition.z);
                        cmd.Data2 = simCmd.TargetEntityId;
                    }
                    break;

                case CommandCategory.Item:
                    cmd.Data0 = simCmd.Slot;
                    cmd.Data1 = simCmd.SecondarySlot;
                    cmd.Data2 = simCmd.TargetEntityId;
                    break;
            }

            return cmd;
        }
    }
}

// MovementHandler.cs - Handles movement commands

using UnityEngine;
using MOBANet.Shared;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;

namespace MOBANet.GameSim.Commands.Handlers
{
    /// <summary>
    /// Handles movement commands: Start, Change, Stop, Jump.
    /// Called when player presses/releases WASD keys or presses Space.
    /// </summary>
    public class MovementHandler : ICommandHandler
    {
        public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
        {
            if (!player.Stats.IsAlive) return;

            switch (cmd.Action)
            {
                case MovementAction.Start:
                case MovementAction.Change:
                    // Set movement direction - player will move in Tick()
                    player.SetMoveDirection(cmd.Direction);
                    // Debug.Log($"[MovementHandler] Player {player.Id} MoveStart/Change: dir={cmd.Direction}, velocity after={player.Transform.Velocity}");
                    break;

                case MovementAction.Stop:
                    // Stop moving
                    player.StopMoving();
                    // Debug.Log($"[MovementHandler] Player {player.Id} MoveStop");
                    break;

                case MovementAction.Jump:
                    float moveSpeed = config.PlayerMoveSpeed * player.Stats.MoveSpeedModifier;
                    MovementEngine.ApplyJump(ref player.Transform, moveSpeed, config);
                    break;
            }
        }
    }
}

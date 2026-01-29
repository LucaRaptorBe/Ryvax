// MovementHandler.cs - Handles movement commands

using UnityEngine;
using MOBANet.Shared;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;

namespace MOBANet.GameSim.Commands.Handlers
{
    /// <summary>
    /// Handles movement commands: Start, Change, Stop, Dash.
    /// Called when player presses/releases WASD keys.
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
                    Debug.Log($"[MovementHandler] Player {player.Id} MoveStart/Change: dir={cmd.Direction}, velocity after={player.Transform.Velocity}");
                    break;

                case MovementAction.Stop:
                    // Stop moving
                    player.StopMoving();
                    Debug.Log($"[MovementHandler] Player {player.Id} MoveStop");
                    break;

                case MovementAction.Dash:
                    // TODO: Implement dash ability in new architecture
                    // Dash is now handled via Launch command (similar to Jump)
                    // player.ApplyLaunchVelocity(cmd.Direction * dashSpeed);
                    break;
            }
        }
    }
}

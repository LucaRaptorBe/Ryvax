// ICommandHandler.cs - Interface for command handlers

using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;

namespace MOBANet.GameSim.Commands
{
    /// <summary>
    /// Interface for command handlers.
    /// Each handler processes a specific category of commands.
    /// Extensible: add new handlers for new game features.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>
        /// Execute a command for a player.
        /// Called by CommandDispatcher when a matching command is received.
        /// </summary>
        /// <param name="player">The player executing the command</param>
        /// <param name="cmd">The command to execute</param>
        /// <param name="world">The simulation world</param>
        /// <param name="config">Simulation configuration</param>
        void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config);
    }
}

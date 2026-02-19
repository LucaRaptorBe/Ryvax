// SystemHandler.cs - Handles system commands (class selection, etc.)
// Raises SimEvents for network broadcast by ServerGameLoop

using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.GameSim.Events;
using MOBANet.Shared;

namespace MOBANet.GameSim.Commands.Handlers
{
    /// <summary>
    /// Handles system commands: ClassSelect, etc.
    /// Sets simulation state and raises SimEvents for network broadcast.
    /// </summary>
    public class SystemHandler : ICommandHandler
    {
        public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
        {
            switch (cmd.Action)
            {
                case SystemAction.ClassSelect:
                    player.ClassId = cmd.Slot;

                    world.RaiseEvent(new SimEvent
                    {
                        Type = SimEventType.ClassAssign,
                        EntityId = player.Id,
                        Data1 = cmd.Slot
                    });
                    break;
            }
        }
    }
}

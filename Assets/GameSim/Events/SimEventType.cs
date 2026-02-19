// SimEventType.cs - Event types raised by GameSim handlers
// PURE C# - No FishNet dependencies

namespace MOBANet.GameSim.Events
{
    /// <summary>
    /// Types of events raised during simulation.
    /// Drained by ServerGameLoop and converted to ReliableEvents for network broadcast.
    /// </summary>
    public enum SimEventType : byte
    {
        None = 0,
        AbilityUsed = 1,
        DamageDealt = 2,
        EntityDeath = 3,
        EntityRespawn = 4,
        ClassAssign = 5,
    }
}

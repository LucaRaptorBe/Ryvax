// CommandTypes.cs - Shared command enums and constants
// Used by both GameSim (simulation) and NetAdapter (network) layers

namespace MOBANet.Shared
{
    /// <summary>
    /// Command categories - extensible for new game features.
    /// Values are grouped by range for organization.
    /// </summary>
    public enum CommandCategory : byte
    {
        None = 0,

        // === Movement (1-19) ===
        Movement = 1,

        // === Combat (20-39) ===
        Attack = 20,
        Ability = 21,

        // === Items (40-59) ===
        Item = 40,
        Shop = 41,

        // === Social (60-79) ===
        Ping = 60,
        Emote = 61,

        // === System (200+) ===
        System = 200,
    }

    /// <summary>
    /// Movement actions
    /// </summary>
    public static class MovementAction
    {
        public const byte Start = 1;
        public const byte Change = 2;
        public const byte Stop = 3;
        public const byte Jump = 5;
    }

    /// <summary>
    /// Ability actions (slot-based)
    /// </summary>
    public static class AbilityAction
    {
        public const byte CastQ = 1;
        public const byte CastW = 2;
        public const byte CastE = 3;
        public const byte CastR = 4;
        public const byte CastSummoner1 = 10;
        public const byte CastSummoner2 = 11;
        public const byte Cancel = 99;
    }

    /// <summary>
    /// Item actions
    /// </summary>
    public static class ItemAction
    {
        public const byte Use = 1;
        public const byte Drop = 2;
        public const byte Swap = 3;
    }

    /// <summary>
    /// Attack actions
    /// </summary>
    public static class AttackAction
    {
        public const byte Start = 1;
        public const byte Stop = 2;
        public const byte Target = 3;
        public const byte Move = 4;  // Attack-move
    }

    /// <summary>
    /// Ping actions (METRIC B: RTT measurement)
    /// </summary>
    public static class PingAction
    {
        public const byte Request = 1;   // Client → Server ping request
        public const byte Response = 2;  // Server → Client pong response (via ReliableEvent)
    }

    /// <summary>
    /// System actions (class selection, etc.)
    /// </summary>
    public static class SystemAction
    {
        public const byte ClassSelect = 1;
    }
}

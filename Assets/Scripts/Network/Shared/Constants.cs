// Constants.cs - Global constants for the MOBA netcode prototype

namespace MOBANet.Shared
{
    /// <summary>
    /// Network-related constants
    /// </summary>
    public static class NetworkConstants
    {
        /// <summary>
        /// Default server port
        /// </summary>
        public const ushort DEFAULT_PORT = 7777;

        /// <summary>
        /// Maximum clients per server
        /// </summary>
        public const int MAX_CLIENTS = 100;

        /// <summary>
        /// Maximum entities per snapshot
        /// </summary>
        public const int MAX_ENTITIES_PER_SNAPSHOT = 128;

        /// <summary>
        /// Default snapshot rate (Hz)
        /// </summary>
        public const float DEFAULT_SNAPSHOT_RATE = 20f;

        /// <summary>
        /// Default interpolation delay (seconds)
        /// </summary>
        public const float DEFAULT_INTERPOLATION_DELAY = 0.1f;
    }

    /// <summary>
    /// Simulation-related constants
    /// </summary>
    public static class SimulationConstants
    {
        /// <summary>
        /// Simulation tick rate (Hz)
        /// </summary>
        public const int TICK_RATE = 30;

        /// <summary>
        /// Fixed delta time per tick
        /// </summary>
        public const float TICK_DELTA = 1f / TICK_RATE;

        /// <summary>
        /// Default arena width
        /// </summary>
        public const float ARENA_WIDTH = 200f;

        /// <summary>
        /// Default arena height (depth)
        /// </summary>
        public const float ARENA_HEIGHT = 200f;

        /// <summary>
        /// Maximum Y height
        /// </summary>
        public const float MAX_HEIGHT = 20f;
    }

    /// <summary>
    /// Team constants
    /// </summary>
    public static class Teams
    {
        public const byte NEUTRAL = 0;
        public const byte TEAM_1 = 1;
        public const byte TEAM_2 = 2;
    }

    /// <summary>
    /// Layer masks and tags
    /// </summary>
    public static class Layers
    {
        public const string PLAYER = "Player";
        public const string PROJECTILE = "Projectile";
        public const string GROUND = "Ground";
        public const string OBSTACLE = "Obstacle";
    }
}

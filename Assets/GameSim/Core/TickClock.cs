// TickClock.cs - Fixed timestep simulation clock
// PURE C# - No Unity/FishNet dependencies (except for basic math)
// This is the heart of deterministic simulation

using MOBANet.Shared;

namespace MOBANet.GameSim.Core
{
    /// <summary>
    /// Fixed timestep simulation clock.
    /// Manages tick counting and time accumulation for deterministic simulation.
    /// </summary>
    public class TickClock
    {
        /// <summary>
        /// Simulation tick rate (ticks per second).
        /// Defined in NetcodeConstants for centralized configuration.
        /// </summary>
        public const int TICK_RATE = NetcodeConstants.TICK_RATE;

        /// <summary>
        /// Fixed delta time per tick.
        /// </summary>
        public const float TICK_DELTA = NetcodeConstants.TICK_DELTA;

        /// <summary>
        /// Tick delta in milliseconds.
        /// </summary>
        public const float TICK_DELTA_MS = NetcodeConstants.TICK_DELTA_MS;

        /// <summary>
        /// Current simulation tick (monotonically increasing)
        /// </summary>
        public uint CurrentTick { get; private set; }

        /// <summary>
        /// Fixed delta time per tick
        /// </summary>
        public float TickDelta => TICK_DELTA;

        /// <summary>
        /// Tick rate in Hz
        /// </summary>
        public int TickRate => TICK_RATE;

        /// <summary>
        /// Time accumulator for frame-independent simulation
        /// </summary>
        private float _accumulator;

        /// <summary>
        /// Create a new tick clock
        /// </summary>
        /// <param name="startTick">Initial tick value</param>
        public TickClock(uint startTick = 0)
        {
            CurrentTick = startTick;
            _accumulator = 0f;
        }

        /// <summary>
        /// Accumulate frame time and return number of ticks to simulate.
        /// Call this each Unity Update/FixedUpdate with deltaTime.
        /// </summary>
        /// <param name="deltaTime">Time since last frame</param>
        /// <returns>Number of simulation ticks to run</returns>
        public int Accumulate(float deltaTime)
        {
            _accumulator += deltaTime;
            int ticksToRun = 0;

            while (_accumulator >= TICK_DELTA)
            {
                _accumulator -= TICK_DELTA;
                ticksToRun++;
            }

            // Prevent spiral of death: cap max ticks per frame
            const int MAX_TICKS_PER_FRAME = 5;
            if (ticksToRun > MAX_TICKS_PER_FRAME)
            {
                // Discard excess time
                _accumulator = 0f;
                ticksToRun = MAX_TICKS_PER_FRAME;
            }

            return ticksToRun;
        }

        /// <summary>
        /// Advance the tick counter by one.
        /// Call this after each simulation step.
        /// </summary>
        public void Advance()
        {
            CurrentTick++;
        }

        /// <summary>
        /// Advance by multiple ticks (used for fast-forward/catchup)
        /// </summary>
        public void Advance(int count)
        {
            CurrentTick += (uint)count;
        }

        /// <summary>
        /// Reset to specific tick (used for reconciliation).
        /// Does NOT reset accumulator.
        /// </summary>
        public void SetTick(uint tick)
        {
            CurrentTick = tick;
        }

        /// <summary>
        /// Reset tick counter and accumulator
        /// </summary>
        public void Reset(uint startTick = 0)
        {
            CurrentTick = startTick;
            _accumulator = 0f;
        }

        /// <summary>
        /// Interpolation alpha for visual smoothing (0-1).
        /// Use this to interpolate between previous and current state for rendering.
        /// </summary>
        public float InterpolationAlpha => _accumulator / TICK_DELTA;

        /// <summary>
        /// Current simulation time in seconds
        /// </summary>
        public float SimulationTime => CurrentTick * TICK_DELTA;

        /// <summary>
        /// Convert tick to time in seconds
        /// </summary>
        public static float TickToTime(uint tick) => tick * TICK_DELTA;

        /// <summary>
        /// Convert time to tick (rounds down)
        /// </summary>
        public static uint TimeToTick(float time) => (uint)(time / TICK_DELTA);

        /// <summary>
        /// Get tick difference (handles wrap-around for very long sessions)
        /// </summary>
        public static int GetTickDifference(uint a, uint b)
        {
            return (int)(a - b);
        }

        /// <summary>
        /// Check if tick A is after tick B (handles wrap-around)
        /// </summary>
        public static bool IsAfter(uint a, uint b)
        {
            return GetTickDifference(a, b) > 0;
        }

        /// <summary>
        /// Check if tick A is before tick B (handles wrap-around)
        /// </summary>
        public static bool IsBefore(uint a, uint b)
        {
            return GetTickDifference(a, b) < 0;
        }
    }
}

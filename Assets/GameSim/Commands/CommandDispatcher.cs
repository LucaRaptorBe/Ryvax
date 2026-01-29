// CommandDispatcher.cs - Routes commands to appropriate handlers

using System.Collections.Generic;
using MOBANet.Shared;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.GameSim.Commands.Handlers;

namespace MOBANet.GameSim.Commands
{
    /// <summary>
    /// Routes commands to appropriate handlers based on Category.
    /// Extensible: add new handlers for new command types.
    ///
    /// Pattern: Strategy pattern with dictionary dispatch.
    /// New command types only require adding a new handler.
    /// </summary>
    public class CommandDispatcher
    {
        private readonly SimWorld _world;
        private readonly SimConfig _config;

        // Handlers registered by category
        private readonly Dictionary<CommandCategory, ICommandHandler> _handlers = new();

        public CommandDispatcher(SimWorld world, SimConfig config)
        {
            _world = world;
            _config = config;

            // Register default handlers
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Register default command handlers.
        /// Override in subclass for custom game logic.
        /// </summary>
        protected virtual void RegisterDefaultHandlers()
        {
            RegisterHandler(CommandCategory.Movement, new MovementHandler());
            // TODO: Create new handlers in component-based architecture
            // RegisterHandler(CommandCategory.Attack, new AttackHandler());
            // RegisterHandler(CommandCategory.Ability, new AbilityHandler());
            // RegisterHandler(CommandCategory.Item, new ItemHandler());
        }

        /// <summary>
        /// Register a command handler for a category.
        /// Replaces existing handler if one is already registered.
        /// </summary>
        public void RegisterHandler(CommandCategory category, ICommandHandler handler)
        {
            _handlers[category] = handler;
        }

        /// <summary>
        /// Unregister a command handler.
        /// </summary>
        public bool UnregisterHandler(CommandCategory category)
        {
            return _handlers.Remove(category);
        }

        /// <summary>
        /// Check if a handler is registered for a category.
        /// </summary>
        public bool HasHandler(CommandCategory category)
        {
            return _handlers.ContainsKey(category);
        }

        /// <summary>
        /// Dispatch a command to the appropriate handler.
        /// </summary>
        /// <param name="player">The player executing the command</param>
        /// <param name="cmd">The command to execute</param>
        /// <returns>True if a handler was found and executed</returns>
        public bool Execute(SimPlayer player, in SimCommand cmd)
        {
            if (player == null || !cmd.IsValid)
                return false;

            if (_handlers.TryGetValue(cmd.Category, out var handler))
            {
                handler.Execute(player, cmd, _world, _config);
                return true;
            }

            // No handler registered for this category
            UnityEngine.Debug.LogWarning($"[CommandDispatcher] No handler for category: {cmd.Category}");
            return false;
        }

        /// <summary>
        /// Get the number of registered handlers.
        /// </summary>
        public int HandlerCount => _handlers.Count;
    }
}

// SnapshotHelper.cs - Helper methods for snapshot creation/application
// Bridges GameSim entities with network messages

using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter.Messages;

// Alias to avoid ambiguity with GameSim.Entities.EntityState
using NetEntityState = MOBANet.NetAdapter.Messages.EntityState;

namespace MOBANet.NetAdapter
{
    /// <summary>
    /// Helper class for creating and applying snapshots.
    /// Bridges the GameSim layer with network messages.
    /// </summary>
    public static class SnapshotHelper
    {
        /// <summary>
        /// Create a snapshot from the simulation world
        /// </summary>
        public static SnapshotDelta CreateSnapshot(SimWorld world, int forClientId, uint ackInputSeq, uint ackMovementSeq = 0)
        {
            var entities = new System.Collections.Generic.List<NetEntityState>();

            float playerMoveSpeed = world.Config?.PlayerMoveSpeed ?? 8f;
            foreach (var entity in world.AllEntities)
            {
                entities.Add(EntityStateFromSimEntity(entity, playerMoveSpeed));
            }

            return new SnapshotDelta
            {
                ServerTick = world.Clock.CurrentTick,
                AckInputSeq = ackInputSeq,
                AckMovementSeq = ackMovementSeq,
                EntityCount = (ushort)entities.Count,
                Entities = entities.ToArray()
            };
        }

        /// <summary>
        /// Create a filtered snapshot containing only entities visible to a client (AOI).
        /// </summary>
        /// <param name="world">Simulation world</param>
        /// <param name="visibleEntities">Set of entity IDs visible to the client</param>
        /// <param name="ackInputSeq">Last acknowledged input sequence</param>
        /// <param name="localEntityId">The client's own entity (always included)</param>
        /// <param name="ackMovementSeq">Last acknowledged movement sequence</param>
        public static SnapshotDelta CreateFilteredSnapshot(
            SimWorld world,
            System.Collections.Generic.IReadOnlyCollection<uint> visibleEntities,
            uint ackInputSeq,
            uint localEntityId,
            uint ackMovementSeq = 0)
        {
            var entities = new System.Collections.Generic.List<NetEntityState>();

            float playerMoveSpeed = world.Config?.PlayerMoveSpeed ?? 8f;

            // Always include local player's entity
            var localEntity = world.GetEntity(localEntityId);
            if (localEntity != null)
            {
                entities.Add(EntityStateFromSimEntity(localEntity, playerMoveSpeed));
            }

            // Add visible entities (skip if already added as local)
            foreach (var entityId in visibleEntities)
            {
                if (entityId == localEntityId) continue;

                var entity = world.GetEntity(entityId);
                if (entity != null)
                {
                    entities.Add(EntityStateFromSimEntity(entity, playerMoveSpeed));
                }
            }

            return new SnapshotDelta
            {
                ServerTick = world.Clock.CurrentTick,
                AckInputSeq = ackInputSeq,
                AckMovementSeq = ackMovementSeq,
                EntityCount = (ushort)entities.Count,
                Entities = entities.ToArray()
            };
        }

        /// <summary>
        /// Apply a snapshot to the simulation world
        /// </summary>
        public static void ApplySnapshot(SimWorld world, in SnapshotDelta snapshot)
        {
            world.Clock.SetTick(snapshot.ServerTick);

            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                ref readonly var state = ref snapshot.Entities[i];
                var entity = world.GetEntity(state.EntityId);

                if (entity != null)
                {
                    // ApplyNetworkState is now entity-specific (component-based states)
                    if (entity is SimPlayer player)
                    {
                        player.ApplyNetworkState(state.Position, state.Velocity, state.Rotation);
                    }
                    // TODO: Add other entity types (Minion, Tower, etc.) when needed
                }
            }
        }

        /// <summary>
        /// Convert SimEntity to network EntityState
        /// </summary>
        public static NetEntityState EntityStateFromSimEntity(SimEntity entity, float playerMoveSpeed = 8f)
        {
            // Component-based states: extract data from specific entity type
            if (entity is SimPlayer player)
            {
                // Calculate effective move speed
                float effectiveSpeed = playerMoveSpeed * player.Stats.MoveSpeedModifier;

                // Build event flags from player state
                var eventFlags = Messages.EntityEventFlags.None;
                // TODO: Add CC flags, blink/teleport events when implemented

                return NetEntityState.FromSimEntity(
                    entity.Id,
                    (byte)entity.Type,
                    player.Transform.Position,
                    player.Transform.RotationY,
                    player.Stats.Health,
                    (byte)entity.State,
                    player.Stats.IsAlive,
                    player.Transform.Velocity,
                    effectiveSpeed,
                    eventFlags
                );
            }

            // TODO: Add other entity types (Minion, Tower, etc.) when needed
            // For now, return empty state for unsupported types
            return new NetEntityState
            {
                EntityId = entity.Id,
                EntityType = (byte)entity.Type,
                State = (byte)entity.State
            };
        }
    }
}

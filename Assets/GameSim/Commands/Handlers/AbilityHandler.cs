// AbilityHandler.cs - Server-side ability execution via CommandDispatcher
// Validates cast, spawns projectiles, sets cooldowns, raises SimEvents

using UnityEngine;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.GameSim.Events;
using MOBANet.Shared;

namespace MOBANet.GameSim.Commands.Handlers
{
    /// <summary>
    /// Handles ability commands (CastQ, CastW, etc.).
    /// Executes cast logic and raises AbilityUsed SimEvent for network broadcast.
    /// </summary>
    public class AbilityHandler : ICommandHandler
    {
        public void Execute(SimPlayer player, in SimCommand cmd, SimWorld world, SimConfig config)
        {
            byte slot = cmd.Action;
            Vector3 targetPos = cmd.TargetPosition;

            bool success = TryCastAbility(player, slot, targetPos, world, config);

            if (success)
            {
                Vector3 dir = targetPos - player.Transform.Position;
                dir.y = 0;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;

                world.RaiseEvent(new SimEvent
                {
                    Type = SimEventType.AbilityUsed,
                    EntityId = player.Id,
                    Data1 = slot,
                    Direction = dir
                });
            }
        }

        private static bool TryCastAbility(SimPlayer player, byte slot, Vector3 targetPos, SimWorld world, SimConfig config)
        {
            if (!player.Abilities.CanCast(slot)) return false;

            // Only slot 0 (Piercing Shot) spawns a projectile for now
            if (slot != 0) return false;

            Vector3 dir = targetPos - player.Transform.Position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir = dir.normalized;

            // Set cooldown
            player.Abilities.Cooldowns[slot] = config.PiercingShotCooldown;

            // Spawn projectile slightly in front of caster
            var proj = new SimProjectile(
                player.Id,
                player.TeamId,
                player.Transform.Position + dir,
                dir,
                config.ProjectileSpeed,
                config.PiercingShotDamage,
                config.PiercingShotRange,
                config.ProjectileRadius
            );
            world.SpawnProjectile(proj);

            return true;
        }
    }
}

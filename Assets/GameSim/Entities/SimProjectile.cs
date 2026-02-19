// SimProjectile.cs - Server-side projectile simulation
// NOT a SimEntity - not in entity dictionary, not in snapshots
// Stored in SimWorld._projectiles list
// Clients get cosmetic visuals via AbilityUsed event

using UnityEngine;

namespace MOBANet.GameSim.Entities
{
    public class SimProjectile
    {
        public uint OwnerEntityId { get; }
        public byte TeamId { get; }
        public Vector3 Position { get; private set; }
        public Vector3 Direction { get; }
        public float Speed { get; }
        public float Damage { get; }
        public float MaxRange { get; }
        public float Radius { get; }
        public float DistanceTraveled { get; private set; }
        public bool IsExpired => DistanceTraveled >= MaxRange;

        public SimProjectile(uint ownerEntityId, byte teamId, Vector3 position, Vector3 direction,
            float speed, float damage, float maxRange, float radius)
        {
            OwnerEntityId = ownerEntityId;
            TeamId = teamId;
            Position = position;
            Direction = direction.normalized;
            Speed = speed;
            Damage = damage;
            MaxRange = maxRange;
            Radius = radius;
            DistanceTraveled = 0f;
        }

        public void Tick(float dt)
        {
            float distance = Speed * dt;
            Position += Direction * distance;
            DistanceTraveled += distance;
        }
    }
}

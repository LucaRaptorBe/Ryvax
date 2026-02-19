// SnapshotDelta.cs - Server snapshot sent to clients
// Contains all entity states, sent unreliable at 60Hz (= TICK_RATE)

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Server snapshot containing entity states.
    /// Sent unreliable at snapshot rate (60Hz = TICK_RATE).
    /// Packet loss is acceptable - clients dead-reckon between snapshots.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SnapshotDelta : INetMessage
    {
        public const byte MSG_ID = 2;
        public const ushort MSG_VERSION = 1;
        public const int MAX_ENTITIES = 128;

        public byte MessageId => MSG_ID;
        public ushort Version => MSG_VERSION;

        /// <summary>
        /// Server simulation tick this snapshot represents
        /// </summary>
        public uint ServerTick;

        /// <summary>
        /// Last processed input sequence for this client (for reconciliation)
        /// </summary>
        public uint AckInputSeq;

        /// <summary>
        /// Last processed movement sequence for this client.
        /// Used for movement reconciliation (separate from event command ACKs).
        /// </summary>
        public uint AckMovementSeq;

        /// <summary>
        /// Number of entities in this snapshot
        /// </summary>
        public ushort EntityCount;

        /// <summary>
        /// Entity states (variable length, up to MAX_ENTITIES)
        /// Note: For FishNet, this will be serialized separately
        /// </summary>
        public EntityState[] Entities;

        // Header size: 4 + 4 + 4 + 2 = 14 bytes
        // + EntityCount * sizeof(EntityState)
    }

    /// <summary>
    /// Compact entity state for network transmission.
    /// Uses 16-bit quantization for position (0.003 unit precision on 200x200 map).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EntityState
    {
        /// <summary>
        /// Unique entity identifier
        /// </summary>
        public uint EntityId;

        /// <summary>
        /// Entity type (Player, Projectile, Minion, etc.)
        /// </summary>
        public byte EntityType;

        /// <summary>
        /// Entity flags (Alive, Changed, Spawned, Destroyed)
        /// </summary>
        public byte Flags;

        /// <summary>
        /// X position quantized to 16-bit (map range)
        /// </summary>
        public ushort PosX;

        /// <summary>
        /// Y position (height) quantized to 16-bit (0-20 units)
        /// </summary>
        public ushort PosY;

        /// <summary>
        /// Z position quantized to 16-bit (map range)
        /// </summary>
        public ushort PosZ;

        /// <summary>
        /// Y rotation quantized to 16-bit (0-360 degrees)
        /// </summary>
        public ushort RotY;

        /// <summary>
        /// Current health
        /// </summary>
        public ushort Health;

        /// <summary>
        /// Current state (Idle, Moving, Attacking, etc.)
        /// </summary>
        public byte State;

        /// <summary>
        /// Horizontal velocity X component, quantized (0.01 u/s precision)
        /// Range: -327 to +327 u/s (plenty for 8 u/s base + buffs)
        /// </summary>
        public short VelX;

        /// <summary>
        /// Horizontal velocity Z component, quantized (0.01 u/s precision)
        /// </summary>
        public short VelZ;

        /// <summary>
        /// Vertical velocity Y component, quantized (0.01 u/s precision)
        /// Used for gravity/jump synchronization
        /// </summary>
        public short VelY;

        /// <summary>
        /// Current movement speed, quantized (0.1 u/s precision)
        /// Used for visual offset thresholds
        /// </summary>
        public ushort SpeedQ;

        /// <summary>
        /// Event flags (CC, blink, teleport, etc.)
        /// </summary>
        public byte EventFlags;

        /// <summary>
        /// Ability cooldowns (4 slots), quantized to byte (0-255 = 0-60 seconds, ~0.24s precision).
        /// Only meaningful for player entities.
        /// </summary>
        public byte Cd0, Cd1, Cd2, Cd3;

        // Total size: 4 + 1 + 1 + 2 + 2 + 2 + 2 + 2 + 1 + 2 + 2 + 2 + 2 + 1 + 4 = 30 bytes per entity

        #region Quantization Constants

        /// <summary>
        /// Map scale for position quantization (200x200 unit map)
        /// </summary>
        public const float MAP_SCALE = 200f;

        /// <summary>
        /// Height scale for Y position (0-20 units)
        /// </summary>
        public const float HEIGHT_SCALE = 20f;

        /// <summary>
        /// Quantization factor for 16-bit encoding
        /// </summary>
        public const float QUANT_FACTOR = 65535f;

        /// <summary>
        /// Velocity quantization scale (0.01 u/s precision)
        /// short range: -32768 to +32767 → -327 to +327 u/s
        /// </summary>
        public const float VELOCITY_SCALE = 100f;

        /// <summary>
        /// Max cooldown representable in a byte (seconds).
        /// 60s covers most abilities. Longer cooldowns clamp to 60.
        /// </summary>
        public const float COOLDOWN_MAX = 60f;

        #endregion

        #region Helpers

        /// <summary>
        /// Get dequantized world position
        /// </summary>
        public Vector3 Position => new Vector3(
            (PosX / QUANT_FACTOR) * MAP_SCALE - (MAP_SCALE / 2f),
            (PosY / QUANT_FACTOR) * HEIGHT_SCALE,
            (PosZ / QUANT_FACTOR) * MAP_SCALE - (MAP_SCALE / 2f)
        );

        /// <summary>
        /// Get dequantized rotation in degrees
        /// </summary>
        public float Rotation => (RotY / QUANT_FACTOR) * 360f;

        /// <summary>
        /// Get dequantized velocity (full 3D including vertical component)
        /// </summary>
        public Vector3 Velocity => new Vector3(
            VelX / VELOCITY_SCALE,
            VelY / VELOCITY_SCALE,
            VelZ / VELOCITY_SCALE
        );

        /// <summary>
        /// Check if entity is alive
        /// </summary>
        public bool IsAlive => (Flags & (byte)EntityFlags.Alive) != 0;

        /// <summary>
        /// Check if entity was just spawned
        /// </summary>
        public bool JustSpawned => (Flags & (byte)EntityFlags.Spawned) != 0;

        /// <summary>
        /// Check if entity was destroyed
        /// </summary>
        public bool WasDestroyed => (Flags & (byte)EntityFlags.Destroyed) != 0;

        /// <summary>
        /// Get dequantized movement speed
        /// </summary>
        public float Speed => SpeedQ / 10f;

        /// <summary>
        /// Get dequantized cooldown for ability slot (0-3).
        /// </summary>
        public float GetCooldown(int slot) => slot switch
        {
            0 => Cd0 / 255f * COOLDOWN_MAX,
            1 => Cd1 / 255f * COOLDOWN_MAX,
            2 => Cd2 / 255f * COOLDOWN_MAX,
            3 => Cd3 / 255f * COOLDOWN_MAX,
            _ => 0f
        };

        /// <summary>
        /// Check if entity is under immobilizing CC
        /// </summary>
        public bool HasImmobilizeCC => (EventFlags & (byte)EntityEventFlags.ImmobilizeCC) != 0;

        /// <summary>
        /// Check if entity just blinked
        /// </summary>
        public bool HasBlinkEvent => (EventFlags & (byte)EntityEventFlags.BlinkEvent) != 0;

        /// <summary>
        /// Check if entity just teleported
        /// </summary>
        public bool HasTeleportEvent => (EventFlags & (byte)EntityEventFlags.TeleportEvent) != 0;

        /// <summary>
        /// Check if entity state was reset
        /// </summary>
        public bool HasStateReset => (EventFlags & (byte)EntityEventFlags.StateReset) != 0;

        /// <summary>
        /// Check if entity is dashing
        /// </summary>
        public bool IsDashing => (EventFlags & (byte)EntityEventFlags.Dashing) != 0;

        /// <summary>
        /// Create EntityState from simulation entity
        /// </summary>
        public static EntityState FromSimEntity(
            uint entityId,
            byte entityType,
            Vector3 position,
            float rotationY,
            int health,
            byte state,
            bool isAlive,
            Vector3 velocity,
            float mapScale = MAP_SCALE)
        {
            return FromSimEntity(entityId, entityType, position, rotationY, health, state, isAlive, velocity, 0f, EntityEventFlags.None, null, mapScale);
        }

        /// <summary>
        /// Create EntityState from simulation entity with speed, event flags, and cooldowns
        /// </summary>
        public static EntityState FromSimEntity(
            uint entityId,
            byte entityType,
            Vector3 position,
            float rotationY,
            int health,
            byte state,
            bool isAlive,
            Vector3 velocity,
            float speed,
            EntityEventFlags eventFlags,
            float[] cooldowns = null,
            float mapScale = MAP_SCALE)
        {
            byte flags = 0;
            if (isAlive) flags |= (byte)EntityFlags.Alive;

            return new EntityState
            {
                EntityId = entityId,
                EntityType = entityType,
                Flags = flags,
                PosX = QuantizePosition(position.x, mapScale),
                PosY = QuantizeHeight(position.y),
                PosZ = QuantizePosition(position.z, mapScale),
                RotY = QuantizeRotation(rotationY),
                Health = (ushort)Mathf.Clamp(health, 0, ushort.MaxValue),
                State = state,
                VelX = QuantizeVelocity(velocity.x),
                VelZ = QuantizeVelocity(velocity.z),
                VelY = QuantizeVelocity(velocity.y),
                SpeedQ = (ushort)Mathf.Clamp(speed * 10f, 0, ushort.MaxValue),
                EventFlags = (byte)eventFlags,
                Cd0 = QuantizeCooldown(cooldowns, 0),
                Cd1 = QuantizeCooldown(cooldowns, 1),
                Cd2 = QuantizeCooldown(cooldowns, 2),
                Cd3 = QuantizeCooldown(cooldowns, 3),
            };
        }

        private static short QuantizeVelocity(float value)
        {
            return (short)Mathf.Clamp(value * VELOCITY_SCALE, short.MinValue, short.MaxValue);
        }

        private static ushort QuantizePosition(float value, float scale)
        {
            float normalized = (value + scale / 2f) / scale;
            return (ushort)(Mathf.Clamp01(normalized) * QUANT_FACTOR);
        }

        private static ushort QuantizeHeight(float value)
        {
            float normalized = value / HEIGHT_SCALE;
            return (ushort)(Mathf.Clamp01(normalized) * QUANT_FACTOR);
        }

        private static ushort QuantizeRotation(float degrees)
        {
            float normalized = ((degrees % 360f) + 360f) % 360f / 360f;
            return (ushort)(normalized * QUANT_FACTOR);
        }

        private static byte QuantizeCooldown(float[] cooldowns, int slot)
        {
            if (cooldowns == null || slot >= cooldowns.Length) return 0;
            float cd = cooldowns[slot];
            if (cd <= 0f) return 0;
            return (byte)Mathf.Clamp(cd / COOLDOWN_MAX * 255f, 1, 255);
        }

        #endregion
    }

    /// <summary>
    /// Entity state flags
    /// </summary>
    [Flags]
    public enum EntityFlags : byte
    {
        None = 0,
        Alive = 1 << 0,
        Changed = 1 << 1,
        Spawned = 1 << 2,
        Destroyed = 1 << 3,
        Owned = 1 << 4,      // Entity is owned by receiving client
        Relevant = 1 << 5,   // Entity is relevant to receiving client
        // 6-7 reserved
    }

    /// <summary>
    /// Extended entity flags for CC and events.
    /// Sent as a separate byte to maintain backwards compatibility.
    /// </summary>
    [Flags]
    public enum EntityEventFlags : byte
    {
        None = 0,
        /// <summary>Entity is under immobilizing CC (root/stun)</summary>
        ImmobilizeCC = 1 << 0,
        /// <summary>Entity just blinked (short teleport)</summary>
        BlinkEvent = 1 << 1,
        /// <summary>Entity just teleported (long distance)</summary>
        TeleportEvent = 1 << 2,
        /// <summary>Entity state was reset (respawn, etc.)</summary>
        StateReset = 1 << 3,
        /// <summary>Entity is dashing</summary>
        Dashing = 1 << 4,
        // 5-7 reserved
    }
}

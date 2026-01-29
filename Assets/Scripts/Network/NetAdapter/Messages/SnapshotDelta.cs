// SnapshotDelta.cs - Server snapshot sent to clients
// Contains all entity states, sent unreliable at ~20-30Hz

using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MOBANet.NetAdapter.Messages
{
    /// <summary>
    /// Server snapshot containing entity states.
    /// Sent unreliable at snapshot rate (20-30Hz).
    /// Packet loss is acceptable - clients interpolate between snapshots.
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
        /// Number of entities in this snapshot
        /// </summary>
        public ushort EntityCount;

        /// <summary>
        /// Entity states (variable length, up to MAX_ENTITIES)
        /// Note: For FishNet, this will be serialized separately
        /// </summary>
        public EntityState[] Entities;

        // Header size: 4 + 4 + 2 = 10 bytes
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

        // Total size: 4 + 1 + 1 + 2 + 2 + 2 + 2 + 2 + 1 + 2 + 2 = 21 bytes per entity

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
        /// Get dequantized horizontal velocity (Y component is always 0)
        /// </summary>
        public Vector3 Velocity => new Vector3(
            VelX / VELOCITY_SCALE,
            0f,
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
                VelZ = QuantizeVelocity(velocity.z)
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
}

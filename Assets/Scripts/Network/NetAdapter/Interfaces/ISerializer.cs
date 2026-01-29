// ISerializer.cs - Serialization interface for network messages
// Allows custom serialization implementations (binary, protobuf, etc.)

using System;

namespace MOBANet.NetAdapter
{
    /// <summary>
    /// Interface for message serialization.
    /// Implementations can use different formats (binary, JSON, protobuf, etc.)
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// Serialize a message to bytes
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="message">Message to serialize</param>
        /// <param name="buffer">Target buffer</param>
        /// <param name="bytesWritten">Actual bytes written</param>
        void Serialize<T>(in T message, Span<byte> buffer, out int bytesWritten) where T : struct;

        /// <summary>
        /// Deserialize bytes to message
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <param name="buffer">Source buffer</param>
        /// <returns>Deserialized message</returns>
        T Deserialize<T>(ReadOnlySpan<byte> buffer) where T : struct;

        /// <summary>
        /// Get required buffer size for message type
        /// </summary>
        /// <typeparam name="T">Message type</typeparam>
        /// <returns>Required buffer size in bytes</returns>
        int GetSize<T>() where T : struct;

        /// <summary>
        /// Rent a buffer from the pool
        /// </summary>
        /// <param name="minSize">Minimum buffer size</param>
        /// <returns>Pooled buffer</returns>
        byte[] Rent(int minSize);

        /// <summary>
        /// Return a buffer to the pool
        /// </summary>
        /// <param name="buffer">Buffer to return</param>
        void Return(byte[] buffer);
    }

    /// <summary>
    /// Default binary serializer using StructLayout for blittable types.
    /// Uses GCHandle for safe marshaling without unsafe code.
    /// </summary>
    public class BinarySerializer : ISerializer
    {
        private readonly System.Buffers.ArrayPool<byte> _pool = System.Buffers.ArrayPool<byte>.Shared;

        public void Serialize<T>(in T message, Span<byte> buffer, out int bytesWritten) where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            if (buffer.Length < size)
                throw new ArgumentException($"Buffer too small. Need {size}, got {buffer.Length}");

            // Use intermediate array for marshaling
            byte[] temp = _pool.Rent(size);
            try
            {
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(temp, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    System.Runtime.InteropServices.Marshal.StructureToPtr(message, handle.AddrOfPinnedObject(), false);
                }
                finally
                {
                    handle.Free();
                }
                temp.AsSpan(0, size).CopyTo(buffer);
            }
            finally
            {
                _pool.Return(temp);
            }
            bytesWritten = size;
        }

        public T Deserialize<T>(ReadOnlySpan<byte> buffer) where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            if (buffer.Length < size)
                throw new ArgumentException($"Buffer too small. Need {size}, got {buffer.Length}");

            // Use intermediate array for marshaling
            byte[] temp = _pool.Rent(size);
            try
            {
                buffer.Slice(0, size).CopyTo(temp);
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(temp, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    return System.Runtime.InteropServices.Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }
            finally
            {
                _pool.Return(temp);
            }
        }

        public int GetSize<T>() where T : struct
        {
            return System.Runtime.InteropServices.Marshal.SizeOf<T>();
        }

        public byte[] Rent(int minSize)
        {
            return _pool.Rent(minSize);
        }

        public void Return(byte[] buffer)
        {
            _pool.Return(buffer);
        }
    }
}

using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Determinism
{
    /// <summary>
    /// Incremental 32-bit FNV-1a state hash. Numeric values use an explicit little-endian
    /// byte order so identical simulation state hashes identically on every architecture.
    /// The default struct value is ready to use and starts at OffsetBasis.
    /// </summary>
    [Serializable]
    public struct Fnv1a32
    {
        public const uint OffsetBasis = 2166136261U;
        public const uint Prime = 16777619U;

        private uint _hash;
        private bool _initialized;

        public Fnv1a32(uint seed)
        {
            _hash = seed;
            _initialized = true;
        }

        public uint Value => _initialized ? _hash : OffsetBasis;

        public uint Hash => Value;

        public void Reset()
        {
            _hash = OffsetBasis;
            _initialized = true;
        }

        public void Add(byte value)
        {
            uint hash = Value;
            hash ^= value;
            _hash = unchecked(hash * Prime);
            _initialized = true;
        }

        public void Add(bool value)
        {
            Add(value ? (byte)1 : (byte)0);
        }

        public void Add(short value) => Add(unchecked((ushort)value));

        public void Add(ushort value)
        {
            Add((byte)value);
            Add((byte)(value >> 8));
        }

        public void Add(int value) => Add(unchecked((uint)value));

        public void Add(uint value)
        {
            Add((byte)value);
            Add((byte)(value >> 8));
            Add((byte)(value >> 16));
            Add((byte)(value >> 24));
        }

        public void Add(long value) => Add(unchecked((ulong)value));

        public void Add(ulong value)
        {
            Add((uint)value);
            Add((uint)(value >> 32));
        }

        public void Add(FP value)
        {
            Add(value.Raw);
        }

        public void Add(FPVector2 value)
        {
            Add(value.X);
            Add(value.Y);
        }

        public void Add(FPVector3 value)
        {
            Add(value.X);
            Add(value.Y);
            Add(value.Z);
        }

        public void AddBytes(ReadOnlySpan<byte> bytes)
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                Add(bytes[index]);
            }
        }

        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            Fnv1a32 hash = default;
            hash.AddBytes(bytes);
            return hash.Value;
        }
    }
}

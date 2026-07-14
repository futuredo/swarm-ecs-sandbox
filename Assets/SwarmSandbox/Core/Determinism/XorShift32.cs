using System;
using SwarmECS.FixedPoint;

namespace SwarmECS.Determinism
{
    /// <summary>
    /// Small deterministic PRNG suitable for simulation decisions. It is not cryptographically secure.
    /// State is explicit so rollback snapshots can store and restore it without allocation.
    /// </summary>
    [Serializable]
    public struct XorShift32
    {
        public const uint DefaultNonZeroSeed = 0x6D2B79F5U;

        private uint _state;

        public XorShift32(uint seed)
        {
            _state = seed == 0U ? DefaultNonZeroSeed : seed;
        }

        public uint State
        {
            get => _state == 0U ? DefaultNonZeroSeed : _state;
            set => _state = value == 0U ? DefaultNonZeroSeed : value;
        }

        public uint NextUInt()
        {
            uint value = State;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _state = value;
            return value;
        }

        public int NextInt()
        {
            return (int)(NextUInt() & 0x7FFFFFFFU);
        }

        /// <summary>Returns a uniformly distributed integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be greater than minimum.");
            }

            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong sampleRange = 1UL << 32;
            ulong acceptanceLimit = sampleRange - (sampleRange % range);
            uint sample;

            do
            {
                sample = NextUInt();
            }
            while ((ulong)sample >= acceptanceLimit);

            return (int)(minInclusive + (long)((ulong)sample % range));
        }

        /// <summary>Returns a Q16.16 value in [0, 1).</summary>
        public FP NextFP01()
        {
            return FP.FromRaw((int)(NextUInt() >> 16));
        }

        public FP NextFP(FP minInclusive, FP maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Maximum must be greater than minimum.");
            }

            return minInclusive + ((maxExclusive - minInclusive) * NextFP01());
        }
    }
}

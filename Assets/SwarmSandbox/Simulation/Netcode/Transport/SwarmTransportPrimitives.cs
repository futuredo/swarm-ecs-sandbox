using System;

namespace SwarmECS.Simulation.Netcode.Transport
{
    /// <summary>
    /// Fixed-capacity reliable datagram window. It retains encoded packets so a
    /// retransmission repeats the same sequence and payload without reallocating.
    /// </summary>
    public sealed class ReliableDatagramWindow
    {
        private sealed class Entry
        {
            public readonly byte[] Bytes = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            public bool Active;
            public uint Sequence;
            public int Count;
            public long FirstSentMilliseconds;
            public long LastSentMilliseconds;
            public int Attempts;
        }

        private readonly Entry[] _entries;
        private int _resendCursor;

        public ReliableDatagramWindow(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entries = new Entry[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _entries[index] = new Entry();
            }
        }

        public int Capacity => _entries.Length;

        public int PendingCount { get; private set; }

        public int AcknowledgedCount { get; private set; }

        public int RetransmissionCount { get; private set; }

        public long LastRoundTripMilliseconds { get; private set; }

        public long MaximumRoundTripMilliseconds { get; private set; }

        public long TotalRoundTripMilliseconds { get; private set; }

        public bool TryStore(
            uint sequence,
            byte[] bytes,
            int count,
            long nowMilliseconds)
        {
            if (bytes == null || count <= 0 || count > SwarmUdpPacketCodec.MaxDatagramBytes ||
                count > bytes.Length)
            {
                return false;
            }

            for (int index = 0; index < _entries.Length; index++)
            {
                Entry entry = _entries[index];
                if (entry.Active)
                {
                    if (entry.Sequence == sequence)
                    {
                        return false;
                    }

                    continue;
                }

                Buffer.BlockCopy(bytes, 0, entry.Bytes, 0, count);
                entry.Active = true;
                entry.Sequence = sequence;
                entry.Count = count;
                entry.FirstSentMilliseconds = nowMilliseconds;
                entry.LastSentMilliseconds = nowMilliseconds;
                entry.Attempts = 1;
                PendingCount++;
                return true;
            }

            return false;
        }

        public int ApplyAcknowledgements(
            uint acknowledgement,
            uint acknowledgementBits,
            long nowMilliseconds)
        {
            int released = 0;
            for (int index = 0; index < _entries.Length; index++)
            {
                Entry entry = _entries[index];
                if (!entry.Active ||
                    !SerialNumber32.IsAcknowledged(
                        entry.Sequence,
                        acknowledgement,
                        acknowledgementBits))
                {
                    continue;
                }

                long roundTrip = nowMilliseconds - entry.FirstSentMilliseconds;
                if (roundTrip < 0)
                {
                    roundTrip = 0;
                }

                LastRoundTripMilliseconds = roundTrip;
                TotalRoundTripMilliseconds += roundTrip;
                if (roundTrip > MaximumRoundTripMilliseconds)
                {
                    MaximumRoundTripMilliseconds = roundTrip;
                }

                entry.Active = false;
                entry.Count = 0;
                PendingCount--;
                AcknowledgedCount++;
                released++;
            }

            return released;
        }

        public bool TryGetNextResend(
            long nowMilliseconds,
            int resendAfterMilliseconds,
            out uint sequence,
            out byte[] bytes,
            out int count)
        {
            sequence = 0u;
            bytes = null;
            count = 0;
            if (resendAfterMilliseconds < 1)
            {
                resendAfterMilliseconds = 1;
            }

            for (int offset = 0; offset < _entries.Length; offset++)
            {
                int index = (_resendCursor + offset) % _entries.Length;
                Entry entry = _entries[index];
                if (!entry.Active ||
                    nowMilliseconds - entry.LastSentMilliseconds < resendAfterMilliseconds)
                {
                    continue;
                }

                entry.LastSentMilliseconds = nowMilliseconds;
                entry.Attempts++;
                RetransmissionCount++;
                _resendCursor = (index + 1) % _entries.Length;
                sequence = entry.Sequence;
                bytes = entry.Bytes;
                count = entry.Count;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            for (int index = 0; index < _entries.Length; index++)
            {
                _entries[index].Active = false;
                _entries[index].Count = 0;
            }

            PendingCount = 0;
            _resendCursor = 0;
        }
    }

    /// <summary>
    /// Thread-safe, fixed-capacity datagram handoff. Socket code copies into this
    /// queue; only the simulation thread drains and interprets the payload.
    /// </summary>
    public sealed class FixedDatagramQueue
    {
        private sealed class Slot
        {
            public readonly byte[] Bytes = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            public uint RemotePeerId;
            public int Count;
        }

        private readonly object _gate = new();
        private readonly Slot[] _slots;
        private int _read;
        private int _write;
        private int _count;

        public FixedDatagramQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _slots = new Slot[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _slots[index] = new Slot();
            }
        }

        public int Capacity => _slots.Length;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public long DroppedWhenFull { get; private set; }

        public bool TryEnqueue(uint remotePeerId, byte[] bytes, int count)
        {
            if (bytes == null || count <= 0 || count > SwarmUdpPacketCodec.MaxDatagramBytes ||
                count > bytes.Length)
            {
                return false;
            }

            lock (_gate)
            {
                if (_count >= _slots.Length)
                {
                    DroppedWhenFull++;
                    return false;
                }

                Slot slot = _slots[_write];
                Buffer.BlockCopy(bytes, 0, slot.Bytes, 0, count);
                slot.RemotePeerId = remotePeerId;
                slot.Count = count;
                _write = (_write + 1) % _slots.Length;
                _count++;
                return true;
            }
        }

        public bool TryDequeue(
            byte[] destination,
            out uint remotePeerId,
            out int count)
        {
            remotePeerId = 0u;
            count = 0;
            if (destination == null || destination.Length < SwarmUdpPacketCodec.MaxDatagramBytes)
            {
                return false;
            }

            lock (_gate)
            {
                if (_count == 0)
                {
                    return false;
                }

                Slot slot = _slots[_read];
                Buffer.BlockCopy(slot.Bytes, 0, destination, 0, slot.Count);
                remotePeerId = slot.RemotePeerId;
                count = slot.Count;
                slot.Count = 0;
                _read = (_read + 1) % _slots.Length;
                _count--;
                return true;
            }
        }
    }

    public readonly struct WeakNetworkSettings
    {
        public WeakNetworkSettings(
            int baseLatencyMilliseconds,
            int jitterMilliseconds,
            int lossPermille,
            int duplicatePermille,
            int reorderPermille)
        {
            if (baseLatencyMilliseconds < 0 || jitterMilliseconds < 0 ||
                lossPermille < 0 || lossPermille > 1000 ||
                duplicatePermille < 0 || duplicatePermille > 1000 ||
                reorderPermille < 0 || reorderPermille > 1000)
            {
                throw new ArgumentOutOfRangeException();
            }

            BaseLatencyMilliseconds = baseLatencyMilliseconds;
            JitterMilliseconds = jitterMilliseconds;
            LossPermille = lossPermille;
            DuplicatePermille = duplicatePermille;
            ReorderPermille = reorderPermille;
        }

        public int BaseLatencyMilliseconds { get; }
        public int JitterMilliseconds { get; }
        public int LossPermille { get; }
        public int DuplicatePermille { get; }
        public int ReorderPermille { get; }
    }

    /// <summary>
    /// Deterministic outgoing datagram scheduler used by both real UDP endpoints.
    /// Loss/duplication/reorder choices are seed-driven and every slot owns its bytes.
    /// </summary>
    public sealed class DeterministicWeakNetwork
    {
        private sealed class ScheduledDatagram
        {
            public readonly byte[] Bytes = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            public bool Active;
            public uint DestinationPeerId;
            public int Count;
            public long DueMilliseconds;
            public ulong StableOrder;
        }

        private readonly WeakNetworkSettings _settings;
        private readonly ScheduledDatagram[] _entries;
        private uint _randomState;
        private ulong _nextStableOrder;

        public DeterministicWeakNetwork(
            WeakNetworkSettings settings,
            int capacity,
            uint seed)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _settings = settings;
            _entries = new ScheduledDatagram[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _entries[index] = new ScheduledDatagram();
            }

            _randomState = seed == 0u ? 0x6D2B79F5u : seed;
        }

        public int PendingCount { get; private set; }
        public long ScheduledCount { get; private set; }
        public long DeliveredCount { get; private set; }
        public long DroppedByLoss { get; private set; }
        public long DroppedByCapacity { get; private set; }
        public long DuplicatedCount { get; private set; }
        public long ReorderedCount { get; private set; }

        public bool Schedule(
            uint destinationPeerId,
            byte[] bytes,
            int count,
            long nowMilliseconds)
        {
            if (bytes == null || count <= 0 || count > SwarmUdpPacketCodec.MaxDatagramBytes ||
                count > bytes.Length)
            {
                return false;
            }

            if (NextPermille() < _settings.LossPermille)
            {
                DroppedByLoss++;
                return true;
            }

            bool reordered = NextPermille() < _settings.ReorderPermille;
            long due = nowMilliseconds + ComputeDelayMilliseconds(reordered);
            if (!TryInsert(destinationPeerId, bytes, count, due))
            {
                DroppedByCapacity++;
                return false;
            }

            if (reordered)
            {
                ReorderedCount++;
            }

            if (NextPermille() < _settings.DuplicatePermille)
            {
                long duplicateDue = due + 1 + (NextUInt() % 7u);
                if (TryInsert(destinationPeerId, bytes, count, duplicateDue))
                {
                    DuplicatedCount++;
                }
                else
                {
                    DroppedByCapacity++;
                }
            }

            return true;
        }

        public bool TryDequeueDue(
            long nowMilliseconds,
            out uint destinationPeerId,
            out byte[] bytes,
            out int count)
        {
            destinationPeerId = 0u;
            bytes = null;
            count = 0;
            int selected = -1;
            long selectedDue = long.MaxValue;
            ulong selectedOrder = ulong.MaxValue;
            for (int index = 0; index < _entries.Length; index++)
            {
                ScheduledDatagram entry = _entries[index];
                if (!entry.Active || entry.DueMilliseconds > nowMilliseconds ||
                    entry.DueMilliseconds > selectedDue ||
                    (entry.DueMilliseconds == selectedDue && entry.StableOrder >= selectedOrder))
                {
                    continue;
                }

                selected = index;
                selectedDue = entry.DueMilliseconds;
                selectedOrder = entry.StableOrder;
            }

            if (selected < 0)
            {
                return false;
            }

            ScheduledDatagram selectedEntry = _entries[selected];
            destinationPeerId = selectedEntry.DestinationPeerId;
            bytes = selectedEntry.Bytes;
            count = selectedEntry.Count;
            selectedEntry.Active = false;
            selectedEntry.Count = 0;
            PendingCount--;
            DeliveredCount++;
            return true;
        }

        private bool TryInsert(
            uint destinationPeerId,
            byte[] bytes,
            int count,
            long dueMilliseconds)
        {
            for (int index = 0; index < _entries.Length; index++)
            {
                ScheduledDatagram entry = _entries[index];
                if (entry.Active)
                {
                    continue;
                }

                Buffer.BlockCopy(bytes, 0, entry.Bytes, 0, count);
                entry.Active = true;
                entry.DestinationPeerId = destinationPeerId;
                entry.Count = count;
                entry.DueMilliseconds = dueMilliseconds;
                entry.StableOrder = _nextStableOrder++;
                PendingCount++;
                ScheduledCount++;
                return true;
            }

            return false;
        }

        private long ComputeDelayMilliseconds(bool reordered)
        {
            int jitter = 0;
            if (_settings.JitterMilliseconds > 0)
            {
                uint width = (uint)((_settings.JitterMilliseconds * 2) + 1);
                jitter = (int)(NextUInt() % width) - _settings.JitterMilliseconds;
            }

            int delay = _settings.BaseLatencyMilliseconds + jitter;
            if (reordered)
            {
                delay += _settings.BaseLatencyMilliseconds + _settings.JitterMilliseconds + 1;
            }

            return delay < 0 ? 0 : delay;
        }

        private int NextPermille()
        {
            return (int)(NextUInt() % 1000u);
        }

        private uint NextUInt()
        {
            uint value = _randomState;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            _randomState = value;
            return value;
        }
    }
}

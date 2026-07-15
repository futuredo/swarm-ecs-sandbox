using System;

namespace SwarmECS.Simulation.Netcode.Transport
{
    public enum SwarmNetworkChannel : byte
    {
        Control = 0,
        ReliableCommand = 1,
        HashTelemetry = 2,
    }

    [Flags]
    public enum SwarmPacketFlags : byte
    {
        None = 0,
        Reliable = 1 << 0,
        AckOnly = 1 << 1,
    }

    public readonly struct SwarmPacketHeader
    {
        public SwarmPacketHeader(
            uint sessionId,
            uint peerId,
            uint sequence,
            uint acknowledgement,
            uint acknowledgementBits,
            int tick,
            SwarmNetworkChannel channel,
            SwarmPacketFlags flags,
            ushort payloadLength)
        {
            SessionId = sessionId;
            PeerId = peerId;
            Sequence = sequence;
            Acknowledgement = acknowledgement;
            AcknowledgementBits = acknowledgementBits;
            Tick = tick;
            Channel = channel;
            Flags = flags;
            PayloadLength = payloadLength;
        }

        public uint SessionId { get; }

        /// <summary>Sender peer id. Zero is reserved for the authoritative server.</summary>
        public uint PeerId { get; }

        public uint Sequence { get; }

        public uint Acknowledgement { get; }

        public uint AcknowledgementBits { get; }

        public int Tick { get; }

        public SwarmNetworkChannel Channel { get; }

        public SwarmPacketFlags Flags { get; }

        public ushort PayloadLength { get; }

        public bool IsReliable => (Flags & SwarmPacketFlags.Reliable) != 0;

        public bool IsAckOnly => (Flags & SwarmPacketFlags.AckOnly) != 0;
    }

    /// <summary>
    /// Explicit little-endian UDP envelope. Header CRC covers bytes [0, 40), while
    /// payload CRC covers the declared payload. The CRC is corruption detection,
    /// not authentication or anti-cheat protection.
    /// </summary>
    public static class SwarmUdpPacketCodec
    {
        public const uint Magic = 0x4D525753u; // "SWRM" in little-endian bytes.
        public const ushort ProtocolVersion = 1;
        public const int HeaderSize = 44;
        public const int HeaderCrcOffset = 40;
        public const int MaxDatagramBytes = 1200;
        public const int MaxPayloadBytes = MaxDatagramBytes - HeaderSize;

        private const SwarmPacketFlags KnownFlags =
            SwarmPacketFlags.Reliable | SwarmPacketFlags.AckOnly;

        public static bool TryWrite(
            in SwarmPacketHeader header,
            byte[] payload,
            int payloadOffset,
            int payloadCount,
            byte[] destination,
            out int written)
        {
            written = 0;
            if (destination == null || payloadCount < 0 || payloadCount > MaxPayloadBytes ||
                destination.Length < HeaderSize + payloadCount ||
                header.PayloadLength != payloadCount ||
                (uint)header.Channel > (uint)SwarmNetworkChannel.HashTelemetry ||
                (header.Flags & ~KnownFlags) != 0)
            {
                return false;
            }

            if (payloadCount > 0 &&
                (payload == null || payloadOffset < 0 || payloadOffset > payload.Length - payloadCount))
            {
                return false;
            }

            NetworkBinary.WriteUInt32(destination, 0, Magic);
            NetworkBinary.WriteUInt16(destination, 4, ProtocolVersion);
            NetworkBinary.WriteUInt16(destination, 6, HeaderSize);
            NetworkBinary.WriteUInt32(destination, 8, header.SessionId);
            NetworkBinary.WriteUInt32(destination, 12, header.PeerId);
            NetworkBinary.WriteUInt32(destination, 16, header.Sequence);
            NetworkBinary.WriteUInt32(destination, 20, header.Acknowledgement);
            NetworkBinary.WriteUInt32(destination, 24, header.AcknowledgementBits);
            NetworkBinary.WriteInt32(destination, 28, header.Tick);
            destination[32] = (byte)header.Channel;
            destination[33] = (byte)header.Flags;
            NetworkBinary.WriteUInt16(destination, 34, (ushort)payloadCount);
            uint payloadCrc = payloadCount == 0
                ? 0u
                : NetworkCrc32.Compute(payload, payloadOffset, payloadCount);
            NetworkBinary.WriteUInt32(destination, 36, payloadCrc);
            NetworkBinary.WriteUInt32(destination, HeaderCrcOffset, 0u);
            uint headerCrc = NetworkCrc32.Compute(destination, 0, HeaderCrcOffset);
            NetworkBinary.WriteUInt32(destination, HeaderCrcOffset, headerCrc);

            if (payloadCount > 0)
            {
                Buffer.BlockCopy(payload, payloadOffset, destination, HeaderSize, payloadCount);
            }

            written = HeaderSize + payloadCount;
            return true;
        }

        public static bool TryRead(
            byte[] datagram,
            int count,
            out SwarmPacketHeader header,
            out int payloadOffset)
        {
            header = default;
            payloadOffset = 0;
            if (datagram == null || count < HeaderSize || count > datagram.Length ||
                NetworkBinary.ReadUInt32(datagram, 0) != Magic ||
                NetworkBinary.ReadUInt16(datagram, 4) != ProtocolVersion ||
                NetworkBinary.ReadUInt16(datagram, 6) != HeaderSize)
            {
                return false;
            }

            ushort payloadLength = NetworkBinary.ReadUInt16(datagram, 34);
            if (payloadLength > MaxPayloadBytes || count != HeaderSize + payloadLength)
            {
                return false;
            }

            byte channelValue = datagram[32];
            SwarmPacketFlags flags = (SwarmPacketFlags)datagram[33];
            if (channelValue > (byte)SwarmNetworkChannel.HashTelemetry ||
                (flags & ~KnownFlags) != 0 ||
                ((flags & SwarmPacketFlags.AckOnly) != 0 && payloadLength != 0))
            {
                return false;
            }

            uint expectedHeaderCrc = NetworkBinary.ReadUInt32(datagram, HeaderCrcOffset);
            uint actualHeaderCrc = NetworkCrc32.Compute(datagram, 0, HeaderCrcOffset);
            if (expectedHeaderCrc != actualHeaderCrc)
            {
                return false;
            }

            uint expectedPayloadCrc = NetworkBinary.ReadUInt32(datagram, 36);
            uint actualPayloadCrc = payloadLength == 0
                ? 0u
                : NetworkCrc32.Compute(datagram, HeaderSize, payloadLength);
            if (expectedPayloadCrc != actualPayloadCrc)
            {
                return false;
            }

            header = new SwarmPacketHeader(
                NetworkBinary.ReadUInt32(datagram, 8),
                NetworkBinary.ReadUInt32(datagram, 12),
                NetworkBinary.ReadUInt32(datagram, 16),
                NetworkBinary.ReadUInt32(datagram, 20),
                NetworkBinary.ReadUInt32(datagram, 24),
                NetworkBinary.ReadInt32(datagram, 28),
                (SwarmNetworkChannel)channelValue,
                flags,
                payloadLength);
            payloadOffset = HeaderSize;
            return true;
        }
    }

    public static class SerialNumber32
    {
        /// <summary>
        /// RFC-1982-style half-range comparison. Exactly half-range-apart values
        /// are intentionally unordered and return false in both directions.
        /// </summary>
        public static bool IsNewer(uint candidate, uint reference)
        {
            uint difference = candidate - reference;
            return difference != 0u && difference < 0x80000000u;
        }

        public static uint ForwardDistance(uint newer, uint older)
        {
            return newer - older;
        }

        public static bool IsAcknowledged(uint sequence, uint acknowledgement, uint acknowledgementBits)
        {
            if (sequence == acknowledgement)
            {
                return true;
            }

            uint distance = acknowledgement - sequence;
            return distance >= 1u && distance <= 32u &&
                (acknowledgementBits & (1u << ((int)distance - 1))) != 0u;
        }
    }

    /// <summary>Duplicate rejection plus the ack/ackBits state sent in the next packet.</summary>
    public sealed class PacketReceiveWindow
    {
        private bool _initialized;
        private uint _latest;
        private uint _receivedBits;

        public bool IsInitialized => _initialized;

        public uint Acknowledgement => _initialized ? _latest : 0u;

        public uint AcknowledgementBits => _initialized ? _receivedBits : 0u;

        public bool TryAccept(uint sequence)
        {
            if (!_initialized)
            {
                _initialized = true;
                _latest = sequence;
                _receivedBits = 0u;
                return true;
            }

            if (sequence == _latest)
            {
                return false;
            }

            if (SerialNumber32.IsNewer(sequence, _latest))
            {
                uint distance = sequence - _latest;
                _receivedBits = distance > 32u
                    ? 0u
                    : distance == 32u
                        ? 1u << 31
                        : (_receivedBits << (int)distance) | (1u << ((int)distance - 1));
                _latest = sequence;
                return true;
            }

            uint age = _latest - sequence;
            if (age == 0u || age > 32u)
            {
                return false;
            }

            uint mask = 1u << ((int)age - 1);
            if ((_receivedBits & mask) != 0u)
            {
                return false;
            }

            _receivedBits |= mask;
            return true;
        }

        public void Reset()
        {
            _initialized = false;
            _latest = 0u;
            _receivedBits = 0u;
        }
    }

    public static class NetworkBinary
    {
        public static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        public static void WriteInt32(byte[] bytes, int offset, int value)
        {
            WriteUInt32(bytes, offset, unchecked((uint)value));
        }

        public static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        public static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            WriteUInt32(bytes, offset, (uint)value);
            WriteUInt32(bytes, offset + 4, (uint)(value >> 32));
        }

        public static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        public static int ReadInt32(byte[] bytes, int offset)
        {
            return unchecked((int)ReadUInt32(bytes, offset));
        }

        public static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] |
                (bytes[offset + 1] << 8) |
                (bytes[offset + 2] << 16) |
                (bytes[offset + 3] << 24));
        }

        public static ulong ReadUInt64(byte[] bytes, int offset)
        {
            return ReadUInt32(bytes, offset) | ((ulong)ReadUInt32(bytes, offset + 4) << 32);
        }
    }

    public static class NetworkCrc32
    {
        private const uint Polynomial = 0xEDB88320u;

        public static uint Compute(byte[] bytes, int offset, int count)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0 || count < 0 || offset > bytes.Length - count)
            {
                throw new ArgumentOutOfRangeException();
            }

            uint crc = uint.MaxValue;
            int end = offset + count;
            for (int index = offset; index < end; index++)
            {
                crc ^= bytes[index];
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (Polynomial & mask);
                }
            }

            return ~crc;
        }
    }
}

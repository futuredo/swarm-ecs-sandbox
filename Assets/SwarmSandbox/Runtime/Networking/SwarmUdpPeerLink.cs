using SwarmECS.Simulation.Netcode.Transport;

namespace SwarmECS.Runtime.Networking
{
    /// <summary>Per-direction packet sequencing, receive acknowledgements and reliable retransmission.</summary>
    internal sealed class SwarmUdpPeerLink
    {
        private readonly ReliableDatagramWindow _reliableWindow;
        private readonly PacketReceiveWindow _receiveWindow = new PacketReceiveWindow();
        private readonly byte[] _encodeBuffer = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
        private uint _nextPacketSequence = 1u;
        private bool _ackDirty;

        public SwarmUdpPeerLink(uint destinationPeerId, int reliableCapacity = 64)
        {
            DestinationPeerId = destinationPeerId;
            _reliableWindow = new ReliableDatagramWindow(reliableCapacity);
        }

        public uint DestinationPeerId { get; }

        public int PendingReliableCount => _reliableWindow.PendingCount;

        public int AcknowledgedReliableCount => _reliableWindow.AcknowledgedCount;

        public int RetransmissionCount => _reliableWindow.RetransmissionCount;

        public long LastRoundTripMilliseconds => _reliableWindow.LastRoundTripMilliseconds;

        public long MaximumRoundTripMilliseconds => _reliableWindow.MaximumRoundTripMilliseconds;

        public long AverageRoundTripMilliseconds => _reliableWindow.AcknowledgedCount == 0
            ? 0L
            : _reliableWindow.TotalRoundTripMilliseconds / _reliableWindow.AcknowledgedCount;

        public long DuplicateOrStalePackets { get; private set; }

        public long AcceptedPackets { get; private set; }

        public bool AcceptHeader(in SwarmPacketHeader header, long nowMilliseconds)
        {
            _reliableWindow.ApplyAcknowledgements(
                header.Acknowledgement,
                header.AcknowledgementBits,
                nowMilliseconds);

            if (header.IsReliable)
            {
                // A duplicate reliable packet usually means the previous ACK was lost.
                _ackDirty = true;
            }

            if (!_receiveWindow.TryAccept(header.Sequence))
            {
                DuplicateOrStalePackets++;
                return false;
            }

            AcceptedPackets++;
            return true;
        }

        public bool TrySendPayload(
            uint sessionId,
            uint senderPeerId,
            int tick,
            SwarmNetworkChannel channel,
            bool reliable,
            byte[] payload,
            int payloadCount,
            DeterministicWeakNetwork weakNetwork,
            long nowMilliseconds)
        {
            uint packetSequence = _nextPacketSequence++;
            SwarmPacketFlags flags = reliable ? SwarmPacketFlags.Reliable : SwarmPacketFlags.None;
            var header = new SwarmPacketHeader(
                sessionId,
                senderPeerId,
                packetSequence,
                _receiveWindow.Acknowledgement,
                _receiveWindow.AcknowledgementBits,
                tick,
                channel,
                flags,
                (ushort)payloadCount);
            if (!SwarmUdpPacketCodec.TryWrite(
                    header,
                    payload,
                    0,
                    payloadCount,
                    _encodeBuffer,
                    out int packetCount))
            {
                return false;
            }

            if (reliable && !_reliableWindow.TryStore(
                    packetSequence,
                    _encodeBuffer,
                    packetCount,
                    nowMilliseconds))
            {
                return false;
            }

            return weakNetwork.Schedule(
                DestinationPeerId,
                _encodeBuffer,
                packetCount,
                nowMilliseconds);
        }

        public bool TrySendPendingAcknowledgement(
            uint sessionId,
            uint senderPeerId,
            int tick,
            DeterministicWeakNetwork weakNetwork,
            long nowMilliseconds)
        {
            if (!_ackDirty)
            {
                return false;
            }

            uint packetSequence = _nextPacketSequence++;
            var header = new SwarmPacketHeader(
                sessionId,
                senderPeerId,
                packetSequence,
                _receiveWindow.Acknowledgement,
                _receiveWindow.AcknowledgementBits,
                tick,
                SwarmNetworkChannel.Control,
                SwarmPacketFlags.AckOnly,
                0);
            if (!SwarmUdpPacketCodec.TryWrite(
                    header,
                    null,
                    0,
                    0,
                    _encodeBuffer,
                    out int packetCount))
            {
                return false;
            }

            if (!weakNetwork.Schedule(
                    DestinationPeerId,
                    _encodeBuffer,
                    packetCount,
                    nowMilliseconds))
            {
                return false;
            }

            _ackDirty = false;
            return true;
        }

        public int PumpResends(
            DeterministicWeakNetwork weakNetwork,
            long nowMilliseconds,
            int resendAfterMilliseconds,
            int budget)
        {
            int sent = 0;
            while (sent < budget && _reliableWindow.TryGetNextResend(
                       nowMilliseconds,
                       resendAfterMilliseconds,
                       out _,
                       out byte[] bytes,
                       out int count))
            {
                weakNetwork.Schedule(DestinationPeerId, bytes, count, nowMilliseconds);
                sent++;
            }

            return sent;
        }
    }
}

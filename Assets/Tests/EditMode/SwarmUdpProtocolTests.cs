using System;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Netcode.Transport;
using SwarmECS.Simulation.Systems;

namespace SwarmECS.Tests
{
    public sealed class SwarmUdpProtocolTests
    {
        [Test]
        public void PacketCodec_RoundTripsExplicitHeaderAndRejectsCorruption()
        {
            byte[] payload = new byte[32];
            for (int index = 0; index < payload.Length; index++)
            {
                payload[index] = (byte)(index * 7);
            }

            var source = new SwarmPacketHeader(
                0xA1B2C3D4u,
                2u,
                uint.MaxValue,
                uint.MaxValue - 4u,
                0xC0000001u,
                1234,
                SwarmNetworkChannel.ReliableCommand,
                SwarmPacketFlags.Reliable,
                (ushort)payload.Length);
            byte[] datagram = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            Assert.That(SwarmUdpPacketCodec.TryWrite(
                source,
                payload,
                0,
                payload.Length,
                datagram,
                out int written), Is.True);
            Assert.That(written, Is.EqualTo(SwarmUdpPacketCodec.HeaderSize + payload.Length));
            Assert.That(SwarmUdpPacketCodec.TryRead(
                datagram,
                written,
                out SwarmPacketHeader decoded,
                out int payloadOffset), Is.True);
            Assert.That(decoded.SessionId, Is.EqualTo(source.SessionId));
            Assert.That(decoded.PeerId, Is.EqualTo(source.PeerId));
            Assert.That(decoded.Sequence, Is.EqualTo(source.Sequence));
            Assert.That(decoded.Acknowledgement, Is.EqualTo(source.Acknowledgement));
            Assert.That(decoded.AcknowledgementBits, Is.EqualTo(source.AcknowledgementBits));
            Assert.That(decoded.Tick, Is.EqualTo(source.Tick));
            Assert.That(decoded.Channel, Is.EqualTo(source.Channel));
            Assert.That(decoded.Flags, Is.EqualTo(source.Flags));
            CollectionAssert.AreEqual(
                payload,
                new ArraySegment<byte>(datagram, payloadOffset, payload.Length));

            datagram[12] ^= 0x40;
            Assert.That(SwarmUdpPacketCodec.TryRead(datagram, written, out _, out _), Is.False);
            datagram[12] ^= 0x40;
            datagram[payloadOffset + 3] ^= 0x80;
            Assert.That(SwarmUdpPacketCodec.TryRead(datagram, written, out _, out _), Is.False);
            datagram[payloadOffset + 3] ^= 0x80;
            Assert.That(SwarmUdpPacketCodec.TryRead(datagram, written - 1, out _, out _), Is.False);
        }

        [Test]
        public void SerialAndReceiveWindow_HandleWrapOutOfOrderAndDuplicatePackets()
        {
            Assert.That(SerialNumber32.IsNewer(0u, uint.MaxValue), Is.True);
            Assert.That(SerialNumber32.IsNewer(1u, uint.MaxValue), Is.True);
            Assert.That(SerialNumber32.IsNewer(uint.MaxValue, 0u), Is.False);
            Assert.That(SerialNumber32.IsNewer(0x80000000u, 0u), Is.False);
            Assert.That(SerialNumber32.IsNewer(0u, 0x80000000u), Is.False);

            var window = new PacketReceiveWindow();
            Assert.That(window.TryAccept(uint.MaxValue - 1u), Is.True);
            Assert.That(window.TryAccept(1u), Is.True);
            Assert.That(window.Acknowledgement, Is.EqualTo(1u));
            Assert.That(
                SerialNumber32.IsAcknowledged(uint.MaxValue - 1u, window.Acknowledgement, window.AcknowledgementBits),
                Is.True);
            Assert.That(window.TryAccept(0u), Is.True);
            Assert.That(window.TryAccept(0u), Is.False);
            Assert.That(window.TryAccept(uint.MaxValue - 1u), Is.False);

            window.Reset();
            Assert.That(window.TryAccept(10u), Is.True);
            Assert.That(window.TryAccept(42u), Is.True);
            Assert.That(SerialNumber32.IsAcknowledged(10u, 42u, window.AcknowledgementBits), Is.True);
            Assert.That(window.TryAccept(9u), Is.False, "Packets older than ackBits must be rejected.");
        }

        [Test]
        public void ReliableWindow_ReleasesAckedPacketsAndResendsWithoutAllocation()
        {
            var window = new ReliableDatagramWindow(4);
            byte[] datagram = new byte[64];
            datagram[0] = 7;
            Assert.That(window.TryStore(uint.MaxValue, datagram, 32, 100), Is.True);
            Assert.That(window.TryStore(0u, datagram, 32, 101), Is.True);
            Assert.That(window.PendingCount, Is.EqualTo(2));
            Assert.That(window.TryGetNextResend(149, 50, out _, out _, out _), Is.False);
            Assert.That(window.TryGetNextResend(150, 50, out uint resent, out byte[] bytes, out int count), Is.True);
            Assert.That(resent, Is.EqualTo(uint.MaxValue));
            Assert.That(bytes[0], Is.EqualTo(7));
            Assert.That(count, Is.EqualTo(32));
            Assert.That(window.RetransmissionCount, Is.EqualTo(1));

            Assert.That(window.ApplyAcknowledgements(0u, 1u, 180), Is.EqualTo(2));
            Assert.That(window.PendingCount, Is.Zero);
            Assert.That(window.AcknowledgedCount, Is.EqualTo(2));
            Assert.That(window.MaximumRoundTripMilliseconds, Is.EqualTo(80));
        }

        [Test]
        public void FixedDatagramQueue_IsBoundedAndCopiesProducerBytes()
        {
            var queue = new FixedDatagramQueue(2);
            byte[] source = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            source[0] = 11;
            Assert.That(queue.TryEnqueue(1u, source, 4), Is.True);
            source[0] = 22;
            Assert.That(queue.TryEnqueue(2u, source, 4), Is.True);
            Assert.That(queue.TryEnqueue(3u, source, 4), Is.False);
            Assert.That(queue.DroppedWhenFull, Is.EqualTo(1));

            byte[] destination = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
            Assert.That(queue.TryDequeue(destination, out uint peer, out int count), Is.True);
            Assert.That(peer, Is.EqualTo(1u));
            Assert.That(count, Is.EqualTo(4));
            Assert.That(destination[0], Is.EqualTo(11));
            Assert.That(queue.TryDequeue(destination, out peer, out count), Is.True);
            Assert.That(peer, Is.EqualTo(2u));
            Assert.That(destination[0], Is.EqualTo(22));
            Assert.That(queue.TryDequeue(destination, out _, out _), Is.False);
        }

        [Test]
        public void WeakNetwork_IsSeedDeterministicAndPreservesStableDueOrdering()
        {
            var settings = new WeakNetworkSettings(20, 7, 120, 180, 250);
            var first = new DeterministicWeakNetwork(settings, 128, 0x12345678u);
            var second = new DeterministicWeakNetwork(settings, 128, 0x12345678u);
            byte[] datagram = new byte[64];
            for (int packet = 0; packet < 40; packet++)
            {
                datagram[0] = (byte)packet;
                Assert.That(first.Schedule(1u, datagram, 12, packet),
                    Is.EqualTo(second.Schedule(1u, datagram, 12, packet)));
            }

            byte[] firstValues = DrainWeakNetwork(first, 200);
            byte[] secondValues = DrainWeakNetwork(second, 200);
            CollectionAssert.AreEqual(firstValues, secondValues);
            Assert.That(first.DroppedByLoss, Is.EqualTo(second.DroppedByLoss));
            Assert.That(first.DuplicatedCount, Is.EqualTo(second.DuplicatedCount));
            Assert.That(first.ReorderedCount, Is.EqualTo(second.ReorderedCount));
            Assert.That(firstValues.Length, Is.GreaterThan(0));
        }

        [Test]
        public void MessageCodec_RoundTripsIdentityCommandsHashesAndCompletion()
        {
            SwarmConfig config = SwarmConfig.DemoDefault(64);
            NetworkCompatibilityIdentity identity = NetworkCompatibilityIdentity.Create(config, 64, 0xCAFEu);
            byte[] payload = new byte[SwarmUdpPacketCodec.MaxPayloadBytes];

            int count = SwarmNetworkMessageCodec.WriteHandshake(payload, identity);
            Assert.That(SwarmNetworkMessageCodec.TryReadHandshake(
                payload,
                0,
                count,
                out NetworkCompatibilityIdentity decodedIdentity), Is.True);
            Assert.That(decodedIdentity.IsCompatibleWith(identity), Is.True);
            var incompatible = new NetworkCompatibilityIdentity(
                identity.ProtocolVersion,
                identity.LogicHash,
                identity.ConfigHash + 1UL,
                identity.ConfigSchemaVersion,
                identity.ReplaySchemaVersion,
                identity.SnapshotSchemaVersion,
                identity.AuthoritySchemaVersion,
                identity.FixedPointBits,
                identity.AgentCount,
                identity.Seed);
            Assert.That(decodedIdentity.IsCompatibleWith(incompatible), Is.False);

            var welcome = new NetworkWelcome(77u, 2u, identity, 2, 3, 240);
            count = SwarmNetworkMessageCodec.WriteWelcome(payload, welcome);
            Assert.That(SwarmNetworkMessageCodec.TryReadWelcome(payload, 0, count, out NetworkWelcome decodedWelcome), Is.True);
            Assert.That(decodedWelcome.SessionId, Is.EqualTo(77u));
            Assert.That(decodedWelcome.AssignedPeerId, Is.EqualTo(2u));
            Assert.That(decodedWelcome.Identity.IsCompatibleWith(identity), Is.True);

            var request = new NetworkCommandRequest(
                9u,
                42,
                3,
                new FPVector2(FP.FromInt(12), FP.FromInt(-7)));
            count = SwarmNetworkMessageCodec.WriteCommandRequest(payload, request);
            Assert.That(SwarmNetworkMessageCodec.TryReadCommandRequest(payload, 0, count, out NetworkCommandRequest decodedRequest), Is.True);
            Assert.That(decodedRequest.RequestId, Is.EqualTo(9u));
            Assert.That(decodedRequest.Target, Is.EqualTo(request.Target));

            var authoritative = new NetworkAuthoritativeCommand(
                2u,
                9u,
                new SimulationCommand(48, 5, SimulationCommandType.SetGroupTarget, 3, request.Target));
            count = SwarmNetworkMessageCodec.WriteAuthoritativeCommand(payload, authoritative);
            Assert.That(SwarmNetworkMessageCodec.TryReadAuthoritativeCommand(
                payload,
                0,
                count,
                out NetworkAuthoritativeCommand decodedAuthority), Is.True);
            Assert.That(decodedAuthority.Command.Tick, Is.EqualTo(48));
            Assert.That(decodedAuthority.Command.Sequence, Is.EqualTo(5));
            Assert.That(decodedAuthority.Command.Value, Is.EqualTo(request.Target));

            var telemetry = new NetworkHashTelemetry(50, 0x1234ABCDEFUL, 5);
            count = SwarmNetworkMessageCodec.WriteHashTelemetry(payload, telemetry);
            Assert.That(SwarmNetworkMessageCodec.TryReadHashTelemetry(payload, 0, count, out NetworkHashTelemetry decodedTelemetry), Is.True);
            Assert.That(decodedTelemetry.AuthorityHash, Is.EqualTo(telemetry.AuthorityHash));

            var complete = new NetworkSessionComplete(240, 0x55AAUL, 6);
            count = SwarmNetworkMessageCodec.WriteSessionComplete(payload, complete);
            Assert.That(SwarmNetworkMessageCodec.TryReadSessionComplete(payload, 0, count, out NetworkSessionComplete decodedComplete), Is.True);
            Assert.That(decodedComplete.TotalCommands, Is.EqualTo(6));
        }

        [Test]
        public void Reconciler_LateAuthorityReplaysToOnTimeWorldAndDetectsSnapshotRequirement()
        {
            SwarmConfig config = SwarmConfig.DemoDefault(64);
            SwarmWorld onTimeWorld = CreateWorld(config);
            SwarmWorld lateWorld = CreateWorld(config);
            using var onTimeSimulation = new SwarmSimulation(onTimeWorld);
            using var lateSimulation = new SwarmSimulation(lateWorld);
            var onTime = new RollbackController(onTimeWorld, onTimeSimulation, 16, 64);
            var late = new RollbackController(lateWorld, lateSimulation, 16, 64);
            var reconciler = new ClientCommandReconciler(lateWorld, late, 32);
            reconciler.BeginPrediction();
            var authority = new NetworkAuthoritativeCommand(
                1u,
                1u,
                new SimulationCommand(
                    4,
                    0,
                    SimulationCommandType.SetGroupTarget,
                    0,
                    new FPVector2(FP.FromInt(15), FP.FromInt(20))));
            Assert.That(onTime.QueueCommand(authority.Command), Is.True);
            for (int tick = 0; tick < 12; tick++)
            {
                onTime.Step();
                late.Step();
            }

            Assert.That(reconciler.Apply(authority), Is.EqualTo(NetworkCommandApplyResult.RolledBack));
            Assert.That(lateWorld.ComputeStateHash(), Is.EqualTo(onTimeWorld.ComputeStateHash()));
            Assert.That(reconciler.MaximumRollbackDepth, Is.EqualTo(8));
            Assert.That(reconciler.Apply(authority), Is.EqualTo(NetworkCommandApplyResult.Duplicate));

            for (int tick = 0; tick < 20; tick++)
            {
                late.Step();
            }

            var expired = new NetworkAuthoritativeCommand(
                2u,
                2u,
                new SimulationCommand(
                    1,
                    1,
                    SimulationCommandType.SetGroupTarget,
                    1,
                    FPVector2.Zero));
            Assert.That(reconciler.Apply(expired), Is.EqualTo(NetworkCommandApplyResult.SnapshotRequired));
            Assert.That(reconciler.State, Is.EqualTo(ClientSynchronizationState.SnapshotRequired));
        }

        [Test]
        public void HashHistory_ReplayObserverReplacesSpeculativeMismatchWithConfirmation()
        {
            var history = new NetworkAuthorityHashHistory(32);
            history.RecordLocal(5, 10UL);
            history.RecordServer(5, 20UL);
            Assert.That(history.CountUnresolvedMismatches(), Is.EqualTo(1));
            Assert.That(history.ConfirmedSampleCount, Is.Zero);
            history.RecordLocal(5, 20UL);
            Assert.That(history.CountUnresolvedMismatches(), Is.Zero);
            Assert.That(history.ConfirmedSampleCount, Is.EqualTo(1));
            Assert.That(history.LastConfirmedTick, Is.EqualTo(5));
        }

        [Test]
        public void OrderedBuffers_HoldOutOfOrderReliableMessagesUntilGapArrives()
        {
            var authorityBuffer = new OrderedAuthorityCommandBuffer(8);
            NetworkAuthoritativeCommand sequenceOne = Authority(1);
            NetworkAuthoritativeCommand sequenceZero = Authority(0);
            Assert.That(authorityBuffer.TryInsert(sequenceOne), Is.True);
            Assert.That(authorityBuffer.TryTakeNext(out _), Is.False);
            Assert.That(authorityBuffer.TryInsert(sequenceZero), Is.True);
            Assert.That(authorityBuffer.TryTakeNext(out NetworkAuthoritativeCommand first), Is.True);
            Assert.That(first.Command.Sequence, Is.Zero);
            Assert.That(authorityBuffer.TryTakeNext(out NetworkAuthoritativeCommand second), Is.True);
            Assert.That(second.Command.Sequence, Is.EqualTo(1));
            Assert.That(authorityBuffer.TryInsert(sequenceZero), Is.True, "Old duplicates are idempotent.");

            var requestBuffer = new OrderedCommandRequestBuffer(8);
            var requestTwo = new NetworkCommandRequest(2u, 20, 0, FPVector2.Zero);
            var requestOne = new NetworkCommandRequest(1u, 10, 0, FPVector2.Zero);
            Assert.That(requestBuffer.TryInsert(requestTwo), Is.True);
            Assert.That(requestBuffer.TryTakeNext(out _), Is.False);
            Assert.That(requestBuffer.TryInsert(requestOne), Is.True);
            Assert.That(requestBuffer.TryTakeNext(out NetworkCommandRequest firstRequest), Is.True);
            Assert.That(firstRequest.RequestId, Is.EqualTo(1u));
            Assert.That(requestBuffer.TryTakeNext(out NetworkCommandRequest secondRequest), Is.True);
            Assert.That(secondRequest.RequestId, Is.EqualTo(2u));
        }

        private static byte[] DrainWeakNetwork(DeterministicWeakNetwork network, long nowMilliseconds)
        {
            byte[] result = new byte[256];
            int count = 0;
            while (network.TryDequeueDue(nowMilliseconds, out _, out byte[] bytes, out int length))
            {
                Assert.That(length, Is.EqualTo(12));
                result[count++] = bytes[0];
            }

            Array.Resize(ref result, count);
            return result;
        }

        private static SwarmWorld CreateWorld(SwarmConfig config)
        {
            var world = new SwarmWorld(config);
            world.InitializeDeterministicFormation(config.Capacity, 0x1234u);
            return world;
        }

        private static NetworkAuthoritativeCommand Authority(int sequence)
        {
            return new NetworkAuthoritativeCommand(
                0u,
                (uint)sequence,
                new SimulationCommand(
                    10 + sequence,
                    sequence,
                    SimulationCommandType.SetGroupTarget,
                    0,
                    FPVector2.Zero));
        }
    }
}

using System;
using System.IO;
using NUnit.Framework;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Replay;

namespace SwarmECS.Tests.EditMode
{
    public sealed class SwarmReplayFormatTests
    {
        private const int PayloadOffset = SwarmReplaySerializer.HeaderSize;
        private const int NeighborDistancePayloadOffset = 44;
        private const int SpatialModePayloadOffset = 60;
        private const int ConfigHashPayloadOffset = 61;
        private const int CommandCountPayloadOffset = 81;
        private const int FirstCommandTickPayloadOffset = 85;
        private const int FirstCommandTypePayloadOffset = 93;
        private const int SecondCommandValueXPayloadOffset = 113;

        [Test]
        public void MemoryStreamRoundTrip_PreservesConfigCommandsAndCheckpoints()
        {
            SwarmReplay source = CreateReplay();
            using var stream = new MemoryStream();

            SwarmReplaySerializer.Write(stream, source);
            stream.Position = 0;
            SwarmReplay restored = SwarmReplaySerializer.Read(stream);

            Assert.That(restored.LogicHash, Is.EqualTo(source.LogicHash));
            Assert.That(restored.Config.ConfigHash, Is.EqualTo(source.Config.ConfigHash));
            AssertConfigEqual(source.Config, restored.Config);
            Assert.That(restored.Seed, Is.EqualTo(0xDEADBEEFu));
            Assert.That(restored.AgentCount, Is.EqualTo(321));
            Assert.That(restored.FinalTick, Is.EqualTo(12));
            Assert.That(restored.CommandCount, Is.EqualTo(3));
            AssertCommandEqual(source.GetCommand(0), restored.GetCommand(0));
            AssertCommandEqual(source.GetCommand(1), restored.GetCommand(1));
            AssertCommandEqual(source.GetCommand(2), restored.GetCommand(2));
            Assert.That(restored.CheckpointCount, Is.EqualTo(3));
            for (int i = 0; i < restored.CheckpointCount; ++i)
            {
                Assert.That(restored.GetCheckpoint(i).Tick, Is.EqualTo(source.GetCheckpoint(i).Tick));
                Assert.That(restored.GetCheckpoint(i).StateHash, Is.EqualTo(source.GetCheckpoint(i).StateHash));
            }
        }

        [Test]
        public void WireFormat_HasStableMagicSchemaAndLittleEndianFields()
        {
            SwarmReplay replay = CreateReplay();
            byte[] bytes = SwarmReplaySerializer.Serialize(replay);

            Assert.That(
                new string(Array.ConvertAll(bytes[..8], value => (char)value)),
                Is.EqualTo(SwarmReplaySerializer.MagicText));
            Assert.That(bytes[8], Is.EqualTo(1));
            Assert.That(bytes[9], Is.Zero);
            Assert.That(ReadUInt64(bytes, PayloadOffset), Is.EqualTo(SimulationBuildIdentity.CurrentLogicHash));
            Assert.That(ReadInt32(bytes, PayloadOffset + 16), Is.EqualTo(replay.Config.Capacity));
            Assert.That(ReadInt32(bytes, PayloadOffset + 20), Is.EqualTo(replay.Config.FixedDeltaTime.Raw));
        }

        [Test]
        public void CorruptedPayload_IsRejectedByCrc32()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            bytes[^1] ^= 0x40;

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void TruncatedPayload_IsRejected()
        {
            byte[] complete = SwarmReplaySerializer.Serialize(CreateReplay());
            var truncated = new byte[complete.Length - 1];
            Array.Copy(complete, truncated, truncated.Length);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(truncated));
        }

        [Test]
        public void InvalidSpatialEnumWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            bytes[PayloadOffset + SpatialModePayloadOffset] = byte.MaxValue;
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void InvalidCommandEnumWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            bytes[PayloadOffset + FirstCommandTypePayloadOffset] = byte.MaxValue;
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void ConfigHashMismatchWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            bytes[PayloadOffset + ConfigHashPayloadOffset] ^= 1;
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void UnsupportedConfigHashSchemaWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + 8, SwarmConfig.ConfigHashSchemaVersion + 1);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void UnsupportedAuthorityHashSchemaWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + 12, 2);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void ZeroNeighborDistanceWithValidCrc_IsRejectedBeforeRuntimeConstruction()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + NeighborDistancePayloadOffset, 0);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void SpatialModeCommandWithUnusedValueAndValidCrc_IsRejectedAsNonCanonical()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + SecondCommandValueXPayloadOffset, 1);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void CommandTickAboveExecutionBudgetWithValidCrc_IsRejected()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + FirstCommandTickPayloadOffset, int.MaxValue);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void OversizedCommandCountWithValidCrc_IsRejectedBeforeAllocation()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteInt32(bytes, PayloadOffset + CommandCountPayloadOffset, int.MaxValue);
            RewritePayloadCrc(bytes);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void DeclaredPayloadAboveLimit_IsRejectedBeforeAllocation()
        {
            byte[] bytes = SwarmReplaySerializer.Serialize(CreateReplay());
            WriteUInt32(bytes, 12, (uint)SwarmReplayLimits.MaxPayloadBytes + 1u);

            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(bytes));
        }

        [Test]
        public void WrongMagicSchemaLogicHashAndTrailingBytesAreRejected()
        {
            byte[] wrongMagic = SwarmReplaySerializer.Serialize(CreateReplay());
            wrongMagic[0] ^= 1;
            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(wrongMagic));

            byte[] wrongSchema = SwarmReplaySerializer.Serialize(CreateReplay());
            wrongSchema[8] = 2;
            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(wrongSchema));

            byte[] logicMismatch = SwarmReplaySerializer.Serialize(CreateReplay());
            Assert.Throws<InvalidDataException>(() =>
                SwarmReplaySerializer.Deserialize(
                    logicMismatch,
                    SimulationBuildIdentity.CurrentLogicHash ^ 1UL));

            byte[] complete = SwarmReplaySerializer.Serialize(CreateReplay());
            var withTrailingByte = new byte[complete.Length + 1];
            Array.Copy(complete, withTrailingByte, complete.Length);
            Assert.Throws<InvalidDataException>(() => SwarmReplaySerializer.Deserialize(withTrailingByte));
        }

        [Test]
        public void InMemoryModel_RejectsNonCanonicalCommandAndCheckpointOrdering()
        {
            SwarmConfig config = CreateConfig();
            SimulationCommand[] reversedCommands =
            {
                new(2, 2, SimulationCommandType.SetGroupTarget, 0, FPVector2.Zero),
                new(2, 1, SimulationCommandType.SetGroupTarget, 0, FPVector2.Zero),
            };
            Assert.Throws<ArgumentException>(() => new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                1,
                1,
                reversedCommands,
                new[] { new SwarmReplayCheckpoint(3, 1) }));

            SwarmReplayCheckpoint[] duplicateCheckpoints =
            {
                new(3, 1),
                new(3, 2),
            };
            Assert.Throws<ArgumentException>(() => new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                1,
                1,
                Array.Empty<SimulationCommand>(),
                duplicateCheckpoints));
        }

        [Test]
        public void InMemoryModel_RequiresExplicitFinalCheckpointAndBoundsCommandsBeforeIt()
        {
            SwarmConfig config = CreateConfig();
            Assert.Throws<ArgumentException>(() => new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                1,
                1,
                Array.Empty<SimulationCommand>(),
                Array.Empty<SwarmReplayCheckpoint>()));

            SimulationCommand[] commandAtFinalTick =
            {
                new(3, 0, SimulationCommandType.SetGroupTarget, 0, FPVector2.Zero),
            };
            Assert.Throws<ArgumentException>(() => new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                1,
                1,
                3,
                commandAtFinalTick,
                new[] { new SwarmReplayCheckpoint(3, 1) }));
        }

        [Test]
        public void InMemoryModel_RejectsExecutionWorkBeyondTheBoundedReplayBudget()
        {
            SwarmConfig config = CreateConfig();
            int finalTick = SwarmReplayLimits.MaxTick;

            Assert.Throws<ArgumentException>(() => new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                config,
                1,
                50,
                finalTick,
                Array.Empty<SimulationCommand>(),
                new[] { new SwarmReplayCheckpoint(finalTick, 1) }));
        }

        private static SwarmReplay CreateReplay()
        {
            SimulationCommand[] commands =
            {
                new(
                    2,
                    10,
                    SimulationCommandType.SetGroupTarget,
                    1,
                    new FPVector2(FP.FromRatio(7, 3), FP.FromRatio(-11, 5))),
                new(
                    2,
                    11,
                    SimulationCommandType.SetSpatialIndexMode,
                    (byte)SpatialIndexMode.KdTreeKNearest,
                    FPVector2.Zero),
                new(
                    9,
                    12,
                    SimulationCommandType.SetGroupTarget,
                    3,
                    new FPVector2(FP.FromInt(-17), FP.FromInt(23))),
            };
            SwarmReplayCheckpoint[] checkpoints =
            {
                new(0, 0x0102030405060708UL),
                new(2, 0x8877665544332211UL),
                new(12, 0xFEDCBA9876543210UL),
            };
            return new SwarmReplay(
                SimulationBuildIdentity.CurrentLogicHash,
                CreateConfig(),
                0xDEADBEEFu,
                321,
                commands,
                checkpoints);
        }

        private static SwarmConfig CreateConfig()
        {
            return new SwarmConfig(
                12_345,
                FP.FromRatio(1, 60),
                FP.FromRatio(3, 8),
                FP.FromRatio(13, 2),
                FP.FromInt(27),
                SwarmConfig.DefaultMaxTurnStep,
                FP.FromRatio(19, 4),
                13,
                FP.FromRatio(9, 4),
                FP.FromInt(96),
                SpatialIndexMode.KdTree);
        }

        private static void AssertConfigEqual(SwarmConfig expected, SwarmConfig actual)
        {
            Assert.That(actual.Capacity, Is.EqualTo(expected.Capacity));
            Assert.That(actual.FixedDeltaTime.Raw, Is.EqualTo(expected.FixedDeltaTime.Raw));
            Assert.That(actual.AgentRadius.Raw, Is.EqualTo(expected.AgentRadius.Raw));
            Assert.That(actual.MaxSpeed.Raw, Is.EqualTo(expected.MaxSpeed.Raw));
            Assert.That(actual.MaxAcceleration.Raw, Is.EqualTo(expected.MaxAcceleration.Raw));
            Assert.That(actual.MaxTurnStep.X.Raw, Is.EqualTo(expected.MaxTurnStep.X.Raw));
            Assert.That(actual.MaxTurnStep.Y.Raw, Is.EqualTo(expected.MaxTurnStep.Y.Raw));
            Assert.That(actual.NeighborDistance.Raw, Is.EqualTo(expected.NeighborDistance.Raw));
            Assert.That(actual.MaxNeighbors, Is.EqualTo(expected.MaxNeighbors));
            Assert.That(actual.TimeHorizon.Raw, Is.EqualTo(expected.TimeHorizon.Raw));
            Assert.That(actual.WorldHalfExtent.Raw, Is.EqualTo(expected.WorldHalfExtent.Raw));
            Assert.That(actual.SpatialIndexMode, Is.EqualTo(expected.SpatialIndexMode));
            Assert.That(actual.ConfigHash, Is.EqualTo(expected.ConfigHash));
        }

        private static void AssertCommandEqual(SimulationCommand expected, SimulationCommand actual)
        {
            Assert.That(actual.Tick, Is.EqualTo(expected.Tick));
            Assert.That(actual.Sequence, Is.EqualTo(expected.Sequence));
            Assert.That(actual.Type, Is.EqualTo(expected.Type));
            Assert.That(actual.Group, Is.EqualTo(expected.Group));
            Assert.That(actual.Value.X.Raw, Is.EqualTo(expected.Value.X.Raw));
            Assert.That(actual.Value.Y.Raw, Is.EqualTo(expected.Value.Y.Raw));
        }

        private static void RewritePayloadCrc(byte[] bytes)
        {
            int payloadLength = ReadInt32(bytes, 12);
            uint crc = ComputeCrc32(bytes, PayloadOffset, payloadLength);
            WriteUInt32(bytes, 16, crc);
        }

        private static uint ComputeCrc32(byte[] bytes, int offset, int count)
        {
            const uint polynomial = 0xEDB88320u;
            uint crc = uint.MaxValue;
            for (int i = offset; i < offset + count; ++i)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; ++bit)
                {
                    uint mask = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (polynomial & mask);
                }
            }

            return ~crc;
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return unchecked((int)ReadUInt32(bytes, offset));
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return (uint)(bytes[offset] |
                         (bytes[offset + 1] << 8) |
                         (bytes[offset + 2] << 16) |
                         (bytes[offset + 3] << 24));
        }

        private static ulong ReadUInt64(byte[] bytes, int offset)
        {
            return ReadUInt32(bytes, offset) | ((ulong)ReadUInt32(bytes, offset + 4) << 32);
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            WriteUInt32(bytes, offset, unchecked((uint)value));
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}

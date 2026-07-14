using System;
using System.IO;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Determinism;
using SwarmECS.Simulation.Netcode;

namespace SwarmECS.Simulation.Replay
{
    /// <summary>
    /// Versioned .swarmreplay binary IO. Every numeric field is explicitly encoded
    /// little-endian; payload CRC covers all replay data after the fixed header.
    /// </summary>
    public static class SwarmReplaySerializer
    {
        public const string MagicText = "SWARMREP";
        public const ushort CurrentSchemaVersion = 1;
        public const int HeaderSize = 20;

        private const ushort HeaderFlags = 0;
        private const int CommandSize = 18;
        private const int CheckpointSize = 12;
        private const int MinimumPayloadSize = 89;

        private static readonly byte[] MagicBytes =
        {
            (byte)'S', (byte)'W', (byte)'A', (byte)'R',
            (byte)'M', (byte)'R', (byte)'E', (byte)'P',
        };

        public static byte[] Serialize(SwarmReplay replay)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            using (var stream = new MemoryStream(HeaderSize + EstimatePayloadSize(replay)))
            {
                Write(stream, replay);
                return stream.ToArray();
            }
        }

        public static void Write(Stream destination, SwarmReplay replay)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!destination.CanWrite)
            {
                throw new ArgumentException("Replay destination must be writable.", nameof(destination));
            }

            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            byte[] payload = BuildPayload(replay);
            uint payloadCrc = ReplayCrc32.Compute(payload, 0, payload.Length);

            destination.Write(MagicBytes, 0, MagicBytes.Length);
            ReplayLittleEndian.WriteUInt16(destination, CurrentSchemaVersion);
            ReplayLittleEndian.WriteUInt16(destination, HeaderFlags);
            ReplayLittleEndian.WriteUInt32(destination, (uint)payload.Length);
            ReplayLittleEndian.WriteUInt32(destination, payloadCrc);
            destination.Write(payload, 0, payload.Length);
        }

        public static SwarmReplay Deserialize(byte[] bytes)
        {
            return Deserialize(bytes, SimulationBuildIdentity.CurrentLogicHash);
        }

        public static SwarmReplay Deserialize(byte[] bytes, ulong expectedLogicHash)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            using (var stream = new MemoryStream(bytes, false))
            {
                return Read(stream, expectedLogicHash);
            }
        }

        public static SwarmReplay Read(Stream source)
        {
            return Read(source, SimulationBuildIdentity.CurrentLogicHash);
        }

        public static SwarmReplay Read(Stream source, ulong expectedLogicHash)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!source.CanRead)
            {
                throw new ArgumentException("Replay source must be readable.", nameof(source));
            }

            SwarmReplay.ValidateLogicHash(expectedLogicHash);
            for (int i = 0; i < MagicBytes.Length; ++i)
            {
                if (ReadRequiredByte(source) != MagicBytes[i])
                {
                    throw new InvalidDataException("Replay magic does not match SWARMREP.");
                }
            }

            ushort schemaVersion = ReplayLittleEndian.ReadUInt16(source);
            if (schemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException("Replay schema version is not supported.");
            }

            ushort flags = ReplayLittleEndian.ReadUInt16(source);
            if (flags != HeaderFlags)
            {
                throw new InvalidDataException("Replay header contains unsupported flags.");
            }

            uint payloadLength = ReplayLittleEndian.ReadUInt32(source);
            uint expectedCrc = ReplayLittleEndian.ReadUInt32(source);
            if (payloadLength < MinimumPayloadSize || payloadLength > SwarmReplayLimits.MaxPayloadBytes)
            {
                throw new InvalidDataException("Replay payload length is outside the supported limit.");
            }

            var payload = new byte[(int)payloadLength];
            ReadExactly(source, payload, 0, payload.Length);
            if (source.ReadByte() != -1)
            {
                throw new InvalidDataException("Replay contains trailing bytes after its declared payload.");
            }

            uint actualCrc = ReplayCrc32.Compute(payload, 0, payload.Length);
            if (actualCrc != expectedCrc)
            {
                throw new InvalidDataException("Replay payload CRC32 does not match.");
            }

            return ParsePayload(payload, expectedLogicHash);
        }

        private static byte[] BuildPayload(SwarmReplay replay)
        {
            int estimatedLength = EstimatePayloadSize(replay);
            if (estimatedLength > SwarmReplayLimits.MaxPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(replay), "Replay payload exceeds the supported limit.");
            }

            using (var payload = new MemoryStream(estimatedLength))
            {
                SwarmConfig config = replay.Config;
                ReplayLittleEndian.WriteUInt64(payload, replay.LogicHash);
                ReplayLittleEndian.WriteInt32(payload, SwarmConfig.ConfigHashSchemaVersion);
                ReplayLittleEndian.WriteInt32(payload, WorldAuthorityDiagnostics.SchemaVersion);
                ReplayLittleEndian.WriteInt32(payload, config.Capacity);
                ReplayLittleEndian.WriteInt32(payload, config.FixedDeltaTime.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.AgentRadius.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.MaxSpeed.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.MaxAcceleration.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.MaxTurnStep.X.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.MaxTurnStep.Y.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.NeighborDistance.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.MaxNeighbors);
                ReplayLittleEndian.WriteInt32(payload, config.TimeHorizon.Raw);
                ReplayLittleEndian.WriteInt32(payload, config.WorldHalfExtent.Raw);
                payload.WriteByte((byte)config.SpatialIndexMode);
                ReplayLittleEndian.WriteUInt64(payload, config.ConfigHash);
                ReplayLittleEndian.WriteUInt32(payload, replay.Seed);
                ReplayLittleEndian.WriteInt32(payload, replay.AgentCount);
                ReplayLittleEndian.WriteInt32(payload, replay.FinalTick);
                ReplayLittleEndian.WriteInt32(payload, replay.CommandCount);

                for (int i = 0; i < replay.CommandCount; ++i)
                {
                    SimulationCommand command = replay.GetCommand(i);
                    ReplayLittleEndian.WriteInt32(payload, command.Tick);
                    ReplayLittleEndian.WriteInt32(payload, command.Sequence);
                    payload.WriteByte((byte)command.Type);
                    payload.WriteByte(command.Group);
                    ReplayLittleEndian.WriteInt32(payload, command.Value.X.Raw);
                    ReplayLittleEndian.WriteInt32(payload, command.Value.Y.Raw);
                }

                ReplayLittleEndian.WriteInt32(payload, replay.CheckpointCount);
                for (int i = 0; i < replay.CheckpointCount; ++i)
                {
                    SwarmReplayCheckpoint checkpoint = replay.GetCheckpoint(i);
                    ReplayLittleEndian.WriteInt32(payload, checkpoint.Tick);
                    ReplayLittleEndian.WriteUInt64(payload, checkpoint.StateHash);
                }

                return payload.ToArray();
            }
        }

        private static SwarmReplay ParsePayload(byte[] payload, ulong expectedLogicHash)
        {
            var reader = new ReplayPayloadReader(payload);
            ulong logicHash = reader.ReadUInt64();
            if (logicHash != expectedLogicHash)
            {
                throw new InvalidDataException("Replay logic hash is incompatible with this simulation build.");
            }

            int configHashSchema = reader.ReadInt32();
            if (configHashSchema != SwarmConfig.ConfigHashSchemaVersion)
            {
                throw new InvalidDataException("Replay configuration hash schema is not supported.");
            }

            int authorityHashSchema = reader.ReadInt32();
            if (authorityHashSchema != WorldAuthorityDiagnostics.SchemaVersion)
            {
                throw new InvalidDataException("Replay authority hash schema is not supported.");
            }

            int capacity = reader.ReadInt32();
            FP fixedDeltaTime = FP.FromRaw(reader.ReadInt32());
            FP agentRadius = FP.FromRaw(reader.ReadInt32());
            FP maxSpeed = FP.FromRaw(reader.ReadInt32());
            FP maxAcceleration = FP.FromRaw(reader.ReadInt32());
            FPVector2 maxTurnStep = new(
                FP.FromRaw(reader.ReadInt32()),
                FP.FromRaw(reader.ReadInt32()));
            FP neighborDistance = FP.FromRaw(reader.ReadInt32());
            int maxNeighbors = reader.ReadInt32();
            FP timeHorizon = FP.FromRaw(reader.ReadInt32());
            FP worldHalfExtent = FP.FromRaw(reader.ReadInt32());
            byte spatialModeRaw = reader.ReadByte();
            if (spatialModeRaw > (byte)SpatialIndexMode.KdTreeKNearest)
            {
                throw new InvalidDataException("Replay contains an invalid spatial-index mode.");
            }

            ulong storedConfigHash = reader.ReadUInt64();
            uint seed = reader.ReadUInt32();
            int agentCount = reader.ReadInt32();
            int finalTick = reader.ReadInt32();
            int commandCount = reader.ReadInt32();
            ValidateDecodedCount(commandCount, SwarmReplayLimits.MaxCommandCount, "command");
            try
            {
                // Reject hostile agent/tick/command combinations before allocating
                // or decoding the variable command stream. Checkpoints are added to
                // the same budget after their count becomes available.
                SwarmReplay.ValidateWorkload(agentCount, finalTick, commandCount, 0);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Replay execution workload is too large.", exception);
            }
            long commandBytesAndCheckpointCount = ((long)commandCount * CommandSize) + sizeof(int);
            if (commandBytesAndCheckpointCount > reader.Remaining)
            {
                throw new InvalidDataException("Replay command stream is truncated.");
            }

            SwarmConfig config;
            try
            {
                config = new SwarmConfig(
                    capacity,
                    fixedDeltaTime,
                    agentRadius,
                    maxSpeed,
                    maxAcceleration,
                    maxTurnStep,
                    neighborDistance,
                    maxNeighbors,
                    timeHorizon,
                    worldHalfExtent,
                    (SpatialIndexMode)spatialModeRaw);
                SwarmReplay.ValidateConfig(config);
                SwarmReplay.ValidateAgentCount(agentCount, config.Capacity);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Replay contains an invalid simulation configuration.", exception);
            }

            if (config.ConfigHash != storedConfigHash)
            {
                throw new InvalidDataException("Replay configuration hash does not match its serialized fields.");
            }

            var commands = new SimulationCommand[commandCount];
            for (int i = 0; i < commands.Length; ++i)
            {
                int tick = reader.ReadInt32();
                int sequence = reader.ReadInt32();
                SimulationCommandType type = (SimulationCommandType)reader.ReadByte();
                byte group = reader.ReadByte();
                FPVector2 value = new(
                    FP.FromRaw(reader.ReadInt32()),
                    FP.FromRaw(reader.ReadInt32()));
                commands[i] = new SimulationCommand(tick, sequence, type, group, value);
            }

            int checkpointCount = reader.ReadInt32();
            ValidateDecodedCount(checkpointCount, SwarmReplayLimits.MaxCheckpointCount, "checkpoint");
            try
            {
                SwarmReplay.ValidateWorkload(agentCount, finalTick, commandCount, checkpointCount);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Replay execution workload is too large.", exception);
            }
            long checkpointBytes = (long)checkpointCount * CheckpointSize;
            if (checkpointBytes != reader.Remaining)
            {
                throw new InvalidDataException(
                    checkpointBytes > reader.Remaining
                        ? "Replay checkpoint stream is truncated."
                        : "Replay payload contains undeclared data.");
            }

            var checkpoints = new SwarmReplayCheckpoint[checkpointCount];
            for (int i = 0; i < checkpoints.Length; ++i)
            {
                checkpoints[i] = new SwarmReplayCheckpoint(reader.ReadInt32(), reader.ReadUInt64());
            }

            try
            {
                return new SwarmReplay(
                    logicHash,
                    config,
                    seed,
                    agentCount,
                    finalTick,
                    commands,
                    checkpoints);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("Replay command or checkpoint stream is invalid.", exception);
            }
        }

        private static int EstimatePayloadSize(SwarmReplay replay)
        {
            long size = MinimumPayloadSize +
                        ((long)replay.CommandCount * CommandSize) +
                        ((long)replay.CheckpointCount * CheckpointSize);
            if (size > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(replay), "Replay payload is too large.");
            }

            return (int)size;
        }

        private static void ValidateDecodedCount(int count, int maximum, string label)
        {
            if (count < 0 || count > maximum)
            {
                throw new InvalidDataException("Replay " + label + " count is outside the supported limit.");
            }
        }

        private static int ReadRequiredByte(Stream source)
        {
            int value = source.ReadByte();
            if (value < 0)
            {
                throw new InvalidDataException("Replay is truncated.");
            }

            return value;
        }

        private static void ReadExactly(Stream source, byte[] destination, int offset, int count)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read = source.Read(destination, offset + readTotal, count - readTotal);
                if (read <= 0)
                {
                    throw new InvalidDataException("Replay payload is truncated.");
                }

                readTotal += read;
            }
        }

        private struct ReplayPayloadReader
        {
            private readonly byte[] _bytes;
            private int _offset;

            public ReplayPayloadReader(byte[] bytes)
            {
                _bytes = bytes;
                _offset = 0;
            }

            public int Remaining => _bytes.Length - _offset;

            public byte ReadByte()
            {
                Require(1);
                return _bytes[_offset++];
            }

            public int ReadInt32()
            {
                return unchecked((int)ReadUInt32());
            }

            public uint ReadUInt32()
            {
                Require(4);
                uint value = (uint)(_bytes[_offset] |
                                    (_bytes[_offset + 1] << 8) |
                                    (_bytes[_offset + 2] << 16) |
                                    (_bytes[_offset + 3] << 24));
                _offset += 4;
                return value;
            }

            public ulong ReadUInt64()
            {
                uint low = ReadUInt32();
                uint high = ReadUInt32();
                return low | ((ulong)high << 32);
            }

            private void Require(int count)
            {
                if (count < 0 || Remaining < count)
                {
                    throw new InvalidDataException("Replay payload is truncated.");
                }
            }
        }

        private static class ReplayLittleEndian
        {
            public static void WriteUInt16(Stream stream, ushort value)
            {
                stream.WriteByte((byte)value);
                stream.WriteByte((byte)(value >> 8));
            }

            public static void WriteInt32(Stream stream, int value)
            {
                WriteUInt32(stream, unchecked((uint)value));
            }

            public static void WriteUInt32(Stream stream, uint value)
            {
                stream.WriteByte((byte)value);
                stream.WriteByte((byte)(value >> 8));
                stream.WriteByte((byte)(value >> 16));
                stream.WriteByte((byte)(value >> 24));
            }

            public static void WriteUInt64(Stream stream, ulong value)
            {
                WriteUInt32(stream, (uint)value);
                WriteUInt32(stream, (uint)(value >> 32));
            }

            public static ushort ReadUInt16(Stream stream)
            {
                int first = ReadRequiredByte(stream);
                int second = ReadRequiredByte(stream);
                return (ushort)(first | (second << 8));
            }

            public static uint ReadUInt32(Stream stream)
            {
                uint first = (uint)ReadRequiredByte(stream);
                uint second = (uint)ReadRequiredByte(stream);
                uint third = (uint)ReadRequiredByte(stream);
                uint fourth = (uint)ReadRequiredByte(stream);
                return first | (second << 8) | (third << 16) | (fourth << 24);
            }
        }

        private static class ReplayCrc32
        {
            private const uint Polynomial = 0xEDB88320u;

            public static uint Compute(byte[] bytes, int offset, int count)
            {
                uint crc = uint.MaxValue;
                int end = offset + count;
                for (int i = offset; i < end; ++i)
                {
                    crc ^= bytes[i];
                    for (int bit = 0; bit < 8; ++bit)
                    {
                        uint mask = (uint)-(int)(crc & 1u);
                        crc = (crc >> 1) ^ (Polynomial & mask);
                    }
                }

                return ~crc;
            }
        }
    }
}

using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Determinism;
using SwarmECS.Simulation.Replay;

namespace SwarmECS.Simulation.Netcode.Transport
{
    public enum SwarmNetworkMessageType : byte
    {
        Handshake = 1,
        Welcome = 2,
        Reject = 3,
        SessionStart = 4,
        CommandRequest = 5,
        AuthoritativeCommand = 6,
        HashTelemetry = 7,
        SessionComplete = 8,
        SnapshotRequired = 9,
    }

    public enum SwarmNetworkRejectReason : byte
    {
        None = 0,
        InvalidPeer = 1,
        IdentityMismatch = 2,
        SessionFull = 3,
        MalformedPayload = 4,
    }

    public readonly struct NetworkCompatibilityIdentity
    {
        public const byte FixedPointFractionalBits = 16;

        public NetworkCompatibilityIdentity(
            ushort protocolVersion,
            ulong logicHash,
            ulong configHash,
            int configSchemaVersion,
            ushort replaySchemaVersion,
            ushort snapshotSchemaVersion,
            int authoritySchemaVersion,
            byte fixedPointFractionalBits,
            int agentCount,
            uint seed)
        {
            ProtocolVersion = protocolVersion;
            LogicHash = logicHash;
            ConfigHash = configHash;
            ConfigSchemaVersion = configSchemaVersion;
            ReplaySchemaVersion = replaySchemaVersion;
            SnapshotSchemaVersion = snapshotSchemaVersion;
            AuthoritySchemaVersion = authoritySchemaVersion;
            FixedPointBits = fixedPointFractionalBits;
            AgentCount = agentCount;
            Seed = seed;
        }

        public ushort ProtocolVersion { get; }

        public ulong LogicHash { get; }

        public ulong ConfigHash { get; }

        public int ConfigSchemaVersion { get; }

        public ushort ReplaySchemaVersion { get; }

        public ushort SnapshotSchemaVersion { get; }

        public int AuthoritySchemaVersion { get; }

        public byte FixedPointBits { get; }

        public int AgentCount { get; }

        public uint Seed { get; }

        public static NetworkCompatibilityIdentity Create(
            SwarmConfig config,
            int agentCount,
            uint seed)
        {
            return new NetworkCompatibilityIdentity(
                SwarmUdpPacketCodec.ProtocolVersion,
                SimulationBuildIdentity.CurrentLogicHash,
                config.ConfigHash,
                SwarmConfig.ConfigHashSchemaVersion,
                SwarmReplaySerializer.CurrentSchemaVersion,
                WorldSnapshotRing.CurrentSchemaVersion,
                WorldAuthorityDiagnostics.SchemaVersion,
                FixedPointFractionalBits,
                agentCount,
                seed);
        }

        public bool IsCompatibleWith(in NetworkCompatibilityIdentity other)
        {
            return ProtocolVersion == other.ProtocolVersion &&
                LogicHash == other.LogicHash &&
                ConfigHash == other.ConfigHash &&
                ConfigSchemaVersion == other.ConfigSchemaVersion &&
                ReplaySchemaVersion == other.ReplaySchemaVersion &&
                SnapshotSchemaVersion == other.SnapshotSchemaVersion &&
                AuthoritySchemaVersion == other.AuthoritySchemaVersion &&
                FixedPointBits == other.FixedPointBits &&
                AgentCount == other.AgentCount &&
                Seed == other.Seed;
        }
    }

    public readonly struct NetworkWelcome
    {
        public NetworkWelcome(
            uint sessionId,
            uint assignedPeerId,
            NetworkCompatibilityIdentity identity,
            int inputDelayTicks,
            int predictionLeadTicks,
            int finalTick)
        {
            SessionId = sessionId;
            AssignedPeerId = assignedPeerId;
            Identity = identity;
            InputDelayTicks = inputDelayTicks;
            PredictionLeadTicks = predictionLeadTicks;
            FinalTick = finalTick;
        }

        public uint SessionId { get; }
        public uint AssignedPeerId { get; }
        public NetworkCompatibilityIdentity Identity { get; }
        public int InputDelayTicks { get; }
        public int PredictionLeadTicks { get; }
        public int FinalTick { get; }
    }

    public readonly struct NetworkSessionStart
    {
        public NetworkSessionStart(int serverTick, int finalTick, int startDelayMilliseconds)
        {
            ServerTick = serverTick;
            FinalTick = finalTick;
            StartDelayMilliseconds = startDelayMilliseconds;
        }

        public int ServerTick { get; }
        public int FinalTick { get; }
        public int StartDelayMilliseconds { get; }
    }

    public readonly struct NetworkCommandRequest
    {
        public NetworkCommandRequest(
            uint requestId,
            int clientTick,
            byte group,
            FPVector2 target)
        {
            RequestId = requestId;
            ClientTick = clientTick;
            Group = group;
            Target = target;
        }

        public uint RequestId { get; }
        public int ClientTick { get; }
        public byte Group { get; }
        public FPVector2 Target { get; }
    }

    public readonly struct NetworkAuthoritativeCommand
    {
        public NetworkAuthoritativeCommand(
            uint sourcePeerId,
            uint requestId,
            SimulationCommand command)
        {
            SourcePeerId = sourcePeerId;
            RequestId = requestId;
            Command = command;
        }

        public uint SourcePeerId { get; }
        public uint RequestId { get; }
        public SimulationCommand Command { get; }
    }

    public readonly struct NetworkHashTelemetry
    {
        public NetworkHashTelemetry(int tick, ulong authorityHash, int lastAuthoritySequence)
        {
            Tick = tick;
            AuthorityHash = authorityHash;
            LastAuthoritySequence = lastAuthoritySequence;
        }

        public int Tick { get; }
        public ulong AuthorityHash { get; }
        public int LastAuthoritySequence { get; }
    }

    public readonly struct NetworkSessionComplete
    {
        public NetworkSessionComplete(int finalTick, ulong finalAuthorityHash, int totalCommands)
        {
            FinalTick = finalTick;
            FinalAuthorityHash = finalAuthorityHash;
            TotalCommands = totalCommands;
        }

        public int FinalTick { get; }
        public ulong FinalAuthorityHash { get; }
        public int TotalCommands { get; }
    }

    public readonly struct NetworkSnapshotRequired
    {
        public NetworkSnapshotRequired(int commandTick, int currentTick, int earliestRestorableTick)
        {
            CommandTick = commandTick;
            CurrentTick = currentTick;
            EarliestRestorableTick = earliestRestorableTick;
        }

        public int CommandTick { get; }
        public int CurrentTick { get; }
        public int EarliestRestorableTick { get; }
    }

    public static class SwarmNetworkMessageCodec
    {
        public const int IdentitySize = 39;
        public const int HandshakeSize = 1 + IdentitySize;
        public const int WelcomeSize = 1 + 4 + 4 + IdentitySize + 4 + 4 + 4;
        public const int RejectSize = 2;
        public const int SessionStartSize = 13;
        public const int CommandRequestSize = 18;
        public const int AuthoritativeCommandSize = 27;
        public const int HashTelemetrySize = 17;
        public const int SessionCompleteSize = 17;
        public const int SnapshotRequiredSize = 13;

        public static bool TryReadMessageType(
            byte[] payload,
            int offset,
            int count,
            out SwarmNetworkMessageType type)
        {
            type = default;
            if (payload == null || count < 1 || offset < 0 || offset > payload.Length - count)
            {
                return false;
            }

            byte value = payload[offset];
            if (value < (byte)SwarmNetworkMessageType.Handshake ||
                value > (byte)SwarmNetworkMessageType.SnapshotRequired)
            {
                return false;
            }

            type = (SwarmNetworkMessageType)value;
            return true;
        }

        public static int WriteHandshake(byte[] destination, in NetworkCompatibilityIdentity identity)
        {
            destination[0] = (byte)SwarmNetworkMessageType.Handshake;
            WriteIdentity(destination, 1, identity);
            return HandshakeSize;
        }

        public static bool TryReadHandshake(
            byte[] payload,
            int offset,
            int count,
            out NetworkCompatibilityIdentity identity)
        {
            identity = default;
            return HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.Handshake, HandshakeSize) &&
                TryReadIdentity(payload, offset + 1, out identity);
        }

        public static int WriteWelcome(byte[] destination, in NetworkWelcome welcome)
        {
            destination[0] = (byte)SwarmNetworkMessageType.Welcome;
            NetworkBinary.WriteUInt32(destination, 1, welcome.SessionId);
            NetworkBinary.WriteUInt32(destination, 5, welcome.AssignedPeerId);
            WriteIdentity(destination, 9, welcome.Identity);
            int cursor = 9 + IdentitySize;
            NetworkBinary.WriteInt32(destination, cursor, welcome.InputDelayTicks);
            NetworkBinary.WriteInt32(destination, cursor + 4, welcome.PredictionLeadTicks);
            NetworkBinary.WriteInt32(destination, cursor + 8, welcome.FinalTick);
            return WelcomeSize;
        }

        public static bool TryReadWelcome(
            byte[] payload,
            int offset,
            int count,
            out NetworkWelcome welcome)
        {
            welcome = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.Welcome, WelcomeSize) ||
                !TryReadIdentity(payload, offset + 9, out NetworkCompatibilityIdentity identity))
            {
                return false;
            }

            int cursor = offset + 9 + IdentitySize;
            welcome = new NetworkWelcome(
                NetworkBinary.ReadUInt32(payload, offset + 1),
                NetworkBinary.ReadUInt32(payload, offset + 5),
                identity,
                NetworkBinary.ReadInt32(payload, cursor),
                NetworkBinary.ReadInt32(payload, cursor + 4),
                NetworkBinary.ReadInt32(payload, cursor + 8));
            return true;
        }

        public static int WriteReject(byte[] destination, SwarmNetworkRejectReason reason)
        {
            destination[0] = (byte)SwarmNetworkMessageType.Reject;
            destination[1] = (byte)reason;
            return RejectSize;
        }

        public static bool TryReadReject(
            byte[] payload,
            int offset,
            int count,
            out SwarmNetworkRejectReason reason)
        {
            reason = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.Reject, RejectSize) ||
                payload[offset + 1] > (byte)SwarmNetworkRejectReason.MalformedPayload)
            {
                return false;
            }

            reason = (SwarmNetworkRejectReason)payload[offset + 1];
            return true;
        }

        public static int WriteSessionStart(byte[] destination, in NetworkSessionStart start)
        {
            destination[0] = (byte)SwarmNetworkMessageType.SessionStart;
            NetworkBinary.WriteInt32(destination, 1, start.ServerTick);
            NetworkBinary.WriteInt32(destination, 5, start.FinalTick);
            NetworkBinary.WriteInt32(destination, 9, start.StartDelayMilliseconds);
            return SessionStartSize;
        }

        public static bool TryReadSessionStart(
            byte[] payload,
            int offset,
            int count,
            out NetworkSessionStart start)
        {
            start = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.SessionStart, SessionStartSize))
            {
                return false;
            }

            start = new NetworkSessionStart(
                NetworkBinary.ReadInt32(payload, offset + 1),
                NetworkBinary.ReadInt32(payload, offset + 5),
                NetworkBinary.ReadInt32(payload, offset + 9));
            return true;
        }

        public static int WriteCommandRequest(byte[] destination, in NetworkCommandRequest request)
        {
            destination[0] = (byte)SwarmNetworkMessageType.CommandRequest;
            NetworkBinary.WriteUInt32(destination, 1, request.RequestId);
            NetworkBinary.WriteInt32(destination, 5, request.ClientTick);
            destination[9] = request.Group;
            NetworkBinary.WriteInt32(destination, 10, request.Target.X.Raw);
            NetworkBinary.WriteInt32(destination, 14, request.Target.Y.Raw);
            return CommandRequestSize;
        }

        public static bool TryReadCommandRequest(
            byte[] payload,
            int offset,
            int count,
            out NetworkCommandRequest request)
        {
            request = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.CommandRequest, CommandRequestSize))
            {
                return false;
            }

            byte group = payload[offset + 9];
            if (group >= SwarmWorld.GroupCount)
            {
                return false;
            }

            request = new NetworkCommandRequest(
                NetworkBinary.ReadUInt32(payload, offset + 1),
                NetworkBinary.ReadInt32(payload, offset + 5),
                group,
                new FPVector2(
                    FP.FromRaw(NetworkBinary.ReadInt32(payload, offset + 10)),
                    FP.FromRaw(NetworkBinary.ReadInt32(payload, offset + 14))));
            return true;
        }

        public static int WriteAuthoritativeCommand(
            byte[] destination,
            in NetworkAuthoritativeCommand authority)
        {
            SimulationCommand command = authority.Command;
            destination[0] = (byte)SwarmNetworkMessageType.AuthoritativeCommand;
            NetworkBinary.WriteUInt32(destination, 1, authority.SourcePeerId);
            NetworkBinary.WriteUInt32(destination, 5, authority.RequestId);
            NetworkBinary.WriteInt32(destination, 9, command.Tick);
            NetworkBinary.WriteInt32(destination, 13, command.Sequence);
            destination[17] = (byte)command.Type;
            destination[18] = command.Group;
            NetworkBinary.WriteInt32(destination, 19, command.Value.X.Raw);
            NetworkBinary.WriteInt32(destination, 23, command.Value.Y.Raw);
            return AuthoritativeCommandSize;
        }

        public static bool TryReadAuthoritativeCommand(
            byte[] payload,
            int offset,
            int count,
            out NetworkAuthoritativeCommand authority)
        {
            authority = default;
            if (!HasTypeAndSize(
                    payload,
                    offset,
                    count,
                    SwarmNetworkMessageType.AuthoritativeCommand,
                    AuthoritativeCommandSize))
            {
                return false;
            }

            byte commandType = payload[offset + 17];
            byte group = payload[offset + 18];
            if (commandType > (byte)SimulationCommandType.SetSpatialIndexMode ||
                (commandType == (byte)SimulationCommandType.SetGroupTarget && group >= SwarmWorld.GroupCount) ||
                (commandType == (byte)SimulationCommandType.SetSpatialIndexMode &&
                    group > (byte)SpatialIndexMode.KdTreeKNearest))
            {
                return false;
            }

            authority = new NetworkAuthoritativeCommand(
                NetworkBinary.ReadUInt32(payload, offset + 1),
                NetworkBinary.ReadUInt32(payload, offset + 5),
                new SimulationCommand(
                    NetworkBinary.ReadInt32(payload, offset + 9),
                    NetworkBinary.ReadInt32(payload, offset + 13),
                    (SimulationCommandType)commandType,
                    group,
                    new FPVector2(
                        FP.FromRaw(NetworkBinary.ReadInt32(payload, offset + 19)),
                        FP.FromRaw(NetworkBinary.ReadInt32(payload, offset + 23)))));
            return true;
        }

        public static int WriteHashTelemetry(byte[] destination, in NetworkHashTelemetry telemetry)
        {
            destination[0] = (byte)SwarmNetworkMessageType.HashTelemetry;
            NetworkBinary.WriteInt32(destination, 1, telemetry.Tick);
            NetworkBinary.WriteUInt64(destination, 5, telemetry.AuthorityHash);
            NetworkBinary.WriteInt32(destination, 13, telemetry.LastAuthoritySequence);
            return HashTelemetrySize;
        }

        public static bool TryReadHashTelemetry(
            byte[] payload,
            int offset,
            int count,
            out NetworkHashTelemetry telemetry)
        {
            telemetry = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.HashTelemetry, HashTelemetrySize))
            {
                return false;
            }

            telemetry = new NetworkHashTelemetry(
                NetworkBinary.ReadInt32(payload, offset + 1),
                NetworkBinary.ReadUInt64(payload, offset + 5),
                NetworkBinary.ReadInt32(payload, offset + 13));
            return true;
        }

        public static int WriteSessionComplete(byte[] destination, in NetworkSessionComplete complete)
        {
            destination[0] = (byte)SwarmNetworkMessageType.SessionComplete;
            NetworkBinary.WriteInt32(destination, 1, complete.FinalTick);
            NetworkBinary.WriteUInt64(destination, 5, complete.FinalAuthorityHash);
            NetworkBinary.WriteInt32(destination, 13, complete.TotalCommands);
            return SessionCompleteSize;
        }

        public static bool TryReadSessionComplete(
            byte[] payload,
            int offset,
            int count,
            out NetworkSessionComplete complete)
        {
            complete = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.SessionComplete, SessionCompleteSize))
            {
                return false;
            }

            complete = new NetworkSessionComplete(
                NetworkBinary.ReadInt32(payload, offset + 1),
                NetworkBinary.ReadUInt64(payload, offset + 5),
                NetworkBinary.ReadInt32(payload, offset + 13));
            return true;
        }

        public static int WriteSnapshotRequired(byte[] destination, in NetworkSnapshotRequired required)
        {
            destination[0] = (byte)SwarmNetworkMessageType.SnapshotRequired;
            NetworkBinary.WriteInt32(destination, 1, required.CommandTick);
            NetworkBinary.WriteInt32(destination, 5, required.CurrentTick);
            NetworkBinary.WriteInt32(destination, 9, required.EarliestRestorableTick);
            return SnapshotRequiredSize;
        }

        public static bool TryReadSnapshotRequired(
            byte[] payload,
            int offset,
            int count,
            out NetworkSnapshotRequired required)
        {
            required = default;
            if (!HasTypeAndSize(payload, offset, count, SwarmNetworkMessageType.SnapshotRequired, SnapshotRequiredSize))
            {
                return false;
            }

            required = new NetworkSnapshotRequired(
                NetworkBinary.ReadInt32(payload, offset + 1),
                NetworkBinary.ReadInt32(payload, offset + 5),
                NetworkBinary.ReadInt32(payload, offset + 9));
            return true;
        }

        private static void WriteIdentity(
            byte[] destination,
            int offset,
            in NetworkCompatibilityIdentity identity)
        {
            NetworkBinary.WriteUInt16(destination, offset, identity.ProtocolVersion);
            NetworkBinary.WriteUInt64(destination, offset + 2, identity.LogicHash);
            NetworkBinary.WriteUInt64(destination, offset + 10, identity.ConfigHash);
            NetworkBinary.WriteInt32(destination, offset + 18, identity.ConfigSchemaVersion);
            NetworkBinary.WriteUInt16(destination, offset + 22, identity.ReplaySchemaVersion);
            NetworkBinary.WriteUInt16(destination, offset + 24, identity.SnapshotSchemaVersion);
            NetworkBinary.WriteInt32(destination, offset + 26, identity.AuthoritySchemaVersion);
            destination[offset + 30] = identity.FixedPointBits;
            NetworkBinary.WriteInt32(destination, offset + 31, identity.AgentCount);
            NetworkBinary.WriteUInt32(destination, offset + 35, identity.Seed);
        }

        private static bool TryReadIdentity(
            byte[] payload,
            int offset,
            out NetworkCompatibilityIdentity identity)
        {
            identity = new NetworkCompatibilityIdentity(
                NetworkBinary.ReadUInt16(payload, offset),
                NetworkBinary.ReadUInt64(payload, offset + 2),
                NetworkBinary.ReadUInt64(payload, offset + 10),
                NetworkBinary.ReadInt32(payload, offset + 18),
                NetworkBinary.ReadUInt16(payload, offset + 22),
                NetworkBinary.ReadUInt16(payload, offset + 24),
                NetworkBinary.ReadInt32(payload, offset + 26),
                payload[offset + 30],
                NetworkBinary.ReadInt32(payload, offset + 31),
                NetworkBinary.ReadUInt32(payload, offset + 35));
            return true;
        }

        private static bool HasTypeAndSize(
            byte[] payload,
            int offset,
            int count,
            SwarmNetworkMessageType expectedType,
            int expectedSize)
        {
            return payload != null && count == expectedSize && offset >= 0 &&
                offset <= payload.Length - count && payload[offset] == (byte)expectedType;
        }
    }
}

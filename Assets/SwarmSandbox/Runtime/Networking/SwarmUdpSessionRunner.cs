using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode;
using SwarmECS.Simulation.Netcode.Transport;
using SwarmECS.Simulation.Replay;
using SwarmECS.Simulation.Systems;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SwarmECS.Runtime.Networking
{
    internal enum SwarmUdpProcessRole : byte
    {
        Server = 0,
        Client = 1,
    }

    internal sealed class SwarmUdpSessionOptions
    {
        public const uint FormationSeed = 0x5EED1234u;
        public const uint FixedSessionId = 0x04000001u;

        public SwarmUdpProcessRole Role;
        public uint PeerId;
        public int Port = 47040;
        public string OutputDirectory;
        public int AgentCount = 512;
        public int FinalTick = 210;
        public int InputDelayTicks = 2;
        public int PredictionLeadTicks = 6;
        public int StartDelayMilliseconds = 500;
        public int BaseLatencyMilliseconds = 70;
        public int JitterMilliseconds = 30;
        public int LossPermille = 100;
        public int DuplicatePermille = 40;
        public int ReorderPermille = 120;

        public WeakNetworkSettings WeakSettings => new WeakNetworkSettings(
            BaseLatencyMilliseconds,
            JitterMilliseconds,
            LossPermille,
            DuplicatePermille,
            ReorderPermille);

        public static bool IsNetworkProcessRequested()
        {
            return ReadArgument("-swarmNetRole") != null;
        }

        public static bool TryParse(out SwarmUdpSessionOptions options, out string error)
        {
            options = new SwarmUdpSessionOptions();
            error = string.Empty;
            string role = ReadArgument("-swarmNetRole");
            if (string.Equals(role, "server", StringComparison.OrdinalIgnoreCase))
            {
                options.Role = SwarmUdpProcessRole.Server;
                options.PeerId = 0u;
            }
            else if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
            {
                options.Role = SwarmUdpProcessRole.Client;
                if (!TryReadUInt("-swarmNetPeerId", out uint peerId) || peerId < 1u || peerId > 2u)
                {
                    error = "Client requires -swarmNetPeerId 1 or 2.";
                    return false;
                }

                options.PeerId = peerId;
            }
            else
            {
                error = "-swarmNetRole must be server or client.";
                return false;
            }

            if (!TryApplyInt("-swarmNetPort", 1, ushort.MaxValue, ref options.Port, out error) ||
                !TryApplyInt("-swarmNetAgents", 32, 10_000, ref options.AgentCount, out error) ||
                !TryApplyInt("-swarmNetFinalTick", 90, 100_000, ref options.FinalTick, out error) ||
                !TryApplyInt("-swarmNetInputDelay", 0, 30, ref options.InputDelayTicks, out error) ||
                !TryApplyInt("-swarmNetPredictionLead", 0, 30, ref options.PredictionLeadTicks, out error) ||
                !TryApplyInt("-swarmNetStartDelayMs", 100, 5_000, ref options.StartDelayMilliseconds, out error) ||
                !TryApplyInt("-swarmNetLatencyMs", 0, 2_000, ref options.BaseLatencyMilliseconds, out error) ||
                !TryApplyInt("-swarmNetJitterMs", 0, 2_000, ref options.JitterMilliseconds, out error) ||
                !TryApplyInt("-swarmNetLossPermille", 0, 900, ref options.LossPermille, out error) ||
                !TryApplyInt("-swarmNetDuplicatePermille", 0, 900, ref options.DuplicatePermille, out error) ||
                !TryApplyInt("-swarmNetReorderPermille", 0, 900, ref options.ReorderPermille, out error))
            {
                return false;
            }

            options.OutputDirectory = ReadArgument("-swarmNetOutputDir");
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                options.OutputDirectory = Path.Combine(Application.persistentDataPath, "SwarmNetworkResults");
            }

            options.OutputDirectory = Path.GetFullPath(options.OutputDirectory);
            return true;
        }

        private static bool TryApplyInt(
            string key,
            int minimum,
            int maximum,
            ref int value,
            out string error)
        {
            error = string.Empty;
            string text = ReadArgument(key);
            if (text == null)
            {
                return true;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ||
                parsed < minimum || parsed > maximum)
            {
                error = $"{key} must be in [{minimum}, {maximum}].";
                return false;
            }

            value = parsed;
            return true;
        }

        private static bool TryReadUInt(string key, out uint value)
        {
            string text = ReadArgument(key);
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string ReadArgument(string key)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index + 1 < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], key, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Runs one real UDP process as either the authoritative server or a predictive
    /// client. All simulation access remains in Update on Unity's main thread.
    /// </summary>
    internal sealed class SwarmUdpSessionRunner : MonoBehaviour
    {
        private const int FixedRate = 30;
        private const int SessionStartupTimeoutMilliseconds = 15_000;
        private const int ReliableResendMilliseconds = 120;
        private const int RollbackHistoryTicks = 128;

        private readonly byte[] _incomingDatagram = new byte[SwarmUdpPacketCodec.MaxDatagramBytes];
        private readonly byte[] _payload = new byte[SwarmUdpPacketCodec.MaxPayloadBytes];
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly int[] _rollbackSamples = new int[64];

        private SwarmUdpSessionOptions _options;
        private SwarmUdpSocketWorker _socket;
        private DeterministicWeakNetwork _weakNetwork;
        private SwarmConfig _config;
        private NetworkCompatibilityIdentity _identity;
        private SwarmWorld _world;
        private SwarmSimulation _simulation;
        private RollbackController _rollback;
        private SwarmUdpSessionReport _report;
        private bool _terminating;

        // Server-only state.
        private readonly SwarmUdpPeerLink[] _serverLinks = new SwarmUdpPeerLink[3];
        private readonly OrderedCommandRequestBuffer[] _requestBuffers = new OrderedCommandRequestBuffer[3];
        private readonly bool[] _handshakes = new bool[3];
        private readonly bool[] _welcomesSent = new bool[3];
        private bool _sessionStartSent;
        private bool _serverClockScheduled;
        private long _serverStartAtMilliseconds;
        private int _nextAuthoritySequence;
        private bool _completionSent;
        private long _completionSentAtMilliseconds;
        private ulong _serverFinalHash;

        // Client-only state.
        private SwarmUdpPeerLink _serverLink;
        private ClientCommandReconciler _reconciler;
        private OrderedAuthorityCommandBuffer _authorityBuffer;
        private NetworkAuthorityHashHistory _hashHistory;
        private bool _welcomeReceived;
        private bool _sessionStartReceived;
        private long _clientStartAtMilliseconds;
        private readonly bool[] _requestsSent = new bool[2];
        private uint _nextRequestId = 1u;
        private bool _serverCompletionReceived;
        private NetworkSessionComplete _serverCompletion;
        private long _clientValidationAtMilliseconds = -1L;
        private int _rollbackSampleCount;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 240;
            _clock.Start();

            if (!SwarmUdpSessionOptions.TryParse(out _options, out string error))
            {
                Fail(error);
                return;
            }

            try
            {
                Directory.CreateDirectory(_options.OutputDirectory);
                _config = SwarmConfig.DemoDefault(_options.AgentCount);
                _identity = NetworkCompatibilityIdentity.Create(
                    _config,
                    _options.AgentCount,
                    SwarmUdpSessionOptions.FormationSeed);
                _world = new SwarmWorld(_config);
                _world.InitializeDeterministicFormation(
                    _options.AgentCount,
                    SwarmUdpSessionOptions.FormationSeed);
                _simulation = new SwarmSimulation(_world);
                _rollback = new RollbackController(
                    _world,
                    _simulation,
                    RollbackHistoryTicks,
                    1024);
                _weakNetwork = new DeterministicWeakNetwork(
                    _options.WeakSettings,
                    512,
                    0xA17E0000u ^ (_options.PeerId * 0x9E3779B9u));
                _socket = new SwarmUdpSocketWorker(
                    _options.Role == SwarmUdpProcessRole.Server ? _options.Port : 0,
                    512);
                _report = CreateBaseReport();

                if (_options.Role == SwarmUdpProcessRole.Server)
                {
                    InitializeServer();
                }
                else
                {
                    InitializeClient();
                }
            }
            catch (Exception exception)
            {
                Fail($"Initialization failed: {exception}");
            }
        }

        private void Update()
        {
            if (_terminating || _socket == null)
            {
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            long expectedSimulationMilliseconds =
                ((long)_options.FinalTick * 1000L + FixedRate - 1L) / FixedRate;
            if (now > SessionStartupTimeoutMilliseconds + expectedSimulationMilliseconds)
            {
                Fail("Session timed out before convergence.");
                return;
            }

            DrainIncoming(now);
            if (_terminating)
            {
                return;
            }

            PumpReliability(now);
            FlushWeakNetwork(now);

            if (_options.Role == SwarmUdpProcessRole.Server)
            {
                UpdateServer(now);
            }
            else
            {
                UpdateClient(now);
            }

            FlushWeakNetwork(now);
        }

        private void OnDestroy()
        {
            _socket?.Dispose();
            _socket = null;
            _simulation?.Dispose();
            _simulation = null;
        }

        private void InitializeServer()
        {
            for (uint peerId = 1u; peerId <= 2u; peerId++)
            {
                _serverLinks[peerId] = new SwarmUdpPeerLink(peerId);
                _requestBuffers[peerId] = new OrderedCommandRequestBuffer(32);
            }

            var ready = new SwarmUdpReadyReport
            {
                ready = true,
                port = _socket.LocalPort,
                processId = Process.GetCurrentProcess().Id,
                protocolVersion = SwarmUdpPacketCodec.ProtocolVersion,
            };
            WriteJson("server-ready.json", ready);
            Debug.Log($"[SwarmNet] authoritative server ready on 127.0.0.1:{_socket.LocalPort}");
        }

        private void InitializeClient()
        {
            _socket.SetPeerEndpoint(0u, "127.0.0.1", _options.Port);
            _serverLink = new SwarmUdpPeerLink(0u);
            _reconciler = new ClientCommandReconciler(_world, _rollback, 1024);
            _authorityBuffer = new OrderedAuthorityCommandBuffer(256);
            _hashHistory = new NetworkAuthorityHashHistory(_options.FinalTick + 64);
            _rollback.StepObserver = _hashHistory;
            SendHandshake(_clock.ElapsedMilliseconds);
            Debug.Log($"[SwarmNet] client {_options.PeerId} connecting to 127.0.0.1:{_options.Port}");
        }

        private void DrainIncoming(long nowMilliseconds)
        {
            int budget = 256;
            while (budget-- > 0 && _socket.ReceiveQueue.TryDequeue(
                       _incomingDatagram,
                       out uint remotePeerId,
                       out int count))
            {
                if (!SwarmUdpPacketCodec.TryRead(
                        _incomingDatagram,
                        count,
                        out SwarmPacketHeader header,
                        out int payloadOffset) ||
                    header.PeerId != remotePeerId)
                {
                    continue;
                }

                if (_options.Role == SwarmUdpProcessRole.Server)
                {
                    ProcessServerPacket(header, payloadOffset, remotePeerId, nowMilliseconds);
                }
                else
                {
                    ProcessClientPacket(header, payloadOffset, nowMilliseconds);
                }

                if (_terminating)
                {
                    return;
                }
            }
        }

        private void ProcessServerPacket(
            in SwarmPacketHeader header,
            int payloadOffset,
            uint remotePeerId,
            long nowMilliseconds)
        {
            if (remotePeerId < 1u || remotePeerId > 2u ||
                (header.SessionId != 0u && header.SessionId != SwarmUdpSessionOptions.FixedSessionId))
            {
                return;
            }

            SwarmUdpPeerLink link = _serverLinks[remotePeerId];
            bool accepted = link.AcceptHeader(header, nowMilliseconds);
            if (!accepted || header.IsAckOnly)
            {
                return;
            }

            if (!SwarmNetworkMessageCodec.TryReadMessageType(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out SwarmNetworkMessageType messageType))
            {
                SendReject(remotePeerId, SwarmNetworkRejectReason.MalformedPayload, nowMilliseconds);
                return;
            }

            switch (messageType)
            {
                case SwarmNetworkMessageType.Handshake:
                    HandleHandshake(remotePeerId, header, payloadOffset, nowMilliseconds);
                    break;
                case SwarmNetworkMessageType.CommandRequest:
                    HandleCommandRequest(remotePeerId, header, payloadOffset, nowMilliseconds);
                    break;
                case SwarmNetworkMessageType.SnapshotRequired:
                    if (SwarmNetworkMessageCodec.TryReadSnapshotRequired(
                            _incomingDatagram,
                            payloadOffset,
                            header.PayloadLength,
                            out NetworkSnapshotRequired required))
                    {
                        Fail($"Client {remotePeerId} requires a snapshot for command tick " +
                            $"{required.CommandTick}; v0.5 snapshot repair is not active.");
                    }
                    break;
            }
        }

        private void ProcessClientPacket(
            in SwarmPacketHeader header,
            int payloadOffset,
            long nowMilliseconds)
        {
            if (header.PeerId != 0u ||
                (_welcomeReceived && header.SessionId != SwarmUdpSessionOptions.FixedSessionId) ||
                (!_welcomeReceived && header.SessionId != 0u &&
                    header.SessionId != SwarmUdpSessionOptions.FixedSessionId))
            {
                return;
            }

            bool accepted = _serverLink.AcceptHeader(header, nowMilliseconds);
            if (!accepted || header.IsAckOnly)
            {
                return;
            }

            if (!SwarmNetworkMessageCodec.TryReadMessageType(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out SwarmNetworkMessageType messageType))
            {
                Fail("Server sent a malformed message envelope.");
                return;
            }

            switch (messageType)
            {
                case SwarmNetworkMessageType.Welcome:
                    HandleWelcome(header, payloadOffset);
                    break;
                case SwarmNetworkMessageType.Reject:
                    if (SwarmNetworkMessageCodec.TryReadReject(
                            _incomingDatagram,
                            payloadOffset,
                            header.PayloadLength,
                            out SwarmNetworkRejectReason reason))
                    {
                        Fail($"Server rejected the client: {reason}.");
                    }
                    break;
                case SwarmNetworkMessageType.SessionStart:
                    HandleSessionStart(header, payloadOffset, nowMilliseconds);
                    break;
                case SwarmNetworkMessageType.AuthoritativeCommand:
                    HandleAuthoritativeCommand(header, payloadOffset, nowMilliseconds);
                    break;
                case SwarmNetworkMessageType.HashTelemetry:
                    if (SwarmNetworkMessageCodec.TryReadHashTelemetry(
                            _incomingDatagram,
                            payloadOffset,
                            header.PayloadLength,
                            out NetworkHashTelemetry telemetry))
                    {
                        _hashHistory.RecordServer(telemetry.Tick, telemetry.AuthorityHash);
                    }
                    break;
                case SwarmNetworkMessageType.SessionComplete:
                    if (SwarmNetworkMessageCodec.TryReadSessionComplete(
                            _incomingDatagram,
                            payloadOffset,
                            header.PayloadLength,
                            out NetworkSessionComplete complete))
                    {
                        _serverCompletion = complete;
                        _serverCompletionReceived = true;
                    }
                    break;
            }
        }

        private void HandleHandshake(
            uint peerId,
            in SwarmPacketHeader header,
            int payloadOffset,
            long nowMilliseconds)
        {
            if (header.SessionId != 0u || !SwarmNetworkMessageCodec.TryReadHandshake(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out NetworkCompatibilityIdentity remoteIdentity))
            {
                SendReject(peerId, SwarmNetworkRejectReason.MalformedPayload, nowMilliseconds);
                return;
            }

            if (!_identity.IsCompatibleWith(remoteIdentity))
            {
                SendReject(peerId, SwarmNetworkRejectReason.IdentityMismatch, nowMilliseconds);
                return;
            }

            _handshakes[peerId] = true;
            if (_welcomesSent[peerId])
            {
                return;
            }

            var welcome = new NetworkWelcome(
                SwarmUdpSessionOptions.FixedSessionId,
                peerId,
                _identity,
                _options.InputDelayTicks,
                _options.PredictionLeadTicks,
                _options.FinalTick);
            int payloadCount = SwarmNetworkMessageCodec.WriteWelcome(_payload, welcome);
            if (!_serverLinks[peerId].TrySendPayload(
                    SwarmUdpSessionOptions.FixedSessionId,
                    0u,
                    _world.Tick,
                    SwarmNetworkChannel.Control,
                    true,
                    _payload,
                    payloadCount,
                    _weakNetwork,
                    nowMilliseconds))
            {
                Fail($"Reliable welcome window is full for client {peerId}.");
                return;
            }

            _welcomesSent[peerId] = true;
        }

        private void HandleWelcome(in SwarmPacketHeader header, int payloadOffset)
        {
            if (!SwarmNetworkMessageCodec.TryReadWelcome(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out NetworkWelcome welcome) ||
                welcome.SessionId != SwarmUdpSessionOptions.FixedSessionId ||
                welcome.AssignedPeerId != _options.PeerId ||
                !_identity.IsCompatibleWith(welcome.Identity) ||
                welcome.FinalTick != _options.FinalTick ||
                welcome.InputDelayTicks != _options.InputDelayTicks ||
                welcome.PredictionLeadTicks != _options.PredictionLeadTicks)
            {
                Fail("Welcome identity or session parameters do not match the local process.");
                return;
            }

            _welcomeReceived = true;
        }

        private void HandleSessionStart(
            in SwarmPacketHeader header,
            int payloadOffset,
            long nowMilliseconds)
        {
            if (!_welcomeReceived || !SwarmNetworkMessageCodec.TryReadSessionStart(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out NetworkSessionStart start) ||
                start.ServerTick != 0 || start.FinalTick != _options.FinalTick)
            {
                Fail("SessionStart arrived before a valid Welcome or has invalid parameters.");
                return;
            }

            if (!_sessionStartReceived)
            {
                _sessionStartReceived = true;
                _clientStartAtMilliseconds = nowMilliseconds + start.StartDelayMilliseconds;
                _reconciler.BeginPrediction();
            }
        }

        private void HandleCommandRequest(
            uint peerId,
            in SwarmPacketHeader header,
            int payloadOffset,
            long nowMilliseconds)
        {
            if (!_serverClockScheduled || header.SessionId != SwarmUdpSessionOptions.FixedSessionId ||
                !SwarmNetworkMessageCodec.TryReadCommandRequest(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out NetworkCommandRequest request) ||
                !_requestBuffers[peerId].TryInsert(request))
            {
                return;
            }

            while (_requestBuffers[peerId].TryTakeNext(out NetworkCommandRequest ordered))
            {
                int commandTick = _world.Tick + _options.InputDelayTicks;
                var command = new SimulationCommand(
                    commandTick,
                    _nextAuthoritySequence++,
                    SimulationCommandType.SetGroupTarget,
                    ordered.Group,
                    ordered.Target);
                if (!_rollback.QueueCommand(command))
                {
                    Fail($"Authority could not queue command {command.Sequence} at tick {command.Tick}.");
                    return;
                }

                var authority = new NetworkAuthoritativeCommand(peerId, ordered.RequestId, command);
                int payloadCount = SwarmNetworkMessageCodec.WriteAuthoritativeCommand(_payload, authority);
                for (uint destination = 1u; destination <= 2u; destination++)
                {
                    if (!_serverLinks[destination].TrySendPayload(
                            SwarmUdpSessionOptions.FixedSessionId,
                            0u,
                            _world.Tick,
                            SwarmNetworkChannel.ReliableCommand,
                            true,
                            _payload,
                            payloadCount,
                            _weakNetwork,
                            nowMilliseconds))
                    {
                        Fail($"Reliable authority window is full for client {destination}.");
                        return;
                    }
                }
            }
        }

        private void HandleAuthoritativeCommand(
            in SwarmPacketHeader header,
            int payloadOffset,
            long nowMilliseconds)
        {
            if (!SwarmNetworkMessageCodec.TryReadAuthoritativeCommand(
                    _incomingDatagram,
                    payloadOffset,
                    header.PayloadLength,
                    out NetworkAuthoritativeCommand authority) ||
                !_authorityBuffer.TryInsert(authority))
            {
                Fail("Authority command is malformed or outside the ordered receive window.");
                return;
            }

            while (_authorityBuffer.TryTakeNext(out NetworkAuthoritativeCommand ordered))
            {
                NetworkCommandApplyResult result = _reconciler.Apply(ordered);
                if (result == NetworkCommandApplyResult.RolledBack &&
                    _rollbackSampleCount < _rollbackSamples.Length)
                {
                    _rollbackSamples[_rollbackSampleCount++] = _rollback.LastResimulatedTicks;
                }

                if (result == NetworkCommandApplyResult.SnapshotRequired)
                {
                    var required = new NetworkSnapshotRequired(
                        ordered.Command.Tick,
                        _world.Tick,
                        _rollback.EarliestRestorableTick);
                    int payloadCount = SwarmNetworkMessageCodec.WriteSnapshotRequired(_payload, required);
                    _serverLink.TrySendPayload(
                        SwarmUdpSessionOptions.FixedSessionId,
                        _options.PeerId,
                        _world.Tick,
                        SwarmNetworkChannel.Control,
                        true,
                        _payload,
                        payloadCount,
                        _weakNetwork,
                        nowMilliseconds);
                    Fail($"Command tick {ordered.Command.Tick} is outside the rollback window; SnapshotRequired.");
                    return;
                }

                if (result == NetworkCommandApplyResult.Invalid ||
                    result == NetworkCommandApplyResult.Failed)
                {
                    Fail($"Failed to reconcile authority command {ordered.Command.Sequence}: {result}.");
                    return;
                }
            }
        }

        private void UpdateServer(long nowMilliseconds)
        {
            if (!_sessionStartSent && BothClientsWelcomedAndAcknowledged())
            {
                var start = new NetworkSessionStart(0, _options.FinalTick, _options.StartDelayMilliseconds);
                int payloadCount = SwarmNetworkMessageCodec.WriteSessionStart(_payload, start);
                for (uint peerId = 1u; peerId <= 2u; peerId++)
                {
                    if (!_serverLinks[peerId].TrySendPayload(
                            SwarmUdpSessionOptions.FixedSessionId,
                            0u,
                            0,
                            SwarmNetworkChannel.Control,
                            true,
                            _payload,
                            payloadCount,
                            _weakNetwork,
                            nowMilliseconds))
                    {
                        Fail($"Could not send SessionStart to client {peerId}.");
                        return;
                    }
                }

                _sessionStartSent = true;
            }

            if (_sessionStartSent && !_serverClockScheduled &&
                _serverLinks[1].PendingReliableCount == 0 &&
                _serverLinks[2].PendingReliableCount == 0)
            {
                _serverClockScheduled = true;
                _serverStartAtMilliseconds = nowMilliseconds + _options.StartDelayMilliseconds;
            }

            if (!_serverClockScheduled || nowMilliseconds < _serverStartAtMilliseconds || _completionSent)
            {
                TryFinalizeServer(nowMilliseconds);
                return;
            }

            int targetTick = (int)(((nowMilliseconds - _serverStartAtMilliseconds) * FixedRate) / 1000L) + 1;
            if (targetTick > _options.FinalTick)
            {
                targetTick = _options.FinalTick;
            }

            int stepBudget = 8;
            while (_world.Tick < targetTick && stepBudget-- > 0)
            {
                _rollback.Step();
                SendHashTelemetry(nowMilliseconds);
            }

            if (_world.Tick >= _options.FinalTick)
            {
                _serverFinalHash = _world.ComputeStateHash();
                var complete = new NetworkSessionComplete(
                    _options.FinalTick,
                    _serverFinalHash,
                    _nextAuthoritySequence);
                int payloadCount = SwarmNetworkMessageCodec.WriteSessionComplete(_payload, complete);
                for (uint peerId = 1u; peerId <= 2u; peerId++)
                {
                    if (!_serverLinks[peerId].TrySendPayload(
                            SwarmUdpSessionOptions.FixedSessionId,
                            0u,
                            _world.Tick,
                            SwarmNetworkChannel.Control,
                            true,
                            _payload,
                            payloadCount,
                            _weakNetwork,
                            nowMilliseconds))
                    {
                        Fail($"Could not send SessionComplete to client {peerId}.");
                        return;
                    }
                }

                _completionSent = true;
                _completionSentAtMilliseconds = nowMilliseconds;
            }
        }

        private void UpdateClient(long nowMilliseconds)
        {
            if (!_sessionStartReceived || nowMilliseconds < _clientStartAtMilliseconds)
            {
                return;
            }

            int targetTick = (int)(((nowMilliseconds - _clientStartAtMilliseconds) * FixedRate) / 1000L) +
                _options.PredictionLeadTicks;
            if (targetTick > _options.FinalTick)
            {
                targetTick = _options.FinalTick;
            }

            int stepBudget = 12;
            while (_world.Tick < targetTick && stepBudget-- > 0)
            {
                _rollback.Step();
            }

            SendScheduledClientRequests(nowMilliseconds);
            if (!_serverCompletionReceived || _world.Tick < _serverCompletion.FinalTick ||
                _reconciler.ReceivedAuthorityCommands < _serverCompletion.TotalCommands ||
                _authorityBuffer.NextSequence < _serverCompletion.TotalCommands)
            {
                return;
            }

            if (_clientValidationAtMilliseconds < 0L)
            {
                if (!ValidateClientConvergence(out string error))
                {
                    Fail(error);
                    return;
                }

                _clientValidationAtMilliseconds = nowMilliseconds;
                _reconciler.MarkComplete();
            }

            // Remain alive long enough to deliver completion ACKs and any request ACKs.
            if (nowMilliseconds - _clientValidationAtMilliseconds >= 750L &&
                _serverLink.PendingReliableCount == 0 && _weakNetwork.PendingCount == 0)
            {
                CompleteClient();
            }
        }

        private void SendScheduledClientRequests(long nowMilliseconds)
        {
            int firstTick = _options.PeerId == 1u ? 30 : 60;
            int secondTick = _options.PeerId == 1u ? 120 : 150;
            if (!_requestsSent[0] && _world.Tick >= firstTick)
            {
                SendCommandRequest(
                    (byte)(_options.PeerId == 1u ? 0 : 1),
                    _options.PeerId == 1u
                        ? new FPVector2(FP.FromInt(28), FP.FromInt(18))
                        : new FPVector2(FP.FromInt(-28), FP.FromInt(18)),
                    nowMilliseconds);
                _requestsSent[0] = true;
            }

            if (!_requestsSent[1] && _world.Tick >= secondTick)
            {
                SendCommandRequest(
                    (byte)(_options.PeerId == 1u ? 2 : 3),
                    _options.PeerId == 1u
                        ? new FPVector2(FP.FromInt(-24), FP.FromInt(-22))
                        : new FPVector2(FP.FromInt(24), FP.FromInt(-22)),
                    nowMilliseconds);
                _requestsSent[1] = true;
            }
        }

        private void SendCommandRequest(byte group, FPVector2 target, long nowMilliseconds)
        {
            var request = new NetworkCommandRequest(_nextRequestId++, _world.Tick, group, target);
            int payloadCount = SwarmNetworkMessageCodec.WriteCommandRequest(_payload, request);
            if (!_serverLink.TrySendPayload(
                    SwarmUdpSessionOptions.FixedSessionId,
                    _options.PeerId,
                    _world.Tick,
                    SwarmNetworkChannel.ReliableCommand,
                    true,
                    _payload,
                    payloadCount,
                    _weakNetwork,
                    nowMilliseconds))
            {
                Fail("Client reliable request window is full.");
            }
        }

        private void SendHandshake(long nowMilliseconds)
        {
            int payloadCount = SwarmNetworkMessageCodec.WriteHandshake(_payload, _identity);
            if (!_serverLink.TrySendPayload(
                    0u,
                    _options.PeerId,
                    0,
                    SwarmNetworkChannel.Control,
                    true,
                    _payload,
                    payloadCount,
                    _weakNetwork,
                    nowMilliseconds))
            {
                Fail("Could not queue the reliable handshake.");
            }
        }

        private void SendHashTelemetry(long nowMilliseconds)
        {
            var telemetry = new NetworkHashTelemetry(
                _world.Tick,
                _world.ComputeStateHash(),
                _nextAuthoritySequence - 1);
            int payloadCount = SwarmNetworkMessageCodec.WriteHashTelemetry(_payload, telemetry);
            for (uint peerId = 1u; peerId <= 2u; peerId++)
            {
                _serverLinks[peerId].TrySendPayload(
                    SwarmUdpSessionOptions.FixedSessionId,
                    0u,
                    _world.Tick,
                    SwarmNetworkChannel.HashTelemetry,
                    false,
                    _payload,
                    payloadCount,
                    _weakNetwork,
                    nowMilliseconds);
            }
        }

        private void SendReject(
            uint peerId,
            SwarmNetworkRejectReason reason,
            long nowMilliseconds)
        {
            int payloadCount = SwarmNetworkMessageCodec.WriteReject(_payload, reason);
            _serverLinks[peerId].TrySendPayload(
                0u,
                0u,
                _world.Tick,
                SwarmNetworkChannel.Control,
                true,
                _payload,
                payloadCount,
                _weakNetwork,
                nowMilliseconds);
        }

        private bool BothClientsWelcomedAndAcknowledged()
        {
            return _handshakes[1] && _handshakes[2] &&
                _welcomesSent[1] && _welcomesSent[2] &&
                _serverLinks[1].PendingReliableCount == 0 &&
                _serverLinks[2].PendingReliableCount == 0;
        }

        private void PumpReliability(long nowMilliseconds)
        {
            if (_options.Role == SwarmUdpProcessRole.Server)
            {
                for (uint peerId = 1u; peerId <= 2u; peerId++)
                {
                    SwarmUdpPeerLink link = _serverLinks[peerId];
                    link.PumpResends(_weakNetwork, nowMilliseconds, ReliableResendMilliseconds, 8);
                    link.TrySendPendingAcknowledgement(
                        SwarmUdpSessionOptions.FixedSessionId,
                        0u,
                        _world.Tick,
                        _weakNetwork,
                        nowMilliseconds);
                }
            }
            else
            {
                _serverLink.PumpResends(_weakNetwork, nowMilliseconds, ReliableResendMilliseconds, 8);
                _serverLink.TrySendPendingAcknowledgement(
                    _welcomeReceived ? SwarmUdpSessionOptions.FixedSessionId : 0u,
                    _options.PeerId,
                    _world.Tick,
                    _weakNetwork,
                    nowMilliseconds);
            }
        }

        private void FlushWeakNetwork(long nowMilliseconds)
        {
            int budget = 256;
            while (budget-- > 0 && _weakNetwork.TryDequeueDue(
                       nowMilliseconds,
                       out uint destinationPeerId,
                       out byte[] bytes,
                       out int count))
            {
                _socket.TrySend(destinationPeerId, bytes, count);
            }
        }

        private void TryFinalizeServer(long nowMilliseconds)
        {
            if (!_completionSent ||
                _serverLinks[1].PendingReliableCount != 0 ||
                _serverLinks[2].PendingReliableCount != 0 ||
                _weakNetwork.PendingCount != 0 ||
                nowMilliseconds - _completionSentAtMilliseconds < 500L)
            {
                return;
            }

            _report.success = true;
            _report.sessionId = SwarmUdpSessionOptions.FixedSessionId;
            _report.finalTick = _world.Tick;
            _report.confirmedTick = _world.Tick;
            _report.predictedTick = _world.Tick;
            _report.finalStateHash = HashText(_serverFinalHash);
            _report.authorityFinalStateHash = HashText(_serverFinalHash);
            _report.authorityCommands = _nextAuthoritySequence;
            Complete("server.json", 0);
        }

        private bool ValidateClientConvergence(out string error)
        {
            error = string.Empty;
            ulong localHash = _world.ComputeStateHash();
            int mismatches = _hashHistory.CountUnresolvedMismatches();
            int missing = _hashHistory.CountMissingLocalSamples();
            if (_serverCompletion.FinalTick != _options.FinalTick)
            {
                error = "Server completed at an unexpected tick.";
                return false;
            }

            if (localHash != _serverCompletion.FinalAuthorityHash)
            {
                error = $"Final state hash mismatch: local={HashText(localHash)}, " +
                    $"authority={HashText(_serverCompletion.FinalAuthorityHash)}.";
                return false;
            }

            if (mismatches != 0 || missing != 0)
            {
                error = $"Hash matrix is unresolved: mismatches={mismatches}, missing={missing}.";
                return false;
            }

            if (_reconciler.State == ClientSynchronizationState.SnapshotRequired ||
                _reconciler.ReceivedAuthorityCommands != _serverCompletion.TotalCommands)
            {
                error = "Authority command stream did not reconcile completely.";
                return false;
            }

            if (_reconciler.LateAuthorityCommands == 0)
            {
                error = "Weak-network run produced no late authoritative command rollback.";
                return false;
            }

            return true;
        }

        private void CompleteClient()
        {
            ulong localHash = _world.ComputeStateHash();
            _report.success = true;
            _report.sessionId = SwarmUdpSessionOptions.FixedSessionId;
            _report.finalTick = _world.Tick;
            _report.confirmedTick = _hashHistory.LastConfirmedTick;
            _report.predictedTick = _world.Tick;
            _report.finalStateHash = HashText(localHash);
            _report.authorityFinalStateHash = HashText(_serverCompletion.FinalAuthorityHash);
            _report.authorityCommands = _serverCompletion.TotalCommands;
            _report.receivedAuthorityCommands = _reconciler.ReceivedAuthorityCommands;
            _report.lateAuthorityCommands = _reconciler.LateAuthorityCommands;
            _report.rollbackCount = _rollback.RollbackCount;
            _report.rollbackMaximumTicks = _reconciler.MaximumRollbackDepth;
            _report.rollbackP50Ticks = Percentile(_rollbackSamples, _rollbackSampleCount, 50);
            _report.rollbackP95Ticks = Percentile(_rollbackSamples, _rollbackSampleCount, 95);
            _report.rollbackP99Ticks = Percentile(_rollbackSamples, _rollbackSampleCount, 99);
            _report.serverHashSamples = _hashHistory.ServerSampleCount;
            _report.confirmedHashSamples = _hashHistory.ConfirmedSampleCount;
            _report.unresolvedHashMismatches = _hashHistory.CountUnresolvedMismatches();
            _report.missingLocalHashSamples = _hashHistory.CountMissingLocalSamples();
            Complete($"client-{_options.PeerId}.json", 0);
        }

        private SwarmUdpSessionReport CreateBaseReport()
        {
            return new SwarmUdpSessionReport
            {
                role = _options.Role == SwarmUdpProcessRole.Server ? "server" : "client",
                peerId = _options.PeerId,
                processId = Process.GetCurrentProcess().Id,
                sessionId = SwarmUdpSessionOptions.FixedSessionId,
                localPort = _socket.LocalPort,
                inputDelayTicks = _options.InputDelayTicks,
                predictionLeadTicks = _options.PredictionLeadTicks,
                agentCount = _options.AgentCount,
                seed = SwarmUdpSessionOptions.FormationSeed,
                logicHash = HashText(SimulationBuildIdentity.CurrentLogicHash),
                configHash = HashText(_config.ConfigHash),
            };
        }

        private void Complete(string fileName, int exitCode)
        {
            if (_terminating)
            {
                return;
            }

            _terminating = true;
            PopulateTransportReport();
            WriteJson(fileName, _report);
            Debug.Log($"[SwarmNet] {_report.role} peer {_report.peerId} complete: " +
                $"success={_report.success}, hash={_report.finalStateHash}");
            Application.Quit(exitCode);
        }

        private void Fail(string failure)
        {
            if (_terminating)
            {
                return;
            }

            _terminating = true;
            Debug.LogError($"[SwarmNet] {failure}");
            if (_report == null && _options != null)
            {
                _report = new SwarmUdpSessionReport
                {
                    role = _options.Role == SwarmUdpProcessRole.Server ? "server" : "client",
                    peerId = _options.PeerId,
                    processId = Process.GetCurrentProcess().Id,
                    failure = failure,
                };
            }

            if (_report != null)
            {
                _report.success = false;
                _report.failure = failure;
                if (_world != null)
                {
                    _report.finalTick = _world.Tick;
                    _report.finalStateHash = HashText(_world.ComputeStateHash());
                }

                PopulateTransportReport();
                string fileName = _options.Role == SwarmUdpProcessRole.Server
                    ? "server.json"
                    : $"client-{_options.PeerId}.json";
                WriteJson(fileName, _report);
            }

            Application.Quit(2);
        }

        private void PopulateTransportReport()
        {
            if (_report == null)
            {
                return;
            }

            _report.elapsedMilliseconds = (int)_clock.ElapsedMilliseconds;
            if (_socket != null)
            {
                _report.localPort = _socket.LocalPort;
                _report.receivedDatagrams = _socket.ReceivedDatagrams;
                _report.sentDatagrams = _socket.SentDatagrams;
                _report.sentBytes = _socket.SentBytes;
                _report.rejectedDatagrams = _socket.RejectedDatagrams;
                _report.socketErrors = _socket.SocketErrors;
                _report.inboundQueueDrops = _socket.ReceiveQueue.DroppedWhenFull;
            }

            if (_weakNetwork != null)
            {
                _report.weakScheduled = _weakNetwork.ScheduledCount;
                _report.weakDelivered = _weakNetwork.DeliveredCount;
                _report.weakLossDrops = _weakNetwork.DroppedByLoss;
                _report.weakCapacityDrops = _weakNetwork.DroppedByCapacity;
                _report.weakDuplicates = _weakNetwork.DuplicatedCount;
                _report.weakReorders = _weakNetwork.ReorderedCount;
            }

            if (_options == null)
            {
                return;
            }

            if (_options.Role == SwarmUdpProcessRole.Server)
            {
                int retransmissions = 0;
                int pending = 0;
                long rttTotal = 0;
                long rttMaximum = 0;
                for (int peerId = 1; peerId <= 2; peerId++)
                {
                    SwarmUdpPeerLink link = _serverLinks[peerId];
                    if (link == null)
                    {
                        continue;
                    }

                    retransmissions += link.RetransmissionCount;
                    pending += link.PendingReliableCount;
                    rttTotal += link.AverageRoundTripMilliseconds;
                    if (link.MaximumRoundTripMilliseconds > rttMaximum)
                    {
                        rttMaximum = link.MaximumRoundTripMilliseconds;
                    }
                }

                _report.reliableRetransmissions = retransmissions;
                _report.pendingReliablePackets = pending;
                _report.averageRttMilliseconds = rttTotal / 2L;
                _report.maximumRttMilliseconds = rttMaximum;
            }
            else if (_serverLink != null)
            {
                _report.reliableRetransmissions = _serverLink.RetransmissionCount;
                _report.pendingReliablePackets = _serverLink.PendingReliableCount;
                _report.averageRttMilliseconds = _serverLink.AverageRoundTripMilliseconds;
                _report.maximumRttMilliseconds = _serverLink.MaximumRoundTripMilliseconds;
            }
        }

        private void WriteJson(string fileName, object value)
        {
            if (_options == null || string.IsNullOrWhiteSpace(_options.OutputDirectory))
            {
                return;
            }

            Directory.CreateDirectory(_options.OutputDirectory);
            string path = Path.Combine(_options.OutputDirectory, fileName);
            File.WriteAllText(path, JsonUtility.ToJson(value, true) + Environment.NewLine);
        }

        private static string HashText(ulong hash)
        {
            return $"0x{hash:X16}";
        }

        private static int Percentile(int[] samples, int count, int percentile)
        {
            if (count <= 0)
            {
                return 0;
            }

            int[] copy = new int[count];
            Array.Copy(samples, copy, count);
            Array.Sort(copy);
            int index = ((count - 1) * percentile + 99) / 100;
            return copy[index];
        }

        [Serializable]
        private sealed class SwarmUdpReadyReport
        {
            public bool ready;
            public int port;
            public int processId;
            public int protocolVersion;
        }
    }

    public static class SwarmUdpSessionBootstrap
    {
        public static bool IsNetworkProcessRequested =>
            SwarmUdpSessionOptions.IsNetworkProcessRequested();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void StartNetworkProcess()
        {
            if (!IsNetworkProcessRequested)
            {
                return;
            }

            var runner = new GameObject("Swarm UDP Session Runner");
            runner.AddComponent<SwarmUdpSessionRunner>();
        }
    }
}

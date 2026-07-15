using System;

namespace SwarmECS.Simulation.Netcode.Transport
{
    public interface IRollbackStepObserver
    {
        void OnWorldAdvanced(SwarmWorld world);
    }

    public enum ClientSynchronizationState : byte
    {
        Connecting = 0,
        Predicting = 1,
        SnapshotRequired = 2,
        Complete = 3,
        Failed = 4,
    }

    public enum NetworkCommandApplyResult : byte
    {
        Invalid = 0,
        Duplicate = 1,
        Queued = 2,
        RolledBack = 3,
        SnapshotRequired = 4,
        Failed = 5,
    }

    /// <summary>
    /// Applies server-stamped commands only on the simulation thread. Packet order
    /// and duplicates are decoupled from command order through the authoritative
    /// command sequence and the existing rollback timeline.
    /// </summary>
    public sealed class ClientCommandReconciler
    {
        private readonly SwarmWorld _world;
        private readonly RollbackController _rollback;
        private readonly int[] _sequenceTags;

        public ClientCommandReconciler(
            SwarmWorld world,
            RollbackController rollback,
            int trackedSequenceCapacity = 512)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
            if (trackedSequenceCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(trackedSequenceCapacity));
            }

            _sequenceTags = new int[trackedSequenceCapacity];
            for (int index = 0; index < _sequenceTags.Length; index++)
            {
                _sequenceTags[index] = -1;
            }

            State = ClientSynchronizationState.Connecting;
        }

        public ClientSynchronizationState State { get; private set; }

        public int ReceivedAuthorityCommands { get; private set; }

        public int LateAuthorityCommands { get; private set; }

        public int MaximumRollbackDepth { get; private set; }

        public int SnapshotRequiredCommandTick { get; private set; } = -1;

        public void BeginPrediction()
        {
            if (State == ClientSynchronizationState.Connecting)
            {
                State = ClientSynchronizationState.Predicting;
            }
        }

        public void MarkComplete()
        {
            if (State == ClientSynchronizationState.Predicting)
            {
                State = ClientSynchronizationState.Complete;
            }
        }

        public NetworkCommandApplyResult Apply(in NetworkAuthoritativeCommand authority)
        {
            SimulationCommand command = authority.Command;
            if (State == ClientSynchronizationState.SnapshotRequired ||
                State == ClientSynchronizationState.Failed ||
                command.Tick < 0 || command.Sequence < 0)
            {
                return NetworkCommandApplyResult.Invalid;
            }

            int slot = command.Sequence % _sequenceTags.Length;
            if (_sequenceTags[slot] == command.Sequence)
            {
                return NetworkCommandApplyResult.Duplicate;
            }

            if (command.Tick < _world.Tick)
            {
                int depth = _world.Tick - command.Tick;
                if (!_rollback.CanRestoreTick(command.Tick))
                {
                    State = ClientSynchronizationState.SnapshotRequired;
                    SnapshotRequiredCommandTick = command.Tick;
                    return NetworkCommandApplyResult.SnapshotRequired;
                }

                if (!_rollback.InjectLateCommand(command))
                {
                    State = ClientSynchronizationState.Failed;
                    return NetworkCommandApplyResult.Failed;
                }

                _sequenceTags[slot] = command.Sequence;
                ReceivedAuthorityCommands++;
                LateAuthorityCommands++;
                if (depth > MaximumRollbackDepth)
                {
                    MaximumRollbackDepth = depth;
                }

                return NetworkCommandApplyResult.RolledBack;
            }

            if (!_rollback.QueueCommand(command))
            {
                State = ClientSynchronizationState.Failed;
                return NetworkCommandApplyResult.Failed;
            }

            _sequenceTags[slot] = command.Sequence;
            ReceivedAuthorityCommands++;
            return NetworkCommandApplyResult.Queued;
        }
    }

    /// <summary>
    /// Fixed tick-indexed hash matrix. Server samples may arrive before the command
    /// that produced them; replay observer writes replace speculative hashes in place.
    /// </summary>
    public sealed class NetworkAuthorityHashHistory : IRollbackStepObserver
    {
        private readonly int[] _localTicks;
        private readonly ulong[] _localHashes;
        private readonly int[] _serverTicks;
        private readonly ulong[] _serverHashes;
        private readonly int[] _confirmedTicks;

        public NetworkAuthorityHashHistory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _localTicks = new int[capacity];
            _localHashes = new ulong[capacity];
            _serverTicks = new int[capacity];
            _serverHashes = new ulong[capacity];
            _confirmedTicks = new int[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _localTicks[index] = int.MinValue;
                _serverTicks[index] = int.MinValue;
                _confirmedTicks[index] = int.MinValue;
            }

            LastConfirmedTick = -1;
        }

        public int Capacity => _localTicks.Length;

        public int ServerSampleCount { get; private set; }

        public int ConfirmedSampleCount { get; private set; }

        public int LastConfirmedTick { get; private set; }

        public void OnWorldAdvanced(SwarmWorld world)
        {
            if (world == null)
            {
                return;
            }

            RecordLocal(world.Tick, world.ComputeStateHash());
        }

        public void RecordLocal(int tick, ulong hash)
        {
            int slot = PositiveModulo(tick, _localTicks.Length);
            if (_localTicks[slot] != int.MinValue && _localTicks[slot] != tick &&
                _confirmedTicks[slot] == _localTicks[slot])
            {
                ConfirmedSampleCount--;
                _confirmedTicks[slot] = int.MinValue;
            }

            _localTicks[slot] = tick;
            _localHashes[slot] = hash;
            Evaluate(slot, tick);
        }

        public void RecordServer(int tick, ulong hash)
        {
            int slot = PositiveModulo(tick, _serverTicks.Length);
            if (_serverTicks[slot] != tick)
            {
                if (_serverTicks[slot] != int.MinValue)
                {
                    ServerSampleCount--;
                }

                if (_serverTicks[slot] != int.MinValue &&
                    _confirmedTicks[slot] == _serverTicks[slot])
                {
                    ConfirmedSampleCount--;
                    _confirmedTicks[slot] = int.MinValue;
                }

                _serverTicks[slot] = tick;
                ServerSampleCount++;
            }

            _serverHashes[slot] = hash;
            Evaluate(slot, tick);
        }

        public bool TryGetLocal(int tick, out ulong hash)
        {
            int slot = PositiveModulo(tick, _localTicks.Length);
            if (_localTicks[slot] == tick)
            {
                hash = _localHashes[slot];
                return true;
            }

            hash = 0UL;
            return false;
        }

        public bool TryGetServer(int tick, out ulong hash)
        {
            int slot = PositiveModulo(tick, _serverTicks.Length);
            if (_serverTicks[slot] == tick)
            {
                hash = _serverHashes[slot];
                return true;
            }

            hash = 0UL;
            return false;
        }

        public int CountUnresolvedMismatches()
        {
            int mismatches = 0;
            for (int slot = 0; slot < _serverTicks.Length; slot++)
            {
                int tick = _serverTicks[slot];
                if (tick != int.MinValue && _localTicks[slot] == tick &&
                    _localHashes[slot] != _serverHashes[slot])
                {
                    mismatches++;
                }
            }

            return mismatches;
        }

        public int CountMissingLocalSamples()
        {
            int missing = 0;
            for (int slot = 0; slot < _serverTicks.Length; slot++)
            {
                int tick = _serverTicks[slot];
                if (tick != int.MinValue && _localTicks[slot] != tick)
                {
                    missing++;
                }
            }

            return missing;
        }

        private void Evaluate(int slot, int tick)
        {
            bool matches = _localTicks[slot] == tick && _serverTicks[slot] == tick &&
                _localHashes[slot] == _serverHashes[slot];
            if (matches)
            {
                if (_confirmedTicks[slot] != tick)
                {
                    _confirmedTicks[slot] = tick;
                    ConfirmedSampleCount++;
                }

                if (tick > LastConfirmedTick)
                {
                    LastConfirmedTick = tick;
                }
            }
            else if (_confirmedTicks[slot] == tick)
            {
                _confirmedTicks[slot] = int.MinValue;
                ConfirmedSampleCount--;
            }
        }

        private static int PositiveModulo(int value, int modulus)
        {
            int remainder = value % modulus;
            return remainder < 0 ? remainder + modulus : remainder;
        }
    }

    /// <summary>Fixed window that exposes server commands strictly by authority sequence.</summary>
    public sealed class OrderedAuthorityCommandBuffer
    {
        private readonly int[] _sequenceTags;
        private readonly NetworkAuthoritativeCommand[] _commands;

        public OrderedAuthorityCommandBuffer(int capacity, int firstSequence = 0)
        {
            if (capacity <= 0 || firstSequence < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            _sequenceTags = new int[capacity];
            _commands = new NetworkAuthoritativeCommand[capacity];
            for (int index = 0; index < capacity; index++)
            {
                _sequenceTags[index] = -1;
            }

            NextSequence = firstSequence;
        }

        public int NextSequence { get; private set; }

        public int BufferedCount { get; private set; }

        public bool TryInsert(in NetworkAuthoritativeCommand authority)
        {
            int sequence = authority.Command.Sequence;
            if (sequence < NextSequence || sequence < 0 || sequence - NextSequence >= _sequenceTags.Length)
            {
                return sequence < NextSequence;
            }

            int slot = sequence % _sequenceTags.Length;
            if (_sequenceTags[slot] == sequence)
            {
                return true;
            }

            if (_sequenceTags[slot] >= NextSequence)
            {
                return false;
            }

            _sequenceTags[slot] = sequence;
            _commands[slot] = authority;
            BufferedCount++;
            return true;
        }

        public bool TryTakeNext(out NetworkAuthoritativeCommand authority)
        {
            int slot = NextSequence % _sequenceTags.Length;
            if (_sequenceTags[slot] != NextSequence)
            {
                authority = default;
                return false;
            }

            authority = _commands[slot];
            _sequenceTags[slot] = -1;
            NextSequence++;
            BufferedCount--;
            return true;
        }
    }

    /// <summary>Per-client reliable request ordering using uint serial arithmetic.</summary>
    public sealed class OrderedCommandRequestBuffer
    {
        private readonly uint[] _requestTags;
        private readonly NetworkCommandRequest[] _requests;
        private readonly bool[] _occupied;

        public OrderedCommandRequestBuffer(int capacity, uint firstRequestId = 1u)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _requestTags = new uint[capacity];
            _requests = new NetworkCommandRequest[capacity];
            _occupied = new bool[capacity];
            NextRequestId = firstRequestId;
        }

        public uint NextRequestId { get; private set; }

        public int BufferedCount { get; private set; }

        public bool TryInsert(in NetworkCommandRequest request)
        {
            if (request.RequestId == NextRequestId)
            {
                return InsertAtSlot(request);
            }

            if (!SerialNumber32.IsNewer(request.RequestId, NextRequestId))
            {
                return true;
            }

            uint distance = request.RequestId - NextRequestId;
            return distance < _requestTags.Length && InsertAtSlot(request);
        }

        public bool TryTakeNext(out NetworkCommandRequest request)
        {
            int slot = (int)(NextRequestId % (uint)_requestTags.Length);
            if (!_occupied[slot] || _requestTags[slot] != NextRequestId)
            {
                request = default;
                return false;
            }

            request = _requests[slot];
            _occupied[slot] = false;
            NextRequestId++;
            BufferedCount--;
            return true;
        }

        private bool InsertAtSlot(in NetworkCommandRequest request)
        {
            int slot = (int)(request.RequestId % (uint)_requestTags.Length);
            if (_occupied[slot])
            {
                return _requestTags[slot] == request.RequestId;
            }

            _requestTags[slot] = request.RequestId;
            _requests[slot] = request;
            _occupied[slot] = true;
            BufferedCount++;
            return true;
        }
    }
}

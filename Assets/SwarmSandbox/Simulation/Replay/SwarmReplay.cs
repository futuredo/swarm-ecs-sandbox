using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Netcode;

namespace SwarmECS.Simulation.Replay
{
    public static class SwarmReplayLimits
    {
        public const int MaxPayloadBytes = 64 * 1024 * 1024;
        public const int MaxAgentCapacity = 1_000_000;
        public const int MaxNeighborCount = 4_096;
        public const int MaxCommandCount = 1_000_000;
        public const int MaxCheckpointCount = 1_000_000;
        public const int MaxTick = 1_000_000;
        public const long MaxExecutionWork = 50_000_000L;
    }

    public readonly struct SwarmReplayCheckpoint
    {
        public SwarmReplayCheckpoint(int tick, ulong stateHash)
        {
            Tick = tick;
            StateHash = stateHash;
        }

        public int Tick { get; }

        public ulong StateHash { get; }
    }

    /// <summary>
    /// Immutable in-memory representation of one deterministic replay. Variable-size
    /// streams are copied at construction so serialized bytes cannot race mutations.
    /// </summary>
    public sealed class SwarmReplay
    {
        private readonly SimulationCommand[] _commands;
        private readonly SwarmReplayCheckpoint[] _checkpoints;

        public SwarmReplay(
            ulong logicHash,
            SwarmConfig config,
            uint seed,
            int agentCount,
            SimulationCommand[] commands,
            SwarmReplayCheckpoint[] checkpoints)
            : this(
                logicHash,
                config,
                seed,
                agentCount,
                InferFinalTick(checkpoints),
                commands,
                checkpoints)
        {
        }

        public SwarmReplay(
            ulong logicHash,
            SwarmConfig config,
            uint seed,
            int agentCount,
            int finalTick,
            SimulationCommand[] commands,
            SwarmReplayCheckpoint[] checkpoints)
        {
            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (checkpoints == null)
            {
                throw new ArgumentNullException(nameof(checkpoints));
            }

            if (commands.Length > SwarmReplayLimits.MaxCommandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(commands));
            }

            if (checkpoints.Length > SwarmReplayLimits.MaxCheckpointCount)
            {
                throw new ArgumentOutOfRangeException(nameof(checkpoints));
            }

            ValidateLogicHash(logicHash);
            ValidateConfig(config);
            ValidateAgentCount(agentCount, config.Capacity);
            ValidateCommands(commands);
            ValidateCheckpoints(checkpoints);
            ValidateTimelineBounds(finalTick, commands, checkpoints);
            ValidateWorkload(agentCount, finalTick, commands.Length, checkpoints.Length);

            _commands = new SimulationCommand[commands.Length];
            Array.Copy(commands, _commands, commands.Length);
            _checkpoints = new SwarmReplayCheckpoint[checkpoints.Length];
            Array.Copy(checkpoints, _checkpoints, checkpoints.Length);

            LogicHash = logicHash;
            Config = config;
            Seed = seed;
            AgentCount = agentCount;
            FinalTick = finalTick;
        }

        public ulong LogicHash { get; }

        public SwarmConfig Config { get; }

        public uint Seed { get; }

        public int AgentCount { get; }

        public int FinalTick { get; }

        public int CommandCount => _commands.Length;

        public int CheckpointCount => _checkpoints.Length;

        public SimulationCommand GetCommand(int index)
        {
            return _commands[index];
        }

        public SwarmReplayCheckpoint GetCheckpoint(int index)
        {
            return _checkpoints[index];
        }

        public SimulationCommand[] CopyCommands()
        {
            var copy = new SimulationCommand[_commands.Length];
            Array.Copy(_commands, copy, copy.Length);
            return copy;
        }

        public SwarmReplayCheckpoint[] CopyCheckpoints()
        {
            var copy = new SwarmReplayCheckpoint[_checkpoints.Length];
            Array.Copy(_checkpoints, copy, copy.Length);
            return copy;
        }

        internal static void ValidateLogicHash(ulong logicHash)
        {
            if (logicHash == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(logicHash), "Logic hash must be non-zero.");
            }
        }

        internal static void ValidateConfig(SwarmConfig config)
        {
            if (config.Capacity <= 0 || config.Capacity > SwarmReplayLimits.MaxAgentCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "Replay capacity is outside the supported limit.");
            }

            if (config.FixedDeltaTime <= FP.Zero ||
                config.AgentRadius < FP.Zero ||
                config.MaxSpeed < FP.Zero ||
                config.MaxAcceleration < FP.Zero ||
                config.NeighborDistance <= FP.Zero ||
                config.MaxNeighbors < 0 ||
                config.MaxNeighbors > SwarmReplayLimits.MaxNeighborCount ||
                config.TimeHorizon <= FP.Zero ||
                config.WorldHalfExtent <= FP.Zero)
            {
                throw new ArgumentException("Replay contains an invalid simulation configuration.", nameof(config));
            }

            if ((uint)config.SpatialIndexMode > (uint)SpatialIndexMode.KdTreeKNearest)
            {
                throw new ArgumentOutOfRangeException(nameof(config), "Replay contains an invalid spatial-index mode.");
            }

            SwarmConfig canonical;
            try
            {
                canonical = new SwarmConfig(
                    config.Capacity,
                    config.FixedDeltaTime,
                    config.AgentRadius,
                    config.MaxSpeed,
                    config.MaxAcceleration,
                    config.MaxTurnStep,
                    config.NeighborDistance,
                    config.MaxNeighbors,
                    config.TimeHorizon,
                    config.WorldHalfExtent,
                    config.SpatialIndexMode);
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException(
                    "Replay contains an invalid simulation configuration.",
                    nameof(config),
                    exception);
            }

            if (canonical.ConfigHash != config.ConfigHash)
            {
                throw new ArgumentException("Replay configuration hash is not canonical.", nameof(config));
            }
        }

        internal static void ValidateAgentCount(int agentCount, int capacity)
        {
            if (agentCount < 0 || agentCount > capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(agentCount));
            }
        }

        internal static void ValidateCommands(SimulationCommand[] commands)
        {
            int previousTick = -1;
            int previousSequence = -1;
            for (int i = 0; i < commands.Length; ++i)
            {
                SimulationCommand command = commands[i];
                if (command.Tick < 0 || command.Tick > SwarmReplayLimits.MaxTick || command.Sequence < 0)
                {
                    throw new ArgumentException("Replay command tick or sequence is outside the supported range.", nameof(commands));
                }

                switch (command.Type)
                {
                    case SimulationCommandType.SetGroupTarget:
                        if (command.Group >= SwarmWorld.GroupCount)
                        {
                            throw new ArgumentException("Replay group-target command has an invalid group.", nameof(commands));
                        }

                        break;
                    case SimulationCommandType.SetSpatialIndexMode:
                        if (command.Group > (byte)SpatialIndexMode.KdTreeKNearest)
                        {
                            throw new ArgumentException("Replay spatial-mode command has an invalid mode.", nameof(commands));
                        }

                        if (command.Value != FPVector2.Zero)
                        {
                            throw new ArgumentException(
                                "Replay spatial-mode command must use a canonical zero value.",
                                nameof(commands));
                        }

                        break;
                    default:
                        throw new ArgumentException("Replay contains an unknown command type.", nameof(commands));
                }

                if (i > 0 &&
                    (command.Tick < previousTick ||
                     (command.Tick == previousTick && command.Sequence <= previousSequence)))
                {
                    throw new ArgumentException(
                        "Replay commands must be strictly ordered by tick and sequence.",
                        nameof(commands));
                }

                previousTick = command.Tick;
                previousSequence = command.Sequence;
            }
        }

        internal static void ValidateCheckpoints(SwarmReplayCheckpoint[] checkpoints)
        {
            int previousTick = -1;
            for (int i = 0; i < checkpoints.Length; ++i)
            {
                int tick = checkpoints[i].Tick;
                if (tick < 0 || tick > SwarmReplayLimits.MaxTick || (i > 0 && tick <= previousTick))
                {
                    throw new ArgumentException(
                        "Replay checkpoints must use strictly increasing non-negative ticks.",
                        nameof(checkpoints));
                }

                previousTick = tick;
            }
        }

        private static int InferFinalTick(SwarmReplayCheckpoint[] checkpoints)
        {
            if (checkpoints == null)
            {
                throw new ArgumentNullException(nameof(checkpoints));
            }

            if (checkpoints.Length == 0)
            {
                throw new ArgumentException("Replay requires a final hash checkpoint.", nameof(checkpoints));
            }

            return checkpoints[checkpoints.Length - 1].Tick;
        }

        private static void ValidateTimelineBounds(
            int finalTick,
            SimulationCommand[] commands,
            SwarmReplayCheckpoint[] checkpoints)
        {
            if (finalTick < 0 || finalTick > SwarmReplayLimits.MaxTick)
            {
                throw new ArgumentOutOfRangeException(nameof(finalTick));
            }

            if (checkpoints.Length == 0 || checkpoints[checkpoints.Length - 1].Tick != finalTick)
            {
                throw new ArgumentException(
                    "Replay must end with a checkpoint at its declared final tick.",
                    nameof(checkpoints));
            }

            for (int i = 0; i < commands.Length; ++i)
            {
                if (commands[i].Tick >= finalTick)
                {
                    throw new ArgumentException(
                        "Replay commands must execute before the final checkpoint.",
                        nameof(commands));
                }
            }
        }

        internal static void ValidateWorkload(
            int agentCount,
            int finalTick,
            int commandCount,
            int checkpointCount)
        {
            if (commandCount < 0 || checkpointCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(commandCount),
                    "Replay stream counts must be non-negative.");
            }

            long ticks = finalTick + 1L;
            long work = ((long)agentCount + 1L) * ticks;
            work += commandCount;
            work += checkpointCount;
            if (work > SwarmReplayLimits.MaxExecutionWork)
            {
                throw new ArgumentException(
                    "Replay execution workload exceeds the supported budget.");
            }
        }
    }
}

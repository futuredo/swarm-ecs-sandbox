using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Netcode
{

public enum SimulationCommandType : byte
{
    SetGroupTarget = 0,
    SetSpatialIndexMode = 1,
}

public readonly struct SimulationCommand
{
    public SimulationCommand(int tick, int sequence, SimulationCommandType type, byte group, FPVector2 value)
    {
        Tick = tick;
        Sequence = sequence;
        Type = type;
        Group = group;
        Value = value;
    }

    public int Tick { get; }

    public int Sequence { get; }

    public SimulationCommandType Type { get; }

    public byte Group { get; }

    public FPVector2 Value { get; }
}

/// <summary>
/// Fixed command history used by normal simulation and rollback replay.
/// </summary>
public sealed class CommandTimeline
{
    private readonly SimulationCommand[] _commands;
    private int _count;
    private int _nextApplyIndex;
    private int _lastAppliedTick = -1;

    public CommandTimeline(int capacity)
    {
        _commands = new SimulationCommand[capacity];
    }

    public int Count => _count;

    public bool Add(SimulationCommand command)
    {
        for (int i = 0; i < _count; i++)
        {
            SimulationCommand current = _commands[i];
            if (current.Tick == command.Tick && current.Sequence == command.Sequence)
            {
                _commands[i] = command;
                ResetPlaybackCursor();
                return true;
            }
        }

        if (_count >= _commands.Length)
        {
            return false;
        }

        int insertionIndex = _count;
        while (insertionIndex > 0 && ComesBefore(command, _commands[insertionIndex - 1]))
        {
            _commands[insertionIndex] = _commands[insertionIndex - 1];
            insertionIndex--;
        }

        _commands[insertionIndex] = command;
        _count++;
        ResetPlaybackCursor();
        return true;
    }

    /// <summary>
    /// Appends one command from an already validated canonical stream in O(1). This
    /// avoids the duplicate scan and insertion sort required for live out-of-order
    /// arrivals. The new command must be strictly later by (tick, sequence).
    /// </summary>
    public bool AppendOrdered(SimulationCommand command)
    {
        if (_count >= _commands.Length ||
            (_count > 0 && !ComesBefore(_commands[_count - 1], command)))
        {
            return false;
        }

        _commands[_count++] = command;
        ResetPlaybackCursor();
        return true;
    }

    /// <summary>
    /// Discards the sorted prefix that can no longer participate in rollback.
    /// Remaining commands keep their canonical (tick, sequence) order and the
    /// fixed backing storage is reused without managed allocation.
    /// </summary>
    public int DiscardBeforeTick(int minimumTickInclusive)
    {
        int firstRetained = 0;
        while (firstRetained < _count &&
            _commands[firstRetained].Tick < minimumTickInclusive)
        {
            firstRetained++;
        }

        if (firstRetained == 0)
        {
            return 0;
        }

        int retainedCount = _count - firstRetained;
        for (int i = 0; i < retainedCount; i++)
        {
            _commands[i] = _commands[firstRetained + i];
        }

        _count = retainedCount;
        ResetPlaybackCursor();
        return firstRetained;
    }

    public void ApplyAtTick(SwarmWorld world, int tick)
    {
        // Sequential playback visits each retained command once. Rollback or an
        // explicit repeated tick rewinds the cursor and preserves the old semantics.
        if (tick <= _lastAppliedTick)
        {
            _nextApplyIndex = 0;
        }

        while (_nextApplyIndex < _count && _commands[_nextApplyIndex].Tick < tick)
        {
            _nextApplyIndex++;
        }

        while (_nextApplyIndex < _count && _commands[_nextApplyIndex].Tick == tick)
        {
            SimulationCommand command = _commands[_nextApplyIndex++];
            if (command.Type == SimulationCommandType.SetGroupTarget)
            {
                world.SetGroupTarget(command.Group, command.Value);
            }
            else if (command.Type == SimulationCommandType.SetSpatialIndexMode)
            {
                world.SetSpatialIndexMode((SpatialIndexMode)command.Group);
            }
        }

        _lastAppliedTick = tick;
    }

    public void Clear()
    {
        _count = 0;
        ResetPlaybackCursor();
    }

    private void ResetPlaybackCursor()
    {
        _nextApplyIndex = 0;
        _lastAppliedTick = -1;
    }

    private static bool ComesBefore(SimulationCommand left, SimulationCommand right)
    {
        return left.Tick < right.Tick ||
            (left.Tick == right.Tick && left.Sequence < right.Sequence);
    }
}
}

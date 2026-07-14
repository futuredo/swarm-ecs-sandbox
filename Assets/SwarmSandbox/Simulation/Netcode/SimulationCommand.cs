using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Netcode
{

public enum SimulationCommandType : byte
{
    SetGroupTarget = 0,
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
                return true;
            }
        }

        if (_count >= _commands.Length)
        {
            return false;
        }

        _commands[_count++] = command;
        return true;
    }

    public void ApplyAtTick(SwarmWorld world, int tick)
    {
        for (int i = 0; i < _count; i++)
        {
            SimulationCommand command = _commands[i];
            if (command.Tick != tick)
            {
                continue;
            }

            if (command.Type == SimulationCommandType.SetGroupTarget)
            {
                world.SetGroupTarget(command.Group, command.Value);
            }
        }
    }

    public void Clear()
    {
        _count = 0;
    }
}
}

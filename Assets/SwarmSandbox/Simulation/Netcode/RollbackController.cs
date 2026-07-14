using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Netcode
{

/// <summary>
/// GGPO-style state restore and deterministic replay over a fixed command timeline.
/// </summary>
public sealed class RollbackController
{
    private readonly SwarmWorld _world;
    private readonly IDeterministicSimulation _simulation;
    private readonly CommandTimeline _commands;
    private readonly WorldSnapshotRing _snapshots;
    private int _nextSequence;

    public RollbackController(
        SwarmWorld world,
        IDeterministicSimulation simulation,
        int historyLength = 64,
        int commandCapacity = 512)
    {
        _world = world;
        _simulation = simulation;
        _commands = new CommandTimeline(commandCapacity);
        _snapshots = new WorldSnapshotRing(world.Config.Capacity, historyLength);
        _snapshots.Save(world);
    }

    public int RollbackCount { get; private set; }

    public int LastResimulatedTicks { get; private set; }

    public int TotalResimulatedTicks { get; private set; }

    public int HistoryLength => _snapshots.HistoryLength;

    public ulong LastHashBeforeRollback { get; private set; }

    public ulong LastHashAfterRollback { get; private set; }

    public void Step()
    {
        _snapshots.Save(_world);
        _commands.ApplyAtTick(_world, _world.Tick);
        _simulation.Step(_world);
        _world.AdvanceTick();
    }

    public bool QueueCommand(SimulationCommand command)
    {
        return _commands.Add(command);
    }

    public bool InjectLateGroupTarget(int latencyTicks, int group, FPVector2 target)
    {
        int currentTick = _world.Tick;
        int clampedLatency = latencyTicks < 1 ? 1 : latencyTicks;
        if (clampedLatency >= HistoryLength)
        {
            clampedLatency = HistoryLength - 1;
        }

        int originTick = currentTick - clampedLatency;
        if (originTick < 0)
        {
            return false;
        }

        SimulationCommand command = new(
            originTick,
            _nextSequence++,
            SimulationCommandType.SetGroupTarget,
            (byte)group,
            target);
        if (!_commands.Add(command))
        {
            return false;
        }

        LastHashBeforeRollback = _world.ComputeStateHash();
        if (!_snapshots.TryRestore(_world, originTick))
        {
            return false;
        }

        int replayed = 0;
        while (_world.Tick < currentTick)
        {
            Step();
            replayed++;
        }

        LastResimulatedTicks = replayed;
        TotalResimulatedTicks += replayed;
        RollbackCount++;
        LastHashAfterRollback = _world.ComputeStateHash();
        return true;
    }

    public void ResetHistory()
    {
        _commands.Clear();
        _snapshots.Clear();
        _snapshots.Save(_world);
        _nextSequence = 0;
        RollbackCount = 0;
        LastResimulatedTicks = 0;
        TotalResimulatedTicks = 0;
        LastHashBeforeRollback = 0;
        LastHashAfterRollback = 0;
    }
}
}

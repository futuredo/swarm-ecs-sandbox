using SwarmECS.FixedPoint;

namespace SwarmECS.Simulation.Netcode
{

/// <summary>
/// Preallocated rollback history. A 10k-agent, 64-frame history occupies about 10 MB.
/// </summary>
public sealed class WorldSnapshotRing
{
    private readonly int _agentCapacity;
    private readonly int _historyLength;
    private readonly int[] _ticks;
    private readonly int[] _counts;
    private readonly int[] _positionX;
    private readonly int[] _positionY;
    private readonly int[] _velocityX;
    private readonly int[] _velocityY;
    private readonly ushort[] _pathCursors;
    private readonly int[] _groupTargetX;
    private readonly int[] _groupTargetY;

    public WorldSnapshotRing(int agentCapacity, int historyLength)
    {
        _agentCapacity = agentCapacity;
        _historyLength = historyLength;
        _ticks = new int[historyLength];
        _counts = new int[historyLength];
        _positionX = new int[agentCapacity * historyLength];
        _positionY = new int[agentCapacity * historyLength];
        _velocityX = new int[agentCapacity * historyLength];
        _velocityY = new int[agentCapacity * historyLength];
        _pathCursors = new ushort[agentCapacity * historyLength];
        _groupTargetX = new int[SwarmWorld.GroupCount * historyLength];
        _groupTargetY = new int[SwarmWorld.GroupCount * historyLength];

        for (int i = 0; i < _ticks.Length; i++)
        {
            _ticks[i] = int.MinValue;
        }
    }

    public int HistoryLength => _historyLength;

    public void Save(SwarmWorld world)
    {
        int slot = PositiveModulo(world.Tick, _historyLength);
        int agentOffset = slot * _agentCapacity;
        int targetOffset = slot * SwarmWorld.GroupCount;
        _ticks[slot] = world.Tick;
        _counts[slot] = world.Count;

        for (int i = 0; i < world.Count; i++)
        {
            _positionX[agentOffset + i] = world.Positions[i].X.Raw;
            _positionY[agentOffset + i] = world.Positions[i].Y.Raw;
            _velocityX[agentOffset + i] = world.Velocities[i].X.Raw;
            _velocityY[agentOffset + i] = world.Velocities[i].Y.Raw;
            _pathCursors[agentOffset + i] = world.PathCursors[i];
        }

        for (int i = 0; i < SwarmWorld.GroupCount; i++)
        {
            _groupTargetX[targetOffset + i] = world.GroupTargets[i].X.Raw;
            _groupTargetY[targetOffset + i] = world.GroupTargets[i].Y.Raw;
        }
    }

    public bool TryRestore(SwarmWorld world, int tick)
    {
        int slot = PositiveModulo(tick, _historyLength);
        if (_ticks[slot] != tick || _counts[slot] != world.Count)
        {
            return false;
        }

        int agentOffset = slot * _agentCapacity;
        int targetOffset = slot * SwarmWorld.GroupCount;
        for (int i = 0; i < world.Count; i++)
        {
            world.Positions[i] = new FPVector2(
                FP.FromRaw(_positionX[agentOffset + i]),
                FP.FromRaw(_positionY[agentOffset + i]));
            world.Velocities[i] = new FPVector2(
                FP.FromRaw(_velocityX[agentOffset + i]),
                FP.FromRaw(_velocityY[agentOffset + i]));
            world.PathCursors[i] = _pathCursors[agentOffset + i];
        }

        for (int i = 0; i < SwarmWorld.GroupCount; i++)
        {
            world.GroupTargets[i] = new FPVector2(
                FP.FromRaw(_groupTargetX[targetOffset + i]),
                FP.FromRaw(_groupTargetY[targetOffset + i]));
        }

        world.Tick = tick;
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < _ticks.Length; i++)
        {
            _ticks[i] = int.MinValue;
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }
}
}

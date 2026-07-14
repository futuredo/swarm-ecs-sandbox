using SwarmECS.FixedPoint;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Pathfinding;

namespace SwarmECS.Simulation.Systems
{

/// <summary>
/// Four shared A* routes feed 10,000 agents; agents retain only a ushort cursor.
/// Traversal penalties are blurred around SAT obstacle cells at initialization.
/// </summary>
public sealed class SharedPathNavigationSystem
{
    private static readonly FP WaypointRadiusSquared = FP.FromInt(9);
    private readonly GridMap _map;
    private readonly AStarPathfinder _pathfinder;
    private readonly SharedPath[] _groupPaths;
    private readonly FPVector2[] _pathGoals;

    public SharedPathNavigationSystem(SwarmWorld world, FPOrientedBox2[] obstacles)
    {
        _map = new GridMap(
            64,
            64,
            FP.FromRatio(5, 2),
            new FPVector2(FP.FromInt(-80), FP.FromInt(-80)));
        RasterizeObstacles(obstacles, world.Config.AgentRadius);
        BlurTraversalPenalties();

        _pathfinder = new AStarPathfinder(_map);
        _groupPaths = new SharedPath[SwarmWorld.GroupCount];
        _pathGoals = new FPVector2[SwarmWorld.GroupCount];
        for (int i = 0; i < _groupPaths.Length; i++)
        {
            _groupPaths[i] = new SharedPath(_map.NodeCount);
            _pathGoals[i] = world.GroupTargets[i];
        }

        FPVector2[] starts =
        {
            new(FP.FromInt(-46), FP.FromInt(-46)),
            new(FP.FromInt(46), FP.FromInt(-46)),
            new(FP.FromInt(46), FP.FromInt(46)),
            new(FP.FromInt(-46), FP.FromInt(46)),
        };

        for (int group = 0; group < SwarmWorld.GroupCount; group++)
        {
            if (_map.TryWorldToCell(starts[group], out int startX, out int startY) &&
                _map.TryWorldToCell(_pathGoals[group], out int goalX, out int goalY))
            {
                _pathfinder.FindSharedPath(startX, startY, goalX, goalY, _groupPaths[group]);
            }
        }
    }

    public GridMap Map => _map;

    public SharedPath GetGroupPath(int group) => _groupPaths[group];

    public int TotalSharedWaypoints
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _groupPaths.Length; i++)
            {
                count += _groupPaths[i].Count;
            }

            return count;
        }
    }

    public void Execute(SwarmWorld world)
    {
        for (int i = 0; i < world.Count; i++)
        {
            int group = world.Groups[i];
            SharedPath path = _groupPaths[group];
            FPVector2 target;

            // Runtime/rollback commands deliberately bypass the cached static route,
            // proving that late authoritative input changes simulation immediately.
            if (world.GroupTargets[group] != _pathGoals[group] || path.Count == 0)
            {
                target = world.GetTargetForAgent(i);
            }
            else
            {
                int cursor = world.PathCursors[i];
                if (cursor >= path.Count)
                {
                    cursor = path.Count - 1;
                }

                target = path.Waypoints[cursor] + world.FormationOffsets[i];
                FPVector2 delta = target - world.Positions[i];
                if (delta.SqrMagnitude <= WaypointRadiusSquared && cursor + 1 < path.Count)
                {
                    cursor++;
                    world.PathCursors[i] = (ushort)cursor;
                    target = path.Waypoints[cursor] + world.FormationOffsets[i];
                }
            }

            FPVector2 toTarget = target - world.Positions[i];
            world.PreferredVelocities[i] = toTarget.SqrMagnitude <= FP.FromRatio(1, 4)
                ? FPVector2.Zero
                : FPMath.NormalizeSafe(toTarget) * world.MaxSpeeds[i];
        }
    }

    private void RasterizeObstacles(FPOrientedBox2[] obstacles, FP agentRadius)
    {
        FP clearance = (_map.CellSize * FP.FromRatio(3, 5)) + agentRadius;
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                FPCircle2 sample = new(_map.CellCenter(x, y), clearance);
                for (int obstacleIndex = 0; obstacleIndex < obstacles.Length; obstacleIndex++)
                {
                    FPOrientedBox2 obstacle = obstacles[obstacleIndex];
                    if (!FPSat2D.Intersect(in obstacle, in sample, out _, out _))
                    {
                        continue;
                    }

                    _map.SetWalkable(x, y, false);
                    break;
                }
            }
        }
    }

    private void BlurTraversalPenalties()
    {
        const int radius = 3;
        for (int y = 0; y < _map.Height; y++)
        {
            for (int x = 0; x < _map.Width; x++)
            {
                if (!_map.IsWalkable(x, y))
                {
                    continue;
                }

                int penalty = 0;
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        int sampleX = x + offsetX;
                        int sampleY = y + offsetY;
                        if (!_map.IsInside(sampleX, sampleY) || _map.IsWalkable(sampleX, sampleY))
                        {
                            continue;
                        }

                        penalty += GaussianWeight(offsetX) * GaussianWeight(offsetY);
                    }
                }

                if (penalty > 0)
                {
                    _map.SetPenalty(x, y, penalty);
                }
            }
        }
    }

    private static int GaussianWeight(int offset)
    {
        int absolute = offset < 0 ? -offset : offset;
        return absolute switch
        {
            0 => 20,
            1 => 12,
            2 => 5,
            3 => 1,
            _ => 0,
        };
    }
}
}

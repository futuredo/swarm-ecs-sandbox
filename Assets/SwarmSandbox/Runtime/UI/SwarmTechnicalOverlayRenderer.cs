using System;
using SwarmECS.FixedPoint;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Avoidance;
using SwarmECS.Simulation.Collision;
using SwarmECS.Simulation.Pathfinding;
using SwarmECS.Simulation.Systems;
using UnityEngine;
using UnityEngine.Rendering;

namespace SwarmECS.Runtime
{
    /// <summary>
    /// Presentation-only visual diagnostics for the interactive technical lab.
    /// Every buffer is fixed-capacity and none of this state participates in the
    /// authoritative simulation, snapshots, hashes or replay schema.
    /// </summary>
    [RequireComponent(typeof(SwarmSimulationHost), typeof(SwarmDebugHud))]
    public sealed class SwarmTechnicalOverlayRenderer : MonoBehaviour
    {
        private const int DiagnosticNeighborCapacity = 16;
        private const int DiagnosticLineCapacity = 64;
        private const int CollisionTraceCapacity = 64;
        private const int CollisionTraceLifetimeTicks = 90;
        private const float RawToFloat = 1f / FP.OneRaw;

        private static readonly Color GridColor = new(0.12f, 0.38f, 0.48f, 0.20f);
        private static readonly Color BlockedColor = new(1f, 0.24f, 0.22f, 0.42f);
        private static readonly Color ObstacleColor = new(1f, 0.54f, 0.16f, 0.88f);
        private static readonly Color NeighborColor = new(0.20f, 0.78f, 1f, 0.42f);
        private static readonly Color AgentOrcaColor = new(0.32f, 0.66f, 1f, 0.46f);
        private static readonly Color ObstacleOrcaColor = new(1f, 0.42f, 0.16f, 0.62f);
        private static readonly Color PreferredVelocityColor = new(1f, 0.88f, 0.16f, 1f);
        private static readonly Color SolvedVelocityColor = new(0.16f, 1f, 0.82f, 1f);
        private static readonly Color BvhInternalColor = new(0.72f, 0.28f, 1f, 0.36f);
        private static readonly Color BvhLeafColor = new(1f, 0.24f, 0.62f, 0.66f);
        private static readonly Color CcdPathColor = new(1f, 0.20f, 0.18f, 0.74f);
        private static readonly Color CcdImpactColor = new(1f, 0.68f, 0.12f, 1f);
        private static readonly Color CcdSlideColor = new(0.10f, 1f, 0.80f, 1f);
        private static readonly Color ContactNormalColor = new(1f, 0.94f, 0.40f, 1f);
        private static readonly Color RollbackBeforeColor = new(1f, 0.22f, 0.74f, 0.78f);
        private static readonly Color RollbackAfterColor = new(0.14f, 0.96f, 1f, 0.94f);
        private static readonly Color[] GroupColors =
        {
            new(0.18f, 0.88f, 1f, 0.96f),
            new(1f, 0.42f, 0.28f, 0.96f),
            new(0.52f, 1f, 0.36f, 0.96f),
            new(0.92f, 0.46f, 1f, 0.96f),
        };

        private struct CollisionTrace
        {
            public SweepContactDiagnostic Diagnostic;
            public int ExpireTick;
        }

        private readonly int[] _neighborIds = new int[DiagnosticNeighborCapacity];
        private readonly OrcaLine[] _orcaLines = new OrcaLine[DiagnosticLineCapacity];
        private readonly CollisionTrace[] _collisionTraces = new CollisionTrace[CollisionTraceCapacity];

        private SwarmSimulationHost _host;
        private SwarmDebugHud _hud;
        private Material _lineMaterial;
        private GUIStyle _worldLabelStyle;
        private FPOrientedBox2[] _obstacles = Array.Empty<FPOrientedBox2>();
        private ObstacleSegment[] _obstacleSegments = Array.Empty<ObstacleSegment>();
        private int _cachedWorldEpoch = int.MinValue;
        private int _lastDiagnosticTick = int.MinValue;
        private int _lastCollisionCaptureTick = int.MinValue;
        private int _collisionTraceWrite;
        private int _collisionTraceCount;
        private int _sampleGroup;
        private FPVector2 _diagnosticSolvedVelocity;
        private FPVector2 _probeStart;
        private FPVector2 _probeRequestedEnd;
        private FPVector2 _probeImpact;
        private FPVector2 _probeSlideEnd;
        private FPVector2 _probeNormal;

        public bool OverlaysEnabled { get; set; } = true;

        public int SelectedAgentId { get; private set; } = -1;

        public int DiagnosticNeighborCount { get; private set; }

        public int DiagnosticLineCount { get; private set; }

        public int DiagnosticObstacleLineCount { get; private set; }

        public int RecentCollisionTraceCount => CountActiveCollisionTraces();

        public bool CollisionProbeHit { get; private set; }

        public int CollisionProbeFractionRaw { get; private set; }

        private void Awake()
        {
            _host = GetComponent<SwarmSimulationHost>();
            _hud = GetComponent<SwarmDebugHud>();
            EnsureLineMaterial();
        }

        private void LateUpdate()
        {
            RebindWorldIfNeeded();
            CaptureCollisionDiagnostics();

            if (_hud != null && _hud.ActiveView == SwarmLabView.Avoidance)
            {
                UpdateAvoidanceDiagnostic();
            }
        }

        private void OnRenderObject()
        {
            if (!OverlaysEnabled || _lineMaterial == null || _host?.World == null ||
                _hud == null || Camera.current != Camera.main)
            {
                return;
            }

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.LINES);

            switch (_hud.ActiveView)
            {
                case SwarmLabView.Navigation:
                    DrawNavigationGrid();
                    DrawObstacleGeometry(0.82f);
                    DrawSharedPaths(true);
                    DrawGroupTargets();
                    break;
                case SwarmLabView.Avoidance:
                    DrawObstacleGeometry(0.58f);
                    DrawAvoidanceDiagnostic();
                    break;
                case SwarmLabView.Collision:
                    DrawObstacleGeometry(1f);
                    DrawBvh();
                    DrawCollisionProbe();
                    DrawCollisionTraces();
                    break;
                case SwarmLabView.Rollback:
                    DrawObstacleGeometry(0.34f);
                    DrawSharedPaths(false);
                    DrawRollbackGhost();
                    DrawGroupTargets();
                    break;
                default:
                    DrawObstacleGeometry(0.52f);
                    DrawSharedPaths(false);
                    DrawGroupTargets();
                    break;
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            if (!OverlaysEnabled || _host?.World == null || _hud == null)
            {
                return;
            }

            EnsureWorldLabelStyle();
            switch (_hud.ActiveView)
            {
                case SwarmLabView.Navigation:
                    for (int group = 0; group < SwarmWorld.GroupCount; group++)
                    {
                        DrawWorldLabel(
                            _host.World.GroupTargets[group],
                            "G" + group + (_hud.IsChinese ? " 目标" : " TARGET"),
                            GroupColors[group]);
                    }
                    break;
                case SwarmLabView.Avoidance:
                    if ((uint)SelectedAgentId < (uint)_host.World.Count)
                    {
                        DrawWorldLabel(
                            _host.World.Positions[SelectedAgentId],
                            (_hud.IsChinese ? "采样 #" : "SAMPLE #") + SelectedAgentId,
                            SolvedVelocityColor);
                    }
                    break;
                case SwarmLabView.Collision:
                    if (CollisionProbeHit)
                    {
                        DrawWorldLabel(_probeImpact, _hud.IsChinese ? "CCD 碰撞时刻" : "CCD TOI", CcdImpactColor);
                    }
                    break;
                case SwarmLabView.Rollback:
                    if (_host.RollbackGhostCount == 0)
                    {
                        DrawWorldLabel(
                            FPVector2.Zero,
                            _hud.IsChinese ? "按 L / 注入迟到命令" : "PRESS L / LATE CMD",
                            RollbackAfterColor);
                    }
                    break;
            }
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
                _lineMaterial = null;
            }
        }

        public void CycleSampleAgent()
        {
            _sampleGroup = (_sampleGroup + 1) % SwarmWorld.GroupCount;
            _lastDiagnosticTick = int.MinValue;
        }

        public void ClearCollisionTraces()
        {
            _collisionTraceCount = 0;
            _collisionTraceWrite = 0;
        }

        private void RebindWorldIfNeeded()
        {
            if (_host?.Simulation == null || _cachedWorldEpoch == _host.WorldEpoch)
            {
                return;
            }

            _cachedWorldEpoch = _host.WorldEpoch;
            _obstacles = _host.Simulation.Obstacles.Obstacles;
            _obstacleSegments = _host.Simulation.Obstacles.ObstacleSegments;
            _lastDiagnosticTick = int.MinValue;
            _lastCollisionCaptureTick = int.MinValue;
            SelectedAgentId = -1;
            DiagnosticNeighborCount = 0;
            DiagnosticLineCount = 0;
            DiagnosticObstacleLineCount = 0;
            ClearCollisionTraces();
            BuildCollisionProbe();
        }

        private void UpdateAvoidanceDiagnostic()
        {
            SwarmWorld world = _host.World;
            if (world == null || world.Count == 0 || _lastDiagnosticTick == world.Tick)
            {
                return;
            }

            _lastDiagnosticTick = world.Tick;
            SelectedAgentId = FindCentralAgent(world, _sampleGroup);
            if (SelectedAgentId < 0 || !_host.Simulation.Avoidance.TryBuildDiagnosticSample(
                world,
                SelectedAgentId,
                _neighborIds,
                _orcaLines,
                out int neighborCount,
                out int lineCount,
                out int obstacleLineCount,
                out _diagnosticSolvedVelocity))
            {
                DiagnosticNeighborCount = 0;
                DiagnosticLineCount = 0;
                DiagnosticObstacleLineCount = 0;
                return;
            }

            DiagnosticNeighborCount = neighborCount;
            DiagnosticLineCount = lineCount;
            DiagnosticObstacleLineCount = obstacleLineCount;
        }

        private static int FindCentralAgent(SwarmWorld world, int group)
        {
            int selected = -1;
            ulong bestDistance = ulong.MaxValue;
            for (int entityId = group; entityId < world.Count; entityId += SwarmWorld.GroupCount)
            {
                if (world.Groups[entityId] != group)
                {
                    continue;
                }

                long x = world.Positions[entityId].X.Raw;
                long y = world.Positions[entityId].Y.Raw;
                ulong distance = (ulong)((x * x) + (y * y));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    selected = entityId;
                }
            }

            return selected;
        }

        private void CaptureCollisionDiagnostics()
        {
            if (_host?.Simulation == null || _host.World == null ||
                _lastCollisionCaptureTick == _host.World.Tick)
            {
                return;
            }

            _lastCollisionCaptureTick = _host.World.Tick;
            StaticObstacleCollisionSystem collision = _host.Simulation.Obstacles;
            for (int index = 0; index < collision.LastSweepDiagnosticCount; index++)
            {
                if (collision.TryGetLastSweepDiagnostic(index, out SweepContactDiagnostic diagnostic))
                {
                    AddCollisionTrace(diagnostic, _host.World.Tick + CollisionTraceLifetimeTicks);
                }
            }
        }

        private void AddCollisionTrace(SweepContactDiagnostic diagnostic, int expireTick)
        {
            _collisionTraces[_collisionTraceWrite] = new CollisionTrace
            {
                Diagnostic = diagnostic,
                ExpireTick = expireTick,
            };
            _collisionTraceWrite = (_collisionTraceWrite + 1) % _collisionTraces.Length;
            if (_collisionTraceCount < _collisionTraces.Length)
            {
                _collisionTraceCount++;
            }
        }

        private int CountActiveCollisionTraces()
        {
            if (_host?.World == null)
            {
                return 0;
            }

            int active = 0;
            int first = (_collisionTraceWrite - _collisionTraceCount + _collisionTraces.Length) %
                _collisionTraces.Length;
            for (int offset = 0; offset < _collisionTraceCount; offset++)
            {
                CollisionTrace trace = _collisionTraces[(first + offset) % _collisionTraces.Length];
                if (trace.ExpireTick >= _host.World.Tick)
                {
                    active++;
                }
            }

            return active;
        }

        private void BuildCollisionProbe()
        {
            CollisionProbeHit = false;
            CollisionProbeFractionRaw = 0;
            if (_obstacles.Length == 0)
            {
                return;
            }

            FPOrientedBox2 obstacle = _obstacles[_obstacles.Length - 1];
            _probeStart = new FPVector2(FP.FromInt(-24), FP.FromInt(-12));
            FPVector2 displacement = new(FP.FromInt(48), FP.FromInt(24));
            _probeRequestedEnd = _probeStart + displacement;
            FPCircle2 circle = new(_probeStart, FP.FromInt(1));
            if (!FPSweptCircle2D.SweepAgainstBox(
                in circle,
                displacement,
                FP.FromRaw(4),
                in obstacle,
                out FPSweepHit2D hit))
            {
                return;
            }

            CollisionProbeHit = true;
            CollisionProbeFractionRaw = hit.Fraction.Raw;
            _probeNormal = hit.Normal;
            _probeImpact = _probeStart + (displacement * hit.Fraction);
            FPVector2 remaining = displacement * (FP.One - hit.Fraction);
            FP inward = FPMath.Dot(remaining, hit.Normal);
            if (inward < FP.Zero)
            {
                remaining -= hit.Normal * inward;
            }

            _probeSlideEnd = _probeImpact + remaining;
        }

        private void DrawNavigationGrid()
        {
            GridMap map = _host.Simulation.Navigation.Map;
            float originX = map.Origin.X.Raw * RawToFloat;
            float originY = map.Origin.Y.Raw * RawToFloat;
            float cellSize = map.CellSize.Raw * RawToFloat;
            float maxX = originX + (map.Width * cellSize);
            float maxY = originY + (map.Height * cellSize);

            for (int x = 0; x <= map.Width; x++)
            {
                float worldX = originX + (x * cellSize);
                DrawLine(new Vector3(worldX, 0.06f, originY), new Vector3(worldX, 0.06f, maxY), GridColor);
            }

            for (int y = 0; y <= map.Height; y++)
            {
                float worldY = originY + (y * cellSize);
                DrawLine(new Vector3(originX, 0.06f, worldY), new Vector3(maxX, 0.06f, worldY), GridColor);
            }

            float half = cellSize * 0.42f;
            for (int node = 0; node < map.NodeCount; node++)
            {
                if (map.IsWalkable(node))
                {
                    continue;
                }

                FPVector2 center = map.CellCenter(node);
                Vector3 world = ToWorld(center, 0.08f);
                DrawLine(
                    world + new Vector3(-half, 0f, -half),
                    world + new Vector3(half, 0f, half),
                    BlockedColor);
                DrawLine(
                    world + new Vector3(-half, 0f, half),
                    world + new Vector3(half, 0f, -half),
                    BlockedColor);
            }
        }

        private void DrawSharedPaths(bool drawWaypoints)
        {
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                SharedPath path = _host.Simulation.Navigation.GetGroupPath(group);
                Color color = GroupColors[group];
                for (int waypoint = 1; waypoint < path.Count; waypoint++)
                {
                    DrawLine(ToWorld(path.Waypoints[waypoint - 1], 0.20f), ToWorld(path.Waypoints[waypoint], 0.20f), color);
                }

                if (!drawWaypoints)
                {
                    continue;
                }

                for (int waypoint = 0; waypoint < path.Count; waypoint += 4)
                {
                    DrawCircle(ToWorld(path.Waypoints[waypoint], 0.22f), 0.42f, 10, color);
                }
            }
        }

        private void DrawGroupTargets()
        {
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                Vector3 target = ToWorld(_host.World.GroupTargets[group], 0.30f);
                DrawCircle(target, 2.1f, 24, GroupColors[group]);
                DrawLine(target + Vector3.left * 2.6f, target + Vector3.right * 2.6f, GroupColors[group]);
                DrawLine(target + Vector3.back * 2.6f, target + Vector3.forward * 2.6f, GroupColors[group]);
            }
        }

        private void DrawObstacleGeometry(float alphaScale)
        {
            Color color = new(ObstacleColor.r, ObstacleColor.g, ObstacleColor.b, ObstacleColor.a * alphaScale);
            for (int index = 0; index < _obstacleSegments.Length; index++)
            {
                DrawLine(ToWorld(_obstacleSegments[index].Start, 0.18f), ToWorld(_obstacleSegments[index].End, 0.18f), color);
            }
        }

        private void DrawAvoidanceDiagnostic()
        {
            SwarmWorld world = _host.World;
            if ((uint)SelectedAgentId >= (uint)world.Count)
            {
                return;
            }

            FPVector2 selectedPosition = world.Positions[SelectedAgentId];
            Vector3 origin = ToWorld(selectedPosition, 0.44f);
            DrawCircle(origin, world.Config.NeighborDistance.Raw * RawToFloat, 36, NeighborColor);
            DrawCircle(origin, 0.78f, 18, SolvedVelocityColor);

            for (int index = 0; index < DiagnosticNeighborCount; index++)
            {
                int neighborId = _neighborIds[index];
                if ((uint)neighborId < (uint)world.Count)
                {
                    DrawLine(origin, ToWorld(world.Positions[neighborId], 0.34f), NeighborColor);
                }
            }

            DrawVelocityArrow(origin, world.PreferredVelocities[SelectedAgentId], PreferredVelocityColor, 1.25f);
            DrawVelocityArrow(origin, _diagnosticSolvedVelocity, SolvedVelocityColor, 1.25f);

            const float velocitySpaceScale = 1.25f;
            for (int index = 0; index < DiagnosticLineCount; index++)
            {
                OrcaLine line = _orcaLines[index];
                Vector3 point = origin + ToWorldDelta(line.Point, velocitySpaceScale);
                Vector3 direction = ToWorldDelta(line.Direction, 5.2f);
                Color color = index < DiagnosticObstacleLineCount
                    ? ObstacleOrcaColor
                    : AgentOrcaColor;
                DrawLine(point - direction, point + direction, color);
            }
        }

        private void DrawBvh()
        {
            var bvh = _host.Simulation.Obstacles.Broadphase;
            for (int node = 0; node < bvh.NodeCount; node++)
            {
                if (!bvh.TryGetNodeDiagnostic(node, out FPAabb2 bounds, out _, out _, out int obstacleId))
                {
                    continue;
                }

                DrawAabb(bounds, obstacleId >= 0 ? BvhLeafColor : BvhInternalColor, 0.12f + (node * 0.006f));
            }
        }

        private void DrawCollisionProbe()
        {
            DrawCircle(ToWorld(_probeStart, 0.52f), 1f, 24, Color.white);
            DrawLine(ToWorld(_probeStart, 0.48f), ToWorld(_probeRequestedEnd, 0.48f), CcdPathColor);
            DrawCircle(ToWorld(_probeRequestedEnd, 0.50f), 1f, 24, CcdPathColor);
            if (!CollisionProbeHit)
            {
                return;
            }

            Vector3 impact = ToWorld(_probeImpact, 0.62f);
            DrawCircle(impact, 1.25f, 28, CcdImpactColor);
            DrawArrow(impact, impact + ToWorldDelta(_probeNormal, 6f), ContactNormalColor);
            DrawArrow(impact, ToWorld(_probeSlideEnd, 0.60f), CcdSlideColor);
        }

        private void DrawCollisionTraces()
        {
            int first = (_collisionTraceWrite - _collisionTraceCount + _collisionTraces.Length) %
                _collisionTraces.Length;
            for (int offset = 0; offset < _collisionTraceCount; offset++)
            {
                CollisionTrace trace = _collisionTraces[(first + offset) % _collisionTraces.Length];
                if (trace.ExpireTick < _host.World.Tick)
                {
                    continue;
                }

                SweepContactDiagnostic diagnostic = trace.Diagnostic;
                Vector3 impact = ToWorld(diagnostic.Impact, 0.68f);
                DrawLine(ToWorld(diagnostic.Start, 0.54f), ToWorld(diagnostic.RequestedEnd, 0.54f), CcdPathColor);
                DrawCircle(impact, 0.72f, 16, CcdImpactColor);
                DrawArrow(impact, impact + ToWorldDelta(diagnostic.Normal, 3.2f), ContactNormalColor);
            }
        }

        private void DrawRollbackGhost()
        {
            for (int index = 0; index < _host.RollbackGhostCount; index++)
            {
                if (!_host.TryGetRollbackGhost(index, out _, out FPVector2 before, out FPVector2 after))
                {
                    continue;
                }

                Vector3 beforeWorld = ToWorld(before, 0.50f);
                Vector3 afterWorld = ToWorld(after, 0.58f);
                DrawCircle(beforeWorld, 0.58f, 12, RollbackBeforeColor);
                DrawCircle(afterWorld, 0.68f, 12, RollbackAfterColor);
                DrawLine(beforeWorld, afterWorld, RollbackAfterColor);
            }
        }

        private void DrawVelocityArrow(Vector3 origin, FPVector2 velocity, Color color, float scale)
        {
            DrawArrow(origin, origin + ToWorldDelta(velocity, scale), color);
        }

        private static void DrawAabb(FPAabb2 bounds, Color color, float height)
        {
            Vector3 minMin = ToWorld(bounds.Min, height);
            Vector3 maxMax = ToWorld(bounds.Max, height);
            Vector3 minMax = new(minMin.x, height, maxMax.z);
            Vector3 maxMin = new(maxMax.x, height, minMin.z);
            DrawLine(minMin, minMax, color);
            DrawLine(minMax, maxMax, color);
            DrawLine(maxMax, maxMin, color);
            DrawLine(maxMin, minMin, color);
        }

        private static void DrawArrow(Vector3 start, Vector3 end, Color color)
        {
            DrawLine(start, end, color);
            Vector3 direction = end - start;
            float length = direction.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            direction /= length;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            float head = Mathf.Min(1.1f, length * 0.32f);
            DrawLine(end, end - (direction * head) + (side * head * 0.52f), color);
            DrawLine(end, end - (direction * head) - (side * head * 0.52f), color);
        }

        private static void DrawCircle(Vector3 center, float radius, int segments, Color color)
        {
            Vector3 previous = center + new Vector3(radius, 0f, 0f);
            for (int segment = 1; segment <= segments; segment++)
            {
                float angle = segment * (Mathf.PI * 2f / segments);
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                DrawLine(previous, next, color);
                previous = next;
            }
        }

        private static void DrawLine(Vector3 start, Vector3 end, Color color)
        {
            GL.Color(color);
            GL.Vertex(start);
            GL.Vertex(end);
        }

        private static Vector3 ToWorld(FPVector2 value, float height)
        {
            return new Vector3(value.X.Raw * RawToFloat, height, value.Y.Raw * RawToFloat);
        }

        private static Vector3 ToWorldDelta(FPVector2 value, float scale)
        {
            return new Vector3(value.X.Raw * RawToFloat * scale, 0f, value.Y.Raw * RawToFloat * scale);
        }

        private void DrawWorldLabel(FPVector2 world, string text, Color color)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(ToWorld(world, 0.85f));
            if (screen.z <= 0f)
            {
                return;
            }

            _worldLabelStyle.normal.textColor = color;
            GUI.Label(new Rect(screen.x - 54f, Screen.height - screen.y - 14f, 108f, 24f), text, _worldLabelStyle);
        }

        private void EnsureLineMaterial()
        {
            if (_lineMaterial != null)
            {
                return;
            }

            Shader shader = Resources.Load<Shader>("SwarmDiagnosticLines");
            if (shader == null)
            {
                Debug.LogError("[SwarmECS] SwarmDiagnosticLines shader was not found.");
                enabled = false;
                return;
            }

            _lineMaterial = new Material(shader)
            {
                name = "Swarm Technical Lab Lines (Runtime)",
                hideFlags = HideFlags.DontSave,
            };
        }

        private void EnsureWorldLabelStyle()
        {
            if (_worldLabelStyle != null)
            {
                return;
            }

            _worldLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
        }
    }
}

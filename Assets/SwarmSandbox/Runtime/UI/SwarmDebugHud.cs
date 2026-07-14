using System.Text;
using SwarmECS.Simulation;
using UnityEngine;

namespace SwarmECS.Runtime
{
    [RequireComponent(typeof(SwarmSimulationHost))]
    public sealed class SwarmDebugHud : MonoBehaviour
    {
        private readonly StringBuilder _builder = new(1024);
        private SwarmSimulationHost _host;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _goodStyle;
        private string _cachedMetrics = "Initializing deterministic simulation...";
        private float _nextRefresh;

        private void Awake()
        {
            _host = GetComponent<SwarmSimulationHost>();
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (Time.unscaledTime >= _nextRefresh)
            {
                RefreshMetrics();
                _nextRefresh = Time.unscaledTime + 0.25f;
            }

            const float panelWidth = 410f;
            float panelHeight = Mathf.Min(Screen.height - 28f, 590f);
            Rect panel = new(14f, 14f, panelWidth, panelHeight);
            GUI.color = new Color(0.025f, 0.045f, 0.07f, 0.94f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = Color.white;

            GUI.Label(new Rect(30f, 26f, panelWidth - 32f, 34f), "SWARM ECS SANDBOX", _titleStyle);
            GUI.Label(new Rect(30f, 60f, panelWidth - 32f, panelHeight - 150f), _cachedMetrics, _bodyStyle);

            float y = panel.yMax - 78f;
            if (GUI.Button(new Rect(28f, y, 82f, 28f), _host.IsPaused ? "RESUME" : "PAUSE"))
            {
                _host.TogglePause();
            }

            if (GUI.Button(new Rect(116f, y, 82f, 28f), "LATE CMD"))
            {
                _host.InjectLateCorrection();
            }

            if (GUI.Button(new Rect(204f, y, 82f, 28f), "CATCH UP"))
            {
                _host.QueueCatchUp();
            }

            if (GUI.Button(new Rect(292f, y, 82f, 28f), "QUERY MODE"))
            {
                _host.ToggleSpatialIndex();
            }

            GUI.Label(
                new Rect(30f, panel.yMax - 42f, panelWidth - 30f, 24f),
                "SPACE pause | L rollback | T catch-up | K Grid/KD-radius/KNN | R reset",
                _goodStyle);
        }

        private void RefreshMetrics()
        {
            if (_host.World == null || _host.Simulation == null)
            {
                return;
            }

            _builder.Clear();
            _builder.Append("Q16.16 FIXED-POINT  ·  CUSTOM SoA ECS\n");
            _builder.Append("Agents       ").Append(_host.AgentCount.ToString("N0"));
            _builder.Append("    Render FPS  ").Append(_host.MeasuredFps.ToString("F1")).Append('\n');
            _builder.Append("Logic Tick   ").Append(_host.SimulationTick);
            _builder.Append(" @ ").Append(_host.FixedRateHz).Append(" Hz");
            _builder.Append("    CPU/tick ").Append(_host.SimulationMilliseconds.ToString("F2")).Append(" ms\n");
            _builder.Append("Hot-path GC  ").Append(_host.LastAllocatedBytes).Append(" B/tick");
            _builder.Append("    GPU draw  1 indirect batch\n\n");

            _builder.Append("SPATIAL + AVOIDANCE\n");
            _builder.Append("Neighbor     ").Append(GetNeighborModeLabel(_host.World.SpatialIndexMode)).Append('\n');
            _builder.Append("Neighbors    ").Append(_host.Simulation.Avoidance.LastNeighborLinks.ToString("N0"));
            _builder.Append("    ORCA O/A ");
            _builder.Append(_host.Simulation.Avoidance.LastObstacleOrcaLines.ToString("N0")).Append('/');
            _builder.Append(_host.Simulation.Avoidance.LastAgentOrcaLines.ToString("N0")).Append('\n');
            _builder.Append("Avoid BVH q ").Append(_host.Simulation.Avoidance.LastObstacleBroadphaseQueries.ToString("N0"));
            _builder.Append("    collision q/cand ");
            _builder.Append(_host.Simulation.Obstacles.LastBroadphaseQueries.ToString("N0")).Append('/');
            _builder.Append(_host.Simulation.Obstacles.LastBroadphaseCandidates.ToString("N0")).Append('\n');
            _builder.Append("CCD hits     ").Append(_host.Simulation.Obstacles.LastSweepHits.ToString("N0"));
            _builder.Append("    SAT fallback ").Append(_host.Simulation.Obstacles.LastPenetrationRecoveries.ToString("N0")).Append('\n');
            _builder.Append("Residual raw ").Append(_host.Simulation.Obstacles.LastMaxResidualDepth.Raw).Append('\n');
            _builder.Append("Steering A/T ").Append(_host.Simulation.Movement.LastAccelerationLimitedAgents.ToString("N0"));
            _builder.Append('/').Append(_host.Simulation.Movement.LastTurnLimitedAgents.ToString("N0"));
            _builder.Append("    A* waypoints ").Append(_host.Simulation.Navigation.TotalSharedWaypoints).Append('\n');
            _builder.Append("Path req     ").Append(_host.Simulation.Navigation.LastProcessedPathRequests);
            _builder.Append('/').Append(_host.Simulation.Navigation.MaxPathRequestsPerTick);
            _builder.Append("    pending ").Append(_host.Simulation.Navigation.PendingPathRequests).Append('\n');
            _builder.Append("Path cache   ").Append(_host.Simulation.Navigation.CacheHits).Append(" hit / ");
            _builder.Append(_host.Simulation.Navigation.CacheMisses).Append(" miss");
            _builder.Append("    replay A* ").Append(_host.Simulation.Navigation.DerivedAStarRebuilds).Append('\n');
            _builder.Append("Nav islands  ").Append(_host.Simulation.Navigation.Islands.RegionCount);
            _builder.Append("    rejected ").Append(_host.Simulation.Navigation.IslandRejectedRequests).Append("\n\n");

            _builder.Append("DETERMINISTIC NETCODE LAB\n");
            _builder.Append("Twin-world   ").Append(_host.DeterminismProbePassed ? "PASS (raw state identical)" : "FAIL").Append('\n');
            _builder.Append("Config hash  0x").Append(_host.World.Config.ConfigHash.ToString("X16")).Append('\n');
            _builder.Append("State hash   0x").Append(_host.CurrentHash.ToString("X16")).Append('\n');
            _builder.Append("Rollback     ").Append(_host.Rollback.RollbackCount);
            _builder.Append("    last replay ").Append(_host.Rollback.LastResimulatedTicks).Append(" ticks\n");
            _builder.Append("Latency      ").Append(_host.SimulatedLatencyTicks).Append(" ticks");
            _builder.Append("    catch-up backlog ").Append(_host.CatchUpBacklog).Append("\n\n");

            _builder.Append("PIPELINE\n");
            _builder.Append("A* → Grid/KD + obstacle BVH → obstacle/agent ORCA\n");
            _builder.Append("→ bounded steering → swept CCD/slide → SAT fallback → GPU");
            _cachedMetrics = _builder.ToString();
        }

        private static string GetNeighborModeLabel(SpatialIndexMode mode)
        {
            return mode switch
            {
                SpatialIndexMode.UniformGrid => "Uniform Grid radius (bounded top-K)",
                SpatialIndexMode.KdTree => "KD-Tree radius (branch-pruned)",
                SpatialIndexMode.KdTreeKNearest => "KD-Tree exact KNN (bounded K)",
                _ => "Unknown",
            };
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.32f, 0.9f, 1f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = false,
                wordWrap = false,
                normal = { textColor = new Color(0.82f, 0.9f, 0.96f) },
            };
            _goodStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.48f, 0.8f, 0.86f) },
            };
        }
    }
}

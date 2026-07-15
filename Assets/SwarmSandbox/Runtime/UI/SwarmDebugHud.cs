using System.Text;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Pathfinding;
using SwarmECS.Simulation.Systems;
using UnityEngine;

namespace SwarmECS.Runtime
{
    [RequireComponent(typeof(SwarmSimulationHost))]
    public sealed class SwarmDebugHud : MonoBehaviour
    {
        private const float LeftPanelWidth = 438f;
        private const float ContextPanelWidth = 360f;
        private const float LogicBudgetMilliseconds = 1000f / 30f;

        private static readonly string[] ViewLabels =
        {
            "OVERVIEW",
            "NAVIGATION",
            "AVOIDANCE",
            "COLLISION",
            "ROLLBACK",
        };

        private const string OverviewContext =
            "SYSTEM MAP\n\n" +
            "The fixed-point SoA world is authoritative. Unity input, overlays and indirect rendering are presentation-only.\n\n" +
            "<color=#72E6FF>CYAN</color> shared squad routes and goals\n" +
            "<color=#FF8A2A>ORANGE</color> immutable obstacle topology\n\n" +
            "PROOF POINT\nThe overlay reads the same live systems that produce the benchmark, rollback and replay evidence.";

        private const string NavigationContext =
            "NAVIGATION LAB\n\n" +
            "64 x 64 deterministic grid, blurred traversal penalties, connected-region rejection, fixed request budget and shared paths for four squads.\n\n" +
            "<color=#2ECCE8>GRID</color> walkable topology\n" +
            "<color=#FF4B42>RED CROSSES</color> blocked cells\n" +
            "<color=#72E6FF>COLORED ROUTES</color> shared A* output\n\n" +
            "Queue the blocked target to place Group 0 inside the central obstacle and observe immediate rejection without an exhaustive search.";

        private const string AvoidanceContext =
            "AVOIDANCE LAB\n\n" +
            "One real Agent is sampled from the active spatial index. Its exact neighbors and reconstructed ORCA constraints are drawn without changing world state.\n\n" +
            "<color=#39C9FF>LINKS / BLUE</color> Agent constraints\n" +
            "<color=#FF6A29>ORANGE</color> obstacle constraints\n" +
            "<color=#FFE329>YELLOW</color> preferred velocity\n" +
            "<color=#28FFD3>CYAN</color> solved safe velocity";

        private const string CollisionContext =
            "COLLISION LAB\n\n" +
            "The static-obstacle BVH, a deterministic swept-circle probe and recent live CCD contacts are visible together.\n\n" +
            "<color=#B84CFF>PURPLE</color> BVH internal bounds\n" +
            "<color=#FF3C9D>PINK</color> BVH leaves\n" +
            "<color=#FF3A32>RED</color> requested sweep\n" +
            "<color=#FFAD24>ORANGE</color> time of impact\n" +
            "<color=#22FFD0>CYAN</color> tangent slide\n" +
            "<color=#FFF05E>YELLOW</color> contact normal";

        private const string RollbackContext =
            "ROLLBACK LAB\n\n" +
            "Inject a command stamped 18 ticks in the past. The controller restores a snapshot, inserts the ordered command and resimulates to the present.\n\n" +
            "<color=#FF3DB8>MAGENTA</color> predicted positions before correction\n" +
            "<color=#24F2FF>CYAN</color> corrected positions after replay\n\n" +
            "The hashes and sampled ghost links are diagnostics; the authoritative result remains the fixed-point world after replay.";

        private readonly StringBuilder _builder = new(1536);
        private SwarmSimulationHost _host;
        private SwarmTechnicalOverlayRenderer _overlay;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _contextStyle;
        private GUIStyle _footerStyle;
        private string _cachedMetrics = "Initializing deterministic simulation...";
        private float _nextRefresh;

        public SwarmLabView ActiveView { get; private set; } = SwarmLabView.Overview;

        private void Awake()
        {
            _host = GetComponent<SwarmSimulationHost>();
            _overlay = GetComponent<SwarmTechnicalOverlayRenderer>();
        }

        private void Start()
        {
            _overlay ??= GetComponent<SwarmTechnicalOverlayRenderer>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                SetActiveView(SwarmLabView.Overview);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                SetActiveView(SwarmLabView.Navigation);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                SetActiveView(SwarmLabView.Avoidance);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SetActiveView(SwarmLabView.Collision);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                SetActiveView(SwarmLabView.Rollback);
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            if (Time.unscaledTime >= _nextRefresh)
            {
                RefreshMetrics();
                _nextRefresh = Time.unscaledTime + 0.20f;
            }

            float panelHeight = Mathf.Min(Screen.height - 28f, 720f);
            Rect panel = new(14f, 14f, LeftPanelWidth, panelHeight);
            DrawPanelBackground(panel);

            GUI.Label(new Rect(30f, 25f, LeftPanelWidth - 34f, 30f), "SWARM ECS TECHNICAL LAB", _titleStyle);
            GUI.Label(
                new Rect(30f, 53f, LeftPanelWidth - 34f, 22f),
                "10,000-AGENT DETERMINISTIC SIMULATION · v0.3.1 LAB",
                _subtitleStyle);

            DrawViewTabs(panel);
            GUI.Label(
                new Rect(30f, 116f, LeftPanelWidth - 34f, panelHeight - 218f),
                _cachedMetrics,
                _bodyStyle);
            DrawPrimaryControls(panel);

            GUI.Label(
                new Rect(30f, panel.yMax - 34f, LeftPanelWidth - 32f, 20f),
                "1-5 lab views | SPACE pause | L rollback | T catch-up | K query | R reset",
                _footerStyle);

            if (Screen.width >= 980)
            {
                DrawContextPanel(panelHeight);
            }
        }

        public void SetActiveView(SwarmLabView view)
        {
            if ((uint)view > (uint)SwarmLabView.Rollback)
            {
                return;
            }

            ActiveView = view;
            _nextRefresh = 0f;
        }

        private void DrawViewTabs(Rect panel)
        {
            const float buttonWidth = 76f;
            const float gap = 4f;
            float x = 30f;
            Color previousBackground = GUI.backgroundColor;
            for (int index = 0; index < ViewLabels.Length; index++)
            {
                bool selected = index == (int)ActiveView;
                GUI.backgroundColor = selected
                    ? new Color(0.12f, 0.72f, 0.86f, 1f)
                    : new Color(0.18f, 0.24f, 0.31f, 1f);
                if (GUI.Button(new Rect(x, 80f, buttonWidth, 27f), ViewLabels[index]))
                {
                    SetActiveView((SwarmLabView)index);
                }

                x += buttonWidth + gap;
            }

            GUI.backgroundColor = previousBackground;
        }

        private void DrawPrimaryControls(Rect panel)
        {
            const float width = 76f;
            const float gap = 4f;
            float y = panel.yMax - 72f;
            float x = 30f;
            if (GUI.Button(new Rect(x, y, width, 28f), _host.IsPaused ? "RESUME" : "PAUSE"))
            {
                _host.TogglePause();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), "LATE CMD"))
            {
                _host.InjectLateCorrection();
                SetActiveView(SwarmLabView.Rollback);
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), "CATCH UP"))
            {
                _host.QueueCatchUp();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), "QUERY"))
            {
                _host.ToggleSpatialIndex();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), "RESET"))
            {
                _host.ResetSimulation();
            }
        }

        private void DrawContextPanel(float maximumHeight)
        {
            float height = Mathf.Min(maximumHeight, 526f);
            Rect panel = new(Screen.width - ContextPanelWidth - 14f, 14f, ContextPanelWidth, height);
            DrawPanelBackground(panel);
            GUI.Label(new Rect(panel.x + 18f, 25f, panel.width - 36f, 28f), "WHAT YOU ARE SEEING", _titleStyle);
            GUI.Label(
                new Rect(panel.x + 18f, 62f, panel.width - 36f, panel.height - 162f),
                GetContextText(),
                _contextStyle);

            float actionY = panel.yMax - 86f;
            if (GUI.Button(
                new Rect(panel.x + 18f, actionY, panel.width - 36f, 28f),
                GetContextActionLabel()))
            {
                ExecuteContextAction();
            }

            string overlayLabel = _overlay == null || _overlay.OverlaysEnabled
                ? "WORLD OVERLAY: ON"
                : "WORLD OVERLAY: OFF";
            if (GUI.Button(new Rect(panel.x + 18f, panel.yMax - 48f, panel.width - 36f, 28f), overlayLabel) &&
                _overlay != null)
            {
                _overlay.OverlaysEnabled = !_overlay.OverlaysEnabled;
            }
        }

        private void ExecuteContextAction()
        {
            switch (ActiveView)
            {
                case SwarmLabView.Navigation:
                    _host.QueueBlockedNavigationProbe();
                    break;
                case SwarmLabView.Avoidance:
                    _overlay?.CycleSampleAgent();
                    break;
                case SwarmLabView.Collision:
                    _overlay?.ClearCollisionTraces();
                    break;
                case SwarmLabView.Rollback:
                    _host.InjectLateCorrection();
                    break;
                default:
                    _host.ToggleSpatialIndex();
                    break;
            }
        }

        private string GetContextActionLabel()
        {
            return ActiveView switch
            {
                SwarmLabView.Navigation => "QUEUE BLOCKED TARGET",
                SwarmLabView.Avoidance => "SAMPLE NEXT GROUP",
                SwarmLabView.Collision => "CLEAR LIVE CCD TRACES",
                SwarmLabView.Rollback => "INJECT 18-TICK LATE COMMAND",
                _ => "CYCLE SPATIAL QUERY MODE",
            };
        }

        private string GetContextText()
        {
            return ActiveView switch
            {
                SwarmLabView.Navigation => NavigationContext,
                SwarmLabView.Avoidance => AvoidanceContext,
                SwarmLabView.Collision => CollisionContext,
                SwarmLabView.Rollback => RollbackContext,
                _ => OverviewContext,
            };
        }

        private void RefreshMetrics()
        {
            if (_host.World == null || _host.Simulation == null)
            {
                return;
            }

            _builder.Clear();
            switch (ActiveView)
            {
                case SwarmLabView.Navigation:
                    AppendNavigationMetrics();
                    break;
                case SwarmLabView.Avoidance:
                    AppendAvoidanceMetrics();
                    break;
                case SwarmLabView.Collision:
                    AppendCollisionMetrics();
                    break;
                case SwarmLabView.Rollback:
                    AppendRollbackMetrics();
                    break;
                default:
                    AppendOverviewMetrics();
                    break;
            }

            _cachedMetrics = _builder.ToString();
        }

        private void AppendOverviewMetrics()
        {
            _builder.Append("LIVE PERFORMANCE\n");
            _builder.Append("Agents          ").Append(_host.AgentCount.ToString("N0"));
            _builder.Append("     Render FPS  ").Append(_host.MeasuredFps.ToString("F1")).Append('\n');
            _builder.Append("Logic tick      ").Append(_host.SimulationTick).Append(" @ ").Append(_host.FixedRateHz).Append(" Hz\n");
            _builder.Append("CPU / tick      ").Append(_host.SimulationMilliseconds.ToString("F2")).Append(" ms   budget ");
            _builder.Append(LogicBudgetMilliseconds.ToString("F2")).Append(" ms   ");
            _builder.Append(_host.SimulationMilliseconds <= LogicBudgetMilliseconds ? "OK" : "OVER").Append('\n');
            _builder.Append("Hot-path GC     ").Append(_host.LastAllocatedBytes).Append(" B/tick (caller thread)\n");
            _builder.Append("Agent render    1 indirect command\n\n");

            _builder.Append("PIPELINE HEALTH\n");
            _builder.Append("Spatial         ").Append(GetNeighborModeLabel(_host.World.SpatialIndexMode)).Append('\n');
            _builder.Append("Neighbor links  ").Append(_host.Simulation.Avoidance.LastNeighborLinks.ToString("N0")).Append('\n');
            _builder.Append("ORCA obstacle / agent  ")
                .Append(_host.Simulation.Avoidance.LastObstacleOrcaLines.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Avoidance.LastAgentOrcaLines.ToString("N0")).Append('\n');
            _builder.Append("CCD / SAT / residual   ")
                .Append(_host.Simulation.Obstacles.LastSweepHits).Append(" / ")
                .Append(_host.Simulation.Obstacles.LastPenetrationRecoveries).Append(" / ")
                .Append(_host.Simulation.Obstacles.LastMaxResidualDepth.Raw).Append(" raw\n");
            _builder.Append("A/T limited     ")
                .Append(_host.Simulation.Movement.LastAccelerationLimitedAgents.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Movement.LastTurnLimitedAgents.ToString("N0")).Append("\n\n");

            _builder.Append("DETERMINISM\n");
            _builder.Append("Twin-world      ").Append(_host.DeterminismProbePassed ? "PASS" : "FAIL").Append('\n');
            _builder.Append("Config hash     0x").Append(_host.World.Config.ConfigHash.ToString("X16")).Append('\n');
            _builder.Append("State hash      0x").Append(_host.CurrentHash.ToString("X16")).Append('\n');
            _builder.Append("Rollback / replay ticks  ")
                .Append(_host.Rollback.RollbackCount).Append(" / ")
                .Append(_host.Rollback.LastResimulatedTicks).Append('\n');
        }

        private void AppendNavigationMetrics()
        {
            var navigation = _host.Simulation.Navigation;
            GridMap map = navigation.Map;
            int active = 0;
            int unreachable = 0;
            for (int group = 0; group < SwarmWorld.GroupCount; group++)
            {
                GroupPathStatus status = _host.World.GroupPathStates[group].Status;
                active += status == GroupPathStatus.Active ? 1 : 0;
                unreachable += status == GroupPathStatus.Unreachable ? 1 : 0;
            }

            _builder.Append("GRID + CONNECTIVITY\n");
            _builder.Append("Dimensions      ").Append(map.Width).Append(" x ").Append(map.Height);
            _builder.Append("     nodes ").Append(map.NodeCount.ToString("N0")).Append('\n');
            _builder.Append("Cell size       ").Append(map.CellSize.ToDouble().ToString("F2")).Append(" world units\n");
            _builder.Append("Map revision    ").Append(map.Revision).Append('\n');
            _builder.Append("Connected regions  ").Append(navigation.Islands.RegionCount).Append('\n');
            _builder.Append("Groups active / unreachable  ").Append(active).Append(" / ").Append(unreachable).Append("\n\n");

            _builder.Append("BUDGETED SHARED A*\n");
            _builder.Append("Shared waypoints  ").Append(navigation.TotalSharedWaypoints).Append('\n');
            _builder.Append("Requests this tick / budget  ")
                .Append(navigation.LastProcessedPathRequests).Append(" / ")
                .Append(navigation.MaxPathRequestsPerTick).Append('\n');
            _builder.Append("Pending requests  ").Append(navigation.PendingPathRequests).Append('\n');
            _builder.Append("Cache hit / miss   ").Append(navigation.CacheHits).Append(" / ").Append(navigation.CacheMisses).Append('\n');
            _builder.Append("Cache capacity     ").Append(navigation.PathCacheCapacity).Append('\n');
            _builder.Append("Island rejected    ").Append(navigation.IslandRejectedRequests).Append('\n');
            _builder.Append("Rollback cache restore / A* rebuild  ")
                .Append(navigation.DerivedCacheRestores).Append(" / ")
                .Append(navigation.DerivedAStarRebuilds).Append('\n');
        }

        private void AppendAvoidanceMetrics()
        {
            var avoidance = _host.Simulation.Avoidance;
            double averageNeighbors = _host.AgentCount == 0
                ? 0d
                : (double)avoidance.LastNeighborLinks / _host.AgentCount;
            _builder.Append("SPATIAL QUERY\n");
            _builder.Append("Mode            ").Append(GetNeighborModeLabel(_host.World.SpatialIndexMode)).Append('\n');
            _builder.Append("Radius / max K  ")
                .Append(_host.World.Config.NeighborDistance.ToDouble().ToString("F2")).Append(" / ")
                .Append(_host.World.Config.MaxNeighbors).Append('\n');
            _builder.Append("Links total / average  ")
                .Append(avoidance.LastNeighborLinks.ToString("N0")).Append(" / ")
                .Append(averageNeighbors.ToString("F2")).Append('\n');
            _builder.Append("Background workers  ").Append(avoidance.BackgroundWorkerCount).Append("\n\n");

            _builder.Append("ORCA CONSTRAINTS\n");
            _builder.Append("Obstacle lines   ").Append(avoidance.LastObstacleOrcaLines.ToString("N0")).Append('\n');
            _builder.Append("Agent lines      ").Append(avoidance.LastAgentOrcaLines.ToString("N0")).Append('\n');
            _builder.Append("Obstacle BVH queries  ").Append(avoidance.LastObstacleBroadphaseQueries.ToString("N0")).Append('\n');
            _builder.Append("Sample Agent     #").Append(_overlay?.SelectedAgentId ?? -1).Append('\n');
            _builder.Append("Sample neighbors ").Append(_overlay?.DiagnosticNeighborCount ?? 0).Append('\n');
            _builder.Append("Sample ORCA O/A  ")
                .Append(_overlay?.DiagnosticObstacleLineCount ?? 0).Append(" / ")
                .Append((_overlay?.DiagnosticLineCount ?? 0) - (_overlay?.DiagnosticObstacleLineCount ?? 0)).Append('\n');
            _builder.Append("A/T limited      ")
                .Append(_host.Simulation.Movement.LastAccelerationLimitedAgents.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Movement.LastTurnLimitedAgents.ToString("N0")).Append('\n');
        }

        private void AppendCollisionMetrics()
        {
            StaticObstacleCollisionSystem collision = _host.Simulation.Obstacles;
            _builder.Append("STATIC GEOMETRY\n");
            _builder.Append("OBBs / directed edges  ").Append(collision.ObstacleCount).Append(" / ").Append(collision.ObstacleSegmentCount).Append('\n');
            _builder.Append("BVH nodes       ").Append(collision.Broadphase.NodeCount).Append('\n');
            _builder.Append("Broadphase q / candidates  ")
                .Append(collision.LastBroadphaseQueries.ToString("N0")).Append(" / ")
                .Append(collision.LastBroadphaseCandidates.ToString("N0")).Append("\n\n");

            _builder.Append("CONTINUOUS SAFETY\n");
            _builder.Append("CCD hits this tick  ").Append(collision.LastSweepHits.ToString("N0")).Append('\n');
            _builder.Append("Live retained traces ").Append(_overlay?.RecentCollisionTraceCount ?? 0).Append('\n');
            _builder.Append("SAT fallback       ").Append(collision.LastPenetrationRecoveries.ToString("N0")).Append('\n');
            _builder.Append("Residual depth     ").Append(collision.LastMaxResidualDepth.Raw).Append(" raw\n");
            _builder.Append("Sweep / recovery budget  ")
                .Append(StaticObstacleCollisionSystem.MaxSweepIterations).Append(" / ")
                .Append(StaticObstacleCollisionSystem.MaxPenetrationPasses).Append('\n');
            _builder.Append("Diagnostic probe   ")
                .Append(_overlay != null && _overlay.CollisionProbeHit ? "HIT" : "MISS")
                .Append("  TOI raw ").Append(_overlay?.CollisionProbeFractionRaw ?? 0).Append('\n');
            _builder.Append("Pipeline            ORCA -> limiter -> CCD/slide -> SAT\n");
        }

        private void AppendRollbackMetrics()
        {
            int latencyMilliseconds = (_host.SimulatedLatencyTicks * 1000) / _host.FixedRateHz;
            _builder.Append("ROLLBACK WINDOW\n");
            _builder.Append("Current tick      ").Append(_host.SimulationTick).Append('\n');
            _builder.Append("Simulated latency ").Append(_host.SimulatedLatencyTicks).Append(" ticks / ")
                .Append(latencyMilliseconds).Append(" ms\n");
            _builder.Append("History length    ").Append(_host.Rollback.HistoryLength).Append(" ticks\n");
            _builder.Append("Retained commands ").Append(_host.Rollback.CommandCount).Append("\n\n");

            _builder.Append("REPLAY RESULT\n");
            _builder.Append("Rollback count    ").Append(_host.Rollback.RollbackCount).Append('\n');
            _builder.Append("Last / total resimulated  ")
                .Append(_host.Rollback.LastResimulatedTicks).Append(" / ")
                .Append(_host.Rollback.TotalResimulatedTicks).Append(" ticks\n");
            _builder.Append("Replay range      ").Append(_host.RollbackGhostOriginTick).Append(" -> ")
                .Append(_host.RollbackGhostDestinationTick).Append('\n');
            _builder.Append("Ghost samples     ").Append(_host.RollbackGhostCount);
            _builder.Append("     group ").Append(_host.RollbackGhostGroup).Append('\n');
            _builder.Append("Hash before       0x").Append(_host.Rollback.LastHashBeforeRollback.ToString("X16")).Append('\n');
            _builder.Append("Hash after        0x").Append(_host.Rollback.LastHashAfterRollback.ToString("X16")).Append('\n');
            _builder.Append("Catch-up backlog  ").Append(_host.CatchUpBacklog).Append(" ticks\n");
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

        private static void DrawPanelBackground(Rect panel)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.018f, 0.035f, 0.055f, 0.95f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = previous;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.30f, 0.91f, 1f) },
            };
            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.48f, 0.68f, 0.78f) },
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = false,
                wordWrap = false,
                normal = { textColor = new Color(0.82f, 0.90f, 0.96f) },
            };
            _contextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.80f, 0.89f, 0.95f) },
            };
            _footerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.40f, 0.73f, 0.80f) },
            };
        }
    }
}

using System.Text;
using SwarmECS.Simulation;
using SwarmECS.Simulation.Netcode.Transport;
using SwarmECS.Simulation.Pathfinding;
using SwarmECS.Simulation.Systems;
using UnityEngine;

namespace SwarmECS.Runtime
{
    [RequireComponent(typeof(SwarmSimulationHost))]
    public sealed class SwarmDebugHud : MonoBehaviour
    {
        private const float LeftPanelWidth = 518f;
        private const float ContextPanelWidth = 360f;
        private const float LogicBudgetMilliseconds = 1000f / 30f;

        private const string OverviewContextEnglish =
            "SYSTEM MAP\n\n" +
            "The fixed-point SoA world is authoritative. Unity input, overlays and indirect rendering are presentation-only.\n\n" +
            "<color=#72E6FF>CYAN</color> shared squad routes and goals\n" +
            "<color=#FF8A2A>ORANGE</color> immutable obstacle topology\n\n" +
            "PROOF POINT\nThe overlay reads the same live systems that produce the benchmark, rollback and replay evidence.";

        private const string OverviewContextChinese =
            "系统全景\n\n" +
            "定点数 SoA World 是唯一权威状态。Unity 输入、覆盖层和间接渲染只属于表现层。\n\n" +
            "<color=#72E6FF>青色</color> 群组共享路径与目标\n" +
            "<color=#FF8A2A>橙色</color> 不可变障碍拓扑\n\n" +
            "验证要点\n覆盖层直接读取生成基准、回滚和 Replay 证据的同一组实时系统。";

        private const string NavigationContextEnglish =
            "NAVIGATION LAB\n\n" +
            "64 x 64 deterministic grid, blurred traversal penalties, connected-region rejection, fixed request budget and shared paths for four squads.\n\n" +
            "<color=#2ECCE8>GRID</color> walkable topology\n" +
            "<color=#FF4B42>RED CROSSES</color> blocked cells\n" +
            "<color=#72E6FF>COLORED ROUTES</color> shared A* output\n\n" +
            "Queue the blocked target to place Group 0 inside the central obstacle and observe immediate rejection without an exhaustive search.";

        private const string NavigationContextChinese =
            "导航实验室\n\n" +
            "64 × 64 确定性网格，包含模糊通行代价、连通区域拒绝、固定请求预算，以及四个群组共享路径。\n\n" +
            "<color=#2ECCE8>网格</color> 可行走拓扑\n" +
            "<color=#FF4B42>红叉</color> 阻塞节点\n" +
            "<color=#72E6FF>彩色路径</color> 共享 A* 输出\n\n" +
            "点击“加入阻塞目标”，把 0 组目标放入中心障碍，观察系统在不穷举搜索的情况下立即拒绝请求。";

        private const string AvoidanceContextEnglish =
            "AVOIDANCE LAB\n\n" +
            "One real Agent is sampled from the active spatial index. Its exact neighbors and reconstructed ORCA constraints are drawn without changing world state.\n\n" +
            "<color=#39C9FF>LINKS / BLUE</color> Agent constraints\n" +
            "<color=#FF6A29>ORANGE</color> obstacle constraints\n" +
            "<color=#FFE329>YELLOW</color> preferred velocity\n" +
            "<color=#28FFD3>CYAN</color> solved safe velocity";

        private const string AvoidanceContextChinese =
            "避障实验室\n\n" +
            "从当前空间索引中采样一个真实 Agent，在不改变 World 状态的前提下绘制其精确邻居与重建的 ORCA 约束。\n\n" +
            "<color=#39C9FF>蓝色连线</color> Agent 约束\n" +
            "<color=#FF6A29>橙色</color> 障碍约束\n" +
            "<color=#FFE329>黄色</color> 期望速度\n" +
            "<color=#28FFD3>青色</color> 求解后的安全速度";

        private const string CollisionContextEnglish =
            "COLLISION LAB\n\n" +
            "The static-obstacle BVH, a deterministic swept-circle probe and recent live CCD contacts are visible together.\n\n" +
            "<color=#B84CFF>PURPLE</color> BVH internal bounds\n" +
            "<color=#FF3C9D>PINK</color> BVH leaves\n" +
            "<color=#FF3A32>RED</color> requested sweep\n" +
            "<color=#FFAD24>ORANGE</color> time of impact\n" +
            "<color=#22FFD0>CYAN</color> tangent slide\n" +
            "<color=#FFF05E>YELLOW</color> contact normal";

        private const string CollisionContextChinese =
            "碰撞实验室\n\n" +
            "同时显示静态障碍 BVH、确定性扫掠圆探针和近期真实 CCD 接触。\n\n" +
            "<color=#B84CFF>紫色</color> BVH 内部包围盒\n" +
            "<color=#FF3C9D>粉色</color> BVH 叶节点\n" +
            "<color=#FF3A32>红色</color> 请求扫掠路径\n" +
            "<color=#FFAD24>橙色</color> 碰撞时刻\n" +
            "<color=#22FFD0>青色</color> 切向滑动\n" +
            "<color=#FFF05E>黄色</color> 接触法线";

        private const string RollbackContextEnglish =
            "ROLLBACK LAB\n\n" +
            "Inject a command stamped 18 ticks in the past. The controller restores a snapshot, inserts the ordered command and resimulates to the present.\n\n" +
            "<color=#FF3DB8>MAGENTA</color> predicted positions before correction\n" +
            "<color=#24F2FF>CYAN</color> corrected positions after replay\n\n" +
            "The hashes and sampled ghost links are diagnostics; the authoritative result remains the fixed-point world after replay.";

        private const string RollbackContextChinese =
            "回滚实验室\n\n" +
            "注入一条时间戳落后 18 tick 的命令。控制器恢复快照、插入有序命令，并重新模拟到当前帧。\n\n" +
            "<color=#FF3DB8>洋红</color> 修正前的预测位置\n" +
            "<color=#24F2FF>青色</color> 重演后的修正位置\n\n" +
            "哈希与采样残影只用于诊断；权威结果始终是重演完成后的定点数 World。";

        private const string NetworkContextEnglish =
            "AUTHORITATIVE UDP LAB\n\n" +
            "The normal interactive scene runs one local World. The v0.4 qualification launches one headless authority and two predictive clients as separate Player processes over real UDP.\n\n" +
            "<color=#72E6FF>RELIABLE</color> handshake and ordered commands\n" +
            "<color=#FFE329>UNRELIABLE</color> authority hash telemetry\n" +
            "<color=#FF3DB8>ROLLBACK</color> speculative state correction\n\n" +
            "Run Scripts/run-authoritative-udp-session.sh to reproduce the tracked three-process evidence.";

        private const string NetworkContextChinese =
            "权威 UDP 实验室\n\n" +
            "普通交互场景运行一个本地 World。v0.4 验证会通过真实 UDP 启动一个无头权威端和两个独立预测客户端 Player 进程。\n\n" +
            "<color=#72E6FF>可靠通道</color> 握手与有序命令\n" +
            "<color=#FFE329>非可靠通道</color> 权威哈希遥测\n" +
            "<color=#FF3DB8>回滚</color> 预测状态修正\n\n" +
            "运行 Scripts/run-authoritative-udp-session.sh，可复现仓库记录的三进程证据。";

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

        public SwarmUiLanguage Language { get; private set; } = SwarmUiLanguage.English;

        public bool IsChinese => Language == SwarmUiLanguage.SimplifiedChinese;

        private void Awake()
        {
            _host = GetComponent<SwarmSimulationHost>();
            _overlay = GetComponent<SwarmTechnicalOverlayRenderer>();
            Language = SwarmUiLocalization.LoadLanguage();
            _cachedMetrics = T("Initializing deterministic simulation...", "正在初始化确定性仿真……");
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
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                SetActiveView(SwarmLabView.Network);
            }
            else if (Input.GetKeyDown(KeyCode.F1))
            {
                ToggleLanguage();
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

            GUI.Label(
                new Rect(30f, 25f, LeftPanelWidth - 112f, 30f),
                T("SWARM ECS TECHNICAL LAB", "SWARM ECS 技术实验室"),
                _titleStyle);
            if (GUI.Button(
                new Rect(panel.xMax - 78f, 25f, 58f, 25f),
                IsChinese ? "EN" : "中文"))
            {
                ToggleLanguage();
            }

            GUI.Label(
                new Rect(30f, 53f, LeftPanelWidth - 34f, 22f),
                T(
                    "10,000-AGENT DETERMINISTIC SIMULATION · v" + Application.version,
                    "10,000 单位确定性仿真 · v" + Application.version),
                _subtitleStyle);

            DrawViewTabs(panel);
            GUI.Label(
                new Rect(30f, 116f, LeftPanelWidth - 34f, panelHeight - 218f),
                _cachedMetrics,
                _bodyStyle);
            DrawPrimaryControls(panel);

            GUI.Label(
                new Rect(30f, panel.yMax - 34f, LeftPanelWidth - 32f, 20f),
                T(
                    "1-6 views | F1 language | SPACE pause | L rollback | T catch-up | K query | R reset",
                    "1-6 切换页面 | F1 中英文 | 空格 暂停 | L 回滚 | T 追帧 | K 查询 | R 重置"),
                _footerStyle);

            if (Screen.width >= 980)
            {
                DrawContextPanel(panelHeight);
            }
        }

        public void SetActiveView(SwarmLabView view)
        {
            if ((uint)view > (uint)SwarmLabView.Network)
            {
                return;
            }

            ActiveView = view;
            _nextRefresh = 0f;
        }

        public void SetLanguage(SwarmUiLanguage language, bool persist = true)
        {
            if (language != SwarmUiLanguage.English && language != SwarmUiLanguage.SimplifiedChinese)
            {
                language = SwarmUiLanguage.English;
            }

            Language = language;
            if (persist)
            {
                SwarmUiLocalization.SaveLanguage(language);
            }

            _cachedMetrics = T("Initializing deterministic simulation...", "正在初始化确定性仿真……");
            _nextRefresh = 0f;
        }

        public void ToggleLanguage()
        {
            SetLanguage(IsChinese ? SwarmUiLanguage.English : SwarmUiLanguage.SimplifiedChinese);
        }

        private void DrawViewTabs(Rect panel)
        {
            const float buttonWidth = 76f;
            const float gap = 4f;
            float x = 30f;
            Color previousBackground = GUI.backgroundColor;
            for (int index = 0; index <= (int)SwarmLabView.Network; index++)
            {
                bool selected = index == (int)ActiveView;
                GUI.backgroundColor = selected
                    ? new Color(0.12f, 0.72f, 0.86f, 1f)
                    : new Color(0.18f, 0.24f, 0.31f, 1f);
                if (GUI.Button(
                    new Rect(x, 80f, buttonWidth, 27f),
                    SwarmUiLocalization.GetViewLabel(Language, index)))
                {
                    SetActiveView((SwarmLabView)index);
                }

                x += buttonWidth + gap;
            }

            GUI.backgroundColor = previousBackground;
        }

        private void DrawPrimaryControls(Rect panel)
        {
            const float width = 88f;
            const float gap = 4f;
            float y = panel.yMax - 72f;
            float x = 30f;
            if (GUI.Button(
                new Rect(x, y, width, 28f),
                _host.IsPaused ? T("RESUME", "继续") : T("PAUSE", "暂停")))
            {
                _host.TogglePause();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), T("LATE CMD", "迟到命令")))
            {
                _host.InjectLateCorrection();
                SetActiveView(SwarmLabView.Rollback);
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), T("CATCH UP", "追帧")))
            {
                _host.QueueCatchUp();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), T("QUERY", "查询模式")))
            {
                _host.ToggleSpatialIndex();
            }

            x += width + gap;
            if (GUI.Button(new Rect(x, y, width, 28f), T("RESET", "重置")))
            {
                _host.ResetSimulation();
            }
        }

        private void DrawContextPanel(float maximumHeight)
        {
            float height = Mathf.Min(maximumHeight, 526f);
            Rect panel = new(Screen.width - ContextPanelWidth - 14f, 14f, ContextPanelWidth, height);
            DrawPanelBackground(panel);
            GUI.Label(
                new Rect(panel.x + 18f, 25f, panel.width - 36f, 28f),
                T("WHAT YOU ARE SEEING", "当前视图说明"),
                _titleStyle);
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
                ? T("WORLD OVERLAY: ON", "世界覆盖层：开启")
                : T("WORLD OVERLAY: OFF", "世界覆盖层：关闭");
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
                case SwarmLabView.Network:
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
                SwarmLabView.Navigation => T("QUEUE BLOCKED TARGET", "加入阻塞目标"),
                SwarmLabView.Avoidance => T("SAMPLE NEXT GROUP", "采样下一群组"),
                SwarmLabView.Collision => T("CLEAR LIVE CCD TRACES", "清除实时 CCD 轨迹"),
                SwarmLabView.Rollback => T("INJECT 18-TICK LATE COMMAND", "注入迟到 18 TICK 的命令"),
                SwarmLabView.Network => T("EXERCISE SHARED ROLLBACK CORE", "触发共享回滚核心"),
                _ => T("CYCLE SPATIAL QUERY MODE", "切换空间查询模式"),
            };
        }

        private string GetContextText()
        {
            return ActiveView switch
            {
                SwarmLabView.Navigation => T(NavigationContextEnglish, NavigationContextChinese),
                SwarmLabView.Avoidance => T(AvoidanceContextEnglish, AvoidanceContextChinese),
                SwarmLabView.Collision => T(CollisionContextEnglish, CollisionContextChinese),
                SwarmLabView.Rollback => T(RollbackContextEnglish, RollbackContextChinese),
                SwarmLabView.Network => T(NetworkContextEnglish, NetworkContextChinese),
                _ => T(OverviewContextEnglish, OverviewContextChinese),
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
                case SwarmLabView.Network:
                    AppendNetworkMetrics();
                    break;
                default:
                    AppendOverviewMetrics();
                    break;
            }

            _cachedMetrics = _builder.ToString();
        }

        private void AppendOverviewMetrics()
        {
            _builder.Append(T("LIVE PERFORMANCE\n", "实时性能\n"));
            _builder.Append(T("Agents          ", "单位数量        ")).Append(_host.AgentCount.ToString("N0"));
            _builder.Append(T("     Render FPS  ", "     渲染 FPS  ")).Append(_host.MeasuredFps.ToString("F1")).Append('\n');
            _builder.Append(T("Logic tick      ", "逻辑帧          ")).Append(_host.SimulationTick).Append(" @ ").Append(_host.FixedRateHz).Append(" Hz\n");
            _builder.Append(T("CPU / tick      ", "单帧 CPU        ")).Append(_host.SimulationMilliseconds.ToString("F2"))
                .Append(T(" ms   budget ", " ms   预算 "));
            _builder.Append(LogicBudgetMilliseconds.ToString("F2")).Append(" ms   ");
            _builder.Append(_host.SimulationMilliseconds <= LogicBudgetMilliseconds
                ? T("OK", "正常")
                : T("OVER", "超预算")).Append('\n');
            _builder.Append(T("Hot-path GC     ", "热路径 GC       ")).Append(_host.LastAllocatedBytes)
                .Append(T(" B/tick (caller thread)\n", " B/tick（调用线程）\n"));
            _builder.Append(T("Agent render    1 indirect command\n\n", "单位渲染        1 条间接绘制命令\n\n"));

            _builder.Append(T("PIPELINE HEALTH\n", "管线状态\n"));
            _builder.Append(T("Spatial         ", "空间索引        ")).Append(GetNeighborModeLabel(_host.World.SpatialIndexMode)).Append('\n');
            _builder.Append(T("Neighbor links  ", "邻居连接        ")).Append(_host.Simulation.Avoidance.LastNeighborLinks.ToString("N0")).Append('\n');
            _builder.Append(T("ORCA obstacle / agent  ", "ORCA 障碍 / 单位       "))
                .Append(_host.Simulation.Avoidance.LastObstacleOrcaLines.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Avoidance.LastAgentOrcaLines.ToString("N0")).Append('\n');
            _builder.Append(T("CCD / SAT / residual   ", "CCD / SAT / 残余深度   "))
                .Append(_host.Simulation.Obstacles.LastSweepHits).Append(" / ")
                .Append(_host.Simulation.Obstacles.LastPenetrationRecoveries).Append(" / ")
                .Append(_host.Simulation.Obstacles.LastMaxResidualDepth.Raw).Append(T(" raw\n", " raw\n"));
            _builder.Append(T("A/T limited     ", "加速/转向限幅   "))
                .Append(_host.Simulation.Movement.LastAccelerationLimitedAgents.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Movement.LastTurnLimitedAgents.ToString("N0")).Append("\n\n");

            _builder.Append(T("DETERMINISM\n", "确定性\n"));
            _builder.Append(T("Twin-world      ", "双世界校验      ")).Append(_host.DeterminismProbePassed
                ? T("PASS", "通过")
                : T("FAIL", "失败")).Append('\n');
            _builder.Append(T("Config hash     0x", "配置哈希        0x")).Append(_host.World.Config.ConfigHash.ToString("X16")).Append('\n');
            _builder.Append(T("State hash      0x", "状态哈希        0x")).Append(_host.CurrentHash.ToString("X16")).Append('\n');
            _builder.Append(T("Rollback / replay ticks  ", "回滚次数 / 重演帧数    "))
                .Append(_host.Rollback.RollbackCount).Append(" / ")
                .Append(_host.Rollback.LastResimulatedTicks).Append('\n');
        }

        private void AppendNetworkMetrics()
        {
            _builder.Append(T("PROCESS TOPOLOGY\n", "进程拓扑\n"));
            _builder.Append(T(
                "Interactive mode  1 local World (presentation lab)\n",
                "交互模式          1 个本地 World（表现层实验室）\n"));
            _builder.Append(T(
                "Qualification     1 authority + 2 predictive clients\n",
                "验证拓扑          1 权威端 + 2 预测客户端\n"));
            _builder.Append(T(
                "Transport         real IPv4 UDP, loopback / LAN scope\n\n",
                "传输              真实 IPv4 UDP，本机 / 局域网范围\n\n"));

            _builder.Append(T("PACKET CONTRACT\n", "数据报契约\n"));
            _builder.Append(T("Protocol / header  ", "协议 / 包头       "))
                .Append(SwarmUdpPacketCodec.ProtocolVersion).Append(" / ")
                .Append(SwarmUdpPacketCodec.HeaderSize).Append(T(" bytes\n", " 字节\n"));
            _builder.Append(T("Max datagram       ", "最大数据报        ")).Append(SwarmUdpPacketCodec.MaxDatagramBytes)
                .Append(T(" bytes\n", " 字节\n"));
            _builder.Append(T(
                "Sequencing         uint32 serial + ACK / 32-bit history\n",
                "序列              uint32 序列号 + ACK / 32 位历史\n"));
            _builder.Append(T("Reliable           handshake + ordered commands\n", "可靠通道          握手 + 有序命令\n"));
            _builder.Append(T(
                "Unreliable         per-tick authority hash telemetry\n",
                "非可靠通道        逐帧权威哈希遥测\n"));
            _builder.Append(T("Integrity          header + payload CRC32\n\n", "完整性            包头 + 负载 CRC32\n\n"));

            _builder.Append(T("PREDICTION / REPAIR\n", "预测 / 修复\n"));
            _builder.Append(T("Default input delay / lead  2 / 6 ticks\n", "默认输入延迟 / 领先       2 / 6 tick\n"));
            _builder.Append(T("Late command       restore + canonical replay\n", "迟到命令          恢复 + 规范重演\n"));
            _builder.Append(T(
                "Expired command    SnapshotRequired (v0.5 repair)\n",
                "过期命令          SnapshotRequired（v0.5 修复）\n"));
            _builder.Append(T("Local rollback count / depth  ", "本地回滚次数 / 深度        "))
                .Append(_host.Rollback.RollbackCount).Append(" / ")
                .Append(_host.Rollback.LastResimulatedTicks).Append(T(" ticks\n\n", " tick\n\n"));

            _builder.Append(T("REPRODUCTION\n", "复现入口\n"));
            _builder.Append("Scripts/run-authoritative-udp-session.sh\n");
            _builder.Append("NetworkResults/latest/{server,client-1,client-2}.json\n");
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

            _builder.Append(T("GRID + CONNECTIVITY\n", "网格 + 连通性\n"));
            _builder.Append(T("Dimensions      ", "尺寸            ")).Append(map.Width).Append(" x ").Append(map.Height);
            _builder.Append(T("     nodes ", "     节点 ")).Append(map.NodeCount.ToString("N0")).Append('\n');
            _builder.Append(T("Cell size       ", "单元尺寸        ")).Append(map.CellSize.ToDouble().ToString("F2"))
                .Append(T(" world units\n", " 世界单位\n"));
            _builder.Append(T("Map revision    ", "地图修订号      ")).Append(map.Revision).Append('\n');
            _builder.Append(T("Connected regions  ", "连通区域        ")).Append(navigation.Islands.RegionCount).Append('\n');
            _builder.Append(T("Groups active / unreachable  ", "群组 活跃 / 不可达         "))
                .Append(active).Append(" / ").Append(unreachable).Append("\n\n");

            _builder.Append(T("BUDGETED SHARED A*\n", "固定预算共享 A*\n"));
            _builder.Append(T("Shared waypoints  ", "共享路径点      ")).Append(navigation.TotalSharedWaypoints).Append('\n');
            _builder.Append(T("Requests this tick / budget  ", "本帧请求 / 预算           "))
                .Append(navigation.LastProcessedPathRequests).Append(" / ")
                .Append(navigation.MaxPathRequestsPerTick).Append('\n');
            _builder.Append(T("Pending requests  ", "待处理请求      ")).Append(navigation.PendingPathRequests).Append('\n');
            _builder.Append(T("Cache hit / miss   ", "缓存 命中 / 未命中        "))
                .Append(navigation.CacheHits).Append(" / ").Append(navigation.CacheMisses).Append('\n');
            _builder.Append(T("Cache capacity     ", "缓存容量        ")).Append(navigation.PathCacheCapacity).Append('\n');
            _builder.Append(T("Island rejected    ", "岛屿预检拒绝    ")).Append(navigation.IslandRejectedRequests).Append('\n');
            _builder.Append(T("Rollback cache restore / A* rebuild  ", "回滚缓存恢复 / A* 重建              "))
                .Append(navigation.DerivedCacheRestores).Append(" / ")
                .Append(navigation.DerivedAStarRebuilds).Append('\n');
        }

        private void AppendAvoidanceMetrics()
        {
            var avoidance = _host.Simulation.Avoidance;
            double averageNeighbors = _host.AgentCount == 0
                ? 0d
                : (double)avoidance.LastNeighborLinks / _host.AgentCount;
            _builder.Append(T("SPATIAL QUERY\n", "空间查询\n"));
            _builder.Append(T("Mode            ", "模式            ")).Append(GetNeighborModeLabel(_host.World.SpatialIndexMode)).Append('\n');
            _builder.Append(T("Radius / max K  ", "半径 / 最大 K   "))
                .Append(_host.World.Config.NeighborDistance.ToDouble().ToString("F2")).Append(" / ")
                .Append(_host.World.Config.MaxNeighbors).Append('\n');
            _builder.Append(T("Links total / average  ", "连接 总数 / 均值         "))
                .Append(avoidance.LastNeighborLinks.ToString("N0")).Append(" / ")
                .Append(averageNeighbors.ToString("F2")).Append('\n');
            _builder.Append(T("Background workers  ", "后台工作线程    ")).Append(avoidance.BackgroundWorkerCount).Append("\n\n");

            _builder.Append(T("ORCA CONSTRAINTS\n", "ORCA 约束\n"));
            _builder.Append(T("Obstacle lines   ", "障碍约束线      ")).Append(avoidance.LastObstacleOrcaLines.ToString("N0")).Append('\n');
            _builder.Append(T("Agent lines      ", "单位约束线      ")).Append(avoidance.LastAgentOrcaLines.ToString("N0")).Append('\n');
            _builder.Append(T("Obstacle BVH queries  ", "障碍 BVH 查询       "))
                .Append(avoidance.LastObstacleBroadphaseQueries.ToString("N0")).Append('\n');
            _builder.Append(T("Sample Agent     #", "采样单位        #")).Append(_overlay?.SelectedAgentId ?? -1).Append('\n');
            _builder.Append(T("Sample neighbors ", "采样邻居        ")).Append(_overlay?.DiagnosticNeighborCount ?? 0).Append('\n');
            _builder.Append(T("Sample ORCA O/A  ", "采样 ORCA 障碍/单位  "))
                .Append(_overlay?.DiagnosticObstacleLineCount ?? 0).Append(" / ")
                .Append((_overlay?.DiagnosticLineCount ?? 0) - (_overlay?.DiagnosticObstacleLineCount ?? 0)).Append('\n');
            _builder.Append(T("A/T limited      ", "加速/转向限幅   "))
                .Append(_host.Simulation.Movement.LastAccelerationLimitedAgents.ToString("N0")).Append(" / ")
                .Append(_host.Simulation.Movement.LastTurnLimitedAgents.ToString("N0")).Append('\n');
        }

        private void AppendCollisionMetrics()
        {
            StaticObstacleCollisionSystem collision = _host.Simulation.Obstacles;
            _builder.Append(T("STATIC GEOMETRY\n", "静态几何\n"));
            _builder.Append(T("OBBs / directed edges  ", "OBB / 有向边            "))
                .Append(collision.ObstacleCount).Append(" / ").Append(collision.ObstacleSegmentCount).Append('\n');
            _builder.Append(T("BVH nodes       ", "BVH 节点        ")).Append(collision.Broadphase.NodeCount).Append('\n');
            _builder.Append(T("Broadphase q / candidates  ", "Broadphase 查询 / 候选      "))
                .Append(collision.LastBroadphaseQueries.ToString("N0")).Append(" / ")
                .Append(collision.LastBroadphaseCandidates.ToString("N0")).Append("\n\n");

            _builder.Append(T("CONTINUOUS SAFETY\n", "连续碰撞安全\n"));
            _builder.Append(T("CCD hits this tick  ", "本帧 CCD 命中     ")).Append(collision.LastSweepHits.ToString("N0")).Append('\n');
            _builder.Append(T("Live retained traces ", "保留的实时轨迹   ")).Append(_overlay?.RecentCollisionTraceCount ?? 0).Append('\n');
            _builder.Append(T("SAT fallback       ", "SAT 兜底恢复      ")).Append(collision.LastPenetrationRecoveries.ToString("N0")).Append('\n');
            _builder.Append(T("Residual depth     ", "残余深度          ")).Append(collision.LastMaxResidualDepth.Raw).Append(" raw\n");
            _builder.Append(T("Sweep / recovery budget  ", "扫掠 / 恢复预算         "))
                .Append(StaticObstacleCollisionSystem.MaxSweepIterations).Append(" / ")
                .Append(StaticObstacleCollisionSystem.MaxPenetrationPasses).Append('\n');
            _builder.Append(T("Diagnostic probe   ", "诊断探针          "))
                .Append(_overlay != null && _overlay.CollisionProbeHit ? T("HIT", "命中") : T("MISS", "未命中"))
                .Append(T("  TOI raw ", "  TOI raw ")).Append(_overlay?.CollisionProbeFractionRaw ?? 0).Append('\n');
            _builder.Append(T(
                "Pipeline            ORCA -> limiter -> CCD/slide -> SAT\n",
                "管线              ORCA -> 限幅器 -> CCD/滑动 -> SAT\n"));
        }

        private void AppendRollbackMetrics()
        {
            int latencyMilliseconds = (_host.SimulatedLatencyTicks * 1000) / _host.FixedRateHz;
            _builder.Append(T("ROLLBACK WINDOW\n", "回滚窗口\n"));
            _builder.Append(T("Current tick      ", "当前帧            ")).Append(_host.SimulationTick).Append('\n');
            _builder.Append(T("Simulated latency ", "模拟延迟          ")).Append(_host.SimulatedLatencyTicks).Append(T(" ticks / ", " tick / "))
                .Append(latencyMilliseconds).Append(" ms\n");
            _builder.Append(T("History length    ", "历史长度          ")).Append(_host.Rollback.HistoryLength).Append(T(" ticks\n", " tick\n"));
            _builder.Append(T("Retained commands ", "保留命令          ")).Append(_host.Rollback.CommandCount).Append("\n\n");

            _builder.Append(T("REPLAY RESULT\n", "重演结果\n"));
            _builder.Append(T("Rollback count    ", "回滚次数          ")).Append(_host.Rollback.RollbackCount).Append('\n');
            _builder.Append(T("Last / total resimulated  ", "最近 / 累计重演帧数       "))
                .Append(_host.Rollback.LastResimulatedTicks).Append(" / ")
                .Append(_host.Rollback.TotalResimulatedTicks).Append(T(" ticks\n", " tick\n"));
            _builder.Append(T("Replay range      ", "重演区间          ")).Append(_host.RollbackGhostOriginTick).Append(" -> ")
                .Append(_host.RollbackGhostDestinationTick).Append('\n');
            _builder.Append(T("Ghost samples     ", "残影样本          ")).Append(_host.RollbackGhostCount);
            _builder.Append(T("     group ", "     群组 ")).Append(_host.RollbackGhostGroup).Append('\n');
            _builder.Append(T("Hash before       0x", "回滚前哈希        0x")).Append(_host.Rollback.LastHashBeforeRollback.ToString("X16")).Append('\n');
            _builder.Append(T("Hash after        0x", "回滚后哈希        0x")).Append(_host.Rollback.LastHashAfterRollback.ToString("X16")).Append('\n');
            _builder.Append(T("Catch-up backlog  ", "追帧积压          ")).Append(_host.CatchUpBacklog).Append(T(" ticks\n", " tick\n"));
        }

        private string GetNeighborModeLabel(SpatialIndexMode mode)
        {
            return mode switch
            {
                SpatialIndexMode.UniformGrid => T("Uniform Grid radius (bounded top-K)", "均匀网格半径（有界 top-K）"),
                SpatialIndexMode.KdTree => T("KD-Tree radius (branch-pruned)", "KD-Tree 半径（分支剪枝）"),
                SpatialIndexMode.KdTreeKNearest => T("KD-Tree exact KNN (bounded K)", "KD-Tree 精确 KNN（有界 K）"),
                _ => T("Unknown", "未知"),
            };
        }

        private string T(string english, string chinese)
        {
            return SwarmUiLocalization.Select(Language, english, chinese);
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

# Swarm-ECS-Sandbox

> 10,000 Agents · Q16.16 Fixed-Point · Custom SoA ECS · Budgeted A* · Uniform Grid / KD-Tree · RVO2-style ORCA · Authoritative UDP · Prediction/Rollback · Versioned Replay · Indirect Rendering

Swarm-ECS-Sandbox 是一个面向大规模确定性仿真的 Unity 工程。10,000 个 Agent 的权威逻辑运行在纯 C#、固定容量、固定时间步的数据层；Unity 负责输入、调试界面与 GPU 表现。核心仿真不依赖 Unity Physics、NavMesh，也不为每个 Agent 创建 GameObject。

![10,000-agent Swarm ECS Sandbox](Docs/Images/swarm-sandbox.png)

### Runtime lab views

| Navigation: grid, rejected target and shared A* | Avoidance: sampled neighbors and ORCA half-planes |
|---|---|
| ![Navigation lab](Docs/Images/lab-navigation.png) | ![Avoidance lab](Docs/Images/lab-avoidance.png) |
| Collision: immutable BVH, CCD and contact normals | Rollback: before/after correction samples |
| ![Collision lab](Docs/Images/lab-collision.png) | ![Rollback lab](Docs/Images/lab-rollback.png) |

![Authoritative UDP lab](Docs/Images/lab-network.png)

## Release line

`v0.4.0` 增加真实 loopback/LAN UDP 会话：一个 headless 权威服务器与两个独立预测客户端通过版本化数据报、ack/ackBits、可靠命令、非可靠 hash telemetry 和固定容量线程交接队列通信。服务器统一决定命令 tick/sequence；客户端保持 prediction lead，在迟到命令到达后回滚重演并逐 tick 确认权威 hash。超出历史窗口的命令进入显式 `SnapshotRequired` 状态，完整 snapshot repair 与 reconnect 留给 v0.5。

公开能力以 Git tag、GitHub Release 和同一 commit 生成的证据附件为准；工作树中的结果不视为发布证据。

默认 GitHub Actions 会执行无需 Unity License 的静态工程与证据格式校验。Unity EditMode job 只有配置授权后才运行，因此静态 job 通过不代表远端已执行 Unity 测试。

## Architecture

```mermaid
flowchart LR
    A["Ordered commands"] --> B["Budgeted navigation"]
    B --> C["Spatial queries"]
    C --> D["Obstacle + agent ORCA"]
    D --> E["Acceleration / turn limits"]
    E --> F["Swept collision + slide"]
    F --> G["SAT fallback"]
    G --> H["Snapshot / hash / replay"]
    H -. "presentation only" .-> I["GPU indirect rendering"]
    J["UDP commands"] --> A
    H --> K["Hash telemetry"]
```

权威状态只使用 Q16.16、稳定遍历顺序和显式 tie-break。渲染层可使用 `float` 与可变帧率，但表现值不会写回仿真。

## Implemented systems

- **Q16.16 fixed-point core**：饱和加减乘除、整数平方根、向量、确定性 PRNG、配置哈希与状态哈希；`Core` / `Simulation` 不引用 `UnityEngine`。
- **Custom SoA ECS**：Position、Velocity、Radius、Group、PathCursor 等按组件列连续存储；实体使用稳定的 `index + generation`，运行容量预分配。
- **Budgeted shared A\***：64×64 八邻接网格、稳定 binary heap、连通岛预检、地图 revision、4 个固定群组请求槽、每 tick 固定预算和 68-entry 确定性路径缓存。10,000 个 Agent 共享 4 条宏观路线，而非各自执行 A*。
- **Three neighborhood modes**：Uniform Grid radius + bounded top-K、KD-Tree radius、KD-Tree exact KNN。KNN 以 65-bit squared distance 覆盖完整二维 Q16.16 坐标域。
- **Static and dynamic avoidance**：静态 OBB 生成稳定有向边与 obstacle ORCA lines；Agent-Agent 使用 RVO2-style half-plane 与 LP1/LP2/LP3。障碍约束固定写在 Agent 约束之前。
- **Immutable obstacle broadphase**：静态障碍构建不可变 BVH，查询复用 caller-owned scratch，并以稳定 ID 输出候选。
- **Fixed-point collision pipeline**：OBB 基轴量化为 Q16.16 点积下的严格正交单位基，BVH bounds 包含有证明上界的 raw-unit 截断保护；expanded-OBB slab conservative CCD、exact-raw circle corner distance、固定次数 impact/slide、SAT 最终穿透恢复与残余深度遥测。
- **Bounded kinematics**：ORCA 目标速度之后应用最大加速度、最大转向步长与最大速度限制；所有参数进入 `ConfigHash`。
- **Rollback and catch-up**：64 tick snapshot ring、按 `(tick, sequence)` 排序的固定容量命令时间线、延迟命令回滚重演，以及追帧期间跳过中间渲染。
- **Authoritative UDP session**：一个 headless Server 与两个独立 Client；44-byte 显式小端 packet header、CRC32、32-bit serial arithmetic、ack/ackBits、可靠重传、应用层命令顺序、输入延迟、prediction lead、逐 tick hash 确认和 `SnapshotRequired` 失败状态。Socket 线程只向固定容量队列复制数据报，不能访问 `SwarmWorld`。
- **Deterministic weak network**：在真实 socket send 前注入可复现的 latency、jitter、loss、duplication 与 reorder；固定容量调度器记录丢包、重复、乱序、容量溢出、RTT、带宽和重传数据。
- **Versioned replay and diagnostics**：`.swarmreplay` 固定字节序、显式 schema/config/logic 信息、命令与 checkpoint、完整性校验、有界执行预算、O(N) 规范命令装载与顺序播放；分层权威哈希可进一步定位到 component、entity/group、field 与 raw value。
- **Interactive technical lab**：Overview / Navigation / Avoidance / Collision / Rollback / Network 六页分层 HUD；支持 English / 简体中文全局切换并持久化本机选择；世界空间显示真实共享路线、阻塞节点、采样邻居、ORCA 速度约束、BVH bounds、CCD contact/slide 与 rollback ghost，Network 页明确区分本地交互 World 和外部三进程资格验证。覆盖层与语言状态不写回权威 World。
- **Indirect rendering**：CPU 上传 Agent 结构化数据，Unity 6 `Graphics.RenderMeshIndirect` 以一个 Agent indirect command 绘制；没有逐 Agent GameObject。
- **Commercial integration boundary**：工程固定 YooAsset 3.0.4 与 HybridCLR 8.12.0，并提供程序集、资源收集与加载边界；目标平台发布闭环仍需独立验收。

## Reproducible evidence

仓库跟踪三组 10k headless benchmark、一组独立进程 replay，以及一组真实 UDP 会话结果：

- [`BenchmarkResults/latest.json`](BenchmarkResults/latest.json) / [`latest.md`](BenchmarkResults/latest.md)：默认 Uniform Grid 的完整逻辑 tick。
- [`BenchmarkResults/spatial-index-matrix.json`](BenchmarkResults/spatial-index-matrix.json) / [`spatial-index-matrix.md`](BenchmarkResults/spatial-index-matrix.md)：相同 seed/config 下的三种完整运行模式。
- [`BenchmarkResults/obstacle-approach/latest.json`](BenchmarkResults/obstacle-approach/latest.json) / [`latest.md`](BenchmarkResults/obstacle-approach/latest.md)：延长 warmup/sample、实际触发静态障碍 ORCA 与 CCD 的场景。
- [`ReplayResults/latest.md`](ReplayResults/latest.md)：两个独立 Unity 进程对同一版本化 replay 的逐 checkpoint 校验和字段级 desync probe。
- [`NetworkResults/latest/latest.md`](NetworkResults/latest/latest.md) / [`summary.json`](NetworkResults/latest/summary.json)：三个独立 Player 进程的权威 UDP 弱网会话；保留 Server/Client 原始 JSON 报告。

结果记录 Unity/CPU/Graphics Device、warmup/sample、执行策略、`ConfigHash`、full/canonical state hash，以及 obstacle/agent ORCA lines、BVH query/candidate、CCD、SAT fallback、加速度/转向限幅等计数。`Null Device` 只代表纯逻辑测量，不代表渲染帧率；`managedBytesAcrossSamples` 只覆盖采样线程，不等同于所有 worker 均零分配。

## Quick start

1. 使用 **Unity 6000.3.9f1** 打开工程。
2. 打开 `Assets/Scenes/SwarmSandbox.unity` 并进入 Play Mode。
3. 从 HUD 观察 logic tick、CPU/tick、路径预算、空间查询、ORCA、CCD、限幅、状态哈希和 rollback。
4. 点击左上角 `中文` / `EN` 或按 `F1`，全局切换六个页面、操作按钮、说明文本和世界空间标注的语言。

完整的能力映射、8–10 分钟演示顺序和命令行复现入口见 [`Docs/DEMO_GUIDE.md`](Docs/DEMO_GUIDE.md)。

技术实验页可通过 HUD 标签或数字键切换：

| Key | Lab view | Visible evidence |
|---|---|---|
| `1` | Overview | 实时逻辑预算、完整 pipeline、hash 与共享路线 |
| `2` | Navigation | 64×64 Grid、阻塞节点、共享 A*、目标与请求/缓存状态 |
| `3` | Avoidance | 真实采样邻居、Agent/Obstacle ORCA lines、preferred/safe velocity |
| `4` | Collision | OBB、BVH bounds、确定性 CCD probe、实时 contact/normal/slide |
| `5` | Rollback | late command 的预测/修正 ghost、重演区间与 before/after hash |
| `6` | Network | UDP envelope、可靠性、三进程拓扑、prediction/repair 边界与复现入口 |

| Key | Action |
|---|---|
| `Space` | 暂停 / 继续 |
| `F1` | English / 简体中文全局切换 |
| `L` | 注入延迟 18 tick 的群组目标命令并回滚重演 |
| `T` | 加入 600 tick 追帧积压；期间跳过中间渲染 |
| `K` | 循环 `Uniform Grid radius → KD-Tree radius → KD-Tree exact KNN`，以权威命令切换 |
| `R` | 使用相同 seed 重置世界 |
| `WASD` / 滚轮 | 平移 / 缩放相机 |

Navigation 页的 `QUEUE BLOCKED TARGET` 通过正常权威命令把 Group 0 目标放入中心障碍，用于展示不可达请求的固定预算拒绝；`RESET` 恢复基准世界。Collision 页同时区分 presentation-only deterministic probe 与最近真实 ECS CCD contacts。

## Validation

运行全部 EditMode 测试：

```bash
mkdir -p TestResults
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

运行默认 10k headless benchmark：

```bash
SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"
```

运行跨进程 replay 验证：

```bash
./Scripts/run-cross-process-replay.sh
```

脚本先后启动两个独立 Unity batchmode 进程，生成/校验同一个 replay，并输出：

- `ReplayResults/cross-process.swarmreplay`
- `ReplayResults/capture.json`
- `ReplayResults/verify.json`
- `ReplayResults/latest.md`

`verify.json` 记录独立 PID、逐 checkpoint 比对、最终分层权威哈希，以及对 `AgentPositions[0].X.Raw` 的可控 desync probe。三模式矩阵与证据字段说明见 [`Docs/BENCHMARKING.md`](Docs/BENCHMARKING.md)。发布前应在目标 commit 上重新生成测试、benchmark、replay 和 Player 证据；具体流程见 [`Docs/RELEASE_CHECKLIST.md`](Docs/RELEASE_CHECKLIST.md)。

构建 macOS Player 后运行真实 `1 Server + 2 Clients` UDP 验证：

```bash
./Scripts/run-authoritative-udp-session.sh
```

脚本要求三个不同的操作系统进程在弱网注入下完成握手、四条命令仲裁、可靠重传、预测回滚和最终 hash 收敛，并写入 `NetworkResults/latest/`。默认运行 210 tick 的发布资格验证；长时间 soak 可设置 `SWARM_NETWORK_FINAL_TICK=54000`。具体 envelope、状态机和边界见 [`Docs/PROTOCOL_v0.4.md`](Docs/PROTOCOL_v0.4.md)。

## Repository map

```text
Assets/SwarmSandbox/
├── Core/FixedPoint          Q16.16 vectors and integer math
├── Core/Determinism         PRNG and FNV-1a
├── Simulation/ECS           Entities, SoA World, authority state
├── Simulation/Spatial       Uniform Grid, KD-Tree, static-obstacle BVH
├── Simulation/Pathfinding   Grid, islands, A*, shared path cache
├── Simulation/Avoidance     Agent and obstacle ORCA constraints
├── Simulation/Collision     Fixed-point OBB, SAT and swept tests
├── Simulation/Netcode       Command timeline, snapshots, rollback and UDP protocol primitives
├── Simulation/Replay        Versioned replay format and validation
├── Simulation/Determinism   Layered hashes and desync diagnostics
├── Simulation/Systems       Navigation, avoidance, motion and workers
├── Runtime/Rendering        GraphicsBuffer and indirect rendering
├── Runtime/UI               Layered HUD and presentation-only technical overlays
├── Runtime/Networking       UDP socket worker, peer links and Server/Client session runners
├── Runtime/Commercial       YooAsset / HybridCLR integration boundary
└── Editor                   Scene, tests, benchmark and replay tools
```

## Design boundaries

- 运动学 limiter 位于 holonomic ORCA 之后；限加速度或限转向可能使最终速度不再严格满足全部 ORCA half-plane。CCD、slide 和 SAT fallback 负责几何安全，但这不等价于 kinodynamic ORCA 的严格可行解。
- CCD 使用圆半径扩张 OBB 后的 slab sweep。该方法对高速穿越是保守的，但扩张后的方形角会在真实圆角附近产生偏早接触。
- Swept broadphase 按真实圆形运动包围盒扩张，因此不保证保留只存在于方角 slab 保守区域中的 narrowphase 假阳性；真实 circle-vs-OBB 接触仍由保守 OBB bounds 覆盖。
- 静态障碍与 BVH 在初始化后不可变。拓扑变化需要重建仿真并开启新的 rollback epoch，当前不能跨 topology epoch 恢复旧快照。
- BVH 构建只发生在静态初始化阶段，使用确定性排序，最坏 `O(N²)`；剪枝查询最坏 `O(N)`，K 个结果的稳定排序为 `O(K log K)`。
- KD 查询使用精确 branch pruning，但退化分布下仍可能访问 `O(N)` 节点，不能假定稳定 `O(log N)`。
- replay 与字段级 diff 已提供可复现和定位工具，但跨 Mono/IL2CPP、ARM64/x64 的完整哈希矩阵仍需由对应平台 artifact 证明。
- v0.4 transport 面向 loopback/LAN 实验，不包含加密、身份服务、NAT traversal、anti-cheat 或通用时钟同步。它使用输入延迟与有限 prediction lead，而不是生产级 clock discipline。
- 超 rollback 窗口会显式进入 `SnapshotRequired`，但 v0.4 不传输或应用 full/delta snapshot，也不支持断线重连；这些属于 v0.5。
- Agent 渲染仍由 CPU 每帧上传全部实例；没有 per-instance GPU culling、Hi-Z 或 HLOD。
- Technical Lab 覆盖层使用 Unity `float`、GL lines 与采样诊断，只负责解释权威结果；它不进入状态哈希、snapshot、replay 或 headless benchmark 时间口径。

## Documentation

- [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md)：权威数据流、导航、避障、碰撞与内存边界
- [`Docs/DETERMINISM_AND_NETCODE.md`](Docs/DETERMINISM_AND_NETCODE.md)：确定性契约、snapshot、replay 与 desync diagnostics
- [`Docs/PROTOCOL_v0.4.md`](Docs/PROTOCOL_v0.4.md)：UDP envelope、可靠性、握手、Server/Client 状态机和线程边界
- [`Docs/TECHNICAL_WALKTHROUGH.md`](Docs/TECHNICAL_WALKTHROUGH.md)：架构审阅路径与复现实验
- [`Docs/DEMO_GUIDE.md`](Docs/DEMO_GUIDE.md)：能力映射、交互演示顺序、双语界面和证据复现入口
- [`Docs/BENCHMARKING.md`](Docs/BENCHMARKING.md)：基准入口、输出字段与解释边界
- [`Docs/ROADMAP_2027.md`](Docs/ROADMAP_2027.md)：后续版本顺序与量化门禁
- [`Docs/COMMERCIAL_PIPELINE.md`](Docs/COMMERCIAL_PIPELINE.md)：YooAsset + HybridCLR 当前接入范围
- [`Docs/RELEASE_CHECKLIST.md`](Docs/RELEASE_CHECKLIST.md)：发布前验证与证据清单
- [`Docs/RELEASE_NOTES_v0.4.0.md`](Docs/RELEASE_NOTES_v0.4.0.md)：v0.4.0 变化、网络证据与已知限制
- [`CHANGELOG.md`](CHANGELOG.md)：版本变更记录

## License

项目自有源码使用 [MIT License](LICENSE)。RVO2-derived fixed-point adaptation、Unity、YooAsset 与 HybridCLR 适用各自许可证，详见 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

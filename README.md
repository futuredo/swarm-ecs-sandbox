# Swarm-ECS-Sandbox

> 万单位同屏 · Q16.16 定点数 · 自定义 SoA ECS · A* / Uniform Grid / KD-Tree · RVO2 ORCA · SAT · Rollback · GPU Indirect Instancing

这是一个面向客户端底层、引擎与资深 Gameplay 岗位的 Unity 技术作品集。项目不用 Unity Physics、NavMesh 或“每单位一个 GameObject”，而是把 10,000 个 Agent 的权威逻辑放进纯 C#、连续内存、固定时间步的确定性仿真中；Unity 只负责输入、调试 UI 和渲染表现。

![10,000-agent Swarm ECS Sandbox](Docs/Images/swarm-sandbox.png)

## 已实现的硬核技术栈

- **Q16.16 定点数数学库**：饱和加减乘除、整数平方根、向量、确定性 PRNG 和状态哈希；`Core` / `Simulation` 不依赖 `UnityEngine`。
- **自定义 SoA ECS**：位置、速度、半径、阵营、路径游标等组件分列连续存储，实体使用稳定 `index + generation`，运行容量预分配。
- **空间索引**：默认 Uniform Grid 空间哈希，以扫描期有序 top-K 保留最近邻；可在运行时切换数据导向 KD-Tree，结果以 `distance² + entityId` 稳定排序。
- **宏观 A***：64×64 固定网格、不可行走区、障碍惩罚模糊、四编队共享路径，避免给 10,000 个单位重复规划同一条路线。
- **微观 RVO2 / ORCA**：手写速度障碍与线性规划 LP1/LP2/LP3；持久工作线程按确定区间并行计算，不使用每帧 `Task`、LINQ 或临时容器。
- **定点数 SAT**：OBB/圆碰撞与静态障碍穿透修正，完全脱离 Unity Physics。
- **Rollback / Catch-up**：64 帧预分配快照环、确定性命令时间线、延迟权威指令回滚重演，以及跳过渲染的快速追帧。
- **GPU Driven 表现**：Agent 数据写入 `ComputeBuffer`，通过 `Graphics.DrawMeshInstancedIndirect` 一次间接实例化绘制 10,000 个单位；没有单位 GameObject。
- **商业化边界**：已固定并配置 YooAsset 3.0.4 与 HybridCLR 8.12.0，热更程序集、AOT 元数据加载和资源包收集器保持在仿真层之外。
- **工程验证**：43 个 EditMode 测试覆盖定点数边界、空间查询、A*、SAT、ORCA、双世界逐位一致、回滚一致与热路径零 GC。

## 可复现基准

以下数据由仓库内 `SwarmBenchmarkRunner` 直接生成，原始结果位于 [`BenchmarkResults/latest.json`](BenchmarkResults/latest.json)。这是 `-batchmode -nographics` 下的**纯逻辑 tick**，不包含 GPU 渲染；它不是移动端 60 FPS 宣称。

| 项目 | 实测值 |
|---|---:|
| Unity / CPU | 6000.3.9f1 / Apple M5 Pro（15 logical cores） |
| Agent | 10,000 |
| 固定逻辑帧率 | 30 Hz（33.333 ms 预算） |
| Warmup / Sample | 8 / 32 ticks |
| 平均耗时 | **24.123 ms/tick** |
| P95 | **29.366 ms/tick** |
| Min / Max | 20.944 / 31.046 ms |
| Sample 托管分配 | **0 B** |
| 最终完整状态哈希 | `0x35BF23DECBD70D8D` |

基准结果会随 CPU、温控、Unity 版本和后台负载变化；仓库提交实测环境与哈希，便于复跑和识别算法改动。

## 快速运行

1. 用 **Unity 6000.3.9f1** 打开项目。首次打开会从固定 Git tag 拉取 YooAsset / HybridCLR。
2. 打开 `Assets/Scenes/SwarmSandbox.unity` 并进入 Play Mode。
3. 观察 HUD 中的 logic tick、CPU/tick、GC、邻居/ORCA 约束数、SAT 接触数、状态哈希与回滚次数。

运行控制：

| 按键 | 行为 |
|---|---|
| `Space` | 暂停 / 继续 |
| `L` | 注入延迟 18 tick 的权威指令并回滚重演 |
| `T` | 加入 600 tick 追帧积压（追帧期间不渲染） |
| `K` | Uniform Grid / KD-Tree 切换 |
| `R` | 以同一 seed 重置世界 |
| `WASD` / 滚轮 | 平移 / 缩放相机 |

## 一键验证

在 macOS 上运行 EditMode 测试：

```bash
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

运行 10k headless 基准：

```bash
SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"
```

结果会覆盖 `BenchmarkResults/latest.json` 与 `latest.md`。

## 代码地图

```text
Assets/SwarmSandbox/
├── Core/FixedPoint          Q16.16、向量与整数数学
├── Core/Determinism         XorShift32 / FNV-1a
├── Simulation/ECS           Entity + SoA World + Config
├── Simulation/Spatial       Uniform Grid / KD-Tree
├── Simulation/Pathfinding   GridMap / A* / SharedPath
├── Simulation/Avoidance     RVO2 ORCA LP 求解器
├── Simulation/Collision     定点数 SAT
├── Simulation/Netcode       Command Timeline / Snapshot Ring / Rollback
├── Simulation/Systems       确定性系统流水线与持久并行工作线程
├── Runtime/Rendering        GPU Indirect Instancing
├── Runtime/Commercial       YooAsset / HybridCLR 运行时边界
└── Editor                   场景、基准、构建与商业管线配置工具
```

进一步阅读：

- [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md)：数据流、内存布局与系统顺序
- [`Docs/DETERMINISM_AND_NETCODE.md`](Docs/DETERMINISM_AND_NETCODE.md)：确定性契约、状态哈希、Rollback 与追帧
- [`Docs/COMMERCIAL_PIPELINE.md`](Docs/COMMERCIAL_PIPELINE.md)：YooAsset + HybridCLR 的真实接入边界
- [`Docs/INTERVIEW_GUIDE.md`](Docs/INTERVIEW_GUIDE.md)：面试讲解路线、权衡与可继续扩展项

## 项目边界

这是一个聚焦底层算法的技术沙盒：网络层用延迟指令注入模拟，没有实现真实 Socket、服务器仲裁或完整断线协议；SAT 当前面向静态 OBB 障碍；YooAsset / HybridCLR 已接入工程配置和加载边界，但每个平台仍需按官方流程执行 Installer、Generate/All 与资源构建。所有这些边界都在文档中显式保留，不用未完成项冒充实测能力。

## License

[MIT](LICENSE)

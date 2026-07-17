# Technical Lab Guide

Swarm-ECS-Sandbox 提供两条互补的验证路径：交互式 Technical Lab 用于观察系统行为，命令行验证用于生成可比较的机器证据。两者读取同一套定点数仿真与诊断数据，但表现层不写回权威状态。

## Capability map

| Layer | Implemented capability | Runtime evidence |
|---|---|---|
| Deterministic math | Q16.16 标量/向量、整数平方根、确定性 PRNG、配置与状态哈希 | Overview 的 Twin-world、Config hash、State hash |
| Data layout | 自定义固定容量 SoA ECS，稳定 Entity index/generation，无逐 Agent GameObject | Overview 的 10,000 Agents 与 hot-path allocation |
| Navigation | 64×64 Grid、模糊代价、连通岛预检、固定请求预算、确定性缓存、四组共享 A* | Navigation 的网格、阻塞节点、路径、队列、缓存和岛屿拒绝 |
| Spatial query | Uniform Grid radius top-K、KD-Tree radius、KD-Tree exact KNN | Avoidance 的查询模式、邻居连接与采样连线 |
| Local avoidance | Agent/Obstacle ORCA half-plane、稳定约束顺序、LP 求解 | Avoidance 的邻居、约束线、期望速度和安全速度 |
| Collision safety | 不可变障碍 BVH、定点数 swept-circle CCD、slide、SAT 穿透兜底 | Collision 的 BVH、扫掠路径、TOI、法线、滑动与实时接触 |
| Motion quality | 最大加速度、转向步长和速度限制 | Overview/Avoidance 的 acceleration/turn limited 计数 |
| Rollback | 64 tick snapshot ring、有序命令时间线、迟到命令恢复与规范重演、追帧 | Rollback 的修正前后残影、重演范围与 before/after hash |
| Replay diagnostics | 版本化 replay、checkpoint、分层权威 hash、字段级首个差异 | `ReplayResults/` 与跨进程脚本输出 |
| UDP session | 1 权威端 + 2 预测客户端、可靠命令、非可靠 hash telemetry、弱网注入 | Network 页面与 `NetworkResults/latest/` |
| Rendering | `Graphics.RenderMeshIndirect` 单条 Agent 间接绘制命令，诊断覆盖层与仿真解耦 | Overview 的 draw count 与全部世界空间覆盖层 |

## Run the interactive lab

### Unity Editor

1. 使用 Unity `6000.3.9f1` 打开仓库根目录。
2. 打开 `Assets/Scenes/SwarmSandbox.unity`。
3. 进入 Play Mode。场景会以固定 seed 创建 10,000 个 Agent，并默认打开 Overview。
4. 点击左上角的 `中文` / `EN`，或按 `F1`，切换全部运行时界面的语言。选择会保存在本机，下次启动继续使用。

### Built Player

从 GitHub Release 下载对应平台构建，解压后直接运行。macOS 首次运行如果被 Gatekeeper 拦截，可在 Finder 中右键应用并选择“打开”。

Player 与 Editor 使用相同按键和同一场景。渲染 FPS 只应在有图形设备的交互模式下解释；命令行 `-nographics` 结果只衡量逻辑执行。

## Recommended walkthrough

以下流程约需 8–10 分钟，不要求修改工程设置。

### 1. Overview — system boundary

- 确认 Agent 数量为 10,000，Agent render 为一条 indirect command。
- 观察 logic tick、CPU/tick budget、hot-path GC、ORCA/CCD/SAT/limiter 计数。
- 对照 Config hash 与 State hash；Twin-world 应为 `PASS` / `通过`。
- 按 `K` 循环空间查询实现，观察模式改变而仿真继续以权威命令推进。

### 2. Navigation — macro path planning

- 按 `2`，查看 64×64 网格、阻塞节点、四条群组共享路径与目标。
- 点击 `QUEUE BLOCKED TARGET` / `加入阻塞目标`。
- Group 0 的目标落入中心障碍后，`Island rejected` / `岛屿预检拒绝` 增加；请求不会穷举整张地图。
- 观察每 tick 请求预算、pending、cache hit/miss 和 rollback-time derived cache rebuild。

### 3. Avoidance — local velocity solving

- 按 `3`，世界空间会标记一个真实采样 Agent、其邻居和 ORCA 约束。
- 黄色向量是 preferred velocity，青色向量是 LP 求解后的 safe velocity；蓝色和橙色分别表示 Agent 与静态障碍约束。
- 点击 `SAMPLE NEXT GROUP` / `采样下一群组`，确认诊断采样来自不同活动群组。
- 按 `K` 比较三种空间查询模式的实时邻居连接和运行成本。

### 4. Collision — geometric safety

- 按 `4`，查看不可变 BVH 的内部节点/叶节点与确定性扫掠探针。
- 红色为请求位移，橙色为 TOI，青色为切向滑动，黄色为接触法线。
- `Live retained traces` / `保留的实时轨迹` 来自真实 ECS CCD contact；diagnostic probe 只用于解释算法。
- SAT recovery 是最终穿透兜底，正常安全路径优先经过 obstacle ORCA、limiter 和 CCD/slide。

### 5. Rollback — correction and replay

- 按 `5` 后点击 `INJECT 18-TICK LATE COMMAND` / `注入迟到 18 TICK 的命令`，或直接按 `L`。
- 洋红位置表示修正前预测，青色表示恢复快照并重演后的结果。
- 观察 rollback count、重演区间、重演 tick 数以及修正前后 hash。
- 按 `T` 加入追帧积压；中间表现帧会被跳过，逻辑保持固定步长推进。

### 6. Network — process boundary

- 按 `6`，确认交互场景的单 World 与外部资格验证的三进程拓扑被明确区分。
- 页面列出 packet header、CRC32、ACK history、可靠/非可靠通道以及 prediction/repair 边界。
- 此页面解释协议状态；真正的 Server/Client 隔离由下一节脚本启动的三个操作系统进程证明。

## Controls

| Input | Action |
|---|---|
| `1`–`6` | Overview / Navigation / Avoidance / Collision / Rollback / Network |
| `F1` | English / 简体中文全局切换 |
| `Space` | 暂停 / 继续 |
| `L` | 注入迟到 18 tick 的命令并回滚重演 |
| `T` | 加入 600 tick 追帧积压 |
| `K` | 循环 Uniform Grid radius / KD-Tree radius / KD-Tree exact KNN |
| `R` | 以相同 seed 重置世界 |
| `WASD` / 滚轮 | 平移 / 缩放相机 |

## Reproduce machine evidence

### EditMode tests

```bash
mkdir -p TestResults
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

### Cross-process replay

```bash
./Scripts/run-cross-process-replay.sh
```

该脚本启动两个独立 Unity batchmode 进程，对同一个 `.swarmreplay` 执行 checkpoint/hash 验证，并输出字段级 desync probe。

### Authoritative UDP session

先构建 macOS Player，再运行：

```bash
./Scripts/run-authoritative-udp-session.sh
```

脚本启动一个 headless Server 和两个 predictive Client Player 进程，在可复现弱网注入下验证握手、服务器 tick/sequence 仲裁、可靠重传、迟到命令回滚与最终 hash 收敛。

### Automated bilingual screenshot

构建后的 Player 支持可复现截图参数：

```bash
"./Builds/macOS/SwarmECS.app/Contents/MacOS/Swarm ECS Sandbox" \
  -screen-width 1440 -screen-height 900 \
  -swarmCaptureView Overview \
  -swarmCaptureLanguage SimplifiedChinese \
  -swarmCapturePath "$PWD/TestResults/overview-zh.png"
```

`-swarmCaptureView` 接受六个 Lab view 名称，`-swarmCaptureLanguage` 接受 `English` 或 `SimplifiedChinese`。自动截图参数只影响表现层。

## Interpretation boundaries

- Editor/Player HUD 是实时诊断，不替代同 commit 的 benchmark、replay 和 UDP 结果文件。
- `Null Device` benchmark 不提供渲染 FPS 结论；交互 Player FPS 不等于纯逻辑 benchmark。
- HUD、GL lines、相机与语言状态均不参与 ConfigHash、StateHash、snapshot 或 replay。
- v0.4 网络范围是 loopback/LAN；full/delta snapshot repair、断线重连、认证、加密和 NAT traversal 不在当前实现范围。

# Swarm-ECS-Sandbox 2027 求职强化计划

## 1. 计划定位

当前仓库已经是一套可以运行、测试和复现性能数据的 `v0.2` 技术沙盒。下一阶段的目标不是继续堆算法名词，而是把现有能力升级为四条可审查的工业级证据链：

1. **确定性可证明**：跨进程、跨后端、跨 CPU 架构仍能得到同一 raw state。
2. **网络可恢复**：真实弱网下可以预测、回滚、重连、追帧并定位不同步。
3. **性能可审计**：有 System 级 CPU/GPU/GC 指标、长稳 P99 和回归基线。
4. **构建可交付**：HybridCLR + YooAsset + IL2CPP 能从干净机器完成构建、更新和失败回滚。

建议以 14–18 周完成六个阶段。每个阶段必须同时交付代码、自动化测试、基准报告、演示素材和一条可写进简历的结论。

## 2. 当前基线

### 已完成

- Q16.16 定点数、SoA ECS、Uniform Grid、KD-Tree、共享 A*、RVO2 ORCA、SAT。
- 持久线程池并行 ORCA，10,000 Agent headless 平均 24.123 ms/tick，采样期主线程 0 B。
- 64 帧完整快照环、延迟命令回滚和无渲染追帧。
- 同 tick 指令按 `(tick, sequence)` 规范排序，不依赖到包先后顺序。
- Unity 6 `GraphicsBuffer + RenderMeshIndirect` 单批 Agent 渲染。
- YooAsset / HybridCLR 工程配置与运行时加载边界。
- 44 个 EditMode 测试、macOS 验证 Player、公开仓库文档和截图。

### 尚未完成，不能写成已有能力

- 真实 UDP/KCP 通信、服务器仲裁、双客户端联机。
- Mono/IL2CPP、ARM64/x64 的跨进程确定性矩阵。
- 超过 rollback window 后的权威全量状态修复。
- Compute Shader Frustum/Hi-Z Culling、GPU LOD 与十万表现实例验证。
- HybridCLR Installer/Generate All、YooAsset Host Mode、CDN 和正式 IL2CPP 热更闭环。
- 10 分钟以上长稳 P99、全部 worker GC 和 GPU 时间报告。

## 3. 总体里程碑

| 阶段 | 建议周期 | 核心产出 | 求职价值 |
|---|---:|---|---|
| M1 跨进程确定性认证 | 2 周 | Replay Runner、Desync Diff、跨 lane/hash 矩阵 | 帧同步底座 |
| M2 真实 UDP 帧同步 | 3–4 周 | 1 Server + 2 Client、弱网模拟、预测回滚 | 联机深水区 |
| M3 重连与状态修复 | 2–3 周 | Versioned Snapshot、Delta、追帧、取证包 | 商业容灾能力 |
| M4 性能工程实验室 | 2 周 | ProfilerMarker、长稳 P99、Burst/Jobs 对照 | 引擎性能证据 |
| M5 GPU Driven 2.0 | 3 周 | Compute Culling、LOD、Hi-Z、多命令 Indirect | 渲染底层能力 |
| M6 商业发布闭环 | 2–3 周 | HybridCLR + YooAsset + IL2CPP E2E | 国内商业管线 |

推荐依赖顺序：

```text
确定性认证 → 真实网络 → 重连/取证
        ↘ 性能实验室 → GPU Driven
                       ↘ 商业发布闭环
```

## 4. M1：跨进程确定性认证实验室

### 目标

把“同一进程中两个 World 一致”升级为可自动证明的跨进程、跨线程调度和跨构建后端一致。

### 技术任务

- 将命令规范键扩展为 `(tick, playerId, sequence)`，明确重复、冲突、过期和容量淘汰规则。
- 定义 `.swarmreplay` 二进制格式：magic、schema version、logic version、config hash、seed、command stream、checkpoint hash。
- 实现无 Unity 画面的 Replay Runner，加载录像并输出每 N tick 的状态哈希。
- 将 worker lane 数变成测试参数，覆盖 1/2/4/8/16 lane。
- 建立权威状态注册表；Entity generation、PRNG state 及未来新增组件必须显式进入 Snapshot/Hash schema。
- 实现 Desync Diff：报告第一个分歧 tick、组件列、entityId、raw expected/actual 和附近命令窗口。
- 将 replay schema 与 logic hash 写入握手所需的版本描述对象。

### 验收门槛

- 同一 tick 的指令随机洗牌 1,000 次，最终状态哈希一致。
- 10 个 seed × 3,000 tick × 1/2/4/8/16 lane 无分歧。
- 同一 replay 在 Editor Mono、macOS IL2CPP ARM64、Windows IL2CPP x64 上检查点哈希一致。
- PR 跑 3 seed × 600 tick；nightly 跑完整矩阵。
- 故意翻转一个 raw 字段时，工具能定位首个分歧字段，而不只显示“hash 不同”。

### 主要风险

- Snapshot/Hash schema 发布后必须版本化，不能静默改变字段顺序。
- 跨平台 CI 需要 Unity License 或自托管构建机。
- 任何非确定性容器、未稳定排序和未登记状态都会在这一阶段暴露。

### 阶段成果

- `v0.3-determinism-lab` Release。
- 一份跨平台 hash matrix 与 desync 示例报告。
- 简历表述：构建跨进程确定性回放与差异定位框架，覆盖 Mono/IL2CPP、ARM64/x64 及 1–16 lane 调度矩阵。

## 5. M2：真实 UDP 帧同步与 Rollback

### 目标

把 HUD 按钮模拟延迟升级为一个无渲染服务器进程和两个独立 Unity Client 的真实联机 Demo。

### 技术任务

- 实现 Headless Session Server；服务器只接受命令并广播权威 `FrameBundle`。
- UDP 包头包含 protocol、session、peer、sequence、ack、ackBits、channel、tick、payload length 和 CRC。
- 命令走可靠有序通道；状态哈希走非可靠遥测通道；大快照预留分片通道。
- 客户端实现 input delay、local prediction、confirmed tick、权威帧校正和 rollback。
- 网络线程只写入有界 SPSC/MPSC 队列，不直接修改 `SwarmWorld`。
- 握手校验协议版本、logic hash、config hash、fixed-point version 和 hot-update version。
- 内置 loss、jitter、duplicate、reorder 和 bandwidth 网络模拟器。
- HUD 增加 RTT、confirmed tick、prediction lead、rollback depth、loss 与带宽。

### 验收门槛

- 自动拉起 1 Server + 2 Client，10,000 Agent 连续运行 30 分钟。
- 在 `100±50 ms RTT / 5% loss / 2% reorder / 1% duplicate` 下，所有 confirmed tick 哈希一致。
- 可靠命令最终送达率 100%，命令历史和网络队列容量有上限。
- Rollback P95 小于 12 tick，最大深度不超过 64 tick 窗口。
- 典型群组指令场景单客户端平均带宽目标低于 20 KB/s。

### 范围控制

第一版只做 loopback/LAN。NAT 穿透、匹配、账号、房间服务和反作弊不属于该里程碑，避免把基础设施范围无限扩大。

### 阶段成果

- `v0.4-network-rollback` Release。
- 自动弱网演示视频和 30 分钟 soak-test 报告。
- 简历表述：实现服务端仲裁帧同步协议、ack-bitfield 可靠传输、预测与 GGPO 式回滚，并通过双客户端弱网长稳测试。

## 6. M3：断线重连、状态修复与 Desync 取证

### 目标

补齐超过 rollback window 后的商业恢复路径，并让线上不同步具备可复现证据。

### 技术任务

- 实现带 schema version、logic/config hash、CRC 的权威 Snapshot Serializer。
- 支持 Full Snapshot、基于 confirmed tick 的 XOR/Delta Snapshot、压缩、分片和重传。
- 超过 64 tick 窗口时请求权威快照，不再把延迟简单钳制到历史范围。
- 重连状态机：握手 → 基准快照 → 后续命令流 → 无渲染追帧 → 恢复表现。
- 状态哈希拆成 World / Component / Chunk / Entity 层级，缩小取证范围。
- 自动输出 desync bundle：双方版本、命令窗口、快照、hash tree 和首个分歧字段。
- 服务端加入指令所有权、频率、tick 范围和参数合法性检查。

### 验收门槛

- 1,000 轮随机断连/重连回归后无未恢复分歧。
- 任意损坏分片会被拒绝并重传，不能污染权威状态。
- Delta Snapshot 中位体积目标低于对应 Full Snapshot 的 35%。
- 先记录当前 300/600 tick 追帧基线；优化后以 300 tick 在目标机 3.5 秒内完成为挑战目标。
- 故意修改一个实体字段时，诊断包能定位具体 tick、component 和 entityId。

### 阶段成果

- `v0.5-reconnect-forensics` Release。
- Snapshot 格式文档、重连时序图、带宽和追帧报告。
- 简历表述：设计版本化全量/增量快照、断线重连、无渲染追帧和组件级 desync dump。

## 7. M4：性能工程实验室与 Burst/Jobs 对照

### 目标

把一次短 benchmark 变成可审查、可回归、能解释瓶颈的性能证据链。

### 技术任务

- 给 Navigation、Grid Build/Query、ORCA、Integration、SAT、Snapshot、Hash、GPU Upload 和 Draw 增加 `ProfilerMarker`。
- 用 `ProfilerRecorder` 采集主线程、所有 ORCA worker、GC、峰值内存、draw call、CPU/GPU frame time。
- 建立 1k/5k/10k/20k Agent、不同密度、邻居数、Grid/KD、lane 数和 rollback burst 矩阵。
- benchmark 延长至 10 分钟，提交 P50/P95/P99、温控前后差异和 commit hash。
- 增加 Burst/Jobs 后端作为**对照实现**，不删除当前手写 SoA/线程池基线。
- 优先使用 Unity 6 已发布的 [Burst](https://docs.unity3d.com/cn/current/Manual/com.unity.burst.html) 与 [Collections](https://docs.unity3d.com/6000.0/Manual/com.unity.collections.html) 稳定版本；Entities 可作为后续 archetype/chunk 对照，而不是为了标签强行重写全部系统。
- 固定机器做性能门禁；公共共享 Runner 只产出报告，不因机器噪声直接阻断 PR。

### 验收门槛

- M5 Pro Development Player 连续 10 分钟，10k 仿真 P99 目标小于 33.333 ms。
- 所有 worker 的 simulation marker 都证明 0 B，而非只统计调用线程。
- 至少覆盖 5 个 Agent/密度组合与 5 个 lane 配置。
- 单独报告 rollback 18 tick、reconnect 300 tick、catch-up 600 tick 的耗时。
- 固定基线退化超过 10%–15% 时生成对比 artifact 和告警。

### 阶段成果

- `v0.6-performance-lab` Release。
- 可复现 benchmark matrix、Profiler trace 和 Burst/Managed 对照报告。
- 简历表述：建立 System 级 CPU/GPU/GC 可观测性与自动性能回归矩阵，以长稳 P99 驱动优化。

## 8. M5：GPU Driven 2.0

### 目标

从“一次 indirect draw”升级为 GPU 自己决定可见性、LOD 和 draw args。

当前基线已经迁移到 Unity 6 官方推荐的 `GraphicsBuffer + RenderMeshIndirect`；旧 `DrawMeshInstancedIndirect` 在 Unity 6.3 已标记 obsolete。参见 [Unity 6.3 RenderMeshIndirect](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Graphics.RenderMeshIndirect.html)。

### 技术任务

- Compute Shader 执行视锥裁剪、距离 LOD 分类、可见索引压缩和 indirect args 生成。
- 设计高/中/低三档表现，可使用实体 Mesh、低面 Mesh、Billboard；每档一个 indirect command。
- 使用 `GraphicsBuffer.BeginWrite/EndWrite` 或 NativeArray 路径减少 CPU 中间复制。
- 增加纯渲染压力模式，将“十万表现实例”与“万单位完整仿真”分开报告。
- 第二步生成 depth pyramid 并实现保守 Hi-Z 遮挡裁剪。
- 加 temporal hysteresis 和 bounds inflation，避免相机快速移动时误剔除/闪烁。
- Debug overlay 显示 input、visible、culled、各 LOD 数量与 GPU 时间。

### 验收门槛

- 100,000 个纯表现实例、1600×900、M5 Pro，挑战目标 GPU frame time 小于 8 ms。
- Agent indirect command 不超过 3 个，CPU render submit 目标小于 1 ms。
- 70% 实例位于视锥外时，不发生逐帧 CPU readback。
- 小规模自动测试中，Compute 可见集合与 CPU oracle 一致。
- Metal 与 DX12 都通过 buffer layout、culling 和截图回归。

### 主要风险

- Hi-Z 与渲染管线耦合较强，应先交付 Frustum + LOD，再做 Hi-Z。
- 仿真能力和纯渲染实例数必须使用两张独立结果表，不能混写。
- Unity 6 在 URP/HDRP 还提供 GPU Resident Drawer / BRG；本项目是自定义数据源，保留 Compute + Indirect 路线，同时将 [Unity 官方 draw-call 方案比较](https://docs.unity3d.com/current/Manual/optimizing-draw-calls-choose-method.html) 作为架构对照。

### 阶段成果

- `v0.7-gpu-driven` Release。
- GPU culling 可视化视频、Metal/DX12 对照和十万实例报告。
- 简历表述：实现 Compute Frustum/Hi-Z Culling、LOD 分类、可见列表压缩与多命令 Indirect Rendering。

## 9. M6：HybridCLR + YooAsset + IL2CPP 发布闭环

### 目标

证明热更新不是只有配置，而是能从干净机器构建、发布、升级、校验和失败回滚。

### 技术任务

- CI 执行 HybridCLR Installer、Generate/All、AOT metadata 与 hot-update DLL 编译。
- 使用 IL2CPP 构建正式 Player，不再以临时关闭 HybridCLR 的 Mono 验证包作为交付证据。
- DLL、metadata、配置和表现资源进入 YooAsset package；实现 Offline / Host Mode。
- 建本地测试 CDN 或对象存储：manifest、hash、断点续传、缓存校验与原子切版。
- 启动时保留上一份可用 manifest，下载/校验/加载失败自动回退。
- 区分表现热更与确定性逻辑热更；后者必须改变 logic hash，组局握手拒绝混版本。
- CI 增加 EditMode、Replay Matrix、IL2CPP Build、Bundle Build 和 Hot-update E2E。
- 生成 macOS ARM64 与 Windows x64 Release artifact。

### 验收门槛

- Player v1 构建完成后，仅发布 DLL/资源 v1.1 即可升级逻辑，不重建原生 Player。
- 两客户端 logic/config hash 不一致时，服务端在入局前拒绝。
- 故意损坏 bundle 后自动回退上一 manifest；清缓存后可以完整重下。
- 离线运行内置版本，联网切换 Host 版本。
- 全新构建机从 checkout 到可运行 IL2CPP artifact 全自动完成。

GitHub Actions 所需凭据只放入 repository/environment secrets，遵循最小权限；不要把 Unity License、账号或 CDN 密钥写进仓库。参见 [GitHub Actions secrets](https://docs.github.com/en/actions/reference/security/secrets)。

### 阶段成果

- `v1.0-commercial-pipeline` Release。
- 两平台构建、热更前后视频、损坏回退测试和发布说明。
- 简历表述：打通 HybridCLR + YooAsset + IL2CPP 自动发布链路，支持 metadata、CDN manifest、完整性校验、失败回滚与逻辑版本握手。

## 10. 跨阶段工程规则

每个里程碑都必须满足以下 Definition of Done：

- 新逻辑有确定性单测；影响状态时同步更新 Snapshot/Hash schema。
- 提交 benchmark 原始 JSON，不只放截图或口头 FPS。
- 功能边界在 README 中写清楚，目标值与实测值分栏。
- 录制 30–90 秒演示视频；同时保留无 UI 的自动验证入口。
- Release note 写清硬件、Unity 版本、构建后端、测试时长和 commit。
- 公共仓库不提交 License、账号、CDN Token、签名证书和平台生成缓存。
- 任何性能优化必须同时证明哈希未改变，除非明确升级 logic version。

## 11. 暂不优先的方向

现阶段不建议马上加入 GAS、Motion Matching、Ragdoll 或继续堆更多寻路算法。这些主题本身有价值，但会分散当前项目的核心叙事。先把现有能力做成：

> 跨进程可证明、弱网可恢复、性能可审计、正式包可更新。

完成 M1–M3 后，这个仓库已经能重点冲击大厂客户端底层/网络同步岗位；完成 M4–M6 后，再向引擎性能、GPU Driven 和商业基础设施岗位扩展。

## 12. 建议执行节奏

- 每阶段从一个独立 milestone/feature branch 开始，避免六条路线并发失控。
- 每周至少产出一次可运行 build、一次测试报告和一段短视频。
- 每阶段结束打 GitHub Release，并同步更新 README 的“已完成/未完成”边界。
- 简历只写当前 Release 已通过验收的能力；路线图目标不提前写成结果。

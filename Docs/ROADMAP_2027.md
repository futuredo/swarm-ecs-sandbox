# Swarm-ECS-Sandbox：2027 客户端底层强化路线图

## 1. 路线定位与证据规则

本路线图服务于**客户端底层 / 确定性仿真 / 网络同步**岗位。主线不是继续堆叠算法名词，而是依次补齐：

```text
v0.2.1 Navigation Completeness（当前公开基线）
  → v0.2.2 静态避障正确性
  → v0.3 跨进程确定性与 Desync Diff
  → v0.4 真实 UDP Server + 双 Client
  → v0.5 断线重连、快照与状态修复
  → v0.6 长稳性能证据
  → v0.7 GPU Culling / LOD
  → v0.8 YooAsset + HybridCLR + IL2CPP 发布闭环
```

本文严格区分三类信息：

- **已完成**：当前源代码中存在实现，并有对应测试或仓库内原始结果。
- **本地验证**：开发机运行过，但原始结果只有在附加到 GitHub Release/Actions artifact 后才成为公开证据。
- **目标门槛**：尚未实现的计划值，不能写入简历的“已完成”描述。

分支、工作树或 README 中的版本名不等于公开发布；只有绑定具体 commit 的 Git tag、Release notes 和证据附件才算公开 Release。发布步骤见 [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md)。

## 2. v0.2.1 当前基线

### 已实现：仿真与导航

- Q16.16、fixed-step 30 Hz、自定义 SoA World、稳定 entity id。
- 64×64 八邻接 A*，固定数组 heap/open/closed 与稳定 tie-break。
- `GridIslandMap` 采用与 A* 一致的斜角规则做连通分量预检，blocked 或跨岛请求在展开 A* 前拒绝。
- 4 个群组各有一个固定请求状态槽；新目标会合并该群组尚未处理的目标，跨群组按稳定 `PendingSequence` 消费，默认最多处理 **1 request/tick**。这不是通用的任意容量请求队列。
- `SharedPathCache` 默认 68 项，以 `(start, goal, mapRevision)` 为 key、确定性 round-robin 替换；4 个群组共享宏观路线。
- Uniform Grid radius + bounded top-K、KD-Tree radius、KD-Tree exact KNN 三种模式；radius 查询使用精确 `ulong` raw-square，KNN 使用覆盖完整二维 Q16.16 坐标域且无分配的 65-bit squared distance。
- Bounded-neighbor Agent-Agent RVO2-style ORCA；静态障碍仍由移动后的 fixed-point circle-vs-OBB penetration resolve 兜底。

### 已实现：Rollback 边界

- 位置、速度、群组目标、path cursor、`GroupPathState`、请求序号与 `SpatialIndexMode` 进入 hash/snapshot。
- `SpatialIndexMode` 通过权威命令在指定 tick 生效，rollback 跨越切换点时按原顺序重放。
- 64 tick `WorldSnapshotRing`、按 `(tick, sequence)` 排序的命令时间线、延迟命令 rollback/replay；时间线按最老可恢复 tick 确定性回收过期有序前缀并复用固定容量。
- Late command 在写入 timeline 前预检 origin snapshot；失败不推进 sequence，也不污染命令历史。
- Waypoint/node 数组属于可从 resolved key 重建的派生缓存，不复制进每帧快照。
- Catch-up backlog 期间可跳过中间渲染。
- 地图 topology/revision 是 snapshot 外部 epoch；切换后必须 `ResetHistory()`，当前不支持跨 topology epoch 回滚。

### 当前证据范围

- 2026-07-14 在 v0.2.1 源码上本地运行 Unity 6000.3.9f1 EditMode：**77 / 77 Passed，0 failed/skipped**（0.9254662 秒）。`TestResults/` 默认不入库，XML 作为 Release artifact 附件公开。
- 仓库跟踪的 10k headless 短基准：Apple M5 Pro、8 warmup + 32 sample、Uniform Grid；平均 **19.919240625 ms**、P95 **24.5924 ms**、17.0747/26.0361 ms min/max、current-thread **0 B**。运行时使用 caller + 14 workers，full/canonical hash 均为 `0x4BD5680667C14261`；原始值见 `BenchmarkResults/latest.json`。
- 同配置三模式端到端矩阵：Uniform Grid 18.73074375/21.2342 ms、KD radius 114.1174875/125.944 ms、KD exact KNN 98.874775/108.3109 ms（average/P95）；min/max 分别为 16.8525/22.6702、104.8276/127.9858、90.9214/109.7333 ms，三者 current-thread 均为 0 B。Uniform Grid 使用 caller + 14 workers，KD 当前使用 caller thread；该结果不是隔离 spatial query 的微基准。
- 三模式 full hash 因包含权威 `SpatialIndexMode` 而不同；只归一化该字段后的 canonical hash 均为 `0x4BD5680667C14261`。Grid/KD radius 的一致值证明本次相同 seed/config 下的权威结果等价；KNN 的一致值只描述当前场景，不能泛化为不同选邻语义永远等价。
- 该基准使用 `Null Device`，只测纯逻辑 tick；不证明 GPU 帧率、移动端 60 FPS、10 分钟 P99 或所有 worker 零分配。
- 当前一致性证据来自同一代码/运行环境中的双 World、命令重放和 rollback 测试；尚未证明跨进程、跨后端或跨 CPU 架构逐位一致。

## 3. v0.2.1 发布门禁：Navigation Completeness

v0.2.1 不再扩展新的大算法，目标是形成可审查的公开导航基线。

### 发布前必须完成

- 当前完整 EditMode suite 在待发布 commit 上重新运行并保持 77/77；XML 附加到 Release。发布前若代码再次变化，必须重跑，不能沿用本次结果。
- 10k benchmark 在同一 commit 上重新运行；JSON/Markdown 与 Release notes 写明 Unity、硬件、Graphics Device、warmup/sample 和 commit。
- 对 Uniform Grid radius、KD radius、KD exact KNN 使用相同 seed/config 输出对照结果；结果只描述测得成本，不宣称 KD 稳定 `O(log N)`。
- 明确记录“4 个固定群组槽 + 同组目标合并”的洪峰语义，不能描述成承载任意数量请求的通用队列。
- 至少保留跨岛不可达、地图 revision 失效/重排、动态目标合并、预算 backlog 与 rollback 一致性测试。
- 发布 macOS Universal 验证 Player（Mono、ad-hoc、未 notarize）、SHA-256、30–90 秒演示和 Gatekeeper/已知限制。

## 4. v0.2.2：静态避障正确性（1–2 周）

这是客户端主线的下一步。先解决“单位撞墙后再推出”，再进入跨进程和网络实验。

### 实现范围

- 将静态凸多边形/OBB 的边界转换成稳定排序的 ORCA obstacle lines，并在 Agent-Agent lines 之前写入约束集合。
- 向 LP3 传入真实 `obstacleLineCount`；HUD/测试分别报告 obstacle 与 agent line 数。
- 使用 Uniform Grid、BVH 或等价固定容量结构给静态障碍做 broadphase，取消每 Agent 遍历所有障碍的扩展路径。
- SAT/circle-vs-OBB 保留为最终穿透恢复与断言，不作为常规避墙机制。
- 增加 swept circle-vs-OBB（或等价保守扫掠）处理高速 tunneling。
- 给期望速度增加最大加速度与最大转向速率约束，所有限值进入 logic/config hash。

### 验收门槛

- 走廊对穿、贴墙、墙角、狭窄入口、高速撞墙五类场景均有确定性回归测试。
- 有静态障碍邻居时 `obstacleLineCount > 0`，常规场景不得依赖每 tick penetration push-out 前进。
- 固定 seed 的同环境重复运行 hash 一致；warmup 后新增 broadphase/obstacle-line 热路径 0 B。
- 高速扫掠测试没有穿越静态 OBB；最终 penetration 超过容差时测试失败，而不是静默推出。

## 5. v0.3：跨进程确定性与 Desync Diff（2–3 周）

### 实现范围

- 定义 versioned `.swarmreplay`：magic、schema version、logic/config hash、seed、command stream、checkpoint hash。
- 将导航请求和网络命令 sequence 升级为带 epoch 的序号，或实现经过回绕测试的模序比较；禁止继续依赖普通有符号 `<` 跨越 `int.MaxValue`。
- 建立无渲染 replay runner；相同文件可在独立进程重复执行。
- 建立分层 hash 与 desync diff，定位首个分歧 tick、component、entity id、field 和 raw value。
- 参数化 worker lane 数，覆盖 single-thread 与不同并行分区。
- 为 snapshot/hash/replay schema 建立字段登记表和显式升级规则。

### 验收门槛

- `10 seeds × 10,000 ticks × 1/2/4/8/16 lanes` 的 checkpoint hash 一致。
- 同一 replay 至少覆盖 Editor Mono、macOS IL2CPP ARM64 与 Windows IL2CPP x64；Android ARM64 有真实设备后再加入，不提前宣称。
- 同 tick 命令随机到达顺序经规范排序后结果一致。
- 人为翻转一个权威字段时，一次运行内定位首个 tick/entity/field。
- PR 可运行短矩阵；完整矩阵由有 Unity 环境的自托管构建机或本地发布流程产生 artifact。

## 6. v0.4：真实 UDP Server + 双 Client（3–4 周）

第一版只做 loopback/LAN，不扩展登录、匹配、NAT 穿透、房间服务或反作弊。

### 实现范围

- 一个 headless authoritative session server + 两个独立 client 进程。
- UDP header 包含 protocol/session/peer/sequence/ack/ackBits/tick/channel/length/CRC。
- 命令走可靠有序通道；hash telemetry 可走非可靠通道。
- Client 维护 predicted tick、confirmed tick、input delay、prediction lead 与 rollback depth。
- 网络线程只写 fixed-capacity queue，不直接修改 `SwarmWorld`。
- 握手校验 protocol、logic/config、fixed-point 与 snapshot schema version。
- 内置 latency、jitter、loss、duplicate 与 reorder 模拟。

### 验收门槛

- 自动拉起 `1 server + 2 clients`，在目标弱网配置下运行 30 分钟。
- 所有 confirmed tick 的 authoritative hash 一致，可靠输入最终送达。
- 报告 RTT、带宽、prediction lead、rollback count/depth P50/P95/P99。
- 超出 64 tick 窗口时明确进入 `SnapshotRequired`，不得钳制延迟后继续伪装成功。

## 7. v0.5：断线重连、快照与状态修复（2–3 周）

### 实现顺序

1. 先完成带 schema/logic/config hash 和 CRC 的 versioned Full Snapshot。
2. 完成分片、重传、超窗状态替换与无渲染追帧。
3. 完成断线重连状态机：握手 → full snapshot → 后续命令 → catch-up → 恢复表现。
4. Full Snapshot 稳定后再做 delta、压缩与带宽优化。

### 验收门槛

- 1,000 轮随机断线/重连回归最终回到 confirmed authoritative state。
- 损坏、缺失、重复和乱序分片不会污染已确认状态。
- Full Snapshot 及后续命令绑定同一 protocol/logic/config/schema version。
- 报告 full/delta 体积、传输耗时、追帧耗时和内存峰值，不只展示“重连成功”。

## 8. v0.6：性能与稳定性证据（1–2 周）

- 为 Navigation、Spatial Build/Query、ORCA、Integration、Collision、Snapshot、Hash、Network Queue 增加分阶段 marker。
- 固定机器运行 10 分钟 soak，报告 P50/P95/P99、所有 worker allocation、内存峰值与温控信息。
- 单独报告 18 tick rollback burst、300/600 tick catch-up、full snapshot apply 和 rejoin。
- 建立 Uniform Grid/KD radius/KD exact KNN 同配置矩阵，以及 1/2/4/8/16 lane 对照。
- Burst/Jobs 作为对照后端，不为标签重写现有 SoA 基线。

所有数值先作为目标或实验结果；只有绑定 commit 与原始 JSON/XML/trace 后才进入 README。

## 9. v0.7：GPU Culling 与 LOD（后置，3–4 周）

- Compute per-instance frustum culling、visible index compaction 与 indirect args 生成。
- 高/中/低三档 Agent LOD；第二阶段再加入 depth pyramid + 保守 Hi-Z。
- “10k 完整仿真”和“100k 纯渲染压力”必须分成两套报告。
- 不做 CPU visibility readback；输出 visible/culled/LOD count 与 GPU timing。
- Metal 与 DX12 分别验证 buffer layout、CPU oracle 和截图回归。

该阶段面向渲染/引擎岗位加分，不阻塞客户端网络主线。

## 10. v0.8：YooAsset + HybridCLR + IL2CPP 闭环（后置，2–3 周）

- 干净构建机执行 HybridCLR Installer、Generate/All、AOT metadata 与 hot-update DLL build。
- YooAsset Offline/Host Mode、versioned manifest、bundle hash、断点续传与上一版本回退。
- 确定性逻辑更新必须改变 logic hash；服务器握手拒绝混版本客户端。
- macOS ARM64 与 Windows x64 IL2CPP artifact 绑定 source commit 和依赖版本。
- 故意损坏 bundle/manifest 时能回退上一可用版本；日志不泄露 license/CDN secret。

当前仓库只包含接入边界，不能把本阶段写成已完成的生产热更新管线。

## 11. 暂缓的算法扩展

Flow Field、HPA*、LPA*/D* Lite、运行时动态地图和更多避障算法都保留为扩展支线。除非出现 512×512 大图、32+ 独立目标等实测瓶颈，否则至少等 v0.5 的网络恢复闭环完成后再排期。

现阶段不建议加入 GAS、Motion Matching、Ragdoll，也不建议重写成完整通用 archetype ECS。这些方向会稀释当前作品最有价值的叙事：

> 大规模确定性仿真 → 可复现与可定位 → 真实弱网回滚 → 超窗状态恢复。

## 12. 跨阶段 Definition of Done

每个版本都必须满足：

- 新权威字段同步更新 hash、snapshot、replay/schema version 与 rollback test。
- 原始 JSON/XML/trace 绑定 release commit；截图和口头 FPS 不能替代原始数据。
- 基准写明 hardware、Unity、backend、agent/map scale、warmup/sample/soak 和 graphics device。
- 性能回退与算法结果变化显式记录；逻辑规则改变时升级 logic version。
- Release notes 明确“已完成 / 本地验证 / 尚未完成”，并列出已知限制。
- 每个 milestone 同时交付可运行 build、30–90 秒视频、自动验证入口、架构/协议说明和不超过两行的简历描述。

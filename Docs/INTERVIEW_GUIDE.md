# 面试讲解指南

## 30 秒版本

“我实现了一个不用 Unity Physics/NavMesh 的 10,000 单位确定性沙盒。权威层是 Q16.16 + 自定义 SoA ECS；动态群组目标从消除 formation offset 后的逻辑编队中心发起，进入默认每 tick 一个请求的 A* 调度，并用连通岛预检和固定容量缓存削峰。局部避障支持 Grid radius、KD radius 与覆盖完整 Q16.16 域的 65-bit exact KNN，Uniform Grid 模式通过持久线程池并行。项目还实现了包含路径请求状态和查询模式的 64 tick rollback；模式切换也走可重放的权威命令，late command 在写入 timeline 前先预检快照，时间线回收超出窗口的过期前缀。当前 M5 Pro headless 10k Uniform Grid 平均 19.919240625 ms、P95 24.5924 ms，本地 EditMode 77/77。”

## 3 分钟演示顺序

1. **看 HUD**：指出 10,000 Agents、logic CPU/tick、hot-path GC、`Path req` / pending、cache hit/miss、独立的 `replay A*`、ORCA lines 与 state hash。
2. **按 `K` 三次**：依次解释 Uniform Grid radius、KD-Tree radius、KD-Tree exact KNN；强调 `SetSpatialIndexMode` 会进入当前 tick 的权威命令时间线，在下一 logic step 应用，rollback 跨越切换 tick 时会按原序重放。
3. **按 `L`**：延迟目标命令先用 `WorldSnapshotRing.Contains()` 预检 origin snapshot，再触发 rollback；观察 replay tick，同时说明恢复了 `GroupPathState`、request sequence 与 query mode，waypoint 是派生缓存。
4. **按 `T`**：追 600 tick 时跳过渲染，展示 simulation/presentation 解耦。
5. **看证据文件**：`BenchmarkResults/latest.json`、EditMode XML，以及下面的代码入口。

## 证据地图

| 主题 | 入口 |
|---|---|
| Q16.16 规则 | `Core/FixedPoint/FP.cs`、`FPMath.cs` |
| SoA 与完整 hash | `Simulation/ECS/SwarmWorld.cs` |
| Island / A* / cache | `GridIslandMap.cs`、`AStarPathfinder.cs`、`SharedPathCache.cs` |
| 请求预算 | `GroupPathState.cs`、`SharedPathNavigationSystem.cs` |
| 三种邻域 | `UniformGrid2D.cs`、`DataOrientedKdTree2D.cs`、`NeighborAvoidanceSystem.cs` |
| ORCA | `Avoidance/OrcaSolver.cs`、`Systems/Parallel/AvoidanceWorkerPool.cs` |
| Snapshot / replay | `WorldSnapshotRing.cs`、`RollbackController.cs` |
| GPU 边界 | `Runtime/Rendering/SwarmIndirectRenderer.cs`、`SwarmIndirect.shader` |

## 高频追问

### 为什么手写 ECS，不直接用 DOTS？

这个 Demo 的目的，是把稳定实体、SoA、barrier、容量和 rollback schema 全部暴露出来并可测试。它不是完整 archetype/chunk 框架。生产项目可以保留同一数据模型，再迁移到 Entities/Burst/Jobs；这里先证明对底层约束的理解，而不是证明会调用框架 API。

### 为什么不是 10,000 次 A*？

同群组共享宏观走廊，Agent 只保存 cursor 与 formation offset，局部差异交给 ORCA。Replan anchor 先对所有成员的 `Position - FormationOffset` 求平均，得到不会重复叠加 formation offset 的逻辑编队中心；若该 cell 不可走，再按固定 node 遍历选择最近可走 cell。动态请求进入 4 个固定状态槽，按 sequence 排队，默认 1 request/tick。

### GridIslandMap 带来什么？

它用与 A* 相同的八邻接和禁止斜穿规则做 connected-component labeling。blocked 或跨岛请求无需展开 A* 就能确定不可达。数组预分配，map revision 变化时重建；当前运行时 map 仍是静态的，动态障碍是下一阶段。

### 为什么路径数组不放进 snapshot？

真正决定未来调度的是 `GroupPathState`、pending sequence 和全局 `NextPathRequestSequence`，这些已进入 snapshot/hash。Waypoint/node 数组能从 `(start, goal, mapRevision)` 和确定性 A* 重建；复制整条路径到 64 帧快照只会放大内存。Cache 影响 hit/miss 成本，不允许影响结果。

### 固定容量 cache 如何保证确定性？

Cache key 是 start/goal/revision，替换是 round-robin，没有时间戳或哈希容器迭代。默认 68 项覆盖 4 个 active group 和默认 64 tick rollback window 的常见路径集合。极端淘汰后，rollback 恢复的是 resolved key；系统会同步重建派生 A*，不占 `Path req` 预算，HUD 单列 `replay A*`。测试证明这种 rebuild 为 0 B 且 `LastProcessedPathRequests == 0`。

### 为什么默认 Uniform Grid？KD-Tree 不是 O(log N) 吗？

Agent 全体每 tick 移动、邻居半径固定且分布较密，Grid 的 rebuild 平均 O(N)，查询成本与局部候选数相关。KD-Tree 每 tick 也要重建；radius/KNN 虽可 branch prune，但最坏仍会访问 O(N)，radius 还要排序命中结果。它保留为 radius/KNN 对照，而不是宣传成稳定 O(log N)。

### KD radius 和 exact KNN 有什么语义差异？

Radius 只返回 `NeighborDistance` 内的实体，稀疏区域可能不足 K 个；exact KNN 不受半径限制，会查询 `MaxNeighbors + 1` 个最近实体，过滤 self 后最多保留 `MaxNeighbors`。Radius 距离使用精确 `ulong` raw-square；KNN 用一个高位加 `ulong` 低位表示完整 65-bit 两轴平方和，覆盖任意二维 Q16.16 坐标对，无运行时分配，并用相同宽距离做候选排序和 KD split-plane pruning。极端坐标与错误剪枝回归证明该表示不会在 `ulong` 边界后退化成 entity-id tie-break。最后仍以 entity id 消除真实同距歧义。

### 为什么三模式 full hash 不同，canonical hash 却相同？

Full hash 包含权威字段 `SpatialIndexMode`，所以即使位置、速度、路径状态完全相同，Grid/KD 模式的 full hash 也应该不同。基准额外输出 canonical hash：仅在计算时把这个模式字段归一化为 `UniformGrid`，其余权威状态保持不变，再立即恢复原模式。当前相同 seed/config 下，Grid radius 与 KD radius 的 canonical hash 都是 `0x4BD5680667C14261`，证明本次终态权威结果等价；KD exact KNN 这次也相同，但它的选邻语义不同，这只是当前密集场景的观察，不能推广到所有半径、密度或轨迹。

### 并行 ORCA 为什么仍确定？

每个 Agent 读取上一 tick 的 position/velocity 和稳定排序后的 neighbors，只写自己的 next velocity。每条 lane 有独立 scratch，所有 worker 结束后才积分。线程调度只改变完成时间，不改变单 Agent 输入集合或输出槽。

### ORCA 是否也绕静态障碍？

当前没有 static-obstacle ORCA line。静态 OBB 先进入 A* blocked grid，移动后再做 circle-vs-OBB penetration resolve。ORCA 处理 Agent-Agent 速度约束；项目也没有 Agent-Agent SAT contact solver、动态障碍或 CCD。回答这一点比把所有碰撞都叫“ORCA + SAT”更专业。

### 一次 indirect draw 是否等于完整 GPU Driven？

不是。当前所有 Agent 共用一个 `RenderMeshIndirect` command，vertex shader 从 StructuredBuffer 组装变换；但 CPU 每帧仍上传全部实例，只做整批 bounds culling。没有 per-instance Compute culling、Hi-Z、LOD/HLOD 或 GPU simulation。

### Rollback 为什么新增路径状态？

如果两个客户端的位置相同，但 pending request sequence 或 `SpatialIndexMode` 不同，它们下一 tick 的路径/避障结果就可能不同。因此 resolved/pending `GroupPathState`、全局请求序号和查询模式都进入 hash/snapshot。模式切换本身也是带 tick/sequence 的 `SetSpatialIndexMode` 权威命令，rollback 跨越切换点时会重新应用；恢复后 waypoint cache 可重建，而请求消费顺序必须原样恢复。

### 为什么 late command 不会污染命令历史？

先根据 latency 得到 origin tick，再调用 `WorldSnapshotRing.Contains()` 检查对应环槽的 tick 与 agent count。只有快照可恢复，命令才会插入 timeline 并推进 sequence；history reset、slot 被覆盖或 count 不匹配时直接返回失败。测试明确验证失败前后 `CommandCount` 不变。

### 固定容量 command timeline 长时间运行会满吗？

每次保存 tick `T` 的快照后，Controller 计算仍可恢复的最老 tick，并确定性丢弃 timeline 中更早的有序前缀；保留命令仍按 `(tick, sequence)` 排序，数组原地搬移并复用，不产生托管分配。600 条连续命令、8 tick rollback window、16 项 timeline 容量的双 World 回归验证它不会因累计历史耗尽容量，最终 target/hash 仍一致。它只回收已不可能参与当前 rollback window 的命令。

### 地图 revision 能跨 rollback 吗？

目前不能跨 topology epoch。`GroupPathState` 会保存 path 使用的 map revision，但实际 Grid topology 是 snapshot 外部数据；部署新拓扑后必须调用 `ResetHistory()`，让双方从相同 revision 开启新的 rollback epoch。现有测试只验证新 revision 已生效并重置 history 后，该 epoch 内的 on-time/late replay 收敛。

### 0 B 的范围是什么？

单测在 warmup 后包围 fixed-point simulation、宽距离 KNN、island rebuild、cached/uncached request A*，以及被淘汰派生路径的 replay A* rebuild；后者明确不消耗 request budget。Headless benchmark 也统计 sample managed allocation。这个结论只针对 simulation sampling，不包含 HUD 字符串、Editor、GPU driver 或线程创建阶段。

### 网络和热更新完成到哪一步？

网络目前是本地 command timeline、延迟注入、rollback 与 catch-up，没有真实 transport/server/input confirmation。YooAsset/HybridCLR 已有固定版本、asmdef、collector 和加载入口，但还没有跨平台 IL2CPP + CDN + manifest rollback 的生产闭环。

## 用数据说话

当前记录的 10k Uniform Grid 单次性能结论是：

```text
Unity 6000.3.9f1, Apple M5 Pro, Null Device
10,000 agents, Uniform Grid, warmup 8 / sample 32
Caller + 14 background workers
Path budget 1/tick, cache 68, islands 1, shared waypoints 248
Average 19.919240625 ms, P95 24.5924 ms, min 17.0747, max 26.0361
Current-thread managed allocation 0 B
Full/canonical hash 0x4BD5680667C14261
```

这是短时 headless logic benchmark，不能外推为移动端、渲染 FPS、所有 worker 零分配或长稳 P99；上述数字逐字段来自 `BenchmarkResults/latest.json`。三模式矩阵中，Uniform Grid、KD radius、KD exact KNN 的 average/P95 分别为 18.73074375/21.2342、114.1174875/125.944、98.874775/108.3109 ms，min/max 分别为 16.8525/22.6702、104.8276/127.9858、90.9214/109.7333 ms，三者 current-thread 都是 0 B。Uniform Grid 使用 caller + 14 workers、KD 当前使用 caller thread，所以这是端到端完整运行模式对照，不是孤立查询微基准。三者 full hash 分别为 `0x4BD5680667C14261`、`0xE8AE71279C8EC54C`、`0x008726C93F9563E3`；canonical hash 均为 `0x4BD5680667C14261`，含义与限制见上文。

## 简历措辞参考

> 独立实现 Unity 万单位确定性仿真沙盒：以 Q16.16 与 SoA ECS 构建 30 Hz 权威逻辑，设计逻辑编队中心、连通岛预检、固定预算动态共享 A* 与可重建路径缓存；实现 Uniform Grid / KD radius / 完整 Q16.16 域 65-bit exact KNN 和 bounded-neighbor RVO2-style ORCA，并将路径请求、查询模式及其权威命令纳入 64 tick rollback；command timeline 按回滚窗口无分配回收过期前缀。Apple M5 Pro headless 10k Uniform Grid 当前记录平均 19.919240625 ms/tick、P95 24.5924 ms、current-thread 0 B；本地 EditMode 77/77，公开状态以 Release XML 为准。

不要添加“跨平台零误差”“移动端 60 FPS”“生产级网络”“完整 GPU culling”或“已上线热更”——这些尚未通过对应验收。

# 架构与数据流

## 1. 分层原则

项目把权威仿真与 Unity 表现严格分开：

- `SwarmECS.Core`：Q16.16、向量、确定性工具，无 `UnityEngine`。
- `SwarmECS.Simulation`：SoA World、寻路、空间查询、ORCA、碰撞和 rollback state，无 `UnityEngine`。
- `SwarmECS.Runtime`：输入、HUD、相机、GPU buffer 与商业加载边界。
- `SwarmECS.Editor`：场景生成、测试、benchmark、YooAsset / HybridCLR 配置。

仿真以固定 30 Hz 推进。渲染可以使用 `float` 和可变帧率，但表现值不会回写权威世界。

## 2. 一个 logic tick

`RollbackController.Step()` 先保存 tick 起点快照，再应用该 tick 的命令，然后执行固定 System 顺序：

```mermaid
flowchart LR
    A["Save snapshot"] --> B["Apply ordered commands"]
    B --> C["Detect path requests"]
    C --> D["Process fixed A* budget"]
    D --> E["Prepare derived shared paths"]
    E --> F["Preferred velocity"]
    F --> G["Grid radius / KD radius / KD KNN"]
    G --> H["Agent-Agent ORCA LP"]
    H --> I["Fixed-point integration"]
    I --> J["Static circle-OBB resolve"]
    J --> K["Advance tick"]
    K -. "presentation only" .-> L["GPU upload + RenderMeshIndirect"]
```

所有 Agent 的 ORCA 都读取本 tick 开始时的 position / velocity，只写独占的 `NextVelocities[i]`；worker barrier 完成后才统一积分，因此线程完成顺序不会形成读后写依赖。

## 3. SoA World 与状态分类

### Agent 组件列

```text
Positions[]           FPVector2
Velocities[]          FPVector2
PreferredVelocities[] FPVector2
NextVelocities[]      FPVector2
FormationOffsets[]    FPVector2
Radii[]               FP
MaxSpeeds[]           FP
Groups[]              byte
PathCursors[]         ushort
```

实体 ID 为 `Index + Generation`。数组固定容量、不在运行中 resize；热 System 顺序扫描组件列。

### 权威、静态与派生数据

| 类别 | 代表字段 | 处理方式 |
|---|---|---|
| 动态权威状态 | tick、position、velocity、group target、path cursor、`GroupPathState`、`NextPathRequestSequence`、`SpatialIndexMode` | 进入 hash；未来会影响 replay 的字段进入 snapshot |
| 初始化后静态数据 | seed、group、radius、max speed、formation offset、当前静态 map/config | 由相同 seed/config 构建，运行时不修改 |
| 派生热数据 | preferred / next velocity | 每 tick 重算，不进 snapshot |
| 派生路径缓存 | `SharedPath[]` 的 node/waypoint、`SharedPathCache` 内容 | 从 authoritative key + deterministic map/A* 重建，不复制进每帧 snapshot |
| 表现数据 | HUD 字符串、camera、GPU upload buffer | 不参与权威结果 |

这里的 ECS 是为解释数据布局和确定性边界而手写的最小框架，不是完整的 archetype/chunk Unity Entities 替代品。

## 4. 动态共享寻路调度

### 4.1 Grid 与连通岛

导航图是 64×64、八邻接 `GridMap`。静态 OBB 先按 Agent clearance 栅格化为 blocked cell，再用整数权重核为邻近 walkable cell 添加惩罚。A* 禁止从两个 blocked cardinal cell 之间斜穿。

`GridIslandMap` 使用完全相同的八邻接与 diagonal corner rule 做 connected-component labeling：

- region seed 按 row-major 顺序扫描，region id 可重复验证；
- `_regionIds` 与 flood queue 在构造时一次分配；
- `GridMap.Revision` 改变时延迟重建；
- blocked、越界或跨岛请求在 A* 前直接标记为 `Unreachable`。

### 4.2 固定预算请求队列

每个群组有一个 `GroupPathState`，同时保存最近 resolved 结果和最多一个 pending 请求：

```text
ResolvedStart / Goal / MapRevision / Status
PendingStart / Goal / MapRevision / Sequence
```

每个 tick 的调度流程是：

1. 比较当前 group target 与 resolved/pending key。
2. 目标变化时，对该组所有成员的 `Position - FormationOffset` 求定点数平均，得到逻辑编队中心；若中心 cell 不可走或越界，按 raw-distance square、再以较小 node id 隐式 tie-break 稳定选择最近可走 cell 作为 anchor。
3. 从所有 pending group 中选择序号最早者。
4. 默认最多处理 `1 request/tick`；构造器允许显式配置其他固定预算。
5. 先检查 walkability / island connectivity，再查询 cache 或执行 A*。
6. 写回 `Active` / `Unreachable`，并重置该群组的 path cursor。

当前只有 4 个群组，因此“队列”体现在 4 个固定状态槽中，不需要每 tick 创建 request object 或动态容器。

### 4.3 SharedPathCache

`SharedPathCache` 默认固定 68 个 entry（4 个群组 + 默认 64 tick rollback window），每个 entry 在初始化时预分配到 `GridMap.NodeCount`：

- key：`startIndex + goalIndex + mapRevision`；
- hit：复制进对应群组的 reusable `SharedPath`；
- miss：运行 allocation-free A*，成功后写入 cache；
- eviction：确定性 round-robin，常数时间选择替换槽。

Cache 内容和 replacement cursor 不属于权威状态。原因是 hit 与 miss 都必须得到同一条确定性路径；cache 只改变计算成本。Rollback 后如果 waypoint buffer 指向未来状态，`PrepareDerivedPaths()` 会根据已恢复的 `GroupPathState` key 从 cache 复制。默认 68 项覆盖 4 个 active group path 与 64 tick 窗口的常见恢复集合；极端淘汰导致 derived cache miss 时，系统会**同步重建 A***，以便本 tick 立即使用已恢复的权威 path state。

这类 `DerivedAStarRebuilds` 不代表新的 gameplay path request，也不消耗 `MaxPathRequestsPerTick`；HUD 因此把正常调度写成 `Path req`，并单列 `replay A*`。同步重建仍复用预分配 A* storage，已有 cache-eviction rollback 的 0 B 测试。

### 4.4 A* 复杂度边界

A* 使用 binary heap，标准上界为 `O((V + E) log V)`、空间 `O(V)`；当前 `V=4096`、每节点最多 8 条边。10,000 个 Agent 不各自运行 A*，而是 4 个群组共享宏观路径，Agent 只保留 `ushort PathCursor` 与 formation offset。

## 5. 三种邻域查询

| 模式 | 行为 | 当前执行路径 | 复杂度边界 |
|---|---|---|---|
| Uniform Grid radius | 扫描覆盖 cell，维护有序 bounded top-K | 默认；持久 worker pool 并行 | hash build 平均 `O(N)`；query 与访问候选数相关，极端密集最坏 `O(N)` |
| KD-Tree radius | `ulong` raw-square branch-pruned 半径搜索，按距离/id 排序 | 单线程对照 | balanced tree 可剪枝，但最坏访问 `O(N)`；m 个命中排序 `O(m log m)` |
| KD-Tree exact KNN | 65-bit raw-square 查询 `MaxNeighbors + 1`，过滤 self 后最多保留 `MaxNeighbors` | 单线程对照 | exact branch-pruned KNN，最坏仍可能访问 `O(N)` |

KD radius 在 raw integer space 使用精确的单轴 `ulong` square；因为非负 FP radius 的平方最多是 `int.MaxValue²`，二维距离超过 `ulong` 时必然已经在半径外，所以饱和不会改变过滤结果。Exact KNN 不能采用这个捷径：任意两点的二维 Q16.16 raw-square 最多需要 65 位，因此实现以 `1-bit carry + ulong low` 保存完整距离，候选排序与 split-plane pruning 都使用该宽距离，最后只用 entity id 消除真实同距歧义。极端坐标和 far-branch 回归覆盖了 `ulong` 边界；但 KD-Tree 仍不能被简化宣传成“稳定 O(log N)”。

`SpatialIndexMode` 属于权威 simulation configuration，会进入 hash/snapshot；Avoidance 每 tick 从 World 读取该模式。Host 的 `K` 键只负责计算下一模式并调用 `QueueSpatialIndexMode()`，将 `SimulationCommandType.SetSpatialIndexMode` 以当前 tick 和稳定 sequence 排入同一 `CommandTimeline`。它在下一次 logic step 的 command phase 生效；rollback 即使跨越切换 tick，也会按原 `(tick, sequence)` 重新应用该命令，因此不需要清空 history。

Late authority 注入也遵守事务边界：网络层把服务器给出的完整 `SimulationCommand` 传给 `InjectLateCommand()`，原始 `(tick, sequence)` 不会按抵达顺序重分配。`RollbackController` 先调用 `WorldSnapshotRing.Contains()`，确认 origin tick 的 slot 和 agent count 仍可恢复，之后才把命令插入 timeline。快照缺失（例如 history 已重置或环槽已不匹配）时直接拒绝，既不污染 command history，也不推进本地演示 sequence。每次保存 tick `T` 的快照后，Controller 会原地丢弃早于 `T - HistoryLength + 1` 的有序命令前缀；最深可恢复 tick、当前 tick 与未来命令仍保留，因此固定容量服务于 rollback window，而不会随进程累计历史永久耗尽。Host 的 `InjectLocallyGeneratedLateGroupTarget()` 只用于按键模拟延迟，不是网络接收 API。

## 6. ORCA 并行与碰撞边界

- ORCA 只从选中的 **Agent neighbors** 生成 half-plane；默认最多 8 条。
- 修正量的一半由当前 Agent 承担，LP1 / LP2 / LP3 在定点数空间求解安全速度。
- 完全重叠时用 stable entity id 构造可复现的反对称逃逸方向。
- Uniform Grid 模式按连续 entity range 分给主线程和持久 worker；每条 lane 有独立 query / neighbor / line scratch。
- KD radius 与 KD exact KNN 当前保持单线程，便于暴露重建和 traversal 的真实成本。

ORCA 当前没有 static-obstacle line。静态障碍先影响 A* grid，移动后再做 circle-vs-OBB 离散穿透修正。碰撞库另有 OBB-vs-OBB SAT，但运行时 Agent 不是 OBB；项目也没有 Agent-Agent contact solver 或 CCD。

## 7. 渲染边界

`SwarmIndirectRenderer` 在表现帧中遍历全部 Agent，将 fixed-point raw 转成 `float` 并上传 `GraphicsBuffer`。Vertex shader 依据 `SV_InstanceID` 读取位置、速度、group 和 radius，在 GPU 组装实例变换。

- 全部 Agent 使用一个 `Graphics.RenderMeshIndirect` command。
- 地面和静态障碍有独立 draw，不能称“全场景一 draw call”。
- 当前仅通过一个 swarm `worldBounds` 做整批 culling。
- 没有 ComputeShader visibility list、per-instance frustum/occlusion、Hi-Z、GPU simulation 或 HLOD。

## 8. 内存与 GC

组件列、空间索引、A* open/closed/heap、island flood queue、共享路径/cache、ORCA scratch、命令时间线和 snapshot ring 都在初始化时分配。热循环不使用 LINQ、装箱、闭包或容量增长。

10,000 Agent × 64 tick 的 snapshot ring 主要保存 position、velocity 与 path cursor，原始数组约 11 MiB，再加少量 group target / path state 元数据。它用内存换取 `tick % historyLength` 的 O(1) slot 定位；当前没有 delta compression。

# 面试讲解指南

## 30 秒项目介绍

“我实现了一套不用 Unity Physics/NavMesh 的 10,000 单位确定性沙盒。权威逻辑是 Q16.16 + 自定义 SoA ECS；共享 A* 负责宏观路径，Uniform Grid/KD-Tree 找邻居，手写 RVO2 ORCA 做局部避障，SAT 处理静态障碍。ORCA 通过持久线程池并行，热路径 0 B GC；表现层用一次 indirect instancing 绘制所有单位。项目还做了 64 帧 rollback、延迟包重演和无渲染追帧，并接好了 YooAsset/HybridCLR 边界。”

## 建议演示顺序

1. 先让面试官看 HUD：10,000 Agents、CPU/tick、0 B、80,000 ORCA lines、状态哈希。
2. 按 `K` 对比 Uniform Grid / KD-Tree，解释为什么密集均匀群体默认用 Grid。
3. 按 `L` 注入延迟指令，展示回滚次数与 replay tick。
4. 按 `T` 追 600 tick，解释为何 backlog 阶段跳过渲染。
5. 打开 `SwarmWorld`、`NeighborAvoidanceSystem`、`OrcaSolver`、`WorldSnapshotRing` 和 `SwarmIndirectRenderer`，对应数据、算法、网络、渲染四条证据链。

## 高频追问与回答要点

### 为什么不直接用 Unity DOTS？

目标是证明我理解稳定实体、SoA、缓存局部性、系统 barrier 和内存生命周期，而不是只会调用框架 API。生产项目可以把相同数据模型迁移到 Entities/Burst/Jobs；这个 Demo 的边界更小，更容易验证每一个分配与排序规则。

### 为什么 Uniform Grid 比 KD-Tree 更适合作为默认？

单位半径和邻居距离固定、分布密集、每 tick 全体移动。Grid 的 rebuild 是线性的，查询只扫局部 cell；KD-Tree 每 tick 重建和平衡的常数更高。KD-Tree 仍保留作非均匀分布和 KNN 的对照，不把任何结构宣传成全场景最优。

### 并行后怎么保证确定性？

每个 Agent 的 ORCA 只读取上一 tick 的 immutable position/velocity，邻居有稳定排序，并写独占的 `NextVelocities[i]`。区间划分和线程调度不会改变输入集合或写入顺序；所有 worker 完成后才积分。因此线程数影响耗时，不影响 raw 结果。

### 为什么共享 A*，会不会所有单位走成一团？

同群组共享宏观走廊，个体目标叠加 formation offset，局部冲突交给 ORCA。这样把 10,000 次重复 A* 降为四条路径，同时保留微观差异。若做 RTS 商业项目，可进一步加入 flow field、分层寻路、路径走廊和拥堵代价反馈。

### ORCA 有哪些工程问题？

邻居截断、时间视野和半径会影响稳定性与吞吐；高密度下约束可能不可行，需要 LP3 找最小违反速度；完全重叠必须用稳定 ID 构造反对称逃逸方向。ORCA 也不能替代全局寻路，狭窄通道仍需要队列/拥堵策略。

### Rollback 为什么保存这些字段？

保存会影响未来状态的动态权威数据：位置、速度、路径游标和群组目标；preferred/next velocity 每 tick 会从这些字段重算。恢复过去快照后按 command timeline 重演。完整状态哈希包含 Seed 和 PathCursor，避免只比较画面位置漏掉未来分歧。

### 0 B 是怎么证明的？

单元测试在 warmup 后用 `GC.GetAllocatedBytesForCurrentThread()` 包围逻辑 step；headless benchmark 对 32 个采样 tick 统计同一指标并提交 JSON。它描述的是 simulation hot path，不把 HUD 字符串或 Editor/GPU 驱动分配混进结论。

### YooAsset / HybridCLR 做到了哪一步？

仓库固定官方 tag，配置了热更 asmdef、YooAsset package/collector、补充元数据加载和 DLL 加载边界；平台专属 libil2cpp、Generate/All、Bundle/CDN 发布必须在目标构建机执行。回答时明确区分“工程接入”和“已经上线的完整发布平台”。

## 数据驱动的性能讨论

当前 Apple M5 Pro headless 实测为 24.123 ms/tick、P95 29.366 ms、0 B，满足 30 Hz 的 33.333 ms 逻辑预算。不要把这组数据外推为“千元机 60 FPS”。下一轮优化应通过 profiler/benchmark 决定，例如：

- Burst/Jobs 或 NativeArray 迁移，比较手写线程池的收益与维护成本
- Grid cell occupancy 分布与热点 cell 分片
- ORCA 邻居上限/时间视野的质量—性能曲线
- GPU culling、Hi-Z 与 LOD（当单位 mesh 不再是简单三角形时）
- snapshot delta、压缩和后台序列化

## 可继续扩展的作品集路线

1. 双进程帧同步：真实 UDP/KCP、输入确认、丢包与抖动模拟、desync dump。
2. 分层寻路：cluster graph + flow field + dynamic cost。
3. GPU Driven：Compute Shader culling、prefix sum、indirect args、Hi-Z。
4. 战斗层：定点数 Ability/Effect/Tag、事件确认帧与表现层 Cue。
5. 商业发布：CI 安装 HybridCLR、YooAsset 构建、CDN manifest、灰度与回滚。

扩展时继续保留“可复现 benchmark + 状态哈希 + 自动化测试”，比单纯增加功能数量更有说服力。

# Changelog

本文件记录公开版本的可验证变化。未绑定 Git tag 和 GitHub Release 的内容统一保留在 `Unreleased`，不能当作已公开发布能力。

## [Unreleased]

- 暂无。

## [0.2.1] - 2026-07-14

#### Added

- `GridIslandMap` 连通分量标记，以及 blocked/cross-island 请求的 A* 前置拒绝。
- 4 个群组固定请求状态槽、稳定 `PendingSequence` 消费、每 tick 固定预算和同组未处理目标合并。
- `SharedPathCache` 固定容量、确定性 round-robin 替换，以及 rollback 后派生路径重建。
- Uniform Grid radius、KD-Tree radius、KD-Tree exact KNN 三种可切换邻域模式。
- `SpatialIndexMode` 权威命令、snapshot/hash 覆盖，以及跨切换 tick 的 rollback 重放。
- Grid Island、导航调度/缓存、宽距离 exact KNN 与命令回滚回归测试。
- 固定容量 command timeline 的 rollback-window 前缀回收，以及连续 600 条命令的确定性回归。

#### Changed

- 群组重规划 anchor 改为 `Position - FormationOffset` 的定点数平均逻辑中心，避免 formation offset 重复叠加。
- KD radius 距离使用精确 `ulong` raw-square；exact KNN 改用覆盖完整二维 Q16.16 坐标域、无分配的 65-bit squared distance，避免两轴和溢出或饱和破坏 KNN 排序与剪枝。
- Benchmark/HUD 增加 path budget、cache、island、shared waypoint 与空间索引信息。
- Player `bundleVersion` 更新为 `0.2.1`。
- 路线图调整为客户端优先：静态避障正确性 → 跨进程确定性 → 真实 UDP → 重连/快照 → 性能、渲染和热更新。

#### Evidence boundary

- 2026-07-14 在 v0.2.1 源码上本地运行 Unity 6000.3.9f1 EditMode：77/77 Passed，0 failed/skipped。`TestResults/editmode.xml` 默认不入库，正式发布时作为 Release artifact 上传；耗时以附件中的对应运行记录为准。
- 仓库跟踪的 `BenchmarkResults/latest.json` 为 Apple M5 Pro、10,000 Agent、8 warmup + 32 sample、Null Device 的短时纯逻辑结果：Uniform Grid 平均 19.919240625 ms、P95 24.5924 ms、17.0747/26.0361 ms min/max、current-thread 0 B，full/canonical hash 均为 `0x4BD5680667C14261`。
- `BenchmarkResults/spatial-index-matrix.json` 在相同 formation seed/config 下对照三个完整模式：Uniform Grid 18.73074375/21.2342 ms、KD radius 114.1174875/125.944 ms、KD exact KNN 98.874775/108.3109 ms（average/P95）；min/max 分别为 16.8525/22.6702、104.8276/127.9858、90.9214/109.7333 ms。Uniform Grid 使用 caller + 14 workers，KD 当前使用 caller thread，因此这是端到端 runtime-mode 对照，不是隔离查询结构的微基准。
- 三模式 full hash 分别为 `0x4BD5680667C14261`、`0xE8AE71279C8EC54C`、`0x008726C93F9563E3`，差异来自 full hash 包含权威 `SpatialIndexMode`；只归一化该字段后的 canonical hash 均为 `0x4BD5680667C14261`。Grid/KD radius 的一致值证明本次基准输入下权威结果等价；KNN 的一致值仅是本场景观察，不能推广到所有输入。
- 上述短采样不证明渲染 FPS、移动端表现、长稳 P99 或所有 worker 零分配。
- 当前一致性证据仍限于同一代码/运行环境；跨进程、跨后端与跨 CPU 架构验证尚未完成。
- 地图 topology/revision 尚未进入 snapshot；切换拓扑后依赖 `ResetHistory()` 开启新的 rollback epoch，不能跨 topology epoch 回滚。
- 32-bit 导航/本地命令 sequence 尚未实现跨 `int.MaxValue` 的模序比较；长期在线协议将在 v0.3 引入 epoch 或 serial-number arithmetic。
- 当前避障只有 Agent-Agent ORCA；静态障碍仍使用移动后 circle-vs-OBB 穿透修正，没有 obstacle lines、broadphase 或 CCD。

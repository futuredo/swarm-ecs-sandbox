# v0.2.1 发布检查清单

本清单用于把本地 `Navigation Completeness` 候选变成可公开审查的 Release。它不要求付费服务，也不依赖 CI 中存在 Unity License；Unity 测试和 Player 构建可以在本地已激活的 Unity 6000.3.9f1 完成，再把原始证据附加到 Release。

## 1. 冻结范围与 Git 真值

- [ ] 确认目标版本为 `v0.2.1`，不混入 v0.2.2 的 obstacle ORCA/CCD 实验。
- [ ] `git status --short` 中每个文件都属于本次发布，未误带 Library、Temp、Logs、Builds、TestResults 或私密配置。
- [ ] 检查新增 Unity asset 与对应 `.meta` 成对存在。
- [ ] 确认 `ProjectSettings/ProjectVersion.txt` 为 Unity 6000.3.9f1，`Packages/manifest.json` 依赖仍使用固定版本。
- [ ] 确认 `ProjectSettings/ProjectSettings.asset` 的 `bundleVersion` 为 `0.2.1`。
- [ ] 记录待发布 commit：`git rev-parse HEAD`。所有测试、benchmark、Player 和 Release notes 必须引用同一 commit；如果验证后代码变化，则全部重跑。

## 2. 无 Unity License 的静态检查

以下检查可在本地或默认 GitHub Actions 中执行：

```bash
find Assets Packages -type f \( -name '*.json' -o -name '*.asmdef' \) -print0 \
  | xargs -0 -n1 jq empty

git ls-files | grep -E '^(Library|Temp|Obj|Build|Builds|Logs|UserSettings|MemoryCaptures|Recordings|TestResults)/' \
  && exit 1 || true

jq -e '
  .unityVersion == "6000.3.9f1" and
  .agents == 10000 and
  .sampleTicks > 0 and
  .averageMilliseconds > 0 and
  .p95Milliseconds > 0 and
  (.stateHash | startswith("0x")) and
  (.canonicalSpatialComparisonHash | startswith("0x"))
' BenchmarkResults/latest.json

jq -e '
  .unityVersion == "6000.3.9f1" and
  .agents == 10000 and
  .runOrder == ["UniformGrid", "KdTreeRadius", "KdTreeKNearest"] and
  (.results | length) == 3 and
  ([.results[].spatialIndex] == .runOrder) and
  (all(.results[];
    .sampleTicks > 0 and
    .averageMilliseconds > 0 and
    .p95Milliseconds > 0 and
    (.stateHash | startswith("0x")) and
    (.canonicalSpatialComparisonHash | startswith("0x"))
  )) and
  (.results[0].canonicalSpatialComparisonHash == .results[1].canonicalSpatialComparisonHash)
' BenchmarkResults/spatial-index-matrix.json
```

- [ ] GitHub `Static project validation` 通过。
- [ ] 下载 `release-evidence-<commit>` artifact，确认其中 benchmark 与文档属于待发布 commit。
- [ ] 不把绿色静态 job 描述成“Unity EditMode 已在 CI 通过”。

## 3. Unity EditMode 证据

在项目根目录运行：

```bash
mkdir -p TestResults
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

- [ ] `TestResults/editmode.xml` 的 `result="Passed"`、`passed="77"`、`failed="0"`、`skipped="0"`，且 duration 为 `0.9254662`、用例数与 README 一致。
- [ ] 检查 `editmode.log` 没有编译错误、未处理异常或测试发现失败。
- [ ] 保留 XML 和 log，不提交到仓库；Release 时作为原始附件上传。
- [ ] Release notes 标注这是本地 Unity 结果还是可选 GameCI 结果，不混淆两者。

## 4. 同 commit Benchmark

```bash
SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"

SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
./Scripts/run-spatial-index-benchmark-matrix.sh
```

- [ ] `latest.json` / `.md` 与 `spatial-index-matrix.json` / `.md` 数值一致并进入发布 commit。
- [ ] 记录 Unity、CPU、logical cores、Graphics Device、agent count、spatial mode、warmup/sample、path budget/cache 和 state hash。
- [ ] 明确 `Null Device` 只证明纯逻辑 tick，不换算成渲染 FPS 或移动端结论。
- [ ] 把两个 benchmark log 作为 Release 附件，不提交仓库。
- [ ] 三模式矩阵保持相同 formation seed、fixed delta、neighbor distance、max neighbors、warmup/sample；Release notes 明确 Uniform Grid 使用 caller + 14 workers，而两个 KD 模式当前使用 caller thread。
- [ ] 把三模式结果描述成完整 runtime-mode 对照，不描述成孤立 spatial-query 微基准；full hash 包含 `SpatialIndexMode`，跨模式不同是预期行为。
- [ ] 核对 Grid radius 与 KD radius 的 canonical hash 均为 `0x4BD5680667C14261`，说明本次相同输入下其余权威状态等价；KD exact KNN 此次也相同只作为场景观察，不写成不同选邻语义对所有输入都等价。
- [ ] 确认测试覆盖完整二维 Q16.16 域的无分配 65-bit exact KNN，以及 600 条命令下按 rollback window 回收 timeline 过期前缀。

## 5. Player 与演示

- [ ] 用待发布 commit 构建 macOS Universal Player（Mach-O `x86_64 + arm64`）；记录 Mono backend 与是否为 Development Build。
- [ ] 启动验证场景，至少演示三种空间模式切换、late-command rollback、catch-up、跨岛不可达与动态目标合并。
- [ ] 运行 5–10 分钟，检查 Unity Player log 无异常退出、重复报错或持续 GC 警告。
- [ ] 压缩 Player，并生成 `shasum -a 256 <archive>`。
- [ ] 用 `file` 检查 Universal Mach-O 架构，用 `codesign -dv --verbose=4 <App>` 记录 ad-hoc 签名状态。
- [ ] Release notes 明确 Player 为 ad-hoc 签名且**未 notarize**；列出 macOS Gatekeeper 可能拦截下载包的限制，不描述成正式发行签名包。
- [ ] 录制 30–90 秒视频；HUD 数值与 Release notes 使用同一版本。

Player smoke test 只证明该构建可启动和交互，不等于跨平台确定性、正式弱网或长稳性能验收。

## 6. 文档口径检查

- [ ] README 的“已实现”都能指向代码和测试；未来目标只存在于 ROADMAP。
- [ ] 明确请求调度是 4 个群组固定槽与同组目标合并，不称为任意容量通用队列。
- [ ] 明确 10,000 Agent 共享 4 条宏观路线，不写成 10,000 次独立 A*。
- [ ] 明确 ORCA 当前只有 Agent-Agent lines；静态墙体 obstacle lines、broadphase、CCD 未完成。
- [ ] 明确 indirect rendering 仍由 CPU 上传全部实例，没有 per-instance GPU culling、Hi-Z 或 HLOD。
- [ ] 明确当前没有真实 UDP Server、双 Client、跨进程 replay、Desync Diff、超窗快照恢复或生产热更新闭环。
- [ ] 明确地图 topology/revision 尚未进入 snapshot，切换拓扑后必须 `ResetHistory()` 开启新 epoch，不能跨 topology epoch rollback。
- [ ] 明确 32-bit sequence 的 `int.MaxValue` 回绕尚未实现模序比较，不能把当前短时实验室直接描述为长期在线协议。
- [ ] 不写“KD 查询稳定 O(log N)”“移动端 60 FPS”“跨平台零误差”“生产级网络”等未验收结论。

## 7. GitHub Release

- [ ] 将 `CHANGELOG.md` 中候选条目提升为 `## [0.2.1] - YYYY-MM-DD`。
- [ ] 创建 annotated tag `v0.2.1`，确保 tag 指向已验证 commit。
- [ ] Release notes 包含：目的、已实现、运行方式、实测表、证据环境、已知限制和下一阶段 v0.2.2。
- [ ] 上传 `editmode.xml`、`editmode.log`、`latest.json`、`latest.md`、`spatial-index-matrix.json`、`spatial-index-matrix.md`、两个 benchmark log、Player archive、SHA-256 和演示视频/链接。
- [ ] 从干净临时目录克隆 tag，按照 README 至少完成一次打开/运行或静态复核。
- [ ] 发布后检查 GitHub 默认分支 README、Release tag、附件与下载链接一致。

## 8. 发布后回退条件

发现以下任一情况时先标记 Release 为 pre-release 或撤下二进制，修复后以新 patch 版本重新发布，不移动已有 tag：

- 证据或 Player 并非来自 tag commit；
- 依赖无法从固定版本恢复；
- 测试 XML 与 README 数量/结论不一致；
- benchmark JSON 与 Markdown 不一致；
- 二进制 SHA-256 不匹配；
- 文档把未完成能力写成已经验收。

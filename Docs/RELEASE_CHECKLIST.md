# v0.4.0 release checklist

This checklist binds source, tests, benchmarks, replay output and Player artifacts to one immutable commit. Unity tests and Player builds may run on a locally activated Unity 6000.3.9f1 installation; the default hosted workflow performs static validation only.

## 1. Freeze scope and source identity

- [ ] Confirm every `git status --short` entry belongs to v0.4.0.
- [ ] Confirm every new Unity asset has its matching `.meta` file.
- [ ] Reject generated `Library`, `Temp`, `Obj`, `Builds`, `Logs`, `UserSettings`, `TestResults`, HybridCLR local data and private credentials.
- [ ] Confirm `ProjectSettings/ProjectVersion.txt` records Unity 6000.3.9f1 and package dependencies remain pinned.
- [ ] Confirm `bundleVersion` and release documentation use `0.4.0`.
- [ ] Record `git rev-parse HEAD`; rerun every artifact-producing step after any source change.

## 2. Static validation

```bash
find Assets Packages -type f \( -name '*.json' -o -name '*.asmdef' \) -print0 \
  | xargs -0 -n1 jq empty

git ls-files | grep -E \
  '^(Library|Temp|Obj|Build|Builds|Logs|UserSettings|MemoryCaptures|Recordings|TestResults)/' \
  && exit 1 || true

jq -e '
  .unityVersion == "6000.3.9f1" and
  .agents == 10000 and
  .warmupTicks == 8 and
  .sampleTicks == 32 and
  .graphicsDevice == "Null Device" and
  .averageMilliseconds > 0 and
  .p95Milliseconds > 0 and
  (.configHash | test("^0x[0-9A-F]{16}$")) and
  (.stateHash | test("^0x[0-9A-F]{16}$")) and
  (.canonicalSpatialComparisonHash | test("^0x[0-9A-F]{16}$"))
' BenchmarkResults/latest.json
```

- [ ] GitHub `Static project validation` passes.
- [ ] Download `release-evidence-<commit>` and verify that its documents/results belong to the frozen commit.
- [ ] Do not describe a green static job as a hosted Unity test run.

## 3. Unity EditMode evidence

```bash
mkdir -p TestResults
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

- [ ] XML reports `Passed` with zero failed/skipped cases.
- [ ] Log has no compilation error, unhandled exception or test-discovery failure.
- [ ] Coverage includes fixed-point math, navigation/islands/cache, three spatial modes, obstacle ORCA, immutable BVH, high-speed CCD, SAT fallback, motion limits, rollback, replay/desync diagnostics, packet/message codecs, serial arithmetic, reliable windows, bounded queues, weak-network scheduling, ordered command buffers and `SnapshotRequired`.
- [ ] Keep raw XML/log outside Git history; they may contain user paths, host names and network details.
- [ ] Generate a machine-readable test summary and privacy-scrubbed excerpt for public attachments.
- [ ] Release notes state whether the result came from local Unity or the optional licensed workflow.

## 4. Same-commit benchmarks

```bash
SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"

SWARM_AGENT_COUNT=10000 SWARM_WARMUP_TICKS=8 SWARM_SAMPLE_TICKS=32 \
./Scripts/run-spatial-index-benchmark-matrix.sh
```

- [ ] `latest.json/.md` and `spatial-index-matrix.json/.md` contain matching values and are committed.
- [ ] Record Unity, CPU, logical cores, Graphics Device, workload, spatial mode, worker policy and timing distribution.
- [ ] Validate `ConfigHash`, full hash and canonical comparison hash formats.
- [ ] Validate obstacle/Agent ORCA, avoidance/collision BVH, CCD, SAT fallback, residual depth and motion-limit counters.
- [ ] Keep short default and long obstacle-approach runs separate; do not compare their timings as the same workload.
- [ ] State that `Null Device` measures logic only and allocation covers only the sampling thread.
- [ ] Keep raw benchmark logs local; publish tracked JSON/Markdown and only privacy-scrubbed excerpts when needed.

## 5. Replay and desync evidence

```bash
./Scripts/run-cross-process-replay.sh
```

- [ ] Script writes `ReplayResults/cross-process.swarmreplay`, `capture.json`, `verify.json` and `latest.md`.
- [ ] `capture.json.replaySha256` matches the replay file and `verify.json.replaySha256`.
- [ ] Capture/verify process IDs differ and `independentProcess` is `true`.
- [ ] `crcAndSchemaValidated` and `allCheckpointsMatched` are `true`; matched checkpoint count equals the capture count.
- [ ] Logic/config hashes and final layered hashes agree between capture and verify reports.
- [ ] The desync probe identifies `AgentPositions`, entity `0`, field `X.Raw`, with distinct expected/actual raw values.
- [ ] Compare every recorded checkpoint and retain the tracked machine-readable output.
- [ ] Keep capture/verify logs local unless a privacy scan confirms a scrubbed copy is safe to publish.
- [ ] Corrupt header, length, enum/config identity, payload and checksum in tests; every malformed input must fail before simulation mutation.
- [ ] Deliberately mutate one authority scalar and confirm the diagnostic reports the expected component, entity/group, field and raw values.
- [ ] Do not claim backend/architecture compatibility unless the same replay artifact was executed on every named target.

## 6. Authoritative UDP evidence

Build the Player, then run:

```bash
./Scripts/run-authoritative-udp-session.sh
```

- [ ] Server and both client reports have distinct operating-system process IDs and report success.
- [ ] Protocol/session/logic/config/seed/input-delay/prediction-lead/final hashes agree across all three reports.
- [ ] Both clients receive the full server-stamped command stream and exercise late-command rollback.
- [ ] Every received server hash sample is confirmed after replay; unresolved mismatch and missing-local counts are zero.
- [ ] Loss, reorder and reliable retransmission counters are non-zero.
- [ ] Socket errors, rejected datagrams, bounded queue/capacity drops and pending reliable packets are zero.
- [ ] `summary.json` and `latest.md` match the raw Server/Client JSON values.
- [ ] Raw Player logs remain ignored; tracked reports contain no absolute path, host name, private address or credential.
- [ ] A 210-tick qualification is not described as the optional 54,000-tick/30-minute soak.

## 7. Player smoke test

- [ ] Build the intended macOS architecture/backend from the frozen commit and record Development Build state.
- [ ] Launch the target scene and exercise spatial-mode switching, late-command rollback, catch-up and obstacle interaction.
- [ ] Capture Overview, Navigation, Avoidance, Collision, Rollback and Network views from the built Player and inspect every image at full resolution.
- [ ] Confirm navigation rejection, sampled ORCA constraints, BVH/CCD probe and rollback correction are labelled as authoritative experiment versus presentation-only diagnostics.
- [ ] Run 5–10 minutes; inspect Player log for repeated exceptions or abnormal exit.
- [ ] Archive the Player and generate SHA-256.
- [ ] Record Mach-O architectures and signing/notarization state exactly.
- [ ] Treat smoke launch as build validation, not network, cross-platform or long-run performance evidence.

## 8. Documentation audit

- [ ] README capabilities point to code/tests or same-commit artifacts.
- [ ] Navigation is described as four bounded group slots and four shared macro paths.
- [ ] Obstacle ORCA, immutable BVH, conservative CCD, limiter ordering and SAT fallback boundaries are explicit.
- [ ] BVH build/query/result-order complexity is stated without assuming stable logarithmic queries.
- [ ] Static topology changes start a new rollback epoch.
- [ ] Replay/diagnostics remain distinct from real transport and completed platform compatibility claims.
- [ ] UDP is described as loopback/LAN validation; CRC is not authentication and peer IDs are not trusted identities.
- [ ] `SnapshotRequired` is not described as implemented snapshot transfer or reconnect repair.
- [ ] Current-thread allocation is not generalized to all workers.
- [ ] Indirect rendering is not described as per-instance GPU culling.
- [ ] YooAsset/HybridCLR integration is not described as a completed production update service.

## 9. GitHub Release

- [ ] Create annotated tag `v0.4.0` at the frozen commit.
- [ ] Use [`RELEASE_NOTES_v0.4.0.md`](RELEASE_NOTES_v0.4.0.md) as the release-note base and add the final commit/test/environment values.
- [ ] Upload the scrubbed test summary, benchmark JSON/Markdown, replay/input/output, UDP Server/Client reports, Player archive/screenshot and checksum.
- [ ] Scan every attachment for user names, absolute local paths, host names, private IPs, credentials and build-machine metadata.
- [ ] Clone the tag into a clean directory and repeat the documented static checks plus at least one runnable verification path.
- [ ] Verify the default-branch README, tag, Release text and attachment names after publishing.

## 10. Rollback criteria

Mark a Release as pre-release or withdraw affected binaries and publish a new patch version if:

- an artifact was not built from the tag commit;
- pinned dependencies cannot be restored;
- the test summary and release text disagree;
- benchmark JSON and Markdown disagree;
- replay or Player checksum does not match;
- UDP process reports do not converge or the summary disagrees with raw reports;
- documents report an unverified capability as completed.

Do not move an existing public tag to repair evidence.

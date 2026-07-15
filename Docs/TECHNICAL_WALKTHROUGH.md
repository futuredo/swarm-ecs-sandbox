# Technical walkthrough

This document provides a compact route for inspecting the architecture and reproducing its key claims. It favors source-to-evidence links over feature summaries.

## 1. Start from the authority boundary

Open these areas first:

```text
Assets/SwarmSandbox/Core/FixedPoint/
Assets/SwarmSandbox/Simulation/ECS/
Assets/SwarmSandbox/Simulation/Systems/SwarmSimulation.cs
Assets/SwarmSandbox/Simulation/Netcode/
```

Check four invariants:

1. `Core` / `Simulation` do not reference `UnityEngine`.
2. Agent state is stored in fixed-capacity SoA columns and scanned by stable index.
3. the logic step has a fixed System order and fixed-point delta;
4. snapshot/hash coverage includes every mutable field that can affect a future tick.

The classification table in [`ARCHITECTURE.md`](ARCHITECTURE.md) distinguishes authority, immutable setup, derived caches and presentation state.

## 2. Follow one navigation request

Trace a target command through:

```text
CommandTimeline
  → GroupPathState pending key
  → fixed request budget
  → GridIslandMap precheck
  → SharedPathCache or deterministic A*
  → reusable group SharedPath
  → Agent preferred velocity
```

Important details:

- each of four groups has at most one pending request;
- same-group updates replace an unprocessed goal;
- cross-group work is consumed by stable pending sequence;
- 10,000 Agents share four macro paths;
- the start anchor removes formation offset before averaging;
- cache layout is derived and may alter cost, never path semantics.

Relevant regression groups are `GridIslandMapTests`, `NavigationSchedulerTests` and rollback/path-cache cases in EditMode.

## 3. Compare spatial modes

The runtime exposes three complete modes:

- Uniform Grid radius + bounded top-K;
- KD-Tree radius;
- KD-Tree exact KNN with 65-bit squared distance.

Inspect stable distance/ID ordering and the fact that `SpatialIndexMode` is a command-driven authority field. Then run:

```bash
SWARM_AGENT_COUNT=10000 \
SWARM_WARMUP_TICKS=8 \
SWARM_SAMPLE_TICKS=32 \
./Scripts/run-spatial-index-benchmark-matrix.sh
```

Use both full and canonical hashes when reading the result. The full hash must differ when the authoritative mode differs; canonical comparison normalizes only that field. Timing is an end-to-end mode comparison because Uniform Grid and KD currently use different execution policies.

## 4. Trace static avoidance

The static-obstacle path is:

```text
immutable OBB set
  → counter-clockwise stable segments
  → immutable BVH
  → nearby visible obstacle segments
  → obstacle ORCA prefix
  → Agent ORCA suffix
  → LP1 / LP2 / LP3
```

Inspect that:

- segment and obstacle IDs provide deterministic tie-breaks;
- query scratch belongs to the caller/worker, not the immutable BVH;
- back-facing obstacle segments are excluded;
- LP3 receives the actual obstacle-prefix count;
- worker lanes have independent neighbor/line/projection buffers.

`StaticObstacleOrcaTests` covers line ordering, wall/corner behavior and deterministic replay of the same snapshot.

## 5. Trace movement safety

ORCA produces a target velocity. The final motion path is:

```text
ORCA target
  → max acceleration / turn / speed
  → swept circle vs expanded OBB
  → earliest impact + tangent slide
  → final SAT/circle penetration recovery
```

The limiter follows the ORCA solve, so its output may leave the strict ORCA feasible region. CCD and SAT provide geometric containment; they are not a proof of kinodynamic ORCA feasibility.

The sweep expands the OBB by Agent radius and applies a slab test in local coordinates. Its square expanded corners intentionally make contact conservative. `StaticObstacleBroadphaseCcdTests`, `StaticObstacleRuntimeIntegrationTests` and `KinematicVelocityLimiterTests` cover tunneling, corners, narrow passages, fallback and deterministic limiting.

The OBB tests also lock the fixed-point geometry contract at raw-unit boundaries: exact Q16.16 orthonormal basis construction, the three-raw-unit conservative world-AABB bound, direct SAT/CCD corner hits that must survive BVH pruning, and exact rejection of sub-unit circle-corner false positives.

## 6. Inspect rollback transactions

For a late command, verify the order:

1. preflight the origin snapshot;
2. reject without timeline mutation if it is not restorable;
3. preserve the command’s original `(tick, sequence)`;
4. restore, replay commands and rebuild derived paths as needed;
5. refresh snapshots through the previous current tick.

The command timeline discards only the ordered prefix older than the earliest restorable tick. Fixed capacity therefore tracks the rollback window rather than total process lifetime.

## 7. Reproduce and diagnose divergence

The v0.3.0 observability path has two levels:

```text
versioned .swarmreplay
  → independent no-render execution
  → checkpoint hash stream
  → first differing tick
  → layered authority hashes
  → component/entity/field/raw first difference
```

Inspect replay validation for explicit byte order, schema/config/logic identity, bounded counts and payload integrity. Inspect authority diagnostics for stable field order and deliberate-mutation tests.

A useful verification sequence is:

1. run `./Scripts/run-cross-process-replay.sh`;
2. inspect `ReplayResults/capture.json` and `verify.json` for distinct process IDs and the same replay SHA-256;
3. confirm every checkpoint and final layered hash matches;
4. inspect the recorded `AgentPositions[0].X.Raw` one-unit desync probe;
5. repeat `cross-process.swarmreplay` on each additional backend/architecture under test.

This proves same-input reproducibility and diagnostic precision in the tested environment. Repeat the exact file on every backend/architecture before reporting platform compatibility.

## 8. Trace one network command

Inspect the transport path in this order:

```text
SwarmUdpPacketCodec
  → SwarmUdpSocketWorker
  → FixedDatagramQueue
  → PacketReceiveWindow / ReliableDatagramWindow
  → ordered request or authority buffer
  → ClientCommandReconciler
  → RollbackController
  → NetworkAuthorityHashHistory
```

Verify that the socket worker has no `SwarmWorld` reference, command tick/sequence come from the server, packet ACK state is distinct from application command order, and replay callbacks replace speculative tick hashes. `SwarmUdpProtocolTests` covers codecs, corruption, unsigned wraparound, duplicate/ACK windows, fixed queues, deterministic impairment, ordered messages, convergence and expired-history `SnapshotRequired`.

After building the Player, run `./Scripts/run-authoritative-udp-session.sh`. Inspect the three raw JSON reports before the summary: process IDs must differ, common identities and final hashes must agree, both clients must receive all four commands, every received server hash sample must be confirmed, rollback/retransmission/impairment counters must be active, and all capacity/socket/pending counters must be zero.

## 9. Inspect the interactive technical lab

Run the Player or enter Play Mode, then switch views with `1` through `6`:

1. **Overview** relates the live timing/hash counters to the complete simulation pipeline.
2. **Navigation** draws the real 64×64 grid, blocked cells and four shared paths. `QUEUE BLOCKED TARGET` sends a normal command whose destination is inside the central obstacle; the scheduled request is rejected by the normal walkability/region precheck.
3. **Avoidance** samples one live Agent through the currently selected Uniform Grid or KD mode and rebuilds its actual Agent/obstacle ORCA constraints into caller-owned buffers.
4. **Collision** draws immutable obstacle-BVH nodes, a labelled presentation-only swept-circle probe and retained live ECS CCD contacts.
5. **Rollback** injects an 18-tick-late command and displays sampled before/after positions around the real snapshot restore and replay transaction.
6. **Network** displays the compiled envelope constants, process topology, prediction/repair contract and external qualification command. It explicitly labels the current scene as one local World rather than presenting static text as a live session.

The overlay is an inspection surface, not authority. Confirm that its APIs do not modify `SwarmWorld`, enter hashes/snapshots/replay, or run inside the headless benchmark measurement. Automated Player screenshots accept `-swarmCapturePath <path>` and `-swarmCaptureView <Overview|Navigation|Avoidance|Collision|Rollback|Network>`.

## 10. Read benchmark evidence correctly

The tracked default and spatial-matrix JSON files contain workload, execution policy, timing, current-thread allocation, navigation counters, obstacle/Agent lines, BVH counters, CCD/fallback counters, motion-limit counters and hashes.

The following conclusions are intentionally excluded:

- `Null Device` does not measure rendered FPS;
- short samples do not establish long-run P99 or thermal behavior;
- current-thread 0 B does not establish all-worker 0 B;
- a pruned KD/BVH query does not guarantee `O(log N)` worst case;
- a matching final hash on one platform does not establish cross-platform identity.

See [`BENCHMARKING.md`](BENCHMARKING.md) for commands and field definitions.

## 11. Inspect presentation and delivery boundaries

`SwarmIndirectRenderer` uploads all Agent data and uses one Agent indirect draw command. It does not yet build a GPU-visible index list or perform Hi-Z/HLOD.

YooAsset and HybridCLR are isolated behind runtime/editor integration points. Their pinned packages and configuration are present, while installer, IL2CPP, bundle/CDN and rollback verification remain a separate delivery pipeline. See [`COMMERCIAL_PIPELINE.md`](COMMERCIAL_PIPELINE.md).

## 12. Run the complete local suite

```bash
mkdir -p TestResults
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/TestResults/editmode.xml" \
  -logFile "$PWD/TestResults/editmode.log"
```

Treat the resulting XML/log as local evidence for the exact commit only. Any code or configuration change invalidates the previous run. Before publication, derive a compact machine-readable summary and remove user paths, host names, private network details and other build-machine metadata; do not upload the raw Unity files directly.

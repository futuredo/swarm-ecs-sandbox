# Benchmarking

`SwarmBenchmarkRunner` measures a complete deterministic logic tick: navigation scheduling, spatial-index rebuild/query, obstacle and Agent ORCA, kinematic limiting, swept movement and final collision recovery. It does not measure rendering and is not an isolated data-structure microbenchmark.

## Single runtime mode

Without a selector, the runner uses `UniformGrid` and writes `BenchmarkResults/latest.json` plus `latest.md`.

```bash
SWARM_SPATIAL_INDEX=UniformGrid \
SWARM_AGENT_COUNT=10000 \
SWARM_WARMUP_TICKS=8 \
SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"
```

`-swarmSpatialIndex <mode>` overrides `SWARM_SPATIAL_INDEX`. Canonical modes are `UniformGrid`, `KdTreeRadius` and `KdTreeKNearest`; parsing also accepts the short aliases defined by the runner.

## Comparable three-mode matrix

Close any Unity Editor currently holding the project, then run:

```bash
SWARM_AGENT_COUNT=10000 \
SWARM_WARMUP_TICKS=8 \
SWARM_SAMPLE_TICKS=32 \
./Scripts/run-spatial-index-benchmark-matrix.sh
```

| Variable | Default | Purpose |
|---|---:|---|
| `UNITY_EXECUTABLE` | project Editor version | Select a Unity binary |
| `SWARM_AGENT_COUNT` | `10000` | Agent count shared by all modes |
| `SWARM_WARMUP_TICKS` | `8` | Warmup per mode |
| `SWARM_SAMPLE_TICKS` | `24` | Measured ticks per mode |
| `SWARM_BENCHMARK_OUTPUT_DIR` | `BenchmarkResults` | Absolute or project-relative output directory |

Every mode starts from a new World with the same seed, default simulation values, Agent count and sample policy. The fixed order is `UniformGrid → KdTreeRadius → KdTreeKNearest` and is recorded in JSON.

Uniform Grid currently uses its persistent worker pool when available. Both KD avoidance paths run on the caller thread. The matrix therefore compares current end-to-end runtime modes; timing differences include both query structure and execution policy.

## Obstacle-approach sample

The default 8/32 run can finish before large formations reach the central obstacles. A longer warmup/sample run is useful for exercising obstacle lines and collision telemetry:

```bash
SWARM_AGENT_COUNT=10000 \
SWARM_WARMUP_TICKS=200 \
SWARM_SAMPLE_TICKS=120 \
SWARM_BENCHMARK_OUTPUT_DIR="$PWD/BenchmarkResults/obstacle-approach" \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/obstacle-approach/benchmark.log"
```

This is a scenario-duration change, not a directly comparable timing replacement for the tracked 8/32 matrix. Its purpose is to show that obstacle ORCA/CCD counters become active and that normal movement does not depend on repeated SAT fallback.

## Output schema

Each result contains:

| Group | Fields |
|---|---|
| Environment | Unity version, timestamp, processor, logical cores, graphics device |
| Workload | Agent count, warmup/sample ticks, spatial mode and avoidance execution |
| Timing | average, P95, min and max milliseconds |
| Allocation | `managedBytesAcrossSamples` |
| Navigation | request budget, cache capacity, island count, shared waypoint count |
| Avoidance | obstacle/Agent ORCA line totals and obstacle-BVH query count |
| Collision | BVH query/candidate totals, CCD sweep hits, SAT fallback recoveries, maximum residual penetration raw |
| Motion | acceleration-limited and turn-limited Agent totals |
| Identity | `ConfigHash`, full authority hash and canonical spatial-comparison hash |

The counters are sample-window totals, not per-tick averages. A zero obstacle-line/candidate count in a short sample means the formation did not enter the query range during that window; it does not mean the feature is disabled.

## Hash interpretation

- `configHash` identifies all result-affecting simulation parameters.
- `stateHash` includes the authoritative `SpatialIndexMode`; different modes are expected to have different full hashes.
- `canonicalSpatialComparisonHash` temporarily normalizes only `SpatialIndexMode` during hashing and then restores it.

Matching canonical values show equivalent remaining authority state for that exact seed/config/run. They do not prove equivalence for every density, radius, trajectory, runtime or architecture. Exact KNN has different neighbor-selection semantics from bounded radius modes even when one sampled scenario converges to the same final value.

## Interpretation limits

- `-nographics` reports `Null Device`; no rendered FPS or GPU conclusion can be derived.
- `GC.GetAllocatedBytesForCurrentThread()` covers only the sampling thread. It does not prove that every background worker allocated 0 B.
- Short samples do not establish thermal stability or long-run P99.
- KD branch pruning is distribution-dependent and has `O(N)` worst-case traversal.
- Static-obstacle BVH build is one-time deterministic work with worst-case `O(N²)` sorting; query worst case is `O(N)`, and ordering K results costs `O(K log K)`.
- Timing evidence must name Unity, hardware, graphics device, workload, execution policy and source commit.

Tracked machine-readable results live in [`BenchmarkResults/latest.json`](../BenchmarkResults/latest.json) and [`BenchmarkResults/spatial-index-matrix.json`](../BenchmarkResults/spatial-index-matrix.json). Raw Unity logs remain local because they can contain host names, private network details and absolute paths; a public Release should attach the tracked JSON/Markdown and only privacy-scrubbed log excerpts when a diagnostic trace is necessary.

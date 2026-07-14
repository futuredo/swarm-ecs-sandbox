# Benchmarking

`SwarmBenchmarkRunner` measures a complete deterministic logic tick: navigation scheduling, neighbor query, ORCA, movement integration and static-obstacle correction. It does **not** measure rendering, and it is not an isolated spatial-index microbenchmark.

## Single spatial-index mode

The single-mode entry point keeps the existing `BenchmarkResults/latest.json` and `latest.md` outputs. `UniformGrid` remains the default when no selector is supplied.

Use an environment variable:

```bash
SWARM_SPATIAL_INDEX=KdTreeRadius \
SWARM_AGENT_COUNT=10000 \
SWARM_WARMUP_TICKS=8 \
SWARM_SAMPLE_TICKS=32 \
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"
```

Or use a command-line selector, which takes precedence over the environment variable:

```bash
"/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -nographics -projectPath "$PWD" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunFromCommandLine \
  -swarmSpatialIndex KdTreeKNearest \
  -quit -logFile "$PWD/BenchmarkResults/benchmark.log"
```

Canonical selectors are `UniformGrid`, `KdTreeRadius` and `KdTreeKNearest`. The parser also accepts `Grid`, `KdTree`, `KdRadius`, `KdKnn` and `Knn`, ignoring case, spaces, `_` and `-`.

## Comparable three-mode matrix

Close any Unity Editor currently holding the project, then run:

```bash
./Scripts/run-spatial-index-benchmark-matrix.sh
```

Optional environment variables:

| Variable | Default | Purpose |
|---|---:|---|
| `UNITY_EXECUTABLE` | Editor version recorded by the project | Select a Unity binary |
| `SWARM_AGENT_COUNT` | `10000` | Agent count used by all three runs |
| `SWARM_WARMUP_TICKS` | `8` | Per-mode warmup ticks |
| `SWARM_SAMPLE_TICKS` | `24` | Per-mode measured ticks |
| `SWARM_BENCHMARK_OUTPUT_DIR` | `BenchmarkResults` | Output directory, absolute or project-relative |

The matrix entry point creates a fresh `SwarmWorld` for each mode. All three runs use the same formation seed, `PortfolioDefault` simulation values, agent count, warmup count and sample count. Each mode is warmed independently before measurement. The fixed run order is `UniformGrid → KdTreeRadius → KdTreeKNearest` and is recorded in JSON.

Outputs:

- `BenchmarkResults/spatial-index-matrix.json`: machine-readable settings and per-mode results.
- `BenchmarkResults/spatial-index-matrix.md`: review-friendly comparison table.
- `BenchmarkResults/spatial-index-matrix.log`: Unity batchmode log; ignored by Git.

The JSON records shared Q16.16 configuration values and execution policy so that results cannot silently mix scenarios. At present, the Uniform Grid runtime uses its persistent worker pool when available, while both KD avoidance paths run on the caller thread. Therefore this matrix compares the performance of the **current end-to-end runtime modes**; it must not be presented as an apples-to-apples single-threaded data-structure comparison.

Each result records two hashes:

- `stateHash` is the full authoritative hash. It includes `SpatialIndexMode`, so values from different modes are expected to differ even when every other authoritative field is identical.
- `canonicalSpatialComparisonHash` temporarily normalizes only `SpatialIndexMode` to `UniformGrid` while hashing, then restores the original mode. Matching values are evidence that the remaining authoritative end state is equivalent for that exact seed/config/run; they are not a proof for every input or platform.

In the tracked 10k, 8-warmup + 32-sample matrix, Uniform Grid radius and KD radius both produce canonical hash `0x4BD5680667C14261`, which proves equivalence for this benchmark input. KD exact KNN happens to produce the same canonical hash in this scenario as well, but its neighbor-selection semantics differ, so that observation must not be generalized to other densities, radii or trajectories.

## Tracked v0.2.1 evidence

The current `latest.json` is a separate Uniform Grid invocation: average `19.919240625` ms, P95 `24.5924` ms, min/max `17.0747/26.0361` ms, current-thread allocation `0 B`, and full/canonical hash `0x4BD5680667C14261`.

The current matrix records:

| Mode | Average (ms) | P95 (ms) | Min / max (ms) | Current-thread GC |
|---|---:|---:|---:|---:|
| Uniform Grid radius | 18.73074375 | 21.2342 | 16.8525 / 22.6702 | 0 B |
| KD-Tree radius | 114.1174875 | 125.944 | 104.8276 / 127.9858 | 0 B |
| KD-Tree exact KNN | 98.874775 | 108.3109 | 90.9214 / 109.7333 | 0 B |

The KD exact KNN implementation uses a preallocated 65-bit squared-distance representation, so candidate ordering and split-plane pruning cover the full two-dimensional Q16.16 coordinate domain without per-query managed allocation. This implementation fact does not change the execution-policy caveat above: both KD modes currently run avoidance on the caller thread.

`managedBytesAcrossSamples` uses `GC.GetAllocatedBytesForCurrentThread()`. It proves only the sampling thread's allocation delta and does not yet replace an all-worker allocation capture. Headless `Null Device` results also cannot be used as evidence of rendered FPS or mobile performance.

# Swarm ECS spatial-index benchmark matrix

- UTC: 2026-07-14T12:21:16.9300660Z
- Unity: 6000.3.9f1
- CPU: Apple M5 Pro (15 logical cores)
- GPU: Null Device
- Agents: 10,000
- Formation seed: `0x5EED1234`
- Fixed delta / neighbor distance (Q16.16 raw): 2184/262144
- Max neighbors: 8
- Warmup/sample ticks per mode: 8/32

| Spatial index | Avoidance execution | Average (ms) | P95 (ms) | Min (ms) | Max (ms) | Current-thread GC (B) | Full state hash | Canonical comparison hash |
|---|---|---:|---:|---:|---:|---:|---|---|
| UniformGrid | Caller + 14 background workers | 18.731 | 21.234 | 16.853 | 22.670 | 0 | `0x4BD5680667C14261` | `0x4BD5680667C14261` |
| KdTreeRadius | Caller thread | 114.117 | 125.944 | 104.828 | 127.986 | 0 | `0xE8AE71279C8EC54C` | `0x4BD5680667C14261` |
| KdTreeKNearest | Caller thread | 98.875 | 108.311 | 90.921 | 109.733 | 0 | `0x008726C93F9563E3` | `0x4BD5680667C14261` |

This is an end-to-end logic-tick comparison of the current runtime modes, not an isolated spatial-query microbenchmark. UniformGrid uses its persistent worker pool when available; both KD modes currently execute avoidance on the caller thread.

The full state hash includes the authoritative `SpatialIndexMode`, so hashes from different modes are expected to differ. The canonical comparison hash temporarily normalizes only that field to `UniformGrid`, computes the hash, and restores the original mode. It is evidence for algorithm-result equivalence: UniformGrid and KdTreeRadius should match, while exact KNN may legitimately differ because it has different neighbor-selection semantics.

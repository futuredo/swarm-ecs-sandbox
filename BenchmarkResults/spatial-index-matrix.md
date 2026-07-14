# Swarm ECS spatial-index benchmark matrix

- UTC: 2026-07-14T14:07:27.8084880Z
- Unity: 6000.3.9f1
- CPU: Apple M5 Pro (15 logical cores)
- GPU: Null Device
- Agents: 10,000
- Formation seed: `0x5EED1234`
- Fixed delta / neighbor distance (Q16.16 raw): 2184/262144
- Max neighbors: 8
- Warmup/sample ticks per mode: 8/32

| Spatial index | Avoidance execution | Average (ms) | P95 (ms) | Min (ms) | Max (ms) | Current-thread GC (B) | Config hash | Full state hash | Canonical comparison hash |
|---|---|---:|---:|---:|---:|---:|---|---|---|
| UniformGrid | Caller + 14 background workers | 15.396 | 17.185 | 14.125 | 17.562 | 0 | `0x90EFFCAE3189FF28` | `0x2F7882ADEB0C9076` | `0x2F7882ADEB0C9076` |
| KdTreeRadius | Caller thread | 136.997 | 150.894 | 126.704 | 152.235 | 0 | `0x30EAA8B687BFBBB9` | `0xD05619100109D013` | `0x2F7882ADEB0C9076` |
| KdTreeKNearest | Caller thread | 123.118 | 133.478 | 114.844 | 134.728 | 0 | `0xD0E554BEDDF5784A` | `0x3B79FF50D217BA38` | `0x2F7882ADEB0C9076` |

This is an end-to-end logic-tick comparison of the current runtime modes, not an isolated spatial-query microbenchmark. UniformGrid uses its persistent worker pool when available; both KD modes currently execute avoidance on the caller thread.

The full state hash includes the authoritative `SpatialIndexMode`, so hashes from different modes are expected to differ. The canonical comparison hash temporarily normalizes only that field to `UniformGrid`, computes the hash, and restores the original mode. It is evidence for algorithm-result equivalence: UniformGrid and KdTreeRadius should match, while exact KNN may legitimately differ because it has different neighbor-selection semantics.

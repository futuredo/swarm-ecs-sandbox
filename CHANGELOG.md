# Changelog

This file records verifiable public changes. Unreleased work remains under `Unreleased` until a tag and GitHub Release bind it to a commit.

## [Unreleased]

- No entries.

## [0.3.0] - 2026-07-14

### Added

- Static OBB to stable counter-clockwise obstacle segments and RVO2-style obstacle ORCA constraints.
- Immutable static-obstacle BVH with caller-owned scratch, stable candidate ordering and telemetry.
- Fixed-point swept circle against expanded OBB, deterministic earliest-impact selection, bounded slide iterations and residual-depth reporting.
- Exact Q16.16 OBB basis quantization, three-raw-unit conservative AABB padding and exact raw circle-corner distance checks keep BVH, CCD and SAT geometry consistent at sub-unit boundaries.
- Authoritative formation sizing now uses an integer ceiling square root instead of binary floating point.
- Maximum acceleration and per-tick turn limiting after the ORCA target velocity; new result-affecting parameters participate in `ConfigHash`.
- Separate obstacle/Agent ORCA line, avoidance/collision BVH, CCD, SAT fallback and motion-limit benchmark counters.
- Versioned `.swarmreplay` serialization with explicit byte order, simulation identity/configuration, canonical command/checkpoint data, integrity checks and bounded input validation.
- Canonical replay commands use O(N) ordered loading and cursor playback; a combined Agent/tick/command/checkpoint budget rejects excessive legal workloads before variable-stream allocation.
- Layered authority hashing and stable first-difference diagnostics down to component, entity/group, field and raw values.
- Focused regression coverage for walls, corners, corridors, narrow entrances, high-speed tunneling, immutable obstacle snapshots, limiter behavior, replay validation and deliberate authority mutations.

### Changed

- Movement now follows `ORCA target → acceleration/turn/speed limit → swept movement/slide → final SAT recovery`.
- LP3 receives the real obstacle-prefix line count; static obstacle constraints precede Agent constraints.
- Static obstacle collision queries no longer scan every obstacle during normal movement.
- Benchmark Markdown/JSON and HUD expose static-avoidance, collision and limiter activity plus `ConfigHash`.
- Public documentation now separates implemented mechanisms, same-commit evidence and future acceptance targets.

### Evidence boundary

- Timing and hash values are read from the same-commit tracked benchmark JSON and matching Release artifacts; `Null Device` results cover logic, not rendered FPS.
- Managed allocation sampling covers the caller thread only.
- The motion limiter may move a holonomic ORCA solution outside its strict half-plane feasible set; CCD/SAT provide geometric safety, not kinodynamic optimality.
- Expanded-OBB slab CCD is conservative around square expanded corners.
- Static obstacle topology is immutable within a rollback epoch.
- Replay and field-level diagnostics provide cross-process tooling; backend/architecture compatibility is reported only for targets that executed the same replay artifact.
- Real transport, server arbitration, out-of-window snapshot recovery and reconnect remain outside this release.

## [0.2.1] - 2026-07-14

### Added

- Connected-region labeling and pre-A* rejection for blocked or cross-region requests.
- Four fixed group request slots, stable pending-sequence consumption and a fixed per-tick budget.
- Fixed-capacity deterministic shared path cache and rollback-time derived path reconstruction.
- Uniform Grid radius, KD-Tree radius and 65-bit exact KNN runtime modes.
- Authoritative `SpatialIndexMode` commands with snapshot/hash/rollback coverage.
- Fixed-capacity command-timeline prefix reclamation based on the rollback window.

### Changed

- Replanning anchor uses the group average of `Position - FormationOffset`.
- Radius and exact-KNN distance handling cover the complete intended Q16.16 coordinate semantics without per-query allocation.
- Benchmark/HUD report path budget, cache, island, shared-waypoint and spatial-mode information.

### Evidence boundary

- The tracked headless evidence used Unity 6000.3.9f1, 10,000 Agents and `Null Device`.
- Uniform Grid used its worker pool while KD avoidance ran on the caller thread; the matrix compared complete runtime modes rather than isolated query structures.
- Full hashes included `SpatialIndexMode`; canonical comparison normalized only that field.
- Map topology remained external epoch data, and signed 32-bit command ordering retained a long-running wraparound boundary.

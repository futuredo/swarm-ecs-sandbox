# Changelog

This file records verifiable public changes. Unreleased work remains under `Unreleased` until a tag and GitHub Release bind it to a commit.

## [Unreleased]

### Added

- English / Simplified Chinese runtime localization across all six Technical Lab pages, metrics, controls, context explanations and world-space labels.
- Persistent language selection through the HUD or `F1`, plus deterministic Player capture language selection for visual validation.
- A capability-oriented Technical Lab guide covering interactive walkthroughs and reproducible test, replay and UDP evidence paths.

### Evidence boundary

- Localization remains presentation-only and does not enter authoritative state, configuration/state hashes, snapshots, replay or network protocol data.

## [0.4.0] - 2026-07-15

### Added

- Real IPv4 UDP session with one headless authoritative Player and two independent predictive clients.
- Explicit little-endian packet envelope, CRC32, unsigned serial arithmetic, ACK bitmap, fixed reliable retransmission windows and ordered request/authority buffers.
- Compatibility handshake for protocol, logic/config, Q16.16, Agent/seed and replay/snapshot/authority schemas.
- Main-thread-only command/rollback reconciliation behind a fixed-capacity socket-thread handoff queue.
- Non-reliable authority hash telemetry with replay-time speculative hash replacement and confirmed-tick diagnostics.
- Deterministic latency, jitter, loss, duplication and reorder scheduler plus RTT, bandwidth, retransmission, rollback percentile and capacity telemetry.
- Automated three-process qualification script and tracked machine-readable network evidence.

### Changed

- `RollbackController` exposes snapshot availability/earliest-restorable tick and an optional read-only step observer for network hash reconciliation.
- Headless network roles suppress the normal scene simulation/presentation host.

### Evidence boundary

- The tracked network workload uses three macOS Mono Player processes, 512 Agents per world and 210 ticks; it is a protocol/convergence qualification, not a 10,000-Agent rendered benchmark or 30-minute soak.
- Weak-network decisions are deterministic for a fixed scheduling call order; operating-system timing can change the server tick assigned to a request across independent runs.
- CRC is corruption detection, not authentication. Internet services, encryption, NAT traversal and congestion control are excluded.
- Out-of-window commands enter `SnapshotRequired`; full/delta snapshot repair and reconnect remain v0.5 scope.

## [0.3.1] - 2026-07-15

### Added

- Five presentation-only technical lab views for overview, navigation, avoidance, collision and rollback inspection.
- World-space overlays for the deterministic navigation grid, blocked cells, shared A* routes, goals, sampled neighbors, reconstructed ORCA constraints, obstacle topology, immutable BVH bounds, CCD contacts and rollback correction ghosts.
- Fixed-capacity CCD contact diagnostics and caller-buffer Agent/ORCA sampling APIs that remain outside snapshots, hashes and replay payloads.
- Interactive blocked-target rejection, spatial-mode cycling, sampled-group selection, retained CCD traces and late-command rollback controls.

### Changed

- The runtime HUD now separates live rendering, logic budget, navigation, avoidance, collision and rollback metrics instead of presenting one dense counter wall.
- The macOS/Player presentation identifies live FPS and per-tick simulation cost separately from tracked headless benchmark evidence.

### Evidence boundary

- Technical overlays are diagnostic views of real runtime data; they do not alter authoritative state or constitute additional simulation features.
- The deterministic CCD probe is presentation-only and is labelled separately from retained live ECS collision contacts.
- Runtime overlays use Unity `float`/GL rendering after the fixed-point step and are intentionally excluded from determinism claims.

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

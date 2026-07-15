# Swarm-ECS-Sandbox engineering roadmap

## 1. Direction and evidence rules

The project evolves in dependency order:

```text
v0.2.1  Navigation completeness
  → v0.2.2  Static avoidance and continuous collision safety
  → v0.3.0  Versioned replay and desync diagnostics
  → v0.3.1  Interactive technical lab and runtime observability
  → v0.4    Authoritative UDP session with two clients
  → v0.5    Reconnect, snapshots and state repair
  → v0.6    Long-run performance and stability matrix
  → v0.7    GPU visibility and LOD
  → v0.8    YooAsset + HybridCLR + IL2CPP delivery loop
```

Three evidence states are kept separate:

- **Implemented**: code exists and has a focused automated verification path.
- **Locally verified**: a named environment produced raw results; those results become public evidence only when attached to a commit/tag.
- **Acceptance target**: a planned quantitative gate, not a current result.

Tags are immutable. If evidence changes after a tag, publish a new patch version rather than moving the old tag.

## 2. Completed foundation

### v0.2.1 — Navigation completeness

- Q16.16 fixed-step simulation and a custom fixed-capacity SoA World.
- Stable 64×64 eight-way A*, connected-region rejection and map revision handling.
- Four bounded group request slots, stable sequence consumption and a fixed per-tick budget.
- Deterministic 68-entry shared path cache and rollback-time derived A* reconstruction.
- Uniform Grid radius, KD-Tree radius and 65-bit exact KNN runtime modes.
- Authoritative spatial-mode commands, snapshot/hash coverage and rollback replay.

### v0.2.2 — Static avoidance and collision safety

- Static OBBs converted to stable counter-clockwise segments and obstacle ORCA constraints.
- Obstacle constraints precede Agent constraints; LP3 receives the real obstacle-prefix count.
- Immutable static-obstacle BVH with caller-owned query scratch and stable candidate ordering.
- Conservative fixed-point swept circle against expanded OBB, earliest-impact tie-break and slide iterations.
- SAT/circle-vs-OBB retained as final penetration recovery and residual-depth telemetry.
- Maximum acceleration, turn step and speed limiting included in configuration identity.
- Corridor, wall, corner, narrow entrance, high-speed and zero-allocation regression coverage.

The motion limiter runs after a holonomic ORCA solve. It can move the final velocity outside a previously feasible half-plane intersection; CCD/SAT protect geometry, but strict kinodynamic feasibility remains a separate solver problem. The expanded-OBB sweep is conservative at square corners.

### v0.3.0 — Replay and diagnostics

- Versioned `.swarmreplay` envelope with explicit byte order, simulation identity/configuration, canonical commands, checkpoints, integrity validation and bounded reads.
- No-render replay execution suitable for independent-process comparison.
- Layered authority hashes for config, metadata, group targets/path states, navigation sequence, positions, velocities and path cursors.
- Stable first-difference reporting with component, entity/group, field and raw expected/actual values.
- Replay and diagnostic schema tests covering malformed input and deliberate authority mutations.

This version provides the mechanism for cross-process evidence. A complete Mono/IL2CPP and ARM64/x64 result matrix still requires artifacts from those targets.

### v0.3.1 — Interactive technical lab

- Five switchable runtime views for system overview, navigation, avoidance, collision and rollback.
- World-space inspection of the real shared paths, grid topology, selected neighbors, reconstructed ORCA constraints, immutable BVH bounds, CCD contacts and rollback correction samples.
- Fixed-capacity, caller-owned diagnostic sampling that cannot write to the authoritative World.
- Automated Player capture selection for repeatable visual inspection of every lab view.

This patch changes observability and presentation only. Diagnostic buffers, Unity `float` conversion and GL overlays remain outside authority hashes, snapshots, replay payloads and headless benchmark timing.

## 3. v0.4 — Authoritative UDP session

Initial scope is loopback/LAN and excludes account, matchmaking, NAT traversal and anti-cheat services.

### Implementation scope

- One headless authoritative session server and two independent clients.
- Packet header with protocol/session/peer IDs, sequence, ack/ackBits, tick, channel, length and CRC.
- Reliable ordered commands; non-reliable hash telemetry where appropriate.
- Client predicted/confirmed tick, input delay, prediction lead and rollback depth.
- A fixed-capacity network-to-simulation queue; network threads never mutate `SwarmWorld`.
- Handshake validation for protocol, logic, config, fixed-point, replay and snapshot schema versions.
- Built-in latency, jitter, loss, duplication and reorder simulation.
- Sequence epoch or tested serial-number arithmetic for long-running wraparound.

### Acceptance targets

- Automated `1 server + 2 clients` launch and a 30-minute weak-network run.
- Matching authority hashes at every confirmed tick.
- Report RTT, bandwidth, prediction lead and rollback depth P50/P95/P99.
- A command outside the rollback window enters an explicit `SnapshotRequired` state.

## 4. v0.5 — Reconnect and state repair

### Implementation order

1. Versioned full snapshot with logic/config/schema identity and CRC.
2. Fragmentation, retransmission, out-of-window replacement and no-render catch-up.
3. Reconnect state machine: handshake → snapshot → subsequent commands → catch-up → presentation resume.
4. Delta encoding and compression only after the full path is stable.

### Acceptance targets

- 1,000 randomized disconnect/reconnect cycles converge to confirmed authority state.
- Corrupt, missing, duplicate and reordered fragments never mutate confirmed state.
- Snapshot and following commands bind to one protocol/logic/config/schema identity.
- Report full/delta sizes, transfer/apply time, catch-up duration and memory peak.

## 5. v0.6 — Performance and stability evidence

- Stage markers for navigation, spatial build/query, ORCA, motion, collision, snapshot, hash and network queues.
- Fixed-machine 10-minute soak with P50/P95/P99, all-worker allocation, memory peak and thermal context.
- Separate reports for rollback bursts, 300/600-tick catch-up, full-snapshot apply and reconnect.
- Uniform Grid/KD radius/KD exact KNN matrix plus 1/2/4/8/16 worker-lane comparison.
- Burst/Jobs as a measured backend comparison, not a label-driven rewrite.

Numbers enter top-level documentation only when machine-readable evidence is tied to the source commit and has passed a privacy scan.

## 6. v0.7 — GPU visibility and LOD

- Compute per-instance frustum culling, visible-index compaction and indirect-argument generation.
- Three Agent LOD levels, followed by depth pyramid and conservative Hi-Z.
- Separate 10k full-simulation and 100k presentation-only workloads.
- No CPU visibility readback; retain visible/culled/LOD counts and GPU timing.
- Validate Metal and DX12 buffer layout against a CPU visibility oracle and screenshot regression.

## 7. v0.8 — Asset and code delivery loop

- Clean-build-machine HybridCLR installation, `Generate/All`, AOT metadata and hot-update assembly build.
- YooAsset Offline/Host modes, versioned manifest, bundle hash, resume and previous-version fallback.
- Logic updates change logic identity; server handshake rejects mixed versions.
- macOS ARM64 and Windows x64 IL2CPP artifacts tied to source commit and dependency versions.
- Corrupt bundle/manifest injection proves fallback without leaking license or CDN secrets.

The current repository contains the integration boundary, not a completed production delivery service.

## 8. Deferred branches

Flow Field, HPA*, LPA*/D* Lite, runtime topology editing and additional avoidance solvers remain optional branches. They should be scheduled only after measured scale or update patterns show that the current shared A* design is the limiting factor.

General combat, animation and ragdoll systems are intentionally outside this repository’s scope. The core sequence remains:

```text
large deterministic simulation
  → reproducible execution and precise diagnostics
  → real weak-network rollback
  → out-of-window state recovery
```

## 9. Definition of done

Every version must:

- register new authority fields in hash, snapshot, replay/schema and rollback tests;
- bind privacy-reviewed machine-readable artifacts to the release commit;
- name hardware, Unity, backend, workload, warmup/sample/soak and graphics device;
- record performance regressions and intentional logic/hash changes;
- separate implemented behavior, local evidence and future targets;
- publish a runnable build or deterministic runner, automated validation entry points and architecture/protocol notes;
- preserve the stated complexity and safety boundaries rather than replacing them with asymptotic shorthand.

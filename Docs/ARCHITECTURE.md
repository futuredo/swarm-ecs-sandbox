# Architecture and data flow

## 1. Layer boundaries

The project separates authoritative simulation from Unity presentation:

- `SwarmECS.Core`: Q16.16 values, vectors and deterministic utilities; no `UnityEngine` reference.
- `SwarmECS.Simulation`: SoA World, navigation, spatial queries, ORCA, collision, rollback, replay and diagnostics; no `UnityEngine` reference.
- `SwarmECS.Runtime`: input, HUD, camera, GPU buffers, UDP process runners and commercial loading boundary.
- `SwarmECS.Editor`: scene setup, tests, benchmark, replay tooling and package configuration.

The simulation advances at a fixed 30 Hz. Rendering may use `float` and a variable frame rate, but presentation values never write back into authoritative state.

## 2. Logic tick

`RollbackController.Step()` stores a snapshot at the tick boundary, applies commands in canonical order, then runs Systems in a fixed sequence:

```mermaid
flowchart LR
    A["Save snapshot"] --> B["Apply ordered commands"]
    B --> C["Detect path requests"]
    C --> D["Process fixed A* budget"]
    D --> E["Prepare shared paths"]
    E --> F["Preferred velocity"]
    F --> G["Agent + obstacle queries"]
    G --> H["Obstacle-prefix ORCA LP"]
    H --> I["Acceleration / turn limiter"]
    I --> J["Swept movement + slide"]
    J --> K["SAT fallback"]
    K --> L["Advance tick"]
    L -. "presentation only" .-> M["GPU upload + indirect draw"]
```

All Agent ORCA jobs read the same tick-start position/velocity arrays and write only their assigned `NextVelocities[i]`. A barrier completes before movement integration, so worker completion order cannot form a read-after-write dependency.

## 3. SoA World and state classes

### Agent component columns

```text
Positions[]           FPVector2
Velocities[]          FPVector2
PreferredVelocities[] FPVector2
NextVelocities[]      FPVector2
FormationOffsets[]    FPVector2
Radii[]               FP
MaxSpeeds[]           FP
Groups[]              byte
PathCursors[]         ushort
```

Entity identity is `Index + Generation`. Arrays have fixed capacity and hot Systems scan columns in stable index order. This is a deliberately small data-oriented runtime, not a general archetype/chunk replacement.

| Class | Examples | Rule |
|---|---|---|
| Dynamic authority | tick, position, velocity, group target, path cursor, `GroupPathState`, request sequence, `SpatialIndexMode` | Included in authority hashing; future-affecting values are snapshotted |
| Immutable setup | seed, group, radius, max speed, formation offset, config, static obstacle topology | Rebuilt from the same input; topology changes start a new epoch |
| Derived hot data | preferred/next velocity, query scratch, ORCA lines | Recomputed; excluded from snapshots |
| Derived path cache | shared path nodes/waypoints and cache entries | Rebuilt from authoritative keys and deterministic A* |
| Presentation | HUD strings, camera, technical-lab samples and GPU upload buffers | Excluded from authority |

## 4. Navigation

### Grid and connectivity

The 64×64 navigation grid uses eight-way movement. Static OBBs are rasterized with Agent clearance; an integer penalty kernel increases nearby walk cost. Diagonal movement is rejected when both adjacent cardinal cells are blocked.

`GridIslandMap` uses exactly the same neighbor and diagonal rules for connected-component labeling. Region seeds are scanned row-major, arrays are allocated once, and blocked/out-of-bounds/cross-region requests are rejected before A* expansion. A map revision change invalidates derived navigation data.

### Fixed request budget

Each of four groups owns one `GroupPathState` containing the resolved key and at most one pending key. A new goal for the same group replaces its unprocessed goal. Across groups, the smallest stable pending sequence runs first; the default budget is one request per tick.

The start anchor is the fixed-point average of `Position - FormationOffset` for the group. If that cell is unusable, the nearest walkable cell is chosen by raw squared distance and stable node ID.

This structure intentionally models four bounded group slots; it is not an arbitrary-capacity request queue.

### Shared path cache

`SharedPathCache` has 68 preallocated entries by default. Its key is `(start, goal, mapRevision)` and deterministic round-robin chooses an eviction slot. A hit copies into the group’s reusable path; a miss runs allocation-free A* and fills the slot.

The cache and its replacement cursor are derived state. After rollback, a resolved path can be copied from cache or synchronously rebuilt from its authoritative key. A derived rebuild does not create a new path request and does not consume the per-tick authority budget.

A* uses a binary heap with stable `f → h → nodeId` ordering. With `V=4096` and at most eight edges per node, the standard bound is `O((V + E) log V)` time and `O(V)` storage. The 10,000 Agents share four group paths.

## 5. Neighborhood queries

| Mode | Semantics | Execution | Complexity boundary |
|---|---|---|---|
| Uniform Grid radius | Covered-cell scan with bounded top-K | Persistent worker pool by default | Build is average `O(N)`; a dense query can inspect `O(N)` candidates |
| KD-Tree radius | Exact `ulong` raw-square radius test | Caller thread | Branch pruning is data-dependent; worst case `O(N)` |
| KD-Tree exact KNN | 65-bit raw-square nearest-neighbor test | Caller thread | Exact pruning; worst case `O(N)` |

Exact KNN stores squared distance as a one-bit carry plus a 64-bit low word, which covers the complete two-dimensional Q16.16 coordinate domain. Results are ordered by distance and stable entity ID.

`SpatialIndexMode` affects future velocity and is therefore authoritative. Runtime mode changes enter the same command timeline and are restored/reapplied by rollback.

## 6. Static-obstacle data

Static OBBs are copied at simulation construction. Each box produces four counter-clockwise, stable-ID segments. The obstacle set and its BVH are immutable for the lifetime of the simulation.

OBB input directions are deterministically quantized so `AxisX`/`AxisY` are exactly orthogonal and have `SqrMagnitude == FP.One` under Q16.16 dot-product truncation. Converting local slabs back into world coordinates can still differ from the vertex envelope because each dot-product term truncates independently. `FPAabb2.FromOrientedBox` therefore rounds products outward and adds a proven three-raw-unit two-dimensional bound per world axis. Circle-corner SAT comparisons use exact raw-coordinate squared distance, avoiding sub-unit false positives from ordinary Q16.16 multiplication.

The BVH owns its nodes and obstacle ordering; each caller owns reusable query scratch. Candidate output is stable by obstacle ID. Construction is a one-time deterministic insertion sort with worst-case `O(N²)` cost. Query pruning is distribution-dependent and worst-case `O(N)`; ordering K returned candidates costs `O(K log K)`.

The same structure serves two paths:

1. avoidance queries collect nearby visible segment constraints;
2. swept movement queries collect obstacles intersecting the motion AABB.

A topology change requires constructing a new obstacle set and resetting history. Current snapshots do not serialize BVH nodes or obstacle topology and cannot cross that epoch boundary.

## 7. ORCA, motion and collision

### Constraint ordering

Obstacle neighbors are filtered and sorted deterministically. Their half-planes form a prefix in the ORCA line buffer, followed by bounded Agent-Agent lines. LP3 receives the actual obstacle-prefix count, preserving the obstacle constraints when it repairs an infeasible candidate.

Agent overlap uses stable entity IDs to choose a reproducible antisymmetric escape direction. Worker lanes own their neighbor, line and projection buffers.

### Motion limiter

The ORCA result is a holonomic target velocity. `KinematicVelocityLimiter` then applies maximum acceleration, maximum per-tick turn and maximum speed.

This ordering has an explicit consequence: a limited velocity may no longer satisfy every ORCA half-plane. The runtime therefore does not claim strict kinodynamic feasibility. A future motion-aware solver would have to include acceleration and turn constraints inside the optimization domain.

### Conservative CCD and fallback

Movement uses a swept circle against an OBB expanded by the Agent radius, implemented as a fixed-point slab test in obstacle-local space. Earliest time of impact is selected by time, obstacle ID and feature ID; a fixed number of impact iterations project the remaining displacement along the contact tangent.

The expanded box has square corners, so the sweep is conservative near the true rounded Minkowski corner and may report contact early. The swept broadphase expands by the true circular radius, so it is not required to preserve narrowphase hits that exist only in this square-corner conservative region; real circle/box contacts remain inside the conservative OBB bounds. This is accepted in exchange for a deterministic, no-tunneling broad phase/narrow phase path.

After the sweep, circle-vs-OBB penetration recovery/SAT remains a final geometric guard and diagnostic. Normal obstacle approach should be handled by ORCA and CCD rather than repeated push-out. Telemetry records obstacle/agent lines, BVH queries/candidates, sweep hits, fallback recoveries and maximum residual depth.

## 8. Rollback, replay and diagnostics

The snapshot ring stores dynamic authority for a bounded rollback window. The command timeline stores canonical `(tick, sequence)` entries in fixed capacity and discards only the prefix older than the earliest restorable tick.

Versioned `.swarmreplay` data records simulation identity/configuration, seed, canonical commands and checkpoint hashes with explicit little-endian encoding, bounded counts and integrity validation. Already validated command streams append to `CommandTimeline` in O(N) total time and sequential playback advances a cursor instead of rescanning history every tick; rewinding a tick resets that cursor for rollback semantics. A combined Agent/tick/command/checkpoint execution budget is checked before variable command allocation and again after checkpoint decoding. A headless runner can execute the same input without rendering.

Layered authority hashing splits the full hash into config, world metadata, group targets, group path state, navigation request sequence, positions, velocities and path cursors. When two worlds differ, the diagnostic scan uses the same schema order to report the first component, entity/group, field and raw value mismatch.

Replay is an observability and reproduction mechanism, not a network transport. Platform-wide bit identity requires running the same replay artifact on every claimed backend/architecture and retaining the results.

## 9. UDP transport and prediction

The v0.4 runner starts as one of three independent Player processes. Server peer `0` owns canonical command timing; client peers `1` and `2` predict and reconcile the unchanged server-stamped `(tick, sequence)` values.

The socket worker is deliberately narrower than a game session object. Its background thread performs envelope/CRC validation, learns the loopback/LAN endpoint for a declared peer and copies the raw datagram into `FixedDatagramQueue`. It has no simulation dependency. Unity's main thread owns receive-window state, fixed message decoding, ordered buffers, command timeline mutation, rollback and hash confirmation.

Per-peer links maintain one packet receive window, one fixed reliable retransmission window and outgoing packet sequence. ACK-only packets are not themselves reliable. Duplicate reliable packets dirty ACK state so a lost ACK can recover on retransmission. Hash telemetry shares packet sequencing but remains non-reliable at the message layer.

The weak-network scheduler is between encoding and `Socket.SendTo`; it stores complete encoded datagrams in fixed slots and injects latency/jitter/loss/duplication/reorder with stable due-time ordering. Reports separate impairment drops from queue-capacity drops and socket errors.

This layer is a loopback/LAN deterministic-session laboratory. Peer identity is not authenticated, CRC is not a MAC, and v0.4 has no snapshot transfer or reconnect state repair.

## 10. Rendering and memory

`SwarmIndirectRenderer` converts fixed-point raw values to presentation floats, uploads all Agents to a `GraphicsBuffer`, and draws them through one `Graphics.RenderMeshIndirect` Agent command. Ground and obstacles use separate draws. There is currently one aggregate bounds test, not per-instance GPU visibility, Hi-Z or HLOD.

### Interactive technical lab

`SwarmDebugHud` selects one of six presentation views and `SwarmTechnicalOverlayRenderer` maps live runtime structures into world-space lines and labels:

| View | Data source | Presentation |
|---|---|---|
| Overview | group paths, group targets and live stage counters | end-to-end pipeline map |
| Navigation | `GridMap`, `SharedPath`, request/cache/island counters | grid topology, blocked cells and four shared routes |
| Avoidance | active spatial index and the existing ORCA line builder | one Agent's selected neighbors, constraint lines and preferred/solved velocity |
| Collision | static obstacle segments, immutable BVH and CCD diagnostics | BVH bounds, deterministic sweep probe and recent live contacts |
| Rollback | sampled positions immediately before and after late-command replay | correction vectors across the replay transaction |
| Network | compiled protocol constants, process topology and external qualification contract | explicit local-scene versus three-process boundary and reproduction entry point |

Diagnostic APIs copy into fixed-capacity buffers owned by the presentation caller. They do not mutate component columns, consume navigation budget, append timeline commands or participate in snapshot/hash/replay serialization. The navigation and rollback buttons are explicit experiments: when pressed, they enqueue normal authoritative commands through the existing command path. The collision sweep probe is presentation-only and is labelled separately from live ECS contacts.

The overlay converts Q16.16 values to Unity `float` only after the fixed simulation step and renders with GL lines. It is excluded from headless benchmark timing and determinism claims.

Component columns, spatial indices, A* storage, island flood queue, path cache, worker scratch, command timeline and snapshot ring allocate during setup. Sampling uses `GC.GetAllocatedBytesForCurrentThread()`, so a 0 B result proves only the measured thread’s allocation delta. All-worker allocation evidence requires a separate profiler capture.

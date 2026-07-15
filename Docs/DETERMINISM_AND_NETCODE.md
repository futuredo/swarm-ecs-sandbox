# Determinism, rollback and replay

## 1. Deterministic contract

For the same logic version, configuration, initial seed and canonical command stream, the simulation is designed to produce identical raw authoritative state.

The contract is based on:

1. **Fixed numeric rules**: signed Q16.16, saturating overflow, truncating multiplication/division, integer square root and integer-only formation sizing.
2. **Fixed time step**: the authority layer uses the Q16.16 representation of `1/30` and never reads `Time.deltaTime`.
3. **Stable traversal and tie-breaks**: entity index, obstacle ID, A* `f → h → nodeId`, request sequence and distance/ID ordering are explicit.
4. **Canonical commands**: fixed-capacity storage orders commands by `(tick, sequence)`.
5. **Race-free parallelism**: avoidance reads tick-start state, writes disjoint output slots and completes before integration.
6. **No Unity authority types**: `Core` and `Simulation` do not use Unity Physics, NavMesh, `UnityEngine.Random` or Unity vectors.
7. **Fixed hot-path capacity**: component, query, path, ORCA, command and snapshot storage are reserved during setup.

These rules reduce platform divergence; they do not by themselves prove bit identity on every runtime or CPU. Cross-backend claims require replay artifacts and results from each named target.

## 2. Configuration identity

`SwarmConfig.ConfigHash` covers all values that can change simulation results, including fixed delta, radius, speed, acceleration, turn step, neighbor distance/count, time horizon, world extent and spatial mode. A replay/config handshake must reject an incompatible value instead of executing under local defaults.

Static obstacle topology is immutable setup data. The current rollback snapshot does not carry obstacle geometry or BVH nodes. Changing topology requires rebuilding the simulation and starting a new history epoch.

## 3. Authority schema and layered hashes

The diagnostic schema hashes fixed domains independently and also produces a complete schema-ordered value:

| Domain | Fields |
|---|---|
| Config | `ConfigHash` |
| World metadata | tick, agent count, seed, `SpatialIndexMode` |
| Group targets | four fixed-point targets |
| Group path states | resolved/pending key, revision, status and pending sequence |
| Navigation sequence | `NextPathRequestSequence` |
| Agent positions | X/Y raw by stable entity index |
| Agent velocities | X/Y raw by stable entity index |
| Agent path cursors | cursor by stable entity index |

The full hash is a fast regression and checkpoint signal, not a cryptographic signature.

When two worlds differ, the desync scan follows the same stable schema order and reports:

```text
component + entity/group index + field + expected raw + actual raw
```

This separates two questions:

- Which tick first diverged? Compare replay checkpoint hashes.
- Which scalar first differs? Compare authority layers and then scan the failing component.

A legitimate schema or logic change may change every later checkpoint. The replay schema, logic identity and source commit must therefore travel with the hash sequence.

## 4. Snapshot schema

`WorldSnapshotRing` preallocates a bounded number of slots containing:

- tick and agent count;
- position and velocity columns;
- path cursors;
- group targets;
- four `GroupPathState` values;
- `NextPathRequestSequence`;
- `SpatialIndexMode`.

Seed, group, radius, max speed, formation offset, config, navigation topology and static obstacles are immutable within an epoch and are not copied into each slot.

### Derived paths

Shared path node/waypoint arrays and `SharedPathCache` are derived storage. Their authoritative key is:

```text
resolvedStartIndex + resolvedGoalIndex + resolvedMapRevision
```

After restore, `PrepareDerivedPaths()` keeps a matching path, copies a cached path, or runs deterministic A* synchronously. Cache layout and hit rate may change recovery cost but must not change the resulting path or authority state. A derived rebuild does not consume the per-tick path-request budget.

## 5. Budgeted navigation under rollback

A target command writes or replaces the pending slot for its group. The navigation System then consumes at most the configured budget in pending-sequence order:

```mermaid
sequenceDiagram
    participant Cmd as Command timeline
    participant W as World
    participant Q as Group path slots
    participant Nav as Navigation budget
    Cmd->>W: Apply target at tick T
    W->>Q: Store pending key + sequence
    loop Fixed request budget
        Nav->>Q: Select oldest sequence
        Nav->>Nav: Connectivity check → cache/A*
        Nav->>Q: Resolve Active or Unreachable
    end
```

Resolved and pending fields plus the global request sequence are snapshotted and hashed. Restoring tick T therefore restores the same backlog and consumption order.

## 6. Late command transaction

`InjectLateCommand()` receives the authority-provided command including its original `(tick, sequence)`. Before modifying the timeline it verifies that the origin snapshot still exists for the current agent count.

```mermaid
sequenceDiagram
    participant C as Current world
    participant T as Command timeline
    participant S as Snapshot ring
    C->>S: Contains(origin tick)
    alt Restorable
        C->>T: Insert original command
        C->>S: Restore origin snapshot
        loop Until previous current tick
            T->>C: Apply canonical commands
            C->>C: Rebuild derived paths as required
            C->>C: Simulate one fixed tick
            C->>S: Refresh replayed snapshot
        end
    else Missing or mismatched slot
        C-->>C: Reject without timeline mutation
    end
```

After saving tick T, the controller removes only commands older than the earliest restorable tick and reuses fixed storage. Missing history is an explicit failure; a network implementation should request an authoritative full snapshot rather than silently clamp the origin tick.

The current command/navigation sequence is a signed 32-bit value. Long-running transport protocol work must define an epoch or tested serial-number comparison before crossing wraparound; the replay format does not turn ordinary signed ordering into a safe long-lived network sequence.

## 7. Versioned replay

`.swarmreplay` is an explicit little-endian binary envelope for reproducible simulation input. Its versioned data includes format identity, schema/logic/config identity, initialization parameters, canonical commands and checkpoint hashes. Reading validates bounds, enum/config compatibility and payload integrity before constructing runtime data.

A replay runner executes without rendering and emits checkpoint results suitable for comparison across independent processes. The file is not a snapshot replacement: commands before the initial state, static topology and logic/config identity must still agree.

The repository entry point is:

```bash
./Scripts/run-cross-process-replay.sh
```

It launches `SwarmReplayProcessRunner.CaptureFromCommandLine` and `VerifyFromCommandLine` in two separate Unity batchmode processes. The tracked evidence set is:

```text
ReplayResults/cross-process.swarmreplay
ReplayResults/capture.json
ReplayResults/verify.json
ReplayResults/latest.md
```

The capture uses a fixed workload and records replay SHA-256, schema/logic/config identity and final layered hashes. Verification requires a different process ID, deserializes and validates the same bytes, recomputes every checkpoint, and records a deliberate `AgentPositions[0].X.Raw` one-raw-unit mutation as the desync probe. Capture and verify JSON/Markdown are the public evidence; raw Unity logs remain local unless a privacy-scrubbed excerpt is required.

Recommended cross-process procedure:

1. Run `Scripts/run-cross-process-replay.sh` from a frozen commit and record the replay SHA-256.
2. Confirm capture/verify process IDs differ and execute that exact file on the same backend.
3. Compare every checkpoint, not only the final value.
4. Repeat on each runtime/architecture that will be named in the compatibility matrix.
5. On a mismatch, identify the first checkpoint and run the layered field diff on the corresponding restored states.

## 8. Authoritative UDP session

v0.4 wraps the same command timeline and rollback controller in one headless authority and two predictive clients. The server alone assigns canonical command tick/sequence. Clients never reconstruct these values from packet arrival time.

The 44-byte explicit little-endian envelope carries session/peer identity, packet sequence, ack/ackBits, sender tick, channel, flags, length and CRC32. A fixed retransmission window provides reliable control/command delivery; separate fixed request/authority buffers release application commands in canonical order. Unsigned packet/request sequences use tested half-range serial comparison.

```mermaid
flowchart LR
    A["UDP receive thread"] -->|"validate + fixed copy"| B["Datagram queue"]
    B --> C["Main-thread decoder"]
    C --> D["Ordered authority commands"]
    D --> E["RollbackController"]
    E --> F["Predicted World"]
    F --> G["Tick hash history"]
    H["Server hash telemetry"] --> G
```

The receive worker has no reference to `SwarmWorld`. Only the main thread drains the queue, applies server commands and replays simulation. `NetworkAuthorityHashHistory` records local and server values by tick; the rollback step observer rewrites speculative local samples during re-simulation. Every server sample actually received must end as confirmed. Telemetry lost by the impairment layer is intentionally not retransmitted.

The compatibility handshake rejects mismatched packet protocol, simulation logic/config, Q16.16 fractional bits, Agent/seed input or replay/snapshot/authority schema. CRC is accidental-corruption detection, not authentication.

When a late command's snapshot is missing, reconciliation enters `SnapshotRequired` and reports command/current/earliest-restorable ticks. v0.4 does not clamp the origin or mutate the confirmed state with a partial repair. See [`PROTOCOL_v0.4.md`](PROTOCOL_v0.4.md) for the complete wire and state-machine contract.

## 9. Catch-up

`QueueCatchUp(600)` models a logic backlog. The Host advances a bounded number of ticks per presentation frame and suppresses intermediate GPU upload/draw until caught up. This verifies simulation/presentation separation, not network download, full-snapshot deserialization or confirmed-tick protocol behavior.

## 10. Evidence levels

Evidence should be recorded at increasing strength:

1. two Worlds in one process, same seed and command stream;
2. on-time command versus late command plus rollback;
3. serialized replay executed by two independent processes on one backend;
4. the same replay across Mono/IL2CPP and ARM64/x64 targets;
5. three independent Player processes over real UDP with weak-network rollback convergence;
6. long-running backend, architecture, lane-count and weak-network matrices.

Only completed levels with raw artifacts bound to a source commit should be reported. A matching final hash alone cannot show where an earlier transient divergence occurred, which is why checkpoint streams and field-level diagnostics are retained.

## 11. Current network boundary

The repository contains real UDP transport, acknowledgements/retransmission, authority arbitration, bounded client prediction, late-command rollback and received-tick hash confirmation. It does not yet include:

- authenticated/encrypted internet transport, NAT traversal, congestion control or production clock discipline;
- out-of-window full/delta snapshot transfer, fragmentation or repair;
- disconnect/reconnect session state;
- dynamic map topology serialization;
- a completed Mono/IL2CPP and ARM64/x64 evidence matrix or tracked 30-minute soak.

The ordered next steps and acceptance gates are defined in [`ROADMAP_2027.md`](ROADMAP_2027.md).

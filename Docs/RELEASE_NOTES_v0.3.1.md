# Swarm-ECS-Sandbox v0.3.1

v0.3.1 adds an interactive technical lab around the v0.3.0 deterministic simulation kernel. It turns previously text-only runtime counters into five switchable inspection views without extending the authoritative state or replay schema.

## Highlights

### Five runtime views

- **Overview** relates live CPU/tick, render FPS, allocation sampling, path, ORCA, collision, limiter and hash data to the complete pipeline.
- **Navigation** draws the real 64×64 grid, blocked nodes, four shared A* routes, waypoints and targets.
- **Avoidance** samples one live Agent through the active spatial index and displays selected neighbors, obstacle/Agent ORCA lines and preferred/solved velocity.
- **Collision** displays immutable obstacle-BVH bounds, a labelled deterministic sweep probe and recent live ECS CCD contacts.
- **Rollback** displays sampled positions before and after an 18-tick late-command restore/replay transaction.

### Bounded diagnostic surfaces

- CCD contact capture uses a fixed 64-entry presentation ring.
- Neighbor and ORCA inspection copies into caller-owned fixed-capacity buffers.
- Static BVH nodes expose a read-only diagnostic query.
- Rollback correction sampling uses a fixed 48-Agent buffer.
- Automated Player capture accepts a named lab view for repeatable visual validation.

### Authority boundary

The lab is an observability layer over the existing simulation:

- technical overlays never write to component columns;
- diagnostic data is excluded from snapshots, hashes and `.swarmreplay` payloads;
- Unity `float` conversion and GL line rendering occur after the Q16.16 simulation step;
- headless benchmark timing excludes the HUD and overlays;
- the navigation and rollback experiments enter through normal authoritative commands;
- the collision sweep probe is presentation-only and labelled separately from live CCD contacts.

The simulation logic identity remains `0x35B82A1C03E5E1B7`. v0.3.0 benchmark and cross-process replay artifacts therefore remain the authority-performance/reproduction baseline; v0.3.1 adds focused diagnostic-isolation tests and Player visual evidence.

## Validation

Release validation for the tagged commit includes:

- Unity 6000.3.9f1 EditMode with zero failed or skipped cases;
- focused regression coverage proving CCD diagnostics preserve movement results, ORCA sampling preserves world state and every immutable BVH node is inspectable;
- a Universal arm64+x86_64 macOS Mono Player smoke run;
- automated full-resolution captures for all five lab views;
- static repository, metadata, path-leak and public-content hygiene checks.

Raw Unity XML/log files remain local because they can contain build-machine metadata. The GitHub Release carries a privacy-scrubbed validation summary, Player archive, selected captures and checksums.

The default hosted workflow performs static validation. Unity EditMode runs remotely only when the optional licensed job is enabled.

### Recorded v0.3.1 results

- Unity 6000.3.9f1 local EditMode: **188/188 passed**, 0 failed, 0 skipped; test execution duration 1.111 s.
- macOS Player: Universal arm64+x86_64, Mono backend, bundle version 0.3.1, deterministic twin-world probe passed and every automated capture exited with code 0.
- Visual validation: five 1600×900 captures inspected at full resolution; the navigation probe reported one rejected target, the avoidance view displayed 1,090 live obstacle lines with a sampled `1 obstacle / 8 Agent` constraint split, and the collision view retained 14 recent live CCD contacts in the observed frame.
- Signing: the application bundle passed `codesign --verify --deep --strict`; it is ad-hoc signed, has no Team ID and is not notarized.

Live counters are frame-specific diagnostic observations, not performance guarantees. Headless v0.3.0 benchmark and replay files remain the machine-readable authority baseline because v0.3.1 does not change the simulation logic identity.

## Reproduction

Open the project with Unity 6000.3.9f1, run `Assets/Scenes/SwarmSandbox.unity`, and use keys `1` through `5` to switch views. See:

- [`README.md`](../README.md) for controls and validation commands;
- [`ARCHITECTURE.md`](ARCHITECTURE.md) for diagnostic and authority boundaries;
- [`TECHNICAL_WALKTHROUGH.md`](TECHNICAL_WALKTHROUGH.md) for the source-to-view inspection route;
- [`RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md) for publication gates.

For automated Player evidence, launch the built application with:

```text
-swarmCapturePath <absolute-output.png>
-swarmCaptureView <Overview|Navigation|Avoidance|Collision|Rollback>
```

## Known limits

- The lab visualizes one sampled Agent at a time; it is not a full constraint-field debugger.
- Overlay lines are presentation floats and may render with platform-specific subpixel differences. They are not determinism evidence.
- Live CCD traces have bounded capacity and lifetime, so they are a recent diagnostic window rather than a complete event log.
- The deterministic collision probe explains sweep/contact/slide geometry but is not an authoritative Agent command.
- The core v0.3.0 limits still apply: post-ORCA motion limiting is not a strict kinodynamic solve, expanded-OBB CCD is conservative at square corners, and static topology is immutable within a rollback epoch.
- There is no real UDP session, authoritative server arbitration, out-of-window snapshot transfer or reconnect protocol in this release.
- Agent rendering still uploads all instances from CPU and has no per-instance GPU visibility, Hi-Z or HLOD.
- YooAsset/HybridCLR remains an integration boundary rather than a completed production delivery service.

## Third-party attribution

The fixed-point ORCA implementation includes an adaptation of the obstacle construction/linear-program approach from the Apache-2.0-licensed RVO2 project. See [`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md) and [`LICENSES/Apache-2.0.txt`](../LICENSES/Apache-2.0.txt).

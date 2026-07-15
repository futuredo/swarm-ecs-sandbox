# Swarm-ECS-Sandbox v0.4.0

v0.4.0 adds a real authoritative UDP session around the deterministic simulation core. One headless Player process owns command timing and authority state; two independent Player clients predict locally, reconcile server-stamped commands through bounded rollback and confirm received authority hashes.

## Added

- Explicit 44-byte little-endian UDP envelope with protocol/session/peer identity, sequence, ACK/ACK bits, logic tick, channel, payload length and header/payload CRC32.
- Tested unsigned serial-number wraparound semantics, duplicate rejection and a fixed-capacity reliable datagram retransmission window.
- Fixed-size codecs for handshake, welcome/reject, session start/complete, command request/authority, hash telemetry and `SnapshotRequired` messages.
- Compatibility handshake covering logic/config identity, Q16.16 format, agent/seed input and replay/snapshot/authority schema versions.
- Headless authoritative server and two predictive clients with fixed input delay, prediction lead, server command sequencing, application-level reliable ordering and tick hash confirmation.
- Socket receive worker that only validates and copies datagrams into a fixed-capacity queue; all `SwarmWorld`, command and rollback access remains on the main simulation thread.
- Deterministic outgoing weak-network scheduler for latency, jitter, loss, duplication and reorder injection.
- Machine-readable process reports for RTT, bytes, retransmissions, weak-network events, prediction/confirmation, rollback depth percentiles, hash confirmation and bounded queue failures.
- Automated three-process runner and tracked `NetworkResults/latest/` evidence.
- Sixth interactive Technical Lab page that exposes the packet contract, process topology, prediction/repair boundary and exact external qualification entry point without presenting the local scene as a live network session.

## Verification

- Unity 6000.3.9f1 EditMode suite: 197 passed, 0 failed, 0 skipped in local batchmode validation.
- macOS Mono Player build completed from the same source snapshot used for the tracked network qualification.
- The tracked qualification launches three distinct Player processes with 512 Agents per world for 210 ticks under latency, jitter, loss, duplication and reorder.
- Both clients receive four authoritative commands, exercise late-command rollback, confirm every received server hash sample and converge to the server's final state hash.
- Static evidence validation checks process identity, common protocol/config/logic/hash values, complete reliable streams, rollback activity, impairment activity, and zero socket/queue/pending-reliable failures.

See [`NetworkResults/latest/latest.md`](../NetworkResults/latest/latest.md) for the bound result summary and [`PROTOCOL_v0.4.md`](PROTOCOL_v0.4.md) for the wire/state-machine contract.

## Boundaries

- The implementation targets loopback/LAN technical validation. It does not provide account identity, authentication, encryption, NAT traversal, matchmaking, anti-cheat or internet-scale congestion control.
- Hash telemetry is intentionally non-reliable; the client must confirm every received sample, not samples lost in transit.
- The weak-network PRNG is deterministic for a fixed scheduling call order. Operating-system process timing can change the server tick assigned to a request across separate runs.
- An authority command outside retained history enters `SnapshotRequired`; v0.4 does not transfer or apply a replacement snapshot.
- Disconnect/reconnect, full/delta snapshots, fragmentation, state repair and no-render reconnect catch-up remain v0.5 scope.
- The tracked 210-tick run is a release qualification test. The runner supports a 54,000-tick real-time soak, but this release does not present the short run as 30-minute stability evidence.
- Existing 10,000-Agent benchmark results measure the deterministic simulation workload on `Null Device`; the network qualification uses 512 Agents per independent world and is a protocol/convergence workload, not a rendered-FPS result.

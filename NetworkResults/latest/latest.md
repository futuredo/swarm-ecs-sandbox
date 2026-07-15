# Authoritative UDP session evidence

- Version: `0.4.0`
- Processes: `3` (one authoritative server, two predictive clients)
- Agents per world: `512`
- Final tick: `210`
- Authoritative commands: `4`
- Input delay / prediction lead: `2 / 6` ticks
- Logic hash: `0x35B82A1C03E5E1B7`
- Config hash: `0xB1C69F225212E13F`
- Converged state hash: `0x30B5586BCED9B103`
- Client rollback counts: `[4, 4]`
- Client maximum rollback depth: `[10, 11]` ticks
- Confirmed authority hash samples: `[192, 189]`
- Reliable retransmissions: `23`
- Weak-network drops / duplicates / reorders: `48 / 33 / 55`
- Bounded queue drops / socket errors: `0 / 0`

The reports were emitted by three distinct Player processes over loopback UDP. The weak-network layer is deterministic and applies latency, jitter, loss, duplication and reordering before the operating-system socket send. Each client accepted the complete server-stamped command stream, rewrote speculative tick hashes during rollback replay and converged to the server's final authority hash without snapshot repair.

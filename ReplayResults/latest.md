# Cross-process deterministic replay

- Unity: 6000.3.9f1
- Capture/verify process IDs: 29915/30257 (independent: True)
- Replay schema / logic hash: 1 / `0x35B82A1C03E5E1B7`
- Config hash: `0x31B24B9577CABB7E`
- Replay SHA-256: `F69DE88B1761753868549ADE4789E6B5BE495A199B30732C17A9A9B7BF6D4BB5`
- Agents / commands / final tick: 256 / 5 / 120
- Matched checkpoints: 5/5
- Final authority hash: `0x4276D45139792AF6`
- Desync probe: `AgentPositions[0].X.Raw` raw -3031438 -> -3031437

The capture and verification commands run in separate Unity batchmode processes. This evidence covers the same macOS host, Unity version, scripting backend, and CPU architecture; it does not claim Mono/IL2CPP or ARM64/x64 equivalence.

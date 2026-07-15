#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLAYER="${SWARM_PLAYER_EXECUTABLE:-$ROOT/Builds/macOS/SwarmECS.app/Contents/MacOS/Swarm ECS Sandbox}"
OUT="${SWARM_NETWORK_OUTPUT_DIR:-$ROOT/NetworkResults/latest}"
PORT="${SWARM_NETWORK_PORT:-47040}"
FINAL_TICK="${SWARM_NETWORK_FINAL_TICK:-210}"
AGENTS="${SWARM_NETWORK_AGENTS:-512}"

if [[ ! -x "$PLAYER" ]]; then
  echo "Player executable not found: $PLAYER" >&2
  echo "Build it with SwarmProjectSetup.BuildMacPlayerFromCommandLine or set SWARM_PLAYER_EXECUTABLE." >&2
  exit 2
fi

mkdir -p "$OUT"
rm -f \
  "$OUT/server-ready.json" \
  "$OUT/server.json" \
  "$OUT/client-1.json" \
  "$OUT/client-2.json" \
  "$OUT/summary.json" \
  "$OUT/latest.md" \
  "$OUT/server.log" \
  "$OUT/client-1.log" \
  "$OUT/client-2.log"

PIDS=()
cleanup() {
  for pid in "${PIDS[@]:-}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT INT TERM

COMMON=(
  -batchmode
  -nographics
  -swarmNetPort "$PORT"
  -swarmNetOutputDir "$OUT"
  -swarmNetFinalTick "$FINAL_TICK"
  -swarmNetAgents "$AGENTS"
)

"$PLAYER" "${COMMON[@]}" \
  -logFile "$OUT/server.log" \
  -swarmNetRole server &
SERVER_PID=$!
PIDS+=("$SERVER_PID")

for _ in $(seq 1 150); do
  if [[ -f "$OUT/server-ready.json" ]]; then
    break
  fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then
    break
  fi
  sleep 0.1
done

if [[ ! -f "$OUT/server-ready.json" ]]; then
  echo "Server did not produce server-ready.json." >&2
  wait "$SERVER_PID" || true
  exit 3
fi

"$PLAYER" "${COMMON[@]}" \
  -logFile "$OUT/client-1.log" \
  -swarmNetRole client \
  -swarmNetPeerId 1 &
CLIENT_ONE_PID=$!
PIDS+=("$CLIENT_ONE_PID")

"$PLAYER" "${COMMON[@]}" \
  -logFile "$OUT/client-2.log" \
  -swarmNetRole client \
  -swarmNetPeerId 2 &
CLIENT_TWO_PID=$!
PIDS+=("$CLIENT_TWO_PID")

set +e
wait "$CLIENT_ONE_PID"; CLIENT_ONE_EXIT=$?
wait "$CLIENT_TWO_PID"; CLIENT_TWO_EXIT=$?
wait "$SERVER_PID"; SERVER_EXIT=$?
set -e
PIDS=()

if (( SERVER_EXIT != 0 || CLIENT_ONE_EXIT != 0 || CLIENT_TWO_EXIT != 0 )); then
  echo "Network process failure: server=$SERVER_EXIT client1=$CLIENT_ONE_EXIT client2=$CLIENT_TWO_EXIT" >&2
  exit 4
fi

python3 - "$OUT" "$FINAL_TICK" "$AGENTS" <<'PY'
import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

out = Path(sys.argv[1])
expected_tick = int(sys.argv[2])
expected_agents = int(sys.argv[3])
paths = {
    "server": out / "server.json",
    "client1": out / "client-1.json",
    "client2": out / "client-2.json",
}
missing = [str(path) for path in paths.values() if not path.is_file()]
if missing:
    raise SystemExit(f"Missing session reports: {missing}")

reports = {name: json.loads(path.read_text(encoding="utf-8")) for name, path in paths.items()}
server = reports["server"]
clients = (reports["client1"], reports["client2"])
hash_pattern = re.compile(r"^0x[0-9A-F]{16}$")

for name, report in reports.items():
    if report.get("success") is not True:
        raise SystemExit(f"{name} did not report success: {report.get('failure')}")
    if report.get("finalTick") != expected_tick:
        raise SystemExit(f"{name} finalTick mismatch: {report.get('finalTick')}")
    if report.get("agentCount") != expected_agents:
        raise SystemExit(f"{name} agentCount mismatch: {report.get('agentCount')}")
    for field in ("logicHash", "configHash", "finalStateHash", "authorityFinalStateHash"):
        if not hash_pattern.fullmatch(str(report.get(field, ""))):
            raise SystemExit(f"{name} has invalid {field}: {report.get(field)!r}")
    if report.get("inboundQueueDrops") != 0 or report.get("weakCapacityDrops") != 0:
        raise SystemExit(f"{name} overflowed a bounded network queue.")
    if report.get("socketErrors") != 0 or report.get("rejectedDatagrams") != 0:
        raise SystemExit(f"{name} reported transport errors.")

if server.get("role") != "server" or server.get("peerId") != 0:
    raise SystemExit("Invalid server identity in report.")
if [client.get("peerId") for client in clients] != [1, 2]:
    raise SystemExit("Client reports do not contain peer IDs 1 and 2.")
if any(client.get("role") != "client" for client in clients):
    raise SystemExit("Invalid client role in report.")
if len({report["processId"] for report in reports.values()}) != 3:
    raise SystemExit("The run must contain three distinct operating-system processes.")

common_fields = (
    "sessionId",
    "logicHash",
    "configHash",
    "finalStateHash",
    "authorityFinalStateHash",
    "authorityCommands",
    "inputDelayTicks",
    "predictionLeadTicks",
    "seed",
)
for field in common_fields:
    values = {report[field] for report in reports.values()}
    if len(values) != 1:
        raise SystemExit(f"Cross-process {field} mismatch: {sorted(values)}")

if server["authorityCommands"] != 4:
    raise SystemExit(f"Expected four authoritative commands, got {server['authorityCommands']}.")
if server.get("pendingReliablePackets") != 0:
    raise SystemExit("Server exited with unacknowledged reliable packets.")
for client in clients:
    if client.get("receivedAuthorityCommands") != server["authorityCommands"]:
        raise SystemExit(f"Client {client['peerId']} did not receive the full command stream.")
    if client.get("lateAuthorityCommands", 0) <= 0 or client.get("rollbackCount", 0) <= 0:
        raise SystemExit(f"Client {client['peerId']} did not exercise rollback.")
    if client.get("rollbackMaximumTicks", 0) <= 0:
        raise SystemExit(f"Client {client['peerId']} has no measured rollback depth.")
    if client.get("unresolvedHashMismatches") != 0 or client.get("missingLocalHashSamples") != 0:
        raise SystemExit(f"Client {client['peerId']} has unresolved authority hashes.")
    if client.get("serverHashSamples", 0) <= 0 or \
            client.get("confirmedHashSamples") != client.get("serverHashSamples"):
        raise SystemExit(f"Client {client['peerId']} did not confirm every received hash sample.")
    if client.get("pendingReliablePackets") != 0:
        raise SystemExit(f"Client {client['peerId']} exited with unacknowledged requests.")

if sum(report.get("weakLossDrops", 0) for report in reports.values()) <= 0:
    raise SystemExit("The deterministic weak-network run did not drop a packet.")
if sum(report.get("weakReorders", 0) for report in reports.values()) <= 0:
    raise SystemExit("The deterministic weak-network run did not reorder a packet.")
if sum(report.get("reliableRetransmissions", 0) for report in reports.values()) <= 0:
    raise SystemExit("The run did not exercise reliable retransmission.")

summary = {
    "version": "0.4.0",
    "timestampUtc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    "processCount": 3,
    "sessionId": server["sessionId"],
    "agentsPerWorld": expected_agents,
    "finalTick": expected_tick,
    "authorityCommands": server["authorityCommands"],
    "logicHash": server["logicHash"],
    "configHash": server["configHash"],
    "finalStateHash": server["finalStateHash"],
    "inputDelayTicks": server["inputDelayTicks"],
    "predictionLeadTicks": server["predictionLeadTicks"],
    "clientRollbackCounts": [client["rollbackCount"] for client in clients],
    "clientRollbackMaximumTicks": [client["rollbackMaximumTicks"] for client in clients],
    "clientConfirmedHashSamples": [client["confirmedHashSamples"] for client in clients],
    "reliableRetransmissions": sum(report["reliableRetransmissions"] for report in reports.values()),
    "weakLossDrops": sum(report["weakLossDrops"] for report in reports.values()),
    "weakDuplicates": sum(report["weakDuplicates"] for report in reports.values()),
    "weakReorders": sum(report["weakReorders"] for report in reports.values()),
    "inboundQueueDrops": sum(report["inboundQueueDrops"] for report in reports.values()),
    "socketErrors": sum(report["socketErrors"] for report in reports.values()),
}
(out / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")

markdown = f"""# Authoritative UDP session evidence

- Version: `{summary['version']}`
- Processes: `{summary['processCount']}` (one authoritative server, two predictive clients)
- Agents per world: `{summary['agentsPerWorld']}`
- Final tick: `{summary['finalTick']}`
- Authoritative commands: `{summary['authorityCommands']}`
- Input delay / prediction lead: `{summary['inputDelayTicks']} / {summary['predictionLeadTicks']}` ticks
- Logic hash: `{summary['logicHash']}`
- Config hash: `{summary['configHash']}`
- Converged state hash: `{summary['finalStateHash']}`
- Client rollback counts: `{summary['clientRollbackCounts']}`
- Client maximum rollback depth: `{summary['clientRollbackMaximumTicks']}` ticks
- Confirmed authority hash samples: `{summary['clientConfirmedHashSamples']}`
- Reliable retransmissions: `{summary['reliableRetransmissions']}`
- Weak-network drops / duplicates / reorders: `{summary['weakLossDrops']} / {summary['weakDuplicates']} / {summary['weakReorders']}`
- Bounded queue drops / socket errors: `{summary['inboundQueueDrops']} / {summary['socketErrors']}`

The reports were emitted by three distinct Player processes over loopback UDP. The weak-network layer is deterministic and applies latency, jitter, loss, duplication and reordering before the operating-system socket send. Each client accepted the complete server-stamped command stream, rewrote speculative tick hashes during rollback replay and converged to the server's final authority hash without snapshot repair.
"""
(out / "latest.md").write_text(markdown, encoding="utf-8")
print(json.dumps(summary, indent=2))
PY

rm -f "$OUT/server-ready.json"

echo "Authoritative UDP session passed. Evidence: $OUT"

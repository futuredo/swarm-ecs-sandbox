#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity}"
OUTPUT_DIR="${SWARM_REPLAY_OUTPUT_DIR:-$ROOT/ReplayResults}"

if [[ ! -x "$UNITY" ]]; then
  echo "Unity executable not found: $UNITY" >&2
  echo "Set UNITY_PATH to the Unity 6000.3.9f1 executable." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"

SWARM_REPLAY_OUTPUT_DIR="$OUTPUT_DIR" "$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$ROOT" \
  -executeMethod SwarmECS.Editor.SwarmReplayProcessRunner.CaptureFromCommandLine \
  -logFile "$OUTPUT_DIR/capture.log"

SWARM_REPLAY_OUTPUT_DIR="$OUTPUT_DIR" "$UNITY" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$ROOT" \
  -executeMethod SwarmECS.Editor.SwarmReplayProcessRunner.VerifyFromCommandLine \
  -logFile "$OUTPUT_DIR/verify.log"

echo "Replay evidence written to $OUTPUT_DIR"

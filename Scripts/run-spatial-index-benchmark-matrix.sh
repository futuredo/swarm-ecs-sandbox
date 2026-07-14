#!/usr/bin/env bash

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="$(sed -n 's/^m_EditorVersion: //p' "$PROJECT_ROOT/ProjectSettings/ProjectVersion.txt" | head -n 1)"
UNITY_EXECUTABLE="${UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
OUTPUT_DIRECTORY="${SWARM_BENCHMARK_OUTPUT_DIR:-$PROJECT_ROOT/BenchmarkResults}"
if [[ "$OUTPUT_DIRECTORY" != /* ]]; then
  OUTPUT_DIRECTORY="$PROJECT_ROOT/$OUTPUT_DIRECTORY"
fi
export SWARM_BENCHMARK_OUTPUT_DIR="$OUTPUT_DIRECTORY"
LOG_FILE="$OUTPUT_DIRECTORY/spatial-index-matrix.log"

if [[ ! -x "$UNITY_EXECUTABLE" ]]; then
  echo "Unity executable not found: $UNITY_EXECUTABLE" >&2
  echo "Set UNITY_EXECUTABLE to the Unity binary to use." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIRECTORY"

"$UNITY_EXECUTABLE" \
  -batchmode \
  -nographics \
  -projectPath "$PROJECT_ROOT" \
  -executeMethod SwarmECS.Editor.SwarmBenchmarkRunner.RunSpatialIndexMatrixFromCommandLine \
  -quit \
  -logFile "$LOG_FILE" \
  "$@"

echo "Spatial-index matrix written to:"
echo "  $OUTPUT_DIRECTORY/spatial-index-matrix.json"
echo "  $OUTPUT_DIRECTORY/spatial-index-matrix.md"
echo "Unity log: $LOG_FILE"

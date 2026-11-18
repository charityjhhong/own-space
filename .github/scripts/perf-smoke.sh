#!/usr/bin/env bash
# Batch-mode scene cost smoke: Main.unity plus a full outside layout per style, versus the static budget.
# Usage: .github/scripts/perf-smoke.sh   (Editor closed on app/). Exit 1 on a budget breach.
set -euo pipefail
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/app/Logs/perf"
mkdir -p "$OUT"
"$UNITY" -batchmode -nographics -quit -projectPath "$ROOT/app" -logFile "$OUT/editor.log" \
  -executeMethod OwnSpace.Diagnostics.Editor.PerfSmoke.Run || status=$?
grep -E "OwnSpace.PerfSmoke|error CS|warning CS" "$OUT/editor.log" | sort -u || true
# Unity 6000.3 on macOS can abort with 134 after a clean run: accept it only when our PASS line was logged.
if [ "${status:-0}" = "134" ] && grep -q "\[OwnSpace.PerfSmoke\] PASS" "$OUT/editor.log"; then status=0; fi
exit "${status:-0}"

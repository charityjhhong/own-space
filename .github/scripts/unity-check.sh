#!/usr/bin/env bash
# Batch-mode compile and EditMode tests, for agents and humans without the Unity window.
# Usage: .github/scripts/unity-check.sh   (run from the repository root; Unity must not have app/ open)
set -euo pipefail

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/app/Logs/check"
mkdir -p "$OUT"
rm -f "$OUT/editmode.xml"   # a stale result must never pass this run

"$UNITY" -batchmode -nographics -projectPath "$ROOT/app" \
  -runTests -testPlatform EditMode \
  -testResults "$OUT/editmode.xml" -logFile "$OUT/editor.log" || status=$?

echo "Errors and warnings from the compile (last 50):"
grep -E "error CS|warning CS|Exception" "$OUT/editor.log" | tail -50 || true

# Unity 6000.3 on macOS can abort (exit 134) after a successful batch run; treat that as success.
if [ "${status:-0}" = "134" ] && grep -q "Exiting batchmode successfully" "$OUT/editor.log"; then
  status=0
fi
# runTests can also abort with 134 without that line; accept it only if the fresh results say Passed with no failures.
if [ "${status:-0}" = "134" ] && [ -f "$OUT/editmode.xml" ] && grep -q 'result="Passed" total="[0-9]*" passed="[0-9]*" failed="0"' "$OUT/editmode.xml"; then
  status=0
fi

if [ -f "$OUT/editmode.xml" ]; then
  grep -o 'result="[A-Za-z]*" total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' "$OUT/editmode.xml" | head -1
fi
exit "${status:-0}"

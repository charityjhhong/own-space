#!/usr/bin/env bash
# Batch-mode Android APK build -> app/Builds/ownspace-v<version>-<commit>.apk
# Usage: OWNSPACE_VERSION=0.1.0 OWNSPACE_VERSION_CODE=1 .github/scripts/build-android.sh
# Optional signing env: OWNSPACE_KEYSTORE_PATH, OWNSPACE_KEYSTORE_PASS, OWNSPACE_KEYALIAS_NAME, OWNSPACE_KEYALIAS_PASS
# OWNSPACE_DEVELOPMENT=1 makes a development build (perf overlay on, Profiler attachable); never upload it.
# Without a keystore the APK is debug-signed (not uploadable). Close the Unity Editor on app/ first.
set -euo pipefail

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/app/Logs/build"
mkdir -p "$OUT"

: "${OWNSPACE_VERSION:?Set OWNSPACE_VERSION, e.g. 0.1.0}"
: "${OWNSPACE_VERSION_CODE:?Set OWNSPACE_VERSION_CODE, an integer that goes up on every upload}"
export OWNSPACE_COMMIT="${OWNSPACE_COMMIT:-$(git -C "$ROOT" rev-parse --short=7 HEAD)}"

# A release build (keystore set, not a development build) must use a new, higher version code.
if [ -n "${OWNSPACE_KEYSTORE_PATH:-}" ]; then
  if [ "${OWNSPACE_DEVELOPMENT:-0}" = "1" ]; then
    echo "NOTICE: version check SKIPPED (OWNSPACE_DEVELOPMENT=1). This development build must not be uploaded."
  elif [ "${OWNSPACE_SKIP_VERSION_CHECK:-0}" = "1" ]; then
    echo "NOTICE: version check SKIPPED (OWNSPACE_SKIP_VERSION_CHECK=1). Do not upload unless you checked the code by hand."
  else
    "$ROOT/.github/scripts/check-version-code.sh" "$OWNSPACE_VERSION_CODE"
  fi
fi

if [ -z "${OWNSPACE_KEYSTORE_PATH:-}" ]; then
  echo "WARNING: OWNSPACE_KEYSTORE_PATH not set; building a DEBUG-SIGNED APK (cannot go to the Competition channel)."
fi

"$UNITY" -batchmode -nographics -quit -projectPath "$ROOT/app" \
  -buildTarget Android -logFile "$OUT/editor.log" \
  -executeMethod OwnSpace.Editor.Build.AndroidBuild.BuildApk || status=$?

echo "Errors and warnings from the build (last 50):"
grep -E "error CS|warning CS|\[OwnSpace.Build\]|BuildFailedException|^[A-Za-z.]*Exception:" "$OUT/editor.log" | tail -50 || true

# Unity 6000.3 on macOS can abort (exit 134) after a successful batch run; treat that as success
# only if our build method logged SUCCESS and the APK exists.
if [ "${status:-0}" = "134" ] && grep -q "\[OwnSpace.Build\] SUCCESS" "$OUT/editor.log"; then
  status=0
fi

if [ "${status:-0}" = "0" ]; then
  APK="$ROOT/app/Builds/ownspace-v$OWNSPACE_VERSION-${OWNSPACE_COMMIT:0:7}.apk"
  ls -l "$APK"
  # Native ABIs inside the APK: expect only arm64-v8a (armeabi-v7a must be gone).
  echo "Native ABIs in the APK:"
  unzip -l "$APK" | awk '$NF ~ /^lib\// { split($NF, p, "/"); print "lib/" p[2] "/" }' | sort -u | sed 's/^/  /' || echo "  (no native libraries found)"
fi
exit "${status:-0}"

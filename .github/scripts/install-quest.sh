#!/usr/bin/env bash
# Build (development APK), install over USB and launch on a Quest, from the Mac.
# Usage: .github/scripts/install-quest.sh [--no-build] [--apk <path>] [--reinstall] [--logcat]
#   --no-build   reuse the newest app/Builds/*.apk instead of building
#   --apk <path> install this APK (implies --no-build)
#   --reinstall  uninstall first (needed after switching debug <-> release signing; erases app data)
#   --logcat     after launch, follow the app's log (Ctrl-C to stop)
# Env: OWNSPACE_VERSION (default 0.0.0), OWNSPACE_VERSION_CODE (default 1), OWNSPACE_DEVELOPMENT (default 1: overlay + Profiler),
#      ANDROID_SERIAL to pick one device when several are connected, ADB to point at adb.
# The headset must be in developer mode, plugged in, and you must accept "Allow USB debugging" inside it.
set -euo pipefail

PKG="com.charityjhhong.ownspace"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BUILD=1; APK=""; REINSTALL=0; LOGCAT=0
while [ $# -gt 0 ]; do
  case "$1" in
    --no-build) BUILD=0 ;;
    --apk) APK="${2:?--apk needs a path}"; BUILD=0; shift ;;
    --reinstall) REINSTALL=1 ;;
    --logcat) LOGCAT=1 ;;
    -h|--help) sed -n 2,10p "$0"; exit 0 ;;
    *) echo "Unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done

# 1. Find adb
ADB="${ADB:-$(command -v adb || true)}"
if [ -z "$ADB" ]; then
  ADB="$(ls /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb 2>/dev/null | tail -1 || true)"
fi
if [ -z "$ADB" ] || [ ! -x "$ADB" ]; then
  echo "ERROR: adb not found. Install it with 'brew install android-platform-tools' or set ADB=/path/to/adb." >&2
  exit 1
fi

# 2. Check the device BEFORE the slow build
"$ADB" start-server >/dev/null 2>&1 || true
devices="$("$ADB" devices | tail -n +2 | awk 'NF')"
if [ -z "$devices" ]; then
  echo "ERROR: no device connected. Plug the Quest in with a USB-C data cable, turn it on, and run 'adb devices'." >&2
  echo "       If it is not listed: enable Developer Mode in the Meta Horizon phone app, and try another cable or port." >&2
  exit 1
fi
if echo "$devices" | grep -q "unauthorized"; then
  echo "ERROR: the headset has not authorised this Mac. Put the headset on, accept 'Allow USB debugging' (tick 'Always allow'), run again." >&2
  exit 1
fi
if echo "$devices" | grep -q "offline"; then
  echo "ERROR: the device is offline. Unplug and replug the cable, or run 'adb kill-server', then try again." >&2
  exit 1
fi
count="$(echo "$devices" | grep -c "device$" || true)"
if [ "$count" -gt 1 ] && [ -z "${ANDROID_SERIAL:-}" ]; then
  echo "ERROR: $count devices connected. Choose one: ANDROID_SERIAL=<serial> $0 ..." >&2
  echo "$devices" >&2
  exit 1
fi
echo "Device: $(echo "$devices" | head -1)"

# 3. Build
if [ "$BUILD" = "1" ]; then
  export OWNSPACE_VERSION="${OWNSPACE_VERSION:-0.0.0}"
  export OWNSPACE_VERSION_CODE="${OWNSPACE_VERSION_CODE:-1}"
  export OWNSPACE_DEVELOPMENT="${OWNSPACE_DEVELOPMENT:-1}"
  echo "Building (version $OWNSPACE_VERSION, code $OWNSPACE_VERSION_CODE, development=$OWNSPACE_DEVELOPMENT) ..."
  "$ROOT/.github/scripts/build-android.sh"
fi
if [ -z "$APK" ]; then
  APK="$(ls -t "$ROOT"/app/Builds/*.apk 2>/dev/null | head -1 || true)"
fi
if [ -z "$APK" ] || [ ! -f "$APK" ]; then
  echo "ERROR: no APK found in app/Builds/. Run without --no-build." >&2
  exit 1
fi
echo "APK: $APK ($(du -h "$APK" | cut -f1))"

# 4. Install
if [ "$REINSTALL" = "1" ]; then
  "$ADB" uninstall "$PKG" >/dev/null 2>&1 || true
fi
if ! out="$("$ADB" install -r "$APK" 2>&1)"; then
  echo "$out" >&2
  case "$out" in
    *INSTALL_FAILED_UPDATE_INCOMPATIBLE*|*SIGNATURES*) echo "HINT: signed with a different key than the installed copy. Run again with --reinstall (erases app data)." >&2 ;;
    *INSUFFICIENT_STORAGE*) echo "HINT: the headset is out of storage. Free space in Meta Horizon > Storage." >&2 ;;
    *VERSION_DOWNGRADE*) echo "HINT: installed version code is higher. Use a higher OWNSPACE_VERSION_CODE or --reinstall." >&2 ;;
  esac
  exit 1
fi
echo "Installed."

# 5. Launch
"$ADB" shell am force-stop "$PKG" >/dev/null 2>&1 || true
if ! "$ADB" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; then
  echo "ERROR: could not launch $PKG. Start it from Library > Unknown Sources on the headset." >&2
  exit 1
fi
echo "Launched $PKG. Put the headset on."
if [ "$LOGCAT" = "1" ]; then
  "$ADB" logcat -c
  "$ADB" logcat -s Unity
fi

#!/usr/bin/env bash
# Refuse a version code that is not higher than the last one in docs/release/CHANGELOG.md.
# Usage: .github/scripts/check-version-code.sh <version code>     (exit 0 = ok, 1 = refused)
# Table rows look like: | 1 | 0.0.1 | f419063 | Competition | 2026-11-05 | summary |
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
LOG="$ROOT/docs/release/CHANGELOG.md"
CODE="${1:?Usage: $0 <version code>}"

case "$CODE" in ''|*[!0-9]*) echo "ERROR: version code must be a positive integer, got '$CODE'." >&2; exit 1 ;; esac
[ -f "$LOG" ] || { echo "ERROR: $LOG not found." >&2; exit 1; }

LAST="$(awk -F'|' '{ c=$2; gsub(/ /,"",c); if (c ~ /^[0-9]+$/ && c+0 > m) m=c+0 } END { print m+0 }' "$LOG")"
if [ "$CODE" -le "$LAST" ]; then
  echo "ERROR: version code $CODE is not higher than the last released code $LAST (docs/release/CHANGELOG.md). Use $((LAST + 1)) or more." >&2
  exit 1
fi
echo "Version code $CODE is higher than the last released code $LAST."

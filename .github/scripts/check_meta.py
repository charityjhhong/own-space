#!/usr/bin/env python3
"""Fail when a Unity asset under app/Assets has no .meta file, or a .meta file has no asset."""
import subprocess
import sys

files = set(subprocess.run(["git", "ls-files", "app/Assets"], capture_output=True, text=True, check=True).stdout.split("\n")) - {""}
dirs = {f.rsplit("/", 1)[0] for f in files if "/" in f}
# Every parent folder below app/Assets is an asset too.
all_dirs = set()
for d in dirs:
    parts = d.split("/")
    for i in range(3, len(parts) + 1):
        all_dirs.add("/".join(parts[:i]))

problems = []
for f in sorted(files):
    if f.endswith(".meta"):
        target = f[: -len(".meta")]
        if target not in files and target not in all_dirs:
            problems.append(f"orphan .meta: {f}")
    elif f + ".meta" not in files:
        problems.append(f"missing .meta: {f}")
for d in sorted(all_dirs):
    if d != "app/Assets" and d + ".meta" not in files:
        problems.append(f"missing .meta for folder: {d}")

if problems:
    print("\n".join(problems))
    sys.exit(1)
print(f"OK: {len(files)} files under app/Assets, every asset has its .meta.")

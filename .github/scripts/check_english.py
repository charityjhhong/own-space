#!/usr/bin/env python3
"""Fail when a tracked text file contains Hangul. Korean reading copies belong in the gitignored ko/ folder."""
import re
import subprocess
import sys

HANGUL = re.compile("[" + chr(0xAC00) + "-" + chr(0xD7A3) + "]")
files = subprocess.run(["git", "ls-files"], capture_output=True, text=True, check=True).stdout.splitlines()
hits = []
for path in files:
    try:
        with open(path, encoding="utf-8") as f:
            for n, line in enumerate(f, 1):
                if HANGUL.search(line):
                    hits.append(f"{path}:{n}")
                    break
    except (UnicodeDecodeError, FileNotFoundError, IsADirectoryError):
        continue  # binary or LFS pointer

if hits:
    print("Tracked files must be in English. Korean copies belong in ko/:")
    print("\n".join(hits))
    sys.exit(1)
print(f"OK: no Korean in {len(files)} tracked files.")

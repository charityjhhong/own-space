#!/usr/bin/env python3
"""Fail a PR whose branch prefix (core/, xr/, build/, content/) changes files outside that agent's paths.

Usage: check_boundaries.py <branch> <file-with-changed-paths> [--allow-cross-boundary]
Branches without an agent prefix (a human's own branch) are reported but not failed.
"""
import json
import sys
from pathlib import Path


def allowed(path: str, prefixes: list[str]) -> bool:
    return any(path == p or (p.endswith("/") and path.startswith(p)) for p in prefixes)


def main() -> int:
    branch, changed_file = sys.argv[1], sys.argv[2]
    cross_ok = "--allow-cross-boundary" in sys.argv[3:]
    rules = json.loads((Path(__file__).parent.parent / "boundaries.json").read_text())
    agent = branch.split("/", 1)[0]
    changed = [line.strip() for line in Path(changed_file).read_text().splitlines() if line.strip()]

    if agent not in rules or agent.startswith("_"):
        print(f"Branch '{branch}' has no agent prefix; boundary check skipped (human branch).")
        return 0

    outside = [p for p in changed if not allowed(p, rules[agent])]
    if not outside:
        print(f"OK: all {len(changed)} changed files are inside the '{agent}' paths.")
        return 0

    print(f"Files outside the '{agent}' paths:")
    for p in outside:
        print(f"  {p}")
    if cross_ok:
        print("Allowed by the 'cross-boundary' label. The owner of those paths must approve this PR.")
        return 0
    print("Ask the owner on the Agent sync issue, or add the 'cross-boundary' label with the owner's approval.")
    return 1


if __name__ == "__main__":
    sys.exit(main())

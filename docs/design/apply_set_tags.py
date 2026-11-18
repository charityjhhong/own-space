#!/usr/bin/env python3
"""Adds (or refreshes) the `set:<preset id>` tags in app/Assets/_Project/Content/catalog.json from Content/presets.json. Idempotent.
Usage: python3 docs/design/apply_set_tags.py <repo>
Not applied yet: core's PresetSetTests assume the real catalog carries no set tags (they tag a copy themselves), so core must make those
tests independent of catalog.json first (request on #1)."""
import json, re, sys, os

def main():
    if len(sys.argv) != 2: print(__doc__); return 2
    root = os.path.join(sys.argv[1], "app", "Assets", "_Project", "Content")
    presets = json.load(open(os.path.join(root, "presets.json")))["presets"]
    tags = {}
    for p in presets:
        for pid in p["pieceIds"]: tags.setdefault(pid, []).append(p["tag"])
    path = os.path.join(root, "catalog.json"); lines = open(path).read().split("\n"); n = 0
    for i, l in enumerate(lines):
        m = re.match(r'\s*\{"id": "([^"]+)"', l)
        if not m: continue
        keep = lambda t: [x for x in t if not x.startswith("set:")] + tags.get(m.group(1), [])
        new = re.sub(r'"tags": (\[[^\]]*\])', lambda mt: '"tags": ' + json.dumps(keep(json.loads(mt.group(1)))), l, count=1)
        if new != l: lines[i] = new; n += 1
    open(path, "w").write("\n".join(lines)); json.loads("\n".join(lines)); print(n, "items changed")
    return 0

if __name__ == "__main__":
    sys.exit(main())

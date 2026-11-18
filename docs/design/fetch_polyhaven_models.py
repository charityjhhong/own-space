#!/usr/bin/env python3
"""Downloads Poly Haven (CC0) models as 1K glTF into <dir>/<slug>/ (human-approved source). Usage: python3 fetch_polyhaven_models.py <dir> <slug>...
Prints name, author and polycount so the licence row for docs/design/asset-licenses.md can be written."""
import json, os, sys, urllib.request

UA = {"User-Agent": "OwnSpace-content-agent (CC0 import)"}
def get(u): return urllib.request.urlopen(urllib.request.Request(u, headers=UA), timeout=120).read()

def main():
    if len(sys.argv) < 3: print(__doc__); return 2
    out = sys.argv[1]
    for slug in sys.argv[2:]:
        info = json.loads(get(f"https://api.polyhaven.com/info/{slug}")); files = json.loads(get(f"https://api.polyhaven.com/files/{slug}"))
        g = files["gltf"]["1k"]["gltf"]; d = os.path.join(out, slug); os.makedirs(d, exist_ok=True)
        open(os.path.join(d, slug + ".gltf"), "wb").write(get(g["url"]))
        for rel, f in g["include"].items():
            p = os.path.join(d, rel); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, "wb").write(get(f["url"]))
        print(slug, "|", info["name"], "|", ", ".join(info["authors"]), "| polycount", info["polycount"], "| dimensions mm", [round(x) for x in info["dimensions"]])
    return 0

if __name__ == "__main__":
    sys.exit(main())

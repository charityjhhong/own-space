#!/usr/bin/env python3
"""Downloads two CC0 fabric sets from ambientCG (human-approved source, 1K): a wool carpet for rugs and a linen for curtains, converts them
(grey albedo normalised to 0.70, tinted at runtime like the tinted finishes) into app/Assets/_Project/Art/Fabrics/Tex/ and writes
docs/design/fabric-textures.json (source URL, author, licence). Usage: python3 docs/design/import_fabrics.py <repo> <download-dir>"""
import importlib.util, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("import_finishes", os.path.join(HERE, "import_finishes.py")); F = importlib.util.module_from_spec(spec); spec.loader.exec_module(F)
SETS = {"rug_wool": ("ambientcg", "Carpet016"), "curtain_linen": ("ambientcg", "Fabric032")}

def main():
    if len(sys.argv) != 3: print(__doc__); return 2
    repo, cache = sys.argv[1], sys.argv[2]; tex = os.path.join(repo, "app", "Assets", "_Project", "Art", "Fabrics", "Tex"); os.makedirs(tex, exist_ok=True); report = {}
    for name, (src, aid) in SETS.items():
        maps, meta = F.fetch(src, aid, os.path.join(cache, src))
        a = np.asarray(maps["albedo"].resize((1024, 1024), Image.LANCZOS), float) / 255
        g = np.clip((a @ np.array([0.2126, 0.7152, 0.0722])) / (a @ np.array([0.2126, 0.7152, 0.0722])).mean() * F.MEAN, 0, 1)
        Image.fromarray((np.stack([g] * 3, -1) * 255).round().astype("uint8")).save(os.path.join(tex, name + "_albedo.jpg"), quality=90)
        maps["normal"].resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, name + "_normal.jpg"), quality=92)
        maps["rough"].convert("L").resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, name + "_rough.jpg"), quality=90)
        report[name] = dict(source=meta, gain=round(1 / F.MEAN, 4))
    json.dump(report, open(os.path.join(repo, "docs", "design", "fabric-textures.json"), "w"), indent=1)
    print(len(report), "fabric sets")
    return 0

if __name__ == "__main__":
    sys.exit(main())

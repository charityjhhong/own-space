#!/usr/bin/env python3
"""Downloads four CC0 surface sets from ambientCG (human-approved source, 1K) for the floor lamps and plants: brushed metal (lamp stands),
a leaf (foliage), bark (stems) and plaster (pots). Converts them like import_fabrics.py (grey albedo normalised to 0.70 where the colour comes from the
style, the leaf keeps its own colour) into app/Assets/_Project/Art/Surfaces/Tex/ and writes docs/design/surface-textures.json (source URL, author, licence).
Wicker001 (woven baskets and screen panels) was added for the clutter covers.
Usage: python3 docs/design/import_surfaces.py <repo> <download-dir> [set names to (re)import; default all]"""
import importlib.util, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("import_finishes", os.path.join(HERE, "import_finishes.py")); F = importlib.util.module_from_spec(spec); spec.loader.exec_module(F)
SETS = {"lamp_metal": ("Metal012", True), "plant_leaf": ("Leaf001", False), "plant_bark": ("Bark006", True), "plant_pot": ("Plaster004", True), "clutter_wicker": ("Wicker001", True)}

def main():
    if len(sys.argv) < 3: print(__doc__); return 2
    repo, cache = sys.argv[1], sys.argv[2]; tex = os.path.join(repo, "app", "Assets", "_Project", "Art", "Surfaces", "Tex"); os.makedirs(tex, exist_ok=True); report = {}
    only = sys.argv[3:]
    rpath = os.path.join(repo, "docs", "design", "surface-textures.json")
    if os.path.exists(rpath): report = json.load(open(rpath))
    for name, (aid, tinted) in SETS.items():
        if only and name not in only: continue
        maps, meta = F.fetch("ambientcg", aid, os.path.join(cache, "ambientcg"))
        alb = maps["albedo"].resize((1024, 1024), Image.LANCZOS)
        if tinted:
            a = np.asarray(alb, float) / 255; lum = a @ np.array([0.2126, 0.7152, 0.0722])
            g = np.clip(lum / lum.mean() * F.MEAN, 0, 1)
            alb = Image.fromarray((np.stack([g] * 3, -1) * 255).round().astype("uint8"))
        alb.save(os.path.join(tex, name + "_albedo.jpg"), quality=90)
        maps["normal"].resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, name + "_normal.jpg"), quality=92)
        maps["rough"].convert("L").resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, name + "_rough.jpg"), quality=90)
        report[name] = dict(source=meta, tinted=tinted)
    json.dump(report, open(rpath, "w"), indent=1)
    print(len(report), "surface sets")
    return 0

if __name__ == "__main__":
    sys.exit(main())

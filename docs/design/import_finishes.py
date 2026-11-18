#!/usr/bin/env python3
"""Downloads the CC0 PBR texture sets for the floor and wall finishes (ambientCG and Poly Haven only, 1K, human-approved),
converts them to the maps Own Space uses and writes app/Assets/_Project/Art/Finishes/Tex/<finish>_{albedo,normal,rough,thumb}.jpg
and docs/design/finish-textures.json (source URL, licence, size and scale per finish).
Usage: python3 docs/design/import_finishes.py <repo> <download-dir>      (pillow, numpy)
Tinted finishes (paint, stripes, panels, concrete) get a grey albedo normalised to a mean of 0.70; the shader multiplies it by the finish colour
and a gain of 1/0.70, so the colour stays the catalog colour and the texture only adds grain, relief and gloss. Wood floors keep their colour."""
import io, json, os, re, sys, urllib.request, zipfile
import numpy as np
from PIL import Image

UA = {"User-Agent": "OwnSpace-content-agent (CC0 import)"}
SETS = {  # finish id -> (source, asset id, tinted)
    "floor-oak-planks": ("ambientcg", "WoodFloor051", False),
    "floor-birch-planks": ("ambientcg", "WoodFloor062", True),
    "floor-dark-planks": ("ambientcg", "WoodFloor064", True),
    "floor-concrete-tiles": ("ambientcg", "Concrete010", True),
    "wall-cream": ("ambientcg", "PaintedPlaster017", True),
    "wall-soft-stripes": ("polyhaven", "white_planks_clean", True),
    "wall-olive": ("ambientcg", "Plaster001", True),
    "wall-navy-panels": ("polyhaven", "dark_paneled_wood", True),
}
SCALE = {"floor-oak-planks": 1.8, "floor-birch-planks": 1.8, "floor-dark-planks": 1.8, "floor-concrete-tiles": 1.2, "wall-cream": 1.2, "wall-soft-stripes": 0.9, "wall-olive": 1.2, "wall-navy-panels": 1.4}
MEAN = 0.70

def get(u): return urllib.request.urlopen(urllib.request.Request(u, headers=UA), timeout=120).read()

def fetch(source, aid, cache):
    os.makedirs(cache, exist_ok=True); out = {}
    if source == "ambientcg":
        z = os.path.join(cache, aid + "_1K-JPG.zip")
        if not os.path.exists(z): open(z, "wb").write(get(f"https://ambientcg.com/get?file={aid}_1K-JPG.zip"))
        with zipfile.ZipFile(z) as zf:
            for n in zf.namelist():
                for k, suf in (("albedo", "_Color.jpg"), ("normal", "_NormalGL.jpg"), ("rough", "_Roughness.jpg")):
                    if n.endswith(suf): out[k] = Image.open(io.BytesIO(zf.read(n))).convert("RGB")
        d = json.loads(get(f"https://ambientcg.com/api/v2/full_json?id={aid}"))["foundAssets"][0]
        meta = dict(name=d["displayName"], author="ambientCG", url=f"https://ambientcg.com/view?id={aid}", licence="CC0-1.0", size_cm=d.get("dimensionX"))
    else:
        f = json.loads(get(f"https://api.polyhaven.com/files/{aid}"))
        for k, key in (("albedo", "Diffuse"), ("normal", "nor_gl"), ("rough", "Rough")):
            out[k] = Image.open(io.BytesIO(get(f[key]["1k"]["jpg"]["url"]))).convert("RGB")
        i = json.loads(get(f"https://api.polyhaven.com/info/{aid}"))
        meta = dict(name=i["name"], author=", ".join(i["authors"]), url=f"https://polyhaven.com/a/{aid}", licence="CC0-1.0", size_mm=i.get("dimensions"))
    return out, meta

def main():
    if len(sys.argv) != 3: print(__doc__); return 2
    repo, cache = sys.argv[1], sys.argv[2]; tex = os.path.join(repo, "app", "Assets", "_Project", "Art", "Finishes", "Tex"); os.makedirs(tex, exist_ok=True)
    report = {}
    for fid, (src, aid, tinted) in SETS.items():
        maps, meta = fetch(src, aid, os.path.join(cache, src))
        alb = maps["albedo"].resize((1024, 1024), Image.LANCZOS); a = np.asarray(alb, float) / 255
        if tinted:
            g = (a @ np.array([0.2126, 0.7152, 0.0722])); g = np.clip(g / g.mean() * MEAN, 0, 1); a = np.stack([g, g, g], -1)
        Image.fromarray((a * 255).round().astype("uint8")).save(os.path.join(tex, fid + "_albedo.jpg"), quality=90)
        maps["normal"].resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, fid + "_normal.jpg"), quality=92)
        maps["rough"].convert("L").resize((1024, 1024), Image.LANCZOS).save(os.path.join(tex, fid + "_rough.jpg"), quality=90)
        mean = (a.reshape(-1, 3).mean(0) * 255).round().astype(int).tolist()
        report[fid] = dict(source=meta, albedo_mean_rgb=mean, tinted=tinted, gain=round(1 / MEAN, 4) if tinted else 1.0, scale_m=SCALE[fid],
                           files=[fid + s for s in ("_albedo.jpg", "_normal.jpg", "_rough.jpg")])
    # thumbnails (128 px, the finish colour applied) and the texture block in finishes.json
    fj = os.path.join(repo, "app", "Assets", "_Project", "Content", "finishes.json"); txt = open(fj).read()
    base = {f["id"]: f["baseColor"] for f in json.loads(txt)["finishes"]}
    for fid, r in report.items():
        a = Image.open(os.path.join(tex, fid + "_albedo.jpg")).convert("RGB").crop((212, 212, 812, 812)).resize((128, 128), Image.LANCZOS)
        if r["tinted"]:
            c = np.array([int(base[fid][i:i + 2], 16) for i in (1, 3, 5)]) / 255 * r["gain"]
            a = Image.fromarray(np.clip(np.asarray(a, float) * c, 0, 255).astype("uint8"))
        a.save(os.path.join(tex, fid + "_thumb.png"))
        r["files"].append(fid + "_thumb.png")
        block = f'"textures": {{"albedo": "{fid}_albedo.jpg", "normal": "{fid}_normal.jpg", "roughness": "{fid}_rough.jpg", "thumbnail": "{fid}_thumb.png", "tinted": {str(r["tinted"]).lower()}, "gain": {r["gain"]}, "scaleM": {r["scale_m"]}}}'
        txt = re.sub(r'(\{"id": "%s",[^\n]*?)(, "textures": \{[^}]*\})?\}(,?)\n' % re.escape(fid), lambda m: m.group(1) + ", " + block + "}" + m.group(3) + "\n", txt, count=1)
    open(fj, "w").write(txt)
    with open(os.path.join(repo, "docs", "design", "finish-textures.json"), "w") as f: json.dump(report, f, indent=1)
    print(len(report), "finishes")
    return 0

if __name__ == "__main__":
    sys.exit(main())

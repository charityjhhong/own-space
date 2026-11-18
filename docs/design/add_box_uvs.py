#!/usr/bin/env python3
"""Adds box-projected UVs (in metres) to flat-colour OBJ models in Art/Models, so a tiling PBR set can be applied at true scale.
Usage: python3 add_box_uvs.py <obj> [<obj> ...]   (idempotent: run it again and it rewrites the same file)
Each triangle is projected along the axis its normal points to most: sides use (horizontal axis, y), tops and bottoms use (x, z).
Vertices are split per triangle, so every triangle keeps its own UV. Each material also gets a "g <material>" group: Unity's OBJ importer
ignores usemtl (one submesh for the whole file), but makes one child mesh per group, and the builder assigns materials by that child's name. The CatalogPrefabBuilder sets the tiling per material (1 / tile size).
The model is already in metres (convert_models.py scales it to the catalog footprint), so one UV unit is one metre."""
import sys

def process(path):
    pos = []; out = []; faces_out = []; cur = None; vt = []
    header = []
    # groups: list of (material, [triangles of 3 position indices])
    groups = []; name = None
    for line in open(path):
        t = line.split()
        if not t: continue
        if t[0] == "v": pos.append(tuple(float(x) for x in t[1:4]))
        elif t[0] == "o": name = t[1]
        elif t[0] == "usemtl": groups.append((t[1], []))
        elif t[0] == "f":
            idx = [int(p.split("/")[0]) - 1 for p in t[1:]]
            for k in range(1, len(idx) - 1): groups[-1][1].append((idx[0], idx[k], idx[k + 1]))
    lines = ["# Own Space import, box-projected UVs in metres (docs/design/add_box_uvs.py)", f"o {name}"]
    vlines = []; tlines = []; flines = []; n = 0
    for mat, tris in groups:
        flines.append(f"g {mat}"); flines.append(f"usemtl {mat}")   # a group per material: Unity splits the OBJ into one child mesh per group, named after the material
        for a, b, c in tris:
            p = [pos[a], pos[b], pos[c]]
            u = [p[1][i] - p[0][i] for i in range(3)]; w = [p[2][i] - p[0][i] for i in range(3)]
            nrm = (u[1] * w[2] - u[2] * w[1], u[2] * w[0] - u[0] * w[2], u[0] * w[1] - u[1] * w[0])
            ax = max(range(3), key=lambda i: abs(nrm[i]))
            for q in p:
                if ax == 1: uv = (q[0], q[2])
                elif ax == 0: uv = (q[2], q[1])
                else: uv = (q[0], q[1])
                vlines.append("v %.5f %.5f %.5f" % q); tlines.append("vt %.5f %.5f" % uv)
            flines.append(f"f {n + 1}/{n + 1} {n + 2}/{n + 2} {n + 3}/{n + 3}"); n += 3
    open(path, "w").write("\n".join(lines + vlines + tlines + flines) + "\n")
    return n // 3

if __name__ == "__main__":
    if len(sys.argv) < 2: print(__doc__); sys.exit(2)
    for p in sys.argv[1:]: print(p, process(p), "triangles")

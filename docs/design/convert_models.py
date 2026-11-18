"""Converts downloaded CC0 models into Art/Models/<source>/<id>.obj + .model.json for CatalogPrefabBuilder.
Usage: python3 convert_models.py <repo> <polyhaven-dir> <kenney-dir> [spread-limit=1.4] [result.json]
See docs/design/asset-import.md."""
import trimesh, numpy as np, json, os, sys, glob, shutil, math
import fast_simplification as fs
from PIL import Image
REPO = PHDIR = KENDIR = OUT = None
items = {}
USAGE = "usage: python3 convert_models.py <repo> <polyhaven-dir> <kenney-dir> [spread-limit=1.4] [result.json]"
PH={ # slug -> (catalog id predicate on kind/substring)
 "small_wooden_table_01":["desk","coffee-table"],"modern_wooden_cabinet":["closet","shelf"],
 "wooden_display_shelves_01":["shelf"],"Shelf_01":["shelf"],"steel_frame_shelves_03":["shelf"],
 "sofa_02":["sofa"],"mid_century_lounge_chair":["armchair"],"modern_arm_chair_01":["armchair"],
 "coffee_table_round_01":["coffee-table"],"modern_coffee_table_01":["coffee-table"],"side_table_01":["coffee-table"],
 "modern_ceiling_lamp_01":["pendant-lamp"],"hanging_industrial_lamp":["pendant-lamp"],
 "potted_plant_04":["plant"],"wicker_basket_01":["basket"],"wicker_basket_02":["basket"],
 "throw_pillows_01":["cushions","floor-cushion"],"hanging_picture_frame_01":["wall-art"],"hanging_picture_frame_02":["wall-art"]}
KEN={"bedSingle":["bed"],"rugRectangle":["rug"],"lampRoundFloor":["lamp"],"lampSquareFloor":["lamp"]}
def kinds(i):
    k=i["kind"]; r=[k]
    if k=="clutter-cover":
        for s in("basket","cushions","floor-cushion","plant"):
            if s in i["id"]: r.append(s)
    return r
def load_ph(slug):
    sc=trimesh.load(os.path.join(PHDIR, slug, slug + ".gltf"),force='scene'); return sc.dump()
def load_ken(name):
    p=os.path.join(KENDIR, "Models", "OBJ format", name + ".obj")
    m=trimesh.load(p,force='scene',process=False)
    return m.dump()
def group(parts, flat):
    """-> list of dict(name,color,tex,verts,faces,uv)"""
    groups={}
    for k,m in enumerate(parts):
        mat=getattr(m.visual,'material',None)
        name=(getattr(mat,'name',None) or f"m{k}").replace(' ','_')
        col=[0.8,0.8,0.8,1.0]; img=None
        if mat is not None:
            f=getattr(mat,'baseColorFactor',None) if hasattr(mat,'baseColorFactor') else None
            if f is not None: col=[c/255.0 for c in f]
            elif getattr(mat,'diffuse',None) is not None: col=[c/255.0 for c in mat.diffuse]
            img=getattr(mat,'baseColorTexture',None) if hasattr(mat,'baseColorTexture') else getattr(mat,'image',None)
        uv=getattr(m.visual,'uv',None)
        g=groups.setdefault(name,dict(name=name,color=col,img=img,v=[],f=[],uv=[],n=0,nimg=getattr(mat,'normalTexture',None) if mat is not None else None,rimg=getattr(mat,'metallicRoughnessTexture',None) if mat is not None else None))
        g['f'].append(m.faces+g['n']); g['v'].append(m.vertices.copy()); g['n']+=len(m.vertices)
        g['uv'].append(uv if uv is not None else np.zeros((len(m.vertices),2)))
    out=[]
    for g in groups.values():
        g['v']=np.vstack(g['v']); g['f']=np.vstack(g['f']); g['uv']=np.vstack(g['uv']); out.append(g)
    return out
def mean_col(g):
    if g['img'] is None: return g['color'][:3]
    a=np.asarray(g['img'].convert('RGB').resize((32,32)),dtype=float)/255.0
    return list(a.reshape(-1,3).mean(0)*np.array(g['color'][:3]))
BUDGET = int(os.environ.get("OWNSPACE_BUDGET", "5000"))   # triangles per piece; models above it are decimated and flattened
ONLY = set(filter(None, os.environ.get("OWNSPACE_IDS", "").split(",")))   # optional: convert only these catalog ids

def prep(groups, budget=None):
    budget = budget or BUDGET
    tot=sum(len(g['f']) for g in groups); flat=tot>budget; ratio=min(0.95,1-budget*0.96/tot) if flat else 0
    if flat:
        for g in groups:
            g['color']=mean_col(g)+[1.0]; g['img']=None; g['nimg']=None; g['rimg']=None
            if len(g['f'])>20:
                t=max(0.0,1-max(20,int(len(g['f'])*(1-ratio)))/len(g['f']))
                v,f=fs.simplify(g['v'].astype(np.float32),g['f'].astype(np.int32),target_reduction=t)
                g['v'],g['f']=v.astype(float),f; g['uv']=np.zeros((len(v),2))
    return groups,flat
def bounds(groups):
    v=np.vstack([g['v'] for g in groups]); return v.min(0),v.max(0)
def transform(groups,yaw,size):
    c,s=math.cos(math.radians(yaw)),math.sin(math.radians(yaw))
    R=np.array([[c,0,s],[0,1,0],[-s,0,c]])
    for g in groups: g['v']=g['v']@R.T
    lo,hi=bounds(groups); e=hi-lo; sc=np.array(size)/e
    for g in groups:
        g['v']=(g['v']-np.array([(lo[0]+hi[0])/2,lo[1],(lo[2]+hi[2])/2]))*sc
    return sc
def spread(model_ext,size,flat=False):
    best=None
    for yaw in((0,) if flat else (0,90)):
        e=np.array(model_ext) if yaw==0 else np.array([model_ext[2],model_ext[1],model_ext[0]])
        r=np.array(size)/e
        if flat: r=r[:2]   # a picture frame: only width and height must fit, its depth may differ
        sp=r.max()/r.min()
        if best is None or sp<best[0]: best=(sp,yaw)
    return best
def back_flip(groups,kind):
    if kind not in("sofa","armchair","bed","closet"): return False
    v=np.vstack([g['v'] for g in groups]); lo,hi=v.min(0),v.max(0)
    top=v[v[:,1]>lo[1]+0.6*(hi[1]-lo[1])]
    if len(top)<5: return False
    return top[:,2].mean()>(lo[2]+hi[2])/2   # backrest mass at +Z -> turn around
def write(slug,iid,groups,flat,srcdir):
    d=os.path.join(OUT, slug); os.makedirs(d,exist_ok=True)
    lines=["# Own Space import, see docs/design/asset-licenses.md"]; mats=[]; vo=1; tri=0
    for g in groups:
        tex=""
        if g['img'] is not None:
            tp=f"{d}/{slug}_{g['name']}.jpg"
            if not os.path.exists(tp):
                im=g['img'].convert('RGB'); im.thumbnail((1024,1024)); im.save(tp,quality=88)
            tex=f"Assets/_Project/Art/Models/{slug}/{slug}_{g['name']}.jpg"
        ntex=rtex=""
        if g['img'] is not None and g.get('nimg') is not None:
            npth=f"{d}/{slug}_{g['name']}_n.jpg"
            if not os.path.exists(npth):
                im=g['nimg'].convert('RGB'); im.thumbnail((1024,1024)); im.save(npth,quality=92)
            ntex=f"Assets/_Project/Art/Models/{slug}/{slug}_{g['name']}_n.jpg"
        if g['img'] is not None and g.get('rimg') is not None:
            rpth=f"{d}/{slug}_{g['name']}_r.jpg"
            if not os.path.exists(rpth):
                im=g['rimg'].convert('RGB').split()[1]; im.thumbnail((1024,1024)); im.save(rpth,quality=90)   # glTF packs roughness in the green channel
            rtex=f"Assets/_Project/Art/Models/{slug}/{slug}_{g['name']}_r.jpg"
        mats.append(dict(name=g['name'],normal=ntex,rough=rtex,meanLum=round(float(np.dot(mean_col(g),[0.2126,0.7152,0.0722])),4),color=[round(float(x),4) for x in (g['color'] if len(g['color'])==4 else g['color']+[1])],texture=tex,mean=[round(float(x),4) for x in mean_col(g)]))
    lines.append(f"o {iid}")
    for g in groups:
        for p in g['v']: lines.append("v %.5f %.5f %.5f"%tuple(p))
        if g['img'] is not None:
            for t in g['uv']: lines.append("vt %.5f %.5f"%(t[0],t[1]))
    vo=1;to=1
    for g in groups:
        lines.append(f"g {g['name']}"); lines.append(f"usemtl {g['name']}")   # Unity ignores usemtl but splits a child mesh per group, named after the material
        for f in g['f']:
            if g['img'] is not None: lines.append("f "+" ".join(f"{vo+i}/{to+i}" for i in f))
            else: lines.append("f "+" ".join(f"{vo+i}" for i in f))
        vo+=len(g['v']); to+=len(g['v']) if g['img'] is not None else 0; tri+=len(g['f'])
    open(f"{d}/{iid}.obj","w").write("\n".join(lines)+"\n")
    json.dump(dict(id=iid,slug=slug,tris=int(tri),flat=flat,materials=mats),open(f"{d}/{iid}.model.json","w"),indent=1)
    return tri
def main(argv):
    global REPO, PHDIR, KENDIR, OUT, items
    if len(argv) not in (4, 5, 6):
        print(USAGE, file=sys.stderr); return 2
    REPO, PHDIR, KENDIR = argv[1], argv[2], argv[3]
    LIMIT = float(argv[4]) if len(argv) > 4 else 1.4
    result_path = argv[5] if len(argv) > 5 else None
    OUT = os.path.join(REPO, "app", "Assets", "_Project", "Art", "Models")
    with open(os.path.join(REPO, "app", "Assets", "_Project", "Content", "catalog.json")) as f:
        items = {i["id"]: i for i in json.load(f)["items"]}
    result={}
    srcs={}
    for slug in PH: srcs[slug]=("ph",slug,PH[slug])
    for n in KEN: srcs[n]=("ken",n,KEN[n])
    best={}
    for key,(kind_src,name,kk) in srcs.items():
        if ONLY and not any(i in ONLY and any(k in kinds(items[i]) for k in kk) for i in items): continue
        parts=load_ph(name) if kind_src=="ph" else load_ken(name)
        v=np.vstack([p.vertices for p in parts]); ext=v.max(0)-v.min(0)
        for iid,it in items.items():
            if not any(k in kinds(it) for k in kk): continue
            if kind_src=="ph" and it["kind"]=="rug": continue
            if name=="hanging_industrial_lamp" and not set(it["styles"])&{"cafe","night-city"}: continue
            sp,yaw=spread(ext,[it["size"]["x"],it["size"]["y"],it["size"]["z"]],flat=(it["kind"]=="wall-art"))
            if iid not in best or sp<best[iid][0]: best[iid]=(sp,yaw,key)
    for iid,(sp,yaw,key) in sorted(best.items()):
        ok=sp<=LIMIT
        print(("USE " if ok else "skip"),iid.ljust(36),key.ljust(28),"spread %.2f yaw %d"%(sp,yaw))
        if not ok: continue
        kind_src,name,_=srcs[key]; it=items[iid]
        parts=load_ph(name) if kind_src=="ph" else load_ken(name)
        groups,flat=prep([g for g in group(parts,False) if 'glass' not in g['name'].lower()])   # the furniture shader is opaque: glass parts would turn into dark solids
        size=[it["size"]["x"],it["size"]["y"],it["size"]["z"]]
        transform(groups,yaw,size)
        if back_flip(groups,it["kind"]): transform(groups,180,size); print("   flipped to put the back at -Z")
        tri=write(name,iid,groups,flat,None); result[iid]=dict(slug=name,src=kind_src,tris=tri,flat=flat)
    if result_path:
        with open(result_path, "w") as f: json.dump(result, f, indent=1)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

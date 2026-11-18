"""Renders the Own Space post art backdrops in Blender (Cycles): one room, twice.
  before: the room as it is: bare grey walls, concrete floor, cool dim light, only the real desk and laptop.
  after:  the same room redesigned: oak floor, cream walls, the app's own CC0 furniture (Poly Haven), warm light.
The real desk is identical in both: it stays real.
Run (about 2 to 4 minutes on Apple silicon):
  /Applications/Blender.app/Contents/MacOS/Blender -b -P submission/art/render_room.py -- <out-dir> [samples]
Writes <out-dir>/room-before.png and room-after.png (1600 x 1200). make_post_art.py builds the thumbnails from them."""
import bpy, math, os, sys
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ART = os.path.join(ROOT, "app", "Assets", "_Project", "Art")
M = os.path.join(ART, "Models")
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else os.path.join(ROOT, "submission", "art", "post", "render")
SAMPLES = int(argv[1]) if len(argv) > 1 else 160
os.makedirs(OUT, exist_ok=True)

W, D, H = 4.6, 4.4, 2.6          # room width (x), depth (y), height (z); back wall at y = D
WIN = (0.5, 2.0, 0.85, 2.25)      # window on the back wall: x0, x1, z0, z1

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.engine = "CYCLES"
    prefs = bpy.context.preferences.addons["cycles"].preferences
    try:
        prefs.compute_device_type = "METAL"; prefs.get_devices()
        for d in prefs.devices: d.use = True
        s.cycles.device = "GPU"
    except Exception:
        s.cycles.device = "CPU"
    s.cycles.samples = SAMPLES; s.cycles.use_denoising = True
    s.render.resolution_x, s.render.resolution_y = 1600, 1200
    s.view_settings.view_transform = "AgX"; s.view_settings.look = "AgX - Base Contrast"
    s.render.film_transparent = False
    return s

def img(path, color=True):
    im = bpy.data.images.load(path, check_existing=True)
    if not color: im.colorspace_settings.name = "Non-Color"
    return im

def pbr(name, albedo=None, normal=None, rough=None, color=(0.8, 0.8, 0.8), roughness=0.6, scale=None, emission=None, strength=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = roughness
    b.inputs["Base Color"].default_value = (*color, 1)
    uv = None
    if scale:
        tc = nt.nodes.new("ShaderNodeTexCoord"); mp = nt.nodes.new("ShaderNodeMapping")
        mp.inputs["Scale"].default_value = (scale, scale, scale); nt.links.new(tc.outputs["UV"], mp.inputs["Vector"]); uv = mp.outputs["Vector"]
    def tex(path, color):
        t = nt.nodes.new("ShaderNodeTexImage"); t.image = img(path, color)
        if uv: nt.links.new(uv, t.inputs["Vector"])
        return t
    if albedo and os.path.exists(albedo): nt.links.new(tex(albedo, True).outputs["Color"], b.inputs["Base Color"])
    if rough and os.path.exists(rough): nt.links.new(tex(rough, False).outputs["Color"], b.inputs["Roughness"])
    if normal and os.path.exists(normal):
        nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(tex(normal, False).outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    if emission:
        b.inputs["Emission Color"].default_value = (*emission, 1); b.inputs["Emission Strength"].default_value = strength
    return m

def box(name, x0, x1, y0, y1, z0, z1, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    o = bpy.context.object; o.name = name; o.scale = (x1 - x0, y1 - y0, z1 - z0)
    bpy.ops.object.transform_apply(scale=True)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode="OBJECT")
    o.data.materials.append(mat); return o

def model(folder, obj, tex, at, yaw=0.0, height=None):
    """Imports an app model, puts its feet on the floor at `at` (x, y, z), turned `yaw` degrees."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=os.path.join(M, folder, obj))
    objs = [o for o in bpy.data.objects if o not in before]
    base = os.path.join(M, folder, tex)
    mat = pbr(obj, base + ".jpg", base + "_n.jpg", base + "_r.jpg", roughness=0.7)
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(mat)
        for p in o.data.polygons: p.use_smooth = True
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    s = height / (hi.z - lo.z) if height else 1.0
    piv = bpy.data.objects.new(obj + "_pivot", None); bpy.context.collection.objects.link(piv)
    for o in objs:
        o.location -= centre; o.parent = piv
    piv.scale = (s, s, s); piv.rotation_euler = (0, 0, math.radians(yaw)); piv.location = Vector(at)
    return piv

def room(after):
    if after:
        F = os.path.join(ART, "Finishes", "Tex")
        floor = pbr("floor", F + "/floor-oak-planks_albedo.jpg", F + "/floor-oak-planks_normal.jpg", F + "/floor-oak-planks_rough.jpg", scale=0.6)
        wall = pbr("wall", None, F + "/wall-cream_normal.jpg", F + "/wall-cream_rough.jpg", color=(0.86, 0.79, 0.68), scale=0.5)
        trim = pbr("trim", color=(0.92, 0.9, 0.86), roughness=0.4)
    else:
        floor = pbr("floor", color=(0.42, 0.42, 0.41), roughness=0.85)
        wall = pbr("wall", color=(0.62, 0.62, 0.61), roughness=0.9)
        trim = wall
    ceil = pbr("ceiling", color=(0.85, 0.85, 0.84), roughness=0.9)
    t = 0.1
    box("floor", -W / 2 - t, W / 2 + t, -1.5, D + t, -t, 0, floor)
    box("ceiling", -W / 2 - t, W / 2 + t, -1.5, D + t, H, H + t, ceil)
    box("wall_l", -W / 2 - t, -W / 2, -1.5, D, 0, H, wall)
    box("wall_r", W / 2, W / 2 + t, -1.5, D, 0, H, wall)
    x0, x1, z0, z1 = WIN
    box("back_a", -W / 2, x0, D, D + t, 0, H, wall); box("back_b", x1, W / 2, D, D + t, 0, H, wall)
    box("back_c", x0, x1, D, D + t, 0, z0, wall); box("back_d", x0, x1, D, D + t, z1, H, wall)
    frame = pbr("frame", color=(0.95, 0.95, 0.94), roughness=0.35)
    box("sill", x0 - 0.05, x1 + 0.05, D - 0.06, D, z0 - 0.03, z0, frame)
    box("mullion", (x0 + x1) / 2 - 0.02, (x0 + x1) / 2 + 0.02, D, D + 0.04, z0, z1, frame)
    # daylight beyond the window: a bright sky card, and the sun through it
    sky = pbr("sky", color=(0, 0, 0), emission=(0.75, 0.86, 1.0) if after else (0.7, 0.75, 0.8), strength=6.0 if after else 2.5)
    box("sky", x0 - 1, x1 + 1, D + 1.5, D + 1.6, z0 - 1, z1 + 1, sky)
    if after:
        base = pbr("baseboard", color=(0.93, 0.92, 0.89), roughness=0.4)
        box("base_l", -W / 2, -W / 2 + 0.015, 0, D, 0, 0.1, base); box("base_r", W / 2 - 0.015, W / 2, 0, D, 0, 0.1, base)
        box("base_b", -W / 2, W / 2, D - 0.015, D, 0, 0.1, base)
        box("crown_l", -W / 2, -W / 2 + 0.03, 0, D, H - 0.07, H, base); box("crown_r", W / 2 - 0.03, W / 2, 0, D, H - 0.07, H, base)
        box("crown_b", -W / 2, W / 2, D - 0.03, D, H - 0.07, H, base)

def desk():
    """The real desk, front left against the wall: identical before and after."""
    model("small_wooden_table_01", "desk-cafe-01.obj", "small_wooden_table_01_small_wooden_table_01", (-1.75, 1.55, 0), yaw=90, height=0.74)
    mug = pbr("mug", color=(0.9, 0.9, 0.88), roughness=0.25)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.04, depth=0.1, location=(-1.6, 1.35, 0.79)); bpy.context.object.data.materials.append(mug)
    book = pbr("book", color=(0.25, 0.32, 0.45), roughness=0.6)
    box("book", -1.95, -1.70, 1.6, 1.78, 0.74, 0.77, book)

def furniture():
    model("sofa_02", "sofa-cafe-01.obj", "sofa_02_sofa_02", (-0.6, D - 0.55, 0), yaw=180)
    model("modern_coffee_table_01", "coffee-table-warm-wood-01.obj", "modern_coffee_table_01_modern_coffee_table_01", (-0.55, D - 1.65, 0), yaw=0)
    model("mid_century_lounge_chair", "armchair-cafe-01.obj", "mid_century_lounge_chair_mid_century_lounge_chair", (0.95, D - 1.7, 0), yaw=-125)
    model("potted_plant_04", "clutter-plant-cafe-01.obj", "potted_plant_04_potted_plant_04", (-W / 2 + 0.35, D - 0.35, 0), height=1.25)
    model("Shelf_01", "shelf-cafe-01.obj", "Shelf_01_Shelf_01", (W / 2 - 0.2, D - 1.9, 0), yaw=-90)
    model("hanging_industrial_lamp", "pendant-cafe-01.obj", "hanging_industrial_lamp_hanging_industrial_lamp", (-0.55, D - 1.65, H - 0.75))
    rugf = os.path.join(ART, "Fabrics", "Tex", "rug_wool")
    rug = pbr("rug", rugf + "_albedo.jpg", rugf + "_normal.jpg", rugf + "_rough.jpg", scale=1.5, roughness=0.95)
    box("rug", -1.85, 0.75, D - 2.55, D - 0.75, 0, 0.012, rug)

def lights(after):
    s = bpy.context.scene; w = bpy.data.worlds.new("w"); s.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.6, 0.7, 0.85, 1) if after else (0.5, 0.52, 0.55, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.4 if after else 0.25
    x0, x1, z0, z1 = WIN
    a = bpy.data.lights.new("window", "AREA"); a.shape = "RECTANGLE"; a.size, a.size_y = x1 - x0, z1 - z0
    a.energy = 900 if after else 260; a.color = (1.0, 0.96, 0.9) if after else (0.85, 0.9, 1.0)
    o = bpy.data.objects.new("window", a); bpy.context.collection.objects.link(o)
    o.location = ((x0 + x1) / 2, D + 0.12, (z0 + z1) / 2); o.rotation_euler = (math.radians(90), 0, 0); o.visible_camera = False
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 4.0 if after else 0.0; sun.angle = math.radians(2); sun.color = (1.0, 0.93, 0.82)
    so = bpy.data.objects.new("sun", sun); bpy.context.collection.objects.link(so); so.rotation_euler = (math.radians(-62), math.radians(-18), math.radians(0))
    fill = bpy.data.lights.new("fill", "AREA"); fill.size = 2.5; fill.energy = 140 if after else 90; fill.color = (1.0, 0.92, 0.82) if after else (0.9, 0.93, 1.0)
    fo = bpy.data.objects.new("fill", fill); bpy.context.collection.objects.link(fo); fo.location = (0, 0.4, H - 0.05); fo.rotation_euler = (0, 0, 0); fo.visible_camera = False
    if after:
        bulb = bpy.data.lights.new("pendant", "POINT"); bulb.energy = 60; bulb.color = (1.0, 0.78, 0.5); bulb.shadow_soft_size = 0.08
        bo = bpy.data.objects.new("pendant", bulb); bpy.context.collection.objects.link(bo); bo.location = (-0.55, D - 1.65, H - 0.85)

def camera():
    c = bpy.data.cameras.new("cam"); c.lens = 22; c.sensor_width = 36
    o = bpy.data.objects.new("cam", c); bpy.context.collection.objects.link(o)
    o.location = (-1.25, -0.55, 1.35)
    target = Vector((0.15, D - 0.9, 0.95)); d = target - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = o

def render(after, name):
    reset(); room(after); desk()
    bpy.context.scene.view_settings.exposure = 0.0 if after else -0.9
    if after: furniture()
    lights(after); camera()
    s = bpy.context.scene; s.render.filepath = os.path.join(OUT, name); bpy.ops.render.render(write_still=True)

render(False, "room-before.png")
render(True, "room-after.png")
print("rendered", OUT)

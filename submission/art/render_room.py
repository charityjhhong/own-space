"""Renders the Own Space post art backdrops in Blender (Cycles): one room, twice, from the same camera.
  before: the room as it is: bare grey walls and floor, flat cool light, only the real desk.
  after:  the same room made your own: oak floor, warm walls, curtains, a sofa under framed art, plants, a floor lamp,
          a rug and a coffee table, in warm evening light. All furniture is the app's own CC0 set (Poly Haven).
The real desk is identical in both. The sofa sits on the image centre, so a 50/50 split shows half a room with no
sofa (before) and half a room with it (after); the framed art hangs on the right wall.
Run (about 3 to 6 minutes on Apple silicon):
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

W, D, H = 4.8, 4.4, 2.6           # room width (x), depth (y), height (z); back wall at y = D
WIN = (-1.75, -0.45, 0.85, 2.25)   # window on the back wall, left half: x0, x1, z0, z1
SOFA_X = 0.2                       # the sofa sits on the image centre: the split cuts it in half

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
    return s

def img(path, color=True):
    im = bpy.data.images.load(path, check_existing=True)
    if not color: im.colorspace_settings.name = "Non-Color"
    return im

def pbr(name, albedo=None, normal=None, rough=None, color=(0.8, 0.8, 0.8), roughness=0.6, scale=None,
        emission=None, strength=0.0, metallic=0.0, transmission=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = roughness; b.inputs["Metallic"].default_value = metallic
    b.inputs["Base Color"].default_value = (*color, 1)
    if transmission: b.inputs["Transmission Weight"].default_value = transmission
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

def slot_material(folder, name, glow):
    """The app's textures for one OBJ material: <folder>_<material>.jpg, _n, _r; glass and plain lamp shades by name."""
    base = os.path.join(M, folder, folder + "_" + name)
    if name.endswith("glass"): return pbr(name, color=(0.95, 0.95, 0.95), roughness=0.05, transmission=1.0)
    if os.path.exists(base + ".jpg"): return pbr(name, base + ".jpg", base + "_n.jpg", base + "_r.jpg", roughness=0.7)
    if name == "lamp": return pbr(name, color=(1.0, 0.9, 0.72), roughness=0.8, emission=(1.0, 0.78, 0.5), strength=glow)
    return pbr(name, color=(0.12, 0.12, 0.12), roughness=0.35, metallic=0.8)

def model(folder, obj, at, yaw=0.0, height=None, glow=0.0, on_wall=False):
    """Imports an app model and puts it at `at` (x, y, z): feet on z, or its centre at `at` when on_wall."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=os.path.join(M, folder, obj))
    objs = [o for o in bpy.data.objects if o not in before]
    for o in objs:
        for slot in o.material_slots:
            name = (slot.material.name if slot.material else "default").split(".")[0]
            slot.material = slot_material(folder, name, glow)
        for p in o.data.polygons: p.use_smooth = True
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, (lo.z + hi.z) / 2 if on_wall else lo.z))
    s = height / (hi.z - lo.z) if height else 1.0
    piv = bpy.data.objects.new(obj + "_pivot", None); bpy.context.collection.objects.link(piv)
    for o in objs:
        o.location -= centre; o.parent = piv
    piv.scale = (s, s, s); piv.rotation_euler = (0, 0, math.radians(yaw)); piv.location = Vector(at)
    return piv

def room(after):
    F = os.path.join(ART, "Finishes", "Tex")
    if after:
        floor = pbr("floor", F + "/floor-oak-planks_albedo.jpg", F + "/floor-oak-planks_normal.jpg", F + "/floor-oak-planks_rough.jpg", scale=0.6)
        wall = pbr("wall", None, F + "/wall-cream_normal.jpg", F + "/wall-cream_rough.jpg", color=(0.80, 0.70, 0.58), scale=0.5)
    else:
        floor = pbr("floor", color=(0.40, 0.40, 0.39), roughness=0.85)
        wall = pbr("wall", color=(0.60, 0.60, 0.59), roughness=0.9)
    ceil = pbr("ceiling", color=(0.85, 0.84, 0.82), roughness=0.9)
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
    sky = pbr("sky", color=(0, 0, 0), emission=(1.0, 0.72, 0.48) if after else (0.7, 0.74, 0.8), strength=3.5 if after else 2.0)
    box("sky", x0 - 1.5, x1 + 1.5, D + 1.5, D + 1.6, z0 - 1, z1 + 1.5, sky)
    if after:
        trim = pbr("trim", color=(0.93, 0.91, 0.87), roughness=0.4)
        box("base_l", -W / 2, -W / 2 + 0.015, 0, D, 0, 0.1, trim); box("base_r", W / 2 - 0.015, W / 2, 0, D, 0, 0.1, trim)
        box("base_b", -W / 2, W / 2, D - 0.015, D, 0, 0.1, trim)
        box("crown_l", -W / 2, -W / 2 + 0.03, 0, D, H - 0.07, H, trim); box("crown_r", W / 2 - 0.03, W / 2, 0, D, H - 0.07, H, trim)
        box("crown_b", -W / 2, W / 2, D - 0.03, D, H - 0.07, H, trim)
        lin = os.path.join(ART, "Fabrics", "Tex", "curtain_linen")
        curtain = pbr("curtain", lin + "_albedo.jpg", lin + "_normal.jpg", lin + "_rough.jpg", scale=2.0, roughness=0.9)
        box("curtain_l", x0 - 0.45, x0 + 0.05, D - 0.14, D - 0.1, 0.05, H - 0.1, curtain)
        box("curtain_r", x1 - 0.05, x1 + 0.45, D - 0.14, D - 0.1, 0.05, H - 0.1, curtain)
        rail = pbr("rail", color=(0.15, 0.13, 0.12), roughness=0.4, metallic=0.7)
        box("rail", x0 - 0.55, x1 + 0.55, D - 0.14, D - 0.1, H - 0.12, H - 0.1, rail)

def desk():
    """The real desk, front left against the left wall: identical before and after."""
    model("small_wooden_table_01", "desk-cafe-01.obj", (-1.85, 1.45, 0), yaw=90, height=0.74)
    mug = pbr("mug", color=(0.9, 0.9, 0.88), roughness=0.25)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.04, depth=0.1, location=(-1.7, 1.25, 0.79)); bpy.context.object.data.materials.append(mug)
    book = pbr("book", color=(0.25, 0.32, 0.45), roughness=0.6)
    box("book", -2.05, -1.80, 1.5, 1.68, 0.74, 0.77, book)

def furniture():
    y = D - 0.5
    model("sofa_02", "sofa-cafe-01.obj", (SOFA_X, y, 0), yaw=180)
    model("hanging_picture_frame_02", "wall-art-warm-wood-01.obj", (W / 2 - 0.03, D - 1.05, 1.6), yaw=-90, on_wall=True)
    model("hanging_picture_frame_01", "wall-art-scandi-01.obj", (W / 2 - 0.03, D - 2.0, 1.65), yaw=-90, on_wall=True)
    model("modern_coffee_table_01", "coffee-table-warm-wood-01.obj", (SOFA_X, D - 1.55, 0))
    model("potted_plant_04", "clutter-plant-cafe-01.obj", (SOFA_X + 0.05, D - 1.55, 0.42), height=0.35)
    model("potted_plant_04", "clutter-plant-cafe-01.obj", (W / 2 - 0.4, D - 0.4, 0), height=1.4)
    model("lampRoundFloor", "lamp-scandi-01.obj", (SOFA_X + 1.1, D - 0.4, 0), height=1.5, glow=6.0)
    model("mid_century_lounge_chair", "armchair-cafe-01.obj", (W / 2 - 0.65, D - 2.15, 0), yaw=-125)
    model("modern_ceiling_lamp_01", "pendant-warm-wood-01.obj", (SOFA_X, D - 1.55, H - 0.6))
    rugf = os.path.join(ART, "Fabrics", "Tex", "rug_wool")
    rug = pbr("rug", rugf + "_albedo.jpg", rugf + "_normal.jpg", rugf + "_rough.jpg", scale=1.5, roughness=0.95)
    box("rug", SOFA_X - 1.15, SOFA_X + 1.15, D - 2.45, D - 0.85, 0, 0.012, rug)

def light(name, kind, energy, color, loc, rot=(0, 0, 0), size=None):
    l = bpy.data.lights.new(name, kind); l.energy = energy; l.color = color
    if size is not None:
        if kind == "AREA": l.size = size
        else: l.shadow_soft_size = size
    o = bpy.data.objects.new(name, l); bpy.context.collection.objects.link(o); o.location = loc; o.rotation_euler = rot
    o.visible_camera = False
    return l

def lights(after):
    s = bpy.context.scene; w = bpy.data.worlds.new("w"); s.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.9, 0.7, 0.55, 1) if after else (0.5, 0.52, 0.55, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.25
    x0, x1, z0, z1 = WIN
    a = light("window", "AREA", 700 if after else 300, (1.0, 0.8, 0.6) if after else (0.85, 0.9, 1.0),
              ((x0 + x1) / 2, D + 0.12, (z0 + z1) / 2), (math.radians(90), 0, 0))
    a.shape = "RECTANGLE"; a.size, a.size_y = x1 - x0, z1 - z0
    if after:
        sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.0; sun.angle = math.radians(3); sun.color = (1.0, 0.7, 0.45)
        so = bpy.data.objects.new("sun", sun); bpy.context.collection.objects.link(so); so.rotation_euler = (math.radians(-72), math.radians(-30), 0)
        light("floorlamp", "POINT", 90, (1.0, 0.72, 0.42), (SOFA_X + 1.1, D - 0.4, 1.35), size=0.12)
        light("pendant", "POINT", 45, (1.0, 0.75, 0.45), (SOFA_X, D - 1.55, H - 0.75), size=0.1)
        light("fill", "AREA", 70, (1.0, 0.85, 0.7), (0, 0.6, H - 0.05), size=3.0)
    else:
        light("fill", "AREA", 110, (0.9, 0.93, 1.0), (0, 0.6, H - 0.05), size=3.0)

def camera():
    c = bpy.data.cameras.new("cam"); c.lens = 22; c.sensor_width = 36
    o = bpy.data.objects.new("cam", c); bpy.context.collection.objects.link(o)
    o.location = (-1.0, -0.6, 1.35)
    target = Vector((0.1, D - 0.9, 1.0)); d = target - o.location
    o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = o

def render(after, name):
    reset(); room(after); desk()
    if after: furniture()
    lights(after); camera()
    s = bpy.context.scene; s.view_settings.exposure = 0.0 if after else -0.6
    s.render.filepath = os.path.join(OUT, name); bpy.ops.render.render(write_still=True)

render(False, "room-before.png")
render(True, "room-after.png")
print("rendered", OUT)

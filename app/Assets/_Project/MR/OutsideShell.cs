using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.MR
{
    /// <summary>
    /// The outside zone in VR (CONCEPT 4, 5.3): opaque virtual floor, walls and ceiling aligned to the
    /// MRUK room, with passthrough kept in my zone, in windows and doors, and on real furniture in my
    /// zone (hole geometry drawn first, writing depth). The floor fades into passthrough over a band
    /// outside the zone edge. Three draw calls; rebuilt only when the room, zone or design changes.
    /// Hidden while there is no scan, no zone, or the zone is being edited.
    /// Finishes (CONCEPT 5.4): the outside floor and walls use the outside finish (or a default); my
    /// zone stays real unless a my-zone floor or wall finish is chosen, which then covers that real
    /// surface (real furniture, windows and doors still show). Openings in virtual walls get content's
    /// curtain or door trim, sized with <see cref="OpeningFrame.Fit"/>, in the style of that wall's finish.
    /// </summary>
    public sealed class OutsideShell : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        public const int FloorSubmesh = 0, WallSubmesh = 1, CeilingSubmesh = 2, MyFloorSubmesh = 3, MyWallSubmesh = 4, BaseboardSubmesh = 5, Submeshes = 6;
        const string DefaultStyle = "warm-wood";

        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] ZoneSetup _zone;
        [SerializeField, Tooltip("Floor, wall and ceiling materials until a finish is chosen (opaque URP).")] Material[] _surfaces = new Material[3];
        [SerializeField, Tooltip("OwnSpace/PassthroughHole")] Material _hole;
        [SerializeField, Tooltip("OwnSpace/SeamFade")] Material _seam;
        [SerializeField, Tooltip("content's finish id -> material map.")] FinishSet _finishes;
        [SerializeField, Tooltip("content's catalog id -> prefab map, for curtains and door trims.")] CatalogPrefabs _prefabs;
        [SerializeField, Tooltip("Width of the soft seam on the floor outside my zone (m).")] float _seamWidth = 0.35f;
        [SerializeField, Tooltip("Width of the soft fades on walls (zone border, under the real ceiling) and the ceiling (m).")] float _edgeWidth = 0.3f;
        [SerializeField, Tooltip("Real furniture in my zone keeps this much passthrough around it (m).")] float _furniturePad = 0.02f;

        // Build-time buffers, reused across rebuilds.
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>();
        Vector2 _centre;
        float _midY;
        readonly List<Color> _c = new List<Color>();
        readonly List<int>[] _sub = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        readonly List<Vector2> _board = new List<Vector2>();
        readonly Dictionary<Material, Material> _boards = new Dictionary<Material, Material>();

        /// <summary>The baseboard (realism pass): its height and how far it stands out from the wall (m).</summary>
        public const float BoardHeight = 0.08f, BoardDepth = 0.012f;

        /// <summary>The crown molding (the human: "a baseboard on the ceiling too"): its height and depth (m).</summary>
        public const float CrownHeight = 0.06f, CrownDepth = 0.012f;

        /// <summary>The baseboard's tone for a wall finish's accent colour (pure, for tests): a little darker, opaque.</summary>
        public static Color BoardTone(Color accent)
        {
            Color c = accent * 0.85f;
            c.a = 1f;
            return c;
        }

        // One baseboard material per wall material, made once: plain (no texture or pattern), matte, the accent's darker tone.
        Material BoardFor(Material wall)
        {
            if (wall == null) return null;
            if (_boards.TryGetValue(wall, out Material b)) return b;
            b = new Material(wall) { name = wall.name + " (baseboard)" };
            Color accent = wall.HasProperty(AccentId) ? wall.GetColor(AccentId) : wall.HasProperty(BaseColorId) ? wall.GetColor(BaseColorId) * 0.8f : Color.grey;
            if (b.HasProperty(BaseColorId)) b.SetColor(BaseColorId, BoardTone(accent));
            if (b.HasProperty(UseTexId)) b.SetFloat(UseTexId, 0f);
            if (b.HasProperty(PatternId)) b.SetFloat(PatternId, 0f);
            if (b.HasProperty(GainId)) b.SetFloat(GainId, 1f);
            if (b.HasProperty(MaxSmoothId)) b.SetFloat(MaxSmoothId, 0.25f);
            _boards[wall] = b;
            return b;
        }

        static readonly int AccentId = Shader.PropertyToID("_AccentColor");

        /// <summary>
        /// The wall spans with the doorways taken out (pure, for tests): a door whose centre is within 0.25 m of wall ab
        /// removes its width (projected on the wall) from <paramref name="spans"/>, so the baseboard never runs across a door.
        /// </summary>
        public static void TakeDoors(Vec2 a, Vec2 b, IReadOnlyList<RoomElement> doors, List<Vector2> spans) =>
            TakeOpenings(a, b, doors, spans, float.NegativeInfinity);

        /// <summary>
        /// The wall spans with the openings that reach the ceiling taken out (pure, for tests): doors or windows on wall ab whose
        /// top is within <see cref="CrownHeight"/> of <paramref name="ceiling"/>, so the crown never runs across them.
        /// </summary>
        public static void TakeCeilingOpenings(Vec2 a, Vec2 b, IReadOnlyList<RoomElement> openings, float ceiling, List<Vector2> spans) =>
            TakeOpenings(a, b, openings, spans, ceiling - CrownHeight);

        // Removes from spans the width of every element on wall ab whose top is at least minTop.
        static void TakeOpenings(Vec2 a, Vec2 b, IReadOnlyList<RoomElement> doors, List<Vector2> spans, float minTop)
        {
            float vx = b.x - a.x, vz = b.y - a.y, len2 = vx * vx + vz * vz;
            if (len2 < 1e-6f || doors == null) return;
            float len = Mathf.Sqrt(len2);
            for (int d = 0; d < doors.Count; d++)
            {
                OrientedBox box = doors[d].Box;
                if (box.center.y + box.size.y * 0.5f < minTop) continue; // does not reach that high
                float px = box.center.x - a.x, pz = box.center.z - a.y;
                if (Mathf.Abs(px * vz - pz * vx) / len > 0.25f) continue; // not on this wall
                float t = (px * vx + pz * vz) / len2, half = box.size.x * 0.5f / len;
                float t0 = t - half, t1 = t + half;
                for (int i = spans.Count - 1; i >= 0; i--)
                {
                    Vector2 r = spans[i];
                    if (r.y <= t0 || r.x >= t1) continue;
                    spans.RemoveAt(i);
                    if (r.y > t1) spans.Insert(i, new Vector2(t1, r.y));
                    if (r.x < t0) spans.Insert(i, new Vector2(r.x, t0));
                }
            }
        }
        readonly Material[] _shellMaterials = new Material[Submeshes];
        readonly List<GameObject> _frames = new List<GameObject>();
        readonly List<Vector2> _inside = new List<Vector2>();
        readonly List<int> _holeTris = new List<int>();
        readonly List<int> _tris = new List<int>();
        readonly List<Vector2> _parts = new List<Vector2>();
        readonly List<Vec2> _band = new List<Vec2>();
        readonly List<Vector2> _fades = new List<Vector2>();
        readonly List<Vec2> _expanded = new List<Vec2>();
        const float OverWalls = 0.2f; // the floor and ceiling reach this far past the scanned outline
        Mesh _shellMesh, _holeMesh, _seamMesh;
        MeshRenderer _shellRenderer, _holeRenderer, _seamRenderer;

        /// <summary>The renderer whose submeshes are floor, walls, ceiling; finishes swap its materials.</summary>
        public MeshRenderer ShellRenderer => _shellRenderer;

        // ---- presence guard (virtual extent part B): in an extended room the real walls are nearer than the virtual ones ----

        /// <summary>
        /// The head this far outside my zone (m) shows the real room (the shell is hidden: passthrough); back within
        /// <see cref="GuardBack"/> the virtual room returns. The gap keeps it from flickering at the edge.
        /// </summary>
        public const float GuardOut = 0.3f, GuardBack = 0.15f;
        bool _guarding, _shown;
        Transform _head;

        /// <summary>The guard's next state (pure, for tests) for the head <paramref name="outside"/> m outside my zone.</summary>
        public static bool StepGuard(bool guarding, float outside, bool extended) =>
            extended && (guarding ? outside > GuardBack : outside > GuardOut);

        /// <summary>How far <paramref name="p"/> is outside <paramref name="zone"/> on the floor (pure): 0 inside.</summary>
        public static float OutsideBy(IReadOnlyList<Vec2> zone, Vec2 p)
        {
            if (zone == null || zone.Count < 3 || Zones.Contains(zone, p)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < zone.Count; i++)
            {
                Vec2 a = zone[i], b = zone[(i + 1) % zone.Count];
                float vx = b.x - a.x, vz = b.y - a.y, l = vx * vx + vz * vz;
                float t = l > 1e-8f ? Mathf.Clamp01(((p.x - a.x) * vx + (p.y - a.y) * vz) / l) : 0f;
                float dx = p.x - (a.x + vx * t), dz = p.y - (a.y + vz * t);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        // Per frame, allocation-free: a polygon distance in an extended room only.
        void Update()
        {
            if (!_shown || _shellRenderer == null) return;
            if (_head == null) { Camera cam = Camera.main; if (cam == null) return; _head = cam.transform; }
            Vector3 h = _head.position;
            bool next = StepGuard(_guarding, _room.Extended ? OutsideBy(_store.Session.State.zoneOutline, new Vec2(h.x, h.z)) : 0f, _room.Extended);
            if (next == _guarding) return;
            _guarding = next;
            _shellRenderer.enabled = _holeRenderer.enabled = _seamRenderer.enabled = !next;
            Debug.Log(next ? "[OwnSpace.MR] Guard: left my zone in an extended room; showing the real room." : "[OwnSpace.MR] Guard: back in my zone; the virtual room returns.");
        }

        void Start()
        {
            _shellRenderer = Part("OutsideShell", _shellMaterials, out _shellMesh);
            _holeRenderer = Part("PassthroughHoles", new[] { _hole }, out _holeMesh);
            _seamRenderer = Part("Seam", new[] { _seam }, out _seamMesh);
            _room.Changed += MarkDirty;
            _store.Session.Changed += MarkDirty;
            _zone.Confirmed += MarkDirty;
            _zone.EditStarted += MarkDirty;
            _extent = new VirtualExtent(_room, _store, _zone); // a small room's virtual room reaches past the real walls
            Rebuild();
        }

        void OnDestroy()
        {
            if (_room != null) _room.Changed -= MarkDirty;
            if (_store != null && _store.Session != null) _store.Session.Changed -= MarkDirty;
            if (_zone != null) { _zone.Confirmed -= MarkDirty; _zone.EditStarted -= MarkDirty; }
            _extent?.Dispose();
            foreach (Material c in _ceilings.Values) if (c != null) Destroy(c);
            foreach (Material b in _boards.Values) if (b != null) Destroy(b);
        }

        MeshRenderer Part(string name, Material[] materials, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        bool _ceilingOverZone;
        readonly Dictionary<Material, Material> _ceilings = new Dictionary<Material, Material>();

        /// <summary>
        /// Whether the virtual ceiling also covers my zone (pure, for tests): always (the human, 2427b8d: with the walls on
        /// Plain the real ceiling tiles and light showed over my zone; the ceiling is to be fully covered whatever the walls).
        /// The parameters stay for the log and a later switch.
        /// </summary>
        public static bool CeilingCoversZone(bool outsideWallStyled, bool visiting) => true;

        /// <summary>How far the ceiling's tone goes from the wall colour toward white (device feedback: 0.6 read nearly as dark as the walls).</summary>
        public const float CeilingLift = 0.78f;

        /// <summary>The ceiling's tone for a wall colour (pure): <see cref="CeilingLift"/> of the way to a neutral white, opaque.</summary>
        public static Color CeilingTone(Color wall)
        {
            Color c = Color.Lerp(wall, new Color(0.95f, 0.95f, 0.95f), CeilingLift); // neutral white: the wall's hue stays, the mood warms it
            c.a = 1f;
            return c;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), UseTexId = Shader.PropertyToID("_UseTex"),
            PatternId = Shader.PropertyToID("_Pattern"), GainId = Shader.PropertyToID("_Gain"), MaxSmoothId = Shader.PropertyToID("_MaxSmooth");

        // One ceiling material per wall finish, made once: plain (no texture or pattern), matte, the wall's lighter tone.
        Material CeilingFor(Material wall)
        {
            if (wall == null) return _surfaces[2];
            if (_ceilings.TryGetValue(wall, out Material c)) return c;
            c = new Material(wall) { name = wall.name + " (ceiling)" };
            if (c.HasProperty(BaseColorId)) c.SetColor(BaseColorId, CeilingTone(wall.GetColor(BaseColorId)));
            if (c.HasProperty(UseTexId)) c.SetFloat(UseTexId, 0f);
            if (c.HasProperty(PatternId)) c.SetFloat(PatternId, 0f);
            if (c.HasProperty(GainId)) c.SetFloat(GainId, 1f);
            if (c.HasProperty(MaxSmoothId)) c.SetFloat(MaxSmoothId, 0.1f);
            _ceilings[wall] = c;
            return c;
        }

        // A change (room, design, zone) marks the shell; it is rebuilt once at the end of the frame (log 2dc475d: five
        // rebuilds, and five sets of log lines, within 0.02 s for one change).
        bool _dirty;
        void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Rebuild();
        }

        /// <summary>Rebuilds all three meshes from the current room and zone now (changes are coalesced through <see cref="MarkDirty"/>).</summary>
        public void Rebuild()
        {
            if (_shellMesh == null) return;
            IReadOnlyList<Vec2> zone = _store.Session.State.zoneOutline;
            bool show = _room.HasScan && zone.Count >= 3 && _zone.Current == ZoneSetup.Phase.Confirmed;
            _shown = show;
            _shellRenderer.enabled = _holeRenderer.enabled = _seamRenderer.enabled = show && !_guarding;
            ClearFrames();
            // The room for the finish shader's contact shading (corners, floor and ceiling edges); off with no shell.
            ShellShading.Push(show ? _room.FloorOutline : null, _room.FloorY, _room.CeilingY > _room.FloorY ? _room.CeilingY : _room.FloorY + 2.5f);
            if (!show) return;

            DesignState state = _store.Session.State;
            bool myFloor = Finish(state.myZoneFinish.floorFinishId, out Material myFloorMat, out _);
            bool myWall = Finish(state.myZoneFinish.wallFinishId, out Material myWallMat, out string myStyle);
            _shellMaterials[FloorSubmesh] = Finish(state.outsideFinish.floorFinishId, out Material m, out _) ? m : _surfaces[0];
            bool outsideWall = Finish(state.outsideFinish.wallFinishId, out m, out string outsideStyle);
            _shellMaterials[WallSubmesh] = outsideWall ? m : _surfaces[1];
            // Styled walls get a ceiling to match, over the whole room (the human, build 9bfbd72: "why are only the edges
            // white?"): a lighter, matte, plain tone of the wall finish on the same shader, so the mood's tone reaches it.
            _shellMaterials[CeilingSubmesh] = outsideWall ? CeilingFor(m) : _surfaces[2];
            _ceilingOverZone = CeilingCoversZone(outsideWall, _room.Visiting != null);
            _shellMaterials[MyFloorSubmesh] = myFloor ? myFloorMat : _surfaces[0];
            _shellMaterials[MyWallSubmesh] = myWall ? myWallMat : _surfaces[1];
            _shellMaterials[BaseboardSubmesh] = BoardFor(_shellMaterials[WallSubmesh]) ?? _surfaces[1]; // one room: the wall's accent
            _shellRenderer.sharedMaterials = _shellMaterials;

            _myFloorShown = myFloor;
            BuildShell(zone, myFloor, myWall);
            BuildHoles(zone, myFloor, myWall);
            BuildSeam(zone, myWall);
            if (Debug.isDebugBuild) LogPieces(zone, myWall, outsideWall);
            if (_room.Extended) return; // the real windows and doors are not drawn in an extended room (as in a visit)
            Frames(_room.Windows, zone, myWall, myWall ? myStyle : outsideStyle, outsideStyle, "curtain-");
            Frames(_room.Doors, zone, myWall, myWall ? myStyle : outsideStyle, outsideStyle, "door-trim-");
        }

        // The walls follow the floor outline edge by edge in a visit and in an extended room; else the scanned walls.
        bool OutlineWalls => _room.Visiting != null || _room.Extended;

        void WallAt(int w, out Vec2 a, out Vec2 b)
        {
            IReadOnlyList<Vec2> outline = _room.FloorOutline;
            if (OutlineWalls) { a = outline[w]; b = outline[(w + 1) % outline.Count]; }
            else ShellGeometry.WallEnds(_room.Walls[w].Box, out a, out b);
        }

        VirtualExtent _extent;

        void BuildShell(IReadOnlyList<Vec2> zone, bool myFloor, bool myWall)
        {
            Begin();
            float floor = _room.FloorY, ceiling = _room.CeilingY > floor ? _room.CeilingY : floor + 2.5f;
            ShellGeometry.Expand(_room.FloorOutline, OverWalls, _expanded); // no gap at the walls (device batch 7)
            Cover(floor, _sub[FloorSubmesh]);
            Cover(ceiling, _sub[CeilingSubmesh]);
            // 1 mm above the outside floor so my-zone finish wins without z-fighting.
            // One floor: when my zone has the outside floor's material, the outside floor covers it (no second layer, no
            // edge); a different (older) my-zone finish still draws on top.
            if (myFloor && !SameFloor(_shellMaterials[MyFloorSubmesh], _shellMaterials[FloorSubmesh])) Polygon(zone, floor + 0.001f, _sub[MyFloorSubmesh]);

            var walls = _room.Walls;
            // A visited space (Saved spaces) is closed all round: its walls follow its floor outline edge by edge, so no
            // gap shows the real room (the human: "everything except my own spot must be virtual").
            // An extended room (VirtualExtent) is closed the same way: its walls are the extended outline, not the real ones.
            bool visiting = _room.Visiting != null, outlineWalls = OutlineWalls;
            IReadOnlyList<Vec2> outline = _room.FloorOutline;
            int wallCount = outlineWalls ? outline.Count : walls.Count;
            FurnitureAnchor piece = ZonePiece(); // the wall it stands against is painted across the zone's width
            for (int w = 0; w < wallCount; w++)
            {
                Vec2 a, b;
                WallAt(w, out a, out b);
                ShellGeometry.OutsideIntervals(a, b, zone, _parts);
                // The painted width: the wall line inside the zone, plus the stretch the zone runs along within
                // ShellGeometry.WallReach (its corners stop inside the room, short of the wall).
                ShellGeometry.TakeWallSpan(a, b, zone, ZoneAlign.WallReachFor(a, b, piece), _parts);
                WallQuads(a, b, _parts, floor, ceiling, _sub[WallSubmesh]);
                Baseboard(a, b, _parts, floor);
                Crown(a, b, _parts, ceiling); // the outside: the ceiling is always virtual there
                if (!myWall) continue;
                Complement(_parts, _inside);
                WallQuads(a, b, _inside, floor, ceiling, _sub[MyWallSubmesh]);
                Baseboard(a, b, _inside, floor);
                if (_ceilingOverZone) Crown(a, b, _inside, ceiling); // else the real ceiling meets this wall: no crown
            }

            // A visited space's windows: neutral light panels (the ceiling's surface), never passthrough to the real room.
            if (visiting) { LightPanels(_room.Windows, _sub[CeilingSubmesh]); LightPanels(_room.Doors, _sub[WallSubmesh]); }

            _shellMesh.Clear();
            _shellMesh.SetVertices(_v);
            // Normals (device, build 2dc475d: "Lines" stretched down the wall, Oak not showing on the floor): the finish shader
            // projects its pattern and texture in metres along the surface from the normal; the shell had none.
            _shellMesh.SetNormals(_n);
            _shellMesh.subMeshCount = Submeshes;
            for (int s = 0; s < Submeshes; s++) _shellMesh.SetTriangles(_sub[s], s);
            _shellMesh.RecalculateBounds();
        }

        // The desk or bed in my zone (the first one), or null.
        FurnitureAnchor ZonePiece()
        {
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
                if ((furniture[i].Role == FurnitureRole.Table || furniture[i].Role == FurnitureRole.Bed) && _store.Session.IsInMyZone(furniture[i])) return furniture[i];
            return null;
        }

        /// <summary>
        /// Whether the stretch of wall that belongs to my zone is a passthrough hole (pure, for tests): yes while that wall
        /// is left real. The virtual floor and ceiling reach <see cref="OverWalls"/> past the outline to close gaps behind
        /// virtual walls; behind a real stretch nothing hid that overreach, so a strip of virtual ceiling and floor showed
        /// past the real wall and it read as a panel standing in front (device shot 221205).
        /// </summary>
        public static bool RealWallIsHole(bool myWallStyled) => !myWallStyled;

        // The my-zone stretch of every wall, floor to ceiling, 2 mm inside the wall plane: real, and nothing virtual behind it.
        void RealWallHoles(IReadOnlyList<Vec2> zone, float floor, float ceiling)
        {
            Vector2 centre = RoomGeometry.Centroid(_room.FloorOutline);
            FurnitureAnchor piece = ZonePiece();
            int count = OutlineWalls ? _room.FloorOutline.Count : _room.Walls.Count;
            Vector3 up = Vector3.up * (ceiling - floor);
            for (int w = 0; w < count; w++)
            {
                WallAt(w, out Vec2 a, out Vec2 b);
                ShellGeometry.OutsideIntervals(a, b, zone, _parts);
                ShellGeometry.TakeWallSpan(a, b, zone, ZoneAlign.WallReachFor(a, b, piece), _parts); // the same split as the walls
                Complement(_parts, _inside);
                Vector3 inward = Inward(a, b, centre) * 0.002f;
                for (int i = 0; i < _inside.Count; i++)
                {
                    Vector3 p0 = Lerp(a, b, _inside[i].x, floor) + inward, p1 = Lerp(a, b, _inside[i].y, floor) + inward;
                    Quad(p0, p1, p1 + up, p0 + up, _holeTris);
                }
            }
        }

        // Development builds, on a rebuild: what the shell is made of, one line per wall (device shot 221205: walls read as
        // separate panels at different depths). "off outline" is how far the scanned wall's ends are from the floor outline,
        // the plane the floor and ceiling end at.
        void LogPieces(IReadOnlyList<Vec2> zone, bool myWall, bool outsideWall)
        {
            FurnitureAnchor piece = ZonePiece();
            int count = OutlineWalls ? _room.FloorOutline.Count : _room.Walls.Count, holes = 0;
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
                if (FurnitureIsHole(_store.Session.IsInMyZone(furniture[i]), _store.Session.State.ChoiceFor(furniture[i].Id).mode)) holes++;
            Debug.Log($"{LogTag} Shell pieces: {count} wall(s) from {(OutlineWalls ? "the floor outline" : "the scanned walls")}, " +
                      $"outside floor {Name(_shellMaterials[FloorSubmesh])}, my-zone floor {(_shellMaterials[MyFloorSubmesh] != _surfaces[0] ? Name(_shellMaterials[MyFloorSubmesh]) : "real")}, " +
                      $"outside wall {Name(_shellMaterials[WallSubmesh])}{(outsideWall ? "" : " (default)")}, my-zone wall {(myWall ? Name(_shellMaterials[MyWallSubmesh]) : "real")}, " +
                      $"ceiling {Name(_shellMaterials[CeilingSubmesh])}{(_ceilingOverZone ? " over my zone too" : "")}, floor/ceiling reach {OverWalls:F2} m past the outline, " +
                      $"{holes} kept-real furniture hole(s), {_room.Windows.Count} window(s), {_room.Doors.Count} door(s).");
            for (int w = 0; w < count; w++)
            {
                WallAt(w, out Vec2 a, out Vec2 b);
                float length = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                ShellGeometry.OutsideIntervals(a, b, zone, _parts);
                ShellGeometry.TakeWallSpan(a, b, zone, ZoneAlign.WallReachFor(a, b, piece), _parts);
                Complement(_parts, _inside);
                var text = new System.Text.StringBuilder();
                // The material each span is drawn with (device 2dc475d: spans beside the zone read as plain white).
                string outsideMat = Name(_shellMaterials[WallSubmesh]), mineMat = myWall ? Name(_shellMaterials[MyWallSubmesh]) : "passthrough";
                Vector3 n = FaceNormal(new Vector3(a.x, _midY, a.y), new Vector3(b.x, _midY, b.y), new Vector3(a.x, _midY + 1f, a.y), new Vector3(_centre.x, _midY, _centre.y));
                for (int i = 0; i < _parts.Count; i++) text.Append($" outside {_parts[i].x * length:F2}-{_parts[i].y * length:F2} [{outsideMat}]");
                for (int i = 0; i < _inside.Count; i++) text.Append($" {(myWall ? "mine" : "real")} {_inside[i].x * length:F2}-{_inside[i].y * length:F2} [{mineMat}]");
                text.Append($"; normal ({n.x:F2}, {n.y:F2}, {n.z:F2})");
                Debug.Log($"{LogTag} Shell wall {w}: {length:F2} m, ends ({a.x:F2}, {a.y:F2}) ({b.x:F2}, {b.y:F2}), off outline " +
                          $"{ShellGeometry.DistanceToOutline(a, _room.FloorOutline):F3} / {ShellGeometry.DistanceToOutline(b, _room.FloorOutline):F3} m, spans (m):{text}.");
            }
        }

        static string Name(Material m) => m != null ? m.name : "none";

        void BuildHoles(IReadOnlyList<Vec2> zone, bool myFloor, bool myWall)
        {
            Begin();
            float floor = _room.FloorY, ceiling = _room.CeilingY > floor ? _room.CeilingY : floor + 2.5f;
            // My zone stays real from floor to ceiling.
            if (!myFloor) Polygon(zone, floor + 0.003f, _holeTris);
            if (!_ceilingOverZone) Polygon(zone, ceiling - 0.003f, _holeTris); // else the ceiling is virtual over it too
            if (RealWallIsHole(myWall)) RealWallHoles(zone, floor, ceiling);
            if (OpeningsArePassthrough(OutlineWalls)) // a visit or an extended room: the real openings are not shown
            {
                Openings(_room.Windows);
                Openings(_room.Doors);
            }

            // Real furniture in my zone that is kept real must not be covered by the shell behind it.
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                if (!FurnitureIsHole(_store.Session.IsInMyZone(furniture[i]), _store.Session.State.ChoiceFor(furniture[i].Id).mode)) continue;
                Box(furniture[i].Box, _furniturePad);
            }

            _holeMesh.Clear();
            _holeMesh.SetVertices(_v);
            _holeMesh.SetTriangles(_holeTris, 0);
            _holeMesh.RecalculateBounds();
        }

        // Every place real meets virtual fades over a band instead of a hard line (realism pass I): the floor and the
        // ceiling around my zone, outside walls where they cross the zone border, and restyled my-zone walls under
        // the real ceiling. Each band fades the surface under it (SeamFade multiplies), so textures carry through.
        // One mesh, one draw call.
        void BuildSeam(IReadOnlyList<Vec2> zone, bool myWall)
        {
            Begin();
            float floor = _room.FloorY, ceiling = _room.CeilingY > floor ? _room.CeilingY : floor + 2.5f;
            // The floor fade only where my zone's floor is real (the human, 8fdbcda: a designed floor turned see-through near
            // the zone's edge); a designed floor is opaque edge to edge.
            if (FloorSeam(_myFloorShown))
            {
                ShellGeometry.SeamBand(zone, _seamWidth, _band);
                Band(floor + 0.002f);
            }
            if (!_ceilingOverZone)
            {
                ShellGeometry.SeamBand(zone, _edgeWidth, _band);
                Band(ceiling - 0.005f); // below the ceiling and its hole, so it draws over both
            }

            Vector2 centre = RoomGeometry.Centroid(_room.FloorOutline);
            var walls = _room.Walls;
            FurnitureAnchor piece = ZonePiece();
            int seamWalls = OutlineWalls ? _room.FloorOutline.Count : walls.Count;
            for (int w = 0; w < seamWalls; w++)
            {
                WallAt(w, out Vec2 a, out Vec2 b);
                Vector3 inward = Inward(a, b, centre) * 0.004f; // in front of the wall
                ShellGeometry.OutsideIntervals(a, b, zone, _parts);
                ShellGeometry.TakeWallSpan(a, b, zone, ZoneAlign.WallReachFor(a, b, piece), _parts); // the same split as the walls
                if (!myWall)
                {
                    // The real wall in my zone meets the virtual wall outside it.
                    float length = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                    ShellGeometry.BorderFades(_parts, length, _edgeWidth, _fades);
                    for (int f = 0; f < _fades.Count; f++)
                    {
                        Vector3 p0 = Lerp(a, b, _fades[f].x, floor) + inward, p1 = Lerp(a, b, _fades[f].y, floor) + inward, up = Vector3.up * (ceiling - floor);
                        Fade(p0, p1, p1 + up, p0 + up, 0f, 1f, 1f, 0f);
                    }
                    continue;
                }
                // A restyled wall in my zone meets the real ceiling above my zone (none when the ceiling is virtual there).
                if (_ceilingOverZone) continue;
                Complement(_parts, _inside);
                for (int i = 0; i < _inside.Count; i++)
                {
                    Vector3 p0 = Lerp(a, b, _inside[i].x, ceiling - _edgeWidth) + inward, p1 = Lerp(a, b, _inside[i].y, ceiling - _edgeWidth) + inward;
                    Vector3 up = Vector3.up * _edgeWidth;
                    Fade(p0, p1, p1 + up, p0 + up, 1f, 1f, 0f, 0f);
                }
            }
            _seamMesh.Clear();
            _seamMesh.SetVertices(_v);
            _seamMesh.SetColors(_c);
            _seamMesh.SetTriangles(_holeTris, 0);
            _seamMesh.RecalculateBounds();
        }

        // The zone band in _band at height y: inner edge passthrough, outer edge the surface.
        void Band(float y)
        {
            for (int q = 0; q + 3 < _band.Count; q += 4)
            {
                Vector3 a = new Vector3(_band[q].x, y, _band[q].y), b = new Vector3(_band[q + 1].x, y, _band[q + 1].y);
                Vector3 c = new Vector3(_band[q + 2].x, y, _band[q + 2].y), d = new Vector3(_band[q + 3].x, y, _band[q + 3].y);
                Fade(a, b, c, d, 0f, 0f, 1f, 1f);
            }
        }

        void Fade(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float aa, float ab, float ac, float ad)
        {
            int start = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            AddNormals(a, b, d, 4);
            _c.Add(new Color(1f, 1f, 1f, aa)); _c.Add(new Color(1f, 1f, 1f, ab));
            _c.Add(new Color(1f, 1f, 1f, ac)); _c.Add(new Color(1f, 1f, 1f, ad));
            AddQuadIndices(start, _holeTris);
        }

        // Unit floor direction across the wall a-b toward the room's centre.
        static Vector3 Inward(Vec2 a, Vec2 b, Vector2 centre)
        {
            var n = new Vector3(-(b.y - a.y), 0f, b.x - a.x);
            if (n.sqrMagnitude < 1e-8f) return Vector3.zero;
            n.Normalize();
            var mid = new Vector3((a.x + b.x) * 0.5f - centre.x, 0f, (a.y + b.y) * 0.5f - centre.y);
            return Vector3.Dot(n, mid) > 0f ? -n : n;
        }

        void Begin()
        {
            _v.Clear();
            _n.Clear();
            _centre = RoomGeometry.Centroid(_room.FloorOutline);
            _midY = (_room.FloorY + (_room.CeilingY > _room.FloorY ? _room.CeilingY : _room.FloorY + 2.5f)) * 0.5f;
            _c.Clear();
            _holeTris.Clear();
            for (int s = 0; s < Submeshes; s++) _sub[s].Clear();
        }

        bool Finish(string id, out Material material, out string style)
        {
            material = null;
            style = DefaultStyle;
            if (_finishes == null || !_finishes.TryGet(id, out FinishSet.Entry entry)) return false;
            material = entry.material;
            if (entry.styles != null && entry.styles.Length > 0) style = entry.styles[0];
            return true;
        }

        // A baseboard along the bottom of the virtual wall spans (realism pass): a front face and a top, BoardDepth into the
        // room, never across a doorway. Its spans are this wall's virtual spans less the doors.
        void Baseboard(Vec2 a, Vec2 b, List<Vector2> parts, float floor)
        {
            _board.Clear();
            _board.AddRange(parts);
            TakeDoors(a, b, _room.Doors, _board);
            Vector3 inward = Inward(a, b, _centre) * BoardDepth, up = Vector3.up * BoardHeight;
            for (int i = 0; i < _board.Count; i++)
            {
                Vector3 p0 = Lerp(a, b, _board[i].x, floor) + inward, p1 = Lerp(a, b, _board[i].y, floor) + inward;
                QuadFacing(p0, p1, p1 + up, p0 + up, Inward(a, b, _centre), _sub[BaseboardSubmesh]); // the face
                QuadFacing(p0 + up, p1 + up, p1 + up - inward, p0 + up - inward, Vector3.up, _sub[BaseboardSubmesh]); // the top
            }
        }

        // A crown molding along the top of the virtual wall spans, where they meet the virtual ceiling: a front face and an
        // underside, CrownDepth into the room, in the baseboard's submesh (no new material or draw call), never across a door
        // or window that reaches the ceiling.
        void Crown(Vec2 a, Vec2 b, List<Vector2> parts, float ceiling)
        {
            _board.Clear();
            _board.AddRange(parts);
            TakeCeilingOpenings(a, b, _room.Doors, ceiling, _board);
            TakeCeilingOpenings(a, b, _room.Windows, ceiling, _board);
            Vector3 inward = Inward(a, b, _centre) * CrownDepth, down = Vector3.down * CrownHeight;
            for (int i = 0; i < _board.Count; i++)
            {
                Vector3 p0 = Lerp(a, b, _board[i].x, ceiling) + inward, p1 = Lerp(a, b, _board[i].y, ceiling) + inward;
                QuadFacing(p0 + down, p1 + down, p1, p0, Inward(a, b, _centre), _sub[BaseboardSubmesh]); // the face
                QuadFacing(p0 + down, p1 + down, p1 + down - inward, p0 + down - inward, Vector3.down, _sub[BaseboardSubmesh]); // the underside
            }
        }

        // A quad wound so its front (Unity: the side cross(b - a, c - a) points to) faces <paramref name="facing"/>.
        void QuadFacing(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, List<int> into)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) Quad(a, d, c, b, into);
            else Quad(a, b, c, d, into);
        }

        void WallQuads(Vec2 a, Vec2 b, List<Vector2> parts, float floor, float ceiling, List<int> into)
        {
            Vector3 up = Vector3.up * (ceiling - floor);
            for (int i = 0; i < parts.Count; i++)
            {
                Vector3 p0 = Lerp(a, b, parts[i].x, floor), p1 = Lerp(a, b, parts[i].y, floor);
                Quad(p0, p1, p1 + up, p0 + up, into);
            }
        }

        /// <summary>The parts of [0, 1] not covered by <paramref name="parts"/> (sorted, disjoint).</summary>
        static void Complement(List<Vector2> parts, List<Vector2> result)
        {
            result.Clear();
            float t = 0f;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].x - t > 1e-4f) result.Add(new Vector2(t, parts[i].x));
                t = parts[i].y;
            }
            if (1f - t > 1e-4f) result.Add(new Vector2(t, 1f));
        }

        /// <summary>A curtain (windows) or trim (doors) on every opening whose wall is virtual.</summary>
        void Frames(IReadOnlyList<RoomElement> openings, IReadOnlyList<Vec2> zone, bool myWall, string myStyle, string outsideStyle, string prefix)
        {
            if (_prefabs == null) return;
            for (int i = 0; i < openings.Count; i++)
            {
                OrientedBox b = openings[i].Box;
                bool mine = Zones.Contains(zone, b.center.Floor());
                if (mine && !myWall) continue; // a real wall keeps its real opening as is
                if (!_prefabs.TryGetPrefab(prefix + (mine ? myStyle : outsideStyle) + "-01", out GameObject prefab) &&
                    !_prefabs.TryGetPrefab(prefix + DefaultStyle + "-01", out prefab)) continue;
                Vector3 into = ShellGeometry.IntoRoom(b, _room.FloorOutline);
                Quaternion rot = Quaternion.LookRotation(into, Vector3.up); // frame +Z faces the room
                Vector3 bottom = BoxMath.Center(b) + Vector3.down * (b.size.y * 0.5f) + into * 0.005f;
                GameObject frame = Instantiate(prefab, bottom, rot, transform);
                if (frame.TryGetComponent(out OpeningFrame fit)) fit.Fit(b.size.x, b.size.y);
                _frames.Add(frame);
            }
        }

        void ClearFrames()
        {
            for (int i = 0; i < _frames.Count; i++) if (_frames[i] != null) Destroy(_frames[i]);
            _frames.Clear();
        }

        // The floor or ceiling, never missing: the outline pushed past the walls, else the scanned outline, else its
        // bounding rectangle (a skipped polygon left a black void, device batch 7).
        void Cover(float y, List<int> into)
        {
            if (Polygon(_expanded, y, into, warn: false) || Polygon(_room.FloorOutline, y, into, warn: false)) return;
            ShellGeometry.Bounds(_room.FloorOutline, OverWalls, _expanded);
            if (!Polygon(_expanded, y, into)) return;
            Debug.LogWarning($"{LogTag} Shell: the room outline could not be triangulated; covered with its bounding rectangle.");
        }

        bool Polygon(IReadOnlyList<Vec2> polygon, float y, List<int> into, bool warn = true)
        {
            if (!ShellGeometry.Triangulate(polygon, _tris))
            {
                if (warn) Debug.LogWarning($"{LogTag} Shell: an outline could not be triangulated; skipped.");
                return false;
            }
            int start = _v.Count;
            Vector3 normal = FlatNormal(y, _midY);
            for (int i = 0; i < polygon.Count; i++) { _v.Add(new Vector3(polygon[i].x, y, polygon[i].y)); _n.Add(normal); }
            AddWound(_tris, start, normal.y > 0f, into);
            return true;
        }

        /// <summary>
        /// Whether windows and doors are passthrough holes (pure, for tests): yes in the real room (they are real); never
        /// during a visit. There the real room must show only inside the small zone around the user's desk or bed.
        /// </summary>
        public static bool OpeningsArePassthrough(bool visiting) => !visiting;

        /// <summary>Which furniture boxes are passthrough holes (pure, for tests): kept-real pieces inside my zone only.</summary>
        public static bool FurnitureIsHole(bool inMyZone, AnchorMode mode) => inMyZone && mode == AnchorMode.KeepReal;

        // An opening drawn as a flat panel just inside the wall, in the given surface (a visit's windows and doors).
        void LightPanels(IReadOnlyList<RoomElement> openings, List<int> into)
        {
            for (int i = 0; i < openings.Count; i++)
            {
                OrientedBox b = openings[i].Box;
                Quaternion rot = BoxMath.Rotation(b);
                Vector3 c = BoxMath.Center(b) + ShellGeometry.IntoRoom(b, _room.FloorOutline) * 0.012f;
                Vector3 r = rot * Vector3.right * (b.size.x * 0.5f), u = Vector3.up * (b.size.y * 0.5f);
                Quad(c - r - u, c + r - u, c + r + u, c - r + u, into);
                Quad(c - r + u, c + r + u, c + r - u, c - r - u, into); // both faces: the wall's winding is not known here
            }
        }

        void Openings(IReadOnlyList<RoomElement> openings)
        {
            for (int i = 0; i < openings.Count; i++)
            {
                OrientedBox b = openings[i].Box;
                Quaternion rot = BoxMath.Rotation(b);
                // 1 cm into the room (whatever the scan's normal says) so the hole wins against the wall.
                Vector3 c = BoxMath.Center(b) + ShellGeometry.IntoRoom(b, _room.FloorOutline) * 0.01f;
                Vector3 r = rot * Vector3.right * (b.size.x * 0.5f), u = Vector3.up * (b.size.y * 0.5f);
                Quad(c - r - u, c + r - u, c + r + u, c - r + u, _holeTris);
            }
        }

        void Box(OrientedBox b, float pad)
        {
            Quaternion rot = BoxMath.Rotation(b);
            Vector3 c = BoxMath.Center(b);
            Vector3 x = rot * Vector3.right * (b.size.x * 0.5f + pad), y = Vector3.up * (b.size.y * 0.5f + pad);
            Vector3 z = rot * Vector3.forward * (b.size.z * 0.5f + pad);
            Quad(c - x - y - z, c + x - y - z, c + x + y - z, c - x + y - z, _holeTris); // back
            Quad(c - x - y + z, c + x - y + z, c + x + y + z, c - x + y + z, _holeTris); // front
            Quad(c - x - y - z, c - x - y + z, c - x + y + z, c - x + y - z, _holeTris); // left
            Quad(c + x - y - z, c + x - y + z, c + x + y + z, c + x + y - z, _holeTris); // right
            Quad(c - x + y - z, c + x + y - z, c + x + y + z, c - x + y + z, _holeTris); // top
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> into)
        {
            int start = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            AddNormals(a, b, d, 4);
            AddQuadIndices(start, into);
        }

        void AddNormals(Vector3 a, Vector3 b, Vector3 d, int count)
        {
            Vector3 n = FaceNormal(a, b, d, new Vector3(_centre.x, _midY, _centre.y));
            for (int i = 0; i < count; i++) _n.Add(n);
        }

        /// <summary>
        /// Adds a counter-clockwise (seen from above) triangulation, wound for the side it faces (pure, for tests): reversed when
        /// it faces up. Unity's front face is clockwise as seen, so the floor and the my-zone floor finish were back faces from
        /// above and culled (device 26a0806: "Floor Oak/Birch/Dark/Tiles changes nothing"; the real floor showed through).
        /// </summary>
        public static void AddWound(List<int> ccwTriangles, int start, bool facesUp, List<int> into)
        {
            for (int i = 0; i + 2 < ccwTriangles.Count; i += 3)
            {
                into.Add(start + ccwTriangles[i]);
                into.Add(start + ccwTriangles[facesUp ? i + 2 : i + 1]);
                into.Add(start + ccwTriangles[facesUp ? i + 1 : i + 2]);
            }
        }

        bool _myFloorShown;

        /// <summary>Whether the floor gets the soft real-to-virtual fade at my zone's edge (pure): only when my zone's floor is real.</summary>
        public static bool FloorSeam(bool myFloorDesigned) => !myFloorDesigned;

        /// <summary>Whether my zone's floor is the outside floor's material (pure): then one floor covers both.</summary>
        public static bool SameFloor(Material mine, Material outside) => mine != null && mine == outside;

        /// <summary>A floor faces up, a ceiling down (pure): by which side of the room's mid height <paramref name="y"/> is.</summary>
        public static Vector3 FlatNormal(float y, float midY) => y > midY ? Vector3.down : Vector3.up;

        /// <summary>
        /// A quad's normal toward the room (pure, for tests): its face normal, turned to face <paramref name="roomCentre"/>
        /// (a wall into the room, a floor up, a ceiling down) whatever its winding.
        /// </summary>
        public static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 d, Vector3 roomCentre)
        {
            Vector3 n = Vector3.Cross(b - a, d - a);
            if (n.sqrMagnitude < 1e-10f) return Vector3.up;
            n.Normalize();
            Vector3 toRoom = roomCentre - a;
            if (Mathf.Abs(n.y) < 0.5f) toRoom.y = 0f; // a wall: across the room, not up or down
            return Vector3.Dot(n, toRoom) < 0f ? -n : n;
        }

        static void AddQuadIndices(int s, List<int> into)
        {
            into.Add(s); into.Add(s + 1); into.Add(s + 2);
            into.Add(s); into.Add(s + 2); into.Add(s + 3);
        }

        static Vector3 Lerp(Vec2 a, Vec2 b, float t, float y) => new Vector3(a.x + (b.x - a.x) * t, y, a.y + (b.y - a.y) * t);
    }
}

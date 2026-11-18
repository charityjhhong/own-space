using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.MR
{
    /// <summary>
    /// The real top of a redesigned desk or bed, without depth: the real surface and what lies on it show (paper, a phone,
    /// a keyboard, a mouse), while the design still draws legs, sides, front and edges.
    /// <para>For each redesigned piece with a usable surface and the option on there is one invisible rectangle: the shown
    /// design's top, inset by <see cref="Rim"/> so the design's own edge hides the seam, <see cref="RealOcclusion.TopBias"/>
    /// above the real top. It is drawn with the passthrough hole shader (depth written), facing down and seen from
    /// above: every virtual surface under it gives way to passthrough (the design's top, the legs seen through it,
    /// shadows on it), and virtual things standing on the desk (a placed lamp) or in front of it (the tool panel) stay.</para>
    /// <para>It is flat on purpose (the human, build 2dc475d: a 0.45 m box cut the virtual wall behind the desk in a band,
    /// "everything looks stuck in there"). A view ray through the rectangle always ends on the desk, never on a wall, so
    /// the wall behind stays virtual from the desk's back edge up. The price: a real thing standing on the desk (a cup, a
    /// laptop screen) shows only where it is seen against the desk top; above that it is covered unless depth occlusion
    /// runs, which adds what stands above the top (<see cref="RealOcclusion"/>'s band over a redesigned desk).</para>
    /// Two triangles per piece in the opaque queue: no extra pass, no texture, rebuilt only when the design, the room or
    /// an adjustment changes. The option is stored per piece in <see cref="AnchorAdjust.realTop"/>.
    /// </summary>
    public sealed class RealTops
    {
        /// <summary>How far the window stays inside the design's footprint on each side (m).</summary>
        public const float Rim = 0.015f;
        public const int Auto = 0, On = 1, Off = 2;
        const string HoleShader = "OwnSpace/PassthroughHole";

        /// <summary>Only pieces with a top to work at (desk, bed).</summary>
        public static bool Applies(FurnitureRole role) => FitLimits.HasUsableSurface(role);

        /// <summary>
        /// On only when the user chose Real (the human, 26a0806: the design's top is the default, "Desk top Real | Design"
        /// starts on Design). A piece that already chose Real keeps it. <paramref name="depthRunning"/> does not change it.
        /// </summary>
        public static bool Effective(int setting, bool depthRunning) => setting == On;

        /// <summary>The setting after one press of the switch: always the opposite of what shows now.</summary>
        public static int Toggled(int setting, bool depthRunning) => Effective(setting, depthRunning) ? Off : On;

        /// <summary>A window: its centre, its size along its own x and z (y is 0: it is flat) and its yaw (degrees).</summary>
        public struct Box { public Vector3 center, size; public float yaw; }

        /// <summary>
        /// The window over a redesigned piece: the shown design's footprint (core's fit plus the user's adjustment, as
        /// <see cref="RedesignView"/> places it) inset by <see cref="Rim"/>, flat, <see cref="RealOcclusion.TopBias"/> above
        /// the real box's top (above the design's top, so the top gives way).
        /// </summary>
        public static Box BoxFor(FurnitureAnchor anchor, CatalogItem item, AnchorChoice choice, AnchorAdjust adjust)
        {
            Vector3 scale = AnchorAdjusts.Scale(item, choice, adjust);
            float top = anchor.Box.center.y + anchor.Box.size.y * 0.5f;
            return new Box
            {
                center = new Vector3(anchor.Box.center.x + adjust.dx, top + RealOcclusion.TopBias, anchor.Box.center.z + adjust.dz),
                size = new Vector3(Mathf.Max(0.01f, item.size.x * scale.x - 2f * Rim), 0f, Mathf.Max(0.01f, item.size.z * scale.z - 2f * Rim)),
                yaw = anchor.Box.yawDegrees + (choice.rotated90 ? 90f : 0f) + adjust.turns * 90f + adjust.yaw,
            };
        }

        /// <summary>
        /// Adds the window's 4 corners and 2 triangles (world space). They face down (clockwise seen from below, Unity's
        /// front face); the material culls front faces, so the window works from above and does nothing seen from below.
        /// </summary>
        public static void Append(Box box, List<Vector3> vertices, List<int> triangles)
        {
            int first = vertices.Count;
            Quaternion turn = Quaternion.Euler(0f, box.yaw, 0f);
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            vertices.Add(box.center + turn * new Vector3(-hx, 0f, -hz));
            vertices.Add(box.center + turn * new Vector3(hx, 0f, -hz));
            vertices.Add(box.center + turn * new Vector3(hx, 0f, hz));
            vertices.Add(box.center + turn * new Vector3(-hx, 0f, hz));
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        /// <summary>
        /// The boxes for a room and design: redesigned desks and beds whose option is on and whose design is shown (one
        /// that would cover a window or door is not). Returns how many were added.
        /// </summary>
        public static int Collect(IRoomInfo room, DesignState state, ICatalog catalog, AnchorAdjusts adjusts, bool depthRunning, List<Box> boxes)
        {
            int before = boxes.Count;
            IReadOnlyList<FurnitureAnchor> furniture = room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                FurnitureAnchor anchor = furniture[i];
                if (!Applies(anchor.Role)) continue;
                AnchorChoice choice = state.ChoiceFor(anchor.Id);
                if (choice.mode != AnchorMode.Redesign || !catalog.TryGet(choice.catalogItemId, out CatalogItem item)) continue;
                AnchorAdjust adjust = adjusts != null ? adjusts.Get(anchor.Id) : default;
                if (!Effective(adjust.realTop, depthRunning)) continue;
                if (OpeningGuard.Covers(room, OpeningGuard.Placed(anchor, item, choice.rotated90, choice.xScale, choice.yScale, choice.zScale, room.FloorY))) continue;
                boxes.Add(BoxFor(anchor, item, choice, adjust));
            }
            return boxes.Count - before;
        }

        readonly GameObject _go;
        readonly Mesh _mesh;
        readonly List<Box> _boxes = new List<Box>();
        readonly List<Vector3> _vertices = new List<Vector3>();
        readonly List<int> _triangles = new List<int>();
        bool _hidden;

        /// <summary>How many windows the last <see cref="Refresh"/> built.</summary>
        public int Count => _boxes.Count;

        public RealTops(Transform parent)
        {
            _go = new GameObject("RealTops");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "RealTops" };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Shader shader = Shader.Find(HoleShader); // in the build through M_PassthroughHole (the zone's real floor)
            if (shader != null)
            {
                var material = new Material(shader) { name = "RealTopHole" };
                material.SetFloat("_Cull", (float)CullMode.Front);
                renderer.sharedMaterial = material;
            }
            else Debug.LogWarning($"[OwnSpace.MR] Real tops: shader {HoleShader} not found; desk tops stay virtual.");
            _go.SetActive(false);
        }

        /// <summary>Hidden with the designs (the zone edit shows the real room anyway).</summary>
        public void SetVisible(bool visible)
        {
            _hidden = !visible;
            _go.SetActive(visible && _boxes.Count > 0);
        }

        /// <summary>Rebuilds the windows. On a design, room, adjustment or depth change only.</summary>
        public void Refresh(IRoomInfo room, DesignState state, ICatalog catalog, AnchorAdjusts adjusts, bool depthRunning)
        {
            int was = _boxes.Count;
            _boxes.Clear();
            _vertices.Clear();
            _triangles.Clear();
            if (room != null && state != null && catalog != null) Collect(room, state, catalog, adjusts, depthRunning, _boxes);
            for (int i = 0; i < _boxes.Count; i++) Append(_boxes[i], _vertices, _triangles);
            _mesh.Clear();
            if (_boxes.Count > 0)
            {
                _go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // the mesh is in world space
                _mesh.SetVertices(_vertices);
                _mesh.SetTriangles(_triangles, 0);
                _mesh.RecalculateBounds();
            }
            _go.SetActive(!_hidden && _boxes.Count > 0);
            if (_boxes.Count != was)
                Debug.Log($"[OwnSpace.MR] Real tops: {_boxes.Count} window(s) (depth {(depthRunning ? "running" : "not running")}).");
        }
    }
}

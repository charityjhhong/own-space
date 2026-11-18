using System;
using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// With Bigger room on, the virtual room reaches past the real walls (the human: "the space is too small, there is
    /// nothing I can do in Add"; and on a 4 x 3.1 m room where nothing changed: "when On, always wider"). Every room grows:
    /// the outline becomes a rectangle in <see cref="ZoneAlign"/>'s direction, grown away from my zone. The side with the
    /// most room reaches at least <see cref="TargetGap"/> past the zone and grows by at least <see cref="MinGrow"/>, never
    /// more than <see cref="MaxGrow"/>; the room is at least <see cref="MinSide"/> across. My zone and everything in it stay real. Saved per room next to the design
    /// (<c>design-&lt;room&gt;.json.extent.json</c>) and never shrunk, so a design made in it reloads the same.
    /// </summary>
    public sealed class VirtualExtent
    {
        public const float MinSide = 3f, TargetGap = 3f, Touching = 0.4f, MinGrow = 1f, MaxGrow = 4f;

        /// <summary>
        /// The user's "Bigger room" setting (Settings › Room), default off: by default the virtual room is the size it was
        /// scanned (the human, 26a0806: "this is exactly the room I want"); on, a small room's virtual room reaches past the
        /// real walls (the human: "add an option like 'make bigger' so a small room looks wider"). Saved in
        /// <see cref="UiPrefs"/> by PanelHost. One value for the app, so a change reaches every instance at once.
        /// </summary>
        public static bool Enabled { get; private set; }

        static event Action EnabledChanged;

        /// <summary>Turns the setting on or off and re-applies it at once (no restart): off returns to the scanned walls.</summary>
        public static void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            Debug.Log($"[OwnSpace.MR] Bigger room: {(on ? "on (a small room's virtual room reaches past the real walls)" : "off (the scanned walls)")}.");
            EnabledChanged?.Invoke();
        }

        /// <summary>A saved extent in its own frame: the yaw and the rectangle's bounds in it (review of #292).</summary>
        [Serializable]
        public sealed class Saved
        {
            public float yaw, x0, z0, x1, z1;

            /// <summary>Finite, ordered and within 100 m (a damaged file is ignored).</summary>
            public bool Valid => Ok(yaw) && Ok(x0) && Ok(z0) && Ok(x1) && Ok(z1) && x1 - x0 > 0.1f && z1 - z0 > 0.1f;
            static bool Ok(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 100f;
        }

        readonly MrukRoomInfo _room;
        readonly DesignStore _store;
        readonly ZoneSetup _zone;
        readonly List<Vec2> _result = new List<Vec2>();

        public VirtualExtent(MrukRoomInfo room, DesignStore store, ZoneSetup zone)
        {
            _room = room; _store = store; _zone = zone;
            _room.Changed += Apply;
            _zone.Confirmed += Apply;
            _store.RoomReset += Apply; // Reset my design: worked out again at once (review of #292)
            EnabledChanged += Apply;   // the Bigger room setting
            Apply(); // the room and zone may be ready already
        }

        public void Dispose() { _room.Changed -= Apply; _zone.Confirmed -= Apply; _store.RoomReset -= Apply; EnabledChanged -= Apply; }

        public static string PathFor(string designPath) => designPath + ".extent.json";

        // On a room change or a confirmed zone (never per frame). Extend raises Changed only when the outline changed.
        void Apply()
        {
            if (_room.Visiting != null || !_room.HasScan) return;
            if (!Enabled) { _room.Extend(null); return; } // the scanned walls; a saved extent is kept for when it is on again
            if (_zone.Current != ZoneSetup.Phase.Confirmed) return;
            IReadOnlyList<Vec2> zone = _store.Session.State.zoneOutline;
            if (zone.Count < 3) return;
            string path = _store.SavePath != null ? PathFor(_store.SavePath) : null;
            Saved saved = Load(path);
            Vector2 cc = RoomGeometry.Centroid(zone);
            var c = new Vec2(cc.x, cc.y);
            float yaw = ZoneAlign.FrameYaw(ZoneFit.AtPiece(_room.Furniture, c), _room.RealOutline, c);
            bool grown = Compute(_room.RealOutline, zone, _room.Furniture, yaw, saved, _result, out Saved now);
            if (grown && path != null && !Same(saved, now)) Save(path, now);
            _room.Extend(grown ? _result : null);
        }

        /// <summary>
        /// The extended outline (pure, for tests) into <paramref name="result"/>; false only without a usable room or zone.
        /// <paramref name="saved"/> (or null) is kept inside the result: the extent never shrinks. <paramref name="furniture"/>
        /// is no longer used (it sized the old small-room test) and is kept for callers.
        /// </summary>
        public static bool Compute(IReadOnlyList<Vec2> real, IReadOnlyList<Vec2> zone, IReadOnlyList<FurnitureAnchor> furniture, float yaw,
            Saved saved, List<Vec2> result, out Saved extent)
        {
            result.Clear();
            extent = null;
            if (real == null || real.Count < 3 || zone == null || zone.Count < 3) return false;
            Bounds(real, yaw, out float x0, out float z0, out float x1, out float z1);
            Bounds(zone, yaw, out float zx0, out float zz0, out float zx1, out float zz1);
            // Kept only in the same frame (a new zone direction starts over), so it never grows by re-boxing a turned rectangle.
            bool hasSaved = saved != null && saved.Valid && Mathf.Abs(Mathf.DeltaAngle(saved.yaw, yaw)) < 0.5f;
            {
                // gaps: left, right, back, front (outside the zone, in the frame).
                float gl = zx0 - x0, gr = x1 - zx1, gb = zz0 - z0, gf = z1 - zz1;
                float best = Mathf.Max(Mathf.Max(gl, gr), Mathf.Max(gb, gf));
                if (best == gl) x0 = zx0 - Reach(gl);
                else if (best == gr) x1 = zx1 + Reach(gr);
                else if (best == gb) z0 = zz0 - Reach(gb);
                else z1 = zz1 + Reach(gf);
                bool alongX = best == gl || best == gr; // grew in x: the room must also be MinSide deep in z
                if (alongX) Widen(ref z0, ref z1, zz0 - z0 > Touching, z1 - zz1 > Touching);
                else Widen(ref x0, ref x1, zx0 - x0 > Touching, x1 - zx1 > Touching);
            }
            if (hasSaved) { x0 = Mathf.Min(x0, saved.x0); z0 = Mathf.Min(z0, saved.z0); x1 = Mathf.Max(x1, saved.x1); z1 = Mathf.Max(z1, saved.z1); }
            // My zone is always inside (review of #292: after a reset the rectangle could cut through it).
            x0 = Mathf.Min(x0, zx0); z0 = Mathf.Min(z0, zz0); x1 = Mathf.Max(x1, zx1); z1 = Mathf.Max(z1, zz1);
            extent = new Saved { yaw = yaw, x0 = x0, z0 = z0, x1 = x1, z1 = z1 };
            result.Add(Rotate(new Vec2(x0, z0), yaw));
            result.Add(Rotate(new Vec2(x1, z0), yaw));
            result.Add(Rotate(new Vec2(x1, z1), yaw));
            result.Add(Rotate(new Vec2(x0, z1), yaw));
            if (RoomGeometry.SignedArea(result) < 0f) result.Reverse(); // counter-clockwise, as the scanned outline
            return true;
        }

        /// <summary>
        /// How far past my zone the grown side reaches, from the real gap there (pure): at least <see cref="TargetGap"/>, at
        /// least <see cref="MinGrow"/> more than the gap (so On always widens), at most <see cref="MaxGrow"/> more.
        /// </summary>
        public static float Reach(float gap) => Mathf.Min(Mathf.Max(gap + MinGrow, TargetGap), gap + MaxGrow);

        // Grows a too-narrow span to MinSide on its free sides (away from a zone that touches the other), else on both.
        static void Widen(ref float lo, ref float hi, bool loFree, bool hiFree)
        {
            float need = MinSide - (hi - lo);
            if (need <= 0f) return;
            if (loFree && !hiFree) lo -= need;
            else if (hiFree && !loFree) hi += need;
            else { lo -= need * 0.5f; hi += need * 0.5f; }
        }

        static void Bounds(IReadOnlyList<Vec2> p, float yaw, out float x0, out float z0, out float x1, out float z1)
        {
            x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
            for (int i = 0; i < p.Count; i++)
            {
                Vec2 q = Rotate(p[i], -yaw);
                x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y);
            }
        }

        static Vec2 Rotate(Vec2 p, float yawDegrees)
        {
            double r = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(r), sin = (float)Math.Sin(r);
            return new Vec2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }

        static bool Same(Saved a, Saved b) =>
            a != null && b != null && Mathf.Abs(a.yaw - b.yaw) < 1e-3f && Mathf.Abs(a.x0 - b.x0) < 1e-3f && Mathf.Abs(a.z0 - b.z0) < 1e-3f
            && Mathf.Abs(a.x1 - b.x1) < 1e-3f && Mathf.Abs(a.z1 - b.z1) < 1e-3f;

        /// <summary>This room's saved extent, or null (none, unreadable, or not <see cref="Saved.Valid"/>).</summary>
        public static Saved Load(string path)
        {
            if (path == null || !File.Exists(path)) return null;
            try
            {
                Saved s = JsonUtility.FromJson<Saved>(File.ReadAllText(path));
                if (s != null && s.Valid) return s;
                Debug.LogWarning("[OwnSpace.MR] Extent file ignored: not a valid rectangle.");
                return null;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { Debug.LogWarning($"[OwnSpace.MR] Extent not read: {e.Message}"); return null; }
        }

        public static void Save(string path, Saved extent)
        {
            try { AtomicFile.WriteAllText(path, JsonUtility.ToJson(extent)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"[OwnSpace.MR] Extent not saved: {e.Message}"); }
        }

        /// <summary>Forgets this room's extent (Reset my design).</summary>
        public static void Clear(string designPath) { string p = PathFor(designPath); if (File.Exists(p)) File.Delete(p); }
    }
}

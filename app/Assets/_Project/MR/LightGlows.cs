using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Light polish without real-time lights (72 Hz budget, CONCEPT 5.6), using content's glow
    /// (<c>Art/Lighting/LightGlow.prefab</c>, shader <c>OwnSpace/LightGlow</c>):
    /// a soft glow on the floor under every placed piece whose prefab carries content's
    /// <see cref="LampLight"/> (floor lamps and pendants), sized by its <c>glowRadius</c>, coloured by its
    /// <c>kelvin</c> (0 = the mood's lamp temperature) and as strong as its <c>intensity</c> times the
    /// mood's lamp multiplier; and a soft patch of window light on the floor in front of every window
    /// in a virtual wall, in the mood's window colour. Rebuilt on design or room change; per frame
    /// only an int compare, plus property-block updates while a mood blends. No allocation per frame.
    /// Device batch 10 ("no light comes from the lamps"):
    /// <list type="bullet">
    /// <item>Redesigned lamps glow too.</item>
    /// <item>Every lamp also gets a soft halo at its bulb (content's <c>sourceHeight</c>), turned to face the user.</item>
    /// <item>The strength has a visible base plus the mood's share (<see cref="LampStrength"/>), so lamps read by day and
    /// are stronger in Evening and Night.</item>
    /// </list>
    /// Each rebuild is logged.
    /// </summary>
    public sealed class LightGlows : MonoBehaviour
    {
        struct Glow
        {
            public Renderer Renderer;
            public float Kelvin, Intensity; // Kelvin 0 = follow the mood
            public bool Halo; // at the bulb, facing the user (else flat on the floor)
            public Vector3 Bulb; // the halo's light source
            public float Pull;   // how far in front of the bulb, toward the user (clear of the shade)
            public bool Wall;    // the warm wash on a nearby virtual wall
        }

        /// <summary>A lamp this close to a virtual wall (m) also washes it (realism pass); at this share of the floor pool.</summary>
        public const float WallReach = 0.6f, WallShare = 0.6f;

        /// <summary>
        /// The wall a lamp washes (pure, for tests): the room outline's edge nearest <paramref name="lamp"/> on the floor, within
        /// <see cref="WallReach"/>; its closest point and its normal into the room. False with none that close.
        /// </summary>
        public static bool NearWall(IReadOnlyList<Vec2> outline, Vector3 lamp, out Vector3 onWall, out Vector3 intoRoom)
        {
            onWall = intoRoom = Vector3.zero;
            float best = WallReach;
            int n = outline != null ? outline.Count : 0;
            var c = n >= 3 ? RoomGeometry.Centroid(outline) : Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                Vec2 a = outline[i], b = outline[(i + 1) % n];
                float vx = b.x - a.x, vz = b.y - a.y, l = vx * vx + vz * vz;
                if (l < 1e-6f) continue;
                float t = Mathf.Clamp01(((lamp.x - a.x) * vx + (lamp.z - a.y) * vz) / l);
                float px = a.x + vx * t, pz = a.y + vz * t, d = Mathf.Sqrt((lamp.x - px) * (lamp.x - px) + (lamp.z - pz) * (lamp.z - pz));
                if (d >= best) continue;
                best = d;
                onWall = new Vector3(px, lamp.y, pz);
                var nrm = new Vector3(-vz, 0f, vx).normalized;
                intoRoom = Vector3.Dot(nrm, new Vector3(c.x - px, 0f, c.y - pz)) < 0f ? -nrm : nrm;
            }
            return best < WallReach;
        }

        /// <summary>Shader intensity at the bulb relative to the floor pool.</summary>
        public const float HaloBoost = 0.7f, BaseLamp = 0.35f, MoodLamp = 0.5f, MinKelvin = 2700f, ToAmber = 0.6f;
        public static readonly Color Amber = new Color(1f, 0.78f, 0.5f);

        /// <summary>
        /// The glow colour: soft amber, never red-orange. Night's 2200 K drew a saturated red blob, so the temperature is
        /// at least <see cref="MinKelvin"/> and the colour is pulled <see cref="ToAmber"/> of the way to <see cref="Amber"/>.
        /// </summary>
        public static Color LampColor(float kelvin) =>
            Color.Lerp(Mathf.CorrelatedColorTemperatureToRGB(Mathf.Max(MinKelvin, kelvin)), Amber, ToAmber);

        /// <summary>
        /// The lamp glow's shader intensity: a visible base plus the mood's share (Day 0.2 → Night 1.4), clamped to the
        /// shader's 0-2. Before batch 10 it was intensity × mood × 0.5, which is 0.08 for an amber lamp by day: invisible.
        /// </summary>
        public static float LampStrength(float intensity, float moodMultiplier) =>
            Mathf.Clamp(intensity * (BaseLamp + MoodLamp * Mathf.Max(0f, moodMultiplier)), 0f, 2f);

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] MoodApplier _mood;
        [SerializeField, Tooltip("content's Art/Lighting/LightGlow.prefab (1 m quad).")] GameObject _glowPrefab;
        [SerializeField, Tooltip("content's catalog id -> prefab map (lamp data lives on the prefab).")] CatalogPrefabs _prefabs;
        [SerializeField, Tooltip("Floor patch distance from the wall per metre of window height (sun ~60 degrees).")] float _windowReach = 0.6f;
        [SerializeField, Tooltip("Shader intensity for window intensity 1.")] float _windowScale = 0.4f;

        readonly List<Glow> _lamps = new List<Glow>();
        readonly List<Renderer> _windows = new List<Renderer>();
        MaterialPropertyBlock _block;
        int _seenVersion = -1, _loggedLamps = -1, _loggedWindows = -1;
        Transform _head;

        void Start()
        {
            _block = new MaterialPropertyBlock();
            _store.Session.Changed += Rebuild;
            _room.Changed += Rebuild;
            _zone.Confirmed += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (_store != null && _store.Session != null) _store.Session.Changed -= Rebuild;
            if (_room != null) _room.Changed -= Rebuild;
            if (_zone != null) _zone.Confirmed -= Rebuild;
        }

        void Update()
        {
            FaceHalos();
            if (_mood == null || _mood.Version == _seenVersion) return;
            _seenVersion = _mood.Version;
            for (int i = 0; i < _lamps.Count; i++)
            {
                Glow g = _lamps[i];
                float strength = LampStrength(g.Intensity, _mood.LampMultiplier) * (g.Halo ? HaloBoost : g.Wall ? WallShare : 1f);
                Set(g.Renderer, LampColor(g.Kelvin > 0f ? g.Kelvin : _mood.LampKelvin), strength);
            }
            for (int i = 0; i < _windows.Count; i++) Set(_windows[i], _mood.WindowColor, _mood.WindowIntensity * _windowScale);
        }

        void Set(Renderer r, Color color, float intensity)
        {
            r.GetPropertyBlock(_block);
            _block.SetColor(ColorId, color);
            _block.SetFloat(IntensityId, Mathf.Clamp(intensity, 0f, 2f));
            r.SetPropertyBlock(_block);
        }

        void Rebuild()
        {
            Clear();
            if (_glowPrefab == null) return;
            float floor = _room.FloorY;
            DesignState state = _store.Session.State;
            ICatalog catalog = _store.Session.Catalog;

            for (int i = 0; catalog != null && _prefabs != null && i < state.placements.Count; i++)
            {
                FreePlacement p = state.placements[i];
                AddLamp(p.catalogItemId, new Vector3(p.position.x, floor, p.position.z));
            }
            // Redesigned real pieces that became lamps (device batch 10: they never glowed).
            var furniture = _room.Furniture;
            for (int i = 0; catalog != null && _prefabs != null && i < state.anchorChoices.Count; i++)
            {
                AnchorChoice c = state.anchorChoices[i];
                if (c.mode != AnchorMode.Redesign) continue;
                for (int k = 0; k < furniture.Count; k++)
                    if (furniture[k].Id == c.anchorId) { AddLamp(c.catalogItemId, new Vector3(furniture[k].Box.center.x, floor, furniture[k].Box.center.z)); break; }
            }

            bool myWallVirtual = !string.IsNullOrEmpty(state.myZoneFinish.wallFinishId);
            var windows = _room.Windows;
            for (int i = 0; _room.HasScan && !_room.Extended && i < windows.Count; i++) // no real windows in an extended room
            {
                OrientedBox w = windows[i].Box;
                if (!myWallVirtual && Zones.Contains(state.zoneOutline, w.center.Floor())) continue; // real wall: real light
                Vector3 into = ShellGeometry.IntoRoom(w, _room.FloorOutline);
                float bottom = Mathf.Max(0f, w.center.y - w.size.y * 0.5f - floor), top = bottom + w.size.y;
                float near = bottom * _windowReach, far = top * _windowReach;
                Vector3 c = new Vector3(w.center.x, floor + 0.005f, w.center.z) + into * ((near + far) * 0.5f);
                _windows.Add(Decal(c, into, w.size.x, Mathf.Max(0.3f, far - near)));
            }
            _seenVersion = -1; // recolour on the next frame
            if (_lamps.Count != _loggedLamps || _windows.Count != _loggedWindows)
            {
                _loggedLamps = _lamps.Count;
                _loggedWindows = _windows.Count;
                int lamps = 0, walls = 0;
                for (int i = 0; i < _lamps.Count; i++) { if (_lamps[i].Halo) lamps++; if (_lamps[i].Wall) walls++; }
                Debug.Log($"[OwnSpace.MR] Light glows: {lamps} lamps (floor pool + bulb halo), {walls} wall wash(es), {_windows.Count} window patches.");
            }
        }

        // A lamp: a pool on the floor and a halo at the bulb.
        void AddLamp(string itemId, Vector3 floorPoint)
        {
            if (!_prefabs.TryGetPrefab(itemId, out GameObject prefab) || !prefab.TryGetComponent(out LampLight lamp)) return;
            float radius = Mathf.Max(0.1f, lamp.GlowRadius), pool = 2f * radius * 1.5f; // the glow spreads wider on the floor than at the shade
            _lamps.Add(new Glow { Renderer = Decal(floorPoint + Vector3.up * 0.004f, Vector3.forward, pool, pool), Kelvin = lamp.Kelvin, Intensity = lamp.Intensity });
            // Larger and fainter than before, and pulled in front of the shade so the shade never cuts it into a hard edge.
            Vector3 bulb = floorPoint + Vector3.up * lamp.SourceHeight;
            Renderer halo = Decal(bulb, Vector3.forward, radius * 2.4f, radius * 2.4f);
            _lamps.Add(new Glow { Renderer = halo, Kelvin = lamp.Kelvin, Intensity = lamp.Intensity, Halo = true, Bulb = bulb, Pull = radius * 0.8f });
            // A lamp by a virtual wall washes it warm (one more additive quad, the floor pool's budget).
            if (NearWall(_room.FloorOutline, floorPoint, out Vector3 onWall, out Vector3 into))
            {
                float h = Mathf.Max(0.4f, lamp.SourceHeight * 1.3f);
                Vector3 centre = onWall + into * 0.006f + Vector3.up * (h * 0.5f);
                GameObject go = Instantiate(_glowPrefab, centre, Quaternion.LookRotation(-into, Vector3.up), transform);
                go.transform.localScale = new Vector3(pool * 0.8f, h, 1f);
                _lamps.Add(new Glow { Renderer = go.GetComponentInChildren<Renderer>(), Kelvin = lamp.Kelvin, Intensity = lamp.Intensity, Wall = true });
            }
        }

        // Halos face the user (a few quads; no allocation).
        void FaceHalos()
        {
            if (_head == null) { Camera cam = Camera.main; if (cam == null) return; _head = cam.transform; }
            Vector3 head = _head.position;
            for (int i = 0; i < _lamps.Count; i++)
            {
                if (!_lamps[i].Halo || _lamps[i].Renderer == null) continue;
                Transform t = GameObjectOf(_lamps[i].Renderer).transform;
                Vector3 away = _lamps[i].Bulb - head;
                if (away.sqrMagnitude < 1e-6f) continue;
                Vector3 dir = away.normalized;
                t.SetPositionAndRotation(_lamps[i].Bulb - dir * Mathf.Min(_lamps[i].Pull, away.magnitude * 0.5f), Quaternion.LookRotation(dir, Vector3.up));
            }
        }

        Renderer Decal(Vector3 position, Vector3 lengthAxis, float width, float length)
        {
            // Lay content's quad flat: its normal points down/up, its local y runs along lengthAxis.
            GameObject go = Instantiate(_glowPrefab, position, Quaternion.LookRotation(Vector3.down, lengthAxis), transform);
            go.transform.localScale = new Vector3(width, length, 1f);
            return go.GetComponentInChildren<Renderer>();
        }

        void Clear()
        {
            for (int i = 0; i < _lamps.Count; i++) if (_lamps[i].Renderer != null) Destroy(GameObjectOf(_lamps[i].Renderer));
            for (int i = 0; i < _windows.Count; i++) if (_windows[i] != null) Destroy(GameObjectOf(_windows[i]));
            _lamps.Clear();
            _windows.Clear();
        }

        // The decal root is our direct child (the prefab instance), whatever its renderer's depth.
        GameObject GameObjectOf(Renderer r)
        {
            Transform t = r.transform;
            while (t.parent != null && t.parent != transform) t = t.parent;
            return t.gameObject;
        }
    }
}

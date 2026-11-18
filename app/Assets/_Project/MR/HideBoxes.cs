using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Shows hidden clutter (CONCEPT 5.5) from core's design state: every
    /// <see cref="DesignState.hideBoxes"/> entry as a box in its finish (or my zone's floor finish,
    /// or the default floor), and every <see cref="DesignState.coverProps"/> entry as its prefab.
    /// The rules, undo and saving live in core's <see cref="DesignSession"/> and <see cref="DesignStore"/>.
    /// Because hidden things are still there, standing up (head rising 30 cm above its seated
    /// height) shows every hide box and cover prop as a yellow outline for a few seconds.
    /// Rebuilt on design change only; the stand-up check is a few floats per frame.
    /// </summary>
    public sealed class HideBoxes : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";

        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField, Tooltip("Floor material when the zone has no floor finish.")] Material _defaultFloor;
        [SerializeField, Tooltip("Outline material (URP Unlit).")] Material _outline;
        [SerializeField] FinishSet _finishes;
        [SerializeField] CatalogPrefabs _prefabs;
        [SerializeField, Tooltip("Head rise above the seated height that counts as standing up (m).")] float _standRise = 0.3f;
        [SerializeField, Tooltip("Seconds the outlines stay visible after standing up or adding a box.")] float _revealSeconds = 5f;

        readonly List<GameObject> _shown = new List<GameObject>();
        readonly List<string> _shownIds = new List<string>(); // the hide box or cover prop each shown object stands for
        readonly List<LineRenderer> _outlines = new List<LineRenderer>();
        int _outlineCount;
        float _seatedY, _revealUntil;
        bool _haveSeat, _standing, _revealed;

        readonly CoverAdjusts _scales = new CoverAdjusts();

        /// <summary>A prop cover's width and depth scale (the user's resize; 1, 1 by default).</summary>
        public Vector2 ScaleOf(string placementId) { _scales.Use(_store.SavePath); return _scales.Get(placementId); }

        /// <summary>Sets a prop cover's scale (saved with the room) and shows it.</summary>
        public void SetScale(string placementId, Vector2 scale)
        {
            _scales.Use(_store.SavePath);
            _scales.Set(placementId, scale);
            Rebuild();
        }

        /// <summary>Turns a shown cover in place while its corner handle is dragged (nothing is saved; Rebuild puts it back).</summary>
        public void Pose(string id, float yawDegrees)
        {
            for (int i = 0; i < _shown.Count; i++)
                if (_shownIds[i] == id && _shown[i] != null) _shown[i].transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        /// <summary>Hidden things in the design: hide boxes plus cover props.</summary>
        public int Count => _store.Session.State.hideBoxes.Count + _store.Session.State.coverProps.Count;

        void Start()
        {
            _store.Session.Changed += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (_store != null && _store.Session != null) _store.Session.Changed -= Rebuild;
        }

        /// <summary>Shows every hidden thing's outline for a few seconds (after adding one, or standing up).</summary>
        public void Reveal()
        {
            _revealUntil = Time.time + _revealSeconds;
            SetOutlines(true);
        }

        /// <summary>The box a hide box occupies (core stores its floor centre).</summary>
        public static OrientedBox BoxOf(HideBox h) =>
            new OrientedBox(new Vec3(h.position.x, h.position.y + h.size.y * 0.5f, h.position.z), h.size, h.yawDegrees);

        void Update()
        {
            if (_revealed && Time.time >= _revealUntil) SetOutlines(false);
            if (_outlineCount == 0) return;

            float y = _hands.HeadPosition.y;
            if (!_haveSeat) { _seatedY = y; _haveSeat = true; }
            // Follow the seated height: quickly down, very slowly up (so standing is not absorbed).
            float dt = Time.deltaTime;
            _seatedY = y < _seatedY ? Mathf.Lerp(_seatedY, y, Mathf.Clamp01(dt * 2f)) : _seatedY + (y - _seatedY) * Mathf.Clamp01(dt * 0.02f);
            // Hysteresis: standing starts above the full rise and ends below half of it.
            float rise = y - _seatedY;
            bool standing = _standing ? rise > _standRise * 0.5f : rise > _standRise;
            if (standing && !_standing)
            {
                Reveal();
                Debug.Log($"{LogTag} Standing up with {_outlineCount} hidden thing(s): outlines shown, take care.");
            }
            _standing = standing;
        }

        void Rebuild()
        {
            for (int i = 0; i < _shown.Count; i++) if (_shown[i] != null) Destroy(_shown[i]);
            _shown.Clear();
            _shownIds.Clear();
            _outlineCount = 0;
            DesignState s = _store.Session.State;
            _scales.Use(_store.SavePath); // this room's cover sizes
            for (int i = 0; i < s.hideBoxes.Count; i++)
            {
                OrientedBox b = BoxOf(s.hideBoxes[i]);
                Renderer cube = XrVisuals.Make(PrimitiveType.Cube, transform, Material(s, s.hideBoxes[i]), Color.white, "HideBox");
                cube.SetPropertyBlock(null); // keep the finish's own colours
                cube.transform.SetPositionAndRotation(BoxMath.Center(b), BoxMath.Rotation(b));
                cube.transform.localScale = new Vector3(b.size.x, b.size.y, b.size.z);
                _shown.Add(cube.gameObject);
                _shownIds.Add(s.hideBoxes[i].hideBoxId);
                Outline(b);
            }
            ICatalog catalog = _store.Session.Catalog;
            for (int i = 0; i < s.coverProps.Count; i++)
            {
                FreePlacement p = s.coverProps[i];
                if (catalog == null || !catalog.TryGet(p.catalogItemId, out CatalogItem item) ||
                    _prefabs == null || !_prefabs.TryGetPrefab(item.id, out GameObject prefab))
                {
                    // Was silent: a cover look with no prefab showed nothing over a box already removed.
                    Debug.LogWarning($"{LogTag} Cover look {p.catalogItemId} has no prefab; it cannot show.");
                    continue;
                }
                var pos = new Vector3(p.position.x, p.position.y, p.position.z);
                GameObject shown = Instantiate(prefab, pos, Quaternion.Euler(0f, p.yawDegrees, 0f), transform);
                Vector2 scale = _scales.Get(p.placementId); // the user's resize: width and depth only
                shown.transform.localScale = Vector3.Scale(shown.transform.localScale, new Vector3(scale.x, 1f, scale.y));
                // A cover prop sits on the floor like any piece (realism pass: it had no ground shadow); it scales with the prop.
                if (ContactShadows.Grounded(item)) ContactShadows.Attach(shown.transform, new Vector3(item.size.x, item.size.y, item.size.z));
                _shown.Add(shown);
                _shownIds.Add(p.placementId);
                Outline(new OrientedBox(new Vec3(pos.x, pos.y + item.size.y * 0.5f, pos.z), new Vec3(item.size.x * scale.x, item.size.y, item.size.z * scale.y), p.yawDegrees));
            }
            for (int i = _outlineCount; i < _outlines.Count; i++) _outlines[i].enabled = false;
            SetOutlines(_revealed);
        }

        Material Material(DesignState s, HideBox h)
        {
            string id = !string.IsNullOrEmpty(h.finishId) ? h.finishId : s.myZoneFinish.floorFinishId;
            return _finishes != null && _finishes.TryGet(id, out FinishSet.Entry e) ? e.material : _defaultFloor;
        }

        void Outline(OrientedBox b)
        {
            if (_outlineCount == _outlines.Count) _outlines.Add(NewOutline());
            BoxMath.WriteOutline(b, _outlines[_outlineCount++]);
        }

        LineRenderer NewOutline()
        {
            var go = new GameObject("HideOutline");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _outline;
            line.widthMultiplier = 0.012f;
            line.positionCount = BoxMath.OutlinePointCount;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            XrVisuals.Tint(line, XrVisuals.Hover);
            line.enabled = false;
            return line;
        }

        void SetOutlines(bool on)
        {
            _revealed = on;
            for (int i = 0; i < _outlines.Count; i++) _outlines[i].enabled = on && i < _outlineCount;
        }
    }
}

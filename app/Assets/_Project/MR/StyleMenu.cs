using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// A hands-only style menu (CONCEPT 5.4, 5.6). A small violet orb sits within reach, low and to
    /// the left; pinch it (or look at it and pinch) to open three rows:
    /// floor finishes and wall finishes (each starting with grey "real"), and moods (starting with
    /// grey "neutral"). The first row's leftmost sphere toggles which zone the finishes apply to:
    /// green = my zone, blue = outside. Pinch the orb again to close.
    /// Every choice goes through core's <see cref="Core.DesignSession"/> and can be undone.
    /// </summary>
    [DefaultExecutionOrder(5)]
    public sealed class StyleMenu : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        const int PerRow = 6, Rows = 3, Swatches = PerRow * Rows, Toggle = Swatches, Orb = Swatches + 1, Targets = Swatches + 2;
        static readonly Color Real = new Color(0.55f, 0.55f, 0.55f);
        static readonly Color Mine = new Color(0.3f, 0.85f, 0.45f), Outside = new Color(0.3f, 0.55f, 1f);

        [SerializeField] DesignStore _store;
        [SerializeField, Tooltip("Keeps the orb off the walls; optional.")] MrukRoomInfo _room;
        [SerializeField] HandRig _hands;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] Material _material;
        [SerializeField] FinishSet _finishes;
        [SerializeField] MoodPresets _moods;
        [SerializeField, Tooltip("Direct pinch reach (m).")] float _nearRadius = 0.06f;

        readonly Renderer[] _targets = new Renderer[Targets];
        readonly string[] _ids = new string[Swatches]; // finish or mood id per swatch; "" = real / neutral
        bool _open;
        readonly ToolFollow _follow = new ToolFollow(-0.2f);

        /// <summary>Where the style orb is, and whether it is shown (for onboarding hints).</summary>
        public Vector3 OrbPosition => _targets[Orb] != null ? _targets[Orb].transform.position : Vector3.zero;
        public bool OrbShown => _targets[Orb] != null && _targets[Orb].gameObject.activeSelf;
        DesignZone _zoneTarget = DesignZone.Outside;

        void Start()
        {
            for (int i = 0; i < Targets; i++)
            {
                _targets[i] = XrVisuals.Make(i == Orb ? PrimitiveType.Sphere : i == Toggle ? PrimitiveType.Sphere : PrimitiveType.Cube,
                    transform, _material, XrVisuals.Idle, "StyleTarget");
                _targets[i].transform.localScale = Vector3.one * (i == Orb ? 0.07f : 0.05f);
                _targets[i].gameObject.SetActive(false);
                if (i < Swatches) _ids[i] = null;
            }
            XrVisuals.Tint(_targets[Orb], new Color(0.65f, 0.4f, 0.95f));
            FillRow(0, "floor");
            FillRow(1, "wall");
            FillMoods();
            _zone.Confirmed += ShowOrb;
            _zone.EditStarted += HideAll;
            if (_zone.Current == ZoneSetup.Phase.Confirmed) ShowOrb();
        }

        void OnDestroy()
        {
            if (_zone != null) { _zone.Confirmed -= ShowOrb; _zone.EditStarted -= HideAll; }
        }

        void FillRow(int row, string surface)
        {
            int slot = row * PerRow + (row == 0 ? 1 : 0); // row 0 starts after the zone toggle
            Set(slot++, "", Real);
            var entries = _finishes != null ? _finishes.Entries : null;
            for (int i = 0; entries != null && i < entries.Count && slot < (row + 1) * PerRow; i++)
            {
                FinishSet.Entry e = entries[i];
                if (e.material == null || (e.surface != surface && e.surface != "both")) continue;
                Set(slot++, e.id, e.material.HasProperty("_BaseColor") ? e.material.GetColor("_BaseColor") : Color.white);
            }
        }

        void FillMoods()
        {
            int slot = 2 * PerRow;
            Set(slot++, "", Real);
            var entries = _moods != null ? _moods.Entries : null;
            for (int i = 0; entries != null && i < entries.Count && slot < Swatches; i++)
                Set(slot++, entries[i].id, entries[i].windowColor);
        }

        void Set(int slot, string id, Color color)
        {
            _ids[slot] = id;
            XrVisuals.Tint(_targets[slot], color);
        }

        void ShowOrb()
        {
            // Within reach while seated: 35 cm ahead, 20 cm left, 30 cm below the eyes; follows the user (Update).
            _follow.Snap(_targets[Orb].transform, _hands.View, _room != null ? _room.FloorOutline : null);
            _targets[Orb].gameObject.SetActive(true);
            SetOpen(false);
        }

        void HideAll()
        {
            SetOpen(false);
            _targets[Orb].gameObject.SetActive(false);
        }

        void SetOpen(bool open)
        {
            _open = open;
            Transform orb = _targets[Orb].transform;
            Vector3 forward = Vector3.ProjectOnPlane(orb.position - _hands.HeadPosition, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i <= Toggle; i++)
            {
                bool shown = open && (i == Toggle || _ids[i] != null);
                _targets[i].gameObject.SetActive(shown);
                if (!shown) continue;
                int row = i == Toggle ? 0 : i / PerRow, col = i == Toggle ? 0 : i % PerRow;
                _targets[i].transform.position = orb.position + right * (0.1f + col * 0.08f) + Vector3.up * (0.2f - row * 0.08f);
            }
            XrVisuals.Tint(_targets[Toggle], _zoneTarget == DesignZone.Mine ? Mine : Outside);
        }

        void Update()
        {
            if (!_targets[Orb].gameObject.activeSelf) return;
            _follow.Tick(_targets[Orb].transform, _hands.View, _room != null ? _room.FloorOutline : null, !_open, Time.deltaTime);
            if (_hands.ClaimedThisFrame) return;
            HandState left = _hands.Left, right = _hands.Right;
            if (!right.PinchStarted && !left.PinchStarted) return;
            int target = Pick(right.PinchStarted ? right.PinchPoint : left.PinchPoint);
            if (target < 0) return;
            _hands.Claim();

            if (target == Orb) { SetOpen(!_open); return; }
            if (target == Toggle)
            {
                _zoneTarget = _zoneTarget == DesignZone.Mine ? DesignZone.Outside : DesignZone.Mine;
                XrVisuals.Tint(_targets[Toggle], _zoneTarget == DesignZone.Mine ? Mine : Outside);
                return;
            }
            Choose(target);
        }

        void Choose(int slot)
        {
            var session = _store.Session;
            string id = _ids[slot];
            int row = slot / PerRow;
            string before = Current(row);
            if (row != 2)
            {
                // One choice for the whole room: both zones take the pick (as on the panel's Style tab).
                int steps = StylePage.SetBoth(session, row == 0 ? FinishSurface.Floor : FinishSurface.Wall, id);
                if (steps > 0) _store.NoteUserChange(steps);
                Debug.Log($"{LogTag} Style: {(row == 0 ? "floor" : "wall")} (whole room) -> '{id}'");
                return;
            }
            session.SetMood(id);
            // Core refuses unknown ids and ignores no-ops; only a real change joins the undo barrier.
            if (Current(row) != before) _store.NoteUserChange();
            Debug.Log($"{LogTag} Style: {(row == 2 ? "mood" : _zoneTarget + (row == 0 ? " floor" : " wall"))} -> '{id}'");
        }

        string Current(int row)
        {
            DesignState s = _store.Session.State;
            if (row == 2) return s.moodPresetId;
            ZoneFinish f = _zoneTarget == DesignZone.Mine ? s.myZoneFinish : s.outsideFinish;
            return row == 0 ? f.floorFinishId : f.wallFinishId;
        }

        int Pick(Vector3 pinch)
        {
            int best = -1;
            float bestMiss = float.MaxValue;
            for (int i = 0; i < Targets; i++)
            {
                if (!_targets[i].gameObject.activeSelf) continue;
                Vector3 c = _targets[i].transform.position;
                if (Vector3.Distance(pinch, c) < _nearRadius) return i;
                float miss = _hands.GazeHit(c);
                if (miss >= 0f && miss < bestMiss) { bestMiss = miss; best = i; }
            }
            return best;
        }
    }
}

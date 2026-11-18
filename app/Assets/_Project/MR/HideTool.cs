using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Hides clutter by hand (CONCEPT 5.5) through core's <see cref="DesignSession"/> and
    /// <see cref="HideRules"/>. A white cube sits within reach, low and to the right. Pinch it (orange =
    /// armed), then on the floor of my zone pinch-hold at one corner and release at the opposite one
    /// (hand near the start: your fingers; otherwise your gaze). The pending box shows yellow when the
    /// rules allow it and red when not (window, door, desk top, outside my zone…); a sphere on top
    /// sets its height, green Add commits it (<see cref="DesignSession.TryAddHideBox"/>), Done drops it.
    /// Pinch a hide box to cover it with a clutter prop (<see cref="DesignSession.TryAddCoverProp"/>,
    /// which then replaces the box), or remove it; pinch a cover prop to remove it. Undo is the shared
    /// one (never past the zone). Losing the hand cancels a drag.
    /// </summary>
    // -4: after the tool panel (-10) and an armed card (-5), before EditMode (0): an armed "Draw a box" owns the floor
    // pinch (device batch 9: "draws nothing" when the ray crossed a real box, which EditMode took).
    [DefaultExecutionOrder(-4)]
    public sealed class HideTool : MonoBehaviour, ITidyUp, ICoverResize
    {
        const string LogTag = "[OwnSpace.MR]";
        const int MaxCovers = 4, Add = MaxCovers, Remove = MaxCovers + 1, Undo = MaxCovers + 2, Done = MaxCovers + 3,
            Height = MaxCovers + 4, Orb = MaxCovers + 5, Targets = MaxCovers + 6;
        const float DefaultHeight = 0.4f;
        static readonly Color Refused = new Color(0.9f, 0.15f, 0.15f);
        enum Mode { Idle, Armed, Drawing, Pending, Resizing, SelectedBox, SelectedProp }

        [SerializeField, Tooltip("Driven by the tool panel: no orb, chips or tiles in the room (the logic and world input stay).")] bool _panelMode;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] HideBoxes _boxes;
        [SerializeField] Material _material;
        [SerializeField, Tooltip("Direct pinch reach (m).")] float _nearRadius = 0.06f;

        readonly Renderer[] _targets = new Renderer[Targets];
        readonly List<CatalogItem> _covers = new List<CatalogItem>(MaxCovers);
        Renderer _preview;
        WorldLabel _reason;
        readonly ToolFollow _follow = new ToolFollow(0.2f);
        Mode _mode;
        HideBox _pending;
        string _selected;
        bool _dragLeft, _dragNear;
        Vector3 _start, _handStart;
        float _yaw, _startHeight;
        HideStatus _shownStatus = (HideStatus)(-1);

        void Start()
        {
            _reason = new WorldLabel(transform, "Refusal", new Color(1f, 0.55f, 0.5f), refusal: true);
            Color[] colors = { XrVisuals.Confirmed, Refused, new Color(0.5f, 0.5f, 0.5f), new Color(0.75f, 0.75f, 0.75f), XrVisuals.Hover, Color.white };
            for (int i = 0; i < Targets; i++)
            {
                bool sphere = i >= MaxCovers && i != Orb;
                _targets[i] = XrVisuals.Make(sphere ? PrimitiveType.Sphere : PrimitiveType.Cube, transform, _material,
                    i < MaxCovers ? XrVisuals.Idle : colors[i - MaxCovers], "HideTarget");
                _targets[i].transform.localScale = Vector3.one * (i == Orb ? 0.06f : 0.05f);
                _targets[i].gameObject.SetActive(false);
                _targets[i].enabled = !_panelMode;
            }
            _preview = XrVisuals.Make(PrimitiveType.Cube, transform, _material, XrVisuals.Hover, "HidePreview");
            // While armed, a ring on the floor under the ray shows where the box will start.
            _drawMarker = XrVisuals.Make(PrimitiveType.Cylinder, transform, _material, new Color(0.039f, 0.455f, 0.878f), "DrawMarker");
            _drawMarker.transform.localScale = new Vector3(0.14f, 0.002f, 0.14f);
            _drawMarker.gameObject.SetActive(false);
            _preview.gameObject.SetActive(false);

            ICatalog catalog = _store.Session.Catalog;
            for (int i = 0; catalog != null && i < catalog.Items.Count && _covers.Count < MaxCovers; i++)
            {
                if (catalog.Items[i].kind != HideRules.CoverPropKind) continue;
                XrVisuals.Tint(_targets[_covers.Count], RedesignView.ColorOf(catalog.Items[i]));
                _covers.Add(catalog.Items[i]);
            }

            _zone.Confirmed += ShowOrb;
            _zone.EditStarted += HideAll;
            _store.Session.Changed += OnDesignChanged;
            if (_zone.Current == ZoneSetup.Phase.Confirmed) ShowOrb();
        }

        void OnDestroy()
        {
            if (_zone != null) { _zone.Confirmed -= ShowOrb; _zone.EditStarted -= HideAll; }
            if (_store != null && _store.Session != null) _store.Session.Changed -= OnDesignChanged;
        }

        void OnDesignChanged()
        {
            if (_replacing) return; // our own remove-and-add of a resized box: the selection stays
            // An undo or a removal can take the selected thing away.
            if ((_mode == Mode.SelectedBox && BoxIndex(_selected) < 0) || (_mode == Mode.SelectedProp && PropIndex(_selected) < 0))
                SetMode(Mode.Idle);
        }

        void ShowOrb()
        {
            // 35 cm ahead, 20 cm right, 30 cm below the eyes; follows the user (Update).
            _follow.Snap(_targets[Orb].transform, _hands.View, _room.FloorOutline);
            SetMode(Mode.Idle);
        }

        void HideAll()
        {
            SetMode(Mode.Idle);
            _targets[Orb].gameObject.SetActive(false);
        }

        void Update()
        {
            _reason.Tick();
            if (_zone.Current != ZoneSetup.Phase.Confirmed || !_targets[Orb].gameObject.activeSelf) return;
            HandState left = _hands.Left, right = _hands.Right;
            // Only while nothing hangs off it: idle (just Undo beside it) or armed to draw.
            bool free = _mode == Mode.Idle || _mode == Mode.Armed;
            if (_follow.Tick(_targets[Orb].transform, _hands.View, _room.FloorOutline, free, Time.deltaTime)) Layout();
            if (_turning) { TrackTurn(_turnLeft ? left : right); return; }
            if (_mode == Mode.Drawing || _mode == Mode.Resizing) { Drag(_dragLeft ? left : right); return; }
            ShowDrawMarker();
            ShowTurnHandle();
            if (!right.PinchStarted && !left.PinchStarted) return;
            if (_hands.ClaimedThisFrame) { if (_mode == Mode.Armed) InputDebug.Log("hide: armed pinch already taken (the panel or a placement)"); return; }
            bool isLeft = !right.PinchStarted;
            HandState hand = isLeft ? left : right;

            int target = _panelMode ? -1 : PickTarget(hand.PinchPoint); // the Tidy up tab replaces the cube's buttons
            if (target >= 0) { _hands.Claim(); Press(target, isLeft, hand); return; }
            if (_mode == Mode.Armed)
            {
                if (!_hands.HasAim(isLeft)) InputDebug.Log("hide: armed pinch, but that hand is not aiming");
                else if (XrVisuals.RayFloor(_hands.Aim(isLeft), _room.FloorY, out Vector3 hit)) { _hands.Claim(); _drawMarker.gameObject.SetActive(false); BeginDraw(hit, isLeft, hand); }
                else InputDebug.Log("hide: armed pinch, but the ray does not reach the floor");
                return;
            }
            if (_mode == Mode.Pending) return;
            if (TryBeginTurn(isLeft, _hands.Aim(isLeft))) { _hands.Claim(); return; }
            if (PickHidden(hand.PinchPoint, _hands.Aim(isLeft), out string id, out bool isProp))
            {
                _hands.Claim();
                _selected = id;
                InputDebug.Log($"hide: pinch on {(isProp ? "cover prop" : "hide box")} {id}");
                // A plain box jumps to step 2 (pick its look). A prop already has its look: step 3, where it can be
                // resized or removed.
                _step = isProp ? TidyStep.Done : TidyStep.PickLook;
                SetMode(isProp ? Mode.SelectedProp : Mode.SelectedBox);
                if (isProp)
                {
                    _lastId = id; _lastIsProp = true;
                    int look = SelectedLook;
                    _coveredWith = look > 0 ? "Covered with a " + LookNames[look] + "." : "Covered.";
                    Changed?.Invoke();
                }
            }
        }

        // ---- the panel's Tidy up tab (spec v4 7.4) drives these ----

        // Look -> catalog id prefix (exact, so "cushion" is the single floor cushion, not the cushion pile) and its name.
        public static readonly string[] LookKeys = { "", "clutter-basket-", "clutter-plant-", "clutter-floor-cushion-", "clutter-box-", "clutter-screen-" };
        static readonly string[] LookNames = { "", "basket", "plant", "cushion", "box", "screen" };
        string _lastId;
        bool _lastIsProp;
        TidyStep _step = TidyStep.Draw;
        bool _autoAdd;
        string _coveredWith;

        public TidyStep Step => _step;
        public bool Drawing => _mode == Mode.Drawing;
        public string CoveredWith => _coveredWith;
        public bool HasSelection => _mode == Mode.SelectedBox || _mode == Mode.SelectedProp;

        /// <summary>The look of the selected cover (1-5; 0 = Plain, a box with no prop), or -1 with nothing selected.</summary>
        public int SelectedLook
        {
            get
            {
                if (_mode == Mode.SelectedBox) return 0;
                if (_mode != Mode.SelectedProp) return -1;
                int i = PropIndex(_selected);
                string id = i >= 0 ? _store.Session.State.coverProps[i].catalogItemId : null;
                for (int look = 1; id != null && look < LookKeys.Length; look++) if (id.StartsWith(LookKeys[look], StringComparison.Ordinal)) return look;
                return -1;
            }
        }
        public event Action Changed;
        public event Action<string> Refusal;

        /// <summary>Step 1: the next pinch-and-drag on the floor draws a box; releasing it adds the box when core accepts it.</summary>
        Renderer _drawMarker;

        // The armed ring follows the ray on the floor; hidden otherwise. Transform math only.
        void ShowDrawMarker()
        {
            Vector3 at = Vector3.zero;
            bool show = _mode == Mode.Armed && XrVisuals.RayFloor(_hands.Gaze, _room.FloorY, out at);
            if (_drawMarker.gameObject.activeSelf != show) _drawMarker.gameObject.SetActive(show);
            if (show) _drawMarker.transform.position = at + Vector3.up * 0.004f;
        }

        public void StartDrawing() { _autoAdd = true; _step = TidyStep.Draw; SetMode(Mode.Armed); }

        public void CancelDrawing() { _autoAdd = false; SetMode(Mode.Idle); }

        /// <summary>True while the next floor pinch would start a box (step 1 armed, not yet drawing).</summary>
        public bool ArmedToDraw => _mode == Mode.Armed;

        /// <summary>
        /// Gives the floor back (device batch 4): drops an armed "draw a box" so a floor pinch reaches the next owner
        /// (placing furniture). A box being drawn or a selection is left alone.
        /// </summary>
        public void Disarm() { if (_mode == Mode.Armed) CancelDrawing(); }

        /// <summary>The catalog cover prop for look <paramref name="look"/> (1-5), or null (Plain or none in the catalog).</summary>
        public CatalogItem LookItem(int look)
        {
            if (look <= 0 || look >= LookKeys.Length) return null;
            ICatalog catalog = _store != null ? _store.Session.Catalog : null;
            for (int i = 0; catalog != null && i < catalog.Items.Count; i++)
                if (catalog.Items[i].kind == HideRules.CoverPropKind && catalog.Items[i].id.StartsWith(LookKeys[look], StringComparison.Ordinal)) return catalog.Items[i];
            return null;
        }

        /// <summary>Step 2: covers the selected box with look <paramref name="look"/> (0 = Plain: the box itself); on success, step 3.</summary>
        public bool PickLook(int look)
        {
            if (_mode != Mode.SelectedBox) return false;
            _autoAdd = false; // the next box starts from "Draw a box" again
            if (look == 0) { _coveredWith = "Covered."; _lastId = _selected; _lastIsProp = false; _step = TidyStep.Done; SetMode(Mode.Idle); return true; }
            CatalogItem item = LookItem(look);
            InputDebug.Log($"hide: look {look} -> {(item != null ? item.id : "no catalog item")}");
            if (item == null || !Cover(item, null)) return false;
            _coveredWith = "Covered with a " + LookNames[look] + ".";
            _step = TidyStep.Done;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Removes the selected cover, or on step 3 the one just made (one undo step), and goes back to step 1.</summary>
        public bool RemoveSelected()
        {
            bool last = !HasSelection && _step == TidyStep.Done && _lastId != null;
            if (!HasSelection && !last) return false;
            DesignSession session = _store.Session;
            bool prop = last ? _lastIsProp : _mode == Mode.SelectedProp;
            string id = last ? _lastId : _selected;
            bool removed = prop ? session.RemoveCoverProp(id) : session.RemoveHideBox(id);
            _lastId = null;
            _autoAdd = false;
            if (removed) _store.NoteUserChange();
            _step = TidyStep.Draw;
            SetMode(Mode.Idle);
            return removed;
        }

        // ---- turn: the corner handle on the cover, as on furniture (15° steps) ----

        RotateHandle _handle;
        bool _turning, _turnLeft, _turnProp;
        string _turnId;
        Vector3 _turnFrom, _turnCentre, _turnSize;
        float _turnStartYaw, _turnYaw;

        /// <summary>Builds the corner handle in the panel's look (PanelHost calls it once).</summary>
        public void UseHandle(OwnSpace.UI.ToolPanel panel) { if (_handle == null && panel != null) _handle = new RotateHandle(transform, panel, _material); }

        // The cover the Done step is about, as a footprint: its floor centre, its size (a prop's with its scale) and yaw.
        bool TargetBox(out string id, out bool prop, out Vector3 centre, out Vector3 size, out float yaw)
        {
            centre = size = default;
            yaw = 0f;
            if ((_mode != Mode.Idle && _mode != Mode.SelectedBox && _mode != Mode.SelectedProp) || !Target(out id, out prop)) { id = null; prop = false; return false; }
            DesignState s = _store.Session.State;
            if (!prop)
            {
                HideBox b = s.hideBoxes[BoxIndex(id)];
                centre = new Vector3(b.position.x, b.position.y, b.position.z); size = new Vector3(b.size.x, b.size.y, b.size.z); yaw = b.yawDegrees;
                return true;
            }
            FreePlacement p = s.coverProps[PropIndex(id)];
            ICatalog catalog = _store.Session.Catalog;
            if (catalog == null || !catalog.TryGet(p.catalogItemId, out CatalogItem item)) return false;
            Vector2 scale = _boxes.ScaleOf(id);
            centre = new Vector3(p.position.x, p.position.y, p.position.z); size = new Vector3(item.size.x * scale.x, item.size.y, item.size.z * scale.y); yaw = p.yawDegrees;
            return true;
        }

        Vector3 TurnCorner(Vector3 centre, Vector3 size, float yaw) => RotateHandle.FrontRightCorner(centre, size, yaw, _room.FloorY + 0.02f, _hands.HeadPosition);

        // The handle shows when the ray comes near the cover's front-right corner.
        void ShowTurnHandle()
        {
            if (_handle == null) return;
            if (TargetBox(out _, out _, out Vector3 centre, out Vector3 size, out float yaw))
            {
                Vector3 corner = TurnCorner(centre, size, yaw);
                if (RotateHandle.Near(_hands.Gaze, corner, RotateHandle.ShowRadius)) { _handle.Show(corner, _hands.HeadPosition, RotateHandle.Near(_hands.Gaze, corner, RotateHandle.HitRadius)); return; }
            }
            if (_handle.Visible) _handle.Hide();
        }

        bool TryBeginTurn(bool isLeft, Ray aim)
        {
            if (_handle == null || !TargetBox(out string id, out bool prop, out Vector3 centre, out Vector3 size, out float yaw)) return false;
            Vector3 corner = TurnCorner(centre, size, yaw);
            if (!RotateHandle.Near(aim, corner, RotateHandle.HitRadius)) return false;
            _turning = true; _turnLeft = isLeft; _turnId = id; _turnProp = prop;
            _turnCentre = centre; _turnSize = size; _turnStartYaw = _turnYaw = yaw;
            _turnFrom = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floor) ? floor : corner;
            InputDebug.Log($"hide: turning {(prop ? "prop" : "box")} {id} from {yaw:0}°");
            return true;
        }

        void TrackTurn(HandState owner)
        {
            if (owner.TrackingLost || !owner.Tracked)
            {
                // Losing the hand cancels the turn: the cover goes back to where it was.
                _turning = false; _handle.Hide(); _boxes.Pose(_turnId, _turnStartYaw);
                Debug.Log($"{LogTag} Hand lost: cover turn cancelled.");
                return;
            }
            if (owner.PinchEnded)
            {
                _turning = false;
                _handle.Hide();
                if (!Mathf.Approximately(_turnYaw, _turnStartYaw) && !CommitTurn()) _boxes.Pose(_turnId, _turnStartYaw);
                return;
            }
            if (XrVisuals.RayFloor(_hands.Aim(_turnLeft), _room.FloorY, out Vector3 now)) _turnYaw = RotateHandle.DragYaw(_turnStartYaw, _turnCentre, _turnFrom, now);
            _boxes.Pose(_turnId, _turnYaw);
            _handle.Show(TurnCorner(_turnCentre, _turnSize, _turnYaw), _hands.HeadPosition, true);
            _handle.ShowAngle(_turnYaw, _hands.HeadPosition);
        }

        // One undo step. The turned footprint must pass the cover rules; a refusal says why and the cover stays as it was.
        bool CommitTurn()
        {
            DesignSession session = _store.Session;
            int i = _turnProp ? PropIndex(_turnId) : BoxIndex(_turnId);
            if (i < 0) return false;
            HideBox turned = _turnProp
                ? CoverAdjusts.Footprint(_turnId, session.State.coverProps[i].position, new Vec3(_turnSize.x, _turnSize.y, _turnSize.z), Vector2.one, _turnYaw)
                : session.State.hideBoxes[i];
            turned.yawDegrees = _turnYaw;
            HideStatus status = session.CheckHideBox(turned);
            FreePlacement prop = default;
            if (status == HideStatus.Ok && _turnProp) { prop = session.State.coverProps[i]; prop.yawDegrees = _turnYaw; status = session.CheckCoverProp(prop); }
            if (status != HideStatus.Ok)
            {
                InputDebug.Log($"hide: turn to {_turnYaw:0}° refused ({status})");
                Refusal?.Invoke(RefusalText.Of(status));
                return false;
            }
            // Core has no "turn this cover": remove it and add it again under the same id, as one user change.
            _replacing = true;
            bool removed = _turnProp ? session.RemoveCoverProp(_turnId) : session.RemoveHideBox(_turnId);
            bool added = removed && (_turnProp ? session.TryAddCoverProp(prop, out _) : session.TryAddHideBox(turned, out _));
            _replacing = false;
            if (removed) _store.NoteUserChange(added ? 2 : 1);
            InputDebug.Log($"hide: turned {(_turnProp ? "prop" : "box")} {_turnId} to {_turnYaw:0}° (saved {added})");
            Changed?.Invoke();
            return added;
        }

        // ---- resize (the Done step's Wider / Narrower / Deeper / Shallower) ----

        bool _replacing;

        // The cover the Done step is about: the one selected in the room, else the one just made.
        bool Target(out string id, out bool prop)
        {
            if (_mode == Mode.SelectedProp || _mode == Mode.SelectedBox) { id = _selected; prop = _mode == Mode.SelectedProp; }
            else { id = _step == TidyStep.Done ? _lastId : null; prop = _lastIsProp; }
            return id != null && (prop ? PropIndex(id) : BoxIndex(id)) >= 0;
        }

        // The cover after one step: its footprint as a box (for core's rules) and, for a prop, its new scale. False at a limit.
        bool Next(AdjustAction action, out string id, out bool prop, out HideBox box, out Vector2 scale)
        {
            box = default;
            scale = Vector2.one;
            if (!CoverAdjusts.IsResize(action) || !Target(out id, out prop)) { id = null; prop = false; return false; }
            DesignState s = _store.Session.State;
            if (!prop)
            {
                box = s.hideBoxes[BoxIndex(id)];
                bool width = CoverAdjusts.IsWidth(action);
                float now = width ? box.size.x : box.size.z, next = CoverAdjusts.NextSide(now, CoverAdjusts.Sign(action));
                if (Mathf.Approximately(now, next)) return false;
                box.size = width ? new Vec3(next, box.size.y, box.size.z) : new Vec3(box.size.x, box.size.y, next);
                return true;
            }
            FreePlacement p = s.coverProps[PropIndex(id)];
            ICatalog catalog = _store.Session.Catalog;
            if (catalog == null || !catalog.TryGet(p.catalogItemId, out CatalogItem item)) return false;
            Vector2 before = _boxes.ScaleOf(id);
            scale = CoverAdjusts.Scaled(before, action);
            if (scale == before) return false;
            box = new HideBox { hideBoxId = id, position = p.position, yawDegrees = p.yawDegrees, finishId = "", size = new Vec3(item.size.x * scale.x, item.size.y, item.size.z * scale.y) };
            return true;
        }

        public bool HasCover => Target(out _, out _);
        public bool CanResize(AdjustAction action) => Next(action, out _, out _, out _, out _);

        /// <summary>
        /// One step of size. The resized footprint must pass the same rules as a drawn box (inside my zone and the room,
        /// never over a window, a door or a desk top); a refusal says why and changes nothing. One undo step each.
        /// </summary>
        public bool Resize(AdjustAction action)
        {
            if (!Next(action, out string id, out bool prop, out HideBox box, out Vector2 scale)) return false;
            DesignSession session = _store.Session;
            HideStatus status = session.CheckHideBox(box);
            if (status != HideStatus.Ok)
            {
                InputDebug.Log($"hide: resize {action} refused ({status})");
                Refusal?.Invoke(RefusalText.Of(status));
                return false;
            }
            if (prop)
            {
                Vector2 before = _boxes.ScaleOf(id);
                string path = _store.SavePath;
                _boxes.SetScale(id, scale);
                _store.NoteUserChange(new CoverScaleStep(v => { if (path == _store.SavePath) { _boxes.SetScale(id, v); Changed?.Invoke(); } }, before, scale));
            }
            else
            {
                // Core has no "change this box": remove it and add the resized one under the same id, as one user change.
                _replacing = true;
                bool removed = session.RemoveHideBox(id), added = removed && session.TryAddHideBox(box, out _);
                _replacing = false;
                if (removed) _store.NoteUserChange(added ? 2 : 1);
            }
            InputDebug.Log($"hide: {action} -> {(prop ? "prop" : "box")} {id}, {box.size.x:F2} x {box.size.z:F2} m");
            Changed?.Invoke();
            return true;
        }

        // Auto-advance (spec): a released box that core accepts is added at once and the tab moves to step 2;
        // a refused one stays drawn in red on step 1 with the reason.
        void AutoAdd()
        {
            if (_store.Session.TryAddHideBox(_pending, out HideStatus status))
            {
                _store.NoteUserChange();
                _boxes.Reveal();
                _selected = _pending.hideBoxId;
                _step = TidyStep.PickLook;
                SetMode(Mode.SelectedBox);
                return;
            }
            SetMode(Mode.Armed);
            _preview.gameObject.SetActive(true); // the refused box stays red until the next draw
            InputDebug.Log($"hide: draw refused ({status}): box {_pending.size.x:F2} x {_pending.size.z:F2} m at {_pending.position.x:F2},{_pending.position.z:F2}");
            Refusal?.Invoke(RefusalText.Of(status));
        }

        void Press(int target, bool isLeft, HandState hand)
        {
            InputDebug.Log($"hide: pinch on target {target}");
            DesignSession session = _store.Session;
            if (target == Orb) { SetMode(_mode == Mode.Armed ? Mode.Idle : Mode.Armed); return; }
            if (target == Done) { SetMode(Mode.Idle); return; }
            if (target == Undo) { _store.Undo(); return; }
            if (target == Height)
            {
                _dragLeft = isLeft;
                _handStart = hand.PinchPoint;
                _startHeight = _pending.size.y;
                SetMode(Mode.Resizing);
                return;
            }
            if (target == Add)
            {
                if (session.TryAddHideBox(_pending, out HideStatus status))
                {
                    _store.NoteUserChange();
                    _boxes.Reveal();
                    Debug.Log($"{LogTag} Hide box {_pending.hideBoxId} added.");
                    _selected = _pending.hideBoxId;
                    SetMode(Mode.SelectedBox);
                }
                else Refuse(_targets[Add], status);
                return;
            }
            if (target == Remove)
            {
                bool removed = _mode == Mode.SelectedProp ? session.RemoveCoverProp(_selected) : session.RemoveHideBox(_selected);
                if (removed) _store.NoteUserChange();
                return;
            }
            if (target < _covers.Count && _mode == Mode.SelectedBox) Cover(_covers[target], _targets[target]);
        }

        /// <summary>Puts a cover prop where the selected hide box stands; on success the prop replaces the box.</summary>
        bool Cover(CatalogItem item, Renderer tile)
        {
            DesignSession session = _store.Session;
            int i = BoxIndex(_selected);
            if (i < 0) return false;
            HideBox box = session.State.hideBoxes[i];
            var prop = new FreePlacement
            {
                placementId = "c-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                catalogItemId = item.id, position = box.position, yawDegrees = box.yawDegrees,
            };
            _lastId = prop.placementId;
            _lastIsProp = true;
            if (!session.TryAddCoverProp(prop, out HideStatus status))
            {
                if (tile != null) Refuse(tile, status);
                Refusal?.Invoke(RefusalText.Of(status));
                return false;
            }
            // Two session commits (core has no combined change), noted as one user change: one Undo reverts both.
            _store.NoteUserChange(session.RemoveHideBox(box.hideBoxId) ? 2 : 1);
            Debug.Log($"{LogTag} {item.id} covers the clutter.");
            SetMode(Mode.Idle);
            return true;
        }

        void BeginDraw(Vector3 hit, bool isLeft, HandState hand)
        {
            _start = hit;
            Vector3 f = Vector3.ProjectOnPlane(_hands.View.direction, Vector3.up); // the box faces the way the user faces
            _yaw = f.sqrMagnitude > 1e-6f ? RoomGeometry.YawOf(f) : 0f;
            _dragLeft = isLeft;
            _dragNear = Vector3.Distance(hand.PinchPoint, hit) < 0.3f;
            _handStart = hand.PinchPoint;
            _pending = new HideBox
            {
                hideBoxId = "h-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                position = new Vec3(hit.x, _room.FloorY, hit.z), size = new Vec3(0f, DefaultHeight, 0f), yawDegrees = _yaw, finishId = "",
            };
            InputDebug.Log($"hide: draw start at {hit} ({(_dragNear ? "hand" : "gaze")})");
            SetMode(Mode.Drawing);
        }

        void Drag(HandState owner)
        {
            if (owner.TrackingLost || !owner.Tracked)
            {
                Debug.Log($"{LogTag} Hand lost: hide box drag cancelled.");
                if (_mode == Mode.Resizing) _pending.size.y = _startHeight;
                SetMode(_mode == Mode.Resizing ? Mode.Pending : Mode.Armed);
                return;
            }
            if (_mode == Mode.Resizing)
            {
                _pending.size.y = Mathf.Clamp(_startHeight + (owner.PinchPoint.y - _handStart.y), 0.05f, HideRules.MaxSide);
            }
            else
            {
                Vector3 corner = _start;
                if (_dragNear) corner = _start + (owner.PinchPoint - _handStart);
                else if (XrVisuals.RayFloor(_hands.Aim(_dragLeft), _room.FloorY, out Vector3 g)) corner = g;
                Span(_start, corner);
            }
            ShowPending();
            if (owner.PinchEnded)
            {
                if (_autoAdd && _mode == Mode.Drawing) AutoAdd();
                else SetMode(Mode.Pending);
            }
        }

        /// <summary>Footprint spanned by two floor corners in the frame turned by the start yaw.</summary>
        void Span(Vector3 a, Vector3 c)
        {
            Quaternion inv = Quaternion.Euler(0f, -_yaw, 0f);
            Vector3 la = inv * a, lc = inv * c;
            Vector3 mid = Quaternion.Euler(0f, _yaw, 0f) * ((la + lc) * 0.5f);
            _pending.position = new Vec3(mid.x, _room.FloorY, mid.z);
            _pending.size = new Vec3(Mathf.Abs(lc.x - la.x), _pending.size.y, Mathf.Abs(lc.z - la.z));
        }

        void ShowPending()
        {
            OrientedBox b = HideBoxes.BoxOf(_pending);
            _preview.gameObject.SetActive(true);
            _preview.transform.SetPositionAndRotation(BoxMath.Center(b), BoxMath.Rotation(b));
            _preview.transform.localScale = new Vector3(Mathf.Max(b.size.x, 0.005f), b.size.y, Mathf.Max(b.size.z, 0.005f));
            HideStatus status = _store.Session.CheckHideBox(_pending); // allocation-free preview check
            if (status == _shownStatus) return;
            _shownStatus = status;
            XrVisuals.Tint(_preview, status == HideStatus.Ok ? XrVisuals.Hover : Refused);
            InputDebug.Log($"hide: preview {status}");
        }

        void Refuse(Renderer shown, HideStatus status)
        {
            XrVisuals.Tint(shown, Refused);
            _reason.Show(RefusalText.Of(status), shown.transform.position + Vector3.up * 0.1f, _hands.HeadPosition, 2f);
            Debug.Log($"{LogTag} Not hidden: {status}.");
        }

        void SetMode(Mode mode)
        {
            _mode = mode;
            Changed?.Invoke();
            if (mode == Mode.Idle || mode == Mode.Armed) _selected = null;
            _shownStatus = (HideStatus)(-1);
            bool pending = mode == Mode.Pending || mode == Mode.Resizing || mode == Mode.Drawing;
            if (pending) ShowPending(); else _preview.gameObject.SetActive(false);
            _targets[Orb].gameObject.SetActive(true);
            XrVisuals.Tint(_targets[Orb], mode == Mode.Armed || mode == Mode.Drawing ? XrVisuals.Active : Color.white);
            XrVisuals.Tint(_targets[Add], XrVisuals.Confirmed);
            Layout();
        }

        void Layout()
        {
            Transform orb = _targets[Orb].transform;
            Vector3 forward = Vector3.ProjectOnPlane(orb.position - _hands.HeadPosition, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (int i = 0; i < Orb; i++)
            {
                bool shown;
                if (_mode == Mode.Pending) shown = i == Add || i == Done || i == Height;
                else if (_mode == Mode.SelectedBox) shown = i < _covers.Count || i == Remove || i == Undo || i == Done;
                else if (_mode == Mode.SelectedProp) shown = i == Remove || i == Undo || i == Done;
                else shown = _mode == Mode.Idle && i == Undo;
                _targets[i].gameObject.SetActive(shown);
                if (!shown) continue;
                if (i == Height)
                {
                    OrientedBox b = HideBoxes.BoxOf(_pending);
                    _targets[i].transform.position = BoxMath.Center(b) + Vector3.up * (b.size.y * 0.5f + 0.06f);
                }
                else _targets[i].transform.position = orb.position + Vector3.up * 0.1f + right * ((i - 3.5f) * 0.075f - 0.1f);
                if (i < _covers.Count) XrVisuals.Tint(_targets[i], RedesignView.ColorOf(_covers[i]));
            }
        }

        int PickTarget(Vector3 pinch)
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

        bool PickHidden(Vector3 pinch, Ray gaze, out string id, out bool isProp)
        {
            id = null;
            isProp = false;
            float bestFar = float.MaxValue;
            DesignState s = _store.Session.State;
            for (int i = 0; i < s.hideBoxes.Count; i++)
            {
                if (Hit(HideBoxes.BoxOf(s.hideBoxes[i]), pinch, gaze, ref bestFar)) { id = s.hideBoxes[i].hideBoxId; isProp = false; }
            }
            ICatalog catalog = _store.Session.Catalog;
            for (int i = 0; catalog != null && i < s.coverProps.Count; i++)
            {
                FreePlacement p = s.coverProps[i];
                if (!catalog.TryGet(p.catalogItemId, out CatalogItem item)) continue;
                Vector2 scale = _boxes.ScaleOf(p.placementId); // the user's resize
                var box = new OrientedBox(new Vec3(p.position.x, p.position.y + item.size.y * 0.5f, p.position.z), new Vec3(item.size.x * scale.x, item.size.y, item.size.z * scale.y), p.yawDegrees);
                if (Hit(box, pinch, gaze, ref bestFar)) { id = p.placementId; isProp = true; }
            }
            if (id != null && bestFar > 0f) _hands.ReportHover(gaze.GetPoint(bestFar), HandRig.BoxHitMiss);
            return id != null;
        }

        static bool Hit(OrientedBox box, Vector3 pinch, Ray gaze, ref float bestFar)
        {
            if (BoxMath.Contains(box, pinch, 0.02f)) { bestFar = -1f; return true; }
            if (bestFar < 0f) return false;
            float far = BoxMath.Raycast(box, gaze);
            if (far < 0f || far >= bestFar) return false;
            bestFar = far;
            return true;
        }

        int BoxIndex(string id)
        {
            var boxes = _store.Session.State.hideBoxes;
            for (int i = 0; id != null && i < boxes.Count; i++) if (boxes[i].hideBoxId == id) return i;
            return -1;
        }

        int PropIndex(string id)
        {
            var props = _store.Session.State.coverProps;
            for (int i = 0; id != null && i < props.Count; i++) if (props[i].placementId == id) return i;
            return -1;
        }
    }
}

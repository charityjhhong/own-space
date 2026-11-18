using System;
using System.Collections.Generic;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// My-zone redesign (CONCEPT 5.2). After the zone is confirmed, every real furniture box in it is
    /// outlined. Pinch a box (or look at it and pinch) to select it; a row of the catalog designs that
    /// fit it (core's fit rule) appears within reach, with Real, Undo and Keep buttons below.
    /// Pinching a design applies it through core's <see cref="DesignSession"/>; designs that would
    /// stand in front of a window or door are refused (the tile flashes red). Undo here only undoes
    /// this mode's own changes, never the zone confirmation. The row follows the view (see
    /// <see cref="FloatingRow"/>); the hovered tile shows the item's name and a refusal shows its
    /// reason for two seconds. No allocation per frame.
    /// </summary>
    public sealed class EditMode : MonoBehaviour, IRedesignTarget
    {
        const string LogTag = "[OwnSpace.MR]";
        static readonly Color Refused = new Color(0.9f, 0.15f, 0.15f);
        const int MaxBoxes = 24, MaxTiles = 6, Real = MaxTiles, Undo = MaxTiles + 1, Keep = MaxTiles + 2, Targets = MaxTiles + 3;

        [SerializeField, Tooltip("Driven by the tool panel: no orb, chips or tiles in the room (the logic and world input stay).")] bool _panelMode;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] Material _material;
        [SerializeField, Tooltip("Content/catalog.json")] TextAsset _catalogJson;
        [SerializeField, Tooltip("content's catalog id -> prefab map; a tinted box is shown when empty.")] CatalogPrefabs _prefabs;
        [SerializeField, Tooltip("Direct pinch reach (m).")] float _nearRadius = 0.06f;

        readonly List<FurnitureAnchor> _inZone = new List<FurnitureAnchor>(MaxBoxes);
        readonly List<LineRenderer> _outlines = new List<LineRenderer>(MaxBoxes);
        readonly List<(CatalogItem item, FitResult fit)> _fits = new List<(CatalogItem, FitResult)>(MaxTiles * 2);
        readonly Renderer[] _targets = new Renderer[Targets];
        ICatalog _catalog = Catalog.Empty;
        RedesignView _view;
        FloatingRow _row;
        WorldLabel _name, _reason;
        FurnitureAnchor _selected;
        int _hoverBox = -1, _hoverTarget = -1;

        bool _active;

        void Awake()
        {
            CatalogLoadResult loaded = CatalogLoader.Load(_catalogJson != null ? _catalogJson.text : "");
            _catalog = loaded.Catalog;
            if (loaded.Errors.Count > 0) Debug.LogWarning($"{LogTag} Catalog: {loaded.Errors.Count} item(s) skipped; first: {loaded.Errors[0]}");
            _zone.Confirmed += Enter;
            _zone.EditStarted += Exit;
        }

        void Start()
        {
            _view = new RedesignView(transform, _material, _prefabs);
            FocusMode.Changed += OnFocus;
            _realTops = new RealTops(transform);
            _occlusion = FindAnyObjectByType<RealOcclusion>(); // once, at start
            if (_occlusion != null) _occlusion.ActiveChanged += OnDepthChanged;
            _row = new FloatingRow(transform, "EditRow", MaxTiles * 0.15f * 0.5f);
            _name = new WorldLabel(transform, "TileName", Color.white);
            _reason = new WorldLabel(transform, "Refusal", new Color(1f, 0.55f, 0.5f), refusal: true);
            Color[] colors = { XrVisuals.Idle, XrVisuals.Active, XrVisuals.Confirmed };
            for (int i = 0; i < Targets; i++)
            {
                Color c = i < MaxTiles ? XrVisuals.Idle : colors[i - MaxTiles];
                _targets[i] = XrVisuals.Make(i < MaxTiles ? PrimitiveType.Cube : PrimitiveType.Sphere, _row.Root, _material, c, "EditTarget");
                _targets[i].transform.localScale = Vector3.one * 0.06f;
                _targets[i].gameObject.SetActive(false);
            }
            _store.Session.Changed += OnDesignChanged;
            _room.Changed += OnRoomChanged;
            if (_zone.Current == ZoneSetup.Phase.Confirmed) Enter();
        }

        void OnDestroy()
        {
            if (_zone != null) { _zone.Confirmed -= Enter; _zone.EditStarted -= Exit; }
            FocusMode.Changed -= OnFocus;
            if (_room != null) _room.Changed -= OnRoomChanged;
            if (_store != null && _store.Session != null) _store.Session.Changed -= OnDesignChanged;
            if (_occlusion != null) _occlusion.ActiveChanged -= OnDepthChanged;
        }

        // The real top of a redesigned desk or bed (RealTops): a flat window, on by default with or without depth.
        RealTops _realTops;
        RealOcclusion _occlusion;
        bool DepthRunning => _occlusion != null && _occlusion.Active;
        void OnDepthChanged() { if (_store != null && _store.Session != null) OnDesignChanged(); }

        public bool RealTopOn => SelectedDesign(out _, out _) && RealTops.Applies(_selected.Role) &&
                                 RealTops.Effective(_adjusts.Get(_selected.Id).realTop, DepthRunning);

        void OnRoomChanged() { if (_active) Enter(); }

        readonly AnchorAdjusts _adjusts = new AnchorAdjusts();

        RedesignView _visitView;
        readonly DesignState _visitState = new DesignState();

        /// <summary>
        /// A visited saved space's furniture (Saved spaces): each piece outside the real zone shown with the design chosen
        /// there, or a default design of its role (core's <see cref="SnapshotRoomInfo.VirtualFurniture"/>). Null clears it.
        /// Pieces with no fitting design are skipped (logged).
        /// </summary>
        public void ShowVisitPieces(IRoomInfo room, List<VirtualPiece> pieces)
        {
            if (_visitView == null) _visitView = new RedesignView(transform, _material, _prefabs);
            _visitState.anchorChoices.Clear();
            int none = 0;
            for (int i = 0; pieces != null && i < pieces.Count; i++)
            {
                VirtualPiece p = pieces[i];
                if (p.Item == null) { none++; continue; }
                _visitState.anchorChoices.Add(new AnchorChoice
                {
                    anchorId = p.Anchor.Id, mode = AnchorMode.Redesign, catalogItemId = p.Item.id,
                    rotated90 = p.Fit.Rotated90, xScale = p.Fit.XScale, yScale = p.Fit.YScale, zScale = p.Fit.ZScale,
                });
            }
            if (room != null) _visitView.Refresh(room, _visitState, _catalog);
            else _visitView.Refresh(_room, _visitState, _catalog); // empty: clears
            Debug.Log($"{LogTag} Visit furniture: {_visitState.anchorChoices.Count} shown, {none} with no fitting design.");
        }

        void OnDesignChanged()
        {
            if (_view == null) return;
            _adjusts.Use(_store.SavePath); // this room's fine-tuning
            _view.Refresh(_room, _store.Session.State, _catalog, _adjusts);
            _realTops?.Refresh(_room, _store.Session.State, _catalog, _adjusts, DepthRunning);
        }

        /// <summary>Fine-tunes the design on the selected box: a quarter turn, ±5 cm width or depth, or back to core's fit.</summary>
        public bool AdjustSelected(AdjustAction action)
        {
            if (!SelectedDesign(out AnchorChoice choice, out CatalogItem item)) return false;
            AnchorAdjust a = _adjusts.Get(_selected.Id), before = a;
            switch (action)
            {
                case AdjustAction.Turn: a = AnchorAdjusts.Turn(a); break;
                case AdjustAction.Wider: a = AnchorAdjusts.Resize(a, item, choice, true, 1); break;
                case AdjustAction.Narrower: a = AnchorAdjusts.Resize(a, item, choice, true, -1); break;
                case AdjustAction.Deeper: a = AnchorAdjusts.Resize(a, item, choice, false, 1); break;
                case AdjustAction.Shallower: a = AnchorAdjusts.Resize(a, item, choice, false, -1); break;
                case AdjustAction.RealTop:
                    if (!RealTops.Applies(_selected.Role)) return false;
                    a.realTop = RealTops.Toggled(a.realTop, DepthRunning);
                    break;
                default: a = new AnchorAdjust { anchorId = _selected.Id, realTop = a.realTop }; break; // Reset is about the fit
            }
            if (a.Equals(before)) return false; // at a limit
            _adjusts.Set(a);
            NoteAdjust(before, a);
            OnDesignChanged();
            InputDebug.Log($"edit: adjust {action} -> turns {a.turns}, offset {a.dx:F2},{a.dz:F2}, +w {a.dw:F2}, +d {a.dd:F2}");
            return true;
        }

        /// <summary>False when that resize is at its limit (the panel greys the button); other actions are always allowed.</summary>
        public bool CanAdjust(AdjustAction action)
        {
            if (!SelectedDesign(out AnchorChoice choice, out CatalogItem item)) return false;
            AnchorAdjust a = _adjusts.Get(_selected.Id);
            switch (action)
            {
                case AdjustAction.Wider: return AnchorAdjusts.CanResize(a, item, choice, true, 1);
                case AdjustAction.Narrower: return AnchorAdjusts.CanResize(a, item, choice, true, -1);
                case AdjustAction.Deeper: return AnchorAdjusts.CanResize(a, item, choice, false, 1);
                case AdjustAction.Shallower: return AnchorAdjusts.CanResize(a, item, choice, false, -1);
                case AdjustAction.RealTop: return RealTops.Applies(_selected.Role);
                default: return true;
            }
        }

        // ---- undo: each adjust (a press, a turn, a drag) is one step in the panel's undo history (device: undo did nothing) ----

        void NoteAdjust(AnchorAdjust before, AnchorAdjust after) => _store.NoteUserChange(new AdjustStep(this, _store.SavePath, before, after));

        // Back to an adjustment, in the room it was made in (a room change clears the history anyway).
        void ApplyAdjust(string path, AnchorAdjust a)
        {
            if (path != _store.SavePath) return;
            _adjusts.Set(a);
            OnDesignChanged();
            InputDebug.Log($"edit: adjust undo/redo -> {a.anchorId} turns {a.turns}, offset {a.dx:F2},{a.dz:F2}, +w {a.dw:F2}, +d {a.dd:F2}");
        }

        sealed class AdjustStep : IUndoStep
        {
            readonly EditMode _owner;
            readonly string _path;
            readonly AnchorAdjust _before, _after;

            public AdjustStep(EditMode owner, string path, AnchorAdjust before, AnchorAdjust after) { _owner = owner; _path = path; _before = before; _after = after; }
            public void Undo() => _owner.ApplyAdjust(_path, _before);
            public void Redo() => _owner.ApplyAdjust(_path, _after);
        }

        bool SelectedDesign(out AnchorChoice choice, out CatalogItem item)
        {
            item = null;
            choice = _selected != null ? _store.Session.State.ChoiceFor(_selected.Id) : default;
            return _selected != null && choice.mode == AnchorMode.Redesign && _catalog != null && _catalog.TryGet(choice.catalogItemId, out item);
        }

        // ---- nudge: pinch and hold the redesigned piece, drag it on the floor (damped like placed pieces) ----
        // ---- turn: the corner handle on the selected, redesigned piece (device batch 10: no Turn button) ----

        RotateHandle _handle;
        bool _turning, _turnLeft;
        Vector3 _turnFrom, _turnCentre;
        AnchorAdjust _turnStart, _turnNow;

        /// <summary>Builds the corner handle in the panel's look (PanelHost calls it once).</summary>
        public void UseHandle(OwnSpace.UI.ToolPanel panel) { if (_handle == null && panel != null) _handle = new RotateHandle(transform, panel, _material); }

        bool TurnCorner(AnchorAdjust a, out Vector3 corner, out Vector3 centre)
        {
            corner = centre = default;
            if (_handle == null || !SelectedDesign(out AnchorChoice choice, out _)) return false;
            OrientedBox box = _selected.Box;
            centre = new Vector3(box.center.x + a.dx, _room.FloorY, box.center.z + a.dz);
            float yaw = box.yawDegrees + (choice.rotated90 ? 90f : 0f) + a.turns * 90f + a.yaw;
            corner = RotateHandle.FrontRightCorner(centre, new Vector3(box.size.x, box.size.y, box.size.z), yaw, _room.FloorY + 0.02f, _hands.HeadPosition);
            return true;
        }

        void ShowTurnHandle()
        {
            if (_handle == null) return;
            if (TurnCorner(_adjusts.Get(_selected?.Id), out Vector3 corner, out _) && RotateHandle.Near(_hands.Gaze, corner, RotateHandle.ShowRadius))
                _handle.Show(corner, _hands.HeadPosition, RotateHandle.Near(_hands.Gaze, corner, RotateHandle.HitRadius));
            else _handle.Hide();
        }

        bool TryBeginTurn(bool isLeft)
        {
            AnchorAdjust a = _adjusts.Get(_selected?.Id);
            Ray aim = _hands.Aim(isLeft);
            if (!TurnCorner(a, out Vector3 corner, out _turnCentre) || !RotateHandle.Near(aim, corner, RotateHandle.HitRadius)) return false;
            _turning = true;
            _turnLeft = isLeft;
            _turnStart = _turnNow = a;
            _turnFrom = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floor) ? floor : corner;
            InputDebug.Log($"edit: turning {_selected.Id} from {a.yaw:0}°");
            return true;
        }

        void TrackTurn(HandState owner)
        {
            bool lost = owner.TrackingLost || !owner.Tracked;
            if (lost || owner.PinchEnded)
            {
                _turning = false;
                _handle.Hide();
                if (!lost && !_turnNow.Equals(_turnStart)) { _adjusts.Set(_turnNow); NoteAdjust(_turnStart, _turnNow); } // one step per turn
                InputDebug.Log(lost ? "edit: hand lost, turn cancelled" : $"edit: turned to {_turnNow.yaw:0}°");
                OnDesignChanged(); // saved, or back as it was
                return;
            }
            if (XrVisuals.RayFloor(_hands.Aim(_turnLeft), _room.FloorY, out Vector3 now))
                _turnNow.yaw = Mathf.Repeat(RotateHandle.DragYaw(_turnStart.yaw, _turnCentre, _turnFrom, now), 360f);
            if (SelectedDesign(out AnchorChoice choice, out CatalogItem item)) _view.Pose(_selected.Id, _selected, item, choice, _room.FloorY, _turnNow);
            if (TurnCorner(_turnNow, out Vector3 corner, out _)) { _handle.Show(corner, _hands.HeadPosition, true); _handle.ShowAngle(_turnNow.yaw, _hands.HeadPosition); }
        }

        bool _nudging, _nudgeLeft;
        Vector3 _nudgeFrom, _nudgePos, _nudgeVelocity;
        AnchorAdjust _nudgeStart;

        void BeginNudge(bool isLeft)
        {
            if (!XrVisuals.RayFloor(_hands.Aim(isLeft), _room.FloorY, out _nudgeFrom)) return;
            _nudging = true;
            _nudgeLeft = isLeft;
            _nudgeStart = _adjusts.Get(_selected.Id);
            _nudgePos = new Vector3(_nudgeStart.dx, 0f, _nudgeStart.dz);
            _nudgeVelocity = Vector3.zero;
        }

        void TrackNudge(HandState owner)
        {
            bool done = owner.PinchEnded || owner.TrackingLost || !owner.Tracked;
            if (!done && XrVisuals.RayFloor(_hands.Aim(_nudgeLeft), _room.FloorY, out Vector3 now))
            {
                var target = new Vector3(_nudgeStart.dx + now.x - _nudgeFrom.x, 0f, _nudgeStart.dz + now.z - _nudgeFrom.z);
                _nudgePos = OutsidePlacer.SmoothFollow(_nudgePos, target, ref _nudgeVelocity, Time.deltaTime);
            }
            AnchorAdjust a = AnchorAdjusts.Nudge(_nudgeStart, _nudgePos.x, _nudgePos.z);
            if (SelectedDesign(out AnchorChoice choice, out CatalogItem item)) _view.Pose(_selected.Id, _selected, item, choice, _room.FloorY, a);
            if (!done) return;
            _nudging = false;
            if (owner.TrackingLost || !owner.Tracked) { OnDesignChanged(); return; } // lost: back where it was
            _adjusts.Set(a);
            if (!a.Equals(_nudgeStart)) NoteAdjust(_nudgeStart, a); // one step per drag
            OnDesignChanged();
        }

        /// <summary>Outlines the furniture in my zone and starts listening for selection.</summary>
        void Enter()
        {
            if (_view == null) return; // Start() calls again once ready.
            // A room reload re-enters while active: keep the user's undo history. Only a fresh entry
            // (first confirm, or after re-editing the zone) starts a new barrier.
            if (!_active) _store.BeginUserChanges();
            _active = true;
            Select(null);
            _inZone.Clear();
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count && _inZone.Count < MaxBoxes; i++)
            {
                if (_store.Session.IsInMyZone(furniture[i])) _inZone.Add(furniture[i]);
            }
            while (_outlines.Count < _inZone.Count) _outlines.Add(NewOutline());
            for (int i = 0; i < _outlines.Count; i++)
            {
                bool shown = i < _inZone.Count;
                _outlines[i].gameObject.SetActive(shown && _outlinesShown && !FocusMode.Active);
                if (shown) BoxMath.WriteOutline(_inZone[i].Box, _outlines[i]);
            }
            _hoverBox = -1;
            Tint();
            _view.SetVisible(true);
            _realTops?.SetVisible(true);
            OnDesignChanged();
            Debug.Log($"{LogTag} Edit mode: {_inZone.Count} furniture box(es) in my zone.");
        }

        /// <summary>Hides outlines and the row while the zone is being edited.</summary>
        void Exit()
        {
            _turning = false;
            _handle?.Hide();
            _active = false;
            Select(null);
            for (int i = 0; i < _outlines.Count; i++) _outlines[i].gameObject.SetActive(false);
            _view?.SetVisible(false); // the real room while the zone is edited
            _realTops?.SetVisible(false);
        }

        LineRenderer NewOutline()
        {
            var go = new GameObject("BoxOutline");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.widthMultiplier = 0.012f;
            line.positionCount = BoxMath.OutlinePointCount;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        // Focus: outlines, the turn handle and labels hide (the designs stay); off: the outlines come back as they were.
        void OnFocus(bool on)
        {
            for (int i = 0; i < _outlines.Count; i++) _outlines[i].gameObject.SetActive(!on && _outlinesShown && _active && i < _inZone.Count);
            if (!on) return;
            _handle?.Hide();
            _name?.Hide();
            _reason?.Hide();
        }

        void Update()
        {
            if (_reason != null) _reason.Tick();
            if (FocusMode.Active) return; // no picking, hover names or handles while focusing
            if (!_active || _zone.Current != ZoneSetup.Phase.Confirmed) return;
            if (_selected != null) _row.Follow(_hands.View, _room.FloorOutline);
            HandState left = _hands.Left, right = _hands.Right;
            if (_nudging) { TrackNudge(_nudgeLeft ? left : right); return; }
            if (_turning) { TrackTurn(_turnLeft ? left : right); return; }
            ShowTurnHandle();
            bool rightFirst = right.Tracked || !left.Tracked;
            Vector3 pinch = rightFirst ? right.PinchPoint : left.PinchPoint;
            bool tracked = left.Tracked || right.Tracked;

            int target = _selected != null ? PickTarget(pinch, tracked) : -1;
            int box = target < 0 ? PickBox(pinch, tracked, _hands.Gaze) : -1;
            if (target != _hoverTarget || box != _hoverBox) { _hoverTarget = target; _hoverBox = box; Tint(); }
            ShowName();

            bool started = right.PinchStarted || left.PinchStarted;
            if (started) InputDebug.Log($"edit mode pinch: claimedThisFrame={_hands.ClaimedThisFrame} frame={Time.frameCount}");
            if (!TakesPinch(started, _hands.ClaimedThisFrame)) return;
            if (TryBeginTurn(!right.PinchStarted)) { _hands.Claim(); return; } // the corner handle before the box under it
            if (right.PinchStarted != left.PinchStarted)
            {
                // Re-pick with the hand that pinched, so near targets follow that hand.
                pinch = right.PinchStarted ? right.PinchPoint : left.PinchPoint;
                target = _selected != null ? PickTarget(pinch, true) : -1;
                box = target < 0 ? PickBox(pinch, true, _hands.Aim(!right.PinchStarted)) : -1;
            }
            if (target < 0 && box < 0) return;
            _hands.Claim();
            if (target >= 0) Press(target);
            else if (_inZone[box] == _selected && SelectedDesign(out _, out _)) BeginNudge(!right.PinchStarted); // the selected, redesigned piece: drag to nudge
            else Select(_inZone[box]);
        }

        void Press(int target)
        {
            DesignSession session = _store.Session;
            if (target == Keep) { Select(null); return; }
            if (target == Undo)
            {
                _store.Undo();
                return;
            }
            if (target == Real) { if (session.KeepReal(_selected.Id)) _store.NoteUserChange(); return; }

            if (!ApplyDesign(target, out string reason)) Refuse(target, reason);
        }

        /// <summary>
        /// Whether a pinch reaches the room (pure, for tests): never when an earlier consumer (the tool panel,
        /// which runs first) already claimed it, so a click on the panel cannot select the box behind it.
        /// </summary>
        public static bool TakesPinch(bool pinchStarted, bool claimedThisFrame) => pinchStarted && !claimedThisFrame;

        /// <summary>Keeps the selected real piece as it is (the Furniture tab's "Keep the real one"); one undo step.</summary>
        /// <summary>Leaves the selected box: the panel's Furniture tab goes back to placing outside.</summary>
        public void Deselect() => Select(null);

        public bool KeepReal()
        {
            if (_selected == null || !_store.Session.KeepReal(_selected.Id)) return false;
            _store.NoteUserChange();
            return true;
        }

        /// <summary>The real box selected in my zone, or null.</summary>
        public FurnitureAnchor Selected => _selected;

        /// <summary>The catalog designs that fit the selected box (core's fit rule).</summary>
        public IReadOnlyList<(CatalogItem item, FitResult fit)> Fits => _fits;

        public string CurrentDesignId
        {
            get
            {
                if (_selected == null) return null;
                AnchorChoice choice = _store.Session.State.ChoiceFor(_selected.Id);
                return choice.mode == AnchorMode.Redesign ? choice.catalogItemId : null;
            }
        }

        /// <summary>Raised when the selected box changes (the panel's Furniture tab follows it).</summary>
        public event Action SelectionChanged;

        /// <summary>Applies design <paramref name="index"/> of <see cref="Fits"/> through core; false with a short reason when refused.</summary>
        public bool ApplyDesign(int index, out string reason)
        {
            reason = null;
            if (_selected == null || index < 0 || index >= _fits.Count) return false;
            CatalogItem item = _fits[index].item;
            OrientedBox placed = OpeningGuard.Placed(_selected, item, _fits[index].fit, _room.FloorY);
            if (OpeningGuard.Covers(_room, placed))
            {
                Debug.Log($"{LogTag} {item.id} would cover a window or door; not placed.");
                reason = RefusalText.CoversOpening;
                return false;
            }
            RedesignOutcome outcome = _store.Session.Redesign(_selected.Id, item, out FitResult fit);
            Debug.Log($"{LogTag} {item.id} on {_selected.Id}: {outcome} (rotated90={fit.Rotated90}, yScale={fit.YScale:0.00})");
            if (outcome != RedesignOutcome.Applied) { reason = RefusalText.Of(outcome, fit.Status); return false; }
            _store.NoteUserChange();
            return true;
        }

        void Refuse(int target, string reason)
        {
            XrVisuals.Tint(_targets[target], Refused);
            _reason.Show(reason, _targets[target].transform.position + Vector3.up * 0.12f, _hands.HeadPosition, 2f);
        }

        void ShowName()
        {
            if (_hoverTarget < 0 || _hoverTarget >= MaxTiles || _hoverTarget >= _fits.Count) { _name.Hide(); return; }
            CatalogItem item = _fits[_hoverTarget].item;
            _name.Show(string.IsNullOrEmpty(item.name) ? item.id : item.name,
                _targets[_hoverTarget].transform.position + Vector3.down * 0.07f, _hands.HeadPosition);
        }

        /// <summary>
        /// Selects the real piece in my zone nearest the user, desks first (the My furniture tab opens on it). False
        /// when my zone has no real piece.
        /// </summary>
        public bool SelectNearest()
        {
            FurnitureAnchor nearest = Nearest(_inZone, _hands.HeadPosition);
            if (nearest == null) return false;
            Select(nearest);
            return true;
        }

        /// <summary>The piece to restyle first (pure, for tests): the nearest table, else the nearest piece, else null.</summary>
        public static FurnitureAnchor Nearest(IReadOnlyList<FurnitureAnchor> inZone, Vector3 head)
        {
            FurnitureAnchor best = null;
            float bestD = float.MaxValue;
            bool bestTable = false;
            for (int i = 0; inZone != null && i < inZone.Count; i++)
            {
                Vec3 c = inZone[i].Box.center;
                float d = (c.x - head.x) * (c.x - head.x) + (c.z - head.z) * (c.z - head.z);
                bool table = inZone[i].Role == FurnitureRole.Table;
                if ((table && !bestTable) || (table == bestTable && d < bestD)) { best = inZone[i]; bestD = d; bestTable = table; }
            }
            return best;
        }

        void Select(FurnitureAnchor anchor)
        {
            if (_row == null) return; // before Start
            _selected = anchor;
            _fits.Clear();
            if (anchor != null) CatalogQueries.FittingItems(_catalog, anchor, _fits);
            int tiles = Mathf.Min(_fits.Count, MaxTiles);

            // The row sits within reach in front of the user, inside the room, and follows the view.
            _row.Place(_hands.View, _room.FloorOutline);
            _name.Hide();
            for (int i = 0; i < Targets; i++)
            {
                bool tile = i < MaxTiles;
                bool shown = anchor != null && !_panelMode && (!tile || i < tiles); // in panel mode the Furniture tab has all of it
                _targets[i].gameObject.SetActive(shown);
                if (!shown) continue;
                float x = tile ? (i - (tiles - 1) * 0.5f) * 0.15f : (i - Undo) * 0.15f;
                _targets[i].transform.localPosition = new Vector3(x, tile ? 0f : -0.13f, 0f);
                if (tile) ShapeTile(_targets[i], _fits[i].item);
            }
            if (anchor != null) Debug.Log($"{LogTag} Selected {anchor.Id} ({anchor.Role}): {_fits.Count} design(s) fit.");
            _hoverTarget = -1;
            Tint();
            SelectionChanged?.Invoke();
        }

        static void ShapeTile(Renderer tile, CatalogItem item)
        {
            // A miniature of the item: largest side 0.1 m, colored by its primary color.
            float largest = Mathf.Max(item.size.x, Mathf.Max(item.size.y, item.size.z));
            float k = largest > 0f ? 0.1f / largest : 0.1f;
            tile.transform.localScale = new Vector3(item.size.x * k, item.size.y * k, item.size.z * k);
            tile.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // front faces the user
            XrVisuals.Tint(tile, RedesignView.ColorOf(item));
        }

        int PickTarget(Vector3 pinch, bool usePinch)
        {
            int best = -1;
            float bestMiss = float.MaxValue;
            for (int i = 0; i < Targets; i++)
            {
                if (!_targets[i].gameObject.activeSelf) continue;
                Vector3 c = _targets[i].transform.position;
                if (usePinch && Vector3.Distance(pinch, c) < _nearRadius) return i;
                float miss = _hands.GazeHit(c);
                if (miss >= 0f && miss < bestMiss) { bestMiss = miss; best = i; }
            }
            return best;
        }

        /// <summary>Box under the pinch point (near) or along <paramref name="gaze"/> (far: the pinching hand's ray).</summary>
        int PickBox(Vector3 pinch, bool usePinch, Ray gaze)
        {
            int best = -1;
            float bestFar = float.MaxValue;
            for (int i = 0; i < _inZone.Count; i++)
            {
                if (usePinch && BoxMath.Contains(_inZone[i].Box, pinch, 0.03f)) return i;
                float far = BoxMath.Raycast(_inZone[i].Box, gaze);
                if (far >= 0f && far < bestFar) { bestFar = far; best = i; }
            }
            if (best >= 0) _hands.ReportHover(gaze.GetPoint(bestFar), HandRig.BoxHitMiss);
            return best;
        }

        bool _outlinesShown = true;

        /// <summary>
        /// The in-zone box outlines (the selected piece's orange one included) show on My room only (device shots: they
        /// stayed on Tidy up and AI and showed through the panel). PanelHost sets it on every tab change.
        /// </summary>
        public bool OutlinesShown
        {
            get => _outlinesShown;
            set
            {
                if (_outlinesShown == value) return;
                _outlinesShown = value;
                for (int i = 0; i < _outlines.Count; i++) _outlines[i].gameObject.SetActive(value && _active && !FocusMode.Active && i < _inZone.Count);
            }
        }

        void Tint()
        {
            for (int i = 0; i < _inZone.Count; i++)
            {
                Color c = _inZone[i] == _selected ? XrVisuals.Active : i == _hoverBox ? XrVisuals.Hover : XrVisuals.Idle;
                XrVisuals.Tint(_outlines[i], c);
            }
            for (int i = MaxTiles; i < Targets; i++)
            {
                _targets[i].transform.localScale = Vector3.one * (i == _hoverTarget ? 0.08f : 0.06f);
            }
        }
    }
}

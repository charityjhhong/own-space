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
    /// Free placement in the outside zone (CONCEPT 5.3), hands only:
    /// look at the outside floor and pinch → a row of pieces allowed outside appears within reach;
    /// pinch one to add it there, facing you. Pinch a placed piece (near, or by gaze) and hold to drag
    /// it on the floor (red while the pose breaks a rule); release to commit through core's
    /// <see cref="DesignSession.TryMovePlacement"/>, or it snaps back. Rotate 45°, Remove, Undo, Done.
    /// Refusals turn the tile or piece red and show core's reason for two seconds. The row follows
    /// the view (<see cref="FloatingRow"/>) and names the hovered tile. Tracking loss cancels a drag.
    /// Runs after <see cref="EditMode"/> and never acts on a pinch it already claimed.
    /// </summary>
    // -5: after the tool panel (-10), before EditMode (0) and HideTool (6), so an armed card owns the outside floor
    // pinch (device batch 5: nothing placed; earlier tools saw the pinch first).
    [DefaultExecutionOrder(-5)]
    public sealed class OutsidePlacer : MonoBehaviour, IOutsideArm
    {
        const string LogTag = "[OwnSpace.MR]";
        const int MaxTiles = 8, Rotate = MaxTiles, Remove = MaxTiles + 1, Undo = MaxTiles + 2, Done = MaxTiles + 3, Targets = MaxTiles + 4;
        static readonly Color Refused = new Color(0.9f, 0.15f, 0.15f);

        [SerializeField, Tooltip("Driven by the tool panel: no orb, chips or tiles in the room (the logic and world input stay).")] bool _panelMode;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] Material _material;
        [SerializeField] CatalogPrefabs _prefabs;
        [SerializeField, Tooltip("Direct pinch reach (m).")] float _nearRadius = 0.06f;

        readonly List<CatalogItem> _outsideItems = new List<CatalogItem>(MaxTiles);
        readonly Renderer[] _targets = new Renderer[Targets];
        PlacementView _view;
        FloatingRow _row;
        WorldLabel _name, _reason;
        Renderer _marker;
        Vector3 _addPoint;
        string _selected;        // placement id, or null in add mode
        bool _rowOpen, _dragging, _dragLeft, _dragFar, _previewRefused;
        Vector3 _dragOffset, _dragPos;
        float _dragYaw;

        void Start()
        {
            _view = new PlacementView(transform, _material, _prefabs);
            _row = new FloatingRow(transform, "PlaceRow", MaxTiles * 0.13f * 0.5f);
            _name = new WorldLabel(transform, "TileName", Color.white);
            _reason = new WorldLabel(transform, "Refusal", new Color(1f, 0.55f, 0.5f), refusal: true);
            Color[] buttons = { XrVisuals.Hover, XrVisuals.Active, new Color(0.6f, 0.6f, 0.6f), XrVisuals.Confirmed };
            for (int i = 0; i < Targets; i++)
            {
                _targets[i] = XrVisuals.Make(i < MaxTiles ? PrimitiveType.Cube : PrimitiveType.Sphere, _row.Root, _material,
                    i < MaxTiles ? XrVisuals.Idle : buttons[i - MaxTiles], "PlaceTarget");
                _targets[i].transform.localScale = Vector3.one * 0.06f;
                _targets[i].gameObject.SetActive(false);
            }
            var outlineGo = new GameObject("PieceOutline");
            outlineGo.transform.SetParent(transform, false);
            _outline = outlineGo.AddComponent<LineRenderer>();
            _outline.sharedMaterial = _material;
            _outline.widthMultiplier = 0.01f;
            _outline.positionCount = BoxMath.OutlinePointCount;
            _outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _outline.enabled = false;
            _marker = XrVisuals.Make(PrimitiveType.Cylinder, transform, _material, XrVisuals.Hover, "PlaceMarker");
            _marker.transform.localScale = new Vector3(0.25f, 0.003f, 0.25f);
            _marker.gameObject.SetActive(false);

            ICatalog catalog = _store.Session.Catalog;
            for (int i = 0; catalog != null && i < catalog.Items.Count && _outsideItems.Count < MaxTiles; i++)
            {
                if (catalog.Items[i].AllowedIn("outside")) _outsideItems.Add(catalog.Items[i]);
            }
            _store.Session.Changed += OnDesignChanged;
            _zone.EditStarted += CloseRow;
            OnDesignChanged();
        }

        void OnDestroy()
        {
            if (_store != null && _store.Session != null) _store.Session.Changed -= OnDesignChanged;
            if (_zone != null) _zone.EditStarted -= CloseRow;
        }

        void OnDesignChanged()
        {
            _view.Refresh(_store.Session.State, _store.Session.Catalog);
            if (_selected != null && IndexOf(_selected) < 0) CloseRow(); // removed or undone
        }

        /// <summary>The piece picked on the panel's Furniture tab; the next pinch on the outside floor places it there.</summary>
        public CatalogItem Armed { get; private set; }

        /// <summary>Raised with a short reason when a placement is refused (for the panel's toast).</summary>
        public event Action<string> Refusal;

        /// <summary>Raised when <see cref="Armed"/> changes (the Furniture tab shows the armed card).</summary>
        public event Action ArmedChanged;

        public void Arm(CatalogItem item)
        {
            CatalogItem armed = item != null && item.AllowedIn("outside") ? item : null;
            if (armed == Armed) return;
            Armed = armed;
            HideSuggestion();
            if (Armed != null && Ready) Suggest(AimedFloor(), "picked");
            ArmedChanged?.Invoke();
        }

        /// <summary>Drops the armed card: on tracking loss, pause, leaving the Furniture tab or minimizing the panel.</summary>
        public void Disarm() => Arm(null);

        void OnApplicationPause(bool paused) => Pause(paused);
        internal void Pause(bool paused) { if (paused) Disarm(); }

        public enum PinchAction { None, Drag, Place, OpenRow }

        /// <summary>
        /// What a pinch does (pure, for tests): a placed piece under it is dragged, even while a card is armed
        /// (the card stays armed); else an armed card is placed on the outside floor; else the floor opens the row.
        /// </summary>
        public static PinchAction Decide(bool onPiece, bool armed, bool onOutsideFloor) =>
            armed && onOutsideFloor ? PinchAction.Place // an armed card places, even over a rug or a piece in the way
            : onPiece ? PinchAction.Drag : !onOutsideFloor ? PinchAction.None : armed ? PinchAction.Place : PinchAction.OpenRow;

        /// <summary>How a placed piece looks (pure, for tests): red while a drag breaks a rule, accent while selected or dragged, a light accent on hover.</summary>
        public enum PieceLook { Plain, Hover, Selected, Refused }

        public static PieceLook LookOf(bool hovered, bool selected, bool dragging, bool refused) =>
            dragging && refused ? PieceLook.Refused : selected || dragging ? PieceLook.Selected : hovered ? PieceLook.Hover : PieceLook.Plain;

        // ---- the panel's selection (no floating row in the room: device batch 7, "what are these balls?") ----

        /// <summary>Raised when a placed piece is selected or let go (the Add tab shows its card).</summary>
        public event Action SelectionChanged;

        public string SelectedName
        {
            get
            {
                int i = IndexOf(_selected);
                if (i < 0) return null;
                string id = _store.Session.State.placements[i].catalogItemId;
                return _store.Session.Catalog != null && _store.Session.Catalog.TryGet(id, out CatalogItem item) && !string.IsNullOrEmpty(item.name) ? item.name : id;
            }
        }

        void Select(string placementId)
        {
            if (placementId == _selected) return;
            _selected = placementId;
            SelectionChanged?.Invoke();
        }

        public void Deselect() => Select(null);

        public bool RemoveSelected()
        {
            int i = IndexOf(_selected);
            if (i < 0 || !_store.Session.RemovePlacement(_store.Session.State.placements[i].placementId)) return false;
            _store.NoteUserChange();
            Select(null);
            return true;
        }

        LineRenderer _outline;
        GlassChip _chip; // the hovered piece's name in the panel's look (the TextMesh label is the fallback)

        /// <summary>Names hovered pieces with a glass chip in the panel's look (PanelHost calls it once).</summary>
        public void UseChip(OwnSpace.UI.ToolPanel panel)
        {
            if (_chip != null || panel == null) return;
            _chip = new GlassChip(transform, panel, "PieceName");
            _handle = new RotateHandle(transform, panel, _material); // turning is on the piece (device batch 10)
        }

        // ---- turn: the round handle at the piece's front-right bottom corner ----

        RotateHandle _handle;
        bool _rotating, _rotLeft;
        Vector3 _rotFrom;
        float _rotStartYaw;

        bool HandleCorner(int i, out Vector3 corner)
        {
            corner = default;
            if (i < 0) return false;
            FreePlacement p = _store.Session.State.placements[i];
            if (_store.Session.Catalog == null || !_store.Session.Catalog.TryGet(p.catalogItemId, out CatalogItem item)) return false;
            if (PlacementRules.IsWallMounted(item)) return false; // a wall piece faces out of its wall: no turn handle
            bool live = (_dragging || _rotating) && i == IndexOf(_selected);
            Vector3 at = live ? _dragPos : new Vector3(p.position.x, p.position.y, p.position.z);
            corner = RotateHandle.FrontRightCorner(at, new Vector3(item.size.x, item.size.y, item.size.z), live ? _dragYaw : p.yawDegrees, _room.FloorY + 0.02f, _hands.HeadPosition);
            return true;
        }

        // The selected piece, else the hovered one: the handle shows when the ray comes near its corner.
        int HandlePiece() { int s = IndexOf(_selected); return s >= 0 ? s : _hovered; }

        void ShowHandle(int piece)
        {
            if (_handle == null) return;
            if (HandleCorner(piece, out Vector3 corner) && RotateHandle.Near(_hands.Gaze, corner, RotateHandle.ShowRadius))
                _handle.Show(corner, _hands.HeadPosition, RotateHandle.Near(_hands.Gaze, corner, RotateHandle.HitRadius));
            else _handle.Hide();
        }

        bool TryBeginRotate(int piece, bool isLeft, Ray aim)
        {
            if (_handle == null || !HandleCorner(piece, out Vector3 corner) || !RotateHandle.Near(aim, corner, RotateHandle.HitRadius)) return false;
            FreePlacement p = _store.Session.State.placements[piece];
            OpenRow(p.placementId, new Vector3(p.position.x, p.position.y, p.position.z));
            _rotating = true;
            _rotLeft = isLeft;
            _dragPos = new Vector3(p.position.x, p.position.y, p.position.z);
            _rotStartYaw = _dragYaw = p.yawDegrees;
            _rotFrom = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floor) ? floor : corner;
            _previewRefused = false;
            InputDebug.Log($"place: turning {p.catalogItemId} from {p.yawDegrees:0}°");
            return true;
        }

        void TrackRotate(HandState owner)
        {
            if (owner.TrackingLost || !owner.Tracked) { _rotating = false; _handle.Hide(); Debug.Log($"{LogTag} Hand lost: turn cancelled."); _view.Refresh(_store.Session.State, _store.Session.Catalog); return; }
            if (owner.PinchEnded)
            {
                _rotating = false;
                _handle.Hide();
                InputDebug.Log($"place: turned to {_dragYaw:0}°");
                if (_dragYaw == _rotStartYaw) return;
                if (_store.Session.TryMovePlacement(_selected, new Vec3(_dragPos.x, _dragPos.y, _dragPos.z), _dragYaw, out PlacementStatus status)) { _store.NoteUserChange(); return; }
                Refuse(_dragPos + Vector3.up * 0.6f, _selected, status);
                _view.Refresh(_store.Session.State, _store.Session.Catalog); // back to where it was
                return;
            }
            if (XrVisuals.RayFloor(_hands.Aim(_rotLeft), _room.FloorY, out Vector3 now)) _dragYaw = RotateHandle.DragYaw(_rotStartYaw, _dragPos, _rotFrom, now);
            _view.Pose(_selected, _dragPos, _dragYaw);
            int i = IndexOf(_selected);
            if (i < 0) return;
            FreePlacement preview = _store.Session.State.placements[i];
            preview.yawDegrees = _dragYaw;
            bool refused = _store.Session.CheckPlacement(preview, _selected) != PlacementStatus.Ok;
            if (refused != _previewRefused) { _previewRefused = refused; _view.SetRefused(_selected, refused); }
            if (HandleCorner(i, out Vector3 corner)) { _handle.Show(corner, _hands.HeadPosition, true); _handle.ShowAngle(_dragYaw, _hands.HeadPosition); }
        }

        void ShowName(string text, Vector3 at) { if (_chip != null) _chip.Show(text, at, _hands.HeadPosition); else _name.Show(text, at, _hands.HeadPosition); }
        void HideName() { if (_chip != null) _chip.Hide(); else _name.Hide(); }

        bool _placing, _placeLeft, _placeValid; // a card dragged off the panel: its ghost follows the ray on the floor
        Vector3 _placeSpot;
        string _placeWhy;

        /// <summary>
        /// Starts placing the armed piece by dragging (the Add tab: a card pinched and dragged off the panel). An outline
        /// ghost of the piece follows the ray's point on the outside floor, accent where it fits and red where it does
        /// not; releasing places it there (or says why not and stays armed, so tap-then-pinch still works).
        /// </summary>
        public void BeginDragPlace(bool isLeft)
        {
            if (Armed == null) return;
            _placing = true;
            _placeLeft = isLeft;
            InputDebug.Log($"place: dragging {Armed.id} from the panel");
        }

        void TrackDragPlace(HandState owner)
        {
            if (owner.TrackingLost || !owner.Tracked) { _placing = false; _outline.enabled = false; return; }
            Ray aim = _hands.Aim(_placeLeft);
            if (PlacementRules.IsWallMounted(Armed)) { TrackWallPlace(owner, aim); return; }
            bool hitFloor = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floorHit);
            bool floor = ArmedFloor(hitFloor, floorHit, _room.FloorOutline, _store.Session.State.zoneOutline, out _placeSpot, out _placeWhy);
            _placeValid = floor && _store.Session.CheckPlacement(Ghost(_placeSpot), null) == PlacementStatus.Ok;
            if (hitFloor) ShowGhost(floor ? _placeSpot : floorHit, _placeValid);
            if (!owner.PinchEnded) return;
            _placing = false;
            _outline.enabled = false;
            if (_placeValid) { _addPoint = _placeSpot; if (Add(Armed, null)) Disarm(); return; }
            if (floor) InputDebug.Log($"place: {Armed.id} refused at {_placeSpot:F2}: {_store.Session.CheckPlacement(Ghost(_placeSpot), null)}");
            Refusal?.Invoke(floor ? "Can't place it there" : _placeWhy);
            Suggest(hitFloor ? floorHit : AimedFloor(), "the drop was refused");
        }

        // A wall piece dragged off the panel: the ghost slides along the walls, flush; release hangs it.
        void TrackWallPlace(HandState owner, Ray aim)
        {
            bool onWall = WallSpot(aim, Armed, out Vector3 pivot, out float yaw);
            _placeValid = onWall && _store.Session.CheckPlacement(WallGhost(pivot, yaw), null) == PlacementStatus.Ok;
            if (onWall) { _placeSpot = pivot; _placeWallYaw = yaw; ShowWallGhost(pivot, yaw, _placeValid); } else _outline.enabled = false;
            if (!owner.PinchEnded) return;
            _placing = false;
            _outline.enabled = false;
            if (_placeValid) { _addPoint = _placeSpot; _addWall = true; _addWallYaw = _placeWallYaw; if (Add(Armed, null)) Disarm(); }
            else Refusal?.Invoke(onWall ? "Can't hang it there" : HangWhy);
        }

        public const string HangWhy = "Pinch a wall to hang it";
        float _placeWallYaw;

        FreePlacement WallGhost(Vector3 pivot, float yaw) => new FreePlacement
        {
            placementId = "ghost", catalogItemId = Armed.id, position = new Vec3(pivot.x, pivot.y, pivot.z), yawDegrees = yaw,
        };

        void ShowWallGhost(Vector3 pivot, float yaw, bool valid)
        {
            BoxMath.WriteOutline(new OrientedBox(new Vec3(pivot.x, pivot.y + Armed.size.y * 0.5f, pivot.z), Armed.size, yaw), _outline);
            XrVisuals.Tint(_outline, valid ? new Color(0.039f, 0.455f, 0.878f) : Refused);
            _outline.enabled = true;
            ShowName(Armed.name, pivot + Vector3.up * (Armed.size.y + 0.08f));
        }

        FreePlacement Ghost(Vector3 at)
        {
            Vector3 toUser = _hands.HeadPosition - at;
            return new FreePlacement
            {
                placementId = "ghost", catalogItemId = Armed.id, position = new Vec3(at.x, _room.FloorY, at.z),
                yawDegrees = RoomGeometry.YawOf(new Vector3(toUser.x, 0f, toUser.z)),
            };
        }

        void ShowGhost(Vector3 at, bool valid)
        {
            Vector3 toUser = _hands.HeadPosition - at;
            float yaw = RoomGeometry.YawOf(new Vector3(toUser.x, 0f, toUser.z));
            BoxMath.WriteOutline(new OrientedBox(new Vec3(at.x, _room.FloorY + Armed.size.y * 0.5f, at.z), Armed.size, yaw), _outline);
            XrVisuals.Tint(_outline, valid ? new Color(0.039f, 0.455f, 0.878f) : Refused);
            _outline.enabled = true;
            ShowName(Armed.name, new Vector3(at.x, _room.FloorY + Armed.size.y + 0.12f, at.z)); // the same glass chip as hover
        }
        int _hovered = -1;
        PieceLook _shownLook;

        // Hover: the ray or a near hand on a placed piece outlines it in a light accent and names it; the selected piece
        // keeps an accent outline; a drag that breaks a rule turns it red. One outline, no allocation per frame.
        void ShowPieceLook()
        {
            int i = _dragging || _selected != null ? IndexOf(_selected) : _hovered;
            if (i < 0 && _hovered >= 0) i = _hovered;
            PieceLook look = i < 0 ? PieceLook.Plain : LookOf(i == _hovered, i == IndexOf(_selected), _dragging || _rotating, _previewRefused);
            bool shown = look != PieceLook.Plain;
            if (_outline.enabled != shown) _outline.enabled = shown;
            if (!shown) { if (_shownLook != PieceLook.Plain) HideName(); _shownLook = look; return; }
            FreePlacement p = _store.Session.State.placements[i];
            if (_store.Session.Catalog == null || !_store.Session.Catalog.TryGet(p.catalogItemId, out CatalogItem item)) return;
            bool live = (_dragging || _rotating) && i == IndexOf(_selected);
            Vector3 at = live ? _dragPos : new Vector3(p.position.x, p.position.y, p.position.z);
            float yaw = live ? _dragYaw : p.yawDegrees;
            BoxMath.WriteOutline(new OrientedBox(new Vec3(at.x, at.y + item.size.y * 0.5f, at.z), item.size, yaw), _outline);
            XrVisuals.Tint(_outline, look == PieceLook.Refused ? Refused : look == PieceLook.Hover ? new Color(0.45f, 0.72f, 1f) : new Color(0.039f, 0.455f, 0.878f));
            ShowName(string.IsNullOrEmpty(item.name) ? item.id : item.name, at + Vector3.up * (item.size.y + 0.12f));
            _shownLook = look;
        }

        /// <summary>An armed card is dropped when both hands are lost or the zone or scan is gone (pure, for tests).</summary>
        public static bool ShouldDisarm(bool armed, bool ready, bool leftTracked, bool rightTracked) =>
            armed && (!ready || (!leftTracked && !rightTracked));

        void Update()
        {
            _reason.Tick();
            HandState left = _hands.Left, right = _hands.Right;
            bool ready = _zone.Current == ZoneSetup.Phase.Confirmed && _room.HasScan;
            if (ShouldDisarm(Armed != null, ready, left.Tracked, right.Tracked)) Disarm(); // before the early return: a lost scan disarms too
            if (!ready)
            {
                if (_rotating) { _rotating = false; _view.Refresh(_store.Session.State, _store.Session.Catalog); }
                _handle?.Hide();
                return;
            }
            if (_view != null && _view.GhostShown) _view.PulseGhost(PlacementView.Pulse(Time.time));
            if (_rowOpen) { _row.Follow(_hands.View, _room.FloorOutline); Hover(); }
            if (_dragging) { Drag(_dragLeft ? left : right); ShowPieceLook(); return; }
            if (_rotating) { TrackRotate(_rotLeft ? left : right); ShowPieceLook(); return; }
            if (_placing) { if (Armed == null) { _placing = false; _outline.enabled = false; } else TrackDragPlace(_placeLeft ? left : right); return; }
            if (_panelMode)
            {
                Vector3 touch = right.Tracked ? right.PinchPoint : left.Tracked ? left.PinchPoint : Vector3.positiveInfinity;
                _hovered = PickPiece(touch, _hands.Gaze, out _);
                ShowPieceLook();
                if (Armed == null) ShowHandle(HandlePiece()); else _handle?.Hide();
                if (Armed != null && PlacementRules.IsWallMounted(Armed) && _hovered < 0)
                {
                    // An armed wall piece: its ghost follows the ray on the walls before the pinch (device batch 10).
                    if (WallSpot(_hands.Gaze, Armed, out Vector3 pivot, out float yaw))
                        ShowWallGhost(pivot, yaw, _store.Session.CheckPlacement(WallGhost(pivot, yaw), null) == PlacementStatus.Ok);
                    else { _outline.enabled = false; HideName(); }
                }
            }

            bool rightPinch = right.PinchStarted;
            if (!rightPinch && !left.PinchStarted) return;
            if (_hands.ClaimedThisFrame)
            {
                if (Armed != null) InputDebug.Log("place: armed pinch already taken (the panel)");
                return;
            }
            HandState hand = rightPinch ? right : left;

            int target = _rowOpen ? PickTarget(hand.PinchPoint) : -1;
            if (target >= 0) { _hands.Claim(); Press(target); return; }

            // A hand at rest aims at nothing: never pick or log its no-aim ray as an aim (log 2dc475d: "aim (0.16, 50.94, ...)").
            if (!_hands.HasAim(!rightPinch)) { if (Armed != null) InputDebug.Log("place: armed pinch, but that hand is not aiming"); return; }
            Ray aim = _hands.Aim(!rightPinch);
            if (Armed == null && TryBeginRotate(HandlePiece(), !rightPinch, aim)) { _hands.Claim(); return; } // the corner handle first
            int piece = PickPiece(hand.PinchPoint, aim, out bool near);
            if (Armed != null && piece < 0 && OnSuggestion(aim))
            {
                // The suggested spot, pinched: placed there as shown (one undo step, like any add).
                _hands.Claim();
                _addPoint = _suggestAt; _addYawSet = true; _addYaw = _suggestYaw;
                InputDebug.Log($"place: {Armed.id} at the suggested spot {_suggestAt:F2}, yaw {_suggestYaw:0}");
                if (Add(Armed, null)) Disarm();
                return;
            }
            if (Armed != null && piece < 0 && PlacementRules.IsWallMounted(Armed))
            {
                // A wall piece hangs where the ray meets a wall; a miss says so (the pinch stays free otherwise).
                if (!WallSpot(aim, Armed, out Vector3 pivot, out float yaw)) { Refusal?.Invoke(HangWhy); return; }
                _hands.Claim();
                _addPoint = pivot; _addWall = true; _addWallYaw = yaw;
                InputDebug.Log($"place: hang {Armed.id} at {pivot:F2}, yaw {yaw:0}");
                if (Add(Armed, null)) Disarm();
                return;
            }
            bool hitFloor = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floorHit);
            bool floor = ArmedFloor(hitFloor, floorHit, _room.FloorOutline, _store.Session.State.zoneOutline, out Vector3 spot, out string why);
            if (Armed != null)
            {
                InputDebug.Log($"place: armed pinch aim {aim.origin:F2}->{aim.direction:F2} floor {(hitFloor ? floorHit.ToString("F2") : "none")} " +
                               $"on piece {piece >= 0} -> {(floor ? "place at " + spot.ToString("F2") : why)}");
                // Silence is the bug: an armed pinch that misses the outside floor says why (the pinch stays free
                // for a box in my zone or the panel behind).
                if (piece < 0 && !floor) { Refusal?.Invoke(why); if (hitFloor) Suggest(floorHit, why); return; }
            }
            switch (Decide(piece >= 0, Armed != null, floor))
            {
                case PinchAction.Drag: _hands.Claim(); BeginDrag(piece, !rightPinch, near, hand); return;
                case PinchAction.Place: // panel flow: card first, then the floor
                    _hands.Claim();
                    _addPoint = spot;
                    if (Add(Armed, null)) Disarm(); // refused: stay armed, so the user can pick another spot
                    else Suggest(spot, "the aimed spot was refused"); // and show the nearest spot that fits
                    return;
                case PinchAction.OpenRow:
                    if (_panelMode) return; // the Furniture tab picks pieces: card first, then the floor
                    _hands.Claim(); OpenRow(null, spot); return;
            }
        }

        // ---- add / row ----

        void OpenRow(string placementId, Vector3 point)
        {
            if (_panelMode) { Select(placementId); return; } // the panel shows the selected piece; no row in the room
            _selected = placementId;
            _addPoint = point;
            _rowOpen = true;
            _marker.gameObject.SetActive(placementId == null);
            _marker.transform.position = point + Vector3.up * 0.002f;
            XrVisuals.Tint(_marker, XrVisuals.Hover);

            _row.Place(_hands.View, _room.FloorOutline);
            int tiles = placementId == null ? _outsideItems.Count : 0;
            for (int i = 0; i < Targets; i++)
            {
                bool tile = i < MaxTiles;
                bool shown = tile ? i < tiles : (placementId != null || i == Undo || i == Done);
                _targets[i].gameObject.SetActive(shown);
                if (!shown) continue;
                float x = tile ? (i - (tiles - 1) * 0.5f) * 0.13f : (i - Rotate - 1.5f) * 0.14f;
                _targets[i].transform.localPosition = new Vector3(x, tile ? 0f : -0.13f, 0f);
                if (tile) Shape(_targets[i], _outsideItems[i]);
            }
        }

        void CloseRow()
        {
            if (_panelMode) { Select(null); return; }
            _rowOpen = false;
            _selected = null;
            _marker.gameObject.SetActive(false);
            for (int i = 0; i < Targets; i++) _targets[i].gameObject.SetActive(false);
            if (_name != null) _name.Hide();
        }

        // Names the tile under the pinch point or gaze, below it.
        void Hover()
        {
            HandState right = _hands.Right, left = _hands.Left;
            // A stale pinch point from an untracked hand must not hover anything near it.
            Vector3 pinch = right.Tracked ? right.PinchPoint : left.Tracked ? left.PinchPoint : Vector3.positiveInfinity;
            int hover = PickTarget(pinch);
            if (hover < 0 || hover >= MaxTiles) { _name.Hide(); return; }
            CatalogItem item = _outsideItems[hover];
            _name.Show(string.IsNullOrEmpty(item.name) ? item.id : item.name,
                _targets[hover].transform.position + Vector3.down * 0.07f, _hands.HeadPosition);
        }

        void Press(int target)
        {
            DesignSession session = _store.Session;
            if (target == Done) { CloseRow(); return; }
            if (target == Undo) { _store.Undo(); return; }
            if (target < MaxTiles) { Add(_outsideItems[target], _targets[target]); return; }

            int i = IndexOf(_selected);
            if (i < 0) { CloseRow(); return; }
            FreePlacement p = session.State.placements[i];
            if (target == Remove)
            {
                if (session.RemovePlacement(p.placementId)) _store.NoteUserChange();
                return;
            }
            // Rotate
            if (session.TryMovePlacement(p.placementId, p.position, p.yawDegrees + 45f, out PlacementStatus status)) _store.NoteUserChange();
            else Refuse(_targets[Rotate], p.catalogItemId, status);
        }

        bool Add(CatalogItem item, Renderer tile)
        {
            Vector3 toUser = _hands.HeadPosition - _addPoint;
            var placement = new FreePlacement
            {
                placementId = "p-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                catalogItemId = item.id,
                position = new Vec3(_addPoint.x, _addWall ? _addPoint.y : _room.FloorY, _addPoint.z),
                yawDegrees = _addWall ? _addWallYaw : _addYawSet ? _addYaw : RoomGeometry.YawOf(new Vector3(toUser.x, 0f, toUser.z)),
            };
            _addWall = false;
            _addYawSet = false;
            if (_store.Session.TryAddPlacement(placement, out PlacementStatus status))
            {
                _store.NoteUserChange();
                Debug.Log($"{LogTag} Placed {item.id} outside.");
                OpenRow(placement.placementId, _addPoint);
                return true;
            }
            if (tile != null) Refuse(tile, item.id, status);
            else Refuse(_addPoint + Vector3.up * 0.3f, item.id, status);
            XrVisuals.Tint(_marker, Refused);
            return false;
        }

        void Refuse(Renderer shown, string itemId, PlacementStatus status) => Refuse(shown.transform.position + Vector3.up * 0.12f, itemId, status, shown);

        void Refuse(Vector3 at, string itemId, PlacementStatus status, Renderer shown = null)
        {
            if (shown != null) XrVisuals.Tint(shown, Refused);
            _reason.Show(RefusalText.Of(status), at, _hands.HeadPosition, 2f);
            Refusal?.Invoke(RefusalText.Of(status));
            Debug.Log($"{LogTag} {itemId} not placed: {status}.");
        }

        // ---- drag ----

        void BeginDrag(int index, bool isLeft, bool near, HandState hand)
        {
            FreePlacement p = _store.Session.State.placements[index];
            OpenRow(p.placementId, new Vector3(p.position.x, p.position.y, p.position.z));
            _dragging = true;
            _dragLeft = isLeft;
            _dragFar = !near;
            _dragPos = _dragTarget = new Vector3(p.position.x, p.position.y, p.position.z);
            _dragVelocity = Vector3.zero;
            _dragYaw = p.yawDegrees;
            Vector3 anchor = _dragFar ? GazeFloor(_dragPos) : hand.PinchPoint;
            _dragOffset = _dragPos - new Vector3(anchor.x, _dragPos.y, anchor.z);
            _previewRefused = false;
        }

        Vector3 _dragTarget, _dragVelocity;
        public const float DragSmoothTime = 0.12f, DragMaxSpeed = 2f;

        /// <summary>One step of the drag's smoothing (pure, for tests): critically damped toward the target, at most <see cref="DragMaxSpeed"/> m/s.</summary>
        public static Vector3 SmoothFollow(Vector3 current, Vector3 target, ref Vector3 velocity, float dt) =>
            Vector3.SmoothDamp(current, target, ref velocity, DragSmoothTime, DragMaxSpeed, Mathf.Max(dt, 1e-4f));

        void Drag(HandState owner)
        {
            if (owner.TrackingLost || !owner.Tracked) { EndDrag(false); return; }
            if (owner.PinchEnded) { EndDrag(true); return; }

            if (WallItem(_selected, out CatalogItem wallItem))
            {
                // A wall piece slides along the walls, flush (core snaps it); its turn comes from the wall.
                if (WallSpot(_hands.Aim(_dragLeft), wallItem, out Vector3 pivot, out float yaw)) { _dragTarget = pivot; _dragYaw = yaw; }
            }
            else
            {
                Vector3 anchor = _dragFar ? GazeFloor(_dragTarget) : owner.PinchPoint;
                // The hand's (or ray's floor) point, followed smoothly: critically damped (~0.12 s) and speed-limited, with no
                // amplification of the ray (device batch 9: the piece jumped).
                _dragTarget = new Vector3(anchor.x, _room.FloorY, anchor.z) + new Vector3(_dragOffset.x, 0f, _dragOffset.z);
            }
            _dragPos = SmoothFollow(_dragPos, _dragTarget, ref _dragVelocity, Time.deltaTime);
            _view.Pose(_selected, _dragPos, _dragYaw);

            int i = IndexOf(_selected);
            if (i < 0) return;
            FreePlacement preview = _store.Session.State.placements[i];
            preview.position = new Vec3(_dragPos.x, _dragPos.y, _dragPos.z);
            preview.yawDegrees = _dragYaw;
            bool refused = _store.Session.CheckPlacement(preview, _selected) != PlacementStatus.Ok;
            if (refused != _previewRefused) { _previewRefused = refused; _view.SetRefused(_selected, refused); }
        }

        void EndDrag(bool commit)
        {
            _dragging = false;
            if (!commit) Debug.Log($"{LogTag} Hand lost: move cancelled.");
            else if (_store.Session.TryMovePlacement(_selected, new Vec3(_dragPos.x, _dragPos.y, _dragPos.z), _dragYaw, out PlacementStatus status))
            {
                _store.NoteUserChange();
                return;
            }
            else Refuse(_dragPos + Vector3.up * 0.6f, _selected, status);
            _view.Refresh(_store.Session.State, _store.Session.Catalog); // snap back
        }

        // ---- picking ----

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

        int PickPiece(Vector3 pinch, Ray gaze, out bool near)
        {
            near = false;
            int best = -1;
            float bestFar = float.MaxValue;
            List<FreePlacement> placements = _store.Session.State.placements;
            ICatalog catalog = _store.Session.Catalog;
            for (int i = 0; catalog != null && i < placements.Count; i++)
            {
                if (!catalog.TryGet(placements[i].catalogItemId, out CatalogItem item)) continue;
                Vec3 p = placements[i].position;
                var box = new OrientedBox(new Vec3(p.x, p.y + item.size.y * 0.5f, p.z), item.size, placements[i].yawDegrees);
                if (BoxMath.Contains(box, pinch, 0.03f)) { near = true; return i; }
                float far = BoxMath.Raycast(box, gaze);
                if (far >= 0f && far < bestFar) { bestFar = far; best = i; }
            }
            if (best >= 0) _hands.ReportHover(gaze.GetPoint(bestFar), HandRig.BoxHitMiss);
            return best;
        }

        public const string AimAtFloor = "Aim at the floor outside your zone", InsideZone = "That's your zone: pinch the floor past the green line";

        /// <summary>
        /// The nearest point where <paramref name="ray"/> meets a room wall: the floor outline's edges as vertical planes,
        /// up to <paramref name="height"/> above the floor (pure, for tests). False when it meets none.
        /// </summary>
        public static bool RayWall(Ray ray, IReadOnlyList<Vec2> outline, float floorY, float height, out Vector3 hit)
        {
            hit = default;
            if (outline == null || outline.Count < 2) return false;
            float best = float.MaxValue;
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i], b = outline[(i + 1) % outline.Count];
                var along = new Vector3(b.x - a.x, 0f, b.y - a.y);
                float length = along.magnitude;
                if (length < 1e-4f) continue;
                along /= length;
                var normal = new Vector3(-along.z, 0f, along.x);
                float denom = Vector3.Dot(ray.direction, normal);
                if (Mathf.Abs(denom) < 1e-5f) continue;
                float t = Vector3.Dot(new Vector3(a.x, 0f, a.y) - ray.origin, normal) / denom;
                if (t <= 0f || t >= best) continue;
                Vector3 p = ray.GetPoint(t);
                float s = Vector3.Dot(p - new Vector3(a.x, 0f, a.y), along);
                if (s < 0f || s > length || p.y < floorY || p.y > floorY + height) continue;
                best = t;
                hit = p;
            }
            return best < float.MaxValue;
        }

        // A wall piece: the ray's wall point, snapped flush by core (centre 0.9-2.0 m, facing into the room).
        bool WallSpot(Ray aim, CatalogItem item, out Vector3 pivot, out float yaw)
        {
            pivot = default;
            yaw = 0f;
            if (!RayWall(aim, _room.FloorOutline, _room.FloorY, 3f, out Vector3 hit)) return false;
            if (!PlacementRules.TrySnapToWall(_room, item, new Vec3(hit.x, hit.y, hit.z), out Vec3 at, out yaw)) return false;
            pivot = new Vector3(at.x, at.y, at.z);
            return true;
        }

        bool _addWall, _addYawSet;
        float _addWallYaw, _addYaw;

        // ---- the suggested spot (the human: "can't place" everywhere a bed was aimed in a big room) ----

        readonly int[] _tally = new int[PlaceSuggest.TallySize];
        PlaceSuggest.Check _check;
        CatalogItem _suggestFor;
        Vector3 _suggestAt;
        float _suggestYaw;
        bool _suggested;

        // Started, wired and scanned: a search and a ghost can run.
        bool Ready => _view != null && _hands != null && _room != null && _store != null && _store.Session != null && _room.HasScan;

        // Where the user aims on the floor now, else 1.5 m ahead of them.
        Vector3 AimedFloor()
        {
            if (XrVisuals.RayFloor(_hands.Gaze, _room.FloorY, out Vector3 hit)) return hit;
            Ray view = _hands.View;
            Vector3 f = view.direction; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            return new Vector3(view.origin.x, _room.FloorY, view.origin.z) + f * 1.5f;
        }

        // Finds the valid spot nearest the aim (once, never per frame) and shows the piece there, pulsing; logs each rule's count.
        void Suggest(Vector3 aim, string why)
        {
            if (Armed == null || !Ready) return;
            if (PlacementRules.IsWallMounted(Armed) || !_room.HasScan) return;
            if (_check == null) _check = CheckSpot;
            _suggestFor = Armed;
            Vector3 head = _hands.HeadPosition;
            bool found = PlaceSuggest.Find(_room.FloorOutline, new Vector2(Armed.size.x, Armed.size.z), new Vector2(aim.x, aim.z),
                new Vector2(head.x, head.z), _check, _tally, out Vector2 at, out float yaw);
            Debug.Log($"{LogTag} Spot search for {Armed.id} ({Armed.size.x:F2} x {Armed.size.z:F2} m, {why}): {PlaceSuggest.Describe(_tally)}; " +
                      $"{(found ? $"suggested at ({at.x:F2}, {at.y:F2}), yaw {yaw:0}" : "nothing fits")}.");
            if (!found) { HideSuggestion(); Refusal?.Invoke(NoSpot); return; }
            _suggested = true;
            _suggestAt = new Vector3(at.x, PlacementRules.MountY(_room, Armed), at.y);
            _suggestYaw = yaw;
            _view.ShowGhost(Armed, _suggestAt, yaw);
        }

        public const string NoSpot = "No room for it anywhere outside your zone";

        void HideSuggestion()
        {
            _suggested = false;
            _view?.HideGhost();
        }

        PlacementStatus CheckSpot(Vector2 at, float yaw) => _store.Session.CheckPlacement(new FreePlacement
        {
            placementId = "ghost", catalogItemId = _suggestFor.id,
            position = new Vec3(at.x, PlacementRules.MountY(_room, _suggestFor), at.y), yawDegrees = yaw,
        }, null);

        // Whether the ray meets the suggested piece's box.
        bool OnSuggestion(Ray aim)
        {
            if (!_suggested || _suggestFor != Armed) return false;
            var box = new OrientedBox(new Vec3(_suggestAt.x, _suggestAt.y + Armed.size.y * 0.5f, _suggestAt.z), Armed.size, _suggestYaw);
            return BoxMath.Raycast(box, aim) >= 0f;
        }

        /// <summary>
        /// Where an armed card lands for a floor hit (pure, for tests): in the outside zone; a hit past the room's
        /// edge (seen through a virtual wall) is pulled back just inside the room. False with the reason otherwise.
        /// </summary>
        public static bool ArmedFloor(bool hitFloor, Vector3 hit, IReadOnlyList<Vec2> room, IReadOnlyList<Vec2> zone, out Vector3 spot, out string why)
        {
            spot = hit;
            why = null;
            if (!hitFloor) { why = AimAtFloor; return false; }
            var p = new Vec2(hit.x, hit.z);
            if (room != null && room.Count >= 3 && !Zones.Contains(room, p))
            {
                Vec2 edge = RoomGeometry.ClampInside(room, p);
                Vector2 c = RoomGeometry.Centroid(room);
                var inward = new Vector2(c.x - edge.x, c.y - edge.y);
                float len = inward.magnitude;
                p = len > 1e-4f ? new Vec2(edge.x + inward.x / len * 0.15f, edge.y + inward.y / len * 0.15f) : edge;
                spot = new Vector3(p.x, hit.y, p.y);
            }
            if (zone != null && zone.Count >= 3 && Zones.Contains(zone, p)) { why = InsideZone; return false; }
            return true;
        }


        Vector3 GazeFloor(Vector3 fallback) =>
            XrVisuals.RayFloor(_hands.Aim(_dragLeft), _room.FloorY, out Vector3 hit) ? hit : fallback; // the dragging hand's ray

        bool WallItem(string placementId, out CatalogItem item)
        {
            item = null;
            int i = IndexOf(placementId);
            return i >= 0 && _store.Session.Catalog != null && _store.Session.Catalog.TryGet(_store.Session.State.placements[i].catalogItemId, out item)
                && PlacementRules.IsWallMounted(item);
        }

        int IndexOf(string placementId)
        {
            if (placementId == null) return -1;
            List<FreePlacement> placements = _store.Session.State.placements;
            for (int i = 0; i < placements.Count; i++) if (placements[i].placementId == placementId) return i;
            return -1;
        }

        static void Shape(Renderer tile, CatalogItem item)
        {
            float largest = Mathf.Max(item.size.x, Mathf.Max(item.size.y, item.size.z));
            float k = largest > 0f ? 0.09f / largest : 0.09f;
            tile.transform.localScale = new Vector3(item.size.x * k, item.size.y * k, item.size.z * k);
            tile.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // front faces the user
            XrVisuals.Tint(tile, RedesignView.ColorOf(item));
        }
    }
}

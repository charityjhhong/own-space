using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// First-launch zone setup (CONCEPT 5.1): proposes a zone, shows it as a floor outline with a post
    /// and knob per corner at desk height (reachable while seated), lets either hand move a corner by
    /// pinching its knob (near) or by looking at it and pinching (far, the corner follows the gaze on
    /// the floor), and saves it when the user pinches and releases on Done. Corners stay on the room
    /// floor. Losing the hand or pausing cancels a drag.
    /// A saved zone skips setup on the next launch.
    /// First run (device day, build 0.0.2): the outline is on the floor (a desk-height copy read as the floor edge and
    /// misled), with a post up to each corner's knob at desk height so a seated user sees the corners; Done is a flat green button labelled "Done" near the view centre, with a
    /// short instruction above it (<see cref="Hint"/>). No ghost hand at this step.
    /// </summary>
    public sealed class ZoneSetup : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        const int MaxCorners = 16;

        [SerializeField] MrukRoomInfo _room;
        [SerializeField] DesignStore _store;
        [SerializeField] HandRig _hands;
        [SerializeField] Material _material;
        [SerializeField, Tooltip("Knob height above the floor (m): near a seated desk top.")] float _knobHeight = 0.75f;
        [SerializeField, Tooltip("Direct pinch reach around a knob or button (m).")] float _nearRadius = 0.07f;

        public enum Phase { Waiting, Editing, Confirmed }

        readonly List<Vec2> _points = new List<Vec2>(MaxCorners);
        readonly Transform[] _knobs = new Transform[MaxCorners];
        readonly Renderer[] _knobRenderers = new Renderer[MaxCorners];
        readonly Renderer[] _posts = new Renderer[MaxCorners];
        LineRenderer _line;
        Renderer _done, _fit, _keep;
        WorldLabel _doneLabel, _hint, _fitLabel, _keepLabel;
        int _hover = -1, _drag = -1, _armedButton = -1;
        int _moveEdge = NoMove;          // a whole-zone (-1) or edge (index) drag; NoMove = none
        readonly List<Vec2> _moveStart = new List<Vec2>(MaxCorners);
        Vector3 _moveFrom;
        ZoneGrab _hoverGrab;
        bool _guard;                     // Done was pinched with the desk outside: Fit to desk / Keep anyway
        ZoneCard _card;                  // the panel-style card (instruction + buttons); null = the older 3D Done slab
        string _cardLine1, _cardLine2, _cardPrimary, _cardSecondary;
        bool _cardDrag, _cardMoved;      // the user moves the card by its bar; then it stays where they put it
        float _cardDistance;
        const int BarIndex = MaxCorners + 3, CardBodyIndex = MaxCorners + 4, SampleIndex = MaxCorners + 5, CancelIndex = MaxCorners + 6;

        // Opened from Settings: Cancel puts the zone back as it was (device: "I can't get out").
        bool _cancellable;
        readonly List<Vec2> _editStart = new List<Vec2>(MaxCorners);
        public const string CancelLabel = "Cancel", HoverCorner = "Pinch to move this corner", HoverEdge = "Pinch to move this side",
            HoverWhole = "Pinch to move the whole area", MissLine = "Nothing there. Aim at a ball or the line.";

        /// <summary>
        /// Whether the zone was just confirmed (Done, Keep anyway or Cancel) from an edit opened in Settings; false for every
        /// other confirm (a first run, a reload, a Settings edit that started over). PanelHost reads it on Confirmed.
        /// </summary>
        public bool ConfirmedFromSettings { get; private set; }

        /// <summary>Whether the last edit ended with Cancel (the zone as it was).</summary>
        public bool Cancelled { get; private set; }

        string Secondary() => _cancellable ? CancelLabel : NoScanOffer();

        /// <summary>Saved spaces: with no usable scan, the card offers the sample room (PanelHost goes there).</summary>
        public event Action SampleRequested;
        public const string SampleLabel = "Use sample room";
        string NoScanOffer() => !_room.HasScan && SampleRequested != null ? SampleLabel : null;
        // Seated exactly where the tool panel seats. If it is out of view for 2 s, it comes back in front (device bug on
        // a2c27d2: the user could not find it and was stuck). The user can still drag it by its bar. Holding the bar for
        // 2 s confirms the current zone: a safety exit.
        const float CardAhead = 0.5f, CardBelow = 0.33f, CardRight = 0.1f, CardViewDegrees = 45f, HoldToConfirm = 2f;
        ToolPanel _panel;
        float _barHeld;
        Vector3 _barStartAim;

        /// <summary>The card's two lines (device batch 8: short, single lines; what the zone is, in one line).</summary>
        public const string ZoneLine = "Drag the corners to set your zone.", ZoneMeaning = "Inside stays real. Outside becomes virtual.",
            NoScanLine = "No room scan: Settings > Rescan room", DeskLine = "Your desk is outside your zone.", DeskQuestion = "Fit the zone to it, or keep it?";
        // The panel's accent instead of raw green (device batch 7: share the tool panel's look).
        internal static readonly Color Accent = new Color(0.039f, 0.455f, 0.878f), AccentHover = new Color(0.35f, 0.65f, 1f), AccentActive = new Color(0.02f, 0.3f, 0.65f);

        /// <summary>Builds the zone card from the tool panel's look (PanelHost calls it once the panel exists).</summary>
        public void UseCard(ToolPanel panel)
        {
            if (panel == null || _card != null) return;
            _panel = panel;
            _card = new ZoneCard(transform, panel);
            _turnHandle = new RotateHandle(transform, panel, _material); // the zone turns like a piece (the same handle)
            if (Current == Phase.Editing) SetPhase(Phase.Editing);
        }

        // The card's spot: the panel's seat (or in front of the eyes, a little low), moved up or aside when it would cover a
        // corner or the line (device shot 182125: it sat over the corner post).
        Vector3 CardSpot()
        {
            Ray view = _hands.View;
            Vector3 f = Vector3.ProjectOnPlane(view.direction, Vector3.up);
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 at = view.origin + f * CardAhead + Vector3.down * CardBelow + Vector3.Cross(Vector3.up, f) * CardRight;
            if (_panel != null) _panel.SeatPose(out at, out _);
            at = OutOfReach(view.origin, at);
            return ClearOfZone(view.origin, at, Vector3.Cross(Vector3.up, f), _points, _room.FloorY, _knobHeight);
        }

        /// <summary>A pinch this far in front of the card, over it, is the card's (never the zone's), button or not.</summary>
        public const float CardClaimFront = 0.12f;

        /// <summary>The card seats at least this far from the eyes (outside arm's reach, like the panel's 0.65 m seat).</summary>
        public const float CardMinDistance = 0.6f;

        /// <summary>The card covers a zone mark when it is within this angle of it, seen from the head.</summary>
        public const float CardClearDegrees = 12f;

        /// <summary>The spot pushed out to <see cref="CardMinDistance"/> from the eyes when nearer (pure, for tests).</summary>
        public static Vector3 OutOfReach(Vector3 head, Vector3 spot)
        {
            Vector3 to = spot - head;
            float d = to.magnitude;
            return d >= CardMinDistance || d < 1e-4f ? spot : head + to / d * CardMinDistance;
        }

        /// <summary>
        /// The first of the card spot, 12 or 24 cm higher, or 30 cm to either side that keeps every corner ball, post foot and
        /// edge middle at least <see cref="CardClearDegrees"/> from the card, seen from <paramref name="head"/> (pure); else the
        /// clearest of them.
        /// </summary>
        public static Vector3 ClearOfZone(Vector3 head, Vector3 spot, Vector3 right, IReadOnlyList<Vec2> zone, float floorY, float knobHeight)
        {
            Vector3 best = spot;
            float bestAngle = -1f;
            for (int k = 0; k < 5; k++)
            {
                Vector3 at = spot + (k == 1 ? Vector3.up * 0.12f : k == 2 ? Vector3.up * 0.24f : k == 3 ? -right * 0.3f : k == 4 ? right * 0.3f : Vector3.zero);
                float angle = ClosestMark(head, at, zone, floorY, knobHeight);
                if (angle >= CardClearDegrees) return at;
                if (angle > bestAngle) { bestAngle = angle; best = at; }
            }
            return best;
        }

        static float ClosestMark(Vector3 head, Vector3 at, IReadOnlyList<Vec2> zone, float floorY, float knobHeight)
        {
            Vector3 to = at - head;
            float min = 180f;
            int n = zone != null ? zone.Count : 0;
            for (int i = 0; i < n; i++)
            {
                Vec2 a = zone[i], b = zone[(i + 1) % n];
                min = Mathf.Min(min, Vector3.Angle(to, new Vector3(a.x, floorY + knobHeight, a.y) - head));
                min = Mathf.Min(min, Vector3.Angle(to, new Vector3(a.x, floorY, a.y) - head));
                min = Mathf.Min(min, Vector3.Angle(to, new Vector3((a.x + b.x) * 0.5f, floorY, (a.y + b.y) * 0.5f) - head));
            }
            return min;
        }

        void ShowCard(string line1, string line2, string primary, string secondary, bool mainLeft = false)
        {
            _cardLine1 = line1; _cardLine2 = line2; _cardPrimary = primary; _cardSecondary = secondary;
            // A card already up stays where it is (device: it moved, and the next pinch at Done's spot hit nothing).
            _card.Show(line1, line2, primary, secondary, _cardMoved || _card.Visible ? _card.Position : CardSpot(), _hands.HeadPosition, mainLeft);
            // Where it went (device bug on a2c27d2: the card was not visible anywhere on the no-scan path).
            Ray view = _hands.View;
            Vector3 to = _card.Position - view.origin;
            InputDebug.Log($"zone: card shown at {_card.Position:F2}; head {view.origin:F2}; {to.magnitude:F2} m, " +
                           $"{Vector3.Angle(view.direction, to):0}° off the view; visible {_card.Visible}; scan {_room.HasScan}");
        }

        float _noCardFor;
        bool _scanWas;

        // The card must be on screen whenever the zone step is (a missing card left the user stuck).
        void KeepCardUp()
        {
            if (_card == null)
            {
                _noCardFor += Time.deltaTime;
                if (_noCardFor > 2f && _noCardFor - Time.deltaTime <= 2f) Debug.LogWarning($"{LogTag} Zone step without its card for 2 s (the panel host has not built it).");
                return;
            }
            _noCardFor = 0f;
            if (_card.Visible) return;
            InputDebug.Log("zone: the card was hidden during the zone step; showing it again");
            _cardMoved = false;
            ShowGuard(_guard);
        }

        bool _dragLeft, _dragFar, _touched, _doneArmed, _doneLeft, _proposePending;
        Vec2 _dragStart;
        Vector3 _dragOffset;

        public Phase Current { get; private set; } = Phase.Waiting;
        public IReadOnlyList<Vec2> Outline => _points;

        /// <summary>Where the Done cube is (for onboarding hints).</summary>
        public Vector3 DonePosition => _done != null ? _done.transform.position : Vector3.zero;

        /// <summary>Raised once the zone is saved (and at start when a saved zone was restored).</summary>
        public event Action Confirmed;

        /// <summary>Raised when the handles are shown again; edit mode hides its outlines and row.</summary>
        public event Action EditStarted;

        const int DoneIndex = MaxCorners, FitIndex = MaxCorners + 1, KeepIndex = MaxCorners + 2, NoMove = -2;

        /// <summary>The Done guard (device batch 7: the desk ended up outside and the whole view went virtual).</summary>
        public const string DeskOutsideText = "Your desk is outside your zone.\nFit to desk, or keep it anyway?";

        /// <summary>Whether the desk or bed the user is at lies outside the zone (pure, for tests).</summary>
        public static bool DeskOutside(IReadOnlyList<Vec2> zone, IReadOnlyList<FurnitureAnchor> furniture, Vec2 user)
        {
            FurnitureAnchor piece = ZoneFit.AtPiece(furniture, user);
            return piece != null && !ZoneFit.Holds(zone, piece);
        }

        /// <summary>The Done guard's words for the piece the user is at ("desk" or "bed").</summary>
        public static string OutsideLine(string piece) => "Your " + piece + " is outside your area";
        public static string FitLabel(string piece) => "Fit to " + piece;

        /// <summary>The zone step's instruction, above the Done button.</summary>
        public const string Hint = "This outline is your zone. Pinch a ball, an edge,\nor inside it to move it, then pinch Done.";

        /// <summary>The instruction when the room has no usable scan (none, or another room's): how to get one.</summary>
        public const string NoScanHint = "No scan of this room: walls and furniture need\none from Space Setup (Settings > Physical space).\n\n" + Hint;

        /// <summary>The zone step's instruction for a room with or without a usable scan (pure, for tests).</summary>
        public static string HintFor(bool hasScan) => hasScan ? Hint : NoScanHint;

        /// <summary>Said once per launch when the scan has a desk but no chair beside it.</summary>
        public const string NoChairNote = "Only a desk was found. Add your chair\nin Space Setup to restyle it.";

        /// <summary>The zone step's instruction, with the no-chair note in front when it applies (pure, for tests).</summary>
        public static string HintFor(bool hasScan, bool noChair) => hasScan && noChair ? NoChairNote + "\n\n" + Hint : HintFor(hasScan);

        bool _chairNoteSaid;
        static readonly Vector3 DoneSize = new Vector3(0.14f, 0.06f, 0.012f);
        const float DoneAhead = 0.45f, DoneBelow = 0.14f; // near the view centre, seated or standing

        void Start()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = _material;
            _line.loop = true;
            _line.widthMultiplier = 0.02f;
            _line.positionCount = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < MaxCorners; i++)
            {
                _knobRenderers[i] = XrVisuals.Make(PrimitiveType.Sphere, transform, _material, XrVisuals.Idle, "ZoneKnob");
                _knobs[i] = _knobRenderers[i].transform;
                _knobs[i].localScale = Vector3.one * 0.09f;
                _posts[i] = XrVisuals.Make(PrimitiveType.Cylinder, transform, _material, XrVisuals.Idle, "ZonePost");
            }
            _done = XrVisuals.Make(PrimitiveType.Cube, transform, _material, XrVisuals.Confirmed, "ZoneDone");
            _done.transform.localScale = DoneSize;
            _doneLabel = new WorldLabel(transform, "ZoneDoneLabel", new Color(0.05f, 0.08f, 0.12f));
            _fit = XrVisuals.Make(PrimitiveType.Cube, transform, _material, XrVisuals.Confirmed, "ZoneFit");
            _keep = XrVisuals.Make(PrimitiveType.Cube, transform, _material, new Color(0.85f, 0.85f, 0.85f), "ZoneKeep");
            _fit.transform.localScale = _keep.transform.localScale = DoneSize;
            _fit.gameObject.SetActive(false);
            _keep.gameObject.SetActive(false);
            _fitLabel = new WorldLabel(transform, "ZoneFitLabel", new Color(0.05f, 0.08f, 0.12f));
            _keepLabel = new WorldLabel(transform, "ZoneKeepLabel", new Color(0.05f, 0.08f, 0.12f));
            _hint = new WorldLabel(transform, "ZoneHint", Color.white);

            _room.Changed += OnRoomChanged;
            _hands.RecenterSettled += OnRecenterSettled;
            OnRoomChanged();
        }

        void OnDestroy()
        {
            if (_room != null) _room.Changed -= OnRoomChanged;
            if (_hands != null) _hands.RecenterSettled -= OnRecenterSettled;
        }

        // After a recenter the card goes back to its spot in front of the user, unless they moved it by its bar.
        void OnRecenterSettled()
        {
            if (Current != Phase.Editing || _card == null || !_card.Visible || _cardMoved) return;
            _card.MoveTo(CardSpot(), _hands.HeadPosition);
            InputDebug.Log("zone: card back in front after a recenter");
        }

        void OnRoomChanged()
        {
            if (!_store.RoomApplied) return; // the room's save is not loaded yet: nothing to restore or propose
            if (_store.HasZone)
            {
                Copy(_store.Session.State.zoneOutline);
                if (_room.HasScan) SquareSavedZone();
                SetPhase(Phase.Confirmed);
                return;
            }
            // Another room's save without a zone replaced a confirmed one: start over with a fresh proposal.
            if (Current == Phase.Confirmed) _touched = false;
            // The scan loaded late (after the no-scan start): propose again in the real room rather than keep a zone
            // drawn without walls (device bug on a2c27d2).
            bool scanArrived = _room.HasScan && !_scanWas;
            _scanWas = _room.HasScan;
            if (scanArrived && Current == Phase.Editing) { _touched = false; InputDebug.Log("zone: the room scan arrived; proposing again in the real room"); }
            // Re-propose until the user has moved a corner; then keep their work.
            if (_touched) return;
            // The proposal centres on the user, so wait for a tracked head pose.
            if (!_hands.HeadTracked) { _proposePending = true; return; }
            _proposePending = false;
            _cancellable = false; // a fresh proposal is not the Settings edit any more
            Propose();
            SetPhase(Phase.Editing);
        }

        // The zone is the real part of the room: every clamp and proposal uses the scanned outline, never the extended one.
        void Propose()
        {
            Vector3 head = _hands.HeadPosition;
            Copy(ZoneProposer.Propose(_room, new Vec3(head.x, head.y, head.z)));
            // Parallel to the desk or bed (or the nearest wall), and a rectangle inside the room (per-corner clamping
            // skewed it: the human's diagonal zone).
            ZoneAlign.Square(_points, FrameYaw(), _room.RealOutline);
            // The user's desk (and chair) always start inside the zone, with room to spare (device batch 5).
            ZoneFit.Contain(_points, _room.Furniture, new Vec2(head.x, head.z), _room.RealOutline);
            // ...and leaves room outside it for Add (device day: the zone covered the whole study).
            ZoneFit.KeepOutside(_points, _room.Furniture, new Vec2(head.x, head.z), _room.RealOutline);
        }

        // A saved zone the user never turned with the handle is squared to the desk's (or wall's) direction, whatever its angle
        // (the human: the zone is parallel to the desk; device, build dfb22f4: a zone left about 33 degrees off by the old bug).
        void SquareSavedZone()
        {
            float target = FrameYaw();
            FurnitureAnchor piece = ZoneFit.AtPiece(_room.Furniture, User());
            string why;
            if (ZoneTurnRecord.TurnedByUser(_store.SavePath)) { RectFrame(_points, out _, out _, out _, out float y); why = $"kept (turned by the user), saved {y:0}°"; }
            else if (ZoneAlign.SquareSaved(_points, target, _room.RealOutline, out float saved))
            {
                if (piece != null && !ZoneFit.Holds(_points, piece)) ZoneFit.Contain(_points, _room.Furniture, User(), _room.RealOutline); // the desk stays in
                _store.Session.SetZone(_points);
                why = $"squared from {saved:0}°";
            }
            else { RectFrame(_points, out _, out _, out _, out float y); why = $"kept (already square or not a rectangle), saved {y:0}°"; }
            InputDebug.Log($"zone: saved zone {why}; target {target:0}° ({(piece != null ? ZoneFit.PieceWord(piece) + $" yaw {piece.Box.yawDegrees:0}°, {ZoneAlign.FootprintToWall(piece.Box, _room.RealOutline):F2} m from its wall" : "no desk or bed: nearest wall")})");
        }

        Vec2 User() { Vector3 h = _hands.HeadPosition; return new Vec2(h.x, h.z); }

        // The zone's direction: the desk or bed the user is at (squared to its wall), else the nearest wall.
        float FrameYaw() => ZoneAlign.FrameYaw(ZoneFit.AtPiece(_room.Furniture, User()), _room.RealOutline, User());

        /// <summary>After Reset my design: a fresh proposal and the zone step again, as on a first run.</summary>
        public void StartOver()
        {
            _touched = false;
            _cancellable = false; // no longer the Settings edit: its Done goes to the panel as on a first run (review of #268)
            ZoneTurnRecord.Write(_store.SavePath, false); // Reset my design: a fresh, square zone
            _points.Clear();
            if (!_hands.HeadTracked) { _proposePending = true; return; } // Update proposes once the head is tracked
            Propose();
            SetPhase(Phase.Editing);
        }

        /// <summary>Shows the handles again so the user can change a confirmed zone.</summary>
        public void BeginEdit(bool cancellable = false)
        {
            if (_points.Count < 3) Propose();
            _editStart.Clear();
            _editStart.AddRange(_points);
            _cancellable = cancellable && _points.Count >= 3;
            SetPhase(Phase.Editing);
        }

        void CancelEdit()
        {
            InputDebug.Log("zone: edit cancelled; the zone is as it was");
            Copy(_editStart);
            Cancelled = true;
            SetPhase(Phase.Confirmed); // nothing saved: the stored zone never changed
        }

        // The card's first line says what a pinch will grab (corner, side or the whole area).
        void ShowHoverLine()
        {
            if (_card == null || !_card.Visible || _guard) return;
            string grab = _hoverGrab == ZoneGrab.Corner || (_hover >= 0 && _hover < MaxCorners) ? HoverCorner
                : _hoverGrab == ZoneGrab.Edge ? HoverEdge : _hoverGrab == ZoneGrab.Whole ? HoverWhole : null;
            // With no scan, the card keeps saying how to get one (the knob and line tints still show the hover); this also
            // brings that line back after a miss note (review of #268).
            _card.SetLines(HoverLines(_room.HasScan, grab, out string second), second);
        }

        /// <summary>The card's lines while hovering (pure, for tests): what a pinch grabs, or with no scan how to get one.</summary>
        public static string HoverLines(bool hasScan, string grab, out string second)
        {
            second = ZoneMeaning;
            return hasScan ? grab ?? ZoneLine : NoScanLine;
        }

        /// <summary>The card's lines after a pinch on nothing (pure): with no scan, the scan line stays first.</summary>
        public static string MissLines(bool hasScan, out string second)
        {
            second = hasScan ? ZoneMeaning : MissLine;
            return hasScan ? MissLine : NoScanLine;
        }

        // A pinch that grabs nothing: the card says so, and comes into view if it was out of it.
        void MissNote()
        {
            if (_card == null || !_card.Visible || _guard) return;
            _card.SetLines(MissLines(_room.HasScan, out string second), second);
            Ray view = _hands.View;
            if (!ZoneCard.InView(view, _card.Position, CardViewDegrees)) { _cardMoved = false; _card.MoveTo(CardSpot(), view.origin); }
        }

        void Update()
        {
            if (_proposePending && _hands.HeadTracked) OnRoomChanged();
            if (Current != Phase.Editing) return;
            KeepCardUp();
            HandState left = _hands.Left, right = _hands.Right;

            if (_doneArmed)
            {
                HandState owner = _doneLeft ? left : right;
                if (owner.TrackingLost || !owner.Tracked) { _doneArmed = false; Refresh(); }
                else if (owner.PinchEnded)
                {
                    _doneArmed = false;
                    Refresh();
                    // Act only when the pinch is released still on the same button, never from a knob.
                    // Released on the same button, or still on the card but on no other button (fingers drift as they open).
                    int at = Pick(owner.PinchPoint, true);
                    if (at == _armedButton || at == CardBodyIndex) PressButton(_armedButton);
                    else InputDebug.Log($"zone: button {_armedButton - DoneIndex} released off it ({at}); nothing pressed");
                }
                return;
            }

            if (_turning)
            {
                HandState owner = _dragLeft ? left : right;
                if (owner.TrackingLost || !owner.Tracked) CancelTurn();
                else if (owner.PinchEnded) EndTurn();
                else TrackTurn();
                return;
            }

            if (_drag >= 0 || _moveEdge != NoMove)
            {
                HandState owner = _dragLeft ? left : right;
                if (owner.TrackingLost || !owner.Tracked) { CancelDrag(); }
                else if (owner.PinchEnded) { EndDrag(); }
                else { Follow(owner); }
                Refresh();
                return;
            }

            ScaleKnobs(); // the head moves: knobs keep their size in view
            if (_card != null && _card.Visible)
            {
                Ray view = _hands.View;
                if (_cardDrag)
                {
                    HandState owner = _dragLeft ? left : right;
                    if (owner.PinchEnded || owner.TrackingLost || !owner.Tracked) { _cardDrag = false; return; }
                    Vector3 aimAt = _hands.Aim(_dragLeft).GetPoint(_cardDistance);
                    // A still pinch on the bar for 2 s confirms the zone as it is (the safety exit).
                    _barHeld = Vector3.Distance(aimAt, _barStartAim) < 0.03f ? _barHeld + Time.deltaTime : -1000f;
                    if (_barHeld >= HoldToConfirm) { _cardDrag = false; InputDebug.Log("zone: bar held 2 s; confirming the zone as it is"); Confirm(); return; }
                    if (_barHeld < 0f) _card.MoveTo(aimAt, view.origin);
                    return;
                }
                // The card stays where it is while the user works on the zone (device: it chased the view every 2 s and
                // covered a corner); a pinch that grabs nothing brings it back into view (MissNote).
            }
            Vector3 pinch = right.Tracked ? right.PinchPoint : left.PinchPoint;
            // The ray the next pinch will use (Press aims with the pinching hand; the right one first), so the hover names
            // exactly what a pinch grabs (review of #268: hover used the head gaze).
            Ray aim = _hands.Aim(!right.Tracked && left.Tracked);
            int hover = Pick(pinch, right.Tracked || left.Tracked);
            ZoneGrab grab = ZoneGrab.None;
            if (hover < 0)
            {
                grab = ZonePick.Pick(aim, pinch, right.Tracked || left.Tracked, _points, _room.FloorY, _knobHeight, out int corner, out _);
                if (grab == ZoneGrab.Corner) hover = corner;
            }
            if (hover != _hover || grab != _hoverGrab) { _hover = hover; _hoverGrab = grab; Refresh(); ShowHoverLine(); }
            ShowTurnHandle(hover < 0, aim);

            if (right.PinchStarted) Press(false, right);
            else if (left.PinchStarted) Press(true, left);
        }

        // ---- turning the zone (the human: "the zone rectangle must be rotatable, the same way as furniture") ----

        RotateHandle _turnHandle;
        bool _turning;
        Vector3 _turnFrom, _turnCentre;
        float _turnStartYaw, _turnYaw;

        /// <summary>Zone turns snap to this step, and to the room's wall directions within <see cref="WallSnapDegrees"/>.</summary>
        public const float TurnStep = 5f, WallSnapDegrees = 3f, HandleOut = 0.25f;

        // The handle sits just outside the zone's front-right corner (seen from the user), clear of that corner's knob.
        bool TurnHandleSpot(out Vector3 at)
        {
            at = default;
            if (_turnHandle == null || !RectFrame(_points, out Vec2 c, out float w, out float d, out float yaw)) return false;
            Vector3 centre = new Vector3(c.x, _room.FloorY, c.y);
            Vector3 corner = RotateHandle.FrontRightCorner(centre, new Vector3(w, 0f, d), yaw, _room.FloorY + 0.02f, _hands.HeadPosition);
            Vector3 outward = corner - centre;
            outward.y = 0f;
            at = corner + (outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward) * HandleOut;
            return true;
        }

        void ShowTurnHandle(bool free, Ray aim)
        {
            if (_turnHandle == null) return;
            if (free && TurnHandleSpot(out Vector3 at) && RotateHandle.Near(aim, at, RotateHandle.ShowRadius))
                _turnHandle.Show(at, _hands.HeadPosition, RotateHandle.Near(aim, at, RotateHandle.HitRadius));
            else _turnHandle.Hide();
        }

        bool TryBeginTurn(bool isLeft, Ray aim)
        {
            if (!TurnHandleSpot(out Vector3 at) || !RotateHandle.Near(aim, at, RotateHandle.HitRadius)) return false;
            RectFrame(_points, out Vec2 c, out _, out _, out _turnStartYaw);
            _turnCentre = new Vector3(c.x, _room.FloorY, c.y);
            _turnFrom = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floor) ? floor : at;
            _turnYaw = _turnStartYaw;
            _moveStart.Clear();
            _moveStart.AddRange(_points);
            _dragLeft = isLeft;
            _turning = true;
            InputDebug.Log($"zone: turning from {_turnStartYaw:0}°");
            return true;
        }

        void TrackTurn()
        {
            if (!XrVisuals.RayFloor(_hands.Aim(_dragLeft), _room.FloorY, out Vector3 now)) return;
            Vector3 a = _turnFrom - _turnCentre, b = now - _turnCentre;
            a.y = b.y = 0f;
            if (a.sqrMagnitude < 1e-4f || b.sqrMagnitude < 1e-4f) return;
            float yaw = SnapZoneYaw(_turnStartYaw + Vector3.SignedAngle(a, b, Vector3.up), _room.RealOutline, FrameYaw());
            // Inside the room only: a turn that would cross a wall stops at the last good angle.
            if (!RotateZone(_moveStart, new Vec2(_turnCentre.x, _turnCentre.z), yaw - _turnStartYaw, _room.RealOutline, _points)) return;
            _turnYaw = yaw;
            for (int i = 0; i < _points.Count; i++) Place(i);
            if (TurnHandleSpot(out Vector3 at)) { _turnHandle.Show(at, _hands.HeadPosition, true); _turnHandle.ShowAngle(_turnYaw, _hands.HeadPosition); }
        }

        bool _turnedThisEdit;

        void EndTurn()
        {
            _turnedThisEdit = true; // kept as turned once confirmed (ZoneTurnRecord)
            _turning = false;
            _touched = true;
            _turnHandle.Hide();
            InputDebug.Log($"zone: turned to {_turnYaw:0}°");
        }

        void CancelTurn()
        {
            _turning = false;
            _turnHandle.Hide();
            for (int i = 0; i < _points.Count && i < _moveStart.Count; i++) { _points[i] = _moveStart[i]; Place(i); }
            Debug.Log($"{LogTag} Hand lost: zone turn cancelled.");
        }

        /// <summary>
        /// A 4-corner rectangle's centre, side lengths and yaw (the direction of its first side, as Unity's yaw) (pure,
        /// for tests). False for anything else.
        /// </summary>
        public static bool RectFrame(IReadOnlyList<Vec2> p, out Vec2 centre, out float width, out float depth, out float yaw)
        {
            centre = default; width = depth = yaw = 0f;
            if (p == null || p.Count != 4) return false;
            centre = new Vec2((p[0].x + p[1].x + p[2].x + p[3].x) * 0.25f, (p[0].y + p[1].y + p[2].y + p[3].y) * 0.25f);
            float ax = p[1].x - p[0].x, az = p[1].y - p[0].y, bx = p[3].x - p[0].x, bz = p[3].y - p[0].y;
            width = Mathf.Sqrt(ax * ax + az * az);
            depth = Mathf.Sqrt(bx * bx + bz * bz);
            if (width < 1e-3f || depth < 1e-3f) return false;
            yaw = Mathf.Atan2(ax, az) * Mathf.Rad2Deg - 90f; // the first side runs along the box's own x
            return true;
        }

        /// <summary>
        /// Rotates <paramref name="start"/> about <paramref name="centre"/> by <paramref name="degrees"/> into
        /// <paramref name="result"/> (pure, for tests). False (nothing written) when a corner would leave
        /// <paramref name="room"/> (no room outline = no limit).
        /// </summary>
        public static bool RotateZone(IReadOnlyList<Vec2> start, Vec2 centre, float degrees, IReadOnlyList<Vec2> room, List<Vec2> result)
        {
            if (start == null || result == null || result.Count != start.Count) return false;
            Quaternion q = Quaternion.Euler(0f, degrees, 0f);
            for (int i = 0; i < start.Count; i++)
            {
                Vector3 r = q * new Vector3(start[i].x - centre.x, 0f, start[i].y - centre.y);
                var p = new Vec2(centre.x + r.x, centre.y + r.z);
                if (room != null && room.Count >= 3 && !Zones.Contains(room, p)) return false;
            }
            for (int i = 0; i < start.Count; i++)
            {
                Vector3 r = q * new Vector3(start[i].x - centre.x, 0f, start[i].y - centre.y);
                result[i] = new Vec2(centre.x + r.x, centre.y + r.z);
            }
            return true;
        }

        /// <summary>
        /// A zone turn's yaw (pure, for tests): the nearest <see cref="TurnStep"/>, or a room wall's direction (mod 90°)
        /// when within <see cref="WallSnapDegrees"/> of it.
        /// </summary>
        public static float SnapZoneYaw(float yaw, IReadOnlyList<Vec2> room, float pieceYaw = float.NaN)
        {
            // The desk's (or bed's) direction first, mod 90 (the human: snap onto the desk direction).
            if (!float.IsNaN(pieceYaw))
            {
                float toPiece = Mathf.DeltaAngle(0f, (Mathf.DeltaAngle(yaw, pieceYaw) % 90f + 135f) % 90f - 45f);
                if (Mathf.Abs(toPiece) <= WallSnapDegrees) return yaw + toPiece;
            }
            for (int i = 0; room != null && i < room.Count; i++)
            {
                Vec2 a = room[i], b = room[(i + 1) % room.Count];
                if ((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y) < 0.25f) continue; // short wall bits: no snap
                float wall = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                float off = Mathf.DeltaAngle(yaw, wall);
                off = Mathf.DeltaAngle(0f, (off % 90f + 135f) % 90f - 45f);
                if (Mathf.Abs(off) <= WallSnapDegrees) return yaw + off;
            }
            return Mathf.Round(yaw / TurnStep) * TurnStep;
        }

        void Press(bool isLeft, HandState hand)
        {
            int button = Pick(hand.PinchPoint, true);
            // A pinch at the card is the card's, even on no button: never a floor or zone pinch (device, build dfb22f4).
            if (button < 0 && _card != null && _card.NearCard(hand.PinchPoint, CardClaimFront, out Vector2 at))
            {
                _hands.Claim();
                InputDebug.Log($"zone: pinch at the card on no button (local {at.x:F3},{at.y:F3})");
                return;
            }
            if (button >= DoneIndex) _hands.Claim(); // a pinch on the card is the card's: never a floor pinch
            if (button == CardBodyIndex) { InputDebug.Log("zone: pinch on the card (no button)"); return; }
            if (button == BarIndex)
            {
                _cardDrag = _cardMoved = true; // moved by the user: it stays where they put it
                _dragLeft = isLeft;
                _cardDistance = Vector3.Distance(_hands.Aim(isLeft).origin, _card.Position);
                _barStartAim = _hands.Aim(isLeft).GetPoint(_cardDistance);
                _barHeld = 0f;
                return;
            }
            if (button >= DoneIndex) { InputDebug.Log($"zone: pinch on button {button - DoneIndex}"); _doneArmed = true; _armedButton = button; _doneLeft = isLeft; Refresh(); return; }
            if (!_hands.HasAim(isLeft)) { InputDebug.Log("zone: pinch, but that hand is not aiming"); MissNote(); return; }
            Ray aim = _hands.Aim(isLeft);
            if (TryBeginTurn(isLeft, aim)) { _hands.Claim(); return; } // the turn handle before the floor
            ZoneGrab grab = ZonePick.Pick(aim, hand.PinchPoint, hand.Tracked, _points, _room.FloorY, _knobHeight, out int target, out float miss);
            if (grab == ZoneGrab.None)
            {
                InputDebug.Log($"zone: pinch hit nothing; {(XrVisuals.RayFloor(aim, _room.FloorY, out _) ? "nearest corner " + miss.ToString("F2") + " m from the ray's floor point" : "ray misses the floor; closest " + (miss < float.MaxValue ? miss.ToString("F2") + " m" : "n/a") + " to a side")}; floor y {_room.FloorY:F2}");
                MissNote();
                return;
            }
            InputDebug.Log($"zone: pinch on {grab} {target}");
            if (_guard) ShowGuard(false); // editing again: Done asks again
            // Follow the floor point when the ray reaches the floor well; else keep the grabbed reach (a flat, seated ray).
            _dragFloor = ZonePick.FloorDrag(aim, _room.FloorY);
            if (grab != ZoneGrab.Corner)
            {
                // The whole zone (pinch inside it) or one edge follows the ray.
                Vector3 grabbed = XrVisuals.RayFloor(aim, _room.FloorY, out Vector3 floorHit) ? floorHit : EdgeMiddle(target);
                _dragReach = ZonePick.Reach(aim, grabbed);
                if (!ZonePick.DragPoint(aim, _room.FloorY, _dragFloor, _dragReach, out _moveFrom)) return;
                _moveEdge = grab == ZoneGrab.Edge ? target : -1;
                _dragLeft = isLeft;
                _moveStart.Clear();
                _moveStart.AddRange(_points);
                _dragLive = false;
                Refresh();
                return;
            }

            _drag = target;
            _dragLeft = isLeft;
            _dragStart = _points[target];
            _moveStart.Clear();
            _moveStart.AddRange(_points); // the rectangle at the start of the drag
            _dragLive = false;
            _dragSmooth = new Vector3(_dragStart.x, 0f, _dragStart.y);
            _dragVelocity = Vector3.zero;
            _dragFar = Vector3.Distance(hand.PinchPoint, _knobs[target].position) > _nearRadius;
            _dragReach = ZonePick.Reach(aim, _knobs[target].position);
            Vector3 anchor = _dragFar ? RayPoint(_knobs[target].position) : hand.PinchPoint;
            _dragOffset = _knobs[target].position - anchor;
            Refresh();
        }

        bool _dragLive, _dragFloor;
        float _dragReach;
        Vector3 _dragSmooth, _dragVelocity;

        Vector3 EdgeMiddle(int i)
        {
            int n = _points.Count;
            if (n == 0) return Vector3.zero;
            Vec2 a = _points[Mathf.Clamp(i, 0, n - 1)], b = _points[(Mathf.Clamp(i, 0, n - 1) + 1) % n];
            return new Vector3((a.x + b.x) * 0.5f, _room.FloorY, (a.y + b.y) * 0.5f);
        }

        /// <summary>A drag starts moving only past this (m): small hand jitter never moves the zone.</summary>
        public const float DeadZone = 0.03f, MinSide = 0.5f;

        void Follow(HandState hand)
        {
            if (hand.LowConfidence) return; // dim light: hold still rather than jump (device bug on a2c27d2)
            if (_moveEdge != NoMove)
            {
                if (!ZonePick.DragPoint(_hands.Aim(_dragLeft), _room.FloorY, _dragFloor, _dragReach, out Vector3 now)) return;
                float dx = now.x - _moveFrom.x, dz = now.z - _moveFrom.z;
                if (!_dragLive && dx * dx + dz * dz < DeadZone * DeadZone) return;
                _dragLive = true;
                int n = _points.Count;
                for (int i = 0; i < n; i++)
                {
                    if (_moveEdge >= 0 && i != _moveEdge && i != (_moveEdge + 1) % n) continue;
                    _points[i] = RoomGeometry.ClampInside(_room.RealOutline, new Vec2(_moveStart[i].x + dx, _moveStart[i].y + dz));
                    Place(i);
                }
                return;
            }
            Vector3 p = (_dragFar ? RayPoint(_knobs[_drag].position) : hand.PinchPoint) + _dragOffset;
            var target = new Vector3(p.x, 0f, p.z);
            if (!_dragLive && (target - new Vector3(_dragStart.x, 0f, _dragStart.y)).sqrMagnitude < DeadZone * DeadZone) return;
            _dragLive = true;
            _dragSmooth = OutsidePlacer.SmoothFollow(_dragSmooth, target, ref _dragVelocity, Time.deltaTime); // damped like a piece drag
            Vec2 q = RoomGeometry.ClampInside(_room.RealOutline, new Vec2(_dragSmooth.x, _dragSmooth.z));
            // The zone stays a rectangle: the dragged corner's two neighbours slide along their sides.
            if (!RectDrag(_moveStart, _drag, q, _points)) _points[_drag] = q;
            for (int i = 0; i < _points.Count; i++) Place(i);
        }

        /// <summary>
        /// Drags corner <paramref name="i"/> of the rectangle <paramref name="start"/> to <paramref name="target"/> (pure, for
        /// tests). The opposite corner stays, the two neighbours slide along the rectangle's own axes, and each side stays at
        /// least <see cref="MinSide"/>. False (nothing written) when <paramref name="start"/> is not a 4-corner rectangle.
        /// </summary>
        public static bool RectDrag(IReadOnlyList<Vec2> start, int i, Vec2 target, List<Vec2> result)
        {
            if (start == null || start.Count != 4 || result == null || result.Count != 4) return false;
            Vec2 o = start[(i + 2) % 4], n1 = start[(i + 1) % 4], n2 = start[(i + 3) % 4];
            float ax = n1.x - o.x, ay = n1.y - o.y, bx = n2.x - o.x, by = n2.y - o.y;
            float la = Mathf.Sqrt(ax * ax + ay * ay), lb = Mathf.Sqrt(bx * bx + by * by);
            if (la < 1e-3f || lb < 1e-3f || Mathf.Abs((ax * bx + ay * by) / (la * lb)) > 0.15f) return false; // not a rectangle
            ax /= la; ay /= la; bx /= lb; by /= lb;
            float dx = target.x - o.x, dy = target.y - o.y;
            float da = Mathf.Max(MinSide, dx * ax + dy * ay), db = Mathf.Max(MinSide, dx * bx + dy * by); // never folds over
            result[(i + 2) % 4] = o;
            result[(i + 1) % 4] = new Vec2(o.x + ax * da, o.y + ay * da);
            result[(i + 3) % 4] = new Vec2(o.x + bx * db, o.y + by * db);
            result[i] = new Vec2(o.x + ax * da + bx * db, o.y + ay * da + by * db);
            return true;
        }

        void EndDrag()
        {
            _touched = true;
            _drag = -1;
            _moveEdge = NoMove;
        }

        void CancelDrag()
        {
            if (_moveEdge != NoMove)
            {
                for (int i = 0; i < _points.Count && i < _moveStart.Count; i++) { _points[i] = _moveStart[i]; Place(i); }
                _moveEdge = NoMove;
            }
            else { _points[_drag] = _dragStart; Place(_drag); }
            _drag = -1;
            Debug.Log($"{LogTag} Hand lost: corner drag cancelled.");
        }

        void Confirm()
        {
            _store.Session.SetZone(_points);
            if (_turnedThisEdit) ZoneTurnRecord.Write(_store.SavePath, true);
            if (!_store.HasZone || _store.Session.State.zoneOutline.Count != _points.Count)
            {
                Debug.LogWarning($"{LogTag} Zone was not stored; keep editing.");
                return;
            }
            SetPhase(Phase.Confirmed);
            Debug.Log($"{LogTag} Zone confirmed with {_points.Count} corners.");
        }

        // Where the dragging hand's ray points on the floor (or, for a flat ray, at the grabbed reach), at the height of
        // <paramref name="at"/>. Device, build 0ddb26d: with a flat ray this returned the knob itself, so a far corner never moved.
        Vector3 RayPoint(Vector3 at) =>
            ZonePick.DragPoint(_hands.Aim(_dragLeft), _room.FloorY, _dragFloor, _dragReach, out Vector3 hit) ? new Vector3(hit.x, at.y, hit.z) : at;

        /// <summary>The button (Done, or Fit to desk / Keep anyway) under the pinch point (near) or the gaze (far); -1 for none.</summary>
        string _hintNow = Hint;
        string CardLine() => _room.HasScan ? ZoneLine : NoScanLine;

        int Pick(Vector3 pinchPoint, bool usePinch)
        {
            if (_card != null)
            {
                int hit = _card.Hit(_hands.Gaze, pinchPoint, usePinch);
                return hit < 0 ? -1 : hit == ZoneCard.Bar ? BarIndex : hit == ZoneCard.Body ? CardBodyIndex : _guard ? (hit == 0 ? FitIndex : KeepIndex) : hit == 1 ? (_cancellable ? CancelIndex : SampleIndex) : DoneIndex;
            }
            int best = -1;
            float bestNear = _nearRadius * 1.5f, bestMiss = float.MaxValue;
            for (int b = DoneIndex; b <= KeepIndex; b++)
            {
                Renderer r = Button(b);
                if (!r.gameObject.activeSelf) continue;
                float near = usePinch ? Vector3.Distance(pinchPoint, r.transform.position) : float.MaxValue;
                if (near < bestNear) { bestNear = near; best = b; }
            }
            if (best >= 0) return best;
            for (int b = DoneIndex; b <= KeepIndex; b++)
            {
                Renderer r = Button(b);
                if (!r.gameObject.activeSelf) continue;
                float miss = _hands.GazeHit(r.transform.position);
                if (miss >= 0f && miss < bestMiss) { bestMiss = miss; best = b; }
            }
            return best;
        }

        Renderer Button(int b) => b == DoneIndex ? _done : b == FitIndex ? _fit : _keep;

        void PressButton(int b)
        {
            Vector3 head = _hands.HeadPosition;
            if (b == KeepIndex) { Confirm(); return; }
            if (b == CancelIndex) { CancelEdit(); return; }
            if (b == SampleIndex) { InputDebug.Log("zone: sample room asked for"); SampleRequested?.Invoke(); return; }
            if (b == FitIndex)
            {
                ZoneFit.Contain(_points, _room.Furniture, new Vec2(head.x, head.z), _room.RealOutline);
                ZoneFit.KeepOutside(_points, _room.Furniture, new Vec2(head.x, head.z), _room.RealOutline);
                _touched = true;
                ShowGuard(false);
                ShowCorners(true);
                return;
            }
            // Done: with the desk outside the zone, ask first; the whole room would turn virtual.
            if (DeskOutside(_points, _room.Furniture, new Vec2(head.x, head.z))) ShowGuard(true);
            else Confirm();
        }

        // Fit to desk and Keep anyway take Done's place, side by side; the instruction says why.
        void ShowGuard(bool on)
        {
            if (_card != null)
            {
                _guard = on;
                if (on)
                {
                    Vector3 me = _hands.HeadPosition;
                    string word = ZoneFit.PieceWord(ZoneFit.AtPiece(_room.Furniture, new Vec2(me.x, me.z))); // desk or bed
                    // The human: Fit to desk on the left, Keep anyway on the right (as the older 3D buttons were).
                    ShowCard(OutsideLine(word), DeskQuestion, FitLabel(word), "Keep anyway", mainLeft: true);
                    InputDebug.Log($"zone: Done with the {word} outside; asking");
                }
                else ShowCard(CardLine(), ZoneMeaning, "Done", Secondary());
                return;
            }
            _guard = on;
            _done.gameObject.SetActive(!on);
            _fit.gameObject.SetActive(on);
            _keep.gameObject.SetActive(on);
            if (!on) { _fitLabel.Hide(); _keepLabel.Hide(); _doneLabel.Show("Done", DoneLabelAt(_done.transform), _hands.HeadPosition); return; }
            _doneLabel.Hide();
            Transform d = _done.transform;
            Vector3 side = d.right * (DoneSize.x * 0.5f + 0.02f);
            _fit.transform.SetPositionAndRotation(d.position - side, d.rotation);
            _keep.transform.SetPositionAndRotation(d.position + side, d.rotation);
            _fitLabel.Show("Fit to desk", DoneLabelAt(_fit.transform), _hands.HeadPosition);
            _keepLabel.Show("Keep anyway", DoneLabelAt(_keep.transform), _hands.HeadPosition);
            _hint.Show(DeskOutsideText, d.position + Vector3.up * (0.05f + 0.026f * Lines(DeskOutsideText)), _hands.HeadPosition);
            InputDebug.Log("zone: Done with the desk outside; asking");
        }

        Vector3 DoneLabelAt(Transform button) => button.position - button.forward * (DoneSize.z * 0.5f + 0.002f) + Vector3.up * 0.016f;

        void SetPhase(Phase phase)
        {
            if (phase == Phase.Editing && _room != null && _hands != null) // where the outline lies (device bug on a2c27d2: it looked desk-high)
                InputDebug.Log($"zone: editing; floor y {_room.FloorY:F2}, head y {_hands.HeadPosition.y:F2}, scan {_room.HasScan}");
            Current = phase;
            bool editing = phase == Phase.Editing;
            if (editing) { Cancelled = false; _turnedThisEdit = false; }
            if (editing)
            {
                // Done sits within reach just below the view centre, facing the user, set once per edit (no
                // forced motion); its label is on its face and the instruction above it.
                Ray gaze = _hands.View; // in front of the head, wherever a hand points
                Vector3 forward = Vector3.ProjectOnPlane(gaze.direction, Vector3.up);
                forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
                Vector3 at = gaze.origin + forward * DoneAhead + Vector3.down * DoneBelow;
                _done.transform.SetPositionAndRotation(at, Quaternion.LookRotation(forward, Vector3.up));
                _doneLabel.Show("Done", at - forward * (DoneSize.z * 0.5f + 0.002f) + Vector3.up * 0.016f, gaze.origin);
                Vector3 me = _hands.HeadPosition;
                FurnitureAnchor desk = ZoneFit.Desk(_room.Furniture, new Vec2(me.x, me.z));
                bool noChair = !_chairNoteSaid && desk != null && ZoneFit.Chair(_room.Furniture, desk) == null;
                _chairNoteSaid |= noChair; // once per launch
                string hint = HintFor(_room.HasScan, noChair);
                _hintNow = hint;
                _hint.Show(hint, at + Vector3.up * (0.05f + 0.026f * Lines(hint)), gaze.origin); // grows down to just above Done
            }
            else { _doneLabel.Hide(); _hint.Hide(); _fitLabel.Hide(); _keepLabel.Hide(); }
            _guard = false;
            _done.gameObject.SetActive(editing && _card == null);
            _fit.gameObject.SetActive(false);
            if (_card != null)
            {
                _doneLabel.Hide(); _hint.Hide();
                if (editing) { _cardMoved = false; _card.Hide(); ShowCard(CardLine(), ZoneMeaning, "Done", Secondary()); } else _card.Hide();
            }
            _keep.gameObject.SetActive(false);
            ShowCorners(editing);
            _turning = false;
            _turnHandle?.Hide();
            _hover = -1;
            _drag = -1;
            _moveEdge = NoMove;
            _doneArmed = false;
            Refresh();
            ConfirmedFromSettings = phase == Phase.Confirmed && _cancellable;
            if (!editing) _cancellable = false;
            if (phase == Phase.Confirmed) Confirmed?.Invoke();
            else if (editing) EditStarted?.Invoke();
        }

        // About 3 degrees of view at any distance (device batch 7: a corner 2-3 m away was a tiny ball); bigger when hovered.
        void ScaleKnobs()
        {
            Vector3 head = _hands.HeadPosition;
            for (int i = 0; i < _points.Count; i++)
            {
                float size = ZonePick.KnobSize(Vector3.Distance(head, _knobs[i].position));
                _knobs[i].localScale = Vector3.one * (size * (i == _hover || i == _drag ? 0.95f : 0.65f)); // smaller; full size on hover
            }
        }

        void ShowCorners(bool editing)
        {
            _line.positionCount = _points.Count;
            for (int i = 0; i < MaxCorners; i++)
            {
                bool shown = editing && i < _points.Count;
                _knobs[i].gameObject.SetActive(shown);
                _posts[i].gameObject.SetActive(shown);
                if (i < _points.Count) Place(i);
            }
            Refresh();
        }

        void Place(int i)
        {
            float floor = _room.FloorY;
            Vec2 p = _points[i];
            _line.SetPosition(i, new Vector3(p.x, floor + 0.005f, p.y));
            _knobs[i].position = new Vector3(p.x, floor + _knobHeight, p.y);
            Transform post = _posts[i].transform;
            post.position = new Vector3(p.x, floor + _knobHeight * 0.5f, p.y);
            post.localScale = new Vector3(0.01f, _knobHeight * 0.5f, 0.01f); // thin (device bug on a2c27d2: too heavy); the ball is the target
        }

        void Refresh()
        {
            bool zoneHover = _hoverGrab == ZoneGrab.Edge || _hoverGrab == ZoneGrab.Whole || _moveEdge != NoMove;
            XrVisuals.Tint(_line, zoneHover && Current != Phase.Confirmed ? AccentHover : Accent); // the accent, never green (#202)
            int pressed = !_doneArmed ? -1 : _armedButton == KeepIndex || _armedButton == SampleIndex || _armedButton == CancelIndex ? 1 : 0;
            if (_card != null) _card.SetHover(_hover == DoneIndex || _hover == FitIndex ? 0 : _hover == KeepIndex || _hover == SampleIndex || _hover == CancelIndex ? 1 : -1, pressed);
            for (int i = 0; i < _points.Count; i++)
            {
                // Quiet until hovered: a light accent at rest (the outline material is opaque, so a lighter tint stands in
                // for 60% opacity), the full accent when hovered or dragged.
                Color c = i == _drag ? AccentActive : i == _hover ? AccentHover : Color.Lerp(Accent, Color.white, 0.4f);
                XrVisuals.Tint(_knobRenderers[i], c);
                XrVisuals.Tint(_posts[i], c);
            }
            ScaleKnobs();
            for (int b = DoneIndex; b <= KeepIndex; b++) Button(b).transform.localScale = DoneSize * (_hover == b ? 1.15f : 1f);
        }

        static int Lines(string text)
        {
            int n = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') n++;
            return n;
        }

        void Copy(IReadOnlyList<Vec2> source)
        {
            _points.Clear();
            for (int i = 0; i < source.Count && i < MaxCorners; i++) _points.Add(source[i]);
        }
    }
}

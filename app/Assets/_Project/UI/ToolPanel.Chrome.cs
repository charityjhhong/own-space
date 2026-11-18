using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// The panel's chrome: the grab handle and minimize button on the top strip, the pill, and themes.
    /// <list type="bullet">
    /// <item>Minimize shrinks the panel into a pill (title + tab) over 0.25 s; pinching the pill expands it.</item>
    /// <item>Pinch and move the handle (or the pill) to move the panel; once moved it counts as
    /// <see cref="Placed"/> and stops following. Losing tracking mid-drag keeps it where it is (placed).
    /// Double-pinch the handle to send it back to its seat.</item>
    /// <item><see cref="SetTheme"/> swaps every element's tokens through property blocks (no allocation).</item>
    /// </list>
    /// </summary>
    public sealed partial class ToolPanel
    {
        internal const int Handle = Back + 1, Minimize = Back + 2, Pill = Back + 3;
        const float MinimizeSeconds = 0.25f, DoublePinchSeconds = 0.4f, DragThreshold = 0.01f;

        [SerializeField, Tooltip("Dark theme; the built-in smoked glass when empty.")] PanelStyle _dark;
        [SerializeField] Vector2 _pillSize = new Vector2(0.16f, 0.036f);
        [SerializeField] string _title = "Own Space";

        PanelElement _handle, _minimize, _pill;
        PanelStyle _lightStyle;
        string[] _tabNames = new string[0];
        bool _minimized, _dragging, _dragNear, _dragMoved;
        float _shrink, _grabDistance, _lastHandleTap = -10f;
        Vector3 _grabOffset, _grabStart;

        public bool Minimized => _minimized;
        public PanelTheme Theme { get; private set; }
        internal PanelStyle Style => _style;

        /// <summary>The panel's look for other glass cards (the zone step): its material, fonts and current theme.</summary>
        public Material GlassMaterial { get { Build(); return _material; } }
        public PanelFonts Fonts { get { Build(); return _fonts; } }
        public PanelStyle CurrentStyle => _style != null ? _style : PanelStyle.Default;

        /// <summary>Raised after the theme changes (persist it there).</summary>
        public event System.Action<PanelTheme> ThemeChanged;

        public void SetTheme(PanelTheme theme)
        {
            if (_lightStyle == null) _lightStyle = _style != null ? _style : PanelStyle.Default;
            bool changed = theme != Theme;
            Theme = theme;
            _style = theme == PanelTheme.Dark ? (_dark != null ? _dark : PanelThemes.DarkDefault) : _lightStyle;
            if (_built)
            {
                _back.SetStyle(_style); _handle.SetStyle(_style); _minimize.SetStyle(_style); _pill.SetStyle(_style);
                _undo.SetStyle(_style); _redo.SetStyle(_style); _settings.SetStyle(_style); _focus.SetStyle(_style); _toast.SetStyle(_style); _hint.SetStyle(_style);
                _hintDot.SetStyle(_style); _hintDot.SetSwatch(_style.Accent);
                _rowPrev.SetStyle(_style); _rowNext.SetStyle(_style);
                _gripLong.SetStyle(_style); _gripShort.SetStyle(_style);
                for (int i = 0; i < MaxTabs; i++) _tabs[i].SetStyle(_style);
                for (int i = 0; i < MaxCards; i++) _cards[i].SetStyle(_style);
            }
            if (changed) ThemeChanged?.Invoke(theme);
        }

        /// <summary>Shrinks the panel into a pill (or back), animated unless <paramref name="instant"/>.</summary>
        /// <summary>Raised when the panel is minimized or expanded.</summary>
        public event System.Action<bool> MinimizedChanged;

        public void SetMinimized(bool minimized, bool instant = false)
        {
            Build();
            if (minimized != _minimized) MinimizedChanged?.Invoke(minimized);
            _minimized = minimized;
            if (instant) _shrink = minimized ? 1f : 0f;
            if (minimized) _pill.SetText(_tab < _tabNames.Length ? _title + " · " + _tabNames[_tab] : _title); // on change only
            if (!minimized || instant) ShowContent(!minimized && _shrink == 0f); // minimizing hides the content at the end of the shrink
            Shape();
        }

        void BuildChrome()
        {
            if (_lightStyle == null) _lightStyle = _style != null ? _style : PanelStyle.Default;
            _handle = new PanelElement(transform, _material, _style, _fonts, "Handle", 13f);
            _minimize = new PanelElement(transform, _material, _style, _fonts, "Minimize", 14f);
            _pill = new PanelElement(transform, _material, _style, _fonts, "Pill", 14f);
            _minimize.SetText("–");
            float strip = Top - _metrics.StripTop - _metrics.StripHeight * 0.5f; // the top strip's centre line
            _handle.Place(new Vector2(0f, strip), new Vector2(0.064f, 0.008f), 0.004f);
            _minimize.Place(new Vector2(_metrics.Size.x * 0.5f - _metrics.Padding - _metrics.StripHeight * 0.5f, strip),
                new Vector2(_metrics.StripHeight, _metrics.StripHeight), _metrics.StripHeight * 0.5f);
            _pill.Place(new Vector2(0f, Top - _pillSize.y * 0.5f), _pillSize, _pillSize.y * 0.5f);
            _pill.SetVisible(false);
            BuildFeedback();
            BuildGrip();
        }

        void Animate()
        {
            float target = _minimized ? 1f : 0f;
            if (_shrink == target) return;
            _shrink = Mathf.MoveTowards(_shrink, target, Time.deltaTime / MinimizeSeconds);
            Shape();
            if (_shrink == 0f) ShowContent(true);
            else if (_shrink >= 1f) ShowContent(false);
        }

        // The surface morphs from the panel to the pill, anchored at the top (the panel grows down from the handle).
        void Shape()
        {
            float k = Mathf.SmoothStep(0f, 1f, _shrink);
            Vector2 size = Vector2.Lerp(new Vector2(_metrics.Size.x, _height), _pillSize, k);
            _back.Place(new Vector2(0f, Top - size.y * 0.5f), size, Mathf.Lerp(_style != null ? _style.SurfaceRadius : 0.02f, _pillSize.y * 0.5f, k));
            _back.SetVisible(_shrink < 1f);
            _pill.SetVisible(_shrink >= 1f);
            PlaceGrip(_shrink == 0f && _handle != null && _handle.Visible);
            PlaceHint();
        }

        void ShowContent(bool shown)
        {
            _handle.SetVisible(shown);
            _minimize.SetVisible(shown);
            PlaceGrip(shown && _shrink == 0f);
            ShowStripButtons(shown);
            for (int i = 0; i < MaxTabs; i++) _tabs[i].SetVisible(shown && i < _tabCount && !_tabsHidden);
            if (shown) Invalidate();
            else for (int i = 0; i < MaxCards; i++) _cards[i].SetVisible(false);
        }

        bool TryFindChrome(Vector2 p, float pad, out int id)
        {
            id = -1;
            if ((_minimized || _shrink > 0f) && !TryFindFeedback(p, pad, out id))
            {
                if (_shrink >= 1f && PanelLayout.Contains(_pill.Centre, _pill.Size, p, pad)) id = Pill;
                return true; // while minimized only the pill is there
            }
            if (TryFindFeedback(p, pad, out id)) return true; // undo, redo, settings (and the toast and hint)
            if (OnGrip(p, pad)) id = Resize; // the corner grip, before the cards under it
            else if (PanelLayout.Contains(_minimize.Centre, _minimize.Size, p, pad)) id = Minimize;
            else if (InStrip(p, pad)) id = Handle; // the bar is thin; the whole top strip around the buttons grabs
            return id >= 0;
        }

        // The top strip: the panel's full width, from its top edge to the strip's bottom.
        bool InStrip(Vector2 p, float pad)
        {
            float height = _metrics.StripTop + _metrics.StripHeight;
            return PanelLayout.Contains(new Vector2(0f, Top - height * 0.5f), new Vector2(_metrics.Size.x, height), p, pad);
        }

        PanelElement ChromeElement(int id) => id == Handle ? _handle : id == Minimize ? _minimize : id == Pill ? _pill : id == Resize ? _gripLong
            : id > Pill ? FeedbackElement(id) : _back;

        // ---- moving ----

        void BeginDrag(HandState hand)
        {
            Ray aim = _hands.Aim(_pressLeft);
            _dragging = true;
            _dragMoved = false;
            float z = ToLocal(hand.PinchPoint).z;
            _dragNear = hand.Tracked && z >= -NearGrabDepth && z <= 0.01f; // a near grab moves with the pinch point
            InputDebug.Log($"panel grab: {(_dragNear ? "near (pinch point)" : "far (ray)")} z={z:F3}");
            _grabDistance = Vector3.Distance(aim.origin, transform.position);
            _grabStart = transform.position;
            _grabOffset = transform.position - (_dragNear ? hand.PinchPoint : aim.GetPoint(_grabDistance));
            _ease = 1f;
        }

        void TrackDrag(HandState owner)
        {
            Vector3 p = (_dragNear ? owner.PinchPoint : _hands.Aim(_pressLeft).GetPoint(_grabDistance)) + _grabOffset;
            if (_dragMoved || (p - _grabStart).sqrMagnitude > DragThreshold * DragThreshold)
            {
                _dragMoved = true;
                p = ClampToRoom(p);
                p = OutOfReach(_hands.View.origin, p); // never dragged inside arm's reach by accident
                Quaternion face = FaceEyes(_hands.View.origin, p, transform.rotation); // faces the eyes wherever it goes
                AboveSurfaces(_hands.View.origin, ref p, ref face); // and never into a desk
                transform.SetPositionAndRotation(p, face);
            }
            if (owner.PinchEnded) Release(_pressed);
        }

        /// <summary>A dragged panel keeps at least this far from the eyes (m); the seat stays at 0.65 m, and a hand still pokes it.</summary>
        public const float MinDragDistance = 0.45f;

        /// <summary>The point pushed out to <see cref="MinDragDistance"/> from the head when nearer (pure, for tests).</summary>
        public static Vector3 OutOfReach(Vector3 head, Vector3 p)
        {
            Vector3 to = p - head;
            float d = to.magnitude;
            return d >= MinDragDistance || d < 1e-4f ? p : head + to / d * MinDragDistance;
        }

        // The same wall pull as the seat: a dragged panel is pulled toward the user until it fits in the room.
        Vector3 ClampToRoom(Vector3 p)
        {
            if (Reach == null) return p;
            Vector3 head = _hands.View.origin, flat = new Vector3(p.x - head.x, 0f, p.z - head.z);
            float d = flat.magnitude;
            if (d < 1e-3f) return p;
            float max = Reach.Reach(head, flat / d, _metrics.Size.x * 0.5f * Scale, d);
            return max < d ? p - flat / d * (d - max) : p;
        }

        // Losing tracking or cancelling mid-drag keeps the panel where it is, as placed.
        void StopDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            if (_dragMoved) { Placed = true; LogMove("the user's drag"); }
        }

        void ReleaseChrome(int id, int over)
        {
            if (id == Resize) { ReleaseResize(); return; }
            bool moved = _dragMoved && _dragging;
            StopDrag();
            if (moved || over != id) return;
            if (id > Pill) ReleaseFeedback(id);
            else if (id == Minimize) SetMinimized(true);
            else if (id == Pill) SetMinimized(false);
            else if (id == Handle)
            {
                bool second = Time.time - _lastHandleTap < DoublePinchSeconds;
                if (second) ReturnToSeat(); // double-pinch: send it back
                _lastHandleTap = second ? -10f : Time.time; // a third pinch starts a new pair
            }
        }

        internal void ReturnToSeat()
        {
            Placed = false;
            _easeFrom = transform.position;
            _rotFrom = transform.rotation;
            Seat(out _easeTo, out _rotTo);
            _ease = 0f;
        }
    }
}

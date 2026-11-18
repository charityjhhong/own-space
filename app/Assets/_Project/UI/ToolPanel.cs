using OwnSpace.Interaction;
using TMPro;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// The tool panel: a glass surface with a tab bar and a grid of cards (one <see cref="IPanelPage"/> per tab),
    /// laid out from <see cref="PanelMetrics"/>. It follows lazily (<see cref="ShouldReturn"/>) unless <see cref="Placed"/>.
    /// Input is a fingertip poke or the pinching hand's aim ray; every pinch on the panel is claimed (gaps and
    /// disabled cards too), so none reaches the room. Release over the pressed element activates it; losing
    /// tracking, pausing, losing focus or closing cancels. No allocation per frame.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed partial class ToolPanel : MonoBehaviour
    {
        public const int MaxTabs = 5, MaxCards = 24;
        internal const int Back = MaxTabs + MaxCards; // the surface itself: claimed, never activated

        [SerializeField] HandRig _hands;
        [SerializeField, Tooltip("OwnSpace/PanelGlass material, shared by every quad.")] Material _material;
        [SerializeField, Tooltip("Optional; the built-in light glass when empty.")] PanelStyle _style;
        [SerializeField, Tooltip("Inter Regular SDF; TMP's default font when empty.")] TMP_FontAsset _font;
        [SerializeField, Tooltip("Inter SemiBold SDF; bold of the regular font when empty.")] TMP_FontAsset _fontSemiBold;
        [SerializeField] PanelMetrics _metrics = PanelMetrics.Default;

        readonly PanelElement[] _tabs = new PanelElement[MaxTabs];
        readonly PanelElement[] _cards = new PanelElement[MaxCards];
        readonly bool[] _disabled = new bool[MaxCards]; // disabled or not pressable (titles)
        readonly IPanelPage[] _pages = new IPanelPage[MaxTabs];
        PanelElement _back;
        int _tabCount, _tab, _hovered = -1, _pressed = -1, _refused = -1;
        bool _pressLeft, _built;
        float _refusedUntil, _ease = 1f;
        Vector3 _easeFrom, _easeTo;
        Quaternion _rotFrom, _rotTo;
        CardInfo _info;

        public int CurrentTab => _tab;
        internal int Pressed => _pressed;
        public PanelMetrics Metrics => _metrics;

        /// <summary>True once the user has put the panel somewhere; lazy follow then pauses.</summary>
        public bool Placed { get; set; }

        /// <summary>Keeps the panel in front of walls; optional.</summary>
        public IPanelReach Reach { get; set; }

        static bool IsTab(int id) => id >= 0 && id < MaxTabs;
        static bool IsCard(int id) => id >= MaxTabs && id < Back;

        /// <summary>Sets the tab names (at most <see cref="MaxTabs"/>) and their pages.</summary>
        IPanelPage _sheet;

        /// <summary>A page shown in place of the current tab's (the gear's settings), or null. Selecting a tab closes it.</summary>
        public IPanelPage Sheet => _sheet;

        public void ShowSheet(IPanelPage page) { _sheet = page; ShowTabStates(); Invalidate(); }

        bool _tabsHidden;

        /// <summary>
        /// The guided first run: the tab row is hidden (the sheet's own header takes its place) and the page starts where
        /// the tabs were. Off: the tabs come back. Used with <see cref="ShowSheet"/>.
        /// </summary>
        public bool TabsHidden
        {
            get => _tabsHidden;
            set
            {
                if (value == _tabsHidden) return;
                _tabsHidden = value;
                for (int i = 0; i < MaxTabs; i++) _tabs[i].SetVisible(!value && i < _tabCount && !_minimized);
                Invalidate();
            }
        }

        float BodyTop => _tabsHidden ? _metrics.TabTop : _metrics.BodyTop;

        /// <summary>World position of card <paramref name="index"/> of the current page (onboarding points at it).</summary>
        public Vector3 CardPosition(int index) => _built && index >= 0 && index < MaxCards ? _cards[index].Root.position : transform.position;
        public void CloseSheet() { if (_sheet == null) return; _sheet = null; ShowTabStates(); Invalidate(); }

        IPanelPage Current => _sheet ?? (_tabCount > 0 ? _pages[_tab] : null);

        public void Configure(string[] tabNames, IPanelPage[] pages)
        {
            Build();
            _tabCount = Mathf.Min(MaxTabs, tabNames.Length);
            _tabNames = tabNames;
            float width = _metrics.Size.x - 2f * _metrics.Padding, y = Top - _metrics.TabTop - _metrics.TabHeight * 0.5f;
            for (int i = 0; i < MaxTabs; i++)
            {
                _pages[i] = i < _tabCount && i < pages.Length ? pages[i] : null;
                _tabs[i].SetVisible(i < _tabCount && !_minimized && !_tabsHidden);
                if (i >= _tabCount) continue;
                _tabs[i].SetText(tabNames[i]);
            }
            // Tabs as wide as their words (spec: the tabs never ellipsize): each gets its text width plus an equal share
            // of what is left; if the words alone do not fit, they share in proportion and the text shrinks (12 mm min).
            float gap = 0f, room = width - gap * (_tabCount - 1), text = 0f; // no gaps: the tabs have no fill of their own to keep apart
            for (int i = 0; i < _tabCount; i++) text += _tabWidths[i] = _tabs[i].PreferredWidth(tabNames[i]) + TabPadding;
            float x = -width * 0.5f;
            for (int i = 0; i < _tabCount; i++)
            {
                float w = text <= room ? _tabWidths[i] + (room - text) / _tabCount : _tabWidths[i] * room / text;
                _tabs[i].Place(new Vector2(x + w * 0.5f, y), new Vector2(w, _metrics.TabHeight));
                x += w + gap;
            }
            SelectTab(0);
        }

        /// <summary>Raised when the shown tab changes (the host drops an armed card when Furniture is left).</summary>
        public event System.Action<int> TabChanged;

        /// <summary>World position of tab <paramref name="tab"/>'s button (onboarding points at it).</summary>
        public Vector3 TabPosition(int tab) => _built && tab >= 0 && tab < _tabCount ? _tabs[tab].Root.position : transform.position;

        public void SelectTab(int tab)
        {
            int before = _tab;
            _sheet = null; // a tab always shows its own page
            _tab = Mathf.Clamp(tab, 0, Mathf.Max(0, _tabCount - 1));
            if (_tab != before) TabChanged?.Invoke(_tab);
            ShowTabStates();
            Invalidate();
        }

        // While a sheet (Settings) shows, no tab is selected and the gear is lit (device shot 233031: "Tidy up" stayed lit).
        void ShowTabStates()
        {
            for (int i = 0; i < _tabCount; i++) _tabs[i].SetState(i == _tab && _sheet == null ? ElementState.Selected : ElementState.Normal);
            if (_settings != null) _settings.SetState(_sheet != null ? ElementState.Selected : ElementState.Normal);
        }

        /// <summary>Re-describes the current page's cards (call when its data changes).</summary>
        public void Invalidate() => Relayout(false);

        // Sub-category rows wider than the panel scroll sideways (spec 7.2), clipped and faded without a Mask.
        const int MaxRows = 16;
        // A scrolling row clips hard (EdgeSoft is anti-aliasing, not a fade: a fade over the white glass washed the
        // pills white, device batch 4) and only on an edge that hides something; that edge shows a chevron button.
        const float ScrollSlop = 0.006f, EdgeSoft = 0.0015f, RowArrow = 0.024f;
        int _arrowRow = -1;
        readonly float[] _rowScroll = new float[MaxRows];
        readonly bool[] _rowScrolls = new bool[MaxRows];
        readonly float[] _rowTotals = new float[MaxRows]; // a scrolling row's full width, from this layout
        readonly int[] _cardRow = new int[MaxCards];
        readonly CardInfo[] _infos = new CardInfo[MaxCards]; // this layout's Describe results: one call per card
        readonly float[] _cardLeft = new float[MaxCards];    // a hugging card's left edge from its row's start
        int _count;
        int _scrollRow = -1;
        float _scrollStartX, _scrollStart;
        bool _scrolling;

        internal float RowScroll(int row) => row >= 0 && row < MaxRows ? _rowScroll[row] : 0f;
        bool Scrolls(int row) => row >= 0 && row < MaxRows && _rowScrolls[row];

        void Relayout(bool keepPress)
        {
            if (!_built) return;
            System.Array.Clear(_rowScrolls, 0, MaxRows);
            _arrowRow = -1;
            IPanelPage page = Current;
            int count = page != null ? Mathf.Min(page.Count, MaxCards) : 0, columns = page != null ? Mathf.Max(1, page.Columns) : 1;
            float width = _metrics.Size.x - 2f * _metrics.Padding, column = width / columns, y = Top - BodyTop, rowHeight = 0f;
            int row = int.MinValue, col = 0;
            float rowRight = float.MinValue; // the right edge of the row's controls, greyed ones too (the grip check)
            CardKind rowKind = CardKind.Chip;
            float rowGap = -1f; // a page's own gap under the current row; below 0 = the kind's
            float cursor = -width * 0.5f;
            _count = count;
            for (int i = 0; i < count; i++)
            {
                _infos[i] = new CardInfo { Title = "" };
                page.Describe(i, ref _infos[i]);
                if (_infos[i].Kind == CardKind.Chip && _infos[i].Width > IconChipWidth && _infos[i].Width < MinChipWidth) _infos[i].Width = MinChipWidth;
            }
            for (int i = 0; i < MaxCards; i++)
            {
                _cards[i].SetVisible(i < count && !_minimized);
                if (i >= count) continue;
                _info = _infos[i];
                _disabled[i] = _info.Disabled || _info.Kind == CardKind.Title;
                if (_info.Row != row || (_info.Width <= 0f && col >= columns)) // a new row (or a full one wraps) starts below the previous one
                {
                    float total = RowWidth(i, count, _info.Row);
                    bool scrolls = !_info.AlignRight && !_info.AlignCenter && _info.Width > 0f && total > width + 1e-4f && _info.Row >= 0 && _info.Row < MaxRows;
                    if (scrolls) ScrollRow(i, count, _info.Row, total, width);
                    if (_info.Row >= 0 && _info.Row < MaxRows) { _rowScrolls[_info.Row] = scrolls; _rowTotals[_info.Row] = total; }
                    if (scrolls && _arrowRow < 0) _arrowRow = _info.Row; // the chevrons serve the first scrolling row
                    cursor = _info.AlignRight ? width * 0.5f - total : _info.AlignCenter ? -total * 0.5f : -width * 0.5f + (scrolls ? _rowScroll[_info.Row] : 0f);
                    if (row != int.MinValue) y -= rowHeight + (rowGap >= 0f ? rowGap : RowGap(rowKind));
                    rowGap = _info.RowGapSet ? _info.RowGapBelow : -1f;
                    row = _info.Row;
                    col = 0;
                    rowRight = float.MinValue;
                    rowHeight = RowMaxHeight(i, count, _info.Row);
                    rowKind = _info.Kind;
                }
                bool fullWidth = _info.Kind == CardKind.Title && _info.Width <= 0f && i + 1 < count && _infos[i + 1].Row != row;
                bool hug = _info.Width > 0f;
                // Cards run edge to edge with the gap between them (mockups round 2: three 84 mm cards across 268 mm).
                bool card = !hug && _info.Kind == CardKind.Card;
                float cardWidth = (width - (columns - 1) * _metrics.Gap) / columns;
                var size = new Vector2(hug ? _info.Width : fullWidth ? width : card ? cardWidth : (_info.Kind == CardKind.Chip ? column - _metrics.Gap : column), CardHeight(_info));
                float x = hug ? cursor + size.x * 0.5f : fullWidth ? 0f : card ? -width * 0.5f + cardWidth * 0.5f + col * (cardWidth + _metrics.Gap) : -width * 0.5f + column * (col + 0.5f);
                cursor += size.x + _metrics.Gap;
                if (_info.Kind != CardKind.Title) rowRight = Mathf.Max(rowRight, x + size.x * 0.5f);
                _cardRow[i] = row;
                _cardLeft[i] = x - size.x * 0.5f + width * 0.5f - (hug && Scrolls(row) ? _rowScroll[row] : 0f);
                _cards[i].SetKind(_info.Kind);
                _cards[i].SetLines(_info.Kind == CardKind.Title ? _info.Lines : 1);
                _cards[i].SetCentred(_info.AlignCenter);
                _cards[i].SetTextStyle(_info.Header ? PanelElement.TextStyle.Header : _info.Body ? PanelElement.TextStyle.Body : _info.Caption ? PanelElement.TextStyle.Caption : PanelElement.TextStyle.Section);
                _cards[i].Place(new Vector2(x, y - rowHeight * 0.5f), size); // one centre line per row: a title and its toggles share a baseline
                _cards[i].SetThumb(_info.Thumb);
                _cards[i].SetIcon(_info.Icon, _info.IconSide);
                col++;
                _cards[i].SetText(_info.Title);
                _cards[i].SetSwatch(_info.Swatch, _info.Swatch2);
                _cards[i].SetDecor(_info.Pattern, _info.PatternColor, _info.Glyph);
                _cards[i].SetDanger(_info.Danger);
                _cards[i].SetState(StateOf(_info));
                if (hug && Scrolls(row)) ClipToRow(i, x, size.x, width, row);
                else _cards[i].SetClip(false);
                // Never past the bottom: a card that would hang below the tallest panel is hidden (and so not pressable).
                if (y - rowHeight < Top - _metrics.Size.y + _metrics.Padding * 0.5f - 1e-4f) _cards[i].SetVisible(false);
            }
            if (!keepPress) _hovered = _pressed = -1;
            PlaceRowArrows();
            // The surface follows the content (it grows down from the top strip), up to Size.y.
            float bottom = count > 0 ? y - rowHeight : Top - BodyTop;
            _height = Mathf.Clamp(Top - bottom + BottomPadding(rowRight), BodyTop + _metrics.Padding, _metrics.Size.y);
            Shape();
            ShowUndoStates();
            // A relayout never moves the panel (device bug on f1d4fe0: every tab change moved a pinned panel). The surface
            // grows down from a fixed top; the desk clamp runs on seat, drag, resize release and keyboard open only.
        }

        float _extraBelow;

        /// <summary>
        /// Height (m, panel-local) of something docked under the panel, such as the AI keyboard. The desk clamp keeps it
        /// clear of surfaces too (device batch 10: the open keyboard sank into the desk). Setting it re-clamps at once.
        /// </summary>
        public float ExtraBelow
        {
            get => _extraBelow;
            set { if (Mathf.Approximately(_extraBelow, value)) return; _extraBelow = Mathf.Max(0f, value); KeepAboveSurfaces("the keyboard"); }
        }

        float _extraBelowWidth;

        /// <summary>Width (m, panel-local) of what is docked under the panel; the clamp checks the wider of the two.</summary>
        public float ExtraBelowWidth
        {
            get => _extraBelowWidth;
            set { if (Mathf.Approximately(_extraBelowWidth, value)) return; _extraBelowWidth = Mathf.Max(0f, value); KeepAboveSurfaces("the keyboard"); }
        }

        /// <summary>The clamp's half width (panel-local): the panel's, or a wider docked piece's while one is there.</summary>
        public static float ClampHalfWidth(float panelWidth, float extraBelow, float extraWidth) =>
            0.5f * (extraBelow > 0f ? Mathf.Max(panelWidth, extraWidth) : panelWidth);

        /// <summary>The clamp's bottom edge (panel-local y): under the content, and under anything docked below it.</summary>
        public static float ClampBottom(float top, float height, float extraBelow) => top - height - Mathf.Max(0f, extraBelow);

        // Lifts the panel above furniture now (not while it is being moved or eased in).
        internal void KeepAboveSurfaces(string reason)
        {
            if (Surfaces == null || _hands == null || _dragging || _ease < 1f) return;
            Vector3 p = transform.position;
            Quaternion r = transform.rotation;
            AboveSurfaces(_hands.View.origin, ref p, ref r);
            if (p == transform.position) return;
            transform.SetPositionAndRotation(p, r);
            LogMove(reason);
        }

        /// <summary>Every move the code makes is logged ("panel: moved by …"), so a jump on device can be traced.</summary>
        internal static void LogMove(string reason) => InputDebug.Log("panel: moved by " + reason);

        float _height;
        const float TabPadding = 0.012f; // a tab hugs its bold text + 12 mm, spread across the inner width
        /// <summary>A text button is never narrower than this (mockups round 2: short words do not shrink below a thumb); icon-size chips keep their size.</summary>
        public const float MinChipWidth = 0.072f, IconChipWidth = 0.037f;
        readonly float[] _tabWidths = new float[MaxTabs];
        PanelFonts _fonts;

        /// <summary>The surface's current height (content-fitted).</summary>
        public float Height => _height;

        // A wrapping title is as tall as its lines.
        float CardHeight(in CardInfo info) => info.Height > 0f ? info.Height : RowHeight(info.Kind) * (info.Kind == CardKind.Title && info.Lines > 1 ? info.Lines : 1);

        float RowHeight(CardKind kind) => kind == CardKind.Title ? _metrics.TitleHeight : kind == CardKind.Toggle ? _metrics.ToggleHeight
            : kind == CardKind.Chip ? _metrics.CardHeight : kind == CardKind.Card ? _metrics.TileHeight : _metrics.SwatchHeight;

        // A card in a scrolling row: hidden when fully outside; the exact edge clip only while it crosses an edge,
        // so cards inside keep one constant clip and are not re-faded every frame of a drag.
        void ClipToRow(int i, float x, float w, float width, int row)
        {
            RowWindow(row, width, out float lo, out float hi);
            const float Eps = 1e-5f;
            bool outside = x + w * 0.5f <= lo + Eps || x - w * 0.5f >= hi - Eps;
            _cards[i].SetVisible(!outside && !_minimized);
            if (outside) return;
            bool inside = x - w * 0.5f >= lo - Eps && x + w * 0.5f <= hi + Eps;
            if (inside) _cards[i].SetClip(true, -1f, 1f, EdgeSoft);
            else _cards[i].SetClip(true, lo - x, hi - x, EdgeSoft);
        }

        /// <summary>Whether a scrolling row hides items past its left / right edge (pure, for tests).</summary>
        public static void HiddenEnds(float scroll, float total, float width, out bool left, out bool right)
        {
            left = total > width + 1e-4f && scroll < -1e-4f;
            right = total > width + 1e-4f && scroll > width - total + 1e-4f;
        }

        // The visible span of a scrolling row (panel x): the inner width, less a chevron's room on each hiding edge.
        void RowWindow(int row, float width, out float lo, out float hi)
        {
            HiddenEnds(_rowScroll[row], _rowTotals[row], width, out bool left, out bool right);
            float room = RowArrow + _metrics.Gap * 0.5f;
            lo = -width * 0.5f + (left ? room : 0f);
            hi = width * 0.5f - (right ? room : 0f);
        }

        /// <summary>Scrolls a row by about two thirds of its width (the chevrons). No allocation.</summary>
        internal void ScrollRowBy(int row, int direction)
        {
            if (!Scrolls(row)) return;
            float width = _metrics.Size.x - 2f * _metrics.Padding;
            _rowScroll[row] = Mathf.Clamp(_rowScroll[row] - direction * width * 0.66f, width - _rowTotals[row], 0f);
            MoveRow(row);
        }

        // During a drag only the dragged row moves: positions and clips from the cached layout, no Describe.
        void MoveRow(int row)
        {
            float width = _metrics.Size.x - 2f * _metrics.Padding, start = -width * 0.5f + _rowScroll[row];
            for (int i = 0; i < _count; i++)
            {
                if (_cardRow[i] != row || _infos[i].Width <= 0f) continue;
                float w = _infos[i].Width, x = start + _cardLeft[i] + w * 0.5f;
                _cards[i].Place(new Vector2(x, _cards[i].Centre.y), _cards[i].Size);
                ClipToRow(i, x, w, width, row);
            }
            PlaceRowArrows();
        }

        // Clamps a scrolling row and, unless the user is dragging it, scrolls it so its selected item is in view
        // (so the next Invalidate after a drag brings the selected item back into view).
        void ScrollRow(int i, int count, int row, float total, float width)
        {
            float scroll = Mathf.Clamp(_rowScroll[row], width - total, 0f);
            if (!_scrolling)
            {
                float left = 0f;
                for (int j = i; j < count; j++)
                {
                    CardInfo c = _infos[j];
                    if (c.Row != row) break;
                    if (c.Selected)
                    {
                        float room = RowArrow + _metrics.Gap * 0.5f; // clear of a chevron on either side
                        if (left > 0f && left + scroll < room) scroll = room - left;
                        if (left + c.Width + scroll > width - room) scroll = width - room - left - c.Width;
                        break;
                    }
                    left += c.Width + _metrics.Gap;
                }
            }
            _rowScroll[row] = Mathf.Clamp(scroll, width - total, 0f);
        }

        // The total width of a row of content-hugging items starting at card i (for a right-aligned row).
        float RowTotal(int row)
        {
            for (int i = 0; i < _count; i++) if (_cardRow[i] == row) return RowWidth(i, _count, row);
            return 0f;
        }

        // A card's hit rect, clipped like its look: the part of a scrolled chip past the row edge is not pressable.
        bool CardContains(int i, Vector2 p, float pad)
        {
            Vector2 c = _cards[i].Centre, s = _cards[i].Size;
            Vector4 clip = _cards[i].Clip;
            float lo = c.x - s.x * 0.5f - pad, hi = c.x + s.x * 0.5f + pad;
            if (clip.w > 0f) { lo = Mathf.Max(lo, c.x + clip.x); hi = Mathf.Min(hi, c.x + clip.y); }
            return p.x >= lo && p.x <= hi && Mathf.Abs(p.y - c.y) <= s.y * 0.5f + pad;
        }

        // The row's height: its tallest card (a title beside taller toggles is centred on them).
        float RowMaxHeight(int i, int count, int row)
        {
            float h = 0f;
            for (int j = i; j < count && _infos[j].Row == row; j++) h = Mathf.Max(h, CardHeight(_infos[j]));
            return h;
        }

        float RowWidth(int i, int count, int row)
        {
            float total = 0f;
            for (int j = i; j < count && _infos[j].Row == row; j++) total += _infos[j].Width + (j > i ? _metrics.Gap : 0f);
            return total;
        }

        float RowGap(CardKind kind) => kind == CardKind.Title ? _metrics.TitleGap : kind == CardKind.Chip || kind == CardKind.Card ? _metrics.Gap : _metrics.RowGap;


        /// <summary>Opens the panel on its seat (following again), or closes it (cancelling any press).</summary>
        public void Open(bool open)
        {
            Build();
            CancelPress();
            if (open)
            {
                Seat(out Vector3 p, out Quaternion r);
                transform.SetPositionAndRotation(p, r);
                LogMove("opening on its seat");
                if (HeadReady != null && !HeadReady()) _reseatPending = "opening with the head not ready (a headset on the desk)";
                _ease = 1f;
                Placed = false;
                SetMinimized(false, instant: true);
            }
            gameObject.SetActive(open);
        }

        float Top => _metrics.Size.y * 0.5f;

        void Build()
        {
            if (_built) return;
            _built = true;
            _fonts = new PanelFonts(_font, _fontSemiBold);
            _height = _metrics.Size.y;
            _back = new PanelElement(transform, _material, _style, _fonts, "Surface", 14f, surface: true);
            for (int i = 0; i < MaxTabs; i++) _tabs[i] = new PanelElement(transform, _material, _style, _fonts, "Tab", 14f) { MinTextMm = 12f, TextPadding = TabPadding, Bold = true, Flat = true }; // bold labels; only the chosen tab is filled
            for (int i = 0; i < MaxCards; i++) _cards[i] = new PanelElement(transform, _material, _style, _fonts, "Card", 14f);
            BuildChrome();
            Shape();
        }

        void OnEnable() => Listen(true);
        void OnDisable() { CancelPress(); Listen(false); }
        void OnDestroy() => _fonts?.Dispose();

        // A system recenter brings the panel back in front of the user, placed or not.
        void Listen(bool on)
        {
            if (_hands == null) return;
            _hands.Recentered -= OnRecentered;
            _hands.RecenterSettled -= OnRecenterSettled;
            if (on) { _hands.Recentered += OnRecentered; _hands.RecenterSettled += OnRecenterSettled; }
        }

        // The recenter itself cancels a press; the seat is taken once the head pose is the new one (RecenterSettled).
        void OnRecentered() { if (_built) CancelPress(); }

        void OnRecenterSettled() => CatchUpRecenters();

        int _recentersSeen;

        // Every settled recenter re-seats once, also one that came while the panel was hidden (device, e3f28ec: the headset
        // was put on with the panel hidden by system UI; it came back where the headset had sat on the desk).
        void CatchUpRecenters()
        {
            if (_hands == null || _hands.SettledRecenters == _recentersSeen) return;
            _recentersSeen = _hands.SettledRecenters;
            Reseat("a recenter");
        }

        /// <summary>
        /// Whether the head can seat the panel (MR sets it): tracked and at least 0.9 m above the floor, not a headset lying
        /// on the desk. Null = always.
        /// </summary>
        public System.Func<bool> HeadReady { get; set; }

        string _reseatPending; // a re-seat asked for while the head could not seat it; done once it can

        /// <summary>Whether a re-seat waits for the head (pure): asked for, and the head is not ready yet.</summary>
        public static bool ReseatWaits(bool asked, bool headReady) => asked && !headReady;

        /// <summary>
        /// Back on the seat after a recenter or a change of the floor level (the tracking origin moved), unless the user placed
        /// it and it is still in view (then it stays where they put it).
        /// </summary>
        public void Reseat(string why)
        {
            if (!_built || !isActiveAndEnabled) return;
            if (ReseatWaits(true, HeadReady == null || HeadReady())) { _reseatPending = why; return; }
            _reseatPending = null;
            Ray view = _hands != null ? _hands.View : new Ray(transform.position - transform.forward * 0.6f, transform.forward);
            if (!ReseatsAfterOriginChange(Placed, view.origin, view.direction, transform.position, _metrics)) return;
            ReturnToSeat();
            LogMove("returning to its seat after " + why);
        }

        /// <summary>
        /// Whether the panel re-seats after the tracking origin moved (pure, for tests): always when not placed; when placed,
        /// only if it ended up out of view (<see cref="AwayAngle"/>) or lost (past MaxDistance).
        /// </summary>
        public static bool ReseatsAfterOriginChange(bool placed, Vector3 head, Vector3 viewDirection, Vector3 panel, PanelMetrics m) =>
            !placed || Vector3.Angle(viewDirection, panel - head) > AwayAngle || (panel - head).sqrMagnitude > m.MaxDistance * m.MaxDistance;
        void OnApplicationPause(bool paused) { if (paused) CancelPress(); }
        void OnApplicationFocus(bool focused) { if (!focused) CancelPress(); }

        internal HandRig Hands
        {
            get => _hands;
            set { Listen(false); _hands = value; Listen(isActiveAndEnabled); }
        }

        /// <summary>True while a hand points at the panel or presses it (so others leave its tab alone).</summary>
        public bool Interacting => isActiveAndEnabled && (_pressed >= 0 || _pointedAt);
        bool _pointedAt;

        void Update() => Tick();

        internal void Tick()
        {
            if (!_built || _hands == null) return;
            CatchUpRecenters();
            if (_reseatPending != null && (HeadReady == null || HeadReady())) Reseat(_reseatPending + ", once the head was ready");
            HandState right = _hands.Right, left = _hands.Left;
            // What a hand points at, before the follow: a pointed-at panel does not move this frame.
            int over = _pressed < 0 ? HitAny(right, left) : -1;
            _pointedAt = over >= 0;
            Follow();
            Animate();
            TickFeedback();
            if (_refused >= 0 && Time.time >= _refusedUntil) { int r = _refused; _refused = -1; Restore(r); }

            bool started = right.PinchStarted || left.PinchStarted;
            if (_pressed >= 0)
            {
                if (started) InputDebug.Log($"panel pinch: ignored, press {_pressed} still open");
                TrackPress(_pressLeft ? left : right);
                return;
            }
            SetHover(over);
            if (!started) return;
            if (_hands.ClaimedThisFrame) { InputDebug.Log("panel pinch: already claimed this frame"); return; }
            bool isLeft = !right.PinchStarted;
            HandState hand = isLeft ? left : right;
            int hit = Hit(hand, isLeft);
            bool claimed = hit >= 0 && _hands.Claim(); // every pinch on the panel is claimed
            LogPinch(hand, isLeft, hit, claimed);
            if (!claimed) return;
            if (hit != Back) BeginPress(hit, isLeft);
            if (IsCard(hit) && Scrolls(_cardRow[hit - MaxTabs]) && PointerLocal(hand, isLeft, out Vector2 at)) BeginScroll(_cardRow[hit - MaxTabs], at.x);
            if (hit == Handle || hit == Pill) BeginDrag(hand);
            if (hit == Resize) BeginResize(hand);
        }

        internal void BeginPress(int id, bool isLeft) { _pressed = id; _pressLeft = isLeft; Element(id).SetState(ElementState.Pressed); }

        void TrackPress(HandState owner)
        {
            if (!owner.Tracked || owner.TrackingLost) { CancelPress(); return; }
            if (_dragging) { TrackDrag(owner); return; }
            if (_resizing) { TrackResize(owner); return; }
            if (_scrollRow >= 0 && TrackScroll(owner)) return;
            int over = Hit(owner, _pressLeft);
            // A card pinched and dragged off the panel (still pinching): a page that takes it starts a drag outside.
            if (over < 0 && !owner.PinchEnded && IsCard(_pressed) && Current is IPanelDragOut drag && drag.DragOut(_pressed - MaxTabs))
            {
                bool left = _pressLeft;
                CancelPress();
                Invalidate();
                CardDraggedOut?.Invoke(left);
                return;
            }
            if (owner.PinchEnded) Release(over);
            else Element(_pressed).SetState(over == _pressed ? ElementState.Pressed : ElementState.Hover);
        }

        /// <summary>Ends the press; activates the pressed element if <paramref name="over"/> is still it.</summary>
        /// <summary>Raised when a card was dragged off the panel and its page took it (true = the left hand holds it).</summary>
        public event System.Action<bool> CardDraggedOut;

        internal void Release(int over)
        {
            int id = _pressed;
            if (id < 0) return;
            _pressed = _hovered = -1;
            Restore(id);
            if (id > Back) { ReleaseChrome(id, over); return; }
            if (over != id) return; // released off the element: nothing happens
            if (IsTab(id)) SelectTab(id);
            else if (IsCard(id) && !_disabled[id - MaxTabs] && Current != null)
            {
                if (Current.Press(id - MaxTabs)) Invalidate();
                else Refuse(id);
            }
        }

        void CancelPress() { StopDrag(); StopResize(); _scrollRow = -1; _scrolling = false; int id = _pressed; _pressed = _hovered = -1; if (id >= 0) Restore(id); }

        internal void BeginScroll(int row, float x) { _scrollRow = row; _scrollStartX = x; _scrollStart = _rowScroll[row]; _scrolling = false; }

        // A sideways move past the slop turns the press into a scroll of its row; a scroll never activates.
        bool TrackScroll(HandState owner)
        {
            if (PointerLocal(owner, _pressLeft, out Vector2 at)) DragScroll(at.x);
            if (!_scrolling) return false;
            if (owner.PinchEnded) { int id = _pressed; _pressed = _hovered = _scrollRow = -1; _scrolling = false; Restore(id); }
            return true;
        }

        internal void DragScroll(float x)
        {
            float dx = x - _scrollStartX;
            if (!_scrolling && Mathf.Abs(dx) < ScrollSlop) return;
            if (!_scrolling && _pressed >= 0) Element(_pressed).SetState(ElementState.Normal);
            _scrolling = true;
            float width = _metrics.Size.x - 2f * _metrics.Padding;
            _rowScroll[_scrollRow] = Mathf.Clamp(_scrollStart + dx, width - RowTotal(_scrollRow), 0f);
            MoveRow(_scrollRow);
        }

        // Where the pinching hand points on the panel: its poke point, else its aim ray.
        bool PointerLocal(HandState hand, bool isLeft, out Vector2 local)
        {
            if (hand.Tracked)
            {
                Vector3 p = ToLocal(hand.PinchPoint);
                if (p.z >= -_metrics.PokeDepth && p.z <= 0.01f) { local = new Vector2(p.x, p.y); return true; }
            }
            return RayLocal(_hands.Aim(isLeft), out local, out _);
        }

        // Spec v3 section 6: the refused look holds for 0.6 s.
        const float RefusedSeconds = 0.6f;

        void Refuse(int id) { Element(id).SetState(ElementState.Refused); _refused = id; _refusedUntil = Time.time + RefusedSeconds; }

        void SetHover(int hover)
        {
            if (hover == Back) hover = -1;
            if (hover == _hovered) return;
            if (_hovered >= 0) Restore(_hovered);
            _hovered = hover;
            if (hover >= 0 && Element(hover).State == ElementState.Normal) Element(hover).SetState(ElementState.Hover);
        }

        void Restore(int id)
        {
            if (id == _refused || id == Back) return;
            PanelElement e = Element(id);
            if (IsTab(id)) e.SetState(id == _tab && _sheet == null ? ElementState.Selected : ElementState.Normal);
            else if (id == SettingsButton) e.SetState(_sheet != null ? ElementState.Selected : ElementState.Normal);
            else if (id == UndoButton) e.SetState(UndoOn ? ElementState.Normal : ElementState.Disabled);
            else if (id == RedoButton) e.SetState(RedoOn ? ElementState.Normal : ElementState.Disabled);
            else if (!IsCard(id) || Current == null) e.SetState(ElementState.Normal);
            else { _info = default; Current.Describe(id - MaxTabs, ref _info); e.SetState(StateOf(_info)); }
        }

        internal static ElementState StateOf(CardInfo info) =>
            info.Strong ? (info.Disabled ? ElementState.Normal : ElementState.Selected) // still readable when off; not pressable either way
            : info.Disabled ? ElementState.Disabled : info.Selected ? ElementState.Selected : ElementState.Normal;

        internal PanelElement Element(int id) => id > Back ? ChromeElement(id) : IsTab(id) ? _tabs[id] : IsCard(id) ? _cards[id - MaxTabs] : _back;

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void LogPinch(HandState hand, bool isLeft, int hit, bool claimed)
        {
            Ray aim = _hands.Aim(isLeft);
            bool onPlane = RayLocal(aim, out Vector2 local, out float distance);
            Vector3 pinch = ToLocal(hand.PinchPoint);
            int under = Find(new Vector2(pinch.x, pinch.y));
            // near: the pinch point in panel space (z < 0 = in front) and what is under it; a poke needs z in
            // [-PokeDepth, 0.01], a handle grab z in [-NearGrabDepth, 0.01].
            InputDebug.Log($"panel pinch: hand={(isLeft ? "left" : "right")} tracked={hand.Tracked} " +
                $"near=local {pinch.x:F3},{pinch.y:F3} z {pinch.z:F3} over id {under} " +
                $"aim={(_hands.GazeFromHand ? "hand/controller ray" : "head ray")} " +
                $"origin={aim.origin:F3} dir={aim.direction:F3} panel={transform.position:F3} active={isActiveAndEnabled} " +
                $"plane={(onPlane ? $"local {local.x:F3},{local.y:F3} at {distance:F2} m" : "missed")} " +
                $"hit={(hit >= 0 ? "yes id " + hit : "no")} claimed={(claimed ? "yes" : "no")} frame={Time.frameCount}");
        }

        // Hover and press share one rule (the human: "no hover shows; I can't select unless I go right up to it"): a hand near
        // the panel hovers and presses the element under its fingers (NearHover); beyond that, the ray's hit.
        int HitAny(HandState right, HandState left)
        {
            int id = right.Tracked ? Near(right.PinchPoint) : -1;
            if (id < 0 && left.Tracked) id = Near(left.PinchPoint);
            return id >= 0 ? id : Far(_hands.Gaze);
        }

        int Hit(HandState hand, bool isLeft)
        {
            int id = hand.Tracked ? Near(hand.PinchPoint) : -1;
            return id >= 0 ? id : Far(_hands.Aim(isLeft));
        }

        // ---- scale (the resize grip): every world <-> panel conversion goes through these ----

        public const float MinScale = 0.8f, MaxScale = 1.6f;

        /// <summary>The panel's uniform scale (0.8-1.6); its layout stays in unscaled metres.</summary>
        public float Scale { get; private set; } = 1f;

        /// <summary>Raised when the user finishes a resize or resets it (persist it there).</summary>
        public event System.Action<float> ScaleChanged;

        public static float ClampScale(float s) => float.IsNaN(s) || s <= 0f ? 1f : Mathf.Clamp(s, MinScale, MaxScale);

        /// <summary>Sets the scale (clamped). Hit tests, poke depths, the banner and toasts scale with it.</summary>
        public void SetScale(float scale)
        {
            Scale = ClampScale(scale);
            transform.localScale = Vector3.one * Scale;
        }

        void NotifyScale() => ScaleChanged?.Invoke(Scale);

        /// <summary>A world point in unscaled panel space (z &lt; 0 in front): pokes and grabs scale with the panel.</summary>
        Vector3 ToLocal(Vector3 world) => PanelLayout.ToLocal(world, transform.position, transform.rotation) / Scale;

        bool RayLocal(Ray ray, out Vector2 local, out float distance)
        {
            bool on = PanelLayout.RayToPanel(ray, transform.position, transform.rotation, out local, out distance);
            local /= Scale;
            return on;
        }

        /// <summary>How far in front of the top strip a pinch still grabs the handle (a near grab, like a system window).</summary>
        internal const float NearGrabDepth = 0.08f;

        /// <summary>
        /// The element a hand's pinch point touches: anything within <see cref="PanelMetrics.PokeDepth"/> of the
        /// surface, and the handle (the top strip) from up to <see cref="NearGrabDepth"/> in front of it; -1 otherwise.
        /// </summary>
        /// <summary>Whether a hand's pinch point is touching the panel (the hand ray is hidden meanwhile).</summary>
        public bool Poking(Vector3 point) => isActiveAndEnabled && !_minimized && Poke(point) >= 0;

        /// <summary>A hand this far in front of the panel (or <see cref="NearBehind"/> behind it), over an element, hovers and presses it.</summary>
        public const float NearHover = 0.08f, NearBehind = 0.03f;

        /// <summary>The element under a hand near the panel (hover and press alike); else the poke rule (the handle's deeper grab).</summary>
        internal int Near(Vector3 point)
        {
            int id = Poke(point);
            if (id >= 0) return id;
            Vector3 local = ToLocal(point);
            return NearDepth(local.z) ? Find(new Vector2(local.x, local.y)) : -1;
        }

        /// <summary>Whether a local depth (z &lt; 0 in front, unscaled m) is in the near band (pure, for tests).</summary>
        public static bool NearDepth(float z) => z >= -NearHover && z <= NearBehind;

        internal int Poke(Vector3 point)
        {
            Vector3 local = ToLocal(point);
            if (local.z > 0.01f || local.z < -NearGrabDepth) return -1;
            int id = Find(new Vector2(local.x, local.y));
            return local.z >= -_metrics.PokeDepth || id == Handle ? id : -1;
        }

        int Far(Ray ray)
        {
            if (!RayLocal(ray, out Vector2 local, out float distance)) return -1;
            int id = Find(local);
            if (id >= 0) _hands.ReportHover(ray.GetPoint(distance - 0.005f), 0f); // the panel is in front of any world target
            return id;
        }

        /// <summary>The element at panel-local <paramref name="p"/>: a tab, an enabled card, <see cref="Back"/> anywhere else on the surface, or -1 off it.</summary>
        internal int Find(Vector2 p)
        {
            float pad = _metrics.HitPad;
            if (TryFindChrome(p, pad, out int chrome)) return chrome;
            if (!PanelLayout.Contains(_back.Centre, _back.Size, p)) return -1;
            for (int i = 0; i < _tabCount && !_tabsHidden; i++) if (PanelLayout.Contains(_tabs[i].Centre, _tabs[i].Size, p, pad)) return i;
            for (int i = 0; i < MaxCards; i++)
                if (_cards[i].Visible && CardContains(i, p, pad)) return _disabled[i] ? Back : MaxTabs + i;
            return Back;
        }

        // ---- lazy follow ----

        /// <summary>A placed panel this far off the view centre (degrees) for <see cref="AwaySeconds"/> eases back to the seat.</summary>
        public const float AwayAngle = 50f, AwaySeconds = 3f;

        /// <summary>
        /// Seconds a placed panel has been out of view (pure, for tests): grows by <paramref name="dt"/> while the
        /// panel is more than <see cref="AwayAngle"/> off the view centre, back to 0 once it is in view.
        /// </summary>
        public static float AwayTime(float away, float dt, Vector3 head, Vector3 viewDirection, Vector3 panel) =>
            Vector3.Angle(viewDirection, panel - head) > AwayAngle ? away + dt : 0f;

        /// <summary>A panel out of view and this far (m) from its seat for <see cref="LostSeconds"/> s comes back, placed or not.</summary>
        public const float LostFromSeat = 1f, LostSeconds = 2f;

        /// <summary>
        /// Seconds the panel has been lost (pure, for tests): grows while it is more than <see cref="AwayAngle"/> off the
        /// view centre and more than <see cref="LostFromSeat"/> from where its seat is now; back to 0 otherwise.
        /// </summary>
        public static float LostTime(float lost, float dt, Vector3 head, Vector3 viewDirection, Vector3 panel, Vector3 seat) =>
            Vector3.Angle(viewDirection, panel - head) > AwayAngle && (panel - seat).sqrMagnitude > LostFromSeat * LostFromSeat ? lost + dt : 0f;

        float _lostFor;

        // Where the seat is now: the last seat held in the view's yaw frame, or the default seat.
        Vector3 SeatNow(Ray view) => _seatOffset.sqrMagnitude > 1e-6f
            ? view.origin + Quaternion.LookRotation(Flat(view.direction), Vector3.up) * _seatOffset
            : HeadSeat(view.origin, view.direction, _metrics.Ahead, _metrics, Scale);

        void Follow()
        {
            // Never moves while a hand points at it, pokes it or pinches it (device day: it slid away from the hand).
            if (_pressed >= 0 || _pointedAt) return;
            Ray view = _hands.View;
            _lostFor = _ease >= 1f ? LostTime(_lostFor, Time.deltaTime, view.origin, view.direction, transform.position, SeatNow(view)) : 0f;
            if (_lostFor >= LostSeconds && (HeadReady == null || HeadReady()))
            {
                _lostFor = 0f;
                Vector3 was = transform.position;
                ReturnToSeat();
                LogMove($"returning to its seat: out of view and {Vector3.Distance(was, _easeTo):F2} m from it for {LostSeconds:0} s");
            }
            if (_ease >= 1f)
            {
                // Unplaced: the lazy follow. Placed: stays put while in view, comes back once it has been out of
                // view for a few seconds (the user moved on) or is lost far away.
                // A panel the user moved stays exactly there (device batch 9): only a double-pinch on the handle or a
                // system recenter brings it back. The unplaced panel keeps its lazy follow.
                if (Placed || !ShouldReturn(false, view.origin, view.direction, transform.position, _seatOffset, _metrics)) return;
                if (HeadReady != null && !HeadReady()) return; // never seated from a headset lying on the desk
                Placed = false;
                _easeFrom = transform.position;
                _rotFrom = transform.rotation;
                Vector3 drift = transform.position;
                Seat(out _easeTo, out _rotTo);
                _ease = 0f;
                LogMove($"returning to its seat ({Vector3.Distance(drift, _easeTo):F2} m off)");
            }
            _ease = Mathf.Min(1f, _ease + Time.deltaTime / Mathf.Max(0.01f, _metrics.EaseSeconds));
            float k = Mathf.SmoothStep(0f, 1f, _ease);
            transform.SetPositionAndRotation(Vector3.Lerp(_easeFrom, _easeTo, k), Quaternion.Slerp(_rotFrom, _rotTo, k));
        }

        /// <summary>An unplaced panel this close to its seat (m) never follows, however small the seat's angles are.</summary>
        public const float MinDrift = 0.12f;

        /// <summary>Follow rule (pure): placed returns only past LostDistance; else past MaxDistance or MaxYaw/MaxPitch off the seat.</summary>
        public static bool ShouldReturn(bool placed, Vector3 head, Vector3 viewDirection, Vector3 panel, PanelMetrics m) =>
            ShouldReturn(placed, head, viewDirection, panel, Vector3.zero, m);

        /// <summary>The follow rule against a seat held at <paramref name="seatOffset"/> in the view's yaw frame (zero = the default seat).</summary>
        public static bool ShouldReturn(bool placed, Vector3 head, Vector3 viewDirection, Vector3 panel, Vector3 seatOffset, PanelMetrics m)
        {
            Vector3 to = panel - head;
            if (placed) return false; // pinned where the user put it
            if (to.sqrMagnitude > m.MaxDistance * m.MaxDistance) return true;
            Vector3 seat = seatOffset.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(Flat(viewDirection), Vector3.up) * seatOffset
                : SeatPosition(head, viewDirection, m.Ahead, m) - head;
            // A seat close to the eyes (the desk clamp brings it toward the user) turns the angle limits into a few
            // centimetres: small head motions at the desk likely re-seated it every second (device log). Within MinDrift of
            // the seat it stays, whatever the angles.
            if ((to - seat).sqrMagnitude < MinDrift * MinDrift) return false;
            Vector3 flatTo = Vector3.ProjectOnPlane(to, Vector3.up), flatSeat = Vector3.ProjectOnPlane(seat, Vector3.up);
            if (flatTo.sqrMagnitude > 1e-6f && Vector3.Angle(flatTo, flatSeat) > m.MaxYaw) return true;
            return Mathf.Abs(Elevation(to) - Elevation(seat)) > m.MaxPitch;
        }

        static float Elevation(Vector3 v) => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).sqrMagnitude < 1e-6f ? Vector3.forward : new Vector3(v.x, 0f, v.z).normalized;

        /// <summary>Panel centre for a seat whose top edge is <paramref name="reach"/> ahead and Below under the eyes, tilted by Tilt.</summary>
        /// <summary>Looking up more than this (reclined on a bed), the seat follows the view's pitch.</summary>
        public const float ReclineDegrees = 30f;

        public static Vector3 SeatPosition(Vector3 head, Vector3 viewDirection, float reach, PanelMetrics m)
        {
            // Reclined on a bed (product rule: the desk or the bed), the panel sits along the view, not at eye level ahead
            // over the user's knees; the desk clamp then keeps it above the bed.
            if (viewDirection.sqrMagnitude > 1e-6f && Vector3.Angle(Flat(viewDirection), viewDirection) > ReclineDegrees && viewDirection.y > 0f)
                return head + viewDirection.normalized * reach;
            Vector3 f = Flat(viewDirection);
            float t = m.Tilt * Mathf.Deg2Rad, half = m.Size.y * 0.5f;
            return head + f * (reach - Mathf.Sin(t) * half) + Vector3.down * (m.Below + Mathf.Cos(t) * half);
        }

        /// <summary>
        /// The panel's rotation at <paramref name="panel"/>: its face square to the eyes (yaw and pitch toward the
        /// head), so it reads without keystone seated or standing, seated or dragged anywhere (pure, for tests).
        /// </summary>
        public static Quaternion FaceEyes(Vector3 head, Vector3 panel, Quaternion fallback)
        {
            Vector3 d = panel - head;
            return d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d, Vector3.up) : fallback;
        }

        // The seat: from the head only (0.65 m ahead, a little right of the view centre), never from a hand (device
        // day: a hand-anchored seat moved with every hand motion). Computed on open, recenter and a follow return;
        // kept inside the room and above the desk.
        void Seat(out Vector3 position, out Quaternion rotation)
        {
            Ray view = _hands != null ? _hands.View : new Ray(Vector3.zero, Vector3.forward);
            Vector3 f = Flat(view.direction);
            float reach = Reach != null ? Reach.Reach(view.origin, f, _metrics.Size.x * 0.5f * Scale, _metrics.Ahead) : _metrics.Ahead;
            position = HeadSeat(view.origin, view.direction, reach, _metrics, Scale);
            rotation = FaceEyes(view.origin, position, Quaternion.LookRotation(f, Vector3.up));
            AboveSurfaces(view.origin, ref position, ref rotation);
            _seatOffset = Quaternion.Inverse(Quaternion.LookRotation(f, Vector3.up)) * (position - view.origin);
        }

        /// <summary>Where the panel would seat now (in front, a little right and low), without moving it: the zone card uses the same seat.</summary>
        public void SeatPose(out Vector3 position, out Quaternion rotation)
        {
            Ray view = _hands != null ? _hands.View : new Ray(Vector3.zero, Vector3.forward);
            Vector3 f = Flat(view.direction);
            position = HeadSeat(view.origin, view.direction, _metrics.Ahead, _metrics, Scale);
            rotation = FaceEyes(view.origin, position, Quaternion.LookRotation(f, Vector3.up));
        }

        Vector3 _seatOffset; // the last seat in the view's yaw frame (x right, y up, z ahead); zero = the default seat

        /// <summary>The seat's centre is this far right of the view centre (scaled with the panel).</summary>
        public const float SeatRight = 0.12f;

        /// <summary>The panel's seat from the head alone (pure, for tests): the usual seat, <see cref="SeatRight"/> to the right.</summary>
        public static Vector3 HeadSeat(Vector3 head, Vector3 viewDirection, float reach, PanelMetrics m, float scale) =>
            SeatPosition(head, viewDirection, reach, m) + Vector3.Cross(Vector3.up, Flat(viewDirection)) * (SeatRight * scale);

        /// <summary>Furniture tops the panel stays above (MR sets it); null = none.</summary>
        public IPanelSurfaces Surfaces { get; set; }

        /// <summary>The panel's bottom edge stays this far above a desk (or other furniture) top under it.</summary>
        public const float SurfaceClearance = 0.05f;

        void AboveSurfaces(Vector3 head, ref Vector3 position, ref Quaternion rotation)
        {
            if (Surfaces == null) return;
            position = ClampAbove(position, rotation, ClampBottom(Top, _height, _extraBelow) * Scale, Top * Scale, ClampHalfWidth(_metrics.Size.x, _extraBelow, _extraBelowWidth) * Scale, head, Surfaces);
            rotation = FaceEyes(head, position, rotation);
        }

        /// <summary>
        /// The panel's position kept above furniture (pure, for tests): its bottom edge (left, centre, right at local y
        /// <paramref name="bottomY"/>) stays <see cref="SurfaceClearance"/> above any top under it. It rises; if that
        /// would put its top edge more than 0.1 m above the eyes, it comes toward the user first (up to 0.4 m), then
        /// rises what is still needed. Never left below a surface.
        /// </summary>
        public static Vector3 ClampAbove(Vector3 position, Quaternion rotation, float bottomY, float topY, float halfWidth, Vector3 head, IPanelSurfaces surfaces)
        {
            if (surfaces == null) return position;
            float need = Need(position, rotation, bottomY, halfWidth, surfaces);
            if (need <= 0f) return position;
            float topLimit = head.y + 0.1f;
            Vector3 raised = position + Vector3.up * need;
            if ((raised + rotation * new Vector3(0f, topY, 0f)).y <= topLimit) return raised;
            Vector3 toUser = new Vector3(head.x - position.x, 0f, head.z - position.z);
            if (toUser.sqrMagnitude > 1e-6f)
            {
                toUser.Normalize();
                for (float d = 0.05f; d <= 0.4f + 1e-4f; d += 0.05f)
                {
                    Vector3 nearer = position + toUser * d;
                    float rest = Need(nearer, rotation, bottomY, halfWidth, surfaces);
                    if (rest <= 0f) return nearer;
                    if ((nearer + Vector3.up * rest + rotation * new Vector3(0f, topY, 0f)).y <= topLimit) return nearer + Vector3.up * rest;
                }
            }
            return raised; // never below the surface, even if high
        }

        static float Need(Vector3 position, Quaternion rotation, float bottomY, float halfWidth, IPanelSurfaces surfaces)
        {
            float need = 0f;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 p = position + rotation * new Vector3(k * halfWidth, bottomY, 0f);
                if (surfaces.TopUnder(p, out float top)) need = Mathf.Max(need, top + SurfaceClearance - p.y);
            }
            return need;
        }
    }
}

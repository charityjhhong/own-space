using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using TMPro;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>Picks a piece to place outside: pinch a card, then the floor (OutsidePlacer).</summary>
    public interface IOutsideArm
    {
        CatalogItem Armed { get; }
        void Arm(CatalogItem item);
        /// <summary>The selected placed piece's name, or null (a quick pinch on a piece selects it).</summary>
        string SelectedName { get; }
        bool RemoveSelected();
        void Deselect();
    }

    /// <summary>The my-zone redesign target: the selected real box and the designs that fit it (EditMode).</summary>
    /// <summary>The fine-tuning of a redesigned real piece.</summary>
    public enum AdjustAction { Turn, Wider, Narrower, Deeper, Shallower, Reset, RealTop }

    public interface IRedesignTarget
    {
        FurnitureAnchor Selected { get; }
        IReadOnlyList<(CatalogItem item, FitResult fit)> Fits { get; }
        /// <summary>The design applied on the selected box, or null when it is kept real.</summary>
        string CurrentDesignId { get; }
        /// <summary>Applies design <paramref name="index"/> of <see cref="Fits"/>; false with a short reason when refused.</summary>
        bool ApplyDesign(int index, out string reason);
        /// <summary>Keeps the selected real piece as it is.</summary>
        bool KeepReal();
        /// <summary>Leaves the selected box (back to placing outside).</summary>
        void Deselect();
        /// <summary>Fine-tunes the design on the selected box (device batch 10); false when it cannot (no design, a limit).</summary>
        bool AdjustSelected(AdjustAction action);
        /// <summary>False when that action is at its limit (the button greys out and the hint says why).</summary>
        bool CanAdjust(AdjustAction action);
        /// <summary>Whether the selected desk or bed keeps its real top showing through the design (<see cref="RealTops"/>).</summary>
        bool RealTopOn { get; }
    }

    /// <summary>
    /// The panel's Furniture tab (spec v4 section 7.2): a scrolling row of sub-category text toggles, a grid of
    /// 3 x 2 cards (content's thumbnail and the name), and a pager. Outside, pinching a card arms it and the next
    /// floor pinch places it; with a real box selected in my zone, the categories lock to its role and the cards are
    /// the designs that fit it (pinch one to apply). All strings are built once; Describe never allocates.
    /// </summary>
    public sealed class FurniturePage : IPanelPage, IPanelDragOut
    {
        public static readonly string[] Categories = { "Seating", "Beds", "Tables", "Storage", "Lights", "Decor", "Window" };
        public const int CardsPerPage = 6;
        const int Cards = 7, Prev = Cards + CardsPerPage, PageLabel = Prev + 1, Next = Prev + 2, Keep = Next + 1, Leave = Keep + 1, Note = Leave + 1,
            Total = Note + 1, MaxPages = 12;
        public const string NoFit = "No designs fit this piece", KeepRealLabel = "Keep real", LeaveLabel = "Place outside",
            EmptyZone = "No furniture in your zone. Edit my zone in Settings.",
            NoSpace = "No space outside your zone. Shrink it in Settings > Edit my zone.";

        /// <summary>Add only: true when there is too little free floor outside the zone to place anything (the host's room check).</summary>
        public Func<bool> NoOutsideSpace { get; set; }

        /// <summary>
        /// Which job the page does (device day: picking a desk card placed a desk outside instead of restyling the real
        /// one). MyFurniture restyles the selected real piece and never arms floor placement; AddFurniture only places
        /// pieces outside; Both is the older single tab (a selection switches it to restyling).
        /// </summary>
        public enum Job { Both, MyFurniture, AddFurniture }
        readonly Job _job;

        /// <summary>The line that says why the other categories are greyed: designs that fit the selected piece.</summary>
        public static string FitNote(FurnitureRole role) => FitNotes[(int)role < FitNotes.Length && role >= 0 ? (int)role : 0];
        static readonly string[] FitNotes =
        {
            "Designs that fit this piece", "Designs that fit this desk", "Designs that fit this bed",
            "Designs that fit this storage", "Designs that fit this couch",
        };

        readonly IOutsideArm _outside;
        readonly IRedesignTarget _redesign;
        readonly FurnitureThumbs _thumbs;
        readonly List<CatalogItem>[] _outsideItems = new List<CatalogItem>[7];
        readonly List<CatalogItem> _shown = new List<CatalogItem>(32);
        readonly float[] _widths = new float[7];
        readonly Dictionary<string, string> _names = new Dictionary<string, string>(); // card names split over two lines, once per item
        readonly string[] _pageLabels = new string[MaxPages * MaxPages];
        readonly float _arrowWidth, _labelWidth, _keepWidth, _leaveWidth;
        int _category, _page;
        FurnitureAnchor _lastSelected;

        /// <summary>Called when the page's data changes on its own (selection); the panel re-describes.</summary>
        public Action Changed { get; set; }

        /// <summary>Why the last card was refused (for the toast), or null.</summary>
        public string LastRefusal { get; private set; }

        /// <summary>Called with the reason when a design is refused (the panel's red toast).</summary>
        public Action<string> Refused { get; set; }

        /// <summary>The pager's chevron icons (PanelIcons); zero = the ‹ › text.</summary>
        public Vector4 PrevIcon { get; set; }
        public Vector4 NextIcon { get; set; }

        // Keep the real one, Place outside instead and the fit note are the last items, only in my zone, so outside
        // mode has no empty rows.
        public int Count => Adjusting ? AdjustItems.Length : Empty ? Cards + 1 : MyZone ? Total + TopItems : PieceCard ? SelHint + 1 : Keep;

        // ---- My room: fine-tune the applied design (device batch 10: the scanned box is imperfect) ----
        // Turning is on the piece itself (RotateHandle at its corner), not a panel button (device batch 10).
        public const string AdjustLabel = "Adjust fit", AdjustTitle = "Fit the design to your piece",
            NudgeHint = "Drag the piece to move it, or its corner handle to turn it";
        static readonly string[] AdjustItems = { AdjustTitle, "Reset", "Wider", "Narrower", "Deeper", "Shallower", NudgeHint, "Done" };
        static readonly AdjustAction[] AdjustActs = { AdjustAction.Reset, AdjustAction.Reset, AdjustAction.Wider,
            AdjustAction.Narrower, AdjustAction.Deeper, AdjustAction.Shallower, AdjustAction.Reset, AdjustAction.Reset };
        static readonly int[] AdjustRows = { 0, 1, 2, 2, 3, 3, 4, 5 };
        const int AdjustHint = 6, AdjustDone = 7;
        readonly float[] _adjustWidths = new float[8];

        // ---- My room: keep the desk's or bed's real top (the human: "default: choosing a desk design covers
        // everything; below it, add an option to show only the real top, so I can choose it"). One off-by-default toggle
        // under the design pager, above Keep real | Adjust fit: the row's name and an On / Off button, as Settings' rows.
        // Off: the design covers the whole piece. On: the real top shows flat through the design. It drives the same
        // per-piece setting and undo step as before (AdjustAction.RealTop). Hidden, taking no room, when the piece is kept
        // real or has no usable top.
        public const string RealTopLabel = "Keep real top", TopOnLabel = "On", TopOffLabel = "Off";
        const int TopItems = 2, TopRow = 30;
        float _topLabelWidth, _topButtonWidth;
        bool TopChoice => MyZone && _redesign.CurrentDesignId != null && _redesign.CanAdjust(AdjustAction.RealTop);
        // The fit note gives way when the toggle is shown over two rows of cards: the page must fit 460 mm.
        bool NoteHidden => TopChoice && _shown.Count - _page * CardsPerPage > 3;

        void DescribeTop(int k, ref CardInfo card)
        {
            card.Row = TopRow;
            if (!TopChoice)
            {
                card.Kind = CardKind.Title; card.Title = ""; card.Width = 0.001f; card.Height = 0.0001f;
                card.RowGapSet = true; card.RowGapBelow = 0f; // no row at all
                return;
            }
            if (k == 0) { card.Kind = CardKind.Title; card.Body = true; card.Title = RealTopLabel; card.Width = _topLabelWidth; card.Height = 0.036f; return; }
            bool on = _redesign.RealTopOn;
            card.Kind = CardKind.Chip;
            card.Title = on ? TopOnLabel : TopOffLabel;
            card.Width = _topButtonWidth;
            card.Selected = on; // On is the theme's chosen pill; Off a plain chip
        }

        bool PressTop(int k) => k == 1 && TopChoice && _redesign.AdjustSelected(AdjustAction.RealTop);

        public const string SmallestWidth = "Smallest width", SmallestDepth = "Smallest depth", LargestWidth = "Largest width",
            LargestDepth = "Largest depth";
        // The hint at a limit, one per combination of the four limits (built once, so Describe never allocates).
        static readonly string[] LimitHints = BuildLimitHints();

        static string[] BuildLimitHints()
        {
            var hints = new string[16];
            string[] parts = { SmallestWidth, SmallestDepth, LargestWidth, LargestDepth };
            for (int mask = 0; mask < 16; mask++)
            {
                string h = null;
                for (int b = 0; b < 4; b++) if ((mask & (1 << b)) != 0) h = h == null ? parts[b] : h + ", " + parts[b]; // no middle dot in labels
                hints[mask] = h ?? NudgeHint;
            }
            return hints;
        }

        /// <summary>The hint for the limits reached: <see cref="NudgeHint"/> when none.</summary>
        public static string LimitHint(bool narrowest, bool shallowest, bool widest, bool deepest) =>
            LimitHints[(narrowest ? 1 : 0) | (shallowest ? 2 : 0) | (widest ? 4 : 0) | (deepest ? 8 : 0)];
        bool _adjusting;
        bool Adjusting => _adjusting && _job == Job.MyFurniture && MyZone && _redesign.CurrentDesignId != null;

        void DescribeAdjust(int index, ref CardInfo card)
        {
            card.Row = AdjustRows[index];
            card.Title = AdjustItems[index];
            if (index == AdjustHint)
                card.Title = LimitHint(!_redesign.CanAdjust(AdjustAction.Narrower), !_redesign.CanAdjust(AdjustAction.Shallower),
                    !_redesign.CanAdjust(AdjustAction.Wider), !_redesign.CanAdjust(AdjustAction.Deeper));
            if (index == 0 || index == AdjustHint) { card.Kind = CardKind.Title; card.Width = FullWidth; card.Lines = index == AdjustHint ? 2 : 1; return; }
            card.Kind = CardKind.Chip;
            card.Width = _adjustWidths[index];
            card.Selected = index == AdjustDone;
            // At a limit the button is grey (disabled), not a red refusal (device: Shallower showed red and did nothing).
            if (index >= 2 && index < AdjustHint) card.Disabled = !_redesign.CanAdjust(AdjustActs[index]);
        }

        bool PressAdjust(int index)
        {
            if (index == AdjustDone) { _adjusting = false; return true; }
            if (index == 0 || index == AdjustHint) return false;
            if (!_redesign.CanAdjust(AdjustActs[index])) return false;
            return _redesign.AdjustSelected(AdjustActs[index]);
        }

        // Add: a placed piece selected in the room (a quick pinch) gets a card under the grid: its name, Remove, Done, and
        // how to move and turn it (device batch 7: the room's unlabelled row of balls is gone; batch 10: turning is on the piece).
        const int SelTitle = Keep, SelRemove = Keep + 1, SelDone = Keep + 2, SelHint = Keep + 3;
        public const string RemoveLabel = "Remove", DoneLabel = "Done", MoveHint = "Drag it to move, or its corner handle to turn it";
        bool PieceCard => _job == Job.AddFurniture && _outside?.SelectedName != null;
        string _selectedTitle, _selectedFor;
        float _removeWidth = 0.08f, _doneWidth = 0.065f;

        // "Selected: <name>", built when the selection changes (not per Describe).
        string SelectedTitle()
        {
            string name = _outside.SelectedName;
            if (!ReferenceEquals(name, _selectedFor)) { _selectedFor = name; _selectedTitle = "Selected: " + name; }
            return _selectedTitle;
        }
        public int Columns => 3;
        public int Category => _category;
        public int Page => _page;

        public FurniturePage(ICatalog catalog, IOutsideArm outside, IRedesignTarget redesign, FurnitureThumbs thumbs, TMP_FontAsset font = null, Job job = Job.Both)
        {
            _job = job;
            _outside = outside;
            _redesign = redesign;
            _thumbs = thumbs;
            for (int c = 0; c < 7; c++) _outsideItems[c] = new List<CatalogItem>();
            for (int i = 0; catalog != null && i < catalog.Items.Count; i++)
            {
                CatalogItem item = catalog.Items[i];
                int c = CategoryOf(item.kind);
                if (c >= 0 && item.AllowedIn("outside")) _outsideItems[c].Add(item);
            }
            PanelElement.MeasureWidths(Categories, PanelElement.BodyMm, font, _widths);
            for (int c = 0; c < 7; c++) _widths[c] += 0.008f; // a little air around each text toggle
            _arrowWidth = 0.036f; // device batch 10: the pager was nearly invisible
            _labelWidth = PanelElement.MeasureWidth("12 / 12", PanelElement.SectionMm, font) + 0.012f;
            _keepWidth = PanelElement.MeasureWidth(KeepRealLabel, PanelElement.BodyMm, font) + 0.024f;
            _leaveWidth = PanelElement.MeasureWidth(LeaveLabel, PanelElement.BodyMm, font) + 0.024f;
            _adjustLabelWidth = PanelElement.MeasureWidth(AdjustLabel, PanelElement.BodyMm, font) + 0.024f;
            for (int i = 0; i < AdjustItems.Length; i++) _adjustWidths[i] = PanelElement.MeasureWidth(AdjustItems[i], PanelElement.BodyMm, font) + 0.024f;
            _removeWidth = PanelElement.MeasureWidth(RemoveLabel, PanelElement.BodyMm, font) + 0.024f;
            _doneWidth = PanelElement.MeasureWidth(DoneLabel, PanelElement.BodyMm, font) + 0.024f;
            _topButtonWidth = ToolPanel.MinChipWidth;
            _topLabelWidth = FullWidth - _topButtonWidth - 0.008f; // the button on the content edge, as in Settings
            _font = font;
            for (int p = 0; p < MaxPages; p++)
                for (int n = 1; n <= MaxPages; n++) _pageLabels[p * MaxPages + n - 1] = $"{p + 1} / {n}";
            Refresh();
        }

        /// <summary>The category of a catalog kind (spec 7.2), or -1.</summary>
        public static int CategoryOf(string kind)
        {
            switch (kind)
            {
                case "sofa": case "armchair": return 0;
                case "bed": return 1;
                case "desk": case "coffee-table": return 2;
                case "closet": case "shelf": return 3;
                case "lamp": case "pendant-lamp": return 4;
                case "plant": case "rug": case "wall-art": case "clutter-cover": return 5;
                case "curtain": case "door-trim": return 6;
                default: return -1;
            }
        }

        // The panel's inner width (0.30 m less 2 x 16 mm padding): a message line spans it and wraps.
        const float FullWidth = 0.268f;
        string _noFitText = NoFit;

        /// <summary>The category for a real piece's role (pure, for tests).</summary>
        public static int CategoryOfRole(FurnitureRole role) => role switch
        {
            FurnitureRole.Couch => 0, FurnitureRole.Bed => 1, FurnitureRole.Table => 2, FurnitureRole.Storage => 3, _ => 5,
        };

        /// <summary>Why a real piece has no design (built on a selection change): "No design fits this desk (1.20 × 0.60 m)".</summary>
        public static string NoFitFor(FurnitureAnchor piece)
        {
            if (piece == null) return NoFit;
            string what = piece.Role switch
            {
                FurnitureRole.Table => "desk", FurnitureRole.Bed => "bed", FurnitureRole.Couch => "couch", FurnitureRole.Storage => "storage", _ => "piece",
            };
            return $"No design fits this {what} ({piece.Box.size.x:0.00} × {piece.Box.size.z:0.00} m)";
        }

        void OutlineNoFit() => _noFitText = _shown.Count == 0 ? NoFitFor(_redesign?.Selected) : NoFit;

        bool MyZone => _job != Job.AddFurniture && _redesign?.Selected != null;
        bool Empty => (_job == Job.MyFurniture && !MyZone) || Full; // nothing to restyle, or no floor to add on
        bool Full => _job == Job.AddFurniture && NoOutsideSpace != null && NoOutsideSpace();

        /// <summary>A card's text width (m): a third of the inner width less the gaps, less the 6 mm insets and 2 mm of slack.</summary>
        public const float CardNameWidth = (0.268f - 2f * 0.008f) / 3f - 2f * 0.006f - 0.002f;
        TMP_FontAsset _font;

        string CardName(CatalogItem item)
        {
            // First show only. Wrapped at the card's own text width (device shot 222215: "Birch compact d..."): names are never cut.
            if (!_names.TryGetValue(item.id, out string name)) _names[item.id] = name = PanelElement.WrapToWidth(item.name, CardNameWidth, PanelElement.CaptionMm, _font);
            return name;
        }
        int Pages => Mathf.Clamp((_shown.Count + CardsPerPage - 1) / CardsPerPage, 1, MaxPages);

        /// <summary>Rebuilds the shown items after a selection change (call from the selection event; allocation-free).</summary>
        public void Refresh()
        {
            _shown.Clear();
            if (MyZone)
            {
                var fits = _redesign.Fits;
                for (int i = 0; i < fits.Count; i++) _shown.Add(fits[i].item);
                // Locked to the selected piece's role, even when no design fits (device batch 9: it showed Seating).
                _category = CategoryOfRole(_redesign.Selected.Role);
                if (_shown.Count > 0) _category = Mathf.Max(0, CategoryOf(_shown[0].kind));
                OutlineNoFit();
            }
            else _shown.AddRange(_outsideItems[_category]);
            if (_redesign?.Selected != _lastSelected) { _page = 0; _adjusting = false; }
            _lastSelected = _redesign?.Selected;
            _page = Mathf.Clamp(_page, 0, Pages - 1);
        }

        float _adjustLabelWidth = 0.1f;

        public void Describe(int index, ref CardInfo card)
        {
            if (Adjusting) { DescribeAdjust(index, ref card); return; }
            if (Empty)
            {
                // Row 0 stays as a quiet spacer; one line says why there is nothing to restyle.
                card.Kind = CardKind.Title; card.Row = index < Cards ? 0 : 1; card.Width = index < Cards ? 0.001f : FullWidth;
                card.Title = index == Cards ? (Full ? NoSpace : EmptyZone) : ""; card.Lines = index == Cards ? 2 : 0;
                return;
            }
            // My room: the top choice's three items come right after the pager; the rest keep their places after them.
            if (MyZone && index > Next) { if (index <= Next + TopItems) { DescribeTop(index - Next - 1, ref card); return; } index -= TopItems; }
            if (index < Cards)
            {
                card.Title = Categories[index];
                card.Kind = CardKind.Toggle;
                card.Row = 0;
                card.Width = _widths[index];
                card.Selected = index == _category;
                card.Disabled = MyZone && index != _category;
                return;
            }
            if (index < Prev)
            {
                int i = _page * CardsPerPage + index - Cards;
                card.Kind = CardKind.Card;
                card.Row = 1 + (index - Cards) / 3;
                if (i >= _shown.Count)
                {
                    // An empty slot is invisible (a text-only, not pressable item); a box with no design says so once.
                    card.Kind = CardKind.Title;
                    bool says = MyZone && _shown.Count == 0 && index == Cards;
                    card.Title = says ? _noFitText : "";
                    card.Lines = says ? 2 : 0;
                    card.Width = says ? FullWidth : 0.001f; // the line spans the panel and wraps; empty slots take no room
                    return;
                }
                CatalogItem item = _shown[i];
                card.Title = CardName(item);
                card.Thumb = _thumbs != null ? _thumbs.Rect(item.id) : Vector4.zero;
                card.Selected = MyZone ? _redesign.CurrentDesignId == item.id : _outside?.Armed == item;
                return;
            }
            card.Kind = CardKind.Chip;
            if (PieceCard && index >= SelTitle)
            {
                if (index == SelTitle) { card.Kind = CardKind.Title; card.Row = 4; card.Title = SelectedTitle(); return; }
                if (index == SelHint) { card.Kind = CardKind.Title; card.Row = 6; card.Title = MoveHint; card.Width = FullWidth; card.Lines = 2; return; }
                card.Row = 5;
                card.Title = index == SelRemove ? RemoveLabel : DoneLabel;
                card.Width = index == SelRemove ? _removeWidth : _doneWidth;
                card.Danger = index == SelRemove;
                return;
            }
            if (index == Note)
            {
                // Why Seating, Beds... are greyed: the cards are the designs that fit the selected piece.
                card.Row = 5;
                card.Kind = CardKind.Title;
                if (NoteHidden) { card.Title = ""; card.Width = 0.001f; card.Height = 0.0001f; return; }
                card.Title = FitNote(_redesign.Selected.Role);
                card.Width = FullWidth; // one line across the panel (it wrapped inside one column: "must be one line")
                card.Lines = 1;
                return;
            }
            if (index == Leave)
            {
                // A visible way out of "replace this real box": back to placing outside (the Add tab does that now).
                card.Row = 4;
                if (_job == Job.MyFurniture)
                {
                    // My room: "Adjust fit" once a design is on the piece; nothing otherwise.
                    bool can = MyZone && _redesign.CurrentDesignId != null;
                    if (!can) { card.Kind = CardKind.Title; card.Title = ""; card.Width = 0.001f; return; }
                    card.Title = AdjustLabel;
                    card.Width = _adjustLabelWidth;
                    return;
                }
                card.Title = LeaveLabel;
                card.Width = _leaveWidth;
                return;
            }
            if (index == Keep)
            {
                // My zone: keep the real piece (the room's Original button in the old UI); hidden outside.
                card.Row = 4;
                card.Title = MyZone ? KeepRealLabel : "";
                card.Kind = MyZone ? CardKind.Chip : CardKind.Title;
                card.Width = MyZone ? _keepWidth : 0f;
                card.Selected = MyZone && _redesign.CurrentDesignId == null;
                return;
            }
            // The pager: centred under the cards (the count's text is centred in its box by the panel: AlignCenter), the chevrons as dark pills (a visible fill when off), the count as ink text.
            card.Row = 3;
            card.AlignCenter = true;
            card.Width = index == PageLabel ? _labelWidth : _arrowWidth;
            card.Kind = index == PageLabel ? CardKind.Title : CardKind.Chip;
            card.Strong = index != PageLabel;
            card.Title = index == Prev ? "<" : index == Next ? ">" : _pageLabels[_page * MaxPages + Pages - 1]; // ASCII until the chevron icons are set
            Vector4 icon = index == Prev ? PrevIcon : index == Next ? NextIcon : Vector4.zero;
            if (icon != Vector4.zero) { card.Icon = icon; card.Title = ""; }
            card.Disabled = index == PageLabel || (index == Prev && _page == 0) || (index == Next && _page >= Pages - 1);
        }

        /// <summary>Add only: a piece card dragged off the panel arms that piece for a drag onto the floor.</summary>
        public bool DragOut(int index)
        {
            if (_job != Job.AddFurniture || Empty || index < Cards || index >= Prev) return false;
            int i = _page * CardsPerPage + index - Cards;
            if (i >= _shown.Count) return false;
            _outside?.Arm(_shown[i]);
            return _outside != null;
        }

        public bool Press(int index)
        {
            if (Adjusting) return PressAdjust(index);
            if (Empty) return false;
            if (PieceCard && index >= SelTitle)
            {
                if (index == SelRemove) return _outside.RemoveSelected();
                if (index == SelDone) { _outside.Deselect(); return true; }
                return false;
            }
            if (MyZone && index > Next) { if (index <= Next + TopItems) return PressTop(index - Next - 1); index -= TopItems; }
            if (index == Leave && _job == Job.MyFurniture)
            {
                if (!MyZone || _redesign.CurrentDesignId == null) return false;
                _adjusting = true;
                return true;
            }
            if (index < Cards)
            {
                if (MyZone) return index == _category;
                _category = index;
                _page = 0;
                Refresh();
                return true;
            }
            if (index == Keep) return MyZone && _redesign.KeepReal();
            if (index == Leave) { if (!MyZone) return false; _redesign.Deselect(); return true; }
            if (index == Note) return false;
            if (index == Prev) { _page = Mathf.Max(0, _page - 1); return true; }
            if (index == Next) { _page = Mathf.Min(Pages - 1, _page + 1); return true; }
            int i = _page * CardsPerPage + index - Cards;
            if (index >= Prev || i >= _shown.Count) return false;
            if (!MyZone) { _outside?.Arm(_outside.Armed == _shown[i] ? null : _shown[i]); return true; }
            var fits = _redesign.Fits;
            for (int f = 0; f < fits.Count; f++)
            {
                if (fits[f].item != _shown[i]) continue;
                bool ok = _redesign.ApplyDesign(f, out string reason);
                LastRefusal = ok ? null : reason;
                if (!ok) Refused?.Invoke(reason);
                return ok;
            }
            return false;
        }
    }
}

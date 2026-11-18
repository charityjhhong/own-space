using System;
using OwnSpace.Contracts;
using OwnSpace.UI;
using TMPro;
using UnityEngine;

namespace OwnSpace.MR
{
    public enum TidyStep { Draw, PickLook, Done }

    /// <summary>The clutter-cover tool behind the Tidy up tab (HideTool).</summary>
    public interface ITidyUp
    {
        TidyStep Step { get; }
        bool Drawing { get; }
        bool HasSelection { get; }
        /// <summary>The selected cover's look (0 Plain, 1-5), or -1.</summary>
        int SelectedLook { get; }
        string CoveredWith { get; }
        CatalogItem LookItem(int look);
        void StartDrawing();
        void CancelDrawing();
        bool PickLook(int look);
        bool RemoveSelected();
        event Action Changed;
        event Action<string> Refusal;
    }

    /// <summary>
    /// The panel's Tidy up tab (spec v4 section 7.4), sequential: a stepper "1 Draw a box" → "2 Pick a look" →
    /// "3 Done" (current semibold, done plain, future muted). Step 1 arms drawing; releasing a box that core
    /// accepts moves to step 2 by itself, a refused box stays on step 1, red, with the reason (for the toast).
    /// Step 2 has six look cards like the furniture cards (thumbnail + name: Plain, Basket, Plant, Cushion, Box, Screen); picking one applies it at once and
    /// moves to step 3 ("Covered with a basket.", Add another, Remove). Selecting a cover in the room jumps to step 2.
    /// A safety line is always shown. Text is built on a change, so Describe never allocates.
    /// </summary>
    public sealed class TidyUpPage : IPanelPage, IDisposable
    {
        public static readonly string[] Steps = { "1 Draw a box", "2 Pick a look", "3 Done" };
        /// <summary>A completed step: the check takes the number's place ("✓ Draw a box"), with its own room in the pill.</summary>
        public static readonly string[] StepNames = { "Draw a box", "Pick a look", "Done" };
        public static readonly string[] Looks = { "Plain", "Basket", "Plant", "Cushion", "Box", "Screen" };
        public const string Safety = "Hidden things are still there. Mind your step when you stand up.";

        enum Act { None, Draw, Cancel, Look, Another, Remove, Size }
        static readonly AdjustAction[] Sizes = { AdjustAction.Wider, AdjustAction.Narrower, AdjustAction.Deeper, AdjustAction.Shallower };
        static readonly string[] SizeLabels = { "Wider", "Narrower", "Deeper", "Shallower" };
        readonly float[] _sizeWidths = new float[4];
        readonly ICoverResize _resize;
        struct Item { public CardKind Kind; public int Row; public string Title; public float Width; public bool Selected, Disabled, Danger; public Color Swatch; public Vector4 Thumb; public Act Act; public int Arg; }

        readonly ITidyUp _tidy;
        readonly Item[] _items = new Item[16];
        readonly float[] _stepWidths = new float[3], _checkedWidths = new float[3];
        readonly Color[] _lookColours = new Color[6];
        readonly Vector4[] _lookThumbs = new Vector4[6];
        readonly float _cancelWidth, _anotherWidth, _removeWidth, _removeShortWidth, _drawWidth;
        int _count;

        public Action Changed { get; set; }

        /// <summary>The last refusal's reason (for the toast), or null.</summary>
        public string LastRefusal { get; private set; }

        /// <summary>The check icon (PanelIcons "check") on completed steps; zero = none.</summary>
        public Vector4 CheckIcon
        {
            get => _checkIcon;
            set { if (value == _checkIcon) return; _checkIcon = value; Rebuild(); }
        }
        Vector4 _checkIcon;

        public int Count => _count;
        public int Columns => 3;

        /// <summary>The atlas key of the Plain look's thumbnail (content PR #264); looks 1-5 use their catalog item's id.</summary>
        public const string PlainThumb = "cover-plain";

        public TidyUpPage(ITidyUp tidy, TMP_FontAsset font = null, FurnitureThumbs thumbs = null)
        {
            _tidy = tidy;
            PanelElement.MeasureWidths(Steps, PanelElement.BodyMm, font, _stepWidths);
            for (int i = 0; i < 3; i++) _stepWidths[i] += 0.008f;
            PanelElement.MeasureWidths(StepNames, PanelElement.BodyMm, font, _checkedWidths);
            for (int i = 0; i < 3; i++) _checkedWidths[i] += 0.004f + PanelElement.ToggleIconRoom; // tighter than a numbered step, so three steps still fit the panel
            _drawWidth = Mathf.Max(0.120f, PanelElement.MeasureWidth("Draw a box", PanelElement.BodyMm, font) + 0.024f); // the step's one action: 120 x 36 mm
            _cancelWidth = PanelElement.MeasureWidth("Cancel", PanelElement.BodyMm, font) + 0.024f;
            _anotherWidth = PanelElement.MeasureWidth("Add another", PanelElement.BodyMm, font) + 0.024f;
            _removeWidth = PanelElement.MeasureWidth("Remove cover", PanelElement.BodyMm, font) + 0.024f;
            _removeShortWidth = PanelElement.MeasureWidth("Remove", PanelElement.BodyMm, font) + 0.024f;
            PanelElement.MeasureWidths(SizeLabels, PanelElement.BodyMm, font, _sizeWidths);
            for (int i = 0; i < 4; i++) _sizeWidths[i] += 0.024f;
            _resize = tidy as ICoverResize;
            _lookColours[0] = new Color(0.78f, 0.76f, 0.72f); // Plain: the floor's colour
            if (thumbs != null) _lookThumbs[0] = thumbs.Rect(PlainThumb);
            for (int i = 1; i < 6; i++)
            {
                CatalogItem item = tidy?.LookItem(i);
                if (thumbs != null && item != null) _lookThumbs[i] = thumbs.Rect(item.id);
                _lookColours[i] = item != null && ColorUtility.TryParseHtmlString(item.primaryColor, out Color c) ? c : new Color(0.6f, 0.6f, 0.62f);
            }
            if (_tidy != null) { _tidy.Changed += Rebuild; _tidy.Refusal += OnRefusal; }
            Rebuild();
        }

        void Rebuild()
        {
            _count = 0;
            TidyStep step = _tidy != null ? _tidy.Step : TidyStep.Draw;
            // The three steps always fit the panel's width: if their words at 14 mm are wider, they share it in proportion
            // (the text then draws a little smaller, never below 12 mm) instead of scrolling sideways.
            float total = 2f * 0.008f;
            for (int i = 0; i < 3; i++) total += i < (int)step && _checkIcon != Vector4.zero ? _checkedWidths[i] : _stepWidths[i];
            float fit = total > FullWidth ? (FullWidth - 2f * 0.008f) / (total - 2f * 0.008f) : 1f;
            for (int i = 0; i < 3; i++)
            {
                bool check = i < (int)step && _checkIcon != Vector4.zero; // the check replaces the number; it never sits on the text
                Add(new Item { Kind = CardKind.Toggle, Row = 0, Title = check ? StepNames[i] : Steps[i], Width = (check ? _checkedWidths[i] : _stepWidths[i]) * fit, Selected = i == (int)step, Disabled = i > (int)step });
            }
            switch (step)
            {
                case TidyStep.Draw:
                    Add(new Item { Kind = CardKind.Title, Row = 1, Title = "Pinch and drag on the floor over the clutter" });
                    bool drawing = _tidy != null && _tidy.Drawing;
                    Add(new Item { Kind = CardKind.Chip, Row = 2, Title = drawing ? "Cancel" : "Draw a box", Width = drawing ? _cancelWidth : _drawWidth, Act = drawing ? Act.Cancel : Act.Draw });
                    break;
                case TidyStep.PickLook:
                    Add(new Item { Kind = CardKind.Title, Row = 1, Title = "Pick a look" });
                    int selectedLook = _tidy != null ? _tidy.SelectedLook : -1; // an existing cover shows its look selected
                    // The furniture card (thumbnail + name), three per row; a look with no thumbnail keeps its colour dot.
                    for (int i = 0; i < 6; i++)
                    {
                        bool thumb = _lookThumbs[i] != Vector4.zero;
                        Add(new Item { Kind = thumb ? CardKind.Card : CardKind.Disc, Row = 2 + i / 3, Title = Looks[i], Swatch = thumb ? default : _lookColours[i], Thumb = _lookThumbs[i], Act = Act.Look, Arg = i, Selected = i == selectedLook });
                    }
                    Add(new Item { Kind = CardKind.Chip, Row = 4, Title = "Remove cover", Width = _removeWidth, Act = Act.Remove, Danger = true, Disabled = _tidy == null || !_tidy.HasSelection });
                    break;
                default:
                    Add(new Item { Kind = CardKind.Title, Row = 1, Title = _tidy?.CoveredWith ?? "Covered." });
                    Add(new Item { Kind = CardKind.Chip, Row = 2, Title = "Add another", Width = _anotherWidth, Act = Act.Another });
                    Add(new Item { Kind = CardKind.Chip, Row = 2, Title = "Remove", Width = _removeShortWidth, Act = Act.Remove, Danger = true });
                    // The cover's size, like Adjust fit for furniture: grey at a limit, with the same hint line.
                    if (_resize != null && _resize.HasCover)
                    {
                        for (int i = 0; i < 4; i++)
                            Add(new Item { Kind = CardKind.Chip, Row = 3 + i / 2, Title = SizeLabels[i], Width = _sizeWidths[i], Act = Act.Size, Arg = i, Disabled = !_resize.CanResize(Sizes[i]) });
                        string hint = FurniturePage.LimitHint(!_resize.CanResize(AdjustAction.Narrower), !_resize.CanResize(AdjustAction.Shallower),
                            !_resize.CanResize(AdjustAction.Wider), !_resize.CanResize(AdjustAction.Deeper));
                        if (hint != FurniturePage.NudgeHint) Add(new Item { Kind = CardKind.Title, Row = 5, Title = hint });
                    }
                    break;
            }
            Add(new Item { Kind = CardKind.Title, Row = 6, Title = Safety });
            Changed?.Invoke();
        }

        void OnRefusal(string reason) { LastRefusal = reason; Changed?.Invoke(); }

        /// <summary>Stops listening to the tool (call when the panel is torn down).</summary>
        public void Dispose() { if (_tidy != null) { _tidy.Changed -= Rebuild; _tidy.Refusal -= OnRefusal; } }

        const float FullWidth = 0.268f; // the panel's inner width

        /// <summary>The armed banner while "Draw a box" waits for the floor (the host shows it above the panel).</summary>
        public const string DrawBanner = "Pinch and drag on the floor to draw a box · Cancel";

        void Add(Item it) { if (_count < _items.Length) _items[_count++] = it; }

        public void Describe(int index, ref CardInfo card)
        {
            Item it = _items[index];
            card.Kind = it.Kind;
            card.Row = it.Row;
            card.Title = it.Title;
            card.Width = it.Width;
            card.Selected = it.Selected;
            card.Disabled = it.Disabled;
            card.Swatch = it.Swatch;
            card.Thumb = it.Thumb;
            card.Danger = it.Danger;
            // Text lines span the panel and wrap (device batch 9: cut off at the right edge).
            if (it.Kind == CardKind.Title) { card.Width = FullWidth; card.Lines = 2; }
            bool completed = index < 3 && _tidy != null && index < (int)_tidy.Step;
            card.Icon = completed ? CheckIcon : Vector4.zero; // a check on completed steps (spec 7.4)
            card.IconSide = -1;
        }

        public bool Press(int index)
        {
            if (index < 0 || index >= _count || _items[index].Disabled || _tidy == null) return false;
            Item it = _items[index];
            switch (it.Act)
            {
                case Act.Draw: case Act.Another: _tidy.StartDrawing(); return true;
                case Act.Cancel: _tidy.CancelDrawing(); return true;
                case Act.Look: return _tidy.PickLook(it.Arg);
                case Act.Remove: return _tidy.RemoveSelected();
                case Act.Size: return _resize != null && _resize.Resize(Sizes[it.Arg]);
                default: return false;
            }
        }
    }
}

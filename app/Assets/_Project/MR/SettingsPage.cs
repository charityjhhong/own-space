using System;
using System.Collections.Generic;
using OwnSpace.UI;
using TMPro;

namespace OwnSpace.MR
{
    /// <summary>
    /// The gear's Settings sheet (mockups round 2, settings.html).
    /// <list type="bullet">
    /// <item>A header, "Settings" (14 semibold, 24 mm; no close button: the lit gear or any tab closes the sheet).</item>
    /// <item>Section names in sentence case (12 semibold, 20 mm): Room, Appearance, Design, with 12 mm above each but the first.</item>
    /// <item>Every row is 40 mm: a title (14) with a one-line caption (12) under it on the left margin, and ONE control
    /// on the right in an 80 mm column, 36 mm high. Rows touch: no divider lines.</item>
    /// <item>Reset asks on the rows where it was: the question, then Confirm and Cancel in the control column.</item>
    /// <item>Development builds: Real occlusion is the last row under Design (no Developer heading).</item>
    /// </list>
    /// 419 mm tall in release builds, 459 mm in development builds or while Reset asks (the mockup's 408 and 448 plus
    /// the extra bottom padding that keeps the resize grip clear of the last button, less 4 mm on the last row). Text is built when the state
    /// changes (<see cref="Refresh"/>, a press), so Describe never allocates.
    /// </summary>
    public sealed class SettingsPage : IPanelPage
    {
        // Labels (kept for callers and tests); wording from the mockups' table (rules.html).
        public const string Title = "Settings", CloseGlyph = "×", MySpaceTitle = SectionSpace,
            SpacesLabel = "Change", EditZoneLabel = "Edit", RescanLabel = "Rescan", ResetLabel = "Reset", ResetConfirm = "Confirm",
            CancelLabel = "Cancel", CloseLabel = CloseGlyph,
            CurrentSpaceTitle = "Current space", RealAreaTitle = "My zone", RoomScanTitle = "Room scan", ThemeTitle = "Theme",
            ResetTitle = "Reset design", OcclusionTitle = "Real occlusion", BiggerRoomTitle = "Bigger room",
            BiggerRoomCaption = "Room looks wider", BiggerRoomOn = "On", BiggerRoomOff = "Off",
            EditZoneCaption = "The part that stays real", RescanCaptionText = "Scan this room again",
            ResetCaption = "Remove all changes", // "…in this room" is 31 characters: at 12 mm it does not fit the 180 mm beside an 80 mm button
             ConfirmText = "Remove all changes? Your zone stays.",
            RescanNote = RescanCaptionText,
            ConfirmQuestion = "Remove all changes?", ConfirmNote = "Your zone stays.",
            OcclusionOn = "On", OcclusionOff = "Off", OcclusionNone = "Not available", OcclusionStartingLabel = "Starting";
        public const string SectionSpace = "Room", SectionAppearance = "Appearance", SectionDesign = "Design";
        public const float FullWidth = 0.268f, Gap = 0.008f, CloseWidth = 0.036f;
        /// <summary>The layout's numbers (m): one control column, one row height, the header and section rows, the gap above a section.</summary>
        public const float ControlWidth = 0.080f, RowHeight = 0.040f, HeaderHeight = 0.024f, SectionHeight = 0.020f, GroupGap = 0.012f,
            ControlHeight = 0.036f, OptionWidth = (ControlWidth - Gap) * 0.5f;

        enum Act { None, Spaces, EditZone, Rescan, Light, Dark, Reset, Confirm, Cancel, Occlusion, BiggerRoom }
        enum Text { Section, Header, Body }
        struct Item { public CardKind Kind; public int Row; public string Title; public float Width, Height, GapBelow; public Act Act; public bool Danger; public int Lines; public Text Text; }

        public Func<bool> IsDark { get; set; }
        public Action<bool> SetDark { get; set; }
        /// <summary>Rescan room; false when Space Setup cannot run here.</summary>
        public Func<bool> Rescan { get; set; }
        /// <summary>Reset my design (after the confirm).</summary>
        public Action Reset { get; set; }
        public Action Close { get; set; }
        /// <summary>Edit my zone (also on the Style tab): the zone step with the current outline.</summary>
        public Action EditZone { get; set; }
        Action _openSpaces;
        Func<string> _currentSpace;
        Func<bool> _isOcclusionOn;
        bool _dirty = true;

        /// <summary>Opens the Spaces chooser; null = no Change control.</summary>
        public Action OpenSpaces { get => _openSpaces; set { _openSpaces = value; _dirty = true; } }
        /// <summary>The current space's name ("This room", "Dorm"); read on <see cref="Refresh"/>.</summary>
        public Func<string> CurrentSpace { get => _currentSpace; set { _currentSpace = value; _dirty = true; } }
        /// <summary>Development builds only: the real-occlusion switch; null = no DEVELOPER section.</summary>
        public Func<bool> IsOcclusionOn { get => _isOcclusionOn; set { _isOcclusionOn = value; _dirty = true; } }
        public Action<bool> SetOcclusion { get; set; }
        public Func<bool> OcclusionAvailable { get; set; }
        public Func<bool> OcclusionStarting { get; set; }
        Func<bool> _isBiggerRoom;
        /// <summary>Bigger room (<see cref="VirtualExtent"/>): on or off; null = no row.</summary>
        public Func<bool> IsBiggerRoom { get => _isBiggerRoom; set { _isBiggerRoom = value; _dirty = true; } }
        public Action<bool> SetBiggerRoom { get; set; }

        readonly Item[] _items = new Item[24];
        int _count;
        bool _confirming;
        string _spaceRow, _areaRow, _scanRow, _resetRow, _confirmRow, _themeRow;
        string _occRow, _occOn, _occOff, _occNone, _occStarting, _biggerRow;

        public bool Confirming => _confirming;
        public int Count { get { if (_dirty) Refresh(); return _count; } }
        public int Columns => 1;

        readonly TMP_FontAsset _font;

        public SettingsPage(TMP_FontAsset font = null)
        {
            _font = font;
            _themeRow = ThemeTitle;
            _occOn = Row(OcclusionTitle, "Running");
            _occOff = Row(OcclusionTitle, "Off by you");
            _occNone = Row(OcclusionTitle, OcclusionNone);
            _occStarting = Row(OcclusionTitle, OcclusionStartingLabel);
            _areaRow = Row(RealAreaTitle, EditZoneCaption);
            _scanRow = Row(RoomScanTitle, RescanCaptionText);
            _biggerRow = Row(BiggerRoomTitle, BiggerRoomCaption);
            _resetRow = Row(ResetTitle, ResetCaption);
            _confirmRow = Row(ConfirmQuestion, ConfirmNote);
        }

        // A 14 mm title over a 12 mm caption (86%), as one left-aligned text. Size only: TMP has no closing tag for <alpha>
        // (device shot 181928 printed "</alpha>"), and a dimmer caption was too faint on the light glass (shot 182020).
        static string Row(string title, string caption) => string.IsNullOrEmpty(caption) ? title : title + "\n<size=86%>" + caption + "</size>";

        /// <summary>Rebuilds the rows (opening the sheet, after a change).</summary>
        public void Refresh()
        {
            _dirty = false;
            // A long space name keeps the caption's size and shows its beginning (rows never shrink or wrap).
            _spaceRow = Row(CurrentSpaceTitle, CurrentSpace != null ? TextTail.Head(CurrentSpace(), FullWidth - ControlWidth - Gap - 0.004f, PanelElement.CaptionMm, _font) : null);
            _count = 0;
            int row = 0;
            // Header: the title alone (the human: no ×; the lit gear or any tab closes the sheet).
            Line(row++, Title, Text.Header, HeaderHeight, 0f);

            // While Reset asks, only the question and its two buttons show under the title (device 8fdbcda, shot 144121: with
            // Bigger room the question and its buttons were one row taller than the panel, and Confirm and Cancel were cut off).
            // Cancel brings every section back. However many rows Room and Appearance grow, the question always fits.
            if (!_confirming)
            {
                Line(row++, SectionSpace, Text.Section, SectionHeight, 0f);
                if (OpenSpaces != null) Control(row++, _spaceRow, SpacesLabel, Act.Spaces);
                Control(row++, _areaRow, EditZoneLabel, Act.EditZone);
                if (IsBiggerRoom == null) Control(row++, _scanRow, RescanLabel, Act.Rescan, gapBelow: GroupGap);
                else
                {
                    Control(row++, _scanRow, RescanLabel, Act.Rescan);
                    Control(row++, _biggerRow, BiggerRoomOff, Act.BiggerRoom, gapBelow: GroupGap); // On/Off picked in Describe
                }

                Line(row++, SectionAppearance, Text.Section, SectionHeight, 0f);
                // Light | Dark: two text options that fill the control column; only the chosen one is marked.
                Put(new Item { Kind = CardKind.Title, Row = row, Title = _themeRow, Width = FullWidth - ControlWidth - Gap, Height = RowHeight, GapBelow = GroupGap, Lines = 1, Text = Text.Body });
                Put(new Item { Kind = CardKind.Toggle, Row = row, Title = "Light", Width = OptionWidth, Height = ControlHeight, Act = Act.Light });
                Put(new Item { Kind = CardKind.Toggle, Row = row++, Title = "Dark", Width = OptionWidth, Height = ControlHeight, Act = Act.Dark });
            }

            Line(row++, SectionDesign, Text.Section, SectionHeight, 0f);
            if (_confirming)
            {
                // The question takes Reset's row; Confirm and Cancel sit under it in the control column's edge.
                Put(new Item { Kind = CardKind.Title, Row = row++, Title = _confirmRow, Width = FullWidth, Height = RowHeight, Lines = 2, Text = Text.Body });
                Put(new Item { Kind = CardKind.Title, Row = row, Title = "", Width = FullWidth - 2f * ControlWidth - 2f * Gap, Height = RowHeight, Lines = 1 });
                Put(new Item { Kind = CardKind.Chip, Row = row, Title = ResetConfirm, Width = ControlWidth, Height = ControlHeight, Act = Act.Confirm, Danger = true });
                Put(new Item { Kind = CardKind.Chip, Row = row++, Title = CancelLabel, Width = ControlWidth, Height = ControlHeight, Act = Act.Cancel });
                TrimLastRow();
                return; // while it asks, the other sections and the development row wait (the sheet stays within 460 mm)
            }
            Control(row++, _resetRow, ResetLabel, Act.Reset, danger: true);
            // Development builds: Real occlusion under Design, no Developer heading. The caption is picked in Describe.
            if (IsOcclusionOn != null) Control(row++, _occOff, OcclusionOff, Act.Occlusion);
            TrimLastRow();
        }

        // The last row is only as tall as its control: under it the panel keeps extra padding so the resize grip stays
        // clear of the button (device shot 181928), and with a full 40 mm row the development sheet would pass 460 mm.
        void TrimLastRow()
        {
            for (int i = _count - 1; i >= 0 && _items[i].Row == _items[_count - 1].Row; i--)
                if (_items[i].Kind == CardKind.Title) _items[i].Height = ControlHeight;
        }

        void Line(int row, string text, Text style, float height, float gapBelow) =>
            Put(new Item { Kind = CardKind.Title, Row = row, Title = text, Width = FullWidth, Height = height, GapBelow = gapBelow, Lines = 1, Text = style });

        void Control(int row, string text, string label, Act act, bool danger = false, float gapBelow = 0f)
        {
            Put(new Item { Kind = CardKind.Title, Row = row, Title = text, Width = FullWidth - ControlWidth - Gap, Height = RowHeight, GapBelow = gapBelow, Lines = 2, Text = Text.Body });
            Put(new Item { Kind = CardKind.Chip, Row = row, Title = label, Width = ControlWidth, Height = ControlHeight, Act = act, Danger = danger });
        }

        void Put(Item item) { if (_count < _items.Length) _items[_count++] = item; }

        bool OccAvailable => OcclusionAvailable == null || OcclusionAvailable();
        bool OccStarting => OcclusionStarting != null && OcclusionStarting();

        public void Describe(int index, ref CardInfo card)
        {
            if (_dirty) Refresh();
            Item it = _items[index];
            card.Kind = it.Kind;
            card.Row = it.Row;
            card.Title = it.Title;
            card.Width = it.Width;
            card.Lines = it.Lines;
            card.Danger = it.Danger;
            card.Height = it.Height;
            card.RowGapSet = true; // rows touch; only the last row of a group has the 12 mm above the next section name
            card.RowGapBelow = it.GapBelow;
            card.Header = it.Text == Text.Header;
            card.Body = it.Text == Text.Body;
            bool dark = IsDark != null && IsDark();
            switch (it.Act)
            {
                case Act.Light: card.Selected = !dark; break;
                case Act.Dark: card.Selected = dark; break;
                case Act.Spaces: card.Disabled = OpenSpaces == null; break;
                case Act.BiggerRoom:
                    card.Selected = IsBiggerRoom != null && IsBiggerRoom();
                    card.Title = card.Selected ? BiggerRoomOn : BiggerRoomOff;
                    break;
                case Act.Occlusion:
                    bool on = IsOcclusionOn != null && IsOcclusionOn();
                    card.Title = on ? OcclusionOn : OcclusionOff;
                    card.Selected = on && OccAvailable && !OccStarting;
                    card.Disabled = !OccAvailable || OccStarting;
                    break;
                case Act.None:
                    // The developer row's caption is the depth state (cached strings, no allocation).
                    if (index + 1 < _count && _items[index + 1].Act == Act.Occlusion)
                        card.Title = OccStarting ? _occStarting : !OccAvailable ? _occNone : IsOcclusionOn != null && IsOcclusionOn() ? _occOn : _occOff;
                    break;
            }
        }

        public bool Press(int index)
        {
            if (_dirty) Refresh();
            if (index < 0 || index >= _count) return false;
            switch (_items[index].Act)
            {
                case Act.Spaces: OpenSpaces?.Invoke(); return OpenSpaces != null;
                case Act.EditZone: EditZone?.Invoke(); return EditZone != null;
                case Act.Rescan: return Rescan != null && Rescan();
                case Act.BiggerRoom:
                    if (IsBiggerRoom == null || SetBiggerRoom == null) return false;
                    SetBiggerRoom(!IsBiggerRoom());
                    return true;
                case Act.Light: SetDark?.Invoke(false); return true;
                case Act.Dark: SetDark?.Invoke(true); return true;
                case Act.Reset: _confirming = true; Refresh(); return true; // step 1: ask, on the same row
                case Act.Confirm: _confirming = false; Refresh(); Reset?.Invoke(); return true; // step 2: do it
                case Act.Cancel: _confirming = false; Refresh(); return true;
                case Act.Occlusion:
                    if (IsOcclusionOn == null || SetOcclusion == null || !OccAvailable || OccStarting) return false;
                    SetOcclusion(!IsOcclusionOn());
                    return true;
                default: return false;
            }
        }

        /// <summary>Back to the first state (the sheet was closed mid-confirm), with fresh rows.</summary>
        public void Rewind() { _confirming = false; Refresh(); }
    }
}

using System;
using OwnSpace.UI;
using TMPro;

namespace OwnSpace.MR
{
    /// <summary>The AI Designer's request flow (AskMenu): presets, cancel, the last plan and its one-step undo.</summary>
    public interface IAiDesigner
    {
        bool Busy { get; }
        bool HasPlan { get; }
        bool CanUndoPlan { get; }
        bool LastFailed { get; }
        bool LastFromFallback { get; }
        string LastRequest { get; }
        string LastRationale { get; }
        /// <summary>Core's current status message (IAssistantStatus.Message), or null.</summary>
        string StatusMessage { get; }
        int LastPlaced { get; }
        int LastSkipped { get; }
        bool AskPreset(int index, string label);
        void CancelRequest();
        bool UndoPlan();
        event Action Changed;
    }

    /// <summary>The "Describe the room you want" field: our keyboard under the panel (typing only).</summary>
    public interface IRequestField
    {
        /// <summary>Shows the keyboard; false (with a reason raised elsewhere) when it cannot.</summary>
        bool Open();
        /// <summary>Sends the typed text; false when empty or busy.</summary>
        bool Send();
        /// <summary>The typed text so far ("" = none).</summary>
        string Text { get; }
        /// <summary>Raised when the typed text changes.</summary>
        event Action Changed;
    }

    /// <summary>
    /// The panel's AI Designer tab (spec v4 section 7.3): the "Describe the room you want" field showing the typed text
    /// with Create beside it (pinching it opens the keyboard under the panel; typing only, device batch 10), StylesTitle with four content-hugging preset chips, and the AI's plan only after
    /// a run: the quoted request, the explanation, "7 placed · 1 skipped" and Undo all; "Thinking…" with Cancel
    /// while it runs; "No change made. Try again." after an error. The explanation wraps to two lines. (The disabled
    /// "Suggest for the selected piece" placeholder is gone until per-piece suggestions exist: on device it hung
    /// past the panel's bottom.) Text is built when the state changes, so Describe never allocates.
    /// </summary>
    public sealed class AiDesignerPage : IPanelPage, IDisposable
    {
        public static readonly string[] Presets = { "Living room", "Study café", "Bedroom", "Bright studio" };
        public const string Field = FreeTextRequest.Placeholder, ShortField = "Describe your space", CreateLabel = "Create", StylesTitle = "Styles", PlanTitle = "What the AI did", UndoLabel = "Undo all", Before = "The AI furnishes the outside zone.",
            Thinking = "Thinking", OfflinePlan = PlanTitle + ", offline plan";

        enum Act { None, Field, Send, Preset, Cancel, Undo }
        struct Item { public CardKind Kind; public int Row; public string Title; public float Width; public bool Disabled, Section; public Act Act; public int Arg; public int Lines; }

        readonly IAiDesigner _ai;
        readonly IRequestField _field;
        readonly Item[] _items = new Item[14];
        readonly float[] _presetWidths = new float[4];
        readonly float _fieldWidth, _sendWidth, _cancelWidth, _undoWidth;
        readonly TMP_FontAsset _font;

        /// <summary>The size (mm) the panel draws a chip's text at; the field's text is never smaller.</summary>
        public const float ChipTextMm = 14f;
        /// <summary>A chip's inner side padding (PanelElement.TextPadding) plus 4 mm of slack, so the fitted text is never auto-shrunk.</summary>
        const float FieldTextInset = 0.012f + 0.004f;

        /// <summary>The room (m) for the field's text at full size.</summary>
        public float FieldTextWidth => _fieldWidth - FieldTextInset;
        /// <summary>The field's placeholder: <see cref="Field"/> when it fits at full size, else <see cref="ShortField"/>.</summary>
        public string Placeholder { get; }
        int _count;

        /// <summary>Called after the state changes, so the panel re-describes.</summary>
        public Action Changed { get; set; }

        public int Count => _count;
        public int Columns => 2;

        public AiDesignerPage(IAiDesigner ai, IRequestField field, float innerWidth, TMP_FontAsset font = null)
        {
            _ai = ai;
            _field = field;
            PanelElement.MeasureWidths(Presets, PanelElement.BodyMm, font, _presetWidths);
            for (int i = 0; i < 4; i++) _presetWidths[i] = (innerWidth - 0.008f) * 0.5f; // an aligned 2 x 2 grid: 130 x 36 mm, gap 8
            _sendWidth = PanelElement.MeasureWidth(CreateLabel, PanelElement.BodyMm, font) + 0.024f;
            _fieldWidth = innerWidth - _sendWidth - 0.008f; // the panel's gap
            _cancelWidth = PanelElement.MeasureWidth("Cancel", PanelElement.BodyMm, font) + 0.024f;
            _undoWidth = PanelElement.MeasureWidth(UndoLabel, PanelElement.BodyMm, font) + 0.024f;
            _font = font;
            Placeholder = PanelElement.MeasureWidth(Field, ChipTextMm, font) <= FieldTextWidth ? Field : ShortField;
            if (_ai != null) _ai.Changed += Rebuild;
            if (_field != null) _field.Changed += Rebuild;
            Rebuild();
        }

        // Rebuilds the item list for the current state (allocates the few strings; on change only).
        void Rebuild()
        {
            _count = 0;
            bool busy = _ai != null && _ai.Busy;
            string typed = _field != null ? _field.Text : null;
            // The same text size as Create and the chips: a long request shows its end, it never shrinks.
            Add(CardKind.Chip, 0, string.IsNullOrEmpty(typed) ? Placeholder : TextTail.Fit(typed, FieldTextWidth, ChipTextMm, _font), _fieldWidth, busy, Act.Field);
            Add(CardKind.Chip, 0, CreateLabel, _sendWidth, busy || string.IsNullOrEmpty(typed), Act.Send);
            Add(CardKind.Title, 1, StylesTitle, 0f, false, Act.None); _items[_count - 1].Section = true;
            for (int i = 0; i < 4; i++) Add(CardKind.Chip, 2 + i / 2, Presets[i], _presetWidths[i], busy, Act.Preset, i);
            if (busy)
            {
                Add(CardKind.Title, 4, Thinking, 0f, false, Act.None);
                Add(CardKind.Chip, 5, "Cancel", _cancelWidth, false, Act.Cancel);
            }
            else if (_ai != null && _ai.LastFailed) Add(CardKind.Title, 4, "No change made. Try again.", 0f, false, Act.None);
            else if (_ai != null && _ai.HasPlan)
            {
                Add(CardKind.Title, 4, _ai.LastFromFallback ? OfflinePlan : PlanTitle, 0f, false, Act.None); _items[_count - 1].Section = true;
                if (!string.IsNullOrEmpty(_ai.LastRequest)) Add(CardKind.Title, 5, "“" + _ai.LastRequest + "”", 0f, false, Act.None);
                // The AI's explanation: the plan's rationale, else core's status message ("Added 7 pieces").
                string explanation = !string.IsNullOrEmpty(_ai.LastRationale) ? _ai.LastRationale : _ai.StatusMessage;
                if (!string.IsNullOrEmpty(explanation)) { Add(CardKind.Title, 6, explanation, 0f, false, Act.None); _items[_count - 1].Lines = 2; }
                Add(CardKind.Title, 7, _ai.LastSkipped > 0 ? $"{_ai.LastPlaced} placed, {_ai.LastSkipped} skipped" : $"{_ai.LastPlaced} placed", 0f, false, Act.None);
                Add(CardKind.Chip, 8, UndoLabel, _undoWidth, !_ai.CanUndoPlan, Act.Undo);
            }
            else Add(CardKind.Title, 4, Before, 0f, false, Act.None);
            Changed?.Invoke();
        }

        /// <summary>Stops listening to the AI (call when the panel is torn down).</summary>
        public void Dispose()
        {
            if (_ai != null) _ai.Changed -= Rebuild;
            if (_field != null) _field.Changed -= Rebuild;
        }

        void Add(CardKind kind, int row, string title, float width, bool disabled, Act act, int arg = 0)
        {
            if (_count >= _items.Length) return;
            _items[_count++] = new Item { Kind = kind, Row = row, Title = title, Width = width, Disabled = disabled, Act = act, Arg = arg };
        }

        public void Describe(int index, ref CardInfo card)
        {
            Item it = _items[index];
            card.Kind = it.Kind;
            card.Row = it.Row;
            card.Title = it.Title;
            card.Width = it.Width;
            card.Disabled = it.Disabled;
            card.Lines = it.Lines;
            // Text lines span the panel and wrap (device batch 10: no "…"; the last line sat in one column and was cut).
            // A section name is one 12 mm semibold line; everything else (hint, quote, explanation, count) is 12 mm regular.
            if (it.Kind == CardKind.Title) { card.Width = 0.268f; card.Lines = it.Section ? 1 : 2; card.Caption = !it.Section; }
            if (it.Act == Act.Send) card.Selected = !it.Disabled; // the main action: the theme's dark pill
        }

        public bool Press(int index)
        {
            if (index < 0 || index >= _count || _items[index].Disabled) return false;
            Item it = _items[index];
            switch (it.Act)
            {
                case Act.Field: return _field != null && _field.Open();
                case Act.Send: return _field != null && _field.Send();
                case Act.Preset: return _ai != null && _ai.AskPreset(it.Arg, Presets[it.Arg]);
                case Act.Cancel: _ai?.CancelRequest(); return true;
                case Act.Undo: return _ai != null && _ai.UndoPlan();
                default: return false;
            }
        }
    }
}

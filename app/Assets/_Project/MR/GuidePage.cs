using System;
using OwnSpace.UI;
using TMPro;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The guided first run on the panel (shown as a sheet with the tab row hidden). Top to bottom:
    /// <list type="bullet">
    /// <item>A header: the step's title (14 semibold) and "Step 3 of 5" on the right (core's numbering: the outside's choice is
    /// step 4, the way chosen step 5).</item>
    /// <item>One instruction line (12 mm).</item>
    /// <item>The way on: Back (where it makes sense), Skip setup, and Next or Finish (the filled one) on the right. It sits
    /// above the content, so a long page never pushes it out of the panel.</item>
    /// <item>The content: step 3 is My room's own page (the furniture in the zone). Step 4 is two big cards, "Let AI
    /// design the outside" or "Design the outside myself"; then the AI page, or Style and Add with two text options
    /// ("Walls and floor", "Furniture") to switch between them.</item>
    /// </list>
    /// Text is fixed or built on a change, so Describe never allocates.
    /// </summary>
    public sealed class GuidePage : IPanelPage, IPanelDragOut
    {
        public const string SkipLabel = "Skip setup", BackLabel = "Back", NextLabel = "Next", FinishLabel = "Finish",
            AiChoice = "Let AI design the outside", SelfChoice = "Design the outside myself",
            StyleOption = "Walls and floor", AddOption = "Furniture";
        // Titles and instructions per step (round 2 wording rules: names of 1 to 3 words; instructions are sentences).
        public static readonly string[] Titles = { "Scan your room", "Set your zone", "Your zone", "Outside" };
        public static readonly string[] Lines =
        {
            "Scan this room with Space Setup first.", "Drag the corners to set your zone.",
            "Pick a design for your desk or bed.", "Choose who designs the virtual area.",
        };
        const string WorkAi = "Pick a style, or describe your space.", WorkStyle = "Pick a floor, a wall and a mood.",
            WorkAdd = "Pinch a piece, then pinch the floor.";
        const float FullWidth = 0.268f, Gap = 0.008f;

        readonly IGuideSteps _steps;
        readonly IPanelPage _ai, _myRoom, _style, _add;
        readonly string[] _counts = new string[6];
        readonly float _countWidth, _skipWidth, _backWidth, _nextWidth, _finishWidth, _optionWidth;
        bool _showAdd; // "Design the outside myself": Style (false) or Add (true)

        public Action Changed { get; set; }

        public GuidePage(IGuideSteps steps, IPanelPage ai, IPanelPage myRoom, IPanelPage style, IPanelPage add, TMP_FontAsset font = null)
        {
            _steps = steps; _ai = ai; _myRoom = myRoom; _style = style; _add = add;
            for (int i = 1; i <= 5; i++) _counts[i] = $"Step {i} of 5";
            _countWidth = PanelElement.MeasureWidth(_counts[5], PanelElement.CaptionMm, font) + 0.008f;
            float W(string s) => Mathf.Max(ToolPanel.MinChipWidth, PanelElement.MeasureWidth(s, PanelElement.BodyMm, font) + 0.024f);
            _skipWidth = W(SkipLabel); _backWidth = W(BackLabel); _nextWidth = W(NextLabel); _finishWidth = W(FinishLabel);
            _optionWidth = Mathf.Max(PanelElement.MeasureWidth(StyleOption, PanelElement.BodyMm, font), PanelElement.MeasureWidth(AddOption, PanelElement.BodyMm, font)) + 0.012f;
            if (_steps != null) _steps.Changed += () => { if (_steps.Branch != GuideBranch.Self) _showAdd = false; Changed?.Invoke(); };
        }

        /// <summary>Whether "Design the outside myself" shows Add (pieces) rather than Style (walls and floor).</summary>
        public bool ShowingAdd => _showAdd;

        /// <summary>The page shown under the header, or null (the choice, or a step with no page).</summary>
        public IPanelPage Inner => _steps == null ? null
            : _steps.Step == GuideStep.Inside ? _myRoom
            : _steps.Step != GuideStep.Outside ? null
            : _steps.Branch == GuideBranch.Ai ? _ai : _steps.Branch == GuideBranch.Self ? (_showAdd ? _add : _style) : null;

        bool Choosing => _steps != null && _steps.Step == GuideStep.Outside && _steps.Branch == GuideBranch.None;
        bool Self => _steps != null && _steps.Step == GuideStep.Outside && _steps.Branch == GuideBranch.Self;
        bool HasBack => _steps != null && _steps.Step == GuideStep.Outside;
        bool HasNext => _steps != null && (_steps.Step == GuideStep.Inside || (_steps.Step == GuideStep.Outside && _steps.Branch != GuideBranch.None));
        // The way-on row: Back (on the outside step), Skip setup, then Next or Finish (once there is somewhere to go).
        int NavCount => (HasBack ? 1 : 0) + 1 + (HasNext ? 1 : 0);
        int OptionStart => 3 + NavCount; // "Walls and floor" | "Furniture" while designing the outside by hand
        int ContentStart => OptionStart + (Self ? 2 : 0);

        public int Count => ContentStart + (Choosing ? 2 : Inner != null ? Inner.Count : 0);
        public int Columns => Inner != null ? Inner.Columns : 2;

        /// <summary>The index of the card the ghost hand points at: the first choice, else Next or Finish.</summary>
        public int MainIndex => Choosing ? ContentStart : HasNext ? 3 + NavCount - 1 : -1;

        public void Describe(int index, ref CardInfo card)
        {
            if (_steps == null) return;
            int step = Mathf.Clamp((int)_steps.Step, 0, 3);
            switch (index)
            {
                case 0: card.Kind = CardKind.Title; card.Header = true; card.Row = 0; card.Title = Titles[step]; card.Width = FullWidth - _countWidth - Gap; return;
                case 1: card.Kind = CardKind.Title; card.Caption = true; card.Row = 0; card.Title = _counts[_steps.Number]; card.Width = _countWidth; return;
                case 2: card.Kind = CardKind.Title; card.Caption = true; card.Row = 1; card.Title = Instruction(step); card.Width = FullWidth; card.Lines = 1; return;
            }
            int nav = index - 3;
            if (nav < NavCount)
            {
                card.Kind = CardKind.Chip;
                card.Row = 2;
                card.AlignRight = true;
                int k0 = HasBack ? nav : nav + 1; // 0 Back, 1 Skip setup, 2 Next or Finish
                if (k0 == 0) { card.Title = BackLabel; card.Width = _backWidth; return; }
                if (k0 == 1) { card.Title = SkipLabel; card.Width = _skipWidth; return; }
                bool last = _steps.Step == GuideStep.Outside;
                card.Title = last ? FinishLabel : NextLabel;
                card.Width = last ? _finishWidth : _nextWidth;
                card.Selected = _steps.CanNext; // the way on: the filled pill, once the step is done
                card.Disabled = !_steps.CanNext; // grey until then (core's Onboarding.Satisfied)
                return;
            }
            if (Self && index < ContentStart)
            {
                bool add = index == OptionStart + 1;
                card.Kind = CardKind.Toggle;
                card.Row = 3;
                card.Title = add ? AddOption : StyleOption;
                card.Width = _optionWidth;
                card.Selected = add == _showAdd;
                return;
            }
            int k = index - ContentStart;
            if (Choosing)
            {
                card.Kind = CardKind.Chip;
                card.Row = 4 + k; // one card per row
                card.Title = k == 0 ? AiChoice : SelfChoice;
                card.Width = FullWidth;
                card.Height = 0.056f; // two big cards, one per row: the words are long
                return;
            }
            IPanelPage inner = Inner;
            if (inner == null) return;
            inner.Describe(k, ref card);
            card.Row += 6; // under the guide's rows
        }

        string Instruction(int step)
        {
            if (_steps.Step == GuideStep.Outside)
                switch (_steps.Branch)
                {
                    case GuideBranch.Ai: return WorkAi;
                    case GuideBranch.Self: return _showAdd ? WorkAdd : WorkStyle;
                }
            return Lines[step];
        }

        public bool Press(int index)
        {
            if (_steps == null || index < 3) return false;
            int nav = index - 3;
            if (nav < NavCount)
            {
                int k0 = HasBack ? nav : nav + 1;
                if (k0 == 0) return _steps.Back();
                if (k0 == 1) { _steps.Skip(); return true; }
                if (!_steps.CanNext) return false;
                _steps.Next();
                return true;
            }
            if (Self && index < ContentStart)
            {
                bool add = index == OptionStart + 1;
                if (add != _showAdd) { _showAdd = add; Changed?.Invoke(); }
                return true;
            }
            int k = index - ContentStart;
            if (Choosing) { _steps.Choose(k == 0 ? GuideBranch.Ai : GuideBranch.Self); return true; }
            return Inner != null && Inner.Press(k);
        }

        public bool DragOut(int index) => !Choosing && Inner is IPanelDragOut drag && index >= ContentStart && drag.DragOut(index - ContentStart);
    }
}

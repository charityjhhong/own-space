using System;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// The top strip's icon buttons (undo, redo, settings; minimize as an icon) and the feedback that is not part
    /// of the layout (spec v4 section 7.5): a toast pill above the panel for about 2 s (graphite, or red for a
    /// refusal; pinching it runs its action, e.g. Undo), and a pending-action banner while an action is pending.
    /// The banner (device batch 4) sits right above the panel, near-black with white text in both themes, and stays
    /// until hidden; toasts stack above it.
    /// </summary>
    public sealed partial class ToolPanel
    {
        internal const int UndoButton = Back + 4, RedoButton = Back + 5, SettingsButton = Back + 6, Toast = Back + 7, Hint = Back + 8,
            RowPrev = Back + 9, RowNext = Back + 10, FocusButton = Back + 12; // Back + 11 is Resize
        bool _hintPressable;

        /// <summary>Raised when a pressable hint chip is pinched (e.g. "Skip tips" during onboarding).</summary>
        public event Action HintPressed;
        const float ToastSeconds = 2f, ToastFadeIn = 0.15f, ToastFadeOut = 0.25f, ToastHeight = 0.032f, HintHeight = 0.034f, DotSize = 0.007f,
            HintMaxWidth = 0.46f, Above = 0.012f;
        static readonly Color BannerFill = new Color(0.07f, 0.08f, 0.1f, 0.96f), BannerText = Color.white;

        [SerializeField, Tooltip("Content/ui-icons.json (the atlas texture is on the panel material as _IconMap).")] TextAsset _iconsJson;

        PanelElement _undo, _redo, _settings, _focus, _toast, _hint, _hintDot, _rowPrev, _rowNext;
        float _toastShownAt, _toastUntil, _hintWidth;

        /// <summary>The icon atlas (empty without ui-icons.json).</summary>
        public PanelIcons Icons { get; private set; } = new PanelIcons(null);

        public event Action UndoPressed, RedoPressed, SettingsPressed, ToastPressed;

        /// <summary>Raised when the strip's Focus button (eye icon, next to minimize) is pinched: MR's FocusMode.</summary>
        public event Action FocusPressed;

        /// <summary>The eye button's place in the world (Focus's exit chip appears there: the hand already knows the way).</summary>
        public Vector3 FocusButtonPosition => _focus != null ? _focus.Root.position : transform.position;

        /// <summary>Whether undo (redo) has a step; false greys the arrow and it takes no pinch. Null = always on.</summary>
        public Func<bool> CanUndo { get; set; }
        public Func<bool> CanRedo { get; set; }
        bool UndoOn => CanUndo == null || CanUndo();
        bool RedoOn => CanRedo == null || CanRedo();

        // Grey or normal, after every relayout (the host invalidates when the history changes).
        void ShowUndoStates()
        {
            if (_undo == null) return;
            if (_pressed != UndoButton && _hovered != UndoButton) _undo.SetState(UndoOn ? ElementState.Normal : ElementState.Disabled);
            if (_pressed != RedoButton && _hovered != RedoButton) _redo.SetState(RedoOn ? ElementState.Normal : ElementState.Disabled);
        }

        /// <summary>The eye (Focus) button in the strip; off because minimize does the same job.</summary>
        public static readonly bool ShowFocusButton = false; // readonly, not const: a const false trips CS0429 (warnings are errors)

        void BuildFeedback()
        {
            Icons = new PanelIcons(_iconsJson != null ? _iconsJson.text : null);
            float strip = Top - _metrics.StripTop - _metrics.StripHeight * 0.5f, s = _metrics.StripHeight, left = -_metrics.Size.x * 0.5f + _metrics.Padding;
            _undo = StripButton("Undo", "undo", new Vector2(left + s * 0.5f, strip));
            _redo = StripButton("Redo", "redo", new Vector2(left + s * 1.5f + 0.004f, strip));
            // No eye button (the human): minimize already hides the tools, so the strip keeps one way to do it.
            // The element stays built but never shows or takes a pinch; FocusMode keeps working if a host calls it.
            _focus = StripButton("Focus", "eye", new Vector2(_minimize.Centre.x - s - 0.004f, strip));
            _settings = StripButton("Settings", "settings", new Vector2(_minimize.Centre.x - s - 0.004f, strip));
            _focus.SetVisible(ShowFocusButton);
            if (Icons.Rect("minimize") != Vector4.zero) { _minimize.SetText(""); _minimize.SetIcon(Icons.Rect("minimize")); }
            _toast = new PanelElement(transform, _material, _style, _fonts, "Toast", 13f);
            _hint = new PanelElement(transform, _material, _style, _fonts, "Hint", 13f);
            _hintDot = new PanelElement(transform, _material, _style, _fonts, "HintDot", 13f);
            _hint.SetColors(BannerFill, BannerText); // readable over any room, either theme
            _rowPrev = RowChevron("RowPrev", "chevron-left", "<");
            _rowNext = RowChevron("RowNext", "chevron-right", ">");
            _toast.SetVisible(false);
            _hint.SetVisible(false);
            _hintDot.SetVisible(false);
        }

        // A round chevron at a scrolling row's end: there is more that way; pinch it to scroll.
        PanelElement RowChevron(string name, string icon, string fallback)
        {
            var e = new PanelElement(transform, _material, _style, _fonts, name, 13f);
            Vector4 rect = Icons.Rect(icon);
            if (rect != Vector4.zero) e.SetIcon(rect); else e.SetText(fallback);
            e.SetVisible(false);
            return e;
        }

        // Shows the chevrons on the edges of the first scrolling row that hide items.
        void PlaceRowArrows()
        {
            if (_rowPrev == null) return;
            bool left = false, right = false;
            float y = 0f, width = _metrics.Size.x - 2f * _metrics.Padding;
            if (_arrowRow >= 0 && !_minimized && _shrink == 0f)
            {
                HiddenEnds(_rowScroll[_arrowRow], _rowTotals[_arrowRow], width, out left, out right);
                for (int i = 0; i < _count; i++) if (_cardRow[i] == _arrowRow) { y = _cards[i].Centre.y; break; }
            }
            _rowPrev.SetVisible(left);
            _rowNext.SetVisible(right);
            var size = new Vector2(RowArrow, RowArrow);
            if (left) _rowPrev.Place(new Vector2(-width * 0.5f + RowArrow * 0.5f, y), size, RowArrow * 0.5f);
            if (right) _rowNext.Place(new Vector2(width * 0.5f - RowArrow * 0.5f, y), size, RowArrow * 0.5f);
        }

        internal bool RowArrowShown(int id) => id == RowPrev ? _rowPrev.Visible : id == RowNext && _rowNext.Visible;

        PanelElement StripButton(string name, string icon, Vector2 at)
        {
            var e = new PanelElement(transform, _material, _style, _fonts, name, 13f);
            e.Place(at, new Vector2(_metrics.StripHeight, _metrics.StripHeight), _metrics.StripHeight * 0.5f);
            e.SetIcon(Icons.Rect(icon));
            return e;
        }

        /// <summary>Shows a toast above the panel for about 2 s: graphite, or red when <paramref name="refused"/>.</summary>
        public void ShowToast(string text, bool refused = false)
        {
            Build();
            _toast.SetText(text);
            _toast.SetState(refused ? ElementState.Refused : ElementState.Selected);
            PlaceToast(Mathf.Min(_metrics.Size.x, _toast.PreferredWidth(text) + 0.024f));
            _toast.SetVisible(true);
            _toastShownAt = Time.time;
            _toastUntil = Time.time + ToastSeconds;
            _toast.SetFade(0f); // fades in over 150 ms, out over the last 250 ms (spec 7.5)
        }

        /// <summary>Shows the pending-action banner above the panel until <see cref="HideHint"/> (e.g. "Pinch the floor … · Cancel").</summary>
        /// <param name="pressable">True when pinching the banner does something (<see cref="HintPressed"/>, e.g. Cancel).</param>
        public void ShowHint(string text, bool pressable = false)
        {
            Build();
            _hintPressable = pressable;
            _hint.SetText(text);
            _hintWidth = Mathf.Min(HintMaxWidth, _hint.PreferredWidth(text) + 0.028f + DotSize + 0.006f); // measured once; longer ends in an ellipsis
            _hintDot.SetSwatch(_style != null ? _style.Accent : PanelStyle.Default.Accent); // the accent dot: an action is pending
            _hint.SetVisible(true);
            _hintDot.SetVisible(true);
            PlaceHint();
        }

        public void HideHint()
        {
            _hintPressable = false;
            if (!_built) return;
            _hint.SetVisible(false);
            _hintDot.SetVisible(false);
            if (_toast.Visible) PlaceToast(_toast.Size.x);
        }

        internal bool ToastShown => _built && _toast.Visible;
        internal bool HintShown => _built && _hint.Visible;
        internal float HintCentreY => _hint.Centre.y;

        // The banner sits right above the panel's top edge (always in view, unlike the panel's bottom).
        void PlaceHint()
        {
            if (_hint == null || !_hint.Visible) return;
            float y = Top + Above + HintHeight * 0.5f;
            _hint.Place(new Vector2(0f, y), new Vector2(_hintWidth, HintHeight), HintHeight * 0.5f);
            _hintDot.Place(new Vector2(-_hintWidth * 0.5f + 0.012f + DotSize * 0.5f, y), new Vector2(DotSize, DotSize), DotSize * 0.5f);
            if (_toast != null && _toast.Visible) PlaceToast(_toast.Size.x);
        }

        // Toasts go above the panel, or above the banner while one shows.
        void PlaceToast(float width)
        {
            float y = Top + Above + (_hint != null && _hint.Visible ? HintHeight + 0.008f : 0f) + ToastHeight * 0.5f;
            _toast.Place(new Vector2(0f, y), new Vector2(width, ToastHeight));
        }

        void TickFeedback()
        {
            if (_toast == null || !_toast.Visible) return;
            if (Time.time >= _toastUntil) { _toast.SetVisible(false); return; }
            _toast.SetFade(Mathf.Min((Time.time - _toastShownAt) / ToastFadeIn, (_toastUntil - Time.time) / ToastFadeOut));
        }

        bool TryFindFeedback(Vector2 p, float pad, out int id)
        {
            id = -1;
            if (_toast != null && _toast.Visible && PanelLayout.Contains(_toast.Centre, _toast.Size, p, pad)) id = Toast;
            else if (_hintPressable && _hint != null && _hint.Visible && PanelLayout.Contains(_hint.Centre, _hint.Size, p, pad)) id = Hint;
            else if (_minimized || _shrink > 0f) return false;
            else if (_rowPrev.Visible && PanelLayout.Contains(_rowPrev.Centre, _rowPrev.Size, p, pad)) id = RowPrev;
            else if (_rowNext.Visible && PanelLayout.Contains(_rowNext.Centre, _rowNext.Size, p, pad)) id = RowNext;
            else if (UndoOn && PanelLayout.Contains(_undo.Centre, _undo.Size, p, pad)) id = UndoButton;
            else if (RedoOn && PanelLayout.Contains(_redo.Centre, _redo.Size, p, pad)) id = RedoButton;
            else if (PanelLayout.Contains(_settings.Centre, _settings.Size, p, pad)) id = SettingsButton;
            else if (ShowFocusButton && PanelLayout.Contains(_focus.Centre, _focus.Size, p, pad)) id = FocusButton;
            return id >= 0;
        }

        PanelElement FeedbackElement(int id) => id == UndoButton ? _undo : id == RedoButton ? _redo : id == SettingsButton ? _settings
            : id == FocusButton ? _focus : id == Hint ? _hint
            : id == RowPrev ? _rowPrev : id == RowNext ? _rowNext : _toast;

        void ReleaseFeedback(int id)
        {
            if (id == UndoButton) UndoPressed?.Invoke();
            else if (id == RedoButton) RedoPressed?.Invoke();
            else if (id == SettingsButton) SettingsPressed?.Invoke();
            else if (id == FocusButton) FocusPressed?.Invoke();
            else if (id == Toast) { _toast.SetVisible(false); ToastPressed?.Invoke(); }
            else if (id == Hint) HintPressed?.Invoke();
            else if (id == RowPrev) ScrollRowBy(_arrowRow, -1);
            else if (id == RowNext) ScrollRowBy(_arrowRow, 1);
        }

        void ShowStripButtons(bool shown)
        {
            _undo.SetVisible(shown); _redo.SetVisible(shown); _settings.SetVisible(shown); _focus.SetVisible(shown && ShowFocusButton);
            if (!shown) { _rowPrev.SetVisible(false); _rowNext.SetVisible(false); } else PlaceRowArrows();
        }
    }
}

using System;
using System.Text;
using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The AI Designer's typed request ("Describe the room you want"). Pinching the field opens our own keyboard
    /// (<see cref="PanelKeyboard"/>) docked under the tool panel. Device batch 10: the Quest system overlay timed out and
    /// never showed, and Meta's in-app Virtual Keyboard is gone from SDK 207. Typing only, no voice.
    /// <list type="bullet">
    /// <item>Send (on the keyboard or the AI tab) passes the words to <see cref="AskMenu.AskText"/>: cleaned, clipped to 200 characters,
    /// the same busy/fallback/cancel/undo flow as the presets. A sent request clears the field.</item>
    /// <item>Cancel, leaving the AI tab, pausing, or losing both hands for a second closes the keyboard and keeps the typed text.</item>
    /// </list>
    /// "Open" is true only while the keyboard is actually on screen. Runs after the panel (-10) and before the
    /// placers (-5) and EditMode (0), so a pinch on a key is never also a pinch on the floor.
    /// </summary>
    [DefaultExecutionOrder(-9)]
    public sealed class FreeTextRequest : MonoBehaviour, IRequestField
    {
        const string LogTag = "[OwnSpace.MR]";
        public const string Placeholder = "Describe your ideal space"; // the human: a short phrase meaning "tell what you want in your space"
        public const string NoKeyboard = "The keyboard did not open. Try again.", BusyText = "The AI is still working on the last request",
            EmptyText = "Type what you'd like first";
        /// <summary>
        /// Both hands may drop out of tracking this long before the keyboard closes (device, build 2dc475d: it closed twice a
        /// few seconds after opening, when tracking dropped for a moment while the user looked down at the keys). A short
        /// loss, 1.5 s and more, is ridden out; only a long one closes it, and the typed text is kept either way.
        /// </summary>
        public const float LostHandsGrace = 4f;
        /// <summary>The keyboard's own line when nothing is typed: a caret only. The placeholder lives in the panel's field.</summary>
        public const string Caret = "|";
        const float FlashSeconds = 0.12f;
        const int ShownChars = 64; // the most the keyboard's one-line echo strip is asked to fit; it shows the end of longer text

        [SerializeField] AskMenu _ask;
        [SerializeField] HandRig _hands;
        ToolPanel _panel; // PanelHost hands it over (UsePanel)

        readonly StringBuilder _typed = new StringBuilder(AskMenu.MaxRequestLength);
        readonly StringBuilder _shown = new StringBuilder(ShownChars + 4);
        string _text = "";
        PanelKeyboard _keyboard;
        float _handsLostFor, _flashFor;
        int _flash = -1;
        bool _open, _shift;

        /// <summary>Whether the AI is busy; tests replace it.</summary>
        internal Func<bool> IsBusy;
        bool Busy => IsBusy != null ? IsBusy() : _ask != null && _ask.Busy;
        /// <summary>Shift is on: the next letter is a capital.</summary>
        public bool Shift => _shift;

        /// <summary>Sends cleaned words; tests replace it.</summary>
        internal Func<string, bool> SendText;

        /// <summary>Whether the keyboard is on screen.</summary>
        public bool IsOpen => _open;
        /// <summary>The typed text (kept across close and reopen until sent).</summary>
        public string Text => _text;

        /// <summary>Raised when the typed text or the open state changes (the AI tab re-describes).</summary>
        public event Action Changed;
        /// <summary>Raised when the request could not open or send, with why.</summary>
        public event Action<string> Failed;

        void Awake() { if (SendText == null && _ask != null) SendText = _ask.AskText; }

        void OnEnable() { if (_hands != null) _hands.RecenterSettled += OnRecenterSettled; }

        // After a recenter the open keyboard goes back under the panel, once the panel's own ease to its seat is over.
        void OnRecenterSettled() { if (_open && _keyboard != null) _replaceKeyboard = 0.4f; }
        float _replaceKeyboard;

        /// <summary>The panel the keyboard docks under (PanelHost, at start).</summary>
        public void UsePanel(ToolPanel panel) => _panel = panel;

        // Naming a saved space (Saved spaces): the same keyboard types a short name instead of an AI request.
        Func<string, bool> _nameDone;
        string _aiText = "";
        public const int NameMax = 40;
        public const string NamePlaceholder = "Name this space";
        /// <summary>Whether the keyboard is typing a space's name (not an AI request).</summary>
        public bool Naming => _nameDone != null;
        int MaxChars => _nameDone != null ? NameMax : AskMenu.MaxRequestLength;

        /// <summary>
        /// Opens the keyboard to type a name (starting from <paramref name="current"/>). Create calls
        /// <paramref name="done"/> with the trimmed name; true closes it. The unsent AI text is kept and comes back after.
        /// </summary>
        public bool OpenForName(string current, Func<string, bool> done)
        {
            if (_nameDone == null) _aiText = _text;
            _nameDone = done;
            _typed.Clear();
            if (!string.IsNullOrEmpty(current)) _typed.Append(current, 0, Math.Min(current.Length, NameMax));
            _text = _typed.ToString();
            InputDebug.Log("spaces: naming keyboard");
            return ShowKeyboard();
        }

        void EndNaming()
        {
            if (_nameDone == null) return;
            _nameDone = null;
            _typed.Clear().Append(_aiText);
            _text = _aiText;
            ShowText();
        }

        /// <summary>Shows the keyboard under the panel; false when it cannot.</summary>
        public bool Open()
        {
            InputDebug.Log($"ai field: open (busy {Busy}, already open {_open}, panel {(_panel != null)})");
            if (Busy) { Failed?.Invoke(BusyText); return false; }
            EndNaming();
            return ShowKeyboard();
        }

        bool ShowKeyboard()
        {
            if (_panel == null)
            {
                Debug.LogWarning($"{LogTag} No tool panel to dock the keyboard under.");
                Failed?.Invoke(NoKeyboard);
                return false;
            }
            if (_keyboard == null)
            {
                _keyboard = new PanelKeyboard(transform, _panel); // its own floating piece, not part of the panel
                _panel.ThemeChanged += _ => _keyboard.Restyle(_panel.CurrentStyle);
            }
            PlaceKeyboard();
            _keyboard.Show();
            ShowText();
            SetOpen(true);
            InputDebug.Log("ai field: keyboard shown");
            return true;
        }

        /// <summary>Hides the keyboard; the typed text stays for next time.</summary>
        public void Close()
        {
            if (!_open) return;
            _keyboard?.Hide();
            EndNaming();
            SetOpen(false);
            InputDebug.Log($"ai field: keyboard closed, {_text.Length} characters kept");
        }

        void SetOpen(bool open)
        {
            _open = open;
            _handsLostFor = 0f;
            Changed?.Invoke();
        }

        /// <summary>Sends the typed text (the keyboard's or the AI tab's Send); false with a reason when it cannot.</summary>
        public bool Send()
        {
            if (_nameDone != null)
            {
                string name = AskMenu.CleanRequest(_text);
                if (name.Length == 0) { Failed?.Invoke("Type a name first"); return false; }
                if (!_nameDone(name)) return false;
                Close(); // ends naming: the AI text comes back
                return true;
            }
            string clean = AskMenu.CleanRequest(_text);
            if (clean.Length == 0) { Failed?.Invoke(EmptyText); return false; }
            if (Busy) { Failed?.Invoke(BusyText); return false; }
            bool sent = SendText != null && SendText(clean);
            InputDebug.Log($"ai field: send, {clean.Length} characters, sent {sent}");
            if (!sent) { Failed?.Invoke("Nothing was sent (the AI is busy, or no zone yet)"); return false; }
            _typed.Clear();
            _text = "";
            ShowText();
            Close();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Acts on one key (pure enough for tests).</summary>
        internal void PressKey(int key)
        {
            if (key < 0 || key >= PanelKeyboard.KeyCount) return;
            switch (PanelKeyboard.ActOf(key))
            {
                case PanelKeyboard.Act.Shift:
                    SetShift(!_shift);
                    return;
                case PanelKeyboard.Act.Char:
                case PanelKeyboard.Act.Space:
                    if (_typed.Length >= MaxChars) return;
                    char c = PanelKeyboard.CharOf(key, _shift);
                    if (_shift && char.IsLetter(c)) SetShift(false); // one shot
                    if (c == ' ' && (_typed.Length == 0 || _typed[_typed.Length - 1] == ' ')) return; // no leading or double spaces
                    _typed.Append(c);
                    break;
                case PanelKeyboard.Act.Delete:
                    if (_typed.Length == 0) return;
                    _typed.Length--;
                    break;
                case PanelKeyboard.Act.Send: Send(); return;
                case PanelKeyboard.Act.Cancel: Close(); return;
            }
            _text = _typed.ToString(); // a key press, not a frame: the one string per key
            ShowText();
            Changed?.Invoke();
        }

        void SetShift(bool on)
        {
            _shift = on;
            _keyboard?.SetShift(on);
        }

        void ShowText()
        {
            if (_keyboard == null) return;
            _shown.Clear();
            string shown;
            // Empty until the user types (the human, build 2dc475d: the sentence must not show on the keyboard too).
            if (_typed.Length == 0) shown = Caret;
            else
            {
                int from = Mathf.Max(0, _typed.Length - ShownChars);
                _shown.Append(_typed, from, _typed.Length - from).Append('|');
                // Full size always: the end of the text that fits the line, never shrunk (a key press, not a frame).
                shown = TextTail.Fit(_shown.ToString(), PanelKeyboard.TextWidth, PanelKeyboard.TextMm, _panel != null ? _panel.Fonts?.Regular : null);
            }
            _keyboard.SetText(shown, AskMenu.CleanRequest(_text).Length > 0);
        }

        // Device feedback on 04ad24c: the panel jumped up when the keyboard opened. The panel never moves now. Device feedback
        // on 0ddb26d: the keyboard was three times the panel's width; it is sized to the panel and docked under it.
        void PlaceKeyboard()
        {
            Transform t = _panel.transform;
            Vector3 bottom = t.TransformPoint(new Vector3(0f, _panel.Metrics.Size.y * 0.5f - _panel.Height, 0f));
            Vector3 head = _hands != null ? _hands.HeadPosition : bottom - t.forward * 0.6f + Vector3.up * 0.3f;
            Vector3 forward = _hands != null ? _hands.View.direction : (bottom - head).normalized;
            PanelKeyboard.Dock(head, forward, bottom, t.rotation, _panel.Metrics.Size.x * _panel.Scale, _panel.Surfaces, out Vector3 p, out Quaternion r, out float scale);
            _keyboard.Place(p, r, scale);
            _dockedScale = _panel.Scale;
            _userMoved = false;
            LogSize(head);
        }

        void LogSize(Vector3 head)
        {
            Vector3 p = _keyboard.Root.position;
            float width = PanelKeyboard.Width * _keyboard.Scale, distance = Vector3.Distance(head, p);
            InputDebug.Log($"ai field: keyboard at {p:F2}, {distance:F2} m from the eyes, {width:F2} m wide ({PanelKeyboard.AngularSize(width, distance):F0} deg across, panel {_panel.Metrics.Size.x * _panel.Scale:F2} m), keys {PanelKeyboard.KeyHeight * _keyboard.Scale * 1000f:F0} mm");
        }

        // The panel was resized while the keyboard is open: it follows (re-docked, or only re-sized where the user put it).
        void FollowPanelScale()
        {
            if (Mathf.Approximately(_dockedScale, _panel.Scale)) return;
            if (!_userMoved) { PlaceKeyboard(); return; }
            _dockedScale = _panel.Scale;
            Transform k = _keyboard.Root;
            _keyboard.Place(k.position, k.rotation, PanelKeyboard.ScaleFor(_panel.Metrics.Size.x * _panel.Scale, Vector3.Distance(_hands.HeadPosition, k.position)));
            LogSize(_hands.HeadPosition);
        }

        float _dockedScale;
        bool _userMoved;
        bool _moving, _moveLeft;
        float _moveDistance;

        void Update()
        {
            if (!_open || CloseIfTabGone()) return;
            if (_hands == null) return;
            if (_replaceKeyboard > 0f && (_replaceKeyboard -= Time.deltaTime) <= 0f) PlaceKeyboard();
            if (Tick(Time.deltaTime, _hands.AnyHandTracked)) return;
            FollowPanelScale();

            HandState right = _hands.Right, left = _hands.Left;
            if (_moving)
            {
                // Its own grab bar: the keyboard follows the ray at the grab distance, still facing the eyes.
                HandState owner = _moveLeft ? left : right;
                if (owner.PinchEnded || owner.TrackingLost || !owner.Tracked) { _moving = false; return; }
                Vector3 at = _hands.Aim(_moveLeft).GetPoint(_moveDistance);
                _keyboard.Root.SetPositionAndRotation(at, PanelKeyboard.Facing(_hands.HeadPosition, at));
                return;
            }
            Vector3 pinch = right.Tracked ? right.PinchPoint : left.PinchPoint;
            int hover = _keyboard.Hit(_hands.Gaze, pinch, right.Tracked || left.Tracked);
            if (right.PinchStarted || left.PinchStarted)
            {
                bool isLeft = !right.PinchStarted;
                HandState hand = isLeft ? left : right;
                int key = _keyboard.Hit(_hands.Aim(isLeft), hand.PinchPoint, true);
                if (key != -1 && _hands.Claim())
                {
                    if (key == PanelKeyboard.Bar)
                    {
                        _moving = _userMoved = true;
                        _moveLeft = isLeft;
                        _moveDistance = Vector3.Distance(_hands.Aim(isLeft).origin, _keyboard.Root.position);
                        return;
                    }
                    if (key >= 0)
                    {
                        InputDebug.Log($"ai field: key {PanelKeyboard.LabelOf(key)}");
                        _flash = key;
                        _flashFor = FlashSeconds;
                        PressKey(key);
                    }
                    if (!_open) return;
                }
            }
            if (_flashFor > 0f) { _flashFor -= Time.deltaTime; if (_flashFor <= 0f) _flash = -1; }
            _keyboard.SetHover(hover >= 0 ? hover : -1, _flash);
        }

        /// <summary>Closes the keyboard when the AI tab is no longer showing (tab change, minimise, panel off). True when it closed.</summary>
        /// <summary>Whether the AI page is showing (the AI tab, or the guided first run's "Let AI design" step); PanelHost sets it.</summary>
        public Func<bool> AiPageShown { get; set; }
        bool AiShown() => AiPageShown != null ? AiPageShown() : _panel.CurrentTab == PanelHost.AiTab;

        internal bool CloseIfTabGone()
        {
            if (!_open) return true;
            if (_panel != null && _panel.isActiveAndEnabled && !_panel.Minimized && (_nameDone != null ? _panel.Sheet != null : AiShown())) return false;
            InputDebug.Log("ai field: the AI tab is gone, closing the keyboard");
            Close();
            return true;
        }

        /// <summary>Hand loss: both hands gone for a second closes the keyboard (text kept). True when it closed.</summary>
        internal bool Tick(float deltaTime, bool handsTracked)
        {
            if (!_open) return true;
            _handsLostFor = handsTracked ? 0f : _handsLostFor + deltaTime;
            if (_handsLostFor <= LostHandsGrace) return false;
            InputDebug.Log("ai field: hands lost, closing the keyboard");
            Close();
            return true;
        }

        internal PanelKeyboard Keyboard => _keyboard;

        /// <summary>Opens without a panel (tests).</summary>
        internal void OpenForTest() => SetOpen(true);

        internal void Pause(bool paused) { if (paused) Close(); }
        void OnApplicationPause(bool paused) => Pause(paused);
        void OnDisable() { Close(); if (_hands != null) _hands.RecenterSettled -= OnRecenterSettled; }
    }
}

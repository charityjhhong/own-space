using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Focus: the core scene, working on the real laptop in the virtual room (the human). Entered from the panel's
    /// Focus button: the panel, the ray and reticle, edit outlines, handles, labels and onboarding hints hide; the room,
    /// furniture, mood and the real desk stay. The development perf overlay is not touched.
    /// <para>Exit, two ways (device bug on fa32819: a chip low beside the desk could not be aimed at, the aim gate hides a ray
    /// pointing down at the body):
    /// a small dim "Exit focus" chip where the panel's eye button was (the hand reaches it the way it reached the button),
    /// pushed aside off the laptop in the middle of the desk; it shows while a hand aims and stays <see cref="Linger"/> s
    /// after, and a pinch aimed at it or made at it (no aim needed) exits.
    /// Or, with no aim at all: a pinch held <see cref="HoldSeconds"/> s with the hand raised (no more than
    /// <see cref="RaisedDrop"/> below the eyes; typing hands are lower). A palm-up pose was not used: it is Meta's own system
    /// gesture, which drops hand tracking while it runs.
    /// Losing hand tracking or pausing keeps focus. For this session only (never saved).</para>
    /// <para>The same hiding happens on its own while system UI has the input (the Meta menu, YouTube, the remote-desktop
    /// display over the app: OpenXR visible but not focused, or paused): the room, furniture, mood and passthrough keep
    /// rendering exactly as they are, and the tools come back as they were when the app has the input again (the human,
    /// Own Space as the background room). No exit chip then: the app has no input.</para>
    /// No per-frame allocation: the chip's place is worked out when it appears, not every frame.
    /// </summary>
    public sealed class FocusMode : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        public const string ExitLabel = "Exit focus";
        /// <summary>How close the aim ray must pass to the chip's centre to pinch it (m): generous, there is no reticle.</summary>
        public const float HitRadius = 0.09f;
        /// <summary>A pinch made this close to the chip (m) presses it with no aim (a direct touch).</summary>
        public const float TouchRadius = 0.08f;
        /// <summary>The chip stays this long (s) after the aim gate drops: no flicker while the hand moves to it.</summary>
        public const float Linger = 2f;
        /// <summary>The no-aim way out: a pinch held this long (s) with the hand at most <see cref="RaisedDrop"/> (m) below the eyes.</summary>
        public const float HoldSeconds = 1f, RaisedDrop = 0.3f;
        /// <summary>The chip keeps this far (m) to the side of the desk's centre line, where the laptop is.</summary>
        public const float LaptopClear = 0.22f;
        /// <summary>Without the eye button's place: about where the panel seats (below the eyes, to the right, ahead; m).</summary>
        public const float NoSeatDrop = 0.3f, NoSeatSide = 0.25f, NoSeatForward = 0.5f;

        /// <summary>
        /// True while the tools are hidden: focus is on, or system UI has the input. Read by the pieces that draw tools
        /// (reticle, edit mode, onboarding).
        /// </summary>
        public static bool Active { get; private set; }

        /// <summary>True while the user's Focus is on (the exit chip can bring the tools back).</summary>
        public static bool UserFocus { get; private set; }

        /// <summary>True while system UI has the input (the app is visible but not focused, or paused).</summary>
        public static bool SystemHasInput { get; private set; }

        /// <summary>Whether the tools are hidden (pure): the user's Focus, or system UI over the app.</summary>
        public static bool ToolsHidden(bool userFocus, bool systemHasInput) => userFocus || systemHasInput;

        /// <summary>Whether the exit chip may show (pure): only in the user's Focus, with the input, while a hand aims.</summary>
        public static bool ChipShows(bool userFocus, bool systemHasInput, bool aiming) => userFocus && !systemHasInput && aiming;

        /// <summary>True while the exit chip shows (a hand raised and aimed): the reticle may draw then, so the chip can be aimed at.</summary>
        public static bool ChipShown { get; private set; }

        /// <summary>Raised on entering (true) and leaving (false).</summary>
        public static event Action<bool> Changed;

        HandRig _hands;
        IRoomInfo _room;
        Func<FurnitureAnchor> _desk;
        WorldLabel _chip;
        Vector3 _chipAt;
        Vector3? _seat;
        float _sinceAim = Linger, _holdLeft, _holdRight;

        /// <summary>Wires the hands and the room; <paramref name="desk"/> gives the desk or bed in my zone, or null.</summary>
        public void Use(HandRig hands, IRoomInfo room, Func<FurnitureAnchor> desk)
        {
            _hands = hands;
            _room = room;
            _desk = desk;
        }

        /// <summary>
        /// Turns focus on (the panel's Focus button). The host hides the panel on <see cref="Changed"/>.
        /// <paramref name="eyeButton"/> is where the button was: the exit chip appears there.
        /// </summary>
        public void Enter(Vector3? eyeButton = null)
        {
            if (UserFocus) return;
            UserFocus = true;
            _seat = eyeButton;
            _sinceAim = Linger;
            _holdLeft = _holdRight = 0f;
            Debug.Log($"{LogTag} Focus on: tools hidden; pinch \"{ExitLabel}\" where the eye button was, or hold a raised pinch {HoldSeconds:0} s, to leave.");
            Refresh();
        }

        /// <summary>Turns focus off (the exit chip, or the host). The host shows the panel again on <see cref="Changed"/>.</summary>
        public void Exit()
        {
            if (!UserFocus) return;
            UserFocus = false;
            ShowChip(false);
            Debug.Log($"{LogTag} Focus off: tools back.");
            Refresh();
        }

        /// <summary>System UI took the input (true) or gave it back (false). Called by Unity's focus and pause callbacks.</summary>
        public void SetSystemHasInput(bool system)
        {
            if (SystemHasInput == system) return;
            SystemHasInput = system;
            if (system) ShowChip(false);
            Debug.Log($"{LogTag} {(system ? "System UI has the input: tools hidden, the room keeps rendering." : "The app has the input again: tools as they were.")}");
            Refresh();
        }

        // Raises Changed only when the tools' visibility flips (Focus during system UI, or the other way, changes nothing).
        void Refresh()
        {
            bool hidden = ToolsHidden(UserFocus, SystemHasInput);
            if (hidden == Active) return;
            Active = hidden;
            Changed?.Invoke(hidden);
        }

        bool _inputLost, _paused;

        /// <summary>
        /// Whether system UI has the input after an input event (pure): Unity's focus callback and Meta's
        /// InputFocusLost / InputFocusAcquired report the same change, in either order (device logs show both); the latest
        /// one wins, so a second report of the same state changes nothing. A pause hides the tools as well.
        /// </summary>
        public static bool AfterInputEvent(bool inputLost, bool paused) => inputLost || paused;

        // Unity's callbacks and Meta's events, whichever comes first. On the headset only: in the Editor, clicking another
        // window must not hide the tools.
        void OnApplicationFocus(bool focused) => InputEvent(!focused);
        void OnApplicationPause(bool paused) { _paused = paused; Apply(); }
        void OnInputFocusLost() => InputEvent(true);
        void OnInputFocusAcquired() => InputEvent(false);

        void InputEvent(bool lost) { _inputLost = lost; Apply(); }

        void Apply() { if (!Application.isEditor) SetSystemHasInput(AfterInputEvent(_inputLost, _paused)); }

        void OnEnable()
        {
            OVRManager.InputFocusLost += OnInputFocusLost;
            OVRManager.InputFocusAcquired += OnInputFocusAcquired;
        }

        void OnDisable()
        {
            OVRManager.InputFocusLost -= OnInputFocusLost;
            OVRManager.InputFocusAcquired -= OnInputFocusAcquired;
        }

        void OnDestroy() { Active = UserFocus = SystemHasInput = ChipShown = false; }

        void Update()
        {
            if (_chip != null) _chip.Tick();
            if (!UserFocus || SystemHasInput || _hands == null) { _holdLeft = _holdRight = 0f; return; }
            float dt = Time.deltaTime;
            bool leftAims = _hands.HasAim(true), rightAims = _hands.HasAim(false);
            HandState left = _hands.Left, right = _hands.Right;
            Vector3 head = _hands.HeadPosition;

            // The no-aim way out: a raised pinch held a second. Tracking lost or paused: no pinch, the hold starts over.
            _holdLeft = Hold(_holdLeft, left, head, dt);
            _holdRight = Hold(_holdRight, right, head, dt);
            if (_holdLeft >= HoldSeconds || _holdRight >= HoldSeconds)
            {
                Debug.Log($"{LogTag} Focus: left by a held pinch ({(_holdRight >= HoldSeconds ? "right" : "left")} hand).");
                Exit();
                return;
            }

            _sinceAim = leftAims || rightAims ? 0f : _sinceAim + dt;
            bool holding = _holdLeft > 0f || _holdRight > 0f; // the chip shows what the hold will do
            bool chip = ChipShows(UserFocus, SystemHasInput, ChipWanted(_sinceAim, holding));
            if (chip && !ChipShown) { _chipAt = ChipPlace(); ShowChip(true); }
            else if (!chip && ChipShown) ShowChip(false);
            if (!ChipShown) return;

            bool rightOn = Presses(right, rightAims, _hands.Aim(false), _chipAt), leftOn = Presses(left, leftAims, _hands.Aim(true), _chipAt);
            if ((rightOn || leftOn) && !_hands.ClaimedThisFrame)
            {
                _hands.Claim();
                Debug.Log($"{LogTag} Focus: left by the chip ({(rightOn ? "right" : "left")} hand).");
                Exit();
            }
        }

        /// <summary>Whether the chip is wanted (pure): a hand aims now or did within <see cref="Linger"/> s, or a raised pinch is held.</summary>
        public static bool ChipWanted(float sinceAim, bool holding) => sinceAim < Linger || holding;

        /// <summary>
        /// A hand presses the chip this frame (pure): a pinch starts either aimed at it (its ray shown) or made at it
        /// (within <see cref="TouchRadius"/>, no aim needed).
        /// </summary>
        public static bool Presses(HandState hand, bool aims, Ray aim, Vector3 chip) =>
            hand.Tracked && hand.PinchStarted &&
            ((aims && Aims(aim, chip)) || (hand.PinchPoint - chip).sqrMagnitude <= TouchRadius * TouchRadius);

        /// <summary>
        /// The held-pinch timer (pure): grows while the hand is tracked, pinching and raised (at most <see cref="RaisedDrop"/>
        /// below the eyes), back to 0 otherwise. It counts only a pinch that started after focus began (the pinch on the eye
        /// button never carries over). Typing and resting hands are lower, so they never get there.
        /// </summary>
        public static float Hold(float held, HandState hand, Vector3 head, float dt) =>
            hand.Tracked && hand.Pinching && !hand.TrackingLost && (held > 0f || hand.PinchStarted) &&
            head.y - hand.PinchPoint.y <= RaisedDrop ? held + Mathf.Max(dt, 1e-4f) : 0f;

        /// <summary>Whether <paramref name="aim"/> passes within <see cref="HitRadius"/> of <paramref name="chip"/>, in front of the hand (pure).</summary>
        public static bool Aims(Ray aim, Vector3 chip)
        {
            Vector3 to = chip - aim.origin;
            float along = Vector3.Dot(to, aim.direction);
            if (along <= 0f) return false;
            return (to - aim.direction * along).sqrMagnitude <= HitRadius * HitRadius;
        }

        void ShowChip(bool show)
        {
            ChipShown = show;
            if (_chip == null) { if (!show) return; _chip = new WorldLabel(transform, "ExitFocus", new Color(1f, 1f, 1f, 0.7f)); }
            if (show) _chip.Show(ExitLabel, _chipAt, _hands != null ? _hands.HeadPosition : _chipAt + Vector3.back);
            else if (_chip.Shown) _chip.Hide();
        }

        Vector3 ChipPlace()
        {
            Vector3 head = _hands.HeadPosition;
            Vector3 at = _seat ?? NoSeat(head, _hands.View.direction);
            FurnitureAnchor desk = _desk?.Invoke();
            return desk != null ? OffLaptop(at, desk.Box, head) : at;
        }

        /// <summary>
        /// The chip kept off the laptop (pure): if it is within <see cref="LaptopClear"/> of the desk's centre line (seen from
        /// the head), it moves sideways to that distance, on the side it was already on (the right when centred). Height
        /// and depth stay: it is still where the hand reached the eye button.
        /// </summary>
        public static Vector3 OffLaptop(Vector3 chip, OrientedBox desk, Vector3 head)
        {
            Vector3 centre = new Vector3(desk.center.x, desk.center.y, desk.center.z);
            Vector3 forward = centre - head; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return chip;
            Vector3 right = Vector3.Cross(Vector3.up, forward.normalized);
            float lateral = Vector3.Dot(chip - centre, right);
            if (Mathf.Abs(lateral) >= LaptopClear) return chip;
            float side = lateral < 0f ? -1f : 1f;
            return chip + right * (side * LaptopClear - lateral);
        }

        /// <summary>Without the eye button's place (pure): about where the panel seats, below the eyes, right, ahead.</summary>
        public static Vector3 NoSeat(Vector3 head, Vector3 viewDirection)
        {
            Vector3 f = viewDirection; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return head + f * NoSeatForward + right * NoSeatSide + Vector3.down * NoSeatDrop;
        }
    }
}

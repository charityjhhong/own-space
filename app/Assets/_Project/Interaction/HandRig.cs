using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OwnSpace.Interaction
{
    /// <summary>One hand's pinch, read once per frame by <see cref="HandRig"/>.</summary>
    public struct HandState
    {
        public bool Tracked;
        public bool Pinching;
        public bool PinchStarted;
        public bool PinchEnded;

        /// <summary>
        /// True from the frame tracking (or focus) was lost until the pinch is released after it
        /// comes back, so a drag stays cancelled even if the hand is re-tracked on the next frame.
        /// While true, the hand reports no pinch.
        /// </summary>
        public bool TrackingLost;

        /// <summary>Tracked, but the runtime's confidence is low (dim light): fine drags hold still rather than jump.</summary>
        public bool LowConfidence;

        /// <summary>World point between thumb tip and index tip (the hand anchor when bones are not ready).</summary>
        public Vector3 PinchPoint;
    }

    /// <summary>
    /// Hands-only input for the whole app: pinch per hand, the pinch point, and the far-targeting ray
    /// (<see cref="Gaze"/>): the Quest-standard hand ray (the system pointer pose) of the hand that
    /// pinches, else of the right hand, else of the left, and the head ray only when no hand has one.
    /// Reads the camera rig's <see cref="OVRHand"/>/<see cref="OVRSkeleton"/> found once at start;
    /// no allocation per frame. Other scripts read it in their own Update (it runs first).
    /// In the Editor only (Meta XR Simulator, its webcam hand tracking, Link), pinch also counts when
    /// the runtime reports the index pinch flag or the thumb and index tips touch, because the
    /// simulator may not fill the aim pinch strength (its left-click only animates the pinch pose).
    /// Also in the Editor only, mouse mode (M toggles it, off by default): the gaze follows the mouse
    /// cursor through the camera and the left button is a right-hand pinch on that ray; webcam hands
    /// keep working, but the mouse wins while its button is held.
    /// Headset builds use the runtime pinch strength and the hand rays.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class HandRig : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.Interaction]";

        /// <summary>Gaze hit radius (m) around every pinch target: knobs, buttons, tiles, orbs.</summary>
        public const float GazeHitRadius = 0.12f;

        [SerializeField, Tooltip("Pinch strength above which a pinch starts.")]
        float _pinchOn = 0.85f;

        [SerializeField, Tooltip("Pinch strength below which a pinch ends (hysteresis against flicker).")]
        float _pinchOff = 0.6f;

        [SerializeField, Tooltip("Seconds between looks for hand rigs that are missing (spawned later).")]
        float _rescanInterval = 1f;

        OVRHand _leftHand, _rightHand;
        float _nextScan;
        OVRSkeleton _leftSkeleton, _rightSkeleton;
        Transform _head;
        HandState _left, _right;
        Ray _leftRay, _rightRay;
        bool _leftRayValid, _rightRayValid;
        float _leftAimFor, _rightAimFor;
        readonly AimSteady _leftSteady = new AimSteady(), _rightSteady = new AimSteady();
        bool _recenteredSinceLaunch;
        string _leftWhy = "", _rightWhy = "";

        /// <summary>
        /// The ray shows (and picks far) only while a hand is really aiming (the human, build 754b485: "the ray shows up weird
        /// again": two long rays from hands typing on a laptop). Pointing within <see cref="StraightDownY"/> of straight down,
        /// back at the user (<see cref="AtBodyDot"/>), or a hand <see cref="RestDrop"/> below the eyes with its palm down
        /// (<see cref="PalmDownY"/>: typing) does not aim. The pose must hold <see cref="AimHold"/> s; it hides at once.
        /// </summary>
        public const float AimHold = 0.18f, StraightDownY = -0.85f, AtBodyDot = 0.3f, RestDrop = 0.3f, PalmDownY = -0.75f;

        /// <summary>
        /// Whether a hand's pose aims (pure, for tests); <paramref name="why"/> says why not. While a floor action is armed or
        /// active (<paramref name="floorAction"/>: placing, drawing a box, zone edit, a selected piece), a low hand pointing
        /// steeply down at the floor aims: only pointing at the body does not (log 2dc475d: aiming at the floor from the chair
        /// was gated as resting). Resting is a low, palm-down hand just above a desk or bed top (<paramref name="overRestSurface"/>).
        /// </summary>
        public static bool AimPose(Vector3 direction, Vector3 hand, Vector3 head, Vector3 palm, bool highConfidence, out string why,
            bool floorAction = false, bool overRestSurface = true)
        {
            Vector3 d = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.forward;
            Vector3 toHead = head - hand;
            // Low confidence alone does not hide the ray (a dim room is low all the time: hardening audit); a low hand with low
            // confidence (resting, typing, half occluded) does not aim.
            if (!highConfidence && head.y - hand.y > RestDrop) { why = "low confidence, hand low"; return false; }
            if (toHead.sqrMagnitude > 1e-6f && Vector3.Dot(d, toHead.normalized) > AtBodyDot) { why = "pointing at the body"; return false; }
            if (floorAction) { why = "aiming (floor action)"; return true; }
            if (d.y < StraightDownY) { why = "pointing straight down"; return false; }
            if (overRestSurface && head.y - hand.y > RestDrop && palm.sqrMagnitude > 1e-6f && palm.normalized.y < PalmDownY) { why = "hand at rest, palm down"; return false; }
            why = "aiming";
            return true;
        }

        /// <summary>True while a floor action is armed or active (MR sets it): a hand pointing at the floor then always aims.</summary>
        public System.Func<bool> FloorAction { get; set; }

        /// <summary>Whether a point is just above a real desk or bed top (MR sets it); null = anywhere low counts.</summary>
        public System.Func<Vector3, bool> RestSurface { get; set; }

        /// <summary>Whether this hand aims now (its ray is shown); a hand that does not must never be picked with.</summary>
        public bool HasAim(bool left)
        {
#if UNITY_EDITOR
            if (_mouseRayValid || _simAiming) return true;
#endif
            return left ? _leftRayValid : _rightRayValid;
        }

        /// <summary>The aim hold (pure): time aimed so far, 0 when not aiming; the ray shows once it reaches <see cref="AimHold"/>.</summary>
        public static float StepAim(float aimedFor, bool aiming, float dt) => aiming ? aimedFor + dt : 0f;

        // A hand whose ray is hidden aims at nothing: far picks never fire while the hands are working (typing).
        Ray NoAim => new Ray(View.origin + Vector3.up * 50f, Vector3.up);
        int _hoverFrame = -1;
        float _hoverMiss;
        Vector3 _hoverPoint;

#if UNITY_EDITOR
        // Editor-only fields live under the same #if as their readers, so players compile with no CS0414.
        [SerializeField, Tooltip("Editor only: thumb-index tip distance (m) that counts as a full pinch; 0 strength at 3x this.")]
        float _editorTouchDistance = 0.015f;

        [SerializeField, Tooltip("Editor only: the mouse aims through the Game view and its left button pinches the right hand. M toggles it. Off: use the simulator window's Point & click, which drives the hand rays.")]
        bool _editorMouse;

        // Distance along the mouse ray where the mouse pinch point sits (a hand's reach).
        const float MouseReach = 0.45f;
        Camera _camera;
        Ray _mouseRay;
        bool _mouseRayValid;

        [SerializeField, Tooltip("Editor only: the Meta XR Simulator's Point & click pinches both hands at once and does not aim the hand rays. " +
            "Then aim with the view centre (the reticle dot), also while no hand pinches.")]
        bool _editorSimAim = true;

        bool _simClick;           // a both-hands click is in progress
        bool _simAiming;          // this frame's Gaze/Aim use _simRay
        Ray _simRay;
#endif

        /// <summary>Tests: sets this frame's hands and (in the Editor) the aim ray, as the simulator click would.</summary>
        internal void SetForTest(HandState left, HandState right, Ray aim)
        {
            _left = left;
            _right = right;
#if UNITY_EDITOR
            _simAiming = true;
            _simRay = aim;
#endif
        }

        public HandState Left => _left;
        public HandState Right => _right;

        /// <summary>
        /// The far-targeting ray: the pointer ray of the hand that is pinching (right when both are,
        /// as in the simulator's Point & click), else the right hand's, else the left hand's, else the
        /// head ray. In Editor mouse mode, the mouse ray.
        /// </summary>
        public Ray Gaze
        {
            get
            {
#if UNITY_EDITOR
                if (_mouseRayValid) return _mouseRay;
                if (_simAiming) return _simRay;
#endif
                switch (AimSource(Busy(_right), Busy(_left), _rightRayValid, _leftRayValid))
                {
                    case AimFrom.Right: return _rightRay;
                    case AimFrom.Left: return _leftRay;
                    default: return AnyHandTracked ? NoAim : View; // hands at rest: no far target; no hands: the head
                }
            }
        }

        /// <summary>Whether something aims now (a shown hand ray, or the head with no hands): the reticle shows only then.</summary>
        public bool Pointing => GazeFromHand || !AnyHandTracked
#if UNITY_EDITOR
            || _mouseRayValid || _simAiming
#endif
            ;

        public enum AimFrom { Head, Left, Right }

        /// <summary>
        /// The simulator's click signature (pure, for tests): both hands start a pinch on the same
        /// frame. It stays on until neither hand pinches. Returns true on the frame it starts.
        /// </summary>
        public static bool StepSimClick(ref bool active, HandState right, HandState left)
        {
            if (right.PinchStarted && left.PinchStarted && !active) { active = true; return true; }
            if (active && !Busy(right) && !Busy(left)) active = false;
            return false;
        }

        static bool Busy(HandState h) => h.Pinching || h.PinchStarted || h.PinchEnded;

        /// <summary>
        /// Which ray aims (pure, for tests): the pinching hand's when it has one (right when both
        /// pinch), else right, else left, else the head.
        /// </summary>
        public static AimFrom AimSource(bool rightBusy, bool leftBusy, bool rightValid, bool leftValid)
        {
            if (leftBusy && !rightBusy && leftValid) return AimFrom.Left;
            if (rightValid) return AimFrom.Right;
            return leftValid ? AimFrom.Left : AimFrom.Head;
        }

        /// <summary>True when <see cref="Gaze"/> comes from a hand (show a ray line from it).</summary>
        /// <summary>
        /// The pinch point of the hand the gaze ray comes from (the midpoint of thumb and index tips): where the drawn ray
        /// starts. False when the ray is not a tracked hand's.
        /// </summary>
        public bool GazeHandPoint(out Vector3 point)
        {
            point = default;
            if (!GazeFromHand) return false;
            switch (AimSource(Busy(_right), Busy(_left), _rightRayValid, _leftRayValid))
            {
                case AimFrom.Right: point = _right.PinchPoint; return _right.Tracked;
                case AimFrom.Left: point = _left.PinchPoint; return _left.Tracked;
                default: return false;
            }
        }

        public bool GazeFromHand
        {
            get
            {
#if UNITY_EDITOR
                if (_mouseRayValid || _simAiming) return false; // the eye ray through the cursor: a dot, no line
#endif
                return _rightRayValid || _leftRayValid;
            }
        }

        /// <summary>One hand's pointer ray, for a drag that hand owns; the head ray when it has none.</summary>
        public Ray Aim(bool left)
        {
#if UNITY_EDITOR
            if (_mouseRayValid) return _mouseRay;
            if (_simAiming) return _simRay;
#endif
            if (left) return _leftRayValid ? _leftRay : _left.Tracked ? NoAim : View;
            return _rightRayValid ? _rightRay : _right.Tracked ? NoAim : View;
        }

        /// <summary>Ray from the head along the view direction, even when the gaze follows the Editor mouse. Use it to place UI.</summary>
        public Ray View => _head != null ? new Ray(_head.position, _head.forward) : new Ray(Vector3.zero, Vector3.forward);

        public Vector3 HeadPosition => _head != null ? _head.position : Vector3.zero;

        /// <summary>True once the headset reports a tracked head pose; zone proposals wait for it.</summary>
        public bool HeadTracked => _head != null && OVRManager.isHmdPresent &&
                                   OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head);

        /// <summary>True when either hand is tracked; false shows a "bring your hands forward" prompt.</summary>
        public bool AnyHandTracked => _left.Tracked || _right.Tracked;

        /// <summary>True while Touch controllers are the active input and no hand is tracked (the app is hands only).</summary>
        public bool ControllersInUse { get; private set; }

        /// <summary>Whether to ask the user to put the controllers down (pure, for tests).</summary>
        public static bool ControllersOnly(bool touchActive, bool anyHandTracked) => touchActive && !anyHandTracked;

        /// <summary>
        /// How far (m) <paramref name="center"/> is off the gaze ray when it is ahead and within
        /// <see cref="GazeHitRadius"/>; -1 otherwise. A hit is also reported as this frame's hover.
        /// Pickers take the target with the smallest result, so close neighbours never steal the aim.
        /// </summary>
        public float GazeHit(Vector3 center)
        {
            Ray gaze = Gaze;
            Vector3 oc = center - gaze.origin;
            float along = Vector3.Dot(oc, gaze.direction);
            if (along < 0f) return -1f;
            float miss = Mathf.Sqrt(Mathf.Max(0f, oc.sqrMagnitude - along * along));
            if (miss > GazeHitRadius) return -1f;
            // Shown just in front of the target so the reticle is not hidden inside it.
            ReportHover(gaze.GetPoint(Mathf.Max(0.05f, along - 0.07f)), miss);
            return miss;
        }

        /// <summary>
        /// The miss a box hit reports: the ray passes through the box, but every picker tries its
        /// point targets (tiles, buttons, knobs) before boxes, so a box ranks after any of them.
        /// </summary>
        public const float BoxHitMiss = GazeHitRadius;

        /// <summary>
        /// Reports a gaze hover at <paramref name="point"/> this frame, <paramref name="miss"/> metres
        /// off the ray (<see cref="BoxHitMiss"/> for box hits). The smallest miss wins, as in the pickers.
        /// </summary>
        public void ReportHover(Vector3 point, float miss)
        {
            if (_hoverFrame == Time.frameCount && miss >= _hoverMiss) return;
            _hoverFrame = Time.frameCount;
            _hoverMiss = miss;
            _hoverPoint = point;
        }

        /// <summary>True when something reported a gaze hover this frame (read it in LateUpdate).</summary>
        public bool TryGetHover(out Vector3 point)
        {
            point = _hoverPoint;
            return _hoverFrame == Time.frameCount;
        }

        int _claimedFrame = -1;

        /// <summary>
        /// Takes this frame's pinch for one consumer, so two modes never act on the same pinch.
        /// False if another consumer already took it. Consumers that run earlier (execution order) win.
        /// </summary>
        public bool Claim()
        {
            if (_claimedFrame == Time.frameCount) return false;
            _claimedFrame = Time.frameCount;
            return true;
        }

        public bool ClaimedThisFrame => _claimedFrame == Time.frameCount;

        /// <summary>Raised when the user recenters (holds the Meta button): seated UI goes back in front of them.</summary>
        public event System.Action Recentered;

        /// <summary>
        /// Raised <see cref="SettleFrames"/> frames after a recenter, once the head pose is the new one: seated UI is placed
        /// then (device, build 754b485: the panel re-seated in the recenter event itself, against the old head, and stayed low).
        /// </summary>
        public event System.Action RecenterSettled;
        public const int SettleFrames = 2;
        int _settleIn;

        /// <summary>The settle countdown (pure, for tests): true on the frame it reaches zero.</summary>
        public static bool StepSettle(ref int framesLeft)
        {
            if (framesLeft <= 0) return false;
            return --framesLeft == 0;
        }

        void Start()
        {
            Scan();
            if (OVRManager.display != null) OVRManager.display.RecenteredPose += OnRecentered;
        }

        void OnDestroy()
        {
            if (OVRManager.display != null) OVRManager.display.RecenteredPose -= OnRecentered;
        }

        void OnRecentered()
        {
            InputDebug.Log("recentered");
            _recenteredSinceLaunch = true;
            _settleIn = SettleFrames; // a burst (three at launch) settles once, after the last
            Recentered?.Invoke();
        }

        /// <summary>
        /// How many recenters have settled since launch. A listener that was disabled at the time (the panel, hidden while
        /// system UI had the input: the headset taken off and put back on) compares it to catch up.
        /// </summary>
        public int SettledRecenters { get; private set; }

        /// <summary>Tests: raises <see cref="Recentered"/> as a system recenter would.</summary>
        internal void RecenterForTest() => OnRecentered();

        /// <summary>Tests: the recenter has settled (the new head pose is in).</summary>
        internal void RecenterSettledForTest() { SettledRecenters++; RecenterSettled?.Invoke(); }

        // Allocates (Find*), so it runs once at start and then only while something is missing, throttled.
        void Scan()
        {
            OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) _head = rig.centerEyeAnchor;
            else if (Camera.main != null) _head = Camera.main.transform;

            foreach (OVRHand hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
            {
                bool isLeft = hand.GetHand() == OVRPlugin.Hand.HandLeft;
                if (isLeft) { _leftHand = hand; _leftSkeleton = hand.GetComponent<OVRSkeleton>(); }
                else { _rightHand = hand; _rightSkeleton = hand.GetComponent<OVRSkeleton>(); }
            }
            if (_leftHand == null && _rightHand == null) Debug.LogWarning($"{LogTag} No OVRHand found yet; retrying.");
        }

        void Update()
        {
            if (StepSettle(ref _settleIn)) { InputDebug.Log("recenter settled"); SettledRecenters++; RecenterSettled?.Invoke(); }
            ControllersInUse = ControllersOnly((OVRInput.GetActiveController() & OVRInput.Controller.Touch) != 0, AnyHandTracked); // last frame's hands; no allocation
            if ((_leftHand == null || _rightHand == null || _head == null) && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + _rescanInterval;
                Scan();
            }
            Read(_leftHand, _leftSkeleton, ref _left, ref _leftRay, ref _leftRayValid, ref _leftAimFor, ref _leftWhy, _leftSteady, "Left");
#if UNITY_EDITOR
            if (ReadMouse()) return;
#endif
            Read(_rightHand, _rightSkeleton, ref _right, ref _rightRay, ref _rightRayValid, ref _rightAimFor, ref _rightWhy, _rightSteady, "Right");
#if UNITY_EDITOR
            ReadSimAim();
#endif
        }

#if UNITY_EDITOR
        // Simulator Point & click: with Right input = Hand it pinches both hands at once but moves neither the hand
        // rays nor the controller with the cursor, and the app gets no cursor position from the simulator window.
        // So a both-hands click, its drag, and the idle reticle aim with the view centre (the reticle dot): right-drag
        // the view to put the dot on a target, then click. A single-hand pinch still aims with that hand's ray. The
        // Game-view mouse mode (M) is the precise alternative. Players: hand rays only.
        void ReadSimAim()
        {
            bool started = StepSimClick(ref _simClick, _right, _left);
            bool idle = !Busy(_right) && !Busy(_left);
            _simAiming = _editorSimAim && (_simClick || idle);
            if (!_simAiming) return;
            _simRay = View;
            if (started) InputDebug.Log($"sim click: aim = view center {_simRay.origin:F3} -> {_simRay.direction:F3}");
        }
#endif

#if UNITY_EDITOR
        public bool MouseMode => _editorMouse;

        // Mouse mode: true when the mouse drives the right hand this frame (button held or just
        // released, or no webcam right hand to fall back on).
        bool ReadMouse()
        {
            _mouseRayValid = false;
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            {
                _editorMouse = !_editorMouse;
                Debug.Log($"{LogTag} Mouse mode {(_editorMouse ? "on" : "off")}.");
            }
            Mouse mouse = Mouse.current;
            if (!_editorMouse || mouse == null || _head == null) return false;
            if (_camera == null || _camera.transform != _head) _camera = _head.GetComponent<Camera>();
            if (_camera == null) return false;
            _mouseRay = _camera.ScreenPointToRay(mouse.position.ReadValue(), Camera.MonoOrStereoscopicEye.Mono);
            _mouseRayValid = true;

            bool held = mouse.leftButton.isPressed;
            bool webcam = _rightHand != null && _rightHand.IsTracked && _rightHand.IsDataValid;
            if (!held && !mouse.leftButton.wasReleasedThisFrame && webcam) return false;
            // A fresh click is a fresh pinch, whatever the webcam hand did before.
            if (mouse.leftButton.wasPressedThisFrame) _right.TrackingLost = false;
            Step(ref _right, true, held ? 1f : 0f, _pinchOn, _pinchOff);
            _right.PinchPoint = _mouseRay.GetPoint(MouseReach);
            if (_right.PinchStarted) InputDebug.Log("pinch start (Mouse)");
            if (_right.PinchEnded) InputDebug.Log("pinch end (Mouse)");
            return true;
#else
            return false;
#endif
        }

        void OnGUI()
        {
            if (_editorMouse) GUI.Label(new Rect(10f, 10f, 360f, 22f), "Mouse mode (M toggles): aim with the cursor, click to pinch");
        }
#endif

        void OnApplicationFocus(bool focused) => OnApplicationPause(!focused);

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            // Treat pausing like losing both hands so drags cancel.
            Step(ref _left, false, 0f, _pinchOn, _pinchOff);
            Step(ref _right, false, 0f, _pinchOn, _pinchOff);
        }

        // ---- One ray from the hand, for drawing and picking alike (device, 2694d7e: the ray sat well below the hand, aligned
        // only sometimes, and a recenter did not help). Its origin is the pinch point between the thumb and index tips, from
        // the same skeleton that draws the hand; its direction is Meta's pointer pose, lightly smoothed. If the pointer pose
        // is far from the hand (another space, or stale), the direction comes from an estimated shoulder through the hand.

        /// <summary>A pointer pose farther than this (m) from the pinch point is not trusted for direction.</summary>
        public const float PointerTrust = 0.25f;
        /// <summary>The estimated shoulder: this far below the eyes and to the side (m).</summary>
        public const float ShoulderDrop = 0.15f, ShoulderSide = 0.17f;

        /// <summary>The estimated shoulder of the <paramref name="left"/> or right arm (pure).</summary>
        public static Vector3 Shoulder(Vector3 head, Vector3 headForward, bool left)
        {
            Vector3 f = headForward; f.y = 0f;
            f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return head + Vector3.down * ShoulderDrop + right * (left ? -ShoulderSide : ShoulderSide);
        }

        /// <summary>
        /// The aim ray from the hand (pure): it starts at <paramref name="origin"/> (the index knuckle: it does not move while
        /// the fingers pinch) and points along the pointer pose, or from the shoulder through the hand when the pointer pose
        /// is more than <see cref="PointerTrust"/> from the hand.
        /// </summary>
        public static Ray HandAim(Ray pointer, Vector3 origin, Vector3 shoulder, out bool trusted)
        {
            trusted = (pointer.origin - origin).sqrMagnitude <= PointerTrust * PointerTrust && pointer.direction.sqrMagnitude > 1e-6f;
            Vector3 dir = trusted ? pointer.direction : origin - shoulder;
            if (dir.sqrMagnitude < 1e-6f) dir = pointer.direction.sqrMagnitude > 1e-6f ? pointer.direction : Vector3.forward;
            return new Ray(origin, dir.normalized);
        }

        void Read(OVRHand hand, OVRSkeleton skeleton, ref HandState state, ref Ray ray, ref bool rayValid, ref float aimedFor, ref string why, AimSteady steady, string side)
        {
            bool tracked = hand != null && hand.IsTracked && hand.IsDataValid && !hand.IsSystemGestureInProgress;
            float strength = tracked ? hand.GetFingerPinchStrength(OVRHand.HandFinger.Index) : 0f;
            float tipGap = float.MaxValue;
            Vector3 point = tracked ? PinchPoint(hand, skeleton, out tipGap) : state.PinchPoint;
            bool pointer = tracked && hand.IsPointerPoseValid && hand.PointerPose != null;
            Vector3 rawOrigin = default;
            bool trusted = true;
            if (pointer)
            {
                var raw = new Ray(hand.PointerPose.position, hand.PointerPose.forward);
                rawOrigin = raw.origin;
                Vector3 head = _head != null ? _head.position : point + Vector3.up * 0.5f;
                // From the index knuckle, not the pinch point (it moves as the thumb and index close: the ray swung on
                // every pinch); the direction one-euro filtered (steady when still, quick when moving).
                Ray aim = HandAim(raw, Knuckle(skeleton, point), Shoulder(head, _head != null ? _head.forward : Vector3.forward, side == "Left"), out trusted);
                ray = steady.Step(aim, Time.time, Time.deltaTime);
            }
            else steady.Reset();
            bool wasShown = rayValid;
            string now = !tracked ? (hand != null && hand.IsSystemGestureInProgress ? "system gesture" : "not tracked") : !pointer ? "no pointer pose" : "";
            bool aiming = pointer && AimPose(ray.direction, point, _head != null ? _head.position : point + Vector3.up * 0.5f,
                Palm(skeleton, side == "Left"), hand.HandConfidence == OVRHand.TrackingConfidence.High, out now,
                FloorAction != null && FloorAction(), RestSurface == null || RestSurface(point));
            // A drag that started with the ray keeps it to the end (pointing down at a floor corner mid-drag).
            if (pointer && wasShown && (state.Pinching || state.PinchStarted)) aiming = true;
            aimedFor = StepAim(aimedFor, aiming, Time.deltaTime);
            rayValid = aiming && (aimedFor >= AimHold || wasShown);
            if (rayValid != wasShown) InputDebug.Log($"ray: {(rayValid ? "shown" : "hidden")} ({side}, {now})");
            why = now;
#if UNITY_EDITOR
            if (tracked)
            {
                if (hand.GetFingerIsPinching(OVRHand.HandFinger.Index)) strength = 1f;
                strength = Mathf.Max(strength, TouchStrength(tipGap, _editorTouchDistance));
            }
#endif
            Step(ref state, tracked, strength, _pinchOn, _pinchOff);
            state.LowConfidence = tracked && hand.HandConfidence != OVRHand.TrackingConfidence.High;
            if (tracked) state.PinchPoint = point;
            if (state.PinchStarted) InputDebug.Log($"pinch start ({side}) strength={strength:0.00} tipGap={tipGap:0.000} handRay={rayValid}");
            if (state.PinchStarted && pointer)
            {
                // The press goes where the hand pointed just before the fingers started to close.
                Ray latched = steady.Latched(Time.time, ray);
                LogLatch(side, Vector3.Angle(latched.direction, ray.direction));
                ray = latched;
                LogAimGap(side, ray.origin, rawOrigin, state.PinchPoint, trusted);
            }
            if (state.PinchEnded) InputDebug.Log($"pinch end ({side})");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void LogLatch(string side, float degrees) =>
            InputDebug.Log($"aim latch ({side}): the press uses the aim from {AimSteady.LatchSeconds * 1000f:0} ms before, {degrees:F1} deg from the current aim");

        // On each pinch (dev builds): the ray's origin against the pinch point (target under 2 cm), and how far Meta's raw
        // pointer pose was from the hand, so an offset like 2694d7e's shows in logcat.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        void LogAimGap(string side, Vector3 origin, Vector3 rawOrigin, Vector3 pinch, bool trusted) =>
            InputDebug.Log($"aim gap ({side}): ray origin (index knuckle) to pinch point {Vector3.Distance(origin, pinch) * 100f:F1} cm, " +
                           $"Meta pointer pose to pinch point {Vector3.Distance(rawOrigin, pinch) * 100f:F1} cm " +
                           $"({(trusted ? "its direction used" : "too far: shoulder-to-hand direction")}), recentered since launch: {_recenteredSinceLaunch}");

        /// <summary>1 when the tips touch (gap at most <paramref name="touch"/>), 0 at 3x that, linear between.</summary>
        public static float TouchStrength(float gap, float touch) =>
            touch <= 0f ? 0f : Mathf.Clamp01(1f - (gap - touch) / (2f * touch));

        /// <summary>
        /// One frame of the pinch state machine (pure, for tests). Losing tracking latches
        /// <see cref="HandState.TrackingLost"/> until the hand is back and the fingers open.
        /// </summary>
        public static void Step(ref HandState state, bool tracked, float strength, float pinchOn, float pinchOff)
        {
            state.PinchStarted = false;
            state.PinchEnded = false;
            if (!tracked)
            {
                state.TrackingLost |= state.Tracked || state.Pinching;
                state.Tracked = false;
                state.Pinching = false;
                return;
            }

            state.Tracked = true;
            if (state.TrackingLost)
            {
                // A pinch held through the loss never resumes a drag.
                if (strength < pinchOff) state.TrackingLost = false;
                return;
            }

            bool pinching = state.Pinching ? strength > pinchOff : strength > pinchOn;
            state.PinchStarted = pinching && !state.Pinching;
            state.PinchEnded = !pinching && state.Pinching;
            state.Pinching = pinching;
        }

        // The palm's normal (out of the palm) from the wrist and the index and little-finger knuckles; zero when unknown.
        static Vector3 Palm(OVRSkeleton skeleton, bool left)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return Vector3.zero;
            bool openXr = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft || skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId wristId = openXr ? OVRSkeleton.BoneId.XRHand_Wrist : OVRSkeleton.BoneId.Hand_WristRoot;
            OVRSkeleton.BoneId indexId = openXr ? OVRSkeleton.BoneId.XRHand_IndexProximal : OVRSkeleton.BoneId.Hand_Index1;
            OVRSkeleton.BoneId littleId = openXr ? OVRSkeleton.BoneId.XRHand_LittleProximal : OVRSkeleton.BoneId.Hand_Pinky1;
            Transform w = null, i = null, l = null;
            var bones = skeleton.Bones;
            for (int b = 0; b < bones.Count; b++)
            {
                if (bones[b].Id == wristId) w = bones[b].Transform;
                else if (bones[b].Id == indexId) i = bones[b].Transform;
                else if (bones[b].Id == littleId) l = bones[b].Transform;
            }
            return w == null || i == null || l == null ? Vector3.zero : PalmNormal(w.position, i.position, l.position, left);
        }

        /// <summary>The palm's normal from the wrist and two knuckles (pure): toward the side the palm faces.</summary>
        public static Vector3 PalmNormal(Vector3 wrist, Vector3 indexKnuckle, Vector3 littleKnuckle, bool left)
        {
            Vector3 n = Vector3.Cross(indexKnuckle - wrist, littleKnuckle - wrist);
            return (left ? n : -n).normalized;
        }

        // The index knuckle (the proximal joint): steady while the fingers pinch. The pinch point without a skeleton.
        static Vector3 Knuckle(OVRSkeleton skeleton, Vector3 fallback)
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return fallback;
            bool openXr = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft || skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId id = openXr ? OVRSkeleton.BoneId.XRHand_IndexProximal : OVRSkeleton.BoneId.Hand_Index1;
            var bones = skeleton.Bones;
            for (int b = 0; b < bones.Count; b++) if (bones[b].Id == id) return bones[b].Transform.position;
            return fallback;
        }

        static Vector3 PinchPoint(OVRHand hand, OVRSkeleton skeleton, out float tipGap)
        {
            tipGap = float.MaxValue;
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null) return hand.transform.position;
            bool openXr = skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandLeft ||
                          skeleton.GetSkeletonType() == OVRSkeleton.SkeletonType.XRHandRight;
            OVRSkeleton.BoneId thumb = openXr ? OVRSkeleton.BoneId.XRHand_ThumbTip : OVRSkeleton.BoneId.Hand_ThumbTip;
            OVRSkeleton.BoneId index = openXr ? OVRSkeleton.BoneId.XRHand_IndexTip : OVRSkeleton.BoneId.Hand_IndexTip;

            Transform t = null, i = null;
            var bones = skeleton.Bones;
            for (int b = 0; b < bones.Count; b++)
            {
                if (bones[b].Id == thumb) t = bones[b].Transform;
                else if (bones[b].Id == index) i = bones[b].Transform;
            }
            if (t == null || i == null) return hand.transform.position;
            tipGap = Vector3.Distance(t.position, i.position);
            return (t.position + i.position) * 0.5f;
        }
    }
}

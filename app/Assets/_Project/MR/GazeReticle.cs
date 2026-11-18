using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// A small dot where the gaze lands: on the hovered pinch target when there is one (bigger),
    /// else on the room's floor, walls or ceiling, else 1.5 m ahead. Runs after every pinch
    /// consumer has reported this frame's hover. When the ray comes from a hand, a thin line shows that
    /// same ray (its origin and direction), tapering toward its end. Shown on device and in the Editor; no allocation.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class GazeReticle : MonoBehaviour
    {
        [SerializeField] HandRig _hands;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] Material _material;
        [SerializeField, Tooltip("Dot diameter per metre of distance (keeps it the same size in view).")] float _size = 0.012f;
        [SerializeField, Tooltip("Size multiplier while a pinch target is hovered.")] float _hoverScale = 2f;
        [SerializeField, Tooltip("Distance (m) when the gaze hits nothing.")] float _fallback = 1.5f;

        const int OnTopQueue = 3200; // after the panel (Transparent+100): the cursor is always the top layer
        static readonly int RectId = Shader.PropertyToID("_Rect"), RimId = Shader.PropertyToID("_RimColor");
        bool _disc; // the dot is a camera-facing PanelGlass disc (ZTest Always), else the old sphere
        MaterialPropertyBlock _discBlock;
        const float SizeBoost = 1.3f; // a bit larger than before, to read over the glass

        Renderer _dot;
        Material _onTop;

        void OnDestroy() { if (_onTop != null) Destroy(_onTop); }
        LineRenderer _line;

        // ---- The hand ray, as the human wants it ("back to how it was at the very first", before a6cdf5b / #259). ----
        // DO NOT CHANGE without the human asking: RayLineTests locks the end points and the width.
        // A thin line on the hand's pointer ray (the picking ray): from 5 cm along it to 80% of the way to the target, at
        // least 6 cm and at most 1 m, so it never hides what it points at; 4 mm wide, tapering to nothing toward the dot.

        /// <summary>The original ray: where it starts along the pointer ray, its shortest and longest end, its share of the distance, its width.</summary>
        public const float LineStart = 0.05f, LineMin = 0.06f, LineMax = 1f, LineShare = 0.8f, LineWidth = 0.004f;

        /// <summary>The line's two ends on <paramref name="pointer"/> for a target <paramref name="distance"/> m away (pure, for tests).</summary>
        public static void Line(Ray pointer, float distance, out Vector3 start, out Vector3 end)
        {
            start = pointer.GetPoint(LineStart);
            end = pointer.GetPoint(Mathf.Clamp(distance * LineShare, LineMin, LineMax));
        }

        /// <summary>The width along the line (pure): full at the hand, nothing at its end.</summary>
        public static AnimationCurve LineTaper() => new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        /// <summary>
        /// Whether the ray is drawn (pure, for tests): the aim comes from a tracked hand's pointer pose, and that hand is
        /// not poking the panel directly.
        /// </summary>
        public static bool RayShown(bool fromHand, bool handTracked, bool poking) => fromHand && handTracked && !poking;

        void Start()
        {
            // A flat disc on the panel's own shader (ZTest Always), drawn after the panel: never hidden by the room,
            // furniture or the panel itself. Falls back to the old sphere if the shader is missing.
            Shader glass = Shader.Find("OwnSpace/PanelGlass");
            _disc = glass != null;
            _dot = XrVisuals.Make(_disc ? PrimitiveType.Quad : PrimitiveType.Sphere, transform, _material, Color.white, "Reticle");
            _onTop = _disc ? new Material(glass) { name = "ReticleOnTop", renderQueue = OnTopQueue }
                           : new Material(_material) { name = "ReticleOnTop", renderQueue = OnTopQueue };
            _dot.sharedMaterial = _onTop;
            var go = new GameObject("HandRay");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.sharedMaterial = _material; // the original material (the human liked the first look)
            _line.positionCount = 2;
            _line.widthMultiplier = LineWidth;
            _line.widthCurve = LineTaper(); // fades out toward the dot
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;
            _controllerNote = new WorldLabel(transform, "ControllerNote", Color.white);
            _panel = FindFirstObjectByType<OwnSpace.UI.ToolPanel>(FindObjectsInactive.Include); // once, not per frame
        }

        OwnSpace.UI.ToolPanel _panel;

        /// <summary>Shown in front of the user while controllers are in use: the app works with hands only.</summary>
        public const string ControllerNote = "This app uses your hands.\nPut the controllers down.";
        WorldLabel _controllerNote;

        void LateUpdate()
        {
            if (_hands.ControllersInUse)
            {
                Ray view = _hands.View;
                _controllerNote.Show(ControllerNote, view.origin + view.direction * 0.6f + Vector3.down * 0.05f, view.origin);
            }
            else if (_controllerNote.Shown) _controllerNote.Hide();
            // Nothing aims (hands at rest, typing): no dot and no ray (the human: "the ray shows up weird again").
            // Focus: no ray or dot, except while the exit chip shows (so it can be aimed at).
            bool pointing = _hands.Pointing && (!FocusMode.Active || FocusMode.ChipShown);
            if (_dot.enabled != pointing) _dot.enabled = pointing;
            if (!pointing) { if (_line.enabled) _line.enabled = false; return; }
            Ray gaze = _hands.Gaze;
            bool hover = _hands.TryGetHover(out Vector3 point);
            if (!hover)
            {
                float d = _fallback;
                if (_room != null && _room.HasScan &&
                    RoomGeometry.RayRoom(gaze, _room.FloorOutline, _room.FloorY, _room.CeilingY, out float hit))
                {
                    d = hit - 0.01f; // just in front of the surface
                }
                point = gaze.GetPoint(d);
            }
            float distance = Vector3.Distance(gaze.origin, point);
            float size = _size * SizeBoost * Mathf.Max(0.3f, distance) * (hover ? _hoverScale : 1f);
            _dot.transform.position = point;
            _dot.transform.localScale = Vector3.one * size;
            if (_disc)
            {
                Vector3 away = point - gaze.origin;
                if (away.sqrMagnitude > 1e-6f) _dot.transform.rotation = Quaternion.LookRotation(away, Vector3.up); // faces the eye
                if (_discBlock == null) _discBlock = new MaterialPropertyBlock();
                _dot.GetPropertyBlock(_discBlock);
                _discBlock.SetVector(RectId, new Vector4(size, size, size * 0.5f, 0f));
                _discBlock.SetColor(RimId, Color.clear);
                _dot.SetPropertyBlock(_discBlock);
            }
            XrVisuals.Tint(_dot, hover ? XrVisuals.Hover : Color.white);

            // The drawn ray is the picking ray itself: the same origin and direction as hover and pinch use.
            bool tracked = _hands.GazeHandPoint(out Vector3 hand);
            bool shown = RayShown(_hands.GazeFromHand, tracked, tracked && _panel != null && _panel.Poking(hand));
            if (_line.enabled != shown) _line.enabled = shown;
            if (!shown) return;
            Line(gaze, Vector3.Distance(gaze.origin, point), out Vector3 from, out Vector3 end);
            _line.SetPosition(0, from);
            _line.SetPosition(1, end);
            if (_hands.Right.PinchStarted || _hands.Left.PinchStarted) LogRay(from, end, _hands.Aim(!_hands.Right.PinchStarted));
        }

        // On a pinch only (dev builds): the drawn ray next to the pinching hand's picking ray, to compare in logcat.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        static void LogRay(Vector3 origin, Vector3 end, Ray pick) =>
            InputDebug.Log($"ray: drawn origin={origin:F3} dir={(end - origin).normalized:F3} length={Vector3.Distance(origin, end):F2} | picking origin={pick.origin:F3} dir={pick.direction:F3}");
    }
}

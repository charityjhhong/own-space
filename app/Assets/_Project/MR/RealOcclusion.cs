using System.Collections.Generic;
using Meta.XR.EnvironmentDepth;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OwnSpace.MR
{
    /// <summary>
    /// Real things in front of virtual ones (device batch 10). It turns on Meta's Environment Depth, with hands kept
    /// and no Meta shader keywords. It also enables the "RealOcclusion" Render Objects pass (<c>OwnSpace/RealOcclusion</c>),
    /// which lets passthrough show wherever a kept-real surface is nearer than the virtual one.
    /// <list type="bullet">
    /// <item>"Kept real" is my zone up to <see cref="ZoneTop"/> above the floor, plus a <see cref="KeptBand"/> above every
    /// real piece that is kept outside it. These are drawn into a small top-down RG mask when the room, zone or design
    /// changes, never per frame.</item>
    /// <item>Without depth (Editor, no permission, unsupported) the pass stays off, so it costs nothing.</item>
    /// </list>
    /// Every state change is logged.
    /// </summary>
    public sealed class RealOcclusion : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        public const string FeatureName = "RealOcclusion";
        public const int MaskSize = 128;
        public const float ZoneTop = 2.0f, KeptBand = 0.4f, KeptScale = 3f, Softness = 0.03f, Margin = 0.25f;
        /// <summary>
        /// Over a REDESIGNED desk or bed (device feedback on 1b913d6: "the things on my desk disappear"), only a band above
        /// its scanned top shows real: from <see cref="TopBias"/> above it (so the real top plane never punches through the
        /// new top) up to <see cref="DeskBand"/> (desk) or <see cref="BedBand"/> (bed, bedding, a person lying there), over
        /// its footprint grown by <see cref="FootprintPad"/>. Its real body below stays hidden by the design.
        /// </summary>
        public const float DeskBand = 0.45f, BedBand = 0.6f, TopBias = 0.02f, FootprintPad = 0.05f;

        [SerializeField] DesignStore _store;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField, Tooltip("URP renderers that carry the RealOcclusion pass (it is switched on only with depth).")]
        ScriptableRendererData[] _renderers;

        static readonly int MaskId = Shader.PropertyToID("_OwnSpaceRealMask"), RectId = Shader.PropertyToID("_OwnSpaceRealRect"),
            HeightsId = Shader.PropertyToID("_OwnSpaceRealHeights");

        EnvironmentDepthManager _depth;
        Texture2D _mask;
        Color32[] _pixels;
        readonly Vec2[] _corners = new Vec2[4];
        readonly List<Vec2[]> _kept = new List<Vec2[]>();
        readonly List<float> _keptTop = new List<float>(), _keptBottom = new List<float>();
        readonly List<bool> _keptBand = new List<bool>();
        DesignSession _session;
        bool _dirty = true, _on, _depthWas, _logged;

        /// <summary>Whether Environment Depth works here (the manager was created); false = unsupported so far.</summary>
        public bool Supported => _depth != null;

        public enum DepthState { Starting, Unsupported, Waiting, Running, Off }

        /// <summary>
        /// One state for the log and the Settings label (device shot 120543 said "Not available" while the mask log looked
        /// healthy; the mask is built either way). Starting = still asking for support; Unsupported = gave up; Waiting =
        /// requested, no frames yet; Running = depth frames arrive; Off = the switch is off, depth is not asked for.
        /// </summary>
        public DepthState State => !_userOn ? DepthState.Off
            : _depth == null ? (_since < GiveUpSeconds ? DepthState.Starting : DepthState.Unsupported)
            : _depth.IsDepthAvailable ? DepthState.Running : DepthState.Waiting;

        /// <summary>Asks again from the start (after a visit or leaving one, and when the app resumes).</summary>
        public void Reacquire(string why)
        {
            _logged = false; // the next frame logs the state
            if (_depth != null || !_userOn) { Debug.Log($"{LogTag} Real occlusion ({why}): depth {State}."); return; }
            _since = 0f;
            _retryAt = 0f;
            Debug.Log($"{LogTag} Real occlusion ({why}): asking for Environment Depth again.");
        }

        void OnApplicationFocus(bool focused) { if (focused && _mask != null) Reacquire("focus"); }
        void OnApplicationPause(bool paused) { if (!paused && _mask != null) Reacquire("resume"); }
        void OnRoomChanged() { _dirty = true; if (_mask != null) Reacquire("room changed"); }

        /// <summary>Whether the occlusion pass is running (depth available and the pass found).</summary>
        public bool Active => _on;

        /// <summary>Raised when <see cref="Active"/> changes (a state change, never per frame).</summary>
        public event System.Action ActiveChanged;

        /// <summary>
        /// Whether real occlusion may run (the human: "Real occlusion off by default"; depth edges crackled in the
        /// middle of the view). Never in a release build: no pass, no depth, no Settings row; the flat real top shows the
        /// real desk instead. In a development build it is an experiment switch, saved, default off.
        /// </summary>
        public static bool EnabledFor(bool developmentBuild, bool savedSwitch) => developmentBuild && savedSwitch;

        /// <summary>
        /// Whether Meta's depth manager runs (pure): only while the switch is on. It asks the runtime for a depth image every
        /// frame while enabled, whatever our pass does; with the switch off it kept doing so and hit
        /// "xrAcquireEnvironmentDepthImageMETA failed with result XR_ERROR_LIMIT_REACHED" (device, build d3a6dcc).
        /// Disabled, it stops Unity's occlusion subsystem and frees its depth textures.
        /// </summary>
        public static bool RunsDepth(bool switchOn) => switchOn;

        bool _userOn; // off until a development build's saved switch says otherwise
        MoodApplier _moodApplier;

        /// <summary>
        /// The switch: Settings › "Real occlusion", in development builds only (an experiment). Release builds have no row
        /// and pass false (<see cref="EnabledFor"/>). Default off. Off turns the pass off on the next frame and stops depth;
        /// on starts asking for depth (from scratch if it was never started).
        /// </summary>
        public bool UserEnabled
        {
            get => _userOn;
            set
            {
                if (_userOn == value) return;
                _userOn = value;
                _logged = false;
                if (_depth != null) _depth.enabled = RunsDepth(value); // off: no depth images are asked for at all
                else if (value) { _since = 0f; _retryAt = 0f; } // never started: start asking now
            }
        }

        void Start()
        {
            _mask = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "RealMask" };
            _pixels = new Color32[MaskSize * MaskSize];
            Shader.SetGlobalTexture(MaskId, _mask);
            if (_room != null) _room.Changed += OnRoomChanged; // Visit and Leave raise it too
            SetPass(false);
            if (_userOn) TryStartDepth(); // off (every release build): depth is never started
            // Development builds: what the runtime actually enabled (device check of PR #275: the room load came back empty
            // with Unity's OpenXR Meta package installed). Once, at start.
            if (Debug.isDebugBuild && !Application.isEditor)
                Debug.Log($"{LogTag} OpenXR {UnityEngine.XR.OpenXR.OpenXRRuntime.name} {UnityEngine.XR.OpenXR.OpenXRRuntime.version}, " +
                          $"enabled extensions: {string.Join(" ", UnityEngine.XR.OpenXR.OpenXRRuntime.GetEnabledExtensions())}");
        }

        public const float RetrySeconds = 2f, GiveUpSeconds = 60f;
        float _retryAt, _since;

        // Device bug on a2c27d2: "not supported here" at start, and it never asked again. Support can come later (the
        // scene permission is granted after start), so it retries every 2 s for a minute and logs why each time it fails.
        void TryStartDepth()
        {
            if (_depth != null) return;
            if (!EnvironmentDepthManager.IsSupported)
            {
                Debug.Log($"{LogTag} Real occlusion: Environment Depth not supported yet ({Why()}); trying again in {RetrySeconds:0} s.");
                _retryAt = _since + RetrySeconds;
                return;
            }
            _depth = gameObject.AddComponent<EnvironmentDepthManager>();
            _depth.OcclusionShadersMode = OcclusionShadersMode.None; // our own pass reads the depth; no keywords on content's shaders
            _depth.RemoveHands = false; // hands are real things in front of the room
            _depth.enabled = RunsDepth(_userOn); // the switch may have been turned off before depth was supported
            Debug.Log($"{LogTag} Real occlusion: Environment Depth requested ({Why()}).");
        }

        /// <summary>
        /// The OpenXR extension behind Environment Depth. Unity's "Meta Quest: Occlusion" feature (com.unity.xr.meta-openxr)
        /// requests it and gives Meta's EnvironmentDepthManager the occlusion subsystem it reads; without that package
        /// <c>IsSupported</c> is always false on OpenXR. Turned on by <c>OwnSpace.MR.Editor.DepthOcclusionSetup.Apply</c>.
        /// </summary>
        public const string DepthExtensionName = "XR_META_environment_depth";

        /// <summary>What depth needs, as one log line: the OpenXR extension, the scene permission and passthrough.</summary>
        static string Why()
        {
            bool extension = UnityEngine.XR.OpenXR.OpenXRRuntime.IsExtensionEnabled(DepthExtensionName);
            bool permission = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            permission = UnityEngine.Android.Permission.HasUserAuthorizedPermission("com.oculus.permission.USE_SCENE");
#endif
            OVRManager manager = OVRManager.instance;
            bool passthrough = manager != null && manager.isInsightPassthroughEnabled;
            return $"extension {DepthExtensionName} {(extension ? "on" : "OFF")}, scene permission {(permission ? "granted" : "NOT granted")}, " +
                   $"passthrough {(passthrough ? "on" : "off")}, runtime {UnityEngine.XR.OpenXR.OpenXRRuntime.name}";
        }

        void OnDestroy()
        {
            if (_room != null) _room.Changed -= OnRoomChanged;
            if (_session != null) _session.Changed -= MarkDirty;
            SetPass(false);
            if (_mask != null) Destroy(_mask);
        }

        void MarkDirty() => _dirty = true;

        void Update()
        {
            if (_userOn) _since += Time.unscaledDeltaTime;
            if (_userOn && _depth == null && _since >= _retryAt && _since < GiveUpSeconds) TryStartDepth();
            DesignSession session = _store != null ? _store.Session : null;
            if (session != _session)
            {
                if (_session != null) _session.Changed -= MarkDirty;
                _session = session;
                if (_session != null) _session.Changed += MarkDirty;
                _dirty = true;
            }
            if (_dirty && _room != null && _session != null) { _dirty = false; Rebuild(); }

            bool depth = _depth != null && _depth.IsDepthAvailable;
            if (depth != _depthWas || !_logged)
            {
                _logged = true;
                _depthWas = depth;
                bool wasOn = _on;
                SetPass(depth && _userOn);
                if (_on != wasOn) ActiveChanged?.Invoke();
                if (_on)
                {
                    // The passthrough layer's state right when the pass starts (device shot 115520: holes showed black).
                    if (_moodApplier == null) _moodApplier = FindAnyObjectByType<MoodApplier>(); // on a state change only
                    if (_moodApplier != null) _moodApplier.EnsurePassthrough("occlusion on");
                }
                Debug.Log($"{LogTag} Real occlusion: depth {(_depth != null && !_depth.enabled ? "stopped" : State.ToString())}, switch {(_userOn ? "on" : "off")}, " +
                          $"pass {(_on ? "on" : depth && _userOn ? "MISSING from the renderer" : "off")}.");
            }
        }

        void SetPass(bool on)
        {
            _on = false;
            if (_renderers == null) return;
            foreach (ScriptableRendererData data in _renderers)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature != null && feature.name == FeatureName) { feature.SetActive(on); _on |= on; }
            }
        }

        // The top-down mask over the room: R = my zone, G = a kept-real piece's top + band. On a change only.
        void Rebuild()
        {
            IReadOnlyList<Vec2> zone = _session.State.zoneOutline;
            IReadOnlyList<Vec2> floor = _room.RealOutline; // the real room: an extended outline would spread the mask thin
            IReadOnlyList<Vec2> bounds = floor != null && floor.Count >= 3 ? floor : zone;
            if (bounds == null || bounds.Count < 3) { Shader.SetGlobalVector(HeightsId, new Vector4(_room.FloorY, -1f, KeptScale, Softness)); return; }

            _kept.Clear();
            _keptTop.Clear();
            _keptBottom.Clear();
            _keptBand.Clear();
            int bands = 0;
            IReadOnlyList<FurnitureAnchor> furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                FurnitureAnchor a = furniture[i];
                AnchorMode mode = _session.State.ChoiceFor(a.Id).mode;
                float top = a.Box.center.y + a.Box.size.y * 0.5f - _room.FloorY;
                bool redesigned = mode == AnchorMode.Redesign && (a.Role == FurnitureRole.Table || a.Role == FurnitureRole.Bed);
                if (mode != AnchorMode.KeepReal && !redesigned) continue;
                // A visited space's furniture is all virtual (shown with designs); only my zone's real pieces count.
                if (_room.Visiting != null && !Zones.Contains(zone, a)) continue;
                var quad = new Vec2[4];
                float pad = redesigned ? 2f * FootprintPad : 0f;
                FloorGeometry.Footprint(new Vec2(a.Box.center.x, a.Box.center.z), a.Box.yawDegrees, a.Box.size.x + pad, a.Box.size.z + pad, quad);
                _kept.Add(quad);
                if (redesigned)
                {
                    bands++;
                    _keptBottom.Add(top + TopBias);
                    _keptTop.Add(top + (a.Role == FurnitureRole.Bed ? BedBand : DeskBand));
                    _keptBand.Add(true);
                }
                else
                {
                    _keptBottom.Add(0f);
                    _keptTop.Add(top + KeptBand);
                    _keptBand.Add(false);
                }
            }

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < bounds.Count; i++)
            {
                minX = Mathf.Min(minX, bounds[i].x); maxX = Mathf.Max(maxX, bounds[i].x);
                minZ = Mathf.Min(minZ, bounds[i].y); maxZ = Mathf.Max(maxZ, bounds[i].y);
            }
            minX -= Margin; minZ -= Margin; maxX += Margin; maxZ += Margin;
            int zoneTexels = Fill(_pixels, MaskSize, minX, minZ, maxX, maxZ, zone, _kept, _keptTop, _keptBottom, _keptBand);
            _mask.SetPixels32(_pixels);
            _mask.Apply(false);
            Shader.SetGlobalVector(RectId, new Vector4(minX, minZ, 1f / (maxX - minX), 1f / (maxZ - minZ)));
            Shader.SetGlobalVector(HeightsId, new Vector4(_room.FloorY, ZoneTop, KeptScale, Softness));
            Debug.Log($"{LogTag} Real occlusion: mask rebuilt ({zoneTexels} zone texels, {_kept.Count - bands} kept-real pieces, {bands} redesigned desk/bed bands; the mask is built with or without depth, depth {State}).");
        }

        /// <summary>
        /// Fills the mask (row = z, column = x, texel centres; the border texels stay empty so outside reads as nothing).
        /// R = 255 inside the zone. G and B = the top and bottom of the real band over the texel (fractions of
        /// <see cref="KeptScale"/>; a kept-real piece is real from the floor to its top + band). A = 255 over a redesigned
        /// desk or bed: there only its band is real, even inside the zone. Returns the zone texel count.
        /// </summary>
        public static int Fill(Color32[] pixels, int size, float minX, float minZ, float maxX, float maxZ,
            IReadOnlyList<Vec2> zone, List<Vec2[]> kept, List<float> keptTop) =>
            Fill(pixels, size, minX, minZ, maxX, maxZ, zone, kept, keptTop, null, null);

        public static int Fill(Color32[] pixels, int size, float minX, float minZ, float maxX, float maxZ,
            IReadOnlyList<Vec2> zone, List<Vec2[]> kept, List<float> keptTop, List<float> keptBottom, List<bool> band)
        {
            int count = 0;
            bool hasZone = zone != null && zone.Count >= 3;
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                byte r = 0, g = 0, b = 0, a = 0;
                if (x > 0 && z > 0 && x < size - 1 && z < size - 1)
                {
                    var p = new Vec2(minX + (x + 0.5f) / size * (maxX - minX), minZ + (z + 0.5f) / size * (maxZ - minZ));
                    if (hasZone && Zones.Contains(zone, p)) { r = 255; count++; }
                    float top = 0f, bottom = 0f;
                    bool isBand = false;
                    for (int k = 0; k < kept.Count; k++)
                    {
                        if (keptTop[k] <= top || !Zones.Contains(kept[k], p)) continue;
                        top = keptTop[k];
                        bottom = keptBottom != null ? keptBottom[k] : 0f;
                        isBand = band != null && band[k];
                    }
                    g = (byte)Mathf.Clamp(Mathf.RoundToInt(top / KeptScale * 255f), 0, 255);
                    b = (byte)Mathf.Clamp(Mathf.RoundToInt(bottom / KeptScale * 255f), 0, 255);
                    a = isBand ? (byte)255 : (byte)0;
                }
                pixels[z * size + x] = new Color32(r, g, b, a);
            }
            return count;
        }
    }
}

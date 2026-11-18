using OwnSpace.Content;
using OwnSpace.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.MR
{
    /// <summary>
    /// Applies the design's mood preset (CONCEPT 5.6, content's <see cref="MoodPresets"/>) to the sun
    /// (the scene's directional light: color temperature, intensity, elevation), the ambient light,
    /// and passthrough (brightness/contrast/saturation and an RGB tint). Changes blend over the
    /// preset's blendSeconds (at least <see cref="MinBlendSeconds"/>) so nothing ever flashes.
    /// "" = the neutral look. Lights are found once at start; per frame only while blending.
    /// No allocation during a blend: passthrough brightness, contrast and tint go through the layer's
    /// color scale and offset (plain fields); saturation, which needs the SDK's color map (it
    /// allocates), is set once when the blend ends. Pausing, losing focus or losing the room scan
    /// resets passthrough and lights to neutral; on return the mood blends back in.
    /// </summary>
    public sealed class MoodApplier : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";
        public const float MinBlendSeconds = 1.5f;
        const float BrightnessToOffset = 0.25f; // gentle: preset brightness -1..1 -> color offset -0.25..0.25

        struct Look
        {
            public float Kelvin, Intensity, Elevation, Ambient, Brightness, Contrast, Saturation, LampKelvin, Lamp, Window;
            public Color AmbientColor, Tint, WindowColor;

            public static Look Lerp(Look a, Look b, float t) => new Look
            {
                Kelvin = Mathf.Lerp(a.Kelvin, b.Kelvin, t), Intensity = Mathf.Lerp(a.Intensity, b.Intensity, t),
                Elevation = Mathf.Lerp(a.Elevation, b.Elevation, t), Ambient = Mathf.Lerp(a.Ambient, b.Ambient, t),
                Brightness = Mathf.Lerp(a.Brightness, b.Brightness, t), Contrast = Mathf.Lerp(a.Contrast, b.Contrast, t),
                Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t),
                AmbientColor = Color.Lerp(a.AmbientColor, b.AmbientColor, t), Tint = Color.Lerp(a.Tint, b.Tint, t),
                LampKelvin = Mathf.Lerp(a.LampKelvin, b.LampKelvin, t), Lamp = Mathf.Lerp(a.Lamp, b.Lamp, t),
                Window = Mathf.Lerp(a.Window, b.Window, t), WindowColor = Color.Lerp(a.WindowColor, b.WindowColor, t),
            };
        }

        [SerializeField] DesignStore _store;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField, Tooltip("content's mood presets.")] MoodPresets _moods;
        [SerializeField, Tooltip("Sun intensity for relative intensity 1 (the afternoon preset).")] float _sunBase = 1.2f;
        [SerializeField, Range(0f, 1f), Tooltip("How much passthrough takes on the sun's colour temperature, so real and virtual match.")]
        float _passthroughMatch = 0.25f;

        /// <summary>Current (blended) mood lamp temperature, for lamps whose catalog kelvin is 0. Read by <see cref="LightGlows"/>.</summary>
        public float LampKelvin { get; private set; } = 2700f;

        /// <summary>Current (blended) mood multiplier on every lamp's own intensity.</summary>
        public float LampMultiplier { get; private set; } = 1f;

        /// <summary>Current (blended) window light colour; <see cref="WindowIntensity"/> is its strength.</summary>
        public Color WindowColor { get; private set; } = Color.white;

        public float WindowIntensity { get; private set; }

        /// <summary>Goes up whenever the look changes, so readers update only then.</summary>
        public int Version { get; private set; }

        Light _sun;
        OVRPassthroughLayer _passthrough;
        ToneMatchSettings _tone;
        string _moodId;
        Look _from, _to, _now;
        float _blendStart, _blendSeconds;
        bool _blending, _suspended;

        void Start()
        {
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { _sun = l; break; }
            }
            _passthrough = FindAnyObjectByType<OVRPassthroughLayer>();
            _tone = ToneMatchSettings.Load();
            if (_sun != null) _sun.useColorTemperature = true;
            RenderSettings.ambientMode = AmbientMode.Trilight; // shape on furniture: tops brighter, sides less, undersides darker

            _now = Neutral();
            Apply(_now, true);
            EnsurePassthrough("start");
            _store.Session.Changed += OnDesignChanged;
            if (_room != null) _room.Changed += OnRoomChanged;
            OnDesignChanged();
        }

        void OnApplicationPause(bool paused) { SetSuspended(paused); if (!paused) EnsurePassthrough("resume"); }
        // Losing the input to system UI (the Meta menu, YouTube, a remote-desktop display over the app) keeps the mood: the
        // app is still the room behind them (the human). Only a pause (nothing renders) goes neutral.
        void OnApplicationFocus(bool focused) { if (focused) EnsurePassthrough("focus"); }
        void OnRoomChanged() { SetSuspended(!_room.HasScan); EnsurePassthrough("room reload"); }

        /// <summary>
        /// Keeps the real room visible (device batch 8: the zone's floor and ceiling holes rendered black, so the
        /// passthrough layer was dark or off; earlier builds showed it). Logs the layer's state and switches it back on:
        /// passthrough enabled on OVRManager, the layer enabled, not hidden, fully opaque, and a colour scale that is
        /// never black. Called at start, on focus or resume (after Space Setup), after a room reload, Reset and the zone
        /// step. No allocation unless something had to be fixed (one log line).
        /// </summary>
        public void EnsurePassthrough(string why)
        {
            OVRManager manager = OVRManager.instance;
            if (_passthrough == null) _passthrough = FindAnyObjectByType<OVRPassthroughLayer>();
            bool managerOn = manager != null && manager.isInsightPassthroughEnabled;
            bool layerOn = _passthrough != null && _passthrough.enabled && _passthrough.gameObject.activeInHierarchy;
            bool hidden = _passthrough != null && _passthrough.hidden;
            float opacity = _passthrough != null ? _passthrough.textureOpacity : 0f;
            Vector4 scale = _passthrough != null ? _passthrough.colorScale : Vector4.zero;
            bool dark = _passthrough != null && _passthrough.overridePerLayerColorScaleAndOffset && scale.x + scale.y + scale.z < 0.3f;
            InputDebug.Log($"passthrough ({why}): manager {managerOn}, layer {(_passthrough != null ? layerOn.ToString() : "missing")}, hidden {hidden}, " +
                           $"opacity {opacity:F2}, colour scale {scale}, initialized {OVRPlugin.IsInsightPassthroughInitialized()}");
            if (manager != null && !managerOn) manager.isInsightPassthroughEnabled = true;
            if (_passthrough == null) { Debug.LogWarning($"{LogTag} No passthrough layer in the scene: the real room cannot show."); return; }
            if (!_passthrough.enabled) _passthrough.enabled = true;
            if (hidden) _passthrough.hidden = false;
            if (opacity < 1f) _passthrough.textureOpacity = 1f;
            if (dark) Apply(_suspended ? Neutral() : _now, false); // a black colour scale: put the current look back
            if (!managerOn || !layerOn || hidden || opacity < 1f || dark)
                Debug.LogWarning($"{LogTag} Passthrough was off or dark ({why}); switched back on.");
        }

        /// <summary>Neutral look while suspended; the current mood blends back in on resume.</summary>
        void SetSuspended(bool suspended)
        {
            if (suspended == _suspended) return;
            _suspended = suspended;
            if (suspended)
            {
                _blending = false;
                _now = Neutral();
                Apply(_now, true);
                if (_passthrough != null)
                {
                    _passthrough.overridePerLayerColorScaleAndOffset = false;
                    _passthrough.DisableColorMap();
                }
                return;
            }
            _moodId = null; // force a fresh blend from neutral to the saved mood
            OnDesignChanged();
        }

        void OnDestroy()
        {
            if (_store != null && _store.Session != null) _store.Session.Changed -= OnDesignChanged;
            if (_room != null) _room.Changed -= OnRoomChanged;
        }

        void OnDesignChanged()
        {
            string id = _store.Session.State.moodPresetId ?? "";
            if (id == _moodId || _suspended) return;
            _moodId = id;
            _from = _now;
            _to = Neutral();
            _blendSeconds = MinBlendSeconds;
            if (_moods != null && _moods.TryGet(id, out MoodPresets.Entry e))
            {
                _to = new Look
                {
                    Kelvin = e.sunKelvin, Intensity = e.sunIntensity * _sunBase, Elevation = e.sunElevationDeg,
                    AmbientColor = e.ambientColor, Ambient = e.ambientIntensity,
                    Brightness = e.passthroughBrightness, Contrast = e.passthroughContrast,
                    Saturation = e.passthroughSaturation, Tint = e.passthroughTint,
                    LampKelvin = e.lampKelvin, Lamp = e.lampIntensityMultiplier,
                    WindowColor = e.windowColor, Window = e.windowIntensity,
                };
                _blendSeconds = Mathf.Max(MinBlendSeconds, e.blendSeconds);
            }
            _blendStart = Time.time;
            _blending = true;
            Debug.Log($"{LogTag} Mood -> '{id}' over {_blendSeconds:0.0} s.");
            EnsurePassthrough("mood " + id); // the layer's state in the log on every mood change (device bug on f1d4fe0: Night went black)
        }

        void Update()
        {
            if (!_blending) return;
            float t = Mathf.Clamp01((Time.time - _blendStart) / _blendSeconds);
            _now = Look.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, t));
            bool done = t >= 1f;
            Apply(_now, done);
            _blending = !done;
        }

        void Apply(Look look, bool saturation)
        {
            if (_sun != null)
            {
                _sun.colorTemperature = look.Kelvin;
                _sun.intensity = look.Intensity;
                _sun.transform.rotation = Quaternion.Euler(look.Elevation, -30f, 0f);
            }
            // Trilight, not flat (device, build e3f28ec: "every face lit the same"): the mood's ambient becomes a brighter sky, the
            // same colour at the horizon and a darker ground, so a piece's top, sides and bottom differ without shadow maps.
            Color ambient = look.AmbientColor * look.Ambient;
            Trilight(ambient, out Color sky, out Color equator, out Color ground);
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientProbe = AmbientProbe(sky, equator, ground);
            LampKelvin = look.LampKelvin;
            LampMultiplier = look.Lamp;
            WindowColor = look.WindowColor;
            WindowIntensity = look.Window;
            Version++;
            if (_tone != null) _tone.Push(look.Kelvin, look.Tint); // virtual surfaces take the mood's temperature and the passthrough's tint (one space)
            if (_passthrough == null) return;
            _passthrough.overridePerLayerColorScaleAndOffset = true;
            // Colour matching: tint passthrough a little toward the sun's colour (normalised so it keeps brightness).
            Color sun = Mathf.CorrelatedColorTemperatureToRGB(look.Kelvin);
            float max = Mathf.Max(sun.r, Mathf.Max(sun.g, sun.b));
            if (max > 0f) sun /= max;
            Color match = Color.Lerp(Color.white, sun, _passthroughMatch);
            Grade(look.Brightness, look.Contrast, look.Tint, match, out Vector4 scale, out Vector4 offset);
            _passthrough.colorScale = scale;
            _passthrough.colorOffset = offset;
            if (saturation) _passthrough.SetBrightnessContrastSaturation(0f, 0f, look.Saturation);
        }

        /// <summary>Lowest per-channel gain and offset on passthrough: the real room always stays visible (about half brightness at most dimmed).</summary>
        public const float MinGain = 0.55f, MinOffset = -0.02f, MaxOffset = 0.12f;

        /// <summary>
        /// Passthrough's grade (pure, for tests): out = in × scale + offset per channel, with contrast around mid-grey,
        /// brightness as an offset and the tint as a scale. Device bug on f1d4fe0: Night (brightness −0.3, contrast 0.1,
        /// a blue tint) gave an offset of −0.125, which crushed a dim real room to pure black in the window, the zone
        /// floor and the ceiling patch. Each gain is now at least <see cref="MinGain"/> and the offset is at least
        /// <see cref="MinOffset"/>, so Night is a dim, cool tint at most.
        /// </summary>
        public static void Grade(float brightness, float contrast, Color tint, Color match, out Vector4 scale, out Vector4 offset)
        {
            float k = 1f + contrast;
            float o = Mathf.Clamp(brightness * BrightnessToOffset + 0.5f * (1f - k), MinOffset, MaxOffset);
            scale = new Vector4(Mathf.Max(MinGain, tint.r * match.r * k), Mathf.Max(MinGain, tint.g * match.g * k), Mathf.Max(MinGain, tint.b * match.b * k), 1f);
            offset = new Vector4(o, o, o, 0f);
        }

        Look Neutral() => new Look
        {
            Kelvin = 5500f, Intensity = _sunBase, Elevation = 50f, AmbientColor = new Color(0.5f, 0.5f, 0.5f), Ambient = 1f,
            Brightness = 0f, Contrast = 0f, Saturation = 0f, Tint = Color.white,
            LampKelvin = 2700f, Lamp = 1f, WindowColor = new Color(1f, 0.97f, 0.9f), Window = 0.6f,
        };

        /// <summary>The trilight gradient for a mood's flat ambient (pure, for tests): sky 1.2x, horizon 1x, ground 0.45x.</summary>
        public const float SkyGain = 1.2f, GroundGain = 0.45f;
        public static void Trilight(Color ambient, out Color sky, out Color equator, out Color ground)
        {
            sky = ambient * SkyGain;
            equator = ambient;
            ground = ambient * GroundGain;
            sky.a = equator.a = ground.a = 1f;
        }

        /// <summary>
        /// The ambient probe for a trilight gradient, set explicitly (what the shaders' SampleSH reads; a runtime change of the
        /// gradient alone does not update it): the horizon colour all round, plus a cosine lobe up for the sky and down for the
        /// ground. Pure, for tests.
        /// </summary>
        public static SphericalHarmonicsL2 AmbientProbe(Color sky, Color equator, Color ground)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(equator);
            // A directional lobe adds pi * colour at its peak, spread as a cosine; scale so the top gets about (sky - equator).
            Color up = sky - equator, down = ground - equator;
            sh.AddDirectionalLight(Vector3.up, new Color(Mathf.Max(0f, up.r), Mathf.Max(0f, up.g), Mathf.Max(0f, up.b)) / Mathf.PI, 1f);
            sh.AddDirectionalLight(Vector3.down, new Color(Mathf.Max(0f, -down.r), Mathf.Max(0f, -down.g), Mathf.Max(0f, -down.b)) / -Mathf.PI, 1f);
            return sh;
        }
    }
}

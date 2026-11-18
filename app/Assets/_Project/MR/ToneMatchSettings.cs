using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Realism pass H: the numbers that make virtual surfaces sit with passthrough, in one tunable asset
    /// (MR/Resources/ToneMatchSettings.asset). Virtual pixels only (alpha &gt; 0) lose a little saturation and
    /// contrast, take a little of the mood's colour temperature (the sun's Kelvin, which MoodApplier blends), and get a
    /// very light grain. Applied inside each virtual surface's own shader through MR/Shaders/ToneMatch.hlsl (no
    /// extra render pass: the Quest tile pass stays intact; UI does not include it, so text stays crisp). MoodApplier
    /// pushes these as shader globals when the look changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Own Space/Tone match settings")]
    public sealed class ToneMatchSettings : ScriptableObject
    {
        public const string ResourceName = "ToneMatchSettings";
        static readonly int GradeId = Shader.PropertyToID("_OS_ToneGrade"), TintId = Shader.PropertyToID("_OS_ToneTint");

        [Tooltip("Off: every surface keeps its colour (the include returns it unchanged).")]
        public bool Enabled = true;
        [Range(0f, 1.2f), Tooltip("1 = unchanged; lower is greyer. Passthrough is less saturated than rendered colour.")]
        public float Saturation = 0.88f;
        [Range(0.5f, 1.2f), Tooltip("1 = unchanged; lower is flatter (a power curve around mid-grey, blacks stay black).")]
        public float Contrast = 0.92f;
        [Range(0f, 0.1f), Tooltip("Grain amplitude in linear colour; passthrough is noisy, clean CG reads as pasted on.")]
        public float Grain = 0.015f;

        [Header("Contact shadows (realism pass G)")]
        [Range(0f, 1f), Tooltip("Darkness of a contact shadow over the piece's footprint; it fades out over 20 cm past it (ContactShadows).")]
        public float ShadowStrength = ContactShadows.DefaultStrength;
        [Range(0f, 1f), Tooltip("How much the mood's sun colour temperature tints virtual surfaces (on top of the lighting).")]
        public float Temperature = 0.2f;
        [Tooltip("The Kelvin that leaves colours unchanged.")]
        public float NeutralKelvin = 6500f;

        static ToneMatchSettings _loaded;

        /// <summary>The asset from Resources, or null.</summary>
        public static ToneMatchSettings Load() => _loaded != null ? _loaded : (_loaded = Resources.Load<ToneMatchSettings>(ResourceName));

        /// <summary>The grade vector (saturation, contrast, grain, on) (pure, for tests).</summary>
        public Vector4 Grade() => new Vector4(Saturation, Contrast, Grain, Enabled ? 1f : 0f);

        /// <summary>The white-balance multiplier for a mood's sun Kelvin, normalised to keep brightness (pure, for tests).</summary>
        public Color Tint(float kelvin)
        {
            Color k = Normalised(Mathf.CorrelatedColorTemperatureToRGB(kelvin));
            Color n = Normalised(Mathf.CorrelatedColorTemperatureToRGB(NeutralKelvin));
            var rel = new Color(k.r / Mathf.Max(n.r, 1e-3f), k.g / Mathf.Max(n.g, 1e-3f), k.b / Mathf.Max(n.b, 1e-3f), 1f);
            return Color.Lerp(Color.white, Normalised(rel), Temperature);
        }

        /// <summary>Sets the pass's globals for <paramref name="kelvin"/>. No allocation.</summary>
        public void Push(float kelvin) => Push(kelvin, Color.white);

        /// <summary>
        /// Sets the globals for <paramref name="kelvin"/> and the mood's passthrough tint (realism pass: Night tints the real
        /// room blue, the virtual room did not, so they read as two spaces). No allocation.
        /// </summary>
        public void Push(float kelvin, Color moodTint)
        {
            Shader.SetGlobalVector(GradeId, Grade());
            Shader.SetGlobalColor(TintId, Tint(kelvin, moodTint));
        }

        /// <summary>How much of the mood's passthrough tint the virtual surfaces take (its colour only, brightness kept).</summary>
        public const float MoodTintShare = 0.8f;

        /// <summary>
        /// The virtual surfaces' white balance (pure, for tests): the mood temperature's tint, times the passthrough tint's
        /// colour (normalised, so the room is not darker), at <see cref="MoodTintShare"/>. Brightness already follows the mood
        /// through the sun and ambient light, so it is not applied twice.
        /// </summary>
        public Color Tint(float kelvin, Color moodTint)
        {
            Color t = Tint(kelvin), m = Color.Lerp(Color.white, Normalised(moodTint), MoodTintShare);
            return new Color(t.r * m.r, t.g * m.g, t.b * m.b, 1f);
        }

        static Color Normalised(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return max > 0f ? new Color(c.r / max, c.g / max, c.b / max, 1f) : Color.white;
        }
    }
}

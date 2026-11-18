using System;
using System.Collections.Generic;
using UnityEngine;

namespace OwnSpace.Content
{
    /// <summary>Looks up mood presets. Implemented by <see cref="MoodPresets"/>; fake it in tests.</summary>
    public interface IMoodPresets
    {
        IReadOnlyList<MoodPresets.Entry> Entries { get; }

        /// <summary>The preset for a design-state <c>moodPresetId</c> (morning, afternoon, evening, night). False for "" or an unknown id.</summary>
        bool TryGet(string moodId, out MoodPresets.Entry entry);
    }

    /// <summary>
    /// The four mood presets as data. Generated from <c>mood-presets.json</c> (values from docs/design/mood-presets.md)
    /// by the content editor script (menu: OwnSpace/Content/Build Mood Presets); do not edit by hand.
    /// Every value is a starting point until it is tuned on a Quest 3 (see <see cref="Entry.needsHeadset"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "OwnSpace/Mood Presets", fileName = "MoodPresets")]
    public sealed class MoodPresets : ScriptableObject, IMoodPresets
    {
        [Serializable]
        public struct Entry
        {
            [Tooltip("Mood id stored in the design state.")] public string id;
            public string name;

            [Header("Virtual lighting")]
            [Tooltip("Directional light color temperature in Kelvin (Light.useColorTemperature).")] public float sunKelvin;
            [Tooltip("Directional light intensity, relative to the afternoon sun (1).")] public float sunIntensity;
            [Tooltip("Sun angle above the horizon in degrees.")] public float sunElevationDeg;
            public Color ambientColor;
            [Tooltip("Ambient intensity multiplier.")] public float ambientIntensity;
            [Tooltip("Placed lamp color temperature in Kelvin.")] public float lampKelvin;
            [Tooltip("Multiplier on a placed lamp's base intensity.")] public float lampIntensityMultiplier;

            [Header("Window light")]
            public Color windowColor;
            public float windowIntensity;

            [Header("Passthrough (each -1..1, 0 = unchanged)")]
            public float passthroughBrightness;
            public float passthroughContrast;
            public float passthroughSaturation;
            [Tooltip("RGB multiplier applied to passthrough; white = no tint.")] public Color passthroughTint;

            [Tooltip("Seconds to blend from the previous preset. Never shorter, to avoid flashing.")] public float blendSeconds;
            [Tooltip("True when a value is outside the documented safe range and must be checked on a headset.")] public bool needsHeadset;
            public string notes;
        }

        [SerializeField, Tooltip("Generated list; one entry per mood.")]
        Entry[] _entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public bool TryGet(string moodId, out Entry entry)
        {
            entry = default;
            if (string.IsNullOrEmpty(moodId)) return false;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].id != moodId) continue;
                entry = _entries[i];
                return true;
            }
            return false;
        }
    }
}

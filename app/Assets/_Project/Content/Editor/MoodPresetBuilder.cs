using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Builds MoodPresets.asset from mood-presets.json. Idempotent. It checks the safety limits from
    /// docs/design/mood-presets.md and fails when a preset breaks them without being flagged needsHeadset.
    /// Batch mode: Unity -batchmode -nographics -projectPath app
    /// -executeMethod OwnSpace.Content.Editor.MoodPresetBuilder.BuildBatch -quit
    /// </summary>
    public static class MoodPresetBuilder
    {
        const string JsonPath = "Assets/_Project/Content/mood-presets.json";
        const string AssetPath = "Assets/_Project/Content/MoodPresets.asset";
        const float MinBrightness = -0.35f;  // never darken passthrough below this
        const float MinTintChannel = 0.75f;  // tint within about 25% of white
        const float MinBlendSeconds = 2.5f;
        static readonly string[] MoodIds = { "morning", "afternoon", "evening", "night" };

        [Serializable] class MoodFile { public Def[] presets = Array.Empty<Def>(); }
        [Serializable] class Def
        {
            public string id, name, ambientColor, windowColor, passthroughTint, notes;
            public float sunKelvin, sunIntensity, sunElevationDeg, ambientIntensity, lampKelvin, lampIntensityMultiplier,
                windowIntensity, passthroughBrightness, passthroughContrast, passthroughSaturation, blendSeconds;
            public bool needsHeadset;
        }

        [MenuItem("OwnSpace/Content/Build Mood Presets")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e)
            {
                Debug.LogError($"[OwnSpace.Content] Mood build failed: {e}");
                code = 2;
            }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            var file = JsonUtility.FromJson<MoodFile>(File.ReadAllText(JsonPath));
            bool ok = true;
            foreach (var id in MoodIds)
                if (Array.FindIndex(file.presets, p => p.id == id) < 0) { Debug.LogError($"[OwnSpace.Content] mood \"{id}\" is missing."); ok = false; }

            // Validate everything before touching the asset: a failed build leaves the old file as it was.
            foreach (var d in file.presets)
            {
                var tint = Parse(d.passthroughTint);
                bool outside = d.passthroughBrightness < MinBrightness || Mathf.Min(tint.r, tint.g, tint.b) < MinTintChannel;
                if (outside && !d.needsHeadset)
                {
                    Debug.LogError($"[OwnSpace.Content] {d.id}: outside the safe range (brightness {d.passthroughBrightness}, tint {d.passthroughTint}); set needsHeadset.");
                    ok = false;
                }
                if (d.blendSeconds < MinBlendSeconds) { Debug.LogError($"[OwnSpace.Content] {d.id}: blendSeconds under {MinBlendSeconds}."); ok = false; }
            }
            if (!ok) return false;

            var set = AssetDatabase.LoadAssetAtPath<MoodPresets>(AssetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<MoodPresets>();
                AssetDatabase.CreateAsset(set, AssetPath);
            }
            var so = new SerializedObject(set);
            var list = so.FindProperty("_entries");
            list.arraySize = file.presets.Length;

            for (int i = 0; i < file.presets.Length; i++)
            {
                var d = file.presets[i];
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").stringValue = d.id;
                e.FindPropertyRelative("name").stringValue = d.name;
                e.FindPropertyRelative("sunKelvin").floatValue = d.sunKelvin;
                e.FindPropertyRelative("sunIntensity").floatValue = d.sunIntensity;
                e.FindPropertyRelative("sunElevationDeg").floatValue = d.sunElevationDeg;
                e.FindPropertyRelative("ambientColor").colorValue = Parse(d.ambientColor);
                e.FindPropertyRelative("ambientIntensity").floatValue = d.ambientIntensity;
                e.FindPropertyRelative("lampKelvin").floatValue = d.lampKelvin;
                e.FindPropertyRelative("lampIntensityMultiplier").floatValue = d.lampIntensityMultiplier;
                e.FindPropertyRelative("windowColor").colorValue = Parse(d.windowColor);
                e.FindPropertyRelative("windowIntensity").floatValue = d.windowIntensity;
                e.FindPropertyRelative("passthroughBrightness").floatValue = d.passthroughBrightness;
                e.FindPropertyRelative("passthroughContrast").floatValue = d.passthroughContrast;
                e.FindPropertyRelative("passthroughSaturation").floatValue = d.passthroughSaturation;
                e.FindPropertyRelative("passthroughTint").colorValue = Parse(d.passthroughTint);
                e.FindPropertyRelative("blendSeconds").floatValue = d.blendSeconds;
                e.FindPropertyRelative("needsHeadset").boolValue = d.needsHeadset;
                e.FindPropertyRelative("notes").stringValue = d.notes;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnSpace.Content] Built {file.presets.Length} mood presets.");
            return true;
        }

        static Color Parse(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) throw new FormatException($"bad color {hex}");
            return c;
        }
    }
}

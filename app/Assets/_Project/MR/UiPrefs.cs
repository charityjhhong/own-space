using System;
using System.IO;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The user's UI preferences (the panel theme and size), kept apart from the design save: a device
    /// preference, not part of the room design. One small JSON file under persistentDataPath, written
    /// through core's <see cref="AtomicFile"/>. Reading never throws: a missing or broken file gives
    /// the defaults (and a broken one is kept as .bad). Read once at start and written on change.
    /// </summary>
    public static class UiPrefs
    {
        public const string FileName = "ui-prefs.json";
        public const string Light = "light", Dark = "dark";

        [Serializable]
        sealed class Data
        {
            public int version = 1;
            public string theme = Light;
            public float scale = 1f; // the panel's resize, 0.8-1.6
            public bool occlusion = true; // no longer read: the old switch (default on). Older files carry it.
            public bool occlusionExperiment; // development builds: the real-occlusion experiment switch, default off
            public bool biggerRoom; // Settings › Room › Bigger room (VirtualExtent), default off
        }

        // The whole file (defaults when missing or broken); each Save rewrites it with one field changed.
        static Data Load(string path)
        {
            string text = AtomicFile.TryReadAllText(path);
            if (string.IsNullOrEmpty(text)) return new Data();
            try { return JsonUtility.FromJson<Data>(text) ?? new Data(); }
            catch (ArgumentException) { AtomicFile.KeepBrokenCopy(path); return new Data(); }
        }

        static bool Save(string path, Data data)
        {
            try
            {
                AtomicFile.WriteAllText(path, JsonUtility.ToJson(data));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[OwnSpace.MR] UI prefs not saved: {e.Message}");
                return false;
            }
        }

        /// <summary>The panel's scale; 1 when missing or out of 0.8-1.6.</summary>
        public static float LoadScale(string path)
        {
            float s = Load(path).scale;
            return s >= 0.8f && s <= 1.6f ? s : 1f;
        }

        /// <summary>Saves the panel's scale (the theme is kept).</summary>
        public static bool SaveScale(string path, float scale)
        {
            Data data = Load(path);
            data.scale = Mathf.Clamp(scale, 0.8f, 1.6f);
            return Save(path, data);
        }

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>"light" or "dark"; "light" when the file is missing, broken or holds anything else.</summary>
        public static string LoadTheme(string path)
        {
            string text = AtomicFile.TryReadAllText(path);
            if (string.IsNullOrEmpty(text)) return Light;
            try
            {
                Data data = JsonUtility.FromJson<Data>(text);
                return data != null && data.theme == Dark ? Dark : Light;
            }
            catch (ArgumentException)
            {
                AtomicFile.KeepBrokenCopy(path);
                return Light;
            }
        }

        /// <summary>Saves the theme; false (with a warning) when the file cannot be written.</summary>
        public static bool SaveTheme(string path, string theme)
        {
            Data data = Load(path);
            data.theme = theme == Dark ? Dark : Light; // the scale is kept
            return Save(path, data);
        }

        /// <summary>
        /// The real-occlusion experiment switch (development builds); off when the file is missing or older. A new field, so
        /// an "on" saved by the old switch (default on) does not carry over (the human: off by default).
        /// </summary>
        public static bool LoadOcclusion(string path) => Load(path).occlusionExperiment;

        /// <summary>Settings › Bigger room (<see cref="VirtualExtent"/>); off when the file is missing or older.</summary>
        public static bool LoadBiggerRoom(string path) => Load(path).biggerRoom;

        /// <summary>Saves Bigger room (the rest is kept).</summary>
        public static bool SaveBiggerRoom(string path, bool on)
        {
            Data data = Load(path);
            data.biggerRoom = on;
            return Save(path, data);
        }

        /// <summary>Saves the real-occlusion switch (the rest is kept).</summary>
        public static bool SaveOcclusion(string path, bool on)
        {
            Data data = Load(path);
            data.occlusionExperiment = on;
            return Save(path, data);
        }
    }
}

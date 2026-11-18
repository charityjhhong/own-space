using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Save/restore for <see cref="DesignState"/>. Newtonsoft (com.unity.nuget.newtonsoft-json)
    /// keeps Core free of UnityEngine; enums are written as names so saves survive reordering.
    /// </summary>
    public static class DesignStateJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string ToJson(DesignState state) =>
            JsonConvert.SerializeObject(state ?? throw new ArgumentNullException(nameof(state)), Settings);

        /// <summary>
        /// False, with a fresh empty state, when the text is empty, unreadable, or from a newer app
        /// version. A broken save must never block the app from starting.
        /// </summary>
        public static bool TryFromJson(string json, out DesignState state)
        {
            state = new DesignState();
            if (string.IsNullOrWhiteSpace(json)) return false;

            DesignState read;
            JObject root;
            try
            {
                root = JObject.Parse(json);
                read = root.ToObject<DesignState>(Serializer);
            }
            catch (JsonException)
            {
                return false;
            }

            if (read == null || read.version > DesignState.CurrentVersion) return false;
            if (read.version < 2) MigrateV1(root, read);

            // Lists can be null if the file says so explicitly.
            if (read.zoneOutline == null) read.zoneOutline = new DesignState().zoneOutline;
            if (read.anchorChoices == null) read.anchorChoices = new DesignState().anchorChoices;
            if (read.placements == null) read.placements = new DesignState().placements;
            if (read.hideBoxes == null) read.hideBoxes = new DesignState().hideBoxes;
            if (read.coverProps == null) read.coverProps = new DesignState().coverProps;
            read.myZoneFinish = Repair(read.myZoneFinish);
            read.outsideFinish = Repair(read.outsideFinish);
            if (read.moodPresetId == null) read.moodPresetId = "";

            // Saves from before the fit stored a Y scale have none: show those items unscaled.
            for (int i = 0; i < read.anchorChoices.Count; i++)
            {
                var c = read.anchorChoices[i];
                c.yScale = OneIfUnset(c.yScale);
                c.xScale = OneIfUnset(c.xScale);
                c.zScale = OneIfUnset(c.zScale);
                read.anchorChoices[i] = c;
            }

            state = read;
            return true;
        }

        // Version 1 had one floor and one wall finish at the top level; they were meant for the
        // outside zone (CONCEPT 5.3), so they move there. Saves are always written as the current version.
        static void MigrateV1(JObject root, DesignState read)
        {
            read.outsideFinish = new ZoneFinish
            {
                floorFinishId = Text(root["floorFinishId"]),
                wallFinishId = Text(root["wallFinishId"]),
            };
            read.version = DesignState.CurrentVersion;
        }

        static string Text(JToken token) => token != null && token.Type == JTokenType.String ? (string)token : "";

        // Saves from before a scale was stored have none: show those items unscaled.
        static float OneIfUnset(float scale) => scale > 0f && !float.IsInfinity(scale) ? scale : 1f;

        static ZoneFinish Repair(ZoneFinish f) =>
            new ZoneFinish { floorFinishId = f.floorFinishId ?? "", wallFinishId = f.wallFinishId ?? "" };

        /// <summary>
        /// Writes the state atomically: a temp file first, then it replaces the old save in one
        /// step, so a crash or power loss mid-write leaves the previous save intact.
        /// </summary>
        public static void SaveToFile(string path, DesignState state) => AtomicFile.WriteAllText(path, ToJson(state));

        /// <summary>False, with an empty state, when the file is missing, unreadable or broken.</summary>
        /// <remarks>A file that exists but cannot be used is kept as <c>&lt;path&gt;.bad</c> (see <see cref="AtomicFile.KeepBrokenCopy"/>).</remarks>
        public static bool TryLoadFromFile(string path, out DesignState state)
        {
            string text = AtomicFile.TryReadAllText(path);
            if (TryFromJson(text, out state)) return true;
            if (text != null) AtomicFile.KeepBrokenCopy(path);
            return false;
        }
    }
}

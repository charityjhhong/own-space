using System;
using System.IO;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Whether the user turned this room's zone with the rotate handle (MR-side, next to the design save:
    /// <c>design-&lt;room&gt;.json.zone.json</c>). Old saves have none: false. A zone not turned by the user is squared to
    /// <see cref="ZoneAlign"/>'s direction on load (device, build dfb22f4: a zone left about 33 degrees off by the old skew bug).
    /// </summary>
    public static class ZoneTurnRecord
    {
        [Serializable] sealed class Data { public bool turnedByUser; }

        public static string PathFor(string designPath) => designPath + ".zone.json";

        public static bool TurnedByUser(string designPath)
        {
            string path = designPath != null ? PathFor(designPath) : null;
            if (path == null || !File.Exists(path)) return false;
            try { return JsonUtility.FromJson<Data>(File.ReadAllText(path))?.turnedByUser ?? false; }
            catch (Exception e) when (e is IOException || e is ArgumentException) { Debug.LogWarning($"[OwnSpace.MR] Zone record not read: {e.Message}"); return false; }
        }

        public static void Write(string designPath, bool turnedByUser)
        {
            if (designPath == null) return;
            try { AtomicFile.WriteAllText(PathFor(designPath), JsonUtility.ToJson(new Data { turnedByUser = turnedByUser })); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"[OwnSpace.MR] Zone record not saved: {e.Message}"); }
        }
    }
}

using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR
{
    /// <summary>
    /// One design save per room (device day #16: a zone saved in one room came back in another). The key is the
    /// MRUK room anchor's UUID, or <see cref="NoScanKey"/> without a usable scan. Pure, for tests.
    /// </summary>
    public static class RoomSaves
    {
        public const string NoScanKey = "noscan";

        /// <summary>
        /// Reset my design: one room's save becomes an empty design that keeps the zone (finishes, mood, redesigns, placed
        /// pieces, hide boxes and covers go; the zone stays, so there is no zone step). Every other room's file is untouched.
        /// </summary>
        public static void Reset(string path, IReadOnlyList<Vec2> keepZone = null)
        {
            var state = new DesignState();
            if (keepZone != null) state.zoneOutline.AddRange(keepZone);
            DesignStateJson.SaveToFile(path, state);
        }

        /// <summary>"design.json" + "abc" → "design-abc.json".</summary>
        public static string FileName(string baseName, string key) =>
            Path.GetFileNameWithoutExtension(baseName) + "-" + key + Path.GetExtension(baseName);

        /// <summary>
        /// Whether a save may come back here. A scanned room's own save always does. A save that is not tied to
        /// this room (the old single save, or the shared no-scan save) comes back only when its zone is around the
        /// user, so another room's zone is never restored somewhere else.
        /// </summary>
        public static bool Restore(bool ownRoomSave, IReadOnlyList<Vec2> zone, Vec2 head) =>
            ownRoomSave || (zone != null && zone.Count >= 3 && Zones.Contains(zone, head));
    }
}

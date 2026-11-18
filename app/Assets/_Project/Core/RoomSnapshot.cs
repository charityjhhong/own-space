using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// A saved room ("Saved spaces"): a copy of what <see cref="IRoomInfo"/> exposes, the design made in it,
    /// a name, a creation time and an anchor by which it is placed again anywhere with <see cref="SnapshotRoomInfo"/>:
    /// the desk or bed the user was at (the user is always at one), or the zone's centre in a room with neither.
    /// Room-frame coordinates, as scanned.
    /// </summary>
    public sealed class RoomSnapshot
    {
        public const int CurrentVersion = 1;
        public const int MaxJsonChars = 512 * 1024;
        public const int MaxPoints = 256, MaxElements = 64, MaxNameLength = 40;

        public int version = CurrentVersion;
        public string id = "";
        public string name = "";
        /// <summary>UTC, ISO 8601 ("o" format).</summary>
        public string createdUtc = "";
        public List<Vec2> floor = new List<Vec2>();
        public float floorY, ceilingY;
        public List<SnapshotElement> walls = new List<SnapshotElement>();
        public List<SnapshotElement> windows = new List<SnapshotElement>();
        public List<SnapshotElement> doors = new List<SnapshotElement>();
        public List<SnapshotFurniture> furniture = new List<SnapshotFurniture>();
        public DesignState design = new DesignState();
        /// <summary>The desk or bed the space is anchored by, or "" for the zone (or floor) centre.</summary>
        public string anchorId = "";
        /// <summary>Table or Bed for a furniture anchor; Other for a centre.</summary>
        public FurnitureRole anchorRole = FurnitureRole.Other;
        /// <summary>The anchor on the floor (y = floorY) and the way it faces.</summary>
        public Vec3 anchorPoint;
        public float anchorYaw;

        /// <summary>
        /// Copies a scanned room and its design. Refuses a room with no scan (nothing to rebuild) and one
        /// too big to save. The anchor is the desk or bed the user is at (<see cref="ZoneProposer.PieceAt"/>, from
        /// <paramref name="userPosition"/>); in a room with neither, the centre of the design's zone, or of the floor.
        /// </summary>
        public static bool TryCapture(IRoomInfo room, DesignState design, string name, DateTime createdUtc, Vec3 userPosition,
            out RoomSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (room == null || !room.HasScan || room.FloorOutline.Count < 3) { error = "no room scan"; return false; }
            var s = new RoomSnapshot
            {
                name = CleanName(name),
                createdUtc = createdUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                floor = new List<Vec2>(room.FloorOutline),
                floorY = room.FloorY,
                ceilingY = room.CeilingY,
                design = (design ?? new DesignState()).Clone(),
            };
            foreach (var w in room.Walls) s.walls.Add(new SnapshotElement { id = w.Id, box = w.Box });
            foreach (var w in room.Windows) s.windows.Add(new SnapshotElement { id = w.Id, box = w.Box });
            foreach (var d in room.Doors) s.doors.Add(new SnapshotElement { id = d.Id, box = d.Box });
            foreach (var f in room.Furniture)
                s.furniture.Add(new SnapshotFurniture { id = f.Id, role = f.Role, box = f.Box, usableHeight = f.UsableHeight ?? -1f });

            var at = ZoneProposer.PieceAt(room, userPosition);
            var anchor = at == null ? null : s.furniture.Find(f => f.id == at.Id);
            if (anchor != null)
            {
                s.anchorId = anchor.id;
                s.anchorRole = anchor.role;
                s.anchorPoint = new Vec3(anchor.box.center.x, s.floorY, anchor.box.center.z);
                s.anchorYaw = anchor.box.yawDegrees;
            }
            else
            {
                var c = Centre(s.design.zoneOutline.Count >= 3 ? s.design.zoneOutline : s.floor);
                s.anchorPoint = new Vec3(c.x, s.floorY, c.y);
            }
            error = Validate(s);
            if (error == null && RoomSnapshotJson.ToJson(s).Length > MaxJsonChars) error = "room too big to save";
            if (error != null) return false;
            snapshot = s;
            return true;
        }

        internal static string CleanName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "My room";
            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength);
        }

        /// <summary>Null when the snapshot can be played back; otherwise what is wrong.</summary>
        internal static string Validate(RoomSnapshot s)
        {
            if (s == null) return "empty";
            if (s.version < 1 || s.version > CurrentVersion) return "unknown version " + s.version;
            if (s.floor == null || s.floor.Count < 3) return "no floor outline";
            if (s.floor.Count > MaxPoints || Count(s.walls) > MaxElements || Count(s.windows) > MaxElements ||
                Count(s.doors) > MaxElements || Count(s.furniture) > MaxElements) return "room too big to save";
            foreach (var p in s.floor) if (!Finite(p.x) || !Finite(p.y)) return "bad floor point";
            if (!Finite(s.floorY) || !Finite(s.ceilingY) || !Finite(s.anchorPoint.x) || !Finite(s.anchorPoint.z) || !Finite(s.anchorYaw))
                return "bad number";
            return null;
        }

        static int Count<T>(List<T> list) => list?.Count ?? 0;
        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static Vec2 Centre(List<Vec2> points)
        {
            float x = 0f, y = 0f;
            foreach (var p in points) { x += p.x; y += p.y; }
            return new Vec2(x / points.Count, y / points.Count);
        }
    }

    [Serializable]
    public sealed class SnapshotElement
    {
        public string id = "";
        public OrientedBox box;
    }

    [Serializable]
    public sealed class SnapshotFurniture
    {
        public string id = "";
        public FurnitureRole role;
        public OrientedBox box;
        /// <summary>Usable surface height, or below 0 when unknown.</summary>
        public float usableHeight = -1f;
    }

    /// <summary>Versioned JSON for <see cref="RoomSnapshot"/>; the design inside goes through <see cref="DesignStateJson"/>.</summary>
    public static class RoomSnapshotJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Converters = { new StringEnumConverter() },
        };

        public static string ToJson(RoomSnapshot snapshot) =>
            JsonConvert.SerializeObject(snapshot ?? throw new ArgumentNullException(nameof(snapshot)), Settings);

        /// <summary>False (and null) for empty, broken, oversized, newer-version or unplayable text; never throws.</summary>
        public static bool TryFromJson(string json, out RoomSnapshot snapshot, out string error)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "empty"; return false; }
            if (json.Length > RoomSnapshot.MaxJsonChars) { error = "room too big to save"; return false; }
            try
            {
                // Dates stay text: JObject.Parse would turn createdUtc into a local DateTime string.
                JObject root;
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None })
                    root = JObject.Load(reader);
                var designJson = root["design"]?.ToString(Formatting.None);
                root.Remove("design");
                var s = root.ToObject<RoomSnapshot>(JsonSerializer.Create(Settings));
                if (s == null) { error = "empty"; return false; }
                // The design gets the same repairs and version checks as a normal save.
                if (designJson != null && !DesignStateJson.TryFromJson(designJson, out s.design)) { error = "unreadable design"; return false; }
                s.design = s.design ?? new DesignState();
                s.name = RoomSnapshot.CleanName(s.name);
                s.walls = s.walls ?? new List<SnapshotElement>();
                s.windows = s.windows ?? new List<SnapshotElement>();
                s.doors = s.doors ?? new List<SnapshotElement>();
                s.furniture = s.furniture ?? new List<SnapshotFurniture>();
                error = RoomSnapshot.Validate(s);
                if (error != null) return false;
                snapshot = s;
                return true;
            }
            catch (Exception e) when (e is JsonException || e is InvalidOperationException || e is ArgumentException || e is FormatException)
            {
                error = "unreadable";
                return false;
            }
        }
    }
}

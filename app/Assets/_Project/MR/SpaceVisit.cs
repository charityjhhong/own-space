using System;
using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// "Go there" for Saved spaces: rebuilds a saved room around the user as virtual walls, floor and ceiling, with its
    /// finishes, mood, placed pieces and furniture designs. A small real zone stays around the desk or bed the user is at.
    /// <list type="bullet">
    /// <item>The saved anchor (desk or bed) lands on the real piece of the same role (core's <see cref="RoomPose.AlignTo"/>),
    /// else <see cref="AheadOfUser"/> in front of the user, facing their view.</item>
    /// <item>Its design is saved under its own key ("space-&lt;id&gt;"), never mixed with scanned rooms. The next visit
    /// brings back the zone and every change from the last one, moved with the real desk or bed the room lands on
    /// (the human). Only when that real piece differs (another size, or none now) is the zone fitted again;
    /// the changes stay. "Leave" returns to the real room.</item>
    /// </list>
    /// </summary>
    public sealed class SpaceVisit
    {
        const string LogTag = "[OwnSpace.MR]";
        /// <summary>With no real desk or bed, the saved anchor's centre lands this far in front of the user.</summary>
        public const float AheadOfUser = 0.55f;
        public const string KeyPrefix = "space-";
        /// <summary>A real desk or bed within this much of last visit's size (each side, metres) counts as the same one.</summary>
        public const float SameSize = 0.15f;

        /// <summary>Where the last visit stood: its pose and the real piece it landed on (none = zero size).</summary>
        [Serializable]
        public sealed class VisitRecord
        {
            public float yaw, x, y, z, realW, realD;
            public bool hasReal;

            public static VisitRecord Of(RoomPose pose, FurnitureAnchor real) => new VisitRecord
            {
                yaw = pose.YawDegrees, x = pose.Translation.x, y = pose.Translation.y, z = pose.Translation.z,
                hasReal = real != null, realW = real != null ? real.Box.size.x : 0f, realD = real != null ? real.Box.size.z : 0f,
            };

            public RoomPose Pose => new RoomPose(yaw, new Vec3(x, y, z));
        }

        /// <summary>Whether the visit fitted the zone again (the real desk or bed changed) instead of bringing it back.</summary>
        public bool ZoneRefitted { get; private set; }

        readonly MrukRoomInfo _room;
        readonly DesignStore _store;
        readonly EditMode _edit;
        readonly HandRig _hands;
        readonly List<VirtualPiece> _pieces = new List<VirtualPiece>();

        public SpaceVisit(MrukRoomInfo room, DesignStore store, EditMode edit, HandRig hands)
        {
            _room = room;
            _store = store;
            _edit = edit;
            _hands = hands;
        }

        /// <summary>The visited space's name, or null in the real room.</summary>
        public string Current => _room.Visiting != null ? _room.Visiting.Snapshot.name : null;
        public string CurrentId => _room.Visiting != null ? _room.Visiting.Snapshot.id : null;
        /// <summary>Whether the real room has a usable scan (remembered across a visit, which shows the visited room's).</summary>
        public bool RealRoomHasScan => _room.Visiting != null ? _realScan : _room.HasScan;
        bool _realScan;

        /// <summary>Goes to <paramref name="snapshot"/>; false when it cannot (no head yet).</summary>
        public bool GoTo(RoomSnapshot snapshot)
        {
            if (snapshot == null || _hands == null) return false;
            if (_room.Visiting != null) Leave(); // a fresh pose from the real room every time
            _realScan = _room.HasScan;
            Vector3 head = _hands.HeadPosition;
            Vector3 view = _hands.View.direction;
            Plan(snapshot, _room.HasScan ? _room.Furniture : null, _room.HasScan ? _room.FloorY : 0f, head, view,
                out SnapshotRoomInfo visit, out DesignState seed, out FurnitureAnchor real);
            string key = KeyPrefix + snapshot.id;
            string designPath = _store.PathFor(key), recordPath = designPath + ".visit";
            ZoneRefitted = false;
            if (snapshot.id != SampleRoom.Id && TryLoadRecord(recordPath, out VisitRecord last) && DesignStateJson.TryLoadFromFile(designPath, out DesignState previous))
            {
                bool refit = Restore(previous, last, visit.Pose, real, seed);
                ZoneRefitted = refit;
                InputDebug.Log($"spaces: last visit's design back ({previous.placements.Count} pieces){(refit ? ", zone fitted again: the real desk or bed differs" : ", zone kept")}");
            }
            SaveRecord(recordPath, VisitRecord.Of(visit.Pose, real));
            _store.SeedNext(key, seed);
            _room.Visit(visit, key);
            visit.VirtualFurniture(_store.Session.Catalog, seed.zoneOutline, _pieces);
            _edit.ShowVisitPieces(visit, _pieces);
            InputDebug.Log($"spaces: in '{snapshot.name}', anchor {snapshot.anchorRole} at {visit.Pose.Translation.x:F2},{visit.Pose.Translation.z:F2}, yaw {visit.Pose.YawDegrees:0}");
            return true;
        }

        /// <summary>Back to the real room.</summary>
        public void Leave()
        {
            if (_room.Visiting == null) return;
            _pieces.Clear();
            _edit.ShowVisitPieces(null, null);
            _room.Leave();
        }

        /// <summary>
        /// The visit's room and starting design (pure, for tests). The pose comes from core. The zone holds the desk or bed
        /// the user is at in the posed room, with the usual margin.
        /// </summary>
        public static void Plan(RoomSnapshot snapshot, IReadOnlyList<FurnitureAnchor> realPieces, float floorY, Vector3 head, Vector3 view,
            out SnapshotRoomInfo visit, out DesignState seed) => Plan(snapshot, realPieces, floorY, head, view, out visit, out seed, out _);

        /// <param name="real">The real desk or bed the saved anchor landed on, or null.</param>
        public static void Plan(RoomSnapshot snapshot, IReadOnlyList<FurnitureAnchor> realPieces, float floorY, Vector3 head, Vector3 view,
            out SnapshotRoomInfo visit, out DesignState seed, out FurnitureAnchor real)
        {
            Vector3 flat = new Vector3(view.x, 0f, view.z);
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            // A seat-anchored room (the sample room) is posed on the user themself; a desk or bed anchor goes ahead of them.
            bool seat = snapshot.id == SampleRoom.Id;
            Vector3 ahead = seat ? head : head + flat * AheadOfUser;
            RoomPose pose = RoomPose.AlignTo(snapshot, realPieces, floorY, new Vec3(ahead.x, floorY, ahead.z), RoomGeometry.YawOf(flat), out real);
            visit = new SnapshotRoomInfo(snapshot, pose);
            seed = visit.VisitState();
            var zone = new List<Vec2>();
            if (seat) Square(zone, head, flat, SampleRoom.ZoneHalf); // the user's real spot: no virtual wall, door or piece inside
            else if (!ZoneFit.Contain(zone, visit.Furniture, new Vec2(head.x, head.z), visit.FloorOutline))
                Square(zone, head, Vector3.forward, 0.8f); // neither a desk nor a bed in the saved room: 1.6 m around the user
            seed.zoneOutline.Clear();
            seed.zoneOutline.AddRange(zone);
            visit.RedesignsInZone(seed.zoneOutline, seed.anchorChoices); // a redesigned desk or bed stays redesigned
        }

        /// <summary>
        /// Brings the last visit's design into <paramref name="seed"/> (pure, for tests): every piece, cover, hide box,
        /// choice, finish and mood, moved from last visit's pose to <paramref name="now"/>. The zone comes back too,
        /// unless the real piece the room landed on differs (<see cref="SameReal"/>); then the seed's fresh zone stays.
        /// Returns whether the zone was fitted again.
        /// </summary>
        public static bool Restore(DesignState previous, VisitRecord last, RoomPose now, FurnitureAnchor real, DesignState seed)
        {
            RoomPose was = last.Pose;
            seed.placements.Clear();
            foreach (FreePlacement p in previous.placements) seed.placements.Add(Moved(p, was, now));
            seed.coverProps.Clear();
            foreach (FreePlacement p in previous.coverProps) seed.coverProps.Add(Moved(p, was, now));
            seed.hideBoxes.Clear();
            foreach (HideBox b in previous.hideBoxes)
            {
                HideBox m = b;
                m.position = Move(b.position, was, now);
                m.yawDegrees = MoveYaw(b.yawDegrees, was, now);
                seed.hideBoxes.Add(m);
            }
            foreach (AnchorChoice c in previous.anchorChoices)
            {
                int at = seed.anchorChoices.FindIndex(x => x.anchorId == c.anchorId);
                if (at >= 0) seed.anchorChoices[at] = c; else seed.anchorChoices.Add(c);
            }
            seed.myZoneFinish = previous.myZoneFinish;
            seed.outsideFinish = previous.outsideFinish;
            seed.moodPresetId = previous.moodPresetId ?? "";
            bool refit = !SameReal(last, real) || previous.zoneOutline.Count < 3;
            if (!refit)
            {
                seed.zoneOutline.Clear();
                foreach (Vec2 v in previous.zoneOutline) seed.zoneOutline.Add(Move(v, was, now));
            }
            return refit;
        }

        /// <summary>Whether <paramref name="real"/> is the same kind of real piece as last visit's: both none, or both within <see cref="SameSize"/> (either way round).</summary>
        public static bool SameReal(VisitRecord last, FurnitureAnchor real)
        {
            if (!last.hasReal || real == null) return !last.hasReal && real == null;
            float w = real.Box.size.x, d = real.Box.size.z;
            bool straight = Math.Abs(w - last.realW) <= SameSize && Math.Abs(d - last.realD) <= SameSize;
            bool turned = Math.Abs(w - last.realD) <= SameSize && Math.Abs(d - last.realW) <= SameSize;
            return straight || turned;
        }

        static FreePlacement Moved(FreePlacement p, RoomPose was, RoomPose now)
        {
            p.position = Move(p.position, was, now);
            p.yawDegrees = MoveYaw(p.yawDegrees, was, now);
            return p;
        }

        // World under last visit's pose → the saved room's own frame → world under this visit's pose.
        static Vec2 Move(Vec2 p, RoomPose was, RoomPose now) =>
            now.Apply(FloorGeometry.Rotate(new Vec2(p.x - was.Translation.x, p.y - was.Translation.z), -was.YawDegrees));

        static Vec3 Move(Vec3 p, RoomPose was, RoomPose now)
        {
            Vec2 f = Move(new Vec2(p.x, p.z), was, now);
            return new Vec3(f.x, p.y - was.Translation.y + now.Translation.y, f.y);
        }

        static float MoveYaw(float yaw, RoomPose was, RoomPose now) => now.ApplyYaw(yaw - was.YawDegrees);

        static bool TryLoadRecord(string path, out VisitRecord record)
        {
            record = null;
            try
            {
                if (!File.Exists(path)) return false;
                record = JsonUtility.FromJson<VisitRecord>(File.ReadAllText(path));
                return record != null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning($"{LogTag} Visit record unreadable: {e.Message}");
                return false;
            }
        }

        static void SaveRecord(string path, VisitRecord record)
        {
            try { File.WriteAllText(path, JsonUtility.ToJson(record)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"{LogTag} Visit record not saved: {e.Message}");
            }
        }

        // A square on the floor around the user, `half` each way, turned to face `forward`.
        static void Square(List<Vec2> zone, Vector3 head, Vector3 forward, float half)
        {
            Vector3 f = forward * half, r = Vector3.Cross(Vector3.up, forward) * half;
            zone.Add(new Vec2(head.x - r.x - f.x, head.z - r.z - f.z)); zone.Add(new Vec2(head.x + r.x - f.x, head.z + r.z - f.z));
            zone.Add(new Vec2(head.x + r.x + f.x, head.z + r.z + f.z)); zone.Add(new Vec2(head.x - r.x + f.x, head.z - r.z + f.z));
        }
    }
}

using System;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    public enum PlacementStatus
    {
        Ok,
        UnknownItem,
        NotForOutside,
        InvalidPose,
        OutsideRoom,
        InMyZone,
        BlocksDoor,
        BlocksWindow,
        Overlaps,
        /// <summary>DesignSession only: another piece already has this placement id.</summary>
        DuplicateId,
        /// <summary>DesignSession only: no piece has this placement id.</summary>
        UnknownPlacement,
        /// <summary>Overlaps a real piece of furniture outside my zone (from the room scan).</summary>
        OverlapsFurniture,
        /// <summary>A wall piece (see <see cref="PlacementRules.IsWallMounted"/>) not flush on a room wall, or hung outside 0.9–2.0 m.</summary>
        NotOnWall,
    }

    /// <summary>
    /// Free placement in the outside zone (CONCEPT 5.3). A piece of any role may go anywhere that:
    /// its catalog entry allows "outside"; its footprint stays on the room floor (with a scan) and
    /// out of my zone; it keeps <see cref="DoorClearance"/> on both sides of every door and, if it
    /// is taller than the window sill, <see cref="WindowClearance"/> in front of every window; and it
    /// does not overlap another placed piece or real furniture outside my zone. Touching is fine.
    /// Overlap is checked in height too, so a hanging piece (see <see cref="MountY"/>) may hang over a
    /// desk. Flat pieces (rugs, at most <see cref="FlatHeight"/> high) may lie in doorways and under other pieces.
    /// Allocation-free after the first call on a thread, so it can run every frame for a drag preview,
    /// and safe on any thread (the assistant checks plans off the main thread).
    /// </summary>
    public static class PlacementRules
    {
        public const float DoorClearance = 0.9f;
        public const float WindowClearance = 0.5f;
        public const float FlatHeight = 0.03f;
        /// <summary>A hanging piece's lowest point stays at least this high above the floor (head room).</summary>
        public const float HangClearance = 2.0f;
        /// <summary>Ceiling height used when the room has no scan.</summary>
        public const float DefaultCeiling = 2.5f;

        /// <summary>Hangs from the ceiling: a pendant lamp, or any item tagged "ceiling".</summary>
        public static bool IsHanging(CatalogItem item) =>
            item != null && (item.kind == "pendant-lamp" || (item.tags != null && item.tags.Contains("ceiling")));

        /// <summary>Wall pieces hang with their centre this high above the floor (meters)...</summary>
        public const float MinWallCenter = 0.9f, MaxWallCenter = 2.0f;
        /// <summary>...and here when no height is asked for.</summary>
        public const float DefaultWallCenter = 1.5f;
        const float FlushTolerance = 0.02f;

        /// <summary>Hangs on a wall: wall art, a mirror or a clock, or any item tagged "wall".</summary>
        public static bool IsWallMounted(CatalogItem item) =>
            item != null && (item.kind == "wall-art" || item.kind == "mirror" || item.kind == "clock" || (item.tags != null && item.tags.Contains("wall")));

        /// <summary>
        /// Puts a wall piece flush on the room wall nearest <paramref name="center"/> (a wall hit, or where the
        /// piece's centre should go): on the wall segment, kept clear of its ends, facing into the room, its
        /// centre at the asked height clamped to <see cref="MinWallCenter"/>..<see cref="MaxWallCenter"/>.
        /// <paramref name="position"/> is the pivot (bottom centre), as for every placement. False with no
        /// scanned walls or when no wall is wide enough. Allocation-free, so it can run every frame for a preview.
        /// </summary>
        public static bool TrySnapToWall(IRoomInfo room, CatalogItem item, Vec3 center, out Vec3 position, out float yawDegrees)
        {
            position = center;
            yawDegrees = 0f;
            if (room == null || item == null || room.FloorOutline.Count < 3) return false;
            var outline = room.FloorOutline;
            float best = float.MaxValue, half = item.size.x / 2f;
            bool found = false;
            for (int i = 0; i < outline.Count; i++)
            {
                if (!Wall(outline, i, out Vec2 a, out Vec2 n, out Vec2 e, out float len) || len < item.size.x) continue;
                float t = Math.Max(half, Math.Min(len - half, (center.x - a.x) * e.x + (center.z - a.y) * e.y));
                float px = a.x + e.x * t, pz = a.y + e.y * t;
                float d = (center.x - px) * (center.x - px) + (center.z - pz) * (center.z - pz);
                if (d >= best) continue;
                best = d;
                found = true;
                float off = item.size.z / 2f;
                float h = Math.Max(MinWallCenter, Math.Min(MaxWallCenter, center.y - room.FloorY));
                position = new Vec3(px + n.x * off, room.FloorY + h - item.size.y / 2f, pz + n.y * off);
                yawDegrees = ((float)(Math.Atan2(n.x, n.y) * 180.0 / Math.PI) + 360f) % 360f;
            }
            return found;
        }

        // Outline edge i as a wall: its start, inward normal, direction and length.
        static bool Wall(System.Collections.Generic.IReadOnlyList<Vec2> outline, int i, out Vec2 a, out Vec2 n, out Vec2 e, out float len)
        {
            a = outline[i];
            Vec2 b = outline[(i + 1) % outline.Count];
            len = (float)Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            e = n = default;
            if (len < 1e-3f) return false;
            e = new Vec2((b.x - a.x) / len, (b.y - a.y) / len);
            n = new Vec2(-e.y, e.x);
            if (!Zones.Contains(outline, new Vec2((a.x + b.x) / 2f + n.x * 0.05f, (a.y + b.y) / 2f + n.y * 0.05f))) n = new Vec2(e.y, -e.x);
            return true;
        }

        // Flush on a wall (its back on a wall segment, facing into the room) at an allowed height.
        static bool OnWall(IRoomInfo room, CatalogItem item, Vec3 pos, float yawDegrees)
        {
            if (room == null || room.FloorOutline.Count < 3) return false;
            float h = pos.y - room.FloorY + item.size.y / 2f;
            if (h < MinWallCenter - FlushTolerance || h > MaxWallCenter + FlushTolerance) return false;
            var f = FloorGeometry.Rotate(new Vec2(0f, 1f), yawDegrees);
            float bx = pos.x - f.x * item.size.z / 2f, bz = pos.z - f.y * item.size.z / 2f;
            var outline = room.FloorOutline;
            for (int i = 0; i < outline.Count; i++)
            {
                if (!Wall(outline, i, out Vec2 a, out Vec2 n, out Vec2 e, out float len)) continue;
                float t = (bx - a.x) * e.x + (bz - a.y) * e.y;
                float off = (bx - a.x) * n.x + (bz - a.y) * n.y;
                if (t >= -FlushTolerance && t <= len + FlushTolerance && Math.Abs(off) <= FlushTolerance && f.x * n.x + f.y * n.y > 0.98f) return true;
            }
            return false;
        }

        /// <summary>
        /// The height of a piece's pivot (its bottom): the floor, or for a hanging piece the ceiling minus
        /// its height, but never lower than <see cref="HangClearance"/> above the floor (a long cord then
        /// reaches past the ceiling) and never higher than 0.3 m under the ceiling.
        /// </summary>
        public static float MountY(IRoomInfo room, CatalogItem item)
        {
            float floor = room?.FloorY ?? 0f;
            if (IsWallMounted(item)) return floor + DefaultWallCenter - item.size.y / 2f;
            if (!IsHanging(item)) return floor;
            float ceiling = room != null && room.CeilingY > floor + 1f ? room.CeilingY : floor + DefaultCeiling;
            return Math.Max(ceiling - item.size.y, Math.Min(floor + HangClearance, ceiling - 0.3f));
        }

        // Per-thread scratch buffers so Check never allocates after a thread's first call. The first
        // holds the footprint being checked for the whole call; the second is overwritten by each door,
        // window (in BlocksAny) and placed piece in turn and is only read right after it is written.
        // Per thread because the assistant checks plans on a worker thread while the main thread
        // checks the drag preview; shared buffers corrupted each other's footprints. Not reentrant.
        [ThreadStatic] static Vec2[] _candidate, _other;

        /// <param name="ignorePlacementId">The piece being moved, so it does not collide with itself.</param>
        public static PlacementStatus Check(IRoomInfo room, DesignState state, ICatalog catalog,
            FreePlacement placement, string ignorePlacementId = null)
        {
            if (catalog == null || !catalog.TryGet(placement.catalogItemId, out var item)) return PlacementStatus.UnknownItem;
            if (!item.AllowedIn("outside")) return PlacementStatus.NotForOutside;
            Vec3 pos = placement.position;
            if (!Finite(pos.x) || !Finite(pos.y) || !Finite(pos.z) || !Finite(placement.yawDegrees))
                return PlacementStatus.InvalidPose;

            if (IsWallMounted(item) && !OnWall(room, item, pos, placement.yawDegrees)) return PlacementStatus.NotOnWall;
            var footprint = _candidate ?? (_candidate = new Vec2[4]);
            var scratch = _other ?? (_other = new Vec2[4]);
            FloorGeometry.Footprint(pos.Floor(), placement.yawDegrees, item.size.x, item.size.z, footprint);
            if (room != null && room.FloorOutline.Count >= 3 && !FloorGeometry.InsideArea(room.FloorOutline, footprint))
                return PlacementStatus.OutsideRoom;
            if (state != null && FloorGeometry.OverlapsArea(state.zoneOutline, footprint))
                return PlacementStatus.InMyZone;

            bool flat = item.size.y <= FlatHeight;
            if (room != null)
            {
                if (!flat && BlocksAny(room.Doors, DoorClearance, float.PositiveInfinity, footprint, scratch)) return PlacementStatus.BlocksDoor;
                if (BlocksAny(room.Windows, WindowClearance, pos.y + item.size.y, footprint, scratch)) return PlacementStatus.BlocksWindow;
            }

            if (flat) return PlacementStatus.Ok;
            float top = pos.y + item.size.y;
            if (room != null)
            {
                var furniture = room.Furniture;
                for (int i = 0; i < furniture.Count; i++)
                {
                    var a = furniture[i];
                    // Only the real pieces in my zone are seen (kept or redesigned), so only they block, e.g. a desk
                    // that juts past the zone line. The virtual room covers everything outside my zone: real
                    // furniture there is hidden and its floor is free (device: Add refused a bed almost everywhere).
                    if (a == null || state == null || !Zones.Contains(state.zoneOutline, a)) continue;
                    var box = a.Box;
                    if (top <= box.center.y - box.size.y / 2f || pos.y >= box.center.y + box.size.y / 2f) continue;
                    FloorGeometry.Footprint(box.center.Floor(), box.yawDegrees, box.size.x, box.size.z, scratch);
                    if (FloorGeometry.RectsOverlap(footprint, scratch)) return PlacementStatus.OverlapsFurniture;
                }
            }
            if (state == null) return PlacementStatus.Ok;
            var placements = state.placements;
            for (int i = 0; i < placements.Count; i++)
            {
                var other = placements[i];
                if (other.placementId == placement.placementId || other.placementId == ignorePlacementId) continue;
                if (!catalog.TryGet(other.catalogItemId, out var otherItem) || otherItem.size.y <= FlatHeight) continue;
                if (top <= other.position.y || pos.y >= other.position.y + otherItem.size.y) continue; // one above the other
                FloorGeometry.Footprint(other.position.Floor(), other.yawDegrees, otherItem.size.x, otherItem.size.z, scratch);
                if (FloorGeometry.RectsOverlap(footprint, scratch)) return PlacementStatus.Overlaps;
            }
            return PlacementStatus.Ok;
        }

        /// <summary>How far <see cref="TryNudgeIntoRoom"/> may move a piece (meters).</summary>
        public const float MaxWallNudge = 0.5f;
        const float NudgeStep = 0.05f;
        static readonly Vec2[] NudgeOffsets = BuildNudgeOffsets();

        static Vec2[] BuildNudgeOffsets()
        {
            var list = new System.Collections.Generic.List<Vec2>();
            int n = (int)Math.Round(MaxWallNudge / NudgeStep);
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                    if ((i != 0 || j != 0) && i * i + j * j <= n * n) list.Add(new Vec2(i * NudgeStep, j * NudgeStep));
            list.Sort((a, b) =>
            {
                int c = (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y);
                if (c == 0) c = a.x.CompareTo(b.x);
                return c != 0 ? c : a.y.CompareTo(b.y);
            });
            return list.ToArray();
        }

        /// <summary>
        /// Moves a piece that pokes through a wall to the nearest spot (same turn, at most <see cref="MaxWallNudge"/>
        /// away) where every rule passes. False, and the piece unchanged, when there is none. Call on release.
        /// </summary>
        public static bool TryNudgeIntoRoom(IRoomInfo room, DesignState state, ICatalog catalog, ref FreePlacement placement,
            string ignorePlacementId = null)
        {
            var candidate = placement;
            foreach (var o in NudgeOffsets)
            {
                candidate.position = new Vec3(placement.position.x + o.x, placement.position.y, placement.position.z + o.y);
                if (Check(room, state, catalog, candidate, ignorePlacementId) != PlacementStatus.Ok) continue;
                placement = candidate;
                return true;
            }
            return false;
        }

        // Writes each opening's clearance footprint into the scratch buffer (see above).
        // An opening blocks when the item's top is above its bottom edge and the footprints overlap
        // the opening widened by the clearance on both sides of its plane.
        static bool BlocksAny(System.Collections.Generic.IReadOnlyList<RoomElement> openings, float clearance, float itemTop,
            Vec2[] footprint, Vec2[] scratch)
        {
            for (int i = 0; i < openings.Count; i++)
            {
                var box = openings[i].Box;
                if (itemTop <= box.center.y - box.size.y / 2f) continue;
                FloorGeometry.Footprint(box.center.Floor(), box.yawDegrees, box.size.x, box.size.z + 2f * clearance, scratch);
                if (FloorGeometry.RectsOverlap(footprint, scratch)) return true;
            }
            return false;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}

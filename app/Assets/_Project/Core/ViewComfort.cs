using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Keeps the seated user's view open when the assistant places pieces: nothing taller than
    /// <see cref="TallHeight"/> within <see cref="HeadClearance"/> of the head, nor inside a ±<see cref="ConeHalfAngle"/>°
    /// cone out to <see cref="ConeReach"/> in front of the piece the user is at (the desk or bed in my zone).
    /// Hanging and wall pieces are above eye level and never count. Device bug: a floor lamp landed at arm's
    /// length right in front of the seated user.
    /// </summary>
    public sealed class ViewComfort
    {
        public const float TallHeight = 0.9f, HeadClearance = 1.0f, ConeHalfAngle = 35f, ConeReach = 1.5f;
        /// <summary>With no user position, the seat is this far in front of the desk's front edge.</summary>
        public const float SeatOffset = 0.4f;

        ViewComfort(Vec2 head, Vec2 apex, Vec2 facing) { Head = head; Apex = apex; Facing = facing; }

        public Vec2 Head { get; }
        public Vec2 Apex { get; }
        /// <summary>Unit floor direction the cone opens toward: the anchor's facing (local +z).</summary>
        public Vec2 Facing { get; }

        /// <summary>
        /// From the user's head when known (and the desk or bed they are at), else from the desk or bed in my zone
        /// (a desk's seat in front of it; a bed's centre). Null with no such piece and no user position.
        /// </summary>
        public static ViewComfort For(IRoomInfo room, IReadOnlyList<Vec2> zone, Vec3? userPosition)
        {
            FurnitureAnchor anchor = userPosition.HasValue ? ZoneProposer.PieceAt(room, userPosition.Value) : InZone(room, zone);
            if (anchor == null)
                return userPosition.HasValue ? new ViewComfort(userPosition.Value.Floor(), userPosition.Value.Floor(), new Vec2(0f, 0f)) : null;
            var box = anchor.Box;
            var facing = FloorGeometry.Rotate(new Vec2(0f, 1f), box.yawDegrees);
            var apex = box.center.Floor();
            Vec2 head = userPosition.HasValue ? userPosition.Value.Floor()
                : anchor.Role == FurnitureRole.Table ? new Vec2(apex.x + facing.x * (box.size.z / 2f + SeatOffset), apex.y + facing.y * (box.size.z / 2f + SeatOffset))
                : apex;
            return new ViewComfort(head, apex, facing);
        }

        static FurnitureAnchor InZone(IRoomInfo room, IReadOnlyList<Vec2> zone)
        {
            if (room == null || zone == null || zone.Count < 3) return null;
            FurnitureAnchor best = null;
            foreach (var a in room.Furniture)
            {
                if ((a.Role != FurnitureRole.Table && a.Role != FurnitureRole.Bed) || !Zones.Contains(zone, a)) continue;
                if (best == null || (a.Role == FurnitureRole.Table && best.Role != FurnitureRole.Table)) best = a;
            }
            return best;
        }

        /// <summary>Whether a piece at this pose would block the view. Short, hanging and wall pieces never do.</summary>
        public bool Blocks(CatalogItem item, Vec3 position, float yawDegrees)
        {
            if (item == null || item.size.y <= TallHeight || PlacementRules.IsHanging(item) || PlacementRules.IsWallMounted(item)) return false;
            var c = position.Floor();
            var e = FloorGeometry.Rotate(new Vec2(1f, 0f), yawDegrees);
            var f = FloorGeometry.Rotate(new Vec2(0f, 1f), yawDegrees);
            float hx = item.size.x / 2f, hz = item.size.z / 2f;
            // The footprint's centre, corners and edge midpoints.
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var p = new Vec2(c.x + e.x * hx * i + f.x * hz * j, c.y + e.y * hx * i + f.y * hz * j);
                    if (Dist(p, Head) < HeadClearance || InCone(p)) return true;
                }
            return FootprintDistance(c, e, f, hx, hz, Head) < HeadClearance;
        }

        bool InCone(Vec2 p)
        {
            if (Facing.x == 0f && Facing.y == 0f) return false;
            float dx = p.x - Apex.x, dz = p.y - Apex.y, d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d > ConeReach || d < 1e-4f) return d < 1e-4f;
            return (dx * Facing.x + dz * Facing.y) / d >= (float)Math.Cos(ConeHalfAngle * Math.PI / 180.0);
        }

        static float FootprintDistance(Vec2 c, Vec2 e, Vec2 f, float hx, float hz, Vec2 p)
        {
            float lx = (p.x - c.x) * e.x + (p.y - c.y) * e.y, lz = (p.x - c.x) * f.x + (p.y - c.y) * f.y;
            float dx = Math.Max(0f, Math.Abs(lx) - hx), dz = Math.Max(0f, Math.Abs(lz) - hz);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static float Dist(Vec2 a, Vec2 b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }
}

using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Simple room-layout rules so an assistant plan reads as one set: seats around the table, the rug
    /// under it, the pendant above it, shelves against a wall and plants in a corner. The plan checker
    /// keeps the model's spot when it already follows the rule, otherwise tries these spots first, and
    /// falls back to its usual nudge and grid layout. Pieces without a rule, or whose rule needs a table
    /// when there is none, are laid out as before. Runs once per ask, off the main thread.
    /// </summary>
    static class SetLayout
    {
        public enum Role { None, Table, Seat, Rug, Pendant, Shelf, Plant, Wall }

        /// <summary>Space between a table and the seats around it, in meters.</summary>
        public const float SeatGap = 0.35f;
        const float WallTolerance = 0.06f, CornerSlack = 0.2f;

        public static Role RoleOf(CatalogItem item)
        {
            if (item == null) return Role.None;
            if (PlacementRules.IsWallMounted(item)) return Role.Wall;
            if (PlacementRules.IsHanging(item)) return Role.Pendant;
            if (item.size.y <= PlacementRules.FlatHeight || item.kind == "rug") return Role.Rug;
            if (item.kind == "plant") return Role.Plant;
            if (item.kind == "shelf" || item.role == FurnitureRole.Storage) return Role.Shelf;
            if (item.role == FurnitureRole.Table) return Role.Table;
            if (item.role == FurnitureRole.Couch || item.kind == "chair" || item.kind == "stool" || item.kind == "bench") return Role.Seat;
            return Role.None;
        }

        /// <summary>Whether a placed pose already follows its role's rule (a wall piece that passed the rules is on a wall).</summary>
        public static bool Follows(IRoomInfo room, Role role, CatalogItem item, FreePlacement p, FreePlacement? table, CatalogItem tableItem)
        {
            var at = p.position.Floor();
            switch (role)
            {
                case Role.Rug: return table.HasValue && Dist(at, table.Value.position.Floor()) <= 0.3f;
                case Role.Pendant: return table.HasValue && Dist(at, table.Value.position.Floor()) <= 0.2f;
                case Role.Seat:
                {
                    // Within a seat's depth of the table's far edge, and turned toward it.
                    if (!table.HasValue) return false;
                    var t = table.Value.position.Floor();
                    float d = Dist(at, t);
                    if (d > Math.Max(tableItem.size.x, tableItem.size.z) / 2f + SeatGap + item.size.z / 2f + 0.25f) return false;
                    var f = Facing(p.yawDegrees);
                    return d < 1e-3f || (f.x * (t.x - at.x) + f.y * (t.y - at.y)) / d > 0.7f;
                }
                case Role.Shelf:
                {
                    var facing = Facing(p.yawDegrees);
                    var back = new Vec2(at.x - facing.x * item.size.z / 2f, at.y - facing.y * item.size.z / 2f);
                    foreach (var (a, b, n) in Walls(room))
                        if (SegmentDist(back, a, b) <= WallTolerance && facing.x * n.x + facing.y * n.y > 0.9f) return true;
                    return false;
                }
                case Role.Plant:
                {
                    float reach = (float)Math.Sqrt(item.size.x * item.size.x + item.size.z * item.size.z) / 2f + CornerSlack;
                    foreach (var v in room.FloorOutline) if (Dist(at, v) <= reach) return true;
                    return false;
                }
                default: return true;
            }
        }

        /// <summary>Spots (floor position and yaw) that follow the rule, best first; null when the role has no rule here.</summary>
        public static List<(Vec2 at, float yaw)> Spots(IRoomInfo room, Role role, CatalogItem item, FreePlacement? table, CatalogItem tableItem, Vec2? near)
        {
            var spots = new List<(Vec2 at, float yaw)>();
            Vec2? t = table.HasValue ? table.Value.position.Floor() : (Vec2?)null;
            float ty = table.HasValue ? table.Value.yawDegrees : 0f;
            switch (role)
            {
                case Role.Rug:
                    if (t == null) return null;
                    spots.Add((t.Value, ty));
                    spots.Add((t.Value, (ty + 90f) % 360f));
                    return spots;
                case Role.Pendant:
                    if (t == null) return null;
                    spots.Add((t.Value, ty));
                    return spots;
                case Role.Seat:
                    if (t == null) return null;
                    // Front and back of the table first, then its ends; each seat faces the table.
                    var ez = Facing(ty); var ex = new Vec2(ez.y, -ez.x);
                    AddSeat(spots, t.Value, ez, tableItem.size.z / 2f + SeatGap + item.size.z / 2f);
                    AddSeat(spots, t.Value, new Vec2(-ez.x, -ez.y), tableItem.size.z / 2f + SeatGap + item.size.z / 2f);
                    AddSeat(spots, t.Value, ex, tableItem.size.x / 2f + SeatGap + item.size.z / 2f);
                    AddSeat(spots, t.Value, new Vec2(-ex.x, -ex.y), tableItem.size.x / 2f + SeatGap + item.size.z / 2f);
                    return spots;
                case Role.Shelf:
                case Role.Wall: // shelves stand against a wall; wall pieces hang flush on one
                    foreach (var (a, b, n) in Walls(room))
                    {
                        float len = Dist(a, b), half = item.size.x / 2f + 0.01f;
                        var e = new Vec2((b.x - a.x) / len, (b.y - a.y) / len);
                        float yaw = Yaw(n), off = item.size.z / 2f + (role == Role.Shelf ? 0.005f : 0f);
                        for (float s = half; s <= len - half + 1e-4f; s += PlanChecker.GridStep)
                            spots.Add((new Vec2(a.x + e.x * s + n.x * off, a.y + e.y * s + n.y * off), yaw));
                    }
                    break;
                case Role.Plant:
                    var outline = room.FloorOutline;
                    for (int i = 0; outline.Count >= 3 && i < outline.Count; i++)
                    {
                        Vec2 v = outline[i], prev = outline[(i + outline.Count - 1) % outline.Count], next = outline[(i + 1) % outline.Count];
                        var u1 = Unit(prev, v); var u2 = Unit(next, v);
                        float hx = item.size.x / 2f + 0.01f, hz = item.size.z / 2f + 0.01f;
                        spots.Add((new Vec2(v.x + u1.x * hx + u2.x * hz, v.y + u1.y * hx + u2.y * hz), Yaw(u2)));
                    }
                    break;
                default: return null;
            }
            // Wall and corner spots nearest the asked spot, else nearest the table.
            var refPoint = near ?? t;
            if (refPoint.HasValue) spots.Sort((p, q) => Dist(p.at, refPoint.Value).CompareTo(Dist(q.at, refPoint.Value)));
            return spots;
        }

        static void AddSeat(List<(Vec2, float)> spots, Vec2 table, Vec2 dir, float distance)
        {
            var at = new Vec2(table.x + dir.x * distance, table.y + dir.y * distance);
            spots.Add((at, Yaw(new Vec2(-dir.x, -dir.y)))); // facing the table
        }

        // Each floor-outline edge with its inward normal.
        static IEnumerable<(Vec2 a, Vec2 b, Vec2 n)> Walls(IRoomInfo room)
        {
            var outline = room.FloorOutline;
            if (outline.Count < 3) yield break;
            for (int i = 0; i < outline.Count; i++)
            {
                Vec2 a = outline[i], b = outline[(i + 1) % outline.Count];
                float len = Dist(a, b);
                if (len < 1e-3f) continue;
                var n = new Vec2(-(b.y - a.y) / len, (b.x - a.x) / len);
                var probe = new Vec2((a.x + b.x) / 2f + n.x * 0.05f, (a.y + b.y) / 2f + n.y * 0.05f);
                if (!Zones.Contains(outline, probe)) n = new Vec2(-n.x, -n.y);
                yield return (a, b, n);
            }
        }

        // The floor direction a yaw faces (local +z); Yaw is its inverse.
        static Vec2 Facing(float yawDegrees) => FloorGeometry.Rotate(new Vec2(0f, 1f), yawDegrees);
        static float Yaw(Vec2 dir) => ((float)(Math.Atan2(dir.x, dir.y) * 180.0 / Math.PI) + 360f) % 360f;

        static Vec2 Unit(Vec2 to, Vec2 from)
        {
            float d = Math.Max(Dist(to, from), 1e-6f);
            return new Vec2((to.x - from.x) / d, (to.y - from.y) / d);
        }

        static float Dist(Vec2 a, Vec2 b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        static float SegmentDist(Vec2 p, Vec2 a, Vec2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y, len2 = dx * dx + dy * dy;
            float t = len2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2));
            return Dist(p, new Vec2(a.x + t * dx, a.y + t * dy));
        }
    }
}

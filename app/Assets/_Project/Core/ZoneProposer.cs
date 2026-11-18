using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Proposes "my side" of the room as a first zone the user then adjusts by hand.
    /// Rule: the piece the user is at (<see cref="PieceAt"/>: the nearer of their nearest desk and nearest
    /// bed) and the nearest piece of the other kind (if it is within <see cref="MaxDeskToBed"/> of the first),
    /// bound their footprints in the first piece's own frame,
    /// add <see cref="Clearance"/> for the chair and walking space, and clip to the room's bounds.
    /// With no desk and no bed (unknown room, no scan) it falls back to a square around the user.
    /// The polygon is counter-clockwise on the floor plane (x, z).
    /// Allocates the returned list: call it when the room loads or changes, never per frame.
    /// </summary>
    public static class ZoneProposer
    {
        public const float Clearance = 0.6f;
        public const float MaxDeskToBed = 3.5f;
        public const float FallbackHalfSize = 1.5f;

        public static List<Vec2> Propose(IRoomInfo room, Vec3 userPosition)
        {
            Vec2 user = userPosition.Floor();
            var main = PieceAt(room, userPosition);
            var other = main == null ? null : Nearest(room, main.Role == FurnitureRole.Bed ? FurnitureRole.Table : FurnitureRole.Bed, user);
            if (other != null && Distance(main.Box.center.Floor(), other.Box.center.Floor()) > MaxDeskToBed)
                other = null;

            float yaw = main?.Box.yawDegrees ?? 0f;
            var r = Rect.Empty;
            if (main == null)
            {
                Vec2 u = Rotate(user, -yaw);
                r = r.Add(new Vec2(u.x - FallbackHalfSize, u.y - FallbackHalfSize))
                     .Add(new Vec2(u.x + FallbackHalfSize, u.y + FallbackHalfSize));
            }
            else
            {
                r = AddBox(r, main, yaw);
                r = AddBox(r, other, yaw);
                r = r.Grow(Clearance);
            }

            if (room != null && room.FloorOutline.Count >= 3)
            {
                var bounds = Rect.Empty;
                foreach (var p in room.FloorOutline) bounds = bounds.Add(Rotate(p, -yaw));
                var clipped = r.Intersect(bounds);
                if (!clipped.IsEmpty) r = clipped;
            }

            return new List<Vec2>
            {
                Rotate(new Vec2(r.MinX, r.MinZ), yaw),
                Rotate(new Vec2(r.MaxX, r.MinZ), yaw),
                Rotate(new Vec2(r.MaxX, r.MaxZ), yaw),
                Rotate(new Vec2(r.MinX, r.MaxZ), yaw),
            };
        }

        /// <summary>
        /// The piece the user is at: of the desk (Table) nearest them and the bed nearest them, the nearer one.
        /// Null with neither. Used for the first zone and as a saved space's anchor.
        /// </summary>
        public static FurnitureAnchor PieceAt(IRoomInfo room, Vec3 userPosition)
        {
            Vec2 user = userPosition.Floor();
            var desk = Nearest(room, FurnitureRole.Table, user);
            var bed = Nearest(room, FurnitureRole.Bed, user);
            if (desk == null || bed == null) return desk ?? bed;
            return FootprintDistance(bed, user) < FootprintDistance(desk, user) ? bed : desk;
        }

        // Distance from the user to a piece's footprint: 0 on it (lying on the bed), else to its nearest edge.
        static float FootprintDistance(FurnitureAnchor a, Vec2 user)
        {
            var local = Rotate(new Vec2(user.x - a.Box.center.x, user.y - a.Box.center.z), -a.Box.yawDegrees);
            float dx = Math.Max(0f, Math.Abs(local.x) - a.Box.size.x / 2f), dz = Math.Max(0f, Math.Abs(local.y) - a.Box.size.z / 2f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static FurnitureAnchor Nearest(IRoomInfo room, FurnitureRole role, Vec2 user)
        {
            if (room == null) return null;
            FurnitureAnchor best = null;
            float bestDistance = float.MaxValue;
            foreach (var anchor in room.Furniture)
            {
                if (anchor.Role != role) continue;
                float d = Distance(anchor.Box.center.Floor(), user);
                if (d < bestDistance) { best = anchor; bestDistance = d; }
            }
            return best;
        }

        static Rect AddBox(Rect r, FurnitureAnchor anchor, float frameYaw)
        {
            if (anchor == null) return r;
            var box = anchor.Box;
            Vec2 c = box.center.Floor();
            float hx = box.size.x / 2f, hz = box.size.z / 2f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vec2 corner = Rotate(new Vec2(sx * hx, sz * hz), box.yawDegrees);
                r = r.Add(Rotate(new Vec2(c.x + corner.x, c.y + corner.y), -frameYaw));
            }
            return r;
        }

        /// <summary>Unity's rotation around +Y applied to a floor point (x, z).</summary>
        static Vec2 Rotate(Vec2 p, float yawDegrees)
        {
            double a = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            return new Vec2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }

        static float Distance(Vec2 a, Vec2 b) =>
            (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        readonly struct Rect
        {
            public readonly float MinX, MinZ, MaxX, MaxZ;

            Rect(float minX, float minZ, float maxX, float maxZ)
            {
                MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
            }

            public static Rect Empty => new Rect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            public bool IsEmpty => MinX >= MaxX || MinZ >= MaxZ;

            public Rect Add(Vec2 p) =>
                new Rect(Math.Min(MinX, p.x), Math.Min(MinZ, p.y), Math.Max(MaxX, p.x), Math.Max(MaxZ, p.y));

            public Rect Grow(float d) => new Rect(MinX - d, MinZ - d, MaxX + d, MaxZ + d);

            public Rect Intersect(Rect o) =>
                new Rect(Math.Max(MinX, o.MinX), Math.Max(MinZ, o.MinZ), Math.Min(MaxX, o.MaxX), Math.Min(MaxZ, o.MaxZ));
        }
    }
}

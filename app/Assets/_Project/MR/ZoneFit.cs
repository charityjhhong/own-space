using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR
{
    /// <summary>
    /// Makes a proposed zone hold the user's real work furniture (device batch 5: the desk ended up half outside the
    /// zone and the AI placed a lamp into it). The desk (Table) nearest the user, and its chair (the nearest "Other"
    /// piece within <see cref="ChairReach"/> of it: MRUK has no chair role), must lie inside the zone with
    /// <see cref="Margin"/> to spare. The zone grows as a rectangle in the desk's own frame, stays inside the room's
    /// bounds, and its corners are pulled into the room outline unless that would cut the desk again.
    /// Pure, for tests; allocates only the work lists it is given.
    /// </summary>
    public static class ZoneFit
    {
        public const float Margin = 0.3f, ChairReach = 1.5f;

        /// <summary>A proposal leaves at least this share of the room's floor outside the zone, when the room allows.</summary>
        public const float MinOutsideShare = 0.35f;

        /// <summary>Below this much free outside floor (m²) there is no room to add a piece.</summary>
        public const float MinOutsideArea = 1f;

        /// <summary>
        /// Keeps room for the outside zone (device day: after Reset the proposal covered nearly the whole study, so Add
        /// could not place anything): when the zone covers more than 1 - <see cref="MinOutsideShare"/> of the room, it
        /// becomes just the desk and chair with <see cref="Margin"/>; with no desk, it shrinks about its centre. A room too
        /// small for both keeps the desk-and-chair zone. True when the zone changed.
        /// </summary>
        public static bool KeepOutside(List<Vec2> zone, IReadOnlyList<FurnitureAnchor> furniture, Vec2 user, IReadOnlyList<Vec2> room)
        {
            if (zone == null || zone.Count < 3 || room == null || room.Count < 3) return false;
            float roomArea = Math.Abs(RoomGeometry.SignedArea(room)), max = roomArea * (1f - MinOutsideShare);
            if (Math.Abs(RoomGeometry.SignedArea(zone)) <= max) return false;
            if (AtPiece(furniture, user) != null)
            {
                zone.Clear();
                Contain(zone, furniture, user, room); // the work furniture alone, with the margin
                return true;
            }
            float area = Math.Abs(RoomGeometry.SignedArea(zone)), k = (float)Math.Sqrt(max / area);
            float cx = 0f, cz = 0f;
            for (int i = 0; i < zone.Count; i++) { cx += zone[i].x; cz += zone[i].y; }
            cx /= zone.Count; cz /= zone.Count;
            for (int i = 0; i < zone.Count; i++) zone[i] = new Vec2(cx + (zone[i].x - cx) * k, cz + (zone[i].y - cz) * k);
            return true;
        }

        /// <summary>
        /// Free floor outside the zone (m², pure, for tests): the room less the zone, less the footprints of the
        /// furniture standing outside it. Allocation-free.
        /// </summary>
        public static float OutsideFreeArea(IReadOnlyList<Vec2> room, IReadOnlyList<Vec2> zone, IReadOnlyList<FurnitureAnchor> furniture)
        {
            if (room == null || room.Count < 3) return 0f;
            float free = Math.Abs(RoomGeometry.SignedArea(room)) - (zone != null && zone.Count >= 3 ? Math.Abs(RoomGeometry.SignedArea(zone)) : 0f);
            for (int i = 0; furniture != null && i < furniture.Count; i++)
            {
                Vec2 c = furniture[i].Box.center.Floor();
                if (zone != null && zone.Count >= 3 && Zones.Contains(zone, c)) continue;
                free -= furniture[i].Box.size.x * furniture[i].Box.size.z;
            }
            return Math.Max(0f, free);
        }

        /// <summary>The desk nearest <paramref name="user"/>, or null.</summary>
        public static FurnitureAnchor Desk(IReadOnlyList<FurnitureAnchor> furniture, Vec2 user) => Nearest(furniture, FurnitureRole.Table, user, float.MaxValue);

        /// <summary>
        /// The piece the user is at (product rule: people use the app at the desk or on the bed). It is the nearest Table or
        /// Bed by floor distance to its footprint (0 when the head is over it); on a tie the desk wins. Null when there is neither.
        /// </summary>
        public static FurnitureAnchor AtPiece(IReadOnlyList<FurnitureAnchor> furniture, Vec2 user)
        {
            FurnitureAnchor best = null;
            float bestD = float.MaxValue;
            for (int i = 0; furniture != null && i < furniture.Count; i++)
            {
                FurnitureAnchor a = furniture[i];
                if (a.Role != FurnitureRole.Table && a.Role != FurnitureRole.Bed) continue;
                float d = FootprintDistance(a.Box, user) + (a.Role == FurnitureRole.Bed ? 1e-3f : 0f);
                if (d < bestD) { bestD = d; best = a; }
            }
            return best;
        }

        /// <summary>Floor distance from <paramref name="p"/> to the box's footprint; 0 inside it.</summary>
        public static float FootprintDistance(OrientedBox box, Vec2 p)
        {
            Vec2 q = Rotate(new Vec2(p.x - box.center.x, p.y - box.center.z), -box.yawDegrees);
            float dx = Math.Max(0f, Math.Abs(q.x) - box.size.x * 0.5f), dz = Math.Max(0f, Math.Abs(q.y) - box.size.z * 0.5f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>"desk" or "bed", for the zone step's words.</summary>
        public static string PieceWord(FurnitureAnchor piece) => piece != null && piece.Role == FurnitureRole.Bed ? "bed" : "desk";

        /// <summary>The desk's chair: the nearest "Other" piece within <see cref="ChairReach"/> of it, or null.</summary>
        public static FurnitureAnchor Chair(IReadOnlyList<FurnitureAnchor> furniture, FurnitureAnchor desk) =>
            desk == null ? null : Nearest(furniture, FurnitureRole.Other, desk.Box.center.Floor(), ChairReach);

        /// <summary>Grows <paramref name="zone"/> (in place) to hold the work furniture; false when there is no desk.</summary>
        public static bool Contain(List<Vec2> zone, IReadOnlyList<FurnitureAnchor> furniture, Vec2 user, IReadOnlyList<Vec2> roomOutline)
        {
            FurnitureAnchor desk = AtPiece(furniture, user); // the desk or bed the user is at
            if (desk == null || zone == null) return false;
            FurnitureAnchor chair = Chair(furniture, desk);
            float yaw = ZoneAlign.FrameYaw(desk, roomOutline, user); // the desk's direction, squared to its wall

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            void Add(Vec2 p, float pad)
            {
                Vec2 q = Rotate(p, -yaw);
                minX = Math.Min(minX, q.x - pad); minZ = Math.Min(minZ, q.y - pad);
                maxX = Math.Max(maxX, q.x + pad); maxZ = Math.Max(maxZ, q.y + pad);
            }
            for (int i = 0; i < zone.Count; i++) Add(zone[i], 0f);
            AddBox(desk, Add);
            if (chair != null) AddBox(chair, Add);

            if (roomOutline != null && roomOutline.Count >= 3)
            {
                float rx0 = float.MaxValue, rz0 = float.MaxValue, rx1 = float.MinValue, rz1 = float.MinValue;
                for (int i = 0; i < roomOutline.Count; i++)
                {
                    Vec2 q = Rotate(roomOutline[i], -yaw);
                    rx0 = Math.Min(rx0, q.x); rz0 = Math.Min(rz0, q.y); rx1 = Math.Max(rx1, q.x); rz1 = Math.Max(rz1, q.y);
                }
                minX = Math.Max(minX, rx0); minZ = Math.Max(minZ, rz0); maxX = Math.Min(maxX, rx1); maxZ = Math.Min(maxZ, rz1);
            }

            zone.Clear();
            zone.Add(Rotate(new Vec2(minX, minZ), yaw));
            zone.Add(Rotate(new Vec2(maxX, minZ), yaw));
            zone.Add(Rotate(new Vec2(maxX, maxZ), yaw));
            zone.Add(Rotate(new Vec2(minX, maxZ), yaw));

            // Corners inside the room outline (an L-shaped room) unless that would cut the desk out again.
            if (roomOutline != null && roomOutline.Count >= 3)
            {
                var clamped = new List<Vec2>(zone.Count);
                for (int i = 0; i < zone.Count; i++) clamped.Add(RoomGeometry.ClampInside(roomOutline, zone[i]));
                // ...and only while it stays a rectangle (clamping corners one by one skewed it: the diagonal zone).
                if (Holds(clamped, desk) && ZoneAlign.IsRect(clamped)) { zone.Clear(); zone.AddRange(clamped); }
            }
            return true;
        }

        /// <summary>Whether every corner of the piece's footprint is inside the zone.</summary>
        public static bool Holds(IReadOnlyList<Vec2> zone, FurnitureAnchor piece)
        {
            bool all = true;
            AddBox(piece, (p, pad) => all &= Zones.Contains(zone, p));
            return all;
        }

        static void AddBox(FurnitureAnchor piece, Action<Vec2, float> add)
        {
            OrientedBox b = piece.Box;
            Vec2 c = b.center.Floor();
            float hx = b.size.x * 0.5f, hz = b.size.z * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vec2 corner = Rotate(new Vec2(sx * hx, sz * hz), b.yawDegrees);
                add(new Vec2(c.x + corner.x, c.y + corner.y), Margin);
            }
        }

        static FurnitureAnchor Nearest(IReadOnlyList<FurnitureAnchor> furniture, FurnitureRole role, Vec2 from, float within)
        {
            FurnitureAnchor best = null;
            float bestD = within;
            for (int i = 0; furniture != null && i < furniture.Count; i++)
            {
                if (furniture[i].Role != role) continue;
                Vec2 c = furniture[i].Box.center.Floor();
                float d = (float)Math.Sqrt((c.x - from.x) * (c.x - from.x) + (c.y - from.y) * (c.y - from.y));
                if (d < bestD) { bestD = d; best = furniture[i]; }
            }
            return best;
        }

        // Unity's rotation around +Y applied to a floor point (x, z), as in core's ZoneProposer.
        static Vec2 Rotate(Vec2 p, float yawDegrees)
        {
            double a = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            return new Vec2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }
    }
}

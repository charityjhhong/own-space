using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The zone's direction (the human, build 0ddb26d: "the zone keeps coming out diagonal to the room"; a my-zone wall
    /// finish then painted only a strip). The zone is a rectangle parallel to the desk or bed the user is at, snapped to the
    /// nearest wall within <see cref="DeskToWallDegrees"/> (a scanned box is a few degrees off); with neither, parallel to
    /// the wall nearest the user. Never the head's or the world's axes. Yaws are Unity yaws of the rectangle's first side
    /// (as <see cref="ZoneSetup.RectFrame"/>). Pure, for tests.
    /// </summary>
    public static class ZoneAlign
    {
        public const float DeskToWallDegrees = 5f, MinWall = 0.5f, AgainstWall = 0.6f, DeskWallReach = 1.5f;

        /// <summary>The zone's yaw for the piece the user is at (or none) in <paramref name="room"/>.</summary>
        public static float FrameYaw(FurnitureAnchor piece, IReadOnlyList<Vec2> room, Vec2 user)
        {
            if (piece != null)
            {
                float yaw = piece.Box.yawDegrees;
                // A desk or bed against a wall is parallel to it (the human): the wall is the reliable direction; a scanned
                // box can be well off it (device, build dfb22f4: the zone stayed about 33 degrees off in the human's room).
                Vec2 c = piece.Box.center.Floor();
                if (NearestWallYaw(room, c, out float behind) && FootprintToWall(piece.Box, room) <= AgainstWall)
                    return yaw + Mod90(Mathf.DeltaAngle(yaw, behind));
                float off = NearestWallOffset(yaw, room, out bool any);
                return any && Mathf.Abs(off) <= DeskToWallDegrees ? yaw + off : yaw;
            }
            return NearestWallYaw(room, user, out float wall) ? wall : 0f;
        }

        /// <summary>The gap (m) between the box's footprint and wall segment ab (pure).</summary>
        public static float BoxToSegment(OrientedBox box, Vec2 a, Vec2 b)
        {
            Vec2 c = box.center.Floor();
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f, best = float.MaxValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vec2 r = Rotate(new Vec2(sx * hx, sz * hz), box.yawDegrees);
                    best = Mathf.Min(best, ToSegment(new Vec2(c.x + r.x, c.y + r.y), a, b));
                }
            return best;
        }

        /// <summary>
        /// How far a zone may stop short of wall ab and still own it (the my-zone wall finish): the wall the zone's desk or bed
        /// stands against reaches <see cref="DeskWallReach"/> (it is painted across the zone's width), any other wall
        /// <see cref="ShellGeometry.WallReach"/>.
        /// </summary>
        public static float WallReachFor(Vec2 a, Vec2 b, FurnitureAnchor piece) =>
            piece != null && BoxToSegment(piece.Box, a, b) <= AgainstWall ? DeskWallReach : ShellGeometry.WallReach;

        /// <summary>The gap (m) between the box's footprint and the room wall nearest its centre (pure).</summary>
        public static float FootprintToWall(OrientedBox box, IReadOnlyList<Vec2> room)
        {
            float best = float.MaxValue;
            Vec2 c = box.center.Floor();
            float bestWall = float.MaxValue;
            int wall = -1;
            for (int i = 0; room != null && i < room.Count; i++)
            {
                Vec2 a = room[i], b = room[(i + 1) % room.Count];
                if (Length(a, b) < MinWall) continue;
                float d = ToSegment(c, a, b);
                if (d < bestWall) { bestWall = d; wall = i; }
            }
            if (wall < 0) return best;
            Vec2 wa = room[wall], wb = room[(wall + 1) % room.Count];
            float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vec2 r = Rotate(new Vec2(sx * hx, sz * hz), box.yawDegrees);
                    best = Mathf.Min(best, ToSegment(new Vec2(c.x + r.x, c.y + r.y), wa, wb));
                }
            return best;
        }

        /// <summary>
        /// Squares a saved zone onto <paramref name="yaw"/> whatever its angle (pure): turned about its centre, then kept
        /// inside the room's bounds in that frame. <paramref name="savedYaw"/> is its yaw before; false when it was already
        /// square or is not a rectangle.
        /// </summary>
        public static bool SquareSaved(List<Vec2> zone, float yaw, IReadOnlyList<Vec2> room, out float savedYaw)
        {
            savedYaw = float.NaN;
            if (!ZoneSetup.RectFrame(zone, out Vec2 c, out _, out _, out savedYaw)) return false;
            float off = Mod90(Mathf.DeltaAngle(savedYaw, yaw));
            if (Mathf.Abs(off) < 0.05f) return false;
            var start = new List<Vec2>(zone);
            ZoneSetup.RotateZone(start, c, off, null, zone);
            Square(zone, yaw, room); // the size kept as far as the room allows
            return true;
        }

        /// <summary>The offset (degrees, within ±45) from <paramref name="yaw"/> to the closest wall direction (mod 90).</summary>
        public static float NearestWallOffset(float yaw, IReadOnlyList<Vec2> room, out bool any)
        {
            any = false;
            float best = 0f;
            for (int i = 0; room != null && i < room.Count; i++)
            {
                Vec2 a = room[i], b = room[(i + 1) % room.Count];
                if (Length(a, b) < MinWall) continue;
                float off = Mod90(Mathf.DeltaAngle(yaw, WallYaw(a, b)));
                if (!any || Mathf.Abs(off) < Mathf.Abs(best)) { best = off; any = true; }
            }
            return best;
        }

        /// <summary>The yaw of the room wall nearest <paramref name="p"/> (walls shorter than <see cref="MinWall"/> skipped).</summary>
        public static bool NearestWallYaw(IReadOnlyList<Vec2> room, Vec2 p, out float yaw)
        {
            yaw = 0f;
            float best = float.MaxValue;
            for (int i = 0; room != null && i < room.Count; i++)
            {
                Vec2 a = room[i], b = room[(i + 1) % room.Count];
                if (Length(a, b) < MinWall) continue;
                float d = ToSegment(p, a, b);
                if (d < best) { best = d; yaw = WallYaw(a, b); }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Makes <paramref name="zone"/> the rectangle at <paramref name="yaw"/> around its points, inside the room's bounds in
        /// that frame (in place; it keeps 4 corners). Replaces clamping each corner on its own, which skewed it.
        /// </summary>
        public static void Square(List<Vec2> zone, float yaw, IReadOnlyList<Vec2> room)
        {
            if (zone == null || zone.Count == 0) return;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < zone.Count; i++) { Vec2 q = Rotate(zone[i], -yaw); x0 = Math.Min(x0, q.x); z0 = Math.Min(z0, q.y); x1 = Math.Max(x1, q.x); z1 = Math.Max(z1, q.y); }
            if (room != null && room.Count >= 3)
            {
                float rx0 = float.MaxValue, rz0 = float.MaxValue, rx1 = float.MinValue, rz1 = float.MinValue;
                for (int i = 0; i < room.Count; i++) { Vec2 q = Rotate(room[i], -yaw); rx0 = Math.Min(rx0, q.x); rz0 = Math.Min(rz0, q.y); rx1 = Math.Max(rx1, q.x); rz1 = Math.Max(rz1, q.y); }
                float cx0 = Math.Max(x0, rx0), cz0 = Math.Max(z0, rz0), cx1 = Math.Min(x1, rx1), cz1 = Math.Min(z1, rz1);
                if (cx1 - cx0 > 0.1f && cz1 - cz0 > 0.1f) { x0 = cx0; z0 = cz0; x1 = cx1; z1 = cz1; }
            }
            zone.Clear();
            zone.Add(Rotate(new Vec2(x0, z0), yaw));
            zone.Add(Rotate(new Vec2(x1, z0), yaw));
            zone.Add(Rotate(new Vec2(x1, z1), yaw));
            zone.Add(Rotate(new Vec2(x0, z1), yaw));
        }

        /// <summary>Whether 4 corners make a rectangle (each corner within about 2 degrees of square).</summary>
        public static bool IsRect(IReadOnlyList<Vec2> p)
        {
            if (p == null || p.Count != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                Vec2 a = p[(i + 3) % 4], o = p[i], b = p[(i + 1) % 4];
                float ax = a.x - o.x, az = a.y - o.y, bx = b.x - o.x, bz = b.y - o.y;
                float la = Mathf.Sqrt(ax * ax + az * az), lb = Mathf.Sqrt(bx * bx + bz * bz);
                if (la < 1e-3f || lb < 1e-3f || Mathf.Abs((ax * bx + az * bz) / (la * lb)) > 0.035f) return false;
            }
            return true;
        }

        public static float WallYaw(Vec2 a, Vec2 b) => Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg - 90f;
        static float Mod90(float off) => ((off % 90f) + 135f) % 90f - 45f;
        static float Length(Vec2 a, Vec2 b) => Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));

        static float ToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            float vx = b.x - a.x, vz = b.y - a.y, l = vx * vx + vz * vz;
            float t = l > 1e-8f ? Mathf.Clamp01(((p.x - a.x) * vx + (p.y - a.y) * vz) / l) : 0f;
            float dx = p.x - (a.x + vx * t), dz = p.y - (a.y + vz * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Unity's rotation around +Y applied to a floor point (x, z), as in ZoneFit and core's ZoneProposer.
        static Vec2 Rotate(Vec2 p, float yawDegrees)
        {
            double r = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(r), sin = (float)Math.Sin(r);
            return new Vec2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }
    }
}

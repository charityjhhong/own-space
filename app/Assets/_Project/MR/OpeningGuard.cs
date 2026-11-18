using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Windows and doors always stay real: a placed design must not overlap any part of one.
    /// Box-vs-box: the opening's full rectangle grown by <see cref="Clearance"/> against the piece's
    /// box, as a floor-plane separating-axis test plus a vertical overlap. Allocation-free.
    /// </summary>
    public static class OpeningGuard
    {
        /// <summary>Gap kept around every opening (m), so a piece flush with the wall still counts.</summary>
        public const float Clearance = 0.05f;

        /// <summary>The box the item would occupy on the anchor, from the fit's orientation and scales.</summary>
        public static OrientedBox Placed(FurnitureAnchor anchor, CatalogItem item, FitResult fit, float floorY) =>
            Placed(anchor, item, fit.Rotated90, fit.XScale, fit.YScale, fit.ZScale, floorY);

        /// <summary>
        /// Scales are in the item's own axes (x width, z depth), applied before the 90° turn.
        /// A scale of 0 or less counts as 1 (a hand-built choice may hold 0).
        /// </summary>
        public static OrientedBox Placed(FurnitureAnchor anchor, CatalogItem item, bool rotated90,
            float xScale, float yScale, float zScale, float floorY)
        {
            float h = item.size.y * OrOne(yScale);
            float x = item.size.x * OrOne(xScale), z = item.size.z * OrOne(zScale);
            float w = rotated90 ? z : x;
            float d = rotated90 ? x : z;
            var c = anchor.Box.center;
            return new OrientedBox(new Vec3(c.x, floorY + h * 0.5f, c.z), new Vec3(w, h, d), anchor.Box.yawDegrees);
        }

        /// <summary>The scale to use: <paramref name="scale"/>, or 1 when it is 0 or less.</summary>
        public static float OrOne(float scale) => scale > 0f ? scale : 1f;

        /// <summary>True when the piece overlaps any window or door (with clearance).</summary>
        public static bool Covers(IRoomInfo room, OrientedBox placed) =>
            CoversAny(room.Windows, placed) || CoversAny(room.Doors, placed);

        static bool CoversAny(IReadOnlyList<RoomElement> openings, OrientedBox placed)
        {
            for (int i = 0; i < openings.Count; i++)
            {
                if (Overlap(openings[i].Box, placed, Clearance)) return true;
            }
            return false;
        }

        /// <summary>True when box <paramref name="a"/> grown by <paramref name="pad"/> overlaps box <paramref name="b"/>.</summary>
        public static bool Overlap(OrientedBox a, OrientedBox b, float pad)
        {
            float aHalfY = a.size.y * 0.5f + pad, bHalfY = b.size.y * 0.5f;
            if (Mathf.Abs(a.center.y - b.center.y) > aHalfY + bHalfY) return false;

            Vector2 d = new Vector2(b.center.x - a.center.x, b.center.z - a.center.z);
            Vector2 ax = Axis(a.yawDegrees), az = Perp(ax), bx = Axis(b.yawDegrees), bz = Perp(bx);
            float aw = a.size.x * 0.5f + pad, ad = a.size.z * 0.5f + pad, bw = b.size.x * 0.5f, bd = b.size.z * 0.5f;
            return !Separated(ax, d, aw, ad, bw, bd, ax, az, bx, bz) && !Separated(az, d, aw, ad, bw, bd, ax, az, bx, bz) &&
                   !Separated(bx, d, aw, ad, bw, bd, ax, az, bx, bz) && !Separated(bz, d, aw, ad, bw, bd, ax, az, bx, bz);
        }

        static bool Separated(Vector2 axis, Vector2 d, float aw, float ad, float bw, float bd,
            Vector2 ax, Vector2 az, Vector2 bx, Vector2 bz)
        {
            float ra = aw * Mathf.Abs(Vector2.Dot(ax, axis)) + ad * Mathf.Abs(Vector2.Dot(az, axis));
            float rb = bw * Mathf.Abs(Vector2.Dot(bx, axis)) + bd * Mathf.Abs(Vector2.Dot(bz, axis));
            return Mathf.Abs(Vector2.Dot(d, axis)) > ra + rb;
        }

        // Local +x on the floor plane (x, z) for a yaw around +Y; local +z is its perpendicular.
        static Vector2 Axis(float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
        }

        static Vector2 Perp(Vector2 x) => new Vector2(-x.y, x.x);
    }
}

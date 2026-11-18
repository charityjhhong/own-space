using System.Collections.Generic;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Pure conversions from MRUK-style anchor geometry (Unity vectors) to the Contracts shapes.
    /// Kept apart from <see cref="MrukRoomInfo"/> so it can be tested without a headset or a scan.
    /// </summary>
    public static class RoomGeometry
    {
        /// <summary>
        /// A gravity-aligned box from a volume's world center and its three world-space edge vectors
        /// (full lengths, in any order). The most vertical edge is the height; the longer horizontal
        /// edge is the width; the front (local +z) is the horizontal normal of the width that points
        /// toward <paramref name="facePoint"/> (usually the room center), so furniture faces the room.
        /// </summary>
        public static OrientedBox BoxFromEdges(Vector3 center, Vector3 e0, Vector3 e1, Vector3 e2, Vector2 facePoint)
        {
            Vector3 up = e0, h1 = e1, h2 = e2;
            if (Mathf.Abs(e1.y) > Mathf.Abs(up.y)) { up = e1; h1 = e0; h2 = e2; }
            if (Mathf.Abs(e2.y) > Mathf.Abs(up.y)) { up = e2; h1 = e0; h2 = e1; }

            h1.y = 0f;
            h2.y = 0f;
            bool firstWider = h1.sqrMagnitude >= h2.sqrMagnitude;
            Vector3 width = firstWider ? h1 : h2;
            Vector3 depth = firstWider ? h2 : h1;

            // Front is perpendicular to the width on the floor; flip it to face the face point.
            Vector3 front = new Vector3(-width.z, 0f, width.x);
            if (front.sqrMagnitude < 1e-8f) front = Vector3.forward;
            front.Normalize();
            Vector3 toFace = new Vector3(facePoint.x - center.x, 0f, facePoint.y - center.z);
            if (Vector3.Dot(front, toFace) < 0f) front = -front;

            return new OrientedBox(
                new Vec3(center.x, center.y, center.z),
                new Vec3(width.magnitude, up.magnitude, depth.magnitude),
                YawOf(front));
        }

        /// <summary>
        /// A flat box for a wall, window or door plane: size (width, height, 0), facing its normal.
        /// </summary>
        public static OrientedBox BoxFromPlane(Vector3 center, float width, float height, Vector3 normal)
        {
            return new OrientedBox(new Vec3(center.x, center.y, center.z), new Vec3(width, height, 0f), YawOf(normal));
        }

        /// <summary>Degrees around +Y that turn +z onto the horizontal part of <paramref name="dir"/>.</summary>
        public static float YawOf(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        /// <summary>Signed area of a floor polygon; positive means counter-clockwise seen from above.</summary>
        public static float SignedArea(IReadOnlyList<Vec2> polygon)
        {
            float area = 0f;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                area += (polygon[j].x * polygon[i].y) - (polygon[i].x * polygon[j].y);
            }
            return area * 0.5f;
        }

        /// <summary>Fills <paramref name="result"/> with the (x, z) outline, counter-clockwise seen from above.</summary>
        public static void FloorOutlineCcw(IReadOnlyList<Vector3> corners, List<Vec2> result)
        {
            result.Clear();
            if (corners == null) return;
            for (int i = 0; i < corners.Count; i++) result.Add(new Vec2(corners[i].x, corners[i].z));
            if (result.Count >= 3 && SignedArea(result) < 0f) result.Reverse();
        }

        /// <summary>Average of the outline points; (0, 0) for an empty outline.</summary>
        public static Vector2 Centroid(IReadOnlyList<Vec2> polygon)
        {
            if (polygon.Count == 0) return Vector2.zero;
            float x = 0f, y = 0f;
            for (int i = 0; i < polygon.Count; i++) { x += polygon[i].x; y += polygon[i].y; }
            return new Vector2(x / polygon.Count, y / polygon.Count);
        }

        /// <summary>
        /// Usable-surface height above the floor for roles that have one (Table, Bed): a horizontal
        /// top plane when the scan gives one, else the box top. Null for other roles.
        /// </summary>
        public static float? UsableHeight(FurnitureRole role, OrientedBox box, float floorY, float? topPlaneY)
        {
            if (!FitLimits.HasUsableSurface(role)) return null;
            float top = topPlaneY ?? (box.center.y + box.size.y * 0.5f);
            float height = top - floorY;
            return height > 0f ? height : (float?)null;
        }
    
        /// <summary>
        /// Distance along <paramref name="ray"/> (unit direction) to the room's floor, ceiling or walls
        /// (the floor outline extruded between the two heights); false when it hits none of them.
        /// Allocation-free.
        /// </summary>
        public static bool RayRoom(Ray ray, IReadOnlyList<Vec2> outline, float floorY, float ceilingY, out float distance)
        {
            distance = float.MaxValue;
            Vector3 o = ray.origin, d = ray.direction;
            if (d.y < -1e-4f) Consider((floorY - o.y) / d.y, ref distance);
            if (d.y > 1e-4f && ceilingY > floorY) Consider((ceilingY - o.y) / d.y, ref distance);
            for (int i = 0, j = outline.Count - 1; outline.Count >= 3 && i < outline.Count; j = i++)
            {
                float ex = outline[i].x - outline[j].x, ez = outline[i].y - outline[j].y;
                float denom = d.x * ez - d.z * ex;
                if (Mathf.Abs(denom) < 1e-6f) continue;
                float wx = outline[j].x - o.x, wz = outline[j].y - o.z;
                float t = (wx * ez - wz * ex) / denom;
                float u = (wx * d.z - wz * d.x) / denom;
                float y = o.y + d.y * t;
                if (u >= 0f && u <= 1f && y >= floorY && (ceilingY <= floorY || y <= ceilingY)) Consider(t, ref distance);
            }
            return distance < float.MaxValue;
        }

        static void Consider(float t, ref float best)
        {
            if (t > 0.01f && t < best) best = t;
        }

        /// <summary>
        /// <paramref name="p"/> if it is inside the polygon (or the polygon has under 3 points);
        /// otherwise the nearest point on the polygon's edges. Allocation-free.
        /// </summary>
        public static Vec2 ClampInside(IReadOnlyList<Vec2> polygon, Vec2 p)
        {
            if (polygon == null || polygon.Count < 3 || OwnSpace.Core.Zones.Contains(polygon, p)) return p;
            Vec2 best = p;
            float bestD2 = float.MaxValue;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vec2 a = polygon[j], b = polygon[i];
                float dx = b.x - a.x, dy = b.y - a.y;
                float len2 = dx * dx + dy * dy;
                float t = len2 > 0f ? Mathf.Clamp01(((p.x - a.x) * dx + (p.y - a.y) * dy) / len2) : 0f;
                var q = new Vec2(a.x + t * dx, a.y + t * dy);
                float d2 = (q.x - p.x) * (q.x - p.x) + (q.y - p.y) * (q.y - p.y);
                if (d2 < bestD2) { bestD2 = d2; best = q; }
            }
            return best;
        }
    }
}

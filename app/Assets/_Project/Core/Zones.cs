using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Zone membership on the floor plane. The user's zone is a simple polygon of (x, z) points,
    /// in either winding order; concave shapes are fine. No allocation, safe to call per frame.
    /// </summary>
    public static class Zones
    {
        /// <summary>Points this close to an edge or vertex (meters) count as on it, and so inside.</summary>
        public const float EdgeTolerance = 1e-4f;

        /// <summary>
        /// True if <paramref name="p"/> is inside the polygon or on its boundary (within
        /// <see cref="EdgeTolerance"/>), so points on a shared edge or vertex give the same answer
        /// in any winding order. Fewer than 3 points, or zero area, contain nothing.
        /// </summary>
        public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 p)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false, onEdge = false;
            float doubleArea = 0f;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vec2 a = polygon[i];
                Vec2 b = polygon[j];
                doubleArea += (b.x - a.x) * (b.y + a.y);
                if (!onEdge && NearSegment(p, a, b)) onEdge = true;
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            if (System.Math.Abs(doubleArea) < EdgeTolerance * EdgeTolerance) return false;
            return onEdge || inside;
        }

        internal static bool NearSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y;
            float lengthSq = dx * dx + dy * dy;
            float t = lengthSq > 0f ? ((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSq : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            float ex = a.x + t * dx - p.x, ey = a.y + t * dy - p.y;
            return ex * ex + ey * ey <= EdgeTolerance * EdgeTolerance;
        }

        /// <summary>A world point is in the zone when its floor projection (x, z) is.</summary>
        public static bool Contains(IReadOnlyList<Vec2> polygon, Vec3 worldPoint) =>
            Contains(polygon, worldPoint.Floor());

        /// <summary>An anchor belongs to the zone its box center stands in.</summary>
        public static bool Contains(IReadOnlyList<Vec2> polygon, FurnitureAnchor anchor) =>
            anchor != null && Contains(polygon, anchor.Box.center.Floor());
    }
}

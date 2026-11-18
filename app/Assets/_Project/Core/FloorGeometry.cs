using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Floor-plane (x, z) geometry for footprints. Rectangles are 4 corners, counter-clockwise.
    /// "Overlap" means a shared area wider than <see cref="Zones.EdgeTolerance"/>: pieces that only
    /// touch (flush against each other, a wall or the zone edge) do not overlap. Allocation-free.
    /// </summary>
    public static class FloorGeometry
    {
        const float Eps = Zones.EdgeTolerance;

        /// <summary>Writes the 4 corners of a footprint (width along local x, depth along local z) into <paramref name="corners"/>.</summary>
        public static void Footprint(Vec2 center, float yawDegrees, float width, float depth, Vec2[] corners)
        {
            float hx = width / 2f, hz = depth / 2f;
            corners[0] = Add(center, Rotate(new Vec2(-hx, -hz), yawDegrees));
            corners[1] = Add(center, Rotate(new Vec2(hx, -hz), yawDegrees));
            corners[2] = Add(center, Rotate(new Vec2(hx, hz), yawDegrees));
            corners[3] = Add(center, Rotate(new Vec2(-hx, hz), yawDegrees));
        }

        /// <summary>Unity's rotation around +Y applied to a floor point (x, z).</summary>
        public static Vec2 Rotate(Vec2 p, float yawDegrees)
        {
            double a = yawDegrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            return new Vec2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }

        /// <summary>Two rectangles share area (separating-axis test).</summary>
        public static bool RectsOverlap(Vec2[] a, Vec2[] b) =>
            !Separated(a, b, a[0], a[1]) && !Separated(a, b, a[1], a[2]) &&
            !Separated(a, b, b[0], b[1]) && !Separated(a, b, b[1], b[2]);

        /// <summary>A rectangle shares area with a polygon (which may be concave).</summary>
        public static bool OverlapsArea(IReadOnlyList<Vec2> polygon, Vec2[] rect)
        {
            if (polygon == null || polygon.Count < 3) return false;
            if (StrictlyInside(polygon, Mid(rect[0], rect[2]))) return true;
            for (int i = 0; i < 4; i++)
            {
                if (StrictlyInside(polygon, rect[i]) || StrictlyInside(polygon, Mid(rect[i], rect[(i + 1) % 4]))) return true;
            }
            for (int i = 0; i < polygon.Count; i++)
            {
                if (StrictlyInside(rect, polygon[i])) return true;
            }
            return EdgesCross(polygon, rect);
        }

        /// <summary>The rectangle lies within the polygon (touching its edges is fine).</summary>
        public static bool InsideArea(IReadOnlyList<Vec2> polygon, Vec2[] rect)
        {
            if (polygon == null || polygon.Count < 3) return false;
            for (int i = 0; i < 4; i++)
            {
                if (!Zones.Contains(polygon, rect[i])) return false;
            }
            for (int i = 0; i < polygon.Count; i++)
            {
                if (StrictlyInside(rect, polygon[i])) return false;
            }
            return !EdgesCross(polygon, rect);
        }

        static bool StrictlyInside(IReadOnlyList<Vec2> polygon, Vec2 p)
        {
            if (!Zones.Contains(polygon, p)) return false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (Zones.NearSegment(p, polygon[i], polygon[j])) return false;
            }
            return true;
        }

        static bool EdgesCross(IReadOnlyList<Vec2> polygon, Vec2[] rect)
        {
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (ProperCross(polygon[j], polygon[i], rect[k], rect[(k + 1) % 4])) return true;
                }
            }
            return false;
        }

        // Segments cross at a single interior point of both (touching or collinear does not count).
        static bool ProperCross(Vec2 p1, Vec2 p2, Vec2 q1, Vec2 q2)
        {
            float d1 = Orient(q1, q2, p1), d2 = Orient(q1, q2, p2);
            float d3 = Orient(p1, p2, q1), d4 = Orient(p1, p2, q2);
            const float e = Eps * Eps;
            return ((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e));
        }

        static bool Separated(Vec2[] a, Vec2[] b, Vec2 e0, Vec2 e1)
        {
            float ax = -(e1.y - e0.y), ay = e1.x - e0.x;
            float len = (float)Math.Sqrt(ax * ax + ay * ay);
            if (len <= 0f) return false;
            Project(a, ax / len, ay / len, out float aMin, out float aMax);
            Project(b, ax / len, ay / len, out float bMin, out float bMax);
            return Math.Min(aMax, bMax) - Math.Max(aMin, bMin) <= Eps;
        }

        static void Project(Vec2[] r, float ax, float ay, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float d = r[i].x * ax + r[i].y * ay;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        static float Orient(Vec2 a, Vec2 b, Vec2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        static Vec2 Add(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        static Vec2 Mid(Vec2 a, Vec2 b) => new Vec2((a.x + b.x) / 2f, (a.y + b.y) / 2f);
    }
}

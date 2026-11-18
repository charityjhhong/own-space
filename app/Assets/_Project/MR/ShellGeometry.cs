using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Floor-plane geometry for the outside-zone shell: polygon triangulation, the parts of a wall
    /// that lie outside my zone, and the seam band around the zone. Runs when the room or zone
    /// changes, never per frame; results go into caller-owned lists.
    /// </summary>
    public static class ShellGeometry
    {
        /// <summary>
        /// Ear-clipping triangulation of a simple polygon in either winding. Writes index triples
        /// that are counter-clockwise seen from above (x, z). Returns false for a degenerate polygon.
        /// </summary>
        public static bool Triangulate(IReadOnlyList<Vec2> polygon, List<int> triangles)
        {
            triangles.Clear();
            int n = polygon != null ? polygon.Count : 0;
            if (n < 3) return false;

            var remaining = new List<int>(n);
            bool ccw = RoomGeometry.SignedArea(polygon) > 0f;
            for (int i = 0; i < n; i++) remaining.Add(ccw ? i : n - 1 - i);

            int guard = n * n;
            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (!IsEar(polygon, remaining, a, b, c)) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) return false; // self-intersecting outline
            }
            if (remaining.Count == 3) { triangles.Add(remaining[0]); triangles.Add(remaining[1]); triangles.Add(remaining[2]); }
            return triangles.Count > 0;
        }

        static bool IsEar(IReadOnlyList<Vec2> p, List<int> remaining, int a, int b, int c)
        {
            if (Cross(p[a], p[b], p[c]) <= 1e-8f) return false; // reflex or flat
            for (int i = 0; i < remaining.Count; i++)
            {
                int k = remaining[i];
                if (k == a || k == b || k == c) continue;
                if (Cross(p[a], p[b], p[k]) >= 0f && Cross(p[b], p[c], p[k]) >= 0f && Cross(p[c], p[a], p[k]) >= 0f) return false;
            }
            return true;
        }

        static float Cross(Vec2 a, Vec2 b, Vec2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        /// <summary>
        /// The parameter ranges (t0, t1) in [0, 1] of segment a→b that lie outside <paramref name="zone"/>.
        /// With no zone (under 3 points) the whole segment is outside.
        /// </summary>
        public static void OutsideIntervals(Vec2 a, Vec2 b, IReadOnlyList<Vec2> zone, List<Vector2> intervals)
        {
            intervals.Clear();
            if (zone == null || zone.Count < 3) { intervals.Add(new Vector2(0f, 1f)); return; }

            var cuts = new List<float> { 0f, 1f };
            for (int i = 0, j = zone.Count - 1; i < zone.Count; j = i++)
            {
                if (SegmentHit(a, b, zone[j], zone[i], out float t)) cuts.Add(t);
            }
            cuts.Sort();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                float t0 = cuts[i], t1 = cuts[i + 1];
                if (t1 - t0 < 1e-4f) continue;
                float m = (t0 + t1) * 0.5f;
                if (Zones.Contains(zone, new Vec2(a.x + (b.x - a.x) * m, a.y + (b.y - a.y) * m))) continue;
                // Merge with the previous outside run when they touch.
                if (intervals.Count > 0 && Mathf.Abs(intervals[intervals.Count - 1].y - t0) < 1e-4f)
                    intervals[intervals.Count - 1] = new Vector2(intervals[intervals.Count - 1].x, t1);
                else intervals.Add(new Vector2(t0, t1));
            }
        }

        /// <summary>A zone side within this distance of a wall (m) counts that stretch of wall as the zone's.</summary>
        public const float WallReach = 0.4f;

        /// <summary>
        /// Takes out of <paramref name="outside"/> (from <see cref="OutsideIntervals"/>) the stretch of wall ab that the zone
        /// runs along: the span of the zone corners within <paramref name="reach"/> of the wall line, projected onto it (pure).
        /// A zone parallel to the wall but stopping short of it (its corners are kept inside the room) then owns the wall
        /// edge to edge, not nothing or a centre strip (device shot 182221). Needs at least two corners within reach.
        /// </summary>
        public static void TakeWallSpan(Vec2 a, Vec2 b, IReadOnlyList<Vec2> zone, float reach, List<Vector2> outside)
        {
            if (zone == null || zone.Count < 3) return;
            float vx = b.x - a.x, vz = b.y - a.y, len2 = vx * vx + vz * vz;
            if (len2 < 1e-6f) return;
            float len = Mathf.Sqrt(len2), t0 = float.MaxValue, t1 = float.MinValue;
            int near = 0;
            for (int i = 0; i < zone.Count; i++)
            {
                float px = zone[i].x - a.x, pz = zone[i].y - a.y;
                if (Mathf.Abs(px * vz - pz * vx) / len > reach) continue; // perpendicular distance to the wall line
                float t = (px * vx + pz * vz) / len2;
                near++;
                t0 = Mathf.Min(t0, t); t1 = Mathf.Max(t1, t);
            }
            t0 = Mathf.Clamp01(t0); t1 = Mathf.Clamp01(t1);
            if (near < 2 || t1 - t0 < 1e-4f) return;
            for (int i = outside.Count - 1; i >= 0; i--)
            {
                Vector2 r = outside[i];
                if (r.y <= t0 || r.x >= t1) continue; // no overlap
                outside.RemoveAt(i);
                if (r.y > t1) outside.Insert(i, new Vector2(t1, r.y));
                if (r.x < t0) outside.Insert(i, new Vector2(r.x, t0));
            }
        }

        static bool SegmentHit(Vec2 a, Vec2 b, Vec2 c, Vec2 d, out float t)
        {
            t = 0f;
            float rx = b.x - a.x, ry = b.y - a.y, sx = d.x - c.x, sy = d.y - c.y;
            float den = rx * sy - ry * sx;
            if (Mathf.Abs(den) < 1e-9f) return false;
            float qx = c.x - a.x, qy = c.y - a.y;
            t = (qx * sy - qy * sx) / den;
            float u = (qx * ry - qy * rx) / den;
            return t > 0f && t < 1f && u >= 0f && u <= 1f;
        }

        /// <summary>
        /// One quad per zone edge, from the edge outward by <paramref name="width"/> (away from the zone).
        /// Writes 4 points per edge: inner start, inner end, outer end, outer start.
        /// </summary>
        public static void SeamBand(IReadOnlyList<Vec2> zone, float width, List<Vec2> quads)
        {
            quads.Clear();
            if (zone == null || zone.Count < 3) return;
            float sign = RoomGeometry.SignedArea(zone) > 0f ? 1f : -1f;
            for (int i = 0; i < zone.Count; i++)
            {
                Vec2 a = zone[i], b = zone[(i + 1) % zone.Count];
                float dx = b.x - a.x, dy = b.y - a.y, len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-5f) continue;
                // For a counter-clockwise polygon the outward normal is (dy, -dx).
                float nx = sign * dy / len * width, ny = -sign * dx / len * width;
                quads.Add(a); quads.Add(b);
                quads.Add(new Vec2(b.x + nx, b.y + ny)); quads.Add(new Vec2(a.x + nx, a.y + ny));
            }
        }

        /// <summary>
        /// Where a wall's outside parts end at the zone border (not at the wall's own ends), the soft fade into the
        /// real wall (pure, for tests): for each such end, a pair (t where alpha is 0, t where alpha is 1) along the
        /// wall, <paramref name="width"/> metres long and never past the part's other end.
        /// </summary>
        public static void BorderFades(List<Vector2> parts, float wallLength, float width, List<Vector2> fades)
        {
            fades.Clear();
            if (wallLength < 1e-4f) return;
            float w = width / wallLength;
            const float End = 1e-3f;
            for (int i = 0; i < parts.Count; i++)
            {
                float t0 = parts[i].x, t1 = parts[i].y;
                if (t0 > End) fades.Add(new Vector2(t0, Mathf.Min(t0 + w, t1)));
                if (t1 < 1f - End) fades.Add(new Vector2(t1, Mathf.Max(t1 - w, t0)));
            }
        }

        /// <summary>
        /// The room outline pushed out by <paramref name="by"/> metres (pure, for tests): the virtual floor and ceiling
        /// reach under and over the walls, so no gap shows where the scanned outline and the wall planes do not quite
        /// meet (device batch 7: black slivers at the floor edge and a ceiling corner). Mitred, capped at 3x at sharp
        /// corners. Any part past a wall is hidden behind it.
        /// </summary>
        public static void Expand(IReadOnlyList<Vec2> outline, float by, List<Vec2> result)
        {
            result.Clear();
            int n = outline != null ? outline.Count : 0;
            if (n < 3) { for (int i = 0; i < n; i++) result.Add(outline[i]); return; }
            float sign = RoomGeometry.SignedArea(outline) > 0f ? 1f : -1f;
            for (int i = 0; i < n; i++)
            {
                Vec2 prev = outline[(i + n - 1) % n], p = outline[i], next = outline[(i + 1) % n];
                Vector2 n1 = Outward(prev, p, sign), n2 = Outward(p, next, sign), m = n1 + n2;
                if (m.sqrMagnitude < 1e-8f) m = n1;
                m.Normalize();
                float cos = Mathf.Max(Vector2.Dot(m, n1), 1f / 3f); // a mitre, at most 3x long at sharp corners
                result.Add(new Vec2(p.x + m.x * by / cos, p.y + m.y * by / cos));
            }
        }

        /// <summary>The outline's axis-aligned bounding rectangle grown by <paramref name="by"/>, counter-clockwise (pure).</summary>
        public static void Bounds(IReadOnlyList<Vec2> outline, float by, List<Vec2> result)
        {
            result.Clear();
            if (outline == null || outline.Count == 0) return;
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            for (int i = 0; i < outline.Count; i++)
            {
                x0 = Mathf.Min(x0, outline[i].x); z0 = Mathf.Min(z0, outline[i].y);
                x1 = Mathf.Max(x1, outline[i].x); z1 = Mathf.Max(z1, outline[i].y);
            }
            result.Add(new Vec2(x0 - by, z0 - by)); result.Add(new Vec2(x1 + by, z0 - by));
            result.Add(new Vec2(x1 + by, z1 + by)); result.Add(new Vec2(x0 - by, z1 + by));
        }

        // The outward unit normal of edge a-b of a polygon wound by sign (+1 counter-clockwise).
        static Vector2 Outward(Vec2 a, Vec2 b, float sign)
        {
            var d = new Vector2(b.x - a.x, b.y - a.y);
            if (d.sqrMagnitude < 1e-10f) return Vector2.zero;
            d.Normalize();
            return new Vector2(d.y, -d.x) * sign;
        }

        /// <summary>
        /// How far a floor point is from the nearest edge of <paramref name="outline"/> (m, pure). A scanned wall whose ends
        /// are not on the floor outline stands in front of or behind the plane the floor and ceiling end at.
        /// </summary>
        public static float DistanceToOutline(Vec2 p, IReadOnlyList<Vec2> outline)
        {
            if (outline == null || outline.Count < 2) return 0f;
            float best = float.MaxValue;
            var q = new Vector2(p.x, p.y);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = new Vector2(outline[i].x, outline[i].y), b = new Vector2(outline[(i + 1) % outline.Count].x, outline[(i + 1) % outline.Count].y);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
            }
            return best;
        }

        /// <summary>The floor-level endpoints of a wall box (its width runs along local x).</summary>
        public static void WallEnds(OrientedBox wall, out Vec2 start, out Vec2 end)
        {
            Vector3 right = Quaternion.Euler(0f, wall.yawDegrees, 0f) * Vector3.right * (wall.size.x * 0.5f);
            start = new Vec2(wall.center.x - right.x, wall.center.z - right.z);
            end = new Vec2(wall.center.x + right.x, wall.center.z + right.z);
        }
    
        /// <summary>
        /// Unit floor direction from a window or door toward the inside of the room: the opening's
        /// front (+z by its yaw), flipped when that points out of the room polygon. Holes and frames
        /// are offset along it so the virtual wall can never cover the opening.
        /// </summary>
        public static Vector3 IntoRoom(OrientedBox opening, IReadOnlyList<Vec2> floorOutline)
        {
            Vector3 front = Quaternion.Euler(0f, opening.yawDegrees, 0f) * Vector3.forward;
            if (floorOutline == null || floorOutline.Count < 3) return front;
            const float probe = 0.1f;
            var ahead = new Vec2(opening.center.x + front.x * probe, opening.center.z + front.z * probe);
            var behind = new Vec2(opening.center.x - front.x * probe, opening.center.z - front.z * probe);
            return !Zones.Contains(floorOutline, ahead) && Zones.Contains(floorOutline, behind) ? -front : front;
        }
    }
}

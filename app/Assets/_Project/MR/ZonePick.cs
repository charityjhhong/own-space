using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>What a pinch grabs in the zone step.</summary>
    public enum ZoneGrab { None, Corner, Edge, Whole }

    /// <summary>
    /// Zone-step picking (device batch 7: 14 of 18 pinches "hit nothing"; a knob needed the pinch point within 7 cm or
    /// the ray right on the ball). In order: a corner (the pinch point near its ball or post; the ray within
    /// <see cref="RayAngle"/> of the ball or near its post; the ray's floor point within <see cref="FloorSnap"/> of the
    /// corner), an edge (the floor point within <see cref="EdgeSnap"/> of it), or the whole zone (the floor point inside
    /// it). Pure and allocation-free.
    /// </summary>
    public static class ZonePick
    {
        public const float NearRadius = 0.1f, RayAngle = 8f, PostReach = 0.06f, FloorSnap = 0.25f, EdgeSnap = 0.15f,
            PostPerMetre = 0.05f, SnapPerMetre = 0.123f, EdgeReach = 0.05f, EdgePerMetre = 0.06f, MinDownDegrees = 10f, MaxFloorReach = 6f;

        /// <summary>A knob's diameter at <paramref name="distance"/> m: about 3 degrees of view, at least 9 cm (pure).</summary>
        public static float KnobSize(float distance) => Mathf.Max(0.09f, distance * 0.052f);

        public static ZoneGrab Pick(Ray ray, Vector3 pinch, bool pinchValid, IReadOnlyList<Vec2> zone, float floorY, float knobHeight,
            out int index, out float nearestCorner)
        {
            index = -1;
            nearestCorner = float.MaxValue;
            int n = zone != null ? zone.Count : 0;
            float best = NearRadius;
            for (int i = 0; pinchValid && i < n; i++)
            {
                float d = ToSegment(pinch, Floor(zone[i], floorY), Floor(zone[i], floorY + knobHeight));
                if (d < best) { best = d; index = i; }
            }
            if (index >= 0) return ZoneGrab.Corner;

            best = RayAngle;
            for (int i = 0; i < n; i++)
            {
                Vector3 knob = Floor(zone[i], floorY + knobHeight);
                float a = Vector3.Angle(ray.direction, knob - ray.origin);
                if (a < best) { best = a; index = i; }
            }
            if (index >= 0) return ZoneGrab.Corner;

            best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                // The post's reach grows with distance (about 3 degrees), so a far corner is as easy as a near one.
                float d = RayToSegment(ray, Floor(zone[i], floorY), Floor(zone[i], floorY + knobHeight));
                float reach = Mathf.Max(PostReach, Vector3.Distance(ray.origin, Floor(zone[i], floorY + knobHeight * 0.5f)) * PostPerMetre);
                if (d < reach && d < best) { best = d; index = i; }
            }
            if (index >= 0) return ZoneGrab.Corner;

            // Seated at a desk the ray is often too flat to reach the floor (device, build 0ddb26d: "ray misses the floor"):
            // then an edge is picked by the ray's closest approach to it, scaled by distance (about 3.5 degrees).
            if (!XrVisuals.RayFloor(ray, floorY, out Vector3 hit))
            {
                nearestCorner = RayClosest(ray, zone, floorY); // the miss log says how close the ray came, in metres
                return RayEdge(ray, zone, floorY, out index);
            }
            var p = new Vec2(hit.x, hit.z);
            for (int i = 0; i < n; i++)
            {
                float d = Distance(p, zone[i]);
                if (d < nearestCorner) nearestCorner = d;
                // The floor snap also grows with distance (about 7 degrees): 0.25 m near, 0.37 m at 3 m.
                float snap = Mathf.Max(FloorSnap, Vector3.Distance(ray.origin, Floor(zone[i], floorY)) * SnapPerMetre);
                if (d < snap && (index < 0 || d < Distance(p, zone[index]))) index = i;
            }
            if (index >= 0) return ZoneGrab.Corner;

            best = EdgeSnap;
            for (int i = 0; i < n; i++)
            {
                float d = ToSegment2(p, zone[i], zone[(i + 1) % n]);
                if (d < best) { best = d; index = i; }
            }
            if (index >= 0) return ZoneGrab.Edge;
            if (n >= 3 && Zones.Contains(zone, p)) return ZoneGrab.Whole;
            return RayEdge(ray, zone, floorY, out index);
        }

        /// <summary>The ray's closest approach (m) to any side of the zone on the floor (pure); MaxValue for no zone.</summary>
        public static float RayClosest(Ray ray, IReadOnlyList<Vec2> zone, float floorY)
        {
            float best = float.MaxValue;
            int n = zone != null ? zone.Count : 0;
            for (int i = 0; n >= 2 && i < n; i++) best = Mathf.Min(best, RayToLine(ray, Floor(zone[i], floorY), Floor(zone[(i + 1) % n], floorY), out _));
            return best;
        }

        /// <summary>The edge the ray passes closest to, within <see cref="EdgeReach"/> or about 3.5 degrees (pure).</summary>
        public static ZoneGrab RayEdge(Ray ray, IReadOnlyList<Vec2> zone, float floorY, out int index)
        {
            index = -1;
            int n = zone != null ? zone.Count : 0;
            float best = float.MaxValue;
            for (int i = 0; n >= 2 && i < n; i++)
            {
                Vector3 a = Floor(zone[i], floorY), b = Floor(zone[(i + 1) % n], floorY);
                float d = RayToLine(ray, a, b, out Vector3 at);
                float reach = Mathf.Max(EdgeReach, Vector3.Distance(ray.origin, at) * EdgePerMetre);
                if (d < reach && d < best) { best = d; index = i; }
            }
            return index >= 0 ? ZoneGrab.Edge : ZoneGrab.None;
        }

        /// <summary>
        /// Whether a drag starting with <paramref name="ray"/> follows the ray's floor point (pure): the ray points down by
        /// at least <see cref="MinDownDegrees"/> and lands within <see cref="MaxFloorReach"/>. Otherwise the drag keeps the
        /// grabbed point's distance and follows the ray's direction (<see cref="DragPoint"/>).
        /// </summary>
        public static bool FloorDrag(Ray ray, float floorY) =>
            XrVisuals.RayFloor(ray, floorY, out Vector3 hit) && Vector3.Angle(ray.direction, Vector3.ProjectOnPlane(ray.direction, Vector3.up)) >= MinDownDegrees
            && ray.direction.y < 0f && Vector3.Distance(ray.origin, hit) <= MaxFloorReach;

        /// <summary>
        /// Where a drag is now (pure, on the floor): the ray's floor point in floor mode; else the point at the grabbed
        /// point's level, as far out (horizontally) as it was, in the ray's direction. False only for a vertical ray.
        /// </summary>
        public static bool DragPoint(Ray ray, float floorY, bool floorMode, float reach, out Vector3 point)
        {
            point = default;
            if (floorMode && XrVisuals.RayFloor(ray, floorY, out Vector3 hit)) { point = hit; return true; }
            Vector3 flat = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f) return false;
            Vector3 o = new Vector3(ray.origin.x, floorY, ray.origin.z);
            point = o + flat.normalized * reach;
            return true;
        }

        /// <summary>The horizontal distance from the ray's origin to <paramref name="p"/> (the reach a flat-ray drag keeps).</summary>
        public static float Reach(Ray ray, Vector3 p) { float x = p.x - ray.origin.x, z = p.z - ray.origin.z; return Mathf.Sqrt(x * x + z * z); }

        static Vector3 Floor(Vec2 p, float y) => new Vector3(p.x, y, p.y);
        static float Distance(Vec2 a, Vec2 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        static float ToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        static float ToSegment2(Vec2 p, Vec2 a, Vec2 b) =>
            ToSegment(new Vector3(p.x, 0f, p.y), new Vector3(a.x, 0f, a.y), new Vector3(b.x, 0f, b.y));

        // The ray's closest approach to segment ab (exact): the distance, and the segment's closest point.
        static float RayToLine(Ray ray, Vector3 a, Vector3 b, out Vector3 at)
        {
            Vector3 u = ray.direction.normalized, v = b - a, w = ray.origin - a;
            float A = 1f, B = Vector3.Dot(u, v), C = Vector3.Dot(v, v), D = Vector3.Dot(u, w), E = Vector3.Dot(v, w);
            float den = A * C - B * B, t = 0f, s;
            if (C < 1e-8f) s = 0f;
            else if (den < 1e-8f) s = Mathf.Clamp01(E / C); // parallel
            else { s = Mathf.Clamp01((A * E - B * D) / den); }
            at = a + v * s;
            t = Mathf.Max(0f, Vector3.Dot(at - ray.origin, u)); // only in front of the hand
            return Vector3.Distance(ray.origin + u * t, at);
        }

        // The ray's closest approach to a segment, sampled along it (posts are short; five samples are plenty).
        static float RayToSegment(Ray ray, Vector3 a, Vector3 b)
        {
            float best = float.MaxValue;
            for (int k = 0; k <= 4; k++)
            {
                Vector3 q = Vector3.Lerp(a, b, k / 4f), oq = q - ray.origin;
                float along = Vector3.Dot(oq, ray.direction);
                if (along < 0f) continue;
                best = Mathf.Min(best, Mathf.Sqrt(Mathf.Max(0f, oq.sqrMagnitude - along * along)));
            }
            return best;
        }
    }
}

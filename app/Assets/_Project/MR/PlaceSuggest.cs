using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// A suggested spot for a floor piece from the Add tab (the human: a big scanned room, plenty of virtual floor,
    /// and "can't place" wherever a bed or sofa was aimed). Tries the same rules as placing (the caller's check), against
    /// every wall (back to the wall, facing into the room) and on a floor grid (facing the user), and keeps the valid spot
    /// nearest where the user aimed, a wall spot counting <see cref="WallBonus"/> m nearer. Counts every candidate's
    /// result per rule, so a log says which rule refuses. Runs once per pick or per invalid aim, never per frame.
    /// No allocation: the caller owns the tally.
    /// </summary>
    public static class PlaceSuggest
    {
        /// <summary>Floor grid step and the step along a wall (m).</summary>
        public const float Grid = 0.3f, WallStep = 0.25f;
        /// <summary>Gap between a piece's back and the wall (m); how much nearer a spot against a wall counts (m).</summary>
        public const float WallGap = 0.02f, WallBonus = 0.6f;
        /// <summary>The tally's size: one slot per <see cref="PlacementStatus"/>.</summary>
        public const int TallySize = 16;

        /// <summary>The placement rules for a piece's floor centre and yaw.</summary>
        public delegate PlacementStatus Check(Vector2 at, float yawDegrees);

        /// <summary>
        /// The best valid spot (pure): <paramref name="best"/> is the floor centre (x, z), <paramref name="bestYaw"/> its yaw.
        /// <paramref name="size"/> is the piece's width (x) and depth (y). False when no candidate passes.
        /// <paramref name="tally"/> (at least <see cref="TallySize"/> long, cleared here) counts each rule's verdicts.
        /// </summary>
        public static bool Find(IReadOnlyList<Vec2> outline, Vector2 size, Vector2 aim, Vector2 user, Check check, int[] tally,
            out Vector2 best, out float bestYaw)
        {
            best = default;
            bestYaw = 0f;
            if (tally != null) System.Array.Clear(tally, 0, tally.Length);
            if (outline == null || outline.Count < 3 || check == null) return false;
            float bestScore = float.MaxValue;

            // Against each wall: the piece's back on the wall, facing into the room, all along the wall.
            for (int i = 0; i < outline.Count; i++)
            {
                if (!Wall(outline, i, out Vector2 a, out Vector2 e, out Vector2 n, out float len) || len < size.x) continue;
                float yaw = Mathf.Repeat(Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg, 360f);
                for (float t = size.x * 0.5f; t <= len - size.x * 0.5f + 1e-4f; t += WallStep)
                {
                    Vector2 at = a + e * t + n * (size.y * 0.5f + WallGap);
                    Try(at, yaw, Vector2.Distance(at, aim) - WallBonus, check, tally, ref best, ref bestYaw, ref bestScore);
                }
            }

            // On the floor grid, facing the user.
            Bounds(outline, out Vector2 min, out Vector2 max);
            for (float x = min.x + Grid * 0.5f; x < max.x; x += Grid)
            for (float z = min.y + Grid * 0.5f; z < max.y; z += Grid)
            {
                var at = new Vector2(x, z);
                if (!Zones.Contains(outline, new Vec2(x, z))) { Count(tally, PlacementStatus.OutsideRoom); continue; }
                float yaw = Facing.TowardUser(new Vec2(x, z), new Vec2(user.x, user.y));
                Try(at, yaw, Vector2.Distance(at, aim), check, tally, ref best, ref bestYaw, ref bestScore);
            }
            return bestScore < float.MaxValue;
        }

        static void Try(Vector2 at, float yaw, float score, Check check, int[] tally, ref Vector2 best, ref float bestYaw, ref float bestScore)
        {
            PlacementStatus status = check(at, yaw);
            Count(tally, status);
            if (status != PlacementStatus.Ok || score >= bestScore) return;
            bestScore = score;
            best = at;
            bestYaw = yaw;
        }

        static void Count(int[] tally, PlacementStatus status)
        {
            int i = (int)status;
            if (tally != null && i >= 0 && i < tally.Length) tally[i]++;
        }

        /// <summary>One line: the candidates and how many each rule refused (allocates: for the log, once per search).</summary>
        public static string Describe(int[] tally)
        {
            var text = new System.Text.StringBuilder();
            int total = 0;
            for (int i = 0; i < tally.Length; i++) total += tally[i];
            text.Append(total).Append(" spots tried:");
            for (int i = 0; i < tally.Length; i++)
                if (tally[i] > 0) text.Append(' ').Append((PlacementStatus)i).Append(' ').Append(tally[i]);
            return text.ToString();
        }

        // Outline edge i: its start, direction, inward normal and length.
        static bool Wall(IReadOnlyList<Vec2> outline, int i, out Vector2 a, out Vector2 e, out Vector2 n, out float len)
        {
            Vec2 p = outline[i], q = outline[(i + 1) % outline.Count];
            a = new Vector2(p.x, p.y);
            Vector2 d = new Vector2(q.x - p.x, q.y - p.y);
            len = d.magnitude;
            e = n = default;
            if (len < 1e-3f) return false;
            e = d / len;
            n = new Vector2(-e.y, e.x);
            Vector2 mid = a + d * 0.5f + n * 0.05f;
            if (!Zones.Contains(outline, new Vec2(mid.x, mid.y))) n = -n;
            return true;
        }

        static void Bounds(IReadOnlyList<Vec2> outline, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < outline.Count; i++)
            {
                min = Vector2.Min(min, new Vector2(outline[i].x, outline[i].y));
                max = Vector2.Max(max, new Vector2(outline[i].x, outline[i].y));
            }
        }
    }
}

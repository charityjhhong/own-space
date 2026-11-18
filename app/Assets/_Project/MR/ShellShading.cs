using System.Collections.Generic;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Soft contact shading where the virtual room's surfaces meet, and a fine world-anchored grain (the human, build 9bfbd72:
    /// "the wall corners lack realism"). Computed in the finish shader from the fragment's world position
    /// (<c>ToneMatchSurface</c> in MR/Shaders/ToneMatch.hlsl) against the room outline, floor and ceiling, which
    /// <see cref="Push"/> sets as globals when the shell is rebuilt: no extra pass, no texture, no per-frame work.
    /// <see cref="Shade"/> is the same rule in C#, for tests. It only darkens by a share, so a crease at Night is darker, never black.
    /// </summary>
    public static class ShellShading
    {
        /// <summary>
        /// Every strength in one place (device feedback on e5bf5fd: "overall too dark, the wall noise too strong").
        /// Darkening right in a wall-wall or wall-floor crease, and in the lighter wall-ceiling crease; gone at
        /// <see cref="CreaseWidth"/> (m); exactly 1 elsewhere (no overall falloff). Grain: a soft, zero-mean paint variation
        /// (± share) on walls and floor, none on the ceiling. Before (#287): 0.35, 0.35, 0.20 m, falloff 0.06 over 1 m, ±0.025.
        /// </summary>
        public const float CreaseStrength = 0.19f, CeilingCreaseStrength = 0.12f, CreaseWidth = 0.12f, Grain = 0.007f;
        public const int MaxCorners = 16;

        static readonly int EdgesId = Shader.PropertyToID("_OS_RoomEdges"), InfoId = Shader.PropertyToID("_OS_RoomInfo"),
            CreaseId = Shader.PropertyToID("_OS_Crease");
        static readonly Vector4[] Edges = new Vector4[MaxCorners];

        /// <summary>Sets the room for the finish shader (on a shell rebuild only); off with no outline.</summary>
        public static void Push(IReadOnlyList<Vec2> outline, float floorY, float ceilingY)
        {
            int n = CornerCount(outline);
            for (int i = 0; i < MaxCorners; i++) Edges[i] = i < n ? new Vector4(outline[i].x, outline[i].y, 0f, 0f) : Vector4.zero;
            Shader.SetGlobalVectorArray(EdgesId, Edges);
            Shader.SetGlobalVector(InfoId, new Vector4(n, floorY, ceilingY, n >= 3 ? 1f : 0f));
            Shader.SetGlobalVector(CreaseId, new Vector4(CreaseStrength, CreaseWidth, CeilingCreaseStrength, Grain));
        }

        /// <summary>
        /// The corners the shader loops over (pure): the outline's, capped at <see cref="MaxCorners"/> (the shader's constant
        /// loop bound, OS_MAX_CORNERS). A longer outline (a visit's) is not shaded rather than shaded wrong.
        /// </summary>
        public static int CornerCount(IReadOnlyList<Vec2> outline) => outline != null && outline.Count <= MaxCorners ? outline.Count : 0;

        /// <summary>The shading factor (0..1, multiplies the colour) at <paramref name="p"/>, without the grain (pure).</summary>
        public static float Shade(Vector3 p, IReadOnlyList<Vec2> outline, float floorY, float ceilingY)
        {
            int n = CornerCount(outline);
            if (n < 3) return 1f;
            float wall = float.MaxValue, corner = float.MaxValue;
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = new Vector2(outline[i].x, outline[i].y), b = new Vector2(outline[(i + 1) % n].x, outline[(i + 1) % n].y);
                corner = Mathf.Min(corner, Vector2.Distance(q, a));
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude) : 0f;
                wall = Mathf.Min(wall, Vector2.Distance(q, a + ab * t));
            }
            float sWall = Near(wall, CreaseWidth);
            float low = Mathf.Max(Near(corner, CreaseWidth), Near(p.y - floorY, CreaseWidth) * sWall), high = Near(ceilingY - p.y, CreaseWidth) * sWall;
            return Mathf.Min(1f - CreaseStrength * low, 1f - CeilingCreaseStrength * high);
        }

        // 1 at the crease, 0 from width on, smooth (smoothstep), as in the shader.
        static float Near(float d, float width)
        {
            float x = Mathf.Clamp01(Mathf.Max(0f, d) / width);
            return 1f - x * x * (3f - 2f * x);
        }
    }
}

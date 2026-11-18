using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: "the wall became horizontal lines; it was vertical lines originally" (build d3a6dcc, before #318's normals).
    // The Lines texture's stripes run down the image (v). Art/Shaders/OwnSpaceFinish.shader samples
    // uv = (dot(p, t), dot(p, b)) with t = normalize(cross(up, n)), b = cross(n, t). With the shell's normals (#318) that is
    // u along the wall and v up, in metres, on every wall whatever its direction: the stripes stand floor to ceiling.
    public class WallUvTests
    {
        static readonly Vector3 Centre = new Vector3(0f, 1.3f, 0f);

        // The shader's frame, for a wall from a to b (OutsideShell.FaceNormal gives the normal it now has).
        static Vector2 Uv(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 n = OutsideShell.FaceNormal(a, b, a + Vector3.up, Centre);
            Vector3 t = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
            Vector3 bt = Vector3.Cross(n, t);
            return new Vector2(Vector3.Dot(p, t), Vector3.Dot(p, bt));
        }

        [Test]
        public void OnEveryWall_UGrowsAlongIt_AndVGrowsUp_InMetres()
        {
            var walls = new[]
            {
                (new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f)),   // faces -z
                (new Vector3(2f, 0f, -2f), new Vector3(-2f, 0f, -2f)), // faces +z
                (new Vector3(2f, 0f, 2f), new Vector3(2f, 0f, -2f)),   // faces -x
                (new Vector3(-2f, 0f, -2f), new Vector3(-2f, 0f, 2f)), // faces +x
                (new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 2f)),    // diagonal
            };
            foreach (var (a, b) in walls)
            {
                Vector3 along = (b - a).normalized, p = Vector3.Lerp(a, b, 0.3f) + Vector3.up * 1.2f;
                Vector2 uv = Uv(p, a, b), stepAlong = Uv(p + along * 0.5f, a, b) - uv, stepUp = Uv(p + Vector3.up * 0.5f, a, b) - uv;
                Assert.AreEqual(0.5f, Mathf.Abs(stepAlong.x), 1e-4f, $"u runs along the wall {a}->{b}, 1:1 in metres");
                Assert.AreEqual(0f, stepAlong.y, 1e-4f, "v does not change along it");
                Assert.AreEqual(0.5f, stepUp.y, 1e-4f, "v grows upward, 1:1 in metres: the texture's vertical stripes stand upright");
                Assert.AreEqual(0f, stepUp.x, 1e-4f, "u does not change going up");
            }
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device (build 2dc475d): "Lines" thin across and stretched down the wall; Oak did not show on the floor. The finish
    // shader projects in metres along the surface from the normal (u along the wall, v up; floors x, z); the shell had none.
    public class ShellNormalsTests
    {
        static readonly Vector3 Centre = new Vector3(2f, 1.3f, 1.5f);

        [Test]
        public void Walls_FaceIntoTheRoom_Horizontally_WhateverTheWinding()
        {
            Vector3 a = new Vector3(0f, 0f, 0f), b = new Vector3(4f, 0f, 0f), up = Vector3.up * 2.6f;
            Vector3 n1 = OutsideShell.FaceNormal(a, b, a + up, Centre), n2 = OutsideShell.FaceNormal(b, a, b + up, Centre);
            Assert.AreEqual(Vector3.forward, n1, "the wall along x at z 0 faces +z, into the room");
            Assert.AreEqual(Vector3.forward, n2, "the other winding: the same");
            Assert.AreEqual(0f, n1.y, 1e-6f, "horizontal: the pattern runs along the wall and up, in metres on both");
            Vector3 c = new Vector3(4f, 0f, 3f);
            Assert.AreEqual(Vector3.left, OutsideShell.FaceNormal(c, new Vector3(4f, 0f, 0f), c + up, Centre), "the wall at x 4 faces -x");
        }

        [Test]
        public void FloorsFaceUp_CeilingsDown()
        {
            Assert.AreEqual(Vector3.up, OutsideShell.FlatNormal(0.001f, 1.3f), "the floor and a my-zone floor finish");
            Assert.AreEqual(Vector3.down, OutsideShell.FlatNormal(2.6f, 1.3f), "the ceiling");
            Vector3 f = OutsideShell.FaceNormal(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), Centre);
            Assert.AreEqual(Vector3.up, f, "a floor quad faces up toward the room");
        }

        // Device 26a0806: "Floor Oak/Birch/Dark/Tiles changes nothing". The floor was a back face from above (culled).
        [Test]
        public void TheFloor_IsAFrontFaceFromAbove_TheCeilingFromBelow()
        {
            var square = new System.Collections.Generic.List<OwnSpace.Contracts.Vec2>
                { new OwnSpace.Contracts.Vec2(0f, 0f), new OwnSpace.Contracts.Vec2(4f, 0f), new OwnSpace.Contracts.Vec2(4f, 3f), new OwnSpace.Contracts.Vec2(0f, 3f) };
            foreach (bool reversed in new[] { false, true }) // either winding of the scanned outline
            {
                var outline = new System.Collections.Generic.List<OwnSpace.Contracts.Vec2>(square);
                if (reversed) outline.Reverse();
                var ccw = new System.Collections.Generic.List<int>();
                Assert.IsTrue(ShellGeometry.Triangulate(outline, ccw));
                foreach (bool up in new[] { true, false })
                {
                    var tris = new System.Collections.Generic.List<int>();
                    OutsideShell.AddWound(ccw, 0, up, tris);
                    Assert.AreEqual(ccw.Count, tris.Count);
                    for (int t = 0; t < tris.Count; t += 3)
                    {
                        Vector3 a = P(outline[tris[t]]), b = P(outline[tris[t + 1]]), c = P(outline[tris[t + 2]]);
                        // Unity's front face is the side cross(b - a, c - a) points to (its RecalculateNormals rule).
                        float y = Vector3.Cross(b - a, c - a).y;
                        if (up) Assert.Greater(y, 0f, "the floor faces up: drawn from above");
                        else Assert.Less(y, 0f, "the ceiling faces down: drawn from below");
                    }
                }
            }
        }

        static Vector3 P(OwnSpace.Contracts.Vec2 v) => new Vector3(v.x, 0f, v.y);
    }
}

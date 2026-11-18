using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human (build 9bfbd72): "the wall corners lack realism; add shadow where the walls meet".
    public class ShellShadingTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(5f, 0f), new Vec2(5f, 4f), new Vec2(0f, 4f) };
        const float Floor = 0f, Ceiling = 2.6f;
        static float S(float x, float y, float z) => ShellShading.Shade(new Vector3(x, y, z), Room, Floor, Ceiling);

        // Device feedback (e5bf5fd): "overall too dark". Lighter, tighter creases; exactly 1 everywhere else.
        [Test]
        public void Creases_AreLightAndTight_AndTheRestIsUntouched()
        {
            Assert.AreEqual(1f, S(2.5f, 1.3f, 0f), 1e-5f, "a plain wall is untouched");
            Assert.AreEqual(1f, S(0.5f, 1.3f, 0f), 1e-5f, "no overall falloff toward a corner: 50 cm from it, untouched");
            Assert.AreEqual(1f - ShellShading.CreaseStrength, S(0f, 1.3f, 0f), 1e-4f, "wall-wall corner: 19% darker");
            Assert.AreEqual(1f - ShellShading.CreaseStrength, S(2.5f, 0f, 0f), 1e-4f, "wall meets floor");
            Assert.AreEqual(1f - ShellShading.CeilingCreaseStrength, S(2.5f, Ceiling, 0f), 1e-4f, "wall meets ceiling: the lightest");
            Assert.Greater(S(2.5f, Ceiling, 0f), S(2.5f, 0f, 0f));
            Assert.AreEqual(1f, S(2.5f, 0.13f, 0f), 1e-5f, "13 cm up the wall: gone");
            Assert.AreEqual(1f, S(2.5f, 0f, 0.13f), 1e-5f, "13 cm out on the floor: gone");
            Assert.Less(S(2.5f, 0.03f, 0f), S(2.5f, 0.08f, 0f), "smooth: darker toward the crease");
            Assert.AreEqual(1f, S(2.5f, Floor, 2f), 1e-5f, "the floor mid-room");
            Assert.AreEqual(1f, S(2.5f, Ceiling, 2f), 1e-5f, "the ceiling mid-room");
            Assert.GreaterOrEqual(S(0f, 0f, 0f), 1f - ShellShading.CreaseStrength - 1e-5f, "the darkest point: 19%, never more");
            Assert.LessOrEqual(ShellShading.Grain, 0.008f, "grain cut hard (was 0.025)");
        }

        [Test]
        public void NoRoom_NoShading()
        {
            Assert.AreEqual(1f, ShellShading.Shade(Vector3.zero, null, 0f, 2.5f));
            Assert.AreEqual(1f, ShellShading.Shade(Vector3.zero, new List<Vec2> { new Vec2(0f, 0f), new Vec2(1f, 0f) }, 0f, 2.5f));
        }

        // The shader's loop has a constant bound (16): the worst case is a 16-corner outline; more is not shaded.
        [Test]
        public void TheCornerLoop_IsBounded_SixteenAtMost()
        {
            List<Vec2> Circle(int k)
            {
                var c = new List<Vec2>();
                for (int i = 0; i < k; i++) c.Add(new Vec2(2f + 2f * Mathf.Cos(i * 2f * Mathf.PI / k), 2f + 2f * Mathf.Sin(i * 2f * Mathf.PI / k)));
                return c;
            }
            Assert.AreEqual(ShellShading.MaxCorners, ShellShading.CornerCount(Circle(16)));
            Assert.Less(ShellShading.Shade(new Vector3(4f, 1.3f, 2f), Circle(16), 0f, 2.6f), 1f, "16 corners: shaded at a corner");
            Assert.AreEqual(0, ShellShading.CornerCount(Circle(17)), "17: off, never a wrong partial outline");
            Assert.AreEqual(1f, ShellShading.Shade(new Vector3(4f, 1.3f, 2f), Circle(17), 0f, 2.6f));
        }
    }
}

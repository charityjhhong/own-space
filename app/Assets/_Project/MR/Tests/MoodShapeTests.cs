using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.MR.Tests
{
    // Device, build e3f28ec: "furniture has no shadows ... every face lit the same". The ambient is a gradient now.
    public class MoodShapeTests
    {
        static Color At(SphericalHarmonicsL2 sh, Vector3 n)
        {
            var dirs = new[] { n.normalized };
            var result = new Color[1];
            sh.Evaluate(dirs, result);
            return result[0];
        }

        [Test]
        public void TheAmbient_IsBrighterOnTops_LessOnSides_DarkerUnderneath_AndNeverBelowZero()
        {
            var ambient = new Color(0.5f, 0.48f, 0.45f);
            MoodApplier.Trilight(ambient, out Color sky, out Color equator, out Color ground);
            Assert.Greater(sky.grayscale, equator.grayscale);
            Assert.Greater(equator.grayscale, ground.grayscale);
            Assert.AreEqual(ambient, equator);
            SphericalHarmonicsL2 sh = MoodApplier.AmbientProbe(sky, equator, ground);
            float top = At(sh, Vector3.up).grayscale, side = At(sh, Vector3.forward).grayscale, bottom = At(sh, Vector3.down).grayscale;
            float lowSide = At(sh, new Vector3(0f, -0.5f, 1f)).grayscale;
            Debug.Log($"ambient probe: top {top:F3}, side {side:F3}, low side {lowSide:F3}, bottom {bottom:F3} (flat {ambient.grayscale:F3})");
            Assert.Greater(top, side * 1.1f, "a top reads brighter than a front");
            Assert.Greater(side, bottom * 1.4f, "an underside reads clearly darker");
            Assert.Greater(side, lowSide, "a face turned down a little is a little darker");
            Assert.AreEqual(ambient.grayscale, side, ambient.grayscale * 0.2f, "the sides keep about the mood's own ambient");
            Assert.GreaterOrEqual(bottom, 0f);
            Assert.Less(top, ambient.grayscale * 1.5f, "not blown out");
        }
    }
}

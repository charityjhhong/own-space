using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device bug on f1d4fe0 (Night): every passthrough area was pure black, and the lamp glow was a hard red blob.
    public class NightLookTests
    {
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        [Test]
        public void NightPassthrough_IsADimCoolTint_NeverBlack()
        {
            // Night: brightness -0.3, contrast 0.1, tint #B9CBF2, a little sun match toward 8500 K.
            Color sun = Mathf.CorrelatedColorTemperatureToRGB(8500f);
            sun /= Mathf.Max(sun.r, Mathf.Max(sun.g, sun.b));
            MoodApplier.Grade(-0.3f, 0.1f, Hex("#B9CBF2"), Color.Lerp(Color.white, sun, 0.15f), out Vector4 scale, out Vector4 offset);
            for (int c = 0; c < 3; c++)
            {
                float dim = 0.15f * scale[c] + offset[c]; // a dimly lit real wall
                Assert.Greater(dim, 0.06f, $"channel {c}: a dim real room stays visible");
                Assert.GreaterOrEqual(0.5f * scale[c] + offset[c], 0.5f * 0.45f, $"channel {c}: mid-grey keeps about half its brightness or more");
            }
            Assert.Less(scale.x, scale.z, "still a cool tint");
            // The worst case: nothing can push it to black.
            MoodApplier.Grade(-1f, 1f, Color.black, Color.black, out scale, out offset);
            Assert.GreaterOrEqual(scale.x, MoodApplier.MinGain);
            Assert.GreaterOrEqual(offset.x, MoodApplier.MinOffset);
        }

        [Test]
        public void LampGlow_IsSoftAmber_NotRed()
        {
            Color night = LightGlows.LampColor(2200f);
            Assert.AreEqual(LightGlows.LampColor(2700f), night, "never warmer than 2700 K");
            Assert.Greater(night.g, 0.6f, "amber, not red-orange");
            Assert.Greater(night.b, 0.3f, "softened, not saturated");
            Assert.Less(LightGlows.HaloBoost, 1f, "the halo is fainter than the pool's base");
        }
    }
}

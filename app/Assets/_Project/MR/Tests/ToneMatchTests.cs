using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Realism pass H: one tunable asset, one full-screen pass on the Quest renderer.
    public class ToneMatchTests
    {
        [Test]
        public void Settings_LoadFromResources_WithGentleDefaults()
        {
            ToneMatchSettings s = ToneMatchSettings.Load();
            Assert.IsNotNull(s, "MR/Resources/ToneMatchSettings.asset");
            Vector4 g = s.Grade();
            Assert.That(g.x, Is.InRange(0.7f, 1f), "a little less saturated");
            Assert.That(g.y, Is.InRange(0.8f, 1f), "a little flatter");
            Assert.That(g.z, Is.InRange(0f, 0.04f), "very light grain");
            Assert.AreEqual(1f, g.w);
        }

        [Test]
        public void Tint_IsWhiteAtTheNeutralKelvin_WarmEveningCoolNight()
        {
            var s = ScriptableObject.CreateInstance<ToneMatchSettings>();
            try
            {
                Color neutral = s.Tint(s.NeutralKelvin);
                Assert.AreEqual(1f, neutral.r, 1e-3f); Assert.AreEqual(1f, neutral.g, 1e-3f); Assert.AreEqual(1f, neutral.b, 1e-3f);
                Color evening = s.Tint(3000f), night = s.Tint(9000f);
                Assert.Greater(evening.r, evening.b, "evening warms");
                Assert.Greater(night.b, night.r, "night cools");
                Assert.Greater(evening.b, 1f - s.Temperature - 1e-3f, "only by the Temperature share");
                s.Temperature = 0f;
                Assert.AreEqual(Color.white, s.Tint(3000f));
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void TheIncludeCompiles_AndThereIsNoExtraRenderPass()
        {
            Shader check = Shader.Find("Hidden/OwnSpace/ToneMatchCheck");
            Assert.IsNotNull(check);
            Assert.IsFalse(ShaderUtil.ShaderHasError(check), "MR/Shaders/ToneMatch.hlsl compiles in a URP shader");
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/Settings/Mobile_Renderer.asset"))
                Assert.AreNotEqual("FullScreenPassRendererFeature", o != null ? o.GetType().Name : "", "no full-screen pass on the Quest renderer");
        }

        // Realism pass: the virtual room takes the passthrough's mood tint, so the real desk and the room read as one space.
        [Test]
        public void TheVirtualRoom_TakesTheMoodsPassthroughTint_NotDarker()
        {
            var settings = ScriptableObject.CreateInstance<ToneMatchSettings>();
            try
            {
                Color day = settings.Tint(6000f, Color.white);
                Assert.AreEqual(settings.Tint(6000f), day, "Afternoon (white tint): as before");
                Color night = settings.Tint(6000f, new Color(0xB9 / 255f, 0xCB / 255f, 0xF2 / 255f));
                Assert.Greater(night.b / night.r, day.b / day.r + 0.1f, "Night: cooler, like the real room");
                Assert.AreEqual(Mathf.Max(day.r, Mathf.Max(day.g, day.b)), Mathf.Max(night.r, Mathf.Max(night.g, night.b)), 0.05f, "not darker: brightness follows the lights");
                Color evening = settings.Tint(6000f, new Color(1f, 0xD7 / 255f, 0xB0 / 255f));
                Assert.Greater(evening.r / evening.b, day.r / day.b + 0.1f, "Evening: warmer");
            }
            finally { Object.DestroyImmediate(settings); }
        }
    }
}

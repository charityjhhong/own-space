using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: real things in front of the virtual room. The mask says where real surfaces are kept real.
    public class RealOcclusionTests
    {
        const int Size = RealOcclusion.MaskSize;

        static Color32 At(Color32[] px, float x, float z, float minX, float minZ, float maxX, float maxZ)
        {
            int cx = Mathf.FloorToInt((x - minX) / (maxX - minX) * Size), cz = Mathf.FloorToInt((z - minZ) / (maxZ - minZ) * Size);
            return px[cz * Size + cx];
        }

        [Test]
        public void Mask_ZoneIsR_KeptPieceTopPlusBandIsG_BorderIsEmpty()
        {
            var zone = new List<Vec2> { new Vec2(-1f, -1f), new Vec2(1f, -1f), new Vec2(1f, 1f), new Vec2(-1f, 1f) };
            var dresser = new[] { new Vec2(2f, 2f), new Vec2(3f, 2f), new Vec2(3f, 2.5f), new Vec2(2f, 2.5f) };
            float top = 0.9f + RealOcclusion.KeptBand;
            var px = new Color32[Size * Size];
            int count = RealOcclusion.Fill(px, Size, -4f, -4f, 4f, 4f, zone, new List<Vec2[]> { dresser }, new List<float> { top });

            Assert.AreEqual(255, At(px, 0f, 0f, -4f, -4f, 4f, 4f).r, "inside my zone");
            Assert.AreEqual(0, At(px, 0f, 0f, -4f, -4f, 4f, 4f).g);
            Assert.AreEqual(0, At(px, 1.5f, 0f, -4f, -4f, 4f, 4f).r, "outside it: the real wall there never cuts the virtual room");
            Color32 kept = At(px, 2.5f, 2.25f, -4f, -4f, 4f, 4f);
            Assert.AreEqual(0, kept.r);
            Assert.AreEqual(top, kept.g / 255f * RealOcclusion.KeptScale, 0.02f, "the band above a kept-real piece");
            Assert.AreEqual(0, px[0].r + px[Size - 1].r + px[Size * Size - 1].g, "border texels stay empty (clamped reads outside)");
            Assert.AreEqual(4f / 64f * Size * Size, count, Size * 2f, "about 2 x 2 m of 8 x 8 m");
        }

        // Device feedback on 1b913d6: "when I redesign my desk, the things on it disappear".
        [Test]
        public void Mask_RedesignedDesk_OnlyTheBandAboveItsTopIsReal_EvenInMyZone()
        {
            var zone = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 2f), new Vec2(-2f, 2f) };
            var desk = new[] { new Vec2(-0.6f, 0f), new Vec2(0.6f, 0f), new Vec2(0.6f, 0.6f), new Vec2(-0.6f, 0.6f) };
            float top = 0.75f;
            var px = new Color32[Size * Size];
            RealOcclusion.Fill(px, Size, -4f, -4f, 4f, 4f, zone, new List<Vec2[]> { desk },
                new List<float> { top + RealOcclusion.DeskBand }, new List<float> { top + RealOcclusion.TopBias }, new List<bool> { true });
            Color32 over = At(px, 0f, 0.3f, -4f, -4f, 4f, 4f);
            Assert.AreEqual(255, over.a, "over the redesigned desk: band only");
            Assert.AreEqual(top + RealOcclusion.TopBias, over.b / 255f * RealOcclusion.KeptScale, 0.02f, "the real top plane stays virtual");
            Assert.AreEqual(top + RealOcclusion.DeskBand, over.g / 255f * RealOcclusion.KeptScale, 0.02f, "a laptop or cup shows");
            Color32 beside = At(px, 1.5f, 1.5f, -4f, -4f, 4f, 4f);
            Assert.AreEqual(255, beside.r);
            Assert.AreEqual(0, beside.a, "elsewhere in my zone: everything real, as before");
            Assert.Greater(RealOcclusion.BedBand, RealOcclusion.DeskBand, "a person lying on the bed");
        }

        [Test]
        public void Mask_NoZoneYet_IsEmpty()
        {
            var px = new Color32[Size * Size];
            Assert.AreEqual(0, RealOcclusion.Fill(px, Size, 0f, 0f, 4f, 4f, new List<Vec2>(), new List<Vec2[]>(), new List<float>()));
        }

        [Test]
        public void Shader_AndRendererPass_AreSetUp()
        {
            Assert.IsNotNull(Shader.Find("OwnSpace/RealOcclusion"));
            foreach (string path in new[] { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" })
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(path);
                var pass = data.rendererFeatures.Find(f => f != null && f.name == RealOcclusion.FeatureName) as UnityEngine.Rendering.Universal.RenderObjects;
                Assert.IsNotNull(pass, path);
                Assert.IsFalse(pass.isActive, "off in the asset: costs nothing without depth");
                Assert.AreEqual("OwnSpace/RealOcclusion", pass.settings.overrideMaterial.shader.name);
                Assert.IsFalse(pass.settings.enableWrite);
                CollectionAssert.AreEqual(new[] { "UniversalForward" }, pass.settings.filterSettings.PassNames,
                    "content's lit surfaces only: never the passthrough holes (device shot 115520)");
            }
        }

        // Lead, #226: a development-build switch, saved. The human: off by default, and an older "on" does not carry over.
        [Test]
        public void Switch_IsSaved_DefaultsOff_AndShowsOnlyWhenWired()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ownspace-occ-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(dir);
            try
            {
                string path = System.IO.Path.Combine(dir, UiPrefs.FileName);
                Assert.IsFalse(UiPrefs.LoadOcclusion(path), "no file: off");
                System.IO.File.WriteAllText(path, "{\"version\":1,\"theme\":\"dark\",\"scale\":1.2,\"occlusion\":true}");
                Assert.IsFalse(UiPrefs.LoadOcclusion(path), "an older file with the old switch on: still off");
                Assert.IsTrue(UiPrefs.SaveOcclusion(path, true));
                Assert.IsTrue(UiPrefs.LoadOcclusion(path));
                Assert.IsTrue(UiPrefs.SaveOcclusion(path, false));
                Assert.IsFalse(UiPrefs.LoadOcclusion(path));
                Assert.AreEqual(UiPrefs.Dark, UiPrefs.LoadTheme(path), "the theme is kept");
            }
            finally { System.IO.Directory.Delete(dir, true); }

            var settings = new SettingsPage();
            int plain = settings.Count;
            bool on = true;
            settings.IsOcclusionOn = () => on;
            settings.SetOcclusion = v => on = v;
            Assert.AreEqual(plain + 2, settings.Count, "development builds: one more row under Design (its text and its switch), no Developer heading");
            int sw = settings.Count - 1;
            var card = new OwnSpace.UI.CardInfo();
            settings.Describe(sw, ref card);
            Assert.AreEqual(SettingsPage.OcclusionOn, card.Title);
            Assert.IsTrue(settings.Press(sw));
            Assert.IsFalse(on);
            card = new OwnSpace.UI.CardInfo();
            settings.Describe(sw, ref card);
            Assert.AreEqual(SettingsPage.OcclusionOff, card.Title);
        }

        // Device bug: depth "not supported" on a Quest 3. On Unity 6 + OpenXR, Meta's depth manager needs Unity's
        // "Meta Quest: Occlusion" feature (and "Meta Quest: Session", which it requires); nothing else from that package.
        [Test]
        public void MetaQuestOcclusion_IsOnForAndroid()
        {
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(UnityEditor.BuildTargetGroup.Android);
            var occlusion = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.AROcclusionFeature>();
            var session = settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARSessionFeature>();
            Assert.IsNotNull(occlusion, "Meta Quest: Occlusion is registered in the Android OpenXR settings");
            Assert.IsTrue(occlusion.enabled, "and on");
            Assert.IsNotNull(session);
            Assert.IsTrue(session.enabled, "Meta Quest: Session, required by Occlusion, is on");
            Assert.IsFalse(settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARCameraFeature>().enabled, "passthrough stays with the Meta XR Core SDK");
            Assert.IsFalse(settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARPlaneFeature>().enabled, "scene stays with MRUK");
            Assert.IsFalse(settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARAnchorFeature>().enabled, "anchors stay with the Meta XR Core SDK");
            Assert.AreEqual("XR_META_environment_depth", RealOcclusion.DepthExtensionName);
            Assert.Greater(RealOcclusion.GiveUpSeconds, RealOcclusion.RetrySeconds, "it asks again after start (the permission may come later)");
        }

        // Device, build dd3521d: with Real occlusion on the room was magenta. OwnSpace/RealOcclusion compiled on the Mac
        // (Metal) and not for the headset, so the pass drew with the error shader. This compiles our shaders for Android.
        [Test]
        public void OurShaders_CompileForTheHeadset()
        {
            if (!UnityEditor.BuildPipeline.IsBuildTargetSupported(UnityEditor.BuildTargetGroup.Android, UnityEditor.BuildTarget.Android))
                Assert.Ignore("Android build support is not installed in this Editor.");
            System.Collections.Generic.List<string> errors = OwnSpace.MR.Editor.ShaderDeviceCheck.Check();
            Assert.IsEmpty(errors, string.Join("\n", errors));
            CollectionAssert.Contains(OwnSpace.MR.Editor.ShaderDeviceCheck.Shaders, "OwnSpace/RealOcclusion");
        }

        // Device, build dd3521d: the headset reported 1750 MiB of GPU memory for the app; this line says what holds it.
        [Test]
        public void MemoryReport_GivesTheTotalAndTheLargestFirst()
        {
            var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long>>
            {
                new System.Collections.Generic.KeyValuePair<string, long>("small", 1048576),
                new System.Collections.Generic.KeyValuePair<string, long>("big", 3 * 1048576),
                new System.Collections.Generic.KeyValuePair<string, long>("mid", 2 * 1048576),
            };
            Assert.AreEqual("things: 3, 6.0 MiB; largest: big 3.0, mid 2.0", MemoryReport.Summarise("things", items, 2));
            Assert.AreEqual("none: 0, 0.0 MiB", MemoryReport.Summarise("none", new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, long>>(), 8));
        }

        // Device, build d3a6dcc: with the switch off the depth manager still asked for depth images every frame (LIMIT_REACHED).
        // The human: eye resolution 1.2x (Meta's default 1.6 held about 1.7 GB of GL memory, build d3a6dcc).
        [Test]
        public void EyeTextures_AreAllocatedAt1Point2_OnEveryQuest()
        {
            string scene = System.IO.File.ReadAllText("Assets/_Project/Scenes/Main.unity").Replace("\r\n", "\n");
            StringAssert.Contains("  quest3MaxDynamicResolutionScale: 1.2\n", scene, "run OwnSpace.MR.Editor.EyeResolutionSetup.Apply");
            StringAssert.Contains("  quest2MaxDynamicResolutionScale: 1.2\n", scene);
            StringAssert.Contains("  _enableDynamicResolution: 1\n", scene);
            var pipeline = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(OwnSpace.MR.Editor.EyeResolutionSetup.AndroidPipelinePath);
            Assert.AreEqual(OwnSpace.MR.Editor.EyeResolutionSetup.Max, pipeline.renderScale, 1e-4f, "the URP asset agrees with OVRManager at start");
        }

        [Test]
        public void DepthIsAskedForOnlyWhileTheSwitchIsOn()
        {
            Assert.IsTrue(RealOcclusion.RunsDepth(true));
            Assert.IsFalse(RealOcclusion.RunsDepth(false));
        }

        [Test]
        public void Component_SwitchDefaultsOff_AndDepthIsNotAskedFor()
        {
            var go = new GameObject("Occ");
            try
            {
                var occ = go.AddComponent<RealOcclusion>();
                Assert.IsFalse(occ.UserEnabled);
                Assert.AreEqual(RealOcclusion.DepthState.Off, occ.State, "off: depth is not started, so it is neither Starting nor Not available");
                occ.UserEnabled = true;
                Assert.IsTrue(occ.UserEnabled);
                Assert.AreEqual(RealOcclusion.DepthState.Starting, occ.State, "on: it starts asking");
                occ.UserEnabled = false;
                Assert.IsFalse(occ.Active);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

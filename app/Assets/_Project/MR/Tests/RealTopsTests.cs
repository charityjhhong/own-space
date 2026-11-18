using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The real top of a redesigned desk or bed shows through the design without depth (flat things included).
    public class RealTopsTests
    {
        static readonly CatalogItem Desk = new CatalogItem { id = "desk", kind = "desk", size = new Vec3(1.2f, 0.75f, 0.6f) };
        static FurnitureAnchor Anchor(FurnitureRole role) =>
            new FurnitureAnchor("a", role, new OrientedBox(new Vec3(1f, 0.36f, 2f), new Vec3(1.2f, 0.72f, 0.6f), 30f));

        [Test]
        public void Default_IsTheDesign_RealOnlyWhenChosen_AndTheSwitchFlipsWhatShows()
        {
            Assert.IsFalse(RealTops.Effective(RealTops.Auto, false), "the human (26a0806): the desk top starts on Design");
            Assert.IsFalse(RealTops.Effective(RealTops.Auto, true));
            Assert.IsTrue(RealTops.Effective(RealTops.On, true), "a piece that chose Real keeps it");
            Assert.IsFalse(RealTops.Effective(RealTops.Off, false));
            Assert.AreEqual(RealTops.On, RealTops.Toggled(RealTops.Auto, false), "one press from the default: Real");
            Assert.AreEqual(RealTops.Off, RealTops.Toggled(RealTops.On, true));
            Assert.AreEqual(RealTops.On, RealTops.Toggled(RealTops.Off, false));
            Assert.IsTrue(RealTops.Applies(FurnitureRole.Table));
            Assert.IsTrue(RealTops.Applies(FurnitureRole.Bed));
            Assert.IsFalse(RealTops.Applies(FurnitureRole.Storage));
        }

        // The shipped default: depth occlusion on whenever depth runs; the real top wherever it does not (or is switched off).
        [Test]
        public void OutOfTheBox_OcclusionIsOn_AndTheRealTopCoversEveryOtherCase()
        {
            string missing = System.IO.Path.Combine(Application.temporaryCachePath, "no-such-prefs-file.json");
            Assert.IsFalse(UiPrefs.LoadOcclusion(missing), "no saved choice: real occlusion is off (the human)");
            Assert.IsFalse(RealOcclusion.EnabledFor(developmentBuild: false, savedSwitch: false), "a release build never runs it");
            Assert.IsFalse(RealOcclusion.EnabledFor(false, true), "not even if a development build saved 'on' on this headset");
            Assert.IsFalse(RealOcclusion.EnabledFor(developmentBuild: true, savedSwitch: false));
            Assert.IsTrue(RealOcclusion.EnabledFor(true, true), "the development experiment switch still works");
            var go = new GameObject("Occ");
            try { Assert.IsFalse(go.AddComponent<RealOcclusion>().UserEnabled); }
            finally { Object.DestroyImmediate(go); }
            // pass active = depth Running and the switch on; anything else leaves the default real top on
            Assert.IsFalse(RealTops.Effective(RealTops.Auto, depthRunning: true), "out of the box the design's top shows (26a0806)");
            Assert.IsFalse(RealTops.Effective(RealTops.Auto, depthRunning: false), "with or without depth");
        }

        [Test]
        public void Box_IsTheDesignsTopInsetByTheRim_FromJustAboveTheRealTopUpToTheBand()
        {
            var choice = new AnchorChoice { xScale = 1.1f, yScale = 1f, zScale = 1f, rotated90 = true };
            var adjust = new AnchorAdjust { anchorId = "a", dx = 0.1f, dz = -0.05f, yaw = 15f, dd = 0.05f };
            RealTops.Box box = RealTops.BoxFor(Anchor(FurnitureRole.Table), Desk, choice, adjust);
            Assert.AreEqual(1.2f * 1.1f - 2f * RealTops.Rim, box.size.x, 1e-4f);
            Assert.AreEqual(0.65f - 2f * RealTops.Rim, box.size.z, 1e-4f, "the user's extra depth counts");
            Assert.AreEqual(0.72f + RealOcclusion.TopBias, box.center.y, 1e-4f, "just above the design's top, so the top gives way");
            Assert.AreEqual(0f, box.size.y, "flat: a ray through it ends on the desk, never on the wall behind (build 2dc475d)");
            Assert.AreEqual(1.1f, box.center.x, 1e-4f);
            Assert.AreEqual(1.95f, box.center.z, 1e-4f);
            Assert.AreEqual(30f + 90f + 15f, box.yaw, 1e-4f, "turned like the shown design");
            RealTops.Box bed = RealTops.BoxFor(Anchor(FurnitureRole.Bed), Desk, choice, default);
            Assert.AreEqual(0f, bed.size.y, "a bed's window is flat too");
        }

        [Test]
        public void TheSetting_IsSavedPerPiece_WithTheAdjustments()
        {
            string design = System.IO.Path.Combine(Application.temporaryCachePath, "realtops-test-design.json");
            string file = AnchorAdjusts.PathFor(design);
            try
            {
                var adjusts = new AnchorAdjusts();
                adjusts.Use(design);
                var a = new AnchorAdjust { anchorId = "desk-1", realTop = RealTops.Off };
                Assert.IsFalse(a.IsEmpty, "an explicit choice is kept even with no other adjustment");
                adjusts.Set(a);
                var again = new AnchorAdjusts();
                again.Use(design);
                Assert.AreEqual(RealTops.Off, again.Get("desk-1").realTop);
                Assert.AreEqual(RealTops.Auto, again.Get("other").realTop, "untouched pieces follow the default");
            }
            finally { if (System.IO.File.Exists(file)) System.IO.File.Delete(file); }
        }

        [Test]
        public void Mesh_IsOneFlatRectanglePerPiece_FacingDown()
        {
            RealTops.Box box = RealTops.BoxFor(Anchor(FurnitureRole.Table), Desk, new AnchorChoice { xScale = 1f, yScale = 1f, zScale = 1f }, default);
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            RealTops.Append(box, vertices, triangles);
            RealTops.Append(box, vertices, triangles);
            Assert.AreEqual(8, vertices.Count);
            Assert.AreEqual(12, triangles.Count);
            for (int i = 0; i < vertices.Count; i++) Assert.AreEqual(box.center.y, vertices[i].y, 1e-5f, "every corner at the window's height: nothing reaches up the wall");
            for (int t = 0; t < triangles.Count; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Assert.Less(Vector3.Cross(b - a, c - a).y, 0f, $"triangle {t / 3} faces down (drawn with front faces culled, so seen from above)");
            }
            float width = Vector3.Distance(vertices[0], vertices[1]), depth = Vector3.Distance(vertices[1], vertices[2]);
            Assert.AreEqual(box.size.x, width, 1e-4f);
            Assert.AreEqual(box.size.z, depth, 1e-4f);
        }
    }
}

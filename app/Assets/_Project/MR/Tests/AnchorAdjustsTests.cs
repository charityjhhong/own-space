using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: the redesigned desk did not line up with the real one; the user fine-tunes it within core's limits.
    public class AnchorAdjustsTests
    {
        static readonly CatalogItem Desk = new CatalogItem { id = "desk", kind = "desk", size = new Vec3(1.2f, 0.75f, 0.6f) };

        [Test]
        public void Resize_StaysWithinCoresFootprintRule()
        {
            var choice = new AnchorChoice { xScale = 1.1f, zScale = 1f, yScale = 1f };
            var a = new AnchorAdjust { anchorId = "a" };
            a = AnchorAdjusts.Resize(a, Desk, choice, true, 1);
            Assert.AreEqual(AnchorAdjusts.Step, a.dw, 1e-5f, "+5 cm wider");
            for (int i = 0; i < 20; i++) a = AnchorAdjusts.Resize(a, Desk, choice, true, 1);
            Assert.AreEqual(1.3f, AnchorAdjusts.Scale(Desk, choice, a).x, 1e-4f, "never past 1.3x the item's own width");
            for (int i = 0; i < 40; i++) a = AnchorAdjusts.Resize(a, Desk, choice, true, -1);
            Assert.AreEqual(AnchorAdjusts.MinManualScale, AnchorAdjusts.Scale(Desk, choice, a).x, 1e-4f, "by hand, down to 0.7x the item's own width");
            Assert.IsFalse(AnchorAdjusts.CanResize(a, Desk, choice, true, -1), "Narrower greys out at the limit");
            Assert.IsTrue(AnchorAdjusts.CanResize(a, Desk, choice, true, 1));
        }

        // Device (build 0ddb26d): the real desk was shallower than the design, core's depth fit was 1, and Shallower did nothing.
        [Test]
        public void Resize_ShallowerThanTheDesign_WhenTheRealDeskIsShallower()
        {
            var choice = new AnchorChoice { xScale = 1f, zScale = 1f, yScale = 1f };
            var a = new AnchorAdjust { anchorId = "a" };
            Assert.IsTrue(AnchorAdjusts.CanResize(a, Desk, choice, false, -1));
            a = AnchorAdjusts.Resize(a, Desk, choice, false, -1);
            Assert.AreEqual(-AnchorAdjusts.Step, a.dd, 1e-5f, "5 cm shallower");
            for (int i = 0; i < 20; i++) a = AnchorAdjusts.Resize(a, Desk, choice, false, -1);
            Assert.AreEqual(AnchorAdjusts.MinManualScale, AnchorAdjusts.Scale(Desk, choice, a).z, 1e-4f, "never below 0.7x the item's own depth");
            Assert.IsFalse(AnchorAdjusts.CanResize(a, Desk, choice, false, -1));
            for (int i = 0; i < 40; i++) a = AnchorAdjusts.Resize(a, Desk, choice, false, 1);
            Assert.AreEqual(1.3f, AnchorAdjusts.Scale(Desk, choice, a).z, 1e-4f, "the upper limit stays 1.3x");
            Assert.IsFalse(AnchorAdjusts.CanResize(a, Desk, choice, false, 1));
        }

        [Test]
        public void Turn_Wraps_AndNudge_StaysNear()
        {
            var a = new AnchorAdjust();
            for (int i = 0; i < 5; i++) a = AnchorAdjusts.Turn(a);
            Assert.AreEqual(1, a.turns);
            a = AnchorAdjusts.Nudge(a, 1f, 0f);
            Assert.AreEqual(AnchorAdjusts.MaxNudge, new Vector2(a.dx, a.dz).magnitude, 1e-4f, "at most 30 cm from the real box");
        }

        [Test]
        public void SavedPerRoom_NextToTheDesign_AndClearedByReset()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ownspace-adjust-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string design = Path.Combine(dir, "design-room.json");
                var store = new AnchorAdjusts();
                store.Use(design);
                store.Set(new AnchorAdjust { anchorId = "desk-1", turns = 1, dw = 0.05f });
                var again = new AnchorAdjusts();
                again.Use(design);
                Assert.AreEqual(1, again.Get("desk-1").turns);
                Assert.AreEqual(0.05f, again.Get("desk-1").dw, 1e-5f);
                Assert.IsTrue(again.Get("other").IsEmpty);
                AnchorAdjusts.Clear(design);
                Assert.IsFalse(File.Exists(AnchorAdjusts.PathFor(design)));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}

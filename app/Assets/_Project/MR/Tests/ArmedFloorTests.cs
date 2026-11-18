using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 5: an armed card placed nothing on a thin outside sliver, silently.
    public class ArmedFloorTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };
        static readonly List<Vec2> Zone = new List<Vec2> { new Vec2(0f, 0f), new Vec2(3.6f, 0f), new Vec2(3.6f, 3f), new Vec2(0f, 3f) }; // a 40 cm sliver at x 3.6..4

        [Test]
        public void TheSliver_Places()
        {
            Assert.IsTrue(OutsidePlacer.ArmedFloor(true, new Vector3(3.8f, 0f, 1.5f), Room, Zone, out Vector3 spot, out string why));
            Assert.AreEqual(3.8f, spot.x, 1e-4f);
            Assert.IsNull(why);
        }

        [Test]
        public void AHitPastTheWall_IsPulledBackIntoTheSliver()
        {
            Assert.IsTrue(OutsidePlacer.ArmedFloor(true, new Vector3(4.6f, 0f, 1.5f), Room, Zone, out Vector3 spot, out _), "the virtual wall hid the floor edge");
            Assert.That(spot.x, Is.InRange(3.6f, 4f));
        }

        [Test]
        public void Misses_SayWhy()
        {
            Assert.IsFalse(OutsidePlacer.ArmedFloor(true, new Vector3(2f, 0f, 1.5f), Room, Zone, out _, out string inside));
            Assert.AreEqual(OutsidePlacer.InsideZone, inside);
            Assert.IsFalse(OutsidePlacer.ArmedFloor(false, Vector3.zero, Room, Zone, out _, out string none));
            Assert.AreEqual(OutsidePlacer.AimAtFloor, none);
        }

        [Test]
        public void PlacementSeesThePinch_BeforeEditModeAndTheHideTool()
        {
            int Order(System.Type t) => t.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            Assert.Less(Order(typeof(OutsidePlacer)), Order(typeof(EditMode)));
            Assert.Less(Order(typeof(OutsidePlacer)), Order(typeof(HideTool)));
            Assert.Greater(Order(typeof(OutsidePlacer)), Order(typeof(OwnSpace.UI.ToolPanel)), "the panel still comes first");
        }

        // Device batch 9: an armed "Draw a box" lost the floor pinch to EditMode when the ray crossed a real box.
        [Test]
        public void DrawingSeesThePinch_BeforeEditMode_AfterAnArmedCard()
        {
            int Order(System.Type t) => t.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            Assert.Less(Order(typeof(HideTool)), Order(typeof(EditMode)));
            Assert.Greater(Order(typeof(HideTool)), Order(typeof(OutsidePlacer)));
            Assert.Greater(Order(typeof(HideTool)), Order(typeof(OwnSpace.UI.ToolPanel)));
            StringAssert.EndsWith("· Cancel", TidyUpPage.DrawBanner);
        }
    }
}

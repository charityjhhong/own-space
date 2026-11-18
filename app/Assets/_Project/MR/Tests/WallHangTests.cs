using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: frames and wall art hang on walls, not the floor.
    public class WallHangTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 3f), new Vec2(-2f, 3f) };

        [Test]
        public void RayWall_FindsTheNearestWallInFront_WithinItsHeight()
        {
            var eye = new Vector3(0f, 1.2f, 0f);
            Assert.IsTrue(OutsidePlacer.RayWall(new Ray(eye, Vector3.forward), Room, 0f, 3f, out Vector3 hit));
            Assert.AreEqual(new Vector3(0f, 1.2f, 3f), hit);
            Assert.IsTrue(OutsidePlacer.RayWall(new Ray(eye, new Vector3(1f, 0f, 0.2f).normalized), Room, 0f, 3f, out hit));
            Assert.AreEqual(2f, hit.x, 1e-4f);
            Assert.IsFalse(OutsidePlacer.RayWall(new Ray(eye, Vector3.up), Room, 0f, 3f, out _), "the ceiling is no wall");
            Assert.IsFalse(OutsidePlacer.RayWall(new Ray(eye, new Vector3(0f, 1f, 1f).normalized), Room, 0f, 1.5f, out _), "above the wall's height");
            Assert.IsFalse(OutsidePlacer.RayWall(new Ray(eye, Vector3.forward), null, 0f, 3f, out _));
        }

        [Test]
        public void HangBanner_SaysPinchAWall_AndWallPiecesCastNoFloorShadow()
        {
            Assert.AreEqual("Pinch a wall to hang Oak frame · Cancel", PanelHost.HangBanner("Oak frame"));
            var art = new CatalogItem { id = "art", kind = "wall-art", size = new Vec3(0.6f, 0.4f, 0.03f) };
            var mirror = new CatalogItem { id = "m", kind = "mirror", size = new Vec3(0.5f, 0.9f, 0.03f) };
            Assert.IsTrue(PlacementRules.IsWallMounted(mirror));
            Assert.IsFalse(ContactShadows.Grounded(art));
            Assert.IsFalse(ContactShadows.Grounded(mirror));
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using UnityEngine.TestTools.Constraints;

namespace OwnSpace.Tests
{
    /// <summary>Dorm fake; my zone is side "a" (x &lt;= 0), so the free floor is side "b" (0 &lt;= x &lt;= 1.8, 0 &lt;= z &lt;= 5).</summary>
    public class PlacementRulesTests
    {
        static readonly Catalog Items = new Catalog(new[]
        {
            Item("sofa", 1.8f, 0.8f, 0.9f, "outside"),
            Item("rug", 2.0f, 0.015f, 1.4f, "outside"),
            Item("lamp", 0.4f, 1.55f, 0.4f, "outside"),
            Item("desk", 1.2f, 0.74f, 0.6f, "my"),
        });

        IRoomInfo _room;
        DesignState _state;

        static CatalogItem Item(string id, float w, float h, float d, string zone) => new CatalogItem
        {
            id = id, role = FurnitureRole.Other, size = new Vec3(w, h, d), zones = new List<string> { zone },
        };

        static FreePlacement At(string item, float x, float z, float yaw = 0f, string id = "new") =>
            new FreePlacement { placementId = id, catalogItemId = item, position = new Vec3(x, 0f, z), yawDegrees = yaw };

        PlacementStatus Check(FreePlacement p, string ignore = null) => PlacementRules.Check(_room, _state, Items, p, ignore);

        [SetUp]
        public void SetUp()
        {
            _room = TestRooms.Dorm(TestRooms.SideA);
            _state = new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a") };
        }

        [Test]
        public void FlushAgainstWallAndZoneEdge_IsOk() =>
            Assert.AreEqual(PlacementStatus.Ok, Check(At("sofa", 0.9f, 2.5f)));

        [Test]
        public void Turned90_IsOk() =>
            Assert.AreEqual(PlacementStatus.Ok, Check(At("sofa", 0.9f, 2.5f, 90f)));

        [Test]
        public void CrossingIntoMyZone_IsInMyZone() =>
            Assert.AreEqual(PlacementStatus.InMyZone, Check(At("sofa", 0.8f, 2.5f)));

        [Test]
        public void PastTheWall_IsOutsideRoom()
        {
            Assert.AreEqual(PlacementStatus.OutsideRoom, Check(At("sofa", 1.0f, 2.5f)));
            Assert.AreEqual(PlacementStatus.OutsideRoom, Check(At("sofa", 0.9f, 4.7f)));
        }

        [Test]
        public void InFrontOfDoor_BlocksDoorUnlessFlat()
        {
            Assert.AreEqual(PlacementStatus.BlocksDoor, Check(At("sofa", 0.9f, 1.0f)));
            Assert.AreEqual(PlacementStatus.Ok, Check(At("sofa", 0.9f, 1.4f)), "past the 0.9 m clearance");
            Assert.AreEqual(PlacementStatus.Ok, Check(At("rug", 0.9f, 1.0f, 90f)));
        }

        [Test]
        public void TallPieceAtWindow_BlocksWindowButLowPieceDoesNot()
        {
            Assert.AreEqual(PlacementStatus.BlocksWindow, Check(At("lamp", 0.9f, 4.7f)));
            Assert.AreEqual(PlacementStatus.Ok, Check(At("rug", 0.9f, 4.0f, 90f)));
        }

        [Test]
        public void OverlappingAnotherPiece_Overlaps_ButNotItselfOrARug()
        {
            _state.placements.Add(At("sofa", 0.9f, 2.5f, 0f, "p1"));
            _state.placements.Add(At("rug", 0.9f, 3.5f, 0f, "r1"));
            Assert.AreEqual(PlacementStatus.Overlaps, Check(At("lamp", 1.2f, 2.5f)));
            Assert.AreEqual(PlacementStatus.Ok, Check(At("lamp", 1.2f, 3.15f)), "touching is fine");
            Assert.AreEqual(PlacementStatus.Ok, Check(At("sofa", 0.9f, 2.6f, 0f, "moved"), "p1"));
            Assert.AreEqual(PlacementStatus.Ok, Check(At("lamp", 1.0f, 3.5f)), "on the rug");
            Assert.AreEqual(PlacementStatus.Ok, Check(At("rug", 0.9f, 2.5f, 90f, "r2")), "rug under the sofa");
        }

        [Test]
        public void IdenticalFootprints_Overlap()
        {
            _state.placements.Add(At("lamp", 1.0f, 2.0f, 0f, "l1"));
            Assert.AreEqual(PlacementStatus.Overlaps, Check(At("lamp", 1.0f, 2.0f)));
            Assert.AreEqual(PlacementStatus.Overlaps, Check(At("lamp", 1.2f, 2.0f)), "half overlap with collinear edges");
        }

        [Test]
        public void BadInput_IsReported()
        {
            Assert.AreEqual(PlacementStatus.UnknownItem, Check(At("nope", 0.9f, 2.5f)));
            Assert.AreEqual(PlacementStatus.NotForOutside, Check(At("desk", 0.9f, 2.5f)));
            Assert.AreEqual(PlacementStatus.InvalidPose, Check(At("lamp", float.NaN, 2.5f)));
        }

        [Test]
        public void NoScanAndNoZone_AnywhereIsOk()
        {
            _room = FakeRoomInfo.Empty();
            _state = new DesignState();
            Assert.AreEqual(PlacementStatus.Ok, Check(At("sofa", -20f, 7f, 33f)));
        }

        [Test]
        public void Check_DoesNotAllocate()
        {
            _state.placements.Add(At("sofa", 0.9f, 2.5f, 0f, "p1"));
            var p = At("lamp", 1.2f, 1.6f);
            // Warm up well past JIT and first-use setup: right after a recompile one call was not always enough.
            for (int i = 0; i < 20; i++) Check(p);
            TestDelegate check = () => Check(p);
            Assert.That(check, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }
    }
}

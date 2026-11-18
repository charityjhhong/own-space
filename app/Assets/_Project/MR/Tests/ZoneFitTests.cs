using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR.Tests
{
    // Device batch 5: the proposed zone holds the user's desk (and its chair) with ~0.3 m to spare, inside the room.
    public class ZoneFitTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(6f, 0f), new Vec2(6f, 5f), new Vec2(0f, 5f) };

        static FurnitureAnchor Piece(string id, FurnitureRole role, float x, float z, float w, float d, float yaw = 0f) =>
            new FurnitureAnchor(id, role, new OrientedBox(new Vec3(x, 0.4f, z), new Vec3(w, 0.75f, d), yaw));

        static List<Vec2> HalfTheDesk() => new List<Vec2> { new Vec2(2f, 1f), new Vec2(5.3f, 1f), new Vec2(5.3f, 4f), new Vec2(2f, 4f) };

        static void AssertHoldsWithMargin(List<Vec2> zone, FurnitureAnchor piece)
        {
            OrientedBox b = piece.Box;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var p = new Vec2(b.center.x + sx * (b.size.x * 0.5f + ZoneFit.Margin - 0.01f), b.center.z + sz * (b.size.z * 0.5f + ZoneFit.Margin - 0.01f));
                Assert.IsTrue(Zones.Contains(zone, p), $"{piece.Id}: {p.x:F2},{p.y:F2} inside with the margin");
            }
        }

        [Test]
        public void ADeskHalfOutside_EndsUpInsideWithTheMargin_AndInsideTheRoom()
        {
            var desk = Piece("desk", FurnitureRole.Table, 5.3f, 2.5f, 0.6f, 1.2f);
            var zone = HalfTheDesk();
            Assert.IsFalse(ZoneFit.Holds(zone, desk), "the bug: half the desk outside");
            Assert.IsTrue(ZoneFit.Contain(zone, new[] { desk }, new Vec2(4f, 2.5f), Room));
            AssertHoldsWithMargin(zone, desk);
            foreach (Vec2 p in zone) Assert.IsTrue(p.x >= -1e-4f && p.x <= 6f + 1e-4f && p.y >= -1e-4f && p.y <= 5f + 1e-4f, "inside the room");
        }

        [Test]
        public void TheDesksChairComesToo_AFarPieceDoesNot()
        {
            var desk = Piece("desk", FurnitureRole.Table, 5.3f, 2.5f, 0.6f, 1.2f);
            var chair = Piece("chair", FurnitureRole.Other, 4.5f, 3.6f, 0.5f, 0.5f);
            var far = Piece("far", FurnitureRole.Other, 0.5f, 0.5f, 0.4f, 0.4f);
            var zone = new List<Vec2> { new Vec2(3f, 1.5f), new Vec2(5.3f, 1.5f), new Vec2(5.3f, 3f), new Vec2(3f, 3f) };
            Assert.AreSame(chair, ZoneFit.Chair(new[] { desk, chair, far }, desk));
            ZoneFit.Contain(zone, new[] { desk, chair, far }, new Vec2(4f, 2.5f), Room);
            AssertHoldsWithMargin(zone, desk);
            AssertHoldsWithMargin(zone, chair);
            Assert.IsFalse(Zones.Contains(zone, new Vec2(0.5f, 0.5f)), "a piece across the room is not pulled in");
        }

        [Test]
        public void TurnedDesk_NearestOfTwo_NoDeskLeavesTheZoneAlone()
        {
            var turned = Piece("near", FurnitureRole.Table, 3f, 4f, 1.2f, 0.6f, 30f);
            var other = Piece("other", FurnitureRole.Table, 1f, 1f, 1.2f, 0.6f);
            var zone = new List<Vec2> { new Vec2(2f, 2f), new Vec2(3f, 2f), new Vec2(3f, 3f), new Vec2(2f, 3f) };
            ZoneFit.Contain(zone, new[] { turned, other }, new Vec2(3f, 3f), Room);
            Assert.IsTrue(ZoneFit.Holds(zone, turned));

            // Product rule (desk or bed): a room with only a bed fits the zone to the bed; with neither, it is left alone.
            var bed = Piece("bed", FurnitureRole.Bed, 1f, 1f, 2f, 1.6f);
            var withBed = new List<Vec2> { new Vec2(2f, 2f), new Vec2(3f, 2f), new Vec2(3f, 3f), new Vec2(2f, 3f) };
            Assert.IsTrue(ZoneFit.Contain(withBed, new[] { bed }, new Vec2(3f, 3f), Room));
            Assert.IsTrue(ZoneFit.Holds(withBed, bed));
            var untouched = new List<Vec2>(Room);
            Assert.IsFalse(ZoneFit.Contain(untouched, new[] { Piece("shelf", FurnitureRole.Storage, 1f, 1f, 0.5f, 0.4f) }, new Vec2(3f, 3f), Room));
            Assert.AreEqual(Room, untouched);
            Assert.IsNull(ZoneFit.Chair(new[] { turned }, turned), "only a desk was scanned");
        }

        [Test]
        public void NoChair_IsSaidInTheZoneHint()
        {
            StringAssert.StartsWith(ZoneSetup.NoChairNote, ZoneSetup.HintFor(true, true));
            Assert.AreEqual(ZoneSetup.Hint, ZoneSetup.HintFor(true, false));
            Assert.AreEqual(ZoneSetup.NoScanHint, ZoneSetup.HintFor(false, true), "without a scan the scan hint wins");
        }

        // Device day (d520910): after Reset the proposal covered nearly the whole study; Add had nowhere to place.
        [Test]
        public void AZoneOverTheWholeRoom_ShrinksToTheDeskAndChair_LeavingAThirdOutside()
        {
            var desk = Piece("desk", FurnitureRole.Table, 5.3f, 2.5f, 0.6f, 1.2f);
            var chair = Piece("chair", FurnitureRole.Other, 4.6f, 2.5f, 0.5f, 0.5f);
            var zone = new List<Vec2>(Room); // the whole room
            Assert.IsTrue(ZoneFit.KeepOutside(zone, new[] { desk, chair }, new Vec2(4f, 2.5f), Room));
            float roomArea = 30f, zoneArea = System.Math.Abs(RoomGeometry.SignedArea(zone));
            Assert.LessOrEqual(zoneArea, roomArea * (1f - ZoneFit.MinOutsideShare), "at least 35% stays outside");
            AssertHoldsWithMargin(zone, desk);
            AssertHoldsWithMargin(zone, chair);
            Assert.Greater(ZoneFit.OutsideFreeArea(Room, zone, new[] { desk, chair }), ZoneFit.MinOutsideArea);

            var small = new List<Vec2> { new Vec2(4f, 1.5f), new Vec2(6f, 1.5f), new Vec2(6f, 3.5f), new Vec2(4f, 3.5f) };
            Assert.IsFalse(ZoneFit.KeepOutside(small, new[] { desk }, new Vec2(4f, 2.5f), Room), "a modest zone is left alone");
        }

        [Test]
        public void NoDesk_TheZoneShrinksAboutItsCentre()
        {
            var zone = new List<Vec2>(Room);
            Assert.IsTrue(ZoneFit.KeepOutside(zone, new FurnitureAnchor[0], new Vec2(3f, 2.5f), Room));
            Assert.AreEqual(30f * (1f - ZoneFit.MinOutsideShare), System.Math.Abs(RoomGeometry.SignedArea(zone)), 0.01f);
            Assert.IsTrue(Zones.Contains(zone, new Vec2(3f, 2.5f)), "still around the middle");
        }

        [Test]
        public void OutsideFreeArea_IsTheRoomLessTheZoneLessFurnitureOutside()
        {
            var zone = new List<Vec2> { new Vec2(0f, 0f), new Vec2(5.5f, 0f), new Vec2(5.5f, 5f), new Vec2(0f, 5f) }; // 27.5 of 30
            var outside = Piece("shelf", FurnitureRole.Storage, 5.75f, 4f, 0.5f, 1f); // 0.5 m² in the sliver
            Assert.AreEqual(2.5f, ZoneFit.OutsideFreeArea(Room, zone, new FurnitureAnchor[0]), 1e-3f);
            Assert.AreEqual(2.0f, ZoneFit.OutsideFreeArea(Room, zone, new[] { outside }), 1e-3f);
        }
    }
}

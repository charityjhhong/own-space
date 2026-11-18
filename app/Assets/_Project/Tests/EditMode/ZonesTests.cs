using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class ZonesTests
    {
        static readonly List<Vec2> Square = new List<Vec2>
        {
            new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 2), new Vec2(0, 2),
        };

        // An L shape: the square with its top-right quarter cut out.
        static readonly List<Vec2> LShape = new List<Vec2>
        {
            new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 1), new Vec2(1, 1), new Vec2(1, 2), new Vec2(0, 2),
        };

        [Test]
        public void PointInsideSquare_IsContained() =>
            Assert.IsTrue(Zones.Contains(Square, new Vec2(1, 1)));

        [Test]
        public void PointOutsideSquare_IsNotContained() =>
            Assert.IsFalse(Zones.Contains(Square, new Vec2(3, 1)));

        [Test]
        public void ClockwiseWinding_GivesSameResult()
        {
            var clockwise = new List<Vec2>(Square);
            clockwise.Reverse();
            Assert.IsTrue(Zones.Contains(clockwise, new Vec2(1, 1)));
            Assert.IsFalse(Zones.Contains(clockwise, new Vec2(-1, 1)));
        }

        [Test]
        public void ConcaveNotch_IsNotContained()
        {
            Assert.IsTrue(Zones.Contains(LShape, new Vec2(0.5f, 1.5f)));
            Assert.IsFalse(Zones.Contains(LShape, new Vec2(1.5f, 1.5f)));
        }

        [Test]
        public void DegeneratePolygon_ContainsNothing()
        {
            Assert.IsFalse(Zones.Contains(null, new Vec2(0, 0)));
            Assert.IsFalse(Zones.Contains(new List<Vec2>(), new Vec2(0, 0)));
            Assert.IsFalse(Zones.Contains(new List<Vec2> { new Vec2(0, 0), new Vec2(1, 1) }, new Vec2(0.5f, 0.5f)));
        }

        [TestCase(1f, 0f)]   // bottom edge
        [TestCase(2f, 1f)]   // right edge
        [TestCase(1f, 2f)]   // top edge
        [TestCase(0f, 1f)]   // left edge
        [TestCase(0f, 0f)]   // vertex
        [TestCase(2f, 2f)]   // vertex
        public void PointOnBoundary_IsInsideInEitherWinding(float x, float y)
        {
            var clockwise = new List<Vec2>(Square);
            clockwise.Reverse();
            Assert.IsTrue(Zones.Contains(Square, new Vec2(x, y)));
            Assert.IsTrue(Zones.Contains(clockwise, new Vec2(x, y)));
        }

        [Test]
        public void JustOutsideBoundary_IsOutside() =>
            Assert.IsFalse(Zones.Contains(Square, new Vec2(2.001f, 1f)));

        [Test]
        public void ZeroAreaOutlines_ContainNothing()
        {
            var line = new List<Vec2> { new Vec2(0, 0), new Vec2(1, 1), new Vec2(2, 2) };
            var point = new List<Vec2> { new Vec2(1, 1), new Vec2(1, 1), new Vec2(1, 1) };
            Assert.IsFalse(Zones.Contains(line, new Vec2(1, 1)));
            Assert.IsFalse(Zones.Contains(point, new Vec2(1, 1)));
        }

        [Test]
        public void DuplicatePoints_DoNotChangeTheAnswer()
        {
            var doubled = new List<Vec2> { new Vec2(0, 0), new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 2), new Vec2(2, 2), new Vec2(0, 2) };
            Assert.IsTrue(Zones.Contains(doubled, new Vec2(1, 1)));
            Assert.IsFalse(Zones.Contains(doubled, new Vec2(3, 1)));
        }

        [Test]
        public void WorldPoint_UsesFloorProjection() =>
            Assert.IsTrue(Zones.Contains(Square, new Vec3(1, 1.7f, 1)));

        [Test]
        public void DormSideA_ContainsOnlySideAFurniture()
        {
            var room = FakeRoomInfo.DormRoom();
            var zoneA = FakeRoomInfo.HalfZone("a");
            int inA = 0;
            foreach (var anchor in room.Furniture)
            {
                bool expected = anchor.Id.StartsWith("a-");
                Assert.AreEqual(expected, Zones.Contains(zoneA, anchor), anchor.Id);
                if (expected) inA++;
            }
            Assert.AreEqual(3, inA);
        }
    }

    public class FakeRoomInfoTests
    {
        [Test]
        public void DormRoom_IsSymmetricWithFiveElementsPerSide()
        {
            var room = FakeRoomInfo.DormRoom();
            Assert.IsTrue(room.HasScan);
            Assert.AreEqual(6, room.Furniture.Count);
            Assert.AreEqual(2, room.Doors.Count);
            Assert.AreEqual(2, room.Windows.Count);
            Assert.AreEqual(4, room.Walls.Count);
            Assert.AreEqual(4, room.FloorOutline.Count);

            for (int i = 0; i < 3; i++)
            {
                var a = room.Furniture[i];
                var b = room.Furniture[i + 3];
                Assert.AreEqual(a.Role, b.Role);
                Assert.AreEqual(-a.Box.center.x, b.Box.center.x, 1e-5f);
                Assert.AreEqual(a.Box.center.z, b.Box.center.z, 1e-5f);
                Assert.AreEqual(a.UsableHeight, b.UsableHeight);
            }
        }

        [Test]
        public void DormRoom_FurnitureInOrderAlongTheRoom()
        {
            var f = FakeRoomInfo.DormRoom().Furniture;
            Assert.AreEqual(FurnitureRole.Storage, f[0].Role);
            Assert.AreEqual(FurnitureRole.Table, f[1].Role);
            Assert.AreEqual(FurnitureRole.Bed, f[2].Role);
            Assert.Less(f[0].Box.center.z, f[1].Box.center.z);
            Assert.Less(f[1].Box.center.z, f[2].Box.center.z);
        }

        [Test]
        public void EmptyRoom_HasNoScanAndNoAnchors()
        {
            var room = FakeRoomInfo.Empty();
            Assert.IsFalse(room.HasScan);
            Assert.AreEqual(0, room.Furniture.Count);
            Assert.AreEqual(0, room.FloorOutline.Count);
            Assert.IsFalse(Zones.Contains(room.FloorOutline, new Vec2(0, 0)));
        }
    }
}

using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using OwnSpace.MR;

namespace OwnSpace.MR.Tests
{
    public class OpeningGuardTests
    {
        static FurnitureAnchor Anchor(IRoomInfo room, string id)
        {
            foreach (var f in room.Furniture) if (f.Id == id) return f;
            return null;
        }

        [Test]
        public void DormBedAndDesk_DoNotCoverTheWindowOrDoor()
        {
            var room = FakeRoomInfo.DormRoom();
            var bed = new CatalogItem { id = "bed", role = FurnitureRole.Bed, size = new Vec3(2.0f, 0.6f, 0.95f), usableHeight = 0.5f };
            var desk = new CatalogItem { id = "desk", role = FurnitureRole.Table, size = new Vec3(1.2f, 0.74f, 0.6f), usableHeight = 0.74f };

            foreach (var (id, item) in new[] { ("a-bed", bed), ("a-desk", desk) })
            {
                FurnitureAnchor anchor = Anchor(room, id);
                FitResult fit = FitRule.Check(anchor, item);
                Assert.IsTrue(fit.Fits, id);
                Assert.IsFalse(OpeningGuard.Covers(room, OpeningGuard.Placed(anchor, item, fit, room.FloorY)), id);
            }
        }

        [Test]
        public void TallPieceInFrontOfWindow_Covers()
        {
            var room = FakeRoomInfo.DormRoom();
            // A 2 m tall box standing against the back wall right under side a's window (x = -0.9, z = 5).
            var placed = new OrientedBox(new Vec3(-0.9f, 1.0f, 4.7f), new Vec3(1.0f, 2.0f, 0.6f), 180f);
            Assert.IsTrue(OpeningGuard.Covers(room, placed));
        }
    
        [Test]
        public void PieceOverlappingOnlyTheEdgeOfAWideDoor_Covers()
        {
            // 1.6 m wide door centred at x = 0 on the z = 0 wall (x -0.8..0.8). The piece spans
            // x 0.75..1.45: far from the door centre, but it overlaps the door's right edge.
            var room = new OneOpeningRoom(new OrientedBox(new Vec3(0f, 1f, 0f), new Vec3(1.6f, 2f, 0f), 0f));
            var piece = new OrientedBox(new Vec3(1.1f, 0.4f, 0.3f), new Vec3(0.7f, 0.8f, 0.6f), 0f);
            Assert.IsTrue(OpeningGuard.Covers(room, piece));

            var clear = new OrientedBox(new Vec3(1.5f, 0.4f, 0.3f), new Vec3(0.5f, 0.8f, 0.6f), 0f);
            Assert.IsFalse(OpeningGuard.Covers(room, clear));
        }

        [Test]
        public void Overlap_RespectsYaw()
        {
            var wall = new OrientedBox(new Vec3(0f, 1f, 0f), new Vec3(2f, 2f, 0f), 90f); // runs along z at x = 0
            var nearX = new OrientedBox(new Vec3(0.2f, 0.5f, 0f), new Vec3(0.4f, 1f, 0.4f), 0f);
            var farX = new OrientedBox(new Vec3(0.5f, 0.5f, 0f), new Vec3(0.4f, 1f, 0.4f), 0f);
            Assert.IsTrue(OpeningGuard.Overlap(wall, nearX, 0.05f));
            Assert.IsFalse(OpeningGuard.Overlap(wall, farX, 0.05f));
        }

        sealed class OneOpeningRoom : IRoomInfo
        {
            readonly System.Collections.Generic.List<RoomElement> _doors = new System.Collections.Generic.List<RoomElement>();
            static readonly System.Collections.Generic.List<RoomElement> NoElements = new System.Collections.Generic.List<RoomElement>();
            static readonly System.Collections.Generic.List<FurnitureAnchor> NoFurniture = new System.Collections.Generic.List<FurnitureAnchor>();
            static readonly System.Collections.Generic.List<Vec2> NoOutline = new System.Collections.Generic.List<Vec2>();
            public OneOpeningRoom(OrientedBox door) => _doors.Add(new RoomElement("door", door));
            public bool HasScan => true;
            public System.Collections.Generic.IReadOnlyList<Vec2> FloorOutline => NoOutline;
            public float FloorY => 0f;
            public float CeilingY => 2.5f;
            public System.Collections.Generic.IReadOnlyList<RoomElement> Walls => NoElements;
            public System.Collections.Generic.IReadOnlyList<RoomElement> Windows => NoElements;
            public System.Collections.Generic.IReadOnlyList<RoomElement> Doors => _doors;
            public System.Collections.Generic.IReadOnlyList<FurnitureAnchor> Furniture => NoFurniture;
            public event System.Action Changed { add { } remove { } }
        }

        [Test]
        public void Placed_StretchesInItemAxesBeforeTheTurn()
        {
            var item = new CatalogItem { id = "desk", size = new Vec3(1.0f, 0.75f, 0.5f) };
            var anchor = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.375f, 0f), new Vec3(0.6f, 0.75f, 1.2f), 0f), 0.75f);
            OrientedBox turned = OpeningGuard.Placed(anchor, item, true, 1.2f, 1f, 1.2f, 0f);
            Assert.AreEqual(0.6f, turned.size.x, 1e-4f); // item depth 0.5 x 1.2
            Assert.AreEqual(1.2f, turned.size.z, 1e-4f); // item width 1.0 x 1.2
        }

        [Test]
        public void Placed_TreatsZeroScaleAsOne()
        {
            var item = new CatalogItem { id = "desk", size = new Vec3(1.0f, 0.75f, 0.5f) };
            var anchor = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.375f, 0f), new Vec3(1f, 0.75f, 0.5f), 0f), 0.75f);
            OrientedBox box = OpeningGuard.Placed(anchor, item, false, 0f, 0f, 0f, 0f);
            Assert.AreEqual(1.0f, box.size.x, 1e-4f);
            Assert.AreEqual(0.75f, box.size.y, 1e-4f);
            Assert.AreEqual(0.5f, box.size.z, 1e-4f);
        }
    }
}

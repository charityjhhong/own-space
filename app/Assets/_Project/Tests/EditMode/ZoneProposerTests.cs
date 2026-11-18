using System;
using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class ZoneProposerTests
    {
        sealed class TestRoom : IRoomInfo
        {
            public TestRoom(List<Vec2> outline, params FurnitureAnchor[] furniture)
            {
                FloorOutline = outline;
                Furniture = furniture;
            }

            public bool HasScan => FloorOutline.Count > 0;
            public IReadOnlyList<Vec2> FloorOutline { get; }
            public float FloorY => 0f;
            public float CeilingY => 2.5f;
            public IReadOnlyList<RoomElement> Walls => Array.Empty<RoomElement>();
            public IReadOnlyList<RoomElement> Windows => Array.Empty<RoomElement>();
            public IReadOnlyList<RoomElement> Doors => Array.Empty<RoomElement>();
            public IReadOnlyList<FurnitureAnchor> Furniture { get; }
            public event Action Changed { add { } remove { } }
        }

        static FurnitureAnchor Anchor(string id, FurnitureRole role, float x, float z, float w, float d, float yaw) =>
            new FurnitureAnchor(id, role, new OrientedBox(new Vec3(x, 0.4f, z), new Vec3(w, 0.8f, d), yaw), 0.75f);

        static float SignedArea(List<Vec2> poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++) a += (poly[j].x - poly[i].x) * (poly[j].y + poly[i].y);
            return a / 2f;
        }

        [TestCase("a", -0.9f)]
        [TestCase("b", 0.9f)]
        public void Dorm_PicksTheUsersSideOnly(string side, float userX)
        {
            var room = FakeRoomInfo.DormRoom();
            var zone = ZoneProposer.Propose(room, new Vec3(userX, 1.2f, 2.2f));

            Assert.AreEqual(4, zone.Count);
            Assert.Greater(SignedArea(zone), 0f, "counter-clockwise");
            Assert.IsTrue(Zones.Contains(zone, new Vec3(userX, 1.2f, 2.2f)), "user");
            foreach (var anchor in room.Furniture)
            {
                bool mine = anchor.Id.StartsWith(side + "-");
                Assert.AreEqual(mine, Zones.Contains(zone, anchor), anchor.Id);
            }
            foreach (var p in zone)
            {
                Assert.That(p.x, Is.InRange(-FakeRoomInfo.HalfWidth - 1e-4f, FakeRoomInfo.HalfWidth + 1e-4f));
                Assert.That(p.y, Is.InRange(-1e-4f, FakeRoomInfo.Length + 1e-4f));
            }
        }

        [Test]
        public void RotatedRoom_FollowsTheDesk()
        {
            var desk = Anchor("desk", FurnitureRole.Table, 1f, 1f, 1.2f, 0.6f, 30f);
            var bed = Anchor("bed", FurnitureRole.Bed, 2.5f, 0f, 2.0f, 0.95f, 30f);
            var zone = ZoneProposer.Propose(new TestRoom(new List<Vec2>(), desk, bed), new Vec3(1f, 1f, 0.5f));
            Assert.IsTrue(Zones.Contains(zone, desk));
            Assert.IsTrue(Zones.Contains(zone, bed));
            Assert.IsFalse(Zones.Contains(zone, new Vec2(-3f, 5f)));
            float side01 = Distance(zone[0], zone[1]), side12 = Distance(zone[1], zone[2]);
            Assert.Less(side01 * side12, 12f, "tight around desk and bed, not a world-aligned box");
        }

        // The user is always at a desk or on a bed: on the bed, the zone is built around the bed.
        [Test]
        public void UserOnTheBed_ZoneFollowsTheBed()
        {
            var desk = Anchor("desk", FurnitureRole.Table, 0f, 0f, 1.2f, 0.6f, 0f);
            var bed = Anchor("bed", FurnitureRole.Bed, 6f, 0f, 2.0f, 0.95f, 90f);
            var room = new TestRoom(new List<Vec2>(), desk, bed);
            Assert.AreSame(bed, ZoneProposer.PieceAt(room, new Vec3(6.1f, 0.6f, 0.3f)), "lying on it");
            Assert.AreSame(desk, ZoneProposer.PieceAt(room, new Vec3(0f, 1.2f, 0.6f)), "sitting at it");
            var zone = ZoneProposer.Propose(room, new Vec3(6.1f, 0.6f, 0.3f));
            Assert.IsTrue(Zones.Contains(zone, bed));
            Assert.IsFalse(Zones.Contains(zone, desk), "the desk is too far from the bed");

            // A big bed whose centre is nearer than the desk the user sits at: the desk still wins.
            var bigBed = Anchor("bed", FurnitureRole.Bed, 0f, 1.8f, 2.0f, 2.0f, 0f);
            Assert.AreSame(desk, ZoneProposer.PieceAt(new TestRoom(new List<Vec2>(), desk, bigBed), new Vec3(0f, 1.2f, 0.45f)));
            Assert.IsNull(ZoneProposer.PieceAt(new TestRoom(new List<Vec2>()), new Vec3(0f, 1f, 0f)));
        }

        [Test]
        public void FarBed_IsLeftOut()
        {
            var desk = Anchor("desk", FurnitureRole.Table, 0f, 0f, 1.2f, 0.6f, 0f);
            var farBed = Anchor("bed", FurnitureRole.Bed, 6f, 0f, 2.0f, 0.95f, 0f);
            var zone = ZoneProposer.Propose(new TestRoom(new List<Vec2>(), desk, farBed), new Vec3(0f, 1f, 0.6f));
            Assert.IsTrue(Zones.Contains(zone, desk));
            Assert.IsFalse(Zones.Contains(zone, farBed));
        }

        [Test]
        public void NoFurniture_FallsBackToSquareAroundUserInsideRoom()
        {
            var outline = new List<Vec2> { new Vec2(0, 0), new Vec2(4, 0), new Vec2(4, 4), new Vec2(0, 4) };
            var zone = ZoneProposer.Propose(new TestRoom(outline), new Vec3(0.5f, 1f, 2f));
            Assert.IsTrue(Zones.Contains(zone, new Vec2(0.5f, 2f)));
            Assert.AreEqual(2f * 3f, Math.Abs(SignedArea(zone)), 1e-3f, "clipped at the wall x = 0");
        }

        [Test]
        public void NoScan_GivesThreeMeterSquare()
        {
            var zone = ZoneProposer.Propose(FakeRoomInfo.Empty(), new Vec3(1f, 1.6f, 1f));
            Assert.AreEqual(9f, SignedArea(zone), 1e-3f);
            Assert.IsTrue(Zones.Contains(zone, new Vec2(1f, 1f)));
        }

        static float Distance(Vec2 a, Vec2 b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }
}

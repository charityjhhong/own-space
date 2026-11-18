using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.MR;

namespace OwnSpace.MR.Tests
{
    public class ClampInsideTests
    {
        static readonly List<Vec2> Room = new List<Vec2>
        {
            new Vec2(0f, 0f), new Vec2(3f, 0f), new Vec2(3f, 4f), new Vec2(0f, 4f),
        };

        [Test]
        public void InsidePoint_Unchanged()
        {
            Vec2 p = RoomGeometry.ClampInside(Room, new Vec2(1f, 2f));
            Assert.AreEqual(1f, p.x, 1e-5f);
            Assert.AreEqual(2f, p.y, 1e-5f);
        }

        [Test]
        public void OutsidePoint_MovesToNearestWall()
        {
            Vec2 p = RoomGeometry.ClampInside(Room, new Vec2(5f, 2f));
            Assert.AreEqual(3f, p.x, 1e-5f);
            Assert.AreEqual(2f, p.y, 1e-5f);

            Vec2 corner = RoomGeometry.ClampInside(Room, new Vec2(-1f, -1f));
            Assert.AreEqual(0f, corner.x, 1e-5f);
            Assert.AreEqual(0f, corner.y, 1e-5f);
        }

        [Test]
        public void NoOutline_Unchanged()
        {
            Vec2 p = RoomGeometry.ClampInside(new List<Vec2>(), new Vec2(9f, 9f));
            Assert.AreEqual(9f, p.x, 1e-5f);
        }
    }
}

using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Realism pass: a lamp within 0.6 m of a virtual wall washes it warm.
    public class LampWallTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };

        [Test]
        public void ALampByAWall_WashesIt_FacingIntoTheRoom()
        {
            Assert.IsTrue(LightGlows.NearWall(Room, new Vector3(2f, 0f, 2.6f), out Vector3 on, out Vector3 into));
            Assert.AreEqual(3f, on.z, 1e-4f, "the back wall");
            Assert.AreEqual(2f, on.x, 1e-4f, "straight behind the lamp");
            Assert.AreEqual(-1f, into.z, 1e-4f, "facing into the room");
            Assert.IsTrue(LightGlows.NearWall(Room, new Vector3(0.3f, 0f, 1.5f), out on, out into));
            Assert.AreEqual(1f, into.x, 1e-4f, "the left wall faces +x");
        }

        [Test]
        public void ALampInTheMiddle_WashesNoWall()
        {
            Assert.IsFalse(LightGlows.NearWall(Room, new Vector3(2f, 0f, 1.5f), out _, out _));
            Assert.IsFalse(LightGlows.NearWall(null, new Vector3(2f, 0f, 2.9f), out _, out _));
            Assert.Less(LightGlows.WallShare, 1f, "fainter than the floor pool");
        }
    }
}

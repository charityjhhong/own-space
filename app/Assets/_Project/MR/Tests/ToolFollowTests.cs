using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class ToolFollowTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 2f), new Vec2(-2f, 2f) };
        static readonly Vector3 Head = new Vector3(0f, 1.2f, 0f);

        [Test]
        public void Seat_IsAheadBesideAndBelow()
        {
            Vector3 seat = ToolFollow.Seat(Head, Vector3.forward, -0.2f, Room);
            Assert.AreEqual(-0.2f, seat.x, 1e-4f);
            Assert.AreEqual(0.9f, seat.y, 1e-4f);
            Assert.AreEqual(0.35f, seat.z, 1e-4f);
        }

        [Test]
        public void Seat_StaysInFrontOfAWall()
        {
            Vector3 seat = ToolFollow.Seat(new Vector3(0f, 1.2f, 1.75f), Vector3.forward, 0.2f, Room);
            Assert.Less(seat.z, 2f);
        }

        [Test]
        public void NeedsReturn_WhenBehindOrFar_NotWhenSeated()
        {
            Assert.IsFalse(ToolFollow.NeedsReturn(Head, Vector3.forward, ToolFollow.Seat(Head, Vector3.forward, 0.2f, Room)));
            Assert.IsTrue(ToolFollow.NeedsReturn(Head, Vector3.right, ToolFollow.Seat(Head, Vector3.forward, 0.2f, Room)));
            Assert.IsTrue(ToolFollow.NeedsReturn(Head, Vector3.forward, Head + new Vector3(0f, -0.3f, 1f)));
        }
    }
}

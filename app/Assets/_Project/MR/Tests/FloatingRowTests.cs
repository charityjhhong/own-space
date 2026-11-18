using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class FloatingRowTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 2f), new Vec2(-2f, 2f) };

        [Test]
        public void ReachInside_FullReachInTheOpen()
        {
            Assert.AreEqual(0.42f, FloatingRow.ReachInside(Vector3.zero, Vector3.forward, 0.45f, Room), 1e-4f);
        }

        [Test]
        public void ReachInside_PullsTowardTheUserNearAWall()
        {
            // Head 0.3 m from the +z wall: the row must end up in front of the wall, not inside it.
            float reach = FloatingRow.ReachInside(new Vector3(0f, 1.5f, 1.7f), Vector3.forward, 0.45f, Room);
            Assert.Less(1.7f + reach, 2f - 0.05f);
        }

        [Test]
        public void ReachInside_NoOutlineKeepsFullReach()
        {
            Assert.AreEqual(0.42f, FloatingRow.ReachInside(Vector3.zero, Vector3.forward, 0.45f, new List<Vec2>()), 1e-4f);
        }
    }
}

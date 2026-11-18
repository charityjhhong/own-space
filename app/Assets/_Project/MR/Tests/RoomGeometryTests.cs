using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.MR;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class RoomGeometryTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void BoxFromEdges_PicksVerticalHeightAndLongerWidth()
        {
            // MRUK-style volume: local z is up, so the edges arrive in an odd order.
            OrientedBox box = RoomGeometry.BoxFromEdges(
                new Vector3(1f, 0.375f, 2f),
                new Vector3(0f, 0f, 0.6f), new Vector3(1.3f, 0f, 0f), new Vector3(0f, -0.75f, 0f),
                new Vector2(1f, 5f));

            Assert.AreEqual(1.3f, box.size.x, Eps);
            Assert.AreEqual(0.75f, box.size.y, Eps);
            Assert.AreEqual(0.6f, box.size.z, Eps);
        }

        [Test]
        public void BoxFromEdges_FrontFacesTheFacePoint()
        {
            // Desk against the x = -1.8 wall, room center at x = 0: front must point +x (yaw 90).
            OrientedBox box = RoomGeometry.BoxFromEdges(
                new Vector3(-1.5f, 0.375f, 2.2f),
                new Vector3(0f, 0f, 1.3f), new Vector3(0f, 0.75f, 0f), new Vector3(0.6f, 0f, 0f),
                new Vector2(0f, 2.5f));

            Assert.AreEqual(90f, box.yawDegrees, 0.01f);
            Assert.AreEqual(1.3f, box.size.x, Eps);
        }

        [Test]
        public void FloorOutlineCcw_ReversesClockwiseInput()
        {
            var clockwise = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 2f), new Vector3(3f, 0f, 2f), new Vector3(3f, 0f, 0f),
            };
            var outline = new List<Vec2>();
            RoomGeometry.FloorOutlineCcw(clockwise, outline);

            Assert.AreEqual(4, outline.Count);
            Assert.Greater(RoomGeometry.SignedArea(outline), 0f);
            Assert.AreEqual(6f, RoomGeometry.SignedArea(outline), Eps);
        }

        [Test]
        public void UsableHeight_TablePrefersTopPlane_StorageHasNone()
        {
            var box = new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1f, 0.8f, 0.5f), 0f);

            Assert.AreEqual(0.74f, RoomGeometry.UsableHeight(FurnitureRole.Table, box, 0f, 0.74f).Value, Eps);
            Assert.AreEqual(0.8f, RoomGeometry.UsableHeight(FurnitureRole.Bed, box, 0f, null).Value, Eps);
            Assert.IsNull(RoomGeometry.UsableHeight(FurnitureRole.Storage, box, 0f, null));
        }

        [Test]
        public void YawOf_MatchesUnityRotation()
        {
            Vector3 dir = Quaternion.Euler(0f, -90f, 0f) * Vector3.forward;
            Assert.AreEqual(-90f, RoomGeometry.YawOf(dir), 0.01f);
        }

        static readonly List<Vec2> Square = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 2f), new Vec2(-2f, 2f) };

        [Test]
        public void RayRoom_HitsTheWallAhead()
        {
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.forward);
            Assert.IsTrue(RoomGeometry.RayRoom(ray, Square, 0f, 2.5f, out float d));
            Assert.AreEqual(2f, d, Eps);
        }

        [Test]
        public void RayRoom_HitsTheFloorBeforeTheWall()
        {
            var ray = new Ray(new Vector3(0f, 1f, 0f), new Vector3(0f, -1f, 1f).normalized);
            Assert.IsTrue(RoomGeometry.RayRoom(ray, Square, 0f, 2.5f, out float d));
            Assert.AreEqual(Mathf.Sqrt(2f), d, Eps);
        }

        [Test]
        public void RayRoom_MissesWithNoRoom()
        {
            var ray = new Ray(new Vector3(0f, 1.5f, 0f), Vector3.forward);
            Assert.IsFalse(RoomGeometry.RayRoom(ray, new List<Vec2>(), 0f, 0f, out _));
        }
    }
}

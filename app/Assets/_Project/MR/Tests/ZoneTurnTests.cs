using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: "the zone rectangle must be rotatable, the same way as furniture".
    public class ZoneTurnTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(-3f, -3f), new Vec2(3f, -3f), new Vec2(3f, 3f), new Vec2(-3f, 3f) };
        static List<Vec2> Rect() => new List<Vec2> { new Vec2(-1f, -0.5f), new Vec2(1f, -0.5f), new Vec2(1f, 0.5f), new Vec2(-1f, 0.5f) };

        static void AssertRectangle(List<Vec2> p, float w, float d)
        {
            Assert.IsTrue(ZoneSetup.RectFrame(p, out _, out float width, out float depth, out _));
            Assert.AreEqual(w, width, 1e-3f);
            Assert.AreEqual(d, depth, 1e-3f);
            for (int i = 0; i < 4; i++)
            {
                Vec2 a = p[i], b = p[(i + 1) % 4], c = p[(i + 2) % 4];
                Assert.AreEqual(0f, (b.x - a.x) * (c.x - b.x) + (b.y - a.y) * (c.y - b.y), 1e-3f, $"right angle at {(i + 1) % 4}");
            }
        }

        [Test]
        public void Turning_KeepsARectangle_AboutItsCentre_AndCornerDragsUseItsTurnedAxes()
        {
            List<Vec2> start = Rect(), turned = Rect();
            Assert.IsTrue(ZoneSetup.RotateZone(start, new Vec2(0f, 0f), 35f, Room, turned));
            AssertRectangle(turned, 2f, 1f);
            Assert.IsTrue(ZoneSetup.RectFrame(turned, out Vec2 c, out _, out _, out float yaw));
            Assert.AreEqual(0f, c.x, 1e-4f); Assert.AreEqual(0f, c.y, 1e-4f);
            Assert.AreEqual(35f, Mathf.DeltaAngle(0f, yaw), 0.01f);
            // A corner drag on the turned zone keeps it a turned rectangle.
            var resized = new List<Vec2>(turned);
            Assert.IsTrue(ZoneSetup.RectDrag(turned, 2, new Vec2(turned[2].x + 0.3f, turned[2].y + 0.2f), resized));
            Assert.IsTrue(ZoneSetup.RectFrame(resized, out _, out _, out _, out float yaw2));
            Assert.AreEqual(35f, Mathf.DeltaAngle(0f, yaw2), 0.5f, "still turned 35°");
        }

        [Test]
        public void Turning_StaysInsideTheRoom()
        {
            var big = new List<Vec2> { new Vec2(-2.9f, -0.5f), new Vec2(2.9f, -0.5f), new Vec2(2.9f, 0.5f), new Vec2(-2.9f, 0.5f) };
            var result = new List<Vec2>(big);
            var corridor = new List<Vec2> { new Vec2(-3f, -1f), new Vec2(3f, -1f), new Vec2(3f, 1f), new Vec2(-3f, 1f) };
            Assert.IsFalse(ZoneSetup.RotateZone(big, new Vec2(0f, 0f), 45f, corridor, result), "a 5.8 m zone cannot turn 45° in a 6 × 2 m room");
            Assert.AreEqual(big, result, "nothing written");
        }

        [Test]
        public void Snaps_To5Degrees_AndToTheWalls()
        {
            Assert.AreEqual(35f, ZoneSetup.SnapZoneYaw(33.4f, null), 1e-4f);
            var turnedRoom = new List<Vec2>();
            Quaternion q = Quaternion.Euler(0f, 12f, 0f);
            foreach (Vec2 p in Room) { Vector3 r = q * new Vector3(p.x, 0f, p.y); turnedRoom.Add(new Vec2(r.x, r.z)); }
            Assert.AreEqual(12f, Mathf.DeltaAngle(0f, ZoneSetup.SnapZoneYaw(10.5f, turnedRoom)), 1e-3f, "near a wall's direction: square to it");
            Assert.AreEqual(102f, Mathf.DeltaAngle(0f, ZoneSetup.SnapZoneYaw(100.5f, turnedRoom)), 1e-3f, "and to the other walls (mod 90°)");
            Assert.AreEqual(25f, ZoneSetup.SnapZoneYaw(24f, turnedRoom), 1e-3f, "far from any wall: 5° steps");
        }
    }
}

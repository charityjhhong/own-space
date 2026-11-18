using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human (build 0ddb26d): "the zone keeps coming out diagonal to the room"; a my-zone wall finish painted a strip.
    public class ZoneAlignTests
    {
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };

        static FurnitureAnchor Desk(float yaw) =>
            new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(2f, 0.4f, 2.6f), new Vec3(1.2f, 0.75f, 0.6f), yaw));

        static float Off(float a, float b) => Mathf.Abs(((Mathf.DeltaAngle(a, b) % 90f) + 135f) % 90f - 45f);

        [Test]
        public void Orientation_IsTheDesks_SquaredToItsWall_ElseTheNearestWall()
        {
            Assert.Less(Off(ZoneAlign.FrameYaw(Desk(0f), Room, new Vec2(2f, 2f)), 0f), 1e-3f);
            Assert.Less(Off(ZoneAlign.FrameYaw(Desk(3f), Room, new Vec2(2f, 2f)), 0f), 1e-3f, "a scanned desk 3 degrees off: squared to the wall");
            Assert.Less(Off(ZoneAlign.FrameYaw(Desk(30f), Room, new Vec2(2f, 2f)), 0f), 1e-3f, "against the wall: the wall's direction, however the box was scanned");
            var middle = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(2f, 0.4f, 1.5f), new Vec3(1.2f, 0.75f, 0.6f), 30f));
            Assert.Less(Off(ZoneAlign.FrameYaw(middle, Room, new Vec2(2f, 1f)), 30f), 1e-3f, "a desk set at an angle in the middle of the room: its own direction");
            var tilted = new List<Vec2>();
            foreach (Vec2 p in Room) { Vector3 r = Quaternion.Euler(0f, 20f, 0f) * new Vector3(p.x, 0f, p.y); tilted.Add(new Vec2(r.x, r.z)); }
            Assert.Less(Off(ZoneAlign.FrameYaw(null, tilted, new Vec2(0.5f, 0.5f)), 20f), 1e-3f, "no desk or bed: the nearest wall, never the world axes");
        }

        [Test]
        public void TheProposal_IsARectangleOnThatFrame_InsideTheRoom()
        {
            var zone = new List<Vec2> { new Vec2(1f, 1.5f), new Vec2(3.5f, 2f), new Vec2(3f, 3.4f), new Vec2(0.6f, 3.2f) }; // skewed, past a wall
            ZoneAlign.Square(zone, 0f, Room);
            Assert.IsTrue(ZoneAlign.IsRect(zone));
            Assert.IsTrue(ZoneSetup.RectFrame(zone, out _, out _, out _, out float yaw));
            Assert.Less(Off(yaw, 0f), 1e-3f, "parallel to the walls");
            foreach (Vec2 p in zone) Assert.LessOrEqual(p.y, 3f + 1e-4f, "inside the room");
            var fit = new List<Vec2> { new Vec2(1.8f, 2f), new Vec2(2.2f, 2f), new Vec2(2.2f, 2.2f), new Vec2(1.8f, 2.2f) };
            Assert.IsTrue(ZoneFit.Contain(fit, new[] { Desk(3f) }, new Vec2(2f, 2f), Room));
            Assert.IsTrue(ZoneAlign.IsRect(fit), "Fit to desk stays a rectangle");
            Assert.IsTrue(ZoneSetup.RectFrame(fit, out _, out _, out _, out yaw));
            Assert.Less(Off(yaw, 0f), 1e-3f, "Fit to desk: the desk's direction squared to its wall");
        }

        // Device (build dfb22f4): the human's saved zone, a clean rectangle about 33 degrees off the room, made by the old
        // skew bug. Not turned by the user: squared whatever the angle, centre and size kept as the room allows.
        [Test]
        public void TheHumansSavedZone_IsSquaredToTheDesk_WhateverTheAngle()
        {
            var zone = new List<Vec2> { new Vec2(-0.0336164832f, 1.08377743f), new Vec2(-1.46310091f, 0.168976128f), new Vec2(-0.8153861f, -0.833682537f), new Vec2(0.609167337f, 0.0865716338f) };
            Assert.IsTrue(ZoneSetup.RectFrame(zone, out Vec2 c0, out float w0, out float d0, out float saved));
            var room = new List<Vec2> { new Vec2(-2.5f, -1.5f), new Vec2(1.5f, -1.5f), new Vec2(1.5f, 1.6f), new Vec2(-2.5f, 1.6f) };
            var desk = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(-0.4f, 0.4f, 1.25f), new Vec3(1.2f, 0.75f, 0.6f), 33f)); // a scanned box well off its wall
            float target = ZoneAlign.FrameYaw(desk, room, new Vec2(-0.4f, 0.6f));
            Assert.Less(Off(target, 0f), 1e-3f, "against the back wall: the wall's direction, not the box's 33 degrees");
            Assert.Greater(Off(saved, target), 10f, "more than #269's 10 degrees");
            Assert.IsTrue(ZoneAlign.SquareSaved(zone, target, room, out float before));
            Assert.AreEqual(saved, before, 1e-3f);
            Assert.IsTrue(ZoneAlign.IsRect(zone));
            Assert.IsTrue(ZoneSetup.RectFrame(zone, out Vec2 c1, out float w1, out float d1, out float now));
            Assert.Less(Off(now, target), 1e-3f, "parallel to the desk's wall");
            Assert.AreEqual(c0.x, c1.x, 0.05f, "centre kept"); Assert.AreEqual(c0.y, c1.y, 0.05f);
            Assert.AreEqual(w0 * d0, w1 * d1, 0.05f, "size kept (the room allows it here)");
            Assert.IsFalse(ZoneAlign.SquareSaved(zone, target, room, out _), "already square: nothing to do");
        }

        [Test]
        public void TheWallBehindTheDesk_IsPaintedAcrossTheZone_EvenFromFurtherAway()
        {
            var desk = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(2f, 0.4f, 2.65f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
            Vec2 a = new Vec2(0f, 3f), b = new Vec2(4f, 3f);
            Assert.AreEqual(ZoneAlign.DeskWallReach, ZoneAlign.WallReachFor(a, b, desk), 1e-5f, "the wall behind the desk");
            Assert.AreEqual(ShellGeometry.WallReach, ZoneAlign.WallReachFor(new Vec2(0f, 0f), new Vec2(4f, 0f), desk), 1e-5f, "any other wall");
            var zone = new List<Vec2> { new Vec2(1f, 1.2f), new Vec2(3f, 1.2f), new Vec2(3f, 2.2f), new Vec2(1f, 2.2f) }; // 0.8 m short of the wall
            var parts = new List<Vector2>();
            ShellGeometry.OutsideIntervals(a, b, zone, parts);
            ShellGeometry.TakeWallSpan(a, b, zone, ZoneAlign.WallReachFor(a, b, desk), parts);
            Assert.AreEqual(new Vector2(0f, 0.25f), parts[0]);
            Assert.AreEqual(new Vector2(0.75f, 1f), parts[1], "painted from x 1 to 3: the zone's width");
        }

        [Test]
        public void TheTurnHandle_SnapsOntoTheDesksDirection()
        {
            Assert.AreEqual(32f, ZoneSetup.SnapZoneYaw(30.5f, null, 32f), 1e-3f, "within 3 degrees of the desk");
            Assert.AreEqual(30f, ZoneSetup.SnapZoneYaw(30.5f, null, 40f), 1e-3f, "else the 5-degree steps");
        }

        [Test]
        public void AZoneAlongTheWall_OwnsThatWallEdgeToEdge()
        {
            var zone = new List<Vec2> { new Vec2(1f, 1.8f), new Vec2(3f, 1.8f), new Vec2(3f, 2.8f), new Vec2(1f, 2.8f) }; // 20 cm short of the back wall
            Vec2 a = new Vec2(0f, 3f), b = new Vec2(4f, 3f);
            var parts = new List<Vector2>();
            ShellGeometry.OutsideIntervals(a, b, zone, parts);
            Assert.AreEqual(1, parts.Count, "the wall line misses the zone: all outside");
            ShellGeometry.TakeWallSpan(a, b, zone, ShellGeometry.WallReach, parts);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(new Vector2(0f, 0.25f), parts[0], "outside up to x 1");
            Assert.AreEqual(new Vector2(0.75f, 1f), parts[1], "outside from x 3: the zone owns 1..3, its full width");
            ShellGeometry.OutsideIntervals(new Vec2(0f, 0f), new Vec2(4f, 0f), zone, parts);
            ShellGeometry.TakeWallSpan(new Vec2(0f, 0f), new Vec2(4f, 0f), zone, ShellGeometry.WallReach, parts);
            Assert.AreEqual(new Vector2(0f, 1f), parts[0], "the far wall: untouched");
        }
    }
}

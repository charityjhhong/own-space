using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.MR;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class ShellGeometryTests
    {
        // Device shot 221205 (Evening): the walls read as separate panels at different depths.
        [Test]
        public void DistanceToOutline_SaysHowFarAScannedWallStandsFromTheFloorEdge()
        {
            var room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };
            Assert.AreEqual(0f, ShellGeometry.DistanceToOutline(new Vec2(2f, 0f), room), 1e-5f, "on an edge");
            Assert.AreEqual(0.25f, ShellGeometry.DistanceToOutline(new Vec2(2f, 0.25f), room), 1e-5f, "a wall standing 25 cm inside");
            Assert.AreEqual(0.1f, ShellGeometry.DistanceToOutline(new Vec2(4.1f, 1f), room), 1e-5f, "or 10 cm outside");
            Assert.AreEqual(0f, ShellGeometry.DistanceToOutline(new Vec2(1f, 1f), null));
        }

        [Test]
        public void AWallLeftRealInMyZone_IsAHoleFloorToCeiling_AStyledOneIsNot()
        {
            Assert.IsTrue(OutsideShell.RealWallIsHole(myWallStyled: false), "nothing virtual shows behind the real stretch");
            Assert.IsFalse(OutsideShell.RealWallIsHole(myWallStyled: true), "a styled stretch is drawn, it must not be cut");
        }

        // A finish change on one flat wall is not a crease, and no crease or grain makes a wall unreadable.
        [Test]
        public void Shading_DarkensOnlyAtRealCorners_AndNeverBelowFourFifths()
        {
            var room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(4f, 0f), new Vec2(4f, 3f), new Vec2(0f, 3f) };
            // Mid-wall at eye height, where my-zone finish may meet the outside finish: no darkening at all.
            for (float x = 0.5f; x <= 3.5f; x += 0.25f) Assert.AreEqual(1f, ShellShading.Shade(new Vector3(x, 1.2f, 0f), room, 0f, 2.5f), 1e-5f);
            float darkest = 1f;
            for (float x = 0f; x <= 4f; x += 0.05f) for (float y = 0f; y <= 2.5f; y += 0.05f)
                darkest = Mathf.Min(darkest, ShellShading.Shade(new Vector3(x, y, 0f), room, 0f, 2.5f));
            Assert.GreaterOrEqual(darkest - ShellShading.Grain, 0.8f, "the darkest wall point keeps at least 80% of the finish's colour");
        }

        static List<Vec2> Poly(params float[] xy)
        {
            var p = new List<Vec2>();
            for (int i = 0; i < xy.Length; i += 2) p.Add(new Vec2(xy[i], xy[i + 1]));
            return p;
        }

        static float Area(List<Vec2> p, List<int> tris)
        {
            float sum = 0f;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vec2 a = p[tris[i]], b = p[tris[i + 1]], c = p[tris[i + 2]];
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                Assert.Greater(cross, 0f, "every triangle is counter-clockwise from above");
                sum += cross * 0.5f;
            }
            return sum;
        }

        [Test]
        public void Triangulate_LShape_EitherWinding_CoversItsArea()
        {
            var l = Poly(0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2); // area 3, concave
            var tris = new List<int>();
            Assert.IsTrue(ShellGeometry.Triangulate(l, tris));
            Assert.AreEqual(12, tris.Count);
            Assert.AreEqual(3f, Area(l, tris), 1e-4f);

            l.Reverse();
            Assert.IsTrue(ShellGeometry.Triangulate(l, tris));
            Assert.AreEqual(3f, Area(l, tris), 1e-4f);
        }

        [Test]
        public void OutsideIntervals_WallCrossingTheZone_KeepsBothEnds()
        {
            var zone = Poly(1, -1, 2, -1, 2, 1, 1, 1);
            var parts = new List<Vector2>();
            ShellGeometry.OutsideIntervals(new Vec2(0, 0), new Vec2(4, 0), zone, parts);

            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(0f, parts[0].x, 1e-4f); Assert.AreEqual(0.25f, parts[0].y, 1e-4f);
            Assert.AreEqual(0.5f, parts[1].x, 1e-4f); Assert.AreEqual(1f, parts[1].y, 1e-4f);
        }

        [Test]
        public void OutsideIntervals_WallInsideZone_IsEmpty_NoZone_IsWhole()
        {
            var zone = Poly(-1, -1, 5, -1, 5, 1, -1, 1);
            var parts = new List<Vector2>();
            ShellGeometry.OutsideIntervals(new Vec2(0, 0), new Vec2(4, 0), zone, parts);
            Assert.AreEqual(0, parts.Count);

            ShellGeometry.OutsideIntervals(new Vec2(0, 0), new Vec2(4, 0), new List<Vec2>(), parts);
            Assert.AreEqual(1, parts.Count);
        }

        [Test]
        public void SeamBand_PointsAwayFromTheZone()
        {
            var zone = Poly(0, 0, 1, 0, 1, 1, 0, 1);
            var quads = new List<Vec2>();
            ShellGeometry.SeamBand(zone, 0.2f, quads);

            Assert.AreEqual(16, quads.Count);
            for (int i = 2; i < quads.Count; i += 4) Assert.IsFalse(Zones.Contains(zone, quads[i]));
            Assert.AreEqual(-0.2f, quads[2].y, 1e-4f); // first edge runs along z = 0; outward is -z
        }

        [Test]
        public void WallEnds_FollowYaw()
        {
            var wall = new OrientedBox(new Vec3(0f, 1f, 2f), new Vec3(4f, 2f, 0f), 90f);
            ShellGeometry.WallEnds(wall, out Vec2 a, out Vec2 b);
            Assert.AreEqual(0f, a.x, 1e-4f); Assert.AreEqual(4f, a.y, 1e-4f);
            Assert.AreEqual(0f, b.x, 1e-4f); Assert.AreEqual(0f, b.y, 1e-4f);
        }
    
        [Test]
        public void IntoRoom_PointsInsideForEveryDormOpening_EvenWithAFlippedNormal()
        {
            var room = OwnSpace.Contracts.Fakes.FakeRoomInfo.DormRoom();
            var openings = new List<RoomElement>(room.Windows);
            openings.AddRange(room.Doors);
            Assert.Greater(openings.Count, 0);
            foreach (RoomElement o in openings)
            {
                foreach (float extra in new[] { 0f, 180f })
                {
                    var box = new OrientedBox(o.Box.center, o.Box.size, o.Box.yawDegrees + extra);
                    Vector3 into = ShellGeometry.IntoRoom(box, room.FloorOutline);
                    var probe = new Vec2(box.center.x + into.x * 0.05f, box.center.z + into.z * 0.05f);
                    Assert.IsTrue(Zones.Contains(room.FloorOutline, probe), $"{o.Id} yaw+{extra}");
                }
            }
        }

        // Realism pass I: an outside wall fades into the real wall where it crosses the zone border, never at the
        // wall's own ends (corners), and never past the other end of a short part.
        [Test]
        public void BorderFades_OnlyAtTheZoneBorder_ClampedToThePart()
        {
            var parts = new List<Vector2> { new Vector2(0f, 0.4f), new Vector2(0.7f, 1f) };
            var fades = new List<Vector2>();
            ShellGeometry.BorderFades(parts, 4f, 0.3f, fades);
            Assert.AreEqual(2, fades.Count, "one fade at each border crossing; none at the corners");
            Assert.AreEqual(0.4f, fades[0].x, 1e-5f); Assert.AreEqual(0.4f - 0.075f, fades[0].y, 1e-5f);
            Assert.AreEqual(0.7f, fades[1].x, 1e-5f); Assert.AreEqual(0.7f + 0.075f, fades[1].y, 1e-5f);

            ShellGeometry.BorderFades(new List<Vector2> { new Vector2(0.45f, 0.5f) }, 4f, 0.3f, fades);
            Assert.AreEqual(2, fades.Count);
            Assert.AreEqual(0.5f, fades[0].y, 1e-5f, "a short part: the fade stops at its other end");

            ShellGeometry.BorderFades(new List<Vector2> { new Vector2(0f, 1f) }, 4f, 0.3f, fades);
            Assert.AreEqual(0, fades.Count, "a wall wholly outside: no border on it");
        }
    }
}

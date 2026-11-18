using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device bug on a2c27d2: stuck in the zone step (card out of sight, twitchy corners that skewed into diagonals).
    public class ZoneRescueTests
    {
        static List<Vec2> Rect() => new List<Vec2> { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 1.5f), new Vec2(0f, 1.5f) };

        [Test]
        public void DraggingACorner_KeepsARectangle()
        {
            List<Vec2> start = Rect(), result = Rect();
            Assert.IsTrue(ZoneSetup.RectDrag(start, 2, new Vec2(2.6f, 2.1f), result));
            Assert.AreEqual(new Vec2(0f, 0f), result[0], "the opposite corner stays");
            Assert.AreEqual(2.6f, result[1].x, 1e-4f); Assert.AreEqual(0f, result[1].y, 1e-4f);
            Assert.AreEqual(2.6f, result[2].x, 1e-4f); Assert.AreEqual(2.1f, result[2].y, 1e-4f);
            Assert.AreEqual(0f, result[3].x, 1e-4f); Assert.AreEqual(2.1f, result[3].y, 1e-4f);
            // Dragged past the opposite corner: never folds, each side at least 0.5 m.
            Assert.IsTrue(ZoneSetup.RectDrag(start, 2, new Vec2(-1f, -1f), result));
            Assert.AreEqual(ZoneSetup.MinSide, result[2].x, 1e-4f);
            Assert.AreEqual(ZoneSetup.MinSide, result[2].y, 1e-4f);
        }

        [Test]
        public void DraggingACorner_OfATurnedRectangle_KeepsItsAxes()
        {
            var start = new List<Vec2>();
            Quaternion t = Quaternion.Euler(0f, 30f, 0f);
            foreach (Vec2 p in Rect()) { Vector3 r = t * new Vector3(p.x, 0f, p.y); start.Add(new Vec2(r.x, r.z)); }
            var result = new List<Vec2>(start);
            Assert.IsTrue(ZoneSetup.RectDrag(start, 0, new Vec2(start[0].x - 0.3f, start[0].y + 0.2f), result));
            for (int i = 0; i < 4; i++)
            {
                Vec2 a = result[i], b = result[(i + 1) % 4], c = result[(i + 2) % 4];
                float dot = (b.x - a.x) * (c.x - b.x) + (b.y - a.y) * (c.y - b.y);
                Assert.AreEqual(0f, dot, 1e-3f, $"right angle at corner {(i + 1) % 4}");
            }
        }

        [Test]
        public void NotARectangle_IsLeftToTheOldDrag()
        {
            var tri = new List<Vec2> { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(1f, 1f) };
            Assert.IsFalse(ZoneSetup.RectDrag(tri, 0, new Vec2(0f, 0f), new List<Vec2>(tri)));
            var skew = new List<Vec2> { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(3f, 1.5f), new Vec2(1f, 1.5f) };
            Assert.IsFalse(ZoneSetup.RectDrag(skew, 0, new Vec2(0f, 0f), new List<Vec2>(skew)));
        }

        [Test]
        public void Card_InView_And_TheLinesReadPlainly()
        {
            var view = new Ray(Vector3.up * 1.2f, Vector3.forward);
            Assert.IsTrue(ZoneCard.InView(view, new Vector3(0.1f, 0.9f, 0.5f), 45f), "the panel's seat: low and a little right");
            Assert.IsFalse(ZoneCard.InView(view, new Vector3(-1.5f, 1.2f, 0.3f), 45f), "far left: comes back after 2 s");
            Assert.IsFalse(ZoneSetup.ZoneLine.Contains(";") || ZoneSetup.ZoneMeaning.Contains(";"));
            Assert.AreEqual("Inside stays real. Outside becomes virtual.", ZoneSetup.ZoneMeaning);
            Assert.GreaterOrEqual(ZoneSetup.DeadZone, 0.03f);
        }
    }
}

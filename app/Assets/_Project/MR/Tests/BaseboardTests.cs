using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Realism pass: a baseboard along the virtual walls' bottom edge, in the wall finish's accent, never across a door.
    public class BaseboardTests
    {
        [Test]
        public void TheBaseboard_SkipsTheDoorway()
        {
            Vec2 a = new Vec2(0f, 0f), b = new Vec2(4f, 0f);
            var door = new RoomElement("door", new OrientedBox(new Vec3(2f, 1f, 0.05f), new Vec3(0.9f, 2f, 0.1f), 0f));
            var spans = new List<Vector2> { new Vector2(0f, 1f) };
            OutsideShell.TakeDoors(a, b, new[] { door }, spans);
            Assert.AreEqual(2, spans.Count);
            Assert.AreEqual(0f, spans[0].x, 1e-4f); Assert.AreEqual(1.55f / 4f, spans[0].y, 1e-4f, "up to the door's left jamb");
            Assert.AreEqual(2.45f / 4f, spans[1].x, 1e-4f, "from its right jamb"); Assert.AreEqual(1f, spans[1].y, 1e-4f);
            var other = new List<Vector2> { new Vector2(0f, 1f) };
            OutsideShell.TakeDoors(new Vec2(0f, 3f), new Vec2(4f, 3f), new[] { door }, other);
            Assert.AreEqual(1, other.Count, "a door on another wall: untouched");
        }

        [Test]
        public void TheBaseboard_IsADarkerToneOfTheAccent()
        {
            var accent = new Color(0.6f, 0.5f, 0.4f, 0.5f);
            Color c = OutsideShell.BoardTone(accent);
            Assert.Less(c.r, accent.r); Assert.Less(c.g, accent.g);
            Assert.AreEqual(1f, c.a, "opaque");
            Assert.Less(OutsideShell.BoardHeight, 0.12f, "a slim board"); Assert.Greater(OutsideShell.BoardDepth, 0.005f, "stands out from the wall");
        }

        // The human: "a baseboard on the ceiling too". The crown skips only what reaches the ceiling.
        [Test]
        public void TheCrown_SkipsOnlyOpeningsThatReachTheCeiling()
        {
            Vec2 a = new Vec2(0f, 0f), b = new Vec2(4f, 0f);
            const float ceiling = 2.5f;
            var door = new RoomElement("door", new OrientedBox(new Vec3(1f, 1f, 0.05f), new Vec3(0.8f, 2f, 0.1f), 0f)); // top 2.0 m
            var tall = new RoomElement("window", new OrientedBox(new Vec3(3f, 1.5f, 0.05f), new Vec3(0.6f, 1.98f, 0.1f), 0f)); // top 2.49 m
            var spans = new List<Vector2> { new Vector2(0f, 1f) };
            OutsideShell.TakeCeilingOpenings(a, b, new[] { door, tall }, ceiling, spans);
            Assert.AreEqual(2, spans.Count, "the door stops 0.5 m short: the crown runs over it; the tall window cuts it");
            Assert.AreEqual(2.7f / 4f, spans[0].y, 1e-4f);
            Assert.AreEqual(3.3f / 4f, spans[1].x, 1e-4f);
            Assert.Less(OutsideShell.CrownHeight, OutsideShell.BoardHeight, "slimmer than the baseboard");
            Assert.Greater(OutsideShell.CrownDepth, 0.005f, "stands out from the wall");
        }
    }
}

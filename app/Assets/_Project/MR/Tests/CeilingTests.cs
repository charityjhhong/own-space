using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human (build 9bfbd72): "why are only the edges white?": a white virtual band around the real office ceiling over
    // my zone. Styled walls now get one virtual ceiling over the whole room, in a lighter tone of the wall finish.
    public class CeilingTests
    {
        [Test]
        public void TheCeiling_CoversMyZone_WhenTheWallsAreStyled_OrInAVisit_NotWhenTheyAreLeftReal()
        {
            Assert.IsTrue(OutsideShell.CeilingCoversZone(outsideWallStyled: true, visiting: false));
            Assert.IsTrue(OutsideShell.CeilingCoversZone(false, true), "a visit is virtual everywhere");
            Assert.IsTrue(OutsideShell.CeilingCoversZone(false, false), "walls on Plain too: the ceiling is always fully covered (2427b8d)");
        }

        [Test]
        public void TheCeilingMesh_CoversTheWholeRoom_ZoneIncluded()
        {
            var room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(5f, 0f), new Vec2(5f, 4f), new Vec2(0f, 4f) };
            var zone = new List<Vec2> { new Vec2(3.4f, 2.6f), new Vec2(5f, 2.6f), new Vec2(5f, 4f), new Vec2(3.4f, 4f) }; // in a corner
            var ceiling = new List<Vec2>();
            ShellGeometry.Expand(room, 0.2f, ceiling);
            foreach (Vec2 p in zone) Assert.IsTrue(Zones.Contains(ceiling, p), $"zone corner {p.x},{p.y} under the ceiling");
            foreach (Vec2 p in room) Assert.IsTrue(Zones.Contains(ceiling, p), "every room corner, to past the walls");
        }

        [Test]
        public void TheCeilingTone_IsALighterOpaqueToneOfTheWall()
        {
            var navy = new Color(0.12f, 0.165f, 0.24f, 1f);
            Color c = OutsideShell.CeilingTone(navy);
            Assert.Greater(c.r + c.g + c.b, navy.r + navy.g + navy.b + 1.5f, "clearly lighter than the wall (0.78 toward white)");
            Assert.Greater(c.b - c.r, 0.02f, "still the navy's hue");
            Assert.AreEqual(1f, c.a);
        }
    }
}

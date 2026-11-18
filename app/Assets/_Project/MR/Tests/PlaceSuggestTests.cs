using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: a big scanned room, my zone only the desk, and "can't place" wherever a bed was aimed.
    public class PlaceSuggestTests
    {
        // A 6 x 5 m room, x 0..6, z 0..5.
        static readonly List<Vec2> Room = new List<Vec2> { new Vec2(0f, 0f), new Vec2(6f, 0f), new Vec2(6f, 5f), new Vec2(0f, 5f) };
        static readonly Vector2 Bed = new Vector2(1.6f, 2.0f), User = new Vector2(3f, 2.5f);

        // A rule set like core's, small enough to read: inside the room, out of a zone box, out of an obstacle box.
        static PlaceSuggest.Check Rules(Rect zone, Rect obstacle, Vector2 size) => (at, yaw) =>
        {
            var corners = new Vec2[4];
            FloorGeometry.Footprint(new Vec2(at.x, at.y), yaw, size.x, size.y, corners);
            if (!FloorGeometry.InsideArea(Room, corners)) return PlacementStatus.OutsideRoom;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector2(corners[i].x, corners[i].y);
                if (zone.Contains(c)) return PlacementStatus.InMyZone;
                if (obstacle.Contains(c)) return PlacementStatus.OverlapsFurniture;
            }
            if (zone.Contains(at)) return PlacementStatus.InMyZone;
            if (obstacle.Contains(at)) return PlacementStatus.OverlapsFurniture;
            return PlacementStatus.Ok;
        };

        [Test]
        public void ABed_FindsAValidSpot_NearWhereTheUserAimed_BackToAWall()
        {
            var tally = new int[PlaceSuggest.TallySize];
            var check = Rules(new Rect(2.5f, 2f, 1.2f, 0.8f), new Rect(0f, 0f, 0f, 0f), Bed);
            Assert.IsTrue(PlaceSuggest.Find(Room, Bed, new Vector2(5.5f, 4.5f), User, check, tally, out Vector2 at, out float yaw));
            Assert.AreEqual(PlacementStatus.Ok, check(at, yaw), "the same rules as placing");
            Assert.Less(Vector2.Distance(at, new Vector2(5.5f, 4.5f)), 2f, "near the aimed corner");
            float back = Bed.y * 0.5f + PlaceSuggest.WallGap;
            bool againstAWall = Mathf.Abs(at.x - back) < 0.01f || Mathf.Abs(6f - at.x - back) < 0.01f || Mathf.Abs(at.y - back) < 0.01f || Mathf.Abs(5f - at.y - back) < 0.01f;
            Assert.IsTrue(againstAWall, $"its back against a wall: {at}");
            Assert.Greater(tally[(int)PlacementStatus.Ok], 0);
        }

        [Test]
        public void TheTally_SaysWhichRuleRefuses_AndNoSpotIsAnHonestFalse()
        {
            var tally = new int[PlaceSuggest.TallySize];
            var everywhere = Rules(new Rect(0f, 0f, 0f, 0f), new Rect(-1f, -1f, 8f, 7f), Bed); // real furniture all over
            Assert.IsFalse(PlaceSuggest.Find(Room, Bed, new Vector2(3f, 3f), User, everywhere, tally, out _, out _));
            Assert.AreEqual(0, tally[(int)PlacementStatus.Ok]);
            Assert.Greater(tally[(int)PlacementStatus.OverlapsFurniture], 50, "the log names the rule");
            StringAssert.Contains("OverlapsFurniture", PlaceSuggest.Describe(tally));
        }

        [Test]
        public void AWallSpot_WinsOverAFreeSpotAlmostAsNear()
        {
            var tally = new int[PlaceSuggest.TallySize];
            var open = Rules(new Rect(0f, 0f, 0f, 0f), new Rect(0f, 0f, 0f, 0f), new Vector2(0.8f, 0.8f));
            Assert.IsTrue(PlaceSuggest.Find(Room, new Vector2(0.8f, 0.8f), new Vector2(3f, 0.9f), User, open, tally, out Vector2 at, out float yaw));
            Assert.AreEqual(0.4f + PlaceSuggest.WallGap, at.y, 0.01f, "against the near wall");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, 0f), 0.5f, "facing into the room (+z)");
        }

        [Test]
        public void TheGhost_PulsesSmoothly_OverTwoSeconds()
        {
            Assert.AreEqual(0f, PlacementView.Pulse(0f), 1e-5f);
            Assert.AreEqual(1f, PlacementView.Pulse(PlacementView.PulseSeconds * 0.5f), 1e-5f, "fully in after about a second");
            Assert.AreEqual(0f, PlacementView.Pulse(PlacementView.PulseSeconds), 1e-4f, "and out again");
        }
    }
}

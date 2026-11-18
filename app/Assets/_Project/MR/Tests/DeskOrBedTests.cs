using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Product rule: people use the app at the desk or on the bed.
    public class DeskOrBedTests
    {
        static FurnitureAnchor Piece(string id, FurnitureRole role, float x, float z, float w, float d) =>
            new FurnitureAnchor(id, role, new OrientedBox(new Vec3(x, 0.4f, z), new Vec3(w, 0.75f, d), 0f));

        [Test]
        public void AtPiece_IsTheDeskOrBedTheUserIsAt()
        {
            var room = new List<FurnitureAnchor> { Piece("desk", FurnitureRole.Table, 0f, 1f, 1.2f, 0.6f), Piece("bed", FurnitureRole.Bed, 3f, 1f, 1f, 2f), Piece("shelf", FurnitureRole.Storage, 0.5f, 0f, 0.4f, 0.4f) };
            Assert.AreEqual("desk", ZoneFit.AtPiece(room, new Vec2(0f, 0.3f)).Id, "seated at the desk");
            Assert.AreEqual("bed", ZoneFit.AtPiece(room, new Vec2(3.1f, 1.2f)).Id, "lying on the bed (head over it)");
            Assert.AreEqual("bed", ZoneFit.AtPiece(room, new Vec2(2.3f, 1f)).Id, "beside the bed, closer to it");
            Assert.IsNull(ZoneFit.AtPiece(new List<FurnitureAnchor> { room[2] }, new Vec2(0f, 0f)), "neither: none");
            Assert.AreEqual(0f, ZoneFit.FootprintDistance(room[1].Box, new Vec2(3f, 1.5f)));
            Assert.AreEqual("bed", ZoneFit.PieceWord(room[1]));
            Assert.AreEqual("Your bed is outside your area", ZoneSetup.OutsideLine("bed"));
            Assert.AreEqual("Fit to bed", ZoneSetup.FitLabel("bed"));
        }

        [Test]
        public void Contain_HoldsTheBedWhenTheUserIsOnIt()
        {
            var room = new List<FurnitureAnchor> { Piece("desk", FurnitureRole.Table, 0f, 1f, 1.2f, 0.6f), Piece("bed", FurnitureRole.Bed, 3f, 1f, 1f, 2f) };
            var outline = new List<Vec2> { new Vec2(-2f, -2f), new Vec2(6f, -2f), new Vec2(6f, 4f), new Vec2(-2f, 4f) };
            var zone = new List<Vec2> { new Vec2(2.9f, 0.9f), new Vec2(3.1f, 0.9f), new Vec2(3.1f, 1.1f), new Vec2(2.9f, 1.1f) };
            Assert.IsTrue(ZoneFit.Contain(zone, room, new Vec2(3f, 1f), outline));
            Assert.IsTrue(ZoneFit.Holds(zone, room[1]), "the bed is inside");
            Assert.IsFalse(ZoneSetup.DeskOutside(zone, room, new Vec2(3f, 1f)));
        }

        [Test]
        public void Seat_FollowsTheViewWhenReclined()
        {
            PanelMetrics m = PanelMetrics.Default;
            var head = new Vector3(0f, 0.7f, 0f);
            Vector3 up = Quaternion.Euler(-60f, 0f, 0f) * Vector3.forward; // lying back, looking 60° up
            Vector3 seat = ToolPanel.SeatPosition(head, up, 0.5f, m);
            Assert.Greater(seat.y, head.y + 0.3f, "above the face, where the user looks");
            Vector3 level = ToolPanel.SeatPosition(head, Vector3.forward, 0.5f, m);
            Assert.Less(level.y, head.y, "seated: low and ahead as before");
        }
    }
}

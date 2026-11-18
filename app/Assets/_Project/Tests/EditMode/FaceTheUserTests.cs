using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>
    /// Human: furniture placed by the AI (and by Add) faces me by default. A piece's front is its local +Z.
    /// Dorm side a is my zone; the seat in front of its desk is at (-0.8, 2.2).
    /// </summary>
    public class FaceTheUserTests
    {
        ICatalog _catalog;
        DesignSession _session;
        IRoomInfo _room;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = TestRooms.Dorm(TestRooms.SideA);
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        static float FacingDot(Vec3 position, float yaw, Vec2 user)
        {
            var f = FloorGeometry.Rotate(new Vec2(0f, 1f), yaw);
            float dx = user.x - position.x, dz = user.y - position.z, d = (float)Math.Sqrt(dx * dx + dz * dz);
            return (f.x * dx + f.y * dz) / d;
        }

        [Test]
        public void TowardUser_TurnsTheFront_SnappedTo15Degrees()
        {
            Assert.AreEqual(90f, Facing.TowardUser(new Vec3(0f, 0f, 0f), new Vec3(2f, 1.2f, 0f)), 1e-4f, "user at +x");
            Assert.AreEqual(180f, Facing.TowardUser(new Vec2(0f, 0f), new Vec2(0f, -1f)), 1e-4f);
            Assert.AreEqual(270f, Facing.TowardUser(new Vec2(0f, 0f), new Vec2(-3f, 0f)), 1e-4f);
            Assert.AreEqual(0f, Facing.TowardUser(new Vec2(0f, 0f), new Vec2(0.05f, 2f)), 1e-4f, "1.4° snaps to 0");
            Assert.AreEqual(45f, Facing.TowardUser(new Vec2(0f, 0f), new Vec2(1f, 1.1f)), 1e-4f, "42° snaps to 45");
            Assert.AreEqual(30f, Facing.TowardUser(new Vec2(1f, 1f), new Vec2(1f, 1f), 30f), "same spot: the fallback");
            foreach (var user in new[] { new Vec2(2f, 3f), new Vec2(-1f, -4f), new Vec2(-2f, 0.5f) })
                Assert.Greater(FacingDot(new Vec3(0f, 0f, 0f), Facing.TowardUser(new Vec2(0f, 0f), user), user), 0.99f, "local +Z points at the user");

            Assert.IsTrue(Facing.RoughlyToward(0f, new Vec2(0f, 0f), new Vec2(1f, 0.5f)));
            Assert.IsFalse(Facing.RoughlyToward(0f, new Vec2(0f, 0f), new Vec2(1f, -0.5f)), "the user is behind it");
        }

        [Test]
        public void UserSpot_IsTheHead_ElseTheDeskSeat_ElseTheZoneCentre()
        {
            var zone = FakeRoomInfo.HalfZone("a");
            var seat = Facing.UserSpot(_room, zone, null).Value;
            Assert.AreEqual(-0.8f, seat.x, 1e-4f);
            Assert.AreEqual(2.2f, seat.y, 1e-4f);
            Assert.AreEqual(4.0f, Facing.UserSpot(_room, zone, new Vec3(0.3f, 1.2f, 4.0f)).Value.y, 1e-4f);
            var centre = Facing.UserSpot(TestRooms.Dorm(), zone, null).Value;
            Assert.AreEqual(zone.Average(v => v.x), centre.x, 1e-4f);
            Assert.AreEqual(zone.Average(v => v.y), centre.y, 1e-4f);
            Assert.IsFalse(Facing.HasFront(_catalog.Items.First(i => i.id == "rug-cafe-01")));
            Assert.IsFalse(Facing.HasFront(_catalog.Items.First(i => i.id == "pendant-cafe-01")));
            Assert.IsFalse(Facing.HasFront(_catalog.Items.First(i => i.id == "plant-cafe-01")));
            Assert.IsTrue(Facing.HasFront(_catalog.Items.First(i => i.id == "armchair-cafe-01")));
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        AssistantOutcome Ask(Vec3? user, params PlanPlacement[] pieces)
        {
            var plan = new AssistantPlan();
            plan.placements.AddRange(pieces);
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, _session, _catalog, null, null) { UserPosition = user };
            return runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Test]
        public void AiFloorPieces_FaceTheUser_WhateverYawTheModelGave()
        {
            var outcome = Ask(null,
                new PlanPlacement { catalogItemId = "armchair-cafe-01", x = 0.9f, z = 1.0f, yawDegrees = 0f }, // the model faced it away
                new PlanPlacement { catalogItemId = "sofa-cafe-01" },
                new PlanPlacement { catalogItemId = "lamp-warm-wood-01", x = 1.4f, z = 4.5f, yawDegrees = 90f });
            Assert.AreEqual(3, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var seat = new Vec2(-0.8f, 2.2f);
            foreach (var p in outcome.Placements)
                Assert.Greater(FacingDot(p.position, p.yawDegrees, seat), 0.99f, $"{p.catalogItemId} at ({p.position.x}, {p.position.z}) yaw {p.yawDegrees}");
            Assert.AreEqual(0f, outcome.Placements.Select(p => p.yawDegrees % Facing.SnapDegrees).Sum(), 1e-3f, "snapped to 15°");
            var state = _session.State.Clone();
            foreach (var p in outcome.Placements) { Assert.AreEqual(PlacementStatus.Ok, PlacementRules.Check(_room, state, _catalog, p), p.catalogItemId); state.placements.Add(p); }
        }

        // The human: in an AI living room the sofa had its back to me. A seat still faces its table, but on the
        // table's side where that also faces the user.
        [Test]
        public void ASofaAtATable_FacesTheTableAndTheUser()
        {
            var outcome = Ask(null,
                new PlanPlacement { catalogItemId = "coffee-table-warm-wood-01", x = 0.9f, z = 2.5f },
                new PlanPlacement { catalogItemId = "sofa-cafe-01", x = 0.0f, z = 2.5f, yawDegrees = 90f }); // between me and the table
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var sofa = outcome.Placements.Single(p => p.catalogItemId == "sofa-cafe-01");
            Assert.GreaterOrEqual(FacingDot(sofa.position, sofa.yawDegrees, new Vec2(-0.8f, 2.2f)), 0f,
                $"sofa at ({sofa.position.x}, {sofa.position.z}) yaw {sofa.yawDegrees} does not turn its back to the user");
        }

        [Test]
        public void AGivenUserPosition_IsWhatPiecesFace()
        {
            var user = new Vec3(0.2f, 1.2f, 4.2f);
            var outcome = Ask(user, new PlanPlacement { catalogItemId = "armchair-cafe-01", x = 0.9f, z = 1.2f, yawDegrees = 180f });
            var p = outcome.Placements.Single();
            Assert.Greater(FacingDot(p.position, p.yawDegrees, user.Floor()), 0.99f, $"yaw {p.yawDegrees}");
        }

        [Test]
        public void ShelvesKeepTheirBackToTheWall_AndRugsAndWallPiecesKeepTheirTurn()
        {
            var outcome = Ask(null,
                new PlanPlacement { catalogItemId = "shelf-cafe-01", x = 1.6f, z = 3.8f, yawDegrees = 270f },
                new PlanPlacement { catalogItemId = "rug-cafe-01", x = 0.9f, z = 2.5f, yawDegrees = 0f });
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var shelf = outcome.Placements[0];
            Assert.AreEqual(270f, shelf.yawDegrees, 1e-3f, "faces out from the x = 1.8 wall, toward the user within 90°");
            var rug = outcome.Placements[1];
            Assert.AreEqual(0f, rug.yawDegrees % 90f, 1e-3f, "a rug has no front: not turned toward the user");
        }
    }
}

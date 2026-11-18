using System;
using System.Collections.Generic;
using System.IO;
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
    /// The catalog trim (content #240) removes 14 ids. A save, a saved space or an AI plan that still names one
    /// is skipped with a log line: no exception, no dead piece. The removed ids are taken out of a copy here, so
    /// these tests pass before and after the trim.
    /// </summary>
    public class CatalogTrimTests
    {
        static readonly string[] Removed =
        {
            "clutter-basket-cafe-01", "clutter-box-cafe-01", "clutter-floor-cushion-cafe-01", "clutter-plant-cafe-01", "clutter-screen-cafe-01",
            "clutter-basket-night-city-01", "clutter-box-night-city-01", "clutter-floor-cushion-night-city-01", "clutter-plant-night-city-01",
            "clutter-screen-night-city-01", "desk-cafe-01", "coffee-table-cafe-01", "wall-art-night-city-01", "rug-night-city-01",
        };

        ICatalog _trimmed;

        [SetUp]
        public void SetUp() =>
            _trimmed = new Catalog(CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog.Items.Where(i => !Removed.Contains(i.id)));

        static DesignState OldDesign()
        {
            var s = new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a"), moodPresetId = "evening" };
            s.SetChoice(new AnchorChoice { anchorId = "a-desk", mode = AnchorMode.Redesign, catalogItemId = "desk-cafe-01", yScale = 1f, xScale = 1f, zScale = 1f });
            s.placements.Add(new FreePlacement { placementId = "old-table", catalogItemId = "coffee-table-cafe-01", position = new Vec3(0.6f, 0f, 2.0f) });
            s.placements.Add(new FreePlacement { placementId = "kept", catalogItemId = "plant-cafe-01", position = new Vec3(0.6f, 0f, 3.6f) });
            s.coverProps.Add(new FreePlacement { placementId = "old-cover", catalogItemId = "clutter-basket-cafe-01", position = new Vec3(-0.6f, 0f, 3.6f) });
            return s;
        }

        [Test]
        public void CannedPlans_NameNoRemovedId() =>
            CollectionAssert.IsEmpty(CannedPlans.Presets.SelectMany(p => p.Items).Intersect(Removed));

        [Test]
        public void OldSave_DropsRemovedPieces_WithALogLine()
        {
            string path = Path.Combine(Path.GetTempPath(), "ownspace-trim-" + Guid.NewGuid().ToString("N") + ".json");
            var log = new List<string>();
            try
            {
                DesignStateJson.SaveToFile(path, OldDesign());
                var session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), _trimmed) { Log = log.Add };
                Assert.IsTrue(session.Load(path));
                Assert.AreEqual(AnchorMode.KeepReal, session.State.ChoiceFor("a-desk").mode);
                CollectionAssert.AreEqual(new[] { "kept" }, session.State.placements.Select(p => p.placementId));
                Assert.AreEqual(0, session.State.coverProps.Count);
                Assert.AreEqual(1, log.Count);
                StringAssert.Contains("a-desk UnknownItem dropped", log[0]);
                StringAssert.Contains("old-table PlacementNotAllowed (UnknownItem) dropped", log[0]);
                StringAssert.Contains("old-cover HideNotAllowed dropped", log[0]);
                Assert.AreEqual(log[0], session.IssuesLine());
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void SavedSpace_WithRemovedPieces_PlaysBackWithoutThem()
        {
            Assert.IsTrue(RoomSnapshot.TryCapture(FakeRoomInfo.DormRoom(), OldDesign(), "Old room", DateTime.UtcNow, new Vec3(-0.9f, 1.2f, 2.2f), out var snap, out var error), error);
            Assert.IsTrue(RoomSnapshotJson.TryFromJson(RoomSnapshotJson.ToJson(snap), out snap, out error), error);
            var room = new SnapshotRoomInfo(snap, RoomPose.AnchorTo(snap, new Vec3(0f, 0f, 0f), 0f, 0f));
            var pieces = new List<VirtualPiece>();
            room.VirtualFurniture(_trimmed, null, pieces);
            var desk = pieces.Single(p => p.Anchor.Id == "a-desk");
            Assert.IsFalse(desk.Chosen, "the removed desk design falls back to a default");
            Assert.IsNotNull(desk.Item);
            Assert.AreNotEqual("desk-cafe-01", desk.Item.id);

            var session = new DesignSession(room, _trimmed, room.VisitState());
            session.SetZone(new List<Vec2> { new Vec2(-0.5f, -0.5f), new Vec2(0.5f, -0.5f), new Vec2(0.5f, 0.5f), new Vec2(-0.5f, 0.5f) });
            Assert.IsFalse(session.State.placements.Any(p => Removed.Contains(p.catalogItemId)));
            StringAssert.Contains("old-table", session.IssuesLine());
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        [Test]
        public void AiPlan_NamingARemovedId_SkipsIt()
        {
            var session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), _trimmed);
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            var plan = new AssistantPlan();
            plan.placements.Add(new PlanPlacement { catalogItemId = "rug-night-city-01" });
            plan.placements.Add(new PlanPlacement { catalogItemId = "plant-cafe-01" });
            var log = new List<string>();
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, session, _trimmed, null, null) { Log = log.Add };
            var outcome = runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "plant-cafe-01" }, outcome.Placements.Select(p => p.catalogItemId));
            CollectionAssert.AreEqual(new[] { "rug-night-city-01: unknown id" }, outcome.Dropped);
            StringAssert.Contains("rug-night-city-01: unknown id", log.Single());
            Assert.IsTrue(runner.Apply(outcome));
        }
    }
}

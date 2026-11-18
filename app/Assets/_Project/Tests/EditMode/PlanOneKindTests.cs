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
    /// Device bug (2427b8d): the "Bright studio" plan placed armchair-scandi-01 twice. An AI plan keeps one piece
    /// of each kind; later ones are skipped with a reason and counted in the summary.
    /// </summary>
    public class PlanOneKindTests
    {
        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        static AssistantOutcome Ask(ICatalog catalog, params string[] ids)
        {
            var session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), catalog);
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            var plan = new AssistantPlan();
            foreach (var id in ids) plan.placements.Add(new PlanPlacement { catalogItemId = id });
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, session, catalog, null, null);
            return runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
        }

        [Test]
        public void TwoArmchairs_PlaceOne()
        {
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            var outcome = Ask(catalog, "armchair-scandi-01", "armchair-scandi-01", "lamp-scandi-01", "armchair-cafe-01");
            CollectionAssert.AreEqual(new[] { "armchair-scandi-01", "lamp-scandi-01" }, outcome.Placements.Select(p => p.catalogItemId),
                string.Join("; ", outcome.Dropped));
            CollectionAssert.AreEqual(new[] { "armchair-scandi-01: one of each kind", "armchair-cafe-01: one of each kind" }, outcome.Dropped);
            Assert.AreEqual("Placed 2 of 4: one of each kind", outcome.Summary);
        }

        // Human: the AI added a second sofa next to the one the user placed by hand.
        [Test]
        public void AKindTheUserAlreadyPlaced_IsSkipped_ButTheLastAiPlansPiecesDoNotCount()
        {
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            var session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), catalog);
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            Assert.IsTrue(session.TryAddPlacement(new FreePlacement { placementId = "p-mine", catalogItemId = "sofa-cafe-01", position = new Vec3(1.35f, 0f, 3.7f), yawDegrees = 270f }, out var status), status.ToString());

            AssistantRunner Runner(params string[] ids)
            {
                var plan = new AssistantPlan();
                foreach (var id in ids) plan.placements.Add(new PlanPlacement { catalogItemId = id });
                return new AssistantRunner(new PlanAssistant { Plan = plan }, session, catalog, null, null);
            }
            var runner = Runner("sofa-scandi-01", "armchair-cafe-01");
            var first = runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "armchair-cafe-01" }, first.Placements.Select(p => p.catalogItemId), string.Join("; ", first.Dropped));
            CollectionAssert.AreEqual(new[] { "sofa-scandi-01: you already have one" }, first.Dropped);
            Assert.AreEqual("Placed 1 of 2: you already have one", first.Summary);
            Assert.IsTrue(runner.Apply(first));

            // The next plan replaces the AI's armchair, so another armchair is fine.
            var second = Runner("armchair-scandi-01").AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "armchair-scandi-01" }, second.Placements.Select(p => p.catalogItemId), string.Join("; ", second.Dropped));
        }

        [Test]
        public void PiecesWithNoKind_AreKeptOncePerId()
        {
            static CatalogItem Box(string id) => new CatalogItem
            {
                id = id, name = id, role = FurnitureRole.Other, kind = "", zones = new List<string> { "outside" },
                size = new Vec3(0.4f, 0.5f, 0.4f),
            };
            var catalog = new Catalog(new[] { Box("box-a"), Box("box-a2") });
            var outcome = Ask(catalog, "box-a", "box-a", "box-a2");
            CollectionAssert.AreEqual(new[] { "box-a", "box-a2" }, outcome.Placements.Select(p => p.catalogItemId), string.Join("; ", outcome.Dropped));
            CollectionAssert.AreEqual(new[] { "box-a: one of each kind" }, outcome.Dropped);
        }
    }
}

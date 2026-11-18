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
    /// Device bug (8fdbcda): Study café, then Bright studio, left both plans' pieces in the room. A new AI plan
    /// replaces the pieces the last AI plan placed; pieces the user placed stay, and one Undo brings the old plan back.
    /// </summary>
    public class AiReplacesAiTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "floor-birch-planks", "floor-dark-planks", "floor-concrete-tiles",
            "wall-cream", "wall-soft-stripes", "wall-olive", "wall-navy-panels" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        ICatalog _catalog;
        DesignSession _session;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public AssistantRequest Seen;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) { Seen = r; return Task.FromResult(Plan); }
        }

        AssistantRunner Runner(IDesignAssistant assistant) => new AssistantRunner(assistant, _session, _catalog, Finishes, Moods, 200);

        // Offline, so each chip lays out its own canned set.
        AssistantOutcome Preset(string id)
        {
            var runner = Runner(new FakeDesignAssistant { FailNext = true });
            var outcome = Task.Run(() => runner.AskPresetAsync(id, CancellationToken.None)).GetAwaiter().GetResult();
            Assert.Greater(outcome.Placements.Count, 0, id + ": " + string.Join("; ", outcome.Dropped));
            Assert.IsTrue(runner.Apply(outcome));
            return outcome;
        }

        static string[] Ids(IEnumerable<FreePlacement> ps) => ps.Select(p => p.catalogItemId + "@" + p.placementId).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        [Test]
        public void StudyCafeThenBrightStudio_LeavesOnlyBrightStudio_AndTheUsersPieces()
        {
            var mine = new FreePlacement { placementId = "p-mine", catalogItemId = "plant-cafe-01", position = new Vec3(1.5f, 0f, 3.3f) };
            Assert.IsTrue(_session.TryAddPlacement(mine, out var status), status.ToString());

            var cafe = Preset("study-cafe");
            Assert.IsTrue(_session.State.placements.Where(p => p.placementId != "p-mine").All(p => p.aiPlaced));
            var afterCafe = Ids(_session.State.placements);

            var studio = Preset("bright-studio");
            var expected = Ids(studio.Placements.Append(mine));
            CollectionAssert.AreEqual(expected, Ids(_session.State.placements), "café pieces were removed");
            Assert.IsFalse(_session.State.placements.Single(p => p.placementId == "p-mine").aiPlaced);

            Assert.IsTrue(_session.Undo(), "one undo step");
            CollectionAssert.AreEqual(afterCafe, Ids(_session.State.placements), "the café plan is back");
            Assert.Greater(cafe.Placements.Count, 0);
        }

        [Test]
        public void TheNewPlan_SeesTheOldAiPieces_AsGone()
        {
            var first = new PlanAssistant { Plan = new AssistantPlan() };
            first.Plan.placements.Add(new PlanPlacement { catalogItemId = "armchair-cafe-01" });
            var runner = Runner(first);
            var one = runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(runner.Apply(one));
            var old = _session.State.placements.Single();

            // The next plan asks for the old armchair's exact spot: it is free, so the new piece lands there.
            var second = new PlanAssistant { Plan = new AssistantPlan() };
            second.Plan.placements.Add(new PlanPlacement { catalogItemId = "armchair-scandi-01", x = old.position.x, z = old.position.z, yawDegrees = old.yawDegrees });
            runner = Runner(second);
            var two = runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.IsEmpty(second.Seen.Design.placements, "the request leaves the old AI pieces out");
            var p = two.Placements.Single();
            Assert.AreEqual(old.position.x, p.position.x, 1e-4f);
            Assert.AreEqual(old.position.z, p.position.z, 1e-4f);
            Assert.IsTrue(runner.Apply(two));
            Assert.AreEqual("armchair-scandi-01", _session.State.placements.Single().catalogItemId);
        }

        [Test]
        public void TheAiMark_IsSaved_AndOldSavesCountAsTheUsers()
        {
            var state = new DesignState();
            state.placements.Add(new FreePlacement { placementId = "ai-1", catalogItemId = "plant-cafe-01", aiPlaced = true });
            state.placements.Add(new FreePlacement { placementId = "p-1", catalogItemId = "plant-cafe-01" });
            Assert.IsTrue(DesignStateJson.TryFromJson(DesignStateJson.ToJson(state), out var read));
            CollectionAssert.AreEqual(new[] { true, false }, read.placements.Select(p => p.aiPlaced));

            string old = System.Text.RegularExpressions.Regex.Replace(DesignStateJson.ToJson(state), @",\s*""aiPlaced"":\s*(true|false)", "");
            StringAssert.DoesNotContain("aiPlaced", old);
            Assert.IsTrue(DesignStateJson.TryFromJson(old, out read));
            Assert.IsTrue(read.placements.All(p => !p.aiPlaced), "a save from before the mark keeps every piece as the user's");
        }
    }
}

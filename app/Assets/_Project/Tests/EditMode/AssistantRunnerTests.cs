using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class AssistantRunnerTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "floor-birch-planks", "wall-cream", "wall-soft-stripes" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        DesignSession _session;
        ICatalog _catalog;
        IRoomInfo _room;

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        /// <summary>Ignores its cancellation token, like a stuck network call.</summary>
        sealed class StubbornAssistant : IDesignAssistant
        {
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) =>
                Task.Delay(5000).ContinueWith(_ => new AssistantPlan(), TaskScheduler.Default);
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = TestRooms.Dorm(TestRooms.SideA);
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        AssistantOutcome Ask(IDesignAssistant assistant, string intent = "make it a cozy living room",
            AssistantTask task = AssistantTask.FillOutside, int timeoutMs = 8000, CancellationToken ct = default) =>
            new AssistantRunner(assistant, _session, _catalog, Finishes, Moods, timeoutMs)
                .AskAsync(task, intent, task == AssistantTask.RecommendDesign ? "a-desk" : null, ct).GetAwaiter().GetResult();

        void AssertAllPiecesPass(AssistantOutcome outcome)
        {
            var state = _session.State.Clone(); // the user's own pieces are obstacles
            foreach (var p in outcome.Placements)
            {
                Assert.AreEqual(PlacementStatus.Ok, PlacementRules.Check(_room, state, _catalog, p), p.catalogItemId);
                state.placements.Add(p);
            }
        }

        // Every canned preset must lay out on the free side of a typical (dorm) room.
        [TestCase("make it a cozy living room")]
        [TestCase("a quiet study corner")]
        [TestCase("cafe with friends")]
        [TestCase("neon night lounge")]
        public void FillOutside_LaysOutEachCannedPlanOnTheFreeFloor(string intent)
        {
            var outcome = Ask(new FakeDesignAssistant(), intent);
            Assert.IsFalse(outcome.Plan.fromFallback);
            Assert.GreaterOrEqual(outcome.Placements.Count, 4, string.Join("; ", outcome.Dropped));
            AssertAllPiecesPass(outcome);
            Assert.AreEqual(CannedPlans.Nearest(intent).Mood, outcome.MoodPresetId);
        }

        [Test]
        public void Apply_KeepsTheUsersPieces_AndOneUndoRestoresEverything()
        {
            Assert.IsTrue(_session.TryAddPlacement(new FreePlacement
            {
                placementId = "mine-1", catalogItemId = "sofa-night-city-01", position = new Vec3(0.9f, 0f, 2.5f), yawDegrees = 90f,
            }, out var status), status.ToString());
            _session.SetFinish(DesignZone.Outside, FinishSurface.Wall, "wall-soft-stripes");
            _session.SetMood("night");
            string before = DesignStateJson.ToJson(_session.State);

            var outcome = Ask(new FakeDesignAssistant());
            AssertAllPiecesPass(outcome);
            Assert.Greater(outcome.Placements.Count, 0);
            Assert.IsFalse(outcome.Placements.Exists(p => p.placementId == "mine-1"));
            new AssistantRunner(new FakeDesignAssistant(), _session, _catalog, Finishes, Moods).Apply(outcome);
            Assert.AreEqual("mine-1", _session.State.placements[0].placementId, "the user's sofa stays");
            Assert.AreEqual(1 + outcome.Placements.Count, _session.State.placements.Count);
            Assert.AreEqual("wall-cream", _session.State.outsideFinish.wallFinishId);

            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(before, DesignStateJson.ToJson(_session.State));
        }

        // 0: the call fails; 1: a slow call times out; 2: an assistant ignoring its token still times out.
        [TestCase(0, "offline")]
        [TestCase(1, "too long")]
        [TestCase(2, "too long")]
        public void FailedOrSlowCall_FallsBackToTheNearestPreset_AndSaysSo(int kind, string reason)
        {
            IDesignAssistant assistant = kind == 0 ? new FakeDesignAssistant { FailNext = true }
                : kind == 1 ? new FakeDesignAssistant(5000) : (IDesignAssistant)new StubbornAssistant();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var outcome = Ask(assistant, "quiet reading corner", timeoutMs: 100);
            Assert.Less(clock.ElapsedMilliseconds, 2000, "well under the stuck call's 5 s");
            Assert.IsTrue(outcome.Plan.fromFallback);
            StringAssert.Contains(reason, outcome.Plan.notice);
            StringAssert.Contains("Study library", outcome.Plan.notice);
            Assert.AreEqual("morning", outcome.MoodPresetId);
            Assert.Greater(outcome.Placements.Count, 0);
        }

        [Test]
        public void UserCancel_IsNotTurnedIntoAFallback() =>
            Assert.That(() => Ask(new FakeDesignAssistant(5000), ct: new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());

        [Test]
        public void ModelMistakes_AreDroppedOrMoved_NeverAppliedUnchecked()
        {
            var plan = new AssistantPlan { floorFinishId = "floor-lava", moodPresetId = "disco", wallFinishId = "wall-cream" };
            plan.placements.Add(new PlanPlacement { catalogItemId = "sofa-warm-wood-01", x = 0.9f, z = 2.5f });   // valid: kept as is
            plan.placements.Add(new PlanPlacement { catalogItemId = "armchair-cafe-01", x = -0.9f, z = 2.5f });  // in my zone: moved
            plan.placements.Add(new PlanPlacement { catalogItemId = "desk-scandi-01", x = 1f, z = 4f });         // my-zone only: dropped
            plan.placements.Add(new PlanPlacement { catalogItemId = "unicorn-01" });                              // unknown: dropped
            var outcome = Ask(new PlanAssistant { Plan = plan });

            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            Assert.AreEqual(0.9f, outcome.Placements[0].position.x, 1e-5f);
            Assert.Greater(outcome.Placements[1].position.x, 0f);
            AssertAllPiecesPass(outcome);
            Assert.AreEqual("", outcome.FloorFinishId);
            Assert.AreEqual("", outcome.MoodPresetId);
            Assert.AreEqual("wall-cream", outcome.WallFinishId);
            Assert.AreEqual(4, outcome.Dropped.Count, string.Join("; ", outcome.Dropped));
        }

        [Test]
        public void Recommend_KeepsOnlyPicksThatFitTheAnchor()
        {
            var plan = new AssistantPlan();
            void Pick(string anchor, string item) => plan.recommendations.Add(new PlanRecommendation { anchorId = anchor, catalogItemId = item, reason = "r" });
            Pick("a-desk", "desk-warm-wood-01");  // fits
            Pick("a-desk", "desk-warm-wood-01");  // duplicate
            Pick("a-desk", "bed-scandi-01");      // wrong role
            Pick("b-desk", "desk-scandi-01");     // not the requested anchor
            Pick("a-desk", "sofa-cafe-01");       // outside-only item
            var outcome = Ask(new PlanAssistant { Plan = plan }, "calm", AssistantTask.RecommendDesign);

            Assert.AreEqual(1, outcome.Recommendations.Count);
            Assert.AreEqual("desk-warm-wood-01", outcome.Recommendations[0].item.id);
            Assert.IsTrue(outcome.Recommendations[0].fit.Fits);
            Assert.AreEqual(4, outcome.Dropped.Count, string.Join("; ", outcome.Dropped));
            Assert.IsFalse(new AssistantRunner(new FakeDesignAssistant(), _session, _catalog, Finishes, Moods).Apply(outcome), "never auto-applied");
        }

        [Test]
        public void Recommend_WithNothingUsable_OffersFittingItems_MoodFirst()
        {
            _session.SetMood("night");
            var outcome = Ask(new PlanAssistant { Plan = new AssistantPlan() }, "anything", AssistantTask.RecommendDesign);
            Assert.That(outcome.Recommendations.Count, Is.InRange(1, 3));
            var desk = FakeRoomInfo.DormRoom().Furniture[1];
            foreach (var (item, fit, _) in outcome.Recommendations)
            {
                Assert.AreEqual(FurnitureRole.Table, item.role);
                Assert.IsTrue(FitRule.Check(desk, item).Fits && fit.Fits, item.id);
            }
            bool sawNonMatch = false;
            foreach (var (item, _, _) in outcome.Recommendations)
            {
                bool match = item.moods.Contains("night");
                Assert.IsFalse(sawNonMatch && match, "mood matches come first");
                sawNonMatch |= !match;
            }
        }

        [Test]
        public void RugAtTheSofaSpot_StaysUnderIt_NotNudged()
        {
            // The live model put the rug exactly where the sofa is. Flat pieces may lie under furniture.
            var plan = new AssistantPlan();
            plan.placements.Add(new PlanPlacement { catalogItemId = "sofa-warm-wood-01", x = 0.9f, z = 2.5f, yawDegrees = 90f });
            plan.placements.Add(new PlanPlacement { catalogItemId = "rug-warm-wood-01", x = 0.9f, z = 2.5f, yawDegrees = 90f });
            var outcome = Ask(new PlanAssistant { Plan = plan });
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            foreach (var p in outcome.Placements)
            {
                Assert.AreEqual(0.9f, p.position.x, 1e-5f, p.catalogItemId);
                Assert.AreEqual(2.5f, p.position.z, 1e-5f, p.catalogItemId);
            }
            // The sofa turns to face the user (the desk seat in my zone); the rug has no front and keeps its turn.
            var seat = Facing.UserSpot(_room, _session.State.zoneOutline, null).Value;
            Assert.AreEqual(Facing.TowardUser(new Vec2(0.9f, 2.5f), seat), outcome.Placements[0].yawDegrees, 1e-5f);
            Assert.AreEqual(90f, outcome.Placements[1].yawDegrees, 1e-5f);
        }

        [Test]
        public void Recommend_WhenOffline_StillOffersFittingItems_AndSaysSo()
        {
            var outcome = Ask(new FakeDesignAssistant { FailNext = true }, "calm", AssistantTask.RecommendDesign);
            Assert.IsTrue(outcome.Plan.fromFallback);
            StringAssert.Contains("offline", outcome.Plan.notice);
            Assert.That(outcome.Recommendations.Count, Is.InRange(1, 3));
            var desk = FakeRoomInfo.DormRoom().Furniture[1];
            foreach (var (item, _, _) in outcome.Recommendations) Assert.IsTrue(FitRule.Check(desk, item).Fits, item.id);
            Assert.AreEqual(0, outcome.Placements.Count, "a recommendation never carries outside pieces");
        }

        [Test]
        public void Recommend_WithNoMood_UsesCatalogOrder()
        {
            Assert.AreEqual("", _session.State.moodPresetId);
            var outcome = Ask(new PlanAssistant { Plan = new AssistantPlan() }, "anything", AssistantTask.RecommendDesign);
            var fits = new System.Collections.Generic.List<(CatalogItem item, FitResult fit)>();
            CatalogQueries.FittingItems(_catalog, FakeRoomInfo.DormRoom().Furniture[1], fits);
            Assert.AreEqual(System.Math.Min(3, fits.Count), outcome.Recommendations.Count);
            for (int i = 0; i < outcome.Recommendations.Count; i++)
            {
                Assert.AreEqual(fits[i].item.id, outcome.Recommendations[i].item.id);
                Assert.AreEqual("Fits this piece.", outcome.Recommendations[i].reason);
            }
        }
    }
}

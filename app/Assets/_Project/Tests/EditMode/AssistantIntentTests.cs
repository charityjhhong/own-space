using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class AssistantIntentTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "floor-birch-planks", "floor-dark-planks", "floor-concrete-tiles",
            "wall-cream", "wall-soft-stripes", "wall-olive", "wall-navy-panels" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        DesignSession _session;
        ICatalog _catalog;

        static IEnumerable<string> PresetIds()
        {
            foreach (var p in CannedPlans.Presets) yield return p.Id;
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _session = new DesignSession(FakeRoomInfo.DormRoom(), _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        AssistantRunner Runner(IDesignAssistant assistant) => new AssistantRunner(assistant, _session, _catalog, Finishes, Moods, 200);

        // Runs off Unity's main-thread context, so status changes are applied directly.
        static T OffMain<T>(Func<Task<T>> call) => Task.Run(call).GetAwaiter().GetResult();

        [Test]
        public void Chips_IncludeTheNewIntents_WithShortNotices()
        {
            foreach (var id in new[] { "study-cafe", "calm-bedroom", "bright-studio", "reading-nook" })
                Assert.IsNotNull(CannedPlans.ById(id), id);
            Assert.IsNull(CannedPlans.ById("nope"));
            foreach (var preset in CannedPlans.Presets)
            {
                Assert.AreSame(preset, CannedPlans.Nearest(preset.Intent), preset.Id + ": its own intent maps back to it");
                Assert.LessOrEqual(AssistantRunner.Notice("You're offline", preset).Length, 60, preset.Id);
                Assert.LessOrEqual(AssistantRunner.Notice("Took too long", preset).Length, 60, preset.Id);
            }
        }

        [Test]
        public void SurpriseMe_ReachesEveryPreset()
        {
            var random = new Random(7);
            var seen = new HashSet<string>();
            for (int i = 0; i < 400; i++) seen.Add(CannedPlans.Surprise(random).Id);
            Assert.AreEqual(CannedPlans.Presets.Count, seen.Count);
        }

        [TestCaseSource(nameof(PresetIds))]
        public void EachChip_FallsBackToItsOwnPreset_AndLaysOut(string id)
        {
            var preset = CannedPlans.ById(id);
            var outcome = OffMain(() => Runner(new FakeDesignAssistant { FailNext = true }).AskPresetAsync(id, CancellationToken.None));
            Assert.IsTrue(outcome.Plan.fromFallback);
            Assert.AreEqual("You're offline — here's “" + preset.Label + "”", outcome.Plan.notice);
            Assert.AreEqual(preset.Mood, outcome.MoodPresetId);
            Assert.GreaterOrEqual(outcome.Placements.Count, 3, string.Join("; ", outcome.Dropped));
        }

        [Test]
        public void Status_FollowsAskAndApply()
        {
            var runner = Runner(new FakeDesignAssistant());
            var seen = new List<AssistantState>();
            runner.Changed += () => seen.Add(runner.State);

            var outcome = OffMain(() => runner.AskPresetAsync("bright-studio", CancellationToken.None));
            Assert.AreEqual(AssistantState.Idle, runner.State);
            Assert.AreEqual(outcome.Plan.rationale, runner.Message);
            runner.Apply(outcome);
            Assert.AreEqual(AssistantState.Applied, runner.State);
            Assert.AreEqual($"Added {outcome.Placements.Count} pieces", runner.Message);
            CollectionAssert.AreEqual(new[] { AssistantState.Asking, AssistantState.Idle, AssistantState.Applied }, seen);
        }

        [Test]
        public void Status_ShowsFallbackAndCancel()
        {
            var runner = Runner(new FakeDesignAssistant(5000));
            OffMain(() => runner.AskPresetAsync("reading-nook", CancellationToken.None));
            Assert.AreEqual(AssistantState.Fallback, runner.State);
            Assert.AreEqual("Took too long — here's “Reading nook”", runner.Message);

            Assert.That(() => OffMain(() => runner.AskPresetAsync("cafe", new CancellationToken(true))),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.AreEqual(AssistantState.Idle, runner.State);
        }

        [Test]
        public void FakeStatus_DrivesAPanel()
        {
            var status = new FakeAssistantStatus();
            int changes = 0;
            status.Changed += () => changes++;
            status.Set(AssistantState.Fallback, "You're offline — here's “Café”");
            Assert.AreEqual(AssistantState.Fallback, status.State);
            Assert.AreEqual(1, changes);
        }
    }
}

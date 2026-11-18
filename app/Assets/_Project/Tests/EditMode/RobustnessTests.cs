using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>Whole-design save/load, broken saves, long undo/redo runs and zone changes over a full design.</summary>
    public class RobustnessTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "floor-birch-planks", "wall-cream", "wall-soft-stripes" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        ICatalog _catalog;
        DesignSession _session;
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _session = new DesignSession(FakeRoomInfo.DormRoom(), _catalog);
            _dir = Path.Combine(Path.GetTempPath(), "ownspace-robust-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_dir, true);

        string PathFor(string name) => Path.Combine(_dir, name);
        string Json() => DesignStateJson.ToJson(_session.State);
        CatalogItem Item(string id) { _catalog.TryGet(id, out var item); return item; }

        // Every feature: zone, two redesigns (with scales), an AI batch outside, a manual piece,
        // a hide box, a cover prop, finishes in both zones, a mood.
        void BuildFullDesign()
        {
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
            Assert.AreEqual(RedesignOutcome.Applied, _session.Redesign("a-desk", Item("desk-warm-wood-01"), out _));
            Assert.AreEqual(RedesignOutcome.Applied, _session.Redesign("a-bed", Item("bed-scandi-01"), out _));
            var runner = new AssistantRunner(new FakeDesignAssistant(), _session, _catalog, Finishes, Moods);
            var outcome = runner.AskAsync(AssistantTask.FillOutside, "cozy", null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(runner.Apply(outcome));
            Assert.Greater(_session.State.placements.Count, 2);
            Assert.IsTrue(_session.TryAddHideBox(new HideBox
            {
                hideBoxId = "h1", position = new Vec3(-0.5f, 0f, 2.0f), size = new Vec3(0.5f, 0.4f, 0.5f), finishId = "floor-oak-planks",
            }, out var hide), hide.ToString());
            Assert.IsTrue(_session.TryAddCoverProp(new FreePlacement
            {
                placementId = "c1", catalogItemId = "clutter-basket-warm-wood-01", position = new Vec3(-0.5f, 0f, 3.6f),
            }, out var prop), prop.ToString());
            _session.SetFinish(DesignZone.Mine, FinishSurface.Wall, "wall-soft-stripes");
            _session.SetMood("night");
        }

        [Test]
        public void FullDesign_SurvivesSaveAndLoad_Exactly()
        {
            BuildFullDesign();
            string before = Json();
            _session.Save(PathFor("design.json"));

            var restored = new DesignSession(FakeRoomInfo.DormRoom(), _catalog);
            Assert.IsTrue(restored.Load(PathFor("design.json")));
            CollectionAssert.IsEmpty(restored.Issues, "nothing dropped on reload");
            Assert.AreEqual(before, DesignStateJson.ToJson(restored.State));
        }

        [TestCase("{\"version\":2,\"zoneOutline\":[{\"x\":-1.8,")]            // cut off mid-write
        [TestCase("\u0000\u0000\u0000")]                                        // zero-filled block
        [TestCase("{\"version\":99}")]                                          // from a newer app
        public void BrokenSave_LoadsNothing_AndIsBackedUp(string content)
        {
            string path = PathFor("design.json");
            File.WriteAllText(path, content);
            BuildFullDesign();
            string before = Json();

            Assert.IsFalse(_session.Load(path));
            Assert.AreEqual(before, Json(), "the current design is untouched");
            Assert.AreEqual(content, File.ReadAllText(path + ".bad"), "the broken file is kept for inspection");

            var fresh = new DesignSession(FakeRoomInfo.DormRoom(), _catalog);
            Assert.IsFalse(fresh.Load(path));
            Assert.AreEqual(0, fresh.State.placements.Count, "a fresh start stays empty, not half-loaded");
        }

        [Test]
        public void BrokenOnboardingSave_IsBackedUp_AndStartsFresh()
        {
            string path = PathFor("onboarding.json");
            File.WriteAllText(path, "{\"version\":1,\"step\":");
            Assert.IsFalse(OnboardingJson.TryLoadFromFile(path, out var state));
            Assert.AreEqual(OnboardingStep.Scan, state.step);
            Assert.AreEqual("{\"version\":1,\"step\":", File.ReadAllText(path + ".bad"));
            Assert.IsFalse(OnboardingJson.TryLoadFromFile(PathFor("missing.json"), out _));
            Assert.IsFalse(File.Exists(PathFor("missing.json.bad")), "a missing file is not 'broken'");
        }

        [Test]
        public void PartialSave_WithMissingFields_LoadsWithDefaults()
        {
            string path = PathFor("design.json");
            File.WriteAllText(path, "{\"version\":2,\"moodPresetId\":\"evening\"}");
            Assert.IsTrue(_session.Load(path));
            Assert.AreEqual("evening", _session.State.moodPresetId);
            Assert.IsNotNull(_session.State.hideBoxes);
            Assert.IsNotNull(_session.State.coverProps);
            Assert.AreEqual("", _session.State.outsideFinish.floorFinishId);
            Assert.IsFalse(File.Exists(path + ".bad"));
        }

        [TestCase(50, 0)]
        [TestCase(60, 10)]
        public void FiftyMixedSteps_UndoAndRedo_RetraceEveryState(int steps, int forgotten)
        {
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
            var states = new List<string> { Json() };
            var random = new Random(42);
            string[] moods = { "morning", "afternoon", "evening", "night" };
            int made = 0;
            while (made < steps)
            {
                int n = _session.State.placements.Count;
                bool changed;
                switch (random.Next(7))
                {
                    case 0: changed = MoodChange(moods[random.Next(4)]); break;
                    case 1: changed = FinishChange(random.Next(2) == 0 ? "floor-oak-planks" : "floor-birch-planks"); break;
                    case 2: changed = _session.TryAddPlacement(Piece("p" + made, 0.4f + 0.2f * random.Next(3), 1.2f + 0.6f * random.Next(6)), out _); break;
                    case 3: changed = n > 0 && _session.RemovePlacement(_session.State.placements[random.Next(n)].placementId); break;
                    case 4: changed = _session.Redesign("a-desk", Item(random.Next(2) == 0 ? "desk-warm-wood-01" : "desk-scandi-01"), out _) == RedesignOutcome.Applied; break;
                    case 5: changed = _session.KeepReal("a-desk"); break;
                    default: changed = _session.TryAddHideBox(new HideBox { hideBoxId = "h" + made, position = new Vec3(-0.5f, 0f, 1.6f + 0.2f * random.Next(8)), size = new Vec3(0.3f, 0.3f, 0.3f) }, out _); break;
                }
                if (!changed) continue;
                made++;
                states.Add(Json());
            }

            for (int i = steps - 1; i >= forgotten; i--)
            {
                Assert.IsTrue(_session.Undo(), "undo " + i);
                Assert.AreEqual(states[i], Json(), "after undoing step " + (i + 1));
            }
            Assert.IsFalse(_session.Undo(), $"only {DesignSession.MaxUndo} steps are kept");
            for (int i = forgotten + 1; i <= steps; i++)
            {
                Assert.IsTrue(_session.Redo(), "redo " + i);
                Assert.AreEqual(states[i], Json(), "after redoing step " + i);
            }
            Assert.IsFalse(_session.Redo());
        }

        bool MoodChange(string mood)
        {
            bool changed = _session.State.moodPresetId != mood;
            _session.SetMood(mood);
            return changed;
        }

        bool FinishChange(string id)
        {
            bool changed = _session.State.outsideFinish.floorFinishId != id;
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, id);
            return changed;
        }

        static FreePlacement Piece(string id, float x, float z) =>
            new FreePlacement { placementId = id, catalogItemId = "plant-warm-wood-01", position = new Vec3(x, 0f, z) };

        [Test]
        public void GrowingTheZone_OverAFullDesign_DropsAndReportsWhatNoLongerFits()
        {
            BuildFullDesign();
            int outside = _session.State.placements.Count;
            string before = Json();

            // The whole room becomes my zone: every outside piece must go; redesigns, hiding and props stay.
            _session.SetZone(new[] { new Vec2(-1.8f, 0f), new Vec2(1.8f, 0f), new Vec2(1.8f, 5f), new Vec2(-1.8f, 5f) });
            Assert.AreEqual(0, _session.State.placements.Count);
            Assert.AreEqual(outside, _session.Issues.Count);
            foreach (var issue in _session.Issues)
            {
                Assert.AreEqual(DesignIssueKind.PlacementNotAllowed, issue.Kind);
                Assert.AreEqual(PlacementStatus.InMyZone, issue.Placement);
                Assert.IsTrue(issue.Dropped);
            }
            Assert.AreEqual(2, _session.State.anchorChoices.Count);
            Assert.AreEqual(1, _session.State.hideBoxes.Count);
            Assert.AreEqual(1, _session.State.coverProps.Count);

            // Shrinking it to nothing on side a drops the redesigns, hiding and props too, with reasons.
            _session.SetZone(new[] { new Vec2(0.5f, 0f), new Vec2(1.8f, 0f), new Vec2(1.8f, 5f), new Vec2(0.5f, 5f) });
            Assert.AreEqual(0, _session.State.anchorChoices.Count);
            Assert.AreEqual(0, _session.State.hideBoxes.Count);
            Assert.AreEqual(0, _session.State.coverProps.Count);
            Assert.AreEqual(4, _session.Issues.Count, string.Join(", ", _session.Issues));

            Assert.IsTrue(_session.Undo());
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(before, Json(), "two undos bring the whole design back");
        }
    }
}

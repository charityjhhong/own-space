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
    /// Preset sets (docs/design/presets.md): a preset brings its own finishes and mood, asks only for pieces
    /// tagged "set:&lt;preset id&gt;", and its pieces are arranged as a set. Each test starts from a copy of the
    /// catalog with no set tags and adds its own, so it passes whether or not catalog.json carries content's sets.
    /// </summary>
    public class PresetSetTests
    {
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };
        static readonly string[] Finishes = CannedPlans.Presets.SelectMany(p => new[] { p.Floor, p.Wall }).Distinct().ToArray();
        static readonly string[] StudyCafeSet = { "coffee-table-warm-wood-01", "armchair-cafe-01", "rug-cafe-01", "pendant-cafe-01", "shelf-cafe-01", "plant-cafe-01" };

        ICatalog _catalog;
        DesignSession _session;
        IRoomInfo _room;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            // Content tags the real catalog's sets; these tests choose their own, so start from none.
            foreach (var item in _catalog.Items) item.tags?.RemoveAll(t => t.StartsWith(PresetSets.TagPrefix, System.StringComparison.Ordinal));
            _room = TestRooms.Dorm(TestRooms.SideA); // outside: all of side b, x 0..1.8, z 0..5
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        void Tag(string set, params string[] ids)
        {
            foreach (var id in ids)
            {
                Assert.IsTrue(_catalog.TryGet(id, out var item), id);
                item.tags.Add("set:" + set);
            }
        }

        sealed class Recording : IDesignAssistant
        {
            public AssistantPlan Plan = new AssistantPlan();
            public bool Fail;
            public AssistantRequest Last;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct)
            {
                Last = r;
                return Fail ? Task.FromException<AssistantPlan>(new System.Exception("offline")) : Task.FromResult(Plan);
            }
        }

        AssistantRunner Runner(IDesignAssistant a) => new AssistantRunner(a, _session, _catalog, Finishes, Moods);

        static AssistantOutcome Wait(Task<AssistantOutcome> t) => t.GetAwaiter().GetResult();

        [Test]
        public void Preset_AppliesItsFinishesAndMoodWithThePieces_AsOneUndo()
        {
            var preset = CannedPlans.ById("study-cafe");
            var model = new Recording();
            model.Plan.floorFinishId = "floor-concrete-tiles"; model.Plan.wallFinishId = "wall-navy-panels"; model.Plan.moodPresetId = "night";
            model.Plan.placements.Add(new PlanPlacement { catalogItemId = "plant-cafe-01" });
            var runner = Runner(model);
            string before = DesignStateJson.ToJson(_session.State);

            var outcome = Wait(runner.AskPresetAsync(preset.Id, CancellationToken.None));
            Assert.AreEqual(preset.Floor, outcome.FloorFinishId, "the preset's look wins over the model's");
            Assert.AreEqual(preset.Wall, outcome.WallFinishId);
            Assert.AreEqual(preset.Mood, outcome.MoodPresetId);
            Assert.IsTrue(runner.Apply(outcome));
            Assert.AreEqual(preset.Floor, _session.State.outsideFinish.floorFinishId);
            Assert.AreEqual(preset.Wall, _session.State.outsideFinish.wallFinishId);
            Assert.AreEqual(preset.Mood, _session.State.moodPresetId);
            Assert.AreEqual(1, _session.State.placements.Count);

            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(before, DesignStateJson.ToJson(_session.State), "one undo removes pieces, finishes and mood");

            var free = Wait(Runner(model).AskAsync(AssistantTask.FillOutside, "a study café", null, CancellationToken.None));
            Assert.AreEqual("night", free.MoodPresetId, "free text keeps the model's look");
        }

        [Test]
        public void Preset_AsksOnlyForItsTaggedSet_FreeTextStaysUnrestricted()
        {
            Tag("study-cafe", StudyCafeSet);
            var model = new Recording();
            Wait(Runner(model).AskPresetAsync("study-cafe", CancellationToken.None));
            CollectionAssert.AreEquivalent(StudyCafeSet, model.Last.AllowedItems.Select(i => i.id));
            Assert.AreEqual("set:study-cafe", PresetSets.TagFor(CannedPlans.ById("study-cafe")));

            Wait(Runner(model).AskAsync(AssistantTask.FillOutside, "a study café", null, CancellationToken.None));
            Assert.AreEqual(_catalog.Items.Count, model.Last.AllowedItems.Count);

            Wait(Runner(model).AskPresetAsync("calm-bedroom", CancellationToken.None));
            Assert.AreEqual(_catalog.Items.Count, model.Last.AllowedItems.Count, "a preset with no set yet uses the whole catalog");
        }

        [Test]
        public void Offline_PresetUsesItsSet_OrItsCannedPiecesWithoutOne()
        {
            Tag("study-cafe", StudyCafeSet);
            var outcome = Wait(Runner(new Recording { Fail = true }).AskPresetAsync("study-cafe", CancellationToken.None));
            Assert.IsTrue(outcome.Plan.fromFallback);
            CollectionAssert.AreEquivalent(StudyCafeSet, outcome.Plan.placements.Select(p => p.catalogItemId));

            var bedroom = CannedPlans.ById("calm-bedroom");
            outcome = Wait(Runner(new Recording { Fail = true }).AskPresetAsync(bedroom.Id, CancellationToken.None));
            CollectionAssert.AreEqual(bedroom.Items, outcome.Plan.placements.Select(p => p.catalogItemId));

            Tag("calm-bedroom", "rug-scandi-01", "plant-scandi-01"); // two are not a set
            Assert.AreEqual(0, PresetSets.Items(_catalog, bedroom).Count);
        }

        // The model's spots break every rule: pieces scattered away from the table and off the walls.
        [Test]
        public void SetPieces_AreArrangedAroundTheTable_AgainstWalls_AndInCorners()
        {
            var plan = new AssistantPlan();
            void Put(string id, float x, float z) => plan.placements.Add(new PlanPlacement { catalogItemId = id, x = x, z = z });
            Put("rug-scandi-01", 0.9f, 4.2f);          // listed before the table
            Put("armchair-cafe-01", 0.5f, 4.4f);
            Put("coffee-table-warm-wood-01", 0.9f, 2.5f);
            Put("sofa-cafe-01", 1.3f, 4.4f);
            Put("pendant-cafe-01", 0.4f, 1.2f);
            Put("shelf-scandi-01", 0.9f, 1.4f);
            Put("plant-warm-wood-01", 0.9f, 3.8f);
            var outcome = Wait(Runner(new Recording { Plan = plan }).AskAsync(AssistantTask.FillOutside, "study café", null, CancellationToken.None));
            Assert.AreEqual(7, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var by = outcome.Placements.ToDictionary(p => p.catalogItemId);
            var table = by["coffee-table-warm-wood-01"].position;
            Assert.AreEqual("coffee-table-warm-wood-01", outcome.Placements[0].catalogItemId, "the table is placed first");
            Assert.AreEqual(0.9f, table.x, 1e-4f, "the table keeps the model's valid spot");

            Assert.AreEqual(0f, Flat(by["rug-scandi-01"].position, table), 1e-4f, "rug under the table");
            Assert.AreEqual(0f, Flat(by["pendant-cafe-01"].position, table), 1e-4f, "pendant above the table");
            Assert.Greater(by["pendant-cafe-01"].position.y, 1.9f);
            foreach (var seat in new[] { "armchair-cafe-01", "sofa-cafe-01" })
            {
                var p = by[seat];
                Assert.Less(Flat(p.position, table), 1.6f, seat + " next to the table");
                var facing = FloorGeometry.Rotate(new Vec2(0f, 1f), p.yawDegrees);
                Vec2 to = new Vec2(table.x - p.position.x, table.z - p.position.z);
                float len = (float)System.Math.Sqrt(to.x * to.x + to.y * to.y);
                Assert.Greater((facing.x * to.x + facing.y * to.y) / len, 0.99f, seat + " faces the table");
            }
            var shelf = by["shelf-scandi-01"];
            var back = FloorGeometry.Rotate(new Vec2(0f, -0.175f), shelf.yawDegrees);
            float bx = shelf.position.x + back.x, bz = shelf.position.z + back.y;
            Assert.IsTrue(System.Math.Abs(bx - 1.8f) < 0.02f || bz < 0.02f || bz > 4.98f, $"shelf back against a wall: ({bx}, {bz})");
            var plant = by["plant-warm-wood-01"].position;
            Assert.IsTrue(new[] { new Vec2(1.8f, 0f), new Vec2(1.8f, 5f) }.Any(c => Flat(plant, new Vec3(c.x, 0f, c.y)) < 0.5f), $"plant in a corner: {plant.x}, {plant.z}");

            var state = _session.State.Clone();
            foreach (var p in outcome.Placements)
            {
                Assert.AreEqual(PlacementStatus.Ok, PlacementRules.Check(_room, state, _catalog, p), p.catalogItemId);
                state.placements.Add(p);
            }
        }

        [Test]
        public void ModelSpotsThatFollowTheRules_AreKept()
        {
            var plan = new AssistantPlan();
            plan.placements.Add(new PlanPlacement { catalogItemId = "coffee-table-warm-wood-01", x = 0.9f, z = 2.5f });
            plan.placements.Add(new PlanPlacement { catalogItemId = "armchair-cafe-01", x = 0.9f, z = 3.5f, yawDegrees = 180f });
            plan.placements.Add(new PlanPlacement { catalogItemId = "shelf-scandi-01", x = 1.625f, z = 4.0f, yawDegrees = 270f });
            var outcome = Wait(Runner(new Recording { Plan = plan }).AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None));
            Assert.AreEqual(3, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            Assert.AreEqual(3.5f, outcome.Placements[1].position.z, 1e-4f);
            Assert.AreEqual(180f, outcome.Placements[1].yawDegrees, 1e-4f);
            Assert.AreEqual(4.0f, outcome.Placements[2].position.z, 1e-4f);
            Assert.AreEqual(1.625f, outcome.Placements[2].position.x, 1e-4f);
        }

        static float Flat(Vec3 a, Vec3 b) => (float)System.Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
    }
}

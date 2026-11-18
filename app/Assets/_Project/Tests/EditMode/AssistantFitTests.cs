using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>
    /// Device bug: in a small study room the live plan placed 0 pieces and skipped 6, with no reasons.
    /// The outside zone was a narrow sliver. These tests use the dorm room with my zone covering all but
    /// a strip along side b (x from <c>edge</c> to the wall at 1.8), and only the real furniture a test names.
    /// </summary>
    public class AssistantFitTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "wall-cream" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };
        static readonly string[] Reasons =
            { "unknown id", "not for outside", "no free floor outside your zone", "overlaps other pieces", "would block a door", "would block a window", "too big for the free floor", "blocked by real furniture" };

        ICatalog _catalog;
        DesignSession _session;
        IRoomInfo _room;
        List<Vec2> _zone;

        static List<Vec2> ZoneUpTo(float edge) => new List<Vec2>
        {
            new Vec2(-FakeRoomInfo.HalfWidth, 0f), new Vec2(edge, 0f), new Vec2(edge, FakeRoomInfo.Length), new Vec2(-FakeRoomInfo.HalfWidth, FakeRoomInfo.Length),
        };

        void Setup(float edge, params string[] furniture)
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = TestRooms.Dorm(furniture);
            _session = new DesignSession(_room, _catalog);
            _zone = ZoneUpTo(edge);
            _session.SetZone(_zone);
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        // An AI plan keeps one piece of each kind, so a test that needs several of one piece gets copies of
        // different kinds ("id#1", "id#2"...).
        string[] Copies(string id, int n)
        {
            var item = _catalog.Items.First(i => i.id == id);
            var copies = Enumerable.Range(1, n).Select(k =>
            {
                var c = Newtonsoft.Json.JsonConvert.DeserializeObject<CatalogItem>(Newtonsoft.Json.JsonConvert.SerializeObject(item));
                c.id = id + "#" + k;
                c.kind = item.kind + "-" + k;
                return c;
            }).ToList();
            _catalog = new Catalog(_catalog.Items.Concat(copies));
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(_zone);
            return copies.Select(c => c.id).ToArray();
        }

        static AssistantPlan Plan(params string[] ids)
        {
            var plan = new AssistantPlan { rationale = "r" };
            foreach (var id in ids) plan.placements.Add(new PlanPlacement { catalogItemId = id });
            return plan;
        }

        AssistantRunner Runner(AssistantPlan plan, List<string> log = null) =>
            new AssistantRunner(new PlanAssistant { Plan = plan }, _session, _catalog, Finishes, Moods) { Log = log == null ? null : (System.Action<string>)log.Add };

        AssistantOutcome Ask(AssistantRunner runner) =>
            runner.AskAsync(AssistantTask.FillOutside, "study café", null, CancellationToken.None).GetAwaiter().GetResult();

        void AssertReasons(AssistantOutcome outcome)
        {
            foreach (var d in outcome.Dropped)
                Assert.IsTrue(Reasons.Any(r => d.EndsWith(": " + r)), d);
            Assert.LessOrEqual(outcome.Summary.Length, 60, outcome.Summary);
            var state = _session.State.Clone();
            foreach (var p in outcome.Placements)
            {
                Assert.AreEqual(PlacementStatus.Ok, PlacementRules.Check(_room, state, _catalog, p), p.catalogItemId);
                state.placements.Add(p);
            }
        }

        // A 50 cm strip: the 25 cm grid has no spot for a 40 cm lamp, the 10 cm grid does.
        [Test]
        public void NarrowSliver_StillGetsTheSmallPiece_AndSaysWhyTheRestDidNot()
        {
            Setup(1.3f);
            var log = new List<string>();
            var runner = Runner(Plan("lamp-warm-wood-01", "sofa-warm-wood-01", "unicorn-01"), log);
            var outcome = Ask(runner);

            Assert.AreEqual(1, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            Assert.AreEqual("lamp-warm-wood-01", outcome.Placements[0].catalogItemId);
            CollectionAssert.AreEqual(new[] { "sofa-warm-wood-01: too big for the free floor", "unicorn-01: unknown id" }, outcome.Dropped);
            Assert.AreEqual("Placed 1 of 3: not enough free floor", outcome.Summary);
            AssertReasons(outcome);

            Assert.AreEqual(1, log.Count, "one line per run");
            StringAssert.Contains("1 placed, 2 skipped", log[0]);
            StringAssert.Contains("sofa-warm-wood-01: too big for the free floor", log[0]);

            Assert.IsTrue(runner.Apply(outcome));
            Assert.AreEqual("Placed 1 of 3: not enough free floor", runner.Message, "the reason shows whenever pieces were skipped");
        }

        // An 88 cm strip: the 90 cm deep sofa does not fit, the 80 cm café sofa does.
        [Test]
        public void TooBigPiece_IsSwappedForASmallerOneOfTheSameKind()
        {
            Setup(0.92f);
            var outcome = Ask(Runner(Plan("sofa-warm-wood-01", "plant-cafe-01")));
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            Assert.AreEqual("sofa-cafe-01", outcome.Placements[0].catalogItemId);
            CollectionAssert.AreEqual(new[] { "sofa-warm-wood-01 -> sofa-cafe-01" }, outcome.Swaps);
            Assert.AreEqual(0, outcome.Dropped.Count);
            Assert.AreEqual("", outcome.Summary);
            AssertReasons(outcome);
            StringAssert.Contains("Swapped: sofa-warm-wood-01 -> sofa-cafe-01", outcome.LogLine());
        }

        // Device: Add refused pieces over real furniture the virtual room hides. Outside my zone it is hidden,
        // so its floor is free for the assistant and for placement (HiddenFurnitureTests).
        [Test]
        public void RealFurnitureOutsideMyZone_IsHidden_SoItsFloorIsFree()
        {
            Setup(0.92f);
            double areaWithout = FreeArea();
            Setup(0.92f, "b-desk"); // x 1.2..1.8, z 1.55..2.85, 0.75 m high
            Assert.AreEqual(areaWithout, FreeArea(), 1e-6, "the hidden desk's floor is free");

            var state = _session.State.Clone();
            var lamp = new FreePlacement { placementId = "x", catalogItemId = "lamp-warm-wood-01", position = new Vec3(1.5f, 0f, 2.2f) };
            Assert.AreEqual(PlacementStatus.Ok, PlacementRules.Check(_room, state, _catalog, lamp));
            var runner = Runner(Plan(Copies("sofa-cafe-01", 3)));
            var outcome = Ask(runner);
            Assert.AreEqual(3, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            AssertReasons(outcome);
        }

        double FreeArea()
        {
            var request = new AssistantRequest(AssistantTask.FillOutside, "cafe", _room, _session.State.Clone(), _catalog.Items);
            return (double)JObject.Parse(AssistantRequestJson.Build(request, Finishes, Moods))["room"]["free"]["area"];
        }

        // Device: a pendant lamp stood on the floor and hung to desk height.
        [Test]
        public void HangingPieces_HangFromTheCeiling_NotTheFloor()
        {
            Setup(0f);
            var outcome = Ask(Runner(Plan("pendant-warm-wood-01", "lamp-warm-wood-01")));
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            Assert.AreEqual(2.0f, outcome.Placements[0].position.y, 1e-4f, "1.2 m cord under a 2.6 m ceiling: shade kept at 2.0 m");
            Assert.AreEqual(0f, outcome.Placements[1].position.y, "a floor lamp stands on the floor");

            var shortCord = new CatalogItem { id = "p", kind = "pendant-lamp", size = new Vec3(0.3f, 0.4f, 0.3f) };
            Assert.AreEqual(2.2f, PlacementRules.MountY(_room, shortCord), 1e-4f, "ceiling minus the cord");
            Assert.AreEqual(2.0f, PlacementRules.MountY(FakeRoomInfo.Empty(), _catalog.Items.First(i => i.id == "pendant-warm-wood-01")), 1e-4f, "no scan: a 2.5 m ceiling");
            Assert.IsTrue(PlacementRules.IsHanging(new CatalogItem { kind = "chandelier", tags = new List<string> { "ceiling" } }));

            // Manual placement too: the session lifts it whatever height it is given.
            Assert.IsTrue(_session.TryAddPlacement(new FreePlacement { placementId = "m", catalogItemId = "pendant-cafe-01", position = new Vec3(0.9f, 0f, 2.5f) }, out var status), status.ToString());
            Assert.AreEqual(2.0f, _session.State.placements.Last().position.y, 1e-4f);
            Assert.IsTrue(_session.TryMovePlacement("m", new Vec3(0.9f, 0f, 3f), 0f, out status), status.ToString());
            Assert.AreEqual(2.0f, _session.State.placements.Last().position.y, 1e-4f);
            Assert.IsTrue(_session.TryAddPlacement(new FreePlacement { placementId = "s", catalogItemId = "sofa-cafe-01", position = new Vec3(0.9f, 0f, 3f), yawDegrees = 90f }, out status),
                "a sofa fits under the pendant: " + status);
        }

        [Test]
        public void NoFreeFloor_SaysSo_OnThePanel()
        {
            Setup(FakeRoomInfo.HalfWidth);
            var runner = Runner(Plan("lamp-warm-wood-01", "plant-cafe-01"));
            var outcome = Ask(runner);
            Assert.AreEqual(0, outcome.Placements.Count);
            Assert.AreEqual("Not enough free floor outside your zone", outcome.Summary);
            CollectionAssert.AreEqual(new[] { "lamp-warm-wood-01: no free floor outside your zone", "plant-cafe-01: no free floor outside your zone" }, outcome.Dropped);
            runner.Apply(outcome);
            Assert.AreEqual("Not enough free floor outside your zone", runner.Message);
        }

        [Test]
        public void FullFreeFloor_SaysOverlaps_NotTooBig()
        {
            Setup(0.92f);
            var outcome = Ask(Runner(Plan(Copies("sofa-cafe-01", 6))));
            Assert.Greater(outcome.Placements.Count, 0);
            Assert.Greater(outcome.Dropped.Count, 0, "six 1.5 m sofas cannot share a 5 m strip");
            foreach (var d in outcome.Dropped) StringAssert.EndsWith(": overlaps other pieces", d);
            AssertReasons(outcome);
        }

        [Test]
        public void Request_SendsTheFreeFloor_AndOnlyPiecesThatFitThere()
        {
            Setup(0.92f);
            var request = new AssistantRequest(AssistantTask.FillOutside, "cafe", _room, _session.State.Clone(), _catalog.Items);
            var json = JObject.Parse(AssistantRequestJson.Build(request, Finishes, Moods));
            var free = json["room"]["free"];
            Assert.AreEqual(0.92, (double)free["x0"], 0.11);
            Assert.AreEqual(1.8, (double)free["x1"], 0.11);
            Assert.AreEqual(0.88 * 5.0, (double)free["area"], 0.6);
            var ids = json["catalog"].Select(t => (string)t).ToList();
            CollectionAssert.Contains(ids, "sofa-cafe-01");
            CollectionAssert.DoesNotContain(ids, "sofa-warm-wood-01", "90 cm deep: does not fit the 88 cm strip");
            CollectionAssert.DoesNotContain(ids, "rug-cafe-01");
        }

        // Repro: PlanChecker runs on a worker thread while the main thread checks the drag preview every
        // frame. With shared scratch buffers the two calls overwrote each other's footprints.
        [Test]
        public void Check_IsSafeOnTwoThreadsAtOnce()
        {
            Setup(0f);
            var room = _room;
            var state = _session.State.Clone();
            var fits = new FreePlacement { placementId = "a", catalogItemId = "lamp-warm-wood-01", position = new Vec3(0.9f, 0f, 2.5f) };
            var outsideRoom = new FreePlacement { placementId = "b", catalogItemId = "sofa-cafe-01", position = new Vec3(1.7f, 0f, 2.5f) };
            int wrong = 0;
            var worker = Task.Run(() =>
            {
                for (int i = 0; i < 200000; i++)
                    if (PlacementRules.Check(room, state, _catalog, fits) != PlacementStatus.Ok) Interlocked.Increment(ref wrong);
            });
            while (!worker.IsCompleted)
                if (PlacementRules.Check(room, state, _catalog, outsideRoom) != PlacementStatus.OutsideRoom) Interlocked.Increment(ref wrong);
            worker.Wait();
            Assert.AreEqual(0, wrong);
        }
    }
}

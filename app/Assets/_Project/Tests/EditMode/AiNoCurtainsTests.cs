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
    /// Human: the Bedroom preset placed curtains and their spot was always wrong. No AI plan, preset set or
    /// offline plan places a window piece; the user adds curtains by hand.
    /// </summary>
    public class AiNoCurtainsTests
    {
        ICatalog _catalog;
        DesignSession _session;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        static bool IsCurtain(ICatalog catalog, string id) => catalog.TryGet(id, out var item) && (item.kind == "curtain" || item.kind == "blind");

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public AssistantRequest Seen;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) { Seen = r; return Task.FromResult(Plan); }
        }

        [Test]
        public void BedroomPreset_PlacesNoCurtain_OfflineOrLive()
        {
            var bedroom = CannedPlans.ById("calm-bedroom");
            CollectionAssert.IsEmpty(bedroom.Items.Where(id => id.StartsWith("curtain")), "the canned plan");
            var set = PresetSets.Items(_catalog, bedroom);
            Assert.IsFalse(set.Any(i => IsCurtain(_catalog, i.id)), "the set sent to the assistant");
            Assert.GreaterOrEqual(set.Count, PresetSets.MinItems, "the set is still a set");

            var offline = new AssistantRunner(new FakeDesignAssistant { FailNext = true }, _session, _catalog, null, null, 200);
            var outcome = Task.Run(() => offline.AskPresetAsync("calm-bedroom", CancellationToken.None)).GetAwaiter().GetResult();
            Assert.Greater(outcome.Placements.Count, 0, string.Join("; ", outcome.Dropped));
            Assert.IsFalse(outcome.Placements.Any(p => IsCurtain(_catalog, p.catalogItemId)));
        }

        [Test]
        public void AModelPlanNamingACurtain_SkipsIt_AndTheRequestNeverOffersOne()
        {
            var plan = new AssistantPlan();
            plan.placements.Add(new PlanPlacement { catalogItemId = "curtain-scandi-01" });
            plan.placements.Add(new PlanPlacement { catalogItemId = "plant-cafe-01" });
            var assistant = new PlanAssistant { Plan = plan };
            var runner = new AssistantRunner(assistant, _session, _catalog, null, null);
            var outcome = runner.AskAsync(AssistantTask.FillOutside, "a calm bedroom with curtains", null, CancellationToken.None).GetAwaiter().GetResult();
            CollectionAssert.AreEqual(new[] { "plant-cafe-01" }, outcome.Placements.Select(p => p.catalogItemId));
            CollectionAssert.AreEqual(new[] { "curtain-scandi-01: placed by hand" }, outcome.Dropped);
            Assert.AreEqual("Placed 1 of 2: curtains go up by hand", outcome.Summary);

            var ids = JObject.Parse(AssistantRequestJson.Build(assistant.Seen, null, null))["catalog"].Select(t => (string)t);
            Assert.IsFalse(ids.Any(id => IsCurtain(_catalog, id)), "no curtain in the request's catalog");
            Assert.IsTrue(_catalog.Items.Any(i => i.kind == "curtain" && i.AllowedIn("outside")), "the catalog still has curtains for Add");
        }
    }
}

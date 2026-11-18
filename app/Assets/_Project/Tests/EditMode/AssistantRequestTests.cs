using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class AssistantRequestTests
    {
        static readonly string[] Finishes = { "floor-oak-planks", "wall-cream" };
        static readonly string[] Moods = { "morning", "afternoon", "evening", "night" };

        static AssistantRequest Request(AssistantTask task, string intent = "make it a cozy living room", string anchor = null)
        {
            var design = new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a"), moodPresetId = "evening" };
            design.placements.Add(new FreePlacement { placementId = "p1", catalogItemId = "sofa-warm-wood-01" });
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            return new AssistantRequest(task, intent, FakeRoomInfo.DormRoom(), design, catalog.Items, anchor);
        }

        static JObject Build(AssistantRequest r) => JObject.Parse(AssistantRequestJson.Build(r, Finishes, Moods));

        [Test]
        public void FillOutside_MatchesTheProxyShape()
        {
            var json = Build(Request(AssistantTask.FillOutside));
            CollectionAssert.AreEquivalent(new[] { "v", "task", "intent", "room", "current", "catalog", "finishes", "moods" },
                json.Properties().Select(p => p.Name));
            Assert.AreEqual("FillOutside", (string)json["task"]);
            Assert.AreEqual(4, ((JArray)json["room"]["floor"]).Count);
            Assert.AreEqual(4, ((JArray)json["room"]["myZone"]).Count);
            Assert.AreEqual(4, ((JArray)json["room"]["openings"]).Count);
            var furniture = (JArray)json["room"]["furniture"];
            Assert.AreEqual(6, furniture.Count);
            foreach (var f in furniture) Assert.AreEqual(((string)f["id"]).StartsWith("a-"), (bool)f["mine"]);
            Assert.AreEqual("evening", (string)json["current"]["mood"]);
            CollectionAssert.AreEqual(new[] { "sofa-warm-wood-01" }, json["current"]["placed"].Select(t => (string)t));
            Assert.AreEqual(2, ((JArray)json["finishes"]).Count);

            string intent = (string)Build(Request(AssistantTask.FillOutside, "cozy\n\tplease " + new string('x', 400)))["intent"];
            Assert.AreEqual(200, intent.Length, "clipped to the proxy limit");
            StringAssert.StartsWith("cozy  please", intent, "control characters flattened");
        }

        [Test]
        public void FillOutside_SendsOnlyOutsideNonCoverIds()
        {
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            var ids = Build(Request(AssistantTask.FillOutside))["catalog"].Select(t => (string)t).ToList();
            Assert.Greater(ids.Count, 10);
            foreach (var id in ids)
            {
                Assert.IsTrue(catalog.TryGet(id, out var item));
                Assert.IsTrue(item.AllowedIn("outside"), id);
                Assert.AreNotEqual(HideRules.CoverPropKind, item.kind, id);
            }
        }

        [Test]
        public void RecommendDesign_SendsTheAnchorAndOnlyItsRole()
        {
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            var json = Build(Request(AssistantTask.RecommendDesign, "calm", "a-desk"));
            Assert.AreEqual("a-desk", (string)json["anchorId"]);
            var ids = json["catalog"].Select(t => (string)t).ToList();
            Assert.Greater(ids.Count, 0);
            foreach (var id in ids)
            {
                catalog.TryGet(id, out var item);
                Assert.AreEqual(FurnitureRole.Table, item.role, id);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.Tests
{
    public class AssistantContractTests
    {
        static AssistantRequest Request(string intent, AssistantTask task = AssistantTask.FillOutside, IReadOnlyList<CatalogItem> items = null) =>
            new AssistantRequest(task, intent, FakeRoomInfo.DormRoom(), new DesignState(), items, task == AssistantTask.RecommendDesign ? "a-desk" : null);

        [TestCase("make it a cozy living room", "cozy-living-room")]
        [TestCase("I want a quiet place to read books", "study-library")]
        [TestCase("Café vibes with coffee!", "cafe")]
        [TestCase("neon night city", "night-lounge")]
        [TestCase("", "cozy-living-room")]
        [TestCase("something completely different", "cozy-living-room")]
        public void NearestPreset_MatchesKeywords(string intent, string expected) =>
            Assert.AreEqual(expected, CannedPlans.Nearest(intent).Id);

        [Test]
        public void CannedPlans_UseIdsThatExistInContentData()
        {
            var catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            string finishes = File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Content/finishes.json"));
            foreach (var preset in CannedPlans.Presets)
            {
                foreach (var id in preset.Items)
                {
                    Assert.IsTrue(catalog.TryGet(id, out var item), id);
                    Assert.IsTrue(item.AllowedIn("outside"), id);
                }
                StringAssert.Contains("\"" + preset.Floor + "\"", finishes);
                StringAssert.Contains("\"" + preset.Wall + "\"", finishes);
                CollectionAssert.Contains(new[] { "morning", "afternoon", "evening", "night" }, preset.Mood);
            }
        }

        [Test]
        public void Fake_ReturnsCannedPlanWithoutPositions()
        {
            var plan = new FakeDesignAssistant().RequestAsync(Request("cozy"), CancellationToken.None).Result;
            Assert.AreEqual("evening", plan.moodPresetId);
            Assert.Greater(plan.placements.Count, 3);
            Assert.IsFalse(plan.placements[0].x.HasValue);
            Assert.IsFalse(plan.fromFallback);
        }

        [Test]
        public void Fake_RecommendsFromAllowedItems()
        {
            var items = new[] { new CatalogItem { id = "desk-scandi-01" } };
            var plan = new FakeDesignAssistant().RequestAsync(Request("calm", AssistantTask.RecommendDesign, items), CancellationToken.None).Result;
            Assert.AreEqual(0, plan.placements.Count);
            Assert.AreEqual("desk-scandi-01", plan.recommendations[0].catalogItemId);
            Assert.AreEqual("a-desk", plan.recommendations[0].anchorId);
        }

        [Test]
        public void Fake_CanFailOnceAndHonorsCancellation()
        {
            var fake = new FakeDesignAssistant { FailNext = true };
            Assert.Throws<AggregateException>(() => fake.RequestAsync(Request("x"), CancellationToken.None).Wait());
            Assert.IsNotNull(fake.RequestAsync(Request("x"), CancellationToken.None).Result);
            var slow = new FakeDesignAssistant(5000);
            using (var cts = new CancellationTokenSource(50))
            {
                Assert.Throws<AggregateException>(() => slow.RequestAsync(Request("x"), cts.Token).Wait());
            }
            Assert.AreEqual(3, fake.Calls + slow.Calls);
        }
    }
}

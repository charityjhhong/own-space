using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class CatalogLoaderTests
    {
        const string Good = "{\"id\":\"desk-a-01\",\"role\":\"Table\",\"kind\":\"desk\",\"zones\":[\"my\"],\"size\":{\"x\":1.2,\"y\":0.74,\"z\":0.6},\"usableHeight\":0.74,\"styles\":[\"scandi\"],\"moods\":[\"night\"]}";

        static CatalogLoadResult LoadItems(params string[] items) =>
            CatalogLoader.Load("{\"schemaVersion\":0," + Lists + "\"items\":[" + string.Join(",", items) + "]}");

        const string Lists = "\"styles\":[\"scandi\",\"cafe\"],\"moods\":[\"night\",\"morning\"],";

        static string With(string field, string value) =>
            Good.Replace(field, value);

        [Test]
        public void RealCatalog_LoadsWithoutErrors()
        {
            var result = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath);
            CollectionAssert.IsEmpty(result.Errors);
            Assert.GreaterOrEqual(result.Catalog.Items.Count, 16);
        }

        [Test]
        public void GoodItem_LoadsAndIsFoundById()
        {
            var result = LoadItems(Good);
            CollectionAssert.IsEmpty(result.Errors);
            Assert.IsTrue(result.Catalog.TryGet("desk-a-01", out var item));
            Assert.AreEqual(FurnitureRole.Table, item.role);
            Assert.IsTrue(item.AllowedIn("my"));
            Assert.IsFalse(result.Catalog.TryGet("nope", out _));
        }

        [TestCase("\"role\":\"Table\"", "\"role\":\"Chair\"", "unreadable")]
        [TestCase("\"x\":1.2", "\"x\":-1.2", "size")]
        [TestCase("\"usableHeight\":0.74", "\"usableHeight\":0", "usableHeight > 0")]
        [TestCase("\"usableHeight\":0.74", "\"usableHeight\":0.9", "between 0 and size.y")]
        [TestCase("[\"scandi\"]", "[\"baroque\"]", "unknown style")]
        [TestCase("[\"night\"]", "[\"dawn\"]", "unknown mood")]
        [TestCase("\"zones\":[\"my\"]", "\"zones\":[]", "zones is empty")]
        public void BadItem_IsSkippedAndNamedInError(string field, string value, string message)
        {
            var result = LoadItems(With(field, value), Good.Replace("desk-a-01", "desk-b-01"));
            Assert.AreEqual(1, result.Errors.Count, string.Join("\n", result.Errors));
            StringAssert.StartsWith("desk-a-01: ", result.Errors[0]);
            StringAssert.Contains(message, result.Errors[0]);
            Assert.AreEqual(1, result.Catalog.Items.Count);
            Assert.AreEqual("desk-b-01", result.Catalog.Items[0].id);
        }

        [Test]
        public void DuplicateAndMalformedIds_AreReported()
        {
            var result = LoadItems(Good, Good, Good.Replace("desk-a-01", "Desk A"), Good.Replace("\"id\":\"desk-a-01\",", ""));
            Assert.AreEqual(1, result.Catalog.Items.Count);
            Assert.AreEqual(3, result.Errors.Count);
            StringAssert.Contains("duplicate id", result.Errors[0]);
            StringAssert.StartsWith("Desk A: id", result.Errors[1]);
            StringAssert.StartsWith("#3: id", result.Errors[2]);
        }

        [Test]
        public void RealAssetWithPlaceholderLicense_IsRejected()
        {
            var item = Good.Replace("\"moods\"", "\"asset\":{\"path\":\"x\",\"isPlaceholder\":false},\"license\":\"PLACEHOLDER\",\"moods\"");
            var result = LoadItems(item);
            StringAssert.Contains("real license", string.Join("\n", result.Errors));
        }

        [Test]
        public void WithoutStyleAndMoodLists_StylesAndMoodsAreNotChecked()
        {
            var result = CatalogLoader.Load("{\"schemaVersion\":0,\"items\":[" + Good.Replace("scandi", "baroque") + "]}");
            CollectionAssert.IsEmpty(result.Errors);
        }

        [Test]
        public void MalformedStyleList_IsReported()
        {
            var result = CatalogLoader.Load("{\"schemaVersion\":0,\"styles\":\"scandi\",\"items\":[" + Good + "]}");
            Assert.AreEqual(1, result.Errors.Count);
            StringAssert.Contains("styles must be a list", result.Errors[0]);
            Assert.AreEqual(1, result.Catalog.Items.Count);
        }

        [Test]
        public void NonObjectElementsAndOddIds_AreReportedByIndex()
        {
            var result = LoadItems("42", "\"desk\"", "null", "[]", Good.Replace("\"desk-a-01\"", "{}"), Good);
            Assert.AreEqual(1, result.Catalog.Items.Count);
            Assert.AreEqual(5, result.Errors.Count, string.Join("\n", result.Errors));
            StringAssert.StartsWith("#0: not an object", result.Errors[0]);
            StringAssert.StartsWith("#3: not an object", result.Errors[3]);
            StringAssert.StartsWith("#4: ", result.Errors[4]);
        }

        [TestCase(null)]
        [TestCase("not json")]
        [TestCase("{\"schemaVersion\":1,\"items\":[]}")]
        [TestCase("{\"items\":[]}")]
        [TestCase("{\"schemaVersion\":\"zero\",\"items\":[]}")]
        [TestCase("{\"schemaVersion\":0.5,\"items\":[]}")]
        [TestCase("[1,2]")]
        [TestCase("{\"schemaVersion\":0}")]
        public void BrokenFile_GivesEmptyCatalogAndOneError(string json)
        {
            var result = CatalogLoader.Load(json);
            Assert.AreEqual(0, result.Catalog.Items.Count);
            Assert.AreEqual(1, result.Errors.Count);
        }

        [Test]
        public void MissingFile_GivesError() =>
            Assert.AreEqual(1, CatalogLoader.LoadFile("/no/such/catalog.json").Errors.Count);

        [Test]
        public void FittingItems_ListsOnlyMyZoneItemsThatFit()
        {
            var desk = FakeRoomInfo.DormRoom().Furniture[1];
            var results = new List<(CatalogItem item, FitResult fit)>();
            CatalogQueries.FittingItems(FakeCatalog.Dorm(), desk, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("desk-fake-01", results[0].item.id);
            Assert.IsTrue(results[0].fit.Fits);
        }

        [Test]
        public void FakeCatalog_FitsEveryDormAnchor()
        {
            var results = new List<(CatalogItem item, FitResult fit)>();
            foreach (var anchor in FakeRoomInfo.DormRoom().Furniture)
            {
                CatalogQueries.FittingItems(FakeCatalog.Dorm(), anchor, results);
                Assert.AreEqual(1, results.Count, anchor.Id);
            }
        }
    }
}

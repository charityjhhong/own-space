using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.Tests
{
    /// <summary>The real content catalog against the demo dorm.</summary>
    public class CatalogFitTests
    {
        // OWNSPACE_CATALOG points at another copy, e.g. to test a content branch before it merges.
        internal static readonly string CatalogPath = System.Environment.GetEnvironmentVariable("OWNSPACE_CATALOG")
            ?? Path.Combine(Application.dataPath, "_Project/Content/catalog.json");
        static readonly string[] RedesignKinds = { "desk", "bed", "closet" };

        static List<(string kind, CatalogItem item)> LoadRedesignItems()
        {
            if (!File.Exists(CatalogPath)) Assert.Ignore("Content/catalog.json is missing.");

            var result = new List<(string, CatalogItem)>();
            foreach (var item in CatalogLoader.LoadFile(CatalogPath).Catalog.Items)
            {
                if (System.Array.IndexOf(RedesignKinds, item.kind) >= 0) result.Add((item.kind, item));
            }
            return result;
        }

        [Test]
        public void EveryDeskBedAndCloset_FitsADormAnchor()
        {
            var room = FakeRoomInfo.DormRoom();
            var items = LoadRedesignItems();
            var seenKinds = new HashSet<string>();
            foreach (var (kind, item) in items)
            {
                seenKinds.Add(kind);
                bool fitsAny = false;
                foreach (var anchor in room.Furniture) fitsAny |= FitRule.Fits(anchor, item);
                Assert.IsTrue(fitsAny, $"{item.id} ({kind}, {item.size}) fits no dorm anchor");
            }
            CollectionAssert.IsSubsetOf(RedesignKinds, seenKinds, "catalog should have every redesign kind");
        }

        [Test]
        public void DormFurniture_HasRealisticSizes()
        {
            var f = FakeRoomInfo.DormRoom().Furniture;
            Vec3 closet = f[0].Box.size, desk = f[1].Box.size, bed = f[2].Box.size;

            Assert.That(bed.x, Is.InRange(1.9f, 2.1f), "bed long side");
            Assert.That(bed.z, Is.InRange(0.8f, 1.2f), "single bed width");
            Assert.That(f[2].UsableHeight, Is.InRange(0.4f, 0.6f), "mattress top");
            Assert.That(desk.x, Is.InRange(1.0f, 1.6f), "desk width");
            Assert.That(desk.z, Is.InRange(0.5f, 0.8f), "desk depth");
            Assert.That(f[1].UsableHeight, Is.InRange(0.70f, 0.78f), "desk top");
            Assert.That(closet.y, Is.InRange(1.7f, 2.2f), "closet height");
            Assert.That(closet.z, Is.InRange(0.5f, 0.7f), "closet depth");
        }
    }
}

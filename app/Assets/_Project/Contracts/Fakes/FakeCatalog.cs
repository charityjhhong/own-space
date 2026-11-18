using System.Collections.Generic;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>A small catalog that fits <see cref="FakeRoomInfo.DormRoom"/>: one desk, bed, closet each for my zone, and a sofa outside.</summary>
    public static class FakeCatalog
    {
        public static Catalog Dorm() => new Catalog(new[]
        {
            Item("desk-fake-01", FurnitureRole.Table, "desk", "my", 1.2f, 0.74f, 0.6f, 0.74f),
            Item("bed-fake-01", FurnitureRole.Bed, "bed", "my", 1.0f, 0.6f, 2.0f, 0.5f),
            Item("closet-fake-01", FurnitureRole.Storage, "closet", "my", 0.9f, 1.9f, 0.6f, 0f),
            Item("sofa-fake-01", FurnitureRole.Couch, "sofa", "outside", 1.8f, 0.8f, 0.9f, 0f),
        });

        static CatalogItem Item(string id, FurnitureRole role, string kind, string zone,
            float w, float h, float d, float usable) => new CatalogItem
        {
            id = id,
            name = id,
            role = role,
            kind = kind,
            zones = new List<string> { zone },
            size = new Vec3(w, h, d),
            usableHeight = usable,
            asset = new CatalogAsset { path = "", isPlaceholder = true },
            license = "PLACEHOLDER",
        };
    }
}

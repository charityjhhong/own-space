using System;
using System.Collections.Generic;

namespace OwnSpace.Contracts
{
    /// <summary>
    /// The minimum the fit rule needs from one catalog entry. The <c>content</c> lane owns the
    /// catalog data and may add fields (style tags, prefab key, license); these four stay.
    /// JSON: {"id":"desk-oak-01","role":"Table","size":{"x":1.2,"y":0.75,"z":0.6},"usableHeight":0.75}
    /// An item fits a real anchor under the numbers in <see cref="FitLimits"/>:
    /// same role; footprint (never stretched) within the real box enlarged by
    /// <see cref="FitLimits.MarginPerSide"/> on each side, as-is or turned 90 degrees;
    /// uniform Y scale within 1 ± <see cref="FitLimits.MaxYScale"/> so the usable surface
    /// (Table, Bed) or the top (other roles) lands exactly on the real one.
    /// Lengths are compared with <see cref="FitLimits.Tolerance"/>.
    /// </summary>
    [Serializable]
    public sealed class CatalogItem
    {
        public string id = "";
        public FurnitureRole role;

        /// <summary>Meters, in the item's own frame: x = width along its front, y = height, z = depth.</summary>
        public Vec3 size;

        /// <summary>Height above the floor of the surface people use (desk top, mattress). 0 = none (e.g. a closet).</summary>
        public float usableHeight;

        // Fields from docs/design/catalog-schema.md (content owns the data). Not used by the fit rule.
        public string name = "";
        /// <summary>Detailed role: desk, bed, closet, shelf, sofa, lamp, plant, rug, clutter-cover, ...</summary>
        public string kind = "";
        /// <summary>"my" (redesign in my zone) and/or "outside" (free placement).</summary>
        public List<string> zones = new List<string>();
        public List<string> styles = new List<string>();
        public List<string> moods = new List<string>();
        public List<string> tags = new List<string>();
        public string primaryColor = "";
        public string description = "";
        public CatalogAsset asset = new CatalogAsset();
        public string license = "";

        public bool AllowedIn(string zone) => zones != null && zones.Contains(zone);
    }

    [Serializable]
    public sealed class CatalogAsset
    {
        /// <summary>Prefab path under Assets/_Project/Art/.</summary>
        public string path = "";
        /// <summary>True while the prefab does not exist yet.</summary>
        public bool isPlaceholder;
    }

    /// <summary>Read-only furniture catalog. <c>OwnSpace.Core.CatalogLoader</c> builds one from catalog.json.</summary>
    public interface ICatalog
    {
        IReadOnlyList<CatalogItem> Items { get; }
        bool TryGet(string id, out CatalogItem item);
    }

    /// <summary>A catalog over a fixed list. Later items with a duplicate id are ignored by <see cref="TryGet"/>.</summary>
    public sealed class Catalog : ICatalog
    {
        readonly List<CatalogItem> _items;
        readonly Dictionary<string, CatalogItem> _byId = new Dictionary<string, CatalogItem>();

        public Catalog(IEnumerable<CatalogItem> items)
        {
            _items = new List<CatalogItem>(items ?? Array.Empty<CatalogItem>());
            foreach (var item in _items)
            {
                if (item?.id != null && !_byId.ContainsKey(item.id)) _byId.Add(item.id, item);
            }
        }

        public static Catalog Empty { get; } = new Catalog(null);
        public IReadOnlyList<CatalogItem> Items => _items;
        public bool TryGet(string id, out CatalogItem item) => _byId.TryGetValue(id ?? "", out item);
    }

    /// <summary>The fit rule's numbers, in one place. Final for P0; content documents these values.</summary>
    public static class FitLimits
    {
        /// <summary>The real box is enlarged by this much on each side before comparing footprints (meters).</summary>
        public const float MarginPerSide = 0.05f;

        /// <summary>Comparison tolerance for every length check, footprint and height (meters).</summary>
        public const float Tolerance = 0.01f;

        /// <summary>Largest allowed change of the uniform Y scale from 1 (0.15 = 85% to 115%).</summary>
        public const float MaxYScale = 0.15f;

        /// <summary>
        /// Largest stretch per footprint axis (item's own x and z) used to cover a real box that is
        /// bigger than the item, so the real piece never shows beside its redesign. An item that would
        /// need more does not fit. Items are never shrunk.
        /// </summary>
        public const float MaxFootprintScale = 1.3f;

        /// <summary>Roles whose target height is the usable surface; all other roles use the box top.</summary>
        public static bool HasUsableSurface(FurnitureRole role) =>
            role == FurnitureRole.Table || role == FurnitureRole.Bed;
    }
}

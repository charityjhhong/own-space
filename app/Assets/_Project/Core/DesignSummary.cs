using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// One short English line describing a design, for the panel header and the demo captions,
    /// e.g. "Warm-wood desk and bed in your zone; café lounge outside; evening mood".
    /// Pure; allocates, so call it when the design changes, not every frame. At most <see cref="MaxLength"/> characters.
    /// </summary>
    public static class DesignSummary
    {
        public const int MaxLength = 120;
        public const string Untouched = "Your room, just as it is";

        // Kinds in the order people name them.
        static readonly string[] KindOrder = { "desk", "bed", "closet" };

        public static string Describe(DesignState state, ICatalog catalog)
        {
            if (state == null) return Untouched;
            var parts = new List<string>();

            string mine = MyZone(state, catalog);
            if (mine != null) parts.Add(mine);
            string outside = Outside(state, catalog);
            if (outside != null) parts.Add(outside);
            if (!string.IsNullOrEmpty(state.moodPresetId)) parts.Add(state.moodPresetId + " mood");
            if (parts.Count == 0) return Untouched;

            string text = Capitalize(string.Join("; ", parts));
            return text.Length <= MaxLength ? text : text.Substring(0, MaxLength - 1) + "…";
        }

        static string MyZone(DesignState state, ICatalog catalog)
        {
            var items = new List<CatalogItem>();
            int unknown = 0;
            foreach (var c in state.anchorChoices)
            {
                if (c.mode != AnchorMode.Redesign) continue;
                if (catalog != null && catalog.TryGet(c.catalogItemId, out var item)) items.Add(item);
                else unknown++;
            }
            int tidied = (state.hideBoxes?.Count ?? 0) + (state.coverProps?.Count ?? 0);
            string tidy = tidied == 0 ? "" : tidied == 1 ? ", 1 spot tidied" : $", {tidied} spots tidied";

            if (items.Count == 0 && unknown == 0)
            {
                if (state.zoneOutline.Count < 3) return null;
                return tidied == 0 ? null : "your zone kept real" + tidy;
            }
            var kinds = new List<string>();
            foreach (var kind in KindOrder) if (items.Exists(i => i.kind == kind)) kinds.Add(kind);
            foreach (var i in items) if (!kinds.Contains(i.kind) && i.kind.Length > 0) kinds.Add(i.kind);
            string what = kinds.Count > 0 ? JoinAnd(kinds) : Count(items.Count + unknown, "piece");
            if (kinds.Count > 0 && unknown > 0) what += " and " + Count(unknown, "other piece");
            return $"{Style(items)} {what} in your zone{tidy}".TrimStart();
        }

        static string Outside(DesignState state, ICatalog catalog)
        {
            var items = new List<CatalogItem>();
            foreach (var p in state.placements)
                if (catalog != null && catalog.TryGet(p.catalogItemId, out var item)) items.Add(item);
            if (items.Count == 0)
            {
                if (state.placements.Count > 0) return Count(state.placements.Count, "piece") + " outside";
                bool finished = !string.IsNullOrEmpty(state.outsideFinish.floorFinishId) || !string.IsNullOrEmpty(state.outsideFinish.wallFinishId);
                return finished ? "new finishes outside" : null;
            }
            return $"{Style(items)} {Theme(items)} outside".TrimStart();
        }

        // What the outside space reads as, from its pieces.
        static string Theme(List<CatalogItem> items)
        {
            bool Has(string kind) => items.Exists(i => i.kind == kind);
            if (Has("sofa")) return "lounge";
            if (Has("armchair") && Has("shelf")) return "reading corner";
            if (Has("armchair") || Has("coffee-table")) return "sitting area";
            return "space";
        }

        // The style most pieces share ("warm-wood", "café"); "mixed" when none leads.
        static string Style(List<CatalogItem> items)
        {
            var counts = new Dictionary<string, int>();
            foreach (var item in items)
                if (item.styles != null && item.styles.Count > 0)
                    counts[item.styles[0]] = counts.TryGetValue(item.styles[0], out int n) ? n + 1 : 1;
            string best = null;
            int bestCount = 0;
            bool tie = false;
            foreach (var pair in counts)
            {
                if (pair.Value > bestCount) { best = pair.Key; bestCount = pair.Value; tie = false; }
                else if (pair.Value == bestCount) tie = true;
            }
            if (best == null) return "";
            return tie ? "mixed-style" : best == "cafe" ? "café" : best;
        }

        static string JoinAnd(List<string> words) =>
            words.Count == 1 ? words[0] : string.Join(", ", words.GetRange(0, words.Count - 1)) + " and " + words[words.Count - 1];

        static string Count(int n, string noun) => n == 1 ? "1 " + noun : $"{n} {noun}s";

        static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}

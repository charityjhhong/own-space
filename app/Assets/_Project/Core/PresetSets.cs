using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Curated sets for the AI presets (docs/design/presets.md). A catalog item belongs to a preset's set
    /// when it carries the tag <c>set:&lt;preset id&gt;</c>, e.g. "set:study-cafe". A preset request asks the
    /// assistant only for its set's pieces and always applies the preset's outside finishes and mood.
    /// With fewer than <see cref="MinItems"/> tagged outside pieces the catalog is used unrestricted and the
    /// offline plan keeps <see cref="CannedPlans.Preset.Items"/>.
    /// </summary>
    public static class PresetSets
    {
        public const string TagPrefix = "set:";
        public const int MinItems = 3;
        public const int MaxItems = 7;

        public static string TagFor(CannedPlans.Preset preset) => TagPrefix + preset.Id;

        /// <summary>The preset's tagged outside pieces (never curtains: those are placed by hand) in catalog order, at most <see cref="MaxItems"/>; empty if fewer than <see cref="MinItems"/>.</summary>
        public static List<CatalogItem> Items(ICatalog catalog, CannedPlans.Preset preset)
        {
            var list = new List<CatalogItem>();
            if (catalog == null || preset == null) return list;
            string tag = TagFor(preset);
            foreach (var item in catalog.Items)
                if (item != null && item.tags != null && item.tags.Contains(tag) && item.AllowedIn("outside") &&
                    item.kind != HideRules.CoverPropKind && !PlanChecker.HandOnly(item) && list.Count < MaxItems)
                    list.Add(item);
            if (list.Count < MinItems) list.Clear();
            return list;
        }

        /// <summary>The offline plan: the preset's look with its set's pieces, or its canned pieces when there is no set.</summary>
        public static AssistantPlan PlanFor(CannedPlans.Preset preset, List<CatalogItem> set)
        {
            var plan = CannedPlans.PlanFor(preset);
            if (set == null || set.Count == 0) return plan;
            plan.placements.Clear();
            foreach (var item in set) plan.placements.Add(new PlanPlacement { catalogItemId = item.id });
            return plan;
        }

        /// <summary>A preset always brings its own outside floor, wall and mood (ids the app knows), whatever the model chose.</summary>
        internal static void ApplyLook(AssistantOutcome outcome, CannedPlans.Preset preset, ICollection<string> finishIds, ICollection<string> moodIds)
        {
            if (Known(preset.Floor, finishIds)) outcome.FloorFinishId = preset.Floor;
            if (Known(preset.Wall, finishIds)) outcome.WallFinishId = preset.Wall;
            if (Known(preset.Mood, moodIds)) outcome.MoodPresetId = preset.Mood;
        }

        static bool Known(string id, ICollection<string> known) => !string.IsNullOrEmpty(id) && (known == null || known.Contains(id));
    }
}

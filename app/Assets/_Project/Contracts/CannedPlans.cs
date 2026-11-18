using System;
using System.Collections.Generic;

namespace OwnSpace.Contracts
{
    /// <summary>
    /// Built-in outside-zone plans, one per style, used offline and by <c>FakeDesignAssistant</c>.
    /// Pieces have no positions: core lays them out in the free space. Ids are from content's
    /// catalog.json and finishes.json; an id that disappears is simply dropped by validation.
    /// </summary>
    public static class CannedPlans
    {
        public sealed class Preset
        {
            public Preset(string id, string label, string intent, string[] keywords, string[] items,
                string floor, string wall, string mood, string rationale)
            {
                Id = id;
                Label = label;
                Intent = intent;
                Keywords = keywords;
                Items = items;
                Floor = floor;
                Wall = wall;
                Mood = mood;
                Rationale = rationale;
            }

            /// <summary>Stable id for UI chips and saves, e.g. "study-cafe".</summary>
            public string Id { get; }

            /// <summary>Chip text, e.g. "Study café".</summary>
            public string Label { get; }

            /// <summary>What the chip asks the live assistant for.</summary>
            public string Intent { get; }

            /// <summary>Lower-case name, kept for older callers and logs.</summary>
            public string Name => Label.ToLowerInvariant();
            public IReadOnlyList<string> Keywords { get; }
            public IReadOnlyList<string> Items { get; }
            public string Floor { get; }
            public string Wall { get; }
            public string Mood { get; }
            public string Rationale { get; }
        }

        /// <summary>The first preset is the default when no keyword matches. Order is the chip order.</summary>
        public static readonly IReadOnlyList<Preset> Presets = new[]
        {
            new Preset("cozy-living-room", "Cozy living room", "make it a cozy living room with a sofa, a rug and soft light",
                new[] { "cozy", "cosy", "living", "warm", "relax", "sofa", "family", "comfortable", "home" },
                new[] { "rug-warm-wood-01", "sofa-warm-wood-01", "coffee-table-warm-wood-01", "lamp-warm-wood-01", "plant-warm-wood-01", "shelf-warm-wood-01" },
                "floor-oak-planks", "wall-cream", "evening",
                "A warm sofa corner on an oak floor with a soft lamp, for winding down."),
            new Preset("study-library", "Study library", "a quiet study library with a shelf, an armchair and bright light",
                new[] { "study", "read", "reading", "library", "focus", "book", "books", "quiet", "work", "calm", "bright" },
                new[] { "rug-scandi-01", "armchair-scandi-01", "shelf-scandi-01", "lamp-scandi-01", "plant-scandi-01" },
                "floor-birch-planks", "wall-soft-stripes", "morning",
                "A bright reading nook with a low shelf and an armchair, for focused study."),
            new Preset("cafe", "Café", "a café lounge with leather seats, plants and coffee with friends",
                new[] { "cafe", "café", "coffee", "brunch", "bistro", "lounge", "chat", "friends" },
                new[] { "rug-cafe-01", "sofa-cafe-01", "armchair-cafe-01", "coffee-table-warm-wood-01", "shelf-cafe-01", "plant-cafe-01" },
                "floor-dark-planks", "wall-olive", "afternoon",
                "A café corner with leather seats and plants, for relaxed afternoons."),
            new Preset("night-lounge", "Night lounge", "a dark night lounge with neon city lights for movies and music",
                new[] { "night", "neon", "city", "dark", "party", "music", "movie", "cinema", "game", "gaming", "late" },
                new[] { "rug-warm-wood-01", "sofa-night-city-01", "coffee-table-night-city-01", "lamp-night-city-01", "shelf-night-city-01" },
                "floor-concrete-tiles", "wall-navy-panels", "night",
                "A dim city lounge with a low sofa and amber light, for late evenings."),
            new Preset("study-cafe", "Study café", "a study café: a table to work at, shelves, plants and a warm pendant light",
                new[] { "study", "cafe", "café", "laptop", "table", "homework", "cowork" },
                new[] { "rug-cafe-01", "coffee-table-warm-wood-01", "armchair-cafe-01", "shelf-cafe-01", "pendant-cafe-01", "plant-cafe-01" },
                "floor-dark-planks", "wall-cream", "afternoon",
                "A café-style work corner with a table, books and warm light, for long study sessions."),
            new Preset("calm-bedroom", "Calm bedroom", "a calm bedroom feel: soft rug, plants and low light",
                new[] { "bedroom", "calm", "sleep", "rest", "soft", "peaceful", "unwind" },
                new[] { "rug-scandi-01", "plant-scandi-01", "lamp-warm-wood-01", "shelf-scandi-01" },
                "floor-oak-planks", "wall-cream", "night",
                "Soft textures, plants and a low lamp, so the room winds down with you."),
            new Preset("bright-studio", "Bright studio", "a bright, airy studio with light wood, a sofa and fresh plants",
                new[] { "studio", "bright", "airy", "light", "minimal", "fresh", "clean", "open" },
                new[] { "rug-scandi-01", "sofa-scandi-01", "armchair-scandi-01", "lamp-scandi-01", "plant-scandi-01", "wall-art-scandi-01" },
                "floor-birch-planks", "wall-cream", "morning",
                "Light wood and plants in an open layout, for a fresh, bright start to the day."),
            new Preset("reading-nook", "Reading nook", "a reading nook with an armchair, a floor lamp and a full bookshelf",
                new[] { "nook", "reading", "armchair", "novel", "snug" },
                new[] { "rug-warm-wood-01", "armchair-warm-wood-01", "lamp-warm-wood-01", "shelf-warm-wood-01", "plant-warm-wood-01" },
                "floor-oak-planks", "wall-olive", "evening",
                "An armchair under a warm lamp next to a full shelf, for getting lost in a book."),
        };

        /// <summary>The preset with this id, or null.</summary>
        public static Preset ById(string id)
        {
            foreach (var preset in Presets) if (preset.Id == id) return preset;
            return null;
        }

        /// <summary>"Surprise me": a random preset.</summary>
        public static Preset Surprise(Random random) => Presets[(random ?? new Random()).Next(Presets.Count)];

        /// <summary>The preset sharing the most words with <paramref name="intent"/> (case-insensitive); the first on a tie or no match.</summary>
        public static Preset Nearest(string intent)
        {
            var words = new HashSet<string>(
                (intent ?? "").ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries));
            Preset best = Presets[0];
            int bestHits = 0;
            foreach (var preset in Presets)
            {
                int hits = 0;
                foreach (var keyword in preset.Keywords) if (words.Contains(keyword)) hits++;
                if (hits > bestHits) { best = preset; bestHits = hits; }
            }
            return best;
        }

        /// <summary>A fresh plan for the nearest preset (positions left to core).</summary>
        public static AssistantPlan PlanFor(string intent) => PlanFor(Nearest(intent));

        /// <summary>A fresh plan for <paramref name="preset"/>.</summary>
        public static AssistantPlan PlanFor(Preset preset)
        {
            var plan = new AssistantPlan
            {
                floorFinishId = preset.Floor,
                wallFinishId = preset.Wall,
                moodPresetId = preset.Mood,
                rationale = preset.Rationale,
            };
            foreach (var id in preset.Items) plan.placements.Add(new PlanPlacement { catalogItemId = id });
            return plan;
        }

        static readonly char[] Separators = { ' ', ',', '.', '!', '?', ';', ':', '-', '\'', '"', '\t', '\n' };
    }
}

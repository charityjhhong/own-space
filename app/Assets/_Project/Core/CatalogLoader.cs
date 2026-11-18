using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>Result of loading catalog.json: the valid items, plus one error line per problem.</summary>
    public sealed class CatalogLoadResult
    {
        public CatalogLoadResult(ICatalog catalog, IReadOnlyList<string> errors)
        {
            Catalog = catalog;
            Errors = errors;
        }

        /// <summary>Only the items that passed validation. Never null.</summary>
        public ICatalog Catalog { get; }

        /// <summary>"&lt;item id or #index&gt;: &lt;problem&gt;". Empty when the file is clean.</summary>
        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Reads Content/catalog.json (schema: docs/design/catalog-schema.md) with Newtonsoft, since
    /// JsonUtility cannot read enum names. A bad item is left out and reported; it never stops the
    /// rest of the catalog from loading.
    /// </summary>
    public static class CatalogLoader
    {
        public const int SupportedSchemaVersion = 0;

        /// <summary>The two zones are a core concept, so they are fixed here.</summary>
        public static readonly string[] KnownZones = { "my", "outside" };

        static readonly Regex IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter { AllowIntegerValues = false } },
        });

        public static CatalogLoadResult LoadFile(string path)
        {
            try
            {
                return Load(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return Fail($"catalog file: {e.Message}");
            }
        }

        public static CatalogLoadResult Load(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json ?? "");
            }
            catch (JsonException e)
            {
                return Fail($"catalog: not valid JSON ({e.Message})");
            }

            var versionToken = root["schemaVersion"];
            int version = versionToken != null && versionToken.Type == JTokenType.Integer ? versionToken.Value<int>() : -1;
            if (version < 0 || version > SupportedSchemaVersion)
                return Fail($"catalog: unsupported schemaVersion {versionToken?.ToString(Formatting.None) ?? "(missing)"}");
            if (!(root["items"] is JArray array)) return Fail("catalog: missing items array");

            var errors = new List<string>();
            // Style and mood ids are content's: the catalog lists them at the top level. Missing list = not checked.
            var styles = ReadIds(root, "styles", errors);
            var moods = ReadIds(root, "moods", errors);
            var items = new List<CatalogItem>();
            var seen = new HashSet<string>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject obj))
                {
                    errors.Add($"#{i}: not an object");
                    continue;
                }
                var idToken = obj["id"];
                string raw = idToken != null && idToken.Type == JTokenType.String ? (string)idToken : null;
                string label = string.IsNullOrEmpty(raw) ? "#" + i : raw;
                CatalogItem item;
                try
                {
                    item = array[i].ToObject<CatalogItem>(Serializer);
                }
                catch (JsonException e)
                {
                    errors.Add($"{label}: unreadable ({e.Message})");
                    continue;
                }

                int before = errors.Count;
                Validate(item, label, seen, styles, moods, errors);
                if (errors.Count == before) items.Add(item);
            }
            return new CatalogLoadResult(new Catalog(items), errors);
        }

        static void Validate(CatalogItem item, string label, HashSet<string> seen,
            string[] styles, string[] moods, List<string> errors)
        {
            if (item == null) { errors.Add($"{label}: empty item"); return; }
            if (string.IsNullOrEmpty(item.id) || !IdPattern.IsMatch(item.id)) errors.Add($"{label}: id must be lowercase words joined by hyphens");
            else if (!seen.Add(item.id)) errors.Add($"{label}: duplicate id");

            Vec3 s = item.size;
            if (!Positive(s.x) || !Positive(s.y) || !Positive(s.z)) errors.Add($"{label}: size must be positive");
            if (float.IsNaN(item.usableHeight) || item.usableHeight < 0f || item.usableHeight > s.y + FitLimits.Tolerance)
                errors.Add($"{label}: usableHeight must be between 0 and size.y");
            if ((item.kind == "desk" || item.kind == "bed") && !(item.usableHeight > 0f))
                errors.Add($"{label}: a {item.kind} needs usableHeight > 0");

            if (item.zones == null || item.zones.Count == 0) errors.Add($"{label}: zones is empty");
            CheckAll(item.zones, KnownZones, "zone", label, errors);
            if (styles != null) CheckAll(item.styles, styles, "style", label, errors);
            if (moods != null) CheckAll(item.moods, moods, "mood", label, errors);

            if (item.asset != null && !item.asset.isPlaceholder && item.license == "PLACEHOLDER")
                errors.Add($"{label}: a real asset needs a real license");
        }

        static void CheckAll(List<string> values, string[] allowed, string what, string label, List<string> errors)
        {
            if (values == null) return;
            foreach (var v in values)
            {
                if (Array.IndexOf(allowed, v) < 0) errors.Add($"{label}: unknown {what} \"{v}\"");
            }
        }

        static string[] ReadIds(JObject root, string key, List<string> errors)
        {
            var token = root[key];
            if (token == null) return null;
            var ids = new List<string>();
            if (token is JArray list)
            {
                foreach (var t in list)
                {
                    if (t.Type == JTokenType.String) ids.Add((string)t);
                }
                if (ids.Count == list.Count) return ids.ToArray();
            }
            errors.Add($"catalog: {key} must be a list of strings");
            return null;
        }

        static bool Positive(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;

        static CatalogLoadResult Fail(string error) =>
            new CatalogLoadResult(OwnSpace.Contracts.Catalog.Empty, new[] { error });
    }

    public static class CatalogQueries
    {
        /// <summary>How many of the closest designs a real piece gets when nothing of its role fits.</summary>
        public const int StretchedOptions = 3;

        /// <summary>
        /// Fills <paramref name="results"/> with my-zone items that fit the anchor, in catalog order. When none
        /// fits, a real piece still gets options: the <see cref="StretchedOptions"/> closest designs of its role,
        /// scaled to the box and marked <see cref="FitResult.Stretched"/>, least distorted first.
        /// Reuses the caller's list (cleared first); call it on selection, not every frame.
        /// </summary>
        public static void FittingItems(ICatalog catalog, FurnitureAnchor anchor, List<(CatalogItem item, FitResult fit)> results)
        {
            results.Clear();
            if (catalog == null || anchor == null) return;
            var items = catalog.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].AllowedIn("my")) continue;
                var fit = FitRule.Check(anchor, items[i]);
                if (fit.Fits) results.Add((items[i], fit));
            }
            if (results.Count > 0) return;
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].AllowedIn("my")) continue;
                var fit = FitRule.Stretch(anchor, items[i]);
                if (fit.Fits) results.Add((items[i], fit));
            }
            results.Sort(ByDistortion); // stable enough: ties keep no guaranteed order, so break them by id
            if (results.Count > StretchedOptions) results.RemoveRange(StretchedOptions, results.Count - StretchedOptions);
        }

        static readonly Comparison<(CatalogItem item, FitResult fit)> ByDistortion = (a, b) =>
        {
            int c = FitRule.Distortion(a.fit).CompareTo(FitRule.Distortion(b.fit));
            return c != 0 ? c : string.CompareOrdinal(a.item.id, b.item.id);
        };

        /// <summary>
        /// How <paramref name="item"/> goes on the anchor: its real fit, or a stretched one when it is among the
        /// closest designs offered because nothing fits (see <see cref="FittingItems"/>). Not Fits otherwise.
        /// </summary>
        public static FitResult FitFor(ICatalog catalog, FurnitureAnchor anchor, CatalogItem item)
        {
            var fit = FitRule.Check(anchor, item);
            if (fit.Fits || catalog == null || item == null) return fit;
            var options = new List<(CatalogItem item, FitResult fit)>();
            FittingItems(catalog, anchor, options);
            foreach (var o in options) if (o.item.id == item.id && o.fit.Stretched) return o.fit;
            return fit;
        }

        /// <summary>One log line: the real box and why each design of its role does not fit, e.g. for "0 design(s) fit".</summary>
        public static string Explain(ICatalog catalog, FurnitureAnchor anchor)
        {
            if (anchor == null) return "no piece";
            var b = anchor.Box.size;
            var line = new System.Text.StringBuilder();
            line.Append($"{anchor.Id} ({anchor.Role}) box {b.x:0.00} x {b.z:0.00} m, {b.y:0.00} m high");
            if (anchor.UsableHeight.HasValue) line.Append($", surface {anchor.UsableHeight.Value:0.00} m");
            if (catalog == null) return line.ToString();
            foreach (var item in catalog.Items)
            {
                if (item == null || !item.AllowedIn("my") || item.role != anchor.Role) continue;
                var fit = FitRule.Check(anchor, item);
                line.Append($"; {item.id} {item.size.x:0.00} x {item.size.z:0.00}: {(fit.Fits ? "fits" : fit.Status.ToString())}");
            }
            return line.ToString();
        }
    }
}

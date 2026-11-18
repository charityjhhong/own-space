using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Builds the one JSON shape the proxy accepts (proxy/src/shape.ts): a compact room summary,
    /// the current design and catalog ids only. Everything is clipped to the proxy's limits here so
    /// a valid room never gets a 400. No names, descriptions or anything personal leave the device.
    /// </summary>
    public static class AssistantRequestJson
    {
        const int MaxPoints = 32, MaxOpenings = 16, MaxFurniture = 32, MaxPlaced = 24, MaxCatalog = 150, MaxFinishes = 32, MaxMoods = 8, MaxIntent = 200;
        static readonly Regex CatalogId = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        static readonly Regex AnchorId = new Regex("^[A-Za-z0-9_-]{1,64}$");

        public static string Build(AssistantRequest r, IEnumerable<string> finishIds, IEnumerable<string> moodIds)
        {
            var zone = r.Design.zoneOutline; var furniture = new JArray();
            foreach (var a in r.Room.Furniture)
            {
                if (furniture.Count == MaxFurniture || !AnchorId.IsMatch(a.Id ?? "")) continue;
                furniture.Add(new JObject
                {
                    ["id"] = a.Id, ["role"] = a.Role.ToString(),
                    ["x"] = M(a.Box.center.x), ["z"] = M(a.Box.center.z),
                    ["w"] = M(a.Box.size.x), ["d"] = M(a.Box.size.z), ["yaw"] = Deg(a.Box.yawDegrees),
                    ["mine"] = Zones.Contains(zone, a),
                });
            }
            var openings = new JArray();
            AddOpenings(openings, r.Room.Doors, "door");
            AddOpenings(openings, r.Room.Windows, "window");
            var placed = new JArray();
            foreach (var p in r.Design.placements) if (placed.Count < MaxPlaced && IsId(p.catalogItemId)) placed.Add(p.catalogItemId);
            var room = new JObject
            {
                ["floor"] = Points(r.Room.FloorOutline), ["myZone"] = Points(zone),
                ["openings"] = openings, ["furniture"] = furniture,
            };
            IEnumerable<string> catalog = CatalogFor(r);
            if (r.Task == AssistantTask.FillOutside)
            {
                // The free floor's box and area, and only pieces that fit on it, so the model picks what can be placed.
                var free = new FreeFloor(r.Room, zone);
                room["free"] = new JObject { ["x0"] = M(free.MinX), ["z0"] = M(free.MinZ), ["x1"] = M(free.MaxX), ["z1"] = M(free.MaxZ), ["area"] = M(free.Area) };
                var fits = PlanChecker.Placeable(r.Room, r.Design, r.AllowedItems, free);
                if (fits.Count > 0) catalog = fits.ConvertAll(i => i.id);
            }
            var root = new JObject
            {
                ["v"] = 1,
                ["task"] = r.Task.ToString(),
                ["intent"] = Intent(r.Intent),
                ["room"] = room,
                ["current"] = new JObject
                {
                    ["mood"] = IdOrEmpty(r.Design.moodPresetId),
                    ["floor"] = IdOrEmpty(r.Design.outsideFinish.floorFinishId),
                    ["wall"] = IdOrEmpty(r.Design.outsideFinish.wallFinishId),
                    ["placed"] = placed,
                },
                ["catalog"] = Ids(catalog, MaxCatalog),
                ["finishes"] = Ids(finishIds, MaxFinishes),
                ["moods"] = Ids(moodIds, MaxMoods),
            };
            if (r.Task == AssistantTask.RecommendDesign) root["anchorId"] = r.AnchorId ?? "";
            return root.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>The catalog ids the assistant may choose for this task (FillOutside: before the free-floor filter).</summary>
        public static IEnumerable<string> CatalogFor(AssistantRequest r)
        {
            FurnitureRole? role = null;
            if (r.Task == AssistantTask.RecommendDesign)
                foreach (var a in r.Room.Furniture) if (a.Id == r.AnchorId) role = a.Role;
            foreach (var item in r.AllowedItems)
            {
                if (item == null) continue;
                bool ok = r.Task == AssistantTask.FillOutside
                    ? item.AllowedIn("outside") && item.kind != HideRules.CoverPropKind && !PlanChecker.HandOnly(item)
                    : item.AllowedIn("my") && role.HasValue && item.role == role.Value;
                if (ok) yield return item.id;
            }
        }

        static void AddOpenings(JArray into, IReadOnlyList<RoomElement> list, string kind)
        {
            foreach (var o in list)
            {
                if (into.Count == MaxOpenings) return;
                into.Add(new JObject
                {
                    ["kind"] = kind, ["x"] = M(o.Box.center.x), ["z"] = M(o.Box.center.z),
                    ["w"] = M(o.Box.size.x), ["yaw"] = Deg(o.Box.yawDegrees),
                });
            }
        }

        // Every n-th point when an outline has more than the proxy accepts.
        static JArray Points(IReadOnlyList<Vec2> outline)
        {
            var result = new JArray();
            if (outline == null) return result;
            int step = (outline.Count + MaxPoints - 1) / MaxPoints;
            for (int i = 0; i < outline.Count && result.Count < MaxPoints; i += Math.Max(1, step))
                result.Add(new JArray(M(outline[i].x), M(outline[i].y)));
            return result;
        }

        static JArray Ids(IEnumerable<string> ids, int max)
        {
            var result = new JArray();
            var seen = new HashSet<string>();
            if (ids == null) return result;
            foreach (var id in ids) if (result.Count < max && IsId(id) && seen.Add(id)) result.Add(id);
            return result;
        }

        static string Intent(string s)
        {
            s = Regex.Replace(s ?? "", "[\\u0000-\\u001f]", " ").Trim();
            return s.Length <= MaxIntent ? s : s.Substring(0, MaxIntent);
        }

        static bool IsId(string s) => !string.IsNullOrEmpty(s) && s.Length <= 64 && CatalogId.IsMatch(s);
        static string IdOrEmpty(string s) => IsId(s) ? s : "";

        // Centimeter precision, inside the proxy's ±100 m range; NaN becomes 0.
        static double M(float v) => float.IsNaN(v) ? 0 : Math.Round(Math.Max(-100f, Math.Min(100f, v)), 2);

        static double Deg(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0;
            double d = v % 360.0;
            return Math.Round(d < 0 ? d + 360.0 : d, 1);
        }
    }
}

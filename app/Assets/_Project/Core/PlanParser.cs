using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Strict reader for the assistant's JSON answer. Only known fields of the right type are read;
    /// <c>fromFallback</c> and <c>notice</c> are core's and ignored. A broken root fails the whole
    /// plan; a broken entry is dropped with a reason. Ids must look like catalog ids; whether they
    /// exist and fit is checked later by <c>PlanChecker</c>.
    /// </summary>
    public static class PlanParser
    {
        public const int MaxPlacements = 12;
        public const int MaxRecommendations = 5;
        public const int MaxRationaleLength = 300;

        /// <summary>No room is bigger than this many meters from the origin.</summary>
        public const float MaxCoordinate = 50f;

        /// <summary>Yaw may be any number of turns; anything wilder is treated as missing (0).</summary>
        public const float MaxYawMagnitude = 3600f;

        static readonly Regex IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        /// <summary>False (with an empty plan) when the text is not a JSON object.</summary>
        public static bool TryParse(string text, out AssistantPlan plan, List<string> errors)
        {
            plan = new AssistantPlan();
            JObject root;
            try
            {
                root = JObject.Parse(StripFence(text ?? ""));
            }
            catch (JsonException e)
            {
                errors?.Add("plan: not a JSON object (" + e.Message + ")");
                return false;
            }

            if (root["placements"] is JArray placements)
            {
                for (int i = 0; i < placements.Count; i++)
                {
                    if (plan.placements.Count == MaxPlacements) { errors?.Add($"placements: more than {MaxPlacements}, rest ignored"); break; }
                    if (TryPlacement(placements[i] as JObject, out var p, out string why)) plan.placements.Add(p);
                    else errors?.Add($"placements[{i}]: {why}");
                }
            }
            if (root["recommendations"] is JArray recs)
            {
                for (int i = 0; i < recs.Count && plan.recommendations.Count < MaxRecommendations; i++)
                {
                    var o = recs[i] as JObject;
                    string anchor = Text(o?["anchorId"]), item = Text(o?["catalogItemId"]);
                    if (string.IsNullOrEmpty(anchor) || !IsId(item)) { errors?.Add($"recommendations[{i}]: needs anchorId and a catalog id"); continue; }
                    plan.recommendations.Add(new PlanRecommendation { anchorId = anchor, catalogItemId = item, reason = Clip(Text(o["reason"]), MaxRationaleLength) });
                }
            }
            plan.floorFinishId = IdOrEmpty(root["floorFinishId"], "floorFinishId", errors);
            plan.wallFinishId = IdOrEmpty(root["wallFinishId"], "wallFinishId", errors);
            plan.moodPresetId = IdOrEmpty(root["moodPresetId"], "moodPresetId", errors);
            plan.rationale = Clip(Text(root["rationale"]), MaxRationaleLength);
            return true;
        }

        static bool TryPlacement(JObject o, out PlanPlacement p, out string why)
        {
            p = default;
            why = null;
            if (o == null) { why = "not an object"; return false; }
            string id = Text(o["catalogItemId"]);
            if (!IsId(id)) { why = "catalogItemId is not a catalog id"; return false; }
            float? x = Number(o["x"], MaxCoordinate), z = Number(o["z"], MaxCoordinate);
            if ((x.HasValue != z.HasValue) || (o["x"] != null && o["x"].Type != JTokenType.Null && !x.HasValue))
            {
                why = "x and z must both be numbers within the room, or both left out";
                return false;
            }
            float yaw = Number(o["yawDegrees"], MaxYawMagnitude) ?? 0f; // wrapped to 0..360 below
            yaw %= 360f;
            if (yaw < 0f) yaw += 360f;
            p = new PlanPlacement { catalogItemId = id, x = x, z = z, yawDegrees = yaw };
            return true;
        }

        // A finite number within ±limit, else null; huge literals (1e400, >64-bit integers) must not throw.
        static float? Number(JToken t, float limit)
        {
            if (t == null || (t.Type != JTokenType.Float && t.Type != JTokenType.Integer)) return null;
            double v;
            try
            {
                v = t.Value<double>();
            }
            catch (Exception e) when (e is OverflowException || e is InvalidCastException || e is FormatException)
            {
                return null;
            }
            return double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > limit ? (float?)null : (float)v;
        }

        static string IdOrEmpty(JToken t, string field, List<string> errors)
        {
            string s = Text(t);
            if (string.IsNullOrEmpty(s) || IsId(s)) return s ?? "";
            errors?.Add($"{field}: \"{Clip(s, 40)}\" is not an id, ignored");
            return "";
        }

        static bool IsId(string s) => !string.IsNullOrEmpty(s) && s.Length <= 64 && IdPattern.IsMatch(s);
        static string Text(JToken t) => t != null && t.Type == JTokenType.String ? ((string)t).Trim() : "";
        static string Clip(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        // Models sometimes wrap JSON in a ```json fence even when asked for raw JSON.
        static string StripFence(string s)
        {
            s = s.Trim();
            if (!s.StartsWith("```")) return s;
            int start = s.IndexOf('\n');
            int end = s.LastIndexOf("```", StringComparison.Ordinal);
            return start > 0 && end > start ? s.Substring(start + 1, end - start - 1) : s;
        }
    }
}

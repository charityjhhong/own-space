using System;
using System.Collections.Generic;
using System.Linq;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Checks an assistant plan against the room, the zone and the catalog with the same rules as
    /// manual edits. A piece with a position that breaks a rule is nudged to the nearest pose within
    /// <see cref="MaxNudge"/> that passes; a piece without a position (or one that cannot be nudged) is
    /// laid out on a <see cref="GridStep"/> grid, nearest the middle of the free floor first.
    /// Anything that still fails is dropped with a reason. Runs off the main thread on a snapshot.
    /// </summary>
    static class PlanChecker
    {
        public const float GridStep = 0.25f;
        public const float MaxNudge = 1.0f;
        static readonly float[] Turns = { 0f, 90f, 180f, 270f };

        // The view to keep open for the plan being checked (see ViewComfort); per thread, set only during Check.
        [ThreadStatic] static ViewComfort _view;
        const int ComfortBit = 1 << 30;

        // Where the user sits (see Facing.UserSpot), for turning floor pieces toward them; per thread, set only
        // during Check. _face: Nudge and Layout try only the yaw that faces the user.
        [ThreadStatic] static Vec2? _user;
        [ThreadStatic] static bool _face;
        [ThreadStatic] static float _faceStep;

        /// <param name="userPosition">The user's head, when known; otherwise the view is taken from the desk or bed in my zone.</param>
        public static AssistantOutcome Check(AssistantPlan plan, AssistantRequest request, ICatalog catalog,
            ICollection<string> finishIds, ICollection<string> moodIds, Vec3? userPosition = null)
        {
            bool fill = request.Task == AssistantTask.FillOutside;
            _view = fill ? ViewComfort.For(request.Room, request.Design.zoneOutline, userPosition) : null;
            _user = fill ? Facing.UserSpot(request.Room, request.Design.zoneOutline, userPosition) : null;
            try { return CheckCore(plan, request, catalog, finishIds, moodIds); }
            finally { _view = null; _user = null; _face = false; }
        }

        static AssistantOutcome CheckCore(AssistantPlan plan, AssistantRequest request, ICatalog catalog,
            ICollection<string> finishIds, ICollection<string> moodIds)
        {
            var outcome = new AssistantOutcome(request.Task, plan);
            outcome.FloorFinishId = Known(plan.floorFinishId, finishIds, "floor finish", outcome);
            outcome.WallFinishId = Known(plan.wallFinishId, finishIds, "wall finish", outcome);
            outcome.MoodPresetId = Known(plan.moodPresetId, moodIds, "mood", outcome);
            if (request.Task == AssistantTask.FillOutside) CheckPlacements(plan, request, catalog, outcome);
            else CheckRecommendations(plan, request, catalog, outcome);
            return outcome;
        }

        /// <summary>Fallback for RecommendDesign: items that fit the anchor, those matching the current mood first, at most 3.</summary>
        public static void RecommendFitting(AssistantOutcome outcome, AssistantRequest request, ICatalog catalog)
        {
            var anchor = Find(request.Room, request.AnchorId);
            if (anchor == null || !Zones.Contains(request.Design.zoneOutline, anchor)) return;
            var fits = new List<(CatalogItem item, FitResult fit)>();
            CatalogQueries.FittingItems(catalog, anchor, fits);
            string mood = request.Design.moodPresetId;
            // Stable order: mood matches first, otherwise catalog order.
            foreach (var (item, fit) in fits.OrderByDescending(f => Matches(f.item, mood)).Take(3))
                outcome.Recommendations.Add((item, fit, Matches(item, mood) ? $"Suits the {mood} mood." : "Fits this piece."));
        }

        // Each pick must name the requested anchor, be a my-zone catalog item, and pass FitRule (the stretch,
        // turn and Y scale come from that check, never from the model).
        static void CheckRecommendations(AssistantPlan plan, AssistantRequest request, ICatalog catalog, AssistantOutcome outcome)
        {
            var anchor = Find(request.Room, request.AnchorId);
            foreach (var rec in plan.recommendations)
            {
                if (anchor != null && rec.anchorId == anchor.Id && Zones.Contains(request.Design.zoneOutline, anchor) &&
                    catalog.TryGet(rec.catalogItemId, out var item) && item.AllowedIn("my") &&
                    !outcome.Recommendations.Exists(r => r.item.id == item.id))
                {
                    var fit = FitRule.Check(anchor, item);
                    if (fit.Fits)
                    {
                        outcome.Recommendations.Add((item, fit, rec.reason ?? ""));
                        continue;
                    }
                }
                outcome.Dropped.Add(rec.catalogItemId + ": does not fit " + rec.anchorId);
            }
        }

        static void CheckPlacements(AssistantPlan plan, AssistantRequest request, ICatalog catalog, AssistantOutcome outcome)
        {
            var work = request.Design.Clone();
            // The user's own outside pieces stay and are obstacles: the plan only adds.
            var free = new FreeFloor(request.Room, work.zoneOutline);
            outcome.FreeFloorArea = free.Area;
            int next = 1;
            var reasons = new List<string>();
            FreePlacement? table = null;
            CatalogItem tableItem = null;
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            // Kinds the user already placed by hand. The last AI plan's pieces are being replaced, so they don't count
            // (AssistantRunner already leaves them out of the request's design; aiPlaced is checked again here).
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in request.Design.placements)
                if (!q.aiPlaced && catalog.TryGet(q.catalogItemId, out var mine)) owned.Add(KindOf(mine));
            foreach (var pp in TablesFirst(plan.placements, catalog))
            {
                if (!catalog.TryGet(pp.catalogItemId, out var item))
                {
                    Skip(outcome, pp.catalogItemId, "unknown id", reasons);
                    continue;
                }
                if (!item.AllowedIn("outside") || item.kind == HideRules.CoverPropKind)
                {
                    Skip(outcome, item.id, "not for outside", reasons);
                    continue;
                }
                if (HandOnly(item))
                {
                    Skip(outcome, item.id, "placed by hand", reasons);
                    continue;
                }
                // One piece of each kind (one armchair, one lamp...): the first one asked for wins. Device bug: two
                // armchairs in one "Bright studio" plan. A piece with no kind is only kept once by id.
                if (owned.Contains(KindOf(item)))
                {
                    Skip(outcome, item.id, "you already have one", reasons); // human: no second bed or sofa next to yours
                    continue;
                }
                if (!kinds.Add(KindOf(item)))
                {
                    Skip(outcome, item.id, "one of each kind", reasons);
                    continue;
                }
                while (work.placements.Exists(q => q.placementId == "ai-" + next)) next++;
                var p = new FreePlacement { placementId = "ai-" + next, catalogItemId = item.id, yawDegrees = pp.yawDegrees, aiPlaced = true };
                int seen = 0;
                Vec2? at = pp.x.HasValue && pp.z.HasValue ? new Vec2(pp.x.Value, pp.z.Value) : (Vec2?)null;
                var role = SetLayout.RoleOf(item);
                bool placed = Arrange(request.Room, work, catalog, ref p, item, role, at, table, tableItem, ref seen)
                              || AwayFromView(request.Room, work, catalog, ref p, item, at, ref seen)
                              || Place(request.Room, work, catalog, free, ref p, at, ref seen);
                if (!placed)
                    foreach (var smaller in Smaller(catalog, item))
                    {
                        p.catalogItemId = smaller.id;
                        if (!Place(request.Room, work, catalog, free, ref p, at, ref seen)) continue;
                        outcome.Swaps.Add($"{item.id} -> {smaller.id}");
                        placed = true;
                        break;
                    }
                if (!placed)
                {
                    Skip(outcome, item.id, Reason(free, seen, role == SetLayout.Role.Wall), reasons);
                    continue;
                }
                next++;
                work.placements.Add(p);
                outcome.Placements.Add(p);
                if (role == SetLayout.Role.Table && table == null && catalog.TryGet(p.catalogItemId, out tableItem)) table = p;
            }
            outcome.Summary = Summary(outcome.Placements.Count, reasons);
        }

        static string KindOf(CatalogItem item) => string.IsNullOrEmpty(item.kind) ? "id:" + item.id : item.kind;

        /// <summary>
        /// Window pieces (curtains, blinds, "window" tag) are never placed by an AI plan, a preset set or the
        /// offline plan: on device their spot was always wrong. The user adds them by hand. (Once #317 lands,
        /// this becomes PlacementRules.IsWindowMounted, which also reads attachTo "window".)
        /// </summary>
        internal static bool HandOnly(CatalogItem item) =>
            item != null && (item.kind == "curtain" || item.kind == "blind" || (item.tags != null && item.tags.Contains("window")));

        // The first table goes first, so the rug, the pendant and the seats can be arranged around it.
        static IEnumerable<PlanPlacement> TablesFirst(List<PlanPlacement> placements, ICatalog catalog)
        {
            int first = placements.FindIndex(pp => catalog.TryGet(pp.catalogItemId, out var it) && it.AllowedIn("outside") &&
                                                   SetLayout.RoleOf(it) == SetLayout.Role.Table);
            if (first >= 0) yield return placements[first];
            for (int i = 0; i < placements.Count; i++) if (i != first) yield return placements[i];
        }

        // A tall piece the model put in the user's view goes against the nearest wall, else into a corner,
        // rather than the nearest free floor (which would often still be right in front of them).
        static bool AwayFromView(IRoomInfo room, DesignState work, ICatalog catalog, ref FreePlacement p, CatalogItem item, Vec2? at, ref int seen)
        {
            if (_view == null || !at.HasValue || !_view.Blocks(item, new Vec3(at.Value.x, room.FloorY, at.Value.y), p.yawDegrees)) return false;
            seen |= ComfortBit;
            foreach (var role in new[] { SetLayout.Role.Shelf, SetLayout.Role.Plant })
            {
                var spots = SetLayout.Spots(room, role, item, null, null, at);
                if (spots == null) continue;
                foreach (var (spot, yaw) in spots)
                    if (TryAt(room, work, catalog, ref p, new Vec3(spot.x, room.FloorY, spot.y), Turned(item, role, spot, yaw), ref seen)) return true;
            }
            return false;
        }

        // The model's spot when it already follows the piece's set rule, else the rule's spots (see SetLayout).
        static bool Arrange(IRoomInfo room, DesignState work, ICatalog catalog, ref FreePlacement p, CatalogItem item,
            SetLayout.Role role, Vec2? at, FreePlacement? table, CatalogItem tableItem, ref int seen)
        {
            if (role == SetLayout.Role.None || role == SetLayout.Role.Table) return false;
            // A seat faces its table; the human: a sofa had its back to me. So a seat first takes a side of the
            // table where facing it also faces the user, and only when none fits, any side.
            bool seat = role == SetLayout.Role.Seat && _user.HasValue;
            for (int pass = seat ? 0 : 1; pass < 2; pass++)
            {
                bool towardOnly = pass == 0;
                if (at.HasValue && (!towardOnly || Toward(at.Value, p.yawDegrees)))
                {
                    var asked = p;
                    if (TryAt(room, work, catalog, ref asked, new Vec3(at.Value.x, room.FloorY, at.Value.y), Turned(item, role, at.Value, p.yawDegrees), ref seen) &&
                        SetLayout.Follows(room, role, item, asked, table, tableItem))
                    { p = asked; return true; }
                }
                var spots = SetLayout.Spots(room, role, item, table, tableItem, at);
                if (spots == null) return false;
                foreach (var (spot, yaw) in spots)
                {
                    float turned = Turned(item, role, spot, yaw);
                    if (towardOnly && !Toward(spot, turned)) continue;
                    if (TryAt(room, work, catalog, ref p, new Vec3(spot.x, room.FloorY, spot.y), turned, ref seen)) return true;
                }
            }
            return false;
        }

        static bool Toward(Vec2 at, float yaw) => !_user.HasValue || Facing.RoughlyToward(yaw, at, _user.Value);

        // A floor piece's front turns toward the user (the model's yaw is not kept). Seats at a table keep facing
        // it; a piece with its back to a wall keeps facing out while that is within 90° of the user. Pieces
        // with no front (rugs, hanging, wall and window pieces) keep their yaw.
        static float Turned(CatalogItem item, SetLayout.Role role, Vec2 at, float yaw)
        {
            if (!_user.HasValue || !Facing.HasFront(item) || role == SetLayout.Role.Seat) return yaw;
            if (role == SetLayout.Role.Shelf && Facing.RoughlyToward(yaw, at, _user.Value)) return yaw;
            return Facing.TowardUser(at, _user.Value, yaw);
        }

        static void Skip(AssistantOutcome outcome, string id, string reason, List<string> reasons)
        {
            outcome.Dropped.Add(id + ": " + reason);
            reasons.Add(reason);
        }

        // At the asked spot (nudged), else on the coarse grid, else on the fine grid when the free floor is small.
        // Facing the user first; only when no spot fits that way, at any turn.
        static bool Place(IRoomInfo room, DesignState work, ICatalog catalog, FreeFloor free, ref FreePlacement p, Vec2? at, ref int seen)
        {
            if (_user.HasValue && catalog.TryGet(p.catalogItemId, out var item) && Facing.HasFront(item))
            {
                _face = true;
                // A table turns square to the room (90° steps): the rug, seats and pendant are laid out around it.
                _faceStep = SetLayout.RoleOf(item) == SetLayout.Role.Table ? 90f : Facing.SnapDegrees;
                try { if (PlaceAnyTurn(room, work, catalog, free, ref p, at, ref seen)) return true; }
                finally { _face = false; }
            }
            return PlaceAnyTurn(room, work, catalog, free, ref p, at, ref seen);
        }

        static bool PlaceAnyTurn(IRoomInfo room, DesignState work, ICatalog catalog, FreeFloor free, ref FreePlacement p, Vec2? at, ref int seen)
        {
            if (at.HasValue && Nudge(room, work, catalog, ref p, at.Value, ref seen)) return true;
            return Layout(room, work, catalog, ref p, free.Coarse, ref seen) || Layout(room, work, catalog, ref p, free.Fine, ref seen);
        }

        /// <summary>Smaller outside items to try instead: the same kind first (or the same role, except "Other"), closest in size first.</summary>
        static IEnumerable<CatalogItem> Smaller(ICatalog catalog, CatalogItem item) =>
            catalog.Items.Where(c => c != null && c.id != item.id && c.AllowedIn("outside") && c.kind != HideRules.CoverPropKind &&
                                     (c.kind == item.kind || (c.role == item.role && item.role != FurnitureRole.Other)) &&
                                     Area(c) < Area(item))
                .OrderByDescending(c => c.kind == item.kind).ThenByDescending(Area).ThenBy(c => c.id, StringComparer.Ordinal);

        static float Area(CatalogItem c) => c.size.x * c.size.z;

        // Why a piece found no spot, from the rules it failed: a spot that only failed on another piece
        // or an opening says so; otherwise the piece is bigger than any free patch.
        static string Reason(FreeFloor free, int seen, bool wall)
        {
            if (wall) return "no free wall spot"; // every wall spot was taken, blocked or in my zone
            if (free.Fine.Count == 0 && free.Coarse.Count == 0) return "no free floor outside your zone";
            if (Saw(seen, PlacementStatus.Overlaps)) return "overlaps other pieces";
            if (Saw(seen, PlacementStatus.OverlapsFurniture)) return "blocked by real furniture";
            if (Saw(seen, PlacementStatus.BlocksDoor)) return "would block a door";
            if (Saw(seen, PlacementStatus.BlocksWindow)) return "would block a window";
            if ((seen & ComfortBit) != 0) return "would block your view";
            return "too big for the free floor";
        }

        /// <summary>
        /// One line for the panel (at most 60 characters) whenever a piece was skipped, from the most common
        /// skip reason, e.g. "Placed 1 of 6: not enough free floor"; "" when every piece was placed.
        /// </summary>
        static string Summary(int placed, List<string> reasons)
        {
            if (reasons.Count == 0) return "";
            string why = null; int best = 0;
            foreach (var r in reasons)
            {
                string w = Short(r);
                int n = reasons.Count(x => Short(x) == w);
                if (n > best) { why = w; best = n; }
            }
            if (placed > 0) return $"Placed {placed} of {placed + reasons.Count}: {why}";
            return why == "not enough free floor" ? "Not enough free floor outside your zone" : char.ToUpperInvariant(why[0]) + why.Substring(1);
        }

        static string Short(string reason) =>
            reason == "overlaps other pieces" ? "no free spot left"
            : reason == "blocked by real furniture" ? "real furniture in the way"
            : reason == "would block a door" || reason == "would block a window" ? "doors and windows in the way"
            : reason == "unknown id" || reason == "not for outside" ? "unknown pieces"
            : reason == "no free wall spot" ? "no free wall"
            : reason == "would block your view" ? "kept your view clear"
            : reason == "one of each kind" ? "one of each kind"
            : reason == "you already have one" ? "you already have one"
            : reason == "placed by hand" ? "curtains go up by hand"
            : "not enough free floor";

        static bool Saw(int seen, PlacementStatus s) => (seen & (1 << (int)s)) != 0;

        /// <summary>
        /// Outside items that fit somewhere on the free floor on their own (the user's pieces are obstacles),
        /// so the assistant only picks pieces that can be placed. Empty when nothing fits.
        /// </summary>
        internal static List<CatalogItem> Placeable(IRoomInfo room, DesignState design, IEnumerable<CatalogItem> items, FreeFloor free)
        {
            var list = new List<CatalogItem>();
            var all = new List<CatalogItem>();
            foreach (var item in items) if (item != null) all.Add(item);
            var catalog = new Catalog(all);
            foreach (var item in all)
            {
                if (!item.AllowedIn("outside") || item.kind == HideRules.CoverPropKind || HandOnly(item)) continue;
                var p = new FreePlacement { placementId = "ai-probe", catalogItemId = item.id };
                int seen = 0;
                bool wall = SetLayout.RoleOf(item) == SetLayout.Role.Wall;
                if (wall ? Arrange(room, design, catalog, ref p, item, SetLayout.Role.Wall, null, null, null, ref seen)
                         : Layout(room, design, catalog, ref p, free.Coarse, ref seen) || Layout(room, design, catalog, ref p, free.Fine, ref seen))
                    list.Add(item);
            }
            return list;
        }

        // Grid offsets up to MaxNudge, nearest first; ties broken by x, then z, so results are repeatable.
        static readonly List<Vec2> NudgeOffsets = BuildOffsets();

        static List<Vec2> BuildOffsets()
        {
            var list = new List<Vec2>(); int n = (int)Math.Round(MaxNudge / GridStep);
            for (int i = -n; i <= n; i++)
                for (int j = -n; j <= n; j++)
                    if (i * i + j * j <= n * n) list.Add(new Vec2(i * GridStep, j * GridStep));
            list.Sort((a, b) =>
            {
                int c = Dist2(a, default).CompareTo(Dist2(b, default));
                if (c == 0) c = a.x.CompareTo(b.x);
                return c != 0 ? c : a.y.CompareTo(b.y);
            });
            return list;
        }

        // The asked yaw first at each offset, then a quarter turn.
        static bool Nudge(IRoomInfo room, DesignState work, ICatalog catalog, ref FreePlacement p, Vec2 at, ref int seen)
        {
            float yaw = p.yawDegrees;
            foreach (var o in NudgeOffsets)
            {
                var pos = new Vec3(at.x + o.x, room.FloorY, at.y + o.y);
                if (_face)
                {
                    if (TryAt(room, work, catalog, ref p, pos, Facing.TowardUser(pos.Floor(), _user.Value, yaw, _faceStep), ref seen)) return true;
                    continue;
                }
                if (TryAt(room, work, catalog, ref p, pos, yaw, ref seen) || TryAt(room, work, catalog, ref p, pos, (yaw + 90f) % 360f, ref seen)) return true;
            }
            return false;
        }

        static bool Layout(IRoomInfo room, DesignState work, ICatalog catalog, ref FreePlacement p, List<Vec2> grid, ref int seen)
        {
            foreach (var g in grid)
            {
                if (_face)
                {
                    if (TryAt(room, work, catalog, ref p, new Vec3(g.x, room.FloorY, g.y), Facing.TowardUser(g, _user.Value, 0f, _faceStep), ref seen)) return true;
                    continue;
                }
                foreach (var yaw in Turns)
                    if (TryAt(room, work, catalog, ref p, new Vec3(g.x, room.FloorY, g.y), yaw, ref seen)) return true;
            }
            return false;
        }

        // Records every failed rule in the `seen` bit set, for the skip reason.
        static bool TryAt(IRoomInfo room, DesignState work, ICatalog catalog, ref FreePlacement p, Vec3 pos, float yaw, ref int seen)
        {
            var candidate = p;
            if (catalog.TryGet(p.catalogItemId, out var item)) pos.y = PlacementRules.MountY(room, item);
            candidate.position = pos;
            candidate.yawDegrees = yaw;
            var status = PlacementRules.Check(room, work, catalog, candidate);
            if (status != PlacementStatus.Ok) { seen |= 1 << (int)status; return false; }
            if (_view != null && item != null && _view.Blocks(item, pos, yaw)) { seen |= ComfortBit; return false; }
            p = candidate;
            return true;
        }

        static string Known(string id, ICollection<string> known, string what, AssistantOutcome outcome)
        {
            if (string.IsNullOrEmpty(id) || known == null || known.Contains(id)) return id ?? "";
            outcome.Dropped.Add($"{what} \"{id}\": unknown");
            return "";
        }

        static FurnitureAnchor Find(IRoomInfo room, string id)
        {
            foreach (var a in room.Furniture) if (a.Id == id) return a;
            return null;
        }

        static bool Matches(CatalogItem item, string mood) => !string.IsNullOrEmpty(mood) && item.moods != null && item.moods.Contains(mood);
        static float Dist2(Vec2 a, Vec2 b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
    }

    /// <summary>
    /// The free floor (in the room, outside my zone) sampled on a <see cref="PlanChecker.GridStep"/> grid,
    /// nearest its middle first, and on a <see cref="FineStep"/> grid for narrow patches the coarse grid
    /// misses. With no scan the free floor is taken as a 6 m square around the origin.
    /// </summary>
    sealed class FreeFloor
    {
        public const float FineStep = 0.1f;
        /// <summary>The fine grid is laid out only when the coarse one has fewer points (a small free floor).</summary>
        public const int FineLimit = 200;

        public readonly List<Vec2> Coarse, Fine;
        /// <summary>Square meters, from the fine samples.</summary>
        public readonly float Area;
        public readonly float MinX, MinZ, MaxX, MaxZ;

        public FreeFloor(IRoomInfo room, IReadOnlyList<Vec2> zone)
        {
            var outline = room.FloorOutline;
            Coarse = Sample(room, zone, PlanChecker.GridStep);
            var fine = Sample(room, zone, FineStep);
            Area = fine.Count * FineStep * FineStep;
            MinX = MinZ = float.MaxValue; MaxX = MaxZ = float.MinValue;
            foreach (var v in fine)
            {
                MinX = Math.Min(MinX, v.x - FineStep / 2f); MaxX = Math.Max(MaxX, v.x + FineStep / 2f);
                MinZ = Math.Min(MinZ, v.y - FineStep / 2f); MaxZ = Math.Max(MaxZ, v.y + FineStep / 2f);
            }
            if (fine.Count == 0) MinX = MinZ = MaxX = MaxZ = 0f;
            Fine = Coarse.Count < FineLimit ? fine : new List<Vec2>();
        }

        static List<Vec2> Sample(IRoomInfo room, IReadOnlyList<Vec2> zone, float step)
        {
            var outline = room.FloorOutline;
            float minX = -3f, maxX = 3f, minZ = -3f, maxZ = 3f;
            if (outline.Count >= 3)
            {
                minX = minZ = float.MaxValue;
                maxX = maxZ = float.MinValue;
                foreach (var v in outline)
                {
                    minX = Math.Min(minX, v.x); maxX = Math.Max(maxX, v.x);
                    minZ = Math.Min(minZ, v.y); maxZ = Math.Max(maxZ, v.y);
                }
            }
            var points = new List<Vec2>(); float cx = 0f, cz = 0f;
            int nx = (int)Math.Floor((maxX - minX) / step + 1e-3f), nz = (int)Math.Floor((maxZ - minZ) / step + 1e-3f); // 3.6 / 0.1 is 35.99998 in float
            for (int i = 1; i < nx; i++)
                for (int j = 1; j < nz; j++)
                {
                    // Rounded to 0.1 mm: in float -1.8 + 32 * 0.1 is 1.4000001, which pushed a piece that
                    // exactly fits 0.1 µm through the wall.
                    var v = new Vec2(Snap(minX + i * (double)step), Snap(minZ + j * (double)step));
                    if ((outline.Count >= 3 && !Zones.Contains(outline, v)) || Zones.Contains(zone, v)) continue;
                    points.Add(v);
                    cx += v.x; cz += v.y;
                }
            if (points.Count == 0) return points;
            var mid = new Vec2(cx / points.Count, cz / points.Count);
            points.Sort((a, b) => Dist2(a, mid).CompareTo(Dist2(b, mid)));
            return points;
        }

        static float Snap(double v) => (float)Math.Round(v, 4);
        static float Dist2(Vec2 a, Vec2 b) => (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
    }
}

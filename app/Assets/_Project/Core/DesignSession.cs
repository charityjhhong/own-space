using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    public enum RedesignOutcome
    {
        Applied,
        UnknownAnchor,
        OutsideMyZone,
        DoesNotFit,
    }

    public enum DesignIssueKind
    {
        /// <summary>The anchor is not in the current room (maybe still loading). The choice is kept.</summary>
        MissingAnchor,
        AnchorOutsideZone,
        UnknownItem,
        NoLongerFits,
        PlacementNotAllowed,
        /// <summary>A finish id not in <see cref="DesignSession.KnownFinishIds"/>; reset to real. Id is the finish id.</summary>
        UnknownFinish,
        /// <summary>A mood id not in <see cref="DesignSession.KnownMoodIds"/>; reset to none. Id is the mood id.</summary>
        UnknownMood,
        /// <summary>A hide box or cover prop that breaks <see cref="HideRules"/>; see <see cref="DesignIssue.Hide"/>.</summary>
        HideNotAllowed,
    }

    /// <summary>A saved choice or placement that did not survive re-validation (unless <see cref="Dropped"/> is false).</summary>
    public readonly struct DesignIssue
    {
        public DesignIssue(string id, DesignIssueKind kind, bool dropped,
            PlacementStatus placement = PlacementStatus.Ok, HideStatus hide = HideStatus.Ok)
        {
            Hide = hide;
            Id = id;
            Kind = kind;
            Dropped = dropped;
            Placement = placement;
        }

        /// <summary>Anchor id or placement id.</summary>
        public string Id { get; }
        public DesignIssueKind Kind { get; }
        public bool Dropped { get; }
        public PlacementStatus Placement { get; }
        public HideStatus Hide { get; }
    }

    /// <summary>
    /// The in-memory editor of a <see cref="DesignState"/>: every change goes through here, so it
    /// can be undone, raises <see cref="Changed"/> once, and respects the zone and fit rules.
    /// Changes allocate a snapshot for undo; call them on user actions, not every frame
    /// (move the visual while dragging, then call <see cref="MovePlacement"/> on release).
    /// </summary>
    public sealed class DesignSession
    {
        public const int MaxUndo = 50;

        readonly IRoomInfo _room;
        readonly List<DesignIssue> _issues = new List<DesignIssue>();
        readonly List<DesignState> _undo = new List<DesignState>();
        readonly List<DesignState> _redo = new List<DesignState>();

        /// <summary>Without a catalog: placements get only the zone check, and saved items are not re-fitted (set <see cref="Catalog"/> later).</summary>
        public DesignSession(IRoomInfo room, DesignState state = null)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            State = state ?? new DesignState();
        }

        public DesignSession(IRoomInfo room, ICatalog catalog, DesignState state = null) : this(room, state)
        {
            Catalog = catalog;
        }

        /// <summary>Receives <see cref="IssuesLine"/> after each check that dropped or flagged something (Load, SetZone, Revalidate).</summary>
        public Action<string> Log { get; set; }

        /// <summary>One log line for the last check's issues, e.g. "Design check: 2 issue(s): p1 PlacementNotAllowed (UnknownItem); …"; "" with none.</summary>
        public string IssuesLine()
        {
            if (_issues.Count == 0) return "";
            var parts = new List<string>(_issues.Count);
            foreach (var i in _issues)
                parts.Add($"{i.Id} {i.Kind}" + (i.Kind == DesignIssueKind.PlacementNotAllowed ? $" ({i.Placement})" : "") + (i.Dropped ? " dropped" : ""));
            return $"Design check: {_issues.Count} issue(s): " + string.Join("; ", parts);
        }

        /// <summary>
        /// Enables the full <see cref="PlacementRules"/> and item re-fitting in <see cref="Revalidate"/>.
        /// Null (the default) keeps the P1 behavior. Call <see cref="Revalidate"/> after setting it.
        /// </summary>
        public ICatalog Catalog { get; set; }

        /// <summary>Optional: finish ids content ships (finishes.json). When set, unknown ids are reset to real on validation.</summary>
        public ICollection<string> KnownFinishIds { get; set; }

        /// <summary>Optional: mood preset ids content ships (morning, afternoon, evening, night). When set, unknown ids are cleared.</summary>
        public ICollection<string> KnownMoodIds { get; set; }

        /// <summary>The current design. Read it; change it only through this session's methods.</summary>
        public DesignState State { get; private set; }

        /// <summary>Raised once after every change, undo, redo or load.</summary>
        public event Action Changed;

        /// <summary>What the last <see cref="SetZone"/>, <see cref="Load"/> or <see cref="Revalidate"/> found.</summary>
        public IReadOnlyList<DesignIssue> Issues => _issues;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public bool IsInMyZone(FurnitureAnchor anchor) => Zones.Contains(State.zoneOutline, anchor);

        /// <summary>Sets my zone, then re-validates: redesigns now outside it go back to real, pieces now inside it are removed.</summary>
        public void SetZone(IReadOnlyList<Vec2> outline) => Commit(s =>
        {
            s.zoneOutline = new List<Vec2>(outline ?? Array.Empty<Vec2>());
            Validate(s);
            return true;
        });

        /// <summary>
        /// Re-checks every choice and piece against the current room, zone and catalog (call after
        /// the room reloads). Drops what no longer fits, refreshes orientation and Y scale, and
        /// reports in <see cref="Issues"/>. Undoable when it changes anything.
        /// </summary>
        public IReadOnlyList<DesignIssue> Revalidate()
        {
            var before = State.Clone();
            Validate(State);
            if (!DesignStateJson.ToJson(before).Equals(DesignStateJson.ToJson(State))) Record(before);
            return _issues;
        }

        /// <summary>Shows <paramref name="item"/> over a real anchor in my zone if it fits; stores its orientation and Y scale.</summary>
        public RedesignOutcome Redesign(string anchorId, CatalogItem item, out FitResult fit)
        {
            fit = default;
            var anchor = FindAnchor(anchorId);
            if (anchor == null) return RedesignOutcome.UnknownAnchor;
            if (!IsInMyZone(anchor)) return RedesignOutcome.OutsideMyZone;
            fit = CatalogQueries.FitFor(Catalog, anchor, item); // a stretched option counts when nothing fits
            if (!fit.Fits) return RedesignOutcome.DoesNotFit;

            var choice = new AnchorChoice
            {
                anchorId = anchorId, mode = AnchorMode.Redesign, catalogItemId = item.id,
                rotated90 = fit.Rotated90, yScale = fit.YScale, xScale = fit.XScale, zScale = fit.ZScale,
            };
            Commit(s => { s.SetChoice(choice); return true; });
            return RedesignOutcome.Applied;
        }

        /// <summary>Shows the real anchor again. False if it already was.</summary>
        public bool KeepReal(string anchorId) =>
            Commit(s => s.anchorChoices.RemoveAll(c => c.anchorId == anchorId) > 0);

        /// <summary>Sets a zone's floor or wall finish; empty or null keeps the real surface.</summary>
        public void SetFinish(DesignZone zone, FinishSurface surface, string finishId) => Commit(s =>
        {
            var f = zone == DesignZone.Mine ? s.myZoneFinish : s.outsideFinish;
            bool changed = surface == FinishSurface.Floor ? Swap(ref f.floorFinishId, finishId) : Swap(ref f.wallFinishId, finishId);
            if (zone == DesignZone.Mine) s.myZoneFinish = f; else s.outsideFinish = f;
            return changed;
        });
        /// <summary>P1 setters, kept for compatibility: they set the outside zone's finish.</summary>
        public void SetFloorFinish(string finishId) => SetFinish(DesignZone.Outside, FinishSurface.Floor, finishId);
        public void SetWallFinish(string finishId) => SetFinish(DesignZone.Outside, FinishSurface.Wall, finishId);

        public void SetMood(string moodPresetId) => Commit(s => Swap(ref s.moodPresetId, moodPresetId));

        /// <summary>
        /// The placement rules for a piece, without changing anything. Allocation-free: use it for a
        /// drag preview. Without a <see cref="Catalog"/> only "not in my zone" (piece center) is checked.
        /// </summary>
        public PlacementStatus CheckPlacement(FreePlacement placement, string ignorePlacementId = null)
        {
            if (Catalog != null) return PlacementRules.Check(_room, State, Catalog, placement, ignorePlacementId);
            return Zones.Contains(State.zoneOutline, placement.position) ? PlacementStatus.InMyZone : PlacementStatus.Ok;
        }

        /// <summary>Adds a free piece in the outside zone if the rules allow it; <paramref name="status"/> says why not.</summary>
        public bool TryAddPlacement(FreePlacement placement, out PlacementStatus status)
        {
            Mount(ref placement);
            status = string.IsNullOrEmpty(placement.placementId) || IndexOf(placement.placementId) >= 0
                ? PlacementStatus.DuplicateId
                : CheckPlacement(placement);
            if (status == PlacementStatus.OutsideRoom && PlacementRules.TryNudgeIntoRoom(_room, State, Catalog, ref placement)) status = PlacementStatus.Ok;
            return status == PlacementStatus.Ok && Commit(s => { s.placements.Add(placement); return true; });
        }

        // Hanging pieces go up to their ceiling height. Wall pieces go flush on the nearest wall, facing into
        // the room, keeping their asked height (a piece given at floor level gets the default 1.5 m centre).
        // Everything else stands where it was put.
        void Mount(ref FreePlacement p)
        {
            if (Catalog == null || !Catalog.TryGet(p.catalogItemId ?? "", out var item)) return;
            if (PlacementRules.IsHanging(item)) p.position = new Vec3(p.position.x, PlacementRules.MountY(_room, item), p.position.z);
            else if (PlacementRules.IsWallMounted(item))
            {
                float floor = _room?.FloorY ?? 0f;
                float center = p.position.y <= floor + 0.01f ? floor + PlacementRules.DefaultWallCenter : p.position.y + item.size.y / 2f;
                if (PlacementRules.TrySnapToWall(_room, item, new Vec3(p.position.x, center, p.position.z), out var at, out float yaw))
                {
                    p.position = at;
                    p.yawDegrees = yaw;
                }
            }
        }

        /// <summary>Moves a free piece (call on release, not while dragging) if the rules allow the new pose.</summary>
        public bool TryMovePlacement(string placementId, Vec3 position, float yawDegrees, out PlacementStatus status)
        {
            int i = IndexOf(placementId);
            if (i < 0)
            {
                status = PlacementStatus.UnknownPlacement;
                return false;
            }
            var moved = State.placements[i];
            moved.position = position;
            moved.yawDegrees = yawDegrees;
            Mount(ref moved);
            status = CheckPlacement(moved, placementId);
            // A turn or drag next to a wall pushes a corner through it: move it back in instead of refusing.
            if (status == PlacementStatus.OutsideRoom && PlacementRules.TryNudgeIntoRoom(_room, State, Catalog, ref moved, placementId)) status = PlacementStatus.Ok;
            return status == PlacementStatus.Ok && Commit(s => { s.placements[i] = moved; return true; });
        }

        /// <summary>P1 form of <see cref="TryAddPlacement"/>.</summary>
        public bool AddPlacement(FreePlacement placement) => TryAddPlacement(placement, out _);

        /// <summary>P1 form of <see cref="TryMovePlacement"/>.</summary>
        public bool MovePlacement(string placementId, Vec3 position, float yawDegrees) =>
            TryMovePlacement(placementId, position, yawDegrees, out _);

        public bool RemovePlacement(string placementId)
        {
            int i = IndexOf(placementId);
            return i >= 0 && Commit(s => { s.placements.RemoveAt(i); return true; });
        }

        /// <summary>Hide-box rules without changing anything (allocation-free, for a drawing preview).</summary>
        public HideStatus CheckHideBox(HideBox box) => HideRules.CheckBox(_room, State, box);

        /// <summary>Cover-prop rules without changing anything. Needs <see cref="Catalog"/>.</summary>
        public HideStatus CheckCoverProp(FreePlacement prop) => HideRules.CheckProp(_room, State, Catalog, prop);

        /// <summary>Adds a hand-drawn hide box in my zone if <see cref="HideRules"/> allow it.</summary>
        public bool TryAddHideBox(HideBox box, out HideStatus status)
        {
            status = string.IsNullOrEmpty(box.hideBoxId) || State.hideBoxes.Exists(b => b.hideBoxId == box.hideBoxId)
                ? HideStatus.DuplicateId
                : CheckHideBox(box);
            return status == HideStatus.Ok && Commit(s => { s.hideBoxes.Add(box); return true; });
        }

        public bool RemoveHideBox(string hideBoxId) =>
            Commit(s => s.hideBoxes.RemoveAll(b => b.hideBoxId == hideBoxId) > 0);

        /// <summary>Places a cover prop (basket, cushions) over clutter in my zone if <see cref="HideRules"/> allow it.</summary>
        public bool TryAddCoverProp(FreePlacement prop, out HideStatus status)
        {
            status = string.IsNullOrEmpty(prop.placementId) || State.coverProps.Exists(p => p.placementId == prop.placementId)
                ? HideStatus.DuplicateId
                : CheckCoverProp(prop);
            return status == HideStatus.Ok && Commit(s => { s.coverProps.Add(prop); return true; });
        }

        public bool RemoveCoverProp(string placementId) =>
            Commit(s => s.coverProps.RemoveAll(p => p.placementId == placementId) > 0);

        public bool Undo() => Step(_undo, _redo);
        public bool Redo() => Step(_redo, _undo);

        public void Save(string path) => DesignStateJson.SaveToFile(path, State);

        /// <summary>
        /// Replaces the design with the saved one, re-validated against the current room (see
        /// <see cref="Issues"/>), and clears undo history. False (nothing changes) if there is no usable save.
        /// </summary>
        public bool Load(string path)
        {
            if (!DesignStateJson.TryLoadFromFile(path, out var loaded)) return false;
            Validate(loaded);
            State = loaded;
            _undo.Clear();
            _redo.Clear();
            Changed?.Invoke();
            return true;
        }

        /// <summary>The room this session edits (for core helpers such as the assistant).</summary>
        internal IRoomInfo Room => _room;

        /// <summary>Several changes as one undo step, for core callers that check every change themselves.</summary>
        internal bool CommitBatch(Func<DesignState, bool> change) => Commit(change);

        bool Commit(Func<DesignState, bool> change)
        {
            var before = State.Clone();
            if (!change(State)) return false;
            Record(before);
            return true;
        }

        void Record(DesignState before)
        {
            _undo.Add(before);
            if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
            _redo.Clear();
            Changed?.Invoke();
        }

        void Validate(DesignState s)
        {
            _issues.Clear();
            for (int i = s.anchorChoices.Count - 1; i >= 0; i--)
            {
                var c = s.anchorChoices[i];
                var anchor = FindAnchor(c.anchorId);
                DesignIssueKind? problem = null;
                if (anchor == null) { _issues.Add(new DesignIssue(c.anchorId, DesignIssueKind.MissingAnchor, false)); continue; }
                if (!Zones.Contains(s.zoneOutline, anchor)) problem = DesignIssueKind.AnchorOutsideZone;
                else if (c.mode == AnchorMode.Redesign)
                {
                    if (Catalog == null) continue; // cannot re-fit without the catalog
                    if (!Catalog.TryGet(c.catalogItemId, out var item)) problem = DesignIssueKind.UnknownItem;
                    else
                    {
                        var fit = CatalogQueries.FitFor(Catalog, anchor, item);
                        if (!fit.Fits) problem = DesignIssueKind.NoLongerFits;
                        else { c.rotated90 = fit.Rotated90; c.yScale = fit.YScale; c.xScale = fit.XScale; c.zScale = fit.ZScale; s.anchorChoices[i] = c; }
                    }
                }
                if (problem.HasValue)
                {
                    s.anchorChoices.RemoveAt(i);
                    _issues.Add(new DesignIssue(c.anchorId, problem.Value, true));
                }
            }

            s.myZoneFinish = CheckFinish(s.myZoneFinish);
            s.outsideFinish = CheckFinish(s.outsideFinish);
            if (!IsKnown(KnownMoodIds, s.moodPresetId))
            {
                _issues.Add(new DesignIssue(s.moodPresetId, DesignIssueKind.UnknownMood, true));
                s.moodPresetId = "";
            }

            for (int i = s.hideBoxes.Count - 1; i >= 0; i--)
            {
                var box = s.hideBoxes[i];
                var status = HideRules.CheckBox(_room, s, box);
                if (status != HideStatus.Ok)
                {
                    s.hideBoxes.RemoveAt(i);
                    _issues.Add(new DesignIssue(box.hideBoxId, DesignIssueKind.HideNotAllowed, true, hide: status));
                }
                else if (!IsKnown(KnownFinishIds, box.finishId))
                {
                    _issues.Add(new DesignIssue(box.finishId, DesignIssueKind.UnknownFinish, true));
                    box.finishId = "";
                    s.hideBoxes[i] = box;
                }
            }

            // Keep pieces in order; a piece that clashes with an earlier kept one is dropped.
            if (Catalog == null) return;
            for (int i = s.coverProps.Count - 1; i >= 0; i--)
            {
                var status = HideRules.CheckProp(_room, s, Catalog, s.coverProps[i]);
                if (status == HideStatus.Ok) continue;
                _issues.Add(new DesignIssue(s.coverProps[i].placementId, DesignIssueKind.HideNotAllowed, true, hide: status));
                s.coverProps.RemoveAt(i);
            }
            var all = s.placements;
            s.placements = new List<FreePlacement>(all.Count);
            foreach (var p in all)
            {
                var status = PlacementRules.Check(_room, s, Catalog, p);
                if (status == PlacementStatus.Ok) s.placements.Add(p);
                else _issues.Add(new DesignIssue(p.placementId, DesignIssueKind.PlacementNotAllowed, true, status));
            }
            if (_issues.Count > 0) Log?.Invoke(IssuesLine());
        }

        ZoneFinish CheckFinish(ZoneFinish f)
        {
            if (!IsKnown(KnownFinishIds, f.floorFinishId))
            {
                _issues.Add(new DesignIssue(f.floorFinishId, DesignIssueKind.UnknownFinish, true));
                f.floorFinishId = "";
            }
            if (!IsKnown(KnownFinishIds, f.wallFinishId))
            {
                _issues.Add(new DesignIssue(f.wallFinishId, DesignIssueKind.UnknownFinish, true));
                f.wallFinishId = "";
            }
            return f;
        }

        static bool IsKnown(ICollection<string> known, string id) =>
            known == null || string.IsNullOrEmpty(id) || known.Contains(id);

        bool Step(List<DesignState> from, List<DesignState> to)
        {
            if (from.Count == 0) return false;
            to.Add(State);
            State = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Changed?.Invoke();
            return true;
        }

        FurnitureAnchor FindAnchor(string anchorId)
        {
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                if (furniture[i].Id == anchorId) return furniture[i];
            }
            return null;
        }

        int IndexOf(string placementId) => State.placements.FindIndex(p => p.placementId == placementId);

        static bool Swap(ref string field, string value)
        {
            value = value ?? "";
            if (field == value) return false;
            field = value;
            return true;
        }
    }
}

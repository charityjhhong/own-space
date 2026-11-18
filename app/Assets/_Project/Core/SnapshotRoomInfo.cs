using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>A rigid move on the floor: turn by <see cref="YawDegrees"/> around Y (Unity's convention), then move by <see cref="Translation"/>.</summary>
    public readonly struct RoomPose
    {
        public RoomPose(float yawDegrees, Vec3 translation) { YawDegrees = yawDegrees; Translation = translation; }

        public float YawDegrees { get; }
        public Vec3 Translation { get; }

        public Vec3 Apply(Vec3 p)
        {
            var f = FloorGeometry.Rotate(new Vec2(p.x, p.z), YawDegrees);
            return new Vec3(f.x + Translation.x, p.y + Translation.y, f.y + Translation.z);
        }

        public Vec2 Apply(Vec2 p)
        {
            var f = FloorGeometry.Rotate(p, YawDegrees);
            return new Vec2(f.x + Translation.x, f.y + Translation.z);
        }

        public float ApplyYaw(float yawDegrees) => ((yawDegrees + YawDegrees) % 360f + 360f) % 360f;

        public OrientedBox Apply(OrientedBox b) => new OrientedBox(Apply(b.center), b.size, ApplyYaw(b.yawDegrees));

        /// <summary>
        /// The pose that puts the snapshot's anchor on <paramref name="worldPoint"/> (a real desk's centre, or a
        /// point in front of the user), facing <paramref name="worldYaw"/>, with its floor at <paramref name="worldFloorY"/>.
        /// </summary>
        public static RoomPose AnchorTo(RoomSnapshot snapshot, Vec3 worldPoint, float worldYaw, float worldFloorY)
        {
            float yaw = worldYaw - snapshot.anchorYaw;
            var turned = FloorGeometry.Rotate(snapshot.anchorPoint.Floor(), yaw);
            return new RoomPose(yaw, new Vec3(worldPoint.x - turned.x, worldFloorY - snapshot.floorY, worldPoint.z - turned.y));
        }

        /// <summary>
        /// The pose for a visit: the snapshot's desk or bed lands on the real piece of the same role nearest the user;
        /// with none, on the nearest real desk or bed of the other role; with neither (or a centre anchor), on
        /// <paramref name="userPoint"/> facing <paramref name="userYaw"/>. <paramref name="target"/> is the real piece
        /// used (build the small real zone around it), or null.
        /// </summary>
        public static RoomPose AlignTo(RoomSnapshot snapshot, IReadOnlyList<FurnitureAnchor> realPieces, float worldFloorY,
            Vec3 userPoint, float userYaw, out FurnitureAnchor target)
        {
            target = null;
            bool hasAnchor = snapshot.anchorRole == FurnitureRole.Table || snapshot.anchorRole == FurnitureRole.Bed;
            float best = float.MaxValue;
            bool bestSameRole = false;
            for (int i = 0; hasAnchor && realPieces != null && i < realPieces.Count; i++)
            {
                var p = realPieces[i];
                if (p == null || (p.Role != FurnitureRole.Table && p.Role != FurnitureRole.Bed)) continue;
                bool same = p.Role == snapshot.anchorRole;
                float dx = p.Box.center.x - userPoint.x, dz = p.Box.center.z - userPoint.z, d = dx * dx + dz * dz;
                if ((same && !bestSameRole) || (same == bestSameRole && d < best)) { target = p; best = d; bestSameRole = same; }
            }
            return target != null
                ? AnchorTo(snapshot, target.Box.center, target.Box.yawDegrees, worldFloorY)
                : AnchorTo(snapshot, userPoint, userYaw, worldFloorY);
        }
    }

    /// <summary>A saved room's furniture shown virtually: the design chosen there, or a default design when it was kept real.</summary>
    public readonly struct VirtualPiece
    {
        public VirtualPiece(FurnitureAnchor anchor, CatalogItem item, FitResult fit, bool chosen)
        {
            Anchor = anchor; Item = item; Fit = fit; Chosen = chosen;
        }

        /// <summary>The saved piece's box, in world space.</summary>
        public FurnitureAnchor Anchor { get; }
        /// <summary>The design to show, or null when the catalog has none for its role (show a plain box).</summary>
        public CatalogItem Item { get; }
        /// <summary>Turn and scales for <see cref="Item"/>, as for a redesign.</summary>
        public FitResult Fit { get; }
        /// <summary>True when the user chose this design in the saved room; false for a default.</summary>
        public bool Chosen { get; }
    }

    /// <summary>
    /// Plays a <see cref="RoomSnapshot"/> back as the room, moved by a <see cref="RoomPose"/> so its anchor lands
    /// where the user is now. Pure: built once, it never changes. In a visited space the whole saved room is
    /// virtual ("outside"); the session's zone is only the small real zone around the user's current desk.
    /// </summary>
    public sealed class SnapshotRoomInfo : IRoomInfo
    {
        readonly List<Vec2> _floor = new List<Vec2>();
        readonly List<RoomElement> _walls = new List<RoomElement>(), _windows = new List<RoomElement>(), _doors = new List<RoomElement>();
        readonly List<FurnitureAnchor> _furniture = new List<FurnitureAnchor>();

        public SnapshotRoomInfo(RoomSnapshot snapshot, RoomPose pose)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Pose = pose;
            foreach (var p in snapshot.floor) _floor.Add(pose.Apply(p));
            foreach (var e in snapshot.walls) _walls.Add(new RoomElement(e.id, pose.Apply(e.box)));
            foreach (var e in snapshot.windows) _windows.Add(new RoomElement(e.id, pose.Apply(e.box)));
            foreach (var e in snapshot.doors) _doors.Add(new RoomElement(e.id, pose.Apply(e.box)));
            foreach (var f in snapshot.furniture)
                _furniture.Add(new FurnitureAnchor(f.id, f.role, pose.Apply(f.box), f.usableHeight >= 0f ? f.usableHeight : (float?)null));
        }

        public RoomSnapshot Snapshot { get; }
        public RoomPose Pose { get; }

        public bool HasScan => true;
        public IReadOnlyList<Vec2> FloorOutline => _floor;
        public float FloorY => Snapshot.floorY + Pose.Translation.y;
        public float CeilingY => Snapshot.ceilingY + Pose.Translation.y;
        public IReadOnlyList<RoomElement> Walls => _walls;
        public IReadOnlyList<RoomElement> Windows => _windows;
        public IReadOnlyList<RoomElement> Doors => _doors;
        public IReadOnlyList<FurnitureAnchor> Furniture => _furniture;

        // A snapshot never changes; explicit accessors avoid an unused-event warning.
        public event Action Changed { add { } remove { } }

        /// <summary>
        /// The design to start a visit with, in world space: the saved outside pieces, the saved outside
        /// finishes (or my zone's, when the outside was kept real) and the mood. No zone (the caller sets the
        /// real zone around the user's desk), no redesign choices (the saved furniture is all virtual, see
        /// <see cref="VirtualFurniture"/>) and no hide boxes or cover props (they hid clutter that is not here).
        /// </summary>
        public DesignState VisitState()
        {
            var saved = Snapshot.design ?? new DesignState();
            var s = new DesignState { moodPresetId = saved.moodPresetId ?? "" };
            bool outsideReal = string.IsNullOrEmpty(saved.outsideFinish.floorFinishId) && string.IsNullOrEmpty(saved.outsideFinish.wallFinishId);
            s.outsideFinish = outsideReal ? saved.myZoneFinish : saved.outsideFinish;
            foreach (var p in saved.placements)
            {
                var moved = p;
                moved.position = Pose.Apply(p.position);
                moved.yawDegrees = Pose.ApplyYaw(p.yawDegrees);
                s.placements.Add(moved);
            }
            return s;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with every saved furniture piece outside <paramref name="realZone"/> (the
        /// real desk area, whose real furniture stays): the design chosen in the saved room, or the first design
        /// that fits (stretched if need be) when it was kept real and seen there (<see cref="SeenInSavedRoom"/>). Call when the visit starts, not every frame.
        /// </summary>
        /// <summary>
        /// Whether a saved piece that was kept real was seen in the saved room (pure, for tests): only inside its zone, or
        /// anywhere when the outside was kept real. Outside a virtual shell it was hidden, and pieces were placed over it
        /// (the human: a visit showed those as default sofas and beds, overlapping the design).
        /// </summary>
        public static bool SeenInSavedRoom(bool outsideWasReal, bool inSavedZone) => outsideWasReal || inSavedZone;

        /// <summary>
        /// The saved redesigns of pieces inside <paramref name="realZone"/> (the real desk or bed the visit lands on): the
        /// visit's session draws them over the real piece, as in the saved room. Call when the visit starts.
        /// </summary>
        public void RedesignsInZone(IReadOnlyList<Vec2> realZone, List<AnchorChoice> into)
        {
            var saved = Snapshot.design ?? new DesignState();
            foreach (var anchor in _furniture)
            {
                if (realZone == null || !Zones.Contains(realZone, anchor)) continue;
                var choice = saved.ChoiceFor(anchor.Id);
                if (choice.mode == AnchorMode.Redesign) into.Add(choice);
            }
        }

        public void VirtualFurniture(ICatalog catalog, IReadOnlyList<Vec2> realZone, List<VirtualPiece> into)
        {
            into.Clear();
            var saved = Snapshot.design ?? new DesignState();
            bool outsideReal = string.IsNullOrEmpty(saved.outsideFinish.floorFinishId) && string.IsNullOrEmpty(saved.outsideFinish.wallFinishId);
            var savedZone = new List<Vec2>();
            foreach (var v in saved.zoneOutline) savedZone.Add(Pose.Apply(v));
            var fits = new List<(CatalogItem item, FitResult fit)>();
            foreach (var anchor in _furniture)
            {
                if (realZone != null && Zones.Contains(realZone, anchor)) continue;
                var choice = saved.ChoiceFor(anchor.Id);
                if (choice.mode == AnchorMode.Redesign && catalog != null && catalog.TryGet(choice.catalogItemId ?? "", out var item))
                {
                    into.Add(new VirtualPiece(anchor, item,
                        new FitResult(FitStatus.Fits, choice.rotated90, choice.yScale, choice.xScale, choice.zScale), true));
                    continue;
                }
                if (!SeenInSavedRoom(outsideReal, savedZone.Count >= 3 && Zones.Contains(savedZone, anchor))) continue;
                CatalogQueries.FittingItems(catalog, anchor, fits);
                into.Add(fits.Count > 0 ? new VirtualPiece(anchor, fits[0].item, fits[0].fit, false) : new VirtualPiece(anchor, null, default, false));
            }
        }
    }
}

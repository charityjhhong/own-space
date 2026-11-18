using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    public enum HideStatus
    {
        Ok,
        DuplicateId,
        InvalidSize,
        TooLarge,
        OutsideMyZone,
        OutsideRoom,
        CoversOpening,
        CoversUsableSurface,
        UnknownProp,
        NotACoverProp,
    }

    /// <summary>
    /// Hiding clutter in my zone (CONCEPT 5.5): a hand-drawn <see cref="HideBox"/> or a cover prop
    /// (catalog kind "clutter-cover") standing on the floor. Either one must: have a sane size (each
    /// side at most <see cref="MaxSide"/>); stand in my zone and on the room floor; never cover a
    /// window or door (footprints within <see cref="OpeningMargin"/> and heights overlapping); and
    /// never cover a usable surface (desk top, bed) — it must stay below that surface wherever their
    /// footprints overlap, so the laptop on the desk is never hidden. Redesigned anchors count too:
    /// their virtual surface sits at the same height.
    /// Allocation-free and main-thread only (shared scratch buffers, as in <see cref="PlacementRules"/>).
    /// </summary>
    public static class HideRules
    {
        public const float MaxSide = 2.0f;
        public const float OpeningMargin = 0.05f;
        public const string CoverPropKind = "clutter-cover";

        // Scratch buffers: Shape holds the box being checked for the whole call; Other is
        // rewritten for each opening or anchor and read right away.
        static readonly Vec2[] Shape = new Vec2[4];
        static readonly Vec2[] Other = new Vec2[4];

        public static HideStatus CheckBox(IRoomInfo room, DesignState state, HideBox box) =>
            CheckVolume(room, state, box.position, box.size, box.yawDegrees);

        public static HideStatus CheckProp(IRoomInfo room, DesignState state, ICatalog catalog, FreePlacement prop)
        {
            if (catalog == null || !catalog.TryGet(prop.catalogItemId, out var item)) return HideStatus.UnknownProp;
            if (item.kind != CoverPropKind || !item.AllowedIn("my")) return HideStatus.NotACoverProp;
            return CheckVolume(room, state, prop.position, item.size, prop.yawDegrees);
        }

        static HideStatus CheckVolume(IRoomInfo room, DesignState state, Vec3 pos, Vec3 size, float yaw)
        {
            if (!Finite(pos.x) || !Finite(pos.y) || !Finite(pos.z) || !Finite(yaw) ||
                !Positive(size.x) || !Positive(size.y) || !Positive(size.z))
                return HideStatus.InvalidSize;
            if (size.x > MaxSide || size.y > MaxSide || size.z > MaxSide) return HideStatus.TooLarge;
            if (state == null || !Zones.Contains(state.zoneOutline, pos)) return HideStatus.OutsideMyZone;
            if (room == null) return HideStatus.Ok;

            FloorGeometry.Footprint(pos.Floor(), yaw, size.x, size.z, Shape);
            if (room.FloorOutline.Count >= 3 && !FloorGeometry.InsideArea(room.FloorOutline, Shape))
                return HideStatus.OutsideRoom;

            float bottom = pos.y, top = pos.y + size.y;
            if (Covers(room.Doors, bottom, top) || Covers(room.Windows, bottom, top)) return HideStatus.CoversOpening;

            var furniture = room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
            {
                var anchor = furniture[i];
                if (!FitLimits.HasUsableSurface(anchor.Role)) continue;
                var b = anchor.Box;
                float surface = anchor.UsableHeight.HasValue
                    ? room.FloorY + anchor.UsableHeight.Value
                    : b.center.y + b.size.y / 2f;
                if (top <= surface - FitLimits.Tolerance) continue;
                FloorGeometry.Footprint(b.center.Floor(), b.yawDegrees, b.size.x, b.size.z, Other);
                if (FloorGeometry.RectsOverlap(Shape, Other)) return HideStatus.CoversUsableSurface;
            }
            return HideStatus.Ok;
        }

        static bool Covers(System.Collections.Generic.IReadOnlyList<RoomElement> openings, float bottom, float top)
        {
            for (int i = 0; i < openings.Count; i++)
            {
                var b = openings[i].Box;
                if (top <= b.center.y - b.size.y / 2f || bottom >= b.center.y + b.size.y / 2f) continue;
                FloorGeometry.Footprint(b.center.Floor(), b.yawDegrees,
                    b.size.x + 2f * OpeningMargin, b.size.z + 2f * OpeningMargin, Other);
                if (FloorGeometry.RectsOverlap(Shape, Other)) return true;
            }
            return false;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Positive(float v) => Finite(v) && v > 0f;
    }
}

using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.Core
{
    /// <summary>
    /// Turns a floor piece's front toward the user. A catalog piece's front faces its local +Z
    /// (docs/design/catalog-schema.md), and yaw 0 faces world +Z, so yaw = atan2(dx, dz). Pure and
    /// allocation-free: MR's Add can call <see cref="TowardUser(Vec3, Vec3, float)"/> as the piece is placed.
    /// </summary>
    public static class Facing
    {
        /// <summary>Facing yaws are snapped to this step, in degrees.</summary>
        public const float SnapDegrees = 15f;

        /// <summary>
        /// The yaw (degrees, 0..360, snapped to <see cref="SnapDegrees"/>) that turns a piece at
        /// <paramref name="position"/> to face <paramref name="user"/> on the floor. <paramref name="fallback"/>
        /// when the two are on the same spot (under 1 cm apart).
        /// </summary>
        public static float TowardUser(Vec3 position, Vec3 user, float fallback = 0f) =>
            TowardUser(position.Floor(), user.Floor(), fallback);

        /// <param name="step">The snap step; tables use 90 so the rug and seats around them stay square to the room.</param>
        public static float TowardUser(Vec2 at, Vec2 user, float fallback = 0f, float step = SnapDegrees)
        {
            float dx = user.x - at.x, dz = user.y - at.y;
            if (dx * dx + dz * dz < 1e-4f) return fallback;
            double yaw = Math.Atan2(dx, dz) * 180.0 / Math.PI;
            float snapped = (float)(Math.Round(yaw / step) * step) % 360f;
            return snapped < 0f ? snapped + 360f : snapped;
        }

        /// <summary>Whether a piece at <paramref name="at"/> turned to <paramref name="yawDegrees"/> faces the user within 90°.</summary>
        public static bool RoughlyToward(float yawDegrees, Vec2 at, Vec2 user)
        {
            float dx = user.x - at.x, dz = user.y - at.y;
            if (dx * dx + dz * dz < 1e-4f) return true;
            var f = FloorGeometry.Rotate(new Vec2(0f, 1f), yawDegrees);
            return f.x * dx + f.y * dz >= -1e-4f;
        }

        /// <summary>
        /// Where the user sits, on the floor: the head when known, else the seat in front of the desk (or the bed)
        /// in my zone (as <see cref="ViewComfort"/>), else my zone's centre. Null with none of these.
        /// </summary>
        public static Vec2? UserSpot(IRoomInfo room, IReadOnlyList<Vec2> zone, Vec3? userPosition)
        {
            var view = ViewComfort.For(room, zone, userPosition);
            if (view != null) return view.Head;
            if (zone == null || zone.Count < 3) return null;
            float x = 0f, z = 0f;
            for (int i = 0; i < zone.Count; i++) { x += zone[i].x; z += zone[i].y; }
            return new Vec2(x / zone.Count, z / zone.Count);
        }

        /// <summary>
        /// Whether a piece has a front to turn: not a rug or a plant (round, and turning one breaks its corner fit),
        /// nor a hanging, wall or window (curtain, blind) piece.
        /// </summary>
        public static bool HasFront(CatalogItem item) =>
            item != null && !PlacementRules.IsHanging(item) && !PlacementRules.IsWallMounted(item) &&
            item.size.y > PlacementRules.FlatHeight && item.kind != "rug" && item.kind != "plant" && item.kind != "curtain" && item.kind != "blind";
    }
}

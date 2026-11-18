using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The parent of a row of tiles and buttons within reach in front of the user. It is placed
    /// in front of the head when it opens, and placed there again whenever it leaves the field of
    /// view. It never ends up beyond a wall: it is pulled toward the user until the whole row is
    /// on the room floor. Children use local positions: +x runs right, and +z faces away from the user.
    /// Allocation-free.
    /// </summary>
    public sealed class FloatingRow
    {
        const float Reach = 0.42f, MinReach = 0.22f, Drop = 0.18f, WallGap = 0.06f;
        // Re-place when the row centre is this far (degrees) from the view direction.
        const float MaxAngle = 40f;

        public readonly Transform Root;
        readonly float _halfWidth;

        public FloatingRow(Transform parent, string name, float halfWidth)
        {
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);
            _halfWidth = halfWidth;
        }

        /// <summary>Places the row in front of <paramref name="view"/> (the head ray), inside the room.</summary>
        public void Place(Ray view, IReadOnlyList<Vec2> outline)
        {
            Vector3 forward = Vector3.ProjectOnPlane(view.direction, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            float reach = ReachInside(view.origin, forward, _halfWidth, outline);
            Root.SetPositionAndRotation(view.origin + forward * reach + Vector3.down * Drop, Quaternion.LookRotation(forward, Vector3.up));
        }

        /// <summary>Call every frame while the row is shown: re-places it once it leaves the view.</summary>
        public void Follow(Ray view, IReadOnlyList<Vec2> outline)
        {
            if (Vector3.Angle(view.direction, Root.position - view.origin) > MaxAngle) Place(view, outline);
        }

        /// <summary>
        /// The longest reach (from <see cref="Reach"/> down to <see cref="MinReach"/>) at which both
        /// ends of a row, <paramref name="halfWidth"/> to either side, stay at least WallGap inside the
        /// floor outline. Reach is used when there is no outline.
        /// </summary>
        public static float ReachInside(Vector3 head, Vector3 forward, float halfWidth, IReadOnlyList<Vec2> outline) =>
            ReachInside(head, forward, halfWidth, outline, Reach);

        /// <summary>As above, starting from <paramref name="maxReach"/> instead of the row's reach.</summary>
        public static float ReachInside(Vector3 head, Vector3 forward, float halfWidth, IReadOnlyList<Vec2> outline, float maxReach)
        {
            if (outline == null || outline.Count < 3) return maxReach;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            for (float reach = maxReach; reach > MinReach; reach -= 0.04f)
            {
                Vector3 c = head + forward * (reach + WallGap);
                if (Inside(outline, c) && Inside(outline, c + right * (halfWidth + WallGap)) &&
                    Inside(outline, c - right * (halfWidth + WallGap)))
                {
                    return reach;
                }
            }
            return MinReach;
        }

        static bool Inside(IReadOnlyList<Vec2> outline, Vector3 p) => Zones.Contains(outline, new Vec2(p.x, p.z));
    }
}

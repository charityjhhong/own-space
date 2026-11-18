using System.Collections.Generic;
using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Keeps a tool (the style orb, the hide cube) within reach: once it leaves a cone around the
    /// view or gets too far from the head, it eases back over <see cref="EaseSeconds"/> to its seat.
    /// The seat is 35 cm ahead, <c>side</c> metres right (negative: left) and 30 cm below the eyes,
    /// pulled toward the user so it never sits beyond a wall (<see cref="FloatingRow.ReachInside(Vector3, Vector3, float, IReadOnlyList{Vec2}, float)"/>).
    /// Allocation-free.
    /// </summary>
    public sealed class ToolFollow
    {
        public const float Ahead = 0.35f, Below = 0.3f, HalfCone = 35f, MaxDistance = 0.8f, EaseSeconds = 0.4f;

        readonly float _side;
        Vector3 _from, _to;
        float _t = 1f;

        public ToolFollow(float side) => _side = side;

        public bool Moving => _t < 1f;

        /// <summary>Puts <paramref name="tool"/> on its seat at once (when it first appears).</summary>
        public void Snap(Transform tool, Ray view, IReadOnlyList<Vec2> outline)
        {
            _t = 1f;
            tool.position = Seat(view.origin, view.direction, _side, outline);
        }

        /// <summary>
        /// Call every frame. While <paramref name="allowed"/> is false (a menu is open, a drag is
        /// running) the tool stays put and any ease stops. True when the tool moved this frame.
        /// </summary>
        public bool Tick(Transform tool, Ray view, IReadOnlyList<Vec2> outline, bool allowed, float deltaTime)
        {
            if (!allowed) { _t = 1f; return false; }
            if (!Moving)
            {
                if (!NeedsReturn(view.origin, view.direction, tool.position)) return false;
                _from = tool.position;
                _to = Seat(view.origin, view.direction, _side, outline);
                _t = 0f;
            }
            _t = Mathf.Min(1f, _t + deltaTime / EaseSeconds);
            tool.position = Vector3.Lerp(_from, _to, Mathf.SmoothStep(0f, 1f, _t));
            return true;
        }

        /// <summary>
        /// True when the tool is more than <see cref="HalfCone"/> to either side of the view (measured
        /// on the floor plane: the seat itself is ~46° below the view axis, and looking down at it must
        /// not move it) or more than <see cref="MaxDistance"/> from the head.
        /// </summary>
        public static bool NeedsReturn(Vector3 head, Vector3 viewDirection, Vector3 tool)
        {
            Vector3 to = tool - head;
            if (to.sqrMagnitude > MaxDistance * MaxDistance) return true;
            Vector3 flatView = Vector3.ProjectOnPlane(viewDirection, Vector3.up), flatTo = Vector3.ProjectOnPlane(to, Vector3.up);
            if (flatView.sqrMagnitude < 1e-6f || flatTo.sqrMagnitude < 1e-6f) return false; // looking straight down or up
            return Vector3.Angle(flatView, flatTo) > HalfCone;
        }

        /// <summary>The seat in front of the head, inside the room.</summary>
        public static Vector3 Seat(Vector3 head, Vector3 viewDirection, float side, IReadOnlyList<Vec2> outline)
        {
            Vector3 forward = Vector3.ProjectOnPlane(viewDirection, Vector3.up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float reach = FloatingRow.ReachInside(head, forward, Mathf.Abs(side), outline, Ahead);
            return head + forward * reach + right * side + Vector3.down * Below;
        }
    }
}

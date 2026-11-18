using System.Diagnostics;
using UnityEngine;

namespace OwnSpace.Content
{
    /// <summary>
    /// A curtain or door trim built from parts that the app sizes to a real window or door, so it never covers the
    /// opening whatever its size. Placement: put the root at the bottom center of the real opening, on the wall
    /// plane, +Z facing into the room, then call <see cref="Fit"/> once with the opening's width and height (meters).
    /// Parts are unit cubes centered on the root's z axis; the outside parts keep their own width and depth.
    /// <list type="bullet">
    /// <item>Curtain: <c>Left</c> and <c>Right</c> panels beside the opening, from 0.10 m below the opening to 0.12 m above it, and a <c>Rod</c> across the top.</item>
    /// <item>Trim: <c>Left</c> and <c>Right</c> bars beside the opening (full opening height) and a <c>Top</c> bar above it.</item>
    /// </list>
    /// </summary>
    public sealed class OpeningFrame : MonoBehaviour
    {
        public enum Mode { Curtain, Trim }

        public const float CurtainBelow = 0.10f;
        public const float CurtainAbove = 0.12f;
        public const float RodThickness = 0.02f;

        [SerializeField, Tooltip("Curtain or trim.")] Mode _mode;
        [SerializeField, Tooltip("Unit-cube part left of the opening.")] Transform _left;
        [SerializeField, Tooltip("Unit-cube part right of the opening.")] Transform _right;
        [SerializeField, Tooltip("Rod (curtain) or top bar (trim).")] Transform _top;
        [SerializeField, Tooltip("Width of one side part, outside the opening (m).")] float _sideWidth = 0.1f;
        [SerializeField, Tooltip("Thickness of the parts along z (m).")] float _depth = 0.06f;

        public Mode FrameMode => _mode;

        /// <summary>Wires the parts. Used by the editor script; the prefab then stores it.</summary>
        public void Configure(Mode mode, Transform left, Transform right, Transform top, float sideWidth, float depth)
        {
            _mode = mode; _left = left; _right = right; _top = top; _sideWidth = sideWidth; _depth = depth;
        }

        /// <summary>Sizes the parts around an opening of the given width and height. No allocation; call on placement, not per frame.</summary>
        public void Fit(float openingWidth, float openingHeight, string openingId = null)
        {
            float w = Mathf.Max(0.1f, openingWidth), h = Mathf.Max(0.1f, openingHeight);
            float sideX = w / 2f + _sideWidth / 2f;
            if (_mode == Mode.Curtain)
            {
                float panelH = h + CurtainBelow + CurtainAbove;
                float panelY = (h + CurtainAbove - CurtainBelow) / 2f;
                Place(_left, -sideX, panelY, _sideWidth, panelH);
                Place(_right, sideX, panelY, _sideWidth, panelH);
                Place(_top, 0f, h + CurtainAbove - RodThickness / 2f, w + 2f * _sideWidth, RodThickness, RodThickness);
            }
            else
            {
                Place(_left, -sideX, h / 2f, _sideWidth, h);
                Place(_right, sideX, h / 2f, _sideWidth, h);
                Place(_top, 0f, h + _sideWidth / 2f, w + 2f * _sideWidth, _sideWidth);
            }
            Trace(openingId, openingWidth, openingHeight);
        }

        /// <summary>Editor-only: one line per fitted opening for diagnosing placement from a Play run. Compiled out of players.</summary>
        [Conditional("UNITY_EDITOR")]
        void Trace(string openingId, float inW, float inH)
        {
            Transform t = transform;
            UnityEngine.Debug.Log($"[OwnSpace.Content] frame: id={openingId ?? "?"} label={(_mode == Mode.Curtain ? "window" : "door")} " +
                $"worldPos(opening bottom)={t.position:F3} right={t.right:F3} up={t.up:F3} fwd={t.forward:F3} lossyScale={t.lossyScale:F3} " +
                $"opening={inW:F3}x{inH:F3} pieces: {Describe(_left)} {Describe(_right)} {Describe(_top)}", this);
        }

        static string Describe(Transform p) =>
            p == null ? "null" : $"{p.name}[local {p.localPosition:F3} size {p.localScale:F3} world {p.position:F3}]";

        void Place(Transform part, float x, float y, float width, float height, float depth = -1f)
        {
            if (part == null) return;
            part.localPosition = new Vector3(x, y, 0f);
            part.localScale = new Vector3(width, height, depth < 0f ? _depth : depth);
        }
    }
}

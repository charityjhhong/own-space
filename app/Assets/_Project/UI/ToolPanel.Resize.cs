using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// The resize grip (device day): two short diagonal bars at the panel's bottom-right corner, in the top handle's
    /// style. Pinch (near or by ray) and drag to scale the whole panel uniformly between <see cref="MinScale"/> and
    /// <see cref="MaxScale"/>; the top-left corner stays put. Double-pinch the grip to go back to 1.0x. The scale is
    /// raised through <see cref="ScaleChanged"/> on release so the host can save it (UI prefs).
    /// </summary>
    public sealed partial class ToolPanel
    {
        internal const int Resize = Back + 11;
        const float GripZone = 0.032f, GripInset = 0.013f;

        PanelElement _gripLong, _gripShort;
        bool _resizing, _resizeNear, _resizeMoved;
        float _scaleStart, _startDistance, _lastGripTap = -10f;
        Vector3 _anchor, _resizeGrab;

        void BuildGrip()
        {
            _gripLong = new PanelElement(transform, _material, _style, _fonts, "ResizeGrip", 13f);
            _gripShort = new PanelElement(transform, _material, _style, _fonts, "ResizeGripShort", 13f);
        }

        /// <summary>
        /// Space under the last row: the usual padding, or enough to clear the grip when the last row has a control under
        /// it (device shot 181928: the grip sat on the Real occlusion switch). Pure, for tests.
        /// </summary>
        public static float BottomPaddingFor(float lastRowRight, float panelWidth, float padding) =>
            lastRowRight > panelWidth * 0.5f - GripInset - GripZone * 0.5f ? Mathf.Max(padding, GripInset + GripZone * 0.5f + 0.002f) : padding;

        float BottomPadding(float lastRowRight) => BottomPaddingFor(lastRowRight, _metrics.Size.x, _metrics.Padding);

        Vector2 GripCentre => new Vector2(_metrics.Size.x * 0.5f - GripInset, Top - _height + GripInset);

        // Two bars across the corner (like the handle bar), the short one nearer the corner.
        void PlaceGrip(bool shown)
        {
            if (_gripLong == null) return;
            _gripLong.SetVisible(shown);
            _gripShort.SetVisible(shown);
            if (!shown) return;
            Vector2 c = GripCentre;
            _gripLong.Place(c + new Vector2(-0.002f, 0.002f), new Vector2(0.016f, 0.0028f), 0.0014f);
            _gripShort.Place(c + new Vector2(0.003f, -0.003f), new Vector2(0.008f, 0.0028f), 0.0014f);
            Quaternion diagonal = Quaternion.Euler(0f, 0f, 45f);
            _gripLong.Root.localRotation = diagonal;
            _gripShort.Root.localRotation = diagonal;
        }

        bool OnGrip(Vector2 p, float pad) => _gripLong != null && _gripLong.Visible && PanelLayout.Contains(GripCentre, new Vector2(GripZone, GripZone), p, pad);

        // The hand's point on the panel plane: its pinch point when it is at the panel, else where its ray meets the plane.
        Vector3 HandOnPanel(HandState hand)
        {
            if (_resizeNear) return hand.PinchPoint;
            Ray aim = _hands.Aim(_pressLeft);
            return PanelLayout.RayToPanel(aim, transform.position, transform.rotation, out _, out float d) ? aim.GetPoint(d) : hand.PinchPoint;
        }

        void BeginResize(HandState hand)
        {
            float z = ToLocal(hand.PinchPoint).z;
            _resizeNear = hand.Tracked && z >= -NearGrabDepth && z <= 0.01f;
            _resizing = true;
            _resizeMoved = false;
            _scaleStart = Scale;
            _anchor = TopLeft();
            _resizeGrab = HandOnPanel(hand);
            _startDistance = Mathf.Max(0.02f, Vector3.Distance(_resizeGrab, _anchor));
            _ease = 1f;
        }

        Vector3 TopLeft() => transform.TransformPoint(new Vector3(-_metrics.Size.x * 0.5f, Top, 0f));

        /// <summary>The scale for a grip moved from <paramref name="startDistance"/> to <paramref name="distance"/> from the fixed corner (pure, for tests).</summary>
        public static float ResizeScale(float startScale, float startDistance, float distance) =>
            ClampScale(startScale * distance / Mathf.Max(1e-4f, startDistance));

        void TrackResize(HandState owner)
        {
            Vector3 p = HandOnPanel(owner);
            if (!_resizeMoved && (p - _resizeGrab).sqrMagnitude > DragThreshold * DragThreshold) _resizeMoved = true;
            if (_resizeMoved) ScaleAbout(_anchor, ResizeScale(_scaleStart, _startDistance, Vector3.Distance(p, _anchor)));
            if (owner.PinchEnded) Release(_pressed);
        }

        // Scales the panel keeping a world corner fixed.
        void ScaleAbout(Vector3 corner, float scale)
        {
            SetScale(scale);
            transform.position = corner - transform.rotation * (new Vector3(-_metrics.Size.x * 0.5f, Top, 0f) * Scale);
            Placed = true;
        }

        void StopResize() => _resizing = false;

        void ReleaseResize()
        {
            bool moved = _resizing && _resizeMoved;
            StopResize();
            if (moved) { LogMove("the user's resize"); KeepAboveSurfaces("the resize (desk clamp)"); NotifyScale(); return; }
            bool second = Time.time - _lastGripTap < DoublePinchSeconds;
            _lastGripTap = second ? -10f : Time.time;
            if (!second) return;
            ScaleAbout(TopLeft(), 1f); // double-pinch: back to 1.0x
            NotifyScale();
        }
    }
}

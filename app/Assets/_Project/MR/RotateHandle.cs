using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Turning a piece in place (device batch 10: no Rotate button on the panel).
    /// <list type="bullet">
    /// <item>When the ray or hand comes near the bottom front-right corner of a piece's outline box (seen from the user),
    /// a round glass handle with a curved-arrow glyph appears there. At rest it is small and quiet (<see cref="Size"/>, a
    /// light see-through disc with a thin blue arrow); hovered or turning it is lit (the theme's dark pill, a white arrow) and
    /// <see cref="LitScale"/> larger, so the two read apart at a glance (device shot 20261112-140141: too big and heavy).</item>
    /// <item>Pinch-dragging it turns the piece around its centre, snapping every <see cref="SnapDegrees"/>. A glass chip above
    /// shows the angle while turning.</item>
    /// </list>
    /// Placed pieces (OutsidePlacer) and redesigned real pieces (EditMode) share it. The geometry is pure, for tests.
    /// Show and the drag do not allocate; the angle text is built only when the snapped angle changes.
    /// </summary>
    public sealed class RotateHandle
    {
        public const float HitRadius = 0.12f, HitDegrees = 4f, ShowRadius = 0.3f, SnapDegrees = 15f, Size = 0.045f, LitScale = 1.2f;
        /// <summary>The disc at rest: white, mostly see-through (the room shows through it).</summary>
        public static readonly Color RestFill = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color RestArrow = new Color(0.039f, 0.455f, 0.878f), LitArrow = Color.white;
        const int ArcPoints = 14;

        readonly Transform _root;
        readonly PanelElement _disc;
        readonly LineRenderer _arc;
        readonly GlassChip _hint;
        readonly Vector3 _hintOffset = new Vector3(0f, 0.065f, 0f);
        bool _lit, _litShown = true;
        int _shownAngle = int.MinValue;
        string _angleText;

        public RotateHandle(Transform parent, ToolPanel panel, Material lineMaterial)
        {
            _root = new GameObject("RotateHandle").transform;
            _root.SetParent(parent, false);
            _disc = new PanelElement(_root, panel.GlassMaterial, panel.CurrentStyle, panel.Fonts, "Disc", 13f);
            _disc.Place(Vector2.zero, new Vector2(Size, Size), Size * 0.5f);
            _arc = new GameObject("Arrow").AddComponent<LineRenderer>();
            _arc.transform.SetParent(_root, false);
            _arc.useWorldSpace = false;
            _arc.sharedMaterial = lineMaterial;
            _arc.widthMultiplier = 0.003f;
            _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arc.positionCount = ArcPoints + 3;
            // A 270° arc with an arrowhead at its end, just in front of the disc.
            float r = Size * 0.27f;
            for (int i = 0; i <= ArcPoints; i++)
            {
                float a = Mathf.Deg2Rad * (90f - 270f * i / ArcPoints);
                _arc.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -0.002f));
            }
            Vector3 end = _arc.GetPosition(ArcPoints);
            _arc.SetPosition(ArcPoints + 1, end + new Vector3(-r * 0.45f, r * 0.15f, 0f));
            _arc.SetPosition(ArcPoints + 2, end);
            // The angle chip is the handle's sibling, not its child: under the handle's own distance scale it was scaled twice
            // and read larger than every other in-room label. As a sibling it is the same chip, at the same size, as they are.
            _hint = new GlassChip(parent, panel, "Angle");
            _root.gameObject.SetActive(false);
        }

        public bool Visible => _root.gameObject.activeSelf;
        public bool Lit => _lit;
        internal float DiscSize => _disc.Size.x * _root.localScale.x;

        /// <summary>The handle's scale for a distance (m): about the same size in view, as the panel at 0.8 m.</summary>
        public static float ViewScale(float distance) => Mathf.Clamp(distance / 0.8f, 0.8f, 3f);

        /// <summary>Shows the handle at <paramref name="corner"/> facing the user (lit when hovered or turning).</summary>
        public void Show(Vector3 corner, Vector3 head, bool lit)
        {
            Vector3 away = corner - head;
            _root.SetPositionAndRotation(corner, away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away, Vector3.up) : _root.rotation);
            _root.localScale = Vector3.one * (ViewScale(away.magnitude) * (lit ? LitScale : 1f)); // about the same size in view
            _lit = lit;
            if (lit != _litShown)
            {
                // Lit: the theme's dark pill and a white arrow. At rest: a light see-through disc and a thin blue arrow.
                _litShown = lit;
                _disc.SetColors(lit ? Color.clear : RestFill, Color.clear);
                _disc.SetState(lit ? ElementState.Selected : ElementState.Normal);
                XrVisuals.Tint(_arc, lit ? LitArrow : RestArrow);
            }
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        /// <summary>The live angle above the handle while turning ("135°").</summary>
        public void ShowAngle(float yawDegrees, Vector3 head)
        {
            int angle = Mathf.RoundToInt(Mathf.Repeat(yawDegrees, 360f)) % 360;
            if (angle != _shownAngle) { _shownAngle = angle; _angleText = angle + "°"; }
            _hint.Show(_angleText, _root.position + _hintOffset * _root.localScale.x, head);
        }

        public void Hide()
        {
            _hint.Hide();
            _shownAngle = int.MinValue;
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        // ---- pure geometry (tests) ----

        /// <summary>
        /// The bottom corner the handle sits on: of the box's two corners nearest the user, the one on their right.
        /// The box is centred at <paramref name="centre"/> (floor x/z), <paramref name="size"/> in its own axes, turned
        /// <paramref name="yawDegrees"/>.
        /// </summary>
        public static Vector3 FrontRightCorner(Vector3 centre, Vector3 size, float yawDegrees, float floorY, Vector3 head)
        {
            Quaternion turn = Quaternion.Euler(0f, yawDegrees, 0f);
            Vector3 toUser = new Vector3(head.x - centre.x, 0f, head.z - centre.z);
            Vector3 right = Vector3.Cross(Vector3.up, -toUser); // the user's right, looking at the piece
            Vector3 best = centre, second = centre;
            float bestD = float.MaxValue, secondD = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = centre + turn * new Vector3((i & 1) == 0 ? -size.x * 0.5f : size.x * 0.5f, 0f, (i & 2) == 0 ? -size.z * 0.5f : size.z * 0.5f);
                float d = (c.x - head.x) * (c.x - head.x) + (c.z - head.z) * (c.z - head.z);
                if (d < bestD) { second = best; secondD = bestD; best = c; bestD = d; }
                else if (d < secondD) { second = c; secondD = d; }
            }
            Vector3 pick = Vector3.Dot(best - centre, right) >= Vector3.Dot(second - centre, right) ? best : second;
            return new Vector3(pick.x, floorY, pick.z);
        }

        /// <summary>How near the ray passes the corner: within <paramref name="radius"/> m, or within <see cref="HitDegrees"/>.</summary>
        public static bool Near(Ray ray, Vector3 corner, float radius)
        {
            Vector3 to = corner - ray.origin;
            float along = Vector3.Dot(to, ray.direction);
            if (along <= 0f) return false;
            float miss = (to - ray.direction * along).magnitude;
            return miss <= radius || Vector3.Angle(ray.direction, to) <= HitDegrees;
        }

        /// <summary>The nearest multiple of <see cref="SnapDegrees"/>.</summary>
        public static float Snap(float yawDegrees) => Mathf.Round(yawDegrees / SnapDegrees) * SnapDegrees;

        /// <summary>The turned yaw: the start yaw plus the angle the ray's floor point swept around the centre, snapped.</summary>
        public static float DragYaw(float startYaw, Vector3 centre, Vector3 from, Vector3 now)
        {
            Vector3 a = new Vector3(from.x - centre.x, 0f, from.z - centre.z), b = new Vector3(now.x - centre.x, 0f, now.z - centre.z);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return Snap(startYaw);
            return Snap(startYaw + Vector3.SignedAngle(a, b, Vector3.up));
        }
    }
}

using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The zone step's card, small and quiet (device batch 8): a grab bar, one instruction line, one line saying what
    /// the zone is, and its button(s) (Done; or Fit to desk / Keep anyway). Each line is its own single-line element, so
    /// lines never wrap or drift off their baseline. Built from the tool panel's elements, material, Inter fonts and
    /// theme. Layout and hit tests in the card's local metres; no allocation after it is built.
    /// </summary>
    public sealed class ZoneCard
    {
        // Mockups round 2 (zone-card.html): the panel's 16 mm padding; line 1 is a title (14 semibold, 22 mm line), line 2 a
        // caption (12 regular, 18 mm line, primary ink); 12 mm from the lines to the buttons; buttons 36 mm high, as wide
        // as their words + 24 mm and never under 96 mm, the main one always the right-most.
        public const float Width = 0.30f, Pad = 0.016f, Line1 = 0.022f, Line2 = 0.018f, ButtonHeight = 0.036f, Gap = 0.012f,
            ButtonGap = 0.008f, MinButton = 0.096f, ButtonPad = 0.024f;
        const float BarHeight = 0.006f, BarZone = 0.024f;

        /// <summary>Hit results besides the buttons (0, 1): the grab bar, and the card's body (claimed, does nothing).</summary>
        public const int Bar = 2, Body = 3;

        /// <summary>Whether <paramref name="point"/> is within <paramref name="maxDegrees"/> of the view direction (pure, for tests).</summary>
        public static bool InView(Ray view, Vector3 point, float maxDegrees) => Vector3.Angle(view.direction, point - view.origin) <= maxDegrees;

        readonly Transform _root;
        readonly PanelElement _back, _bar, _line1, _line2, _primary, _secondary;
        bool _two;
        float _top;

        public ZoneCard(Transform parent, ToolPanel panel)
        {
            _root = new GameObject("ZoneCard").transform;
            _root.SetParent(parent, false);
            Material glass = panel.GlassMaterial;
            PanelStyle style = panel.CurrentStyle;
            PanelFonts fonts = panel.Fonts;
            _back = new PanelElement(_root, glass, style, fonts, "Back", 13f, surface: true);
            _bar = new PanelElement(_root, glass, style, fonts, "Bar", 13f);
            _line1 = new PanelElement(_root, glass, style, fonts, "Text", PanelElement.TitleMm);
            _line2 = new PanelElement(_root, glass, style, fonts, "Text2", PanelElement.CaptionMm);
            _line1.SetKind(CardKind.Title);
            _line2.SetKind(CardKind.Title);
            _line1.SetTextStyle(PanelElement.TextStyle.Header);
            _line2.SetTextStyle(PanelElement.TextStyle.Caption);
            // The primary ink on both lines, not the secondary grey (device bug on a2c27d2: hard to read in a dim room).
            _line1.SetColors(Color.clear, style.Normal.Text);
            _line2.SetColors(Color.clear, style.Normal.Text);
            _primary = new PanelElement(_root, glass, style, fonts, "Primary", PanelElement.BodyMm);
            _secondary = new PanelElement(_root, glass, style, fonts, "Secondary", PanelElement.BodyMm);
            _root.gameObject.SetActive(false);
        }

        public bool Visible => _root.gameObject.activeSelf;
        public Vector3 Position => _root.position;

        /// <summary>
        /// Shows the card at <paramref name="at"/> facing <paramref name="head"/>: two lines, then one button or two. The main
        /// action sits on the right, or on the left with <paramref name="mainLeft"/>.
        /// </summary>
        public void Show(string line1, string line2, string primary, string secondary, Vector3 at, Vector3 head, bool mainLeft = false)
        {
            float height = Pad + BarZone + Line1 + Line2 + Gap + ButtonHeight + Pad;
            _top = height * 0.5f;
            _two = !string.IsNullOrEmpty(secondary);
            float inner = Width - 2f * Pad, y = _top - Pad;
            _back.Place(Vector2.zero, new Vector2(Width, height));
            _bar.Place(new Vector2(0f, y - BarZone * 0.5f), new Vector2(0.05f, BarHeight), BarHeight * 0.5f);
            y -= BarZone;
            _line1.SetText(line1);
            _line1.Place(new Vector2(0f, y - Line1 * 0.5f), new Vector2(inner, Line1));
            y -= Line1;
            _line2.SetText(line2);
            _line2.Place(new Vector2(0f, y - Line2 * 0.5f), new Vector2(inner, Line2));
            float by = -_top + Pad + ButtonHeight * 0.5f, right = inner * 0.5f;
            _primary.SetText(primary);
            // Each button hugs its words; two that would not fit side by side share the width instead.
            float w = Mathf.Max(MinButton, _primary.PreferredWidth(primary) + ButtonPad);
            float w2 = _two ? Mathf.Max(MinButton, _secondary.PreferredWidth(secondary) + ButtonPad) : 0f;
            if (_two && w + w2 + ButtonGap > inner) { float fit = (inner - ButtonGap) / (w + w2); w *= fit; w2 *= fit; }
            _primary.SetState(ElementState.Selected); // the main action: the theme's dark pill
            // The main action is always on the right, where Done is, so a second pinch in Done's spot lands on the next
            // question's main button (device: Done became Fit / Keep and the next pinches hit nothing).
            // The human, on the zone's desk question: Fit to desk on the left, Keep anyway on the right (mainLeft).
            float mainX = _two && mainLeft ? right - w2 - ButtonGap - w * 0.5f : right - w * 0.5f;
            float otherX = mainLeft ? right - w2 * 0.5f : right - w - ButtonGap - w2 * 0.5f;
            _primary.Place(new Vector2(mainX, by), new Vector2(w, ButtonHeight), ButtonHeight * 0.5f);
            _secondary.SetVisible(_two);
            if (_two)
            {
                _secondary.SetText(secondary);
                _secondary.SetState(ElementState.Normal);
                _secondary.Place(new Vector2(otherX, by), new Vector2(w2, ButtonHeight), ButtonHeight * 0.5f);
            }
            MoveTo(at, head);
            _root.gameObject.SetActive(true);
        }

        /// <summary>Changes the two lines only (hover and miss notes); the card stays where it is.</summary>
        public void SetLines(string line1, string line2)
        {
            if (!Visible) return;
            _line1.SetText(line1);
            _line2.SetText(line2);
        }

        /// <summary>Where the main (right) button is, in the card's plane (for tests).</summary>
        public Vector2 PrimaryCentre => _primary.Centre;
        public Vector2 SecondaryCentre => _secondary.Centre;
        public Vector2 PrimarySize => _primary.Size;
        public Vector2 SecondarySize => _secondary.Size;

        /// <summary>Moves the card (a drag by its bar, or docking) facing <paramref name="head"/>.</summary>
        public void MoveTo(Vector3 at, Vector3 head)
        {
            Vector3 away = at - head;
            _root.SetPositionAndRotation(at, away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away, Vector3.up) : _root.rotation);
        }

        public void Hide() { if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); }

        /// <summary>What a hand touches (near, within 3 cm) or its ray points at: 0 or 1 for a button, <see cref="Bar"/>, or -1.</summary>
        public int Hit(Ray ray, Vector3 pinch, bool pinchValid)
        {
            if (!Visible) return -1;
            Vector2 p;
            // Near first, like the tool panel: a hand at the card presses what is under its pinch point, wherever the ray
            // points (device, build dfb22f4: fingers on Cancel, the ray past the card, every pinch "hit nothing").
            if (pinchValid && NearCard(pinch, NearFront, out Vector2 at)) p = at;
            else if (!PanelLayout.RayToPanel(ray, _root.position, _root.rotation, out p, out _)) return -1;
            if (PanelLayout.Contains(_primary.Centre, _primary.Size, p, 0.006f)) return 0;
            if (_two && PanelLayout.Contains(_secondary.Centre, _secondary.Size, p, 0.006f)) return 1;
            if (PanelLayout.Contains(new Vector2(0f, _top - Pad - BarZone * 0.5f), new Vector2(Width, BarZone + Pad), p, 0.004f)) return Bar;
            return PanelLayout.Contains(Vector2.zero, new Vector2(Width, _top * 2f), p, 0.01f) ? Body : -1;
        }

        /// <summary>A pinch point this far in front of the card (toward the user) or <see cref="NearBehind"/> behind it, over it, is a near press.</summary>
        public const float NearFront = 0.06f, NearBehind = 0.05f, NearMargin = 0.01f;

        /// <summary>
        /// Whether <paramref name="point"/> is at the card: over it (with <see cref="NearMargin"/>), from
        /// <paramref name="front"/> in front of it to <see cref="NearBehind"/> behind it; <paramref name="local"/> is where.
        /// </summary>
        public bool NearCard(Vector3 point, float front, out Vector2 local)
        {
            local = default;
            if (!Visible) return false;
            Vector3 q = PanelLayout.ToLocal(point, _root.position, _root.rotation);
            local = new Vector2(q.x, q.y);
            return q.z >= -front && q.z <= NearBehind && Mathf.Abs(q.x) <= Width * 0.5f + NearMargin && Mathf.Abs(q.y) <= _top + NearMargin;
        }

        /// <summary>The hovered and pressed button (0, 1, or -1 for none), like the panel's buttons.</summary>
        public void SetHover(int button, int pressed = -1)
        {
            _primary.SetState(pressed == 0 ? ElementState.Pressed : button == 0 ? ElementState.Hover : ElementState.Selected);
            if (_two) _secondary.SetState(pressed == 1 ? ElementState.Pressed : button == 1 ? ElementState.Hover : ElementState.Normal);
        }
    }
}

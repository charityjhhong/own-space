using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The AI request's own keyboard, docked under the tool panel (device batch 10), as wide as the panel. The Quest system overlay timed out on
    /// device, and Meta's in-app Virtual Keyboard is gone from SDK 207. It is built from the panel's glass elements,
    /// Inter fonts and theme:
    /// <list type="bullet">
    /// <item>a two-line field with the typed text;</item>
    /// <item>digits, three letter rows with Shift and Del;</item>
    /// <item>a bottom row: Cancel, comma, space, full stop, Send.</item>
    /// <item>Shift capitalises the next letter (one shot; the letter keys show capitals while it is on).</item>
    /// </list>
    /// Layout and hit tests are in the keyboard's local metres (34 mm keys); the root is scaled to the panel's width
    /// (<see cref="Dock"/>), which makes a key about 29 mm at the panel's default size. Nothing allocates after it is built.
    /// </summary>
    public sealed class PanelKeyboard
    {
        public enum Act { Char, Delete, Space, Send, Cancel, Shift }

        public const float Pitch = 0.038f, KeyHeight = 0.034f, Gap = 0.004f, Pad = 0.012f, FieldHeight = 0.028f, BarZone = 0.02f;
        /// <summary>While dragged by its bar it faces the eyes, tilted back this much.</summary>
        public const float TiltDegrees = 30f;
        /// <summary>
        /// Docking (device feedback on 0ddb26d: it was three times the panel's width and ran off the view; on dfb22f4: over
        /// a desk it shrank to 18 mm keys, too small). It is always this many times the panel's width, never wider than
        /// <see cref="MaxViewDegrees"/> of the view. It shrinks only to bring every corner within
        /// <see cref="ViewConeDegrees"/> of where the head points, and never below <see cref="MinKey"/> keys.
        /// </summary>
        public const float WidthRatio = 1.15f, DockGap = 0.012f, MaxViewDegrees = 50f, ViewConeDegrees = 45f, MinKey = 0.024f, MinDistance = 0.3f;
        /// <summary>
        /// Over a desk it rests on the desk instead: its top edge <see cref="DeskGap"/> in front of the panel, its bottom
        /// edge at least <see cref="DeskNear"/> ahead of the eyes, tilted back to face the eyes plus
        /// <see cref="DeskOblique"/> (flatter, like a real keyboard, so it covers less of the panel), within
        /// <see cref="DeskMinTilt"/> and <see cref="DeskMaxTilt"/> from upright.
        /// </summary>
        public const float DeskGap = 0.03f, DeskNear = 0.2f, DeskOblique = 20f, DeskMinTilt = 30f, DeskMaxTilt = 70f;
        /// <summary>Hit on the grab bar (drag to move the keyboard).</summary>
        public const int Bar = -3;
        public const string DeleteLabel = "Del", SpaceLabel = "space", SendLabel = "Create", CancelLabel = "Cancel", ShiftLabel = "Shift";
        static readonly string[] Rows = { "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm" };

        /// <summary>Hit result on the keyboard's glass but on no key (still the keyboard's pinch).</summary>
        public const int Back = -2;

        /// <summary>The text line: its size (field text, 14 regular) and the room (m) for text at that size, with 4 mm of slack.</summary>
        public const float TextMm = PanelElement.BodyMm;
        public static float TextWidth => Width - 2f * Pad - 0.016f - 0.004f;

        public static float Width => 10f * Pitch - Gap + 2f * Pad;
        public static float Height => Pad + BarZone + FieldHeight + Gap + 5f * Pitch - Gap + Pad;

        /// <summary>The scale that makes it <see cref="WidthRatio"/> of the panel's width, capped at <see cref="MaxViewDegrees"/> across from <paramref name="distance"/>.</summary>
        public static float ScaleFor(float panelWidth, float distance) =>
            Mathf.Min(panelWidth < float.MaxValue ? panelWidth * WidthRatio : float.MaxValue, 2f * distance * Mathf.Tan(MaxViewDegrees * 0.5f * Mathf.Deg2Rad)) / Width;

        /// <summary>How wide (degrees) something <paramref name="width"/> m across looks from <paramref name="distance"/> m.</summary>
        public static float AngularSize(float width, float distance) => 2f * Mathf.Atan2(width * 0.5f, Mathf.Max(1e-4f, distance)) * Mathf.Rad2Deg;

        /// <summary>
        /// Where the keyboard docks (pure, for tests). The panel never moves for it, and a desk never makes it smaller.
        /// <list type="bullet">
        /// <item>Free space under the panel: in the panel's plane, directly under its bottom edge, at the panel's tilt.</item>
        /// <item>A desk or bed in the way there: at the same size on that surface, in front of the panel, between the
        /// panel and the user, tilted toward the eyes, its bottom edge the panel's clearance (5 cm) above the surface.</item>
        /// <item>Last resort: it shrinks until every corner is inside the view cone, never below <see cref="MinKey"/> keys.</item>
        /// </list>
        /// </summary>
        public static void Dock(Vector3 head, Vector3 viewForward, Vector3 panelBottom, Quaternion panelRotation, float panelWidth,
            IPanelSurfaces surfaces, out Vector3 position, out Quaternion rotation, out float scale)
        {
            Vector3 up = panelRotation * Vector3.up, anchor = panelBottom - up * DockGap; // the keyboard's top edge when under the panel
            float floor = MinKey / KeyHeight;
            scale = Mathf.Max(floor, ScaleFor(panelWidth, Mathf.Max(MinDistance, Vector3.Distance(head, anchor))));
            rotation = panelRotation;
            position = anchor - up * (Height * scale * 0.5f);
            float sunk = surfaces != null ? Sunk(position, rotation, scale, surfaces) : 0f;
            bool onDesk = sunk > 0f;
            float deskTop = (position - up * (Height * scale * 0.5f)).y + sunk - ToolPanel.SurfaceClearance;
            if (onDesk)
            {
                // Nearer the eyes it looks wider: the view cap again, from where it rests.
                OnDesk(head, panelBottom, deskTop, scale, surfaces, out position, out rotation);
                scale = Mathf.Max(floor, Mathf.Min(scale, ScaleFor(float.MaxValue, Vector3.Distance(head, position))));
            }
            for (int i = 0; i < 16; i++)
            {
                if (onDesk) OnDesk(head, panelBottom, deskTop, scale, surfaces, out position, out rotation);
                else position = anchor - up * (Height * scale * 0.5f);
                if (scale <= floor || InView(head, viewForward, position, rotation, scale)) return;
                scale = Mathf.Max(floor, scale * 0.95f);
            }
        }

        // Resting on a surface whose top is at deskTop: see Dock. The tilt settles in a few steps (it depends on where the
        // keyboard ends up, which depends on the tilt).
        static void OnDesk(Vector3 head, Vector3 panelBottom, float deskTop, float scale, IPanelSurfaces surfaces, out Vector3 position, out Quaternion rotation)
        {
            Vector3 flat = panelBottom - head;
            flat.y = 0f;
            float reach = flat.magnitude, h = Height * scale, tilt = 50f;
            Vector3 toPanel = reach > 1e-4f ? flat / reach : Vector3.forward;
            position = default;
            rotation = Quaternion.identity;
            for (int i = 0; i < 4; i++)
            {
                float depth = h * Mathf.Sin(tilt * Mathf.Deg2Rad); // how far its top edge is behind its bottom edge
                float near = Mathf.Max(DeskNear, reach - DeskGap - depth);
                rotation = Quaternion.LookRotation(toPanel, Vector3.up) * Quaternion.Euler(tilt, 0f, 0f);
                position = new Vector3(head.x, deskTop + ToolPanel.SurfaceClearance, head.z) + toPanel * near + rotation * new Vector3(0f, h * 0.5f, 0f);
                float elevation = Mathf.Atan2(head.y - position.y, Mathf.Max(0.01f, near + depth * 0.5f)) * Mathf.Rad2Deg;
                tilt = Mathf.Clamp(elevation + DeskOblique, DeskMinTilt, DeskMaxTilt);
            }
            float need = Sunk(position, rotation, scale, surfaces); // something higher under where it landed: over that too
            if (need > 0f) position += Vector3.up * need;
        }

        /// <summary>Whether all four corners are within <see cref="ViewConeDegrees"/> of where the head points.</summary>
        public static bool InView(Vector3 head, Vector3 viewForward, Vector3 position, Quaternion rotation, float scale)
        {
            float cos = Mathf.Cos(ViewConeDegrees * Mathf.Deg2Rad);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = position + rotation * new Vector3((i % 2 == 0 ? -0.5f : 0.5f) * Width * scale, (i < 2 ? -0.5f : 0.5f) * Height * scale, 0f);
                if (Vector3.Dot((corner - head).normalized, viewForward) < cos) return false;
            }
            return true;
        }

        // How far (m) its bottom edge is below the clearance over the highest surface under it; 0 or less when clear.
        static float Sunk(Vector3 position, Quaternion rotation, float scale, IPanelSurfaces surfaces)
        {
            float need = float.MinValue;
            for (int k = -1; k <= 1; k++)
            {
                Vector3 edge = position + rotation * new Vector3(k * Width * 0.5f * scale, -Height * 0.5f * scale, 0f);
                if (surfaces.TopUnder(edge, out float top)) need = Mathf.Max(need, top + ToolPanel.SurfaceClearance - edge.y);
            }
            return need;
        }

        /// <summary>Facing the eyes, tilted back (the top leans away).</summary>
        public static Quaternion Facing(Vector3 head, Vector3 position)
        {
            Vector3 away = position - head;
            return Quaternion.LookRotation(away.sqrMagnitude > 1e-6f ? away : Vector3.forward, Vector3.up) * Quaternion.Euler(TiltDegrees, 0f, 0f);
        }

        struct Key { public Act Act; public char Char; public string Label, Upper; public float Slot, Span; public int Row; }

        static readonly Key[] Keys = BuildKeys();
        public static int KeyCount => Keys.Length;
        public static Act ActOf(int key) => Keys[key].Act;
        public static char CharOf(int key) => Keys[key].Char;
        /// <summary>The character a key types with Shift on or off.</summary>
        public static char CharOf(int key, bool shift) => shift ? char.ToUpperInvariant(Keys[key].Char) : Keys[key].Char;
        public static string LabelOf(int key) => Keys[key].Label;

        /// <summary>The index of the key with this label (tests and the Editor).</summary>
        public static int Find(string label)
        {
            for (int i = 0; i < Keys.Length; i++) if (Keys[i].Label == label) return i;
            return -1;
        }

        static Key[] BuildKeys()
        {
            var keys = new System.Collections.Generic.List<Key>();
            for (int r = 0; r < Rows.Length; r++)
            {
                float offset = r == 2 ? 0.5f : r == 3 ? 1.5f : 0f; // the home row half a key in; z..m after Shift
                for (int i = 0; i < Rows[r].Length; i++)
                {
                    string label = Rows[r][i].ToString();
                    keys.Add(new Key { Act = Act.Char, Char = Rows[r][i], Label = label, Upper = label.ToUpperInvariant(), Slot = offset + i, Span = 1f, Row = r });
                }
            }
            keys.Add(new Key { Act = Act.Shift, Label = ShiftLabel, Slot = 0f, Span = 1.5f, Row = 3 });
            keys.Add(new Key { Act = Act.Delete, Label = DeleteLabel, Slot = 8.5f, Span = 1.5f, Row = 3 });
            keys.Add(new Key { Act = Act.Cancel, Label = CancelLabel, Slot = 0f, Span = 2f, Row = 4 });
            keys.Add(new Key { Act = Act.Char, Char = ',', Label = ",", Slot = 2f, Span = 1f, Row = 4 });
            keys.Add(new Key { Act = Act.Space, Char = ' ', Label = SpaceLabel, Slot = 3f, Span = 4f, Row = 4 });
            keys.Add(new Key { Act = Act.Char, Char = '.', Label = ".", Slot = 7f, Span = 1f, Row = 4 });
            keys.Add(new Key { Act = Act.Send, Label = SendLabel, Slot = 8f, Span = 2f, Row = 4 });
            return keys.ToArray();
        }

        /// <summary>A key's centre and size in the keyboard's local metres (origin at its centre).</summary>
        public static void KeyRect(int key, out Vector2 centre, out Vector2 size)
        {
            Key k = Keys[key];
            float top = Height * 0.5f - Pad - BarZone - FieldHeight - Gap;
            float w = k.Span * Pitch - Gap;
            centre = new Vector2(-Width * 0.5f + Pad + k.Slot * Pitch + w * 0.5f, top - k.Row * Pitch - KeyHeight * 0.5f);
            size = new Vector2(w, KeyHeight);
        }

        readonly Transform _root;
        readonly PanelElement _back, _field, _text, _bar;
        readonly PanelElement[] _keys = new PanelElement[Keys.Length];
        int _hover = -1, _flash = -1;
        bool _canSend, _shift;

        public PanelKeyboard(Transform parent, ToolPanel panel)
        {
            _root = new GameObject("Keyboard").transform;
            _root.SetParent(parent, false);
            Material glass = panel.GlassMaterial;
            PanelStyle style = panel.CurrentStyle;
            PanelFonts fonts = panel.Fonts;
            _back = new PanelElement(_root, glass, style, fonts, "Back", 13f, surface: true);
            _back.Place(Vector2.zero, new Vector2(Width, Height));
            _bar = new PanelElement(_root, glass, style, fonts, "Bar", 13f);
            _bar.Place(new Vector2(0f, Height * 0.5f - Pad * 0.5f - BarZone * 0.5f), new Vector2(0.06f, 0.006f), 0.003f);
            // The echo strip: one line with the end of the typed text, so the user need not look up at the panel.
            float fieldY = Height * 0.5f - Pad - BarZone - FieldHeight * 0.5f, inner = Width - 2f * Pad;
            _field = new PanelElement(_root, glass, style, fonts, "Field", 13f);
            _field.Place(new Vector2(0f, fieldY), new Vector2(inner, FieldHeight), 0.01f);
            _text = new PanelElement(_root, glass, style, fonts, "Text", 13f);
            _text.SetKind(CardKind.Title);
            _text.MinTextMm = TextMm; // never auto-sized: the request fits the line before it is shown (TextTail)
            _text.SetTextStyle(PanelElement.TextStyle.Body); // field text: 14 regular, full ink
            _text.SetLines(1);
            _text.Place(new Vector2(0f, fieldY), new Vector2(inner - 0.016f, FieldHeight));
            for (int i = 0; i < Keys.Length; i++)
            {
                _keys[i] = new PanelElement(_root, glass, style, fonts, "Key " + Keys[i].Label, Keys[i].Span > 1f ? 13f : 15f);
                _keys[i].TextPadding = 0.004f; // a 34 mm key: the panel's 12 mm padding left no room for "Del"
                KeyRect(i, out Vector2 c, out Vector2 s);
                _keys[i].Place(c, s, 0.008f);
                _keys[i].SetText(Keys[i].Label);
            }
            Restyle(style);
            _root.gameObject.SetActive(false);
        }

        public bool Visible => _root.gameObject.activeSelf;
        public Transform Root => _root;
        /// <summary>The root's scale (1 = 34 mm keys).</summary>
        public float Scale => _root.localScale.x;

        public void Place(Vector3 position, Quaternion rotation, float scale)
        {
            _root.SetPositionAndRotation(position, rotation);
            _root.localScale = Vector3.one * scale;
        }

        public void Show() { if (!Visible) _root.gameObject.SetActive(true); }
        public void Hide() { if (Visible) _root.gameObject.SetActive(false); _hover = _flash = -1; }

        /// <summary>The theme changed (light or dark).</summary>
        public void Restyle(PanelStyle style)
        {
            _back.SetStyle(style);
            _bar.SetStyle(style);
            _field.SetStyle(style);
            _text.SetStyle(style);
            for (int i = 0; i < _keys.Length; i++) _keys[i].SetStyle(style);
            Refresh();
        }

        /// <summary>The field's text (built by the caller on a key press only), and whether Send is live.</summary>
        public void SetText(string shown, bool canSend)
        {
            _text.SetText(shown);
            if (_canSend != canSend) { _canSend = canSend; Refresh(); }
        }

        /// <summary>The key under the ray (or touched by the pinch point within 3 cm), <see cref="Back"/>, or -1.</summary>
        public int Hit(Ray ray, Vector3 pinch, bool pinchValid)
        {
            if (!Visible) return -1;
            float scale = Mathf.Max(1e-4f, _root.lossyScale.x);
            Vector2 p;
            Vector3 local = PanelLayout.ToLocal(pinch, _root.position, _root.rotation) / scale;
            // Near: the key under the fingers, from 8 cm in front to 3 cm behind (the panel's rule: hover and press alike).
            if (pinchValid && ToolPanel.NearDepth(local.z * scale) && PanelLayout.Contains(Vector2.zero, new Vector2(Width, Height), new Vector2(local.x, local.y)))
                p = new Vector2(local.x, local.y);
            else if (PanelLayout.RayToPanel(ray, _root.position, _root.rotation, out p, out _)) p /= scale;
            else return -1;
            return HitLocal(p);
        }

        /// <summary>The key at a local point (half the gap around each key counts), <see cref="Back"/>, or -1.</summary>
        public static int HitLocal(Vector2 p)
        {
            if (PanelLayout.Contains(new Vector2(0f, Height * 0.5f - (Pad + BarZone) * 0.5f), new Vector2(Width, Pad + BarZone), p)) return Bar;
            for (int i = 0; i < Keys.Length; i++)
            {
                KeyRect(i, out Vector2 c, out Vector2 s);
                if (PanelLayout.Contains(c, s, p, Gap * 0.5f)) return i;
            }
            return PanelLayout.Contains(Vector2.zero, new Vector2(Width, Height), p) ? Back : -1;
        }

        /// <summary>Shift on: the letter keys show capitals and Shift is lit (on a change only; the strings are cached).</summary>
        public void SetShift(bool on)
        {
            if (on == _shift) return;
            _shift = on;
            for (int i = 0; i < Keys.Length; i++) if (Keys[i].Upper != null) _keys[i].SetText(on ? Keys[i].Upper : Keys[i].Label);
            Refresh();
        }

        /// <summary>Lifts the hovered key; <paramref name="flash"/> shows a key as pressed (-1 = none).</summary>
        public void SetHover(int hover, int flash)
        {
            if (hover == _hover && flash == _flash) return;
            _hover = hover;
            _flash = flash;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < _keys.Length; i++)
            {
                bool send = Keys[i].Act == Act.Send;
                ElementState state = i == _flash ? ElementState.Pressed
                    : send && !_canSend ? ElementState.Disabled
                    : send || (_shift && Keys[i].Act == Act.Shift) ? ElementState.Selected // Send, and Shift while on: the dark pill
                    : i == _hover ? ElementState.Hover : ElementState.Normal;
                _keys[i].SetState(state);
            }
        }
    }
}

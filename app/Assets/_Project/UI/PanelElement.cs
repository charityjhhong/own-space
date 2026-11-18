using TMPro;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>One panel's fonts (content's Inter Regular and SemiBold SDF); null = TMP's default, with bold for semibold.</summary>
    public sealed class PanelFonts
    {
        /// <summary>The panel's queue: after the world and its transparents (PanelGlass is Transparent+100).</summary>
        public const int Queue = 3100;
        static readonly int ZTestId = Shader.PropertyToID("unity_GUIZTestMode");

        public readonly TMP_FontAsset Regular, SemiBold;
        /// <summary>Per-panel copies of the fonts' materials that draw on top of the room (ZTest Always, <see cref="Queue"/>).</summary>
        public readonly Material RegularMaterial, SemiBoldMaterial;

        public PanelFonts(TMP_FontAsset regular, TMP_FontAsset semiBold)
        {
            Regular = regular;
            SemiBold = semiBold;
            RegularMaterial = OnTop(regular);
            SemiBoldMaterial = OnTop(semiBold);
        }

        public Material MaterialFor(TMP_FontAsset font) => font == SemiBold ? SemiBoldMaterial : font == Regular ? RegularMaterial : null;

        static Material OnTop(TMP_FontAsset font)
        {
            if (font == null || font.material == null) return null;
            var m = new Material(font.material) { name = font.material.name + " (panel, on top)", renderQueue = Queue };
            m.SetFloat(ZTestId, (float)UnityEngine.Rendering.CompareFunction.Always);
            return m;
        }

        public void Dispose()
        {
            if (RegularMaterial != null) Object.Destroy(RegularMaterial);
            if (SemiBoldMaterial != null) Object.Destroy(SemiBoldMaterial);
        }
    }

    /// <summary>
    /// One piece of a panel (surface, tab, card, handle, pill): a quad on the one shared
    /// OwnSpace/PanelGlass material, styled per state from a <see cref="PanelStyle"/> through a
    /// MaterialPropertyBlock (fill, rim, rounded rect), plus one line of TextMeshPro text sized in
    /// millimetres (spec v4 type scale) that ends in an ellipsis when it does not fit. Text, colours and
    /// shape are written only when they change.
    /// </summary>
    public sealed class PanelElement
    {
        static readonly int FillId = Shader.PropertyToID("_BaseColor");
        static readonly int Fill2Id = Shader.PropertyToID("_BaseColor2");
        static readonly int PatternId = Shader.PropertyToID("_Pattern"), PatternColorId = Shader.PropertyToID("_PatternColor");
        static readonly int GlyphId = Shader.PropertyToID("_Glyph"), GlyphColorId = Shader.PropertyToID("_GlyphColor");
        static readonly int ClipId = Shader.PropertyToID("_Clip");
        Vector4 _clip; // element-local x: min, max, fade, on
        bool _textClipped, _textMeshStale = true;
        Vector4 _pattern;
        Color _patternColor;
        int _glyph;
        bool _danger;
        float _fade = 1f;

        /// <summary>Fades the whole element (fill, rim, text, icon) to <paramref name="alpha"/> (toast in and out).</summary>
        public void SetFade(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            if (Mathf.Approximately(alpha, _fade)) return;
            _fade = alpha;
            _dirty = true;
            Apply();
        }

        internal float Fade => _fade;

        Color _forceFill, _forceText;

        /// <summary>Fixed fill and text colours in every theme and state (alpha 0 = the style's own); the pending banner.</summary>
        public void SetColors(Color fill, Color text)
        {
            if (fill == _forceFill && text == _forceText) return;
            _forceFill = fill;
            _forceText = text;
            _dirty = true;
            Apply();
        }

        internal PanelStyle.Slot Shown => Look(_style, State, _swatch, _surface, _kind);
        internal Color ForcedFill => _forceFill;
        internal Color ForcedText => _forceText;

        /// <summary>Shows the text in the refused red (a destructive action such as Remove).</summary>
        public void SetDanger(bool danger)
        {
            if (danger == _danger) return;
            _danger = danger;
            _dirty = true;
            Apply();
        }
        static readonly int RimId = Shader.PropertyToID("_RimColor");
        static readonly int RectId = Shader.PropertyToID("_Rect");
        static MaterialPropertyBlock _block;



        const float CardRadius = 0.014f, ThumbWidth = 0.068f, CardInset = 0.006f;
        public const float ThumbHeight = 0.046f;
        Renderer _thumb, _icon;
        Vector4 _iconRect;
        int _iconSide;
        static readonly int IconId = Shader.PropertyToID("_Icon"), IconOnId = Shader.PropertyToID("_IconOn");
        public const float IconSize = 0.016f;
        /// <summary>
        /// A side icon's room in a toggle (m): 1 mm from the edge, the icon, 2 mm to the text. The text box ends there, so
        /// an icon never sits on the text (device, build 9bfbd72: the Tidy up step row's checks covered the step names).
        /// A page adds this to the width of a toggle that shows a side icon.
        /// </summary>
        public const float ToggleIconRoom = ToggleIconInset + IconSize + SideIconGap;
        const float ToggleIconInset = 0.001f, ChipIconInset = 0.012f, SideIconGap = 0.002f;

        /// <summary>
        /// Shows an icon from content's atlas (<see cref="PanelIcons"/>) on its own small quad with the panel
        /// material: <paramref name="side"/> 0 = centred (an icon button), -1 = left of the text, 1 = right of it.
        /// Zero hides it. Tinted like the text.
        /// </summary>
        public void SetIcon(Vector4 atlasRect, int side = 0)
        {
            if (atlasRect == _iconRect && side == _iconSide) return;
            _iconRect = atlasRect;
            _iconSide = side;
            if (atlasRect != Vector4.zero && _icon == null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Icon";
                q.transform.SetParent(Root, false);
                _icon = q.GetComponent<Renderer>();
                _icon.sharedMaterial = _quad.sharedMaterial;
                _icon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _icon.sortingOrder = 1;
            }
            if (_icon != null) _icon.enabled = atlasRect != Vector4.zero;
            _dirty = true;
            Shape();
        }

        /// <summary>The width (m) <paramref name="text"/> needs in this element's font and size (no new objects).</summary>
        public float PreferredWidth(string text) => _text.GetPreferredValues(text).x;

        /// <summary>
        /// The widths (m) <paramref name="texts"/> need at <paramref name="mm"/> in <paramref name="font"/>
        /// (null = TMP's default), for content-hugging chips and toggles. Call when a page is built, not per
        /// frame: it makes one temporary text object and destroys it.
        /// </summary>
        public static void MeasureWidths(System.Collections.Generic.IReadOnlyList<string> texts, float mm, TMP_FontAsset font, float[] widths)
        {
            var go = new GameObject("PanelTextMeasure") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var measure = go.AddComponent<TextMeshPro>();
                go.GetComponent<MeshRenderer>().enabled = false;
                measure.textWrappingMode = TextWrappingModes.NoWrap;
                if (font != null) measure.font = font;
                measure.fontSize = FontSize(mm);
                for (int i = 0; i < texts.Count && i < widths.Length; i++) widths[i] = measure.GetPreferredValues(texts[i]).x;
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>One width; see <see cref="MeasureWidths"/>.</summary>
        public static float MeasureWidth(string text, float mm, TMP_FontAsset font = null)
        {
            var one = new float[1];
            MeasureWidths(new[] { text }, mm, font, one);
            return one[0];
        }

        /// <summary>Shows an atlas thumbnail on a Card (zero hides it).</summary>
        public void SetThumb(Vector4 atlasST)
        {
            bool shown = atlasST != Vector4.zero;
            if (shown && _thumb == null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.name = "Thumb";
                q.transform.SetParent(Root, false);
                _thumb = q.GetComponent<Renderer>();
                _thumb.sharedMaterial = _quad.sharedMaterial; // the same glass material, in thumbnail mode (no extra material)
                _thumb.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _thumb.sortingOrder = 1;
                Shape();
            }
            if (_thumb == null) return;
            _thumb.enabled = shown;
            if (!shown) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            _thumb.GetPropertyBlock(_block);
            _block.SetFloat(IconOnId, 2f);
            _block.SetVector(IconId, atlasST);
            _thumb.SetPropertyBlock(_block);
        }

        /// <summary>TextMeshPro (3D) font size for a text whose em is <paramref name="mm"/> millimetres (1 font unit = 0.1 m em).</summary>
        public static float FontSize(float mm) => mm * 0.01f;

        public readonly Transform Root;
        readonly Renderer _quad;
        readonly TextMeshPro _text;
        readonly float _textMm;
        readonly PanelFonts _fonts;

        /// <summary>Text size (mm) while selected (spec: the selected tab is 15 mm); 0 = the normal size.</summary>
        public float SelectedTextMm { get; set; }

        /// <summary>When above 0, the text shrinks down to this size (mm) instead of an ellipsis (the tabs).</summary>
        public float MinTextMm { get; set; }

        /// <summary>Inner side padding (m) of a chip's text box; tabs use less so their words fit.</summary>
        public float TextPadding { get; set; } = 0.012f;
        readonly bool _surface;
        PanelStyle _style;
        Color _swatch, _swatch2; // a = 0: none
        float _radius = -1f;
        bool _dirty = true;
        CardKind _kind;
        const float SwatchSide = 0.034f, CardTextInset = 0.006f, CardTextMm = CaptionMm;

        /// <summary>
        /// The panel's only four text styles (mockups round 2, rules.html; millimetres): title, tab and chosen 14 semibold;
        /// body, button, field and key 14 regular; section name 12 semibold at the secondary ink; caption, card name and
        /// hint 12 regular. Nothing is drawn below 12.
        /// </summary>
        public const float TitleMm = 14f, BodyMm = 14f, SectionMm = 12f, CaptionMm = 12f;

        /// <summary>How a <see cref="CardKind.Title"/> reads: a section name (12 semibold; a title of two or more lines is a
        /// 12 regular message), a page header (14 semibold, full ink), a row's body text (14 regular, full ink) or a
        /// caption (12 regular).</summary>
        public enum TextStyle { Section, Header, Caption, Body }
        TextStyle _textStyle;

        public void SetTextStyle(TextStyle style)
        {
            if (style == _textStyle) return;
            _textStyle = style;
            _dirty = true;
            Apply();
        }

        /// <summary>Always semibold (the tabs: bold labels, chosen or not).</summary>
        public bool Bold { get; set; }
        /// <summary>No fill and no rim until hovered, pressed or chosen (the tabs: only the chosen tab is filled).</summary>
        public bool Flat { get; set; }
        /// <summary>The chosen text option's underline (m).</summary>
        public const float UnderlineHeight = 0.0018f;

        /// <summary>The smallest text the panel shrinks to before an ellipsis (millimetres).</summary>
        public const float FloorTextMm = 12f;

        public Vector2 Centre { get; private set; }
        public Vector2 Size { get; private set; }
        public ElementState State { get; private set; }

        /// <param name="textMm">Text em size in millimetres for chips (titles, toggles and swatch labels use 13).</param>
        /// <param name="surface">True for the panel background (uses the Surface slot, drawn behind).</param>
        public PanelElement(Transform parent, Material material, PanelStyle style, PanelFonts fonts, string name, float textMm, bool surface = false)
        {
            _textMm = textMm;
            _fonts = fonts;
            _style = style != null ? style : PanelStyle.Default;
            _surface = surface;
            Root = new GameObject(name).transform;
            Root.SetParent(parent, false);
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>()); // hit-tested in code; also safe outside Play (tests)
            quad.transform.SetParent(Root, false);
            _quad = quad.GetComponent<Renderer>();
            _quad.sharedMaterial = material;
            _quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _quad.receiveShadows = false;
            _quad.sortingOrder = surface ? -1 : 0; // surface, then elements, then text (all transparent)

            var label = new GameObject("Text", typeof(RectTransform));
            label.transform.SetParent(Root, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.002f);
            _text = label.AddComponent<TextMeshPro>();
            if (fonts?.Regular != null) SetFont(fonts.Regular);
            _text.GetComponent<MeshRenderer>().sortingOrder = 1;
            _text.textWrappingMode = TextWrappingModes.NoWrap;
            _text.overflowMode = TextOverflowModes.Ellipsis;
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = FontSize(textMm);
            _text.text = "";
        }

        /// <summary>Places the element at panel-local <paramref name="centre"/>; radius &lt; 0 = the style's.</summary>
        public void Place(Vector2 centre, Vector2 size, float radius = -1f)
        {
            Centre = centre;
            Root.localPosition = new Vector3(centre.x, centre.y, _surface ? 0f : -0.001f);
            if (size == Size && radius == _radius) return;
            Size = size;
            _radius = radius;
            Shape();
        }

        /// <summary>Sets how the element draws within its rect (see <see cref="CardKind"/>).</summary>
        public void SetKind(CardKind kind)
        {
            if (kind == _kind) return;
            _kind = kind;
            Shape();
        }

        int _nameLines = 1;
        /// <summary>A card's name line (11 mm text), the gap under the thumbnail, and the slack of the name's text box.</summary>
        public const float CardLineHeight = 0.015f, CardGap = 0.004f, CardNameSlack = 0.008f;

        /// <summary>
        /// A card's thumbnail and name as one group, centred in the card's height (pure, for tests; device, build dfb22f4:
        /// "centre the object and the text": with a one-line name the picture sat at the top and the name low, 18 mm apart).
        /// Returns the thumbnail's and the name box's centre y (card-local) and the name box's height.
        /// </summary>
        public static void CardLayout(float height, int nameLines, out float thumbY, out float nameY, out float nameHeight) =>
            CardLayout(height, nameLines, out thumbY, out nameY, out nameHeight, out _);

        /// <summary>The picture's height with a three-line name: smaller, so the whole name fits the card (names are never cut).</summary>
        public const float SmallThumbHeight = 0.036f;

        /// <summary>As above, and the picture's height: <see cref="SmallThumbHeight"/> under a name of three or more lines.</summary>
        public static void CardLayout(float height, int nameLines, out float thumbY, out float nameY, out float nameHeight, out float thumbHeight)
        {
            thumbHeight = nameLines >= 3 ? SmallThumbHeight : ThumbHeight;
            float text = Mathf.Max(1, nameLines) * CardLineHeight, group = thumbHeight + CardGap + text;
            float top = height * 0.5f - Mathf.Max(nameLines >= 3 ? CardInset * 0.5f : CardInset, (height - group) * 0.5f);
            thumbY = top - thumbHeight * 0.5f;
            nameY = top - thumbHeight - CardGap - text * 0.5f;
            nameHeight = text + (nameLines >= 3 ? CardNameSlack * 0.5f : CardNameSlack); // room around the lines, so a line is never cut by its own box
        }

        /// <summary>
        /// <paramref name="text"/> broken at spaces so no line is wider than <paramref name="width"/> (m) at
        /// <paramref name="mm"/> in <paramref name="font"/> (a card's name: it wraps, it is never cut). A single word wider
        /// than the width stays whole on its own line. Call when the data is first shown, not per frame.
        /// </summary>
        public static string WrapToWidth(string text, float width, float mm, TMP_FontAsset font)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(' ') < 0) return text;
            var go = new GameObject("PanelTextMeasure") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var measure = go.AddComponent<TextMeshPro>();
                go.GetComponent<MeshRenderer>().enabled = false;
                measure.textWrappingMode = TextWrappingModes.NoWrap;
                if (font != null) measure.font = font;
                measure.fontSize = FontSize(mm);
                string[] words = text.Split(' ');
                var wrapped = new System.Text.StringBuilder(text.Length + 2);
                string line = words[0];
                for (int i = 1; i < words.Length; i++)
                {
                    string longer = line + " " + words[i];
                    if (measure.GetPreferredValues(longer).x <= width) line = longer;
                    else { wrapped.Append(line).Append('\n'); line = words[i]; }
                }
                return wrapped.Append(line).ToString();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Quad and text placement per kind: a chip fills the rect; a swatch is a square or disc at the top
        // with its label below; a toggle's quad is its underline; a title is left-aligned text.
        void Shape()
        {
            bool swatch = _kind == CardKind.Swatch || _kind == CardKind.Disc, card = _kind == CardKind.Card;
            CardLayout(Size.y, _nameLines, out float thumbY, out float nameY, out float nameHeight, out float thumbHeight);
            if (_thumb != null)
            {
                _thumb.transform.localPosition = new Vector3(0f, thumbY, -0.0015f);
                _thumb.transform.localScale = new Vector3(Mathf.Min(ThumbWidth * thumbHeight / ThumbHeight, Size.x - 2f * CardInset), thumbHeight, 1f);
            }
            Vector2 quad = swatch ? new Vector2(SwatchSide, SwatchSide) : Size; // a toggle is a pill segment
            float quadY = swatch ? Size.y * 0.5f - SwatchSide * 0.5f : 0f;
            _quad.transform.localPosition = new Vector3(0f, quadY, 0f);
            _quad.transform.localScale = new Vector3(quad.x, quad.y, 1f);
            // The text box: the whole rect (a title left-aligned), or under the swatch for its label.
            float labelHeight = swatch ? Size.y - SwatchSide : card ? nameHeight : Size.y;
            _textMeshStale = true; // the text box, alignment or wrapping changes below
            // A side icon keeps its own room: the text box stops short of it and moves over.
            float iconInset = _kind == CardKind.Chip ? ChipIconInset : ToggleIconInset;
            float iconRoom = _icon != null && _iconRect != Vector4.zero && _iconSide != 0 ? iconInset + IconSize + SideIconGap : 0f;
            _text.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.001f, Size.x - iconRoom - (_kind == CardKind.Chip ? TextPadding : card ? 2f * CardTextInset : 0f)), Mathf.Max(0.001f, labelHeight));
            _text.transform.localPosition = new Vector3(-_iconSide * iconRoom * 0.5f, card ? nameY : swatch ? -Size.y * 0.5f + labelHeight * 0.5f : 0f, -0.002f);
            // Card names come pre-split at a space (PanelText.TwoLines) and never wrap on their own, so a hyphenated
            // word is never broken; a line that is still too long ends in an ellipsis. Long titles may wrap.
            _text.textWrappingMode = _lines > 1 && !card ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            // Titles sit on their row's centre line (device shot 120543: "Appearance" and "My space" rode above their chips;
            // TMP's Left is top-left).
            _text.alignment = _kind != CardKind.Title ? TextAlignmentOptions.Center : _centred ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineLeft;
            _shapeSize = quad;
            if (_icon != null)
            {
                float x = _iconSide == 0 ? 0f : _iconSide * (Size.x * 0.5f - iconInset - IconSize * 0.5f);
                _icon.transform.localPosition = new Vector3(x, 0f, -0.0015f);
                _icon.transform.localScale = new Vector3(IconSize, IconSize, 1f);
            }
            _dirty = true;
            Apply();
        }

        Vector2 _shapeSize;

        // The font and its on-top material copy (the panel never hides behind real furniture).
        void SetFont(TMP_FontAsset font)
        {
            _text.font = font;
            Material onTop = _fonts?.MaterialFor(font);
            if (onTop != null) _text.fontSharedMaterial = onTop;
        }
        int _lines = 1;

        /// <summary>How many lines the text may wrap to (the element's height must fit them); 1 = one line with an ellipsis.</summary>
        bool _centred;

        /// <summary>A title in a centred row (the pager's page count) centres its text in its box; other titles start at the left.</summary>
        public void SetCentred(bool centred)
        {
            if (centred == _centred) return;
            _centred = centred;
            Shape();
        }

        public void SetLines(int lines)
        {
            lines = Mathf.Max(1, lines);
            if (lines == _lines) return;
            _lines = lines;
            Shape();
        }

        public void SetText(string text)
        {
            if (ReferenceEquals(_text.text, text)) return;
            _text.text = text;
            _textMeshStale = true;
            int lines = 1;
            for (int i = 0; text != null && i < text.Length; i++) if (text[i] == '\n') lines++;
            if (lines != _nameLines) { _nameLines = lines; if (_kind == CardKind.Card) Shape(); }
            if (_clip.w > 0f) ClipText();
        }

        /// <summary>A colour swatch (finish, mood) shown as the fill in the Normal and Hover states; a = 0 for none.</summary>
        public void SetStyle(PanelStyle style) { _style = style != null ? style : PanelStyle.Default; _dirty = true; Apply(); }
        /// <summary>
        /// Clips the element to [<paramref name="minX"/>, <paramref name="maxX"/>] (element-local x, metres),
        /// fading over <paramref name="fade"/>: a scrolling row without a Mask. <paramref name="on"/> false = no clip.
        /// </summary>
        public void SetClip(bool on, float minX = 0f, float maxX = 0f, float fade = 0f)
        {
            var clip = on ? new Vector4(minX, maxX, fade, 1f) : Vector4.zero;
            if (clip == _clip) return;
            _clip = clip;
            _dirty = true;
            Apply();
        }

        // Text fades like the quad: per-vertex alpha. The mesh is rebuilt only after a text, colour, font or size
        // change; a clip-only change (a drag) rewrites the alpha of the cached mesh from the text colour.
        void ClipText()
        {
            if (_textMeshStale) { _text.ForceMeshUpdate(); _textMeshStale = false; }
            float baseAlpha = _text.color.a * 255f;
            TMP_TextInfo info = _text.textInfo;
            float f = Mathf.Max(_clip.z, 1e-5f), x0 = _text.transform.localPosition.x;
            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                Vector3[] v = info.meshInfo[m].vertices;
                Color32[] c = info.meshInfo[m].colors32;
                for (int k = 0; v != null && c != null && k < info.meshInfo[m].vertexCount; k++)
                {
                    float x = v[k].x + x0;
                    float fade = _clip.w > 0f ? Mathf.Clamp01((x - _clip.x) / f) * Mathf.Clamp01((_clip.y - x) / f) : 1f;
                    c[k].a = (byte)(baseAlpha * fade);
                }
            }
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        internal Vector4 Clip => _clip;

        /// <summary>Finish pattern lines and a glyph (see <see cref="CardInfo"/>); written only when they change.</summary>
        public void SetDecor(Vector4 pattern, Color patternColor, int glyph)
        {
            if (pattern == _pattern && patternColor == _patternColor && glyph == _glyph) return;
            _pattern = pattern;
            _patternColor = patternColor;
            _glyph = glyph;
            _dirty = true;
            Apply();
        }

        public void SetSwatch(Color swatch, Color bottom = default)
        {
            if (swatch == _swatch && bottom == _swatch2) return;
            _swatch = swatch;
            _swatch2 = bottom;
            _dirty = true;
            Apply();
        }

        public void SetState(ElementState state)
        {
            if (state == State) return;
            State = state;
            _dirty = true;
            Apply();
        }

        public void SetVisible(bool visible)
        {
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
        }

        public bool Visible => Root.gameObject.activeSelf;
        internal TextMeshPro Text => _text;

        /// <summary>The fill, rim and text for a state (pure, for tests): the surface slot, a swatch, or the state's slot.</summary>
        public static PanelStyle.Slot Look(PanelStyle style, ElementState state, Color swatch, bool surface, CardKind kind = CardKind.Chip)
        {
            if (surface) return style.Surface;
            bool selected = state == ElementState.Selected;
            switch (kind)
            {
                case CardKind.Title: return new PanelStyle.Slot(Color.clear, Color.clear, style.TextSecondary);
                case CardKind.Toggle:
                    // A text option, not a pill (mockups round 2): the chosen one is full ink (semibold, underlined in
                    // Apply), the others secondary ink; a fill shows only under the hand (hover, pressed) or a refusal.
                    PanelStyle.Slot t = style.Get(state);
                    if (state == ElementState.Normal || selected || state == ElementState.Disabled) { t.Fill = Color.clear; t.Rim = Color.clear; }
                    if (state == ElementState.Normal) t.Text = style.TextSecondary;
                    if (selected) t.Text = style.Normal.Text;
                    return t;
                case CardKind.Swatch:
                case CardKind.Disc:
                case CardKind.Card:
                    PanelStyle.Slot s = style.Get(selected ? ElementState.Normal : state);
                    // "Real" (no swatch) is see-through: no fill, just a ring, so the room shows through it.
                    if (swatch.a > 0f) s.Fill = swatch;
                    else { s.Fill = Color.clear; s.Rim = style.TextSecondary; }
                    if (selected) s.Rim = style.Accent;
                    s.Text = style.Normal.Text;
                    return s;
            }
            PanelStyle.Slot slot = style.Get(state);
            if (swatch.a > 0f && (state == ElementState.Normal || state == ElementState.Hover)) slot.Fill = swatch;
            return slot;
        }

        void Apply()
        {
            if (!_dirty) return;
            _dirty = false;
            PanelStyle.Slot slot = Look(_style, State, _swatch, _surface, _kind);
            if (_forceFill.a > 0f) { slot.Fill = _forceFill; slot.Rim = Color.clear; }
            if (_forceText.a > 0f) slot.Text = _forceText;
            if (Flat && (State == ElementState.Normal || State == ElementState.Disabled)) { slot.Fill = Color.clear; slot.Rim = Color.clear; }
            if (_kind == CardKind.Title && (_textStyle == TextStyle.Header || _textStyle == TextStyle.Body)) slot.Text = _style.Normal.Text;
            if (_kind == CardKind.Toggle)
            {
                // The chosen option's quad is its underline, in the text's ink; otherwise the quad is the whole option.
                bool underline = State == ElementState.Selected;
                _shapeSize = underline ? new Vector2(Mathf.Max(0.004f, Size.x - 0.004f), UnderlineHeight) : Size;
                _quad.transform.localPosition = new Vector3(0f, underline ? -Size.y * 0.5f + UnderlineHeight * 0.5f : 0f, 0f);
                _quad.transform.localScale = new Vector3(_shapeSize.x, _shapeSize.y, 1f);
                if (underline) slot.Fill = slot.Text;
            }
            float radius = _kind == CardKind.Disc ? SwatchSide * 0.5f : _kind == CardKind.Toggle ? _shapeSize.y * 0.5f : _kind == CardKind.Card ? CardRadius
                : _radius >= 0f ? _radius : _surface ? _style.SurfaceRadius : _style.CornerRadius;
            if (_block == null) _block = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_block);
            if (_danger && State != ElementState.Disabled)
            {
                // A destructive action at full contrast in both themes (device shot 233031: dim red on a dark pill was unreadable):
                // the refusal red as the fill, light text.
                Color red = _style.Refused.Rim;
                slot.Fill = new Color(red.r, red.g, red.b, State == ElementState.Hover || State == ElementState.Pressed ? 1f : 0.88f);
                slot.Rim = red;
                slot.Text = Color.white;
            }
            if (_fade < 1f) { slot.Fill.a *= _fade; slot.Rim.a *= _fade; }
            _block.SetColor(FillId, slot.Fill);
            bool gradient = _swatch2.a > 0f && slot.Fill == _swatch; // only while the swatch itself shows
            _block.SetColor(Fill2Id, gradient ? _swatch2 : slot.Fill);
            _block.SetColor(RimId, slot.Rim);
            _block.SetVector(PatternId, _pattern);
            _block.SetColor(PatternColorId, _patternColor);
            _block.SetFloat(GlyphId, _glyph);
            _block.SetColor(GlyphColorId, _style.TextSecondary);
            _block.SetVector(ClipId, _clip);
            _block.SetVector(RectId, new Vector4(_shapeSize.x, _shapeSize.y, radius, _style.RimWidth));
            _block.SetFloat(IconOnId, 0f);
            _quad.SetPropertyBlock(_block);
            if (_fade < 1f) { slot.Text.a *= _fade; }
            if (_icon != null && _iconRect != Vector4.zero)
            {
                _icon.GetPropertyBlock(_block);
                _block.SetFloat(IconOnId, 1f);
                _block.SetVector(IconId, _iconRect);
                _block.SetColor(GlyphColorId, slot.Text);
                _icon.SetPropertyBlock(_block);
            }
            if (_text.color != slot.Text) { _text.color = slot.Text; _textMeshStale = true; }
            bool message = _kind == CardKind.Title && (_textStyle == TextStyle.Caption || _textStyle == TextStyle.Body || (_textStyle == TextStyle.Section && _lines > 1));
            bool semibold = Bold || (_kind == CardKind.Title && !message) || (State == ElementState.Selected && (_kind == CardKind.Chip || _kind == CardKind.Toggle));
            TMP_FontAsset font = semibold && _fonts?.SemiBold != null ? _fonts.SemiBold : _fonts?.Regular;
            if (font != null && _text.font != font) { SetFont(font); _textMeshStale = true; }
            FontStyles weight = semibold && _fonts?.SemiBold == null ? FontStyles.Bold : FontStyles.Normal;
            if (_text.fontStyle != weight) { _text.fontStyle = weight; _textMeshStale = true; }
            float mm = State == ElementState.Selected && SelectedTextMm > 0f ? SelectedTextMm : _kind == CardKind.Chip ? _textMm
                : _kind == CardKind.Toggle ? BodyMm : _kind == CardKind.Title ? (_textStyle == TextStyle.Header ? TitleMm : _textStyle == TextStyle.Body ? BodyMm : SectionMm) : CaptionMm;
            // Text fits before it is ever cut (device batch 10: no "…"): it shrinks down to the floor size first, and only
            // a string still too long at the floor ends in an ellipsis (content shortens those; see the audit test).
            // A row's body text keeps its size (its page fits the words): TMP's auto-size steps in 0.05 font units, more
            // than our whole 12-14 mm range, and dropped a two-line row straight to 12 mm although it fitted at 14.
            float floor = _kind == CardKind.Title && _textStyle == TextStyle.Body ? mm : MinTextMm > 0f ? MinTextMm : FloorTextMm;
            if (mm > floor)
            {
                if (!_text.enableAutoSizing) { _text.enableAutoSizing = true; _textMeshStale = true; }
                if (!Mathf.Approximately(_text.fontSizeMin, FontSize(floor))) { _text.fontSizeMin = FontSize(floor); _textMeshStale = true; }
                if (!Mathf.Approximately(_text.fontSizeMax, FontSize(mm))) { _text.fontSizeMax = FontSize(mm); _textMeshStale = true; }
                // Auto-sizing starts from the base size. TMP steps in 0.05 font units, more than our whole 12-14 mm range, so
                // a base below the maximum never grows back (the keyboard's line stayed at 12 mm): start at the maximum.
                if (!Mathf.Approximately(_text.fontSize, FontSize(mm))) { _text.fontSize = FontSize(mm); _textMeshStale = true; }
            }
            else
            {
                if (_text.enableAutoSizing) { _text.enableAutoSizing = false; _textMeshStale = true; }
                if (!Mathf.Approximately(_text.fontSize, FontSize(mm))) { _text.fontSize = FontSize(mm); _textMeshStale = true; }
            }
            // Spec v4 section 7: hover lifts 1 mm toward the user; pressed is 97% scale.
            Root.localPosition = new Vector3(Centre.x, Centre.y, (_surface ? 0f : -0.001f) - (State == ElementState.Hover ? 0.001f : 0f));
            Root.localScale = State == ElementState.Pressed ? new Vector3(0.97f, 0.97f, 1f) : Vector3.one;
            if (_clip.w > 0f || _textClipped) { ClipText(); _textClipped = _clip.w > 0f; }
        }
    }
}

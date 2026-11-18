using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// How a card looks and sits (spec v4 hierarchy): Chip = level-3 pill; Title = level-2 section title
    /// (text only, not pressable); Toggle = level-2 text toggle (underline when selected); Swatch and
    /// Disc = level-3 34 mm square or disc with the label below (selected = accent ring); Card = a level-3
    /// furniture card (thumbnail on top, name below in up to two lines; selected = accent ring).
    /// </summary>
    public enum CardKind { Chip, Title, Toggle, Swatch, Disc, Card }

    /// <summary>Glyphs the glass shader draws (more come with content's icon atlas).</summary>
    public static class Glyphs { public const int None = 0, Eye = 1; }

    /// <summary>What one card shows. Filled by a page; read only when the panel re-describes.</summary>
    public struct CardInfo
    {
        public string Title;
        public CardKind Kind;
        /// <summary>Rows stack top to bottom; cards in one row fill the page's columns left to right in order.</summary>
        public int Row;
        /// <summary>Optional colour swatch (a finish or mood colour); alpha 0 = a plain text card.</summary>
        public Color Swatch;
        /// <summary>Optional bottom colour of a gradient swatch (moods); alpha 0 = a flat swatch.</summary>
        public Color Swatch2;
        /// <summary>Finish pattern lines: horizontal count x, vertical count y, width z (0-1 of the disc); zero = none.</summary>
        public Vector4 Pattern;
        public Color PatternColor;
        /// <summary>A glyph drawn in the disc: <see cref="Glyphs.Eye"/> on "Real".</summary>
        public int Glyph;
        /// <summary>Thumbnail atlas rect as _BaseMap_ST (scale x y, offset z w); zero = no thumbnail.</summary>
        public Vector4 Thumb;
        /// <summary>Width (m) for a content-hugging item (<see cref="PanelElement.MeasureWidth"/>); 0 = the column width.</summary>
        public float Width;
        /// <summary>The row starts at the right edge (a pager); its items keep their order.</summary>
        public bool AlignRight;
        /// <summary>The row is centred (the furniture pager, device batch 10); its items keep their order.</summary>
        public bool AlignCenter;
        /// <summary>A control that must read on the glass (the pager's chevrons): the dark pill when live, a visible fill when off.</summary>
        public bool Strong;
        /// <summary>A destructive action (Remove): quiet red text.</summary>
        public bool Danger;
        /// <summary>An icon from <see cref="PanelIcons"/> (atlas rect; zero = none), and where: 0 centred, -1 left, 1 right of the text.</summary>
        public Vector4 Icon;
        public int IconSide;
        public bool Selected;
        public bool Disabled;
        /// <summary>A title that may wrap to this many lines (the row grows); 0 or 1 = one line with an ellipsis.</summary>
        public int Lines;
        /// <summary>A title that is the page's header (14 semibold, full ink) instead of a section name (12 semibold).</summary>
        public bool Header;
        /// <summary>A title that is a caption or hint (12 regular) instead of a section name.</summary>
        public bool Caption;
        /// <summary>A title that is a row's body text (14 regular, full ink): a settings row's name.</summary>
        public bool Body;
        /// <summary>This item's height (m); 0 = its kind's own. A row is as tall as its tallest item.</summary>
        public float Height;
        /// <summary>When set on a row's first item, the gap (m) under that row instead of its kind's own (0 = rows touch).</summary>
        public bool RowGapSet;
        public float RowGapBelow;
    }

    /// <summary>
    /// One tab's content: a grid of cards. A page adapts an existing tool (style menu, outside row, ask,
    /// hide) to the panel: Describe reads that tool's data and Press calls that tool's action, so no
    /// core logic is duplicated. Call <see cref="ToolPanel.Invalidate"/> when the data changes.
    /// </summary>
    /// <summary>A page whose cards can be dragged off the panel (the Add tab: drag a piece onto the floor).</summary>
    public interface IPanelDragOut
    {
        /// <summary>Card <paramref name="index"/> was pinched and dragged off the panel; true to take it (the press ends).</summary>
        bool DragOut(int index);
    }

    public interface IPanelPage
    {
        int Count { get; }
        int Columns { get; }

        /// <summary>
        /// Fills <paramref name="card"/> for card <paramref name="index"/>. Called on a change and whenever a
        /// card's hover or press ends (to restore its state), so it must not allocate: return cached strings.
        /// </summary>
        void Describe(int index, ref CardInfo card);

        /// <summary>Acts on card <paramref name="index"/>; false = refused (the card flashes red).</summary>
        bool Press(int index);
    }

    /// <summary>Optional: keeps the panel in front of walls (MR implements it with the room outline).</summary>
    /// <summary>The tops of real or virtual furniture under a point, so the panel never sinks into a desk.</summary>
    public interface IPanelSurfaces
    {
        /// <summary>The highest furniture top (world y) under <paramref name="point"/>'s floor position; false when none.</summary>
        bool TopUnder(Vector3 point, out float top);
    }

    public interface IPanelReach
    {
        /// <summary>The reach (m, at most <paramref name="maxReach"/>) at which a panel <paramref name="halfWidth"/> wide fits in the room.</summary>
        float Reach(Vector3 head, Vector3 forward, float halfWidth, float maxReach);
    }
}

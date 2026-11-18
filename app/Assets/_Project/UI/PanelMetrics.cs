using System;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>Layout and placement tokens (metres, degrees, seconds); defaults follow UI spec v4 (#135), a new spec only changes values.</summary>
    [Serializable]
    public struct PanelMetrics
    {
        public Vector2 Size;
        [Tooltip("Size.y is the tallest the panel grows; it fits its content below that.")]
        public float Padding, Gap, StripTop, StripHeight, TabTop, TabHeight, BodyTop, CardHeight, HitPad, PokeDepth;
        [Tooltip("Row heights by kind (spec v4 sections 5, 7.1, 7.2) and the gap below each row.")] public float TitleHeight, ToggleHeight, SwatchHeight, TileHeight, TitleGap, RowGap;
        [Tooltip("Seat: top edge this far ahead of and below the eyes; Tilt only places the seat (the panel always faces the eyes).")] public float Ahead, Below, Tilt;
        [Tooltip("Lazy follow: ease back when outside these limits of the seat (degrees, metres, seconds).")] public float MaxYaw, MaxPitch, MaxDistance, LostDistance, EaseSeconds;

        public static PanelMetrics Default => new PanelMetrics
        {
            Size = new Vector2(0.30f, 0.46f), Padding = 0.016f, Gap = 0.008f, StripTop = 0.008f, StripHeight = 0.028f,
            TabTop = 0.040f, TabHeight = 0.036f, BodyTop = 0.084f, CardHeight = 0.036f, HitPad = 0.004f, PokeDepth = 0.015f,
            TitleHeight = 0.016f, ToggleHeight = 0.024f, SwatchHeight = 0.056f, TileHeight = 0.092f, TitleGap = 0.006f, RowGap = 0.012f,
            Ahead = 0.65f, Below = 0.15f, Tilt = 27f, // 0.65 m: out of the way seated (0.45 filled half the view); rays are primary there
            MaxYaw = 20f, MaxPitch = 15f, MaxDistance = 1.0f, LostDistance = 3f, EaseSeconds = 0.6f,
        };
    }
}

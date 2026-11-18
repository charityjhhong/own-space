using System;
using UnityEngine;

namespace OwnSpace.UI
{
    public enum ElementState { Normal, Hover, Selected, Pressed, Disabled, Refused }

    /// <summary>
    /// A theme's tokens: one slot per element state plus the panel surface (fill and rim in straight
    /// alpha for OwnSpace/PanelGlass, and text), secondary text, accent, corner radius and rim width.
    /// Defaults are the light (white glass) theme; the spec's final values go into PanelStyle assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Own Space/Panel Style")]
    public sealed class PanelStyle : ScriptableObject
    {
        [Serializable]
        public struct Slot
        {
            public Color Fill, Rim, Text;
            public Slot(Color fill, Color rim, Color text) { Fill = fill; Rim = rim; Text = text; }
        }

        // Spec v4 light tokens: ink #0E1520, graphite #2C2F36, accent #0A74E0, refused #E5483B.
        static Color Ink(float a) => new Color(0.055f, 0.082f, 0.125f, a);
        static readonly Color Graphite = new Color(0.173f, 0.184f, 0.212f), Red = new Color(0.898f, 0.282f, 0.231f);

        // The surface is 88% (was 70%): text stayed hard to read over a bright laptop screen (device shot 202311).
        // Chips are fill only (mockups round 2): no rim except the chosen state's ring and a refusal.
        public Slot Surface = new Slot(new Color(1f, 1f, 1f, SurfaceAlpha), new Color(1f, 1f, 1f, 0.80f), Ink(1f));
        public const float SurfaceAlpha = 0.88f;
        public Slot Normal = new Slot(Ink(0.07f), Color.clear, Ink(1f));
        public Slot Hover = new Slot(Ink(0.17f), Color.clear, Ink(1f));
        public Slot Selected = new Slot(Graphite, new Color(0.039f, 0.455f, 0.878f), Color.white);
        public Slot Pressed = new Slot(Ink(0.22f), Color.clear, Ink(1f));
        public Slot Disabled = new Slot(Ink(0.03f), Color.clear, Ink(0.40f));
        public Slot Refused = new Slot(new Color(Red.r, Red.g, Red.b, 0.12f), Red, Ink(1f));
        public Color TextSecondary = Ink(0.76f), Accent = new Color(0.039f, 0.455f, 0.878f);

        [Tooltip("Corner radius (m) of chips and tabs; clamped to half the height, so 0.018 makes 36 mm pills.")] public float CornerRadius = 0.018f;
        [Tooltip("Corner radius (m) of the panel surface (spec: 28 mm).")] public float SurfaceRadius = 0.028f;
        [Tooltip("Rim width (m).")] public float RimWidth = 0.0008f;

        static PanelStyle _default;

        /// <summary>The built-in defaults (created once).</summary>
        public static PanelStyle Default => _default != null ? _default : (_default = CreateInstance<PanelStyle>());

        public Slot Get(ElementState state) => state switch
        {
            ElementState.Hover => Hover, ElementState.Selected => Selected, ElementState.Pressed => Pressed,
            ElementState.Disabled => Disabled, ElementState.Refused => Refused, _ => Normal,
        };
    }
}

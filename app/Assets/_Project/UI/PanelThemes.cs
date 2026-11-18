using UnityEngine;

namespace OwnSpace.UI
{
    public enum PanelTheme { Light, Dark }

    /// <summary>Built-in themes: light (white glass, default) is PanelStyle.Default; dark (smoked glass) is made once here.</summary>
    public static class PanelThemes
    {
        static PanelStyle _dark;
        public const float DarkSurfaceAlpha = 0.90f;

        public static PanelStyle DarkDefault => _dark != null ? _dark : (_dark = CreateDark());

        static PanelStyle CreateDark()
        {
            var s = ScriptableObject.CreateInstance<PanelStyle>();
            // Spec v4 dark tokens: tint #0F1826 at 80%, white text, accent #6DB6FF.
            Color text = Color.white, ink = new Color(0.055f, 0.082f, 0.125f), accent = new Color(0.427f, 0.714f, 1f);
            Color W(float a) => new Color(1f, 1f, 1f, a);
            s.Surface = new PanelStyle.Slot(new Color(0.059f, 0.094f, 0.149f, DarkSurfaceAlpha), W(0.35f), text); // 90% (was 80%), as the light theme's rise
            s.Normal = new PanelStyle.Slot(W(0.14f), Color.clear, text); // fill only: no rim on chips
            s.Hover = new PanelStyle.Slot(W(0.26f), Color.clear, text);
            s.Selected = new PanelStyle.Slot(Color.white, accent, ink);
            s.Pressed = new PanelStyle.Slot(W(0.32f), Color.clear, text);
            s.Disabled = new PanelStyle.Slot(W(0.05f), Color.clear, W(0.40f));
            s.Refused = new PanelStyle.Slot(new Color(0.898f, 0.282f, 0.231f, 0.12f), new Color(0.898f, 0.282f, 0.231f), text);
            s.TextSecondary = W(0.82f);
            s.Accent = accent;
            return s;
        }
    }
}

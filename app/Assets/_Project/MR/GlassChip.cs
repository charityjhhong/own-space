using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// A short label in the tool panel's look: a pill on the panel's glass material with Inter text, sized to its words,
    /// facing the user. Device batch 9: the raw white TextMesh over a piece looked crude. Device batch 10: every in-room
    /// text uses it.
    /// <list type="bullet">
    /// <item>The theme's dark pill reads over any room.</item>
    /// <item>The red variant is for refusals ("Crosses your zone").</item>
    /// <item>Lines split by '\n' stack.</item>
    /// </list>
    /// Built once; Show only moves it and swaps the text, and sizes it only when the text changes.
    /// </summary>
    public sealed class GlassChip
    {
        const float LineHeight = 0.022f, PadY = 0.008f, PadX = 0.024f;
        public static readonly Color RefusalFill = new Color(0.78f, 0.2f, 0.18f, 0.94f), RefusalText = Color.white;

        readonly Transform _root;
        readonly PanelElement _chip;
        string _text;

        public GlassChip(Transform parent, ToolPanel panel, string name, bool refusal = false)
        {
            _root = new GameObject(name).transform;
            _root.SetParent(parent, false);
            _chip = new PanelElement(_root, panel.GlassMaterial, panel.CurrentStyle, panel.Fonts, "Chip", 13f);
            _chip.SetState(ElementState.Selected); // the theme's dark pill: reads over any room
            if (refusal) _chip.SetColors(RefusalFill, RefusalText);
            _root.gameObject.SetActive(false);
        }

        public bool Shown => _root.gameObject.activeSelf;
        internal string Text => _text;

        /// <summary>The number of lines in <paramref name="text"/> ('\n' separated).</summary>
        public static int LineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int lines = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lines++;
            return lines;
        }

        void SetText(string text)
        {
            if (ReferenceEquals(text, _text)) return;
            _text = text;
            _chip.SetText(text);
            int lines = LineCount(text);
            float height = LineHeight * lines + PadY;
            _chip.Place(Vector2.zero, new Vector2(_chip.PreferredWidth(text) + PadX, height), lines > 1 ? 0.012f : height * 0.5f);
        }

        /// <summary>Shows the chip at <paramref name="at"/> facing <paramref name="head"/>, about the same size in view at any distance.</summary>
        public void Show(string text, Vector3 at, Vector3 head)
        {
            SetText(text);
            Vector3 away = at - head;
            _root.SetPositionAndRotation(at, away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away, Vector3.up) : _root.rotation);
            // About the same size in view at any distance (the panel's chips at 0.5 m).
            _root.localScale = Vector3.one * Mathf.Clamp(away.magnitude / 0.5f, 0.8f, 4f);
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        /// <summary>Shows the chip at a fixed spot in its parent (the parent faces the user and scales itself).</summary>
        public void ShowLocal(string text, Vector3 localPosition)
        {
            SetText(text);
            _root.localPosition = localPosition;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        public void Hide() { if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false); }
    }
}

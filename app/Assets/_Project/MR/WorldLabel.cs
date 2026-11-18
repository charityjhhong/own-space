using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// A short line of in-room text that faces the user: a refusal reason for a couple of seconds, a hovered name, a hint.
    /// Device batch 10: no raw world text. Once the tool panel is known (<see cref="UsePanel"/>, from PanelHost.Awake), it is
    /// a <see cref="GlassChip"/> in the panel's look: Inter on the glass pill, red for refusals. The legacy TextMesh only
    /// draws when there is no panel at all (some tests). Strings come from constants or the catalog, so showing one
    /// allocates nothing. Call <see cref="Tick"/> every frame.
    /// </summary>
    public sealed class WorldLabel
    {
        static ToolPanel s_panel;

        /// <summary>The panel whose glass, fonts and theme every label uses (PanelHost, in Awake).</summary>
        public static void UsePanel(ToolPanel panel) => s_panel = panel;
        internal static ToolPanel Panel => s_panel != null ? s_panel : null;

        readonly Transform _parent;
        readonly string _name;
        readonly bool _refusal;
        readonly Color _color;
        GlassChip _chip;
        TextMesh _text;
        GameObject _go;
        float _hideAt = float.MaxValue;

        public WorldLabel(Transform parent, string name, Color color, bool refusal = false)
        {
            _parent = parent;
            _name = name;
            _color = color;
            _refusal = refusal;
        }

        /// <summary>Whether it draws as a glass chip (false only without a panel).</summary>
        public bool IsGlass => Build() && _chip != null;

        bool Build()
        {
            if (_chip != null || _go != null) return true;
            if (_parent == null) return false;
            if (Panel != null) { _chip = new GlassChip(_parent, Panel, _name, _refusal); return true; }
            _go = new GameObject(_name);
            _go.transform.SetParent(_parent, false);
            _text = _go.AddComponent<TextMesh>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _go.GetComponent<MeshRenderer>().sharedMaterial = _text.font.material;
            _text.anchor = TextAnchor.UpperCenter;
            _text.alignment = TextAlignment.Center;
            _text.characterSize = 0.006f;
            _text.fontSize = 48;
            _text.color = _color;
            _go.SetActive(false);
            return true;
        }

        /// <summary>Shows <paramref name="text"/> at <paramref name="at"/>, facing <paramref name="viewer"/>; hides after <paramref name="seconds"/> (0 = until hidden).</summary>
        public void Show(string text, Vector3 at, Vector3 viewer, float seconds = 0f)
        {
            if (!Build()) return;
            _hideAt = seconds > 0f ? Time.time + seconds : float.MaxValue;
            if (_chip != null) { _chip.Show(text, at, viewer); return; }
            if (!ReferenceEquals(_text.text, text)) _text.text = text;
            Vector3 away = at - viewer;
            away.y = 0f;
            _go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away.sqrMagnitude > 1e-6f ? away : Vector3.forward, Vector3.up));
            // Readable at any distance: about the same size in view.
            _go.transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(at, viewer) / 0.5f, 0.8f, 4f);
            _go.SetActive(true);
        }

        public void Hide()
        {
            _chip?.Hide();
            if (_go != null && _go.activeSelf) _go.SetActive(false);
            _hideAt = float.MaxValue;
        }

        public bool Shown => _chip != null ? _chip.Shown : _go != null && _go.activeSelf;

        public void Tick()
        {
            if (Time.time >= _hideAt) Hide();
        }
    }

    /// <summary>Short reasons for core's refusals, shown next to the refused tile or piece.</summary>
    public static class RefusalText
    {
        public const string CoversOpening = "Would cover a window or door";

        public static string Of(PlacementStatus status)
        {
            switch (status)
            {
                case PlacementStatus.NotForOutside: return "Not for outside";
                case PlacementStatus.OutsideRoom: return "Outside the room";
                case PlacementStatus.InMyZone: return "Crosses your zone";
                case PlacementStatus.BlocksDoor: return "Too close to the door";
                case PlacementStatus.BlocksWindow: return "Blocks a window";
                case PlacementStatus.Overlaps: return "Overlaps";
                case PlacementStatus.InvalidPose: return "Can't stand there";
                default: return "Can't place it";
            }
        }

        public static string Of(RedesignOutcome outcome, FitStatus fit)
        {
            if (outcome == RedesignOutcome.DoesNotFit && fit == FitStatus.NeedsTooMuchStretch) return "Too small for this spot";
            return Of(outcome);
        }

        public static string Of(RedesignOutcome outcome)
        {
            switch (outcome)
            {
                case RedesignOutcome.OutsideMyZone: return "Outside your zone";
                case RedesignOutcome.DoesNotFit: return "Doesn't fit here";
                default: return "Can't apply it";
            }
        }

        public static string Of(HideStatus status)
        {
            switch (status)
            {
                case HideStatus.InvalidSize: return "Too small";
                case HideStatus.TooLarge: return "Too large";
                case HideStatus.OutsideMyZone: return "Outside your zone";
                case HideStatus.OutsideRoom: return "Outside the room";
                case HideStatus.CoversOpening: return CoversOpening;
                case HideStatus.CoversUsableSurface: return "Covers a surface you use";
                case HideStatus.NotACoverProp: return "Can't cover with this";
                default: return "Can't hide it";
            }
        }
    }
}

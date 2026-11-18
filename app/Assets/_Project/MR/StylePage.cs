using System;
using System.Collections.Generic;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// The panel's Style tab (spec v4 section 7.1): "Apply to" with the My zone / Outside text toggle, then
    /// Floor, Wall and Mood, each a section title and one row of five circles: Real plus four options. Choices go through core's <see cref="DesignSession"/> exactly as the old style menu did,
    /// and only a real change joins the undo history. All strings are built once (Describe never allocates).
    /// </summary>
    public sealed class StylePage : IPanelPage
    {
        /// <summary>One choice: an id ("" = keep the real surface / no mood), its short name and colour.</summary>
        public readonly struct Option
        {
            public readonly string Id, Name;
            public readonly Color Colour, Colour2, PatternColour;
            public readonly Vector4 Pattern;
            public Option(string id, string name, Color colour, Color colour2 = default, Vector4 pattern = default, Color patternColour = default)
            { Id = id; Name = name; Colour = colour; Colour2 = colour2; Pattern = pattern; PatternColour = patternColour; }
        }

        public const int Choices = 5;
        public const string PlainLabel = "Plain", FloorMine = "Floor", FloorOutside = "Floor", // which room it applies to is already set by Apply to
            WallMine = "Wall", WallOutside = "Wall";
        const float ApplyWidth = 0.072f, SegmentWidth = 0.078f; // metres; fit the 14 mm words with room to spare (the row is 244 of 268)
        const int ApplyTitle = 0, Mine = 1, Outside = 2, FloorTitle = 3, Floor = 4, WallTitle = Floor + Choices,
            Wall = WallTitle + 1, MoodTitle = Wall + Choices, Mood = MoodTitle + 1, Total = Mood + Choices;
        readonly DesignSession _session;
        readonly Action _noteUserChange;
        readonly Option[] _floor, _wall, _mood;

        /// <summary>Where floor and wall choices apply; mood is always the whole room.</summary>
        // One choice for the whole room (the human, 8fdbcda: "floor and wall don't need a My zone / Outside split"): the page
        // shows and sets the outside choice, and every pick writes both zones. Plain = the defaults (outside plain shell, my
        // zone real). The "Apply to" row is hidden (its indices stay so nothing else shifts).
        public DesignZone Zone { get; private set; } = DesignZone.Outside;

        public int Count => Total;
        public int Columns => Choices;

        /// <summary>Notes one user change made of n session steps (DesignStore.NoteUserChange(n)); else noteUserChange is used.</summary>
        public Action<int> NoteSteps { get; set; }

        /// <param name="noteUserChange">Called once per real change (DesignStore.NoteUserChange).</param>
        public StylePage(DesignSession session, Action noteUserChange, IReadOnlyList<Option> floors, IReadOnlyList<Option> walls, IReadOnlyList<Option> moods)
        {
            _session = session;
            _noteUserChange = noteUserChange;
            _floor = Row(floors);
            _wall = Row(walls);
            _mood = Row(moods);
        }

        /// <summary>Real plus the first four options.</summary>
        static Option[] Row(IReadOnlyList<Option> options)
        {
            var row = new Option[Choices];
            row[0] = new Option("", "Real", Color.clear);
            for (int i = 1; i < Choices; i++) row[i] = options != null && i - 1 < options.Count ? options[i - 1] : new Option(null, "", Color.clear);
            return row;
        }

        public void Describe(int index, ref CardInfo card)
        {
            switch (index)
            {
                // One line, sized to the words: "Apply to" then the two segments (no column clips "My zone").
                case ApplyTitle: case Mine: case Outside:
                    // Hidden: no row at all (the room has one floor and one wall choice).
                    card.Kind = CardKind.Title; card.Title = ""; card.Row = 0; card.Width = 0.001f; card.Height = 0.0001f;
                    card.RowGapSet = true; card.RowGapBelow = 0f;
                    return;
                // Which room the choices change (device batch 8: "Real" outside still showed white virtual walls).
                case FloorTitle: card.Title = Zone == DesignZone.Mine ? FloorMine : FloorOutside; card.Kind = CardKind.Title; card.Row = 1; return;
                case WallTitle: card.Title = Zone == DesignZone.Mine ? WallMine : WallOutside; card.Kind = CardKind.Title; card.Row = 3; return;
                case MoodTitle: card.Title = "Mood"; card.Kind = CardKind.Title; card.Row = 5; return;
            }
            Locate(index, out Option[] row, out int slot, out int line);
            Option o = row[slot];
            // Outside is always virtual: its default is the plain shell, not "Real" (no eye: nothing real shows there).
            bool plain = o.Id == "" && row != _mood && Zone == DesignZone.Outside;
            card.Title = plain ? PlainLabel : o.Name;
            card.Kind = CardKind.Disc; // every option is a circle (content's v4 delta)
            card.Row = line;
            card.Swatch = plain ? new Color(0.86f, 0.84f, 0.8f) : o.Colour; // the shell's default wall colour
            card.Swatch2 = o.Colour2; // moods: the spec's two-colour time-of-day gradient
            card.Pattern = o.Pattern;
            card.PatternColor = o.PatternColour;
            card.Glyph = o.Id == "" && !plain ? Glyphs.Eye : Glyphs.None; // Real: the room shows through, the eye says "as it is"
            card.Disabled = o.Id == null; // fewer than four options
            card.Selected = o.Id != null && Same(o.Id, Current(row));
        }

        public bool Press(int index)
        {
            if (index == Mine || index == Outside) return false; // hidden
            if (index == ApplyTitle || index == FloorTitle || index == WallTitle || index == MoodTitle) return false;
            Locate(index, out Option[] row, out int slot, out _);
            string id = row[slot].Id;
            if (id == null) return false;
            if (row != _mood) return PressBoth(row == _floor ? FinishSurface.Floor : FinishSurface.Wall, id);
            string before = Current(row);
            _session.SetMood(id);
            string after = Current(row);
            if (!Same(before, after)) _noteUserChange?.Invoke();
            return Same(after, id); // core refused an unknown id: show it as refused
        }

        // The floor is one material everywhere (the human, 8fdbcda: "the whole floor must be one and the same material"):
        // a pick from My zone or Outside sets both; Plain ("") puts both back (my zone real, the outside the plain shell).
        // One undo step for both.
        bool PressBoth(FinishSurface surface, string id)
        {
            int steps = SetBoth(_session, surface, id);
            if (steps > 0) { if (NoteSteps != null) NoteSteps(steps); else _noteUserChange?.Invoke(); }
            DesignState s = _session.State;
            return Same(Of(s.myZoneFinish, surface), id) && Same(Of(s.outsideFinish, surface), id);
        }

        static string Of(ZoneFinish f, FinishSurface surface) => surface == FinishSurface.Floor ? f.floorFinishId : f.wallFinishId;

        /// <summary>Sets one finish on both zones (pure over the session, for tests); returns the session steps it took (0-2).</summary>
        public static int SetBoth(DesignSession session, FinishSurface surface, string id)
        {
            DesignState s = session.State;
            string mine = Of(s.myZoneFinish, surface), outside = Of(s.outsideFinish, surface);
            session.SetFinish(DesignZone.Mine, surface, id);
            session.SetFinish(DesignZone.Outside, surface, id);
            s = session.State;
            return (Same(mine, Of(s.myZoneFinish, surface)) ? 0 : 1) + (Same(outside, Of(s.outsideFinish, surface)) ? 0 : 1);
        }

        /// <summary>
        /// A design saved with different zone and outside finishes takes the outside one for both (one room); the steps taken.
        /// </summary>
        public static int Unify(DesignSession session)
        {
            DesignState s = session.State;
            int steps = 0;
            if (!Same(s.myZoneFinish.floorFinishId, s.outsideFinish.floorFinishId)) steps += SetBoth(session, FinishSurface.Floor, s.outsideFinish.floorFinishId);
            s = session.State;
            if (!Same(s.myZoneFinish.wallFinishId, s.outsideFinish.wallFinishId)) steps += SetBoth(session, FinishSurface.Wall, s.outsideFinish.wallFinishId);
            return steps;
        }

        void Locate(int index, out Option[] row, out int slot, out int line)
        {
            if (index >= Mood) { row = _mood; slot = index - Mood; line = 6; }
            else if (index >= Wall) { row = _wall; slot = index - Wall; line = 4; }
            else { row = _floor; slot = index - Floor; line = 2; }
        }

        string Current(Option[] row)
        {
            DesignState s = _session.State;
            if (row == _mood) return s.moodPresetId;
            ZoneFinish f = Zone == DesignZone.Mine ? s.myZoneFinish : s.outsideFinish;
            return row == _floor ? f.floorFinishId : f.wallFinishId;
        }

        /// <summary>The label: content's short name, else the first word of the name ("Oak planks" -> "Oak").</summary>
        public static string Short(string id, string name, string shortName)
        {
            if (!string.IsNullOrEmpty(shortName)) return shortName;
            if (string.IsNullOrEmpty(name)) return "";
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        /// <summary>Line pattern for a finish (spec: planks = 3 rows; tiles = 3 columns + 1 row; stripes = 5 columns).</summary>
        public static Vector4 PatternOf(string pattern) => pattern switch
        {
            "planks" => new Vector4(3f, 0f, 0.024f, 0f),
            "tiles" => new Vector4(1f, 3f, 0.024f, 0f),
            "stripes" => new Vector4(0f, 5f, 0.032f, 0f),
            _ => Vector4.zero,
        };

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.clear;

        [Serializable] sealed class FinishFile { public FinishJson[] finishes; }
        [Serializable] sealed class FinishJson { public string id, name, shortName, surface, baseColor, accentColor, pattern; }
        [Serializable] sealed class MoodFile { public MoodJson[] presets; }
        [Serializable] sealed class MoodJson { public string id, name, shortName, gradientTop, gradientBottom, windowColor; }

        static bool Same(string a, string b) => (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) || a == b;

        /// <summary>Floor or wall options from content's <c>finishes.json</c>: base colour, accent-coloured pattern, short name.</summary>
        public static List<Option> Finishes(string finishesJson, string surface)
        {
            var list = new List<Option>();
            FinishFile file = string.IsNullOrEmpty(finishesJson) ? null : JsonUtility.FromJson<FinishFile>(finishesJson);
            for (int i = 0; file?.finishes != null && i < file.finishes.Length; i++)
            {
                FinishJson f = file.finishes[i];
                if (f.surface != surface && f.surface != "both") continue;
                list.Add(new Option(f.id, Short(f.id, f.name, f.shortName), Hex(f.baseColor), default, PatternOf(f.pattern), Hex(f.accentColor)));
            }
            return list;
        }

        /// <summary>
        /// Mood options from content's <c>mood-presets.json</c>: short name and the two-colour gradient
        /// (gradientTop to gradientBottom); a preset without a gradient is a flat disc in its window colour.
        /// </summary>
        public static List<Option> Moods(string moodPresetsJson)
        {
            var list = new List<Option>();
            MoodFile file = string.IsNullOrEmpty(moodPresetsJson) ? null : JsonUtility.FromJson<MoodFile>(moodPresetsJson);
            for (int i = 0; file?.presets != null && i < file.presets.Length; i++)
            {
                MoodJson m = file.presets[i];
                Color flat = Hex(m.windowColor);
                Color top = string.IsNullOrEmpty(m.gradientTop) ? flat : Hex(m.gradientTop);
                Color bottom = string.IsNullOrEmpty(m.gradientBottom) ? top : Hex(m.gradientBottom);
                list.Add(new Option(m.id, Short(m.id, m.name, m.shortName), top, bottom));
            }
            return list;
        }
    }
}

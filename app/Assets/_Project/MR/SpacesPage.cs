using System;
using System.Collections.Generic;
using OwnSpace.UI;

namespace OwnSpace.MR
{
    /// <summary>
    /// The Spaces chooser (Saved spaces), a sheet opened from Settings › My space.
    /// <list type="bullet">
    /// <item>List: "Save this room" (when a real scan is in use), then one card per space: "This room", every saved space
    /// by name with its date and a small "…" for Rename or Delete. "Sample room" comes last, only when this room has no
    /// scan (with a scan it is not offered). The current one is marked;
    /// pinching a card goes there.</item>
    /// <item>Save: quick names as chips (Home, Dorm, Study room, Hotel, Office) or "Type a name" (the on-panel keyboard).</item>
    /// <item>"…": Go there, Rename (keyboard), Delete (asks first).</item>
    /// </list>
    /// Items are rebuilt on a change only, so Describe never allocates.
    /// </summary>
    public sealed class SpacesPage : IPanelPage
    {
        public const string Title = "Spaces", ThisRoom = "This room", CloseLabel = "Close", SaveLabel = "Save this room",
            SaveTitle = "Save this room as", TypeLabel = "Type a name", CancelLabel = "Cancel", MoreLabel = "…",
            GoLabel = "Go there", RenameLabel = "Rename", DeleteLabel = "Delete", BackLabel = "Back";
        public static readonly string[] QuickNames = { "Home", "Dorm", "Study room", "Hotel", "Office" };
        public const string SampleId = SampleRoom.Id, ThisRoomId = "";
        const int MaxItems = 24;
        const float FullWidth = 0.268f, Gap = 0.008f;

        enum Mode { List, Save, Menu, ConfirmDelete }
        enum Act { None, Save, Go, More, QuickName, Type, Cancel, Rename, Delete, DoDelete, Back, Close }
        struct Item { public CardKind Kind; public int Row; public string Title; public float Width; public bool Selected, Danger; public Act Act; public int Arg; public int Lines; }

        readonly List<string> _ids = new List<string>(), _names = new List<string>(), _labels = new List<string>();
        readonly List<bool> _saved = new List<bool>();
        readonly Item[] _items = new Item[MaxItems];
        readonly float _moreWidth, _saveWidth, _typeWidth, _cancelWidth, _goWidth, _renameWidth, _deleteWidth, _backWidth;
        readonly float[] _quickWidths = new float[5];
        int _count, _menu = -1;
        Mode _mode;
        bool _canSave;
        string _current = ThisRoomId, _menuTitle, _confirmTitle;

        /// <summary>Goes to a space by id ("" = this room, "sample" = the sample room); false when it cannot.</summary>
        public Func<string, bool> Go { get; set; }
        /// <summary>Saves the real room under a name; false when it cannot (the host says why).</summary>
        public Func<string, bool> Save { get; set; }
        /// <summary>Opens the keyboard to type a name for saving.</summary>
        public Func<bool> TypeName { get; set; }
        /// <summary>Opens the keyboard to rename a saved space (id, current name).</summary>
        public Func<string, string, bool> Rename { get; set; }
        public Func<string, bool> Delete { get; set; }
        public Action Close { get; set; }
        /// <summary>Called when the page's items change (the panel re-describes).</summary>
        public Action Changed { get; set; }

        public SpacesPage(TMPro.TMP_FontAsset font = null)
        {
            const float pad = 0.024f;
            float W(string s) => PanelElement.MeasureWidth(s, PanelElement.BodyMm, font) + pad;
            _moreWidth = 0.036f;
            _saveWidth = W(SaveLabel); _typeWidth = W(TypeLabel); _cancelWidth = W(CancelLabel);
            _goWidth = W(GoLabel); _renameWidth = W(RenameLabel); _deleteWidth = W(DeleteLabel); _backWidth = W(BackLabel);
            for (int i = 0; i < QuickNames.Length; i++) _quickWidths[i] = W(QuickNames[i]);
            Rebuild();
        }

        /// <summary>
        /// Rebuilds the list: whether the real room is in use (and so can be saved), the saved spaces (id, name, date), and
        /// the current id. Back to the list.
        /// </summary>
        public void Refresh(bool realRoomUsable, IReadOnlyList<(string id, string name, string date)> saved, string currentId, bool offerSample = false)
        {
            _ids.Clear(); _names.Clear(); _labels.Clear(); _saved.Clear();
            _current = currentId ?? ThisRoomId;
            _canSave = realRoomUsable && _current == ThisRoomId;
            if (realRoomUsable || _current != ThisRoomId) Add(ThisRoomId, ThisRoom, ThisRoom, false);
            for (int i = 0; saved != null && i < saved.Count && _ids.Count < 11; i++)
                Add(saved[i].id, saved[i].name, string.IsNullOrEmpty(saved[i].date) ? saved[i].name : saved[i].name + ", " + saved[i].date, true);
            if (offerSample || _current == SampleId) Add(SampleId, SampleRoom.Name, SampleRoom.Name, false); // the current space is always listed
            _mode = Mode.List;
            _menu = -1;
            Rebuild();
        }

        void Add(string id, string name, string label, bool saved) { _ids.Add(id); _names.Add(name); _labels.Add(label); _saved.Add(saved); }

        /// <summary>"Nov 11" from a saved space's ISO date; "" when it cannot be read.</summary>
        public static string ShortDate(string isoUtc) =>
            DateTime.TryParse(isoUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime d)
                ? d.ToLocalTime().ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture) : "";

        void Rebuild()
        {
            _count = 0;
            switch (_mode)
            {
                case Mode.List:
                    // The same header as Settings: the title on the left, a round × on the right.
                    Put(CardKind.Title, 0, Title, FullWidth - SettingsPage.CloseWidth - Gap, Act.None);
                    Put(CardKind.Chip, 0, SettingsPage.CloseGlyph, SettingsPage.CloseWidth, Act.Close);
                    int row = 1;
                    if (_canSave) Put(CardKind.Chip, row++, SaveLabel, _saveWidth, Act.Save);
                    for (int i = 0; i < _ids.Count && _count < MaxItems - 2; i++, row++)
                    {
                        Put(CardKind.Chip, row, _labels[i], _saved[i] ? FullWidth - _moreWidth - Gap : FullWidth, Act.Go, i);
                        _items[_count - 1].Selected = _ids[i] == _current; // the current space is marked
                        if (_saved[i]) Put(CardKind.Chip, row, MoreLabel, _moreWidth, Act.More, i);
                    }
                    break;
                case Mode.Save:
                    Put(CardKind.Title, 0, SaveTitle, 0f, Act.None);
                    for (int i = 0; i < QuickNames.Length; i++) Put(CardKind.Chip, 1 + i / 3, QuickNames[i], _quickWidths[i], Act.QuickName, i);
                    Put(CardKind.Chip, 3, TypeLabel, _typeWidth, Act.Type);
                    Put(CardKind.Chip, 4, CancelLabel, _cancelWidth, Act.Cancel);
                    break;
                case Mode.Menu:
                    Put(CardKind.Title, 0, _menuTitle, 0f, Act.None);
                    Put(CardKind.Chip, 1, GoLabel, _goWidth, Act.Go, _menu);
                    Put(CardKind.Chip, 1, RenameLabel, _renameWidth, Act.Rename, _menu);
                    Put(CardKind.Chip, 1, DeleteLabel, _deleteWidth, Act.Delete, _menu);
                    _items[_count - 1].Danger = true;
                    Put(CardKind.Chip, 2, BackLabel, _backWidth, Act.Back);
                    break;
                case Mode.ConfirmDelete:
                    Put(CardKind.Title, 0, _confirmTitle, 0f, Act.None);
                    _items[_count - 1].Lines = 2;
                    Put(CardKind.Chip, 1, DeleteLabel, _deleteWidth, Act.DoDelete, _menu);
                    _items[_count - 1].Danger = true;
                    Put(CardKind.Chip, 1, CancelLabel, _cancelWidth, Act.Back);
                    break;
            }
            Changed?.Invoke();
        }

        void Put(CardKind kind, int row, string title, float width, Act act, int arg = 0)
        {
            if (_count >= MaxItems) return;
            _items[_count++] = new Item { Kind = kind, Row = row, Title = title, Width = width, Act = act, Arg = arg };
        }

        public int Count => _count;
        public int Columns => 1;
        public bool Saving => _mode == Mode.Save;

        public void Describe(int index, ref CardInfo card)
        {
            Item it = _items[index];
            card.Kind = it.Kind;
            card.Row = it.Row;
            card.Title = it.Title;
            card.Width = it.Kind == CardKind.Title && it.Width <= 0f ? FullWidth : it.Width;
            card.Selected = it.Selected;
            card.Danger = it.Danger;
            card.Lines = it.Lines;
        }

        public bool Press(int index)
        {
            if (index < 0 || index >= _count) return false;
            Item it = _items[index];
            switch (it.Act)
            {
                case Act.Close: Close?.Invoke(); return true;
                case Act.Save: _mode = Mode.Save; Rebuild(); return true;
                case Act.Cancel: case Act.Back: _mode = Mode.List; Rebuild(); return true;
                case Act.QuickName: return Save != null && Save(QuickNames[it.Arg]);
                case Act.Type: return TypeName != null && TypeName();
                case Act.Go:
                    if (_ids[it.Arg] == _current) { Close?.Invoke(); return true; } // already there: back where the chooser came from
                    return Go != null && Go(_ids[it.Arg]);
                case Act.More:
                    _menu = it.Arg; _menuTitle = _names[it.Arg]; _mode = Mode.Menu; Rebuild(); return true;
                case Act.Rename: return Rename != null && Rename(_ids[it.Arg], _names[it.Arg]);
                case Act.Delete:
                    _confirmTitle = "Delete " + _names[it.Arg] + "? This cannot be undone.";
                    _mode = Mode.ConfirmDelete; Rebuild(); return true;
                case Act.DoDelete: return Delete != null && Delete(_ids[it.Arg]);
                default: return false;
            }
        }
    }
}

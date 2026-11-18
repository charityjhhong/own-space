using System;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Owns the app's one <see cref="DesignSession"/> (core) and writes it atomically after every change, undo or
    /// redo. All design rules and undo live in the session.
    /// Saves are per room (<see cref="RoomSaves"/>): once the room is known (<see cref="MrukRoomInfo.Settled"/>) the
    /// store loads that room's save, or starts empty, and keeps every other room's save for when the user returns.
    /// Until then nothing is loaded and <see cref="RoomApplied"/> is false.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class DesignStore : MonoBehaviour
    {
        const string LogTag = "[OwnSpace.MR]";

        [SerializeField, Tooltip("The room the session checks anchors against.")]
        MrukRoomInfo _room;

        [SerializeField, Tooltip("Content/catalog.json, so the session can check outside placements.")]
        TextAsset _catalogJson;

        [SerializeField, Tooltip("Save file name under Application.persistentDataPath.")]
        string _fileName = "design.json";

        string _path, _key;
        readonly UserChanges _changes = new UserChanges();

        /// <summary>The current room's design file (null before the room is known); per-room side files sit next to it.</summary>
        public string SavePath => _path;

        /// <summary>The save file a room or visited space with <paramref name="key"/> uses.</summary>
        public string PathFor(string key) => Path.Combine(Application.persistentDataPath, RoomSaves.FileName(_fileName, key));

        /// <summary>True once the current room's save (or an empty design) is loaded.</summary>
        public bool RoomApplied => _key != null;

        public DesignSession Session { get; private set; }

        /// <summary>True when the design has a zone (restored or confirmed).</summary>
        public bool HasZone => Session != null && Session.State.zoneOutline.Count >= 3;

        void Awake()
        {
            Session = new DesignSession(_room);
            if (_catalogJson != null) Session.Catalog = CatalogLoader.Load(_catalogJson.text).Catalog;
            Session.Changed += Save;
            Session.Changed += OnSessionChanged;
            if (_room != null) _room.Changed += OnRoomChanged; // before anyone reading HasZone subscribes (their Start)
        }

        void Start() => OnRoomChanged();

        // The room is known (or changed): switch to its save. Earlier rooms' saves stay on disk.
        void OnRoomChanged()
        {
            if (_room == null || !_room.Settled) return;
            string key = _room.RoomKey;
            if (key == _key) return;
            string dir = Application.persistentDataPath;
            string path = Path.Combine(dir, RoomSaves.FileName(_fileName, key));
            if (_seed != null && key == _seedKey)
            {
                // A visit (Saved spaces) starts from the space's own design, posed around the user, every time.
                DesignStateJson.SaveToFile(path, _seed);
                _seed = null;
                _key = key;
                _path = path;
                Session.Load(path);
                _changes.Begin();
                Debug.Log($"{LogTag} Room save {key}: the visited space's design, zone points={Session.State.zoneOutline.Count}.");
                return;
            }
            bool own = key != RoomSaves.NoScanKey && File.Exists(path);
            string from = own || key == RoomSaves.NoScanKey ? path : Path.Combine(dir, _fileName); // a new scanned room may adopt the old single save
            _key = key;
            _path = path;
            DesignStateJson.TryLoadFromFile(from, out DesignState saved);
            Vector3 head = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            bool restore = saved != null && RoomSaves.Restore(own, saved.zoneOutline, new Vec2(head.x, head.z));
            if (!restore) { DesignStateJson.SaveToFile(path, new DesignState()); VirtualExtent.Clear(path); } // a fresh design: no old extent
            else if (from != path) DesignStateJson.SaveToFile(path, saved);    // adopt it under this room's key
            Session.Load(path); // raises Changed (listeners refresh); clears undo
            // One room: an older save with different zone and outside finishes takes the outside one for both.
            if (StylePage.Unify(Session) > 0) Debug.Log($"{LogTag} Room save {key}: zone finishes set to the outside ones (one room).");
            _changes.Begin();
            Debug.Log($"{LogTag} Room save {key}: {(restore ? $"restored zone points={Session.State.zoneOutline.Count}, choices={Session.State.anchorChoices.Count}" : "new design")}.");
        }

        DesignState _seed;
        string _seedKey;

        /// <summary>The design the next room with <paramref name="key"/> starts from (a visit), replacing its save.</summary>
        public void SeedNext(string key, DesignState state) { _seedKey = key; _seed = state; }

        void OnSessionChanged() => _changes.SessionChanged(Session);

        /// <summary>
        /// Reset my design: this room's save becomes an empty design (zone, finishes, mood, pieces, redesigns, hide
        /// boxes, covers) and undo starts over. Other rooms' saves stay. False before the room is known.
        /// </summary>
        public bool ResetRoom()
        {
            if (_path == null) return false;
            RoomSaves.Reset(_path, Session.State.zoneOutline); // the zone stays (changing it is Edit my zone)
            AnchorAdjusts.Clear(_path); // the fine-tuning of redesigned pieces goes with the design
            CoverAdjusts.Clear(_path); // and so do the covers' sizes
            VirtualExtent.Clear(_path); // and the extended virtual room (it is worked out again)
            RoomReset?.Invoke();
            Session.Load(_path); // raises Changed: every view and tool refreshes
            _changes.Begin();
            Debug.Log($"{LogTag} Room save {_key}: design reset, zone kept ({Session.State.zoneOutline.Count} corners).");
            return true;
        }

        /// <summary>Raised after Reset my design (the extended room is worked out again).</summary>
        public event Action RoomReset;

        /// <summary>
        /// Starts a new undo barrier (after the zone is confirmed): <see cref="Undo"/> only steps back
        /// through changes noted since, so it never pops the zone commit. Core has no barrier yet.
        /// </summary>
        public void BeginUserChanges() => _changes.Begin();

        /// <summary>Call after every successful user change made in edit or placement mode.</summary>
        public void NoteUserChange() => NoteUserChange(1);

        /// <summary>One user change made of <paramref name="sessionSteps"/> session commits; Undo reverts all of them.</summary>
        public void NoteUserChange(int sessionSteps) { _changes.Note(sessionSteps); HistoryChanged?.Invoke(); }

        /// <summary>One MR-side user change (Adjust fit), in order with the session's; the panel's arrows undo it.</summary>
        public void NoteUserChange(IUndoStep step) { _changes.Note(step); HistoryChanged?.Invoke(); }

        /// <summary>The undo or redo history changed (the panel greys its arrows).</summary>
        public event System.Action HistoryChanged;

        public bool CanUndo => _changes.CanUndo(Session);
        public bool CanRedo => _changes.CanRedo(Session);

        /// <summary>How many user changes can be undone now (to tell whether a given change is still the last one).</summary>
        public int UserChangeCount => _changes.Count;

        /// <summary>Undoes the last user change, never past the barrier.</summary>
        public bool Undo() { bool done = _changes.Undo(Session); HistoryChanged?.Invoke(); return done; }

        /// <summary>Redoes the last undone user change (its session steps together).</summary>
        public bool Redo() { bool done = _changes.Redo(Session); HistoryChanged?.Invoke(); return done; }

        void OnDestroy()
        {
            if (Session != null) { Session.Changed -= Save; Session.Changed -= OnSessionChanged; }
            if (_room != null) _room.Changed -= OnRoomChanged;
        }

        void Save()
        {
            if (_path == null) return; // no room yet: nothing to key the save to
            try
            {
                Session.Save(_path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"{LogTag} Save failed: {e.Message}");
            }
        }
    }
}

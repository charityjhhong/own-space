using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace OwnSpace.Core
{
    public enum SaveSpaceResult { Saved, Full, Invalid }

    /// <summary>One saved space as listed: enough for a menu row without loading the room.</summary>
    public readonly struct SavedSpaceInfo
    {
        public SavedSpaceInfo(string id, string name, string createdUtc) { Id = id; Name = name; CreatedUtc = createdUtc; }
        public string Id { get; }
        public string Name { get; }
        public string CreatedUtc { get; }
    }

    /// <summary>
    /// The user's saved spaces, one JSON file each (<c>&lt;id&gt;.json</c>) in a folder such as
    /// <c>Path.Combine(Application.persistentDataPath, "spaces")</c>. Writes are atomic; an unreadable file is
    /// skipped in the list (and kept as <c>.bad</c> when loaded) so it never blocks the others. At most
    /// <see cref="MaxSpaces"/>. Main thread or one thread at a time.
    /// </summary>
    public sealed class SavedSpaces
    {
        public const int MaxSpaces = 10;
        static readonly Regex IdPattern = new Regex("^[a-z0-9-]{1,40}$");

        readonly string _directory;

        public SavedSpaces(string directory) => _directory = directory ?? throw new ArgumentNullException(nameof(directory));

        /// <summary>Saved spaces, newest first.</summary>
        public List<SavedSpaceInfo> List()
        {
            var list = new List<SavedSpaceInfo>();
            if (!Directory.Exists(_directory)) return list;
            foreach (var path in Directory.GetFiles(_directory, "*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (IdPattern.IsMatch(id) && RoomSnapshotJson.TryFromJson(AtomicFile.TryReadAllText(path), out var s, out _))
                    list.Add(new SavedSpaceInfo(id, s.name, s.createdUtc));
            }
            list.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(b.CreatedUtc, a.CreatedUtc);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return list;
        }

        /// <summary>
        /// Saves a snapshot: a new one (empty id) gets a fresh id, written into <c>snapshot.id</c>; one with a
        /// known id is replaced. Full when <see cref="MaxSpaces"/> already exist and this one is new.
        /// </summary>
        public SaveSpaceResult Save(RoomSnapshot snapshot)
        {
            if (RoomSnapshot.Validate(snapshot) != null) return SaveSpaceResult.Invalid;
            if (!string.IsNullOrEmpty(snapshot.id) && !IdPattern.IsMatch(snapshot.id)) return SaveSpaceResult.Invalid;
            bool isNew = string.IsNullOrEmpty(snapshot.id) || !File.Exists(PathOf(snapshot.id));
            if (isNew && List().Count >= MaxSpaces) return SaveSpaceResult.Full;
            if (string.IsNullOrEmpty(snapshot.id)) snapshot.id = Guid.NewGuid().ToString("N");
            string json = RoomSnapshotJson.ToJson(snapshot);
            if (json.Length > RoomSnapshot.MaxJsonChars) return SaveSpaceResult.Invalid;
            Directory.CreateDirectory(_directory);
            AtomicFile.WriteAllText(PathOf(snapshot.id), json);
            return SaveSpaceResult.Saved;
        }

        public bool TryLoad(string id, out RoomSnapshot snapshot)
        {
            snapshot = null;
            if (id == null || !IdPattern.IsMatch(id)) return false;
            string path = PathOf(id), text = AtomicFile.TryReadAllText(path);
            if (RoomSnapshotJson.TryFromJson(text, out snapshot, out _)) return true;
            if (text != null) AtomicFile.KeepBrokenCopy(path);
            return false;
        }

        public bool Rename(string id, string name)
        {
            if (!TryLoad(id, out var s)) return false;
            s.name = RoomSnapshot.CleanName(name);
            return Save(s) == SaveSpaceResult.Saved;
        }

        public bool Delete(string id)
        {
            if (id == null || !IdPattern.IsMatch(id) || !File.Exists(PathOf(id))) return false;
            File.Delete(PathOf(id));
            return true;
        }

        string PathOf(string id) => Path.Combine(_directory, id + ".json");
    }
}

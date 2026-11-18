using System;
using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>Resizes a Tidy up cover from the panel's Done step (HideTool).</summary>
    public interface ICoverResize
    {
        /// <summary>Whether there is a cover to resize (the one just made, or the one selected in the room).</summary>
        bool HasCover { get; }
        /// <summary>False when that step is at its limit (the button greys out).</summary>
        bool CanResize(AdjustAction action);
        /// <summary>One step wider, narrower, deeper or shallower; false when at a limit or refused by the cover rules.</summary>
        bool Resize(AdjustAction action);
    }

    /// <summary>
    /// The user's resizing of Tidy up covers (the human: "add a way to adjust its size").
    /// <list type="bullet">
    /// <item>A plain cover is a hand-drawn box, and core holds its size: each side steps <see cref="BoxStep"/> between
    /// <see cref="MinSide"/> and core's <see cref="HideRules.MaxSide"/>.</item>
    /// <item>A prop cover (basket, plant, cushion, box, screen) has no size in core's <c>FreePlacement</c>. Its width and
    /// depth scale, <see cref="MinScale"/> to <see cref="MaxScale"/> of the model in steps of <see cref="Step"/>, is kept
    /// here, in a file next to the room's design save (<c>design-&lt;room&gt;.json.covers.json</c>), until core can hold
    /// it (asked on #1).</item>
    /// </list>
    /// The rules are pure, for tests.
    /// </summary>
    public sealed class CoverAdjusts
    {
        public const float Step = 0.1f, MinScale = 0.7f, MaxScale = 1.3f, BoxStep = 0.05f, MinSide = 0.15f;

        [Serializable] struct Entry { public string id; public float sx, sz; }
        [Serializable] sealed class FileData { public List<Entry> covers = new List<Entry>(); }

        readonly Dictionary<string, Vector2> _byId = new Dictionary<string, Vector2>();
        string _path;

        public static string PathFor(string designPath) => designPath + ".covers.json";

        /// <summary>A prop cover's width and depth scale (1, 1 when it was never resized).</summary>
        public Vector2 Get(string id) => id != null && _byId.TryGetValue(id, out Vector2 s) ? s : Vector2.one;

        /// <summary>Loads the room's file when the design save changed rooms (cheap when it did not).</summary>
        public void Use(string designPath)
        {
            string path = designPath != null ? PathFor(designPath) : null;
            if (path == _path) return;
            _path = path;
            _byId.Clear();
            if (path == null || !File.Exists(path)) return;
            try
            {
                FileData data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                if (data?.covers != null)
                    foreach (Entry e in data.covers)
                        if (!string.IsNullOrEmpty(e.id)) _byId[e.id] = new Vector2(Mathf.Clamp(e.sx, MinScale, MaxScale), Mathf.Clamp(e.sz, MinScale, MaxScale));
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { Debug.LogWarning($"[OwnSpace.MR] Cover sizes not read: {e.Message}"); }
        }

        public void Set(string id, Vector2 scale)
        {
            if (id == null) return;
            if (Mathf.Approximately(scale.x, 1f) && Mathf.Approximately(scale.y, 1f)) _byId.Remove(id); else _byId[id] = scale;
            if (_path == null) return;
            var data = new FileData();
            foreach (KeyValuePair<string, Vector2> kv in _byId) data.covers.Add(new Entry { id = kv.Key, sx = kv.Value.x, sz = kv.Value.y });
            try { AtomicFile.WriteAllText(_path, JsonUtility.ToJson(data)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"[OwnSpace.MR] Cover sizes not saved: {e.Message}"); }
        }

        /// <summary>Forgets every cover's size in this room (Reset my design).</summary>
        public static void Clear(string designPath)
        {
            string path = PathFor(designPath);
            if (File.Exists(path)) File.Delete(path);
        }

        // ---- the rules (pure) ----

        public static bool IsResize(AdjustAction a) => a == AdjustAction.Wider || a == AdjustAction.Narrower || a == AdjustAction.Deeper || a == AdjustAction.Shallower;
        public static bool IsWidth(AdjustAction a) => a == AdjustAction.Wider || a == AdjustAction.Narrower;
        public static int Sign(AdjustAction a) => a == AdjustAction.Wider || a == AdjustAction.Deeper ? 1 : -1;

        /// <summary>A prop's next scale on one axis: one tenth more or less, within 0.7 to 1.3 of the model.</summary>
        public static float NextScale(float now, int sign) => Mathf.Clamp(Mathf.Round((now + sign * Step) * 10f) / 10f, MinScale, MaxScale);

        /// <summary>
        /// A plain box's next side (m): 5 cm more or less, within <see cref="MinSide"/> to core's largest side. A box drawn
        /// outside that range never steps the wrong way.
        /// </summary>
        public static float NextSide(float now, int sign)
        {
            float next = Mathf.Clamp(now + sign * BoxStep, MinSide, HideRules.MaxSide);
            return sign < 0 ? Mathf.Min(next, now) : Mathf.Max(next, now);
        }

        /// <summary>
        /// A cover's footprint as a box, for core's cover rules: a prop's model size times its scale, or a plain box's own
        /// size (<paramref name="scale"/> one), at <paramref name="yaw"/>.
        /// </summary>
        public static HideBox Footprint(string id, Vec3 position, Vec3 size, Vector2 scale, float yaw) => new HideBox
        {
            hideBoxId = id, position = position, yawDegrees = yaw, finishId = "", size = new Vec3(size.x * scale.x, size.y, size.z * scale.y),
        };

        /// <summary>A prop's scale after <paramref name="action"/> (unchanged at a limit).</summary>
        public static Vector2 Scaled(Vector2 scale, AdjustAction action) =>
            IsWidth(action) ? new Vector2(NextScale(scale.x, Sign(action)), scale.y) : new Vector2(scale.x, NextScale(scale.y, Sign(action)));
    }

    /// <summary>One resize of a prop cover in the user's undo history (it lives outside core's session).</summary>
    public sealed class CoverScaleStep : IUndoStep
    {
        readonly Action<Vector2> _apply;
        readonly Vector2 _before, _after;
        public CoverScaleStep(Action<Vector2> apply, Vector2 before, Vector2 after) { _apply = apply; _before = before; _after = after; }
        public void Undo() => _apply(_before);
        public void Redo() => _apply(_after);
    }
}

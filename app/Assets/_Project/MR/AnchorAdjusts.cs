using System;
using System.Collections.Generic;
using System.IO;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>The user's fine-tuning of a redesigned real piece (device batch 10: the scanned box is imperfect).</summary>
    [Serializable]
    public struct AnchorAdjust
    {
        public string anchorId;
        /// <summary>Extra quarter turns (0-3) on top of core's orientation.</summary>
        public int turns;
        /// <summary>Extra turn (degrees, in RotateHandle's 15° steps) from the corner handle on the piece.</summary>
        public float yaw;
        /// <summary>Floor offset (m, world x and z), at most <see cref="AnchorAdjusts.MaxNudge"/> from the real box.</summary>
        public float dx, dz;
        /// <summary>Extra width and depth (m, along the item's own x and z), within core's footprint limits.</summary>
        public float dw, dd;
        /// <summary>Keep the piece's top real (<see cref="RealTops"/>): 0 = on while depth occlusion is not running, 1 = on, 2 = off.</summary>
        public int realTop;

        public bool IsEmpty => turns == 0 && yaw == 0f && dx == 0f && dz == 0f && dw == 0f && dd == 0f && realTop == 0;
    }

    /// <summary>
    /// Per-piece adjustments for redesigned real furniture, kept MR-side in a file next to the room's design save
    /// (<c>design-&lt;room&gt;.json.adjust.json</c>) until core's <see cref="AnchorChoice"/> carries them (asked on #1).
    /// Core recomputes orientation and scale on reload, so these sit on top of its fit. The rules are pure, for tests.
    /// </summary>
    public sealed class AnchorAdjusts
    {
        public const float Step = 0.05f, MaxNudge = 0.3f;
        /// <summary>
        /// The user's own resize may go down to this share of the item's own size per footprint axis (device: the real desk
        /// was shallower than the design and Shallower did nothing). Core's automatic fit still never shrinks.
        /// </summary>
        public const float MinManualScale = 0.7f;

        [Serializable] sealed class FileData { public List<AnchorAdjust> pieces = new List<AnchorAdjust>(); }

        readonly Dictionary<string, AnchorAdjust> _byAnchor = new Dictionary<string, AnchorAdjust>();
        string _path;

        public static string PathFor(string designPath) => designPath + ".adjust.json";

        public AnchorAdjust Get(string anchorId) =>
            anchorId != null && _byAnchor.TryGetValue(anchorId, out AnchorAdjust a) ? a : new AnchorAdjust { anchorId = anchorId };

        /// <summary>Loads the room's file when the design save changed rooms (cheap when it did not).</summary>
        public void Use(string designPath)
        {
            string path = designPath != null ? PathFor(designPath) : null;
            if (path == _path) return;
            _path = path;
            _byAnchor.Clear();
            if (path == null || !File.Exists(path)) return;
            try
            {
                FileData data = JsonUtility.FromJson<FileData>(File.ReadAllText(path));
                if (data?.pieces != null) foreach (AnchorAdjust a in data.pieces) if (!string.IsNullOrEmpty(a.anchorId)) _byAnchor[a.anchorId] = a;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { Debug.LogWarning($"[OwnSpace.MR] Adjustments not read: {e.Message}"); }
        }

        public void Set(AnchorAdjust a)
        {
            if (a.IsEmpty) _byAnchor.Remove(a.anchorId); else _byAnchor[a.anchorId] = a;
            if (_path == null) return;
            var data = new FileData();
            data.pieces.AddRange(_byAnchor.Values);
            try { AtomicFile.WriteAllText(_path, JsonUtility.ToJson(data)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"[OwnSpace.MR] Adjustments not saved: {e.Message}"); }
        }

        /// <summary>Forgets every piece's adjustment in this room (Reset my design).</summary>
        public static void Clear(string designPath)
        {
            string path = PathFor(designPath);
            if (File.Exists(path)) File.Delete(path);
        }

        // ---- the rules (pure) ----

        public static AnchorAdjust Turn(AnchorAdjust a) { a.turns = (a.turns + 1) % 4; return a; }

        /// <summary>
        /// Wider/narrower (or deeper/shallower) by one <see cref="Step"/>: the final size along that axis stays within
        /// <see cref="MinManualScale"/> to <see cref="FitLimits.MaxFootprintScale"/> of the item's own size.
        /// </summary>
        public static AnchorAdjust Resize(AnchorAdjust a, CatalogItem item, AnchorChoice choice, bool width, int sign)
        {
            float own = width ? item.size.x : item.size.z, fit = OpeningGuard.OrOne(width ? choice.xScale : choice.zScale);
            float lo = Mathf.Min(0f, own * (MinManualScale - fit)), hi = Mathf.Max(0f, own * (FitLimits.MaxFootprintScale - fit));
            float now = width ? a.dw : a.dd, next = Mathf.Clamp(now + sign * Step, lo, hi);
            if (sign < 0 ? next > now : next < now) next = now; // already past a limit (an older save): never step the wrong way
            if (width) a.dw = next; else a.dd = next;
            return a;
        }

        /// <summary>False when the resize is already at its limit (the button greys out).</summary>
        public static bool CanResize(AnchorAdjust a, CatalogItem item, AnchorChoice choice, bool width, int sign)
        {
            AnchorAdjust b = Resize(a, item, choice, width, sign);
            return width ? b.dw != a.dw : b.dd != a.dd;
        }

        /// <summary>Moves the piece on the floor, at most <see cref="MaxNudge"/> from where core put it.</summary>
        public static AnchorAdjust Nudge(AnchorAdjust a, float dx, float dz)
        {
            var v = Vector2.ClampMagnitude(new Vector2(dx, dz), MaxNudge);
            a.dx = v.x; a.dz = v.y;
            return a;
        }

        /// <summary>The piece's local scale with the adjustment (item axes).</summary>
        public static Vector3 Scale(CatalogItem item, AnchorChoice choice, AnchorAdjust a) => new Vector3(
            OpeningGuard.OrOne(choice.xScale) + (item.size.x > 0f ? a.dw / item.size.x : 0f),
            OpeningGuard.OrOne(choice.yScale),
            OpeningGuard.OrOne(choice.zScale) + (item.size.z > 0f ? a.dd / item.size.z : 0f));
    }
}

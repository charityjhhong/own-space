using System;
using System.Collections.Generic;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Content's furniture thumbnail atlas (<c>Art/UI/FurnitureThumbs.png</c>) read from
    /// <c>Content/thumbnails.json</c>: each catalog id's cell as a <c>_BaseMap_ST</c> rect for a panel card
    /// (u = x / width, v = 1 - (y + cellHeight) / height, from the atlas's top-left cell coordinates).
    /// </summary>
    public sealed class FurnitureThumbs
    {
        [Serializable] sealed class File { public int cellWidth, cellHeight, width, height; public Cell[] items; }
        [Serializable] sealed class Cell { public string id; public int x, y; }

        readonly Dictionary<string, Vector4> _rects = new Dictionary<string, Vector4>();

        public FurnitureThumbs(string thumbnailsJson)
        {
            File f = string.IsNullOrEmpty(thumbnailsJson) ? null : JsonUtility.FromJson<File>(thumbnailsJson);
            if (f?.items == null || f.width <= 0 || f.height <= 0) return;
            float sx = (float)f.cellWidth / f.width, sy = (float)f.cellHeight / f.height;
            foreach (Cell c in f.items)
                if (!string.IsNullOrEmpty(c.id)) _rects[c.id] = new Vector4(sx, sy, (float)c.x / f.width, 1f - (float)(c.y + f.cellHeight) / f.height);
        }

        public int Count => _rects.Count;

        /// <summary>The item's atlas rect, or zero when it has no thumbnail (the card shows its colour).</summary>
        public Vector4 Rect(string itemId) => itemId != null && _rects.TryGetValue(itemId, out Vector4 r) ? r : Vector4.zero;
    }
}

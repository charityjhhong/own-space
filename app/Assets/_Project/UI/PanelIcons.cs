using System;
using System.Collections.Generic;
using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// Content's UI icon atlas (<c>Art/UI/UIIcons.png</c>) read from <c>Content/ui-icons.json</c>: each icon's cell
    /// as an atlas rect (scale xy, offset zw; v from the bottom) for <see cref="PanelElement.SetIcon"/>.
    /// The atlas is a texture on the one panel material, so icons add no material.
    /// </summary>
    public sealed class PanelIcons
    {
        [Serializable] sealed class File { public int width, height; public Icon[] icons; }
        [Serializable] sealed class Icon { public string name; public int x, y, w, h; }

        readonly Dictionary<string, Vector4> _rects = new Dictionary<string, Vector4>();

        public PanelIcons(string iconsJson)
        {
            File f = string.IsNullOrEmpty(iconsJson) ? null : JsonUtility.FromJson<File>(iconsJson);
            if (f?.icons == null || f.width <= 0 || f.height <= 0) return;
            foreach (Icon i in f.icons)
                if (!string.IsNullOrEmpty(i.name))
                    _rects[i.name] = new Vector4((float)i.w / f.width, (float)i.h / f.height, (float)i.x / f.width, 1f - (float)(i.y + i.h) / f.height);
        }

        /// <summary>The icon's atlas rect, or zero when the atlas has no such icon.</summary>
        public Vector4 Rect(string name) => name != null && _rects.TryGetValue(name, out Vector4 r) ? r : Vector4.zero;
    }
}

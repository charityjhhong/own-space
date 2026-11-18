#!/usr/bin/env python3
"""Builds the UI icon atlas from docs/design/ui/icons/*.svg: app/Assets/_Project/Art/UI/UIIcons.png (white on transparent, tinted at
runtime by vertex colour) and app/Assets/_Project/Content/ui-icons.json (name and rect of each icon, from the top left).
Run:  python3 docs/design/ui/make_icon_atlas.py        (needs rsvg-convert and pillow)
Then: Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.UiIconAtlasBuilder.BuildBatch -quit   (import settings, .meta)
Deterministic: icons are sorted by name; a rerun writes identical files."""
import io, json, os, subprocess, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..", "..", "app", "Assets", "_Project")
CELL, PAD, COLS, W, H = 80, 4, 6, 512, 256
ORDER = ["chevron-left", "chevron-right", "check", "eye", "info", "mic", "minimize", "redo", "settings", "sparkle", "undo"]

def main():
    atlas = Image.new("RGBA", (W, H), (0, 0, 0, 0)); icons = []
    for i, name in enumerate(sorted(ORDER)):
        svg = open(os.path.join(HERE, "icons", name + ".svg")).read().replace("currentColor", "#FFFFFF").replace('color="#0E1520"', 'color="#FFFFFF"')
        png = subprocess.run(["rsvg-convert", "-w", str(CELL - 2 * PAD), "-h", str(CELL - 2 * PAD), "-f", "png"], input=svg.encode(), capture_output=True, check=True).stdout
        im = Image.open(io.BytesIO(png)).convert("RGBA"); x, y = (i % COLS) * CELL, (i // COLS) * CELL
        atlas.paste(im, (x + PAD, y + PAD)); icons.append(dict(name=name, x=x, y=y, w=CELL, h=CELL))
    out = os.path.join(ROOT, "Art", "UI"); os.makedirs(out, exist_ok=True)
    atlas.save(os.path.join(out, "UIIcons.png"), optimize=False)
    data = dict(atlas="Assets/_Project/Art/UI/UIIcons.png", width=W, height=H, cell=CELL, padding=PAD, tint="white on transparent; tint at runtime", icons=icons)
    with open(os.path.join(ROOT, "Content", "ui-icons.json"), "w") as f: f.write(json.dumps(data, indent=1) + "\n")
    print(f"{len(icons)} icons, atlas {W}x{H}")
    return 0

if __name__ == "__main__":
    sys.exit(main())

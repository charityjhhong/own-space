#!/usr/bin/env python3
"""Draws the Own Space store art (own artwork, CC0; font Inter SemiBold, SIL OFL, Art/Fonts) for the Meta Horizon dashboard.
Run: python3 submission/art/make_store_art.py        (needs pillow; the room backdrops come from docs/design/ui)
Sizes (Meta branded asset specs): icon 512x512; cover landscape 2560x1440, square 1440x1440, portrait 1008x1440,
hero 3000x900; logo (transparent) up to 9000x1440; screenshots 2560x1440 (5, device captures: the frames here are PLACEHOLDERS).
All opaque assets are 24-bit PNG; the logo is 32-bit with alpha. Deterministic: a rerun writes identical files."""
import math, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
UI = os.path.join(ROOT, "docs", "design", "ui")
FONT = os.path.join(ROOT, "app", "Assets", "_Project", "Art", "Fonts", "Inter-SemiBold.ttf")
INK = (14, 21, 32)

def room(name, w, h, blur=7):
    im = Image.open(os.path.join(UI, name)).convert("RGB"); iw, ih = im.size
    s = max(w / iw, h / ih); im = im.resize((int(iw * s) + 1, int(ih * s) + 1), Image.LANCZOS)
    l, t = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((l, t, l + w, t + h)).filter(ImageFilter.GaussianBlur(blur))

def gradient(w, h, top, bottom):
    im = Image.new("RGB", (w, h)); px = im.load()
    for y in range(h):
        t = y / max(1, h - 1); c = tuple(round(top[i] * (1 - t) + bottom[i] * t) for i in range(3))
        for x in range(w): px[x, y] = c
    return im

def glass(base, box, radius, tint=(255, 255, 255), alpha=0.58, rim=0.8):
    """Translucent glass rounded rectangle with a rim and a soft shadow."""
    w, h = base.size; x0, y0, x1, y1 = box
    sh = Image.new("L", (w, h), 0); ImageDraw.Draw(sh).rounded_rectangle((x0 + 4, y0 + 14, x1 + 4, y1 + 14), radius, fill=90)
    sh = sh.filter(ImageFilter.GaussianBlur(max(8, (y1 - y0) // 16))); base.paste((0, 0, 0), mask=sh)
    ov = Image.new("RGBA", (w, h), (0, 0, 0, 0)); d = ImageDraw.Draw(ov)
    d.rounded_rectangle(box, radius, fill=tint + (round(255 * alpha),), outline=(255, 255, 255, round(255 * rim)), width=max(2, (y1 - y0) // 160))
    g = Image.new("L", (w, h), 0); gd = ImageDraw.Draw(g)
    for i in range(y1 - y0): gd.line([(x0, y0 + i), (x1, y0 + i)], fill=round(60 * (1 - i / (y1 - y0))))
    mask = Image.new("L", (w, h), 0); ImageDraw.Draw(mask).rounded_rectangle(box, radius, fill=255)
    ov.paste((255, 255, 255, 255), mask=Image.composite(g, Image.new("L", (w, h), 0), mask))
    base.paste(ov, (0, 0), ov)

def mark(draw, cx, cy, r, col=INK, width=None):
    """The Own Space mark (B3): a house, the left half an outline (the real room), the right half filled (designed)."""
    width = width or max(3, round(r / 7))
    pts = [(cx - r, cy + r * 0.9), (cx - r, cy - r * 0.12), (cx, cy - r), (cx + r, cy - r * 0.12), (cx + r, cy + r * 0.9)]
    draw.polygon([(cx, cy - r), (cx + r, cy - r * 0.12), (cx + r, cy + r * 0.9), (cx, cy + r * 0.9)], fill=col)
    draw.line(pts + [pts[0], pts[1]], fill=col, width=width, joint="curve")

def wordmark(draw, cx, cy, size, col=INK, with_mark=True):
    f = ImageFont.truetype(FONT, size); text = "Own Space"
    tw = draw.textlength(text, font=f); mr = round(size * 0.5); gap = round(size * 0.35)
    total = tw + (2 * mr + gap if with_mark else 0); x = cx - total / 2
    if with_mark: mark(draw, x + mr, cy, mr, col); x += 2 * mr + gap
    draw.text((x, cy), text, font=f, fill=col, anchor="lm")

def cover(w, h, room_name, size_frac, glass_box_frac):
    im = room(room_name, w, h); bw, bh = glass_box_frac
    box = (round(w / 2 - w * bw / 2), round(h / 2 - h * bh / 2), round(w / 2 + w * bw / 2), round(h / 2 + h * bh / 2))
    glass(im, box, round(h * 0.06)); d = ImageDraw.Draw(im); wordmark(d, w // 2, h // 2, round(h * size_frac))
    return im

def icon():
    im = gradient(512, 512, (246, 162, 94), (70, 82, 138)); glass(im, (96, 96, 416, 416), 72, alpha=0.30, rim=0.9)
    d = ImageDraw.Draw(im); mark(d, 256, 256, 92, (255, 255, 255), 14); return im

def logo():
    im = Image.new("RGBA", (4500, 720), (0, 0, 0, 0)); d = ImageDraw.Draw(im); wordmark(d, 2250, 360, 380, INK + (255,)); return im

SHOTS = [("01-dorm-before", "room-bright.jpg", None, "Shot 1: the real dorm, passthrough, untouched"),
         ("02-glass-panel-style", "room-bright.jpg", "mockup-style-light-day.png", "Shot 2: the tool panel, Style tab (floor, wall, mood)"),
         ("03-ai-designer-voice", "room-bright.jpg", "mockup-ai-designer-typing-light-day.png", "Shot 3: AI Designer request"),
         ("04-tidy-up", "room-bright.jpg", "mockup-tidy-up-light-day.png", "Shot 4: Tidy up, laundry covered with a basket"),
         ("05-evening-after", "room-dark.jpg", "mockup-furniture-dark-night.png", "Shot 5: the redesigned room in the evening mood")]

def screenshot(name, bg, panel, label):
    w, h = 2560, 1440; im = room(bg, w, h, blur=3); d = ImageDraw.Draw(im)
    if panel:
        p = Image.open(os.path.join(UI, panel)).convert("RGB"); s = 1100 / p.height; p = p.resize((round(p.width * s), 1100), Image.LANCZOS)
        im.paste(p, (w - p.width - 220, (h - 1100) // 2))
    f = ImageFont.truetype(FONT, 44); t = "PLACEHOLDER, not for upload. Replace with a device capture. " + label
    bw = d.textlength(t, font=f) + 60; d.rounded_rectangle((40, h // 2 - 40, 40 + bw, h // 2 + 40), 20, fill=(200, 40, 30)); d.text((70, h // 2), t, font=f, fill=(255, 255, 255), anchor="lm")
    return im

def main():
    out = HERE; os.makedirs(os.path.join(out, "screenshots-placeholder"), exist_ok=True)
    icon().save(os.path.join(out, "icon-512.png"))
    cover(2560, 1440, "room-bright.jpg", 0.085, (0.50, 0.30)).save(os.path.join(out, "cover-landscape-2560x1440.png"))
    cover(1440, 1440, "room-bright.jpg", 0.075, (0.82, 0.22)).save(os.path.join(out, "cover-square-1440x1440.png"))
    cover(1008, 1440, "room-bright.jpg", 0.052, (0.88, 0.16)).save(os.path.join(out, "cover-portrait-1008x1440.png"))
    cover(3000, 900, "room-dark.jpg", 0.17, (0.40, 0.62)).save(os.path.join(out, "hero-3000x900.png"))
    logo().save(os.path.join(out, "logo-transparent-4500x720.png"))
    for n, bg, panel, label in SHOTS: screenshot(n, bg, panel, label).save(os.path.join(out, "screenshots-placeholder", n + ".png"))
    print("done")
    return 0

if __name__ == "__main__":
    sys.exit(main())

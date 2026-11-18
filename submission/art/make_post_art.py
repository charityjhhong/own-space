#!/usr/bin/env python3
"""Draws the Own Space post thumbnails (own artwork, CC0; font Inter, SIL OFL, Art/Fonts): a 1:1 square and a 16:9 landscape
(the app thumbnail) and a 16:9 landscape (posts: Devpost, YouTube, social), plus GitHub's 2:1 social preview. Unlike the store covers these carry the tagline.
Concept: one room, split. Left, the room as it is (bare, grey, dim); right, the same room redesigned in Own Space.
The rooms are 3D renders of the app's own CC0 furniture: run render_room.py in Blender first (it writes post/render/).
Run: python3 submission/art/make_post_art.py        (needs pillow)
Deterministic: a rerun writes identical files."""
import os, sys
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
UI = os.path.join(ROOT, "docs", "design", "ui")
FONTS = os.path.join(ROOT, "app", "Assets", "_Project", "Art", "Fonts")
SEMI, REG = os.path.join(FONTS, "Inter-SemiBold.ttf"), os.path.join(FONTS, "Inter-Regular.ttf")
WHITE, INK = (255, 255, 255), (14, 21, 32)
TITLE = "Own Space"
HEADLINE = ["The room you can't change,", "made your own."]
SUB = "Redesign your dorm, rental or hotel room on Meta Quest."  # no laptop claim: the human, the desk is the point
SUB2 = "Hands only. Your real desk stays real."

def cover_fit(im, w, h):
    iw, ih = im.size; s = max(w / iw, h / ih)
    im = im.resize((int(iw * s) + 1, int(ih * s) + 1), Image.LANCZOS)
    l, t = (im.width - w) // 2, (im.height - h) // 2
    return im.crop((l, t, l + w, t + h))

RENDER = os.path.join(HERE, "post", "render")

def rendered(name, w, h):
    return cover_fit(Image.open(os.path.join(RENDER, name)).convert("RGB"), w, h)

def split_room(w, h, split):
    """The same room twice: as it is on the left (bare, grey), redesigned on the right; the real desk is in both."""
    after = rendered("room-after.png", w, h)
    before = ImageEnhance.Color(rendered("room-before.png", w, h)).enhance(0.3)
    # soft vertical blend at the split
    mask = Image.new("L", (w, h), 0); px = mask.load(); feather = max(2, round(w * 0.012))
    for x in range(w):
        v = 0 if x < split - feather else 255 if x > split + feather else round(255 * (x - split + feather) / (2 * feather))
        for y in range(h): px[x, y] = v
    return Image.composite(after, before, mask)

def divider(im, x, top, bottom):
    """A thin glass line at the split, with the Own Space mark as the handle."""
    d = ImageDraw.Draw(im, "RGBA"); w = max(2, im.width // 600)
    d.line([(x, top), (x, bottom)], fill=(255, 255, 255, 220), width=w)
    r = max(18, im.height // 22); cy = (top + bottom) // 2
    glow = Image.new("L", im.size, 0); ImageDraw.Draw(glow).ellipse((x - r * 1.4, cy - r * 1.4, x + r * 1.4, cy + r * 1.4), fill=120)
    im.paste((0, 0, 0), mask=glow.filter(ImageFilter.GaussianBlur(r // 2)))
    d = ImageDraw.Draw(im, "RGBA")
    d.ellipse((x - r, cy - r, x + r, cy + r), fill=(255, 255, 255, 240))
    ring = round(r * 0.62); d.ellipse((x - ring, cy - ring, x + ring, cy + ring), outline=INK, width=max(2, ring // 6))
    dot = ring // 3; d.ellipse((x + ring // 5 - dot, cy + ring // 6 - dot, x + ring // 5 + dot, cy + ring // 6 + dot), fill=INK)

def chip(im, x, y, text, size, dark):
    f = ImageFont.truetype(SEMI, size); d = ImageDraw.Draw(im, "RGBA")
    tw = d.textlength(text, font=f); pad = size * 0.7
    box = (x, y, x + tw + 2 * pad, y + size * 2.0)
    d.rounded_rectangle(box, size, fill=(12, 14, 20, 170) if dark else (255, 255, 255, 215))
    d.text((x + pad, y + size), text, font=f, fill=WHITE if dark else INK, anchor="lm")

def shade_top(im, frac):
    """A dark band at the top so white text reads over any room."""
    w, h = im.size; band = round(h * frac); g = Image.new("L", (w, h), 0); gd = ImageDraw.Draw(g)
    for y in range(band): gd.line([(0, y), (w, y)], fill=round(232 * (1 - y / band) ** 1.1))
    im.paste((8, 10, 16), mask=g)

def mark(d, cx, cy, r, col):
    d.ellipse((cx - r, cy - r, cx + r, cy + r), outline=col, width=max(3, r // 7))
    k = r // 3; d.ellipse((cx + r // 5 - k, cy + r // 6 - k, cx + r // 5 + k, cy + r // 6 + k), fill=col)

def text_block(im, x, y, title_px, head_px, sub_px, align="l", subs=True):
    d = ImageDraw.Draw(im)
    ft, fh, fs = ImageFont.truetype(SEMI, title_px), ImageFont.truetype(SEMI, head_px), ImageFont.truetype(REG, sub_px)
    def put(text, font, yy, col):
        tw = d.textlength(text, font=font); xx = x - tw / 2 if align == "c" else x
        d.text((xx, yy), text, font=font, fill=col, anchor="la")
    # wordmark
    r = round(title_px * 0.42); tw = d.textlength(TITLE, font=ft); total = 2 * r + r * 0.7 + tw
    x0 = x - total / 2 if align == "c" else x
    mark(d, round(x0 + r), round(y + title_px * 0.62), r, WHITE)
    d.text((x0 + 2 * r + r * 0.7, y), TITLE, font=ft, fill=WHITE, anchor="la")
    yy = y + title_px * 1.75
    for line in HEADLINE: put(line, fh, yy, WHITE); yy += head_px * 1.18
    yy += head_px * 0.35
    for line in ((SUB, SUB2) if subs else ()): put(line, fs, yy, (225, 230, 240)); yy += sub_px * 1.4

def landscape(w, h, split_at=0.5):
    """Before and after, side by side: the bare room on the left (only the real desk), the same room redesigned on
    the right. The split sits past the desk so the redesigned side shows the whole set."""
    split = round(w * split_at); im = split_room(w, h, split); shade_top(im, 0.6)
    divider(im, split, round(h * 0.58), h)
    size = round(h * 0.024)
    chip(im, round(w * 0.03), round(h * 0.88), "Your room", size, True)
    lab = "In Own Space"; f = ImageFont.truetype(SEMI, size); lw = ImageDraw.Draw(im).textlength(lab, font=f) + 2 * size * 0.7
    chip(im, round(w * 0.97 - lw), round(h * 0.88), lab, size, False)
    text_block(im, round(w * 0.06), round(h * 0.07), round(h * 0.055), round(h * 0.075), round(h * 0.031))
    return im

def square(s):
    """The app thumbnail: before and after split (the bare room left, past the desk), the name large in the centre."""
    split = round(s * 0.5); im = split_room(s, s, split)
    band = Image.new("L", (s, s), 0); bd = ImageDraw.Draw(band)
    for y in range(s):
        t = abs(y - s * 0.5) / (s * 0.5); bd.line([(0, y), (s, y)], fill=round(175 * max(0.0, 1 - t * 1.5) ** 1.3))
    im.paste((10, 12, 18), mask=band)
    d = ImageDraw.Draw(im); size = round(s * 0.115); f = ImageFont.truetype(SEMI, size)
    tw = d.textlength(TITLE, font=f); r = round(size * 0.42); gap = round(r * 0.7); total = 2 * r + gap + tw; x0 = (s - total) / 2
    mark(d, round(x0 + r), s // 2, r, WHITE)
    d.text((x0 + 2 * r + gap, s // 2), TITLE, font=f, fill=WHITE, anchor="lm")
    fs = ImageFont.truetype(REG, round(s * 0.034)); line = HEADLINE[0] + " " + HEADLINE[1]
    d.text((s / 2, s // 2 + size * 0.95), line, font=fs, fill=(235, 238, 244), anchor="mm")
    divider(im, split, round(s * 0.72), s)
    cs = round(s * 0.026)
    chip(im, round(s * 0.03), round(s * 0.9), "Before", cs, True)
    f2 = ImageFont.truetype(SEMI, cs); lw = ImageDraw.Draw(im).textlength("After", font=f2) + 2 * cs * 0.7
    chip(im, round(s * 0.97 - lw), round(s * 0.9), "After", cs, False)
    return im

def main():
    out = os.path.join(HERE, "post"); os.makedirs(out, exist_ok=True)
    square(1080).save(os.path.join(out, "post-square-1080.png"))
    landscape(1920, 1080).save(os.path.join(out, "post-landscape-1920x1080.png"))
    landscape(1280, 640).save(os.path.join(out, "github-social-1280x640.png"))
    landscape(1500, 1000).save(os.path.join(out, "devpost-thumbnail-1500x1000.png"))  # Devpost asks for 3:2
    print("done")
    return 0

if __name__ == "__main__":
    sys.exit(main())

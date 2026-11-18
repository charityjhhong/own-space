#!/usr/bin/env python3
"""Synthetic stand-in room images (not photos) used behind the panel mockups: a bright day room and a dark night room.
Deterministic (fixed seed). Output: room-bright.jpg, room-dark.jpg (1600 x 1000). Needs numpy and pillow."""
import numpy as np, os, sys
from PIL import Image, ImageDraw, ImageFilter

W, H = 1600, 1000
HERE = os.path.dirname(os.path.abspath(__file__))

def grad(h, w, top, bottom):
    t = np.linspace(0, 1, h)[:, None, None]
    return (np.array(top)[None, None, :] * (1 - t) + np.array(bottom)[None, None, :] * t) * np.ones((h, w, 1))

def blur(img, r): return img.filter(ImageFilter.GaussianBlur(r))

def shadow(layer, box, r=18, a=110):
    s = Image.new("L", (W, H), 0); ImageDraw.Draw(s).ellipse(box, fill=a)
    s = blur(s, r); layer.paste((20, 15, 10), mask=s)

def render(night):
    rng = np.random.default_rng(7)
    wall_t, wall_b = ((222, 216, 205), (205, 197, 184)) if not night else ((36, 40, 52), (30, 33, 42))
    img = Image.fromarray(grad(H, W, wall_t, wall_b).astype("uint8"))
    horizon = 640
    # floor: perspective planks
    fl = Image.fromarray(grad(H - horizon, W, (178, 146, 108) if not night else (44, 36, 30), (150, 118, 84) if not night else (30, 24, 20)).astype("uint8"))
    d = ImageDraw.Draw(fl); vx = 800
    for k in range(-14, 15):
        x0 = vx + k * 140; d.line([(vx + k * 18, 0), (x0 * 1.0, H - horizon)], fill=(120, 92, 64) if not night else (24, 19, 15), width=2)
    for y in (30, 80, 150, 240, 350):
        d.line([(0, y), (W, y)], fill=(130, 100, 70) if not night else (26, 21, 17), width=1)
    img.paste(fl, (0, horizon))
    d = ImageDraw.Draw(img)
    d.rectangle([0, horizon - 22, W, horizon], fill=(236, 232, 224) if not night else (52, 55, 66))  # skirting
    # window with light
    wx0, wy0, wx1, wy1 = 1020, 120, 1380, 520
    sky = grad(wy1 - wy0, wx1 - wx0, (210, 232, 250), (245, 248, 250)) if not night else grad(wy1 - wy0, wx1 - wx0, (18, 30, 62), (46, 62, 100))
    img.paste(Image.fromarray(sky.astype("uint8")), (wx0, wy0))
    d = ImageDraw.Draw(img); fc = (240, 238, 232) if not night else (70, 74, 88)
    d.rectangle([wx0 - 10, wy0 - 10, wx1 + 10, wy1 + 10], outline=fc, width=14); d.line([(wx0 + 180, wy0), (wx0 + 180, wy1)], fill=fc, width=10); d.line([(wx0, wy0 + 200), (wx1, wy0 + 200)], fill=fc, width=10)
    # curtains
    for x in (wx0 - 60, wx1 + 14):
        d.rounded_rectangle([x, wy0 - 30, x + 48, wy1 + 120], 14, fill=(232, 228, 220) if not night else (58, 62, 78))
    # light patch on floor / glow
    glow = Image.new("RGB", (W, H), (0, 0, 0)); g = ImageDraw.Draw(glow)
    g.polygon([(1000, 660), (1400, 660), (1560, 900), (900, 900)], fill=(120, 105, 70) if not night else (14, 22, 44))
    glow = blur(glow, 40); img = Image.fromarray(np.clip(np.asarray(img, float) + np.asarray(glow, float), 0, 255).astype("uint8"))
    lay = img.copy(); d = ImageDraw.Draw(lay)
    # desk + laptop (left)
    shadow(lay, [100, 735, 560, 795]); d = ImageDraw.Draw(lay)
    d.rounded_rectangle([110, 470, 540, 500], 6, fill=(200, 164, 122) if not night else (70, 56, 44))
    for x in (130, 500): d.rectangle([x, 500, x + 14, 770], fill=(150, 116, 84) if not night else (48, 38, 30))
    d.polygon([(220, 470), (420, 470), (400, 330), (240, 330)], fill=(60, 64, 72) if not night else (24, 26, 32))
    d.polygon([(246, 462), (414, 462), (394, 340), (252, 340)], fill=(176, 198, 224) if not night else (96, 140, 196))
    # shelf
    d.rectangle([600, 200, 760, 620], fill=(232, 224, 210) if not night else (56, 52, 56))
    for y in (270, 350, 430, 510): d.line([(600, y), (760, y)], fill=(190, 178, 160) if not night else (36, 34, 38), width=4)
    rngc = [(196, 120, 90), (110, 140, 170), (150, 170, 120), (220, 200, 150)] if not night else [(90, 60, 50), (50, 66, 90), (70, 86, 60), (100, 90, 60)]
    for r, y in enumerate((270, 350, 430, 510)):
        for k in range(5): d.rectangle([612 + k * 28, y - 54 + (k % 2) * 8, 612 + k * 28 + 20, y - 4], fill=rngc[(r + k) % 4])
    # sofa (center right)
    shadow(lay, [780, 770, 1300, 840]); d = ImageDraw.Draw(lay)
    d.rounded_rectangle([800, 650, 1280, 800], 40, fill=(120, 134, 148) if not night else (40, 46, 56))
    d.rounded_rectangle([830, 580, 1250, 700], 36, fill=(138, 152, 166) if not night else (48, 56, 68))
    d.rounded_rectangle([830, 700, 1250, 780], 28, fill=(150, 164, 176) if not night else (54, 62, 74))
    # plant
    d.rounded_rectangle([1420, 700, 1500, 800], 10, fill=(200, 190, 175) if not night else (70, 66, 62))
    for a in range(9):
        ang = -80 + a * 20; x = 1460 + np.sin(np.radians(ang)) * 70; y = 690 - np.cos(np.radians(ang)) * 110
        d.line([(1460, 700), (x, y)], fill=(70, 110, 70) if not night else (26, 44, 30), width=14)
    # pendant lamp
    d.line([(520, 0), (520, 150)], fill=(60, 60, 60), width=3)
    d.pieslice([480, 130, 560, 210], 180, 360, fill=(240, 236, 226) if not night else (255, 214, 150))
    img = lay
    if night:
        lamp = Image.new("RGB", (W, H), (0, 0, 0)); ImageDraw.Draw(lamp).ellipse([380, 90, 660, 380], fill=(150, 100, 40))
        lamp = blur(lamp, 70); img = Image.fromarray(np.clip(np.asarray(img, float) + np.asarray(lamp, float), 0, 255).astype("uint8"))
    img = blur(img, 1.3)
    a = np.asarray(img, float)
    yy, xx = np.mgrid[0:H, 0:W]; v = 1 - 0.28 * (((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
    a = a * v[..., None] + rng.normal(0, 2.2, a.shape)
    return Image.fromarray(np.clip(a, 0, 255).astype("uint8"))

def main():
    render(False).save(os.path.join(HERE, "room-bright.jpg"), quality=90)
    render(True).save(os.path.join(HERE, "room-dark.jpg"), quality=90)
    return 0

if __name__ == "__main__":
    sys.exit(main())

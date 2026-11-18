#!/usr/bin/env python3
"""Generates the Own Space glass tool-panel mockups and spec sheets (own artwork, CC0) over synthetic room backdrops.
Run:  python3 docs/design/ui/make_room_backdrops.py   (once: room-bright.jpg, room-dark.jpg)
      python3 docs/design/ui/make_ui_assets.py        (needs rsvg-convert, numpy, pillow)
Units: millimetres on a 4 mm grid (4/8 steps). The panel is 288 mm wide. Mockup PNGs scale it to pixels.
Furniture thumbnails: thumbs-atlas.png (a copy of Art/UI/FurnitureThumbs.png) and Content/thumbnails.json.
The mockup font is Helvetica Neue (SVG only); the app uses Inter (SIL OFL), see ui-spec.md.
"""
import json, os, shutil, subprocess, sys
from html import escape
import numpy as np
from PIL import Image, ImageFont
import icons as I

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "..", "..", "..", "app", "Assets", "_Project", "Content")
FONT = "Inter, Helvetica Neue, Helvetica, Arial, sans-serif"
_MEAS = ImageFont.truetype("/System/Library/Fonts/HelveticaNeue.ttc", 100, index=0)

def load(n):
    with open(os.path.join(CONTENT, n)) as f: return json.load(f)
CAT = {i["id"]: i for i in load("catalog.json")["items"]}
FIN = {f["id"]: f for f in load("finishes.json")["finishes"]}
THUMB = load("thumbnails.json"); CELLS = {c["id"]: c for c in THUMB["items"]}

# ---- metrics (mm, 4 mm grid) ----
PW = 296; PAD = 16; IW = PW - 2 * PAD           # panel width, outer padding, inner width
R_PANEL = 28; R_TILE = 12                        # corner radii (chips are pills: radius = height / 2)
CHIP = 36; CHIP_S = 28; SEG = 36; SEG_S = 30     # heights
GAP = 8; GAP_S = 4; SEC = 12                     # gap between items, small gap, gap between sections
FS_BODY, FS_SMALL, FS_TAB, FS_CARD = 14, 13, 14, 12   # RULE: no option or body text is larger than the tab text (14)                       # font sizes; cap height = 0.72 x size: 10.8 mm / 9.4 mm
STATUS_H = 14
TOP = 8; TABS_Y = 40; BODY_Y = 84
INK = "#0E1520"; NEAR_BLACK = "#2C2F36"; RED = "#E5483B"   # graphite (the Light-theme selected fill, not pure black), refusal red

THEMES = {
    "light": dict(fg=INK, sec_op=0.76, base=INK, chip_op=0.07, chip_rim=INK, chip_rim_op=0.10, sel_fill=NEAR_BLACK, sel_text="#FFFFFF", ring="#0A74E0",
                  tint="#FFFFFF", scrim=0.70, hover_op=0.17, track_op=0.07, refuse_text="#C62F22", rim="#FFFFFF", shadow=0.22, sheen_top=0.30, sheen_bot=0.10),
    "dark": dict(fg="#FFFFFF", sec_op=0.82, base="#FFFFFF", chip_op=0.14, chip_rim="#FFFFFF", chip_rim_op=0.22, sel_fill="#FFFFFF", sel_text=INK, ring="#6DB6FF",
                 tint="#0F1826", scrim=0.80, hover_op=0.24, track_op=0.12, refuse_text="#FF9A90", rim="#FFFFFF", shadow=0.40, sheen_top=0.16, sheen_bot=0.05),
}
TH = {}
def set_theme(name): TH.clear(); TH.update(THEMES[name]); TH["name"] = name
set_theme("light")

def tw(s, size, bold=False): return _MEAS.getlength(s) / 100 * size * 1.06 + (size * 0.05 if bold else 0)   # +6% for Inter

# ---- primitives ----
def rrect(x, y, w, h, r, fill, op=1.0, stroke=None, sw=0.5, sop=1.0, extra=""):
    st = f' stroke="{stroke}" stroke-width="{sw}" stroke-opacity="{sop}"' if stroke else ""
    return f'<rect x="{x:.2f}" y="{y:.2f}" width="{w:.2f}" height="{h:.2f}" rx="{r:.2f}" fill="{fill}" fill-opacity="{op}"{st} {extra}/>'
def text(x, y, s, size=FS_BODY, fill=None, anchor="start", weight="400", op=1.0, shadow=True):
    fill = fill or TH["fg"]
    f = ' filter="url(#ts)"' if (shadow and TH["name"] == "dark") else ""
    return (f'<text x="{x:.2f}" y="{y:.2f}" font-family="{FONT}" font-size="{size}" font-weight="{weight}" fill="{fill}" '
            f'fill-opacity="{op}" text-anchor="{anchor}"{f}>{escape(s)}</text>')
def label(cx, cy, s, size=FS_BODY, **kw):   # one-line label, vertically centred on cy (cap-height centred)
    return text(cx, cy + size * 0.36, s, size, anchor="middle", **kw)
def icon(name, x, y, size, color=None, op=1.0):
    return f'<g transform="translate({x:.2f},{y:.2f}) scale({size/48:.4f})" color="{color or TH["fg"]}" opacity="{op}" {I.S}>{I.ICONS[name]}</g>'
def clip(s, w, size, bold=False):
    if tw(s, size, bold) <= w: return s
    while s and tw(s + "…", size, bold) > w: s = s[:-1]
    return s.rstrip() + "…"
def wrap(s, w, size, maxl):
    lines, cur = [], ""
    for word in s.split():
        t = (cur + " " + word).strip()
        if tw(t, size) <= w: cur = t
        else: lines.append(cur); cur = word
    if cur: lines.append(cur)
    if len(lines) > maxl: lines = lines[:maxl]; lines[-1] = clip(lines[-1] + " …", w, size)
    return lines

# ---- widgets. state: normal | hover | pressed | selected | disabled | refused ----
def chip(x, y, w, h, lab, state="normal", size=FS_BODY, r=None, content=None, ring=True, pad=None):
    pad = pad if pad is not None else (24 if size >= FS_BODY else 16)
    r = h / 2 if r is None else r
    dy = -1.0 if state == "hover" else 0.0; sc = 0.97 if state == "pressed" else 1.0
    s = f'<g transform="translate({x + w/2:.2f},{y + h/2 + dy:.2f}) scale({sc}) translate({-(x + w/2):.2f},{-(y + h/2):.2f})" opacity="{0.4 if state == "disabled" else 1}">'
    if state == "hover": s += rrect(x, y + 1.6, w, h, r, "#000000", TH["shadow"] * 0.7, extra='filter="url(#cs)"')
    if state == "selected":
        s += rrect(x, y, w, h, r, TH["sel_fill"], 0.96)
        if ring: s += rrect(x - 2.4, y - 2.4, w + 4.8, h + 4.8, r + 2.4, "none", 1, TH["ring"], 1.3, 0.95)
    elif state == "refused":
        s += rrect(x, y, w, h, r, RED, 0.12) + rrect(x, y, w, h, r, "none", 1, RED, 1.2, 0.95)
    else:
        op = TH["hover_op"] if state == "hover" else (TH["chip_op"] * (0.6 if state == "pressed" else 1))
        s += rrect(x, y, w, h, r, TH["base"], op, TH["chip_rim"], 0.4, TH["chip_rim_op"])
    col = TH["sel_text"] if state == "selected" else TH["fg"]
    if content: s += content(col)
    elif lab: s += label(x + w / 2, y + h / 2, clip(lab, w - pad, size), size, fill=col, shadow=(state != "selected"))
    return s + "</g>"

def segmented(x, y, w, h, labels, active, size=FS_BODY, widths=None):
    s = rrect(x, y, w, h, h / 2, TH["base"], TH["track_op"], TH["chip_rim"], 0.4, TH["chip_rim_op"])
    widths = widths or [w / len(labels)] * len(labels); cx = x
    for i, (lab, cw) in enumerate(zip(labels, widths)):
        if i == active:
            s += rrect(cx + 2, y + 2, cw - 4, h - 4, (h - 4) / 2, TH["sel_fill"], 0.96) + label(cx + cw / 2, y + h / 2, lab, size, fill=TH["sel_text"], weight="600", shadow=False)
        else: s += label(cx + cw / 2, y + h / 2, lab, size)
        cx += cw
    return s


def thumb(x, y, w, h, item_id):
    c = CELLS[item_id]; ar = THUMB["cellWidth"] / THUMB["cellHeight"]
    tw_, th_ = (h * ar, h) if w / h > ar else (w, w / ar)
    return (f'<svg x="{x + (w - tw_)/2:.2f}" y="{y + (h - th_)/2:.2f}" width="{tw_:.2f}" height="{th_:.2f}" viewBox="{c["x"]} {c["y"]} {THUMB["cellWidth"]} {THUMB["cellHeight"]}">'
            f'<image href="thumbs-atlas.png" width="{THUMB["width"]}" height="{THUMB["height"]}" preserveAspectRatio="none"/></svg>')

# ---- annotations (what each interactive element does) ----
NOTES = []
def note(x, y, text_): NOTES.append((x, y, text_))

# ---- level-2 helpers: section title and text toggles ----
def text_toggle(x, y, labels, active, size=FS_SMALL, gap=16, clip_w=None):
    """Quiet text-only options with a thin underline under the active one. y = vertical centre. Returns (svg, total_width)."""
    s = ""; cx = x
    for i, lab in enumerate(labels):
        w = tw(lab, size, i == active)
        if i == active:
            s += text(cx, y + size * 0.36, lab, size, weight="600", shadow=False) + rrect(cx, y + size * 0.36 + 5, w, 1.8, 0.9, TH["fg"], 0.9)
        else: s += text(cx, y + size * 0.36, lab, size, op=TH["sec_op"], shadow=False)
        cx += w + gap
    return s, cx - gap - x

# ---- frame: top strip and tab control only (no footer) ----
TABS = ["Style", "Furniture", "AI Designer", "Tidy up"]
def frame(h, tab, undo="normal", redo="disabled", grabbed=False, hint_tab=None, gear="normal"):
    s = rrect(2, 7, PW, h, R_PANEL, "#000000", TH["shadow"], extra='filter="url(#ps)"')
    s += rrect(0, 0, PW, h, R_PANEL, TH["tint"], TH["scrim"]) + rrect(0, 0, PW, h, R_PANEL, "url(#sheen)", 1.0)
    s += rrect(0.25, 0.25, PW - 0.5, h - 0.5, R_PANEL - 0.25, "none", 1, TH["rim"], 0.5, 0.8) + rrect(-0.2, -0.2, PW + 0.4, h + 0.4, R_PANEL + 0.2, "none", 1, "#000000", 0.4, 0.06)
    s += chip(PAD, TOP, 28, 28, None, undo, content=lambda c: icon("undo", PAD + 6, TOP + 6, 16, c))
    s += chip(PAD + 32, TOP, 28, 28, None, redo, content=lambda c: icon("redo", PAD + 38, TOP + 6, 16, c))
    hw = 44 if grabbed else 36
    s += rrect(PW / 2 - hw / 2, TOP + 12, hw, 4, 2, TH["ring"] if grabbed else TH["fg"], 1.0 if grabbed else 0.28)
    s += chip(PW - PAD - 60, TOP, 28, 28, None, gear, ring=False, content=lambda c: icon("settings", PW - PAD - 54, TOP + 6, 16, c))
    s += chip(PW - PAD - 28, TOP, 28, 28, None, "normal", content=lambda c: icon("minimize", PW - PAD - 22, TOP + 6, 16, c))
    note(PAD + 14, TOP + 14, "Undo the last change (one AI plan is one step)"); note(PAD + 46, TOP + 14, "Redo")
    note(PW / 2, TOP + 14, "Handle: pinch and hold to move the panel; double-pinch to bring it back"); note(PW - PAD - 46, TOP + 14, "Settings: opens Appearance (Light / Dark)")
    note(PW - PAD - 14, TOP + 14, "Minimize to a pill; pinch the pill to open")
    base = [tw(t, FS_TAB, True) for t in TABS]; pad = (IW - sum(base)) / 4
    widths = [b + pad for b in base]
    s += segmented(PAD, TABS_Y, IW, SEG, TABS, tab, size=FS_TAB, widths=widths)
    note(PW / 2, TABS_Y + SEG / 2, "Level 1: tabs switch the whole panel")
    if hint_tab is not None:
        x = PAD + sum(widths[:hint_tab]); s += rrect(x - 1, TABS_Y - 1, widths[hint_tab] + 2, SEG + 2, SEG / 2 + 1, "none", 1, TH["ring"], 1.2, 0.95, 'stroke-dasharray="3 2.5"')
    return s

def toast(text_, action="Undo", refused=False, left=None, top=None):
    """Feedback pill that floats above the panel for about 2 s (outside it, so nothing shifts). Returns (svg, y_offset_from_panel_top)."""
    w = tw(text_, FS_SMALL) + (tw(action, FS_SMALL, True) + 28 if action else 0) + 28; h = 30
    x = (PW - w) / 2 if left is None else left; y = -h - 10 if top is None else top
    fill = RED if refused else (NEAR_BLACK if TH["name"] == "light" else "#FFFFFF"); col = "#FFFFFF" if (refused or TH["name"] == "light") else INK
    s = rrect(x + 1, y + 4, w, h, h / 2, "#000000", TH["shadow"], extra='filter="url(#ps)"') + rrect(x, y, w, h, h / 2, fill, 0.95)
    s += text(x + 14, y + h / 2 + FS_SMALL * 0.36, text_, FS_SMALL, col, shadow=False)
    if action: s += text(x + w - 14, y + h / 2 + FS_SMALL * 0.36, action, FS_SMALL, "#7DBBFF" if TH["name"] == "light" and not refused else (TH["ring"] if not refused else "#FFFFFF"), "end", "600", shadow=False)
    return s

def hint_chip(text_, h, left=None, top=None):
    """Inline instruction, only while an action is pending: a small glass chip hanging under the panel."""
    w = tw(text_, FS_SMALL) + 44; x = (PW - w) / 2 if left is None else left; y = h + 10 if top is None else top
    s = rrect(x + 1, y + 4, w, 28, 14, "#000000", TH["shadow"], extra='filter="url(#ps)"') + rrect(x, y, w, 28, 14, TH["tint"], TH["scrim"]) + rrect(x, y, w, 28, 14, "url(#sheen)", 1)
    s += rrect(x + 0.25, y + 0.25, w - 0.5, 27.5, 13.8, "none", 1, TH["rim"], 0.5, 0.8) + f'<circle cx="{x+16}" cy="{y+14}" r="3.2" fill="{TH["ring"]}"/>'
    return s + text(x + 26, y + 14 + FS_SMALL * 0.36, text_, FS_SMALL, shadow=False)

# ---- Style ----
SHORT_FIN = {"floor-oak-planks": "Oak", "floor-birch-planks": "Birch", "floor-dark-planks": "Dark", "floor-concrete-tiles": "Tiles",
             "wall-cream": "Cream", "wall-soft-stripes": "Lines", "wall-olive": "Olive", "wall-navy-panels": "Navy"}
MOODS = [("Morning", "#FFE3B8", "#F9C0B2"), ("Day", "#CFE6FA", "#F8F2E2"), ("Evening", "#F6A25E", "#8E5C9B"), ("Night", "#46528A", "#161E3C")]
COLW = IW / 5

def swatch(cx, y, lab, kind, state, mood=None, real="eye"):
    """34 mm disc with the label (13 mm) below. Every option swatch is a circle. kind: None (keep real) | finish id | 'mood'.
    Real = a transparent disc with a thin ring and an eye glyph (the room shows through: 'as is')."""
    sz = 34; x = cx - sz / 2; dy = -1.0 if state == "hover" else 0; cy = y + sz / 2 + dy; r = sz / 2
    s = f'<g opacity="{0.4 if state == "disabled" else 1}">'
    if kind is None:
        s += f'<circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r - 0.6}" fill="none" stroke="{TH["fg"]}" stroke-opacity="0.55" stroke-width="1.1"/>'
        if real == "eye": s += icon("eye", cx - 9, cy - 9, 18, TH["fg"], 0.8)
        else: s += f'<path d="M{cx-9:.1f} {cy+8:.1f} L{cx-9:.1f} {cy-4:.1f} L{cx:.1f} {cy-9:.1f} L{cx+9:.1f} {cy-4:.1f} L{cx+9:.1f} {cy+8:.1f} M{cx-9:.1f} {cy-4:.1f} L{cx:.1f} {cy+1:.1f} L{cx+9:.1f} {cy-4:.1f} M{cx:.1f} {cy+1:.1f} L{cx:.1f} {cy+11:.1f}" fill="none" stroke="{TH["fg"]}" stroke-opacity="0.75" stroke-width="1.2" stroke-linejoin="round"/>'
    elif kind == "plain":
        s += f'<circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}" fill="{FIN["floor-oak-planks"]["baseColor"]}" stroke="#000000" stroke-opacity="0.15" stroke-width="0.4"/>'
    elif kind.startswith("thumb:"):
        iid = kind[6:]; cid = f"t{iid}{int(x)}{int(y)}"
        s += f'<circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}" fill="{TH["base"]}" fill-opacity="{0.12 if TH["name"] == "light" else 0.22}" stroke="{TH["chip_rim"]}" stroke-opacity="0.2" stroke-width="0.4"/>'
        s += f'<clipPath id="{cid}"><circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}"/></clipPath><g clip-path="url(#{cid})">{thumb(x + 1.5, y + dy + 4, sz - 3, sz - 8, iid)}</g>'
    elif kind == "mood":
        s += f'<circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}" fill="url(#m{mood})" stroke="#000000" stroke-opacity="0.15" stroke-width="0.4"/>'
    else:
        f = FIN[kind]; cid = f"c{kind}{int(x)}{int(y)}"
        s += f'<circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}" fill="{f["baseColor"]}" stroke="#000000" stroke-opacity="0.15" stroke-width="0.4"/>'
        s += f'<clipPath id="{cid}"><circle cx="{cx:.2f}" cy="{cy:.2f}" r="{r}"/></clipPath><g clip-path="url(#{cid})">'
        if f["pattern"] == "planks":
            for k in (1, 2, 3): s += f'<line x1="{x}" x2="{x+sz}" y1="{y+dy+k*sz/4:.1f}" y2="{y+dy+k*sz/4:.1f}" stroke="{f["accentColor"]}" stroke-width="0.8"/>'
        elif f["pattern"] == "tiles":
            for k in (1, 2, 3): s += f'<line x1="{x+k*sz/4:.1f}" x2="{x+k*sz/4:.1f}" y1="{y+dy}" y2="{y+dy+sz}" stroke="{f["accentColor"]}" stroke-width="0.8"/>'
            s += f'<line x1="{x}" x2="{x+sz}" y1="{y+dy+sz/2:.1f}" y2="{y+dy+sz/2:.1f}" stroke="{f["accentColor"]}" stroke-width="0.8"/>'
        elif f["pattern"] == "stripes":
            for k in range(1, 6): s += f'<line x1="{x+k*sz/6:.1f}" x2="{x+k*sz/6:.1f}" y1="{y+dy}" y2="{y+dy+sz}" stroke="{f["accentColor"]}" stroke-width="1.1"/>'
        s += "</g>"
    if state == "selected": s += f'<circle cx="{cx:.2f}" cy="{y+sz/2:.2f}" r="{r+2.4}" fill="none" stroke="{TH["ring"]}" stroke-width="1.4" stroke-opacity="0.95"/>'
    s += text(cx, y + sz + 5 + FS_SMALL * 0.72 + 1, lab, FS_SMALL, anchor="middle", weight="400", op=1.0 if state == "selected" else TH["sec_op"] + 0.2, shadow=False)
    return s + "</g>"

def tab_style(floor=1, wall=0, mood=2, zone=0, hover=None):
    y0 = BODY_Y; fy = y0 + 24 + 16; wy = fy + 22 + 52 + 12; my = wy + 22 + 52 + 12; h = my + 22 + 52 + PAD
    s = frame(h, 0)
    s += text(PAD, y0 + 12 + FS_SMALL * 0.36 - 3, "Apply to", FS_SMALL, weight="600", op=TH["sec_op"], shadow=False)
    t, _ = text_toggle(PAD + 62, y0 + 12 - 3, ["My zone", "Outside"], zone); s += t
    note(PAD + 80, y0 + 9, "Level 2: choose where floor and wall changes apply")
    floors = [i for i, f in FIN.items() if f["surface"] == "floor"]; walls = [i for i, f in FIN.items() if f["surface"] == "wall"]
    for y, name, ids, sel in ((fy, "Floor", floors, floor), (wy, "Wall", walls, wall)):
        s += section(PAD, y, name)
        labs = ["Real"] + [SHORT_FIN[i] for i in ids]; kinds = [None] + ids
        for k in range(5):
            st = "selected" if sel == k else ("hover" if hover == (name, k) else "normal")
            s += swatch(PAD + COLW * (k + 0.5), y + 22, labs[k], kinds[k], st)
        note(PAD + COLW * 1.5, y + 22 + 17, f"{name}: pinch a swatch to apply it; Real keeps the real {name.lower()}")
    s += section(PAD, my, "Mood")
    labs = ["Real"] + [m[0] for m in MOODS]
    for k in range(5):
        st = "selected" if mood + 1 == k else "normal"
        s += swatch(PAD + COLW * (k + 0.5), my + 22, labs[k], None if k == 0 else "mood", st, mood=(k - 1) if k else -1)
    note(PAD + COLW * 2.5, my + 22 + 17, "Mood: light of the whole room; blends over 2.5 s")
    return s, h

# ---- Furniture ----
GROUPS = [("Seating", ("sofa", "armchair")), ("Beds", ("bed",)), ("Tables", ("desk", "coffee-table")), ("Storage", ("closet", "shelf")),
          ("Lights", ("lamp", "pendant-lamp")), ("Decor", ("plant", "rug", "wall-art", "clutter-cover")), ("Window", ("curtain", "door-trim"))]
KIND_WORDS = {"sofa", "armchair", "desk", "table", "bed", "lamp", "shelf", "rug", "closet", "chair", "shelves", "frame"}
def short_name(it):
    w = it["name"].split()
    return " ".join(w[:-1]) if len(it["name"]) > 15 and len(w) > 2 and w[-1].lower() in KIND_WORDS else it["name"]

def thumb(x, y, w, h, item_id):
    c = CELLS[item_id]; ar = THUMB["cellWidth"] / THUMB["cellHeight"]
    tw_, th_ = (h * ar, h) if w / h > ar else (w, w / ar)
    return (f'<svg x="{x + (w - tw_)/2:.2f}" y="{y + (h - th_)/2:.2f}" width="{tw_:.2f}" height="{th_:.2f}" viewBox="{c["x"]} {c["y"]} {THUMB["cellWidth"]} {THUMB["cellHeight"]}">'
            f'<image href="thumbs-atlas.png" width="{THUMB["width"]}" height="{THUMB["height"]}" preserveAspectRatio="none"/></svg>')

def tab_furniture(group=0, sel=1, hover=2, refused=None, scroll=0):
    y0 = BODY_Y; ch = 92; cw = (IW - 2 * GAP) / 3; cy = y0 + 24 + 12
    h = cy + 2 * ch + GAP + GAP + 26 + PAD
    s = frame(h, 1)
    # one row of quiet text options (level 2), scrollable: clipped at the row edge with a soft fade
    labels = [g[0] for g in GROUPS]
    row, total = text_toggle(PAD - scroll, y0 + 12, labels, group, gap=18)
    s += f'<mask id="fade"><rect x="{PAD-6}" y="{y0-4}" width="{IW+12-30}" height="36" fill="#fff"/><rect x="{PAD+IW-30}" y="{y0-4}" width="36" height="36" fill="url(#fadeg)"/></mask><g mask="url(#fade)">{row}</g>'
    note(PAD + 20, y0 + 12, "Level 2: sub-category (drag the row sideways for more)")
    items = sorted([i for i in CAT.values() if i["kind"] in GROUPS[group][1]], key=lambda i: i["id"])[:6]
    for n, it in enumerate(items):
        x = PAD + (n % 3) * (cw + GAP); yy = cy + (n // 3) * (ch + GAP)
        st = "selected" if n == sel else ("hover" if n == hover else "normal")
        if n == refused: st = "refused"
        def inner(c, x=x, yy=yy, it=it):
            o = thumb(x + 6, yy + 6, cw - 12, 46, it["id"])
            for li, ln in enumerate(wrap(short_name(it), cw - 14, FS_CARD, 2)): o += text(x + cw / 2, yy + 52 + 11 + li * 15, ln, FS_CARD, c, "middle", shadow=False)
            return o
        s += chip(x, yy, cw, ch, None, st, r=R_TILE + 2, content=inner)
    note(PAD + cw / 2, cy + 20, "Level 3: pinch a design, then pinch the floor to place it")
    by = h - PAD - 26
    s += chip(PW - PAD - 28, by, 28, 26, None, "normal", ring=False, content=lambda c: icon("chevron-right", PW - PAD - 22, by + 5, 16, c))
    s += text(PW - PAD - 28 - 22, by + 17, "1 / 3", FS_SMALL, anchor="middle", op=TH["sec_op"], shadow=False)
    s += chip(PW - PAD - 28 - 44 - 28, by, 28, 26, None, "disabled", ring=False, content=lambda c: icon("chevron-left", PW - PAD - 28 - 44 - 22, by + 5, 16, c))
    note(PW - PAD - 14, by + 13, "Next page (no scrolling in the grid)")
    return s, h

# ---- AI Designer ----
AI_H = 30; AI_FS = 13   # the "Describe your space" field and the style chips: a little smaller than the tab bar (36 high, 14 mm text)
INTENTS = ["Living room", "Study caf\u00e9", "Bedroom", "Bright studio"]
PLACEHOLDER = "Describe your space\u2026"
def input_field(y, state, value=""):
    """Glass text field with a mic glyph: pinch opens the Quest system keyboard (its mic key dictates). It is the only input in the panel."""
    h = AI_H; s = rrect(PAD, y, IW, h, h / 2, TH["base"], TH["track_op"] * 1.4, TH["chip_rim"], 0.5, TH["chip_rim_op"] * 1.4)
    if state == "typing": s += rrect(PAD - 2.4, y - 2.4, IW + 4.8, h + 4.8, h / 2 + 2.4, "none", 1, TH["ring"], 1.3, 0.95)
    dim_ = 0.45 if state == "busy" else 1.0
    if value:
        s += text(PAD + 16, y + h / 2 + AI_FS * 0.36, clip(value, IW - 80, AI_FS), AI_FS, op=dim_, shadow=False)
        if state == "typing": s += rrect(PAD + 16 + tw(clip(value, IW - 80, AI_FS), AI_FS) + 2, y + 9, 1.4, h - 18, 0.7, TH["ring"])
    else: s += text(PAD + 16, y + h / 2 + AI_FS * 0.36, PLACEHOLDER, AI_FS, op=TH["sec_op"] * 0.8, shadow=False)
    if state == "busy": s += f'<path d="M{PW-PAD-30} {y+h/2-7} a7 7 0 0 1 7 7" fill="none" stroke="{TH["ring"]}" stroke-width="2" stroke-linecap="round"/>'
    else: s += icon("mic", PW - PAD - 34, y + h / 2 - 8, 16, TH["ring"] if state == "typing" else TH["fg"], 1.0 if state == "typing" else 0.75)
    note(PAD + 40, y + h / 2, "Describe your space: pinch opens the system keyboard, its mic key dictates. Sent as text, no audio")
    note(PW - PAD - 27, y + h / 2, "Mic: same as the keyboard's mic key (dictation)")
    return s

def flow(x0, maxw, labels, size=AI_FS, h=AI_H, gap=GAP, pad=20):
    """Content-hugging chips laid out left to right, wrapping to the next row. Returns ([(x, row, w)], rows)."""
    out = []; x = 0; row = 0
    for lab in labels:
        w = tw(lab, size) + pad
        if x > 0 and x + w > maxw: x = 0; row += 1
        out.append((x0 + x, row, w)); x += w + gap
    return out, row + 1

def tab_ask(active=None, busy=False, result=False, offline=False, field="empty", value="", quote=None):
    """field: empty | typing | busy | done. result: the AI's plan card is shown (only after the AI has run)."""
    y0 = BODY_Y; show_card = busy or result
    pos, rows = flow(PAD, IW, INTENTS)
    c0 = y0 + AI_H + 12 + 22; cy = c0 + rows * (AI_H + GAP) + 4
    chh = (150 if quote else 124) if show_card else 32
    h = cy + chh + GAP + CHIP_S + PAD
    s = frame(h, 2, undo="normal" if result and not busy else "disabled")
    s += input_field(y0, "busy" if busy else field, value)
    s += section(PAD, y0 + AI_H + 12, "Or pick a style")
    for k, ((x, r, w), lab) in enumerate(zip(pos, INTENTS)):
        y = c0 + r * (AI_H + GAP)
        st = "selected" if (k == active and result and not busy) else ("disabled" if busy and k != active else "normal")
        s += chip(x, y, w, AI_H, lab, st, size=AI_FS)
        if busy and k == active: s += rrect(x - 2.4, y - 2.4, w + 4.8, AI_H + 4.8, AI_H / 2 + 2.4, "none", 1, TH["ring"], 1.3, 0.95)
    note(PAD + pos[0][2] / 2, c0 + AI_H / 2, "Quick picks: ask the AI for this style (same as describing it)")
    if not show_card:   # before the AI has run: one quiet line, no card
        s += text(PAD + 4, cy + 14, "The AI will furnish the outside zone", FS_SMALL, op=TH["sec_op"], shadow=False)
    else:   # AI output card: sparkle title, accent hairline, the request quoted, Undo plan inside
        s += rrect(PAD, cy, IW, chh, 20, TH["base"], TH["chip_op"] * 0.8, TH["ring"], 0.8, 0.45)
        s += icon("sparkle", PAD + 16, cy + 6, 15, TH["ring"]) + text(PAD + 36, cy + 12 + FS_SMALL * 0.36 + 6, "AI\u2019s plan", FS_SMALL, weight="600", op=TH["sec_op"], shadow=False)
        ty = cy + 40
        if quote:
            s += text(PAD + 16, ty, clip("\u201c" + quote + "\u201d", IW - 32, FS_SMALL), FS_SMALL, op=TH["sec_op"], shadow=False); ty += 18
        if busy:
            s += text(PAD + 16, ty + 14, "Thinking\u2026", AI_FS) + text(PAD + 16, ty + 34, "Placing furniture", FS_SMALL, op=TH["sec_op"], shadow=False)
            s += chip(PAD + 16, cy + chh - 16 - CHIP_S, tw("Cancel", FS_SMALL) + 24, CHIP_S, "Cancel", size=FS_SMALL)
            note(PAD + 40, cy + chh - 16 - CHIP_S / 2, "Cancel: nothing is applied")
        else:
            if offline: s += text(PW - PAD - 16, cy + 24, "Offline plan", FS_SMALL, anchor="end", op=TH["sec_op"], shadow=False)
            for li, ln in enumerate(wrap("Sofa and shelves on the long wall, a lamp by the window.", IW - 32, AI_FS, 2)): s += text(PAD + 16, ty + 14 + li * 19, ln, AI_FS)
            s += text(PAD + 16, cy + chh - 16 - 9, "7 placed \u00b7 1 skipped", FS_SMALL, op=TH["sec_op"], shadow=False)
            uw = tw("Undo plan", FS_SMALL) + 24
            s += chip(PW - PAD - 16 - uw, cy + chh - 16 - CHIP_S, uw, CHIP_S, "Undo plan", size=FS_SMALL)
            note(PW - PAD - 16 - uw / 2, cy + chh - 16 - CHIP_S / 2, "Undo plan: removes everything the AI placed")
        note(PAD + 24, cy + 14, "AI output (not an input): appears only after the AI has run")
    sw_ = tw("Suggest for the selected piece", FS_SMALL) + 24
    s += chip(PAD, cy + chh + GAP, sw_, CHIP_S, "Suggest for the selected piece", "disabled" if busy else "normal", size=FS_SMALL)
    note(PAD + sw_ / 2, cy + chh + GAP + CHIP_S / 2, "Suggest: designs for the selected piece (needs a selection)")
    return s, h

# ---- Hide ----
PROPS = ["Basket", "Plant", "Cushions", "Box", "Cushion", "Screen"]
def tab_hide_before(mode=1, prop=0, hover=None):
    y = BODY_Y; cwm = (IW - GAP) / 2
    h = (y + CHIP + GAP + 76 + GAP + CHIP + SEC + 56 + PAD) if mode == 0 else (y + CHIP + GAP + 2 * CHIP + GAP + GAP + CHIP + SEC + 56 + PAD)
    s = frame(h, 3)
    s += chip(PAD, y, cwm, CHIP, "Draw a box", "selected" if mode == 0 else "normal", size=FS_SMALL) + chip(PAD + cwm + GAP, y, cwm, CHIP, "Cover with prop", "selected" if mode == 1 else "normal", size=FS_SMALL)
    note(PAD + cwm / 2, y + CHIP / 2, "Level 3: choose how to tidy (draw a box, or cover clutter)"); y += CHIP + GAP + 4
    if mode == 0:
        for li, ln in enumerate(["1  Pinch and hold at one corner", "2  Release at the opposite corner", "3  Set the height, then Add"]): s += text(PAD + 4, y + 14 + li * 24, ln, FS_BODY)
        s += chip(PAD, y + 76 + GAP - 4, cwm, CHIP, "Add", "selected") + chip(PAD + cwm + GAP, y + 76 + GAP - 4, cwm, CHIP, "Done")
        note(PAD + cwm / 2, y + 76 + GAP - 4 + CHIP / 2, "Add: keeps the box and covers it with the floor or wall")
        ny = y + 76 + GAP - 4 + CHIP + SEC
    else:
        cw = (IW - 2 * GAP) / 3
        for n, p in enumerate(PROPS):
            x = PAD + (n % 3) * (cw + GAP); yy = y + (n // 3) * (CHIP + GAP)
            s += chip(x, yy, cw, CHIP, p, "selected" if prop == n else ("hover" if hover == n else "normal"), size=FS_SMALL)
        note(PAD + cw / 2, y + CHIP / 2, "Props: cover the selected hide box with this prop")
        s += chip(PAD, y + 2 * (CHIP + GAP), IW, CHIP, "Remove this cover")
        ny = y + 2 * (CHIP + GAP) + CHIP + SEC
    s += rrect(PAD, ny, IW, 56, 16, TH["base"], TH["chip_op"] * 0.7, TH["chip_rim"], 0.4, TH["chip_rim_op"])
    s += text(PAD + 16, ny + 24, "Hidden things are still there.", FS_SMALL, weight="600", shadow=False) + text(PAD + 16, ny + 42, "Mind your step when you stand up.", FS_SMALL, op=TH["sec_op"] + 0.15, shadow=False)
    return s, h

# ---- Tidy up: two steps (draw a box over the clutter, choose how it looks) ----
LOOKS = [("Plain", "plain"), ("Basket", "thumb:clutter-basket-warm-wood-01"), ("Plant", "thumb:clutter-plant-warm-wood-01"), ("Cushion", "thumb:clutter-floor-cushion-warm-wood-01"),
         ("Box", "thumb:clutter-box-warm-wood-01"), ("Screen", "thumb:clutter-screen-warm-wood-01")]
def tab_hide_v6(state="ready", look=None):
    """state: idle (no box yet) | drawing | ready (a box is selected, pick a look) | cover (an existing cover is selected)."""
    y = BODY_Y; row = 28
    h = y + row + 16 + 22 + 52 + 12 + 30 + PAD + (24 if state == "cover" else 0)
    s = frame(h, 3)
    s += text(PAD, y + row / 2 + FS_SMALL * 0.36, "1  Draw a box over the clutter", FS_SMALL, weight="600", op=TH["sec_op"], shadow=False)
    if state == "drawing": s += chip(PW - PAD - 72, y, 72, row, "Cancel", size=FS_SMALL)
    else: s += chip(PW - PAD - 72, y, 72, row, "Redraw" if state in ("ready", "cover") else "Draw", "normal", size=FS_SMALL)
    note(PW - PAD - 36, y + row / 2, "Draw: then pinch and drag across the floor; release to set the box")
    y2 = y + row + 16
    enabled = state in ("ready", "cover")
    s += f'<g opacity="{1 if enabled else 0.4}">' + text(PAD, y2 + 12, "2  Choose how it looks", FS_SMALL, weight="600", op=TH["sec_op"], shadow=False)
    colw = IW / 6
    for k, (lab, kind) in enumerate(LOOKS):
        st = "selected" if (look == k and enabled) else "normal"
        s += swatch(PAD + colw * (k + 0.5), y2 + 22, lab, kind, st)
    s += "</g>"
    note(PAD + colw * 0.5, y2 + 22 + 17, "Plain: covers the box with the floor or wall; props: a basket, plant, cushion, storage box or screen")
    ny = y2 + 22 + 52 + 12
    if state == "cover":
        s += text(PW / 2, ny + 11, "Remove this cover", FS_SMALL, "#C62F22" if TH["name"] == "light" else "#FF9A90", "middle", "400", shadow=False)
        s += rrect(PW / 2 - tw("Remove this cover", FS_SMALL) / 2, ny + 15, tw("Remove this cover", FS_SMALL), 0.8, 0.4, "#C62F22" if TH["name"] == "light" else "#FF9A90", 0.7)
        note(PW / 2, ny + 8, "Remove this cover: only when an existing cover is selected")
        ny += 24
    s += icon("info", PAD, ny + 1, 13, TH["fg"], TH["sec_op"])
    for li, ln in enumerate(wrap("Hidden things are still there. Mind your step when you stand up.", IW - 20, FS_CARD, 2)): s += text(PAD + 19, ny + 10 + li * 15, ln, FS_CARD, op=TH["sec_op"], shadow=False)
    return s, h

def step_art(x, y, w, h, refused=False):
    """Small illustration for step 1: clutter on the floor with a dashed box being dragged over it."""
    s = rrect(x, y, w, h, 14, TH["base"], TH["chip_op"] * 0.8, TH["chip_rim"], 0.4, TH["chip_rim_op"])
    fx0, fx1, fy0, fy1 = x + 14, x + w - 14, y + 18, y + h - 10
    s += f'<polygon points="{fx0+14},{fy0} {fx1-14},{fy0} {fx1},{fy1} {fx0},{fy1}" fill="{TH["fg"]}" fill-opacity="0.08"/>'
    cx = x + w * 0.5
    for (dx, dy, r, c) in ((-26, 8, 6, "#C9A27A"), (-8, 14, 5, "#8E9B7A"), (10, 6, 7, "#B96A4B"), (26, 14, 5, "#7C8896")): s += f'<circle cx="{cx+dx:.1f}" cy="{y+h/2+dy:.1f}" r="{r}" fill="{c}" fill-opacity="0.85"/>'
    bc = RED if refused else TH["ring"]
    s += rrect(cx - 40, y + h / 2 - 8, 80, 36, 6, "none", 1, bc, 1.3, 0.95, 'stroke-dasharray="3.2 2.4"')
    for (px_, py_) in ((cx - 40, y + h / 2 - 8), (cx + 40, y + h / 2 - 8), (cx - 40, y + h / 2 + 28), (cx + 40, y + h / 2 + 28)): s += f'<circle cx="{px_:.1f}" cy="{py_:.1f}" r="2.2" fill="{bc}"/>'
    s += f'<path d="M{cx+52:.1f} {y+h/2-18:.1f} L{cx+41:.1f} {y+h/2-9:.1f}" stroke="{TH["fg"]}" stroke-width="1.4" stroke-linecap="round" stroke-opacity="0.7" fill="none"/><circle cx="{cx+53:.1f}" cy="{y+h/2-19:.1f}" r="4" fill="{TH["fg"]}" fill-opacity="0.75"/>'
    return s

STEPS = ["Draw a box", "Pick a look", "Done"]
def stepper(y, current):
    """Numbered stepper: completed = check, current = graphite (white on dark), future = muted outline. Returns (svg, bottom y)."""
    d = 18; s = ""; widths = [d + 6 + tw(t, FS_SMALL, i == current) for i, t in enumerate(STEPS)]
    gap = (IW - sum(widths)) / 2; x = PAD
    for i, t in enumerate(STEPS):
        cy = y + d / 2; done = i < current; cur = i == current
        if cur: s += f'<circle cx="{x+d/2:.1f}" cy="{cy:.1f}" r="{d/2}" fill="{TH["sel_fill"]}"/>' + text(x + d / 2, cy + 4.2, str(i + 1), FS_CARD, TH["sel_text"], "middle", "600", shadow=False)
        elif done: s += f'<circle cx="{x+d/2:.1f}" cy="{cy:.1f}" r="{d/2}" fill="none" stroke="{TH["fg"]}" stroke-opacity="0.6" stroke-width="0.9"/>' + icon("check", x + 4, cy - 5, 10, TH["fg"], 0.8)
        else: s += f'<circle cx="{x+d/2:.1f}" cy="{cy:.1f}" r="{d/2}" fill="none" stroke="{TH["fg"]}" stroke-opacity="0.3" stroke-width="0.9"/>' + text(x + d / 2, cy + 4.2, str(i + 1), FS_CARD, op=0.4, anchor="middle", shadow=False)
        s += text(x + d + 6, cy + FS_SMALL * 0.36, t, FS_SMALL, weight="600" if cur else "400", op=1.0 if cur else (0.8 if done else 0.45), shadow=False)
        x += widths[i] + (gap if i < 2 else 0)
        if i < 2: s += f'<line x1="{x - gap + 4:.1f}" y1="{cy:.1f}" x2="{x - 4:.1f}" y2="{cy:.1f}" stroke="{TH["fg"]}" stroke-opacity="{0.35 if i < current else 0.18}" stroke-width="0.8"/>'
    note(PAD + 9, y + 9, "Steps: current is graphite, done steps get a check, future steps are muted")
    return s, y + d

LOOKS = [("Plain", "plain"), ("Basket", "thumb:clutter-basket-warm-wood-01"), ("Plant", "thumb:clutter-plant-warm-wood-01"), ("Cushion", "thumb:clutter-floor-cushion-warm-wood-01"),
         ("Box", "thumb:clutter-box-warm-wood-01"), ("Screen", "thumb:clutter-screen-warm-wood-01")]
def tab_hide(step=1, look=None, drawing=False, existing=False, refused=False):
    """step 0: draw a box (instruction and picture), 1: pick a look (Plain or a prop), 2: done (Add another, Remove).
    existing=True: the user selected an existing cover, so the tab opens on step 2 for that cover (Remove shown)."""
    y = BODY_Y; body_h = {0: 22 + 84 + (36 if drawing else 0), 1: 22 + 52 + (22 if existing else 0), 2: 70}[step]
    h = y + 18 + 14 + body_h + 14 + 30 + PAD
    s = frame(h, 3)
    st, by = stepper(y, step); s += st; ay = by + 14
    if step == 0:
        s += text(PAD, ay + 11, "Can\u2019t cover a window. Draw somewhere else." if refused else "Pinch and drag on the floor over the clutter", FS_SMALL, RED if refused and TH["name"] == "dark" else ("#C62F22" if refused else None), op=1.0 if refused else TH["sec_op"] + 0.15, shadow=False)
        s += step_art(PAD, ay + 22, IW, 84, refused)
        if drawing: s += chip(PW - PAD - (tw("Cancel", FS_SMALL) + 24), ay + 22 + 84 + 8, tw("Cancel", FS_SMALL) + 24, CHIP_S, "Cancel", size=FS_SMALL)
        note(PAD + 20, ay + 11, "Step 1: pinch and drag on the floor; on release a valid box goes to step 2 automatically, a refused box stays here with a red reason")
    elif step == 1:
        s += section(PAD, ay, "Pick a look")
        colw = IW / 6
        for k, (lab, kind) in enumerate(LOOKS): s += swatch(PAD + colw * (k + 0.5), ay + 22, lab, kind, "selected" if look == k else "normal")
        note(PAD + colw * 0.5, ay + 22 + 17, "Step 2 (automatic after a valid box); choosing a look goes to step 3 automatically. Plain covers the box with the floor or wall; props: basket, plant, cushion, storage box, screen")
        if existing:
            rw = tw("Remove this cover", FS_SMALL); s += text(PW - PAD, ay + 22 + 52 + 16, "Remove this cover", FS_SMALL, "#C62F22" if TH["name"] == "light" else "#FF9A90", "end", "400", shadow=False)
            note(PW - PAD - rw / 2, ay + 22 + 52 + 12, "Remove this cover: shown when you select an existing cover (the tab opens here)")
    else:
        s += text(PAD, ay + 14, "Covered with a basket.", FS_BODY) + text(PAD, ay + 33, "The clutter is hidden. Pinch it to change.", FS_SMALL, op=TH["sec_op"], shadow=False)
        aw = tw("Add another", FS_SMALL) + 24
        s += chip(PAD, ay + 44, aw, CHIP_S, "Add another", size=FS_SMALL)
        rr = "#C62F22" if TH["name"] == "light" else "#FF9A90"
        s += text(PAD + aw + 20, ay + 44 + CHIP_S / 2 + FS_SMALL * 0.36, "Remove", FS_SMALL, rr, shadow=False)
        note(PAD + aw / 2, ay + 44 + CHIP_S / 2, "Add another: back to step 1 for a new box"); note(PAD + aw + 36, ay + 44 + CHIP_S / 2, "Remove: undoes this cover (quiet red text)")
    ny = h - PAD - 30
    s += icon("info", PAD, ny + 1, 13, TH["fg"], TH["sec_op"])
    for li, ln in enumerate(wrap("Hidden things are still there. Mind your step when you stand up.", IW - 20, FS_CARD, 2)): s += text(PAD + 19, ny + 10 + li * 15, ln, FS_CARD, op=TH["sec_op"], shadow=False)
    return s, h

# ---- settings (appearance) ----
def settings_sheet(h_panel, hover_gear=False, open_=True):
    """Popover opened by the gear button: a title and a Light / Dark control. Drawn over the panel's top-right."""
    pw, ph = 176, 88; px, py = PW - PAD - pw, TOP + 28 + 8
    s = ""
    if open_:
        s += rrect(px + 1, py + 5, pw, ph, 20, "#000000", TH["shadow"], extra='filter="url(#ps)"') + rrect(px, py, pw, ph, 20, TH["tint"], 0.94) + rrect(px, py, pw, ph, 20, "url(#sheen)", 1) + rrect(px + 0.25, py + 0.25, pw - 0.5, ph - 0.5, 19.8, "none", 1, TH["rim"], 0.5, 0.8)
        s += section(px + 16, py + 12, "Appearance")
        s += segmented(px + 12, py + 40, pw - 24, SEG, ["Light", "Dark"], 0 if TH["name"] == "light" else 1, size=FS_SMALL)
        note(px + pw / 2, py + 40 + SEG / 2, "Appearance: Light or Dark, applies at once")
    else:   # tooltip under the hovered gear
        w = tw("Appearance", FS_SMALL) + 24; x = PW - PAD - 46 - w / 2; y = TOP + 28 + 6
        fill = NEAR_BLACK if TH["name"] == "light" else "#FFFFFF"; col = "#FFFFFF" if TH["name"] == "light" else INK
        s += rrect(x, y, w, 24, 12, fill, 0.95) + text(x + w / 2, y + 12 + FS_SMALL * 0.36, "Appearance", FS_SMALL, col, "middle", shadow=False)
    return s

def tab_style_settings(open_=True):
    s, h = tab_style(); s = s.replace(frame(h, 0), frame(h, 0, gear="selected" if open_ else "hover"), 1) if False else s
    return s + settings_sheet(h, open_=open_), h

# ---- scenes ----
def defs():
    t, b = TH["sheen_top"], TH["sheen_bot"]
    g = "".join(f'<linearGradient id="m{i}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{a}"/><stop offset="1" stop-color="{c}"/></linearGradient>' for i, (n, a, c) in enumerate(MOODS))
    fade = "#000" if True else "#000"
    return f'''<defs>
<linearGradient id="sheen" x1="0" y1="0" x2="0.12" y2="1"><stop offset="0" stop-color="#FFFFFF" stop-opacity="{t}"/><stop offset="0.5" stop-color="#FFFFFF" stop-opacity="{b*0.5:.3f}"/><stop offset="1" stop-color="#FFFFFF" stop-opacity="{b}"/></linearGradient>
<linearGradient id="fadeg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#000000"/></linearGradient>
{g}
<filter id="ts" x="-5%" y="-30%" width="110%" height="170%"><feDropShadow dx="0" dy="0.4" stdDeviation="0.5" flood-color="#000000" flood-opacity="0.4"/></filter>
<filter id="ps" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="8"/></filter>
<filter id="cs" x="-20%" y="-30%" width="140%" height="170%"><feGaussianBlur stdDeviation="1.6"/></filter>
</defs>'''

def svg(w, h, body): return f'<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{body}</svg>'

def scene(panel, ph, night, k, w=900, top="", bottom=""):
    bg = "room-dark.jpg" if night else "room-bright.jpg"; h = int((ph + 50 + 50) * k); ox = (w / k - PW) / 2; oy = 50
    return svg(w, h, f'<g transform="scale({k})">{defs()}<image href="{bg}" x="0" y="0" width="{w/k}" height="{h/k}" preserveAspectRatio="xMidYMid slice"/><g transform="translate({ox},{oy})">{panel}{top}{bottom}</g></g>')

def pill(tabname, hover=False):
    label_a = "Own Space"; w = tw(label_a, FS_BODY) + tw(tabname, FS_BODY) + 16 * 2 + 12; h = 36
    s = rrect(2, 5, w, h, 18, "#000000", TH["shadow"], extra='filter="url(#ps)"') + rrect(0, 0, w, h, 18, TH["tint"], TH["scrim"]) + rrect(0, 0, w, h, 18, "url(#sheen)", 1.0)
    s += rrect(0.25, 0.25, w - 0.5, h - 0.5, 17.8, "none", 1, TH["rim"], 0.5, 0.8)
    if hover: s += rrect(0, 0, w, h, 18, TH["base"], TH["hover_op"])
    s += text(16, h / 2 + FS_BODY * 0.36, label_a, FS_BODY) + text(16 + tw(label_a, FS_BODY) + 12, h / 2 + FS_BODY * 0.36, tabname, FS_BODY, op=TH["sec_op"], shadow=False)
    return s, w, h

def states_sheet(night):
    inner = ""; names = [("normal", "Unselected"), ("hover", "Hover"), ("pressed", "Pressed"), ("selected", "Selected"), ("disabled", "Disabled"), ("refused", "Refused")]
    for k, (st, lab) in enumerate(names):
        x = 16 + (k % 3) * 88; y = 16 + (k // 3) * 56
        inner += chip(x, y, 76, CHIP, "Birch", st) + text(x + 38, y + CHIP + 14, lab, FS_SMALL, anchor="middle", op=TH["sec_op"], shadow=False)
    k = 3.2; w, h = 900, 520; bg = "room-dark.jpg" if night else "room-bright.jpg"
    box = rrect(0, 0, 288, 140, 28, TH["tint"], TH["scrim"]) + rrect(0, 0, 288, 140, 28, "url(#sheen)", 1) + rrect(0.25, 0.25, 287.5, 139.5, 27.8, "none", 1, TH["rim"], 0.5, 0.8)
    t = toast("Doesn’t fit this box: 2.4 m wide", None, refused=True)
    return svg(w, h, f'<g transform="scale({k})">{defs()}<image href="{bg}" x="0" y="0" width="{w/k}" height="{h/k}" preserveAspectRatio="xMidYMid slice"/>{box}{inner}<g transform="translate(0,{140+50})">{t}</g></g>')

def pill_sheet(night):
    k = 3.2; w, h = 900, 400; bg = "room-dark.jpg" if night else "room-bright.jpg"
    a, aw, _ = pill("Furniture"); b, bw, _ = pill("AI Designer", hover=True)
    def tag(x, y, t): return rrect(x, y, tw(t, FS_SMALL) + 16, 22, 11, "#FFFFFF", 0.92) + text(x + 8, y + 15, t, FS_SMALL, INK, shadow=False)
    return svg(w, h, f'<g transform="scale({k})">{defs()}<image href="{bg}" x="0" y="0" width="{w/k}" height="{h/k}" preserveAspectRatio="xMidYMid slice"/>'
               f'<g transform="translate(16,20)">{a}</g>{tag(16, 64, "Minimized: pinch to open")}<g transform="translate(16,100)">{b}</g>{tag(16, 144, "Hover: faint lift")}</g>')

def composite(night):
    k = 1.5; w, h = 1600, 1000; bg = "room-dark.jpg" if night else "room-bright.jpg"
    pan, ph = tab_furniture(0, 1, 2); pl, pw_, _ = pill("Style"); pl2, pw2, _ = pill("AI Designer")
    ox, oy = (w / k - PW) / 2 + 10, 360 / k
    def tag(x, y, t): return rrect(x, y, tw(t, FS_SMALL) + 18, 24, 12, "#FFFFFF", 0.92) + text(x + 9, y + 16.5, t, FS_SMALL, INK, shadow=False)
    return svg(w, h, f'<g transform="scale({k})">{defs()}<image href="{bg}" x="0" y="0" width="{w/k}" height="{h/k}" preserveAspectRatio="xMidYMid slice"/>'
               f'<g transform="translate({ox},{oy})">{pan}{toast("Slate lounge · outside zone")}{hint_chip("Pinch the floor to place", ph)}</g><g transform="translate(30,{oy+20})">{pl}</g><g transform="translate(30,{oy+76})">{pl2}</g>'
               f'{tag(30, oy + 126, "Minimized: pinch to open")}{tag(ox + PW + 12, oy + 4, "Panel 0.30 m wide, 0.45 m ahead, low")}</g>')

# ---- annotated sheets: the panel on a flat background with numbered markers and a legend ----
def annotated(fn, name):
    NOTES.clear(); set_theme("light"); body, ph = fn(); notes = list(NOTES)
    k = 3.4; lw = 360; W_ = int(PW * k + 60 + 760); H_ = int((ph + 40) * k)
    ox = 30; oy = 20; s = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/><g transform="translate({ox},{oy}) scale({k})">{defs()}{body}</g>'
    seen = {}; legend = ""; n = 0
    for (x, y, t) in notes:
        if t in seen: continue
        n += 1; seen[t] = n
        cx, cy = ox + x * k, oy + y * k
        s += f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="11" fill="#E5483B" stroke="#fff" stroke-width="1.5"/><text x="{cx:.1f}" y="{cy+4.5:.1f}" font-family="Helvetica" font-size="13" font-weight="700" fill="#fff" text-anchor="middle">{n}</text>'
        for li, ln in enumerate(wrap(t, 300, 11)[:3] if False else [t]):
            pass
        legend += f'<text x="{PW*k+60:.0f}" y="{40 + (n-1)*34}" font-family="Helvetica" font-size="17" fill="#222"><tspan font-weight="700" fill="#E5483B">{n}  </tspan>{escape(t)}</text>'
    return svg(W_, max(H_, 60 + n * 34), s + legend)

def real_options_sheet():
    """Two ways to say 'keep the real one' next to a normal swatch, on light and dark glass."""
    k = 3.2; W_, H_ = 1500, 650; body = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/>'
    def lab(x, y, t, size=15, w="400"): return f'<text x="{x}" y="{y}" font-family="Helvetica" font-size="{size}" font-weight="{w}" fill="#222">{escape(t)}</text>'
    body += lab(30, 36, "\u201cReal\u201d (keep the real surface, floor, wall or light): two options", 20, "700")
    x0 = 30
    for ti, theme in enumerate(("light", "dark")):
        set_theme(theme); bg = "#F3F5F8" if theme == "light" else "#222B38"
        for oi, (name, real) in enumerate((("A: ring + eye glyph (recommended)", "eye"), ("B: ring + room-corner glyph", "room"))):
            x = x0 + oi * 700; y = 70 + ti * 270
            tile = rrect(0, 0, 160, 64, 14, bg, 1) + swatch(30, 8, "Real", None, "normal", real=real) + swatch(75, 8, "Oak", "floor-oak-planks", "normal") + swatch(120, 8, "Real", None, "selected", real=real)
            body += f'<g transform="translate({x},{y}) scale({k})">{defs()}{tile}</g>' + lab(x, y + 64 * k + 24, name + f"  ({theme})")
    set_theme("light")
    body += lab(30, 630, "A reads as \u201cas is / see it as it is\u201d; B needs a second look. The panel uses A: a transparent disc with a thin ring shows the room through it.", 15)
    return svg(W_, H_, body)

def annotated_pair(before_fn, after_fn):
    """Before (left, plain) and after (right, numbered) on one sheet."""
    set_theme("light"); NOTES.clear(); bb, bh = before_fn(); NOTES.clear(); ab, ah = after_fn(); notes = list(NOTES)
    k = 3.0; ox1 = 30; ox2 = int(30 + PW * k + 50); oy = 60; lw = 1100
    W_ = int(ox2 + PW * k + 40 + lw); H_ = int(max(bh, ah) * k + oy + 40)
    s = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/>'
    s += f'<text x="{ox1}" y="36" font-family="Helvetica" font-size="20" font-weight="700" fill="#222">Before</text><text x="{ox2}" y="36" font-family="Helvetica" font-size="20" font-weight="700" fill="#222">After</text>'
    s += f'<g transform="translate({ox1},{oy}) scale({k})">{defs()}{bb}</g><g transform="translate({ox2},{oy}) scale({k})">{ab}</g>'
    seen = {}; legend = ""; n = 0
    for (x, y, t) in notes:
        if t in seen: continue
        n += 1; seen[t] = n; cx, cy = ox2 + x * k, oy + y * k
        s += f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="11" fill="#E5483B" stroke="#fff" stroke-width="1.5"/><text x="{cx:.1f}" y="{cy+4.5:.1f}" font-family="Helvetica" font-size="13" font-weight="700" fill="#fff" text-anchor="middle">{n}</text>'
        legend += f'<text x="{ox2 + PW*k + 40:.0f}" y="{oy + 20 + (n-1)*34}" font-family="Helvetica" font-size="17" fill="#222"><tspan font-weight="700" fill="#E5483B">{n}  </tspan>{escape(t)}</text>'
    return svg(W_, H_, s + legend)

def flow_sheet():
    """Tidy up flow: step 1 -> 2 -> 3 happen automatically; a refused box stays on step 1 with a reason."""
    set_theme("light"); k = 2.0; W_ = int(3 * PW * k + 2 * 150 + 60); pans = [tab_hide(0, drawing=True), tab_hide(1, 1), tab_hide(2)]; refp = tab_hide(0, refused=True)
    H0 = max(p[1] for p in pans); H_ = int((H0 + refp[1] + 140) * k)
    s = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/>{defs()}'
    xs = [30 + i * (PW * k + 150) for i in range(3)]
    for i, (p, x) in enumerate(zip(pans, xs)): s += f'<g transform="translate({x},70) scale({k})">{p[0]}</g>'
    def arrow(x1, x2, y, label, col="#222"):
        return (f'<line x1="{x1}" y1="{y}" x2="{x2-8}" y2="{y}" stroke="{col}" stroke-width="2.4"/><path d="M{x2-14} {y-7} L{x2-2} {y} L{x2-14} {y+7}" fill="none" stroke="{col}" stroke-width="2.4"/>'
                f'<text x="{(x1+x2)/2}" y="{y-12}" font-family="Helvetica" font-size="15" font-weight="700" fill="{col}" text-anchor="middle">{label}</text>')
    ay = 70 + 150 * k / 2
    s += arrow(xs[0] + PW * k + 10, xs[1] - 10, ay, "auto") + arrow(xs[1] + PW * k + 10, xs[2] - 10, ay, "auto")
    s += f'<text x="{xs[0]+PW*k/2+20}" y="{ay+60}" font-family="Helvetica" font-size="13" fill="#222" text-anchor="middle"></text>'
    for x, t in zip(xs, ("Step 1: draw. Release over a valid spot:", "Step 2: pick a look. Choose one:", "Step 3: done. Add another goes back to step 1")): s += f'<text x="{x}" y="50" font-family="Helvetica" font-size="17" font-weight="700" fill="#222">{escape(t)}</text>'
    y2 = 70 + H0 * k + 60
    s += f'<g transform="translate({xs[0]},{y2}) scale({k})">{refp[0]}</g>'
    s += f'<text x="{xs[0]}" y="{y2-14}" font-family="Helvetica" font-size="17" font-weight="700" fill="#E5483B">Release over a window, door or outside my zone: stays on step 1</text>'
    s += f'<text x="{xs[1]}" y="{y2+60}" font-family="Helvetica" font-size="16" fill="#222">The box turns red and one line says why (red text).</text>'
    s += f'<text x="{xs[1]}" y="{y2+90}" font-family="Helvetica" font-size="16" fill="#222">Nothing is covered. Draw again; the panel advances only when the box is valid.</text>'
    s += f'<text x="{xs[1]}" y="{y2+130}" font-family="Helvetica" font-size="16" fill="#222">Selecting an existing cover in the room opens step 2 for that cover.</text>'
    return svg(W_, H_, s)

def hierarchy_sheet():
    set_theme("light"); k = 3.0; W_, H_ = 1500, 880
    def g(x, y, body): return f'<g transform="translate({x},{y}) scale({k})">{body}</g>'
    def lab(x, y, t, size=14, w="400"): return f'<text x="{x}" y="{y}" font-family="Helvetica" font-size="{size}" font-weight="{w}" fill="#222">{escape(t)}</text>'
    tile = lambda w, h: rrect(0, 0, w, h, 14, "#F3F5F8", 1)
    body = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/>{defs()}'
    cols = [30, 520, 1010]; ty = 100; th = 112
    heads = (("Level 1: tabs", "Where am I? One segmented control, always on top."), ("Level 2: sections and sub-navigation", "What am I choosing? Quiet text, no fill, no pill."), ("Level 3: options", "The choice itself: chips, swatches, cards."))
    for c, (h1, h2) in zip(cols, heads): body += lab(c, 40, h1, 18, "700") + lab(c, 64, h2, 13)
    body += g(cols[0], ty, tile(150, th) + segmented(8, 28, 134, SEG, ["Style", "Tidy up"], 0, size=FS_TAB))
    body += g(cols[1], ty, tile(150, th) + section(10, 8, "Floor") + text_toggle(10, 52, ["My zone", "Outside"], 0)[0] + text_toggle(10, 76, ["Seating", "Beds"], 1, gap=18)[0])
    body += g(cols[2], ty, tile(150, th) + chip(8, 8, 62, CHIP, "Day", "normal", size=FS_SMALL) + chip(76, 8, 68, CHIP, "Evening", "selected", size=FS_SMALL)
             + swatch(30, 50, "Oak", "floor-oak-planks", "selected") + swatch(75, 50, "Birch", "floor-birch-planks", "normal") + swatch(120, 50, "Night", "mood", "normal", mood=3))
    notes = (("Segmented control, 36 high, track 7% ink", "Selected: graphite pill, white 14 mm semibold", "Unselected: 14 mm regular, no fill", "Exactly one per panel", "Nothing else in the panel uses this look"),
             ("Section title: 13 mm semibold, 76% ink", "Text toggle: 13 mm, active semibold + 1.8 mm underline", "Inactive: 76% ink, no outline, no fill", "Smaller and quieter than the tabs", "Never filled, never a pill"),
             ("Chip: 36 high pill, 14 mm, hugs its content; selected = filled + thin ring", "Swatch: 34 mm disc (floor, wall and mood)", "Label 13 mm below the swatch", "Card: 80 x 92, thumbnail and name", "Selected is always a filled or ringed element"))
    for c, ns in zip(cols, notes):
        for i, t in enumerate(ns): body += lab(c, ty + th * k + 30 + i * 24, t, 13)
    fy = 660; k = 2.2
    body += lab(30, fy, "Feedback (never part of the layout)", 18, "700")
    t1 = toast("Oak floor \u00b7 My zone", "Undo", left=0, top=0); t2 = toast("Doesn\u2019t fit this box: 2.4 m wide", None, refused=True, left=0, top=0); h1_ = hint_chip("Pinch the floor to place", 0, left=0, top=0)
    body += g(30, fy + 30, t1) + g(500, fy + 30, t2) + g(1010, fy + 30, h1_)
    body += lab(30, fy + 110, "Toast: above the panel, about 2 s, ends with Undo", 13) + lab(500, fy + 110, "Refused: the same toast in red with the reason", 13) + lab(1010, fy + 110, "Hint chip: under the panel, only while an action is pending", 13)
    body += lab(30, fy + 170, "Spacing: 16 outer padding; 8 between items; 12 to 16 between sections.   Type: 14 / 13 / 12 mm, two weights (regular, semibold).   No footer.", 14)
    return svg(W_, 880, body)

def section(x, y, s, w=None):
    return text(x, y + 12, s, FS_SMALL, weight="600", op=TH["sec_op"], shadow=False)

# ---- spec sheets (flat background, measurements) ----
def dim(x1, y1, x2, y2, label_, k, side=0, anchor="middle"):
    col = "#E5483B"; s = f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{col}" stroke-width="1.2"/>'
    for (x, y) in ((x1, y1), (x2, y2)): s += f'<circle cx="{x}" cy="{y}" r="2" fill="{col}"/>'
    mx, my = (x1 + x2) / 2, (y1 + y2) / 2
    return s + f'<text x="{mx + (side if y1 == y2 else 6)}" y="{my + (-5 if y1 == y2 else 4) + (side if y1 == y2 else 0)}" font-family="Helvetica" font-size="11" font-weight="700" fill="{col}" text-anchor="{anchor if y1 == y2 else "start"}">{escape(label_)}</text>'

def spec_layout():
    set_theme("light"); k = 4.0; NOTES.clear()
    pan, ph = tab_style(); W_ = int((PW + 150) * k); ox, oy = 20, 20; ax, ay = ox * k, oy * k
    px = lambda x: ax + x * k; py = lambda y: ay + y * k
    y0 = BODY_Y; fy = y0 + 24 + 16; wy = fy + 22 + 52 + 12; my = wy + 22 + 52 + 12
    s = dim(px(0), py(-6), px(PAD), py(-6), "16", k, side=-4) + dim(px(PW - PAD), py(-6), px(PW), py(-6), "16 padding", k, side=-4, anchor="start")
    s += dim(px(PW + 8), py(0), px(PW + 8), py(TABS_Y), "48 top strip", k) + dim(px(PW + 8), py(TABS_Y), px(PW + 8), py(TABS_Y + SEG), "36 tabs", k) + dim(px(PW + 8), py(TABS_Y + SEG), px(PW + 8), py(BODY_Y), "12", k)
    s += dim(px(PW + 8), py(y0), px(PW + 8), py(fy), "24 toggle row + 16 gap", k)
    s += dim(px(PW + 8), py(fy), px(PW + 8), py(fy + 22), "16 title + 6", k) + dim(px(PW + 8), py(fy + 22), px(PW + 8), py(fy + 22 + 52), "52 row: 34 + 4 + 14", k) + dim(px(PW + 8), py(fy + 22 + 52), px(PW + 8), py(wy), "12 section gap", k)
    s += dim(px(PW + 8), py(my + 22 + 52), px(PW + 8), py(ph), "16 bottom padding", k)
    s += dim(px(PAD), py(fy + 22 + 58), px(PAD + COLW), py(fy + 22 + 58), "col 51.2", k, side=12)
    legend = [(f"Panel 296 wide; height follows the tab (Style {int(ph)}); corner radius 28", 0), ("Grid 4 mm; padding 16; gap 8; section gap 12", 1), ("Level 1 tabs: 36 high, 14 mm", 2), ("Level 2: 13 mm semibold titles, text toggles", 3),
              ("Level 3: round swatches 34 mm, label 13 below, 5 columns of 51.2", 4), ("Selected: ring 1.3 mm, 2.4 outside, filled chip", 5), ("No footer: feedback is a toast above, hints a chip below", 6)]
    for t, i in legend: s += f'<text x="{px(0)}" y="{py(ph) + 28 + i * 16}" font-family="Helvetica" font-size="12" fill="#222">{escape(t)}</text>'
    H2 = int(py(ph) + 28 + len(legend) * 16 + 10)
    return svg(W_, H2, f'<rect width="{W_}" height="{H2}" fill="#E4E7EC"/>{defs()}<g transform="translate({ox * k},{oy * k}) scale({k})">{pan}</g>{s}')

def spec_components():
    set_theme("light"); k = 4.0; W_, H_ = 1420, 1060
    def g(x, y, body): return f'<g transform="translate({x},{y}) scale({k})">{body}</g>'
    def lab(x, y, t, size=13, w="400"): return f'<text x="{x}" y="{y}" font-family="Helvetica" font-size="{size}" font-weight="{w}" fill="#222">{escape(t)}</text>'
    tile = lambda w, h: rrect(0, 0, w, h, 8, "#F3F5F8", 1)
    body = f'<rect width="{W_}" height="{H_}" fill="#E4E7EC"/>{defs()}'
    body += lab(30, 28, "Chip: 36 mm high pill, label 15 mm regular centred, side padding at least 24 (16 on 28 mm chips with 13 mm labels)", 14, "700")
    for i, st in enumerate(("normal", "hover", "pressed", "selected", "disabled", "refused")):
        xx = 30 + (i % 3) * 340; yy = 48 + (i // 3) * 232
        body += g(xx, yy, tile(84, 48) + chip(4, 6, 76, CHIP, "Birch", st)) + lab(xx, yy + 192 + 18, st.capitalize())
    body += dim(30 + 88 * k, 48 + 6 * k, 30 + 88 * k, 48 + 42 * k, "36", k)
    body += lab(30, 48 + 2 * 232 + 10, "Selected: graphite #2C2F36 fill (Light) or white fill (Dark), 1.3 mm accent ring. Hover: 17% ink tint (Dark 26% white) and a 1 mm lift. Normal 7% (Dark 14%). Pressed: 97%. Disabled: 40%.", 13)
    y3 = 48 + 2 * 232 + 40
    body += g(30, y3, tile(150, 48) + segmented(6, 6, 138, SEG, ["Style", "Furniture"], 0, size=FS_TAB)) + lab(30, y3 + 192 + 18, "Level 1 segmented: 36 high, 14 mm, only for the tabs")
    body += g(690, y3, tile(72, 60) + swatch(36, 6, "Oak", "floor-oak-planks", "selected")) + lab(690, y3 + 240 + 18, "Round swatch 34, label 13 below")
    body += g(1000, y3, tile(92, 108) + chip(6, 6, 80, 92, None, "selected", r=14, content=lambda c: thumb(12, 12, 68, 46, "armchair-cafe-01") + text(46, 70, "Light", 12, c, "middle", shadow=False) + text(46, 85, "armchair", 12, c, "middle", shadow=False))) + lab(1000, y3 + 432 + 18, "Card 80 x 92, radius 14, thumbnail 68 x 46, name 12 mm, 2 lines max")
    return svg(W_, H_, body)

# ---- contrast ----
def rgb(h): return np.array([int(h[i:i+2], 16) for i in (1, 3, 5)], float)
def lum(c):
    c = np.array(c, float) / 255; c = np.where(c <= 0.03928, c / 12.92, ((c + 0.055) / 1.055) ** 2.4); return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2]
def ratio(a, b): la, lb = sorted((lum(a), lum(b)), reverse=True); return (la + 0.05) / (lb + 0.05)
def under_panel(night):
    im = np.asarray(Image.open(os.path.join(HERE, "room-dark.jpg" if night else "room-bright.jpg")).convert("RGB"), float)
    return im[480:900, 560:1040].reshape(-1, 3)
def contrast_report():
    out = {}; wall = np.array([rgb("#E6E0D4")]); black = np.array([rgb("#101216")])
    for theme in ("light", "dark"):
        t = THEMES[theme]
        for room, px in (("bright room", under_panel(False)), ("dark room", under_panel(True)), ("white wall", wall), ("black wall", black)):
            res = []
            for p in px[::37]:
                panel = (p * (1 - t["scrim"]) + rgb(t["tint"]) * t["scrim"]) * (1 - 0.10) + 255 * 0.10   # tint, then the sheen (about 10% white on average)
                fg = rgb(t["fg"]); sec = fg * t["sec_op"] + panel * (1 - t["sec_op"])
                chip_bg = panel * (1 - t["chip_op"]) + rgb(t["base"]) * t["chip_op"]
                res.append((ratio(fg, panel), ratio(sec, panel), ratio(fg, chip_bg), ratio(rgb(t["sel_text"]), rgb(t["sel_fill"]))))
            a = np.array(res)
            out[f"{theme} glass, {room}"] = dict(primary_worst=round(float(a[:, 0].min()), 1), secondary_worst=round(float(a[:, 1].min()), 1), chip_text_worst=round(float(a[:, 2].min()), 1), selected_chip=round(float(a[:, 3].min()), 1))
    return out

def write(p, c):
    with open(os.path.join(HERE, p), "w") as f: f.write(c)


def write(p, c):
    with open(os.path.join(HERE, p), "w") as f: f.write(c)

COMBOS = (("light", "day"), ("light", "night"), ("dark", "day"), ("dark", "night"))
def main():
    icd = os.path.join(HERE, "icons"); shutil.rmtree(icd, ignore_errors=True); os.makedirs(icd)
    for nm, inner in I.ICONS.items(): write(os.path.join("icons", nm + ".svg"), f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48" width="48" height="48" color="#0E1520" {I.S}>{inner}</svg>\n')
    shutil.copyfile(os.path.join(CONTENT, "..", "Art", "UI", "FurnitureThumbs.png"), os.path.join(HERE, "thumbs-atlas.png"))
    k = 2.6; files = {}
    for theme, room in COMBOS:
        set_theme(theme); night = room == "night"; sfx = f"-{theme}-{room}"
        main_combo = (theme, room) in (("light", "day"), ("dark", "night"))
        def sc(fn, toast_=None, hint=None, refused=False):
            p, ph = fn(); return scene(p, ph, night, k, 900, top=toast(*toast_, refused=refused) if toast_ else "", bottom=hint_chip(hint, ph) if hint else "")
        files[f"mockup-style{sfx}"] = sc(lambda: tab_style(hover=("Floor", 2)))
        files[f"mockup-furniture{sfx}"] = sc(lambda: tab_furniture(0, 1, 2), hint="Pinch the floor to place")
        files[f"mockup-ai-designer{sfx}"] = sc(lambda: tab_ask(active=0, result=True))
        files[f"mockup-tidy-up{sfx}"] = sc(lambda: tab_hide(1, 1))
        if main_combo:
            files[f"mockup-style-changed{sfx}"] = sc(lambda: tab_style(hover=None), toast_=("Oak floor \u00b7 My zone", "Undo"))
            files[f"mockup-ai-designer-empty{sfx}"] = sc(lambda: tab_ask())
            files[f"mockup-ai-designer-typing{sfx}"] = sc(lambda: tab_ask(field="typing", value="A cozy reading corner"), hint="Type, or dictate with the mic key")
            files[f"mockup-ai-designer-busy{sfx}"] = sc(lambda: tab_ask(busy=True, value="A cozy reading corner", quote="A cozy reading corner"))
            files[f"mockup-ai-designer-done{sfx}"] = sc(lambda: tab_ask(result=True, value="A cozy reading corner", quote="A cozy reading corner"), toast_=("Placed 7 pieces", "Undo"))
            files[f"mockup-tidy-up-step1{sfx}"] = sc(lambda: tab_hide(0), hint="Pinch and drag on the floor over the clutter")
            files[f"mockup-tidy-up-step1-drawing{sfx}"] = sc(lambda: tab_hide(0, drawing=True), hint="Drawing: release to set the box")
            files[f"mockup-tidy-up-step3{sfx}"] = sc(lambda: tab_hide(2), toast_=("Basket placed", "Undo"))
            files[f"mockup-tidy-up-existing{sfx}"] = sc(lambda: tab_hide(1, 1, existing=True))
            files[f"mockup-furniture-refused{sfx}"] = sc(lambda: tab_furniture(0, 1, None, refused=3), toast_=("Doesn\u2019t fit this box: 2.4 m wide", None), refused=True)
            files[f"mockup-settings-tooltip{sfx}"] = sc(lambda: (lambda r: (r[0].replace(frame(r[1], 0), frame(r[1], 0, gear="hover"), 1) + settings_sheet(r[1], open_=False), r[1]))(tab_style()))
            files[f"mockup-settings-open{sfx}"] = sc(lambda: (lambda r: (r[0].replace(frame(r[1], 0), frame(r[1], 0, gear="selected"), 1) + settings_sheet(r[1]), r[1]))(tab_style()))
            files[f"mockup-states{sfx}"] = states_sheet(night); files[f"mockup-pill{sfx}"] = pill_sheet(night); files[f"mockup-room{sfx}"] = composite(night)
    set_theme("light")
    files["spec-tidy-up-flow"] = flow_sheet(); files["spec-hierarchy"] = hierarchy_sheet(); files["spec-real-options"] = real_options_sheet(); files["spec-layout-style"] = spec_layout(); files["spec-components"] = spec_components()
    for nm, fn in (("style", tab_style), ("furniture", tab_furniture), ("ai-designer", lambda: tab_ask(active=0, result=True)), ("ai-designer-voice", lambda: tab_ask(field="typing", value="A cozy reading corner")), ("tidy-up", lambda: tab_hide(1, 1)), ("tidy-up-step1", lambda: tab_hide(0)), ("tidy-up-step3", lambda: tab_hide(2))): files[f"spec-annotated-{nm}"] = annotated(fn, nm)
    files["spec-annotated-tidy-up"] = annotated_pair(lambda: tab_hide_v6("ready", 1), lambda: tab_hide(1, 1))
    set_theme("light")
    for n, v in files.items(): write(n + ".svg", v)
    if shutil.which("rsvg-convert"):
        for n in files: subprocess.run(["rsvg-convert", "-o", os.path.join(HERE, n + ".png"), os.path.join(HERE, n + ".svg")], check=True)
    else: print("rsvg-convert not found: SVG only", file=sys.stderr)
    rep = contrast_report(); write("contrast.json", json.dumps(rep, indent=1) + "\n"); print(json.dumps(rep, indent=1))
    return 0

if __name__ == "__main__":
    sys.exit(main())

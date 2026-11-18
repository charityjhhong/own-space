#!/usr/bin/env python3
"""Writes the self-contained HTML mockups and a PNG of each next to this file.
Usage: python3 make_mockups.py [all|settings|zone|style|myroom|add|ai|keyboard|tidy|rules] [--no-png]
PNG rendering needs Google Chrome (headless) and Pillow; HTML needs only the standard library (the Pick a look
and furniture cards embed atlas cells with Pillow)."""
import base64, io, json, os, subprocess, sys, tempfile
from mockkit import *

ROOT = os.path.join(HERE, "..", "..", "..", "app", "Assets", "_Project")
NO_PNG = "--no-png" in sys.argv


def write(name, content):
    with open(os.path.join(HERE, name + ".html"), "w", encoding="utf-8") as f:
        f.write(content)
    print("wrote", name + ".html", len(content) // 1024, "KB")
    if not NO_PNG:
        png(name)


def png(name):
    chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
    if not os.path.exists(chrome):
        print("  (no Chrome: PNG skipped)"); return
    from PIL import Image
    tmp = os.path.join(tempfile.gettempdir(), f"mock_{name}.png")
    subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--window-size=1440,9000", f"--screenshot={tmp}",
                    "file://" + os.path.join(HERE, name + ".html")], capture_output=True)
    im = Image.open(tmp).convert("RGB")
    bg = im.getpixel((2, 2))
    px = im.load(); w, h = im.size
    last = 0
    for y in range(h - 1, 0, -1):
        if any(px[x, y] != bg for x in range(0, w, 7)):
            last = y; break
    im.crop((0, 0, w, min(h, last + 40))).save(os.path.join(HERE, name + ".png"), optimize=True)
    os.remove(tmp)


# ------------------------------------------------------------------ data
def atlas_cells(ids):
    from PIL import Image
    atlas = Image.open(os.path.join(ROOT, "Art", "UI", "FurnitureThumbs.png")).convert("RGBA")
    cells = {c["id"]: c for c in json.load(open(os.path.join(ROOT, "Content", "thumbnails.json")))["items"]}
    out = {}
    for cid in ids:
        c = cells[cid]; buf = io.BytesIO()
        atlas.crop((c["x"], c["y"], c["x"] + 192, c["y"] + 128)).save(buf, "PNG", optimize=True)
        out[cid] = "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()
    return out


def finishes(surface):
    f = json.load(open(os.path.join(ROOT, "Content", "finishes.json")))["finishes"]
    return [(x["shortName"], x["baseColor"]) for x in f if x["surface"] == surface]


def moods():
    p = json.load(open(os.path.join(ROOT, "Content", "mood-presets.json")))["presets"]
    return [(x["shortName"], x["gradientTop"], x["gradientBottom"]) for x in p]


def card(name, img, on=False, w=84, h=92):
    return f'<div class="card{" on" if on else ""}" style="width:{m(w)};height:{m(h)}"><img src="{img}" alt=""><div class="s">{e(name)}</div></div>'


def grid_cards(cards, cols=3):
    return f'<div style="display:grid;grid-template-columns:repeat({cols},{m(84)});gap:{m(8)};justify-content:space-between">{"".join(cards)}</div>'


def safety():
    return (f'<div class="s" style="display:flex;gap:{m(6)};margin-top:{m(12)}"><span style="margin-top:{m(.5)}">{icon("info", 14)}</span>'
            f'<span>Hidden things are still there. Mind your step when you stand up.</span></div>')


SAFETY_H = 12 + 30


def states_stage(states, body_fn, tab, height_fn=None, gear=False):
    """One variant (a single panel) per state and theme: light then dark side by side."""
    out = ""
    for label, key in states:
        for t in ("light", "dark"):
            body, h = body_fn(key, t == "dark")
            out += variant(t, f"{t.capitalize()} · {label}", panel(tab, body, h, gear_lit=gear))
    return out


NUMS = ["Item", "Current", "Proposed", "Why"]
K = [None, "cur", "new", None]


# ------------------------------------------------------------------ 1 Settings
CTRL = 80


def row(title, caption, control, h=40):
    return (f'<div style="height:{m(h)};display:flex;align-items:center;gap:{m(8)}"><div style="flex:1;min-width:0"><div class="b">{e(title)}</div>'
            + (f'<div class="s s2">{e(caption)}</div>' if caption else "") + f'</div><div style="width:{m(CTRL)};flex:none;display:flex;justify-content:flex-end">{control}</div></div>')


def make_settings():
    btn = lambda t, c="": chip(t, c, CTRL)
    seg = lambda dark: (f'<div style="display:flex;width:{m(CTRL)}"><div class="seg b{"" if dark else " on"}" style="width:{m(CTRL//2)}">Light</div>'
                        f'<div class="seg b{" on" if dark else ""}" style="width:{m(CTRL//2)}">Dark</div></div>')

    def body(key, dark):
        dev = key == "dev"
        parts = [f'<div class="h" style="height:{m(24)};display:flex;align-items:center">Settings</div>', section("Room"),
                 row("Current space", "This room", btn("Change")), row("My zone", "The part that stays real", btn("Edit")),
                 row("Room scan", "Scan this room again", btn("Rescan")), section("Appearance", 12), row("Theme", "", seg(dark)),
                 section("Design", 12), row("Reset design", "Remove all changes in this room", btn("Reset", "danger"))]
        if dev: parts.append(row("Real occlusion", "Running", btn("On", "sel")))
        h = 84 + 24 + 20 + 120 + 12 + 20 + 40 + 12 + 20 + 40 + (40 if dev else 0) + 16
        return "".join(parts), h

    stage = states_stage([("release build, 408 mm", "rel"), ("dev build, 448 mm", "dev")], body, -1, gear=True)
    rows = [
        ("Gear in the strip", "!drawn as a sun-like glyph in round 1", "the app's real gear (docs/design/ui/icons/settings.svg), lit while the sheet is open", "The mockup now uses the app's own glyphs: undo, redo, gear, minimize"),
        ("Header", "13 mm, row 16", "14 mm semibold, row 24", "Same style as a tab"),
        ("Section name", "!10 mm all caps (SPACE, APPEARANCE, DESIGN, DEVELOPER)", "12 mm semibold, 76% ink, sentence case, row 20", "Never below 12 mm"),
        ("Row", "!2 title lines of 16 next to a 36 chip; captions auto-shrink to 10 mm", "40 mm: title 14 mm + caption 12 mm on one line; no divider lines", "One-line captions (see wording), so no row wraps"),
        ("Trailing control", "!own width per kind (text + 24)", f"{CTRL} mm ({CTRL/PANEL_W*100:.0f}% of width), right edge on the content edge, 36 high", "Change, Edit, Rescan, Reset, Light|Dark and On share both edges"),
        ("Light | Dark", "two toggles in a row", f"two text options of {CTRL//2} x 36; the chosen one is filled; no pill behind both", "Only the chosen option has a box"),
        ("Group gap", "!22 (16 title + 6)", "12 above each section name (20 high)", "4 mm grid"),
        ("Panel height", "!about 460", "release 408, dev build 448", "Both fit the 460 maximum. Trade-off: Real occlusion is hidden in release builds; in dev builds it sits under Design with no Developer heading"),
    ]
    write("settings", page("Settings list", "The Settings sheet, redrawn with the app's real glyphs and one type set. The top strip and tab row are the shared shell; the gear is lit and the header has no ×.",
          ["Gear is the app's own glyph, and there are no divider lines or pill behind Light | Dark.",
           "One width and one edge for every button on the right; all captions fit on one line (wording changes are on the rules page).",
           "Fits the 460 mm panel: 408 mm in release builds, 448 mm in dev builds with Real occlusion under Design."],
          ["Wording is the proposed wording; the table of every change, current → proposed, is on the rules page (the human approves it separately)."],
          stage, table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 2 Zone card
def zone_card(l1, l2, buttons):
    w, pad, h = 300, 16, 156
    n = len(buttons)
    bs = "".join(f'<div class="chip {k}" style="min-width:{m(96)};padding:0 {m(12)};{"flex:1" if n == 2 else "width:"+m(120)}">{e(t)}</div>' for t, k in buttons)
    return (f'<div class="panel" style="width:{m(w)};height:{m(h)}"><div class="handle" style="top:{m(23)}"></div>'
            f'<div class="u-btn" style="left:{m(pad)};top:{m(8)};width:{m(36)};height:{m(36)}">{icon("chevron-left", 18)}</div>'
            f'<div class="h" style="position:absolute;left:{m(pad)};right:{m(pad)};top:{m(48)};height:{m(22)};line-height:{m(22)}">{e(l1)}</div>'
            f'<div class="s" style="position:absolute;left:{m(pad)};right:{m(pad)};top:{m(74)};height:{m(18)};line-height:{m(18)}">{e(l2)}</div>'
            f'<div style="position:absolute;left:{m(pad)};right:{m(pad)};bottom:{m(pad)};display:flex;gap:{m(8)};justify-content:flex-end">{bs}</div></div>')


def make_zone():
    N = ("Drag the corners to set your zone.", "Inside stays real. Outside becomes virtual.")
    D = ("Your desk is outside your zone.", "Fit the zone to it, or keep it?")
    cols = ""
    for t in ("light", "dark"):
        c = (f'<div class="cap">Normal</div>{zone_card(*N, [("Done", "sel")])}<div class="cap">Desk is outside</div>{zone_card(*D, [("Keep anyway", ""), ("Fit to desk", "sel")])}'
             f'<div class="cap">No scan, sample room offered</div>{zone_card(*N, [("Use sample room", ""), ("Done", "sel")])}')
        cols += variant(t, t.capitalize(), c)
    rows = [
        ("Back button", "none", "36 x 36 round, top left, the app's chevron-left; normal state: leave without changing the zone; question state: back to editing", "!Needs xr/core behaviour; it is a control, so it keeps its fill"),
        ("Padding", "12", "16", "Same as the tool panel"),
        ("Line 1", "13 mm regular, 20 line", "14 mm semibold, 22 line", "Title style"),
        ("Line 2", "!11 mm, 17 line", "12 mm regular, 18 line, primary ink", "Never below 12 mm"),
        ("Gap, line 2 to buttons", "6", "12", "Separates the question from the answers"),
        ("Buttons", "!one: 100 wide; two: half each, primary on the LEFT", "36 high, hug + 24, minimum 96; the main one is always the right-most", "Done and Fit to desk sit where the thumb expects the main action"),
        ("Height", "127", "156", "+29 for the Back row"),
    ]
    write("zone-card", page("Zone card", "The card shown while the zone is edited: normal, the question when the desk is outside, and when there is no scan.",
          ["A round Back button top-left leaves the zone step without choosing.",
           "The main button (Done, Fit to desk) is always on the right.",
           "More padding (16 mm) and the second line grows from 11 to 12 mm."],
          ["No wording changes on this screen."], '<div class="stage" style="margin:0">' + cols + "</div>", table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 3 Style
def make_style():
    fl, wl, md = finishes("floor"), finishes("wall"), moods()
    eye = f'<div class="disc" style="width:{m(34)};height:{m(34)};box-shadow:0 0 0 {m(.8)} var(--fg2) inset;display:flex;align-items:center;justify-content:center;color:var(--fg2)">{icon("eye", 18)}</div>'

    def disc(style, on, label, extra=""):
        d = f'<div class="disc{" on" if on else ""}" style="width:{m(34)};height:{m(34)};{style}"></div>' if style else eye.replace('class="disc"', f'class="disc{" on" if on else ""}"')
        return f'<div style="width:{m(52.8)};display:flex;flex-direction:column;align-items:center;gap:{m(4)}">{d}<div class="s">{e(label)}</div></div>'

    def band(title, items, sel):
        cells = [disc(None, sel == 0, "Real")] + [disc(s, sel == i + 1, n) for i, (n, s) in enumerate(items)]
        return section(title) + f'<div style="display:flex;margin-top:{m(6)};justify-content:space-between">{"".join(cells)}</div>'

    def body(key, dark):
        floors = [(n, f"background:{c}") for n, c in fl]
        walls = [(n, f"background:{c};box-shadow:0 0 0 {m(.4)} var(--rim)") for n, c in wl]
        moodd = [(n, f"background:linear-gradient(160deg,{a},{b})") for n, a, b in md]
        apply = (f'<div style="display:flex;gap:{m(12)};align-items:center;height:{m(24)}"><span class="b">Apply to</span>'
                 f'<span class="opt b on">My zone</span><span class="opt b">Outside</span></div>')
        h = 84 + 24 + 12 + 3 * (20 + 6 + 34 + 4 + 15) + 2 * 12 + 16
        return (apply + f'<div style="height:{m(12)}"></div>' + band("Floor", floors, 1) + f'<div style="height:{m(12)}"></div>' + band("Wall", walls, 0)
                + f'<div style="height:{m(12)}"></div>' + band("Mood", moodd, 2)), h

    stage = states_stage([("My zone", "a")], body, 0)
    rows = [
        ("Apply to", "!13 mm label + two text toggles on a 24 mm row", "14 mm label, two text options (chosen = semibold with a 1.8 mm underline), 24 mm row", "Text options only; no pills"),
        ("Section names", "!Floor / Wall / Mood at 13 mm (the code also has 'Floor · your real room' variants)", "Floor, Wall, Mood at 12 mm semibold, row 20", "Which room it applies to is already set by Apply to"),
        ("Discs", "34 mm disc, label 13 mm", "34 mm disc, label 12 mm, 5 columns of 52.8", "Label never below 12"),
        ("Chosen disc", "accent ring", "accent ring 1.4 mm, 2 mm clear gap", "unchanged"),
        ("Real", "transparent disc, thin ring, eye glyph", "same, the app's real eye glyph", "unchanged"),
        ("Gaps", "!row gap 12, title gap 6", "12 between bands, 6 title to discs, 4 disc to label", "4 mm grid"),
        ("Panel height", "398", "397", "unchanged"),
    ]
    write("style", page("Style tab", "Floor, wall and mood for my zone. Disc colours come from the finish and mood data (Content/finishes.json, mood-presets.json).",
          ["Section names are 12 mm semibold, never all caps; labels under the discs are 12 mm.",
           "The tab row has no pill behind it; tabs are bold text and only the chosen tab is filled.",
           "Apply to is plain text options, not pills."],
          ["Wording changes are on the rules page."], stage, table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 4 My room, 5 Add
def cats(active):
    items = ["Seating", "Beds", "Tables", "Storage", "Lights", "Decor", "Window"]
    inner = "".join(f'<span class="opt b{" on" if x == active else ""}">{e(x)}</span>' for x in items)
    return (f'<div style="display:flex;gap:{m(12)};height:{m(24)};overflow:hidden;white-space:nowrap;'
            f'-webkit-mask-image:linear-gradient(90deg,#000 88%,transparent);mask-image:linear-gradient(90deg,#000 88%,transparent)">{inner}</div>')


def make_myroom():
    ids = ["desk-warm-wood-01", "desk-scandi-01", "desk-night-city-01"]
    names = ["Oak study desk", "Birch compact desk", "Dark laminate desk"]
    th = atlas_cells(ids)

    def body(key, dark):
        cs = [card(n, th[i], on=(j == (1 if dark else 0))) for j, (i, n) in enumerate(zip(ids, names))]
        h = 84 + 24 + 12 + 20 + 6 + 92 + 12 + 36 + 16
        return (cats("Tables") + f'<div style="height:{m(12)}"></div>' + section("Designs that fit this desk") + f'<div style="height:{m(6)}"></div>'
                + grid_cards(cs) + f'<div style="display:flex;gap:{m(8)};margin-top:{m(12)}">{chip("Keep real")}{chip("Adjust fit")}</div>'), h

    rows = [
        ("Categories", "!13 mm text toggles, one scrolling row", "14 mm text options, chosen = semibold + 1.8 mm underline; the row fades at the right edge to show it scrolls", "No pills"),
        ("Cards", "80 x 92, name 11 mm (the code: 11, shrinking to 10)", "84 x 92, three columns across 268; name 12 mm, at most 2 lines", "Card name minimum is 12"),
        ("Chosen card", "graphite fill + ring", "unchanged", "Same as every chosen control"),
        ("Chips", "!Keep the real one, Adjust fit at 13 mm", "Keep real, Adjust fit at 14 mm, 36 high, hug + 24, min 72", "Wording on the rules page"),
        ("Gaps", "8 between cards", "8 between cards, 12 above the chips, 6 title to cards", "4 mm grid"),
        ("Panel height", "362", "302", "Only three desks fit, so one row"),
    ]
    write("my-room", page("My room tab", "Restyle the real piece in my zone: the designs that fit the selected desk.",
          ["Cards and text follow the shared sizes; card names are 12 mm, never 11 or 10.",
           "Category names are plain text options with an underline for the chosen one.",
           "Chip labels are shortened to a verb and one noun (see wording)."],
          ["Wording changes are on the rules page."], states_stage([("desk selected", "a")], body, 1), table("Numbers for xr (current → proposed)", NUMS, rows, K)))


def make_add():
    ids = ["sofa-warm-wood-01", "sofa-cafe-01", "sofa-scandi-01", "sofa-night-city-01", "armchair-warm-wood-01", "armchair-cafe-01"]
    names = ["Linen two-seat sofa", "Leather lounge sofa", "Gray sofa", "Slate lounge sofa", "Linen armchair", "Leather armchair"]
    th = atlas_cells(ids)

    def body(key, dark):
        cs = [card(n, th[i], on=(j == 2)) for j, (i, n) in enumerate(zip(ids, names))]
        pager = (f'<div style="display:flex;justify-content:flex-end;align-items:center;gap:{m(4)};height:{m(36)};margin-top:{m(8)}">'
                 f'<span style="width:{m(36)};height:{m(36)};display:flex;align-items:center;justify-content:center;color:var(--fg2)">{icon("chevron-left", 16)}</span>'
                 f'<span class="s">1 / 3</span><span style="width:{m(36)};height:{m(36)};display:flex;align-items:center;justify-content:center">{icon("chevron-right", 16)}</span></div>')
        return cats("Seating") + f'<div style="height:{m(12)}"></div>' + grid_cards(cs) + pager, 84 + 24 + 12 + 192 + 8 + 36 + 16

    rows = [
        ("Cards, categories", "as My room", "as My room (84 x 92, 12 mm names, text options)", "Same page layout for both tabs"),
        ("Pager", "!two chevrons and 1 / 3 at the bottom right", "36 x 36 hit areas with the app's chevrons, no fill; 12 mm count", "A control with no box: the chevron is the control"),
        ("Chosen card", "graphite fill + ring", "unchanged (it stays chosen while a placement is armed)", "Same as every chosen control"),
        ("Panel height", "362", "372", "+10 because the pager keeps its 36 mm hit area"),
    ]
    write("add", page("Add tab", "Place a virtual piece in the outside zone: pick a card, then pinch the floor outside the zone.",
          ["Same cards, text sizes and category options as My room.",
           "The pager is two plain chevrons and a count, with no frames.",
           "Everything sits on the shared spacing scale."],
          ["No wording changes on this screen."], states_stage([("seating", "a")], body, 2), table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 6 AI (no keyboard), 7 keyboard
def ai_field(text, focus=False, placeholder=False):
    return (f'<div class="chip field{" focus" if focus else ""}" style="width:{m(188)}"><span class="b{" s2" if placeholder else ""}">{e(text)}</span>'
            + (f'<span style="display:inline-block;width:{m(1)};height:{m(20)};background:var(--accent);margin-left:{m(2)}"></span>' if focus else "") + "</div>")


def presets():
    ps = "".join(chip(t, "", 130) for t in ["Living room", "Study café", "Bedroom", "Bright studio"])
    return section("Styles", 12) + f'<div style="display:grid;grid-template-columns:{m(130)} {m(130)};gap:{m(8)};margin-top:{m(6)}">{ps}</div>'


def make_ai():
    def body(key, dark):
        top = f'<div style="display:flex;gap:{m(8)}">{ai_field("Describe the room you want", placeholder=True)}{chip("Create", "dis", 72)}</div>'
        if key == "empty":
            return top + presets() + f'<div class="s s2" style="margin-top:{m(12)}">The AI furnishes the outside zone.</div>', 84 + 36 + 12 + 20 + 6 + 80 + 12 + 15 + 16
        plan = (section("What the AI did", 12) + f'<div class="s s2" style="margin-top:{m(2)}">“a cozy living room”</div>'
                f'<div class="b" style="margin-top:{m(4)}">A sofa, two shelves and a lamp fill the empty side.</div>'
                f'<div class="s s2" style="margin-top:{m(4)}">7 placed, 1 skipped</div><div style="margin-top:{m(8)}">{chip("Undo all")}</div>')
        return top + presets() + plan, 84 + 36 + 12 + 20 + 6 + 80 + 12 + 20 + 2 + 15 + 4 + 36 + 4 + 15 + 8 + 36 + 16

    rows = [
        ("Field", "36 high, width inner - Create - 8", "188 x 36 + Create 72 x 36 (Create is dimmed until text is typed)", "unchanged sizes"),
        ("Style chips", "!hug their text, ragged", "2 x 2 grid, 130 x 36, gap 8", "Aligned columns; 130 = (268 - 8) / 2"),
        ("Plan", "!a card with an accent hairline frame", "no frame: a small title, the quoted request (12 mm), the explanation (14 mm, 2 lines), the count and Undo all", "The block is separated by space, not a box"),
        ("Section name, hint", "13 mm", "12 mm semibold; hint 12 mm", "Shared type set"),
        ("Panel height", "about 280 / 430", "281 before the AI has run, 400 with a plan", "Within 460"),
    ]
    write("ai-tab", page("AI tab", "The AI Designer before it has run and after a plan. The keyboard is on its own page.",
          ["The style chips become an aligned 2 x 2 grid.",
           "The AI's plan is plain text under a section name, not a framed card.",
           "Section names, hints and quotes use the shared 12 mm styles."],
          ["Wording changes are on the rules page (no ellipsis; Styles; the AI furnishes the outside zone.)."],
          states_stage([("before a run", "empty"), ("after a plan", "done")], body, 3), table("Numbers for xr (current → proposed)", NUMS, rows, K)))


ROWS = ["1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm"]


def keyboard():
    w, pad, py, kh, gap = 300, 8, 44, 40, 4
    px = (w - 2 * pad) / 10
    ks = []
    for r, rw in enumerate(ROWS):
        off = 0.5 if r == 2 else 1.5 if r == 3 else 0
        ks += [(c, off + i, 1, r) for i, c in enumerate(rw)]
    ks += [("Shift", 0, 1.5, 3), ("Del", 8.5, 1.5, 3), ("Cancel", 0, 2, 4), (",", 2, 1, 4), ("space", 3, 4, 4), (".", 7, 1, 4), ("Create", 8, 2, 4)]
    parts = "".join(f'<div class="chip{" sel" if l == "Create" else ""}" style="position:absolute;left:{m(pad+s*px)};top:{m(pad+r*py)};width:{m(sp*px-gap)};height:{m(kh)};border-radius:{m(12)};padding:0">{e(l)}</div>' for l, s, sp, r in ks)
    h = pad + 4 * py + kh + pad
    return f'<div class="panel" style="width:{m(w)};height:{m(h)};border-radius:{m(20)}">{parts}</div>', h


def make_keyboard():
    cols = ""
    for t in ("light", "dark"):
        kb, kh = keyboard()
        top = f'<div style="display:flex;gap:{m(8)}">{ai_field("a cozy living room", focus=True)}{chip("Create", "sel", 72)}</div>'
        p = panel(3, top + presets() + f'<div class="s s2" style="margin-top:{m(12)}">The AI furnishes the outside zone.</div>', 281)
        cols += variant(t, f"{t.capitalize()} · typing", p + f'<div style="height:{m(8)}"></div>' + kb)
    rows = [
        ("Width", "400 (Pad 12 + 10 x 38 pitch)", "300 = panel width", "!Trade-off: keys shrink from 34 to 24.4 wide. Compromise: 352 wide (key 29)"),
        ("Keys", "34 x 34, pitch 38, round", "24.4 x 40, pitch 28.4 x 44, radius 12", "Hit area stays within 15% of today"),
        ("Own text field and grab bar", "28 + 20 mm", "removed", "The panel's field shows the text; the panel's handle moves both"),
        ("Position", "!own piece, 0.43 m from the eyes, tilted 30 degrees", "docked directly under the panel, 8 mm gap, same left edge", "!xr decides the pose"),
        ("Height", "262", "228", ""),
        ("Key text", "13 mm", "14 mm regular", "Shared body style; Create is the filled key"),
    ]
    write("ai-keyboard", page("AI tab with the keyboard", "The typing state: the field has the accent ring and caret, and the keyboard sits right under the panel at the same width.",
          ["The keyboard is as wide as the panel and attached under it.",
           "It no longer repeats the typed text or carries its own handle.",
           "Keys are 14 mm text, 24 x 40 mm, with a 12 mm corner."],
          ["No wording changes on the keyboard."], '<div class="stage" style="margin:0">' + cols + "</div>", table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 8 Tidy up
LOOKS = [("Plain", "cover-plain"), ("Basket", "clutter-basket-warm-wood-01"), ("Plant", "clutter-plant-warm-wood-01"),
         ("Cushion", "clutter-floor-cushion-warm-wood-01"), ("Box", "clutter-box-warm-wood-01"), ("Screen", "clutter-screen-warm-wood-01")]


def make_tidy():
    th = atlas_cells([c for _, c in LOOKS])
    S = safety()

    def body(key, dark):
        if key == "s1":
            b = (stepper(0) + f'<div class="b" style="margin-top:{m(12)}">Pinch and drag on the floor over the clutter</div>'
                 f'<div style="margin-top:{m(12)}">{chip("Draw a box", "sel", 120)}</div>' + S)
            return b, 84 + 28 + 12 + 36 + 12 + 36 + SAFETY_H + 16
        if key in ("s2", "ex"):
            cs = [card(n, th[c], on=(key == "ex" and n == "Basket")) for n, c in LOOKS]
            b = stepper(1) + section("Pick a look", 12) + f'<div style="height:{m(6)}"></div>' + grid_cards(cs)
            h = 84 + 28 + 12 + 20 + 6 + 192
            if key == "ex":
                b += f'<div style="display:flex;justify-content:center;margin-top:{m(12)}">{chip("Remove cover", "danger", 140)}</div>'; h += 12 + 36
            return b + S, h + SAFETY_H + 16
        b = (stepper(2) + f'<div class="h" style="margin-top:{m(12)}">Covered with a basket.</div>'
             f'<div style="display:flex;gap:{m(8)};margin-top:{m(12)}">{chip("Add another", "sel")}{chip("Remove", "danger")}</div>' + S)
        return b, 84 + 28 + 12 + 18 + 12 + 36 + SAFETY_H + 16

    stage = states_stage([("step 1, draw a box", "s1"), ("step 2, pick a look", "s2"), ("step 3, done", "s3"), ("existing cover selected", "ex")], body, 4)
    rows = [
        ("Stepper", "!number discs with outlines for each step", "no discs or outlines: current = bold number and name, done = the app's check glyph, future = 40%", "Not a control, so no box"),
        ("Look options", "!six 34 mm colour dots, label below", "six cards 84 x 92 (radius 14), 3 x 2, gap 8, 72 x 48 thumbnail from the #264 atlas", "Same card as My room and Add"),
        ("Label", "13 mm", "12 mm regular (chosen: semibold)", "Card name style"),
        ("Chosen card", "accent ring on the dot", "graphite fill (Dark: white) + ring", "Same as every chosen control"),
        ("Hit area", "!34 + 4", "84 x 92 + 4", "Much bigger targets"),
        ("Step 1 / 3 buttons", "13 mm chips", "14 mm, Draw a box 120 x 36; Add another and Remove 36 high", "Primary is the filled one"),
        ("Safety line", "12 mm with info glyph", "same, the app's info glyph (14)", "unchanged"),
        ("Panel height", "246 to 282", "step 1 about 250, step 2 about 410, step 3 about 250, existing cover about 458", "Within 460"),
    ]
    write("tidy-up", page("Tidy up tab, every step", "The four views of Tidy up: draw a box, pick a look (thumbnail cards from #264), done, and an existing cover selected.",
          ["Picture cards replace the colour dots, with the same thumbnails as the furniture cards.",
           "The stepper has no number discs or outlines; only text and a check.",
           "Every look is a big touch target (84 x 92 mm) and the chosen one is filled."],
          ["Wording changes (Remove cover) are on the rules page."], stage, table("Numbers for xr (current → proposed)", NUMS, rows, K)))


# ------------------------------------------------------------------ 9 Rules
def make_rules():
    cols = ""
    for t in ("light", "dark"):
        st = lambda cls, label: f'<div style="display:flex;flex-direction:column;align-items:center;gap:{m(4)}">{chip("Label", cls, 72)}<div class="s s2">{label}</div></div>'
        states = f'<div style="display:flex;flex-wrap:wrap;gap:{m(12)}">' + "".join(st(c, l) for c, l in [("", "Normal"), ("hover", "Hover"), ("press", "Pressed"), ("sel", "Selected"), ("dis", "Disabled"), ("danger", "Refused")]) + "</div>"
        specimens = "".join(f'<div style="display:flex;align-items:baseline;gap:{m(8)};height:{m(24)}"><span class="{c}" {sty}>{n}</span><span class="s s2">{d}</span></div>'
                            for c, sty, n, d in [("h", "", "Title, tab, chosen", "14 semibold"), ("b", "", "Body, button, field, key", "14 regular"),
                                                 ("sh", "", "Section name", "12 semibold, 76% ink"), ("s", "", "Caption, card name, hint", "12 regular")])
        sp = "".join(f'<div style="display:flex;align-items:center;gap:{m(8)}"><div style="width:{m(n)};height:{m(8)};background:var(--accent);border-radius:2px"></div><span class="s">{n} mm</span><span class="s s2">{u}</span></div>'
                     for n, u in [(4, "inside a card"), (8, "gap between chips, cards, keys"), (12, "between groups"), (16, "panel padding"), (24, "between big blocks")])
        sizes = (f'<div style="display:flex;gap:{m(12)};align-items:flex-end;flex-wrap:wrap">'
                 f'<div>{chip("Chip", "", 80)}<div class="s s2">36 high</div></div>'
                 f'<div><div class="u-btn" style="position:static">{icon("settings", 16)}</div><div class="s s2">28 icon</div></div>'
                 f'<div><div class="u-btn" style="position:static;width:{m(36)};height:{m(36)}">{icon("chevron-left", 18)}</div><div class="s s2">36 back</div></div>'
                 f'<div><div class="chip" style="width:{m(24.4)};height:{m(40)};border-radius:{m(12)};padding:0">a</div><div class="s s2">24 x 40 key</div></div>'
                 f'<div><div class="card" style="width:{m(84)};height:{m(92)};justify-content:center;padding:0"><div class="s">Card</div></div><div class="s s2">84 x 92</div></div>'
                 f'<div><div class="disc" style="width:{m(34)};height:{m(34)};background:var(--chip)"></div><div class="s s2">34 disc</div></div></div>')
        blk = lambda title, inner: f'<div class="cap">{title}</div><div>{inner}</div>'
        cols += (f'<div class="variant {t}" style="width:560px;gap:18px">{blk(t.capitalize() + " · states", states)}{blk("Text styles (the only four)", specimens)}'
                 f'{blk("Spacing scale", f"<div style=\"display:flex;flex-direction:column;gap:{m(6)}\">{sp}</div>")}{blk("Sizes", sizes)}</div>')
    typ = [
        ("Typeface", "!Inter in the app; mockups use Inter if installed, else the system font", "Inter Regular and Inter SemiBold only (already in the project)", "One family, two weights, no new download"),
        ("Title 14 semibold", "tabs 14 regular (selected 15 semibold); headers 13", "tabs (all, bold), page headers, the chosen step, the zone card title", "Tabs are bold as requested; the chosen tab is also filled"),
        ("Body 14 regular", "13 (buttons, row titles)", "buttons, row titles, field text, keys, steps", "One size for everything you press or read as a row"),
        ("Section name 12 semibold", "13 semibold; Settings 10 all caps", "section names at 76% ink, sentence case", "Same style on every tab"),
        ("Caption 12 regular", "!11 (cards, zone line 2), 10 (Settings), 12 min in spec", "captions, card names, hints, the safety line", "Never below 12"),
        ("Line heights", "mixed 16 to 20", "14 → 18, 12 → 15", "One rule"),
        ("Tab text width", "!53.6 mm each (equal)", "hug text + 12, spread across 268", "!Bold 'My room' does not fit 53.6; estimate, check on device"),
    ]
    boxes = [
        ("Pill behind the whole tab row", "Tabs are separate controls; only the chosen tab has a fill"),
        ("Pill behind Light | Dark", "Two options; only the chosen one is filled"),
        ("Divider lines between Settings rows", "Space (12 mm between groups) separates them"),
        ("Number discs and outlines in the Tidy up stepper", "The stepper is a status line, not controls"),
        ("Frame around the AI plan", "A text block under a section name"),
        ("The keyboard's own text field and handle bar", "A duplicate of the panel's field; the panel handle moves both"),
        ("Fills behind the Style Apply to toggles and the category names", "Text options with an underline for the chosen one"),
        ("Rims on buttons", "Chips are fill only; the ring is kept for the chosen state and focus")]
    spacing = [
        ("Panel width", "300", "300 (100%)", "unchanged"),
        ("Padding", "16 panel, 12 zone card, 12 keyboard", "16 panel and zone card (5.3% of width), 8 keyboard", "One per surface type"),
        ("Gap, chips cards keys", "8 (keys 4)", "8 (keys 4)", "unchanged"),
        ("Group gap", "12 to 22", "12 above a section name; title to content 6", "4 mm grid"),
        ("Strip, tabs, body start", "8 / 40 / 84", "8 / 40 / 84", "unchanged")]
    sizes_t = [
        ("Tab row", "36 high, 268 wide (89%)", "36 high, no container", ""),
        ("Chip, button", "36 high, text + 24", "36 high, text + 24, minimum 72 wide (24%)", "Short words do not shrink below a thumb"),
        ("Settings trailing control", "own width", "80 wide (27%)", ""),
        ("Icon button", "28 x 28, hit +4", "28 x 28, hit +8 (36 x 36)", "!xr change: raise the hit pad"),
        ("Back button", "none", "36 x 36 round", ""),
        ("Disc", "34", "34", ""),
        ("Card", "80 x 92", "84 x 92 (28% x 31%)", "3 columns across 268"),
        ("Key", "34 x 34, pitch 38", "24.4 x 40, pitch 28.4 x 44", ""),
        ("Hit padding", "4", "4 (icon buttons 8)", "Hands only: nothing gets smaller"),
        ("Radius", "panel 28, card 14, chip pill", "same; key 12, keyboard 20", "")]
    colours = [
        ("Text", "#0E1520", "#FFFFFF", "unchanged"), ("Secondary", "ink 76%", "white 82%", "unchanged"),
        ("Surface", "#FFFFFF 70%", "#0F1826 80%", "unchanged"), ("Chip fill", "ink 7%", "white 14%", "no rim any more"),
        ("Hover", "ink 17%, 1 mm lift", "white 26%, 1 mm lift", "unchanged"), ("Pressed", "ink 22%, 97% scale", "white 32%, 97% scale", "unchanged"),
        ("Chosen", "#2C2F36 / white text / #0A74E0 ring", "#FFFFFF / #0E1520 text / #6DB6FF ring", "unchanged"),
        ("Disabled", "ink 3% / text 40%", "white 5% / text 40%", "unchanged"), ("Refused", "#E5483B rim 12% fill; reason #C62F22", "same; reason #FF9A90", "unchanged")]
    rules = [
        ("W1 Case", "Sentence case everywhere. No ALL CAPS."),
        ("W2 Buttons", "Start with a verb, at most 3 words, no pronouns (this, the real one). No full stop."),
        ("W3 Names and captions", "Section names are 1 to 3 words. Captions are at most 8 words and fit one line. Neither ends with a full stop."),
        ("W4 Messages", "Instructions, warnings and toasts are full sentences and end with a full stop."),
        ("W5 One word per thing", "zone = the part that stays real (My zone); outside = the virtual rest; room = the physical room; space = a saved space only; cover = a Tidy up cover; design = a finish or furniture choice."),
        ("W6 Symbols", "No ellipsis, no middle dot in labels (use a comma). One name per tab: Style, My room, Add, AI, Tidy up."),
    ]
    wording = [
        ("Settings", "Section", "SPACE", "Room", "W1, W5"), ("Settings", "Section", "APPEARANCE", "Appearance", "W1"), ("Settings", "Section", "DESIGN", "Design", "W1"),
        ("Settings", "Section", "DEVELOPER", "(removed: Real occlusion sits under Design in dev builds)", "space"),
        ("Settings", "Row", "My real area", "My zone", "W5"), ("Settings", "Caption", "The part of the room that stays real", "The part that stays real", "W3"),
        ("Settings", "Caption", "Scan this room again with Space Setup", "Scan this room again", "W3"),
        ("AI", "Section", "Or start from a style", "Styles", "W3"), ("AI", "Hint", "The AI fills your virtual area", "The AI furnishes the outside zone.", "W4, W5"),
        ("AI", "Status", "Thinking…", "Thinking", "W6"), ("AI", "Count", "7 placed · 1 skipped", "7 placed, 1 skipped", "W6"),
        ("My room", "Chip", "Keep the real one", "Keep real", "W2"), ("Tidy up", "Chip", "Remove this cover", "Remove cover", "W2"),
        ("Style", "Section", "Floor · your real room", "Floor", "W6"), ("Style", "Section", "Wall · your real room", "Wall", "W6"),
        ("Unchanged", "", "Settings, Current space, Room scan, Rescan, Reset design, Light, Dark, Theme, Real occlusion; every zone card line and button; Describe the room you want, Create, Undo all, Cancel; Floor, Wall, Mood, Apply to, My zone, Outside; Pick a look, Draw a box, Done, Add another, Remove, the safety line; the tab names", "same", "already follow W1 to W6")]
    tabs = [
        ("Style", "Apply to options, section names, 34 mm discs with 12 mm labels"), ("My room, Add", "category options, 84 x 92 cards, chips or pager"),
        ("AI", "field 188 x 36, Create 72 x 36, Styles chips 130 x 36, plan text"), ("Tidy up", "stepper, 84 x 92 look cards, chips, safety line"),
        ("Settings sheet", "header 24, section 20, rows 40, controls 80 x 36")]
    tables = (table("Typeface and text styles (current → proposed, mm)", NUMS, typ, K)
              + table("Boxes removed (nothing here marked something pressable or grouped anything)", ["Removed", "What replaces it"], boxes)
              + table("Spacing (current → proposed, mm)", NUMS, spacing, K) + table("Sizes and buttons (share of the 300 mm width in brackets)", NUMS, sizes_t, K)
              + table("Colours (current → proposed)", ["Token", "Light", "Dark", "Change"], colours)
              + table("Which tab uses what", ["Tab", "Components"], tabs)
              + table("Wording rules", ["Rule", "Says"], rules)
              + table("Wording: every label that changes, current → proposed (the human approves this separately; layout numbers are above and do not depend on it)", ["Screen", "Kind", "Current", "Proposed", "Rule"], wording))
    write("rules", page("Shared rules for the five tabs", "One typeface, four text styles, one spacing scale, one set of sizes and states, and the wording rules every mockup follows.",
          ["One typeface and only four text styles: 14 semibold, 14 regular, 12 semibold, 12 regular.",
           "Boxes only where something is pressable or truly groups things (list of removals below).",
           "Wording has six short rules; the table of every changed label is kept apart for approval."],
          ["Orange cells are estimates or open decisions."], "".join(cols), tables))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    which = args[0] if args else "all"
    fns = {"settings": make_settings, "zone": make_zone, "style": make_style, "myroom": make_myroom, "add": make_add, "ai": make_ai,
           "keyboard": make_keyboard, "tidy": make_tidy, "rules": make_rules}
    for k, f in fns.items():
        if which in ("all", k): f()

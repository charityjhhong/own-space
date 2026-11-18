"""Shared pieces for the panel mockups (round 2): tokens, the glass panel shell, real icons, small components.

Drawn in millimetres at the panel's real proportions (--u = 2 px per mm; change it in the page CSS to scale).
Pages are self-contained: no network, no downloads. One typeface (Inter if installed, else the system font) and
four text styles only: 14 semibold, 14 regular, 12 semibold, 12 regular.
"""
import html, os, re

PANEL_W = 300
INNER = 268
HERE = os.path.dirname(os.path.abspath(__file__))
ICON_DIR = os.path.join(HERE, "..", "ui", "icons")


def m(n):
    return f"calc({n} * var(--u))"


def e(s):
    return html.escape(s, quote=False)


def icon(name, size=16):
    """The app's real glyph (docs/design/ui/icons/<name>.svg), one colour, 48 x 48 source."""
    svg = open(os.path.join(ICON_DIR, name + ".svg"), encoding="utf-8").read()
    svg = re.sub(r'\s(xmlns|width|height|color)="[^"]*"', "", svg)
    svg = svg.replace("<svg", f'<svg xmlns="http://www.w3.org/2000/svg" style="width:{m(size)};height:{m(size)};flex:none"', 1)
    return svg


CSS = """
:root{--u:2px;color-scheme:light dark}
*{box-sizing:border-box;margin:0}
body{font-family:Inter,-apple-system,"SF Pro Text","Helvetica Neue",Arial,sans-serif;background:#eef0f3;color:#0E1520;
  padding:24px 24px 64px;-webkit-font-smoothing:antialiased}
h1{font-size:22px;font-weight:600;margin-bottom:4px}
h2{font-size:16px;font-weight:600;margin:32px 0 8px}
p.lead{max-width:920px;line-height:1.5;color:#3a4350;margin-bottom:8px}
p.note{max-width:920px;line-height:1.5;color:#3a4350;font-size:13px;margin:6px 0}
.stage{display:flex;flex-wrap:wrap;gap:24px;align-items:flex-start;margin-top:12px}
.variant{border-radius:16px;padding:28px;display:flex;flex-direction:column;gap:14px;align-items:flex-start}
.variant.light{background:linear-gradient(180deg,#d9dfe6 0%,#cfd5db 55%,#b9ad9a 55%,#a89a86 100%)}
.variant.dark{background:linear-gradient(180deg,#1b2230 0%,#161c28 55%,#1d1a18 55%,#141210 100%)}
.cap{font-size:12px;font-weight:600;letter-spacing:.04em;text-transform:uppercase;color:#4a5361}
.dark .cap{color:#aab4c4}
.light{--fg:#0E1520;--fg2:rgba(14,21,32,.76);--chip:rgba(14,21,32,.07);--rim:rgba(14,21,32,.10);--hover:rgba(14,21,32,.17);
  --press:rgba(14,21,32,.22);--sel:#2C2F36;--seltext:#fff;--selrim:#0A74E0;--accent:#0A74E0;--red:#E5483B;--reason:#C62F22;
  --surface:rgba(255,255,255,.70);--surfrim:rgba(255,255,255,.80);--dis-fill:rgba(14,21,32,.03);--dis-text:rgba(14,21,32,.40);
  --shadow:0 calc(7*var(--u)) calc(8*var(--u)) rgba(0,0,0,.22)}
.dark{--fg:#fff;--fg2:rgba(255,255,255,.82);--chip:rgba(255,255,255,.14);--rim:rgba(255,255,255,.22);--hover:rgba(255,255,255,.26);
  --press:rgba(255,255,255,.32);--sel:#fff;--seltext:#0E1520;--selrim:#6DB6FF;--accent:#6DB6FF;--red:#E5483B;--reason:#FF9A90;
  --surface:rgba(15,24,38,.80);--surfrim:rgba(255,255,255,.35);--dis-fill:rgba(255,255,255,.05);--dis-text:rgba(255,255,255,.40);
  --shadow:0 calc(7*var(--u)) calc(8*var(--u)) rgba(0,0,0,.40)}
/* the four text styles */
.h,.b,.s,.sh{font-family:inherit}
.h{font-size:calc(14*var(--u));line-height:calc(18*var(--u));font-weight:600}
.b{font-size:calc(14*var(--u));line-height:calc(18*var(--u));font-weight:400}
.s{font-size:calc(12*var(--u));line-height:calc(15*var(--u));font-weight:400}
.sh{font-size:calc(12*var(--u));line-height:calc(15*var(--u));font-weight:600;color:var(--fg2)}
.s2{color:var(--fg2)}
.panel{color:var(--fg);background:var(--surface);border:calc(.4*var(--u)) solid var(--surfrim);box-shadow:var(--shadow);
  border-radius:calc(28*var(--u));position:relative}
.u-strip{position:absolute;left:0;right:0;top:calc(8*var(--u));height:calc(28*var(--u))}
.u-btn{position:absolute;top:0;width:calc(28*var(--u));height:calc(28*var(--u));border-radius:50%;background:var(--chip);
  display:flex;align-items:center;justify-content:center;color:var(--fg)}
.u-btn.lit{background:var(--sel);color:var(--seltext);box-shadow:0 0 0 calc(1.3*var(--u)) var(--selrim)}
.handle{position:absolute;left:50%;top:calc(11*var(--u));width:calc(50*var(--u));height:calc(6*var(--u));margin-left:calc(-25*var(--u));
  border-radius:99px;background:var(--hover)}
.tabs{position:absolute;left:calc(16*var(--u));top:calc(40*var(--u));width:calc(268*var(--u));height:calc(36*var(--u));display:flex;justify-content:space-between}
.tab{display:flex;align-items:center;justify-content:center;padding:0 calc(6*var(--u));border-radius:99px;white-space:nowrap}
.tab.sel{background:var(--sel);color:var(--seltext);box-shadow:0 0 0 calc(1.3*var(--u)) var(--selrim)}
.body{position:absolute;left:calc(16*var(--u));right:calc(16*var(--u));top:calc(84*var(--u))}
.chip{display:inline-flex;align-items:center;justify-content:center;height:calc(36*var(--u));border-radius:99px;
  background:var(--chip);color:var(--fg);white-space:nowrap;font-size:calc(14*var(--u));font-weight:400}
.chip.sel{background:var(--sel);color:var(--seltext);box-shadow:0 0 0 calc(1.3*var(--u)) var(--selrim)}
.chip.hover{background:var(--hover);transform:translateY(calc(-1*var(--u)))}
.chip.press{background:var(--press);transform:scale(.97)}
.chip.dis{background:var(--dis-fill);color:var(--dis-text)}
.chip.danger{box-shadow:0 0 0 calc(.8*var(--u)) var(--red) inset;background:color-mix(in srgb,var(--red) 12%,transparent)}
.chip.field{justify-content:flex-start;padding:0 calc(16*var(--u))}
.chip.focus{box-shadow:0 0 0 calc(1.3*var(--u)) var(--accent)}
.opt{display:inline-flex;align-items:center;height:calc(24*var(--u));padding:0 calc(2*var(--u));color:var(--fg2);white-space:nowrap}
.opt.on{color:var(--fg);font-weight:600;box-shadow:0 calc(-1.8*var(--u)) 0 var(--fg) inset}
.seg{display:inline-flex;align-items:center;justify-content:center;height:calc(36*var(--u));border-radius:99px;white-space:nowrap;color:var(--fg2)}
.seg.on{background:var(--sel);color:var(--seltext);font-weight:600;box-shadow:0 0 0 calc(1.3*var(--u)) var(--selrim)}
.disc{border-radius:50%;flex:none}
.disc.on{box-shadow:0 0 0 calc(2*var(--u)) var(--panelbg,transparent),0 0 0 calc(3.4*var(--u)) var(--accent)}
.card{border-radius:calc(14*var(--u));background:var(--chip);display:flex;flex-direction:column;align-items:center;padding-top:calc(6*var(--u));overflow:hidden}
.card.on{background:var(--sel);color:var(--seltext);box-shadow:0 0 0 calc(1.3*var(--u)) var(--selrim)}
.card img{width:calc(72*var(--u));height:calc(48*var(--u));object-fit:contain}
.card .s{text-align:center;padding:0 calc(4*var(--u));margin-top:calc(4*var(--u))}
.card.on .s{font-weight:600}
table.nums{border-collapse:collapse;font-size:13px;margin:8px 0 4px;max-width:980px;width:100%;background:#fff;border-radius:10px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.12)}
table.nums th{background:#e6e9ee;text-align:left;font-weight:600;padding:8px 10px}
table.nums td{padding:7px 10px;border-top:1px solid #e3e6ea;vertical-align:top;line-height:1.4}
table.nums td.k{font-weight:600}
table.nums td.fl{color:#9a3412}
table.nums td.cur{color:#555}
table.nums td.new{font-weight:600}
code{font-family:ui-monospace,Menlo,monospace;font-size:12px;background:#eceff3;padding:1px 4px;border-radius:4px}
.changes{max-width:920px;background:#fff;border-radius:10px;padding:10px 16px;margin:10px 0;box-shadow:0 1px 3px rgba(0,0,0,.12)}
.changes b{font-size:13px}
.changes ol{font-size:14px;line-height:1.5;padding-left:20px}
"""

TABS = ["Style", "My room", "Add", "AI", "Tidy up"]


def panel(selected_tab, body, height, gear_lit=False, width=PANEL_W):
    """Glass panel: strip (undo, redo, handle, gear, minimize), tab row (bold text, no container), body from y = 84."""
    tabs = "".join(f'<div class="tab h{" sel" if i == selected_tab else ""}">{e(t)}</div>' for i, t in enumerate(TABS))
    b = lambda x, ic, lit=False: f'<div class="u-btn{" lit" if lit else ""}" style="left:{m(x)}">{icon(ic, 16)}</div>'
    strip = (b(16, "undo") + b(48, "redo") + '<div class="handle"></div>' + b(width - 16 - 28 - 32, "settings", gear_lit) + b(width - 16 - 28, "minimize"))
    return (f'<div class="panel" style="width:{m(width)};height:{m(height)}"><div class="u-strip">{strip}</div>'
            f'<div class="tabs">{tabs}</div><div class="body">{body}</div></div>')


def variant(theme, label, content):
    return f'<div class="variant {theme}"><div class="cap">{e(label)}</div>{content}</div>'


def table(title, header, rows, kinds=None):
    """rows: tuples; a cell starting with '!' is flagged (estimate or open decision)."""
    h = "".join(f"<th>{e(c)}</th>" for c in header)
    out = []
    for r in rows:
        cells = []
        for i, c in enumerate(r):
            flag = c.startswith("!")
            c = c[1:] if flag else c
            cls = "k" if i == 0 else "fl" if flag else (kinds[i] if kinds and i < len(kinds) and kinds[i] else "")
            cells.append(f'<td class="{cls}">{e(c)}</td>')
        out.append("<tr>" + "".join(cells) + "</tr>")
    return f"<h2>{e(title)}</h2><table class=\"nums\"><tr>{h}</tr>{''.join(out)}</table>"


def page(title, lead, changes, notes, stage, tables):
    ch = ('<div class="changes"><b>What changes</b><ol>' + "".join(f"<li>{e(c)}</li>" for c in changes) + "</ol></div>") if changes else ""
    nh = "".join(f'<p class="note">{n}</p>' for n in notes)
    return (f'<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            f'<title>{e(title)}</title><style>{CSS}</style></head><body><h1>{e(title)}</h1><p class="lead">{lead}</p>{ch}{nh}'
            f'<div class="stage">{stage}</div>{tables}</body></html>')


# ----- small components shared by the screens
def chip(label, cls="", w=None, extra=""):
    return f'<div class="chip {cls}" style="{"width:"+m(w)+";" if w else "padding:0 "+m(12)+";min-width:"+m(72)+";"}{extra}">{e(label)}</div>'


def section(text, top=0):
    return f'<div class="sh" style="height:{m(20)};margin-top:{m(top)};display:flex;align-items:flex-end;padding-bottom:{m(2)}">{e(text)}</div>'


def stepper(current, labels=("Draw a box", "Pick a look", "Done")):
    out = []
    for i, n in enumerate(labels):
        cur, done = i == current, i < current
        mark = icon("check", 14) if done else f'<span class="{"h" if cur else "b"}">{i+1}</span>'
        out.append(f'<div style="display:flex;align-items:center;gap:{m(6)};opacity:{1 if cur or done else .4}">{mark}<span class="{"h" if cur else "b"}">{e(n)}</span></div>')
    return f'<div style="display:flex;justify-content:space-between;height:{m(28)};align-items:center">{"".join(out)}</div>'

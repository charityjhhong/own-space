# Panel mockups (content, P5, round 2)

Self-contained HTML mockups of the tool-panel screens at the panel's real proportions (300 mm wide; `--u` = 2 px per mm), each with a Light and a Dark variant, a three-item "What changes" list, a **current → proposed** layout table, and a **PNG render of the whole page** next to it. No network, no downloads; the app's own glyphs (`docs/design/ui/icons/`) and the atlas thumbnails are embedded.

| Screen | Page | Image |
| --- | --- | --- |
| Settings list | `settings.html` | `settings.png` |
| Zone card | `zone-card.html` | `zone-card.png` |
| Style | `style.html` | `style.png` |
| My room | `my-room.html` | `my-room.png` |
| Add | `add.html` | `add.png` |
| AI (no keyboard) | `ai-tab.html` | `ai-tab.png` |
| AI with keyboard | `ai-keyboard.html` | `ai-keyboard.png` |
| Tidy up, every step | `tidy-up.html` | `tidy-up.png` |
| Shared rules (type, spacing, sizes, states, wording) | `rules.html` | `rules.png` |

Regenerate: `python3 docs/design/mockups/make_mockups.py [all|settings|zone|style|myroom|add|ai|keyboard|tidy|rules] [--no-png]` (PNGs need Google Chrome and Pillow). Wording changes are collected apart, in `rules.html`, for separate approval. Numbers marked orange are estimates. Not seen on a headset.

# Store art (Meta Horizon dashboard)

Specs from Meta's branded asset guidelines (https://developers.meta.com/horizon/resources/asset-guidelines/). **Check the dashboard fields before uploading**: one third-party guide quotes a 1024 x 1024 icon, Meta's own page says 512 x 512.

| Asset | Size | Format | File |
| --- | --- | --- | --- |
| Icon (1:1, solid, square corners; the system rounds them) | 512 x 512 | 24-bit PNG | `icon-512.png` |
| Cover landscape (16:9) | 2560 x 1440 | 24-bit PNG | `cover-landscape-2560x1440.png` |
| Cover square (1:1) | 1440 x 1440 | 24-bit PNG | `cover-square-1440x1440.png` |
| Cover portrait (7:10) | 1008 x 1440 | 24-bit PNG | `cover-portrait-1008x1440.png` |
| Hero cover (10:3) | 3000 x 900 | 24-bit PNG | `hero-3000x900.png` |
| Logo, transparent (optional, up to 9000 x 1440) | 4500 x 720 | 32-bit PNG | `logo-transparent-4500x720.png` |
| Screenshots (five, 16:9, no duplicates) | 2560 x 1440 | 24-bit PNG | **device captures, not drawn** |

Rules we follow: clear branding and no taglines on cover art; no text in the top or bottom 20% of cover art; the title sits in the centre safe area. Screenshots must be real captures of the app with no added logos, text or icons.

**`screenshots-placeholder/` are placeholders only** (a synthetic room, the UI mockup and a red "not for upload" bar). Replace them with five captures on device day, following the shots in `docs/demo/storyboard.md`.

The art is our own (a synthetic room from `docs/design/ui`, the Inter font under the SIL OFL, no Apple marks). Regenerate: `python3 submission/art/make_store_art.py`.

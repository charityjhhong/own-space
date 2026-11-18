# Asset sources (P0-D-3)

Candidate free sources for furniture models and finish textures. Issue: #10. **Nothing was downloaded.** Poly counts are typical ranges from memory and must be checked per model. Facts marked "checked" were read on the source's own license page; everything else is from memory and must be confirmed at import time.

## Rules for importing

- **Allowed licenses:** CC0 (preferred), then CC-BY 3.0 or 4.0 (needs a credit in the app and in `submission/`). Models we make ourselves are `OWN`.
- **Not allowed:** any NonCommercial (NC) or NoDerivatives (ND) license, ShareAlike (SA) licenses (they can force our edits to be shared under the same terms), "personal use only", "editorial use only", and any license we cannot read on the page.
- Record each import as a row in `asset-licenses.md` (asset, source URL, license, author, date). Save a screenshot or PDF of the license page next to it when the license is per-model.
- **Budget (proposal):** at most 5,000 triangles per piece (target 1,000-3,000), one 1024 px atlas per style, no per-piece 4K textures. Heavy models must be decimated first.
- Brand-name furniture (IKEA and similar) is not used, even when a modeler uploaded it under CC-BY, to avoid trademark and design questions.

## Sources

### 1. Kenney (kenney.nl), Furniture Kit and others
- **URL:** https://kenney.nl/assets/furniture-kit
- **License:** CC0 (checked).
- **Style fit:** simple, flat-color low poly. Fits `scandi` and `warm-wood` after recoloring with our palettes. Too toy-like for `cafe` unless retextured.
- **Typical poly count:** about 100-1,500 triangles.
- **Disqualifiers:** none for license. The look is stylized, so it will not match photographic passthrough closely. Sizes are not realistic and must be rescaled to the catalog footprint.

### 2. Quaternius (quaternius.com)
- **URL:** https://quaternius.com/
- **License:** CC0 for all models on the site (checked, FAQ).
- **Style fit:** stylized low poly packs. Same use as Kenney. Pick packs with indoor furniture.
- **Typical poly count:** about 200-3,000 triangles.
- **Disqualifiers:** none for license. Some packs use vertex colors, which is fine. Check that a pack is not a paid "Patreon extra".

### 3. Poly Haven (polyhaven.com) models and textures
- **URL:** https://polyhaven.com/models and https://polyhaven.com/textures
- **License:** CC0 for all models and textures (checked). Attribution optional.
- **Style fit:** photoreal. Best for `warm-wood` and `cafe` (chairs, shelves, plants, decor). The textures (wood, plaster, fabric, floor) are the best free source for the 6-8 floor and wall finishes in P2-D.
- **Typical poly count:** 5,000-100,000+ triangles; textures come at 1K to 8K.
- **Disqualifiers:** the furniture catalog is small. Models must be decimated and their textures cut to 1K before use. The terms forbid scraping, so download by hand only.

### 4. KayKit (kaylousberg.com)
- **URL:** https://kaylousberg.itch.io/ (furniture and interior packs)
- **License:** CC0 for the free packs (from memory; confirm per pack).
- **Style fit:** stylized low poly, strong shapes. Useful for plants, lamps and small props.
- **Typical poly count:** about 100-2,000 triangles.
- **Disqualifiers:** paid versions of some packs are not CC0. Use only the free pack and confirm its license on the page.

### 5. Poly Pizza (poly.pizza)
- **URL:** https://poly.pizza/
- **License:** per model, CC0 or CC-BY 3.0/4.0 (from memory; the site's FAQ page could not be read).
- **Style fit:** a search layer over low poly models, including some from Kenney and Quaternius. Good for finding single pieces such as a rug or a lamp.
- **Typical poly count:** 100-5,000 triangles.
- **Disqualifiers:** the license varies by model. Reject anything that is not CC0 or CC-BY. CC-BY items need a credit row in `asset-licenses.md` and in the app credits.

### 6. Sketchfab (sketchfab.com), filtered to downloadable CC0
- **URL:** https://sketchfab.com/features/free-3d-models (filter: Downloadable, License CC0)
- **License:** per model; many are CC-BY, CC-BY-NC or editorial.
- **Style fit:** the widest range, including realistic dorm-size furniture and café pieces.
- **Typical poly count:** 2,000-200,000 triangles; quality varies a lot.
- **Disqualifiers:** only CC0 and CC-BY models are allowed. Reject NC, ND, SA and "Standard" licenses. Check that the uploader is the original author, since re-uploads of other people's models are common. Needs a Sketchfab account to download (a login the human provides; agents do not create accounts).

### 7. OpenGameArt (opengameart.org), filtered to CC0
- **URL:** https://opengameart.org/art-search-advanced (License: CC0)
- **License:** per item: CC0, CC-BY, CC-BY-SA, GPL and others.
- **Style fit:** low poly and older assets; mostly low value for this look.
- **Typical poly count:** 100-3,000 triangles.
- **Disqualifiers:** many items are CC-BY-SA or GPL, which are not allowed. Use only as a backup.

### 8. ambientCG (ambientcg.com), textures only
- **URL:** https://ambientcg.com/
- **License:** CC0 (from memory; confirm on the license page).
- **Style fit:** PBR textures for floors and walls (wood planks, plaster, tiles, fabric). No models.
- **Typical size:** 1K-8K PBR sets; use 1K.
- **Disqualifiers:** none known.

### 9. Unity Asset Store (free packs), fallback only
- **URL:** https://assetstore.unity.com/
- **License:** Unity Asset Store EULA, not CC0. It allows use inside a published game or app, but forbids reselling or redistributing the asset on its own.
- **Style fit:** varies.
- **Typical poly count:** 500-20,000 triangles.
- **Disqualifiers:** the human must accept the EULA with their own account. Some packs are made for built-in render pipeline only and need a material upgrade to URP. Record the pack and its publisher in `asset-licenses.md` with license id `UNITY-ASSET-STORE-EULA`. Use only if a gap cannot be filled by CC0 sources.

## Rejected

| Source | Reason |
| --- | --- |
| Free3D, many free-model sites | Mixed or "personal use only" terms; license often unclear |
| TurboSquid / CGTrader free tiers | Per-item custom "royalty-free" terms; some forbid use in apps that redistribute the asset |
| Synty and other paid packs | Not free; the license is per-seat and needs a purchase decision by the human |
| Google Poly archive mirrors | No official source; license data is unreliable |

## Suggested first pass for catalog v0

1. Use Kenney and Quaternius pieces for placeholders and the first playable build (fast, CC0, tiny poly count).
2. Use Poly Haven for plants, decor and all floor and wall textures.
3. Re-check each catalog row's `license` field against `asset-licenses.md` before the submission.

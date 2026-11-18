# CC0 asset shortlist for approval (P3-D-5)

Specific models to download, for the human to approve in one go. **Status: approved by the human except four models marked rejected below; downloaded, converted and imported (see `asset-licenses.md`).** Originally nothing was downloaded. Rules and license policy are in `asset-sources.md`; every import gets a row in `asset-licenses.md`.

How the numbers were read: Poly Haven is CC0 for all models (license page checked). Names, slugs and the "polycount" were read from Poly Haven's public asset list (`api.polyhaven.com`); whether "polycount" means triangles or faces is **unverified**, so treat it as a rough size. Each model has its own page, `https://polyhaven.com/a/<slug>`, listed below. **Confirmed** = the page was opened and shows the model name and CC0. **From list** = the slug comes from Poly Haven's asset list and the page was not opened; open it before approving. Our budget is at most 5,000 triangles per piece and 1K textures, so models above that need decimating in Blender first (extra work for `content`; noted in the last column). Download the **1K** texture option.

## Poly Haven picks (all CC0)

| Catalog kind (style) | Slug | Polycount | Notes |
| --- | --- | --- | --- |
| desk (warm-wood, scandi) | ~~[`SchoolDesk_01`](https://polyhaven.com/a/SchoolDesk_01)~~ **rejected by the human** (confirmed) | 4,162 | Student desk, about dorm scale; wood top, metal legs. Check the usable height is about 0.74 m |
| desk (warm-wood) | [`small_wooden_table_01`](https://polyhaven.com/a/small_wooden_table_01) (from list) | 3,410 | Simple small table; vintage look |
| desk (night-city) | ~~[`metal_office_desk`](https://polyhaven.com/a/metal_office_desk)~~ **rejected by the human** (from list) | 6,898 | Industrial, worn; decimate slightly |
| closet / storage (warm-wood) | ~~[`painted_wooden_cabinet`](https://polyhaven.com/a/painted_wooden_cabinet)~~ **rejected by the human** (from list) | 2,227 | Farmhouse look, low poly |
| closet / storage (scandi) | [`modern_wooden_cabinet`](https://polyhaven.com/a/modern_wooden_cabinet) (from list) | 24,976 | Clean modern; **decimate heavily** |
| shelf (warm-wood, scandi) | [`wooden_display_shelves_01`](https://polyhaven.com/a/wooden_display_shelves_01) (from list) | 3,174 | Modern pine display shelf |
| shelf (any, tiny) | [`Shelf_01`](https://polyhaven.com/a/Shelf_01) (from list) | 182 | Extremely light bookcase; plain |
| shelf (night-city) | [`steel_frame_shelves_03`](https://polyhaven.com/a/steel_frame_shelves_03) (from list) | 8,396 | Dark frame; decimate |
| sofa (warm-wood, cafe) | [`sofa_02`](https://polyhaven.com/a/sofa_02) (from list) | 2,728 | Leather and fabric, low poly |
| armchair (cafe) | [`mid_century_lounge_chair`](https://polyhaven.com/a/mid_century_lounge_chair) (from list) | 6,148 | Wood and leather; fits cafe |
| armchair (scandi, warm-wood) | [`modern_arm_chair_01`](https://polyhaven.com/a/modern_arm_chair_01) (from list) | 8,916 | Decimate |
| coffee table (cafe) | [`coffee_table_round_01`](https://polyhaven.com/a/coffee_table_round_01) (confirmed) | 4,044 | Marble and metal; matches our marble table |
| coffee table (warm-wood, scandi) | [`modern_coffee_table_01`](https://polyhaven.com/a/modern_coffee_table_01) (from list) | 4,504 | Stone and wood, clean |
| side table (any) | [`side_table_01`](https://polyhaven.com/a/side_table_01) (from list) | 2,756 | Minimal wood |
| pendant lamp (scandi, warm-wood) | [`modern_ceiling_lamp_01`](https://polyhaven.com/a/modern_ceiling_lamp_01) (from list) | 5,602 | Simple ceiling lamp |
| pendant lamp (night-city) | [`hanging_industrial_lamp`](https://polyhaven.com/a/hanging_industrial_lamp) (from list) | 9,530 | Decimate |
| plant (any) | [`potted_plant_04`](https://polyhaven.com/a/potted_plant_04) (from list) | 6,097 | The only light plant; `potted_plant_01` (96,030) and `_02` (69,806) are too heavy |
| clutter basket (warm-wood, scandi) | [`wicker_basket_01`](https://polyhaven.com/a/wicker_basket_01) (from list) / [`wicker_basket_02`](https://polyhaven.com/a/wicker_basket_02) (from list) | 22,276 / 17,850 | **Decimate to about 3,000** |
| clutter floor cushion / cushions | [`throw_pillows_01`](https://polyhaven.com/a/throw_pillows_01) (confirmed) | 6,362 | Good for the pile of cushions |
| wall art | [`hanging_picture_frame_01`](https://polyhaven.com/a/hanging_picture_frame_01) (from list) / [`hanging_picture_frame_02`](https://polyhaven.com/a/hanging_picture_frame_02) (from list) | 2,586 / 3,678 | Frames; swap the picture for our own art |
| folding screen (cafe, warm-wood) | ~~[`chinese_screen_panels`](https://polyhaven.com/a/chinese_screen_panels)~~ **rejected by the human** (from list) | 784 | Ornate; check it suits the style |

Per-model pages for the Kenney kit cannot be linked: Kenney publishes the kit as one zip and its page lists no model names. **Not confirmable without downloading.**

## Not found on Poly Haven (use Kenney or build ourselves)

- **Bed:** Poly Haven has only `GothicBed_01` (18,741), `old_bed_frame` (49,990) and `vintage_day_bed` (2,715); none fits a modern dorm single bed. Use the Kenney Furniture Kit bed, or keep our generated bed.
- **Floor lamp, closet doors, rugs, curtains, door trims, storage box, wardrobe:** no suitable Poly Haven model. Keep our generated pieces (`OWN`) or take them from the Kenney kit.
- **Desk lamp:** `desk_lamp_arm_01` (25,710) exists but is not a catalog kind; skip.

## Kenney Furniture Kit (CC0, one download)

- **URL:** https://kenney.nl/assets/furniture-kit. License CC0 (checked); 140 files; released 2018; tags furniture, interior, table, chair, bed. The page does not list model names or triangle counts; **unverified** until the pack is opened. Kenney models are very light (typically a few hundred triangles) and flat-colored, so they work for beds, rugs, lamps and plants if recolored to our palettes.

## What to approve

1. **Download list:** the 20 Poly Haven models above plus the Kenney Furniture Kit (one zip).
2. **Texture size:** 1K only.
3. **After download:** `content` decimates models over 5,000 triangles, rescales to catalog sizes, adds rows to `asset-licenses.md`, and flips `isPlaceholder` and `license` (`CC0-1.0`) in `catalog.json` item by item.
4. **Fallback:** where a model does not fit the style or size, the generated placeholder stays.

## Risks

- Most Poly Haven furniture is vintage or worn, which fits `warm-wood` and `cafe` better than `scandi` and `night-city`. Expect to recolor or keep our generated pieces for those two styles.
- Poly counts above may not mean triangles; check each model in Blender.

## Import result

- **Imported (33 catalog items):** armchair-cafe-01, armchair-scandi-01, armchair-warm-wood-01, bed-cafe-01, bed-night-city-01, bed-scandi-01, bed-warm-wood-01, clutter-plant-cafe-01, clutter-plant-night-city-01, clutter-plant-scandi-01, clutter-plant-warm-wood-01, coffee-table-night-city-01, coffee-table-warm-wood-01, desk-cafe-01, desk-night-city-01, desk-scandi-01, desk-warm-wood-01, lamp-night-city-01, lamp-scandi-01, pendant-cafe-01, pendant-warm-wood-01, plant-warm-wood-01, rug-cafe-01, rug-night-city-01, rug-scandi-01, rug-warm-wood-01, shelf-cafe-01, sofa-cafe-01, sofa-night-city-01, sofa-scandi-01, sofa-warm-wood-01, wall-art-night-city-01, wall-art-warm-wood-01.
- **Method:** glTF 1K from Poly Haven, OBJ from the Kenney kit; each scaled per axis to the catalog size (only where the axis ratios differ by at most 1.4x, otherwise the generated placeholder stays), pivot at bottom center, back toward -Z for seating and beds. Models over 5,000 triangles were decimated to at most 4,800 and flattened to one color per material (textures dropped); the rest keep their 1K diffuse map.
- **Placeholders kept** (no model fit the size or style): all closets, shelf-warm-wood, shelf-scandi, shelf-night-city, coffee-table-cafe, lamp-warm-wood, pendant-night-city, plant-cafe, plant-scandi, wall-art-scandi, the baskets, cushions and floor cushions.
- **Needs a headset or editor look:** model orientation (front toward +Z) was inferred, not seen; check sofas, armchairs and beds in Play.

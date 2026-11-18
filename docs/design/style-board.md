# Style board (P0-D-1)

Four interior styles for the first catalog. Issue: #8. Style ids below are the values used in the `styles` field of `catalog.json`.

## Art direction in one page

Furniture must sit next to real objects seen through passthrough, so the look is **warm, matte and slightly desaturated**, never glossy or neon.

1. **Match the real room, not a showroom.** Real dorm walls and desks are beige, white and light wood. Pick mid-value colors (HSV value 25–85%). No pure white (clips in passthrough) and no pure black (looks like a hole).
2. **Matte first.** Roughness 0.6–0.9. Real surfaces next to us are matte, so a glossy virtual piece reads as fake. Only small accents (metal, glass) may be shinier.
3. **Soft, simple shapes.** Rounded edges (bevel 1–2 cm) catch light and hide the small size mismatch against the real box. Avoid thin parts under 2 cm; they flicker with MSAA 4x.
4. **One shared material per style.** One atlas texture (1024 px max) and one URP Lit material per style keeps draw calls low (budget in the team plan).
5. **Baked look.** Fake the shadow with a baked contact-shadow strip under each piece; no real-time shadows.
6. **My-zone pieces fit the real box as is** (`CONCEPT.md` 5.2; fit rule in `catalog-schema.md`). Width and depth are never stretched; the box is enlarged by 5 cm per side for the comparison, and the model scales uniformly on Y by up to 15% so its usable surface (or its top, for storage) lands on the real height. Keep desk legs and bed frames free of fine detail on the Y axis so that scaling is not visible, and give every piece a clean outer edge that covers the real box.
7. **Never cover the user's things.** Desk and bed tops stay plain and empty (no baked-in books or lamps on the usable surface).

Passthrough is color-adjusted per mood (see `mood-presets.md`), so all palettes below are tuned for the **afternoon** preset (neutral light). Moods shift them.

## 1. Warm wood (`warm-wood`)

A cozy study-and-rest room. The default for the demo: it sits closest to real dorm furniture.

- **Palette:** oak `#C9A27A`, walnut `#8A5A3C`, cream `#F1E6D3`, terracotta `#B96A4B`, sage accent `#8E9B7A`, charcoal detail `#3A3532`
- **Materials:** matte oiled wood, linen fabric, woven rattan, brushed brass for small accents
- **Silhouettes:** straight legs with a slight taper, rounded corners, sofas with low arms, paper-shade floor lamps
- **Suits moods:** morning, afternoon, evening (night works with lamps on)

## 2. Scandinavian (`scandi`)

Bright and calm. Best for study and focus.

- **Palette:** off-white `#EDE9E0`, birch `#DCC7A1`, soft gray `#B8BCBE`, dusty blue `#8FA3B5`, muted black `#2F3133`
- **Materials:** pale birch, white-painted board, light wool, thin matte-black metal
- **Silhouettes:** slim tapered legs, flat clean fronts, handle-less closets, platform beds with a low headboard
- **Suits moods:** morning, afternoon (evening acceptable)

## 3. Café (`cafe`)

Relaxed and slightly dark. For the outside zone: a lounge or reading corner.

- **Palette:** espresso `#4B3428`, caramel leather `#A8693A`, cream marble `#E4DCCF`, olive green `#5E6B3F`, brass `#B08D4A`
- **Materials:** aged leather, dark stained wood, marble tops, plants, ceramic
- **Silhouettes:** round table tops, deep rounded sofas, wood-and-metal shelving, hanging or arc lamps
- **Suits moods:** afternoon, evening

## 4. Night city (`night-city`)

Cool and quiet, with a small glow. For night rest. Keep the glow small so it works in passthrough and stays under the overdraw budget.

- **Palette:** ink navy `#1F2A3D`, slate `#3C4A5E`, steel gray `#7C8896`, window blue `#8FA8D8`, amber glow `#F2B35E`, warm white `#F4E9D8`
- **Materials:** dark matte laminate, powder-coated metal, smoked glass, one emissive accent per piece (an LED strip or lamp shade only)
- **Silhouettes:** low and long shapes, thin metal frames, slim floor lamps, plain dark closets
- **Suits moods:** evening, night

## Mood fit at a glance

| Style | Morning | Afternoon | Evening | Night |
| --- | --- | --- | --- | --- |
| warm-wood | good | best | best | ok |
| scandi | best | best | ok | weak |
| cafe | weak | good | best | good |
| night-city | weak | weak | good | best |

"Weak" means the style still works, but the palette loses contrast or looks flat in that light.

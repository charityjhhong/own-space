# AI presets: sets

Each AI preset (the quick picks in the AI tab) is one **set**: an outside floor finish, a wall finish, a mood and the pieces that belong together. Core (`PresetSets`) reads the catalog tag `set:<preset id>`; the sets below are exactly the pieces of each preset in `CannedPlans`, so the offline plan and the AI's set agree. `Content/presets.json` holds them for tools, and `docs/design/apply_set_tags.py` writes the tags into `catalog.json`.

**Tags are not applied yet.** Core's `PresetSetTests` assume the real catalog carries no `set:` tags (they tag a copy themselves, and one test needs `calm-bedroom` untagged), so tagging `catalog.json` now fails two tests. Core: make those tests build their own tagged catalog, then run `python3 docs/design/apply_set_tags.py <repo>` (or ask content to) and the AI uses the sets.

**Status column:** `photo` = an imported CC0 Poly Haven model with textures; `flat` = a Kenney flat-colour model; `placeholder` = a generated shape. Flat and placeholder pieces are replaced in the model PRs (ids and sizes stay, so the sets do not change when they land). The finishes are the PBR sets from #204.

## Update: the four quick picks (content realism pass)

`presets.json` and the catalog `set:` tags now give each quick pick one coherent set in **one palette**, 6 to 7 pieces, **no curtains** (the AI places catalog pieces only, outside my zone; beds are my-zone pieces, so a bedroom set has no bed):

| Quick pick (preset id) | Palette | Pieces |
| --- | --- | --- |
| Living room (`cozy-living-room`) | warm wood | rug, sofa, coffee table, floor lamp, plant, shelf, wall art |
| Study café (`study-cafe`) | café (dark wood, leather) | rug, sofa, armchair, shelf, pendant lamp, plant, wall art (warm-wood/café) |
| Bedroom (`calm-bedroom`) | scandi | rug, armchair, floor lamp, low shelf, plant, wall art; birch floor, cream wall, evening |
| Bright studio (`bright-studio`) | scandi | rug, sofa, armchair, floor lamp, plant, low shelf, wall art |

`Contracts/CannedPlans.cs` (core) still lists the older pieces for the offline plan (Study café: warm-wood coffee table, no sofa or art; Calm bedroom: `lamp-warm-wood-01` and `curtain-scandi-01`, no art or armchair). Core: copy the lists above so the offline plan matches the tags.


## Cozy living room (`cozy-living-room`, tag `set:cozy-living-room`)

- Outside floor `floor-oak-planks`, wall `wall-cream`, mood `evening`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-warm-wood-01` | Woven jute rug | rug | flat (Kenney) |
| `sofa-warm-wood-01` | Linen two-seat sofa | sofa | photo |
| `coffee-table-warm-wood-01` | Oak coffee table | coffee-table | photo |
| `lamp-warm-wood-01` | Paper floor lamp | lamp | placeholder |
| `plant-warm-wood-01` | Small fern | plant | photo |
| `shelf-warm-wood-01` | Oak bookshelf | shelf | placeholder |

## Study library (`study-library`, tag `set:study-library`)

- Outside floor `floor-birch-planks`, wall `wall-soft-stripes`, mood `morning`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-scandi-01` | Wool rug | rug | flat (Kenney) |
| `armchair-scandi-01` | Light armchair | armchair | photo |
| `shelf-scandi-01` | Low birch shelf | shelf | placeholder |
| `lamp-scandi-01` | Birch floor lamp | lamp | flat (Kenney) |
| `plant-scandi-01` | Tall leaf plant | plant | placeholder |

## Café (`cafe`, tag `set:cafe`)

- Outside floor `floor-dark-planks`, wall `wall-olive`, mood `afternoon`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-cafe-01` | Large patterned rug | rug | flat (Kenney) |
| `sofa-cafe-01` | Leather lounge sofa | sofa | photo |
| `armchair-cafe-01` | Leather armchair | armchair | photo |
| `coffee-table-cafe-01` | Marble coffee table | coffee-table | placeholder |
| `shelf-cafe-01` | Espresso bookcase | shelf | photo |
| `plant-cafe-01` | Large leaf plant | plant | placeholder |

## Night lounge (`night-lounge`, tag `set:night-lounge`)

- Outside floor `floor-concrete-tiles`, wall `wall-navy-panels`, mood `night`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-night-city-01` | Dark runner rug | rug | flat (Kenney) |
| `sofa-night-city-01` | Slate lounge sofa | sofa | photo |
| `coffee-table-night-city-01` | Low dark table | coffee-table | photo |
| `lamp-night-city-01` | Slim amber floor lamp | lamp | flat (Kenney) |
| `shelf-night-city-01` | Slim dark shelf | shelf | placeholder |

## Study café (`study-cafe`, tag `set:study-cafe`)

- Outside floor `floor-dark-planks`, wall `wall-cream`, mood `afternoon`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-cafe-01` | Large patterned rug | rug | flat (Kenney) |
| `coffee-table-cafe-01` | Marble coffee table | coffee-table | placeholder |
| `armchair-cafe-01` | Leather armchair | armchair | photo |
| `shelf-cafe-01` | Espresso bookcase | shelf | photo |
| `pendant-cafe-01` | Brass dome pendant | pendant-lamp | photo |
| `plant-cafe-01` | Large leaf plant | plant | placeholder |

## Calm bedroom (`calm-bedroom`, tag `set:calm-bedroom`)

- Outside floor `floor-oak-planks`, wall `wall-cream`, mood `night`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-scandi-01` | Wool rug | rug | flat (Kenney) |
| `plant-scandi-01` | Tall leaf plant | plant | placeholder |
| `lamp-warm-wood-01` | Paper floor lamp | lamp | placeholder |
| `curtain-scandi-01` | Light curtains | curtain | placeholder |
| `shelf-scandi-01` | Low birch shelf | shelf | placeholder |

## Bright studio (`bright-studio`, tag `set:bright-studio`)

- Outside floor `floor-birch-planks`, wall `wall-cream`, mood `morning`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-scandi-01` | Wool rug | rug | flat (Kenney) |
| `sofa-scandi-01` | Gray three-seat sofa | sofa | photo |
| `armchair-scandi-01` | Light armchair | armchair | photo |
| `lamp-scandi-01` | Birch floor lamp | lamp | flat (Kenney) |
| `plant-scandi-01` | Tall leaf plant | plant | placeholder |
| `wall-art-scandi-01` | Minimal print | wall-art | placeholder |

## Reading nook (`reading-nook`, tag `set:reading-nook`)

- Outside floor `floor-oak-planks`, wall `wall-olive`, mood `evening`.

| Piece id | Name | Kind | Status |
| --- | --- | --- | --- |
| `rug-warm-wood-01` | Woven jute rug | rug | flat (Kenney) |
| `armchair-warm-wood-01` | Linen armchair | armchair | photo |
| `lamp-warm-wood-01` | Paper floor lamp | lamp | placeholder |
| `shelf-warm-wood-01` | Oak bookshelf | shelf | placeholder |
| `plant-warm-wood-01` | Small fern | plant | photo |

## Recommendations to core (from the human's "the Study café setup looks bad, make it realistic")

- **Study café look:** warm oak floor `floor-oak-planks` with the dark-green plaster wall `wall-olive`, mood `afternoon` (now dark planks with cream). It suits the espresso wood, leather and brass, and shows the new PBR finishes. A one-line edit of that preset in `CannedPlans`.
- **Study café pieces:** the set is all style `cafe` (espresso, leather, brass), so it matches. Two pieces are not photoreal yet: `coffee-table-cafe-01` (a placeholder marble table) and `rug-cafe-01` (a Kenney flat rug). Until the model PRs replace them, an oak `coffee-table-warm-wood-01` and `plant-warm-wood-01` are photoreal alternatives with the same role.
- Keep each set to one style family; mixing styles in a set is what makes a plan look random.

## Rules for the AI (for core)

- For a preset chip, apply the preset's look, then place its tagged pieces (skip a piece that does not fit).
- For a free-text request, prefer items sharing one `set:` tag with the best matching preset, then add singles from the same style.
- Never mix sets in one plan unless the request asks for it.

# Catalog schema v0 (P0-D-2)

Data fields for `app/Assets/_Project/Content/catalog.json`. Issue: #9. This is the `content` agent's **proposal** for the catalog item shape; the `core` agent owns the C# interface in `Contracts/` and may change names. The JSON is written so Unity's `JsonUtility` can read it: no nulls, no dictionaries, lists of strings and plain nested objects only.

## File shape

```json
{ "schemaVersion": 0, "units": "m", "items": [ { ... } ] }
```

All lengths are in meters. Model pivot: bottom center of the footprint. Front of the piece faces +Z.

## Item fields

Aligned with the `core` shape (`Contracts/CatalogItem.cs`, #14): `id`, `role`, `size`, `usableHeight`. The other fields are additions for `content`, the picker and the AI.

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | string | Unique, `<kind>-<style>-<nn>`, lowercase with hyphens |
| `name` | string | Display name for the picker |
| `role` | string | **Core enum, used by the fit rule:** `Table`, `Bed`, `Storage`, `Couch`, `Other` |
| `kind` | string | Detailed role for the picker and the AI: `desk`, `bed`, `closet`, `chair`, `shelf`, `sofa`, `lamp`, `plant`, `rug`, `wall-art`, `clutter-cover`. Mapping: desk to Table, bed to Bed, closet and shelf to Storage, sofa to Couch, everything else to Other |
| `zones` | string[] | `my` (MR redesign) and/or `outside` (VR, any role) |
| `size` | `{ x, y, z }` | Width along the front, height, depth, in m (same as core) |
| `usableHeight` | float | Desk top or mattress top above the floor. `0` means none. Required for `desk` and `bed` |
| `styles` | string[] | Style ids from `style-board.md`: `warm-wood`, `scandi`, `cafe`, `night-city` |
| `moods` | string[] | `morning`, `afternoon`, `evening`, `night` |
| `tags` | string[] | Free words for search and the AI |
| `primaryColor` | string | Main color, `#RRGGBB` |
| `description` | string | 60-200 characters the AI can reason about |
| `asset` | `{ path, isPlaceholder }` | Prefab path under `Assets/_Project/Art/`; `isPlaceholder: true` means the file does not exist yet |
| `license` | string | `OWN`, `CC0-1.0`, `CC-BY-3.0`, `CC-BY-4.0`, `UNITY-ASSET-STORE-EULA`, or `PLACEHOLDER`. Must match `asset-licenses.md` |
| `triBudget` | int | Maximum triangles for this asset (proposal: 5000) |

## The fit rule, in catalog terms

Decided by the human; `core` implements it in #14. Using only catalog fields, an item fits a real anchor box when:

1. It has the same `role` as the box.
2. Footprint: the anchor box is enlarged by **5 cm on each side** (`MarginPerSide`) before comparing. The item's `size.x` by `size.z` must fit inside that enlarged box, as is or rotated 90 degrees, with a **1 cm tolerance** (`Tolerance`). Width and depth are never stretched.
3. Height: the model is scaled **uniformly on Y by up to +-15%** (`MaxYScale`) for every role. `Table` and `Bed` scale so `usableHeight` lands exactly on the real desk top or mattress. Roles without a usable surface (`Storage` and others) scale so the model reaches the box top. If that needs more than +-15%, the item does not fit.

Because width and depth are not stretched, the catalog lists several sizes per kind. Outside-zone items skip these rules; only `zones` must contain `outside`.

## For the AI assistant

The assistant sees `id`, `role`, `kind`, `size.x`, `size.y`, `size.z`, `usableHeight`, `styles`, `moods`, `tags` and `description` only, and answers with ids from this file. It never invents an id and never generates 3D. The server rejects any unknown id.

## Validation (to add with `core` tests)

- Ids are unique and match the pattern.
- `kind` `desk` and `bed` have `usableHeight > 0` and `usableHeight <= size.y`.
- Every `styles` and `moods` value is from the lists above.
- `license` is not `PLACEHOLDER` for any item whose `isPlaceholder` is `false`.
- No item has more than the budgeted `triBudget` after import.

## Open questions

- `chair` has no core role; it maps to `Other` for now. Add a `Chair` role if chairs become redesignable.

## Top-level lists

`catalog.json` also holds `"styles"` and `"moods"` (the allowed ids). `core`'s loader checks every item's `styles` and `moods` against them.

## Window and door items: `attachTo`

`curtain` and `door-trim` items surround a real window or door and **never cover its opening, whatever its size**. They carry `"attachTo": "window"` or `"door"` (no fixed opening). Each prefab is a few parts that the app sizes to the real opening at runtime with `OwnSpace.Content.OpeningFrame.Fit(openingWidth, openingHeight)`:

| Item | Parts (unit cubes, centered on the root z axis) | Shape after `Fit(w, h)` |
| --- | --- | --- |
| `curtain` | `Left`, `Right`, `Rod` | Panels beside the opening, each `sideWidth` wide, from 0.10 m below the opening to 0.12 m above it; the rod spans `w + 2 * sideWidth` at the top |
| `door-trim` | `Left`, `Right`, `Top` | Side bars beside the opening, `h` tall; top bar above it, `w + 2 * sideWidth` wide |

**Pivot:** the root origin is the bottom center of the real opening, on the wall plane; +Z faces into the room. The app places the root at the window or door anchor, then calls `Fit` once (not per frame). The catalog `size` is the outer size at the reference opening (window 1.2 x 1.2 m, door 0.9 x 2.0 m); `sideWidth` and thickness come from it.

Kinds added in P2 (all `outside` zone): `armchair`, `coffee-table`, `pendant-lamp` (hangs from a cord; pivot at the bottom of the shade), `wall-art`, `curtain`, `door-trim`.

## Clutter covers (P3)

`clutter-cover` items hide things the device does not recognize (`CONCEPT.md` 5.5). Sizes follow typical dorm piles: a laundry pile or bag is about 0.4-0.6 m wide and up to 0.5 m high, shoes and books fit 0.6 x 0.4 m, and a screen covers a corner up to 1.2 m wide. Every style has a basket, a lidded box, a floor cushion, a potted plant and a three-panel folding screen (the oldest two items are `clutter-basket-warm-wood-01` and `clutter-cushions-scandi-01`). The app picks the smallest cover that is at least as large as the hand-drawn box, and shows the reminder to take care when standing up. Allowed in both zones.

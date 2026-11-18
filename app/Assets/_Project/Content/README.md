# Content

Owner: `content`. Catalog data (ScriptableObjects, JSON): furniture roles, sizes, usable heights, style tags, mood presets.

## Placeholder prefabs

`Art/Furniture/<id>/<id>.prefab` for every item in `catalog.json`, and `CatalogPrefabs.asset` (catalog id to prefab), are **generated**; do not edit them by hand. Run after any change to `catalog.json`:

```
/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath app \
  -executeMethod OwnSpace.Content.Editor.CatalogPrefabBuilder.BuildBatch -quit -logFile app/Logs/content-build.log
```

(or the menu OwnSpace > Content > Build Placeholder Prefabs). The run is idempotent and fails if a prefab's bounds differ from the catalog `size`. Pivot is the bottom center, +Z is the front. Runtime lookup: `OwnSpace.Content.ICatalogPrefabs.TryGetPrefab(id, out prefab)`, implemented by `CatalogPrefabs`.

## Mood presets

`mood-presets.json` holds the four presets (values from `docs/design/mood-presets.md`). `Editor/MoodPresetBuilder` builds `MoodPresets.asset`, read at runtime through `OwnSpace.Content.IMoodPresets.TryGet(id, out entry)`; ids match `moodPresetId` in the design state. Presets outside the safety limits are flagged `needsHeadset`, and the builder fails when the flag is missing. Rebuild with `-executeMethod OwnSpace.Content.Editor.MoodPresetBuilder.BuildBatch`. `xr` applies the values to lights and the passthrough layer.

## Finishes

`finishes.json` lists the floor and wall finishes (id, name, surface, styles, moods, pattern, colors). `Editor/FinishBuilder` turns it into materials under `Art/Finishes/` (shader `OwnSpace/Finish`: base color plus one world-space pattern, no textures) and `FinishSet.asset`, which the app loads through `OwnSpace.Content.IFinishSet.TryGet(id, out entry)`. The ids are the values of `floorFinishId` and `wallFinishId` in the design state; an empty id means "keep the real surface". Rebuild with `-executeMethod OwnSpace.Content.Editor.FinishBuilder.BuildBatch`. The shader is lit with URP's PBR function: main light, placed lamps (additional lights, per pixel) and ambient, no shadows. `FinishBuilder` fails on shader compile errors, duplicate ids and unknown surfaces or patterns.

## Lighting assets

`Editor/LightingBuilder` builds the glow material, the window-light materials per mood and `Art/Lighting/LightGlow.prefab` (`-executeMethod OwnSpace.Content.Editor.LightingBuilder.BuildBatch`). Lamp prefabs carry `LampLight` data from the catalog `light` field. Details for `xr`: `docs/design/lighting-assets.md`.

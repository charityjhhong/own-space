# Lighting assets (P3-D-2)

For `xr`: what `content` provides to render placed lamps and window light. All procedural, no downloads. Numbers are starting values; nothing is checked on a headset (`needs-headset`).

## Glow (soft light on surfaces)

- **Shader** `OwnSpace/LightGlow` (`Art/Shaders/OwnSpaceLightGlow.shader`): additive color times a radial falloff, no texture, no lighting, no depth write. It only brightens, so it never hides a real surface or object.
- **Prefab** `Art/Lighting/LightGlow.prefab`: a 1 m quad with material `LightGlow.mat` (2700 K warm). Place it on a wall or floor, turn it to face into the room, and scale it to `2 * glowRadius`. It is double-sided.
- **Properties** (per material): `_Color` (HDR), `_Intensity` (0-2), `_Power` (falloff, 2.5 default). To change color per lamp without a new material per lamp, use a `MaterialPropertyBlock`.
- **Budget:** at most about 4 glows in view, one quad each; follow the "at most 2 real-time lights" rule in `mood-presets.md`.

## Lamp light per catalog item

Lamp and pendant items carry a `light` object in `catalog.json`, and their prefab has a `OwnSpace.Content.LampLight` component with the same data (data only; the app creates the real light and glow):

| Field | Meaning |
| --- | --- |
| `kelvin` | Bulb color temperature. `0` = follow the active mood's `lampKelvin` |
| `intensity` | Base intensity, 1 = a normal lamp |
| `range` | Reach of the real-time light (m) |
| `glowRadius` | Radius of the surface glow (m) |
| `sourceHeight` | Light source height above the prefab pivot (m) |

Final color: `kelvin` (or the mood's `lampKelvin`), converted with `Mathf.CorrelatedColorTemperatureToRGB`. Final intensity: `intensity * mood.lampIntensityMultiplier`.

| Item | kelvin | intensity | range | glowRadius |
| --- | --- | --- | --- | --- |
| lamp-warm-wood-01, pendant-warm-wood-01 | mood | 1.0 | 3.0 / 3.5 | 0.6 / 0.7 |
| lamp-scandi-01 | mood | 0.9 | 3.0 | 0.6 |
| pendant-cafe-01 | 2400 | 1.1 | 3.0 | 0.6 |
| lamp-night-city-01, pendant-night-city-01 | 2200 | 0.8 | 2.5 | 0.5 |

## Window light

The color and strength per mood are in `MoodPresets` (`windowColor`, `windowIntensity`; table in `mood-presets.md`). Ready-made materials with those colors: `Art/Lighting/LightGlow_window_morning|afternoon|evening|night.mat`. Use a large glow quad (or a few) on the floor inside the real window, so light seems to fall into the room. The window light never covers the window; windows stay real.

## Rebuild

`-executeMethod OwnSpace.Content.Editor.LightingBuilder.BuildBatch` (menu: OwnSpace > Content > Build Lighting Assets). The lamp data comes from `CatalogPrefabBuilder`; the window materials read `mood-presets.json`, so rebuild after changing either.

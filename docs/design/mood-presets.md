# Mood presets (P0-D-4)

Four presets: `morning`, `afternoon`, `evening`, `night`. Issue: #11. These are **starting values**. Nothing here has been checked on a headset; the real range of passthrough color control is a "needs device" item (`CONCEPT.md` section 9). Tune on a Quest 3, then update this file.

## How a preset is applied

A preset changes three things together, so the real and virtual parts share one tone:

1. **Virtual lighting:** one directional "sun" light, ambient color and intensity, and the intensity of placed lamps.
2. **Passthrough adjustment:** brightness, contrast, saturation and a tint on the passthrough layer (`OVRPassthroughLayer`, color map or brightness/contrast/saturation controls; each range is -1 to 1, 0 is unchanged).
3. **Window light:** the color and strength of the light that falls into the virtual space from the real window position.

Blend between presets over **2.5 s** with an ease-in-out curve. No flashing and no step changes (comfort rule in the team plan).

## Values

Light units are Unity URP `Light.intensity` with `useColorTemperature = true` (Kelvin). Intensity is relative: 1.0 is the afternoon sun.

| Field | morning | afternoon | evening | night |
| --- | --- | --- | --- | --- |
| Sun color temperature (K) | 5200 | 6000 | 2800 | 8500 |
| Sun intensity | 0.9 | 1.0 | 0.7 | 0.15 |
| Sun elevation (deg above horizon) | 25 | 55 | 8 | 40 (moonlight) |
| Ambient color | `#E9EEF5` | `#F5F3EE` | `#E8B58A` | `#3A4A6B` |
| Ambient intensity | 0.9 | 1.0 | 0.7 | 0.45 |
| Placed lamps: color temperature (K) | 3000 | 3000 | 2400 | 2200 |
| Placed lamps: intensity multiplier | 0.3 | 0.2 | 1.0 | 1.4 |
| Window-light color | `#FFF1DC` | `#FFFAF0` | `#FF9F55` | `#8FA8D8` |
| Window-light intensity | 0.8 | 1.0 | 0.9 | 0.25 |
| Passthrough brightness | +0.05 | 0.00 | -0.10 | -0.30 |
| Passthrough contrast | +0.05 | 0.00 | +0.05 | +0.10 |
| Passthrough saturation | +0.05 | 0.00 | +0.05 | -0.20 |
| Passthrough tint (RGB multiplier) | `#FFF8EE` | `#FFFFFF` | `#FFD7B0` | `#B9CBF2` |

## Reading the table

- **Morning:** fresh and clean. Slightly cooler white light, a little brighter and more saturated. Good for the "study, bright and calm" case in `CONCEPT.md` 5.8.
- **Afternoon:** the neutral reference. No passthrough adjustment, so the real and virtual parts match as they do by default. All style palettes are tuned to this preset.
- **Evening:** warm and low. The sun is near the horizon, lamps carry the room, and the real parts warm up through the tint.
- **Night:** dark and cool, with warm lamps as the focus. Moonlight through the window is faint.

## Safety and comfort limits

- **Never darken passthrough below brightness -0.35.** At night the user must still see the real room, laptop and floor (Space Sense and standing up safely). Night is -0.30 for that reason. **Needs-headset:** the -0.35 floor itself is untested; confirm that the room stays safely visible at night.
- **Keep tint multipliers within about 25% of white** so text on paper and the laptop screen stay readable. **Known exceptions, needs-headset:** the evening tint `#FFD7B0` (blue channel 31% below white) and the night tint `#B9CBF2` (red channel 27% below white) exceed this rule. Check text readability on a Quest 3 and weaken them if needed.
- **No flashing:** no change faster than 2.5 s for the whole preset, and no oscillating values.
- **Hidden clutter:** the reminder to take care when standing up is shown in every preset.
- **Windows and doors stay real,** so the window-light color only lights virtual surfaces. It never tints the real window itself.

## Placed lights

- A placed lamp is a point light with range about 3 m, color from the lamp-temperature row, and intensity `base * lampMultiplier`.
- Point-light budget: **at most 2 real-time lights** in view at once. Others use a baked-looking glow sprite. (Starting value; `xr` and `build` confirm against the 72 Hz budget.)
- Over real surfaces, use a soft additive overlay (radial gradient, alpha 0.10-0.25) instead of a real light.

## Data shape (for `Content/`)

A developer can read this as one record per preset:

```json
{
  "id": "evening",
  "sunKelvin": 2800, "sunIntensity": 0.7, "sunElevationDeg": 8,
  "ambientColor": "#E8B58A", "ambientIntensity": 0.7,
  "lampKelvin": 2400, "lampIntensityMultiplier": 1.0,
  "windowColor": "#FF9F55", "windowIntensity": 0.9,
  "passthroughBrightness": -0.10, "passthroughContrast": 0.05, "passthroughSaturation": 0.05,
  "passthroughTint": "#FFD7B0",
  "blendSeconds": 2.5
}
```

The same values are in `app/Assets/_Project/Content/mood-presets.json`, built into `MoodPresets.asset` and read through `OwnSpace.Content.IMoodPresets.TryGet(id, out entry)`. Presets outside the safety limits below carry `needsHeadset: true` (evening and night today); the builder fails if a preset breaks a limit without that flag. Update the JSON and this file together.

## Open items (needs device)

- Real range and banding of passthrough brightness, contrast and saturation on Quest 3 and 3S.
- Whether a tint is possible with the color-map controls alone, or needs a LUT.
- Whether these presets hold for text readability at night.

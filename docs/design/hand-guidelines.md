# Hand interaction guidelines (draft, P1-D-3)

A short summary of Meta's official hand-interaction guidance, for `xr` and `content` when they build UI. Issue: #27. Read from Meta's pages; each number names its page. Meta's pages change, so re-check before relying on a value. **Nothing here is checked on a headset.** Anything not stated by Meta is marked *unverified*.

Sources:
- Hands UI best practices: https://developers.meta.com/horizon/design/hands-ui-best-practices/
- Hit targets: https://developers.meta.com/horizon/design/styles_inputs_hit_targets/
- Hands design overview and Interaction SDK: https://developers.meta.com/horizon/design/hands/
- Design requirements (text and hit targets, 2D apps): https://developers.meta.com/horizon/documentation/android-apps/design-requirements

## Interaction types

Meta names three core interactions: **ray casting** (distant objects), **touch** (direct contact) and **grab** (holding and moving objects). The team plan limits Own Space to pinch, grab and poke. Hands are not controllers: do not port controller interactions as they are.

## Numbers

| Item | Value | Source |
| --- | --- | --- |
| Minimum hit target | 48 dp at panel scale (the invisible hit area, not the visible button) | Hands UI best practices |
| Primary controls with hand tracking | 60 x 60 dp | Hit targets |
| Visual padding inside the hit target | about 4-6 dp | Hands UI best practices |
| Physical size of a 48 dp target at touch distance | about 22 mm | Hands UI best practices |
| Minimum angular size of a target | about 2.5-3 degrees, for touch and ray | Hands UI best practices |
| Minimum space between adjacent targets | 12 mm | Hands UI best practices |
| Best distance for touch UI | 42-46 cm from the user | Hands UI best practices |
| Button press travel | 7 mm by default | Hands UI best practices |
| Comfortable distance for ray (indirect) UI | about 0.8-3 m; not closer than 0.8 m | Hands UI best practices |
| Avoid this middle distance | about 0.5-0.8 m: put panels under about 46 cm or at 1 m or more | Hands UI best practices |
| Text | at least 14 px to read, 18 px or more for comfort (2D-app page; lighter weights need larger text) | Design requirements |

## Rules to follow

- Keep direct-touch UI within the seated 60 cm reach from the team plan, near the 42-46 cm touch distance. Far UI uses ray and pinch, at 1 m or more.
- A UI that moves in depth keeps its **angular** size, not its physical size.
- Keep the user's arms close to the body and elbows near the hips; put the most-used controls closest. Avoid frequent long reaches.
- Give immediate visual and audio feedback on hover, select and confirm (hands have no vibration).
- Design for the least precise input. Hit targets must not overlap.
- Hand tracking struggles when one hand covers the other; do not place hand UI where this is likely.
- Offer more than one input where possible, and keep accessibility in mind (one-handed and left-handed modes are in the team plan).

## Unverified or not stated by Meta

- **Text size for 3D UI in meters or degrees:** Meta's hands pages give no number. The 14/18 px figures come from the 2D-app page and may not apply. *Unverified:* start with about 1.5 degrees of visual angle for body text, then test on a Quest 3.
- **Panel dimensions:** not stated.
- **Tracking-loss timing:** the pages mention brief gaps and occlusion but no durations. Our own rule (cancel safely on loss) is in the team plan.
- **dp-to-pixel mapping on a panel:** depends on the panel's pixels per meter; verify in the Meta Horizon OS UI Set.
- Meta recommends its **Horizon OS UI Set**, which ships pre-sized hit targets and hit slop. Check it before designing custom controls.

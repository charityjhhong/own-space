# Devpost submission

Standard Devpost sections, English. Items marked **[confirm]** are facts to check on the device or on the form before submitting. Everything described is in the build on `main`.

- **Project name:** Own Space
- **Track:** Productivity **[confirm on the form]**
- **Division:** New Experience **[confirm on the form]**
- **Platform:** Meta Quest 3 / 3S, Unity 6, hand tracking and passthrough **[confirm devices tested]**

## Tagline (limit 200 characters)

Keep Own Space running as your background room, then watch YouTube or work on your real laptop in it, with your real desk still in reach.

## Inspiration

Where we work shapes how we work, yet most people cannot change their room: they share it, rent it, or the walls are not theirs. We wanted a room you can design without touching it, and keep as the background for everything else you do on a headset: working, watching, resting. The real desk and laptop should stay in reach.

## What it does

Own Space is a seated mixed reality app for Meta Quest. Its core use: **keep Own Space running as your background room, open the Meta menu, and watch YouTube or work on your real laptop through Meta's virtual display, while your real desk stays in reach.** The app steps back by itself when the Meta menu or a Meta window has the input, and the room stays as you left it.

How the room is made:

- **Room scan and my zone.** It scans your room and keeps one part real: your desk, your laptop and your hands. Around it, a virtual room appears at the real scanned size, or wider with **Bigger room**.
- **My room.** Give your real desk or bed a new design that keeps its usable height. **Keep real top** shows your real desk top, and your laptop, through the design.
- **One finish for the whole room.** One floor and one wall finish, plus baseboards, crown molding and a warm wash from lamps on nearby walls.
- **Moods.** Morning, day, evening or night, tone-matched to the real room so the virtual and real parts sit together.
- **Add.** Place furniture and decor by hand, with suggested spots on the floor.
- **AI design.** Type a request or pick a style. It places catalog pieces only, one per kind, skips kinds you already placed, never places curtains, and every result can be undone.
- **Tidy up.** Cover clutter with a basket, plant, cushion, box or screen.
- **Focus mode.** One button hides the tools and leaves the room.
- **Saved spaces.** Save your room once and open it anywhere. It is your scanned geometry and your design, rebuilt around you, **not a photo**.
- **First-run guide.** A ghost hand points at the next thing to pinch **[confirm: only the shipped hints, not the full scan-to-tabs walkthrough]**.

Everything works with hands only, at 72 Hz on Quest 3 and 3S **[confirm measured]**. Contact shadows ground each piece on the floor.

## How we built it

Unity 6 with the Meta XR SDK: passthrough, the Mixed Reality Utility Kit for the room layout, and hand tracking. The rules live in plain C# with tests: the zone, the design state and saving, and the fit rule (same role, same usable height). Content is a data catalog of furniture, finishes and moods. Most models and textures are CC0 (Poly Haven, Kenney, ambientCG) and the rest are our own shapes **[confirm final assets]**. Our own shaders keep the budget low: no blur, no extra passes, a tone match to passthrough. The AI calls Gemini through a small Worker that holds the key, checks the answer and rejects anything outside the catalog. Saved spaces stay on the headset.

## Challenges we ran into

- **Hand-ray accuracy and stability.** A ray from a tracked hand jitters and drifts, so small targets were hard to hit. We gate the ray on tracking confidence, enlarged the hit areas, and cancel an action safely when tracking drops.
- **A floor that vanished.** A floor mesh wound the wrong way was culled from some angles; the cause was the mesh winding, not the lighting.
- **Meta system UI over the app.** The Meta menu and Meta windows take the input while the app keeps rendering, so the app has to hide its tools, keep its state and come back cleanly.
- **Overlapping room scans.** More than one scan of the same room confused the layout, so the room layout now has to settle on one scan **[confirm wording with core]**.

## Accomplishments that we're proud of

- A real working scene: your real laptop and desk inside a room you designed, with YouTube beside it.
- Redesigning a real desk without moving the laptop on it.
- Staying within a Quest frame budget with grounded shadows, moods and a full room shell.
- An AI that can only place what exists in the catalog, and can always be undone.

## What we learned

Mixed reality is judged by the real objects in view: heights, edges and light must agree with the room, or the illusion breaks at once. Small, visible rules (usable height, one finish, one piece per kind) beat clever generation. Hands-only input needs generous targets and calm feedback.

## What's next

Automatic clutter detection, bunk beds, sound, and sharing a design or a saved space with a roommate.

## Built with

Unity 6, C#, Meta XR SDK, Mixed Reality Utility Kit, hand tracking, passthrough, Universal Render Pipeline, Meta Quest 3, Meta Quest 3S, Gemini API, Cloudflare Workers, Poly Haven, ambientCG, Kenney, Inter.

## Short description (500 words or fewer, for a form field with that limit)

### Inspiration

Where we work shapes how we work, yet most people cannot change their room: they share it, rent it, or the walls are not theirs. We wanted a room you can design without touching it, and keep as the background for everything you do on a headset. The real desk and laptop stay in reach.

### What it does

Own Space is a seated mixed reality app for Meta Quest. Its core use: **keep Own Space running as your background room, open the Meta menu, and watch YouTube or work on your real laptop through Meta's virtual display, while your real desk stays in reach.** The app steps back by itself when a Meta window has the input, and the room stays as you left it.

It scans your room and keeps one part real, **my zone**: your desk, laptop and hands. Around it, a virtual room appears at the real scanned size, or wider with **Bigger room**. **My room** gives your real desk or bed a new design that keeps its usable height; **Keep real top** shows your real desk top and laptop through it. One floor and one wall finish cover the whole room, with baseboards, crown molding and warm lamp light on nearby walls. Moods (morning, day, evening, night) are tone-matched to the real room. **Add** places furniture with suggested spots. The **AI** places catalog pieces only, one per kind, skips kinds you placed, never places curtains, and can be undone. **Tidy up** covers clutter. **Focus mode** hides the tools. **Saved spaces** keep your scanned room and design to open anywhere, not a photo. It works with hands only, at 72 Hz on Quest 3 and 3S **[confirm]**.

### How we built it

Unity 6 with the Meta XR SDK: passthrough, the Mixed Reality Utility Kit for the room layout, and hand tracking. The rules live in plain C# with tests: the zone, the design state and saving, and the fit rule (same role, same usable height). Content is a data catalog; most models and textures are CC0 (Poly Haven, Kenney, ambientCG) **[confirm]**. Our shaders add no extra passes. The AI calls Gemini through a small Worker that holds the key and rejects anything outside the catalog. Saved spaces stay on the headset.

### Challenges

Hand-ray accuracy and stability, a floor mesh wound the wrong way and culled, Meta system UI taking the input over the app, and overlapping room scans.

### What's next

Automatic clutter detection, bunk beds, sound, and sharing a design or saved space with a roommate.

## Hand interaction (limit and format: confirm on the Devpost form)

Own Space is hands only; no controller is needed. (The Meta menu, YouTube and the laptop display are Meta's own windows, used with the same hands.)

- **Pinch** to pick a card, press a button, select a real piece or confirm a choice. The far panel uses a hand ray and a pinch.
- **Poke** the panel with a finger when it is within arm's reach.
- **Pinch and drag** to move the panel by its top handle, to draw a box in Tidy up, to move the zone corners, and to place a piece on the floor.
- Each action gives a visual response (highlight, small animation), since hands cannot vibrate. If tracking is lost, the action in progress is cancelled safely **[confirm]**.

## Privacy note (outside the description; for the form's privacy field or the store listing)

**Own Space does not use the microphone** and asks for no audio permission **[confirm: the final APK manifest has no microphone permission]**. Saved spaces stay on the headset and are never uploaded.

The AI sends only the request you typed or picked (as text), the room geometry (floor and zone outlines, door, window and furniture boxes) and catalog, finish and mood ids, through our Cloudflare Worker to Gemini. **No camera images, no names and no account data are sent.** On Gemini's free tier, Google may use submitted content to improve its products and humans may review it, so do not put private information in a request **[confirm: tier in use]**.

## Credits and licenses (details in `docs/design/asset-licenses.md`)

- Models and textures: CC0 from Poly Haven, Kenney and ambientCG; the others are our own generated shapes.
- UI font: Inter 4.1, SIL Open Font License 1.1 (`app/Assets/_Project/Art/Fonts/Inter-LICENSE.txt`).
- Store art and UI mockups are our own work.

## Screenshots

Three to five headset captures, 16:9, no added text: see `docs/demo/screenshot-plan.md`. Save under `submission/screenshots/`. Store art is in `submission/art/`.

## What we do not claim

The Meta menu, YouTube and the laptop virtual display are Meta's system features, not ours; we only keep Own Space running behind them. No Real occlusion (a hand or an object never cuts the virtual room), no voice or microphone, no AI-generated 3D models, no automatic clutter detection, no curtains in AI plans. Saved spaces are not photos.

## Facts to confirm before submitting

1. Devices tested, and the frame rate measured on Quest 3 and 3S (target 72 Hz).
2. Every feature named above is in the submitted build, and the Meta menu windows float over Own Space as described.
3. Final model and texture sources and licenses.
4. How the AI behaves offline, and which Gemini tier is used.
5. Video link (`docs/demo/video-shotlist.md`), repository link and Invite URL.
6. The name check on the Meta store and Devpost (`CONCEPT.md` section 10).

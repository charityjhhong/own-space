# Device day: 60 minutes with the Quest

Goal: in one hour, learn what the simulator could not tell us, and record it. Everything here is `needs-headset`. Do the blocks in order; if time runs out, drop from the bottom of block 6, never block 2 (the rules need a working install from the Invite URL).

Sources: `needs-headset` items from merged PRs (#39, #43, #46, #47, #53, #59, #66, #69, #74, #78 and the P3/P4 hand-ray, AI, hide and onboarding PRs), and `CONCEPT.md` section 9. Commands: `.github/scripts/install-quest.sh`, `docs/qa/perf-budget.md`, `docs/release/dashboard-upload.md`.

## Before the day (at the desk, no headset)

- [ ] The newest **release-signed** build is on the `Competition` channel (`docs/release/CHANGELOG.md` has its row) and the Invite URL opens in a browser. Today the channel holds 0.0.1 (code 1, f419063).
- [ ] A **second Meta account** (not in the organization) can log in on the headset. Know its password and 2FA. **verify** how long an account switch takes.
- [ ] Headset charged above 80 %, USB-C **data** cable, developer mode on (Meta Horizon phone app), OVR Metrics Tool installed, `adb devices` works.
- [ ] A **development** APK is built and ready: Build it with `OWNSPACE_DEVELOPMENT=1 OWNSPACE_VERSION=0.0.0 OWNSPACE_VERSION_CODE=1 .github/scripts/build-android.sh` before the day and bring the APK (`install-quest.sh` also builds one, but only with the headset connected). It carries the PerfOverlay; the Competition build does not.
- [ ] Cast or capture setup for the headset view, a notes file open, a real laptop, a printed page and a book.
- [ ] The Worker answers: `curl -s -o /dev/null -w "%{http_code}" https://ownspace-assistant.charityjhhong.workers.dev` returns a code (not a timeout). **verify** the expected code for GET.

## Timeline

| Min | Block |
| --- | --- |
| 0-5 | 1. Setup |
| 5-15 | 2. Install from the Competition Invite URL (clean account, no sideload), cold start |
| 15-25 | 3. Space Setup in the dorm room |
| 25-47 | 4. Flows: first run, hand-ray aim, zone, designs, hide, moods, Ask |
| 47-55 | 5. Performance with the development build (72 Hz) |
| 55-60 | 6. Tracking loss, pause, recenter, then footage for the video |

## 1. Setup (0-5)

1. Put the headset on once, accept **Allow USB debugging** (tick always allow). `adb devices` shows `device`.
2. Start OVR Metrics Tool, enable the overlay and CSV recording. **verify** menu names.

## 2. Install from the Invite URL (5-15)

This is the submission path, so it goes first and uses no sideloading.

1. Switch to the second account. Open the Invite URL (headset browser, or phone then open on the headset), accept the invite.
2. Install from the library, launch. Cold start three times (force-stop between), stopwatch from icon to first working interaction; target under 5 s.
3. The first launch must show the **onboarding** (row K1). If a sideloaded copy exists, uninstall it first.
4. Capture: install page and first-screen screenshots, build number against `docs/release/CHANGELOG.md`.

## 3. Space Setup (15-25)

Real dorm room, usual lights.

1. Run Space Setup: walls, desk, bed, closet, chair. Label them correctly; note what cannot be labelled.
2. Capture: a photo of the room and the room map screenshot.
3. Launch the app with the room scanned and check the log: `adb logcat -d -s Unity | grep "OwnSpace.MR"` must show `[OwnSpace.MR] Room read: scan=True` with plausible wall, window, door and furniture counts (row A1). The Invite build is a release build and logs warnings and errors only (`Diagnostics/ReleaseLogLevel.cs`), so this line is **not** there: run this check with the development build in block 5.

## 4. Flows (25-47)

Use the Invite build for the K, G, C, D, H, I, J and L rows. Re-run onboarding at any time with `adb shell pm clear com.charityjhhong.ownspace` (erases saved design). Mark each row in the table.

**Simulator vs device aiming:** in the XR Simulator, aiming follows the head/mouse; on the device the far pick uses the **pinching hand's pointer ray**. Everything aiming-related (G rows) must be judged on the device, not by the simulator experience.

**New tool panel UI:** the placeholder orbs are being replaced by one designed tool panel. When it has merged, add one row per tool in the table (reach, legibility, text size per `docs/design/hand-guidelines.md`) and re-check the K1 onboarding against it.

For **Ask** (rows L1-L4): live means the Worker at `https://ownspace-assistant.charityjhhong.workers.dev` answers (about 2 s in tests, 6 s budget in the proxy). For the offline test switch Wi-Fi off in the headset, ask again, then switch it back on.

## 5. Performance (47-55)

1. Reinstall as the development build: `.github/scripts/install-quest.sh --apk <dev apk> --reinstall` (a different signing key, so it erases data).
2. Fill the outside zone with CC0 models, mood = evening (heaviest case). Play 5 minutes with OVR Metrics recording and the `DiagnosticsRoot` overlay on.
3. Pass: 72 FPS steady, CPU and GPU each under 13.9 ms, GC row 0 B. How to read each number: `docs/qa/perf-budget.md`. Capture the CSV and an overlay screenshot.

## 6. Tracking loss, pause, recenter, video footage (55-60)

1. Rows B2, F3, F4, F5 below (hands out of view mid-drag, system boundary, Home button / pause / long-press recenter, no-scan path).
2. Record the footage list at the end of this file if there is any time left; it matters more than finishing every row.

## Results table

Result: P = pass, F = fail, S = skipped. Capture = what to save to a shared folder named `device-day-<date>`.

| # | Check | Pass when | Result | Capture |
| --- | --- | --- | --- | --- |
| A1 | Real scan read (#43) | Walls, windows, doors, furniture listed; counts plausible | | logcat line, room photo |
| A2 | Furniture types, chairs (CONCEPT 9) | Table, bed, storage, couch recognised; note chairs | | list of labels per object |
| A3 | Box vs real furniture error (CONCEPT 9) | Measure 3 pieces with a tape: error in cm per side | | table of cm |
| B1 | Pinch on real hands (#46, #78) | Pinch selects reliably; no false pinches over 2 min | | screen recording |
| B2 | Hand tracking lost mid-action (#46) | Drag, zone corner move and Ask apply all cancel safely when hands leave view; nothing half-placed | | recording |
| B3 | Reach and comfort | Zone corners and UI reachable seated | | notes |
| C1 | Propose, adjust, save my zone (#39, #47) | Proposed zone fits the dorm; corners movable; saved | | screenshot |
| C2 | Relaunch restores the zone and design | State identical after force-stop and launch | | before/after |
| D1 | Outside shell (#66) | Beige floor and walls outside zone; my zone stays real | | screenshots |
| D2 | Seam where real and virtual floors meet (CONCEPT 9) | Soft gradient, no hard line or flicker | | close-up screenshot |
| D3 | Windows and doors stay open (#66, #75) | Holes line up with the real window and door | | screenshot per opening |
| D4 | Desk usable-height alignment (#59) | Virtual desk top within 1 cm of the real top | | tape-measure photo |
| D5 | Bed usable-height alignment (#59) | Virtual mattress top within 1 cm of the real one | | tape-measure photo |
| D6 | Real desk visible inside my zone (#66) | Laptop on the real desk is never covered | | screenshot |
| D7 | Outside placement by hand (#69) | Place, move, rotate, remove a piece; stays put | | recording |
| E1 | Mood tints (#74, CONCEPT 9) | Morning, afternoon, evening, night each visibly different, smooth over 1.5 s | | 4 screenshots; note range |
| E2 | Warm light on real surfaces | Lamp/window light looks plausible, no banding | | screenshots |
| F1 | Hands over virtual furniture (Depth API, CONCEPT 9) | Hands stay visible, not hidden behind virtual pieces | | recording |
| F2 | Laptop and printed text through passthrough (CONCEPT 9) | Laptop screen and a printed page readable for 5 min in each mood | | notes, photo |
| F3 | System boundary and Space Sense (CONCEPT 9) | Boundary never hidden by the shell; appears when you walk toward the edge | | recording |
| F4 | Pause, Home button, resume, recenter (long-press) | App resumes with design intact; recenter keeps alignment | | notes |
| F5 | No scan path | With no room scan the user is guided to Space Setup | | screenshot |
| F6 | AI response time (CONCEPT 9) | Covered by L1-L3 | | see L rows |
| P1 | 72 FPS, 5 min (budget) | OVR Metrics minimum at or above 72 FPS | | CSV, overlay screenshot |
| P2 | CPU/GPU frame time (#53) | Both under 13.9 ms; overlay numbers match OVR Metrics | | screenshot |
| P3 | GC allocation (budget) | Overlay GC row stays 0 B | | screenshot |
| P4 | Cold start (budget) | Under 5 s, average of 3 | | three times |
| P5 | Lighting budget, baked vs real-time (CONCEPT 9) | Fully furnished room holds 72 FPS | | CSV |
| I1 | Install from the Competition Invite URL, clean account | Installs and starts without sideloading | | screenshots |
| I2 | Play one redesign on the invited build | Completes without crash | | recording |
| K1 | Onboarding on first run | Ghost hand/steps appear, first redesign within one minute, minimal text; skipping works | | recording, stopwatch |
| G1 | Hand-ray aim (far pick) | The ray from the pinching hand hits what you point at, at 1, 2 and 3 m; no jitter that blocks a pick | | recording |
| G2 | Aim near vs far | Near pieces and far boxes both selectable seated within 60 cm reach | | notes |
| J1 | Hide tool | Hide a real object (box) and bring it back; hide cube reachable; Done faces the head | | recording |
| H1 | CC0 models in the room | The CC0 models (count: **verify** in `Art/Models`) look right next to real objects: scale, material, no black/pink surfaces, no z-fighting | | screenshots per model type |
| L1 | Ask, live Worker | Orb, intent chips, one-step apply with rationale; answer within about 6 s; applied layout fits | | recording, stopwatch |
| L2 | Ask, offline fallback | Wi-Fi off: fallback recommendation appears, never blocks, message is clear | | recording |
| L3 | Ask, bad/empty answer | A request that returns nothing useful leaves the room unchanged; undo works | | notes |
| L4 | Ask, after relaunch | Orb works after pause/resume and after force-stop | | notes |

## What to capture for the video

Film from the headset capture (and a second camera on the room if a person helps). Three minutes maximum overall, so shoot short clips.

- [ ] First 15 seconds: the real desk becomes a new design while the **laptop stays put** (the core moment). Two takes.
- [ ] Hand-ray aim: pointing from a distance and selecting a piece.
- [ ] The outside zone filling with CC0 models; slow head turn so the seam and the open window/door show.
- [ ] Mood change morning to evening to night, real light and passthrough tint together.
- [ ] Ask: tap the orb, choose an intent, the layout appears, undo.
- [ ] Daily life: working on the laptop, reading a book, resting in the new space (readability shown on screen).
- [ ] Wide shot of the real two-person dorm room with the user wearing the headset.
- [ ] Clean audio of a short voice-over take or room sound; no music with unclear rights.
- [ ] Record on the headset itself (not the simulator); keep raw files, do not rely on AI-generated footage.

## Afterwards (same evening)

- Copy the capture folder somewhere safe; file one GitHub issue per failed row, labelled with the owning lane and `needs-headset`.
- Update the results table in `docs/qa/perf-budget.md` with the P1–P4 numbers.
- Answer the CONCEPT 9 items in `docs/design/tech-checks.md` (**verify** that file exists by then).
- Post a 10-line summary on #1: passed, failed, blockers for the next slot.

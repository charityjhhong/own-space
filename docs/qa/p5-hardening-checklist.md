# P5 hardening checklist (on the headset)

Source: "P5 Feature freeze and hardening" in the team plan. Everything here is `needs-headset`: the simulator never counts. Run it on the **release build from the Invite URL** (the one that will be submitted), except H4 (needs the development build for the overlay). Perf commands and budgets are in `docs/qa/perf-budget.md`; the general device-day flow is in `docs/qa/device-day.md`.

Record each row as **PASS**, **FAIL** or **N/A** with the build (version, commit) and a one-line note. Any FAIL is a bug issue labelled `needs-headset` and `phase:p5`; after the freeze only bug fixes land.

Setup: headset above 80 %, controllers **powered off and out of reach** (see H1), Guardian boundary set, the second Meta account signed in, OVR Metrics Tool installed.

| ID | Check | How to test | Pass | Fail |
| --- | --- | --- | --- | --- |
| H1 | No controller, start to finish | Remove the controllers from the room (or take out the batteries). Cold start the app, then play the whole demo flow in `CONCEPT.md`: onboarding, Space Setup, zone, a redesign, a mood, Ask, save and reload. Never touch a controller, never need one to dismiss a system prompt of ours. | Every step works with hands only; no screen says "use a controller". | Any step needs a controller, or the app stalls waiting for one. |
| H2 | Seated, 60 cm reach | Sit in a chair with arms against the body (airplane-seat posture). From the seat, do every interaction in the tool panel and on furniture: open each tab, place, move, rotate, scale, delete, undo, pinch-pick far objects. Do not lean forward past a normal seated posture. | Everything reachable within a 60 cm radius of the shoulders; no tool needs standing or arm stretch. | Any control only reachable by standing, leaning, or stretching the arm fully. |
| H3 | Hand tracking loss | While dragging a piece, move the hand out of view, then cover it. Repeat while resizing and while a tool panel button is half pressed. | The action cancels safely, nothing jumps, the "bring your hands forward" prompt shows, saved data is unchanged. | Object left in a wrong place, saved layout changed, stuck "held" state, or no prompt. |
| H4 | 30 minutes at 72 fps | Use a **development** build (`OWNSPACE_DEVELOPMENT=1`, see `device-day.md`) so the overlay shows. Start OVR Metrics CSV recording. Play continuously for 30 min in the worst scene (the zone full of objects, a mood on, outside shell up). Before and after run `adb shell dumpsys meminfo com.charityjhhong.ownspace`. During the run: `adb logcat -s Unity ActivityManager`. | At least 72 fps (judges' minimum is 60), CPU and GPU under 13.9 ms in the overlay, no thermal warning toast, no crash, no memory growth beyond a few percent. | Any dropped-below-60 stretch, a thermal warning, a crash, or steady memory growth. |
| H5 | Unfamiliar room | Take the headset to a room you have not scanned (a different room than the dorm). Run Space Setup, then redesign one piece and fill the zone. | Space Setup completes or guides clearly; the MR redesign keeps usable heights. | Setup dead-ends, the app places things through walls, or errors out. |
| H6 | Dark room | Turn the lights off to a dim level with only a screen on. Repeat a short flow: Space Setup if needed, a redesign, hand interaction. | Either it works, or tracking loss shows the prompt (H3 behaviour); nothing wrong is saved. | Crash, silent wrong placement, or hands misread and change saved data. |
| H7 | No scan | In Settings, clear the room data for the app (or use a fresh account). Launch. | The user is guided to Space Setup; no empty or broken scene. | Blank world, an exception, or no way forward. |
| H8 | Pause, Home, resume | In the middle of an interaction press the Meta/Home button, wait 10 s, resume. Then open the system menu, take the headset off 30 s, put it back on. Also let the proximity sensor sleep and wake it. | The app resumes where it was; held objects are released, tracking comes back, audio resumes; no duplicate scene, no crash. | Crash, frozen frame, lost or duplicated design, or stuck interaction. |
| H9 | Real windows, doors and desk things never covered | Place a large virtual wall, bookshelf and rug near and across a real window, a door and the real desk with a laptop on it. Try to push finishes and objects over them. | The window, door and desk objects always stay visible (passthrough cutouts or blocked placement). | Anything virtual hides a window, a door or the real laptop and desk items. |
| H10 | Boundary never hidden | Walk to the edge of the Guardian boundary with the outside zone (full VR) and a full-height wall finish active. | The Guardian boundary appears as normal, nothing of ours hides it. | The boundary is covered or the system warning is suppressed. |
| H11 | Policy, minimal permissions | On the headset open the app's Permissions page (Settings, Apps). Compare with the manifest check in `docs/release/final-release-steps.md`. | Only the expected permissions are listed and each is explained by a feature. | An unexpected permission, or a permission prompt with no reason. |

## Notes

- Do H1 through H3 first: they are hard requirements. If one fails, stop and report; do not spend the 30 minutes of H4 on a build with a known blocker.
- Take a screen recording or screenshot for any FAIL, and note the time so the OVR Metrics CSV can be matched.
- The tool-panel rows from `device-day.md` (reach, legibility) count toward H2.
- Thermals: if the headset gets warm before 30 minutes, note the minute; a warning toast is an automatic FAIL for H4.

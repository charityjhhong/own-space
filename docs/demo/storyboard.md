# Demo video storyboard (P3-D-3)

**Superseded for filming by `video-shotlist.md` (current app, exact taps, times in PT).** This file stays as the original idea.

Under 3 minutes: planned length 2:55 (the last shot ends at 2:55; the hard limit is 3:00), public on YouTube, English captions. Follows `CONCEPT.md` section 6. Rules from the team plan P6: filmed in a real two-person dorm room, recorded on a real device, **the core moment in the first 15 seconds** (a real desk becomes a new design while the laptop stays put), shows hand interaction, the outside zone filling, the mood change and daily life, and uses no AI-generated video.

Source key: **Headset** = recorded on a real Quest (mixed reality capture); **Phone** = a normal camera in the room (a person wearing the headset, hands visible); **Sim OK** = the XR Simulator may stand in if the device is not ready (fallback only). Anything marked Headset is `needs-headset`. All on-screen text is short and English.

## Shots

The tool panel is the glass panel in front of the user (tabs Style, My room, Add, AI, Tidy up; `docs/design/ui-spec.md`). It stays small and is minimized when it is not needed.

| Time | Shot | On-screen text | Source |
| --- | --- | --- | --- |
| 0:00-0:03 | Passthrough view of the real desk: a laptop open on a plain desk, hands on the keyboard | none | Headset |
| 0:03-0:10 | The hand pinches the desk's box, picks a new design; the desk changes to oak. The laptop, keyboard and mouse stay exactly where they were, seen as they really are | "Same desk. Same height. New design." | Headset |
| 0:10-0:15 | The hand taps the laptop keys on the new desk top (usable height kept) | none | Headset |
| 0:15-0:22 | Title card over the room: Own Space, then the tagline | "Own Space" / "Redesign your real room with your hands, and keep living in it." | Edit |
| 0:22-0:30 | **Before:** wide shot of the real two-person dorm room, the roommate's side and your side; a person puts on the headset | "A shared room is hard to change." | Phone |
| 0:30-0:42 | Zone setup: the app proposes a boundary, the hand adjusts the corners and confirms. Your desk, chair and bed side is marked | "Mark your zone." | Headset |
| 0:42-1:00 | The glass panel opens on Furniture: select the desk box, thumbnails of designs that fit; pick one. Then the bed and closet the same way. A brief look at the Style tab: floor and wall finishes | "Same role. Same usable height." | Headset |
| 1:00-1:12 | Tidy up: laundry on the floor; pinch and drag a box over it, the panel moves to "Pick a look" by itself, choose the basket; the laundry is covered. A small reminder appears | "Cover what you don't want to see." / "Take care when standing up." | Headset |
| 1:12-1:32 | AI Designer, **typed request**: tap "Describe your space…", the system keyboard opens, type "a cozy living room" (a time-lapse of the typing is fine); the plan card quotes the request with the AI's explanation, and a sofa, bookshelves and lamps appear in the roommate's blank virtual space. Undo plan is visible | "Type it. The AI furnishes the room." | Headset |
| 1:32-1:40 | Style tab, Mood: choose evening; virtual lights and the real parts of the room warm up together | "Evening" | Headset |
| 1:40-1:47 | The hand pinches minimize: the panel becomes a small pill and the room is clear; pinch the pill to open it again | "Minimize it. The room is yours." | Headset |
| 1:47-2:00 | Daily life: sit at the new desk, open the laptop, work; the laptop screen and a notebook stay real | "Keep living in it." | Headset + Phone |
| 2:00-2:06 | Switch to night; lean back on the new bed to rest | "Night" | Headset |
| 2:06-2:16 | **Saved spaces, part 1:** Settings › My space › Spaces › Save this room; pinch the "Home" chip. The toast says "Saved as Home" | "Save your room." | Headset |
| 2:16-2:34 | **Saved spaces, part 2:** a different place (another desk, such as a hotel table or a friend's desk). Open Spaces, pinch "Home · Nov 9": the saved room is rebuilt around you, with your designs, and its desk lands on the real desk in front of you. Say it plainly: a saved room, not a photo | "Go back to it from anywhere." / "Your scanned room and your designs, not a photo." | Headset |
| 2:34-2:43 | **After:** the same wide dorm shot as 0:22, split screen: the real room on one side, the redesigned room (headset view) on the other | "Before" / "After" | Phone + Headset |
| 2:43-2:55 | Closing: three short lines and the name | "Hands only. Your room, your design." / "Own Space" | Edit |

Total 2:55 (the hard limit is 3:00).

## Fallback shots if a feature is not built

The video must show only what is in the submitted build. Times stay the same.

| Time | If this is missing | Fallback shot | On-screen text | Source |
| --- | --- | --- | --- | --- |
| 1:12-1:32 | The keyboard request is slow or fails | Tap the "Living room" quick pick under "Or pick a style"; the plan card and the furniture appear the same way | "Pick a style. The AI furnishes the room." | Headset |
| 1:12-1:32 | The AI Designer is not merged | In the blank outside zone, place a sofa, shelves and a lamp by hand from the Furniture tab | "Furnish it freely." | Headset |
| 0:42-1:00 | The Furniture tab is not merged | Select the desk, pick a design from the older style menu; the desk changes | "Same role. Same usable height." | Headset |

| 2:06-2:34 | There is no second place to film | Use the built-in sample room: Settings › My space › Spaces › Sample room; the room is built around you with its desk in front of you | "A saved room, anywhere." | Headset |
| 2:06-2:34 | Saved spaces is not in the submitted build | Cut the beat and give its 28 seconds to the daily-life shots (2:00-2:34 stays real use of the new desk and bed); remove the Saved spaces lines from the Devpost text | "Keep living in it." | Headset |

The first 15 seconds do not depend on the panel, the AI or Saved spaces.

## Must be real headset, may be simulator

- **Must be real (Headset):** shots 0:00-0:15 (the core moment: real desk height and the laptop through passthrough), the Saved spaces visit at a second place, the mood change, the panel minimize, the before/after and the daily-life shots. These are what the judges check, and a simulator cannot show real passthrough or real heights.
- **Simulator is acceptable as a fallback:** zone setup, the design list, Tidy up, the AI Designer request UI and the Spaces sheet (UI behavior only; the saved room around you must be real). Label such clips internally; use none in the first 15 seconds.
- **Phone camera only:** the wide room shot and any shot of the person wearing the headset.

## Filming checklist

- Same dorm room for every shot except the Saved spaces visit (2:16-2:34), which is filmed at a second place; tidy except the laundry that is hidden on purpose. The desk's real height equals the redesigned height (check once with a tape measure).
- Both people in the room agree to be filmed; no private information visible on the laptop screen.
- Hands visible in every headset shot; record with the controllers never in view.
- Record the headset view at the highest quality the device offers, with the same Quest and the same build for all shots. Note the build version for each take.
- Captions burned in or as a subtitle track. No narration is required; if music is used, it must be licensed or CC0 (record the license in `docs/design/asset-licenses.md`).
- Do not use AI-generated video or images.
- Record the typed AI request twice; the AI answer needs a network, so film with the Wi-Fi checked **[confirm on device]**. Save "Home" in the dorm before filming the visit, and check that the second place has a desk or table about the same height.
- Film the "before" wide shot and the "after" wide shot from exactly the same spot (a tripod mark on the floor).

## Open questions

- Is a voice-over wanted, or captions only? (Sound is out of scope for the app, not for the video.)

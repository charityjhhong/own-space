# Concept

**Own Space.** Repository and code name: `ownspace`.

## 1. Background

The spaces we live in shape how we feel. A room we like helps us focus and helps us relax. But changing a room costs money and time, and many people can't change theirs at all: they share it, or the rules don't allow it.

This project uses mixed reality to let people redesign their room into the space they want, without touching the real one. The redesigned room is not a scene to look at. People keep living in it: they work on a laptop, study and rest there, just as they would in their own room.

## 2. Product definition

A Quest app that recognizes your real room and redesigns it in two ways:
- **The area you use (MR):** furniture keeps its place and only its design changes.
- **The area you don't use (VR):** it is replaced with a virtual space you furnish freely.

The result is a better space in which you carry on your day: working, studying and resting.

- **Track:** Productivity (wellbeing and focus)
- **Division:** New Experience
- **Special award to aim for:** Best Agentic Interaction

## 3. Target users

The app works best at the scale of a single room. Its primary users are people who spend much of their day in one room: students in dormitories, people living in studios, and people in shared houses.

## 4. Two zones

The app treats the room as two zones.

| | My zone | Outside zone |
| --- | --- | --- |
| What it is | The space you actually use (usually around the desk, chair and bed) | The space you don't use (for example, a roommate's side) |
| How it is shown | MR: virtual designs over the real furniture | VR: the real room is covered and replaced with a virtual space |
| What can change | The design only. Each piece keeps its role and its usable height. | Anything. Furniture of any role can go anywhere. |

The reason is practical. Furniture you sit at and use has to match reality in position and height. Space you don't use doesn't, so it can be designed freely.

## 5. Features

### 5.1 Zone setup
- On first launch, the user marks their zone.
- The app proposes a boundary from the recognized room layout; the user adjusts it by hand and confirms.
- The zone is saved and restored on later launches.

### 5.2 Redesigning furniture in my zone (MR)
- In edit mode, real furniture is shown as bounding boxes.
- Selecting a box lists designs of the same role that fit its size.
- Usable surfaces keep their height (desk top, bed). Only color, material and form change.
- So that the edges of the real furniture don't show, the virtual piece is placed slightly wider and deeper than the box. Its height is not changed.

### 5.3 Building the outside zone (VR)
- The outside zone becomes an empty virtual space aligned to the real floor, walls and ceiling.
- The user places any furniture there, regardless of role. Example: a sofa and bookshelves where the roommate's desk stands.

### 5.4 Floor, walls, windows, doors
- The user chooses whether the floor and walls stay real or get a virtual finish.
- Windows and doors are always shown as they really are, in either zone. Only their surroundings (frames, curtains) can be redesigned.

### 5.5 Hiding things you don't want to see
- Laundry, trash and other things the device doesn't recognize can be hidden by drawing a box around them by hand. The box is covered with the surrounding floor or wall design.
- A virtual prop (basket, plant, cushion) can also be placed over them.
- Hidden things are still physically there, so the user is reminded to take care when standing up.

### 5.6 Lighting and mood
- **Mood presets:** morning, afternoon, evening, night. The virtual lighting and the color of the real parts change together.
- **Matching the real view:** adjust passthrough brightness, contrast, saturation and color tone so the real parts and the virtual furniture share one tone.
- **Placed lights:** a virtual floor lamp or pendant brightens the virtual furniture around it. On real surfaces, a soft light overlay makes the lamp appear to light them.
- **Window light:** sunlight or evening light from the real window falls across the virtual space.

### 5.7 AI interior assistant
- **Fill a space:** ask for the outside zone, for example "make it a cozy living room", and the assistant places furniture and lighting to fit the room's layout in one step.
- **Recommend designs:** select a piece in my zone and the assistant suggests designs that suit the room's current mood.
- The assistant chooses and places from the assets in the app. The user can change any result.

### 5.8 Daily life in the new space
The goal doesn't end with decorating. The space the user makes becomes the space they live in.
- **Laptop work:** the desk stays in the same place at the same height. The laptop, keyboard and mouse on it are seen as they really are, so the user works as usual inside the redesigned room.
- **Study:** books, notebooks and pens are seen as they really are. For focus, the mood preset is set bright and calm.
- **Rest:** the user sits on the bed or chair and enjoys the space. Evening or night presets lower the lights.

Things the user handles, and the surfaces they sit on, are never covered by virtual objects.

## 6. User flow (demo video)

**Setting:** a symmetric two-person dormitory room. Each side has, in order, a door, a closet, a desk, a bed and a window.

1. Putting on the headset, the user sees their real dorm room.
2. They mark their zone (their desk, chair and bed side).
3. In edit mode, they select their desk and switch to one of the designs the assistant recommends. The height is unchanged, so the laptop stays where it was.
4. They redesign the bed and the closet the same way. Laundry on the floor is hidden under a virtual basket.
5. The roommate's side becomes an empty virtual space. They ask for "a cozy living room", and a sofa, bookshelves and lights appear.
6. They switch the mood to "evening". The virtual lights and the real parts of the room warm up together.
7. They adjust a few pieces by hand, then sit at the new desk, open the laptop and start working. Later they switch the lights to "night" and lean back on the bed to rest.

## 7. Design principles

- Designed for seated use.
- Every interaction works with hands, no controllers.
- Furniture the user actually uses matches reality in position and height.
- Windows and doors are always visible.
- Daily activities must not be interrupted. Things the user handles (laptop, books, pens) and the surfaces they sit on are always seen as they really are.
- The redesigned space is saved, appears again on the next launch, and can be changed at any time.
- Safety around other people relies on the Quest system feature (Space Sense). The app does not hide the system boundary.

## 8. Out of scope for this competition

- Sound (ambience and effects) comes in a later stage.
- Vertically split spaces such as bunk beds.
- Automatic detection of clutter using the camera feed and AI (future work).
- Showing the laptop screen as a sharp virtual window through remote desktop (future work).

## 9. Technical checks

| Item | Why it matters |
| --- | --- |
| Furniture types the device recognizes, especially chairs | Decides what can be redesigned automatically and what needs a hand-drawn box |
| Error between bounding boxes and real furniture | Decides how much wider the virtual piece must be |
| Keeping virtual furniture from covering the user's hands (Depth API) | Hands must stay visible |
| Visual treatment where the real and virtual floors meet | The seam is visible in the demo |
| Range of passthrough color adjustment | The core of the mood feature |
| Relationship between the system boundary and Space Sense | Safety relies on it |
| Lighting performance budget (baked vs. real-time) | 72 fps with a fully furnished room |
| AI response time, and the fallback when offline | The assistant must never block the user |
| Readability of a laptop screen and printed text through passthrough | Daily activities are a key message |

## 10. Name

**Own Space.** The name carries two meanings: *to own your space*, making a room you can't change truly yours, and *a space of your own*, a place that is only yours even inside a shared room.

- Display name: **Own Space**
- Repository, folders and package: `ownspace`
- A web search found no VR or MR app with this name. Similar names exist in unrelated categories (YourSpace, OurSpace, OnSpace.AI). Check the Meta store and Devpost once more before submission.


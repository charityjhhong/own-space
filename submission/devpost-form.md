# Devpost form: paste-ready text

One block per field, in the order the form asks. Personal fields (email, country, age and eligibility boxes) are left to the submitter. Thumbnails: `submission/art/post/`.

---

## 2. Project overview

**Project name**

```
Own Space
```

**Elevator pitch** (200 characters)

```
The room you can't change, made your own. Redesign your dorm, rental or hotel room on Meta Quest with your hands, while your real desk stays real and within reach.
```

**Thumbnail** (3:2): `submission/art/post/devpost-thumbnail-1500x1000.png`

---

## 3. Project details

**About the project** (Markdown)

```markdown
## Inspiration

Most of us study, work and rest in rooms we can't change. A shared dorm, a rental with rules, a hotel for a week. The walls aren't ours, there's no space for the furniture we want, and repainting isn't an option.

We wanted a space you change into what you want: a room where you can study or rest with an easy mind, because it feels like yours. And the real part you use, your desk, stays exactly as it is.

It isn't only comfort. Research on workspaces points the same way:

- People who arranged and decorated their own workspace were up to **32% more productive** than in a bare "lean" office, without more errors, and reported better well-being (Knight & Haslam, 2010, *Journal of Experimental Psychology: Applied*).
- Adding plants to bare offices raised productivity by about **15%** and improved concentration and workplace satisfaction (Nieuwenhuis, Knight, Postmes & Haslam, 2014, *Journal of Experimental Psychology: Applied*).

These studies are about real offices, and we haven't measured Own Space's own effect; they are why we built it, not a claim about the app. Most people can't rebuild their room to get that. Own Space lets them choose the space they study and rest in, without changing anything that belongs to someone else.

Sources: [Knight & Haslam 2010](https://pubmed.ncbi.nlm.nih.gov/20565201/), [University of Exeter summary](https://www.sciencedaily.com/releases/2010/09/100907104035.htm), [Nieuwenhuis et al. 2014](https://research.utwente.nl/en/publications/the-relative-benefits-of-green-versus-lean-office-space-three-fie/).

## What it does

Own Space is a seated, hands-only mixed reality app for Meta Quest 3 and 3S. It turns the room you can't change into a room you design, while your real desk stays real.

- **Scan and my zone.** Own Space reads the Quest room scan and keeps the area you use real: your desk and your hands. Everything around it becomes a virtual room at your room's real size, or bigger.
- **My room.** Give your real desk or bed a new design. It keeps the real piece's footprint and height, so it stays just as usable.
- **Style.** One floor and one wall finish for the whole room, with baseboards and crown molding.
- **Moods.** Morning, afternoon, evening or night. The virtual light and the passthrough are tone-matched so the real and virtual parts read as one room.
- **Add.** Pick a piece and point at the floor. If the spot doesn't fit, Own Space suggests the nearest one that does, and the piece pulses there until you pinch to confirm.
- **AI design.** Type a request or tap a style. The AI furnishes the room with catalog pieces only, one of each kind, facing you, never duplicating what you placed yourself. One Undo brings back the last look.
- **Tidy up.** Draw a box over laundry or clutter and cover it with a basket, a plant or the floor.
- **Saved spaces.** Save your room and open it anywhere: at a hotel desk or back home, it is rebuilt around the desk you're at, with your zone and changes kept.
- **A background room.** Open the Meta menu, YouTube or Meta's virtual displays: Own Space steps back and keeps your room around you while you work.

## How we built it

- **Unity 6 + Meta XR SDK:** passthrough, the Mixed Reality Utility Kit for walls, windows, doors and furniture boxes, and hand tracking.
- **A pure C# core** holds every rule (zones, the design state, placement and fit rules, saves, the AI plan check), covered by 688 EditMode tests. The MR, interaction and UI layers only draw and take input.
- **AI through a small Cloudflare Worker** that holds the Gemini key. The app sends text and room geometry only, never camera images, and checks every answer against the catalog before it touches your design.
- **Built for Quest:** a 72 Hz target, no per-frame allocation, lightweight custom shaders for contact shadows and tone matching, ARM64 only.
- **CC0 content:** furniture and textures from Poly Haven, Kenney and ambientCG, our own UI and art, the Inter font.
- **Four AI coding agents in parallel**, each owning its own folders (core, XR, build, content), with one human making the decisions and testing on the headset every day.

## Safety, privacy and respect for others

- **Seated by design.** Real furniture outside your zone is hidden by the virtual room, so Own Space is meant to be used sitting at your desk, with the Quest boundary on. A safer view when you stand up is on our list.
- **Shared rooms.** A roommate who walks in outside your zone is not shown. Use it the way you'd use headphones in a shared room: let people know, and take it off to talk.
- **Privacy.** No microphone, no camera images, no account data. Saved rooms stay on the headset. Only an AI request leaves it: your typed text, room outlines and catalog ids, through our Worker to Google's Gemini. Don't type private information into it.
- **Comfort.** Take regular breaks, as with any headset.

## Challenges we ran into

- **Hand-ray accuracy.** A ray from a tracked hand jitters, and a pinch moves the hand. We start the ray at the knuckle, filter it, and use the aim from just before the pinch, so you get what you pointed at.
- **Real and virtual in one room.** Heights, edges and light must agree with the real room, or the illusion breaks. Real pieces keep their usable height under every new design.
- **Overlapping room scans.** Two scans of the same room made the app switch designs. It now stays with the room you're in.
- **System UI over the app.** When the Meta menu or a Meta window takes the input, Own Space hides its tools, keeps its state and comes back cleanly.

## Accomplishments that we're proud of

- A real desk redesigned while it stays the desk you actually use.
- A room you can carry: save it, and open it again around a different desk.
- An AI that can only place what exists, and can always be undone.
- Hands only, start to finish. No controller needed.

## What we learned

Mixed reality is judged by the real things in view. Small, visible rules (keep the real height, one finish, one piece per kind) beat clever generation. Hands-only input needs generous targets and calm feedback.

## What's next

- Furniture that resizes without stretching (legs stay the same, the top grows).
- More pieces, made with 3D generation tools and curated before they enter the catalog.
- Seeing a roommate who walks in, and a safer view when you stand up.
- Sharing a saved space with a friend.
```

**Built with** (one tag at a time)

```
unity, c#, meta-xr-sdk, mixed-reality-utility-kit, openxr, hand-tracking, passthrough, universal-render-pipeline, meta-quest-3, meta-quest-3s, gemini, cloudflare-workers, typescript, poly-haven, ambientcg, kenney
```

**"Try it out" links**

```
https://github.com/charityjhhong/own-space
```

Add the Invite URL as a second link once the 1.0.0 upload passes.

**Image gallery** (3:2, up to 15): the 3:2 thumbnail first, then 3 to 5 headset screenshots.

**Demonstration video:** the YouTube URL (public or unlisted).

---

## 4. Additional info (judges only)

**Meta competition release channel URL:** the Invite URL of the `Competition` channel (Developer Dashboard, your app, Release channels, Competition).

**What Meta VR capabilities did you use?**

```
Passthrough (full-room mixed reality with a tone-matched virtual room around a real zone), Mixed Reality Utility Kit / Scene (walls, floor, ceiling, windows, doors and furniture volumes with their labels), hand tracking (pinch, poke, grab and a filtered hand ray; hands only, no controllers), scene anchors for furniture (a saved room is rebuilt around the real desk or bed it finds), and OpenXR focus handling so the room keeps running behind the Meta menu, YouTube and the laptop virtual display.
```

**Describe how Hand Interactions are implemented**

```
Own Space is hands only; no controller is ever needed.
- Pinch: aim a hand ray and pinch to press a button, pick a card, select a real piece or confirm a suggested spot. The ray starts at the index knuckle, is smoothed with a one-euro filter, and the pinch uses the aim from just before the fingers close, so the pinch lands where you pointed.
- Poke: when the panel is within arm's reach, touch it with a finger like a screen.
- Pinch and drag: move the panel by its handle, draw a Tidy up box, move the zone corners, and place or move furniture on the floor.
- Every action answers visually (highlight, press animation, pulse), since hands cannot vibrate.
- If hand tracking is lost mid-action, the action is cancelled safely and nothing is half-applied.
- Everything is reachable seated, within about 60 cm.
```

**What makes you excited about the future of VR?** (150 words)

```
For years VR asked you to leave your room. Mixed reality lets you stay, and change what's around you. That matters most for people who can't change their rooms at all: students in shared dorms, renters, people who travel for work. A headset can give them a space that feels like theirs, without a hammer or a landlord's permission, while the things they actually use (a desk, a laptop, a cup of coffee) stay real and within reach.

We're excited for the moment when this is ordinary: you sit down at any desk, and your own room appears around it, the way your phone's home screen follows you everywhere. Hands-only input, room understanding and better passthrough are already close. The next step is making these spaces personal, shareable, safe in shared rooms and comfortable, so they support real study and rest instead of replacing time away from the screen.
```

**Target launch date** (if not published)

```
Q1 2027 on the Meta Horizon Store, after testing in more rooms and on Quest 3S.
```

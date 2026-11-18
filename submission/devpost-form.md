# Devpost form: paste-ready text

One block per field, in the order the form asks. Personal fields (email, country, age and eligibility boxes) are left to the submitter. Thumbnails: `submission/art/post/`.

---

## 2. Project overview

**Project name**

```
Own Space
```

**Elevator pitch** (140 characters, from the rules)

```
The room you can't change, made your own. Redesign your dorm or rental on Meta Quest with your hands, while your real desk stays real.
```

**Thumbnail** (3:2): `submission/art/post/devpost-thumbnail-1500x1000.png`

---

## 3. Project details

**About the project** (Markdown, under 500 words as the rules suggest)

```markdown
## Inspiration

Most of us study, work and rest in rooms we can't change: a shared dorm, a rental with rules, a hotel for a week. We wanted a space you can change into what you want, where you can study or rest with an easy mind because it feels like yours, while the desk you actually use stays real.

Research on workspaces points the same way. People who decorated their own workspace were up to **32% more productive** than in a bare office, with better well-being ([Knight & Haslam, 2010](https://pubmed.ncbi.nlm.nih.gov/20565201/)). Adding plants raised productivity by about **15%** and improved concentration ([Nieuwenhuis et al., 2014](https://research.utwente.nl/en/publications/the-relative-benefits-of-green-versus-lean-office-space-three-fie/)). These are office studies, not measurements of our app, but they are why we built it.

## What it does

Own Space is a seated, hands-only mixed reality app for Meta Quest 3 and 3S.

- **My zone:** it reads the Quest room scan and keeps your desk area real. Everything around it becomes a virtual room at your room's size, or bigger.
- **My room:** give your real desk or bed a new look that keeps its footprint and height.
- **Style and moods:** floor and wall finishes, baseboards and crown molding; morning to night light, tone-matched to the real room.
- **Add:** place furniture by pointing; if a spot doesn't fit, the nearest one that does is suggested.
- **AI design:** type a request; the AI places catalog pieces only, facing you, and one Undo reverts it.
- **Tidy up:** cover clutter with a basket, a plant or the floor.
- **Saved spaces:** save your room and open it again around another desk.

## How we built it

Unity 6 with the Meta XR SDK: passthrough, the Mixed Reality Utility Kit and hand tracking. A pure C# core holds every rule (zones, placement, saves, the AI plan check) with 689 EditMode tests. The AI goes through a Cloudflare Worker that holds the Gemini key; the app sends text and room outlines, never camera images, and checks every answer against the catalog. Furniture and textures are CC0 (Poly Haven, Kenney, ambientCG). Four AI coding agents built it in parallel, each owning its own folders, with one human deciding and testing on the headset daily.

## Safety and privacy

Use it seated with the Quest boundary on: real furniture and people outside your zone are hidden. In a shared room, tell others, as with headphones. No microphone, no camera images, no account; saved rooms stay on the headset.

## Challenges

A tracked hand ray jitters and a pinch moves the hand, so we start the ray at the knuckle, filter it, and use the aim from just before the pinch. Overlapping room scans made the app switch designs; it now stays with the room you're in.

## What's next

Furniture that resizes without stretching, more curated pieces, showing a roommate who walks in, and sharing a saved space with a friend.
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

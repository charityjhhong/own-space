# Tool panel: UI spec (glass, light by default)

**Final — approved by the human.** This is the version `xr` builds from. Style, Furniture and Tidy up are as approved in the fourth review (circles, eye-icon Real, graphite selected, lighter hover, stepper); only the AI Designer tab got a small adjustment (a smaller field and chips). There is no separate type-size rule: sizes are the ones in the type table below.

**Updated: voice input is removed (no microphone) and Saved spaces is a main feature (7.7).**

**Updated to match the shipped panel (xr #193 to #196).** The tab bar is now **Style · My room · Add · AI · Tidy up** (the mockups under `ui/` still show the earlier four tabs; the layouts of the pages are unchanged). The old Furniture tab is split in two (section 7.2), the panel seats right of the right hand, can be resized and never sinks into a desk (section 6), and the gear opens a Settings sheet with more than Appearance.

A small translucent glass panel that floats over the user's real room. **Light glass is the default; the user can switch to Dark** in a small appearance setting. Text first, neutral colours, one cool accent. It takes its style (clean, light, rounded) from modern glass interfaces and uses no Apple assets, names or marks.

`content` designed it; `xr` builds it. Everything here is geometry and rendering reasoning: **size, distance, tilt and readability are `needs-headset`.** Nothing was tried on a device or in the simulator.

![room, light](ui/mockup-room-light-day.png)

The rooms behind the panel in the mockups are **synthetic stand-ins drawn by a script, not photos** (`ui/make_room_backdrops.py`); a real photo or HDRI would need a download approval. The furniture thumbnails are real renders of the catalog prefabs (section 9).

**Measurements for implementation:** `ui/spec-layout-style.png` (the Style tab with every padding, gap and height marked) and `ui/spec-components.png` (every component with sizes and states). All sizes below are millimetres at the panel's world scale, on a **4 mm grid** (steps 4, 8, 12, 16; the 8 pt grid scaled so that 1 pt = 0.5 mm).

## 1. Glass, and what Quest cannot do

| Layer (back to front) | Light | Dark |
| --- | --- | --- |
| Shadow | black 22%, blur 8, offset 2 x 7 | black 40% |
| Tint | `#FFFFFF` at **70%** | `#0F1826` at **80%** |
| Inner gradient | white 30% at the top to 5% to 10% at the bottom | white 16% to 3% to 5% |
| Rim | 0.5 white line at 80%, plus a 0.4 black hairline at 6% | same |
| Corner radius | panel 28, chips are pills (height / 2), tiles 12, cards 14, cards-in-card 20 | same |

**Honest limit:** the passthrough picture is composited by the system, so a panel on Quest **cannot blur or sample what is behind it**. "Glass" means tint, rim, highlight and shadow; the room shows through sharply. Legibility therefore comes from the denser tint (70 to 80%, not 15 to 25%), dark text on light glass and white text on dark glass, and text size and weight. A blur of virtual content only is possible but costs a render target and the panel rarely sits over virtual content, so it is not planned.

## 2. Themes and tokens

| Token | Light (default) | Dark |
| --- | --- | --- |
| Text `fg` | `#0E1520` | `#FFFFFF` (soft 0.4 mm shadow) |
| Secondary text | `fg` at 76% | `fg` at 82% |
| Chip fill, rim | ink 7%, ink 10% | white 14%, white 22% |
| Hover fill | ink 13%, and a 1 mm lift | white 24%, 1 mm lift |
| Selected fill, text | **graphite `#2C2F36`** (not pure black), white text | white, ink `#0E1520` text |
| Three steps | normal 7% ink < **hover 17% ink** + 1 mm lift < selected (solid graphite + ring) | normal 14% white < hover 26% white < selected (solid white + ring) |
| Accent ring | `#0A74E0`, 1.3 line, 2.4 outside the chip | `#6DB6FF` |
| Refused | `#E5483B` rim 1.2 and 12% fill, reason in `#C62F22` | reason in `#FF9A90` |

No brown, gold or beige in the chrome; furniture thumbnails and finish swatches keep their own colours. The accent is only for the selection ring, the grabbed handle, the busy ring and the onboarding ring.

**Appearance setting:** the **gear button** next to minimize (tooltip "Appearance") opens a small sheet with a `Light | Dark` control (`mockup-settings-open-light-day.png`; the numbered sheets say which button opens it). It is stored with the other settings and applies at once (a 0.3 s cross-fade of the tint and the text colour).

### Contrast (text over the pixels under the panel; `ui/contrast.json`, from `make_ui_assets.py`)

Worst case over the area under the panel, with the glass layers modelled (tint, then about 10% sheen):

| Theme, background | Primary text | Secondary text | Text on a chip | Selected chip |
| --- | --- | --- | --- | --- |
| Light, bright room | 12.0 | 6.6 | 10.4 | 18.2 |
| Light, dark room | 9.9 | 5.9 | 8.7 | 18.2 |
| Light, white wall `#E6E0D4` | 17.1 | 8.2 | 14.8 | 18.2 |
| Light, black wall `#101216` | 10.0 | 5.9 | 8.7 | 18.2 |
| Dark, bright room | 7.4 | 5.6 | 5.1 | 18.3 |
| Dark, dark room | 11.3 | 8.2 | 7.3 | 18.3 |
| Dark, white wall | 7.7 | 5.9 | 5.4 | 18.3 |
| Dark, black wall | 13.7 | 9.7 | 8.8 | 18.3 |

All pass 4.5 : 1. These come from the synthetic rooms and a model of the layers; the real passthrough image, exposure and display will differ. The tint densities (70%, 80%) were raised until the weakest case passed.

## 3. Type scale

Font: **Inter 4.1** (SIL OFL 1.1; not SF Pro, whose licence limits it to Apple platforms), approved by the human and downloaded from the official release (github.com/rsms/inter). `Art/Fonts/` holds `Inter-Regular.ttf`, `Inter-SemiBold.ttf` and `Inter-LICENSE.txt`. **`Content/Editor/FontBuilder.cs` builds the two static TMP SDF assets** (1024 x 512 atlas each, Latin plus curly quotes, en and em dash, ellipsis, middle dot, multiplication sign, arrows, degree, é). **Built** (xr imported the TMP Essential Resources in #146): `Art/Fonts/Inter-Regular SDF.asset` and `Inter-SemiBold SDF.asset`, 109 characters each, one 1024 x 512 atlas each, static (no runtime atlas growth); a rerun of `FontBuilder.BuildBatch` leaves no diff. Mockups use Helvetica Neue because they are SVG.

| Style | Size | Weight | Use |
| --- | --- | --- | --- |
| Tab (Level 1) | **14 mm** | Regular 400 (selected: Semibold) | the four tabs: **the largest text in the panel** |
| Label | **14 mm** | Regular 400 | chips, plan text, field text (never larger than the tabs) |
| Card name | **12 mm** | Regular 400 | furniture card names (2 lines at most, ellipsis); the minimum size |
| Small | **13 mm** | Regular 400 | filter chips, swatch and card labels, small controls, status |
| Section title | 13 mm | **Semibold 600**, secondary colour | "Floor", "Wall", "Mood", "Plan", "Fill the outside zone", "Apply to", "Appearance" |
| Selected tab | 15 mm | **Semibold 600** | the selected segment of a segmented control only |

Option chips hug their content (width = text + 24 mm, wrapping to a new row); the full-width controls are the text field and the plan card.

Two weights only. Sentence case everywhere. Labels are centred on the chip's vertical middle (cap-height centring), so baselines line up across a row.

**Angular size at 0.45 m** (Quest 3 about 25 px per degree): cap height is 0.72 of the font size. 14 mm gives a 10.1 mm cap height, **1.29 degrees (about 32 px)**; 13 mm gives 9.4 mm, **1.19 degrees (about 30 px)**. The minimum is **1.1 degrees (8.6 mm cap, a 12 mm font)**, so nothing is set below 12 mm (cap 8.6 mm, exactly 1.1 degrees). This is smaller than v2 (17 and 15 mm) on purpose.

**Relation to the chip:** a normal chip is 36 mm high. The 14 mm label is 39% of the height (the cap height is 28%), the text fills at most 75% of the chip width, and there are at least 24 mm of side padding (16 mm on 28 mm chips with 13 mm labels). The suggested 55 to 60% would need a 20 mm font in a 36 mm chip, which is what read as too big; it is not used.

## 4. Hierarchy: three levels, one style per level

![hierarchy](ui/spec-hierarchy.png)

| Level | Question it answers | Style |
| --- | --- | --- |
| **1 Tabs** | Where am I? | The only segmented control in the panel: 36 high, 14 mm, selected = graphite pill with white semibold text. |
| **2 Sections and sub-navigation** | What am I choosing? | Quiet text with no fill and no pill: section titles (13 mm semibold, 76% ink) and text toggles (13 mm; the active one is semibold with a 1.8 mm underline, the others 76% ink). Smaller than the tabs, never filled. |
| **3 Options** | The choice itself | Chips (36 high pills, 15 mm), swatches (34 mm, label below), cards (80 x 92). Selected is always filled or ringed. |

Feedback is not part of the layout: a **toast** above the panel and a **hint chip** below it (section 7.5).

## 5. Spacing

| Token | mm | Where |
| --- | --- | --- |
| Outer padding | 16 | all four sides; the inner width is 256 |
| Gap | 8 | between chips, swatches rows, cards |
| Section gap | 12 to 16 | 16 below the toggle row, 12 between Floor, Wall and Mood |
| Title to content | 6 | section title (16 high) to its row |
| Top strip | y 8 to 36 | undo, redo, the handle, settings, minimize (28 x 28 buttons); the top padding is 8 |
| Tab control | y 40, 36 high | 4 below the strip |
| Body starts | y 84 | 8 below the tabs |
| Bottom | 16 | **no footer** |

Heights follow the tab: **Style 386, Furniture 362, AI Designer 312 (before the AI has run) to 430 (plan with the quoted request), Tidy up 246 to 282**. Width **296**. `ui/spec-layout-style.png` marks every value; `ui/spec-components.png` shows each component.

## 6. Behaviour: place, minimize, move

| Property | Value |
| --- | --- |
| Size | **0.296 m wide**, 0.30 to 0.39 m high at 1.0x (0.8x to 1.6x with the resize grip) |
| Seat | The panel's centre is **0.25 m right of the tracked right hand** (a little further when scaled up), 0.4 to 0.7 m ahead of the eyes, at chest or desk height (0.2 m above the hand, between 0.55 and 0.1 m below the eyes), facing the eyes. With no tracked hand: right of the view centre at the usual distance. Double-pinching the top handle opens it there. |
| **Never in a desk** | The bottom edge stays at least **0.05 m above any furniture under it**: the panel rises, and if that would put its top more than 0.1 m above the eyes it first comes toward the user (up to 0.4 m). Applied on seat, drag and relayout. |
| **Draws on top** | The glass, icons, thumbnails, text and the reticle draw over the room and over kept real pieces (no depth test), so nothing is cut off by a desk. |
| **Resize** | A **grip** at the bottom-right corner (two short diagonal bars, hit zone 32 mm): pinch and drag scales the whole panel **0.8x to 1.6x** about its top-left corner; **double-pinch** the grip resets to 1.0x. The size is saved and restored. Everything (hit tests, text, banner) scales with it. |
| Follow | Lazy: stays within 20 degrees yaw and 15 degrees pitch of the view and under 0.6 m, else eases back over 0.6 s (`ToolFollow`, `FloatingRow.ReachInside`, never past a wall). While a drag runs it dims to 35% and holds still. |
| **Minimize** | The bar button collapses it to a pill (`Own Space` + the tab, 36 high); pinching the pill opens it again (`mockup-pill-*.png`). |
| **Move** | Pinch and hold the handle bar, move, release; it stays there (follow off). Double-pinch the handle to send it back. The handle turns accent blue while grabbed. |
| **Settings** | The **gear button** (top right, next to minimize; tooltip "Appearance") opens a **Settings sheet** in place of the current page (about 0.32 m tall at most; the gear again, **Close** or any tab closes it): **Appearance** `Light | Dark`; **Rescan room** (opens Space Setup; a new scan of this room gets a fresh zone; a note says so; in the Editor it says "Space Setup runs on the headset"); **Reset my design** in two steps ("Reset everything in this room? This can't be undone." with **Reset** and **Cancel**; it clears this room's zone, finishes, mood, pieces, covers and the AI plan, and the zone step starts again; other rooms are untouched). Mockups: `mockup-settings-tooltip-*.png`, `mockup-settings-open-*.png` (Appearance only). |
| Input | Hands only: far ray plus pinch, or finger poke within 15 mm. Hit boxes are the visual plus 4 mm. No haptics. |

## 7. States, feedback and tabs

![states](ui/mockup-states-light-day.png)

| State | Look |
| --- | --- |
| Unselected | glass chip with text |
| Hover | slightly denser fill and a 1 mm lift with a soft shadow |
| Pressed | 97% scale, a little more transparent |
| **Selected** | **filled** (Light: graphite `#2C2F36` with white text; Dark: white with ink text) plus the thin accent ring. No check marks. Swatches: the accent ring. |
| Disabled | 40% opacity |
| Refused | the red toast with the reason (7.5), a 5 mm shake of the element |
| Busy | accent ring on the chosen chip and a turning arc; the others disabled |

Transitions: 80 ms for colour and lift; nothing longer than 0.3 s except the mood blend, the follow and the theme cross-fade.

### 7.1 Style (398)

![style](ui/mockup-style-light-day.png)

- **Apply to** `My zone | Outside`: Level-2 text toggle (13 mm, underline on the active one) on one 24 mm row.
- **Floor**, **Wall** and **Mood** each have a section title and one row of **five columns of 52.8 mm**, so everything lines up. **Every option swatch is a 34 mm disc** with the label (13 mm) below; floor and wall discs carry a thin pattern, mood discs a soft time-of-day gradient: Morning (warm peach), Day (pale sky), Evening (orange to violet), Night (indigo). Names: Oak, Birch, Dark, Tiles; Cream, Lines, Olive, Navy (`content` adds `shortName` to `finishes.json`).
- **`Real`** (keep the real floor, wall or light) is a **transparent disc with a thin ring and an eye glyph**: the room shows through it, and the eye says "as it is". A diagonal slash read as "none". The other option tried, a room-corner glyph, needs a second look (`ui/spec-real-options.png`, light and dark). Selected = the accent ring.
- Mood applies to the whole room; it no longer looks like the tab bar.
- There is no persistent text about the current choice: the selected swatch is the state. After a change the toast confirms it (7.5).

### 7.2 My room and Add (the old Furniture tab, split in two)

![furniture](ui/mockup-furniture-light-day.png)

Restyling a real piece and adding a virtual one are **two tabs**, so the two jobs never mix (device day: a desk card placed a desk outside instead of restyling the real one). The five tab names are short on purpose: "My furniture", "Add furniture" and "AI Designer" do not fit the 268 mm tab row at the 12 mm minimum text size. The page layout below is the same for both tabs.

- **Sub-categories** are Level-2 text toggles in **one scrollable row** (13 mm, the active one semibold with an underline, the rest 76% ink): Seating (sofa, armchair), Beds, Tables (desk, coffee table), Storage (closet, shelf), Lights, Decor (plant, rug, wall art, props), Window (curtain, door trim).
- **Cards:** 3 columns x 2 rows, 80 x 92, radius 14: the real thumbnail (68 x 46) and the name (**12 mm**, at most 2 lines, then an ellipsis). The grid does not scroll; the **pager** `<  1 / 3  >` (two chevrons) is at the bottom right.
- **My room** restyles the **real furniture in my zone**. Opening it with nothing selected **selects the nearest real piece, desks first**, with its outline; the cards are the designs that fit that piece (`FitLimits`) and pinching one restyles it. Pinching another real box switches the selection; **Keep the real one** restores the real piece. **No floor placement and no banner ever happen here.** With no real piece in the zone the page says: "No furniture in your zone. Edit my zone in Settings."
- **Add** places **virtual pieces in the outside zone**. Pick a card, then pinch the floor outside your zone; the dark **banner above the panel** says "Pinch the floor outside your zone to place <piece> · Cancel" while it is pending (the only place the banner appears). It ignores any room selection; leaving the tab drops an armed card.
- Selecting a real box in the room switches to **My room**, unless you are using the panel.
- Refused: the red toast with the reason (`mockup-furniture-refused-*.png`).

### 7.3 AI Designer (tab label: AI)

![ai designer](ui/mockup-ai-designer-done-light-day.png)

- **"Describe your space…"** is a glass text field (**30 high, a little smaller than the 36 mm tab bar**, 13 mm text, 16 mm side padding, pill) at the top of the tab. **Pinch** opens the Quest system keyboard. It is the only free-text input in the panel besides naming a saved space (7.7). **There is no voice input and no microphone glyph;** requests are typed, or come from a preset chip, and go out as text.
- **States** (all in `ui/mockup-ai-designer-*.png`): **empty** (placeholder in secondary colour); **typing** (an accent ring and a caret; the system keyboard is open, shown only by a hint chip under the panel: "Type your request"; the panel does not draw a keyboard); **submitted** (`-busy`: the field dims and shows a turning arc, the card says "Thinking…" with **Cancel**); **done** (`-done`: the plan card below, with the request quoted).
- Below the field a section title **"Or pick a style"** and four **content-hugging** preset chips (**30 high, 13 mm text, 20 mm side padding**, wrapping: Living room, Study café, Bedroom, Bright studio): quick picks that send the same request as typing it (the request keeps the full prompts of `AskMenu`).
- **AI's plan card** (radius 20, 124 high, 150 with a quote; a thin accent hairline so it reads as the AI's output, not an input): a small sparkle and "AI's plan", the **user's request quoted in one line** (13 mm, secondary), the AI's explanation (14 mm, 2 lines), `7 placed · 1 skipped` and the **Undo plan** chip inside the card. It exists **only after the AI has run**; before that one quiet line: "The AI will furnish the outside zone". A fallback result says "Offline plan". After a result a toast says "Placed 7 pieces · Undo".
- **Error:** "No change made. Try again." Cancel also happens on pause, losing both hands for 1 s, or editing the zone; nothing is applied.
- **Suggest for the selected piece:** a hugging chip (28 high), enabled only while a my-zone piece is selected.
- The panel grows to about 0.45 m on this tab when the plan card shows (see heights above).

### 7.7 Saved spaces (Settings › My space › Spaces)

**What it is:** save the room you are in once, for example your room at home, and go back to it from anywhere, such as a hotel abroad. **It is not a photo.** A saved space is the scanned room geometry (floor, walls, windows, doors, furniture boxes) plus your designs, and it is shown as a virtual room built around you. Say so plainly in every place the feature is described.

- **Where:** the Settings sheet, My space section, **Spaces**. It opens the **Spaces sheet** in place of the current page. When there is no usable scan, the zone card also offers **Use sample room** next to Done.
- **The list:** one card per space, in this order: **This room** (when a scan is usable, and while visiting), every saved space by name with its date ("Home · Nov 9"), and **Sample room** last. The current one is marked. **Pinch a card to go there.** Toasts: "You're in <name>" and "Back in this room".
- **Save this room** (only in the real room, not while visiting): choose a name chip, **Home, Dorm, Study room, Hotel, Office**, or **Type a name** with the on-panel keyboard (up to 40 characters, placeholder "Name this space", **Create** saves). Toast: "Saved as <name>". The limit is **10 spaces**: "Up to 10 spaces: delete one first". With no scan it is refused with a short reason.
- **The "..." on a saved card:** Go there, **Rename** (the keyboard, starting from the old name) and **Delete** (red, asks first: "Delete Home? This cannot be undone.").
- **The anchor** is the **desk or bed you are at** when you save. On a visit the saved desk or bed lands on the real piece of the same role (else in front of you, about 0.55 m ahead, on the floor), the **real zone** is that desk or bed plus the usual margin, and everything else in the saved room is virtual. Furniture shows with the design you chose, or a default design of its role.
- **The sample room** is built in (3.2 x 4 m, 2.5 m ceiling, a window over the desk, a door, a desk and a single bed) so anyone can try the feature with no scan, and so it can be tested in a dark or unscanned room. It never needs a network.
- **Not yet:** the room outline on each card and the soft fade-in on Go there.

### 7.4 Tidy up

![tidy up](ui/mockup-tidy-up-light-day.png)

**Why "Tidy up":** "Hide" read as "close the panel". The tab is the clutter-cover tool; minimize stays the top-right icon only. It is **sequential, so the tab shows its steps in order** (`ui/spec-annotated-tidy-up.png` shows before and after; `-step1.png` and `-step3.png` annotate the other steps):

- **The steps advance by themselves** (`ui/spec-tidy-up-flow.png`, arrows marked "auto"): when the user **releases the pinch and the drawn box is valid**, the panel goes from step 1 to **step 2 "Pick a look"** on its own; after a look is picked, **step 3 "Done"** shows automatically. If the box is **refused** (window, door, desk top, outside my zone, ...), the panel **stays on step 1**: the box turns red and **one red line gives the reason** ("Can't cover a window. Draw somewhere else."); nothing is covered, and the panel advances only once a valid box is drawn. **Add another** goes back to step 1.
- **Stepper** at the top: `1 Draw a box` → `2 Pick a look` → `3 Done` (13 mm). The **current** step has a graphite number disc and semibold text; **completed** steps get a small check in an outline disc; **future** steps are muted (40%). It is not a tab control: no fill, no pill.
- **Step 1, Draw a box:** the instruction "Pinch and drag on the floor over the clutter" and a small illustration (clutter on the floor with a dashed box being dragged over it). While drawing, a **Cancel** chip and a hint chip under the panel ("Drawing: release to set the box"; `mockup-tidy-up-step1-drawing-*.png`). In the room the box is green when allowed and red when not (window, door, desk top, outside my zone), with a red toast giving the reason.
- **Step 2, Pick a look** (`mockup-tidy-up-*.png`): the section title and six **round options** (the same disc swatches as Style): **Plain** (a disc in the floor colour: cover with the floor or wall), **Basket**, **Plant**, **Cushion**, **Box** (storage box), **Screen**, each with a rendered thumbnail and a label below. Choosing one applies it at once.
- **Step 3, Done** (`mockup-tidy-up-step3-*.png`): "Covered with a basket." and a short line, a hugging **Add another** chip (back to step 1) and a quiet red text **Remove**. A toast "Basket placed · Undo".
- **Existing cover:** selecting a cover in the room **jumps to step 2 for that cover** (its look is selected) with a quiet red **Remove this cover** at the end of the row (`mockup-tidy-up-existing-*.png`).
- **Safety line:** one small secondary line with an info glyph at the bottom: "Hidden things are still there. Mind your step when you stand up." (12 mm).
- Zone setup stays in the world: the **Done** cube that confirms the zone is not part of the panel.

### 7.5 Feedback: toast and hint chip (no footer)

- **Toast:** a small pill **above the panel** (outside it, so nothing shifts), graphite on Light and white on Dark, for about 2 s with a 150 ms fade in and a 250 ms fade out: "Oak floor · My zone  **Undo**" (`mockup-style-changed-*.png`). Pinching **Undo** in it is the same as the Undo button. Refused actions use the same pill in red with a one-line reason ("Doesn't fit this box: 2.4 m wide").
- **Hint chip:** a small glass chip **below the panel** with an accent dot, shown only while an action is pending ("Pinch the floor to place", "Drag across the floor"); it disappears when the action ends or is cancelled.

### 7.6 What each button does

Numbered sheets for every tab: `ui/spec-annotated-style.png`, `-furniture.png`, `-ai-designer.png` (`-ai-designer-voice.png` is retired with voice), `-tidy-up.png`, `-tidy-up-step1.png`, `-tidy-up-step3.png` (each interactive element is numbered and described).

## 8. Icons: ten glyphs

Only where words do not work (own artwork, CC0, 48 x 48, one colour; `ui/icons/*.svg`, source `ui/icons.py`): `undo`, `redo`, `settings` (the gear), `minimize` (a bar), `chevron-left`, `chevron-right`, `eye` (inside the Real discs), `sparkle` (the AI's plan title), `info` (the safety line) and `check` (completed steps). Everything else is text.

## 9. Furniture thumbnails (implemented, unchanged)

`Content/Editor/ThumbnailBuilder.cs` (menu `OwnSpace/Content/Build Furniture Thumbnails`, batch `ThumbnailBuilder.BuildBatch`; **no `-nographics`**) renders every catalog prefab in an editor preview scene: orthographic 3/4 view, framed to the bounds, one light, transparent background, 2x supersampled and filtered. **Each item is rendered with its own colour variant** (the importer tints a shared model toward the item's `primaryColor`, so the three armchairs and the four sofas differ). Output `Art/UI/FurnitureThumbs.png` (1920 x 896, 10 x 7 cells of 192 x 128, 68 items) and `Content/thumbnails.json` (cell size and each item's `x, y` from the top left; UV `u = x / width`, `v = 1 - (y + cellHeight) / height`). A rerun gives the same picture; the GPU changes about 0.1% of the edge pixels by a few levels, so the PNG bytes can differ. Rerun after any asset PR.

## 10. Performance (for `xr`)

| Budget | Plan |
| --- | --- |
| Draw calls | **At most 6**: shadow, glass body and chips (one UI atlas of 9-slice shapes and a gradient, tinted by vertex colour), thumbnails (one atlas), text (TMP SDF, one font), hover shadow, the world-space onboarding ring. Limit about 10. |
| Transparency | Alpha-blended and sorted back to front. **No `Mask`, no `ScrollRect`, no per-card `Canvas`, no runtime layout groups** (positions come from this spec). A tab change toggles pooled card objects. |
| Per frame | Zero allocation: pre-built strings, the ring turns by transform, the theme is a set of colours swapped on a change. |
| UI atlas | 1024 x 1024, ASTC 6x6, no mipmaps: 9-slice pill, tile, card, panel, the gradient, the glyphs at 96 px. White with alpha, tinted by vertex colour, so one atlas serves both themes. |
| Glass | No blur, no grab pass, no render texture. |

## 11. Onboarding hints

`OnboardingGuide` keeps its steps; the targets change. A hint draws a dashed accent ring (1.2 line, pulsing 1.0 to 1.08 over 1.5 s) around the target, with the ghost hand. The panel opens on the tab the step needs.

| Step | Target | Hint |
| --- | --- | --- |
| Set the zone | the world **Done** cube (unchanged) | "Pinch Done" |
| Redesign a piece | the nearest desk or bed box, then the **My room** tab | "Pinch your desk" (or open My room) |
| Fill the outside | the **AI** tab, then the first chip | "Ask for a room" |
| Pick a mood | the **Style** tab, then the mood control | "Pick a mood" |
| Skip | a small "Skip" text chip at the right of the status line | |

## 12. Replacing the placeholders

| Today | Becomes |
| --- | --- |
| Violet style orb and its sphere rows | **Style** tab |
| Swatch grid and tile rows | **My room** and **Add** cards and **Style** swatches |
| Cyan Ask orb and its chips | **AI** tab (the AI Designer) |
| White hide cube and handles | **Tidy up** tab (the in-room box colours stay) |
| Unlabeled spheres (zone, skip) | the zone control and a "Skip" chip |
| Floating text labels | the status line |

## 13. For the human

- Is 0.296 m wide with 13 to 15 mm text readable at 0.45 m? (`needs-headset`; one scale factor tunes the whole panel, default 1.0, try 0.9 to 1.25.)
- The panel is up to 0.39 m high on the busiest tabs; if that is too tall, Furniture can show 3 cards per page instead of 6.
- A real room photo for better mockups, and the font Inter, each need a download approval.

## Files

`ui/make_ui_assets.py` (mockups, spec sheets, `contrast.json`), `ui/make_room_backdrops.py`, `ui/icons.py`, `ui/icons/`, `ui/thumbs-atlas.png` (a copy of the thumbnail atlas for the mockups), `ui/mockup-<name>-<theme>-<room>.png` (names: style, furniture, ai-designer, hide, plus states, pill, room, settings-*; theme `light` or `dark`, room `day` or `night`; SVGs are generated and ignored), `ui/spec-hierarchy.png`, `ui/spec-layout-style.png`, `ui/spec-components.png`, `ui/spec-annotated-*.png`. Regenerate: `python3 docs/design/ui/make_room_backdrops.py && python3 docs/design/ui/make_ui_assets.py` (numpy, pillow, `rsvg-convert`).

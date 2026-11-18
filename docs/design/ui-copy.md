# UI copy pass: plain words for every user-facing string

Brief from the human: "make the UI more intuitive; the words are vague." This is the proposal only. `xr` applies it in `app/Assets/_Project/MR/` and `UI/`; `core` applies the rows marked **core** (`Core/`). Source of the "current" column: the strings in the code at `main` 01057d6 (file named in each row).

## Rules used

1. A button is a verb first, or a clear noun, and **at most 3 words**.
2. No jargon. "Zone" becomes **your real area** and "outside" becomes **the virtual area**. The words are explained once, on the zone card: "Your real area is the part of the room you keep as it is. Everything outside it is virtual." Every later string just uses them.
3. One name per thing, everywhere. Furniture you own = **your real piece**; furniture the app adds = **a piece** or **a look**; clearing the room = **start over**.
4. Refusal reasons say what to do, not what the code found.
5. `Done` is never used alone: it says what is saved.

## Fit check (xr text audit limits)

The text audit (`MR/Tests/TextFitAuditTests.cs`) fails when TextMeshPro has to cut text after shrinking to the **10 mm floor** (tabs: **12 mm** floor, 15 mm when selected). Widths below are measured with the shipped Inter Regular and SemiBold at that size, plus the panel's own padding (24 mm per chip, 8 mm per tab). The panel is 300 mm wide with 16 mm padding, so a row has **268 mm**.

| Tab bar | Widest case (any one tab selected), 12 mm / 15 mm | Fits 268 mm? |
| --- | --- | --- |
| Current: Style · My room · Add · AI · Tidy up | 209 mm | yes |
| Wish list: Style · My furniture · Add furniture · AI designer · Hide clutter | 365 mm | **no** |
| Style · My things · Add · Ask AI · Hide clutter | 267 mm | no margin, **not proposed** |
| **Proposed: Style · My things · Add · Ask AI · Hide** | **225 mm** | **yes, 43 mm to spare** |

So the tab names stay short and the long names move to the **page title** inside each page, where there is room: "Add furniture", "Hide clutter", "AI designer". If `xr` widens the panel to 340 mm, `Hide clutter` can become the tab name (it then fits at 267 of 308 mm).

Chip and button widths (13 mm text, 24 mm padding; floor 10 mm in brackets): Add furniture 105 (86), Show my real desk 141 (114), Save area ~90 (74), Include desk ~95 (78), Leave it out ~100 (82), Hide another ~105 (86), Remove cover 113 (92), Start over ~90 (74). Each is at most 141 mm, well inside a 268 mm row, and two chips side by side (the widest pair, Include desk + Leave it out) need about 200 mm. All of them fit above the 10 mm floor.

## Tabs and strip

| Current | Proposed | Where |
| --- | --- | --- |
| Style | Style | tab 1 (`PanelHost.Tabs`) |
| My room | My things | tab 2: the page that changes your real pieces. "Room" clashed with Settings and the real room. |
| Add | Add (page title: **Add furniture**) | tab 3 |
| AI | Ask AI (page title: **AI designer**) | tab 4 |
| Tidy up | Hide (page title: **Hide clutter**) | tab 5 |
| Undo / Redo / Settings (strip tooltips) | Undo / Redo / Settings | strip, `ToolPanel.Feedback.cs` |
| Appearance (gear tooltip) | Settings | gear button, `ui-spec.md` section 5 |
| Own Space | Own Space | panel title |

## Style tab (`StylePage.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Apply to | Change | row title: which room the circles change |
| My zone / Outside | Real area / Virtual area | the two-segment toggle |
| Floor · your real room | Floor · real area | section title |
| Floor · the virtual room | Floor · virtual area | section title |
| Wall · your real room / Wall · the virtual room | Wall · real area / Wall · virtual area | section title |
| Mood | Mood | section title (unchanged) |
| Real (eye circle) | Keep real (46 mm at 10 mm, circle is 56 mm) | first circle of every row |
| Plain | Simple (wall: tooltip "Simple wall", floor: "Simple floor") | virtual-area circle with no pattern |

## My things tab (`FurniturePage.cs`, was "My room")

| Current | Proposed | Where |
| --- | --- | --- |
| Designs that fit this desk / bed / storage / couch / piece | Looks for this desk / bed / storage / couch / piece | section title above the cards |
| Selected: <name> | Selected: <name> | title (unchanged) |
| Keep the real one | Show my real desk (bed / couch / storage; "Show my real piece" when unknown) | button that undoes the look |
| Place outside | Move to virtual | button on a real piece that sends it to the virtual area |
| Adjust fit | Adjust size | button once a look is on a piece |
| Fit the design to your piece | Match your real piece | title of the adjust sheet |
| Reset / Wider / Narrower / Deeper / Shallower | unchanged | adjust sheet |
| Drag the piece to move it, or its corner handle to turn it | Drag to move. Drag the corner dot to turn. | hint, adjust sheet and My things |
| Done (adjust sheet) | Keep size | closes the adjust sheet |
| Remove | Remove piece | button on a selected added piece |
| No designs fit this piece | No ready-made look fits this piece | `NoFit` |
| No design fits this desk (1.20 × 0.60 m) | Nothing fits your desk (1.20 × 0.60 m) | per-piece reason |
| No furniture in your zone. Edit my zone in Settings. | No furniture in your real area. Change the area in Settings. | empty page |

## Add tab (`FurniturePage.cs`, `PanelHost.cs`, `OutsidePlacer.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Seating / Beds / Tables / Storage / Lights / Decor | unchanged | category chips |
| Window | Curtains | category chip (it holds curtains and door trims; "Curtains and trim" is too wide for a chip) |
| Pinch the floor outside your zone to place · Cancel | Pinch the virtual floor to place · Cancel | armed banner, `PlaceHint` |
| Pinch the floor outside your zone to place <name> · Cancel | Pinch the virtual floor to place <name> · Cancel | armed banner with a name |
| Pinch a wall to hang <name> · Cancel | unchanged | wall-art banner |
| No space outside your zone. Shrink it in Settings > Edit my zone. | No free floor. Make your real area smaller in Settings. | `NoSpace` |
| Aim at the floor outside your zone | Aim at the virtual floor | `AimAtFloor` |
| That's your zone: pinch the floor past the green line | That's your real area. Pinch past the green line. | `InsideZone` |
| Can't place it there / Can't hang it there / Pinch a wall to hang it | unchanged | refusals |

## AI tab (`AiDesignerPage.cs`, `AskMenu.cs`, `FreeTextRequest.cs`, **core** `AssistantRunner.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Describe your space… | Describe the room you want… | text field |
| Send | Ask | button beside the field |
| Or pick a style | Or start from a style | section title |
| Living room / Study café / Bedroom / Bright studio | unchanged | preset chips |
| The AI will furnish the outside zone | The AI fills your virtual area | line before the first run |
| Thinking… / Cancel | unchanged | while it runs |
| No change made. Try again. | Nothing changed. Try again. | after an error |
| AI's plan · Offline plan | AI's plan · Offline | result title when the fallback ran |
| 7 placed · 1 skipped | 7 added · 1 didn't fit | result line |
| Undo plan | Undo all | button after a run |
| Nothing was sent (the AI is busy, or no zone yet) | Not sent. The AI is busy, or your real area isn't set. | `FreeTextRequest` |
| Typing isn't available here | Typing isn't available here | unchanged |
| Couldn't ask the assistant | Couldn't reach the AI | `AskMenu` |
| Took too long | The AI took too long. Try again. | **core** `AssistantRunner` |
| You're offline | No internet. Try again. | **core** |
| Something went wrong. Try again. | unchanged | **core** |
| Added 7 pieces / Added 1 piece | unchanged | **core** status line |
| Nothing fit on the free floor | Nothing fit in the virtual area | **core** |

## Refusal reasons (**core** `PlanChecker.cs`, `WorldLabel.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Placed 1 of 6: not enough free floor | Added 1 of 6: not enough room | **core** summary |
| Not enough free floor outside your zone | Not enough room in the virtual area | **core** |
| no free wall spot | no free wall space | **core** reason |
| no free floor outside your zone | no free room in the virtual area | **core** reason |
| overlaps other pieces | overlaps another piece | **core** reason |
| blocked by real furniture | your real furniture is in the way | **core** reason |
| would block a door / window | would block a door / window | unchanged |
| too big for the free floor | too big for the space | **core** reason |
| unknown pieces | not in the catalog | **core** short reason |
| Not for outside | Can't go in the virtual area | `WorldLabel` |
| Too close to the door | unchanged | `WorldLabel` |
| Can't stand there | Can't go there | `WorldLabel` |
| Can't place it | Can't go there | `WorldLabel` |
| Too small for this spot | Too small for this spot | unchanged |
| Can't apply it | Can't use it here | `WorldLabel` |
| Too small / Too large (a hide box) | Box too small / Box too big | `WorldLabel` |
| Can't cover with this | Pick another cover | `WorldLabel` |
| Can't hide it | Can't hide that | `WorldLabel` |

## Hide tab (`TidyUpPage.cs`, `HideTool.cs`, was "Tidy up")

| Current | Proposed | Where |
| --- | --- | --- |
| 1 Draw a box / 2 Pick a look / 3 Done | 1 Draw box / 2 Pick cover / 3 Hidden | stepper |
| Pinch and drag on the floor over the clutter | Draw a box on the floor around the clutter | step 1 instruction |
| Draw a box | Draw box | step 1 button |
| Pinch and drag on the floor to draw a box · Cancel | Pinch and drag on the floor around it · Cancel | armed banner, `DrawBanner` |
| Pick a look | Pick a cover | step 2 title |
| Plain / Basket / Plant / Cushion / Box / Screen | Simple / Basket / Plant / Cushion / Box / Screen | cover chips |
| Remove this cover | Remove cover | step 2 button |
| Covered with a basket. / Covered. | Hidden under a basket. / Hidden. | step 3 title |
| Add another | Hide another | step 3 button |
| Remove | Remove cover | step 3 button |
| Hidden things are still there. Mind your step when you stand up. | Hidden things are still there. Watch your step. | safety line |

## Settings (`SettingsPage.cs`, `PanelHost.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Settings | Settings | sheet title |
| Appearance (Light / Dark) | Theme (Light / Dark) | section title |
| My space | Room and area | section title |
| Edit my zone | Change real area | button |
| Rescan room | Scan again | button |
| Opens Space Setup. A new scan of this room gets a fresh zone. | Opens Space Setup. Scanning again resets your real area. | note under the button |
| Reset my design | Start over | button |
| Remove all your changes in this room? Your zone stays. | Remove all your changes in this room? Your real area stays. | confirm text |
| Reset / Cancel | Remove all / Cancel | confirm buttons |
| Close | Close | button |
| Real occlusion: On / Off | Real objects in front: On / Off | toggle |
| Undone / Redone | unchanged | toasts |
| Space Setup runs on the headset | Room scan works on the headset only | toast, Editor only |
| Nothing to reset yet | Nothing to start over yet | toast |
| Design reset | Back to your real room | toast |

## Zone card and onboarding (`ZoneSetup.cs`, `OnboardingGuide.cs`)

| Current | Proposed | Where |
| --- | --- | --- |
| Drag the corners to set your zone. | Drag the corners to mark your real area. | zone card line |
| Inside stays real. Outside becomes virtual. | Your real area is the part of the room you keep as it is. Everything outside it is virtual. | zone card, the one place the words are explained |
| Done | Save area | zone card button and on-floor label |
| Your desk is outside your zone. | Your desk is outside your real area. | desk card |
| Fit the zone to it, or keep it? | Include it, or leave it out? | desk card question |
| Fit to desk / Keep anyway | Include desk / Leave it out | desk card buttons |
| This outline is your zone. Pinch a ball, an edge, or inside it to move it, then pinch Done. | This outline is your real area. Pinch a corner ball, an edge or the inside to move it. Then pinch Save area. | on-floor hint |
| No room scan: Settings > Rescan room | No room scan yet. Settings > Scan again. | zone card |
| No scan of this room: walls and furniture need one from Space Setup (Settings > Physical space). | No scan of this room yet. Walls and furniture need one from Space Setup (Quest Settings > Physical space). | on-floor hint |
| Only a desk was found. Add your chair in Space Setup to restyle it. | unchanged | note |
| Pinch Done | Pinch Save area | onboarding step 1 |
| Pinch your desk (or open My room) | Pinch your desk (or open My things) | onboarding step 2 |
| Ask for a room | Ask the AI for a room | onboarding step 3 |
| Pick a mood | unchanged | onboarding step 4 |

## Also to update when this lands

- `docs/design/ui-spec.md` and the Devpost and storyboard text (`content`): I will sync them once `xr` has applied the names, so the docs match the shipped panel.
- Tests that pin strings (for example `PlaceHint`, `Tabs`, `KeepRealLabel`) change with the apply PR.
- Word count: every button above is 1 to 3 words.

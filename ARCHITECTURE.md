# How Own Space works

This page is for people reading or changing the code. What the app does is in [README.md](README.md). The repo rules are in [AGENTS.md](AGENTS.md).

## The idea in one paragraph

Every rule lives in plain C# with no engine APIs (**Core**), behind shared types (**Contracts**). The scene code (**MR**, **Interaction**, **UI**) reads the room from MRUK, draws the result and turns hand input into calls on one `DesignSession`. So the rules run in EditMode tests, without a headset, and the scene can't change the design except through the session. That one path also gives one undo history and one save.

## Folder map: `app/Assets/_Project/`

| Folder | Owns | Key files |
| --- | --- | --- |
| [`Contracts/`](app/Assets/_Project/Contracts/) | Shared types, no engine references. Changed only together with their Fakes. | `IRoomInfo.cs` (walls, openings, furniture boxes), `DesignState.cs` (the saved design), `CatalogItem.cs`, `Assistant.cs` (plan types), `CannedPlans.cs` (AI styles), `OnboardingState.cs`, `Fakes/` (a dorm room, a catalog, an assistant, onboarding states) |
| [`Core/`](app/Assets/_Project/Core/) | Every rule, pure C# | `DesignSession.cs` (the design with undo and redo; every change goes through it), `Zones.cs`, `ZoneProposer.cs`, `FitRule.cs` (redesigns keep footprint and usable height), `PlacementRules.cs`, `HideRules.cs`, `Facing.cs`, `DesignStateJson.cs`, `AtomicFile.cs`, `SavedSpaces.cs`, `RoomSnapshot.cs`, `SnapshotRoomInfo.cs`, `Onboarding.cs` |
| `Core/` (AI) | The AI request, check and apply | `AssistantRunner.cs`, `AssistantRequestJson.cs`, `PlanParser.cs`, `PlanChecker.cs`, `SetLayout.cs`, `ViewComfort.cs`, `PresetSets.cs`, `ProxyDesignAssistant.cs`; `Core/Unity/` holds the only engine-side piece, the web transport |
| [`MR/`](app/Assets/_Project/MR/) | The room in the headset | `MrukRoomInfo.cs` (MRUK → `IRoomInfo`), `DesignStore.cs` + `RoomSaves.cs` (one session, saved per room), `ZoneSetup.cs`, `OutsideShell.cs` (virtual floor, walls, ceiling), `RedesignView.cs`, `RealTops.cs`, `PlacementView.cs`, `OutsidePlacer.cs`, `PlaceSuggest.cs`, `MoodApplier.cs`, `ToneMatchSettings.cs`, `VirtualExtent.cs` (Bigger room), `FocusMode.cs`, `SpaceVisit.cs`, `SampleRoom.cs`, `PanelHost.cs` and the tab pages, `OnboardingGuide.cs` + `GuideSteps.cs`, `XrRoot.prefab` |
| [`Interaction/`](app/Assets/_Project/Interaction/) | Hands | `HandRig.cs`, `AimSteady.cs`, `InputDebug.cs` |
| [`UI/`](app/Assets/_Project/UI/) | The glass tool panel | `ToolPanel*.cs`, `PanelLayout.cs`, `PanelText.cs`, `PanelThemes.cs`, `ToolPanel.prefab` |
| [`Content/`](app/Assets/_Project/Content/) | Data | `catalog.json` (furniture: role, size, usable height, styles, tags), `finishes.json`, `mood-presets.json`, `presets.json`, `CatalogPrefabs.asset`, `OpeningFrame.cs` (window and door trim) |
| `Art/`, `Audio/` | Models, materials, textures | Sources and licenses in [docs/design/asset-licenses.md](docs/design/asset-licenses.md) |
| `Diagnostics/` | The development performance overlay | `PerfOverlay.cs`, `DiagnosticsRoot.prefab` (destroyed in release builds) |
| `Editor/` | Batch build and validation | `Editor/Build/` |
| `Tests/` | EditMode tests for Core and Contracts | One file per rule or bug, for example `PlacementRules`, `PlanOneKindTests`, `OnboardingTests` |
| `Scenes/` | `Main.unity` | Each area adds one root prefab |

Each area owns its folders. Who may write where is in [AGENTS.md](AGENTS.md).

## Runtime flow

1. **Start.** `Main.unity` loads `XrRoot` (camera rig, passthrough, hands) and the area roots. Nothing is drawn over passthrough yet.
2. **Room.** MRUK loads the room scan. `MrukRoomInfo` turns it into an `IRoomInfo` (floor outline, walls, windows, doors, furniture boxes). A room counts only when the user's head is inside it. With no usable scan, the app offers the sample room.
3. **Design.** Once the room is settled, `DesignStore` loads that room's save (`design-<roomId>.json`), or starts an empty design, into its one `DesignSession`.
4. **Zone.** If the design has no zone, `ZoneSetup` shows one proposed by `ZoneProposer` around the desk or bed. The user moves its corners and confirms with Done.
5. **Drawing.** Each view rebuilds only when the room, the zone or the design changes, never per frame:
   - `OutsideShell` draws the virtual floor, walls and ceiling outside my zone, with holes for windows and doors.
   - `RedesignView` and `RealTops` draw the new designs over real furniture.
   - `PlacementView` draws the placed pieces.
   - `MoodApplier` blends the light and the passthrough tint.
6. **Input.** `HandRig` and the panel (`PanelHost`, the tab pages, `UI/ToolPanel`) turn pinches into session calls: `Redesign`, `TryAddPlacement`, `SetFinish`, `SetMood`, hide boxes, and AI apply. A refused change shows its reason as a toast.
7. **Save.** After every change, undo or redo, `DesignStore` writes the room's file atomically. Pause or lost focus resets passthrough and lights. Losing hand tracking cancels a drag.

## Data and saves

Everything is local JSON under `Application.persistentDataPath`, which is the app's own files folder on the Quest. Every write goes through `AtomicFile`: a temporary file, then a rename. A broken file is kept as `.bad`, and the app starts fresh.

| File | Holds |
| --- | --- |
| `design-<roomId>.json` | One room's design: zone, furniture choices, placed pieces, hide boxes, covers, finishes, mood. `<roomId>` is the MRUK room anchor, a fixed key without a scan, or `space-<id>` for a saved-space visit. |
| `design-<roomId>.json.adjust.json`, `.covers.json`, `.extent.json`, `.zone.json` | Per-room side files: fit fine-tuning, cover tweaks, the Bigger room outline, the zone's turn |
| `onboarding.json` | Guided first-run progress (step, chosen path, skipped). Done or skipped stays off. |
| `ui-prefs.json` | Panel theme and size |
| `spaces/<id>.json` | Saved spaces (at most 10): a room snapshot with its design, capped at 512 KB |

Nothing is uploaded. The only network call is the AI request below.

## The AI path

1. **Ask.** The AI tab calls `AssistantRunner` with typed text or a style chip. It snapshots the design and leaves out the last AI plan's pieces, because a new plan replaces them.
2. **Request** (`AssistantRequestJson`, clipped to the proxy's limits):
   - The task and intent.
   - The room: floor outline, my zone, doors and windows, furniture boxes, and the free floor outside the zone.
   - The current mood and finishes, and the pieces the user placed.
   - The catalog ids allowed: outside pieces that fit, never curtains, and a style's own set for a chip.
3. **Proxy.** `proxy/` checks the request's shape and calls Gemini with a response schema whose enums allow only the ids sent. The key exists only as a Worker secret.
4. **Check** (`PlanChecker`, off the main thread). Each piece is run through the same `PlacementRules` as a hand placement. A piece that breaks a rule is nudged, laid out on a grid, swapped for a smaller one of its kind, or skipped with a reason. Also:
   - One piece of each kind.
   - No kind the user already placed.
   - No window pieces.
   - Floor pieces face the user.
   - Tall pieces stay out of the seated user's view.
   - Set rules: seats around the table, rug under it, shelves on walls, plants in corners.
   The panel shows one short summary line, for example "Placed 4 of 5: you already have one".
5. **Apply.** One undo step removes the last AI plan's pieces, adds the new ones (marked `aiPlaced`), and sets the finishes and mood.
6. **Offline.** On a network error or a timeout, the runner uses the chosen style's built-in layout instead (`CannedPlans`, `PresetSets`). It goes through the same check and says so in the panel.

## Performance rules

- **72 Hz** on Quest 3 and 3S in the worst scene.
- **No per-frame garbage:** no `new`, LINQ, string building or `Find*` in `Update`. Rebuilds happen on change events. EditMode tests assert zero allocation for the per-frame core calls.
- **Few draw calls:** the shell is three draw calls, and the panel stays within its budget. The development overlay (`Diagnostics/`) shows FPS, frame times and GC per frame.
- **Zero compiler warnings.** CI treats warnings as errors.

## Known limits and things to know

- **Two overlapping MRUK rooms.** If the headset has scans of two rooms that overlap, MRUK can report the other one. The app uses only the room the user's head is inside, but at launch it may load one room's design and then switch. Code that reacts to design changes must not treat that switch as a user action.
- **Meta's system UI draws over the app.** The keyboard, system dialogs and virtual displays are drawn by the OS above the app's layers. The app can't place anything in front of them.
- **Bigger room covers real doors.** With Bigger room on, the virtual room grows past the real walls, so a real door in a wall that moved is behind virtual wall. The setting is off by default.
- **Real furniture outside my zone is hidden,** so virtual pieces may stand where it physically is. Only my-zone furniture blocks placement.
- **No real-object occlusion** in release builds: virtual pieces draw over real objects in front of them.
- **Prompt changes need a Worker redeploy.** `proxy/src/shape.ts` holds the system prompt; the app can't change it. Run `npx wrangler deploy` in `proxy/` after changing it. The app-side checks work without it.
- **Fakes are not a headset test.** The Fakes and the XR Simulator check logic and layout only. Anything about tracking, passthrough or comfort needs a real Quest.

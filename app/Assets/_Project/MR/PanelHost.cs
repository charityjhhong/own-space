using System;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.Interaction;
using OwnSpace.UI;
using TMPro;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Puts the tool panel to work (spec v4): the four tabs Style, Furniture, AI Designer and Tidy up on top of
    /// the existing tools, opened once the zone is confirmed, plus the glue between them:
    /// <list type="bullet">
    /// <item>The panel opens on Style. A page's data change re-describes the panel; selecting a real box switches
    /// to Furniture unless the user is pointing at or pressing the panel.</item>
    /// <item>An armed furniture card shows the hint "Pinch the floor to place". Leaving the tab or minimizing
    /// drops the card.</item>
    /// <item>Every refusal (placing, redesigning, hiding) is a red toast. Undo and redo are on the top strip and
    /// in the toast; the gear switches Light / Dark, saved in the UI prefs.</item>
    /// </list>
    /// Walls pull the panel toward the user (<see cref="RoomReach"/>).
    /// </summary>
    public sealed class PanelHost : MonoBehaviour
    {
        // Restyling real furniture and adding virtual pieces are separate tabs, so the two jobs never mix (device day:
        // a desk card placed a desk outside instead of restyling the real desk). Short names: five fit one row.
        public static readonly string[] Tabs = { "Style", "My room", "Add", "AI", "Tidy up" };
        public const int StyleTab = 0, MyTab = 1, AddTab = 2, AiTab = 3, TidyTab = 4;
        public const string PlaceHint = "Pinch the floor outside your zone to place";

        /// <summary>The pending banner while a piece is armed (built on arming only): the action, the piece and Cancel.</summary>
        public static string PlaceBanner(string pieceName) =>
            string.IsNullOrEmpty(pieceName) ? PlaceHint + " · Cancel" : PlaceHint + " " + pieceName + " · Cancel";

        /// <summary>The banner for a wall piece (device batch 10: frames hang on walls, not the floor).</summary>
        public static string HangBanner(string pieceName) => "Pinch a wall to hang " + pieceName + " · Cancel";

        [SerializeField] ToolPanel _panel;
        [SerializeField] DesignStore _store;
        [SerializeField] MrukRoomInfo _room;
        [SerializeField] ZoneSetup _zone;
        [SerializeField] EditMode _edit;
        [SerializeField] OutsidePlacer _placer;
        [SerializeField] AskMenu _ask;
        [SerializeField] FreeTextRequest _freeText;
        [SerializeField] HideTool _hide;
        [SerializeField] TextAsset _finishes, _moods, _thumbnails;
        [SerializeField] TMP_FontAsset _font;

        FurniturePage _mine, _furniture; // My room (restyle real pieces), Add (place outside)
        AiDesignerPage _ai;
        TidyUpPage _tidy;
        SettingsPage _settings;
        SpacesPage _spaces;   // Saved spaces: the chooser
        SpaceVisit _visit;
        SavedSpaces _saved;
        MoodApplier _mood;
        IGuideSteps _guide;     // the guided first run (one step at a time, then all five tabs): core's onboarding
        GuidePage _guidePage;
        string _prefsPath;

        public ToolPanel Panel => _panel;

        // Every in-room label (refusals, names, hints) draws in this panel's look (device batch 10: no raw world text).
        void Awake() => WorldLabel.UsePanel(_panel);

        void Start()
        {
            _prefsPath = UiPrefs.DefaultPath;
            var style = new StylePage(_store.Session, _store.NoteUserChange, StylePage.Finishes(Text(_finishes), "floor"),
                StylePage.Finishes(Text(_finishes), "wall"), StylePage.Moods(Text(_moods))) { NoteSteps = _store.NoteUserChange };
            var thumbs = new FurnitureThumbs(Text(_thumbnails));
            _mine = new FurniturePage(_store.Session.Catalog, _placer, _edit, thumbs, _font, FurniturePage.Job.MyFurniture);
            _furniture = new FurniturePage(_store.Session.Catalog, _placer, _edit, thumbs, _font, FurniturePage.Job.AddFurniture);
            float inner = _panel.Metrics.Size.x - 2f * _panel.Metrics.Padding;
            _ai = new AiDesignerPage(_ask, _freeText, inner, _font);
            _tidy = new TidyUpPage(_hide, _font, thumbs);
            _panel.Configure(Tabs, new IPanelPage[] { style, _mine, _furniture, _ai, _tidy });
            _settings = new SettingsPage(_font)
            {
                IsDark = () => _panel.Theme == PanelTheme.Dark,
                SetDark = dark => _panel.SetTheme(dark ? PanelTheme.Dark : PanelTheme.Light),
                Rescan = Rescan,
                Reset = ResetDesign,
                Close = CloseSettings,
                EditZone = EditZoneFromSettings,
                OpenSpaces = OpenSpaces,
                CurrentSpace = () => _visit?.Current ?? SpacesPage.ThisRoom,
            };
            _spaces = new SpacesPage(_font)
            {
                Go = GoToSpace, Close = CloseSpaces, Save = SaveSpace, Delete = DeleteSpace, Changed = _panel.Invalidate,
                TypeName = () => _freeText.OpenForName("", SaveSpace),
                Rename = (id, name) => _freeText.OpenForName(name, typed => RenameSpace(id, typed)),
            };
            _saved = new SavedSpaces(System.IO.Path.Combine(Application.persistentDataPath, "spaces"));
            _rig = FindAnyObjectByType<OwnSpace.Interaction.HandRig>(); // once
            if (_rig != null)
            {
                // A floor action armed or active: a hand pointing down at the floor aims (log 2dc475d). Resting = just above a desk or bed top.
                _rig.FloorAction = () => _placer.Armed != null || _placer.SelectedName != null || _hide.ArmedToDraw
                    || _zone.Current == ZoneSetup.Phase.Editing || _edit.Selected != null;
                var surfaces = new RoomSurfaces(_room, _store);
                _rig.RestSurface = p => surfaces.TopUnder(p, out float top) && OnRestSurface(p.y, top);
                // Never seat the panel from a headset lying on the desk (device, e3f28ec: launched by adb with the headset
                // off, head 0.75 m up): wait until it is worn and tracked.
                _panel.HeadReady = () => HeadValid(_rig.HeadTracked, Application.isEditor || (OVRManager.instance != null && OVRManager.instance.isUserPresent),
                    _rig.HeadPosition.y - _room.FloorY, _room.HasScan);
            }
            OVRManager.HMDMounted += OnWake;
            OVRManager.InputFocusAcquired += OnWake;
            _visit = new SpaceVisit(_room, _store, _edit, _rig);
            _room.Changed += OnRoomFloor;
            _zone.SampleRequested += () => GoToSpace(SpacesPage.SampleId);
            _furniture.NoOutsideSpace = () => ZoneFit.OutsideFreeArea(_room.FloorOutline, _store.Session.State.zoneOutline, _room.Furniture) < ZoneFit.MinOutsideArea;
            PanelIcons icons = _panel.Icons;
            _furniture.PrevIcon = _mine.PrevIcon = icons.Rect("chevron-left");
            _furniture.NextIcon = _mine.NextIcon = icons.Rect("chevron-right");
            _tidy.CheckIcon = icons.Rect("check");
            _panel.Invalidate(); // re-describe with the icons (the pager's chevrons, the checks)
            _panel.Reach = new RoomReach(_room);
            _panel.Surfaces = new RoomSurfaces(_room, _store); // never into the desk (device day, 6c6b59a)
            _zone.UseCard(_panel); // the zone step's instruction and buttons in the panel's glass look
            _placer.UseChip(_panel); // hovered pieces are named in the panel's look too
            _edit.UseHandle(_panel); // redesigned real pieces turn by the same corner handle
            _hide.UseHandle(_panel); // and so do Tidy up covers
            _freeText.UsePanel(_panel); // the AI request's keyboard docks under the panel
            _freeText.Failed += Refused;
            _mood = FindAnyObjectByType<MoodApplier>(); // once: the passthrough check after the zone step and Reset
            // Real occlusion is off by default (the human): release builds never run it (no row, no depth); development
            // builds keep a saved experiment switch under Design, default off. A redesigned desk or bed keeps its real top
            // instead (RealTops).
            RealOcclusion occlusion = FindAnyObjectByType<RealOcclusion>(); // once
            if (occlusion != null)
            {
                // Always off and never shown, development builds too (the human, 26a0806: it flickers and makes them dizzy).
                // The code stays; ShowOcclusionSwitch brings the switch back.
                occlusion.UserEnabled = ShowOcclusionSwitch && RealOcclusion.EnabledFor(Debug.isDebugBuild, UiPrefs.LoadOcclusion(_prefsPath));
                if (ShowOcclusionSwitch && Debug.isDebugBuild)
                {
                    _settings.IsOcclusionOn = () => occlusion.UserEnabled;
                    // One state for the label and the log: Starting… while it still asks, Not available once it gave up.
                    _settings.OcclusionAvailable = () => occlusion.State != RealOcclusion.DepthState.Unsupported;
                    _settings.OcclusionStarting = () => occlusion.State == RealOcclusion.DepthState.Starting;
                    _settings.SetOcclusion = on => { occlusion.UserEnabled = on; UiPrefs.SaveOcclusion(_prefsPath, on); _panel.Invalidate(); };
                }
            }
            // Settings › Room › Bigger room (VirtualExtent): saved, default off; a change re-applies at once.
            VirtualExtent.SetEnabled(UiPrefs.LoadBiggerRoom(_prefsPath));
            _settings.IsBiggerRoom = () => VirtualExtent.Enabled;
            _settings.SetBiggerRoom = on => { VirtualExtent.SetEnabled(on); UiPrefs.SaveBiggerRoom(_prefsPath, on); _panel.Invalidate(); };
            _panel.SetTheme(UiPrefs.LoadTheme(_prefsPath) == UiPrefs.Dark ? PanelTheme.Dark : PanelTheme.Light);
            _panel.SetScale(UiPrefs.LoadScale(_prefsPath));
            _panel.ScaleChanged += SaveScale;

            _furniture.Changed = _mine.Changed = _panel.Invalidate;
            // The guided first run: until it is finished or skipped, the panel opens on it with the tab row hidden.
            // Core's onboarding, loaded and saved by OnboardingGuide (onboarding.json); without one, or with
            // OnboardingGuide.GuideEnabled off (the human: "no tutorial"), the guide is off: all five tabs.
            OnboardingGuide onboarding = FindAnyObjectByType<OnboardingGuide>(); // once
            Onboarding steps = onboarding != null ? onboarding.Onboarding : null;
            if (steps == null) { steps = new Onboarding(); steps.Skip(); }
            _guide = new OnboardingSteps(steps, () => _store.Session.State, () => _room.HasScan || _room.Visiting != null);
            _guidePage = new GuidePage(_guide, _ai, _mine, style, _furniture, _font) { Changed = OnGuideChanged };
            _freeText.AiPageShown = () => _panel.Sheet == null ? _panel.CurrentTab == AiTab : _panel.Sheet == _guidePage && _guidePage.Inner == _ai;
            OnboardingGuide.GuideTarget = GuideTarget;
            _ai.Changed = _panel.Invalidate;
            _tidy.Changed = _panel.Invalidate;
            _furniture.Refused = _mine.Refused = Refused;
            _store.Session.Changed += _panel.Invalidate;
            _edit.SelectionChanged += OnSelection;
            _placer.ArmedChanged += OnArmed;
            _placer.SelectionChanged += OnPieceSelected;
            _panel.CardDraggedOut += _placer.BeginDragPlace; // drag a card off the Add tab onto the floor
            _hide.Changed += OnHideChanged;
            _placer.Refusal += Refused;
            _hide.Refusal += Refused;
            _panel.TabChanged += OnTab;
            _edit.OutlinesShown = _panel.CurrentTab == MyTab;
            if (Debug.isDebugBuild)
            {
                // Where the frame time goes, on device (FPS 65 after "You're in Dorm"): CPU main, render and GPU, plus what shows.
                RealOcclusion occ = FindAnyObjectByType<RealOcclusion>(); // once
                gameObject.AddComponent<FrameTimeLog>().Context = () =>
                    $"tab {_panel.CurrentTab}, sheet {(_panel.Sheet != null)}, visit {_visit?.Current ?? "none"}, occlusion {(occ != null ? occ.State.ToString() : "none")}, " +
                    $"placed {_store.Session.State.placements.Count}, redesigned {_store.Session.State.anchorChoices.Count}";
            }
            _panel.HintPressed += CancelPending;
            _panel.MinimizedChanged += OnMinimized;
            _panel.UndoPressed += Undo;
            _panel.ToastPressed += Undo;
            _panel.RedoPressed += Redo;
            _panel.CanUndo = () => _store.CanUndo; // grey when there is nothing to undo (device: pinches did nothing)
            _panel.CanRedo = () => _store.CanRedo;
            _store.HistoryChanged += _panel.Invalidate;
            _panel.SettingsPressed += ToggleSettings;
            // Focus (the strip's eye button): the tools hide, the room stays; FocusMode's exit chip brings them back.
            _focus = gameObject.AddComponent<FocusMode>();
            _focus.Use(_rig, _room, ZoneDesk);
            _panel.FocusPressed += EnterFocus;
            FocusMode.Changed += OnFocus;
            _panel.ThemeChanged += SaveTheme;
            _zone.Confirmed += Show;
            _zone.EditStarted += Hide;
            if (_zone.Current == ZoneSetup.Phase.Confirmed) Show();
            else _panel.Open(false);
        }

        // The floor level moved (the tracking origin changed at launch: floor y 0.01, 0.46, 0.78 in three runs): the panel
        // goes back on its seat against the new head, unless the user placed it and it is still in view.
        float _floorSeen = float.NaN;
        void OnRoomFloor()
        {
            if (_room == null || !_room.HasScan) return;
            if (FloorMoved(_floorSeen, _room.FloorY)) _panel.Reseat($"the floor level moved ({_room.FloorY:F2})");
            _floorSeen = _room.FloorY;
        }

        /// <summary>Whether the floor level moved enough to re-seat (pure): the first reading, or more than 5 cm.</summary>
        public static bool FloorMoved(float before, float now) => float.IsNaN(before) || Mathf.Abs(now - before) > 0.05f;

        void OnDestroy()
        {
            if (OnboardingGuide.GuideTarget == (System.Func<Vector3?>)GuideTarget) OnboardingGuide.GuideTarget = null;
            if (_room != null) _room.Changed -= OnRoomFloor;
            if (_store != null && _store.Session != null && _panel != null) _store.Session.Changed -= _panel.Invalidate;
            if (_store != null && _panel != null) _store.HistoryChanged -= _panel.Invalidate;
            if (_edit != null) _edit.SelectionChanged -= OnSelection;
            if (_placer != null) { _placer.ArmedChanged -= OnArmed; _placer.Refusal -= Refused; _placer.SelectionChanged -= OnPieceSelected; }
            if (_hide != null) { _hide.Refusal -= Refused; _hide.Changed -= OnHideChanged; }
            if (_zone != null) { _zone.Confirmed -= Show; _zone.EditStarted -= Hide; }
            if (_panel != null) _panel.FocusPressed -= EnterFocus;
            OVRManager.HMDMounted -= OnWake;
            OVRManager.InputFocusAcquired -= OnWake;
            FocusMode.Changed -= OnFocus;
            _ai?.Dispose();
            _tidy?.Dispose();
        }

        static string Text(TextAsset asset) => asset != null ? asset.text : null;

        FocusMode _focus;

        // Focus on (the strip's eye button): nothing pending, nothing selected, no sheet; FocusMode hides the tools.
        void EnterFocus()
        {
            CancelPending();
            _edit.Deselect();
            _placer.Deselect();
            _panel.CloseSheet();
            _focus.Enter(_panel.FocusButtonPosition); // the exit chip appears where the eye button was
        }

        bool _panelWasShown;

        // The tools hide (Focus, or system UI over the app) and come back as they were: the panel where it was, minimized
        // or not, with its sheet; nothing else changes (the room, furniture, mood and passthrough keep rendering).
        void OnFocus(bool hidden)
        {
            if (hidden) { _panelWasShown = _panel.gameObject.activeSelf; if (_panelWasShown) _panel.gameObject.SetActive(false); }
            else if (_panelWasShown) _panel.gameObject.SetActive(true);
            if (!hidden && _wakePending) Wake();
        }

        /// <summary>A worn head this low (m above the floor) is still a head: seated low, or reclined on a bed.</summary>
        public const float MinHeadHeight = 0.6f;

        /// <summary>
        /// Whether the head can seat the panel (pure): tracked, the headset worn, and (with a floor) at least
        /// <see cref="MinHeadHeight"/> above it.
        /// </summary>
        public static bool HeadValid(bool tracked, bool worn, float aboveFloor, bool hasFloor) =>
            tracked && worn && (!hasFloor || aboveFloor >= MinHeadHeight);

        bool _wakePending;

        // The headset put on, or the app given the input back: the panel re-seats in front of the user (unless they placed
        // it and it is in view). While the panel is hidden (system UI), it waits until it shows again.
        void OnWake()
        {
            _wakePending = true;
            if (_panel != null && _panel.gameObject.activeInHierarchy) Wake();
        }

        void Wake()
        {
            _wakePending = false;
            _panel.Reseat("waking (the headset put on, or the input back)");
        }

        // The desk or bed in my zone (the first), for placing the exit chip beside it; null without one.
        FurnitureAnchor ZoneDesk()
        {
            var furniture = _room.Furniture;
            for (int i = 0; i < furniture.Count; i++)
                if ((furniture[i].Role == FurnitureRole.Table || furniture[i].Role == FurnitureRole.Bed) && _store.Session.IsInMyZone(furniture[i])) return furniture[i];
            return null;
        }

        // The panel opens on Style after the zone step. A room reload (a visit, a late scan) confirms the zone again while
        // the panel is open: then the panel stays where it is, on its tab or sheet (device: a space change landed on Style).
        /// <summary>The real-occlusion switch in Settings (development builds): off, never shown.</summary>
        public static readonly bool ShowOcclusionSwitch = false;

        void Show()
        {
            if (!FocusMode.UserFocus && ReopensOnConfirm(_panel.gameObject.activeSelf)) // a room reload keeps the user's Focus
            {
                // Device, bece6e8: launched with the headset on the desk, the zone confirmed while system UI had the input,
                // and the panel never opened. Now it waits for the input and a valid head, then opens (Update).
                if (OpenWaits(FocusMode.SystemHasInput, HeadReadyNow())) { WaitToOpen(); return; }
                OpenOnStyle();
            }
            if (_mood != null) _mood.EnsurePassthrough("zone done");
        }

        bool _openPending, _waitLogged;

        /// <summary>
        /// Whether a wanted panel waits instead of opening (pure): while system UI has the input, or the head cannot seat it
        /// (a headset lying on the desk, untracked, not worn).
        /// </summary>
        public static bool OpenWaits(bool systemHasInput, bool headReady) => systemHasInput || !headReady;

        // On the headset only: the Editor (the XR Simulator, batch tests with the hands driven) opens at once, as before.
        bool HeadReadyNow() => Application.isEditor || _panel.HeadReady == null || _panel.HeadReady();

        void WaitToOpen()
        {
            _openPending = true;
            if (_mood != null) _mood.EnsurePassthrough("zone done");
            if (_waitLogged) return;
            _waitLogged = true; // once per wait (allocates the line once)
            string head = _rig == null ? "no rig" : $"y={_rig.HeadPosition.y - _room.FloorY:F2} m above the floor, tracked={_rig.HeadTracked}";
            bool present = Application.isEditor || (OVRManager.instance != null && OVRManager.instance.isUserPresent);
            InputDebug.Log($"panel: waiting for a valid head ({head}, present={present}, system input={FocusMode.SystemHasInput})");
        }

        // A wanted panel opens on its seat as soon as the app has the input and the head is valid. Only a bool while nothing waits.
        void Update()
        {
            if (!_openPending || OpenWaits(FocusMode.SystemHasInput, HeadReadyNow())) return;
            _openPending = _waitLogged = false;
            if (FocusMode.UserFocus || !ReopensOnConfirm(_panel.gameObject.activeSelf)) return;
            OpenOnStyle();
            InputDebug.Log("panel: opened after the head became valid");
        }

        void OpenOnStyle()
        {
            _panel.Open(true);
            _panel.SelectTab(StyleTab);
            // Core's onboarding has passed the scan and the zone by now (it watches the design); the guide picks up there.
            if (_guide.Active) ShowGuide();
            // Edit my zone from Settings: Done or Cancel goes back to Settings (the human: "I can't get out").
            if (_zone.ConfirmedFromSettings) BackToSettings(_zone.Cancelled ? SettingsExit.ZoneCancel : SettingsExit.ZoneDone);
        }


        /// <summary>Whether a confirmed zone opens the panel afresh (on its seat, on Style): only when it is closed. Pure, for tests.</summary>
        public static bool ReopensOnConfirm(bool panelOpen) => !panelOpen;
        void Hide() { _openPending = false; _placer.Disarm(); _panel.Open(false); if (_mood != null) _mood.EnsurePassthrough("zone edit"); }

        // A real box selected in my zone: Furniture shows the designs that fit it, unless the user is using the
        // panel (pointing at it or pressing it): then the tab they are on stays.
        void OnSelection()
        {
            _furniture.Refresh();
            _mine.Refresh();
            if (_edit.Selected != null && SelectionSwitchesTab(_panel.Sheet != null, _panel.Interacting)) _panel.SelectTab(MyTab);
            _panel.Invalidate();
        }

        void OnArmed()
        {
            if (_placer.Armed != null) _hide.Disarm(); // placing owns the floor pinch
            RefreshBanner();
            _panel.Invalidate();
        }

        // One floor owner at a time (device batch 4: an armed "draw a box" left on from Tidy up took the floor pinch
        // meant to place a sofa). Each tab's floor action ends when its tab is left or the panel is minimized, and
        // arming one drops the other.
        void OnTab(int tab)
        {
            _edit.OutlinesShown = tab == MyTab; // the box outlines belong to My room
            if (tab != AddTab) _placer.Disarm(); // only Add places on the floor
            if (tab == MyTab && _edit.Selected == null) _edit.SelectNearest(); // opens on the nearest real piece, the desk first
            if (tab != TidyTab) _hide.Disarm();
        }

        void OnMinimized(bool minimized) { if (minimized) { _placer.Disarm(); _hide.Disarm(); } }

        void CancelPending() { _placer.Disarm(); _hide.Disarm(); }

        // The pending action's dark banner above the panel, while a piece is armed or "Draw a box" waits for the floor;
        // pinching it cancels. It goes when the action is done or dropped (tab change, minimize, Cancel).
        void RefreshBanner()
        {
            if (_placer.Armed != null)
                _panel.ShowHint(PlacementRules.IsWallMounted(_placer.Armed) ? HangBanner(_placer.Armed.name) : PlaceBanner(_placer.Armed.name), pressable: true);
            else if (_hide.ArmedToDraw) _panel.ShowHint(TidyUpPage.DrawBanner, pressable: true);
            else _panel.HideHint();
        }

        // A placed piece selected in the room: the Add tab shows its card (unless the user is on the panel).
        void OnPieceSelected()
        {
            if (_placer.SelectedName != null && SelectionSwitchesTab(_panel.Sheet != null, _panel.Interacting)) _panel.SelectTab(AddTab);
            _panel.Invalidate();
        }

        void OnHideChanged() { if (_hide.ArmedToDraw && _placer.Armed != null) _placer.Disarm(); RefreshBanner(); }

        void Refused(string reason) { if (!string.IsNullOrEmpty(reason)) _panel.ShowToast(reason, refused: true); }

        void Undo() { if (_store.Undo()) _panel.ShowToast("Undone"); }
        void Redo() { if (_store.Redo()) _panel.ShowToast("Redone"); }

        // The gear opens the settings sheet in place of the tab's page; again (or Close, or any tab) closes it.
        void ToggleSettings()
        {
            _settings.Rewind();
            if (_panel.Sheet == _settings) { if (_guide.Active) ShowGuide(); else _panel.CloseSheet(); }
            else { _placer.Disarm(); _hide.Disarm(); _panel.ShowSheet(_settings); }
        }

        // ---- Saved spaces ----

        OwnSpace.Interaction.HandRig _rig;

        void OpenSpaces()
        {
            RefreshSpaces();
            _settings.Rewind();
            _panel.ShowSheet(_spaces);
        }

        void CloseSpaces() => BackToSettings(SettingsExit.SpacesClosed); // the chooser opens from Settings only

        // The list, built on open and after a save, rename or delete only.
        void RefreshSpaces()
        {
            var saved = new System.Collections.Generic.List<(string id, string name, string date)>();
            foreach (SavedSpaceInfo s in _saved.List()) saved.Add((s.Id, s.Name, SpacesPage.ShortDate(s.CreatedUtc)));
            _spaces.Refresh(_room.HasScan && _room.Visiting == null, saved, _visit.CurrentId ?? SpacesPage.ThisRoomId, offerSample: !_visit.RealRoomHasScan);
        }

        // Save this room: core captures the room and its design, anchored to the desk or bed the user is at.
        bool SaveSpace(string name)
        {
            Vector3 head = _rig != null ? _rig.HeadPosition : Vector3.zero;
            RoomSnapshot snap = null;
            string error = "visiting a saved space";
            // The real room, never the extended virtual one (review of #292: real windows would float in a later visit).
            if (_room.Visiting != null || !RoomSnapshot.TryCapture(new RealRoomView(_room), _store.Session.State, name, System.DateTime.UtcNow,
                    new Vec3(head.x, head.y, head.z), out snap, out error))
            {
                Refused("This room can't be saved (it needs a room scan)");
                Debug.Log($"[OwnSpace.MR] Space not saved: {error}.");
                return false;
            }
            switch (_saved.Save(snap))
            {
                case SaveSpaceResult.Saved:
                    _panel.ShowToast("Saved as " + snap.name);
                    RefreshSpaces();
                    return true;
                case SaveSpaceResult.Full: Refused("Up to " + SavedSpaces.MaxSpaces + " spaces: delete one first"); return false;
                default: Refused("This room couldn't be saved"); return false;
            }
        }

        bool RenameSpace(string id, string name)
        {
            if (!_saved.Rename(id, name)) { Refused("Couldn't rename it"); return false; }
            RefreshSpaces();
            return true;
        }

        bool DeleteSpace(string id)
        {
            if (!_saved.Delete(id)) { Refused("Couldn't delete it"); return false; }
            _panel.ShowToast("Deleted");
            RefreshSpaces();
            return true;
        }

        bool GoToSpace(string id)
        {
            if (id == SpacesPage.ThisRoomId)
            {
                _visit.Leave();
                BackFromSpaces();
                _panel.ShowToast("Back in this room");
                return true;
            }
            RoomSnapshot snap = null;
            if (id == SpacesPage.SampleId) snap = SampleRoom.Build();
            else if (!_saved.TryLoad(id, out snap)) { Refused("That space could not be opened"); return false; }
            if (!_visit.GoTo(snap)) { Refused("Not ready yet: try again in a moment"); return false; }
            BackFromSpaces();
            _panel.ShowToast(_visit.ZoneRefitted ? "You're in " + snap.name + ": zone fitted to this desk" : "You're in " + snap.name);
            return true;
        }

        // The chooser was opened from Settings: back there, with "Current space" showing the new name (the human). The
        // first-run sample button calls GoToSpace with no sheet open, so nothing opens then.
        void BackFromSpaces()
        {
            if (_panel.Sheet != _spaces) return;
            BackToSettings(SettingsExit.SpacesPicked);
        }

        /// <summary>Every way out of a flow opened from Settings (the human: "anything entered from Settings must come back to Settings").</summary>
        public enum SettingsExit { SpacesPicked, SpacesSameSpace, SpacesClosed, ZoneDone, ZoneKeepAnyway, ZoneCancel, ZoneBarHold, ZoneRoomReload, Rescan, ResetConfirmed, ResetCancelled }

        /// <summary>Where each exit lands (pure, for the table test): the Settings sheet, always.</summary>
        public static bool EndsOnSettings(SettingsExit exit) => true;

        /// <summary>
        /// Whether a selection in the room may switch the panel's tab (pure): never while a sheet (Settings, Spaces) is open,
        /// or while the user is on the panel (device, build dfb22f4: back from Settings › Edit, a selection opened Add).
        /// </summary>
        public static bool SelectionSwitchesTab(bool sheetOpen, bool interacting) => !sheetOpen && !interacting;

        // Back on the Settings sheet, on the panel's seat (the panel never moves here).
        void BackToSettings(SettingsExit why)
        {
            if (!EndsOnSettings(why)) return;
            _settings.Rewind();
            _panel.ShowSheet(_settings);
            InputDebug.Log($"panel: back to Settings ({why})");
        }

        void CloseSettings() { _settings.Rewind(); if (_guide.Active) ShowGuide(); else _panel.CloseSheet(); }

        // ---- the guided first run ----

        void ShowGuide()
        {
            _panel.TabsHidden = true;
            _panel.ShowSheet(_guidePage);
            OnGuideChanged();
        }

        // A step, a choice, Back, Next, Finish or Skip setup. The page's own side effects follow the way shown, as a tab's
        // would: outlines on My room only, and only Add places on the floor. At the end: all five tabs, on Style.
        void OnGuideChanged()
        {
            if (!_guide.Active)
            {
                _placer.Disarm();
                _panel.TabsHidden = false;
                if (_panel.Sheet == _guidePage) _panel.CloseSheet();
                _panel.SelectTab(_guideEnd);
                _edit.OutlinesShown = _panel.CurrentTab == MyTab;
                InputDebug.Log("guide: over, all tabs");
                return;
            }
            IPanelPage shown = _guidePage.Inner;
            if (shown != _furniture) _placer.Disarm();
            _edit.OutlinesShown = shown == _mine;
            _guideEnd = shown == _furniture ? AddTab : shown == _ai ? AiTab : shown == _mine ? MyTab : StyleTab;
            if (shown == _mine) _mine.Refresh();
            InputDebug.Log($"guide: step {_guide.Step} ({_guide.Number}/{_guide.Total}), way {_guide.Branch}, page {_guideEnd}");
            _panel.Invalidate();
        }

        int _guideEnd = StyleTab; // the tab the guide ends on: the last page it showed

        /// <summary>Ends the guide without saving it (tests that drive the tabs).</summary>
        internal void EndGuideForTest()
        {
            if (_guide == null || !_guide.Active) return;
            _panel.TabsHidden = false;
            if (_panel.Sheet == _guidePage) _panel.CloseSheet();
            var over = new Onboarding();
            over.Skip(); // a fresh one, never saved
            _guide = new OnboardingSteps(over, () => _store.Session.State);
        }

        // The ghost hand points at the guide's next pinch: the first choice, else Next or Finish.
        Vector3? GuideTarget()
        {
            if (_guide == null || !_guide.Active || !_panel.isActiveAndEnabled || _panel.Minimized || _panel.Sheet != _guidePage) return null;
            int main = _guidePage.MainIndex;
            return main < 0 ? (Vector3?)null : _panel.CardPosition(main) - _panel.transform.forward * 0.02f;
        }

        void EditZoneFromSettings() { CloseSettings(); _zone.BeginEdit(cancellable: true); }

        bool Rescan()
        {
            if (!_room.RequestRescan()) { _panel.ShowToast("Space Setup runs on the headset", refused: true); return false; }
            BackToSettings(SettingsExit.Rescan); // Space Setup runs on top; back here, Settings is still open
            return true;
        }

        // Reset my design: this room's save is emptied and the zone step starts over; other rooms keep theirs.
        void ResetDesign()
        {
            BackToSettings(SettingsExit.ResetConfirmed); // the confirm row closes; Settings stays
            if (!_store.ResetRoom()) { _panel.ShowToast("Nothing to reset yet", refused: true); return; }
            _ask.ForgetPlan();
            _panel.ShowToast("Design reset"); // the zone and room stay; the panel stays open
            if (_mood != null) _mood.EnsurePassthrough("reset");
        }

        void SaveScale(float scale) => UiPrefs.SaveScale(_prefsPath, scale);

        void SaveTheme(PanelTheme theme) => UiPrefs.SaveTheme(_prefsPath, theme == PanelTheme.Dark ? UiPrefs.Dark : UiPrefs.Light);

        /// <summary>
        /// Furniture tops for the panel to stay above: every scanned box (kept real or not), raised to the redesigned
        /// piece on it when that piece is taller. Called on seat, drag and relayout only (no per-frame cost).
        /// </summary>
        /// <summary>A hand this high over a desk or bed top (m) is resting on or just above it (pure, for tests).</summary>
        public static bool OnRestSurface(float handY, float topY) => handY >= topY - 0.05f && handY <= topY + 0.25f;

        internal sealed class RoomSurfaces : IPanelSurfaces
        {
            readonly IRoomInfo _room;
            readonly DesignStore _store;
            public RoomSurfaces(IRoomInfo room, DesignStore store) { _room = room; _store = store; }

            public bool TopUnder(Vector3 point, out float top)
            {
                top = float.MinValue;
                var furniture = _room != null ? _room.Furniture : null;
                for (int i = 0; furniture != null && i < furniture.Count; i++)
                {
                    OrientedBox b = furniture[i].Box;
                    if (!Over(b, point)) continue;
                    float t = b.center.y + b.size.y * 0.5f;
                    if (_store != null && _store.Session != null)
                    {
                        AnchorChoice c = _store.Session.State.ChoiceFor(furniture[i].Id);
                        if (c.mode == AnchorMode.Redesign && _store.Session.Catalog != null && _store.Session.Catalog.TryGet(c.catalogItemId, out CatalogItem item))
                            t = Mathf.Max(t, _room.FloorY + item.size.y * OpeningGuard.OrOne(c.yScale));
                    }
                    top = Mathf.Max(top, t);
                }
                return top > float.MinValue;
            }

            /// <summary>Whether a point's floor position is over the box's footprint (pure, for tests).</summary>
            public static bool Over(OrientedBox b, Vector3 point)
            {
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, b.yawDegrees, 0f)) * new Vector3(point.x - b.center.x, 0f, point.z - b.center.z);
                return Mathf.Abs(local.x) <= b.size.x * 0.5f && Mathf.Abs(local.z) <= b.size.z * 0.5f;
            }
        }

        /// <summary>The wall pull for the panel: the room outline, through <see cref="FloatingRow.ReachInside"/>.</summary>
        sealed class RoomReach : IPanelReach
        {
            readonly MrukRoomInfo _room;
            public RoomReach(MrukRoomInfo room) => _room = room;
            public float Reach(Vector3 head, Vector3 forward, float halfWidth, float maxReach) =>
                _room != null && _room.HasScan ? FloatingRow.ReachInside(head, forward, halfWidth, _room.FloorOutline, maxReach) : maxReach;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace OwnSpace.MR.Tests
{
    /// <summary>
    /// The real XrRoot prefab in Play mode, clicked the way the Meta XR Simulator clicks: both hands start a pinch
    /// on the same frame and the aim is a ray toward a tab. HandRig is disabled so the test owns the hands; every
    /// other component runs its real Start/Update in its real execution order.
    /// </summary>
    public class PanelSimClickPlayTests
    {
        const string XrRootPath = "Assets/_Project/MR/XrRoot.prefab";
        GameObject _root, _camera;
        string _savePath;

        [UnityTest]
        public IEnumerator ClickingEachTabSelectsItAndClaimsThePinch()
        {
            _xrWasOn = SetXrStartOnPlay(false); // batch mode has no graphics device for an OpenXR session
            yield return new EnterPlayMode();
            yield return Spawn();
            HandRig hands = _root.GetComponentInChildren<HandRig>(true);
            ToolPanel panel = _root.GetComponentInChildren<ToolPanel>(true);
            ZoneSetup zone = _root.GetComponentInChildren<ZoneSetup>(true);
            Assert.AreEqual(ZoneSetup.Phase.Confirmed, zone.Current, "the zone should be confirmed");
            _root.GetComponentInChildren<PanelHost>(true).EndGuideForTest(); // this test drives the five tabs, not the first-run guide
            Assert.IsTrue(panel.isActiveAndEnabled, "the panel opens once the zone is confirmed");
            Assert.AreEqual(PanelHost.StyleTab, panel.CurrentTab, "the panel opens on Style");
            for (int k = 0; k < 2; k++) yield return null; // let the seat ease settle a frame or two

            int[] order = { PanelHost.AiTab, PanelHost.TidyTab, PanelHost.AddTab, PanelHost.MyTab, PanelHost.StyleTab };
            foreach (int tab in order)
            {
                Vector3 target = panel.TabPosition(tab);
                var aim = new Ray(target - panel.transform.forward * 0.6f, panel.transform.forward);
                hands.SetForTest(Click(true, false), Click(true, false), aim);
                yield return null; // the click frame: ToolPanel (-10) then EditMode (0) run
                Assert.IsTrue(hands.ClaimedThisFrame, $"the panel should claim the click on tab {tab}");
                hands.SetForTest(Click(false, false), Click(false, false), aim); // held
                yield return null;
                hands.SetForTest(Click(false, true), Click(false, true), aim); // released
                yield return null;
                Assert.AreEqual(tab, panel.CurrentTab, $"tab {tab} should be selected");
                hands.SetForTest(default, default, aim);
                yield return null;
            }
            yield return new ExitPlayMode();
        }

        // Device day #7: the first-run zone step reads at a glance: a labelled Done button near the view centre with
        // the instruction above it, the outline at desk height, and pinching Done saves the zone.
        [UnityTest]
        public IEnumerator ZoneStep_DoneIsALabelledButtonInView_AndPinchingItConfirms()
        {
            _xrWasOn = SetXrStartOnPlay(false);
            yield return new EnterPlayMode();
            yield return Spawn();
            HandRig hands = _root.GetComponentInChildren<HandRig>(true);
            ZoneSetup zone = _root.GetComponentInChildren<ZoneSetup>(true);
            zone.BeginEdit();
            yield return null;
            Assert.AreEqual(ZoneSetup.Phase.Editing, zone.Current);
            // Batch 7: the instruction and Done are a glass card in the panel's look, in front of the user.
            Transform card = zone.transform.Find("ZoneCard"), primary = card.Find("Primary"), text = card.Find("Text");
            Assert.IsTrue(card.gameObject.activeSelf);
            Assert.IsFalse(zone.transform.Find("ZoneDone").gameObject.activeSelf, "no 3D slab when the card is there");
            Assert.AreEqual("Done", primary.GetComponentInChildren<TMPro.TextMeshPro>().text);
            Assert.AreEqual(ZoneSetup.NoScanLine, text.GetComponentInChildren<TMPro.TextMeshPro>().text, "no scan in the test scene");
            Assert.AreEqual(ZoneSetup.ZoneMeaning, card.Find("Text2").GetComponentInChildren<TMPro.TextMeshPro>().text, "what the zone is, in one line");
            Assert.AreEqual(primary.GetComponentInChildren<MeshRenderer>().sharedMaterial.shader.name, "OwnSpace/PanelGlass");
            Ray view = hands.View;
            Vector3 to = primary.position - view.origin;
            Assert.Less(Vector3.Angle(view.direction, to), 50f, "docked low, still in view");
            Assert.Less(to.magnitude, 0.75f, "and within reach (device bug on a2c27d2: at the tool panel's own seat)");
            Assert.IsNull(zone.transform.Find("ZoneRail"), "the outline is on the floor only (a desk-height copy misled)");
            LineRenderer floor = zone.GetComponent<LineRenderer>();
            Assert.AreEqual(zone.Outline.Count, floor.positionCount);

            var gaze = new Ray(view.origin, to);
            hands.SetForTest(Click(true, false), Click(true, false), gaze);
            yield return null;
            hands.SetForTest(Click(false, true), Click(false, true), gaze);
            yield return null;
            Assert.AreEqual(ZoneSetup.Phase.Confirmed, zone.Current);
            Assert.IsFalse(card.gameObject.activeSelf, "the card goes with the step");
            yield return new ExitPlayMode();
        }

        // Device day #9: with no usable scan (none, or another room's) the zone step says how to get one.
        [Test]
        public void ZoneHint_WithoutAScan_PointsToSpaceSetup()
        {
            Assert.AreEqual(ZoneSetup.Hint, ZoneSetup.HintFor(true));
            StringAssert.Contains("Space Setup", ZoneSetup.HintFor(false));
            StringAssert.EndsWith(ZoneSetup.Hint, ZoneSetup.HintFor(false));
        }

        // Device batch 4: an armed card owns the floor pinch. "Draw a box" left armed on Tidy up took it before.
        [UnityTest]
        public IEnumerator ArmedPlacement_OwnsTheFloor_AndShowsADarkBannerWithThePieceAndCancel()
        {
            _xrWasOn = SetXrStartOnPlay(false);
            yield return new EnterPlayMode();
            yield return Spawn();
            ToolPanel panel = _root.GetComponentInChildren<ToolPanel>(true);
            HideTool hide = _root.GetComponentInChildren<HideTool>(true);
            OutsidePlacer placer = _root.GetComponentInChildren<OutsidePlacer>(true);
            DesignStore store = _root.GetComponentInChildren<DesignStore>(true);
            CatalogItem sofa = null;
            foreach (CatalogItem item in store.Session.Catalog.Items) if (item.kind == "sofa" && item.AllowedIn("outside")) { sofa = item; break; }
            Assert.IsNotNull(sofa);

            panel.SelectTab(PanelHost.TidyTab);
            hide.StartDrawing();
            Assert.IsTrue(hide.ArmedToDraw);
            panel.SelectTab(PanelHost.AddTab);
            Assert.IsFalse(hide.ArmedToDraw, "leaving Tidy up gives the floor back");

            hide.StartDrawing(); // even if it was armed again somehow
            placer.Arm(sofa);
            Assert.IsFalse(hide.ArmedToDraw, "arming a piece takes the floor from Draw a box");
            Assert.IsTrue(panel.HintShown);
            Assert.AreEqual(PanelHost.PlaceBanner(sofa.name), panel.Element(ToolPanel.Hint).Text.text);
            StringAssert.EndsWith("· Cancel", PanelHost.PlaceBanner(sofa.name));
            Assert.Greater(panel.HintCentreY, panel.Metrics.Size.y * 0.5f, "above the panel, always in view");

            hide.StartDrawing();
            Assert.IsNull(placer.Armed, "and the other way round");
            Assert.AreEqual(TidyUpPage.DrawBanner, panel.Element(ToolPanel.Hint).Text.text, "batch 9: armed drawing has its banner too");
            panel.BeginPress(ToolPanel.Hint, false);
            panel.Release(ToolPanel.Hint);
            Assert.IsFalse(hide.ArmedToDraw, "pinching it cancels the draw");
            Assert.IsFalse(panel.HintShown);
            placer.Arm(sofa);
            panel.Release(-1); // no press: nothing
            panel.BeginPress(ToolPanel.Hint, false);
            panel.Release(ToolPanel.Hint);
            Assert.IsNull(placer.Armed, "pinching the banner cancels");
            Assert.IsFalse(panel.HintShown);
            yield return new ExitPlayMode();
        }

        static HandState Click(bool started, bool ended) => new HandState
        {
            Tracked = true,
            Pinching = !ended,
            PinchStarted = started,
            PinchEnded = ended,
            PinchPoint = new Vector3(0.15f, -0.35f, 0.25f), // a resting simulated hand, nowhere near the panel
        };

        IEnumerator Spawn()
        {
            _camera = new GameObject("TestHead", typeof(Camera)) { tag = "MainCamera" };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRootPath);
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            _root = Object.Instantiate(prefab, holder.transform);
            DesignStore store = _root.GetComponentInChildren<DesignStore>(true);
            _savePath = Path.Combine(Application.persistentDataPath, "test-panel-click-" + System.Guid.NewGuid().ToString("N") + ".json");
            typeof(DesignStore).GetField("_fileName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(store, Path.GetFileName(_savePath));
            HandRig hands = _root.GetComponentInChildren<HandRig>(true);
            hands.enabled = false; // the test owns the hands
            _root.transform.SetParent(null);
            Object.Destroy(holder);
            yield return null; // Awake, Start
            store.Session.SetZone(new List<Vec2> { new Vec2(-2f, -2f), new Vec2(2f, -2f), new Vec2(2f, 3f), new Vec2(-2f, 3f) });
            ZoneSetup zone = _root.GetComponentInChildren<ZoneSetup>(true);
            typeof(ZoneSetup).GetMethod("OnRoomChanged", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(zone, null);
            yield return null;
        }

        bool? _xrWasOn;

        // XRGeneralSettings.InitManagerOnStart for Standalone, through reflection (no XR Management reference needed).
        internal static bool? SetXrStartOnPlay(bool on)
        {
            System.Type perTarget = System.Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
            object settings = perTarget?.GetMethod("XRGeneralSettingsForBuildTarget", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new object[] { BuildTargetGroup.Standalone });
            PropertyInfo init = settings?.GetType().GetProperty("InitManagerOnStart");
            if (init == null) return null;
            bool was = (bool)init.GetValue(settings);
            init.SetValue(settings, on);
            return was;
        }

        [TearDown]
        public void TearDown()
        {
            if (_xrWasOn.HasValue) { SetXrStartOnPlay(_xrWasOn.Value); _xrWasOn = null; }
            if (_root != null) Object.DestroyImmediate(_root);
            if (_camera != null) Object.DestroyImmediate(_camera);
            if (_savePath != null)
            {
                string noScan = Path.Combine(Path.GetDirectoryName(_savePath), RoomSaves.FileName(Path.GetFileName(_savePath), RoomSaves.NoScanKey));
                if (File.Exists(_savePath)) File.Delete(_savePath);
                if (File.Exists(noScan)) File.Delete(noScan);
            }
        }
    }
}

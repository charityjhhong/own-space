using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using UnityEngine.TestTools.Constraints;

namespace OwnSpace.Tests
{
    /// <summary>
    /// The guided first run: scan → my zone → an AI style or a my-zone redesign → the outside → all tabs.
    /// </summary>
    public class OnboardingTests
    {
        Onboarding _flow;
        DesignState _design;
        List<OnboardingStep> _steps;
        int _ended;

        [SetUp]
        public void SetUp()
        {
            _flow = new Onboarding();
            _design = new DesignState();
            _steps = new List<OnboardingStep>();
            _ended = 0;
            _flow.StepChanged += s => _steps.Add(s);
            _flow.Ended += () => _ended++;
        }

        void Redesign() => _design.SetChoice(new AnchorChoice { anchorId = "a-desk", mode = AnchorMode.Redesign, catalogItemId = "d", yScale = 1f });
        void Place(bool ai = false) => _design.placements.Add(new FreePlacement { placementId = "p" + _design.placements.Count, catalogItemId = "sofa", aiPlaced = ai });
        void Observe() => _flow.Observe(_design, true);

        void ToBranchPage()
        {
            _design.zoneOutline = FakeRoomInfo.HalfZone("a");
            Observe();
            Assert.AreEqual(OnboardingStep.RedesignOne, _flow.Step);
            Redesign();
            Observe();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step);
        }

        [Test]
        public void ByHandPath_ScanZoneRedesignChooseOutside_ThenAllTabs()
        {
            Assert.IsTrue(_flow.IsActive);
            _flow.Observe(_design, false);
            Assert.AreEqual(OnboardingStep.Scan, _flow.Step, "no scan and no sample room yet");
            Observe();
            Assert.AreEqual(OnboardingStep.SetZone, _flow.Step);
            _design.zoneOutline = FakeRoomInfo.HalfZone("a");
            Observe();
            Assert.AreEqual(OnboardingStep.RedesignOne, _flow.Step, "my-zone furniture comes before the outside, always");
            Redesign();
            Observe();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step, "the branch page waits for a choice");

            _flow.Choose(OnboardingBranch.ByHand);
            Assert.AreEqual(OnboardingStep.Outside, _flow.Step);
            _design.outsideFinish = new ZoneFinish { floorFinishId = "floor-oak-planks", wallFinishId = "" };
            Observe();

            Assert.AreEqual(OnboardingStep.Done, _flow.Step);
            Assert.IsFalse(_flow.IsActive);
            Assert.AreEqual(OnboardingBranch.ByHand, _flow.Branch);
            CollectionAssert.AreEqual(new[] { OnboardingStep.SetZone, OnboardingStep.RedesignOne, OnboardingStep.ChooseBranch, OnboardingStep.Outside, OnboardingStep.Done }, _steps);
            Assert.AreEqual(1, _ended);
        }

        [Test]
        public void AiPath_AnAppliedStyle_EndsTheGuide()
        {
            ToBranchPage();
            _flow.Choose(OnboardingBranch.Ai);
            Assert.AreEqual(OnboardingStep.AiStyle, _flow.Step);
            Place(); // a piece added by hand is not an AI style
            Observe();
            Assert.AreEqual(OnboardingStep.AiStyle, _flow.Step);
            Place(ai: true);
            Observe();
            Assert.AreEqual(OnboardingStep.Done, _flow.Step);
            Assert.AreEqual(1, _ended);
        }

        [Test]
        public void DoingTheOutsideWithoutChoosing_PicksThePath()
        {
            ToBranchPage();
            Place();
            Observe();
            Assert.AreEqual(OnboardingBranch.ByHand, _flow.Branch);
            Assert.AreEqual(OnboardingStep.Done, _flow.Step);

            SetUp();
            ToBranchPage();
            Place(ai: true);
            Observe();
            Assert.AreEqual(OnboardingBranch.Ai, _flow.Branch);
            Assert.AreEqual(OnboardingStep.Done, _flow.Step);
        }

        [Test]
        public void Back_HoldsTheStep_UntilContinueOrChoose()
        {
            Assert.IsFalse(_flow.CanGoBack, "nothing before the scan");
            ToBranchPage();
            _flow.Choose(OnboardingBranch.Ai);
            Assert.AreEqual(OnboardingStep.AiStyle, _flow.Step);

            _flow.Back();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step);
            Assert.AreEqual(OnboardingBranch.None, _flow.Branch, "back to the choice clears it");
            Assert.IsTrue(_flow.IsHeld);
            Place();
            Observe();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step, "held: no auto-pick");
            _flow.Choose(OnboardingBranch.ByHand);
            Assert.AreEqual(OnboardingStep.Done, _flow.Step, "the other path, already done by hand");

            SetUp();
            ToBranchPage();
            _flow.Back();
            Assert.AreEqual(OnboardingStep.RedesignOne, _flow.Step);
            Observe();
            Assert.AreEqual(OnboardingStep.RedesignOne, _flow.Step, "already satisfied, but the user came back to it");
            _flow.Continue();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step);

            _flow.Back();
            _flow.Back();
            _flow.Back();
            Assert.AreEqual(OnboardingStep.Scan, _flow.Step);
            Assert.IsFalse(_flow.CanGoBack);
            _flow.Continue();
            Assert.AreEqual(OnboardingStep.ChooseBranch, _flow.Step, "scan, zone and redesign are still there");
        }

        [TestCase(OnboardingStep.Scan)]
        [TestCase(OnboardingStep.ChooseBranch)]
        [TestCase(OnboardingStep.AiStyle)]
        [TestCase(OnboardingStep.Outside)]
        public void Skip_AtAnyStep_GoesToDone_AndStaysOff(OnboardingStep at)
        {
            var flow = new Onboarding(FakeOnboarding.At(at, OnboardingBranch.Ai));
            int ended = 0;
            flow.Ended += () => ended++;
            flow.Skip();
            flow.Skip();
            Assert.AreEqual(OnboardingStep.Done, flow.Step);
            Assert.IsTrue(flow.State.skipped);
            Assert.IsFalse(flow.IsActive);
            Assert.AreEqual(1, ended);
            flow.Back();
            Assert.AreEqual(OnboardingStep.Done, flow.Step);
            StringAssert.Contains("skipped", flow.Summary());
        }

        [Test]
        public void ThePanel_ShowsOnePageOrTheStepsTabs()
        {
            Assert.AreEqual(OnboardingPage.ScanPrompt, Onboarding.PageFor(OnboardingStep.Scan));
            Assert.AreEqual(OnboardingPage.ZoneSetup, Onboarding.PageFor(OnboardingStep.SetZone));
            Assert.AreEqual(OnboardingPage.BranchChoice, Onboarding.PageFor(OnboardingStep.ChooseBranch));
            Assert.AreEqual(OnboardingPage.Tabs, Onboarding.PageFor(OnboardingStep.AiStyle));
            Assert.AreEqual(PanelTabs.None, Onboarding.TabsFor(OnboardingStep.SetZone));
            Assert.AreEqual(PanelTabs.None, Onboarding.TabsFor(OnboardingStep.ChooseBranch));
            Assert.AreEqual(PanelTabs.MyRoom, Onboarding.OpenTab(OnboardingStep.RedesignOne));
            Assert.AreEqual(PanelTabs.Ai, Onboarding.TabsFor(OnboardingStep.AiStyle));
            Assert.AreEqual(PanelTabs.MyRoom, Onboarding.TabsFor(OnboardingStep.RedesignOne));
            Assert.AreEqual(PanelTabs.Style | PanelTabs.Add, Onboarding.TabsFor(OnboardingStep.Outside));
            Assert.AreEqual(PanelTabs.Style, Onboarding.OpenTab(OnboardingStep.Outside));
            Assert.AreEqual(PanelTabs.All, Onboarding.TabsFor(OnboardingStep.Done));
            Assert.AreEqual(OnboardingPage.Tabs, Onboarding.PageFor(OnboardingStep.Done));
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 5, 0 }, Array.ConvertAll(new[] { OnboardingStep.Scan, OnboardingStep.SetZone, OnboardingStep.RedesignOne,
                OnboardingStep.ChooseBranch, OnboardingStep.AiStyle, OnboardingStep.Outside, OnboardingStep.Done }, s => Onboarding.Number(s)));
        }

        [TestCase(OnboardingStep.Scan)]
        [TestCase(OnboardingStep.SetZone)]
        [TestCase(OnboardingStep.RedesignOne)]
        [TestCase(OnboardingStep.ChooseBranch)]
        [TestCase(OnboardingStep.AiStyle)]
        [TestCase(OnboardingStep.Outside)]
        public void TheFake_StartsTheFlowAtAnyStep(OnboardingStep at)
        {
            var flow = new Onboarding(FakeOnboarding.At(at, at == OnboardingStep.AiStyle ? OnboardingBranch.Ai : OnboardingBranch.ByHand));
            if (at == OnboardingStep.ChooseBranch) flow.State.branch = OnboardingBranch.None;
            flow.Observe(FakeOnboarding.DesignBefore(at), at != OnboardingStep.Scan);
            Assert.AreEqual(at, flow.Step);
        }

        [Test]
        public void Clock_RunsOnlyWhileActiveAndNotPaused_AndClampsLongFrames()
        {
            _flow.Tick(0.1f);
            _flow.Pause();
            _flow.Tick(5f);
            _flow.Resume();
            _flow.Tick(30f);
            _flow.Tick(float.NaN);
            _flow.Tick(-1f);
            Assert.AreEqual(0.1f + Onboarding.MaxTickSeconds, _flow.State.activeSeconds, 1e-5f);
            _flow.Skip();
            _flow.Tick(0.2f);
            Assert.AreEqual(0.1f + Onboarding.MaxTickSeconds, _flow.State.activeSeconds, 1e-5f);
        }

        [Test]
        public void FirstPayoff_IsRecordedAgainstTheOneMinuteTarget()
        {
            Assert.IsNull(_flow.PayoffWithinTarget);
            for (int i = 0; i < 100; i++) _flow.Tick(0.4f); // 40 s
            _design.zoneOutline = FakeRoomInfo.HalfZone("a");
            Place(ai: true);
            Observe();
            Assert.AreEqual(40f, _flow.State.firstPayoffSeconds, 1e-3f, "an applied AI style is a payoff too");
            Assert.IsTrue(_flow.PayoffWithinTarget);

            var late = new Onboarding(new OnboardingState { activeSeconds = 75f });
            Redesign();
            late.Observe(_design, true);
            Assert.IsFalse(late.PayoffWithinTarget);
        }

        [Test]
        public void SavedProgress_ResumesWithItsPath_AndFinishedStaysFinished()
        {
            string path = Path.Combine(Path.GetTempPath(), "ownspace-onboarding-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                Assert.IsFalse(OnboardingJson.TryLoadFromFile(path, out var fresh));
                Assert.AreEqual(OnboardingStep.Scan, fresh.step, "first run");

                ToBranchPage();
                _flow.Choose(OnboardingBranch.Ai);
                _flow.Tick(0.3f);
                OnboardingJson.SaveToFile(path, _flow.State);
                Assert.IsTrue(OnboardingJson.TryLoadFromFile(path, out var resumed));
                Assert.AreEqual(OnboardingStep.AiStyle, resumed.step);
                Assert.AreEqual(OnboardingBranch.Ai, resumed.branch);
                Assert.AreEqual(0.3f, resumed.activeSeconds, 1e-5f);
                StringAssert.Contains("\"AiStyle\"", File.ReadAllText(path));

                _flow.Skip();
                OnboardingJson.SaveToFile(path, _flow.State);
                Assert.IsTrue(OnboardingJson.TryLoadFromFile(path, out var later));
                Assert.IsFalse(new Onboarding(later).IsActive, "skipped onboarding never shows again");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestCase("SetZone", false, OnboardingStep.Scan)]
        [TestCase("RedesignOne", false, OnboardingStep.RedesignOne)]
        [TestCase("FillOutside", false, OnboardingStep.ChooseBranch)]
        [TestCase("PickMood", false, OnboardingStep.ChooseBranch)]
        [TestCase("Done", false, OnboardingStep.Done)]
        [TestCase("FillOutside", true, OnboardingStep.Done)]
        public void Version1Saves_MapOntoTheGuidedFlow(string step, bool skipped, OnboardingStep expected)
        {
            string json = "{\"version\":1,\"step\":\"" + step + "\",\"skipped\":" + (skipped ? "true" : "false") + ",\"activeSeconds\":12.5}";
            Assert.IsTrue(OnboardingJson.TryFromJson(json, out var state));
            Assert.AreEqual(expected, state.step);
            Assert.AreEqual(OnboardingBranch.None, state.branch);
            Assert.AreEqual(OnboardingState.CurrentVersion, state.version);
            Assert.AreEqual(12.5f, state.activeSeconds, 1e-5f);
            Assert.AreEqual(expected != OnboardingStep.Done, new Onboarding(state).IsActive);
        }

        [TestCase("not json")]
        [TestCase("{\"version\":99}")]
        [TestCase("{\"version\":2,\"step\":\"Teleport\"}")]
        [TestCase("{\"version\":2,\"step\":7}")]
        public void BrokenSave_GivesAFreshFirstRun(string json)
        {
            Assert.IsFalse(OnboardingJson.TryFromJson(json, out var state));
            Assert.AreEqual(OnboardingStep.Scan, state.step);
            Assert.IsFalse(state.skipped);
        }

        [Test]
        public void TickAndObserve_DoNotAllocate()
        {
            _design.zoneOutline = FakeRoomInfo.HalfZone("a");
            for (int i = 0; i < 20; i++) { _flow.Tick(0.01f); Observe(); }
            TestDelegate tick = () => { _flow.Tick(0.016f); _flow.Observe(_design, true); };
            Assert.That(tick, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }
    }
}

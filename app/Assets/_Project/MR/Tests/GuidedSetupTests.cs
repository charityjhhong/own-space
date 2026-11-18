using System;
using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: "don't show all tabs at first; walk one step at a time", in this order: scan, my zone, the
    // furniture in my zone (My room), then "Let AI design the outside" or "Design the outside myself", then all five tabs.
    // Later the human said "no tutorial": OnboardingGuide.GuideEnabled is off, so the app never runs this guide.
    // The tests below still drive its code directly (kept for turning it back on); the first test covers the off path.
    public class GuidedSetupTests
    {
        [Test]
        public void GuideOff_OnboardingStartsSkipped_SoThePanelOpensWithAllFiveTabs()
        {
            Assert.IsFalse(OnboardingGuide.GuideEnabled, "the human: no tutorial");
            var go = new GameObject("Onboarding");
            try
            {
                var guide = go.AddComponent<OnboardingGuide>();
                typeof(OnboardingGuide).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(guide, null);
                Assert.IsFalse(guide.Onboarding.IsActive, "no step: no ghost hand, no step label");
                var steps = new OnboardingSteps(guide.Onboarding, () => new DesignState());
                Assert.IsFalse(steps.Active, "PanelHost shows the guide sheet only while this is active");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Core's onboarding behind the panel's guide (Lead): a design that the test changes as the user would.
        sealed class Run
        {
            public readonly DesignState Design = new DesignState();
            public readonly Onboarding Onboarding = new Onboarding();
            public readonly OnboardingSteps Steps;
            public Run(bool zoneSet = true)
            {
                Steps = new OnboardingSteps(Onboarding, () => Design);
                if (zoneSet) Design.zoneOutline.AddRange(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f) });
                Observe();
            }
            public void Observe() => Onboarding.Observe(Design);
            public void Redesign() { Design.anchorChoices.Add(new AnchorChoice { anchorId = "desk", mode = AnchorMode.Redesign }); Observe(); }
            public void AiPiece() { Design.placements.Add(new FreePlacement { placementId = "p", catalogItemId = "sofa", aiPlaced = true }); Observe(); }
            public void OutsideFloor() { Design.outsideFinish.floorFinishId = "oak"; Observe(); }
        }

        [Test]
        public void Steps_AreCoresOnboarding_MyRoom_ThenTheOutsideChoice_NextOnlyWhenDone_BackSkip()
        {
            var run = new Run(zoneSet: false);
            IGuideSteps steps = run.Steps;
            Assert.AreEqual(GuideStep.Zone, steps.Step, "a room: past the scan; the zone is next");
            Assert.AreEqual(5, steps.Total, "core's count: scan, zone, my zone, the choice, the way chosen");
            int changes = 0;
            steps.Changed += () => changes++;
            run.Design.zoneOutline.AddRange(new[] { new Vec2(0f, 0f), new Vec2(1f, 0f), new Vec2(1f, 1f) });
            run.Observe();
            Assert.AreEqual(GuideStep.Inside, steps.Step, "the zone set: the furniture in it");
            Assert.AreEqual(3, steps.Number);
            Assert.AreEqual(1, changes);

            Assert.IsFalse(steps.CanNext, "Next is grey until a piece in my zone is redesigned");
            steps.Next();
            Assert.AreEqual(GuideStep.Inside, steps.Step, "and pressing it does nothing");
            run.Redesign();
            Assert.AreEqual(GuideStep.Outside, steps.Step, "core moves on by itself once the step is done");
            Assert.AreEqual(GuideBranch.None, steps.Branch, "the choice");
            Assert.AreEqual(4, steps.Number, "the choice is step 4");
            Assert.IsFalse(steps.CanNext, "the choice is made with its two cards, not Next");

            steps.Choose(GuideBranch.Ai);
            Assert.AreEqual(GuideBranch.Ai, steps.Branch);
            Assert.AreEqual(5, steps.Number, "the way chosen is step 5");
            Assert.IsFalse(steps.CanNext, "Finish is grey until the AI places a piece");
            Assert.IsTrue(steps.Back(), "back to the choice");
            Assert.AreEqual(GuideBranch.None, steps.Branch);
            Assert.IsTrue(steps.Back(), "and back to the zone's furniture");
            Assert.AreEqual(GuideStep.Inside, steps.Step);
            Assert.IsTrue(steps.CanNext, "it was done: Next is on (the step waits for it after Back)");
            Assert.IsFalse(steps.Back(), "nothing before My room: the zone card leads the scan and the zone");
            steps.Next();
            Assert.AreEqual(GuideStep.Outside, steps.Step);
            steps.Choose(GuideBranch.Self);
            Assert.AreEqual(GuideBranch.Self, steps.Branch);
            run.OutsideFloor();
            Assert.AreEqual(GuideStep.Done, steps.Step, "a floor outside: done");
            Assert.IsFalse(steps.Active);

            var skipped = new Run();
            skipped.Steps.Skip();
            Assert.IsFalse(skipped.Steps.Active, "Skip setup ends it at once");
            Assert.IsFalse(new OnboardingSteps(new Onboarding(new OnboardingState { step = OnboardingStep.Done }), () => null).Active,
                "finished or skipped before (core's saved state): no guide");
        }

        sealed class Page : IPanelPage
        {
            public readonly string Name; public int Pressed = -1;
            public Page(string name) { Name = name; }
            public int Count => 3;
            public int Columns => 3;
            public void Describe(int i, ref CardInfo c) { c.Kind = CardKind.Chip; c.Row = 0; c.Title = Name + " " + i; }
            public bool Press(int i) { Pressed = i; return true; }
        }

        [Test]
        public void OnTheRealPanel_TheTabRowGivesWayToAHeader_MyRoom_ThenTwoBigCards_ThenThePages()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                panel.Configure(PanelHost.Tabs, new IPanelPage[] { new Page("style"), new Page("mine"), new Page("add"), new Page("ai"), new Page("tidy") });
                var run = new Run();
                IGuideSteps steps = run.Steps;
                Page ai = new Page("ai"), mine = new Page("mine"), style = new Page("style"), add = new Page("add");
                var guide = new GuidePage(steps, ai, mine, style, add, panel.Fonts?.Regular) { Changed = panel.Invalidate };
                panel.TabsHidden = true;
                panel.ShowSheet(guide);
                CardInfo C(int k) { var c = new CardInfo(); guide.Describe(k, ref c); return c; }
                PanelElement E(int k) => panel.Element(ToolPanel.MaxTabs + k);
                int Find(string title) { for (int k = 0; k < guide.Count; k++) if (C(k).Title == title) return k; return -1; }

                void Fits(string where)
                {
                    panel.Invalidate();
                    for (int t = 0; t < ToolPanel.MaxTabs; t++) Assert.IsFalse(panel.Element(t).Visible, where + ": no tab row during the guide");
                    float top = panel.Metrics.Size.y * 0.5f, bottom = top - panel.Height, inner = panel.Metrics.Size.x * 0.5f - panel.Metrics.Padding;
                    Assert.LessOrEqual(E(0).Centre.y + E(0).Size.y * 0.5f, top - panel.Metrics.TabTop + 1e-4f, where + ": the header starts where the tabs were");
                    for (int k = 0; k < guide.Count; k++)
                    {
                        PanelElement e = E(k);
                        Assert.IsTrue(e.Visible, where + ": " + C(k).Title);
                        Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom - 1e-4f, where + ": inside: " + C(k).Title);
                        Assert.LessOrEqual(Mathf.Abs(e.Centre.x) + e.Size.x * 0.5f, inner + 1e-4f, where + ": inside the width: " + C(k).Title);
                        e.Text.ForceMeshUpdate();
                        Assert.IsFalse(e.Text.isTextTruncated, where + ": " + C(k).Title);
                        StringAssert.DoesNotContain("…", e.Text.text);
                    }
                }

                // Step 3: the header, one line, Skip setup and Next, and My room's own cards.
                Fits("my zone's furniture");
                Assert.AreEqual("Your zone", C(0).Title);
                Assert.AreEqual("Step 3 of 5", C(1).Title);
                Assert.AreEqual(C(0).Row, C(1).Row, "the count beside the title");
                Assert.AreEqual(GuidePage.SkipLabel, C(3).Title);
                Assert.AreEqual(GuidePage.NextLabel, C(4).Title);
                Assert.IsTrue(C(4).Disabled, "Next is grey until a piece is redesigned");
                Assert.IsFalse(guide.Press(4), "and does nothing");
                Assert.Greater(E(4).Centre.x, E(3).Centre.x, "Next is right-most");
                Assert.Less(Find(GuidePage.BackLabel), 0, "no Back on the first step shown");
                Assert.AreSame(mine, guide.Inner);
                Assert.AreEqual("mine 0", C(5).Title, "My room's own cards follow");
                Assert.IsTrue(guide.Press(6));
                Assert.AreEqual(1, mine.Pressed, "a press reaches the page");
                Assert.AreEqual(4, guide.MainIndex, "the ghost hand points at Next");
                Assert.AreNotEqual(go.transform.position, panel.CardPosition(4));

                // Step 4: two big cards (core moves on once a piece is redesigned).
                run.Redesign();
                Fits("the outside choice");
                Assert.AreEqual("Step 4 of 5", C(1).Title);
                int aiCard = Find(GuidePage.AiChoice), selfCard = Find(GuidePage.SelfChoice);
                Assert.Greater(aiCard, 0); Assert.Greater(selfCard, aiCard);
                Assert.AreEqual(0.056f, E(aiCard).Size.y, 1e-5f, "big cards");
                Assert.Less(Find(GuidePage.FinishLabel), 0, "no Finish before a choice");
                Assert.AreEqual(aiCard, guide.MainIndex, "the ghost hand points at the first choice");

                // "Let AI design the outside": the AI page.
                Assert.IsTrue(guide.Press(aiCard));
                Assert.AreSame(ai, guide.Inner);
                Assert.AreEqual("Step 5 of 5", C(1).Title);
                Fits("the AI");
                Assert.IsTrue(guide.Press(Find(GuidePage.BackLabel)), "Back to the choice");
                Assert.IsNull(guide.Inner);

                // "Design the outside myself": Style, and Add by its text option; then Finish.
                Assert.IsTrue(guide.Press(Find(GuidePage.SelfChoice)));
                Assert.AreSame(style, guide.Inner, "walls and floor first");
                Fits("walls and floor");
                int furniture = Find(GuidePage.AddOption);
                Assert.AreEqual(CardKind.Toggle, C(furniture).Kind);
                Assert.IsTrue(C(Find(GuidePage.StyleOption)).Selected);
                Assert.IsTrue(guide.Press(furniture));
                Assert.AreSame(add, guide.Inner);
                Assert.IsTrue(C(furniture).Selected);
                Fits("furniture");
                Assert.IsTrue(C(Find(GuidePage.FinishLabel)).Disabled, "Finish is grey until something outside changes");
                run.OutsideFloor();
                Assert.IsFalse(steps.Active, "core ends the guide once the outside has a floor");

                panel.TabsHidden = false;
                panel.CloseSheet();
                for (int t = 0; t < PanelHost.Tabs.Length; t++) Assert.IsTrue(panel.Element(t).Visible, "all five tabs after the guide");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void SkipSetup_EndsTheGuide_FromAnyStep()
        {
            var steps = new Run().Steps;
            var guide = new GuidePage(steps, null, null, null, null);
            var c = new CardInfo(); guide.Describe(3, ref c);
            Assert.AreEqual(GuidePage.SkipLabel, c.Title);
            Assert.IsTrue(guide.Press(3));
            Assert.IsFalse(steps.Active);
        }
    }
}

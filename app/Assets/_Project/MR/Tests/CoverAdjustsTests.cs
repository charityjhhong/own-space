using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // The human: "after placing a cover in Tidy up, add a way to adjust its size."
    public class CoverAdjustsTests
    {
        [Test]
        public void APropScales_InTenths_From70To130Percent_AndStopsThere()
        {
            float s = 1f;
            var seen = new List<float>();
            for (int i = 0; i < 6; i++) { s = CoverAdjusts.NextScale(s, 1); seen.Add(s); }
            Assert.AreEqual(1.3f, s, 1e-5f, "never past 1.3");
            Assert.AreEqual(1.1f, seen[0], 1e-5f);
            for (int i = 0; i < 10; i++) s = CoverAdjusts.NextScale(s, -1);
            Assert.AreEqual(0.7f, s, 1e-5f, "never under 0.7");
            Assert.AreEqual(new Vector2(1.1f, 1f), CoverAdjusts.Scaled(Vector2.one, AdjustAction.Wider));
            Assert.AreEqual(new Vector2(1f, 0.9f), CoverAdjusts.Scaled(Vector2.one, AdjustAction.Shallower));
            Assert.AreEqual(new Vector2(1.3f, 1f), CoverAdjusts.Scaled(new Vector2(1.3f, 1f), AdjustAction.Wider), "at the limit: unchanged, so the button greys");
            Assert.IsTrue(CoverAdjusts.IsResize(AdjustAction.Deeper));
            Assert.IsFalse(CoverAdjusts.IsResize(AdjustAction.Reset));
        }

        [Test]
        public void APlainBoxSide_Steps5cm_Between15cmAndCoresLargestSide()
        {
            Assert.AreEqual(0.45f, CoverAdjusts.NextSide(0.4f, 1), 1e-5f);
            Assert.AreEqual(0.35f, CoverAdjusts.NextSide(0.4f, -1), 1e-5f);
            Assert.AreEqual(CoverAdjusts.MinSide, CoverAdjusts.NextSide(0.17f, -1), 1e-5f);
            Assert.AreEqual(CoverAdjusts.MinSide, CoverAdjusts.NextSide(CoverAdjusts.MinSide, -1), 1e-5f, "at the smallest: no change");
            Assert.AreEqual(HideRules.MaxSide, CoverAdjusts.NextSide(HideRules.MaxSide - 0.02f, 1), 1e-5f);
            Assert.AreEqual(HideRules.MaxSide, CoverAdjusts.NextSide(HideRules.MaxSide, 1), 1e-5f);
            Assert.AreEqual(0.1f, CoverAdjusts.NextSide(0.1f, -1), 1e-5f, "a box drawn smaller than the limit never grows on Narrower");
        }

        [Test]
        public void Sizes_AreSavedNextToTheRoomsDesign_AndComeBack()
        {
            string design = Path.Combine(Path.GetTempPath(), "ownspace-covers-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var a = new CoverAdjusts();
                a.Use(design);
                Assert.AreEqual(Vector2.one, a.Get("c-1"), "never resized: the model's own size");
                a.Set("c-1", new Vector2(1.2f, 0.8f));
                Assert.IsTrue(File.Exists(CoverAdjusts.PathFor(design)));
                StringAssert.EndsWith(".covers.json", CoverAdjusts.PathFor(design));
                var again = new CoverAdjusts(); // a restart
                again.Use(design);
                Assert.AreEqual(new Vector2(1.2f, 0.8f), again.Get("c-1"));
                again.Set("c-1", Vector2.one);
                var third = new CoverAdjusts();
                third.Use(design);
                Assert.AreEqual(Vector2.one, third.Get("c-1"), "back to 1: the entry is dropped");
                a.Use(design + ".other");
                Assert.AreEqual(Vector2.one, a.Get("c-1"), "another room has its own sizes");
                CoverAdjusts.Clear(design);
                Assert.IsFalse(File.Exists(CoverAdjusts.PathFor(design)), "Reset my design forgets them");
            }
            finally { CoverAdjusts.Clear(design); }
        }

        // Each press is one step in the same history as the session's steps and Adjust fit (#265), in order.
        [Test]
        public void EachResize_IsOneUndoStep_InOrderWithTheSessionsSteps()
        {
            var session = new DesignSession(FakeRoomInfo.DormRoom());
            var changes = new UserChanges();
            session.Changed += () => changes.SessionChanged(session);
            Vector2 scale = Vector2.one;
            void Press(AdjustAction action)
            {
                Vector2 before = scale, after = CoverAdjusts.Scaled(scale, action);
                scale = after;
                changes.Note(new CoverScaleStep(v => scale = v, before, after));
            }
            Press(AdjustAction.Wider);
            session.SetMood("morning"); changes.Note(1);
            Press(AdjustAction.Wider);
            Press(AdjustAction.Shallower);
            Assert.AreEqual(new Vector2(1.2f, 0.9f), scale);

            Assert.IsTrue(changes.Undo(session));
            Assert.AreEqual(new Vector2(1.2f, 1f), scale, "the last press first");
            Assert.IsTrue(changes.Undo(session));
            Assert.AreEqual(new Vector2(1.1f, 1f), scale);
            Assert.AreEqual("morning", session.State.moodPresetId, "the mood is still on");
            Assert.IsTrue(changes.Undo(session));
            Assert.IsTrue(string.IsNullOrEmpty(session.State.moodPresetId), "then the mood");
            Assert.AreEqual(new Vector2(1.1f, 1f), scale);
            Assert.IsTrue(changes.Undo(session));
            Assert.AreEqual(Vector2.one, scale, "then the first press");
            Assert.IsFalse(changes.CanUndo(session));
            Assert.IsTrue(changes.Redo(session));
            Assert.AreEqual(new Vector2(1.1f, 1f), scale, "redo replays in the same order");
        }

        // Turning (the corner handle, 15° steps) and resizing are checked by core's cover rules on this footprint.
        [Test]
        public void Footprint_IsTheScaledModelAtTheNewYaw_AndCoreRejectsBadOnes()
        {
            HideBox box = CoverAdjusts.Footprint("c-1", new Vec3(1f, 0f, 2f), new Vec3(0.5f, 0.4f, 0.3f), new Vector2(1.2f, 0.8f), 45f);
            Assert.AreEqual("c-1", box.hideBoxId);
            Assert.AreEqual(0.6f, box.size.x, 1e-5f, "width times its scale");
            Assert.AreEqual(0.4f, box.size.y, 1e-5f, "height never scales");
            Assert.AreEqual(0.24f, box.size.z, 1e-5f);
            Assert.AreEqual(45f, box.yawDegrees);
            Assert.AreEqual(1f, box.position.x); Assert.AreEqual(2f, box.position.z);
            Assert.AreEqual(90f, RotateHandle.DragYaw(0f, Vector3.zero, Vector3.forward, Vector3.right), "a quarter sweep around the cover is 90°, in 15° steps");
            Assert.AreEqual(15f, RotateHandle.Snap(11f));
            var session = new DesignSession(FakeRoomInfo.DormRoom());
            Assert.AreNotEqual(HideStatus.Ok, session.CheckHideBox(CoverAdjusts.Footprint("c-1", new Vec3(50f, 0f, 50f), new Vec3(0.5f, 0.4f, 0.3f), Vector2.one, 15f)), "outside my zone: refused, so a turn or resize there changes nothing");
            Assert.AreEqual(HideStatus.TooLarge, session.CheckHideBox(CoverAdjusts.Footprint("c-1", new Vec3(0f, 0f, 0f), new Vec3(1.8f, 0.4f, 0.3f), new Vector2(1.3f, 1f), 0f)), "past core's largest side");
        }

        sealed class Cover : ITidyUp, ICoverResize
        {
            public TidyStep Step { get; set; } = TidyStep.Done;
            public bool Drawing => false;
            public bool HasSelection => false;
            public int SelectedLook => 1;
            public string CoveredWith => "Covered with a basket.";
            public CatalogItem LookItem(int look) => null;
            public void StartDrawing() { }
            public void CancelDrawing() { }
            public bool PickLook(int look) => true;
            public bool RemoveSelected() => true;
            public event Action Changed;
            public event Action<string> Refusal { add { } remove { } }
            public bool HasCover { get; set; } = true;
            public Vector2 Scale = Vector2.one;
            public bool Refuse;
            public readonly List<AdjustAction> Pressed = new List<AdjustAction>();
            public bool CanResize(AdjustAction a) => CoverAdjusts.Scaled(Scale, a) != Scale;
            public bool Resize(AdjustAction a)
            {
                Pressed.Add(a);
                if (Refuse || !CanResize(a)) return false;
                Scale = CoverAdjusts.Scaled(Scale, a);
                Changed?.Invoke();
                return true;
            }
        }

        [Test]
        public void TheDoneStep_HasTheFourSizeButtons_GreyAtALimit_WithTheHintLine_AndFitsThePanel()
        {
            var cover = new Cover();
            var page = new TidyUpPage(cover);
            CardInfo C(int i) { var c = new CardInfo(); page.Describe(i, ref c); return c; }
            int Find(string title) { for (int i = 0; i < page.Count; i++) if (C(i).Title == title) return i; return -1; }
            foreach (string label in new[] { "Wider", "Narrower", "Deeper", "Shallower" })
            {
                Assert.GreaterOrEqual(Find(label), 0, label);
                Assert.AreEqual(CardKind.Chip, C(Find(label)).Kind);
                Assert.IsFalse(C(Find(label)).Disabled, label);
            }
            Assert.AreEqual(C(Find("Wider")).Row, C(Find("Narrower")).Row, "as on Adjust fit: Wider and Narrower share a row");
            Assert.AreEqual(C(Find("Deeper")).Row, C(Find("Shallower")).Row);
            Assert.Less(Find(FurniturePage.LargestWidth), 0, "no hint away from the limits");
            Assert.AreEqual(TidyUpPage.Safety, C(page.Count - 1).Title, "the safety line stays last");

            for (int i = 0; i < 3; i++) Assert.IsTrue(page.Press(Find("Wider")));
            Assert.AreEqual(1.3f, cover.Scale.x, 1e-5f);
            Assert.IsTrue(C(Find("Wider")).Disabled, "at the limit: grey");
            Assert.IsFalse(page.Press(Find("Wider")), "and it does nothing");
            Assert.GreaterOrEqual(Find(FurniturePage.LargestWidth), 0, "the same hint line as Adjust fit");
            Assert.IsFalse(C(Find("Narrower")).Disabled);
            cover.Refuse = true;
            Assert.IsFalse(page.Press(Find("Deeper")), "refused by the cover rules (a window, a desk, outside my zone): no change");
            Assert.AreEqual(1f, cover.Scale.y, 1e-5f);

            cover.HasCover = false;
            cover.Step = TidyStep.Draw; cover.Step = TidyStep.Done;
            page = new TidyUpPage(cover);
            Assert.Less(Find("Wider"), 0, "no cover to resize: no size buttons");

            // On the real panel: every row inside the panel, nothing cut.
            cover.HasCover = true;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                page = new TidyUpPage(cover, panel.Fonts?.Regular);
                panel.Configure(new[] { "Tidy up" }, new IPanelPage[] { page });
                panel.Invalidate();
                Assert.LessOrEqual(panel.Height, panel.Metrics.Size.y - 0.05f, "well inside the tallest panel");
                float bottom = panel.Metrics.Size.y * 0.5f - panel.Height, inner = panel.Metrics.Size.x * 0.5f - panel.Metrics.Padding;
                for (int i = 0; i < page.Count; i++)
                {
                    PanelElement e = panel.Element(ToolPanel.MaxTabs + i);
                    Assert.IsTrue(e.Visible, C(i).Title);
                    Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom - 1e-4f, C(i).Title);
                    Assert.LessOrEqual(e.Centre.x + e.Size.x * 0.5f, inner + 1e-4f, C(i).Title + " is inside the panel's width");
                    e.Text.ForceMeshUpdate();
                    Assert.IsFalse(e.Text.isTextTruncated, C(i).Title);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

using System;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.UI;

namespace OwnSpace.MR.Tests
{
    public class TidyUpPageTests
    {
        sealed class FakeTidy : ITidyUp
        {
            public TidyStep Step { get; set; }
            public bool Drawing { get; set; }
            public bool HasSelection { get; set; }
            public int SelectedLook { get; set; } = -1;
            public string CoveredWith { get; set; }
            public int Starts, Cancels, Picked = -1, Removes;
            public CatalogItem LookItem(int look) => look > 0 ? new CatalogItem { id = "clutter-" + look, primaryColor = "#336699" } : null;
            public void StartDrawing() { Starts++; Drawing = true; Changed?.Invoke(); }
            public void CancelDrawing() { Cancels++; Drawing = false; Changed?.Invoke(); }
            public bool PickLook(int look) { Picked = look; Step = TidyStep.Done; CoveredWith = "Covered with a basket."; Changed?.Invoke(); return true; }
            public bool RemoveSelected() { Removes++; return true; }
            public event Action Changed;
            public event Action<string> Refusal;
            public void Refuse(string reason) => Refusal?.Invoke(reason);
            // Simulates the world: a released box that core accepts moves to step 2 by itself.
            public void BoxAccepted() { Drawing = false; HasSelection = true; Step = TidyStep.PickLook; Changed?.Invoke(); }
        }

        FakeTidy _tidy;
        TidyUpPage _page;

        [SetUp]
        public void SetUp() { _tidy = new FakeTidy(); _page = new TidyUpPage(_tidy); }

        CardInfo Card(int i) { var c = new CardInfo(); _page.Describe(i, ref c); return c; }

        int Find(string title)
        {
            for (int i = 0; i < _page.Count; i++) if (Card(i).Title == title) return i;
            return -1;
        }

        [Test]
        public void Stepper_CurrentSelected_FutureMuted_SafetyLineAlways()
        {
            Assert.IsTrue(Card(0).Selected);
            Assert.IsTrue(Card(1).Disabled && Card(2).Disabled, "future steps are muted");
            Assert.AreEqual(TidyUpPage.Safety, Card(_page.Count - 1).Title);
        }

        [Test]
        public void Draw_AutoAdvancesWhenTheBoxIsAccepted_RefusalStaysOnStep1()
        {
            Assert.IsTrue(_page.Press(Find("Draw a box")));
            Assert.GreaterOrEqual(Find("Cancel"), 0, "drawing shows Cancel");
            _tidy.Refuse("Covers a window or door");
            Assert.AreEqual("Covers a window or door", _page.LastRefusal);
            Assert.IsTrue(Card(0).Selected, "still on step 1");
            _tidy.BoxAccepted();
            Assert.IsTrue(Card(1).Selected, "step 2 by itself");
            Assert.IsFalse(Card(0).Disabled, "a done step is not muted");
            Assert.AreEqual(CardKind.Disc, Card(Find("Basket")).Kind);
        }

        [Test]
        public void PickingALook_AppliesAndShowsDone_AddAnotherStartsOver()
        {
            _tidy.BoxAccepted();
            Assert.IsTrue(_page.Press(Find("Basket")));
            Assert.AreEqual(1, _tidy.Picked);
            Assert.IsTrue(Card(2).Selected, "step 3");
            Assert.GreaterOrEqual(Find("Covered with a basket."), 0);
            Assert.IsTrue(_page.Press(Find("Add another")));
            Assert.AreEqual(1, _tidy.Starts);
        }

        [Test]
        public void Done_HasARedRemove_AndAnExistingCoverShowsItsLook()
        {
            _tidy.BoxAccepted();
            _page.Press(Find("Basket"));
            Assert.IsTrue(Card(Find("Remove")).Danger, "a quiet red Remove on step 3");
            Assert.IsTrue(_page.Press(Find("Remove")));
            _tidy.HasSelection = true; _tidy.SelectedLook = 4; _tidy.Step = TidyStep.PickLook;
            _tidy.CancelDrawing();
            Assert.IsTrue(Card(Find("Box")).Selected, "the existing cover's look is selected");
            _page.Dispose();
            _tidy.Step = TidyStep.Done;
            _tidy.CancelDrawing();
            Assert.IsTrue(Card(1).Selected, "after Dispose the page no longer follows the tool");
        }

        [Test]
        public void ExistingCover_RemoveThisCover()
        {
            _tidy.HasSelection = true;
            _tidy.Step = TidyStep.PickLook;
            _tidy.CancelDrawing(); // any change re-describes
            Assert.IsTrue(_page.Press(Find("Remove cover")));
            Assert.AreEqual(1, _tidy.Removes);
        }

        // Device batch 9: Tidy up's text was cut off at the right edge.
        [Test]
        public void TextLines_SpanThePanel_AndWrap()
        {
            for (int i = 0; i < _page.Count; i++)
            {
                CardInfo c = Card(i);
                if (c.Kind != CardKind.Title) continue;
                Assert.Greater(c.Width, 0.25f, c.Title);
                Assert.AreEqual(2, c.Lines, c.Title);
            }
        }

        // Content PR #264: the looks' thumbnails are in the furniture atlas (Plain = cover-plain, 1-5 = the catalog item's id).
        sealed class RealIds : ITidyUp
        {
            static readonly string[] Ids = { null, "clutter-basket-scandi-01", "clutter-plant-scandi-01", "clutter-floor-cushion-scandi-01", "clutter-box-scandi-01", "clutter-screen-scandi-01" };
            public TidyStep Step => TidyStep.PickLook;
            public bool Drawing => false;
            public bool HasSelection => true;
            public int SelectedLook => 2;
            public string CoveredWith => null;
            public CatalogItem LookItem(int look) => look > 0 ? new CatalogItem { id = Ids[look], primaryColor = "#336699" } : null;
            public void StartDrawing() { }
            public void CancelDrawing() { }
            public bool PickLook(int look) => true;
            public bool RemoveSelected() => true;
            public event Action Changed { add { } remove { } }
            public event Action<string> Refusal { add { } remove { } }
        }

        static FurnitureThumbs Atlas() => new FurnitureThumbs(System.IO.File.ReadAllText("Assets/_Project/Content/thumbnails.json"));

        [Test]
        public void Looks_AreFurnitureCardsWithThumbnails_ThreePerRow()
        {
            _page = new TidyUpPage(new RealIds(), null, Atlas());
            for (int look = 0; look < 6; look++)
            {
                CardInfo c = Card(Find(TidyUpPage.Looks[look]));
                Assert.AreEqual(CardKind.Card, c.Kind, TidyUpPage.Looks[look]);
                Assert.AreNotEqual(UnityEngine.Vector4.zero, c.Thumb, TidyUpPage.Looks[look] + " has a thumbnail");
                Assert.AreEqual(0f, c.Width, "a column-wide card, like furniture");
                Assert.AreEqual(2 + look / 3, c.Row, "three per row");
                Assert.AreEqual(look == 2, c.Selected, "the cover's own look is selected");
            }
            Assert.AreEqual(3, _page.Columns);
            Assert.AreNotEqual(Card(Find("Plain")).Thumb, Card(Find("Basket")).Thumb);
        }

        [Test]
        public void Looks_OnTheRealPanel_AllSix_Remove_AndTheSafetyLineAreInside()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                _page = new TidyUpPage(new RealIds(), null, Atlas());
                panel.Configure(new[] { "Tidy up" }, new IPanelPage[] { _page });
                panel.Invalidate();
                Assert.LessOrEqual(panel.Height, panel.Metrics.Size.y - 1e-4f, "the page fits without reaching the tallest panel");
                float bottom = panel.Metrics.Size.y * 0.5f - panel.Height;
                for (int i = 0; i < _page.Count; i++)
                {
                    PanelElement e = panel.Element(ToolPanel.MaxTabs + i);
                    Assert.IsTrue(e.Visible, Card(i).Title + " is shown");
                    Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom + panel.Metrics.Padding * 0.5f - 1e-4f, Card(i).Title + " is inside the panel");
                    Assert.LessOrEqual(UnityEngine.Mathf.Abs(e.Centre.x) + e.Size.x * 0.5f, panel.Metrics.Size.x * 0.5f + 1e-4f, Card(i).Title);
                }
                foreach (TMPro.TextMeshPro t in go.GetComponentsInChildren<TMPro.TextMeshPro>())
                {
                    if (string.IsNullOrEmpty(t.text)) continue;
                    t.ForceMeshUpdate();
                    Assert.IsFalse(t.isTextTruncated, t.text);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Device, build dfb22f4: "the thumbnails show, but centre the object and the text".
        [Test]
        public void LookCards_OnTheRealPanel_ThumbnailNameAndRowsAreCentred()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                _page = new TidyUpPage(new RealIds(), panel.Fonts?.Regular, Atlas());
                panel.Configure(new[] { "Tidy up" }, new IPanelPage[] { _page });
                panel.Invalidate();
                int first = Find("Plain");
                for (int row = 0; row < 2; row++)
                {
                    PanelElement left = panel.Element(ToolPanel.MaxTabs + first + row * 3), right = panel.Element(ToolPanel.MaxTabs + first + row * 3 + 2);
                    Assert.AreEqual(0f, left.Centre.x - left.Size.x * 0.5f + right.Centre.x + right.Size.x * 0.5f, 1e-4f, "the row of three is centred in the panel");
                }
                for (int look = 0; look < 6; look++)
                {
                    PanelElement card = panel.Element(ToolPanel.MaxTabs + first + look);
                    UnityEngine.Transform thumb = card.Root.Find("Thumb");
                    Assert.IsNotNull(thumb, TidyUpPage.Looks[look] + " has a thumbnail");
                    card.Text.ForceMeshUpdate();
                    UnityEngine.Vector3 ink = card.Root.InverseTransformPoint(card.Text.transform.TransformPoint(card.Text.textBounds.center));
                    Assert.AreEqual(0f, thumb.localPosition.x, 1e-5f, TidyUpPage.Looks[look] + ": the thumbnail is centred in the card");
                    Assert.AreEqual(0f, ink.x, 0.001f, TidyUpPage.Looks[look] + ": the name is centred under it");
                    // The picture and its name are one group in the middle of the card: the same margin above and below.
                    float above = card.Size.y * 0.5f - (thumb.localPosition.y + thumb.localScale.y * 0.5f);
                    float below = ink.y - card.Text.textBounds.size.y * 0.5f + card.Size.y * 0.5f;
                    Assert.AreEqual(above, below, 0.003f, TidyUpPage.Looks[look] + ": balanced in the card's height");
                    float gap = thumb.localPosition.y - thumb.localScale.y * 0.5f - (ink.y + card.Text.textBounds.size.y * 0.5f);
                    Assert.That(gap, Is.InRange(0.002f, 0.009f), TidyUpPage.Looks[look] + ": the name sits just under the picture (it was 18 mm below)");
                    Assert.IsFalse(card.Text.isTextTruncated, TidyUpPage.Looks[look]);
                    Assert.AreEqual(PanelElement.FontSize(PanelElement.CaptionMm), card.Text.fontSize, 1e-5f, "the card name's 12 mm, not shrunk");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void CardLayout_CentresThePictureAndNameAsOneGroup()
        {
            PanelElement.CardLayout(0.092f, 1, out float thumbY, out float nameY, out float nameHeight);
            float top = 0.046f - (thumbY + PanelElement.ThumbHeight * 0.5f), bottom = nameY - PanelElement.CardLineHeight * 0.5f + 0.046f;
            Assert.AreEqual(top, bottom, 1e-5f, "one line: the same margin above the picture and below the name");
            Assert.AreEqual(PanelElement.CardGap, thumbY - PanelElement.ThumbHeight * 0.5f - (nameY + PanelElement.CardLineHeight * 0.5f), 1e-5f);
            Assert.AreEqual(PanelElement.CardLineHeight + PanelElement.CardNameSlack, nameHeight, 1e-6f);
            PanelElement.CardLayout(0.092f, 2, out float thumb2, out float name2, out _);
            Assert.Greater(thumb2, thumbY, "two lines (furniture names): the picture sits higher to make room");
            Assert.GreaterOrEqual(name2 - PanelElement.CardLineHeight - 0.5f * PanelElement.CardNameSlack, -0.046f, "the name's box stays inside the card");
            PanelElement.CardLayout(0.05f, 2, out float tight, out _, out _);
            Assert.AreEqual(0.025f - 0.006f - PanelElement.ThumbHeight * 0.5f, tight, 1e-5f, "a short card: the picture keeps its 6 mm inset");
        }

        sealed class DoneTidy : ITidyUp
        {
            public TidyStep Step => TidyStep.Done;
            public bool Drawing => false;
            public bool HasSelection => true;
            public int SelectedLook => 1;
            public string CoveredWith => "Covered with a basket.";
            public CatalogItem LookItem(int look) => null;
            public void StartDrawing() { }
            public void CancelDrawing() { }
            public bool PickLook(int look) => true;
            public bool RemoveSelected() => true;
            public event Action Changed { add { } remove { } }
            public event Action<string> Refusal { add { } remove { } }
        }

        // Device, build 9bfbd72: on step 3 the checks of steps 1 and 2 were drawn on top of the step text.
        [Test]
        public void StepRow_OnTheRealPanel_ChecksNeverTouchTheText_AndTheRowFits()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                _page = new TidyUpPage(new DoneTidy(), panel.Fonts?.Regular) { CheckIcon = new UnityEngine.Vector4(0.125f, 0.125f, 0.25f, 0.5f) };
                Assert.AreEqual("Draw a box", Card(0).Title, "the check takes the number's place");
                Assert.AreEqual("Pick a look", Card(1).Title);
                Assert.AreEqual("3 Done", Card(2).Title, "the current step keeps its number");
                Assert.AreNotEqual(UnityEngine.Vector4.zero, Card(0).Icon);
                Assert.AreEqual(UnityEngine.Vector4.zero, Card(2).Icon);
                panel.Configure(new[] { "Tidy up" }, new IPanelPage[] { _page });
                foreach (float scale in new[] { 1f, ToolPanel.MinScale, ToolPanel.MaxScale })
                    foreach (PanelTheme theme in new[] { PanelTheme.Light, PanelTheme.Dark })
                    {
                        panel.SetScale(scale);
                        panel.SetTheme(theme);
                        panel.Invalidate();
                        string where = $"{theme} at {scale}x";
                        float inner = panel.Metrics.Size.x * 0.5f - panel.Metrics.Padding;
                        for (int step = 0; step < 3; step++)
                        {
                            PanelElement e = panel.Element(ToolPanel.MaxTabs + step);
                            Assert.IsTrue(e.Visible, where);
                            Assert.GreaterOrEqual(e.Centre.x - e.Size.x * 0.5f, -inner - 1e-4f, where + ": the row starts inside the panel");
                            Assert.LessOrEqual(e.Centre.x + e.Size.x * 0.5f, inner + 1e-4f, where + ": the row fits the panel's width (step " + (step + 1) + ")");
                            e.Text.ForceMeshUpdate();
                            Assert.IsFalse(e.Text.isTextTruncated, where + ": " + e.Text.text);
                            Assert.GreaterOrEqual(e.Text.fontSize, PanelElement.FontSize(PanelElement.FloorTextMm) - 1e-5f, where + ": \"" + e.Text.text + "\" is not shrunk");
                            UnityEngine.Transform icon = e.Root.Find("Icon");
                            bool shown = icon != null && icon.GetComponent<UnityEngine.Renderer>().enabled;
                            Assert.AreEqual(step < 2, shown, where + ": a check on the completed steps only");
                            if (!shown) continue;
                            UnityEngine.Vector3 ink = e.Root.InverseTransformPoint(e.Text.transform.TransformPoint(e.Text.textBounds.center));
                            float inkLeft = ink.x - e.Text.textBounds.size.x * 0.5f, iconRight = icon.localPosition.x + icon.localScale.x * 0.5f;
                            Assert.Greater(inkLeft, iconRight + 0.001f, where + ": the check ends before \"" + e.Text.text + "\" starts");
                            Assert.GreaterOrEqual(icon.localPosition.x - icon.localScale.x * 0.5f, -e.Size.x * 0.5f, where + ": the check is inside its pill");
                            Assert.LessOrEqual(ink.x + e.Text.textBounds.size.x * 0.5f, e.Size.x * 0.5f, where + ": the text is inside its pill");
                        }
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

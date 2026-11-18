using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    public class ToolPanelTests
    {
        sealed class FakePage : IPanelPage
        {
            public int Presses;
            public bool Refuse, DisableFirst;
            public int Count => 3;
            public int Columns => 3;
            public void Describe(int index, ref CardInfo card) { card.Title = "Card"; card.Disabled = DisableFirst && index == 0; }
            public bool Press(int index) { Presses++; return !Refuse; }
        }

        GameObject _go;
        ToolPanel _panel;
        FakePage _page;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Panel");
            _panel = _go.AddComponent<ToolPanel>();
            _panel.Configure(new[] { "A", "B" }, new IPanelPage[] { _page = new FakePage(), null });
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_go);

        Vector2 CardCentre(int card) => _panel.Element(ToolPanel.MaxTabs + card).Centre;

        // Device shot 233031: "Tidy up" stayed lit while Settings was open.
        [Test]
        public void ASheet_LeavesNoTabSelected_AndLightsTheGear()
        {
            _panel.SelectTab(1);
            Assert.AreEqual(ElementState.Selected, _panel.Element(1).State);
            _panel.ShowSheet(new FakePage());
            Assert.AreEqual(ElementState.Normal, _panel.Element(1).State, "no tab selected under a sheet");
            Assert.AreEqual(ElementState.Selected, _panel.Element(ToolPanel.SettingsButton).State, "the gear is lit");
            _panel.CloseSheet();
            Assert.AreEqual(ElementState.Selected, _panel.Element(1).State);
            Assert.AreEqual(ElementState.Normal, _panel.Element(ToolPanel.SettingsButton).State);
        }

        [Test]
        public void Find_ClaimsGapsAndDisabledCards_OffPanelIsMiss()
        {
            Assert.AreEqual(ToolPanel.MaxTabs + 1, _panel.Find(CardCentre(1)));
            float top = _panel.Metrics.Size.y * 0.5f, gapY = top - (_panel.Metrics.TabTop + _panel.Metrics.TabHeight + _panel.Metrics.BodyTop) * 0.5f;
            Assert.AreEqual(ToolPanel.Back, _panel.Find(new Vector2(0f, gapY)), "the gap between the tabs and the cards is claimed");
            Assert.AreEqual(-1, _panel.Find(new Vector2(0f, top - _panel.Height - 0.01f)), "below the content-fitted surface is a miss");
            Assert.AreEqual(-1, _panel.Find(new Vector2(_panel.Metrics.Size.x, 0f)));
            _page.DisableFirst = true;
            _panel.Invalidate();
            Assert.AreEqual(ToolPanel.Back, _panel.Find(CardCentre(0)), "disabled card: claimed, not pressable");
        }

        [Test]
        public void Release_OverTheSameCard_Presses_ElsewhereDoesNot()
        {
            _panel.BeginPress(ToolPanel.MaxTabs + 1, false);
            _panel.Release(ToolPanel.Back);
            Assert.AreEqual(0, _page.Presses);
            _panel.BeginPress(ToolPanel.MaxTabs + 1, false);
            _panel.Release(ToolPanel.MaxTabs + 1);
            Assert.AreEqual(1, _page.Presses);
        }

        [Test]
        public void Release_RefusedCard_ShowsRefused()
        {
            _page.Refuse = true;
            _panel.BeginPress(ToolPanel.MaxTabs, false);
            _panel.Release(ToolPanel.MaxTabs);
            Assert.AreEqual(ElementState.Refused, _panel.Element(ToolPanel.MaxTabs).State);
        }

        [Test]
        public void ClosingCancelsAPress()
        {
            _panel.BeginPress(ToolPanel.MaxTabs + 2, false);
            _panel.Open(false);
            Assert.AreEqual(-1, _panel.Pressed);
            Assert.AreEqual(ElementState.Normal, _panel.Element(ToolPanel.MaxTabs + 2).State);
            _panel.Release(ToolPanel.MaxTabs + 2);
            Assert.AreEqual(0, _page.Presses, "nothing activates after the cancel");
        }

        [Test]
        public void Follow_SeatedPanelStays_TurnedAwayReturns_PlacedStaysUntilLost()
        {
            PanelMetrics m = PanelMetrics.Default;
            Vector3 head = new Vector3(0f, 1.2f, 0f), seat = ToolPanel.SeatPosition(head, Vector3.forward, m.Ahead, m);
            Assert.IsFalse(ToolPanel.ShouldReturn(false, head, Vector3.forward, seat, m));
            Assert.IsTrue(ToolPanel.ShouldReturn(false, head, Vector3.right, seat, m), "user turned 90 degrees");
            Assert.IsFalse(ToolPanel.ShouldReturn(true, head, Vector3.right, seat, m), "placed by the user: stays");
            Assert.IsFalse(ToolPanel.ShouldReturn(true, head + Vector3.back * 4f, Vector3.forward, seat, m), "placed: pinned, even walked away (device batch 9)");
        }

        // Device log: "returning to its seat" every 0.5-1 s while sitting still at the desk. Likely cause: the desk clamp
        // brings the seat close to the eyes, where 20 degrees is a few centimetres of head motion.
        [Test]
        public void ASeatCloseToTheEyes_DoesNotFollowSmallHeadMotions()
        {
            PanelMetrics m = PanelMetrics.Default;
            Vector3 head = new Vector3(0f, 1.2f, 0f), offset = new Vector3(0.05f, -0.15f, 0.2f); // a clamped, close seat
            Vector3 panel = head + offset;
            Vector3 leaned = head + new Vector3(0.08f, -0.04f, 0.03f); // a lean toward the keyboard: 9 cm
            Assert.Greater(Vector3.Angle(Vector3.ProjectOnPlane(panel - leaned, Vector3.up), Vector3.ProjectOnPlane(offset, Vector3.up)), m.MaxYaw,
                "the angle limit alone would re-seat it");
            Assert.IsFalse(ToolPanel.ShouldReturn(false, leaned, Vector3.forward, panel, offset, m), "within MinDrift: it stays");
            Assert.IsTrue(ToolPanel.ShouldReturn(false, head, Vector3.right, panel, offset, m), "turned 90 degrees: it follows");
        }

        // Device day: a dragged panel no longer gets stranded. Out of view (> 50 degrees) for 3 s it comes back;
        // a glance back in view resets the clock.
        [Test]
        public void PlacedPanel_OutOfViewFor3s_Returns_InViewResets()
        {
            Vector3 head = new Vector3(0f, 1.2f, 0f), panel = head + new Vector3(0.1f, -0.2f, 0.7f);
            float away = 0f;
            for (int i = 0; i < 10; i++) away = ToolPanel.AwayTime(away, 0.1f, head, Vector3.forward, panel);
            Assert.AreEqual(0f, away, "in view: stays");
            for (int i = 0; i < 20; i++) away = ToolPanel.AwayTime(away, 0.1f, head, Vector3.right, panel);
            Assert.AreEqual(2f, away, 1e-4f);
            Assert.Less(away, ToolPanel.AwaySeconds, "two seconds away: still put");
            away = ToolPanel.AwayTime(away, 0.1f, head, Vector3.forward, panel);
            Assert.AreEqual(0f, away, "a look back resets it");
            for (int i = 0; i < 31; i++) away = ToolPanel.AwayTime(away, 0.1f, head, Vector3.back, panel);
            Assert.GreaterOrEqual(away, ToolPanel.AwaySeconds, "three seconds behind the user: comes back");
        }

        // Device, e3f28ec: launched with the headset on the desk, the panel hidden by system UI while it was put on; the
        // recenter then came while the panel was hidden, and it stayed out of view.
        [Test]
        public void ARecenterWhileHidden_ReseatsOnShow_AndAHeadOnTheDeskWaits()
        {
            var rigGo = new GameObject("Hands");
            try
            {
                var hands = rigGo.AddComponent<OwnSpace.Interaction.HandRig>();
                _panel.Hands = hands;
                _panel.Open(true);
                _panel.Placed = false;
                Vector3 lost = hands.View.origin - hands.View.direction * 3f;
                _panel.transform.position = lost;
                _panel.gameObject.SetActive(false);
                hands.RecenterSettledForTest(); // missed: the panel is hidden
                Assert.AreEqual(lost, _panel.transform.position);
                bool ready = false;
                _panel.HeadReady = () => ready;
                _panel.gameObject.SetActive(true);
                _panel.Tick();
                Assert.AreEqual(lost, _panel.transform.position, "the head is not ready (a headset on the desk): it waits");
                ready = true;
                _panel.Tick();
                Assert.IsFalse(_panel.Placed);
                for (int i = 0; i < 120; i++) _panel.Tick();
                Assert.Less(Vector3.Distance(_panel.transform.position, hands.View.origin), 1.2f, "back in front of the user");
            }
            finally { Object.DestroyImmediate(rigGo); }
        }

        [Test]
        public void ALostPanel_ComesBackAfterTwoSeconds_ANearOneNever()
        {
            Assert.IsTrue(ToolPanel.ReseatWaits(true, false));
            Assert.IsFalse(ToolPanel.ReseatWaits(true, true));
            Vector3 head = new Vector3(0f, 1.2f, 0f), seat = head + new Vector3(0.1f, -0.3f, 0.6f);
            Vector3 behind = head + new Vector3(0f, -0.3f, -1.5f);
            float t = 0f;
            for (int i = 0; i < 19; i++) t = ToolPanel.LostTime(t, 0.1f, head, Vector3.forward, behind, seat);
            Assert.Less(t, ToolPanel.LostSeconds);
            t = ToolPanel.LostTime(t, 0.1f, head, Vector3.forward, behind, seat);
            Assert.GreaterOrEqual(t, ToolPanel.LostSeconds - 1e-4f, "out of view and 2 m from the seat for 2 s");
            Vector3 aside = head + new Vector3(-0.5f, -0.3f, 0.2f); // out of view, but within 1 m of the seat
            Assert.AreEqual(0f, ToolPanel.LostTime(1f, 0.1f, head, Vector3.forward, aside, seat));
            Assert.AreEqual(0f, ToolPanel.LostTime(1f, 0.1f, head, Vector3.forward, head + Vector3.forward * 2f, seat), "in view: not lost");
        }

        [Test]
        public void Recenter_ReseatsOnceSettled_APlacedPanelOnlyWhenOutOfView()
        {
            var rigGo = new GameObject("Hands");
            try
            {
                var hands = rigGo.AddComponent<OwnSpace.Interaction.HandRig>();
                _panel.Hands = hands;
                _panel.Placed = true;
                _panel.transform.position = hands.View.origin + hands.View.direction * 0.6f; // in view
                hands.RecenterForTest();
                Assert.IsTrue(_panel.Placed, "the recenter event itself never moves it (the head pose is still the old one)");
                hands.RecenterSettledForTest();
                Assert.IsTrue(_panel.Placed, "placed and still in view: it stays where the user put it");
                _panel.transform.position = hands.View.origin - hands.View.direction * 0.6f; // behind the user now
                hands.RecenterSettledForTest();
                Assert.IsFalse(_panel.Placed, "out of view after the origin moved: back on its seat");
            }
            finally { Object.DestroyImmediate(rigGo); }
        }

        [Test]
        public void Seat_IsAbout65cmAhead_AndTheLazyFollowKeepsIt()
        {
            PanelMetrics m = PanelMetrics.Default;
            Assert.AreEqual(0.65f, m.Ahead, 1e-4f);
            Vector3 head = Vector3.zero, seat = ToolPanel.SeatPosition(head, Vector3.forward, m.Ahead, m);
            Assert.Less(seat.magnitude, m.MaxDistance, "the seat itself is inside the follow distance");
            Assert.IsFalse(ToolPanel.ShouldReturn(false, head, Vector3.forward, seat, m));
        }

        sealed class SectionPage : IPanelPage
        {
            public int Count => 6;
            public int Columns => 5;
            public void Describe(int i, ref CardInfo c) { c.Kind = i == 0 ? CardKind.Title : CardKind.Swatch; c.Row = i == 0 ? 0 : 1; c.Title = "x"; }
            public bool Press(int index) => true;
        }

        [Test]
        public void Rows_TitleSpansTheRow_SwatchesFillFiveColumnsBelowIt()
        {
            _panel.Configure(new[] { "Style" }, new IPanelPage[] { new SectionPage() });
            PanelMetrics m = _panel.Metrics;
            PanelElement title = _panel.Element(ToolPanel.MaxTabs), first = _panel.Element(ToolPanel.MaxTabs + 1), last = _panel.Element(ToolPanel.MaxTabs + 5);
            Assert.AreEqual(m.Size.x - 2f * m.Padding, title.Size.x, 1e-4f);
            Assert.Less(first.Centre.y, title.Centre.y - m.TitleHeight * 0.5f, "swatches sit below the title");
            Assert.AreEqual((m.Size.x - 2f * m.Padding) * 0.8f, last.Centre.x - first.Centre.x, 1e-4f, "five equal columns");
            Assert.AreEqual(ToolPanel.Back, _panel.Find(title.Centre), "a title is claimed but not pressable");
        }

        [Test]
        public void Text_IsSizedInMillimetres_AndEndsInAnEllipsisWhenTooLong()
        {
            PanelElement card = _panel.Element(ToolPanel.MaxTabs);
            Assert.AreEqual(PanelElement.FontSize(14f), card.Text.fontSizeMax, 1e-6f, "14 mm, shrinking only when it must");
            Assert.AreEqual(PanelElement.FontSize(PanelElement.FloorTextMm), card.Text.fontSizeMin, 1e-6f, "never below 10 mm");
            // One line's preferred height is the font's line height at a 14 mm em (about 1.1 to 1.5 em, by font).
            float lineHeight = card.Text.GetPreferredValues("H").y;
            Assert.That(lineHeight, Is.InRange(0.014f * 1.0f, 0.014f * 1.6f), "a chip label is a 14 mm em");
            card.SetText("A very long furniture name that cannot fit in one chip");
            card.Text.ForceMeshUpdate();
            Assert.IsTrue(card.Text.isTextTruncated);
        }

        [Test]
        public void TabsAre14mmSemibold_OnlyTheChosenIsFilled_HoverLifts1mm_PressedIs97Percent()
        {
            PanelElement tab = _panel.Element(0), other = _panel.Element(1);
            // Tabs auto-size down (never ellipsize), so their size is the auto-size maximum.
            Assert.AreEqual(PanelElement.FontSize(PanelElement.TitleMm), tab.Text.fontSizeMax, 1e-6f, "the chosen tab: the same 14 mm");
            Assert.AreEqual(PanelElement.FontSize(PanelElement.TitleMm), other.Text.fontSizeMax, 1e-6f);
            Assert.IsTrue(tab.Bold && other.Bold && other.Flat, "every tab label is bold; a tab has no fill of its own");
            Assert.AreEqual(tab.Text.font, other.Text.font, "chosen or not, the same weight");
            Assert.Greater(tab.Shown.Fill.a, 0.5f, "only the chosen tab is filled");
            float z = other.Root.localPosition.z;
            other.SetState(ElementState.Hover);
            Assert.AreEqual(z - 0.001f, other.Root.localPosition.z, 1e-6f);
            other.SetState(ElementState.Pressed);
            Assert.AreEqual(0.97f, other.Root.localScale.x, 1e-6f);
        }

        [Test]
        public void Prefab_HasInterFontsAndTheGlassMaterial()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            Assert.IsNotNull(prefab, "run ToolPanelBuilder.Build");
            var so = new UnityEditor.SerializedObject(prefab.GetComponent<ToolPanel>());
            StringAssert.Contains("Inter-Regular", so.FindProperty("_font").objectReferenceValue.name);
            StringAssert.Contains("Inter-SemiBold", so.FindProperty("_fontSemiBold").objectReferenceValue.name);
            Assert.AreEqual("OwnSpace/PanelGlass", ((Material)so.FindProperty("_material").objectReferenceValue).shader.name);
        }

        [Test]
        public void Height_FollowsTheContent()
        {
            PanelMetrics m = _panel.Metrics;
            // The row's last chip reaches the right edge, under the resize grip: the bottom clears the grip.
            float bottom = ToolPanel.BottomPaddingFor(m.Size.x * 0.5f - m.Padding, m.Size.x, m.Padding);
            Assert.Greater(bottom, m.Padding);
            Assert.AreEqual(m.BodyTop + m.CardHeight + bottom, _panel.Height, 1e-4f, "one row of chips");
            Assert.Less(_panel.Height, m.Size.y);
        }

        sealed class FurnitureLikePage : IPanelPage
        {
            public float ChipWidth;
            public int SelectedToggle = -1;
            public int Count => 11;
            public int Columns => 3;
            public void Describe(int i, ref CardInfo c)
            {
                c.Title = "Category";
                if (i < 5) { c.Kind = CardKind.Toggle; c.Row = 0; c.Width = ChipWidth; c.Selected = i == SelectedToggle; } // wider than the panel: scrolls
                else if (i < 8) { c.Kind = CardKind.Card; c.Row = 1; }
                else { c.Kind = CardKind.Chip; c.Row = 2; c.Width = 0.03f; c.AlignRight = true; } // a pager
            }
            public bool Press(int index) => true;
        }

        [Test]
        public void ScrollingRow_ClipsFadesAndKeepsTheSelectedInView_PagerRightAligned()
        {
            var page = new FurnitureLikePage { ChipWidth = 0.07f };
            _panel.Configure(new[] { "Furniture" }, new IPanelPage[] { page });
            PanelMetrics m = _panel.Metrics;
            float inner = m.Size.x - 2f * m.Padding;
            PanelElement first = _panel.Element(ToolPanel.MaxTabs), card = _panel.Element(ToolPanel.MaxTabs + 5), last = _panel.Element(ToolPanel.MaxTabs + 10);
            Assert.AreEqual(-inner * 0.5f + 0.035f, first.Centre.x, 1e-4f, "starts at the left edge");
            Assert.AreEqual(1f, first.Clip.w, "a row wider than the panel is clipped");
            Assert.AreEqual(1f, _panel.Element(ToolPanel.MaxTabs + 3).Clip.w);
            Assert.AreEqual(m.TileHeight, card.Size.y, 1e-4f);
            Assert.AreEqual(0f, card.Clip.w, "other rows are not");
            Assert.AreEqual(inner * 0.5f - 0.015f, last.Centre.x, 1e-4f, "the pager ends at the right edge");

            page.SelectedToggle = 4;
            _panel.Invalidate();
            PanelElement selected = _panel.Element(ToolPanel.MaxTabs + 4);
            Assert.LessOrEqual(selected.Centre.x + 0.035f, inner * 0.5f + 1e-4f, "scrolled so the selected one is in view");
            Assert.Less(_panel.RowScroll(0), 0f);
        }

        sealed class ApplyRowPage : IPanelPage
        {
            public int Count => 3;
            public int Columns => 5;
            public void Describe(int i, ref CardInfo c)
            {
                c.Row = 0;
                c.Title = i == 0 ? "Apply to" : i == 1 ? "My zone" : "Outside";
                c.Kind = i == 0 ? CardKind.Title : CardKind.Toggle;
                c.Width = i == 0 ? 0.064f : 0.078f;
                c.Selected = i == 1;
            }
            public bool Press(int index) => true;
        }

        // Device #13/#14 (build 0.0.2): the title and its toggles sit on one line, and the selected segment is a pill
        // whose text contrasts with its fill in both themes.
        [Test]
        public void TitleAndToggles_ShareOneCentreLine_TheChosenOptionIsInkAndUnderlined_NoPills()
        {
            _panel.Configure(new[] { "Style" }, new IPanelPage[] { new ApplyRowPage() });
            PanelElement title = _panel.Element(ToolPanel.MaxTabs), mine = _panel.Element(ToolPanel.MaxTabs + 1), outside = _panel.Element(ToolPanel.MaxTabs + 2);
            Assert.AreEqual(title.Centre.y, mine.Centre.y, 1e-5f);
            Assert.AreEqual(mine.Centre.y, outside.Centre.y, 1e-5f);
            Assert.AreEqual(0.078f, mine.Size.x, 1e-5f, "sized to its words, not a fifth of the row");
            foreach (PanelStyle style in new[] { PanelStyle.Default, PanelThemes.DarkDefault })
            {
                PanelStyle.Slot on = PanelElement.Look(style, ElementState.Selected, Color.clear, false, CardKind.Toggle);
                PanelStyle.Slot off = PanelElement.Look(style, ElementState.Normal, Color.clear, false, CardKind.Toggle);
                Assert.AreEqual(0f, on.Fill.a, "text options: no pill behind the chosen one");
                Assert.AreEqual(0f, off.Fill.a, "nor behind the others");
                Assert.AreEqual(style.Normal.Text, on.Text, "the chosen option is full ink");
                Assert.AreEqual(style.TextSecondary, off.Text, "the others are secondary ink");
                Assert.Greater(Contrast(style.Surface.Fill, on.Text), 4.5f, "it reads on the panel");
                Assert.Greater(PanelElement.Look(style, ElementState.Hover, Color.clear, false, CardKind.Toggle).Fill.a, 0f, "a fill shows under the hand");
                foreach (ElementState state in new[] { ElementState.Normal, ElementState.Hover, ElementState.Pressed, ElementState.Disabled })
                    Assert.AreEqual(0f, style.Get(state).Rim.a, state + ": chips are fill only, no rim");
                Assert.Greater(style.Selected.Rim.a, 0.5f, "the chosen state keeps its ring");
                Assert.GreaterOrEqual(style.Surface.Fill.a, 0.88f, "the surface is opaque enough to read over a bright screen");
            }
            // On the panel: the chosen option's quad is a 1.8 mm underline at its bottom edge, in the text's ink.
            Transform quad = mine.Root.GetChild(0);
            Assert.AreEqual(PanelElement.UnderlineHeight, quad.localScale.y, 1e-6f);
            Assert.AreEqual(-mine.Size.y * 0.5f + PanelElement.UnderlineHeight * 0.5f, quad.localPosition.y, 1e-6f);
            Assert.AreEqual(mine.Size.y, outside.Root.GetChild(0).localScale.y, 1e-6f, "the other option's quad is the whole option (its hover fill)");
            Assert.AreEqual(PanelElement.FontSize(PanelElement.BodyMm), mine.Text.fontSizeMax, 1e-6f, "options are 14 mm");
            Assert.AreEqual(PanelElement.FontSize(PanelElement.SectionMm), title.Text.fontSize, 1e-6f, "a section name is 12 mm");
            Object.DestroyImmediate(PanelThemes.DarkDefault);
        }

        static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        static float Luminance(Color c)
        {
            Color l = c.linear;
            return 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
        }

        // Device #12/#15: the panel faces the eyes (no keystone), seated low or dragged high.
        [Test]
        public void FaceEyes_PointsThePanelsNormalAtTheHead()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            foreach (Vector3 panel in new[] { new Vector3(0f, 0.85f, 0.35f), new Vector3(0.4f, 1.5f, 0.5f), new Vector3(-0.3f, 1.2f, 0.6f) })
            {
                Quaternion r = ToolPanel.FaceEyes(head, panel, Quaternion.identity);
                Assert.Greater(Vector3.Dot(r * Vector3.forward, (panel - head).normalized), 0.9999f);
                Assert.Greater(Vector3.Dot(r * Vector3.up, Vector3.up), 0f, "upright, not rolled");
            }
            Assert.AreEqual(Quaternion.identity, ToolPanel.FaceEyes(head, head, Quaternion.identity));
        }

        sealed class TallPage : IPanelPage
        {
            public int Count => 12;
            public int Columns => 1;
            public void Describe(int i, ref CardInfo c)
            {
                c.Row = i;
                c.Title = i == 0 ? "A long explanation that needs a second line to be read in full" : "Chip " + i;
                c.Kind = i == 0 ? CardKind.Title : CardKind.Chip;
                c.Lines = i == 0 ? 2 : 0;
            }
            public bool Press(int index) => true;
        }

        // Device #20: a page taller than the panel never hangs past its bottom, and a title can wrap to two lines.
        [Test]
        public void TallPage_CardsPastTheBottomAreHidden_TwoLineTitleIsTwiceAsTall()
        {
            _panel.Configure(new[] { "AI" }, new IPanelPage[] { new TallPage() });
            PanelMetrics m = _panel.Metrics;
            Assert.AreEqual(m.TitleHeight * 2f, _panel.Element(ToolPanel.MaxTabs).Size.y, 1e-5f);
            float bottom = m.Size.y * 0.5f - m.Size.y;
            for (int i = 1; i < 12; i++)
            {
                PanelElement e = _panel.Element(ToolPanel.MaxTabs + i);
                if (e.Visible) Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom, $"chip {i} is inside the panel");
            }
            Assert.IsFalse(_panel.Element(ToolPanel.MaxTabs + 11).Visible, "the last chips do not fit: hidden");
            Assert.AreEqual(m.Size.y, _panel.Height, 1e-5f);
        }

        // Device batch 4: the row's edges never wash the pills white; chevrons show where more items hide.
        [Test]
        public void ScrollingRow_HardClipsOnlyHidingEdges_ChevronsScrollIt()
        {
            var page = new FurnitureLikePage { ChipWidth = 0.07f };
            _panel.Configure(new[] { "Furniture" }, new IPanelPage[] { page });
            Assert.IsFalse(_panel.RowArrowShown(ToolPanel.RowPrev), "at the start nothing hides on the left");
            Assert.IsTrue(_panel.RowArrowShown(ToolPanel.RowNext));
            PanelElement first = _panel.Element(ToolPanel.MaxTabs);
            Assert.Less(first.Clip.z, 0.002f, "a hard edge, not a fade over the glass");
            Assert.AreEqual(-1f, first.Clip.x, "the first pill is not clipped at all");

            _panel.BeginPress(ToolPanel.RowNext, false);
            _panel.Release(ToolPanel.RowNext);
            Assert.Less(_panel.RowScroll(0), 0f);
            Assert.IsTrue(_panel.RowArrowShown(ToolPanel.RowPrev), "now items hide on the left too");
            for (int i = 0; i < 4; i++) { _panel.BeginPress(ToolPanel.RowNext, false); _panel.Release(ToolPanel.RowNext); }
            Assert.IsFalse(_panel.RowArrowShown(ToolPanel.RowNext), "at the end");

            ToolPanel.HiddenEnds(0f, 0.2f, 0.268f, out bool l, out bool r);
            Assert.IsFalse(l || r, "a row that fits has no chevrons");
        }

        // Device batch 4: the pending banner is dark with white text in both themes, above the panel; toasts stack over it.
        [Test]
        public void PendingBanner_DarkAboveThePanel_ToastAboveIt()
        {
            foreach (PanelTheme theme in new[] { PanelTheme.Light, PanelTheme.Dark })
            {
                _panel.SetTheme(theme);
                _panel.ShowHint("Pinch the floor outside your zone to place Linen two-seat sofa · Cancel", pressable: true);
                PanelElement banner = _panel.Element(ToolPanel.Hint);
                Assert.Less(banner.ForcedFill.r + banner.ForcedFill.g + banner.ForcedFill.b, 0.4f, "near-black");
                Assert.AreEqual(Color.white, banner.ForcedText);
                Assert.Greater(banner.Centre.y - banner.Size.y * 0.5f, _panel.Metrics.Size.y * 0.5f, "above the top edge");
                _panel.ShowToast("Undone");
                Assert.Greater(_panel.Element(ToolPanel.Toast).Centre.y, banner.Centre.y + banner.Size.y * 0.5f, "the toast stacks above");
                _panel.HideHint();
            }
            Object.DestroyImmediate(PanelThemes.DarkDefault);
        }

        [Test]
        public void CardNames_SplitAtTheBalancedSpace_NeverInsideAHyphenatedWord()
        {
            Assert.AreEqual("Linen\ntwo-seat sofa", PanelText.TwoLines("Linen two-seat sofa"));
            Assert.AreEqual("Leather\nlounge sofa", PanelText.TwoLines("Leather lounge sofa"));
            Assert.AreEqual("Oak desk", PanelText.TwoLines("Oak desk"), "short: one line");
            Assert.AreEqual("Extraordinarily", PanelText.TwoLines("Extraordinarily"), "one word: left to the ellipsis");
        }

        // Batch 6: the gear's settings sheet shows in place of the tab's page; selecting a tab closes it.
        [Test]
        public void Sheet_ReplacesThePage_ATabClosesIt()
        {
            var tab = new SectionPage();
            var sheet = new TallPage();
            _panel.Configure(new[] { "Style" }, new IPanelPage[] { tab });
            _panel.ShowSheet(sheet);
            Assert.AreSame(sheet, _panel.Sheet);
            Assert.AreEqual("Chip 1", _panel.Element(ToolPanel.MaxTabs + 1).Text.text, "the sheet's items");
            _panel.SelectTab(0);
            Assert.IsNull(_panel.Sheet);
            Assert.AreEqual("x", _panel.Element(ToolPanel.MaxTabs).Text.text, "the tab's page again");
        }

        [Test]
        public void DraggingARow_ScrollsIt_AndDoesNotActivate()
        {
            var page = new FurnitureLikePage { ChipWidth = 0.07f };
            _panel.Configure(new[] { "Furniture" }, new IPanelPage[] { page });
            _panel.BeginPress(ToolPanel.MaxTabs + 1, false);
            _panel.BeginScroll(0, 0f);
            _panel.DragScroll(-0.002f);
            Assert.AreEqual(0f, _panel.RowScroll(0), "within the slop: still a press");
            _panel.DragScroll(-0.05f);
            Assert.AreEqual(-0.05f, _panel.RowScroll(0), 1e-4f);
            Assert.AreEqual(ToolPanel.MaxTabs + 1, _panel.Pressed, "the press is kept while scrolling");
        }

        [Test]
        public void MeasureWidth_GrowsWithTheText_AndLeavesNoObjectBehind()
        {
            int before = Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            float shortWidth = PanelElement.MeasureWidth("Beds", 13f), longWidth = PanelElement.MeasureWidth("Seating and more", 13f);
            Assert.Greater(longWidth, shortWidth);
            Assert.Greater(shortWidth, 0.01f);
            Assert.AreEqual(before, Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
        }

        [Test]
        public void ClippedPartOfAScrolledChip_IsNotPressable()
        {
            var page = new FurnitureLikePage { ChipWidth = 0.07f };
            _panel.Configure(new[] { "Furniture" }, new IPanelPage[] { page });
            float half = (_panel.Metrics.Size.x - 2f * _panel.Metrics.Padding) * 0.5f;
            _panel.BeginPress(ToolPanel.MaxTabs, false);
            _panel.BeginScroll(0, 0f);
            _panel.DragScroll(-0.03f); // the first chip now straddles the left edge
            _panel.Release(-1);
            PanelElement first = _panel.Element(ToolPanel.MaxTabs);
            Assert.Less(first.Centre.x - first.Size.x * 0.5f, -half, "it crosses the edge");
            Assert.AreEqual(ToolPanel.Back, _panel.Find(new Vector2(-half - 0.005f, first.Centre.y)), "the clipped part, in the side padding, is not the chip");
            Assert.AreEqual(ToolPanel.MaxTabs, _panel.Find(new Vector2(first.Centre.x + first.Size.x * 0.4f, first.Centre.y)), "the visible part still is");
            Vector4 inner = _panel.Element(ToolPanel.MaxTabs + 2).Clip;
            Assert.AreEqual(new Vector4(-1f, 1f, inner.z, 1f), inner, "a chip inside the row keeps one constant clip (no per-frame re-fade)");
        }

        sealed class CentredPagerPage : IPanelPage
        {
            public int Count => 3;
            public int Columns => 1;
            public void Describe(int i, ref CardInfo c)
            {
                c.Row = 0; c.AlignCenter = true; c.Width = i == 1 ? 0.05f : 0.036f;
                c.Kind = i == 1 ? CardKind.Title : CardKind.Chip; c.Title = i == 1 ? "1 / 2" : "<";
                c.Strong = i != 1; c.Disabled = i == 0;
            }
            public bool Press(int index) => true;
        }

        // Device batch 10: the furniture pager sat at the bottom right, nearly invisible on the glass.
        [Test]
        public void CentredRow_IsCentred_AndStrongChevronsReadOnTheGlass()
        {
            _panel.Configure(new[] { "Furniture" }, new IPanelPage[] { new CentredPagerPage() });
            PanelElement prev = _panel.Element(ToolPanel.MaxTabs), label = _panel.Element(ToolPanel.MaxTabs + 1), next = _panel.Element(ToolPanel.MaxTabs + 2);
            Assert.AreEqual(0f, label.Centre.x, 1e-4f, "the count sits on the panel's centre line");
            Assert.AreEqual(-next.Centre.x, prev.Centre.x, 1e-4f);
            Assert.AreEqual(ElementState.Normal, prev.State, "off: a visible fill, not the faint disabled look");
            Assert.AreEqual(ElementState.Selected, next.State, "live: the dark pill");
        }

        // Device (build 0ddb26d): the undo arrow took pinches with nothing to undo. It greys instead.
        [Test]
        public void UndoRedoArrows_GreyWhenThereIsNothingToDo()
        {
            bool canUndo = false, canRedo = true;
            _panel.CanUndo = () => canUndo;
            _panel.CanRedo = () => canRedo;
            _panel.Invalidate();
            Assert.AreEqual(ElementState.Disabled, _panel.Element(ToolPanel.UndoButton).State);
            Assert.AreEqual(ElementState.Normal, _panel.Element(ToolPanel.RedoButton).State);
            canUndo = true; canRedo = false;
            _panel.Invalidate();
            Assert.AreEqual(ElementState.Normal, _panel.Element(ToolPanel.UndoButton).State);
            Assert.AreEqual(ElementState.Disabled, _panel.Element(ToolPanel.RedoButton).State);
        }

        // The human (a4d289e): "no hover shows; I can't select unless I go right up to it". A hand near the panel hovers and
        // presses the element under its fingers, by one rule, before any ray.
        [Test]
        public void AHandNearThePanel_HoversAndPressesTheElementUnderItsFingers()
        {
            PanelElement tab = _panel.Element(0);
            foreach (float z in new[] { -0.07f, -0.04f, -0.01f, 0.02f })
            {
                Vector3 at = _panel.transform.TransformPoint(new Vector3(tab.Centre.x, tab.Centre.y, z));
                Assert.AreEqual(0, _panel.Near(at), $"{z * 100f:0} cm from the panel: the tab under the fingers");
            }
            Assert.AreEqual(-1, _panel.Near(_panel.transform.TransformPoint(new Vector3(tab.Centre.x, tab.Centre.y, -0.12f))), "12 cm away: the ray decides");
            Assert.IsTrue(ToolPanel.NearDepth(-0.08f)); Assert.IsFalse(ToolPanel.NearDepth(-0.09f)); Assert.IsFalse(ToolPanel.NearDepth(0.04f));
        }

        [Test]
        public void ADrag_NeverBringsThePanelInsideArmsReach()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            Assert.AreEqual(ToolPanel.MinDragDistance, Vector3.Distance(head, ToolPanel.OutOfReach(head, new Vector3(0.1f, 1.3f, 0.25f))), 1e-4f);
            var seat = new Vector3(0f, 1f, 0.62f);
            Assert.AreEqual(seat, ToolPanel.OutOfReach(head, seat), "at the seat's distance: untouched");
        }
    }
}

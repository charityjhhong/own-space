using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class FurniturePageTests
    {
        sealed class FakeOutside : IOutsideArm
        {
            public CatalogItem Armed { get; private set; }
            public void Arm(CatalogItem item) => Armed = item;
            public string SelectedName { get; set; }
            public int Rotations, Removals;
            public bool RotateSelected() { Rotations++; return true; }
            public bool RemoveSelected() { Removals++; SelectedName = null; return true; }
            public void Deselect() => SelectedName = null;
        }

        sealed class FakeRedesign : IRedesignTarget
        {
            public FurnitureAnchor Selected { get; set; }
            public readonly List<(CatalogItem item, FitResult fit)> List = new List<(CatalogItem, FitResult)>();
            public IReadOnlyList<(CatalogItem item, FitResult fit)> Fits => List;
            public string CurrentDesignId { get; set; }
            public bool Refuse;
            public int Applied = -1;
            public bool ApplyDesign(int index, out string reason) { reason = Refuse ? "Doesn't fit here" : null; if (!Refuse) Applied = index; return !Refuse; }
            public int KeptReal;
            public bool KeepReal() { KeptReal++; CurrentDesignId = null; return true; }
            public void Deselect() => Selected = null;
            public System.Collections.Generic.List<AdjustAction> Adjusted = new System.Collections.Generic.List<AdjustAction>();
            public bool AdjustSelected(AdjustAction action) { Adjusted.Add(action); return CurrentDesignId != null; }
            public System.Collections.Generic.HashSet<AdjustAction> AtLimit = new System.Collections.Generic.HashSet<AdjustAction>();
            public bool CanAdjust(AdjustAction action) => !AtLimit.Contains(action);
            public bool RealTopOn { get; set; }
        }

        static CatalogItem Item(string id, string kind, FurnitureRole role = FurnitureRole.Other) =>
            new CatalogItem { id = id, name = id, kind = kind, role = role, zones = new List<string> { "outside" } };

        FakeOutside _outside;
        FakeRedesign _redesign;
        FurniturePage _page;

        [SetUp]
        public void SetUp()
        {
            var items = new List<CatalogItem>();
            for (int i = 0; i < 8; i++) items.Add(Item("sofa-" + i, "sofa", FurnitureRole.Couch));
            items.Add(Item("lamp-0", "lamp"));
            items.Add(Item("desk-0", "desk", FurnitureRole.Table));
            const string thumbs = "{\"cellWidth\":192,\"cellHeight\":128,\"width\":1920,\"height\":896,\"items\":[{\"id\":\"sofa-0\",\"x\":192,\"y\":128}]}";
            _outside = new FakeOutside();
            _redesign = new FakeRedesign();
            _page = new FurniturePage(new Catalog(items), _outside, _redesign, new FurnitureThumbs(thumbs));
        }

        CardInfo Card(int i) { var c = new CardInfo(); _page.Describe(i, ref c); return c; }

        [Test]
        public void Thumbs_UseTheAtlasCell()
        {
            var t = new FurnitureThumbs("{\"cellWidth\":192,\"cellHeight\":128,\"width\":1920,\"height\":896,\"items\":[{\"id\":\"a\",\"x\":192,\"y\":128}]}");
            Vector4 r = t.Rect("a");
            Assert.AreEqual(0.1f, r.x, 1e-5f);
            Assert.AreEqual(128f / 896f, r.y, 1e-5f);
            Assert.AreEqual(0.1f, r.z, 1e-5f);
            Assert.AreEqual(1f - 256f / 896f, r.w, 1e-5f, "v from the bottom");
            Assert.AreEqual(Vector4.zero, t.Rect("missing"));
        }

        [Test]
        public void Categories_MapCatalogKinds()
        {
            Assert.AreEqual(0, FurniturePage.CategoryOf("armchair"));
            Assert.AreEqual(2, FurniturePage.CategoryOf("coffee-table"));
            Assert.AreEqual(5, FurniturePage.CategoryOf("clutter-cover"));
            Assert.AreEqual(6, FurniturePage.CategoryOf("door-trim"));
            Assert.AreEqual(-1, FurniturePage.CategoryOf("spaceship"));
        }

        [Test]
        public void Outside_SixCardsPerPage_PagerAndArming()
        {
            Assert.AreEqual(16, _page.Count, "outside: no Keep row (no empty row adds height)");
            Assert.AreEqual(CardKind.Toggle, Card(0).Kind);
            Assert.IsTrue(Card(0).Selected, "Seating first");
            Assert.Greater(Card(0).Width, 0.02f);
            Assert.AreEqual("sofa-0", Card(7).Title);
            Assert.AreNotEqual(Vector4.zero, Card(7).Thumb);
            Assert.AreEqual("1 / 2", Card(14).Title);
            Assert.IsTrue(Card(13).Disabled, "no previous page");
            // Device batch 10: the pager is centred and reads on the glass.
            Assert.IsTrue(Card(13).AlignCenter && Card(14).AlignCenter && Card(15).AlignCenter);
            Assert.IsFalse(Card(15).AlignRight);
            Assert.IsTrue(Card(13).Strong && Card(15).Strong, "chevrons: the dark pill when live, a visible fill when off");
            Assert.AreEqual(CardKind.Title, Card(14).Kind, "the count is ink text, not a faint disabled chip");
            Assert.AreEqual(ElementState.Selected, ToolPanel.StateOf(Card(15)));
            Assert.AreEqual(ElementState.Normal, ToolPanel.StateOf(Card(13)), "off: dimmer but readable");
            Assert.GreaterOrEqual(Card(15).Width, 0.036f);
            Assert.IsTrue(_page.Press(15));
            Assert.AreEqual("2 / 2", Card(14).Title);
            Assert.AreEqual("sofa-6", Card(7).Title);
            Assert.AreEqual(CardKind.Title, Card(9).Kind, "an empty slot on the last page is invisible");
            Assert.AreEqual("", Card(9).Title);
            Assert.AreEqual("<", Card(13).Title, "ASCII until the chevron icons are set");
            Assert.IsTrue(_page.Press(7));
            Assert.AreEqual("sofa-6", _outside.Armed.id, "pinch a card, then the floor");
            Assert.IsTrue(Card(7).Selected);
            _page.Press(4); // Lights
            Assert.AreEqual("lamp-0", Card(7).Title);
            Assert.AreEqual(0, _page.Page);
        }

        [Test]
        public void MyZone_LocksToTheBoxRole_AndAppliesFittingDesigns()
        {
            var desk = Item("desk-9", "desk", FurnitureRole.Table);
            _redesign.Selected = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1f, 0.75f, 0.6f), 0f));
            _redesign.List.Add((desk, default));
            _page.Refresh();
            Assert.AreEqual(2, _page.Category, "Tables");
            Assert.IsTrue(Card(0).Disabled, "other categories are locked");
            Assert.AreEqual("desk-9", Card(7).Title);
            Assert.IsTrue(_page.Press(7));
            Assert.AreEqual(0, _redesign.Applied);
            _redesign.CurrentDesignId = "desk-9";
            Assert.IsTrue(Card(7).Selected);
            _redesign.Refuse = true;
            Assert.IsFalse(_page.Press(7));
            Assert.AreEqual("Doesn't fit here", _page.LastRefusal);
            Assert.AreEqual(FurniturePage.KeepRealLabel, Card(18).Title, "keep the real piece, on the Furniture tab");
            Assert.IsTrue(_page.Press(18));
            Assert.AreEqual(1, _redesign.KeptReal);
            _redesign.List.Clear();
            _page.Refresh();
            Assert.AreEqual("No design fits this desk (1.00 × 0.60 m)", Card(7).Title, "a box with no fitting design says so, and why");
            Assert.AreEqual(CardKind.Title, Card(8).Kind);
        }

        // Device #17/#21: in real-box mode the page says why the other categories are greyed and offers a way out.
        [Test]
        public void MyZone_SaysWhatFits_AndPlaceOutsideLeavesTheBox()
        {
            _redesign.Selected = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1f, 0.75f, 0.6f), 0f));
            _page.Refresh();
            Assert.AreEqual(21, _page.Count, "the pager, the real top toggle's two items, Keep, Place outside, the note");
            Assert.AreEqual(FurniturePage.LeaveLabel, Card(19).Title);
            Assert.AreEqual(Card(18).Row, Card(19).Row, "beside Keep the real one");
            Assert.LessOrEqual(Card(18).Width + Card(19).Width + 0.008f, 0.268f, "both fit on one line");
            Assert.AreEqual("Designs that fit this desk", Card(20).Title);
            Assert.AreEqual(CardKind.Title, Card(20).Kind);
            Assert.IsTrue(_page.Press(19));
            Assert.IsNull(_redesign.Selected, "back to placing outside");
            _page.Refresh();
            Assert.AreEqual(16, _page.Count);
            Assert.AreEqual("Designs that fit this piece", FurniturePage.FitNote((FurnitureRole)99));
        }

        // Device day: My room restyles the real piece and never places outside; Add only places outside.
        [Test]
        public void MyRoomTab_RestylesOnly_AddTab_PlacesOnly()
        {
            var catalog = new Catalog(new[] { Item("desk-0", "desk", FurnitureRole.Table), Item("sofa-0", "sofa", FurnitureRole.Couch) });
            var mine = new FurniturePage(catalog, _outside, _redesign, null, null, FurniturePage.Job.MyFurniture);
            var add = new FurniturePage(catalog, _outside, _redesign, null, null, FurniturePage.Job.AddFurniture);
            CardInfo Mine(int i) { var c = new CardInfo(); mine.Describe(i, ref c); return c; }

            Assert.AreEqual(8, mine.Count, "nothing selected: one line");
            Assert.AreEqual(FurniturePage.EmptyZone, Mine(7).Title);
            Assert.IsFalse(mine.Press(7));
            Assert.IsNull(_outside.Armed, "My room never arms the floor");

            var desk = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1f, 0.75f, 0.6f), 0f));
            _redesign.Selected = desk;
            _redesign.List.Add((catalog.Items[0], default));
            mine.Refresh(); add.Refresh();
            Assert.AreEqual("desk-0", Mine(7).Title);
            Assert.IsTrue(mine.Press(7));
            Assert.AreEqual(0, _redesign.Applied, "the card restyles the real desk");
            Assert.IsNull(_outside.Armed);
            Assert.AreEqual("", Mine(17).Title, "no Place outside chip on My room");

            Assert.AreEqual(16, add.Count, "Add ignores the selection");
            Assert.IsTrue(add.Press(7));
            Assert.IsNotNull(_outside.Armed, "Add arms the floor placement");
        }

        [Test]
        public void MyRoom_OpensOnTheNearestDesk_ElseTheNearestPiece()
        {
            var bed = new FurnitureAnchor("bed", FurnitureRole.Bed, new OrientedBox(new Vec3(0.5f, 0.3f, 0.5f), new Vec3(2f, 0.5f, 1.6f), 0f));
            var far = new FurnitureAnchor("far desk", FurnitureRole.Table, new OrientedBox(new Vec3(3f, 0.4f, 3f), new Vec3(1f, 0.75f, 0.6f), 0f));
            var near = new FurnitureAnchor("near desk", FurnitureRole.Table, new OrientedBox(new Vec3(1.5f, 0.4f, 1f), new Vec3(1f, 0.75f, 0.6f), 0f));
            var head = new UnityEngine.Vector3(0f, 1.2f, 0f);
            Assert.AreSame(near, EditMode.Nearest(new[] { bed, far, near }, head), "desks first, then the nearest");
            Assert.AreSame(bed, EditMode.Nearest(new[] { bed }, head), "no desk: the nearest piece");
            Assert.IsNull(EditMode.Nearest(new FurnitureAnchor[0], head));
        }

        // Device day (d520910): with no floor outside, Add says so instead of arming a card that cannot be placed.
        [Test]
        public void Add_WithNoOutsideSpace_SaysSo_AndDoesNotArm()
        {
            var add = new FurniturePage(new Catalog(new[] { Item("sofa-0", "sofa", FurnitureRole.Couch) }), _outside, _redesign, null, null, FurniturePage.Job.AddFurniture);
            bool full = true;
            add.NoOutsideSpace = () => full;
            var c = new CardInfo(); add.Describe(7, ref c);
            Assert.AreEqual(FurniturePage.NoSpace, c.Title);
            Assert.AreEqual(8, add.Count);
            Assert.IsFalse(add.Press(7));
            Assert.IsNull(_outside.Armed);
            full = false;
            add.Refresh();
            Assert.IsTrue(add.Press(7), "room again: the card arms");
            Assert.IsNotNull(_outside.Armed);
        }

        // Device batch 7: a quick pinch on a placed piece selects it; the Add tab shows its card.
        [Test]
        public void Add_ShowsTheSelectedPiece_RemoveDoneAndHowToTurn()
        {
            var add = new FurniturePage(new Catalog(new[] { Item("sofa-0", "sofa", FurnitureRole.Couch) }), _outside, _redesign, null, null, FurniturePage.Job.AddFurniture);
            Assert.AreEqual(16, add.Count, "nothing selected: no card");
            _outside.SelectedName = "Linen two-seat sofa";
            Assert.AreEqual(20, add.Count);
            CardInfo C(int k) { var c = new CardInfo(); add.Describe(k, ref c); return c; }
            Assert.AreEqual("Selected: Linen two-seat sofa", C(16).Title);
            Assert.AreEqual(FurniturePage.RemoveLabel, C(17).Title);
            Assert.IsTrue(C(17).Danger, "Remove is the quiet red action");
            Assert.AreEqual(FurniturePage.MoveHint, C(19).Title, "batch 10: turning is on the piece, not a Rotate button");
            Assert.IsFalse(add.Press(19));
            Assert.AreEqual(0, _outside.Rotations);
            Assert.IsTrue(add.Press(18));
            Assert.IsNull(_outside.SelectedName, "Done lets go");
            Assert.AreEqual(16, add.Count);
            var mine = new FurniturePage(Catalog.Empty, _outside, _redesign, null, null, FurniturePage.Job.MyFurniture);
            _outside.SelectedName = "x";
            Assert.AreEqual(8, mine.Count, "My room never shows a placed piece's card");
        }

        // Device batch 9: the real desk had no fitting design; the row showed Seating and two cut-off words.
        [Test]
        public void NoFittingDesign_LocksToTables_AndSaysWhyOnTwoLines()
        {
            var desk = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
            _redesign.Selected = desk; // no fits in the list
            _page.Refresh();
            Assert.AreEqual(2, _page.Category, "Tables, even with nothing to show");
            CardInfo line = Card(7);
            Assert.AreEqual("No design fits this desk (1.20 × 0.60 m)", line.Title);
            Assert.AreEqual(2, line.Lines, "wraps instead of cutting off");
            Assert.Greater(line.Width, 0.25f, "spans the panel, not one card column");
            Assert.AreEqual(1, Card(20).Lines, "the fit note is one line across the panel");
            Assert.AreEqual(0.268f, Card(20).Width, 1e-5f);
            Assert.AreEqual(0, FurniturePage.CategoryOfRole(FurnitureRole.Couch));
            Assert.AreEqual(1, FurniturePage.CategoryOfRole(FurnitureRole.Bed));
        }

        // Device batch 8: drag a card off the Add tab onto the floor.
        [Test]
        public void DraggingAnAddCardOut_ArmsIt_MyRoomDoesNot()
        {
            var catalog = new Catalog(new[] { Item("sofa-0", "sofa", FurnitureRole.Couch) });
            var add = new FurniturePage(catalog, _outside, _redesign, null, null, FurniturePage.Job.AddFurniture);
            Assert.IsTrue(add.DragOut(7));
            Assert.AreEqual("sofa-0", _outside.Armed.id);
            _outside.Arm(null);
            Assert.IsFalse(add.DragOut(8), "an empty slot");
            Assert.IsFalse(add.DragOut(0), "a category chip");
            var mine = new FurniturePage(catalog, _outside, _redesign, null, null, FurniturePage.Job.MyFurniture);
            Assert.IsFalse(mine.DragOut(7));
            Assert.IsNull(_outside.Armed);
        }

        // Device batch 10: fine-tune the design on the real desk from My room.
        [Test]
        public void MyRoom_AdjustFit_TurnsResizesAndReturns()
        {
            var mine = new FurniturePage(Catalog.Empty, _outside, _redesign, null, null, FurniturePage.Job.MyFurniture);
            CardInfo C(int k) { var c = new CardInfo(); mine.Describe(k, ref c); return c; }
            _redesign.Selected = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
            mine.Refresh();
            Assert.AreEqual("", C(19).Title, "no design yet: nothing to adjust");
            _redesign.CurrentDesignId = "desk-9";
            Assert.AreEqual(FurniturePage.AdjustLabel, C(19).Title);
            Assert.IsTrue(mine.Press(19));
            Assert.AreEqual(8, mine.Count, "the adjust controls (turning is the handle on the piece; the top choice is on My room)");
            Assert.AreEqual(FurniturePage.AdjustTitle, C(0).Title);
            for (int k = 0; k < mine.Count; k++) StringAssert.DoesNotContain("Turn 90", C(k).Title);
            Assert.IsTrue(mine.Press(1));
            Assert.IsTrue(mine.Press(2));
            for (int k = 0; k < mine.Count; k++) StringAssert.DoesNotContain("Real top", C(k).Title, "one place for the top choice: My room");
            CollectionAssert.AreEqual(new[] { AdjustAction.Reset, AdjustAction.Wider }, _redesign.Adjusted);
            Assert.IsTrue(mine.Press(7), "Done");
            Assert.AreEqual(21, mine.Count, "back to the designs");
        }

        // At a limit the resize button is grey (disabled, not a red refusal) and the hint says which limit.
        [Test]
        public void MyRoom_AdjustFit_AtALimit_ButtonGreys_AndHintSaysWhy()
        {
            var mine = new FurniturePage(Catalog.Empty, _outside, _redesign, null, null, FurniturePage.Job.MyFurniture);
            CardInfo C(int k) { var c = new CardInfo(); mine.Describe(k, ref c); return c; }
            _redesign.Selected = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
            _redesign.CurrentDesignId = "desk-9";
            mine.Refresh();
            Assert.IsTrue(mine.Press(19), "Adjust fit");
            Assert.IsFalse(C(5).Disabled, "Shallower");
            Assert.AreEqual(FurniturePage.NudgeHint, C(6).Title);
            _redesign.AtLimit.Add(AdjustAction.Shallower);
            Assert.IsTrue(C(5).Disabled, "Shallower greys out at the limit");
            Assert.IsFalse(C(5).Danger);
            Assert.AreEqual(FurniturePage.SmallestDepth, C(6).Title);
            Assert.IsFalse(mine.Press(5));
            CollectionAssert.DoesNotContain(_redesign.Adjusted, AdjustAction.Shallower);
            _redesign.AtLimit.Add(AdjustAction.Wider);
            Assert.AreEqual(FurniturePage.SmallestDepth + ", " + FurniturePage.LargestWidth, C(6).Title);
            for (int k = 0; k < mine.Count; k++) StringAssert.DoesNotContain("…", C(k).Title);
        }

        // Device, build dfb22f4: "move the page number to the centre, and the bottom line must be one line".
        [Test]
        public void OnTheRealPanel_ThePagerIsCentred_TheFitNoteIsOneLine_NothingPastTheBottom()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                var items = new List<CatalogItem>();
                for (int i = 0; i < 8; i++) items.Add(Item("sofa-" + i, "sofa", FurnitureRole.Couch));
                var catalog = new Catalog(items);
                TMPro.TMP_FontAsset font = panel.Fonts?.Regular;
                var mine = new FurniturePage(catalog, _outside, _redesign, null, font, FurniturePage.Job.MyFurniture);
                var add = new FurniturePage(catalog, _outside, _redesign, null, font, FurniturePage.Job.AddFurniture);
                panel.Configure(new[] { "My room", "Add" }, new IPanelPage[] { mine, add });

                void Check(FurniturePage page, string where)
                {
                    panel.Invalidate();
                    float bottom = panel.Metrics.Size.y * 0.5f - panel.Height;
                    Assert.LessOrEqual(panel.Height, panel.Metrics.Size.y + 1e-5f, where);
                    PanelElement prev = panel.Element(ToolPanel.MaxTabs + 13), label = panel.Element(ToolPanel.MaxTabs + 14), next = panel.Element(ToolPanel.MaxTabs + 15);
                    Assert.IsTrue(prev.Visible && label.Visible && next.Visible, where + ": the pager is shown");
                    Assert.AreEqual(0f, (prev.Centre.x - prev.Size.x * 0.5f + next.Centre.x + next.Size.x * 0.5f) * 0.5f, 1e-4f, where + ": the pager is centred on the panel");
                    label.Text.ForceMeshUpdate();
                    Vector3 mid = go.transform.InverseTransformPoint(label.Text.transform.TransformPoint(label.Text.textBounds.center));
                    Assert.AreEqual(0f, mid.x, 0.001f, where + ": the count itself is centred between the chevrons (\"" + label.Text.text + "\")");
                    Assert.AreEqual(PanelElement.FontSize(PanelElement.SectionMm), label.Text.fontSize, 1e-5f, where + ": the count is not shrunk");
                    Assert.IsFalse(label.Text.isTextTruncated, where);
                    var c = new CardInfo();
                    for (int i = 7; i < page.Count; i++) // after the category row (it scrolls sideways and hides what is off its edge)
                    {
                        PanelElement e = panel.Element(ToolPanel.MaxTabs + i);
                        if (!e.Visible) { c = new CardInfo(); page.Describe(i, ref c); Assert.IsTrue(string.IsNullOrEmpty(c.Title) && c.Icon == Vector4.zero, where + ": only empty slots are hidden (item " + i + ")"); continue; }
                        Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom - 1e-4f, where + ": item " + i + " is inside the panel's bottom edge");
                    }
                }

                // Add: 8 sofas, two pages.
                panel.SelectTab(1);
                Check(add, "Add · page 1");
                Assert.IsTrue(add.Press(15)); Check(add, "Add · page 2");

                // My room with a desk selected and 8 designs: the pager, Keep the real one, and the fit note.
                _redesign.Selected = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
                foreach (CatalogItem item in items) _redesign.List.Add((item, default));
                mine.Refresh();
                panel.SelectTab(0);
                Check(mine, "My room · page 1");
                PanelElement note = panel.Element(ToolPanel.MaxTabs + 20);
                Assert.AreEqual("Designs that fit this desk", note.Text.text);
                note.Text.ForceMeshUpdate();
                Assert.AreEqual(1, note.Text.textInfo.lineCount, "one line");
                Assert.AreEqual(PanelElement.FontSize(PanelElement.SectionMm), note.Text.fontSize, 1e-5f, "at the normal 12 mm, not shrunk");
                Assert.IsFalse(note.Text.isTextTruncated);
                Assert.AreEqual(panel.Metrics.TitleHeight, note.Size.y, 1e-5f, "one line tall");
                foreach (FurnitureRole role in new[] { FurnitureRole.Other, FurnitureRole.Table, FurnitureRole.Bed, FurnitureRole.Storage, FurnitureRole.Couch })
                    Assert.LessOrEqual(PanelElement.MeasureWidth(FurniturePage.FitNote(role), PanelElement.SectionMm, panel.Fonts?.SemiBold ?? font), 0.268f, FurniturePage.FitNote(role) + " fits one line");
                Assert.IsTrue(mine.Press(15)); Check(mine, "My room · page 2");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // The human, build dd3521d: "there is no tab to choose between keeping only the desk's real top and showing it
        // fully virtual; make one." Shot 222215: card names cut with dots ("Birch compact d...").
        [Test]
        public void MyRoom_KeepRealTop_IsOneOffByDefaultToggle_UnderThePager_DrivesTheSameSetting_AndThePageFits()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                TMPro.TMP_FontAsset font = panel.Fonts?.Regular;
                var items = new List<CatalogItem>();
                string[] names = { "Oak study desk", "Birch compact desk", "Dark laminate desk", "Walnut writing desk with drawers", "Desk", "White standing desk", "Pine desk", "Steel frame workbench" };
                for (int i = 0; i < names.Length; i++) { CatalogItem item = Item("desk-" + i, "desk", FurnitureRole.Table); item.name = names[i]; items.Add(item); }
                var mine = new FurniturePage(new Catalog(items), _outside, _redesign, null, font, FurniturePage.Job.MyFurniture);
                panel.Configure(new[] { "My room" }, new IPanelPage[] { mine });
                CardInfo C(int k) { var c = new CardInfo(); mine.Describe(k, ref c); return c; }
                int Find(string title) { for (int k = 0; k < mine.Count; k++) if (C(k).Title == title) return k; return -1; }
                _redesign.Selected = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
                foreach (CatalogItem item in items) _redesign.List.Add((item, default));
                mine.Refresh();

                void Fits(string where)
                {
                    panel.Invalidate();
                    Assert.LessOrEqual(panel.Height, panel.Metrics.Size.y + 1e-5f, where + ": within 460 mm");
                    float bottom = panel.Metrics.Size.y * 0.5f - panel.Height;
                    for (int k = 7; k < mine.Count; k++)
                    {
                        PanelElement e = panel.Element(ToolPanel.MaxTabs + k);
                        string title = C(k).Title;
                        if (!e.Visible) { Assert.IsTrue(string.IsNullOrEmpty(title) && C(k).Icon == Vector4.zero, where + ": only empty slots are hidden (item " + k + ": " + title + ")"); continue; }
                        if (string.IsNullOrEmpty(title)) continue;
                        Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom - 1e-4f, where + ": inside the bottom edge: " + title);
                        e.Text.ForceMeshUpdate();
                        Assert.IsFalse(e.Text.isTextTruncated, where + ": never cut: " + title.Replace("\n", " / "));
                        StringAssert.DoesNotContain("…", e.Text.text); StringAssert.DoesNotContain("...", e.Text.text);
                        if (C(k).Kind == CardKind.Card) Assert.AreEqual(PanelElement.FontSize(PanelElement.CaptionMm), e.Text.fontSize, 1e-5f, where + ": the name at 12 mm: " + title);
                    }
                }

                // Kept real: no choice row, and the page is as before (the note shows).
                Assert.Less(Find(FurniturePage.RealTopLabel), 0, "kept real: nothing to choose");
                Fits("kept real");
                Assert.GreaterOrEqual(Find("Designs that fit this desk"), 0);
                float before = panel.Height;

                // A design applied: one toggle, "Keep real top  Off", between the pager and Keep real. Off by default (#326):
                // the design covers the whole desk.
                _redesign.CurrentDesignId = "desk-0";
                _redesign.RealTopOn = false;
                Fits("a design applied, page 1");
                int label = Find(FurniturePage.RealTopLabel), button = label + 1, keep = Find(FurniturePage.KeepRealLabel);
                Assert.AreEqual(16, label); Assert.AreEqual(18, keep);
                Assert.AreEqual(FurniturePage.TopOffLabel, C(button).Title, "off by default");
                Assert.AreEqual(CardKind.Chip, C(button).Kind);
                Assert.IsFalse(C(button).Selected);
                Assert.Less(Find("Real"), 0, "no two-way Real | Design row any more");
                Assert.Less(Find("Design"), 0);
                PanelElement pager = panel.Element(ToolPanel.MaxTabs + 14), row = panel.Element(ToolPanel.MaxTabs + label), keepEl = panel.Element(ToolPanel.MaxTabs + keep);
                PanelElement buttonEl = panel.Element(ToolPanel.MaxTabs + button);
                Assert.Less(row.Centre.y, pager.Centre.y, "under the pager");
                Assert.Greater(row.Centre.y, keepEl.Centre.y, "above Keep real | Adjust fit");
                Assert.AreEqual(row.Centre.y, buttonEl.Centre.y, 1e-5f, "the name and its button on one line");
                Assert.AreEqual(0.134f, buttonEl.Centre.x + buttonEl.Size.x * 0.5f, 1e-4f, "the button on the content edge, as in Settings");
                Assert.Less(Find("Designs that fit this desk"), 0, "two rows of cards and the toggle: the note gives way so the page fits");
                Assert.IsFalse(mine.Press(label), "the name does nothing");
                Assert.IsTrue(mine.Press(button));
                CollectionAssert.AreEqual(new[] { AdjustAction.RealTop }, _redesign.Adjusted, "the same per-piece setting and undo step as before");
                _redesign.RealTopOn = true;
                Assert.AreEqual(FurniturePage.TopOnLabel, C(button).Title);
                Assert.IsTrue(C(button).Selected, "On: the chosen pill");
                Assert.IsTrue(mine.Press(button));
                Assert.AreEqual(2, _redesign.Adjusted.Count, "pressed again: back off, one more step");
                Assert.IsTrue(mine.Press(15)); Fits("page 2");
                Assert.GreaterOrEqual(Find("Designs that fit this desk"), 0, "one row of cards: the note fits again");

                // A bed has the same toggle; a piece with no usable top has none.
                _redesign.Selected = new FurnitureAnchor("bed", FurnitureRole.Bed, new OrientedBox(new Vec3(0f, 0.25f, 0f), new Vec3(0.9f, 0.5f, 2f), 0f));
                mine.Refresh();
                Assert.GreaterOrEqual(Find(FurniturePage.RealTopLabel), 0);
                _redesign.AtLimit.Add(AdjustAction.RealTop);
                Assert.Less(Find(FurniturePage.RealTopLabel), 0, "no usable top: hidden");
                Fits("no usable top");
                Assert.Greater(before, 0f);

                // Names wrap at the card's width, to three lines if they must; the picture shrinks to make room.
                StringAssert.Contains("\n", PanelElement.WrapToWidth("Birch compact desk", FurniturePage.CardNameWidth, PanelElement.CaptionMm, font));
                foreach (string name in names)
                    foreach (string line in PanelElement.WrapToWidth(name, FurniturePage.CardNameWidth, PanelElement.CaptionMm, font).Split('\n'))
                        Assert.LessOrEqual(PanelElement.MeasureWidth(line, PanelElement.CaptionMm, font), FurniturePage.CardNameWidth + 1e-4f, name + ": \"" + line + "\" fits the card");
                Assert.AreEqual("Desk", PanelElement.WrapToWidth("Desk", 0.01f, PanelElement.CaptionMm, font), "one word stays whole");
                PanelElement.CardLayout(0.092f, 3, out float thumbY, out float nameY, out float nameHeight, out float thumbHeight);
                Assert.AreEqual(PanelElement.SmallThumbHeight, thumbHeight, 1e-6f);
                Assert.LessOrEqual(thumbY + thumbHeight * 0.5f, 0.046f, "the picture is inside the card");
                Assert.GreaterOrEqual(nameY - nameHeight * 0.5f, -0.046f, "and so are three lines of name");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

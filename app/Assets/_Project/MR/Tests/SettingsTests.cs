using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR.Tests
{
    // Batch 6: Reset my design (two-step, this room only) and Rescan room in the gear's settings.
    public class SettingsTests
    {
        string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "ownspace-reset-" + System.Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_dir); }
        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        static DesignState Designed()
        {
            var s = new DesignState();
            s.zoneOutline.AddRange(new[] { new Vec2(0f, 0f), new Vec2(2f, 0f), new Vec2(2f, 2f), new Vec2(0f, 2f) });
            s.moodPresetId = "evening";
            s.placements.Add(new FreePlacement { placementId = "p1", catalogItemId = "sofa", position = new Vec3(3f, 0f, 1f) });
            return s;
        }

        [Test]
        public void Reset_ClearsThisRoomsDesign_KeepsItsZone_AndTheOtherRooms()
        {
            string here = Path.Combine(_dir, RoomSaves.FileName("design.json", "room-a"));
            string other = Path.Combine(_dir, RoomSaves.FileName("design.json", "room-b"));
            DesignStateJson.SaveToFile(here, Designed());
            DesignStateJson.SaveToFile(other, Designed());
            string otherBefore = File.ReadAllText(other);

            RoomSaves.Reset(here, Designed().zoneOutline);

            Assert.IsTrue(DesignStateJson.TryLoadFromFile(here, out DesignState reset));
            Assert.AreEqual(4, reset.zoneOutline.Count, "the zone stays: no zone step (batch 9 addendum)");
            Assert.AreEqual(0, reset.placements.Count);
            Assert.IsTrue(string.IsNullOrEmpty(reset.moodPresetId));
            Assert.AreEqual(otherBefore, File.ReadAllText(other), "the other room's save is untouched");
        }

        static int Find(SettingsPage page, string title)
        {
            for (int i = 0; i < page.Count; i++) { var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c); if (c.Title == title) return i; }
            return -1;
        }

        [Test]
        public void ResetMyDesign_NeedsTheConfirm_CancelKeepsEverything()
        {
            int resets = 0;
            var page = new SettingsPage { Reset = () => resets++, Rescan = () => false };
            Assert.IsTrue(page.Press(Find(page, SettingsPage.ResetLabel)));
            Assert.AreEqual(0, resets, "step 1 only asks, on the same row");
            Assert.GreaterOrEqual(Find(page, SettingsPage.ResetConfirm), 0);
            Assert.IsTrue(page.Press(Find(page, SettingsPage.CancelLabel)));
            Assert.AreEqual(0, resets);
            Assert.Less(Find(page, SettingsPage.ResetConfirm), 0, "back to the first state");
            page.Press(Find(page, SettingsPage.ResetLabel));
            Assert.IsTrue(page.Press(Find(page, SettingsPage.ResetConfirm)));
            Assert.AreEqual(1, resets, "step 2 resets");
            Assert.IsFalse(page.Press(Find(page, SettingsPage.RescanLabel)), "no Space Setup here: refused");
        }

        [Test]
        public void Sections_Room_Appearance_Design_InThisOrder_OcclusionUnderDesign_NoHeaderClose()
        {
            int edits = 0, spaces = 0;
            bool on = true;
            var page = new SettingsPage { EditZone = () => edits++, OpenSpaces = () => spaces++,
                CurrentSpace = () => "Dorm", IsOcclusionOn = () => on, SetOcclusion = v => on = v, OcclusionAvailable = () => false };
            var titles = new System.Collections.Generic.List<string>();
            var cards = new System.Collections.Generic.List<OwnSpace.UI.CardInfo>();
            for (int i = 0; i < page.Count; i++) { var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c); titles.Add(c.Title); cards.Add(c); }
            int Index(string part) => titles.FindIndex(t => t.Contains(part));
            Assert.AreEqual(SettingsPage.Title, titles[0]);
            Assert.AreNotEqual(cards[0].Row, cards[1].Row, "the header is the title alone (the human: no ×; the gear or a tab closes)");
            CollectionAssert.DoesNotContain(titles, SettingsPage.CloseGlyph);
            Assert.Less(Index(SettingsPage.SectionSpace), Index(SettingsPage.SectionAppearance));
            Assert.Less(Index(SettingsPage.SectionAppearance), Index(SettingsPage.SectionDesign));
            Assert.Less(Index(SettingsPage.SectionDesign), Index(SettingsPage.OcclusionTitle), "Real occlusion sits under Design");
            foreach (string t in titles) { StringAssert.DoesNotContain("eveloper", t ?? ""); StringAssert.DoesNotContain("EVELOPER", t ?? ""); }
            Assert.AreEqual("Room", SettingsPage.SectionSpace);
            Assert.GreaterOrEqual(Index("My zone"), 0, "wording: My real area is My zone");
            Assert.GreaterOrEqual(Index("The part that stays real"), 0);
            Assert.GreaterOrEqual(Index("Scan this room again"), 0);
            foreach (string t in titles) Assert.AreEqual((t ?? "").ToUpperInvariant() == t && (t ?? "").Length > 3, false, "no ALL CAPS: " + t);
            Assert.Greater(Index("Dorm"), Index(SettingsPage.SectionSpace), "Current space shows its name");
            Assert.IsTrue(titles[Index(SettingsPage.OcclusionTitle)].Contains(SettingsPage.OcclusionNone), "the caption is the depth state");
            Assert.IsTrue(cards[Index(SettingsPage.OcclusionTitle) + 1].Disabled, "the switch is off-limits when not available");
            Assert.IsTrue(page.Press(Find(page, SettingsPage.EditZoneLabel)));
            Assert.IsTrue(page.Press(Find(page, SettingsPage.SpacesLabel)));
            Assert.AreEqual(1, edits); Assert.AreEqual(1, spaces);
            Assert.IsTrue(cards[Find(page, SettingsPage.ResetLabel)].Danger);
        }

        // The human: "nothing lines up". All row titles share one left x, all controls one right edge, one height.
        [Test]
        public void Layout_TitlesShareOneLeftEdge_ControlsShareOneRightEdge()
        {
            var go = new UnityEngine.GameObject("Panel");
            try
            {
                var panel = go.AddComponent<OwnSpace.UI.ToolPanel>();
                panel.Configure(new[] { "A" }, new OwnSpace.UI.IPanelPage[] { null });
                bool on = true;
                var page = new SettingsPage { OpenSpaces = () => { }, CurrentSpace = () => "This room", IsOcclusionOn = () => on, SetOcclusion = v => on = v };
                panel.ShowSheet(page);
                float? left = null, right = null;
                for (int i = 0; i < page.Count; i++)
                {
                    var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c);
                    var e = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + i);
                    float l = e.Centre.x - e.Size.x * 0.5f, r = e.Centre.x + e.Size.x * 0.5f;
                    if (c.Kind == OwnSpace.UI.CardKind.Title) { if (left == null) left = l; Assert.AreEqual(left.Value, l, 1e-4f, c.Title); }
                    bool lastInRow = i + 1 >= page.Count || NextRow(page, i) != c.Row;
                    if (c.Kind != OwnSpace.UI.CardKind.Title && lastInRow) { if (right == null) right = r; Assert.AreEqual(right.Value, r, 1e-4f, c.Title); }
                    if (c.Kind == OwnSpace.UI.CardKind.Chip) Assert.AreEqual(0.036f, e.Size.y, 1e-4f, "every control row is 36 mm");
                }
                Assert.IsNotNull(left); Assert.IsNotNull(right);
                Assert.Greater(right.Value, left.Value);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Device shot 181928: "SPACE</alpha>" (TMP has no closing tag for <alpha>). Every tag must be known and closed,
        // and no text ends in "…" (the human's rule; the Spaces "…" button is a glyph, not text).
        [Test]
        public void SettingsAndSpaces_RichTextTags_AreKnownAndClosed()
        {
            var settings = new SettingsPage { OpenSpaces = () => { }, CurrentSpace = () => "Dorm", IsOcclusionOn = () => false,
                SetOcclusion = v => { }, OcclusionStarting = () => true };
            var texts = new List<string>();
            for (int i = 0; i < settings.Count; i++) { var c = new OwnSpace.UI.CardInfo(); settings.Describe(i, ref c); texts.Add(c.Title); }
            settings.Press(Find(settings, SettingsPage.ResetLabel));
            for (int i = 0; i < settings.Count; i++) { var c = new OwnSpace.UI.CardInfo(); settings.Describe(i, ref c); texts.Add(c.Title); }
            var spaces = new SpacesPage();
            spaces.Refresh(true, new[] { ("a", "Dorm", "Nov 11") }, SpacesPage.ThisRoomId);
            for (int i = 0; i < spaces.Count; i++) { var c = new OwnSpace.UI.CardInfo(); spaces.Describe(i, ref c); texts.Add(c.Title); }
            Assert.IsTrue(texts.Exists(t => t.Contains(SettingsPage.OcclusionStartingLabel)), "the starting caption was checked");
            foreach (string t in texts)
            {
                StringAssert.DoesNotContain("</alpha>", t);
                Assert.IsTrue(TagsClosed(t), t);
                if (t != SpacesPage.MoreLabel) { StringAssert.DoesNotContain("…", t); StringAssert.DoesNotContain("...", t); }
            }
        }

        static readonly HashSet<string> KnownTags = new HashSet<string> { "size", "b", "i", "u", "color" };

        static bool TagsClosed(string text)
        {
            var open = new Stack<string>();
            for (int i = text.IndexOf('<'); i >= 0; i = text.IndexOf('<', i + 1))
            {
                int end = text.IndexOf('>', i);
                if (end < 0) return false;
                string tag = text.Substring(i + 1, end - i - 1);
                bool closing = tag.StartsWith("/");
                string name = (closing ? tag.Substring(1) : tag).Split('=')[0];
                if (!KnownTags.Contains(name)) return false;
                if (!closing) open.Push(name);
                else if (open.Count == 0 || open.Pop() != name) return false;
            }
            return open.Count == 0;
        }

        // Device shot 181928: the resize grip sat on the Real occlusion switch in the last row.
        [Test]
        public void LastRow_WithAControlOnTheRight_ClearsTheResizeGrip()
        {
            float w = 0.30f, pad = 0.016f;
            Assert.AreEqual(pad, OwnSpace.UI.ToolPanel.BottomPaddingFor(-0.05f, w, pad), 1e-5f, "nothing under the grip: the usual padding");
            Assert.GreaterOrEqual(OwnSpace.UI.ToolPanel.BottomPaddingFor(0.134f, w, pad), 0.029f, "a control under the grip: room for it");
            Assert.IsFalse(PanelHost.ReopensOnConfirm(true), "a room reload keeps the open panel's tab and sheet");
            Assert.IsTrue(PanelHost.ReopensOnConfirm(false));
        }

        static int NextRow(SettingsPage page, int i) { var c = new OwnSpace.UI.CardInfo(); page.Describe(i + 1, ref c); return c.Row; }

        // Device (build dfb22f4): "anything entered from Settings must come back to Settings"; back from Edit, Add opened.
        [Test]
        public void EveryWayOutOfASettingsFlow_EndsOnSettings_AndNoSelectionSwitchesTheTab()
        {
            foreach (PanelHost.SettingsExit exit in System.Enum.GetValues(typeof(PanelHost.SettingsExit)))
                Assert.IsTrue(PanelHost.EndsOnSettings(exit), exit.ToString());
            Assert.IsFalse(PanelHost.SelectionSwitchesTab(sheetOpen: true, interacting: false), "Settings open: a selection never opens Add or My room");
            Assert.IsFalse(PanelHost.SelectionSwitchesTab(false, true));
            Assert.IsTrue(PanelHost.SelectionSwitchesTab(false, false));

            // The Spaces chooser: its x and the space you are already in both go back (PanelHost wires Close to Settings).
            int closes = 0;
            var spaces = new SpacesPage { Close = () => closes++, Go = id => false };
            spaces.Refresh(true, null, SpacesPage.ThisRoomId);
            var c = new OwnSpace.UI.CardInfo();
            int here = -1;
            for (int i = 0; i < spaces.Count; i++) { c = new OwnSpace.UI.CardInfo(); spaces.Describe(i, ref c); if (c.Title == SpacesPage.ThisRoom) here = i; }
            Assert.IsTrue(spaces.Press(here), "picking the space you are in");
            Assert.IsTrue(spaces.Press(1), "the x");
            Assert.AreEqual(2, closes);

            // Reset: Cancel stays on Settings (the confirm row only).
            var settings = new SettingsPage { Reset = () => { } };
            settings.Press(Find(settings, SettingsPage.ResetLabel));
            Assert.IsTrue(settings.Press(Find(settings, SettingsPage.CancelLabel)));
            Assert.GreaterOrEqual(Find(settings, SettingsPage.ResetLabel), 0, "still the Settings sheet, back to the Reset row");
        }

        // Mockups round 2 (settings.html): 40 mm rows, one 80 mm control column, 408 mm release, 448 mm development.
        // Device shots 194919 and 201716: buttons sat a row low and the Off switch hung outside the panel.
        [Test]
        public void OnTheRealPanel_Rows40_Controls80OnOneEdge_FitsThePanel()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<OwnSpace.UI.ToolPanel>();
                panel.Configure(new[] { "A" }, new OwnSpace.UI.IPanelPage[] { null });
                bool on = true;
                var page = new SettingsPage(panel.Fonts?.Regular) { OpenSpaces = () => { }, CurrentSpace = () => "This room" };
                panel.ShowSheet(page);

                void Check(string where, float height)
                {
                    panel.Invalidate();
                    if (height > 0f) Assert.AreEqual(height, panel.Height, 0.0015f, where + ": the sheet's height");
                    Assert.LessOrEqual(panel.Height, panel.Metrics.Size.y + 1e-5f, where + ": within the 460 mm panel");
                    float bottom = panel.Metrics.Size.y * 0.5f - panel.Height, edge = SettingsPage.FullWidth * 0.5f;
                    var lastCard = new OwnSpace.UI.CardInfo(); page.Describe(page.Count - 1, ref lastCard);
                    int lastRow = lastCard.Row;
                    for (int i = 0; i < page.Count; i++)
                    {
                        var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c);
                        OwnSpace.UI.PanelElement e = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + i);
                        Assert.IsTrue(e.Visible, where + ": " + c.Title);
                        Assert.GreaterOrEqual(e.Centre.y - e.Size.y * 0.5f, bottom - 1e-4f, where + ": inside the bottom edge: " + c.Title);
                        e.Text.ForceMeshUpdate();
                        Assert.IsFalse(e.Text.isTextTruncated, where + ": " + c.Title);
                        if (c.Kind == OwnSpace.UI.CardKind.Chip)
                        {
                            Assert.AreEqual(SettingsPage.ControlWidth, e.Size.x, 1e-5f, where + ": every button is 80 mm: " + c.Title);
                            Assert.AreEqual(SettingsPage.ControlHeight, e.Size.y, 1e-5f, where + ": and 36 mm high");
                            if (c.Title != SettingsPage.ResetConfirm) Assert.AreEqual(edge, e.Centre.x + e.Size.x * 0.5f, 1e-4f, where + ": its right edge is the content edge: " + c.Title);
                            // The control sits on its own row's centre line, never a row low.
                            OwnSpace.UI.PanelElement text = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + i - (c.Title == SettingsPage.CancelLabel ? 2 : 1));
                            Assert.AreEqual(text.Centre.y, e.Centre.y, 1e-5f, where + ": on its row's centre line: " + c.Title);
                        }
                        if (c.Body && c.Lines == 2)
                        {
                            bool last = c.Row == lastRow;
                            Assert.AreEqual(last ? SettingsPage.ControlHeight : SettingsPage.RowHeight, e.Size.y, 1e-5f, where + ": a 40 mm row (the last one 36)");
                            Assert.AreEqual(2, e.Text.textInfo.lineCount, where + ": a title and a one-line caption: " + c.Title);
                            Assert.AreEqual(OwnSpace.UI.PanelElement.FontSize(OwnSpace.UI.PanelElement.BodyMm), e.Text.fontSize, 1e-5f, where + ": 14 mm, not shrunk: " + c.Title);
                        }
                    }
                    // Light | Dark fill the same 80 mm column (not shown while Reset asks).
                    if (Find(page, "Light") < 0) return;
                    OwnSpace.UI.PanelElement light = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + Find(page, "Light")), darkEl = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + Find(page, "Dark"));
                    Assert.AreEqual(edge - SettingsPage.ControlWidth, light.Centre.x - light.Size.x * 0.5f, 1e-4f, where + ": Light starts the control column");
                    Assert.AreEqual(edge, darkEl.Centre.x + darkEl.Size.x * 0.5f, 1e-4f, where + ": Dark ends it");
                }

                Check("release", 0.419f);
                // Bigger room under Room: one more 40 mm row, its caption on one line, still within the panel.
                bool big = false;
                page.IsBiggerRoom = () => big; page.SetBiggerRoom = v => big = v;
                Check("with Bigger room", 0.459f);
                page.IsBiggerRoom = null; page.SetBiggerRoom = null;
                // A long saved-space name: the caption keeps its size and one line, and shows the name's beginning.
                string longName = "Grandmother's summer house, upstairs";
                page.CurrentSpace = () => longName;
                Check("a long space name", 0.419f);
                var row = new OwnSpace.UI.CardInfo(); page.Describe(2, ref row);
                StringAssert.StartsWith(SettingsPage.CurrentSpaceTitle + "\n<size=86%>Grandmother", row.Title);
                StringAssert.DoesNotContain("…", row.Title);
                page.CurrentSpace = () => "This room";
                page.IsOcclusionOn = () => on; page.SetOcclusion = v => on = v;
                Check("development", 0.459f);
                page.Press(Find(page, SettingsPage.ResetLabel));
                Check("Reset asks", -1f);
                for (int i = 0; i < page.Count; i++) { var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c); StringAssert.DoesNotContain(SettingsPage.OcclusionTitle, c.Title ?? "", "while it asks, the development row waits"); }
                page.Press(Find(page, SettingsPage.CancelLabel));

                // Device 8fdbcda, shot 144121: with Bigger room, Reset's question pushed Confirm and Cancel below the panel.
                page.IsOcclusionOn = null; page.SetOcclusion = null;
                page.IsBiggerRoom = () => big; page.SetBiggerRoom = v => big = v;
                Check("with Bigger room", 0.459f);
                page.Press(Find(page, SettingsPage.ResetLabel));
                Check("Reset asks, with Bigger room", -1f); // every element visible and inside the bottom edge
                int confirm = Find(page, SettingsPage.ResetConfirm), cancel = Find(page, SettingsPage.CancelLabel);
                Assert.GreaterOrEqual(confirm, 0, "Confirm is there");
                Assert.GreaterOrEqual(cancel, 0, "Cancel is there");
                float floor = panel.Metrics.Size.y * 0.5f - panel.Metrics.Size.y;
                foreach (int k in new[] { confirm, cancel })
                {
                    OwnSpace.UI.PanelElement b = panel.Element(OwnSpace.UI.ToolPanel.MaxTabs + k);
                    Assert.IsTrue(b.Visible);
                    Assert.GreaterOrEqual(b.Centre.y - b.Size.y * 0.5f, floor - 1e-4f, "fully inside the panel");
                }
                Assert.IsTrue(page.Press(cancel));
                Assert.GreaterOrEqual(Find(page, SettingsPage.BiggerRoomTitle + "\n<size=86%>" + SettingsPage.BiggerRoomCaption + "</size>"), 0, "Cancel brings the sections back");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}

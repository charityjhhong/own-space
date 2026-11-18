using System;
using NUnit.Framework;
using UnityEngine;
using OwnSpace.UI;

namespace OwnSpace.MR.Tests
{
    public class AiDesignerPageTests
    {
        sealed class FakeAi : IAiDesigner
        {
            public bool Busy { get; set; }
            public bool HasPlan { get; set; }
            public bool CanUndoPlan { get; set; }
            public bool LastFailed { get; set; }
            public bool LastFromFallback { get; set; }
            public string LastRequest { get; set; }
            public string LastRationale { get; set; }
            public string StatusMessage { get; set; }
            public int LastPlaced { get; set; }
            public int LastSkipped { get; set; }
            public int Asked = -1, Cancels, Undos;
            public bool AskPreset(int index, string label) { Asked = index; LastRequest = label; return true; }
            public void CancelRequest() => Cancels++;
            public bool UndoPlan() { Undos++; return true; }
            public event Action Changed;
            public void Raise() => Changed?.Invoke();
        }

        sealed class FakeField : IRequestField
        {
            public int Opens, Sends;
            public string Text { get; set; } = "";
            public event Action Changed;
            public bool Open() { Opens++; return true; }
            public bool Send() { Sends++; return Text.Length > 0; }
            public void Type(string text) { Text = text; Changed?.Invoke(); }
        }

        FakeAi _ai;
        FakeField _field;
        AiDesignerPage _page;

        [SetUp]
        public void SetUp()
        {
            _ai = new FakeAi();
            _field = new FakeField();
            _page = new AiDesignerPage(_ai, _field, 0.264f);
        }

        CardInfo Card(int i) { var c = new CardInfo(); _page.Describe(i, ref c); return c; }

        int Find(string title)
        {
            for (int i = 0; i < _page.Count; i++) if (Card(i).Title == title) return i;
            return -1;
        }

        [Test]
        public void BeforeARun_FieldPresetsAndOneQuietLine()
        {
            Assert.AreEqual(_page.Placeholder, Card(0).Title);
            Assert.AreEqual(0.264f, Card(0).Width + 0.008f + Card(1).Width, 1e-5f, "the field and Send fill the row");
            Assert.AreEqual(AiDesignerPage.CreateLabel, Card(1).Title);
            Assert.AreEqual(0, Card(1).Row);
            Assert.IsTrue(Card(1).Disabled, "nothing typed: Send is off");
            Assert.AreEqual(Vector4.zero, Card(0).Icon, "batch 10: typing only, no mic glyph");
            Assert.Greater(Card(3).Width, 0.03f, "presets hug their text");
            Assert.GreaterOrEqual(Find(AiDesignerPage.Before), 0, "no plan card before the AI has run");
            Assert.AreEqual(AiDesignerPage.Before, Card(_page.Count - 1).Title, "no disabled Suggest chip hanging off the bottom (device #20)");
            Assert.IsTrue(_page.Press(0));
            Assert.AreEqual(1, _field.Opens, "the field opens the keyboard");
            Assert.IsTrue(_page.Press(4));
            Assert.AreEqual(1, _ai.Asked);
        }

        // Device batch 10: the field shows what was typed, and Send sends it.
        [Test]
        public void TypedText_ShowsInTheField_AndSendSendsIt()
        {
            _field.Type("a nook");
            Assert.AreEqual("a nook", Card(0).Title);
            Assert.IsFalse(Card(1).Disabled);
            Assert.IsTrue(Card(1).Selected, "Send is the main action");
            Assert.IsTrue(_page.Press(1));
            Assert.AreEqual(1, _field.Sends);
            _field.Type("");
            Assert.AreEqual(_page.Placeholder, Card(0).Title, "sent: the placeholder again");
        }

        [Test]
        public void Busy_ThinkingWithCancel_PresetsDisabled()
        {
            _ai.Busy = true;
            _ai.Raise();
            Assert.GreaterOrEqual(Find("Thinking"), 0);
            Assert.IsTrue(Card(3).Disabled);
            Assert.IsTrue(_page.Press(Find("Cancel")));
            Assert.AreEqual(1, _ai.Cancels);
        }

        [Test]
        public void Plan_QuotedRequestCountsAndUndo_FallbackNoted()
        {
            _ai.HasPlan = true; _ai.CanUndoPlan = true; _ai.LastRequest = "a quiet reading corner"; _ai.LastRationale = "A rug and a lamp.";
            _ai.LastPlaced = 7; _ai.LastSkipped = 1; _ai.LastFromFallback = true;
            _ai.Raise();
            Assert.GreaterOrEqual(Find(AiDesignerPage.OfflinePlan), 0);
            Assert.GreaterOrEqual(Find("“a quiet reading corner”"), 0);
            Assert.GreaterOrEqual(Find("7 placed, 1 skipped"), 0);
            Assert.AreEqual(2, Card(Find("A rug and a lamp.")).Lines, "the explanation wraps to two lines");
            Assert.IsTrue(_page.Press(Find(AiDesignerPage.UndoLabel)));
            Assert.AreEqual(1, _ai.Undos);
            _ai.CanUndoPlan = false;
            _ai.Raise();
            Assert.IsTrue(Card(Find(AiDesignerPage.UndoLabel)).Disabled, "another change came after the plan");
        }

        [Test]
        public void Explanation_FallsBackToTheStatusMessage_AndDisposeStopsListening()
        {
            _ai.HasPlan = true; _ai.LastPlaced = 3; _ai.StatusMessage = "Added 3 pieces";
            _ai.Raise();
            Assert.GreaterOrEqual(Find("Added 3 pieces"), 0);
            _page.Dispose();
            _ai.HasPlan = false; _ai.LastFailed = true;
            _ai.Raise();
            Assert.Less(Find("No change made. Try again."), 0, "no longer listening");
        }

        [Test]
        public void Failed_SaysNoChangeMade()
        {
            _ai.LastFailed = true;
            _ai.Raise();
            Assert.GreaterOrEqual(Find("No change made. Try again."), 0);
        }

        // Device, build dfb22f4: the field's text was about half the size of Create and the chips.
        [Test]
        public void TheField_KeepsFullSize_ALongRequestShowsItsEnd()
        {
            Assert.LessOrEqual(PanelElement.MeasureWidth(_page.Placeholder, AiDesignerPage.ChipTextMm), _page.FieldTextWidth, "the placeholder fits at the chips' size");
            const string Long = "a quiet reading corner with a deep armchair, a floor lamp and many plants by the window";
            _field.Type(Long);
            string shown = Card(0).Title;
            Assert.Less(shown.Length, Long.Length, "too long for the field");
            StringAssert.EndsWith(shown, Long, "it shows the end of the text");
            Assert.LessOrEqual(PanelElement.MeasureWidth(shown, AiDesignerPage.ChipTextMm), _page.FieldTextWidth, "what is shown fits at full size");
            Assert.Greater(PanelElement.MeasureWidth(Long.Substring(Long.Length - shown.Length - 1), AiDesignerPage.ChipTextMm), _page.FieldTextWidth, "and one more character would not");
            StringAssert.DoesNotContain("…", shown);
        }

        [Test]
        public void TextTail_KeepsShortText_AndCutsFromTheLeft()
        {
            Assert.AreEqual("abc", TextTail.Fit("abc", 1f, 13f, null));
            Assert.AreEqual("", TextTail.Fit("", 1f, 13f, null));
            Assert.IsNull(TextTail.Fit(null, 1f, 13f, null));
            string tail = TextTail.Fit("the quick brown fox jumps over the lazy dog", 0.1f, 13f, null);
            StringAssert.EndsWith(tail, "the quick brown fox jumps over the lazy dog");
            Assert.LessOrEqual(PanelElement.MeasureWidth(tail, 13f), 0.1f);
            Assert.Greater(tail.Length, 3);
        }

        // On the real panel and keyboard: the field's text and the keyboard's line are drawn at full size, never shrunk.
        [Test]
        public void OnTheRealPanel_TheFieldAndTheKeyboardLine_AreNeverShrunk()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            GameObject go = UnityEngine.Object.Instantiate(prefab), host = new GameObject("FreeText");
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                var request = host.AddComponent<FreeTextRequest>();
                request.UsePanel(panel);
                var page = new AiDesignerPage(null, request, panel.Metrics.Size.x - 2f * panel.Metrics.Padding, panel.Fonts?.Regular);
                panel.Configure(new[] { "AI" }, new IPanelPage[] { page });
                Assert.IsTrue(request.OpenForName("", _ => true), "the keyboard opens (naming needs no AI tab)");

                void Check(string where)
                {
                    panel.Invalidate();
                    TMPro.TextMeshPro field = panel.Element(ToolPanel.MaxTabs).Text, create = panel.Element(ToolPanel.MaxTabs + 1).Text, chip = panel.Element(ToolPanel.MaxTabs + 3).Text;
                    TMPro.TextMeshPro line = request.Keyboard.Root.Find("Text").GetComponentInChildren<TMPro.TextMeshPro>();
                    foreach (TMPro.TextMeshPro t in new[] { field, create, chip, line }) t.ForceMeshUpdate();
                    Assert.AreEqual(chip.fontSize, field.fontSize, 1e-5f, where + ": the field's text is as big as a style chip's");
                    Assert.AreEqual(PanelElement.FontSize(AiDesignerPage.ChipTextMm), field.fontSize, 1e-5f, where + ": at full size");
                    Assert.GreaterOrEqual(field.fontSize, create.fontSize - 1e-5f, where + ": not smaller than Create");
                    Assert.IsFalse(field.isTextTruncated, where + ": " + field.text);
                    Assert.AreEqual(PanelElement.FontSize(PanelKeyboard.TextMm), line.fontSize, 1e-5f, where + ": the keyboard's line at full size");
                    Assert.IsFalse(line.isTextTruncated, where + ": " + line.text);
                }

                Assert.AreEqual(AiDesignerPage.ShortField, page.Placeholder, "in Inter at the chips' size the long sentence does not fit beside Create: the short one is shown");
                Check("placeholder");
                // The human, build 2dc475d: the keyboard shows no sentence of its own. Its line is a caret until the user
                // types; the placeholder lives only in the panel's field and goes once a character is typed.
                TMPro.TextMeshPro kbLine = request.Keyboard.Root.Find("Text").GetComponentInChildren<TMPro.TextMeshPro>();
                TMPro.TextMeshPro panelField = panel.Element(ToolPanel.MaxTabs).Text;
                Assert.AreEqual(FreeTextRequest.Caret, kbLine.text, "nothing typed: the keyboard's line is empty but for the caret");
                Assert.AreEqual(page.Placeholder, panelField.text, "the placeholder is in the panel's field");
                foreach (TMPro.TextMeshPro t in request.Keyboard.Root.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                {
                    StringAssert.DoesNotContain("Describe", t.text, "no placeholder anywhere on the keyboard");
                    StringAssert.DoesNotContain(FreeTextRequest.NamePlaceholder, t.text);
                }
                request.PressKey(PanelKeyboard.Find("a"));
                panel.Invalidate();
                Assert.AreEqual("a" + FreeTextRequest.Caret, kbLine.text);
                Assert.AreEqual("a", panelField.text, "one character typed: the placeholder is gone from the field too");
                request.PressKey(PanelKeyboard.Find(PanelKeyboard.DeleteLabel));
                panel.Invalidate();
                Assert.AreEqual(FreeTextRequest.Caret, kbLine.text, "deleted again: the caret alone");
                Assert.AreEqual(page.Placeholder, panelField.text);
                foreach (char ch in "a quiet reading corner") request.PressKey(PanelKeyboard.Find(ch == ' ' ? PanelKeyboard.SpaceLabel : ch.ToString()));
                Check("short text");
                for (int i = 0; i < 12; i++) foreach (char ch in "wide warm ") request.PressKey(PanelKeyboard.Find(ch == ' ' ? PanelKeyboard.SpaceLabel : ch.ToString()));
                Check("long text");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(host); }
        }
    }
}

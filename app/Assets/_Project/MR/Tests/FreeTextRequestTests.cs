using NUnit.Framework;

namespace OwnSpace.MR.Tests
{
    public class FreeTextRequestTests
    {
        [Test]
        public void CleanRequest_TrimsAndCollapsesWhitespaceAndControlCharacters()
        {
            Assert.AreEqual("a cozy reading corner", AskMenu.CleanRequest("  a cozy\n\treading\u0007  corner  "));
            Assert.AreEqual("", AskMenu.CleanRequest(" \n\t "));
            Assert.AreEqual("", AskMenu.CleanRequest(null));
        }

        [Test]
        public void CleanRequest_ClipsTo200Characters()
        {
            string clipped = AskMenu.CleanRequest(new string('a', 150) + " " + new string('b', 150));
            Assert.AreEqual(AskMenu.MaxRequestLength, clipped.Length);
            StringAssert.StartsWith(new string('a', 150) + " b", clipped);
        }

        [Test]
        public void CleanRequest_KeepsAccentsAndPunctuation()
        {
            Assert.AreEqual("a café-style nook, please!", AskMenu.CleanRequest("a café-style   nook, please!"));
        }

        UnityEngine.GameObject _go;
        FreeTextRequest _request;
        string _sent;
        int _sends;

        [SetUp]
        public void SetUp()
        {
            _go = new UnityEngine.GameObject("FreeText");
            _request = _go.AddComponent<FreeTextRequest>();
            _sent = null;
            _sends = 0;
            _request.SendText = t => { _sent = t; _sends++; return true; };
        }

        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_go);

        void Type(string text) { foreach (char c in text) _request.PressKey(PanelKeyboard.Find(c == ' ' ? PanelKeyboard.SpaceLabel : c.ToString())); }

        // Device batch 10: the system overlay timed out; our own keyboard types, deletes and sends.
        [Test]
        public void Keys_TypeDeleteAndSend_ThenTheFieldIsClear()
        {
            _request.OpenForTest();
            Type("a  cozy nook");
            Assert.AreEqual("a cozy nook", _request.Text, "no double spaces");
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.DeleteLabel));
            Assert.AreEqual("a cozy noo", _request.Text);
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.SendLabel));
            Assert.AreEqual("a cozy noo", _sent);
            Assert.AreEqual("", _request.Text, "sent: cleared");
            Assert.IsFalse(_request.IsOpen, "sent: the keyboard goes");
        }

        [Test]
        public void Send_WithNothingTyped_SaysSo()
        {
            string why = null;
            _request.Failed += w => why = w;
            _request.OpenForTest();
            Type(" ");
            Assert.IsFalse(_request.Send());
            Assert.AreEqual(FreeTextRequest.EmptyText, why);
            Assert.AreEqual(0, _sends);
            Assert.IsTrue(_request.IsOpen);
        }

        [Test]
        public void Cancel_PauseAndHandLoss_CloseButKeepTheText()
        {
            _request.OpenForTest();
            Type("half");
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.CancelLabel));
            Assert.IsFalse(_request.IsOpen);
            Assert.AreEqual("half", _request.Text, "reopening continues");

            _request.OpenForTest();
            _request.Pause(true);
            Assert.IsFalse(_request.IsOpen);

            _request.OpenForTest();
            // Device, build 2dc475d: it closed twice a few seconds after opening, on a moment's tracking loss.
            Assert.IsFalse(_request.Tick(1.5f, false), "a short tracking loss: the keyboard stays");
            Assert.IsFalse(_request.Tick(0.1f, true), "the hands are back: the count starts over");
            Assert.IsFalse(_request.Tick(1.5f, false));
            Assert.IsFalse(_request.Tick(1.5f, false), "3 s in all: still open");
            Assert.IsTrue(_request.IsOpen);
            Assert.GreaterOrEqual(FreeTextRequest.LostHandsGrace, 3f);
            Assert.IsTrue(_request.Tick(FreeTextRequest.LostHandsGrace, false), "a long loss closes it");
            Assert.IsFalse(_request.IsOpen);
            Assert.AreEqual("half", _request.Text);
            Assert.IsNull(_sent);
        }

        [Test]
        public void Typing_StopsAt200Characters()
        {
            _request.OpenForTest();
            for (int i = 0; i < AskMenu.MaxRequestLength + 20; i++) _request.PressKey(PanelKeyboard.Find("a"));
            Assert.AreEqual(AskMenu.MaxRequestLength, _request.Text.Length);
        }

        // Fix C: "open" is never reported without a keyboard on screen (no panel = refused, not "Visible").
        [Test]
        public void Open_WithoutAPanel_FailsAndStaysClosed()
        {
            string why = null;
            _request.Failed += w => why = w;
            Assert.IsFalse(_request.Open());
            Assert.IsFalse(_request.IsOpen);
            Assert.AreEqual(FreeTextRequest.NoKeyboard, why);
        }

        // On the real panel: the keyboard docks under it, in its plane, and no label is cut off.
        [Test]
        public void Open_OnTheRealPanel_DocksUnderItAtItsWidth_PanelUnmoved_EveryLabelWhole()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<OwnSpace.UI.ToolPanel>();
                go.transform.position = new UnityEngine.Vector3(0f, 1.1f, 0.6f);
                _request.UsePanel(panel);
                UnityEngine.Vector3 panelAt = go.transform.position;
                Assert.IsTrue(_request.Open());
                Assert.IsTrue(_request.IsOpen);
                Assert.AreEqual(panelAt, go.transform.position, "device feedback on 04ad24c: the panel never moves");
                Assert.IsNull(go.transform.Find("Keyboard"), "its own piece, not part of the panel");
                UnityEngine.Transform kb = _request.Keyboard.Root;
                Assert.IsTrue(kb.gameObject.activeSelf);
                UnityEngine.Vector3 bottom = go.transform.TransformPoint(new UnityEngine.Vector3(0f, panel.Metrics.Size.y * 0.5f - panel.Height, 0f));
                UnityEngine.Vector3 kbTop = kb.TransformPoint(new UnityEngine.Vector3(0f, PanelKeyboard.Height * 0.5f, 0f));
                Assert.AreEqual(PanelKeyboard.DockGap, UnityEngine.Vector3.Dot(bottom - kbTop, go.transform.up), 1e-4f, "its top edge is just under the panel's bottom edge");
                Assert.AreEqual(go.transform.position.z, kbTop.z, 1e-4f, "in the panel's plane");
                Assert.AreEqual(go.transform.rotation, kb.rotation, "at the panel's tilt");
                Assert.AreEqual(panel.Metrics.Size.x * PanelKeyboard.WidthRatio, PanelKeyboard.Width * kb.lossyScale.x, 1e-4f, "about as wide as the panel");
                foreach (TMPro.TextMeshPro t in kb.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                {
                    t.ForceMeshUpdate();
                    Assert.IsFalse(t.isTextTruncated, t.text);
                    // Labels scale with the keys (the root's scale) and are not shrunk on their own.
                    if (t.transform.parent.name.StartsWith("Key ")) Assert.AreEqual(t.fontSizeMax, t.fontSize, 1e-5f, "the label of " + t.transform.parent.name + " is at its full size");
                }
                Type("a reading nook");
                _request.Close();
                Assert.IsFalse(kb.gameObject.activeSelf);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void Shift_CapitalisesTheNextLetterOnly()
        {
            _request.OpenForTest();
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.ShiftLabel));
            Assert.IsTrue(_request.Shift);
            Type("a cozy");
            Assert.AreEqual("A cozy", _request.Text, "one shot");
            Assert.IsFalse(_request.Shift);
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.ShiftLabel));
            _request.PressKey(PanelKeyboard.Find(PanelKeyboard.ShiftLabel));
            Assert.IsFalse(_request.Shift, "pressed twice: off again");
        }

        [Test]
        public void Send_WhileTheAiIsBusy_SaysSoAndKeepsTheText()
        {
            string why = null;
            _request.Failed += w => why = w;
            _request.OpenForTest();
            Type("a nook");
            _request.IsBusy = () => true;
            Assert.IsFalse(_request.Send());
            Assert.AreEqual(FreeTextRequest.BusyText, why);
            Assert.AreEqual(0, _sends);
            Assert.AreEqual("a nook", _request.Text);
            Assert.IsFalse(_request.Open(), "nor does it open while busy");
        }

        // Lead review of #225: the clamp, a scaled panel, and leaving the AI tab.
        [Test]
        public void OnTheRealPanel_TiltedKeyboardHitsWork_AndLeavingTheTabCloses()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            UnityEngine.GameObject go = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var panel = go.GetComponent<OwnSpace.UI.ToolPanel>();
                panel.SetScale(1.5f);
                go.transform.SetPositionAndRotation(new UnityEngine.Vector3(0.2f, 1f, 0.6f), UnityEngine.Quaternion.Euler(10f, 25f, 0f));
                _request.UsePanel(panel);
                Assert.IsTrue(_request.Open());
                Assert.AreEqual(0f, panel.ExtraBelow, "the panel is not clamped for the keyboard any more");

                UnityEngine.Transform kb = _request.Keyboard.Root;
                foreach (string label in new[] { "q", "m", PanelKeyboard.SendLabel, PanelKeyboard.ShiftLabel })
                {
                    int key = PanelKeyboard.Find(label);
                    PanelKeyboard.KeyRect(key, out UnityEngine.Vector2 c, out _);
                    UnityEngine.Vector3 world = kb.TransformPoint(new UnityEngine.Vector3(c.x, c.y, 0f));
                    var ray = new UnityEngine.Ray(world - kb.forward * 0.6f, kb.forward);
                    Assert.AreEqual(key, _request.Keyboard.Hit(ray, UnityEngine.Vector3.one * 99f, false), label + " by ray on the tilted keyboard");
                    Assert.AreEqual(key, _request.Keyboard.Hit(ray, world - kb.forward * 0.01f, true), label + " by a near pinch");
                }

                Assert.AreNotEqual(PanelHost.AiTab, panel.CurrentTab);
                Assert.IsTrue(_request.CloseIfTabGone(), "not on the AI tab: closes");
                Assert.IsFalse(_request.IsOpen);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        sealed class Desk : OwnSpace.UI.IPanelSurfaces
        {
            public bool TopUnder(UnityEngine.Vector3 p, out float top) { top = 0.75f; return p.z > 0.2f && p.z < 0.9f && System.Math.Abs(p.x) < 0.8f; }
        }

        static readonly UnityEngine.Vector3 Head = new UnityEngine.Vector3(0f, 1.15f, 0f);

        // A panel whose bottom edge is at `bottom`, facing the eyes (as the tool panel always does).
        static UnityEngine.Quaternion Facing(UnityEngine.Vector3 bottom) => UnityEngine.Quaternion.LookRotation(bottom + UnityEngine.Vector3.up * 0.12f - Head, UnityEngine.Vector3.up);

        static UnityEngine.Vector3 Corner(UnityEngine.Vector3 p, UnityEngine.Quaternion r, float scale, float x, float y) =>
            p + r * new UnityEngine.Vector3(x * PanelKeyboard.Width * 0.5f * scale, y * PanelKeyboard.Height * 0.5f * scale, 0f);

        // Device feedback on 0ddb26d: three times the panel's width, off the view. Now: the panel's width, under it, in its plane.
        [Test]
        public void Dock_UnderThePanel_InItsPlane_AboutItsWidth_AllInView()
        {
            var bottom = new UnityEngine.Vector3(0.1f, 0.9f, 0.6f);
            UnityEngine.Quaternion panel = Facing(bottom);
            UnityEngine.Vector3 forward = (bottom + UnityEngine.Vector3.up * 0.1f - Head).normalized; // looking at the request field
            PanelKeyboard.Dock(Head, forward, bottom, panel, 0.30f, null, out UnityEngine.Vector3 p, out UnityEngine.Quaternion r, out float scale);
            Assert.AreEqual(panel, r, "the panel's tilt");
            Assert.AreEqual(0.30f * PanelKeyboard.WidthRatio, PanelKeyboard.Width * scale, 1e-4f, "1.15 times the panel's width");
            Assert.LessOrEqual(PanelKeyboard.Width * scale, 0.30f * 1.2f);
            UnityEngine.Vector3 top = Corner(p, r, scale, 0f, 1f);
            Assert.AreEqual(0f, UnityEngine.Vector3.Dot(top - bottom, r * UnityEngine.Vector3.forward), 1e-4f, "in the panel's plane: the panel's distance");
            Assert.AreEqual(0f, UnityEngine.Vector3.Dot(top - bottom, r * UnityEngine.Vector3.right), 1e-4f, "centred under it");
            Assert.AreEqual(-PanelKeyboard.DockGap, UnityEngine.Vector3.Dot(top - bottom, r * UnityEngine.Vector3.up), 1e-4f, "under the bottom edge: never overlaps the panel");
            Assert.IsTrue(PanelKeyboard.InView(Head, forward, p, r, scale), "every corner (so every key, the text line, Cancel, Create) is in view");
            Assert.GreaterOrEqual(PanelKeyboard.KeyHeight * scale, 0.028f, "a 29 mm key");
        }

        [Test]
        public void Dock_FollowsThePanelsResize_ButNeverGrowsPastTheView()
        {
            var bottom = new UnityEngine.Vector3(0f, 0.9f, 0.6f);
            UnityEngine.Vector3 forward = (bottom - Head).normalized;
            foreach (float panelScale in new[] { 0.9f, 1f, 1.6f })
            {
                PanelKeyboard.Dock(Head, forward, bottom, Facing(bottom), 0.30f * panelScale, null, out _, out _, out float scale);
                Assert.AreEqual(0.30f * panelScale * PanelKeyboard.WidthRatio, PanelKeyboard.Width * scale, 1e-4f, panelScale + "x");
            }
            // A 1.6x panel pulled close: the keyboard stops at 50 degrees across.
            var near = new UnityEngine.Vector3(0f, 0.95f, 0.3f);
            PanelKeyboard.Dock(Head, (near - Head).normalized, near, Facing(near), 0.48f, null, out UnityEngine.Vector3 p, out _, out float capped);
            Assert.Less(PanelKeyboard.Width * capped, 0.48f * PanelKeyboard.WidthRatio);
            Assert.LessOrEqual(PanelKeyboard.AngularSize(PanelKeyboard.Width * capped, UnityEngine.Vector3.Distance(Head, near)), PanelKeyboard.MaxViewDegrees + 0.5f);
            Assert.AreEqual(PanelKeyboard.MaxViewDegrees, PanelKeyboard.AngularSize(2f * UnityEngine.Mathf.Tan(25f * UnityEngine.Mathf.Deg2Rad), 1f), 1e-3f);
        }

        // The device case (the human sits at a desk): the panel's bottom edge sits just above the desk, so there is no room
        // under it. Build 0ddb26d+#270 shrank it to 18 mm keys; now it keeps its size and rests on the desk.
        [Test]
        public void Dock_OverADesk_KeepsThePanelsWidth_RestsOnTheDesk_InFrontOfThePanel_TiltedToTheEyes()
        {
            const float DeskTop = 0.75f;
            var bottom = new UnityEngine.Vector3(0f, DeskTop + OwnSpace.UI.ToolPanel.SurfaceClearance, 0.6f);
            UnityEngine.Quaternion panel = Facing(bottom);
            UnityEngine.Vector3 forward = (bottom + UnityEngine.Vector3.up * 0.1f - Head).normalized; // looking at the request field
            foreach (float panelScale in new[] { 1f, 1.3f })
            {
                PanelKeyboard.Dock(Head, forward, bottom, panel, 0.30f * panelScale, new Desk(), out UnityEngine.Vector3 p, out UnityEngine.Quaternion r, out float scale);
                Assert.AreEqual(0.30f * panelScale * PanelKeyboard.WidthRatio, PanelKeyboard.Width * scale, 1e-4f, "a desk never makes it smaller: 1.15 times the panel's width at " + panelScale + "x");
                if (panelScale == 1f) Assert.AreEqual(0.0293f, PanelKeyboard.KeyHeight * scale, 0.0005f, "29 mm keys at the default panel size");
                for (int x = -1; x <= 1; x++)
                    Assert.AreEqual(DeskTop + OwnSpace.UI.ToolPanel.SurfaceClearance, Corner(p, r, scale, x, -1f).y, 1e-4f, "its bottom edge rests 5 cm above the desk");
                UnityEngine.Vector3 top = Corner(p, r, scale, 0f, 1f), low = Corner(p, r, scale, 0f, -1f);
                Assert.AreEqual(bottom.z - PanelKeyboard.DeskGap, top.z, 1e-3f, "its top edge is just in front of the panel");
                Assert.Less(low.z, top.z, "between the panel and the user");
                Assert.GreaterOrEqual(low.z, PanelKeyboard.DeskNear - 1e-4f);
                float tilt = UnityEngine.Vector3.Angle(r * UnityEngine.Vector3.up, UnityEngine.Vector3.up);
                Assert.That(tilt, Is.InRange(PanelKeyboard.DeskMinTilt - 0.01f, PanelKeyboard.DeskMaxTilt + 0.01f), "tilted back like a real keyboard");
                float oblique = UnityEngine.Vector3.Angle(-(r * UnityEngine.Vector3.forward), Head - p);
                Assert.LessOrEqual(oblique, PanelKeyboard.DeskOblique + 12f, "its face is turned toward the eyes");
                Assert.IsTrue(PanelKeyboard.InView(Head, forward, p, r, scale), "every corner is in view");
            }
        }

        [Test]
        public void Dock_NeverBelow24mmKeys_EvenToComeIntoView_OrForASmallPanel()
        {
            float floor = PanelKeyboard.MinKey / PanelKeyboard.KeyHeight;
            var bottom = new UnityEngine.Vector3(0f, 0.85f, 0.6f);
            UnityEngine.Quaternion panel = Facing(bottom);
            var forward = new UnityEngine.Vector3(0f, 0.05f, 1f).normalized; // looking straight ahead, over the panel
            PanelKeyboard.Dock(Head, forward, bottom, panel, 0.30f, null, out UnityEngine.Vector3 p, out UnityEngine.Quaternion r, out float scale);
            float full = PanelKeyboard.ScaleFor(0.30f, UnityEngine.Vector3.Distance(Head, bottom - panel * UnityEngine.Vector3.up * PanelKeyboard.DockGap));
            Assert.Less(scale, full, "smaller, to come into view: the one case that shrinks it");
            Assert.GreaterOrEqual(PanelKeyboard.KeyHeight * scale, PanelKeyboard.MinKey - 1e-6f, "never below 24 mm keys");
            Assert.IsTrue(PanelKeyboard.InView(Head, forward, p, r, scale) || UnityEngine.Mathf.Approximately(scale, floor));
            Assert.AreEqual(-PanelKeyboard.DockGap, UnityEngine.Vector3.Dot(Corner(p, r, scale, 0f, 1f) - bottom, r * UnityEngine.Vector3.up), 1e-4f, "it shrinks from its top edge");
            // A 0.8x panel: 1.15 times its width would be 23 mm keys; the floor wins.
            UnityEngine.Vector3 look = (bottom - Head).normalized;
            PanelKeyboard.Dock(Head, look, bottom, panel, 0.24f, null, out _, out _, out float small);
            Assert.AreEqual(PanelKeyboard.MinKey, PanelKeyboard.KeyHeight * small, 1e-6f);
            PanelKeyboard.Dock(Head, look, new UnityEngine.Vector3(0f, 0.8f, 0.6f), Facing(new UnityEngine.Vector3(0f, 0.8f, 0.6f)), 0.24f, new Desk(), out _, out _, out float smallOnDesk);
            Assert.GreaterOrEqual(PanelKeyboard.KeyHeight * smallOnDesk, PanelKeyboard.MinKey - 1e-6f);
            Assert.AreEqual(PanelKeyboard.Bar, PanelKeyboard.HitLocal(new UnityEngine.Vector2(0f, PanelKeyboard.Height * 0.5f - 0.01f)), "its own grab bar");
        }

        [Test]
        public void Keyboard_KeysFitInsideAndEachKeyHitsItself()
        {
            for (int i = 0; i < PanelKeyboard.KeyCount; i++)
            {
                PanelKeyboard.KeyRect(i, out UnityEngine.Vector2 c, out UnityEngine.Vector2 size);
                Assert.LessOrEqual(UnityEngine.Mathf.Abs(c.x) + size.x * 0.5f, PanelKeyboard.Width * 0.5f - PanelKeyboard.Pad + 1e-5f, PanelKeyboard.LabelOf(i));
                Assert.GreaterOrEqual(c.y - size.y * 0.5f, -PanelKeyboard.Height * 0.5f + PanelKeyboard.Pad - 1e-5f, PanelKeyboard.LabelOf(i));
                Assert.GreaterOrEqual(size.x, 0.03f, "a hand ray can hit it");
                Assert.AreEqual(i, PanelKeyboard.HitLocal(c), PanelKeyboard.LabelOf(i));
            }
            Assert.AreEqual(PanelKeyboard.Back, PanelKeyboard.HitLocal(new UnityEngine.Vector2(0f, PanelKeyboard.Height * 0.5f - PanelKeyboard.Pad - PanelKeyboard.BarZone - PanelKeyboard.FieldHeight * 0.5f)), "the echo strip: the keyboard's, no key");
            Assert.AreEqual(-1, PanelKeyboard.HitLocal(new UnityEngine.Vector2(PanelKeyboard.Width, 0f)));
        }
    }
}

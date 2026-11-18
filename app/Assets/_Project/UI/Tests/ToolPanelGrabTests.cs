using NUnit.Framework;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    /// <summary>
    /// Grabbing the panel by its top strip (device bug, build 0.0.2): the whole strip around its buttons is the handle,
    /// a pinch up to <see cref="ToolPanel.NearGrabDepth"/> in front of it grabs near, and a ray grabs far.
    /// The panel sits at the origin facing +z, so panel-local and world coordinates match and z &lt; 0 is in front.
    /// </summary>
    public class ToolPanelGrabTests
    {
        sealed class Page : IPanelPage
        {
            public int Count => 2;
            public int Columns => 2;
            public void Describe(int index, ref CardInfo card) => card.Title = "Card";
            public bool Press(int index) => true;
        }

        GameObject _go, _rigGo;
        ToolPanel _panel;
        HandRig _hands;

        [SetUp]
        public void SetUp()
        {
            _rigGo = new GameObject("Hands");
            _hands = _rigGo.AddComponent<HandRig>();
            _go = new GameObject("Panel");
            _panel = _go.AddComponent<ToolPanel>();
            _panel.Configure(new[] { "Style", "Furniture" }, new IPanelPage[] { new Page(), new Page() });
            _panel.Hands = _hands;
            _panel.Placed = true;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_rigGo);
        }

        float StripY => _panel.Metrics.Size.y * 0.5f - _panel.Metrics.StripTop - _panel.Metrics.StripHeight * 0.5f;

        // A strip point away from the bar and from every button.
        Vector2 StripSpot()
        {
            float half = _panel.Metrics.Size.x * 0.5f;
            for (float x = -half + 0.004f; x < half; x += 0.004f)
            {
                var p = new Vector2(x, StripY);
                if (Mathf.Abs(x) > 0.05f && _panel.Find(p) == ToolPanel.Handle) return p;
            }
            Assert.Fail("no free strip spot");
            return default;
        }

        [Test]
        public void TheWholeTopStripIsTheHandle_ButItsButtonsStayButtons()
        {
            Vector2 spot = StripSpot();
            Assert.Greater(Mathf.Abs(spot.x), 0.05f, "well outside the 64 mm bar");
            Assert.AreEqual(ToolPanel.Handle, _panel.Find(new Vector2(spot.x, _panel.Metrics.Size.y * 0.5f - 0.002f)), "the strip's top edge");
            Assert.AreEqual(ToolPanel.Minimize, _panel.Find(_panel.Element(ToolPanel.Minimize).Centre));
            Assert.AreEqual(ToolPanel.UndoButton, _panel.Find(_panel.Element(ToolPanel.UndoButton).Centre));
            Assert.AreEqual(ToolPanel.SettingsButton, _panel.Find(_panel.Element(ToolPanel.SettingsButton).Centre));
            Assert.AreEqual(0, _panel.Find(_panel.Element(0).Centre), "the tabs below the strip are tabs");
        }

        [Test]
        public void NearGrab_TakesAPinchUpTo8cmInFrontOfTheStrip_OnlyForTheHandle()
        {
            Vector2 spot = StripSpot(), tab = _panel.Element(0).Centre;
            Assert.AreEqual(ToolPanel.Handle, _panel.Poke(new Vector3(spot.x, spot.y, -0.05f)), "5 cm in front grabs");
            Assert.AreEqual(ToolPanel.Handle, _panel.Poke(new Vector3(spot.x, spot.y, -0.075f)), "7.5 cm in front grabs");
            Assert.AreEqual(-1, _panel.Poke(new Vector3(spot.x, spot.y, -0.10f)), "10 cm in front is too far");
            Assert.AreEqual(-1, _panel.Poke(new Vector3(spot.x, spot.y, 0.03f)), "behind the panel");
            Assert.AreEqual(-1, _panel.Poke(new Vector3(tab.x, tab.y, -0.05f)), "a tab still needs a real poke");
            Assert.AreEqual(0, _panel.Poke(new Vector3(tab.x, tab.y, -0.005f)));
        }

        [Test]
        public void NearPinchOnTheStrip_MovesThePanelWithThePinchPoint()
        {
            Vector2 spot = StripSpot();
            Vector3 p0 = _panel.transform.position = new Vector3(0f, 0f, 1f); // out of arm's reach (drags keep 0.45 m from the head)
            var start = p0 + new Vector3(spot.x, spot.y, -0.05f);
            var away = new Ray(new Vector3(0f, -2f, -1f), Vector3.down); // the ray points nowhere near the panel
            _hands.SetForTest(default, Hand(start, started: true), away);
            _panel.Tick();
            Assert.AreEqual(ToolPanel.Handle, _panel.Pressed, "the near pinch grabs the handle");

            var moved = start + new Vector3(0.10f, 0.05f, 0f);
            _hands.SetForTest(default, Hand(moved), away);
            _panel.Tick();
            Assert.AreEqual(p0.x + 0.10f, _panel.transform.position.x, 0.001f);
            Assert.AreEqual(p0.y + 0.05f, _panel.transform.position.y, 0.001f);

            _hands.SetForTest(default, Hand(moved, ended: true), away);
            _panel.Tick();
            Assert.AreEqual(-1, _panel.Pressed);
            Assert.IsTrue(_panel.Placed);
        }

        [Test]
        public void FarRayOnTheStrip_GrabsTheHandle()
        {
            Vector2 spot = StripSpot();
            var ray = new Ray(new Vector3(spot.x + 0.1f, spot.y - 0.3f, -0.5f), Vector3.zero);
            ray.direction = new Vector3(spot.x, spot.y, 0f) - ray.origin;
            var far = new Vector3(0.2f, -0.4f, -0.3f); // the hand rests well in front, out of near reach
            _hands.SetForTest(default, Hand(far, started: true), ray);
            _panel.Tick();
            Assert.AreEqual(ToolPanel.Handle, _panel.Pressed);
        }

        sealed class DragPage : IPanelPage, IPanelDragOut
        {
            public int Taken = -1;
            public int Count => 2;
            public int Columns => 2;
            public void Describe(int index, ref CardInfo card) { card.Title = "Card"; card.Kind = CardKind.Card; card.Row = 0; }
            public bool Press(int index) => true;
            public bool DragOut(int index) { Taken = index; return true; }
        }

        // Device batch 8: a card pinched and dragged off the panel starts a drag outside instead of a press.
        [Test]
        public void ACardDraggedOffThePanel_IsHandedOut_Once()
        {
            var page = new DragPage();
            _panel.Configure(new[] { "Add" }, new IPanelPage[] { page });
            int handedOut = 0;
            _panel.CardDraggedOut += left => handedOut++;
            Vector3 f = _panel.transform.forward, card = _panel.Element(ToolPanel.MaxTabs).Root.position;
            _hands.SetForTest(default, Hand(new Vector3(9f, 9f, 9f), started: true), new Ray(card - f * 0.5f, f));
            _panel.Tick();
            Assert.AreEqual(ToolPanel.MaxTabs, _panel.Pressed);
            var off = new Ray(card - f * 0.5f + Vector3.right * 2f, f); // the ray leaves the panel, still pinching
            _hands.SetForTest(default, Hand(new Vector3(9f, 9f, 9f)), off);
            _panel.Tick();
            Assert.AreEqual(1, handedOut);
            Assert.AreEqual(0, page.Taken);
            Assert.AreEqual(-1, _panel.Pressed, "the press ends: no tap on release");
            _panel.Tick();
            Assert.AreEqual(1, handedOut, "once");
        }

        static HandState Hand(Vector3 pinch, bool started = false, bool ended = false) => new HandState
        {
            Tracked = true,
            Pinching = !ended,
            PinchStarted = started,
            PinchEnded = ended,
            PinchPoint = pinch,
        };
    }
}

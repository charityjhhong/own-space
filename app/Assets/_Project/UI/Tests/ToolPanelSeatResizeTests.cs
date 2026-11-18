using NUnit.Framework;
using OwnSpace.Interaction;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    // Device day: the panel seats right of the right hand and resizes from its bottom-right grip (0.8-1.6x).
    public class ToolPanelSeatResizeTests
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

        [TearDown] public void TearDown() { Object.DestroyImmediate(_go); Object.DestroyImmediate(_rigGo); }

        [Test]
        public void Seat_IsFromTheHead_SlightlyRightOfTheViewCentre()
        {
            PanelMetrics m = PanelMetrics.Default;
            var head = new Vector3(0f, 1.2f, 0f);
            Vector3 seat = ToolPanel.HeadSeat(head, Vector3.forward, m.Ahead, m, 1f);
            Assert.AreEqual(ToolPanel.SeatRight, seat.x, 1e-4f, "a little right of centre");
            Assert.Greater(seat.z, 0.3f);
            Vector3 turned = ToolPanel.HeadSeat(head, Vector3.right, m.Ahead, m, 1f);
            Assert.AreEqual(-ToolPanel.SeatRight, turned.z, 1e-4f, "right of wherever the user looks");
        }

        // Device day (d520910): the hand-anchored seat moved with every hand motion. The hand never moves the panel.
        [Test]
        public void MovingTheHand_DoesNotMoveASeatedPanel()
        {
            _panel.Open(true);
            for (int i = 0; i < 40; i++) _panel.Tick(); // settle
            Vector3 seated = _panel.transform.position;
            Ray away = new Ray(new Vector3(0f, -5f, 0f), Vector3.down);
            for (int i = 0; i < 30; i++)
            {
                var hand = new Vector3(Mathf.Sin(i) * 0.4f, 0.8f + 0.01f * i, 0.3f + Mathf.Cos(i) * 0.2f);
                _hands.SetForTest(default, new HandState { Tracked = true, PinchPoint = hand }, away);
                _panel.Tick();
            }
            Assert.AreEqual(seated, _panel.transform.position);
        }

        [Test]
        public void WhileTheRayHoversThePanel_TheFollowWaits()
        {
            _panel.Open(true);
            for (int i = 0; i < 40; i++) _panel.Tick();
            Vector3 f = _panel.transform.forward;
            _panel.Placed = false;
            Vector3 drifted = _panel.transform.position + new Vector3(0f, 0f, 0.6f); // past MaxDistance: the follow wants it back
            _panel.transform.position = drifted;
            var onPanel = new Ray(_panel.TabPosition(0) - f * 0.4f, f);
            _hands.SetForTest(default, default, onPanel);
            for (int i = 0; i < 5; i++) _panel.Tick();
            Assert.AreEqual(drifted, _panel.transform.position, "pointed at: it stays put");
            _hands.SetForTest(default, default, new Ray(new Vector3(0f, -5f, 0f), Vector3.down));
            for (int i = 0; i < 5; i++) _panel.Tick();
            Assert.AreNotEqual(drifted, _panel.transform.position, "pointed away: it eases back");
        }

        [Test]
        public void Scale_IsClamped_AndFollowsTheGripDistance()
        {
            Assert.AreEqual(0.8f, ToolPanel.ClampScale(0.3f));
            Assert.AreEqual(1.6f, ToolPanel.ClampScale(4f));
            Assert.AreEqual(1f, ToolPanel.ClampScale(float.NaN));
            Assert.AreEqual(1.5f, ToolPanel.ResizeScale(1f, 0.4f, 0.6f), 1e-5f);
            Assert.AreEqual(1.6f, ToolPanel.ResizeScale(1.2f, 0.4f, 1f), 1e-5f, "never past 1.6x");
            _panel.SetScale(2f);
            Assert.AreEqual(1.6f, _panel.Scale);
            Assert.AreEqual(Vector3.one * 1.6f, _panel.transform.localScale);
        }

        [Test]
        public void At15x_PokesAndRaysHitTheScaledTabs()
        {
            _panel.SetScale(1.5f);
            Vector3 tab = _panel.TabPosition(1), f = _panel.transform.forward;
            Assert.AreEqual(1, _panel.Poke(tab - f * 0.01f), "a poke 1 cm in front (scaled depth)");
            Assert.AreEqual(-1, _panel.Poke(tab - f * 0.03f), "3 cm out: no poke even at 1.5x");
            _hands.SetForTest(default, Hand(new Vector3(5f, 5f, 5f), true), new Ray(tab - f * 0.5f, f));
            _panel.Tick();
            Assert.AreEqual(1, _panel.Pressed, "a ray at the scaled tab presses it");
        }

        [Test]
        public void DraggingTheGrip_ScalesAboutTheTopLeft_DoublePinchResets()
        {
            float saved = -1f;
            _panel.ScaleChanged += s => saved = s;
            Vector3 topLeft = _panel.transform.TransformPoint(new Vector3(-_panel.Metrics.Size.x * 0.5f, _panel.Metrics.Size.y * 0.5f, 0f));
            Vector3 grip = _panel.Element(ToolPanel.Resize).Root.position, f = _panel.transform.forward;
            _hands.SetForTest(default, Hand(grip - f * 0.005f, true), new Ray(Vector3.zero, Vector3.down));
            _panel.Tick();
            Assert.AreEqual(ToolPanel.Resize, _panel.Pressed);
            Vector3 further = topLeft + (grip - topLeft) * 1.3f - f * 0.005f;
            _hands.SetForTest(default, Hand(further, false), new Ray(Vector3.zero, Vector3.down));
            _panel.Tick();
            Assert.AreEqual(1.3f, _panel.Scale, 0.02f);
            Vector3 topLeftNow = _panel.transform.TransformPoint(new Vector3(-_panel.Metrics.Size.x * 0.5f, _panel.Metrics.Size.y * 0.5f, 0f));
            Assert.Less(Vector3.Distance(topLeft, topLeftNow), 1e-3f, "the top-left corner stays put");
            _hands.SetForTest(default, Hand(further, false, true), new Ray(Vector3.zero, Vector3.down));
            _panel.Tick();
            Assert.AreEqual(_panel.Scale, saved, 1e-5f, "saved on release");

            for (int i = 0; i < 2; i++) { _panel.BeginPress(ToolPanel.Resize, false); _panel.Release(ToolPanel.Resize); }
            Assert.AreEqual(1f, _panel.Scale, "double-pinch: back to 1.0x");
            Assert.AreEqual(1f, saved);
        }

        static HandState Hand(Vector3 pinch, bool started, bool ended = false) => new HandState
        {
            Tracked = true, Pinching = !ended, PinchStarted = started, PinchEnded = ended, PinchPoint = pinch,
        };
    }
}

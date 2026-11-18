using NUnit.Framework;
using OwnSpace.Interaction;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    /// <summary>Sim pass: a click on the panel must not reach the room (it selected the box behind the panel).</summary>
    public class PanelPassThroughTests
    {
        sealed class Page : IPanelPage
        {
            public int Count => 1;
            public int Columns => 1;
            public void Describe(int i, ref CardInfo c) => c.Title = "x";
            public bool Press(int index) => true;
        }

        GameObject _rig, _panelGo;
        HandRig _hands;
        ToolPanel _panel;

        [SetUp]
        public void SetUp()
        {
            _rig = new GameObject("Hands");
            _hands = _rig.AddComponent<HandRig>();
            _panelGo = new GameObject("Panel");
            _panelGo.transform.position = new Vector3(0f, 1f, 1f); // 1 m ahead, facing away from the user (+z)
            _panel = _panelGo.AddComponent<ToolPanel>();
            _panel.Hands = _hands;
            // The real fonts (content's Inter), so text fitting is measured on real glyphs.
            var so = new UnityEditor.SerializedObject(_panel);
            so.FindProperty("_font").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Inter-Regular SDF.asset");
            so.FindProperty("_fontSemiBold").objectReferenceValue = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Inter-SemiBold SDF.asset");
            so.ApplyModifiedPropertiesWithoutUndo();
            _panel.Configure(new[] { "Style", "Furniture", "AI Designer", "Tidy up" }, new IPanelPage[] { new Page(), new Page(), new Page(), new Page() });
            _panel.Placed = true; // no follow during the test
        }

        [TearDown] public void TearDown() { Object.DestroyImmediate(_panelGo); Object.DestroyImmediate(_rig); }

        static HandState Click() => new HandState { Tracked = true, Pinching = true, PinchStarted = true, PinchPoint = new Vector3(5f, 5f, 5f) };

        [Test]
        public void SimClickOnThePanel_IsClaimed_SoEditModeDoesNotSelect()
        {
            // The simulator's Point & click: both hands start a pinch, aimed by the click ray at the second tab.
            Vector3 tab = _panel.TabPosition(1);
            var aim = new Ray(new Vector3(tab.x, tab.y, 0f), Vector3.forward);
            _hands.SetForTest(Click(), Click(), aim);
            _panel.Tick();
            Assert.IsTrue(_hands.ClaimedThisFrame, "the panel claims the click");
            Assert.AreEqual(1, _panel.Pressed, "on the tab it points at");
            Assert.IsFalse(EditMode.TakesPinch(true, _hands.ClaimedThisFrame), "EditMode (runs after the panel) leaves it");
        }

        [Test]
        public void SimClickBesideThePanel_IsNotClaimed()
        {
            _hands.SetForTest(Click(), Click(), new Ray(new Vector3(2f, 1f, 0f), Vector3.forward));
            _panel.Tick();
            Assert.IsFalse(_hands.ClaimedThisFrame);
            Assert.IsTrue(EditMode.TakesPinch(true, _hands.ClaimedThisFrame), "the room gets it");
        }

        [Test]
        public void Tabs_FitTheirWords()
        {
            for (int i = 0; i < 4; i++)
            {
                PanelElement t = _panel.Element(i);
                t.Text.ForceMeshUpdate();
                StringAssert.Contains("Inter", t.Text.font.name, "measured with Inter");
                Assert.IsFalse(t.Text.isTextTruncated, $"tab {i} is not cut");
            }
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    public class PanelFeedbackTests
    {
        GameObject _go;
        ToolPanel _panel;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Panel");
            _panel = _go.AddComponent<ToolPanel>();
            _panel.Configure(new[] { "Style" }, new IPanelPage[] { null });
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void Icons_ReadTheAtlasRects()
        {
            var icons = new PanelIcons("{\"width\":512,\"height\":256,\"icons\":[{\"name\":\"undo\",\"x\":80,\"y\":80,\"w\":80,\"h\":80}]}");
            Vector4 r = icons.Rect("undo");
            Assert.AreEqual(80f / 512f, r.x, 1e-6f);
            Assert.AreEqual(80f / 256f, r.y, 1e-6f);
            Assert.AreEqual(80f / 512f, r.z, 1e-6f);
            Assert.AreEqual(1f - 160f / 256f, r.w, 1e-6f, "v from the bottom");
            Assert.AreEqual(Vector4.zero, icons.Rect("missing"));
        }

        [Test]
        public void Toast_AbovePanel_PressRunsItsAction_Refusal()
        {
            int pressed = 0;
            _panel.ToastPressed += () => pressed++;
            _panel.ShowToast("Oak floor · My zone  Undo");
            Assert.IsTrue(_panel.ToastShown);
            PanelElement toast = _panel.Element(ToolPanel.Toast);
            Assert.Greater(toast.Centre.y, _panel.Metrics.Size.y * 0.5f, "outside the panel, above it");
            Assert.AreEqual(ToolPanel.Toast, _panel.Find(toast.Centre));
            _panel.BeginPress(ToolPanel.Toast, false);
            _panel.Release(ToolPanel.Toast);
            Assert.AreEqual(1, pressed);
            Assert.IsFalse(_panel.ToastShown);
            _panel.ShowToast("Doesn't fit this box", refused: true);
            Assert.AreEqual(0f, toast.Fade, "starts faded out and fades in over 150 ms");
            Assert.AreEqual(ElementState.Refused, toast.State);
        }

        // The human: minimize already hides the tools, so the eye (Focus) button is gone; settings sits next to minimize.
        [Test]
        public void NoEyeButton_SettingsSitsNextToMinimize()
        {
            int focus = 0;
            _panel.FocusPressed += () => focus++;
            PanelElement f = _panel.Element(ToolPanel.FocusButton), settings = _panel.Element(ToolPanel.SettingsButton),
                minimize = _panel.Element(ToolPanel.Minimize), handle = _panel.Element(ToolPanel.Handle);
            Assert.IsFalse(f.Visible, "the eye button never shows");
            Assert.AreEqual(ToolPanel.SettingsButton, _panel.Find(settings.Centre), "settings takes the eye's old spot");
            Assert.Less(settings.Centre.x, minimize.Centre.x);
            Assert.Greater(settings.Centre.x - settings.Size.x * 0.5f, handle.Centre.x + handle.Size.x * 0.5f, "settings still clears the handle bar");
            Assert.LessOrEqual(settings.Centre.x + settings.Size.x * 0.5f, minimize.Centre.x - minimize.Size.x * 0.5f, "no overlap with minimize");
            Assert.AreEqual(0, focus);
        }

        [Test]
        public void StripButtons_RaiseEvents_HintAbovePanel()
        {
            int undo = 0, settings = 0;
            _panel.UndoPressed += () => undo++;
            _panel.SettingsPressed += () => settings++;
            _panel.BeginPress(ToolPanel.UndoButton, false);
            _panel.Release(ToolPanel.UndoButton);
            _panel.BeginPress(ToolPanel.SettingsButton, false);
            _panel.Release(ToolPanel.SettingsButton);
            Assert.AreEqual(1, undo);
            Assert.AreEqual(1, settings);
            Assert.AreEqual(ToolPanel.UndoButton, _panel.Find(_panel.Element(ToolPanel.UndoButton).Centre));
            _panel.ShowHint("Pinch the floor to place");
            Assert.IsTrue(_panel.HintShown);
            Assert.Greater(_panel.HintCentreY, _panel.Metrics.Size.y * 0.5f, "above the panel (device batch 4: the bottom was out of view)");
            Assert.AreNotEqual(ToolPanel.Hint, _panel.Find(new Vector2(0f, _panel.HintCentreY)), "a plain hint is not pressable");
            int skips = 0;
            _panel.HintPressed += () => skips++;
            _panel.ShowHint("Ask for a room · Skip tips", pressable: true);
            Assert.AreEqual(ToolPanel.Hint, _panel.Find(new Vector2(0f, _panel.HintCentreY)));
            _panel.BeginPress(ToolPanel.Hint, false);
            _panel.Release(ToolPanel.Hint);
            Assert.AreEqual(1, skips, "pinching the onboarding hint skips the tips");
            _panel.HideHint();
            Assert.IsFalse(_panel.HintShown);
        }

        sealed class TwoTabs : IPanelPage
        {
            public int Count => 1;
            public int Columns => 1;
            public void Describe(int i, ref CardInfo c) { c.Kind = CardKind.Card; c.Title = "Sofa"; c.Thumb = new Vector4(0.1f, 0.1f, 0f, 0f); }
            public bool Press(int index) => true;
        }

        [Test]
        public void TabAndMinimizeEvents_ForTheHost_AndThumbnailsUseThePanelMaterial()
        {
            _panel.Configure(new[] { "Style", "Furniture" }, new IPanelPage[] { new TwoTabs(), new TwoTabs() });
            int tab = -1, minimized = 0;
            _panel.TabChanged += t => tab = t;
            _panel.MinimizedChanged += m => { if (m) minimized++; };
            _panel.SelectTab(1);
            Assert.AreEqual(1, tab);
            Assert.AreEqual(_panel.Element(1).Root.position, _panel.TabPosition(1));
            _panel.SetMinimized(true, instant: true);
            Assert.AreEqual(1, minimized);
            _panel.SetMinimized(false, instant: true);
            Renderer thumb = _panel.Element(ToolPanel.MaxTabs).Root.Find("Thumb").GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            thumb.GetPropertyBlock(block);
            Assert.AreEqual(2f, block.GetFloat("_IconOn"), "thumbnail mode of the glass shader: no extra material");
        }
    }
}

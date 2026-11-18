using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    public class ToolPanelChromeTests
    {
        sealed class Page : IPanelPage
        {
            public int Count => 2;
            public int Columns => 2;
            public void Describe(int index, ref CardInfo card) => card.Title = "Card";
            public bool Press(int index) => true;
        }

        GameObject _go;
        ToolPanel _panel;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Panel");
            _panel = _go.AddComponent<ToolPanel>();
            _panel.Configure(new[] { "Style", "Furniture" }, new IPanelPage[] { new Page(), new Page() });
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void SetTheme_SwapsTokens_AndReportsOnlyChanges()
        {
            int changes = 0;
            _panel.ThemeChanged += _ => changes++;
            _panel.SetTheme(PanelTheme.Dark);
            Assert.AreEqual(PanelTheme.Dark, _panel.Theme);
            Assert.AreSame(PanelThemes.DarkDefault, _panel.Style);
            _panel.SetTheme(PanelTheme.Dark);
            _panel.SetTheme(PanelTheme.Light);
            Assert.AreSame(PanelStyle.Default, _panel.Style);
            Assert.AreEqual(2, changes);
            Object.DestroyImmediate(PanelThemes.DarkDefault); // made by this test; the next use makes a new one
        }

        [Test]
        public void Minimized_OnlyThePillIsThere_ExpandBringsCardsBack()
        {
            Vector2 card = _panel.Element(ToolPanel.MaxTabs).Centre;
            _panel.SetMinimized(true, instant: true);
            Assert.IsTrue(_panel.Element(ToolPanel.Pill).Visible);
            Assert.IsFalse(_panel.Element(ToolPanel.MaxTabs).Visible);
            Assert.AreEqual(ToolPanel.Pill, _panel.Find(_panel.Element(ToolPanel.Pill).Centre));
            Assert.AreEqual(-1, _panel.Find(card), "the space the panel left is not claimed");
            _panel.SetMinimized(false, instant: true);
            Assert.IsTrue(_panel.Element(ToolPanel.MaxTabs).Visible);
            Assert.AreEqual(ToolPanel.MaxTabs, _panel.Find(card));
        }

        [Test]
        public void MinimizeButtonAndPill_TogglesOnRelease()
        {
            _panel.BeginPress(ToolPanel.Minimize, false);
            _panel.Release(ToolPanel.Minimize);
            Assert.IsTrue(_panel.Minimized);
            _panel.SetMinimized(true, instant: true);
            _panel.BeginPress(ToolPanel.Pill, false);
            _panel.Release(ToolPanel.Pill);
            Assert.IsFalse(_panel.Minimized);
        }

        [Test]
        public void HandleDoublePinch_SendsAPlacedPanelBack()
        {
            _panel.Placed = true;
            _panel.BeginPress(ToolPanel.Handle, false);
            _panel.Release(ToolPanel.Handle);
            Assert.IsTrue(_panel.Placed, "one pinch does nothing");
            _panel.BeginPress(ToolPanel.Handle, false);
            _panel.Release(ToolPanel.Handle);
            Assert.IsFalse(_panel.Placed);
            _panel.Placed = true;
            _panel.BeginPress(ToolPanel.Handle, false);
            _panel.Release(ToolPanel.Handle);
            Assert.IsTrue(_panel.Placed, "a third pinch starts a new pair");
        }
    }
}

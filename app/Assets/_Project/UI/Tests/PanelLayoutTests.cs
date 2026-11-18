using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.UI.Tests
{
    public class PanelLayoutTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void GridCell_FillsRowsLeftToRightTopToBottom()
        {
            var cell = new Vector2(0.1f, 0.06f);
            var gap = new Vector2(0.01f, 0.01f);
            var topLeft = new Vector2(-0.2f, 0.1f);
            Vector2 first = PanelLayout.GridCell(0, 3, cell, gap, topLeft);
            Vector2 fourth = PanelLayout.GridCell(3, 3, cell, gap, topLeft);
            Assert.AreEqual(-0.15f, first.x, Eps);
            Assert.AreEqual(0.07f, first.y, Eps);
            Assert.AreEqual(first.x, fourth.x, Eps, "wraps to the next row");
            Assert.AreEqual(0.07f - 0.07f, fourth.y, Eps);
        }

        [Test]
        public void TabCell_SplitsTheBarEvenly()
        {
            Assert.AreEqual(-0.15f, PanelLayout.TabCell(0, 4, 0.4f, 0f).x, Eps);
            Assert.AreEqual(0.15f, PanelLayout.TabCell(3, 4, 0.4f, 0f).x, Eps);
        }

        [Test]
        public void RayToPanel_HitsTheFacingPanel_InLocalCoordinates()
        {
            // Panel 1 m ahead facing away from the user (+z), user at the origin looking +z.
            var rot = Quaternion.LookRotation(Vector3.forward);
            var ray = new Ray(Vector3.zero, new Vector3(0.1f, 0.05f, 1f).normalized);
            Assert.IsTrue(PanelLayout.RayToPanel(ray, new Vector3(0f, 0f, 1f), rot, out Vector2 local, out float distance));
            Assert.AreEqual(0.1f, local.x, Eps);
            Assert.AreEqual(0.05f, local.y, Eps);
            Assert.Greater(distance, 1f);
        }

        [Test]
        public void RayToPanel_MissesWhenPointingAway()
        {
            var ray = new Ray(Vector3.zero, Vector3.back);
            Assert.IsFalse(PanelLayout.RayToPanel(ray, new Vector3(0f, 0f, 1f), Quaternion.identity, out _, out _));
        }

        [Test]
        public void Contains_UsesCentreAndSizePlusPad()
        {
            Assert.IsTrue(PanelLayout.Contains(Vector2.zero, new Vector2(0.1f, 0.06f), new Vector2(0.05f, 0.03f)));
            Assert.IsFalse(PanelLayout.Contains(Vector2.zero, new Vector2(0.1f, 0.06f), new Vector2(0.06f, 0f)));
            Assert.IsTrue(PanelLayout.Contains(Vector2.zero, new Vector2(0.1f, 0.06f), new Vector2(0.054f, 0f), 0.005f));
        }

        [Test]
        public void Look_UsesTheStateSlot_SwatchOnlyWhenNormalOrHover()
        {
            PanelStyle style = PanelStyle.Default;
            var swatch = new Color(0.2f, 0.4f, 0.6f, 1f);
            Assert.AreEqual(swatch, PanelElement.Look(style, ElementState.Normal, swatch, false).Fill);
            Assert.AreEqual(style.Selected.Fill, PanelElement.Look(style, ElementState.Selected, swatch, false).Fill, "selected stays clearly selected");
            Assert.Less(style.Selected.Fill.grayscale, 0.3f, "selected is inverted: dark on the light glass");
            Assert.AreEqual(style.Refused.Rim, PanelElement.Look(style, ElementState.Refused, default, false).Rim);
            Assert.AreEqual(style.Surface.Fill, PanelElement.Look(style, ElementState.Hover, swatch, true).Fill);
        }

        [Test]
        public void RealDisc_IsSeeThroughWithARing_SelectedRingIsTheAccent()
        {
            PanelStyle style = PanelStyle.Default;
            PanelStyle.Slot real = PanelElement.Look(style, ElementState.Normal, default, false, CardKind.Disc);
            Assert.AreEqual(0f, real.Fill.a, "the room shows through");
            Assert.Greater(real.Rim.a, 0f);
            Assert.AreEqual(style.Accent, PanelElement.Look(style, ElementState.Selected, default, false, CardKind.Disc).Rim);
        }
    }
}

using NUnit.Framework;
using OwnSpace.UI;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Device batch 10: every in-room text in the panel's look ("Can't place it" was raw pink TextMesh over a piece).
    public class GlassTextTests
    {
        GameObject _panelGo, _host;

        [SetUp]
        public void SetUp()
        {
            _panelGo = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab"));
            _host = new GameObject("Host");
            WorldLabel.UsePanel(_panelGo.GetComponent<ToolPanel>());
        }

        [TearDown]
        public void TearDown()
        {
            WorldLabel.UsePanel(null);
            Object.DestroyImmediate(_host);
            Object.DestroyImmediate(_panelGo);
        }

        [Test]
        public void Labels_AreGlassChips_RefusalsRed_NoTextMesh()
        {
            var name = new WorldLabel(_host.transform, "Name", Color.white);
            var refusal = new WorldLabel(_host.transform, "Refusal", Color.red, refusal: true);
            name.Show("Slate lounge sofa", new Vector3(0f, 1f, 2f), Vector3.up * 1.2f);
            refusal.Show(RefusalText.Of(OwnSpace.Core.PlacementStatus.InMyZone), new Vector3(0f, 1f, 2f), Vector3.up * 1.2f, 2f);
            Assert.IsTrue(name.IsGlass && refusal.IsGlass);
            Assert.IsTrue(name.Shown && refusal.Shown);
            Assert.AreEqual(0, _host.GetComponentsInChildren<TextMesh>(true).Length, "no raw world text");
            TMPro.TextMeshPro[] texts = _host.GetComponentsInChildren<TMPro.TextMeshPro>(true);
            Assert.AreEqual(2, texts.Length, "Inter on glass");
            foreach (TMPro.TextMeshPro t in texts) { t.ForceMeshUpdate(); Assert.IsFalse(t.isTextTruncated, t.text); }
            MeshRenderer red = _host.transform.Find("Refusal").GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(red);
            name.Hide();
            Assert.IsFalse(name.Shown);
        }

        [Test]
        public void Chip_StacksLines()
        {
            Assert.AreEqual(1, GlassChip.LineCount("Pick a mood"));
            Assert.AreEqual(2, GlassChip.LineCount("Pinch your desk\n(or open My room)"));
            var chip = new GlassChip(_host.transform, _panelGo.GetComponent<ToolPanel>(), "Hint");
            chip.ShowLocal("Pinch your desk\n(or open My room)", Vector3.zero);
            TMPro.TextMeshPro t = _host.GetComponentInChildren<TMPro.TextMeshPro>(true);
            t.ForceMeshUpdate();
            Assert.IsFalse(t.isTextTruncated);
            Assert.AreEqual(2, t.textInfo.lineCount);
        }

        [Test]
        public void ZoneOutline_IsTheAccent_NotGreen()
        {
            Assert.AreEqual(new Color(0.039f, 0.455f, 0.878f), ZoneSetup.Accent);
        }
    }
}

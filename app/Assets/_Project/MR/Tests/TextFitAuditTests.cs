using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Content;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    /// <summary>
    /// Device batch 10, the human's text rule: no "…" anywhere at 1.0x. Builds the real panel prefab (Inter) with the real
    /// pages and content (catalog, finishes, moods), walks every tab, every Add category and page, My room on each desk,
    /// Settings, the banner and the toasts, and fails on any text TextMeshPro had to truncate (after shrinking to 10 mm).
    /// </summary>
    public class TextFitAuditTests
    {
        sealed class Outside : IOutsideArm
        {
            public CatalogItem Armed { get; private set; }
            public void Arm(CatalogItem item) => Armed = item;
            public string SelectedName { get; set; }
            public bool RotateSelected() => true;
            public bool RemoveSelected() => true;
            public void Deselect() { }
        }

        sealed class Redesign : IRedesignTarget
        {
            public FurnitureAnchor Selected { get; set; }
            public readonly List<(CatalogItem item, FitResult fit)> List = new List<(CatalogItem, FitResult)>();
            public IReadOnlyList<(CatalogItem item, FitResult fit)> Fits => List;
            public string CurrentDesignId { get; set; }
            public bool ApplyDesign(int index, out string reason) { reason = null; return true; }
            public bool KeepReal() => true;
            public void Deselect() { }
            public bool AdjustSelected(AdjustAction action) => true;
            public bool CanAdjust(AdjustAction action) => true;
            public bool RealTopOn => true;
        }

        sealed class EmptyRoom : IRoomInfo
        {
            public bool HasScan => false;
            public IReadOnlyList<Vec2> FloorOutline { get; } = new List<Vec2>();
            public float FloorY => 0f;
            public float CeilingY => 2.5f;
            public IReadOnlyList<RoomElement> Walls { get; } = new List<RoomElement>();
            public IReadOnlyList<RoomElement> Windows { get; } = new List<RoomElement>();
            public IReadOnlyList<RoomElement> Doors { get; } = new List<RoomElement>();
            public IReadOnlyList<FurnitureAnchor> Furniture { get; } = new List<FurnitureAnchor>();
            public event System.Action Changed { add { } remove { } }
        }

        static string Text(string path) => AssetDatabase.LoadAssetAtPath<TextAsset>(path).text;

        [Test]
        public void NoTextIsCutOff_OnAnyPage_At1x()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/UI/ToolPanel.prefab");
            var go = Object.Instantiate(prefab);
            var cut = new List<string>();
            var names = new HashSet<string>(); // catalog display names are content's strings: reported, not failed
            var contentNames = new SortedSet<string>();
            try
            {
                var panel = go.GetComponent<ToolPanel>();
                var font = (TMP_FontAsset)new SerializedObject(panel).FindProperty("_font").objectReferenceValue;
                ICatalog catalog = CatalogLoader.Load(Text("Assets/_Project/Content/catalog.json")).Catalog;
                foreach (CatalogItem item in catalog.Items) names.Add(PanelText.TwoLines(item.name));
                string finishes = Text("Assets/_Project/Content/finishes.json"), moods = Text("Assets/_Project/Content/mood-presets.json");
                var session = new DesignSession(new EmptyRoom());
                var style = new StylePage(session, null, StylePage.Finishes(finishes, "floor"), StylePage.Finishes(finishes, "wall"), StylePage.Moods(moods));
                var outside = new Outside();
                var redesign = new Redesign();
                var thumbs = new FurnitureThumbs(Text("Assets/_Project/Content/thumbnails.json"));
                var mine = new FurniturePage(catalog, outside, redesign, thumbs, font, FurniturePage.Job.MyFurniture);
                var add = new FurniturePage(catalog, outside, redesign, thumbs, font, FurniturePage.Job.AddFurniture);
                var ai = new AiDesignerPage(null, null, 0.268f, font);
                var tidy = new TidyUpPage(null, font);
                var settings = new SettingsPage(font);
                panel.Configure(PanelHost.Tabs, new IPanelPage[] { style, mine, add, ai, tidy });

                void Check(string where)
                {
                    panel.Invalidate();
                    foreach (TextMeshPro t in go.GetComponentsInChildren<TextMeshPro>())
                    {
                        if (string.IsNullOrEmpty(t.text)) continue;
                        t.ForceMeshUpdate();
                        if (t.text.Contains("…") || t.text.Contains("...")) cut.Add($"{where}: dots in \"{t.text.Replace("\n", " / ")}\"");
                        if (!t.isTextTruncated) continue;
                        // Catalog names count too (device shot 222215: "Birch compact d..."): a card's name wraps, it is never cut.
                        cut.Add($"{where}: \"{t.text.Replace("\n", " / ")}\"");
                    }
                }

                panel.SelectTab(PanelHost.StyleTab); Check("Style · My zone");
                style.Press(2); Check("Style · Outside");
                panel.SelectTab(PanelHost.AddTab);
                for (int c = 0; c < FurniturePage.Categories.Length; c++)
                {
                    add.Press(c);
                    for (int p = 0; p < 6; p++) { Check($"Add · {FurniturePage.Categories[c]} · page {p + 1}"); add.Press(15); }
                }
                panel.SelectTab(PanelHost.MyTab);
                redesign.Selected = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.4f, 0f), new Vec3(1.2f, 0.75f, 0.6f), 0f));
                foreach (CatalogItem item in catalog.Items) if (item.role == FurnitureRole.Table) redesign.List.Add((item, default));
                mine.Refresh(); Check("My room · a desk");
                redesign.List.Clear(); mine.Refresh(); Check("My room · nothing fits");
                panel.SelectTab(PanelHost.AiTab); Check("AI");
                panel.SelectTab(PanelHost.TidyTab); Check("Tidy up");
                panel.ShowSheet(settings); Check("Settings");
                for (int k = 0; k < settings.Count; k++) { var c = new CardInfo(); settings.Describe(k, ref c); if (c.Title == SettingsPage.ResetLabel) { settings.Press(k); break; } }
                Check("Settings · Reset confirm"); // Reset: step 1
                panel.CloseSheet();
                string longest = "";
                foreach (CatalogItem item in catalog.Items) if (item.AllowedIn("outside") && item.name.Length > longest.Length) longest = item.name;
                panel.ShowHint(PanelHost.PlaceBanner(longest), true); Check("Banner");
                panel.ShowToast("Design reset"); Check("Toast");
            }
            finally { Object.DestroyImmediate(go); }
            if (contentNames.Count > 0) Debug.Log("Catalog names too long for a card at 10 mm (content to shorten): " + string.Join(", ", contentNames));
            Assert.IsEmpty(cut, "cut off:\n" + string.Join("\n", cut));
        }
    }
}

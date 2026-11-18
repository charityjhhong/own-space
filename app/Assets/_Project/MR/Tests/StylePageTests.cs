using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class StylePageTests
    {
        DesignSession _session;
        StylePage _page;
        int _changes;

        [SetUp]
        public void SetUp()
        {
            _session = new DesignSession(FakeRoomInfo.DormRoom());
            _changes = 0;
            var floors = new[] { new StylePage.Option("oak", "Oak", Color.yellow), new StylePage.Option("tiles", "Tiles", Color.grey) };
            var walls = new[] { new StylePage.Option("cream", "Cream", Color.white) };
            var moods = new[] { new StylePage.Option("evening", "Evening", Color.red) };
            _page = new StylePage(_session, () => _changes++, floors, walls, moods);
        }

        CardInfo Card(int i) { var c = new CardInfo(); _page.Describe(i, ref c); return c; }

        [Test]
        public void Layout_TitleAndTogglesThenThreeSectionsOfFive()
        {
            Assert.AreEqual(21, _page.Count, "Edit my zone lives in Settings > My space, not here");
            Assert.AreEqual(CardKind.Title, Card(0).Kind);
            Assert.AreEqual("", Card(0).Title, "no Apply to row: one choice for the whole room (the human, 8fdbcda)");
            Assert.AreEqual("", Card(1).Title);
            Assert.AreEqual("", Card(2).Title);
            Assert.IsFalse(_page.Press(1)); Assert.IsFalse(_page.Press(2));
            Assert.AreEqual(StylePage.PlainLabel, Card(4).Title);
            Assert.IsTrue(Card(4).Selected, "Plain floor at the start");
            Assert.AreEqual(CardKind.Disc, Card(16).Kind);
            Assert.AreEqual(CardKind.Disc, Card(5).Kind, "floor options are circles too");
            Assert.IsTrue(Card(7).Disabled, "fewer than four floors: the empty slots are disabled");
        }

        [Test]
        public void FloorSwatch_AppliesToTheWholeFloor_OnceAsOneChange()
        {
            Assert.IsTrue(_page.Press(5)); // Oak
            Assert.AreEqual("oak", _session.State.myZoneFinish.floorFinishId);
            Assert.AreEqual("oak", _session.State.outsideFinish.floorFinishId, "one floor everywhere (the human, 8fdbcda)");
            Assert.IsTrue(Card(5).Selected);
            _page.Press(5);
            Assert.AreEqual(1, _changes, "the same choice again is not a change");
            _page.Press(11); // Cream wall: the whole room too
            Assert.AreEqual("cream", _session.State.outsideFinish.wallFinishId);
            Assert.AreEqual("cream", _session.State.myZoneFinish.wallFinishId);
            Assert.AreEqual(2, _changes);
        }

        [Test]
        public void MoodDisc_SetsTheRoomMood_RealClearsIt()
        {
            _page.Press(17);
            Assert.AreEqual("evening", _session.State.moodPresetId);
            _page.Press(16);
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.moodPresetId));
            Assert.AreEqual(2, _changes);
        }

        [Test]
        public void TitlesAndEmptySlots_AreNotPressable()
        {
            Assert.IsFalse(_page.Press(0));
            Assert.IsFalse(_page.Press(8));
            Assert.AreEqual(0, _changes);
        }

        [Test]
        public void FinishesFromJson_ShortNamesPatternsAndRealEye()
        {
            const string json = "{\"finishes\":[{\"id\":\"floor-concrete-tiles\",\"name\":\"Concrete tiles\",\"surface\":\"floor\",\"baseColor\":\"#7C8896\",\"accentColor\":\"#5A6672\",\"pattern\":\"tiles\"}," +
                "{\"id\":\"wall-olive\",\"name\":\"Olive paint\",\"surface\":\"wall\",\"baseColor\":\"#5E6B3F\",\"pattern\":\"solid\"}]}";
            var floors = StylePage.Finishes(json, "floor");
            Assert.AreEqual(1, floors.Count);
            Assert.AreEqual("Concrete", floors[0].Name, "no shortName: the first word");
            Assert.AreEqual(new Vector4(1f, 3f, 0.024f, 0f), floors[0].Pattern);
            Assert.AreEqual(Vector4.zero, StylePage.Finishes(json, "wall")[0].Pattern, "solid has no lines");
            Assert.AreEqual("Sh", StylePage.Short("x", "Oak planks", "Sh"), "content's shortName wins");
            Assert.AreEqual("Oak", StylePage.Short("unknown", "Oak planks", null));
            Assert.AreEqual(Glyphs.None, Card(4).Glyph, "the floor's first choice is Plain (one room, mostly virtual): no eye");
            Assert.AreEqual(Glyphs.Eye, Card(16).Glyph, "the mood's Real shows the eye");
            Assert.AreEqual(Glyphs.None, Card(5).Glyph);
        }

        [Test]
        public void Moods_FromContentData_GradientAndShortName()
        {
            const string json = "{\"presets\":[{\"id\":\"evening\",\"name\":\"Evening\",\"shortName\":\"Evening\",\"gradientTop\":\"#F6A25E\",\"gradientBottom\":\"#8E5C9B\",\"windowColor\":\"#FF9F55\"}," +
                "{\"id\":\"afternoon\",\"name\":\"Afternoon\",\"shortName\":\"Day\",\"windowColor\":\"#FFFAF0\"}]}";
            var moods = StylePage.Moods(json);
            ColorUtility.TryParseHtmlString("#F6A25E", out Color orange);
            ColorUtility.TryParseHtmlString("#8E5C9B", out Color violet);
            Assert.AreEqual(orange, moods[0].Colour, "orange at the top");
            Assert.AreEqual(violet, moods[0].Colour2, "violet at the bottom");
            Assert.AreEqual("Day", moods[1].Name, "content's shortName");
            Assert.AreEqual(moods[1].Colour, moods[1].Colour2, "no gradient: a flat disc");
        }

        [Test]
        public void ContentFiles_HaveShortNamesAndGradients()
        {
            var finishes = StylePage.Finishes(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Content/finishes.json").text, "floor");
            Assert.AreEqual("Tiles", finishes.Find(o => o.Id == "floor-concrete-tiles").Name);
            var moods = StylePage.Moods(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Project/Content/mood-presets.json").text);
            Assert.AreEqual(4, moods.Count);
            Assert.AreNotEqual(moods[2].Colour, moods[2].Colour2, "evening is a gradient");
        }

        [Test]
        public void QuadUv_IsOneAtTheTop_SoTheShaderGradientRunsTopToBottom()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
            for (int i = 0; i < mesh.vertexCount; i++)
                Assert.AreEqual(mesh.vertices[i].y > 0f ? 1f : 0f, mesh.uv[i].y, 1e-4f);
            Object.DestroyImmediate(go);
        }

        // Device batch 8: with My zone on "Real" the user still saw white walls (the virtual outside) and was confused.
        [Test]
        public void TheDefault_IsPlainNotReal()
        {
            Assert.AreEqual("Floor", Card(3).Title);
            Assert.AreEqual(StylePage.PlainLabel, Card(4).Title, "outside is always virtual: no Real");
            Assert.AreEqual(Glyphs.None, Card(4).Glyph, "no eye: nothing real shows there");
            Assert.Greater(Card(4).Swatch.a, 0f, "a plain swatch, not a see-through ring");
            Assert.AreEqual("Real", Card(16).Title, "the mood's Real (no mood) is unchanged");
        }

        // The human (8fdbcda): "the whole floor must be one and the same material"; then "merge them into one" for walls too.
        [Test]
        public void TheFloor_IsOneMaterial_FromEitherZone_PlainPutsBothBack_OneUndoStep()
        {
            int steps = 0;
            _page.NoteSteps = n => steps += n;
            Assert.IsTrue(_page.Press(6));  // Tiles
            Assert.AreEqual("tiles", _session.State.myZoneFinish.floorFinishId);
            Assert.AreEqual("tiles", _session.State.outsideFinish.floorFinishId);
            Assert.AreEqual(2, steps, "two session steps, noted as one user change (one Undo)");
            Assert.IsTrue(_page.Press(4));  // Plain / Real
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.myZoneFinish.floorFinishId), "my zone's floor real again");
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.outsideFinish.floorFinishId), "the outside back to the plain shell");
            Assert.IsFalse(OutsideShell.FloorSeam(myFloorDesigned: true), "a designed floor is opaque edge to edge: no fade");
            Assert.IsTrue(OutsideShell.FloorSeam(myFloorDesigned: false), "the fade only where my zone's floor is real");
        }

        [Test]
        public void AnOlderSave_WithDifferentZoneFinishes_TakesTheOutsideOnesForBoth()
        {
            _session.SetFinish(DesignZone.Mine, FinishSurface.Floor, "oak");
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, "tiles");
            _session.SetFinish(DesignZone.Mine, FinishSurface.Wall, "cream");
            Assert.AreEqual(2, StylePage.Unify(_session), "my zone's floor and wall take the outside ones");
            Assert.AreEqual("tiles", _session.State.myZoneFinish.floorFinishId);
            Assert.AreEqual("tiles", _session.State.outsideFinish.floorFinishId);
            Assert.IsTrue(string.IsNullOrEmpty(_session.State.myZoneFinish.wallFinishId), "the outside wall was plain: my zone's wall real");
            Assert.AreEqual(0, StylePage.Unify(_session), "already one room");
        }
    }
}

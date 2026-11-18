using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class DesignSummaryTests
    {
        ICatalog _catalog;
        DesignState _state;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _state = new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a") };
        }

        void Redesign(string anchor, string item) =>
            _state.SetChoice(new AnchorChoice { anchorId = anchor, mode = AnchorMode.Redesign, catalogItemId = item, yScale = 1f, xScale = 1f, zScale = 1f });

        void Place(params string[] items)
        {
            foreach (var item in items)
                _state.placements.Add(new FreePlacement { placementId = "p" + _state.placements.Count, catalogItemId = item });
        }

        string Describe() => DesignSummary.Describe(_state, _catalog);

        [Test]
        public void TheDemoDesign_ReadsLikeACaption()
        {
            Redesign("a-desk", "desk-warm-wood-01");
            Redesign("a-bed", "bed-warm-wood-01");
            Place("rug-cafe-01", "sofa-cafe-01", "coffee-table-warm-wood-01", "plant-cafe-01");
            _state.moodPresetId = "evening";
            Assert.AreEqual("Warm-wood desk and bed in your zone; café lounge outside; evening mood", Describe());
        }

        [Test]
        public void NothingChanged_SaysSo()
        {
            Assert.AreEqual(DesignSummary.Untouched, DesignSummary.Describe(new DesignState(), _catalog));
            Assert.AreEqual(DesignSummary.Untouched, Describe(), "a zone alone is not a change");
            Assert.AreEqual(DesignSummary.Untouched, DesignSummary.Describe(null, _catalog));
        }

        [Test]
        public void KindsComeInNaturalOrder_AndMixedStylesSaySo()
        {
            Redesign("a-closet", "closet-scandi-01");
            Redesign("a-bed", "bed-night-city-01");
            Redesign("a-desk", "desk-warm-wood-01");
            Assert.AreEqual("Mixed-style desk, bed and closet in your zone", Describe());
        }

        [Test]
        public void OutsideThemes_FollowThePieces()
        {
            Place("armchair-scandi-01", "shelf-scandi-01", "lamp-scandi-01");
            Assert.AreEqual("Scandi reading corner outside", Describe());
            _state.placements.Clear();
            Place("plant-warm-wood-01", "lamp-warm-wood-01");
            _state.moodPresetId = "morning";
            Assert.AreEqual("Warm-wood space outside; morning mood", Describe());
        }

        [Test]
        public void TidyingAndFinishes_AreMentioned()
        {
            _state.hideBoxes.Add(new HideBox { hideBoxId = "h1" });
            _state.coverProps.Add(new FreePlacement { placementId = "c1", catalogItemId = "clutter-basket-warm-wood-01" });
            _state.outsideFinish = new ZoneFinish { floorFinishId = "floor-oak-planks", wallFinishId = "" };
            Assert.AreEqual("Your zone kept real, 2 spots tidied; new finishes outside", Describe());
            Redesign("a-desk", "desk-scandi-01");
            Assert.AreEqual("Scandi desk in your zone, 2 spots tidied; new finishes outside", Describe());
        }

        [Test]
        public void UnknownIdsOrNoCatalog_StillGiveASensibleLine()
        {
            Redesign("a-desk", "desk-from-the-future-01");
            Place("unicorn-01", "unicorn-02");
            Assert.AreEqual("1 piece in your zone; 2 pieces outside", Describe());
            Assert.AreEqual("1 piece in your zone; 2 pieces outside", DesignSummary.Describe(_state, null));
        }

        [Test]
        public void LongDesigns_AreClipped()
        {
            Redesign("a-desk", "desk-warm-wood-01");
            for (int i = 0; i < 30; i++) _state.hideBoxes.Add(new HideBox { hideBoxId = "h" + i });
            Place("sofa-night-city-01");
            _state.moodPresetId = "afternoon-with-a-very-long-custom-preset-name-from-content-that-goes-on";
            string text = Describe();
            Assert.LessOrEqual(text.Length, DesignSummary.MaxLength);
            StringAssert.EndsWith("…", text);
        }
    }
}

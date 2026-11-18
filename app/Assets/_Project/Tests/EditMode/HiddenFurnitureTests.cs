using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>
    /// Device bug: in a big room with my zone around the desk only, Add refused a bed or sofa almost everywhere
    /// ("can't place"). The virtual room covers everything outside my zone, so the real furniture there is
    /// hidden and the user sees empty floor. Hidden real furniture no longer blocks Add or AI placement; doors,
    /// windows, the room's walls, my zone and other placed pieces still do.
    /// </summary>
    public class HiddenFurnitureTests
    {
        // My zone: the side-a desk only (x -1.8..-1.2, z 1.55..2.85) with a margin. Side a's bed (x -1.8..-0.85,
        // z 2.95..4.95) and all of side b's furniture are outside it, under the virtual room.
        static readonly List<Vec2> DeskZone = new List<Vec2> { new Vec2(-1.8f, 1.4f), new Vec2(-1.0f, 1.4f), new Vec2(-1.0f, 2.9f), new Vec2(-1.8f, 2.9f) };

        ICatalog _catalog;
        IRoomInfo _room;
        DesignState _state;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = FakeRoomInfo.DormRoom();
            _state = new DesignState { zoneOutline = DeskZone };
        }

        PlacementStatus Check(string id, float x, float z, float yaw) =>
            PlacementRules.Check(_room, _state, _catalog, new FreePlacement { placementId = "p", catalogItemId = id, position = new Vec3(x, 0f, z), yawDegrees = yaw });

        [Test]
        public void ASofaOverHiddenRealFurniture_CanBePlaced()
        {
            Assert.AreEqual(PlacementStatus.Ok, Check("sofa-cafe-01", -1.35f, 3.7f, 90f), "over the real side-a bed");
            Assert.AreEqual(PlacementStatus.Ok, Check("sofa-cafe-01", 1.35f, 3.7f, 90f), "over the real side-b bed");
            Assert.AreEqual(PlacementStatus.Ok, Check("lamp-warm-wood-01", 1.58f, 1.2f, 0f), "inside the real side-b closet");
        }

        [Test]
        public void TheOtherRules_StillHold()
        {
            Assert.AreEqual(PlacementStatus.InMyZone, Check("sofa-cafe-01", -1.35f, 2.5f, 90f), "on the desk in my zone");
            Assert.AreEqual(PlacementStatus.BlocksDoor, Check("lamp-warm-wood-01", 0.9f, 0.3f, 0f));
            Assert.AreEqual(PlacementStatus.OutsideRoom, Check("sofa-cafe-01", 1.7f, 3.7f, 90f));
            _state.placements.Add(new FreePlacement { placementId = "first", catalogItemId = "sofa-cafe-01", position = new Vec3(1.35f, 0f, 3.7f), yawDegrees = 90f });
            Assert.AreEqual(PlacementStatus.Overlaps, Check("armchair-cafe-01", 1.35f, 3.7f, 0f), "placed pieces still collide");
        }

        [Test]
        public void TheAssistantsFreeFloor_IncludesTheFloorUnderHiddenFurniture()
        {
            double Area(IRoomInfo room)
            {
                var request = new AssistantRequest(AssistantTask.FillOutside, "x", room, _state.Clone(), _catalog.Items);
                return (double)JObject.Parse(AssistantRequestJson.Build(request, null, null))["room"]["free"]["area"];
            }
            Assert.AreEqual(Area(TestRooms.Dorm()), Area(_room), 1e-6, "same free floor with or without the hidden furniture");
        }
    }
}

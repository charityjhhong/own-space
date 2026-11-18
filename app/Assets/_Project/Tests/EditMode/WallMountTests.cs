using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>
    /// Wall art could only stand on the floor. Wall pieces now hang flush on a room wall, 0.9–2.0 m up.
    /// Dorm room: side walls at x = ±1.8, front wall z = 0 (doors), back wall z = 5 (windows); my zone is side a.
    /// </summary>
    public class WallMountTests
    {
        const string Art = "wall-art-warm-wood-01"; // 0.8 wide, 0.6 high, 0.03 deep

        ICatalog _catalog;
        IRoomInfo _room;
        DesignSession _session;

        void Setup(params string[] furniture)
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = TestRooms.Dorm(furniture);
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        [SetUp]
        public void SetUp() => Setup(TestRooms.SideA);

        CatalogItem Item(string id) => _catalog.Items.First(i => i.id == id);

        FreePlacement OnWall(string id, float x, float centerY, float z, string placementId = "w")
        {
            Assert.IsTrue(PlacementRules.TrySnapToWall(_room, Item(id), new Vec3(x, centerY, z), out var at, out float yaw));
            return new FreePlacement { placementId = placementId, catalogItemId = id, position = at, yawDegrees = yaw };
        }

        PlacementStatus Check(FreePlacement p) => PlacementRules.Check(_room, _session.State, _catalog, p);

        [Test]
        public void WallPieces_AreKnownByKindOrTag()
        {
            Assert.IsTrue(PlacementRules.IsWallMounted(Item(Art)));
            Assert.IsTrue(PlacementRules.IsWallMounted(new CatalogItem { kind = "mirror" }));
            Assert.IsTrue(PlacementRules.IsWallMounted(new CatalogItem { kind = "clock" }));
            Assert.IsTrue(PlacementRules.IsWallMounted(new CatalogItem { kind = "sign", tags = new List<string> { "wall" } }));
            Assert.IsFalse(PlacementRules.IsWallMounted(Item("lamp-warm-wood-01")));
        }

        [Test]
        public void Snap_PutsItFlushOnTheNearestWall_FacingIn_AtAnAllowedHeight()
        {
            var p = OnWall(Art, 1.6f, 1.6f, 2.5f);
            Assert.AreEqual(1.8f - 0.015f, p.position.x, 1e-4f, "back on the wall: half the depth in");
            Assert.AreEqual(2.5f, p.position.z, 1e-4f);
            Assert.AreEqual(1.6f - 0.3f, p.position.y, 1e-4f, "pivot is the bottom: centre minus half the height");
            Assert.AreEqual(270f, p.yawDegrees, 1e-3f, "faces -x, into the room");
            Assert.AreEqual(PlacementStatus.Ok, Check(p));

            Assert.AreEqual(2.0f - 0.3f, OnWall(Art, 1.6f, 2.6f, 2.5f).position.y, 1e-4f, "clamped to a 2.0 m centre");
            Assert.AreEqual(0.9f - 0.3f, OnWall(Art, 1.6f, 0.1f, 2.5f).position.y, 1e-4f, "clamped to a 0.9 m centre");
            Assert.AreEqual(5f - 0.4f, OnWall(Art, 1.79f, 1.5f, 4.9f).position.z, 1e-4f, "kept clear of the wall's end");
            Assert.IsFalse(PlacementRules.TrySnapToWall(FakeRoomInfo.Empty(), Item(Art), new Vec3(0f, 1.5f, 0f), out _, out _), "no scan, no walls");
        }

        [Test]
        public void OffTheWall_OrTooHighOrLow_IsNotOnWall()
        {
            var p = OnWall(Art, 1.6f, 1.5f, 2.5f);
            var floor = p; floor.position = new Vec3(0.9f, 0f, 2.5f);
            Assert.AreEqual(PlacementStatus.NotOnWall, Check(floor), "wall art on the floor");
            var high = p; high.position = new Vec3(p.position.x, 2.3f - 0.3f, p.position.z);
            Assert.AreEqual(PlacementStatus.NotOnWall, Check(high));
            var turned = p; turned.yawDegrees = 90f;
            Assert.AreEqual(PlacementStatus.NotOnWall, Check(turned), "facing the wall");
            var away = p; away.position = new Vec3(1.6f, p.position.y, 2.5f);
            Assert.AreEqual(PlacementStatus.NotOnWall, Check(away), "not flush");
        }

        [Test]
        public void OverAnOpeningAnotherWallPieceOrTallFurniture_IsRefused()
        {
            Assert.AreEqual(PlacementStatus.BlocksWindow, Check(OnWall(Art, 0.9f, 1.5f, 4.9f)), "over the side-b window");
            Assert.AreEqual(PlacementStatus.BlocksDoor, Check(OnWall(Art, 0.9f, 1.5f, 0.1f)), "over the side-b door");
            Assert.AreEqual(PlacementStatus.InMyZone, Check(OnWall(Art, -1.6f, 1.5f, 2.5f)), "on my zone's wall");

            _session.State.placements.Add(OnWall(Art, 1.6f, 1.5f, 2.5f, "first"));
            Assert.AreEqual(PlacementStatus.Overlaps, Check(OnWall("wall-art-scandi-01", 1.6f, 1.6f, 2.9f)));
            Assert.AreEqual(PlacementStatus.Ok, Check(OnWall("wall-art-scandi-01", 1.6f, 1.6f, 3.4f)), "side by side");

            Setup("b-closet", "b-desk"); // closet 2.0 m tall at z 0.55..1.45; desk 0.75 m at z 1.55..2.85
            Assert.AreEqual(PlacementStatus.Ok, Check(OnWall(Art, 1.6f, 1.5f, 1.0f)), "the real closet outside my zone is hidden");
            Assert.AreEqual(PlacementStatus.Ok, Check(OnWall(Art, 1.6f, 1.5f, 2.2f)), "above the low desk");
        }

        [Test]
        public void Session_HangsWallPiecesOnTheWall_WhereverTheyAreGiven()
        {
            Assert.IsTrue(_session.TryAddPlacement(new FreePlacement { placementId = "a", catalogItemId = Art, position = new Vec3(1.5f, 0f, 3.4f) }, out var status), status.ToString());
            var p = _session.State.placements[0];
            Assert.AreEqual(1.785f, p.position.x, 1e-4f);
            Assert.AreEqual(1.5f - 0.3f, p.position.y, 1e-4f, "given at floor level: the default 1.5 m centre");
            Assert.AreEqual(270f, p.yawDegrees, 1e-3f);

            Assert.IsTrue(_session.TryMovePlacement("a", new Vec3(1.7f, 1.0f, 2.2f), 0f, out status), status.ToString());
            p = _session.State.placements[0];
            Assert.AreEqual(2.2f, p.position.z, 1e-4f);
            Assert.AreEqual(1.0f, p.position.y, 1e-4f, "a given height is kept (centre 1.3 m)");
            Assert.AreEqual(270f, p.yawDegrees, 1e-3f, "the wall sets the turn");
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        [Test]
        public void Assistant_HangsWallArtOnTheNearestFreeWall_AndOffersIt()
        {
            var plan = new AssistantPlan();
            plan.placements.Add(new PlanPlacement { catalogItemId = Art, x = 1.2f, z = 3.3f });
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, _session, _catalog, new[] { "wall-cream" }, new[] { "evening" });
            var outcome = runner.AskAsync(AssistantTask.FillOutside, "gallery", null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(1, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var p = outcome.Placements[0];
            Assert.AreEqual(1.785f, p.position.x, 1e-4f, "on the nearest wall (x = 1.8)");
            Assert.AreEqual(270f, p.yawDegrees, 1e-3f);
            Assert.AreEqual(1.5f, p.position.y + 0.3f, 1e-4f, "centre at 1.5 m");
            Assert.AreEqual(PlacementStatus.Ok, Check(p));

            var request = new AssistantRequest(AssistantTask.FillOutside, "x", _room, _session.State.Clone(), _catalog.Items);
            var ids = Newtonsoft.Json.Linq.JObject.Parse(AssistantRequestJson.Build(request, null, null))["catalog"].Select(t => (string)t);
            CollectionAssert.Contains(ids, Art, "wall art is offered to the model");
        }
    }
}

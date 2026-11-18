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
    /// Device bug: the assistant put a floor lamp at arm's length right in front of the seated user.
    /// Dorm side a is my zone; its desk stands at x -1.8..-1.2, z 1.55..2.85, facing +x (the seat is at x -0.8).
    /// </summary>
    public class ViewComfortTests
    {
        ICatalog _catalog;
        DesignSession _session;
        IRoomInfo _room;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _room = TestRooms.Dorm(TestRooms.SideA);
            _session = new DesignSession(_room, _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        CatalogItem Item(string id) => _catalog.Items.First(i => i.id == id);

        [Test]
        public void TallPiecesNearTheHeadOrInFrontOfTheDesk_BlockTheView()
        {
            var view = ViewComfort.For(_room, FakeRoomInfo.HalfZone("a"), null);
            Assert.AreEqual(-0.8f, view.Head.x, 1e-4f, "the seat in front of the desk");
            Assert.AreEqual(1f, view.Facing.x, 1e-4f);

            var lamp = Item("lamp-warm-wood-01"); // 1.55 m tall
            Assert.IsTrue(view.Blocks(lamp, new Vec3(0.1f, 0f, 2.2f), 0f), "arm's length in front");
            Assert.IsTrue(view.Blocks(lamp, new Vec3(0.0f, 0f, 2.9f), 0f), "its edge is within 1 m of the head");
            Assert.IsFalse(view.Blocks(lamp, new Vec3(0.0f, 0f, 3.3f), 0f), "beside the view");
            Assert.IsFalse(view.Blocks(lamp, new Vec3(1.6f, 0f, 2.2f), 0f), "far across the room");
            Assert.IsFalse(view.Blocks(Item("armchair-cafe-01"), new Vec3(0.3f, 0f, 2.2f), 0f), "low pieces never block");
            Assert.IsFalse(view.Blocks(Item("rug-cafe-01"), new Vec3(0.3f, 0f, 2.2f), 0f));
            Assert.IsFalse(view.Blocks(Item("pendant-cafe-01"), new Vec3(0.3f, 2.0f, 2.2f), 0f), "hangs above eye level");

            var onBed = ViewComfort.For(_room, FakeRoomInfo.HalfZone("a"), new Vec3(-1.3f, 0.6f, 4.0f));
            Assert.AreEqual(4.0f, onBed.Head.y, 1e-4f, "a given head position wins");
            Assert.IsNull(ViewComfort.For(TestRooms.Dorm(), FakeRoomInfo.HalfZone("a"), null), "no desk or bed: no view rule");
        }

        sealed class PlanAssistant : IDesignAssistant
        {
            public AssistantPlan Plan;
            public Task<AssistantPlan> RequestAsync(AssistantRequest r, CancellationToken ct) => Task.FromResult(Plan);
        }

        AssistantOutcome Ask(Vec3? user, params (string id, float x, float z)[] pieces)
        {
            var plan = new AssistantPlan();
            foreach (var (id, x, z) in pieces) plan.placements.Add(new PlanPlacement { catalogItemId = id, x = x, z = z });
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, _session, _catalog, null, null) { UserPosition = user };
            return runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
        }

        static bool AgainstAWallOrCorner(Vec3 p) => p.x >= 1.5f || p.z <= 0.3f || p.z >= 4.7f;

        [Test]
        public void ALampInFrontOfTheUser_MovesToAWall_NotJustAside()
        {
            var outcome = Ask(null, ("lamp-warm-wood-01", 0.1f, 2.2f), ("armchair-cafe-01", 0.5f, 2.2f));
            Assert.AreEqual(2, outcome.Placements.Count, string.Join("; ", outcome.Dropped));
            var view = ViewComfort.For(_room, FakeRoomInfo.HalfZone("a"), null);
            var lamp = outcome.Placements[0];
            Assert.IsFalse(view.Blocks(Item(lamp.catalogItemId), lamp.position, lamp.yawDegrees));
            Assert.IsTrue(AgainstAWallOrCorner(lamp.position), $"lamp at ({lamp.position.x}, {lamp.position.z})");
            Assert.AreEqual(0.5f, outcome.Placements[1].position.x, 1e-4f, "a low armchair may stay in front");
        }

        [Test]
        public void AGivenUserPosition_IsKeptClearToo_AndGridPiecesAvoidIt()
        {
            var user = new Vec3(0.6f, 1.2f, 1.0f);
            var outcome = Ask(user, ("shelf-cafe-01", 0.9f, 1.3f));
            var view = ViewComfort.For(_room, FakeRoomInfo.HalfZone("a"), user);
            var shelf = outcome.Placements.Single();
            Assert.IsFalse(view.Blocks(Item(shelf.catalogItemId), shelf.position, shelf.yawDegrees));
            Assert.IsTrue(AgainstAWallOrCorner(shelf.position));

            // Pieces with no position, laid out on the grid, also stay out of the view.
            var plan = new AssistantPlan();
            for (int i = 0; i < 4; i++) plan.placements.Add(new PlanPlacement { catalogItemId = "plant-cafe-01" });
            var runner = new AssistantRunner(new PlanAssistant { Plan = plan }, _session, _catalog, null, null);
            var grid = runner.AskAsync(AssistantTask.FillOutside, "x", null, CancellationToken.None).GetAwaiter().GetResult();
            var deskView = ViewComfort.For(_room, FakeRoomInfo.HalfZone("a"), null);
            foreach (var p in grid.Placements) Assert.IsFalse(deskView.Blocks(Item(p.catalogItemId), p.position, p.yawDegrees), $"({p.position.x}, {p.position.z})");
        }
    }
}

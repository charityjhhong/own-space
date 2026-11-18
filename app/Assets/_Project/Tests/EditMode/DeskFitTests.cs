using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>
    /// Device bug: "Selected … (Table): 0 design(s) fit", so a real desk could not be restyled at all,
    /// and "ai-3 not placed: OutsideRoom" when a placed piece was turned next to a wall.
    /// </summary>
    public class DeskFitTests
    {
        ICatalog _catalog;

        [SetUp]
        public void SetUp() => _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;

        static FurnitureAnchor Desk(float w, float d, float surface = 0.74f) =>
            new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(0f, surface / 2f, 0f), new Vec3(w, surface, d), 0f), surface);

        List<(CatalogItem item, FitResult fit)> Options(FurnitureAnchor anchor)
        {
            var list = new List<(CatalogItem item, FitResult fit)>();
            CatalogQueries.FittingItems(_catalog, anchor, list);
            return list;
        }

        // Each design covers the box and stays within its 5 cm margins, scaled 0.6..1.8 per axis.
        [TestCase(1.8f, 0.6f)]
        [TestCase(0.8f, 0.5f)]
        [TestCase(1.2f, 0.6f)]
        public void RealDesk_GetsEveryCatalogDesk(float w, float d)
        {
            var anchor = Desk(w, d);
            var options = Options(anchor);
            int desks = _catalog.Items.Count(i => i.role == FurnitureRole.Table && i.AllowedIn("my"));
            Assert.GreaterOrEqual(desks, 3);
            Assert.AreEqual(desks, options.Count, CatalogQueries.Explain(_catalog, anchor));
            foreach (var (item, fit) in options)
            {
                Assert.IsFalse(fit.Stretched, item.id);
                Assert.That(fit.XScale, Is.InRange(FitRule.TableMinScale, FitRule.TableMaxScale), item.id);
                Assert.That(fit.ZScale, Is.InRange(FitRule.TableMinScale, FitRule.TableMaxScale), item.id);
                float along = (fit.Rotated90 ? item.size.z * fit.ZScale : item.size.x * fit.XScale);
                float across = (fit.Rotated90 ? item.size.x * fit.XScale : item.size.z * fit.ZScale);
                Assert.That(along, Is.InRange(w - 0.011f, w + 0.111f), item.id);
                Assert.That(across, Is.InRange(d - 0.011f, d + 0.111f), item.id);
                Assert.AreEqual(1f, fit.YScale, 0.02f, "the desk top stays at the real height");
            }
        }

        [Test]
        public void OtherRoles_AreStillNeverShrunk()
        {
            var bed = new FurnitureAnchor("bed", FurnitureRole.Bed, new OrientedBox(new Vec3(0f, 0.25f, 0f), new Vec3(1.2f, 0.5f, 0.6f), 0f), 0.5f);
            foreach (var item in _catalog.Items.Where(i => i.role == FurnitureRole.Bed))
            {
                var fit = FitRule.Check(bed, item);
                if (fit.Fits) Assert.GreaterOrEqual(System.Math.Min(fit.XScale, fit.ZScale), 1f, item.id);
            }
        }

        // A desk whose top is far lower than any design: nothing really fits, yet three designs are offered.
        [Test]
        public void NothingFits_TheThreeClosestComeStretched_AndCanBeApplied()
        {
            var anchor = Desk(2.6f, 0.45f, 0.55f);
            var options = Options(anchor);
            Assert.AreEqual(CatalogQueries.StretchedOptions, options.Count);
            foreach (var (item, fit) in options)
            {
                Assert.IsTrue(fit.Stretched && fit.Fits, item.id);
                Assert.AreEqual(0.55f / item.usableHeight, fit.YScale, 1e-4f);
            }
            for (int i = 1; i < options.Count; i++)
                Assert.LessOrEqual(FitRule.Distortion(options[i - 1].fit), FitRule.Distortion(options[i].fit), "least distorted first");

            string why = CatalogQueries.Explain(_catalog, anchor);
            StringAssert.Contains("box 2.60 x 0.45 m", why);
            StringAssert.Contains("desk-scandi-01 1.40 x 0.65: HeightOutOfRange", why);

            // The session accepts an offered stretched design and keeps it after a reload check.
            var room = new OneDesk(anchor);
            var session = new DesignSession(room, _catalog);
            session.SetZone(new List<Vec2> { new Vec2(-2f, -1f), new Vec2(2f, -1f), new Vec2(2f, 1f), new Vec2(-2f, 1f) });
            Assert.AreEqual(RedesignOutcome.Applied, session.Redesign("desk", options[0].item, out var applied));
            Assert.IsTrue(applied.Stretched);
            Assert.AreEqual(0, session.Revalidate().Count);
            Assert.AreEqual(options[0].item.id, session.State.ChoiceFor("desk").catalogItemId);
            var notOffered = new CatalogItem { id = "desk-unlisted-01", role = FurnitureRole.Table, size = new Vec3(1.4f, 0.74f, 0.65f), usableHeight = 0.74f, zones = new List<string> { "my" } };
            Assert.AreEqual(RedesignOutcome.DoesNotFit, session.Redesign("desk", notOffered, out _));
        }

        [Test]
        public void TurningAPieceIntoAWall_MovesItBackInsteadOfRefusing()
        {
            var session = new DesignSession(TestRooms.Dorm(TestRooms.SideA), _catalog);
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            // A 1.5 x 0.8 sofa along the side wall, then turned 45 degrees: its corners go through the wall.
            Assert.IsTrue(session.TryAddPlacement(new FreePlacement { placementId = "ai-3", catalogItemId = "sofa-cafe-01", position = new Vec3(1.4f, 0f, 2.5f), yawDegrees = 90f }, out var status), status.ToString());
            Assert.IsTrue(session.TryMovePlacement("ai-3", new Vec3(1.4f, 0f, 2.5f), 135f, out status), status.ToString());
            var p = session.State.placements[0];
            Assert.AreEqual(135f, p.yawDegrees);
            Assert.Less(p.position.x, 1.4f, "moved away from the wall");
            Assert.LessOrEqual(System.Math.Abs(p.position.x - 1.4f) + System.Math.Abs(p.position.z - 2.5f), 2f * PlacementRules.MaxWallNudge);

            // Added past the wall: moved in as well. Too far out to fix: still refused.
            Assert.IsTrue(session.TryAddPlacement(new FreePlacement { placementId = "l", catalogItemId = "lamp-warm-wood-01", position = new Vec3(1.75f, 0f, 3.6f) }, out status), status.ToString());
            Assert.IsFalse(session.TryAddPlacement(new FreePlacement { placementId = "far", catalogItemId = "lamp-warm-wood-01", position = new Vec3(3f, 0f, 3.6f) }, out status));
            Assert.AreEqual(PlacementStatus.OutsideRoom, status);
        }

        sealed class OneDesk : IRoomInfo
        {
            readonly List<FurnitureAnchor> _furniture;
            readonly List<Vec2> _floor = new List<Vec2> { new Vec2(-3f, -3f), new Vec2(3f, -3f), new Vec2(3f, 3f), new Vec2(-3f, 3f) };
            public OneDesk(FurnitureAnchor desk) => _furniture = new List<FurnitureAnchor> { desk };
            public bool HasScan => true;
            public IReadOnlyList<Vec2> FloorOutline => _floor;
            public float FloorY => 0f;
            public float CeilingY => 2.6f;
            public IReadOnlyList<RoomElement> Walls => new RoomElement[0];
            public IReadOnlyList<RoomElement> Windows => new RoomElement[0];
            public IReadOnlyList<RoomElement> Doors => new RoomElement[0];
            public IReadOnlyList<FurnitureAnchor> Furniture => _furniture;
            public event System.Action Changed { add { } remove { } }
        }
    }
}

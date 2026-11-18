using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;
using UnityEngine.TestTools.Constraints;

namespace OwnSpace.Tests
{
    /// <summary>Dorm fake, my zone = side "a" (x &lt;= 0). a-desk: x -1.8..-1.2, z 1.55..2.85, top 0.75. a-bed: x -1.8..-0.85, z 2.95..4.95, top 0.5.</summary>
    public class HideRulesTests
    {
        static readonly Catalog Items = new Catalog(new[]
        {
            new CatalogItem { id = "basket", kind = "clutter-cover", zones = new List<string> { "my", "outside" }, size = new Vec3(0.5f, 0.5f, 0.4f) },
            new CatalogItem { id = "desk", kind = "desk", role = FurnitureRole.Table, zones = new List<string> { "my" }, size = new Vec3(1.2f, 0.74f, 0.6f), usableHeight = 0.74f },
        });

        DesignSession _session;

        static HideBox Box(string id, float x, float z, float w, float h, float d) => new HideBox
        {
            hideBoxId = id, position = new Vec3(x, 0f, z), size = new Vec3(w, h, d), finishId = "",
        };

        static FreePlacement Prop(string id, string item, float x, float z) =>
            new FreePlacement { placementId = id, catalogItemId = item, position = new Vec3(x, 0f, z) };

        HideStatus Check(HideBox b) => _session.CheckHideBox(b);

        [SetUp]
        public void SetUp()
        {
            _session = new DesignSession(FakeRoomInfo.DormRoom(), Items);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
        }

        [Test]
        public void LaundryOnOpenFloorOrUnderTheDesk_IsOk()
        {
            Assert.AreEqual(HideStatus.Ok, Check(Box("h", -0.5f, 2.0f, 0.5f, 0.4f, 0.5f)));
            Assert.AreEqual(HideStatus.Ok, Check(Box("h", -1.5f, 2.2f, 0.4f, 0.4f, 0.4f)), "below the desk top");
        }

        [Test]
        public void ReachingAUsableSurface_IsRefused()
        {
            Assert.AreEqual(HideStatus.CoversUsableSurface, Check(Box("h", -1.5f, 2.2f, 0.4f, 0.9f, 0.4f)), "desk top");
            Assert.AreEqual(HideStatus.CoversUsableSurface, Check(Box("h", -1.3f, 4.0f, 0.5f, 0.6f, 0.5f)), "bed");
        }

        [Test]
        public void CoveringADoorOrWindow_IsRefused()
        {
            Assert.AreEqual(HideStatus.CoversOpening, Check(Box("h", -0.9f, 0.3f, 0.5f, 0.4f, 0.5f)), "door");
            Assert.AreEqual(HideStatus.CoversOpening, Check(Box("h", -0.5f, 4.68f, 0.4f, 1.2f, 0.6f)), "window");
            Assert.AreEqual(HideStatus.Ok, Check(Box("h", -0.5f, 4.68f, 0.4f, 0.5f, 0.6f)), "below the sill");
        }

        [Test]
        public void BadBoxes_AreRefused()
        {
            Assert.AreEqual(HideStatus.OutsideMyZone, Check(Box("h", 0.9f, 2.0f, 0.5f, 0.4f, 0.5f)));
            Assert.AreEqual(HideStatus.TooLarge, Check(Box("h", -0.5f, 2.0f, 2.5f, 0.4f, 0.5f)));
            Assert.AreEqual(HideStatus.InvalidSize, Check(Box("h", -0.5f, 2.0f, 0.5f, float.NaN, 0.5f)));
            Assert.AreEqual(HideStatus.InvalidSize, Check(Box("h", -0.5f, 2.0f, 0.5f, 0f, 0.5f)));
            Assert.AreEqual(HideStatus.OutsideRoom, Check(Box("h", -1.7f, 2.0f, 0.5f, 0.4f, 0.5f)));
        }

        [Test]
        public void AddRemoveAndUndo_HideBoxes()
        {
            Assert.IsTrue(_session.TryAddHideBox(Box("h1", -0.5f, 2.0f, 0.5f, 0.4f, 0.5f), out _));
            Assert.IsFalse(_session.TryAddHideBox(Box("h1", -0.5f, 3.0f, 0.5f, 0.4f, 0.5f), out var dup));
            Assert.AreEqual(HideStatus.DuplicateId, dup);
            Assert.IsFalse(_session.TryAddHideBox(Box("h2", -1.5f, 2.2f, 0.4f, 0.9f, 0.4f), out _));
            Assert.AreEqual(1, _session.State.hideBoxes.Count);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(0, _session.State.hideBoxes.Count);
            Assert.IsTrue(_session.Redo());
            Assert.IsTrue(_session.RemoveHideBox("h1"));
            Assert.IsFalse(_session.RemoveHideBox("h1"));
        }

        [Test]
        public void CoverProps_MustBeClutterCoversInMyZone()
        {
            Assert.IsTrue(_session.TryAddCoverProp(Prop("c1", "basket", -0.5f, 2.0f), out _));
            Assert.AreEqual(HideStatus.NotACoverProp, _session.CheckCoverProp(Prop("c2", "desk", -0.5f, 3.0f)));
            Assert.AreEqual(HideStatus.UnknownProp, _session.CheckCoverProp(Prop("c2", "nope", -0.5f, 3.0f)));
            Assert.AreEqual(HideStatus.OutsideMyZone, _session.CheckCoverProp(Prop("c2", "basket", 0.9f, 3.0f)));
            Assert.AreEqual(HideStatus.UnknownProp, new DesignSession(FakeRoomInfo.DormRoom()).CheckCoverProp(Prop("c2", "basket", -0.5f, 3.0f)));
            Assert.IsTrue(_session.RemoveCoverProp("c1"));
        }

        [Test]
        public void ShrinkingTheZone_DropsHidingOutsideIt()
        {
            _session.TryAddHideBox(Box("h1", -0.5f, 2.0f, 0.5f, 0.4f, 0.5f), out _);
            _session.TryAddCoverProp(Prop("c1", "basket", -0.5f, 3.5f), out _);
            _session.SetZone(new[] { new Vec2(-1.8f, 0f), new Vec2(-0.8f, 0f), new Vec2(-0.8f, 5f), new Vec2(-1.8f, 5f) });
            Assert.AreEqual(0, _session.State.hideBoxes.Count);
            Assert.AreEqual(0, _session.State.coverProps.Count);
            Assert.AreEqual(2, _session.Issues.Count);
            Assert.AreEqual(HideStatus.OutsideMyZone, _session.Issues[0].Hide);
        }

        [Test]
        public void Revalidate_ResetsUnknownCoverFinish()
        {
            var box = Box("h1", -0.5f, 2.0f, 0.5f, 0.4f, 0.5f);
            box.finishId = "floor-lava";
            _session.TryAddHideBox(box, out _);
            _session.KnownFinishIds = new[] { "floor-oak-planks" };
            Assert.AreEqual(DesignIssueKind.UnknownFinish, _session.Revalidate()[0].Kind);
            Assert.AreEqual("", _session.State.hideBoxes[0].finishId);
        }

        [Test]
        public void Json_RoundTripsHidingAndRepairsNulls()
        {
            _session.TryAddHideBox(Box("h1", -0.5f, 2.0f, 0.5f, 0.4f, 0.5f), out _);
            _session.TryAddCoverProp(Prop("c1", "basket", -0.5f, 3.5f), out _);
            Assert.IsTrue(DesignStateJson.TryFromJson(DesignStateJson.ToJson(_session.State), out var back));
            Assert.AreEqual(0.4f, back.hideBoxes[0].size.y, 1e-6f);
            Assert.AreEqual("basket", back.coverProps[0].catalogItemId);
            Assert.IsTrue(DesignStateJson.TryFromJson("{\"version\":2,\"hideBoxes\":null,\"coverProps\":null}", out var repaired));
            Assert.IsNotNull(repaired.hideBoxes);
            Assert.IsNotNull(repaired.coverProps);
        }

        [Test]
        public void CheckHideBox_DoesNotAllocate()
        {
            var box = Box("h", -1.5f, 2.2f, 0.4f, 0.4f, 0.4f);
            for (int i = 0; i < 20; i++) Check(box);
            TestDelegate check = () => Check(box);
            Assert.That(check, NUnit.Framework.Is.Not.AllocatingGCMemory());
        }
    }
}

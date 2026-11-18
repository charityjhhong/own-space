using System;
using System.IO;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    public class DesignSessionTests
    {
        static readonly CatalogItem Desk = Item("desk-x", FurnitureRole.Table, 1.2f, 0.74f, 0.6f, 0.74f);
        static readonly CatalogItem Bed = Item("bed-x", FurnitureRole.Bed, 1.0f, 0.6f, 2.0f, 0.5f);
        static readonly CatalogItem HugeDesk = Item("desk-huge", FurnitureRole.Table, 3.0f, 0.74f, 1.2f, 0.74f); // past even the 0.6x desk shrink
        static readonly CatalogItem SofaItem = Item("sofa-x", FurnitureRole.Couch, 1.8f, 0.8f, 0.9f, 0f, "outside");
        static readonly Catalog Items = new Catalog(new[] { Desk, Bed, HugeDesk, SofaItem });

        DesignSession _session;
        int _changes;

        static CatalogItem Item(string id, FurnitureRole role, float w, float h, float d, float usable, string zone = "my") =>
            new CatalogItem { id = id, role = role, size = new Vec3(w, h, d), usableHeight = usable, zones = new System.Collections.Generic.List<string> { zone } };

        static FreePlacement Sofa(string id, float x, float z) =>
            new FreePlacement { placementId = id, catalogItemId = "sofa-x", position = new Vec3(x, 0f, z) };

        [SetUp]
        public void SetUp()
        {
            // Side b keeps only its closet (z 0.55..1.45), so outside pieces have room along side b.
            _session = new DesignSession(TestRooms.Dorm("a-closet", "a-desk", "a-bed", "b-closet"), Items);
            _session.Changed += () => _changes++;
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
            _changes = 0;
        }

        [Test]
        public void Redesign_InMyZone_StoresItemOrientationAndScale()
        {
            Assert.AreEqual(RedesignOutcome.Applied, _session.Redesign("a-desk", Desk, out var fit));
            Assert.AreEqual(RedesignOutcome.Applied, _session.Redesign("a-bed", Bed, out _));

            var desk = _session.State.ChoiceFor("a-desk");
            Assert.AreEqual(AnchorMode.Redesign, desk.mode);
            Assert.AreEqual("desk-x", desk.catalogItemId);
            Assert.AreEqual(fit.YScale, desk.yScale, 1e-6f);
            Assert.AreEqual(0.75f / 0.74f, desk.yScale, 1e-5f);
            Assert.IsTrue(_session.State.ChoiceFor("a-bed").rotated90);
            Assert.AreEqual(2, _changes);
        }

        [Test]
        public void Redesign_Refusals_ChangeNothing()
        {
            Assert.AreEqual(RedesignOutcome.OutsideMyZone, _session.Redesign("b-closet", Desk, out _));
            Assert.AreEqual(RedesignOutcome.UnknownAnchor, _session.Redesign("nope", Desk, out _));
            Assert.AreEqual(RedesignOutcome.DoesNotFit, _session.Redesign("a-desk", HugeDesk, out var fit));
            Assert.AreEqual(FitStatus.FootprintTooLarge, fit.Status);
            Assert.AreEqual(RedesignOutcome.DoesNotFit, _session.Redesign("a-desk", Bed, out _));
            Assert.AreEqual(0, _changes);
            Assert.IsTrue(_session.Undo(), "only SetUp's SetZone is in the history");
            Assert.IsFalse(_session.CanUndo);
        }

        [Test]
        public void NoZone_MeansNothingCanBeRedesigned()
        {
            var session = new DesignSession(FakeRoomInfo.DormRoom(), Items);
            Assert.AreEqual(RedesignOutcome.OutsideMyZone, session.Redesign("a-desk", Desk, out _));
        }

        [Test]
        public void UndoRedo_WalksHistory()
        {
            _session.Redesign("a-desk", Desk, out _);
            _session.SetMood("evening");
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual("", _session.State.moodPresetId);
            Assert.IsTrue(_session.Undo());
            Assert.AreEqual(AnchorMode.KeepReal, _session.State.ChoiceFor("a-desk").mode);
            Assert.AreEqual(4, _session.State.zoneOutline.Count);
            Assert.IsTrue(_session.Undo()); // the SetZone from SetUp
            Assert.AreEqual(0, _session.State.zoneOutline.Count);
            Assert.IsFalse(_session.Undo());

            Assert.IsTrue(_session.Redo());
            Assert.IsTrue(_session.Redo());
            Assert.AreEqual(AnchorMode.Redesign, _session.State.ChoiceFor("a-desk").mode);
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, "floor-oak");
            Assert.IsFalse(_session.CanRedo, "a new change drops the redo branch");
        }

        [Test]
        public void NoOpChanges_AreNotRecorded()
        {
            _session.SetMood("night");
            _session.SetMood("night");
            Assert.IsFalse(_session.KeepReal("a-desk"));
            Assert.AreEqual(1, _changes);
        }

        [Test]
        public void UndoHistory_IsCapped()
        {
            for (int i = 0; i < DesignSession.MaxUndo + 10; i++) _session.SetMood("m" + i);
            int undone = 0;
            while (_session.Undo()) undone++;
            Assert.AreEqual(DesignSession.MaxUndo, undone);
        }

        [Test]
        public void ShrinkingZone_ReturnsChoicesOutsideItToReal()
        {
            _session.Redesign("a-desk", Desk, out _);
            _session.Redesign("a-bed", Bed, out _);
            // Keep only the front half of side a: closet and desk, not the bed.
            _session.SetZone(new[] { new Vec2(-1.8f, 0f), new Vec2(0f, 0f), new Vec2(0f, 2.9f), new Vec2(-1.8f, 2.9f) });
            Assert.AreEqual(AnchorMode.Redesign, _session.State.ChoiceFor("a-desk").mode);
            Assert.AreEqual(AnchorMode.KeepReal, _session.State.ChoiceFor("a-bed").mode);
        }

        [Test]
        public void Placements_StayOutOfMyZone()
        {
            PlacementStatus Add(string id, float x, float z) { _session.TryAddPlacement(Sofa(id, x, z), out var st); return st; }
            PlacementStatus Move(string id, float x, float z, float yaw) { _session.TryMovePlacement(id, new Vec3(x, 0f, z), yaw, out var st); return st; }
            Assert.AreEqual(PlacementStatus.Ok, Add("p1", 0.9f, 2.5f));
            Assert.AreEqual(PlacementStatus.DuplicateId, Add("p1", 0.9f, 4.0f));
            Assert.AreEqual(PlacementStatus.InMyZone, Add("p2", 0.5f, 2.5f));
            Assert.AreEqual(PlacementStatus.InMyZone, Move("p1", 0.5f, 2.5f, 0f));
            Assert.AreEqual(PlacementStatus.UnknownPlacement, Move("p9", 0.9f, 2.5f, 0f));
            Assert.AreEqual(PlacementStatus.Ok, Move("p1", 1.1f, 3.0f, 90f));
            Assert.AreEqual(3.0f, _session.State.placements[0].position.z, 1e-6f);
            Assert.IsTrue(_session.RemovePlacement("p1"));
            Assert.IsFalse(_session.RemovePlacement("p1"));
            Assert.AreEqual(3, _changes);
        }

        [Test]
        public void Finishes_ArePerZoneAndUndoable()
        {
            _session.SetFinish(DesignZone.Mine, FinishSurface.Wall, "wall-plaster");
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, "floor-oak");
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, "floor-oak");
            Assert.AreEqual(2, _changes);
            Assert.AreEqual("wall-plaster", _session.State.myZoneFinish.wallFinishId);
            Assert.AreEqual("", _session.State.myZoneFinish.floorFinishId);
            Assert.AreEqual("floor-oak", _session.State.outsideFinish.floorFinishId);
            _session.Undo();
            Assert.AreEqual("", _session.State.outsideFinish.floorFinishId);
            Assert.AreEqual("wall-plaster", _session.State.myZoneFinish.wallFinishId);
        }

        [Test]
        public void SetZone_RemovesPiecesNowInsideIt()
        {
            _session.AddPlacement(Sofa("p1", 0.9f, 2.5f));
            _session.SetZone(new[] { new Vec2(-1.8f, 0f), new Vec2(1.0f, 0f), new Vec2(1.0f, 5f), new Vec2(-1.8f, 5f) });
            Assert.AreEqual(0, _session.State.placements.Count);
            Assert.AreEqual(1, _session.Issues.Count);
            Assert.AreEqual(PlacementStatus.InMyZone, _session.Issues[0].Placement);
            _session.Undo();
            Assert.AreEqual(1, _session.State.placements.Count);
        }

        [Test]
        public void Load_RevalidatesChoicesAndPieces()
        {
            var saved = new DesignState { zoneOutline = FakeRoomInfo.HalfZone("a") };
            void Choose(string anchor, string item) =>
                saved.SetChoice(new AnchorChoice { anchorId = anchor, mode = AnchorMode.Redesign, catalogItemId = item, yScale = 7f });
            Choose("a-desk", "desk-x");      // fits: kept, Y scale refreshed
            Choose("a-bed", "gone-item");    // dropped: UnknownItem
            Choose("b-closet", "desk-x");    // dropped: AnchorOutsideZone
            Choose("a-closet", "desk-huge"); // dropped: NoLongerFits
            Choose("ghost", "desk-x");       // kept, flagged: MissingAnchor
            saved.placements.Add(Sofa("p1", 0.9f, 2.5f));
            saved.placements.Add(Sofa("p2", 0.9f, 2.6f)); // overlaps p1: dropped
            saved.placements.Add(Sofa("p3", 0.5f, 2.5f)); // in my zone: dropped

            string path = Path.Combine(Path.GetTempPath(), "ownspace-session-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                DesignStateJson.SaveToFile(path, saved);
                Assert.IsTrue(_session.Load(path));
            }
            finally
            {
                File.Delete(path);
            }

            var s = _session.State;
            Assert.AreEqual(0.75f / 0.74f, s.ChoiceFor("a-desk").yScale, 1e-5f);
            Assert.AreEqual(AnchorMode.Redesign, s.ChoiceFor("ghost").mode);
            Assert.AreEqual(2, s.anchorChoices.Count);
            Assert.AreEqual(1, s.placements.Count);
            Assert.AreEqual("p1", s.placements[0].placementId);

            var kinds = new System.Collections.Generic.Dictionary<string, DesignIssue>();
            foreach (var issue in _session.Issues) kinds[issue.Id] = issue;
            Assert.AreEqual(6, kinds.Count);
            Assert.AreEqual(DesignIssueKind.UnknownItem, kinds["a-bed"].Kind);
            Assert.AreEqual(DesignIssueKind.AnchorOutsideZone, kinds["b-closet"].Kind);
            Assert.AreEqual(DesignIssueKind.NoLongerFits, kinds["a-closet"].Kind);
            Assert.AreEqual(DesignIssueKind.MissingAnchor, kinds["ghost"].Kind);
            Assert.IsFalse(kinds["ghost"].Dropped);
            Assert.AreEqual(PlacementStatus.Overlaps, kinds["p2"].Placement);
            Assert.AreEqual(PlacementStatus.InMyZone, kinds["p3"].Placement);
        }

        [Test]
        public void Revalidate_AfterCatalogChange_DropsAndIsUndoable()
        {
            _session.Redesign("a-desk", Desk, out _);
            // desk-x became a bed: no longer a desk design, so not even a stretched option.
            var shrunk = new Catalog(new[] { Item("desk-x", FurnitureRole.Bed, 1.2f, 0.4f, 0.6f, 0.4f) });
            var later = new DesignSession(FakeRoomInfo.DormRoom(), shrunk, _session.State.Clone());
            Assert.AreEqual(DesignIssueKind.NoLongerFits, later.Revalidate()[0].Kind);
            Assert.AreEqual(AnchorMode.KeepReal, later.State.ChoiceFor("a-desk").mode);
            Assert.IsTrue(later.Undo());
            Assert.AreEqual(AnchorMode.Redesign, later.State.ChoiceFor("a-desk").mode);
            Assert.AreEqual(0, new DesignSession(FakeRoomInfo.DormRoom(), Items, _session.State.Clone()).Revalidate().Count);
        }

        [Test]
        public void P1CallPattern_WithoutCatalog_StillWorks()
        {
            // The calls xr's P1 stack makes (#46, #47, #59), plus the P1 placement and finish setters.
            var room = FakeRoomInfo.DormRoom();
            var session = new DesignSession(room);
            int changes = 0;
            session.Changed += () => changes++;
            session.SetZone(FakeRoomInfo.HalfZone("a"));
            Assert.IsTrue(session.IsInMyZone(room.Furniture[1]));
            Assert.AreEqual(RedesignOutcome.Applied, session.Redesign("a-desk", Desk, out FitResult _));
            Assert.IsTrue(session.KeepReal("a-desk"));
            Assert.IsTrue(session.Undo());
            Assert.IsTrue(session.AddPlacement(Sofa("p1", 1.0f, 2.5f)), "no catalog: only the zone check");
            Assert.IsFalse(session.AddPlacement(Sofa("p2", -1.0f, 2.5f)));
            Assert.IsTrue(session.MovePlacement("p1", new Vec3(1.1f, 0f, 3f), 0f));
            session.SetFloorFinish("floor-oak-planks");
            Assert.AreEqual("floor-oak-planks", session.State.outsideFinish.floorFinishId);
            Assert.AreEqual(7, changes);

            string path = Path.Combine(Path.GetTempPath(), "ownspace-p1-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                session.Save(path);
                var restored = new DesignSession(room);
                Assert.IsTrue(restored.Load(path));
                Assert.AreEqual("desk-x", restored.State.ChoiceFor("a-desk").catalogItemId, "kept: no catalog to re-fit");
                Assert.AreEqual(1, restored.State.placements.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void KnownIds_ResetUnknownFinishesAndMood()
        {
            var saved = new DesignState
            {
                myZoneFinish = new ZoneFinish { floorFinishId = "floor-lava", wallFinishId = "wall-cream" },
                outsideFinish = new ZoneFinish { floorFinishId = "floor-oak-planks", wallFinishId = "" },
                moodPresetId = "disco",
            };
            var session = new DesignSession(FakeRoomInfo.DormRoom(), Items, saved)
            {
                KnownFinishIds = new System.Collections.Generic.HashSet<string> { "floor-oak-planks", "wall-cream" },
                KnownMoodIds = new[] { "morning", "afternoon", "evening", "night" },
            };
            var issues = session.Revalidate();
            Assert.AreEqual("", session.State.myZoneFinish.floorFinishId);
            Assert.AreEqual("wall-cream", session.State.myZoneFinish.wallFinishId);
            Assert.AreEqual("floor-oak-planks", session.State.outsideFinish.floorFinishId);
            Assert.AreEqual("", session.State.moodPresetId);
            Assert.AreEqual(2, issues.Count);
            Assert.AreEqual(DesignIssueKind.UnknownFinish, issues[0].Kind);
            Assert.AreEqual("floor-lava", issues[0].Id);
            Assert.AreEqual(DesignIssueKind.UnknownMood, issues[1].Kind);

            session.KnownMoodIds = null;
            session.SetMood("disco");
            Assert.AreEqual(0, session.Revalidate().Count, "unset = not checked");
        }

        [Test]
        public void SaveAndLoad_RestoreDesignAndClearHistory()
        {
            string path = Path.Combine(Path.GetTempPath(), "ownspace-session-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                _session.Redesign("a-bed", Bed, out _);
                _session.Save(path);

                var restored = new DesignSession(FakeRoomInfo.DormRoom(), Items);
                Assert.IsTrue(restored.Load(path));
                Assert.IsFalse(restored.CanUndo);
                var bed = restored.State.ChoiceFor("a-bed");
                Assert.AreEqual("bed-x", bed.catalogItemId);
                Assert.IsTrue(bed.rotated90);
                Assert.AreEqual(1f, bed.yScale, 1e-6f);
                Assert.IsFalse(restored.Load(path + ".missing"));
                Assert.AreEqual("bed-x", restored.State.ChoiceFor("a-bed").catalogItemId);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void OldSaveWithoutScale_LoadsWithScaleOne()
        {
            const string json = "{\"version\":1,\"anchorChoices\":[{\"anchorId\":\"a-desk\",\"mode\":\"Redesign\",\"catalogItemId\":\"desk-x\"}]}";
            Assert.IsTrue(DesignStateJson.TryFromJson(json, out var state));
            Assert.AreEqual(1f, state.ChoiceFor("a-desk").yScale);
        }
    }
}

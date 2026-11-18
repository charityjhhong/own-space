using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;
using OwnSpace.Core;

namespace OwnSpace.Tests
{
    /// <summary>Saved spaces: save a room once, rebuild it virtually around you anywhere.</summary>
    public class SavedSpacesTests
    {
        static readonly DateTime Created = new DateTime(2026, 11, 10, 9, 30, 0, DateTimeKind.Utc);

        ICatalog _catalog;
        DesignSession _session;
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _catalog = CatalogLoader.LoadFile(CatalogFitTests.CatalogPath).Catalog;
            _session = new DesignSession(FakeRoomInfo.DormRoom(), _catalog);
            _session.SetZone(FakeRoomInfo.HalfZone("a"));
            Assert.AreEqual(RedesignOutcome.Applied, _session.Redesign("a-bed", _catalog.Items.First(i => i.id == "bed-scandi-01"), out _));
            Assert.IsTrue(_session.TryAddPlacement(new FreePlacement { placementId = "p1", catalogItemId = "plant-cafe-01", position = new Vec3(0.5f, 0f, 2.0f) }, out var st), st.ToString());
            _session.SetFinish(DesignZone.Outside, FinishSurface.Floor, "floor-oak-planks");
            _session.SetMood("evening");
            _dir = Path.Combine(Path.GetTempPath(), "ownspace-spaces-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // The user sits at side a's desk (x -1.8..-1.2, z 1.55..2.85); side a's bed is z 2.95..4.95.
        static readonly Vec3 AtDesk = new Vec3(-0.9f, 1.2f, 2.2f), OnBed = new Vec3(-1.3f, 0.6f, 4.0f);

        RoomSnapshot Capture(string name = "Home desk", Vec3? user = null, IRoomInfo room = null)
        {
            Assert.IsTrue(RoomSnapshot.TryCapture(room ?? FakeRoomInfo.DormRoom(), _session.State, name, Created, user ?? AtDesk, out var s, out var error), error);
            return s;
        }

        [Test]
        public void Capture_CopiesTheRoomAndDesign_AndRoundTripsAsJson()
        {
            var s = Capture();
            Assert.AreEqual(4, s.floor.Count);
            Assert.AreEqual(6, s.furniture.Count);
            Assert.AreEqual(2, s.doors.Count);
            Assert.AreEqual("a-desk", s.anchorId);
            Assert.AreEqual(FurnitureRole.Table, s.anchorRole);
            Assert.AreEqual(-1.5f, s.anchorPoint.x, 1e-4f);
            Assert.AreEqual(2.2f, s.anchorPoint.z, 1e-4f);
            Assert.AreEqual("2026-11-10T09:30:00.0000000Z", s.createdUtc);

            string json = RoomSnapshotJson.ToJson(s);
            Assert.IsTrue(RoomSnapshotJson.TryFromJson(json, out var back, out var error), error);
            Assert.AreEqual(json, RoomSnapshotJson.ToJson(back));
            Assert.AreEqual(DesignStateJson.ToJson(_session.State), DesignStateJson.ToJson(back.design));
            Assert.AreEqual(0.75f, back.furniture.First(f => f.id == "a-desk").usableHeight, 1e-5f);
            Assert.AreEqual(FurnitureRole.Bed, back.furniture.First(f => f.id == "a-bed").role);

            var zoneAnchored = Capture(room: TestRooms.Dorm()); // no desk or bed in this room
            Assert.AreEqual("", zoneAnchored.anchorId);
            Assert.AreEqual(FurnitureRole.Other, zoneAnchored.anchorRole);
            Assert.AreEqual(-0.9f, zoneAnchored.anchorPoint.x, 1e-4f, "the zone's centre");
        }

        [Test]
        public void NoScanBrokenNewerOrHugeSnapshots_AreRefused()
        {
            Assert.IsFalse(RoomSnapshot.TryCapture(FakeRoomInfo.Empty(), _session.State, "x", Created, AtDesk, out var none, out var error));
            Assert.IsNull(none);
            Assert.AreEqual("no room scan", error);

            string json = RoomSnapshotJson.ToJson(Capture());
            Assert.IsFalse(RoomSnapshotJson.TryFromJson(json.Replace("\"version\":1", "\"version\":99"), out _, out error));
            StringAssert.Contains("version", error);
            Assert.IsFalse(RoomSnapshotJson.TryFromJson(json.Substring(0, json.Length / 2), out _, out _));
            Assert.IsFalse(RoomSnapshotJson.TryFromJson("", out _, out _));
            Assert.IsFalse(RoomSnapshotJson.TryFromJson(json + new string(' ', RoomSnapshot.MaxJsonChars), out _, out error));
            Assert.AreEqual("room too big to save", error);
            var twoPoints = Newtonsoft.Json.Linq.JObject.Parse(json);
            ((Newtonsoft.Json.Linq.JArray)twoPoints["floor"]).RemoveAt(0);
            ((Newtonsoft.Json.Linq.JArray)twoPoints["floor"]).RemoveAt(0);
            Assert.IsFalse(RoomSnapshotJson.TryFromJson(twoPoints.ToString(), out _, out error));
            Assert.AreEqual("no floor outline", error);
        }

        // The saved desk lands on a real hotel desk at (5, -2), turned 30 degrees, floor 0.1 m higher.
        [Test]
        public void Playback_PutsTheAnchorDeskOnTheRealDesk_AndKeepsTheRoomRigid()
        {
            var s = Capture();
            var pose = RoomPose.AnchorTo(s, new Vec3(5f, 0.1f, -2f), 30f, 0.1f);
            var room = new SnapshotRoomInfo(s, pose);
            var desk = room.Furniture.First(f => f.Id == "a-desk");
            Assert.AreEqual(5f, desk.Box.center.x, 1e-4f);
            Assert.AreEqual(-2f, desk.Box.center.z, 1e-4f);
            Assert.AreEqual(30f, desk.Box.yawDegrees, 1e-3f);
            Assert.AreEqual(0.1f, room.FloorY, 1e-5f);
            Assert.AreEqual(0.1f + FakeRoomInfo.Ceiling, room.CeilingY, 1e-5f);
            Assert.AreEqual(0.1f + 0.375f, desk.Box.center.y, 1e-5f, "heights move with the floor");
            Assert.AreEqual(0.75f, desk.UsableHeight.Value, 1e-5f, "usable height is relative to the floor");

            var src = FakeRoomInfo.DormRoom();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Assert.AreEqual(Len(src.FloorOutline[i], src.FloorOutline[j]), Len(room.FloorOutline[i], room.FloorOutline[j]), 1e-4f, "rigid: walls keep their length");
            }
            var bed = room.Furniture.First(f => f.Id == "a-bed");
            Assert.AreEqual(Len(src.Furniture[1].Box.center.Floor(), src.Furniture[2].Box.center.Floor()),
                Len(desk.Box.center.Floor(), bed.Box.center.Floor()), 1e-4f, "rigid: pieces keep their spacing");
            Assert.AreEqual(2, room.Doors.Count);
            Assert.IsTrue(room.HasScan);
        }

        [Test]
        public void UserOnTheBed_AnchorsByTheBed_AndItLandsOnARealBedFirst()
        {
            var s = Capture(user: OnBed);
            Assert.AreEqual("a-bed", s.anchorId);
            Assert.AreEqual(FurnitureRole.Bed, s.anchorRole);
            Assert.AreEqual(3.95f, s.anchorPoint.z, 1e-4f);
            Assert.IsTrue(RoomSnapshotJson.TryFromJson(RoomSnapshotJson.ToJson(s), out var back, out _));
            Assert.AreEqual(FurnitureRole.Bed, back.anchorRole);

            // A hotel room: a desk next to the user and a bed further away. The saved bed goes on the real bed.
            var hotelDesk = new FurnitureAnchor("desk", FurnitureRole.Table, new OrientedBox(new Vec3(5f, 0.37f, -2f), new Vec3(1.2f, 0.74f, 0.6f), 30f), 0.74f);
            var hotelBed = new FurnitureAnchor("bed", FurnitureRole.Bed, new OrientedBox(new Vec3(8f, 0.25f, 1f), new Vec3(2.0f, 0.5f, 1.4f), 90f), 0.5f);
            var user = new Vec3(5f, 1.2f, -1.4f);
            var pose = RoomPose.AlignTo(s, new[] { hotelDesk, hotelBed }, 0f, user, 0f, out var target);
            Assert.AreSame(hotelBed, target);
            var bed = new SnapshotRoomInfo(s, pose).Furniture.First(f => f.Id == "a-bed");
            Assert.AreEqual(8f, bed.Box.center.x, 1e-4f);
            Assert.AreEqual(1f, bed.Box.center.z, 1e-4f);
            Assert.AreEqual(90f, bed.Box.yawDegrees, 1e-3f);

            // No real bed: the saved bed goes on the desk. A desk anchor prefers the real desk.
            RoomPose.AlignTo(s, new[] { hotelDesk }, 0f, user, 0f, out target);
            Assert.AreSame(hotelDesk, target);
            RoomPose.AlignTo(Capture(), new[] { hotelBed, hotelDesk }, 0f, user, 0f, out target);
            Assert.AreSame(hotelDesk, target);

            // Nothing to align to: in front of the user.
            pose = RoomPose.AlignTo(s, new FurnitureAnchor[0], 0f, user, 45f, out target);
            Assert.IsNull(target);
            bed = new SnapshotRoomInfo(s, pose).Furniture.First(f => f.Id == "a-bed");
            Assert.AreEqual(user.x, bed.Box.center.x, 1e-4f);
            Assert.AreEqual(user.z, bed.Box.center.z, 1e-4f);
        }

        [Test]
        public void Visit_SavedRoomIsVirtual_ExceptTheRealDeskZone()
        {
            var s = Capture();
            var room = new SnapshotRoomInfo(s, RoomPose.AnchorTo(s, new Vec3(5f, 0f, -2f), 30f, 0f));
            var visit = room.VisitState();
            Assert.AreEqual(0, visit.zoneOutline.Count);
            Assert.AreEqual(0, visit.anchorChoices.Count);
            Assert.AreEqual("evening", visit.moodPresetId);
            Assert.AreEqual("floor-oak-planks", visit.outsideFinish.floorFinishId);
            var plant = visit.placements.Single();
            var expected = room.Pose.Apply(new Vec3(0.5f, 0f, 2.0f));
            Assert.AreEqual(expected.x, plant.position.x, 1e-4f);
            Assert.AreEqual(expected.z, plant.position.z, 1e-4f);

            // A 1.6 x 1.2 m real zone around the real desk: the saved desk is replaced by the real one.
            var realZone = new List<Vec2> { new Vec2(4.2f, -2.6f), new Vec2(5.8f, -2.6f), new Vec2(5.8f, -1.4f), new Vec2(4.2f, -1.4f) };
            var pieces = new List<VirtualPiece>();
            room.VirtualFurniture(_catalog, realZone, pieces);
            Assert.IsFalse(pieces.Any(p => p.Anchor.Id == "a-desk"));
            var bed = pieces.Single(p => p.Anchor.Id == "a-bed");
            Assert.IsTrue(bed.Chosen);
            Assert.AreEqual("bed-scandi-01", bed.Item.id);
            Assert.AreEqual(_session.State.ChoiceFor("a-bed").yScale, bed.Fit.YScale, 1e-5f);
            // The human: kept real outside the zone, behind the virtual walls, it was never seen there, and
            // pieces may stand over it; the visit must not draw a default bed on top of them.
            Assert.IsFalse(pieces.Any(p => p.Anchor.Id == "b-bed"), "hidden in the saved room: not drawn");
            var inZone = new List<AnchorChoice>();
            room.RedesignsInZone(room.FloorOutline, inZone);
            Assert.AreEqual("bed-scandi-01", inZone.Single(c => c.anchorId == "a-bed").catalogItemId, "a redesigned piece under the real zone keeps its design");
            Assert.IsFalse(inZone.Any(c => c.anchorId == "b-bed"), "kept real: no choice");
            Assert.IsTrue(SnapshotRoomInfo.SeenInSavedRoom(false, true), "kept real inside the saved zone: seen, drawn as a default");
            Assert.IsTrue(SnapshotRoomInfo.SeenInSavedRoom(true, false), "outside kept real: seen");
            Assert.IsFalse(SnapshotRoomInfo.SeenInSavedRoom(false, false));

            // The visit session: new pieces avoid the virtual furniture, and the real zone stays real.
            var session = new DesignSession(room, _catalog, visit);
            session.SetZone(realZone);
            Assert.AreEqual(1, session.State.placements.Count);
        }

        [Test]
        public void Store_SavesListsRenamesLoadsAndDeletes_UpToTen()
        {
            var store = new SavedSpaces(_dir);
            Assert.AreEqual(0, store.List().Count);
            var home = Capture("Home desk");
            Assert.AreEqual(SaveSpaceResult.Saved, store.Save(home));
            StringAssert.IsMatch("^[a-f0-9]{32}$", home.id);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, home.id + ".json")));

            Assert.IsTrue(store.Rename(home.id, "  Seoul room  "));
            Assert.IsTrue(store.TryLoad(home.id, out var loaded));
            Assert.AreEqual("Seoul room", loaded.name);
            Assert.AreEqual(home.anchorId, loaded.anchorId);

            for (int i = 1; i < SavedSpaces.MaxSpaces; i++) Assert.AreEqual(SaveSpaceResult.Saved, store.Save(Capture("Room " + i)));
            Assert.AreEqual(SavedSpaces.MaxSpaces, store.List().Count);
            Assert.AreEqual(SaveSpaceResult.Full, store.Save(Capture("One too many")));
            Assert.AreEqual(SaveSpaceResult.Saved, store.Save(loaded), "replacing an existing one is fine when full");

            File.WriteAllText(Path.Combine(_dir, "0badfile.json"), "{ not json");
            Assert.AreEqual(SavedSpaces.MaxSpaces, store.List().Count, "a broken file is skipped");
            Assert.IsFalse(store.TryLoad("0badfile", out _));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "0badfile.json.bad")), "kept for a look");

            Assert.IsTrue(store.Delete(home.id));
            Assert.IsFalse(store.Delete(home.id));
            Assert.IsFalse(store.TryLoad("../escape", out _));
            var bad = Capture(); bad.id = "../escape";
            Assert.AreEqual(SaveSpaceResult.Invalid, store.Save(bad));
            Assert.AreEqual(SavedSpaces.MaxSpaces - 1, store.List().Count);
        }

        static float Len(Vec2 a, Vec2 b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }
}

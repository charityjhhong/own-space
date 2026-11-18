using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using OwnSpace.UI;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Saved spaces: the sample room placed around the user, and the Spaces chooser.
    public class SavedSpacesUiTests
    {
        // A saved space anchored to its desk (what "Save this room" makes): the sample room's geometry with that anchor.
        static RoomSnapshot DeskAnchored()
        {
            RoomSnapshot s = SampleRoom.Build();
            SnapshotFurniture desk = s.furniture.Find(f => f.id == SampleRoom.DeskId);
            s.id = "a1"; s.anchorId = desk.id; s.anchorRole = FurnitureRole.Table;
            s.anchorPoint = new Vec3(desk.box.center.x, 0f, desk.box.center.z);
            return s;
        }

        [Test]
        public void SampleRoom_IsAValidSnapshot_WithADeskAndASingleBed_AnchoredToTheSeat()
        {
            RoomSnapshot s = SampleRoom.Build();
            Assert.IsTrue(RoomSnapshotJson.TryFromJson(RoomSnapshotJson.ToJson(s), out RoomSnapshot back, out string error), error);
            Assert.AreEqual(SampleRoom.Name, back.name);
            Assert.AreEqual(FurnitureRole.Other, s.anchorRole, "the seat, not the desk");
            Assert.AreEqual(1, s.furniture.FindAll(f => f.role == FurnitureRole.Table).Count);
            Assert.AreEqual(1, s.furniture.FindAll(f => f.role == FurnitureRole.Bed).Count);
            Assert.AreEqual(1, s.windows.Count);
            Assert.AreEqual(1, s.doors.Count);
            Assert.AreEqual(2.5f, s.ceilingY - s.floorY, 1e-4f);
        }

        // How far a floor point is from a box's footprint (0 inside).
        static float Clear(OrientedBox box, Vector3 p)
        {
            Vector3 local = Quaternion.Euler(0f, -box.yawDegrees, 0f) * new Vector3(p.x - box.center.x, 0f, p.z - box.center.z);
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - box.size.x * 0.5f), dz = Mathf.Max(0f, Mathf.Abs(local.z) - box.size.z * 0.5f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // Device, build 9bfbd72 (shot 202311, "anchor Table at 0.56,2.35, yaw -174"): a wall cut through the real desk beside
        // the seat and another stood right behind the laptop. The seat now has clear floor all round, whatever way the user faces.
        [Test]
        public void SampleRoom_SeatsTheUserOnClearFloor_FacingIntoTheRoom_NothingWithinAMetre()
        {
            foreach (float yaw in new[] { 0f, 90f, -174f, 37f })
            {
                var head = new Vector3(0.56f, 1.2f, 2.35f);
                Vector3 view = Quaternion.Euler(15f, yaw, 0f) * Vector3.forward, flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                SpaceVisit.Plan(SampleRoom.Build(), null, 0f, head, view, out SnapshotRoomInfo room, out DesignState seed);
                string at = "facing " + yaw + "°";
                Assert.IsTrue(Zones.Contains(room.FloorOutline, new Vec2(head.x, head.z)), at + ": the user is inside the room");
                foreach (RoomElement wall in room.Walls) Assert.GreaterOrEqual(Clear(wall.Box, head), 1f, at + ": no wall within 1 m of the seat");
                foreach (RoomElement door in room.Doors) Assert.GreaterOrEqual(Clear(door.Box, head), 1f, at + ": no door within 1 m");
                foreach (FurnitureAnchor piece in room.Furniture)
                {
                    Assert.GreaterOrEqual(Clear(piece.Box, head), 1f, at + ": " + piece.Id + " is not within 1 m");
                    Assert.IsFalse(Zones.Contains(seed.zoneOutline, piece), at + ": no virtual piece in the user's real area");
                }
                // The real area: 1 m each way around the seat, squared to the way the user faces. A real desk in front of
                // them (up to 0.9 m ahead, 1.6 m wide) is wholly inside it, so no wall or virtual floor crosses it.
                Assert.AreEqual(4, seed.zoneOutline.Count);
                Vector3 right = Vector3.Cross(Vector3.up, flat);
                foreach (Vector2 corner in new[] { new Vector2(-0.8f, 0.2f), new Vector2(0.8f, 0.2f), new Vector2(0.8f, 0.9f), new Vector2(-0.8f, 0.9f) })
                {
                    Vector3 p = head + right * corner.x + flat * corner.y;
                    Assert.IsTrue(Zones.Contains(seed.zoneOutline, new Vec2(p.x, p.z)), at + ": the real desk's corner " + corner + " is in the real area");
                    Assert.IsTrue(Zones.Contains(room.FloorOutline, new Vec2(p.x, p.z)), at + ": and inside the room");
                }
                foreach (Vec2 z in seed.zoneOutline) Assert.IsTrue(Zones.Contains(room.FloorOutline, z), at + ": the real area is inside the room");
                // Facing into the room: the wall straight ahead is the far one (the window wall), over 2 m away.
                float ahead = float.MaxValue;
                foreach (RoomElement wall in room.Walls)
                    for (float d = 0.5f; d < 6f; d += 0.05f) if (Clear(wall.Box, head + flat * d) <= 0f) { ahead = Mathf.Min(ahead, d); break; }
                Assert.GreaterOrEqual(ahead, 2f, at + ": the user does not face into a near wall");
                Assert.AreEqual(0f, room.FloorY, 1e-4f, "on the tracked floor");
                Assert.AreEqual("floor-oak-planks", seed.outsideFinish.floorFinishId, "the sample's finishes outside the real area");
            }
            Assert.GreaterOrEqual(SampleRoom.SeatClearance, 1f);
        }

        [Test]
        public void Visit_NoScan_PutsASavedDesk55cmAhead_FacingTheUser_OnTheFloor_InsideASmallRealZone()
        {
            var head = new Vector3(2f, 1.2f, -1f);
            Vector3 view = Quaternion.Euler(10f, 90f, 0f) * Vector3.forward; // facing +x, looking a little down
            SpaceVisit.Plan(DeskAnchored(), null, 0f, head, view, out SnapshotRoomInfo room, out DesignState seed);
            FurnitureAnchor desk = room.Furniture[0].Role == FurnitureRole.Table ? room.Furniture[0] : room.Furniture[1];
            Assert.AreEqual(head.x + SpaceVisit.AheadOfUser, desk.Box.center.x, 0.02f, "the desk 0.55 m ahead");
            Assert.AreEqual(head.z, desk.Box.center.z, 0.02f);
            Assert.AreEqual(0f, room.FloorY, 1e-4f, "on the tracked floor");
            Assert.IsTrue(Zones.Contains(seed.zoneOutline, desk), "the real desk area stays real");
            Assert.IsTrue(Zones.Contains(room.FloorOutline, new Vec2(head.x, head.z)), "the user is inside the saved room");
        }

        // The human: "everything except my own spot (the desk or bed) must be virtual, but real and virtual are mixed".
        [Test]
        public void Visit_ThePassthroughHolesAreExactlyTheAnchorZone()
        {
            var head = new Vector3(0f, 1.2f, 0f);
            SpaceVisit.Plan(DeskAnchored(), null, 0f, head, Vector3.forward, out SnapshotRoomInfo room, out DesignState seed);
            Assert.IsFalse(OutsideShell.OpeningsArePassthrough(visiting: true), "the space's windows and doors are not holes");
            Assert.IsTrue(OutsideShell.OpeningsArePassthrough(visiting: false), "in the real room they still are");
            int holes = 0;
            foreach (FurnitureAnchor a in room.Furniture)
            {
                bool hole = OutsideShell.FurnitureIsHole(Zones.Contains(seed.zoneOutline, a), seed.ChoiceFor(a.Id).mode);
                if (hole) { holes++; Assert.AreEqual(SampleRoom.DeskId, a.Id, "only the desk the user sits at"); }
            }
            Assert.AreEqual(1, holes);
            // The sample room: the seat is on open floor, so none of its pieces is a hole.
            SpaceVisit.Plan(SampleRoom.Build(), null, 0f, head, Vector3.forward, out room, out seed);
            foreach (FurnitureAnchor a in room.Furniture)
                Assert.IsFalse(OutsideShell.FurnitureIsHole(Zones.Contains(seed.zoneOutline, a), seed.ChoiceFor(a.Id).mode), a.Id + " is virtual");
        }

        [Test]
        public void Visit_WithARealDesk_LandsTheSavedDeskOnIt()
        {
            var real = new FurnitureAnchor("real-desk", FurnitureRole.Table, new OrientedBox(new Vec3(1f, 0.37f, 3f), new Vec3(1.4f, 0.74f, 0.7f), 30f));
            SpaceVisit.Plan(DeskAnchored(), new[] { real }, 0.02f, new Vector3(1f, 1.2f, 2.4f), Vector3.forward, out SnapshotRoomInfo room, out _);
            FurnitureAnchor desk = room.Furniture[0].Role == FurnitureRole.Table ? room.Furniture[0] : room.Furniture[1];
            Assert.AreEqual(1f, desk.Box.center.x, 0.02f);
            Assert.AreEqual(3f, desk.Box.center.z, 0.02f);
            Assert.AreEqual(30f, desk.Box.yawDegrees, 0.5f);
            Assert.AreEqual(0.02f, room.FloorY, 1e-4f);
        }

        [Test]
        public void Chooser_ThisRoomThenSaved_SampleOnlyWithoutAScan_CurrentMarked()
        {
            var page = new SpacesPage();
            string went = null;
            page.Go = id => { went = id; return true; };
            page.Refresh(true, new List<(string, string, string)> { ("a1", "Home", "Nov 9"), ("b2", "Hotel", "") }, "b2");
            CardInfo C(int i) { var c = new CardInfo(); page.Describe(i, ref c); return c; }
            Assert.AreEqual(SpacesPage.Title, C(0).Title);
            Assert.AreEqual(SettingsPage.CloseGlyph, C(1).Title, "a round × on the right (Settings has none now; the chooser keeps it)");
            Assert.AreEqual(C(0).Row, C(1).Row);
            Assert.AreEqual(SpacesPage.ThisRoom, C(2).Title);
            Assert.AreEqual("Home, Nov 9", C(3).Title, "name and date");
            Assert.AreEqual(SpacesPage.MoreLabel, C(4).Title, "a small … beside a saved space");
            Assert.AreEqual("Hotel", C(5).Title);
            Assert.IsTrue(C(5).Selected, "the current space is marked");
            Assert.AreEqual(page.Count - 1, 6, "with a scan the sample room is not listed; no Close pill at the bottom");
            for (int i = 0; i < page.Count; i++) Assert.AreNotEqual(SampleRoom.Name, C(i).Title);
            page.Refresh(false, null, SpacesPage.ThisRoomId, offerSample: true);
            Assert.AreEqual(SampleRoom.Name, C(2).Title, "no usable scan: no 'This room', and the sample room is offered");
            Assert.IsTrue(page.Press(2));
            Assert.AreEqual(SpacesPage.SampleId, went);
            page.Refresh(false, null, SpacesPage.SampleId);
            Assert.AreEqual(SpacesPage.ThisRoom, C(2).Title, "in the sample room: the way back");
            Assert.AreEqual(SampleRoom.Name, C(3).Title, "and the current space is listed");
            Assert.IsTrue(C(3).Selected);

            var settings = new SettingsPage { OpenSpaces = () => went = "opened" };
            for (int i = 0; i < settings.Count; i++)
            {
                var c = new CardInfo(); settings.Describe(i, ref c);
                if (c.Title == SettingsPage.SpacesLabel) { Assert.IsTrue(settings.Press(i)); break; }
            }
            Assert.AreEqual("opened", went, "Settings › My space › Spaces");
        }
    

        static int Find(SpacesPage page, string title)
        {
            for (int i = 0; i < page.Count; i++) { var c = new CardInfo(); page.Describe(i, ref c); if (c.Title == title) return i; }
            return -1;
        }

        [Test]
        public void Save_QuickNameChips_TypeAName_AndTheMenuRenamesOrDeletesAfterAsking()
        {
            var page = new SpacesPage();
            string saved = null, renamed = null, deleted = null; bool typed = false;
            page.Save = n => { saved = n; return true; };
            page.TypeName = () => typed = true;
            page.Rename = (id, n) => { renamed = id + ":" + n; return true; };
            page.Delete = id => { deleted = id; return true; };
            page.Refresh(true, new List<(string, string, string)> { ("h1", "Home", "Nov 9") }, SpacesPage.ThisRoomId);
            Assert.IsTrue(page.Press(Find(page, SpacesPage.SaveLabel)), "Save this room, in the real room");
            Assert.IsTrue(page.Saving);
            foreach (string q in SpacesPage.QuickNames) Assert.GreaterOrEqual(Find(page, q), 0, q);
            Assert.IsTrue(page.Press(Find(page, "Dorm")));
            Assert.AreEqual("Dorm", saved);
            Assert.IsTrue(page.Press(Find(page, SpacesPage.TypeLabel)));
            Assert.IsTrue(typed, "Type a name opens the keyboard");
            page.Press(Find(page, SpacesPage.CancelLabel));

            Assert.IsTrue(page.Press(Find(page, SpacesPage.MoreLabel)));
            Assert.IsTrue(page.Press(Find(page, SpacesPage.RenameLabel)));
            Assert.AreEqual("h1:Home", renamed);
            int del = Find(page, SpacesPage.DeleteLabel);
            var c = new CardInfo(); page.Describe(del, ref c);
            Assert.IsTrue(c.Danger);
            Assert.IsTrue(page.Press(del));
            Assert.IsNull(deleted, "asks first");
            Assert.GreaterOrEqual(Find(page, "Delete Home? This cannot be undone."), 0);
            Assert.IsTrue(page.Press(Find(page, SpacesPage.DeleteLabel)));
            Assert.AreEqual("h1", deleted);

            page.Refresh(true, null, "h1");
            Assert.AreEqual(-1, Find(page, SpacesPage.SaveLabel), "visiting: nothing to save");
            Assert.AreEqual("Nov 11", SpacesPage.ShortDate("2026-11-11T12:00:00Z"));
            Assert.AreEqual("", SpacesPage.ShortDate("not a date"));
        }

        [Test]
        public void Keyboard_TypesAName_ThenTheAiTextComesBack()
        {
            var go = new GameObject("FreeText");
            try
            {
                var request = go.AddComponent<FreeTextRequest>();
                request.OpenForTest();
                foreach (char ch in "a nook") request.PressKey(PanelKeyboard.Find(ch == ' ' ? PanelKeyboard.SpaceLabel : ch.ToString()));
                string named = null;
                request.OpenForName("", n => { named = n; return true; }); // no panel: the keyboard cannot show, the naming still starts
                Assert.IsTrue(request.Naming);
                Assert.AreEqual("", request.Text);
                foreach (char ch in "dorm") request.PressKey(PanelKeyboard.Find(ch.ToString()));
                for (int i = 0; i < 60; i++) request.PressKey(PanelKeyboard.Find("x"));
                Assert.AreEqual(FreeTextRequest.NameMax, request.Text.Length, "a short name");
                request.OpenForTest();
                Assert.IsTrue(request.Send());
                Assert.IsTrue(named.StartsWith("dorm"));
                Assert.IsFalse(request.Naming);
                Assert.AreEqual("a nook", request.Text, "the unsent AI request is kept");
            }
            finally { Object.DestroyImmediate(go); }
        }

        // The human: a saved space's zone and changes come back next visit, moved with the real desk; the zone
        // is fitted again only when the real desk differs.
        [Test]
        public void NextVisit_BringsBackZoneAndPieces_MovedWithTheRealDesk()
        {
            var deskA = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(1f, 0.37f, 3f), new Vec3(1.4f, 0.74f, 0.7f), 30f));
            SpaceVisit.Plan(DeskAnchored(), new[] { deskA }, 0f, new Vector3(1f, 1.2f, 2.4f), Vector3.forward, out SnapshotRoomInfo first, out DesignState firstSeed, out FurnitureAnchor realA);
            Assert.AreSame(deskA, realA);
            var record = SpaceVisit.VisitRecord.Of(first.Pose, realA);
            // What the user left: a hand-drawn zone and one piece, 1 m in front of the desk.
            var left = new DesignState { moodPresetId = "warm" };
            left.zoneOutline.AddRange(new[] { new Vec2(0f, 2f), new Vec2(2f, 2f), new Vec2(2f, 4f), new Vec2(0f, 4f) });
            left.placements.Add(new FreePlacement { placementId = "p", catalogItemId = "sofa-cafe-01", position = new Vec3(1f, 0f, 1.5f), yawDegrees = 45f });

            // Same desk, moved and turned 90° (another day, the room re-scanned): everything follows it.
            var deskB = new FurnitureAnchor("b", FurnitureRole.Table, new OrientedBox(new Vec3(-2f, 0.37f, 0f), new Vec3(1.45f, 0.74f, 0.7f), 120f));
            SpaceVisit.Plan(DeskAnchored(), new[] { deskB }, 0f, new Vector3(-2f, 1.2f, -0.6f), Vector3.forward, out SnapshotRoomInfo second, out DesignState seed, out FurnitureAnchor realB);
            Assert.IsFalse(SpaceVisit.Restore(left, record, second.Pose, realB, seed), "same desk: the zone is kept");
            Assert.AreEqual("warm", seed.moodPresetId);
            Assert.AreEqual(1, seed.placements.Count);
            Assert.AreEqual(4, seed.zoneOutline.Count);
            // Distances to the desk are kept: the piece and each zone corner sit where they were relative to it.
            float Dist(Vec2 a, Vec3 b) => Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.z) * (a.y - b.z));
            var piece = seed.placements[0].position;
            Assert.AreEqual(Dist(new Vec2(1f, 1.5f), deskA.Box.center), Dist(new Vec2(piece.x, piece.z), deskB.Box.center), 0.01f);
            for (int k = 0; k < 4; k++)
                Assert.AreEqual(Dist(left.zoneOutline[k], deskA.Box.center), Dist(seed.zoneOutline[k], deskB.Box.center), 0.01f);
            Assert.AreEqual(Mathf.Repeat(45f + 90f, 360f), seed.placements[0].yawDegrees, 0.01f, "turned with the desk");

            // A different desk (much wider): the changes stay, the zone is fitted again around it.
            var wide = new FurnitureAnchor("c", FurnitureRole.Table, new OrientedBox(new Vec3(-2f, 0.37f, 0f), new Vec3(2.0f, 0.74f, 0.9f), 120f));
            SpaceVisit.Plan(DeskAnchored(), new[] { wide }, 0f, new Vector3(-2f, 1.2f, -0.6f), Vector3.forward, out SnapshotRoomInfo third, out DesignState seed3, out FurnitureAnchor realC);
            var fresh = new List<Vec2>(seed3.zoneOutline);
            Assert.IsTrue(SpaceVisit.Restore(left, record, third.Pose, realC, seed3), "another desk: zone fitted again");
            CollectionAssert.AreEqual(fresh, seed3.zoneOutline);
            Assert.AreEqual(1, seed3.placements.Count, "the changes still come back");
        }

        // The human: "go back and forth many times". Ten round trips between two real desk spots: the zone and
        // pieces come back where they were each time, with no drift and no duplicates.
        [Test]
        public void TenRoundTrips_NothingDriftsOrDoubles()
        {
            var deskA = new FurnitureAnchor("a", FurnitureRole.Table, new OrientedBox(new Vec3(1f, 0.37f, 3f), new Vec3(1.4f, 0.74f, 0.7f), 30f));
            var deskB = new FurnitureAnchor("b", FurnitureRole.Table, new OrientedBox(new Vec3(-2f, 0.37f, 0f), new Vec3(1.4f, 0.74f, 0.7f), 117f));
            SpaceVisit.Plan(DeskAnchored(), new[] { deskA }, 0f, new Vector3(1f, 1.2f, 2.4f), Vector3.forward, out SnapshotRoomInfo room, out DesignState state, out FurnitureAnchor real);
            state.placements.Add(new FreePlacement { placementId = "p", catalogItemId = "sofa-cafe-01", position = new Vec3(1.2f, 0f, 1.4f), yawDegrees = 15f });
            var startPiece = state.placements[0];
            var startZone = new List<Vec2>(state.zoneOutline);
            var record = SpaceVisit.VisitRecord.Of(room.Pose, real);
            for (int trip = 0; trip < 20; trip++)
            {
                FurnitureAnchor desk = trip % 2 == 0 ? deskB : deskA;
                SpaceVisit.Plan(DeskAnchored(), new[] { desk }, 0f, new Vector3(desk.Box.center.x, 1.2f, desk.Box.center.z - 0.6f), Vector3.forward,
                    out SnapshotRoomInfo next, out DesignState seed, out FurnitureAnchor nextReal);
                Assert.IsFalse(SpaceVisit.Restore(state, record, next.Pose, nextReal, seed), "same desk size: zone kept");
                Assert.AreEqual(1, seed.placements.Count, "no duplicates");
                state = seed;
                record = SpaceVisit.VisitRecord.Of(next.Pose, nextReal);
            }
            // Back on desk A after an even number of trips: exactly where it started.
            Assert.AreEqual(startPiece.position.x, state.placements[0].position.x, 0.001f);
            Assert.AreEqual(startPiece.position.z, state.placements[0].position.z, 0.001f);
            Assert.AreEqual(startPiece.yawDegrees, state.placements[0].yawDegrees, 0.01f);
            for (int k = 0; k < startZone.Count; k++)
            {
                Assert.AreEqual(startZone[k].x, state.zoneOutline[k].x, 0.001f);
                Assert.AreEqual(startZone[k].y, state.zoneOutline[k].y, 0.001f);
            }
        }

        [Test]
        public void SameReal_MatchesSize_EitherWayRound_AndNoneWithNone()
        {
            var rec = new SpaceVisit.VisitRecord { hasReal = true, realW = 1.4f, realD = 0.7f };
            FurnitureAnchor Desk(float w, float d) => new FurnitureAnchor("d", FurnitureRole.Table, new OrientedBox(new Vec3(0f, 0.37f, 0f), new Vec3(w, 0.74f, d), 0f));
            Assert.IsTrue(SpaceVisit.SameReal(rec, Desk(1.5f, 0.75f)));
            Assert.IsTrue(SpaceVisit.SameReal(rec, Desk(0.7f, 1.4f)), "measured the other way round");
            Assert.IsFalse(SpaceVisit.SameReal(rec, Desk(1.8f, 0.7f)));
            Assert.IsFalse(SpaceVisit.SameReal(rec, null), "no real desk now");
            Assert.IsTrue(SpaceVisit.SameReal(new SpaceVisit.VisitRecord(), null), "none then, none now");
        }
    }
}

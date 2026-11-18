using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace OwnSpace.MR.Tests
{
    // The human's first study room: "the space is too small, there is nothing I can do in Add".
    public class VirtualExtentTests
    {
        static readonly FurnitureAnchor[] None = new FurnitureAnchor[0];

        static List<Vec2> Rect(float x0, float z0, float x1, float z1) =>
            new List<Vec2> { new Vec2(x0, z0), new Vec2(x1, z0), new Vec2(x1, z1), new Vec2(x0, z1) };

        static void Bounds(List<Vec2> p, out float x0, out float z0, out float x1, out float z1)
        {
            x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
            foreach (Vec2 q in p) { x0 = Mathf.Min(x0, q.x); z0 = Mathf.Min(z0, q.y); x1 = Mathf.Max(x1, q.x); z1 = Mathf.Max(z1, q.y); }
        }

        [Test]
        public void ASmallRoom_GrowsAwayFromMyZone_ToAComfortableSize()
        {
            var room = Rect(0f, 0f, 2.6f, 3f);   // 2.6 x 3 m
            var zone = Rect(0f, 1.6f, 2.6f, 3f); // the desk end, wall to wall
            var result = new List<Vec2>();
            Assert.IsTrue(VirtualExtent.Compute(room, zone, None, 0f, null, result, out _));
            Bounds(result, out float x0, out float z0, out float x1, out float z1);
            Assert.AreEqual(3f, z1, 1e-4f, "the wall behind my zone stays where it is");
            Assert.AreEqual(1.6f - VirtualExtent.TargetGap, z0, 1e-4f, "grown away from my zone: 3 m of floor in front of it");
            Assert.GreaterOrEqual(x1 - x0, VirtualExtent.MinSide - 1e-4f, "at least 3 m across");
            Assert.GreaterOrEqual((x1 - x0) * (1.6f - z0), 9f - 1e-3f, "at least about 3 x 3 m outside");
            foreach (Vec2 p in room) Assert.IsTrue(Zones.Contains(result, p) || OnEdge(result, p), "the real room is inside the virtual one");
            Assert.Greater(RoomGeometry.SignedArea(result), 0f, "counter-clockwise, like the scanned outline");
        }

        // Device, #328: Bigger room On in a 4 x 3.1 m room changed nothing (it did not count as small). The human: "when On,
        // always wider".
        [Test]
        public void EveryRoom_GrowsWhenOn_ByAtLeastAMetre_AndAtMostFour()
        {
            var room = Rect(0f, 0f, 4f, 3.1f);
            var zone = Rect(0f, 1.7f, 1.6f, 3.1f); // a desk corner
            var result = new List<Vec2>();
            Assert.IsTrue(VirtualExtent.Compute(room, zone, None, 0f, null, result, out _), "the device's room grows");
            Bounds(result, out float x0, out float z0, out float x1, out float z1);
            float area = (x1 - x0) * (z1 - z0);
            Assert.GreaterOrEqual(area, 4f * 3.1f + 3f - 1e-3f, "visibly wider: at least a metre along a 3 m side");
            foreach (Vec2 p in room) Assert.IsTrue(Zones.Contains(result, p) || OnEdge(result, p), "the real room is inside the virtual one");

            var large = Rect(0f, 0f, 8f, 6f);
            Assert.IsTrue(VirtualExtent.Compute(large, Rect(0f, 4.6f, 1.6f, 6f), None, 0f, null, result, out _), "a large room grows too");
            Bounds(result, out x0, out z0, out x1, out z1);
            Assert.AreEqual(6.4f + VirtualExtent.MinGrow, x1 - 1.6f, 1e-4f, "the side with most room: its gap plus a metre");

            Assert.AreEqual(VirtualExtent.TargetGap, VirtualExtent.Reach(0.5f), 1e-5f, "a small gap reaches 3 m");
            Assert.AreEqual(3.5f, VirtualExtent.Reach(2.5f), 1e-5f, "a 2.5 m gap: a metre more");
            Assert.AreEqual(7f, VirtualExtent.Reach(6f), 1e-5f, "a large gap: a metre more");
            Assert.LessOrEqual(VirtualExtent.Reach(0f) - 0f, VirtualExtent.MaxGrow + 1e-5f, "never more than 4 m");
        }

        [Test]
        public void TheExtent_NeverShrinks_InItsOwnFrame_AndALoadChecksIt()
        {
            var room = Rect(0f, 0f, 2.6f, 3f);
            var saved = new VirtualExtent.Saved { yaw = 0f, x0 = -1f, z0 = -2f, x1 = 3.6f, z1 = 3f };
            var result = new List<Vec2>();
            Assert.IsTrue(VirtualExtent.Compute(room, Rect(0f, 1.6f, 2.6f, 3f), None, 0f, saved, result, out VirtualExtent.Saved now));
            Bounds(result, out float x0, out float z0, out float x1, out _);
            Assert.LessOrEqual(x0, -1f + 1e-4f); Assert.GreaterOrEqual(x1, 3.6f - 1e-4f); Assert.LessOrEqual(z0, -2f + 1e-4f);
            Assert.AreEqual(0f, now.yaw);
            Assert.IsTrue(VirtualExtent.Compute(Rect(0f, 0f, 5f, 4f), Rect(0f, 2.8f, 1.6f, 4f), None, 0f, saved, result, out _));
            Bounds(result, out x0, out z0, out _, out _);
            Assert.LessOrEqual(x0, -1f + 1e-4f, "a saved extent stays inside, whatever the room");
            Assert.IsTrue(VirtualExtent.Compute(Rect(0f, 0f, 5f, 4f), Rect(0f, 2.8f, 1.6f, 4f), None, 20f, saved, result, out VirtualExtent.Saved turned));
            Assert.AreEqual(20f, turned.yaw, "another frame: worked out again in it, the saved rectangle is not re-boxed");

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ownspace-extent-" + System.Guid.NewGuid().ToString("N") + ".json");
            try
            {
                VirtualExtent.Save(path, now);
                VirtualExtent.Saved back = VirtualExtent.Load(path);
                Assert.IsNotNull(back);
                Assert.AreEqual(now.x0, back.x0, 1e-5f); Assert.AreEqual(now.z1, back.z1, 1e-5f);
                System.IO.File.WriteAllText(path, "{\"yaw\":0,\"x0\":\"NaN\",\"z0\":0,\"x1\":1e9,\"z1\":1}");
                Assert.IsNull(VirtualExtent.Load(path), "NaN or huge numbers: ignored");
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Extent"));
            }
            finally { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        }

        // Review of #292: the zone must always be inside, even with no saved extent (after a reset).
        [Test]
        public void MyZone_IsAlwaysInside()
        {
            var room = Rect(0f, 0f, 2.6f, 3f);
            var zone = Rect(-0.5f, 1.6f, 2.6f, 3.4f); // a little past the real room
            var result = new List<Vec2>();
            Assert.IsTrue(VirtualExtent.Compute(room, zone, None, 0f, null, result, out _));
            Bounds(result, out float x0, out _, out _, out float z1);
            Assert.LessOrEqual(x0, -0.5f + 1e-4f); Assert.GreaterOrEqual(z1, 3.4f - 1e-4f);
        }

        [Test]
        public void ItFollowsTheZonesDirection()
        {
            var room = new List<Vec2>();
            foreach (Vec2 p in Rect(0f, 0f, 2.6f, 3f)) { Vector3 r = Quaternion.Euler(0f, 30f, 0f) * new Vector3(p.x, 0f, p.y); room.Add(new Vec2(r.x, r.z)); }
            var zone = new List<Vec2>();
            foreach (Vec2 p in Rect(0f, 1.6f, 2.6f, 3f)) { Vector3 r = Quaternion.Euler(0f, 30f, 0f) * new Vector3(p.x, 0f, p.y); zone.Add(new Vec2(r.x, r.z)); }
            var result = new List<Vec2>();
            Assert.IsTrue(VirtualExtent.Compute(room, zone, None, 30f, null, result, out _));
            Assert.IsTrue(ZoneAlign.IsRect(result));
            Assert.IsTrue(ZoneSetup.RectFrame(result, out _, out _, out _, out float yaw));
            Assert.Less(Mathf.Abs(((Mathf.DeltaAngle(yaw, 30f) % 90f) + 135f) % 90f - 45f), 1e-2f, "parallel to the zone's frame");
        }

        static bool OnEdge(List<Vec2> poly, Vec2 p)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                Vec2 a = poly[i], b = poly[(i + 1) % poly.Count];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (Mathf.Abs(cross) < 1e-3f && p.x >= Mathf.Min(a.x, b.x) - 1e-3f && p.x <= Mathf.Max(a.x, b.x) + 1e-3f
                    && p.y >= Mathf.Min(a.y, b.y) - 1e-3f && p.y <= Mathf.Max(a.y, b.y) + 1e-3f) return true;
            }
            return false;
        }

        // The human (26a0806): "this is exactly the room I want" (the scanned walls), so the extent is off by default.
        // The human: "add an option like 'make bigger' so a small room looks wider": Settings › Room › Bigger room.
        [Test]
        public void TheVirtualRoom_StaysAtTheScannedWalls_UnlessBiggerRoomIsOn()
        {
            Assert.IsFalse(PanelHost.ShowOcclusionSwitch, "the real-occlusion switch is never shown (it flickered)");
            string path = System.IO.Path.Combine(Application.temporaryCachePath, "bigger-room-" + System.Guid.NewGuid().ToString("N") + ".json");
            bool was = VirtualExtent.Enabled;
            try
            {
                Assert.IsFalse(UiPrefs.LoadBiggerRoom(path), "no saved choice: the scanned walls");
                Assert.IsTrue(UiPrefs.SaveBiggerRoom(path, true));
                Assert.IsTrue(UiPrefs.LoadBiggerRoom(path));
                Assert.IsTrue(UiPrefs.SaveBiggerRoom(path, false));
                Assert.IsFalse(UiPrefs.LoadBiggerRoom(path));

                VirtualExtent.SetEnabled(false);
                Assert.IsFalse(VirtualExtent.Enabled);
                VirtualExtent.SetEnabled(true);
                Assert.IsTrue(VirtualExtent.Enabled, "on at once, no restart");
                VirtualExtent.SetEnabled(false);
                Assert.IsFalse(VirtualExtent.Enabled, "and off again");
            }
            finally
            {
                VirtualExtent.SetEnabled(was);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
        }

        [Test]
        public void Settings_HasABiggerRoomSwitch_UnderRoom_ThatFlipsTheSetting()
        {
            bool big = false;
            var page = new SettingsPage { IsBiggerRoom = () => big, SetBiggerRoom = v => big = v };
            int sw = -1, room = -1, appearance = -1;
            for (int i = 0; i < page.Count; i++)
            {
                var c = new OwnSpace.UI.CardInfo(); page.Describe(i, ref c);
                if (c.Title == SettingsPage.SectionSpace) room = i;
                if (c.Title == SettingsPage.SectionAppearance) appearance = i;
                if (c.Title == SettingsPage.BiggerRoomOff && sw < 0) { var t = new OwnSpace.UI.CardInfo(); page.Describe(i - 1, ref t); if (t.Title.StartsWith(SettingsPage.BiggerRoomTitle)) sw = i; }
            }
            Assert.Greater(sw, room, "under Room");
            Assert.Less(sw, appearance, "before Appearance");
            var card = new OwnSpace.UI.CardInfo(); page.Describe(sw, ref card);
            Assert.IsFalse(card.Selected);
            Assert.IsTrue(page.Press(sw));
            Assert.IsTrue(big);
            card = new OwnSpace.UI.CardInfo(); page.Describe(sw, ref card);
            Assert.AreEqual(SettingsPage.BiggerRoomOn, card.Title);
            Assert.IsTrue(card.Selected);
            var plain = new SettingsPage();
            for (int i = 0; i < plain.Count; i++) { var c = new OwnSpace.UI.CardInfo(); plain.Describe(i, ref c); StringAssert.DoesNotStartWith(SettingsPage.BiggerRoomTitle, c.Title ?? ""); }
        }
    }
}

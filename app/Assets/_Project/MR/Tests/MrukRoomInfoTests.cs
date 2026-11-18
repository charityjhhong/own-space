using NUnit.Framework;
using OwnSpace.MR;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    public class MrukRoomInfoTests
    {
        // Device check of PR #275: an empty startup load made MRUK load a random Meta sample room as the user's room.
        [Test]
        public void OnAHeadset_OnlyTheDeviceIsARoomSource()
        {
            var device = Meta.XR.MRUtilityKit.MRUK.SceneDataSource.Device;
            var fallback = Meta.XR.MRUtilityKit.MRUK.SceneDataSource.DeviceWithPrefabFallback;
            Assert.AreEqual(device, MrukRoomInfo.DataSourceFor(false, fallback));
            Assert.AreEqual(device, MrukRoomInfo.DataSourceFor(false, Meta.XR.MRUtilityKit.MRUK.SceneDataSource.Prefab));
            Assert.AreEqual(fallback, MrukRoomInfo.DataSourceFor(true, device), "the Editor keeps the sample rooms");
            Assert.IsFalse(MrukRoomInfo.Usable(false, false), "a room that is not from the device is never used on a headset");
            Assert.IsTrue(MrukRoomInfo.Usable(false, true));
            Assert.IsTrue(MrukRoomInfo.Usable(true, false));
        }

        [Test]
        public void MainScene_LoadsTheRoomFromTheDeviceOnly()
        {
            string scene = System.IO.File.ReadAllText("Assets/_Project/Scenes/Main.unity");
            StringAssert.Contains("    DataSource: 0\n", scene.Replace("\r\n", "\n"), "run OwnSpace.MR.Editor.MrukDataSourceSetup.Apply");
        }

        // Device run of build 0dad52e: all five tries were spent while the headset was asleep; the app stayed on no-scan.
        [Test]
        public void LoadRetry_DoesNotCountWhileTheUserIsAway_AndStartsAgainWhenTheyAreBack()
        {
            Assert.IsTrue(MrukRoomInfo.Present(true, false, true));
            Assert.IsFalse(MrukRoomInfo.Present(false, false, true), "the app lost focus");
            Assert.IsFalse(MrukRoomInfo.Present(true, true, true), "paused (the headset sleeps)");
            Assert.IsFalse(MrukRoomInfo.Present(true, false, false), "the headset is not worn");
            Assert.AreEqual(2, MrukRoomInfo.RetriesAfterAnswer(false, 3), "an answer that came while the user was away is not a try");
            Assert.AreEqual(3, MrukRoomInfo.RetriesAfterAnswer(true, 3));
            Assert.AreEqual(0, MrukRoomInfo.RetriesAfterAnswer(false, 0));
            Assert.AreEqual(0, MrukRoomInfo.RetriesOnPresence(true, false, MrukRoomInfo.MaxLoadRetries), "back: all tries again");
            Assert.AreEqual(4, MrukRoomInfo.RetriesOnPresence(true, true, 4));
            Assert.AreEqual(4, MrukRoomInfo.RetriesOnPresence(false, true, 4));
        }

        // Device, builds X and Y of PR #275: "Room save noscan: new design" was written before the room arrived.
        [Test]
        public void BeforeTheFirstAnswer_AHeadsetAsksAndWaits_ItDoesNotDecideNoScan()
        {
            var ongoing = Meta.XR.MRUtilityKit.MRUK.LoadDeviceResult.DiscoveryOngoing;
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Wait, MrukRoomInfo.WhileUnsettled(false, 0.5f, false, 5f), "too early to ask");
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Ask, MrukRoomInfo.WhileUnsettled(false, 2f, false, 5f));
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Wait, MrukRoomInfo.WhileUnsettled(false, 2f, true, 5f), "one ask at a time");
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Wait, MrukRoomInfo.WhileUnsettled(false, 4f, false, 0.2f), "not every frame");
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Ask, MrukRoomInfo.WhileUnsettled(false, 10f, false, 1.5f), "3 s is not a reason to give up on a headset");
            Assert.AreEqual(MrukRoomInfo.FirstLoad.GiveUp, MrukRoomInfo.WhileUnsettled(false, MrukRoomInfo.MaxWaitSeconds, false, 5f));
            Assert.AreEqual(MrukRoomInfo.FirstLoad.Wait, MrukRoomInfo.WhileUnsettled(true, 1f, false, 5f), "the Editor never asks the device");
            Assert.AreEqual(MrukRoomInfo.FirstLoad.GiveUp, MrukRoomInfo.WhileUnsettled(true, 3.1f, false, 5f));
            Assert.IsFalse(MrukRoomInfo.IsAnswer(ongoing), "still loading: keep waiting");
            Assert.IsTrue(MrukRoomInfo.IsAnswer(Meta.XR.MRUtilityKit.MRUK.LoadDeviceResult.NoRoomsFound));
            Assert.IsTrue(MrukRoomInfo.IsAnswer(Meta.XR.MRUtilityKit.MRUK.LoadDeviceResult.Success));
        }

        [Test]
        public void LoadRetry_OnlyOnAHeadsetWithNoRoom_AFewTimes()
        {
            // inEditor, settled, hasScan, inOtherRoom, visiting, busy, retries, now, retryAt
            Assert.IsTrue(MrukRoomInfo.ShouldRetryLoad(false, true, false, false, false, false, 0, 5f, 0f));
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(true, true, false, false, false, false, 0, 5f, 0f), "not in the Editor");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, false, false, false, false, false, 0, 5f, 0f), "not before the first answer");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, true, false, false, false, 0, 5f, 0f), "not with a room");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, false, true, false, false, 0, 5f, 0f), "not when a room loaded but the user is elsewhere");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, false, false, true, false, 0, 5f, 0f), "not during a visit");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, false, false, false, true, 0, 5f, 0f), "not while a load or rescan runs");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, false, false, false, false, MrukRoomInfo.MaxLoadRetries, 5f, 0f), "it gives up");
            Assert.IsFalse(MrukRoomInfo.ShouldRetryLoad(false, true, false, false, false, false, 1, 5f, 6f), "it waits between tries");
        }

        [Test]
        public void RebuildWithNoRoom_GoesEmptyAndRaisesChanged()
        {
            var go = new GameObject("room");
            try
            {
                var room = go.AddComponent<MrukRoomInfo>();
                var furniture = room.Furniture;
                int changed = 0;
                room.Changed += () => changed++;

                room.Rebuild();

                Assert.IsFalse(room.HasScan);
                Assert.AreEqual(0, room.FloorOutline.Count);
                Assert.AreEqual(0, room.Walls.Count + room.Windows.Count + room.Doors.Count + room.Furniture.Count);
                Assert.AreEqual(0f, room.CeilingY);
                Assert.AreEqual(1, changed);
                Assert.AreSame(furniture, room.Furniture, "Consumers may keep the list; it must stay the same instance.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // Device: two overlapping scans; back from a saved space MRUK's current room had flipped, and the app showed
        // the other scan's empty design instead of the one the user decorated.
        [Test]
        public void Reload_KeepsTheLastRoom_WhileTheHeadIsStillInIt()
        {
            Assert.IsTrue(MrukRoomInfo.KeepLast(true, false, true), "still inside: keep it, whatever MRUK calls current");
            Assert.IsTrue(MrukRoomInfo.KeepLast(true, false, null), "no head yet: keep it");
            Assert.IsFalse(MrukRoomInfo.KeepLast(true, false, false), "walked out: pick again");
            Assert.IsFalse(MrukRoomInfo.KeepLast(true, true, true), "removed (rescan): pick again");
            Assert.IsFalse(MrukRoomInfo.KeepLast(false, false, true), "gone: pick again");
        }
    }
}

using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.MR.Tests
{
    // Review of #292: a space saved from an extended room keeps the real outline (its real walls, windows and doors).
    public class RealRoomViewTests
    {
        [Test]
        public void ASpaceSavedFromAnExtendedRoom_HoldsTheRealOutline()
        {
            var go = new GameObject("Room");
            try
            {
                var room = go.AddComponent<MrukRoomInfo>();
                room.UseForTests(new[] { new OwnSpace.Contracts.Vec2(0f, 0f), new OwnSpace.Contracts.Vec2(2.6f, 0f), new OwnSpace.Contracts.Vec2(2.6f, 3f), new OwnSpace.Contracts.Vec2(0f, 3f) });
                room.Extend(new[] { new OwnSpace.Contracts.Vec2(-1f, -2f), new OwnSpace.Contracts.Vec2(3.6f, -2f), new OwnSpace.Contracts.Vec2(3.6f, 3f), new OwnSpace.Contracts.Vec2(-1f, 3f) });
                Assert.IsTrue(room.Extended);
                Assert.AreEqual(-1f, room.FloorOutline[0].x, 1e-5f, "everything else sees the extended room");
                var view = new RealRoomView(room);
                Assert.AreEqual(4, view.FloorOutline.Count);
                Assert.AreEqual(0f, view.FloorOutline[0].x, 1e-5f, "the saved space gets the real outline");
                Assert.AreEqual(2.6f, view.FloorOutline[1].x, 1e-5f);
                Assert.AreSame(room.Windows, view.Windows);
                room.Extend(null);
                Assert.IsFalse(room.Extended);
                Assert.AreEqual(0f, room.FloorOutline[0].x, 1e-5f, "back to the real walls");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}

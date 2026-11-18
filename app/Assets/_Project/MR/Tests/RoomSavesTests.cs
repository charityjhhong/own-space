using System.Collections.Generic;
using NUnit.Framework;
using OwnSpace.Contracts;

namespace OwnSpace.MR.Tests
{
    // Device day #16: a zone saved in one room came back, misplaced, in another.
    public class RoomSavesTests
    {
        static readonly List<Vec2> Dorm = new List<Vec2> { new Vec2(0f, 0f), new Vec2(3f, 0f), new Vec2(3f, 4f), new Vec2(0f, 4f) };

        [Test]
        public void FileName_KeysTheSaveToTheRoom()
        {
            Assert.AreEqual("design-0a1b.json", RoomSaves.FileName("design.json", "0a1b"));
            Assert.AreEqual("design-noscan.json", RoomSaves.FileName("design.json", RoomSaves.NoScanKey));
        }

        [Test]
        public void ARoomsOwnSave_AlwaysComesBack()
        {
            Assert.IsTrue(RoomSaves.Restore(true, Dorm, new Vec2(10f, 10f)), "even with the user outside its zone");
        }

        [Test]
        public void ASaveNotTiedToThisRoom_ComesBackOnlyAroundTheUser()
        {
            Assert.IsTrue(RoomSaves.Restore(false, Dorm, new Vec2(1.5f, 2f)), "the user stands in that zone: same place");
            Assert.IsFalse(RoomSaves.Restore(false, Dorm, new Vec2(8f, 2f)), "another room: a fresh proposal instead");
            Assert.IsFalse(RoomSaves.Restore(false, new List<Vec2>(), new Vec2(0f, 0f)), "no zone saved");
            Assert.IsFalse(RoomSaves.Restore(false, null, new Vec2(0f, 0f)));
        }
    }
}

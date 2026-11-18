using System.Collections.Generic;
using System.Linq;
using OwnSpace.Contracts;
using OwnSpace.Contracts.Fakes;

namespace OwnSpace.Tests
{
    /// <summary>
    /// The dorm room (<see cref="FakeRoomInfo.DormRoom"/>) with only the real furniture a test names.
    /// (Real furniture outside my zone is hidden by the virtual room and no longer blocks placement.)
    /// </summary>
    static class TestRooms
    {
        public static readonly string[] SideA = { "a-closet", "a-desk", "a-bed" };

        public static IRoomInfo Dorm(params string[] keepFurniture) => new Dorm(keepFurniture);
    }

    sealed class Dorm : IRoomInfo
    {
        readonly FakeRoomInfo _dorm = FakeRoomInfo.DormRoom();
        readonly List<FurnitureAnchor> _furniture;
        public Dorm(string[] keep) => _furniture = _dorm.Furniture.Where(a => keep.Contains(a.Id)).ToList();
        public bool HasScan => true;
        public IReadOnlyList<Vec2> FloorOutline => _dorm.FloorOutline;
        public float FloorY => _dorm.FloorY;
        public float CeilingY => _dorm.CeilingY;
        public IReadOnlyList<RoomElement> Walls => _dorm.Walls;
        public IReadOnlyList<RoomElement> Windows => _dorm.Windows;
        public IReadOnlyList<RoomElement> Doors => _dorm.Doors;
        public IReadOnlyList<FurnitureAnchor> Furniture => _furniture;
        public event System.Action Changed { add { } remove { } }
    }
}

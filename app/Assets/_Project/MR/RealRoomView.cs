using System;
using System.Collections.Generic;
using OwnSpace.Contracts;

namespace OwnSpace.MR
{
    /// <summary>
    /// The real room as scanned, for anything that must not see the extended virtual room (<see cref="VirtualExtent"/>):
    /// a saved space keeps the real outline with its real walls, windows and doors (review of #292). Everything else is the
    /// room's own data.
    /// </summary>
    public sealed class RealRoomView : IRoomInfo
    {
        readonly MrukRoomInfo _room;
        public RealRoomView(MrukRoomInfo room) { _room = room; }

        public bool HasScan => _room.HasScan;
        public IReadOnlyList<Vec2> FloorOutline => _room.RealOutline;
        public float FloorY => _room.FloorY;
        public float CeilingY => _room.CeilingY;
        public IReadOnlyList<RoomElement> Walls => _room.Walls;
        public IReadOnlyList<RoomElement> Windows => _room.Windows;
        public IReadOnlyList<RoomElement> Doors => _room.Doors;
        public IReadOnlyList<FurnitureAnchor> Furniture => _room.Furniture;
        public event Action Changed { add => _room.Changed += value; remove => _room.Changed -= value; }
    }
}

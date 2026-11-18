using OwnSpace.Contracts;
using OwnSpace.Core;

namespace OwnSpace.MR
{
    /// <summary>
    /// The built-in "Sample room" (Saved spaces): a 3.2 × 4 m study-bedroom, 2.5 m ceiling, one window over the desk, one
    /// door, a desk (1.2 × 0.6 × 0.74 m) and a single bed. It is a <see cref="RoomSnapshot"/> like any saved space, played
    /// back through core's <see cref="SnapshotRoomInfo"/>. For people with no Space Setup scan (judges, a hotel desk):
    /// it is offered only then.
    /// <para>Its anchor is the user's seat, not the desk (device, build 9bfbd72: with the sample desk put on the user's
    /// spot, the wall behind that desk stood right behind the real laptop and a side wall cut through the real desk).
    /// <see cref="Seat"/> is a point on the open floor at least <see cref="SeatClearance"/> from every wall, the door,
    /// the desk and the bed; the user sits there facing +z, toward the window wall <see cref="Depth"/> / 2 + 0.3 m away.
    /// The real area around them is a square of <see cref="ZoneHalf"/> each way, so their real desk shows through.</para>
    /// </summary>
    public static class SampleRoom
    {
        public const string Id = "sample", Name = "Sample room", DeskId = "sample-desk", BedId = "sample-bed";
        public const float Width = 3.2f, Depth = 4f, Ceiling = 2.5f;
        /// <summary>Where the user sits (room-local floor x, z), the clear floor kept around it, and half the real area's side.</summary>
        public static readonly Vec2 Seat = new Vec2(-0.45f, -0.3f);
        public const float SeatClearance = 1.05f, ZoneHalf = 1f;
        public static readonly Vec3 DeskSize = new Vec3(1.2f, 0.74f, 0.6f), BedSize = new Vec3(0.9f, 0.5f, 2f);

        public static RoomSnapshot Build()
        {
            float hx = Width * 0.5f, hz = Depth * 0.5f;
            var s = new RoomSnapshot
            {
                id = Id, name = Name, createdUtc = "2026-11-11T00:00:00Z", floorY = 0f, ceilingY = Ceiling,
                anchorId = "", anchorRole = FurnitureRole.Other, // the seat: core poses it on the user, facing their way
            };
            s.floor.Add(new Vec2(-hx, -hz)); s.floor.Add(new Vec2(hx, -hz)); s.floor.Add(new Vec2(hx, hz)); s.floor.Add(new Vec2(-hx, hz));
            s.walls.Add(Element("wall-n", 0f, Ceiling * 0.5f, hz, Width, Ceiling, 0.1f, 0f));
            s.walls.Add(Element("wall-s", 0f, Ceiling * 0.5f, -hz, Width, Ceiling, 0.1f, 180f));
            s.walls.Add(Element("wall-e", hx, Ceiling * 0.5f, 0f, Depth, Ceiling, 0.1f, 90f));
            s.walls.Add(Element("wall-w", -hx, Ceiling * 0.5f, 0f, Depth, Ceiling, 0.1f, 270f));
            s.windows.Add(Element("window", 0f, 1.5f, hz, 1.2f, 1.1f, 0.1f, 0f));      // over the desk
            s.doors.Add(Element("door", -hx, 1f, -1.2f, 0.9f, 2f, 0.1f, 270f));       // the left wall, near the back
            var desk = new Vec3(0f, DeskSize.y * 0.5f, hz - DeskSize.z * 0.5f - 0.05f);
            s.furniture.Add(new SnapshotFurniture { id = DeskId, role = FurnitureRole.Table, box = new OrientedBox(desk, DeskSize, 0f), usableHeight = DeskSize.y });
            s.furniture.Add(new SnapshotFurniture
            {
                id = BedId, role = FurnitureRole.Bed, usableHeight = BedSize.y,
                box = new OrientedBox(new Vec3(hx - BedSize.x * 0.5f - 0.05f, BedSize.y * 0.5f, -hz + BedSize.z * 0.5f + 0.05f), BedSize, 0f),
            });
            s.anchorPoint = new Vec3(Seat.x, 0f, Seat.y);
            s.anchorYaw = 0f;
            // Virtual finishes outside the real desk area, so it reads as a different room.
            s.design.outsideFinish = new ZoneFinish { floorFinishId = "floor-oak-planks", wallFinishId = "wall-cream" };
            return s;
        }

        static SnapshotElement Element(string id, float x, float y, float z, float w, float h, float d, float yaw) =>
            new SnapshotElement { id = id, box = new OrientedBox(new Vec3(x, y, z), new Vec3(w, h, d), yaw) };
    }
}

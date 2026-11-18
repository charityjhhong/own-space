using System;
using System.Collections.Generic;

namespace OwnSpace.Contracts.Fakes
{
    /// <summary>
    /// The demo room from CONCEPT.md section 7: a symmetric two-person dorm, 3.6 m wide (x) and
    /// 5.0 m long (z). Side "a" is x &lt; 0, side "b" is x &gt; 0. Along z each side has, in order:
    /// a door (front wall, z = 0), a closet, a desk, a bed, and a window (back wall, z = 5).
    /// Furniture stands against the side walls and faces the room center.
    /// <see cref="Empty"/> is the no-scan room.
    /// </summary>
    public sealed class FakeRoomInfo : IRoomInfo
    {
        public const float HalfWidth = 1.8f;
        public const float Length = 5.0f;
        public const float Ceiling = 2.6f;

        readonly List<Vec2> _outline = new List<Vec2>();
        readonly List<RoomElement> _walls = new List<RoomElement>();
        readonly List<RoomElement> _windows = new List<RoomElement>();
        readonly List<RoomElement> _doors = new List<RoomElement>();
        readonly List<FurnitureAnchor> _furniture = new List<FurnitureAnchor>();

        FakeRoomInfo(bool hasScan)
        {
            HasScan = hasScan;
            if (!hasScan) return;

            _outline.Add(new Vec2(-HalfWidth, 0f));
            _outline.Add(new Vec2(HalfWidth, 0f));
            _outline.Add(new Vec2(HalfWidth, Length));
            _outline.Add(new Vec2(-HalfWidth, Length));

            float midY = Ceiling / 2f;
            _walls.Add(new RoomElement("wall-front", Box(0f, midY, 0f, 2f * HalfWidth, Ceiling, 0f, 0f)));
            _walls.Add(new RoomElement("wall-back", Box(0f, midY, Length, 2f * HalfWidth, Ceiling, 0f, 180f)));
            _walls.Add(new RoomElement("wall-a", Box(-HalfWidth, midY, Length / 2f, Length, Ceiling, 0f, 90f)));
            _walls.Add(new RoomElement("wall-b", Box(HalfWidth, midY, Length / 2f, Length, Ceiling, 0f, -90f)));

            AddSide("a", -1f);
            AddSide("b", 1f);
        }

        public static FakeRoomInfo DormRoom() => new FakeRoomInfo(true);
        public static FakeRoomInfo Empty() => new FakeRoomInfo(false);

        public bool HasScan { get; }
        public IReadOnlyList<Vec2> FloorOutline => _outline;
        public float FloorY => 0f;
        public float CeilingY => HasScan ? Ceiling : 0f;
        public IReadOnlyList<RoomElement> Walls => _walls;
        public IReadOnlyList<RoomElement> Windows => _windows;
        public IReadOnlyList<RoomElement> Doors => _doors;
        public IReadOnlyList<FurnitureAnchor> Furniture => _furniture;

        // The fake room never changes; explicit accessors avoid an unused-event warning.
        public event Action Changed { add { } remove { } }

        /// <summary>The floor polygon of one side's half of the room (x from the side wall to 0).</summary>
        public static List<Vec2> HalfZone(string side)
        {
            float outer = side == "a" ? -HalfWidth : HalfWidth;
            return new List<Vec2>
            {
                new Vec2(Math.Min(outer, 0f), 0f),
                new Vec2(Math.Max(outer, 0f), 0f),
                new Vec2(Math.Max(outer, 0f), Length),
                new Vec2(Math.Min(outer, 0f), Length),
            };
        }

        void AddSide(string side, float sign)
        {
            // Furniture backs onto the side wall; local +z faces the room center.
            float yaw = sign < 0f ? 90f : -90f;

            _doors.Add(new RoomElement(side + "-door", Box(sign * 0.9f, 1.0f, 0f, 0.9f, 2.0f, 0.05f, 0f)));
            // Typical dorm sizes (w x h x d along the wall): closet 0.9 x 2.0 x 0.6, desk 1.3 x 0.75 x 0.6,
            // single bed 2.0 x 0.5 x 0.95 with the mattress top at 0.5 m. Rows along z do not overlap.
            AddFurniture(side + "-closet", FurnitureRole.Storage, sign, 1.0f, 0.9f, 2.0f, 0.6f, yaw, null);
            AddFurniture(side + "-desk", FurnitureRole.Table, sign, 2.2f, 1.3f, 0.75f, 0.6f, yaw, 0.75f);
            AddFurniture(side + "-bed", FurnitureRole.Bed, sign, 3.95f, 2.0f, 0.5f, 0.95f, yaw, 0.5f);
            _windows.Add(new RoomElement(side + "-window", Box(sign * 0.9f, 1.5f, Length, 1.2f, 1.2f, 0.05f, 180f)));
        }

        void AddFurniture(string id, FurnitureRole role, float sign, float z,
            float width, float height, float depth, float yaw, float? usable)
        {
            float x = sign * (HalfWidth - depth / 2f);
            _furniture.Add(new FurnitureAnchor(id, role, Box(x, height / 2f, z, width, height, depth, yaw), usable));
        }

        static OrientedBox Box(float x, float y, float z, float w, float h, float d, float yaw) =>
            new OrientedBox(new Vec3(x, y, z), new Vec3(w, h, d), yaw);
    }
}

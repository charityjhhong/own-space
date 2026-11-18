using System;
using System.Collections.Generic;

namespace OwnSpace.Contracts
{
    /// <summary>Furniture roles, mirroring the MRUK labels we use. Values are stable; append only.</summary>
    public enum FurnitureRole
    {
        Other = 0,
        Table = 1,
        Bed = 2,
        Storage = 3,
        Couch = 4,
    }

    /// <summary>
    /// A gravity-aligned box. <see cref="size"/> is in the box's local frame:
    /// x = width (along its front), y = height, z = depth. <see cref="yawDegrees"/> turns local
    /// +z (the box's front) around world +Y. MRUK volumes are gravity-aligned, so yaw is enough.
    /// </summary>
    [Serializable]
    public struct OrientedBox
    {
        public Vec3 center;
        public Vec3 size;
        public float yawDegrees;

        public OrientedBox(Vec3 center, Vec3 size, float yawDegrees)
        {
            this.center = center;
            this.size = size;
            this.yawDegrees = yawDegrees;
        }
    }

    /// <summary>A wall, window or door. Windows and doors always stay real.</summary>
    public sealed class RoomElement
    {
        public RoomElement(string id, OrientedBox box)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Box = box;
        }

        public string Id { get; }
        public OrientedBox Box { get; }
    }

    /// <summary>A real piece of furniture detected by the room scan (or drawn by hand).</summary>
    public sealed class FurnitureAnchor
    {
        public FurnitureAnchor(string id, FurnitureRole role, OrientedBox box, float? usableHeight = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Role = role;
            Box = box;
            UsableHeight = usableHeight;
        }

        /// <summary>Stable across launches (MRUK anchor UUID, or a fake id).</summary>
        public string Id { get; }
        public FurnitureRole Role { get; }
        public OrientedBox Box { get; }

        /// <summary>Height above the floor of the surface people use (desk top, mattress), when known.</summary>
        public float? UsableHeight { get; }
    }

    /// <summary>
    /// The real room, read-only. The xr lane implements it from MRUK; <c>FakeRoomInfo</c> is the
    /// demo dorm and the no-scan fallback. With no scan, every list is empty and HasScan is false.
    /// </summary>
    public interface IRoomInfo
    {
        bool HasScan { get; }

        /// <summary>Floor outline on the floor plane (x, z), counter-clockwise seen from above. Empty with no scan.</summary>
        IReadOnlyList<Vec2> FloorOutline { get; }

        float FloorY { get; }
        float CeilingY { get; }

        IReadOnlyList<RoomElement> Walls { get; }
        IReadOnlyList<RoomElement> Windows { get; }
        IReadOnlyList<RoomElement> Doors { get; }
        IReadOnlyList<FurnitureAnchor> Furniture { get; }

        /// <summary>Raised when the room is (re)loaded, e.g. after a scan finishes.</summary>
        event Action Changed;
    }
}

using System;

namespace OwnSpace.Contracts
{
    // Own math structs instead of UnityEngine.Vector3 or System.Numerics.Vector3:
    // Contracts and Core build with noEngineReferences, and System.Numerics types are not
    // [Serializable], so they would not survive save/restore. Fields only, so any JSON
    // serializer writes exactly {x, y, z}. Unity convention: meters, Y up, floor plane is (x, z).

    [Serializable]
    public struct Vec2
    {
        public float x;
        public float y;

        public Vec2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public override string ToString() => $"({x:0.###}, {y:0.###})";
    }

    [Serializable]
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>Projection onto the floor plane: (x, z).</summary>
        public Vec2 Floor() => new Vec2(x, z);

        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }
}

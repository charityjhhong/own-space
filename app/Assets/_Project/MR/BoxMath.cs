using OwnSpace.Contracts;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>Hit tests and corners for <see cref="OrientedBox"/>. Allocation-free.</summary>
    public static class BoxMath
    {
        public static Vector3 Center(OrientedBox box) => new Vector3(box.center.x, box.center.y, box.center.z);
        public static Quaternion Rotation(OrientedBox box) => Quaternion.Euler(0f, box.yawDegrees, 0f);
        static Vector3 Half(OrientedBox box, float pad) =>
            new Vector3(box.size.x * 0.5f + pad, box.size.y * 0.5f + pad, box.size.z * 0.5f + pad);

        /// <summary>True when the point is inside the box grown by <paramref name="pad"/> on every side.</summary>
        public static bool Contains(OrientedBox box, Vector3 point, float pad)
        {
            Vector3 local = Quaternion.Inverse(Rotation(box)) * (point - Center(box));
            Vector3 half = Half(box, pad);
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>Distance along the ray to the box (slab test), or -1 when it misses.</summary>
        public static float Raycast(OrientedBox box, Ray ray)
        {
            Quaternion inv = Quaternion.Inverse(Rotation(box));
            Vector3 o = inv * (ray.origin - Center(box));
            Vector3 d = inv * ray.direction;
            Vector3 half = Half(box, 0f);
            float tMin = 0f, tMax = float.MaxValue;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < 1e-6f)
                {
                    if (Mathf.Abs(o[axis]) > half[axis]) return -1f;
                    continue;
                }
                float t1 = (-half[axis] - o[axis]) / d[axis];
                float t2 = (half[axis] - o[axis]) / d[axis];
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return -1f;
            }
            return tMin;
        }

        // Walks all 12 edges in one strip: bottom loop, up, top loop, then the three remaining verticals.
        static readonly int[] EdgePath = { 0, 1, 2, 3, 0, 4, 5, 6, 7, 4, 5, 1, 2, 6, 7, 3 };
        public static int OutlinePointCount => EdgePath.Length;

        /// <summary>Writes the outline strip of the box into a line renderer (positionCount must be <see cref="OutlinePointCount"/>).</summary>
        public static void WriteOutline(OrientedBox box, LineRenderer line)
        {
            Quaternion rot = Rotation(box);
            Vector3 c = Center(box), h = Half(box, 0.01f);
            for (int i = 0; i < EdgePath.Length; i++)
            {
                int k = EdgePath[i];
                float x = (k == 1 || k == 2 || k == 5 || k == 6) ? h.x : -h.x;
                float z = (k == 2 || k == 3 || k == 6 || k == 7) ? h.z : -h.z;
                float y = k >= 4 ? h.y : -h.y;
                line.SetPosition(i, c + rot * new Vector3(x, y, z));
            }
        }
    }
}

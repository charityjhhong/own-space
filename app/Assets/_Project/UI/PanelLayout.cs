using UnityEngine;

namespace OwnSpace.UI
{
    /// <summary>
    /// Pure layout and hit math for a flat world-space panel. Panel-local space: x right, y up,
    /// z away from the user (the panel's plane is z = 0). Element rects are centre + size, in metres.
    /// Allocation-free; tested without a scene.
    /// </summary>
    public static class PanelLayout
    {
        /// <summary>Centre of cell <paramref name="index"/> in a grid filled left to right, top to bottom from <paramref name="topLeft"/>.</summary>
        public static Vector2 GridCell(int index, int columns, Vector2 cell, Vector2 gap, Vector2 topLeft)
        {
            columns = Mathf.Max(1, columns);
            int row = index / columns, col = index % columns;
            return new Vector2(topLeft.x + col * (cell.x + gap.x) + cell.x * 0.5f,
                               topLeft.y - row * (cell.y + gap.y) - cell.y * 0.5f);
        }

        /// <summary>Centre of tab <paramref name="index"/> of <paramref name="count"/> sharing <paramref name="width"/> along a bar centred on x = 0.</summary>
        public static Vector2 TabCell(int index, int count, float width, float centreY)
        {
            float w = width / Mathf.Max(1, count);
            return new Vector2(-width * 0.5f + w * (index + 0.5f), centreY);
        }

        /// <summary>True when <paramref name="p"/> lies in the rect (centre, size) grown by <paramref name="pad"/>.</summary>
        public static bool Contains(Vector2 centre, Vector2 size, Vector2 p, float pad = 0f) =>
            Mathf.Abs(p.x - centre.x) <= size.x * 0.5f + pad && Mathf.Abs(p.y - centre.y) <= size.y * 0.5f + pad;

        /// <summary>
        /// Where <paramref name="ray"/> crosses the panel plane, in panel-local x/y, and how far along
        /// the ray. False when the ray is parallel to the plane or points away from it.
        /// </summary>
        public static bool RayToPanel(Ray ray, Vector3 panelPosition, Quaternion panelRotation, out Vector2 local, out float distance)
        {
            local = default;
            distance = 0f;
            Vector3 normal = panelRotation * Vector3.forward;
            float denom = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denom) < 1e-5f) return false;
            distance = Vector3.Dot(panelPosition - ray.origin, normal) / denom;
            if (distance < 0f) return false;
            Vector3 p = Quaternion.Inverse(panelRotation) * (ray.GetPoint(distance) - panelPosition);
            local = new Vector2(p.x, p.y);
            return true;
        }

        /// <summary>Panel-local position of a world point (z &lt; 0 = in front of the panel).</summary>
        public static Vector3 ToLocal(Vector3 world, Vector3 panelPosition, Quaternion panelRotation) =>
            Quaternion.Inverse(panelRotation) * (world - panelPosition);
    }
}

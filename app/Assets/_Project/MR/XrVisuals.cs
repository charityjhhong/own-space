using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>Placeholder visuals built from Unity primitives with one shared URP Unlit material.</summary>
    public static class XrVisuals
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static MaterialPropertyBlock _block;

        public static readonly Color Idle = new Color(0.85f, 0.9f, 1f);
        public static readonly Color Hover = new Color(1f, 0.85f, 0.2f);
        public static readonly Color Active = new Color(1f, 0.45f, 0.1f);
        public static readonly Color Confirmed = new Color(0.3f, 0.9f, 0.45f);

        /// <summary>A primitive with no collider (we hit-test in code), parented and tinted.</summary>
        public static Renderer Make(PrimitiveType type, Transform parent, Material material, Color color, string name)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Tint(renderer, color);
            return renderer;
        }

        /// <summary>Per-renderer color without a new material. No allocation after the first call.</summary>
        public static void Tint(Renderer renderer, Color color)
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>Distance along the ray to a sphere, or -1 when it misses.</summary>
        public static float RaySphere(Ray ray, Vector3 center, float radius)
        {
            Vector3 oc = center - ray.origin;
            float along = Vector3.Dot(oc, ray.direction);
            if (along < 0f) return -1f;
            float d2 = oc.sqrMagnitude - along * along;
            return d2 <= radius * radius ? along : -1f;
        }

        /// <summary>Where the ray meets the horizontal plane at <paramref name="y"/>; false if it never does (looking up).</summary>
        public static bool RayFloor(Ray ray, float y, out Vector3 hit)
        {
            hit = default;
            if (ray.direction.y > -1e-4f) return false;
            float t = (y - ray.origin.y) / ray.direction.y;
            if (t < 0f || t > 20f) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }
    }
}

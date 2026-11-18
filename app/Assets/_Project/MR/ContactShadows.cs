using OwnSpace.Contracts;
using OwnSpace.Core;
using UnityEngine;

namespace OwnSpace.MR
{
    /// <summary>
    /// Soft contact shadows under virtual pieces (realism pass G): a flat mesh on the floor at the piece's pivot, drawn with
    /// one shared material (OwnSpace/ContactShadow from Resources, GPU instancing on). No shadow maps, no per-frame work: it
    /// is a child of the piece, so it moves, turns and stretches with it.
    /// <para>Device, build e3f28ec ("furniture has no shadows"): the old quad faded over 35% of the footprint on each side,
    /// so under a 2 m bed the fade ran over 70 cm and read as nothing, while its dark core was hidden under the piece. Now
    /// the shadow is full strength (<see cref="DefaultStrength"/>) right up to the footprint's edge and fades out over a
    /// fixed <see cref="Falloff"/> in metres, whatever the piece's size; small dark pads sit near the footprint's corners,
    /// where legs stand. The mesh is a 4 x 4 grid built once per footprint size (cached).</para>
    /// </summary>
    public static class ContactShadows
    {
        public const string ShaderResource = "OwnSpaceContactShadow", ChildName = "ContactShadow";
        /// <summary>Full darkness up to the footprint, then this fade-out (m), the leg pads' extra darkness, and their inset (m).</summary>
        public const float DefaultStrength = 0.55f, Falloff = 0.2f, PadStrength = 0.45f, PadInset = 0.05f, PadRadius = 0.09f;
        // Above my zone's passthrough hole (floor + 3 mm, which writes depth); the shader also pulls it forward in depth.
        public const float Lift = 0.006f;
        static readonly int StrengthId = Shader.PropertyToID("_Strength"), PadId = Shader.PropertyToID("_Pad");

        /// <summary>
        /// The shadow's darkness at a floor point (pure, the shader's rule, for tests). <paramref name="local"/> is in metres from
        /// the footprint's centre, <paramref name="half"/> the footprint's half size: full <paramref name="strength"/> over the
        /// footprint, a smooth fade to 0 at <see cref="Falloff"/> past its edge, darker near its corners (the legs).
        /// </summary>
        public static float Alpha(Vector2 local, Vector2 half, float strength)
        {
            Vector2 q = new Vector2(Mathf.Abs(local.x), Mathf.Abs(local.y));
            float outside = new Vector2(Mathf.Max(q.x - half.x, 0f), Mathf.Max(q.y - half.y, 0f)).magnitude;
            float v = 1f - Mathf.Clamp01(outside / Falloff);
            float a = strength * v * v * (3f - 2f * v);
            return a + (1f - a) * Pad(q, half);
        }

        /// <summary>A leg pad's extra darkness at <paramref name="q"/> (|local| in metres), near the footprint's corners.</summary>
        public static float Pad(Vector2 q, Vector2 half)
        {
            Vector2 leg = new Vector2(Mathf.Max(half.x - PadInset, 0f), Mathf.Max(half.y - PadInset, 0f));
            float d = (q - leg).magnitude;
            return PadStrength * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / PadRadius)));
        }

        static ToneMatchSettings Settings => ToneMatchSettings.Load(); // the strength is tuned there, on device

        static Material _material;
        static readonly System.Collections.Generic.Dictionary<long, Mesh> _meshes = new System.Collections.Generic.Dictionary<long, Mesh>();

        /// <summary>
        /// Whether a piece stands on the floor (pure, for tests): not wall- or ceiling-hung (wall art, curtains, door
        /// trims, pendant lamps, or tagged wall/ceiling), not a flat rug, and not a tabletop piece (tagged
        /// tabletop/desk), so there is never a floor shadow under something that does not touch the floor there.
        /// </summary>
        public static bool Grounded(CatalogItem item)
        {
            if (item == null) return false;
            if (PlacementRules.IsWallMounted(item)) return false; // hung on a wall: no shadow on the floor (device batch 10)
            switch (item.kind)
            {
                case "wall-art": case "curtain": case "door-trim": case "pendant-lamp": case "rug": return false;
            }
            var tags = item.tags;
            for (int i = 0; tags != null && i < tags.Count; i++)
            {
                switch (tags[i]) { case "wall": case "ceiling": case "tabletop": case "desk": return false; }
            }
            return true;
        }

        /// <summary>The shared material, or null when the shader is missing.</summary>
        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                Shader shader = Resources.Load<Shader>(ShaderResource);
                if (shader == null) return null;
                _material = new Material(shader) { name = "ContactShadow (shared)", enableInstancing = true };
                _material.SetFloat(StrengthId, Settings != null ? Settings.ShadowStrength : DefaultStrength);
                _material.SetFloat(PadId, PadStrength);
                return _material;
            }
        }

        /// <summary>
        /// Adds a shadow under <paramref name="piece"/> whose footprint is <paramref name="size"/> (x width, z depth, in
        /// the piece's own axes; pivot at the bottom centre). Returns the renderer, or null without the shader.
        /// </summary>
        public static Renderer Attach(Transform piece, Vector3 size)
        {
            Material material = Material;
            if (material == null || piece == null) return null;
            var go = new GameObject(ChildName);
            go.transform.SetParent(piece, false);
            go.transform.localPosition = new Vector3(0f, Lift, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(size);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>The footprint's half size (m), never under 2.5 cm (pure, for tests).</summary>
        public static Vector2 Half(Vector3 size) => new Vector2(Mathf.Max(0.05f, size.x) * 0.5f, Mathf.Max(0.05f, size.z) * 0.5f);

        /// <summary>
        /// The shadow mesh for a footprint, cached per size (to the centimetre): a 4 x 4 grid in the piece's local metres, the
        /// inner square the footprint, the outer ring <see cref="Falloff"/> wide. Vertex alpha is 1 inside, 0 at the outer
        /// edge; uv is the local x, z (m) and uv2 the half size, for the shader's fade and leg pads.
        /// </summary>
        public static Mesh MeshFor(Vector3 size)
        {
            Vector2 half = Half(size);
            long key = (long)Mathf.RoundToInt(half.x * 100f) << 20 | (long)Mathf.RoundToInt(half.y * 100f);
            if (_meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;
            float[] xs = { -half.x - Falloff, -half.x, half.x, half.x + Falloff }, zs = { -half.y - Falloff, -half.y, half.y, half.y + Falloff };
            var v = new Vector3[16];
            var uv = new Vector2[16];
            var uv2 = new Vector2[16];
            var colors = new Color[16];
            var normals = new Vector3[16];
            for (int j = 0; j < 4; j++)
                for (int i = 0; i < 4; i++)
                {
                    int k = j * 4 + i;
                    v[k] = new Vector3(xs[i], 0f, zs[j]);
                    uv[k] = new Vector2(xs[i], zs[j]);
                    uv2[k] = half;
                    bool inner = (i == 1 || i == 2) && (j == 1 || j == 2);
                    colors[k] = new Color(0f, 0f, 0f, inner ? 1f : 0f);
                    normals[k] = Vector3.up;
                }
            var tris = new int[9 * 6];
            int t = 0;
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    int a = j * 4 + i;
                    tris[t++] = a; tris[t++] = a + 4; tris[t++] = a + 5;
                    tris[t++] = a; tris[t++] = a + 5; tris[t++] = a + 1;
                }
            var mesh = new Mesh { name = "ContactShadow " + key, vertices = v, uv = uv, uv2 = uv2, colors = colors, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            _meshes[key] = mesh;
            return mesh;
        }
    }
}

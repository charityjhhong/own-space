using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace OwnSpace.Diagnostics.Editor
{
    /// <summary>Static render cost of the active renderers under some roots. Editor-time estimate, not a GPU capture.</summary>
    public struct SceneStats
    {
        public int Renderers;
        public long Triangles;
        /// <summary>Upper bound: one draw per submesh, before batching or instancing.</summary>
        public int DrawCalls;
        /// <summary>Unique materials: a proxy for SetPass calls with the SRP Batcher.</summary>
        public int Materials;
        /// <summary>Unique textures at their editor (uncompressed) size: an upper bound for ASTC on Quest.</summary>
        public long TextureBytes;
        public int Textures;

        public static SceneStats Measure(IEnumerable<GameObject> roots)
        {
            var stats = new SceneStats();
            var materials = new HashSet<Material>();
            var textures = new HashSet<Texture>();
            var names = new List<string>();
            var filters = new List<MeshFilter>();
            var shared = new List<Material>();
            foreach (GameObject root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    Mesh mesh = null;
                    if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                    else if (r is MeshRenderer)
                    {
                        r.GetComponent<MeshFilter>()?.GetComponents(filters);
                        if (filters.Count > 0) mesh = filters[0].sharedMesh;
                    }
                    if (mesh == null) continue;
                    r.GetSharedMaterials(shared);
                    int submeshes = Mathf.Min(mesh.subMeshCount, Mathf.Max(1, shared.Count));
                    stats.Renderers++;
                    stats.DrawCalls += submeshes;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        if (mesh.GetTopology(i) == MeshTopology.Triangles) stats.Triangles += mesh.GetIndexCount(i) / 3;
                    }
                    foreach (Material m in shared) if (m != null) materials.Add(m);
                }
            }
            foreach (Material m in materials)
            {
                names.Clear();
                m.GetTexturePropertyNames(names);
                foreach (string n in names)
                {
                    Texture t = m.GetTexture(n);
                    if (t != null) textures.Add(t);
                }
            }
            foreach (Texture t in textures) stats.TextureBytes += Profiler.GetRuntimeMemorySizeLong(t);
            stats.Materials = materials.Count;
            stats.Textures = textures.Count;
            return stats;
        }
    }
}

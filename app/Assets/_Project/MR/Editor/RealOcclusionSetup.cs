using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Sets up real-in-front-of-virtual occlusion (device batch 10). Safe to run twice.
    /// <list type="bullet">
    /// <item>The override material (<c>OwnSpace/RealOcclusion</c>).</item>
    /// <item>A "RealOcclusion" Render Objects feature on the Mobile and PC URP renderers: after opaques, opaque queue, all layers,
    /// the override material, ZTest LEqual, no depth write. It is inactive in the asset; RealOcclusion switches it on only when
    /// Environment Depth is available.</item>
    /// <item>The RealOcclusion component on XrRoot.</item>
    /// </list>
    /// Run: <c>-executeMethod OwnSpace.MR.Editor.RealOcclusionSetup.Apply</c>.
    /// </summary>
    public static class RealOcclusionSetup
    {
        public const string MaterialPath = "Assets/_Project/MR/Materials/M_RealOcclusion.mat";
        static readonly string[] Renderers = { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" };

        public static void Apply()
        {
            Material material = EnsureMaterial();
            var renderers = new List<Object>();
            foreach (string path in Renderers)
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) { Debug.LogWarning("[OwnSpace.MR.Editor] No renderer at " + path); continue; }
                EnsureFeature(data, material);
                renderers.Add(data);
            }
            GameObject root = PrefabUtility.LoadPrefabContents(XrRootBuilder.PrefabPath);
            try
            {
                Configure(root, renderers.ToArray());
                PrefabUtility.SaveAsPrefabAsset(root, XrRootBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnSpace.MR.Editor] Real occlusion set up: material, renderer features (inactive), XrRoot component.");
        }

        /// <summary>The XrRoot part (also called by XrRootBuilder).</summary>
        internal static void Configure(GameObject root, Object[] renderers)
        {
            var occlusion = XrRootBuilder.Ensure<RealOcclusion>(XrRootBuilder.EnsureChild(root, "Occlusion"));
            XrRootBuilder.Wire(occlusion, ("_store", root.GetComponentInChildren<DesignStore>(true)), ("_room", root.GetComponentInChildren<MrukRoomInfo>(true)));
            XrRootBuilder.WireArray(occlusion, "_renderers", renderers);
        }

        internal static Object[] LoadRenderers()
        {
            var list = new List<Object>();
            foreach (string path in Renderers) { var d = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path); if (d != null) list.Add(d); }
            return list.ToArray();
        }

        static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("OwnSpace/RealOcclusion");
            if (shader == null) throw new System.InvalidOperationException("OwnSpace/RealOcclusion shader not found");
            material = new Material(shader) { name = "M_RealOcclusion" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        static void EnsureFeature(UniversalRendererData data, Material material)
        {
            RenderObjects feature = null;
            foreach (ScriptableRendererFeature f in data.rendererFeatures) if (f is RenderObjects ro && f.name == RealOcclusion.FeatureName) feature = ro;
            bool added = feature == null;
            if (added)
            {
                feature = ScriptableObject.CreateInstance<RenderObjects>();
                feature.name = RealOcclusion.FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                var so = new SerializedObject(data);
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            RenderObjects.RenderObjectsSettings s = feature.settings;
            s.passTag = RealOcclusion.FeatureName;
            s.Event = RenderPassEvent.AfterRenderingOpaques;
            s.filterSettings.RenderQueueType = RenderQueueType.Opaque;
            s.filterSettings.LayerMask = ~0;
            // Content's lit surfaces only (furniture, finishes): never the passthrough holes or the seam fade (unlit,
            // SRPDefaultUnlit), whose alpha 0 must reach the compositor untouched (device shot 115520: holes showed black).
            s.filterSettings.PassNames = new[] { "UniversalForward" };
            s.overrideMode = RenderObjects.RenderObjectsSettings.OverrideMaterialMode.Material;
            s.overrideMaterial = material;
            s.overrideMaterialPassIndex = 0;
            s.overrideDepthState = true;
            s.depthCompareFunction = CompareFunction.LessEqual;
            s.enableWrite = false;
            feature.SetActive(false); // RealOcclusion turns it on when depth is available
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            Debug.Log($"[OwnSpace.MR.Editor] {(added ? "Added" : "Updated")} the RealOcclusion pass on {data.name}.");
        }
    }
}

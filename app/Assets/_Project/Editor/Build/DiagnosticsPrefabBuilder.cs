using System.IO;
using OwnSpace.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OwnSpace.Editor.Build
{
    /// <summary>
    /// Regenerates Diagnostics/DiagnosticsRoot.prefab and its materials.
    /// Menu: OwnSpace/Diagnostics/Create DiagnosticsRoot prefab.
    /// Batch: -executeMethod OwnSpace.Editor.Build.DiagnosticsPrefabBuilder.Create
    /// </summary>
    public static class DiagnosticsPrefabBuilder
    {
        const string Folder = "Assets/_Project/Diagnostics";

        [MenuItem("OwnSpace/Diagnostics/Create DiagnosticsRoot prefab")]
        public static void Create()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new FileNotFoundException("URP Unlit shader not found.");
            Material white = Material(shader, "PerfText.mat", new Color(0.92f, 0.95f, 0.92f));
            Material red = Material(shader, "PerfAlert.mat", new Color(1f, 0.32f, 0.28f));
            Material dark = Material(shader, "PerfBackdrop.mat", new Color(0.05f, 0.05f, 0.06f));

            var root = new GameObject("DiagnosticsRoot");
            try
            {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                MeshFilter backdrop = Part(visual, "Backdrop", dark);
                backdrop.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
                backdrop.transform.localPosition = new Vector3(0.115f, -0.055f, 0.002f);
                backdrop.transform.localScale = new Vector3(0.25f, 0.14f, 1f);
                MeshFilter text = Part(visual, "Text", white);
                MeshFilter alert = Part(visual, "Alert", red);

                var overlay = root.AddComponent<PerfOverlay>();
                var so = new SerializedObject(overlay);
                so.FindProperty("textMesh").objectReferenceValue = text;
                so.FindProperty("alertMesh").objectReferenceValue = alert;
                so.FindProperty("visual").objectReferenceValue = visual;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/DiagnosticsRoot.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnSpace.Build] Created " + Folder + "/DiagnosticsRoot.prefab");
        }

        static MeshFilter Part(GameObject parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        static Material Material(Shader shader, string file, Color color)
        {
            string path = Folder + "/Materials/" + file;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            // Reuse the asset so its GUID (and the prefab's reference to it) survives a re-run.
            Material m = existing;
            bool isNew = m == null;
            if (isNew) m = new Material(shader);
            m.shader = shader;
            m.color = color;
            m.SetColor("_BaseColor", color);
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            return m;
        }
    }
}

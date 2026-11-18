using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OwnSpace.Contracts;
using OwnSpace.Content;
using OwnSpace.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OwnSpace.Diagnostics.Editor
{
    /// <summary>
    /// Batch-mode scene cost smoke. Opens Main.unity, adds every outside-zone catalog prefab of one style
    /// (a full outside layout), measures it per style and fails (exit 1) if the worst style breaks a budget.
    /// Run: .github/scripts/perf-smoke.sh   or   -executeMethod OwnSpace.Diagnostics.Editor.PerfSmoke.Run
    /// Static estimate only: runtime-built objects (outside shell, finishes, lights) are not in the scene
    /// until Play, so the real device number is higher. Limits are in docs/qa/perf-budget.md.
    /// </summary>
    public static class PerfSmoke
    {
        const string MainScene = "Assets/_Project/Scenes/Main.unity";
        const string CatalogPath = "Assets/_Project/Content/catalog.json";
        const string PrefabsPath = "Assets/_Project/Content/CatalogPrefabs.asset";

        // Starting values, kept in sync with docs/qa/perf-budget.md "Static scene budget".
        public const int MaxDrawCalls = 150;
        public const long MaxTriangles = 750000;
        public const int MaxMaterials = 60;
        public const long MaxTextureBytes = 256L * 1024 * 1024;

        public static void Run()
        {
            int code;
            try { code = Check(); }
            catch (Exception e) { Debug.LogError("[OwnSpace.PerfSmoke] FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static int Check()
        {
            CatalogLoadResult loaded = CatalogLoader.LoadFile(Path.Combine(Directory.GetParent(Application.dataPath).FullName, CatalogPath));
            foreach (string error in loaded.Errors) Debug.LogWarning("[OwnSpace.PerfSmoke] catalog: " + error);
            var prefabs = AssetDatabase.LoadAssetAtPath<CatalogPrefabs>(PrefabsPath);
            if (prefabs == null) throw new FileNotFoundException(PrefabsPath);

            Scene scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            SceneStats baseline = SceneStats.Measure(scene.GetRootGameObjects());
            var report = new StringBuilder();
            report.AppendLine("[OwnSpace.PerfSmoke] Main.unity baseline: " + Describe(baseline));

            bool breach = false;
            var styles = new SortedSet<string>();
            foreach (CatalogItem item in loaded.Catalog.Items)
                if (item.styles != null) foreach (string st in item.styles) styles.Add(st);
            foreach (string style in styles)
            {
                var spawned = new List<GameObject>();
                int missing = 0;
                float x = 0f;
                foreach (CatalogItem item in loaded.Catalog.Items)
                {
                    if (!item.AllowedIn("outside") || !(item.styles == null || item.styles.Count == 0 || item.styles.Contains(style))) continue;
                    if (!prefabs.TryGetPrefab(item.id, out GameObject prefab)) { missing++; continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.transform.position = new Vector3(x, 0f, 3f);
                    x += 1.5f;
                    spawned.Add(go);
                }
                SceneStats layout = SceneStats.Measure(spawned);
                SceneStats total = Add(baseline, layout);
                foreach (GameObject go in spawned) UnityEngine.Object.DestroyImmediate(go);

                string verdict = Verdict(total);
                if (verdict.Length > 0) breach = true;
                report.AppendLine("[OwnSpace.PerfSmoke] style " + style + ": " + spawned.Count + " pieces" +
                                  (missing > 0 ? " (" + missing + " without prefab)" : "") + ", total " + Describe(total) +
                                  (verdict.Length > 0 ? "  BREACH: " + verdict : "  ok"));
            }
            report.AppendLine("[OwnSpace.PerfSmoke] Limits: draw calls <= " + MaxDrawCalls + ", triangles <= " + MaxTriangles +
                              ", materials <= " + MaxMaterials + ", textures <= " + MaxTextureBytes / 1048576 + " MB (static estimate; shell and runtime objects not counted)");
            Debug.Log(report.ToString());
            Debug.Log(breach ? "[OwnSpace.PerfSmoke] FAIL" : "[OwnSpace.PerfSmoke] PASS");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); // never leave the edited Main open or saved
            return breach ? 1 : 0;
        }

        public static string Verdict(SceneStats s)
        {
            var sb = new StringBuilder();
            if (s.DrawCalls > MaxDrawCalls) sb.Append("draw calls ");
            if (s.Triangles > MaxTriangles) sb.Append("triangles ");
            if (s.Materials > MaxMaterials) sb.Append("materials ");
            if (s.TextureBytes > MaxTextureBytes) sb.Append("texture memory ");
            return sb.ToString().TrimEnd();
        }

        static SceneStats Add(SceneStats a, SceneStats b) => new SceneStats
        {
            Renderers = a.Renderers + b.Renderers,
            Triangles = a.Triangles + b.Triangles,
            DrawCalls = a.DrawCalls + b.DrawCalls,
            // Materials/textures can be shared between baseline and layout: summing over-counts, which is the safe side.
            Materials = a.Materials + b.Materials,
            Textures = a.Textures + b.Textures,
            TextureBytes = a.TextureBytes + b.TextureBytes,
        };

        static string Describe(SceneStats s) =>
            s.Renderers + " renderers, " + s.DrawCalls + " draw calls (max), " + s.Triangles + " tris, " + s.Materials +
            " materials, " + s.Textures + " textures " + s.TextureBytes / 1048576 + " MB";
    }
}

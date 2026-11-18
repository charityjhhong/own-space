using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Renders one thumbnail per catalog prefab into one atlas (Art/UI/FurnitureThumbs.png) and writes
    /// Content/thumbnails.json (cell size, columns, and each item's cell). Transparent background, fixed 3/4 view,
    /// orthographic framing from the projected bounds, 2x supersampled then box-filtered, items in sorted id order,
    /// so a rerun gives the same picture (the GPU may change a few edge pixels by a few levels, so the PNG bytes can differ). Needs a GPU: do NOT pass -nographics.
    /// Batch: Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.ThumbnailBuilder.BuildBatch -quit
    /// </summary>
    public static class ThumbnailBuilder
    {
        const string CatalogPath = "Assets/_Project/Content/catalog.json";
        const string AtlasPath = "Assets/_Project/Art/UI/FurnitureThumbs.png";
        const string JsonPath = "Assets/_Project/Content/thumbnails.json";
        const int CellW = 192, CellH = 128, Columns = 10, Super = 2;
        const float Margin = 1.08f;
        /// <summary>Atlas key of the Tidy up "Plain" look (the box itself, no prop): a neutral floor-coloured box, not a catalog item.</summary>
        public const string PlainCoverId = "cover-plain";

        [Serializable] class CatalogFile { public Entry[] items = Array.Empty<Entry>(); }
        [Serializable] class Entry { public string id, kind; public AssetRef asset; }
        [Serializable] class AssetRef { public string path; }
        [Serializable] class Cell { public string id; public int x, y; }
        [Serializable] class Atlas { public string atlas; public int cellWidth, cellHeight, columns, width, height; public Cell[] items; }

        [MenuItem("OwnSpace/Content/Build Furniture Thumbnails")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e) { Debug.LogError($"[OwnSpace.Content] Thumbnails failed: {e}"); code = 2; }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            var items = new List<Entry>(JsonUtility.FromJson<CatalogFile>(File.ReadAllText(CatalogPath)).items);
            items.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            int total = items.Count + 1; // + the Plain cover cell
            int rows = (total + Columns - 1) / Columns, width = Columns * CellW, height = rows * CellH;
            var pixels = new Color32[width * height];

            Scene scene = EditorSceneManager.NewPreviewScene();
            var ambientMode = RenderSettings.ambientMode; var ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);
            var lightGo = new GameObject("Light", typeof(Light));
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.0f; light.color = new Color(1f, 0.97f, 0.92f); light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            var camGo = new GameObject("Cam", typeof(Camera));
            SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.GetComponent<Camera>();
            cam.cameraType = CameraType.Preview; cam.scene = scene;
            cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 50f;
            var rt = new RenderTexture(CellW * Super, CellH * Super, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var big = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            int failed = 0;
            var cells = new Cell[total];
            try
            {
                for (int i = 0; i < total; i++)
                {
                    int cx = (i % Columns) * CellW, cy = (rows - 1 - i / Columns) * CellH; // row 0 at the top of the image
                    if (i == items.Count)
                    {
                        cells[i] = new Cell { id = PlainCoverId, x = cx, y = height - cy - CellH };
                        var plain = MakePlainCover(scene);
                        Render(cam, rt, big, plain, "clutter-cover");
                        UnityEngine.Object.DestroyImmediate(plain);
                        Blit(big, pixels, width, cx, cy);
                        continue;
                    }
                    cells[i] = new Cell { id = items[i].id, x = cx, y = height - cy - CellH };
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(items[i].asset.path);
                    if (prefab == null) { Debug.LogError($"[OwnSpace.Content] {items[i].id}: prefab missing."); failed++; continue; }
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    foreach (var b in inst.GetComponentsInChildren<MonoBehaviour>()) b.enabled = false;   // a lamp's LampLight must not light its own thumbnail white
                    foreach (var l in inst.GetComponentsInChildren<Light>()) UnityEngine.Object.DestroyImmediate(l);
                    Render(cam, rt, big, inst, items[i].kind);
                    UnityEngine.Object.DestroyImmediate(inst);
                    Blit(big, pixels, width, cx, cy);
                }
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(big);
                EditorSceneManager.ClosePreviewScene(scene);
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels); tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            WriteIfChanged(AtlasPath, png);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            if (importer != null && (importer.mipmapEnabled || !importer.alphaIsTransparency))
            {
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
            var data = new Atlas { atlas = AtlasPath, cellWidth = CellW, cellHeight = CellH, columns = Columns, width = width, height = height, items = cells };
            WriteIfChanged(JsonPath, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true) + "\n"));
            AssetDatabase.Refresh();
            Debug.Log($"[OwnSpace.Content] Thumbnails: {total - failed} rendered, {failed} failed, atlas {width}x{height}.");
            return failed == 0;
        }

        // A plain floor-coloured box (the drawn box itself), sized like the storage box; material lives only in memory.
        static GameObject MakePlainCover(Scene scene)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.localScale = new Vector3(0.6f, 0.4f, 0.4f);
            go.transform.position = new Vector3(0f, 0.2f, 0f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
            var floor = new Color(0.78f, 0.76f, 0.72f);
            mat.SetColor("_BaseColor", floor); mat.color = floor; mat.SetFloat("_Smoothness", 0.15f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void Render(Camera cam, RenderTexture rt, Texture2D big, GameObject inst, string kind)
        {
            float yaw = 145f, pitch = 18f;
            if (kind == "wall-art" || kind == "curtain" || kind == "door-trim") { yaw = 180f; pitch = 0f; }
            else if (kind == "rug") pitch = 40f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            Bounds b = default; bool any = false;
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            Vector3 c = b.center, e = b.extents, inv = Vector3.zero;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var invRot = Quaternion.Inverse(rot);
            for (int k = 0; k < 8; k++)
            {
                var corner = c + new Vector3((k & 1) == 0 ? -e.x : e.x, (k & 2) == 0 ? -e.y : e.y, (k & 4) == 0 ? -e.z : e.z);
                var v = invRot * (corner - c);
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            float aspect = (float)CellW / CellH;
            cam.orthographicSize = Mathf.Max((maxY - minY) / 2f, (maxX - minX) / (2f * aspect)) * Margin;
            Vector3 centerLocal = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = c + rot * centerLocal - rot * Vector3.forward * (e.magnitude * 3f + 1f);
            cam.aspect = aspect;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            big.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = prev;
        }

        // Box-filter the supersampled render into its atlas cell (cy counted from the bottom of the atlas).
        static void Blit(Texture2D big, Color32[] atlas, int atlasWidth, int cx, int cy)
        {
            var src = big.GetPixels32();
            int sw = big.width;
            for (int y = 0; y < CellH; y++)
                for (int x = 0; x < CellW; x++)
                {
                    int r = 0, g = 0, bl = 0, a = 0;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            var p = src[(y * Super + sy) * sw + x * Super + sx];
                            r += p.r * p.a; g += p.g * p.a; bl += p.b * p.a; a += p.a; // premultiply so the edge does not darken
                        }
                    Color32 o = default;
                    if (a > 0) { o.r = (byte)(r / a); o.g = (byte)(g / a); o.b = (byte)(bl / a); }
                    o.a = (byte)(a / (Super * Super));
                    atlas[(cy + y) * atlasWidth + cx + x] = o;
                }
        }

        static void WriteIfChanged(string assetPath, byte[] bytes)
        {
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            if (File.Exists(full) && File.ReadAllBytes(full).Length == bytes.Length && AreEqual(File.ReadAllBytes(full), bytes)) return;
            File.WriteAllBytes(full, bytes);
        }

        static bool AreEqual(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}

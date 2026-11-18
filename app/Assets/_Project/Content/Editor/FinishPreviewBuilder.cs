using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Renders every finish on a 2 x 2 m patch (floors lying down, walls standing) with one directional light into
    /// docs/design/finish-preview.png, so the texture sets, scale and normal direction can be checked without a headset.
    /// Needs a GPU (no -nographics). Batch: Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.FinishPreviewBuilder.BuildBatch -quit
    /// </summary>
    public static class FinishPreviewBuilder
    {
        const string SetPath = "Assets/_Project/Content/FinishSet.asset";
        const int Cell = 480;

        [MenuItem("OwnSpace/Content/Render Finish Preview")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e) { Debug.LogError("[OwnSpace.Content] Finish preview failed: " + e.ToString().Replace("\n", " | ")); code = 2; }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            var set = AssetDatabase.LoadAssetAtPath<FinishSet>(SetPath);
            if (set == null) { Debug.LogError("[OwnSpace.Content] FinishSet.asset is missing."); return false; }
            var so = new SerializedObject(set); var list = so.FindProperty("_entries");
            int n = list.arraySize, cols = 4, rows = (n + cols - 1) / cols;
            var atlas = new Texture2D(Cell * cols, Cell * rows, TextureFormat.RGB24, false);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var ambientMode = RenderSettings.ambientMode; var ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.6f);
            var lg = new GameObject("Light", typeof(Light)); SceneManager.MoveGameObjectToScene(lg, scene);
            var light = lg.GetComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.0f; light.shadows = LightShadows.None;
            lg.transform.rotation = Quaternion.Euler(50f, 35f, 0f);
            var cg = new GameObject("Cam", typeof(Camera)); SceneManager.MoveGameObjectToScene(cg, scene);
            var cam = cg.GetComponent<Camera>(); cam.cameraType = CameraType.Preview; cam.scene = scene; cam.fieldOfView = 40f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            var rt = new RenderTexture(Cell, Cell, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            var tex = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    var mat = e.FindPropertyRelative("material").objectReferenceValue as Material;
                    bool wall = e.FindPropertyRelative("surface").stringValue == "wall";
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); SceneManager.MoveGameObjectToScene(quad, scene);
                    UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                    quad.transform.localScale = new Vector3(2.4f, 2.4f, 1f);
                    quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    if (wall) { quad.transform.rotation = Quaternion.identity; quad.transform.position = new Vector3(0, 1.2f, 3f); cam.transform.SetPositionAndRotation(new Vector3(0.6f, 1.4f, 0.2f), Quaternion.Euler(8f, -12f, 0f)); }
                    else { quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); quad.transform.position = new Vector3(0, 0, 2f); cam.transform.SetPositionAndRotation(new Vector3(0f, 1.3f, 0.2f), Quaternion.Euler(35f, 0f, 0f)); }
                    cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0, false); RenderTexture.active = prev;
                    atlas.SetPixels((i % cols) * Cell, (rows - 1 - i / cols) * Cell, Cell, Cell, tex.GetPixels());
                    UnityEngine.Object.DestroyImmediate(quad);
                }
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            atlas.Apply();
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "design", "finish-preview.png"));
            File.WriteAllBytes(full, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            Debug.Log("[OwnSpace.Content] Finish preview written to docs/design/finish-preview.png.");
            return true;
        }
    }
}

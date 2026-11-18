using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Builds the lighting assets: material LightGlow.mat, one window-light material per mood (colors from
    /// mood-presets.json), and the LightGlow.prefab quad. Idempotent. Batch mode: Unity -batchmode -nographics
    /// -projectPath app -executeMethod OwnSpace.Content.Editor.LightingBuilder.BuildBatch -quit
    /// </summary>
    public static class LightingBuilder
    {
        const string Dir = "Assets/_Project/Art/Lighting";
        const string MoodJson = "Assets/_Project/Content/mood-presets.json";
        const string ShaderName = "OwnSpace/LightGlow";

        [Serializable] class MoodFile { public Mood[] presets = Array.Empty<Mood>(); }
        [Serializable] class Mood { public string id, windowColor; public float windowIntensity; }

        [MenuItem("OwnSpace/Content/Build Lighting Assets")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e)
            {
                Debug.LogError($"[OwnSpace.Content] Lighting build failed: {e}");
                code = 2;
            }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[OwnSpace.Content] Shader {ShaderName} not found."); return false; }
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var m in ShaderUtil.GetShaderMessages(shader))
                    if (m.severity == ShaderCompilerMessageSeverity.Error) Debug.LogError($"[OwnSpace.Content] Shader {m.file}:{m.line}: {m.message}");
                return false;
            }

            // Under passthrough, framebuffer alpha 0 shows the real room. The glow must add colour without touching alpha,
            // or each quad becomes an opaque black square with a bright centre.
            string source = File.ReadAllText(AssetDatabase.GetAssetPath(shader));
            if (!source.Contains("Blend One One, Zero One") || !source.Contains("\"Queue\" = \"Transparent\""))
            { Debug.LogError("[OwnSpace.Content] LightGlow must use 'Blend One One, Zero One' and Queue Transparent."); return false; }
            if (source.Contains("* _Intensity), 1)")) { Debug.LogError("[OwnSpace.Content] LightGlow must output alpha 0."); return false; }

            var moods = JsonUtility.FromJson<MoodFile>(File.ReadAllText(MoodJson));
            var glow = Material("LightGlow", shader, new Color(1f, 0.72f, 0.42f), 0.5f); // 2700 K lamp
            foreach (var mood in moods.presets)
            {
                ColorUtility.TryParseHtmlString(mood.windowColor, out var color);
                Material($"LightGlow_window_{mood.id}", shader, color, mood.windowIntensity * 0.5f);
            }

            string prefabPath = $"{Dir}/LightGlow.prefab";
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "LightGlow";
            UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
            var r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(quad, prefabPath);
            UnityEngine.Object.DestroyImmediate(quad);

            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnSpace.Content] Built glow material, {moods.presets.Length} window-light materials and the glow prefab.");
            return true;
        }

        static Material Material(string name, Shader shader, Color color, float intensity)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Lighting");
            string path = $"{Dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_Color", color);
            mat.SetFloat("_Intensity", intensity);
            mat.SetFloat("_Power", 2.5f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}

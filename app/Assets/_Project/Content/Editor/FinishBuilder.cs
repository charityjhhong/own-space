using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Builds the finish materials (shader OwnSpace/Finish: a CC0 PBR texture set per finish when finishes.json lists one, else the procedural pattern) and FinishSet.asset from finishes.json.
    /// Idempotent. Batch mode: Unity -batchmode -nographics -projectPath app
    /// -executeMethod OwnSpace.Content.Editor.FinishBuilder.BuildBatch -quit
    /// </summary>
    public static class FinishBuilder
    {
        const string JsonPath = "Assets/_Project/Content/finishes.json";
        const string MaterialDir = "Assets/_Project/Art/Finishes";
        const string SetPath = "Assets/_Project/Content/FinishSet.asset";
        const string ShaderName = "OwnSpace/Finish";
        static readonly string[] Surfaces = { "floor", "wall", "both" };
        static readonly string[] PatternNames = { "solid", "stripes", "checker", "planks", "tiles" };

        [Serializable] class FinishFile { public Def[] finishes = Array.Empty<Def>(); }
        [Serializable] class Def
        {
            public string id, name, surface, description, baseColor, accentColor, pattern;
            public float scale = 0.3f, lineWidth = 0.03f;
            public string[] styles, moods;
            public Textures textures;
        }
        [Serializable] class Textures { public string albedo, normal, roughness, thumbnail; public bool tinted; public float gain = 1f, scaleM = 1f; }

        [MenuItem("OwnSpace/Content/Build Finishes")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e)
            {
                Debug.LogError($"[OwnSpace.Content] Finish build failed: {e}");
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

            var file = JsonUtility.FromJson<FinishFile>(File.ReadAllText(JsonPath));
            if (!Validate(file)) return false; // before touching any asset
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder("Assets/_Project/Art", "Finishes");

            var set = AssetDatabase.LoadAssetAtPath<FinishSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<FinishSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            var so = new SerializedObject(set);
            var list = so.FindProperty("_entries");
            list.arraySize = file.finishes.Length;

            for (int i = 0; i < file.finishes.Length; i++)
            {
                var def = file.finishes[i];
                int pattern = Array.IndexOf(PatternNames, def.pattern);
                if (pattern < 0) { Debug.LogError($"[OwnSpace.Content] {def.id}: unknown pattern \"{def.pattern}\""); return false; }

                string path = $"{MaterialDir}/Finish_{def.id}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", Parse(def.baseColor));
                mat.SetColor("_AccentColor", Parse(def.accentColor));
                mat.SetFloat("_Pattern", pattern);
                mat.SetFloat("_Scale", def.scale);
                mat.SetFloat("_Line", def.lineWidth);
                ApplyTextures(mat, def);
                EditorUtility.SetDirty(mat);

                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").stringValue = def.id;
                e.FindPropertyRelative("name").stringValue = def.name;
                e.FindPropertyRelative("surface").stringValue = def.surface;
                e.FindPropertyRelative("description").stringValue = def.description;
                SetStrings(e.FindPropertyRelative("styles"), def.styles);
                SetStrings(e.FindPropertyRelative("moods"), def.moods);
                e.FindPropertyRelative("material").objectReferenceValue = mat;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnSpace.Content] Built {file.finishes.Length} finishes.");
            return true;
        }

        const string TexDir = "Assets/_Project/Art/Finishes/Tex";

        // A finish with a texture set: albedo (sRGB), normal and roughness (linear), mipmaps, anisotropic 8, ASTC 6x6 on Android, 1K max.
        static void ApplyTextures(Material mat, Def def)
        {
            bool has = def.textures != null && !string.IsNullOrEmpty(def.textures.albedo);
            mat.SetFloat("_UseTex", has ? 1f : 0f);
            if (!has) return;
            var t = def.textures;
            mat.SetTexture("_BaseMap", ImportTexture($"{TexDir}/{t.albedo}", true));
            mat.SetTexture("_NormalMap", ImportTexture($"{TexDir}/{t.normal}", false));
            mat.SetTexture("_RoughMap", ImportTexture($"{TexDir}/{t.roughness}", false));
            mat.SetFloat("_TexScale", t.scaleM);
            mat.SetFloat("_Gain", t.gain);
            mat.SetFloat("_NormalStrength", 1f);
            if (!t.tinted) mat.SetColor("_BaseColor", Color.white); // the texture carries the colour
        }

        internal static Texture2D ImportTexture(string path, bool srgb)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException($"texture {path} is missing; run docs/design/import_finishes.py");
            var android = importer.GetPlatformTextureSettings("Android");
            bool changed = importer.sRGBTexture != srgb || !importer.mipmapEnabled || importer.anisoLevel != 8 || importer.maxTextureSize != 1024 ||
                           importer.wrapMode != TextureWrapMode.Repeat || !android.overridden || android.format != TextureImporterFormat.ASTC_6x6;
            if (changed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = srgb;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 1024;
                importer.wrapMode = TextureWrapMode.Repeat;
                android.name = "Android"; android.overridden = true; android.maxTextureSize = 1024; android.format = TextureImporterFormat.ASTC_6x6;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static bool Validate(FinishFile file)
        {
            bool ok = true;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var d in file.finishes)
            {
                if (string.IsNullOrEmpty(d.id) || !seen.Add(d.id)) { Debug.LogError($"[OwnSpace.Content] finish id \"{d.id}\" is empty or duplicated."); ok = false; }
                if (Array.IndexOf(Surfaces, d.surface) < 0) { Debug.LogError($"[OwnSpace.Content] {d.id}: surface must be floor, wall or both, not \"{d.surface}\"."); ok = false; }
                if (Array.IndexOf(PatternNames, d.pattern) < 0) { Debug.LogError($"[OwnSpace.Content] {d.id}: unknown pattern \"{d.pattern}\"."); ok = false; }
            }
            return ok;
        }

        static void SetStrings(SerializedProperty prop, string[] values)
        {
            values = values ?? Array.Empty<string>();
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).stringValue = values[i];
        }

        static Color Parse(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) throw new FormatException($"bad color {hex}");
            return c;
        }
    }
}

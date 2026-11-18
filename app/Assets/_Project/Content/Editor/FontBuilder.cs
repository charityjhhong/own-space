using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Builds the two static TextMeshPro SDF font assets for the UI from Inter (SIL OFL 1.1, see Art/Fonts/Inter-LICENSE.txt
    /// and docs/design/asset-licenses.md): Regular and Semibold, one 1024 x 512 atlas each, with the Latin characters and the
    /// punctuation the panel uses (curly quotes, en and em dash, ellipsis, middle dot, multiplication sign, arrows, degree).
    /// Idempotent: an existing asset is cleared and filled again, so references to it survive.
    /// Batch: Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.FontBuilder.BuildBatch -quit
    /// </summary>
    public static class FontBuilder
    {
        const string Dir = "Assets/_Project/Art/Fonts";
        const int Sampling = 40, Padding = 5, AtlasW = 1024, AtlasH = 512;
        // ASCII 32..126 plus: no-break space, degree, middle dot, e acute, multiplication, en dash, em dash, curly quotes, ellipsis, arrows.
        const string Extra = " °·é×–—‘’“”…←→";

        [MenuItem("OwnSpace/Content/Build UI Fonts")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e) { Debug.LogError("[OwnSpace.Content] Fonts failed: " + e.ToString().Replace("\n", " | ")); code = 2; }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            AssetDatabase.Refresh();
            if (!HasEssentials()) { Debug.LogError("[OwnSpace.Content] TMP Essential Resources are not imported (no TMP Settings or TextMeshPro/Distance Field shader). Import them first, see FontBuilder.cs."); return false; }
            var chars = new System.Text.StringBuilder();
            for (char c = ' '; c <= '~'; c++) chars.Append(c);
            chars.Append(Extra);
            bool ok = true;
            foreach (string weight in new[] { "Regular", "SemiBold" })
                ok &= BuildOne(weight, chars.ToString());
            AssetDatabase.SaveAssets();
            return ok;
        }

        // TMP needs its Essential Resources (the TextMeshPro shaders and the TMP Settings asset), which this project does not have yet.
        // They land in Assets/TextMesh Pro, outside the content paths: ask xr or core to import them once (Window > TextMeshPro >
        // Import TMP Essential Resources, or the batch importer), then run this builder.
        static bool HasEssentials() => TMP_Settings.instance != null && Shader.Find("TextMeshPro/Distance Field") != null;

        static bool BuildOne(string weight, string chars)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{Dir}/Inter-{weight}.ttf");
            if (font == null) { Debug.LogError($"[OwnSpace.Content] {Dir}/Inter-{weight}.ttf is missing."); return false; }
            string path = $"{Dir}/Inter-{weight} SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null)
            {
                fa = TMP_FontAsset.CreateFontAsset(font, Sampling, Padding, GlyphRenderMode.SDFAA, AtlasW, AtlasH, AtlasPopulationMode.Dynamic, false);
                fa.name = $"Inter-{weight} SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.atlasTextures[0].name = $"Inter-{weight} Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = $"Inter-{weight} Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            else
            {
                fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                fa.ClearFontAssetData(true);
            }
            bool all = fa.TryAddCharacters(chars, out string missing);
            if (!all) Debug.LogWarning($"[OwnSpace.Content] Inter-{weight}: characters not in the font: \"{missing}\".");
            fa.atlasPopulationMode = AtlasPopulationMode.Static;   // no runtime atlas growth, so no allocation or hitch in the headset
            EditorUtility.SetDirty(fa); EditorUtility.SetDirty(fa.atlasTextures[0]); EditorUtility.SetDirty(fa.material);
            SettleShaderRatios(fa);
            Debug.Log($"[OwnSpace.Content] Inter-{weight} SDF: {fa.characterTable.Count} characters, atlas {AtlasW}x{AtlasH}, {fa.atlasTextures.Length} atlas texture(s).");
            return all && fa.atlasTextures.Length == 1;
        }

        // Unity writes the material's _ScaleRatioA/_ScaleRatioC the first time a TMP text uses the font. Do that once here, so the
        // committed asset already holds the values and a normal project load leaves no diff.
        static void SettleShaderRatios(TMP_FontAsset fa)
        {
            var go = new GameObject("FontRatioProbe", typeof(TextMeshPro));
            try
            {
                var t = go.GetComponent<TextMeshPro>();
                t.font = fa; t.text = "Aa"; t.ForceMeshUpdate();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            EditorUtility.SetDirty(fa.material);
        }
    }
}

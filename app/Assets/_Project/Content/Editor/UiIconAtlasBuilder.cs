using System;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.Content.Editor
{
    /// <summary>
    /// Imports Art/UI/UIIcons.png (made by docs/design/ui/make_icon_atlas.py) with the right settings for a tinted UI atlas:
    /// no mipmaps, alpha is transparency, clamp, bilinear. Idempotent. Batch:
    /// Unity -batchmode -projectPath app -executeMethod OwnSpace.Content.Editor.UiIconAtlasBuilder.BuildBatch -quit
    /// </summary>
    public static class UiIconAtlasBuilder
    {
        const string Path = "Assets/_Project/Art/UI/UIIcons.png";

        [MenuItem("OwnSpace/Content/Import UI Icon Atlas")]
        public static void Build() => Run();

        public static void BuildBatch()
        {
            int code;
            try { code = Run() ? 0 : 1; }
            catch (Exception e) { Debug.LogError("[OwnSpace.Content] UI icon atlas failed: " + e.ToString().Replace("\n", " | ")); code = 2; }
            EditorApplication.Exit(code);
        }

        static bool Run()
        {
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(Path) as TextureImporter;
            if (importer == null) { Debug.LogError($"[OwnSpace.Content] {Path} is missing; run docs/design/ui/make_icon_atlas.py."); return false; }
            if (importer.mipmapEnabled || !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp || importer.filterMode != FilterMode.Bilinear)
            {
                importer.textureType = TextureImporterType.Default; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }
            Debug.Log("[OwnSpace.Content] UI icon atlas imported.");
            return true;
        }
    }
}

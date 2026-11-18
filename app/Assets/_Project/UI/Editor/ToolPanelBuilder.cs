using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.UI.Editor
{
    /// <summary>
    /// Builds the tool panel assets: the shared glass material (OwnSpace/PanelGlass) and
    /// <c>UI/ToolPanel.prefab</c> with that material and content's Inter Regular / SemiBold SDF fonts
    /// assigned. XrRoot places the prefab and wires the scene references (HandRig). Safe to run twice.
    /// Run: <c>-executeMethod OwnSpace.UI.Editor.ToolPanelBuilder.Build</c>.
    /// </summary>
    public static class ToolPanelBuilder
    {
        public const string Prefab = "Assets/_Project/UI/ToolPanel.prefab";
        public const string Glass = "Assets/_Project/UI/Materials/M_PanelGlass.mat";
        const string Regular = "Assets/_Project/Art/Fonts/Inter-Regular SDF.asset";
        const string SemiBold = "Assets/_Project/Art/Fonts/Inter-SemiBold SDF.asset";
        const string IconAtlas = "Assets/_Project/Art/UI/UIIcons.png", IconRects = "Assets/_Project/Content/ui-icons.json";
        const string ThumbAtlas = "Assets/_Project/Art/UI/FurnitureThumbs.png";

        public static void Build()
        {
            Material glass = GlassMaterial();
            var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Regular);
            var semiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiBold);
            if (regular == null || semiBold == null) throw new FileNotFoundException("Inter SDF fonts not found under Art/Fonts.");

            bool exists = File.Exists(Prefab);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(Prefab) : new GameObject("ToolPanel");
            try
            {
                ToolPanel panel = root.GetComponent<ToolPanel>() ?? root.AddComponent<ToolPanel>();
                var so = new SerializedObject(panel);
                so.FindProperty("_material").objectReferenceValue = glass;
                so.FindProperty("_font").objectReferenceValue = regular;
                so.FindProperty("_fontSemiBold").objectReferenceValue = semiBold;
                so.FindProperty("_iconsJson").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(IconRects);
                // The layout tokens in code (PanelMetrics.Default) are the source of truth: copy every one.
                PanelMetrics d = PanelMetrics.Default;
                so.FindProperty("_metrics.Size").vector2Value = d.Size;
                foreach (var f in typeof(PanelMetrics).GetFields())
                    if (f.FieldType == typeof(float)) so.FindProperty("_metrics." + f.Name).floatValue = (float)f.GetValue(d);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            Debug.Log("[OwnSpace.UI.Editor] ToolPanel prefab built (glass material, Inter fonts).");
        }

        static Material GlassMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Glass);
            if (material != null) { AssignIcons(material); return material; }
            Shader shader = Shader.Find("OwnSpace/PanelGlass");
            if (shader == null) throw new FileNotFoundException("Shader OwnSpace/PanelGlass not found.");
            Directory.CreateDirectory(Path.GetDirectoryName(Glass));
            material = new Material(shader) { name = "M_PanelGlass", enableInstancing = false };
            AssetDatabase.CreateAsset(material, Glass);
            AssignIcons(material);
            return material;
        }

        // Content's icon atlas is a texture on the one glass material, so icons add no material or draw-call setup.
        static void AssignIcons(Material material)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAtlas);
            if (atlas == null) throw new FileNotFoundException("Missing " + IconAtlas);
            var thumbs = AssetDatabase.LoadAssetAtPath<Texture2D>(ThumbAtlas);
            if (material.GetTexture("_IconMap") == atlas && material.GetTexture("_ThumbMap") == thumbs) return;
            material.SetTexture("_IconMap", atlas);
            material.SetTexture("_ThumbMap", thumbs); // furniture thumbnails: the same material, thumbnail mode
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Realism pass H setup, safe to run twice (batch: -executeMethod OwnSpace.MR.Editor.ToneMatchSetup.Run): creates
    /// MR/Resources/ToneMatchSettings.asset when missing. The grade itself lives in each virtual surface's shader
    /// (MR/Shaders/ToneMatch.hlsl); there is no render pass and no renderer-asset change.
    /// </summary>
    public static class ToneMatchSetup
    {
        const string SettingsPath = "Assets/_Project/MR/Resources/" + ToneMatchSettings.ResourceName + ".asset";

        public static void Run()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/MR/Resources")) AssetDatabase.CreateFolder("Assets/_Project/MR", "Resources");
            if (AssetDatabase.LoadAssetAtPath<ToneMatchSettings>(SettingsPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ToneMatchSettings>(), SettingsPath);
            AssetDatabase.SaveAssets();
        }
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Eye textures at 1.2 per axis (the human: "eye resolution 1.2x"). Meta's OVRManager in Main.unity has
    /// dynamic resolution on; at start it sets <c>XRSettings.eyeTextureResolutionScale</c> and the URP asset's render scale
    /// to its per-headset maximum, so that maximum is what the swapchains are allocated at. It was Meta's default 1.6 on
    /// Quest 3 and 3S (2.56x the pixels of 1.0, with MSAA 4: about 1.7 GB of GL memory, build d3a6dcc) and 1.3 on Quest 2
    /// and Pro. All maxima become <see cref="Max"/>; the minima stay <see cref="Min"/> (how far dynamic resolution may lower
    /// the rendered viewport under load; it never changes the allocation). The Android URP asset's render scale is set to
    /// the same value, so nothing disagrees at start. Safe to run twice.
    /// Run: <c>-executeMethod OwnSpace.MR.Editor.EyeResolutionSetup.Apply</c>.
    /// </summary>
    public static class EyeResolutionSetup
    {
        public const float Max = 1.2f, Min = 0.7f;
        const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string AndroidPipelinePath = "Assets/Settings/Mobile_RPAsset.asset";

        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            OVRManager manager = Object.FindAnyObjectByType<OVRManager>(FindObjectsInactive.Include);
            if (manager == null) throw new System.InvalidOperationException("No OVRManager in Main.unity.");
            var so = new SerializedObject(manager);
            bool changed = false;
            changed |= Set(so, "_enableDynamicResolution", true);
            changed |= Set(so, "quest2MaxDynamicResolutionScale", Max);
            changed |= Set(so, "quest3MaxDynamicResolutionScale", Max);
            changed |= Set(so, "maxDynamicResolutionScale", Max);
            changed |= Set(so, "quest2MinDynamicResolutionScale", Min);
            changed |= Set(so, "quest3MinDynamicResolutionScale", Min);
            changed |= Set(so, "minDynamicResolutionScale", Min);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AndroidPipelinePath);
            if (pipeline == null) throw new System.InvalidOperationException("No URP asset at " + AndroidPipelinePath);
            bool pipelineChanged = !Mathf.Approximately(pipeline.renderScale, Max);
            if (pipelineChanged)
            {
                pipeline.renderScale = Max;
                EditorUtility.SetDirty(pipeline);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[OwnSpace.MR.Editor] Eye resolution: dynamic resolution max {Max}, min {Min} (Quest 2/Pro and Quest 3/3S), " +
                      $"URP render scale {Max}; {(changed || pipelineChanged ? "changed" : "already set, nothing changed")}.");
        }

        static bool Set(SerializedObject so, string name, float value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.InvalidOperationException("OVRManager has no field " + name);
            if (Mathf.Approximately(p.floatValue, value)) return false;
            p.floatValue = value;
            return true;
        }

        static bool Set(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) throw new System.InvalidOperationException("OVRManager has no field " + name);
            if (p.boolValue == value) return false;
            p.boolValue = value;
            return true;
        }
    }
}

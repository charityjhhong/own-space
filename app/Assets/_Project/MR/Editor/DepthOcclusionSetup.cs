using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Turns on Unity's "Meta Quest: Occlusion" OpenXR feature for Android, and "Meta Quest: Session", which that package
    /// requires for every AR feature (a build-validation error without it). Meta's <c>EnvironmentDepthManager</c> on Unity 6
    /// with OpenXR reads depth only through this feature's occlusion subsystem (<c>com.unity.xr.meta-openxr</c> 2.1+).
    /// Nothing else from that package is turned on: passthrough, scene and anchors stay with the Meta XR Core SDK.
    /// Safe to run twice. Run: <c>-executeMethod OwnSpace.MR.Editor.DepthOcclusionSetup.Apply</c>.
    /// </summary>
    public static class DepthOcclusionSetup
    {
        public static void Apply()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null) throw new System.InvalidOperationException("No Android OpenXR settings.");
            // Session first: turning it on also turns on the package's hidden life-cycle feature, which its native plug-in needs.
            Enable(settings.GetFeature<ARSessionFeature>(), "Meta Quest: Session");
            Enable(settings.GetFeature<AROcclusionFeature>(), "Meta Quest: Occlusion");
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            var on = new System.Text.StringBuilder();
            foreach (OpenXRFeature feature in settings.GetFeatures())
                if (feature != null && feature.enabled) on.Append(on.Length > 0 ? ", " : "").Append(feature.GetType().Name);
            Debug.Log($"[OwnSpace.MR.Editor] OpenXR (Android) features on: {on}.");
        }

        static void Enable(OpenXRFeature feature, string name)
        {
            if (feature == null) throw new System.InvalidOperationException($"'{name}' is not registered in the Android OpenXR settings (is com.unity.xr.meta-openxr installed?).");
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }
    }
}

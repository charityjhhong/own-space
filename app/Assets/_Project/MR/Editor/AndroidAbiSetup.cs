using System.IO;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Makes the Android player ARM64 only: target architecture ARM64, and the custom launcher Gradle
    /// template (Assets/Plugins/Android/launcherTemplate.gradle, which pins abiFilters to arm64-v8a and
    /// excludes other-ABI libs from AARs) switched on in Player settings. Safe to run twice.
    /// Run: <c>-executeMethod OwnSpace.MR.Editor.AndroidAbiSetup.Apply</c>.
    /// </summary>
    public static class AndroidAbiSetup
    {
        const string Template = "Assets/Plugins/Android/launcherTemplate.gradle";

        public static void Apply()
        {
            if (!File.Exists(Template)) throw new FileNotFoundException("Missing " + Template);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            // No public API for the "Custom Launcher Gradle Template" toggle: set the serialized flag.
            Object[] players = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            if (players.Length == 0) throw new System.InvalidOperationException("PlayerSettings not found.");
            var so = new SerializedObject(players[0]);
            SerializedProperty flag = so.FindProperty("useCustomLauncherGradleManifest");
            if (flag == null) throw new System.InvalidOperationException("useCustomLauncherGradleManifest not found.");
            flag.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnSpace.MR.Editor] Android: ARM64 only, custom launcher Gradle template on.");
        }
    }
}

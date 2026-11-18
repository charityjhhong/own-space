using System.IO;
using UnityEditor;
using UnityEngine;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Imports TextMeshPro's Essential Resources (runtime SDF shaders, TMP Settings, Liberation Sans
    /// SDF) from the com.unity.ugui package into Assets/TextMesh Pro, once. Safe to run twice: it does
    /// nothing when TMP Settings is already there.
    /// Batch (without -quit; it exits when the import finishes):
    /// <c>-executeMethod OwnSpace.MR.Editor.TmpEssentials.ImportBatch</c>.
    /// </summary>
    public static class TmpEssentials
    {
        const string Settings = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string Package = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        /// <summary>Batch mode: imports, waits for the package import to finish, then exits (0 = done).</summary>
        public static void ImportBatch()
        {
            if (File.Exists(Settings)) { Debug.Log("[OwnSpace.MR.Editor] TMP Essentials already imported."); EditorApplication.Exit(0); return; }
            AssetDatabase.importPackageCompleted += _ => Finish(true);
            AssetDatabase.importPackageFailed += (_, error) => { Debug.LogError("[OwnSpace.MR.Editor] TMP import failed: " + error); Finish(false); };
            Import();
        }

        static void Finish(bool ok)
        {
            AssetDatabase.Refresh();
            bool done = ok && File.Exists(Settings);
            Debug.Log($"[OwnSpace.MR.Editor] TMP Essentials imported: {done}.");
            EditorApplication.Exit(done ? 0 : 1);
        }

        /// <summary>Starts the import (asynchronous; the Editor finishes it).</summary>
        public static void Import()
        {
            if (File.Exists(Settings))
            {
                Debug.Log("[OwnSpace.MR.Editor] TMP Essentials already imported.");
                return;
            }
            string full = Path.GetFullPath(Package);
            if (!File.Exists(full)) throw new FileNotFoundException("TMP Essential Resources package not found", full);
            AssetDatabase.ImportPackage(full, false);
        }
    }
}

using Meta.XR.MRUtilityKit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Sets MRUK in Main.unity to load the room from the device only. It was "device with prefab fallback": when the
    /// startup load came back empty on a headset, MRUK loaded one of Meta's sample rooms at random and the app treated it
    /// as the user's room (device check of PR #275, and saves with random room ids). A failed load now goes to the
    /// no-scan path. The Editor still gets the sample rooms: <see cref="MrukRoomInfo"/> switches the fallback back on there.
    /// Safe to run twice. Run: <c>-executeMethod OwnSpace.MR.Editor.MrukDataSourceSetup.Apply</c>.
    /// </summary>
    public static class MrukDataSourceSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            MRUK mruk = Object.FindAnyObjectByType<MRUK>(FindObjectsInactive.Include);
            if (mruk == null || mruk.SceneSettings == null) throw new System.InvalidOperationException("No MRUK with scene settings in Main.unity.");
            if (mruk.SceneSettings.DataSource == MRUK.SceneDataSource.Device)
            {
                Debug.Log("[OwnSpace.MR.Editor] MRUK data source is already Device; nothing changed.");
                return;
            }
            Debug.Log($"[OwnSpace.MR.Editor] MRUK data source {mruk.SceneSettings.DataSource} -> Device.");
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            EditorUtility.SetDirty(mruk);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}

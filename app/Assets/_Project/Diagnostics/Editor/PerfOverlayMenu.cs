using UnityEditor;

namespace OwnSpace.Diagnostics.Editor
{
    /// <summary>Play-mode menu to show or hide the perf overlay without a headset gesture.</summary>
    static class PerfOverlayMenu
    {
        const string Path = "OwnSpace/Diagnostics/Toggle perf overlay";

        [MenuItem(Path)]
        static void Toggle() => PerfOverlay.Instance.Toggle();

        [MenuItem(Path, true)]
        static bool CanToggle() => EditorApplication.isPlaying && PerfOverlay.Instance != null;
    }
}

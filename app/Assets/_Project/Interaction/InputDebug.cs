using System.Diagnostics;

namespace OwnSpace.Interaction
{
    /// <summary>
    /// Input trace for the Editor (XR Simulator) and development builds on the headset (logcat), so a tester can
    /// tell targeting from pinching. Calls (and their string arguments) are compiled out of release builds.
    /// Every call site logs on an event (a pinch start or end), never every frame.
    /// </summary>
    public static class InputDebug
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(string message) => UnityEngine.Debug.Log("[OwnSpace.Interaction] " + message);
    }
}

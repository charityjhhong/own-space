using UnityEngine;

namespace OwnSpace.Diagnostics
{
    /// <summary>
    /// Release builds log warnings and errors only. Info lines (<c>Debug.Log</c>) cost time and fill logcat;
    /// the Editor and development builds keep everything.
    /// </summary>
    public static class ReleaseLogLevel
    {
        /// <summary>The level a release build runs at.</summary>
        public const LogType ReleaseLevel = LogType.Warning;

        /// <summary>True when this build keeps info lines (Editor or development build).</summary>
        public static bool KeepsInfoLogs
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            Debug.unityLogger.filterLogType = ReleaseLevel;
#endif
        }
    }
}

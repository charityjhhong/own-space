using NUnit.Framework;
using UnityEngine;

namespace OwnSpace.Diagnostics.Tests
{
    public class ReleaseLogLevelTests
    {
        [Test] public void ReleaseBuildsLogWarningsAndAbove() =>
            Assert.AreEqual(LogType.Warning, ReleaseLogLevel.ReleaseLevel);

        [Test] public void EditorKeepsInfoLogs() =>
            Assert.IsTrue(ReleaseLogLevel.KeepsInfoLogs);

        [Test] public void EditorLoggerIsNotFiltered() =>
            Assert.AreEqual(LogType.Log, Debug.unityLogger.filterLogType);
    }
}

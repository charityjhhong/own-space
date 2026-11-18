using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
using UnityEngine;

namespace OwnSpace.MR.Editor
{
    /// <summary>
    /// Compiles the player scripts for Android (no build), so warnings inside <c>#if UNITY_EDITOR</c>
    /// splits show up locally. Run: <c>-executeMethod OwnSpace.MR.Editor.PlayerCompileCheck.Android</c>.
    /// Logs every warning and error in our code (Assets/_Project) and, in batch mode, exits 1 when
    /// there is any or the compile fails, else 0.
    /// </summary>
    public static class PlayerCompileCheck
    {
        const string Ours = "Assets/_Project/";
        static int _problems;

        public static void Android()
        {
            _problems = 0;
            string output = FileUtil.GetUniqueTempPathInProject();
            var settings = new ScriptCompilationSettings { group = BuildTargetGroup.Android, target = BuildTarget.Android };
            CompilationPipeline.assemblyCompilationFinished += Collect;
            ScriptCompilationResult result;
            try { result = PlayerBuildInterface.CompilePlayerScripts(settings, output); }
            finally { CompilationPipeline.assemblyCompilationFinished -= Collect; }
            bool compiled = result.assemblies != null && result.assemblies.Count > 0;
            Debug.Log($"[OwnSpace.MR.Editor] Android player scripts: {(compiled ? result.assemblies.Count + " assemblies" : "FAILED")}, " +
                      $"{_problems} warning(s)/error(s) in {Ours}.");
            FileUtil.DeleteFileOrDirectory(output);
            if (Application.isBatchMode) EditorApplication.Exit(compiled && _problems == 0 ? 0 : 1);
        }

        static void Collect(string assembly, CompilerMessage[] messages)
        {
            foreach (CompilerMessage m in messages)
            {
                if (m.file == null || !m.file.Replace('\\', '/').Contains(Ours)) continue;
                _problems++;
                Debug.LogWarning($"[OwnSpace.MR.Editor] {m.type}: {m.message}");
            }
        }
    }
}

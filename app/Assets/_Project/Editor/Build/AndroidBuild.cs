using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OwnSpace.Editor.Build
{
    /// <summary>
    /// Batch-mode Android APK build. Entry point: OwnSpace.Editor.Build.AndroidBuild.BuildApk
    /// Inputs (command-line argument wins over environment variable):
    ///   -ownspaceVersion      / OWNSPACE_VERSION       version name, e.g. 0.1.0 (required)
    ///   -ownspaceVersionCode  / OWNSPACE_VERSION_CODE  integer, +1 per upload (required)
    ///   -ownspaceCommit       / OWNSPACE_COMMIT        git commit, first 7 characters used (required)
    /// Signing (environment only, never committed): OWNSPACE_KEYSTORE_PATH, OWNSPACE_KEYSTORE_PASS,
    /// OWNSPACE_KEYALIAS_NAME, OWNSPACE_KEYALIAS_PASS. If the keystore path is absent the APK is debug-signed.
    /// </summary>
    public static class AndroidBuild
    {
        const string OutputFolder = "Builds";

        public static void BuildApk()
        {
            int exitCode;
            try
            {
                exitCode = Run();
            }
            catch (Exception e)
            {
                Debug.LogError("[OwnSpace.Build] FAILED: " + e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        static int Run()
        {
            string version = Require("ownspaceVersion", "OWNSPACE_VERSION");
            string codeText = Require("ownspaceVersionCode", "OWNSPACE_VERSION_CODE");
            string commit = Require("ownspaceCommit", "OWNSPACE_COMMIT");
            if (!Regex.IsMatch(version, @"^[0-9]+\.[0-9]+\.[0-9]+$"))
                throw new ArgumentException("Version name must be <major>.<minor>.<patch> (for example 0.2.3 or 1.0.0), got '" + version + "'.");
            if (!int.TryParse(codeText, out int versionCode) || versionCode < 1)
                throw new ArgumentException("Version code must be a positive integer, got '" + codeText + "'.");
            if (commit.Length < 7)
                throw new ArgumentException("Commit must have at least 7 characters, got '" + commit + "'.");
            commit = commit.Substring(0, 7);

            string[] scenes = Array.ConvertAll(
                Array.FindAll(EditorBuildSettings.scenes, s => s.enabled),
                s => s.path);
            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes in Build Settings.");

            // The project root is the parent of Assets.
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, OutputFolder);
            Directory.CreateDirectory(outDir);
            string apkPath = Path.Combine(outDir, "ownspace-v" + version + "-" + commit + ".apk");
            // A stale APK with the same name must never be mistaken for the result of this build.
            if (File.Exists(apkPath)) File.Delete(apkPath);
            WarnIfDirty(projectRoot);

            // PlayerSettings are restored afterwards so a batch build never dirties ProjectSettings.
            string oldVersion = PlayerSettings.bundleVersion;
            int oldCode = PlayerSettings.Android.bundleVersionCode;
            bool oldCustomKeystore = PlayerSettings.Android.useCustomKeystore;
            bool oldAab = EditorUserBuildSettings.buildAppBundle;
            string oldKeystore = PlayerSettings.Android.keystoreName;
            string oldAlias = PlayerSettings.Android.keyaliasName;
            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new InvalidOperationException("Could not switch the active build target to Android.");

                PlayerSettings.bundleVersion = version;
                PlayerSettings.Android.bundleVersionCode = versionCode;
                EditorUserBuildSettings.buildAppBundle = false;
                bool signed = ApplySigning();

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = Environment.GetEnvironmentVariable("OWNSPACE_DEVELOPMENT") == "1"
                        ? BuildOptions.Development | BuildOptions.ConnectWithProfiler   // perf overlay on, Autoconnect Profiler
                        : BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError("[OwnSpace.Build] FAILED: result=" + summary.result + ", errors=" + summary.totalErrors);
                    return 1;
                }
                Debug.Log("[OwnSpace.Build] SUCCESS: " + apkPath + " (" + summary.totalSize + " bytes, versionCode " +
                          versionCode + ", " + (signed ? "release-signed" : "DEBUG-SIGNED, not for upload") + ")");
                return 0;
            }
            finally
            {
                PlayerSettings.bundleVersion = oldVersion;
                PlayerSettings.Android.bundleVersionCode = oldCode;
                PlayerSettings.Android.useCustomKeystore = oldCustomKeystore;
                PlayerSettings.Android.keystoreName = oldKeystore;
                PlayerSettings.Android.keyaliasName = oldAlias;
                PlayerSettings.Android.keystorePass = string.Empty;
                PlayerSettings.Android.keyaliasPass = string.Empty;
                EditorUserBuildSettings.buildAppBundle = oldAab;
            }
        }

        /// <returns>true if a release keystore was applied, false for debug signing.</returns>
        static bool ApplySigning()
        {
            string path = Environment.GetEnvironmentVariable("OWNSPACE_KEYSTORE_PATH");
            if (string.IsNullOrEmpty(path))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.LogWarning("[OwnSpace.Build] OWNSPACE_KEYSTORE_PATH is not set: building a DEBUG-SIGNED APK. It cannot go to the Competition channel.");
                return false;
            }
            if (!File.Exists(path))
                throw new FileNotFoundException("OWNSPACE_KEYSTORE_PATH points to a missing file.");
            string storePass = Environment.GetEnvironmentVariable("OWNSPACE_KEYSTORE_PASS");
            string alias = Environment.GetEnvironmentVariable("OWNSPACE_KEYALIAS_NAME");
            string aliasPass = Environment.GetEnvironmentVariable("OWNSPACE_KEYALIAS_PASS");
            if (string.IsNullOrEmpty(storePass) || string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(aliasPass))
                throw new InvalidOperationException(
                    "OWNSPACE_KEYSTORE_PATH is set but OWNSPACE_KEYSTORE_PASS, OWNSPACE_KEYALIAS_NAME or OWNSPACE_KEYALIAS_PASS is missing.");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = storePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPass;
            return true;
        }

        static void WarnIfDirty(string projectRoot)
        {
            try
            {
                var psi = new ProcessStartInfo("git", "status --porcelain")
                {
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && output.Trim().Length > 0)
                        Debug.LogWarning("[OwnSpace.Build] Working tree has uncommitted or untracked files; the commit in the file name does not describe this build exactly.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[OwnSpace.Build] Could not check git status: " + e.Message);
            }
        }

        static string Require(string argName, string envName)
        {
            string value = GetArg("-" + argName) ?? Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Missing -" + argName + " (or environment variable " + envName + ").");
            return value;
        }

        static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}

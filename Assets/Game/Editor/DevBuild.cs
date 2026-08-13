using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    public static class DevBuild
    {
        public const string WinPath = "Builds/Win/GlimmerGrove.exe";

        [MenuItem("Glimmer Grove/Build Windows Player", false, 40)]
        public static void BuildWindows() => Build(false);

        /// <summary>Windows build with the screenshot harness compiled in.</summary>
        public static void BuildShotHarness() => Build(true);

        static void Build(bool shots)
        {
            ProjectSetup.Setup();
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.defaultScreenWidth = 720;
            PlayerSettings.defaultScreenHeight = 1280;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;

            var dir = Path.GetDirectoryName(WinPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = WinPath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
                extraScriptingDefines = shots ? new[] { "GLIMMER_SHOTS" } : new string[0],
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[Glimmer] build {s.result} - {s.totalSize / 1048576} MB, {s.totalErrors} errors, {s.totalWarnings} warnings");
            if (s.result != BuildResult.Succeeded) Debug.LogError("[Glimmer] build failed");
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    public static class DevBuild
    {
        public const string WinPath = "Builds/Win/GlimmerGrove.exe";

        public const string ApkPath = "Builds/Android/GlimmerGrove.apk";

        [MenuItem("Glimmer Grove/Build Windows Player", false, 40)]
        public static void BuildWindows() => Build(false);

        /// <summary>
        /// A test APK, signed with the debug keystore.
        ///
        /// Debug signing is deliberate rather than lazy: that keystore's SHA-1 is the one
        /// registered against the Firebase Android app, so Google Sign-In actually works
        /// in this build. A release-signed APK would fail sign-in until its fingerprint —
        /// and, once you ship through Play, Play App Signing's fingerprint — is added too.
        ///
        /// An APK rather than an App Bundle because this is for sideloading onto a
        /// device; the store build wants <c>buildAppBundle = true</c>.
        /// </summary>
        [MenuItem("Glimmer Grove/Build Android APK", false, 41)]
        public static void BuildAndroidApk() => BuildAndroid(andRun: false);

        /// <summary>
        /// Build, install and launch on whatever device is plugged in — the loop you
        /// actually want while testing anything mobile-only, such as sign-in.
        /// </summary>
        [MenuItem("Glimmer Grove/Build Android APK and Run", false, 42)]
        public static void BuildAndroidApkAndRun() => BuildAndroid(andRun: true);

        static void BuildAndroid(bool andRun)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                // Switching reimports every asset for the new platform, which is slow the
                // first time and unavoidable. Doing it explicitly means the log says so
                // rather than the build appearing to hang.
                Debug.Log("[Glimmer] switching to Android; the first switch reimports all assets");
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(
                        BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[Glimmer] could not switch to Android - is Android Build Support installed?");
                    return;
                }
            }

            ProjectSetup.Setup();

            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;      // debug keystore
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // Engine code stripping removes MonoScript (class 115), which Firebase needs:
            // it starts by adding UnitySynchronizationContext — a MonoBehaviour — to a
            // GameObject at runtime, and nothing references that statically for the
            // stripper to see. With it on, the device logs "Could not produce class with
            // ID 115" thousands of times and Firebase never initialises, so cloud save is
            // silently dead while everything else looks fine.
            //
            // Set here rather than left to whoever last touched Player Settings, for the
            // same reason m_BuildAddressablesWithPlayerBuild is pinned in the project
            // asset: a build that only works on one machine is not a build.
            PlayerSettings.stripEngineCode = false;

            // Firebase ships a link.xml, so managed stripping is safe to leave at the
            // platform default; only the engine-side stripper is the problem here.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,
                                                    ManagedStrippingLevel.Minimal);

            var dir = Path.GetDirectoryName(ApkPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
                          | (andRun ? BuildOptions.AutoRunPlayer : BuildOptions.None),
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;

            Debug.Log($"[Glimmer] apk {s.result} - {s.totalSize / 1048576} MB, " +
                      $"{s.totalErrors} errors, {s.totalWarnings} warnings -> {ApkPath}");

            if (s.result != BuildResult.Succeeded)
                Debug.LogError("[Glimmer] APK build failed; if it mentions Google Play Services, run " +
                               "Assets > External Dependency Manager > Android Resolver > Force Resolve");
        }

        /// <summary>Windows build with the screenshot harness compiled in.</summary>
        public static void BuildShotHarness() => Build(true);

        /// <summary>Batch-mode entry point, used by Tools/Play.ps1 and CI:
        /// Unity.exe -batchmode -executeMethod GlimmerGrove.EditorTools.DevBuild.BuildWindowsBatch
        /// Exits non-zero on failure so the caller can tell a broken build from a good one —
        /// without this, batchmode reports success even when BuildPlayer fails.</summary>
        public static void BuildWindowsBatch() => EditorApplication.Exit(Build(false) ? 0 : 1);

        static bool Build(bool shots)
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
            return s.result == BuildResult.Succeeded;
        }
    }
}

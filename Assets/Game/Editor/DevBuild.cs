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

        /// <summary>Where the store build lands. Gitignored along with the rest of Builds/.</summary>
        public const string AabPath = "Builds/Android/GlimmerGrove.aab";

        /// <summary>
        /// Environment variables the store build reads its signing credentials from.
        ///
        /// <para>
        /// <b>Never Player Settings, and never a file in the repository.</b> Unity stores a
        /// keystore password in <c>ProjectSettings.asset</c> in plain text the moment you
        /// type it into the Publishing Settings panel, and that file is committed — so the
        /// key that identifies this app to Google would be in the history for ever, and
        /// removing it later does not remove it from the history. Reading them from the
        /// environment keeps the credential on the machine doing the build, which is the
        /// only place it belongs.
        /// </para>
        /// </summary>
        public const string KeystoreEnv = "GLIMMER_KEYSTORE";
        public const string KeystorePassEnv = "GLIMMER_KEYSTORE_PASS";
        public const string KeyAliasEnv = "GLIMMER_KEY_ALIAS";
        public const string KeyAliasPassEnv = "GLIMMER_KEY_ALIAS_PASS";

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

        /// <summary>
        /// The Android App Bundle Google Play actually accepts.
        ///
        /// <para>
        /// Deliberately a separate entry point from <see cref="BuildAndroidApk"/> rather
        /// than a flag on it, because almost every setting differs and the two failure modes
        /// are opposites. The APK is a development build, debug-signed, ARM64 only, for
        /// sideloading onto the phone on your desk. This is a release build, signed with a
        /// key only you hold, carrying every architecture, which Play will reject outright
        /// if any one of those is wrong. A single method with a boolean would have to get
        /// six things right in two directions.
        /// </para>
        /// <para>
        /// <b>Both architectures, and it costs players nothing.</b> An App Bundle is split by
        /// Play into per-device downloads, so shipping ARMv7 as well as ARM64 makes the
        /// <em>upload</em> bigger and the build slower and leaves what any individual player
        /// downloads exactly the same. For a game that is meant to be distributed globally
        /// that is a trade with no downside — ARM64-only would silently exclude the cheaper
        /// devices that still make up real markets.
        /// </para>
        /// <para>
        /// <b>The version code is bumped here, on purpose.</b> Play refuses an upload whose
        /// <c>versionCode</c> it has seen before, and forgetting to raise it is the single
        /// commonest rejection there is. Raising it in the build script means it cannot be
        /// forgotten, and because it lives in a tracked file the change is visible in the
        /// diff rather than happening invisibly.
        /// </para>
        /// </summary>
        [MenuItem("Glimmer Grove/Build Android App Bundle (store)", false, 43)]
        public static void BuildAndroidAppBundle()
        {
            string keystore = System.Environment.GetEnvironmentVariable(KeystoreEnv);
            string keystorePass = System.Environment.GetEnvironmentVariable(KeystorePassEnv);
            string alias = System.Environment.GetEnvironmentVariable(KeyAliasEnv);
            string aliasPass = System.Environment.GetEnvironmentVariable(KeyAliasPassEnv);

            if (string.IsNullOrEmpty(keystore) || string.IsNullOrEmpty(keystorePass) ||
                string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(aliasPass))
            {
                // Refused rather than falling back to the debug keystore. A debug-signed
                // bundle is rejected by Play on upload with a message that does not say
                // "debug", so the twenty minutes spent building it are wasted twice.
                Debug.LogError(
                    $"[Glimmer] no signing key. Set {KeystoreEnv}, {KeystorePassEnv}, " +
                    $"{KeyAliasEnv} and {KeyAliasPassEnv} before building, and restart the " +
                    "Editor so it picks them up. Play rejects a debug-signed bundle, so this " +
                    "refuses rather than building one you cannot upload.");
                return;
            }

            if (!File.Exists(keystore))
            {
                Debug.LogError($"[Glimmer] {KeystoreEnv} points at '{keystore}', which is not there");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[Glimmer] switching to Android; the first switch reimports all assets");
                if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(
                        BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[Glimmer] could not switch to Android - is Android Build Support installed?");
                    return;
                }
            }

            ProjectSetup.Setup();

            EditorUserBuildSettings.buildAppBundle = true;

            // Native symbols, so a crash in Play Console reads as a stack trace instead of
            // a column of hex. Uploaded with the bundle automatically.
            //
            // The deprecated API is used deliberately. Unity's replacement lives in
            // `UnityEditor.Android.UserBuildSettings`, which only exists when the Android
            // module is installed — and this file has to compile on a Mac set up to build
            // iOS only, which is exactly the mistake `LauncherIcons` had to be repaired for.
            // The old property is in UnityEditor core, works, and costs two warnings. If it
            // is ever actually removed, guard the new one with `#if UNITY_ANDROID` rather
            // than reaching for it unguarded.
#pragma warning disable 618
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
#pragma warning restore 618

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = keystorePass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = aliasPass;

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // Both of these are the same requirement the APK path documents at length: the
            // engine stripper removes MonoScript, which Firebase needs to exist at runtime,
            // and without it cloud save is silently dead while everything else looks fine.
            PlayerSettings.stripEngineCode = false;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,
                                                    ManagedStrippingLevel.Minimal);

            PlayerSettings.Android.bundleVersionCode++;

            var dir = Path.GetDirectoryName(AabPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = AabPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,

                // No Development and no AllowDebugging. Play rejects a bundle marked
                // debuggable, and a development build ships the profiler and slower code.
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;

            // The file on disk, not `summary.totalSize`. For an App Bundle that property
            // counts the staged intermediates — it reported 983 MB for a 97 MB bundle — and
            // this is the one build where the size decides whether it can be uploaded at all.
            long bytes = File.Exists(AabPath) ? new FileInfo(AabPath).Length : 0L;

            Debug.Log($"[Glimmer] aab {s.result} - {bytes / 1048576} MB on disk, " +
                      $"version {PlayerSettings.bundleVersion} " +
                      $"(versionCode {PlayerSettings.Android.bundleVersionCode}), " +
                      $"{s.totalErrors} error(s), {s.totalWarnings} warning(s) -> {AabPath}");

            if (s.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Glimmer] App Bundle build failed; if it mentions Google Play " +
                               "Services, run Assets > External Dependency Manager > Android " +
                               "Resolver > Force Resolve");
                return;
            }

            // Said on every successful store build rather than written in a document
            // somebody has to remember to open. Play App Signing re-signs the bundle with a
            // key Google holds, so the fingerprint the app actually ships with is *not* the
            // one in the keystore above — and Google Sign-In checks the shipped one.
            Debug.LogWarning(
                "[Glimmer] before this build can sign anybody in: copy the SHA-1 from Play " +
                "Console > Setup > App integrity > App signing key certificate, and add it to " +
                "the Firebase Android app. Play re-signs the bundle with its own key, so the " +
                "debug fingerprint that works for sideloaded APKs does not apply here.");
        }

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

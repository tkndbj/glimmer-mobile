#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Links <c>AuthenticationServices.framework</c>, which nothing else does.
    ///
    /// <para>
    /// Unity copies <c>GlimmerAppleSignIn.mm</c> into the generated project but does not link
    /// the system frameworks it calls into. Without this the build fails at the very end, in
    /// the linker, with undefined symbols for <c>_OBJC_CLASS_$_ASAuthorizationAppleIDProvider</c>
    /// — after the whole of IL2CPP has compiled, and with an error naming Apple's class rather
    /// than our file. Exactly the trap <see cref="IosPrivacyPlist"/> documents for
    /// <c>AppTrackingTransparency</c>, one framework over.
    /// </para>
    /// <para>
    /// <b>Weakly linked</b>, and on the <c>UnityFramework</c> target, for that file's reasons:
    /// the framework arrived in iOS 13 and every call is behind an availability check, so a
    /// weak link degrades to the symbol being absent rather than refusing to load the binary
    /// at all; and native plugins live in <c>UnityFramework</c> in a modern generated project,
    /// where the app target is a thin shell that loads it.
    /// </para>
    /// <para>
    /// <b>It also writes the <c>com.apple.developer.applesignin</c> entitlement, and that is
    /// not redundant.</b> The first build of this project appeared to have one already, which
    /// is why an earlier version of this file deliberately did not write it — but that file had
    /// been produced by <em>Xcode</em>, when a team was selected and it offered to add the
    /// capability. Unity rewrites the whole project on every build, so it vanished on the next
    /// one, taking Apple sign-in with it and leaving nothing behind to say why. That is a step
    /// somebody has to remember after every single build, which this codebase has already
    /// learned twice is a step nobody remembers. Note the entitlement is a <b>paid-account</b>
    /// capability: a free Personal Team cannot sign it.
    /// </para>
    /// <para>
    /// The entitlement goes on the <em>main app target</em> while the framework goes on
    /// <c>UnityFramework</c>, and that split is real rather than an oversight — capabilities
    /// are a property of the thing that gets signed and shipped, and linking is a property of
    /// the binary that makes the call.
    /// </para>
    /// </summary>
    public static class IosAppleSignInBuild
    {
        // 101 so it lands beside the tracking framework rather than before it; both are
        // independent, and ordering only matters relative to the CocoaPods step below them.
        [PostProcessBuild(101)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);

            if (!File.Exists(projectPath))
            {
                Debug.LogError($"[AppleSignIn] no Xcode project at '{projectPath}'; " +
                               "AuthenticationServices will not be linked and the build will " +
                               "fail in the linker");
                return;
            }

            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(),
                                          "AuthenticationServices.framework", weak: true);

            project.WriteToFile(projectPath);

            // Written after the framework, and through ProjectCapabilityManager rather than by
            // hand, because the entitlement is only half of it: the capability also has to be
            // registered against the target or Xcode signs a binary whose entitlement nothing
            // has authorised, which fails at install time rather than at build time.
            const string EntitlementsPath = "Unity-iPhone/Unity-iPhone.entitlements";

            var capabilities = new ProjectCapabilityManager(
                projectPath, EntitlementsPath, targetGuid: project.GetUnityMainTargetGuid());

            capabilities.AddSignInWithApple();
            capabilities.WriteToFile();

            Debug.Log("[AppleSignIn] AuthenticationServices.framework linked (weak) into " +
                      "UnityFramework; Sign in with Apple entitlement written to " +
                      EntitlementsPath);
        }
    }
}
#endif

#if UNITY_IOS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Proves the generated iOS project is the one that should be opened, and says so loudly
    /// when it is not.
    ///
    /// <para>
    /// <b>Why a check rather than trust.</b> The External Dependency Manager owns
    /// <c>pod install</c> and it is the only thing that should run it — a second caller would be
    /// a second source of truth for which pods are installed, which is the mistake this codebase
    /// refuses everywhere else. But EDM4U's step is also the one most likely to fail on a fresh
    /// machine (see <see cref="MacToolPath"/>), and its failure is invisible in the place people
    /// look: the Xcode project is written, is complete, and opens. What is missing is the
    /// <c>.xcworkspace</c> — and a developer who opens <c>Unity-iPhone.xcodeproj</c> instead gets
    /// a project that compiles for twenty minutes and then fails in the linker with undefined
    /// symbols for the ad SDKs, an error that names Apple's frameworks rather than CocoaPods.
    /// So: one owner, and a proof that the owner ran. "Making an error unlikely is not the same
    /// as proving it did not happen."
    /// </para>
    /// <para>
    /// <b>Ordered last on purpose.</b> At 200 this runs after EDM4U's pod install (order 4) and
    /// after <see cref="IosPrivacyPlist"/> (order 100), so what it inspects is the finished
    /// article. Note that it can only report a step that *ran and did nothing* — Unity abandons
    /// the remaining callbacks when an earlier one throws, so a hard CocoaPods failure surfaces
    /// as that exception instead. Both are loud; neither is silent, which is the whole point.
    /// </para>
    /// </summary>
    public static class IosWorkspaceGuard
    {
        [PostProcessBuild(200)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            bool hasPodfile = File.Exists(Path.Combine(pathToBuiltProject, "Podfile"));
            string workspace = Directory
                .GetDirectories(pathToBuiltProject, "*.xcworkspace", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();

            // No Podfile means nothing declared a CocoaPods dependency, so a bare .xcodeproj is
            // the correct output and there is nothing to prove. Do not invent a warning for it.
            if (!hasPodfile)
            {
                Debug.Log("[iOS] no Podfile generated; open Unity-iPhone.xcodeproj");
                return;
            }

            if (workspace == null)
            {
                Debug.LogError(
                    "[iOS] a Podfile was generated but no .xcworkspace exists, so CocoaPods did " +
                    "not finish. Opening Unity-iPhone.xcodeproj will compile and then fail in " +
                    "the linker with undefined symbols for the ad SDKs.\n" +
                    "Repair: check that 'pod' is on the Editor's PATH " +
                    "(Glimmer Grove ▸ Diagnostics ▸ Report Tool Path), then either rebuild or " +
                    $"run 'pod install' in '{pathToBuiltProject}'.");
                return;
            }

            string pods = Path.Combine(pathToBuiltProject, "Pods");
            int installed = Directory.Exists(pods)
                ? Directory.GetDirectories(pods)
                    .Count(d => !Path.GetFileName(d).StartsWith("Target Support Files") &&
                                !Path.GetFileName(d).StartsWith("Local Podspecs") &&
                                !Path.GetFileName(d).StartsWith("Headers"))
                : 0;

            if (installed == 0)
            {
                Debug.LogError(
                    $"[iOS] '{Path.GetFileName(workspace)}' exists but no pods were installed. " +
                    "The ad SDKs will be missing at link time. Repair: run 'pod install' in " +
                    $"'{pathToBuiltProject}'.");
                return;
            }

            Debug.Log($"[iOS] open {Path.GetFileName(workspace)} — {installed} pods installed");
        }
    }
}
#endif

#if UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Puts the Homebrew and RubyGems binary directories on the Editor process's
    /// <c>PATH</c>, so the tools an iOS build shells out to can actually be found.
    ///
    /// <para>
    /// <b>The failure this exists to stop is silent, total, and looks like a Unity bug.</b>
    /// An application launched from Finder, the Dock or Unity Hub does not inherit a login
    /// shell's environment — macOS hands it a minimal <c>PATH</c>, measured here as
    /// <c>/usr/bin:/bin:/usr/sbin:/sbin</c>. Homebrew on Apple Silicon installs to
    /// <c>/opt/homebrew/bin</c>, which is on none of those, and the External Dependency
    /// Manager's iOS resolver searches the process <c>PATH</c> plus <c>/usr/local/bin</c> —
    /// the *Intel* Homebrew prefix. So on every Apple Silicon Mac, EDM4U cannot find
    /// <c>pod</c> even though CocoaPods is installed and works perfectly in a terminal.
    /// </para>
    /// <para>
    /// <b>What that costs is not one missing step but the rest of the chain.</b> EDM4U runs
    /// <c>pod install</c> from a <c>[PostProcessBuild]</c> callback at order 4. Unity aborts
    /// the remaining callbacks when one throws, so the failure takes down every post-processor
    /// ordered after it — here that is <see cref="IosPrivacyPlist"/> at order 100, which is the
    /// only writer of <c>NSUserTrackingUsageDescription</c> and the only thing that links
    /// <c>AppTrackingTransparency.framework</c>. The observable result is an Xcode project that
    /// exists and looks complete, with no <c>.xcworkspace</c>, no linked ad SDKs, no tracking
    /// prompt and a link error at the very end of a twenty-minute Xcode build. None of it names
    /// CocoaPods.
    /// </para>
    /// <para>
    /// <b>Why this is a process-environment fix and not a symlink or a setting.</b> Symlinking
    /// <c>pod</c> into <c>/usr/local/bin</c> needs <c>sudo</c>, fixes one machine, and is exactly
    /// the undocumented manual step this project has twice learned gets forgotten. Enabling
    /// EDM4U's "execute via shell" reads a login shell's profile, which on this machine sets no
    /// <c>PATH</c> at all — it would work by luck and break on the next clone. Setting the
    /// variable in-process is deterministic, needs no privileges, is inherited by every child
    /// process the build spawns, and travels with the repository, so a teammate or a CI runner
    /// gets the same answer as this Mac.
    /// </para>
    /// <para>
    /// It is deliberately additive and idempotent: entries already present are not duplicated,
    /// directories that do not exist are not added, and nothing is ever removed — so a machine
    /// that already has a working <c>PATH</c> is left exactly as it was.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class MacToolPath
    {
        /// <summary>
        /// Where the tools an iOS build needs are actually installed, most specific first.
        ///
        /// Both Homebrew prefixes are listed because the correct one is a fact about the
        /// machine's architecture rather than about this project: Apple Silicon uses
        /// <c>/opt/homebrew</c> and Intel uses <c>/usr/local</c>, and a checkout is expected to
        /// build on either. The gem directories cover a CocoaPods installed with
        /// <c>gem install cocoapods</c> instead of Homebrew, which is the other common route and
        /// the one Apple's own documentation gives.
        /// </summary>
        static readonly string[] Candidates =
        {
            "/opt/homebrew/bin",
            "/opt/homebrew/sbin",
            "/usr/local/bin",
            "/usr/local/sbin",
        };

        static MacToolPath()
        {
            Apply();
        }

        /// <summary>
        /// Prepends every candidate directory that exists and is not already on <c>PATH</c>.
        ///
        /// <para>
        /// Prepended rather than appended, because the point is to win: a stale shim earlier on
        /// the path is the thing that would otherwise be picked, and a tool that is found but is
        /// the wrong one fails further from its cause than a tool that is not found at all.
        /// </para>
        /// </summary>
        public static void Apply()
        {
            string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            var have = new HashSet<string>(
                current.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

            var missing = new List<string>();
            foreach (string dir in Candidates)
            {
                if (have.Contains(dir)) continue;
                if (!Directory.Exists(dir)) continue;
                missing.Add(dir);
            }

            // Also honour a user gem prefix, which varies by Ruby version and so cannot be a
            // constant. `gem install --user-install cocoapods` is what somebody without
            // Homebrew ends up running, and it lands here.
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                string gems = Path.Combine(home, ".gem/ruby");
                if (Directory.Exists(gems))
                {
                    foreach (string version in Directory.GetDirectories(gems))
                    {
                        string bin = Path.Combine(version, "bin");
                        if (!Directory.Exists(bin)) continue;
                        if (have.Contains(bin)) continue;
                        missing.Add(bin);
                    }
                }
            }

            if (missing.Count == 0) return;

            Environment.SetEnvironmentVariable("PATH", string.Join(":", missing) + ":" + current);
        }

        /// <summary>
        /// Resolves an executable the way a shell would, for callers that want to *report* a
        /// missing tool rather than fail in a child process with an exit code and no message.
        /// </summary>
        public static string Find(string tool)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in path.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir, tool);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        [MenuItem("Glimmer Grove/Diagnostics/Report Tool Path", false, 400)]
        static void Report()
        {
            Apply();

            string pod = Find("pod");
            string verdict = pod ?? "NOT FOUND — CocoaPods cannot run, so an iOS build will " +
                                    "produce no .xcworkspace and its post-processors will not run";

            Debug.Log("[Tools] PATH = " + Environment.GetEnvironmentVariable("PATH") + "\n" +
                      "[Tools] pod  = " + verdict);
        }
    }
}
#endif

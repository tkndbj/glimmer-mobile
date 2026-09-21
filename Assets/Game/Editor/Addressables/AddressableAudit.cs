#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Proves that every address the game can ever ask for actually resolves.
    ///
    /// This is the check that turns a missing asset from a player's problem into a
    /// failed build. It runs from the menu and, more importantly, from the build gate:
    /// a chapter whose backdrop was never given an address would otherwise pass content
    /// validation, pass art validation, produce a green build, and then show a player a
    /// blank screen with a single warning in a log nobody has.
    ///
    /// The expected set is built from <see cref="AssetManifest"/> plus the catalog, so
    /// it grows by itself when a chapter is published. Nothing here is hand listed.
    /// </summary>
    public static class AddressableAudit
    {
        public sealed class Result
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> Warnings = new List<string>();

            public int Expected;
            public int Registered;

            public bool Ok => Errors.Count == 0;

            public string Summarise()
                => Ok
                    ? $"[Glimmer] all {Expected} requested address(es) resolve across {Registered} entries"
                    : $"[Glimmer] {Errors.Count} address problem(s) across {Expected} requested";
        }

        [MenuItem("Glimmer Grove/Addressables/Audit Addresses", false, 61)]
        public static void AuditMenu()
        {
            var result = Run();

            foreach (var w in result.Warnings) Debug.LogWarning("[Glimmer] " + w);
            foreach (var e in result.Errors) Debug.LogError("[Glimmer] " + e);

            if (result.Ok) Debug.Log(result.Summarise());
            else Debug.LogError(result.Summarise());
        }

        public static Result Run()
        {
            var result = new Result();

            var settings = AddressableRegistry.Settings(false);
            if (settings == null)
            {
                result.Errors.Add("no Addressables settings; run Addressables ▸ Sync All Assets");
                return result;
            }

            var content = EditorContentLoader.Load();
            var bodies = content.Bodies;

            var addresses = new HashSet<string>();
            var labels = new HashSet<string>();
            var groupOf = new Dictionary<string, string>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                // The VFX bench is asked for by a developer tool rather than by AssetManifest,
                // so every one of its entries would be counted "never requested by the game" —
                // 175 of them, drowning the one real one that warning exists to surface. Skipped
                // whole rather than filtered later, because it is not the game's art in any
                // sense: its own root, its own bundle, and switched off for every build that
                // has not asked for it (VfxBenchGroup).
                bool bench = group.Name == Dev.VfxBench.GroupName;

                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;

                    if (!bench) addresses.Add(entry.address);
                    groupOf[entry.address] = group.Name;

                    foreach (var label in entry.labels) labels.Add(label);
                }
            }

            result.Registered = addresses.Count;

            var expected = AssetManifest.GlobalAssets();

            // Loaded by the launch screen into a scope of its own rather than preloaded, so it
            // is requested by the game and absent from the global list. See SplashAssets.
            expected.AddRange(AssetManifest.SplashAssets());
            expected.AddRange(AssetManifest.AllChapterAssets(bodies));

            // Companion portraits are requested by a scope rather than at boot, which
            // makes them exactly the kind of asset an audit built only from the global
            // and chapter sets would call unused — and then fail to notice when one went
            // missing. Read from the roster, so a companion added by a content drop is
            // audited without anyone editing this.
            expected.AddRange(AssetManifest.CompanionAssets(content.Index.Companions));

            // The turret roster, for the companions' reason with a sharper edge. A run loads the four
            // a player stood on the line and the shelf loads twenty thumbnails, so an audit built
            // only from the global and chapter sets would call eighty sprites unused - and then
            // say nothing at all when one went missing, which draws a white rectangle two cells
            // tall on the object a player looks at for a whole run.
            expected.AddRange(AssetManifest.AllWardAssets(ProgressionRules.Table.Wards));

            // The chest reels, requested by the screens that open one and by nothing at boot,
            // so an audit built without them would call sixty-eight frames unused and then say
            // nothing when a tier's reel went missing — which is a white rectangle over the
            // one ceremony in the game that is entirely a picture.
            expected.AddRange(AssetManifest.ChestAssets(ProgressionRules.Table.Tasks));

            result.Expected = expected.Count;

            foreach (var request in expected)
            {
                bool resolves = request.Kind == AssetKind.SpriteSet
                    ? labels.Contains(request.Address)
                    : addresses.Contains(request.Address);

                if (!resolves)
                    result.Errors.Add($"nothing is addressed '{request.Address}' ({request.Kind}), " +
                                      "which the game requests; run Addressables ▸ Sync All Assets");
            }

            var wanted = new HashSet<string>();
            foreach (var request in expected)
                if (request.Kind == AssetKind.SpriteSet) wanted.Add(request.Address);

            CheckFramesAreLabelled(settings, wanted, result);
            CheckEntriesStillExist(settings, result);
            CheckGrouping(expected, bodies, groupOf, result);
            CheckUnreachable(addresses, expected, result);

            return result;
        }

        /// <summary>
        /// Every registered entry still points at an asset that is really there.
        ///
        /// <para>
        /// <b>This is the half the audit was missing, and it cost an Android build.</b>
        /// Everything above proves that what the game <em>requests</em> resolves — which says
        /// nothing whatever about an entry pointing at a file that has been deleted, because
        /// nothing requests it any more. The game does not care. The bundle builder does:
        /// <c>BundleBuildContent</c> throws <c>Asset '…' is not a valid Asset or Scene</c> the
        /// moment it walks the group, so the build dies in <c>BuildPlayer</c> before a line of
        /// the player is written, and the only clue is one file name in a stack trace full of
        /// package internals.
        /// </para>
        /// <para>
        /// It happened by deleting a folder of art the game had stopped drawing. Every other
        /// gate in this repository stayed green — the compile, the tests, <c>Validate Content</c>,
        /// <c>Validate Art</c> and this audit's own 484 resolving addresses — because all of them
        /// look outward from what the game asks for, and this is the one question that has to be
        /// asked from the other end. An <b>error</b> rather than a warning, for
        /// <c>ManifestSync.SurvivesRoundTrip</c>'s reason: a warning printed beside a green tick
        /// is a warning nobody reads, and this one is the difference between a build and no
        /// build.
        /// </para>
        /// </summary>
        static void CheckEntriesStillExist(AddressableAssetSettings settings, Result result)
        {
            var gone = new List<string>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                // The VFX bench's pack is gitignored - 199MB of licensed particle art that no
                // player build contains - so a fresh clone has the group and not the assets. That
                // is not a fault while the bundle is switched off: nothing builds it, so nothing
                // can throw "is not a valid Asset or Scene". With the bundle switched *on* it is
                // the same fault as any other and is reported the same way, which is what tells
                // somebody who set GLIMMER_BENCH that they still have to import the pack.
                if (group.Name == Dev.VfxBench.GroupName && !BenchIsBuilt(group)) continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;

                    string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (AddressableRegistry.StillThere(path)) continue;

                    gone.Add(string.IsNullOrEmpty(path)
                             ? $"'{entry.address}' (in {group.Name})"
                             : $"'{entry.address}' -> {path}");
                }
            }

            if (gone.Count == 0) return;

            // Named rather than counted, up to a handful: the build's own message names exactly
            // one of them, so the whole list is the thing this adds.
            int shown = gone.Count < 6 ? gone.Count : 6;
            string names = string.Join(", ", gone.GetRange(0, shown));
            string more = gone.Count > shown ? $" and {gone.Count - shown} more" : string.Empty;

            result.Errors.Add(
                $"{gone.Count} addressed asset(s) no longer exist and will fail the bundle " +
                $"build with \"is not a valid Asset or Scene\": {names}{more}. " +
                "Run Addressables ▸ Sync All Assets");
        }

        /// <summary>
        /// Every frame of a reel must carry its folder's label, or the reel cannot be loaded.
        ///
        /// <para>
        /// <b>This asks about what is <em>registered</em> rather than about what is requested, and
        /// that is the whole point of it.</b> Everything else here walks the manifest and proves
        /// the game's own requests resolve — which is exactly blind to a reel the manifest does not
        /// happen to ask for in the Editor. A sprite set has no notion of a folder: it is loaded by
        /// the <em>label</em> its frames share, so frames addressed with no label are addressed,
        /// grouped, built into a bundle and completely unloadable.
        /// </para>
        /// <para>
        /// Measured: the second siege cast came back labelless because the mode hands out a
        /// different cast depending on which chapter asks and the Editor's catalog index had no
        /// answer, so twelve reels shipped addressed, audited <b>green</b>, and reached a device as
        /// raiders with no body and a health bar floating where each should have been —
        /// <c>No Location found for Key=Art/Siege/boneBrute_b</c>, twelve times. The root was fixed
        /// in <c>AddressableAddresses.FrameFolders</c>; this is what says so if it happens again.
        /// </para>
        /// </summary>
        static void CheckFramesAreLabelled(AddressableAssetSettings settings,
                                           HashSet<string> wanted, Result result)
        {
            var bare = new SortedSet<string>();

            foreach (var group in settings.groups)
            {
                if (group == null || group.Name == Dev.VfxBench.GroupName) continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;

                    int cut = entry.address.LastIndexOf('/');
                    if (cut <= 0) continue;

                    string frame = entry.address.Substring(cut + 1);
                    if (frame.Length != 3 || frame[0] != 'f'
                        || !char.IsDigit(frame[1]) || !char.IsDigit(frame[2])) continue;

                    string folder = entry.address.Substring(0, cut);
                    if (!entry.labels.Contains(folder)) bare.Add(folder);
                }
            }

            // **An error when the game asks for it and a warning when it does not**, which is the
            // difference between a feature that cannot draw and a reel nobody deletes. The second
            // is real and worth saying — an unloadable folder is still built into a bundle — but
            // failing a build over art nothing requests would be obstructive.
            foreach (var folder in bare)
            {
                string why = $"'{folder}' is addressed frame by frame but its frames carry no " +
                             "label, so nothing can load it as a reel; it is missing from " +
                             "AddressableAddresses.FrameFolders";

                if (wanted.Contains(folder)) result.Errors.Add(why);
                else result.Warnings.Add(why + " (nothing requests it, so it is dead weight)");
            }
        }

        /// <summary>Whether a group's bundle is actually included in this build.</summary>
        static bool BenchIsBuilt(AddressableAssetGroup group)
        {
            var schema = group.GetSchema<UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema>();
            return schema != null && schema.IncludeInBuild;
        }

        /// <summary>
        /// Chapter art must sit in its own chapter's group, and shared art in the global
        /// one. Getting this wrong costs nothing locally and a whole extra bundle
        /// download once chapters are delivered remotely, which is exactly the kind of
        /// mistake that is invisible until it is expensive.
        /// </summary>
        static void CheckGrouping(List<AssetRequest> expected, IReadOnlyList<ChapterBody> bodies,
                                  Dictionary<string, string> groupOf, Result result)
        {
            var ownership = AddressableAddresses.ChapterOwnership(bodies);

            foreach (var request in expected)
            {
                if (!groupOf.TryGetValue(request.Address, out string actual)) continue;

                string wanted = AddressableAddresses.GroupFor(request.Address, ownership);
                if (actual == wanted) continue;

                result.Warnings.Add($"'{request.Address}' is in group '{actual}' but belongs in " +
                                    $"'{wanted}'; run Addressables ▸ Sync All Assets");
            }
        }

        /// <summary>
        /// An address nothing asks for is dead weight in a bundle. A warning rather than
        /// an error, because authoring art before the chapter that uses it is a
        /// legitimate order to work in.
        /// </summary>
        static void CheckUnreachable(HashSet<string> addresses, List<AssetRequest> expected, Result result)
        {
            var wanted = new HashSet<string>();

            foreach (var request in expected)
            {
                wanted.Add(request.Address);

                // A frame folder is requested as one address but registered as many.
                if (request.Kind == AssetKind.SpriteSet)
                    foreach (var address in addresses)
                        if (address.StartsWith(request.Address + "/", System.StringComparison.Ordinal))
                            wanted.Add(address);
            }

            int unreachable = 0;
            foreach (var address in addresses)
                if (!wanted.Contains(address)) unreachable++;

            if (unreachable > 0)
                result.Warnings.Add($"{unreachable} addressed asset(s) are never requested by the game; " +
                                    "they will still be built into a bundle");
        }
    }
}

#endif

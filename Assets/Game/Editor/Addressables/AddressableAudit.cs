#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
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

            // The grove, for the same reason and with a sharper edge: its art is requested by
            // a scope on one screen, and it is the set that grows at every content drop. An
            // audit blind to it would call a new decor piece unused and then say nothing when
            // somebody shipped a manifest row whose sprite never made it into the project —
            // which draws a white rectangle, because that is what an Image with no sprite is.
            expected.AddRange(AssetManifest.AllGroveAssets(content.Homestead));

            // The browse atlases, which are generated rather than authored — so the failure
            // they guard against is not a missing file but a step nobody ran. A shop whose
            // atlas was never rebuilt draws a grid of nothing, and it draws it only on the
            // device, because in the Editor the previous atlas is still sitting there.
            expected.AddRange(AssetManifest.AllBrowseAtlases());

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

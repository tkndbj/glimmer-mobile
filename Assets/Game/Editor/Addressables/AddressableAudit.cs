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

                foreach (var entry in group.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.address)) continue;

                    addresses.Add(entry.address);
                    groupOf[entry.address] = group.Name;

                    foreach (var label in entry.labels) labels.Add(label);
                }
            }

            result.Registered = addresses.Count;

            var expected = AssetManifest.GlobalAssets();
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

            CheckGrouping(expected, bodies, groupOf, result);
            CheckUnreachable(addresses, expected, result);

            return result;
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

// Guarded on the package rather than on GLIMMER_ADDRESSABLES: this tool has to run
// *before* that define is switched on, since its whole job is to prepare the assets
// the define assumes already exist.
#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using System.IO;
using System.Text;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Moves the project off <c>Resources/</c> and onto Addressables.
    ///
    /// Done by hand this is 276 checkboxes and 276 typed addresses, which is a
    /// guaranteed source of one silent typo that only shows up as a missing sprite on
    /// somebody's phone. The rule is mechanical — an asset's address is its old
    /// Resources path — so it is done here instead, and then verified against the
    /// addresses the game actually asks for.
    ///
    /// Run the three steps in order from the Glimmer Grove menu.
    /// </summary>
    public static class AddressablesMigration
    {
        const string ResourcesRoot = "Assets/Game/Resources";

        /// <summary>
        /// Where the asset folders land. The folders keep their own names, so this is
        /// their new parent — <c>Assets/Game/Art</c>, <c>.../Audio</c>, <c>.../Fonts</c>.
        /// It already exists, which is deliberate: nothing here needs to create a
        /// folder, and creating one mid-move is what broke the first attempt.
        /// </summary>
        const string DestinationRoot = "Assets/Game";

        const string GlobalGroup = "Glimmer Global";
        const string ChapterGroupPrefix = "Glimmer Chapter ";

        /// <summary>
        /// Folders the game loads as a set via <c>LoadAll</c>. Addressables has no
        /// notion of a folder, so each of these becomes a label shared by its frames,
        /// and the label string is exactly the address the code already asks for.
        /// </summary>
        static readonly string[] FrameFolders =
        {
            "Art/Critters/c1", "Art/Critters/c2", "Art/Critters/c3",
            "Art/Critters/c4", "Art/Critters/c5",
            "Art/Fx/Victory", "Art/Ui/Coin",
        };

        // ================================================================ step 1
        [MenuItem("Glimmer Grove/Addressables/1 - Mark Assets Addressable", false, 60)]
        public static void MarkAssets()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogError("[Glimmer] could not create Addressables settings");
                return;
            }

            var chapterOf = BuildChapterOwnership();
            var global = EnsureGroup(settings, GlobalGroup);

            int marked = 0, labelled = 0;
            foreach (var (assetPath, address) in EnumerateResources())
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                // Chapter art gets its own group so it bundles — and downloads — as a unit.
                var group = chapterOf.TryGetValue(address, out var chapterId)
                    ? EnsureGroup(settings, ChapterGroupPrefix + chapterId)
                    : global;

                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null) continue;

                entry.address = address;
                marked++;

                string frameLabel = FrameLabelFor(address);
                if (frameLabel != null)
                {
                    settings.AddLabel(frameLabel, false);
                    entry.SetLabel(frameLabel, true, true, false);
                    labelled++;
                }
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Glimmer] marked {marked} asset(s) addressable, {labelled} tagged as animation frames.\n" +
                      "Next: Glimmer Grove > Addressables > 2 - Verify Addresses");
        }

        // ================================================================ step 2
        [MenuItem("Glimmer Grove/Addressables/2 - Verify Addresses", false, 61)]
        public static void VerifyAddresses()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
            if (settings == null)
            {
                Debug.LogError("[Glimmer] no Addressables settings; run step 1 first");
                return;
            }

            var addresses = new HashSet<string>();
            var labels = new HashSet<string>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    addresses.Add(entry.address);
                    foreach (var label in entry.labels) labels.Add(label);
                }
            }

            // The contract being checked: every address the game will ever ask for
            // must resolve. This is the step that catches a typo before a player does.
            var missing = new List<string>();
            foreach (var request in ExpectedRequests())
            {
                bool ok = request.Kind == AssetKind.SpriteSet
                    ? labels.Contains(request.Address)
                    : addresses.Contains(request.Address);

                if (!ok) missing.Add($"{request.Kind}: {request.Address}");
            }

            if (missing.Count == 0)
            {
                Debug.Log($"[Glimmer] all {addresses.Count} address(es) present; every asset the game " +
                          "requests resolves.\nNext: Glimmer Grove > Addressables > 3 - Move Out Of Resources");
                return;
            }

            var sb = new StringBuilder($"[Glimmer] {missing.Count} requested asset(s) have no address:\n");
            foreach (var m in missing) sb.Append("  ").AppendLine(m);
            Debug.LogError(sb.ToString());
        }

        // ================================================================ step 3
        [MenuItem("Glimmer Grove/Addressables/3 - Move Out Of Resources", false, 62)]
        public static void MoveOutOfResources()
        {
            if (!Directory.Exists(ResourcesRoot))
            {
                Debug.Log("[Glimmer] nothing left under Resources");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Move assets out of Resources",
                    "Addressable entries follow an asset's GUID, so addresses survive the move.\n\n" +
                    "Run step 2 first and make sure it passed.",
                    "Move", "Cancel"))
                return;

            // Refuse rather than merge if a destination is somehow already there.
            foreach (var folder in AssetFolders)
            {
                string to = $"{DestinationRoot}/{folder}";
                if (AssetDatabase.IsValidFolder($"{ResourcesRoot}/{folder}") && AssetDatabase.IsValidFolder(to))
                {
                    Debug.LogError($"[Glimmer] {to} already exists; move or delete it first");
                    return;
                }
            }

            // Addresses are stored explicitly and entries follow an asset's GUID, so
            // moving the files cannot change them. No StartAssetEditing here: the
            // database has to stay live for MoveAsset to resolve the destination.
            int moved = 0;
            foreach (var folder in AssetFolders)
            {
                string from = $"{ResourcesRoot}/{folder}";
                if (!AssetDatabase.IsValidFolder(from)) continue;

                string error = AssetDatabase.MoveAsset(from, $"{DestinationRoot}/{folder}");
                if (string.IsNullOrEmpty(error)) moved++;
                else Debug.LogError($"[Glimmer] could not move {from}: {error}");
            }

            // An empty Resources folder still makes Unity build a Resources index.
            if (moved > 0 && AssetDatabase.IsValidFolder(ResourcesRoot) && IsFolderEmpty(ResourcesRoot))
                AssetDatabase.DeleteAsset(ResourcesRoot);

            AssetDatabase.Refresh();

            Debug.Log($"[Glimmer] moved {moved} folder(s) out of Resources.\n" +
                      "Final step: add GLIMMER_ADDRESSABLES to Player Settings > Scripting Define Symbols.");
        }

        static readonly string[] AssetFolders = { "Art", "Audio", "Fonts" };

        static bool IsFolderEmpty(string folder)
            => AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0;

        // =============================================================== helpers
        /// <summary>Every asset under Resources, paired with the address it will take.</summary>
        static IEnumerable<(string assetPath, string address)> EnumerateResources()
        {
            if (!Directory.Exists(ResourcesRoot)) yield break;

            var guids = AssetDatabase.FindAssets("t:Texture2D t:AudioClip t:Font", new[] { ResourcesRoot });
            var seen = new HashSet<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (path.EndsWith(".meta")) continue;

                yield return (path, AddressFor(path));
            }
        }

        /// <summary>"Assets/Game/Resources/Art/Ui/x.png" becomes "Art/Ui/x".</summary>
        static string AddressFor(string assetPath)
        {
            string relative = assetPath.Substring(ResourcesRoot.Length + 1);
            int dot = relative.LastIndexOf('.');
            if (dot > 0) relative = relative.Substring(0, dot);
            return relative.Replace('\\', '/');
        }

        static string FrameLabelFor(string address)
        {
            foreach (var folder in FrameFolders)
                if (address.StartsWith(folder + "/", System.StringComparison.Ordinal)) return folder;
            return null;
        }

        /// <summary>Maps each chapter-owned address to its chapter, read from the catalog.</summary>
        static Dictionary<string, string> BuildChapterOwnership()
        {
            var map = new Dictionary<string, string>();
            var catalog = EditorContentLoader.Load().Catalog;

            foreach (var chapter in catalog.Chapters)
                foreach (var request in AssetManifest.ChapterAssets(chapter, catalog))
                    map[request.Address] = chapter.Id.Value;

            return map;
        }

        /// <summary>Everything the game can ask for, global chrome plus every chapter.</summary>
        static List<AssetRequest> ExpectedRequests()
        {
            var catalog = EditorContentLoader.Load().Catalog;
            var list = AssetManifest.GlobalAssets();
            list.AddRange(AssetManifest.AllChapterAssets(catalog));
            return list;
        }

        static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name)
        {
            var group = settings.FindGroup(name);
            if (group != null) return group;

            return settings.CreateGroup(name, false, false, false, null,
                                        typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

    }
}

#endif

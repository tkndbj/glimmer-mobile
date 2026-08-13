// Guarded on the package rather than on GLIMMER_ADDRESSABLES: this tooling has to
// work whether or not the runtime define is switched on, because its job is to keep
// the asset database in the state that define assumes.
#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Writes the address convention into the Addressables settings.
    ///
    /// One asset at a time, so the importer hook and the full repair sweep run exactly
    /// the same code — the sweep is just this in a loop. Nothing here decides *what* an
    /// address should be; that is <see cref="AddressableAddresses"/>.
    /// </summary>
    public static class AddressableRegistry
    {
        /// <summary>What a registration pass did, for the caller to report.</summary>
        public struct Summary
        {
            public int Registered;
            public int Moved;
            public int Relabelled;
            public int Removed;

            public bool Changed => Registered + Moved + Relabelled + Removed > 0;

            public override string ToString()
                => $"{Registered} registered, {Moved} regrouped, {Relabelled} relabelled, {Removed} removed";
        }

        public static AddressableAssetSettings Settings(bool create)
            => AddressableAssetSettingsDefaultObject.GetSettings(create);

        /// <summary>
        /// Files one asset: correct address, correct group, correct frame label.
        ///
        /// Idempotent, which is what lets it be called from an import hook on every
        /// reimport without churning the settings asset — an asset already in the right
        /// place reports no change and dirties nothing.
        /// </summary>
        public static void Register(AddressableAssetSettings settings, string assetPath,
                                    Dictionary<string, ChapterId> ownership,
                                    HashSet<string> frameFolders,
                                    ref Summary summary)
        {
            if (settings == null) return;
            if (!AddressableAddresses.TryAddressFor(assetPath, out string address)) return;

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) return;

            string groupName = AddressableAddresses.GroupFor(address, ownership);
            var group = EnsureGroup(settings, groupName);
            if (group == null) return;

            var existing = settings.FindAssetEntry(guid);
            bool isNew = existing == null;
            bool regrouped = !isNew && existing.parentGroup != group;

            var entry = settings.CreateOrMoveEntry(guid, group, false, false);
            if (entry == null) return;

            if (entry.address != address) entry.SetAddress(address, false);

            if (isNew) summary.Registered++;
            else if (regrouped) summary.Moved++;

            ApplyFrameLabel(settings, entry, address, frameFolders, ref summary);
        }

        /// <summary>
        /// A frame folder becomes a label, because Addressables has no folders. The
        /// label is removed again if the folder stops being one, so renaming an
        /// animation folder cannot leave a stale label that quietly loads the wrong set.
        /// </summary>
        static void ApplyFrameLabel(AddressableAssetSettings settings, AddressableAssetEntry entry,
                                    string address, HashSet<string> frameFolders, ref Summary summary)
        {
            string wanted = AddressableAddresses.FrameLabelFor(address, frameFolders);

            var stale = new List<string>();
            foreach (var label in entry.labels)
                if (frameFolders.Contains(label) && label != wanted) stale.Add(label);

            foreach (var label in stale)
            {
                entry.SetLabel(label, false, false, false);
                summary.Relabelled++;
            }

            if (wanted == null || entry.labels.Contains(wanted)) return;

            settings.AddLabel(wanted, false);
            entry.SetLabel(wanted, true, true, false);
            summary.Relabelled++;
        }

        /// <summary>Drops an entry for an asset that no longer exists.</summary>
        public static void Remove(AddressableAssetSettings settings, string guid, ref Summary summary)
        {
            if (settings == null || string.IsNullOrEmpty(guid)) return;
            if (settings.FindAssetEntry(guid) == null) return;

            settings.RemoveAssetEntry(guid, false);
            summary.Removed++;
        }

        public static AddressableAssetGroup EnsureGroup(AddressableAssetSettings settings, string name)
        {
            var group = settings.FindGroup(name);
            if (group != null) return group;

            return settings.CreateGroup(name, false, false, false, null,
                                        typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        }

        /// <summary>
        /// Removes chapter groups for chapters that no longer exist, so retiring a
        /// chapter does not leave an empty bundle definition behind forever.
        /// </summary>
        public static void PruneEmptyChapterGroups(AddressableAssetSettings settings, ref Summary summary)
        {
            var doomed = new List<AddressableAssetGroup>();

            foreach (var group in settings.groups)
            {
                if (group == null || group.entries.Count > 0) continue;
                if (!group.Name.StartsWith(AddressableAddresses.ChapterGroupPrefix,
                                           System.StringComparison.Ordinal)) continue;
                doomed.Add(group);
            }

            foreach (var group in doomed)
            {
                settings.RemoveGroup(group);
                summary.Removed++;
            }
        }

        /// <summary>Every asset under the managed folders, whether registered yet or not.</summary>
        public static IEnumerable<string> EnumerateManagedAssets()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D t:AudioClip t:Font",
                                                 new[] { "Assets/Game" });
            var seen = new HashSet<string>();

            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (!AddressableAddresses.IsManaged(path)) continue;

                yield return path;
            }
        }

        public static void Commit(AddressableAssetSettings settings, Summary summary)
        {
            if (!summary.Changed) return;

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
        }

        internal static void Log(string prefix, Summary summary)
            => Debug.Log($"[Glimmer] {prefix}: {summary}");
    }
}

#endif

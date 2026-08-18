#if GLIMMER_HAS_ADDRESSABLES

using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Re-files every managed asset from scratch.
    ///
    /// The importer hook keeps the project correct as it changes; this puts it right
    /// when something has happened the hook could not see — a merge that brought in a
    /// settings file, an asset added while the package was missing, or a chapter whose
    /// backdrop moved from one chapter to another and so changed which group its art
    /// belongs in. It is safe to run at any time and does nothing when nothing is wrong.
    ///
    /// This is also what replaced the old three-step migration. Those steps described a
    /// journey off <c>Resources/</c> that has since been completed, which left step one
    /// scanning a folder that no longer existed — a repair tool that silently did
    /// nothing, in a project whose whole asset story depends on it.
    /// </summary>
    public static class AddressableSync
    {
        [MenuItem("Glimmer Grove/Addressables/Sync All Assets", false, 60)]
        public static void SyncMenu()
        {
            var summary = Run();
            AddressableRegistry.Log("addressables synced", summary);

            var audit = AddressableAudit.Run();
            foreach (var w in audit.Warnings) Debug.LogWarning("[Glimmer] " + w);
            foreach (var e in audit.Errors) Debug.LogError("[Glimmer] " + e);

            Debug.Log(audit.Summarise());
        }

        public static AddressableRegistry.Summary Run()
        {
            var summary = new AddressableRegistry.Summary();

            var settings = AddressableRegistry.Settings(true);
            if (settings == null)
            {
                Debug.LogError("[Glimmer] could not create Addressables settings");
                return summary;
            }

            var bodies = AddressableAutoRegister.SafeChapterBodies();
            var ownership = AddressableAddresses.ChapterOwnership(bodies);
            var frameFolders = AddressableAddresses.FrameFolders(bodies, AddressableAutoRegister.SafeHomestead());

            // The hook would otherwise see this pass's own writes and redo the work.
            AddressableAutoRegister.Suspended = true;
            try
            {
                foreach (var path in AddressableRegistry.EnumerateManagedAssets())
                    AddressableRegistry.Register(settings, path, ownership, frameFolders, ref summary);

                DropMissing(settings, ref summary);
                AddressableRegistry.PruneEmptyChapterGroups(settings, ref summary);
            }
            finally
            {
                AddressableAutoRegister.Suspended = false;
            }

            AddressableRegistry.Commit(settings, summary);
            return summary;
        }

        /// <summary>
        /// Drops entries whose asset has gone, or which now live outside the managed
        /// folders. Without this an asset deleted while the package was uninstalled
        /// would leave an entry that resolves to nothing at runtime.
        /// </summary>
        static void DropMissing(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings,
                                ref AddressableRegistry.Summary summary)
        {
            var doomed = new System.Collections.Generic.List<string>();

            foreach (var group in settings.groups)
            {
                if (group == null) continue;

                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;

                    string path = AssetDatabase.GUIDToAssetPath(entry.guid);
                    if (!string.IsNullOrEmpty(path) && AddressableAddresses.IsManaged(path)) continue;

                    doomed.Add(entry.guid);
                }
            }

            foreach (var guid in doomed) AddressableRegistry.Remove(settings, guid, ref summary);
        }
    }
}

#endif

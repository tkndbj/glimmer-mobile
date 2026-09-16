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

                // The bench's pack is outside the managed folders on purpose, so the loop above
                // cannot see it and the importer hook never will either.
                //
                // **And it is swept only when this build actually wants the bench**, which is the
                // one place in this file that deliberately does *less* than it can. The pack is
                // gitignored, exactly as the CraftPix packs are - so on a machine that has
                // imported it an unconditional sweep files a hundred and seventy-five entries
                // pointing at assets nobody else has into a **tracked** group asset. A clone then
                // gets a group full of missing references, which is the failure this project
                // already knows by heart: a dead Addressables entry fails `BuildPlayer` rather
                // than the game.
                //
                // Nothing has to be remembered for it, which is what that rule is really about
                // (invariant 7a): `VfxBenchGroup.Gate` syncs the bench at build time when the
                // define is on, so a bench build cannot ship an empty bundle, and
                // `Addressables > Sync VFX Bench` is there for working on it interactively.
                if (VfxBenchGroup.WantedBy(EditorUserBuildSettings.selectedBuildTargetGroup))
                    VfxBenchGroup.Sync(settings, ref summary);
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
        ///
        /// <para>
        /// <b>"The GUID maps to a managed path" is not the same as "the asset is there", and
        /// the difference is a failed build.</b> This used to ask only the first question, and
        /// <c>AssetDatabase.GUIDToAssetPath</c> keeps answering with the old path for a while
        /// after a file has gone — so a folder of art deleted outside the Editor left every one
        /// of its entries in place, each pointing at a path that looked perfectly managed. The
        /// game did not care, because nothing requested them any more, and
        /// <c>AddressableAudit</c> did not care, because it only proved that everything
        /// <em>requested</em> resolves. The bundle builder cared: <c>BundleBuildContent</c>
        /// throws <c>Asset '…' is not a valid Asset or Scene</c> and the Android build dies
        /// before a single line of the player is written. Twenty-five dead frames of a deleted
        /// flipbook cost exactly that.
        /// </para>
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

                    // **A folder entry is dropped even though the folder is still there**, and it
                    // is the one case this sweep could not repair any other way. A frame folder is
                    // addressed by a *label* on its frames (`AddressableAddresses.FrameFolders`),
                    // so a folder entry carrying the same address is redundant - and worse than
                    // redundant: `EnumerateManagedAssets` walks textures rather than folders, so
                    // `Register` never sees one, and a stale folder entry can therefore sit in a
                    // chapter group for ever while the audit reports it belongs somewhere else,
                    // with no run of this tool able to move it. It also pulls every frame under it
                    // into that bundle a second time.
                    if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path)
                        && AddressableAddresses.IsManaged(path))
                    {
                        doomed.Add(entry.guid);
                        continue;
                    }

                    if (!string.IsNullOrEmpty(path)
                        && AddressableAddresses.IsManaged(path)
                        && AddressableRegistry.StillThere(path)) continue;

                    doomed.Add(entry.guid);
                }
            }

            foreach (var guid in doomed) AddressableRegistry.Remove(settings, guid, ref summary);
        }
    }
}

#endif

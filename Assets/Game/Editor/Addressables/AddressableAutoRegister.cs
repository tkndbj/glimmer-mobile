#if GLIMMER_HAS_ADDRESSABLES

using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEditor;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Gives every new art, audio and font asset its address the moment it is imported.
    ///
    /// This exists because the previous arrangement was a menu item, and a menu item is
    /// a thing a person has to remember on the week a chapter ships. It was forgotten
    /// once already in this project's history — the splash screen used to hardcode
    /// "play_0, play_1, play_2" and every content drop needed someone to edit a screen.
    /// Marking assets by hand is the same failure wearing different clothes: the file
    /// imports, the content validates, the build succeeds, and the chapter ships with
    /// no backdrop and one warning nobody reads.
    ///
    /// An importer hook cannot be forgotten. It runs on a fresh checkout, on a
    /// <c>git pull</c> with the Editor closed (Unity imports on focus), and on a
    /// drag-and-drop from the artist. The build-time audit still exists and still
    /// fails the build, because a mechanism that makes an error unlikely is not the
    /// same as a proof that it did not happen.
    /// </summary>
    public sealed class AddressableAutoRegister : AssetPostprocessor
    {
        /// <summary>
        /// Set while the repair sweep is running, so its own writes do not come back
        /// through here and do the same work a second time.
        /// </summary>
        internal static bool Suspended;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                           string[] movedTo, string[] movedFrom)
        {
            if (Suspended) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            bool touchesUs = AnyManaged(imported) || AnyManaged(movedTo)
                          || AnyManaged(movedFrom) || AnyManaged(deleted);
            if (!touchesUs) return;

            var settings = AddressableRegistry.Settings(false);
            if (settings == null) return;   // project has not been set up yet; the sweep will do it

            // Ownership needs the catalog, which needs the content to parse. During a
            // broad reimport that may not be readable yet — in which case everything
            // lands in the global group and the next sweep or audit corrects it.
            var bodies = SafeChapterBodies();
            var ownership = AddressableAddresses.ChapterOwnership(bodies);
            var frameFolders = AddressableAddresses.FrameFolders(bodies);

            var summary = new AddressableRegistry.Summary();

            foreach (var path in imported)
                AddressableRegistry.Register(settings, path, ownership, frameFolders, ref summary);

            foreach (var path in movedTo)
                AddressableRegistry.Register(settings, path, ownership, frameFolders, ref summary);

            foreach (var path in deleted)
            {
                if (!AddressableAddresses.IsManaged(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                AddressableRegistry.Remove(settings, guid, ref summary);
            }

            if (!summary.Changed) return;

            AddressableRegistry.Commit(settings, summary);
            AddressableRegistry.Log("addressables updated on import", summary);
        }

        static bool AnyManaged(string[] paths)
        {
            if (paths == null) return false;

            foreach (var path in paths)
                if (AddressableAddresses.IsManaged(path)) return true;

            return false;
        }

        /// <summary>
        /// The chapter bodies, or none if the content cannot be read right now. Never
        /// throws: an import hook that can fail on malformed content would make a typo
        /// in a JSON file break asset importing, which is a very hard thing to diagnose.
        /// </summary>
        internal static IReadOnlyList<ChapterBody> SafeChapterBodies()
        {
            try
            {
                return EditorContentLoader.Load().Bodies;
            }
            catch
            {
                return System.Array.Empty<ChapterBody>();
            }
        }
    }
}

#endif

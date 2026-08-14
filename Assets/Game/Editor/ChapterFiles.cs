using System;
using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// What is actually sitting in <c>StreamingAssets/Content/chapters</c>.
    ///
    /// Every other reader in the game walks the manifest, and that is right: the
    /// manifest is the authority on what the game contains, and the boot path must
    /// never list a directory — on Android it could not anyway, since StreamingAssets
    /// is inside the APK and only reachable through <c>UnityWebRequest</c>.
    ///
    /// But walking only the manifest leaves one blind spot, and it is a bad one. A
    /// chapter file nobody added to the manifest is not skipped with a warning; it is
    /// never looked at. It validates green, it audits green, it builds green, and it
    /// ships as nothing at all — the author's fortnight of work simply is not in the
    /// game, and no tool says a word. <c>Create Chapter Template</c> used to end by
    /// asking somebody to remember the manifest step, which is the same bet this
    /// project already lost once over asset registration.
    ///
    /// So this is the single place that reads the folder. It exists so
    /// <see cref="ManifestSync"/> can adopt what it finds and the build gate can prove
    /// nothing was left behind. Editor only, and it stays that way.
    /// </summary>
    public static class ChapterFiles
    {
        public const string ContentRoot = "Assets/StreamingAssets/Content";

        public static string ChaptersFolder => Path.Combine(ContentRoot, "chapters");
        public static string ManifestPath => Path.Combine(ContentRoot, "manifest.json");

        /// <summary>
        /// Reads the manifest from disk. Deliberately the raw DTO rather than a built
        /// index: an entry that is <c>disabled</c>, or that needs a newer app version,
        /// is absent from the index but is emphatically not an unlisted file, and
        /// confusing the two would report a retired chapter as a missing one every
        /// single build.
        /// </summary>
        public static bool TryReadManifest(out ManifestDto manifest, out string error)
        {
            manifest = null;

            if (!File.Exists(ManifestPath))
            {
                error = $"no manifest at {ManifestPath}";
                return false;
            }

            manifest = ContentMapper.ReadManifest(File.ReadAllText(ManifestPath), out error);
            if (manifest == null) return false;

            // Normalised here rather than at each use: this is the only reader that
            // hands the DTO back for writing, and Sync serialises the array positionally
            // to keep the file diffable. A hole in it would be a malformed manifest.
            manifest.chapters = Compact(manifest.chapters);

            error = null;
            return true;
        }

        static ManifestChapterDto[] Compact(ManifestChapterDto[] entries)
        {
            if (entries == null) return new ManifestChapterDto[0];

            var kept = new List<ManifestChapterDto>(entries.Length);
            foreach (var entry in entries)
                if (entry != null) kept.Add(entry);

            return kept.Count == entries.Length ? entries : kept.ToArray();
        }

        /// <summary>
        /// Chapter ids with a file on disk, in name order so every run reports the
        /// same thing in the same sequence and a diff means something.
        /// </summary>
        public static List<ChapterId> OnDisk(List<string> problems = null)
        {
            var found = new List<ChapterId>();
            if (!Directory.Exists(ChaptersFolder)) return found;

            var paths = Directory.GetFiles(ChaptersFolder, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(paths, StringComparer.Ordinal);

            foreach (var path in paths)
            {
                string name = Path.GetFileNameWithoutExtension(path);

                if (!ChapterId.TryParse(name, out var id, out string error))
                {
                    problems?.Add($"chapters/{name}.json is not named like a chapter id " +
                                  $"({error}), so nothing can ever load it");
                    continue;
                }

                found.Add(id);
            }

            return found;
        }

        /// <summary>Chapter files the manifest does not mention at all.</summary>
        public static List<ChapterId> Unlisted(ManifestDto manifest, List<string> problems = null)
        {
            var listed = new HashSet<ChapterId>();

            if (manifest?.chapters != null)
                foreach (var entry in manifest.chapters)
                    if (entry != null && ChapterId.TryParse(entry.id, out var id, out _))
                        listed.Add(id);

            var unlisted = new List<ChapterId>();

            foreach (var id in OnDisk(problems))
                if (!listed.Contains(id)) unlisted.Add(id);

            return unlisted;
        }
    }
}

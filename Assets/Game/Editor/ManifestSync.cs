using System.Collections.Generic;
using System.IO;
using System.Text;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using UnityEditor;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Rewrites <c>manifest.json</c>'s level lists from the chapter files themselves.
    ///
    /// The manifest is the authority on which glades exist and in what order, because
    /// that is what lets the game know its own shape after reading one small file. But
    /// an authority somebody maintains by hand alongside the real data is just a second
    /// copy waiting to disagree — the author adds a level to a chapter, forgets the
    /// manifest, and the level silently never appears.
    ///
    /// So nobody writes it: this derives it. The chapter body is where levels are
    /// authored, the manifest is generated from it, and the build gate proves the two
    /// still agree. Order within a chapter follows the body's own order, which is the
    /// order an author can see while editing.
    /// </summary>
    public static class ManifestSync
    {
        const string ContentRoot = "Assets/StreamingAssets/Content";

        [MenuItem("Glimmer Grove/Content/Sync Manifest", false, 42)]
        public static void SyncMenu()
        {
            if (!Run(out string message))
            {
                Debug.LogError("[Glimmer] " + message);
                return;
            }

            AssetDatabase.Refresh();
            Debug.Log("[Glimmer] " + message);

            ContentValidation.ValidateMenu();
        }

        public static bool Run(out string message)
        {
            string manifestPath = Path.Combine(ContentRoot, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                message = "no manifest at " + manifestPath;
                return false;
            }

            var manifest = ContentMapper.ReadManifest(File.ReadAllText(manifestPath), out string error);
            if (manifest == null)
            {
                message = error;
                return false;
            }

            var source = new BundledContentSource();
            var loader = new ChapterLoader(source);

            int changed = 0;
            var notes = new List<string>();

            foreach (var entry in manifest.chapters)
            {
                if (entry == null || !ChapterId.TryParse(entry.id, out var id, out _)) continue;

                var result = loader.LoadAsync(id).GetAwaiter().GetResult();
                if (result.Body == null)
                {
                    notes.Add($"chapter '{id}' could not be read; its level list was left alone");
                    continue;
                }

                var ids = new List<string>(result.Body.Count);
                foreach (var level in result.Body.Levels) ids.Add(level.Id.Value);

                if (Same(entry.levels, ids)) continue;

                // A chapter whose contents changed is a chapter the cache must refetch.
                entry.levels = ids.ToArray();
                entry.version++;
                changed++;

                notes.Add($"chapter '{id}' now lists {ids.Count} level(s), version {entry.version}");
            }

            if (changed > 0) File.WriteAllText(manifestPath, Serialise(manifest));

            var sb = new StringBuilder(changed == 0
                ? "manifest already matches the chapter files"
                : $"manifest synced: {changed} chapter(s) updated");

            foreach (var note in notes) sb.Append("\n  ").Append(note);

            message = sb.ToString();
            return true;
        }

        static bool Same(string[] existing, List<string> wanted)
        {
            if (existing == null) return wanted.Count == 0;
            if (existing.Length != wanted.Count) return false;

            for (int i = 0; i < existing.Length; i++)
                if (!string.Equals(existing[i], wanted[i], System.StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>
        /// Written by hand rather than by JsonUtility, which emits one unreadable line.
        /// The manifest is reviewed in pull requests and edited by people, so it has to
        /// diff cleanly — one chapter per block, one level id per line.
        /// </summary>
        static string Serialise(ManifestDto manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"schemaVersion\": {manifest.schemaVersion},");
            sb.AppendLine($"  \"progressionVersion\": {manifest.progressionVersion},");
            sb.AppendLine("  \"chapters\": [");

            for (int i = 0; i < manifest.chapters.Length; i++)
            {
                var entry = manifest.chapters[i];

                sb.AppendLine("    {");
                sb.AppendLine($"      \"id\": \"{entry.id}\",");
                sb.AppendLine($"      \"version\": {entry.version},");
                sb.AppendLine($"      \"order\": {entry.order},");
                sb.AppendLine($"      \"disabled\": {(entry.disabled ? "true" : "false")},");
                sb.AppendLine($"      \"minAppVersion\": {entry.minAppVersion},");
                sb.AppendLine("      \"levels\": [");

                var levels = entry.levels ?? new string[0];
                for (int k = 0; k < levels.Length; k++)
                    sb.AppendLine($"        \"{levels[k]}\"{(k < levels.Length - 1 ? "," : string.Empty)}");

                sb.AppendLine("      ]");
                sb.AppendLine($"    }}{(i < manifest.chapters.Length - 1 ? "," : string.Empty)}");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}

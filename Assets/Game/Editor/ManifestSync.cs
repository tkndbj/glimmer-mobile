using System;
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
    /// Brings <c>manifest.json</c> back into agreement with the chapter files.
    ///
    /// The manifest is the authority on which glades exist and in what order, because
    /// that is what lets the game know its own shape after reading one small file. But
    /// an authority somebody maintains by hand alongside the real data is just a second
    /// copy waiting to disagree — the author adds a level to a chapter, forgets the
    /// manifest, and the level silently never appears.
    ///
    /// So nobody writes it: this derives it. It does two things, and the second matters
    /// as much as the first:
    ///
    /// <list type="number">
    /// <item>Every chapter the manifest already names has its level list rewritten from
    /// its body, in the body's own order — the order an author can see while editing.</item>
    /// <item>Every chapter file the manifest does <i>not</i> name is adopted into it.
    /// Without that step a whole new chapter is invisible rather than wrong: nothing
    /// reads the folder, so nothing has anything to disagree with, and the drop ships
    /// missing a fortnight of work with a green build behind it.</item>
    /// </list>
    ///
    /// The build gate then proves both held. Running this is not optional — it is just
    /// no longer possible to forget a step it can do itself.
    /// </summary>
    public static class ManifestSync
    {
        /// <summary>
        /// Orders are spaced so a chapter can be slotted between two that shipped
        /// without renumbering either of them.
        /// </summary>
        const int OrderStep = 10;

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
            if (!ChapterFiles.TryReadManifest(out var manifest, out string error))
            {
                message = error;
                return false;
            }

            var notes = new List<string>();
            int changed = Adopt(manifest, notes);

            var source = new BundledContentSource();
            var loader = new ChapterLoader(source);

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

            if (changed > 0)
            {
                // Only ever reordered on a write, so a no-op run cannot produce a diff.
                Array.Sort(manifest.chapters, CompareEntries);

                string text = Serialise(manifest);
                if (!SurvivesRoundTrip(manifest, text, out string lost))
                {
                    message = "refusing to write the manifest: " + lost;
                    return false;
                }

                File.WriteAllText(ChapterFiles.ManifestPath, text);
            }

            var sb = new StringBuilder(changed == 0
                ? "manifest already matches the chapter files"
                : $"manifest synced: {changed} change(s)");

            foreach (var note in notes) sb.Append("\n  ").Append(note);

            message = sb.ToString();
            return true;
        }

        // ----------------------------------------------------------------- adoption
        /// <summary>
        /// Adds an entry for every chapter file the manifest does not mention.
        ///
        /// Adopted chapters are enabled, not disabled. That looks like the riskier
        /// default and is in fact the safer one: an unfinished chapter cannot slip out
        /// quietly, because it still has to pass validation — solvable boards, present
        /// strings, addressed art — and the entry appears in a reviewed diff. Adopting
        /// it disabled would swap a loud failure for the silent absence this whole
        /// mechanism exists to prevent. An author who genuinely wants it held back sets
        /// <c>disabled</c> themselves, which is a deliberate act with a name on it.
        /// </summary>
        static int Adopt(ManifestDto manifest, List<string> notes)
        {
            var problems = new List<string>();
            var unlisted = ChapterFiles.Unlisted(manifest, problems);

            foreach (var problem in problems) notes.Add(problem);
            if (unlisted.Count == 0) return 0;

            var entries = new List<ManifestChapterDto>(manifest.chapters);

            foreach (var id in unlisted)
            {
                int order = OrderFor(entries, id, notes);

                // Version 0, so filling the level list below lands it on 1.
                entries.Add(new ManifestChapterDto
                {
                    id = id.Value,
                    version = 0,
                    order = order,
                    disabled = false,
                    minAppVersion = 0,
                    levels = new string[0],
                });

                notes.Add($"adopted chapter '{id}' into the manifest at order {order}");
            }

            manifest.chapters = entries.ToArray();
            return unlisted.Count;
        }

        /// <summary>
        /// Where a newly adopted chapter goes.
        ///
        /// Chapters are all but always authored in id order, so the useful answer is
        /// nearly always "between the two it sorts between", and sparse orders exist
        /// precisely so there is room to say that without touching anything shipped.
        /// When there is no room, or when the existing orders do not follow id order at
        /// all, it appends at the end and says so in the log: a chapter in the wrong
        /// place is a one-line fix in a file already under review, whereas a chapter
        /// that is silently absent is the bug this is here to kill.
        /// </summary>
        static int OrderFor(List<ManifestChapterDto> entries, ChapterId id, List<string> notes)
        {
            int highest = 0;
            int below = int.MinValue;   // order of the nearest chapter sorting before this id
            int above = int.MaxValue;   // order of the nearest chapter sorting after it

            foreach (var entry in entries)
            {
                if (entry == null || !ChapterId.TryParse(entry.id, out var other, out _)) continue;

                if (entry.order > highest) highest = entry.order;

                int compare = string.CompareOrdinal(other.Value, id.Value);
                if (compare < 0) { if (entry.order > below) below = entry.order; }
                else if (compare > 0) { if (entry.order < above) above = entry.order; }
            }

            int append = highest + OrderStep;

            // Nothing sorts after it: the end is where it belongs.
            if (above == int.MaxValue) return append;

            int floor = below == int.MinValue ? 0 : below;

            if (floor >= above)
            {
                notes.Add($"chapter '{id}' sorts between chapters whose orders are not in id order; " +
                          $"placed last at {append} — set \"order\" by hand if that is wrong");
                return append;
            }

            if (above - floor < 2)
            {
                notes.Add($"chapter '{id}' belongs between orders {floor} and {above}, which are adjacent; " +
                          $"placed last at {append} — respace the orders if that is wrong");
                return append;
            }

            // Prefer a round number, so the file keeps reading like 10, 20, 30.
            int midpoint = floor + (above - floor) / 2;
            int rounded = midpoint / OrderStep * OrderStep;

            return rounded > floor && rounded < above ? rounded : midpoint;
        }

        static int CompareEntries(ManifestChapterDto a, ManifestChapterDto b)
        {
            if (a == null) return b == null ? 0 : 1;
            if (b == null) return -1;

            int byOrder = a.order.CompareTo(b.order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.id, b.id);
        }

        static bool Same(string[] existing, List<string> wanted)
        {
            if (existing == null) return wanted.Count == 0;
            if (existing.Length != wanted.Count) return false;

            for (int i = 0; i < existing.Length; i++)
                if (!string.Equals(existing[i], wanted[i], StringComparison.Ordinal)) return false;

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

            // Everything below is authored here, not derived, so this writer's job is
            // simply to give it back unharmed. Anything the manifest carries that this
            // method forgets to print is deleted by the next sync without a word — which
            // already happened once: the roster's prices and the whole event calendar
            // were both added later, neither reached this method, and the first sync run
            // after them silently deleted a live event and thirty companion prices. That
            // is what <see cref="SurvivesRoundTrip"/> now stands in front of; this method
            // being complete is no longer something anybody has to remember.
            var events = manifest.events ?? new ManifestEventDto[0];
            var companions = manifest.companions ?? new ManifestCompanionDto[0];

            sb.AppendLine(events.Length > 0 || companions.Length > 0 ? "  ]," : "  ]");

            if (events.Length > 0)
            {
                sb.AppendLine("  \"events\": [");

                for (int i = 0; i < events.Length; i++)
                {
                    var e = events[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"id\": \"{e.id}\",");
                    sb.AppendLine($"      \"icon\": \"{e.icon}\",");
                    sb.AppendLine($"      \"startUnix\": {e.startUnix},");
                    sb.AppendLine($"      \"endUnix\": {e.endUnix},");
                    sb.AppendLine($"      \"disabled\": {(e.disabled ? "true" : "false")},");
                    sb.AppendLine("      \"levels\": [");

                    var levels = e.levels ?? new string[0];
                    for (int k = 0; k < levels.Length; k++)
                        sb.AppendLine($"        \"{levels[k]}\"{(k < levels.Length - 1 ? "," : string.Empty)}");

                    sb.AppendLine("      ],");
                    sb.AppendLine("      \"milestones\": [");

                    var rungs = e.milestones ?? new ManifestEventMilestoneDto[0];
                    for (int k = 0; k < rungs.Length; k++)
                        sb.AppendLine($"        {{ \"goal\": {rungs[k].goal}, \"credits\": {rungs[k].credits} }}" +
                                      (k < rungs.Length - 1 ? "," : string.Empty));

                    sb.AppendLine("      ]");
                    sb.AppendLine($"    }}{(i < events.Length - 1 ? "," : string.Empty)}");
                }

                sb.AppendLine(companions.Length > 0 ? "  ]," : "  ]");
            }

            if (companions.Length > 0)
            {
                sb.AppendLine("  \"companions\": [");

                for (int i = 0; i < companions.Length; i++)
                {
                    var c = companions[i];
                    sb.AppendLine("    {");
                    sb.AppendLine($"      \"id\": \"{c.id}\",");
                    sb.AppendLine($"      \"portrait\": \"{c.portrait}\",");
                    sb.AppendLine($"      \"animated\": \"{c.animated}\",");
                    sb.AppendLine($"      \"unlockLevel\": {c.unlockLevel},");
                    sb.AppendLine($"      \"unlockCost\": {c.unlockCost},");
                    sb.AppendLine($"      \"disabled\": {(c.disabled ? "true" : "false")}");
                    sb.AppendLine($"    }}{(i < companions.Length - 1 ? "," : string.Empty)}");
                }

                sb.AppendLine("  ]");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        // ------------------------------------------------------------- round trip
        /// <summary>
        /// Reads back what <see cref="Serialise"/> just produced and proves nothing was
        /// dropped on the way out.
        ///
        /// <para>
        /// This exists because the failure it catches is invisible and permanent. The
        /// writer prints named fields, so a field added to the manifest a year from now —
        /// exactly as <c>unlockCost</c> and <c>events</c> both were, both deliberately
        /// without a schema bump — is not a compile error here, is not a validation error
        /// anywhere, and is not visible in the file until somebody runs the one step
        /// <c>CONTENT.md</c> tells them to run after <em>every</em> content edit. The first
        /// author to do that after such a change deletes live content and gets a success
        /// message.
        /// </para>
        /// <para>
        /// The check is deliberately made against the same reader the game uses, so the
        /// question it asks is exactly the one that matters: would the game still see
        /// everything it sees now? A mismatch refuses the write rather than warning about
        /// it, because a warning in a log that also says "synced" is a warning nobody reads.
        /// </para>
        /// </summary>
        static bool SurvivesRoundTrip(ManifestDto before, string text, out string lost)
        {
            var after = ContentMapper.ReadManifest(text, out string error);
            if (after == null)
            {
                lost = $"what it produced cannot be read back ({error})";
                return false;
            }

            if (after.schemaVersion != before.schemaVersion ||
                after.progressionVersion != before.progressionVersion)
            {
                lost = "the schema or progression version did not survive the write";
                return false;
            }

            if (Count(after.chapters) != Count(before.chapters))
            {
                lost = $"{Count(before.chapters)} chapter(s) went in and {Count(after.chapters)} came out";
                return false;
            }

            for (int i = 0; i < Count(before.chapters); i++)
            {
                var a = before.chapters[i];
                var b = after.chapters[i];
                if (a.id != b.id || a.version != b.version || a.order != b.order ||
                    a.disabled != b.disabled || a.minAppVersion != b.minAppVersion ||
                    !Same(a.levels, new List<string>(b.levels ?? new string[0])))
                {
                    lost = $"chapter '{a.id}' did not survive the write";
                    return false;
                }
            }

            if (Count(after.events) != Count(before.events))
            {
                lost = $"{Count(before.events)} event(s) went in and {Count(after.events)} came out";
                return false;
            }

            for (int i = 0; i < Count(before.events); i++)
            {
                var a = before.events[i];
                var b = after.events[i];
                if (a.id != b.id || a.icon != b.icon || a.startUnix != b.startUnix ||
                    a.endUnix != b.endUnix || a.disabled != b.disabled ||
                    !Same(a.levels, new List<string>(b.levels ?? new string[0])) ||
                    Count(a.milestones) != Count(b.milestones))
                {
                    lost = $"event '{a.id}' did not survive the write";
                    return false;
                }

                for (int k = 0; k < Count(a.milestones); k++)
                    if (a.milestones[k].goal != b.milestones[k].goal ||
                        a.milestones[k].credits != b.milestones[k].credits)
                    {
                        lost = $"event '{a.id}' lost a milestone on the way out";
                        return false;
                    }
            }

            if (Count(after.companions) != Count(before.companions))
            {
                lost = $"{Count(before.companions)} companion(s) went in and " +
                       $"{Count(after.companions)} came out";
                return false;
            }

            for (int i = 0; i < Count(before.companions); i++)
            {
                var a = before.companions[i];
                var b = after.companions[i];
                if (a.id != b.id || a.portrait != b.portrait || a.animated != b.animated ||
                    a.unlockLevel != b.unlockLevel || a.unlockCost != b.unlockCost ||
                    a.disabled != b.disabled)
                {
                    lost = $"companion '{a.id}' did not survive the write";
                    return false;
                }
            }

            lost = null;
            return true;
        }

        static int Count<T>(T[] array) => array?.Length ?? 0;
    }
}

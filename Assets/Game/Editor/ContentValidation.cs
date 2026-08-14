using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GlimmerGrove.EditorTools
{
    /// <summary>What a validation pass found across the whole catalog.</summary>
    public sealed class ContentValidationResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public EditorContent Content;

        public CatalogIndex Index => Content?.Index ?? CatalogIndex.Empty;

        public bool Ok => Errors.Count == 0;

        public string Summarise()
        {
            var sb = new StringBuilder();
            sb.Append($"[Glimmer] {Index.Count} level(s) across {Index.ChapterCount} chapter(s): ");
            sb.Append(Errors.Count == 0 ? "no errors" : $"{Errors.Count} error(s)");
            if (Warnings.Count > 0) sb.Append($", {Warnings.Count} warning(s)");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Proves the shipped content is sound, and refuses to build if it is not.
    ///
    /// A puzzle game gets to ship one unsolvable level before it costs a store review
    /// cycle and a wave of one-star ratings. Every check here is cheap and mechanical,
    /// so there is no reason for it to be optional — it runs on every build, and a
    /// failure stops the build rather than producing a broken binary.
    /// </summary>
    public static class ContentValidation
    {
        [MenuItem("Glimmer Grove/Validate Content", false, 20)]
        public static void ValidateMenu()
        {
            var result = Run(verbose: true);

            foreach (var w in result.Warnings) Debug.LogWarning("[Glimmer] " + w);
            foreach (var e in result.Errors) Debug.LogError("[Glimmer] " + e);

            if (result.Ok) Debug.Log(result.Summarise());
            else Debug.LogError(result.Summarise());
        }

        public static ContentValidationResult Run(bool verbose = false)
        {
            var result = new ContentValidationResult();

            var load = EditorContentLoader.Load();
            result.Content = load;

            // Anything the loader had to skip is a content bug, not a runtime nicety.
            foreach (var problem in load.Problems) result.Errors.Add(problem);

            // Before anything that walks the manifest, because a chapter missing from it
            // is invisible to every check that does — including the empty-catalog one
            // just below, which would otherwise report the symptom and hide the cause.
            ValidateManifestCoverage(result);

            if (load.Index.IsEmpty)
            {
                result.Errors.Add("no levels loaded from Assets/StreamingAssets/Content");
                return result;
            }

            ValidateChapterOrder(load.Index, result);
            ValidateCompanions(load.Index, result, verbose);
            ValidateLevels(load, result, verbose);
            ValidateChapterMaps(load, result);
            ValidateLocalisation(load, result);
            ValidateProgression(load.Index, result, verbose);
            ValidateLegacyMigration(load.Index, result);

            return result;
        }

        /// <summary>
        /// Proves the manifest accounts for every chapter file that ships.
        ///
        /// This is the one check that cannot be made by reading the manifest, because
        /// its subject is what the manifest failed to say. A chapter file nobody listed
        /// is not loaded and rejected — it is never opened, so every other validator
        /// here passes it in silence and the build is green with a fortnight of content
        /// missing from it. <c>Content ▸ Sync Manifest</c> now adopts such a file
        /// automatically; this exists because making a mistake unlikely is not the same
        /// as proving it did not happen.
        ///
        /// An error rather than a warning: shipping a chapter that is present in the
        /// build and absent from the game is exactly as bad as shipping a broken one,
        /// and the fix is one menu item.
        /// </summary>
        static void ValidateManifestCoverage(ContentValidationResult result)
        {
            if (!ChapterFiles.TryReadManifest(out var manifest, out string error))
            {
                result.Errors.Add(error);
                return;
            }

            var problems = new List<string>();

            foreach (var id in ChapterFiles.Unlisted(manifest, problems))
                result.Errors.Add($"chapters/{id}.json is not listed in manifest.json, so nothing will " +
                                  "ever read it and its glades cannot appear in the game; " +
                                  "run Content ▸ Sync Manifest to adopt it");

            // A stray .json that is not named like a chapter is a warning: it may be a
            // scratch file, and it is at least not pretending to be shipped content.
            foreach (var problem in problems) result.Warnings.Add(problem);
        }

        /// <summary>
        /// Checks node placement one chapter at a time, in the index's order.
        ///
        /// Per-level validation cannot see this: whether two glades collide is a fact
        /// about the pair, and how far apart they are depends on how many strips the
        /// chapter declared. See <see cref="ChapterMapValidator"/>.
        /// </summary>
        static void ValidateChapterMaps(EditorContent content, ContentValidationResult result)
        {
            foreach (var chapter in content.Index.Chapters)
            {
                if (!content.Catalog.TryResidentChapter(chapter.Id, out var body)) continue;

                var levels = new List<LevelDefinition>(chapter.LevelIds.Count);
                foreach (var level in body.InIndexOrder(chapter.LevelIds)) levels.Add(level);

                foreach (var issue in ChapterMapValidator.Validate(body.Definition, levels))
                {
                    string line = $"chapter '{chapter.Id}': {issue.Message}";

                    if (issue.Severity == LevelIssueSeverity.Error) result.Errors.Add(line);
                    else result.Warnings.Add(line);
                }
            }
        }

        /// <summary>
        /// Two chapters at the same order sort by id, which is deterministic but is
        /// almost never what the author meant — and the mistake is invisible until
        /// players find the game's chapters in an order nobody chose. Orders are sparse
        /// (10, 20, 30) precisely so there is never a reason for a collision.
        /// </summary>
        static void ValidateChapterOrder(CatalogIndex index, ContentValidationResult result)
        {
            var byOrder = new Dictionary<int, ChapterId>();

            foreach (var chapter in index.Chapters)
            {
                if (byOrder.TryGetValue(chapter.Order, out var other))
                {
                    result.Errors.Add($"chapters '{other}' and '{chapter.Id}' both claim order " +
                                      $"{chapter.Order} in manifest.json; give them distinct orders");
                    continue;
                }
                byOrder[chapter.Order] = chapter.Id;
            }
        }

        /// <summary>
        /// Proves the XP curve and reward table are usable.
        ///
        /// The curve is content, which means it can be retuned without a store review —
        /// and means a typo in it reaches players the same way. A band costing zero XP
        /// would hand out unbounded levels at once, and a reward override naming a
        /// chapter that does not exist would silently pay the default rate forever
        /// while looking, in the file, exactly like it was working.
        /// </summary>
        static void ValidateProgression(CatalogIndex index, ContentValidationResult result, bool verbose)
        {
            var source = new BundledContentSource();
            var fetch = source.FetchAsync(ContentPaths.Progression, default).GetAwaiter().GetResult();

            if (!fetch.Success)
            {
                result.Errors.Add($"missing {ContentPaths.Progression}");
                return;
            }

            var problems = new List<string>();
            if (!ProgressionTable.TryRead(fetch.Text, out var table, problems))
            {
                foreach (var problem in problems) result.Errors.Add(problem);
                return;
            }

            // Anything the reader survived but had to skip is still an authoring bug.
            foreach (var problem in problems) result.Errors.Add(problem);

            foreach (var chapter in index.Chapters)
            {
                if (!table.HasOverrideFor(chapter.Id) && verbose)
                    Debug.Log($"[Glimmer] chapter '{chapter.Id}' uses the default reward rule");
            }

            ValidateRewardChaptersExist(fetch.Text, index, result);
            ValidateDailyChests(table.Daily, result, verbose);

            if (!verbose) return;

            long maximumXp = 0;
            foreach (var id in index.LevelIds)
                maximumXp += table.RuleFor(index.ChapterOf(id)).XpFor(3);

            var reachable = table.LevelFor(maximumXp);
            Debug.Log($"[Glimmer] progression verified: {index.Count} glade(s) at three stars " +
                      $"is {maximumXp} XP, reaching level {reachable.Level} of {table.MaxLevel}");
        }

        /// <summary>
        /// The daily chest table, checked for the things the reader cannot know.
        ///
        /// <para>
        /// The reader rejects a table that is malformed. This rejects one that is merely
        /// wrong: chests that get worse as they get harder, a boost longer than the rules
        /// allow, a chest with no variable slot at all. None of those would throw, and all
        /// of them would ship.
        /// </para>
        /// <para>
        /// It also prints the published odds. That is the point of running it verbosely
        /// before a drop — the disclosure a store or a regulator may ask for is generated
        /// from the file the game actually rolls against, so it cannot be out of date.
        /// </para>
        /// </summary>
        static void ValidateDailyChests(DailyChestTable daily, ContentValidationResult result, bool verbose)
        {
            if (daily == null) { result.Errors.Add("progression.json produced no daily chest table"); return; }

            if (daily.ChestCount < 1)
            {
                result.Errors.Add("the daily table has no chests");
                return;
            }

            long previousFloor = -1;

            for (int i = 0; i < daily.ChestCount; i++)
            {
                var chest = daily.Chest(i);

                long floor = 0;
                foreach (var band in chest.Guaranteed)
                {
                    floor += band.Min;

                    if (band.Kind == ChestDropKind.HeartBoost && band.Max > HeartRules.MaxBoostHours)
                        result.Errors.Add($"daily chest {i} guarantees a {band.Max}h heart boost, " +
                                          $"more than the {HeartRules.MaxBoostHours}h ceiling");
                }

                // Later chests cost more play, so they have to be worth more. A table where
                // the third is meaner than the first reads to a player as the game
                // punishing them for keeping going, and nothing else in the build catches it.
                if (floor < previousFloor)
                    result.Errors.Add($"daily chest {i} guarantees less than chest {i - 1} " +
                                      $"({floor} against {previousFloor}); a later chest costs more " +
                                      "play and must never pay less");
                previousFloor = floor;

                foreach (var option in chest.Options)
                {
                    if (option.Band.Kind == ChestDropKind.HeartBoost &&
                        option.Band.Max > HeartRules.MaxBoostHours)
                        result.Errors.Add($"daily chest {i} can drop a {option.Band.Max}h heart boost, " +
                                          $"more than the {HeartRules.MaxBoostHours}h ceiling");
                }

                if (chest.Options.Count == 0)
                    result.Warnings.Add($"daily chest {i} has no bonus slot, so it pays the same " +
                                        "thing every day");
            }

            if (!verbose) return;

            for (int i = 0; i < daily.ChestCount; i++)
            {
                var chest = daily.Chest(i);
                var line = new System.Text.StringBuilder()
                    .Append("[Glimmer] daily chest ").Append(i + 1)
                    .Append(" (after ").Append(daily.RunsFor(i)).Append(" runs) always pays");

                foreach (var band in chest.Guaranteed)
                    line.Append(' ').Append(band.Min).Append('-').Append(band.Max)
                        .Append(' ').Append(ChestDropKinds.Id(band.Kind));

                if (chest.Options.Count > 0)
                {
                    line.Append("  ·  bonus:");
                    for (int o = 0; o < chest.Options.Count; o++)
                    {
                        var option = chest.Options[o];
                        line.Append("  ").Append(ChestDropKinds.Id(option.Band.Kind))
                            .Append(' ').Append(option.Band.Min).Append('-').Append(option.Band.Max)
                            .Append(" at ").Append(chest.ChanceOf(o).ToString("0.#")).Append('%');
                    }
                }

                Debug.Log(line.ToString());
            }
        }

        /// <summary>
        /// A reward override for a chapter that is not in the catalog is dead config.
        /// Reported as a warning rather than an error because authoring the rule before
        /// the chapter is a legitimate order to work in.
        /// </summary>
        static void ValidateRewardChaptersExist(string json, CatalogIndex index,
                                                ContentValidationResult result)
        {
            var dto = JsonUtility.FromJson<ProgressionDto>(json);
            if (dto?.chapterRewards == null) return;

            foreach (var entry in dto.chapterRewards)
            {
                if (entry == null || string.IsNullOrEmpty(entry.chapterId)) continue;
                if (!ChapterId.TryParse(entry.chapterId, out var id, out _)) continue;
                if (index.ContainsChapter(id)) continue;

                result.Warnings.Add($"progression.json sets rewards for chapter '{entry.chapterId}', " +
                                    "which is not in the catalog; the rule is inert");
            }
        }

        static void ValidateLevels(EditorContent content, ContentValidationResult result, bool verbose)
        {
            var byId = new Dictionary<LevelId, LevelDefinition>();
            foreach (var level in content.AllLevels()) byId[level.Id] = level;

            foreach (var report in LevelValidator.ValidateAll(byId.Values))
            {
                foreach (var issue in report.Issues)
                {
                    string line = $"{report.Id}: {issue.Message}";
                    if (issue.Severity == LevelIssueSeverity.Error) result.Errors.Add(line);
                    else result.Warnings.Add(line);
                }

                if (verbose && report.IsClean && byId.TryGetValue(report.Id, out var level))
                    Debug.Log($"[Glimmer] {report.Id} verified " +
                              $"({level.Layout.Width}x{level.Layout.Height}, par {level.Tuning.Par})");
            }
        }

        /// <summary>
        /// The companion roster: art that exists, a starter anyone can wear, and a
        /// curve that stays reachable.
        ///
        /// The last one is the check that earns its keep. Unlock levels are content now,
        /// so a drop can retune them without a build — and a threshold set above what
        /// the shipped catalog can actually reach produces a companion nobody will ever
        /// see, which nothing else in the pipeline would notice.
        /// </summary>
        static void ValidateCompanions(CatalogIndex index, ContentValidationResult result, bool verbose)
        {
            var companions = index.Companions;
            if (companions.Count == 0)
            {
                result.Warnings.Add("the manifest lists no companions; the built-in roster will be used");
                return;
            }

            bool anyFree = false;
            foreach (var companion in companions)
            {
                if (companion.UnlockLevel == 0) anyFree = true;

                string portrait = "Assets/Game/Art/Companions/" + companion.Portrait + ".png";
                if (AssetDatabase.LoadAssetAtPath<Sprite>(portrait) == null)
                    result.Errors.Add($"companion '{companion.Id}' has no portrait at {portrait}");

                if (companion.HasAnimation &&
                    !AssetDatabase.IsValidFolder("Assets/Game/Art/Critters/" + companion.Animated))
                    result.Errors.Add($"companion '{companion.Id}' names animation set " +
                                      $"'{companion.Animated}', which is not a folder under Art/Critters");
            }

            if (!anyFree)
                result.Errors.Add("no companion unlocks at level 0; a new player would have none to wear");

            // What the whole shipped catalog is worth, three-starred. Anything above it
            // is unreachable until more glades ship.
            //
            // Reported as one line rather than one per companion on purpose: a roster
            // deliberately built to outlast the current content would otherwise emit
            // dozens of warnings every run, and a validator nobody reads is a validator
            // that has stopped working.
            int reachable = ReachableKeeperLevel(index);
            int beyond = 0, highest = 0;
            foreach (var companion in companions)
            {
                if (companion.UnlockLevel <= reachable) continue;
                beyond++;
                if (companion.UnlockLevel > highest) highest = companion.UnlockLevel;
            }

            if (beyond > 0)
                result.Warnings.Add($"{beyond} of {companions.Count} companions unlock above keeper level " +
                                    $"{reachable}, which is all the current catalog can reach " +
                                    $"(highest is {highest}); they are unreachable until more glades ship");

            if (verbose)
                Debug.Log($"[Glimmer] {companions.Count} companions, " +
                          $"{ReachableCount(companions, reachable)} reachable at keeper level {reachable}");
        }

        /// <summary>The keeper level a player reaches by three-starring everything that ships.</summary>
        static int ReachableKeeperLevel(CatalogIndex index)
        {
            var table = ProgressionRules.Table;

            long xp = 0;
            foreach (var id in index.LevelIds)
                xp += table.RuleFor(index.ChapterOf(id)).XpFor(3);

            return table.LevelFor(xp).Level;
        }

        static int ReachableCount(IReadOnlyList<AvatarDefinition> companions, int level)
        {
            int n = 0;
            foreach (var c in companions) if (c.UnlockLevel <= level) n++;
            return n;
        }

        /// <summary>Every key a level references must exist in the fallback language.</summary>
        static void ValidateLocalisation(EditorContent content, ContentValidationResult result)
        {
            var source = new BundledContentSource();
            var fetch = source.FetchAsync(ContentPaths.Localisation(Loc.FallbackLanguage), default)
                              .GetAwaiter().GetResult();

            if (!fetch.Success)
            {
                result.Errors.Add($"missing {ContentPaths.Localisation(Loc.FallbackLanguage)}");
                return;
            }

            var table = LocTable.Parse(fetch.Text, out string error);
            if (error != null) { result.Errors.Add(error); return; }

            foreach (var chapter in content.Index.Chapters)
                Require(table, chapter.NameKey, $"chapter '{chapter.Id}'", result);

            // Keyed off the id, so every glade the manifest names is checked whether or
            // not its body could be read - a missing string and a missing chapter are
            // different bugs and must not mask each other.
            foreach (var id in content.Index.LevelIds)
            {
                Require(table, LevelDefinition.DefaultNameKey(id), $"level '{id}'", result);
                Require(table, LevelDefinition.DefaultTaglineKey(id), $"level '{id}'", result);
                Require(table, LevelDefinition.DefaultLessonKey(id), $"level '{id}'", result);
            }

            // Companion names are derived from the id like a level's, so the source scan
            // below cannot see them — only this can.
            foreach (var companion in content.Index.Companions)
                Require(table, companion.NameKey, $"companion '{companion.Id}'", result);

            ValidateKeysUsedInCode(table, result);
        }

        /// <summary>
        /// Scans the source for string keys and checks each one exists.
        ///
        /// A hand-maintained list of required UI keys would drift the first time
        /// someone added a button. Reading the source cannot drift, so a missing
        /// translation is caught by the build rather than by a player seeing
        /// "ui.pause.resume" printed on a button.
        ///
        /// It matches any literal shaped like a key rather than only the arguments to
        /// Loc, because plenty of keys are passed through a variable first — a nav
        /// item's label, an overlay's title — and those need checking just as much.
        /// </summary>
        static void ValidateKeysUsedInCode(LocTable table, ContentValidationResult result)
        {
            const string scriptRoot = "Assets/Game/Scripts";
            if (!Directory.Exists(scriptRoot)) return;

            // "ui.pause.resume" yes; "Art/Ui/panel" or a sentence no.
            var keyLiteral = new Regex(@"""((?:ui|level|chapter)\.[a-z0-9_]+(?:\.[a-z0-9_]+)+)""",
                                       RegexOptions.Compiled);
            var seen = new HashSet<string>();

            foreach (var file in Directory.GetFiles(scriptRoot, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file);

                foreach (Match match in keyLiteral.Matches(source))
                {
                    string key = match.Groups[1].Value;
                    if (!seen.Add(key)) continue;

                    // A literal glued to something else is a prefix, not a key.
                    int after = match.Index + match.Length;
                    if (after < source.Length && IsConcatenation(source, after)) continue;

                    if (!table.TryGet(key, out _))
                        result.Errors.Add($"{Path.GetFileName(file)} uses missing string '{key}'");
                }
            }
        }

        static bool IsConcatenation(string source, int index)
        {
            while (index < source.Length && source[index] == ' ') index++;
            return index < source.Length && source[index] == '+';
        }

        static void Require(LocTable table, string key, string owner, ContentValidationResult result)
        {
            if (!table.TryGet(key, out _))
                result.Errors.Add($"{owner} references missing string '{key}'");
        }

        /// <summary>
        /// The frozen legacy index table must still point at real levels, or players
        /// updating from the original build would silently lose their stars.
        /// </summary>
        static void ValidateLegacyMigration(CatalogIndex index, ContentValidationResult result)
        {
            foreach (var missing in LegacyPlayerPrefsImport.MissingFromCatalog(index))
                result.Errors.Add($"legacy save migration maps to '{missing}', which is no longer in the catalog; " +
                                  "removing a level that shipped in the original build orphans player progress");
        }
    }

    /// <summary>Stops a build in its tracks when the content does not validate.</summary>
    public sealed class ContentBuildGate : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var result = ContentValidation.Run();

            // Content being sound is only half of shippable. An unaddressed asset
            // produces no content error at all - the JSON is perfect, the file is on
            // disk - and then the player gets a chapter with no backdrop. The audit is
            // the only thing standing between that and the store, so it runs here.
            var errors = new List<string>(result.Errors);
            var warnings = new List<string>(result.Warnings);

#if GLIMMER_HAS_ADDRESSABLES
            var audit = AddressableAudit.Run();
            errors.AddRange(audit.Errors);
            warnings.AddRange(audit.Warnings);
#endif

            foreach (var w in warnings) Debug.LogWarning("[Glimmer] " + w);

            if (errors.Count == 0)
            {
                Debug.Log(result.Summarise());
                return;
            }

            foreach (var e in errors) Debug.LogError("[Glimmer] " + e);
            throw new BuildFailedException(
                $"the build gate found {errors.Count} error(s); see the console");
        }
    }
}

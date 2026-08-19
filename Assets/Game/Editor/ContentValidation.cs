using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Daily;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
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
            ValidateHomestead(load, result, verbose);
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
            ValidateHearts(table.Hearts, result, verbose);
            ValidateDailyChests(table.Daily, table.Hearts, result, verbose);
            ValidateStreak(table.Streak, result, verbose);
            ValidateGolden(table.Golden, table, index, result, verbose);
            ValidateEvents(index, result, verbose);
            ValidateStore(table, result, verbose);

            if (!verbose) return;

            long maximumXp = 0;
            foreach (var id in index.LevelIds)
                maximumXp += table.RuleFor(index.ChapterOf(id)).XpFor(3);

            var reachable = table.LevelFor(maximumXp);
            Debug.Log($"[Glimmer] progression verified: {index.Count} glade(s) at three stars " +
                      $"is {maximumXp} XP, reaching level {reachable.Level} of {table.MaxLevel}");
        }

        /// <summary>
        /// The shop, checked harder than anything else in this file.
        ///
        /// <para>
        /// <b>Errors here, not warnings.</b> Every other block in <c>progression.json</c>
        /// describes what play pays, and an aggressive tuning is a legitimate weekend
        /// decision — which is why the heart gate and the chest odds are checked with
        /// warnings and a build still goes out. This block describes what somebody is
        /// <em>charged</em>, and there is no version of "a bit wrong" that is acceptable: a
        /// product granting the wrong amount is a real payment honoured for a figure nobody
        /// meant, and the only way to put it right afterwards is one refund at a time.
        /// </para>
        /// <para>
        /// The two checks that matter are the ones no reader can make on its own. A ladder
        /// that gets worse as it gets bigger is invisible in the file and obvious to the
        /// first player who does the arithmetic. And a good that cannot be bought at any
        /// moment — hearts above the ceiling, a boost longer than the cap — is a card that
        /// is permanently refused, which reads exactly like a bug and is one.
        /// </para>
        /// <para>
        /// The seeder re-checks all of this before publishing <c>config/products</c>. Two
        /// implementations of one rule is what invariant 9a normally forbids — but these run
        /// on different sides of a wire, in different languages, and the failure they guard
        /// is a card promising what the server will not honour. A disagreement between them
        /// is exactly the thing worth catching, so it is checked twice on purpose.
        /// </para>
        /// </summary>
        static void ValidateStore(ProgressionTable table, ContentValidationResult result, bool verbose)
        {
            var catalog = table.Store;
            if (catalog == null) { result.Errors.Add("progression.json produced no store catalog"); return; }

            if (!catalog.HasAnything)
            {
                if (verbose) Debug.Log("[Glimmer] store: nothing for sale");
                return;
            }

            foreach (var product in catalog.Products)
            {
                // Both stores accept longer ids than this and both refuse some characters
                // this allows; the narrow set is what works on both without surprises. Worth
                // saying out loud because a product id can never be changed after it ships —
                // neither console lets one be reused, so a rename is a new product and a
                // migration for anybody mid-purchase.
                if (product.Id.Length > 40)
                    result.Warnings.Add($"store product id '{product.Id}' is {product.Id.Length} " +
                                        "characters; it works, but a product id can never be " +
                                        "changed once it has shipped");
            }

            foreach (var good in catalog.Goods)
            {
                // A card that can never be tapped. `StoreService.OfferForGood` refuses a
                // purchase that would push a player past the ceiling, so a good bigger than
                // the whole ceiling is refused for everybody, for ever.
                if (good.Kind == StoreGoodKind.Hearts && good.Amount > table.Hearts.Ceiling)
                    result.Errors.Add($"store good '{good.Id}' hands over {good.Amount} hearts, above " +
                                      $"the ceiling of {table.Hearts.Ceiling}; it can never be bought");

                if (good.Kind == StoreGoodKind.HeartBoost && good.Amount > table.Hearts.MaxBoostHours)
                    result.Errors.Add($"store good '{good.Id}' hands over {good.Amount}h of boost, above " +
                                      $"the {table.Hearts.MaxBoostHours}h cap; it can never be bought");

                // A good nobody can afford in a month of play is a card that only exists to
                // be looked at. Warning rather than error: it is a legitimate top rung, and
                // the figure it is measured against is itself an estimate.
                long gemsPerDay = DailyGemIncome(table);
                if (gemsPerDay > 0 && good.Gems > gemsPerDay * 60)
                    result.Warnings.Add($"store good '{good.Id}' costs {good.Gems} gems, about " +
                                        $"{good.Gems / gemsPerDay} days of collecting; nothing else on " +
                                        "the shelf is that far away");
            }

            ValidateStoreLadder(catalog, result);

            if (!verbose) return;

            ReportStoreEconomy(catalog, table);
        }

        /// <summary>
        /// Every shelf gets better as it gets bigger.
        ///
        /// <para>
        /// One-time offers are exempt, and that is the point of them rather than a loophole:
        /// a starter pack is deliberately worth several times the ladder, and it cannot
        /// undercut it because the store refuses to sell it twice. Ranking it alongside the
        /// repeatable rungs would either fail this check or force it to be a worse offer
        /// than it should be.
        /// </para>
        /// </summary>
        static void ValidateStoreLadder(StoreCatalog catalog, ContentValidationResult result)
        {
            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                var rungs = new List<StoreProduct>();
                foreach (var product in catalog.Shelf(shelf))
                    if (!product.IsOneTime) rungs.Add(product);

                rungs.Sort((a, b) => a.ReferenceUsdCents.CompareTo(b.ReferenceUsdCents));

                for (int i = 1; i < rungs.Count; i++)
                {
                    if (rungs[i].ReferenceUsdCents == rungs[i - 1].ReferenceUsdCents)
                    {
                        // Two cards at one price point make a player compare contents rather
                        // than sizes, which is a shop asking somebody to do arithmetic.
                        result.Warnings.Add(
                            $"store shelf '{shelf}': '{rungs[i].Id}' and '{rungs[i - 1].Id}' are the " +
                            "same price; two cards at one price point make a player do arithmetic");
                        continue;
                    }

                    long before = rungs[i - 1].ValuePerCent(catalog.CreditsPerGem);
                    long after = rungs[i].ValuePerCent(catalog.CreditsPerGem);

                    if (after >= before) continue;

                    result.Errors.Add(
                        $"store shelf '{shelf}': '{rungs[i].Id}' costs more than '{rungs[i - 1].Id}' " +
                        "and gives less per unit of money. A ladder that gets worse as it gets " +
                        "bigger is a shop nobody buys the large size in");
                }
            }
        }

        /// <summary>
        /// The shop in the terms it was priced in, printed so nobody has to derive it again.
        ///
        /// <para>
        /// Two figures are worth reading: what a coin pack is worth against what a day of
        /// play earns, and how long a gem-priced good takes to collect for free. Both come
        /// from the same published tables the game reads, so a retune moves them — the rule
        /// every explanatory panel in this game follows, applied to the console.
        /// </para>
        /// </summary>
        static void ReportStoreEconomy(StoreCatalog catalog, ProgressionTable table)
        {
            long daily = DailyCreditIncome(table);
            long gemsPerDay = DailyGemIncome(table);

            foreach (StoreShelf shelf in System.Enum.GetValues(typeof(StoreShelf)))
            {
                foreach (var product in catalog.Shelf(shelf))
                {
                    string worth = product.Credits > 0 && daily > 0
                        ? $", {product.Credits / daily} day(s) of play"
                        : string.Empty;

                    Debug.Log($"[Glimmer] store {shelf} #{product.Tier}: '{product.Id}' " +
                              $"{product.Gems} gems + {product.Credits} credits at " +
                              $"${product.ReferenceUsdCents / 100f:0.00} " +
                              $"(+{product.BonusPercent}%{worth})");
                }
            }

            foreach (var good in catalog.Goods)
            {
                string wait = gemsPerDay > 0 ? $", about {good.Gems / gemsPerDay} day(s) of collecting"
                                             : string.Empty;

                Debug.Log($"[Glimmer] store good '{good.Id}': {good.Amount} " +
                          $"{StoreGoodKinds.Id(good.Kind)} for {good.Gems} gems{wait}");
            }

            Debug.Log($"[Glimmer] store: {catalog.Products.Count} product(s), " +
                      $"{catalog.Goods.Count} good(s); one gem is worth about " +
                      $"{catalog.CreditsPerGem} credits across the two ladders, and free play " +
                      $"collects about {gemsPerDay} gem(s) a day");
        }

        /// <summary>
        /// Gems an engaged player collects in a day: every chest's expected gems plus a
        /// streak rung amortised over the ladder's lap.
        ///
        /// The gem half of <see cref="DailyCreditIncome"/>, and it exists for the same
        /// reason — a price is only meaningful beside the income that has to pay it, and
        /// gems are the currency the whole supplies shelf is priced in.
        /// </summary>
        static long DailyGemIncome(ProgressionTable table)
        {
            long daily = 0;

            var chests = table.Daily;
            for (int i = 0; i < chests.ChestCount; i++)
                daily += ExpectedGems(chests.Chest(i));

            var streak = table.Streak;
            if (streak.Length > 0)
            {
                long lap = 0;
                for (int night = 1; night <= streak.Length; night++)
                {
                    var rung = streak.Rung(night);
                    if (rung.Kind == ChestDropKind.Gems) lap += rung.Amount;
                }

                daily += lap / streak.Length;
            }

            return daily;
        }

        /// <summary>Gems one chest is worth on average. See <see cref="ExpectedCredits"/>.</summary>
        static long ExpectedGems(ChestDefinition chest)
        {
            if (chest == null) return 0;

            double gems = 0;

            for (int i = 0; i < chest.Guaranteed.Count; i++)
            {
                var band = chest.Guaranteed[i];
                if (band.Kind == ChestDropKind.Gems) gems += (band.Min + band.Max) * .5;
            }

            for (int i = 0; i < chest.Options.Count; i++)
            {
                var option = chest.Options[i];
                if (option.Band.Kind != ChestDropKind.Gems) continue;

                gems += (option.Band.Min + option.Band.Max) * .5 * (chest.ChanceOf(i) / 100.0);
            }

            return (long)gems;
        }

        /// <summary>
        /// The heart gate, checked for the things the reader cannot know.
        ///
        /// <para>
        /// The reader clamps every field into a supported range and says so, which stops a
        /// typo shipping as a broken game. What it cannot judge is whether the numbers make
        /// sense <em>together</em> — and the ones below are the combinations that would
        /// validate, build, ship, and then quietly wreck either the economy or the point of
        /// the feature.
        /// </para>
        /// <para>
        /// Warnings rather than errors, deliberately. Every one of these is a legitimate
        /// thing a designer might do on purpose for a weekend event, and a gate that
        /// refuses to build over an aggressive but intentional tuning is a gate people
        /// learn to route around. They are loud, they are named, and they are printed with
        /// the numbers that caused them.
        /// </para>
        /// </summary>
        static void ValidateHearts(HeartRuleTable hearts, ContentValidationResult result, bool verbose)
        {
            if (hearts == null) { result.Errors.Add("progression.json produced no heart table"); return; }

            // A full set that refills in under an hour is not a gate, and every number
            // balanced against it — chest values, ad payouts, the streak ladder — was tuned
            // against a game where sessions are rationed.
            long toFull = hearts.RefillSeconds * hearts.RefillCap;
            if (toFull < 3600)
                result.Warnings.Add($"hearts refill a full set in {toFull / 60} minutes " +
                                    $"({hearts.RefillCap} × {hearts.RefillSeconds}s); at that rate the " +
                                    "gate does not bind and everything balanced against it is loose");

            // The ad offer pays two hearts and the chests pay up to three; a ceiling within
            // touching distance of the cap means those land on a full bar and evaporate,
            // which is precisely the failure the ceiling was separated from the cap to end.
            if (hearts.Ceiling < hearts.RefillCap + 5)
                result.Warnings.Add($"hearts ceiling {hearts.Ceiling} leaves only " +
                                    $"{hearts.Ceiling - hearts.RefillCap} above the refill cap; " +
                                    "collected hearts will routinely be thrown away");

            // Half is the smallest multiple a player feels. Anything above 0.8 is a boost
            // that a player is told about, waits for, and cannot detect.
            if (hearts.BoostedRefillSeconds > hearts.RefillSeconds * 4 / 5)
                result.Warnings.Add($"the heart boost saves only " +
                                    $"{hearts.RefillSeconds - hearts.BoostedRefillSeconds}s of " +
                                    $"{hearts.RefillSeconds}s; a boost nobody can feel is a reward " +
                                    "that reads as broken");

            // A loss that costs the whole bar ends the session on the first mistake.
            if (hearts.DefeatCost >= hearts.RefillCap)
                result.Warnings.Add($"a lost run costs {hearts.DefeatCost} of {hearts.RefillCap} hearts; " +
                                    "one mistake would end the session");

            if (!verbose) return;

            Debug.Log($"[Glimmer] hearts: refill to {hearts.RefillCap} every " +
                      $"{hearts.RefillSeconds / 3600f:0.##}h ({hearts.BoostedRefillSeconds / 3600f:0.##}h " +
                      $"boosted, up to {hearts.MaxBoostHours}h of boost), hold up to {hearts.Ceiling}, " +
                      $"a loss costs {hearts.DefeatCost}");
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
        static void ValidateDailyChests(DailyChestTable daily, HeartRuleTable hearts,
                                        ContentValidationResult result, bool verbose)
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

                    if (band.Kind == ChestDropKind.HeartBoost && band.Max > hearts.MaxBoostHours)
                        result.Errors.Add($"daily chest {i} guarantees a {band.Max}h heart boost, " +
                                          $"more than the {hearts.MaxBoostHours}h ceiling");
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
                        option.Band.Max > hearts.MaxBoostHours)
                        result.Errors.Add($"daily chest {i} can drop a {option.Band.Max}h heart boost, " +
                                          $"more than the {hearts.MaxBoostHours}h ceiling");
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
        /// The event calendar, checked for the things only a person looking at a date can
        /// see.
        ///
        /// <para>
        /// The builder has already refused anything structurally wrong — an inverted
        /// window, a track that cannot be finished, a glade no chapter holds. What is left
        /// is the class of mistake that produces a perfectly valid event nobody wanted: two
        /// running at once, one that opened last year, one whose whole track is a single
        /// glade. None of those would fail anything, and all of them ship.
        /// </para>
        /// <para>
        /// The calendar is also <em>printed</em>, past events included, with the dates
        /// resolved. An event is authored as two Unix timestamps, which is the correct
        /// storage and an impossible thing to proofread — the single most likely mistake in
        /// this whole feature is a window that is off by a month and looks fine in the file.
        /// </para>
        /// </summary>
        static void ValidateEvents(CatalogIndex index, ContentValidationResult result, bool verbose)
        {
            var events = index.Events;
            if (events == null || events.Count == 0) return;      // no calendar is not an error

            long now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            for (int i = 0; i < events.Count; i++)
            {
                var groveEvent = events[i];

                // Two live at once is not forbidden, but the hub shows one, so the second
                // would be invisible to every player it was authored for.
                for (int j = i + 1; j < events.Count; j++)
                {
                    var other = events[j];
                    if (other.StartUnix >= groveEvent.EndUnix) continue;

                    result.Warnings.Add($"events '{groveEvent.Id}' and '{other.Id}' overlap; the hub " +
                                        "shows one event at a time, so the later one would be " +
                                        "invisible for as long as they both run");
                }

                if (groveEvent.FinalGoal <= 1 && groveEvent.Levels.Count > 1)
                    result.Warnings.Add($"event '{groveEvent.Id}' finishes at one glade but names " +
                                        $"{groveEvent.Levels.Count}; the rest are decoration");

                if (groveEvent.TotalCredits <= 0)
                    result.Warnings.Add($"event '{groveEvent.Id}' pays nothing, so its countdown is " +
                                        "a deadline with no prize behind it");

                if (groveEvent.HasEndedAt(now)) continue;

                long days = (groveEvent.EndUnix - groveEvent.StartUnix) / Events.EventRules.SecondsPerDay;
                if (days < 2)
                    result.Warnings.Add($"event '{groveEvent.Id}' runs for under two days; a player " +
                                        "who opens the game every other evening would never see it");
            }

            if (!verbose) return;

            foreach (var groveEvent in events)
            {
                string state = groveEvent.HasEndedAt(now) ? "ended"
                             : groveEvent.IsLiveAt(now) ? "LIVE"
                             : "upcoming";

                var line = new System.Text.StringBuilder()
                    .Append("[Glimmer] event '").Append(groveEvent.Id).Append("' ").Append(state)
                    .Append(": ").Append(Stamp(groveEvent.StartUnix))
                    .Append(" → ").Append(Stamp(groveEvent.EndUnix))
                    .Append("  ·  ").Append(groveEvent.Levels.Count).Append(" glade(s)  ·  track:");

                foreach (var milestone in groveEvent.Milestones)
                    line.Append("  ").Append(milestone.Goal).Append('→').Append(milestone.Credits);

                line.Append("  (").Append(groveEvent.TotalCredits).Append(" total)");
                Debug.Log(line.ToString());
            }
        }

        /// <summary>A Unix second as a date a person can proofread. UTC, like the window.</summary>
        static string Stamp(long unix)
            => System.DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// The golden bands, and what they do to the economy.
        ///
        /// <para>
        /// <c>GoldenTable</c> already refuses a band that would pay below the base. What
        /// is left is the question a reader cannot ask: <em>how much does this multiply
        /// everything by</em>. The bonus sits inside the credit derivation, so its weighted
        /// average multiplies every credits-per-star figure in the file — and unlike a
        /// chest or an ad, nobody sees it as a line item. A table that quietly raised the
        /// economy by forty percent would look like four harmless-looking rows.
        /// </para>
        /// <para>
        /// So the average is computed and reported, and the actual effect on the catalog
        /// is printed in credits. That is the number a tuning pass needs and the one no
        /// individual band shows.
        /// </para>
        /// </summary>
        static void ValidateGolden(GoldenTable golden, ProgressionTable table,
                                   CatalogIndex index, ContentValidationResult result, bool verbose)
        {
            if (golden == null) { result.Errors.Add("progression.json produced no golden table"); return; }

            if (golden.TotalWeight <= 0)
            {
                result.Errors.Add("the golden bands carry no weight between them, so no glade " +
                                  "would ever be picked");
                return;
            }

            int plainWeight = 0;
            for (int i = 0; i < golden.Bands.Count; i++)
                if (!golden.Bands[i].IsBonus) plainWeight += golden.Bands[i].Weight;

            if (plainWeight == 0)
                result.Errors.Add("every golden band pays a bonus, so every glade pays more than " +
                                  "its reward rule says. That is not a bonus, it is an unannounced " +
                                  "retune of every credit figure in the file — change the rule instead");
            else if (plainWeight * 2 <= golden.TotalWeight)
                result.Warnings.Add("most glades draw a golden bonus. The effect works because it " +
                                    "is rare; at this rate a player learns to expect it and the " +
                                    "ordinary reward starts reading as a punishment");

            // The weighted average, in whole percent, computed the way the odds are.
            long weighted = 0;
            for (int i = 0; i < golden.Bands.Count; i++)
                weighted += (long)golden.Bands[i].Percent * golden.Bands[i].Weight;

            float average = weighted / (float)golden.TotalWeight;

            if (average > 200f)
                result.Errors.Add($"the golden bands average {average:0.#}% — they more than double " +
                                  "every credit reward in the game. Retune the reward rule rather " +
                                  "than hiding a multiplier in the bonus table");
            else if (average > 140f)
                result.Warnings.Add($"the golden bands average {average:0.#}%, which raises every " +
                                    "credit reward by more than two fifths");

            if (!verbose) return;

            var line = new System.Text.StringBuilder("[Glimmer] golden bands:");
            for (int i = 0; i < golden.Bands.Count; i++)
                line.Append("  ").Append(golden.Bands[i].Percent).Append("% at ")
                    .Append(golden.ChanceOf(i).ToString("0.#")).Append('%');
            line.Append("  ·  average ").Append(average.ToString("0.#")).Append('%');

            Debug.Log(line.ToString());

            // What it is actually worth over the shipped catalog, at three stars — the
            // figure a tuning pass is really asking about.
            long plain = 0;
            foreach (var id in index.LevelIds)
                plain += table.RuleFor(index.ChapterOf(id)).CreditsFor(3);

            Debug.Log($"[Glimmer] a full three-star catalog pays {plain} credits before the " +
                      $"golden, and about {(long)(plain * average / 100f)} after it on average");
        }

        /// <summary>
        /// The streak ladder, checked for the things the reader cannot know.
        ///
        /// <para>
        /// <c>StreakTable</c> already refuses anything unreadable — an unknown kind, a zero,
        /// a ladder longer than the cap. What is left is the shape, which is a design
        /// question a reader has no opinion about: a rung that pays less than an earlier one
        /// of the same kind, a lap so short that it comes round before a player notices it,
        /// and a lap that pays nothing at all.
        /// </para>
        /// <para>
        /// It also prints the ladder, and now prints what the lap is worth. A streak is the
        /// one reward a player plans several days around, so the person tuning it needs to
        /// see all of it at once, in the order the player meets it — and since the ladder
        /// laps, the week's total is the number that actually sets the payout rate.
        /// </para>
        /// </summary>
        static void ValidateStreak(StreakTable streak, ContentValidationResult result, bool verbose)
        {
            if (streak == null) { result.Errors.Add("progression.json produced no streak ladder"); return; }

            if (streak.Length < 3)
                result.Warnings.Add($"the streak ladder is only {streak.Length} night(s) long, so the " +
                                    "lap comes round almost immediately and the ladder stops " +
                                    "escalating where a player starts noticing it");

            // Hearts clamp at the cap and boosts do not, so the two are not comparable and
            // only like-for-like rungs are checked. That is enough to catch the mistake
            // that matters: a longer streak paying less than a shorter one. Only within one
            // lap — night eight paying less than night seven is the lap starting over,
            // which is the design rather than a mistake.
            for (int night = 2; night <= streak.Length; night++)
            {
                var rung = streak.Rung(night);
                if (!rung.IsValid) continue;

                for (int earlier = night - 1; earlier >= 1; earlier--)
                {
                    var before = streak.Rung(earlier);
                    if (!before.IsValid || before.Kind != rung.Kind) continue;

                    if (rung.Amount < before.Amount)
                        result.Errors.Add($"streak night {night} pays {rung} but night {earlier} pays " +
                                          $"{before}; a longer streak that is worth less is a " +
                                          "reason to stop rather than to continue");
                    break;
                }
            }

            // What one lap hands over, split by who adjudicates it. The currency half is the
            // half that has a server obligation attached, which is what the note below is for.
            long credits = 0, gems = 0;
            int paying = 0;

            for (int night = 1; night <= streak.Length; night++)
            {
                var rung = streak.Rung(night);
                if (!rung.IsValid) continue;

                paying++;
                if (rung.Kind == ChestDropKind.Credits) credits += rung.Amount;
                if (rung.Kind == ChestDropKind.Gems) gems += rung.Amount;
            }

            if (paying == 0)
            {
                result.Errors.Add("the streak ladder pays nothing on any night, so no night is " +
                                  "ever collectable and the streak page can only ever be empty");
            }

            if (!verbose) return;

            var line = new System.Text.StringBuilder("[Glimmer] streak ladder:");
            for (int night = 1; night <= streak.Length; night++)
            {
                var rung = streak.Rung(night);
                line.Append("  n").Append(night).Append(' ')
                    .Append(rung.IsValid ? rung.ToString() : "—");
            }
            line.Append("  · night ").Append(streak.Length + 1).Append(" begins the lap again");

            Debug.Log(line.ToString());

            if (credits <= 0 && gems <= 0) return;

            // Said every time rather than only on a change, because the failure it warns
            // about is silent: the client draws this ladder from the file, the server pays
            // from config/progression, and a lap retuned here without a re-seed pays the old
            // figure into a wallet while the board advertises the new one.
            Debug.Log($"[Glimmer] one lap pays {credits} credits and {gems} gems. Both are granted " +
                      "by the server from config/progression — run firebase/seed/seed-config.mjs " +
                      "after this change or players will be paid the previous ladder.");
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

                if (!verbose || !report.IsClean || !byId.TryGetValue(report.Id, out var level)) continue;

                var tuning = level.Tuning;

                // The clock and what three stars asks of it, printed rather than left to be
                // worked out: stars are the worse of the moves and the clock, so the number
                // that decides whether a glade is fair is the tap rate the two together imply
                // — and that is not something anyone can read off the JSON.
                string clock = tuning.HasTimeLimit
                    ? $", {tuning.TimeLimitMillis / 1000f:0.#}s clock, " +
                      $"{tuning.GoldThreshold / (tuning.TimeGoldMillis / 1000f):0.00} taps/s for three stars"
                    : ", untimed";

                Debug.Log($"[Glimmer] {report.Id} verified " +
                          $"({level.Layout.Width}x{level.Layout.Height}, par {tuning.Par}{clock})");
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
                // A player stands at keeper level 1 on their first launch, so a gate of 1 is
                // as free as a gate of 0 — and reading only == 0 would pass a roster whose
                // starter had been retuned to 1 while still failing to notice one retuned
                // to 2, which is the case that leaves a new player with nobody to wear.
                if (companion.IsStarter) anyFree = true;

                string portrait = "Assets/Game/Art/Companions/" + companion.Portrait + ".png";
                if (AssetDatabase.LoadAssetAtPath<Sprite>(portrait) == null)
                    result.Errors.Add($"companion '{companion.Id}' has no portrait at {portrait}");

                if (companion.HasAnimation &&
                    !AssetDatabase.IsValidFolder("Assets/Game/Art/Critters/" + companion.Animated))
                    result.Errors.Add($"companion '{companion.Id}' names animation set " +
                                      $"'{companion.Animated}', which is not a folder under Art/Critters");
            }

            if (!anyFree)
                result.Errors.Add("no companion is free at keeper level 1; a new player would " +
                                  "have none to wear");

            ValidateCompanionPrices(companions, result);

            // What the whole shipped catalog is worth, three-starred. Anything above it
            // is unreachable until more glades ship.
            //
            // Reported as one line rather than one per companion on purpose: a roster
            // deliberately built to outlast the current content would otherwise emit
            // dozens of warnings every run, and a validator nobody reads is a validator
            // that has stopped working.
            int reachable = ReachableKeeperLevel(index);
            int beyond = 0, highest = 0;
            var stranded = new List<string>();

            foreach (var companion in companions)
            {
                if (companion.UnlockLevel <= reachable) continue;

                beyond++;
                if (companion.UnlockLevel > highest) highest = companion.UnlockLevel;

                // Gated above what the catalog can reach *and* carrying no price is a
                // companion no player can ever obtain by any route. Before prices existed
                // that was a warning, because the only fix was shipping more glades; now
                // there is a second route, so leaving both closed is an authoring mistake
                // rather than a schedule.
                if (!companion.IsForSale) stranded.Add(companion.Id);
            }

            if (beyond > 0)
                result.Warnings.Add($"{beyond} of {companions.Count} companions unlock above keeper level " +
                                    $"{reachable}, which is all the current catalog can reach " +
                                    $"(highest is {highest}); coins are the only route to them " +
                                    "until more glades ship");

            if (stranded.Count > 0)
                result.Errors.Add($"companion(s) {string.Join(", ", stranded)} unlock above keeper level " +
                                  $"{reachable} and carry no unlockCost, so no player can obtain them " +
                                  "by any route; give them a price or lower the gate");

            if (verbose)
                Debug.Log($"[Glimmer] {companions.Count} companions, " +
                          $"{ReachableCount(companions, reachable)} reachable at keeper level {reachable}");
        }

        /// <summary>
        /// The grove: its land, its residents and its shop.
        ///
        /// <para>
        /// Two things here are errors and everything else warns, which is the same line
        /// <see cref="ValidateCompanionPrices"/> draws. A validator is not entitled to
        /// overrule an economy decision; it is entitled to state consequences that cannot be
        /// seen by reading the file. What it <em>is</em> entitled to refuse is a rule
        /// violation and a grove nobody can use.
        /// </para>
        /// <para>
        /// The rule is that a resident is never for sale. That is the whole endowment
        /// argument for the feature - a resident is proof of a glade the player finished, and
        /// one that can be bought turns a record into a receipt - and a rule that matters is
        /// a rule the build proves. <c>HomesteadMapper</c> already drops such a price on the
        /// way in, so the game would ship correct either way; this is what tells whoever
        /// authored it that their row did not mean what they thought.
        /// </para>
        /// </summary>
        static void ValidateHomestead(EditorContent content, ContentValidationResult result, bool verbose)
        {
            var grove = content.Homestead;

            if (grove.Floor.IsEmpty && grove.PieceCount == 0)
            {
                result.Warnings.Add("no grove catalog at " + ContentPaths.Homestead +
                                    "; the Grovement will have nothing to show");
                return;
            }

            ValidateGroveFloor(grove, result, verbose);
            ValidateHomesteadPieces(content, grove, result);
            ValidateHomesteadHome(grove, result, verbose);

            if (verbose)
                Debug.Log($"[Glimmer] grove: {grove.Floor.Cols}x{grove.Floor.Rows} floor, " +
                          $"{grove.SlotCount} tile(s), {grove.Floor.Regions.Count} region(s), " +
                          $"{grove.PieceCount} piece(s)");
        }

        /// <summary>
        /// The ground: that there is some, that a new player has a share of it, and that the
        /// regions divide it without overlapping or leaving holes.
        ///
        /// <para>
        /// <b>Two of these are errors and they are the two that ship a broken first launch.</b>
        /// A floor with no free region opens the Grovement onto a screen the player owns nothing
        /// on, and a hall standing on ground they have not bought is a home they can see and
        /// never reach. Both look perfectly authored in the file — the numbers are all present
        /// and all plausible — which is exactly the class of mistake a build gate is for.
        /// </para>
        /// <para>
        /// Overlapping regions are an error for a different reason: a tile in two regions is a
        /// tile whose owner depends on which one the reader happened to check first, and a
        /// player who bought one of them would see the ground unlock or not depending on the
        /// order of a file.
        /// </para>
        /// </summary>
        static void ValidateGroveFloor(HomesteadCatalog grove, ContentValidationResult result,
                                       bool verbose)
        {
            var floor = grove.Floor;

            if (floor.IsEmpty)
            {
                result.Errors.Add("the grove has no floor; there is nowhere to build");
                return;
            }

            RequireGroveArt(floor.TileArt, "grove floor", false, result, optional: true);
            CheckTileProjection(floor, result);

            // Which region owns each tile, built once — the same map answers overlap, holes and
            // the two named tiles, and walking the regions per question would be four passes
            // over a field that can be forty thousand tiles.
            var owner = new Dictionary<string, GroveRegion>(StringComparer.Ordinal);
            int overlaps = 0;

            foreach (var region in floor.Regions)
            {
                for (int c = region.Col; c < region.Col + region.Cols; c++)
                    for (int r = region.Row; r < region.Row + region.Rows; r++)
                    {
                        string id = GroveFloor.TileId(c, r);
                        if (owner.TryGetValue(id, out var already))
                        {
                            if (overlaps++ == 0)
                                result.Errors.Add($"grove regions '{already.Id}' and '{region.Id}' " +
                                                  $"both hold tile {id}; who owns it would depend " +
                                                  "on the order of the file");
                            continue;
                        }

                        owner[id] = region;
                    }
            }

            if (overlaps > 1)
                result.Errors.Add($"{overlaps} grove tiles are in more than one region");

            int loose = floor.TileCount - owner.Count;
            if (loose > 0)
                result.Warnings.Add($"{loose} grove tile(s) belong to no region, so nobody can " +
                                    "ever own them; they are drawn locked for ever");

            int starters = 0;
            foreach (var region in floor.Regions)
                if (region.IsStarter) starters++;

            if (starters == 0)
                result.Errors.Add("no grove region is free from the first launch; a new player " +
                                  "would open the Grovement owning none of it");

            RequireStarterTile(floor, owner, floor.HallTile, "hall", true, result);
            RequireStarterTile(floor, owner, floor.StarterTile, "starter companion", false, result);

            long land = 0;
            foreach (var region in floor.Regions) land += region.Cost;

            long daily = DailyCreditIncome(ProgressionRules.Table);
            if (verbose && daily > 0)
                Debug.Log($"[Glimmer] grove land: {starters} free region(s), " +
                          $"{floor.Regions.Count - starters} for sale - {land} credits in all, " +
                          $"about {land / daily} day(s) of play at {daily} credits a day");
        }

        /// <summary>
        /// That the floor's art is drawn at the projection the grid steps by.
        ///
        /// <para>
        /// <b>This is the check that would have caught the floor not lining up.</b>
        /// <c>GroveFloor.TileFaceRatio</c> is a constant describing a picture, and a constant
        /// describing a picture is only true until somebody swaps the picture. It shipped as 0.5
        /// on the assumption that "isometric" meant a 2:1 grid; the pack is drawn at 0.5628, so
        /// every tile overlapped its neighbour by six pixels vertically and the floor could not
        /// be made to tessellate by any amount of nudging.
        /// </para>
        /// <para>
        /// Measured off the alpha rather than trusted: the widest opaque row is the tile's
        /// horizontal axis, and the distance from the top of the image to that row is half the
        /// top face. That is the same thing <c>HomesteadMap</c> had to learn about a plot's
        /// height — the number the layout must agree with lives in a PNG nobody can see from the
        /// code, so the code has to go and look.
        /// </para>
        /// </summary>
        static void CheckTileProjection(GroveFloor floor, ContentValidationResult result)
        {
            if (string.IsNullOrEmpty(floor.TileArt)) return;

            string path = "Assets/Game/Art/" + floor.TileArt + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return;

            if (!texture.isReadable)
            {
                // Not a fault: art is imported without CPU read access on purpose. The check
                // simply cannot run, and saying so is better than passing silently.
                result.Warnings.Add($"the grove floor's tile at {path} is not readable, so its " +
                                    "projection could not be checked against " +
                                    $"GroveFloor.TileFaceRatio ({GroveFloor.TileFaceRatio:0.0000})");
                return;
            }

            int w = texture.width, h = texture.height;
            int widestRow = -1, widest = 0, top = -1;

            for (int y = h - 1; y >= 0; y--)
            {
                int count = 0;
                for (int x = 0; x < w; x++)
                    if (texture.GetPixel(x, y).a > .06f) count++;

                if (count > 0 && top < 0) top = y;
                if (count > widest) { widest = count; widestRow = y; }
            }

            if (top < 0 || widestRow < 0) return;

            // Unity's texture origin is bottom-left, so "down from the top" counts backwards.
            float face = (top - widestRow) * 2f;
            float ratio = face / w;

            if (Mathf.Abs(ratio - GroveFloor.TileFaceRatio) <= .01f) return;

            result.Errors.Add($"the grove floor's tile is drawn at a face ratio of {ratio:0.0000} " +
                              $"({w}x{(int)face}) and the grid steps by " +
                              $"{GroveFloor.TileFaceRatio:0.0000}; the floor will not tessellate. " +
                              "Change GroveFloor.TileFaceRatio to match the art, or re-cut the art");
        }

        /// <summary>
        /// That a named tile is on the field and, when it must be, on ground a new player owns.
        ///
        /// The hall has to be reachable on the first launch or the feature opens onto a padlock
        /// where the house should be. The starter companion is only a warning: a grove that
        /// begins with nobody standing in it is a poorer first impression, not a broken one.
        /// </summary>
        static void RequireStarterTile(GroveFloor floor, Dictionary<string, GroveRegion> owner,
                                       string tileId, string what, bool required,
                                       ContentValidationResult result)
        {
            if (string.IsNullOrEmpty(tileId))
            {
                if (required)
                    result.Errors.Add($"the grove floor names no tile for the {what}");
                else
                    result.Warnings.Add($"the grove floor names no tile for the {what}");
                return;
            }

            if (!owner.TryGetValue(tileId, out var region))
            {
                result.Errors.Add($"the grove's {what} stands on {tileId}, which belongs to no " +
                                  "region and can never be owned");
                return;
            }

            if (!region.IsStarter)
                result.Errors.Add($"the grove's {what} stands on {tileId}, in region " +
                                  $"'{region.Id}', which costs {region.Cost}; a new player would " +
                                  "see it behind a padlock");
        }

        /// <summary>
        /// The home ladder: one hearth, tiers that do not collide, and a first rung that is free.
        ///
        /// <para>
        /// Every failure here is silent in the game, which is why they are checked. A catalog
        /// with dwellings and no hearth draws no home anywhere and looks exactly like a catalog
        /// with no dwellings; two rungs on the same tier make "the best one owned" depend on the
        /// order of the file; and a priced first rung means a new player opens the Grovement onto
        /// an island with a ring where the house should be, which is the emptiest possible first
        /// impression of the feature.
        /// </para>
        /// </summary>
        static void ValidateHomesteadHome(HomesteadCatalog grove, ContentValidationResult result,
                                          bool verbose)
        {
            var dwellings = new List<HomesteadPiece>();
            foreach (var piece in grove.Pieces)
                if (piece.IsDwelling) dwellings.Add(piece);

            // The floor has exactly one hall tile or none, so this is a fact about the floor
            // rather than a count over slots — but the checks below still read as a count,
            // because "a home with nowhere to stand" and "somewhere to stand with no home" are
            // still the two ways this goes wrong.
            int hearths = string.IsNullOrEmpty(grove.Floor.HallTile) ? 0 : 1;

            if (dwellings.Count == 0)
            {
                if (hearths > 0)
                    result.Errors.Add("the grove has a hearth but no home to stand on it; every " +
                                      "island would draw an empty ring where the house goes");
                return;
            }

            if (hearths == 0)
                result.Errors.Add($"the grove has {dwellings.Count} home(s) and no hearth slot to " +
                                  "draw one on; they would be bought and never seen");

            if (hearths > 1)
                result.Warnings.Add($"{hearths} hearth slots: the same home draws on every one of " +
                                    "them, which is legitimate but is usually a copied template");

            var seen = new Dictionary<int, string>();
            var first = default(HomesteadPiece);

            foreach (var piece in dwellings)
            {
                if (seen.TryGetValue(piece.Tier, out string other))
                    result.Errors.Add($"grove homes '{other}' and '{piece.Id}' are both tier " +
                                      $"{piece.Tier}; which one a player lives in would depend on " +
                                      "the order of the file");
                else
                    seen[piece.Tier] = piece.Id;

                if (!first.IsValid || piece.Tier < first.Tier) first = piece;
            }

            if (first.IsValid && (first.IsForSale || first.HasRequirement))
                result.Errors.Add($"the first home '{first.Id}' is not free; a new grove would open " +
                                  "with nothing on its hearth");

            if (verbose && first.IsValid)
            {
                long ladder = 0;
                foreach (var piece in dwellings) ladder += piece.Cost;

                Debug.Log($"[Glimmer] grove home ladder: {dwellings.Count} rung(s), " +
                          $"{ladder} credits from '{first.Id}' to the top");
            }
        }

        static void ValidateHomesteadPieces(EditorContent content, HomesteadCatalog grove,
                                            ContentValidationResult result)
        {
            int starters = 0, forSale = 0, earned = 0, residents = 0;

            foreach (var piece in grove.Pieces)
            {
                if (piece.IsStarter) starters++;
                if (piece.IsForSale) forSale++;
                if (piece.HasRequirement) earned++;
                if (piece.IsResident) residents++;

                RequireGroveArt(piece.Art, $"grove piece '{piece.Id}'", piece.Animated, result);

                if (piece.RequiresLevel.IsValid && !content.Index.Contains(piece.RequiresLevel))
                    result.Warnings.Add($"grove piece '{piece.Id}' is earned by clearing " +
                                        $"'{piece.RequiresLevel}', which the catalog does not carry; " +
                                        "it can only be bought, if it has a price");

                if (piece.RequiresChapter.IsValid && !content.Index.ContainsChapter(piece.RequiresChapter))
                    result.Warnings.Add($"grove piece '{piece.Id}' is earned by finishing chapter " +
                                        $"'{piece.RequiresChapter}', which the catalog does not carry");

                if (piece.Lift < -1f || piece.Lift > 1.5f)
                    result.Warnings.Add($"grove piece '{piece.Id}' has lift {piece.Lift:0.00}; " +
                                        "it will draw well away from its slot");
            }

            // An error for the plots' reason: a picker with nothing in it on a first visit is a
            // feature that ships looking broken, and every route out of that state takes play.
            if (starters == 0)
                result.Errors.Add("no grove piece is free from the first launch; a new player " +
                                  "would open the picker onto an empty list");

            if (residents == 0)
                result.Warnings.Add("the grove has no residents; the companion roster is where " +
                                    "they come from, so an empty roster empties a whole shelf");

            ValidateGroveResidents(grove, result);
            ValidateGroveShelves(grove, result);

            // The shop, against the income that has to pay for it. Excludes rewarded ads for
            // ValidateCompanionPrices' reason: ads are the accelerator, so counting them in the
            // baseline lets a ladder only a six-video-a-day player can climb pass as ordinary.
            long daily = DailyCreditIncome(ProgressionRules.Table);
            if (daily <= 0 || forSale == 0) return;

            long total = 0;
            foreach (var piece in grove.Pieces) total += piece.Cost;

            result.Warnings.Add($"grove shop: {forSale} for sale, {earned} earned by playing, " +
                                $"{starters} free - {total} credits in all, about {total / daily} day(s) " +
                                $"of play at {daily} credits a day");
        }

        /// <summary>
        /// The residents, which are the companion roster wearing a second hat.
        ///
        /// <para>
        /// Two things can go wrong and neither is visible in either file on its own. An
        /// authored piece wearing the <b>reserved prefix</b> would put two permanent id spaces
        /// back together — they were minted independently and already collided once, which is
        /// why a resident's id is a companion's id prefixed. And a <b>retired resident</b>
        /// whose replacement has left the roster would
        /// leave the rewrite in <c>GroveResidents</c> pointing at nothing, which quietly empties
        /// a slot somebody arranged. Both are errors: they cost save data, and both are exactly
        /// the class of mistake that only shows up on a device.
        /// </para>
        /// </summary>
        static void ValidateGroveResidents(HomesteadCatalog grove, ContentValidationResult result)
        {
            var companions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var companion in AvatarCatalog.All)
                if (companion.IsValid) companions.Add(companion.Id);

            foreach (var piece in grove.Authored)
            {
                if (!piece.IsValid) continue;

                if (GroveResidents.IsResidentId(piece.Id))
                    result.Errors.Add($"grove piece '{piece.Id}' uses the reserved prefix " +
                                      $"'{GroveResidents.Prefix}', which belongs to residents " +
                                      "projected from the companion roster");
            }

            foreach (string retired in GroveResidents.RetiredIds)
            {
                GroveResidents.TryRename(retired, out string became);

                if (!companions.Contains(GroveResidents.CompanionIdOf(became)))
                    result.Errors.Add($"the retired grove resident '{retired}' is rewritten to " +
                                      $"'{became}', which the roster no longer carries; every " +
                                      "slot holding it would empty");
            }
        }

        /// <summary>
        /// That every shop shelf has something to draw its tab with.
        ///
        /// A tab wears the cheapest thing on its shelf, so an empty shelf is a blank plate in a
        /// row a player navigates by — and it is generated art, so the blank would appear on a
        /// device and not in the Editor. A warning rather than an error: a catalog legitimately
        /// grows one shelf at a time, and the shop draws an empty grid perfectly well.
        /// </summary>
        static void ValidateGroveShelves(HomesteadCatalog grove, ContentValidationResult result)
        {
            foreach (var shelf in GroveShelves.All)
                if (GroveShelves.HasAtlas(shelf) && !HomesteadCatalog.Emblem(grove, shelf).IsValid)
                    result.Warnings.Add($"the grove shop's '{GroveShelves.Key(shelf)}' shelf is " +
                                        "empty; its tab will have no emblem");
        }

        /// <summary>
        /// That a piece or plot's art is actually in the project.
        ///
        /// A missing address draws a solid white rectangle rather than nothing (invariant 7b),
        /// so this is the difference between a shop cell that is obviously incomplete and one
        /// that looks like a rendering bug. It checks the asset database directly rather than
        /// leaning on <c>AddressableAudit</c>, because that runs only where Addressables is
        /// installed and this has to hold in every project state.
        /// </summary>
        static void RequireGroveArt(string art, string owner, bool animated,
                                    ContentValidationResult result, bool optional = false)
        {
            if (string.IsNullOrEmpty(art))
            {
                // The floor may name no tile art at all, which draws a generated diamond — that
                // is how the feature works before the ground has been cut, and it is a choice
                // rather than an omission.
                if (!optional) result.Errors.Add($"{owner} names no art");
                return;
            }

            string folder = "Assets/Game/Art/" + art;

            if (animated)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                    result.Errors.Add($"{owner} names animation set '{art}', " +
                                      "which is not a folder under Art/");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Sprite>(folder + ".png") == null)
                result.Errors.Add($"{owner} has no sprite at {folder}.png");
        }

        /// <summary>
        /// The prices, against the income that has to pay them.
        ///
        /// <para>
        /// Every check here warns rather than errors, with one exception, because a price is
        /// an economy decision and the validator is not entitled to overrule one — what it is
        /// entitled to do is state the consequence, since none of these are visible by
        /// reading the manifest. The exception is a price a player can reach before they can
        /// reach the companion's own gate <em>and</em> before the seed runs out, which is not
        /// a tuning choice but a companion that is effectively free.
        /// </para>
        /// <para>
        /// The daily figure deliberately excludes rewarded ads. Ads are the accelerator, so
        /// including them in the baseline would let a price that is only affordable to
        /// somebody watching six videos a day pass as ordinary.
        /// </para>
        /// </summary>
        static void ValidateCompanionPrices(IReadOnlyList<AvatarDefinition> companions,
                                            ContentValidationResult result)
        {
            var table = ProgressionRules.Table;

            long daily = DailyCreditIncome(table);
            if (daily <= 0) return;                 // nothing published to judge against

            int forSale = 0;
            long total = 0;
            int lastCost = 0, lastLevel = -1;
            string lastId = null;

            foreach (var companion in companions)
            {
                if (!companion.IsForSale)
                {
                    // A companion reachable by play and not for sale is fine and deliberate;
                    // one that is neither is caught by the stranded check above.
                    continue;
                }

                forSale++;
                total += companion.UnlockCost;

                // Free in practice: buyable out of the account seed before the player has
                // played at all, on something the game meant to gate.
                if (companion.UnlockLevel > 1 && companion.UnlockCost <= Currency.SeedCredits / 2)
                    result.Errors.Add($"companion '{companion.Id}' costs {companion.UnlockCost}, " +
                                      $"under half the {Currency.SeedCredits}-coin account seed, so it " +
                                      "is gated at level " + companion.UnlockLevel +
                                      " and free on the first launch; raise the price or drop the gate");

                // A later gate that costs less than an earlier one inverts the ladder: the
                // grid would show a cheaper price beside a rarer companion, and the roster
                // stops reading as a progression.
                if (lastId != null && companion.UnlockLevel > lastLevel && companion.UnlockCost < lastCost)
                    result.Warnings.Add($"companion '{companion.Id}' unlocks later than '{lastId}' " +
                                        $"(level {companion.UnlockLevel} vs {lastLevel}) but costs less " +
                                        $"({companion.UnlockCost} vs {lastCost}); the price ladder is inverted");

                lastCost = companion.UnlockCost;
                lastLevel = companion.UnlockLevel;
                lastId = companion.Id;
            }

            if (forSale == 0)
            {
                result.Warnings.Add("no companion carries an unlockCost, so coins buy nothing; " +
                                    "the roster is level-gated only");
                return;
            }

            // What the whole roster is worth in days of ordinary play. Logged rather than
            // judged: it is the one number that says whether the sink outlasts the content,
            // and no threshold on it would be anything but a guess.
            Debug.Log($"[Glimmer] {forSale} companions for sale, {total} coins in total — about " +
                      $"{total / daily} days of play at roughly {daily} coins a day, " +
                      "before any rewarded video");

            var cheapest = AvatarCatalog.CheapestUnheld(_ => false);
            if (cheapest.IsValid)
            {
                long days = (cheapest.UnlockCost + daily - 1) / daily;
                if (days > 7)
                    result.Warnings.Add($"the cheapest companion ('{cheapest.Id}', " +
                                        $"{cheapest.UnlockCost}) is about {days} days of play away; " +
                                        "nothing on the roster teaches a new player that coins buy friends");
            }
        }

        /// <summary>
        /// Credits an engaged player collects in a day without watching a video: every daily
        /// chest's guaranteed contents plus its expected bonus, and a streak rung amortised
        /// over the ladder's lap.
        ///
        /// Read from the published tables rather than written down, so a retune moves this
        /// with it — the same rule every explanatory panel in the game follows.
        /// </summary>
        static long DailyCreditIncome(ProgressionTable table)
        {
            long daily = 0;

            var chests = table.Daily;
            for (int i = 0; i < chests.ChestCount; i++)
                daily += ExpectedCredits(chests.Chest(i));

            var streak = table.Streak;
            if (streak.Length > 0)
            {
                long lap = 0;
                for (int night = 1; night <= streak.Length; night++)
                {
                    var rung = streak.Rung(night);
                    if (rung.Kind == ChestDropKind.Credits) lap += rung.Amount;
                }

                daily += lap / streak.Length;
            }

            return daily;
        }

        /// <summary>
        /// Credits one chest is worth on average: every guaranteed band's midpoint, plus each
        /// credit option's midpoint weighted by the chance of drawing it.
        ///
        /// An expectation rather than the floor, because the floor understates a chest whose
        /// bonus is usually credits — and understating income here would let a price that is
        /// genuinely two weeks away pass as one week.
        /// </summary>
        static long ExpectedCredits(ChestDefinition chest)
        {
            if (chest == null) return 0;

            double credits = 0;

            for (int i = 0; i < chest.Guaranteed.Count; i++)
            {
                var band = chest.Guaranteed[i];
                if (band.Kind == ChestDropKind.Credits) credits += (band.Min + band.Max) * .5;
            }

            for (int i = 0; i < chest.Options.Count; i++)
            {
                var option = chest.Options[i];
                if (option.Band.Kind != ChestDropKind.Credits) continue;

                credits += (option.Band.Min + option.Band.Max) * .5 * (chest.ChanceOf(i) / 100.0);
            }

            return (long)credits;
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

            // So are a grove region's and a grove piece's, for the same reason and with the
            // same consequence - a piece added without its string ships as "ui.piece.oak"
            // written across a shop cell.
            foreach (var region in content.Homestead.Floor.Regions)
                Require(table, region.NameKey, $"grove region '{region.Id}'", result);

            foreach (var piece in content.Homestead.Pieces)
                Require(table, piece.NameKey, $"grove piece '{piece.Id}'", result);

            // And a shop product's and a good's, for the same reason and with a sharper
            // consequence than any of the above: a product with no string is a card selling
            // "store.product.gg_gems_3" for real money.
            var store = ProgressionRules.Table.Store;

            foreach (var product in store.Products)
                Require(table, product.NameKey, $"store product '{product.Id}'", result);

            foreach (var good in store.Goods)
                Require(table, good.NameKey, $"store good '{good.Id}'", result);

            // So are a tip's, and this is the only place that can prove they exist. A
            // mechanic added without its two strings compiles, validates and ships; the
            // first player to reach the glade that teaches it reads "ui.tip.<id>.title".
            foreach (var mechanic in Mechanic.TeachingOrder)
            {
                Require(table, mechanic.TitleKey, $"mechanic '{mechanic.Id}'", result);
                Require(table, mechanic.BodyKey, $"mechanic '{mechanic.Id}'", result);
            }

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

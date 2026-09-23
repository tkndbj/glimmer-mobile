using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GlimmerGrove.Ads;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Daily;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using GlimmerGrove.Utilities;
using GlimmerGrove.Wards;
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
            ValidateChapterModes(load, result);
            ValidateCompanions(load.Index, result, verbose);
            ValidateLevels(load, result, verbose);
            ValidateChapterMaps(load, result);
            ValidateLocalisation(load, result);
            ValidateProgression(load.Index, result, verbose);
            ValidateChallenges(result, verbose);
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
        /// Every level of a chapter is the mode the manifest says the chapter is.
        ///
        /// <para>
        /// <b>The failure this catches is completely silent, and it has happened.</b> A
        /// chapter's mode lives in <c>manifest.json</c> (invariant 20) and decides three
        /// things: which screen opens its levels, which lane of the switcher it appears
        /// under, and — through <c>LevelUnlock.GateFor</c> — which chapter's stars unlock
        /// it. A weave chapter whose entry does not say <c>"mode": "weave"</c> is indexed
        /// as a glade chapter, and <em>nothing else refuses it</em>: every level parses,
        /// every board is proved solvable, every string resolves and every address loads.
        /// What ships is a chapter gated on a stranger's stars, filed under the wrong tab,
        /// routed to a screen that cannot play it.
        /// </para>
        /// <para>
        /// <c>Sync Manifest</c> now derives the field from the body rather than waiting for
        /// somebody to type it, which is invariant 4a's rule for the level list applied to
        /// the one field of an entry that was still hand-written. This is the other half of
        /// that bargain and the half with teeth: deriving makes the mistake unlikely, and
        /// only a check proves it did not happen anyway — a manifest is a text file, and the
        /// one thing this project has learned twice is that a step somebody has to remember
        /// is a step that gets skipped.
        /// </para>
        /// <para>
        /// The rule itself is <see cref="ChapterModeValidator"/>, in Domain, so both callers
        /// ask one copy of it and it can be proved offline against the chapters that ship.
        /// </para>
        /// </summary>
        static void ValidateChapterModes(EditorContent content, ContentValidationResult result)
        {
            foreach (var chapter in content.Index.Chapters)
            {
                if (!content.Catalog.TryResidentChapter(chapter.Id, out var body)) continue;

                if (ChapterModeValidator.TryDisagreement(chapter.Id, chapter.Mode, body.Levels,
                                                        out var issue))
                    result.Errors.Add(issue.Message);
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
            ValidateHearts(table.Hearts, index, table.Store, result, verbose);
            ValidateHints(table.Hints, table.Ads, result, verbose);
            ValidatePrompts(table.Prompts, result, verbose);
            ValidateChapterGate(table.ChapterGate, index, result, verbose);
            ValidateKeeperWalls(table, index, result, verbose);
            ValidateContinue(table.Continue, table.Store, result, verbose);
            ValidateEndless(table.Endless, table, index, result, verbose);
            ValidateXpBoost(table.XpBoost, table.Ads, result, verbose);
            ValidateDailyChests(table.Daily, table.Hearts, result, verbose);
            ValidateUtilities(table.Utilities, table.Daily, result, verbose);
            ValidateTasks(table.Tasks, table.Utilities, table.Hearts, result, verbose);
            ValidateRanks(table.Ranks, index, result, verbose);
            ValidateWards(table.Wards, result, verbose);
            ValidateStreak(table.Streak, result, verbose);
            ValidateGolden(table.Golden, table, index, result, verbose);
            ValidateWheel(table.Ads, result, verbose);
            ValidateEvents(index, result, verbose);
            ValidateStore(table, result, verbose);

            if (!verbose) return;

            long maximumXp = PerfectXp(table, index);
            var reachable = table.LevelFor(maximumXp);
            Debug.Log($"[Glimmer] progression verified: {index.Count} glade(s) at three stars " +
                      $"is {maximumXp} XP, reaching level {reachable.Level} of {table.MaxLevel}");
        }

        /// <summary>
        /// Every XP the shipped catalog can pay: three stars on every glade in it.
        ///
        /// <b>The ceiling on what any account can ever reach</b>, because XP derives from the
        /// star ledger and from nothing else (invariant 9) — so it is the number every keeper
        /// wall has to be checked against, and the reason that check cannot be made from
        /// <c>progression.json</c> alone.
        /// </summary>
        static long PerfectXp(ProgressionTable table, CatalogIndex index)
        {
            long xp = 0;
            foreach (var id in index.LevelIds)
                xp += table.RuleFor(index.ChapterOf(id)).XpFor(3);

            return xp;
        }

        /// <summary>
        /// The keeper walls the manifest puts in front of chapters, checked against the catalog
        /// that has to pay for them.
        ///
        /// <para>
        /// <b>The only way this goes wrong is invisible in either file alone</b>, which is
        /// <c>ValidateChapterGate</c>'s complaint with the units changed. A wall is one integer
        /// in <c>manifest.json</c>; what can reach it is every reward rule in
        /// <c>progression.json</c> multiplied by every glade in the catalog — so a wall above
        /// that ceiling is a lane padlocked for the life of the build, with the manifest, the
        /// index, the map and the hub all perfectly correct. It has happened to the home ladder
        /// and to the turret shelf already, both deliberately; what must not happen is its
        /// happening by accident.
        /// </para>
        /// <para>
        /// <b>An error above the level curve's own ceiling and a warning above the catalog's.</b>
        /// The first is unreachable by arithmetic and can only be a mistake. The second is a
        /// decision somebody may genuinely want — a wall meant for content that has not shipped
        /// yet is exactly how the home ladder's rungs are authored — so it is said loudly and
        /// never refused.
        /// </para>
        /// <para>
        /// Reported in full even when nothing is wrong, for <c>ValidateChapterGate</c>'s reason:
        /// a wall decides whether a whole way of playing is on the screen, and nobody should
        /// have to open a JSON file to find out where it stands.
        /// </para>
        /// </summary>
        static void ValidateKeeperWalls(ProgressionTable table, CatalogIndex index,
                                        ContentValidationResult result, bool verbose)
        {
            if (table == null || index == null) return;

            var reachable = table.LevelFor(PerfectXp(table, index));

            foreach (var chapter in index.Chapters)
            {
                int wall = chapter.MinKeeperLevel;
                if (wall <= 0) continue;

                if (wall > table.MaxLevel)
                {
                    result.Errors.Add($"chapter '{chapter.Id}' asks for keeper level {wall}, " +
                                      $"above the {table.MaxLevel} the curve tops out at, so it " +
                                      "can never be opened by anybody");
                    continue;
                }

                if (wall > reachable.Level)
                    result.Warnings.Add($"chapter '{chapter.Id}' asks for keeper level {wall} " +
                                        $"and three stars on all {index.Count} shipped glade(s) " +
                                        $"reaches only level {reachable.Level}, so nobody can " +
                                        "open it until more content ships");

                if (verbose)
                    Debug.Log($"[Glimmer] keeper wall: '{chapter.Id}' " +
                              $"({chapter.Mode.Value}/{chapter.Track.Value}) opens at keeper " +
                              $"level {wall}, against {reachable.Level} reachable today");
            }
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
            // A season's pass used to be a store product, and this is where the two files were
            // held to naming each other. It is priced in gems now — one number in the manifest,
            // with nothing on the other side of it to drift from — so the check that survives
            // is about the *price*, and it lives with the rest of the season's rules below.

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
            ValidateHeartContainers(catalog, table, result);

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
        /// The heart containers: a ladder the money ladder above cannot see.
        ///
        /// <para>
        /// A container grants no currency, so its value per unit of money is zero and
        /// <see cref="ValidateStoreLadder"/> would fail every shelf it was ranked on — which
        /// is why it is exempt there (as a non-consumable) and checked here instead. What has
        /// to hold is the same claim in the units this product is sold in: a dearer vessel
        /// holds more. A rung that costs more and holds no more is a card nobody can be right
        /// to buy, and it is invisible in the file because the two numbers are in different
        /// columns.
        /// </para>
        /// <para>
        /// The check against the free cap is the one that matters most and is an error rather
        /// than a warning: a container at or below the cap a player already has takes real
        /// money and changes nothing they can see. Both numbers are content, so the mistake is
        /// one config push away at any time — raise the free tuning past a shipped container
        /// and it happens on its own, to everybody, with no code change to notice.
        /// </para>
        /// </summary>
        static void ValidateHeartContainers(StoreCatalog catalog, ProgressionTable table,
                                            ContentValidationResult result)
        {
            var vessels = new List<StoreProduct>();
            foreach (var product in catalog.Products)
                if (product.IsContainer) vessels.Add(product);

            if (vessels.Count == 0) return;

            vessels.Sort((a, b) => a.ReferenceUsdCents.CompareTo(b.ReferenceUsdCents));

            int freeCap = table.Hearts.RefillCap;
            int ceiling = table.Hearts.Ceiling;

            foreach (var vessel in vessels)
            {
                if (vessel.HeartCapacity <= freeCap)
                    result.Errors.Add(
                        $"store product '{vessel.Id}' sells a heart capacity of " +
                        $"{vessel.HeartCapacity}, at or below the free refill cap of {freeCap}; " +
                        "it would take real money and change nothing the player can see");

                // The timer would carry somebody past the most they may hold, so every grant
                // would be refused while the clock kept paying. HeartContainerLedger holds the
                // cap to the ceiling, so what the player would actually get is less than the
                // card promised.
                if (vessel.HeartCapacity > ceiling)
                    result.Errors.Add(
                        $"store product '{vessel.Id}' sells a heart capacity of " +
                        $"{vessel.HeartCapacity}, above the published ceiling of {ceiling}; the " +
                        "ledger holds it to the ceiling, so the card promises more than it gives");
            }

            for (int i = 1; i < vessels.Count; i++)
            {
                if (vessels[i].ReferenceUsdCents == vessels[i - 1].ReferenceUsdCents)
                {
                    result.Warnings.Add(
                        $"store: heart containers '{vessels[i - 1].Id}' and '{vessels[i].Id}' are " +
                        "the same price; which one the shelf draws first is then arbitrary");
                    continue;
                }

                if (vessels[i].HeartCapacity > vessels[i - 1].HeartCapacity) continue;

                result.Errors.Add(
                    $"store: heart container '{vessels[i].Id}' costs more than " +
                    $"'{vessels[i - 1].Id}' and holds {vessels[i].HeartCapacity} against " +
                    $"{vessels[i - 1].HeartCapacity}. A ladder that stops getting better is a " +
                    "rung nobody can be right to buy");
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
                    // A container is priced in the same money and sold in a different unit,
                    // so printing it as "0 gems + 0 credits" would be true and useless — and
                    // the number worth reading is the one it moves the free cap to.
                    if (product.IsContainer)
                    {
                        Debug.Log($"[Glimmer] store {shelf} #{product.Tier}: '{product.Id}' " +
                                  $"hearts refill to {product.HeartCapacity} (from " +
                                  $"{table.Hearts.RefillCap}) at " +
                                  $"${product.ReferenceUsdCents / 100f:0.00}, once and for ever");
                        continue;
                    }

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
        /// Gems an engaged player collects in a day: every task chest's expected gems plus a
        /// streak night amortised over the ladder's lap — which since the ladder pays chests
        /// is itself a chest expectation as often as it is a figure.
        ///
        /// The gem half of <see cref="DailyCreditIncome"/>, and it exists for the same
        /// reason — a price is only meaningful beside the income that has to pay it, and
        /// gems are the currency the whole supplies shelf is priced in.
        /// </summary>
        static long DailyGemIncome(ProgressionTable table)
        {
            long daily = 0;

            daily += TaskIncome(table.Tasks, ExpectedGems);

            daily += StreakIncome(table.Streak, ChestDropKind.Gems, ExpectedGems);

            return daily;
        }

        /// <summary>
        /// What one night of the streak pays in one currency, amortised over the lap.
        ///
        /// <para>
        /// Two halves, because a night pays a figure <em>or</em> a chest, and a chest's worth
        /// is an expectation rather than an amount. Counting only the figures would have
        /// under-read this ladder by the larger half the day chests went on it, and the
        /// symptom is not a wrong log line: every price in this file is checked against the
        /// income that has to pay it, so an under-read income reports companions and homes as
        /// further away than they are.
        /// </para>
        /// </summary>
        static long StreakIncome(StreakTable streak, ChestDropKind kind,
                                 Func<ChestDefinition, long> worth)
        {
            if (streak == null || streak.Length <= 0) return 0;

            long lap = 0;

            for (int night = 1; night <= streak.Length; night++)
            {
                var rung = streak.Rung(night);

                if (rung.IsChest) lap += worth(rung.Tier.Chest);
                else if (rung.Kind == kind) lap += rung.Amount;
            }

            return lap / streak.Length;
        }

        /// <summary>
        /// What a day of tasks pays in one currency: each slate's live tasks at their tier's
        /// expectation, averaged, times what a period deals, over the period's days.
        /// </summary>
        static long TaskIncome(Tasks.TaskTable tasks, Func<ChestDefinition, long> worth)
        {
            if (tasks == null) return 0;

            double total = 0;
            foreach (var period in Tasks.TaskPeriods.All)
            {
                double sum = 0;
                int live = 0;
                foreach (var task in tasks.Slate(period))
                {
                    if (task.Retired) continue;
                    sum += worth(task.Tier.Chest);
                    live++;
                }
                if (live == 0) continue;

                int dealt = Math.Min(tasks.ActivePerPeriod, live);
                int days = period == Tasks.TaskPeriod.Weekly ? Daily.WeeklyRules.DaysPerWeek : 1;
                total += sum / live * dealt / days;
            }

            return (long)total;
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
        static void ValidateHearts(HeartRuleTable hearts, CatalogIndex index, StoreCatalog store,
                                   ContentValidationResult result, bool verbose)
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

            ValidateFreeOpenings(hearts, index, result, verbose);
            ValidateHeartRescue(hearts, store, result, verbose);

            if (!verbose) return;

            Debug.Log($"[Glimmer] hearts: refill to {hearts.RefillCap} every " +
                      $"{hearts.RefillSeconds / 3600f:0.##}h ({hearts.BoostedRefillSeconds / 3600f:0.##}h " +
                      $"boosted, up to {hearts.MaxBoostHours}h of boost), hold up to {hearts.Ceiling}, " +
                      $"a loss costs {hearts.DefeatCost}, the first " +
                      $"{hearts.GraceLevels} of each mode cost nothing, and so does any level " +
                      "the player has already finished");
        }

        /// <summary>
        /// Where the free opening actually lands, chapter by chapter.
        ///
        /// <para>
        /// The published number alone does not say what it does: it is counted inside the first
        /// chapter of each mode and stops at that chapter's end (<see cref="HeartStake"/>), so
        /// the same three means three of ten on a full chapter and all of a three-glade one. A
        /// window that swallows a whole chapter is a legitimate decision and a large one — the
        /// heart gate simply does not exist on that map — so it is named here rather than
        /// discovered from a retention chart.
        /// </para>
        /// <para>
        /// Asked of <c>HeartStake</c> rather than worked out here, through the overload that
        /// takes a table: what is being checked is the candidate this build would publish
        /// rather than the rules currently running, and a rule about charging players must not
        /// exist twice — least of all in the file whose job is to prove that it does not.
        /// </para>
        /// </summary>
        static void ValidateFreeOpenings(HeartRuleTable hearts, CatalogIndex index,
                                         ContentValidationResult result, bool verbose)
        {
            if (index == null || hearts.GraceLevels <= 0) return;

            for (int i = 0; i < index.Modes.Count; i++)
            {
                var chapter = index.FirstChapterIn(index.Modes[i]);
                if (chapter == null || chapter.IsEmpty) continue;

                int free = HeartStake.FreeLevelsIn(index, chapter.Id, hearts);
                if (free <= 0) continue;

                // Only worth saying of a chapter with something to cover. A mode still finding
                // its shape ships one board, and "the window is longer than this chapter" is
                // then a fact about how much content exists rather than about the tuning — a
                // warning on every build for something that fixes itself when the second board
                // lands is a warning nobody reads.
                if (free >= chapter.LevelCount && chapter.LevelCount > 1)
                    result.Warnings.Add($"the free opening covers all {chapter.LevelCount} level(s) of " +
                                        $"'{chapter.Id}', so the heart gate does not exist anywhere on " +
                                        $"the {index.Modes[i]} map");
                else if (verbose)
                    Debug.Log($"[Glimmer] hearts: the first {free} of '{chapter.Id}' cost nothing");
            }
        }

        /// <summary>
        /// The hint pool, checked for the two things the reader cannot know.
        ///
        /// <para>
        /// Warnings rather than errors, exactly as the heart gate is: an aggressive hint
        /// tuning is a legitimate weekend decision, and neither of these is a mistake that
        /// costs anybody money.
        /// </para>
        /// </summary>
        static void ValidateHints(HintRuleTable hints, AdRewardTable ads,
                                  ContentValidationResult result, bool verbose)
        {
            if (hints == null) { result.Errors.Add("progression.json produced no hint table"); return; }

            // A pool that refills in minutes is not scarce, and a hint that is not scarce is
            // the per-glade allowance this feature replaced: three at every board, costing
            // nothing and meaning nothing.
            long toFull = hints.RefillSeconds * hints.RefillCap;
            if (toFull < 3600)
                result.Warnings.Add($"hints refill a full pool in {toFull / 60} minutes " +
                                    $"({hints.RefillCap} × {hints.RefillSeconds}s); at that rate a hint " +
                                    "costs nothing and the pool is decoration");

            // A payout larger than the whole pool can never land in full, whatever the player
            // is holding — so it is a mistake rather than a tuning, and it is worth naming
            // even though the grant clamps it safely.
            var offer = ads?.Offer(AdPlacement.HintRefill) ?? default;
            if (offer.IsValid && offer.Amount > hints.Ceiling)
                result.Warnings.Add($"'{AdPlacement.HintRefill}' pays {offer.Amount} hint(s) into a " +
                                    $"pool that holds {hints.Ceiling}; the surplus is refused, not banked");

            // Note what is deliberately *not* warned about: a ceiling equal to the cap. That is
            // the shipped shape, so a warning would fire on every run for ever — and a warning
            // that always fires is one nobody reads. It is logged as a fact below instead, and
            // the thing that has to be true because of it, that no offer is made at a full
            // pool, is held by RewardedAds.WouldBenefit and pinned by HintsTests.

            if (!verbose) return;

            Debug.Log($"[Glimmer] hints: refill to {hints.RefillCap} every " +
                      $"{hints.RefillSeconds / 3600f:0.##}h, hold up to {hints.Ceiling} " +
                      $"({toFull / 3600f:0.##}h from empty to full)" +
                      (hints.Ceiling <= hints.RefillCap
                           ? "; the ceiling is the cap, so a granted hint at a full pool is "
                             + "refused rather than banked"
                           : string.Empty));
        }

        /// <summary>
        /// The action bar's catalog: what the reader survived, plus the two things it cannot see.
        ///
        /// <para>
        /// <b>An icon is not content.</b> Which utilities exist, what they cost and how strong
        /// they are are all authored and retunable from a config push; a picture is in the build.
        /// So an entry this build has no sprite for would draw a white rectangle on the bar
        /// (invariant 7b) and is an <em>error</em> — adding a utility is a build, exactly as
        /// adding a mode is (invariant 20). The loc keys are checked in
        /// <see cref="ValidateLocalisation"/>, where every other derived key is.
        /// </para>
        /// <para>
        /// <b>And a chest may only name a utility that exists.</b> This is the one cross-block
        /// check here that is otherwise invisible: a band paying <c>utility:firepop</c> rolls,
        /// publishes, seeds and grants nothing at all — the client's reader skips the id, the
        /// server never granted utilities in the first place, and the only symptom is a chest
        /// that quietly pays less than its odds say.
        /// </para>
        /// </summary>
        static void ValidateUtilities(UtilityCatalog utilities, DailyChestTable daily,
                                      ContentValidationResult result, bool verbose)
        {
            if (utilities == null)
            {
                result.Errors.Add("progression.json produced no utility catalog");
                return;
            }

            var known = new HashSet<string>(StringComparer.Ordinal);
            var declared = new HashSet<string>(StringComparer.Ordinal);

            foreach (var request in AssetManifest.GlobalAssets()) declared.Add(request.Address);

            foreach (var item in utilities.Items)
            {
                known.Add(item.Id);

                if (!declared.Contains(AssetManifest.ArtRoot + item.Art))
                    result.Errors.Add($"utility '{item.Id}' draws '{item.Art}', which " +
                                      "AssetManifest does not name; a picture is not content, so " +
                                      "adding a utility is a build");

                // **A warning rather than an error, and about the run rather than the file.**
                // `UtilityCatalog.Resolve` has already refused anything outside the supported
                // range, so what is left to say is a judgement: a cooldown longer than the quiet
                // between two waves is an item that can be used about once a raid, which is a
                // real authoring choice for a stormcall and almost never one for a mending.
                if (item.CooldownSeconds > SiegeTuning.BetweenWaves * 2f)
                    result.Warnings.Add($"utility '{item.Id}' cools for {item.CooldownSeconds}s, " +
                                        "which is more than two waves apart - it can be used " +
                                        "about once a raid, so check that is what was meant");
            }

            if (daily != null)
            {
                for (int i = 0; i < daily.ChestCount; i++)
                {
                    var chest = daily.Chest(i);

                    foreach (var band in chest.Guaranteed) CheckUtilityBand(band, i, known, result);
                    foreach (var option in chest.Options) CheckUtilityBand(option.Band, i, known, result);
                }
            }

            if (!verbose) return;

            foreach (var item in utilities.Items)
                Debug.Log($"[Glimmer] utility '{item.Id}': {UtilityKinds.Id(item.Kind)} " +
                          $"{item.Magnitude} {UtilityUnits.Of(item.Kind)}, " +
                          $"hold up to {item.MaxHeld}, " +
                          (item.ForSale ? $"{item.GemPrice} gem(s)" : "chests only") +
                          (item.Cools ? $", cools {item.CooldownSeconds}s" : ", no cooldown") +
                          (UtilityUnits.Climbs(item.Kind)
                               ? " - climbs with the chapter it is used on"
                               : string.Empty));
        }

        /// <summary>
        /// The turret roster: its pictures, its keys, its ladder and its prices.
        ///
        /// <para>
        /// <b>The art check is the one only this gate can make.</b> A turret's addresses are
        /// <em>built</em> from its id (<c>WardModel.ArtFor</c>) — twenty models times four colours
        /// is eighty names nobody would keep in step with a roster that is content — so
        /// <c>Tools/verify/artnames.py</c>, which reads literals off a call site, cannot see any of
        /// them. What replaces the literal is this: the roster is walked and every address it
        /// implies is held to what is addressable, which catches a missing picture and a
        /// misspelled id at once where a literal only ever catches the second. A missing one draws
        /// a white rectangle two cells tall on the object a player looks at for a whole run
        /// (invariant 7b).
        /// </para>
        /// <para>
        /// <b>Errors rather than warnings</b>, because every one of these is a turret that cannot
        /// be drawn, cannot be named, or cannot be bought — and all three look like a perfectly
        /// authored file.
        /// </para>
        /// </summary>
        /// <summary>
        /// The rank ladder. The offline mirror is <c>check_ranks</c> in <c>content.py</c>, and
        /// this is the half that can read the asset database and the catalog index.
        ///
        /// <para>
        /// <b>Every rung's badge is on disk.</b> The address is built from the id
        /// (<c>RankDefinition.Icon</c>), so <c>artnames.py</c> cannot see one of them and a rung
        /// renamed without its picture moving is a white rectangle on the map (invariant 7b).
        /// Whether anything <em>loads</em> it is the Addressables audit's question, not this
        /// one — <c>AssetManifest.GlobalAssets</c> derives the list from the live ladder, so
        /// there is no list here that could fall out of step.
        /// </para>
        /// <para>
        /// <b>Every scope names something this catalog ships.</b> A chapter disabled after the
        /// ladder was authored is the sharp case: the file parses, the rung draws, and nobody
        /// can ever meet it.
        /// </para>
        /// <para>
        /// <b>And every derived string resolves.</b> A rung's name, its blurb and each line's
        /// sentence are built from ids, so <c>loc.py</c> can see none of them (invariant 5a).
        /// <b>The table is read here rather than asked of <c>Loc</c></b>, and that is not a
        /// nicety: the runtime localisation table is never loaded in the Editor, so
        /// <c>Loc.Has</c> answers false for every key in the game and a validator built on it
        /// reports the whole file missing. It was, on the first run.
        /// </para>
        /// </summary>
        static void ValidateRanks(Ranks.RankLadder ranks, CatalogIndex index,
                                  ContentValidationResult result, bool verbose)
        {
            if (ranks == null) { result.Errors.Add("progression.json produced no rank ladder"); return; }

            if (ranks.IsEmpty)
            {
                // Legal and complete: a game with no ranks is a game (`RankLadder`). Said out
                // loud because the alternative reading — "the block failed to parse" — is
                // reported as an error by the reader itself, so silence here would be ambiguous.
                if (verbose) Debug.Log("[Glimmer] no rank ladder authored; no badges are drawn");
                return;
            }

            var onDisk = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/Game/Art/", StringComparison.Ordinal)) continue;
                string address = "Art/" + path.Substring("Assets/Game/Art/".Length);
                int dot = address.LastIndexOf('.');
                if (dot > 0) address = address.Substring(0, dot);
                onDisk.Add(address);
            }

            // Null only when the file itself is missing, which `ValidateLocalisation` reports on
            // its own — one missing file must not also read as every string being absent.
            var strings = LocalisationTable();

            foreach (var rung in ranks.Rungs)
            {
                if (!onDisk.Contains(AssetManifest.ArtRoot + rung.Icon))
                    result.Errors.Add($"rank '{rung.Id}' needs '{rung.Icon}' on disk; run " +
                                      "Tools/make_rank_art.py");

                if (strings != null)
                {
                    Require(strings, rung.NameKey, $"rank '{rung.Id}'", result);
                    Require(strings, rung.BlurbKey, $"rank '{rung.Id}'", result);
                }

                foreach (var line in rung.Requirements)
                {
                    if (strings != null)
                        Require(strings, Ranks.RankMeasures.SentenceKey(line.Measure, line.IsScoped),
                                $"rank '{rung.Id}' line '{line.Measure.Id}'", result);

                    if (!line.IsScoped || index == null) continue;

                    // Asked of the catalog rather than of the scope's *name*, which is a
                    // different fact: a chapter that ships with no string is a localisation bug
                    // and is reported as one, while a chapter that does not ship at all is a
                    // rung nobody can ever meet.
                    bool known =
                        line.Measure.Scope == Ranks.RankScopeKind.Chapter
                            ? ChapterId.TryParse(line.Scope, out var cid, out _)
                              && index.ContainsChapter(cid)
                        : line.Measure.Scope == Ranks.RankScopeKind.Level
                            && LevelId.TryParse(line.Scope, out var lid, out _)
                            && index.Contains(lid);

                    if (!known)
                        result.Errors.Add($"rank '{rung.Id}' asks about '{line.Scope}', which this " +
                                          "catalog does not ship or has disabled; the rung would " +
                                          "be unmeetable with every other gate green");
                }
            }

            ValidateRankGate(ranks, index, result, verbose);

            if (!verbose) return;

            Debug.Log($"[Glimmer] rank ladder verified: {ranks.Count} rung(s), " +
                      $"{ranks.Rungs[0].Id} to {ranks.Rungs[ranks.Count - 1].Id}, all derived");
        }

        /// <summary>
        /// The ladder may not open before the lane it ranks does (invariant 52i) — the rule
        /// itself is <see cref="Ranks.RankGate"/>, and this is the build gate asking it.
        ///
        /// <para>
        /// <b>The rule lives in <c>GlimmerGrove.Authoring</c> and not here, and the reason is
        /// this file.</b> Its first cut was written in place, where the suite cannot reach it,
        /// and it shipped having never once executed — a validator with no failing case is not a
        /// check. `RankGateTests` drives every branch of it now, offline.
        /// </para>
        /// <para>
        /// Reported in full when verbose even when nothing is wrong, for
        /// <see cref="ValidateKeeperWalls"/>' reason: this one number decides whether a whole
        /// readout is on the screen, and nobody should have to open two JSON files to find out
        /// where it stands.
        /// </para>
        /// </summary>
        static void ValidateRankGate(Ranks.RankLadder ranks, CatalogIndex index,
                                     ContentValidationResult result, bool verbose)
        {
            Ranks.RankGate.Check(ranks, index, result.Errors, result.Warnings);

            if (!verbose) return;

            int wall = Ranks.RankGate.WallOf(index);

            Debug.Log(wall <= 0
                          ? "[Glimmer] rank gate: this catalog ships no Infinite lane, so the " +
                            "ladder is anchored to nothing"
                          : $"[Glimmer] rank gate: the ladder opens at keeper level " +
                            $"{Ranks.RankGate.OpensAt(ranks)}, against the {wall} the Infinite " +
                            "lane opens at");
        }

        /// <summary>
        /// The fallback language's strings, or null when the file is missing.
        ///
        /// <b>Read rather than asked of <see cref="Loc"/>.</b> Nothing loads the runtime table in
        /// the Editor, so <c>Loc.Has</c> is false for every key in the game — a validator built
        /// on it does not under-report, it reports everything as missing, which is how this was
        /// found. <see cref="ValidateLocalisation"/> parses its own copy for the same reason.
        /// </summary>
        /// <summary>
        /// The daily challenge slate: read through the same reader a device uses, with every
        /// refusal an error, and each row's two derived strings held to the table.
        ///
        /// <b>Whether a board is winnable is the fixture's question</b> (<c>ChallengeTests</c>
        /// plays every row); this is the file's shape and its strings, which is what a content
        /// push can break without a build.
        /// </summary>
        static void ValidateChallenges(ContentValidationResult result, bool verbose)
        {
            var source = new BundledContentSource();
            var fetch = source.FetchAsync(ContentPaths.Challenges, default).GetAwaiter().GetResult();

            if (!fetch.Success)
            {
                result.Errors.Add($"missing {ContentPaths.Challenges}");
                return;
            }

            var problems = new List<string>();
            Challenges.ChallengeTable.TryRead(fetch.Text, out var table, problems);
            foreach (var problem in problems) result.Errors.Add("challenges: " + problem);

            var strings = LocalisationTable();

            foreach (var row in table.All)
            {
                if (strings != null)
                {
                    Require(strings, row.NameKey, $"challenge '{row.Id}'", result);
                    Require(strings, row.BlurbKey, $"challenge '{row.Id}'", result);
                }

                if (verbose)
                    Debug.Log($"[Glimmer] challenge '{row.Id}' ({row.Genre}) {row.Width}x{row.Height}, " +
                              $"hill {row.Hill}, {row.RaiderCount} raider(s) of {row.HillHealth} health");
            }

            // A genre's card and a deal's row each name themselves from a permanent id
            // (invariant 56i), so the strings are held to the table here as a row's are.
            if (strings != null)
            {
                foreach (var genre in table.Genres)
                {
                    string spelling = Challenges.ChallengeGenres.NameOf(genre);
                    Require(strings, "challenge.genre." + spelling + ".name", $"genre '{spelling}'", result);
                    Require(strings, "challenge.genre." + spelling + ".blurb", $"genre '{spelling}'", result);
                }

                foreach (var tier in table.Tiers)
                    Require(strings, tier.NameKey, $"deal '{tier.Id}'", result);
            }

            int genres = table.Genres.Count;
            Debug.Log($"[Glimmer] challenges: {table.FreePlays} free play(s) of each of {genres} genre(s) a day; " +
                      $"a clear pays {table.Rewards.Coins} credits and {table.Rewards.Xp} XP up to " +
                      $"{table.Rewards.MaxClears:N0} clears ({table.Rewards.MaxXp:N0} XP); " +
                      $"{table.Tiers.Count} deal(s)");
            foreach (var tier in table.Tiers)
                Debug.Log($"[Glimmer]   deal '{tier.Id}': {tier.Gems} gems for {tier.Days} day(s) of {tier.Plays} plays a genre, " +
                          $"at most {tier.Plays * genres * table.Rewards.Coins:N0} credits a day");

            if (table.IsEmpty) result.Warnings.Add("challenges.json offers no challenge; the hub's door is shut");
        }

        static LocTable LocalisationTable()
        {
            var source = new BundledContentSource();
            var fetch = source.FetchAsync(ContentPaths.Localisation(Loc.FallbackLanguage), default)
                              .GetAwaiter().GetResult();

            return fetch.Success ? LocTable.Parse(fetch.Text, out _) : null;
        }

        static void ValidateWards(WardCatalog wards, ContentValidationResult result, bool verbose)
        {
            if (wards == null)
            {
                result.Errors.Add("progression.json produced no turret roster");
                return;
            }

            bool starter = false;
            var orders = new List<int>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var model in wards.Models)
            {
                if (!seen.Add(model.Id))
                    result.Errors.Add($"wards names '{model.Id}' twice; an id is permanent and " +
                                      "two entries under one would be two turrets in one save row");

                starter |= model.IsStarter;
                orders.Add(model.Order);

                if (model.ForGems && model.ForCoins)
                    result.Errors.Add($"turret '{model.Id}' is priced in both gems and credits; " +
                                      "it carries one price or the other");

                if (model.MinLevel <= 0 && !model.IsStarter)
                    result.Errors.Add($"turret '{model.Id}' is priced but asks for no keeper " +
                                      "level; every turret on the shelf is behind one, so nought " +
                                      "is no longer how an entry says it is ungated");

                if (!WardHolding.Spellable(model.Id))
                    result.Errors.Add($"turret '{model.Id}' contains '{WardHolding.Mark}' or " +
                                      $"'{WardHolding.CopyMark}', which separate a turret from " +
                                      "the colour it was bought for and from which copy of it a " +
                                      "row is, in wardsOwned");

                if (!Addressed(AssetManifest.WardThumb(model.Id)))
                    result.Errors.Add($"turret '{model.Id}' has no shelf thumbnail at " +
                                      $"'{AssetManifest.WardThumb(model.Id)}'");

                // **A legendary is cut once rather than once per colour** (`WardModel.ArtFor`),
                // so asking about all four would report one missing picture as four missing
                // pictures - which is a build gate telling somebody to look in four places for
                // one file.
                int seats = model.Colourless ? 1 : WardLine.Colours.Length;

                for (int i = 0; i < seats; i++)
                {
                    char colour = WardLine.Colours[i];

                    string body = AssetManifest.SiegeArt(model.ArtFor(colour));
                    string fire = AssetManifest.SiegeArt(model.FireFor(colour));

                    if (!Addressed(body))
                        result.Errors.Add($"turret '{model.Id}' has no art at '{body}'; a picture " +
                                          "is not content, so adding a turret is a build");

                    if (!Addressed(fire))
                        result.Errors.Add($"turret '{model.Id}' has no recoil reel at '{fire}'");

                    // **What it throws**, which a turret with an ability owns and one without
                    // borrows from the colour. Errors rather than warns for the reason the body
                    // does: these names are *built* from the id, so `Tools/verify/artnames.py`
                    // cannot see them (invariant 42), and what a missing reel costs is a white
                    // rectangle crossing the hill four or five times a second (invariant 7b).
                    if (!model.OwnShot) continue;

                    foreach (var reel in new[]
                             {
                                 AssetManifest.SiegeFx(model.ShotFor(colour)),
                                 AssetManifest.SiegeFx(model.MuzzleFor(colour)),
                                 AssetManifest.SiegeFx(model.HitFor(colour)),
                             })
                        if (!Addressed(reel))
                            result.Errors.Add(
                                $"turret '{model.Id}' has no projectile reel at '{reel}'; run " +
                                (model.Legendary ? "Tools/make_legend_fx.py --write"
                                                 : "Art ▸ Bake Turret Projectiles"));
                }

            }

            // **The four flames, once for the whole roster rather than once per ember rung.**
            // They are addressed per ward *colour* (`WardModel.BurnFor`) because a burn says which
            // seat is paying for it, so three ember models share four reels - and asking inside the
            // loop above would report one missing reel three times and send somebody looking for
            // three files. Asked whenever the roster holds an ember at all, which is what decides
            // whether anything can ever be set alight.
            //
            // An error rather than a warning, for the reason every other reel here is: the name is
            // built, so `Tools/verify/artnames.py` cannot see it, and what a missing one costs is a
            // white rectangle a body and a half tall walking down the hill (invariant 7b).
            foreach (var model in wards.Models)
            {
                if (model.Ability != WardAbility.Ember) continue;

                foreach (char colour in WardLine.Colours)
                {
                    string blaze = AssetManifest.SiegeFx(WardModel.BurnFor(colour));

                    if (!Addressed(blaze))
                        result.Errors.Add($"the roster holds an ember turret ('{model.Id}') and " +
                                          $"there is no flame at '{blaze}'; run " +
                                          "python Tools/make_burn_fx.py --write");
                }

                break;
            }

            orders.Sort();
            for (int i = 0; i < orders.Count; i++)
                if (orders[i] != i + 1)
                {
                    result.Errors.Add("wards orders are not 1..N with no gaps and no ties; the " +
                                      "shelf would reshuffle itself under a player on a retune");
                    break;
                }

            // **The shelf read as one ladder**, which is the roster's own rule rather than a
            // second opinion about it: no wall may fall as the shelf climbs, and every wall has
            // to stand inside the band whose header a player reads it under. Asked of the catalog
            // rather than re-derived, so this gate and the reader cannot disagree.
            string climb = wards.LadderProblem();
            if (climb != null) result.Errors.Add(climb);

            if (!starter)
                result.Errors.Add("wards lists no free turret; a player who has bought nothing " +
                                  "would stand an empty line, and a siege with no line cannot be " +
                                  "played");

            // **The upgrade ladder, asked of the table in force rather than of the file**, so a
            // block the reader refused and a block it accepted are both judged by what a player
            // would actually be charged. Every rung has to climb: one that costs no more than the
            // one below it is a rung nobody chooses between (invariant 5d, on a price).
            foreach (var model in wards.Models)
            {
                if (model.IsStarter) continue;

                int under = 0;

                for (int stars = WardStars.Least; stars < WardStars.Most; stars++)
                {
                    int price = WardStars.PriceOf(model, stars);

                    if (price <= 0)
                        result.Errors.Add($"turret '{model.Id}' prices star {stars + 1} at " +
                                          "nought, which is how the ladder says there is no next " +
                                          "star at all");
                    else if (price <= under)
                        result.Errors.Add($"turret '{model.Id}' prices star {stars + 1} at " +
                                          $"{price}, no more than the {under} below it — an " +
                                          "upgrade nobody chooses between");

                    under = price;
                }

                // The top of the ladder is where it stops, and nothing beyond it is for sale.
                if (WardStars.PriceOf(model, WardStars.Most) != 0)
                    result.Errors.Add($"turret '{model.Id}' prices a star past the top of the " +
                                      "ladder");
            }

            // **No two rungs of the shelf are the same turret.** A magnitude nobody reads makes
            // two rungs of one ability identical, and both `rend` and `prism` shipped that way —
            // so a thousand-gem breaker was exactly a four-thousand-credit cleaver, and the
            // dearer one bought nothing (invariant 5d, met on the one thing a player pays for).
            // Priced rungs only: the free turret is not a rung.
            var shapes = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var model in wards.Models)
            {
                if (model.IsStarter) continue;

                // **Every field that plays**, so two rungs that differ only in what their bolt
                // weighs or what they can take are two turrets rather than one twice.
                string shape = WardAbilities.NameOf(model.Ability)
                             + " " + model.Magnitude + "/" + model.Extent
                             + " " + model.PowerTenths + "/" + model.GuardTenths;

                if (shapes.TryGetValue(shape, out string first))
                    result.Errors.Add($"turrets '{first}' and '{model.Id}' are the same turret — " +
                                      $"{shape} — so whichever is dearer buys nothing at all");
                else
                    shapes[shape] = model.Id;
            }

            // **A family's dearer rung is never worse at both jobs.** Trading weight for
            // toughness is the whole of what makes the roster a choice, and it is a choice
            // *between* abilities: within one, the rung that costs more must be better at
            // something and no worse at the rest, or the shelf is asking a player to pay to be
            // downgraded. Asked in shelf order, so it reads the way a player does.
            var below = new Dictionary<WardAbility, WardModel>();

            foreach (var model in wards.Models)
            {
                if (model.IsStarter) continue;

                if (below.TryGetValue(model.Ability, out var under)
                    && model.PowerTenths < under.PowerTenths
                    && model.GuardTenths < under.GuardTenths)
                    result.Errors.Add(
                        $"turret '{model.Id}' sits below '{under.Id}' on the shelf and is worse " +
                        "at both jobs — a dearer rung may trade weight for toughness and may not " +
                        "give up both");

                below[model.Ability] = model;
            }

            if (!verbose) return;

            Debug.Log($"[Glimmer] turrets: {wards.Count} on the shelf, four colours each - a run " +
                      "loads the four a player stood on the line");
        }

        /// <summary>
        /// Whether an address resolves to something in this project.
        ///
        /// <b>The file rather than the Addressables entry</b>, deliberately: registration is an
        /// importer hook (invariant 7a) and can lag a freshly written folder by a domain reload,
        /// where the picture either exists or does not. <c>AddressableAudit</c> is what proves the
        /// registration, and it is a separate gate for exactly that reason.
        /// </summary>
        static bool Addressed(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            string root = "Assets/Game/" + address;

            // A reel is a folder of numbered frames and answers under the folder's own name, which
            // is how `AssetLibrary.Frames` reads one.
            return File.Exists(root + ".png") || Directory.Exists(root);
        }

        static void CheckUtilityBand(ChestBand band, int chest, HashSet<string> known,
                                     ContentValidationResult result)
            => CheckUtilityBand(band, $"daily chest {chest}", known, result);

        static void CheckUtilityBand(ChestBand band, string where, HashSet<string> known,
                                     ContentValidationResult result)
        {
            if (band.Kind != ChestDropKind.Utility) return;

            if (string.IsNullOrEmpty(band.Item))
                result.Errors.Add($"{where} pays a utility and names none");
            else if (!known.Contains(band.Item))
                result.Errors.Add($"{where} pays utility '{band.Item}', which the " +
                                  "utilities block does not define; it would grant nothing");
        }

        /// <summary>
        /// The task slates and their chest ladder.
        ///
        /// <para>
        /// <b>The art check is the one only this gate can make</b>, for the turrets' reason: a
        /// tier's closed icon and opening reel are <em>built</em> from its id, so
        /// <c>artnames.py</c> reads neither, and a missing one is a white rectangle on the hub
        /// or over the one ceremony in the game that is entirely a picture (invariant 7b).
        /// The copy is derived the same way (<c>task.{id}.name</c>, <c>chest.{id}.name</c>),
        /// which is invariant 5a's situation and the reason it is checked here or nowhere.
        /// </para>
        /// <para>
        /// The reader has already refused a tier nobody defined and a duplicated id. What is
        /// left is the class of mistake that produces a perfectly valid slate nobody wanted: a
        /// ladder that does not rise, a slate too short to deal, a target of one with a plural
        /// sentence, and a boost longer than the ceiling.
        /// </para>
        /// </summary>
        static void ValidateTasks(Tasks.TaskTable tasks, UtilityCatalog utilities, HeartRuleTable hearts,
                                  ContentValidationResult result, bool verbose)
        {
            if (tasks == null) { result.Errors.Add("progression.json produced no task table"); return; }

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in AssetManifest.GlobalAssets()) declared.Add(request.Address);
            foreach (var request in AssetManifest.ChestAssets(tasks)) declared.Add(request.Address);

            var known = new HashSet<string>(StringComparer.Ordinal);
            if (utilities != null) foreach (var item in utilities.Items) known.Add(item.Id);

            var addressable = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/Game/Art/", StringComparison.Ordinal)) continue;
                string address = "Art/" + path.Substring("Assets/Game/Art/".Length);
                int dot = address.LastIndexOf('.');
                if (dot > 0) address = address.Substring(0, dot);
                addressable.Add(address);
                int slash = address.LastIndexOf('/');
                if (slash > 0) addressable.Add(address.Substring(0, slash));
            }

            long previousFloor = 0;

            foreach (var tier in tasks.Tiers)
            {
                string icon = AssetManifest.ArtRoot + tier.Icon;
                string reel = AssetManifest.ArtRoot + tier.Reel;

                if (!declared.Contains(icon))
                    result.Errors.Add($"chest tier '{tier.Id}' draws '{tier.Icon}', which " +
                                      "AssetManifest does not name; the hub would draw a white rectangle");
                if (!addressable.Contains(icon) || !addressable.Contains(reel))
                    result.Errors.Add($"chest tier '{tier.Id}' needs '{tier.Icon}' and '{tier.Reel}/' " +
                                      "on disk; run Tools/make_chest_art.py");

                long floor = 0;
                foreach (var band in tier.Chest.Guaranteed)
                {
                    floor += band.Min;
                    CheckUtilityBand(band, $"chest tier '{tier.Id}'", known, result);
                    if (band.Kind == ChestDropKind.HeartBoost && band.Max > hearts.MaxBoostHours)
                        result.Errors.Add($"chest tier '{tier.Id}' guarantees a {band.Max}h heart boost, " +
                                          $"more than the {hearts.MaxBoostHours}h ceiling");
                }

                foreach (var option in tier.Chest.Options)
                {
                    CheckUtilityBand(option.Band, $"chest tier '{tier.Id}'", known, result);
                    if (option.Band.Kind == ChestDropKind.HeartBoost && option.Band.Max > hearts.MaxBoostHours)
                        result.Errors.Add($"chest tier '{tier.Id}' can drop a {option.Band.Max}h heart boost, " +
                                          $"more than the {hearts.MaxBoostHours}h ceiling");
                }

                // A dearer chest that pays less reads as the game punishing the player for the
                // harder task, and nothing else in the build catches it.
                if (floor <= previousFloor)
                    result.Errors.Add($"chest tier '{tier.Id}' guarantees {floor}, not more than the " +
                                      $"tier below it at {previousFloor}; a dearer chest must pay more");
                previousFloor = floor;
            }

            foreach (var period in Tasks.TaskPeriods.All)
            {
                int live = 0;
                foreach (var task in tasks.Slate(period))
                {
                    if (!task.Retired) live++;

                    if (string.IsNullOrEmpty(Tasks.TaskGoals.Icon(task.Goal)))
                        result.Errors.Add($"task '{task.Id}' names goal '{task.Goal}', which has no picture");
                    else if (!declared.Contains(AssetManifest.ArtRoot + Tasks.TaskGoals.Icon(task.Goal)))
                        result.Errors.Add($"task '{task.Id}' draws '{Tasks.TaskGoals.Icon(task.Goal)}', " +
                                          "which AssetManifest does not name");
                }

                string slate = Tasks.TaskPeriods.Id(period);
                if (live < tasks.ActivePerPeriod)
                    result.Errors.Add($"the {slate} slate has {live} live task(s) and deals " +
                                      $"{tasks.ActivePerPeriod}; the page would show fewer rows than promised");
                else if (live < tasks.ActivePerPeriod * 2)
                    result.Warnings.Add($"the {slate} slate has only {live} live task(s) for " +
                                        $"{tasks.ActivePerPeriod} a period, so consecutive periods repeat tasks");
            }

            if (!verbose) return;

            foreach (var tier in tasks.Tiers)
            {
                var line = new System.Text.StringBuilder()
                    .Append("[Glimmer] chest tier ").Append(tier.Rank).Append(" '").Append(tier.Id)
                    .Append("' always pays");
                foreach (var band in tier.Chest.Guaranteed)
                    line.Append(' ').Append(band.Min).Append('-').Append(band.Max)
                        .Append(' ').Append(ChestDropKinds.Id(band.Kind))
                        .Append(band.Item.Length > 0 ? ":" + band.Item : string.Empty);
                if (tier.Chest.Options.Count > 0)
                {
                    line.Append("  ·  bonus:");
                    for (int o = 0; o < tier.Chest.Options.Count; o++)
                    {
                        var option = tier.Chest.Options[o];
                        line.Append("  ").Append(ChestDropKinds.Id(option.Band.Kind))
                            .Append(option.Band.Item.Length > 0 ? ":" + option.Band.Item : string.Empty)
                            .Append(' ').Append(option.Band.Min).Append('-').Append(option.Band.Max)
                            .Append(" at ").Append(tier.Chest.ChanceOf(o).ToString("0.#")).Append('%');
                    }
                }
                Debug.Log(line.ToString());
            }

            foreach (var period in Tasks.TaskPeriods.All)
            {
                var line = new System.Text.StringBuilder("[Glimmer] ")
                    .Append(Tasks.TaskPeriods.Id(period)).Append(" slate:");
                foreach (var task in tasks.Slate(period))
                    line.Append("  ").Append(task.Id).Append(task.Retired ? " (retired)" : string.Empty)
                        .Append(" -> ").Append(task.Tier.Id);
                Debug.Log(line.ToString());
            }

            Debug.Log($"[Glimmer] the tasks deal {tasks.ActivePerPeriod} a period and are paid by " +
                      "the server from config/progression — run firebase/seed/seed-config.mjs " +
                      "after this change or every task claim is left unconfirmed.");
        }

        /// <summary>
        /// The account-prompt pacing, which the reader can bound but cannot judge.
        ///
        /// <para>
        /// <c>AccountPromptRuleTable.Resolve</c> already clamps anything outside the band a
        /// published file may ask for, and says so. What is left is the combination that is
        /// perfectly legal and probably not what somebody meant: switching off the ask that
        /// protects money. Zero is deliberately a value an author can write — it is the lever
        /// that turns the panel off in minutes if it costs more conversion than it protects —
        /// so this is a warning, never an error.
        /// </para>
        /// <para>
        /// Reported in full even when there is nothing wrong, because these three numbers
        /// decide how often the game interrupts a player and nobody should have to open a
        /// JSON file to find out what they are. <c>ValidateHearts</c>' rule.
        /// </para>
        /// </summary>
        static void ValidatePrompts(AccountPromptRuleTable prompts,
                                    ContentValidationResult result, bool verbose)
        {
            if (prompts == null) { result.Errors.Add("progression.json produced no prompt table"); return; }

            if (prompts.PurchaseBudget == 0)
                result.Warnings.Add("prompts purchaseBudget is 0, so a guest who spends real money " +
                                    "is never asked to protect it - and a purchase made on an " +
                                    "anonymous account cannot be restored by any route");

            if (prompts.ChapterBudget == 0 && prompts.PurchaseBudget == 0)
                result.Warnings.Add("both prompt budgets are 0, so the account panel never opens " +
                                    "by itself; the shop's standing notice is the whole warning");

            if (!verbose) return;

            Debug.Log($"[Glimmer] account prompt: {prompts.ChapterBudget} ask(s) after a chapter, " +
                      $"{prompts.PurchaseBudget} after a purchase, " +
                      $"{prompts.QuietSeconds / 3600L}h apart whatever raised them");
        }

        /// <summary>
        /// The chapter gate, checked for the things the reader cannot know.
        ///
        /// <para>
        /// The reader rejects a gate no level could ever meet. This checks the gate against the
        /// catalog it will actually be applied to, which is the part that cannot be known from
        /// the file alone: a chapter that is <em>reachable</em> in principle is not the same as
        /// one somebody will reach, and the two ways a gate goes wrong are both invisible in
        /// <c>progression.json</c>. A gate of three asks for perfect play on every level of
        /// every chapter, which is a wall wearing a gate's clothes; and a chapter that holds
        /// fewer levels than the one before it quietly asks for less, which is worth seeing
        /// before a drop rather than after.
        /// </para>
        /// <para>
        /// Reported in full even when nothing is wrong, for <c>ValidatePrompts</c>' reason: this
        /// one number decides how much of a drop a player has to master before the next one
        /// opens, and nobody should have to open a JSON file to find out what it is.
        /// </para>
        /// </summary>
        static void ValidateChapterGate(ChapterGateTable gate, CatalogIndex index,
                                        ContentValidationResult result, bool verbose)
        {
            if (gate == null) { result.Errors.Add("progression.json produced no chapter gate"); return; }

            if (gate.IsOpenToAll)
                result.Warnings.Add("chapterGate starsPerLevel is 0, so every chapter stands open " +
                                    "from a new player's first launch; only the level-by-level " +
                                    "chain inside a chapter is left");

            if (gate.StarsPerLevel >= LevelRecord.MaxStars)
                result.Warnings.Add($"chapterGate starsPerLevel is {gate.StarsPerLevel}, which is " +
                                    "every star a level can pay - the next chapter needs a perfect " +
                                    "result on every level of this one, with no room for a single " +
                                    "two-star clear anywhere");

            if (index == null || !verbose) return;

            foreach (var chapter in index.Chapters)
            {
                var next = index.ChapterNeighbour(chapter.Id, +1);
                if (next == null) continue;

                int required = gate.RequiredStars(chapter.LevelCount);
                int available = chapter.LevelCount * LevelRecord.MaxStars;

                Debug.Log($"[Glimmer] chapter gate: '{next.Id}' opens at {required} of the " +
                          $"{available} stars in '{chapter.Id}' " +
                          $"({gate.StarsPerLevel} a level over {chapter.LevelCount} levels)");
            }
        }

        /// <summary>
        /// The price of a way back onto a lost board, checked for the things the reader cannot
        /// know.
        ///
        /// <para>
        /// The reader bounds both numbers and refuses the one value that would break the
        /// feature outright — a free heart. What it cannot judge is whether the price is
        /// <em>reachable</em>, and that is the only way this pair goes quietly wrong:
        /// <c>ValidateContinue</c>'s complaint, one panel further on and sharper, because the
        /// player being shown this one has no hearts at all. A rescue dearer than the cheapest
        /// gem pack sends somebody with an empty bar round the shop twice.
        /// </para>
        /// <para>
        /// The second check has no counterpart on the continue and is the one worth having.
        /// Hearts are also sold by the copy in the shop (<c>hearts_five</c> and friends), and
        /// the two prices are set in different blocks of the same file on different days — so
        /// a rescue dearer per heart than the shelf is a panel quietly charging a premium at
        /// the moment a player is least able to compare, which is the shape a store reviewer
        /// is right to object to.
        /// </para>
        /// <para>
        /// <b>Against the shop's smallest pack rather than its best rate</b>, and the
        /// difference is what stops this being noise. A bulk pack is a volume discount — the
        /// shipped ladder runs 50/5, 125/15 and 280/40, so a two-heart rescue is dearer per
        /// heart than the top of it at every price anybody would ever set, and a check that
        /// fires on every honest tuning is a check people learn to scroll past. The entry pack
        /// is the like-for-like comparison: it is what the same player would otherwise buy for
        /// the same reason. Beating it is not asked for; matching it is.
        /// </para>
        /// <para>
        /// Reported in full even when nothing is wrong, for <c>ValidateChapterGate</c>'s
        /// reason: this is a price charged to real players at the worst moment in a session,
        /// and nobody should have to open a JSON file to find out what it is.
        /// </para>
        /// </summary>
        static void ValidateHeartRescue(HeartRuleTable hearts, StoreCatalog store,
                                        ContentValidationResult result, bool verbose)
        {
            if (hearts.RescueHearts <= 0)
            {
                if (verbose)
                    Debug.Log("[Glimmer] heart rescue: withdrawn - a player out of hearts waits, " +
                              "watches a video, or leaves");
                return;
            }

            long entry = 0L;
            if (store != null)
                foreach (var product in store.Products)
                    if (product != null && product.Gems > 0L && (entry == 0L || product.Gems < entry))
                        entry = product.Gems;

            if (entry > 0L && hearts.RescueGems > entry)
                result.Warnings.Add($"hearts rescueGems is {hearts.RescueGems}, dearer than the " +
                                    $"{entry}-gem entry rung - a player short of it cannot cover " +
                                    "the rescue with the cheapest thing in the shop, and they " +
                                    "have no hearts to go away and play with");

            // The shop's entry heart pack - its smallest - rather than its best rate. See
            // the remarks for why the best one would fire on every honest tuning.
            long packGems = 0L, packHearts = 0L;
            if (store != null)
                foreach (var good in store.Goods)
                {
                    if (good == null || good.Kind != StoreGoodKind.Hearts) continue;
                    if (good.Amount <= 0 || good.Gems <= 0L) continue;

                    if (packHearts == 0L || good.Amount < packHearts)
                    {
                        packGems = good.Gems;
                        packHearts = good.Amount;
                    }
                }

            // Cross-multiplied rather than divided, so the comparison is exact integer
            // arithmetic - the rule this project keeps for anything a player counts towards
            // (see LevelTuning, and the four glades that shipped a turn out).
            if (packHearts > 0L &&
                hearts.RescueGems * packHearts > packGems * hearts.RescueHearts)
                result.Warnings.Add($"the heart rescue asks {hearts.RescueGems} gems for " +
                                    $"{hearts.RescueHearts} hearts, dearer per heart than the " +
                                    $"shop's smallest pack ({packGems} for {packHearts}); the " +
                                    "panel charges a premium at the moment a player is least " +
                                    "able to compare");

            if (!verbose) return;

            Debug.Log($"[Glimmer] heart rescue: {hearts.RescueGems} gem(s) for " +
                      $"+{hearts.RescueHearts} heart(s) on the defeat panel - a fresh attempt " +
                      "graded like any other, never a continue");
        }

        /// <summary>
        /// The price of a second chance, checked for the things the reader cannot know.
        ///
        /// <para>
        /// The reader bounds it and refuses the two values that would break the feature
        /// outright — a free continue, and one that hands over nothing. What it cannot judge is
        /// whether the price is <em>reachable</em>, and that is the only way this block goes
        /// quietly wrong: a continue dearer than the cheapest gem pack means every player short
        /// of gems is sold a purchase that does not cover it, and one dearer than a week of
        /// free play is not an offer anybody without a card can ever take. Neither throws,
        /// neither validates red anywhere else, and both read perfectly plausibly in the JSON.
        /// </para>
        /// <para>
        /// Reported in full even when nothing is wrong, for <c>ValidateChapterGate</c>'s
        /// reason: this is a price charged to real players at the worst moment in a session,
        /// and nobody should have to open a JSON file to find out what it is.
        /// </para>
        /// </summary>
        /// <summary>
        /// What an XP boost multiplies, how long it runs, and how often one may be watched.
        ///
        /// <para>
        /// The second block here that decides XP outside the star ledger and the first that is a
        /// <em>multiplier</em>, so the figures are printed rather than left to be worked out — a
        /// percentage nobody has read against the curve is a keeper ladder climbing at a speed
        /// nobody wrote down. It is the mirror of the <c>xp boost</c> block in
        /// <c>Tools/verify/content.py</c>, and both print the same lines on purpose.
        /// </para>
        /// <para>
        /// <b>The one thing checked here rather than printed is the pair of numbers describing
        /// one window.</b> The advert says what a view pays and the boost block says what the
        /// rule actually opens; they have to agree, because the cooldown is derived by
        /// subtracting the boost block's figure from the stored deadline. <c>ProgressionTable</c>
        /// already refuses the mismatch — this repeats the reading so the gate's own output says
        /// which two numbers it is talking about.
        /// </para>
        /// </summary>
        static void ValidateXpBoost(XpBoostTable boost, AdRewardTable ads,
                                    ContentValidationResult result, bool verbose)
        {
            if (boost == null) { result.Errors.Add("progression.json produced no xpBoost rule"); return; }

            if (!boost.Pays)
            {
                if (verbose)
                    Debug.Log("[Glimmer] xp boost: withdrawn - nothing multiplies XP");
                return;
            }

            var watched = ads != null ? ads.Offer(AdPlacement.XpBoost) : AdOffer.None;

            // A percentage with nothing able to open it is a window that pays on paper only.
            // A warning rather than an error: withdrawing the advert and keeping the bought
            // window is a legitimate thing to want, and the shop still sells one.
            if (boost.OffersWatched && !watched.IsValid)
                result.Warnings.Add("progression.json pays for a watched XP boost, but no advert " +
                                    $"placement '{AdPlacement.XpBoost}' offers one - the " +
                                    "percentage can never be opened by watching");

            if (watched.IsValid && watched.Kind != ChestDropKind.XpBoost)
                result.Errors.Add($"the '{AdPlacement.XpBoost}' placement pays " +
                                  $"'{ChestDropKinds.Id(watched.Kind)}'; it has to pay " +
                                  $"'{ChestDropKinds.XpBoost}' or the advert opens somebody " +
                                  "else's reward");

            if (!verbose) return;

            Debug.Log($"[Glimmer] xp boost: +{boost.WatchedPercent}% for {boost.WatchedHours}h " +
                      $"every {boost.WatchedCooldownHours}h watched, +{boost.BoughtPercent}% for " +
                      $"{boost.BoughtHours}h bought, capped at +{boost.MaxPercent}%");

            Debug.Log("[Glimmer]        the banked bonus is clamped to that share of provable XP, " +
                      "so a forged figure buys a keeper level inside an honest range and no " +
                      "currency at all");
        }

        /// <summary>
        /// What the Infinite lane pays, and where its ceiling lands on the keeper curve.
        ///
        /// <para>
        /// <b>The one block in this file that decides XP without a star behind it</b> (invariant
        /// 9's single exception, see <see cref="EndlessRewardTable"/>), so the figures are
        /// printed rather than left to be worked out: a rate whose ceiling nobody has read
        /// against the curve is a keeper ladder climbing at a speed nobody wrote down. It is the
        /// mirror of the <c>endless xp</c> block in <c>Tools/verify/content.py</c>, and both
        /// print the same three lines on purpose — this is the only number here a <em>server</em>
        /// also derives, and the two content gates are where a retune is read.
        /// </para>
        /// <para>
        /// The reader has already clamped anything out of range and said so (every problem it
        /// raises reaches <see cref="ContentValidationResult"/> through the table build), so what
        /// is left here is the reading a person has to make: how far the ceiling reaches, and
        /// against what.
        /// </para>
        /// </summary>
        static void ValidateEndless(EndlessRewardTable endless, ProgressionTable table,
                                    CatalogIndex index, ContentValidationResult result, bool verbose)
        {
            if (endless == null) { result.Errors.Add("progression.json produced no endless rule"); return; }

            // An Infinite lane that pays nothing is a legitimate authoring decision, but a lane
            // nothing *ships* on is a block tuning a feature that is not there — worth a word,
            // because it is the only way an author would find out.
            bool laneShips = false;
            if (index != null)
                foreach (var chapter in index.Chapters)
                    if (chapter != null && !GameTrack.Main.Equals(index.TrackOf(chapter.Id))) { laneShips = true; break; }

            if (!endless.Pays)
            {
                if (verbose)
                    Debug.Log("[Glimmer] endless xp: withdrawn - the Infinite lane pays no XP; " +
                              "the board, the best wave and the map badge are untouched");
                return;
            }

            if (!laneShips)
                result.Warnings.Add("progression.json pays for endless waves, but no shipped " +
                                    "chapter stands on a track other than the main one - the " +
                                    "block is tuning a lane nothing can reach");

            if (!verbose) return;

            long glade = table != null ? table.DefaultRule.XpFor(3) : 0L;

            Debug.Log($"[Glimmer] endless xp: {endless.XpPerWave} xp a wave, capped at " +
                      $"{endless.MaxWaves:N0} lifetime wave(s) ({endless.MaxXp:N0} xp)");

            if (glade > 0L)
                Debug.Log($"[Glimmer]        {glade / (double)endless.XpPerWave:0.0} wave(s) is " +
                          $"worth one three-starred glade ({glade} xp)");

            if (table != null)
                Debug.Log($"[Glimmer]        the ceiling reaches keeper level " +
                          $"{table.LevelFor(endless.MaxXp).Level}");

            // Said rather than checked: how many waves a run sees off is a fact about play, and
            // no gate can know it. The lane is bought at the gate (`HeartStake.IsPaidAtDoor`), so
            // the heart table paces this and the ceiling only ever bounds a forged save.
            Debug.Log("[Glimmer]        a watch is bought at the gate, so hearts pace this and " +
                      "not the ceiling - the ceiling is only ever a bound on a forged save");
        }

        static void ValidateContinue(ContinueTable carryOn, StoreCatalog store,
                                     ContentValidationResult result, bool verbose)
        {
            if (carryOn == null) { result.Errors.Add("progression.json produced no continue rule"); return; }

            if (!carryOn.Enabled)
            {
                if (verbose)
                    Debug.Log("[Glimmer] continue: withdrawn - a lost run ends, and the only way " +
                              "back in is a heart");
                return;
            }

            // The entry rung, which is the number that decides whether the buy-gems branch of
            // the offer can actually be met in one purchase. A player short of 20 gems who is
            // sold a 100-gem pack is fine; one short of 500 against the same pack is being sent
            // round the shop twice, from a panel raised over a frozen board.
            long entry = 0L;
            if (store != null)
                foreach (var product in store.Products)
                    if (product != null && product.Gems > 0L && (entry == 0L || product.Gems < entry))
                        entry = product.Gems;

            if (entry > 0L && carryOn.Gems > entry)
                result.Warnings.Add($"continueRun gems is {carryOn.Gems}, dearer than the " +
                                    $"{entry}-gem entry rung - a player short of a continue " +
                                    "cannot cover it with the cheapest thing in the shop, and the " +
                                    "offer is raised over a board they cannot leave");

            if (!verbose) return;

            Debug.Log($"[Glimmer] continue: {carryOn.Gems} gem(s) for +{carryOn.Turns} turn(s) " +
                      $"on a glade, +{carryOn.Taps} tap(s) on a thicket; " + Ladder(carryOn));
        }

        /// <summary>
        /// What one run's continues cost in order, and where that ladder stops climbing.
        ///
        /// <para>
        /// Printed rather than asserted, and printed in full rather than as the two numbers
        /// that produce it. A factor in hundredths and a step is a recurrence, and nobody
        /// reads a recurrence off two integers — the thing a retune has to be judged against
        /// is the sequence a player is actually quoted, so that is what the gate says. It is
        /// also the one reading that shows <c>ContinueLimits.MaxGems</c> binding, which is
        /// invariant 37cc's rule: a ceiling that binds is checked rather than discovered.
        /// </para>
        /// </summary>
        static string Ladder(ContinueTable carryOn)
        {
            long first = carryOn.PriceFor(0);
            if (carryOn.PriceFor(1) == first && carryOn.PriceFor(50) == first)
                return $"{first} gems flat, so a run may be continued as often as the player " +
                       "can pay";

            var rungs = new System.Text.StringBuilder();
            long spent = 0L;
            int topsOutAt = 0;

            for (int taken = 0; taken < 8; taken++)
            {
                long price = carryOn.PriceFor(taken);
                spent += price;

                if (taken > 0) rungs.Append(", ");
                rungs.Append(price);

                if (topsOutAt == 0 && price >= ContinueLimits.MaxGems) topsOutAt = taken + 1;
            }

            string climb = carryOn.GemsFactor > ContinueLimits.MinGemsFactor
                               ? $"x{carryOn.GemsFactor / 100m} each time"
                               : $"+{carryOn.GemsStep} each time";

            return $"{climb} - one run's ladder is {rungs} gems ({spent} to buy all eight)" +
                   (topsOutAt > 0
                        ? $", topping out at the {ContinueLimits.MaxGems}-gem ceiling on the " +
                          $"{topsOutAt}th"
                        : $", and it reaches the {ContinueLimits.MaxGems}-gem ceiling later");
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

                // Every rung has to name a tier the live table actually holds, or it pays a
                // chest nobody can price — a claim the server leaves unconfirmed for ever.
                // An **error**, because it is invisible in either file on its own: the ladder
                // lives in the manifest and the tiers in progression.json.
                foreach (var rung in groveEvent.Milestones)
                {
                    foreach (var track in Events.SeasonTracks.All)
                    {
                        string tierId = rung.TierIdOn(track);
                        if (tierId.Length == 0 || rung.TierOn(track) != null) continue;

                        result.Errors.Add($"season '{groveEvent.Id}' pays tier '{tierId}' at " +
                                          $"{rung.Goal} marks on the {Events.SeasonTracks.Id(track)} " +
                                          "track, which the tasks block does not define");
                    }
                }

                // How long the ladder takes to climb, against what the slates can deal. A
                // season nobody can finish is a countdown with an unreachable prize on it,
                // and the arithmetic is the only thing that can say so.
                int perDay = MarksPerDay();
                if (perDay > 0)
                {
                    long window = (groveEvent.EndUnix - groveEvent.StartUnix) / Events.EventRules.SecondsPerDay;
                    long reachable = window * perDay;

                    if (reachable < groveEvent.FinalGoal)
                        result.Warnings.Add($"season '{groveEvent.Id}' tops out at {groveEvent.FinalGoal} " +
                                            $"marks but its {window}-day window can deal about " +
                                            $"{reachable} to a player who claims every chest; the " +
                                            "last rungs are unreachable");
                }

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
                    .Append("  ·  ").Append(groveEvent.Milestones.Count).Append(" rung(s)  ·  ladder:");

                foreach (var milestone in groveEvent.Milestones)
                    line.Append("  ").Append(milestone.Goal).Append('→')
                        .Append(milestone.FreeTier)
                        .Append('/')
                        .Append(milestone.PassTier.Length == 0 ? "-" : milestone.PassTier);

                line.Append("  (tops at ").Append(groveEvent.FinalGoal).Append(" marks)");
                Debug.Log(line.ToString());
            }
        }

        /// <summary>
        /// About how many marks a day a player who claims everything is dealt.
        ///
        /// The daily slate over a day plus the weekly slate over a week, at the rate the
        /// rotation actually deals them — which is the only honest reading, because a slate
        /// of ten dealt three at a time never pays all ten in one period. Zero when the table
        /// pays no marks at all, which is a season nobody can advance and is caught by the
        /// reachability warning above rather than here.
        /// </summary>
        static int MarksPerDay()
        {
            var table = Progression.ProgressionRules.Table.Tasks;
            if (table == null) return 0;

            float perDay = 0f;

            foreach (var period in Tasks.TaskPeriods.All)
            {
                var slate = table.Slate(period);
                if (slate.Count == 0) continue;

                float sum = 0f;
                int live = 0;

                foreach (var task in slate)
                {
                    if (task.Retired) continue;
                    sum += task.Tier.Marks;
                    live++;
                }

                if (live == 0) continue;

                float dealt = Mathf.Min(table.ActivePerPeriod, live) * (sum / live);
                perDay += period == Tasks.TaskPeriod.Weekly ? dealt / 7f : dealt;
            }

            return Mathf.FloorToInt(perDay);
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
        /// <summary>
        /// The bonus wheel: what it really pays, and whether it looks like what it is.
        ///
        /// <para>
        /// <b>The mean is printed rather than judged.</b> The wheel multiplies
        /// <c>win_bonus</c>'s authored amount, so from the moment it is published that amount
        /// stops being what a view is worth — and nothing else in the file says so. What the
        /// placement <em>should</em> pay is an economy decision and not this check's to make;
        /// what it must never be is a surprise, which is the same bargain <see cref="ValidateGolden"/>
        /// strikes one method up.
        /// </para>
        /// <para>
        /// The two things it does refuse are the two a picture can get wrong. A slice that pays
        /// less than the flat offer is refused by the reader before this runs (the wheel only
        /// ever adds); what is left is <b>two equal figures side by side</b>, which makes a
        /// wheel look like it has fewer prizes than it has — the rim is drawn in the authored
        /// order, so the file is where that is fixed. A warning rather than an error, because
        /// it is a legibility judgement and a deliberately repeated figure on a big wheel is a
        /// coherent thing to want.
        /// </para>
        /// </summary>
        static void ValidateWheel(AdRewardTable ads, ContentValidationResult result, bool verbose)
        {
            if (ads == null) return;

            var wheel = ads.Wheel;

            // No wheel is the flat offer, which is what this game paid before there was one.
            if (!wheel.IsUsable) return;

            var offer = ads.Offer(AdPlacement.WinBonus);
            if (!offer.IsValid)
            {
                // The reader drops the wheel in this case and says so; reaching here would mean
                // it had stopped doing that.
                result.Errors.Add("a bonus wheel survived a table with no '" + AdPlacement.WinBonus +
                                  "' placement to multiply");
                return;
            }

            for (int i = 0; i < wheel.Count; i++)
            {
                int next = (i + 1) % wheel.Count;
                if (wheel.SliceAt(i).Percent != wheel.SliceAt(next).Percent) continue;

                result.Warnings.Add($"wheel slices {i} and {next} both pay " +
                                    $"{wheel.SliceAt(i).Percent}% and sit side by side. The rim is " +
                                    "drawn in the authored order, so the wheel will look like it " +
                                    "has fewer prizes than it has — interleave them in the file");
            }

            int mean = wheel.MeanPercent;
            long perView = BonusWheel.Apply(offer.Amount, mean);
            long best = BonusWheel.Apply(offer.Amount, wheel.TopPercent);

            if (mean <= WheelRules.MinPercent)
            {
                result.Errors.Add("the bonus wheel averages the flat offer, so the spin costs a " +
                                  "tap and changes nothing anybody can measure");
            }

            if (!verbose) return;

            var line = new System.Text.StringBuilder("[Glimmer] bonus wheel:");
            for (int i = 0; i < wheel.Count; i++)
                line.Append("  ").Append(wheel.SliceAt(i).Percent).Append('%');

            line.Append("  ·  1 in ").Append(wheel.Count).Append(" each");
            Debug.Log(line.ToString());

            Debug.Log($"[Glimmer] '{AdPlacement.WinBonus}' authors {offer.Amount} a view and really " +
                      $"pays about {perView} (mean {mean}%), best {best}. At a cap of " +
                      $"{offer.DailyCap} that is up to about {perView * offer.DailyCap} a day on " +
                      "average — hold it against the free-play figure below before retuning either");
        }

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
        /// <c>StreakTable</c> already refuses anything unreadable — an unknown kind, a retired
        /// one, a zero, a tier the tasks block does not define, a ladder longer than the cap.
        /// What is left is the shape, which is a design question a reader has no opinion
        /// about: a rung that pays less than an earlier one of the same kind, a chest humbler
        /// than one earlier in the lap, a lap so short that it comes round before a player
        /// notices it, and a lap that pays nothing at all.
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
                    if (!before.IsValid) continue;

                    // A chest is compared against the chest before it and a figure against the
                    // figure before it. The two are not comparable at all — what a chest holds
                    // is a roll, so "is a gold chest worth more than eight hundred credits" is a
                    // question with no answer a gate could check — which is why the ladder
                    // climbs in two independent runs rather than one.
                    if (rung.IsChest != before.IsChest) continue;

                    if (rung.IsChest)
                    {
                        if (rung.Tier.Rank < before.Tier.Rank)
                            result.Errors.Add($"streak night {night} pays the {rung.Tier.Id} chest but " +
                                              $"night {earlier} pays the {before.Tier.Id}; a longer " +
                                              "streak that is worth less is a reason to stop rather " +
                                              "than to continue");
                        break;
                    }

                    if (before.Kind != rung.Kind) continue;

                    if (rung.Amount < before.Amount)
                        result.Errors.Add($"streak night {night} pays {rung} but night {earlier} pays " +
                                          $"{before}; a longer streak that is worth less is a " +
                                          "reason to stop rather than to continue");
                    break;
                }
            }

            // The humblest chest in the game is what a daily task pays for two runs. A streak
            // asks for a run of consecutive days, which is the hardest thing this game asks of
            // anybody, so a night paying the bottom rung is a reward that reads as a penalty.
            // A warning rather than an error: it is a tuning opinion, and the tier ladder is
            // content that could grow a rung below today's bottom one.
            var tiers = ProgressionRules.Table.Tasks.Tiers;
            string humblest = tiers.Count > 0 ? tiers[0].Id : null;

            if (humblest != null)
                for (int night = 1; night <= streak.Length; night++)
                {
                    var rung = streak.Rung(night);
                    if (rung.IsChest && rung.Tier.Id == humblest)
                        result.Warnings.Add($"streak night {night} pays the '{humblest}' chest, which " +
                                            "is the humblest tier in the game; a streak asks for a " +
                                            "run of consecutive days and should pay above it");
                }

            // The shield: the one thing on the streak page that is for sale. A price the
            // content forgot is a feature that silently disappears from a screen, which is
            // exactly the failure a gate is for — and it is a warning rather than an error
            // because withdrawing the offer deliberately is a legitimate content decision.
            if (!streak.SellsShield)
                result.Warnings.Add("the streak block sells no shield (shieldGems is zero), so the " +
                                    "streak page draws no offer row at all. Set a price to put it back");

            // What one lap hands over, split by who adjudicates it. The currency half is the
            // half that has a server obligation attached, which is what the note below is for.
            long credits = 0, gems = 0;
            int paying = 0, chests = 0;

            for (int night = 1; night <= streak.Length; night++)
            {
                var rung = streak.Rung(night);
                if (!rung.IsValid) continue;

                paying++;
                if (rung.IsChest) { chests++; continue; }
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

            if (credits <= 0 && gems <= 0 && chests <= 0) return;

            // Said every time rather than only on a change, because the failure it warns
            // about is silent: the client draws this ladder from the file, the server pays
            // from config/progression, and a lap retuned here without a re-seed pays the old
            // figure into a wallet while the board advertises the new one.
            Debug.Log($"[Glimmer] one lap pays {credits} credits, {gems} gems and {chests} chest(s); " +
                      $"a shield costs {streak.ShieldGems} gems for {streak.ShieldDays} days. The " +
                      "currency and the chests are granted by the server from config/progression — run " +
                      "firebase/seed/seed-config.mjs after this change or players will be paid the " +
                      "previous ladder.");
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

                // Anything that is not the classic mode reports its own line and stops:
                // everything below reads a conduit board. Asked as "is it a board" rather than
                // "is it one of the others", which is the reasoning that broke the moment a
                // third kind of level existed.
                if (!level.HasBoard)
                {
                    Debug.Log($"[Glimmer] {report.Id} verified ({level.Mode} level)");
                    continue;
                }

                // Said rather than left to elimination. Everything below reads the conduit
                // board, and "it was not one of the other two" is the reasoning that broke the
                // moment a third kind of level existed.
                if (!level.HasBoard) continue;

                var tuning = level.Tuning;

                // The three numbers a glade is actually judged by, printed rather than left to
                // be worked out from the JSON: par is derived from the board, and both the
                // three-star line and the losing line are multiples of it that no level here
                // authors. A glade is graded on turns and nothing else.
                string budget = tuning.HasBudget ? tuning.MoveBudget.ToString() : "unlimited";

                Debug.Log($"[Glimmer] {report.Id} verified " +
                          $"({level.Layout.Width}x{level.Layout.Height}, par {tuning.Par}, " +
                          $"three stars at {tuning.GoldThreshold} turns, budget {budget})");
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
        /// Credits an engaged player collects in a day without watching a video: every task
        /// chest's guaranteed contents plus its expected bonus, and a streak night amortised
        /// over the ladder's lap — which since the ladder pays chests is itself a chest
        /// expectation as often as it is a figure.
        ///
        /// Read from the published tables rather than written down, so a retune moves this
        /// with it — the same rule every explanatory panel in the game follows.
        /// </summary>
        static long DailyCreditIncome(ProgressionTable table)
        {
            long daily = 0;

            // The task chests: every live task's tier at its expectation, averaged over the
            // slate and scaled to what a period deals — the daily slate over a day, the weekly
            // over seven. The daily *ladder* this replaced no longer pays anybody on this build.
            daily += TaskIncome(table.Tasks, ExpectedCredits);

            daily += StreakIncome(table.Streak, ChestDropKind.Credits, ExpectedCredits);

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
            }

            // **A lane's copy is derived from its track id, and a lane with no ladder is made of
            // almost nothing else.** The ordinary lanes owe a name and a tagline, which the
            // switcher draws; a lane that is a single endless run draws a whole screen instead of
            // a map (`EndlessHub`), and three of the six things on it are strings nothing else in
            // this project names. Asked of the lanes the catalog actually carries rather than of
            // every lane this build knows, because a lane whose chapters are all disabled draws
            // nothing and owes no words.
            var lanes = new HashSet<GameTrack>();

            foreach (var chapter in content.Index.Chapters) lanes.Add(chapter.Track);

            foreach (var track in lanes)
            {
                Require(table, track.NameKey, $"track '{track}'", result);
                Require(table, track.TaglineKey, $"track '{track}'", result);

                if (track.Laddered) continue;

                for (int i = 1; i <= EndlessHubLayout.Points; i++)
                    Require(table, track.PointKey(i),
                            $"track '{track}', which draws a hub rather than a map", result);
            }

            // A turret's name and its one line are derived from its id like a companion's, so the
            // source scan below cannot see them either - and unlike a companion's they are the
            // only words the game ever says about what a turret does.
            foreach (var model in ProgressionRules.Table.Wards.Models)
            {
                Require(table, model.NameKey, $"turret '{model.Id}'", result);
                Require(table, model.NoteKey, $"turret '{model.Id}'", result);
            }

            // A reminder's two lines are derived from its kind's permanent id, so the source
            // scan below sees neither — and a notification whose title does not resolve is a
            // blank row in somebody's shade, drawn by the operating system with the app not
            // running, which is the least observable failure in this whole project.
            //
            // Asked of every kind this build knows rather than of the authored slate, because
            // the built-in table ships inside the app and is what a first launch and a
            // malformed block both fall back to.
            foreach (var kind in Notifications.NotificationKinds.All)
            {
                string id = Notifications.NotificationKinds.Id(kind);
                Require(table, Notifications.NotificationKinds.TitleKey(kind), $"reminder '{id}'", result);
                Require(table, Notifications.NotificationKinds.BodyKey(kind), $"reminder '{id}'", result);
            }

            // Companion names are derived from the id like a level's, so the source scan
            // below cannot see them — only this can.
            foreach (var companion in content.Index.Companions)
                Require(table, companion.NameKey, $"companion '{companion.Id}'", result);

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
            // Walked over every mechanic rather than over the teaching order: the two were the
            // same list until a lesson appeared that no board can bring, and a check over the
            // order would have silently stopped covering all of them.
            foreach (var mechanic in Mechanic.All)
            {
                Require(table, mechanic.TitleKey, $"mechanic '{mechanic.Id}'", result);
                Require(table, mechanic.BodyKey, $"mechanic '{mechanic.Id}'", result);

                RequireTipArgs(table, mechanic, result);
            }

            // And a utility's two, derived from its id like everything above (invariant 5a) and
            // therefore invisible to the source scan below. A utility with no strings ships as
            // "utility.firepot.name" written across a shop panel and a bar slot with no name at
            // all — which is exactly what a mechanic with no strings does, one screen along.
            foreach (var item in ProgressionRules.Table.Utilities.Items)
            {
                Require(table, item.NameKey, $"utility '{item.Id}'", result);
                Require(table, item.NoteKey, $"utility '{item.Id}'", result);
            }

            // And the tasks' and chest tiers', derived from their ids for the same reason. A
            // task with no title is a row reading "task.d_play.name" on the page; a task whose
            // target is one and has no singular sentence reads "1 battles", which is a
            // warning because every language but this one may not need it.
            var tasks = ProgressionRules.Table.Tasks;
            foreach (var tier in tasks.Tiers)
                Require(table, tier.NameKey, $"chest tier '{tier.Id}'", result);

            foreach (var period in Tasks.TaskPeriods.All)
                foreach (var task in tasks.Slate(period))
                {
                    Require(table, task.NameKey, $"task '{task.Id}'", result);
                    if (task.Target == 1 && !table.TryGet(task.NameOneKey, out _))
                        result.Warnings.Add($"task '{task.Id}' has a target of one and no " +
                                            $"'{task.NameOneKey}'; the sentence reads '1 battles'");
                }

            // And a mode's, for the same reason and with one sharper edge. Both of its strings
            // are drawn by the switcher, which is chrome on the map - so a mode shipped without
            // them does not fail anywhere, it simply offers a way of playing labelled
            // "mode.wisp.name". Walked over every mode this build carries rather than over the
            // ones the catalog happens to have chapters for: a mode with no content yet is
            // exactly the one whose strings nobody has thought about.
            foreach (var mode in GameMode.Shipped)
            {
                Require(table, mode.NameKey, $"mode '{mode.Value}'", result);
                Require(table, mode.TaglineKey, $"mode '{mode.Value}'", result);
            }

            // And every line of dialogue a level authors, which is the one kind of key in this
            // game that is *written down* rather than derived from an id (see `StoryLine.Key`).
            // It is unreachable from source by construction, so the scan below cannot see it and
            // nothing else can either; and `ContentMapper.ReadStory` deliberately *drops* a line
            // it cannot read rather than refusing the level, which is only safe while this
            // exists. A missing string here is a character standing in silence.
            foreach (var level in content.AllLevels())
            {
                var story = level.Presentation.Story;
                if (story == null || !story.Any) continue;

                foreach (var beat in story.Beats)
                    foreach (var line in beat.Lines)
                        Require(table, line.Key, $"level '{level.Id}' ({beat.Cue})", result);
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
        /// Holds a lesson's body to the number of values it says it has to be told
        /// (<see cref="Mechanic.Args"/>): every index below it present, and none at or above it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The only place either half of a composed lesson can be checked.</b> Two of these
        /// sentences carry a figure that is content — how many stars open the next chapter — and
        /// invariant 21 is the reason it is printed rather than typed into the prose. What that
        /// buys has to be protected at both ends: a body that <em>lost</em> its placeholder is a
        /// lesson that silently stops naming the number, and a body that <em>gained</em> one is a
        /// literal "{1}" drawn on a panel a player is shown once in their life. Neither fails
        /// anywhere else, because <c>Loc.Format</c> catches the mismatch and hands back the
        /// pattern — which is right at run time and is exactly what makes it invisible.
        /// </para>
        /// <para>
        /// It is the string side that this is really for: a body is translated, and a translator
        /// rearranging a sentence is the likeliest way a placeholder goes missing.
        /// <c>ScreenLessons</c> holds the call sites to the same number.
        /// </para>
        /// </remarks>
        static void RequireTipArgs(LocTable table, Mechanic mechanic, ContentValidationResult result)
        {
            if (!table.TryGet(mechanic.BodyKey, out var body) || string.IsNullOrEmpty(body)) return;

            for (int i = 0; i < mechanic.Args; i++)
                if (!body.Contains("{" + i + "}"))
                    result.Errors.Add(
                        $"mechanic '{mechanic.Id}' is composed with {mechanic.Args} value(s) and "
                        + $"its body never writes '{{{i}}}', so that value is dropped silently");

            // One past the end is enough: a body is written against a declaration, so the first
            // index nobody supplies is where a gap shows, and reporting every one of them would
            // bury the line that matters under a run of identical ones.
            if (body.Contains("{" + mechanic.Args + "}"))
                result.Errors.Add(
                    $"mechanic '{mechanic.Id}' is composed with {mechanic.Args} value(s) and its "
                    + $"body writes '{{{mechanic.Args}}}', which is drawn to the player as those "
                    + "four characters");
        }

        /// <summary>
        /// The frozen legacy index table must still point at real levels, or players
        /// updating from the original build would silently lose their stars.
        ///
        /// <para>
        /// <b>Hidden is not gone, and the two get different severities.</b> A level whose
        /// chapter carries <c>"disabled": true</c> is switched off, not removed: the manifest
        /// still names the id, nothing else may claim it, and re-enabling the chapter puts every
        /// record back exactly where it was. That is a real state this game ships in — the
        /// classic glade and Lightfall are both hidden as of invariant 38 — and failing the
        /// build on it would mean the flag could never be used on a chapter the legacy import
        /// names, which is every chapter of the original build. A level the manifest does not
        /// name <em>at all</em> is the failure invariant 2 exists to raise, and stays an error.
        /// </para>
        /// </summary>
        static void ValidateLegacyMigration(CatalogIndex index, ContentValidationResult result)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);

            if (ChapterFiles.TryReadManifest(out var manifest, out _) && manifest?.chapters != null)
                foreach (var chapter in manifest.chapters)
                {
                    if (chapter?.levels == null) continue;
                    foreach (var id in chapter.levels)
                        if (!string.IsNullOrEmpty(id)) listed.Add(id);
                }

            foreach (var missing in LegacyPlayerPrefsImport.MissingFromCatalog(index))
            {
                if (listed.Contains(missing))
                {
                    result.Warnings.Add($"legacy save migration maps to '{missing}', whose chapter is " +
                                        "disabled; the record is hidden rather than orphaned, and " +
                                        "re-enabling the chapter restores it");
                    continue;
                }

                result.Errors.Add($"legacy save migration maps to '{missing}', which is no longer in the catalog; " +
                                  "removing a level that shipped in the original build orphans player progress");
            }
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

            // And that the art is imported the way the folder rules say. This one is not about
            // whether an asset is *there* — it is about what it costs once it is, which nothing
            // else in this gate can see: an uncompressed texture validates, addresses, loads and
            // draws perfectly while taking several times the memory it should. See
            // ArtImportRules.Audit for why a preprocessor alone cannot be the answer (7a).
            errors.AddRange(ArtImportRules.Audit());

#if GLIMMER_HAS_ADDRESSABLES
            // Before the audit, because it changes what is in the build. The VFX bench is a
            // developer tool sitting on two hundred megabytes of licensed particle art, and this
            // is the one line standing between that and a store build: the bundle is packed only
            // when this target's defines ask for it.
            VfxBenchGroup.Gate(BuildPipeline.GetBuildTargetGroup(report.summary.platform));

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

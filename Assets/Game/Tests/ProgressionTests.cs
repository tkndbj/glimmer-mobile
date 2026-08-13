using System.Collections.Generic;
using System.Threading;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The XP curve and the reward arithmetic.
    ///
    /// These matter more than they look. XP is derived rather than stored, which is
    /// what lets it be merged across devices and retuned after launch — but it also
    /// means every player's level is recomputed from this code on every launch, so a
    /// mistake here does not corrupt one save, it moves everybody at once.
    /// </summary>
    public sealed class ProgressionTests
    {
        static ProgressionTable Read(string json)
        {
            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryRead(json, out var table, problems),
                          string.Join("; ", problems));
            return table;
        }

        static string Curve(string bands = "[100, 200, 300]", int tail = 400, int increment = 100,
                            int maxLevel = 50)
            => "{\"schemaVersion\":1,\"maxLevel\":" + maxLevel + ",\"xpToNext\":" + bands +
               ",\"tailXpToNext\":" + tail + ",\"tailXpIncrement\":" + increment +
               ",\"rewards\":{\"xpFirstClear\":40,\"xpPerStar\":20," +
               "\"creditsFirstClear\":30,\"creditsPerStar\":15}}";

        // ---------------------------------------------------------------- curve
        [Test]
        public void LevelOneCostsNothingAndTheBandsAccumulate()
        {
            var table = Read(Curve());

            Assert.AreEqual(0, table.XpToReach(1));
            Assert.AreEqual(100, table.XpToReach(2));
            Assert.AreEqual(300, table.XpToReach(3));
            Assert.AreEqual(600, table.XpToReach(4));
        }

        [Test]
        public void TheTailContinuesArithmeticallyPastTheAuthoredBands()
        {
            var table = Read(Curve());

            // three authored bands, then 400, then 500, then 600...
            Assert.AreEqual(600 + 400, table.XpToReach(5));
            Assert.AreEqual(600 + 400 + 500, table.XpToReach(6));
            Assert.AreEqual(500, table.XpToNext(5));
        }

        [Test]
        public void ExactlyEnoughXpReachesTheNextLevel()
        {
            var table = Read(Curve());

            Assert.AreEqual(1, table.LevelFor(99).Level);
            Assert.AreEqual(2, table.LevelFor(100).Level, "the threshold is inclusive");
            Assert.AreEqual(2, table.LevelFor(299).Level);
            Assert.AreEqual(3, table.LevelFor(300).Level);
        }

        [Test]
        public void LevelNeverFallsAsXpRises()
        {
            var table = Read(Curve());

            int previous = 0;
            for (long xp = 0; xp < 20000; xp += 37)
            {
                int level = table.LevelFor(xp).Level;
                Assert.GreaterOrEqual(level, previous, $"level went backwards at {xp} xp");
                previous = level;
            }
        }

        [Test]
        public void ProgressIsConsistentWithTheBandItIsIn()
        {
            var table = Read(Curve());
            var at = table.LevelFor(150);          // 50 into a 200-wide band

            Assert.AreEqual(2, at.Level);
            Assert.AreEqual(50, at.XpIntoLevel);
            Assert.AreEqual(200, at.XpForNextLevel);
            Assert.AreEqual(150, at.XpRemaining);
            Assert.AreEqual(.25f, at.Progress01, .0001f);
        }

        [Test]
        public void TheCapHoldsAndReadsAsComplete()
        {
            var table = Read(Curve(maxLevel: 5));
            var at = table.LevelFor(long.MaxValue / 4);

            Assert.AreEqual(5, at.Level);
            Assert.IsTrue(at.IsMaxLevel);
            Assert.AreEqual(0, at.XpRemaining);
            Assert.AreEqual(1f, at.Progress01, "a capped bar reads as full, never as empty");
        }

        [Test]
        public void ABandCostingNothingIsRejected()
        {
            var problems = new List<string>();

            Assert.IsFalse(ProgressionTable.TryRead(Curve("[100, 0, 300]"), out _, problems),
                           "a zero-cost band would grant unbounded levels at once");
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AShrinkingTailIsRejected()
        {
            var problems = new List<string>();

            Assert.IsFalse(ProgressionTable.TryRead(Curve(increment: -50), out _, problems));
            Assert.IsFalse(ProgressionTable.TryRead(Curve(tail: 0), out _, problems));
        }

        [Test]
        public void AMalformedFileFallsBackToTheBuiltInCurve()
        {
            var problems = new List<string>();

            Assert.IsFalse(ProgressionTable.TryRead("{ not json", out var table, problems));
            Assert.AreSame(ProgressionTable.Default, table,
                           "a bad download must cost a retune, never a session");
        }

        // -------------------------------------------------------------- rewards
        [Test]
        public void AChapterOverrideInheritsTheFieldsItDoesNotSet()
        {
            string json = "{\"schemaVersion\":1,\"xpToNext\":[100],\"tailXpToNext\":100," +
                          "\"tailXpIncrement\":0,\"rewards\":{\"xpFirstClear\":40,\"xpPerStar\":20," +
                          "\"creditsFirstClear\":30,\"creditsPerStar\":15}," +
                          "\"chapterRewards\":[{\"chapterId\":\"c02_deeps\",\"xpPerStar\":50}]}";

            var rule = Read(json).RuleFor(ChapterId.Parse("c02_deeps"));

            Assert.AreEqual(50, rule.XpPerStar, "the override applies");
            Assert.AreEqual(40, rule.XpFirstClear, "an unwritten field inherits rather than zeroing");
            Assert.AreEqual(15, rule.CreditsPerStar);
        }

        [Test]
        public void AnUnknownChapterGetsTheDefaultRule()
        {
            var table = Read(Curve());
            var rule = table.RuleFor(ChapterId.Parse("never_shipped"));

            Assert.AreEqual(table.DefaultRule.XpPerStar, rule.XpPerStar,
                            "a chapter out of the catalog must not zero what it paid");
        }

        [Test]
        public void AnUnclearedGladeIsWorthNothing()
        {
            var table = Read(Curve());
            var record = LevelRecord.Empty(LevelId.Parse("a"));

            Assert.IsTrue(ProgressionLedger.Value(record, ChapterId.None, table).IsZero);
        }

        // --------------------------------------------------------------- ledger
        static LevelRecord Cleared(string id, int stars) =>
            LevelRecord.Empty(LevelId.Parse(id)).WithRun(stars, 10, 100);

        [Test]
        public void ReplayingAClearedGladeWithAWorseResultAwardsNothing()
        {
            var table = Read(Curve());
            var before = Cleared("a", 3);
            var after = before.WithRun(1, 40, 200);            // worse on both measures

            var delta = ProgressionLedger.Delta(before, after, ChapterId.None, table);

            Assert.IsTrue(delta.IsZero,
                          "a replay that beat nothing must pay nothing, with no rule to say so");
        }

        [Test]
        public void ImprovingStarsPaysOnlyTheDifference()
        {
            var table = Read(Curve());                          // 40 first clear + 20 a star
            var before = Cleared("a", 1);                       // 60
            var after = before.WithRun(3, 5, 200);              // 100

            var delta = ProgressionLedger.Delta(before, after, ChapterId.None, table);

            Assert.AreEqual(40, delta.Xp);
            Assert.AreEqual(30, delta.EarnedCredits);           // 30 + 15*3 minus 30 + 15*1
            Assert.AreEqual(0, delta.ClearedGlades, "it was already cleared");
        }

        static FixedChapterMap Known(params string[] levels)
        {
            var map = new FixedChapterMap();
            foreach (var level in levels) map.Add(level, "c_test");
            return map;
        }

        [Test]
        public void TotalsAreIndependentOfTheOrderRecordsAreSeen()
        {
            var table = Read(Curve());
            var known = Known("a", "b", "c");
            var forwards = new[] { Cleared("a", 1), Cleared("b", 2), Cleared("c", 3) };
            var backwards = new[] { Cleared("c", 3), Cleared("b", 2), Cleared("a", 1) };

            var one = ProgressionLedger.Compute(forwards, known, table);
            var two = ProgressionLedger.Compute(backwards, known, table);

            Assert.AreEqual(one.Xp, two.Xp);
            Assert.AreEqual(one.EarnedCredits, two.EarnedCredits);
            Assert.AreEqual(3, one.ClearedGlades);
            Assert.AreEqual(6, one.TotalStars);
        }

        [Test]
        public void ComputingTwiceGivesTheSameAnswer()
        {
            var table = Read(Curve());
            var known = Known("a", "b");
            var records = new[] { Cleared("a", 3), Cleared("b", 2) };

            var once = ProgressionLedger.Compute(records, known, table);
            var twice = ProgressionLedger.Compute(records, known, table);

            Assert.AreEqual(once.Xp, twice.Xp,
                            "derivation must be a pure function, or it is an accumulator in disguise");
        }

        /// <summary>
        /// This is the opposite of what the client used to do, and the change was
        /// deliberate. Paying out for a glade the catalog cannot vouch for means a save
        /// listing ten thousand invented level ids mints currency — so the server has to
        /// refuse them, and the client has to refuse them the same way or the two
        /// disagree about what a player can afford.
        ///
        /// What protects a player from a chapter that is genuinely, temporarily missing
        /// is the earned high-water mark on both sides, not counting phantom levels.
        /// </summary>
        [Test]
        public void ALevelTheCatalogCannotVouchForEarnsNothing()
        {
            var table = Read(Curve());
            var known = Known("a");

            var totals = ProgressionLedger.Compute(
                new[] { Cleared("a", 3), Cleared("invented", 3) }, known, table);

            Assert.AreEqual(100, totals.Xp, "an invented id must not inflate a real total");
            Assert.AreEqual(1, totals.ClearedGlades);
        }

        [Test]
        public void ADuplicatedLevelIdCountsOnce()
        {
            var table = Read(Curve());
            var totals = ProgressionLedger.Compute(
                new[] { Cleared("a", 3), Cleared("a", 3) }, Known("a"), table);

            Assert.AreEqual(100, totals.Xp);
            Assert.AreEqual(1, totals.ClearedGlades);
        }

        [Test]
        public void StarsBeyondThreeAreClamped()
        {
            var table = Read(Curve());
            var record = new LevelRecord(LevelId.Parse("a"), stars: 99, bestMoves: 5,
                                         clears: 1, firstClearedUnix: 1, lastPlayedUnix: 1);

            Assert.AreEqual(100, ProgressionLedger.Compute(new[] { record }, Known("a"), table).Xp,
                            "a forged record must not be able to buy a fourth star");
        }

        // ------------------------------------------------------- shipped content
        [Test]
        public void TheShippedProgressionFileIsValid()
        {
            var fetch = new BundledContentSource()
                .FetchAsync(ContentPaths.Progression, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(fetch.Success, $"{ContentPaths.Progression} must ship: {fetch.Error}");

            var problems = new List<string>();
            Assert.IsTrue(ProgressionTable.TryRead(fetch.Text, out var table, problems),
                          string.Join("; ", problems));
            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.Greater(table.MaxLevel, 1);
        }

        /// <summary>
        /// Proves the shipped chapter override is actually read. Worth a test of its own
        /// because the failure mode is silent — a rule that does not deserialise looks
        /// exactly like a rule that is working, and every chapter pays the default rate.
        /// </summary>
        [Test]
        public void TheShippedChapterOverrideActuallyApplies()
        {
            var fetch = new BundledContentSource()
                .FetchAsync(ContentPaths.Progression, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.IsTrue(fetch.Success);

            var table = Read(fetch.Text);
            var index = SaveMigrationTests.LoadBundledIndex();

            bool anyOverride = false;
            foreach (var chapter in index.Chapters)
                anyOverride |= table.HasOverrideFor(chapter.Id);

            Assert.IsTrue(anyOverride,
                          "the shipped file declares a chapter override; if none is visible here, " +
                          "inherited DTO fields are not being deserialised");
        }
    }
}

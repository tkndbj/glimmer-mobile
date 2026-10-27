using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The store's rating box (invariant 68): when it may be asked for, how often, and what an install
    /// remembers. The rule is one pure function (<see cref="RatingRules.Judge"/>), so every clause is
    /// held here; the shipped block is read off disk and resolved, so a retune that breaks a bound fails
    /// offline. Nothing here can see whether a player rated - neither store says - which is why the
    /// history counts asks, never ratings.
    /// </summary>
    public sealed class RatingPromptTests
    {
        const long Now = 1_793_000_000L;
        const long Day = 86_400L;

        static RatingTable Table(int minLevels = 20, int minStars = 3, int gapDays = 10, int maxAsks = 3, int quiet = 30)
        {
            var problems = new List<string>();
            var table = RatingTable.Resolve(new RatingPromptDto
            {
                minClearedLevels = minLevels, minStars = minStars, gapDays = gapDays,
                maxAsks = maxAsks, moneyQuietMinutes = quiet,
            }, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            return table;
        }

        static RatingVerdict Judge(RatingTable table, RatingHistory history, int stars = 3, int cleared = 25,
                                   long now = Now, long money = 0L)
            => RatingRules.Judge(table, history, stars, cleared, now, money);

        // ------------------------------------------------------------ the rule
        [Test]
        public void AThreeStarWinPastTwentyLevelsAsks()
            => Assert.AreEqual(RatingVerdict.Ask, Judge(Table(), default));

        [Test]
        public void EveryClauseRefusesOnItsOwn()
        {
            var table = Table();
            Assert.AreEqual(RatingVerdict.TooFewLevels, Judge(table, default, cleared: 19), "below twenty levels");
            Assert.AreEqual(RatingVerdict.Ask, Judge(table, default, cleared: 20), "twenty is enough");
            Assert.AreEqual(RatingVerdict.TooFewStars, Judge(table, default, stars: 2), "two stars is not the moment");
            Assert.AreEqual(RatingVerdict.AfterMoney, Judge(table, default, money: Now - 29 * 60), "a sales panel a moment ago");
            Assert.AreEqual(RatingVerdict.Ask, Judge(table, default, money: Now - 31 * 60), "and long enough after one");
            Assert.AreEqual(RatingVerdict.Off, Judge(RatingTable.Empty, default), "no block, no ask");
            Assert.AreEqual(RatingVerdict.Off, Judge(null, default));
        }

        [Test]
        public void AsksAreTenDaysApartAndThreeAnInstall()
        {
            var table = Table();
            var once = new RatingHistory(0, 0).After(Now);

            Assert.AreEqual(RatingVerdict.TooSoon, Judge(table, once, now: Now + 10 * Day - 1), "not inside ten days");
            Assert.AreEqual(RatingVerdict.Ask, Judge(table, once, now: Now + 10 * Day), "and again at ten");

            var thrice = once.After(Now + 10 * Day).After(Now + 20 * Day);
            Assert.AreEqual(3, thrice.Asks);
            Assert.AreEqual(RatingVerdict.AskedEnough, Judge(table, thrice, now: Now + 400 * Day), "never a fourth time");
        }

        [Test]
        public void AClockSetBackwardsNeverAsksAgain()
        {
            var once = new RatingHistory(0, 0).After(Now);
            Assert.AreEqual(RatingVerdict.TooSoon, Judge(Table(), once, now: Now - 30 * Day));
        }

        // ------------------------------------------------------------ the history
        [Test]
        public void TheHistoryReadsBackAndGarbageHasAskedNothing()
        {
            var history = new RatingHistory(2, Now);
            var back = RatingHistory.Decode(history.Encode());
            Assert.AreEqual(2, back.Asks);
            Assert.AreEqual(Now, back.LastAskUnix);

            foreach (var bad in new[] { null, "", "x", "2|", "|5", "two|5", "1|2|3" })
                Assert.AreEqual(0, RatingHistory.Decode(bad).Asks, bad ?? "null");

            Assert.AreEqual(Now, new RatingHistory(1, Now).After(Now - Day).LastAskUnix, "the last ask only moves forward");
        }

        [Test]
        public void AnAskIsSpentOnTheDeviceAndNowhereElse()
        {
            var store = new EndlessCoins.MemoryStore();
            RatingLedger.UseStore(store);
            try
            {
                Assert.AreEqual(0, RatingLedger.History.Asks);
                RatingLedger.NoteAsked();
                RatingLedger.NoteAsked();
                Assert.AreEqual(2, RatingLedger.History.Asks);
                Assert.AreEqual("2|", store.Read("glimmer.rating.asks").Substring(0, 2), "one device-local line");
            }
            finally
            {
                RatingLedger.UseStore(null);
            }
        }

        // ------------------------------------------------------------ the block
        [Test]
        public void AFaultyBlockIsTheFeatureOff()
        {
            foreach (var dto in new[]
            {
                new RatingPromptDto { minClearedLevels = 20, minStars = 4, gapDays = 10, maxAsks = 3 },
                new RatingPromptDto { minClearedLevels = 20, minStars = 3, gapDays = 0, maxAsks = 3 },
                new RatingPromptDto { minClearedLevels = 20, minStars = 3, gapDays = 10, maxAsks = 11 },
                new RatingPromptDto { minClearedLevels = 20, minStars = 3, gapDays = 10, maxAsks = 3, moneyQuietMinutes = -1 },
            })
            {
                var problems = new List<string>();
                Assert.AreSame(RatingTable.Empty, RatingTable.Resolve(dto, problems));
                Assert.IsNotEmpty(problems, "a fault is named, never swallowed");
            }

            Assert.AreSame(RatingTable.Empty, RatingTable.Resolve(new RatingPromptDto(), new List<string>()),
                           "a block nobody wrote is absent, and absent is off");
        }

        [Test]
        public void TheShippedBlockAsksAfterTwentyLevelsTenDaysApart()
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json");
            if (!File.Exists(path)) Assert.Inconclusive("progression.json is not on disk");

            var block = TestJson.Object(TestJson.Child(TestJson.Object(TestJson.Parse(File.ReadAllText(path))), "ratingPrompt"));
            var problems = new List<string>();
            var table = RatingTable.Resolve(new RatingPromptDto
            {
                minClearedLevels = TestJson.Int(block, "minClearedLevels"),
                minStars = TestJson.Int(block, "minStars"),
                gapDays = TestJson.Int(block, "gapDays"),
                maxAsks = TestJson.Int(block, "maxAsks"),
                moneyQuietMinutes = TestJson.Int(block, "moneyQuietMinutes"),
            }, problems);

            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.IsTrue(table.IsOn);
            Assert.AreEqual(20, table.MinClearedLevels);
            Assert.AreEqual(3, table.MinStars);
            Assert.AreEqual(10, table.GapDays);
            Assert.AreEqual(3, table.MaxAsks);
        }
    }
}

using System.Collections.Generic;
using GlimmerGrove.Challenges;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The daily challenges' foundation: the rotation, the allowance, the deals, the payout
    /// and the merge — everything the owner asked to be settled before the slate grows.
    ///
    /// <para>
    /// Five things are under contract. Every player on one day deals the same level, and slot
    /// nought walks every level once per cycle. A play is spent when dealt and a genre allows
    /// exactly the allowance. A deal raises the allowance for its window, a bigger one under a
    /// running one is an upgrade, a smaller is refused, and a refused debit takes the deal back.
    /// A win pays credits under an id derived from the day, the genre and the win's ordinal,
    /// and moves the lifetime tally the XP is derived from. And the block merges as a join.
    /// </para>
    /// </summary>
    public sealed class ChallengeLedgerTests
    {
        sealed class FixedClock : IGameClock
        {
            public long Now;
            public long UtcNowUnix => Now;
            public bool IsTrusted => true;
        }

        const int Day = 20_500;
        const long Noon = Day * DailyRules.SecondsPerDay + 12L * 3600L;

        FixedClock _clock;

        [SetUp]
        public void Start()
        {
            _clock = new FixedClock { Now = Noon };
            GameClock.Set(_clock);
            ChallengeLedger.ResetForTests();
            Wallet.LoadFrom(new SaveFileDto());
            ProgressionStore.LoadFrom(new SaveFileDto());
            ChallengeRules.Publish(Table(3, 1));
            PlayerProgression.Invalidate();
        }

        [TearDown]
        public void Restore()
        {
            GameClock.Set(new DeviceClock());
            ChallengeLedger.ResetForTests();
            ChallengeRules.Reset();
            Wallet.LoadFrom(new SaveFileDto());
            PlayerProgression.Invalidate();
        }

        // ------------------------------------------------------------------ the table
        static ChallengeDto Pairs(string id, int seed = 7)
            => new ChallengeDto
            {
                id = id, genre = "pairs", width = 2, height = 2, rows = new[] { "rg", "gr" },
                waves = new[] { "0 r1" }, hill = 5, bolts = 1, seed = seed,
            };

        static ChallengeDto Merge(string id)
            => new ChallengeDto
            {
                id = id, genre = "merge", width = 4, height = 4, rows = new[] { "1..1", "....", "....", "...." },
                waves = new[] { "9 r1" }, hill = 9, bolts = 1, seed = 3, target = 3,
            };

        /// <summary>A slate of <paramref name="pairs"/> pairs rows and <paramref name="merges"/> merge rows, with the three deals.</summary>
        static ChallengeTable Table(int pairs, int merges, int freePlays = 2, int coins = 40, int xp = 20)
        {
            var rows = new List<ChallengeDto>();
            for (int i = 0; i < pairs; i++) rows.Add(Pairs("p" + i, 11 + i));
            for (int i = 0; i < merges; i++) rows.Add(Merge("m" + i));

            var dto = new ChallengeTableDto
            {
                schemaVersion = ChallengeTable.Version,
                line = new ChallengeLineDto { damage = 1, health = 3, strike = 1 },
                allowance = new ChallengeAllowanceDto { freePlays = freePlays },
                tiers = new[]
                {
                    new ChallengeTierDto { id = "bronze", gems = 120, plays = 5, days = 30 },
                    new ChallengeTierDto { id = "silver", gems = 200, plays = 10, days = 30 },
                    new ChallengeTierDto { id = "gold", gems = 500, plays = 25, days = 30 },
                },
                rewards = new ChallengeRewardDto { coins = coins, xp = xp, maxClears = 25000 },
                challenges = rows.ToArray(),
            };

            var problems = new List<string>();
            bool ok = ChallengeTable.TryBuild(dto, out var table, problems);
            Assert.IsTrue(ok && problems.Count == 0, string.Join("; ", problems));
            return table;
        }

        static void Fund(long gems)
        {
            long already = PlayerProgression.Gems;
            if (gems > already) Wallet.Ledger(Currency.Gems).GrantLocally(gems - already);
            PlayerProgression.Invalidate();
        }

        // ------------------------------------------------------------------ the reader
        [Test]
        public void TheShippedFileReadsCleanWithItsBlocks()
        {
            var table = ChallengeTests.Shipped();

            Assert.AreEqual(2, table.FreePlays);
            Assert.AreEqual(3, table.Tiers.Count);
            Assert.AreEqual("bronze", table.Tiers[0].Id);
            Assert.AreEqual(25, table.Tiers[2].Plays);
            Assert.IsTrue(table.Rewards.PaysCoins && table.Rewards.PaysXp);
            Assert.AreEqual(4, table.Genres.Count, "every shipped genre has a row");
        }

        [Test]
        public void AnUnwrittenBlockTakesTheBuiltInFigures()
        {
            var dto = new ChallengeTableDto
            {
                schemaVersion = ChallengeTable.Version,
                line = new ChallengeLineDto { damage = 1, health = 3, strike = 1 },
                challenges = new[] { Pairs("a") },
            };
            var problems = new List<string>();
            Assert.IsTrue(ChallengeTable.TryBuild(dto, out var table, problems));
            Assert.AreEqual(0, problems.Count);

            Assert.AreEqual(ChallengeLimits.DefaultFreePlays, table.FreePlays);
            Assert.AreEqual(0, table.Tiers.Count);
            Assert.AreEqual(ChallengeLimits.DefaultCoinsPerClear, table.Rewards.Coins);
            Assert.AreEqual(ChallengeLimits.DefaultXpPerClear, table.Rewards.Xp);
            Assert.AreEqual(ChallengeLimits.DefaultMaxClears, table.Rewards.MaxClears);
        }

        [Test]
        public void ADealLadderThatDoesNotClimbIsRefusedWhole()
        {
            var dto = new ChallengeTableDto
            {
                schemaVersion = ChallengeTable.Version,
                line = new ChallengeLineDto { damage = 1, health = 3, strike = 1 },
                allowance = new ChallengeAllowanceDto { freePlays = 2 },
                tiers = new[]
                {
                    new ChallengeTierDto { id = "bronze", gems = 120, plays = 5, days = 7 },
                    new ChallengeTierDto { id = "silver", gems = 200, plays = 5, days = 7 },
                },
                challenges = new[] { Pairs("a") },
            };
            var problems = new List<string>();
            Assert.IsFalse(ChallengeTable.TryBuild(dto, out var table, problems));
            Assert.IsTrue(problems.Exists(p => p.Contains("silver") && p.Contains("climb")), string.Join("; ", problems));
            Assert.AreEqual(0, table.Tiers.Count, "a bad ladder sells nothing rather than half");

            dto.tiers[1].plays = 10;
            dto.tiers[1].gems = 120;
            problems.Clear();
            Assert.IsFalse(ChallengeTable.TryBuild(dto, out _, problems));
            Assert.IsTrue(problems.Exists(p => p.Contains("silver") && p.Contains("gems")), string.Join("; ", problems));

            dto.tiers[0].plays = 2;
            dto.tiers[1].plays = 10;
            dto.tiers[1].gems = 200;
            problems.Clear();
            Assert.IsFalse(ChallengeTable.TryBuild(dto, out _, problems), "a deal must beat the free figure");
        }

        [Test]
        public void ARateAboveTheTypoGuardIsClampedAndNamed()
        {
            var problems = new List<string>();
            var rule = ChallengeRewardRule.Resolve(new ChallengeRewardDto { coins = 5000, xp = 20, maxClears = 10 }, problems);
            Assert.AreEqual(ChallengeLimits.MaxCoinsPerClear, rule.Coins);
            Assert.AreEqual(1, problems.Count);

            problems.Clear();
            var withdrawn = ChallengeRewardRule.Resolve(new ChallengeRewardDto { coins = 0, xp = 0, maxClears = 10 }, problems);
            Assert.IsFalse(withdrawn.PaysCoins, "a written nought withdraws the payment rather than being repaired");
            Assert.IsFalse(withdrawn.PaysXp);
            Assert.AreEqual(0, problems.Count);
        }

        // ------------------------------------------------------------------ the art
        /// <summary>
        /// A genre's card picture is addressed off its spelling (`Ui/challenge_{spelling}`), so
        /// `artnames.py` cannot see the call site; this walks the enum against the global preload
        /// list and the disk instead. A fifth genre cut without its picture fails here rather than
        /// as a white rectangle on the card (invariant 7b).
        /// </summary>
        [Test]
        public void EveryGenresMarkIsPreloadedAndOnDisk()
        {
            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            string root = System.IO.Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Ui");

            foreach (ChallengeGenre genre in System.Enum.GetValues(typeof(ChallengeGenre)))
            {
                string key = ChallengeArt.GenreMarkKey(genre);
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.Ui(key)),
                              $"'{key}' is not in AssetManifest.UiSprites; the card would draw a white rectangle");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(root, key + ".png")),
                              $"'{key}.png' is not on disk; cut it with Tools/make_challenge_art.py");
            }
        }

        /// <summary>
        /// The deal band's chest and one stone per shipped deal are preloaded and on disk. A
        /// stone is keyed on its rung (<c>ChallengeArt.DealMark</c>), so this is the gate that
        /// asks for a fourth picture the day a fourth deal is authored — nothing else can, since
        /// the address is built and <c>artnames.py</c> cannot see it.
        /// </summary>
        [Test]
        public void TheDealChestAndEveryShippedDealsStoneArePreloadedAndOnDisk()
        {
            var declared = new HashSet<string>();
            foreach (var request in AssetPipeline.AssetManifest.GlobalAssets())
                declared.Add(request.Address);

            string root = System.IO.Path.Combine(TestJson.RepoRoot(), "Assets", "Game", "Art", "Ui");

            var keys = new List<string> { ChallengeArt.ChestKey };
            int shipped = ChallengeTests.Shipped().Tiers.Count;
            Assert.Greater(shipped, 0, "the shipped file sells no deal; the band's key would hide and this test would hold nothing");
            for (int rung = 1; rung <= shipped; rung++) keys.Add(ChallengeArt.DealMarkKey(rung));

            foreach (string key in keys)
            {
                Assert.IsTrue(declared.Contains(AssetPipeline.AssetManifest.Ui(key)),
                              $"'{key}' is not in AssetManifest.UiSprites; the sheet would draw a white rectangle");
                Assert.IsTrue(System.IO.File.Exists(System.IO.Path.Combine(root, key + ".png")),
                              $"'{key}.png' is not on disk; cut it with Tools/make_challenge_art.py (PACK_CUTS)");
            }
        }

        // ------------------------------------------------------------------ the rotation
        [Test]
        public void EveryPlayerDealsTheSameLevelAndNoDayOpensOnYesterdays()
        {
            var table = ChallengeRules.Table;

            var a = ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, 0);
            var b = ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, 0);
            Assert.AreSame(a, b, "one day, one genre, one level, on every device");

            int n = table.RowsOf(ChallengeGenre.Pairs).Count;

            // Within a day the sequence is a ring of every row, so k wins see k distinct levels
            // while k < n, and the ring wraps after.
            var day = ChallengeCalendar.Sequence(table, ChallengeGenre.Pairs, Day);
            Assert.AreEqual(n, day.Count);
            Assert.AreEqual(n, new HashSet<string>(day.ConvertAll(r => r.Id)).Count);
            Assert.AreSame(day[0], ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, n), "the ring wraps");

            // Consecutive days almost never open on the same level: about one day in n², which
            // at three rows is one in nine, against one in three with no exclusion at all. Pinned
            // as a rate rather than as a guarantee, because that is what a memoryless rule is.
            int repeats = 0;
            for (int d = Day - 900; d < Day + 900; d++)
                if (ReferenceEquals(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, d, 0),
                                    ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, d + 1, 0))) repeats++;
            Assert.Less(repeats, 1800 / 5, $"{repeats} repeats in 1800 days is worse than the rule promises");

            // Every row opens some day: coverage is probabilistic rather than exact, and over a
            // year of days three rows are certain.
            var seen = new HashSet<string>();
            for (int d = 0; d < 365; d++) seen.Add(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day + d, 0).Id);
            Assert.AreEqual(n, seen.Count, "a row never opened in a year");

            // Two genres rank apart, and the order really is per day.
            Assert.AreNotEqual(ChallengeCalendar.Sequence(table, ChallengeGenre.Pairs, Day).ConvertAll(r => r.Id),
                               ChallengeCalendar.Sequence(table, ChallengeGenre.Pairs, Day + 5).ConvertAll(r => r.Id));

            // A genre with one row deals it every time, and a negative day is still a day.
            Assert.AreEqual("m0", ChallengeCalendar.Slot(table, ChallengeGenre.Merge, Day, 7).Id);
            Assert.IsNotNull(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, -3, 2));
            Assert.IsNull(ChallengeCalendar.Slot(table, ChallengeGenre.Sokoban, Day, 0), "a genre with no rows deals nothing");
        }

        /// <summary>
        /// The reason the ring is a per-day ranking rather than a shuffled cycle (56f): adding a
        /// level must not re-deal the levels already there. A new row takes its own rank on each
        /// day; every other row keeps its place relative to the others.
        /// </summary>
        [Test]
        public void AddingALevelDoesNotReDealTheOthers()
        {
            var before = Table(4, 1);
            var after = Table(5, 1);   // p0..p3 unchanged, p4 appended

            int reopened = 0;
            for (int d = Day; d < Day + 365; d++)
            {
                string was = ChallengeCalendar.Slot(before, ChallengeGenre.Pairs, d, 0).Id;
                string now = ChallengeCalendar.Slot(after, ChallengeGenre.Pairs, d, 0).Id;
                if (now == "p4") { reopened++; continue; }

                // The one knock-on a stateless rule has: the day after one the new row's rank
                // wins, yesterday's exclusion moves off the old opener. Any other change is a
                // re-deal, which is what this fixture exists to refuse.
                bool knockOn = ChallengeCalendar.Ranked(after.RowsOf(ChallengeGenre.Pairs), ChallengeGenre.Pairs, d - 1)[0].Id == "p4";
                if (was != now && !knockOn)
                    Assert.Fail($"day {d} opened on {was} and now opens on {now}: adding p4 re-dealt the old rows");
            }

            Assert.Greater(reopened, 365 / 10, "the new row opens far fewer days than its share");
            Assert.Less(reopened, 365 / 3, "the new row opens far more days than its share");
        }

        // ------------------------------------------------------------------ the allowance
        [Test]
        public void APlayIsSpentWhenDealtAndTheAllowanceIsTheFreeFigure()
        {
            Assert.AreEqual(2, ChallengeLedger.Allowance);
            Assert.AreEqual(2, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs));
            Assert.AreEqual(2, ChallengeLedger.ReadyCount, "both genres have a play");

            var first = ChallengeLedger.Begin(ChallengeGenre.Pairs);
            Assert.IsNotNull(first);
            Assert.AreEqual(0, first.Slot);
            Assert.AreEqual(1, first.Attempt);
            Assert.AreEqual(1, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs));
            Assert.AreEqual(2, ChallengeLedger.PlaysLeft(ChallengeGenre.Merge), "genres spend apart");

            // A loss retries the same slot; the play is gone either way.
            ChallengeLedger.Lose(first, 3);
            var second = ChallengeLedger.Begin(ChallengeGenre.Pairs);
            Assert.AreEqual(0, second.Slot, "a lost level is dealt again");
            Assert.AreSame(first.Definition, second.Definition);
            Assert.AreEqual(0, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs));
            Assert.IsFalse(ChallengeLedger.CanPlay(ChallengeGenre.Pairs));
            Assert.IsNull(ChallengeLedger.Begin(ChallengeGenre.Pairs), "no third play for free");
            Assert.AreEqual(1, ChallengeLedger.ReadyCount);
        }

        [Test]
        public void AWinAdvancesToTheNextLevelOfTheDay()
        {
            var play = ChallengeLedger.Begin(ChallengeGenre.Pairs);
            var reward = ChallengeLedger.Win(play);

            Assert.AreEqual(40, reward.Coins);
            Assert.AreEqual(20, reward.Xp);
            Assert.AreEqual(1, ChallengeLedger.WinsToday(ChallengeGenre.Pairs));

            var next = ChallengeLedger.Begin(ChallengeGenre.Pairs);
            Assert.AreEqual(1, next.Slot);
            Assert.AreNotSame(play.Definition, next.Definition, "the next slot is the next level");
            Assert.AreSame(ChallengeCalendar.Slot(ChallengeRules.Table, ChallengeGenre.Pairs, Day, 1), next.Definition);
        }

        [Test]
        public void MidnightResetsTheDaysRowsAndKeepsTheTallyAndTheDeal()
        {
            Fund(120);
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze")));
            for (int i = 0; i < 5; i++) ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            Assert.AreEqual(0, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs));
            Assert.AreEqual(5, ChallengeLedger.LifetimeClears);

            _clock.Now += DailyRules.SecondsPerDay;

            Assert.AreEqual(Day + 1, ChallengeLedger.Day);
            Assert.AreEqual(5, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs), "a fresh day under a running deal");
            Assert.AreEqual(0, ChallengeLedger.WinsToday(ChallengeGenre.Pairs));
            Assert.AreEqual(5, ChallengeLedger.LifetimeClears, "the tally is for ever");
            Assert.AreEqual(29, ChallengeLedger.DaysLeft(ChallengeRules.Table.FindTier("bronze")));
        }

        // ------------------------------------------------------------------ the deals
        [Test]
        public void ADealRunsForExactlyItsDaysOfTheClock()
        {
            var bronze = ChallengeRules.Table.FindTier("bronze");

            Assert.AreEqual(TierBuy.TooPoor, ChallengeLedger.TryBuyTier(bronze));
            Fund(120);
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(5, ChallengeLedger.Allowance);
            Assert.AreEqual(0, PlayerProgression.Gems);
            Assert.AreEqual(TierBuy.Held, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(30, ChallengeLedger.DaysLeft(bronze));
            Assert.AreEqual(30L * DailyRules.SecondsPerDay, ChallengeLedger.SecondsLeft(bronze));

            // Thirty days of the clock from the instant of purchase, bought at noon: still
            // running at noon less a second on the thirtieth day, gone at noon exactly.
            _clock.Now = Noon + 30L * DailyRules.SecondsPerDay - 1L;
            Assert.AreEqual(5, ChallengeLedger.Allowance);
            Assert.AreEqual(1, ChallengeLedger.DaysLeft(bronze), "the last second is still the last day");
            Assert.AreEqual(1L, ChallengeLedger.SecondsLeft(bronze));

            _clock.Now += 1L;
            Assert.AreEqual(2, ChallengeLedger.Allowance, "exactly thirty days after purchase it is free again");
            Assert.IsNull(ChallengeLedger.HeldTier);
            Assert.AreEqual(0, ChallengeLedger.DaysLeft(bronze));

            // And it can be bought again for a fresh window from that instant.
            Fund(120);
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(30, ChallengeLedger.DaysLeft(bronze));
        }

        [Test]
        public void ABiggerDealUnderARunningOneCostsTheDifferenceAndSharesItsWindow()
        {
            var bronze = ChallengeRules.Table.FindTier("bronze");
            var silver = ChallengeRules.Table.FindTier("silver");
            var gold = ChallengeRules.Table.FindTier("gold");

            Fund(500);
            Assert.AreEqual(120, ChallengeLedger.PriceOf(bronze), "with nothing running the price is the full one");
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(380, PlayerProgression.Gems);

            // Ten days in, silver costs the difference and inherits bronze's window.
            _clock.Now += 10L * DailyRules.SecondsPerDay;
            Assert.AreEqual(80, ChallengeLedger.PriceOf(silver));
            Assert.AreSame(bronze, ChallengeLedger.Upgrades(silver));
            Assert.AreEqual(0, ChallengeLedger.PriceOf(bronze), "the running deal itself is not for sale");
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(silver));
            Assert.AreEqual(300, PlayerProgression.Gems);
            Assert.AreEqual(10, ChallengeLedger.Allowance);
            Assert.AreSame(silver, ChallengeLedger.HeldTier);
            Assert.AreEqual(20, ChallengeLedger.DaysLeft(silver), "the upgrade ends when bronze would have");
            Assert.AreEqual(TierBuy.Lower, ChallengeLedger.TryBuyTier(bronze), "a smaller one under it buys nothing");

            // And gold on top of silver costs what is left of gold's price: the three together
            // never cost more than gold bought outright.
            Assert.AreEqual(300, ChallengeLedger.PriceOf(gold));
            Assert.AreSame(silver, ChallengeLedger.Upgrades(gold));
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(gold));
            Assert.AreEqual(0, PlayerProgression.Gems);
            Assert.AreEqual(25, ChallengeLedger.Allowance);
            Assert.AreEqual(20, ChallengeLedger.DaysLeft(gold));

            // All three end together.
            _clock.Now = Noon + 30L * DailyRules.SecondsPerDay;
            Assert.AreEqual(2, ChallengeLedger.Allowance);
            Assert.IsNull(ChallengeLedger.HeldTier);
            Assert.AreEqual(500, ChallengeLedger.PriceOf(gold), "after the window a purchase is a fresh full-price one");
        }

        [Test]
        public void TheDealDebitIsDerivedFromTheTierTheWindowsStartAndThePurchaseDay()
        {
            Fund(200);
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze"));
            _clock.Now += 3L * DailyRules.SecondsPerDay;
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("silver"));

            var spends = Wallet.Ledger(Currency.Gems).PendingSpends;
            Assert.AreEqual(2, spends.Count);
            Assert.AreEqual(SpendEntry.ChallengeTierId("bronze", Day, Day), spends[0].Id);
            Assert.AreEqual(120, spends[0].Amount);
            Assert.AreEqual(SpendEntry.ChallengeTierId("silver", Day, Day + 3), spends[1].Id, "an upgrade names the window it inherits and the day it was bought");
            Assert.AreEqual(80, spends[1].Amount);

            Assert.IsTrue(SpendEntry.TryParseChallengeTierId("chaltier:silver:20500:20503", out string tier, out int from, out int bought));
            Assert.AreEqual("silver", tier);
            Assert.AreEqual(20500, from);
            Assert.AreEqual(20503, bought);
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("pass:watch_0001", out _, out _, out _));
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:20500", out _, out _, out _), "the old three-part shape is not one");
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:0:20500", out _, out _, out _));
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:20503:20500", out _, out _, out _), "a window cannot begin after its purchase");
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:+5:20500", out _, out _, out _));
        }

        [Test]
        public void ARefusedDealDebitTakesTheDealBack()
        {
            Fund(120);
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze"));
            Assert.AreEqual(5, ChallengeLedger.Allowance);

            ChallengeLedger.OnSpendRejected(Currency.Gems, SpendEntry.ChallengeTierId("bronze", Day - 1, Day - 1));
            Assert.AreEqual(5, ChallengeLedger.Allowance, "a different purchase's refusal moves nothing");

            ChallengeLedger.OnSpendRejected(Currency.Gems, SpendEntry.ChallengeTierId("bronze", Day, Day));
            Assert.AreEqual(2, ChallengeLedger.Allowance, "the refused purchase is gone");
            Assert.IsNull(ChallengeLedger.HeldTier);
        }

        // ------------------------------------------------------------------ the payout
        [Test]
        public void AWinRaisesAClaimDerivedFromTheDayTheGenreAndTheOrdinal()
        {
            long before = PlayerProgression.Credits;
            var first = ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            var second = ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            Assert.AreEqual(40, first.Coins);
            Assert.AreEqual(40, second.Coins);

            var grants = Wallet.Ledger(Currency.Credits).PendingGrants;
            Assert.AreEqual(2, grants.Count);
            Assert.AreEqual(GrantEntry.ChallengeClearId(Day, "pairs", 1, Currency.Credits), grants[0].Id);
            Assert.AreEqual("chal:20500:pairs:2:credits", grants[1].Id);
            Assert.AreEqual(GrantEntry.ChallengeClearReason, grants[0].Reason);
            Assert.AreEqual(before + 80, PlayerProgression.Credits, "a claim counts at once, so a reward opened offline is spendable offline");
        }

        [Test]
        public void AWinWithNoPlayBehindItStillCountsAndAWinOfAWonSlotPaysOnce()
        {
            var play = ChallengeLedger.Begin(ChallengeGenre.Pairs);
            ChallengeLedger.Win(play);

            // The same play won again (another device merged its win in, say): the row cannot
            // place it, the claim id collides, and only the tally moves.
            var again = ChallengeLedger.Win(play);
            Assert.AreEqual(0, again.Coins, "one slot, one claim");
            Assert.AreEqual(1, ChallengeLedger.WinsToday(ChallengeGenre.Pairs));
            Assert.AreEqual(2, ChallengeLedger.LifetimeClears);

            // A play dealt yesterday and won after midnight is paid against yesterday.
            var late = ChallengeLedger.Begin(ChallengeGenre.Merge);
            _clock.Now += DailyRules.SecondsPerDay;
            var reward = ChallengeLedger.Win(late);
            Assert.AreEqual(40, reward.Coins);
            Assert.AreEqual(0, ChallengeLedger.WinsToday(ChallengeGenre.Merge), "today's row is untouched");
            var grants = Wallet.Ledger(Currency.Credits).PendingGrants;
            Assert.IsTrue(Holds(grants, GrantEntry.ChallengeClearId(Day, "merge", 1, Currency.Credits)));
        }

        [Test]
        public void XpIsDerivedFromTheTallyThroughTheRuleAndReachesTheKeeperLevel()
        {
            long before = PlayerProgression.Xp;

            ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Merge));

            Assert.AreEqual(2, ChallengeLedger.LifetimeClears);
            Assert.AreEqual(40, PlayerProgression.ChallengeXp);
            Assert.AreEqual(before + 40, PlayerProgression.Xp);

            // The tally is the number; the rule over it is content, so a retune moves the XP
            // without moving the file.
            ChallengeRules.Publish(Table(3, 1, xp: 5));
            Assert.AreEqual(10, PlayerProgression.ChallengeXp);
        }

        [Test]
        public void ARateOfNoughtPaysNothingAndSpendsThePlay()
        {
            ChallengeRules.Publish(Table(3, 1, coins: 0, xp: 0));
            var reward = ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            Assert.IsFalse(reward.Any);
            Assert.AreEqual(0, Wallet.Ledger(Currency.Credits).PendingGrants.Count);
            Assert.AreEqual(1, ChallengeLedger.WinsToday(ChallengeGenre.Pairs));
            Assert.AreEqual(1, ChallengeLedger.LifetimeClears, "the tally still counts; a later rate pays for it");
        }

        // ------------------------------------------------------------------ the file
        [Test]
        public void TheBlockRoundTripsThroughTheFileAndAnEmptyOneWritesNothing()
        {
            var empty = new SaveFileDto();
            ChallengeLedger.WriteInto(empty);
            Assert.AreEqual(0, empty.challenges.day);
            Assert.IsEmpty(empty.challenges.today);
            Assert.IsEmpty(empty.challenges.clears);
            Assert.IsEmpty(empty.challenges.tiers);

            Fund(120);
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze"));
            ChallengeLedger.Win(ChallengeLedger.Begin(ChallengeGenre.Pairs));
            ChallengeLedger.Begin(ChallengeGenre.Pairs);

            var dto = new SaveFileDto();
            ChallengeLedger.WriteInto(dto);
            Assert.AreEqual(Day, dto.challenges.day);
            Assert.AreEqual(1, dto.challenges.today.Length);
            Assert.AreEqual("pairs", dto.challenges.today[0].genre);
            Assert.AreEqual(2, dto.challenges.today[0].attempts);
            Assert.AreEqual(1, dto.challenges.today[0].wins);
            Assert.AreEqual(1, dto.challenges.clears.Length);
            Assert.AreEqual("bronze", dto.challenges.tiers[0].id);
            Assert.AreEqual(Noon, dto.challenges.tiers[0].fromUnix);

            ChallengeLedger.ResetForTests();
            ChallengeLedger.LoadFrom(dto);
            Assert.AreEqual(3, ChallengeLedger.PlaysLeft(ChallengeGenre.Pairs), "two of the deal's five plays came back spent");
            Assert.AreEqual(1, ChallengeLedger.WinsToday(ChallengeGenre.Pairs));
            Assert.AreEqual(1, ChallengeLedger.LifetimeClears);
            Assert.AreEqual(5, ChallengeLedger.Allowance);
        }

        [Test]
        public void TheJoinTakesTheLaterDayAndTheLargerCountsAndDates()
        {
            var mine = new ChallengeStateDto
            {
                day = Day,
                today = new[] { new ChallengeDayDto { genre = "pairs", attempts = 2, wins = 1 } },
                clears = new[] { new ChallengeCountDto { genre = "pairs", count = 4 } },
                tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromUnix = Noon - 3L * DailyRules.SecondsPerDay } },
            };
            var other = new ChallengeStateDto
            {
                day = Day,
                today = new[]
                {
                    new ChallengeDayDto { genre = "pairs", attempts = 1, wins = 1 },
                    new ChallengeDayDto { genre = "merge", attempts = 1, wins = 0 },
                },
                clears = new[] { new ChallengeCountDto { genre = "pairs", count = 9 }, new ChallengeCountDto { genre = "merge", count = 1 } },
                tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromUnix = Noon } },
            };

            var joined = ChallengeLedger.Join(mine, other);
            Assert.AreEqual(Day, joined.day);
            Assert.AreEqual(2, joined.today.Length);
            Assert.AreEqual("merge", joined.today[0].genre, "sorted, so SaveDelta can walk");
            Assert.AreEqual(2, joined.today[1].attempts);
            Assert.AreEqual(1, joined.today[1].wins);
            Assert.AreEqual(9, joined.clears[1].count);
            Assert.AreEqual(1, joined.clears[0].count);
            Assert.AreEqual(Noon, joined.tiers[0].fromUnix);

            // Idempotent and order-independent.
            var again = ChallengeLedger.Join(other, mine);
            Assert.AreEqual(joined.today[1].attempts, again.today[1].attempts);
            var twice = ChallengeLedger.Join(joined, joined);
            Assert.AreEqual(joined.clears[1].count, twice.clears[1].count);

            // The later day wins outright.
            other.day = Day + 1;
            var later = ChallengeLedger.Join(mine, other);
            Assert.AreEqual(Day + 1, later.day);
            Assert.AreEqual(2, later.today.Length);
            Assert.AreEqual(1, later.today[1].attempts, "yesterday's rows do not carry over");

            // A malformed row — wins past attempts — is read as attempts raised, never as free plays.
            var odd = ChallengeLedger.Join(new ChallengeStateDto
            {
                day = Day, today = new[] { new ChallengeDayDto { genre = "pairs", attempts = 0, wins = 3 } },
            }, null);
            Assert.AreEqual(3, odd.today[0].attempts);
        }

        static bool Holds(IReadOnlyList<GrantEntry> grants, string id)
        {
            foreach (var grant in grants) if (grant.Id == id) return true;
            return false;
        }

        static bool Changed(SaveFileDto remote, SaveFileDto merged)
        {
            remote.levels = remote.levels ?? new LevelRecordDto[0];
            merged.levels = merged.levels ?? new LevelRecordDto[0];
            return SaveDelta.Between(remote, merged).ScalarsChanged;
        }

        [Test]
        public void TheDeltaSeesEveryFieldOfTheBlock()
        {
            var a = new SaveFileDto { challenges = new ChallengeStateDto { day = Day, today = new[] { new ChallengeDayDto { genre = "pairs", attempts = 1 } } } };
            var b = new SaveFileDto { challenges = new ChallengeStateDto { day = Day, today = new[] { new ChallengeDayDto { genre = "pairs", attempts = 2 } } } };
            Assert.IsTrue(Changed(a, b));

            var c = new SaveFileDto { challenges = new ChallengeStateDto { clears = new[] { new ChallengeCountDto { genre = "pairs", count = 1 } } } };
            var d = new SaveFileDto { challenges = new ChallengeStateDto { clears = new[] { new ChallengeCountDto { genre = "pairs", count = 2 } } } };
            Assert.IsTrue(Changed(c, d));

            var e = new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromUnix = 1 } } } };
            var f = new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromUnix = 2 } } } };
            Assert.IsTrue(Changed(e, f));
            Assert.IsFalse(Changed(e, new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromUnix = 1 } } } }));
        }
    }
}

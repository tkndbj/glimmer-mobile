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
                    new ChallengeTierDto { id = "bronze", gems = 120, plays = 5, days = 7 },
                    new ChallengeTierDto { id = "silver", gems = 200, plays = 10, days = 7 },
                    new ChallengeTierDto { id = "gold", gems = 500, plays = 25, days = 7 },
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

        // ------------------------------------------------------------------ the rotation
        [Test]
        public void EveryPlayerDealsTheSameLevelAndSlotNoughtWalksEveryLevelOncePerCycle()
        {
            var table = ChallengeRules.Table;

            var a = ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, 0);
            var b = ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, 0);
            Assert.AreSame(a, b, "one day, one genre, one level, on every device");

            // Over a cycle of n days the first level visits every row exactly once.
            int n = table.RowsOf(ChallengeGenre.Pairs).Count;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                var seen = new HashSet<string>();
                for (int d = 0; d < n; d++)
                    seen.Add(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, cycle * n + d, 0).Id);
                Assert.AreEqual(n, seen.Count, $"cycle {cycle} repeated a first level");
            }

            // Within a day the sequence is the ring from the day's start, so k wins see k
            // distinct levels while k < n.
            var day = ChallengeCalendar.Sequence(table, ChallengeGenre.Pairs, Day);
            Assert.AreEqual(n, new HashSet<string>(day.ConvertAll(r => r.Id)).Count);
            Assert.AreSame(day[0], ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, n), "the ring wraps");

            // Two consecutive days do not open on the same level.
            Assert.AreNotSame(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day, 0),
                              ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, Day + 1, 0));

            // A genre with one row deals it every time, and a negative day is still a day.
            Assert.AreEqual("m0", ChallengeCalendar.Slot(table, ChallengeGenre.Merge, Day, 7).Id);
            Assert.IsNotNull(ChallengeCalendar.Slot(table, ChallengeGenre.Pairs, -3, 2));
            Assert.IsNull(ChallengeCalendar.Slot(table, ChallengeGenre.Sokoban, Day, 0), "a genre with no rows deals nothing");
        }

        [Test]
        public void TheShuffleIsSeededByTheGenreAndTheCycle()
        {
            var one = ChallengeCalendar.Order(ChallengeGenre.Pairs, 30, 10);
            var same = ChallengeCalendar.Order(ChallengeGenre.Pairs, 35, 10);
            var next = ChallengeCalendar.Order(ChallengeGenre.Pairs, 40, 10);
            var other = ChallengeCalendar.Order(ChallengeGenre.Merge, 30, 10);

            CollectionAssert.AreEqual(one, same, "days 30 and 35 are the same cycle of ten");
            CollectionAssert.AreNotEqual(one, next, "day 40 opens a new cycle");
            CollectionAssert.AreNotEqual(one, other, "two genres with ten rows shuffle apart");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, one);
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
            Assert.AreEqual(6, ChallengeLedger.DaysLeft(ChallengeRules.Table.FindTier("bronze")));
        }

        // ------------------------------------------------------------------ the deals
        [Test]
        public void ADealRaisesTheAllowanceForItsWindowAndABiggerOneIsAnUpgrade()
        {
            var bronze = ChallengeRules.Table.FindTier("bronze");
            var silver = ChallengeRules.Table.FindTier("silver");

            Assert.AreEqual(TierBuy.TooPoor, ChallengeLedger.TryBuyTier(bronze));
            Fund(320);

            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(5, ChallengeLedger.Allowance);
            Assert.AreEqual(200, PlayerProgression.Gems);
            Assert.AreEqual(TierBuy.Held, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(200, PlayerProgression.Gems, "a running deal is not sold twice");

            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(silver), "a bigger deal is an upgrade");
            Assert.AreEqual(10, ChallengeLedger.Allowance);
            Assert.AreSame(silver, ChallengeLedger.HeldTier);
            Assert.AreEqual(TierBuy.Lower, ChallengeLedger.TryBuyTier(bronze), "a smaller one under it buys nothing");
            Assert.AreEqual(0, PlayerProgression.Gems);

            // The window: seven days from purchase, that day included, then the free figure.
            _clock.Now += 6L * DailyRules.SecondsPerDay;
            Assert.AreEqual(10, ChallengeLedger.Allowance);
            Assert.AreEqual(1, ChallengeLedger.DaysLeft(silver));
            _clock.Now += DailyRules.SecondsPerDay;
            Assert.AreEqual(2, ChallengeLedger.Allowance, "the eighth day is free again");
            Assert.IsNull(ChallengeLedger.HeldTier);

            // And it can be bought again for a fresh window.
            Fund(120);
            Assert.AreEqual(TierBuy.Bought, ChallengeLedger.TryBuyTier(bronze));
            Assert.AreEqual(7, ChallengeLedger.DaysLeft(bronze));
        }

        [Test]
        public void TheDealDebitIsDerivedFromTheTierAndTheDay()
        {
            Fund(120);
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze"));

            var spends = Wallet.Ledger(Currency.Gems).PendingSpends;
            Assert.AreEqual(1, spends.Count);
            Assert.AreEqual(SpendEntry.ChallengeTierId("bronze", Day), spends[0].Id);
            Assert.AreEqual(120, spends[0].Amount);

            Assert.IsTrue(SpendEntry.TryParseChallengeTierId("chaltier:bronze:20500", out string tier, out int from));
            Assert.AreEqual("bronze", tier);
            Assert.AreEqual(20500, from);
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("pass:watch_0001", out _, out _));
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:0", out _, out _));
            Assert.IsFalse(SpendEntry.TryParseChallengeTierId("chaltier:bronze:+5", out _, out _));
        }

        [Test]
        public void ARefusedDealDebitTakesTheDealBack()
        {
            Fund(120);
            ChallengeLedger.TryBuyTier(ChallengeRules.Table.FindTier("bronze"));
            Assert.AreEqual(5, ChallengeLedger.Allowance);

            ChallengeLedger.OnSpendRejected(Currency.Gems, SpendEntry.ChallengeTierId("bronze", Day - 1));
            Assert.AreEqual(5, ChallengeLedger.Allowance, "a different purchase's refusal moves nothing");

            ChallengeLedger.OnSpendRejected(Currency.Gems, SpendEntry.ChallengeTierId("bronze", Day));
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
            Assert.AreEqual(Day, dto.challenges.tiers[0].fromDay);

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
                tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromDay = Day - 3 } },
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
                tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromDay = Day } },
            };

            var joined = ChallengeLedger.Join(mine, other);
            Assert.AreEqual(Day, joined.day);
            Assert.AreEqual(2, joined.today.Length);
            Assert.AreEqual("merge", joined.today[0].genre, "sorted, so SaveDelta can walk");
            Assert.AreEqual(2, joined.today[1].attempts);
            Assert.AreEqual(1, joined.today[1].wins);
            Assert.AreEqual(9, joined.clears[1].count);
            Assert.AreEqual(1, joined.clears[0].count);
            Assert.AreEqual(Day, joined.tiers[0].fromDay);

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

            var e = new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromDay = 1 } } } };
            var f = new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromDay = 2 } } } };
            Assert.IsTrue(Changed(e, f));
            Assert.IsFalse(Changed(e, new SaveFileDto { challenges = new ChallengeStateDto { tiers = new[] { new ChallengeTierStateDto { id = "bronze", fromDay = 1 } } } }));
        }
    }
}

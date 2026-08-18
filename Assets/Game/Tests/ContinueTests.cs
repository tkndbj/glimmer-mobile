using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying time: the rewarded continue, and the four things it must not touch.
    ///
    /// <para>
    /// The feature is one method — <see cref="RunClock.Extend"/> — and almost all of its risk
    /// is in what it leaves alone. It raises the <em>limit</em> and never lowers the
    /// <em>elapsed</em>, so most of what this file proves is that a run somebody bought their
    /// way through still reports the time it really took: <c>bestMillis</c> keeps its meaning,
    /// <c>StarsForTime</c> keeps grading against par, and <c>publishGroveStats</c> keeps
    /// ranking a population that was never told any of this happened.
    /// </para>
    /// <para>
    /// Rewinding the elapsed instead would have been the same number of lines and would have
    /// corrupted all three at once, silently, because both readings are milliseconds and both
    /// look perfectly plausible in a save file. That is <c>CountdownTests</c>' warning applied
    /// to the first change since that could have caused it.
    /// </para>
    /// </summary>
    public sealed class ContinueTests
    {
        /// <summary>Par 30 at the shipped 2s/turn: a 60 second glade, gold at 30, silver at 45.</summary>
        static LevelTuning Timed(int par = 30)
            => new LevelTuning(par, LevelTuning.DefaultGoldFactor, LevelTuning.DefaultSilverFactor,
                               LevelTuning.DefaultHintAllowance, 0f, 0f);

        /// <summary>A clock run out on a 60 second glade, exactly as a timeout leaves it.</summary>
        static RunClock Expired()
        {
            var clock = new RunClock();
            clock.Reset(60_000);
            clock.Start();

            // Advanced a tick at a time, because Advance clamps every one of them — handing it
            // sixty seconds in a single call is the mistake the clamp exists to catch, and it
            // would leave this clock reading a quarter of a second.
            for (int i = 0; i < 400; i++) clock.Advance(RunClock.MaxTick);

            Assert.IsTrue(clock.Expired, "the fixture should start from a run that has timed out");
            return clock;
        }

        // ---------------------------------------------------------------- the grant
        [Test]
        public void ExtendingGivesTheRunBackAndTheClockRunsAgain()
        {
            var clock = Expired();

            Assert.IsTrue(clock.Extend(30_000));

            Assert.IsFalse(clock.Expired, "the run should be playable again");
            Assert.AreEqual(90_000, clock.LimitMillis);
            Assert.AreEqual(30_000, clock.RemainingMillis);
        }

        /// <summary>
        /// The whole design in one assertion. Elapsed is what the save file stores and what
        /// every population statistic is built from, so an extension that moved it would put a
        /// time nobody played into a permanent record — and a best time only ever falls, so it
        /// would stick there.
        /// </summary>
        [Test]
        public void ExtendingRaisesTheLimitAndNeverRewindsTheElapsed()
        {
            var clock = Expired();
            int before = clock.Millis;

            clock.Extend(30_000);

            Assert.AreEqual(before, clock.Millis, "elapsed play time is not the player's to buy");
        }

        /// <summary>
        /// A continued run keeps losing stars, with no rule anywhere that says so.
        ///
        /// The thresholds come from par rather than from the clock's limit, so every second
        /// bought pushes the run further down the time bands. That is what stops the offer
        /// being a way to buy a three-star clear, and it needs no separate enforcement — which
        /// is the only reason the continue could be made repeatable without an economy
        /// argument attached to it.
        /// </summary>
        [Test]
        public void AContinuedRunCannotBuyBackTheStarsItsTimeCost()
        {
            var tuning = Timed();
            var clock = Expired();

            // The full sixty seconds are gone, which is already past silver at forty-five.
            Assert.AreEqual(1, tuning.StarsForTime(clock.Millis));

            clock.Extend(30_000);
            for (int i = 0; i < 100; i++) clock.Advance(RunClock.MaxTick);   // twenty-five more

            Assert.AreEqual(1, tuning.StarsForTime(clock.Millis));
            Assert.AreEqual(1, tuning.StarsFor(1, clock.Millis),
                            "a perfect move count cannot rescue a bought clock");
        }

        [Test]
        public void ExtensionsAccumulateAndAreReportedSeparately()
        {
            var clock = Expired();

            clock.Extend(30_000);
            clock.Extend(30_000);
            clock.Extend(30_000);

            Assert.AreEqual(150_000, clock.LimitMillis);
            Assert.AreEqual(90_000, clock.ExtendedMillis);
            Assert.IsTrue(clock.WasExtended);
        }

        // ------------------------------------------------------------- the refusals
        [Test]
        public void AnUntimedGladeHasNoClockToExtend()
        {
            var clock = new RunClock();
            clock.Reset(0);
            clock.Start();

            Assert.IsFalse(clock.Extend(30_000));
            Assert.AreEqual(0, clock.LimitMillis, "an untimed glade must not grow a limit");
        }

        /// <summary>
        /// Nothing has been spent yet, so there is nothing to buy back — and a clock loaded
        /// before the board is playable would be a strictly better glade, bought in advance.
        /// </summary>
        [Test]
        public void AClockThatHasNotStartedRefusesTheGrant()
        {
            var clock = new RunClock();
            clock.Reset(60_000);

            Assert.IsFalse(clock.Extend(30_000));
            Assert.AreEqual(60_000, clock.LimitMillis);
        }

        [Test]
        public void AResolvedRunRefusesTheGrant()
        {
            var clock = Expired();
            clock.Stop();

            Assert.IsFalse(clock.Extend(30_000));
        }

        [Test]
        public void NonPositiveGrantsAreRefused()
        {
            var clock = Expired();

            Assert.IsFalse(clock.Extend(0));
            Assert.IsFalse(clock.Extend(-30_000));
            Assert.AreEqual(60_000, clock.LimitMillis);
        }

        /// <summary>
        /// The limit is an int and every extension adds to it. Wrapping would read as a run
        /// that expired the instant it was rescued, which is the single most confusing thing
        /// this feature could do to somebody who had just watched a video for it.
        /// </summary>
        [Test]
        public void TheLimitCannotBeRaisedPastWhatAnIntCanHold()
        {
            var clock = new RunClock();
            clock.Reset(RunClock.MaxLimitMillis - 1_000);
            clock.Start();
            clock.Advance(RunClock.MaxTick);

            Assert.IsTrue(clock.Extend(500));
            Assert.IsFalse(clock.Extend(30_000), "past the ceiling the grant is refused, not wrapped");
            Assert.Greater(clock.LimitMillis, 0);
        }

        /// <summary>A fresh board inherits neither the previous run's time nor its extensions.</summary>
        [Test]
        public void ResettingForgetsWhatThePreviousRunBought()
        {
            var clock = Expired();
            clock.Extend(30_000);

            clock.Reset(60_000);

            Assert.AreEqual(0, clock.ExtendedMillis);
            Assert.IsFalse(clock.WasExtended);
            Assert.AreEqual(60_000, clock.LimitMillis);
        }

        // -------------------------------------------------------- the reward's shape
        /// <summary>
        /// Run time is not currency, so nothing about it is adjudicated: no account is needed,
        /// no claim is written, and the network's signed callback grants nothing for it. That
        /// is what makes the continue work on a first launch that has never been online, which
        /// is the launch where a player is most likely to meet a timeout.
        /// </summary>
        [Test]
        public void RunTimeIsNeitherCurrencyNorBankable()
        {
            Assert.IsFalse(ChestDropKinds.IsCurrency(ChestDropKind.RunTime));
            Assert.IsTrue(ChestDropKinds.IsTransient(ChestDropKind.RunTime));
            Assert.AreEqual(string.Empty, ChestDropKinds.CurrencyOf(ChestDropKind.RunTime));
        }

        [Test]
        public void EveryOtherRewardKindIsBankable()
        {
            Assert.IsFalse(ChestDropKinds.IsTransient(ChestDropKind.Credits));
            Assert.IsFalse(ChestDropKinds.IsTransient(ChestDropKind.Gems));
            Assert.IsFalse(ChestDropKinds.IsTransient(ChestDropKind.Hearts));
            Assert.IsFalse(ChestDropKinds.IsTransient(ChestDropKind.HeartBoost));
        }

        /// <summary>The id is data, named by a published content file, and therefore frozen.</summary>
        [Test]
        public void TheRunTimeIdRoundTripsThroughContent()
        {
            Assert.AreEqual(ChestDropKind.RunTime, ChestDropKinds.Parse("run_time"));
            Assert.AreEqual("run_time", ChestDropKinds.Id(ChestDropKind.RunTime));
        }

        // --------------------------------------------------------- the content rules
        [Test]
        public void TheShippedTableCarriesBothNewPlacements()
        {
            var table = AdRewardTable.Default;

            var cont = table.Offer(AdPlacement.RunContinue);
            Assert.IsTrue(cont.IsValid);
            Assert.AreEqual(ChestDropKind.RunTime, cont.Kind);
            Assert.IsFalse(cont.IsCurrency);

            var bonus = table.Offer(AdPlacement.WinBonus);
            Assert.IsTrue(bonus.IsValid);
            Assert.AreEqual(ChestDropKind.Credits, bonus.Kind);
            Assert.IsTrue(bonus.IsCurrency, "the victory bonus pays money, so the server grants it");
        }

        [Test]
        public void BothNewPlacementsAreKnownIds()
        {
            Assert.IsTrue(AdPlacement.IsKnown(AdPlacement.RunContinue));
            Assert.IsTrue(AdPlacement.IsKnown(AdPlacement.WinBonus));
        }

        /// <summary>
        /// A transient reward on any other placement would be offered where no run is open:
        /// the video plays, and the reward lands on nothing. Refused when the table is read
        /// rather than discovered by a player.
        ///
        /// <para>
        /// A second, valid placement is present on purpose. Without it every entry is rejected
        /// and <c>Resolve</c> falls back to the built-in table — correct, and the reason the
        /// first version of this test passed for the wrong reason: it was reading
        /// <c>coin_bonus</c> off the shipped defaults rather than off the table under test.
        /// </para>
        /// </summary>
        [Test]
        public void OnlyTheContinueMayPayRunTime()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                cooldownSeconds = 45,
                placements = new[]
                {
                    new AdPlacementDto
                    {
                        id = AdPlacement.CoinBonus, kind = "run_time", amount = 30, dailyCap = 6,
                    },
                    new AdPlacementDto
                    {
                        id = AdPlacement.HeartRefill, kind = "hearts", amount = 2, dailyCap = 10,
                    },
                },
            }, problems);

            Assert.IsTrue(table.Has(AdPlacement.HeartRefill), "the good entry should survive");
            Assert.IsFalse(table.Has(AdPlacement.CoinBonus), "the bad entry should be dropped");
            Assert.IsTrue(problems.Count > 0, "the refusal has to be named, never silent");
        }

        [Test]
        public void TheContinueMayPayRunTime()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                cooldownSeconds = 45,
                placements = new[]
                {
                    new AdPlacementDto
                    {
                        id = AdPlacement.RunContinue, kind = "run_time", amount = 20, dailyCap = 4,
                    },
                },
            }, problems);

            var offer = table.Offer(AdPlacement.RunContinue);
            Assert.IsTrue(offer.IsValid);
            Assert.AreEqual(20, offer.Amount);
            Assert.AreEqual(4, offer.DailyCap);
        }

        /// <summary>
        /// A chest is opened on the home screen, where there is no run to extend, and a
        /// guaranteed slot rolling one would pay nothing reliably.
        /// </summary>
        [Test]
        public void AChestMayNotPayRunTime()
        {
            var problems = new List<string>();

            var table = DailyChestTable.Resolve(new DailyChestDto
            {
                runsPerChest = -1,
                chests = new[]
                {
                    new DailyChestEntryDto
                    {
                        guaranteed = new[] { new DailyDropDto { kind = "run_time", min = 30, max = 30 } },
                        options = null,
                    },
                },
            }, problems);

            Assert.IsTrue(problems.Count > 0, "the refusal has to be named, never silent");

            // The built-in table stands, because a content mistake fails a build and never a
            // session — the rule the chest reader has always followed.
            Assert.IsNotNull(table);
        }
    }
}

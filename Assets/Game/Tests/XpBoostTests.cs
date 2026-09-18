using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The XP boost: two windows, one multiplier, and the clamp that makes paying for it safe.
    ///
    /// <para>
    /// <b>A boost on a derived number is the awkward case, and every test here is about that.</b>
    /// XP is recomputed from the star ledger each time it is read (invariant 9), so there is no
    /// running total for a multiplier to scale — scaling the derived figure while a window was
    /// open would make a player's level <em>fall</em> when it closed. So the bonus is worked out
    /// when it is earned and banked, and what keeps a banked figure honest is that it is clamped
    /// to a share of XP the account can prove.
    /// </para>
    /// <para>
    /// The clamp is the half that also exists on the server (<c>xpBoostXp</c> in
    /// <c>functions/src/grove.ts</c>), so it runs from <c>firebase/shared/grove-vectors.json</c>
    /// and <c>firebase/functions/test/grove.mjs</c> is the other half. A drift between them is
    /// silent: the published card is valid and its keeper level merely lower, and invariant 19a
    /// <em>drops</em> what that level gated rather than clamping it.
    /// </para>
    /// <para>
    /// Everything else here is client-only by nature — no server ever offers a boost — and is
    /// pinned because it is where a window could be double-paid, silently metered wrong, or
    /// granted on the track that breaks the derived cooldown.
    /// </para>
    /// </summary>
    public sealed class XpBoostTests
    {
        // ------------------------------------------------------------- the vectors
        sealed class ClampCase
        {
            public string Name;
            public long Stored;
            public long Provable;
            public int MaxPercent;
            public long Bonus;

            public static ClampCase From(Dictionary<string, object> map) => new ClampCase
            {
                Name = TestJson.Str(map, "name", "(unnamed)"),
                Stored = TestJson.Long(map, "stored"),
                Provable = TestJson.Long(map, "provable"),
                MaxPercent = TestJson.Int(map, "maxPercent"),
                Bonus = TestJson.Long(map, "bonus"),
            };
        }

        static List<ClampCase> ClampCases()
        {
            var file = TestJson.ReadShared("grove-vectors.json");
            var cases = new List<ClampCase>();

            foreach (object raw in TestJson.Children(file, "xpBoostCases"))
                cases.Add(ClampCase.From(TestJson.Object(raw)));

            Assert.Greater(cases.Count, 0, "the vector file has no xp boost cases");
            return cases;
        }

        /// <summary>
        /// Publishes a whole reward table built around the block under test.
        ///
        /// <b>A real <see cref="ProgressionTable"/> through the shipped reader</b>, rather than an
        /// <see cref="XpBoostTable"/> poked in directly: the block is resolved by the same code a
        /// content push runs, and a table assembled by the test would prove nothing about the one
        /// the game builds. <c>GroveBoardTests</c>'s rule about reading the shared catalog with
        /// the shipped mapper, said one file over.
        /// </summary>
        static void Publish(int maxPercent, int watchedPercent = 50, int watchedHours = 2,
                            int cooldownHours = 4, int boughtPercent = 100, int boughtHours = 24)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                xpBoost = new XpBoostDto
                {
                    watchedPercent = watchedPercent,
                    watchedHours = watchedHours,
                    watchedCooldownHours = cooldownHours,
                    boughtPercent = boughtPercent,
                    boughtHours = boughtHours,
                    maxPercent = maxPercent,
                },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        [SetUp]
        public void Reset()
        {
            ProgressionRules.Reset();
            Wallet.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void Restore()
        {
            ProgressionRules.Reset();
            Wallet.LoadFrom(new SaveFileDto());
        }

        // ------------------------------------------------------- the shared contract
        /// <summary>
        /// <b>The clamp, against the server's own cases.</b> Driven through
        /// <see cref="XpBoost.BonusOn"/> rather than <see cref="XpBoost.BonusFrom"/>, because the
        /// latter reads the live wallet and a vector supplies its own stored figure — what is
        /// under contract is the arithmetic, and the wallet read is exercised below.
        /// </summary>
        [Test]
        public void EveryVectorCaseClampsTheWayTheServerClamps()
        {
            foreach (var c in ClampCases())
            {
                long ceiling = XpBoost.BonusOn(c.Provable, c.MaxPercent);
                long paid = c.Stored <= 0L || c.Provable <= 0L || c.MaxPercent <= 0
                    ? 0L
                    : (c.Stored < ceiling ? c.Stored : ceiling);

                if (paid > XpBoostLimits.HardMaxBonusXp) paid = XpBoostLimits.HardMaxBonusXp;

                Assert.AreEqual(c.Bonus, paid, $"the clamp disagrees with the server: {c.Name}");
            }
        }

        /// <summary>
        /// And the same cases through the shipped accessor, which is what actually runs. Proves
        /// the wallet read, the table read and the clamp compose to the vector's answer.
        /// </summary>
        [Test]
        public void TheShippedAccessorPaysWhatTheVectorsSay()
        {
            foreach (var c in ClampCases())
            {
                // **Both windows zeroed on purpose.** The clamp reads `MaxPercent` and nothing
                // else, and a table whose windows pay more than its cap has that cap *raised* to
                // them by the reader — correctly, but it would quietly replace the figure the
                // vector is about. Zeroing them isolates the one number under test.
                Wallet.LoadFrom(SaveWith(earned: c.Stored));
                Publish(c.MaxPercent, watchedPercent: 0, boughtPercent: 0);

                Assert.AreEqual(c.Bonus, XpBoost.BonusFrom(c.Provable),
                                $"the shipped accessor disagrees: {c.Name}");
            }
        }

        [Test]
        public void TheBuiltInCapIsTheOneTheServerFallsBackTo()
        {
            var file = TestJson.ReadShared("grove-vectors.json");
            var defaults = TestJson.Child(file, "xpBoostDefaults");

            Assert.AreEqual(TestJson.Int(defaults, "maxPercent"), XpBoostLimits.DefaultMaxPercent);
            Assert.AreEqual(XpBoostLimits.DefaultMaxPercent, XpBoostTable.Default.MaxPercent);

            // Mirrored by `HARD_MAX_BOOST_XP` in functions/src/grove.ts, and asserted there.
            Assert.AreEqual(1000000000L, XpBoostLimits.HardMaxBonusXp);
        }

        // --------------------------------------------------------------- the clamp
        /// <summary>
        /// <b>The proportional bound is the whole defence, so it gets its own case.</b> A stored
        /// figure the account cannot have earned is cut to what it could have — which is what
        /// makes a number no server can recompute safe to pay for at all (invariant 13's fourth
        /// clause).
        /// </summary>
        [Test]
        public void AForgedBonusIsClampedToAShareOfProvableXp()
        {
            Wallet.LoadFrom(SaveWith(earned: long.MaxValue / 4));
            Publish(150);

            // 1,000 XP honestly earned can have carried at most 1,500 of bonus at +150%.
            Assert.AreEqual(1500L, XpBoost.BonusFrom(1000L));

            // And an account with nothing to show for it gets nothing, however large the figure.
            Assert.AreEqual(0L, XpBoost.BonusFrom(0L));
        }

        [Test]
        public void AnHonestBonusIsNeverClamped()
        {
            Publish(150);

            // The most an honest player can bank is exactly the share, so the clamp must not
            // bite on it — a bound that trims real earnings is a bug, not a defence.
            Wallet.LoadFrom(SaveWith(earned: 1500L));
            Assert.AreEqual(1500L, XpBoost.BonusFrom(1000L));
        }

        // -------------------------------------------------------------- the windows
        [Test]
        public void NoWindowPaysNothing()
        {
            Publish(150);

            Assert.AreEqual(0, XpBoost.Percent);
            Assert.IsFalse(XpBoost.Active);
            Assert.AreEqual(0L, XpBoost.Bank(1000L));
        }

        [Test]
        public void TheTwoWindowsAddRatherThanTheLargerWinning()
        {
            Publish(150);

            XpBoost.GrantWatched();
            Assert.AreEqual(50, XpBoost.Percent);

            XpBoost.GrantBought(24);
            Assert.AreEqual(150, XpBoost.Percent,
                            "watching during a bought window has to be worth taking, or the " +
                            "offer is a trap that has to be hidden");
        }

        [Test]
        public void TheSumIsCappedByTheTable()
        {
            // A cap under what both windows pay together is the case the cap exists for.
            Publish(120);

            XpBoost.GrantWatched();
            XpBoost.GrantBought(24);

            Assert.AreEqual(120, XpBoost.Percent);
        }

        [Test]
        public void AWindowExtendsRatherThanBeingReplaced()
        {
            Publish(150);

            XpBoost.GrantBought(2);
            long first = XpBoost.BoughtUntilUnix;

            XpBoost.GrantBought(2);

            Assert.Greater(XpBoost.BoughtUntilUnix, first,
                           "a window won while one runs must not take time away from somebody " +
                           "for doing well twice");
        }

        // ------------------------------------------------------------- the clock
        /// <summary>
        /// What the map's readout hangs on: <c>BoostReadout</c> shows itself when this is above
        /// nought and takes itself off screen when it reaches it, so the two states have to be
        /// exactly "a boost is running" and "none is".
        /// </summary>
        [Test]
        public void TheClockIsTheLaterOfTheTwoWindowsAndNoughtWhenNeitherRuns()
        {
            Publish(150);

            Assert.AreEqual(0L, XpBoost.SecondsLeft, "nothing is running, so there is no clock");

            XpBoost.GrantWatched();                       // 2h
            long watched = XpBoost.SecondsLeft;
            Assert.Greater(watched, 0L);

            XpBoost.GrantBought(24);                      // 24h, which outlasts it

            // **The later deadline, not the next change.** A readout that vanished at the first
            // expiry while a boost was still running is the stale-readout fault 44j is about.
            Assert.Greater(XpBoost.SecondsLeft, watched);
        }

        /// <summary>
        /// A withdrawn boost stops the clock even with a deadline still stored, so the readout
        /// does not sit on the map counting down something that pays nothing.
        /// </summary>
        [Test]
        public void AWithdrawnBoostHasNoClockEvenWithAWindowStillOpen()
        {
            Publish(150);
            XpBoost.GrantBought(24);
            Assert.Greater(XpBoost.SecondsLeft, 0L);

            // The same save, read against a table that pays nothing.
            Publish(0, watchedPercent: 0, boughtPercent: 0);

            Assert.AreEqual(0, XpBoost.Percent);
            Assert.AreEqual(0L, XpBoost.SecondsLeft);
        }

        // ------------------------------------------------------------- the cooldown
        /// <summary>
        /// <b>The cooldown is derived from the watched deadline, not stored</b> (invariant 48c's
        /// trick), so this is really a test that one number can carry two facts without them
        /// disagreeing.
        /// </summary>
        [Test]
        public void TheCooldownIsDerivedFromTheWatchedWindowsOwnDeadline()
        {
            Publish(150, watchedHours: 2, cooldownHours: 4);

            Assert.IsTrue(XpBoost.WatchedReady, "an account that has never watched is ready");

            XpBoost.GrantWatched();

            Assert.IsFalse(XpBoost.WatchedReady, "the cooldown starts with the window");

            // The window is 2h and the cooldown 4h, so another is due 2h after this one closes.
            long expected = XpBoost.WatchedUntilUnix - 2 * 3600L + 4 * 3600L;
            Assert.AreEqual(expected, XpBoost.WatchedReadyAt);
            Assert.AreEqual(2 * 3600L, XpBoost.WatchedReadyAt - XpBoost.WatchedUntilUnix);
        }

        /// <summary>
        /// <b>A gift must land on the bought track.</b> The watched deadline carries the cooldown,
        /// so anything else writing it would move a cooldown it knows nothing about — a chest
        /// would silently postpone the player's next free window.
        /// </summary>
        [Test]
        public void AGiftDoesNotMoveTheWatchedCooldown()
        {
            Publish(150);

            XpBoost.GrantWatched();
            long readyAt = XpBoost.WatchedReadyAt;

            Assert.IsTrue(BankedDrop.Apply(new ChestDrop(ChestDropKind.XpBoost, 24)),
                          "a chest paying an XP boost has to be applied by the shared switch");

            Assert.AreEqual(readyAt, XpBoost.WatchedReadyAt,
                            "a gift moved the cooldown on the free window");
            Assert.Greater(XpBoost.BoughtUntilUnix, 0L, "the gift landed on neither track");
        }

        // ---------------------------------------------------------------- the seam
        /// <summary>
        /// <b>The seam.</b> One multiplier, applied once, banking as it computes — because there
        /// is no running XP total to scale later.
        /// </summary>
        [Test]
        public void BankingPaysThePercentageAndRemembersIt()
        {
            Publish(150);
            XpBoost.GrantBought(24);

            Assert.AreEqual(100, XpBoost.Percent);
            Assert.AreEqual(100L, XpBoost.Bank(100L), "a 100% window doubles a 100 XP payment");
            Assert.AreEqual(100L, Wallet.XpBoostEarned, "the bonus has to be remembered");

            Assert.AreEqual(50L, XpBoost.Bank(50L));
            Assert.AreEqual(150L, Wallet.XpBoostEarned, "the total accumulates rather than resets");
        }

        [Test]
        public void TheBankedTotalOnlyEverRises()
        {
            Publish(150);
            XpBoost.GrantBought(24);
            XpBoost.Bank(1000L);

            long held = Wallet.XpBoostEarned;

            // A device handed a larger total by a merge must not push its own smaller one back.
            Assert.IsFalse(Wallet.RaiseXpBoostEarned(held - 1));
            Assert.AreEqual(held, Wallet.XpBoostEarned);
        }

        [Test]
        public void TheArithmeticIsIntegerAndTruncates()
        {
            // 333 at +150% is 499.5. A float would disagree between .NET, Mono and IL2CPP, which
            // is why nothing that decides a payment here may be one.
            Assert.AreEqual(499L, XpBoost.BonusOn(333L, 150));
            Assert.AreEqual(0L, XpBoost.BonusOn(1L, 50));
            Assert.AreEqual(0L, XpBoost.BonusOn(0L, 150));
            Assert.AreEqual(0L, XpBoost.BonusOn(100L, 0));
            Assert.AreEqual(0L, XpBoost.BonusOn(-100L, 150));
        }

        // ---------------------------------------------------------------- the reader
        [Test]
        public void AnAbsentBlockKeepsTheBuiltInFiguresAndIsNotAnError()
        {
            var problems = new List<string>();
            var table = XpBoostTable.Resolve(null, problems);

            Assert.AreEqual(XpBoostTable.Default.WatchedPercent, table.WatchedPercent);
            Assert.AreEqual(XpBoostTable.Default.MaxPercent, table.MaxPercent);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AnUnwrittenFieldInheritsRatherThanZeroing()
        {
            var problems = new List<string>();

            // -1 is what `JsonUtility` leaves on a field the file never wrote. Nought is a
            // decision here — it withdraws a window — so the two must not be the same fact.
            // Under the inherited cap of 150, so the reader has nothing to repair and the case
            // is about inheritance alone. A window *over* the cap is raised and reported, which
            // is `ACapUnderASingleWindowIsRaisedToItAndReported` below.
            var table = XpBoostTable.Resolve(
                new XpBoostDto { watchedPercent = -1, boughtPercent = 120 }, problems);

            Assert.AreEqual(XpBoostLimits.DefaultWatchedPercent, table.WatchedPercent);
            Assert.AreEqual(120, table.BoughtPercent);
            Assert.AreEqual(XpBoostLimits.DefaultMaxPercent, table.MaxPercent);
            Assert.IsEmpty(problems);
        }

        [Test]
        public void ACapUnderASingleWindowIsRaisedToItAndReported()
        {
            var problems = new List<string>();
            var table = XpBoostTable.Resolve(
                new XpBoostDto { watchedPercent = 50, boughtPercent = 100, maxPercent = 60 },
                problems);

            Assert.AreEqual(100, table.MaxPercent,
                            "a window capped under its own figure is one a player is shown and " +
                            "never given");
            Assert.AreEqual(1, problems.Count, "the repair has to be reported, not silent");
        }

        [Test]
        public void AWithdrawnBoostPaysNothingAndIsNotAnError()
        {
            var problems = new List<string>();
            var table = XpBoostTable.Resolve(
                new XpBoostDto { watchedPercent = 0, boughtPercent = 0, maxPercent = 0 }, problems);

            Assert.IsFalse(table.Pays);
            Assert.IsEmpty(problems, "withdrawing a boost is a decision, not a mistake");
        }

        // --------------------------------------------------------------- the round trip
        /// <summary>
        /// All three numbers have to survive being written, merged against nothing and read back,
        /// or a boost is lost on every launch.
        /// </summary>
        [Test]
        public void EveryFieldSurvivesTheRoundTripThroughASaveFile()
        {
            Publish(150);

            XpBoost.GrantWatched();
            XpBoost.GrantBought(24);
            XpBoost.Bank(400L);

            long watched = XpBoost.WatchedUntilUnix;
            long bought = XpBoost.BoughtUntilUnix;
            long earned = Wallet.XpBoostEarned;

            Assert.Greater(earned, 0L, "the case proves nothing if nothing was banked");

            var dto = new SaveFileDto();
            Wallet.WriteInto(dto);
            Wallet.LoadFrom(dto);

            Assert.AreEqual(watched, XpBoost.WatchedUntilUnix);
            Assert.AreEqual(bought, XpBoost.BoughtUntilUnix);
            Assert.AreEqual(earned, Wallet.XpBoostEarned);
        }

        static SaveFileDto SaveWith(long earned) => new SaveFileDto
        {
            wallet = new WalletDto { xpBoostEarned = earned },
        };
    }
}

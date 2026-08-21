using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The hint pool: an account-wide resource on a clock, replacing an allowance that was
    /// handed back at every board.
    ///
    /// <para>
    /// The arithmetic is <see cref="RegenLedger"/>'s and is already pinned by
    /// <c>HeartsTests</c> — which is the point of there being one copy of it. What is tested
    /// here is what is <em>different</em> about hints: the published numbers, the fact that
    /// nothing shortens their clock, the migration from a file that never stored them, and
    /// the one property hearts do not have — a ceiling equal to the cap, which makes a grant
    /// at a full pool a refusal rather than a clamp.
    /// </para>
    /// </summary>
    public sealed class HintsTests
    {
        static readonly long P = HintRules.RefillSeconds;
        const long T0 = 1_700_000_000;      // an arbitrary fixed "now"

        // ------------------------------------------------------------- the clock
        [Test]
        public void AFullPoolRunsNoTimer()
        {
            var hints = Hints.Full;

            Assert.AreEqual(HintRules.RefillCap, hints.Count);
            Assert.AreEqual(0, hints.NextRefillUnix, "a full player has no deadline to show");
            Assert.AreEqual(0, hints.SecondsToNext(T0));
        }

        [Test]
        public void SpendingFromFullStartsTheClock()
        {
            var hints = Hints.Full.Spend(1, T0);

            Assert.AreEqual(HintRules.RefillCap - 1, hints.Count);
            Assert.AreEqual(T0 + P, hints.NextRefillUnix);
            Assert.AreEqual(P, hints.SecondsToNext(T0));
        }

        /// <summary>
        /// Spending a second must not push the pending one away, or a player who uses two
        /// hints on one hard glade silently loses the wait they had already half-served.
        /// </summary>
        [Test]
        public void SpendingAgainDoesNotPushTheRefillBack()
        {
            var first = Hints.Full.Spend(1, T0);
            var second = first.Spend(1, T0 + 60);

            Assert.AreEqual(HintRules.RefillCap - 2, second.Count);
            Assert.AreEqual(first.NextRefillUnix, second.NextRefillUnix);
        }

        [Test]
        public void OnePeriodReturnsExactlyOneHint()
        {
            var spent = Hints.Full.Spend(2, T0);

            Assert.AreEqual(HintRules.RefillCap - 2, spent.At(T0 + P - 1).Count);
            Assert.AreEqual(HintRules.RefillCap - 1, spent.At(T0 + P).Count);
            Assert.AreEqual(HintRules.RefillCap - 1, spent.At(T0 + P + 1).Count,
                            "the second one is not due until a second period has passed");
        }

        /// <summary>
        /// The case an eight-hour timer is really for: the app is closed and reopened a week
        /// later. Nothing runs in between, so the whole catch-up happens on one read — and
        /// it must stop at the cap rather than paying out a week of hints.
        /// </summary>
        [Test]
        public void AWeekAwayRefillsToTheCapAndNoFurther()
        {
            var empty = Hints.Full.Spend(HintRules.RefillCap, T0);
            Assert.AreEqual(0, empty.Count);

            var back = empty.At(T0 + 7 * 24 * 3600);

            Assert.AreEqual(HintRules.RefillCap, back.Count);
            Assert.AreEqual(0, back.NextRefillUnix, "a full pool shows no countdown");
        }

        /// <summary>
        /// Closing the app halfway through a wait neither loses the remainder nor pays it
        /// twice. The failure this pins is drift, which is invisible in a single read and
        /// obvious after a fortnight of them.
        /// </summary>
        [Test]
        public void RefillsDoNotDriftAcrossManyReads()
        {
            long end = T0 + P * HintRules.RefillCap;

            var walked = Hints.Full.Spend(HintRules.RefillCap, T0);
            var jumped = walked;

            for (long t = T0; t < end; t += P / 7) walked = walked.At(t);

            // Both sides finish at the same instant. Without this the walk stops at whatever
            // multiple of P/7 falls short of the end, and the test measures its own step
            // arithmetic rather than the ledger.
            walked = walked.At(end);
            jumped = jumped.At(end);

            Assert.AreEqual(jumped.Count, walked.Count);
            Assert.AreEqual(jumped.DueUnix, walked.DueUnix);
        }

        /// <summary>
        /// Nothing shortens a hint's clock. The heart boost is named for hearts, is sold and
        /// dropped as such, and quietly speeding up a second resource with it would make one
        /// published number mean two things.
        /// </summary>
        [Test]
        public void NoBoostShortensTheHintClock()
        {
            var spent = Hints.Full.Spend(1, T0);

            Assert.AreEqual(T0 + HintRules.RefillSeconds, spent.NextRefillUnix);
            Assert.AreNotEqual(HeartRules.BoostedRefillSeconds, 0);
        }

        // ------------------------------------------------------------- the pool
        [Test]
        public void AnEmptyPoolCannotBeSpent()
        {
            var empty = Hints.Full.Spend(HintRules.RefillCap, T0);

            Assert.IsTrue(empty.IsEmpty);
            Assert.IsFalse(empty.CanSpend);
            Assert.AreEqual(0, empty.Spend(1, T0).Count, "spending nothing takes nothing");
        }

        [Test]
        public void SpendingMoreThanIsHeldTakesOnlyWhatIsThere()
        {
            var pool = Hints.Full.Spend(HintRules.RefillCap + 5, T0);

            Assert.AreEqual(0, pool.Count);
            Assert.AreEqual(HintRules.RefillCap, pool.Spent, "and never more than was produced");
        }

        /// <summary>
        /// The property that separates hints from hearts, and the reason
        /// <c>RewardedAds.WouldBenefit</c> has a second branch: hearts stack well past their
        /// refill cap, hints have no headroom at all. A grant into a full pool is refused
        /// outright, so an offer made there would be thirty seconds of somebody's life in
        /// exchange for nothing.
        /// </summary>
        [Test]
        public void AGrantIntoAFullPoolIsRefusedRatherThanClamped()
        {
            var full = Hints.Full;
            Assert.IsTrue(full.IsAtCeiling, "the shipped ceiling is the refill cap");

            var after = full.Grant(1, T0);

            Assert.AreEqual(full.Count, after.Count);
            Assert.AreEqual(full.Produced, after.Produced, "nothing was produced, so nothing is owed");
        }

        [Test]
        public void AGrantIntoAPartPoolLandsWithoutTouchingTheClock()
        {
            var spent = Hints.Full.Spend(2, T0);
            long due = spent.DueUnix;

            var after = spent.Grant(1, T0 + 60);

            Assert.AreEqual(HintRules.RefillCap - 1, after.Count);
            Assert.AreEqual(due, after.DueUnix, "a granted hint is not a waited-for one");
        }

        /// <summary>
        /// A grant is clamped to the room available rather than refused wholesale, so a
        /// table published with a larger payout than the pool can hold pays what it can.
        /// </summary>
        [Test]
        public void AGrantLargerThanTheRoomFillsTheRoom()
        {
            var one = Hints.Full.Spend(1, T0);

            Assert.AreEqual(HintRules.RefillCap, one.Grant(50, T0).Count);
        }

        // ------------------------------------------------------------- the merge
        /// <summary>
        /// The property the whole representation exists for. One device spends, the other
        /// waits out a refill, and the join keeps both facts — where a stored count would
        /// have to guess which side was stale and would be wrong either way.
        /// </summary>
        [Test]
        public void TheJoinKeepsBothASpendAndARefill()
        {
            var start = Hints.Full.Spend(HintRules.RefillCap, T0);   // empty on both

            var phone = start.Spend(1, T0 + 10);                     // nothing left to take
            var tablet = start.At(T0 + P);                           // waited one out

            var merged = Hints.Join(phone, tablet);

            Assert.AreEqual(1, merged.Count);
            Assert.AreEqual(HintRules.RefillCap, merged.Spent, "the spend survives");
            Assert.AreEqual(HintRules.RefillCap + 1, merged.Produced, "and so does the refill");
        }

        [Test]
        public void TheJoinIsIdempotentAndOrderIndependent()
        {
            var a = Hints.Full.Spend(2, T0).At(T0 + P);
            var b = Hints.Full.Spend(1, T0 + 100);

            var ab = Hints.Join(a, b);
            var ba = Hints.Join(b, a);

            Assert.AreEqual(ab, ba);
            Assert.AreEqual(ab, Hints.Join(ab, ab));
            Assert.AreEqual(ab, Hints.Join(ab, a));
        }

        /// <summary>
        /// Two devices cannot refill each other. Every field is a counter of something that
        /// happened, so the larger is always the one that knows more — there is nothing here
        /// for a stale snapshot to mint.
        /// </summary>
        [Test]
        public void TwoDevicesCannotMintHintsByMerging()
        {
            var start = Hints.Full.Spend(2, T0);

            var phone = start;
            var tablet = start;

            for (int i = 0; i < 20; i++)
            {
                phone = Hints.Join(phone, tablet);
                tablet = Hints.Join(tablet, phone);
            }

            Assert.AreEqual(start.Count, phone.Count);
            Assert.AreEqual(start.Count, tablet.Count);
        }

        /// <summary>
        /// The merged state is always one the game could have reached on its own: the count
        /// never goes negative and never exceeds the structural bound, whatever is joined.
        /// </summary>
        [Test]
        public void TheJoinPreservesTheInvariants()
        {
            var odd = Hints.Ledger(produced: 4, spent: 9, dueUnix: T0);       // impossible
            var wild = Hints.Ledger(produced: long.MaxValue, spent: 0, dueUnix: T0);

            var merged = Hints.Join(odd, wild);

            Assert.GreaterOrEqual(merged.Count, 0);
            Assert.LessOrEqual(merged.Count, HintLimits.HardCeiling);
            Assert.GreaterOrEqual(merged.Produced, merged.Spent);
        }

        // ------------------------------------------------------- the wire and the file
        /// <summary>
        /// A save written before hints were stored reads as a fresh full pool rather than as
        /// a player who has spent everything — which is the whole of the v19 migration, and
        /// why there is no migration code. Zero is unreachable for a genuine ledger because
        /// an account is seeded at the cap and <c>produced</c> only ever rises.
        /// </summary>
        [Test]
        public void ASaveWrittenBeforeHintsExistedReadsAsAFullPool()
        {
            var before = new SaveFileDto { wallet = new WalletDto() };   // JsonUtility's zeros
            var merged = SaveMerge.Join(before, new SaveFileDto { wallet = new WalletDto() });

            Assert.AreEqual(Hints.Full.Produced, merged.wallet.hintsProduced);
            Assert.AreEqual(0, merged.wallet.hintsSpent);
        }

        /// <summary>
        /// A device that has a ledger is not talked out of it by one that does not. The
        /// absent side contributes a full pool, which is what it is about to seed itself
        /// with anyway — generous, bounded by the cap, and worth nothing to forge.
        /// </summary>
        [Test]
        public void ALedgerJoinedAgainstAnAbsentOneKeepsItsSpends()
        {
            var mine = new SaveFileDto
            {
                wallet = new WalletDto { hintsProduced = 12, hintsSpent = 11, hintsDueUnix = T0 },
            };
            var theirs = new SaveFileDto { wallet = new WalletDto() };

            var merged = SaveMerge.Join(mine, theirs);

            Assert.AreEqual(12, merged.wallet.hintsProduced, "the produced counter is not rolled back");
            Assert.AreEqual(11, merged.wallet.hintsSpent, "and neither is the spend");
        }

        /// <summary>
        /// The delta is what decides whether a sync bothers to send anything. All three
        /// counters have to be compared: the deadline moves without the count moving, and
        /// that is precisely the state the other device needs in order to merge correctly.
        /// </summary>
        [Test]
        public void EveryPartOfTheHintLedgerMakesTheSyncSendSomething()
        {
            var baseline = new SaveFileDto
            {
                wallet = new WalletDto { hintsProduced = 3, hintsSpent = 1, hintsDueUnix = T0 },
            };

            Assert.IsTrue(Differs(baseline, w => w.hintsProduced = 4));
            Assert.IsTrue(Differs(baseline, w => w.hintsSpent = 2));
            Assert.IsTrue(Differs(baseline, w => w.hintsDueUnix = T0 + 1));
        }

        static bool Differs(SaveFileDto baseline, System.Action<WalletDto> change)
        {
            var changed = new SaveFileDto
            {
                wallet = new WalletDto
                {
                    hintsProduced = baseline.wallet.hintsProduced,
                    hintsSpent = baseline.wallet.hintsSpent,
                    hintsDueUnix = baseline.wallet.hintsDueUnix,
                },
            };
            change(changed.wallet);

            return SaveDelta.Between(baseline, changed).ScalarsChanged;
        }

        // ------------------------------------------------------------- the offer
        /// <summary>
        /// The published placement pays exactly one, and one is the only number that can be
        /// right while the ceiling equals the cap: two granted to somebody holding two would
        /// have half of it refused, and a video that pays less than the panel promised is
        /// the thing a player checks once.
        /// </summary>
        [Test]
        public void TheHintOfferPaysOneIntoAPoolWithNoHeadroom()
        {
            var offer = AdRewardTable.Default.Offer(AdPlacement.HintRefill);

            Assert.IsTrue(offer.IsValid);
            Assert.AreEqual(ChestDropKind.Hints, offer.Kind);
            Assert.AreEqual(1, offer.Amount);
            Assert.IsFalse(offer.IsCurrency, "a hint is not adjudicated, so it needs no account");
        }

        /// <summary>
        /// A hint is banked rather than spent inside the run that earned it, so the shared
        /// ad cooldown applies to it exactly as it does to a heart. Only the continue is
        /// exempt, and only because what it pays stops existing when the run resolves.
        /// </summary>
        [Test]
        public void AHintIsBankedRatherThanTransient()
        {
            Assert.IsFalse(ChestDropKinds.IsTransient(ChestDropKind.Hints));
            Assert.IsFalse(ChestDropKinds.IsCurrency(ChestDropKind.Hints));
            Assert.AreEqual(ChestDropKind.Hints, ChestDropKinds.Parse(ChestDropKinds.Hints));
        }
    }
}

using GlimmerGrove.Daily;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The arithmetic behind a reward flying into the hub's pills.
    ///
    /// <para>
    /// The flight itself is images on an arc and can only be judged by watching it. What
    /// cannot be judged by watching it is whether the number underneath is honest, and that
    /// is what this pins - because every way it goes wrong is invisible in a screenshot and
    /// visible to a player as the game having eaten something. A pill that stops one short
    /// of the balance reads as a reward that was not fully paid; a pill that walks backwards
    /// mid-cascade reads as one being taken away again; a prize that throws seven tokens for
    /// three hearts reads as the count in the air disagreeing with the count on the pill.
    /// </para>
    /// <para>
    /// The currency cases are the ones worth having. A chest's credits are in the ledger
    /// before the animation starts, so <c>live</c> never moves and the old chest code could
    /// safely capture it once. An ad's are granted by the server (invariant 10d), so
    /// <c>live</c> is read at every landing and may rise part way through - which is the one
    /// arrangement nobody would think to try by hand.
    /// </para>
    /// </summary>
    public sealed class RewardFlightTests
    {
        static ChestDrop Drop(ChestDropKind kind, int amount) => new ChestDrop(kind, amount);

        // ------------------------------------------------------------- token counts
        [Test]
        public void AGenerousPrizeThrowsTheBudgetAndNoMore()
        {
            Assert.AreEqual(RewardFlight.TokensPerDrop,
                            RewardFlight.TokenCount(Drop(ChestDropKind.Credits, 1_000)));
            Assert.AreEqual(RewardFlight.TokensPerDrop,
                            RewardFlight.TokenCount(Drop(ChestDropKind.Credits,
                                                         RewardFlight.TokensPerDrop)));
        }

        /// <summary>
        /// Three hearts throw three hearts. Throwing seven and landing them in fractions is
        /// the one case where the count in the air and the count on the pill visibly
        /// disagree, which is exactly the thing the flight exists to make legible.
        /// </summary>
        [Test]
        public void APrizeSmallerThanTheBudgetThrowsItself()
        {
            Assert.AreEqual(3, RewardFlight.TokenCount(Drop(ChestDropKind.Hearts, 3)));
            Assert.AreEqual(1, RewardFlight.TokenCount(Drop(ChestDropKind.Hearts, 1)));
        }

        /// <summary>
        /// A boost is one thing however many hours it runs for, so it is one token - not
        /// twelve, which is what its amount would otherwise buy it.
        /// </summary>
        [Test]
        public void AHeartBoostIsOneTokenWhateverItsDuration()
        {
            Assert.AreEqual(1, RewardFlight.TokenCount(Drop(ChestDropKind.HeartBoost, 12)));
        }

        [Test]
        public void NothingIsThrownForAPrizeThatIsNotOne()
        {
            Assert.AreEqual(0, RewardFlight.TokenCount(ChestDrop.None));
            Assert.AreEqual(0, RewardFlight.TokenCount(Drop(ChestDropKind.Credits, 0)));
        }

        // ------------------------------------------------------------- the readout
        // What a pill reads is the live balance less what is still in the air, kept per reward
        // (ResourceSlots.Payout) so several can land in one pill at once - the page no longer
        // waits for one cascade to finish before it takes the tap that starts the next.

        static ResourceSlots.Payout Pay(long sequence, long before, int landed, int tokens)
            => new ResourceSlots.Payout(sequence, before) { Landed = landed, Tokens = tokens };

        static long Read(long live, params ResourceSlots.Payout[] payouts)
            => ResourceSlots.Owed(live, payouts);

        /// <summary>
        /// The last token writes the balance itself rather than an interpolation. Without
        /// this a 1,000-credit prize thrown as seven lands on 999 for ever - one short, on
        /// the number a player is most likely to be watching.
        /// </summary>
        [Test]
        public void TheLastTokenWritesTheBalanceRatherThanTheInterpolation()
        {
            const int Tokens = 7;
            Assert.AreEqual(1_000L, Read(1_000L, Pay(1, 0L, Tokens, Tokens)));

            // And the step before it is genuinely short of the total, or the last landing
            // would have nothing left to say.
            Assert.Less(Read(1_000L, Pay(1, 0L, Tokens - 1, Tokens)), 1_000L);
        }

        [Test]
        public void TheReadoutClimbsWithTheLandings()
        {
            long previous = long.MinValue;

            for (int landed = 0; landed <= 7; landed++)
            {
                long shown = Read(800L, Pay(1, 100L, landed, 7));
                Assert.GreaterOrEqual(shown, previous, "landing " + landed + " went backwards");
                Assert.GreaterOrEqual(shown, 100L);
                Assert.LessOrEqual(shown, 800L);
                previous = shown;
            }

            Assert.AreEqual(800L, previous);
        }

        /// <summary>A held panel that has not paid yet owes its whole prize: the pill stands at the snapshot.</summary>
        [Test]
        public void AHeldPayoutShowsTheSnapshot()
        {
            Assert.AreEqual(4_000L, Read(5_000L, Pay(1, 4_000L, 0, 0)));
        }

        /// <summary>
        /// A rewarded ad's credits are granted by the server, so the balance can move
        /// <em>during</em> the cascade - the tokens start flying against an unchanged figure
        /// and the sync lands two landings in. The reading must not fall back to where it
        /// started when that happens: it walks from wherever it had got to.
        /// </summary>
        [Test]
        public void ABalanceThatArrivesMidCascadeIsPickedUpWithoutGoingBackwards()
        {
            const long Before = 4_000L;
            const int Tokens = 7;

            long previous = Before;

            for (int landed = 1; landed <= Tokens; landed++)
            {
                // Nothing from the server for the first three landings, then the grant lands.
                long live = landed < 4 ? Before : Before + 1_000L;

                long shown = Read(live, Pay(1, Before, landed, Tokens));
                Assert.GreaterOrEqual(shown, previous, "landing " + landed + " went backwards");
                previous = shown;
            }

            Assert.AreEqual(Before + 1_000L, previous);
        }

        /// <summary>
        /// The server never answering is an ordinary outcome, not an error state: the tokens
        /// still arrive and the pill still flashes, and the figure simply has not moved. What
        /// it must never do is drift off the balance in either direction.
        /// </summary>
        [Test]
        public void AGrantThatHasNotLandedLeavesTheReadoutExactlyWhereItWas()
        {
            for (int landed = 0; landed <= 7; landed++)
                Assert.AreEqual(4_000L, Read(4_000L, Pay(1, 4_000L, landed, 7)));
        }

        /// <summary>
        /// A balance below the snapshot (a spend landing mid-cascade) owes nothing, so the pill
        /// reads the balance - never above it, and never a figure walked down to it.
        /// </summary>
        [Test]
        public void AFallenBalanceReadsTheBalance()
        {
            for (int landed = 0; landed <= 5; landed++)
                Assert.AreEqual(500L, Read(500L, Pay(1, 900L, landed, 5)));
        }

        [Test]
        public void APillWithNothingInTheAirReadsTheBalance()
        {
            Assert.AreEqual(730L, Read(730L));
            Assert.AreEqual(730L, Read(730L, new ResourceSlots.Payout(1, 0L) { Done = true }));
        }

        /// <summary>
        /// Two chests opened back to back: the first is still landing when the second is
        /// granted. Each owes only its own prize - the gap from its snapshot to the next - so
        /// the pill climbs by the first's tokens and then the second's, and is never shown the
        /// sum early, never walked backwards, and ends exactly on the balance.
        /// </summary>
        [Test]
        public void TwoRewardsLandingInOnePillEachOweOnlyTheirOwn()
        {
            // 100 before the first chest (+50), 150 before the second (+30), 180 now.
            var first = Pay(1, 100L, 3, 7);
            var second = Pay(2, 150L, 0, 0);    // held: its panel is up, nothing thrown yet

            Assert.AreEqual(100L + 50L * 3 / 7, Read(180L, first, second),
                            "the first walks; the second is all owed");

            first.Landed = 7;
            Assert.AreEqual(150L, Read(180L, first, second), "the first is paid in full, the second still held");

            second.Tokens = 7;
            second.Landed = 7;
            Assert.AreEqual(180L, Read(180L, first, second));
        }

        /// <summary>
        /// The newer reward can finish first - a small prize thrown after a large one. The
        /// older one still owes exactly its own segment, which ends where the newer one's
        /// snapshot was taken, so the newer one's prize is shown once and not walked again.
        /// </summary>
        [Test]
        public void TheNewerRewardCanFinishFirst()
        {
            var first = Pay(1, 100L, 0, 7);
            var second = Pay(2, 150L, 7, 7);

            Assert.AreEqual(180L - 50L, Read(180L, first, second));

            second.Done = true;
            Assert.AreEqual(180L - 50L, Read(180L, first, second), "done changes nothing it had not already paid");

            first.Landed = 7;
            Assert.AreEqual(180L, Read(180L, first, second));
        }

        /// <summary>
        /// Through the real readouts: entries are ordered by snapshot, a finished entry that an
        /// older one still measures to is kept, and once everything is released the readout is
        /// free and reads the balance. Release is idempotent.
        /// </summary>
        [Test]
        public void ReleasingEveryPayoutFreesTheReadout()
        {
            const ResourceSlots.Kind Kind = ResourceSlots.Kind.Gems;
            try
            {
                var newer = ResourceSlots.Claim(Kind, 20, 150L);
                var older = ResourceSlots.Claim(Kind, 10, 100L);
                Assert.IsTrue(ResourceSlots.IsPaying(Kind));

                ResourceSlots.Release(Kind, newer);
                Assert.IsTrue(ResourceSlots.IsPaying(Kind), "the older one still owes, measured to the newer one's snapshot");

                ResourceSlots.Release(Kind, older);
                ResourceSlots.Release(Kind, older);
                Assert.IsFalse(ResourceSlots.IsPaying(Kind));
                Assert.AreEqual(ResourceSlots.Balance(Kind), ResourceSlots.Walked(Kind));
            }
            finally
            {
                ResourceSlots.ResetPayouts();
            }
        }

        /// <summary>
        /// Two flights at once through the real readouts: the first to finish must not hand the
        /// pill back while the second still holds it - the fault a single flag per readout had,
        /// and the reason a page could not take a second chest while the first was landing.
        /// </summary>
        [Test]
        public void TheFirstFlightToFinishDoesNotReleaseTheSecondsHold()
        {
            var a = new UnityEngine.GameObject("first");
            var b = new UnityEngine.GameObject("second");
            try
            {
                var first = RewardFlight.Begin();
                first.Hold(a.transform);
                var second = RewardFlight.Begin();
                second.Hold(b.transform);

                first.Play(null, null);
                for (int k = 0; k < 3; k++)
                    Assert.IsTrue(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "still held by the second: " + (ResourceSlots.Kind)k);

                second.Play(null, null);
                for (int k = 0; k < 3; k++)
                    Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "released: " + (ResourceSlots.Kind)k);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
                ResourceSlots.ResetPayouts();
            }
        }

        // ------------------------------------------------------------- the hold
        // A panel freezes the pills from the moment it opens, so a balance landing behind it
        // (an ad's coins arriving from the server) cannot count up twice. What must never
        // happen is a pill left frozen, so every ending releases all three.

        [Test]
        public void AHeldPanelFreezesEveryPillUntilItPays()
        {
            var owner = new UnityEngine.GameObject("prize");
            try
            {
                var flight = RewardFlight.Begin();
                flight.Hold(owner.transform);

                for (int k = 0; k < 3; k++)
                    Assert.IsTrue(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "held: " + (ResourceSlots.Kind)k);

                bool told = false;
                flight.Play(null, () => told = true);

                Assert.IsTrue(told, "the caller is let go");
                for (int k = 0; k < 3; k++)
                    Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "released: " + (ResourceSlots.Kind)k);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                ResourceSlots.ResetPayouts();
            }
        }

        [Test]
        public void AFlightThatIsNeverHeldClaimsNothingUntilItPlays()
        {
            RewardFlight.Begin();
            for (int k = 0; k < 3; k++)
                Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k));
        }
    }
}

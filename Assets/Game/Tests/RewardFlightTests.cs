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
    /// is what this pins — because every way it goes wrong is invisible in a screenshot and
    /// visible to a player as the game having eaten something. A pill that stops one short
    /// of the balance reads as a reward that was not fully paid; a pill that walks backwards
    /// mid-cascade reads as one being taken away again; a prize that throws seven tokens for
    /// three hearts reads as the count in the air disagreeing with the count on the pill.
    /// </para>
    /// <para>
    /// The currency cases are the ones worth having. A chest's credits are in the ledger
    /// before the animation starts, so <c>live</c> never moves and the old chest code could
    /// safely capture it once. An ad's are granted by the server (invariant 10d), so
    /// <c>live</c> is read at every landing and may rise part way through — which is the one
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
        /// A boost is one thing however many hours it runs for, so it is one token — not
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
        /// <summary>
        /// The last token writes the balance itself rather than the interpolation. Without
        /// this a 1,000-credit prize thrown as seven lands on 999 for ever — one short, on
        /// the number a player is most likely to be watching.
        /// </summary>
        [Test]
        public void TheLastTokenWritesTheBalanceRatherThanTheInterpolation()
        {
            const int Tokens = 7;
            Assert.AreEqual(1_000L, RewardFlight.Shown(0L, 1_000L, Tokens, Tokens));

            // And the step before it is genuinely short of the total, or the last landing
            // would have nothing left to say.
            Assert.Less(RewardFlight.Shown(0L, 1_000L, Tokens - 1, Tokens), 1_000L);
        }

        [Test]
        public void TheReadoutClimbsWithTheLandings()
        {
            long previous = long.MinValue;

            for (int landed = 0; landed <= 7; landed++)
            {
                long shown = RewardFlight.Shown(100L, 800L, landed, 7);
                Assert.GreaterOrEqual(shown, previous, "landing " + landed + " went backwards");
                Assert.GreaterOrEqual(shown, 100L);
                Assert.LessOrEqual(shown, 800L);
                previous = shown;
            }

            Assert.AreEqual(800L, previous);
        }

        /// <summary>
        /// A rewarded ad's credits are granted by the server, so the balance can move
        /// <em>during</em> the cascade — the tokens start flying against an unchanged figure
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

                long shown = RewardFlight.Shown(Before, live, landed, Tokens);
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
                Assert.AreEqual(4_000L, RewardFlight.Shown(4_000L, 4_000L, landed, 7));
        }

        /// <summary>
        /// A balance that has fallen since the snapshot cannot happen on the hub — nothing is
        /// spent from behind a modal — but the reading is defined for it anyway, because the
        /// alternative to defining it is a number that jitters if it ever does.
        /// </summary>
        [Test]
        public void AFallenBalanceStillWalksOneWay()
        {
            long previous = long.MaxValue;

            for (int landed = 0; landed <= 5; landed++)
            {
                long shown = RewardFlight.Shown(900L, 500L, landed, 5);
                Assert.LessOrEqual(shown, previous);
                Assert.GreaterOrEqual(shown, 500L);
                Assert.LessOrEqual(shown, 900L);
                previous = shown;
            }

            Assert.AreEqual(500L, previous);
        }

        [Test]
        public void APillWithNoTokensReadsTheBalance()
        {
            Assert.AreEqual(730L, RewardFlight.Shown(0L, 730L, 0, 0));
        }
    }
}

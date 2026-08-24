using GlimmerGrove.Homestead;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// How loud the grove shop's ceremony is, and how long it holds the screen.
    ///
    /// <para>
    /// A band table is the kind of rule that compiles, validates and reads perfectly while
    /// putting the whole catalog in one tier — which on screen looks exactly like a feature
    /// nobody finished, and which nothing but running it over the real price range can see.
    /// So the cases below are mostly about the <em>shipped prices</em>: the pebble at 60, the
    /// median decor piece at 430, the dearest decor at 4,000 and the four rungs of the home
    /// ladder. If a re-price moves those, these are meant to be re-read rather than deleted.
    /// </para>
    /// <para>
    /// The other half is the ceiling. <c>GroveGrowth.MaxSpread</c>'s argument for the second
    /// time: the shop is a grid of 150 cells and buying is what the player came to do, so a
    /// drop that adds something dearer than the sanctum must not be able to lengthen the
    /// interruption.
    /// </para>
    /// </summary>
    public sealed class GroveUnveilTests
    {
        // ----------------------------------------------------------------- tiers
        [Test]
        public void TheBandsRiseAndCoverEveryPrice()
        {
            for (int i = 1; i < GroveUnveil.Bands.Length; i++)
                Assert.Greater(GroveUnveil.Bands[i], GroveUnveil.Bands[i - 1],
                               "a band that does not rise silently swallows the tier under it");

            Assert.AreEqual(GroveUnveil.Tiers - 1, GroveUnveil.Bands.Length,
                            "one fewer boundary than there are tiers, or a tier is unreachable");
        }

        [Test]
        public void TierRisesWithPriceAndNeverLeavesTheLadder()
        {
            int last = 0;

            foreach (int cost in new[] { 0, 1, 60, 249, 250, 430, 699, 700, 1999, 2000,
                                         4000, 6999, 7000, 28000, 1000000 })
            {
                int tier = GroveUnveil.TierOf(cost);

                Assert.GreaterOrEqual(tier, 1, $"{cost} fell off the bottom");
                Assert.LessOrEqual(tier, GroveUnveil.Tiers, $"{cost} fell off the top");
                Assert.GreaterOrEqual(tier, last, $"{cost} arrives quieter than something cheaper");

                last = tier;
            }
        }

        [Test]
        public void ABandBelongsToTheTierBelowIt()
        {
            // Stated once rather than assumed at every call site: the comparison is strictly
            // less-than, so a piece priced exactly on a boundary is the louder of the two.
            for (int i = 0; i < GroveUnveil.Bands.Length; i++)
            {
                int at = GroveUnveil.Bands[i];

                Assert.AreEqual(i + 1, GroveUnveil.TierOf(at - 1));
                Assert.AreEqual(i + 2, GroveUnveil.TierOf(at));
            }
        }

        [Test]
        public void SomethingFreeIsStillGivenAScheme()
        {
            // Nothing here decides whether a ceremony happens, only how loud it is, and a tier
            // of zero would be a celebration with no colours in it.
            Assert.AreEqual(1, GroveUnveil.TierOf(0));
            Assert.AreEqual(1, GroveUnveil.TierOf(-500));
            Assert.AreEqual(1, GroveUnveil.TierOf(default(HomesteadPiece)));
        }

        // -------------------------------------------------------- the shipped shop
        [Test]
        public void TheShippedDecorSpansSeveralTiersRatherThanPilingIntoOne()
        {
            // The whole point of tiering, and the failure that looks like no feature at all.
            // These are real prices out of homestead.json: the cheapest trinket, the quartiles,
            // and the dearest decor piece there is.
            var seen = new System.Collections.Generic.HashSet<int>();

            foreach (int cost in new[] { 60, 140, 220, 260, 340, 430, 640, 850, 1150, 1500, 4000 })
                seen.Add(GroveUnveil.TierOf(cost));

            Assert.GreaterOrEqual(seen.Count, 3,
                                  "decor that all arrives at one volume is a band table nobody tuned");
        }

        [Test]
        public void TheHomeLadderClimbsIntoTheTopTiersAndReachesTheLast()
        {
            // 2,500 / 6,000 / 13,000 / 28,000 — the four rungs above the free cottage. The
            // house is the loudest thing the grove sells and the top of it has to reach gold,
            // or the rarest scheme in the game is one nothing can ever earn.
            Assert.AreEqual(4, GroveUnveil.TierOf(2500), "lodge");
            Assert.AreEqual(4, GroveUnveil.TierOf(6000), "hall");
            Assert.AreEqual(5, GroveUnveil.TierOf(13000), "manor");
            Assert.AreEqual(5, GroveUnveil.TierOf(28000), "sanctum");
        }

        [Test]
        public void ConfettiIsEarnedByThePriceRatherThanByBeingAHouse()
        {
            // The band is what reserves them, not the kind of thing that crosses it. Every rung
            // of the home ladder is over it, the ordinary decor piece is nowhere near, and the
            // one four-thousand-credit centrepiece — about a week of play for a single object —
            // is over it too, which is the intended answer rather than a leak.
            Assert.IsFalse(GroveUnveil.FanfareFor(Piece(430)).HasConfetti, "the median decor piece");
            Assert.IsFalse(GroveUnveil.FanfareFor(Piece(1500)).HasConfetti, "the ninth decile");

            Assert.IsTrue(GroveUnveil.FanfareFor(Piece(4000)).HasConfetti, "the dearest decor");
            Assert.IsTrue(GroveUnveil.FanfareFor(Piece(2500)).HasSeal, "the first home rung");
            Assert.IsTrue(GroveUnveil.FanfareFor(Piece(28000)).HasSeal, "the last");
        }

        [Test]
        public void MostOfWhatIsBoughtIsBoughtQuietly()
        {
            // The property that makes the loud tiers worth anything, stated over the shipped
            // spread: three quarters of decor purchases are tier one or two.
            int quiet = 0;
            var deciles = new[] { 60, 140, 220, 260, 340, 430, 640, 850, 1150, 1500 };

            foreach (int cost in deciles)
                if (GroveUnveil.TierOf(cost) <= 2) quiet++;

            Assert.GreaterOrEqual(quiet, 7, "a shop where everything is a spectacle has none");
        }

        static HomesteadPiece Piece(int cost)
            => new HomesteadPiece("p", "p", false, HomesteadPieceKind.Decor,
                                  cost, default, default, 1f, 0f);

        // -------------------------------------------------------------- fanfare
        [Test]
        public void EveryPartOfTheFanfareGrowsWithTheTier()
        {
            var last = GroveUnveil.FanfareOf(1);

            for (int tier = 2; tier <= GroveUnveil.Tiers; tier++)
            {
                var f = GroveUnveil.FanfareOf(tier);

                Assert.AreEqual(tier, f.Tier);
                Assert.GreaterOrEqual(f.Rays, last.Rays, "rays");
                Assert.GreaterOrEqual(f.Aurora, last.Aurora, "aurora");
                Assert.GreaterOrEqual(f.Shockwaves, last.Shockwaves, "shockwaves");
                Assert.GreaterOrEqual(f.Sparks, last.Sparks, "sparks");
                Assert.GreaterOrEqual(f.Flash, last.Flash, "flash");
                Assert.GreaterOrEqual(f.Hold, last.Hold, "hold");

                last = f;
            }

            Assert.Greater(GroveUnveil.FanfareOf(GroveUnveil.Tiers).Sparks,
                           GroveUnveil.FanfareOf(1).Sparks,
                           "a ladder whose ends are the same is not a ladder");
        }

        [Test]
        public void TheBottomTierIsStillWorthWatching()
        {
            // The cheapest thing in the shop is bought most often, so its ceremony has to be a
            // reward rather than a formality. Everything but the two reserved flourishes.
            var f = GroveUnveil.FanfareOf(1);

            Assert.Greater(f.Rays, 0);
            Assert.Greater(f.Shockwaves, 0);
            Assert.Greater(f.Sparks, 0);
            Assert.Greater(f.Flash, 0f);
            Assert.Greater(f.Hold, .5f);
        }

        [Test]
        public void AnOutOfRangeTierClampsRatherThanThrowing()
        {
            Assert.AreEqual(1, GroveUnveil.FanfareOf(0).Tier);
            Assert.AreEqual(1, GroveUnveil.FanfareOf(-3).Tier);
            Assert.AreEqual(GroveUnveil.Tiers, GroveUnveil.FanfareOf(99).Tier);
        }

        // --------------------------------------------------------------- ceiling
        [Test]
        public void NoPurchaseHoldsTheScreenLongerThanTheCeiling()
        {
            for (int tier = 1; tier <= GroveUnveil.Tiers; tier++)
                Assert.LessOrEqual(GroveUnveil.Seconds(tier), GroveUnveil.MaxSeconds + .0001f,
                                   $"tier {tier} outstays its welcome");

            Assert.LessOrEqual(GroveUnveil.Seconds(99), GroveUnveil.MaxSeconds + .0001f,
                               "and so must anything a drop adds above the ladder");
        }

        [Test]
        public void TheCheapestPurchaseIsOverQuicklyAndTheDearestIsNot()
        {
            // A ceremony seen a hundred and fifty times has to be short; one seen four times in
            // an account's life can afford to linger. Both halves matter — a flat duration is
            // the tiering not happening.
            Assert.Less(GroveUnveil.Seconds(1), 2f);
            Assert.Greater(GroveUnveil.Seconds(GroveUnveil.Tiers), GroveUnveil.Seconds(1) + .5f);
        }

        [Test]
        public void TheFixedBeatsAreRealRatherThanAnEstimate()
        {
            // Seconds is only a ceiling worth having if it is the length the sequence actually
            // runs at. Both halves are read by GroveUnveilOverlay rather than typed there
            // again, and this pins that they are the whole of what sits outside the hold.
            Assert.Greater(GroveUnveil.PlateAt, 0f);
            Assert.Greater(GroveUnveil.Outro, 0f);

            Assert.AreEqual(GroveUnveil.PlateAt + GroveUnveil.FanfareOf(3).Hold + GroveUnveil.Outro,
                            GroveUnveil.Seconds(3), .0001f);
        }
    }
}

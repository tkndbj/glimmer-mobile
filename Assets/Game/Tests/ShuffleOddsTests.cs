using System;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The odds a refresh shows before it is bought (MODES.md 59p): they sum to a hundred, they are
    /// the deck's own tables and promises, a rarity with nothing left hands its share on the way the
    /// deck does, and - the one that matters - thousands of real seeded deals land where they say.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleOddsTests
    {
        static int Sum(ShuffleOdds odds)
        {
            int sum = 0;
            for (int t = 0; t < ShuffleOdds.Tiers; t++) sum += odds.Percent((ShuffleTier)t);
            return sum;
        }

        [Test]
        public void TheOddsAreTheHandsOwnTableAndSumToAHundred()
        {
            var deck = ShuffleTuning.Shipped.Deck;

            foreach (int hand in new[] { 1, 2, deck.HeatedFrom, deck.WildFrom, 40 })
            {
                var odds = ShuffleOdds.For(new ShuffleBuild(1u), hand, ShuffleTuning.Shipped, null);
                var table = deck.Table(hand);

                Assert.AreEqual(100, Sum(odds), $"hand {hand}");
                for (int t = 0; t < ShuffleOdds.Tiers; t++)
                    Assert.AreEqual(table[t], odds.Percent((ShuffleTier)t), $"hand {hand}, {(ShuffleTier)t}");
            }

            var opening = ShuffleOdds.For(new ShuffleBuild(1u), 1, ShuffleTuning.Shipped, null);
            Assert.AreEqual(56, opening.Percent(ShuffleTier.Common));
            Assert.AreEqual(3, opening.Percent(ShuffleTier.Legendary));
        }

        [Test]
        public void ThePromiseIsSaidOnTheHandsThatCarryIt()
        {
            var deck = ShuffleTuning.Shipped.Deck;
            ShuffleTier? FloorOf(int hand) => ShuffleOdds.For(new ShuffleBuild(1u), hand, ShuffleTuning.Shipped, null).Floor;

            Assert.IsNull(FloorOf(1));
            Assert.AreEqual(ShuffleTier.Rare, FloorOf(deck.RarePity));
            Assert.AreEqual(ShuffleTier.Epic, FloorOf(deck.EpicPity));
            Assert.AreEqual(ShuffleTier.Epic, FloorOf(deck.RarePity * deck.EpicPity), "the epic promise outranks the rare one");
        }

        [Test]
        public void ARarityWithNothingLeftHandsItsShareDown()
        {
            var build = new ShuffleBuild(4u);
            foreach (var card in ShuffleTuning.Shipped.Cards)
                if (card.Tier == ShuffleTier.Legendary)
                    while (build.Takeable(card)) Assert.IsTrue(build.Take(card));

            var odds = ShuffleOdds.For(build, 1, ShuffleTuning.Shipped, null);
            var table = ShuffleTuning.Shipped.Deck.Table(1);

            Assert.AreEqual(0, odds.Cards(ShuffleTier.Legendary));
            Assert.AreEqual(0, odds.Percent(ShuffleTier.Legendary), "a rarity with nothing left was given a chance");
            Assert.AreEqual(table[2] + table[3], odds.Percent(ShuffleTier.Epic), "the legendary share did not fall to epic");
            Assert.AreEqual(100, Sum(odds));
        }

        [Test]
        public void TheCountsAreWhatTheRefreshCanDeal()
        {
            var run = new ShuffleRun(31u);
            Assert.IsNull(ShuffleOdds.ForRefresh(run), "odds were read with no hand on the table");

            var hand = run.Deal();
            var odds = ShuffleOdds.ForRefresh(run);

            for (int t = 0; t < ShuffleOdds.Tiers; t++)
            {
                int expected = 0;
                foreach (var card in run.Tuning.Cards)
                {
                    if (card.Tier != (ShuffleTier)t || !run.Build.Takeable(card)) continue;
                    bool offered = false;
                    foreach (var shown in hand) offered |= ReferenceEquals(shown, card);
                    if (!offered) expected++;
                }

                Assert.AreEqual(expected, odds.Cards((ShuffleTier)t), $"{(ShuffleTier)t}: the count includes a card the refresh skips");
            }
        }

        /// <summary>
        /// The printed chance against the deck itself: every card of four thousand seeded refreshes
        /// past the first (the promise only ever lifts the first), counted by rarity.
        /// </summary>
        [Test]
        public void ThousandsOfRealRefreshesLandWhereTheOddsSay()
        {
            foreach (int handNo in new[] { 1, ShuffleTuning.Shipped.Deck.WildFrom })
            {
                var seen = new int[ShuffleOdds.Tiers];
                int total = 0;
                ShuffleOdds printed = null;

                for (uint seed = 1; seed <= 4000; seed++)
                {
                    var run = new ShuffleRun(seed);
                    for (int h = 1; h < handNo; h++) run.Deal();      // reach the hand number, taking nothing
                    run.Deal();

                    printed = printed ?? ShuffleOdds.ForRefresh(run);

                    var refreshed = run.Refresh();
                    for (int i = 1; i < refreshed.Count; i++) { seen[(int)refreshed[i].Tier]++; total++; }
                }

                for (int t = 0; t < ShuffleOdds.Tiers; t++)
                {
                    double dealt = 100.0 * seen[t] / total;
                    Console.WriteLine($"hand {handNo} {(ShuffleTier)t}: printed {printed.Percent((ShuffleTier)t)}%, dealt {dealt:F1}%");
                    Assert.AreEqual(printed.Percent((ShuffleTier)t), dealt, 2.0,
                                    $"hand {handNo}: {(ShuffleTier)t} is printed at {printed.Percent((ShuffleTier)t)}% and dealt at {dealt:F1}%");
                }
            }
        }

        [Test]
        public void ThePanelFitsTheShortestCanvas()
        {
            Assert.LessOrEqual(ShuffleOddsOverlay.Height(true), Layout.PanelStack.TallestPanel);
            Assert.GreaterOrEqual(ShuffleOddsOverlay.RarityX - ShuffleOddsOverlay.RarityW * .5f, -ShuffleOddsOverlay.BodyW * .5f, "rarity runs past the body");
            Assert.LessOrEqual(ShuffleOddsOverlay.RarityX + ShuffleOddsOverlay.RarityW * .5f, ShuffleOddsOverlay.ChanceX - ShuffleOddsOverlay.ChanceW * .5f, "rarity and chance overlap");
            Assert.LessOrEqual(ShuffleOddsOverlay.ChanceX + ShuffleOddsOverlay.ChanceW * .5f, ShuffleOddsOverlay.LeftX - ShuffleOddsOverlay.LeftW * .5f, "chance and cards left overlap");
            Assert.LessOrEqual(ShuffleOddsOverlay.LeftX + ShuffleOddsOverlay.LeftW * .5f, ShuffleOddsOverlay.BodyW * .5f, "cards left runs past the body");
        }
    }
}

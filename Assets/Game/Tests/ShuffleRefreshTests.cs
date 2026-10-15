using System.Collections.Generic;
using GlimmerGrove.Shuffle;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Refreshing a Shuffle hand for gems (MODES.md 59p): the price ladder, what a refresh deals,
    /// what it counts and what it never touches. The debit itself is <c>PlayerProgression.TrySpend</c>'s,
    /// held where every gem debit is held; everything here is the run's own arithmetic.
    /// </summary>
    [TestFixture]
    public sealed class ShuffleRefreshTests
    {
        // ------------------------------------------------------------------ the price
        [Test]
        public void ThePriceStartsAtFiveAndDoubles()
        {
            long[] ladder = { 5, 10, 20, 40, 80, 160, 320, 640, 1280, 2560 };
            for (int taken = 0; taken < ladder.Length; taken++)
                Assert.AreEqual(ladder[taken], ShuffleRefresh.PriceFor(taken), $"refresh {taken + 1}");

            Assert.AreEqual(ShuffleRefresh.FirstGems, ShuffleRefresh.PriceFor(-3), "a negative count is no refresh bought");
        }

        [Test]
        public void ThePriceSaturatesAndNeverFalls()
        {
            long last = 0L;
            for (int taken = 0; taken < 200; taken++)
            {
                long price = ShuffleRefresh.PriceFor(taken);
                Assert.GreaterOrEqual(price, last, $"refresh {taken + 1} is cheaper than the one before");
                Assert.LessOrEqual(price, ShuffleRefresh.MostGems);
                last = price;
            }

            Assert.AreEqual(ShuffleRefresh.MostGems, ShuffleRefresh.PriceFor(int.MaxValue), "a count nothing bounds neither overflows nor spins");
        }

        // ------------------------------------------------------------------ the run
        [Test]
        public void ARefreshReplacesTheHandAndIsCountedOnTheRun()
        {
            var run = new ShuffleRun(41u);
            Assert.IsFalse(run.CanRefresh, "a refresh was offered with no hand on the table");
            Assert.IsNull(run.Refresh());
            Assert.AreEqual(0, run.Refreshes);

            var first = run.Deal();
            Assert.IsTrue(run.CanRefresh);
            Assert.AreEqual(5L, run.RefreshPrice);

            var second = run.Refresh();
            Assert.IsNotNull(second);
            Assert.AreEqual(first.Count, second.Count, "a refresh dealt a shorter hand than the deck could fill");
            Assert.AreSame(second, run.Open, "the refreshed hand is not the one on the table");
            Assert.AreEqual(1, run.Hands, "a refresh was counted as a hand, and would move when the next is owed");
            Assert.AreSame(second, run.Dealt[0]);
            Assert.AreEqual(1, run.Refreshes);
            Assert.AreEqual(10L, run.RefreshPrice);

            Assert.IsFalse(run.Take(first[0]), "a card from the hand a refresh replaced was taken");
        }

        [Test]
        public void ARefreshSparesTheCardsItReplaces()
        {
            for (uint seed = 300; seed < 360; seed++)
            {
                var run = new ShuffleRun(seed);
                var before = new List<ShuffleCard>(run.Deal());
                var after = run.Refresh();

                foreach (var card in after)
                    Assert.IsFalse(Contains(before, card), $"seed {seed}: '{card.Id}' was offered again with a whole deck left");
            }
        }

        [Test]
        public void ARefreshKeepsTheHandsPromise()
        {
            for (uint seed = 500; seed < 540; seed++)
            {
                var run = new ShuffleRun(seed);

                for (int hand = 1; hand <= ShuffleDeck.EpicPity; hand++)
                {
                    var dealt = run.Deal();
                    if (hand < ShuffleDeck.EpicPity) { run.Take(dealt[0]); continue; }

                    for (int r = 0; r < 4; r++)
                        Assert.GreaterOrEqual(run.Refresh()[0].Tier, ShuffleTier.Epic,
                                              $"seed {seed}: refresh {r + 1} of an epic-promised hand broke the promise");
                }
            }
        }

        [Test]
        public void TheCountOutlivesAHandAndNotARun()
        {
            var run = new ShuffleRun(77u);

            run.Deal();
            run.Refresh();
            run.Refresh();
            Assert.IsTrue(run.Take(run.Open[0]));

            run.Deal();
            Assert.AreEqual(2, run.Refreshes, "a new hand reset the refresh count");
            Assert.AreEqual(20L, run.RefreshPrice, "the third refresh of a run is not twenty gems");

            var next = new ShuffleRun(77u);
            Assert.AreEqual(0, next.Refreshes);
            Assert.AreEqual(ShuffleRefresh.FirstGems, next.RefreshPrice, "a new run did not start the price again");
        }

        [Test]
        public void TwoRunsOnOneSeedAreRefreshedAlike()
        {
            var a = new ShuffleRun(901u);
            var b = new ShuffleRun(901u);

            for (int hand = 0; hand < 6; hand++)
            {
                a.Deal(); b.Deal();
                for (int r = 0; r < 2; r++)
                {
                    var ha = a.Refresh();
                    var hb = b.Refresh();
                    Assert.AreEqual(ha.Count, hb.Count);
                    for (int i = 0; i < ha.Count; i++) Assert.AreSame(ha[i], hb[i], $"hand {hand + 1} refresh {r + 1} slot {i}");
                }

                a.Take(a.Open[0]);
                b.Take(b.Open[0]);
            }
        }

        [Test]
        public void SparingNothingIsTheDealRollForRoll()
        {
            var a = new ShuffleBuild(12u);
            var b = new ShuffleBuild(12u);

            for (int hand = 1; hand <= 8; hand++)
            {
                var ha = ShuffleDeck.Deal(a, hand, a.Boosts.Roll100, ShuffleTuning.Shipped);
                var hb = ShuffleDeck.Deal(b, hand, b.Boosts.Roll100, ShuffleTuning.Shipped, new List<ShuffleCard>());

                Assert.AreEqual(ha.Count, hb.Count);
                for (int i = 0; i < ha.Count; i++) Assert.AreSame(ha[i], hb[i], $"hand {hand} slot {i}");

                a.Take(ha[0]);
                b.Take(hb[0]);
            }
        }

        [Test]
        public void NothingIsSoldWhenTheDeckHasNothingFresh()
        {
            var run = new ShuffleRun(5u);
            bool reached = false;

            for (int i = 0; i < 1000; i++)
            {
                var hand = run.Deal();
                if (hand.Count == 0) break;

                if (!run.CanRefresh)
                {
                    reached = true;

                    // Every card the build can still take is already on the table.
                    foreach (var card in ShuffleTuning.Shipped.Cards)
                        if (run.Build.Takeable(card))
                            Assert.IsTrue(Contains(hand, card), $"'{card.Id}' was left to deal and no refresh was sold");

                    int before = run.Refreshes;
                    Assert.IsNull(run.Refresh(), "a refresh with nothing fresh dealt something");
                    Assert.AreEqual(before, run.Refreshes, "a refresh that dealt nothing was counted");
                    Assert.AreSame(hand, run.Open, "a refused refresh moved the hand");
                }

                Assert.IsTrue(run.Take(hand[0]));
            }

            Assert.IsTrue(reached, "the deck ran out without ever reaching a hand that held all it had left");
        }

        // ------------------------------------------------------------------ the panel
        [Test]
        public void TheRefreshKeySitsUnderTheCardsInsideThePanel()
        {
            var L = Layout.ShuffleHandLayout.Default;

            foreach (int cards in new[] { 1, ShuffleDeck.HandSize, ShuffleDeck.MostHand })
            {
                Assert.IsTrue(L.IsClear(cards, out string fault), $"{cards} cards: {fault}");

                float keyTop = L.RefreshCentre(cards) - L.RefreshHeight * .5f;
                float keyFoot = L.RefreshCentre(cards) + L.RefreshHeight * .5f;

                Assert.Greater(keyTop, L.CardsFoot(cards), $"{cards} cards: the key touches the cards");
                Assert.AreEqual(L.PanelHeightFor(cards, refresh: true), keyFoot + L.Foot, 1e-3f,
                                $"{cards} cards: the panel does not end the key's foot below it");
                Assert.AreEqual(L.PanelHeightFor(cards), L.CardsFoot(cards) + L.Foot, 1e-3f,
                                $"{cards} cards: a panel with no key changed height");
            }
        }

        static bool Contains(IReadOnlyList<ShuffleCard> cards, ShuffleCard card)
        {
            for (int i = 0; i < cards.Count; i++)
                if (ReferenceEquals(cards[i], card)) return true;
            return false;
        }
    }
}

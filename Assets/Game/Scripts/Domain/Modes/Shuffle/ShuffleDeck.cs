using System;
using System.Collections.Generic;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// Deals a hand: three cards a build could still take, drawn by tier and by roll off the
    /// run's own stream.
    ///
    /// <para>
    /// <b>Every hand is different because the stream is seeded per run</b> (<c>ShuffleRun</c>),
    /// and every hand is <em>fair</em> because the tier is rolled first and the card second:
    /// a legendary is as rare whether the build holds two cards or twenty, and a tier that has
    /// nothing left to offer falls to the next one down rather than dealing a card the build
    /// cannot take.
    /// </para>
    /// <para>
    /// <b>Two promises on top of the roll</b>, because a deterministic stream turns "rare" into
    /// "never" on some run and averaging cannot see it (invariant 37ci): every third hand holds
    /// at least one card above common, and every fifth at least one epic or better. The promise
    /// is a floor on the first slot's tier and nothing else, so it can never deal a card the
    /// roll would have refused.
    /// </para>
    /// </summary>
    public static class ShuffleDeck
    {
        /// <summary>Cards in a hand.</summary>
        public const int HandSize = 3;

        /// <summary>
        /// How often each tier is dealt, per hundred: common, rare, epic, legendary.
        ///
        /// <b>They sum to a hundred and a test holds them there</b>, because a weight is only a
        /// probability when the whole is known.
        /// </summary>
        public static readonly int[] Weights = { 56, 30, 11, 3 };

        /// <summary>Every <see cref="RarePity"/>-th hand opens with at least a rare; every <see cref="EpicPity"/>-th with at least an epic.</summary>
        public const int RarePity = 3, EpicPity = 5;

        /// <summary>
        /// Deals hand number <paramref name="hand"/> (counting from one) for <paramref name="build"/>,
        /// drawing off <paramref name="roll"/>: a function answering a number under a hundred.
        ///
        /// Fewer than <see cref="HandSize"/> cards when the build has fewer left to take, and
        /// none at all once everything is held to its most - the caller then deals nothing.
        /// </summary>
        public static List<ShuffleCard> Deal(ShuffleBuild build, int hand, Func<int> roll)
        {
            var dealt = new List<ShuffleCard>(HandSize);
            if (build == null || roll == null) return dealt;

            for (int slot = 0; slot < HandSize; slot++)
            {
                var tier = TierFor(roll());

                if (slot == 0 && hand > 0)
                {
                    if (hand % EpicPity == 0 && tier < ShuffleTier.Epic) tier = ShuffleTier.Epic;
                    else if (hand % RarePity == 0 && tier < ShuffleTier.Rare) tier = ShuffleTier.Rare;
                }

                var card = Pick(build, dealt, tier, roll);
                if (card == null) break;

                dealt.Add(card);
            }

            return dealt;
        }

        /// <summary>Which tier a roll under a hundred lands on, over <see cref="Weights"/>.</summary>
        public static ShuffleTier TierFor(int roll)
        {
            if (roll < 0) roll = 0;
            if (roll > 99) roll = 99;

            int at = 0;
            for (int t = 0; t < Weights.Length; t++)
            {
                at += Weights[t];
                if (roll < at) return (ShuffleTier)t;
            }

            return ShuffleTier.Legendary;
        }

        /// <summary>
        /// A card of <paramref name="tier"/> the build can take and the hand does not already hold,
        /// falling down a tier and then up one when that tier has nothing left.
        /// </summary>
        static ShuffleCard Pick(ShuffleBuild build, List<ShuffleCard> hand, ShuffleTier tier, Func<int> roll)
        {
            // Down first, because a lower tier always has more cards left; then up, because a
            // build that has taken every common and rare is a build late enough to be offered
            // the rest of the deck.
            for (int t = (int)tier; t >= 0; t--)
            {
                var card = From(build, hand, (ShuffleTier)t, roll);
                if (card != null) return card;
            }

            for (int t = (int)tier + 1; t <= (int)ShuffleTier.Legendary; t++)
            {
                var card = From(build, hand, (ShuffleTier)t, roll);
                if (card != null) return card;
            }

            return null;
        }

        static ShuffleCard From(ShuffleBuild build, List<ShuffleCard> hand, ShuffleTier tier, Func<int> roll)
        {
            var pool = new List<ShuffleCard>(16);
            var all = ShuffleCards.All;

            for (int i = 0; i < all.Length; i++)
            {
                var card = all[i];
                if (card.Tier != tier || !build.Takeable(card)) continue;

                bool held = false;
                for (int h = 0; h < hand.Count; h++)
                    if (ReferenceEquals(hand[h], card)) { held = true; break; }

                if (!held) pool.Add(card);
            }

            if (pool.Count == 0) return null;

            // Two rolls under a hundred make a number under ten thousand, so a pool of up to a
            // hundred cards is drawn from evenly rather than off the low end of one roll.
            int pick = (roll() * 100 + roll()) % pool.Count;
            return pool[pick];
        }
    }
}

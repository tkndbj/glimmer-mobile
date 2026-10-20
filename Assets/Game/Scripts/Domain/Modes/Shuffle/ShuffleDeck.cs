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
        /// <summary>Cards in a hand, before a card widens it (<see cref="ShuffleBuild.HandSize"/>).</summary>
        public const int HandSize = 3;

        /// <summary>
        /// The most cards a hand may offer, whatever widens it: the panel lays out three across
        /// and two under (<c>ShuffleHandLayout</c>), and a test holds the layout to this.
        /// </summary>
        public const int MostHand = 5;

        /// <summary>
        /// How often each tier is dealt, per hundred: common, rare, epic, legendary.
        ///
        /// <b>They sum to a hundred and a test holds them there</b>, because a weight is only a
        /// probability when the whole is known.
        /// </summary>
        public static readonly int[] Weights = { 56, 30, 11, 3 };

        /// <summary>
        /// <b>The deck heats up</b> (the owner, 2026-10-09: "truly crazy and fully random after
        /// some point"): from hand <see cref="HeatedFrom"/> the tiers are dealt off
        /// <see cref="Heated"/>, and from hand <see cref="WildFrom"/> off <see cref="Wild"/>, so
        /// a run that has lived long enough to be strong is offered the cards that make it
        /// strange. Each sums to a hundred, held by the same test as <see cref="Weights"/>.
        /// </summary>
        public static readonly int[] Heated = { 42, 34, 17, 7 };

        public static readonly int[] Wild = { 26, 34, 26, 14 };

        /// <summary>The hands (counting from one) the two hotter tables start at: waves 12 and 20.</summary>
        public const int HeatedFrom = 6, WildFrom = 10;

        /// <summary>The tier table hand <paramref name="hand"/> is dealt off.</summary>
        public static int[] WeightsFor(int hand)
            => hand >= WildFrom ? Wild : hand >= HeatedFrom ? Heated : Weights;

        /// <summary>Every <see cref="RarePity"/>-th hand opens with at least a rare; every <see cref="EpicPity"/>-th with at least an epic.</summary>
        public const int RarePity = 3, EpicPity = 5;

        /// <summary>
        /// Deals hand number <paramref name="hand"/> (counting from one) for <paramref name="build"/>,
        /// drawing off <paramref name="roll"/>: a function answering a number under a hundred.
        ///
        /// Fewer than the build's hand (<see cref="ShuffleBuild.HandSize"/>) when it has fewer left to take, and
        /// none at all once everything is held to its most - the caller then deals nothing.
        /// </summary>
        public static List<ShuffleCard> Deal(ShuffleBuild build, int hand, Func<int> roll)
            => Deal(build, hand, roll, ShuffleTuning.Shipped);

        /// <summary>
        /// Deals hand number <paramref name="hand"/> as <see cref="Deal(ShuffleBuild, int, Func{int})"/>
        /// does, off <paramref name="tuning"/>'s tier tables, promises and cards (MODES.md 59l):
        /// a run's snapshot, so a hand is dealt off the figures the run opened on.
        /// <see cref="ShuffleTuning.Shipped"/> deals exactly what the shipped deck deals - the same
        /// tables, the same card objects in the same order.
        /// </summary>
        public static List<ShuffleCard> Deal(ShuffleBuild build, int hand, Func<int> roll, ShuffleTuning tuning)
            => Deal(build, hand, roll, tuning, null);

        /// <summary>
        /// Deals hand number <paramref name="hand"/> as the overloads above do, <b>sparing</b>
        /// <paramref name="spare"/>: a refresh (MODES.md 59p) deals round the cards it replaces
        /// while the deck holds anything else, and tops the hand up from them only when it does
        /// not - so a refresh is as full a hand as any other and never offers a card it could
        /// have avoided. With nothing spared it is exactly the deal above, roll for roll.
        /// </summary>
        public static List<ShuffleCard> Deal(ShuffleBuild build, int hand, Func<int> roll, ShuffleTuning tuning,
                                             IReadOnlyList<ShuffleCard> spare)
        {
            var dealt = new List<ShuffleCard>(MostHand);
            if (build == null || roll == null) return dealt;

            tuning = tuning ?? ShuffleTuning.Shipped;
            var deck = tuning.Deck;
            var weights = deck.TableFor(hand);

            // **The build says how many** (`grand_hand` widens it); every slot past the third is
            // dealt exactly as the first three are, so a wider hand is the same deck dealt longer.
            int size = build.HandSize;

            for (int slot = 0; slot < size; slot++)
            {
                var tier = TierFor(roll(), weights);

                if (slot == 0 && hand > 0)
                {
                    if (hand % deck.EpicPity == 0 && tier < ShuffleTier.Epic) tier = ShuffleTier.Epic;
                    else if (hand % deck.RarePity == 0 && tier < ShuffleTier.Rare) tier = ShuffleTier.Rare;
                }

                // A spared card is offered only once nothing else of any tier is left; a pick that
                // finds nothing draws no roll, so the fallback deals off the stream unmoved.
                var card = Pick(tuning.Cards, build, dealt, spare, tier, roll)
                           ?? (spare != null ? Pick(tuning.Cards, build, dealt, null, tier, roll) : null);
                if (card == null) break;

                dealt.Add(card);
            }

            return dealt;
        }

        /// <summary>Which tier a roll under a hundred lands on, over <see cref="Weights"/>.</summary>
        public static ShuffleTier TierFor(int roll) => TierFor(roll, Weights);

        /// <summary>Which tier a roll under a hundred lands on, over <paramref name="weights"/>.</summary>
        public static ShuffleTier TierFor(int roll, int[] weights)
        {
            if (roll < 0) roll = 0;
            if (roll > 99) roll = 99;

            int at = 0;
            for (int t = 0; t < weights.Length; t++)
            {
                at += weights[t];
                if (roll < at) return (ShuffleTier)t;
            }

            return ShuffleTier.Legendary;
        }

        /// <summary>
        /// A card of <paramref name="tier"/> the build can take and the hand does not already hold,
        /// falling down a tier and then up one when that tier has nothing left.
        /// </summary>
        static ShuffleCard Pick(IReadOnlyList<ShuffleCard> all, ShuffleBuild build, List<ShuffleCard> hand,
                                IReadOnlyList<ShuffleCard> spare, ShuffleTier tier, Func<int> roll)
        {
            // Down first, because a lower tier always has more cards left; then up, because a
            // build that has taken every common and rare is a build late enough to be offered
            // the rest of the deck.
            for (int t = (int)tier; t >= 0; t--)
            {
                var card = From(all, build, hand, spare, (ShuffleTier)t, roll);
                if (card != null) return card;
            }

            for (int t = (int)tier + 1; t <= (int)ShuffleTier.Legendary; t++)
            {
                var card = From(all, build, hand, spare, (ShuffleTier)t, roll);
                if (card != null) return card;
            }

            return null;
        }

        static ShuffleCard From(IReadOnlyList<ShuffleCard> all, ShuffleBuild build, List<ShuffleCard> hand,
                                IReadOnlyList<ShuffleCard> spare, ShuffleTier tier, Func<int> roll)
        {
            var pool = new List<ShuffleCard>(16);
            int total = 0;
            bool leaning = false;

            for (int i = 0; i < all.Count; i++)
            {
                var card = all[i];
                if (card.Tier != tier || !ShuffleSchools.Dealable(card, build)) continue;
                if (Holds(hand, card) || Holds(spare, card)) continue;

                pool.Add(card);

                int weight = ShuffleSchools.Weight(card, build);
                total += weight;
                leaning |= weight != ShuffleSchools.PlainWeight;
            }

            if (pool.Count == 0) return null;

            // **A build of no school is dealt exactly as the deck always dealt** (59r): two rolls
            // under a hundred make a number under ten thousand, so a pool of up to a hundred cards
            // is drawn from evenly rather than off the low end of one roll.
            if (!leaning) return pool[(roll() * 100 + roll()) % pool.Count];

            // **The lean** (`ShuffleSchools.Weight`): a card of a school the build has started
            // weighs more within this tier. Three rolls make a number under a million, so a pool's
            // whole weight is drawn from evenly.
            int at = ((roll() * 100 + roll()) * 100 + roll()) % total;
            for (int i = 0; i < pool.Count; i++)
            {
                at -= ShuffleSchools.Weight(pool[i], build);
                if (at < 0) return pool[i];
            }

            return pool[pool.Count - 1];
        }

        /// <summary>
        /// Whether the deck holds a card <paramref name="build"/> can take that
        /// <paramref name="open"/> does not already offer - whether a refresh (MODES.md 59p) could
        /// deal anything new. Off <paramref name="tuning"/>'s cards, the run's snapshot.
        /// </summary>
        public static bool HasFresh(ShuffleBuild build, IReadOnlyList<ShuffleCard> open, ShuffleTuning tuning)
        {
            if (build == null) return false;

            var all = (tuning ?? ShuffleTuning.Shipped).Cards;
            for (int i = 0; i < all.Count; i++)
                if (ShuffleSchools.Dealable(all[i], build) && !Holds(open, all[i])) return true;

            return false;
        }

        /// <summary>Whether <paramref name="cards"/> holds this very card (cards are compared by reference: a run's tuned card is one object).</summary>
        static bool Holds(IReadOnlyList<ShuffleCard> cards, ShuffleCard card)
        {
            if (cards == null) return false;

            for (int i = 0; i < cards.Count; i++)
                if (ReferenceEquals(cards[i], card)) return true;

            return false;
        }
    }
}

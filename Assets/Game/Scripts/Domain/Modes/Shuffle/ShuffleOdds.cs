using System.Collections.Generic;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// <b>What a refresh can deal, said before it is bought</b> (MODES.md 59p): each card's chance
    /// of each tier, the promise on the first card when this hand carries one, and how many cards
    /// each tier holds - among which every card is equally likely, unless the build has started a
    /// school (MODES.md 59r), whose cards then lean within their tier (<see cref="Leaning"/>).
    ///
    /// <para>
    /// <b>Read off what the deck deals with, never written as copy</b> (<c>ChestOddsOverlay</c>'s
    /// rule): the run's own tier table for this hand number (<see cref="ShuffleDeckTuning.Table"/>),
    /// its own promises, its own cards and build. A tier with nothing left to deal hands its share
    /// to the tier below, then above - exactly <see cref="ShuffleDeck"/>'s fall - so a printed
    /// chance is the chance. <c>ShuffleOddsTests</c> deals thousands of seeded hands and holds the
    /// two together.
    /// </para>
    /// </summary>
    public sealed class ShuffleOdds
    {
        /// <summary>Tiers, lowest first.</summary>
        public const int Tiers = (int)ShuffleTier.Legendary + 1;

        readonly int[] _percent;
        readonly int[] _cards;

        ShuffleOdds(int[] percent, int[] cards, ShuffleTier? floor)
        {
            _percent = percent;
            _cards = cards;
            Floor = floor;
        }

        /// <summary>Each card's chance of <paramref name="tier"/>, per hundred. The tiers sum to a hundred.</summary>
        public int Percent(ShuffleTier tier) => _percent[(int)tier];

        /// <summary>How many cards of <paramref name="tier"/> a refresh can deal right now.</summary>
        public int Cards(ShuffleTier tier) => _cards[(int)tier];

        /// <summary>The least tier the first card is promised, or null when this hand promises nothing.</summary>
        public ShuffleTier? Floor { get; }

        /// <summary>
        /// Whether some card a refresh could deal weighs more than the rest of its tier
        /// (<see cref="ShuffleSchools.Weight"/>): the panel then says so beside "equally likely".
        /// A rarity's chance never moves, so every figure above is still the figure.
        /// </summary>
        public bool Leaning { get; private set; }

        /// <summary>
        /// The odds of refreshing <paramref name="run"/>'s open hand, or null when it has none.
        /// A refresh deals the same hand number again, so these are that hand's odds.
        /// </summary>
        public static ShuffleOdds ForRefresh(ShuffleRun run)
            => run == null || run.Open == null ? null : For(run.Build, run.Hands, run.Tuning, run.Open);

        /// <summary>
        /// The odds of hand <paramref name="hand"/> (counting from one) for <paramref name="build"/>
        /// on <paramref name="tuning"/>, with <paramref name="spare"/> skipped while anything else
        /// is left (a refresh's own rule, <see cref="ShuffleDeck.Deal(ShuffleBuild, int, System.Func{int}, ShuffleTuning, IReadOnlyList{ShuffleCard})"/>).
        /// </summary>
        public static ShuffleOdds For(ShuffleBuild build, int hand, ShuffleTuning tuning, IReadOnlyList<ShuffleCard> spare)
        {
            tuning = tuning ?? ShuffleTuning.Shipped;
            var deck = tuning.Deck;

            var cards = new int[Tiers];
            if (build != null)
                foreach (var card in tuning.Cards)
                    if (ShuffleSchools.Dealable(card, build) && !Holds(spare, card)) cards[(int)card.Tier]++;

            // Every card offered last time and nothing else left: the refresh deals from them.
            bool any = false;
            foreach (int n in cards) any |= n > 0;
            if (!any && build != null)
                foreach (var card in tuning.Cards)
                    if (ShuffleSchools.Dealable(card, build)) cards[(int)card.Tier]++;

            var weights = deck.Table(hand);
            var percent = new int[Tiers];
            for (int t = 0; t < Tiers; t++)
            {
                int landed = Landing((ShuffleTier)t, cards);
                if (landed >= 0) percent[landed] += weights[t];
            }

            ShuffleTier? floor = null;
            if (hand > 0)
            {
                if (hand % deck.EpicPity == 0) floor = ShuffleTier.Epic;
                else if (hand % deck.RarePity == 0) floor = ShuffleTier.Rare;
            }

            // A promise is only worth printing when the deck can keep it.
            if (floor.HasValue)
            {
                bool kept = false;
                for (int t = (int)floor.Value; t < Tiers; t++) kept |= cards[t] > 0;
                if (!kept) floor = null;
            }

            bool leaning = false;
            if (build != null)
                foreach (var card in tuning.Cards)
                    if (ShuffleSchools.Dealable(card, build) && ShuffleSchools.Weight(card, build) != ShuffleSchools.PlainWeight)
                        leaning = true;

            return new ShuffleOdds(percent, cards, floor) { Leaning = leaning };
        }

        /// <summary>
        /// The tier a roll of <paramref name="rolled"/> deals from: itself if it has a card, else
        /// the nearest below, else the nearest above (<see cref="ShuffleDeck"/>'s <c>Pick</c>), or
        /// -1 when the deck is empty.
        /// </summary>
        static int Landing(ShuffleTier rolled, int[] cards)
        {
            for (int t = (int)rolled; t >= 0; t--) if (cards[t] > 0) return t;
            for (int t = (int)rolled + 1; t < Tiers; t++) if (cards[t] > 0) return t;
            return -1;
        }

        static bool Holds(IReadOnlyList<ShuffleCard> cards, ShuffleCard card)
        {
            if (cards == null) return false;
            for (int i = 0; i < cards.Count; i++) if (ReferenceEquals(cards[i], card)) return true;
            return false;
        }
    }
}

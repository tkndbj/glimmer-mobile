using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove
{
    /// <summary>
    /// The read model the home screen paints: level, grove progress and balances.
    ///
    /// Nothing is stored here — it is a view over <see cref="PlayerProgression"/>,
    /// <see cref="Wallet"/> and the live catalog. Keeping it as a facade means the HUD
    /// has one place to ask, while progression, persistence and content each stay
    /// responsible for their own part.
    ///
    /// Note there are no setters for the balances any more. A settable balance is the
    /// exact shape of the bug the ledger exists to prevent: credits are earned by
    /// deriving them from cleared glades, granted by the server against a receipt, or
    /// spent through <see cref="PlayerProgression.TrySpend"/>. Assigning a number is
    /// none of those things.
    /// </summary>
    public static class Profile
    {
        public const int MaxHearts = Wallet.MaxHearts;

        public static string Name
        {
            get => Wallet.DisplayName;
            set => Wallet.DisplayName = value;
        }

        public static int Hearts
        {
            get => Wallet.Hearts;
            set => Wallet.Hearts = value;
        }

        // -- currency, derived from the ledger ----------------------------------
        public static long Coins => PlayerProgression.Credits;

        public static long Gems => PlayerProgression.Gems;

        public static bool CanAfford(long coins) => PlayerProgression.CanAfford(Currency.Credits, coins);

        public static bool Spend(long coins, string reason)
            => PlayerProgression.TrySpend(Currency.Credits, coins, reason);

        // -- level, derived from the star ledger and the reward table -----------
        public static PlayerLevel Level => PlayerProgression.Level;

        /// <summary>Kept as "rank" because that is what the HUD calls it.</summary>
        public static int Rank => PlayerProgression.Level.Level;

        /// <summary>0..1 through the current level.</summary>
        public static float RankProgress => PlayerProgression.Level.Progress01;

        public static long Xp => PlayerProgression.Xp;

        // -- grove completion, read straight from the star ledger ---------------
        static LevelCatalog Catalog => GameContent.Catalog;

        public static int TotalStars => PlayerProgress.TotalStars(Catalog);

        public static int MaxStars => PlayerProgress.MaxStars(Catalog);

        public static float GroveProgress => MaxStars == 0 ? 0f : TotalStars / (float)MaxStars;

        /// <summary>Which milestone chests (a third, two thirds, all) have been reached.</summary>
        public static bool MilestoneReached(int index)
            => GroveProgress >= (index + 1) / 3f - 0.0001f;

        /// <summary>Format 1250 as "1.2k" so pills stay narrow.</summary>
        public static string Short(long value)
        {
            if (value >= 1000000) return (value / 1000000f).ToString("0.#") + "M";
            if (value >= 10000) return (value / 1000f).ToString("0") + "k";
            return value.ToString();
        }
    }
}

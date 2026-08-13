using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The read model the home screen paints: rank, grove progress and balances.
    ///
    /// Nothing is stored here any more — it is a view over <see cref="PlayerProgress"/>,
    /// <see cref="Wallet"/> and the live catalog. Keeping it as a facade means the HUD
    /// has one place to ask, while persistence and content each stay responsible for
    /// their own half.
    /// </summary>
    public static class Profile
    {
        public const int MaxHearts = Wallet.MaxHearts;

        public static string Name
        {
            get => Wallet.DisplayName;
            set => Wallet.DisplayName = value;
        }

        public static int Coins
        {
            get => Wallet.Coins;
            set => Wallet.Coins = value;
        }

        public static int Gems
        {
            get => Wallet.Gems;
            set => Wallet.Gems = value;
        }

        public static int Hearts
        {
            get => Wallet.Hearts;
            set => Wallet.Hearts = value;
        }

        // -- real progression, read straight from the star ledger ---------------
        static LevelCatalog Catalog => GameContent.Catalog;

        public static int TotalStars => PlayerProgress.TotalStars(Catalog);

        public static int MaxStars => PlayerProgress.MaxStars(Catalog);

        /// <summary>Rank rises every three stars. Purely derived, nothing stored.</summary>
        public static int Rank => 1 + TotalStars / 3;

        /// <summary>0..1 through the current rank.</summary>
        public static float RankProgress => (TotalStars % 3) / 3f;

        public static float GroveProgress => MaxStars == 0 ? 0f : TotalStars / (float)MaxStars;

        /// <summary>Which milestone chests (a third, two thirds, all) have been reached.</summary>
        public static bool MilestoneReached(int index)
            => GroveProgress >= (index + 1) / 3f - 0.0001f;

        /// <summary>Format 1250 as "1.2k" so pills stay narrow.</summary>
        public static string Short(int value)
        {
            if (value >= 1000000) return (value / 1000000f).ToString("0.#") + "M";
            if (value >= 10000) return (value / 1000f).ToString("0") + "k";
            return value.ToString();
        }
    }
}

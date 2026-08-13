using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The player's account.
    ///
    /// Rank and grove progress are REAL — derived from stars actually earned.
    /// Hearts, coins and gems are PLACEHOLDERS for the economy that is not built
    /// yet: they persist and the UI reads them through this one type, so wiring up
    /// the real feature later means changing this file and nothing else.
    /// </summary>
    public static class Profile
    {
        const string KCoins = "gg.coins";
        const string KGems = "gg.gems";
        const string KHearts = "gg.hearts";
        const string KName = "gg.name";

        // -- placeholder starting balances ------------------------------------
        const int SeedCoins = 1250;
        const int SeedGems = 12;
        public const int MaxHearts = 5;

        public static string Name
        {
            get => PlayerPrefs.GetString(KName, "Grovekeeper");
            set { PlayerPrefs.SetString(KName, value); PlayerPrefs.Save(); }
        }

        public static int Coins
        {
            get => PlayerPrefs.GetInt(KCoins, SeedCoins);
            set { PlayerPrefs.SetInt(KCoins, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        public static int Gems
        {
            get => PlayerPrefs.GetInt(KGems, SeedGems);
            set { PlayerPrefs.SetInt(KGems, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        /// <summary>Lives. Placeholder: always full until the energy system exists.</summary>
        public static int Hearts
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(KHearts, MaxHearts), 0, MaxHearts);
            set { PlayerPrefs.SetInt(KHearts, Mathf.Clamp(value, 0, MaxHearts)); PlayerPrefs.Save(); }
        }

        // -- real progression, read straight from the star ledger ---------------
        public static int TotalStars => Save.TotalStars();
        public static int MaxStars => Levels.Count * 3;

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

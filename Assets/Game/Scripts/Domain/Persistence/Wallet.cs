using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// Soft currencies and the player's chosen name.
    ///
    /// The economy does not exist yet — these are seeded balances that nothing spends.
    /// They live in the save file anyway so that building the real feature later is a
    /// change to this one type, and never another migration of everybody's data.
    /// </summary>
    public static class Wallet
    {
        public const int SeedCoins = 1250;
        public const int SeedGems = 12;
        public const int MaxHearts = 5;
        public const string DefaultName = "Grovekeeper";

        static int _coins = SeedCoins;
        static int _gems = SeedGems;
        static int _hearts = MaxHearts;
        static string _name = DefaultName;

        public static int Coins
        {
            get => _coins;
            set { _coins = Mathf.Max(0, value); SaveService.Save(); }
        }

        public static int Gems
        {
            get => _gems;
            set { _gems = Mathf.Max(0, value); SaveService.Save(); }
        }

        public static int Hearts
        {
            get => _hearts;
            set { _hearts = Mathf.Clamp(value, 0, MaxHearts); SaveService.Save(); }
        }

        public static string DisplayName
        {
            get => string.IsNullOrEmpty(_name) ? DefaultName : _name;
            set { _name = value; SaveService.Save(); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var w = dto.wallet ?? WalletDto.Unwritten();

            // negative means the field was never written, so the seed applies
            _coins = w.coins < 0 ? SeedCoins : w.coins;
            _gems = w.gems < 0 ? SeedGems : w.gems;
            _hearts = w.hearts < 0 ? MaxHearts : Mathf.Clamp(w.hearts, 0, MaxHearts);
            _name = string.IsNullOrEmpty(w.displayName) ? DefaultName : w.displayName;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.wallet = new WalletDto
            {
                coins = _coins,
                gems = _gems,
                hearts = _hearts,
                displayName = _name,
            };
        }
    }
}

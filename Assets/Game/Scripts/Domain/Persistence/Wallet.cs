using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The stored half of the economy: one <see cref="CurrencyLedger"/> per currency,
    /// plus hearts and the player's chosen name.
    ///
    /// It deliberately does not know what a balance is. A balance needs the derived
    /// earnings, those come from the star ledger, and deriving them belongs to
    /// <c>GlimmerGrove.Progression</c> — so persistence stays a description of a file
    /// and never grows an opinion about how the game rewards people. Ask
    /// <c>PlayerProgression.Balance</c> for a number; ask this for the ledger behind it.
    /// </summary>
    public static class Wallet
    {
        public const int MaxHearts = 5;
        public const string DefaultName = "Grovekeeper";

        static readonly Dictionary<string, CurrencyLedger> _ledgers = new Dictionary<string, CurrencyLedger>();
        static readonly Dictionary<string, long> _legacyMirror = new Dictionary<string, long>();

        static int _hearts = MaxHearts;
        static string _name = DefaultName;

        /// <summary>The ledger for a currency, created empty rather than returning null.</summary>
        public static CurrencyLedger Ledger(string currency)
        {
            if (string.IsNullOrEmpty(currency)) currency = Currency.Credits;

            if (!_ledgers.TryGetValue(currency, out var ledger))
            {
                ledger = new CurrencyLedger(currency);
                _ledgers[currency] = ledger;
            }
            return ledger;
        }

        public static IEnumerable<CurrencyLedger> Ledgers => _ledgers.Values;

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

        /// <summary>
        /// Records the balance a currency currently shows, purely so the retired v1
        /// fields stay meaningful. Nothing reads it back in this build — it exists so
        /// that a player rolled back to a pre-ledger client sees their real balance
        /// rather than the starting seed.
        /// </summary>
        internal static void MirrorLegacyBalance(string currency, long balance)
        {
            if (string.IsNullOrEmpty(currency)) return;
            _legacyMirror[currency] = balance < 0 ? 0 : balance;
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _ledgers.Clear();
            _legacyMirror.Clear();

            var w = dto.wallet ?? WalletDto.Unwritten();

            if (w.currencies != null)
            {
                foreach (var ledgerDto in w.currencies)
                {
                    var ledger = CurrencyLedger.FromDto(ledgerDto);
                    if (ledger == null) continue;
                    _ledgers[ledger.Currency] = ledger;      // a duplicate entry keeps the last
                }
            }

            MigrateFlatBalance(Currency.Credits, w.coins);
            MigrateFlatBalance(Currency.Gems, w.gems);

            // negative means the field was never written, so the seed applies
            _hearts = w.hearts < 0 ? MaxHearts : Mathf.Clamp(w.hearts, 0, MaxHearts);
            _name = string.IsNullOrEmpty(w.displayName) ? DefaultName : w.displayName;
        }

        /// <summary>
        /// Turns a v1 flat balance into a granted baseline, exactly once.
        ///
        /// Whatever a player was holding becomes something they were <em>given</em>,
        /// which is the only reading that preserves the number while moving it into a
        /// model where balances are otherwise derived. A file that already has a
        /// ledger is left alone, so this cannot run twice and cannot double a balance
        /// — and because "no ledger yet" is also true of a brand-new save, a new
        /// account picks up its seed through the same path with no version check to
        /// get wrong.
        /// </summary>
        static void MigrateFlatBalance(string currency, int flatBalance)
        {
            if (_ledgers.ContainsKey(currency)) return;

            var ledger = new CurrencyLedger(currency);
            ledger.GrantLocally(flatBalance >= 0 ? flatBalance : Currency.SeedFor(currency));
            _ledgers[currency] = ledger;
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            var currencies = new CurrencyLedgerDto[_ledgers.Count];
            int i = 0;
            foreach (var ledger in _ledgers.Values) currencies[i++] = ledger.ToDto();

            dto.wallet = new WalletDto
            {
                coins = LegacyMirror(Currency.Credits),
                gems = LegacyMirror(Currency.Gems),
                hearts = _hearts,
                displayName = _name,
                currencies = currencies,
            };
        }

        static int LegacyMirror(string currency)
        {
            if (!_legacyMirror.TryGetValue(currency, out long balance)) return -1;
            return balance > int.MaxValue ? int.MaxValue : (int)balance;
        }
    }
}

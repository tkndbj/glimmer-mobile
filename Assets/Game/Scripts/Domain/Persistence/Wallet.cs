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
        /// <summary>Kept as an alias so callers need not learn a second name for it.</summary>
        public const int MaxHearts = HeartRules.Max;

        public const string DefaultName = "Grovekeeper";

        static readonly Dictionary<string, CurrencyLedger> _ledgers = new Dictionary<string, CurrencyLedger>();
        static readonly Dictionary<string, long> _legacyMirror = new Dictionary<string, long>();

        static Hearts _hearts = Hearts.Full;
        static long _heartBoostUntil;
        static string _name = DefaultName;
        static string _avatarId = string.Empty;

        /// <summary>
        /// Raised whenever the heart count changes, so a HUD can follow it without
        /// polling. Hearts move on a timer as well as on a defeat, which is exactly the
        /// case a screen cannot predict for itself.
        /// </summary>
        public static event System.Action<Hearts> HeartsChanged;

        /// <summary>
        /// When faster heart regeneration runs out, or 0 when none is running.
        ///
        /// Read rather than tested for "is it on", because the rule needs the deadline
        /// itself: a catch-up that spans the expiry has to pay some refills at the fast
        /// rate and the rest at the slow one. See <see cref="HeartRules.PeriodAt"/>.
        /// </summary>
        public static long HeartBoostUntilUnix => _heartBoostUntil;

        public static bool HeartBoostActive => _heartBoostUntil > GameClock.NowUnix();

        /// <summary>Seconds of boost left, for a countdown. 0 when none is running.</summary>
        public static long HeartBoostSecondsLeft
        {
            get
            {
                long left = _heartBoostUntil - GameClock.NowUnix();
                return left < 0 ? 0 : left;
            }
        }

        /// <summary>
        /// Starts, or extends, faster heart regeneration.
        ///
        /// Extends rather than replaces: a boost won while one is already running adds to
        /// it, because the alternative — the new one overwriting a longer remaining
        /// window — takes something away from a player for the crime of doing well twice.
        /// Capped at <see cref="HeartRules.MaxBoostHours"/> past now so no sequence of
        /// awards can stack into a permanent one.
        /// </summary>
        public static void GrantHeartBoost(long hours)
        {
            if (hours <= 0) return;

            long now = GameClock.NowUnix();
            long from = _heartBoostUntil > now ? _heartBoostUntil : now;
            long until = from + hours * 3600L;

            long ceiling = now + HeartRules.MaxBoostHours * 3600L;
            if (until > ceiling) until = ceiling;

            if (until <= _heartBoostUntil) return;

            _heartBoostUntil = until;

            // The running refill timer is shortened by the boost, and Hearts.At is what
            // knows by how much. Committing the caught-up state here means the countdown
            // on screen drops the moment the chest is opened rather than at the next read.
            Commit(_hearts.At(now, _heartBoostUntil));

            SaveService.Save();
            HeartsChanged?.Invoke(_hearts);
        }

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

        /// <summary>
        /// The player's hearts, brought up to date before you see them.
        ///
        /// Reading refills: hearts arrive on a clock, so any read that skipped the
        /// catch-up would be stale the moment a timer elapsed while a screen was open.
        /// The state is written back only when it actually changed, so reading in a
        /// HUD update loop does not spin the save file.
        ///
        /// There is deliberately no setter. Assigning a heart count is the same
        /// mistake as assigning a balance — hearts are spent by losing a run, granted
        /// by the server, or returned by the clock, and none of those is an assignment.
        /// </summary>
        public static Hearts Hearts
        {
            get
            {
                var refreshed = _hearts.At(GameClock.NowUnix(), _heartBoostUntil);
                if (refreshed.Count == _hearts.Count && refreshed.NextRefillUnix == _hearts.NextRefillUnix)
                    return _hearts;

                _hearts = refreshed;
                SaveService.Save();
                HeartsChanged?.Invoke(_hearts);
                return _hearts;
            }
        }

        /// <summary>
        /// Charges the player for a lost run. Returns false when there was nothing to
        /// take, which the caller must treat as "already out" rather than as a refusal.
        /// </summary>
        public static bool TrySpendHeart(int amount = HeartRules.DefeatCost)
        {
            long now = GameClock.NowUnix();

            var before = _hearts.At(now, _heartBoostUntil);
            if (!before.CanPlay) { Commit(before); return false; }

            Commit(before.Spend(amount, now, _heartBoostUntil));
            return true;
        }

        /// <summary>
        /// Adds hearts the player did not wait for: a refill purchase, a gift, or a
        /// server correction. Kept separate from spending so the two can be audited
        /// apart once hearts cost money.
        /// </summary>
        public static void GrantHearts(int amount)
            => Commit(_hearts.Grant(amount, GameClock.NowUnix(), _heartBoostUntil));

        static void Commit(Hearts next)
        {
            bool changed = next.Count != _hearts.Count || next.NextRefillUnix != _hearts.NextRefillUnix;
            _hearts = next;

            if (!changed) return;

            SaveService.Save();
            HeartsChanged?.Invoke(_hearts);
        }

        public static string DisplayName
        {
            get => string.IsNullOrEmpty(_name) ? DefaultName : _name;
            set { _name = value; SaveService.Save(); }
        }

        /// <summary>
        /// The chosen companion's permanent id, or empty when the player has never
        /// picked one.
        ///
        /// Returned raw on purpose. Which companions exist, which is the default and
        /// what an unknown id should fall back to are all questions about the roster,
        /// and this type does not have an opinion about the roster any more than it has
        /// one about how credits are earned — ask <c>AvatarCatalog.Resolve</c>.
        /// </summary>
        public static string AvatarId
        {
            get => _avatarId ?? string.Empty;
            set { _avatarId = value ?? string.Empty; SaveService.Save(); }
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

            // negative means the field was never written, so the seed applies. A file
            // from before hearts regenerated carries a count and no deadline; Hearts.At
            // starts the clock from the next read rather than back-paying the gap.
            _heartBoostUntil = w.heartBoostUntilUnix < 0 ? 0L : w.heartBoostUntilUnix;

            _hearts = w.hearts < 0
                ? Hearts.Full
                : new Hearts(w.hearts, w.heartsNextRefillUnix).At(GameClock.NowUnix(), _heartBoostUntil);

            _name = string.IsNullOrEmpty(w.displayName) ? DefaultName : w.displayName;
            _avatarId = w.avatarId ?? string.Empty;
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
                hearts = _hearts.Count,
                heartsNextRefillUnix = _hearts.NextRefillUnix,
                heartBoostUntilUnix = _heartBoostUntil,
                displayName = _name,
                avatarId = _avatarId,
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

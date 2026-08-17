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
        /// <summary>
        /// Where the refill timer stops — the denominator a HUD draws, and not a maximum.
        /// Kept as an alias so callers need not learn a second name for it.
        ///
        /// A property rather than a constant since the gate became content: a <c>const</c>
        /// is copied into every assembly that reads it at compile time, which is exactly
        /// the wrong shape for a number a config push is allowed to move.
        /// </summary>
        public static int MaxHearts => HeartRules.RefillCap;

        /// <summary>The most hearts anybody may hold. See <see cref="HeartRules.Ceiling"/>.</summary>
        public static int HeartCeiling => HeartRules.Ceiling;

        public const string DefaultName = "Grovekeeper";

        static readonly Dictionary<string, CurrencyLedger> _ledgers = new Dictionary<string, CurrencyLedger>();
        static readonly Dictionary<string, long> _legacyMirror = new Dictionary<string, long>();

        static Hearts _hearts = Hearts.Full;
        static long _heartBoostUntil;

        // Empty until the player chooses, never DefaultName — see WalletDto.displayName.
        static string _name = string.Empty;
        static long _nameSetUnix;
        static string _avatarId = string.Empty;
        static long _avatarSetUnix;

        /// <summary>
        /// Raised whenever the heart count changes, so a HUD can follow it without
        /// polling. Hearts move on a timer as well as on a defeat, which is exactly the
        /// case a screen cannot predict for itself.
        /// </summary>
        public static event System.Action<Hearts> HeartsChanged;

        /// <summary>
        /// Raised when the player changes their name or their worn companion.
        ///
        /// <para>
        /// The two preferences in this file, and the only things in it a player edits
        /// directly rather than earns. They are worth announcing because they want a push
        /// of their own: everything else here reaches the server on the next background
        /// sync and nobody notices the delay, whereas a rename is a change the player made
        /// deliberately and expects to survive the next thing they do — which, on a phone,
        /// is quite often uninstalling the game. <c>Boot</c> hangs
        /// <c>CloudSaveService.RequestSync</c> on this, so no call site has to remember.
        /// </para>
        /// </summary>
        public static event System.Action ProfileChanged;

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

                // The whole ledger is compared, not the count on screen. Reaching the cap
                // advances the refill deadline without moving the count, and a device that
                // did not persist that would merge against a deadline it had already
                // passed. Hearts.Equals is the ledger comparison for exactly this reason.
                if (refreshed == _hearts) return _hearts;

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
        /// <summary>
        /// Charges the player what a lost run costs, which is content now — so this is an
        /// overload rather than a default argument. A default is baked in at the call site
        /// at compile time and would have quietly gone on charging one heart forever after
        /// the published cost changed.
        /// </summary>
        public static bool TrySpendHeart() => TrySpendHeart(HeartRules.DefeatCost);

        public static bool TrySpendHeart(int amount)
        {
            long now = GameClock.NowUnix();

            var before = _hearts.At(now, _heartBoostUntil);
            if (!before.CanPlay) { Commit(before); return false; }

            Commit(before.Spend(amount, now, _heartBoostUntil));
            return true;
        }

        /// <summary>
        /// Adds hearts the player did not wait for: a chest, a streak night, a watched
        /// video, a gift, or a server correction. Kept separate from spending so the two
        /// can be audited apart once hearts cost money.
        ///
        /// These stack past <see cref="MaxHearts"/> up to <see cref="HeartCeiling"/> —
        /// see <see cref="Hearts.Grant"/> for why the timer's cap and the holding ceiling
        /// are two different numbers.
        /// </summary>
        public static void GrantHearts(int amount)
            => Commit(_hearts.Grant(amount, GameClock.NowUnix(), _heartBoostUntil));

        static void Commit(Hearts next)
        {
            bool changed = next != _hearts;
            _hearts = next;

            if (!changed) return;

            SaveService.Save();
            HeartsChanged?.Invoke(_hearts);
        }

        /// <summary>
        /// What the keeper is called: the chosen name, or <see cref="DefaultName"/> when
        /// there is none.
        ///
        /// <para>
        /// The fallback happens on the way out and never on the way in. A device that has
        /// not been renamed stores nothing, so the merge can tell it apart from one that
        /// has — which is the whole of why a rename now survives a second device. Setting
        /// this stamps <see cref="NameSetUnix"/>, because the stamp is what decides the
        /// merge and a value written without one is a choice nothing can date.
        /// </para>
        /// </summary>
        public static string DisplayName
        {
            get => string.IsNullOrEmpty(_name) ? DefaultName : _name;
            set => SetDisplayName(value, GameClock.NowUnix());
        }

        /// <summary>True once the player has picked a name of their own.</summary>
        public static bool HasChosenName => !string.IsNullOrEmpty(_name);

        /// <summary>When the name was chosen, or 0 when it never was.</summary>
        public static long NameSetUnix => _nameSetUnix;

        /// <summary>When the companion was chosen, or 0 when it never was.</summary>
        public static long AvatarSetUnix => _avatarSetUnix;

        /// <summary>
        /// Records a chosen name at a given moment. The timestamp is a parameter so the
        /// tests can drive it; every call site in the game passes now.
        /// </summary>
        internal static void SetDisplayName(string name, long atUnix)
        {
            string chosen = name ?? string.Empty;
            if (string.Equals(chosen, _name, System.StringComparison.Ordinal)) return;

            _name = chosen;
            _nameSetUnix = atUnix;

            SaveService.Save();
            ProfileChanged?.Invoke();
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
            set => SetAvatarId(value, GameClock.NowUnix());
        }

        /// <summary>Records a worn companion at a given moment. See <see cref="SetDisplayName"/>.</summary>
        internal static void SetAvatarId(string avatarId, long atUnix)
        {
            string chosen = avatarId ?? string.Empty;
            if (string.Equals(chosen, _avatarId, System.StringComparison.Ordinal)) return;

            _avatarId = chosen;
            _avatarSetUnix = atUnix;

            SaveService.Save();
            ProfileChanged?.Invoke();
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

            _hearts = ReadHearts(w).At(GameClock.NowUnix(), _heartBoostUntil);

            _name = ReadChosenName(w);
            _nameSetUnix = w.displayNameSetUnix < 0 ? 0L : w.displayNameSetUnix;
            _avatarId = w.avatarId ?? string.Empty;
            _avatarSetUnix = w.avatarSetUnix < 0 ? 0L : w.avatarSetUnix;
        }

        /// <summary>
        /// The chosen name, with the one ambiguity a pre-v15 file leaves resolved.
        ///
        /// <para>
        /// Builds before v15 stored <see cref="DefaultName"/> whenever the player had not
        /// picked anything, so an unstamped file holding it means either "never chosen" or
        /// "chosen, and it happened to be the default". They cannot be told apart, and
        /// reading it as never-chosen is the safe half: the player still sees Grovekeeper,
        /// and the merge stops treating a device that has never been renamed as one with
        /// an opinion — which is the bug the version exists to end. A stamped file is
        /// believed exactly as written, default or not.
        /// </para>
        /// </summary>
        static string ReadChosenName(WalletDto w)
        {
            string stored = w.displayName ?? string.Empty;
            if (stored.Length == 0) return string.Empty;

            if (w.displayNameSetUnix <= 0 && string.Equals(stored, DefaultName, System.StringComparison.Ordinal))
                return string.Empty;

            return stored;
        }

        /// <summary>
        /// Reads the heart ledger, upgrading a pre-v8 file on the way through.
        ///
        /// <para>
        /// An older file carries a count and a deadline and no history at all, so the
        /// count becomes the whole of <c>produced</c> against nothing spent. That is the
        /// only reading available and it is the right one: it preserves exactly what the
        /// player was holding, and the invariants hold trivially because a count is never
        /// above the cap. Every heart spent before the upgrade is simply forgotten, which
        /// costs nothing — only the difference between the two counters is ever read.
        /// </para>
        /// </summary>
        static Hearts ReadHearts(WalletDto w)
        {
            // > 0, never >= 0: JsonUtility writes a zero into a field a v7 file never had,
            // so a ledger has to announce itself with a value nothing else can produce.
            // See WalletDto.heartsProduced for why zero is unreachable for a real one.
            if (w.heartsProduced > 0)
                return Hearts.Ledger(w.heartsProduced, w.heartsSpent, w.heartsDueUnix);

            if (w.hearts < 0) return Hearts.Full;                  // never written: seed a full set

            return new Hearts(w.hearts, w.heartsNextRefillUnix);
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
                // The ledger, which is what anything merging reads.
                heartsProduced = _hearts.Produced,
                heartsSpent = _hearts.Spent,
                heartsDueUnix = _hearts.DueUnix,

                // and its derived mirror, for a client rolled back to a pre-v8 build.
                // Written, never read back while the ledger is present.
                hearts = _hearts.Count,
                heartsNextRefillUnix = _hearts.NextRefillUnix,

                heartBoostUntilUnix = _heartBoostUntil,

                // Written raw, so "" survives to the file and to the server. Substituting
                // DefaultName here is what used to make an unnamed device look like a
                // renamed one, and it cost players the name they had chosen.
                displayName = _name,
                displayNameSetUnix = _nameSetUnix,
                avatarId = _avatarId,
                avatarSetUnix = _avatarSetUnix,
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

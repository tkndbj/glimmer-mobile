using System;
using System.Collections.Generic;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The permanent ids of the game's currencies.
    ///
    /// Ids, not an enum: a save file and a server document both key on these, and an
    /// enum's meaning moves the moment somebody reorders the members. Same rule as a
    /// <see cref="Content.LevelId"/> — once shipped, never renamed or reused.
    /// </summary>
    public static class Currency
    {
        public const string Credits = "credits";
        public const string Gems = "gems";

        public static readonly string[] All = { Credits, Gems };

        /// <summary>
        /// What a brand-new account is granted. Applied once, at creation, so changing
        /// these affects new players only — which is the correct behaviour for a seed.
        /// Once the backend is live these move server-side with everything else that
        /// grants currency.
        /// </summary>
        public const long SeedCredits = 1250;
        public const long SeedGems = 12;

        public static long SeedFor(string currency)
            => currency == Gems ? SeedGems : currency == Credits ? SeedCredits : 0L;
    }

    /// <summary>One debit, identified so a retry cannot charge twice.</summary>
    public sealed class SpendEntry
    {
        public readonly string Id;
        public readonly long Amount;
        public readonly long Unix;
        public readonly string Reason;

        public SpendEntry(string id, long amount, long unix, string reason)
        {
            Id = id;
            Amount = amount;
            Unix = unix;
            Reason = reason ?? string.Empty;
        }

        /// <summary>A fresh idempotency key. Never derived from anything reusable.</summary>
        public static string NewId() => Guid.NewGuid().ToString("N");

        public SpendEntryDto ToDto()
            => new SpendEntryDto { id = Id, amount = Amount, unix = Unix, reason = Reason };

        public static bool TryFromDto(SpendEntryDto dto, out SpendEntry entry)
        {
            entry = null;
            if (dto == null || string.IsNullOrEmpty(dto.id)) return false;
            if (dto.amount <= 0) return false;

            entry = new SpendEntry(dto.id, dto.amount, dto.unix, dto.reason);
            return true;
        }
    }

    /// <summary>
    /// Double-entry state for one currency.
    ///
    /// <code>balance = max(derived earned, earned high-water) + granted - spent</code>
    ///
    /// Only the terms that cannot be derived are stored, and each of them is either
    /// monotonic or owned by the server:
    ///
    /// <list type="bullet">
    /// <item><b>earned</b> is recomputed from the star ledger every time. Nothing to
    /// forge and nothing to double-count. The high-water mark under it exists only so
    /// that retuning a reward, or a chapter briefly leaving the catalog, can never
    /// reduce a balance somebody is already holding.</item>
    /// <item><b>granted</b> covers the seed, purchases and gifts. Once the backend is
    /// live the client may never raise it — a client that can grant itself currency is
    /// a client that can print money, and with real purchases in play that is the one
    /// field an attacker actually wants.</item>
    /// <item><b>spent</b> is a server-confirmed baseline plus the debits made since the
    /// last sync, each carrying an idempotency key. A bare counter cannot work here:
    /// merging two devices by taking the larger counter silently forgives a spend, and
    /// by summing them charges twice. A set of identified entries merges by union and
    /// is correct under both.</item>
    /// </list>
    /// </summary>
    public sealed class CurrencyLedger
    {
        readonly List<SpendEntry> _pending = new List<SpendEntry>();

        public CurrencyLedger(string currency) => Currency = currency;

        public string Currency { get; }

        public long GrantedBaseline { get; private set; }
        public long SpentBaseline { get; private set; }
        public long EarnedHighWater { get; private set; }

        public IReadOnlyList<SpendEntry> PendingSpends => _pending;

        /// <summary>Debits not yet confirmed by the server, but already charged locally.</summary>
        public long PendingSpend
        {
            get
            {
                long total = 0;
                for (int i = 0; i < _pending.Count; i++) total += _pending[i].Amount;
                return total;
            }
        }

        public long Spent => SpentBaseline + PendingSpend;

        /// <summary>Derived earnings, floored by the high-water mark.</summary>
        public long EarnedFrom(long derivedEarned)
            => derivedEarned > EarnedHighWater ? derivedEarned : EarnedHighWater;

        /// <summary>
        /// The spendable balance. Clamped at zero: a negative balance is not a state
        /// the game should ever show, and if one is ever computed the cause is a bug
        /// worth surviving rather than propagating into a shop.
        /// </summary>
        public long BalanceFrom(long derivedEarned)
        {
            long balance = EarnedFrom(derivedEarned) + GrantedBaseline - Spent;
            return balance < 0 ? 0 : balance;
        }

        // ------------------------------------------------------------- writing
        /// <summary>Raises the floor under derived earnings. Returns true when it moved.</summary>
        public bool RaiseEarnedHighWater(long derivedEarned)
        {
            if (derivedEarned <= EarnedHighWater) return false;
            EarnedHighWater = derivedEarned;
            return true;
        }

        /// <summary>
        /// Records a debit if the player can afford it.
        ///
        /// Optimistic on purpose — the charge lands locally at once so a shop stays
        /// responsive offline — but it carries an idempotency key, so submitting it to
        /// the server later, possibly more than once, still debits exactly one time.
        /// </summary>
        public bool TrySpend(long amount, long derivedEarned, string reason, out SpendEntry entry)
        {
            entry = null;
            if (amount <= 0) return false;
            if (BalanceFrom(derivedEarned) < amount) return false;

            entry = new SpendEntry(SpendEntry.NewId(), amount, SaveSchema.NowUnix(), reason);
            _pending.Add(entry);
            return true;
        }

        /// <summary>
        /// Grants currency locally. Only legitimate before the backend is live, and
        /// for the account seed; every other grant must come from the server so that a
        /// purchase is validated against a receipt rather than a client's word.
        /// </summary>
        public void GrantLocally(long amount)
        {
            if (amount <= 0) return;
            GrantedBaseline += amount;
        }

        /// <summary>
        /// Adopts the server's view.
        ///
        /// Baselines are taken, not maxed: a refund or a chargeback legitimately lowers
        /// what was granted, and a client that only ever raised its own numbers could
        /// not represent that. Pending debits the server has confirmed are dropped,
        /// because they are inside <paramref name="spentBaseline"/> now and counting
        /// them again would charge the player twice for one purchase.
        /// </summary>
        public void ApplyServerState(long grantedBaseline, long spentBaseline,
                                     ICollection<string> confirmedSpendIds, long confirmedThroughUnix,
                                     long earnedFloor = 0)
        {
            GrantedBaseline = grantedBaseline < 0 ? 0 : grantedBaseline;
            SpentBaseline = spentBaseline < 0 ? 0 : spentBaseline;

            if (confirmedThroughUnix > ConfirmedThroughUnix) ConfirmedThroughUnix = confirmedThroughUnix;

            // The server keeps its own floor under derived earnings, and it is the one
            // that governs what can actually be spent. Adopting it — rather than each
            // side keeping its own — is what stops the game showing a balance the
            // server will not let the player use.
            RaiseEarnedHighWater(earnedFloor);

            if (confirmedSpendIds == null || confirmedSpendIds.Count == 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
                if (confirmedSpendIds.Contains(_pending[i].Id)) _pending.RemoveAt(i);
        }

        /// <summary>
        /// Folds another device's ledger in, losslessly.
        ///
        /// Baselines take the larger value and pending debits take the union by id,
        /// which makes this a join: applying it twice changes nothing, and the order
        /// two devices are merged in cannot change the result. That is what allows the
        /// sync to merge silently instead of asking a player to choose a save and
        /// throwing the other one away.
        /// </summary>
        public void MergeFrom(CurrencyLedger other)
        {
            if (other == null) return;

            if (other.GrantedBaseline > GrantedBaseline) GrantedBaseline = other.GrantedBaseline;
            if (other.SpentBaseline > SpentBaseline) SpentBaseline = other.SpentBaseline;
            if (other.EarnedHighWater > EarnedHighWater) EarnedHighWater = other.EarnedHighWater;
            if (other.ConfirmedThroughUnix > ConfirmedThroughUnix)
                ConfirmedThroughUnix = other.ConfirmedThroughUnix;

            for (int i = 0; i < other._pending.Count; i++)
            {
                var entry = other._pending[i];
                if (HasPending(entry.Id)) continue;
                _pending.Add(entry);
            }

            // A debit already folded into the baseline must not also sit in the queue.
            PruneConfirmedPending();
        }

        public bool HasPending(string id)
        {
            for (int i = 0; i < _pending.Count; i++)
                if (string.Equals(_pending[i].Id, id, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Drops pending debits older than the confirmed baseline's cut-off.
        ///
        /// Only reachable through a merge, where one device may still hold a debit the
        /// other has already had confirmed. Without an id list from the server the
        /// timestamp is the best available signal, so this is deliberately
        /// conservative: it removes nothing unless a baseline exists to have absorbed
        /// it, preferring to charge late over charging twice.
        /// </summary>
        void PruneConfirmedPending()
        {
            if (ConfirmedThroughUnix <= 0) return;

            for (int i = _pending.Count - 1; i >= 0; i--)
                if (_pending[i].Unix <= ConfirmedThroughUnix) _pending.RemoveAt(i);
        }

        /// <summary>
        /// Debits at or before this moment are inside <see cref="SpentBaseline"/>.
        /// Set by the backend on every sync; zero means nothing has been confirmed.
        /// </summary>
        public long ConfirmedThroughUnix { get; set; }

        // --------------------------------------------------- file bridge (internal)
        internal CurrencyLedgerDto ToDto()
        {
            var entries = new SpendEntryDto[_pending.Count];
            for (int i = 0; i < _pending.Count; i++) entries[i] = _pending[i].ToDto();

            return new CurrencyLedgerDto
            {
                currency = Currency,
                grantedBaseline = GrantedBaseline,
                spentBaseline = SpentBaseline,
                earnedHighWater = EarnedHighWater,
                pendingSpends = entries,
                confirmedThroughUnix = ConfirmedThroughUnix,
            };
        }

        internal static CurrencyLedger FromDto(CurrencyLedgerDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.currency)) return null;

            var ledger = new CurrencyLedger(dto.currency)
            {
                GrantedBaseline = dto.grantedBaseline < 0 ? 0 : dto.grantedBaseline,
                SpentBaseline = dto.spentBaseline < 0 ? 0 : dto.spentBaseline,
                EarnedHighWater = dto.earnedHighWater < 0 ? 0 : dto.earnedHighWater,
                ConfirmedThroughUnix = dto.confirmedThroughUnix < 0 ? 0 : dto.confirmedThroughUnix,
            };

            if (dto.pendingSpends != null)
            {
                foreach (var entryDto in dto.pendingSpends)
                {
                    if (!SpendEntry.TryFromDto(entryDto, out var entry)) continue;
                    if (ledger.HasPending(entry.Id)) continue;   // a duplicated key charges once
                    ledger._pending.Add(entry);
                }
            }

            return ledger;
        }
    }
}

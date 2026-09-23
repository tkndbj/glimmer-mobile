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

        /// <summary>
        /// A season pass's debit: <c>pass:{seasonId}</c>.
        ///
        /// <para>
        /// The one spend id in this game that is <b>derived</b> rather than random, and for
        /// the reason invariant 10a gives about awards, read across to a debit: the server
        /// has to recognise this one. <c>submitSpends</c> turns it into the entitlement that
        /// gates a paid-track chest, in the same transaction that takes the gems, so the
        /// purchase and the permission cannot come apart.
        /// </para>
        /// <para>
        /// It is also what makes buying twice unrepresentable: the spend document is keyed by
        /// this id, so a resubmission confirms rather than charging again.
        /// </para>
        /// </summary>
        public static string SeasonPassId(string seasonId) => "pass:" + seasonId;

        /// <summary>
        /// The season a pass debit names, or null for any other id. The inverse of
        /// <see cref="SeasonPassId"/>, so a refused pass debit can find the season it was for.
        /// </summary>
        public static string SeasonOfPassId(string id)
            => !string.IsNullOrEmpty(id) && id.StartsWith("pass:", StringComparison.Ordinal)
               && id.Length > 5
                ? id.Substring(5)
                : null;

        /// <summary>What a support reader sees against a pass debit.</summary>
        public const string SeasonPassReason = "season_pass";

        /// <summary>
        /// A streak shield's debit: <c>shield:{dayKey}</c>.
        ///
        /// <para>
        /// The second derived spend id here, and it is derived for only <em>half</em> of the
        /// pass's reason. The server does not have to recognise it — a shield grants no
        /// currency and no permission, it only keeps a streak alive across days nobody
        /// played, and a protected streak still collects at most one night per calendar day.
        /// What the derivation buys is the other half: two devices that both buy on the same
        /// day while offline write byte-identical entries, the union keeps one, and the
        /// player is charged once. A fresh guid on each would charge twice for one window.
        /// </para>
        /// <para>
        /// The day is in the id rather than out of it because the day <em>is</em> the
        /// entitlement (<c>StreakStateDto.shieldFromDay</c>): one window, one debit.
        /// </para>
        /// </summary>
        public static string StreakShieldId(int dayKey) => "shield:" + dayKey;

        /// <summary>What a support reader sees against a shield debit.</summary>
        public const string StreakShieldReason = "streak_shield";

        /// <summary>
        /// A challenge deal's debit: <c>chaltier:{tierId}:{fromDay}</c>.
        ///
        /// <para>
        /// Derived for <em>both</em> of the pass's reasons. The server has to recognise it:
        /// <c>submitSpends</c> prices it against the published deal and writes the day onto the
        /// wallet document in the same transaction that takes the gems, and that server-held
        /// date is what every coin claim past the free allowance is bounded by. And two devices
        /// buying the same deal offline on one day write byte-identical entries, the union keeps
        /// one, and the player is charged once (48e).
        /// </para>
        /// <para>
        /// The day is in the id because the day <em>is</em> the entitlement: one window, one
        /// debit. A second purchase of the same deal on a later day is a different string and a
        /// different window. Parsed back by <c>parseChallengeTierSpendId</c> on the server; the
        /// format is a wire contract.
        /// </para>
        /// </summary>
        public static string ChallengeTierId(string tierId, int fromDay) => "chaltier:" + tierId + ":" + fromDay;

        /// <summary>The deal and the day a tier debit names, or false for any other id.</summary>
        public static bool TryParseChallengeTierId(string id, out string tierId, out int fromDay)
        {
            tierId = null;
            fromDay = 0;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("chaltier:", StringComparison.Ordinal)) return false;

            var parts = id.Split(':');
            if (parts.Length != 3 || parts[1].Length == 0) return false;
            if (!int.TryParse(parts[2], out int day) || day <= 0 || day.ToString() != parts[2]) return false;

            tierId = parts[1];
            fromDay = day;
            return true;
        }

        /// <summary>What a support reader sees against a deal debit.</summary>
        public const string ChallengeTierReason = "challenge_tier";

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
    /// One award, identified so that granting it twice is impossible rather than unlikely.
    ///
    /// <para>
    /// Unlike a <see cref="SpendEntry"/>, the id here is <b>derived from what earned it</b>
    /// rather than generated fresh. Two purchases of the same thing are two purchases and
    /// need two keys; two claims of the same daily chest are one award and must share one.
    /// That single decision is what makes the whole grant path safe under a merge: two
    /// devices that both open Tuesday's third chest offline produce byte-identical
    /// entries, the union keeps one, and no arithmetic anywhere has to notice.
    /// </para>
    /// </summary>
    public sealed class GrantEntry
    {
        public readonly string Id;
        public readonly long Amount;
        public readonly long Unix;
        public readonly string Reason;

        public GrantEntry(string id, long amount, long unix, string reason)
        {
            Id = id;
            Amount = amount;
            Unix = unix;
            Reason = reason ?? string.Empty;
        }

        /// <summary>
        /// The key for one currency out of one daily chest.
        ///
        /// Written out here rather than composed at each call site because the server
        /// derives the same string, and a format that exists in two places with two
        /// spellings is a format that will one day grant a chest twice.
        /// </summary>
        public static string DailyChestId(int dayKey, int chestIndex, string currency)
            => $"daily:{dayKey}:{chestIndex}:{currency}";

        /// <summary>What every daily chest grant records as its cause.</summary>
        public const string DailyChestReason = "daily_chest";

        /// <summary>
        /// The key for one night of the streak ladder.
        ///
        /// <para>
        /// Two numbers, and each is carrying a different job. The <b>calendar day</b> is the
        /// identity: a streak has exactly one night per day, so a day names a payout
        /// uniquely and two devices collecting the same night produce the same string. The
        /// <b>night</b> is what selects the rung, because the server pays from its own copy
        /// of the ladder and has to know which position to read.
        /// </para>
        /// <para>
        /// The day cannot be left out and the night cannot be used in its place, however
        /// much tidier that would be. A night number is relative to <c>startDay</c>, and
        /// <c>startDay</c> moves under a merge — so the same evening can be night five on
        /// one device and night four on another, which would key two documents and pay
        /// twice. A calendar day is the same number everywhere.
        /// </para>
        /// <para>
        /// Parsed back by <c>parseStreakClaim</c> on the server. The format is a wire
        /// contract; changing it re-opens every night a player has already been paid for.
        /// </para>
        /// </summary>
        public static string StreakNightId(int dayKey, int night, string currency)
            => $"streak:{dayKey}:{night}:{currency}";

        /// <summary>What every streak night grant records as its cause.</summary>
        public const string StreakNightReason = "streak_night";

        /// <summary>
        /// A task's chest: <c>task:{period}:{key}:{taskId}:{currency}</c>.
        ///
        /// Derived from what earned it, for <see cref="DailyChestId"/>'s reason, and parsed
        /// back by <c>functions/src/tasks.ts</c>, which re-rolls the chest from the same three
        /// facts. The period is spelt out rather than inferred from the key's size, because a
        /// day key and a week key are both small integers and a database key must not depend
        /// on which decade it is.
        /// </summary>
        public static string TaskChestId(Tasks.TaskPeriod period, int key, string taskId, string currency)
            => $"task:{Tasks.TaskPeriods.Id(period)}:{key}:{taskId}:{currency}";

        public const string TaskChestReason = "task_chest";

        /// <summary>
        /// A season rung's chest: <c>mark:{seasonId}:{track}:{goal}:{currency}</c>.
        ///
        /// <para>
        /// Derived from what earned it, for <see cref="DailyChestId"/>'s reason, and parsed
        /// back by <c>functions/src/season.ts</c>, which re-rolls the chest from the same
        /// facts. The <em>goal</em> names the rung rather than its position, because a
        /// position moves when a ladder is retuned and a goal does not — the same argument
        /// that keeps a level record keyed on an id rather than an order (invariant 1).
        /// </para>
        /// <para>
        /// The track is spelt out because the two tracks pay different chests at the same
        /// goal, and an id that could not tell them apart would let one claim collect both.
        /// </para>
        /// </summary>
        public static string MarkChestId(string seasonId, Events.SeasonTrack track, int goal,
                                          string currency)
            => $"mark:{seasonId}:{Events.SeasonTracks.Id(track)}:{goal}:{currency}";

        public const string MarkChestReason = "mark_chest";

        /// <summary>
        /// One run of the Infinite lane, in credits: <c>endless:{dayKey}:{paidBefore}:{currency}</c>.
        ///
        /// <para>
        /// <b>The running total is part of the identity, and that is the whole design.</b> Every
        /// other id here names a thing the server can re-price for itself - a chest, a night, a
        /// rung - and this one cannot, because a wave count comes out of a run no server saw
        /// (invariant 13's fourth clause, and 19l). So the id states what the day had already
        /// paid when this run ended, which lets the server bound the claim by reading it: a
        /// payment is only honoured while <c>paidBefore + amount</c> stays inside the day's
        /// ceiling, and the ceiling it is checked against is the server's own copy rather than
        /// this one.
        /// </para>
        /// <para>
        /// <b>It is still derived from what earned it</b>, so two devices banking the same run on
        /// the same day mint the same string and are paid once (invariant 10a). They can also be
        /// paid <em>less</em> than the two runs were worth, which is the deliberate direction:
        /// this shape can never pay twice and can occasionally pay once.
        /// </para>
        /// <para>
        /// Parsed back by <c>parseEndlessClaim</c> on the server. The format is a wire contract;
        /// changing it re-opens every run a player has already been paid for.
        /// </para>
        /// </summary>
        public static string EndlessWavesId(int dayKey, int paidBefore, string currency)
            => $"endless:{dayKey}:{paidBefore}:{currency}";

        /// <summary>What every Infinite-lane grant records as its cause.</summary>
        public const string EndlessWavesReason = "endless_waves";

        /// <summary>
        /// One cleared daily challenge, in credits: <c>chal:{dayKey}:{genre}:{win}:{currency}</c>.
        ///
        /// <para>
        /// Derived from what earned it, for <see cref="DailyChestId"/>'s reason: the <c>win</c>
        /// is the ordinal of the win within the day for that genre — the first win of the day is
        /// <c>1</c> — so two devices that both clear the day's first level mint one string and
        /// are paid once, and a replay of a level already won is not a second payment.
        /// </para>
        /// <para>
        /// The server re-prices it from the published rate and <em>bounds the ordinal</em> by the
        /// allowance it sold this account for that day (<c>challenges.ts</c>): a claim naming a
        /// third win under a free allowance of two is worth nothing unless a deal the server
        /// recorded covers the day. The format is a wire contract.
        /// </para>
        /// </summary>
        public static string ChallengeClearId(int dayKey, string genre, int win, string currency)
            => $"chal:{dayKey}:{genre}:{win}:{currency}";

        /// <summary>What every challenge grant records as its cause.</summary>
        public const string ChallengeClearReason = "challenge_clear";

        public GrantEntryDto ToDto()
            => new GrantEntryDto { id = Id, amount = Amount, unix = Unix, reason = Reason };

        public static bool TryFromDto(GrantEntryDto dto, out GrantEntry entry)
        {
            entry = null;
            if (dto == null || string.IsNullOrEmpty(dto.id)) return false;
            if (dto.amount <= 0) return false;

            entry = new GrantEntry(dto.id, dto.amount, dto.unix, dto.reason);
            return true;
        }
    }

    /// <summary>
    /// Double-entry state for one currency.
    ///
    /// <code>balance = max(derived earned, earned high-water) + granted + unconfirmed awards - spent</code>
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
    /// field an attacker actually wants. Awards made while offline therefore land in a
    /// separate queue of identified entries, exactly as debits do, and are folded into
    /// this field <em>by the server</em> and never by this code.</item>
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
        readonly List<GrantEntry> _pendingGrants = new List<GrantEntry>();

        public CurrencyLedger(string currency) => Currency = currency;

        public string Currency { get; }

        /// <summary>
        /// A pending debit the server refused has just been dropped from a ledger: the
        /// currency and the debit's id. Raised after the entry and its money are gone, from
        /// <see cref="ApplyServerState"/>. Static rather than per ledger because the thing
        /// listening is whatever the debit bought, which knows its own id and not its ledger.
        /// </summary>
        public static event Action<string, string> SpendRejected;

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

        public IReadOnlyList<GrantEntry> PendingGrants => _pendingGrants;

        /// <summary>Awards applied locally but not yet confirmed by the server.</summary>
        public long PendingGrant
        {
            get
            {
                long total = 0;
                for (int i = 0; i < _pendingGrants.Count; i++) total += _pendingGrants[i].Amount;
                return total;
            }
        }

        /// <summary>Everything given: what the server has confirmed, plus what is in flight.</summary>
        public long Granted => GrantedBaseline + PendingGrant;

        /// <summary>Derived earnings, floored by the high-water mark.</summary>
        public long EarnedFrom(long derivedEarned)
            => derivedEarned > EarnedHighWater ? derivedEarned : EarnedHighWater;

        /// <summary>
        /// The spendable balance. Clamped at zero: a negative balance is not a state
        /// the game should ever show, and if one is ever computed the cause is a bug
        /// worth surviving rather than propagating into a shop.
        ///
        /// <para>
        /// Unconfirmed awards count. A daily chest opened on a plane has to be spendable
        /// on that plane, and the alternative — showing the reward and withholding it
        /// until a sync — is a game that appears to be broken to anyone with a bad
        /// connection. The risk it carries is bounded and one-directional: the server can
        /// only ever revise such an award <em>down</em>, and it does so by replacing the
        /// baseline rather than by taking anything back.
        /// </para>
        /// </summary>
        public long BalanceFrom(long derivedEarned)
        {
            long balance = EarnedFrom(derivedEarned) + Granted - Spent;
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
            => TrySpend(amount, derivedEarned, reason, SpendEntry.NewId(), out entry);

        /// <summary>
        /// The same debit under an id the caller chose, for a purchase the <em>server</em> has
        /// to be able to recognise.
        ///
        /// <para>
        /// A spend id is random by default and that is right for almost everything: the id is
        /// only an idempotency key, and two purchases of the same turret are two debits. A
        /// season pass is the exception — it is bought once, for ever, and the server turns
        /// that one debit into the entitlement that gates a currency payout, so it has to be
        /// able to say <em>which</em> spend it is looking at. See
        /// <see cref="SpendEntry.SeasonPassId"/>.
        /// </para>
        /// <para>
        /// A duplicate is refused rather than queued twice. The pending list is the only place
        /// this can check — a confirmed spend is inside <see cref="SpentBaseline"/> and its id
        /// is gone — so it is a guard against a double tap rather than against a repurchase,
        /// and the caller's own entitlement check is what stops the second buy.
        /// </para>
        /// </summary>
        public bool TrySpend(long amount, long derivedEarned, string reason, string id,
                             out SpendEntry entry)
        {
            entry = null;
            if (amount <= 0 || string.IsNullOrEmpty(id)) return false;
            if (BalanceFrom(derivedEarned) < amount) return false;

            for (int i = 0; i < _pending.Count; i++)
                if (string.Equals(_pending[i].Id, id, StringComparison.Ordinal)) return false;

            entry = new SpendEntry(id, amount, SaveSchema.NowUnix(), reason);
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
        /// Queues an award under a key that identifies what earned it.
        ///
        /// <para>
        /// Returns false when an entry with that id is already held, which is the whole
        /// mechanism: a chest that has been opened cannot be opened again, on this device
        /// or on any other, because the second attempt collides with the first. The caller
        /// does not have to remember anything and no separate "already claimed" flag can
        /// drift out of step with the money.
        /// </para>
        /// <para>
        /// It also returns false once the server has confirmed the award and folded it
        /// into the baseline — the id is gone from the queue by then, so
        /// <see cref="HasGranted"/> would say no. That is why a caller that needs to know
        /// whether something was <em>ever</em> claimed must ask its own state, not this.
        /// For the daily chests that state is the claimed count, which the day key resets.
        /// </para>
        /// </summary>
        public bool TryAward(string id, long amount, long unix, string reason, out GrantEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(id) || amount <= 0) return false;
            if (HasGranted(id)) return false;

            entry = new GrantEntry(id, amount, unix, reason);
            _pendingGrants.Add(entry);
            return true;
        }

        public bool HasGranted(string id)
        {
            for (int i = 0; i < _pendingGrants.Count; i++)
                if (string.Equals(_pendingGrants[i].Id, id, StringComparison.Ordinal)) return true;
            return false;
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
                                     long earnedFloor = 0,
                                     ICollection<string> confirmedGrantIds = null,
                                     ICollection<string> rejectedGrantIds = null,
                                     ICollection<string> rejectedSpendIds = null)
        {
            // A claim the server refused is dropped, and the balance it was inflating goes
            // with it. Before the confirmed ids, so an id that somehow appears in both lists
            // is dropped either way — a refusal is the stronger answer.
            if (rejectedGrantIds != null && rejectedGrantIds.Count > 0)
            {
                for (int i = _pendingGrants.Count - 1; i >= 0; i--)
                    if (rejectedGrantIds.Contains(_pendingGrants[i].Id)) _pendingGrants.RemoveAt(i);
            }

            // A debit the server refused is dropped the same way, and the balance it took
            // comes back. A refusal here is permanent — unaffordable on the server's figures,
            // or a pass underpaid or unsold — so an entry kept would be resubmitted on every
            // sync for the life of the account (invariant 13a) while the thing it paid for went
            // on drawing as bought. Announced after it is gone, so a listener reading the
            // balance sees the money already back; whatever the debit bought listens for its
            // own id (a season pass takes itself back through this).
            if (rejectedSpendIds != null && rejectedSpendIds.Count > 0)
            {
                var dropped = new List<string>();
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    if (!rejectedSpendIds.Contains(_pending[i].Id)) continue;
                    dropped.Add(_pending[i].Id);
                    _pending.RemoveAt(i);
                }

                foreach (string id in dropped)
                {
                    try { SpendRejected?.Invoke(Currency, id); }
                    catch (Exception e) { UnityEngine.Debug.LogException(e); }
                }
            }

            GrantedBaseline = grantedBaseline < 0 ? 0 : grantedBaseline;
            SpentBaseline = spentBaseline < 0 ? 0 : spentBaseline;

            if (confirmedThroughUnix > ConfirmedThroughUnix) ConfirmedThroughUnix = confirmedThroughUnix;

            // The server keeps its own floor under derived earnings, and it is the one
            // that governs what can actually be spent. Adopting it — rather than each
            // side keeping its own — is what stops the game showing a balance the
            // server will not let the player use.
            RaiseEarnedHighWater(earnedFloor);

            // Awards the server has recorded are inside `grantedBaseline` now. Dropping
            // them here is what stops a chest being counted twice — once in the queue and
            // once in the baseline — for the rest of the account's life.
            if (confirmedGrantIds != null && confirmedGrantIds.Count > 0)
            {
                for (int i = _pendingGrants.Count - 1; i >= 0; i--)
                    if (confirmedGrantIds.Contains(_pendingGrants[i].Id)) _pendingGrants.RemoveAt(i);
            }

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

            // Awards union by id too, and because those ids are derived rather than
            // random, two devices that claimed the same chest offline contribute one
            // entry between them. Summing would pay twice; taking either side's list
            // alone would lose whatever the other had claimed.
            for (int i = 0; i < other._pendingGrants.Count; i++)
            {
                var entry = other._pendingGrants[i];
                if (HasGranted(entry.Id)) continue;
                _pendingGrants.Add(entry);
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
        ///
        /// <para>
        /// Awards are deliberately <b>not</b> pruned this way, and the asymmetry is the
        /// point. For a debit, guessing wrong in the conservative direction means charging
        /// a player late; for an award it means silently deleting money they were given.
        /// So an award leaves the queue only when the server names its id — which is safe
        /// to wait for, because the id is derived and resubmitting it forever costs
        /// nothing but a few bytes.
        /// </para>
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

            var awards = new GrantEntryDto[_pendingGrants.Count];
            for (int i = 0; i < _pendingGrants.Count; i++) awards[i] = _pendingGrants[i].ToDto();

            return new CurrencyLedgerDto
            {
                currency = Currency,
                grantedBaseline = GrantedBaseline,
                spentBaseline = SpentBaseline,
                earnedHighWater = EarnedHighWater,
                pendingSpends = entries,
                pendingGrants = awards,
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

            if (dto.pendingGrants != null)
            {
                foreach (var entryDto in dto.pendingGrants)
                {
                    if (!GrantEntry.TryFromDto(entryDto, out var entry)) continue;
                    if (ledger.HasGranted(entry.Id)) continue;   // a duplicated key pays once
                    ledger._pendingGrants.Add(entry);
                }
            }

            return ledger;
        }
    }
}

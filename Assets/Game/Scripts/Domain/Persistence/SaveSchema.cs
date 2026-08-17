using System;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The on-disk shape of a save file.
    ///
    /// Three rules keep this survivable for the life of the game. Every record is
    /// keyed by a level's permanent id, never by its position, so content can be
    /// reordered or inserted without a player's history sliding onto the wrong levels.
    /// Every optional value has a "not written" state distinct from a real value,
    /// because JsonUtility fills missing fields with zero and a missing sound setting
    /// must not read as "muted". And nothing derivable is stored — XP and earned
    /// credits are recomputed from the level records, so they cannot drift, be
    /// double-counted across devices, or be forged by editing a number.
    ///
    /// <para>
    /// <b>Adding a field is not free.</b> <see cref="SaveChecksum"/> hashes the
    /// serialised object, so a file written by an older schema can never match a
    /// newer build's hash. That is why verification is skipped across versions —
    /// without it, growing this file would fail every save on every device at once.
    /// </para>
    /// </summary>
    public static class SaveSchema
    {
        /// <summary>
        /// v1 — levels, settings, flat coin/gem balances.
        /// v2 — currency ledgers (granted/spent/earned high-water), progression
        ///      high-water marks, cloud sync state.
        /// v3 — the chosen profile companion (<see cref="WalletDto.avatarId"/>).
        /// v4 — the heart refill deadline (<see cref="WalletDto.heartsNextRefillUnix"/>),
        ///      which turned hearts from a number nothing moved into a resource that
        ///      regenerates and gates play.
        /// v5 — the set of mechanic tips already shown (<see cref="SaveFileDto.tipsSeen"/>),
        ///      so a lesson taught once is never repeated on any of a player's devices.
        /// v6 — the daily chest counters (<see cref="SaveFileDto.daily"/>), the heart-regen
        ///      boost deadline (<see cref="WalletDto.heartBoostUntilUnix"/>) and pending
        ///      grants (<see cref="CurrencyLedgerDto.pendingGrants"/>). The last of those
        ///      is the one that matters: it is how currency a player has been *given*
        ///      reaches them offline without the client ever raising its own granted
        ///      baseline, which is the field the server owns and an attacker wants.
        /// v7 — the rewarded-ad counters (<see cref="SaveFileDto.ads"/>): which day they
        ///      describe, how many paying views each placement has had, and when the last
        ///      one was. All three are caps and pacing, not currency — what an ad actually
        ///      paid arrives through the v6 grant queue, keyed on the impression nonce, so
        ///      losing this section costs a player nothing they earned.
        /// v8 — the heart ledger (<see cref="WalletDto.heartsProduced"/>,
        ///      <see cref="WalletDto.heartsSpent"/>, <see cref="WalletDto.heartsDueUnix"/>),
        ///      replacing a stored count that could not be merged without either minting
        ///      hearts or destroying them. It destroyed them: a stale cloud snapshot won
        ///      the join and was then pushed back, so a timer refill did not survive the
        ///      app being backgrounded. See <see cref="Hearts"/>. The v4 count and deadline
        ///      remain, written as a derived mirror so a client rolled back to an older
        ///      build still reads the right number.
        /// v9 — the daily streak (<see cref="SaveFileDto.streak"/>): the day the current
        ///      run of consecutive days began and the last day a run was finished. Two
        ///      dates rather than a count, because a count cannot be merged — see
        ///      invariant 11b and <see cref="Daily.DailyStreak"/>. The length is derived
        ///      from the pair, so nothing here is a source of truth about how long a
        ///      streak is, only about when it started and when it was last fed.
        /// v10 — the day through which streak rewards have been collected
        ///      (<see cref="StreakStateDto.collectedThroughDay"/>). A streak rung is now
        ///      handed over when the player taps it rather than applied silently at the
        ///      end of a run, which needs somewhere to record what has been taken. A
        ///      third date rather than a count or a set of flags, for the third time and
        ///      the same reason: it only ever rises, so the merge is <c>max</c> like the
        ///      other two and a rung can never be paid twice. See <see cref="Daily.DailyStreak"/>.
        /// v11 — the goal through which each event's reward track has been collected
        ///      (<see cref="SaveFileDto.events"/>), and the flag that says this file has
        ///      been through a build which collects them by hand
        ///      (<see cref="SaveFileDto.eventsSeeded"/>). An event milestone is now handed
        ///      over when the player taps it rather than folded into derived earnings the
        ///      moment the glade is cleared, for the reason v10 changed the streak: a
        ///      reward that arrives as a number moving behind another screen is not a
        ///      reward. A floor per event keyed by the event's permanent id, for the
        ///      fourth time and the same reason — it only ever rises, so the merge is
        ///      <c>max</c> per key. See <see cref="Events.EventCollection"/>.
        /// v12 — the companions bought with credits (<see cref="SaveFileDto.companionsOwned"/>).
        ///      The first thing in this file that is stored because it genuinely <em>cannot</em>
        ///      be derived: a companion reached by keeper level needs no record, but nothing
        ///      observable implies "this player paid 8,000 credits for Coral". A set of
        ///      permanent ids, joined by union, which is the shape invariant 11b permits and
        ///      the one <see cref="TipLedger"/> already had — buying is irreversible, so
        ///      between two devices the player owns whatever either of them bought. A count
        ///      would have been hearts' old mistake and a per-companion flag could not tell
        ///      "not bought" from "written before this companion existed". See
        ///      <see cref="Progression.CompanionLedger"/>.
        /// v13 — the best standing ever held on each glade
        ///      (<see cref="LevelRecordDto.bestRank"/>), so the map can mark a result
        ///      permanently instead of the victory panel mentioning it once and losing it.
        ///      A standing is the first thing in this file derived from a <em>population</em>
        ///      rather than from the player, which is what makes it interesting: the figure
        ///      moves for reasons the player had no part in. Stored and promoted by
        ///      <c>max</c>, never recomputed for display — recomputing means a node sagging
        ///      while its owner is away, and freezing whatever was current when the record
        ///      was set means a player who beats their own move count against a larger
        ///      population is demoted for playing better. Zero is unreachable for a real
        ///      standing (<see cref="Social.LevelStats.MinRank"/> is 5), so a v12 file reads
        ///      as unranked and this is the first section to need no migration at all — the
        ///      move counts it is derived from were already on disk, and
        ///      <see cref="PlayerProgress.RefreshRanks"/> backfills from them the first time
        ///      a table lands. See <see cref="Social.RankTier"/>.
        /// v14 — the fastest clear of each glade in milliseconds
        ///      (<see cref="LevelRecordDto.bestMillis"/>), so a map node can report what the
        ///      player actually did rather than only how it compared. Smaller wins and zero
        ///      is absent, which is the join <c>bestMoves</c> has always used: a best only
        ///      ever falls, so both devices hold real achievements and the lower is the
        ///      better one. Milliseconds rather than seconds so zero is unreachable for a
        ///      real run — a one-turn board can be finished inside a second — which is the
        ///      same sentinel argument v13 made. Needs no migration for the same reason: an
        ///      older file reads as untimed. Unlike a standing it cannot be backfilled,
        ///      because nothing already stored implies how long a past clear took. See
        ///      <see cref="RunClock"/>.
        /// </summary>
        public const int Version = 14;

        /// <summary>Progress that predates this file: index-keyed keys in PlayerPrefs.</summary>
        public const int LegacyPlayerPrefsVersion = 0;

        /// <summary>Flat <c>wallet.coins</c> / <c>wallet.gems</c> balances, before ledgers.</summary>
        public const int FlatWalletVersion = 1;

        public const string FileName = "progress.json";
        public const string BackupFileName = "progress.backup.json";

        public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>Tri-state flag: 0 means the field was never written, so use the default.</summary>
    [Serializable]
    public struct StoredFlag
    {
        public int state;

        public const int Unset = 0;
        public const int On = 1;
        public const int Off = 2;

        public bool Resolve(bool fallback) => state == Unset ? fallback : state == On;

        public void Set(bool value) => state = value ? On : Off;

        public static StoredFlag From(bool value)
        {
            var f = new StoredFlag();
            f.Set(value);
            return f;
        }
    }

    [Serializable]
    public sealed class SaveFileDto
    {
        public int schemaVersion;
        public long updatedUnix;

        public SettingsDto settings;
        public WalletDto wallet;
        public LevelRecordDto[] levels;

        /// <summary>High-water marks that stop a retune from taking anything away.</summary>
        public ProgressionStateDto progression;

        /// <summary>Who this save belongs to and when it last reached the server.</summary>
        public CloudStateDto cloud;

        /// <summary>Where the player left off, so the map can open in the right place.</summary>
        public string lastPlayedLevelId;

        /// <summary>Set once a legacy PlayerPrefs import has run, so it never runs twice.</summary>
        public bool legacyImportDone;

        /// <summary>
        /// Permanent ids of the mechanic tips this player has been shown. Unknown ids
        /// are carried through untouched — a lesson learned on a newer build must not
        /// be re-taught after a trip through an older one.
        /// </summary>
        public string[] tipsSeen;

        /// <summary>Today's chest counters. See <see cref="DailyStateDto"/>.</summary>
        public DailyStateDto daily;

        /// <summary>Today's rewarded-ad counters. See <see cref="AdStateDto"/>.</summary>
        public AdStateDto ads;

        /// <summary>The run of consecutive days being held. See <see cref="StreakStateDto"/>.</summary>
        public StreakStateDto streak;

        /// <summary>
        /// How far each event's reward track has been collected. See <see cref="EventStateDto"/>.
        ///
        /// An array on the wire and a map everywhere else, exactly like <see cref="levels"/>
        /// and for the reason invariant 11a gives: keyed by the event's permanent id, so a
        /// duplicated row is a malformed file rather than a second payout, and a sync can
        /// write one event without re-uploading the calendar.
        /// </summary>
        public EventStateDto[] events;

        /// <summary>
        /// Set once this file has been through a build that collects event rewards by hand.
        ///
        /// <para>
        /// False is what <c>JsonUtility</c> writes into a field an older file never had, so
        /// it means exactly the right thing: "written by a build that folded every reached
        /// milestone straight into derived earnings". <see cref="Events.EventCollection"/>
        /// reads that as a cue to mark everything already reached as already collected —
        /// under the old rule it had been — rather than lighting up a track the player has
        /// in fact already been paid for.
        /// </para>
        /// <para>
        /// Nothing depends on it for correctness. Event credits are derived and bounded
        /// below by the wallet's earned floor, so an unseeded file can only ever produce a
        /// collect that pays nothing visible — never one that pays twice. This exists so
        /// that does not happen, not because it would be unsafe if it did. A bool that only
        /// goes one way is a join, so the merge is <c>or</c>.
        /// </para>
        /// </summary>
        public bool eventsSeeded;

        /// <summary>
        /// Permanent ids of the companions this player <b>bought</b>, sorted.
        ///
        /// <para>
        /// Purchases only. A companion reached by keeper level is never listed, because that
        /// half of the rule is derived and re-derives correctly on every device — writing it
        /// down as well would create a second answer that a retune could put out of step with
        /// the first. See <see cref="Progression.CompanionLedger"/>, which owns the composite
        /// rule.
        /// </para>
        /// <para>
        /// Unknown ids are carried through untouched, exactly like <see cref="tipsSeen"/>: a
        /// companion bought on a newer build must not be confiscated by a trip through an
        /// older one, and an id this build does not recognise costs one short string.
        /// </para>
        /// <para>
        /// Absent is the same fact as "bought nothing", which is what makes this mergeable
        /// without a sentinel — the problem <see cref="WalletDto.heartsProduced"/> needed a
        /// paragraph to solve. <c>JsonUtility</c> writes a null array into a field an older
        /// file never had, and a null set and an empty set say the same true thing.
        /// </para>
        /// </summary>
        public string[] companionsOwned;

        /// <summary>
        /// Integrity check over the rest of the file. Empty on files written before
        /// checksums existed, which are accepted and gain one on the next write.
        /// </summary>
        public string checksum;
    }

    [Serializable]
    public sealed class SettingsDto
    {
        public StoredFlag music;
        public StoredFlag sfx;
        public StoredFlag haptics;
        public string language;
    }

    /// <summary>
    /// Currencies, and the player's chosen name.
    ///
    /// <see cref="coins"/> and <see cref="gems"/> are the v1 shape: flat balances the
    /// client was free to set. They are read once, folded into a ledger's granted
    /// baseline so nobody loses what they had, and never written again.
    /// </summary>
    [Serializable]
    public sealed class WalletDto
    {
        /// <summary>-1 means never written, so the seeded starting balance applies.</summary>
        public int coins;
        public int gems;

        /// <summary>
        /// Hearts held, as a <b>derived mirror</b> of the v8 ledger below. -1 means never
        /// written, so a full set is seeded.
        ///
        /// Read only when <see cref="heartsProduced"/> says the writer kept no ledger —
        /// a pre-v8 build, or a cloud document one of those last pushed. Still written on
        /// every save, for the same reason <see cref="coins"/> is: a player rolled back to
        /// an older build should see their real hearts rather than a seeded five.
        /// </summary>
        public int hearts;

        /// <summary>
        /// When the next heart lands, as a Unix timestamp; 0 while the player is full
        /// and no timer is running. The derived mirror of <see cref="heartsDueUnix"/>,
        /// carrying the "no timer" sentinel that the ledger deliberately does not.
        /// </summary>
        public long heartsNextRefillUnix;

        /// <summary>
        /// Every heart ever handed to this player — timer refills, chests, ads, the
        /// starting set. Only ever rises.
        ///
        /// <para>
        /// <b>Zero or less means the writer kept no ledger</b>, and that is a real
        /// sentinel rather than a hopeful one. <c>JsonUtility</c> fills an absent field
        /// with zero, so a pre-v8 file cannot be recognised by a -1 nobody wrote — reading
        /// one that way would hand every existing player an empty ledger and take all five
        /// of their hearts on the upgrade, which is a worse version of the bug this
        /// replaces. Zero is safe to spend as the marker because it is unreachable: an
        /// account is seeded at a full set, this only ever rises, and so any genuine
        /// ledger has produced at least <see cref="HeartRules.RefillCap"/>. Even if one somehow
        /// did read as zero the fallback is <see cref="hearts"/>, which would also be
        /// zero — the sentinel cannot cost anybody a heart.
        /// </para>
        ///
        /// <para>
        /// This field and the two below are the whole reason hearts survive a sync. A
        /// stored count cannot be merged: two devices showing 3 and 0 are equally
        /// consistent with "one of them spent three" and "one of them has not heard about
        /// a refill", so any rule over the pair mints hearts in one reading and deletes
        /// them in the other. Counters of things that happened have no such ambiguity —
        /// the larger value is always the one that knows more, so the merge is
        /// <c>max</c> and loses nothing. Same argument, same shape and the same reasons as
        /// <see cref="CurrencyLedgerDto.grantedBaseline"/>; see <see cref="Hearts"/> for
        /// the invariants and why the join preserves them.
        /// </para>
        /// </summary>
        public long heartsProduced;

        /// <summary>
        /// Every heart ever consumed. Only ever rises. Read only when
        /// <see cref="heartsProduced"/> says a ledger is present — on its own, zero is
        /// both "spent nothing" and "field absent", and it does not have to tell them
        /// apart.
        /// </summary>
        public long heartsSpent;

        /// <summary>
        /// When the pending refill lands. Advances one period per refill, and forward
        /// again when a spend restarts an idle timer; never rewound, and never cleared on
        /// reaching the cap — a field that is zeroed cannot be merged with <c>max</c>.
        /// Zero means only "this timer has never started".
        /// </summary>
        public long heartsDueUnix;

        /// <summary>
        /// When the faster heart regeneration bought by a chest runs out, as a Unix
        /// timestamp; 0 when no boost is running.
        ///
        /// A deadline rather than a remaining duration, for exactly the reason
        /// <see cref="heartsNextRefillUnix"/> is: a duration has to be decremented by
        /// something, and nothing runs while the app is closed. A deadline is simply
        /// compared, and the comparison is correct after a week in the background.
        /// </summary>
        public long heartBoostUntilUnix;

        public string displayName;

        /// <summary>
        /// The companion shown on the profile, by permanent avatar id. Empty means the
        /// player has never chosen one, which is not the same as choosing the first —
        /// the roster's default may change, and a real choice must survive that.
        /// </summary>
        public string avatarId;

        /// <summary>One ledger per currency, keyed by a permanent currency id.</summary>
        public CurrencyLedgerDto[] currencies;

        public static WalletDto Unwritten() => new WalletDto
        {
            coins = -1, gems = -1, hearts = -1, heartsProduced = -1, heartsSpent = -1,
        };
    }

    /// <summary>
    /// Double-entry state for one currency.
    ///
    /// A balance is <c>max(derived earned, earnedHighWater) + granted - spent</c>. Only
    /// the terms that cannot be derived are stored, and each is monotonic or
    /// server-owned, which is what lets two devices be merged without inventing money
    /// or losing a purchase.
    /// </summary>
    [Serializable]
    public sealed class CurrencyLedgerDto
    {
        /// <summary>Permanent id — <c>credits</c>, <c>gems</c>. Never renamed or reused.</summary>
        public string currency;

        /// <summary>
        /// Everything given rather than earned: the starting seed, purchases, gifts.
        /// Server-owned once cloud save is live; the client may never raise it.
        /// </summary>
        public long grantedBaseline;

        /// <summary>Spends the server has confirmed and folded in.</summary>
        public long spentBaseline;

        /// <summary>
        /// Floor under the derived earnings. Stops a reward retune, or a chapter that
        /// is temporarily out of the catalog, from reducing a balance a player is
        /// already holding.
        /// </summary>
        public long earnedHighWater;

        /// <summary>Spends made since the last sync, each with an idempotency key.</summary>
        public SpendEntryDto[] pendingSpends;

        /// <summary>
        /// Currency awarded but not yet confirmed by the server, each with an idempotency
        /// key. The mirror image of <see cref="pendingSpends"/>, and the reason the client
        /// can hand a player a daily chest while offline without ever touching
        /// <see cref="grantedBaseline"/>.
        ///
        /// <para>
        /// These are a <em>claim</em>, not money. They count toward the displayed balance
        /// so the reward is real the instant it is opened, and they are replaced — not
        /// added to — by the server's own figure on the next sync. If the server disagrees
        /// about what a chest was worth, the server is right.
        /// </para>
        /// </summary>
        public GrantEntryDto[] pendingGrants;

        /// <summary>
        /// Debits at or before this moment are already inside <see cref="spentBaseline"/>.
        /// Persisted because a merge on a later launch still needs it to tell a debit
        /// the server has absorbed from one it has never seen.
        /// </summary>
        public long confirmedThroughUnix;
    }

    /// <summary>
    /// One debit, identified so that submitting it twice can only charge once.
    ///
    /// The id is generated where the spend happens and never reused. It is what makes
    /// a retry after a dropped response safe, which is the whole reason a bare
    /// counter is not good enough here.
    /// </summary>
    [Serializable]
    public sealed class SpendEntryDto
    {
        public string id;
        public long amount;
        public long unix;

        /// <summary>What it was spent on. Carried for support and for analytics.</summary>
        public string reason;
    }

    /// <summary>
    /// One award, identified so that granting it twice is impossible rather than merely
    /// unlikely.
    ///
    /// <para>
    /// The id is <b>derived, not random</b>, and that is the difference between this and
    /// <see cref="SpendEntryDto"/>. A spend needs a fresh key because the same purchase
    /// made twice is two purchases. An award needs a <em>reproducible</em> key, because
    /// the whole point is that day 20315's third chest can be granted exactly once, no
    /// matter how many devices claim it, how many times the response is lost, or whether
    /// the player reinstalls in between. The server keys its own record on the same
    /// string, so the second attempt is refused by the database rather than by any code
    /// remembering anything.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GrantEntryDto
    {
        /// <summary>Derived and stable, e.g. <c>daily:20315:2:credits</c>.</summary>
        public string id;

        public long amount;
        public long unix;

        /// <summary>What earned it. Carried for support and for analytics.</summary>
        public string reason;
    }

    /// <summary>
    /// The daily chest counters, and nothing else.
    ///
    /// <para>
    /// Three integers, which is the smallest state that survives a reset nobody runs, a
    /// merge nobody supervises and a clock nobody controls. The day is a whole-day count
    /// since the epoch (see <c>DailyRules</c>), so a stale day is noticed on the next
    /// read rather than by a timer that has to fire at midnight in every timezone.
    /// </para>
    /// <para>
    /// Note what is <em>not</em> here: what any chest contained. Drops are recomputed from
    /// the player, the day and the chest index every time they are needed, so there is no
    /// stored prize to drift from the table, to be edited by a player, or to have to be
    /// migrated when the table is retuned.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class DailyStateDto
    {
        /// <summary>
        /// Which day these counters describe. Zero means none — 1970 is not a day any
        /// live player has counters for, so no separate "unwritten" flag is needed.
        /// </summary>
        public int dayKey;

        /// <summary>Runs finished today, won or lost.</summary>
        public int runs;

        /// <summary>Chests opened today.</summary>
        public int claimed;
    }

    /// <summary>
    /// The rewarded-ad counters: pacing state, and nothing that is worth money.
    ///
    /// <para>
    /// Every field here exists to answer "may I offer another ad?", and none of them
    /// records what an ad paid. That belongs in the grant queue, keyed on the impression
    /// nonce and adjudicated by the server, so a player who loses this section loses
    /// nothing but their place in today's cap — which is exactly the failure worth having,
    /// because the alternative is a section that can be edited to mint currency.
    /// </para>
    /// <para>
    /// The day is a whole-day count since the epoch, the same one the chest counters use
    /// (see <c>DailyRules</c>), so a stale day is noticed by the next read rather than by
    /// a timer that has to fire at midnight in thirty-eight timezones.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AdStateDto
    {
        /// <summary>
        /// Which day these counters describe. Zero means none — no live player has
        /// counters for 1970, so no separate "unwritten" flag is needed.
        /// </summary>
        public int dayKey;

        /// <summary>
        /// Paying views today, per placement. Absent means none.
        ///
        /// An array rather than parallel fields because placements are content-shaped: a
        /// placement added in a future drop must land in an existing save without a
        /// migration, and one retired must not leave a dead column behind. Duplicates are
        /// folded on read by keeping the <em>larger</em> count, so a malformed file cannot
        /// hand somebody a fresh allowance.
        /// </summary>
        public AdViewCountDto[] watched;

        /// <summary>
        /// When the last paying view finished, for the cooldown.
        ///
        /// Persisted rather than held in memory so that force-quitting the app is not a
        /// way around the gap. Merges by taking the later value, which is the conservative
        /// direction: two devices cannot shorten a cooldown by disagreeing about it.
        /// </summary>
        public long lastWatchedUnix;
    }

    /// <summary>One placement's paying views today. Keyed by a permanent placement id.</summary>
    [Serializable]
    public sealed class AdViewCountDto
    {
        public string placement;
        public int count;
    }

    /// <summary>
    /// The daily streak, as two dates and no count.
    ///
    /// <para>
    /// This is invariant 11b applied before the mistake rather than after it. A stored
    /// <em>length</em> is exactly the shape hearts used to be: two devices showing 6 and 1
    /// are equally consistent with "one is behind" and "the streak broke and restarted",
    /// so the merge would have to guess, and both guesses are wrong somewhere — the
    /// generous one resurrects a streak the player really did lose, the conservative one
    /// deletes one they really do hold.
    /// </para>
    /// <para>
    /// Two dates have no such ambiguity. Both only ever rise, so the merge is <c>max</c>
    /// on each with no special cases, and the length is <c>lastPlayedDay - startDay + 1</c>
    /// — derived, exactly as XP, credits and the heart count are. Zero on either means
    /// "never", which is safe as a sentinel for the reason <see cref="DailyStateDto.dayKey"/>
    /// gives: no live player has a streak dating from 1970, and <c>JsonUtility</c> writes a
    /// zero into every field an older file never had.
    /// </para>
    /// <para>
    /// The third date is the same shape again. Rewards are collected by hand now, so
    /// something has to record which ones have been taken, and the obvious candidates are
    /// both wrong: a count of collected rungs is <see cref="Hearts"/>'s old mistake, and a
    /// set of flags per run is not monotonic across a streak that breaks and restarts.
    /// <see cref="collectedThroughDay"/> is neither — it is the last <em>day</em> whose
    /// rung has been handed over, so it only ever rises, the merge is <c>max</c>, and a
    /// rung already paid on one device cannot come back on another.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class StreakStateDto
    {
        /// <summary>Whole-day count since the epoch when the current run began. 0 for none.</summary>
        public int startDay;

        /// <summary>Whole-day count since the epoch of the last finished run. 0 for none.</summary>
        public int lastPlayedDay;

        /// <summary>
        /// Whole-day count since the epoch of the last night whose reward was collected.
        ///
        /// Zero means "written by a build that paid rungs automatically", which is the one
        /// thing a live v10 file can never say: starting a run sets this to the day before
        /// it, and a day key is a five-figure number. <c>DailyStreak.LoadFrom</c> reads
        /// that zero as a pre-v10 file and marks everything already earned as collected,
        /// because under the old rule it had been.
        /// </summary>
        public int collectedThroughDay;
    }

    /// <summary>
    /// How far one event's reward track has been handed over.
    ///
    /// <para>
    /// A <em>goal</em> rather than a milestone index, and the difference matters when a
    /// live event is retuned. An index would slide: inserting a rung between two authored
    /// ones renumbers everything after it, so a floor of "two" would silently come to mean
    /// a different pair of rewards than the one the player took. A goal is a number of
    /// glades, which is a fact about what they did — every milestone asking for that many
    /// glades or fewer has been collected, whatever the track looks like afterwards.
    /// </para>
    /// <para>
    /// Zero is "nothing taken yet", which is safe as a sentinel because
    /// <see cref="Events.EventMilestone.Goal"/> is clamped to at least one. See
    /// <see cref="SaveFileDto.eventsSeeded"/> for what an <em>absent</em> row means.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class EventStateDto
    {
        /// <summary>The event's permanent id, as authored in the manifest.</summary>
        public string id;

        /// <summary>The largest milestone goal already collected. 0 for none.</summary>
        public int collectedGoal;
    }

    [Serializable]
    public sealed class ProgressionStateDto
    {
        /// <summary>-1 means never written.</summary>
        public long xpHighWater;
        public int levelHighWater;

        public static ProgressionStateDto Unwritten()
            => new ProgressionStateDto { xpHighWater = -1, levelHighWater = -1 };
    }

    [Serializable]
    public sealed class CloudStateDto
    {
        /// <summary>The authenticated account this save belongs to. Empty when local only.</summary>
        public string userId;

        /// <summary>Bumped on every local write, so a backend can order two snapshots.</summary>
        public long revision;

        public long lastSyncedUnix;

        /// <summary>Identifies the writing device in a merge, for support and diagnostics.</summary>
        public string deviceId;
    }

    [Serializable]
    public sealed class LevelRecordDto
    {
        public string levelId;
        public int stars;
        public int bestMoves;
        public int clears;
        public long firstClearedUnix;
        public long lastPlayedUnix;

        /// <summary>
        /// Best standing ever held on this glade, as percent-of-keepers-slower. 0 = never
        /// ranked, which is also what an older file reads as. See
        /// <see cref="LevelRecord.BestRank"/>.
        /// </summary>
        public int bestRank;

        /// <summary>
        /// Fastest clear in milliseconds, from the first turn. 0 = never timed, which is
        /// also what an older file reads as. See <see cref="LevelRecord.BestMillis"/>.
        /// </summary>
        public int bestMillis;
    }
}

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
        /// v15 — when the player last chose their name and their companion
        ///      (<see cref="WalletDto.displayNameSetUnix"/>, <see cref="WalletDto.avatarSetUnix"/>).
        ///      The two preferences in this file are the only values merged by recency rather
        ///      than by a join, and until now the recency they were merged by was the file's
        ///      own <see cref="SaveFileDto.updatedUnix"/> — which
        ///      <see cref="SaveService.Snapshot"/> stamps with <em>now</em> every time the
        ///      cloud sync asks for one. That made "the newer file wins" mean "the local file
        ///      always wins", so a device that had never been renamed pushed its default name
        ///      over one chosen on another device, and a reinstall erased the name it had just
        ///      downloaded. A stamp per field is the fix: it travels with the value it
        ///      describes, so the answer no longer depends on when the question was asked.
        ///      Zero means "never chosen", which is unreachable for a real choice, so a v14
        ///      file needs no migration — see <see cref="Wallet.LoadFrom"/> for the one
        ///      ambiguity it does have to resolve.
        /// v16 — the grove the player builds: the pieces they bought
        ///      (<see cref="SaveFileDto.homesteadOwned"/>) and where everything stands
        ///      (<see cref="SaveFileDto.homesteadPlaced"/>). Two fields for a whole screen,
        ///      because the rest of it is derived: the land from chapters finished, the
        ///      residents from glades cleared, and neither leaves a trace on disk. What
        ///      cannot be derived is split by shape rather than by feature. A purchase is an
        ///      entitlement, so it is a set of permanent ids joined by union — invariant 15,
        ///      and <see cref="Progression.CompanionLedger"/>'s shape for the second time.
        ///      An arrangement is an <em>instruction</em>, so it is merged by recency with a
        ///      stamp per slot — invariant 11c, and the third thing in this file under that
        ///      rule after the keeper's name and their worn companion. Note what is
        ///      deliberately absent: any count of how many benches a player owns. Holding a
        ///      piece is permission to draw it in as many slots as they like, because a
        ///      stored count is the one shape invariant 11b forbids and hearts already spent
        ///      a schema version proving it. See <see cref="Homestead.HomesteadLayout"/>.
        /// v17 — the grove stands on a floor rather than on floating islands, and the floor
        ///      is bought (<see cref="SaveFileDto.groveLandOwned"/>). This is the one thing
        ///      the change cost: land used to be <em>derived</em> from chapters finished, so
        ///      it recomputed everywhere, survived every merge and left nothing on disk
        ///      (invariant 14). Land paid for with credits cannot be derived from anything
        ///      observable, so it is stored — as a set of permanent ids joined by union,
        ///      invariant 15 for the third time after companions and grove pieces. It is a
        ///      set of <em>regions</em> rather than of tiles on purpose: both are legal
        ///      shapes and only one stays small, since a filled floor is several hundred
        ///      tiles and a set that size is merged and checksummed on every sync for ever.
        ///      Note what did <em>not</em> change: <see cref="SaveFileDto.homesteadPlaced"/>
        ///      is untouched, because a tile is a slot and its id is permanent, so an empty
        ///      floor still costs nothing and a floor with two things on it costs two rows.
        /// v18 — which way a placed piece faces
        ///      (<see cref="HomesteadPlacementDto.flipped"/>), so the grove can be edited
        ///      rather than only filled. It is a <em>mirror</em> and not a rotation because the
        ///      art cannot be rotated: every one of the catalog's pieces is a single drawing
        ///      from one fixed isometric angle, and the packs they were cut from ship no
        ///      directional variants, so there is no second sprite to turn to — see
        ///      <see cref="Homestead.Placement.Flipped"/>. It costs a bool on a row that
        ///      already exists rather than a section of its own, and it needs no stamp of its
        ///      own because the facing and the piece are one decision about one slot, dated by
        ///      the stamp the row already carries (invariant 11c). It needs no migration
        ///      either: <see cref="JsonUtility"/> writes false into a field a v17 file never
        ///      had, and false is what every v17 row meant. What did change is
        ///      <c>HomesteadLayout.Later</c> — a tie on stamp <em>and</em> piece used to fall
        ///      through to "return the first argument", which is argument order rather than a
        ///      tie-break, and with a second field able to differ that would have left two
        ///      devices pushing facings at each other for ever.
        /// v19 — the hint pool (<see cref="WalletDto.hintsProduced"/>,
        ///      <see cref="WalletDto.hintsSpent"/>, <see cref="WalletDto.hintsDueUnix"/>).
        ///      A hint used to be three per glade, handed back in full at every board, so it
        ///      was stored nowhere and meant nothing — the only players who never used one
        ///      were the ones who had not found the button. It is now an account-wide
        ///      resource on a clock, which means it is state, which means it has to be
        ///      mergeable. So it is the heart ledger's shape for the second time and for its
        ///      reason: three counters that only ever rise, joined by <c>max</c>, with the
        ///      count derived (invariant 11b). The arithmetic is not written out twice —
        ///      both pools run <see cref="RegenLedger"/>, which is invariant 5b applied
        ///      before the mistake rather than after it. Zero in
        ///      <see cref="WalletDto.hintsProduced"/> means "written before hints were
        ///      stored", which is unreachable for a real ledger because an account is seeded
        ///      at the refill cap and the field only rises, so a v18 file needs no migration
        ///      code at all: it reads as a fresh full pool. The per-glade allowance is gone
        ///      from <see cref="Content.LevelTuning"/> entirely — a glade has no opinion
        ///      about how much of a player's own pool they may spend on it.
        /// </summary>
        public const int Version = 19;

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
        /// Permanent ids of the grove pieces this player <b>bought</b>, sorted.
        ///
        /// <para>
        /// Purchases only, exactly as <see cref="companionsOwned"/> is. A piece earned by
        /// finishing a glade is never listed, because that half of the rule is derived and
        /// re-derives correctly on every device — writing it down as well would create a
        /// second answer for a retune to put out of step with the first. Residents are never
        /// here at all: they have no price, which is the one rule the two kinds of piece do
        /// not share, and <c>ContentValidation</c> fails the build on a priced one.
        /// </para>
        /// <para>
        /// <b>A set, and never a count.</b> Owning a piece is permission to draw it anywhere,
        /// not possession of a copy — so there is nothing here that could go down, and the
        /// merge is a union. A number of copies held would be hearts' old mistake in a new
        /// costume: two devices at 3 and 1 are equally consistent with "one bought two more"
        /// and "one has not heard about a purchase", so every rule over the pair is wrong
        /// somewhere. See <see cref="Homestead.HomesteadPiece"/>.
        /// </para>
        /// <para>
        /// Unknown ids are carried through untouched, for <see cref="tipsSeen"/>'s reason.
        /// Absent is the same fact as "bought nothing", which is what makes this mergeable
        /// with no sentinel at all.
        /// </para>
        /// </summary>
        public string[] homesteadOwned;

        /// <summary>
        /// What the player has put in each slot of their grove.
        ///
        /// <para>
        /// An array on the wire and a map everywhere else, keyed by the slot's permanent id —
        /// invariant 11a, for <see cref="levels"/> and <see cref="events"/>'s reason: a
        /// duplicated row is a malformed file rather than two things in one place, and a sync
        /// can write one slot without re-uploading the grove.
        /// </para>
        /// <para>
        /// The one section in this file merged by recency rather than joined by value, and
        /// therefore the one that can lose something. Invariant 11c is what keeps that
        /// bounded: every row carries <see cref="HomesteadPlacementDto.setUnix"/>, its own
        /// stamp, so the answer does not depend on when the question was asked; and a slot
        /// nobody has touched has no row, so a device with no opinion cannot outrank one that
        /// has. See <see cref="Homestead.HomesteadLayout"/>.
        /// </para>
        /// </summary>
        public HomesteadPlacementDto[] homesteadPlaced;

        /// <summary>
        /// Which regions of the grove floor the player has bought.
        ///
        /// <para>
        /// An entitlement, so a set of permanent ids joined by union — invariant 15, and the
        /// same shape as <see cref="homesteadOwned"/> and <see cref="companionsOwned"/>.
        /// Buying is irreversible, so between two devices the player owns whatever either
        /// bought, and the join is idempotent and order-independent without trying.
        /// </para>
        /// <para>
        /// Regions rather than tiles, and starter land is <b>absent rather than listed</b>:
        /// a region with no price is owned by everyone from the first launch, so writing it
        /// down would be a stored default that says nothing. Absent and "bought nothing" are
        /// the same fact, which is what makes this mergeable with no sentinel.
        /// </para>
        /// <para>
        /// Unknown ids are carried through untouched, for <see cref="tipsSeen"/>'s reason —
        /// land bought on a newer build must not be confiscated by a trip through an older
        /// one. See <see cref="Homestead.GroveLand"/>.
        /// </para>
        /// </summary>
        public string[] groveLandOwned;

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

        /// <summary>
        /// Whether this keeper's grove may appear on the public boards.
        ///
        /// <para>
        /// <b>A setting rather than a new top-level field, and that is what kept it free.</b>
        /// <c>settings</c> is already carried by the merge, already in the mapper both ways and
        /// already inside <c>firestore.rules</c>' <c>hasOnly</c> list, so a preference put here
        /// reaches the server without any of the four places invariant 12a names having to be
        /// touched — which is the same reason it is the right home for it rather than a
        /// coincidence. It is also read by <c>publishGrove</c> off the save document the server
        /// already opens, so the refusal is enforced where it cannot be talked out of.
        /// </para>
        /// <para>
        /// A <see cref="StoredFlag"/> rather than a bool, so "never chosen" is a state. It
        /// defaults to <em>on</em>: a keeper who has never renamed is published under a name
        /// the server generates, which names nobody, and a board that ships empty because
        /// nobody found the toggle is a board that never starts. The toggle is on the profile
        /// beside the account section, which is where identity lives.
        /// </para>
        /// </summary>
        public StoredFlag board;
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

        /// <summary>
        /// Every hint ever handed to this player — timer refills, the starting set, a
        /// watched video. Only ever rises.
        ///
        /// <para>
        /// <b>Zero or less means the writer stored no hint pool</b>, and it is a real
        /// sentinel rather than a hopeful one, for exactly the reason
        /// <see cref="heartsProduced"/> spells out at length: <c>JsonUtility</c> fills an
        /// absent field with zero, and zero is unreachable for a genuine ledger because an
        /// account is seeded at <see cref="HintRules.RefillCap"/> and this only ever rises.
        /// So a v18 file reads as a fresh full pool and needs no migration code.
        /// </para>
        /// <para>
        /// This field and the two below are the whole reason hints survive a sync, and the
        /// argument is the one hearts already lost a schema version to: a stored count of
        /// three-against-zero is equally consistent with "one device spent three" and "one
        /// device has not heard about a refill", so any rule over the pair mints in one
        /// reading and deletes in the other. See <see cref="RegenLedger"/> for the
        /// invariants and why the join preserves them.
        /// </para>
        /// </summary>
        public long hintsProduced;

        /// <summary>
        /// Every hint ever consumed. Only ever rises. Read only when
        /// <see cref="hintsProduced"/> says a ledger is present — on its own, zero is both
        /// "spent nothing" and "field absent", and it does not have to tell them apart.
        /// </summary>
        public long hintsSpent;

        /// <summary>
        /// When the pending hint lands. Advances one period per refill, and forward again
        /// when a spend restarts an idle timer; never rewound, and never cleared on reaching
        /// the cap — a field that is zeroed cannot be merged with <c>max</c>. Zero means only
        /// "this timer has never started".
        /// </summary>
        public long hintsDueUnix;

        /// <summary>
        /// The name the player chose, or empty when they never have.
        ///
        /// <para>
        /// Empty is load-bearing and must stay reachable. <see cref="Wallet.DefaultName"/>
        /// is what an unnamed keeper is <em>shown</em>, never what is stored: writing it
        /// down turns "this device has no opinion" into "this device chose Grovekeeper",
        /// and the merge cannot tell those apart. That is precisely how a rename used to
        /// be lost — a second device, or the same one after a reinstall, pushed the
        /// default over a name the player had picked. See <see cref="Wallet.LoadFrom"/>.
        /// </para>
        /// </summary>
        public string displayName;

        /// <summary>
        /// When <see cref="displayName"/> was chosen, as a Unix timestamp; 0 when it never
        /// was, or when the file predates v15.
        ///
        /// <para>
        /// The one thing in this file merged by recency, so the recency has to be a fact
        /// about the <em>value</em>. It used to be taken from
        /// <see cref="SaveFileDto.updatedUnix"/>, which the cloud sync restamps with the
        /// current moment every time it takes a snapshot — so the local side won every
        /// comparison it was ever part of, whatever it held and however old the choice
        /// behind it was. See <see cref="SaveMerge"/> for the rule this feeds.
        /// </para>
        /// </summary>
        public long displayNameSetUnix;

        /// <summary>
        /// The companion shown on the profile, by permanent avatar id. Empty means the
        /// player has never chosen one, which is not the same as choosing the first —
        /// the roster's default may change, and a real choice must survive that.
        /// </summary>
        public string avatarId;

        /// <summary>
        /// When <see cref="avatarId"/> was chosen, as a Unix timestamp; 0 when it never
        /// was. Exists for the reason <see cref="displayNameSetUnix"/> does, and is merged
        /// by the same rule — a companion worn on a phone must not be undone by a tablet
        /// that has simply been opened more recently.
        /// </summary>
        public long avatarSetUnix;

        /// <summary>One ledger per currency, keyed by a permanent currency id.</summary>
        public CurrencyLedgerDto[] currencies;

        public static WalletDto Unwritten() => new WalletDto
        {
            coins = -1, gems = -1, hearts = -1, heartsProduced = -1, heartsSpent = -1,
            hintsProduced = -1, hintsSpent = -1,
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

    /// <summary>
    /// One slot of the grove, and what the player last decided about it.
    ///
    /// <para>
    /// A row exists only because somebody made a choice. There is no row for a slot nobody
    /// has touched, which is invariant 11c's second half and the reason a fresh install
    /// cannot flatten a grove arranged on another device: absence means "no opinion", and a
    /// device with no opinion never wins a recency comparison. A slot the player deliberately
    /// <em>emptied</em> keeps its row with an empty <see cref="piece"/>, because taking a
    /// tree down is a choice too and deleting the row would let a stale device put it back.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class HomesteadPlacementDto
    {
        /// <summary>The slot's permanent id, as authored in the grove catalog.</summary>
        public string slot;

        /// <summary>The piece standing here, or empty for a slot cleared on purpose.</summary>
        public string piece;

        /// <summary>
        /// When this slot was last set, as a Unix timestamp.
        ///
        /// Its own stamp rather than the file's <see cref="SaveFileDto.updatedUnix"/>, which
        /// <see cref="SaveService.Snapshot"/> writes as <em>now</em> every time a sync asks
        /// for a snapshot — so merging on it would mean "whichever device is syncing wins",
        /// which is exactly how the keeper's name was lost for a year. Zero is unreachable
        /// for a real choice, so it reads as "written by something that did not stamp".
        /// </summary>
        public long setUnix;

        /// <summary>
        /// Drawn mirrored. Added in v18; see <see cref="Homestead.Placement.Flipped"/> for why
        /// the grove offers a flip rather than a rotation.
        ///
        /// <para>
        /// This is the one field in the save file whose "absent" state is a value a real one
        /// can also hold, and it is the one case where that is harmless rather than the mistake
        /// invariant 11b warns about. <see cref="JsonUtility"/> writes false into a field an
        /// older file never had — and false is exactly what every v17 row meant, because
        /// nothing could be mirrored before this existed. There is no third state to confuse it
        /// with: the row's own <see cref="setUnix"/> already carries the "has the player
        /// decided" question, so this never has to answer it.
        /// </para>
        /// </summary>
        public bool flipped;
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

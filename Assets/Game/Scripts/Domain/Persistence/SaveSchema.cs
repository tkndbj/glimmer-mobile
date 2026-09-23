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
        ///      <c>max</c> per key. See <see cref="Events.SeasonLedger"/>.
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
        ///      <c>RunScreen.Tick</c>.
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
        ///      (<c>homesteadOwned</c>, since v20 <c>homesteadStock</c>)
        ///      and where everything stands
        ///      (<c>homesteadPlaced</c>). Two fields for a whole screen,
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
        ///      a schema version proving it. See <c>HomesteadLayout</c>.
        /// v17 — the grove stands on a floor rather than on floating islands, and the floor
        ///      is bought (<c>groveLandOwned</c>). This is the one thing
        ///      the change cost: land used to be <em>derived</em> from chapters finished, so
        ///      it recomputed everywhere, survived every merge and left nothing on disk
        ///      (invariant 14). Land paid for with credits cannot be derived from anything
        ///      observable, so it is stored — as a set of permanent ids joined by union,
        ///      invariant 15 for the third time after companions and grove pieces. It is a
        ///      set of <em>regions</em> rather than of tiles on purpose: both are legal
        ///      shapes and only one stays small, since a filled floor is several hundred
        ///      tiles and a set that size is merged and checksummed on every sync for ever.
        ///      Note what did <em>not</em> change: <c>homesteadPlaced</c>
        ///      is untouched, because a tile is a slot and its id is permanent, so an empty
        ///      floor still costs nothing and a floor with two things on it costs two rows.
        /// v18 — which way a placed piece faces
        ///      (<c>HomesteadPlacementDto.flipped</c>), so the grove can be edited
        ///      rather than only filled. It is a <em>mirror</em> and not a rotation because the
        ///      art cannot be rotated: every one of the catalog's pieces is a single drawing
        ///      from one fixed isometric angle, and the packs they were cut from ship no
        ///      directional variants, so there is no second sprite to turn to — see
        ///      <c>Placement.Flipped</c>. It costs a bool on a row that
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
        /// v20 — grove decor is bought <b>by the copy</b>
        ///      (<c>homesteadStock</c> replaces <c>homesteadOwned</c>).
        ///      Read v16's entry above and then this one, because this is that decision
        ///      reversed and the reversal needs its reasons written down.
        ///      <para>
        ///      What v16 said was that a count of copies is the shape invariant 11b forbids,
        ///      and it was right about the naive shape and wrong that no shape existed. A
        ///      stored <em>count remaining</em> is unmergeable for hearts' reason: two devices
        ///      at 3 and 1 are equally consistent with "one bought two more" and "one has not
        ///      heard about a purchase". A stored count of <em>copies ever bought</em> has no
        ///      such problem — it only ever rises, so the join is <c>max</c> per id and the
        ///      larger value is always the one that knows more. That is the produced/spent
        ///      ledger's trick for the third time after hearts and hints, and it is why this
        ///      section stores purchases and derives what is left: <c>available = bought −
        ///      placed</c>, where the placements are already in the file.
        ///      </para>
        ///      <para>
        ///      <b>The subtraction may go negative and that is not a bug.</b> Two devices can
        ///      each place the last copy on a different tile; the placement map merges by
        ///      recency per slot (invariant 11c), so both placements survive and the grove
        ///      briefly holds more than it bought. The reading is clamped at zero and
        ///      <em>nothing is taken down</em> — a merge that removed a placement to balance
        ///      an arithmetic identity would be the data loss invariant 11 exists to refuse,
        ///      to fix a discrepancy worth one fence. It resolves itself the moment anything
        ///      is bought or cleared.
        ///      </para>
        ///      <para>
        ///      <b>Only priced decor is stocked.</b> Anything earned by playing, anything
        ///      free, every resident and every home rung is held exactly as it was before —
        ///      an entitlement, unlimited, derived where it can be. So the twelve starter
        ///      pieces and the eight earned ones behave identically to v19 and the stock is
        ///      purely the shop's half. See <c>GroveStock</c>.
        ///      </para>
        ///      <para>
        ///      A v19 file is migrated by <c>HomesteadLedger.LoadFrom</c> rather than by a
        ///      pass of its own: each id in the old <c>homesteadOwned</c> set becomes a stock
        ///      row of <c>max(copies placed, GroveStock.LegacyGrant)</c>, which is read out of
        ///      the same DTO. Nobody loses a placement and everybody keeps room to rearrange.
        ///      </para>
        /// v21 — the heart containers bought with real money
        ///      (<see cref="SaveFileDto.heartContainersOwned"/>) and the ones a refund has
        ///      taken back (<see cref="SaveFileDto.heartContainersRevoked"/>).
        ///      <para>
        ///      A container raises the refill cap permanently — 5 becomes 10, 20 or 50 — and
        ///      it is the first thing in this game bought with money that is not currency.
        ///      Invariant 18 says a real-money product grants currency and nothing else, and
        ///      the argument behind it is exact: hearts and boosts are <em>amounts</em>, so a
        ///      product granting one would need the client to apply half a purchase after the
        ///      server applied the other half — which means a record of "did I already apply
        ///      this transaction's hearts", in this file, merged across devices, whose failure
        ///      mode is somebody paying and receiving nothing. A capacity is not an amount: it
        ///      arrives as the union of one permanent id, so applying it twice is applying it
        ///      once and the record has nothing to answer. The rule is therefore widened
        ///      rather than broken — <b>a real-money product grants currency, or an idempotent
        ///      permanent entitlement, never a stored amount and never both</b> — and the
        ///      shape is <see cref="companionsOwned"/>'s for the fourth time (invariant 15).
        ///      </para>
        ///      <para>
        ///      <b>The cap is derived and only the ids are stored.</b> Writing the number down
        ///      would be a second answer to a question the catalog already answers, and it
        ///      would freeze a container's worth at whatever it was on the day it was bought.
        ///      What a player holds is the <em>largest</em> container they own rather than the
        ///      sum, which is what makes buying the rungs out of order, buying one twice
        ///      through a restore, or restoring onto a device that already holds a better one
        ///      all resolve to the same number with no special case.
        ///      </para>
        ///      <para>
        ///      <b>The second set is what a client-held entitlement cannot see by itself.</b>
        ///      Buy, spend, refund, repeat is the commonest way a mobile economy leaks
        ///      (invariant 18c), and a permanent upgrade that survived a refund would be
        ///      exactly that. So the server, which already revokes a refunded receipt, now
        ///      reports the <em>explicitly revoked</em> product ids on every wallet reply, and
        ///      they are recorded here. Note what it is not: it is not the list of ids the
        ///      server thinks the account owns. An answer read as a whitelist would confiscate
        ///      a purchase on any reply that was short or from an account the server had not
        ///      caught up with; an explicit revocation can only be produced by a refund that
        ///      really happened. Both sets only ever grow, so both are joined by union and two
        ///      devices converge whatever order they sync in (invariant 11b); buying a
        ///      refunded container again lifts its revocation, because <c>redeemPurchase</c>
        ///      clears it in the same transaction that grants.
        ///      </para>
        ///      <para>
        ///      A v20 file reads as "bought nothing, refunded nothing", which is true, so
        ///      there is no migration. See <see cref="HeartContainerLedger"/>.
        ///      </para>
        /// v22 — the utilities a player is holding (<see cref="SaveFileDto.utilityStock"/>): a
        ///      firepot thrown onto the hill, a mending poured into a ward, a surge of fuel.
        ///      <para>
        ///      <b>The first consumable in this file that is neither currency nor on a clock</b>,
        ///      and the shape follows from that in one step. Hearts and hints come back by
        ///      themselves, so they are <see cref="RegenLedger"/>: three counters and a deadline.
        ///      Grove decor is bought and then <em>stands somewhere</em>, so
        ///      <c>homesteadStock</c> stores purchases alone and derives what
        ///      is left from the placements already in this file. A utility is granted, used, and
        ///      gone — nothing else in the save implies it ever existed — so both halves have to
        ///      be written down: <c>earned</c> and <c>spent</c>, each monotonic, joined by a
        ///      per-id <c>max</c>, with what is in hand derived as the difference and clamped at
        ///      nought. That is invariant 11b for the fourth time, and the first time the answer
        ///      was two counters per id rather than one.
        ///      </para>
        ///      <para>
        ///      <b>Nothing here is adjudicated, and invariant 39 is why that is safe.</b> A
        ///      utility is not currency (invariant 13), so the server is told nothing about one —
        ///      but a consumable that made a board easier could still reach a public number
        ///      through stars, which derive credits, which are a grove's worth on a leaderboard
        ///      (invariant 19a). What closes that is the grade: a utility that delivers damage is
        ///      charged against the graded count at the most a match could ever have delivered, so
        ///      no number written in this section can improve a star. A forged row buys an easier
        ///      run and never a better one.
        ///      </para>
        ///      <para>
        ///      Absent is the same fact as "granted none", so a v21 file needs no migration and
        ///      no sentinel — the property that makes every other id-keyed section here
        ///      mergeable. See <see cref="Utilities.UtilityStock"/>.
        ///      </para>
        /// v23 - the ward roster and the line it stands in
        ///      (<see cref="SaveFileDto.wardsOwned"/>, <see cref="SaveFileDto.wardLoadout"/>),
        ///      and how far an endless run has ever reached
        ///      (<see cref="SaveFileDto.endlessBest"/>).
        ///      <para>
        ///      <b>Three fields and three different shapes, which is invariant 16's split asked
        ///      of one feature.</b> A turret <em>bought</em> is an entitlement, so
        ///      <c>wardsOwned</c> is a union-joined set of permanent ids and a starter is never
        ///      written down (16e's rule about starter land, and 16f's about the starter
        ///      companion). Which turret stands on which colour is an <em>instruction</em>, so
        ///      <c>wardLoadout</c> is merged by recency and carries its own stamp - invariant
        ///      11c to the letter, since the choice's date and never the file's is what makes
        ///      the comparison mean what it says, and a player who has never arranged the line
        ///      stores nothing rather than storing the default. And how deep an endless run got
        ///      is an <em>achievement</em>, so <c>endlessBest</c> is one monotonic integer per
        ///      level id joined by <c>max</c>, which is 14a's floor exactly.
        ///      </para>
        ///      <para>
        ///      <b>Nothing here is adjudicated, and the reason is the same one invariant 39
        ///      gives for the utilities.</b> A forged turret buys a silhouette and an addition
        ///      to a bolt; it can never reach a public number, because a grove's worth is
        ///      derived from what is <em>held</em> in the grove (19a) and the line is not part
        ///      of it. The money half is defended where money always is - <c>submitSpends</c>
        ///      refuses a debit the server-derived balance cannot cover - so there is nothing
        ///      here for the server to recompute. An endless best is likewise a reading rather
        ///      than a payment: it pays no credits and no XP, because those derive from the star
        ///      ledger and nothing else (invariant 9).
        ///      </para>
        ///      <para>
        ///      Absent is the same fact as "bought nothing, chose nothing, never played one", so
        ///      a v22 file needs no migration and no sentinel.
        ///      </para>
        /// v24 — the grove is a village, so a piece can be <em>turned</em>
        ///      (<c>HomesteadPlacementDto.facing</c>) and a grove belongs to a
        ///      generation of the catalogue (<c>groveEpoch</c>).
        ///      <para>
        ///      <b>The facing is v18's mirror widened, and what changed is the art.</b> Every
        ///      grove piece used to be one drawing cut from a flat isometric sheet, so the only
        ///      transform it survived was a reflection — turning the transform turns the
        ///      <em>painting</em>, and a tree leans over. Every piece is now rendered from a
        ///      model at four camera yaws, so a facing is a different picture and a village can
        ///      be laid out with its doors facing the road. It rides the row it belongs to and
        ///      needs no stamp of its own, for exactly v18's reason (invariant 11c); 0 is what
        ///      every earlier row meant, so a v23 file needs no migration.
        ///      <c>flipped</c> is retired in place and still carried, because a rolled-back
        ///      client still writes it (invariant 12a).
        ///      </para>
        ///      <para>
        ///      <b>The epoch is the first thing in this file that can take something away, and
        ///      it exists because nothing else could.</b> The grove's three sections are joined
        ///      so that nothing is ever lost — purchases and land by union, placements by the
        ///      later stamp — which is invariant 11's promise and also means a grove cannot be
        ///      <em>cleared</em>: clearing it locally is undone by the next pull, and wiping the
        ///      server is undone by the first device that has not synced. A monotonic integer
        ///      merged by <c>max</c> says it, because the rule is that the lower epoch's grove
        ///      is discarded rather than joined. It was needed because the whole catalogue was
        ///      replaced on 2026-09-11 and not one piece id survived, so every stored grove
        ///      named pieces that no longer exist. See <c>GroveEpoch</c> for
        ///      why it must never be used to take things away from players.
        ///      </para>
        /// v25 — a turret can be upgraded, so how far each one has been taken is stored
        ///      (<see cref="SaveFileDto.wardStars"/>).
        ///      <para>
        ///      <b>A count that may be stored, which is rare here and is worth the sentence.</b>
        ///      Invariant 11b refuses a stored count because two devices cannot be told apart —
        ///      and an upgrade is irreversible, so the join is a per-key <c>max</c> and there is
        ///      nothing to be ambiguous about. It is keyed on the <em>holding</em>
        ///      (<c>{id}:{colour}</c>), because a turret is bought per colour and the shelf is
        ///      drawn per seat, so the card a player upgrades is already one seat's.
        ///      </para>
        ///      <para>
        ///      <b>Absent means one star</b> — what a turret bought before this shipped means,
        ///      and what a rolled-back client writes — so a v24 file needs no migration and no
        ///      sentinel, and a row is written only above the first star.
        ///      </para>
        ///      <para>
        ///      <b>Nothing about it is adjudicated</b>, for <c>wardsOwned</c>'s reason: a forged
        ///      star buys an addition to a bolt and can never reach a public number, because a
        ///      grove's worth is derived from what is held in the grove (19a) and the line is not
        ///      part of it. The money half is defended where money always is, by
        ///      <c>submitSpends</c> refusing a debit the derived balance cannot cover.
        ///      </para>
        /// v27 — the tasks (<see cref="SaveFileDto.tasks"/>): what has been done this day and
        ///      this week, and which dealt tasks were paid.
        ///      <para>
        ///      <b>Counters per goal, never progress per task.</b> A task's progress is derived
        ///      from the period's count for its goal, so a slate retuned by a content push
        ///      cannot leave a task starting from nothing on a device that had already done the
        ///      thing it asks for — and a counter of things that happened only ever rises, so
        ///      the join is a per-goal <c>max</c> (invariant 11b). The claims are a set of task
        ///      ids joined by union, because claiming cannot be undone. The period key is the
        ///      later one outright, for <see cref="DailyStateDto"/>'s reason.
        ///      </para>
        ///      <para>
        ///      <b>Absent is a period with key zero</b>, which no live player has counters for,
        ///      so a v26 file needs no migration and no sentinel: it reads as "nothing done yet"
        ///      and the first read rolls it into today. The daily chest ladder this replaces
        ///      keeps its section on the wire (<see cref="SaveFileDto.daily"/>), unread, because
        ///      a rolled-back client still writes it and the rules' allow-list cannot lose a key
        ///      without losing every save write (12a).
        ///      </para>
        ///      <para>
        ///      <b>v28</b> — the season track stopped being a count of glades and became a count
        ///      of <em>marks</em> (<see cref="Events.SeasonLedger"/>), so
        ///      <see cref="EventStateDto"/> gained <see cref="EventStateDto.marks"/> and a
        ///      second claim floor, <see cref="EventStateDto.premiumGoal"/>. All three numbers
        ///      only rise, so the join is a per-field <c>max</c> and the section keeps the shape
        ///      invariant 11b asks for.
        ///      </para>
        ///      <para>
        ///      <b>No migration, and the reason is structural rather than lucky.</b> Every floor
        ///      is clamped to the marks actually grown before it is read
        ///      (<c>EventLedger.ProgressOf</c>), and a v27 file has no marks at all — so a
        ///      stale <c>collectedGoal</c> written under the old meaning clamps to nought on
        ///      the first read whatever it says. (It is also nought in fact: the one authored
        ///      season shipped <c>"disabled": true</c>, and a disabled season never enters the
        ///      catalog, so nothing could ever have written a floor for it.)
        ///      <see cref="SaveFileDto.eventsSeeded"/> is retired in place: unread now, still
        ///      written, because a rolled-back client writes it and the rules' allow-list
        ///      cannot lose a key without losing every save write (12a).
        ///      </para>
        /// v29 — the streak shield (<see cref="StreakStateDto.shieldFromDay"/>): the day a
        ///      player paid gems to keep their streak alive while they are away.
        ///      <para>
        ///      <b>One date, for the fourth time in this section and the same reason.</b> The
        ///      shield covers a fixed number of days from the one it was bought on, so the
        ///      entitlement <em>is</em> that day: it only ever rises, the merge is <c>max</c>,
        ///      and "days remaining" — the shape that first suggests itself — is the stored
        ///      count invariant 11b refuses. It is also what makes the promise exact: there is
        ///      one date, so playing during the window writes nothing and cannot extend it.
        ///      </para>
        ///      <para>
        ///      <b>No migration and no rules release.</b> Zero is a file that has never bought
        ///      one, which no live player can be wrong about, and the field rides inside the
        ///      existing <c>streak</c> map — which <c>firestore.rules</c> already bounds as a
        ///      map without naming its fields, so <c>hasOnly</c> has nothing new to learn
        ///      (12a). The version moves because <see cref="SaveChecksum"/> hashes the
        ///      serialised object and a v28 file can never match a v29 hash.
        ///      </para>
        ///      <para>
        ///      The same drop took hearts and heart boosts off the streak ladder and put
        ///      chests on it. That cost this file nothing at all: a chest night is claimed
        ///      under the id a currency night already used, and what a chest holds has never
        ///      been stored anywhere (<c>DailyChests</c>).
        ///      </para>
        /// v30 — the Infinite lane counts what it has seen off
        ///      (<see cref="EndlessBestDto.waves"/>), because it now pays XP for it.
        ///      <para>
        ///      <b>The first XP in this game that is not derived from the star ledger</b>, which
        ///      invariant 9 is written against — so it is worth saying exactly what was and was
        ///      not given up. What 9 forbids is an <em>accumulator</em>: a number that cannot be
        ///      merged, cannot be retuned for existing players and cannot be recovered when lost.
        ///      This is none of those. It is a monotonic count per level joined by <c>max</c>
        ///      (11b's exception, as <see cref="WardStarDto"/> is), the rate and the ceiling are
        ///      both content, and the server derives the same figure from the same rows — so a
        ///      retune moves every player at once and a lost file recomputes to the same answer.
        ///      </para>
        ///      <para>
        ///      <b>What it did give up is that this number cannot be recomputed by the server</b>
        ///      (invariant 10d's shape), and the two defences left are the ones invariant 13's
        ///      fourth clause allows: it is <em>bounded</em> so tightly that forging it buys
        ///      nothing worth having, and it buys no <em>currency</em> at all — credits still
        ///      derive from the star ledger alone, so a forged tally moves a keeper level inside
        ///      an honest range and moves no balance. The board that is published still reads
        ///      <c>wave</c>, which still pays nothing (19l).
        ///      </para>
        ///      <para>
        ///      <b>No migration and no rules release.</b> Absent is floored by the best beside it
        ///      (see the field), and the field rides inside the existing <c>endlessBest</c> list —
        ///      which <c>firestore.rules</c> bounds by length without naming a row's fields, so
        ///      <c>hasOnly</c> has nothing new to learn (12a). The version moves because
        ///      <see cref="SaveChecksum"/> hashes the serialised object and a v29 file can never
        ///      match a v30 hash.
        ///      </para>
        /// v31 — the XP boost (<see cref="WalletDto.xpBoostWatchedUntilUnix"/>,
        ///      <see cref="WalletDto.xpBoostBoughtUntilUnix"/>,
        ///      <see cref="WalletDto.xpBoostEarned"/>): two windows during which XP is paid at a
        ///      higher rate, and the bonus they have paid.
        ///      <para>
        ///      <b>Two deadlines and a total, which is the only shape a boost on a *derived*
        ///      number can take.</b> XP is recomputed from the star ledger every time it is read
        ///      (invariant 9), so there is no running figure for a multiplier to scale — and
        ///      scaling the derived one while a window was open would make a player's level
        ///      <em>fall</em> when it closed, which every floor in this file exists to prevent. So
        ///      the bonus is worked out when it is earned and banked: one monotonic total joined
        ///      by <c>max</c>, invariant 11b's storable-count exception, the shape
        ///      <see cref="WardStarDto"/> and <see cref="EndlessBestDto.waves"/> already use.
        ///      </para>
        ///      <para>
        ///      <b>The watched deadline carries two facts and the bought one carries a track.</b>
        ///      A window is a fixed length, so the watched one also says when it <em>began</em>,
        ///      and the cooldown on watching another is derived from it rather than stored
        ///      (<c>XpBoost.WatchedReadyAt</c>) — the streak shield's trick (48c). That only holds
        ///      while nothing else writes it, which is why a purchase or a gift lands on the
        ///      bought deadline instead.
        ///      </para>
        ///      <para>
        ///      <b>The bound on the total is proportional rather than flat</b>, and it is the
        ///      interesting half. A boost can only ever have multiplied XP that was really paid,
        ///      so the total is clamped on every read to a share of the star ledger's XP plus the
        ///      Infinite lane's (<c>XpBoost.BonusFrom</c>) — <c>groveWorth</c>'s "clamped to what
        ///      the account could afford" (19a) said about a multiplier, and far tighter than any
        ///      absolute ceiling. A forged figure therefore buys a keeper level inside an honest
        ///      range and buys no <b>currency</b> at all, because credits still derive from the
        ///      star ledger alone (invariant 13's fourth clause).
        ///      </para>
        ///      <para>
        ///      <b>No migration and no rules release.</b> Absent is nought, which is what every
        ///      earlier file means and what a rolled-back client writes; and all three ride inside
        ///      the existing <c>wallet</c> map, whose sub-fields <c>firestore.rules</c> does not
        ///      name, so <c>hasOnly</c> has nothing new to learn (12a). The version moves because
        ///      <see cref="SaveChecksum"/> hashes the serialised object and a v30 file can never
        ///      match a v31 hash.
        ///      </para>
        /// v32 — the lifetime tally (<see cref="TaskStateDto.lifetime"/>): how many of each
        ///      counted verb this account has ever done, for the rank ladder to read.
        ///      <para>
        ///      <b>The same registry as the task counters, at a window that never closes.</b>
        ///      A day and a week deal a slate and reset; "for ever" does neither, so it is not a
        ///      <c>TaskPeriod</c> and carries no key and no claims — one row per
        ///      <c>TaskGoals</c> id and nothing else. One hook in <c>TaskLedger.Note</c> feeds
        ///      all three, so there is one list of counted verbs in this game rather than two
        ///      that drift, and a verb added for a future mode is countable for ever the day it
        ///      is countable for a task.
        ///      </para>
        ///      <para>
        ///      <b>Monotonic, so it merges by a per-goal <c>max</c></b> — invariant 11b's
        ///      storable-count exception for the fourth time (<see cref="WardStarDto"/>,
        ///      <see cref="EndlessBestDto.waves"/>, <see cref="WalletDto.xpBoostEarned"/>), and
        ///      the simplest of them: there is no key to decide and nothing to lose.
        ///      </para>
        ///      <para>
        ///      <b>It buys nothing, which is what makes a client-written count safe here.</b> A
        ///      rank is a badge; currency still derives from the star ledger alone (invariant 9),
        ///      so a forged row moves a picture and never a balance — invariant 13's fourth
        ///      clause, with <c>LifetimeTally.Ceiling</c> as the bound.
        ///      </para>
        ///      <para>
        ///      <b>No migration and no deploy ordering.</b> Absent is nought, which is what every
        ///      earlier file means and what a rolled-back client writes — and the reading is
        ///      floored by what the rest of the save already proves
        ///      (<c>LifetimeTally.FloorFor</c>), so an account older than the feature reads
        ///      correctly on its first launch rather than starting again from zero. The row rides
        ///      inside the existing <c>tasks</c> map, whose sub-keys <c>firestore.rules</c> does
        ///      not allow-list, so the current ruleset already accepts it and the new one merely
        ///      bounds it: client and rules may deploy in either order (12a). The version moves
        ///      because <see cref="SaveChecksum"/> hashes the serialised object and a v31 file
        ///      can never match a v32 hash.
        ///      </para>
        /// v33 — the Grovement is gone, and with it the eight fields that described it:
        ///      <c>homesteadStock</c>, <c>homesteadOwned</c>, <c>homesteadPlaced</c>,
        ///      <c>groveLandOwned</c>, <c>groveEpoch</c>, <c>groveHall</c>,
        ///      <c>groveHallFacing</c> and <c>groveHallSetUnix</c>, together with
        ///      <c>HomesteadStockDto</c> and <c>HomesteadPlacementDto</c>.
        ///      <para>
        ///      <b>This is a removal, so it moves the version for invariant 12's reason read
        ///      backwards.</b> <see cref="SaveChecksum"/> hashes the serialised object, and this
        ///      build's object no longer has fields every stored file still carries — so every
        ///      v32 file on every device would fail its checksum at once. It does not, because
        ///      <see cref="SaveChecksum.Verify"/> trusts a file whose <c>schemaVersion</c> is not
        ///      this one; the bump is what buys that, and the next write stamps a v33 hash.
        ///      </para>
        ///      <para>
        ///      <b>Nothing is destroyed and there is no rules release.</b> The eight keys stay in
        ///      <c>hasOnly</c> in <c>firestore.rules</c> deliberately — dropping a key a
        ///      rolled-back client still writes costs that client <em>every</em> save write
        ///      (12a), and an allow-list entry for a field nobody sends costs nothing. The
        ///      server's copy of a player's grove is left where it is: an incremental push is a
        ///      field-masked <c>UpdateAsync</c>, which cannot delete a key it does not name, and
        ///      the one wholesale <c>SetAsync</c> runs only for a document that does not exist
        ///      yet. What a device drops is its own local copy, on its next write, which is what
        ///      removing a feature means.
        ///      </para>
        /// v34 — the daily challenges (<see cref="SaveFileDto.challenges"/>): today's attempts
        ///      and wins per genre, a lifetime tally of levels cleared per genre, and the day
        ///      each deal was last bought.
        ///      <para>
        ///      <b>A new top-level key, so it costs the whole of invariant 12a</b> — the field
        ///      is in this DTO, in <c>SaveDelta</c>, in the mapper both ways and in
        ///      <c>hasOnly</c>, and the rules release goes out <em>before</em> the client. It
        ///      is not folded into <c>tasks</c> the way the lifetime tally was, because the
        ///      owner's instruction is that a challenge shares nothing with the core game, and
        ///      a block of its own is what makes that true on the wire as well as in code.
        ///      </para>
        ///      <para>
        ///      <b>Three merge rules, each the one its shape allows</b> (11b): the day's rows
        ///      are period counters (later day wins, larger count within a day — the task
        ///      ledger's rule), the tally is a per-genre <c>max</c> (the storable-count
        ///      exception for the fifth time), and a deal is one date per tier id joined by
        ///      <c>max</c> with its window derived from the tier's authored length (48c).
        ///      </para>
        ///      <para>
        ///      <b>What a forged block buys</b>: plays, which pay nothing by themselves; XP
        ///      inside a bounded range and no currency (13's fourth clause); and a page that
        ///      offers more plays whose coin claims the server prices against the deal
        ///      <em>it</em> sold. The version moves because <see cref="SaveChecksum"/> hashes
        ///      the serialised object and a v33 file can never match a v34 hash.
        ///      </para>
        /// </summary>
        public const int Version = 34;

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
        /// Each season's marks and claim floors. See <see cref="EventStateDto"/>.
        ///
        /// An array on the wire and a map everywhere else, exactly like <see cref="levels"/>
        /// and for the reason invariant 11a gives: keyed by the season's permanent id, so a
        /// duplicated row is a malformed file rather than a second payout, and a sync can
        /// write one season without re-uploading the calendar.
        /// </summary>
        public EventStateDto[] events;

        /// <summary>
        /// Set once this file has been through a build that collects event rewards by hand.
        ///
        /// <para>
        /// False is what <c>JsonUtility</c> writes into a field an older file never had, so
        /// it means exactly the right thing: "written by a build that folded every reached
        /// milestone straight into derived earnings".
        /// </para>
        /// <para>
        /// <b>Retired in place at v28.</b> Nothing reads it any more: a season's rewards are
        /// chests claimed by hand and no longer fold into derived earnings at all, so there
        /// is nothing for a seeding pass to make honest. It stays on the wire because a
        /// rolled-back client still writes it and <c>hasOnly</c> is an allow-list over the
        /// whole document — dropping the key would lose <em>every</em> save write (12a). A
        /// bool that only goes one way is a join, so the merge is still <c>or</c>.
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
        /// The heart containers this account has bought, sorted.
        ///
        /// <para>
        /// An entitlement, so a set of permanent ids joined by union — invariant 15, and the
        /// same shape as <see cref="companionsOwned"/> for the fourth time. It is the first
        /// entitlement here paid for with real money rather than with credits, which changes
        /// nothing about the shape and one thing about the reasoning: see v21 in
        /// <see cref="SaveSchema"/> for why an idempotent capacity is the one non-currency
        /// thing a real-money product may grant.
        /// </para>
        /// <para>
        /// The refill cap is <em>derived</em> from these against the store catalog and is
        /// never written down. Unknown ids are carried through untouched, for
        /// <see cref="tipsSeen"/>'s reason — a container bought on a newer build must not be
        /// confiscated by a trip through an older one, and here that would be a real payment
        /// silently undone.
        /// </para>
        /// <para>
        /// Absent is the same fact as "bought none", so this needs no sentinel. It is also
        /// only a <em>cache</em> in the sense that matters: both stores re-deliver a
        /// non-consumable for ever, so a player who loses this file entirely gets every
        /// container back by tapping Restore. See <see cref="HeartContainerLedger"/>.
        /// </para>
        /// </summary>
        public string[] heartContainersOwned;

        /// <summary>
        /// The heart containers a refund or a chargeback has taken back, sorted.
        ///
        /// <para>
        /// Written only from the server's own answer — the receipts it granted and has since
        /// reversed. It is a <b>revocation list and not an ownership list</b>, and that
        /// distinction is the whole safety of the design: an id missing from the server's
        /// reply means nothing, so a short answer, a cold account or an older deployment can
        /// never confiscate a purchase, while an entry can only ever be produced by a refund
        /// that actually happened. See <see cref="HeartContainerLedger.ApplyServerRevocations"/>.
        /// </para>
        /// <para>
        /// Monotonic, so it is joined by union like everything else here. Buying a refunded
        /// container again lifts its entry, driven by a real receipt rather than by anything
        /// this file could say on its own.
        /// </para>
        /// </summary>
        public string[] heartContainersRevoked;

        /// <summary>
        /// The utilities this player has been granted and used, sorted by id.
        ///
        /// <para>
        /// <b>Two counters per row, both monotonic, joined by a per-id <c>max</c>.</b> A count of
        /// utilities <em>remaining</em> is the shape invariant 11b forbids — two devices showing
        /// 3 and 1 are equally consistent with "one opened a chest" and "one spent two on a
        /// siege" — so what is stored is everything ever granted and everything ever used, and
        /// what is in hand is the difference, clamped at nought. The subtraction may briefly go
        /// negative when two devices each spend the last one before syncing, and nothing is taken
        /// back to balance it: that is <c>homesteadStock</c>'s rule and for its reason.
        /// </para>
        /// <para>
        /// <b>Account-wide, and shared by every level of every mode that offers them.</b> A stock
        /// kept per level would be state keyed on a level id and, worse, would make a utility part
        /// of a board's difficulty — which is exactly what invariant 29c refuses a companion's
        /// ability. What a player is holding is a fact about the account.
        /// </para>
        /// <para>
        /// Unknown ids are carried through untouched, for <see cref="tipsSeen"/>'s reason: a
        /// utility granted on a newer build must not be confiscated by a trip through an older
        /// one, and here that could be taking back something bought with gems. See
        /// <see cref="Utilities.UtilityStock"/>.
        /// </para>
        /// </summary>
        public UtilityStockDto[] utilityStock;

        /// <summary>
        /// The turrets this player has bought, as <c>{id}:{colour}</c> rows sorted as text.
        ///
        /// <para>
        /// <b>A union-joined set of permanent ids</b>, which is invariant 15's shape and its
        /// reason: buying is irreversible, so between two devices the player owns whatever either
        /// bought. A count could not be merged at all (11b) and a per-turret flag could not tell
        /// "not bought" from "written before this turret existed".
        /// </para>
        /// <para>
        /// <b>A row names a turret <em>and the colour it was bought for</em></b>
        /// (<see cref="Wards.WardHolding"/>), because a line holds four turrets and a colour is
        /// what a level's hill decides - so one purchase covering all four seats is buying one
        /// decision and receiving four. <b>It cost no schema version</b>, and that is a property
        /// of the shape rather than luck: what changed is what a string means, not what the field
        /// is, and a row with no colour on it - which is what an older build wrote - reads as
        /// every colour. That is the only interpretation a union merge could safely give it,
        /// since one colour or none would confiscate something somebody paid for. Such a row is
        /// carried through untouched rather than rewritten, so both builds read the same holdings
        /// out of the same file.
        /// </para>
        /// <para>
        /// <b>A free turret is never written here.</b> "Absent" and "owns nothing but the one
        /// everybody starts with" stay one fact, which is what stops a later drop that puts a
        /// price on something confiscating it from whoever was only ever holding the default -
        /// starter land's rule (16e) and the starter companion's (16f).
        /// </para>
        /// <para>
        /// Unknown ids are carried through untouched, for <see cref="tipsSeen"/>'s reason: a
        /// turret bought on a newer build must not be confiscated by a trip through an older one,
        /// and here that could be taking back something bought with gems.
        /// </para>
        /// </summary>
        public string[] wardsOwned;

        /// <summary>
        /// Which turret the player has stood on each colour. Never longer than four rows.
        ///
        /// <para>
        /// <b>The one part of this feature a merge can lose something from</b>, because it is an
        /// instruction rather than an achievement (invariant 16's split). It is joined by recency
        /// against <see cref="wardLoadoutSetUnix"/> - the choice's own stamp and never the file's
        /// <c>updatedUnix</c>, which <c>SaveService.Snapshot</c> sets to now and which therefore
        /// made the local side newer in every comparison it ever took part in (11c).
        /// </para>
        /// <para>
        /// <b>A colour the player has never chosen for writes no row</b>, so a device with no
        /// opinion is distinguishable from one that has made a choice - which is the other half of
        /// 11c, and the half that lost a keeper's name on every device for a year.
        /// </para>
        /// </summary>
        public WardSlotDto[] wardLoadout;

        /// <summary>
        /// When the line was last arranged, or nought for a player who never has.
        ///
        /// Its own stamp rather than the file's, for <see cref="WalletDto.displayNameSetUnix"/>'s
        /// reason (invariant 11c).
        /// </summary>
        public long wardLoadoutSetUnix;

        /// <summary>
        /// The furthest wave an endless run has ever reached, per level.
        ///
        /// <para>
        /// <b>One monotonic integer per key, joined by <c>max</c></b>, which is invariant 14a's
        /// floor exactly - the shape an event track's collection already uses, and the only shape
        /// invariant 11b permits for a number two devices both write. A best only ever rises, so
        /// the merge has nothing to decide.
        /// </para>
        /// <para>
        /// <b>It is not a reward and pays nothing.</b> Credits and XP derive from the star ledger
        /// and from nothing else (invariant 9), so an endless run's depth buys a place on a board
        /// and a number on a map node. That is what keeps it safe to let the client write: a
        /// forged wave count moves a reading, never a balance - and the board that reads it is
        /// clamped by <c>publishGrove</c> the way every public number here is (19a).
        /// </para>
        /// </summary>
        public EndlessBestDto[] endlessBest;

        /// <summary>
        /// How far each turret a player owns has been upgraded, keyed on the holding.
        ///
        /// <para>
        /// <b>A count, and storable only because it cannot fall.</b> Invariant 11b refuses a
        /// stored count outright — two devices showing 3 and 0 are equally consistent with "one
        /// spent three" and "one has not heard yet" — and an upgrade cannot be undone, so the
        /// join is a per-key <c>max</c> and the two devices are unambiguous. The same shape as
        /// <see cref="endlessBest"/>.
        /// </para>
        /// <para>
        /// <b>Absent is one star</b>, which is what a turret bought before this shipped means and
        /// what a rolled-back client writes, so no migration and no sentinel. A row is written
        /// only above the first star, which keeps the file small and the absent state single.
        /// </para>
        /// </summary>
        public WardStarDto[] wardStars;

        /// <summary>
        /// What has been done this day and this week, and which tasks have been paid for it.
        /// Added in v27. See <see cref="TaskStateDto"/> and <c>Tasks.TaskLedger</c>.
        /// </summary>
        public TaskStateDto tasks;

        /// <summary>
        /// The daily challenges: today's plays, the lifetime tally and the deals. Added in
        /// v34. See <see cref="ChallengeStateDto"/> and <c>Challenges.ChallengeLedger</c>.
        /// </summary>
        public ChallengeStateDto challenges;

        /// <summary>
        /// Integrity check over the rest of the file. Empty on files written before
        /// checksums existed, which are accepted and gain one on the next write.
        /// </summary>
        public string checksum;
    }

    /// <summary>
    /// The daily challenges' block. See <c>Challenges.ChallengeLedger</c> for the rules.
    ///
    /// <para>
    /// <c>day</c> is the day the <c>today</c> rows describe, nought when there are none, and
    /// every list is id-keyed on the wire and a map everywhere else (invariant 11a), written
    /// sorted so <c>SaveDelta</c> can compare by walking. A genre or a tier spelling this build
    /// cannot name is carried through untouched, for the reason a lesson id is: it is a row a
    /// newer build wrote, and dropping it would cost that build the row on the next merge.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ChallengeStateDto
    {
        public int day;

        /// <summary>One row per genre played today, sorted by genre spelling.</summary>
        public ChallengeDayDto[] today;

        /// <summary>One row per genre ever cleared, sorted by genre spelling. Never written at zero.</summary>
        public ChallengeCountDto[] clears;

        /// <summary>One row per deal ever bought: the day it was last bought, sorted by id.</summary>
        public ChallengeTierStateDto[] tiers;
    }

    /// <summary>Plays of one genre dealt and won on the block's day. Wins never exceed attempts.</summary>
    [Serializable]
    public sealed class ChallengeDayDto
    {
        public string genre;
        public int attempts;
        public int wins;
    }

    /// <summary>Levels of one genre ever cleared. A monotonic tally, joined by <c>max</c>.</summary>
    [Serializable]
    public sealed class ChallengeCountDto
    {
        public string genre;
        public int count;
    }

    /// <summary>The day a deal was last bought. Its window is derived from the tier's authored length.</summary>
    [Serializable]
    public sealed class ChallengeTierStateDto
    {
        public string id;
        public int fromDay;
    }

    /// <summary>
    /// The task section: one <see cref="TaskPeriodDto"/> per cadence. See <c>Tasks.TaskLedger</c>.
    /// </summary>
    [Serializable]
    public sealed class TaskStateDto
    {
        public TaskPeriodDto daily;
        public TaskPeriodDto weekly;

        /// <summary>
        /// What this account has ever done, one row per <c>TaskGoals</c> id with a non-zero
        /// count, sorted by goal. Added in v32; see <c>Tasks.LifetimeTally</c>.
        ///
        /// <para>
        /// <b>A bare list rather than a third <see cref="TaskPeriodDto"/></b>, because it has
        /// neither of the other two fields: there is no period key — the window never ends —
        /// and there is nothing to claim, since a rank is derived and pays nothing. A row shaped
        /// like a period would have carried a nought key, which <c>TaskLedger.Read</c> treats as
        /// "no period" and clears.
        /// </para>
        /// <para>
        /// <b>Inside this map rather than at the top level</b>, which is invariant 12a's other
        /// half and is why this cost no <c>firestore.rules</c> release to ship: <c>hasOnly</c> is
        /// an allow-list over the document's own keys, so a key added inside a map already on
        /// that list is accepted by the ruleset that is already deployed.
        /// </para>
        /// </summary>
        public TaskCountDto[] lifetime;
    }

    /// <summary>
    /// One period's counters and claims.
    ///
    /// <para>
    /// <c>key</c> is the day or week these describe, zero for none. <c>counts</c> is one row
    /// per <c>TaskGoals</c> id with a non-zero count, sorted by goal; <c>claimed</c> the task
    /// ids paid this period, sorted. Both are id-keyed arrays on the wire and maps everywhere
    /// else (invariant 11a), and both are written sorted so <c>SaveDelta</c> can compare them
    /// by walking.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class TaskPeriodDto
    {
        public int key;
        public TaskCountDto[] counts;
        public string[] claimed;
    }

    /// <summary>How much of one goal happened in the period. Never written at zero.</summary>
    [Serializable]
    public sealed class TaskCountDto
    {
        public string goal;
        public int count;
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
        /// When the <em>watched</em> XP boost runs out, or 0. Monotonic; joined by <c>max</c>.
        ///
        /// <para>
        /// <b>One number carrying two facts</b>, which is the streak shield's trick (invariant
        /// 48c): the window ends here, and because a window is a fixed length, it also <em>began</em>
        /// at <c>this - watchedHours</c> — so the cooldown on watching another is derived rather
        /// than stored (<c>XpBoost.WatchedReadyAt</c>). There is one number, so "when does it end"
        /// and "when may I watch again" cannot drift apart, and playing inside the window writes
        /// nothing at all.
        /// </para>
        /// <para>
        /// <b>Only a watched grant may write it.</b> A gift or a purchase lands on
        /// <see cref="xpBoostBoughtUntilUnix"/>, because anything else moving this deadline would
        /// move a cooldown it knows nothing about.
        /// </para>
        /// </summary>
        public long xpBoostWatchedUntilUnix;

        /// <summary>
        /// When the <em>bought</em> XP boost runs out, or 0. Monotonic; joined by <c>max</c>.
        ///
        /// The track with no cooldown: bought with gems, and where a future chest or gift lands.
        /// Separate from the watched deadline rather than sharing one, because the two pay
        /// different percentages and add together (<c>XpBoostTable.MaxPercent</c>), and because
        /// only a separate field keeps the derived cooldown above exact.
        /// </summary>
        public long xpBoostBoughtUntilUnix;

        /// <summary>
        /// Bonus XP that boosts have paid, over this account's whole life. Only ever rises.
        ///
        /// <para>
        /// <b>Stored because XP is derived and a boost is not.</b> XP is recomputed from the star
        /// ledger every time it is read (invariant 9), so there is no running total for a
        /// multiplier to scale — and scaling the derived figure while a window was open would make
        /// a player's level <em>fall</em> when it closed. So the bonus is worked out when it is
        /// earned and remembered, as one monotonic total joined by <c>max</c>: invariant 11b's
        /// storable-count exception, the shape <see cref="WardStarDto"/> and
        /// <see cref="EndlessBestDto.waves"/> already use. Two devices that each earn offline
        /// contribute the larger total rather than the sum, which is what a <c>max</c> always
        /// costs and what a per-payment claim would cost a server round trip to avoid.
        /// </para>
        /// <para>
        /// <b>The bound on it is proportional, not flat</b> — a boost can only ever have
        /// multiplied XP that was really paid, so this is clamped on every read to
        /// <c>(star XP + endless XP) x maxPercent%</c> (<c>XpBoost.BonusFrom</c>), which is
        /// <c>groveWorth</c>'s "clamped to what the account could afford" said about a multiplier
        /// (19a). A forged figure therefore buys a keeper level inside an honest range and buys no
        /// <b>currency</b> at all, because credits still derive from the star ledger alone.
        /// </para>
        /// <para>
        /// Absent is nought, which is what every file written before this means and what a
        /// rolled-back client writes, so no migration and no sentinel.
        /// </para>
        /// </summary>
        public long xpBoostEarned;

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

        /// <summary>
        /// The day a streak shield was bought, or 0 for a file that has never held one.
        ///
        /// <para>
        /// <b>A fourth date, for the fourth time and the same reason.</b> A shield covers
        /// <c>StreakTable.ShieldDays</c> days from the one it was bought on, so the whole
        /// entitlement is a single day key: it only ever rises — a later purchase is by
        /// definition a later day — so the merge is <c>max</c> and nothing has to decide
        /// which device is right. A stored "days remaining" would be hearts' old mistake
        /// (invariant 11b) and could not be joined at all.
        /// </para>
        /// <para>
        /// It is also why a shield cannot be <em>extended</em>: there is one date, so logging
        /// in while protected writes nothing and a second purchase is refused while the first
        /// is still running. The lapsed date stays on disk for ever, which costs four bytes
        /// and is what makes the field monotonic.
        /// </para>
        /// <para>
        /// <b>Nothing about it is adjudicated</b>, and that is a fact about what it does
        /// rather than an oversight. It keeps a streak alive across days nobody played; it
        /// does not advance the night count, so a forged shield still collects at most one
        /// night per calendar day — exactly what an honest player who opens the game every
        /// day collects. The gems it costs are an ordinary debit the server already refuses
        /// to let a balance go negative for. See <c>DailyStreak.TryBuyShield</c>.
        /// </para>
        /// </summary>
        public int shieldFromDay;
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
        /// <summary>The season's permanent id, as authored in the manifest.</summary>
        public string id;

        /// <summary>
        /// Marks grown inside this season's window, bounded by its own last rung.
        ///
        /// A count of things that <em>happened</em>, which is the only shape of count this
        /// save may hold (invariant 11b): it only ever rises, so two devices join with
        /// <c>max</c> and no rule has to decide which of them is behind.
        /// </summary>
        public int marks;

        /// <summary>
        /// The largest <b>free</b>-track goal already claimed. 0 for none.
        ///
        /// Named for what it meant in v11 rather than renamed to match v28's two tracks,
        /// because the field is on the wire and in the security rules' allow-list: a rename
        /// is a new key and a lost one at the same time, and <c>hasOnly</c> answers that by
        /// refusing every save write (12a).
        /// </summary>
        public int collectedGoal;

        /// <summary>The largest <b>pass</b>-track goal already claimed. 0 for none.</summary>
        public int premiumGoal;

        /// <summary>
        /// Whether this account has bought the season's pass.
        ///
        /// <para>
        /// A bool that only ever goes one way, so the join is <c>or</c> — buying is
        /// irreversible, which is the same argument that makes owned companions and owned
        /// land union-joined id sets (invariant 15).
        /// </para>
        /// <para>
        /// <b>It rides inside this row rather than becoming a key of its own</b>, and that is
        /// deliberate: <c>hasOnly</c> is an allow-list over the whole document and a new
        /// top-level key costs a <c>firestore.rules</c> release before the client can ship
        /// (12a). The rules bound the <c>events</c> list without checking a row's fields, so a
        /// field added here needs nothing deployed.
        /// </para>
        /// <para>
        /// <b>It is the client's copy and it does not gate money.</b> The server keeps its own,
        /// written by <c>submitSpends</c> in the same transaction that takes the gems, and
        /// that is what <c>claimAwards</c> reads before paying a paid-track chest. A forged
        /// <c>true</c> here buys a page that draws the paid column and a claim the server
        /// refuses.
        /// </para>
        /// </summary>
        public bool pass;
    }
    /// <summary>
    /// One utility, and the double-entry ledger of it: everything ever granted, everything ever
    /// used.
    ///
    /// <para>
    /// An array on the wire and a map everywhere else, keyed by the utility's permanent id —
    /// invariant 11a, for <see cref="SaveFileDto.levels"/>'s reason.
    /// </para>
    /// <para>
    /// <b>Both counters, and never one.</b> Grove decor could store purchases alone because the
    /// other half of its subtraction — what is standing in the grove — is already in this file.
    /// A utility is consumed inside a run and leaves no trace anywhere, so what has been spent
    /// has to be written down too. Two monotonic counters joined by <c>max</c> is the same shape
    /// <see cref="RegenLedger"/> gives hearts and hints, without the clock neither of these needs.
    /// See <see cref="Utilities.UtilityStock"/>.
    /// </para>
    /// </summary>
    /// <summary>
    /// One colour of the ward line, and the turret standing on it.
    ///
    /// <b>A pair rather than a positional array</b>, because a positional array cannot tell "the
    /// player chose nothing for blue" from "blue is the third element and this file was written by
    /// a build with three colours". A row that is absent is a colour that falls back to the
    /// starter, which is exactly the state a fresh account is in.
    /// </summary>
    [Serializable]
    public sealed class WardSlotDto
    {
        /// <summary>One character: <c>r</c>, <c>g</c>, <c>b</c> or <c>y</c>.</summary>
        public string colour;

        /// <summary>The turret's permanent id, as authored in <c>progression.json</c>.</summary>
        public string ward;
    }

    /// <summary>
    /// The furthest wave one endless level has ever reached.
    ///
    /// Monotonic, so the merge is a per-id <c>max</c> and nothing about it has to be adjudicated
    /// (invariant 14a).
    /// </summary>
    [Serializable]
    public sealed class EndlessBestDto
    {
        /// <summary>The level's permanent id. Invariant 1 reaches it.</summary>
        public string level;

        /// <summary>The furthest a single run has ever got. Only ever rises. The board's number.</summary>
        public int wave;

        /// <summary>
        /// Waves seen off here across every run ever played. Only ever rises.
        ///
        /// <para>
        /// <b>A count, and storable for <see cref="WardStarDto"/>'s reason.</b> Invariant 11b
        /// refuses a stored count because two devices showing 3 and 0 are equally consistent with
        /// "one spent three" and "one has not heard yet" — and waves already played cannot be
        /// un-played, so the join is a per-field <c>max</c> and there is nothing ambiguous about
        /// it. What it costs is the one thing a <c>max</c> always costs: two devices that each
        /// play offline contribute the larger of the two tallies rather than the sum. That is the
        /// same bargain <c>wave</c> beside it has always made, and the alternative — a claim per
        /// run — is a server round trip for a number nothing adjudicates.
        /// </para>
        /// <para>
        /// <b>Absent means "never counted", and the migration is that <c>wave</c> floors it</b>
        /// (<c>EndlessLedger.Row.Payable</c>): a file written before this existed has a best and
        /// no tally, and reading it as nought would tell somebody who had reached wave forty that
        /// they had never played. Taking the larger of the two is honest both ways, idempotent and
        /// monotonic, so a v29 file needs no migration and no sentinel.
        /// </para>
        /// <para>
        /// <b>This is the one number in the save file that decides XP without a star behind it</b>
        /// — see <c>EndlessRewardTable</c> for the ceiling that makes that defensible, and note
        /// that it is never published: the ordered board reads <c>wave</c> and goes on paying
        /// nothing (invariant 19l).
        /// </para>
        /// </summary>
        public int waves;
    }

    /// <summary>
    /// One turret's place on the upgrade ladder.
    ///
    /// <b>Keyed on the holding rather than the turret</b> — <c>{id}:{colour}</c>, the string
    /// <c>wardsOwned</c> already uses (<c>WardHolding</c>), because a turret is bought per colour.
    /// </summary>
    [Serializable]
    public sealed class WardStarDto
    {
        /// <summary>The holding: <c>{id}:{colour}</c>. Invariant 1 reaches the id in it.</summary>
        public string ward;

        /// <summary>How far it has been taken, one to five. Only ever rises.</summary>
        public int stars;
    }

    [Serializable]
    public sealed class UtilityStockDto
    {
        /// <summary>The utility's permanent id, as authored in <c>progression.json</c>.</summary>
        public string id;

        /// <summary>Every one ever handed over — a chest, a purchase. Only ever rises.</summary>
        public int earned;

        /// <summary>Every one ever used on a board. Only ever rises.</summary>
        public int spent;
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

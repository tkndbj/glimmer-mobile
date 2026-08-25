# Glimmer Groove

A globally distributed mobile puzzle game (Unity 6000.5.4f1, Android + iOS).
New level chapters ship every two to four weeks.

## The standard this project is held to

> This app will be distributed globally. Everything we build should be scalable,
> sustainable, and maintainable from day one. No demo, only production builds. We
> have to choose the absolute best practices (the most proper one) so we do not
> regret our decisions in the future when the game is expanding feature-wise and
> player-count-wise. Implement the most proper solutions, like AAA companies, to
> create the most proper system.
>
> — the project owner

What that means in practice here:

- **No placeholder architecture.** If a seam is worth having later, build it now and
  build it properly. If something genuinely is a placeholder (the economy, for
  example), say so in the code and make sure replacing it touches one file.
- **Cost curves decide priority.** Prefer the change that is cheap today and
  expensive later. That is the argument that drove stable ids, the save format and
  the asset pipeline — not taste.
- **Prove it, do not assert it.** Every claim about this codebase should be backed
  by a compile, a test, or a validator run. See *Verifying* below.
- **Push back with reasons.** If the owner asks for something that would create
  regret later, say so plainly once, with the specific failure it causes, then do
  what they decide.
- **Finish the whole job.** Half a migration is worse than none. If something can
  only be done in the Editor, automate everything around it and hand over exact steps.

## Invariants — do not break these

1. **A `LevelId` is permanent.** Save data, analytics and remote config key on it.
   Never rename or reuse a shipped id. Never key anything on a level's position.
2. **Never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`.** It is a frozen record
   of what the pre-1.0 build shipped. Changing it moves real players' stars onto the
   wrong levels. `ContentValidation` fails the build if a level it names disappears.
3. **`Domain` must never reference `Presentation`.** The asmdefs enforce it. If you
   want to call the UI from logic, raise an event instead — see `GameSettings.Changed`.
4. **Content is data, not code.** Levels live in `Assets/StreamingAssets/Content/`.
   Adding a chapter must never require a code change.
4a. **The manifest owns membership and order; a chapter body owns content.** The boot
   path reads `manifest.json` and nothing else — `CatalogIndex` is built from it and is
   what progression, unlocking and the save file key on. Chapter bodies load on entering
   a chapter and are evicted on leaving, exactly like that chapter's art. Never make the
   boot path read a chapter body: that is a cost per chapter, paid at every launch,
   forever, and it is invisible in the Editor because only Android routes
   StreamingAssets through `UnityWebRequest`. Nobody hand-writes the manifest's level
   lists — `Content ▸ Sync Manifest` derives them and the build gate proves they agree.
4b. **Every chapter file must be in the manifest, and only the Editor may check that.**
   Because every reader walks the manifest, an unlisted chapter file is not rejected —
   it is never opened. It validates, audits and builds green, and the drop ships without
   it. `ChapterFiles` is the one place allowed to list the folder; `Sync Manifest` adopts
   what it finds and `ContentValidation` fails the build on anything left over. Do not
   make the boot path read the directory to "fix" this — on Android it cannot.
4c. **Anything that rewrites `manifest.json` must prove it lost nothing.** `Sync Manifest`
   *derives* the chapter list but *rewrites the whole file*, so every field it does not
   know about is deleted the next time somebody runs the step `CONTENT.md` tells them to
   run after every content edit — silently, with a success message. That is not
   hypothetical: `unlockCost` and the whole `events` array were both added later without a
   schema bump (correctly), neither reached the writer, and the first sync after them
   deleted a live event and thirty companion prices. `ManifestSync.SurvivesRoundTrip` reads
   its own output back through the reader **the game uses** and refuses the write on any
   difference. Add a field to `ManifestDto`, forget the writer, and you get a refusal naming
   it. Never relax this into a warning: a warning printed beside the word "synced" is a
   warning nobody reads.
5. **Omit `par` when authoring.** It is derived from the board. A typed one can drift.
5a. **A level's loc keys are derived from its id and cannot be overridden.** That is
   what lets anything holding a `LevelId` name a glade without reading a chapter body.
   An overridable key makes the index insufficient and drags a file read into the map.
5b. **"Is this tile solved" is `Puzzle.Alike`, and it exists exactly once.** It was
   `Rotl(solved, k) == solved`, written out five times across `Puzzle` and `PuzzleFactory`,
   and every copy was correct until a tile appeared whose arm mask is not the whole of its
   orientation. A **crossing** wears all four arms at every angle, so the mask comparison
   calls every one of them already solved: par comes out short by one per twisted crossing,
   and par multiplies into the move budget *and* the clock, so a board validates, derives
   plausible numbers and cannot be finished. Everything now asks the one predicate — par,
   the budget, the clock, the hint, the near-miss reading and the taproot agreement check —
   and it is the code that ships rather than a copy, for invariant 9a's reason in the file
   that had no reason to have two. `content.py` and `author.py` mirror it because they run
   with no Unity anywhere; if you add a tile whose orientation is more than its arms, there
   are three places and the tests name them.

5c. **A rooted tile is authored at `/0`, and that rule guards every other rule.** Every
   proof `LevelValidator` makes runs against a copy of the board with every rotation
   zeroed, because that is the authored solution — so a tile the player can *never turn*,
   authored away from its solution, means the board that was proved is not the board that
   ships. Nothing else can notice: every arm mates, the solved probe lights, the glade
   draws, and par is unmoved because `MinimumMoves` skips rooted tiles. `c02_the_millers_knot`
   carried one for two chapters (`-EW/1!`, a vertical stub in a horizontal row) and was
   winnable only by luck — the stub happened to connect to nothing. What it did break is
   `Puzzle.TurnsToSolution`, which counts rooted tiles: one stuck off-solution adds turns
   that can never be paid, so a player who *had* reached the solution was told they were
   one turn from it. That is the near-miss line being generous, which is the one thing
   invariant-by-design it must never be. `CheckRootedTiles` asks `Puzzle.Alike`, not
   `rot == 0` — a straight conduit and a straight crossing read the same half a turn round,
   and Stonebridge's four bridges are all of them.

5d. **A mechanic that rejects no arrangement is decoration, and that is countable.**
    `Tools/verify/difficulty.py` enumerates every arrangement in which every arm mates and
    none dangles — the tidy boards a player plausibly reaches — and asks which of them win.
    When that count is **one**, the arms alone decide the glade and every other rule on it
    could be deleted without changing a single solution. Twenty-two of the first thirty
    glades were in that state, which is why brittle stone, taproots and duskcaps all read as
    absent: they were. The arms are rigid — even a filled 7x7 spanning tree usually admits
    one arrangement — so the free decisions have to be **put** there, and a twisted crossing
    is the cheapest one, because it wears all four arms at every angle and only colour or the
    dark can settle it. Three rules follow and each was broken everywhere before it was
    written down: brittle stone belongs on a tile the player cannot simply try (so, a
    crossing); a taproot's members must all be tiles the arms cannot settle, or the root is a
    hint rather than a decision; and a duskcap's ford must sit on a **cycle** of the live
    network, so the wrong turn wakes the shadow *while every critter stays lit*. That last
    one is the whole mechanic: if the wrong turn also puts a critter out, the critter tells
    the player and the shadow taught them nothing. `hazards` is the metric this replaces and
    it is worth knowing why it was wrong — it counts rotations that *would* mate two
    networks, but such a rotation leaves an arm dangling elsewhere, so it is not a board
    anybody reaches. A chapter was authored to it and came out reading like a dot-to-dot.

5e. **A briar's thorns mate across the divide, and that is why `Puzzle.Matters` has a
   second clause.** Every other tile on this board conducts along every arm it draws; a briar
   draws four and conducts two, so `Puzzle.Live` exists and the light walks it while the
   drawing walks `Mask`. One consequence is not obvious and it is the reason the tile needed
   more than a parser change. `TurnsToSolution` counts only tiles the authored solution's
   light reaches, and that was safe for four chapters because joining the grove to an island
   of dark needs a mated pair of arms, the solution mates none across that divide, so one of
   the two tiles always had to be a *lit* one turned off its solution — and lit tiles were
   already counted. A briar's shut arms mate straight across it. Open one that the solution
   leaves dark and the shadow lights up with every counted tile still exactly right, so the
   near-miss line would have told a player they had finished a glade that will not settle:
   the one thing invariant-by-design it must never do. The fix is one clause — a tile the
   player has lit counts, whatever the solution wanted of it — and
   `BriarTests.AMisturnedBriarThatLightsTheDarkIsCountedAsADistance` reads 0 on the old rule
   and 1 on this one. Before adding a tile whose drawn arms and conducting arms differ, ask
   what it can now join that no arrangement could join before.

6. **All player-facing text is a loc key.** The build gate scans the source for
   key-shaped literals and fails on any that is missing. Do not build keys by
   concatenation — write them out (see `WinOverlay.RankKeys`).
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or
   `Addressables` directly. Never hand-list asset paths — derive them from the
   catalog via `AssetManifest`.
7a. **Asset registration is an importer hook, never a menu item.** `AddressableAutoRegister`
   addresses anything under `Art/`, `Audio/` or `Fonts/` as it imports;
   `AddressableAddresses` is the single source of truth for the path→address→group rule.
   A step someone has to remember on shipping week will be forgotten — it already was
   once here, and the tool meant to fix it rotted into a silent no-op scanning a folder
   that had been deleted. The build gate runs `AddressableAudit` for the same reason:
   making an error unlikely is not the same as proving it did not happen. A chapter
   must name its own `backdrop`; art shared by several chapters belongs in the global
   group, never in whichever chapter was processed last.
7b. **Transient art belongs to a named `AssetLibrary` scope, never to the global set.**
   `EnsureScopeAsync`/`ReleaseScope` bound memory by what is on screen rather than by how
   much content exists — chapters were the first caller, companion portraits the second.
   Two rules keep it honest: an address already global stays global, and one owned by
   another scope is never re-claimed, or closing one screen frees art another is drawing.
   Loading is asynchronous, so a screen that draws a scope's art must repaint when it
   arrives; a `Image` with no sprite is a white rectangle, not a blank.
8. **The map shows one chapter at a time.** That is what bounds node count and texture
   memory by chapter size instead of catalog size. Do not "improve" it into one long map.
8a. **Map geometry lives in `ChapterMap` (Domain), not beside the screen.** `mapX`/`mapY`
   are fractions of a chapter's own map, so a distance depends on its strip count — which
   makes collisions and backwards trails facts about a chapter, checkable by the build
   gate. A validator cannot reach into Presentation, and one holding its own copy of the
   numbers would silently stop agreeing with the map. `MapLayout` reads them from there.
9. **XP and earned credits are derived, never accumulated.** They are a pure function of
   the star ledger via `ProgressionLedger`. An accumulator cannot be merged across
   devices (nothing distinguishes "cleared twice" from "counted twice"), cannot be
   retuned for existing players, and cannot be recovered when it is lost. The only
   stored progression numbers are the high-water floors in `ProgressionStore`, and they
   are floors — never a source of truth.
9b. **`progression.json` versions independently of the catalog** (`ProgressionSchema`,
   not `ContentSchema`). It ships on its own cadence, so a catalog format bump must never
   invalidate the reward table for clients that have not updated — they would fall back to
   the built-in curve and lose live tuning for an unrelated change.
9a. **The reward rule exists twice and must stay identical.** `ProgressionLedger.cs` and
   `firebase/functions/src/progression.ts`. Both run `firebase/shared/reward-vectors.json`
   as a test, so drift fails a build instead of desynchronising the economy. Change one,
   change the other, and add a vector. Re-run the seed script after every content drop.
9c. **So does the chest generator.** `DailyChestTable.cs` and `functions/src/daily.ts`,
   pinned by the `dailyChestCases` vectors in that same file. A chest's contents are a
   pure function of (account id, day, chest index) — FNV-1a then xorshift32, all 32-bit
   so JavaScript can reproduce it exactly. That is what lets the server work out what a
   chest was worth instead of believing the client, and what stops a player rerolling a
   prize by force-quitting the opening animation. The hash constants, the shift amounts,
   the stream numbers and the modulo are all contract; changing any of them rerolls every
   unopened chest in the world.
10. **The client never raises `grantedBaseline`.** Currency that was given rather than
    earned is server-owned, enforced by Firestore security rules, not by this code.
    Receipt validation must be idempotent on the store transaction id. See
    `ICloudSaveBackend`.
10a. **An award reaches the player as a claim, not as a balance.** A reward the client
    hands out while offline — a daily chest today, anything similar later — goes into
    `CurrencyLedger.TryAward` as an entry whose id is **derived from what earned it**,
    never generated. That one decision is what makes the whole path safe: two devices
    claiming the same chest produce identical entries that union to one, a resubmission
    after a dropped reply confirms instead of paying, and the server keys its own record
    on the same string so the database refuses the second grant. The server recomputes
    the amount and grants its own figure; the client's number is a prediction. Never
    reach for `GrantLocally` — it is for the account seed and nothing else.
10c. **A chest cannot be opened before the account id exists.** `DailyChests.CanOpen`.
    The roll is seeded from the uid so the server can recompute it; before the first
    sign-in there is none, and no scheme can invent one the server would agree with — the
    client simply cannot know the server's seed before it has spoken to the server. So the
    chest waits rather than showing a reward the server would overrule. Invisible in
    practice (anonymous sign-in fires from the splash, and the id is then stored in the
    save forever), and the gate lifts entirely when no backend is configured, because then
    nothing is adjudicated. Do not "improve" this into rolling with the device id.
10d. **A rewarded ad is granted by the network's callback, never claimed by the client.**
    This is the one award that breaks the pattern above, and it has to. A chest is
    recomputable from (account, day, index); nothing about "this player watched a video"
    is derivable from anything, so the authority moves outside — LevelPlay's signed
    server-to-server callback hits `adReward`, which grants in a transaction keyed on
    `ad:{eventId}`. The obvious alternative (client nonce → custom parameter → derived
    award id, exactly like a chest) was built first and does not survive: LevelPlay 9
    removed `setRewardedVideoServerParams`, and `LevelPlaySegment` is documented as user
    segmentation with no promise of reaching the callback. Building the economy's one
    security-critical link on that would fail *silently* — ads that play, players told
    they earned coins, and a server that never pays. So the client credits **hearts only**
    and calls `BeginSync` for currency. Never make the client write an `ad:` claim;
    `claimAwards` refuses them, because a claim that can never confirm is resubmitted
    forever.
10b. **Daily chests are earned, never bought.** That is what keeps them outside loot-box
    rules rather than merely compliant with them, and it is why the odds can be printed
    on the panel. Do not put a price on one, and do not add a second weighted pick — one
    pick is what makes the published odds a list that sums to a hundred.
11. **Cloud conflicts merge; they never prompt.** `SaveMerge.Join` is a join — idempotent
    and order-independent — so both devices' work survives. A "keep local or cloud?"
    dialog is data loss wearing a consent costume. Do not add one.
11b. **Anything a merge touches must be monotonic, or it is not mergeable.** A stored
    *count* — hearts, and anything shaped like hearts — cannot be joined: two devices
    showing 3 and 0 are equally consistent with "one spent three" and "one has not heard
    about a refill", so every rule over the pair is wrong somewhere. Larger mints, smaller
    deletes. Hearts shipped with smaller and destroyed a refill on every sync, because a
    sync is pull → join → push and the stale side won before the local value had ever been
    uploaded. The fix is the shape the currency ledger already had: store counters of
    things that happened (`heartsProduced`, `heartsSpent`) and derive the count, so the
    merge is `max` and the larger value is always the one that knows more. Before adding a
    field the merge reads, check it only ever rises — and that its "absent" state is a
    value a real one cannot hold, because `JsonUtility` writes a zero into every field an
    older file never had.
11c. **A value merged by recency must carry its own date, and its default must never be
    stored.** The two preferences in the save — the keeper's name and their worn companion —
    are the only things not joined on value, because they are instructions rather than
    achievements and the most recent one is the one the player meant. That makes them the
    only place the merge can lose something, and for a year it lost the name on every
    device. Two mistakes, and both are general. The recency came from the *file's*
    `updatedUnix`, which `SaveService.Snapshot` stamps with **now** every time the sync asks
    for one — so the local side was newer in every comparison it ever took part in, and "the
    newest choice wins" meant "this device wins". And an unnamed keeper *stored*
    `Wallet.DefaultName`, which threw away the one fact the merge needed: a device with no
    opinion was indistinguishable from one that had chosen, so a fresh install pushed
    "Grovekeeper" over a name the player had picked, and a reinstall erased the name it had
    just downloaded. The fix is `displayNameSetUnix` / `avatarSetUnix` (v15) and a default
    that is *shown* and never written. `SaveMerge.Chosen` is still a join — a maximum over
    (has a value, then the stamp, then ordinal order) — so it stays idempotent and
    order-independent, which is what invariant 11 promises. Before adding anything merged by
    recency, give it a stamp of its own and make sure its "absent" state is one no real value
    can hold.
11a. **The ledger is a map keyed by level id, never an array.** That makes a duplicated
    record unrepresentable rather than something the server has to filter, and lets a
    sync write `levels.<id>` alone instead of re-uploading thousands of entries.
    `SaveDelta.Between` decides what to send; an unchanged save sends nothing at all.
12a. **A field is not added to the save until it is on the wire, and the wire is four
    places.** `SaveFileDto`, `SaveDelta` (what a sync bothers to send), `FirestoreSaveMapper`
    — *both* directions — and the `hasOnly` list in `firestore.rules`. `groveLandOwned` shipped
    in v17 having reached the first two and neither of the last two, so land bought with credits
    never left the phone that bought it, and **nothing showed it**: a device only discovers what
    it failed to upload when something replaces its local save, and until account switching
    existed nothing ever did. The first player to switch got their grove back as the free starter
    square with everything outside it invisible — the placements had survived, the ground under
    them had not. The rules entry is the one with teeth in the other direction too: `hasOnly` is
    an allow-list over the whole document, so a client that writes an unlisted key does not lose
    that key, it **loses every save write**. Deploy the rules before shipping the client, never
    the other way round. `EveryFieldOfTheSaveIsCarriedByTheRoundTripFixture` is the guard, and it
    checks the *fixture* rather than the mapper — the round trip could only ever be as complete
    as what is fed into it, which is why every wire test passed for a whole schema version. Same
    lesson as invariant 4c, in the other file that silently drops what it was not told about.

12. **Adding a field to `SaveFileDto` interacts with the checksum.** `SaveChecksum` hashes
    the serialised object, so a file written by an older schema can never match a newer
    build's hash. `Verify` therefore skips across versions. Bump `SaveSchema.Version`
    when you add a section, or every save on every device fails at once.
13. **A reward is derivable, adjudicated, third-party, or not currency.** Four features
    learned this separately and it is one rule. Currency the client hands out must reach the
    server as something it can *recompute* — a chest from (account, day, index), a golden
    glade from (account, level), an event track from clears dated inside a window — or as
    something it is *told about by a third party*, which is the rewarded-ad callback, or as
    something it can *bound* so tightly that forging it buys nothing, which is the streak.
    The streak is the interesting one and the newest. Nothing about "seven days running" is
    derivable from anything the server observes, and for a long time that was read as proof
    a streak could not pay currency. It is not: the server does not need to know the streak,
    only that a claim is no *better* than an honest one. A night is claimed as
    `streak:{day}:{night}:{ccy}`, so `grantLog` bounds it to one payout per calendar day, and
    `advances` in `functions/src/streak.ts` bounds the night to climb no faster than the
    calendar climbs — a save saying "night seven" every morning fails both. What is left
    uncapped is the very first claim of a brand-new account, which is why the per-kind
    ceilings in `StreakRules` are an economy decision. Before adding a reward, decide which
    of the four it is; if it is the fourth, the bound has to be server-owned state, not a
    number read out of the save.
13a. **A claim must never be refused for a reason that will still be true tomorrow.**
    The client only warns about `rejected` ids and keeps resubmitting, so a permanent
    refusal is a loop for the life of the account. That is why the server refuses a streak
    night on `advances` (permanent, and correct) but never on the save's own dates — a
    player who collects a night, goes offline and lets the flame lapse pushes a save whose
    `startDay` has moved past that night, and gating on it would reject a reward they
    genuinely earned. `saveSupports` logs the disagreement and pays anyway. Equally, a
    missing config block leaves the claim *unconfirmed* rather than rejected, so it survives
    until the seeder has run.
14. **Derived rewards are free of save state, and that is why they are preferred.** The
    golden glade bonus adds nothing to `SaveFileDto` — no counter, no claim, no merge rule —
    because it is a pure function of things already stored. That is not an economy: it is the
    reason it was designed that way. Save state is where features here go wrong (see 11b), so
    a reward that can be expressed as a function of the star ledger should be.
14a. **Being derived decides where a reward comes *from*, never when it arrives.** The event
    track cost one field in the end (v11, `EventCollection`) and the field is not the reward —
    it is a floor saying how much of a track the player has asked for. The arithmetic is
    untouched: still derived, still recomputed by the server, still bounded below by the
    earned floor. This is the distinction 14 originally blurred, and the streak had already
    made the same correction one version earlier. A reward that lands in the balance while a
    defeat screen is up is an accounting entry; nobody experiences it. If keeping a payout
    derived means it can only arrive silently, add the floor — one monotonic integer per key,
    merged by `max` — and keep everything else derived. What must not come back is a *stored
    amount*.
15. **An entitlement is stored; everything that pays is derived.** Companion purchases are
    the first thing in the save file kept because it genuinely *cannot* be derived — nothing
    observable implies "this player paid 8,000 credits for Coral", and mining it back out of a
    debit's free-text `reason` would make a support field load-bearing. So `companionsOwned`
    (v12) is stored, in the only shape invariant 11b permits: a **set of permanent ids joined
    by union**, because buying is irreversible. A count would be hearts' old mistake and a
    per-companion flag could not tell "not bought" from "written before this companion
    existed". Note what makes this safe where a stored *amount* would not be — an entitlement
    is not money. It is forgeable and that is priced in: a forged entry buys a portrait, no
    currency and no advantage, and the money half is defended where money always is, by
    `submitSpends` refusing a debit the server-derived balance cannot cover. Before storing
    anything new, ask which of the two it is; if it pays, it goes back to 13 and 14a.
15a. **The unlock rule is "keeper level **and** purchase", and it lives only in
    `CompanionLedger`.** `AvatarCatalog.ReachedBy` answers the level half and is named for its
    narrowness on purpose — it used to be `IsUnlocked`, and a call site checking half the rule
    under a name that promises all of it is exactly how a companion somebody paid for stays
    behind a padlock. Every screen asks `IsHeld`. The level half stays derived and is never
    written down: a second answer is a second thing a retune can put out of step with the first.
    It was **or** for a year, and the two clauses cannot both survive the change — if reaching
    the gate still handed the companion over, then at the moment a player became allowed to buy
    one they would already own it and the price would be unreachable code. So the gate is
    *permission to pay*: a priced companion is held only when it was bought, and a companion the
    roster does not price is still granted at its gate, which is what keeps the starter working.
    Two consequences that are easy to get backwards. The gate is tested **before** the price, so
    a player who is both too junior and too poor is told about the wall credits cannot climb —
    otherwise the panel sells a rewarded video for a companion the video could not buy, which is
    the refusal `HintPrompt` exists to prevent one screen over. And `IsHeld` must **never**
    re-check the gate on a companion already bought: a purchase is irreversible (invariant 15),
    so a gate retune that moves past a paid-for friend confiscates them.
16. **A grove is built, and only three facts about it are stored.** The Grovement is the one
    reward in the game that is a *thing the player made* rather than a number that went up,
    and the whole feature costs `SaveFileDto` three fields because everything else is derived —
    the residents from the companion roster, the home from what was bought. What is left splits
    by *shape*, not by feature. A purchase is an **entitlement**, so `homesteadOwned` and
    `groveLandOwned` are union-joined sets of ids, which is 15 twice over. An arrangement is an
    **instruction**, so `homesteadPlaced` is merged by recency with a stamp per slot, which is
    11c for the third time — and it is therefore the only part of this feature that can lose
    something, which is why an untouched slot writes no row at all and a slot the player
    *emptied* keeps one. Note what is deliberately absent: any count of tiles. A slot id is
    written into the save, so invariant 1 applies to it in full and it is unique across the whole
    floor. **This invariant said a count of copies was unrepresentable and 16h is where that was
    corrected** — it was right about a count of copies *remaining* and wrong that no shape
    existed. Anything earned, free or entitled is still permission to draw it as often as you
    like; only the shop's half of the catalog is counted.
16b. **The grove is a tile floor, and a tile is a slot.** It was ten floating islands with
    hand-authored slots — each with a position, a size and a role — so the player's decision was
    which of eleven pre-placed dots got which sticker, and every grove came out with the same
    composition and different stickers on it. A field of identical tiles moves the composition to
    the player: *where* a thing goes is now as much their choice as *what* it is. That is why the
    slot-kind rule went with the islands; it existed to stop a sprinkle of dots looking
    accidental, and there are no dots. The kind survives as a **shop shelf** (16c) — a way of
    finding things rather than a rule anyone can hit. What made the change cheap is that
    `HomesteadLayout` did not move: a tile *is* a slot, its id is permanent, and an untouched
    tile writes no row, so a 196-tile floor with two things on it costs two rows exactly as ten
    islands did. Three rules keep the tile ids safe. They are **absolute floor coordinates**
    (`t_006_006`), so re-drawing which region a tile is *sold* in never changes what is *standing*
    on it; they are **zero-padded**, because `SaveDelta` walks them in order and an ordering that
    changed with the size of a number would make an unchanged save look changed for ever; and the
    floor may **only ever grow right and down**, because a column inserted at the left would
    renumber every tile in the world. `GroveFloor` owns the geometry, in Domain, for the reason
    `ChapterMap` does — the build gate has to be able to prove regions do not overlap, and a
    validator cannot reach into Presentation.

16e. **Land is the one thing here that stopped being derived, and it cost a schema version.**
    An island was held when its chapter was finished — a question about the star ledger, so it
    recomputed everywhere, survived every merge and left nothing on disk (invariant 14). Land
    bought with credits cannot be any of that, so `groveLandOwned` is stored (save **v17**) in the
    only shape invariant 11b permits: a set of permanent ids joined by union, which is invariant
    15 for the third time after companions and grove pieces. It is a set of **regions** rather
    than of tiles, and that is not a detail — both are legal shapes and only one stays small,
    since a filled floor is a couple of hundred tiles and a set that size is merged and
    checksummed on every sync for ever. Two smaller consequences worth keeping. Starter land has
    **no price and is never written down**, so "absent" and "bought nothing" stay the same fact
    and the union needs no sentinel. And the hall must stand on starter land — `ContentValidation`
    fails the build otherwise, because a home a new player can see and not reach is the emptiest
    possible first impression of the feature.

16f. **The starter companion is shown, never stored.** A new grove opens with one friend beside
    the hall, and the obvious way to do that — write the placement at first launch — is exactly
    what invariant 11c forbids: a fresh install would stamp that row with *now*, outrank a device
    where the player had already moved them, and put them back. So the tile draws the starter
    while it has no row of its own, and clearing it is a real instruction that does get one. Which
    starter is **derived from the roster** (`AvatarCatalog.Starter`, the one companion nothing
    gates), so a drop that changes who a new player begins with moves them without a second place
    naming anybody. `Wallet` shows the default keeper name the same way and for the same reason.

16g. **A grove's score is what it is worth, and worth is what is *held*.** The Grovement's
    star readout is the credits' worth of catalog the player holds — decor, the home rung, land
    and residents — and it stores **nothing**: every input is already a union-joined id set in
    the save (`homesteadOwned`, `groveLandOwned`, `companionsOwned`), so the score is derived,
    cloud-safe for free and monotonic for free, which is why there is no high-water floor beside
    it. Invariant 14's preferred shape, and there are two ways to break it. Counting
    **placements** instead would be won by standing one expensive piece on two hundred tiles —
    holding a piece is permission to draw it anywhere (invariant 16), so a placement count
    rewards exactly the monotony the floor exists to remove, and rearranging a grove would
    change its score. And **storing** the number would be a stored count in the shape invariant
    11b forbids, forgeable in the one direction that would matter if a leaderboard ever reads
    it. A free piece adds nothing because it is worth nothing; an *earned* companion adds its
    full price because it is worth that much — the reading is market value, not spend, which is
    the only version of the rule with no special case in it. The ladder is **content**
    (`score.stars` in `homestead.json`, `GroveScoreTable`), because the catalog grows every drop
    and a rung that means "nearly everything" today means "a start" in a year; the build gate
    refuses one that does not rise and warns on a top rung above the value of the whole catalog.

16h. **Priced decor is bought by the copy, and the count is only representable because it
    counts purchases — save v20.** Invariant 16 said a number of copies held is the stored count
    11b forbids, and it was right about the shape it described: a count of copies *remaining*
    cannot be merged, because two devices showing 3 and 1 are equally consistent with "one bought
    two more" and "one has not heard about a purchase". A count of copies **ever bought** only
    rises, so the join is a per-id `max` and the larger side always knows more — hearts' and
    hints' trick for the third time, and the first time it was seen before shipping rather than
    after. What is left to place is *derived*: `bought − placed`, and both halves are already in
    the file. `GroveStock` owns the arithmetic and holds no policy, so every rule about merging
    and migrating it is proved offline against plain integers.
    <br>Four things follow and each is load-bearing. **The subtraction is clamped at zero and
    nothing is ever taken down** — two devices can each place the last copy on a different tile,
    the placement map merges by recency per slot (11c), so both survive and the grove briefly
    holds one more fence than it bought; answering "none left" costs nothing, where removing a
    placement to balance an identity would be the loss 11 refuses. **Only priced decor is
    stocked**: a resident is a companion (16a), a home rung is a rung, and anything free or
    earned is derived from the star ledger, so writing a count of one down would be a second
    answer for a retune to put out of step with the first (14). **A bundle is content**
    (`HomesteadPiece.Bundle`) and a copy is worth `cost / bundle`, so ten fences bought as one
    bundle are worth the bundle — which is why the star ladder needed no retune and why
    `ContentValidation` *errors* on a price its bundle does not divide: the shortfall is
    invisible on a device, the server derives the same short figure, and it lands on the one
    number that reaches a public leaderboard (19a). And **the v19 field is kept as a derived
    mirror rather than deleted** — `homesteadOwned` is written on every save as the ids with a
    copy, read only when the stock section is empty, which is what lets a rolled-back client and
    a not-yet-redeployed `groveWorth` both keep working; that is 12a's deploy-ordering hazard
    removed rather than merely written down. The migration grants `max(placed, 1)` and being
    generous there was tried and is wrong: neither `HomesteadLedger.LoadFrom` nor `SaveMerge` has
    the catalog loaded, so a fixed grant would hand ten copies of a singly-sold 4,000-credit oak
    to anybody who owned one.

16a. **A resident is a companion, and the roster is written down once.** This replaces the
    rule that a resident is never for sale, which was right about the endowment and wrong about
    where creatures come from. The grove used to author five of its own, earned by clearing five
    named glades — a second roster of creatures beside the thirty-one companions a player levels
    towards and pays for, with its own unlock rule, its own prices and two screens that could
    disagree about what somebody owned. `GroveResidents` projects the roster in instead, so a
    drop that adds a companion adds a resident with nothing to remember, one price, one gate and
    one purchased set (`companionsOwned` — never mirrored into `homesteadOwned`, because two
    records of one purchase is two things a merge can disagree about). The endowment argument
    survives where it belongs: **wearing and housing are separate**, so buying in the village
    never changes the nameplate. What did not survive is the free route: since invariant 15a
    became "level and purchase" a priced resident has none at any level, so a shop cell asks
    `HomesteadLedger.HasFreeRoute` for its leaf rather than "does this have a requirement", and
    it leads with whichever half is actually binding — the gate while it is closed, the price
    once it is open. Two ids follow from it. A resident's piece id is the companion's id
    **prefixed** (`friend_coral`), because the two id spaces were minted independently and
    already collided — `pebble` is a decor rock *and* a companion, both in save files, neither
    renameable — so the prefix makes the collision unrepresentable and the build gate reserves
    it. And the five retired ids are **rewritten on every load, for ever** (`sunmote →
    friend_puff`, and four more, each to the companion drawing the same critter flipbook),
    because a retired id resolves to nothing and would leave a hole that still counted as
    occupied. Equally, and unchanged: **nothing in the grove touches a board.** Par is derived
    from the board, stars from par, the clock from par and the server's earnings from all three,
    so a grove that granted anything would make every glade a different difficulty per player and
    no validator could prove one fair again.

16c. **A shop shelf is one idea used three times, and browsing never loads the real art.**
    `GroveShelf` is the shop's tab, the browse atlas and the asset scope — three mechanisms that
    have to agree about how the catalog divides, so the division is expressed once. For decor a
    shelf is its slot kind; the two exceptions are the two kinds that are not decor, and they are
    exactly why the concept exists — a resident fits every slot but sells on one shelf, which is
    what used to put the whole roster on every tab and in every tab's scope. A grid cell draws at
    about 170 points against art cut at 512 for an island, so a browse screen reads **generated
    thumbnails out of one atlas per shelf**: one draw call for the grid however many cells are on
    it, and a memory cost bounded by the largest shelf rather than by the catalog. It packs
    *copies*, and that is load-bearing — a sprite may belong to exactly one atlas, and a sprite
    that belongs to one stops having a texture of its own, so packing the shipped pieces would
    mean the grove screen could not draw a single island without loading its whole shelf, which
    is precisely the bound invariant 7b exists to hold. `Validate Art` proves every atlas holds a
    picture for everything on its shelf, because a stale atlas is invisible everywhere else: the
    Editor still has the old one and every other check passes.

16d. **Anything unbounded keeps only what you can see.** `GridView` builds a cell
    once and rebinds it as it scrolls, so a four-hundred-piece catalog costs the same objects as a
    forty-piece one. That is a correctness rule as much as a performance one, and the flicker
    players reported is why: every grid here used to destroy and rebuild itself on any event, and
    every cell entered with a pop from scale zero — so a screen that repainted twice (once when
    the shelf changed, once when its art arrived) played that entrance twice. `Show` is a new
    list and animates; `Refresh` is the same list redrawn and does not. Anything raised by an
    event is a `Refresh`. `GroveFieldView` is the same bargain in two dimensions — a floor is a
    couple of hundred tiles and a phone shows a few dozen — with one rule a list does not need:
    **depth is applied to the whole visible window in one pass, never per tile as it is
    realised.** `SetSiblingIndex` *inserts*, so every tile behind the one just placed shifts and
    the next intended index no longer means what it meant; the field came out looking sorted
    while the hall drew in front of the companion standing one tile nearer the viewer.

17. **A save may only ever be pushed to the account it says it belongs to.** `AccountGate`,
    five lines, and the only rule in this file whose failure has no undo. A sync is pull →
    join → push and `SaveMerge.Join` is monotonic, so aimed at the wrong account it takes the
    better half of two strangers' groves and writes it over one of them. The window is
    entirely ordinary: switching accounts moves the session before the file on disk, and the
    OAuth consent screen backgrounds the app in the middle of it. It is an economy rule as
    much as a data one — earned credits are derived from the star ledger and a glade's golden
    multiplier is a function of the account id, so the same ledger under a fresh uid is a
    fresh, differently-rolled, **fully funded** wallet, which makes copying a save into any
    account that did not earn it a faucet rather than a mix-up (invariant 13, from the other
    direction). Two corollaries are easy to get wrong and both cost a grove. **A save that
    names an account may never have a new one minted for it** — `ResumeAsync` exists next to
    `SignInAsync` for exactly this, because an anonymous account created on behalf of a save
    that already has an owner can never match it, so the device is refused for ever while the
    player believes they are backed up. And **the refusal has to be visible**: a device in
    this state *is* signed in, so anything reading `IsLinked` alone will tell somebody their
    progress is safe while nothing at all is being written. That last corollary is now a
    backstop rather than the ordinary path — see 17a, which gives the refusal a repair.
17a. **A switch is finished on the device before the network is asked for anything.** The
    original order was secure → authenticate → **fetch** → replace, and the fetch is what made
    it breakable: reading the incoming grove decided whether the switch happened at all, and it
    ran in the frame after an OAuth browser handed control back — the process just foregrounded,
    the Firestore stream just re-authenticated, by some distance the most fragile moment in the
    app's life. One unlucky read left the device authenticated as one player and holding
    another's save, and it was reported live as three sentences in a row, none of which was
    true: "that grove could not be loaded", then "this phone is signed in as someone else", then
    a destructive prompt offering to discard twenty-six glades belonging to the same person.
    `SaveService.SwitchTo` makes the swap **local** — the outgoing grove is copied into
    `IAccountArchive` under its own account, the incoming one restored from there if this device
    has played it — so once the credential is in hand the switch cannot stop halfway, switching
    back is instant and offline, and whatever the server holds is folded in afterwards by an
    ordinary sync that retries on the scheduler's backoff. Three rules keep it honest. The
    archive is **a cache and never a backup** — a slot is evicted when six are held, which loses
    a copy and not a grove, because the securing push still runs first and is still the only
    step allowed to refuse a switch. A slot **names its owner inside the file**, so the folder
    hash is safe: one that does not name the account being asked for is discarded rather than
    adopted. And `AccountGate`'s refusal now has a repair — a session ahead of the save is
    completed *forward*, silently, because Firebase persists its user before this code sees it
    and the session only ever moves because a player chose an account, so forward is the only
    direction that can be right. The one path that must still refuse is `redeemPurchase`
    (`AuthoriseAsync(repair: false)`): a receipt redeemed against whichever account is
    authorised would move a purchase between two of them, and refusing costs nothing because
    both stores re-deliver an unfinished transaction for ever.

18. **A real-money product grants currency, and nothing else.** This is invariant 13's
    second clause — *adjudicated* — taken to its conclusion, and it is what makes the whole
    shop cost the save file zero fields. Currency is server-owned (invariant 10), so a
    validated receipt turns into a grant with no client involvement whatever; hearts and
    boosts live in the save and are applied by the phone. A product granting both would need
    the client to apply half a purchase after the server applied the other half — which means
    a record of *"did I already apply this transaction's hearts"*, in the save, merged across
    devices, whose failure mode is somebody paying and receiving nothing. So hearts and
    boosts are bought with **gems**, and a gem debit is an ordinary `CurrencyLedger.TrySpend`:
    idempotent, offline-capable, refused on the next sync if the derived balance could not
    cover it. It is the same two lines that buy a companion. The mirror rule is that a
    gem-priced good may never pay currency — `hearts` and `heart_boost` are the whole list,
    and `StoreCatalog` refuses anything else by name rather than clamping it.
18a. **A transaction is confirmed only after the grant lands, and never before.** The
    ordering is the entire safety property. A purchase arrives as an *unfinished* transaction;
    it goes to our server, which asks Apple or Google whether it really happened, records it
    against `receipts/{store}__{txn}` — **globally**, because replaying one real receipt across
    thousands of accounts is the industrialised attack and a per-player key would validate
    every one of them — and grants. Only then is it confirmed. Everything that can go wrong is
    therefore "still unfinished", and both stores re-deliver an unfinished transaction on every
    launch for ever, so a crash, a tunnel, a flat battery and a server outage are all one bug
    with one fix. That is why **no per-purchase state exists anywhere in the save**: the store
    already keeps the record, far better than a client could. Google's three-day auto-refund
    on an unacknowledged purchase is the one real deadline, and confirming is what
    acknowledges — hence a retry that is aggressive rather than polite. A refused receipt is
    **never** confirmed, however tempting it is to clear the queue: "the server refused" covers
    a product missing from `config/products` as well as a bad receipt, and confirming the first
    charges a player for a configuration mistake and destroys the evidence.
18b. **The shop is one authored list, and the server derives its half from it.** The `store`
    block of `progression.json` is what the game draws *and* what `seed-config.mjs` turns into
    `config/products`. A card promising 750 gems against a server granting 700 is not a bug
    anybody finds by reading either file — it is two files edited on different days, and the
    difference is charged to a real card. Invariant 9a, for money. Two consequences follow.
    There is **no price field and there must never be one**: a price lives in the two consoles,
    differs per storefront, and comes back from the SDK already formatted, so
    `referenceUsdCents` is never shown and exists only so the build gate can prove the ladder
    improves with size and so the "+40%" ribbon is *derived* rather than typed. And a **product
    id is permanent** in invariant 1's full sense — neither store lets one be reused after
    deletion, and a receipt redeemed next year is looked up against whatever the table says
    then, so retune by adding a product and never by repointing one.
18c. **A refund is money leaving, so something has to watch for it.** Buy, spend, refund,
    repeat needs no exploit and no tooling, which is why it is the commonest way a mobile
    economy leaks. `CurrencyLedger.ApplyServerState` had always taken the server's baselines
    rather than the larger of the two — with a comment saying a refund legitimately lowers what
    was granted — so all that was missing was something to lower it. Apple pushes
    (`appleNotification`) and Google is polled (`sweepVoidedPurchases`, hourly). The Apple
    handler deliberately does **not** verify the notification's JWS chain, and that is a
    stronger position rather than a weaker one: it scrapes transaction ids out of an untrusted
    body, keeps only ones this server actually granted, and then asks the App Store Server API
    about each over the authenticated channel `receipts.ts` already uses. Apple's own answer
    moves the money, so a forged POST can at most make us look something up. That reasoning
    holds **only** because every id is re-checked; anything that ever acts on a notification's
    own word must verify the chain first. Balances clamp at zero rather than going negative —
    a player whose credits silently stop rising for a month uninstalls, and repeat abuse is a
    job for the stores' account bans.

19. **Anything a stranger can see is a separate, server-written document.** The save is
    `isOwner(uid)` and stays that way for ever. `players/{uid}` carries the level ledger, the
    streak's dates, the event floors, the chest counters and the ad allowance; a leaderboard
    row needs a name, a number and where the benches are, so it gets `groves/{uid}` — built by
    `publishGrove` from the save it reads with its own credentials, and never writable by a
    client. Widening the save's read rule instead would publish everything else along with it
    and freeze the save's *shape* into a public API that could never change again, which is
    the more expensive half of the mistake.
19a. **A number that goes public stops being derived-and-trusted and becomes adjudicated.**
    Invariant 16g built the grove's worth as a pure function of three client-written id sets
    and said so plainly: safe while private, "forgeable in the one direction that would matter
    if a leaderboard ever reads it". The boards are that leaderboard, so the score is now
    invariant 13's fourth clause — bounded so tightly that forging buys nothing. It splits in
    two: the **earned** half is derived from records the server already validates for currency
    and so is unforgeable by construction, and the **bought** half is clamped to
    `earnedCredits + grantedBaseline`, because everything in it was paid for in currency the
    server derives. The earned half was *companions the keeper ladder reached*, and it is
    structurally **zero** since invariant 15a became "level and purchase": the ladder was the
    only thing in a grove ever handed over, so now every part of a grove was bought and every
    part is clamped. The gate still does work and does it *before* the clamp — a save naming a
    companion its own keeper level has not reached cannot have come about honestly, so that
    entry is dropped outright rather than cut down, which is strictly tighter. The field stays
    in the shape because the clamp is expressed in terms of the split and a future reward that
    genuinely is handed over belongs in it. The client's figure stays a prediction and is
    still what its own screens draw; they agree for every honest player. Before making
    anything else public, ask what a forged version of it would buy, and clamp to something the
    server owns.
19b. **The public name is a second rule on top of the stored one, and the server's answer
    governs.** `RenameOverlay.Clean` asks what a text field owes a database; `GroveNames` asks
    what a string owes the row beneath it. The bidirectional controls are why that is not a
    length check — U+202E re-orders the text that *follows* it, so one name misdraws the whole
    list, and a length cap and a word filter both miss it. Whitespace is tested before the
    forbidden set, because a tab is a control character *and* a word break and deleting it
    joins two words. The word list lives only on the server (a list in a client is a list read
    out of the client), a refused name is never rejected — the player keeps it and is published
    under a handle derived from their uid — and the opt-out (`settings.board`) raises a
    **withdrawal**, because a card still standing after somebody opted out is a
    data-protection failure rather than a stale cache.
19c. **A standing is read off a published distribution; nothing maintains a global ordering.**
    `stats.ts`' bargain for the second time. Nine score deciles and a hundred-row board,
    rebuilt daily, read as one document at O(1) at any player count — against a query that
    costs a hundred document reads per screen open, on a collection that grows for the life of
    the game. A league is not a second ladder either: it *is* `GroveScoreTable.StarsFor`, so it
    needs no tuning, no explaining and no keeping in step. Do not add a "keepers near you"
    board; it needs the exact ordering this design refuses to keep, and the percentile already
    answers the question it would.

19d. **A name is unique because a document id is unique, never because a query said so.**
    A keeper name is reserved by creating `names/{fold}` — so uniqueness is enforced by the
    database's own primary key, at any concurrency, with no index and no scan behind it. The
    obvious alternative is wrong twice: `where("name","==",x)` returns empty for two players a
    second apart and lets both write, and the duplicate that produces is undetectable
    afterwards and repairable only by hand. It is also the shape that does not grow: asking
    whether a name is free is one document read by id at ten players and at ten million, where
    a query is an index over a collection that lives as long as the game. The cost split
    follows from that and is the whole design — the **hint** while somebody types is a direct
    read (`get` granted, `list` refused, so a name can be asked about and the collection cannot
    be walked), and only the **claim** is a function, because only the claim has to be
    adjudicated. `NameCheckScheduler` is what keeps the hint from being a read per keystroke,
    which is roughly a tenfold difference in the bill and is therefore tested rather than
    assumed. Uniqueness can never live in the save: `wallet.displayName` is merged by recency
    (invariant 11c) and no rule over two devices can decide a global fact, so the name is
    invariant 13's fourth clause — the client's copy is what its own screens draw and the
    reservation is what a stranger sees.
19e. **The two folds are one rule, and the runtimes do not agree about Unicode.** A fold is
    what makes `Fern`, `fern`, `FERN`, `F e r n` and the fullwidth spelling one name rather
    than five documents, so it exists in `GroveNames.Key` and `functions/src/names.ts` and the
    vectors run both (invariant 9a, for the sixth time). What is worth not rediscovering is
    that Unity's Mono and Node's ICU **disagree**, and the shared vectors are the only thing
    that can see it: `İzmir` folded to `izmir` on one side and `İzmir` on the other, because
    U+0130 is the one character in Unicode whose lowercase is longer than itself and only the
    full mapping expands it; a Greek name ending in Σ diverged on Final_Sigma; the Latin
    ligature block is not decomposed by Mono at all; and Cherokee and Georgian Mtavruli were
    given lowercase by Unicode after Mono's tables froze. `Agree` closes those by hand, and
    **stops there deliberately** — 27 of the BMP's 256 blocks still disagree somewhere, and
    closing them would mean shipping normalisation tables in a client to make a *hint* exact.
    That is safe only because of how the split is built: a divergence costs a wrong hint,
    corrected by the claim a moment later, and can never produce a duplicate, because a
    reservation is decided by the server's fold and only ever by the server's fold. Before
    changing the fold, note that it may only ever be **loosened** — a tighter one collapses two
    names already held onto one key, which needs a repair job rather than a deploy.
19f. **A published name comes from the reservation, never from the save.** `boardName` reads
    `players/{uid}/private/wallet`, which no client may write, so a modified save changes its
    owner's screens and leaves the board untouched — invariant 19b's "the server's answer
    governs", taken one step past sanitising. Two consequences. The word filter runs **again**
    at publish time on a name that already passed it, so adding a word takes it off every board
    on the next rebuild instead of needing a sweep. And `publishGrove` **claims** whatever the
    save asks for when it differs from what is held, which is what makes a rename made offline
    land with no client-side retry state at all — re-claiming a name already held writes
    nothing, so the settled case is two strings compared and no database work.


19g. **A word list is the cheapest layer of name moderation and the least important; the fold
    is what stops bypasses and reporting is what catches the rest.** The filter that shipped was
    thirteen English words and `flat.includes(word)` over a string with everything outside
    `a-z0-9` **deleted** rather than folded, and every one of its failures was silent. Three
    bypasses, each one keystroke: leetspeak walked past (`5hit`, `f4ggot`, `phuck`); a single
    Cyrillic character *removed itself* and left a word matching nothing (`fuсk` → `fuk`); and any
    name in a non-Latin script squashed to the empty string and was never filtered at all, which
    in a game shipping globally is most of the world. It also refused **Grapevine**, because
    `rape` is a substring of it, in a game about a garden. So the work is in reducing the name
    *and every list entry* to the same canonical form before comparing — `profanity.ts`, four
    forms, because one cannot serve both jobs: folding Cyrillic `а` onto Latin `a` is exactly
    right for catching an English slur in lookalikes and exactly wrong for comparing two Russian
    words. Matching then splits by **how**, never by meaning: `anywhere` and `reserved` are
    substring classes, short, curated and guarded by an allowlist that is *cut out of the
    haystack* before the test (the Scunthorpe repair); `exact` is the 2,600-entry vendored
    multilingual set matched whole-name and per-word, which cannot have a false positive by
    construction and is what makes it safe to be that long and to come from somebody else.
    `nazi`, `porn`, `anal`, `ass`, `cock` and `dick` are deliberately **not** substring entries —
    Nazir, Pornchai, analysis, bass, peacock and Dickens are real, and each one is somebody's
    name. Everything else follows from those two ideas: `Tools/make_name_blocklist.py` refuses
    rather than warns on the four ways a list is quietly wrong, and it models the *matcher* when
    it does (`shiitake` shadows `shit` only once squeezed, which the first version of that check
    missed).

19h. **The list is a document and the takedown is a flag, because both have to move without a
    deploy.** `config/names` overrides the list compiled into the deployment, and the compiled
    one is the floor rather than a nicety: a filter that fails *open* looks exactly like a filter
    with nothing to catch, so an absent or unreadable document must never mean "allow
    everything" — `blocklist.ts` refuses a published list materially smaller than the shipped
    one, keeps the last good one when a read throws, and caches for ten minutes because the list
    is **not** the takedown path. That is `deniedUnix` on the account's name holding, read inside
    the claim transaction and on every publish, and it lives on the *wallet* rather than on
    `names/{key}` because `publishGrove` already opens the wallet — a flag on the reservation
    would be a document read per publish per player, for ever, to carry one bit that is almost
    always zero. It costs nothing in safety, because a denied name's **reservation is never
    released**: the key stays held, so nobody else can claim a name somebody was just hidden for.
    `claimName` has to refuse a re-claim of a denied name, and that is not a detail — it is the
    branch every publish takes once a name has settled, so without it a report would take a card
    down and the very next sync would put it straight back.

19i. **A report is keyed on the pair of accounts, and the client is told almost nothing.**
    `nameReports/{target}/reporters/{reporter}` — the id *is* the idempotency, so tapping twice
    is one report on any device after any reinstall with no client state to remember (invariant
    10a's argument, for something that is not an award), and it is why the threshold counts
    **distinct reporters** rather than taps, which is the only bound that means anything. The
    count is denormalised onto the parent and written in the same transaction, so it cannot drift
    from the documents it counts. Three collapses matter. The server's seven outcomes reach the
    client as **three**: a caller who can tell "counted" from "already hidden" can binary-search
    the threshold, and one who can tell "counted" from "nothing to report" learns which accounts
    are worth brigading. `nameReports` is server-only in **both** directions for the second half
    of that. And the auto-hide runs **without a human** because it is reversible and cheap — a
    brigade of three costs a real player a plainer row and nothing else — where waiting on a
    queue means the offensive name stands for as long as the queue is long. What a person is
    left with is the half a threshold cannot judge, and `firebase/seed/moderate-names.mjs` is the
    desk: queue, show, hide, restore. A restore stamps `reviewedAt` with the count as it stands
    and never deletes the reports, because clearing them would let the same three reporters undo
    the review with one tap, and the reports are the record of why the name was hidden.


20. **A mode is code, and a chapter names one.** A way of playing brings an interaction, a
    fail state and a scoring rule, so content can never add one — but a chapter says which
    mode it belongs to (`mode` in `manifest.json`, absent meaning the classic `glade`), so a
    drop ships a whole second game with no app update. A chapter naming a mode this build has
    never heard of is **skipped whole and reported to nobody**, exactly as `minAppVersion`
    skips one needing newer code: content ships ahead of builds, so an unknown mode is content
    from the future and the honest response is to lose that chapter rather than open it into a
    screen that cannot run it. `GameMode` is a permanent string id for `LevelId`'s reason — it
    reaches the manifest, analytics and loc keys, so an enum's ordinal would be a second
    identity nobody authored.
20a. **A second mode's glade is an ordinary glade, and that is why it cost nothing.** It has
    its own permanent `LevelId`, so its record, its stars, its merge and its rewards are the
    ones every other glade already has — `ProgressionLedger` has no opinion about modes and
    neither does `functions/src/progression.ts`. So a whole second *game* added **no save
    schema version, no `progression.json` retune, no `firestore.rules` change and no server
    work**. Anything tempted to key on a mode goes back to this: the two things that genuinely
    differ are *order* and *unlocking*, and those are per-mode in `CatalogIndex` alone. Totals
    stay mode-blind — `LevelIds` is every glade in the game and is what stars, XP and credits
    are summed over — while `Next`, `Previous`, `OrderOf` and `IsLast` stay inside one mode.
    Chained end to end instead, finishing the classic game would be the price of opening the
    second one, which is precisely what a second mode must not cost.
20b. **A mode may be a whole screen, and the second one is.** The first attempt at a second
    mode reused the board, the light graph and the star rule, and the containment that bought
    was exactly what made it fail: the same grid, the same conduits, the same critters, with a
    different way to fill it in. It was deleted. What a mode is allowed to share is the
    *world* — the palette, the colour arithmetic, the critters, the sounds — and what it must
    share is everything about being a **run**: the heart, the stake (`RunGuard`), the clock
    (`RunClock`), the daily chest, the streak and the star ledger. Those are reached through
    the same Domain classes rather than copied, because a second copy of the run lifecycle is a
    second place that can come to disagree about when somebody is charged. `LevelsScreen.Open`
    is the one place a mode decides which screen opens.
20c. **A level carries a board or a hollow, never neither.** `LevelDefinition.Layout` is null on
    a hollow and `LevelDefinition.Hollow` is null on a glade; the constructor refuses both being
    absent, because a level with neither is a node on a map that cannot be opened and it would
    validate perfectly. `PuzzleFactory` refuses a boardless level rather than throwing on it, and
    `LevelValidator` branches at the top — none of its proofs are about arms mating, and what
    replaces them is stronger rather than weaker: a hollow is *searched*, so the validator knows
    exactly how few sparks finish it and therefore knows the two ways a board is worthless —
    one nobody can finish, and one that finishes itself. Both look perfectly authored in the JSON.
20d. **A hollow authors no numbers at all.** Its whole surface is a grid of text and a string of
    spark colours. Par is the fewest sparks that finish it, found by search, and the star ladder
    falls out of par — three stars is finishing in exactly par, which is a contract a player can
    hold in their head. A typed par is the failure that has no symptom: one too high hands three
    stars to a careless run for ever, one too low makes them unreachable, and neither is visible
    in the file that caused it. `HollowSetup` searches once, lazily, and the build gate refuses
    any board that cannot be proved inside `HollowSolver.NodeBudget`, so a pathological hollow
    fails a build rather than a phone.
20e. **The ordered spark queue is the puzzle, and light never decaying is why.** Because light
    accumulates and never fades, the *set* of cells a player sparks decides the outcome and the
    order they are sparked in cannot — so a pool of sparks would collapse every hollow to "which
    cells". An ordered queue makes it an assignment instead: this red has to go somewhere now,
    and the green behind it can only reach what the red left asleep. The same property is what
    makes a hollow impossible to get stuck in, which is what makes unlimited undo safe, and what
    makes the mode legible to somebody meeting it — the only endings are winning and running out.
20f. **A grove is hard when the pairs cannot all have their way at once, and that is one
    number.** Invariant 5d for Lightweave, learned twice. It was first reported from play as "each
    critter is literally next to their matching light, so it is super easy", and the answer then was
    a **per-pair** bar: no channel could be the straight line between its own two ends. That was the
    wrong shape and shipped a worse complaint — it meant every pair was sent the long way round on a
    route the board had already chosen, which the player experiences as the game refusing the line
    they drew. Reported the second time as "it forces you to take the longer route", which it did.
    <br>What replaced it is `WeaveSolver.Tally.Slack`: the least **total** detour any arrangement of
    a grove has, summed over every pair, above each pair's own shortest possible route. Zero means
    there is an arrangement in which *every* pair goes as directly as it could — six drags and a
    celebration, and that is what all ten original seeds measured. Two or more means the pairs
    contend: any one route may still be perfectly direct, and what the board denies is all of them
    being direct together, so the question is **who yields** and the player answers it. That is the
    whole distinction between a puzzle and a chore, and it is the reason the bar is on the set and
    never on a pair. `ways` is the second reading and still falls down the chapter, but it now counts
    only arrangements within `Latitude` cells of the best one — without a fill rule the count of all
    legal arrangements is astronomical and almost all of it is a needless kink in one line.
    <br>The cheap half runs on the phone and is **the same predicate rather than a proxy for it**:
    `WeaveSolver.AnyTautSolution` is that search with an excess budget of zero, which prunes every
    step that does not shorten the walk ahead of it and settles a candidate in a few thousand
    positions. So the number a level is authored against and the rule the generator holds out for
    cannot drift apart. Both are exponential in the worst case, so neither is a build gate:
    `Survey Lightweave` picks a seed and `WeaveLadderTests` pins what it picked.
    <br>One rule about **placement** survives all of that, and it is the half the second complaint
    actually asked for. `WeaveGenerator.MinReach` refuses a pair whose ends are close enough to join
    by a reflex — a bar on where things stand, which the player can see before committing to
    anything, rather than on which way they must go. It could not have existed under the old rules
    for a good reason: a close pair whose route had to run right round the grove was the fill rule's
    finest trick, and with routing free there is no trick left in one. Note what made the bar
    affordable — a walk whose ends came out close is **re-grown** rather than the whole attempt
    discarded, because rejecting the board raises the odds to the bar's chance *to the power of the
    pair count*, and measured that alone moved a reach of five from ordinary to one seed in five.

20g. **A mode may bring a rule no board can demonstrate, and the fix is to make the board
    demonstrate it.** Reported as "even though I wake up all the critters, the game doesn't end" —
    which was not a bug and was indistinguishable from one. A weave was won when every critter was
    awake **and** no bare ground was left; the shortest route always wakes a critter; so the ordinary
    way to meet a grove was to drag every crystal straight at its critter, collect the mode's biggest
    celebration six times over, and watch nothing happen. There is no way to reason from that to "the
    grove also has to be covered".
    <br>The first answer was to *say* it — a `Mechanic` shown once, and a standing line naming the
    state for as long as it lasted. Both were right and neither was enough, because the rule itself
    was the fault: a win condition nothing on the board can point at is one the player is always
    being surprised by, and it was also what made every sensible route wrong (20f). So the rule was
    replaced rather than explained better. A **bead** is a cell one channel must be threaded through,
    drawn on the ground in that channel's own colour; it asks for the same detour and *points at
    where*. It constrains twice over, which is what makes one worth more than its own detour — a
    doorway to one colour and a wall to the other five — and that is the congestion the fill rule
    used to manufacture, bought with something the player can see and reason about.
    <br>Three things carry over and each is load-bearing. A bead is only placed on a cell lying
    **off every shortest route** between its own pair's ends, which is checkable before it is placed
    and is 5d's rule again: a bead threaded on the way past is decoration. The state that reads as a
    broken game still exists — every critter awake with a ring unthreaded — so the **standing line
    stays**, now pointing at something visible rather than being the only evidence a rule exists.
    And a bead needed its **own silhouette**: drawn first with `Art.Ring`, which is what a sleeping
    critter already wears to name its colour, the finale came out carrying eleven circles in six
    colours told apart only by whether a creature stood inside one. `Art.HexRing` is the third shape
    against the critter's circle and the crystal's diamond.
    <br>The generalisation is unchanged and now has a second half. Before shipping a mode, ask which
    of its rules a board can *show* — and if the answer is "none of them", the rule is probably wrong
    rather than merely untaught. `weave_fill` is a **retired lesson id and must never be reused**: an
    id travels in the save exactly like a level id, so a rule that is deleted spends its id for ever
    rather than having it re-pointed at a rule it never described.


## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Persistence/ Progression/ Homestead/ Cloud/ Localization/ Analytics/ AssetPipeline/
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
  App/ Board/ Screens/ Dev/
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode; Domain, Cloud, Presentation)
Assets/StreamingAssets/Content/    manifest.json, chapters/, homestead.json, loc/
```

`Assets/Game/CONTENT.md` is the authoring and pipeline guide. Read it before touching
content, assets or localisation.

## Verifying

The Unity Editor is often not running, and the MCP bridge is unavailable whenever
scripts fail to compile. Do not guess — verify offline:

- **Compile check:** run Unity's bundled Roslyn directly. See
  `verify-content-without-unity` in the memory directory for the exact command,
  including the reference-assembly and shim details that took several attempts to
  get right.
- **Content check:** parse the StreamingAssets JSON, prove every level solvable,
  derive par, confirm every loc key resolves.
- **Difficulty check:** `python Tools/verify/difficulty.py` — what each glade actually asks
  of a player, counted rather than argued about. Not a gate; see invariant 5d and
  *What makes a glade hard* in `CONTENT.md`.
- **Word list check:** `python Tools/make_name_blocklist.py --check` proves the checked-in list
  is what the tool would write, and refuses the four ways a blocklist goes quietly wrong. The
  filter itself is `npm --prefix firebase/functions test` (`names.mjs`, `reports.mjs`).
- **Weave determinism check:** `python Tools/verify/weave.py` builds every shipped Lightweave
  grove on **both** the bundled .NET 8 and **Unity's own Mono** and diffs them. A weave board is
  generated rather than authored, on a desktop at authoring time and again on the player's phone,
  so "the same seed deals the same board everywhere" is the property the whole mode rests on and
  the one nothing else checks. It compares boards, beads and difficulty rather than checking any
  of them against a number, so there is no expected table to go stale. See *Hard-won facts* for
  the divergence that made it.
- **Name fold check:** `Tools/verify/names.py` runs `GroveNames` against the shared vectors
  **on Unity's own Mono** (`MonoBleedingEdge/bin/mono.exe`), not on the bundled .NET. That is
  the whole point of it: the first version ran on .NET 8, whose ICU agrees with Node about
  everything, and it passed happily with the Cherokee mapping deleted. A check that cannot fail
  is not a check — see invariant 19e.
- **Why a test says "needs the Editor":** `GLIMMER_WHY=1 python Tools/verify/tests.py` prints
  the native call that stopped it. Sometimes that is a fact about the code under test and
  sometimes it is a limit of the runner, and only the message tells them apart — it is how the
  `Debug.Log` one was found. The runner clears `Debug.unityLogger.logEnabled` for exactly that
  reason, so an ordinary log line on the path being tested no longer decides whether the rule
  can be proved offline.
- **In the Editor:** `Glimmer Grove ▸ Validate Content`, `▸ Validate Art`, and
  Test Runner (EditMode).

Builds are gated: `ContentBuildGate` fails the build on any content error.

## Hard-won facts

- **Addressables must be ≥ 4.0.1.** 2.x calls `Object.GetInstanceID()`, which Unity
  6000.3+ made an *error*-level obsolete. 4.0.1 guards it behind `UNITY_6000_3_OR_NEWER`.
- **`GLIMMER_ADDRESSABLES` comes from asmdef `versionDefines`, not Player Settings.**
  Player Settings defines are per build target — one added on Standalone is absent on
  Android and iOS, and since assets no longer live in `Resources/`, that would ship a
  mobile build with no art and no error explaining it.
- **`m_BuildAddressablesWithPlayerBuild: 1`** is set explicitly in the project asset,
  not left to the per-machine Editor preference, so CI and teammates build identically.
- **A `[Serializable]` class field is never null after `JsonUtility`, so never test one for
  null.** It instantiates the field even when the JSON has no such key, so `dto.hollow != null`
  is true for every level ever parsed — which read all forty shipped glades as hollow fields,
  dropped every one and failed the Android build with eighty errors. The fixed shape is a value
  a real block cannot hold (`HollowDto.IsAuthored`), which is invariant 11b's rule from the
  other direction. Two guards now: `HollowTests` reads both shapes back through `ContentMapper`
  (Editor-only, because the serialiser is a native call and is the subject), and `compile.py`
  refuses a null test on any class-typed field of a DTO. Note why nothing offline saw it —
  Python's `json` returns nothing for a missing key where Unity returns an object, so the
  mirror and the game disagreed about the one thing that mattered. Same class of divergence as
  Mono and ICU in invariant 19e.
- **`LevelDefinition.Layout` is null on a hollow, and nothing in the language says so.**
  Nullable reference types are off, so a reader that forgets is a `NullReferenceException` in
  whichever tool touches it first — which is exactly what shipped: `ContentValidation` and
  `ContentAuthoring` both printed `level.Layout.Width`, and both blew up the moment a
  boardless level existed, on a path no offline gate runs. `compile.py` now refuses a file that reads
  `.Layout.` without anywhere saying it knows the thing can be absent (`HasBoard`, or a null
  check on `Layout`/`Hollow`). The rule is coarse on purpose: four files touch it, so a false
  positive costs one word and a missing guard costs a crash in the Editor's own validate.
- **A Unity magic method is an engine rule, not a language rule, and the offline compile
  is blind to it.** `public bool Awake(int i)` on a `MonoBehaviour` compiles perfectly in
  Roslyn and the Editor then refuses the whole script — *"Script error (BoardView): Awake()
  can not take parameters"* — so a green `Tools/verify/compile.py` is not by itself proof
  the Editor will accept a build. It shipped exactly once, on `BoardView.Alight`, which read
  far better as `Awake` and cost a round trip through the Editor to find. `compile.py` now
  walks every class that ends up a `MonoBehaviour` and refuses a method named after one of
  the no-argument messages; the parameterised ones (`OnApplicationPause(bool)`,
  `OnCollisionEnter`, `OnAnimatorIK`) are deliberately not on the list, because flagging
  those would make the check noise nobody reads. It lives in `compile.py` rather than in a
  seventh script for the reason the importer hook is not a menu item.
- **Unity only re-resolves packages and reimports on window focus.** If a change seems
  not to apply, the Editor probably has not been clicked.
- **An Editor launched from the Hub gets a minimal `PATH`, and one failed post-processor
  abandons the rest.** Measured on macOS: `/usr/bin:/bin:/usr/sbin:/sbin`. Homebrew on Apple
  Silicon installs to `/opt/homebrew/bin`, and EDM4U's iOS resolver searches the process
  `PATH` plus `/usr/local/bin` — the *Intel* prefix — so it cannot find `pod` on any Apple
  Silicon Mac even though CocoaPods works perfectly in a terminal. What that costs is not one
  missing step: EDM4U runs `pod install` from a `[PostProcessBuild]` at order 4, **Unity
  abandons every remaining callback when one throws**, and so it also took down
  `IosPrivacyPlist` (order 100) — the only writer of `NSUserTrackingUsageDescription` and the
  only thing linking `AppTrackingTransparency.framework`. The observable result is an Xcode
  project that exists and looks complete, with no `.xcworkspace`, no tracking prompt, and a
  link error twenty minutes into an Xcode build naming Apple's classes rather than CocoaPods.
  `MacToolPath` fixes the cause in-process (repo-owned, so a fresh clone and CI get the same
  answer) and `IosWorkspaceGuard` proves it happened, because a Podfile with no workspace
  beside it is invisible everywhere else.
- **Nothing that decides a cell may be a `float`, because the runtimes disagree about them.**
  `WeaveGenerator` capped each walk at `(int)(free / (float)walksLeft * 1.3f)`, and 1.3 has no
  exact binary form: thirty free cells across three walks computes 12.99999952…, which truncates
  to **13** if the multiply stays in single precision and to **12** once it is promoted to double.
  Both are legal for a C# compiler and the runtimes chose differently — .NET 8 answered 13 and
  Unity's Mono answered 12 — so the opening grove of the Weftwood was *two different boards*
  depending on who dealt it, and a phone runs IL2CPP, which is a third code generator again. A
  one-cell budget difference is not a rounding error there: the budget decides where the first
  walk stops, so every later walk starts from different ground and the whole grove is re-dealt.
  That is the one thing a generator whose output is proved at authoring time must never do. It was
  found by `WeaveLadderTests` passing in the Editor and failing offline — the same shape as the
  Mono/ICU divergence in invariant 19e, in arithmetic rather than Unicode, and invisible to every
  check that runs on one runtime. The slack is now the exact fraction `13/10` in integer
  arithmetic, and `Tools/verify/weave.py` diffs both runtimes so it cannot come back quietly.
- **Two Google ads SDKs cannot share an APK, and a mediation adapter can drag in the second
  one.** Google ships the legacy `com.google.android.gms:play-services-ads` and a
  next-generation `com.google.android.libraries.ads.mobile.sdk:ads-mobile-sdk`; both define
  `com.google.android.gms.ads.*`, so Gradle stops at `checkDebugDuplicateClasses` with a
  screenful of `Duplicate class` lines. This project holds the legacy one permanently, because
  the GoogleMobileAds plugin is how `UmpConsentGateway` gets UMP and there is no standalone UMP
  package — so the *adapter* is what has to bend. LevelPlay's AdMob adapter switched to the
  next-generation SDK at **5.19.0.0**; **5.18.0.0** is the last version on the legacy one and is
  what `ISAdMobAdapterDependencies.xml` must pin. The Network Manager will keep offering the
  newest, and taking it puts the build straight back here — so the version is a decision, not a
  default. Nothing offline can see this: the adapter is native, so `compile.py` stays green and
  the failure is twenty minutes into a Gradle run. Before installing any mediation adapter,
  diff `mainTemplate.gradle` and look at what ad SDK it pulled.
- **A post-processor ordered after another one is not guaranteed to run.** The corollary of
  the above, and general: ordering expresses *dependency*, never *safety*. Anything that must
  happen has to be robust to the step before it throwing — or be ordered before it.
- **Sign in with Apple on iOS cannot use the generic IDP path, and the refusal kills the
  process.** `FirebaseAuth` on iOS calls `fatalError` the moment `apple.com` reaches
  `FederatedOAuthProvider` — *"You must use the Apple SDK for Sign in with Apple."* A Swift
  `fatalError` is not an exception, so no managed `catch` runs and the app dies on the tap.
  Nothing offline can see it: the refusal is inside Apple's framework and fires only on a
  device, so it compiles, validates and ships looking correct because Android — where the
  generic path *is* allowed for Apple — works. Hence `Assets/Plugins/iOS/GlimmerAppleSignIn.mm`
  and `AppleSignIn.cs`; Google uses the generic path on both platforms, and so does Apple on
  Android. Two details that cost a day each: `LinkCredential.AccessToken` is **not** unused by
  Apple — Firebase's fourth `GetCredential` parameter is named after Google's access token but
  for `apple.com` it must carry Apple's `authorizationCode`, and a credential without one is
  refused with **the same sentence** a malformed token or a mismatched nonce gets
  (`"Invalid OAuth response from apple.com"`), which is why `AppleSignIn.Describe` logs the
  decoded JWT's `aud`, `iss`, nonce agreement and code length. And the **entitlement is written
  by `IosAppleSignInBuild`, not by Xcode** — Xcode writes one when a team is first selected, so
  the first build appears to have it, and Unity rewrites the whole project on every build, so
  it vanishes on the next one and takes Apple sign-in with it. It is a **paid-account**
  capability; a free Personal Team cannot sign it.
- **A Functions secret is pinned at deploy time, so a correct key can produce a 401.**
  Firebase Functions v2 records the secret *version* in the function's config, so
  `firebase functions:secrets:set` changes nothing about a running function until it is
  redeployed — while `functions:secrets:access` reads *latest*. Both readings are true at once,
  which is what makes it expensive to diagnose. **Redeploy every function that names the
  secret**, not only the one being worked on, and **destroy the old versions** once nothing
  uses them (`secrets:destroy NAME@n`; `secrets:prune` will not, because it counts a secret as
  in use by name rather than by version) so a stale pin fails loudly instead of signing with
  the wrong key. Reading `redeemPurchase` logs: a 401 from the production App Store endpoint
  followed by success on the sandbox one is the **normal** path for a sandbox purchase. The
  failure is both endpoints refusing.
- **A newly created 2nd-gen callable has no public invoker binding and answers every call with
  401.** `firebase deploy` neither does this nor warns about it, and from a client it is
  indistinguishable from being signed out — the function deploys and logs cleanly. Functions
  that already existed keep the binding they were created with, so only the new one fails:

      gcloud run services add-iam-policy-binding <lowercased-name> \
        --region=europe-west1 --member=allUsers --role=roles/run.invoker

- **The sprite atlas file extension selects the importer.** A `.spriteatlas` written in the V2
  format imports as editor data with a plain `AssetImporter` and produces no `SpriteAtlas` at
  all — every address resolves, every check passes, and the shop draws an empty grid. It must
  be `.spriteatlasv2`, and `EditorSettings.spritePackerMode` must be `SpriteAtlasV2`
  (`Set Up Project` sets it, and it ships in `ProjectSettings/EditorSettings.asset` for
  `m_BuildAddressablesWithPlayerBuild`'s reason).
- **The importer hook does not address art copied in while the Editor is closed or mid-reload**,
  which is every run of the art import tools. Unaddressed sprites load as nothing and cells
  draw blank. `Glimmer Grove ▸ Addressables ▸ Sync All Assets` is the repair; the build gate's
  `AddressableAudit` is what stops it ever shipping that way.
- **A preprocessor fires on first import only**, so art that landed before an import rule
  changed keeps whatever it was given, silently — hence
  `Glimmer Grove ▸ Reapply Art Import Rules`. **It must batch.** Calling `SaveAndReimport` per
  texture is one round trip to Unity's import workers each; 335 back to back crashed both
  workers and wedged the Editor in a domain reload it could not finish. Use
  `StartAssetEditing`/`StopAssetEditing` with a `finally` — and the `finally` is not optional,
  since an exception between the two leaves the asset database in editing mode, which looks
  exactly like the freeze it prevents. Texture caps are **per folder**
  (`ArtImportRules.Caps`): 512 for grove props and companions, 256 for critter frames, 1024 for
  UI, 2048 only for backdrops and map strips. A texture costs its dimensions, not its file
  size, so a blanket 2048 is a bundle nobody can ship.
- **`JsonUtility` has two parse refusals that read as logic bugs.** It rejects a number written
  `.5` (so a fixture with one never reaches the rule it is about), and it **truncates a string
  at an escape sequence** — which shifted every expectation in a shared vector array by one
  field and read exactly like a bug in the code under test. Test vectors carrying awkward text
  carry **code points** alongside the string, and the other runtime asserts the two agree.
  Related: `[Serializable]` is silently load-bearing — an insertion above a DTO that separates
  it from its attribute makes `JsonUtility` return `null` for that array, and the tests driven
  by it stop running rather than failing.
- **The Firebase Unity SDK's `Firebase.Functions` ships as source with its own asmdef**, so
  `GlimmerGrove.Cloud.asmdef` must reference it explicitly (App, Auth and Firestore are plugin
  DLLs and auto-reference), and that source needs `Google.MiniJson.dll`, which lives in the
  **app** package. All Firebase packages must share one version.
- **Google's UMP plugin must come from OpenUPM as a package, never as the `.unitypackage`.**
  The `.unitypackage` unpacks as loose files under `Assets/`, so it is not a package, so it
  carries no version, so `versionDefines` never fires, `GLIMMER_UMP` is never defined,
  `UmpConsentGateway` compiles to nothing and **nobody is ever asked anything** — a consent
  failure that is completely silent. `GooglePackages/fetch.ps1` pulls `com.google.ads.mobile`
  from OpenUPM beside the Firebase tarballs.

## Tools

Everything here runs without Unity unless it says otherwise.

- `Tools/verify/` — `compile.py`, `tests.py`, `content.py`, `loc.py`, `names.py`,
  `difficulty.py`, `weave.py`. See *Verifying*.
- `Tools/chapters/*.py` — one module per glade chapter; regenerates the shipped JSON and
  `--check`s itself against it. `author.py` is the shared board DSL (`cross`, `root`, `briar`,
  `path`), and it derives a taproot's start rotations from the taps the root should cost rather
  than leaving four numbers that have to agree.
- `Tools/hollow/` — the Hollow's rule mirror, board generator and `build_chapter.py`. The
  mirror is never authoritative; the shipping C# solver is what `Validate Content` runs.
- `Tools/grove_art.tsv` + `import_grove_art.py` — one row per grove piece (source, permanent
  id, slot kind, price, scale, lift, name). Copies the art, writes the loc string, regenerates
  the catalog and bumps `groveVersion`. It **refuses to remove an id it imported before**,
  because a piece id is in save files twice over.
- `Tools/make_chapter_art.py` + `chapter_art.tsv` — map strips and per-level backdrops, graded
  from the chapter's own JSON colours, so retuning a level's `accent` regrades its backdrop.
- `Tools/make_waterfall.py`, `Tools/make_grove_animation.py` — generated decor flipbooks. Rows
  they own are marked `_generated` rather than `_imported`, or the next import run warns
  forever about a row it no longer owns.
- `Tools/make_name_blocklist.py` — vendors LDNOOBW (27 languages, CC-BY-4.0); `--check` proves
  the checked-in list is what the tool would write and refuses the four ways a blocklist goes
  quietly wrong.
- `firebase/seed/seed-config.mjs` — publishes `config/progression`, `config/products`,
  `config/grove`, `config/names` from the content files. `moderate-names.mjs` is the
  moderation desk (queue, show, hide, restore).

## Current state

Everything below is *what is true now*, not how it got here. The reasoning behind a rule
lives in **Invariants**; the traps live in **Hard-won facts**. If a decision is worth
re-reading before changing something, it is in one of those two sections and not here.

### Built and verified

- **Content pipeline** — levels as data in `StreamingAssets/Content/`, stable `LevelId`s,
  manifest-built `CatalogIndex`, lazy chapter bodies, `Content ▸ Sync Manifest`, build gate.
- **Save** — versioned atomic file with checksum, backup rotation, corrupt-file recovery,
  tested migrations, monotonic merge (`SaveMerge.Join`). **Save schema v20.**
  Content schema: manifest and chapter bodies **v2**, grove body **v3**.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google
  linking, per-account local archive for switching, `SyncScheduler` debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water
  floors only. Hearts and hints are produced/spent ledgers (`RegenLedger`).
- **Retention** — daily chests, streak (collected by hand), golden glades, event calendar,
  percentile standings, per-glade records (turns + time).
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads,
  refund sweeps, server-adjudicated grants.
- **The Grovement** — 14x14 isometric tile floor, land regions bought with credits, decor
  bought by the copy, residents projected from the companion roster, derived grove worth.
- **Boards** — public `groves/{uid}` cards, published rank distribution, unique keeper names
  with server-side filtering and reporting.
- **Two modes beyond the classic glade** — the Hollow (`h01_emberfall`) and Lightweave
  (`w01_lightweave`). See *Modes* below.
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).
- **Verifying** — `Tools/verify/` in the repo (see the *Verifying* section).

### Content shipped

| Chapter | Mode | Levels | Par range | `timeFactor` | Subject |
|---|---|---|---|---|---|
| `c01_shallows` | glade | 10 | 34–61 | 2.20 → 1.50 | the verb, then brittle stone, roots, taproots, duskcaps |
| `c02_millvale` | glade | 10 | 36–63 | 1.95 → 1.55 | the crossing |
| `c03_amberwood` | glade | 10 | 44–70 | 1.85 → 1.50 | colour as the subject; no new rule |
| `c04_nightbriar` | glade | 10 | 42–69 | 1.90 → 1.45 | the briar |
| `h01_emberfall` | hollow | 10 | 1–2 sparks | — | ladder is *how few openings win*: 7,8,6,4,2,3,4,1,4,1 |
| `w01_lightweave` | weave | 10 | 19–64 | 5.0 → 3.2 | pairs 3→6, beads 0→5; slack 2 → 8 and ways 230 → 2 |

The Weftwood's `timeFactor` moved because **par** did, not because the clock did. Par used to be
the carved solution's length, which fills the grove — so a 7x9 board carried a par of 63 whatever
its pairs were doing. It is now the sum of the pairs' own floors plus a cell of looking per pair
and per bead (`WeaveLayout.Par`), which on the same board is 64: the multiplier moved so the limits
would stay where they were rather than being cut by a third on the drop that also changed the
puzzle. Three stars is still `par` seconds, so a clear is worth exactly what it was worth.

Par is **never** monotonic within a chapter — par is length, not difficulty, and ten rising
numbers read as a treadmill. A chapter's dip is usually its taproot board (one tap moves
several conduits and par charges once).

Chapter art is generated: `Tools/chapters/*.py` regenerate the shipped JSON and self-check
against it; `Tools/make_chapter_art.py` reads names and colours out of the chapter's own JSON
and **scales a source to whole strips** rather than stretching to them, which is what decides
a chapter's strip count (Shallows 6, Mill Vale 4, Amberwood 5, Nightbriar 6).

### The board's vocabulary

One verb — turn a conduit, light a critter — with modifiers, and no second solver:

- `~` **brittle stone** — survives a fixed number of turns. Belongs on a tile the player
  cannot simply try, so in practice a crossing.
- `!` **rooted** — cannot be turned. Authored at `/0` (invariant 5c).
- `&A` **taproot** — every conduit carrying the rune turns as one; charged once in par.
- `x` **duskcap** — any light wakes it and a woken one means the glade is unfinished. Its
  ford must sit on a *cycle* of the live network.
- `=NS+EW` **crossing** — two strands through one tile that never meet. Straight is inert;
  twisted is worth exactly one tap. No hub disc.
- `%NS+EW` **briar** — four arms drawn, two conducting; one tap swaps which. Order of the
  pairs matters (unlike a crossing). Straight is worth one tap, twisted four.

`Tools/verify/difficulty.py` is the instrument that says whether any of that is doing work —
see invariant 5d. `hazards` is the metric it replaced and is wrong; `arms`/`wins`/`glance`/
`dark` are the ones to author against.

### Modes

**Classic glade** — `PlayScreen`. Turn conduits, light every critter. The clock is the fail
state and the move budget is the backstop under somebody flailing: at the shipped factors,
running out of *turns* first needs ~1.3 taps a second sustained without solving.

**Hollow** (`HollowScreen`) — a field of sleeping critters and a short *ordered* queue of
sparks. Light accumulates and never decays, so a player can never be stuck, the only endings
are winning and running out, and unlimited undo is safe. Par is the fewest sparks that finish
the board, found by search (`HollowSolver`), never authored. Boards are searched for, not
typed — `Tools/hollow/`. Duskcaps parse and are implemented but no hollow uses one, and that
is a finding rather than an omission: every critter must wake and a waking critter feeds all
four neighbours, so a duskcap is either ruined by the board or unreachable.

**Lightweave** (`WeaveScreen`) — drag a channel from each crystal to its critter without
crossing, and thread every **bead**: a hexagonal ring on the ground that its own colour must pass
through and no other colour may enter. Where a channel goes is otherwise entirely the player's
business. Six colours (every mix except `Energy.All`, which is what a woken critter wears, so a
seventh pair would sleep in the colour of being awake).

It used to be won only by covering **every cell of the grove**, which was the whole difficulty and
was also two faults at once: the sensible route was almost always wrong, and the state it produced
— every critter awake, nothing happening — read as the game failing to notice a win. Both are
invariant 20g, and both are gone. A bead asks for the same thinking and points at where.

Boards are *generated from a seed*, so determinism across runtimes is the property the mode rests
on — `Tools/verify/weave.py` diffs .NET 8 against Unity's Mono, boards and beads alike.

A grove is measured two ways and needs both (invariant 20f). **`slack`** is the least total detour
any arrangement has, over and above every pair's own shortest possible route: zero means every pair
can go as directly as it could, all at once, and the grove asks nothing. **`ways`** is how many
arrangements land within a couple of cells of the best one — how much of what a tidy player tries
will work. Slack is meant to climb down the chapter and ways to fall. Both come from `WeaveSolver`,
which is an authoring instrument and deliberately **not** a build gate (the search is exponential in
the worst case, so a gate would fail builds nobody can reproduce);
`Glimmer Grove ▸ Content ▸ Survey Lightweave` reports them and its `SeedSearch` picks a level's seed
against both. The cheap half — is there any arrangement in which everybody goes straight — is
`WeaveSolver.AnyTautSolution`, the same search at an excess budget of zero, and it runs on the
phone as the generator's acceptance bar. `WeaveGenerator.MinReach` is the separate rule about
*placement*: no pair's ends may be close enough to join by a reflex.

A weave record is a **time** and nothing else, and the win panel is deliberately **routeless** —
there is no move count here to compare against the board's own solution, so a route bar would print
the same sentence for every player who ever finished.

Shared by every mode: `RunLedger` (record, chests, streak, reward, analytics — and it builds
the `RunOutcome` *before* folding the record in, because half of what it describes stops being
true after), `RunScreen` (defeat/pause/forfeit panels), `RunGuard` (a committed run is paid
for however it ends), `RunClock`, `PlayRoute` (which screen opens a level — answered once),
`RunWording` (turns vs sparks). `LevelsScreen.Open` is the one place a mode decides its screen.

### The numbers

Free play collects about **593 credits and 6 gems a day**; `Tools/verify/content.py` and
`Validate Content` both derive and print this, so never hard-code it.

- **Companions** — 31, one free (`monarch`, the starter), 30 priced 800 → 30,000
  (~270,500 total). Unlock is keeper level **and** purchase.
- **Grove catalog** — 493,770 credits complete: 154,770 decor and homes, 68,500 land
  (9 regions, a free 6x6 starter), 270,500 residents. 150 priced pieces, of which 99 sell
  in bundles of ten at what one used to cost. Home ladder 5 rungs, first free.
- **Grove star ladder** — 10K / 20K / 50K / 100K / 200K, content in `homestead.json`.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted). A loss costs one.
- **Hints** — pool of 3 account-wide, one back every 8h, ceiling equals the cap (a granted
  hint at a full pool is refused, not clamped). A hint charges no moves.
- **Streak** — a 7-night lap that wraps: 500 credits, 1 heart, 5 gems, 2 hearts, a 12h boost,
  3 hearts, 10 gems.
- **Ads** — five placements, all opt-in, no interstitials: `heart_refill` 2 hearts,
  `coin_bonus` 1,000 credits, `run_continue` 30s, `win_bonus` credits, `hint_refill` 1 hint.
  Daily caps 20/12/16/12/10 — deliberately above what any network will fill, so they bind
  only as a lever that can be lowered. `AdRules.MaxDailyCap` 30 is a hard `const`.
- **Shop** — 13 products. Gems 100 → 8,500 for $0.99 → $49.99; coins 2,500 → 75,000 for
  $1.99 → $39.99 (the whole catalog is ~$146); starter bundle a $2.99 non-consumable;
  5-heart refill 50 gems, a day of fast hearts 30 gems.
- **Stars** — the worse of moves and time. Gold is `par × 1.00` seconds, silver `par × 1.50`,
  held against **par** and not against the limit, so tightening a clock cannot deflate the
  economy. `difficulty.clockScale` (0.6–2.0, `DifficultyLimits`) multiplies every limit and
  reaches nothing stored and nothing published.
- **Account prompts** — 2 chapter asks, 3 purchase asks, one shared 48h quiet period.

Everything in that list except the shop ladder is **content** in `progression.json` or
`homestead.json` and retunable without an app update. Re-seed after any change to it.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`.
**Thirteen functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`,
`adReward`, `appleNotification`, `sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`,
`withdrawGrove`, `publishGroveRanks`, `claimName`, `reportKeeperName`.
`firebase/README.md` is the guide; `firebase/e2e/smoke-test.mjs` is **83/83 live**.

Client half is `Assets/Game/Scripts/Cloud/` (assembly `GlimmerGrove.Cloud`), Firebase Unity
SDK 13.15.0 as vendored UPM tarballs under `GooglePackages/` (gitignored — run
`pwsh GooglePackages/fetch.ps1` on a fresh clone). `GLIMMER_FIREBASE` comes from asmdef
`versionDefines`; `Boot` picks the real backend over `NullCloudBackend`.

Two rules about the live suite, both learned the hard way. It signs in as a **new anonymous
account every run**, so anything derived from the account id varies — never hard-code a
figure, derive it from what the config publishes (this has already broken the earned-credits
case and three streak cases). And it is **sensitive to cold starts**: re-run before believing
a failure that arrives in the first minute after a deploy.

**Owed, in order of cost if forgotten:**

1. The thirteen products in the **Play Console** (iOS is done and verified end to end —
   a sandbox `gg_gems_1` redeemed on 2026-08-24).
2. **View financial data** on the Play service account, or the refund sweep silently no-ops.
3. The `appleNotification` URL registered for **both** production and sandbox.
4. AdMob **instances** under each of the ten LevelPlay ad units (the units exist on both
   sides; only the mediation link between them is missing).
5. Fill in `app-ads.txt` from each network's dashboard and host it on the domain in both
   store listings; turn on in-app bidding.
6. Delete the ~210 synthetic saves and the name reservations the live suite leaves behind.

Ads **fill** as of 2026-08-24: all five placements load on device against the LevelPlay apps
rebuilt alongside the bundle id change, from ironSource's own network and Unity Ads, with no
AdMob instances yet. The long-standing `Mediation No fill (509)` is gone. What is still unproven
is the last link — a real impression reaching `adReward` and paying — because that needs a
watched video rather than a load.

`UmpConsentGateway` has now been compiled **and run**: `status=NotRequired, canRequestAds=True`
on a device outside the EEA. What that does not prove is the branch that matters — a form
actually shown — which still wants a device or an emulator inside the EEA, or a debug geography
override.

### House rules for the UI

Each of these was learned two or three times in different files. They are not invariants —
they are the things that go wrong in Presentation and are invisible in a compile, a validator
and a screenshot of the source.

- **`Destroy` lands at the end of the frame.** Hide a region before destroying it, or the
  outgoing panel draws over its replacement for a frame — which, with everything entering
  from scale zero, reads as a flash.
- **`Show` animates, `Refresh` does not.** Anything raised by an *event* is a redraw. A
  screen repainted by a wallet change, an art scope landing or a ledger event must not
  replay its entrance; `GridView`/`GroveFieldView` bind cells rather than rebuilding them.
- **`Tween.Pop` reads the transform's current scale as the size to spring back to**, so a
  second pop landing inside the first captures a half-sprung scale and the control shrinks
  permanently. Reset to 1 first, and only animate when the state actually moves.
- **A panel with several exits reports through none of them reliably.** Put the safe outcome
  on `OnDestroy` and make the exception the thing somebody declares — `AdOfferOverlay.Dismissed`,
  the pause menu's unlatch, `BoardView.Locked` as a property that raises `OnChanged`,
  `WeaveView.Finishing`. Exactly one of `Rewarded`/`Dismissed` fires, so both must be handled.
- **Repaint from an event, never from a callback on the panel that changed something.**
  `CompanionLedger.Changed`, `CloudSaveService.IdentityChanged`, `GameSettings.Changed`.
- **`UIKit.Box` pivots centre.** Anchoring a child to an edge puts half of it outside, and
  growing a panel puts half the new room above the art.
- **Measure a painted shape's face rather than centring on its sprite.** `PillFaceLift`,
  `SquareFaceLift`, `NodeFaceLift`, the win banner's `RankLift`, the iso tile's derived skirt.
- **`UIKit.Label` defaults to `Overflow` with no clipping** — an over-long translation keeps
  drawing rather than truncating. Anything holding a translated string needs `UIKit.Shrinkable`.
- **Generate art the screen cannot afford to be missing.** An `Image` whose sprite has not
  arrived is a white rectangle, so anything on a dark or ceremonial screen is
  `Art.Bloom`/`Dial`/`Gradient`/`PrismRing`/`IsoTile`/`Ring`/`Glow` rather than an address.
- **Controls go in `View.Safe`, art stays full-bleed.** Letterboxing a backdrop to dodge a
  camera cutout is a worse picture than the cutout. iOS reports its inset a frame or two after
  a cold start, so the node re-fits itself rather than reading the value once in `Build`.
- **Timing rules live in Domain and are tested** — `Cue`, `TweenCycle`, `GroveGrowth`,
  `GroveUnveil`, `WeaveTempo`, `CoachStroke`. Every sequence is bounded and **the rate gives
  way**, so a bigger board is never a longer wait. Motion is the one subsystem whose failures
  show up only in play, which is why the arithmetic has to be reachable without an Editor.
- **A lesson about a gesture is shown, not described.** A `TipOverlay` with a ring and two
  sentences is the right shape for a *rule*; Lightweave's two lessons are a **verb** — this
  mode is dragged, and a channel goes *through* a ring rather than stopping at it — and a
  sentence describing a movement has to be turned back into the movement by whoever reads it.
  So `Trace` lights the route on the real board and `CoachHand` walks a hand along it, the
  sentence is cut to the half words are good at, and `Target` still rings only the thing being
  named. Two rules the demonstration keeps: it never traces the carved solution (`StrokeThrough`
  picks by geometry, because a demonstration is not where the answer is handed over), and its
  ink is **dots**, since a solid line is what a finished channel looks like and a hand must not
  leave one the player has not drawn. `Art.Hand` is generated for invariant 7b's reason and is
  **tilted on purpose** — an upright finger over a closed fist is a gesture that must never
  reach a teaching panel in any market.
- **Celebrate once.** The board already flashes, sounds and (for a glade) throws confetti when
  it solves; the win panel adds no fanfare, no confetti and no haptic. `Handheld.Vibrate` is
  one fixed-length buzz on Android, so a second one cannot be made lighter than the first.
- **Depth is applied to a whole visible window in one pass.** `SetSiblingIndex` *inserts*, so
  assigning depth per tile as tiles are realised leaves a field that looks sorted and is not.
- **An asset scope is bounded by what is on screen**, and an in-flight guard is not
  `IsScopeLoaded` — that goes true the instant a load *starts*. Four grove scopes exist for
  four different bounds (the grove, one shelf, the shop's tab furniture, visiting).
- **A screen may draw a piece from two art sources** — shelf atlases or full-size grove art.
  Ask `HomesteadArt.HasArt` rather than assuming which one is loaded.
- **A `+` beside a resource always opens that resource's panel**, in every state, including
  the ones with no offer behind them. A control that answers a different question depending on
  what happens to be loaded is the mistake that deleted `RouteOverlay` and the toasts.
- **Ask about the blocking condition before the price.** A player who is both too junior and
  too poor is told about the wall money cannot climb (`HintPrompt`, `CompanionPurchaseState`).
  Equally, a short balance opens the shop rather than greying the button.
- **Panels that explain the game read their numbers from the rules**, never from the copy —
  `StreakInfoOverlay`, `AdOfferOverlay`, `EventInfoOverlay`. That copy is the first thing to
  rot when the game is retuned.
- **A `switch` inside a `MonoBehaviour` is the one place here nothing can be proved.** The
  branching decisions live in Domain and are pinned offline: `HintPrompt`, `RenameRules`,
  `AccountPromptPolicy`, `GroveUnveil`, `GroveGrowth`, `AccountGate`.

### Two confirmations, and only two

`ForfeitOverlay` (a committed run being abandoned) and `ReportNameOverlay` (an act taken
against another person that cannot be retracted). Everything else either costs nothing to
undo or is confirmed by the store's own payment sheet — a panel of ours in front of that sheet
is a tap for a question about to be asked properly. The one destructive prompt left is a
*guest* whose provider already carries a grove, reachable from linking and nothing else.

### Not done, deliberately

- **Play Games Services** — better Android sign-in and the natural home for leaderboards, but
  Android-only, so it cannot be the identity.
- **A visual level editor** — tooling, and the thing most likely to matter next for cadence.
- **Remote content delivery** is built and switched off. Setting `ContentConfig.RemoteBaseUrl`
  turns `difficulty.clockScale`, the heart gate, the chest odds and the ad payouts into
  minutes-not-days levers; it is the highest-value unshipped setting in the build.
  One known gap first: `Sync Manifest` bumps a chapter's `version` only when its **level
  list** changes, so a content-only rewrite would never reach a client that had already cached
  the body. The fix is a digest of the body in the manifest entry, which
  `ManifestSync.SurvivesRoundTrip` would then police.
- **A "keepers near you" board** — it needs the exact global ordering invariant 19c refuses to
  keep, and the percentile already answers the question it would ask.
- **A duskcap that matters in a hollow** — needs a hollow where not every critter must wake,
  or a critter whose gift depends on the light that woke it. The validator warns rather than
  letting a decorative one ship.

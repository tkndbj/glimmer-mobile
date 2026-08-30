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
   and par multiplies into both star lines *and* the move budget, so a board validates,
   derives plausible numbers and cannot be finished. Everything now asks the one predicate —
   par, the budget, the hint, the near-miss reading and the taproot agreement check —
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
    glades were in that state, which is why brittle stone, taproots and fords all read as
    absent: they were. The arms are rigid — even a filled 7x7 spanning tree usually admits
    one arrangement — so the free decisions have to be **put** there, and a twisted crossing
    is the cheapest one, because it wears all four arms at every angle and only colour can
    settle it. Three rules follow and each was broken everywhere before it was
    written down: brittle stone belongs on a tile the player cannot simply try (so, a
    crossing); a taproot's members must all be tiles the arms cannot settle, or the root is a
    hint rather than a decision; and a ford must sit on a **cycle** of the live network, with
    a **pocket that carries its own heart and its own critter** on the far side — so the
    wrong turn pours one colour into the other while every critter *the grove* has stays lit.
    That last one is 5f's subject and is where the duskcap used to sit. `hazards` is the
    metric this replaces and
    it is worth knowing why it was wrong — it counts rotations that *would* mate two
    networks, but such a rotation leaves an arm dangling elsewhere, so it is not a board
    anybody reaches. A chapter was authored to it and came out reading like a dot-to-dot.

5g. **A board is graded on its solution and met as it is dealt, and only the first of those
   was ever measured.** Reported from play as two glades that "start half done", and it was
   thirty-four of the forty. `fit` picks a board by par and nothing else — but hundreds of
   (seed, bias) pairs hit any given par, and it walked `bias` from -90 upward and took the
   first, so it systematically returned the board dealt *most* nearly finished; a negative
   bias is `Board.spin`'s instruction to **prefer** leaving a tile sitting on its solution.
   The worst opened with 23 of its 40 turnable conduits already right and four glades opened
   with critters already awake. Nothing could see it, and that is the part worth keeping: a
   part-solved board is solvable, correctly par'd, has one winning arrangement, mates every
   arm and passes every gate in this repository — because "how much of this is already done"
   was a question nothing asked. Invariant 5d's fault (a rule that rejects nothing is
   decoration) in the one place nobody thought to look, which is the board's *opening
   position* rather than its solution.
   <br>`Board.astray` is the reading — critters already lit, conduits already right, over the
   same set `par` charges on — and `fit` now ranks it behind the par distance, which is
   deterministic, costs nothing and cleaned twenty-one of the forty **without moving their par
   at all**. `difficulty.py` prints it as `dealt`, beside the readings about the solution.
   <br>**The other nineteen needed a longer board, and that is arithmetic rather than a
   choice.** Par *is* the count of owed turns, so a glade dealt with three quarters of its
   tiles wrong has a par near three quarters of its tile count and no seed can give it a
   shorter one: the shipped par ceiling on those boards was what forbade a scrambled deal.
   Their targets were raised to the shortest one that carries a clean deal — 19 rungs, most by
   one to five turns, the worst (`c02_braided_water`) by fourteen — and **nothing but the `/n`
   rotations moved in any of the forty**: every arm, colour, crossing, briar, brittle marker
   and taproot rune is byte-identical, `wins` is still 1 on every board and every mechanic
   still bites. Nothing anybody earned moved either, because `LevelRecord.Stars` is stored and
   only promoted (invariant 22) and credits derive from the star ledger (invariant 9).
   <br>The one thing this cost is that **par ramps flatten**, and the reason is worth stating:
   a chapter's par dips were, in several cases, not a taproot charging once but a board being
   dealt partly solved. Mill Vale's dip at `c02_braided_water` was the second kind and is gone.
   Before authoring a par ramp, note that a properly dealt board's par is roughly 1.2–1.35× its
   turnable tile count, so the ramp is mostly a fact about board size and only the taproots
   genuinely buy a dip.

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
   `BriarTests.AMisturnedBriarThatLightsWhatWasDarkIsCountedAsADistance` reads 0 on the old
   rule and 1 on this one. Before adding a tile whose drawn arms and conducting arms differ, ask
   what it can now join that no arrangement could join before.

5f. **A wrong turn must be visible somewhere, and the duskcap was the one that never was.**
   `x` was a creature the light had to never reach: every critter awake and one duskcap lit
   was an unfinished glade. It shipped in twenty-nine places across four chapters and it is
   **removed**, because the state it produced is indistinguishable from a bug. Everything
   else a player can get wrong here announces itself by a critter going out; a woken duskcap
   left every critter lit and the glade simply refusing to settle, which is exactly what a
   broken game looks like — invariant 20g's lesson, found first in Lightweave and true here
   for longer. The panel that explained it was not the fix, for 20g's reason: a rule the
   board cannot show is a rule the player is always being surprised by.
   <br>What replaced it costs nothing and is stronger. Every pool of dark is now a **pocket
   with a heart and a critter of its own**, so the ford still stands on a cycle, the wrong
   turn still joins two things that must stay apart, and the warning is one critter going
   out somewhere the player is not looking. `difficulty.py` measures the swap exactly: the
   `dark` column is gone and `colour` picks up every arrangement it used to reject, `wins`
   is still 1 on all forty glades and every `par` is unmoved, so **no star, record or wallet
   moved** — stars are stored and only promoted (invariant 22), and credits derive from the
   star ledger. Three ids are retired and must never be reused: the token head **`x`**, the
   lesson id **`duskcap`**, and the level id `c01_duskcap_hollow` is *kept* and its name
   changed, because an id is permanent (invariant 1) and only the string above it can move.
   The head is **refused** rather than ignored — a chapter file carrying one is content
   written for a build that no longer exists, and reading it as anything would put a tile on
   the board no rule knows what to do with.
   <br>**The rule has a gate, and writing it is what found the bug in the old one.**
   `LevelValidator.CheckDecidableTiles` turns every briar and every twisted crossing one step
   off its solution and refuses to be satisfied unless the glade stops finishing — the
   consequence, not a proxy for it. It replaces a check that asked only whether *lifting* a
   briar's thorns would join two networks, which was wrong in both directions: it missed a
   briar holding apart two networks of the same colour, and it fired on this very pocket
   shape, where the open pair is the only way into the pocket and both thorned ways lead back
   into the grove. That false positive is how the fault was found — three Nightbriar boards
   were quietly redesigned to satisfy a check that was itself wrong, which is the failure
   mode this file exists to prevent. A **warning**, not an error, because the first board of
   a mode may legitimately carry a briar as scenery.
   <br>**The rule exists three times, so it is pinned by a vector file** —
   `Tools/verify/board-vectors.json`, invariant 9a's shape for a board rule rather than for
   money. `LevelValidator`, `content.py`'s `decidable` and `author.py`'s `Board.decides` each
   need it and none can call the others; `BoardVectorTests` proves the C# copy matches the
   file and `content.py` proves both Python copies do on every offline run, so drift fails a
   gate instead of printing beside the word "ok". Writing the file immediately earned itself:
   three of its first crossing cases used `=EW+NS`, which is a *straight* crossing and
   correctly skipped, so they exercised nothing. The set now carries a twisted crossing that
   is settled and one on a ring that is not, and `BriarTests` keeps the same shapes inline
   because those run on every offline compile without anybody opening Unity.

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
    from the board, stars from par, the move budget from par and the server's earnings from all
    three, so a grove that granted anything would make every glade a different difficulty per
    player and no validator could prove one fair again.

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
    <br>**There is exactly one exception, and what makes it one is that it can never stop being
    true.** Every refusal above is temporary — a re-seed fixes the missing product, a retry fixes
    the tunnel — which is why resubmitting for ever is the right answer to all of them. A receipt
    already granted to a **different account** is not: the document is never deleted (account
    deletion keeps it deliberately, invariant 27) and its `uid` is never rewritten, so no future
    state makes this caller the owner. `redeemPurchase` says so with `already-exists` rather than
    `permission-denied`, the client reads it as `CloudFailure.AlreadyRedeemed`, and the queue
    finishes the transaction and grants nothing. That is invariant 13a's rule applied to the store
    instead of to a claim. **The account it protects is not the one holding the phone**, which is
    the part that decides it: left unfinished, Google auto-refunds after three days and
    `sweepVoidedPurchases` reverses the grant against `receipt.uid` — so a device that can never
    finish somebody else's transaction eventually costs *them* the currency they paid for.
    Reachable by switching accounts, which has always been true, and now by deleting one.
    `StoreReceiptTests` drives both directions, because a fixture proving only the new branch
    would pass just as happily if every refusal started confirming.
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
18d. **A real-money product grants currency, or an idempotent permanent entitlement — never
    a stored amount, and never both.** This is invariant 18 widened rather than broken, and
    the widening is what the heart containers cost. Read 18's argument again and notice what
    it is actually about: hearts and boosts are **amounts**, so a product granting one would
    need the client to apply half a purchase after the server applied the other half — which
    means a record of *"did I already apply this transaction's hearts"*, in the save, merged
    across devices, whose failure mode is somebody paying and receiving nothing. A **capacity**
    is not an amount. It arrives as the union of one permanent product id, so applying it twice
    is applying it once and the record has nothing to answer. That is the same property that
    makes a companion purchase safe to store (invariant 15), and it has three consequences
    worth stating because each is load-bearing. The entitlement can live **entirely on the
    client** (`heartContainersOwned`, save v21) and still survive a reinstall, because both
    stores re-deliver a non-consumable for ever and `HeartContainerLedger.Grant` runs on
    *every* successful redemption rather than only the first — a Restore is the recovery path
    and it needs no state of ours at all. The **cap is derived** from the ids against the
    catalog and is the largest container held rather than the sum, so buying the rungs out of
    order, buying one twice, or restoring onto a device that already holds a better one all
    resolve to the same number with no special case. And **never both** is enforced by
    `StoreCatalog`, `products.ts` and the seeder rather than by convention: a container that
    also paid gems would put an amount straight back onto the path this reasoning removed it
    from.
    <br>**The refund is the half a client-held entitlement cannot see, and it is why there is a
    second set.** Buy, refund, keep the upgrade is invariant 18c's leak with a $39.99 price on
    it. So `revokeReceipt` writes the container id onto `players/{uid}/private/wallet` — the
    document no client may write — and every wallet reply carries it back as
    `containersRevoked`. Note carefully what that field is **not**: it is not the list of ids
    the server thinks the account owns. An answer read as a whitelist would confiscate a
    purchase on any reply that was short, from a cold account, or from a deployment predating
    the field; an explicit revocation can only be produced by a refund that really happened.
    Both sets only ever grow, so both are joined by union and two devices converge whatever
    order they sync in (invariant 11b) — "the newer device is right" would have two phones
    handing a refunded container back and forth for ever. Buying it again lifts the revocation,
    in `redeemPurchase`'s own granting transaction.
    <br>Two smaller things follow. The **supplies shelf now carries real money**, so the guest
    notice is drawn there too and `ContentValidation` proves the container ladder separately —
    a container grants no currency, so its value per unit of money is zero and the money
    ladder's check would fail every shelf it was ranked on. And a container **at or below the
    free refill cap is an error, not a warning**: both numbers are content, so raising
    `hearts.refillCap` past a shipped vessel would take real money and change nothing the
    player can see, from a config push, with no code change to notice it.

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
    share is everything about being a **run**: the heart, the stake (`RunGuard`), the daily
    chest, the streak and the star ledger. Those are reached through
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
    <br>**Lightweave is retired and this stays**, for the reason every retired thing
    here stays: the rule is about how a mode is judged rather than about that mode, it
    was learned by shipping the wrong answer twice, and the file that stops it being
    learned a third time is this one. `weave` is a spent mode id.

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

20h. **A chapter's mode is derived from its levels, never typed — and the build gate proves it.**
    `mode` was the last field of a manifest entry still written by hand, and it is the one field
    whose absence nothing notices. It decides which screen opens a chapter's levels, which lane of
    the switcher it sits in and — through `LevelUnlock.GateFor`, which looks for the chapter before
    this one *in the same mode* — whose stars unlock it. Leave it out and the chapter is indexed as
    a glade chapter: every level still parses, every board is still proved solvable, every string
    resolves, every address loads and the build goes green. What ships is a chapter gated on a
    stranger's stars, filed under the wrong tab and routed to a screen that cannot play it. That is
    not hypothetical — it happened on the very first sync of the mode's second chapter, and the only
    symptom was one line in a log. `Sync Manifest` now derives the field, which is invariant 4a's
    rule for the level list applied to the one field that had escaped it, and
    `ContentValidation` errors on any disagreement, because deriving makes a mistake unlikely and
    only a check proves it did not happen anyway — a manifest is a text file, and a step somebody
    has to remember is a step that gets skipped. The rule lives once, in `ChapterModeValidator`
    (`GlimmerGrove.Authoring` — reachable by the build gate and by the suite, in no player build),
    so both callers ask the same copy and `ChapterModeTests` drives the exact configuration that
    shipped.

20i. **A mechanic that moves the *floor* moves par, the ladder's yardstick and the top of the
    star ladder with it — and only one of those three had a check.** Lightweave's third chapter
    brings the **hedge**: a barrier grown along the edge *between* two cells that no channel may
    cross. It takes no ground, so unlike every other object in the mode it cannot be walked round
    by one step; it removes a *way*, and a run of them anchored at a side of the grove turns a
    field into rooms with a doorway between them. That is the sharpest form of the only question
    this mode asks — who yields — and it is the one form of it the player can see before
    committing to anything (`MinReach`'s argument, for the ground rather than the endpoints).
    <br>**Par had to follow it, and nothing would have said so.** Everything a weave is graded on
    derives from `WeaveLayout.Straight`, the fewest cells any route of a pair's could use, and that
    was a *Manhattan* distance — which walks straight through a hedge. Left alone, a hedged grove
    would have been graded against a floor no arrangement of it could reach: the three-star line
    below the best possible play, and a whole band of the ladder silently gone. That is invariant
    22's stranded band arrived at from the other direction, and it is invisible in every check
    there was — the board is still solvable, still full, still measured. So a distance is now a
    breadth-first walk over the ways that are actually open (`WeaveHedges.Span`), and par, both
    star lines and the ink all rise with the barriers by themselves. On a grove with nothing grown
    it is the same integer, which is why the two chapters already shipped are unmoved board for
    board; `Tools/verify/weave.py` proves that on both runtimes.
    <br>**The difficulty *reading* had to follow it too, and that is the half that is easy to
    miss.** `slack` is measured against each pair's own floor, so a hedge does not raise it — it
    moves forced detour out of the number and into the thing the number is measured against. A
    hedged grove forcing ten cells over floors already six cells longer asks exactly as much as an
    open one forcing sixteen, and the cross-chapter rule as written (every rung at least the
    previous chapter's finale slack) would have called it a step down and been literally
    unsatisfiable — measured, thirty thousand seeds of the shipped shape never produced slack 16
    with hedges on it. `Rung.Toll` is slack plus what the hedges cost, it is identical to slack
    wherever nothing is grown, and it is what a chapter now climbs.
    <br>**And the gate that was missing is now there.** Nothing proved three stars was reachable on
    a weave at all: `CheckStarBands` proves the three lines are ordered and `WeaveValidator` proves
    the ink covers the floor, but "can the best possible arrangement score three stars" is
    `WeaveSolver`'s exponential search, which may never be a build gate.
    `WeaveLadderTests.TheBestArrangementOfEveryGroveStillScoresThreeStars` asks it of every shipped
    grove, and it passed on all twenty of the old ones the day it was written — which is the only
    reason a mechanic that moves the floor could be added at all.
    <br>Two smaller rules the hedge is held to, both countable. It is grown **before** the carve,
    so the arrangement the generator draws respects it and a hedged board is solvable by
    construction rather than by check — placing barriers over a finished solution would mean
    re-proving it and sometimes failing. And a fence that changes **no** pair's shortest route is
    refused (`WeaveLayout.HedgesBite`), by the generator, by the validator and by the suite: a
    barrier the player routes around without noticing is invariant 5d's decoration, and here it is
    one comparison of two integers.
    <br>**Lightweave is retired and this stays**, for the reason every retired thing
    here stays: the rule is about how a mode is judged rather than about that mode, it
    was learned by shipping the wrong answer twice, and the file that stops it being
    learned a third time is this one. `weave` is a spent mode id.

20j. **Three modes were designed for this slot and two were thrown away, and what separates them
   is not cleverness — it is whether a board can be read, and whether it can stall.** This is the
   most expensive lesson in the file and it was paid for twice in one day, so it is written down
   as three tests any new mode has to pass *before* a level is authored, let alone a chapter.
   <br>**One: the answer has to be visible on the board, now.** *Ripplewake* was a mode of
   expanding rings: drop a stone, its ring steps out a cell a beat, and where two rings arrive on
   the same water at the same beat the sleeper under them wakes. Every number about it was good —
   par searched cheaply, the ladder climbed, `ways` was low, a careless player finished the first
   two rungs. It was played and the report was three words: *"I understood nothing."* The fault is
   structural rather than presentational: the thing the player has to predict is a **coincidence
   several beats in the future**, so the puzzle lives in their head instead of on the board.
   Adding readouts for it was proposed and is the wrong fix — it patches the symptom. Every other
   mode here is immediate and spatial (turn a conduit, drop a mote, lay a tile) and this one
   deferred its answer by four beats and then asked to have been planned. **If a mode's payoff
   arrives later than the input, it is a thinking puzzle, whatever the numbers say.**
   <br>**Two: a finite board with no refill must not be able to freeze.** *Windfall* was the next
   attempt and it never shipped a level: swipe a direction, everything slides, three alike
   touching burst, and the wind keeps blowing so it cascades for free. One gesture moving the
   whole board is the best spectacle-per-input there is. It **stalls**. A tilt barely changes
   relative positions — after one compaction per axis the board is frozen, and the player flips
   left-right for ever with nothing happening. Measured on the first hand-built board: stuck at
   beat 3 of 12 with fifteen leaves and two critters left. 2048 survives this only by spawning a
   random tile every move, and randomness is exactly what makes par unsearchable (invariant 26).
   **Ask of any new mode: does every legal input strictly move something that only goes one
   way?** Budburst's does — a tap *adds a channel* to a flower and channels never come off, so the
   grove always moves toward white and toward a burst, and a wave always removes at least three
   flowers while nothing is ever added. That single property gives it *cannot stall*, *always ends*
   and *the search terminates* at once. Note the shape of it: the monotone quantity is not the
   thing being counted (flowers) but the thing being *added* (channels), and a tap that would add
   none is refused outright rather than swallowed — which is what makes "every allowed tap moves
   the grove" true rather than nearly true.
   <br>**Three: a cascade that spreads on its own is not a mechanic, it is a solvent.** An earlier
   cut of Budburst's chain rule looked finished after an afternoon and every board measured par two
   lower than it was designed for. A cell lending to its neighbours unconditionally walks outward
   across open ground for ever: measured at **thirty cells in eleven waves from one tap**,
   finishing a board built to take four. What settles it is a **threshold the spread has to clear
   again** — the shipped rule washes a colour outward and nothing goes off unless *three alike*
   end up touching, so a chain dies wherever the grove is not already nearly right, and the boards
   that run are the ones somebody built to run. **A rule that makes boards more solvable is as
   dangerous as one that makes them unsolvable, and only counting finds it**: every one of these
   shipped a board that was solvable, correctly par'd, fully validated and wrong.
   <br>`ripple` and `weave` are **spent mode ids** and must never be reused, along with the nine
   retired lesson ids the two modes between them spent (`weave_join`, `weave_bead`, `weave_ink`,
   `weave_hedge`, `weave_fill`, `ripple_meet`, `ripple_satchel`, `ripple_reed`, `ripple_deep`,
   `ripple_lily`). An id travels into the manifest, analytics and the save file exactly as a level
   id does.

20l. **"Brain-dead" is a property of what the player has to *work out*, not of how hard the
   board is — and every cheap way to make a mode easier misses that.** Budburst was commissioned
   as chill, was tuned three times toward chill, and came back each time as *"you still have to
   think quite hard"*. Nothing about the boards was wrong: par was 3, the satchel was eight taps
   for a three-tap answer, a thoughtless player cleared every grove. What was wrong is that
   **the match was invisible until you made it**. Every game of this shape — Royal Match, Candy
   Crush — *shows* the player the matches and asks them to pick one; this one made them work out,
   in their head, which cell the colour in hand would turn into a third of something, which is a
   simulation task and no amount of generosity in the numbers touches it.
   <br>Four rules answered it and they are one idea, gated on one field: a grove with a
   <b>strip</b> (`regrow`) is **living**, and one without is **still** — the shape this mode
   shipped with, kept because eight vector cases pin the base rule in isolation from everything
   built on top of it.
   <br>**The board says which taps pop.** `BudRun.Pops` is one preview per flower and
   `BudView.PaintPops` breathes them. The choice is untouched — most groves offer several and
   they differ enormously in size — and what is gone is the arithmetic in front of all of them.
   This is the change; the other three are what make the answer worth looking at.
   <br>**It falls, and it grows.** What bursts leaves a hole, everything above slides into it,
   and once the chain has stopped the holes fill from the strip. The board never thins, so the
   fortieth tap is dealt as good a grove as the first — which is the half of regrowth that
   actually mattered, and the reason chains stopped getting rarer as a level went on.
   <br>**White is the bomb.** It holds every channel, so it could never be mixed into: it was a
   dead cell and a mistake the player had made, the one state in the mode that punished them for
   playing it well. Tapping it now clears the square around it. The trap became the reward and
   the board gained an obvious, spectacular button, at the cost of no new object at all.
   <br>**And one flower ripens between taps**, always beside somebody still shut in, so the grove
   leans toward the player rather than drifting away from them.
   <br>**Two of those nearly cost properties this file exists to protect, and both failures are
   the same failure.** Growing *inside* the chain destroys the termination proof — a wave used to
   remove at least three flowers from a board that never gained any, and a repeating strip can
   resonate with a grove for ever. Measured on the first cut: **two thirds of opening taps ran
   straight into the wave ceiling and par collapsed to one**. So the chain falls and the grove
   grows afterwards, which restores the proof exactly. And what grows may never *make* a bunch —
   a hole takes the first colour off the strip that leaves the grove settled — or the player is
   handed a cascade they did not cause, which is what `BudValidator.Settled` refuses of an
   authored board and had no reason to be less true at rest. **Before adding a rule that puts
   something on the board, ask what used to bound the loop.**

20k. **A mode may be built to be *easy*, and then two of this file's own rules invert.** Budburst
   is the first mode here commissioned against a feeling rather than a difficulty: *chill,
   hypnotic, one tap and something enormous happens* — the register Royal Match and Toy Blast play
   in, where everybody finishes and the stars are where the skill lives. That is a legitimate
   design decision, and honouring it means reading two gates backwards rather than quietly
   ignoring them.
   <br>**`ways` flips.** Invariant 5d warns above a threshold: a board almost anything finishes is
   deciding nothing. Here the brief *is* a board almost anything finishes, so `BudValidator` warns
   **below** two — one single shortest play means the grove has to be solved rather than played.
   <br>**`greedy` flips, and becomes the bar rather than a reading.** Everywhere else a player who
   never looks ahead finishing is a complaint. Here a grove a careless player *cannot* finish
   inside its satchel is the thing that gets warned about, because it is asking for more than the
   mode promises. The shipped board is the honest version of that: careless finishes in 4 against
   a three-star line of 4, so a player who never plans still gets full marks — and it was chosen
   over three shorter baskets whose careless play was *optimal*, because a greedy player playing
   perfectly means the grove decided nothing at all, which is the one way a chill board can still
   be a bad one.
   <br>What does **not** flip is anything about money or grading: par is still searched, the star
   lines are still the same 1.20/1.40 multiples every mode uses, room above par is still counted
   in the unit the mode is graded in (invariant 26e), and the fail state is still real. An easy
   mode is one whose *boards* are generous, never one whose arithmetic is.

21. **A chapter is opened by stars, and only its first level asks.** Inside a chapter the chain
    is unchanged — clear the level before this one. At a **boundary** the chain gives way to
    `LevelUnlock.GateFor`: the next chapter opens once the player holds `starsPerLevel` stars per
    level of the one behind it, which ships as 2, so 20 of a ten-level chapter's 30. The two rules
    answer different questions and only one is about mastery — ten levels cleared on one star each
    is a player who never met what the chapter taught, and a player beaten by the ninth of ten had
    no route forward at all except the board that beat them. A star gate can be met from anywhere
    in the chapter, so being stuck on one level is never being stuck on the game, and it cannot be
    met without having played most of it well. It is authored **per level** rather than as a total
    because a chapter is not a fixed size: 20 is two thirds of ten levels and a fifth of fifty, so
    a total would be the same number meaning a different rule every drop. Four things follow and
    three of them were bugs the change laid. `NextToPlay` must return the **furthest unlocked**
    level rather than the last of the mode — "nothing uncleared is open" no longer implies "the
    mode is finished", and the old fall-through dropped the hub's continue button onto a padlocked
    board. The victory panel's Next button asks `IsUnlocked` rather than `index.Next`, or it walks
    the player straight through the gate the map exists to hold. A chapter opening is a
    **transition**, so it is measured either side of the record fold in `RunLedger`
    (`WinRecord.ChapterOpened`) — by the time a panel is built the gate simply reads open, which is
    indistinguishable from one that opened an hour ago, exactly the trap the streak's `Advanced`
    exists to avoid. And every screen that said "clear this chapter to go on" now prints the
    **count**, because the old sentence is both wrong and unactionable to somebody holding nine
    cleared levels. `ChapterGateTable` is content (`chapterGate` in `progression.json`) for the
    heart gate's reason — a gate that turns out to be a wall costs installs, and the fix must not
    cost a store review; 0 is legal and opens everything. **A level already cleared is always
    open**, and that clause is load-bearing rather than kind: the rule in front of it is content,
    so it can be raised, and it *did* change under everybody already playing. Without it an
    account that cleared three chapters at one star each opens a chapter it finished and finds
    the first level padlocked with the nine behind it open — the chain and the gate answering
    different questions about one save. It cannot weaken the gate, because a level nobody has
    cleared is a level nobody has opened. The server has no opinion: unlocking pays
    nothing, mints nothing and is stored nowhere, so it stays a pure function of the star ledger.


27. **Deleting an account removes data first and the account itself last, and that ordering
    is the only thing making it safe to retry.** Both stores require an app that supports account
    creation to offer deletion *inside* the app — Apple's 5.1.1(v) does not accept a web page —
    and this deployment holds a save ledger, a wallet, a published card and a reserved name for
    every player. `deleteAccount` walks them in one order and the order is the whole design.
    **Visibility first** (`groves/{uid}` and the account's row scrubbed out of all ten
    `leaderboards/*` in a transaction apiece), because a run that dies halfway must never leave a
    deleted keeper's name standing where a stranger can read it, and unlike `withdrawGrove` this
    cannot wait for the nightly rebuild — the account will not exist to correct it. **The name
    next**, while the wallet that holds the key to it is still readable: `names` is not queryable
    by uid, so releasing it after the save is deleted would strand a reservation nothing could
    ever find. **Then the save**, recursively, so a subcollection added next year is not a list
    somebody forgot to extend. **Then Apple.** **The auth user last**, and that is the clause with
    teeth: every failure before it is still authenticated, so the client simply calls again and
    every step is delete-if-exists or a transaction that re-reads its own precondition. Delete the
    user first and a crash halfway leaves documents under a uid nobody can ever authenticate as
    again — unreachable by the player, by this function and by support, which is the one failure
    here with no repair.
    <br>**Three things are deliberately kept and each would be worse to delete.** `receipts/*`,
    because it is the record that one store transaction has been granted, keyed globally against
    the replay attack (18a) — dropping it makes "buy, redeem, delete, sign up, redeem again" an
    unbounded faucet costing an attacker one purchase. Reports this account filed about *other*
    people, because the count on the parent is denormalised from them and removing one would
    either drift that count or silently un-hide a name three real players reported. And a
    **denied** name's reservation, which is retargeted to a tombstone uid rather than released:
    deleting the account is not a reason to hand an offensive name to the next person who asks
    for it, which is exactly what `reports.ts` keeps the reservation to prevent.
    <br>**The client's half is server-first, and it is the half that can orphan something.** The
    local grove is the only evidence the deletion was asked for, so nothing local is touched until
    the server confirms — which is what lets every failure sentence on the panel say "nothing has
    been deleted" and be telling the truth (`AccountDeletion.Untouched`). It runs under the sync
    latch for the whole of it, because a sync is pull → join → push and one in flight would put
    the grove straight back into the document being deleted: the one orphan the server's own
    ordering cannot prevent, because the client causes it. `SaveService.EraseAccount` is the only
    route in that file that destroys a grove without filing it anywhere — a switch archives what
    it leaves and this must not, or `SwitchTo` would cheerfully restore it if that uid came round
    again — and it drops **only** this account's slot, because the other five belong to accounts
    still playing on a shared phone. The device is left on a fresh anonymous account rather than
    signed out: there is no sign-in screen in this game, so holding no account at all is a state
    nothing knows how to draw.
    <br>**A linked account re-authenticates first, and that is one step doing two jobs.** The
    reason is proof — an account left signed in on somebody else's phone should not be erasable by
    whoever is holding it — and it is `ReauthenticateAsync` rather than a sign-in precisely because
    a sign-in *replaces* the session, so picking the wrong entry out of an account chooser would
    quietly make the device somebody else. Firebase's own `UserMismatch` refuses it with the
    session untouched. What the same step also yields is Apple's `authorizationCode`, which is
    single-use and expires in minutes and is therefore only obtainable at the moment it is needed;
    capturing it at *link* time instead would store a live third-party credential for every Apple
    player for the life of their account and exercise the path months later, which is a break
    nobody sees until the one time it matters. A guest has no provider and is asked to confirm and
    nothing more — the only players who could not delete their account must not be the ones with
    least invested in it. **A revocation failure never blocks a deletion**: Apple being down is not
    a reason to refuse somebody their own account back, and the data is gone by then anyway.

22. **A puzzle is graded on the puzzle, so there is no clock anywhere in this game.** Stars
    were the *worse* of what the turns allowed and what a countdown allowed, and that one word
    is the whole fault: the turn thresholds are the only half that measures whether a board was
    solved well, and for anyone who stopped to think the clock's reading was always the lower
    one — so the good measure decided nothing for exactly the players the boards are designed
    for. Everything invariant 5d asks of a glade (brittle stone on a tile you cannot simply
    try, taproots the arms cannot settle, a ford on a cycle) exists to force a
    *decision*, and a countdown prices deliberation. It also scaled with the wrong thing — the
    limit came off par, and par is **length**, so a long dot-to-dot got a generous clock and a
    short board full of twisted crossings got a tight one — and the third star asked 1.35
    sustained taps a second, which is a motor threshold, the same on a bus as at a desk. Gone
    with it: `timeFactor`, `difficulty.clockScale` and the whole `DifficultyRuleTable`,
    `RunClock`, `LevelValidator`'s tap-rate warnings, the `run_continue` rewarded ad, and the
    timer on the board.
    <br>**A glade is now measured against three lines and nothing else, and they are even
    thirds of one slack.** A run can only score inside `[par, par × 1.60]` — 0.60 par of room —
    cut into three bands 0.20 wide: three stars at **1.20**, two at **1.40**, the run ends at
    **1.60**. `par` is derived from the board, so a glade authors no difficulty number at all
    unless it wants a different budget; the only one that does is the first glade in the game,
    which turns the budget off.
    <br>**They are one decision in three numbers and must move together — that is the rule this
    cost us.** The budget was cut from 2.60 to 1.60 while three stars was still `par × 1.35` and
    two was `par × 2.00`, which put the two-star line *outside* the survivable range: a run
    still alive had always spent fewer turns than the budget, so **one star became unscorable by
    anybody**. Every number stayed individually plausible, every board validated green, and a
    third of the ladder quietly stopped existing. That is invariant 5d's fault moved into the
    grading — a band nothing can land in is decoration. `LevelValidator.CheckStarBands` and
    `Tools/verify/content.py` now prove `gold < silver < budget`, and
    `PressureTests.TheStarBandCheckCatchesAStrandedBand` drives the exact broken configuration
    that shipped, because a check with no failing case is not a check. Both read the **factors**
    rather than the thresholds they derive: at par 1 or 2 all three round onto the same number
    however they are set, and reporting that would be a complaint about board size.
    <br>**`MoveBudget` has no floor, and that is deliberate.** It clamped to
    `SilverThreshold + 1` so a run still earning stars could never be the run that ended —
    sound while the clock was the fail state and the budget was a backstop under somebody
    drumming, and wrong the moment the budget became the only way to lose, because a fail line
    past the point where the player has already stopped earning anything is a formality. An
    authored factor now means exactly what it says. What keeps a budget this tight fair is that
    the meter counts **committed** wrong turns only: `BoardView.Undo` refunds a turn and is
    unlimited, and a hint charges none, so trying a crossing and taking it back is free — which
    it has to be, because a straight conduit and a straight crossing read the same half a turn
    round (invariant 5c), so exploring is correct play rather than flailing.
    <br>Three consequences that are easy to get backwards. **Nothing already earned moved**:
    `LevelRecord.Stars` is stored and only ever promoted, so this re-grades no record — had
    stars been derived from stored turns and time, every wallet in the world would have
    inflated on update, because credits derive from the star ledger. The **ceiling** is
    likewise unmoved by any of the retunes: three stars a level, over however many levels the
    catalog holds, so what changed is the standard of play needed to reach it and never what it
    pays. (A content drop does raise it, and is meant to — that is a level being added, not a
    number being retuned.) And **`run_continue`,
    `ChestDropKind.RunTime` and `DefeatReason.OutOfTime` are retired ids that must never be
    reused**, because each travels into analytics, a mediation dashboard or the chest vectors,
    where re-pointing one silently re-labels history.
    <br>Two things this changed that were **not** measured, and should be before they are
    trusted. Every number above was chosen by reasoning about where the bands sit, not from
    play: `Tools/verify/difficulty.py` was never run against them. And whether the chapter gate
    (invariant 21, two stars a level) got easier or harder is genuinely unknown — removing the
    clock can only ever *raise* a star count, while every turn threshold tightened in absolute
    terms (on a par-36 board three stars went from 49 turns to 44), so it depends which
    constraint used to bind for a given player. Do not restate either as settled.
    <br>**`bestMillis` is retired in place, not deleted**, and it is owed a removal. It is on
    the wire in both directions and `hasOnly` is an allow-list, so dropping a field a
    rolled-back client still writes is how a save loses *every* write (invariant 12a). It is
    still merged so times already earned survive, and nothing reads it — but it is now
    permanently zero for all new data, and the honest end state is to drop it from
    `FirestoreSaveMapper` and `firestore.rules` in a later schema version once no shipped
    client writes one.

22a. **A mode with no turns is graded on the count it does keep, never on how fast it was.**
    <b>Lightweave is retired and this stays</b>, because the rule is what a mode does when it has
    no move to count, and the next one to arrive without one will need it. The classes named below
    are gone with the mode.
    Lightweave had no move budget and no move count — it reported a constant — so the clock
    decided every star, and removing it would have handed three stars to every clear. What
    replaced it was already there: `WeaveRun.Occupied`, the cells its channels took, against
    `WeaveLayout.Par`, the sum of every pair's own shortest route. A taut arrangement lands
    under the three-star line and sprawl does not, which is the mode's own difficulty reading
    (invariant 20f) seen from the player's side. It fixed a second thing quietly: the record
    and the published deciles had been fed `par` as the move count, which is a **constant**, so
    every player who ever finished a grove held an identical "best" and the population ranking
    for that mode meant nothing.
22b. **A mode's fail state is a budget in the unit it is graded in — Lightweave's was ink,
    Budburst's is taps.** <b>Retired in place</b>, and every mode since has struck the same
    bargain in its own unit: a well in motes, a groove in tiles, a thicket in taps. The classes
    named below belong to Lightweave and are gone with it; the rule is not.
    22a left a weave unable to be *lost* at all, only forfeited, and named the fix rather than
    making it. This is it: a grove is dealt `par × budgetFactor` **cells of light**
    (`WeaveInk`), a channel costs one per cell it covers, and the run ends when the grove
    provably cannot be finished with what is left. Nothing about the mode's grading moved — the
    same three factors over the same par, which is what keeps a second mode from quietly
    retuning the economy (invariant 9) — so the ink is simply the third line of a ladder that
    already had two, and `LevelValidator.CheckStarBands` is now asked by both modes rather than
    copied into one.
    <br>**Spending is permanent and that is the whole mechanic.** Erasing a channel frees the
    *ground* so a route can be redrawn; it does not give the light back. Without that the meter
    rejects no arrangement and is decoration, which is invariant 5d for a resource. What keeps
    it fair is that a wrong move must be cheap to *discover* and only expensive to *keep*: a
    drag costs nothing until it lands, the drag itself is walled at the ink in hand so a channel
    nobody can afford can never be drawn, and `WeaveStrokes.Allowance` hands two landed channels
    back in full — a true undo, restoring the route a redraw replaced rather than leaving the
    pair bare. Everything past that is paid for.
    <br>**The mode is five small classes and was very nearly one big one.** `WeaveBoard` is the
    puzzle — what is drawn, what may be, and facts about the grove (`Floor`, `Reach`, `Settled`).
    `WeaveInk` is the meter, `WeaveStrokes` the undo stack, `WeaveVerdict` the reading of a board
    against a meter, and `WeaveRun` the ten lines that let the three move together. All of it
    shipped inside `WeaveBoard` first, which took one change to become a puzzle model with an
    economy, an undo stack and a fail state in it — and none of those three could then be tested
    without building a grove. The split is what makes "two undos and they do not come back" and
    "a lost run is only ever ended once" arithmetic over integers. `WeaveMode.Validate` takes a
    **board**, not a run: proving a grove can be finished is not playing one, so it spends no
    light and writes down no stroke.
    <br>**Ink spent is the grade**, not `WeaveRun.Occupied`. For a run with no redraw in it they
    are the same integer (a cell per cell covered), and where they part — the channel drawn,
    thought better of, and drawn again — the ink is the honest reading, because the light really
    was spent. One number means the meter on screen and the stars at the end cannot disagree.
    <br>**The two loss conditions are lower bounds, and they have to be.** Ending a run the
    player could still have won is the worst thing this mode could do, so `WeaveVerdict` counts
    each unfinished pair's floor on an *empty* board — no arrangement finishes for less, and none
    is ruled out, since taking somebody else's channel up is always allowed. The second half is
    what a floor cannot see: a critter walled in where freeing it costs more than is left, which
    would otherwise be a board that cannot be finished and will not end (invariant 20g's state,
    exactly). A pair that is joined but still owes a bead counts in full, because it has to be
    drawn again. `EndsTheRun` is the third clause and belongs with them rather than in an `if` on
    the screen: a run decided twice charges two hearts for one loss, and one decided before the
    first channel lands charges a heart for a board nobody touched.
    <br>**A tap on a channel no longer erases it**, and that follows from the ink rather than
    being a separate opinion: the same stray thumb that used to cost a free redraw now destroys
    something bought, on a screen the player is dragging their hand across. Taking a channel back
    is asked for by name — the undo key, or drawing the pair again from its crystal. For the same
    reason the header's one-tap restart became a pause key: a restart deals a fresh pot, so it is
    the cheapest way out of a grove going wrong and belongs one deliberate tap away, which is
    where a glade's has always been.

23. **A lost run may be bought back, and the offer comes before the accounting rather than on
    top of it.** A run that reaches its fail state is offered one more go for gems
    (`ContinueOverlay`), and only when that is declined does anything about a defeat happen —
    the heart, the record, the chest count, the streak, the analytics. That ordering is the
    whole feature: a continue offered *after* `RunLedger.Loss` would be an offer to undo an
    accounting entry, and undoing one is how a ledger stops meaning anything. `RunContinueFlow`
    owns it for the reason `RunScreen` owns the stake — this is the one way out of a run that
    takes money instead of a heart, and two copies would be two prices and two chances to charge
    somebody for a board that was still lost. It is a **collaborator** rather than more of
    `RunScreen`, which had reached five responsibilities; `RunLessons` came out at the same time
    and for the same reason, leaving the screen with what a way out costs and when a run may
    run. `RunStakeTests` fails if a mode declares any of it, or if either collaborator is folded
    back in.
    <br>**It cannot inflate a reward, and that is arithmetic rather than a promise.** Stars are
    held against par and never against the budget (invariant 22), so a run that has reached its
    fail state has already spent past the two-star line and can score **one star at most** —
    less than replaying the glade for nothing would pay. The offer sells a *finish*, never a
    *grade*. `Puzzle.Granted` and `WeaveInk.Grant` therefore move the budget and touch nothing
    else; `WeaveInk.Spent` in particular is the grade and is never given back, unlike
    `Refund`, which is undo's. Nothing about a continue reaches the save file: **no schema
    version, no merge rule, no server work.** The gems leave through
    `PlayerProgression.TrySpend`, which carries an idempotency key, works on a plane and is
    refused by `submitSpends` on the next sync if the server-derived balance could not cover it
    — the same two lines that buy a companion.
    <br>**A continue that does not continue is a charge, so the shortfall is cleared first.**
    This is the half that is not obvious and the half a mode alone can answer. A glade is lost
    when its counter reaches the budget and *any* turn makes it playable again, so its deficit
    is nought and the offer is exactly what the table authored. A weave is lost when the light
    left cannot cover the cheapest possible finish, which usually leaves cells in the pot that
    cannot be spent — so selling the authored twenty alone would put the player back on a board
    that is still provably unwinnable and end the run again in the same frame, having taken
    their gems. `WeaveVerdict.Deficit` is the two lower bounds `Read` already takes, kept
    instead of thrown away, and `ContinueOffer.Amount` is `deficit + authored`.
    `RunContinueTests.ContinuingAWeaveHandsOverEnoughToActuallyCarryOn` drives the exact board.
    A mode that cannot be rescued at any price answers `RunContinue.NoContinue` and is never
    sold one. And the backstop for everything else is that a grant is never *silent*: if one
    somehow left the run lost, the fail state fires again and the player is **asked again**
    rather than billed again.
    <br>**Short of gems must never navigate.** Every other short balance in this game opens the
    shop, which is right everywhere else and catastrophic here — the board behind the panel is
    frozen at its fail state, so a player who tapped "get gems" to *save* their run would lose
    it on the way to paying for it. `GemShopOverlay` brings the shelf to them instead, stacked
    on the offer, and steps out from under the receipt when the gems land so the offer is still
    standing with its price now affordable. It is deliberately not a second shop: no tabs, no
    supplies, no restore line. Note also which branch is *withdrawn* — short of gems in a build
    with no store is `ContinueChoice.Unavailable` and no offer at all, because a control that
    can never work is worse than no control.
    <br>**The price is content** (`continueRun` in `progression.json`, `ContinueTable`), for the
    heart gate's and the chapter gate's reason: it is charged to real players at the worst
    moment in a session, it is the number most certain to be wrong on the first guess, and
    finding out must not cost a store review. `enabled` is an **integer** tri-state and not a
    bool — `JsonUtility` instantiates a `[Serializable]` class field even when the JSON has no
    such key, so a bool would read `false` for every client that had not taken a content push
    and withdraw the feature silently. `gemsStep` ships at 0, so the price is flat and a run may
    be continued as often as somebody can pay; the field exists because an escalating continue
    price is this genre's commonest retune and its shape decides whether that is a push or a
    review. It is deliberately **not** seeded to `config/progression`: nothing about a continue
    is adjudicated. And `run_continue` stays a **retired** id — the analytics are
    `continue_offered` / `continue_bought`, and the spend reason is `continue:`.

23a. **A lost run has two prices and they buy different things.** `RunContinue` sells the
    *run* — the board stays exactly as it stood, the counter is already past the two-star line,
    so a bought run can only ever score one star (23). `HeartRescue` sells a *heart*, which is
    the gate rather than the run: the board is rebuilt from nothing and the fresh attempt is
    graded like any other. They are offered by different panels in a fixed order — the continue
    first, over a board that is still standing; the rescue only on the defeat panel that follows
    it, and only when there is nothing left to play with — so nobody is ever shown both at once
    and the second is never a way to undo the first's refusal. They ship at the same 20 gems on
    purpose: the two can be met on one screen a minute apart, and a player who declined one price
    and is then quoted another reads the pair as haggling.
    <br>**Neither costs the save file, the wire or the server a thing**, and the rescue is the
    plainer case: hearts are already a produced/spent ledger merged by `max` (11b) and are
    already sold for gems in the shop (18), so this is a second call site for two proven paths —
    no schema version, no merge rule, no server work. It cannot inflate a reward either, because
    hearts pay nothing: stars come from turns against par (22) and credits from the star ledger
    (9), so the only thing gems buy here is *sooner*.
    <br>**The withdrawal switch is `hearts.rescueHearts: 0`, and the price refuses a zero.** Those
    are opposite readings of the same shape and both are load-bearing. An offer that hands over
    nothing is not a cheap offer, it is no offer — so nought hearts is the one clean way to say
    "withdrawn", which a market that regulates paying past a play gate may need from a config
    push. A *free* heart is the gate no longer gating, so nought gems is refused and named
    (invariant 5d's complaint about a rule that rejects nothing, applied to the only thing in
    this game that can stop somebody playing). Both fields carry a negative sentinel for
    `ContinueDto.enabled`'s reason: `JsonUtility` writes a zero into every field an older file
    never had, so a default of zero would withdraw the feature silently on every client that had
    not taken the push.
    <br>**The free way back is always drawn above the paid one.** `DefeatPanel` owns that, and it
    is not a layout preference — a panel that puts a price above a rewarded video at the moment
    somebody has just been stopped from playing is the shape a store reviewer is right to call a
    dark pattern, which costs a submission rather than a metric. The panel's height is derived
    there too: it can now take five shapes and its height had been two typed constants (880 and
    1010), which is the arrangement `PanelStack` was lifted out of a panel that had been drawing
    its last paragraph 78 units into its own close button.
    <br>`GemChoice` (was `ContinueChoice`) and `GemPrice.ChoiceFor` are the one branch both
    offers ask — spend, buy gems, or withdraw the control entirely — for invariant 9a's reason at
    the smallest scale it appears at. And **short of gems still never navigates**: `GemShopOverlay`
    is raised over the defeat panel exactly as it is over the continue offer, and the panel
    underneath repaints when the gems land so the price is affordable without a second tap.

24. **A run is free when it teaches nothing new, and the rule lives in one predicate.** Two
    clauses, and the predicate is `HeartStake.PriceOf`. **The opening**: the first
    `hearts.graceLevels` levels (3, content) of the **first chapter of each mode** cost nothing
    however they end. The heart gate is the only thing in this game that can stop somebody
    playing, and the worst moment to meet it is while they are still working out what the verb
    is — a player who loses their first three boards to a rule nobody has taught them yet is
    being charged for our teaching, before they have decided they like the game enough to wait
    eight hours. Per mode rather than once per account, because a mode shipping a year from now
    is somebody's first board of that mode: Budburst is tapped rather than turned and is lost
    on a satchel rather than on turns, so a player arriving having finished four glade chapters is a
    beginner again in every sense that decides whether taking a heart off them is fair.
    <br>**The replay**: a glade this player has **already finished** costs nothing, for ever. The
    gate exists to pace somebody through content they have not seen, and a board they beat is not
    content — it is a board they beat, gone back to for a better rating or for the pleasure of
    it. It also cannot pay for itself: stars are stored and only ever promoted (invariant 22) and
    credits derive from the star ledger (invariant 9), so a replay that beats nothing is worth
    nothing and the gate was guarding an empty room. What it was actually charging for was
    mastery, and the players who go back are the ones who liked the board. **Cleared, not
    attempted** — the clause reads `PlayerProgress.IsCleared`, which is `Stars > 0`, so a glade
    tried and lost still costs: the gate keeps its grip on any board that is still beating
    somebody. Two consequences follow and both are the point rather than side effects. Leaving or
    restarting a finished glade **raises no confirmation at all**, because a panel warning about
    a heart nobody is taking is how a player learns that the warning means nothing. And the map's
    door **opens on everything already finished with an empty heart bar**, since a glade that
    costs nothing to lose cannot coherently be refused for lack of something to lose — which is
    the one thing there is to do while hearts refill, and it is the half of the rule a player has
    no way of discovering, so `GladeRewardsOverlay` states it on every chapter that is not
    somebody's first.
    <br>**One run has one price, and `RunScreen.Price` is it** (`Staked` is its bool half). It
    is asked in exactly two situations: by the screen, once, as a fact about *its level*;
    and by the map's door, about a board nobody has opened yet. Everything that can take a heart
    for a run in progress reads the screen's answer — the abandonment (`RunScreen.Forfeit`, which
    also stops asking for a confirmation over an exit that costs nothing), the marker a dead
    process leaves behind, and the defeat, which is **told** (`RunLedger.Loss`'s required `price`)
    rather than working it out again. Asking twice is what invariant 9a refuses, and the second
    reading is always the later one, so it is the one that can be wrong.
    <br>**`Price` is a fact about the level, not about the run, and that distinction cost a
    silent bug.** Latching it at `Commit` and clearing it at `Resolve` reads as obviously right —
    a run is owed for between those two calls and not otherwise. It is wrong, and nothing
    structural says so: **both modes call `Resolve` a few lines *before* `RunLedger.Loss`**,
    deliberately, so a crash mid-defeat cannot charge twice. A stake cleared by `Resolve`
    therefore reads "free" at the exact instant the heart is taken — every lost glade in the game
    becomes free, with the heart gate still drawn on every screen. It compiled, it validated, and
    only playing would have shown it. So the answer is resolved per screen and untouched by the
    run lifecycle, and **the latch is one-way**: a free answer is kept for the screen's life,
    where a charged one is re-asked. Only one direction is dangerous — a board the player was
    told was free must never become one they are charged for on the way out of it, while a
    charged one becoming free costs nobody anything and is the honest reading of a rule that has
    just changed in their favour, which is what keeps a first clear followed by a restart on the
    same screen free. `RunStakeLifecycleTests.ResolvingARunDoesNotTurnAPaidGladeFree` drives the
    exact ordering and was watched to fail against the bug before it was kept;
    `ClearingAGladeMidScreenNeverTurnsAFreeRunIntoAChargedOne` pins the direction.
    <br>The marker is the other half. `RunGuard.Claim` runs from `Boot` **before any content has
    loaded**, so nothing at the claiming end can ask whether a glade was free — a free run
    therefore writes **no marker at all**, which says it in the only place that still knows.
    <br>**Nothing about it is stored.** A heart is simply not spent; the save file, the wire and
    the server are untouched, so no schema version, no merge rule and no server work — invariant
    14's preferred shape, and what makes the window free to retune from a config push at any
    time. The replay clause stores nothing either: it reads a record the save has kept since v1,
    which is why a second clause on the game's most sensitive gate cost one predicate and no
    format change. The opening clause keys on a level's *position*, which invariant 1 forbids
    only for things that are **stored**: this is the same derived reading of manifest order that `LevelUnlock`'s chain
    has always taken, so a drop that inserts a glade at the head of chapter one moves the window
    onto it — the intended meaning — and moves nothing anybody has earned.
    <br>**The defeat panel has to tell the silences apart.** "No heart was taken" is three
    pieces of news — nothing was owed because this is an opening, nothing was owed because the
    glade was already finished, or there was nothing left to take — so `LossRecord` carries the
    whole `HeartPrice` rather than letting the panel infer anything from `HeartCharged`. The
    first two need different sentences ("one of the free levels" over the fortieth glade of a
    chapter is a panel nobody believes twice), and the third is the opposite news: read a free
    run as an empty wallet and the panel refuses a retry to somebody who can use one, and read an
    empty wallet as a free run and it offers one to somebody who cannot. A free run also replaces the heart row
    with the reason, because five empty hearts under a run that spent none of them is a picture
    of a charge that did not happen, sitting directly above a working retry button.

25. **A variable reward the client shows must be one the server can recompute, and the bonus
    wheel is the third thing built that way.** The victory panel's video offer used to pay a flat
    figure; it now spins a wheel of eight equal slices, each a **multiplier on that placement's
    own amount** (`BonusWheel`, `ads.wheel` in `progression.json`). Everything about the placement
    is unchanged — one video, one server-adjudicated grant, one daily cap, one entry in the
    published ad table — so the whole feature costs the save file **no schema version, no merge
    rule and no `claimAwards` work**. The wheel decides the multiplier and nothing else.
    <br>**Why a multiplier and not eight authored prizes.** A prize the client names is a prize
    the server has to be told about, and invariant 10d is exactly why it cannot be told: LevelPlay
    9 carries no per-impression token from the phone to the verification callback, so "the client
    says it won a thousand" is evidence of nothing. What the server *can* do is recompute — the
    daily chest's trick (9c) — and a multiplier over an amount it already publishes is the
    smallest thing there is to recompute. The slice is a pure function of **(account, day, spin
    index)** through the same `subjectSeed` the golden bonus uses, so `BonusWheel.cs` and
    `functions/src/wheel.ts` arrive at the same wedge without either telling the other anything,
    and `wheelCases` in `firebase/shared/reward-vectors.json` fails a build if they ever stop.
    <br>**The spin index is server-owned, and that is the half that is easy to get wrong.** Two
    counters that both increment drift the first time a callback is delayed past the next win, and
    the visible form of that drift is a wheel landing on five hundred while the balance rises by
    two. So it lives on `players/{uid}/private/wallet` where no client may write, advances only
    inside the transaction that *grants* a win-bonus view, and rides back on every wallet reply —
    `containersRevoked`'s shape (18d) for the same reason. `readWallet` must carry it through or
    the next write deletes it, which is 12a for the third time in that document.
    <br>**Presence of the field is what says a deployment understands the wheel**, and it is what
    removes the deploy-ordering hazard rather than writing it down: a client that has heard
    nothing draws no wheel and falls back to the flat `AdOfferOverlay`, which is exactly what such
    a server grants. For the same reason an absent `wheel` block means the **flat offer**, never
    the built-in ladder — the one table here that does not fall back to its own default.
    <br>**The odds are uniform and printed, because they can be.** Every slice is the same size
    and equally likely, so the disclosure is one sentence with a number in it (10b's property, for
    the second feature). A weighted wheel drawn with equal wedges is a lie the picture tells and
    is the specific lie loot-box rules exist to catch; the variance lives in the ladder, where a
    player can see all of it at once. A slice may never pay **below the flat offer** (`GoldenRules.
    MinPercent`'s argument word for word), and a wheel with no slice above it is **refused** — a
    spin that rejects no outcome is decoration, which is 5d applied to a reward table.
    <br>**A spin cannot be re-rolled, and that falls out of the seed rather than being enforced.**
    Backing out, force-quitting mid-animation, or coming back an hour later all recompute the same
    slice. What advances the index is a *paid* spin, and `WheelStand.NextSpin` is the **larger**
    of the server's tally and `RewardedAds.WatchedToday` — both count the same thing, and each
    can be the one that knows more. Taking the server's alone leaves a window between collecting
    a reward and hearing about it where the wheel re-shows the slice just paid out; the maximum
    closes it and cannot go backwards. The local half only ever moves in `Redeem`, so an
    abandoned spin, a dismissed video and a no-fill all leave it exactly where it was.
    <br>**The payoff is a panel of its own, and it owes the economy nothing.** The wheel asks a
    question; `WheelPrizeOverlay` answers it — the coin landing, the figure climbing out of it and
    a COLLECT under it, which is the shape `ChestOverlay` and `ShopGrantOverlay` already use for
    the other two places currency is handed over. It used to be a caption change on the button
    that had just asked for the ad, which drew the largest moment in the placement as the smallest
    change on the screen. Two things about it are load-bearing rather than decorative. It is
    raised **before** the wheel is asked whether it still exists, because a player who backgrounded
    the app during the video may be standing somewhere else entirely and the prize is theirs
    either way — nothing on the panel is a step in getting paid, and force-quitting during the
    confetti pays exactly what tapping the button pays. And the offer button underneath goes to
    **COLLECTED, unclickable**, latched on the panel rather than read off the placement: a
    cooldown and a cap both *expire*, so a player sitting on a victory screen for five minutes
    would otherwise watch the offer come back and buy the same glade's bonus twice.
    <br>**The economy moved and it was a swap, not a raise.** The shipped ladder averages 218.75%,
    so `win_bonus` really pays about **438** a view instead of 200 — and its daily cap went from
    twelve to six in the same drop, leaving the day's ceiling at ~2,628 against 2,400 before.
    Fewer, better videos. `Validate Content` and `Tools/verify/content.py` both print the real
    per-view and per-day figures beside the free-play number, because the authored `amount` stops
    being what a view is worth the moment a wheel is published and nothing else in the file says
    so.

26. **A mode that cannot be lost is a prototype, and Lightfall was one.** It dealt random
    colours into an empty well until a column filled up, and every consequence of that one
    decision was invisible in the file. A board with no fixed future cannot be *searched*, so it
    could author no par; with no par there is no star line and no budget; with no budget there is
    no fail state; and with none of those it is not a level, it is a toy with a score on it. What
    it actually shipped was `LevelTuning.Default(1)` — a par of one that nothing read — and two
    players on the same "level" were not playing the same board. **A well now authors what is
    standing in it (`rows`) and what it deals (`motes`), and nothing else.** Par is the fewest
    drops that empty it without ever breaching the brim; the two star lines and the supply the
    run is dealt are the same 1.20 / 1.40 / 1.60 multiples of par every other mode uses, so a
    second mode still cannot retune the economy (invariant 9). The procession is **ordered and
    repeating**, which is invariant 20e's argument in a second place: light never comes back, so
    the *set* of colours could not otherwise matter in what order — an ordered deal makes each
    drop an assignment rather than an aim.
26a. **The chain the mode was documented as having did not exist and could not.** The old rule
    destroyed a white mote and the four motes touching it, and both `FallBoard` and its tests
    described the cascades that set off. There were none: nothing changed a mote's colour except
    a drop, so the first wave took every white on the board and the second could never find one.
    The wave counter, the rising pitch and the chain multiplier were all dead code against a rule
    that rejects them — the same "rejects nothing, so it is decoration" fault invariant 5d names
    for mechanics, in the one place nobody thought to count. What replaced it is *one*
    destruction and a spread: **a white mote bursts alone and washes the colour that finished it
    into the motes beside it**, so any of them thereby completed bursts in turn. That is what
    makes one drop worth more than one mote, and it decides what the mode *is*: dropping blue
    clears the yellows, leaves the reds and greens it passes one step better, and reaches a mote
    buried at the bottom of a column that no drop could ever land on. Before changing it, note
    the ordering the whole thing rests on — a wave decides what bursts and what it washes from
    the positions the bursting motes are standing in, **before** anything is removed and
    **before** anything falls. Apply the wash after gravity and a mote is standing in the burst's
    own cell rather than beside it, so nothing ever touches it.
26b. **Two fail states, and only one of them may be sold a continue.** The supply running out is
    invariant 22b's budget in the unit the mode is graded in. The **brim** — row nought, drawn
    with a hard line under it — is the other, and it is what makes each individual drop a spatial
    decision: a colour the top of a stack already holds has nowhere to go but upward, so one
    wrong mote costs a row of headroom *and* a mote from a finite supply. Running dry is a
    shortage and more motes fix it; a flooded well is not, so `ContinueDeficit` answers
    `RunContinue.NoContinue` for a flood. That is the honest answer rather than a gap — no amount
    of supply empties a well that has already reached its brim — and it means the mistake money
    cannot fix is the one skill is about. Both are read by `FallVerdict`, in one predicate, for
    the reason `WeaveVerdict` is: three booleans in an `if` on a screen is three edges where the
    run is decided and the screen has not caught up.
26c. **A procession must carry all three channels, and the well that cannot be lost is why.**
    The obvious rule is the weaker one — a deal has to supply every channel the *board* is
    missing — and it is wrong by one step. A drop onto bare ground puts a fresh pure mote in the
    well, wanting the two channels it does not hold, so a two-colour procession can be walked
    into a position no amount of play recovers from. On a well with a supply that is an ordinary
    loss. On the opening well, which is authored without one for invariant 24's reason, it is a
    board that can be neither won nor lost — invariant 20g's state, reached by arithmetic rather
    than by a rule nobody could see. It costs authoring nothing, because the deal repeats: one
    character.
26e. **A well's room to err is a count of drops, never a multiple of par — and that is the one
    number a shared factor could not carry.** Every other mode's budget is `par x budgetFactor`
    and it works there because a mistake costs a fixed fraction of the board: a glade's wrong turn
    is *free* (undo refunds it, without limit, so the meter only ever counts turns the player
    meant), and a weave's wrong channel costs the light it covered while leaving the grove exactly
    as it was, two of them a grove handed back in full. A well's wrong drop is neither. It is
    permanent **and it makes the board worse** — the wasted mote lands in the well and now has to
    be cooked to white like everything else — so one mistake is worth about two drops.
    <br>Against `par x 1.60` that gave the second level of the chapter *two* drops of room, which
    is one mistake, and the tenth *four*. Reported from play as "one wrong fall and it shows out
    of turns", on level 2, which is exactly where par is smallest and the proportion buys least.
    Raising the factor cannot fix it and is worse in the other direction: 2.60 gives par 2 the
    room it needs and hands a par-6 well ten wasted drops, at which point the fail state rejects
    nothing and is decoration (invariant 5d). **The room a mode needs is a count when the cost of
    a mistake is a count.** So `LevelTuning.Slack` exists, `FallRules.DefaultSpare` is 5 — two
    mistakes and a little — and it is the same on the second well and the tenth, because the
    budget is a fail line and difficulty is the boards' job; a per-chapter ramp on the fail line
    was tried on the glades and removed for that reason.
    <br>**The star lines did not move and must not.** They stay multiples of par, which is the
    whole division of labour: stars measure how well a board was played, and the budget only
    stops a run that has become hopeless. A generous fail line does not make a level generous —
    it makes the stars the thing being asked for. `CheckStarBands` grows a branch rather than an
    exception, because it reads the factors and a well no longer uses them: a check that
    disagrees with the thing it checks is worse than no check, which is the same reason that
    method reads factors rather than the thresholds they derive. What has no test and now does is
    the claim the player made for us — `FallLadderTests.EveryWellForgivesTwoMistakes`.

26d. **Par may be resolved lazily, and this is the mode that needed it.** A glade's par falls out
    of walking its grid; a well's is a breadth-first search, which is milliseconds rather than
    microseconds, and a chapter body holds ten of them. Paying for all ten while the map is
    opening is a hitch on the one screen that never asks the question — par is read by the run
    screen and by the validator, and by nothing that draws a map node. So `LevelTuning` takes a
    `Func<int>` as well as a number and calls it once, the first time somebody asks. That is the
    only place that class is not strictly immutable and the memo is safe to race on, because the
    function is a pure search over a frozen board. Lazy is not free, though, and the cost has a
    gate of its own: `FallValidator` warns above 40,000 positions and **refuses** a level above
    120,000, because that is about a quarter of a second of nothing happening on a phone on the
    way into a level. Those two numbers are measured rather than guessed, and they are a
    different question from `FallSolver.NodeBudget` — the budget has to be large enough to
    *prove* a hard board, since a board with no par cannot be graded at all. Cost goes as the
    column count to the power of par, so par 7 on a six-wide well is four times par 6 on the
    same board: the cheap fixes are a narrower well or a shorter answer, never a bigger one.
    <br>**And the same cost curve puts a ceiling on par, which decides where a chapter's ramp
    can live.** Budburst's first chapter is ten groves and every one of them is **par 3**, not by
    preference: cost goes as the flower count to the power of par, so a par-4 grove big enough to
    cascade is refused by `BudValidator`'s node ceiling, and a par-4 grove small enough to prove
    comes back — measured, thousands of seeds — at twenty flowers with a **one-wave** best tap.
    That board is solvable, correctly par'd, fully validated and has the mode taken out of it,
    which is invariant 20j's third test failed by arithmetic rather than by design. So the ramp
    was spent on what does not multiply the search: board size, how many are shut in, how many
    need two cracks, old wood, `spare`, and whether a careless run still scores three stars. The
    generalisation is worth stating before the next searched mode authors a chapter — **a mode
    whose par is found by search has a ceiling on par, and the ceiling is lower the more the mode
    puts on the board.** Find it before designing a ladder around par, because CONTENT.md's rule
    that *par is length, not difficulty* stops being a preference there and becomes the only
    option.
    <br>**Lightweave joined it later, and how it got there is the general lesson — kept
    although the mode is not, because it is the only recorded case of a mode's par becoming
    expensive without its code changing.** A weave's par
    means *generating* the grove, and generating means carving until one passes the acceptance
    bar — so par is cheap exactly while good boards are common. When `w03_wildhedge` was re-dealt
    to make its hedges bite on more than one channel, the bar tightened from about 1.1% of seeds
    to 0.3%, and the cost of a board rose with it: measured on Unity's Mono, the Wildhedge's ten
    groves take **965ms** between them against **41ms** for the Weftwood's and **19ms** for the
    Nightloom's, with one grove alone at 201ms. `WeaveMode.Tune` was resolving par eagerly, so all
    ten were built while the chapter body parsed — which is the map opening, the one screen that
    never asks what par is. Nothing about that is visible in a compile, a validator or a test; it
    is a lag on a screen, and it arrived from a change to *difficulty*. The reading to keep is
    that **a mode's par can stop being cheap without its code changing**, so anything deriving par
    from a search should be handed over as one from the start.

28. **A mode that cannot be lost is a prototype, and Groovekeeper was the second one.**
    Invariant 26 arrived at again by the same route and answered the same way. It dealt random
    colours onto empty ground until a fixed number of tiles ran out, and every consequence of that
    one decision was invisible in the file: a board with no fixed future cannot be *searched*, so
    it could author no par; with no par there is no star line and no budget; with no budget there
    is no fail state; and with none of those it is not a level, it is a toy with a score on it.
    What it shipped was `LevelTuning.Default(1)` — a par of one that nothing read — and two
    players on the same "level" were not playing the same board. **A groove now authors its
    ground, the beds that have to bloom and the procession it is dealt, and nothing else.** Par is
    the fewest tiles that open every bed, found by search (`KeeperSolver`), and the two star lines
    are the same 1.20 / 1.40 multiples of par every other mode uses, so a second mode still cannot
    retune the economy (invariant 9). The whole feature cost the save file **no schema version, no
    merge rule and no server work**, because a Groovekeeper level is an ordinary level with its
    own permanent id (invariant 20a).
28a. **The inversion is the mode, and a bed is what turns it into a puzzle.** Every edge-matching
    game ever made rewards putting like against like; this one rewards the opposite — a seam
    between two unlike colours is worth something and a seam between two of the same is worth
    nothing, and a tile whose own colour and its neighbours' between them carry all three
    **blooms**. That alone is a toy. What makes it a level is the **bed**: a cell the author marked
    that has to end up holding a bloomed tile, so the question every turn is not "where does this
    fit" but "what does this one complete, and what does it leave the next one able to complete".
    <br>**One tile can open five, and that is the same play as the shortest answer.** A planting is
    read against the cell it lands on *and* the four beside it, because a tile that was one channel
    short a moment ago is not short any more — so the ceiling is five and it is a fact about the
    rules rather than a taste (`KeeperFlourish.Most`). It is also exactly what par rewards, which
    is the whole design: the prettiest play and the most efficient one are the same play, and the
    chapter's fifth groove is built to say so before a word is read. Note what falls out of the
    board being append-only: **blooming is derived rather than stored**, so a solver's state is the
    grid and nothing else, and there is no flag for the two to disagree about (invariant 9a).
28b. **The room to err is a count of tiles, and the fifth of them is the two-star line.**
    Invariant 26e for a second mode: a wrong tile is permanent *and* it takes a cell of ground a
    bed beside it may have needed, so a mistake is worth about two tiles wherever it happens and
    a fraction of par would give a short groove almost no room at all. What is new is why the
    count is **five** rather than four. A budget of `par + spare` has to clear `ceil(par x 1.40)`
    or the bottom band is stranded and every clear is worth two stars or three — invariant 22's
    fault reached from the budget's side instead of the star line's. Four holds to par seven and
    collides at par eight, which is exactly where this chapter's finale sits; nothing but
    `LevelValidator.CheckStarBands` noticed, and `KeeperLadderTests` is what says so if a later
    chapter ever goes deeper than five will carry.
28c. **Two fail states, and only one of them may be sold a continue.** The basket running out is
    a shortage and more tiles fix it, so it is offered one at `ContinueUnit.Tiles`. A groove with
    **nowhere left to grow** is not: no number of tiles gives it somewhere to plant, so
    `KeeperVerdict` answers `RunContinue.NoContinue` and the offer is never made — invariant 23's
    rule, and Lightfall's flooded well one mode over. It also means the mistake money cannot fix
    is the spatial one, which is the half this mode is actually about.
    <br>**That is also the exact reading of "the first groove cannot be lost".** What the negative
    `budgetFactor` turns off is the *basket*, not every ending — Lightfall's first well is authored
    the same way and can still flood. A groove with nowhere left to grow is over whatever its
    basket says, because there is genuinely nothing left to do on it, and a board that can be
    neither won nor ended is the one state invariant 20g forbids. On the opening groove reaching it
    means filling twenty-seven cells without opening either bed.
28d. **Composting is the one move that changes nothing, and it costs a tile for that reason.**
    A heartbed refuses every colour but its own, so a run can be holding exactly the wrong tile
    with the right bed waiting — and the honest answer to that is not a free re-deal but a priced
    one. Both ways of spending take the same tile from the same basket, so what the player is
    being asked is simply "is moving the procession on worth a tile", which is a decision they can
    take with the basket in front of them. It is allowed on the **last** tile too, and that is
    deliberate rather than an oversight: withholding it there reads as protective and is the one
    setting that can produce a groove which will not end — a last tile no cell will take would be
    unplayable and unspendable at once, which is invariant 20g's state exactly.
28e. **A heartbed refuses rather than spoils.** A bed drawn in a colour takes that colour and no
    other, and the wrong tile cannot be planted there *at all* — so nobody can kill one with a
    mis-tap, and the bed wears its colour where anyone can see it before they tap. That is what
    turns the ordered procession from scenery into the puzzle (invariant 20e for a third mode):
    a plain bed is opened by whichever tile happens to be in hand when its neighbours are ready,
    where a heartbed has to be reached with one particular tile and everything in between has to
    be dealt with.
28f. **The proof that a bed is lost never ends a run, and only ever decides whether it would be
    honest to sell one.** `KeeperBoard.AnyBedLost` is Lightfall's removed clause kept for the one
    question where it is exactly right. Ending a run on it is the mistake `FallVerdict` shipped
    and took back: it came back from play as a run that ended while the tray still had motes in
    it, which reads as the game deciding on the player's behalf and is indistinguishable from a
    bug unless you already know the rule being enforced. A player who wants to spend their last
    three tiles on a groove that cannot be finished is entitled to. Both its clauses are
    certainties rather than heuristics, because the answer decides whether money changes hands, so
    it under-reports and never over-reports.
28g. **A Groovekeeper procession need not carry all three colours, and that is the one place this
    mode is not Lightfall.** A well refuses a two-colour deal and has to: a drop onto bare ground
    there makes a fresh mote wanting the two channels it lacks, so the procession can be walked
    into a position no amount of play recovers from (invariant 26c). Nothing here does that. A
    tile that cannot bloom is simply a tile, the sprigs standing on the ground are permanent, and
    **two of the ten grooves that ship are finished with a two-colour basket** precisely because
    the third colour is already on the board. The check was written anyway, by reflex, and errored
    on both of them; what matters is that every bed can be *opened*, which is what the search
    proves. Copying a rule across from a mode that looks similar is how a gate comes to refuse
    correct content.
28h. **The search is what the mode rests on, so its floor is the thing to be careful with.**
    Par is found by iterative deepening over a grid whose every tile's colour is decided by *when*
    it was laid, so two orderings of the same cells are two different states and a breadth-first
    frontier grows like permutations. Both prunes are exact — a bound that could ever be too high
    would cut the shortest answer and hand back a par nothing can reach. The one worth
    understanding is the **floor**: beds whose closed neighbourhoods touch may share a tile, so
    their costs are grouped and only the worst of each group counts, while groups more than two
    steps apart are **added**. Taking the maximum instead left a two-bed groove with a bound of
    three against a real answer of six and the search walked a quarter of a million positions it
    could have cut. The distance term stays a maximum and is compared rather than added, because a
    path to one bed may well be a path to another — that is the one part that could double-count,
    so it is the one part that is not summed. In practice the cost goes roughly as the open cell
    count to the power of par: **par eight on tight ground is a few hundred positions and par nine
    on open ground is a few hundred thousand**, which is why the chapter tops out at eight and why
    `KeeperValidator` refuses a groove above 90,000 (the player's device runs this same search once,
    when somebody opens the level — invariant 26d).


## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Persistence/ Progression/ Homestead/ Cloud/ Localization/ Analytics/ AssetPipeline/
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
  App/ Board/ Screens/ Dev/
Assets/Game/Authoring/             GlimmerGrove.Authoring    (Editor-only; Domain)
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode; Domain, Authoring, Cloud, Presentation)
Assets/StreamingAssets/Content/    manifest.json, chapters/, homestead.json, loc/
```

**`GlimmerGrove.Authoring` is the home for a rule that decides whether content is fit to ship
and that no player ever runs.** It exists because those rules are under two constraints at once
— the build gate has to reach them, and so does the test assembly, which references `Domain` and
*not* `Editor` — and for a long time `Domain` was the only place that satisfied both. So a seed
sweep, a map-collision check and a chapter-mode check were compiled into every player build and
never called. An Editor-only assembly both can reference satisfies the same two constraints and
none of the third. The membership test is mechanical: **a rule belongs there when no shipped type
references it**, and `compile.py` proves it by building `domain` *without* `Authoring` on its
reference list, so a Domain file that starts calling into it fails offline rather than quietly
dragging the folder back into the build. `Assets/Game/Authoring/README.md` says what is
deliberately *not* there and why — which is now nothing at all: the one exception was
Lightweave's solver, and the mode is retired.

**A mode is declared three times, and the third one is what moved the validator.** `LevelMode`
(Domain) says what a mode *is*; `ModeLook` (Presentation) says what it *looks like*, split off
because Domain may never reference Presentation; `ModeValidator` (Authoring) says how it is
*proved fit to ship*. That last split is what let `LevelValidator` — six hundred lines proving
boards solvable, arms mated, taproots binding, star bands landable — leave the player build.
It could not before: `LevelMode.Validate` was a `virtual` member, so the authoring entry point
called into the mode and the mode called back into it, and the cycle pinned both wherever the
runtime could see them. The price of a registry over an abstract member is that an entry can be
*missing* where an override cannot, and a missing one is silent in the worst way — a green tick
over a mode nothing looked at. So `LevelValidator` reports an unregistered mode as an **error**
rather than a pass, and `ModeValidatorTests` fails the build when the two registries drift.

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
  derive par, confirm every loc key resolves. It also runs `board-vectors.json` through both
  Python copies of the four-armed-tile rule; `BoardVectorTests` runs the same file through the
  C# one (invariant 5f).
- **Difficulty check:** `python Tools/verify/difficulty.py` — what each glade actually asks
  of a player, counted rather than argued about. Not a gate; see invariant 5d and
  *What makes a glade hard* in `CONTENT.md`. It enumerates rotations of a grid of conduits,
  so it reports glades and names the other modes as skipped rather than stopping on them —
  it used to die on a `KeyError` at the first non-glade chapter, which was every run since
  the Hollow shipped. `dealt` is the one column about the board as the player *meets* it
  rather than about its solution — conduits already right over turnable ones, plus any
  critters already awake. It is invariant 5g's reading and the only one there that a player
  meets in the first second.
- **Shop art check:** `python Tools/make_shop_art.py --check` proves the twelve coin and gem
  pictures on disk are what the tool would cut. It needs the source packs (see the
  art-source-packs note) because the sheets live outside the repo. **It proves reproducibility
  and says nothing about quality**, and that distinction shipped a broken card: the coin sack
  is dark brown on a dark purple ground, its bottom edge went undetected, the silhouette fill
  drained out through the gap, and the 9K card drew the sack's white outline wrapped around
  nothing — right size, correctly centred, 24% opaque, every check green. Two numeric gates
  were tried afterwards (how much of a sprite is outline; whether its silhouette encloses
  anything) and **neither separates a broken cut from a healthy one**, because a bite out of
  one side is not distinguishable by any global statistic from a thin part that belongs there.
  So `--contact <png>` lays all twelve out at the size a card really draws them on the card's
  own plate colour, and looking at that is the gate. `render_wheel.py`'s bargain, for the
  second time.
- **Word list check:** `python Tools/make_name_blocklist.py --check` proves the checked-in list
  is what the tool would write, and refuses the four ways a blocklist goes quietly wrong. The
  filter itself is `npm --prefix firebase/functions test` (`names.mjs`, `reports.mjs`).
- **Sound name check:** `python Tools/verify/sfxnames.py` proves the three lists agree — what
  the code plays, what is on disk, and what `AssetManifest.Sfxs` preloads. This is the gap the
  asset-names note describes, closed for audio: a misspelled sound name was a runtime
  `InvalidKeyException` and a silence that shipped green (`Audio.Sfx("tap")` did, twice), and
  `press.wav` sat unplayed for months. It reads **literals only**, and scans `Presentation`
  alone — `Audio` lives there and Domain may never reference it, so a literal in Domain is
  never a sound name. `Art.S`/`Art.Frames` still have no such gate.
- **Sound check:** `python Tools/make_sfx.py --check` proves the twenty shipped clips are what
  `Tools/sfx.tsv` cuts, and `--report` prints what each measures — including the one reading
  no sound library can give you, which is how many copies of itself a clip has to stand
  alongside at its busiest moment against `Audio.PlayOne`'s ten-voice pool. **`--check` proves
  reproducibility and says nothing about whether it sounds any good**, which is
  `make_shop_art.py`'s bargain in a second medium: `--contact <html>` writes a self-contained
  page that *plays* the set — each clip alone, at the pitch ladders the game really uses, at
  the rate it really repeats them, and in four scenes assembled from real play. Press that.
  Every wrong choice this set has made was inaudible one clip at a time.
- **Groovekeeper check:** rolled into `content.py`. A groove is the other non-glade mode whose
  whole level is in the file, so the offline gate proves it: every board searched for par, a
  sprig to grow from, a bed to open, a heartbed whose colour the basket actually deals, and the
  four readings (`beds`, `heartbeds`, `ways`, `greedy`) printed beside par and the basket.
  `keeper-vectors.json` is the contract between `Tools/verify/keeper.py` and the shipping
  `KeeperBoard`/`KeeperSolver` — `content.py` runs it through the Python copy and
  `KeeperVectorTests` runs it through the C# one, so the bloom rule cannot drift quietly
  (invariant 9a).
- **Lightfall check:** rolled into `content.py`. A well is the one non-glade mode whose whole
  level is in the file, so the offline gate proves it: every board searched for par, the brim
  row empty, nothing floating, the procession carrying all three channels, and the four
  difficulty readings (`motes`, `headroom`, `ways`, `greedy`) printed beside par and the supply.
  `fall-vectors.json` is the contract between `Tools/verify/fall.py` and the shipping
  `FallBoard`/`FallSolver` — `content.py` runs it through the Python copy and `FallVectorTests`
  runs it through the C# one, so the burst-and-wash rule cannot drift quietly (invariant 9a).
- **Budburst check:** rolled into `content.py`. A grove is the third non-glade mode whose
  whole level is in the file, so the offline gate proves it: the board searched for par, the grove
  shown to be **authored settled** (no bunch of three standing before a tap is spent), every cocoon
  shown to have a flower beside it, the basket shown to deal pure colour only, the star bands
  ordered, and the readings (`ways`, `careless`, `nodes`) printed beside par and the satchel.
  `bud-vectors.json` is the contract between `Tools/verify/bud.py` and the shipping
  `BudBoard`/`BudSolver` — `content.py` runs it through the Python copy and `BudVectorTests` runs
  it through the C# one, so the mix-and-wash rule cannot drift quietly (invariant 9a). Every case carries a **play** as well as a par, which is
  the half par alone cannot pin. Note that two of the readings are read **backwards** on this mode
  — see invariant 20k.
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
  Test Runner (EditMode). **Reload the domain before believing a failure that follows a play-mode
  session.** `Boot` starts the cloud backend and its threads, and those statics survive leaving
  play mode — so `AccountSwitchTests` fails on a main-thread violation (`GetString can only be
  called from the main thread`) raised by a sync that is still running, in a test that passes on
  every clean domain. `EditorUtility.RequestScriptReload()` and re-run: 1241/1241. The failure has
  nothing to do with whatever was just edited, which is exactly what makes it expensive.

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
- **A vector file that only the Editor can read is not a guard on the rule it pins.** Invariant
  9a says a rule existing twice must be held to a shared file; what it does not say is that the
  file has to be *readable where the code is edited*. Every `*VectorTests` here loads its JSON
  through `JsonUtility`, which is a native call, so the offline runner reports the whole fixture
  as "needs the Editor" and it is the one gate nobody runs on the way past. Budburst's wash rule
  drifted from its mirror and **every offline gate stayed green**: the board parsed, the Python
  copy proved par 3 in 7,903 positions, `content.py` printed `0 error(s), 0 warning(s)` — because
  the mirror is a *different copy* and it happened to be the correct one. What noticed was
  `ContentBuildGate` refusing to prove par at all, twenty minutes into an Android build, with the
  C# search hitting all 120,000 positions on a board the mirror settles in 7,903. So a mode whose
  rule exists twice needs at least one fixture that pins a **shipped board inline** —
  `BudLadderTests`, `FallLadderTests`, `KeeperLadderTests` — because that runs offline and is
  therefore the copy-versus-copy comparison that actually happens. The vector file stays; it is
  broader and it is what the Editor run proves.
  <br>Two smaller things the same bug taught. **A flood fill's `_seen` array means *visited*, not
  *chosen*** — the guard read `!_seen[nb]`, meaning to skip a flower that is itself bursting, and
  it also covered every flower already scanned as part of a group of one or two that was
  *discarded*, so the wash stopped in one direction and not the other purely by index order. And
  **par is a bad canary for a rule that got stricter**: a wash that stops early makes a board
  harder, so par coming out one higher looks exactly like a level somebody authored. What cannot
  look plausible is the best opening tap moving to a different cell and taking three fewer
  flowers, which is why `BudLadderTests` pins that as well as par.
- **A taproot whose members are four-armed conduits binds nothing, and only the Editor
  says so.** A four-armed tile wears all four arms at every angle, so `Puzzle.Alike` calls
  every rotation of it solved — which means `Board.root` hands it a start rotation of zero
  and the rune becomes paint. Nothing offline sees it: `author.check()` passes, the board is
  winnable, par is right, and `difficulty.py` reports the root removing nothing, which is
  also what an honest root on open ground reports. `ContentValidation` is the only thing
  that says it out loud ("every conduit on taproot 'A' looks the same in every
  orientation"), and it caught the first cut of `c01_bound_roots`. `c01_shallows.py`'s
  `taproot()` now asserts a period above one where the board is authored; the same trap is
  waiting for any chapter that hangs a fourth stub off a hub it meant to bind.
- **A licensed pack's preview sheets carry the vendor's own dummy lettering, and grading it
  makes it *less* obvious rather than more.** `craftpix-net-858776-level-game-assets/PNG/
  Backgrounds/1.png` is a flat panel with two blocks of placeholder text on it, and it was what
  the Hollow's only board backdrop was cut from. Reduced to luminance, blurred by 7px and graded,
  the words came through as two dark smudges that read as *something painted* — so a
  glance at the file says "a bit odd" rather than "that is text", and nothing else says anything
  at all: it imports, it addresses, `AddressableAudit` passes, `Validate Art` passes, and the
  content gates never open a PNG. There is no check that could catch it, for the same reason
  `--contact` exists in `make_shop_art.py` and `make_sfx.py`: a bad *cut* is a statistic and a
  bad *source* is a judgement. It reached no player only because `h01_emberfall` is not in the
  manifest, and the same pack's `Backgrounds/2.png` (the one it was nearly replaced with) has the
  same lettering on its houses. **Look at a source at the size the game draws it before naming it
  in `chapter_art.tsv`**, and prefer a pack's `layers/` art to its `_preview` sheets — the layers
  are what the pack is *for*, and the previews are what it is advertised with.
- **A VFX pack's `Textures/` folder is two different things with one naming scheme, and
  picking wrong is invisible everywhere.** Half of it is what a particle *draws* — a flash, a
  flare, a lightning trail, a fire flipbook. The other half is what its shaders *sample* — noise
  fields, gradient ramps, dissolve masks, colour LUTs. Both are white-on-black squares, the names
  do not separate them, and a UI `Image` will happily draw either. Budburst's first cut took a
  colour ramp for its flare, a bubble mask for its bolt and a streak-noise field for its
  shockwave; every one of them imported, addressed, passed `AddressableAudit`, passed
  `Validate Art` and drew on the board. There is no gate in this repository that can see it,
  Before naming a texture after the effect you want, **render it** — the name is a label
  somebody typed, and a contact sheet at the size the game draws it is the only thing that
  answers. Budburst's set is generated now and cost nothing to keep, but the trap belongs to
  every pack: see also the note below about a pack being the wrong shelf entirely.
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
  found by that mode's ladder tests passing in the Editor and failing offline — the same shape as
  the Mono/ICU divergence in invariant 19e, in arithmetic rather than Unicode, and invisible to
  every check that runs on one runtime. **Lightweave is retired and the runtime diff went with it,
  so what is left is the rule rather than the guard:** nothing that decides a *cell* may be a
  float, anywhere, ever. Every mode shipping now authors its board in the file and searches it in
  integers, which is why none of them needs a diff — and a mode that ever generates one again
  needs the diff back before it ships a single board.
- **Nor may a float decide a *threshold*, and that one shipped.** The same fault in the
  numbers a player is graded against, and worse in one way: `1.20f` is 1.20000004768…, so
  `Mathf.CeilToInt(45 * 1.20f)` is **55** where `par × 1.20` is exactly 54, and 61 against 60
  at par 50. Every runtime is wrong the *same* way, so unlike the generator above no diff
  could find it — it disagrees with arithmetic rather than with another runtime, and the only
  thing that ever noticed was the offline mirror, which had always used integers and whose
  table nobody was comparing. Four glades granted a turn more for three stars than the design
  says, for as long as the three lines have existed. `LevelTuning` now holds each factor as
  hundredths (`GoldHundredths`) and derives every threshold with `(par * n + 99) / 100`; the
  floats are what an author writes and what a retune moves, and nothing that produces a graded
  number reads them. `CheckStarBands` compares the hundredths for the same reason — a check
  that disagrees with the thing it checks is worse than no check.
  `PressureTests.AThresholdIsExactWhereTheProductLandsOnAnInteger` pins the two pars that
  caught it. Note what made it survivable: `LevelRecord.Stars` is stored and only promoted
  (invariant 22), so tightening a line re-grades nothing anybody has already earned.
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
- **Deleting art leaves its Addressables entry behind, and that fails the build rather than the
  game.** `AssetDatabase.GUIDToAssetPath` keeps answering with the old path for a while after a
  file has gone — especially when it went outside the Editor — so `AddressableSync`'s
  `DropMissing` used to keep every entry of a deleted folder: the GUID mapped to a path, and the
  path looked perfectly managed. Nothing at runtime cared, because nothing requested them any
  more. `BundleBuildContent` cares: it throws `Asset '…' is not a valid Asset or Scene` while
  `BuildPlayer` prepares, so the Android build dies before a line of the player is written and
  the only clue is one file name buried in a stack trace of package internals. Deleting a
  twenty-five-frame flipbook the game had stopped drawing cost exactly that.
  <br>Two fixes, and the second is the one that matters. `DropMissing` now also asks
  `AddressableRegistry.StillThere` — `AssetDatabase.GetMainAssetTypeAtPath(path) != null`, which
  is null in precisely the case the bundle builder rejects. And `AddressableAudit` now **errors**
  on any registered entry whose asset has gone, because every other gate here looks *outward*
  from what the game requests — the compile, the tests, `Validate Content`, `Validate Art` and
  the audit's own resolving-address pass were all green with twenty-five dead entries sitting in
  the global group. This is the one question that has to be asked from the other end.
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
  `difficulty.py`, `fall.py`, `keeper.py`, `bud.py`, and four shared contracts:
  `board-vectors.json` for the rule that exists in `LevelValidator`, `content.py` and `author.py`
  at once, `fall-vectors.json` for the burst-and-wash rule that exists in
  `FallBoard`/`FallSolver` and `fall.py`, and `keeper-vectors.json` for the bloom rule that
  exists in `KeeperBoard`/`KeeperSolver` and `keeper.py`, and `bud-vectors.json` for the
  mix-and-wash rule that exists in `BudBoard`/`BudSolver` and `bud.py`. See *Verifying*.
- `Tools/chapters/k01_grovekeeper.py` — the Clearing's ten grooves, and `k01_strings.py` for
  the strings that belong to the mode rather than to a level. Both `--check` themselves against
  what is shipped. The boards were hand-drawn against a sweep (`ways`, `greedy` and the cost of
  proving them), because the shape of a groove is what teaches and a random one teaches nothing.
- `Tools/chapters/f01_lightfall.py` — the Deep Well's ten wells, and `f01_strings.py` for the
  strings that belong to the mode rather than to a level. Both `--check` themselves against
  what is shipped. The boards were *searched for* rather than typed, because a random fill is
  almost never solvable — every pure mote needs two more channels and the stragglers pile up
  faster than any chain clears them.
- `Tools/chapters/*.py` — one module per glade chapter; regenerates the shipped JSON and
  `--check`s itself against it. `author.py` is the shared board DSL (`cross`, `root`, `briar`,
  `path`), and it derives a taproot's start rotations from the taps the root should cost rather
  than leaving four numbers that have to agree.
- `Tools/hollow/` — the Hollow's rule mirror, board generator and `build_chapter.py`. The
  mirror is never authoritative; the shipping C# solver is what `Validate Content` runs.
- `Tools/chapters/b01_thicket.py` — the Thicket's ten groves, and `b01_strings.py` for the
  strings that belong to the mode rather than to a level (they lived in the chapter file while it
  was one level; a mode's vocabulary outlives any chapter). Both `--check` themselves against what
  is shipped, and `b01_strings.py` is also what retired Ripplewake's. Every layout is drawn by
  hand and only the **fill** is swept — which colour stands where, and which basket the grove is
  dealt — which is the cheap half of the search and the half that decides how a board plays. The
  sweeper lays a grove in *pairs* on purpose: two alike touching is one wash from bursting, so a
  grove made of them cascades.
- `Tools/render_wheel.py` — draws the bonus wheel exactly as `WheelFace` does, without Unity.
  `render_grove.py`'s argument for the one other object here whose quality is only visible as a
  picture: everything provable about the wheel is proved, and none of it can say whether the
  colour ramp reads as worse-to-better or whether the twelve slices content could publish
  tomorrow would collide. It found four faults in one pass — near-black wedges, a ramp that put
  five of eight slices on one colour, two warm rungs that darkened to olive, and a jackpot the
  same gold as the rim.
- `Tools/grove_art.tsv` + `import_grove_art.py` — one row per grove piece (source, permanent
  id, slot kind, price, scale, lift, name). Copies the art, writes the loc string, regenerates
  the catalog and bumps `groveVersion`. It **refuses to remove an id it imported before**,
  because a piece id is in save files twice over.
- `Tools/make_chapter_art.py` + `chapter_art.tsv` — map strips and per-level backdrops, graded
  from the chapter's own JSON colours, so retuning a level's `accent` regrades its backdrop.
  **Two ramps, and they are not interchangeable.** `night` is what a *map* is graded with,
  because a map is the thing being looked at; `daylight` is what a *board backdrop* is graded
  with, because a board is drawn over it. `--only maps|backdrops` is what keeps a re-grade of
  one from silently re-cutting the other, and `-` in the map column says a chapter's strips are
  not this tool's to cut at all — four chapters share one hand-made set, and a row obliged to
  name *some* map source would overwrite it the first time anybody ran without the flag.
- `Tools/make_shop_art.py` — the shop's two money ladders, six painted pictures each, cut out
  of two licensed sheets. The background is keyed by **chroma rather than brightness**: both
  sheets put a soft coloured halo behind every object, and the halo overlaps the objects in
  brightness completely — a gem pile's own violet sits inside the range its halo covers, and the
  coin sack is in places *darker* than the ground it stands on. What separates them is that the
  halo is the ground's own hue scaled up, so it lies along one axis in RGB and everything painted
  carries some colour off it. The edge threshold that finds a silhouette is **deliberately low**:
  a silhouette open anywhere is not one, the fill drains out through the gap and the whole
  interior is lost, which is far worse than admitting a little speckle — and it is safe because a
  smooth halo carries brightness but no gradient, so it contributes no edges at any usable
  threshold. `--check` gates reproduction; `--contact` is how the cut is judged.
- `Tools/make_waterfall.py`, `Tools/make_grove_animation.py` — generated decor flipbooks. Rows
  they own are marked `_generated` rather than `_imported`, or the next import run warns
  forever about a row it no longer owns.
- `Tools/make_name_blocklist.py` — vendors LDNOOBW (27 languages, CC-BY-4.0); `--check` proves
  the checked-in list is what the tool would write and refuses the four ways a blocklist goes
  quietly wrong.
- `Tools/make_sfx.py` + `sfx.tsv` + `sfx_dsp.py` — the game's twenty sound effects, cut from a
  licensed pack. One row per name the code plays: source, transposition, length cap, gain trim.
  `--check` gates reproduction, `--report` measures, `--contact` is how it is judged. The DSP is
  split out so the cut can be proved without a 384 MB pack on disk. `Tools/sfx_meta.py` writes
  the twenty `AudioImporter` blocks, preserving each GUID — Addressables keys on the GUID, so a
  regenerated `.meta` silently unaddresses every sound in the game.
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
  tested migrations, monotonic merge (`SaveMerge.Join`). **Save schema v21.**
  Content schema: manifest and chapter bodies **v2**, grove body **v3**.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google
  linking, per-account local archive for switching, `SyncScheduler` debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water
  floors only. Hearts and hints are produced/spent ledgers (`RegenLedger`). Levels chain inside
  a chapter; chapters open on stars (`LevelUnlock`, invariant 21). A mode's opening glades are
  free to fail (`HeartStake`, see below).
- **Retention** — daily chests, streak (collected by hand), golden glades, event calendar,
  percentile standings, per-glade records (turns).
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads,
  refund sweeps, server-adjudicated grants, a gem-priced continue on a lost run that costs
  the save file nothing (invariant 23), and a bonus wheel on the victory panel's video offer
  that costs it nothing either (invariant 25).
- **The Grovement** — 14x14 isometric tile floor, land regions bought with credits, decor
  bought by the copy, residents projected from the companion roster, derived grove worth.
- **Boards** — public `groves/{uid}` cards, published rank distribution, unique keeper names
  with server-side filtering and reporting.
- **Modes beyond the classic glade** — Lightfall (`f01_lightfall`), Groovekeeper
  (`k01_grovekeeper`), the Hollow (`h01_emberfall`) and Budburst (`b01_thicket`).
  See *Modes* below. Lightweave and Ripplewake are retired; `weave` and `ripple` are spent
  mode ids.
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).
- **Verifying** — `Tools/verify/` in the repo (see the *Verifying* section).

### Content shipped

| Chapter | Mode | Levels | Par range | `budgetFactor` | Subject |
|---|---|---|---|---|---|
| `c01_shallows` | glade | 10 | 10–50 | none, then default | the verb, then colour, blending, rooted stone, brittle stone, taproots, pockets of colour |
| `c02_millvale` | glade | 10 | 41–63 | default 1.60 | the crossing |
| `c03_amberwood` | glade | 10 | 44–70 | default 1.60 | colour as the subject; no new rule |
| `c04_nightbriar` | glade | 10 | 44–69 | default 1.60 | the briar |
| `f01_lightfall` | fall | 10 | 2–6 drops | none, then default 1.60 (motes) | the cook, then the chain; motes 3 → 30, headroom 4 → 2, `ways` never above 8 |
| `k01_grovekeeper` | keeper | 10 | 2–8 tiles | none, then par + 5 (tiles) | the inversion, then stone, the heartbed and the prism; beds 2 → 4, `ways` 2 → 2 with a 1 at the fifth |
| `h01_emberfall` | hollow | 10 | 1–2 sparks | — | ladder is *how few openings win*: 7,8,6,4,2,3,4,1,4,1 |
| `b01_thicket` | bud | 10 | 3 taps | par + 5 (taps) | every grove *living* (invariant 20l); 5x5 → 8x7, flowers 22 → 49, critters 3 → 12, opening tap 3 waves → 8 |

**No level authors a difficulty number except the first glade in the game, and no chapter authors
a clock** (invariant 22). Par is derived from the board; both star lines and the losing line are
multiples of it — 1.20, 1.40 and 1.60, which are even thirds of the slack. Glade one turns the budget off entirely: nine tiles and
three critters, and a lost heart in the first minute is the most expensive heart in the game. A
per-chapter budget ramp was tried and removed — the budget is a fail line, and difficulty is the
boards' job (invariant 5d).

Par is **never** monotonic within a chapter — par is length, not difficulty, and ten rising
numbers read as a treadmill. A chapter's dip is its taproot board (one tap moves several
conduits and par charges once): the Shallows dips at glades five and nine, the Amberwood at
`c03_rootbound` and the Nightbriar at `c04_rootbriar`. Mill Vale's dip used to be
`c02_braided_water` and is not any more, which is invariant 5g's lesson — that dip was the
board being dealt partly solved rather than a root charging once, and par is now roughly
1.2–1.35× a board's turnable tile count on every glade in the game.

**The first glade in the game cannot be lost, and that is the one place `budgetFactor: -1`
is used.** Both star lines are derived from par and neither is authorable — three stars is
`par × 1.35` turns, two is `par × 2.00` — so the only lever content has over whether a board
can be *lost* is its budget, which is what the DTO documents a negative for. Everything after
glade one carries one, loosened through the Shallows because it is teaching. **None of this
moves the economy**: earned credits are derived from the star ledger, and the star lines are
held against par rather than against the budget, so tightening a budget cannot deflate a
reward.

Chapter art is generated: `Tools/chapters/*.py` regenerate the shipped JSON and self-check
against it; `Tools/make_chapter_art.py` reads names and colours out of the chapter's own JSON
and **scales a source to whole strips** rather than stretching to them, which is what decides
a chapter's strip count (Shallows 6, Mill Vale 4, Amberwood 5, Nightbriar 6).

**A board backdrop is graded in daylight, and the board is what makes that safe.** Every one of
the forty-one shipped backdrops used to arrive at a mean luminance between 28 and 105 out of
255 — a dusk grade, then a top-down wash to 42%, then a vignette pulling the corners toward a
deepened slate, and then, on the phone, a dark shade and a second vignette on top of that. The
result was reported exactly as it was: every level of every mode is dark. Nothing was wrong with
any one of those numbers and the picture they made together was never looked at.
<br>The reason it can simply be reversed is that **the backdrop is not what a tile is read
against**. Every mode draws its board on an opaque plate — `Pal.BoardTheme.From` at .87 alpha for
a glade, an `Art.Round` plate at .70 to .78 for the other four — so brightening what is behind it
*widens* the separation rather than closing it, which is the opposite of the usual worry.
The two
screens gave up their shade entirely and most of their vignette with it — a dark wash over a
bright picture does not make a calm bright picture, it makes a dull one.
<br>**Brightening it was only half, and the half that was left is why the game still read as one
colour.** The first answer, `daylight`, re-lit the slate in HSV and carried the accent through the
mid — correct about brightness and still a **duotone**: the source was reduced to luminance and
mapped back through a slate-to-accent ramp, so every pixel of every backdrop held exactly one hue.
Most authored accents here are gold (`#FFC93C`, `#FFC24A`, `#FFD75E`), so most boards in the game
were a painting seen through an amber gel, and it was reported as one — *is there a yellow overlay
on the background?* There was not. Nothing in `Scenery` tints anything; the colour was being
destroyed in the art tool before a screen could show it.
<br>**The second answer was rejected and then asked for, and the difference between those two
moments is the whole lesson.** It kept the picture's own hues but rotated them all onto the level's
accent — and shown unasked, over art the owner liked, it was correctly read as another tint: *why
did you change the background itself?* The tint came out, the art arrived in its own colours, and
*then* the ask was made — make them pink, purple, various cheerful colours. Same code, opposite
verdict. **A recolouring is a tint when it is substituted for the change that was asked for, and a
feature when it is the change that was asked for.** Nothing about the pixels decides which.
<br>So `vivid` turns a backdrop onto its level's accent, and the spread is free because it is
already authored: the forty glades carry gold, blue, hot pink, periwinkle, teal, mint, rose,
violet, coral and orchid between them, so the colours come out of the chapter files with no second
place to edit (invariant 5a's argument, for a picture). Three rules make it keep the painting
rather than replace it, and each was arrived at by getting it wrong first:
<br>— **A constant offset, never a pull toward a target.** Rotating each hue a *fraction* of the
way to one destination collapses the variety toward that destination: a brown trunk and a blue sky
both come out amber-ish, which is the duotone arriving by a slower road.
<br>— **Saturation is a multiply, with no floor and no cap.** A floor lifts the least saturated
pixels most, so a white cloud is pushed up to meet its blue sky and the clouds stop reading; a cap
pulls the sky down to meet the clouds and does the same from the other side. Both flatten exactly
the contrast the picture was bought for. The gain is chosen per source to reach
`SATURATION_TARGET`, because the packs are not equally colourful.
<br>— **`CLOUD_BLEACH` moves the pale end down, which is the same gap opened from the other
side.** A gamma on saturation above 1 pulls the least saturated pixels hardest and leaves the most
saturated nearly alone, so a sky's clouds whiten and its blue does not. It is the exact inverse of
the floor above and is safe for the reason the floor is not: it is a curve rather than a clamp, so
nothing is collapsed onto one value and a cloud stays as much paler than its sky as the pack
painted it, only more so. It is applied **before** `saturation_gain` measures the picture, so the
gain scales the whole thing back to target and bleaching the clouds costs the backdrop none of its
overall colour — the two work against each other on the mean and together on the gap.
<br>— **The brightness lift runs in V alone, never on the three RGB channels.** A per-channel gamma
raises the *smallest* channel proportionally most, so it lifts a colour toward grey as a side
effect of lifting it toward light. That is what turned `c04_nightbriar`'s dungeon interior (mean V
.25) into pale mud, which reads *dimmer* than the cave did because it lost its colour as well as
its contrast. Lifting V leaves hue and saturation untouched, so the same cave comes up a real
purple — and it is why `BG_FLOOR_LUMA` can be 150, where the RGB version had to be held at 112.
<br>**What a chapter still shares is its source's *picture*, and only its colour varies.** Nine of
the Shallows' backdrops are one sky in nine colours; the fix for wanting nine skies is more sources
in `chapter_art.tsv`, and it is a decision about art rather than about this code.
<br>**And removing the tint uncovered a bug the tint had been hiding for as long as it existed.**
Three of the eight sources named in `chapter_art.tsv` are *overlay layers* out of layered packs —
71% and 75% transparent — opened with `Image.open(...).convert("RGB")`, which drops alpha and
leaves whatever RGB sat under it. Most of two chapters was undefined white paper with a few
branches on it. The duotone could not show it because it threw the colour away and remapped
luminance, so a hole came out as a perfectly plausible ramp value; every gate in the repository was
green, because none of them opens a PNG. `make_chapter_art.opened` composites instead of
flattening, a source may now be a `+`-joined **stack** of layers (what a layered pack is for, and
what `Scenery.Layered` already does with the same art at runtime), and it **errors** on a stack
whose bottom layer is not opaque. The general lesson is the one this file already records twice for
art: a bad *cut* is a statistic and a bad *source* is a judgement — look at a source at the size the
game draws it, and be suspicious of a grade that would look fine either way.
<br>What is still dark is the **board plate**, deliberately and separately: the tiles, motes and
flowers drawn on it are bright saturated shapes, so their ground is what the backdrop is now free
*not* to be. Anything tempted to lighten it is changing a contrast ratio in five modes at once and
should be judged against real boards rather than against this paragraph.
<br>What pays for the readouts is `ModeScreen.ShadeDrop`: the header's own `FadeUp` now reaches
the band the numbers sit in, because they are the one thing on that screen drawn as bare text
with no pill under it. It is derived from `ReadoutsY` rather than typed, for `PanelStack`'s
reason.

### The board's vocabulary

One verb — turn a conduit, light a critter — with modifiers, and no second solver:

- `~` **brittle stone** — survives a fixed number of turns. Belongs on a tile the player
  cannot simply try, so in practice a crossing.
- `!` **rooted** — cannot be turned. Authored at `/0` (invariant 5c).
- `&A` **taproot** — every conduit carrying the rune turns as one; charged once in par.
- a **pocket** is not a tile — it is the shape that replaced the duskcap (invariant 5f): a
  heart and a critter of another colour behind a ford, where the ford sits on a *cycle* of
  the live network so the wrong turn costs the grove nothing and the pocket everything.
- `=NS+EW` **crossing** — two strands through one tile that never meet. Straight is inert;
  twisted is worth exactly one tap. No hub disc.
- `%NS+EW` **briar** — four arms drawn, two conducting; one tap swaps which. Order of the
  pairs matters (unlike a crossing). Straight is worth one tap, twisted four.

`Tools/verify/difficulty.py` is the instrument that says whether any of that is doing work —
see invariant 5d. `hazards` is the metric it replaced and is wrong; `arms`/`wins`/`glance`/
`colour` are the ones to author against. It names a chapter that is not glades as skipped
rather than stopping on it, which it did not always do — see *Verifying*.

### Modes

**Classic glade** — `PlayScreen`. Turn conduits, light every critter. The move budget is the
only fail state (invariant 22) at `par × 1.60`, and `Undo` refunds a move, so exploring a
crossing that reads the same half a turn round costs nothing.

**The solve is five beats, and the choreography is the board's own shape.** A **hush** — the
grove draws in and dims, the only moment in the mode where it gets quieter, and the beat most
often left out because it looks like nothing happening; without it the celebration begins while
the player is still reading their own last move. Then the **surge**: the light walks out from the
heart-crystals along `Puzzle.Depth`, one ring at a time, each lit arm flooding white and swelling
before settling back into its colour. A critter **flinches where it stands** as the wave reaches
it — a squash, a shiver, a ring in its own colour, sparks — so the order the grove comes alive in
is the order the player's own wiring feeds it. Then the **bloom** — every critter **leaps at
once**, every conduit goes white, two rings cross the grove over the top of it and a fan of light
turns behind it. Then it **settles** before the panel covers it.

**The jump belongs to the bloom and nowhere else.** The wake used to leap too, on the reasoning
that the surge teaches a leap to mean "this critter is awake" so the finale would be that sentence
said by the whole grove at once. Reported from play as the critters doing *two different jumps*,
which is what it was: repeating a gesture a second later does not reinforce it, it spends it —
the confetti-on-the-board-and-again-on-the-panel fault, one mode over. Two moments now get two
gestures, and the surge lost nothing, because what made that beat land was never the height. It
was arriving on the beat the light did.

What that replaced was one beat: every tile brightening at a delay proportional to its depth,
which is a *sweep*, and a sweep could be played over any grid at all. Walking the network shows
the player the thing they just built — two people who finish the same glade differently get
visibly different celebrations, and nothing else in the mode can say that. **No confetti and no
haptic**, unchanged and by request; what carries it is light, which is what the mode is about.

**Every duration is `GladeFanfare` (Domain), because the length is a function of the board.** A
fifteen-ring grove has more network to walk than a four-ring one, so this is exactly the shape
that becomes a wait without a bound — and a bound written as a constant beside the paint is a
bound nothing can check. The rate gives way (`SurgeCeiling` 1.35s) and a floor stops it becoming
a blur (`MinRing` 0.05s); where they meet the floor wins, for `BudTempo`'s reason. The deepest
shipped glade is **15 rings** (`c02_stonebridge`), so a real board runs 2.7–3.5s and
`GladeFanfareTests` asserts the whole thing stays inside `Longest` out to 32.

**A won glade says so twice, and the first one is what protects the heart.** `BoardView.OnWon`
fires the instant the model settles; `OnSolved` fires when the celebration ends. A run is written
down as owed until the screen resolves it, and the screen used to resolve on the second — so for
the whole celebration a solved glade was recorded as a run in progress: a process killed there
charged a heart at the next launch, and backing out forfeited a board the player had beaten. Both
were live before this and both got worse when the sequence grew, and the fix is not a shorter
celebration — it is closing the window where the outcome is *known* rather than where it is
announced. `PlayScreen._finished` moves with it (so every control is dead and `RunOver` is true),
and `_awarded` is the second field that keeps the payout exactly once: `Finish` used to guard on
`_finished`, so moving that flag earlier without splitting it would have made the entire payout
unreachable — a solved glade with no stars, no credits and no panel.

**Lightfall** (`FallScreen`) — a well of coloured motes that has to be emptied, and an ordered
procession to empty it with. Tap a column: the mote either **enriches** the top of that column
(a colour it lacks, and the stack does not grow) or **heightens** it (a colour it already holds).
A mote holding all three channels **bursts**, and washes the colour that finished it into the
motes beside it — so any of them thereby completed bursts in turn, and one well-chosen drop runs
through a whole connected blob. It reaches a mote buried at the bottom of a column that no drop
could ever land on, which is what makes a full well solvable at all.

Two fail states, both visible: the supply runs out, or a mote comes to rest above the **brim**
(row nought, drawn with a hard line under it). Only the first may be sold a continue — see
invariant 26b. Par is the fewest drops that empty a well without ever breaching the brim, found
by search (`FallSolver`) and resolved lazily, so a level authors a board and a procession and no
difficulty number at all. Boards are searched for, not typed — `Tools/chapters/f01_lightfall.py`
and `Tools/verify/fall.py`.

**Groovekeeper** (`KeeperScreen`) — a groove of bare ground, a handful of **sprigs** already
standing on it, and an ordered basket of coloured tiles. A tile is laid on bare ground beside
something already standing, and the rule is the inversion: a seam between two *unlike* colours is
worth something and a seam between two of the same is worth nothing. A tile whose own colour and
its neighbours' between them carry all three **blooms**.

The goal is the **beds** — cells the author marked, each of which has to end up holding a bloomed
tile. A planting is read against the cell it lands on *and* the four beside it, so one tile can
open five at once (`KeeperFlourish.Most`), and that is also the shortest way to finish: par
rewards exactly the play that looks best. Par is the fewest **tiles spent** — planted or
composted — found by search (`KeeperSolver`) and never authored.

Its vocabulary is four characters and they are the whole file: `.` bare ground, `#` **stone**
(nothing grows on it and no light passes through), `*` a **bed**, `r`/`g`/`b` a **heartbed** that
takes one colour and refuses every other outright, and `R`/`G`/`B` a **sprig**. The basket is
written in `R`, `G`, `B` and `P` for a **prism**, the one tile carrying all three at once — it
blooms wherever it lands and opens any bed. **Composting** spends the tile in hand without
planting it, to bring the next colour round; it costs a tile like any other, which is what stops
it being a free re-deal.

Two fail states: the basket runs out, or the groove has **nowhere left to grow**. Only the first
may be sold a continue (invariant 28c). There is no undo — the procession is visible and the ring
under a thumb says what a cell would open before it is committed, so a wrong tile is a
misjudgement rather than a surprise. Boards are authored and searched, not generated —
`Tools/chapters/k01_grovekeeper.py` and `Tools/verify/keeper.py`.

**Hollow** (`HollowScreen`) — a field of sleeping critters and a short *ordered* queue of
sparks. Light accumulates and never decays, so a player can never be stuck, the only endings
are winning and running out, and unlimited undo is safe. Par is the fewest sparks that finish
the board, found by search (`HollowSolver`), never authored. Boards are searched for, not
typed — `Tools/hollow/`.

**Budburst** (`BudScreen`) — a grove of **coloured flowers** with critters shut in **cocoons**,
and a basket of pure colour dealt one per tap. Tap a flower and the colour in hand **mixes** into
it — red with green in hand becomes yellow — and any bunch of **three or more touching flowers of
one colour bursts**, washing its colour into every flower it touches. Which makes more bunches.
Which makes more. A cocoon beside any burst takes a crack, and one out of cracks opens. Free every
critter before the taps run out.

**A grove is *living*, which is four rules gated on one field** (`regrow`, the strip new
flowers grow from — see invariant 20l). It **falls and grows**: what bursts leaves a hole,
everything above slides into it, and once the chain has stopped the holes fill, so the board
never thins out. Its **white flowers are bombs**: white can never be mixed into, so it used to be
a dead cell and a mistake — tapping it now clears the square around it. And **one flower ripens
between taps**, always beside somebody still shut in. A grove with no strip is *still* and does
none of it, which is how the mode shipped and is what keeps eight vector cases pinning the base
rule on its own.

**And the board says which taps pop.** Every flower a tap would set something off on breathes,
gently. That is the single change that took the arithmetic out of the mode, and it was reported
into existence: three tunings toward "chill" all missed, because what was hard was never the
boards — it was that the match was invisible until you made it. The choice is untouched; the sum
in front of it is gone.

**The mix is the whole design decision, and it is why this mode is chill rather than clever.**
Mixing only ever *adds* channels, so every tap drives the board toward white and toward a burst:
the grove wants to go off, and the player is only choosing where. There is nothing to work
backwards from, no state to hold in your head, and the answer to "what will this do" is on the
board in front of you. It is also the arithmetic four chapters of glades already taught — red and
green make yellow — reused as a verb rather than re-explained.

**The level is a grid and a basket, and no difficulty number.** Par is the fewest taps that free
every critter, found by `BudSolver` and resolved lazily (invariant 26d), and both star lines and
the tap budget derive from it. A blend is never dealt: the basket is pure `R`/`G`/`B` only,
because a blend handed over is the one decision the mode has in it.

**The goal is the cocoons and not the flowers, and that choice is what makes it affordable.**
"Clear every flower" was tried first: branching is the flower count, so a six-by-six cost
ninety-five thousand positions and often could not be proved at all. The cocoons are a far smaller
target reached by the same chains — measured on the same boards it is a few thousand positions —
and it is also the more forgiving goal, which is the point of the mode.

**A board must be authored settled** (`BudValidator.Settled`, and `content.py`): a grove already
holding three alike touching bursts in the first frame, so the player is shown a chain they did
not cause and par is measured against a position they never met.

Two fail states: the taps run out, or **no tap is legal any more**. Only the first may be sold a
continue (invariant 23), because nothing in a grove ever grows a flower back. The second is
subtler than it looks and cost a bug: white holds every channel, so a white flower can never be
mixed into — a grove of nothing but white has flowers on it and no move in it, which is a board
that can be neither won nor ended (invariant 20g). `BudBoard.AnyMove` asks the right question, over
the *whole* basket rather than the colour in hand, and `AnyFlower` is what got it wrong.

**The view exists to make the chain visible as it travels.** A bunch going off is not a flash and
a jump to the end state — it winds up, bursts, and a beat later the flowers around it flare and
visibly *turn* to the blend they have become. So a five-wave chain is five legible steps crossing
the grove, the count climbs while it is still running, and the pitch and the shake climb with it.
`BudBoard` reports every burst *and every wash* with the wave it belonged to, so the view is
replaying facts rather than keeping a second copy of the rule (invariant 9a).

**And "which wave was this" only ever comes from the pulses, never from the board.** `BudRun.Tap`
settles the *entire* chain before a single frame is drawn, so `Run.Board` is the board as it will
be at the *end*: it carries no time at all. A bolt of lightning used to be drawn between the
bursting cell and each flower it washed, and it picked its source by asking the board which
neighbour was `Bare` — which answers "empty once this is all over", so it fired from flowers that
had not gone off yet and, worse, from cells that were bare ground in the authored layout and never
held a flower. It was reported from play as *"random electric effects at positions unrelated to
where the flowers are rotating"*, which is precisely what it was. The bolt is gone; what remains
is anchored on the cell it belongs to, which is why nothing else in the file could have been wrong
about a position.

**The whole effect set is generated, and it took three attempts to get there.** `Art.Glow`,
`Art.Wave`, `Art.Glint`, `Art.Bloom` and `Art.Crystal` — no addresses, no bundle, no preload, and
every shape a coverage mask that takes the exact colour of the flower that went off. (`Art.Flash`,
`Art.Leaf` and `Art.Rays` were in this list and are not any more — see *more layers is not more
quality* below.) Two cuts from a licensed VFX pack were shipped and thrown away first: the
first took the pack's *shader utility maps* by mistake (a colour ramp drawn as a flare, a bubble
mask as a lightning bolt, a noise field as a shockwave — all of which loaded, addressed, audited
and drew), and the second was a correct cut of a real fire flipbook that still came back from play
as *"smoke/dust"*, because a plume authored for a rocket exhaust is a **volume** and a cell on a
phone wants a **silhouette**. Both attempts are recorded in the explosions block of `Art`, and the
general lesson is there: a pack built for world-space particles is the wrong shelf for a puzzle
grid, however good the pack is.

A freed critter is the real thing

too — the same five flipbooks the glades and the roster use, so what pops out of a cocoon is
somebody the player already knows.

**A wave is a wind-up and a burst, and the wind-up is the half that was missing.** The first cut
went straight from "nothing" to "gone", and it came back from play as *"it happens too fast"* —
which was true, and was not the whole fault. There was no instant in which the player could see
*which flowers had matched*, and that is the thing they had just done. So a bunch now **spins in
place first**, accelerating, swelling and brightening for about a quarter of a second, and only
then goes off. `BudTempo.Charge`/`Burn` split a wave between the two and both are proved to fit
inside it. The player's own tap gets the same treatment — a full turn and the game's `enter`
note, the one it already plays when somebody commits to a level from the map — because the tap
had no moment of its own, so the one thing the player *did* was the one thing with no animation
against it.

**A chain escalates in amplitude, never in duration, and that is forced rather than chosen.** The
obvious way to make a deep chain feel bigger is to give its later waves more time, and it is not
available: `BudTempo.Wave` divides `Ceiling` across the whole chain, so every wave of a nine-wave
cascade is *shorter* than the single wave of an ordinary tap. Lengthening the late ones either
breaks the ceiling — the nine-second freeze it exists to prevent — or steals from the early ones,
which is a chain that starts blurred and ends legible, exactly backwards. So what grows is how far
a flower **travels**: `BudTempo.Swell` takes each wave's wind-up from 1.62 to a capped 2.20, and
`WindSpin` from 420° to 760°, in the same time or less. That reads as accelerating rather than as
dragging, and it is the only axis the ceiling leaves open.

**A peak reached on the last frame is a flash, not a size — and that is why the first cut of this
was invisible.** It shipped as one accelerating curve (`v²`) running to the burst, which sounds
right and is wrong: an accelerating curve is near its destination only at the very end, so a
flower was within 5% of its peak for **3% of the beat, about 1.6 frames at 60fps** — *less* dwell
than the flat curve it replaced. Raising the swell against that changes a number nobody can see,
and it was reported from play as no change at all, on a build genuinely running it. So the flower
now **arrives early and holds**: out-quad out to full size by `Peak` (.66), then motionless until
it goes off, which is 44% of the wind-up at peak instead of 8%. The generalisable form: when a
gesture is not landing, measure how long it is *legible* before touching how big it is.
`BudMotionTests.AFlowerHoldsItsFullSizeLongEnoughToBeSeen` holds a line under the dwell rather than
under the curve shape, because dwell is what was actually wrong.

**And it gathers before it grows, which is the difference between "about to explode" and "getting
bigger".** A shape that only ever rises is being inflated by something outside it; one that crouches
first is doing it on purpose. `WindScale` is three phases — a quick out-quad dip to `1 - Recoil`,
the spring out, then the hold — in Domain for `GladeFanfare.Hop`'s reason. The crouch is
deliberately **constant** while the swell escalates: it is the *tell*, so it has to mean the same
thing on the ninth wave as on the first, and exactly one thing should be growing. `WindWhite`
follows the same phases, holding the flower at `Matched` (.62 toward white) while it is still
growing — the charge's job is to say *which* flowers matched, and a bunch that goes white has
stopped saying it — and spending the rest to `Critical` during the hold, where it is free to.

**A ladder has to be spent on the waves the mode actually reaches.** `b01_thicket` is one board
whose best opening tap runs three waves and most taps run one or two, so the first ladder — nine
waves from 1.46 to 1.82 — put almost all of its range past anything a player sees, and moved wave
one by 9%. It is front-loaded now: +0.22 a wave, so waves one to three span 1.62 → 2.06.
`TheLadderIsSpentOnTheWavesTheShippedBoardReaches` is the guard, and it is worth keeping when the
mode grows past one chapter.

**The chain is also said at grove scale, because a 2% nudge is not.** A wave's answer from the
board was a shake plus a punch on the plate of 1.2%–3.6%, which is under the size at which a scale
change on a whole screen is noticed — a player watching thirteen flowers go off has no attention
spare for it. `BudTempo.Heave` climbs 2.2% → 8.5% and punches `_grid` rather than `_plate`, so the
flowers move with the ground they stand on; a plate swelling behind a static grid is a border
thickening, not a board reacting. It is safe beside the shake because that borrows the *position*
and this the *scale* — the same pairing rule two sections up.

Two things the bigger swell broke that the small one hid, and both are the same fault. The burst
used to snap the cell back to square before throwing the flower, which at 1.34 was a twitch and at
1.82 is the flower visibly **deflating on the frame it explodes**; the wind-up's size and angle are
now handed to `ThrowFlower` so the growth carries through. And `Turn`'s punch on a washed flower is
still running when the next wave winds that flower up — two tweens on one value, so the punch
borrowed a mid-wind-up scale and handed *that* back as rest, leaving it permanently oversized.
`Wind` kills the punch channel first. A flower is also lifted over its neighbours while it is
bigger than its own square and put back by `RestoreDepth` in one ascending pass, never by
remembering an index — `SetSiblingIndex` inserts (`GroveFieldView`'s lesson), and a cell's glow is
half again as wide as its square, so stacking order is visible on a settled board too.

**A burst is a silhouette event, never a volume one, and that is what the smoke taught.** A real
fire flipbook was cut from the pack and drawn over every burst; it came back as *"when I burst
buds a smoke/dust comes out — what is that?"*, which is an exactly correct reading of a plume
authored for the scale of a rocket exhaust, shrunk onto a 170-point cell and drawn thirteen times
in one wave. What replaced it was the flower coming apart into six `Art.Leaf` petals under a
hard `Art.Flash` star, inside a ring, with an `Art.Rays` starburst for an edge, glints, embers and
a prism ring on top — and **every one of those has now been deleted too**, for a reason worth more
than the one that removed the smoke.

**More layers is not more quality, and this file taught me that the expensive way.** Asked for
*"a carnival of animations, stunning, like real mobile games"*, the answer here was to keep
**adding kinds of thing**: petals, rays, embers, a backlight, fireworks, confetti, a prism ring. It
came back as *"I don't want a meshed up random animation"*, and that is the correct reading of what
was built — the burst had become eight things going off at once, none of which owned the moment.
A premium burst in this genre is **four gestures done properly**: the piece itself popping out with
a squash, a hot round core, a wide soft bloom in the piece's colour, one clean expanding ring, and
a handful of round sparkles over it. That is what Royal Match draws, and the difference between it
and what was here is not effort or count — it is that every one of its shapes is **round and
soft-edged**, so the board never grows a hard line that was not already part of it.
<br>**Which is what "the spotlight effects are weird" was about, and it was a shape rather than a
motion.** `Art.Flash` draws a **twelve-pointed spiky star** and `Art.Rays` a starburst of straight
beams, so every burst fired a little searchlight and thirteen of them in a wave read as exactly
that. Both are gone from this mode: the core is `Art.Glow` at a high power (a bright centre with a
fast falloff) over the wide soft bloom, which is the two-layer light every game of this shape
draws, and a firework goes off as a round pop rather than as a star. The rule to keep, because it
generalises past this mode: **on a board of round soft shapes, anything with a straight edge in it
reads as lighting equipment rather than as light.**

**A freed critter is celebrated where it was earned and is gone from that spot, and it took four
goes to get there — every wrong one had the creature *travelling*.** It used to leap out and fade
to nothing
over the last third of its animation, so a grove where everybody had been freed was an empty field
— the thing the player spent the level earning was the one thing not on screen at the end. The
answer to that was to make it *stay*, standing on its own cell for the rest of the run, and that is
the version that shipped and was wrong for a reason nothing in the mode could see: **freeing empties
that square in the model.** It has to — the grove falls into the hole and that is where a chain gets
its compounding from — so a critter standing there is standing exactly where a flower is about to
come to rest. It was reported three times over, as critters falling, as flowers falling through
them, and as critters *turning*, and all three are one fault: the reward was being kept in the one
place the board is allowed to rearrange. (`Wind` turns a whole tile, so when the flower that landed
on that square later burst, the creature went round with the scenery.)
<br>The obvious fix — make the square a **post** the grove may not move — was built, mirrored into
`bud.py` and measured against the shipped ten, and it is recorded above because it must not be
tried again: it takes the cascades out of the boards. So the creature leaves instead, and **where
it went is the whole history of this animation**. It leapt out and fell back onto the square,
bouncing past it on an `OutBack` — reported as falling. It was made to rise out of the shell and
stand — better, and still travelling, and it still ended by **flying to the critters readout**,
which sits *under* the board (`BudBand`): so "it flies to where the score is kept" meant an arc
that rose a little and then crossed the whole height of the grove downward, and it was reported as
critters falling for the second time. The idea behind the flight was sound — a number that changes
on its own becomes somewhere the reward visibly *went* — and it is not worth that.
<br>So nothing moves. The shell breaks; the creature **appears where the cocoon was**, swells on an
`OutBack`, and is the only thing on the board that is moving while a ring closes **inward** onto it
(`BudView.Circle` — every other ring in this mode expands, which says *something went off here*;
closing says *this one*) and a soft glow swells behind it (`BudView.Shine`). It pumps
(`BudView.Pump`, one half-sine, no oscillation) and then fades from that spot, and the counter is
punched as it goes. **Only its scale is ever animated**, which is the rule the four attempts were
converging on: a size springing past itself is a pop and a position springing past itself is a
drop, and this mode may never draw the second. What says the square is free again is the grove
dropping a flower into it a beat later, which is better than a creature vacating it because the
player is watching the board rather than the number.
<br>Two things about it are load-bearing. The greeting lands **after** the shell's own noise has
finished — a cocoon opening used to draw a star, the shell whitening, six chips, two shockwaves,
sparks, three embers and a halo, and the creature arrived in the middle of all of it as a ninth
thing moving, which is why it was reported as *no emphasis at all* on a build drawing eight
separate effects; that list is now the shell, six chips, one shockwave, sparks and a halo. And the
leaving is **chained off the pump's own completion** rather than timed to match it, because a
finished pump restarts an idle breathe and a breathe borrows the scale the fade is writing — timed
alongside, the critter faded at full size with a breath still driving it, which is the
two-tweens-on-one-value fault in freshly written code.
<br>The finish changed with it: the grove used to end with the freed critters hopping one after
another, and there are none standing, so `Triumph` punches the readout instead —
`BudTempo.CheerAt` is retired with them, and `FlyToCount`, `FreedFlight` and `FreedLand` went with
the flight.

**Budburst's two sounds are its own slots, and both were picked by ear after the measurements had
narrowed the field.** `burst` rather than `pop`, because `pop` is a wooden clunk eight other
things are tuned around and this one is struck thirteen times in a wave and pitched up through a
chain — it has to be the shortest, brightest thing in the set, and nothing else should move when
it is retuned. `free` rather than the bell-and-chime pair that used to open a cocoon, which was
the loudest thing in the game fired up to four times inside one chain over thirteen bursts already
sounding; it is `menu`'s block of wood struck a fifth higher, which is the reuse `sfx.tsv`'s head
describes — heard clearly over a burst and gone before the next.

**Nothing is drawn between two cells, and the attempt to is worth knowing about.** A bolt of
lightning ran from the bursting cell to each flower it washed, and it chose its source by asking
`Run.Board` which neighbour was `Bare` — but the model settles the whole chain before a frame is
drawn, so that question answers "empty at the *end*", and the bolts fired out of flowers that had
not gone off yet and out of cells that were bare ground in the authored layout. Reported as
*"random electric effects at positions unrelated to where the flowers are rotating"*. What says
the same thing correctly is anchored on one cell: a small flare in the arriving colour, and the
flower whitening for a frame before it settles. **A wave is dealt as a ripple**, a few tens of
milliseconds between the flowers of one
wave, bounded by `BudTempo` to a fraction of the beat so the wave still ends on time: the board's
biggest tap is thirteen flowers, and drawn all in one frame that is one flat blink.

**The word at the end is the score, said out loud, and it gets its own arrival.** GREAT, AMAZING,
EPIC, LEGENDARY — a ladder every player can already order without being taught it, which the
grove's own vocabulary (LOVELY / WILD / GLORIOUS / WILDFIRE) could not be, because nobody knows
whether GLORIOUS beats WILD. It starts at **two** waves rather than three, so the loudest thing
the mode says is not reserved for a chain most taps never reach. It has **its own label in the
middle of the grove** rather than a caption swap on the running count at the top — the count is
information arriving while somebody is trying to watch the board, and the word is the payoff, so
it is allowed to cover flowers for a second and the count is not. It slams in from oversize past
its resting size, in its rung's colour, out of a bloom, over a ring, with sparks, a screen flash
and a punch on the whole grove; the top rung is the only thing in this mode that gets confetti.
Every rung gets all of it, only louder — a ladder that withholds the celebration below its top
rung teaches the player that most of what they do is not worth celebrating, which on a mode built
to be generous (invariant 20k) is the wrong lesson twice over.

**Every flower is the same four-petal shape, and white is the one exception.** It used to draw
one petal per channel — three for a pure colour, five for a blend, eight for white — as a second
reading for the roughly one man in twelve who cannot separate red from green. Reported from play
as clutter, and correctly: thirty-six flowers in three different silhouettes scattered through
each other is a *field of shapes*, and the thing the mode is actually about is the one thing that
then stops standing out. One silhouette makes the grove one material and leaves colour as the only
thing changing across it. **White keeps eight** because its difference is a rule rather than a
colour — it holds every channel, so it can never be mixed into again — and it is the only flower
that moves while nobody is tapping, so the two readings still agree on the one flower where it
matters. `BudFlower` is the single answer, asked by the grove, the band and the legend alike.

**The second reading moved to a legend above the grove, and that is `FallMixing`'s rule for the
second mode.** *Recall is not difficulty*: "the colour in hand mixes into the flower you tap" is
one sentence and the whole game, and a player mid-grove still has to remember that the pink ones
came from red and blue. So the board answers it — three recipes drawn as flowers, permanently, at
the top of the screen. `BudMixing` derives them from the same `|` on the same masks `BudBoard`
mixes with, so there is no table to fall out of step with the rule; `BudBand` owns where every
piece of one sits, because whether two things collide is arithmetic (invariant 8a) and it costs
the grove a strip of the shortest screen this game is drawn on, which is checked rather than
argued about. **Three recipes, not four**: a blend tapped with the colour it lacks makes white,
and the board says that better than a fourth chip would.

**And three cards, not one plate.** All nine flowers sat inside a single long box and it was
reported as visually confusing — which is exactly right: nine coloured shapes and four operators
inside one border are a row of thirteen things, and the eye has to find the groupings itself every
time it looks. Giving each statement its own edge does that work in the layout, so the legend is
*read* as three facts rather than parsed as one. The gap went up with it, because three cards
eight units apart read as one card with seams in it, which is the worst of both.

**The Thicket is ten groves and every one of them is par 3, which is arithmetic rather than
laziness.** The mode shipped as one board on purpose — two modes before it were built out to five
and ten levels and thrown away, and what decides a mode is whether the verb lands (invariant 20j).
It landed, so the chapter was filled out — twice, because the first fill was not chill enough
and the reason was not the boards (invariant 20l). Filling it out found the ceiling described in
invariant 26d. Cost goes as the flower count to the power of par, so a par-4 grove big enough to
cascade is refused by `BudValidator`'s node ceiling, and a par-4 grove small enough to prove comes
back at twenty flowers with a **one-wave** best tap — a board that validates perfectly with the
mode taken out of it. So par stays where the cascades are and **the ramp is one dial**: how many
are shut in — **3, 4, 5, 6, 6, 6, 7, 8, 9, 12** — and with it how much grove there is (a 5x5 with
22 flowers up to an 8x7 with 49). Every grove is dealt `par + 5`, which is eight taps for a
three-tap answer, and it stays eight on the twelfth-cocoon finale: freeing twelve critters with
the same allowance is more to do than freeing three **without ever being tighter**, which is the
only kind of harder this mode is allowed to get (invariant 20k). A careless player scores three
stars on all ten.

**The first grove is twenty-two flowers in four colours with three critters in it**, and that is
the other half of what was wrong: the old opening board was thirty-six flowers in five colours,
which is a wall of stuff to meet a mode in. It is small enough to read at a glance and its one
marked tap already runs three waves.

**Two dials were tried and thrown away, and both for the same reason.** `spare` came down from
five to three across the chapter, and `greedy` — whether a thoughtless run still scored three
stars — was true early and false late. Both are ramps built out of *withholding*, on a mode
commissioned to be generous; worse, ramping `greedy` forced the board sweep toward layouts whose
biggest chain is a trap, which is exactly backwards. **Old wood went with them**, and it is the
clearest case: a barrier is the one object in this mode that can only ever make a chain
*shorter*, so it was authored across most of the chapter for one drop and taken out again. `#`
still parses, because the character is shared vocabulary with Groovekeeper, and `BudValidator`
warns on a grove that stands any. `bud_wood` is a **spent lesson id**.

**The loudest thing in it is the finale**, whose opening tap runs **eight waves, bursts
twenty-seven flowers and frees ten of its twelve critters at once** — and the sweep held out for a
cascade on every rung, because a grove whose best play is three separate one-wave taps passes
every other check in this repository with the mode taken out of it. Only the *fill* was searched:
every layout is drawn by hand and what was hunted is which colour stands where, which basket the
grove is dealt and **which strip it grows from**, which is `b01_firstburst`'s bargain kept for the
other nine (`Tools/chapters/b01_thicket.py`).

**Par 3 rather than par 2, and the reason generalises.** The layout's first basket gave par 2, and
at par 2 both star lines round onto 3 — `ceil(2 × 1.20)` and `ceil(2 × 1.40)` are the same integer
— so the two-star band is empty and a careless player drops straight to one star.
`CheckStarBands` reads the *factors* rather than the thresholds and so says nothing about it,
deliberately (it would be a complaint about board size). On a mode whose pars are this short, that
is a check nobody has: **look at the derived lines, not the factors.**

Boards are composed and searched, not generated — the layout is fixed by hand and only the
*basket* is hunted, which is the cheap half of the search and the half that decides how a board
plays. `Tools/chapters/b01_thicket.py` and `Tools/verify/bud.py`. `bud-vectors.json` is the
contract between the two copies of the mix-and-wash rule, and every case carries a play as well as
a par: two copies can agree exactly about how many taps a grove costs and still disagree about how
far the chain ran. **`BudLadderTests` is the half of that guard which runs offline** — the vector
fixture needs the Editor, so the rule drifted once and only the build gate noticed; see the
hard-won fact about a vector file only the Editor can read.

**A grove has a hint key, and it buys something a glade's does not.** Everywhere else a hint is
a way past a board that has stopped somebody; nothing here is meant to stop anybody, so what this
one sells is the **big** version of a move they could have found anyway — the marked flower, the
colour it would become floating over it, and a ripple across every cell the tap would reach, in
the order the chain would reach them. `BudHint` finds it: every opening tap that still finishes
the grove inside the taps that are left, and among those the one that goes off hardest, by the
same ranking `BudSolver.Careless` uses. Correct first — a hint that quietly costs somebody the
level is the one thing it must never be — and loud second, which on a mode with hundreds of
shortest plays is not a nicety. It is node-bounded and degrades to the biggest chain going rather
than stalling (60,000 positions, half what par is allowed; measured at 34ms on the shipped board).
The mark **points and does not play**: taking the tap for the player would spend a tap out of
their satchel on their behalf, which is the difference between a hint and a move. And the
empty-pool offer is raised when the mark *goes* rather than when it arrives, which is where this
differs from `PlayScreen` and has to — a glade's hint is consumed by its own reveal, where a
grove's leaves advice the player still has to act on, and a panel thrown up over it covers the one
thing they paid for.

**A chain escalates in _kinds_ of thing, and the version that escalated in amounts did not
work.** Every wave used to draw the same event — petals, a flash, a ring, sparks — with the
escalation carried entirely by numbers: a bigger swell, a harder shake, a brighter flash, a larger
ring. Played through seven levels that reads as **no change at all**, and it was reported in
exactly those words. A number going up is not something anybody sees; a thing that was not there
before is. So `BudSpectacle` switches a whole new kind of thing on at each wave and keeps the ones
before it:

* **wave 1** — the burst; the *rest of the grove jolting under it* — every other flower knocked,
  in order, outward from where the wave went off and harder the nearer it was — and a ring of the
  wave's **own colour** thrown right across the board, with the screen taking that colour rather
  than white, so what floods the screen says *which* colour is running;
* **wave 2** — **fireworks**: sparks arc up out of the grove and go off above it. The first thing
  in this mode that leaves the board, so it is unmistakable without comparing it to anything;
* **wave 3** — a **star lit behind the whole board**, the only layer drawn under the grove rather
  than over it;
* **wave 4** — confetti.

A four-wave chain is therefore six different events arriving one after another rather than the
same event four times a little louder. Two rules keep it honest and both are tested: **nothing is
ever taken away again** (a layer switching off would read as the chain running out of steam
exactly when it is running hardest), and every rung lands on a wave ordinary play actually
reaches — which is the mistake `BudTempo`'s first swell ladder made by spreading its range over
nine, and which **this ladder made too, in its first cut, at the one rung nobody thought to
check**. The bar it was written against was "the *second* wave is the commonest chain", so the
first new kind of thing arrived at wave two and a **one**-wave tap — which is most of what
happens in this mode — drew a burst and a jolt and nothing else. Every rung has moved down one.
`BudSpectacleTests` now holds a floor under what a single tap is worth as well as a ceiling on
where the ladder may start, because a ceiling alone cannot say that the commonest thing in the
mode is drawn as the quietest.

**The whole mode was played at roughly double speed, and one number was doing it.** Reported as
*"the animations happen too fast, and I don't like their style"*, and the two halves of that have
two different answers. The speed is `BudTempo.Ceiling`: **every** duration in this mode — the
wind-up, the ripple, the petals, the shockwaves, the wash, the fall, the jolt — is a fraction of
`Wave`, which is the ceiling divided by the chain, so one constant sets the pace of everything.
At 3.60s the finale's eight-wave tap dealt each wave .45s: a .18s wind-up, a .27s burst, petals on
screen for half a second and the entire grove falling in **.167s**, which is not a fall, it is a
teleport. Nothing was wrong with any single effect and not one of them had time to be seen. It is
8.00s now (`WaveFull` 1.10, `MinWave` .46), and the shape of the change is worth keeping: waves
one to *seven* now get the full beat where the old ceiling started compressing at five, so a chain
no longer accelerates away from the player exactly as it gets more worth watching. A one-wave tap
— most taps — runs 1.10s; the deepest chain the ladder distinguishes runs 8.00s and 9.60s with the
word after it, and `AndTheLongestChainStillEndsWhileAnyoneIsStillWatching` is the line under that.
The bound has not changed in kind: a chain must still end, the rate still gives way, and a
nine-wave cascade must still not be a nine-second freeze. It was simply set where a cascade could
not be watched, on **the one mode in this game commissioned to be generous** (invariant 20k).
<br>It was raised **twice**, and the second raise bought something the first did not. The first
was about whether a gesture could be *seen*; the second was about whether a wave could be dealt
**one flower at a time**, which needs room inside the burn for the ripple, the hold and the fall
all three. At a .55s burn there was none.

**A wave is dealt one thing at a time, and the ripple that does it was clumping.** `StaggerAt` was
`min(nth x step, most)` — so on a wave of thirteen the first four were dealt apart and the
remaining **nine landed on the cap, in the same frame**. The bigger the wave, the more of it went
off at once, which is precisely the flat flicker the stagger was added to break up, and nothing
caught it because the checks asked only that the ripple was *ordered* and that it *ended inside
its beat*, and both of those are true of a clump. It now shortens the **step** until the whole set
fits, so every flower of every wave is dealt at a distinct moment: thirteen go 36ms apart, three
go 109ms apart, and `EveryFlowerOfAWaveIsDealtAtItsOwnMoment` is the line under it.
<br>Two more things were dealt against the *burst* count and should never have been. A wave's
**washes** ran their index past the end of a ripple built for a smaller set, and its **cocoons**
were given no delay at all — so a tap freeing four critters fired four notes, four halos, four
shockwaves and four creatures on one frame, which is the single loudest moment in the mode played
as one chord. Each kind now ripples across its own count, and cocoons get a **wider** allowance
than flowers (`GreetSpread` .95 against `Spread` .62): thirteen flowers bursting is one gesture
said thirteen times and reads as a sweep, where four cocoons opening is four separate payoffs.

**And the grove is clipped to itself.** A flower that grew back enters from `Grown() x _cell` above
the cell it lands in — three, four, five cells above the top row — and it was drawn the whole way,
hanging over the board with nothing under it. A `RectMask2D` on a node the size of the board fixes
it (`_grid` is the whole screen below the band, so masking *that* clips nothing). The margins are
**not symmetric**, because what they are for is not: at the sides and below there is nothing to
hide, so they are generous and no gesture is ever cut there, and above the clip sits on
`BudView.PlateLip` — the 13 units the board's own plate stands out past the grid, which is what
the player reads as the edge of the grove.
<br>**That top margin was first set to the wind-up's overhang and that is the wrong trade.** A
flower swells to 2.20 and so reaches about .29 of a cell past its own square, and leaving room for
it meant leaving room for a falling flower too — which came back as *"I still see them coming out
of the grid slightly"*. A quarter of a cell is invisible when a flower is swelling in it and
perfectly visible when a flower is falling through it. The clip is tight now and what it costs is
about a tenth off the top of a top-row flower at the deepest wind-up: a moment, on one row,
against something that was happening on every fall.
<br>**Only the field is masked**: `_fx` and `_residents` are siblings of it, so rings, sparkles,
fireworks and freed critters all still cross the edge of the board, which they must, since leaving
the board is the whole of what makes the fireworks read as fireworks.

**Nothing in this grove is made of anything that shatters.** The white flower's detonation played
`shatter` — *DESTRUCTION Break Impact Wood*, the one genuinely destructive sample in the pack —
over a low `burst`, and it was reported exactly as it sounds: metallic, explosive, and nothing
like the rest of the game. A bunch of white played a **bell**, which is the one thing this board
must not sound like when every other voice in it is a struck block or a pop. Both are gone: the
white flower is the mode's own burst note struck twice, low and then a fifth above it, and a bunch
of white is `free`'s wooden pop an octave up. Bigger by being **lower and doubled** rather than by
being a different kind of sound — which is `sfx.tsv`'s whole argument, that a small palette of
materials is what makes a set sound like one place.

**A fall's duration is a fact about its height and nothing else, and it was a fact about its
column.** `Rainfall` took the piece's wait out of the wave's allowance and gave it whatever was
left — so the same drop, over the same distance, took up to **45% less time** depending only on
where in the ripple it sat, and a board whose pieces fall at several speeds at once does not read
as a board falling. Reported as the fall being sudden and stuttery, and it measures out exactly
like that: at the far end of the ripple a six-row drop was covering **81 pixels a frame**, most of
a cell. `FallOver` is asked first now and the ripple is trimmed into what is left, so a tall drop
rides at the front of the wave with no wait at all — which is right, it has the furthest to come —
and `ADropOfTheSameHeightTakesTheSameTimeWhereverItIsInTheRipple` asserts it as exact equality
rather than as a tolerance.
<br>**And the curve is gentler than gravity, which is a drawing decision rather than a physical
one.** `InQuad` is what a falling thing really does and it peaks at twice its own average speed;
`t^1.5` peaks at one and a half times. Together with the fix above that takes the worst drop on
the shipped boards from 81 pixels a frame to **33**, still unmistakably accelerating and never
fast enough to tear.

**The grove is allowed to land before the word arrives** (`BudTempo.Landing`). The celebration used
to begin while the last flowers were still in the air *and* every cell was repainted underneath it
in the same frame — reported as the board resetting suddenly. Two separate faults sat on that one
line. The wait was simply missing, and it is derived rather than chosen: it is the hold and the
fall of the wave that has just ended, so a shorter board settles sooner. And the repaint asked for
`animate: true`, which **skips `PaintCell`'s own "nothing changed" guard** — so a loop meant to
tidy up a few stale cells was killing every tween on all thirty-six, snapping every scale back to
one and taking every white flower's breath and every "this one pops" hint with it, for
`PaintPops` to start again from nothing a frame later. That is a whole board flinching at the
moment it should be settling. Asked without `animate`, a cell whose colour has not moved is left
exactly as it is.

**And the word is fitted to the screen rather than to the grove.** LEGENDARY ran off both edges,
and it was over before the slam was involved: measured against the game's own font, the top rung's
194 points draw it **1195px** wide on a canvas with 1024 to give, and AMAZING fitted at rest and
overflowed at 1.85. The label had been built as wide as the *grove* plus a margin, so on a
five-wide board the box was narrower than the phone, and the ladder hands out points by rung
without knowing how many letters the word has or what language it is in. The authored size is a
**ceiling** now, the real width comes off the font at run time, and — the part that decides how it
looks — **the resting size is fitted first and the slam takes what is left**. Shrinking the font
until the slam fits is the obvious reading and it is the wrong trade: it takes LEGENDARY to 102
points to buy an 85% overshoot nobody asked for, when the resting word is the thing being read.
Fitted this way it stays at 132 and slams at 1.26, while GREAT and EPIC keep the full 1.85 because
they are short enough to have it.

**And the grove is heard landing.** A board that falls in silence is being rearranged rather than
dropping things onto other things. The naive version is unlistenable and would not even play: a
deep wave drops twenty-odd pieces, which is twenty identical clunks in half a second *and* more
voices than `Audio.PlayOne`'s ten-voice pool holds, so the tail would be dropped by the mixer and
which pieces went quiet would depend on nothing the player can see. `BudChorus` is the rule —
at most five of a wave are struck, **spread evenly across it rather than taken off the front**
(the first five of twenty sounds like a five-piece wave followed by silence, so the bigger the
fall the earlier it appears to stop, which is exactly backwards), each on its own step of a
pentatonic run so no two can land a semitone apart. In Domain, because a rule that decides which
of twenty things is *skipped* is wrong for a year without anybody being able to say why the board
sounds thin.

**A freed critter rises and stays up; it never comes back down.** It was two beats — a leap that
threw them .62 of a cell into the air, then a settle that brought them back on an `OutBack`, so
they dropped *past* the square and bounced. That is a fall, it was reported as one, and nothing in
this mode should read as a creature being dropped least of all at the one moment the whole level
is for. It is one motion now and the position curve is **monotone by construction** (`OutCubic`
never overshoots, so there is no `t` at which they are moving downward): they ease up out of the
shell breaking in front of them and stop where they will stand. The **scale** still overshoots,
because a size springing past itself is a pop and a position springing past itself is a drop, and
only one of those was ever wanted — the leap was never buying anything the pop does not.

**And the bunch is wired together while it charges.** Three flowers spinning in three places on a

**The style was three curves, and the fall was the one that was actually wrong.** A falling piece
ran on `OutQuad` — it left fast and *decelerated* into the ground, which is the one shape a
falling thing cannot have — and it arrived at exactly its resting size, which is a thing that has
finished moving rather than a thing the ground has stopped. It now accelerates (`InQuad`), is
drawn out along the way it is travelling in proportion to how far it has to come, and **lands with
a squash and a spring** (`BudView.Squash`). That last one is the cheapest half-second in a board
game of this shape and is most of what separates a board with weight from a board whose contents
slide about. Two smaller ones went with it. A burst used to fade its flower out over .11s with a
uniform grow, so a wind-up that had spent a third of a second building came to nothing; it now
*tears free* — pulled long, released past its own size on an out-back, whipped round, alpha held
back so the last thing the eye keeps is the flower at its largest. And colour landing on a flower
was a `Tween.Punch`, which is a damped sine through three half-cycles: a thing that shivers has
been *disturbed*, where a thing that swells and settles has *become something*.

**And a burst is left alone for a beat before the grove falls into it** (`BudTempo.Settle`). The
fall used to be dealt in the same frame as the bursts it was falling into, so the player's own
doing was covered by the consequence of it before they had seen it. The hold is taken **out of**
the fall's allowance rather than added beside it — `Rainfall`'s lesson in the one place that had
not learned it, since a hold added to a duration that is already the whole of the wave is a grove
still falling when the next wave charges, which is two gestures on one transform and the fault
this view has paid for twice. `TheGroveIsBackOnTheGroundBeforeItsOwnWaveEnds` holds the pair
together, and writing it found a live bug: `Rain` was a fraction of the burn **with a floor under
it**, and a floor on a fraction of something can exceed the thing it is a fraction of.

**And the bunch is wired together while it charges.** Three flowers spinning in three places on a
grid of fifty is three things happening; a line of light between them is one thing about to
happen. It is drawn strictly from the pulses — two cells the model says burst *in this wave, in
the same bunch* — which is the only reason it is safe to draw a line between two cells at all
here: a bolt that asked `Run.Board` which neighbour was bare once fired out of blank soil, because
the model settles the entire chain before a frame is drawn.

**A burst also says how big it was.** Three alike is the rule being met and nine alike is a third
of the grove going at once, and both used to draw the same six petals and the same note.
`BudChain.Blast` grades a bunch at five and at eight; the rungs scale the petals, the rays, the
reach and the sparks off one number, add a **second ring chasing the first** (which reads as
*more* rather than as *bigger*), and drop the note lower as the bunch gets fatter — deliberately
the opposite of the chain's own ladder, which climbs, so a fat single bunch and a deep chain stay
two readings. A bunch of **white** gets the one shape nothing else draws, because white holds
every channel and is the only flower whose state the player can no longer change. And **every
tap** throws a ring of the colour it is *making*, because the commonest event in the mode is a tap
that sets off nothing at all and it was answered by a spin under the player's own thumb.

**And a cocoon that cracks and holds now says so.** It was drawn by nothing at all: a cocoon
taking the first of its two cracks changed one ring's alpha on the next repaint, so the most
encouraging thing that can happen short of freeing somebody arrived as a colour appearing quietly
behind thirteen flowers going off. `BudPulseKind.Crack` is the model saying it and `BudView.Crack`
is what it says — the shell jolts, splinters come off it, the ring flares — deliberately smaller
than freeing one in every dimension and pitched under it, so the two are one gesture at two
strengths.

**Freed critters stand in the grove and the grove still falls through their square, and that is
measured rather than preferred.** Reported twice as flowers falling into the slot a critter is
standing in. The obvious fix — a freed cocoon leaves a **post** the grove may not move (old wood's
behaviour with a creature drawn on it) — was built on a branch, mirrored into `bud.py` and run
against the shipped ten. It takes the mode out of the boards, because a chain compounds *because*
the grove falls into the hole a burst makes and a post permanently fragments its column:

| grove | as shipped (best opening tap) | with critters as posts |
|---|---|---|
| `b01_twiceknocked` | 5 waves, 16 flowers, 4 freed | **1 wave, 3 flowers, 1 freed** |
| `b01_dewfall` | 7 waves, 22 flowers, 5 freed | 2 waves, 9 flowers, 2 freed |
| `b01_everbloom` | 6 waves, 25 flowers, 7 freed | 3 waves, 12 flowers, 3 freed |
| `b01_thicketheart` | 8 waves, 27 flowers, 10 freed | 3 waves, 9 flowers, 4 freed |

`b01_sunspill` also collapses to par 2 with one winning play and `b01_thicketheart` stops being
provable inside `BudValidator`'s node ceiling, so two of the ten fail the build gate outright. That
half is a re-sweep; the cascade collapse is **not** — about 1,200 (basket, strip) pairs were swept
on `b01_sunspill`'s layout under the post rule and **not one** produced an opening tap of even three
waves. It is the rule rather than the fill, and its shape is the wrong way round for this mode: the
more critters a player frees, the worse their grove gets at cascading. If it is ever wanted anyway,
the layouts have to change with it — cocoons low in their columns, so a post sits near the floor and
the column above it can still compact — and that is ten hand-drawn boards, not a sweep.

**Two retired modes sit behind this one** and both are worth knowing about before designing a
third — see invariant 20j. Lightweave shipped three chapters and rejected almost nothing;
Ripplewake shipped five levels and could not be read. `weave` and `ripple` are spent mode ids.

Shared by every mode: `RunLedger` (record, chests, streak, reward, analytics — and it builds
the `RunOutcome` *before* folding the record in, because half of what it describes stops being
true after), `RunScreen` (defeat/pause/forfeit panels, and the continue offer that comes before
all three — invariant 23), `RunGuard` (a committed run is paid for however it ends),
`PlayRoute` (which screen opens a level), `RunWording` (turns vs sparks). `LevelsScreen.Open`
is the one place a mode decides its screen. A mode joins the continue by answering three
questions — `MeasuredIn`, `ContinueDeficit`, `ContinueWith` — and never gets at the price.

### The numbers

Free play collects about **593 credits and 6 gems a day**; `Tools/verify/content.py` and
`Validate Content` both derive and print this, so never hard-code it.

- **Companions** — 31, one free (`monarch`, the starter), 30 priced 800 → 30,000
  (~270,500 total). Unlock is keeper level **and** purchase.
- **Grove catalog** — 493,770 credits complete: 154,770 decor and homes, 68,500 land
  (9 regions, a free 6x6 starter), 270,500 residents. 150 priced pieces, of which 99 sell
  in bundles of ten at what one used to cost. Home ladder 5 rungs, first free.
- **Grove star ladder** — 10K / 20K / 50K / 100K / 200K, content in `homestead.json`.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted). A loss costs one. Two kinds of
  run cost nothing (invariant 24): the **first 3 levels of the first chapter of each mode**
  (`hearts.graceLevels`, content) and **any level the player has already finished**, which also
  means they are open, and free to leave, with no hearts left. The cap is per player: a **heart container** raises it to
  10, 20 or 50 permanently (invariant 18d), derived from `heartContainersOwned` and read by
  every screen through `Wallet.MaxHearts`.
- **Hints** — pool of 3 account-wide, one back every 8h, ceiling equals the cap (a granted
  hint at a full pool is refused, not clamped). A hint charges no moves. Spent in **two modes**
  and they buy different things: a glade's turns the conduit (`BoardView.Hint`), a grove's
  *marks a flower* and shows the cascade tapping it would set off (`BudHint`, `BudView.Hint`) —
  because nothing in Budburst is meant to stop anybody, so what a hint sells there is the big
  version of a move they could have found anyway. Neither costs the save file, the wire or the
  server anything: the pool, the clock, the ceiling and the video are account-wide and already
  existed, so the second mode joining them was one predicate and a key.
- **Streak** — a 7-night lap that wraps: 500 credits, 1 heart, 5 gems, 2 hearts, a 12h boost,
  3 hearts, 10 gems.
- **Ads** — four placements, all opt-in, no interstitials: `heart_refill` 2 hearts,
  `coin_bonus` 1,000 credits, `win_bonus` credits, `hint_refill` 1 hint. (`run_continue` is
  retired — invariant 22.) Daily caps 20/12/**6**/10 — the first, second and fourth are
  deliberately above what any network will fill, so they bind only as a lever that can be
  lowered; `win_bonus` is the exception and its six is a real bound, because the wheel more
  than doubled what one view of it pays. `AdRules.MaxDailyCap` 30 is a hard `const`.
- **Bonus wheel** — eight equal slices on `win_bonus`, at 100 / 200 / 150 / 300 / 100 / 250 /
  150 / 500 percent of its authored 200, so the rim reads 200 / 400 / 300 / 600 / 200 / 500 /
  300 / **1,000** and every slice is a 1-in-8 chance. Mean 218.75%, so a view really pays about
  **438** and a capped day about **2,628** — against 2,400 under the old flat 200 at a cap of
  twelve. Content (`ads.wheel`), and removing the block puts the flat offer back. Invariant 25.
- **Shop** — 16 products. Gems 100 → 8,500 for $0.99 → $49.99; coins 2,500 → 75,000 for
  $1.99 → $39.99; starter bundle a $2.99 non-consumable; 5-heart refill 50 gems, a day of
  fast hearts 30 gems. **Heart containers** `gg_heart_vessel_1/2/3` — non-consumables at
  $19.99 / $29.99 / $39.99 raising the refill cap to 10 / 20 / 50, on the supplies shelf
  under the gem-priced hearts, and the only real-money products that grant something other
  than currency (invariant 18d). The whole catalog is ~$236.
- **Stars** — turns and nothing else (invariant 22). Gold is `par × 1.20`, silver `par × 1.40`
  and the run ends at `par × 1.60`: even thirds of the slack between a perfect run and death,
  so all three bands are landable. Held against **par**, never against the budget. Move one and
  you move all three — `LevelValidator.CheckStarBands` proves the ordering. Every threshold is
  `ceil` of exact hundredths, never of a float product: `par × 1.20` at par 45 is 54, and the
  game said 55 on four glades until it was — see *Hard-won facts*.
- **Chapter gate** — the next chapter opens at **2 stars a level** of the one behind it, so
  20 of 30 on today's ten-level chapters. Content (`chapterGate`), per mode, and the first
  chapter of every mode is always open.
- **Heart rescue** — **20 gems** for **+2 hearts** on the defeat panel, when there is nothing
  left to play with (invariant 23a). The same gems-per-heart as the shop's smallest pack
  (50 for 5), which `Validate Content` and `content.py` both check against — never against the
  bulk pack, which is a volume discount every honest tuning is dearer than. It buys a *fresh*
  attempt, graded like any other. Content (`hearts.rescueGems` / `hearts.rescueHearts`), and
  `"rescueHearts": 0` withdraws it.
- **Continue** — **20 gems** for **+15 turns** on a glade, **+6 motes** on a well,
  **+6 tiles** on a groove or **+4 taps** on a grove,
  flat and repeatable for as long as the player can pay (invariant 23). About three days of
  free gems, or a fifth of the entry rung. The grant is *on top of* whatever it took to un-lose
  the board, and a bought run can only ever score one star. Content (`continueRun`), and
  `"enabled": 0` withdraws it.
- **Account prompts** — 2 chapter asks, 3 purchase asks, one shared 48h quiet period.

Everything in that list except the shop ladder is **content** in `progression.json` or
`homestead.json` and retunable without an app update. Re-seed after any change to it.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`.
**Fourteen functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`,
`adReward`, `appleNotification`, `sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`,
`withdrawGrove`, `publishGroveRanks`, `claimName`, `reportKeeperName`, `deleteAccount`.
`firebase/README.md` is the guide; `firebase/e2e/smoke-test.mjs` is **90/90 live** and
`firebase/e2e/delete-account.mjs` is **14/14 live** — the second one erases the throwaway
accounts it makes, so it is the only suite here that leaves less behind than it creates.
`adReward` is the one that grants a wheel slice: it reads the account's spin index off the
wallet, recomputes the wedge the phone drew, grants `amount x percent`, and advances the index
in the same transaction the grant record guards (invariant 25).

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

0. **Delete an Apple-linked account on a device, and check it leaves Apple's list.** Everything
   else about deletion is **live as of 2026-08-28**: all fourteen functions deployed, the invoker
   binding granted on `deleteaccount`, the four `APPLE_SIWA_*` secrets set from the Sign in with
   Apple key, and `firebase/e2e/delete-account.mjs` **14/14 against the real database** — the save
   and its subcollections gone, the card gone, the name released *and re-claimed by another
   account*, the auth user gone, and a second call a clean no-op. `appleConfigured: true` in the
   deletion log confirms the credentials load in the running function.
   <br>What no test here can reach is Apple's own answer. Every account the live suite makes is
   anonymous, so it has no authorization code and the log correctly says `no authorization code`;
   the token exchange and `/auth/revoke` have therefore still never executed. The check is:
   delete an Apple-linked account in-app, then **Settings ▸ your name ▸ Sign-In & Security ▸ Sign
   in with Apple** — the app should be gone from that list, and the log should say
   `appleRevoked: true`.
   <br>**Two deployment traps, both learned here.** Never `firebase deploy --only functions` for
   the whole codebase: it failed all fourteen updates with `Failed to make request to
   cloudfunctions.googleapis.com` — transport, not rejection — while still *creating* the new
   function, so the state read as "nothing deployed" and was really "one of fourteen". Batches of
   three or four succeed first time. And a secret is pinned at deploy time, so setting one prints
   `1 functions are using stale version` and changes nothing until that function is redeployed.

1. The sixteen products in the **Play Console**, and the **three heart containers in App
   Store Connect** (the other thirteen iOS products are done and verified end to end — a
   sandbox `gg_gems_1` redeemed on 2026-08-24). `gg_heart_vessel_1/2/3` must be created as
   **non-consumables** in both, at the $19.99 / $29.99 / $39.99 tiers; a consumable would let
   the store sell a permanent upgrade twice and would break Restore, which is the only way a
   container comes back after a reinstall. The whole server side of them is **live as of
   2026-08-26** — rules deployed, all thirteen functions redeployed, `config/products`
   carrying all three capacities — so the consoles are the only thing left between this and a
   real purchase. What is still unproven is the same last link every product here has: a real
   receipt reaching `redeemPurchase`, which needs a sandbox buy on a device.
2. **View financial data** on the Play service account, or the refund sweep silently no-ops.
3. The `appleNotification` URL registered for **both** production and sandbox.
4. AdMob **instances** under each of the ten LevelPlay ad units (the units exist on both
   sides; only the mediation link between them is missing).
5. Fill in `app-ads.txt` from each network's dashboard and host it on the domain in both
   store listings; turn on in-app bidding.
6. Delete the ~210 synthetic saves and the name reservations the live suite leaves behind.
7. **Measure the turn tuning.** The three lines (1.20 / 1.40 / 1.60) were reasoned about, never
   played against: run `Tools/verify/difficulty.py` and, once there is live data, first-attempt
   clear rates. The budget is the only fail state a glade has now, so it is the number most
   likely to be wrong and the one with the shortest path to an uninstall.
8. **Retune or accept the chapter gate.** Whether two stars a level still filters anything is
   unknown — see invariant 22. `chapterGate.starsPerLevel` is content, so this costs a re-seed
   and no store review.
9. **Delete the retired `run_continue` ad unit** from the LevelPlay dashboard. Harmless where it
   is (nothing requests it, it paid no currency) but it is one of ten units that have to be
   reconciled against AdMob instances in item 4.
10. **Give `bestMillis` its removal.** See invariant 22 — drop it from `FirestoreSaveMapper`
    and `firestore.rules` in a later schema version, once no shipped client writes one.
11. **Measure the heart rescue with the continue.** `heart_rescue_offered` /
    `heart_rescue_bought` is the second funnel on the same screen and it answers a different
    question — the continue's ratio is taken against a lost run, this one against an empty
    heart bar — so read them apart. What they decide together is whether 20 gems is one price
    or two. Content, so a retune costs a re-seed and no store review.
12. **Measure the bonus wheel, and read it against the cap.** The ladder averages 218.75% and
    the cap moved from twelve to six to pay for it, which holds the *day* roughly where it was
    and more than doubles what one video is worth. Both halves were reasoned about rather than
    played against. `rewarded_ad_completed` on `win_bonus` is the funnel; what it decides is
    whether the cap is now the thing that binds (it was never meant to be) and whether the
    tail slice is rare enough to stay a story. Content, so a retune costs a re-seed and no
    store review. **The server side is already live** — seeded and all thirteen functions
    redeployed on 2026-08-26, with `adReward` proved end to end against the deployed build:
    two synthetic callbacks on one account drew slices 2 and 7 and were granted 300 and 1,000,
    the index advanced once per paid view, and a retried callback paid nothing and did not turn
    the wheel again.
13. **Measure the Budburst ramp.** Its ten groves are all par 3 and dealt eight taps each, so
    the whole ramp is how many are shut in (4 → 8). That has not been played against, and the mode
    is the one commissioned against a *feeling* rather than a difficulty (invariant 20k) — so the
    reading that matters is not the clear rate (it should be ~100%) but the **three-star rate**,
    which should stay high throughout and dip only a little at the end. Three dials that would
    have moved it were tried and removed for being ramps built out of withholding (`spare`,
    `greedy`, old wood), so if the chapter turns out to be flat the fix is **more to free**, not
    less to spend. `spare` is authored per level if it is ever needed, so a retune is a content
    drop and no store review.
14. **Measure the continue.** 20 gems for +15 turns was reasoned about, never played against
    (invariant 23), and it is the second number after the move budget most likely to be wrong:
    too dear and a defeat is a dead end, too cheap and the fail state stops meaning anything.
    `continue_offered` / `continue_bought` are the funnel; the ratio and the distribution of
    `taken` are what decide whether `gems` moves or `gemsStep` stops being zero. Content, so a
    retune costs a re-seed and no store review.

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

- **A screen built in the same frame as the canvas can trust neither its rect nor its scale,
  and the launch screen is the only one that is.** `CanvasScaler` applies its scale factor from
  `Canvas.willRenderCanvases`, which runs *after* every `Update` in the frame, and `Boot` builds
  the canvas and raises `SplashScreen` inside that same frame. Two separate things go wrong and
  the second is the one that survives fixing the first. **The rect lies**: `Content.rect` reports
  raw device pixels for a frame — 1440x3120 on a QHD phone rather than 1080x2340 — so a
  full-bleed picture fitted to it is laid out for the wrong shape and snaps a frame later. And
  **the scale lies**: every number this screen computes is in canvas units, so even a perfect
  layout is *drawn* at a scale factor of 1 in the first frame and at the real one in the second,
  which rescales the whole interface after it is on screen. Both read as the same symptom — the
  launch arrives stretched sideways and settles — and both are invisible on a 1080-wide phone,
  where the wrong answer and the right one coincide. That is why it survives a desk full of
  checks and is reported from a device.
  <br>Three fixes, and they are not alternatives. `Boot` calls `Canvas.ForceUpdateCanvases()`
  the statement after it builds the canvas, so the scaler has applied before anything is built
  on it. `SplashScreen.Fit` does not measure the canvas **at all** — the scaler is width-matched
  at `Boot.RefWidth`, so the canvas is always that wide and its height is the display's aspect
  times that width, which is a pure function of `Screen` and correct in the first frame; the same
  division converts `Screen.safeArea`, which `SafeArea` would otherwise divide by a scale factor
  that is not set yet. And the screen holds a **black curtain** until a frame passes in which
  neither the layout nor `Canvas.scaleFactor` has moved (ceiling half a second), then fades it
  over another half — which covers the Android devices that report landscape for a frame before
  locking to portrait, and is the fade the screen needed anyway, since `Flow.Go(instant: true)`
  gives it no iris and the thing before it is the operating system's black window.
- **`Destroy` lands at the end of the frame.** Hide a region before destroying it, or the
  outgoing panel draws over its replacement for a frame — which, with everything entering
  from scale zero, reads as a flash.
- **`Show` animates, `Refresh` does not.** Anything raised by an *event* is a redraw. A
  screen repainted by a wallet change, an art scope landing or a ledger event must not
  replay its entrance; `GridView`/`GroveFieldView` bind cells rather than rebuilding them.
- **A tween that reads its own target's value must say where an interrupted one lands, or
  the error compounds.** `Punch`, `Shake`, `Bob` and `Breathe` read a resting value and
  *borrow* it; `Pop` reads the size it is springing *to*. Superseding one on its channel used
  to drop it exactly where it stood — so the next punch took a half-squashed scale as its own
  rest, and spam-tapping the hub's companion multiplied one squash into the next until the
  critter was a sliver. `Pop` had the same fault upside down and one hand-written workaround
  (`GridView` resets a recycled cell's scale, because a cell retired mid-entrance kept a scale
  of zero for ever). `Tw.OnAbandon` is where a tween declares the answer — hand it back, or
  land on it — and `KillChannel` honours it. Anything moving a value somewhere new declares
  nothing and is still left where it got to, which is what keeps a cross-fade a cross-fade.
- **The corollary, and the half a channel cannot save you from: two tweens on one value are
  a bug however different their channels are.** A channel decides what *supersedes* what; it
  says nothing about what they write. `Punch` and `Scale` sit on different channels and both
  write `localScale`, so a punch fired beside a scale reads a value the scale is still moving
  as the size to squash around and lands the target a few percent off, for ever — which is
  what the glade's bloom did to the whole grove until the punch became a `Shake`, because a
  shake borrows the *position* and nothing else there writes one. The same pairing is why
  `Tween.Breathe`'s remarks tell two callers to kill a breathe before punching. Two rules
  follow: before adding motion to something already moving, ask which *value* each tween
  writes rather than which channel it is on — and when a gesture can supersede itself, kill
  its channel **before** reading the rest value, or the restore has not happened yet and you
  capture mid-flight. The glade's critters cost exactly that: a critter woken on the last ring
  of the surge was still in the air when the bloom asked the whole grove to leap, so reading
  the fixture then took a mid-air position as the ground and left it hanging over its own tile
  for the rest of the run. The overlap is gone — only the bloom leaps now, for an unrelated
  reason — and the kill stays, because a gesture that reads its target's resting value is one
  call site away from superseding itself again and this failure is silent and permanent.
- **And the same rule from the other side: one tween moving several transforms must be owned by
  one they all hang from.** A tween dies with its owner and is killed by its owner, so a tween
  that moves five objects while naming one of them as its owner is four objects nothing can
  reach. Budburst's fall was exactly that — the flower, its heart, its glow, the cocoon and the
  critter inside it, all moved by a tween owned by whichever of the two pictures was falling —
  and *everything* in that file that repaints a flower kills that owner outright
  (`PaintCell` and `ThrowFlower` both call `Tween.KillAll(cell.Bud)`, correctly, to stop a
  breath). A killed tween never reaches its `OnDone`, so a flower that fell into a cell and then
  burst on the next wave, or one still falling when the chain's closing repaint ran, left all
  five pictures hanging between two squares for the rest of the run. It was reported from play as
  flowers getting stuck half way, and nothing here could have caught it: the board, the par and
  every gate are exactly right and only the drawing is wrong. The fix is a **`Cell.Piece`** node
  that holds everything that travels, so a fall moves one transform that nothing else animates —
  and the ground, the hit target and the `Btn` stay where the layout put them, which is what a
  falling board must not lose. Two smaller things went with it, both general. A fall is
  `OnAbandon`'s *second* kind — it arrives at a resting state it knows absolutely — so it declares
  where an interrupted one lands rather than being left mid-air; and the offset is taken **after**
  the tween is registered, because registering supersedes the fall already running there and a
  superseded fall lands, which would undo a lift taken first.
- **A stagger and a duration are one bound, not two.** `BudTempo.Rain` promises the grove is back
  on the ground before the next wave charges and `FallOver` kept a fall inside that allowance —
  but the ripple's delay was *added* to the result, so a piece late in the ripple was still
  travelling a third of a wave after the wave that threw it had ended, which is two waves moving
  the same flowers at once and is what made the stranding above so easy to hit. `BudTempo.Rainfall`
  hands back both halves and spends the delay out of the fall rather than beside it, so a late
  piece falls *faster* rather than later. It is in Domain and has a test, for the reason every
  timing rule here is: motion is the one subsystem whose failures only show up in play.
- **A reward the player has earned must not be kept where the board is allowed to rearrange.**
  Budburst frees a critter by *emptying* its square — the model turns it to bare ground, which is
  the whole point, because the grove then falls into the hole. The critter was drawn as a child
  of that cell, so everything the board did next was done to it: the flower landing on its square
  took it down with the fall, `PaintCell` was free to paint a sleeping critter straight over
  somebody who had just got out, and when that flower later burst, `Wind` — which turns the whole
  tile — span the creature round with the scenery. Reported as critters falling, as flowers
  falling through them, and as critters rotating; one fault, three sentences. `BudView._freed`
  and the `Freed` layer are the fix: a critter that is out is a **resident of the grove**, drawn
  above the field and below the fireworks, standing at the square it was let out on for the rest
  of the run. Nothing that falls can reach it. The general form is worth having before the next
  mode does this: **ask what the model does to a square after the thing that made it special has
  happened** — here it is emptied, which is exactly the case where "draw it in the cell" stops
  being true and nothing says so.
- **What answers an event and what stands as an invitation are different gestures, and a punch is
  not the small one.** `Tween.Punch` is a damped sine through three half-cycles — a *wobble* —
  which is right for a control being pressed and wrong for a creature acknowledging something
  that happened near it. A freed critter answers a wave with `BudView.Pump`: one half-sine out
  and back, no overshoot at either end, at `BudTempo.FreedPump`. It kills the breathe before it
  starts and restarts it after, and it never reads the target's scale — a freed critter's rest is
  `FreedScale` and is *known*, which is stricter than the "kill the breathe before you read"
  rule this file already carries twice.
- **A payoff drawn in the same register as its packaging cannot be seen, however much of it there
  is.** A cocoon opening in Budburst draws a star behind it, the shell whitening and going, six
  chips of shell, two shockwaves, sparks, three embers and a gold halo — and the creature the
  whole level was for arrived in the middle of all of that as one more thing moving. Reported as
  *no emphasis at all*, on a build drawing eight separate effects, which is the useful part: the
  answer was never another effect. It is a beat where the creature is **the only thing moving** —
  `BudView.Circle` closes a ring *inward* onto them (every other ring in the mode expands, which
  says *something went off here*; closing says *this one*) and `Pump` swells them inside it, both
  after the shell's own noise has finished and both slower and larger than the pulse the same
  critter answers a later wave with. Before adding to a celebration, ask what else is on screen
  in that quarter-second; if the answer is "eight things", the fix is a silence rather than a
  ninth.
- **A panel with several exits reports through none of them reliably.** Put the safe outcome
  on `OnDestroy` and make the exception the thing somebody declares — `AdOfferOverlay.Dismissed`,
  the pause menu's unlatch, `BoardView.Locked` as a property that raises `OnChanged`,
  `BudView.Finishing`. Exactly one of `Rewarded`/`Dismissed` fires, so both must be handled.
- **What a way out of a run costs is `RunScreen`'s, never a mode's.** Commit, resolve, forfeit
  and the confirmation all live there, and `RestartLevel` is not overridable — a mode supplies
  `Rewind`, `RunOver`, `NoteAbandoned` and `StakeLevel` and never gets at the price. It was each
  mode's own for two modes and the copies drifted: one guarded a closing cascade and the other
  did not, and Lightweave's restart never called its copy at all, so a restart there was free —
  on a mode whose fail state is a pot of ink that a restart refills. Nothing could catch it but
  playing the game, so `RunStakeTests` now fails if any mode declares a piece of the stake.
  A related trap the same guard caught: `ModeScreen`'s chapter coroutine was called `Resolve`,
  which *hid* `RunScreen.Resolve` from every mode below it — the calls compiled, bound to the
  coroutine, built an iterator nobody ran, and a won grove would have been charged for again at
  the next launch. Two members with one name in one hierarchy is a bug waiting for the third.
- **A run begins when `RunScreen` says so, never when a board happens to be unlocked.** A
  board's `Locked` flag has several writers and one of them is an animation: a first-timer's
  tip latched the board on presentation, `BoardView.IntroSweep`'s tween unlatched it a beat
  later, and the run's own play time then accrued for as long as the player took to read a
  lesson they are shown once in their life — and after three seconds of it the run was
  committed, so backing out cost a heart. Both writes were correct; only their order was wrong, which is why no
  compile, validator or screenshot could see it. `RunHold` is a latch nothing else writes,
  held from construction and released only after the last lesson closes, and `RunScreen.Tick`
  is the door a run may start or advance through. A mode *declares* what it teaches
  (`Lessons`, `Flavour`) and never sequences it.
  <br>**It was a funnel with one caller, and the fix was to stop asking modes to walk into it.**
  `Tick` was a `protected` method each mode called from its own `Update`, which is the same
  "remember to consult the latch" shape it was written to replace, with the remembering moved one
  step. Three modes out of four never called it and nothing noticed — and two of those would take
  input while the iris was still opening, long enough to commit a run and be charged a heart for a
  board the player had not seen. `RunScreen` now owns `Update` and the two halves are **abstract**
  (`Runnable`, `Running`), so a mode cannot decline to answer, and `Tick` is **private**, so it
  cannot advance itself. A default would have kept the hole exactly where it was: a mode that
  overrode nothing would compile, run, and opt out in silence. The one part no language can express
  is that Unity dispatches `Update` to the most-derived declaration only, so a mode declaring one
  silently steals the frame — `RunFrameTests` refuses that by reflection, the way `RunStakeTests`
  refuses a mode declaring part of the stake.
- **A reward that lands somewhere is worth more than one that is merely granted, and the
  cascade that does it exists once.** `RewardFlight` — the chest's collect, lifted out when the
  rewarded ad and then the shop's receipt needed the same thing, for the reason `TokenFlight` was
  lifted out one level down. Three panels turn money or a video into currency and all three now
  end the same way: chips empty into the balance row of whatever screen is underneath, so the hub
  and the shop both register their pills with `ResourceSlots`.
  Two rules keep it honest. A readout has **one writer**: the payout rewinds a pill to what it
  said before the grant and walks it forward a token at a time, so it `Claim`s the slot and
  `ResourceSlots.Repaint` refuses the hub underneath — a wallet change landing mid-cascade would
  otherwise jump the number to the truth and have the next token drag it back down. And the
  target is **read live at every landing, never captured**: a chest's credits are in the ledger
  before the animation starts, but an ad's are granted by the server (invariant 10d), so the
  figure is walked towards whatever the balance says when a token arrives — hearts climb at
  once, credits climb the moment the sync lands, and a grant that has not arrived leaves the
  number exactly where it was rather than anywhere invented. A prize with no pill (seconds on a
  run, a hint) or a panel opened from anywhere but the hub adds nothing and simply closes: a
  reward that is already banked must never depend on an animation being able to run.
- **A panel that explains a resource is the answer to a question, never a toll on the way to
  playing — and what a video pays is a panel of its own.** `AdOfferOverlay` lists how hearts come
  back, when the next one lands and how many videos the day has left, which is exactly right
  behind the `+` beside the heart pill: that control means "tell me about this resource". It was
  also what stood between a player who had just been stopped mid-session and the video that would
  let them carry on — and it paid out by turning its own watch button into a COLLECT, drawing the
  largest moment in the placement as the smallest change on the screen. Both halves are the fault
  `PrizeOverlay` was built for one placement earlier (the bonus wheel, invariant 25) and it
  generalises: the tap on a defeat panel's WATCH FOR HEARTS now shows the video, and what returns
  is the celebration with COLLECT under it. `HeartVideoFlow` owns it, beside `DefeatRescueFlow`
  and for that class's reason — the free way back and the paid one are two collaborators on one
  panel rather than a sixth and seventh responsibility inside it. Three things about it are
  load-bearing. The way onward is `PrizeOverlay.Collected`, raised **exactly once however the
  panel ended**, because the hearts are banked by the redeem and the panel underneath is stale the
  moment they are — a defeat screen still reading "you are out of hearts" over a wallet holding
  two is the one frame of this a player could read as a bug, so a dismissal has to lead onward
  just as a collect does. The button is the **only** thing left saying why a video is not
  available, so it is painted through `AdOfferButton` on a timer rather than given a fixed
  caption. And a refusal is a **toast**, not a row: a panel that derives its height from the rows
  it draws (`DefeatPanel`) would need a fourth shape held under `PanelStack.TallestPanel` to carry
  a sentence that only exists when nothing was paid.
- **Showing a rewarded video is five steps in an order, and the order is the substance.**
  `RewardedVideo.Watch` — mint the impression, show it, snapshot the pills, redeem, read the
  refusal. Two of those orderings matter and neither is visible in a compile, a validator or a
  screenshot: the impression is minted **before** the SDK is asked for anything, because the nonce
  inside it has to reach the network as a custom parameter, and the pills are snapshotted
  **before** the redeem, because deriving the snapshot afterwards by subtracting the offer is
  wrong in the case that matters — a heart reward landing at the ceiling grants nothing, so the
  subtraction rewinds a pill below where it ever stood. It had been written out three times
  before it was written once (invariant 9a at the smallest scale it appears at). What stays with
  each caller is the half only a `MonoBehaviour` can answer — whether it is still alive after the
  await — and the asymmetry that follows: **a prize is raised before that check and a refusal
  after it**, because the reward is banked whether or not anybody is still looking, while a
  refusal is news about a button that no longer exists.
- **A receipt has to show the transaction happening, not report that it happened.** The shop's
  thank-you panel was a chime, a stamp and two printed numbers, and it was defended as
  proportionate — a coin pack is a transaction, not a companion somebody saved for over weeks.
  That was the wrong axis: the fault was never the length, it was that *nothing happened*. It is
  now the goods landing with a shockwave, `Payout` throwing their contents out of them, and
  COLLECT handing the lot to `RewardFlight` — money → goods → your purse, from three mechanisms
  that already existed. Two rules keep it repeatable rather than tiring: **a way out arrives with
  the tokens, not after them** (the button pops in when the payout starts, and tapping it early is
  safe because the flight's snapshot was taken at build time), and **everything loud happens once,
  on the last landing** — one sound and one flourish, for the reason the buzz was removed below.
- **Repaint from an event, never from a callback on the panel that changed something.**
  `CompanionLedger.Changed`, `CloudSaveService.IdentityChanged`, `GameSettings.Changed`.
- **A control whose liveness depends on a per-frame fact has to be repainted on that frame, and
  "there is an event for it" is not the same thing.** Budburst's hint key was painted once when
  its row was built and never again: it is live while the run is *running*, which is
  `RunScreen.Running(bool)`'s answer and is written every frame by that method and by nothing
  else — so the key was painted while the run was still held by the opening iris and stayed grey
  for the life of the screen. Every event the screen *did* listen to (the wallet, the board's own
  `Changed`, the latch) fires for a different reason, so none of them ever corrected it. It
  reached play as "the hint button never works", which is the failure a dead control always
  reads as. The repaint is a comparison and two assignments; put it on the frame that knows.
- **`UIKit.Box` pivots centre.** Anchoring a child to an edge puts half of it outside, and
  growing a panel puts half the new room above the art.
- **Measure a painted shape's face rather than centring on its sprite.** `PillFaceLift`,
  `SquareFaceLift`, `NodeFaceLift`, the win banner's `RankLift`, the iso tile's derived skirt.
- **`UIKit.Label` defaults to `Overflow` with no clipping** — an over-long translation keeps
  drawing rather than truncating. Anything holding a translated string needs `UIKit.Shrinkable`.
- **A one-line caption is set through `UIKit.OneLine`, never by raising `Btn.OneLine`**, and the
  two are not the same thing. `UIKit.TextButton` switches Unity's best-fit on for any button
  carrying a glyph, and best-fit concedes the **line** before it concedes the size — so on a long
  caption it folds rather than shrinks, which is what `Squeeze` exists to prevent. Raising the
  flag alone leaves both rules running over one label: `Squeeze` computes a size from
  `preferredWidth`, best-fit overrides it at draw time, and it re-runs on the next layout rebuild
  — a frame or two later, when the dynamic font's texture is regenerated. The player sees the
  caption arrive crushed and then spring out to its real width, which is how it was reported.
  `UIKit.OneLine` turns best-fit off, so the caption is sized once, in the frame it was set.
  Measured on the wheel's own button at its real size, with `WATCH A VIDEO TO COLLECT` in it:
  the flag alone gives `bestFit=True, lines=2`, and `UIKit.OneLine` gives `bestFit=False,
  lines=1`. It had escaped twice, on the two buttons that open and take the video bonus.
- **Generate art the screen cannot afford to be missing.** An `Image` whose sprite has not
  arrived is a white rectangle, so anything on a dark or ceremonial screen is
  `Art.Bloom`/`Dial`/`Gradient`/`PrismRing`/`IsoTile`/`Ring`/`Glow` rather than an address.
- **Controls go in `View.Safe`, art stays full-bleed.** Letterboxing a backdrop to dodge a
  camera cutout is a worse picture than the cutout. iOS reports its inset a frame or two after
  a cold start, so the node re-fits itself rather than reading the value once in `Build`.
- **Timing rules live in Domain and are tested** — `Cue`, `TweenCycle`, `GroveGrowth`,
  `GroveUnveil`, `BudTempo`, `GladeFanfare`, `CoachStroke`. Every sequence is bounded and **the
  rate gives way**, so a bigger board is never a longer wait. Motion is the one subsystem whose
  failures show up only in play, which is why the arithmetic has to be reachable without an
  Editor. `GladeFanfare` is the one whose length is a function of the *board* rather than of a
  chain the player caused, so it is the one where the bound is doing the most work.
- **A lesson is declared as a fact about the board, never as a fact about the player.** A mode
  fills `RunScreen.Lessons` with everything its board teaches, and `RunScreen` asks `TipLedger`
  which of it is new. That split is what let the "show me again" key in a run's header cost one
  method: the mode used to do the filtering itself, and a list filtered by *never met* is empty
  at exactly the moment somebody presses a button asking to be reminded. The review re-asks
  `Lessons` rather than replaying a list kept from the opening, because a restart rebuilds the
  tiles a tip rings and a cached `RectTransform` is by then a destroyed object — the tip would
  silently lose its ring and its coaching hand and become a sentence in a box. Both paths take
  `RunHold.Teaching` and call `Latch`, so a tip on screen freezes the run whoever raised it,
  and there is no second copy of the rule about when a run is allowed to run. `Teachable` is the
  mode's own "is the board taking input" predicate, and it is what stops a review latching a
  board that a cascade or a closing sequence still owns.
- **A lesson about a gesture is shown, not described, and a demonstration must show a move the
  player could actually make.** A `TipOverlay` with a ring and two sentences is the right shape
  for a *rule*; a **verb** is not that shape, because a sentence describing a movement has to be
  turned back into the movement by whoever reads it. So `Lesson.Trace` lights a route on the real
  board and `CoachHand` walks a hand along it, `CoachStroke` times it, and `Target` still rings only
  the thing being named. Three rules the machinery keeps, each learned by getting it wrong: the
  demonstrated route must be one the mode's own input could produce (a straight interpolation
  between two cells is a diagonal drag on a mode that has no diagonal, shown while teaching what
  the input is); it must never be the board's own answer, because a demonstration is not where the
  solution is handed over; and `Art.Hand` is generated for invariant 7b's reason and is **tilted on
  purpose** — an upright finger over a closed fist is a gesture that must never reach a teaching
  panel in any market.
  <br>Lightweave is what all of that was built for and Lightweave is retired. It survives because
  Groovekeeper already uses the tap half (`CoachHand.Tap`), and because the next mode with a verb
  rather than a rule will need the rest of it — Budburst does not, because its verb is one tap and
  everything it has to teach is what happens *after* the tap, which the board shows better than a
  hand could.
- **Celebrate once.** The board already flashes, sounds and (for a glade) throws confetti when
  it solves; the win panel adds no fanfare and no confetti.
- **The game does not vibrate at all, and `Haptic` is deleted.** It was twenty call sites and a
  settings toggle, and every one of them was the *same* knock whatever it was answering:
  `Handheld.Vibrate` on Android is a single fixed-length heavy pulse with no way to make a second
  lighter than the first, so a mode that opens four cocoons in one chain fired four times inside a
  second and produced one rumble. What it cost in feedback it took back twice over in noise. The
  `haptics` field stays in the settings DTO and `GameSettings.HapticsOn` stays with it, retired in
  place for `bestMillis`' reason — `settings` travels in the save and `firestore.rules` gates that
  document with a `hasOnly` allow-list, so a key dropped before every shipped client has stopped
  writing it loses *every* save write (invariant 12a).
- **Depth is applied to a whole visible window in one pass.** `SetSiblingIndex` *inserts*, so
  assigning depth per tile as tiles are realised leaves a field that looks sorted and is not.
- **An arrangement of identical things is arithmetic too, and the tell is an even count.**
  `TokenPile` — the shop's coins, its gems and both of its heaps of hearts, which were three
  copies of one shallow arc with every second token dropped a little. `i % 2` is only a symmetric
  rule when the count is odd, so a pile of four came out visibly heavier on one side and a pile of
  five did not, from the same expression; that is what read as scattered rather than stacked. Two
  rows, centred, wider at the front. The **order** matters as much as the positions: a row drawn
  left to right shingles every token over the one before it and points the whole pile one way, so
  each row is laid from its ends inwards and the front row goes last — which is also why a pile
  hands back a *slot* alongside its draw order, since a caller filling a bundle's gems is choosing
  by position and the draw order is back to front.
- **Whether two things on a screen overlap is arithmetic, so it goes in Domain and gets a test.**
  `ChapterMap` did it for map nodes (invariant 8a); `BudBand` does it for the readouts under a
  standing line under a grove, and `ReadoutRow` for a row of one, two or three numbers. The rule
  earned its third instance the honest way: the band was three constants with a paragraph
  explaining why they cleared each other, and the paragraph was wrong, because `UIKit.Box`
  *always* pivots at centre whatever it is anchored to. A comment cannot catch that and a
  screenshot on one aspect ratio does not reliably either. `PanelStack` is the fourth and the
  worst-earned: `GladeRewardsOverlay`'s height was a typed number, a fourth section was added
  without moving it, and the last paragraph had been drawn **78 units into the close button**
  ever since — invisible in English, where that paragraph happens to be short enough. A panel
  whose section count varies with content must derive its height, and the derivation is
  measured against the shortest canvas this game is drawn on (portrait 4:3, so 1440 reference
  units). Five sections is what that shape holds; a sixth fails a test rather than a tablet.
  <br>Two things about that ceiling are worth not rediscovering. **A modal is centred, so the
  title ribbon's overhang counts twice** — the binding constraint is `H/2 + overhang ≤
  canvas/2`, and the obvious reading (`H + overhang ≤ canvas`) is 87 units too generous because
  it spends the clear air *under* the panel on a problem entirely *above* it. And once the
  height is spent, **width is the only lever left**: a paragraph needing more room can only get
  it sideways, which is why this panel is 960 rather than 900. Both were found by rendering the
  thing offscreen to a PNG and looking at it — `Text` best-fit is approximate, so a paragraph
  the arithmetic calls exact can still spill a few units, and no test will ever say so.
  <br>`WheelPanel` is the fifth, and it failed in the one way the other four could not: it had
  the test *and* the arithmetic and still drew a row through its neighbour, because **one number
  in the stack meant something different from the rest**. Four rows were centres and the status
  paragraph was documented as a *top* — but `UIKit.Box` pivots at centre whatever it is anchored
  to, so the overlay handing that top over as a position lifted the box 46 units, straight
  through the odds line above it. `WheelPanelTests` passed throughout, because it was checking
  the arithmetic the panel did not use — which is the failure mode a layout test has, and the
  reason a stack should be **all centres**: a number that has to be converted by its caller is a
  number some caller will forget to convert. Found by a player reading the overlap, not by any
  check here; now measured against the live objects with `GetWorldCorners` and `Rect.Overlaps`,
  which is the only thing that compares what was *drawn* against what was *derived*.
  <br>`ProductCardBadges` is the sixth and it widens the rule: **the two things that overlapped
  were on different objects**. A shop card's badge hung 38 units past its own plate and the next
  column's ribbon reached 22 past its plate's other edge, so across a gutter of 34 they shared 26
  units of the screen — and since `GridView` recycles cells, which of the two was drawn on top was
  whatever order the pool happened to be in. Neither number is wrong on its own, so nothing that
  reads one object at a time could ever have seen it, and it was reported by a player. What made
  the fix statable is that the badge now knows where its neighbour's ribbon starts: the grid's
  column pitch *is* the card's own width, so a card can derive the constraint without being told
  it. Two smaller lessons came out of the same corner. A mark is measured as the shape it **draws**
  — `seal_gold` is a disc in a square texture, and treating it as a rotated square overstates its
  reach by a sixth, which is a sixth of a badge of clear air bought by pushing it into the picture
  underneath. And a caption is sized against the **field it is read on**, not against the sprite
  carrying it: the badge's text box was half again as wide as the maroon disc, so a two-word badge
  spilled onto the plate, where lettering coloured for a gold rim was drawn on the darkest thing
  on the card and disappeared.
  <br>`SplashCover` is the seventh and it is the one with the least else to catch it: **the
  thing the layout must not collide with is painted into a texture**. The launch screen is now
  the key art with the wordmark baked in — a looping clip from `StreamingAssets`, over a still
  that is its own first frame, so the handover has nothing to blend and a device that cannot
  decode simply keeps the picture — with a loading bar under the word. Where the lettering ends
  is not a rect anything can measure at runtime — there is no layout to ask, no
  compile that can fail and no validator that walks it. The canvas is width-matched at 1080
  (`Boot.BuildCanvas`), so its *height* is whatever the device's aspect makes it, and one
  portrait picture cannot be all of those shapes: the fit is cover, and the crop comes off the
  **top**, because everything the screen is for is in the bottom tenth. The bar's clearance is
  bounded from both sides — it takes the gap the design wants, is raised to clear a home
  indicator where there is room, and is finally capped so it can never come closer than
  `MinGap` to the lettering, because on a short canvas with a navigation bar those two wants are
  not both satisfiable and the honest answer is to give up the inset rather than the word.
  Anything that re-cuts or replaces the cover has to re-measure `WordFootUv` off the new art:
  it is the one number here that a wrong value puts straight through the logo, on every device
  at once, with nothing anywhere to say so.
  <br>**It is also the one screen whose art has to be given back.** The splash is built at every
  launch and returned to never, so both halves of it are freed on the way out — the `VideoPlayer`
  is stopped before it is destroyed (a player left playing keeps a hardware decoder alive through
  teardown on some Android drivers), and the poster is claimed into `AssetLibrary.SplashScope` and
  released. That claim is why `AssetLibrary.Claim` exists: a screen that draws in the frame it is
  built fetches synchronously, so the address has to belong to a scope *before* it is asked for —
  claimed afterwards, the sprite is already in the global cache and stays there for the session.
  It is named in `AssetManifest.SplashAssets` rather than in `GlobalAssets` for the same reason,
  and named there at all so the audit does not call it dead weight.
- **A row's position is a centre, so a paragraph in one must be centred too — and a slot that
  is reserved and not filled belongs to the row below it.** Both halves were reported from play
  about the same sentence, the defeat panel's "no heart was spent" line. `DefeatPanel` hands out
  centres for the room each row gets, and `Body` was anchoring its text to the *top* of that
  room, so a two-line sentence in a room sized for more sat high in it by the difference and the
  air above it and below it were never the two halves of one gap. On top of that its centre was
  typed, under a near-miss slot that is reserved on every defeat and filled on few — so on the
  ordinary run there were seventy-four units of paper doing nothing above the line and fourteen
  below it, which reads exactly as it was reported: sitting on the try-again button with a hole
  over it. The fix is `DefeatPanel.FreeCentre(close)`, centred in the room that is actually
  free, and `PaperTop` measured from the title ribbon's own geometry rather than guessed — a
  region that starts at the panel's top edge is centred partly *behind* the ribbon. Anything
  reserving room conditionally should ask what the unconditional case does with it.
- **A colour is chosen against the ground it is drawn on, not against the palette.** `Pal.Mint`
  is used forty times and every one of them is a halo, a fill, a board tint or a dark plate —
  so it is bright, correctly. The one place it was asked to carry a *sentence on the cream
  panel paper* it came out at about 1.8:1, on 32pt body copy that by house rule has no outline
  and no shadow, and a whole paragraph had to be squinted at. `Pal.A` cannot fix that: it makes
  a pale colour translucent, not darker. `Pal.Moss` is the dark green for good news on cream,
  and it is `Pal.Amber`'s argument for the second time — named for the colour rather than for
  the line using it, so the next one does not invent a third shade a step away. Two panels use
  it and the second was found by looking rather than by grepping: the account panel's every
  green, including "your progress is saved online", which is the one sentence in the game whose
  whole job is to be believed. The tell for whether a panel is cream or dark is what its *other*
  text is — a panel writing in `Pal.Cream` is a dark one, and Mint is right there.
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
- **Recall is not difficulty, so the board answers it.** Lightfall's legend under the tray is the
  colour arithmetic, drawn permanently, because "which colour finishes yellow" is something a
  player has to hold in their head *while* deciding rather than a thing worth testing them on —
  reported from play as "I always forget which colour blends with which". The rule it draws is
  derived from the same masks the board mixes with (`FallMixing`), never a typed table, so there
  is no second answer to fall out of step. The distinction worth keeping: a legend removes
  *bookkeeping*, and must never remove a *decision* — which is why the ghost still stops at
  whether a drop bursts and never shows how far the chain would run.
- **A celebration should say how good, not that something was good.** Confetti reads identically
  for a two-chain and a six, so Lightfall counts the chain out loud instead — one number per wave
  while it is still running (nobody watching x3 land knows yet whether there is an x4), and a word
  at the end that climbs. The ladder is `FallChain`, in Domain, because a switch on a wave count
  in a `MonoBehaviour` is the one place here nothing can be proved, and because how loud to shout
  is exactly the decision that gets retuned. Measure before setting one: the shipped chapter runs
  chains of 3–7 routinely, so a ladder pitched for 2–5 spends its top word constantly.
- **Panels that explain the game read their numbers from the rules**, never from the copy —
  `StreakInfoOverlay`, `AdOfferOverlay`, `EventInfoOverlay`. That copy is the first thing to
  rot when the game is retuned.
- **A screen that has grown a fourth responsibility has grown one too many.** `RunScreen`
  reached five — the stake, the run hold, the lesson sequence, the review key and the continue
  offer — and the symptom is always the same: no single rule in it can be changed without
  reading all of them. `RunLessons` and `RunContinueFlow` are what came out, and the test to
  apply before adding the next one is the test `WeaveRun` was split against — *could this rule
  be proved without building the other four*. The collaborator holds the screen (a
  `MonoBehaviour`) so `if (_run)` is Unity's own lifetime check, and the mode's extension points
  widen to `protected internal`: still overridden by modes, now also readable by the piece that
  sequences them.
- **A thing the shop sells is drawn in exactly one place.** `ProductCard` — the plate, the
  picture, the headline figure and the price face — because there are now two shops: the browse
  screen, and the gem shelf a lost run raises without navigating (invariant 23). It carries the
  answers the shop already paid for once (a store's formatted price is used verbatim, a short
  gem balance still shows the price and greys the face) and its layout is *one* layout scaled
  from the browse card's numbers, so the full-size card is byte-identical to what shipped and a
  compact one is the same design rather than a second one. Scale vertical offsets by the plate's
  height and horizontal ones by its width — one factor for both is what made the picture and the
  headline overlap on the first compact card.
- **A card that says one thing twice must ask once.** A shop cell carries a painted picture of
  what arrives and, behind it, a fan of light in that rung's colour, and the two are one
  statement — *this is the sixth of six*. They were briefly two roundings of one fraction in two
  files, which is invariant 9a at the smallest scale it appears at: a shelf re-cut from six rungs
  to five would have moved one and not the other, and the fifth picture under the sixth colour is
  not wrong in any way a compile, a validator or a screenshot could name. `ShopLadder` is the one
  answer, it is in Domain because it is arithmetic, and it is **whole numbers throughout** — a
  product landing exactly halfway between two rungs must not be decided by which way a
  single-precision multiply fell, which is the hazard *Hard-won facts* names twice already.
  <br>The rule it replaced was that **motion singles out**, so only the featured card was lit.
  That is right when the light means *look here* and wrong when it means *how much*: a ramp says
  which of six a card is from the far side of the screen, where one card shouting says only that
  somebody wants to sell it. The hierarchy is kept by **strength** rather than by presence — and
  the featured card still has the gold seat, the gold edge and the seal, which is what lets the
  starter bundle read as special while its picture and its colour tell the truth about its size.
- **A `switch` inside a `MonoBehaviour` is the one place here nothing can be proved.** The
  branching decisions live in Domain and are pinned offline: `HintPrompt`, `RenameRules`,
  `AccountPromptPolicy`, `GroveUnveil`, `GroveGrowth`, `AccountGate`.

### Three confirmations, and only three

`ForfeitOverlay` (a committed run being abandoned), `ReportNameOverlay` (an act taken
against another person that cannot be retracted) and `DeleteAccountOverlay` (invariant 27), which
earns one more completely than either: there is no store to re-deliver an account, no archive to
restore it from and no support path that can bring it back. Its second tap is armed only when
there is a grove to lose, which is `AccountOverlay.ConfirmAdopt`'s rule — arming a button over an
empty grove is what teaches a player to tap through it on a full one. `ContinueOverlay` is not a
fourth: it is an offer rather than a confirmation — it asks a question nobody has asked yet, and its default
answer is the free one. Everything else either costs nothing to
undo or is confirmed by the store's own payment sheet — a panel of ours in front of that sheet
is a tap for a question about to be asked properly. The one destructive prompt left is a
*guest* whose provider already carries a grove, reachable from linking and nothing else.

### Not done, deliberately

- **Play Games Services** — better Android sign-in and the natural home for leaderboards, but
  Android-only, so it cannot be the identity.
- **A visual level editor** — tooling, and the thing most likely to matter next for cadence.
- **Remote content delivery** is built and switched off. Setting `ContentConfig.RemoteBaseUrl`
  turns the heart gate, the chapter gate, the chest odds and the ad payouts into
  minutes-not-days levers; it is the highest-value unshipped setting in the build.
  One known gap first: `Sync Manifest` bumps a chapter's `version` only when its **level
  list** changes, so a content-only rewrite would never reach a client that had already cached
  the body. The fix is a digest of the body in the manifest entry, which
  `ManifestSync.SurvivesRoundTrip` would then police.
- **A "keepers near you" board** — it needs the exact global ordering invariant 19c refuses to
  keep, and the percentile already answers the question it would ask.

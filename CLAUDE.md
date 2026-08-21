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
15a. **The unlock rule is "level **or** purchase", and it lives only in `CompanionLedger`.**
    `AvatarCatalog.ReachedBy` answers the level half and is named for its narrowness on
    purpose — it used to be `IsUnlocked`, and a call site checking half the rule under a name
    that promises all of it is exactly how a companion somebody paid for stays behind a
    padlock. Every screen asks `IsHeld`. The level half stays derived and is never written
    down: a second answer is a second thing a retune can put out of step with the first.
16. **A grove is built, and only three facts about it are stored.** The Grovement is the one
    reward in the game that is a *thing the player made* rather than a number that went up,
    and the whole feature costs `SaveFileDto` three fields because everything else is derived —
    the residents from the companion roster, the home from what was bought. What is left splits
    by *shape*, not by feature. A purchase is an **entitlement**, so `homesteadOwned` and
    `groveLandOwned` are union-joined sets of ids, which is 15 twice over. An arrangement is an
    **instruction**, so `homesteadPlaced` is merged by recency with a stamp per slot, which is
    11c for the third time — and it is therefore the only part of this feature that can lose
    something, which is why an untouched slot writes no row at all and a slot the player
    *emptied* keeps one. Note what is deliberately absent: any count of how many benches
    somebody owns, and any count of tiles. **Holding a piece is permission to draw it in as many
    slots as you like**, because a count of copies is precisely the shape 11b forbids and hearts
    already spent a schema version proving it — and it makes the better shop, since variety
    rather than quantity is what makes two groves differ. A slot id is written into the save, so
    invariant 1 applies to it in full and it is unique across the whole floor.
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
    never changes the nameplate, and the free route is still the keeper ladder and is still what
    a cell leads with. Two ids follow from it. A resident's piece id is the companion's id
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
    two: the **earned** half (companions the keeper ladder reached) is derived from records the
    server already validates for currency and so is unforgeable by construction, and the
    **bought** half is clamped to `earnedCredits + grantedBaseline`, because everything in it
    was paid for in currency the server derives. The client's figure stays a prediction and is
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
- **Unity only re-resolves packages and reimports on window focus.** If a change seems
  not to apply, the Editor probably has not been clicked.

## Current state

Done and verified: content pipeline, stable ids, versioned atomic save with checksum
and tested migration, localisation, analytics seam, scoped asset pipeline on
Addressables (assets are out of `Resources/`), chapter-paginated map, enforced
layering, EditMode test suite, **player progression** (derived XP, levels and credits
on a double-entry ledger, save schema v3, monotonic merge).

**Daily bonuses — the home screen's chests are real.** The panel that used to show
lifetime stars is now the daily loop: three chests, one per three finished runs (won or
lost, any glade), opened by hand, resetting at UTC midnight. `Assets/Game/Scripts/Domain/Daily/`
holds it; the state is three integers in save schema **v6** and everything else is derived.

Four decisions are worth not re-litigating. Contents are **computed from (account, day,
chest)** rather than stored, so a chest cannot be rerolled by killing the app and the
server can recompute it — see invariant 9c. Currency reaches the player as a **claim with
a derived id**, so it is spendable offline without the client ever raising `grantedBaseline`
— invariant 10a; the server endpoint is `claimAwards`, backed by `players/{uid}/grantLog/{id}`.
Every chest has a **guaranteed floor**, which is why there is no pity counter and no
per-player streak state to merge. And the drop rates are **content** in `progression.json`,
published to `config/progression` by the seeder, with the odds printed by `Validate Content`
and shown on the chest overlay.

**Hearts are a ledger, not a count — save schema v8.** `Hearts` stores everything ever
produced, everything ever spent and when the next refill is due; the count is derived and
the merge is three `max`es. It replaces a stored count merged by taking the smaller, which
destroyed a heart on every sync — see invariant 11b for why that was unfixable without
changing the representation. The v4 count and deadline are still written as a derived
mirror, for a client rolled back to an older build, and are read only when
`heartsProduced` is zero or less, which is what identifies a pre-v8 file. Hearts were the
only merge in the save file that could lose something; the currency ledgers, the
progression floors, the chest and ad counters and the tip set were all already joins.

**The heart gate is content** (`HeartRuleTable`, the `hearts` block in `progression.json`).
Refill cap, holding ceiling, refill period, boosted period, max boost hours and what a loss
costs are all published, on the same channel and in the same file as the ad payouts and the
chest odds — because the gate decides how many sessions a player gets, so it multiplies every
other number in that file, and eight hours is a guess made before anybody has a retention
curve. `HeartRules` is a facade over `ProgressionRules.Table.Hearts`, so every call site reads
exactly as it did when these were constants.

**Every published field is safe to lower, and one line of code is why.** The ledger's
structural clamp uses `HeartLimits.HardCeiling` — a permanent `const` — and *never* the
published ceiling. If it used the published one, lowering it would cut `produced` downward on
whichever devices had fetched the new table; `produced` only ever rises, and the whole merge
proof rests on that, so one device would keep restoring what the other kept clamping and the
two would never converge. Instead the published ceiling is enforced in `Hearts.Grant`, where
it is a decision taken once: a lower ceiling refuses new hearts and confiscates none.
`LoweringThePublishedCeilingNeverConfiscatesHeartsAlreadyHeld` pins it. Same shape for the
refill cap — lowering it stops the clock earlier and drains nobody.

Two things this forced. `Wallet.MaxHearts` and `Profile.MaxHearts` are properties, not
`const` — a const is baked into every reading assembly at compile time, which is the wrong
shape for a number a config push may move — and `TrySpendHeart` is an overload rather than a
default argument, for the same reason. `ContentValidation.ValidateHearts` warns (never errors)
on the combinations the reader cannot judge: a gate that does not bind, a ceiling too close to
the cap for collected hearts to survive, a boost nobody can feel, a loss that ends the session.

Where the numbers live now: `HeartRules.RefillCap` is 5 — where the *clock* stops, so the pace
of free play is exactly what it always was — and `HeartRules.Ceiling` is 50, the most anybody
may hold. Everything a player *collects* — a
chest, a streak night, a watched video — stacks into the gap between them instead of
evaporating at a full bar, which is what it used to do at precisely the moment somebody was
most engaged. This cost one line of state and no schema bump: the merge's upper invariant is
now `produced ≤ spent + Ceiling` rather than `+ Max`, and `Hearts.At` already asked only for
the count before granting, so the join and its proof are untouched. A second counter
separating waited-for hearts from collected ones was built first and buys nothing — no rule
here ever needs to know which kind a heart was.

Two consequences worth not rediscovering. `IsFull` is gone, split into `IsRefilled` ("the
clock has nothing left to do") and `IsAtCeiling` ("another heart would be thrown away") —
`RewardedAds.WouldBenefit` wants the second, and reading the first there is what used to
pull the offer off the home screen. And `Spend` restarts the timer by asking
`NextRefillUnix` rather than by comparing to the cap: while a surplus is held the stored
deadline idles in the *past*, so the spend that finally drops a player under the cap must
start a fresh period or the next read pays a heart nobody waited for —
`SpendingBackThroughTheCapRestartsTheClockRatherThanPayingInstantly` pins it.

The `heart_boost` drop halves heart regeneration (4h instead of 8h) for 24 hours;
`Hearts.At` takes the boost deadline and asks per refill, so a catch-up spanning the
expiry pays some hearts fast and the rest slow. It merges by taking the **later** deadline
— generous where the heart count is conservative — because the award behind it is already
deduplicated by its own id.

One honest limit: the run counter lives in the player's own save document and is therefore
forgeable. The server bounds abuse to one day's chests per day, which is what an honest
player gets; proving somebody played three glades would mean trusting a different forgeable
number, and the prize for cheating is a reward that was never scarce.

**The profile screen owns identity.** `ProfileScreen` is the fifth nav tab: name,
keeper level and honorific, a derived grove record, a companion picker, and the account
section — which moved here out of Settings, because linking is about *who the player is*
and burying it in a preferences panel is how it stayed unfound. The companion roster is
`AvatarCatalog` (Domain): permanent ids, art keys kept as separate strings so a re-cut
never reaches the save file, and unlocking **derived from keeper level** rather than
stored, for the same reasons XP is. The roster is **content** — `manifest.json` carries a
`companions` array, so adding one is a portrait, a manifest row and a loc string, with no
code change and no app update. `AvatarCatalog` keeps a built-in list only as the fallback
for a client whose content has not loaded. `wallet.avatarId` is a preference, so the merge is last-writer-wins like
`displayName`, and both now prefer a real value over an empty one: a second device that
has never chosen must not erase the first device's choice.

**Content schema v2 — lazy chapter loading.** The manifest now carries every chapter's
ordered level ids, so boot reads one ~25 KB file rather than opening every chapter (at
forty chapters that was ~800 grid parses and, on Android, a frame per chapter). See
*The index and the bodies* in `CONTENT.md`. Addressables registration is now an importer
hook plus a build-gate audit; the old three-step migration is gone, its step 3 having
long since completed and its step 1 having quietly become a no-op.

**Cloud save: the server is live, the client waits on one SDK install.**
Firebase project `glimmer-groove-1cd60` — Firestore in `eur3`, security rules released,
anonymous auth on, both apps registered, and **six deployed functions** on Node 22 in
`europe-west1`: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`, `adReward`
and the `publishGroveStats` schedule. `firebase/README.md` is the guide;
`firebase/e2e/smoke-test.mjs` proves the rules hold and passes **28/28 live**, the last
nine of which walk a streak night through `claimAwards` end to end.

One warning about that suite, learned the hard way: it signs in as a *new* anonymous
account every run, so anything derived from the account id varies between runs. Its
earned-credits case used to assert a single number and failed on roughly a third of runs,
because a glade's golden multiplier is a function of (account, level). It now derives the
achievable set from the bands published in `config/progression`. Never re-hardcode a
figure there — a money suite that fails at random is a money suite everybody ignores.

The Unity side is written (`Assets/Game/Scripts/Cloud/`, assembly `GlimmerGrove.Cloud`)
and the Firebase Unity SDK 13.15.0 is wired into `Packages/manifest.json`. It waits on
one thing: **the Editor has not resolved the packages yet.** Click the Editor window —
Unity only re-resolves on focus. `GLIMMER_FIREBASE` then gets defined by the asmdef
`versionDefines` and `Boot` picks the real backend over `NullCloudBackend`.

**The SDK is installed as UPM tarballs, not a scoped registry and not `.unitypackage`.**
Google publishes no registry for it; `Packages/manifest.json` points at `file:` paths
under `GooglePackages/`, which are gitignored — run `pwsh GooglePackages/fetch.ps1` on a
fresh clone. Tarballs still carry a package version, which is what `versionDefines`
needs; a `.unitypackage` does not, which is why that route would break the define.
All Firebase packages must share one version.

Two traps in that SDK: `Firebase.Functions` ships as **source with its own asmdef**, so
`GlimmerGrove.Cloud.asmdef` must reference it explicitly (App, Auth and Firestore are
plugin DLLs and auto-reference). And the Functions source needs `Google.MiniJson.dll`,
which lives in the **app** package.

**Accounts: anonymous by default, Apple and Google to link.** No login screen — a
player is signed in silently on first launch and the game is fully playable having never
seen `AccountOverlay`. Linking is offered once a chapter is finished, at most twice ever,
because that is when there is something worth protecting. Firebase drives the OAuth flow
itself via `FederatedOAuthProvider`, so **neither Apple's nor Google's Unity plugin is a
dependency** — one code path, both providers, both platforms. Moving to native sign-in
sheets later is a change to `LinkCredential` only.

The one destructive prompt in the game lives here: a provider already attached to another
grove cannot be merged, because currency was granted and spent separately against each
account. `CloudSaveService.AdoptLinkedAccountAsync` replaces the local save and says so
first. Do not "improve" it into a silent merge.

**Rewarded ads — hearts and coins for watching a video.** `Assets/Game/Scripts/Domain/Ads/`
holds the policy (placements, caps, cooldown, what an offer is worth); `Assets/Game/Scripts/Ads/`
is the LevelPlay half behind `GLIMMER_ADS`, and `NullAdProvider` keeps the whole feature dark
in a build without the SDK. Two placements, both content in `progression.json`: `heart_refill`
pays 2 hearts, `coin_bonus` pays 150 credits. Offered from the defeat screen when hearts run
out, and from the home screen's two `+` buttons.

Four decisions worth not re-litigating. The grant is **server-authorised** — invariant 10d,
and the reason the design does not look like the chest one. Caps and cooldown are **pacing,
not money**: they live in save schema **v7**, merge conservatively (larger count wins within
a day, later day wins outright), and a save edited to clear them buys another offer and no
currency, because the server does not count them. The offer is **never shown when it cannot
work** — `AdOfferState` has six members and each renders a different sentence, because a
greyed button with no explanation is how players learn a feature is broken. And the daily cap
is deliberately **higher than any network will fill** (10/day), so it binds only as a lever
that can be lowered from a config push.

**The `+` beside a resource opens that resource's panel, always.** It used to pick between
three destinations depending on whether an ad happened to be loaded — the offer panel, the
out-of-hearts gate, or a toast — so the one control on the hub that looks like a question
mark answered a different question each time it was tapped. `AdOfferOverlay` is now the
panel for hearts and for coins in every state, and it leads with **facts read from the rules
rather than written into the copy**: how fast hearts come back (and how much faster while a
boost runs), when the next one lands, that collected hearts stack past five, and how many
videos today's allowance has left. The offer sits under them. `StreakInfoOverlay` is built
the same way for the same reason — a panel that explains the game is the first thing to rot
when the game is retuned. The one refusal that cannot resolve by waiting, a placement the
content table does not carry, drops the watch button entirely rather than greying it: the
facts are still worth the trip.

The watch buttons carry a play glyph in front of the label, centred with it as one block by
`UIKit.FitLabel` — which any path that repaints a caption has to call, because the captions
here are countdowns.

`AdConfig` holds `UNSET` for the app key and every ad unit, exactly as the store secrets do,
so `Boot` keeps the null provider until a LevelPlay account exists. Filling those in, plus
the `LEVELPLAY_SECRET` in Secret Manager and the callback URL on the dashboard, is the whole
remaining step.

**Retention: the run outcome, the streak, the golden glade, the calendar, the percentile.**
Five features that all hang off one seam. `RunOutcome` (Domain/Board) is decided once by
`PlayScreen` when a run ends and handed to everything downstream — the celebration, the
defeat copy, analytics — so a new reaction is a new reader rather than another hand in the
board's state. The victory sequence is a `Cue` (Presentation/App): beats declared as gaps
after one another, because the timing *is* the design and absolute delays had already
drifted into a collision on a three-star win.

The near-miss line is the one worth understanding. `Puzzle.TurnsToSolution` is an **upper
bound** — if it says one, one turn provably finishes the glade — so a defeat that says "one
turn from it" is a claim the player could check by restarting and counting. It returns -1
once a needed conduit has crumbled, because the count over the survivors would read *lower*
than the truth. That honesty is load-bearing: the effect works because the player cannot
catch it being generous.

The **streak** is three dates and no count (`startDay`, `lastPlayedDay`,
`collectedThroughDay`, all merged by `max`, length and everything else derived) — invariant
11b applied before the mistake rather than after it. Save schema **v10**. The **golden glade**
is a per-(account, glade) credit multiplier folded into
derived earnings, so it needs no claim and the server recomputes it; the **event calendar**
is rows in `manifest.json` whose track pays from clears dated inside the window, likewise
derived. Both are pinned by `firebase/shared/reward-vectors.json` — 24 reward, 25 golden-glade,
120 chest vectors — and both mirror in `functions/src/progression.ts`. The **percentile**
on the victory panel comes from `config/stats`, a daily `publishGroveStats` job that samples
5,000 saves; it is drawn upward only, and never below 200 samples.

**Streak rewards are collected by hand, and the page is the grove at night.** A rung used to
be applied the moment a run ended, which meant the reward for a six-day streak arrived as a
number changing behind a defeat screen — nothing about that is a reward. A night now waits on
`StreakScreen` wearing a turning fan of light until it is tapped, and pays out into a throw,
a burst and a stamped seal. Three decisions are worth not re-litigating. The record of what
has been taken is a **third date**, `collectedThroughDay`, because the two obvious
alternatives both fail invariant 11b — a count of collected rungs is hearts' old mistake, and
per-night flags have to be cleared when a streak breaks, which is not monotonic and therefore
not a join. Its **zero is the migration**: starting a run seeds the floor to the day before
it, so a live file's floor is never zero, which lets zero mean "written by the build that
paid automatically" and stops every night of a live streak paying twice on first launch.
And **tapping a later night sweeps the earlier ones with it**, which is the only reading of a
floor that cannot silently drop a reward. The backdrop is `Bg/streak_*` — literally the hub's
own islands, night-graded under a moon — because sharing the hub's daylight sky made the one
screen about nights kept read as a variant of the two screens either side of it. The hub's
streak chip carries a count badge, without which the change trades a reward players did not
notice for one they never take.

**The ladder laps, and it pays currency.** `StreakTable.Rung` wraps: night eight pays night
one's rung, for ever. It used to repeat the *last* rung instead, which meant a tile labelled
"night 8" paid night seven's reward — the board and the table telling the same player two
different things — and it forced the milestone to be small, because whatever ended the ladder
was what every engaged player received for the rest of their life. A lap lets night seven be
the week's peak and still come round again. The shipped lap is seven nights: **150 credits,
1 heart, 5 gems, 2 hearts, a 12-hour boost, 3 hearts, 10 gems.**

Two currency rungs is the part that needed building, and invariant 13 has the argument. In
short: the server cannot recompute a streak, but it does not have to — a night is claimed as
`streak:{day}:{night}:{ccy}`, the grant log bounds it to one payout per calendar day, and
`advances` bounds the night to climb no faster than the calendar. That floor lives on the
wallet document, which no client can write, and `readWallet` seeds it to *yesterday* for a
brand-new account so a fresh install cannot claim a backlog. An existing account's absent
floor deliberately allows one unbounded first claim — that is the migration, and refusing it
would take nights the game has already shown people. The ceilings in `StreakRules` are shared
with `functions/src/streak.ts` and are the per-day cost of a forged streak, so they are an
economy decision. Fifteen shared vectors pin the lap and the clamp; twelve server-only cases
pin `advances`.

Two things changed underneath it. The streak's three dates **now travel with the save** — they
never used to, so a player's flame quietly restarted on their second device, and
`DailyStreak.Join` had nothing to join against. And the seeder **now publishes the ladder**;
it never did, because until now the server had no opinion about a night. Re-seed after any
change to the rungs or the game shows one number and the wallet receives another.

**The board is one lap of the ladder, not the whole streak.** A streak has no end and a ladder
does, so a board pinned to
the first seven nights simply stopped having a tile for night eight, while `Pending` went on
counting it and the hub badge went on advertising it. The board now draws `CycleLength` nights
starting at `DailyStreak.BoardFirstNight`, and every tile derives from its **absolute** night:
night eight reads "day 8", carries `Rung(8)`, and wears the crest if it ends the lap. Two rules
keep it honest. The lap shown is the one holding the *oldest uncollected* night rather than the
one the streak is on — otherwise night seven's reward drops off the board the moment night
eight arrives — and the progress bar measures the lap, never the streak, because a bar against
something unbounded can never fill. `EveryNightOfThreeWeeksIsOnTheBoardAndPays` walks
twenty-one nights and pins all of it. The count over the flame and on the hub chip are both
`Shrinkable` for the same reason: they are not two-digit fields.

`StreakInfoOverlay` (the "i" on the streak header) answers the three things the board cannot
draw: that a night is earned by *finishing* a glade rather than winning one, what fast hearts
actually do, and that nothing stops at the end of the ladder. Every number in it is read from
`StreakTable` and `HeartRules` rather than written into the copy — a panel explaining the game
is the easiest thing in a project to leave behind when the content is retuned.

**The hub's feature row: the streak and the event are two equal boxes.** Both used to be
drawn at the size of chrome — the streak a 232×116 chip wedged into the top bar beside the
settings gear, the event a 56-high strip — and between them they are the two reasons a
player opens the game on a day they had not planned to. They are now 438×300 boxes on one
row under the daily panel, built by `BuildFeature` from a shared shell (`FeatureCard`,
`FeatureHeader`, `FeatureValue`, `FeatureStrip`, `FeatureBar`) so they read as a system.

Three decisions are worth not re-litigating. The **right box holds the event or the next
companion**, which is not a fallback bolted on: the old strip already chose between exactly
those two and hid the loser, so the only change is that the loser takes the slot when the
winner is absent — and there is no state of the screen with one box and a hole. The **card
is the button** rather than carrying an invisible one over it, so a press squashes the whole
box; there is still no plate behind the row and no rule between the two, because each box
earns its own contrast the way the nav caps do. And the row **cost the hero a tenth of its
size and 226px of height**, which is what the space was worth.

A box holding something collectable **lights its border** (`FeatureBeacon`): a gold seat of
light behind the card that breathes, a gold rim brightening over the resting one, and a
cream ring that steps 18px out of the border and fades. Three layers because each does a
different job — the seat is the only part visible from the far corner of the screen, the rim
says the border is lit rather than merely coloured, and the ring is the "tap me", because
motion that *leaves* the shape is what the eye catches peripherally. The corner badge is not
redundant with it: a 54px disc is something you find once you are already looking at the
row, and the whole problem with a collect-by-hand reward is getting somebody to look at the
row at all. The ring is **cream, not gold** — the first version travelled gold out of a gold
rim into a gold glow and was invisible on the screen while reading perfectly in the source.

**An event wears a mark, and the mark is content.** `manifest.json` events take an optional
`icon`, resolved by `EventMark`. It names a mark the client knows how to draw, *never* an
art path — see *Events* in `CONTENT.md` for why that distinction is the only one that
survives invariant 7, and why an unknown name draws the default instead of refusing the
event. The one that ships is `Art.Bloom`: a rose curve rasterised to a distance mask, so it
tints and needs no address, and **its openness is the event's progress** — a tight bud at
none of the track finished, a full flower at the end of it. The three-star silhouette it
replaces was wrong twice over, being both the vocabulary of a *glade's* reward and unable to
say anything about where the player was.

**The event is a vine you climb by hand — save schema v11.** `EventScreen` replaces
`EventOverlay`, which is deleted. The panel it replaces *reported*, correctly, because until
v11 an event milestone landed in the balance the instant the glade was cleared and there was
nothing on it to do. Now a rung opens when it is reached and waits until it is tapped, which
is the change v10 made to the streak, for the same reason, in the same shape: one monotonic
floor per event — the largest goal taken — keyed by the event's permanent id, merged by
`max`. See invariant 14a; the arithmetic stayed derived and only the moment moved.

Four decisions are worth not re-litigating. The floor is **clamped server-side to the glades
the same derivation counted** (`eventCredits` in `functions/src/progression.ts`), so a
client-written number that decides a payout is still safe: forging it takes early what play
had already earned and nothing more. Nothing here **can** pay twice — a balance is
`max(derived, earnedFloor)` and collecting only raises `derived` — which is why
`SaveFileDto.eventsSeeded` is a courtesy rather than a safeguard: it marks a pre-v11 file's
reached rungs as already taken so a returning player is not offered flowers whose credits are
already banked. A **closed window stops progress and never takes a bloom**, so
`GroveEvents.Featured` keeps the hub's box while anything is uncollected. And the page **lays
itself out from the goals**: rungs sit along the curve at `goal / finalGoal`, so a track of
any shape up to `EventRules.MaxMilestones` draws itself and two rungs placed close together
are drawn close together.

Visually it is a vine rather than a fourth row of tiles, and the backdrop (`Bg/event_*`) is
the ground the hub's islands float above at first light — a place, not a third grade of the
same landscape, because two re-lights of one scene is a mood and three is a filter. Three
rung states are three flowers: a tight bud on a dry stem, a half-open bloom under the same
turning gold fan the streak uses, and a full flower with a seal on it. `Art.Bloom` quantises
its openness to eighths precisely so the middle one can be tweened into the last.

**Companions are bought as well as earned — save schema v12.** The roster used to be
level-gated only, and the arithmetic said that was a paywall by accident: three-starring a
hundred-glade catalog reaches about keeper level 15, the gates run to 66, so **18 of 31
companions were unreachable by any route for over a year**. Coins are now the second route.
`CompanionLedger` owns the composite rule (invariant 15a); `unlockCost` in `manifest.json`
owns the price, so a drop retunes the whole ladder with no app update.

Four decisions are worth not re-litigating. The purchased set is **stored** — the first
non-derivable thing in the save — as a union-joined set of ids, which is invariant 15 and the
reason it is safe. Only **monarch** is free now: every other gate moved up one so a new player
starts with exactly one companion and thirty things to want. The ladder is **800 → 30,000
across 30 companions** (~270,000 credits, about sixteen months of ordinary play), pinned at
both ends — a gated companion priced under half the 1,250-coin account seed is a build error,
and `Validate Content` derives the ~540/day income from `progression.json` and reports the
roster in days, so nobody has to guess again. And a short balance **opens the coin offer
instead of greying the button**: that is the moment a player has decided they want something,
which is the best moment in the game to offer a video and the worst to teach them a control is
dead. `CompanionUnlockOverlay` leads with the free route regardless.

**A companion arrives as a reveal, not as a line on a receipt.** The purchase panel closes and
`CompanionRevealOverlay` takes the screen: the room blacks out, three rings collapse inward, a
flash and a shockwave break it open, and the friend lands out of a counter-rotating fan of light
with stars, a stamped name and confetti. It is a `Cue`, because the timing is the design — the
pause before the flash is the whole trick. Three decisions are worth not re-litigating. The
spectacle **scales with the companion**: a tier derived from the unlock gate (1–5) drives the ray
count, the star count, the confetti and the colour, which runs the rarity ladder every player
already knows — pale, green, blue, purple, **gold last**, because gold is what this UI already
means by "best" and the first version wasted it in fourth place. Everything is **built hidden and
then revealed**, never built by the beats, which is what makes `Skip` one pass of assignments
instead of a second choreography that would drift out of agreement with the first — it is
skippable from frame one, because this is seen thirty times in the life of an account. And it
needs **no new art**: the room, fan, shockwaves, glow and vignette are `Art.Gradient`/`Rays`/
`Ring`/`Glow`/`Vignette`, for the reason `Art.Bloom` is procedural — a reveal that scales with the
roster cannot wait on a sprite per companion.

**The reveal happens in a coloured room, and one colour was not enough.** It first staged
everything against near-black, which is precisely what a screen looks like when its art has failed
to load — so the most expensive thing in the game arrived looking like a loading error with a
portrait on it. `Chroma` replaces the single tint with **three palette colours per tier** (tint,
partner across the wheel, warm accent) plus a deep hue of the tint's own family, and every one of
them comes out of `Pal` so the loudest screen in the game cannot drift away from the game's palette
by inventing shades of its own. What that buys: a **gradient sky** lit from below by the partner,
**three slow aurora masses** that flare on the impact, a **second counter-rotating fan** for every
tier rather than only the top two, shockwaves and collapse rings that **cycle the scheme** instead
of repeating one colour, a **chroma pulse** the white flash resolves into, a **rim that drifts
between tint and accent** for ever, and a second confetti fall for tiers 3+.

Three notes for anyone editing it. `Art.Gradient` is the second generated shape carrying its own
colour (`Gem` is the first) — a full-screen layer costs the same fill rate opaque or nearly
transparent, so three stops in one draw is a third of the price of three washes. The resting
brightnesses are **named constants** because `Play` fades up to them and `Skip` assigns them
directly, and them agreeing is the only reason `Skip` works — they were four pairs of repeated
literals. And the endless ambient loops (`Drift`, `Spin`, `Counterspin`, `Hue`) are **owned by
their own object and channelled**, so `Skip`'s `KillAll` cannot stop the room moving and reaching
the resting state twice replaces a loop rather than running two of them out of step.

Both roster screens repaint on **`CompanionLedger.Changed`** rather than on a callback from the
panel. The callback was the bug: it only fired from the "wear" button, so a player who bought a
companion and then dismissed with the corner cross or the scrim saw it still padlocked until they
left the screen and came back. A panel with three exits reports through none of them reliably; an
event cannot be forgotten.

**A glade keeps its standing — save schema v13.** The percentile the victory panel quotes used
to appear once and evaporate; every cleared node on the map now wears the band it earned
(`ui.rank.top10` / `top25` / `top50`, drawn by `LevelsScreen.RankMark`). One int per level,
`LevelRecordDto.bestRank`, and it is the first thing in the save file derived from a *population*
rather than from the player — which is the whole difficulty, because a population moves:
`publishGroveStats` re-reads 5,000 fresh saves a day, and a game that grows tenfold grows a faster
field with it.

That makes both obvious rules wrong, and `RankTests` exists to keep them out. **Recomputing for
display** means a node quietly reading 66% next month where it read 71% today — a score the player
earned, silently decaying, which reads as the game having lost it. **Freezing whatever was current
when the record was set** is worse: a player who returns and beats their own move count against a
bigger field is *demoted for playing better*, and that is the one signal a progress display must
never send. The rule is therefore **promotion by `max`** — the best standing ever held against any
population ever published — which is invariant 11b's shape for the sixth time. Three consequences
follow. The copy says *"TOP 25%"* rather than *"15 moves beat 70% of keepers"*, because a band is a
standing the player holds and a sentence coupling a rank to a run would be a lie once the rank
outlived that run. `LevelStats.MinRank` is 5, so zero is unreachable for a real standing and a v12
file reads as unranked — **this is the first section to need no migration code at all**, since the
move counts it derives from were already on disk and `PlayerProgress.RefreshRanks` backfills a
whole account the first time a table lands. And the standing is taken over the *record* after a run
is folded in, never over the run's own moves, inside `LevelRecord.WithRun` where no call site can
get the order wrong.

Two smaller decisions. `IsWorthSaying` now reads its floor from `RankTier`, so the map and the
victory panel cannot come to disagree about whether a glade went well. And the mark sits directly
*above* the disc rather than in a corner of it: `mapX`/`mapY` are authored and `ChapterMap` proves
nodes clear each other using the perch's own footprint, so anything growing sideways could validate
green and still touch its neighbour on somebody's phone.

**A run is timed, and the node reports the record — save schema v14.** The map badge used to
read `TOP 25%` and nothing else, which is a comparison with no result attached. It now carries
the player's own record underneath — `31 turns · 2:14` — and the record half is drawn **whenever
a glade is cleared**, band or no band. That is the fix for the thing that made the standing feel
hollow: a percentile needs hundreds of players before it can say anything, and until then the
node had nothing on it at all.

`RunClock` (Domain/Board) is the stopwatch, and three decisions are worth not re-litigating. It
is an **accumulator handed time a frame at a time**, never two readings of a wall clock — a
player who takes a call comes back to a forty-minute record on a two-minute glade, and nothing
about elapsed real time describes what somebody did. **Every tick is clamped** to
`RunClock.MaxTick`, because a resume, an asset load or a rewarded video each arrive as one
enormous `deltaTime`, and a best time only ever falls, so one bad reading is a record no honest
run can ever beat again. And it starts on the **first conduit turned**, not on the board
appearing: a player who studies a glade is not doing worse than one who spins tiles at random.

It is ticked from `PlayScreen.Update` while `!Locked`, so a pause costs nothing and a
backgrounded app contributes nothing because no frames run. The start edge is found by
**polling the move count** rather than hooking the turn — `OnChanged` also fires for undos, so a
subscription would have to re-derive the edge anyway and would be one more thing to unwind.
`ResetClock` is on every path that hands over a fresh board, and the bottom bar's restart button
was rerouted through `RestartLevel` so it and the pause menu cannot disagree about what a
restart resets.

Stored as `bestMillis`: smaller wins, zero is absent — the join `bestMoves` has always used, for
the same reason. **Milliseconds rather than seconds** so zero is unreachable for a real run, since
a one-turn board genuinely resolves inside a second; that is v13's sentinel argument again. Unlike
a standing it **cannot be backfilled**, because nothing already stored implies how long a past
clear took. `WithTime` is a separate fold from `WithRun` on purpose: moves and milliseconds are
both `int`s describing the same run, so adjacent parameters could be swapped, compile, and write
"31 milliseconds" into a permanent record.

`RunOutcome` now carries both `Seconds` (screen time, for analytics — it includes staring at an
untouched board) and `Millis` (play time, for the record). Anything shown to a player wants the
second.

**The clock counts down, and it can end a run — save schema v14, unchanged.** A glade now carries a
time limit and the run is lost when it reaches zero: `DefeatReason.OutOfTime`, a heart, the defeat
panel. Three decisions carry the whole feature.

The limit is **derived from par**, not authored and not flat — `LevelTuning.TimeFactor`, seconds per
par turn, default 2.0, with the same convention `BudgetFactor` uses (0 means "not authored" and only
a negative value removes the clock, so a level cannot lose its fail state by omission). A flat sixty
seconds is a different difficulty on every board: comfortable at par 34 and close to unwinnable at
par 49 two glades later, and nothing about authoring a number per level makes that visible to whoever
authored it. The shipped four come out at 68s / 98s / 76s / 92s.

Stars are the **worse of two readings** — `LevelTuning.StarsFor(moves, millis)` takes
`min(StarsForMoves, StarsForTime)`, with the clock's own gold and silver at 50% and 75% of the limit.
Three stars therefore means efficient *and* quick, a claim the player can check against either number
on the victory panel; a blended score would be a third number nothing on screen shows, and a player
who lost a star would have no way to know which half cost it. There is deliberately **no moves-only
overload** left on `Puzzle` — a caller that could ask for half the rule would get an answer that is
right until a glade is timed, and the compiler would never mention it. Zero milliseconds means "never
timed" and costs nothing, which is the convention `BestMillis`, `RunClock.Millis` and the save file
already share.

And the clock **starts when the board becomes playable**, not on the first conduit turned. It began
as the latter, which was right while this was only a record — a player who studies a glade is not
doing worse than one who spins tiles at random — and wrong the moment it became a limit: a countdown
a player can hold at full by not touching anything lets them plan the whole solution for free and
then execute it, which removes exactly the pressure the limit exists to apply. The edge is polled off
`BoardView.Locked`, so the clock still does not burn during the raise animation, and `Expired` stays
gated on `HasStarted` — without it an untouched board already satisfies `Millis >= LimitMillis` at
zero and the run is lost on the frame it appears. Keeping the edge in `PlayScreen` rather than inside
`RunClock` is what made that reversal one line.

**What the countdown did *not* touch, and why that was the point.** What is measured and stored is
still **elapsed play time**, never time left — remaining is derived for the HUD and reaches nothing
else. So `LevelRecordDto.bestMillis` keeps its meaning, the map badge keeps reading `31 turns · 2:14`,
`SaveMerge` is untouched, and `publishGroveStats` needed **no change and no deploy**: it publishes
deciles of `bestMoves`, the standing still answers "you used fewer turns than X% of keepers", and
every already-published `config/stats` table stays valid. If a change ever makes the save hold time
*left* instead, all of those break at once and silently, because both are milliseconds and both look
plausible in a save file — `CountdownTests` exists to catch it.

Two smaller consequences. `RunOutcome` carries its own `TimeLimit`, so the defeat panel can put
`Millis` in proportion without reaching back into a `Puzzle` the retry button has already restarted —
the hazard that type exists to remove. And `TurnsShort` now answers after a timeout as well as after
the move budget: it is refused only after a **crumbled conduit**, because that is the one ending where
the count over the survivors reads lower than the truth. A timeout leaves the board intact, so "one
turn from it" is exactly as sound there — and it is the ending where it drives a retry hardest.

**A run that has begun is paid for, however it ends — `RunGuard`.** A heart was charged when a run
was *lost*, so every way of ending one without losing it was free: the restart button, the back
arrow, the pause menu's two exits, and killing the app. That barely mattered while only the move
budget could end a run, because running out of turns creeps up on a player. A countdown does not —
it is a visible, reliable cue to tap restart one second before the loss lands, and a gate anybody
can step around on a whim is not a gate. So a committed run now costs a heart whatever ends it.

**Committed means the first turn, or three seconds of clock, whichever comes first.** Both halves
are needed and neither is arbitrary. Waiting only for a turn lets a player study the board for the
whole countdown, back out free, and re-enter knowing the answer — which is exactly the free planning
that moving the clock's start edge was meant to stop. Committing the instant the board appears would
charge somebody who opened the wrong glade and left within a second. Three seconds is longer than a
misplaced tap and far shorter than reading a 6×7 board.

**The deliberate exits ask first** (`ForfeitOverlay`), and only on a committed run — a confirmation
over a free action is friction that teaches players to dismiss the one that is not free. All five
paths route through `PlayScreen`, including the pause menu's *Glades* and *Home*, which used to call
`Flow.Go` directly and were two of the five holes. The green button is **staying**: green is this
game's affirmative everywhere else, and the affirmative here is "keep playing".

**The ending no screen ever sees is handled on disk.** A force-quit, an OOM kill, a crash and a flat
battery are indistinguishable from each other and from each other's intent — no client can tell them
apart and neither could a server — so the run is written down when it commits and rubbed out when it
resolves. Anything still written down at the next launch is charged in `Boot`, straight after
`SaveService.Load()` and before anything can start a new run. Three details carry it:

- **`PlayerPrefs.Save()` is mandatory, not an optimisation.** `SetString` writes to memory and Unity
  persists that on a *clean* quit — the one exit this does not care about. Without the explicit flush
  the marker would be lost by the very crash it exists to catch.
- **It is deliberately not in `SaveFileDto`.** "A run is in flight" is a fact about this *device*, not
  the account: merged, it would charge a player on their tablet for a run open on their phone. It also
  goes up and down, so it could never be joined — invariant 11b, straightforwardly.
- **Nothing about it needs the network.** The marker is local, the charge is local, and the charge
  survives to the cloud because hearts are a produced/spent ledger merged by `max` — a spend made in
  airplane mode only ever raises `spent`, so no device that was absent for it can undo it. Offline is
  not a way out.

**Two smaller decisions.** A forfeit costs a heart and **nothing else** — no daily-chest credit and no
streak — because those are for runs that were *finished*, won or lost, and a withdrawn run was not;
the line is easy to explain and it stops the restart button being the fastest way to bank three
chests. And the charge is **reported on the next launch** rather than left to be noticed, because a
resource that quietly decrements is a resource players feel cheated by later — the rule the defeat
panel's heart row was already built on, and it applies twice as hard to the one charge in the game
the player did not watch happen.

**What this cost, and what it did not.** No save schema change (the marker is not in the save), no
`progression.json` retune (an honest player still pays exactly once per attempt that did not end in a
win), and no server work. `RunGuardTests` pins it, in the Editor rather than offline: a fake store
would prove the arithmetic and not the thing that matters, which is that the marker reaches disk
before the process dies.

**The tap rate is the number that decides whether a glade is fair.** Because gold moves are
`par × GoldFactor` and gold seconds are `par × TimeFactor × TimeGoldFraction`, par cancels: the rate
three stars demands is a fact about the three factors alone, `1.35 / (2.0 × 0.5)` = **1.35 taps a
second** at the shipped defaults. Demanding on a first attempt, comfortable on a replay, which is the
shape a three-star threshold should have. `LevelValidator.CheckClock` warns (never errors) when a
level overriding one of those factors pushes it past 1.8 — drumming rather than solving — or needs
more than 1.2 taps/s merely to finish, which is unwinnable rather than hard. Both `Validate Content`
and `Tools/verify/content.py` print the clock and the rate per glade, so nobody has to derive it.

**The mark is a struck medal over two lines, because a caption is not a reward.** A ranked glade
gets a 392×196 plate: a trophy on a filled disc with a cream rim and a halo, `You are in the top
10%`, then `31 turns · 2:14`. **A trophy and not a star**, and that is not decoration — the node's
own disc art *is* the star rating (`node_s1`/`s2`/`s3`), so a gold star 100px above it would be the
same symbol counting a different thing, and players would read it as a fourth star. One glyph in
three colours rather than three glyphs, because a medal ladder needs no teaching. The rim is cream
on every tier: ringing a bronze medal in bronze makes the rim vanish, which is the beacon's old
gold-out-of-gold mistake. Only the top tier breathes and only it gets rays — motion is the loudest
thing on a map of bobbing rocks, so spending it on every ranked glade singles out none. An unranked
glade keeps a single quiet line with a tick, because dressing a median run as a trophy is how a
trophy stops meaning anything.

Two layout lessons are worth not relearning. `UIKit.Box` **always** pivots at centre, so anchoring
a child to a container's top or bottom edge puts half its height outside — that is what had both
lines hanging out of the pill. And `UIKit.Label` defaults to `HorizontalWrapMode.Overflow`, which
has no clipping at all: an over-long line is not truncated, it simply keeps drawing. Anything
holding a translated string needs `UIKit.Shrinkable`, which is why both lines have it.

One risk this leaves, deliberately unfixed: **`ChapterMap.MinimumNodeSeparation` is 220px** —
derived from the 196px disc, and it knows nothing about what rides above it. The shipped maps sit
756px apart vertically, so the mark (302px of reach) is comfortable there, but a future chapter
authored near that floor would overlap. Raising the guarantee is a content-authoring decision, not
a layout one.

**The keeper's route — self-comparison, and no longer a second screen.** The victory panel measures
the turns a player took against `Puzzle.TurnsToSolution` captured on the untouched board, and it is
the first thing here that compares a player to *themselves* rather than to a population. It needs no
backend, no sample floor and no other players: it works on a fresh install, offline, on every glade.

**The route is beatable, and the copy must never call it perfect.** `TurnsToSolution` is the
distance to the *authored* solution, but a glade is won when every lamp is lit — spare conduits may
be left pointing anywhere. So a player can finish under it, and the live save that prompted this
does: 31 moves on a glade whose route is 34. There are therefore **three readings** — over, exact,
and under — and the third is the rarest and best thing the panel can say. `RouteTests` pins it
precisely so nobody "fixes" the negative case away on the assumption it cannot happen.

**One panel, because two was a tax — `RouteOverlay` is deleted.** The measurement shipped as its own
overlay slipped in front of the Next button, which meant the one control labelled "next glade"
answered a different question the first time it was tapped. That is exactly the mistake the hub's
`+` buttons made before `AdOfferOverlay` became the single destination for a resource, and it failed
the same way: a tap for a panel nobody asked for, then a tap for the thing they did. `WinOverlay`
now owns the comparison, lives in its own file, and the button goes where it says. Nothing was cut.

Three consequences worth not re-litigating. The **bars are drawn on every win that has a route**,
because merging made them free — no tap, no navigation — while the *sentence* under them stays
upward-only. So `RouteWorthSaying` **gave up its personal-best clause**: it bought a whole panel
once, and a rule that hands a new record 90 turns over the route the line "56 turns from a perfect
route" is a scolding printed beside a stamp calling the same run the player's finest. The stamp keeps
the recognition. And the whole thing is still **derived, with no save state at all** — the route is a
fact about the glade and the moves are already in hand, so v14 remains the schema.

**The layout is a stack with a cursor, and the whole block is fitted to the screen.** The panel's
height depends on what the run earned — a hint, two bars, a verdict, a standing, a payout, a golden
line — so it is measured before anything is built and each optional row buys its own pitch. That
replaces a fixed `PanelBase` plus fixed `PaidRoom`/`GoldenRoom` additions and a separately derived
`rewardY`, which agreed only while nobody edited one of them. The measured block then goes inside a
`Fit` node scaled to `Flow.Size.y`, because the canvas is **width**-matched at 1080: its height is
1920 on a 16:9 phone, 2400 on a tall one and **1440 on a 4:3 tablet**, where the old worst case
already overflowed. The scale lives on a parent and never on `Panel`, because `ModalView.Close`
scales that one to an absolute 0.82 — a panel resting at 0.94 would visibly *grow* on the way out,
and every `Tween.Pop`/`Punch` on a child writes absolute local scales too.

**What the panel says, in the order it says it.** Crest (crown over a banner carrying the rank
word) → three stars in an arc over a shadow seat and a gold shine → a wax seal stamping `NEW BEST`
over the row's shoulder → `three stars at N turns` when the run took fewer than three → `YOUR RUN`
with `31 turns · 2:14` and a gold bar → `THE GROVE'S ROUTE`, an **"i"**, a dim bar and a carved
marker holding its number → the verdict line → the standing medal → the payout chips → the golden
line → replay, **NEXT GLADE**, map on one row. Both bars share one scale, which is the entire readability of the
comparison: the shorter bar is the better run and no number is needed to see it. A run *over* the
route continues in `Pal.Amber` past the gold, which says "these turns were spare" without the panel
saying it in words. A screen flash fires on the third star only — spending it on every win marks out
none.

**No confetti and no haptic anywhere in the win, and that is now a rule.** Both were tried on the
panel and both are gone. The thing worth not rediscovering is *why* they were cheap to lose: the
board has already flashed, sounded the fanfare and (before this) thrown confetti and buzzed when it
solved — `BoardView.Celebrate` — and the panel opens about a second later. Firing either again is one
celebration stuttering rather than two, so removing it from one place only would have looked like a
half-finished deletion. The panel's fanfare went the same way for the same reason: `Audio.Sfx("win")`
plays once, on the board. `Payout` no longer buzzes either — it is a two-chip payout on a screen
players see dozens of times a session, and `Handheld.Vibrate` is one fixed-length buzz on Android, so
there is no way to make the second one lighter than the first. `Burst.Confetti` still has four
callers (chest, companion reveal, event), so it is not dead.

Two things the old panel got wrong that are worth naming. The **four dark scorecard slots** are gone:
a run produces a handful of numbers that mean different things, and set as identical framed rows they
read as a receipt. And the record was a **sentence** ("a new best for this glade") under a row of
stars, where it is a footnote; beating your own record is an award, so it is a stamp.

**The route's "i" is a bubble, not a fourth modal.** `StreakInfoOverlay` and `EventInfoOverlay` are
full panels and they earn it — three questions each about a whole screen. This answers one question
about one row, so it is a cream popover hanging **below** the row, which is what keeps both bars
visible while it is read; a panel would cover the thing it describes and make the player close it to
check. It lives inside the `Fit` node with an oversized tap-catching veil, so it scales with the
panel on a short screen and no corner of the screen is left where a tap does nothing. The dot's `x`
is **measured** from the caption's `preferredWidth` and clamped to the caption's own box, because the
caption is a translated string and a constant `x` either collides with a long one or floats away from
a short one. Back closes the bubble before it closes the panel. The copy has one hard constraint: it
must never call the route a minimum.

**The victory art is CraftPix, minus two regions.** `Ui/Win/` holds crown, shield, blank banner and
window, cut from the win pack's atlas — which turned out to keep every part as a separate region,
including the `VICTORY` lettering. Two are **deliberately absent from the project**. The lettering,
because a word painted into a texture can never be localised (invariant 6), so the blank ribbon
carries a loc key instead and ships in every language. And the **herald's horn**: two of them flanked
the crest and were cut, because the crest is the one thing here that has to read in a quarter of a
second and three gold shapes at three angles is not that. The art went with them rather than staying
addressed — an addressed sprite nothing draws is still built into the bundle and preloaded at every
launch, which is the `Fx/Victory` mistake and there is no reason to make it twice. The crown then
moved up 26px to rest on the banner instead of sinking into it; `CrestReach` moved with it, and has
to, or the fit stops reserving the room the crown needs.

**A ribbon's flat face is not the ribbon's centre.** The rank word was centred on the banner sprite
and hung off the bottom of the red onto the draped tails. Measured from the art: in the 361×100
source the face runs y 2–54, so its middle is 22px above the sprite's, and the sprite draws at
1.568× — hence `RankLift` of 34. The width came out the same way; the face is 231 source pixels of
red at its own middle, so `RankBox` is 356 rather than the 430 that let a long translation run out
over the folds. Any art placed inside a painted shape here wants measuring rather than centring —
`UIKit.PillFaceLift`, `SquareFaceLift` and `NodeFaceLift` are the same lesson already learned three
times. Taking the parts also made the pack's 80-frame 805×572 flipbook
unnecessary — the crest is composed and animated with `Cue`/`Tween` the way the companion reveal is,
for a few KB instead of megabytes.

Three art defects were fixed with it, and the first was doing real damage. **`window.png` had no
nine-slice border**, so a 900-wide panel over a thousand tall stretched a 720×642 sprite past twice
its aspect and smeared its corners and its inner hairline — most of why the panel looked cheap. It
now carries a 48px border and its header tab (which nothing ever drew) is cropped off. **`shield.png`
used the atlas rectangle verbatim**, which left the plaque on its side *and* bled a sliver of the
second horn into one edge; re-cut from the rotated region it is a proper shield, and it is what the
grove's own number rides at the end of its bar. The horn was cut before it shipped (above).

**Verifying is now in the repo.** `Tools/verify/` holds `compile.py` (every assembly
separately, which is what actually proves the layering), `tests.py` (the EditMode suite via
a reflection runner — 749 pass offline, 97 need the Editor and say so), `content.py`,
`loc.py` and `names.py` (the keeper-name fold, run on Unity's own Mono so a divergence from the
server's copy reproduces offline). It no longer has to be recovered from a scratchpad.

**The keeper's name reaches the cloud — save schema v15.** Reported as "renaming does not
save", and it was two separate faults meeting. Invariant 11c has the argument; what follows
is what changed.

The merge now dates a preference by **its own stamp** rather than by the file's, and an
unnamed keeper **stores nothing** rather than storing `Grovekeeper`. Between them those two
lines are the whole of why a rename used to evaporate: a device that had never been renamed
looked exactly like one that had chosen the default, and it looked *newer* than every other
device on earth, because the snapshot the sync merges against is stamped with the moment it
was taken. So the fresh install won, and pushed. The stamps are `displayNameSetUnix` and
`avatarSetUnix`; zero means "never chosen", which is unreachable for a real choice, so a v14
file needs no migration — the one ambiguity it does leave, an *undated* default name, is read
as never-chosen by `Wallet.ReadChosenName`, which is the safe half (the player still sees
Grovekeeper, and the device stops outranking one that was renamed).

The second half is **when** a sync runs, which was: after the splash, on backgrounding, and on
returning. That is fine for progress the player will change again in a minute and wrong for a
choice they make once. Backgrounding is the *worst* moment to start a network call — the
process is being frozen and the continuation may not run again for hours — and a sync that
failed was simply forgotten, so a rename made on a train pushed nothing and pushed nothing
again when the signal came back. `SyncScheduler` (Domain/Cloud) is a debounce with a backoff
and a reconnect: 3s coalescing so a rename plus a companion is one write, doubling retries
from 5s to a 5-minute ceiling, and a sync when reachability returns **whether or not anything
local is pending**, because the point of reconnecting is as much what the other device did.

Four decisions worth not re-litigating. It holds **no clock and no socket** — it is handed
elapsed time and told whether the network is up, which is `RunClock`'s bargain and the reason
the whole policy is pinned by `SyncSchedulerTests` offline; `Boot.Pump` reads
`Application.internetReachability` and ticks it. The pending mark is consumed **when the
snapshot is taken**, not when the push returns, so a rename made during a push survives that
push succeeding — otherwise the fix would still lose a name to one unlucky second. Contention
is a **first-class outcome** (`CloudFailure.Busy`) rather than an error string, because "a
sync is already running" must not back the timer off for five minutes. And the trigger is
`Wallet.ProfileChanged`, wired once in `Boot`, rather than a call in the rename panel: this
file has already learned twice that a step someone has to remember gets forgotten.

Nothing on the server changed and nothing was redeployed. The rules never constrained the
wallet map's inner keys and no function reads a name.

**Six glades, and two new ways to think — no second game type.** The chapter was four
glades of one verb: turn a conduit, light a critter. The answer to "this will get boring"
is not a second mode — a second mode means a second solver, a second par derivation, a
second star rule and a second thing the build gate has to prove, forever, per mode, and the
games that ship five hundred levels off one verb never did it. It is **modifiers to the one
verb**, which is what `~` (brittle) and `!` (rooted) already were. Two more, and both bend
the existing shape rather than adding to it.

**Taproots (`&A`) make a tap stop being local.** Every conduit carrying a rune turns as
one, however far apart they sit. Nothing about how light travels changes — it is
`Puzzle.Turn` and `TurnsOwed` and nothing else. Three decisions are worth not
re-litigating. A root is **charged once** in par, because one tap moves them all, so a
bound board's par is *lower* than its tile count suggests and the move budget and the clock
(both multiples of par) follow with no authoring — bindings are a resource the player learns
to want, not only an obstacle. The root's turn count is **not the largest of its members'**:
a straight conduit reads the same every half turn, so it is solved at two of the four offsets
and simply follows whatever the elbows demand, which is where every interesting board lives
and is the thing a naive implementation gets wrong (`AStraightConduitOnARootFollowsTheElbow`
pins it). And a root that can **never agree** is a build error, the same class of trap as a
brittle conduit owed more turns than it survives: unwinnable, and it looks perfectly authored.

The mark is **pale rope, one shade for every root, with pips for identity** — never a hue.
Every other tint a tile can wear is an `EnergyColour`, so a coloured root would be claiming
to be a colour of light, and the board's whole language is that colour means energy. The
fast answer is tapping it: the partners pulse, and nothing else on a board answers a tap
somewhere else, so the rule teaches itself on the first tap rather than on a lost run.

**Duskcaps (`x`) are the first thing on a board you are trying not to reach.** Any light at
all wakes one, and a glade with a woken duskcap is not finished however many critters are
awake. That is **one term in `Won`** — no second graph, no second traversal, no save field.
Waking one is recoverable and deliberately cheap (a sound, a shake, no heart), because
exploring is how the mechanic is meant to be learned and the clock already charges for time.

Authoring them is mostly a consequence of a rule that was already there: every arm mates in
the solution, so a lit cell's neighbours are lit, so a duskcap and its conduits must be
**their own island of dark**. Which means the danger is never the duskcap — it is every
turnable tile where the dark island runs alongside the live network, and **rooting a duskcap
makes it safer, not more dangerous**. The design work is winding the dark island through the
live one. Visually it is a critter played backwards: asleep it breathes, greyed, under a
violet moon; woken it snaps to full colour, stops moving, and a rose ring closes on it.

The two glades: **Bound Roots** (6×7, par 45) braids a red river and a blue one so every red
branch has a blue one within a tile, then lays three taproots across the braid — one pair
touching (the lesson), one pair in opposite corners (the consequence), one pair either side
of the middle at three turns (the cost). **Duskcap Hollow** (7×7, par 57, the biggest board
yet) winds an eleven-conduit dark root through the middle of the live network, and its one
taproot binds a live tile to a tile *on that dark root* — the two turns that put the live one
right pass through an orientation where the dark root's arm swings up to meet it. The trap is
on the way to the answer, and it is recoverable, which is what makes it a lesson.

Both tips are authored the way every tip here is: **nothing**. `MechanicScan` reads the
board, so any chapter shipped in three years that uses either one is covered. The duskcap is
taught before the taproot when a glade brings both, because a duskcap changes what winning
*is* and no board can demonstrate that, while a taproot announces itself on the first tap.

**A cream halo was a colour, and it should never have been one.** Reported from play on
glade 5: "are the white-ringed critters a bonus?" They are `#A` critters, which accept any
light — and every *other* halo on a board is an `EnergyColour`, so cream read as a fifth
colour rather than as the absence of a demand. It had been invisible for four glades by luck:
glade 1 is entirely `#A` so there is nothing to compare against, and glades 2–4 have none at
all. **Glades 5 and 6 are the first boards where an unfussy critter sits beside a fussy one**,
which is precisely where the distinction starts to matter.

Fixed in art rather than in words, so no translation carries it. An unfussy critter sleeps
under `Art.PrismRing` — the three channels as three blended arcs, saying "any of these" — and
when it wakes it takes **the colour that actually reached it**. That second half is the part
that teaches: the rule is demonstrated by the first conduit the player turns. A fussy critter
is untouched, wearing its demand lit or not. `PrismRing` is the third generated shape carrying
its own colour after `Gem` and `Gradient`, for their reason — a tint multiplies, so three
colours painted white and tinted come out as one. The halo now tracks *energy* rather than
only lit-ness, because blending a second heart into an unfussy critter's network changes its
colour without waking it twice.

**The Shallows is finished: ten glades, and four of them are combinations rather than new
rules.** That was the whole bet — that one verb sustains a chapter if each board has a
distinct *idea*, not just more tiles. What the last four are for:

- **Lantern Ring** (7×7, par 43) is a *shape*: one closed ring of light with the crystal set
  into it, eight critters spurring outward and a dark star asleep inside. The ring's four
  corners share one taproot, so its outline is a single control — and the tiles that can leak
  light inward are the straight edges, never the corners. So the control you have most of is
  the one that cannot wake anything. Deliberately open and iconic after two dense boards, and
  deliberately the lowest par since glade 3.
- **Sleeping Thicket** (7×7, par 53) puts colour and darkness on the same tile: a red river,
  a blue one, and a five-duskcap thicket wound between them that for most of the board *is*
  the wall keeping them apart. The tile that would let red reach a blue critter is usually the
  same tile that would wake something.
- **Three Springs** (7×7, par 48) has no duskcaps and no brittle conduits at all — five
  crystals, three networks, and the trap is that two of them already share a channel. Gold is
  red+green, teal is green+blue; join those and both go white at once, so the mistake costs two
  networks rather than one. The chapter's oldest idea taken as far as it goes.
- **The Grovekeeper's Knot** (8×7, par 61, the widest board) is the finale and holds
  everything, with one taproot reaching into all four quarters so nothing can be solved a
  corner at a time.

Two notes for whoever authors chapter 2. The pars run 34, 49, 38, 46, 45, 57, 43, 53, 48, 61 —
**not monotonic on purpose**, because par is length rather than difficulty and ten rising
numbers read as a treadmill. And the finale's clock lands at 122s, which is the ceiling: the
limit is `par × TimeFactor`, so past about par 70 a glade needs a `timeFactor` override or it
becomes a three-minute run on a phone. Nothing warns about that yet — `CheckClock` has an
opinion about tap *rate*, not about duration.

Chapter length was decided by the map art, not by taste: `Art/Map/strip0..5` are exact 1200px
slices of one CraftPix island map walking upward from its bottom edge, and the source holds
six. `strip5` is the top of it and 10px short, so its first row is repeated — that lands above
the highest glade under the end-of-chapter marker, where the sky is flat colour, and it keeps
every strip exactly `ChapterMap.StripHeight` so the map's arithmetic stays integral.

What this cost: no save schema change, no `progression.json` retune, no server work, and no
new concept in the reward path — the glades pay exactly what any glade pays. The duskcap is
the only new art (18 frames, global, ~40 KB); the root mark, the moon and the pips are
generated. `TaprootTests` and `DuskcapTests` add 21 cases and the offline suite is 491.

**Two more places to watch a video, and neither of them is forced.** The game shipped
playable start to finish without ever seeing an ad: there are no interstitials, hearts are
spent only on *losing*, and the coin offer was entirely opt-in from the hub — so a competent
player met the ad system approximately never. The answer is not a forced break between
glades. It is putting the offer at the two moments a player already wants something.

**`run_continue` — thirty seconds, when the clock runs out.** The highest-intent moment in
the game: the run is already invested, the loss is one frame away, and the offer is the only
thing that undoes it. `PlayScreen.TimeUp` intercepts the expiry, freezes the board and puts
`AdOfferOverlay` up; declining loses the run exactly as before. It is repeatable, bounded by
its daily cap alone.

Four decisions carry it. It pays **`run_time`**, a new `ChestDropKind` and the only unbanked
reward in the game — seconds on a `RunClock` that stops existing when the run resolves. That
one property decides everything else: it is not currency, so no account, no claim and no
server opinion (the callback grants nothing and needed no deploy); it is exempt from the
shared cooldown, keyed on `ChestDropKinds.IsTransient` rather than on a list of placement
ids, because a cooldown paces a faucet and this is not one; and a chest may not roll it,
because a chest is opened where there is no run.

**`RunClock.Extend` raises the limit and never lowers the elapsed**, and that is the whole
reason the feature touched nothing. What is stored is still time *taken*, so `bestMillis`
keeps its meaning, the map badge keeps reading `31 turns · 2:14`, `SaveMerge` is untouched
and `publishGroveStats` needed no change — the same property `CountdownTests` was written to
protect, tested against the first change since that could have broken it. Rewinding the
elapsed instead would have been the same number of lines and would have corrupted all three
at once, silently, because both readings are milliseconds and both look plausible in a save
file.

**Repetition needs no balancing rule.** `StarsForTime` grades against thresholds derived
from par, not against the clock's own limit, so every extension pushes the run further down
the time bands — the second has usually already cost the third star. A player who buys their
way through a glade keeps the clear and loses the stars, enforced by arithmetic that was
already there rather than by a cap somebody has to tune. That is what made "over and over if
they want to" safe to grant.

**`win_bonus` — credits on the victory panel**, under the payout and above the exits, only
on a run that actually paid. It is a **flat amount and the button prints it**, not a
doubling, and that is a constraint rather than a preference: earned credits are derived from
the star ledger (invariant 9), so there is no accumulated figure to multiply, and doubling
one run would mean storing which runs had been doubled — a forgeable per-level set that
*pays*, which invariant 15 sends straight back to 13. What a signed callback can attest to is
that a view happened, so the amount is keyed on that. A multiplier the panel cannot honour is
worse than a smaller number it can; the player checks, once.

**One panel still, not three.** `AdOfferOverlay` absorbed both, because it already renders
the six honest refusals and two more copies would be two more chances to get "no fill" wrong
— the argument that deleted `RouteOverlay`. It grew one thing: **`Dismissed`, raised from
`OnDestroy`**, so exactly one of it and `Rewarded` fires for every one of the panel's six
exits — watch, decline, the corner cross, the scrim, the back key, and the screen dying
underneath it. The continue needs that absolutely: the run behind the panel is frozen
mid-defeat, so an exit that reported nothing would strand a player on a dead board. Two
smaller consequences: the continue is the one placement with a **spelled-out decline**
(a cross that ends a run is the ambiguity `ForfeitOverlay` exists to remove) and the one that
**lets itself out** rather than growing a COLLECT button, because what it bought is being back
in the run.

The clock face is `Art.Dial` — generated, for `Art.Bloom`'s reason: an `Image` whose sprite
has not arrived is a white rectangle (invariant 7b), and the panel asking somebody to watch a
video at the instant they lost is the worst place in the game to look broken.

No save schema change, no `progression.json` retune of anything that existed, no server
deploy. `ContinueTests` adds 18 cases; the offline suite is 512.

**The animation clock is arithmetic, and it is tested — `TweenCycle`.** Reported from play
as "the background theme seems to flicker a little", and it was two faults in `Tween.Update`
meeting. `Loop(-1, true)` **never ping-ponged**: the wrap subtracted one duration whenever
`elapsed` reached one, so `elapsed` could never enter the second half of the cycle, the return
branch was unreachable code, and every ping-pong in the game was a sawtooth that snapped back
at the end of each period — the hub's backdrop light dropping in one frame every 3.4s, the
feature card's beacon every 1.5s. And the wrap drained **one** cycle per frame, so a step
covering many left the surplus to burn off over the frames after it, swinging a whole cycle
each. That is what a resume delivers: `Time.deltaTime` is capped by `maximumDeltaTime` and
`Time.unscaledDeltaTime` — which every tween here runs on — is not, so the first frame back
carries however long the app was away. `RunClock.MaxTick` already existed for exactly this
fact about exactly this clock.

Both halves live in `TweenCycle` now, which holds no Unity types and no statics, for the reason
`RunClock` does: **it can be run a thousand simulated frames at a time offline.** That is the
decision worth not re-litigating. This is the one subsystem here whose failures are invisible in
a screenshot and obvious only in motion — it compiled, validated and shipped wrong for a year —
so "prove it, do not assert it" needs the arithmetic reachable without an Editor. `Tween` is now
the driver and `TweenCycle` is the rule; it is the code that ships, never a copy, because a
proved copy proves nothing (invariant 9a's lesson, applied where there is no reason to have two).
`TweenCycleTests` is ten cases and they were checked against the *old* arithmetic first — three
of them fail on it. A suite that would have passed either way is not a guard.

One thing the fix deliberately did **not** change: a traverse still takes exactly `duration`.
The climb is the speed it always was; what was a snap is now a fall of equal length. So the
period of a ping-pong doubles and nothing slows down, and none of the shipped durations wanted
retuning — `OneTraverseStillTakesExactlyTheDuration` pins it, because "does this need retuning"
is the first question anyone will ask of this change.

**Two hub faults with one shape, both about `Destroy` landing at the end of the frame.** The
resource row was **rebuilt** on every wallet event — three pills whose entrance starts at scale
zero behind a delay — so returning to the game flashed them in and out several times over, once
per event, and a resume raises several (a sync applies another device's work, and the first read
of `Wallet.Hearts` commits whatever refilled while the app was away). It repaints now: nothing in
that row is a function of the wallet except the three readouts. That also removed a re-entrancy
worth knowing about — **reading `Wallet.Hearts` raises `HeartsChanged` from inside the getter**,
so `BuildResources` could be re-entered while placing its first pill, and the outer call then
registered *its* pills with `ResourceSlots` on a row already queued for destruction, leaving the
chest's prizes nowhere to fly to.

The second is the house rule the hub was the only screen not following: **hide a region before
destroying it.** `Destroy` lands at the end of the frame, so a panel replaced in place is drawn
over its own replacement until then — and with everything entering from `Tween.Pop` at scale
zero, what the player sees is the old panel, a gap, then the new one springing in.
`ProfileScreen`, `EventScreen`, `StreakScreen`, `CompanionScreen` and `CompanionUnlockOverlay`
all did it; `HomeScreen` (three places) and `LevelsScreen`'s rank marks did not.

**A pause menu must hand the board back however it was dismissed.** `PlayScreen.Pause` latches
the board — which also stops the countdown, since the clock only accrues while `!Locked` — and
the unlatch was wired to the *buttons*. `ModalView.MakePanel` defaults to `dismissOnScrim: true`
and that path calls `Close()` with no continuation, so a tap outside the panel left the board
latched with the run unable to end even by timing out, and `PlayScreen.OnBack` returns false on a
live run so the back key could not rescue it either. The unlatch is on `OnDestroy` now, with a
flag set only by the three exits that hand the run to `ConfirmForfeit`, which wants it kept
frozen. Same shape as `AdOfferOverlay.Dismissed` and the same reason: **a panel with five exits
reports through none of them reliably, so the safe outcome has to be the default and the
exception has to be the thing somebody declares.** Every other panel raised over a live board was
checked and is sound — `ForfeitOverlay` and `DefeatOverlay` pass `dismissOnScrim: false`,
`TipOverlay` and `WinOverlay` build non-dismissable scrims, `AdOfferOverlay` reports from
`OnDestroy`.

**The Grovement — the player builds something, and it is the only screen two accounts at
the same progress do not share.** The fifth nav tab was `items`, a promise; it is now the
grove: floating islands the player earns, creatures they rescued living on them, and decor
they bought and placed by hand. It is deliberately the loudest tab — orange in both states
and 18% larger — because a grove nobody finds is a grove nobody builds, and it is the only
tab leading somewhere the player *made*.

Three things stack, and the split between them is the design rather than an implementation
detail. **Land** is a plot per chapter finished. **Residents** are the board's own critters,
each earned by clearing the glade it was woken in. **Decor** is bought with credits — the
second sink the economy needed, since companions were the only one and they run out.
Earned things prove what you did; bought things are how you express taste. If residents
could be bought the grove would stop being a record.

What it cost the save file is two fields (**v16**) and invariant 16 has the argument: land
and residents are derived and store nothing, a purchase is a union-joined set exactly like
`companionsOwned`, and an arrangement is the third thing in the file merged by recency —
so it is the only part that can lose something, and 11c's two rules (a stamp per slot, and
never storing a default) are what stop it. **Holding a piece is permission to draw it
anywhere, not one copy**, which is what keeps a count of benches — 11b's forbidden shape —
out of the file entirely, and makes the shop about variety instead of quantity.

The catalog is a **body**, `Content/homestead.json`, versioned by one integer in the
manifest and read on entering the screen. That is invariant 4a applied to the thing most
likely to break it: the companion roster rides the manifest because the hub must draw a
companion before anything else happens, and nothing draws a fence until somebody taps a
tab. A shop is also the part of a game that grows fastest.

What shipped: **10 plots, 52 slots, 40 pieces** — 5 residents, 4 free from the first
launch, 13 earned by playing and 23 for sale across 23,490 credits (about six weeks of
ordinary play, against the companion ladder's sixteen months). Seven plots are authored
**ahead of the chapters that open them**, so the ladder is visible before it is walkable;
both validators collect those forward references into one warning rather than one per
plot, which is `ValidateCompanions`' call and for its reason. The art is 45 sprites cut
from the same CraftPix pack the hub background itself was cut from — so the grove is
literally made of the world it floats in — and the residents need no new art at all,
because they draw the critter flipbooks the board already loads globally.

Two smaller decisions worth not re-litigating. **There is no edit mode**: a slot is tapped
and a picker opens whether it is empty or full, because a mode toggle is a control that
changes what every other control does on a screen whose whole vocabulary is "tap the thing
you want to change" — empty slots wear a soft breathing ring instead, which is the price of
having no mode. And the **picker is a modal while the shop is a screen**, which is the
opposite call from `CompanionScreen` and deliberate: placing is not browsing, and the answer
to "what goes here" depends on what is beside it, so a screen would take the grove away at
exactly the moment it is the thing being decided about.

`HomesteadTests` adds 28 cases and the offline suite is 545; `Tools/verify/content.py` now
checks the grove too, which it has to — the shipped-catalog tests reach `Application.dataPath`
and are therefore Editor-only. **`firestore.rules` gained the two fields and needs a deploy**
(`firebase deploy --only firestore:rules`); nothing else on the server changed, because
nothing there adjudicates a picture.

**Three faults the first build shipped with, and two of them are one lesson.** Reported from
play as "every island is locked", "the shop previews are black boxes" and "there is an island
at the bottom I cannot scroll to".

The grove was **drawn half a canvas too low**. `HomesteadMap` measures from the canvas's
top-left and the islands were anchored to its *centre*, so on a 6,261px grove every island sat
3,130px below where the `ScrollRect` believed it was — and a `ScrollRect` bounds itself by the
content **rect**, never by where the children were drawn, so the bottom four islands were
outside the scrollable range in a way no dragging could reach. What a player saw was the middle
of the ladder: every island locked, and the one they owned unreachable below the last inch of
scroll. Two reports, one anchor. `UIKit.Box` pivots centre and anchors where it is told, which
is the same lesson as `UIKit.Corner` and `PillFaceLift` learned in a third place. The parking
latch had the same shape of error one level up — an island whose art has not arrived is laid
out as a square, so the height parked against on the first paint is a guess; it now re-parks
until every plot's sprite is in hand and latches only then.

The black previews were **the grove screen freeing art the shop had already drawn**. `Destroy`
lands at the end of the frame, so an outgoing screen's `OnDestroy` runs *after* the incoming
one has built and painted — releasing there pulled every decor sprite out from under a fully
drawn shop, and nothing repaints, which is why leaving and coming back "fixed" it. The shop
carried a guard for exactly this and the grove screen did not, so the pair only worked in one
direction: the rule now lives in `HomesteadArt.CloseUnlessWanted`, read off an `IDrawsGroveArt`
marker, because a check each screen has to remember is a check the third screen forgets.

And the copy **claimed a placement that had not happened**. Buying said "Added to your grove"
and tapping an owned piece said "X is already in your grove", when holding a piece is
permission to draw it and nothing more — the distinction the whole save format rests on, told
to the player backwards. Both lines now name the next action: tap a spot in your grove.

**The grove has a home in it — and it cost the save file nothing.** Working but primitive was
the verdict on the first version, and it was right for four measurable reasons. The buildable
area was a **stamp**: `plot_meadow` is 801px of art drawn at 669, so the island was *downscaled*
to 19% of the screen and its grass top to about 8% — and eleven objects were being asked to
compose on it, which is why every piece came out at 40–95px. There was **no home**: 35 of 40
pieces were ground cover and the only building was `cottage`, the 40th item, optional, and
interchangeable with a pebble. **Every slot accepted everything**, so the only decision on offer
was which of eleven interchangeable dots got which sticker and every grove came out looking
equally accidental. And **nothing changed because you built it** — a full island and an empty one
were the same picture with different dots on it, when a before-and-after is the entire mechanism
the endowment effect runs on.

Four changes, and the thing they have in common is the point: **not one of them adds a field to
`SaveFileDto`.** Save schema stays at v16.

**The hearth: one dwelling at the centre, and it grows.** `HomesteadPieceKind.Dwelling`, drawn on
a `hearth` slot the starter island alone carries. The ladder is `cottage → lodge → hall → manor →
sanctum`, and each rung is **its own permanent id in the union-joined set that already holds
purchases** — invariant 15 for the second time. A stored "home level" is the shape invariant 11b
forbids, because two devices reading 3 and 1 are equally consistent with "one upgraded" and "one
has not heard yet"; a set of ids has no such problem, and "the home" is a maximum over it, which
is idempotent, order-independent and impossible to lose. The first rung is **free and the build
gate proves it**, so the hearth is never empty. And the home is **derived rather than placed**:
buying *is* the moment the house changes, which is the whole feature — a dwelling the player has
to remember to put down is a dwelling they can buy and not see, the exact confusion the shop's
copy caused a week earlier. Nothing else may stand on a hearth, which is what makes deriving it
safe: a slot the player can place into is a slot whose contents live in the save file.

**Slot roles, so placing is composing.** `HomesteadSlotKind` — ground, hearth, structure, bed,
path, edge, canopy — one field per slot and one per decor piece, and a plain equality between
them. Fences run along the rim because the rim is what accepts them; paths lead to the door;
trees stand at the back. Two exceptions, both deliberate: a **resident fits anywhere but the
hearth** (telling somebody where their own rescued critter may not stand turns a toy into a
form), and a dwelling fits only the hearth. Both default to ground, so a catalog written before
the field keeps working. A slot whose kind the player owns nothing for **says so and points at
the shop** rather than opening an empty grid — the only place in the feature that can explain
what a slot is for without labelling all eleven of them on the island.

**The island is a screenful now, and its width is derived from its art.** One `plotScale` of
~1.27 screen pixels per art pixel for the whole grove, so `width` falls out of the sprite rather
than being authored — which is what makes a piece the same size on a small island as on a big
one. Main plots went 0.62 → 0.94 and satellites 0.24 → 0.38; the buildable strip is now about
700×260 and pieces read at 150–500px. The gap between islands went 150 → 190 with them, because
a rectangle in `HomesteadMap` bounds the *island*, not the oak standing on it.

**The ground reacts, and stores nothing.** `GroveTending` maps fill — occupied over placeable,
the hearth excluded — onto five stages. The island lifts out of a cool grey as it fills, and a
finished one is lit from within, wears fireflies and turns its name plate gold. Every island now
carries **its name and its count** (`The Meadow · 3 of 10`), because a name is what makes
somewhere a place and a signal a player can see but not read is a signal they cannot aim at.
Derived, so no counter to merge, no floor to keep monotonic, no migration — invariant 14's
preferred shape — and it goes *down* if you empty an island, which is right for an arrangement.

Three notes for whoever picks this up. The tiers all draw **one cottage sprite** until real
building art lands: what tells them apart is scale plus life — smoke from the second rung, a lit
window, lanterns at the door, a gilded ridge, fireflies at the top — and that life is not a
placeholder trick to be thrown away, because smoke is the strongest "somebody lives here" signal
available per byte and it cannot be painted into a sprite. Swapping in real art is a change to
`art` in the catalog and nothing else. The shop shows the ladder as **one cell**, not five: five
cells drawing five names over one house read as a bug, and the ladder belongs on the home panel
where pips can show it. And the catalogue was **re-priced by kind** rather than by position in
the file — a path used to cost more than a signpost because the list happened to be sorted by
price — so the cheapest rung of every kind is within a day's play. It now runs 12,250 credits of
decor and a 49,500-credit home ladder.

**The shop is 168 pieces, and the screen that shows them loads 22.** A drop of seventeen
CraftPix isometric tilesets — 1,056 files, 936 of them distinct — went in as **124**, and the
arithmetic of what was left out is the interesting part. The seventeen packs are one series, so
the same signpost, skull, ladder and stone ship in nearly all of them: deduplicated by content
and then by *object*, 936 files collapse to roughly 120 distinct things you could stand on an
island. What is excluded is 249 numbered level tiles and platform chips, the UI furniture
(hearts, progress bars, map pins, coin piles, gift boxes, reward stars) and the packs' mascot
avatars. **None of the seventeen contains a building**, so the home ladder still draws one
cottage; that is a purchase, not a code problem.

Two of the packs' props are shaded and the rest are flat, and both are in. That is a deliberate
acceptance rather than an oversight — the grove reads as one place because the *islands* are
consistent, and a decorator wants variety more than it wants one rendering style.

Three things make this sustainable rather than a one-off dump.

**`Tools/grove_art.tsv` is the import, and `import_grove_art.py` runs it.** One row per piece:
source path, permanent id, slot kind, price, scale, lift, name. The script copies the art,
writes the loc string, regenerates the catalog's `pieces` array and bumps `groveVersion`.
Nothing is hand-copied, the mapping and the price are reviewed together in one diff, and the
next pack is a column rather than an afternoon. It refuses to *remove* an id it imported
before, because a piece id is written into save files twice over (invariant 1) and deleting
one empties the slots of everybody who placed it.

**The grove's one asset scope became two, bounded by different things.** It used to load every
piece that exists whenever the Grovement opened — affordable at forty, absurd at four hundred,
since the screen shows at most one piece per slot. `GroveAssets` is now the islands, the home
ladder and whatever is *placed*, so it is bounded by the player's grove (78 slots) and stops
growing when the catalog does; `GroveKindAssets` is one slot kind, which is what the shop pages
by and the picker filters to. Measured on the shipped catalog: **174 addresses for the whole
thing, 22 for the grove screen, 40 for the largest tab** — and the first two of those numbers
diverge further with every drop, which is the point. Two rules keep it honest — a piece
just placed is `Claim`ed into the grove's scope (otherwise the next tab switch frees art the
islands are drawing), and `AssetLibrary.AddToScopeAsync` exists so claiming one sprite does not
tear down and refetch the other twenty.

**The shop pages by slot kind**, which is a memory decision as much as a browsing one: a single
grid over the whole catalog must load the whole catalog to show the nine cells that fit on a
phone. Residents and the home lead every tab, because the top of that page is the part money
cannot reach and the ladder is the one thing worth saving for. Each tab draws itself with the
cheapest piece of its own kind, so a drop that adds a kind of thing needs no new icon — and
`HomesteadCatalog.Emblem` is in Domain because two things must agree about which piece that is:
the tab that draws it and the scope that has to have loaded it. Getting that wrong is what left
five blank tabs and a black square where the house goes on the first pass, which is invariant 7b
in its most ordinary form.

One operational note that cost a round trip: **the importer hook does not address art copied in
while the Editor is closed or mid-reload**, which is every run of `import_grove_art.py`. The
cells then draw blank, because an unaddressed sprite loads as nothing.
`Glimmer Grove ▸ Addressables ▸ Sync All Assets` is the repair and the script now prints it as
step one; `AddressableAudit` in the build gate is what stops it ever shipping that way.

Two smaller things came out of it. **The texture size cap is per folder now** (`ArtImportRules.Caps`:
512 for grove props and companions, 256 for critter frames, 1024 for UI, 2048 only for
backdrops and map strips) — a texture costs its dimensions, not its file size, so a hundred
props at the old blanket 2048 would have been a bundle nobody could ship. And
`Glimmer Grove ▸ Reapply Art Import Rules` exists because a preprocessor fires on first import
only: art that landed before a rule changed keeps whatever it was given, silently. **It must
batch.** The first version called `SaveAndReimport` per texture in a loop, which is one round
trip to Unity's import workers each; 335 of them back to back crashed both workers and left the
Editor wedged in a domain reload it could not finish. It is `StartAssetEditing`/`StopAssetEditing`
with a `finally` now, and the `finally` is not optional — an exception between the two leaves
the asset database in editing mode, which looks exactly like the freeze it prevents.

**The Grovement's second pass: one roster, eight shelves, and a grid that stops growing.**
Four faults were reported together and three of them turned out to be the same shape — a screen
whose cost was tied to how much content exists rather than to what is on it.

**Residents are companions now**, which is invariant 16a rewritten rather than extended. The
grove authored five creatures of its own, earned by clearing five named glades; the profile had
thirty-one companions with prices and keeper gates. Two rosters, two unlock rules, and a player
who bought Coral could not stand her in their village. `GroveResidents` projects the manifest's
roster into the catalog, `HomesteadLedger` delegates the resident half whole to `CompanionLedger`
(invariant 15a, taken literally) and re-raises its `Changed`, so buying in either place is visible
in both with no callback anybody can forget. Wearing stays a profile preference — the grove's
purchase path deliberately does not go through `Profile.TryBuyAvatar`, which buys and then wears.
Two ids came out of it and both are permanent: the `friend_` prefix, because `pebble` was already
a decor rock *and* a companion, and a retirement table rewriting the five old ids on every load,
because a retired id leaves a hole that still counts as occupied.

**The shop pages by shelf and browses through atlases.** Residents used to be pinned to the top of
every tab — a resident fits every slot, so `Fits` put the whole roster on all six tabs and in all
six asset scopes. `GroveShelf` separates *where may this stand* from *where is this sold*; there
are eight, they are the tabs, the atlases and the scopes, and the shelf's name is spelled out under
the tab row rather than under each glyph. `Glimmer Grove ▸ Addressables ▸ Rebuild Grove Atlases`
generates a 256-max copy of every piece and packs one atlas per shelf — measured on the shipped
catalog, the whole shop is nine pages and about 11 MB, of which a tab holds **two**: its own shelf
and the tiny tab-emblem atlas. Ground is one 1024×512 page for 35 pieces. `Validate Art` proves all
202 pictures are present, because a stale atlas is invisible in the Editor.

**`GridView` keeps the rows you can see.** Measured in play mode on the residents shelf: 31 pieces,
11 rows, 4,276px of content — and **three** live cells. Switching to the ground shelf rebound the
same three objects. That is also the flicker fix: the shop repainted twice on every tab change
(once for the shelf, once when its art landed) and every cell entered from scale zero both times.
`Show` is a new list and animates; `Refresh` is the same list redrawn and does not. `HomesteadArt`
now coalesces browse loads properly too — the old guard asked `IsScopeLoaded`, which goes true the
instant a load *starts*, so a first visit loaded an empty set, got no scope, and loaded again.

One trap worth not rediscovering: **the atlas file extension selects the importer.** A
`.spriteatlas` written in the V2 format imports as editor data with a plain `AssetImporter` and
produces no `SpriteAtlas` at all — every address resolves, every check passes, and the shop draws
an empty grid. It must be `.spriteatlasv2`, and `EditorSettings.spritePackerMode` must be
`SpriteAtlasV2`; `Set Up Project` sets it and it ships in `ProjectSettings/EditorSettings.asset`
for `m_BuildAddressablesWithPlayerBuild`'s reason.

Save schema is **unchanged at v16** — a projected resident is derived, its purchase lives in
`companionsOwned`, and the retirement rewrite happens at the one door every save comes through.
`GroveResidentTests` adds 15 cases; the offline suite is 572 and the Editor suite is 673, all
green. Two pre-existing failures were fixed on the way: `RewardVectorTests` had been red since the
daily, ads and streak blocks were added to the reader (it asserted no problems, and the vector
file deliberately omits those three sections), and one `HomesteadTests` fixture wrote its JSON
numbers as `.5`, which `JsonUtility` refuses — so it had been failing at the parse and never
reaching the rule it is about.

**The Grovement is a floor now, not islands — save schema v17.** The ten floating islands are
gone and the grove is one 14×14 isometric tile field, panned and pinch-zoomed, on which anything
can be placed anywhere. The islands were a ladder of fixed compositions: an author placed every
slot, gave it a role, and the player chose which of eleven pre-placed dots got which sticker, so
every grove came out with the same shape and different stickers on it. A field of identical tiles
moves the composition to the player, which is the whole point of the feature.

**What it cost the save file is one field, and what it did not cost is the interesting half.**
`HomesteadLayout` is untouched — a tile *is* a slot, so an empty floor still costs nothing and a
floor with two things on it costs two rows. The one addition is `groveLandOwned`: land used to be
derived from chapters finished, and land bought with credits cannot be, so it is stored as a
union-joined set of **region** ids. Regions rather than tiles because both are legal shapes and
only one stays small — see invariant 16e. The shipped field is nine regions tiling it exactly, a
free 6×6 in the middle and eight around it from 2,500 to 17,000 credits (68,500 for the lot, about
126 days of ordinary play).

**Unowned ground is not drawn, and land is sold in the shop.** The first build did the obvious
thing — drew the whole field and put a padlock on everything unbought, with expansion sold by
tapping a locked square. That made the grove a wall of padlocks around a small lit patch, which is
the opposite of what a screen about the place you built is for. The floor is now exactly the land
you own, so buying a region is visibly *the ground growing* rather than a padlock going away, and
expansion moved to a `GroveShelf.Land` tab in the shop where the other things you buy are. It is
the only shelf with no browse atlas: a region is a rectangle rather than an object, and a
thumbnail of a patch of grass is a picture of nothing, so its cells and its tab draw
`Art.IsoTile`.

**The ground is a block, not a lozenge, and the offset is derived.** `Homestead/floor_grass` is
418×287: a 418×209 top surface with 78 pixels of side wall painted under it. Centring the image on
the tile point sits every tile 39 pixels high and the grid stops lining up with itself, so the
sprite hangs by half its skirt — and the skirt is *computed*, because the top face of an isometric
tile is 2:1 by definition and whatever is left below it is wall. A re-cut tile with a deeper side
needs no number changed. That is `UIKit.PillFaceLift`'s lesson for the fourth time. It only works
because the tiles are drawn back to front, which `Restack` already had to do.

**A new grove opens with exactly two things on it: the hall and one friend.** Both are *shown*
rather than stored. The hall is drawn from the best dwelling owned, which is the hearth's old rule
on a fixed tile; the companion is `AvatarCatalog.Starter` on the tile beside it, and writing that
placement at first launch is exactly what invariant 11c forbids — a fresh install would stamp it
with *now* and put the friend back on a device where the player had moved them. Invariant 16f.

**Three pieces of new machinery.** `GroveFloor` (Domain) owns the geometry — the 2:1 transform,
its inverse for turning a tap into a tile, and `DrawOrder` — in Domain for `ChapterMap`'s reason:
the build gate proves regions do not overlap and a validator cannot reach into Presentation.
`GroveFieldView` is the camera: drag to pan, pinch to zoom, and only the visible tiles exist,
which is `GridView`'s bargain in two dimensions. And `View.WantsMultiTouch` exists because pinch
needs two fingers and `Boot` turns multi-touch off for the whole game — a board that accepted two
fingers would let a player turn two conduits in one tap. The grove *declares* it and `Flow` applies
it on every screen change, so a board can never inherit it however the player left the grove.

**The one bug worth not rediscovering: depth cannot be assigned per tile.** `SetSiblingIndex`
*inserts*, so placing a tile at index 12 shifts the eleven behind it and the next tile's intended
index no longer means what it meant. The field looked sorted and was not — caught in play mode with
the hall drawing in front of the companion standing one tile nearer the viewer. `Restack` sorts the
whole visible window in one pass when the window changes, which is a few times a second while
panning and never while still.

Content is grove schema **v3** (`ContentSchema.Version` 3, `MinimumSupported` still 2, so the
manifest and chapter bodies are untouched at v2). A v2 grove body is *refused* rather than
half-read, because it describes islands this build cannot draw and the alternative is opening the
Grovement onto no ground at all. Existing arrangements do not migrate: the old slot ids
(`meadow_a`) are not tile ids, everything bought stays owned, and the game has not launched.

**Switching accounts — and why there is no sign-out.** The profile's account panel could
link a provider and nothing else: once linked it showed two buttons that could not be used
and no way to reach another grove. The obvious fix is a logout button and it is the wrong
one. Signing out of a game with no login screen leaves a device holding a grove nothing
owns, and both honest resolutions are worse than the button — keep the save and the next
sync clones a paid-for account into a fresh anonymous one (invariant 17), or erase it and
somebody who only wanted to stop syncing has lost everything. Nobody in the market ships
that either: the big F2P titles offer "connect account" and, at most, a switch that returns
the device to a *separate* local save, which this game does not have and does not need.

So it is a switch. `CloudSaveService.SwitchAccountAsync` and the order **is** the design:
**secure, authenticate, fetch, replace.** The outgoing grove is pushed to the server first
and a failure there abandons the whole thing with nothing touched — that step is the only
reason the act is reversible, and without it "switch account" means "discard whatever this
device has played since its last sync". Then nothing local is destroyed until the
replacement is in hand, so a network drop between two calls costs a retry instead of a
grove. Every step is its own `SwitchOutcome`, because three of the six are not failures and
two more leave the device exactly as it was — telling a player "something went wrong" for
any of those is how somebody decides their grove is gone while it sits safely on a server.

Four things are worth not re-litigating. **`AccountGate` is a pure function** (invariant 17),
in Domain, with no Unity types, for `TweenCycle`'s reason: it guards an unrecoverable failure
that is invisible in the Editor, which never authenticates, so it has to be provable offline
rather than reasoned about. **Arriving at the account already held is a no-op, not a
refresh** — that branch is what makes an interrupted switch recoverable in one tap, so it
sits before anything destructive. **Recovery may not become a third account**: a device
caught between two cannot save its grove anywhere, so `ResumeAccountAsync` proceeds only if
the credential names the account already held and otherwise hands over to the destructive
adopt prompt, which asks twice. And **the destructive prompt is skipped when there is
nothing to destroy** (`HoldsAGrove`) — a player who just installed the game to get their
account back meets it with an empty grove, and a warning that cries wolf there is one nobody
reads on the grove that has everything.

Three fixes fell out of it, all pre-existing and all silent. `SignInWithCredentialAsync`
**signed out before attempting the provider flow**, so closing the Google sheet without
choosing permanently ended the session the player was happily in — fine while that call was
only reachable after somebody had agreed to abandon their account, and not fine once it is
how you switch. `RedeemPurchaseAsync` had its own ad-hoc sign-in; it goes through the gate
now, because crediting a receipt to whichever account happened to be signed in is a support
case with a proof of purchase attached.

And **`LinkAsync` was the leak wearing a friendly name.** Linking attaches a provider to
whichever account the session happens to be, and the backend will *create* an anonymous one
to attach it to if the session has gone — after which the local grove was re-owned by that
account and pushed into it. It is reachable, not theoretical: `IsLinked` asks the SDK, so a
linked player whose session is lost reads as a guest and the panel offers exactly that
button. The authorisation now happens **before** the provider is touched, and the ordering
is the fix rather than a tidiness — refusing afterwards would be too late in a way nothing
here can undo, because the player's Apple ID would already belong to an empty grove for
ever. `AccountGate`'s answer for (owned save, no session) is Resume, which creates nobody.

**What switching exposed on its first real test.** The switch worked; the grove did not come
back. `groveLandOwned` had never been on the wire — invariant 12a, which this is the story
behind — so the land was not in the cloud to return. Two more of the same shape went with the
fix: `flipped` had just been added to a placement and reached neither the mapper nor
`SaveDelta.SamePlacements`, so a piece would have come back facing the other way and a flip on
its own would never have been pushed at all. Worth stating plainly, because it is the general
form: **a switch is the first feature in this game that reads the cloud copy back over a
working local one.** Everything that was never uploaded had been invisible until something
replaced the file that was hiding it, and every one of those bugs predates the switch.

The switch itself adds no save field, needs no `progression.json` retune and no server work.
`firestore.rules` does need a deploy, but for the grove-land fix rather than for the switch —
and **it must go out before any client build that writes `groveLandOwned`**, or `hasOnly`
refuses every save write.
`SaveService.Wipe` grew a `forgetAccount` argument for it, and `ModalView.Rebuild` exists so
the account panel can be sized to the state it is in rather than to the tallest one it could
reach.

**`ISaveStore` was worth the seam.** `SaveService` held a concrete `SaveStore`, so nothing
above it could be tested without `JsonUtility` and a real directory — which put the account
switch, the merge adoption and every ordering they depend on behind somebody remembering to
open the Editor, for the subsystem whose failures are the only unrecoverable ones in the
game. Three members, and it deliberately does **not** abstract what makes `SaveStore` worth
having: the atomic write, the backup rotation and the corrupt-file recovery stay one
implementation, tested against a real filesystem in `SaveStoreTests`, because a second
version of that is a second thing to get wrong. `SaveService.LoadWith` is the only other
door and it is internal.

`AccountGateTests` is 8 offline cases and `AccountSwitchTests` is 13, of which **6 run
offline**; the other 7 reach `Debug.Log`, `PlayerPrefs` or `RunGuard`, which are ECalls the
offline runner cannot execute. Those stay Editor-only rather than having the logging
stripped out of production code to move a number. Offline suite 613.

**Chapter two: The Mill Vale, and light that crosses itself.** Ten glades, and one new
mechanic — the **crossing** (`=NS+EW`), a conduit carrying two flows through one tile
that never meet. It is the third rule to bend the board's shape rather than add to it,
and the only one so far that touches the light graph: the traversal now walks *strands*,
of which every tile has one and a crossing has two. Everything above the walk — colour,
lighting, winning, par, the near-miss reading, the duskcap rule — is untouched, which is
the whole reason a mechanic whose entire point is "two networks share a tile" cost no
second graph and no second pass.

Four decisions carry it, and the first is the one that would have shipped a broken
chapter. **A crossing wears all four arms at every angle**, so the mask comparison that
served as "is this tile solved" — written out five times across `Puzzle` and
`PuzzleFactory` — calls every crossing already solved. That derives a par short by one
per twisted crossing, and par multiplies into the move budget and the clock. It is now
`Puzzle.Alike`, once, and every owed-turn count in the game asks it; the five copies are
gone. Same lesson as invariant 9a in the file that had no reason to have two.

**A straight crossing (`=NS+EW`) is inert and a twisted one (`=NE+SW`) is worth exactly
one tap**, and neither is a special case: rotating a crossing swaps which strand is
called which, and nothing on the board can tell, so `Alike` treats the two strands as
interchangeable labels and both facts fall out. The straight one is architecture — a
bridge somebody built — and Stonebridge is the glade that roots four of them.

**A crossing has no hub.** The hub disc is what this board already means by "these arms
are joined", so leaving it off is the rule stated in the vocabulary the player reads
rather than a decoration missing, and the strand drawn over the other wears a shadow.
That matters because a tile with four arms is a crossroads everywhere else in this game:
the failure mode is not a player who does not know the rule, it is one who concludes the
board is broken — which is why `Mechanic.Crossing` sits between the duskcap and the
taproot in `TeachingOrder` rather than at the end.

And **a crossing whose two strands are joined elsewhere crosses nothing**, which
`LevelValidator.CheckCrossings` says out loud. It is invisible everywhere else — the arms
mate, the solution lights, par is a sensible number, the board draws beautifully — and it
costs the player turns routing around a separation that is not there. A warning rather
than an error, because a loop that leaves by one arm and returns by another has to close
somewhere.

What it unlocks is worth more than what it is. **A duskcap's island of dark can now run
through the light instead of only around it** — chapter one had to state flatly that a
duskcap and its conduits are their own island, because in the solution a lit cell's
neighbours are lit. Across a crossing they are not. *Under the Boughs*, *Hollow Ford* and
the finale are all built on that, and the misrotation that joins the shadow to the grove
is a real trap that a restart forgives.

The ten glades run **36, 48, 41, 52, 43, 58, 49, 55, 51, 63** — deliberately not
monotonic, because par is length rather than difficulty. Each has one idea: the first
crossing; a rope of two runs braided through three of them; the dark running through;
brittle stone on both approaches with the crossing itself brittle (one wrong guess
allowed, and no more); two crossings on one taproot at opposite corners; three networks
where two stay pure and the third holds a heart of each; three fords of one shadow; four
rooted bridges; a cascade handing the grove from yellow to red to green to blue; and a
knot with all of it.

The map is four strips rather than the Shallows' six, because the source image is shorter
and `Tools/make_chapter_art.py` **scales to whole strips rather than stretching to them**.
That tool is the other half of the drop: `Tools/chapter_art.tsv` is one row per chapter
naming only the source packs, and every name and every colour is read out of the chapter's
own JSON — so retuning a glade's `accent` regrades its backdrop with nothing else to edit,
and an eleventh glade gets an eleventh backdrop by being authored. The backdrops are
**graded rather than darkened**: real painted structure reduced to luminance and mapped
onto a three-stop ramp built from the level's own slate and accent, which is what lets ten
glades share two source paintings.

What this cost: **no save schema change, no `progression.json` retune, no server work and
no new concept in the reward path** — the glades pay exactly what any glade pays.
`CrossingTests` adds 23 cases (offline suite 641), `content.py` and `author.py` mirror the
rule, and `author.py` gained `cross`, `root` and `path` — `root` derives every member's
start rotation from the number of taps the root should cost, rather than leaving four
numbers that have to agree to be typed by hand.

**The shop — real money, and the first thing in this game that takes any.** The second nav
tab was `ui.soon.shop`; it is now `ShopScreen`. Four shelves: gems, coins and bundles for
money, and **supplies** — hearts and heart boosts — for gems. Invariant 18 has the argument
for that split and it is the reason the whole feature adds **no field to the save file**, no
schema bump, and no new concept to the reward path.

**Gems had no sink at all before this**, which is worth stating plainly because it was the
real gap: they were earned from chests and streak nights, displayed on two screens, and spent
on nothing. They are now what hearts, faster hearts and time are bought with — so the money
ladder feeds a currency that feeds the gate, which is the shape every match-3 economy
converges on and the reason it is worth converging on.

**Thirteen products and five sprites.** A card is a *container* plus a *pile*, both chosen
from where the product sits on its shelf — and the tier is derived from the reference price,
so a rung inserted in the middle re-draws everything above it with no art order. The coin and
the gem in the pile are the game's own, which is not a saving but the readability of the whole
screen: the pile is made of the same coin the hub's pill spins. `ShopArt` is
`CompanionRevealOverlay`'s argument in a quieter place. The only new art is a pouch and four
chests, cut from the same CraftPix pack the rest of the UI came from and global for `Win/*`'s
reason — five 160px sprites, on the one screen where a frame of white rectangles costs money.

**The prices, and the arithmetic behind them.** Free play collects about **543 credits and 6
gems a day** (`content.py` derives both from the published tables and prints them), and every
credit sink in the game — companions, grove pieces, land, the home ladder — comes to **272,770
credits, about 500 days**. Against that: gems run **100 → 8,500 for $0.99 → $49.99**, a 1.68×
value spread bottom to top; coins run **2,500 → 75,000 for $1.99 → $39.99**, so buying out the
entire catalog is about **$146**; a five-heart refill is **50 gems**, which is eight days of
free gem income or roughly $0.50; a day of fast hearts is **30 gems**. The starter bundle is a
**non-consumable at $2.99** worth about 3× the ladder — one-time offers are exempt from the
monotonic-value check for exactly that reason, and they are safe because the store refuses to
sell one twice.

**Four decisions worth not re-litigating.** Tapping a card opens the store's own payment sheet
and *nothing in between* — the sheet names the product, states the price in the player's
currency and asks for a fingerprint, so a panel of ours in front of it is a tap for a question
about to be asked properly. A **gem** purchase does get a confirmation, because it has no sheet
and no authentication and a mistap on a 280-gem card is two months of free gems. A good that
would overflow the heart ceiling or the boost cap is **refused rather than clamped** — a chest
losing its surplus is fine because nobody paid for it, and this is not that. And short of gems
**opens the gem shelf** rather than greying the cell, which is `CompanionUnlockOverlay`'s rule.

`ShopScreen` never draws a price it made up: every figure with a currency symbol is the store's
own formatted string, and a card whose price has not arrived says so. Six card states, one
sentence each, which is `AdOfferState`'s rule — and `AdOfferOverlay` gained a quiet route to
the shop on the two placements a player can safely walk away from, which is *not* the run
continue (its board is frozen mid-defeat) and *not* the win bonus.

**Unity IAP 5.4.2, behind `GLIMMER_IAP`.** `Assets/Game/Scripts/Store/UnityIapBackend.cs` is
the only file in the project that compiles against the SDK, and the define comes from asmdef
`versionDefines` for the reason `GLIMMER_ADDRESSABLES` does. Version 5 rather than 4 because
Play requires Billing Library 7+ of every update since August 2025, and because 5's model *is*
this design: a purchase arrives as an explicit `PendingOrder` that stays pending until
`ConfirmPurchase`. Two things it deliberately does not do — no local receipt validation
(`CrossPlatformValidator` runs on the one machine an attacker owns), and
`ProcessPendingOrdersOnPurchasesFetched(false)`, because *processing* is the SDK's word and
confirming is ours.

Server: `redeemPurchase` grants multiple currencies and reports what it granted, so the thank-you
panel shows the server's figure rather than a subtraction of two balance readings a background
sync can land between. `products.ts` reads `config/products` and **refuses rather than clamps**.
`refunds.ts` plus `appleNotification` and the hourly `sweepVoidedPurchases` reverse a purchase a
store took back. `seed-config.mjs` now derives `config/products` from the content and refuses to
publish a ladder that gets worse as it gets bigger; `products.example.json` is gone.

`StoreTests` adds 17 cases (offline suite **657**), `firebase/functions/test/store.mjs` adds 18,
and `Validate Content` errors — not warns — on the three shop mistakes no reader can catch.

**What is still needed before a penny moves:** the thirteen products created in App Store
Connect and the Play Console with exactly these ids and kinds, the four store secrets filled in
(they still hold `UNSET`, so every receipt is refused, which is correct), **View financial data**
on the Play service account for the refund sweep, the `appleNotification` URL set for both the
production and sandbox environments, and a redeploy of the functions.

**The Grovement keeps its own score, and the display stopped hiding under the camera.**
Three changes to one screen, and the first is the one that was not only about that screen.

**Safe areas exist now, and they are a layer rather than a margin.** Nothing in the project had
ever read `Screen.safeArea`, so on an iPhone 13 Pro Max the Grovement's back arrow, banner and
shop button all sat under the camera cutout — and so did the hub's gear, the shop's shelf label
and three other screens' chrome. `SafeArea` converts the system's device-pixel inset into canvas
units (the canvas is width-matched at 1080, so the scale factor is the whole conversion, and a
hand-tuned margin is wrong on every phone but the one it was tuned on), and `View.Safe` is a
lazily-built layer a screen puts its **controls** into. Art stays on `Content` and stays
full-bleed: letterboxing a backdrop to avoid a camera is a worse picture than the camera. Two
properties make it safe to adopt a screen at a time — an inset of zero is the ordinary answer, so
every device without a cutout is pixel-identical to before; and the node **re-fits itself**,
because iOS reports its inset a frame or two after a cold start and a value read once in `Build`
is right most of the time and wrong exactly when somebody is watching. The Grovement's field is
inside it too, since a tile under the cutout is a tile that cannot be tapped, and its top fade
grows by the inset rather than moving with the chrome — a gradient that stopped at the safe edge
would draw a seam across the sky.

**The grove has a worth, and it cost the save file nothing.** A readout in the bottom-right
corner: the credits' worth of grove the player holds, and stars at 10K / 20K / 50K / 100K / 200K.
Invariant 16g has the argument for the shape. Three notes beyond it. The whole reading is taken
in **one call** (`GroveScore.Of`) so the number and the stars can never come from two different
moments. The box is **a readout and not a control** — every graphic in it leaves `raycastTarget`
off — because this screen is panned and pinched and the corner it sits in is where a right thumb
rests. And a star won while the screen is open **re-runs the row's fanfare**, which is the point
of drawing it here: land and companions are bought in the shop, so without it the reward for a
purchase would be a number that had quietly changed by the time the player came back. The
baseline for that is only taken once the catalog has actually loaded, or every visit would open
with a celebration for stars won weeks ago. `StarRow` grew a count (still three by default), so
there is one star row in the game rather than a second one that pops differently.

Where the numbers land: the whole catalog is **493,770 credits** — 154,770 of decor and homes,
68,500 of land, 270,500 of residents — so the ladder runs 2% / 4% / 10% / 20% / 41% of
everything, and at ~543 credits a day the first star is about 18 days and the fifth about a year.
Both validators print that table, which is the point of it being content: retuning the ladder is
an edit to `homestead.json` and a `groveVersion` bump, with no app update. (`content.py`'s "every
credit sink in the game" line was wrong on the way past — it double-counted the home ladder,
which is already inside the pieces total, and left the companion roster out entirely. It reads
493,770 now, not 272,770.)

**The two first-visit tips are ordinary lessons.** A welcome and a ring around the shop button,
shown once in a player's life and chained on dismissal, exactly as a glade's are. They are
`Mechanic` values on the existing `TipLedger`, which is what makes "once in a life" true rather
than "once per install" — the ledger is a union-joined set already on the wire, so a second
device does not re-teach what the first one taught, and it cost no new field to say so. They are
deliberately **not** in `Mechanic.TeachingOrder`, which is the board scan's queue; the split is
why `Mechanic.All` now exists, and why the build gate walks that instead — the two lists were the
same until a lesson appeared that no board can bring, after which the loc-key check would have
silently stopped covering all of them. Nothing is taught over an empty screen: the grove body can
land after the transition finishes, and a welcome spent on a blank floor is spent for good.

No save schema change (v17 stands), no `progression.json` retune, no server work and no deploy —
the score's three inputs were already on the wire and already in `firestore.rules`.
`GroveScoreTests` adds 20 cases, all offline; the suite is 681.

**The waterfall moves, and it is the first animated thing in the shop.** Reported as "it is
just water without a mountain, and it is not animated", and both halves were right.
`Elements/04.png` is an *overlay* in its source pack — two translucent vertical stripes meant
to be draped down the face of a stacked platform — so cut on its own it was a pale smear on a
grove tile with nothing behind it and nothing at the bottom. There is no waterfall and no
cliff in any of the seventeen packs, and no animated water anywhere in either asset folder, so
the piece had to be **composed** and the animation had to be **generated**.

`Tools/make_waterfall.py` is the third art pipeline beside `import_grove_art.py` and
`make_chapter_art.py`, and it is separate for the reason they are separate from each other:
that importer copies one source PNG per row, which is the right shape for the hundred and
sixty pieces that are a picture somebody drew, and the wrong shape for a composition. The
cliff is the ruin pack's own `Platforms/19` — a pond on a grass top over a red rock body, in
the same palette as the grove's floor tiles, so the piece reads as ground rather than as a
visitor. The water is drawn in the pond's own colours: a spillway across the grass, a sheet in
three vertical bands (a flat one reads as a pane of glass; a shaded side and a lit one read as
a body of water), streaks, a foam crest and a churning splash.

Three decisions are worth not re-litigating. The composition **measures the sprite** — where
the grass slab ends, where the pond's near edge is and where the rock's silhouette stops, all
read per column — so the water pours over the actual sheared edge rather than over hand-typed
coordinates; that is `UIKit.PillFaceLift`'s lesson for the fifth time. Every cycle in the loop
runs at a **whole number of cycles per loop**, because a streak travelling 1.3 sheet-lengths
would jump on the wrap — `TweenCycle`'s bug in a different clock. And the frames are authored
at **384 wide rather than the source's 418**, because `GroveFieldView.MaxZoom` is 1.0 and the
piece therefore never draws wider than about 265 canvas units; `SCALE` in the tool is derived
from that width, so changing one changes the other.

**It cost one line of engine code, and the seam it needed was already there.**
`HomesteadPiece.Animated` was built for exactly this — "a still resident and a flickering
lantern are both obviously reasonable" — and `AddressableAddresses.FrameFolders` had already
been extended to the grove against "the first animated decor piece would otherwise import as
loose sprites with no label and be unloadable with no error anywhere". Both held. The one gap
was `GroveBrowseAtlases.SourceOf`, which turned an address into `Art/<address>.png` and so
could not find a picture for a piece whose address is a *folder*; it resolves a folder to its
first frame now, which is what `HomesteadArt.SizeOf` already did when it wanted a still one. A
browse grid wants a still picture anyway.

Eight frames at 12fps — a two-thirds-of-a-second loop, and a third of the texture memory of a
twelve-frame one; 673 KB of PNG, loaded only into the grove's scope and only when somebody has
actually placed one. The catalog row is marked `_generated` rather than `_imported`, which is
how the two tools say which rows are theirs — without it the next `import_grove_art.py` run
would warn for ever about a row it no longer owns. Content is grove version 7; no save schema
change, no code change outside that one method, and the offline suite is unchanged at 681.

**Twelve props move now, and they move in the shop too.** The waterfall proved the flipbook
path for decor; `Tools/make_grove_animation.py` is the tool that uses it. Three torches, a
candle and a lantern flicker, two crystals breathe, three banners' streamers ripple, and the
well has water in it. None of the seventeen packs ships an animated asset, so all of it is
generated from the still art the pieces already had.

**One rule holds every recipe together: never draw outside what was already drawn.** Two
torches sit inside a glass globe, so a flame allowed to swell past its own silhouette is drawn
*over* the glass and the prop stops being a lantern — the first attempt did exactly that. So
each recipe either **clips** its work to a mask taken from the source (fire, gems, the
lantern's glass), **paints into** a region that was flat colour to begin with (the well's
shaft, which the still art leaves as grey), or **erases and redraws** a region that stood in
free space (a banner's streamers, which is why they can genuinely deform rather than merely
re-tint). Nothing is inpainted. The banner's own cloth is deliberately left still: it hangs
over the pole, so erasing it would leave a hole where the pole should be.

**And no soft glows, which is the second thing the first attempt got wrong.** The pack is flat
vector — not one gradient in it — so a blurred halo reads as a smudge, and worse, it clips at
the sprite's edge into a visible box. What carries the effect instead is a **brightness
swing**, which is in the idiom *and* is the cue that survives being shrunk to a 170-point shop
cell. That mattered more than usual here, because these had to read in the shop.

**The shop animates, and that reversed a rule.** `HomesteadArt.PaintThumb` used to say
outright that "nothing in a grid ever animates: thirty moving things is a grid nobody can
read" — which was true of the version where they all move in step. `PhaseOf` is what made it
safe: a cell starts at a frame derived from its piece id (FNV-1a, for the chest roll's reason —
stable across devices and visits), so eight torches on the edge shelf read as eight separate
fires rather than as a strobe. A tab's emblem stays still; a header flickering over a moving
grid is a header nobody can read.

**What it cost the atlases, which is the real price of animating a browse screen.** A shelf's
atlas holds a small copy of everything on it, so an animated piece now contributes one
thumbnail per frame — `GroveThumbs.Frame` names them, and **frame zero keeps the bare id**,
which is the whole compatibility decision: `Audit`, the picker and the buy panel go on asking
for the id and go on getting a sensible answer. Measured on the shipped catalog the shop went
**5.91 to 7.59 Mpx of thumbnails (+28%)**, concentrated where the animated pieces are — edge
0.58 → 1.51, structure 0.71 → 1.61 — so the largest single tab a player loads is about 1.6 Mpx.
The frame art itself is 1.8 MB on disk for all twelve, and it only reaches the grove's scope
when a piece is actually placed. Six frames at 12fps (eight for the waterfall) is half a second
a loop; a twelve-frame loop would have doubled both numbers for motion nobody would read as
smoother.

`GroveThumbs` lives in Domain because two assemblies have to agree about those names — the
Editor tool that packs the atlas and the screen that reads it — and a mismatch is invisible:
the atlas is generated, so a wrong name is not a missing file but a cell that quietly stops
moving. `Flipbook` gained an overload taking sprites already in hand, so the shop and the grove
run the same component rather than two that could drift.

**The boards — every keeper's grove has a standing, and you can walk into somebody else's.**
The Grovement was the one screen two accounts at the same progress did not share, and until
now nobody could see anybody's but their own. A leaderboard is the obvious feature and the
architecture is the whole of it: this is the first thing in the game that reads across
accounts, and the first number a player *benefits* from forging.

**What it cost the save file is nothing, and what it cost the security model is one new
adjudication.** Invariant 16g said a grove's score is derived, stores nothing, and would be
"forgeable in the one direction that would matter if a leaderboard ever reads it". This is
that leaderboard. `homesteadOwned`, `groveLandOwned` and `companionsOwned` are all
client-written, and `firestore.rules` justified letting them be with the sentence "a forged
entry buys a picture on a screen nobody else sees" — a sentence this feature makes false.
So the score moves to invariant 13's fourth clause: **bounded so tightly that forging it buys
nothing.** `publishGrove` opens the save with its own credentials and splits the worth in two.
The **earned** half — companions the keeper ladder reached — is derived from records the
server already validates for currency, so it is unforgeable by construction. The **bought**
half was paid for in credits, and credits are server-derived, so it is clamped:

    score = earned + min(bought, earnedCredits + grantedBaseline)

A save awarding itself the whole catalog scores exactly what its owner could have afforded.
The clamp is deliberately generous rather than exact — it counts currency ever *received*
rather than currency spent on the grove — because understating a leaderboard position is a
bug and overstating one is an exploit, and only one of those is recoverable.

**The save document stays private, and that is not a detail.** `players/{uid}` holds the level
ledger, the streak dates, the event floors and the ad allowance; a board needs a name, a
number and where the benches are. Widening its read rule would publish everything else with it
and make the save's *shape* a public API that could never change again. So a card is a
separate server-written document (`groves/{uid}`), holding only what a visitor draws — which
is also the natural place to put the one number that now needs adjudicating.

**Four decisions worth not re-litigating.**

**Ranking is a published distribution, never a global sort.** `stats.ts`' bargain, for the
second time: nine score deciles and a hundred-row board, rebuilt once a day by
`publishGroveRanks`, read as one document at O(1) whatever the player count. The alternative —
order the player collection by score and take the first hundred — is a hundred document reads
every time anybody opens the screen, against a collection that grows for the life of the game.
There is no scale at which the exact version buys a player anything they could notice, and
`GroveRankTable.TopPercent` answers "where do I stand" to within the point it is rounded to.
**Redis is the right tool for a live PvP ladder and the wrong one here**: a grove's worth moves
a few times a week, only ever upward, and Memorystore plus a Serverless VPC connector is an
always-on cost and an extra failure domain in front of a feature that must degrade to "no
board today".

**A league is the star rating the player already wears.** `GroveLeague` derives from
`GroveScoreTable.StarsFor`, so there is no second ladder to tune, no second thing to keep in
step with the catalog's growth, and nothing to explain — a player in the three-star league is a
player wearing three stars. `GroveScoreTable.MaxStars` caps the ladder at eight and there are
nine league ids, which `GroveBoardTests` pins: a content drop that lengthened the ladder past
the ids would put players on a board that does not exist.

**The client chooses *when* to publish and the server chooses *what*.** A Firestore trigger on
`players/{uid}` would rebuild a card on every sync — a function invocation per player per sync
for ever, for a thing that changes a handful of times a week and is not moved by a star, a
heart, a chest or a streak night. So `GrovePublishPolicy` debounces ten seconds over a
`GroveCard.Fingerprint` covering exactly what a visitor can see, and calls a callable whose
**request body is empty**. A player who never calls it is simply not on the board, which is the
shape a forgeable trigger should have. The published fingerprint is remembered in
`PlayerPrefs`, keyed by account — `RunGuard`'s reason, since "what this device uploaded" is a
fact about the device and goes both up and down, so it could never be joined (invariant 11b) —
without which the first request of every session would be a write for a grove that had not
moved.

**Visiting is one document read.** `GroveVisitScreen` is a screen of its own rather than a mode
on `HomesteadScreen`, which is a thousand lines of editing that would each need a branch saying
"not while visiting" — the mode toggle invariant 16 already refused once. It draws a
`GroveCard` and nothing else, so it is read-only by construction rather than by discipline, and
it uses the *same* projection this device publishes for its own grove, so visiting your own
grove and looking at it are the same picture. Its art loads into `AssetLibrary.GroveVisitScope`
— a third scope, because a scope owns its addresses and loading a stranger's grove into the
player's would free art the player's own screen is drawing the moment they left (invariant 7b).

**Two things this forced, both about strangers reading a string.** `GroveNames` is a second,
stricter rule on top of `RenameOverlay.Clean`: storage owes a bounded, trimmed string, and
*publication* owes one that cannot break the row below it. The bidirectional controls are the
reason it is not a length check — U+202E re-orders the text that *follows* it, so a name
carrying one misdraws the rest of the list, and a length cap and a word filter both sail
straight past. Whitespace is tested **before** the forbidden set, deliberately: a tab is a
control character *and* a word break, and deleting it turns two words into one. The word filter
lives only on the server, because a list shipped in a client is a list read out of the client,
and a refused name is not rejected — the player keeps it and appears under a handle the server
derives from their uid, which is also what gives two unnamed keepers rows that differ. The
opt-out is `settings.board` (a `StoredFlag`, so "never chosen" is a state), which cost the wire
nothing because `settings` was already merged, mapped and inside `hasOnly`; turning it off
raises a **withdrawal** rather than merely stopping the next rebuild, because a card left
standing after an opt-out is a data-protection failure rather than a stale cache.

**Four rules now exist twice, and all four fail silently.** The worth, the keeper level behind
it, the public name and the league. `firebase/shared/grove-vectors.json` pins them and both
halves run it — `GroveBoardTests.cs` and `functions/test/grove.mjs` — which is invariant 9a for
the boards. Two things that made it work are worth keeping: `GroveScore.Value` gained an
`IGroveHoldings` overload so the *shipped* summation is what the vectors drive rather than a
copy of it, and the name cases are carried as **code points** as well as strings, because
Unity's `JsonUtility` truncated `Fern‮Willow` at the escape and shifted every expectation
in the array by one field — a failure that read exactly like a bug in the sanitiser. The server
half asserts the two encodings agree, so the file cannot come to disagree with itself.

The vectors earned their keep on the first run: they caught the C# holdings resolving against
the ambient `AvatarCatalog` rather than the vector's own roster, and `GrovePublishPolicyTests`
caught the pending mark being consumed on the *reply* instead of at the start of the call —
which is `SyncScheduler.Started`'s rule and the exact shape that lost a keeper's name for a
year.

**Two more the deploy caught, and neither was reachable from a unit test.** They are the
argument for `firebase/e2e/smoke-test.mjs` existing at all, and both are now in it.

`deciles([])` returned nine `undefined`s, which **Firestore refuses as a document value** — so
the ranking job threw *after* writing ten board documents, leaving the boards published and
`config/groveRanks` absent. That is the state on the first day of the feature, when nobody has
a card, so it is the state it would have shipped in. `stats.ts` never hits it because its
buckets exist only once something is pushed into them. The lesson is narrower than "test the
empty case": **anything a scheduled job writes has to be checked for writability, not only for
arithmetic** — the old test asserted the sample count was zero and never that the result could
be written.

And the clamp read **`credits.grantedBaseline`, which is the name the wallet has on the way
out to a client, not the name it is stored under** (`credits.granted`). It silently yielded
zero, so the ceiling was derived earnings alone and every account seed, daily chest, streak
night, rewarded video and **real-money coin purchase** was left out of what a player could be
said to afford — somebody who bought forty dollars of coins and spent them on their grove
would have been clamped to near nothing and ranked last. Live, the ceiling went from 90 to
1,490 on the same account. Nothing but a live run could see it: the shared vectors take
`affordable` as a *parameter*, and **a clamp that is too tight looks exactly like a clamp that
is working**. The read is typed against `WalletDoc` now, so reaching for the reply's name is a
compile error rather than a zero.

Where the numbers land: **two documents read per screen**, a card is a couple of kilobytes, a
board is a hundred rows, and the whole feature adds **no save schema change** (v17 stands),
**no `progression.json` retune** and **no new concept in the reward path** — nothing here pays
currency. `config/grove` and the `keeper` curve are published by the seeder from
`homestead.json` and the manifest, for `config/products`' reason: a price list maintained beside
the content file is two files edited on different days.

One thing deliberately left: with more than `RANK_SAMPLE_SIZE` participants the global hundred
becomes the best hundred *seen* rather than the best hundred alive, because the ranking job
reads a bounded sample. That is why the screen leads with a percentile, which a bounded sample
answers accurately, and the fix when it matters is a scored index and a query — a change to
`summarise` alone.

**Live as of 2026-08-20.** Rules released, eleven functions deployed (`publishGrove`,
`withdrawGrove` and `publishGroveRanks` created; the eight existing ones updated, which also
took `appleNotification` and the hourly `sweepVoidedPurchases` live for the first time), and
`config/grove` seeded at grove v9 — 150 priced pieces, 8 regions, 30 companions, 5 home rungs,
a complete grove worth 493,770, matching what `Validate Content` prints. The smoke test is
**64/64 live**, of which 21 are the boards and 15 the names, and it attacks them from the client's
side: a
save claiming the entire catalog publishes a card worth 2,290, a bidi override is stripped from
the published name, a client cannot write its own card or a board, a second keeper can read the
card and cannot read the save behind it, and opting out takes the card down.

**Difficulty is a ramp and a live scalar — and the first finding was that the game had one
fail state, not four.** Reported as "the levels are very doable without losing any heart",
which was right, and the arithmetic said why. A glade allowed `2.6 × par` turns and
`2.0 × par` seconds, so running out of *turns* first needed **1.30 taps a second sustained
for the whole run** without solving — above the 1.35 the game asks of an expert replay, just
under the 1.8 `LevelValidator` calls drumming. `BoardView.Undo` also refunds the move while
the clock keeps running, so exploring was free in turns and paid in seconds. And `MoveBudget`
is floored at one turn past the one-star line, so a lower `budgetFactor` does nothing at all.
Lowering the turn count was therefore close to a no-op; **the clock is the fail state and the
budget is the backstop under somebody flailing.** That is now written down in `CONTENT.md`
rather than rediscovered.

**The star thresholds stopped being fractions of the limit, and that reversal is the whole
design.** They were `.50` and `.75` of the clock, chosen so retuning `timeFactor` moved all
three together and they could never drift apart — which also meant they could never move
*apart*, and difficulty and the economy want opposite things. Earned credits are derived from
the star ledger (invariant 9), so a 15% tighter clock was a 15% smaller three-star window and
a quietly poorer game: same clears, same stars on the panel, fewer credits per day, and a
493,770-credit catalog stretching past its 909 days. They are now `TimeGoldFactor = 1.00` and
`TimeSilverFactor = 1.50` **seconds per par turn** — numerically identical to what the old
fractions came to at the old factor, so nothing already earned moved — clamped to the limit,
because a threshold past the point the run is lost is one nothing can be measured against. A
glade tightened under its own gold line grades *finishing* as three stars, which is the only
reading that cannot punish somebody for something they did not do. `RunOutcome` had to start
carrying `TimeGold`: `WinOverlay` derived it from the limit, and the two are no longer
proportional.

**The ramp: `DefaultTimeFactor` 2.00 → 1.70, and all twenty glades author their own.** Flat
was the wrong shape — it punishes hardest where it costs retention and least where it
monetises. The Shallows runs **2.20 → 1.50**, opening *looser* than it used to (nothing about
a player's first ten minutes should end in a lost heart) and ending on a finale wall; the Mill
Vale runs **1.90 → 1.50**, with slack on the glade that teaches the crossing. A new mechanic
gets slack on its own glade. The target is a clear *rate* — roughly 85% of first attempts
early, 60% late, finales lower — not a feeling, and the honest position is that nobody knows
the real numbers until the game is live.

**Which is why `difficulty.clockScale` exists.** One published number multiplying every
glade's limit, bounded to **0.6–2.0** by `DifficultyLimits`, applied in exactly one place
(`LevelTuning.TimeLimitMillis`). Every other number in `progression.json` was tuned against
something observable; difficulty was tuned by people who already knew every solution, which is
the one thing no player will ever be. Four properties keep it safe. The band is a **constant a
file cannot move** — this is the only block whose bad push is an unfinishable game rather than
a worse deal, which is `HeartLimits.HardCeiling`'s argument one notch sharper. It reaches the
limit and **never the stars**, so a difficulty push cannot retune the economy behind it. It
reaches **nothing that is stored** — a run records elapsed play time, never time left, so
`bestMillis`, the map badge and `publishGroveStats` all needed no migration and no deploy,
which is `CountdownTests`' invariant tested against the first change that could have broken it.
And `LevelValidator.CheckClock` now warns when a glade **could not survive being pushed to the
0.6 floor**, because that retune never passes back through the validator.

Two things it deliberately is not. It is **not published to `config/progression`** — nothing
about a clock is adjudicated, so the server has no opinion, exactly as it has none about the
heart gate. And it is **not live yet in the sense the word usually means**: the client reads
`progression.json` through `ContentBootstrap.LocalSource`, so this becomes a minutes-not-days
lever the moment `ContentConfig.RemoteBaseUrl` is set, and until then it is exactly as live as
the heart gate, the chest odds and the ad payouts already are. That one setting is now the
highest-value unshipped thing in the build.

No save schema change (v17 stands), no `progression.json` retune of anything that existed, no
server work and no deploy. `DifficultyRuleTableTests` adds 9 cases and `CountdownTests` gained
the pin for the shipped tuning; the offline suite is **723**.

**Chapter three: The Amberwood, and colour as the subject.** Ten glades, and the first
chapter to introduce no new rule at all — which was the bet the whole content design rests
on. The Shallows brought brittle stone, roots, taproots and duskcaps; the Mill Vale brought
the crossing. A third mechanic in three chapters would have been a habit rather than a
decision, and the answer to "this will get boring" has always been *modifiers to the one
verb*, not more verbs. So the Amberwood is about the one thing the vocabulary already had
and no chapter had ever been **about**: a blend needs its own pair of crystals, a purity is
ruined by a single wrong join, and a crossing is what lets the two share ground.

**The thesis is counted, not asserted.** A board can be *about* colour and still have no
place a wrong turn costs anything — the nets simply never touch, and the glade is a
rotation exercise with a colour theme painted on. So the authoring pass measures
**hazards**: pairs of neighbouring tiles in different networks that some reachable rotation
would mate, plus every twisted crossing carrying two networks (turning one swaps which arm
belongs to which strand, and there is no rotation that does not). The first cut of
*Rootbound Amber* scored **zero** and read perfectly — the amber ridge and the green foot
were three rows apart. It was rebuilt until the two combs interleave. The shipped ten run
5 to 20 hazards each.

**Straight crossings and twisted ones are two different things and the chapter uses both.**
A straight crossing (`=NS+EW`) is inert, owes no turns and cannot be misturned: architecture,
a bridge somebody built. A twisted one (`=NW+ES`) is worth exactly one tap and *both* lanes
turn with it. The chapter's second glade teaches the chromatic use and carries two twisted
ones, deliberately authored off-solution — a twisted crossing left at `/0` is scenery on the
glade whose whole subject is that it is not. The later duskcap boards ford the dark under the
light through straight ones, which is what a ford is.

The ladder runs par **44, 50, 46, 53, 57, 45, 62, 58, 55, 70** and `timeFactor`
**1.75 → 1.45**. Not monotonic, and the dip at glade six is the point: it is the taproot
board, and a bound board's par is *lower* than its tile count suggests because one tap moves
three conduits. 1.45 is one notch past both previous finales and still clears the 0.6
`clockScale` floor with room, which `CheckClock` proves rather than anybody trusting.

**The map is five strips, and the number is arithmetic rather than taste.**
`make_chapter_art.py` scales a source to whole strips and trims surplus width from the
centre — but only if the scaled source is *wider* than 1080. The Amberwood's source is
892x4745, so five strips scale it to 1128 wide (trim 48, invisible) and four would have
forced 902 up to 1080 and stretched the whole map sideways by a fifth. The ten backdrops are
graded from two layers of one jungle painting, which is why a wood shot through with light
shafts comes out as ten different times of day.

**What the chapter cost, and what it found.** No save schema change (v17 stands), no
`progression.json` retune, no new art beyond the chapter's own strips and backdrops, no new
loc key that is not derived from an id, and no server work beyond re-seeding — `config/progression`
now carries 30 levels. What it *found* is invariant 5c: authoring against `LevelValidator`
for a day turned up a rooted tile in the Mill Vale that had shipped a board the validator
had never actually proved. The check now exists in all three places the rules live —
`LevelValidator`, `Tools/verify/content.py` and `Tools/verify/author.py` — because a rule
proved in one of them is a rule that drifts out of the other two.

**Keeper names are unique — and it cost the save file nothing.** Two groves could stand on a
board under one name, and the fix is one collection: `names/{fold}`, holding a uid, created in a
transaction. Uniqueness is the **document id**, so it is enforced by Firestore's primary key at
any concurrency, with no index, no scan and no query — see invariant 19d for why the obvious
alternative is both racy and the shape that grows with the game.

**The cost split is the design, and it is the part worth not re-litigating.** The hint under the
rename field is a **direct document read** — one read, no function invocation — because it happens
while somebody is typing; the claim is a **callable**, because it is the only part that has to be
adjudicated, and it happens once or twice in the life of an account. The rules grant `get` and
refuse `list`, so a player may ask about a name they typed and nobody may walk the reservations.
`NameCheckScheduler` is what keeps the hint honest about cost: a pause rather than a keystroke,
answers remembered, and a name that could never be reserved refused locally where it is free.
Measured on the shipped debounce, a sixteen-character name typed straight through is **one read**;
without it, sixteen — roughly a tenfold difference in the bill over the life of the game, which is
why it is a class with no Unity types and eleven offline tests rather than a comment.

**Uniqueness could never live in the save**, and that is what kept the schema at v17.
`wallet.displayName` is still a preference merged by recency (invariant 11c) — no rule over two
devices can decide a global fact — so the name became invariant 13's fourth clause: the client's
copy is what its own screens draw, and the reservation is what a stranger sees. `boardName` now
reads the confirmed name off the wallet document rather than the save, which is a security
improvement on its own (invariant 19f) and cost one field on the one document a client cannot
write.

**A rename never fails.** Offline, signed out, or in a build with no backend, the name is stored
and the panel closes; `publishGrove` claims whatever the save asks for when it differs from what
is held, so the offline path needed no client-side retry state whatever. Re-claiming a name
already held writes nothing, which is what makes that free. The panel keeps itself open for the
two refusals a player can act on — **taken** (pick another) and **cooldown** (wait, and it says
how long) — and closes for the two it cannot: **refused** applies the name and says it will not
appear on the boards, because a name that quietly does not appear reads as the boards being
broken, and **unavailable** is silent because renaming on a train should feel ordinary.

**The vectors earned their keep on the first Editor run, three times over.** Unity's Mono and
Node's ICU disagree about Unicode, and nothing but running both halves against one file can see
it: `İzmir` folded two ways (U+0130 is the one character whose lowercase is longer than itself),
Greek names ending in Σ diverged on Final_Sigma, Mono does not decompose the Latin ligature block,
and Cherokee and Georgian Mtavruli were given lowercase after Mono's tables froze. `Agree` closes
those by hand and **stops there on purpose** — 27 of the BMP's 256 blocks still disagree
somewhere, measured rather than guessed, and closing them would mean shipping normalisation tables
in a client to make a hint exact. Invariant 19e has why that is safe.

**One bug found on the way, and it is invariant 12a one document over.** Every wallet write is
`transaction.set` with no merge, so a field `readWallet` does not copy is a field the next sync
deletes — the name would have vanished from the board on the player's next chest, silently, and
the next publish would have re-claimed it, so all anybody would ever see is their name
occasionally missing. `readWallet` carries it through now. The same read also stopped deciding
"brand new account" from `snapshot.exists`, which stopped being that question the moment a second
feature wrote to the document: a name claimed before the first sync would have created the wallet
and handed the account the unbounded first streak claim that the seeded floor exists to prevent.

Where the numbers land: **a check is one document read, a claim is one transaction**, renames are
one or two per account ever, and nothing here scales with player count per operation. Save schema
**v17 stands**, `progression.json` is untouched, and no reward path changed — nothing about a name
pays currency. `NameCheckTests` adds 19 offline cases and `GroveBoardTests` three; the offline
suite is **749**, the Editor suite **855** and the server suite **591**, all green.

**What the panel does was moved out of the panel.** `RenameRules` holds the two branching
decisions — what the line under the field reads, and what a claim's answer is worth — because a
`switch` inside a `MonoBehaviour` is the one place here nothing can be proved about. The property
worth having is written as one assertion over the enum: **for every answer the server can give,
either the name is stored or the panel stays open with something to read, never neither.** A
rename that silently vanishes is the failure this codebase has already shipped once for a
different reason (invariant 11c), and that test covers outcomes added later without being edited.

**Two fixes that had no guard now have one.** `functions/test/wallet.mjs` replays the
whole-document write every wallet function performs, three times over, and fails if the name is
dropped; it also pins that "brand new account" is decided by whether the server ever recorded
currency rather than by the document existing. Both were checked against the *old* code first —
they fail 5 assertions on it, which is the only way to know a guard guards anything. **Deployed 2026-08-20**: rules
released with the `names` block, `claimName` created and the other eleven functions updated
(`wallet.ts` and `grove.ts` are shared by all of them). The smoke test is **64/64 live**, the last
fifteen of which walk two accounts racing for one name.

One thing the live run caught that no unit test could, and it is this suite's own recurring
lesson: the case hard-coded a name, and **a reservation is permanent and global**, so the first
run claimed it and every run afterwards was correctly told it was taken. It uses a per-run tag now,
exactly as the spend ids do. Before launch, the reservations that leaves behind get swept with the
synthetic saves.

**Hints are a pool, not an allowance — save schema v19.** A hint was three per glade,
handed back in full at every board, charging +2 moves and stored nowhere. So it cost
nothing and meant nothing: the only players who never used one were the ones who had not
found the button. It is now an account-wide resource on a clock — **three in the pool, one
back every eight hours** — spent wherever the player decides it is worth spending, which
makes using one a decision. `LevelTuning.HintAllowance` is gone and must not come back.

**The arithmetic is not written out twice.** Hearts had a produced/spent/due ledger with a
merge proof attached (invariant 11b), and a second pool needs exactly that ledger with
different numbers. Copying it is invariant 5b's mistake — five correct copies of "is this
tile solved" until a tile appeared one of them had not been written for — and a lossless
merge across an unknown number of devices is a worse thing to hold two copies of than a
mask comparison. So `RegenLedger` (Domain/Persistence) is the walk, the spend, the grant
and the join; `RegenPeriod` is how long one wait lasts and whether anything shortens it;
`Hearts` and `Hints` are thin wrappers holding what is genuinely different about each.
`Hearts` kept its whole public surface, so **the 37 existing heart tests are the proof the
extraction changed nothing** — that was the point of doing it this way round.

Four decisions worth not re-litigating. **The pool lives in `wallet`**, which is what kept
this off the server entirely: `firestore.rules` constrains the save's top-level keys and has
never constrained the wallet map's inner ones, so invariant 12a's four places came to three
and **no rules deploy is needed**. **A v18 file needs no migration code** — `hintsProduced`
of zero is unreachable for a real ledger (an account is seeded at the cap and the counter
only rises), so an older save reads as a fresh full pool, which is v13's sentinel argument
for the fourth time. **The ceiling equals the cap**, unlike hearts, so a granted hint at a
full pool is *refused* rather than clamped — safe only because `RewardedAds.WouldBenefit`
grew a second branch and hides the offer there. And **a hint charges no moves**: the hint is
now the price, and charging moves as well is two punishments for one decision, the second of
them invisible until the victory panel counts a star the player did not know they had lost.

`hint_refill` is the fifth rewarded placement — offered from the hint button when the pool
is empty, paying one, capped at five a day. It pays no currency, so `adCurrencyOf` answers
null and the callback grants nothing; it needed **no server code beyond two list entries**
(`AD_PLACEMENTS` and the seeder's `known`/`kinds`), both of which would otherwise have made
the next `seed-config.mjs` run throw. Its LevelPlay ad units are created under both apps and
filled in (`AdConfig`), with the reward item name set to `hint_refill` on each so
`namedPlacement` can attribute a callback — which this placement never needs, since a hint
is not currency, but which keeps the callback log free of refusals nobody would look into.

The pool is content (`hints` in `progression.json`, `HintRuleTable`, `HintLimits`), beside
the heart gate and for its reason: hearts decide how many attempts a day a player gets and
hints decide how many of those they can rescue, so both multiply the count of glades
finished per day, which is what every credit figure in that file is paid per.

One thing deliberately *not* added: a permanent validator warning about the
ceiling-equals-cap shape. It is the shipped configuration, so the warning would fire on
every run for ever, and a warning that always fires is one nobody reads — the lesson
invariant 4c states about the word "synced". Both validators print it as a fact instead, and
the thing that has to be true because of it is held by code and pinned by a test.

The one hazard this created is worth naming because the compiler was no help: removing
`hintAllowance` from `LevelTuning`'s constructor left an `int` gap that five tests happily
filled with their next positional argument, so `3` became a `budgetFactor` of 3. It compiled
clean and the suite caught it. Same shape as `WithTime` versus `WithRun`.

One bug this introduced and a review caught before it shipped: `AdOfferOverlay` raises
**exactly one** of `Rewarded` and `Dismissed` (the paid branch does not also dismiss), so
unlocking the board in only the dismissed handler left a player who actually watched the
video sitting on a frozen board with a stopped clock. Both handlers call `CloseHintOffer`
now, which restores the latch to what it was rather than clearing it. Third time this file
has learned that the safe outcome has to be what *every* exit does.

`HintsTests` adds 21 offline cases and `CloudWireTests` two; the offline suite is **771**,
the Editor suite **890 of 891** (the one failure is the batch-mode bundle-id artefact, not a
defect), and the server suite unchanged and green.

**Live as of 2026-08-21.** `config/progression` re-seeded — five ad placements now, with
`hint_refill` paying one hint — and all twelve functions redeployed for the one line
`hint_refill` added to `AD_PLACEMENTS`. The smoke test is **64/64 live** against the new
deploy. No `firestore.rules` change was needed or made: `hasOnly` constrains the save's
top-level keys and has never constrained the wallet map's inner ones. Both LevelPlay ad units
exist and are filled in `AdConfig`.

**Chapters two and three, rebuilt — the mechanics reject something now.** Reported from
play as "the glades are pretty easy, and some mechanics don't even make sense — I can
complete a glade with brittle conduits, taproots and duskcaps on it as if they aren't
there". That was exactly right, and it turned out to be measurable rather than a matter of
taste. `Tools/verify/difficulty.py` is the instrument: it enumerates every arrangement in
which **every arm mates and none dangles** — the tidy boards a player plausibly arrives at
— and then asks which of them win.

**Twenty-two of the thirty shipped glades had exactly one such arrangement.** That is the
whole diagnosis in one number: if the arms admit one tidy board and that board wins, then
the brittle stone, the taproots and the duskcaps rejected nothing and could all have been
deleted without changing a single solution. `dark` — arrangements the duskcaps alone
reject — was **zero on every duskcap board that had ever shipped**. Every brittle conduit
sat on a tile the arms already forced, so it never asked anybody to know anything. Every
taproot removed nothing.

Two causes, and both are now written into `CONTENT.md` under *What makes a glade hard*.

**The boards were open ground.** Corridors a tile or two wide with empty cells either side,
so an arm had almost nowhere to point and nearly every tile read at a glance. Filling the
ground is the fix, with two riders: no four-armed conduits (inert, so they read as
nothing), and no spine along the board's edge — a straight or a tee on an edge is forced by
the edge, while a critter or an elbow is not. On the shipped 7x7s that is the difference
between `glance 21/49` and `40/49`.

**And a twisted crossing is the cheapest honest decision a board can carry**, which is what
every other mechanic now rides on. It wears all four arms at every angle, so nothing about
the arms can settle it and only colour or the dark can. Brittle stone therefore sits on a
crossing — a tile the player *cannot* simply try — at `~2` against one turn owed, which is
exactly one wrong guess. A taproot binds two crossings in opposite corners, so one tap
answers both. And a duskcap's ford sits on a **cycle** of the live network, which is the
single decision that separates a shadow that matters from a shadow that is scenery: turning
that ford joins the dark to the grove *while every critter stays lit*, so it is an
arrangement that looks finished and will not settle. If the wrong turn also puts a critter
out, the critter tells the player and the duskcap taught them nothing.

**`hazards` was the wrong metric and a whole chapter was authored to it.** It counts places
where *some* rotation would mate two networks — but such a rotation almost always leaves an
arm dangling somewhere else, so it is not a board anybody reaches. The Amberwood scored 5 to
20 hazards a glade and still admitted one tidy arrangement on eight of ten boards.

Where the twenty rebuilt glades land: `arms` runs 2 → 32 with **`wins` 1 on every one of
them**, `glance` runs 21/42 to 48/56, both duskcap boards in each chapter reject six or
seven arrangements the critters accept, every brittle conduit is a tile only colour can
settle, and both taproots remove real arrangements. Every glade kept its id, its name, its
map position, its palette and its subject — a `LevelId` is permanent (invariant 1) and the
art is cut from the chapter's own JSON, so nothing outside the two `rows` arrays moved.

**What this cost, and what it did not.** No save schema change (v17 stands), no
`progression.json` retune, no server work, no new art and no new loc key that is not derived
from an id — six `lesson` strings were rewritten because their boards changed. `timeFactor`
runs a notch looser at the head of each chapter and lands where it did at the foot (the Vale
1.95 → 1.55, the Amberwood 1.85 → 1.50): a board that has to be *read* rather than fitted
needs a little more room to be read in, and the star lines do not move with it — three stars
is `par × 1.00` seconds however long the limit is — so a clear is worth exactly what it was
worth and only the point where a run is *lost* moved. Difficulty went into the boards, which
is where it was asked for. `clockScale` remains the lever once there is a clear-rate to aim
at.

The Mill Vale now has a Python source beside the Amberwood's (`Tools/chapters/c02_millvale.py`),
both `--check` clean against the shipped JSON. Offline suite 750, Editor suite 870, `Validate
Content` 30 levels across 3 chapters with no errors.

One thing this exposed and did **not** fix, because it is a different feature: `Sync Manifest`
bumps a chapter's `version` only when its **level list** changes, so rewriting every board in
a chapter leaves the version alone — and `ContentRefresher` uses exactly that number to decide
whether to refetch a cached body. Harmless while `ContentConfig.RemoteBaseUrl` is unset and the
bodies ship inside the app; the day remote delivery is switched on, a content-only drop would
never reach anybody who had already cached the chapter. The fix wants a digest of the body in
the manifest entry, which `ManifestSync.SurvivesRoundTrip` would then police.

**Switching accounts, rebuilt — the switch stopped being a download.** Reported from play,
and the report is worth quoting because every sentence in it was the game's fault: switching
between two of the owner's own Google accounts gave "couldn't load this account's progress",
then a panel reading "this phone is signed in as someone else" with a **FIX THIS** button,
then — after choosing the same account again — "this account is already used by another
player · you will lose 26 finished levels and level 7 from this phone". Nothing was lost and
nothing was ever at risk. One document read had failed.

**The root cause was an ordering, not a bug.** The switch was secure → authenticate → fetch →
replace, so *reading the incoming grove over the network decided whether the switch happened*.
That read runs in the frame after an OAuth browser hands control back, which is the single most
fragile moment in an app's life — the process has just been foregrounded and the Firestore
stream has just been re-authenticated — and its failure left the device authenticated as one
account holding another's save, syncing nothing, with `AccountGate` correctly refusing
everything. The recovery path then made it worse: tapping a provider from that state ran
`ResumeAccountAsync`, which proceeds *only* if the credential names the account the save
already belongs to, and answered a legitimate second account with `DifferentAccount` — which
the panel rendered as the destructive adopt prompt, priced in glades that were sitting safely
on the server the whole time.

**`SaveService.SwitchTo` is the fix and invariant 17a is the rule.** The swap is local: the
outgoing grove is copied into `IAccountArchive` under its own account and the incoming one
restored from there if this device has played it before, so once the credential is in hand the
switch is finished and cannot stop halfway. The server is asked afterwards by an ordinary sync
— pull, monotonic join, push, retried on `SyncScheduler`'s backoff like every other sync — so
its failure decides which of two *true* sentences the screen says and nothing else. Switching
back to an account played here before needs no network at all.

`AccountArchiveStore` is one folder per account under `accounts/`, each holding an ordinary save
file, and it **reuses `SaveStore`** rather than writing JSON itself: the atomic write, the backup
rotation, the corrupt-file recovery and the checksum are the hard parts of persistence, they are
already right, and they are already tested against a real filesystem — invariant 5b's lesson in
the file with least reason to relearn it. The folder name is an FNV-1a hash of the uid (a uid is
an opaque provider token and this build must never be why one turns out not to be a legal path
segment) and the id is stored *inside* the file, which is what makes the hash safe: a slot that
does not name the account being asked for is discarded rather than adopted. Six slots are kept,
newest first — it is a **cache, not a backup**, so an eviction loses a copy and never a grove.

**Four states stopped existing, and one refusal grew a repair.** `SwitchOutcome.Interrupted`
and `DifferentAccount` are gone with `ResumeAccountAsync`; `SwitchOutcome.Pending` replaces
them, meaning "signed in, and the server has not been reached, so it cannot yet say whether
this account has a grove" — which exists so the panel never tells somebody with three chapters
behind them that they are starting fresh. And `AccountGate`'s refusal is now completed
**forward**, silently: Firebase persists its signed-in user before this code sees it and the
session only ever moves because a player chose an account, so a disagreement means the
authentication got further than the file did. `SaveService.Wipe` went with all of it — nothing
called it once a switch stopped meaning "erase what is here", and erasing cleared the pre-1.0
PlayerPrefs keys, which belong to whoever installed the game on the handset rather than to
whichever account is signed in this minute.

**Two smaller faults found on the way, both of which put a false sentence on screen.** The
securing sync used `SyncAsync`, which answers `Busy` the instant the latch is held — and a sync
starts on every foreground, which is exactly when somebody opens the account panel, so the
commonest moment to switch was the likeliest to be told "we could not save your grove".
`SyncPatientlyAsync` waits it out (`ClaimAsync`'s reasoning one level up, same budget) and
retries contention only. And closing the provider's sheet was classified as
`CloudFailure.Offline`, so backing out of a consent screen said "no internet connection";
`CloudFailure.Cancelled` is its own answer now.

**The panel can finally name the account.** `CloudIdentity.Label` carries the provider's email
or display name — display only, never compared, never stored, never keyed on — because two
Google accounts belonging to one person routinely share a display name, and without it both
sides of a switch said "your progress is saved online" and neither said *which* grove was on
the phone. Google's scopes gained `email` for it. Every other string in the flow was rewritten
under one rule: **never name a loss that is not happening, and never call the player's own
second account a stranger.** The destructive prompt survives, narrowed to the one case it was
ever about — a *guest* whose provider already carries a grove, reachable from linking and from
nothing else — so its copy can say what is actually true of a guest.

**One thing the Editor run caught that no offline test could, and it is a product bug rather
than a fixture one.** Whether a switch reports *"welcome back · 26 finished levels"* or *"here
is a new grove"* was read off `PlayerProgression.ClearedGlades`, which drops any record the
catalog has never heard of — correct there, because an unrecognised level must never mint
credits, and wrong for a sentence. Before the content index has loaded it answers **zero**, so
the panel would greet a three-chapter grove as an empty one. `PlayerProgress.ClearedCount`
counts the records themselves and is what the account screen asks now, along with `HoldsAGrove`
— which also gates the destructive prompt, where the same zero would have removed a warning
rather than added one.

**What this cost:** no save schema change (v19 stands), no `progression.json` retune, no server
work and no deploy — `firestore.rules` is untouched, because an archive never leaves the device.
`AccountArchiveTests` adds 7 cases and `AccountSwitchTests` was rewritten to 21; the offline
suite is **787**. Twelve of the feature's cases still need Test Runner, all of them because
`RunGuard` and `Application.temporaryCachePath` are `PlayerPrefs` and native by design.

One thing worth having anyway: `Tools/verify/runner` now clears `Debug.unityLogger.logEnabled`
before running. `Debug.Log` reaches an extern handler, so the *first log line on a path* used to
end a test with "ECall methods must be packaged into a system module" before any assertion was
evaluated — which quietly decided which rules could be proved offline, and the cloud and save
layers warn on exactly the paths worth testing. Measured on this suite it moves **fourteen**
cases from Editor-only to offline (110 → 96), including the two that matter most here: that a
sync never pushes a grove to an account it does not belong to, and that a repair never mixes two
groves together. The cost is that a test asserting on log output cannot run offline — those use
`LogAssert`, which needs the Editor anyway.

**Consent, ATT and app-ads.txt — the ad plumbing that decides what an impression is worth.**
Reported as "how will I show ads to millions of users" after a day of `Mediation No fill (509)`
on every placement. The 509s were demand-side and expected (two adapters installed, app not on
a store yet), but the question exposed three things that were genuinely missing and would each
have cost real money silently.

**Nothing here changes gameplay and nothing here is stored.** The save file gains no field,
`progression.json` gains no block, and no server code moved. That is not a small mercy — it is
the design. A consent answer is per-device, revocable and therefore **not monotonic**, which is
exactly the shape invariant 11b forbids in a save file; and the CMP already keeps its own
record in the form the ad networks read. A copy of ours would be a second source of truth for a
value we neither own nor parse, and it would be the copy that went stale.

**The ordering is the whole feature, and it lives in one method.** `RewardedAds.StartAsync`
resolves consent, applies it to the provider, and only then initialises. A mediation SDK that
starts before it has been told has already decided what it may collect and has already
auctioned on that decision — a signal applied afterwards changes the next request and cannot
undo the first. Put anywhere a caller can get the order wrong, it eventually is wrong, so
`Boot` installs and the splash calls one thing. `LevelPlayAdProvider.InitializeAsync` also
refuses to start unconfigured, applying `AdPrivacySignals.Restricted` and warning, because the
failure it prevents is unrecoverable in the one direction that matters.

Four decisions worth not re-litigating. **The seam is `IConsentGateway` in Domain**, naming no
SDK type — `IAdProvider`'s bargain for the third time, and it is what lets the ordering, the
derived rule and the failure paths all be proved offline against a fake, on a subsystem the
Editor never exercises. **The CMP is Google UMP** rather than a dialog of our own: a hand-rolled
prompt cannot tell whether a player is in the EEA (so it interrupts everybody or nobody), cannot
write the IAB TCF string the adapters actually read (so a consenting player still counts as
non-consented and the revenue never arrives), and does not satisfy Google's EU User Consent
Policy once AdMob is in the waterfall. **Every failure lands on the restrictive answer** — a CMP
that throws, times out, or was never installed leaves personalisation off and the game running,
because the cost of guessing the other way is personalised ads served to somebody who was never
asked. And **a build with no ad provider asks nobody anything**, which is a guard rather than a
happy accident: without it a consent form and Apple's prompt would appear on a build that cannot
show a single ad.

**ATT is hand-written rather than a package** (`AppTrackingPrompt` plus a 40-line `.mm`). Unity's
iOS-support package is another dependency to resolve for one framework call, and the mediation
SDKs that ship their own helper each want to own the timing — which is the one thing that has to
stay ours. It **polls rather than taking Apple's completion handler**, deliberately: bridging a
callback needs a static function pointer and a `MonoPInvokeCallback`, which is a known way to
crash on IL2CPP, for a value already readable from the framework. `IosPrivacyPlist` writes
`NSUserTrackingUsageDescription` at build time, because Unity has no field for it and iOS
**silently refuses to show the prompt** without it — the dialog never appears, every player is
non-consented, and the build passes review with iOS revenue near zero.

`app-ads.txt` is in the repository root rather than pasted onto a server once, so it is reviewed
in a diff beside the mediation change that made it wrong. Every line in it is a placeholder and
the file says so.

**The CMP is installed as a vendored UPM tarball, and the route matters.** Google publishes the
Mobile Ads plugin to GitHub as a `.unitypackage` and to OpenUPM as a package, and to
`dl.google.com` — where every other Google package here comes from — not at all. The
`.unitypackage` is the wrong one and would fail *silently*: it unpacks as loose files under
`Assets/`, so it is not a package, so it carries no version, so `versionDefines` never fires,
`GLIMMER_UMP` is never defined, `UmpConsentGateway` compiles to nothing and nobody is ever asked
anything. So `GooglePackages/fetch.ps1` grew a per-package registry and pulls
`com.google.ads.mobile` 11.4.0 from OpenUPM beside the Firebase tarballs, and
`Packages/manifest.json` references it by relative path exactly as they are. EDM went to
**1.2.187** with it, which the ads plugin requires and Firebase accepts — a UPM dependency
version is a minimum, not a pin.

Every symbol the gateway uses was checked against the shipped `GoogleMobileAds.Ump.dll` before
this was believed, because the file had been written against documentation alone. It still has
**never been compiled**: the offline check deliberately builds the privacy assembly *without*
the define, which proves only that a fresh clone compiles, so it wants a real compile the first
time the Editor resolves the package.

**The next Android build will fail until an AdMob app id is set**, and that is by design rather
than a bug — `ManifestProcessor` calls `StopBuildWithMessage` on an empty id rather than letting
a build ship that would crash at launch. So installing the CMP requires an AdMob account and an
app registered in it, which is wanted anyway: AdMob is the largest source of rewarded demand and
is also what makes the certified-CMP requirement bite. Set it in
`Assets ▸ Google Mobile Ads ▸ Settings`. Then: fill in `app-ads.txt` from each network's
dashboard and host it on the domain in **both** store listings, add the adapters that are
actually missing (AdMob first — it is the largest source of rewarded demand, and it is also what
makes the certified-CMP requirement bite), and turn on in-app bidding.

`PrivacyTests` adds 11 offline cases; the offline suite is **798**.

**A guest can buy, and is told what that costs — no field, no schema bump, no deploy.** The
game signs a player in anonymously on the splash and is fully playable having never seen
`AccountOverlay`, which is right; what was missing is that the shop takes real money on that
same anonymous account. An anonymous credential lives in the app's own storage and no server
can mint it again, so a reinstall or a lost phone takes the uid and everything keyed on it.
For progress that is bad. For money it is worse and differently shaped: `receipts/{store}__{txn}`
is keyed **globally** — correctly, because replaying one real receipt across thousands of
accounts is the industrialised attack (invariant 18a) — so a fresh installation presenting the
same transaction is refused as a replay. A purchase made anonymously and then lost is therefore
**unrecoverable by any route, including the stores' own restore**. Nobody can give it back.

**The answer is not a login wall, and that is the design rather than a concession.** Blocking
the buy is where conversion dies: the payment sheet *is* the confirmation (`ShopScreen.Tap`
already argues it), and an OAuth consent screen is worse than a panel because it backgrounds the
app mid-decision — a player talked out of a purchase by the dialog protecting that purchase.
Nobody in the market gates purchases behind login either. So the warning splits in two by
**cost to the player**: a standing bar that costs no tap, and one modal placed where it costs no
sale.

**The bar** sits under the shelf tabs on the three shelves priced in money, and reaches the grid
rather than floating over it — `PaintNotice` reserves its height out of the viewport only while
it is drawn, so a linked player and the supplies shelf are pixel-identical to what shipped.
Supplies are deliberately exempt: hearts and boosts are bought with gems and live in the save,
which merges into whatever account the device eventually links, so nothing bought there can be
lost and a sentence that is false on one shelf is how a warning gets read past everywhere.

**The modal** is chained behind `ShopGrantOverlay` on `Dismissed`, so it is raised only after the
server has actually granted — the sale banked, the goods on screen, and "keep what you just
bought" the easiest sentence in the game to agree with. `Dismissed` is raised from `OnDestroy`
for `AdOfferOverlay`'s reason: four exits, and anything wired to one of them fires from one of
them.

**`AccountPromptPolicy` (Domain) is the rule, and the split it makes is the part worth not
re-litigating.** Two budgets — 2 chapter asks, 3 purchase asks — because those reach different
populations and a player who buys gems in week one must not burn the nudge aimed at everybody
who never will. **One shared quiet period** (48h), because that answers a different question:
not "have we made this case yet" but "how often may this game interrupt somebody", and the
answer cannot depend on which subsystem is interrupting. Without it, finishing a chapter and
then buying a coin pack meets two account panels inside a minute. The generous spacing is safe
precisely *because* the bar carries the message between asks.

It holds no clock and reaches nothing — handed the time and the account's state, `SyncScheduler`'s
bargain — so all sixteen cases run offline, which matters here more than usual because every
state it is about (a live session, a real purchase, a device away two days) is one the Editor
never reaches. Persistence is the caller's, `GrovePublishPolicy`'s rule: the counts are
`PlayerPrefs` and must never enter the save, since merged they would arrive on a second device
as a reason to stay quiet — backwards, because a second device is a player with *more* to lose.
`AccountPrompts` keeps the shipped `account_prompt_count` key, so an installation that has
already declined twice is not handed a fresh allowance. One case earns the file on its own:
the obvious `now - last < Quiet` is negative when a player moves their device clock forward,
reads as "inside the quiet period", and silences every prompt for the life of the installation
with nothing able to write a smaller stamp. Checked against the naive rule first — it fails
exactly that test.

**`CloudSaveService.IdentityChanged` came out of it and is the more general fix.** Every screen
saying something about the account samples `IsLinked` once in `Build`, and nothing told any of
them when it moved — so a player who tapped the bar, linked, and came back met a shelf still
saying their purchases were stranded. The panel that changes it has four exits, which is the
companion screens' bug exactly; an event cannot be forgotten. `NoteIdentity` compares before it
raises, so it is idempotent and safe from anywhere; `Agreed`/`Disagreed` call it for immediacy
and `Tick` polls it at 2Hz as the backstop, because `CurrentIdentity` walks the provider list
and builds a label, and doing that per frame is how a menu screen starts allocating. The first
sample is recorded silently — there is nothing to announce about the state the game booted in.

What it cost: **no save schema change (v17 stands), no `progression.json` retune, no server work
and no deploy** — nothing here adjudicates anything. Two loc keys, `AccountPromptTests` adds 16
offline cases, and the offline suite is **814**.

**`ProfileScreen.BuildBody` was fixed to take the event, and it needed three things rather than
one.** It creates the scrolling body and is called again whenever something moves more than one
card — a card's position is the running cursor, so one that changes height moves every card
below it. It **never destroyed what it replaced**, so the board-visibility toggle left a whole
viewport behind on every tap, stacked over the live one and still carrying its invisible drag
catcher: a leak that also stopped the page scrolling properly. It now hides before destroying,
which this screen already did in `PaintCompanions` and did not do here. It **replayed the
staggered entrance** on every rebuild, so `_entered` splits arriving from redrawing — `GridView`'s
`Show`/`Refresh` rule, in the one place on this screen that had no equivalent, and anything
raised by an event is a redraw. And it **returned the player to the top of the page**, so the
scroll offset is now carried across and clamped to the new content height, since the account card
grows a line when the provider names the account. With all three done, `IdentityChanged` is wired
and the account card stops saying "your progress is saved only on this phone" to somebody who
linked thirty seconds ago in a panel raised over it.

Not done, deliberate: **Play Games Services**
(better Android sign-in and the natural home for the "ranks" leaderboards, but
Android-only so it cannot be the identity), and a **visual level editor** (tooling — the
thing most likely to matter next for shipping cadence). Remote content delivery is built
but switched off; set `ContentConfig.RemoteBaseUrl` to enable.

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

In practice:

- **No placeholder architecture.** If a seam is worth having later, build it now and build
  it properly. If something genuinely is a placeholder, say so in the code and make sure
  replacing it touches one file.
- **Cost curves decide priority.** Prefer the change that is cheap today and expensive
  later. That drove stable ids, the save format and the asset pipeline — not taste.
- **Prove it, do not assert it.** Every claim about this codebase should be backed by a
  compile, a test, or a validator run. See *Verifying*.
- **Push back with reasons.** If the owner asks for something that would create regret
  later, say so plainly once, with the specific failure it causes, then do what they decide.
- **Finish the whole job.** Half a migration is worse than none. If something can only be
  done in the Editor, automate everything around it and hand over exact steps.

> **About this file.** Each invariant is a rule plus the specific failure that bought it,
> compressed to what stops somebody undoing it. Retired features keep only the transferable
> rule and their spent ids.

>
> **Two companions, both checked in.** `Assets/Game/CRAFT.md` holds the craft — the offline tools, how each
> mode looks and sounds, and the house rules for the UI. `Assets/Game/CONTENT.md` is the authoring and
> pipeline guide. Read `CRAFT.md` before touching a screen, a board's animation or an art tool; read
> `CONTENT.md` before touching content, assets or localisation.

## Invariants — do not break these

1. **A `LevelId` is permanent.** Save data, analytics and remote config key on it. Never rename or reuse a
   shipped id, and never key anything on a level's position.
2. **Never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`.** A frozen record of what the pre-1.0 build
   shipped; changing it moves real players' stars onto the wrong levels. `ContentValidation` fails the build
   if a level it names disappears.
3. **`Domain` must never reference `Presentation`.** The asmdefs enforce it. To call the UI from logic, raise
   an event — see `GameSettings.Changed`.
4. **Content is data, not code.** Levels live in `Assets/StreamingAssets/Content/`; adding a chapter must
   never require a code change.
4a. **The manifest owns membership and order; a chapter body owns content.** The boot path reads
   `manifest.json` and nothing else — `CatalogIndex` is built from it and is what progression, unlocking and
   the save key on — while bodies load on entering a chapter and are evicted on leaving. Never make the boot
   path read a body: that is a cost per chapter at every launch forever, and it is invisible in the Editor
   because only Android routes StreamingAssets through `UnityWebRequest`. `Content ▸ Sync Manifest` derives
   the level lists and the build gate proves they agree.
4b. **Every chapter file must be in the manifest, and only the Editor may check that.** Every reader walks
   the manifest, so an unlisted file is never opened — it validates, audits and builds green, and the drop
   ships without it. `ChapterFiles` is the one place allowed to list the folder; the boot path cannot read a
   directory on Android.
4c. **Anything that rewrites `manifest.json` must prove it lost nothing.** `Sync Manifest` rewrites the whole
   file, so any field it does not know about is deleted silently under a success message — `unlockCost` and
   the `events` array both reached the DTO and not the writer, and the first sync deleted a live event and
   thirty companion prices. `ManifestSync.SurvivesRoundTrip` reads its own output back through the reader
   **the game uses** and refuses the write on any difference. Never relax it into a warning.
5. **Omit `par` when authoring.** It is derived from the board; a typed one can drift.
5a. **A level's loc keys are derived from its id and cannot be overridden.** That is what lets anything
   holding a `LevelId` name a glade without reading a chapter body.
5b. **"Is this tile solved" is `Puzzle.Alike`, and it exists exactly once.** Written out five times as
   `Rotl(solved, k) == solved`, every copy was correct until a **crossing** appeared — it wears all four arms
   at every angle, so the mask comparison calls every rotation solved. Par then comes out short by one per
   twisted crossing, and par multiplies into both star lines *and* the move budget, so a board validates,
   derives plausible numbers and cannot be finished. `content.py` and `author.py` mirror it, because they run
   with no Unity anywhere.
5c. **A rooted tile is authored at `/0`, and that rule guards every other rule.** Every proof
   `LevelValidator` makes runs against the board with rotations zeroed, because that is the authored solution
   — so a tile the player can never turn, authored away from its solution, means the board proved is not the
   board that ships, and nothing else notices (arms mate, the solved probe lights, and `MinimumMoves` skips
   rooted tiles). What it breaks is `TurnsToSolution`, which counts them: one stuck off-solution adds turns
   that can never be paid, so a player who *had* solved it is told they are one turn away — the near-miss line
   being generous, the one thing it must never be. `CheckRootedTiles` asks `Puzzle.Alike`, not `rot == 0`.
5d. **A mechanic that rejects no arrangement is decoration, and that is countable.** `difficulty.py`
   enumerates every arrangement where every arm mates and none dangles and asks which win; when that count is
   **one**, the arms alone decide the glade — twenty-two of the first thirty were in that state, which is why
   brittle stone, taproots and fords read as absent: they were. The arms are rigid, so free decisions have to
   be **put** there, and a twisted crossing is the cheapest because only colour can settle it. Three rules
   follow: brittle stone belongs on a tile the player cannot simply try (so, a crossing); a taproot's members
   must all be tiles the arms cannot settle; and a ford must sit on a **cycle** of the live network with a
   **pocket carrying its own heart and critter** beyond it. `hazards` is the metric this replaced and is
   wrong — it counts rotations that would mate two networks but dangle an arm elsewhere.
5g. **A board is graded on its solution and met as it is dealt, and only the first was measured.** Reported
   as glades that "start half done"; it was thirty-four of forty, because `fit` picked by par alone and walked
   `bias` from -90 up taking the first, and a negative bias tells `Board.spin` to prefer leaving a tile on its
   solution. Such a board passes every gate, since "how much of this is already done" was a question nothing
   asked. `Board.astray` is the reading, ranked behind the par distance and printed as `dealt`. Consequence:
   **par ramps flatten** — a properly dealt board's par is roughly 1.2–1.35× its turnable tile count, so only
   taproots genuinely buy a dip.
5e. **A briar's thorns mate across the divide, so `Puzzle.Matters` has a second clause.** A briar draws four
   arms and conducts two, so the light walks `Puzzle.Live` while the drawing walks `Mask`. `TurnsToSolution`
   counts only tiles the solution's light reaches, which was safe until a briar's shut arms could mate
   straight across a divide: open one the solution leaves dark and the shadow lights with every counted tile
   still right, so the near-miss line would say a glade is finished that will not settle. One clause fixes it
   — a tile the player has lit counts, whatever the solution wanted. Before adding a tile whose drawn and
   conducting arms differ, ask what it can now join.
5f. **A wrong turn must be visible somewhere, and the duskcap was the one that never was.** `x` was a
   creature the light had to never reach; a woken one left every critter lit and the glade simply refusing to
   settle, which is indistinguishable from a bug (20g), so it is **removed** and a panel explaining it was not
   the fix. Every pool of dark is now a **pocket with a heart and a critter of its own**, so the ford still
   stands on a cycle and the warning is one critter going out somewhere the player is not looking. Retired:
   the token head **`x`** (refused, not ignored) and the lesson id **`duskcap`**; the level id
   `c01_duskcap_hollow` is *kept* with its name changed, because an id is permanent.
   <br>`LevelValidator.CheckDecidableTiles` is the gate: turn every briar and twisted crossing one step off
   its solution and refuse to be satisfied unless the glade stops finishing — the consequence, not a proxy. It
   replaced a check wrong in both directions whose false positive had three boards redesigned to satisfy it,
   and it is a **warning**, because a mode's first board may carry a briar as scenery. The rule exists three
   times (`LevelValidator`, `content.py`'s `decidable`, `author.py`'s `Board.decides`) and is pinned by
   `Tools/verify/board-vectors.json`; a straight crossing is correctly skipped, so a vector case built on one
   exercises nothing.
6. **All player-facing text is a loc key.** The build gate scans for key-shaped literals and fails on any
   missing. Never build keys by concatenation (see `WinOverlay.RankKeys`).
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or `Addressables` directly,
   and never hand-list paths — derive them from `AssetManifest`.
7a. **Asset registration is an importer hook, never a menu item.** `AddressableAutoRegister` addresses
   anything under `Art/`, `Audio/` or `Fonts/` as it imports, and `AddressableAddresses` is the single source
   of truth for path→address→group. A step somebody has to remember on shipping week will be forgotten — it
   already was, and the tool meant to fix it rotted into a silent no-op scanning a deleted folder — so the
   build gate runs `AddressableAudit`, because making an error unlikely is not proving it did not happen. A
   chapter names its own `backdrop`; shared art belongs in the global group.
7b. **Transient art belongs to a named `AssetLibrary` scope, never to the global set.**
   `EnsureScopeAsync`/`ReleaseScope` bound memory by what is on screen rather than by how much content exists.
   Two rules keep it honest: an address already global stays global, and one owned by another scope is never
   re-claimed, or closing one screen frees art another is drawing. Loading is asynchronous, so a screen must
   repaint when a scope arrives — an `Image` with no sprite is a white rectangle, not a blank.
7c. **A chapter's art is arithmetic on its ordinal, never a choice — and the choice is what made five
   modes read as five games.** Nine chapters picked their art nine ways: six source paintings, four map
   cuts and two borrowed-and-regraded ones, forty-one board backdrops of which a whole Lightfall
   chapter's ten levels shared **one**. Every individual decision was defensible, and nothing looks at
   the *set* — no gate opens a PNG, so a game whose second chapter is a different place in every mode
   validates, audits and ships. So the map now belongs to the chapter's **ordinal inside its own mode**
   (every mode's first chapter draws `map1`, every second `map2`) and the backdrop to the level's
   **place in its chapter** (forty skies, one cloud painting at forty colours, ten per ordinal);
   `Tools/chapters/mapart.py` is both functions and a generator writes the answers into the body, so
   the content still *says* what it draws. A mode is told apart on the map by its **perch** and by
   nothing else, which is what `ModeLook` already claimed in prose. Three consequences: **a chapter
   published next year costs no art at all**, which is the cost curve that decided this; **shared art
   files itself into the global group**, because `ChapterOwnership` gives an address two chapters want
   to nobody; and **`accent`/`slate` stop reaching the backdrop**, going back to being only the board's
   own light and its plate. Note what did *not* move: a chapter still names its own `backdrop` and
   validation still fails without one (7a), because a chapter drawing art nobody chose is a chapter
   nobody decided the look of.
8. **The map shows one chapter at a time.** That bounds node count and texture memory by chapter size instead
   of catalog size. Do not "improve" it into one long map.
8b. **Which chapter that is, is remembered per mode and only ever a hint.** Every way back to the map
    except the chapter arrows arrives naming no chapter — the back key, a forfeit, the victory panel, the
    home screen — so the fallback is what a player meets after *every* level, and it was
    `LevelUnlock.CurrentChapter`: wherever they are **up to**, which on an account that has unlocked
    everything is the newest chapter. Replaying an early chapter therefore meant arrowing back to it every
    time. `ChapterChoice` is `ModeChoice` one level finer and stored the same way — **device-local, never in
    the save**, because it moves both ways and could not be joined (11b) — and it is **per mode**, or crossing
    the switcher and coming back lands each side on the other's chapter. It is a *hint*: `Read` answers null
    the moment the remembered id is not a chapter of that mode in this catalog (a rollback, a disabled
    chapter, an undownloaded drop, a chapter re-filed into another mode), and the caller falls back. Nothing
    keys on it, so invariant 1 does not reach it.

8a. **Map geometry lives in `ChapterMap` (Domain), not beside the screen.** `mapX`/`mapY` are fractions of a
   chapter's own map, so collisions and backwards trails are facts about a chapter that the build gate can
   check — and a validator cannot reach into Presentation. `MapLayout` reads them from there rather than
   holding a second copy. **And the footprint it checks has to be what collides, not the disc**: the
   standing mark above a cleared glade reaches 302 units up and every perch hangs a plate 227 down, so a
   check guaranteeing the discs' 220 passed every mode's first chapter with the next-chapter marker
   sitting on the tenth glade's record. `ChapterMap.Overshadows` is the rectangle test, `ChapterMapTests`
   holds its numbers to what `LevelsScreen` draws, and the ten node positions are one table in
   `Tools/chapters/mapart.py` — tenth glade on the left, marker on the right, in every mode.
9. **XP and earned credits are derived, never accumulated.** A pure function of the star ledger via
   `ProgressionLedger`. An accumulator cannot be merged across devices (nothing distinguishes "cleared twice"
   from "counted twice"), cannot be retuned for existing players, and cannot be recovered when lost. The only
   stored progression numbers are the high-water floors in `ProgressionStore`, and they are floors.
9b. **`progression.json` versions independently of the catalog** (`ProgressionSchema`, not `ContentSchema`),
   so a catalog format bump never invalidates the reward table for clients that have not updated — they would
   fall back to the built-in curve and lose live tuning for an unrelated change.
9a. **The reward rule exists twice and must stay identical.** `ProgressionLedger.cs` and
   `functions/src/progression.ts`, both running `firebase/shared/reward-vectors.json` as a test, so drift
   fails a build instead of desynchronising the economy. Change one, change the other, add a vector, re-seed
   after every content drop.
9c. **So does the chest generator.** `DailyChestTable.cs` and `functions/src/daily.ts`, pinned by
   `dailyChestCases`. Contents are a pure function of (account id, day, chest index) — FNV-1a then xorshift32,
   all 32-bit so JavaScript reproduces it exactly, which is what lets the server work out what a chest was
   worth instead of believing the client and stops a player rerolling by force-quitting the animation. The
   hash constants, shift amounts, stream numbers and modulo are all contract.
10. **The client never raises `grantedBaseline`.** Currency given rather than earned is server-owned,
    enforced by Firestore rules. Receipt validation must be idempotent on the store transaction id.
10a. **An award reaches the player as a claim, not as a balance.** A reward handed out offline goes into
    `CurrencyLedger.TryAward` with an id **derived from what earned it**, never generated: two devices
    claiming the same chest produce identical entries that union to one, a resubmission after a dropped reply
    confirms instead of paying, and the server keys its own record on the same string. The server recomputes
    the amount; the client's number is a prediction. Never reach for `GrantLocally` — it is for the account
    seed and nothing else.
10c. **A chest cannot be opened before the account id exists.** The roll is seeded from the uid so the server
    can recompute it, and the client cannot know that seed before speaking to the server — so the chest waits
    rather than showing a reward the server would overrule. Do not roll with the device id.
10d. **A rewarded ad is granted by the network's callback, never claimed by the client.** Nothing about "this
    player watched a video" is derivable, so the authority moves outside: LevelPlay's signed callback hits
    `adReward`, granting in a transaction keyed on `ad:{eventId}`. The obvious alternative (client nonce →
    custom parameter → derived award id) does not survive, because LevelPlay 9 removed
    `setRewardedVideoServerParams` and `LevelPlaySegment` has no promise of reaching the callback — so it
    fails *silently*: ads that play, players told they earned coins, a server that never pays. The client
    credits **hearts only**; `claimAwards` refuses `ad:` claims, because a claim that can never confirm is
    resubmitted forever.
10b. **Daily chests are earned, never bought.** That keeps them outside loot-box rules rather than merely
    compliant, and is why the odds can be printed. No price, and no second weighted pick — one pick is what
    makes the published odds a list that sums to a hundred.
11. **Cloud conflicts merge; they never prompt.** `SaveMerge.Join` is idempotent and order-independent, so
    both devices' work survives. A "keep local or cloud?" dialog is data loss wearing a consent costume.
11b. **Anything a merge touches must be monotonic, or it is not mergeable.** A stored *count* cannot be
    joined: two devices showing 3 and 0 are equally consistent with "one spent three" and "one has not heard
    about a refill". Hearts shipped taking the smaller and destroyed a refill on every sync, because a sync is
    pull → join → push and the stale side won before the local value had been uploaded. The fix is the
    currency ledger's shape: store counters of things that happened (`heartsProduced`, `heartsSpent`) and
    derive the count, so the merge is `max`. Before adding a field the merge reads, check it only ever rises —
    and that its "absent" state is a value a real one cannot hold, because `JsonUtility` writes a zero into
    every field an older file never had.
11c. **A value merged by recency must carry its own date, and its default must never be stored.** The
    keeper's name and worn companion are the only things not joined on value, being instructions rather than
    achievements — so they are the only place the merge can lose something, and for a year it lost the name on
    every device. Two mistakes, both general: the recency came from the *file's* `updatedUnix`, which
    `SaveService.Snapshot` stamps with **now**, so the local side was newer in every comparison; and an
    unnamed keeper *stored* `Wallet.DefaultName`, so a device with no opinion was indistinguishable from one
    that had chosen. Fixed by `displayNameSetUnix` / `avatarSetUnix` (v15) and a default that is *shown* and
    never written; `SaveMerge.Chosen` is still a join.
11a. **The ledger is a map keyed by level id, never an array.** A duplicated record becomes unrepresentable
    rather than something the server has to filter, and a sync can write `levels.<id>` alone.
    `SaveDelta.Between` decides what to send; an unchanged save sends nothing.
12a. **A field is not added to the save until it is on the wire, and the wire is four places.** `SaveFileDto`,
    `SaveDelta`, `FirestoreSaveMapper` — *both* directions — and the `hasOnly` list in `firestore.rules`.
    `groveLandOwned` shipped in v17 having reached the first two only, so land bought with credits never left
    the phone that bought it, and nothing showed it: a device only discovers what it failed to upload when
    something replaces its local save. The rules entry has teeth the other way too — `hasOnly` is an
    allow-list over the whole document, so a client writing an unlisted key does not lose that key, it **loses
    every save write**, and the rules must be deployed before the client.
    `EveryFieldOfTheSaveIsCarriedByTheRoundTripFixture` checks the *fixture* rather than the mapper, because
    the round trip is only as complete as what is fed into it.
12. **Adding a field to `SaveFileDto` interacts with the checksum.** `SaveChecksum` hashes the serialised
    object, so a file written by an older schema can never match a newer build's hash and `Verify` skips
    across versions. Bump `SaveSchema.Version` when you add a section, or every save on every device fails at
    once.
13. **A reward is derivable, adjudicated, third-party, or not currency.** Currency the client hands out must
    reach the server as something it can *recompute* (a chest from account/day/index, a golden glade from
    account/level, an event track from clears dated inside a window), as something a *third party* tells it
    about (the rewarded-ad callback), or as something it can *bound* so tightly that forging it buys nothing
    (the streak). The streak is the interesting one: the server need not know it, only that a claim is no
    *better* than an honest one — a night is claimed as `streak:{day}:{night}:{ccy}`, so `grantLog` bounds it
    to one payout per calendar day and `advances` bounds the night to climb no faster than the calendar. What
    is left uncapped is a new account's first claim, which is why `StreakRules`' ceilings are an economy
    decision.
13a. **A claim must never be refused for a reason that will still be true tomorrow.** The client only warns
    about `rejected` ids and keeps resubmitting, so a permanent refusal is a loop for the life of the account.
    The server refuses a streak night on `advances` (permanent, correct) but never on the save's own dates —
    a player who lets the flame lapse pushes a save whose `startDay` has moved past a night they earned, so
    `saveSupports` logs the disagreement and pays anyway. A missing config block leaves a claim *unconfirmed*
    rather than rejected.
14. **Derived rewards are free of save state, and that is why they are preferred.** The golden glade bonus
    adds nothing to `SaveFileDto` — no counter, no claim, no merge rule. Save state is where features here go
    wrong (11b).
14a. **Being derived decides where a reward comes *from*, never when it arrives.** The event track's one
    field (v11, `EventCollection`) is not the reward — it is a floor saying how much of a track the player has
    asked for, while the arithmetic stays derived and server-recomputed. A reward landing in the balance while
    a defeat screen is up is an accounting entry nobody experiences, so if keeping it derived means it can
    only arrive silently, add the floor — one monotonic integer per key, merged by `max`. What must not come
    back is a *stored amount*.
15. **An entitlement is stored; everything that pays is derived.** Nothing observable implies "this player
    paid 8,000 credits for Coral", and mining it out of a debit's free-text `reason` would make a support
    field load-bearing — so `companionsOwned` (v12) is a **set of permanent ids joined by union**, because
    buying is irreversible. A count would be hearts' old mistake and a per-companion flag could not tell "not
    bought" from "written before this companion existed". What makes it safe where a stored *amount* would not
    be: an entitlement is not money, so a forged entry buys a portrait and no advantage, and the money half is
    defended by `submitSpends` refusing a debit the derived balance cannot cover.
15a. **The unlock rule is "keeper level **and** purchase", and it lives only in `CompanionLedger`.**
    `AvatarCatalog.ReachedBy` answers the level half and is named for its narrowness on purpose — it used to
    be `IsUnlocked`, and a call site checking half a rule under a name promising all of it is how a companion
    somebody paid for stays behind a padlock. It was **or** for a year and both clauses cannot survive: if
    reaching the gate handed the companion over, the price would be unreachable code — so the gate is
    *permission to pay*, and an unpriced companion is still granted at its gate, which keeps the starter
    working. The gate is tested **before** the price, so a player both too junior and too poor is told about
    the wall credits cannot climb; and `IsHeld` must never re-check the gate on a companion already bought, or
    a retune confiscates a paid-for friend.
16. **A grove is built, and only three facts about it are stored.** Everything else is derived, and what is
    left splits by *shape*, not by feature: a purchase is an **entitlement**, so `homesteadOwned` and
    `groveLandOwned` are union-joined id sets, while an arrangement is an **instruction**, so
    `homesteadPlaced` is merged by recency with a stamp per slot (11c) — the only part that can lose
    something, which is why an untouched slot writes no row and a slot the player *emptied* keeps one.
    Deliberately absent: any count of tiles. A slot id is written into the save, so invariant 1 applies to it.
16b. **The grove is a tile floor, and a tile is a slot.** Ten islands with hand-authored slots made the
    player's only decision which of eleven pre-placed dots got which sticker; a field of identical tiles moves
    the composition to them, and the slot-kind rule went with the islands, surviving as a **shop shelf**. It
    was cheap because `HomesteadLayout` did not move. Three rules keep the ids safe: they are **absolute floor
    coordinates** (`t_006_006`), so redrawing which region a tile is *sold* in never changes what is *standing*
    on it; they are **zero-padded**, because `SaveDelta` walks them in order; and the floor may **only ever
    grow right and down**, because a column inserted at the left renumbers every tile in the world.
    `GroveFloor` owns the geometry, in Domain, so the build gate can prove regions do not overlap.
16e. **Land is the one thing here that stopped being derived, and it cost a schema version.** An island was
    held when its chapter was finished, so it left nothing on disk; land bought with credits cannot be, so
    `groveLandOwned` is stored (v17) as a union-joined set of **regions** rather than tiles — both are legal
    and only one stays small, since a filled floor is a couple of hundred tiles merged and checksummed on
    every sync. Starter land has **no price and is never written down**, so "absent" and "bought nothing" stay
    one fact; and the hall must stand on starter land, because a home a new player can see and not reach is
    the emptiest possible first impression.
16f. **The starter companion is shown, never stored.** Writing that placement at first launch is what 11c
    forbids: a fresh install would stamp it with *now*, outrank a device where the player had moved them, and
    put them back. The tile draws the starter while it has no row of its own, and clearing it is a real
    instruction that does get one. Which starter is derived (`AvatarCatalog.Starter`), as is the default name.
16g. **A grove's score is what it is worth, and worth is what is *held*.** The star readout is the credits'
    worth of catalog held and stores **nothing**, so it is derived, cloud-safe and monotonic for free.
    Counting **placements** would be won by standing one expensive piece on two hundred tiles — rewarding
    exactly the monotony the floor exists to remove — and **storing** it would be the count 11b forbids,
    forgeable in the one direction that matters once a leaderboard reads it. A free piece adds nothing because
    it is worth nothing; an earned companion adds its full price, because the reading is market value, not
    spend, which is the only version with no special case. The ladder is **content** (`score.stars`).
16h. **Priced decor is bought by the copy, and the count is only representable because it counts purchases —
    save v20.** Copies *remaining* cannot be merged; copies **ever bought** only rise, so the join is a per-id
    `max` and what is left to place is derived: `bought − placed`. Four consequences. **The subtraction is
    clamped at zero and nothing is ever taken down** — two devices can each place the last copy on a different
    tile, so the grove briefly holds one more fence than it bought, and answering "none left" costs nothing
    where removing a placement would be the loss invariant 11 refuses. **Only priced decor is stocked.** **A
    bundle is content** and a copy is worth `cost / bundle`, so `ContentValidation` *errors* on a price its
    bundle does not divide, because the shortfall is invisible on a device and lands on the one number that
    reaches a public leaderboard. And **the v19 field is kept as a derived mirror**, read only when the stock
    section is empty, so a rolled-back client and a not-yet-redeployed `groveWorth` both keep working. The
    migration grants `max(placed, 1)`, because neither `HomesteadLedger.LoadFrom` nor `SaveMerge` has the
    catalog loaded and a fixed grant would hand ten copies of a singly-sold piece to anybody who owned one.
16i. **A piece occupies a footprint, the ground is a layer under everything, and a tap tests paint — three
    reports from one session, each a piece whose picture and whose ground disagreed.** *Buried:* a cell held
    its tile and its piece sorted as one, so the tile in front painted its face and its skirt over the base
    of whatever stood behind — every piece drawn standing *on* the ground lost its feet. The ground plane is
    flat and nothing on it is behind it, so `GroveFieldView` draws all ground first and every piece over all
    of it; a cell owns a node in each layer. *Covers more than one tile:* a cottage was one slot painting
    over three, which could still be built on. `HomesteadPiece.Footprint` (`cols`×`rows`, content) is what a
    piece **occupies**; the save still stores one anchor row, the rest is derived through the catalog
    (`GroveOccupancy`), so it cost no schema version and a merge landing two footprints on one tile keeps
    both (11) — the index answers a covered tile with the nearer stand. A mirrored footprint swaps its axes,
    because screen x is `(col − row)`. The hall's footprint is the **floor's** (`hallCols`/`hallRows`) and
    every dwelling must equal it, or buying a manor would evict what stood beside the cabin. Every placement
    goes through one of `TryPlace`, `PlanMove`/`Move` and `Flip`, which fit the footprint around the touched
    tile and answer `NoRoom` out loud; the drag lights the whole footprint green or red from the same plan
    the drop executes. *Hard to tap:* a tap tested the sprite's **box**, and an oak's box is nine tiles of
    air around a forty-pixel trunk. `hit` is a mask of one cell per sixteen art pixels over the picture,
    generated from the PNG by `Tools/grove_art_facts.py` with `w`/`h` — the grid is the size's, so a mask
    is refused unless it is exactly the length its picture implies — and the tap's forgiveness is a
    **distance** on the floor (`GrovePick.TouchSlop`), never a count of cells, so a torch and a boulder
    are as forgiving as each other. Residents carry the same facts on their manifest entry
    (`groveW`/`groveH`/`groveHit`, written back by `ManifestSync` and proved by its round trip), because
    a resident is a companion and the grove draws its art too. `content.py` and `ContentValidation`
    refuse a piece or companion whose facts differ from its file, because a number describing a picture
    is only true until the picture is re-cut (5's `TileFaceRatio`). Two things bought by the same session: **the size a piece draws
    at is authored, not measured off the loaded sprite**, so a tile laid out before its art arrived is not
    laid out around a placeholder; and **"objects appear smaller when working rapidly" was a flipbook
    leak** — two repaints in one frame stacked two `Flipbook`s, `GetComponent` stopped the first, and the
    survivor went on painting a well's frames into a box re-sized for brambles. `Flipbook.Detach` takes
    every one, `FlipbookTests` pins it. The occupancy index (`HomesteadLayout.Occupancy`) is memoised on a
    version that every row write **and the ledger's own event** bump, so nothing rebuilds it on a bind and
    nothing asks the ledger whether it is stale. The zoom band moved with it (`DefaultZoom` .85, .55–1.2), because a
    tile at .7 was a quarter of an inch tall; `render_grove.py` mirrors all of it and stays pinned.
16j. **The floor is sold in two currencies up a ladder, and both halves of that broke a rule that
    had been "the whole rule" while there was only one currency.** The five biggest stretches are
    priced in **gems** (600 → 2,000) and the three smallest in credits, and they are offered **one at
    a time in an authored order** (`east_meadow` → `still_shore`), because ground is the dearest and
    least legible thing in the shop — a wall of nine prices asks a new keeper to compare rectangles
    they cannot picture, where one offer at a time is a next step.
    <br>**`IsStarter` was `Cost <= 0`, and that is exactly right until a region has a price that is
    not a cost.** It gates `IsOwned`, the shelf, the ladder, the hall's ground, the published card
    and what is written into the save — so under the narrow reading every gem stretch reads as free
    and **half the world is handed over at launch**, with every file still reading as authored.
    `content.py` had the same line and needed the same fix. **Before pricing anything in a second
    currency, grep for the predicate that means "free" and read every caller.**
    <br>**The ladder is authored (`order`) and never derived from price**, for a reason and a
    half. It cannot be derived — 600 gems and 5,000 credits do not compare — and the near-miss is
    worse than the impossibility: a gem region's `Cost` is **nought**, so `NextForSale`'s old
    "cheapest unowned" would have sold the five dearest stretches in the game first and for
    nothing. Derived order also reorders itself on a retune, under players part-way up it. An
    unauthored rung sorts **last**, so a content mistake strands a stretch — which somebody
    notices — rather than jumping it to the front, which nobody would; `ContentValidation`,
    `content.py` and `HomesteadTests` each prove the rungs are 1..N with no gaps and no ties.
    <br>**Gem-priced land is worth nothing to a grove's score, and that costs no code at all.**
    The score is the *credits'* worth of what is held (16g) against a server ceiling denominated
    in credits (19a), so a gem cannot be priced into it in either direction — counting it would
    overstate a forgery or understate an honest buyer, and `grove.ts` calls the second one a bug.
    Both worth sums already skip a region costing nothing, so it falls out. A complete grove is
    now **436,270 credits and 6,200 gems**, and only the first number is a score.
    <br>**The one thing that did need saying is the seeder.** `config/grove.regions` is read
    twice — `groveWorth` sums it, and `buildCard` filters the published `land[]` through it as a
    catalog check — so a gem region left *out* scores correctly and quietly deletes eight of the
    floor's fourteen columns from every **visitor's** view, with everything standing on them
    floating over nothing. It is published **present at zero**, which says both things; absent
    says one of them wrong. Starter land stays absent and is not the same case, because it never
    reaches `groveLandOwned` at all (16e).
    <br>Two smaller rules. A region carries **one price or the other**; `HomesteadMapper` salvages
    a double-priced one by dropping the credit half so a remote push cannot brick a live client,
    which means the *build gate* for it is the mapper's own message (every mapper problem is an
    error) plus `content.py` and `seed-config.mjs`, which see the file first — a check written
    after the mapper would be dead code wearing a gate's clothes. And **the ladder is asked before
    the price** (15a's ordering): a keeper both a rung down and short of gems is told about the
    wall money cannot climb.
16a. **A resident is a companion, and the roster is written down once.** The grove used to author five of its
    own — a second roster with its own unlock rule, its own prices and two screens that could disagree about
    what somebody owned. `GroveResidents` projects the roster in, so a drop that adds a companion adds a
    resident with one price, one gate and one purchased set (`companionsOwned`, never mirrored into
    `homesteadOwned`, because two records of one purchase is two things a merge can disagree about). The
    endowment argument survives where it belongs: **wearing and housing are separate**. A resident's piece id
    is the companion's id **prefixed** (`friend_coral`), because the two id spaces were minted independently
    and already collided; and the five retired ids are **rewritten on every load, for ever**, because a
    retired id resolves to nothing and leaves a hole that still counts as occupied. Unchanged: **nothing in
    the grove touches a board**, or every glade would be a different difficulty per player.
16c. **A shop shelf is one idea used three times, and browsing never loads the real art.** `GroveShelf` is
    the shop's tab, the browse atlas and the asset scope — three mechanisms that must agree about how the
    catalog divides, so the division is expressed once. A grid cell draws at ~170 points against art cut at
    512, so browsing reads **generated thumbnails out of one atlas per shelf**: one draw call, memory bounded
    by the largest shelf. It packs *copies*, which is load-bearing — a sprite may belong to exactly one atlas
    and then stops having a texture of its own, so packing the shipped pieces would mean the grove screen
    could not draw one island without loading its whole shelf. `Validate Art` proves every atlas covers its
    shelf, because a stale atlas is invisible everywhere else.
16d. **Anything unbounded keeps only what you can see.** `GridView` builds a cell once and rebinds it as it
    scrolls. A correctness rule as much as a performance one: every grid used to destroy and rebuild itself on
    any event with cells entering from scale zero, so a screen that repainted twice played that entrance
    twice. `Show` is a new list and animates; `Refresh` is the same list redrawn and does not — anything
    raised by an event is a `Refresh`.
17. **A save may only ever be pushed to the account it says it belongs to.** `AccountGate`, five lines, and
    the only rule here whose failure has no undo: a sync is pull → join → push and the join is monotonic, so
    aimed at the wrong account it takes the better half of two strangers' groves and writes it over one of
    them. The window is ordinary — switching accounts moves the session before the file on disk, and the OAuth
    consent screen backgrounds the app mid-way — and it is an economy rule too, because the same ledger under
    a fresh uid is a fresh, differently-rolled, fully funded wallet. Two corollaries, each costing a grove. **A
    save that names an account may never have a new one minted for it** (`ResumeAsync` exists next to
    `SignInAsync` for exactly this), because an anonymous account created for a save that already has an owner
    can never match it, so the device is refused for ever while the player believes they are backed up. And
    **the refusal has to be visible**, because such a device *is* signed in and anything reading `IsLinked`
    alone tells somebody their progress is safe while nothing is being written.
17a. **A switch is finished on the device before the network is asked for anything.** The original order was
    secure → authenticate → **fetch** → replace, and reading the incoming grove decided whether the switch
    happened at all, in the frame after an OAuth browser handed control back: one unlucky read left the device
    authenticated as one player holding another's save, ending in a prompt offering to discard twenty-six
    glades belonging to the same person. `SaveService.SwitchTo` makes the swap **local** — outgoing grove
    copied into `IAccountArchive`, incoming one restored from there — so the switch cannot stop halfway and
    the server is folded in afterwards by an ordinary sync. Three rules: the archive is **a cache and never a
    backup** (evicted at six slots, losing a copy and not a grove, because the securing push still runs first
    and is the only step allowed to refuse a switch); a slot **names its owner inside the file**, so one that
    does not name the account asked for is discarded; and `AccountGate`'s refusal now has a repair, since a
    session ahead of the save is completed *forward*. The one path that must still refuse is `redeemPurchase`
    (`AuthoriseAsync(repair: false)`), because a receipt redeemed against whichever account is authorised
    would move a purchase between two of them.
18. **A real-money product grants currency, and nothing else.** Invariant 13's *adjudicated* clause taken to
    its conclusion, and what makes the shop cost the save file zero fields. A product granting both currency
    and hearts would need a record of *"did I already apply this transaction's hearts"*, in the save, merged
    across devices, whose failure mode is somebody paying and receiving nothing — so hearts and boosts are
    bought with **gems**, and a gem debit is an ordinary `CurrencyLedger.TrySpend`. The mirror rule: a
    gem-priced good may never pay currency, and `StoreCatalog` refuses anything but `hearts` and `heart_boost`
    by name. Widened by 18d.
18a. **A transaction is confirmed only after the grant lands, and never before.** A purchase arrives
    *unfinished*; our server asks Apple or Google whether it happened, records it against
    `receipts/{store}__{txn}` — **globally**, because replaying one real receipt across thousands of accounts
    is the industrialised attack and a per-player key would validate every one — and grants. Only then is it
    confirmed, so everything that can go wrong is "still unfinished", and both stores re-deliver on every
    launch for ever: a crash, a tunnel, a flat battery and a server outage are one bug with one fix. That is
    why **no per-purchase state exists in the save**. A refused receipt is **never** confirmed, because "the
    server refused" covers a product missing from `config/products` as well as a bad receipt, and confirming
    the first charges a player for a configuration mistake and destroys the evidence.
    <br>**One exception, and it is one because it can never stop being true**: a receipt already granted to a
    **different account**, whose document is never deleted (27) and whose `uid` is never rewritten.
    `redeemPurchase` says `already-exists` and the queue finishes the transaction and grants nothing — and the
    account it protects is not the one holding the phone, because left unfinished Google auto-refunds and
    `sweepVoidedPurchases` reverses the grant against `receipt.uid`.
18b. **The shop is one authored list, and the server derives its half from it.** The `store` block of
    `progression.json` is what the game draws *and* what `seed-config.mjs` turns into `config/products`,
    because a card promising 750 gems against a server granting 700 is two files edited on different days and
    the difference is charged to a real card. There is **no price field and there must never be one** — a
    price lives in the two consoles, differs per storefront and comes back formatted from the SDK, so
    `referenceUsdCents` is never shown and exists only so the build gate can prove the ladder improves with
    size. And a **product id is permanent**: neither store lets one be reused after deletion, so retune by
    adding a product, never by repointing one.
18c. **A refund is money leaving, so something has to watch for it.** Buy, spend, refund, repeat needs no
    exploit and no tooling, which is why it is the commonest way a mobile economy leaks. Apple pushes
    (`appleNotification`), Google is polled (`sweepVoidedPurchases`, hourly). The Apple handler deliberately
    does **not** verify the notification's JWS chain, and that is stronger rather than weaker: it scrapes
    transaction ids out of an untrusted body, keeps only ones this server granted, and asks the App Store
    Server API about each over the authenticated channel — so a forged POST can at most make us look something
    up. That holds **only** because every id is re-checked. Balances clamp at zero.
18d. **A real-money product grants currency, or an idempotent permanent entitlement — never a stored amount,
    and never both.** Hearts and boosts are **amounts**; a **capacity** is not, because it arrives as the
    union of one permanent product id, so applying it twice is applying it once. The entitlement therefore
    lives **entirely on the client** (`heartContainersOwned`, v21) and still survives a reinstall, because
    both stores re-deliver a non-consumable for ever and `HeartContainerLedger.Grant` runs on *every*
    successful redemption rather than only the first; the **cap is derived** and is the largest container held
    rather than the sum, so buying out of order, buying twice, or restoring onto a better device all resolve
    to one number.
    <br>**The refund is the half a client-held entitlement cannot see.** `revokeReceipt` writes the container
    id onto `players/{uid}/private/wallet` and every wallet reply carries it back as `containersRevoked` —
    which is **not** the list of ids the server thinks the account owns, because read as a whitelist it would
    confiscate a purchase on any reply that was short, from a cold account, or from a deployment predating the
    field, where an explicit revocation can only come from a refund that really happened. Both sets only ever
    grow and are joined by union. Also: a container **at or below the free refill cap is an error, not a
    warning**, because raising `hearts.refillCap` past a shipped vessel would take real money and change
    nothing the player can see.
18e. **A shelf's picture ladder is exactly as long as the shelf, and it was two longer for months.**
    `ShopLadder.Rung` maps a product's tier onto a fixed-length ladder, so a shelf of four and a shelf
    of six both read as full — which is right, and hides the fact that the *length* is a decision. The
    coin ladder had six rungs because the sheet it was cut from happened to carry six coin tiles, and
    against four products the arithmetic picked rungs 0, 2, 3 and 5: **two of the four painted coin
    quantities were shipped, addressed, always-resident and drawn by nothing.** The mirror fault is a
    ladder shorter than its shelf, which draws two adjacent cards identically — the "one product listed
    six times" reading these pictures exist to fix. Neither is visible in a compile, a validator, an
    audit or a screenshot, because both ship a shelf that is individually correct on every card. The
    one shelf allowed to be longer than what is drawn is **bundles**, and only because a one-time
    product takes the top rung whatever it costs (`Rung`), so with three products the bottom rung is
    unreachable until a fourth is authored.
    <br>**And a ladder has to grow.** The pictures now come from a pack that paints the amount, so
    `make_shop_art.py` scales each rung by its own source against the largest in its ladder and
    **refuses a rung no bigger than the one below it** by name. Squaring each picture to its own
    silhouette — which the sheet-cutting tool it replaced had to do — would draw a single coin as large
    as a vault, which is the ladder thrown away with every gate still green.
19. **Anything a stranger can see is a separate, server-written document.** The save is `isOwner(uid)` for
    ever; a leaderboard row gets `groves/{uid}`, built by `publishGrove` with its own credentials and never
    writable by a client. Widening the save's read rule would publish everything else with it and freeze the
    save's *shape* into a public API that could never change.
19a. **A number that goes public stops being derived-and-trusted and becomes adjudicated.** 16g built the
    grove's worth as a pure function of three client-written id sets: safe while private, forgeable once a
    leaderboard reads it. The score is now invariant 13's fourth clause, split in two — the **earned** half is
    derived from records the server already validates for currency, and the **bought** half is clamped to
    `earnedCredits + grantedBaseline`. The gate still works and works *before* the clamp: a save naming a
    companion its own keeper level has not reached cannot be honest, so that entry is dropped outright rather
    than cut down, which is strictly tighter.
19b. **The public name is a second rule on top of the stored one, and the server's answer governs.**
    `RenameOverlay.Clean` asks what a text field owes a database; `GroveNames` asks what a string owes the row
    beneath it. The bidirectional controls are why that is not a length check — U+202E re-orders the text that
    *follows* it, so one name misdraws the whole list — and whitespace is tested before the forbidden set,
    because a tab is a control character *and* a word break and deleting it joins two words. The word list
    lives only on the server, a refused name is never rejected (the player keeps it and is published under a
    handle derived from their uid), and the opt-out raises a **withdrawal**, because a card still standing
    after somebody opted out is a data-protection failure rather than a stale cache.
19c. **A standing is read off a published distribution; nothing maintains a global ordering.** Nine score
    deciles and a hundred-row board, rebuilt daily, read as one document at O(1) at any player count — against
    a query costing a hundred document reads per screen open on a collection that grows for the life of the
    game. A league is not a second ladder either: it *is* `GroveScoreTable.StarsFor`.
19d. **A name is unique because a document id is unique, never because a query said so.** Reserved by
    creating `names/{fold}`, so uniqueness is enforced by the database's own primary key at any concurrency,
    where `where("name","==",x)` returns empty for two players a second apart and lets both write. It is also
    the shape that does not grow: one document read by id at ten players and at ten million. The cost split is
    the whole design — the **hint** while somebody types is a direct read (`get` granted, `list` refused, so a
    name can be asked about and the collection cannot be walked), and only the **claim** is a function,
    because only the claim is adjudicated. `NameCheckScheduler` keeps the hint from being a read per
    keystroke, roughly a tenfold difference in the bill, so it is tested rather than assumed. Uniqueness can
    never live in the save, because `wallet.displayName` is merged by recency and no rule over two devices can
    decide a global fact.
19e. **The two folds are one rule, and the runtimes do not agree about Unicode.** A fold makes `Fern`,
    `fern`, `FERN`, `F e r n` and the fullwidth spelling one name, so it exists in `GroveNames.Key` and
    `functions/src/names.ts` and the shared vectors run both. Unity's Mono and Node's ICU **disagree** and
    only the vectors can see it: `İzmir` folded two ways (U+0130's lowercase is longer than itself), a Greek
    name ending in Σ diverged on Final_Sigma, the Latin ligature block is not decomposed by Mono, and Cherokee
    and Georgian Mtavruli got lowercase after Mono's tables froze. `Agree` closes those by hand and **stops
    there deliberately** — 27 of the BMP's 256 blocks still disagree somewhere, and closing them would mean
    shipping normalisation tables in a client to make a *hint* exact. Safe because a divergence costs a wrong
    hint, corrected by the claim a moment later, and can never produce a duplicate: a reservation is decided
    by the server's fold and only ever by the server's fold. The fold may only ever be **loosened**.
19f. **A published name comes from the reservation, never from the save.** `boardName` reads
    `players/{uid}/private/wallet`, which no client may write, so a modified save changes its owner's screens
    and leaves the board untouched. The word filter runs **again** at publish time, so adding a word takes a
    name off every board on the next rebuild instead of needing a sweep; and `publishGrove` **claims**
    whatever the save asks for when it differs from what is held, which makes a rename made offline land with
    no client-side retry state.
19g. **A word list is the cheapest layer of name moderation and the least important; the fold stops bypasses
    and reporting catches the rest.** The filter that shipped was thirteen English words and
    `flat.includes(word)` over a string with everything outside `a-z0-9` **deleted** rather than folded, and
    every failure was silent — leetspeak walked past (`5hit`, `f4ggot`), a single Cyrillic character *removed
    itself* and left a word matching nothing (`fuсk` → `fuk`), and any name in a non-Latin script squashed to
    the empty string and was never filtered at all, which in a game shipping globally is most of the world. It
    also refused **Grapevine**. So the work is reducing the name *and every list entry* to one canonical form
    before comparing — `profanity.ts`, four forms, because one cannot serve both jobs: folding Cyrillic `а`
    onto Latin `a` is right for catching an English slur in lookalikes and wrong for comparing two Russian
    words. Matching splits by **how**, never by meaning: `anywhere` and `reserved` are substring classes,
    short, curated and guarded by an allowlist *cut out of the haystack* before the test (the Scunthorpe
    repair); `exact` is the 2,600-entry vendored multilingual set matched whole-name and per-word, which
    cannot have a false positive by construction. `nazi`, `porn`, `anal`, `ass`, `cock` and `dick` are
    deliberately **not** substring entries — Nazir, Pornchai, analysis, bass, peacock and Dickens are each
    somebody's name.
19h. **The list is a document and the takedown is a flag, because both have to move without a deploy.**
    `config/names` overrides the compiled-in list, and the compiled one is the floor rather than a nicety: a
    filter that fails *open* looks exactly like a filter with nothing to catch, so `blocklist.ts` refuses a
    published list materially smaller than the shipped one and keeps the last good one when a read throws. The
    takedown is `deniedUnix` on the account's name holding, on the *wallet* rather than on `names/{key}`
    because `publishGrove` already opens the wallet — a flag on the reservation would be a document read per
    publish per player, for ever, to carry one bit that is almost always zero. Safe because a denied name's
    **reservation is never released**. `claimName` must refuse a re-claim of a denied name: that is the branch
    every publish takes once a name has settled, so without it a report takes a card down and the next sync
    puts it back.
19i. **A report is keyed on the pair of accounts, and the client is told almost nothing.**
    `nameReports/{target}/reporters/{reporter}` — the id *is* the idempotency, so tapping twice is one report
    on any device after any reinstall, and it is why the threshold counts **distinct reporters** rather than
    taps. Three collapses matter: the server's seven outcomes reach the client as **three**, because a caller
    who can tell "counted" from "already hidden" can binary-search the threshold and one who can tell
    "counted" from "nothing to report" learns which accounts are worth brigading; `nameReports` is server-only
    in **both** directions; and the auto-hide runs **without a human** because it is reversible and cheap — a
    brigade of three costs a real player a plainer row — where waiting on a queue means the offensive name
    stands as long as the queue. A restore stamps `reviewedAt` and never deletes the reports, or the same
    three reporters could undo the review with one tap.
19j. **A card is asked for after the sync, never after the change, and the reply is proved.**
    `publishGrove` builds the card from `players/{uid}`, so a publish requested the moment a piece was
    placed was answered from the save pushed *last* time — and the fingerprint then noted as published
    stopped the real one ever being sent. Every board showed every grove one session behind, for as long
    as the boards existed, with a successful call and a well-formed card on each publish. Three parts,
    each closing a way back. **The only thing that asks is `CloudSaveService.Settled`**, raised by a sync
    that left the server holding the save it carries and by nothing else — `Synced` is also raised by a
    switch, a link, a purchase and a deletion, none of which pushed anything — and the card judged is
    `GroveCard.OfSave` over the receipt's save, never the live ledgers, because a piece placed while a push
    is in flight is on the device and not on the server. **The reply is held to the revision the receipt
    named** (`GrovePublication.Proves`): a card built from an older save is `Stale`, pushed again and
    retried, and accepted after `MaxStaleRetries` because a refusal that will still be true tomorrow must
    never loop (13a); a server that reports no revision is not held to one (25's rule — presence says a
    deployment understands the field). **What makes a change reach the server promptly is
    `SyncTriggers`**, hung on *intent* events (`HomesteadLayout.Edited`, the three `Bought`s,
    `ProfileChanged`) and never on `Changed`, which every ledger raises on load and every sync raises by
    adopting a merge — a sync every three seconds for the life of the process. Two faults found on the
    way, both silent: `GroveCard.OfPlayer` walked `PlacedIds` (piece ids) as slot ids and carried no
    placements, so a rearrangement never moved the fingerprint; and opting back in published before the
    setting was pushed, so the server read "off" and withdrew the card just asked for. The note key is
    versioned (`grove.published.2.`) because every old note vouches for a stale card, and nothing but a
    reply can ever say so.
20. **A mode is code, and a chapter names one.** A way of playing brings an interaction, a fail state and a
    scoring rule, so content can never add one — but a chapter says which mode it belongs to (`mode` in
    `manifest.json`, absent meaning the classic `glade`), so a drop ships a whole second game with no app
    update. A chapter naming a mode this build has never heard of is **skipped whole and reported to nobody**,
    exactly as `minAppVersion` skips one needing newer code: an unknown mode is content from the future, and
    the honest response is to lose that chapter rather than open it into a screen that cannot run it.
    `GameMode` is a permanent string id.
20a. **A second mode's glade is an ordinary glade, and that is why it cost nothing.** It has its own
    permanent `LevelId`, so its record, stars, merge and rewards are the ones every other glade has, and a
    whole second *game* added **no save schema version, no `progression.json` retune, no `firestore.rules`
    change and no server work**. Anything tempted to key on a mode comes back to this: the two things that
    genuinely differ are *order* and *unlocking*, and those are per-mode in `CatalogIndex` alone. Totals stay
    mode-blind — `LevelIds` is every glade in the game — while `Next`, `Previous`, `OrderOf` and `IsLast` stay
    inside one mode, because chained end to end, finishing the classic game would be the price of opening the
    second one.
20b. **A mode may be a whole screen, and the second one is.** The first attempt reused the board, the light
    graph and the star rule, and the containment that bought is what made it fail: the same grid, the same
    conduits, the same critters, with a different way to fill it in. What a mode may share is the *world* —
    palette, colour arithmetic, critters, sounds — and what it must share is everything about being a **run**:
    the heart, the stake (`RunGuard`), the daily chest, the streak and the star ledger, reached through the
    same Domain classes rather than copied, because a second copy of the run lifecycle is a second place that
    can disagree about when somebody is charged.
20c. **A level carries a board or a hollow, never neither.** The constructor refuses both being absent,
    because a level with neither is a node on a map that cannot be opened and would validate perfectly.
    `PuzzleFactory` refuses a boardless level rather than throwing on it.
20d. **A hollow authors no numbers at all** — a grid of text and a string of spark colours. Par is the fewest
    sparks that finish it, found by search, and the star ladder falls out of par. A typed par is the failure
    with no symptom: one too high hands three stars to a careless run for ever, one too low makes them
    unreachable, and neither is visible in the file that caused it.
20e. **The ordered spark queue is the puzzle, and light never decaying is why.** Because light accumulates,
    the *set* of cells a player sparks decides the outcome and the order cannot — so a pool of sparks would
    collapse every hollow to "which cells", where an ordered queue makes it an assignment: this red has to go
    somewhere now, and the green behind it can only reach what the red left asleep. The same property makes a
    hollow impossible to get stuck in, which makes unlimited undo safe.
20f. **A mode is hard when its constraints cannot all have their way at once, and that is one number over
    the whole board, never a bar on each element.** *(Lightweave is retired; the rule is not.)* Judging its
    groves pair by pair — no channel may be the straight line between its own two ends — shipped a worse
    complaint than the one it fixed, because it sent every pair the long way round on a route the board had
    chosen, which the player experiences as the game refusing the line they drew. What replaced it is the
    least **total** detour any arrangement has, summed over every element above its own floor: zero means
    everything can be direct at once, and two or more means they contend — any one route may still be direct,
    and what the board denies is all of them being direct together, so the question is **who yields** and the
    player answers it. One placement rule survived: refuse two ends close enough to join by a reflex, which is
    a bar on where things *stand*, visible before committing, rather than on which way they must go. `weave`
    is a spent mode id.
20g. **A mode may bring a rule no board can demonstrate, and the fix is to make the board demonstrate it,
    not to explain it better.** Reported as "even though I wake up all the critters, the game doesn't end" —
    not a bug, and indistinguishable from one, because Lightweave was won when every critter was awake **and**
    no bare ground was left while the shortest route always wakes a critter, so the ordinary way to meet a
    grove was to collect the biggest celebration six times over and watch nothing happen. Saying it — a lesson
    shown once, a standing line naming the state — was right and not enough, because the rule itself was the
    fault. What replaced it asked for the same detour and *pointed at where*, and three things carried over: a
    demonstrating object must be placed where it actually constrains (5d — one met on the way past is
    decoration); the state that reads as a broken game still exists, so the **standing line stays**, now
    pointing at something visible; and it needed its **own silhouette**, because reusing an existing one left
    a finale of circles told apart only by what stood inside them. **Before shipping a mode, ask which of its
    rules a board can *show* — and if the answer is "none of them", the rule is probably wrong rather than
    merely untaught.** `weave_fill` is a retired lesson id: an id travels in the save like a level id.
20h. **A chapter's mode is derived from its levels, never typed — and the build gate proves it.** `mode` was
    the last manifest field written by hand, and the one whose absence nothing notices: it decides which
    screen opens a chapter's levels, which lane of the switcher it sits in and — through
    `LevelUnlock.GateFor`, which looks for the chapter before this one *in the same mode* — whose stars unlock
    it. Leave it out and the chapter is indexed as a glade chapter, every level parses, every board is proved
    solvable, every address loads and the build goes green, and what ships is a chapter gated on a stranger's
    stars, filed under the wrong tab and routed to a screen that cannot play it. That happened on the first
    sync of the second Lightfall chapter, with one line in a log as the only symptom. `Sync Manifest` derives
    the field and `ContentValidation` errors on any disagreement, because deriving makes a mistake unlikely
    and only a check proves it did not happen anyway. The rule lives once, in `ChapterModeValidator`.
20i. **A mechanic that moves the *floor* moves par, the ladder's yardstick and the top of the star ladder
    with it — and only one of those three had a check.** *(Lightweave is retired; the rule is not.)* Its third
    chapter added a barrier between two cells, while everything a weave was graded on derived from a
    **Manhattan** distance, which walks straight through one — so a hedged grove would have been graded
    against a floor no arrangement of it could reach: the three-star line below the best possible play, a
    whole band of the ladder silently gone, and invisible to every check there was, because the board is still
    solvable, still full and still measured. A distance became a walk over the ways actually open, so par,
    both star lines and the resource rose with the barriers by themselves. **The difficulty *reading* had to
    follow too**, and that is the half easiest to miss: contention measured against each element's own floor
    does not rise with a barrier — the barrier moves forced detour out of the number and into the thing the
    number is measured against. **And the gate that was missing is now the pattern**: nothing proved three
    stars was reachable at all, which is an exponential search that may never be a build gate, so it is a
    *test over every shipped board* instead. Two smaller rules: grow the barriers **before** the solution is
    carved, so a board is solvable by construction rather than by check; and refuse a barrier that changes
    **no** shortest route, because one the player routes around without noticing is decoration.
20j. **Three modes were designed for this slot and two were thrown away, and what separates them is not
   cleverness — it is whether a board can be read, and whether it can stall.** Three tests any new mode has to
   pass *before* a level is authored.
   <br>**One: the answer has to be visible on the board, now.** *Ripplewake* was expanding rings — drop a
   stone, its ring steps out a cell a beat, and where two rings arrive on the same water at the same beat the
   sleeper under them wakes. Every number was good: par searched cheaply, the ladder climbed, `ways` was low,
   a careless player cleared the first two rungs. It was played and the report was three words: *"I understood
   nothing."* The fault is structural rather than presentational — the thing to predict is a **coincidence
   several beats in the future**, so the puzzle lives in the player's head instead of on the board, and adding
   readouts is the wrong fix. **If a mode's payoff arrives later than the input, it is a thinking puzzle,
   whatever the numbers say.**
   <br>**Two: a finite board with no refill must not be able to freeze.** *Windfall* never shipped a level:
   swipe, everything slides, three alike touching burst, and the wind keeps blowing so it cascades for free.
   One gesture moving the whole board is the best spectacle-per-input there is, and it **stalls** — a tilt
   barely changes relative positions, so after one compaction per axis the board is frozen and the player
   flips left-right for ever (measured on the first hand-built board: stuck at beat 3 of 12). 2048 survives
   this only by spawning a random tile every move, and randomness is what makes par unsearchable. **Ask of any
   new mode: does every legal input strictly move something that only goes one way?** Budburst's does — a tap
   *adds a channel* and channels never come off, so the grove always moves toward white and toward a burst,
   and a wave always removes at least three flowers while nothing is added — which gives *cannot stall*,
   *always ends* and *the search terminates* at once. Note the shape: the monotone quantity is not the thing
   being counted (flowers) but the thing being *added* (channels), and a tap that would add none is refused
   outright rather than swallowed.
   <br>**Three: a cascade that spreads on its own is not a mechanic, it is a solvent.** An early cut of
   Budburst's chain rule looked finished after an afternoon and every board measured par two lower than
   designed, because a cell lending to its neighbours unconditionally walks outward for ever — measured at
   **thirty cells in eleven waves from one tap**, finishing a board built to take four. What settles it is a
   **threshold the spread has to clear again**, so a chain dies wherever the grove is not already nearly
   right. **A rule that makes boards more solvable is as dangerous as one that makes them unsolvable, and only
   counting finds it** — every one of these shipped a board that was solvable, correctly par'd, fully
   validated and wrong.
   <br>`ripple` and `weave` are **spent mode ids**, along with the nine retired lesson ids the two modes spent
   (`weave_join`, `weave_bead`, `weave_ink`, `weave_hedge`, `weave_fill`, `ripple_meet`, `ripple_satchel`,
   `ripple_reed`, `ripple_deep`, `ripple_lily`).
20l. **"Brain-dead" is a property of what the player has to *work out*, not of how hard the board is.**
   Budburst was commissioned as chill, tuned three times toward chill, and came back each time as *"you still
   have to think quite hard"* — with nothing wrong with the boards, because the fault was that **the match was
   invisible until you made it**. Every game of this shape shows the player the matches and asks them to pick
   one; this one made them work out in their head which cell the colour in hand would turn into a third of
   something, which is a simulation task no generosity in the numbers touches.
   <br>Four rules answered it, gated on one field: a grove with a <b>strip</b> (`regrow`) is **living**, and
   one without is **still** — the shape the mode shipped with, kept because eight vector cases pin the base
   rule in isolation. **The board said which taps pop** (`BudRun.Pops`), which was the change; the choice was
   untouched and what went was the arithmetic in front of it. **Withdrawn by the owner after playing the
   second chapter**: the halo, the graft links and the white flower's breath were all hints, and the board
   is to be *found* rather than read — which tap goes off, which pair trades and what white does are the
   player's to discover. A special's mark still turns, because that is what a special is rather than advice
   about it. The hint key stays, because it is paid for. **It falls, and it grows**, so the board never
   thins and the fortieth tap is dealt as good a grove as the first. **White is the bomb** — it holds every
   channel so it could never be mixed into, which made it a dead cell and the one state that punished the
   player for playing well, and tapping it now clears the square around it at the cost of no new object. And
   **one flower ripens between taps**, always beside somebody still shut in.
   <br>**Two of those nearly cost properties this file exists to protect, and both failures are the same
   failure.** Growing *inside* the chain destroys the termination proof — a wave used to remove at least three
   flowers from a board that never gained any, and a repeating strip can resonate with a grove for ever;
   measured on the first cut, **two thirds of opening taps ran into the wave ceiling and par collapsed to
   one**. So the chain falls and the grove grows afterwards, restoring the proof exactly. And what grows may
   never *make* a bunch, or the player is handed a cascade they did not cause. **Before adding a rule that
   puts something on the board, ask what used to bound the loop.**
20k. **A mode may be built to be *easy*, and then two of this file's own rules invert.** Budburst is the
   first mode commissioned against a feeling rather than a difficulty: *chill, hypnotic, one tap and something
   enormous happens*, the register Royal Match and Toy Blast play in, where everybody finishes and the stars
   are where the skill lives. **`ways` flips**: 5d warns above a threshold, but here the brief *is* a board
   almost anything finishes, so `BudValidator` warns **below** two — one single shortest play means the grove
   has to be solved rather than played. **`greedy` flips and becomes the bar**: a grove a careless player
   *cannot* finish inside its satchel asks for more than the mode promises, and the shipped board was chosen
   over three shorter baskets whose careless play was *optimal*, because a greedy player playing perfectly
   means the grove decided nothing at all. What does **not** flip is anything about money or grading: par is
   still searched, the star lines are the same multiples, and the fail state is real. An easy mode is one
   whose *boards* are generous, never one whose arithmetic is.
20m. **A mechanic the author places is one the player finds, and a payoff has to be one they made.** Budburst's
   second chapter shipped a **runner** — two squares joined by a vine, firing whenever a bunch took in an end
   — measured before it was authored (`changed`, `caught`, the threshold *in* the bunch, the vine painted from
   the first frame), and it came back from one session as *it does no visually different animation, you do
   not have to do anything special to trigger it, and I do not like the vines passing through the board*. It
   was replaced by **five candidates on a grove each** — a windmill that slid a row, a firefly that banked a
   colour, the graft, a puffball that painted its square on the second burst, a hive that swarmed at the third
   — each chosen for a decision the player makes and a way of being wrong (26h's second half), each with its
   own reading warned at nought, every gate green. The verdict on all five was one sentence: *zero new
   animation, nothing different, all I see is flowers popping.* And it was right, for one reason that is
   not about animation: **every one of them paid out as the same chain, and every one of them was put on
   the board by me.** A player who finds an object has been handed a rule; a player who makes one has been
   handed a reward. 26g's test (*replace it with the nearest existing thing*) and 26h's (*what does the
   player decide*) both pass a mechanic that is nobody's achievement. The test that was missing is
   **who put it there.**
   <br>**So the chapter is now the genre's own loop.** A bunch of **five** leaves a **bolt** on the cell the
   player tapped; a bunch of **eight** leaves a **sun**. A special is a flower wearing the bunch's colour,
   and it fires when tapped, when a bunch takes it in, or when another special's reach hits it: a bolt clears
   its row and column, a sun the five-by-five around it, and **a special in a fired special's reach fires
   too**, in the same wave, which is the chain the chapter is for. What a special clears it does **not** wash
   (20j's solvent, again), but it cracks every cocoon it hits and every cocoon beside what it cleared. The
   **graft** stayed — drag two neighbours to trade, refused unless it makes a bunch — because it was the one
   thing the owner said *made a little sense*, and because it is how the genre makes fives. `forges` and
   `grafts` gate both, because the Thicket was authored and pinned without either and bunches of five happen
   there. The forge lands on the cell the player touched, the graft's special on the flower they moved: what
   you did is what you get.
   <br>**Four rules from building it. One: the event is the reward, so it gets the biggest drawing in the
   mode** — a forge is drawn as an arrival (bare for a beat, motes gathering back, the special standing up
   under a ring with the one "you got something" sound), a bolt as lightning drawn out to both edges with
   the cells in its line racing outward (`BudTempo.FireStep`), a sun as a blast with the heaviest shake in
   the mode. **Two: a move that spends a tap need not spend a colour** (`BudRun.Dealt` beside `Spent`): a
   graft keeps the colour in hand, or every trade is also a colour thrown away. **Three: a strong move
   collapses par, and the fix is the board.** A graft bursts by construction, so a grafting grove of eight
   cocoons is par 2 on every fill; the shipped ones carry twelve to fourteen, several tough, spread to every
   edge. **Four: two readings, both warned at nought** — `forgeable`, opening moves that forge one (the
   player can make one on the board as dealt), and `fired`, shortest plays that fire one, measured over every
   shortest solution (26h's `kindled`). `BudObjectReading` holds them, `bud.py` mirrors them,
   `BudLadderTests` pins them, and move order is part of the contract (`BudRun.Moves`).
   <br>**Retired ids that must never be reused: the lessons `bud_runner`, `bud_gust`, `bud_firefly`,
   `bud_puff` and `bud_hive`; the fields `runners`, `winds` and `firefly` (refused by name, 5f); the ten
   level ids of the runner chapter** (`b02_firstvine`, `b02_longreach`, `b02_deepthicket`,
   `b02_windingway`, `b02_twovines`, `b02_thewilds`, `b02_crossvine`, `b02_thornedvine`, `b02_thetangle`,
   `b02_tangleheart`) **and the five of the objects chapter** (`b02_windrow`, `b02_lanternfly`,
   `b02_graftwood`, `b02_puffhollow`, `b02_hivehill`) — the second five were played on a device, so a save
   holds their records. The chapter id `b02_tanglewood` is kept.
21. **A chapter is opened by stars, and only its first level asks.** Inside a chapter the chain is unchanged;
    at a **boundary** `LevelUnlock.GateFor` opens the next chapter once the player holds `starsPerLevel` stars
    per level of the one behind it (shipping as 2, so 20 of a ten-level chapter's 30). The two rules answer
    different questions and only one is about mastery — ten levels cleared at one star each is a player who
    never met what the chapter taught, and a player beaten by the ninth of ten had no route forward except the
    board that beat them, where a star gate can be met from anywhere in the chapter. It is authored **per
    level** rather than as a total, because a chapter is not a fixed size. Four things follow and three were
    bugs the change laid: `NextToPlay` must return the **furthest unlocked** level rather than the last of the
    mode; the victory panel's Next asks `IsUnlocked` rather than `index.Next`; a chapter opening is a
    **transition**, measured either side of the record fold in `RunLedger`; and every screen that said "clear
    this chapter to go on" now prints the **count**, because the old sentence is wrong and unactionable to
    somebody holding nine cleared levels. `ChapterGateTable` is content; 0 is legal and opens everything. **A
    level already cleared is always open**, and that clause is load-bearing rather than kind: the rule in
    front of it is content, so it can be raised, and it *did* change under everybody already playing — without
    it an account that cleared three chapters at one star each opens a chapter it finished and finds the first
    level padlocked with the nine behind it open. It cannot weaken the gate, because a level nobody has
    cleared is a level nobody has opened.
26f. **A payoff handed out for free cannot be a payoff.** Lightfall's second chapter brings the **lens**:
    glass holding no light of its own that *fills up* one channel at a time from any light reaching it, and on
    the third **fires**, each beam crossing bare ground until the first cell in its line takes it. It shipped
    *relaying* instead — any burst beside it set it off once, free — and came back as both "it made the game
    much easier" and "the animation is too weak", which are one fault: a relay that costs nothing happens on
    most drops touching glass, so it hands out reach for free *and* happens far too often to be worth stopping
    the board for. **Before animating anything, ask how often it fires; if the answer is "most turns", change
    the price, not the effect.** The price is arithmetic rather than a dial: a wave washes one drop's colour,
    so a lens gains **at most one channel per drop**
    (`FallGlassTests.OneDropCanOnlyEverAddOneChannelToGlass`), and how full the glass starts is the whole
    chapter ramp — over ninety generated boards, two-thirds-full glass left 50 solvable, one-third 38, empty 7.
    <br>**A lens fires white**, so every mote a beam lands on is completed whatever colour it was — the one
    thing allowed past the threshold rather than through it, with the bound moved from the consequence to the
    cost. **A mechanic may buy reach or it may buy threshold, and only one is safe to give a cascade.**
    Charged normally it fires **sideways** (a well has gravity, so a downward beam crosses one cell into what
    holds it up and an upward one flies into the air); **struck by another lens's shot** it fires on all four
    axes, which is the chain the chapter is built on. `FallBoard._struck` is carried by `Settle` and copied by
    `Fork`, because a fork that drops state the rule reads is a divergence nothing can see; and
    `FallBoard._gain` is a mask accumulated with `|=` rather than a bool latched by whichever arrived first,
    because one cell can be reached by a burst and a beam in the same wave and must take both.
    <br>**A mechanic whose only fuel is destructible must have a second way to feed it.** Glass was charged
    only by burst light and light only comes from a mote, so a player who cleared the motes first was left on
    a board that could not be finished and would not end — the obvious line, measured at three drops away on
    the fifth board. The valve: a drop landing on glass is taken *in*, one channel a drop. Feeding by burst
    stays free so the search still prefers it (**par unmoved on eight of ten boards**), and `ways` inflates,
    so the boards were re-picked rather than the rule backed out.
    <br>**A question with two answers asked as two predicates is one some caller will ask half of.** A mote is
    *enriched* and a lens is *charged*, and `Enriches` is `IsMote(...) && ...` — so `FallView.Drop` drew every
    charging drop as one that had come to rest on top, and the lens's own widget fell out of the index: on
    screen, owned by nothing, never repainting or leaving. The fix is `FallBoard.Takes`, the clause `Landing`
    already turns on, said out loud. Two more silent narrowings of the same kind: `Wanted` read a lens as a
    mote wanting all three, and `FallCell` exists so everything asking "is anything here" is correct unchanged
    while everything asking about light must say which kind it means.
    <br>**`aim` is the metric** — how many of a lens's shots land on anything, out of two, since glass
    pointing at nothing took three drops of charging and bought nothing (5d, where nobody would look). It
    warns and never refuses; reachability needs no check, because **a lens can only leave the well by
    firing**. Two shipped boards stood their pane on a mote and answered par 3 and par 4 while that downward
    beam existed, and **par 6 with 55 and 52 winning lines** once it did not — still valid, and deciding
    nothing. **A rule change that makes a board easier is as invisible as one that makes it harder, and `ways`
    is what says so.** A well holding cells that can never be completed gets **no continue**, because selling
    motes into a run already finished is 23's forbidden charge.
26g. **A mechanic that delivers light competes with the lens, which delivers all three channels — so the next
    one had to change *which colour is travelling*.** The third chapter shipped a **mirror** first (silver
    turning a beam ninety degrees), reported useless in one sentence: *"lenses was doing the same thing"*. It
    had **no event of its own** — it could not be triggered, only passed through, emitted nothing, and on a
    board with no glass did literally nothing. Every reading here is blind to that: solvable, correctly par'd,
    `ways` tight, `greedy` lost on all ten, every gate green, because **a decoration passes every one of
    those**. The check that would have caught it is one comparison and was never run: *replace the new object
    with the nearest existing one and see whether anything changes* — every board was still solvable with a
    lens in the mirror's square.
    <br>So the test for a new object here is **"what can it do that a lens cannot"**: anything else delivering
    channels is weaker-or-equal and competing on degree, leaving remove channels, move cells, create cells,
    change the deal, change gravity, or change the colour travelling. **The wick was that** — one authored
    channel, lit by *any* light, burning that colour into the four cells beside it on the next wave and gone —
    and it was withdrawn too, one session later, for the fault this invariant's own test could not see. **The
    list above is right and the test above is half a test**; see 26h for the other half. What survives here
    unchanged is the *comparison*: replace the new object with the nearest existing one and see whether
    anything changes. Every board was still solvable with a lens in the mirror's square.
    <br>Two smaller things the wick taught and that outlive it. Two predicates had to be **narrowed** for a
    cell that is occupied and is not light, and both were silent failures (`FallCell.IsMote`, `Wants`); the
    whorl needed exactly the same two narrowed again, for exactly the same reason. And `Drop` did
    `_cells[i] |= colour` on the landing cell, quietly making a **two-channel wick** — a value no rule names
    and the letters cannot write down. Retired ids: the lesson `fall_mirror`, and `/` and `\` as a mirror.
26h. **A mechanic can have an event of its own and still be the lens again; what separates them is
    whether the player *decides* anything about it.** The third chapter shipped a **mirror**, then a
    **wick**, and both came back from a single session of play. 26g diagnosed the mirror correctly —
    no event of its own — and prescribed *change which colour is travelling*, which is exactly what
    the wick did. The wick was then reported as **boring**, which is the same verdict a step further
    in and the more useful one. It held one authored channel, *any* light set it off, and what it did
    was wash that channel into its four neighbours — this mode's own burst with the colour swapped.
    Every reading was good: solvable, par'd, `ways` tight, `greedy` beaten on all ten, and `earns`
    (26g's own instrument) non-zero on every board. **None of them can see that nothing about it was
    ever the player's choice** — its colour was fixed at authoring time, its trigger was free, and its
    effect was identical on every board it ever stood on. It played as a second kind of mote that
    pops itself. So 26g's test needs its second half: *what does the player decide about it, and can
    they be wrong?*
    <br>**A whorl draws the motes standing either side of it together and mixes them into one.** That
    is the mode's own arithmetic — `|`, the operator a drop, a wash and a beam all use — applied to a
    pair of operands it never had: every other rule adds a *colour* to a cell, and this is the only
    place two *motes* are combined. A cyan and a red that were each a drop away from white are none.
    Nothing has to be taught for it; a player who has cooked one mote already knows what yellow and
    blue make. **It pulls sideways**, which is a fact about gravity rather than a choice: the well
    falls, so across is the one direction nothing here ever travels in — the same observation that
    makes a lens fire sideways, turned into a verb. It is the only object in this mode that *moves* a
    mote.
    <br>**The trigger is free and the *pair* is what costs.** Any light opens one — a burst beside it,
    a beam, or a drop straight onto it — for the reason the wick had that rule and the lens had one
    added after a player was stranded (26f); and a whorl that turns with nothing beside it **closes**
    rather than waiting, so it is always removable and `FallVerdict` needs no clause about it at all.
    What it gives back is decided entirely by what is standing either side of it **at the instant it
    turns**, and the well collapses under every chain — so the player is engineering two particular
    motes into two particular cells and then choosing the moment. Being early is a real mistake with a
    real cost, which is what makes the timing a decision rather than a formality. A lens asks for three
    drops of three colours in any order at all; a whorl asks for one arrangement.
    <br>**The reading that judges a board is `kindled`, and it is stricter than the one the wick had.**
    `fused` counts whorls that drew in a *pair*; `kindled` counts those whose union reached white. Two
    yellows drawn together make a yellow — a tidier board, deciding nothing — where a yellow and a blue
    make a burst the player *arranged* and could not have bought with any single drop. It is measured
    over **every** shortest solution rather than the first one the search reaches: `ways` is rarely one,
    so the first winning line is arbitrary among several and an author tuning against it is tuning
    against a coin toss. Nought kindled on a board carrying whorls is the answer that condemns it.
    <br>**And one half of it is *exact* where the lens's `aim` is only geometry: gravity never moves a
    whorl sideways.** It draws from the two columns it is authored between, whatever the well collapses
    into — so one standing against a wall has one side for its whole life and can never merge a pair.
    That is the one thing here a validator can prove rather than measure, and it warns rather than
    refuses only because moving a single mote inward is a real if small effect.
    <br>**Three clauses keep the wave free of a reading order**, which is what `FallBoard.Resolve` is
    built around and what a second runtime would diverge on silently. A mote already leaving this wave
    is never drawn in — the light got to it first. **A whorl draws light and nothing else**: glass
    beside one stays where it stands, or the rule would have to say what a lens and a mote mix into,
    and two whorls would eat each other. And a mote with a turning whorl on *each* side is let go by
    both, which is the only symmetric answer available. Every claim is read off the board as it stands
    and only then marked as in motion; marking them as they are taken would hand a contested mote to
    whichever whorl the loop reached first.
    <br>**Retired ids that must never be reused: the lesson `fall_wick`; the cell letters `1`, `2` and
    `3` as a wick** (refused *by name* at parse, for the duskcap's reason — a file carrying one is
    content written for a build that no longer exists); **and the chapter id `f03_wickwater` with its
    ten level ids.** That chapter was never committed, let alone released, so its ids were re-authored
    honestly rather than kept — 26g's own precedent for the mirror, and the reason invariant 1 binds
    *shipped* ids rather than every id that has ever existed. The **chapter** id moving is what
    distinguishes this from the duskcap, where the level id was kept and only the string above it moved
    (5f): there a real save could hold the id, and here nothing outside this working tree ever could.
27. **Deleting an account removes data first and the account itself last, and that ordering is the only thing
    making it safe to retry.** `deleteAccount`: **visibility first** (`groves/{uid}` and the row scrubbed out
    of all ten `leaderboards/*`), because a run that dies halfway must never leave a deleted keeper's name
    where a stranger can read it and this cannot wait for the nightly rebuild; **the name next**, while the
    wallet holding the key is still readable, since `names` is not queryable by uid and releasing it later
    would strand a reservation nothing could find; **then the save**, recursively, so a subcollection added
    next year is not a list somebody forgot to extend; **then Apple; the auth user last**. Every failure
    before that last step is still authenticated, so the client calls again and every step is
    delete-if-exists — where deleting the user first would leave documents under a uid nobody can ever
    authenticate as again, the one failure here with no repair.
    <br>**Three things are deliberately kept**: `receipts/*`, the globally-keyed record that a transaction was
    granted (18a), or "buy, redeem, delete, sign up, redeem again" is a faucet costing an attacker one
    purchase; reports this account filed about *other* people, because the parent's count is denormalised from
    them; and a **denied** name's reservation, retargeted to a tombstone uid. **The client's half is
    server-first**: nothing local is touched until the server confirms, which lets every failure sentence say
    "nothing has been deleted" and be true, and it runs under the sync latch throughout, or a sync in flight
    puts the grove back into the document being deleted. `SaveService.EraseAccount` drops **only** this
    account's slot and leaves the device on a fresh anonymous account, because there is no sign-in screen in
    this game.
    <br>**A linked account re-authenticates first, and that is one step doing two jobs** — proof of ownership,
    and Apple's single-use `authorizationCode`, which expires in minutes and so can only be obtained when it
    is needed. It is `ReauthenticateAsync` rather than a sign-in precisely because a sign-in *replaces* the
    session. **A revocation failure never blocks a deletion.**
22. **A puzzle is graded on the puzzle, so there is no clock anywhere in this game.** Stars were the *worse*
    of what the turns allowed and what a countdown allowed, and that one word is the fault: for anyone who
    stopped to think the clock was always the lower reading, so the half that measures whether a board was
    solved *well* decided nothing. Everything 5d asks of a glade exists to force a decision and a countdown
    prices deliberation; it also scaled with the wrong thing, since the limit came off par and par is
    **length**. Gone with it: `timeFactor`, `difficulty.clockScale`, `DifficultyRuleTable`, `RunClock`, the
    tap-rate warnings, the `run_continue` rewarded ad, and the timer on the board.
    <br>**Three lines, even thirds of one slack**: a run scores inside `[par, par × 1.60]` cut into bands 0.20
    wide — three stars at **1.20**, two at **1.40**, the run ends at **1.60** — and **they must move
    together**. The budget was once cut to 1.60 while three stars was `par × 1.35` and two `par × 2.00`,
    putting the two-star line outside the survivable range: **one star became unscorable by anybody**, every
    number individually plausible, every board green, a third of the ladder gone. `CheckStarBands` and
    `content.py` prove `gold < silver < budget`, driven by
    `PressureTests.TheStarBandCheckCatchesAStrandedBand`, and both read the **factors**, because at par 1 or 2
    all three round onto one number.
    <br>**`MoveBudget` has no floor, deliberately** — it clamped to `SilverThreshold + 1` so a run still
    earning stars could never end, which was sound while the clock was the fail state and wrong once the
    budget was the only way to lose. What keeps it fair is that the meter counts **committed** wrong turns
    only: undo is unlimited and refunds a turn, and a hint charges none, so exploring is correct play rather
    than flailing. **Nothing already earned moved**, because stars are stored and only promoted.
    **`run_continue`, `ChestDropKind.RunTime` and `DefeatReason.OutOfTime` are retired ids.** Two things were
    **not** measured: the three numbers were reasoned about rather than played against, and whether the
    chapter gate got easier or harder is genuinely unknown. **`bestMillis` is retired in place, not deleted** —
    it is on the wire and `hasOnly` is an allow-list, so dropping a field a rolled-back client still writes
    loses *every* save write (12a).
22a. **A mode with no turns is graded on the count it does keep, never on how fast it was.** *(Lightweave is
    retired; the rule is what a mode does with no move to count.)* It reported a constant as its move count,
    so the clock decided every star and removing the clock would have handed three stars to every clear. What
    replaced it was already there: the cells its channels took, against the sum of every pair's shortest
    route. It fixed a second thing quietly — the record and the published deciles had been fed that constant,
    so every player held an identical "best" and the population ranking meant nothing.
22b. **A mode's fail state is a budget in the unit it is graded in.** *(Retired with Lightweave; every mode
    since strikes the same bargain in its own unit — a well in motes, a groove in tiles, a thicket in taps.)*
    A resource dealt as `par × budgetFactor`, spent per unit covered, with the run ending when the board
    provably cannot be finished; nothing about grading moved, so the budget is the third line of a ladder that
    already had two. **Spending is permanent and that is the whole mechanic** — erasing frees the *ground*,
    not the resource, or the meter rejects no arrangement and is decoration — and what keeps it fair is that a
    wrong move is cheap to *discover* and only expensive to *keep*: a drag costs nothing until it lands, is
    walled at what is in hand, and two landed moves are handed back in full. **Resource spent is the grade**,
    not units occupied, so the meter and the stars cannot disagree. **The two loss conditions are lower
    bounds, and have to be**, because ending a run the player could still have won is the worst thing a mode
    can do — so each unfinished element's floor is counted on an *empty* board, plus the half a floor cannot
    see: something walled in where freeing it costs more than is left (20g's state). `EndsTheRun` belongs with
    them rather than in an `if` on the screen, because a run decided twice charges two hearts for one loss.
23. **A lost run may be bought back, and the offer comes before the accounting rather than on top of it.**
    Only when the gem offer (`ContinueOverlay`) is declined does the defeat happen — heart, record, chest
    count, streak, analytics — because a continue offered *after* `RunLedger.Loss` would be an offer to undo
    an accounting entry. `RunContinueFlow` owns it, since two copies would be two prices and two chances to
    charge for a board that was still lost.
    <br>**It cannot inflate a reward, and that is arithmetic.** Stars are held against par, never the budget,
    so a run at its fail state has spent past the two-star line and can score **one star at most** — less than
    replaying for nothing. The offer sells a *finish*, never a *grade*. Nothing reaches the save file, and the
    gems leave through `PlayerProgression.TrySpend`. **A continue that does not continue is a charge, so the
    shortfall is cleared first**: a glade's deficit is nought, but a mode lost on a resource usually has
    unspendable remainder, so selling the authored amount alone would end the run again in the same frame
    having taken the gems — `ContinueOffer.Amount` is `deficit + authored`, a mode that cannot be rescued
    answers `RunContinue.NoContinue`, and a grant is never silent, so if one leaves the run lost the player is
    **asked again**, not billed again.
    <br>**Short of gems must never navigate.** The board behind the panel is frozen at its fail state, so
    tapping "get gems" to *save* a run would lose it on the way to paying; `GemShopOverlay` stacks the shelf
    on the offer instead and steps out when the gems land. With no store configured it is
    `ContinueChoice.Unavailable` and no offer at all, because a control that can never work is worse than no
    control. **The price is content** (`continueRun`), charged at the worst moment in a session and most
    certain to be wrong first guess, and `enabled` is an **integer** tri-state rather than a bool, because
    `JsonUtility` instantiates a `[Serializable]` field even with no such key and a bool would read `false`
    for every client that had not taken a push, withdrawing the feature silently.
23a. **A lost run has two prices and they buy different things.** `RunContinue` sells the *run* (the board
    stands, so one star at most); `HeartRescue` sells a *heart*, which is the gate — the board is rebuilt and
    graded like any other. Different panels in a fixed order — continue first over a standing board, rescue
    only on the defeat panel that follows and only when there is nothing left to play with — so nobody sees
    both at once. Both are 20 gems on purpose: they can be met a minute apart, and a player quoted a second
    price after declining the first reads the pair as haggling. Neither costs the save file, the wire or the
    server a thing, and hearts pay nothing, so gems buy only *sooner*.
    <br>**`hearts.rescueHearts: 0` withdraws it, and the price refuses a zero** — opposite readings of one
    shape. An offer handing over nothing is no offer, so nought hearts is the clean way to say "withdrawn",
    which a market regulating paying past a play gate may need from a config push; a *free* heart is the gate
    no longer gating, so nought gems is refused and named. **The free way back is always drawn above the paid
    one** — a price above a rewarded video at the moment somebody has been stopped from playing is the shape a
    store reviewer is right to call a dark pattern, which costs a submission rather than a metric.
23b. **A mode whose fail state is not the counter it is graded on has to buy that promise back, and
    Thornwatch is the first.** 23's "a bought run can only ever score one star" is *arithmetic* everywhere
    else: a glade dies at `par x 1.60` having already spent past the two-star line at `par x 1.40`, so the
    promise costs no code. A siege is graded in **matches** and lost when its **ward line** falls, and the two
    are unrelated — a player outpaced on the fourth wave may have spent five matches against a three-star
    line of fourteen, so twenty gems would have bought a top-rung clear. That is not a grading curiosity: stars
    derive credits, credits are a grove's worth, and a grove's worth reaches a public board, so a continue that
    moved the grade would let money move a leaderboard (19a). `RunContinue.Toll` charges the run up to
    `SilverThreshold + 1` **against the graded count**, which is 39's own conversion asked of a continue, and
    it is a charge rather than a cap on the star because a cap would be a second thing deciding a grade and
    would leave the *record* — which feeds the published deciles — reading as an excellent run. Nought
    once a run is already past the line, so a second continue is never charged twice. **Before selling a
    continue in a new mode, ask whether its fail state and its grade are the same number.**
    <br>**What it sells is the line** (`ContinueUnit.Wards`, `SiegeBoard.Rally`): every fallen turret back at
    full health keeping the rank its cogs bought, with the hill standing exactly where it stood — no fuel,
    because fuel is damage and damage is progress; the douse cleared, because that is seconds the player would
    be paying for and not receiving. It is the one unit whose authored figure is the whole allowance rather
    than room above a shortfall, since a siege is lost when the **last** ward falls and every ward is down by
    the time the offer is made.
    <br>**`Stranded` stopped meaning "no move left" and went back to meaning what it says.** It was
    `WardsStanding == 0` — identical to `AnyMove` negated — and it is `false`, because the only question
    it is ever asked is *would a purchase rescue this* (28f). Two call sites had been leaning on the accident:
    `ProtoVerdict.Read` hard-coded `NoContinue` on the `Stuck` branch, which is right for a cairn with nothing
    left to pull and wrong here, and `SiegeUtility.Would` used it to mean "the run is already over" and now
    asks `AnyMove`. **A predicate that has been true for the same reason as another one for five modes is a
    predicate nobody has had to read carefully.**
    <br>**And a continue that does not continue is a charge, which here is a fact about the level rather than
    about the offer**, because the hill is still walking and every raider that got through is at the line with
    its hammer up. Measured with **nobody playing at all**, a rallied line stands **10.4–13.5 seconds** on
    all ten rungs — four or five unhurried matches before a finger is lifted, and longer as it is used.
    `SiegeRuleTests.ARalliedLineStandsLongEnoughToBeWorthBuying` is the bar at eight seconds, and it is the
    only thing that could ever see a partial raise or a smaller `WardHealth` making the offer worthless.
    <br>**A run nobody is charged for is a run nobody is sold**, and that lives in `RunContinueFlow` rather
    than in a mode: 24 read from the other end. The worst moment to meet the gate that stops somebody playing
    is while they are still working out what the verb is, and the worst moment to meet a *price* is the same
    moment one step further in — so a mode's opening rungs and any level already finished lose without an
    offer, which are exactly the runs the defeat panel already gives a free retry on. Nothing about this
    reaches the save file, the wire, the server or `seed-config.mjs`: `continueRun` is deliberately unpublished
    (nothing about a continue is adjudicated), so the retune is a build rather than a seed.
24. **A run is free when it teaches nothing new, and the rule lives in one predicate** —
    `HeartStake.PriceOf`. **The opening**: the first `hearts.graceLevels` levels (3, content) of the **first
    chapter of each mode**, because the worst moment to meet the one gate that stops somebody playing is while
    they are still working out what the verb is — per mode, since a mode shipping next year is somebody's
    first board of it. **The replay**: a glade already **finished**, for ever, because a board they beat is
    not content and cannot pay for itself anyway (stars only promote, credits derive from them), so the gate
    was guarding an empty room — **cleared, not attempted** (`PlayerProgress.IsCleared`), so a glade tried and
    lost still costs. Two consequences are the point: a finished glade **raises no confirmation at all** when
    left or restarted, because a warning about a heart nobody is taking teaches that warnings mean nothing;
    and the map's door **opens on finished levels with an empty heart bar**, which is the one thing to do
    while hearts refill and the half of the rule a player cannot discover, so `GladeRewardsOverlay` says it.
    <br>**And the rungs the heart gate lets go are the rungs the fail line lets go too.** Charging a heart
    while somebody is working out what the verb is is the mistake this invariant names; ending their run for
    the same reason is that mistake one step in, and it is invisible because each rule is individually
    correct. Every mode's opening level is authored `budgetFactor: -1` — except Budburst's was not, which
    nothing noticed for a whole chapter. Its first two groves cannot be lost at all now and its third, the
    first grove in the game with a fail line on it and therefore where `bud_satchel` is first taught, is
    dealt the most generous satchel in the chapter. **Nothing about the boards got easier, because they
    could not**: at that size a grove whose chain runs three waves is one that a single tap frees everybody
    on, so par collapses to one and the whole star ladder goes with it (26d). What can be made easier on a
    teaching rung is what a mistake costs, and that is all.
    <br>**One run has one price, and `RunScreen.Price` is it**, asked by the screen once as a fact about *its
    level* and by the map's door about a board nobody has opened; the abandonment, the crash marker and the
    defeat all read the screen's answer, and the defeat is **told** rather than working it out again.
    **`Price` is a fact about the level, not about the run, and that cost a silent bug**: latching at `Commit`
    and clearing at `Resolve` reads as obviously right and is wrong, because **both modes call `Resolve` a few
    lines *before* `RunLedger.Loss`**, deliberately, so a crash mid-defeat cannot charge twice — so a stake
    cleared by `Resolve` reads "free" at the exact instant the heart is taken, and every lost glade becomes
    free with the gate still drawn everywhere. The answer is resolved per screen, and **the latch is one-way**:
    a free answer is kept for the screen's life, a charged one re-asked. `RunGuard.Claim` runs from `Boot`
    before content has loaded, so a free run writes **no marker at all** — the only place that still knows.
    Nothing about any of it is stored.
    <br>**The defeat panel has to tell the silences apart.** "No heart was taken" is three pieces of news — an
    opening, a finished glade, or nothing left to take — so `LossRecord` carries the whole `HeartPrice` rather
    than letting the panel infer from `HeartCharged`: read a free run as an empty wallet and the panel refuses
    a retry to somebody who can use one, and read an empty wallet as a free run and it offers one to somebody
    who cannot. A free run also replaces the heart row with the reason, because five empty hearts under a run
    that spent none is a picture of a charge that did not happen, above a working retry button.
24a. **A run is charged when it ends and gated when it begins, so every door has to ask — and only one of
    five was asking.** Reported as: *a level can be restarted for ever with no hearts left.* Every individual
    rule was right; the invariant joining the two moments — **a run may only begin if the player could pay for
    it if it went wrong** — existed as a line inside `LevelsScreen`'s node tap and nowhere else. The victory
    panel's next, an event's two ways in and the restart key all opened a charged run on an empty bar, and the
    restart was unbounded, because an abandonment charges through `Wallet.TrySpendHeart` — **which at nought
    hearts reports "already out" rather than refusing**, so the caller treated the answer as news rather than
    as permission.
    <br>**The fix is a predicate every door asks, not a check on the door that was reported.**
    `HeartStake.CanBegin`, asked in `PlayRoute` — the funnel the navigating doors already walked through. **A
    restart is two answers with a charge between them** (`CanRestart`): pay for the run being left, then ask
    `CanBegin` of what remains, so a charged glade needs **two** hearts to restart and one to enter,
    arithmetically the same rule as leaving to the map and walking back in. **A run already under way is
    refused with a line over the board, never with `OutOfHeartsOverlay`**, which navigates to the shop and
    self-closes when `Profile.CanPlay` reads true — right only when nothing stands behind it, because leaving
    through `Flow.Go` abandons a run *without resolving it* and `RunGuard`'s marker then charges a heart at
    the next launch. The refusal must call `Resume`, because the pause menu's restart declares a hand-off, so
    a refusal that took nothing would leave a board that never thaws. Two smaller finds: the defeat panel's
    retry read `HeartsLeft`, a **snapshot** taken when the run was lost, so a rebuild after a rescue
    recomputed from a stale nought; and the victory panel asks **before** it closes, so a refusal does not
    strand the player on a solved board under a modal.
24b. **A refusal over a live run brings the shelf to the player; only a refusal with nothing behind it may
    navigate.** `RestartGateOverlay` stands over the run: the free way (`HeartVideoFlow`), the paid way
    (`HeartRescueFlow`, gem shelf **stacked**), a countdown, and KEEP PLAYING under both, because carrying on
    is a real answer. **Onward is `RunScreen.RestartLevel` again, never `Rewind`** — hearts arriving do not
    imply the gate has lifted, so calling the mode's rewind directly would be 24a's bug reintroduced by its
    own fix; re-entering the door re-asks the gate and yields the ordinary forfeit confirmation, since
    skipping it because money changed hands would make a paid restart *less* guarded than a free one.
    <br>Three details that would each have shipped as a bug: the panel hands the board back from `OnDestroy`
    unless it declares a hand-off; the auto-proceed is gated on `Flow.IsTopModal`, because hearts land the
    instant a video finishes and the panel would otherwise close out from under `PrizeOverlay` and raise a
    forfeit confirmation behind somebody's confetti; and `HeartRescueWhere` splits the analytics without ever
    reaching `HeartRescue.Offer`, since a per-panel price would be the haggling 23a refuses. `RestartGateTests`
    builds the real panel, and writing it found two faults no reading would have — **edit mode dispatches no
    `MonoBehaviour` messages**, so `DestroyImmediate` alone never runs `OnDestroy` and a case asserting the
    board was *not* handed back passes for the wrong reason; and `Build` calls `Paint`, so a rebuild is a
    second route into the auto-proceed.
25. **A variable reward the client shows must be one the server can recompute.** The victory panel's video
    offer spins eight equal slices, each a **multiplier on that placement's own amount** (`BonusWheel`,
    `ads.wheel`), so the feature costs **no schema version, no merge rule and no `claimAwards` work**. A prize
    the client *names* is one the server has to be told about, and 10d is why it cannot be told; a multiplier
    over an amount it already publishes is the smallest thing to recompute. The slice is a pure function of
    **(account, day, spin index)** through the same `subjectSeed` the golden bonus uses, so `BonusWheel.cs`
    and `wheel.ts` reach the same wedge without telling each other anything.
    <br>**The spin index is server-owned, and that is the half easy to get wrong.** Two counters that both
    increment drift the first time a callback is delayed past the next win, and the visible form is a wheel
    landing on five hundred while the balance rises by two — so it lives on `players/{uid}/private/wallet`,
    advances only inside the transaction that *grants* a win-bonus view, and rides back on every wallet reply
    (`readWallet` must carry it through, or the next write deletes it). **Presence of the field says a
    deployment understands the wheel**, so a client that has heard nothing draws no wheel and falls back to
    the flat offer, which is exactly what such a server grants; an absent `wheel` block therefore means the
    **flat offer**, never the built-in ladder.
    <br>**The odds are uniform and printed, because they can be** — a weighted wheel drawn with equal wedges
    is the specific lie loot-box rules exist to catch. A slice may never pay **below the flat offer**, and a
    wheel with no slice above it is **refused**, because a spin that rejects no outcome is decoration. **A
    spin cannot be re-rolled**, and that falls out of the seed rather than being enforced; what advances the
    index is a *paid* spin, and `WheelStand.NextSpin` is the **larger** of the server's tally and
    `RewardedAds.WatchedToday`, because each can be the one that knows more.
    <br>**The payoff is a panel of its own** (`WheelPrizeOverlay`), replacing a caption change on the button
    that had just asked for the ad — the largest moment in the placement drawn as the smallest change on the
    screen. It is raised **before** the wheel is asked whether it still exists, because a player who
    backgrounded the app during the video may be elsewhere and the prize is theirs either way; and the offer
    button goes to **COLLECTED, unclickable**, latched on the panel rather than read off the placement,
    because a cooldown and a cap both *expire* and a player sitting on a victory screen would otherwise buy
    the same glade's bonus twice. The ladder averages 218.75%, so `win_bonus` pays about 438 a view instead of
    200 and its daily cap went from twelve to six in the same drop — a swap, not a raise.
26. **A mode that cannot be lost is a prototype, and Lightfall was one.** It dealt random colours into an
    empty well until a column filled up, and every consequence was invisible in the file: a board with no
    fixed future cannot be *searched*, so it could author no par; with no par there is no star line and no
    budget; with no budget no fail state; and with none of those it is not a level, it is a toy with a score
    on it. **A well now authors what is standing in it (`rows`) and what it deals (`motes`), and nothing
    else.** Par is the fewest drops that empty it without breaching the brim, and the star lines are the same
    1.20 / 1.40 multiples every mode uses, so a second mode cannot retune the economy. The procession is
    **ordered and repeating** (20e in a second place): light never comes back, so the *set* of colours could
    not otherwise matter in what order.
26a. **The chain the mode was documented as having did not exist and could not.** The old rule destroyed a
    white mote and the four touching it, and both `FallBoard` and its tests described the cascades that set
    off — there were none, because nothing changed a mote's colour except a drop, so the first wave took every
    white and the second could never find one, and the wave counter, the rising pitch and the chain multiplier
    were dead code against a rule that rejects them (5d, where nobody thought to count). What replaced it is
    *one* destruction and a spread: **a white mote bursts alone and washes the colour that finished it into
    the motes beside it**, so any of them thereby completed bursts in turn — which is what makes one drop
    worth more than one mote and reaches a mote buried where no drop could land. The whole thing rests on an
    ordering: a wave decides what bursts and what it washes **from the positions the bursting motes are
    standing in**, before anything is removed and before anything falls. Apply the wash after gravity and a
    mote stands in the burst's own cell rather than beside it, so nothing ever touches it.
26b. **Two fail states, and only one of them may be sold a continue.** The supply running out is 22b's budget
    in the unit the mode is graded in. The **brim** — row nought, drawn with a hard line under it — is the
    other, and it is what makes each drop a spatial decision: a colour the top of a stack already holds has
    nowhere to go but upward, so one wrong mote costs a row of headroom *and* a mote. Running dry is a
    shortage more motes fix; a flooded well is not, so `ContinueDeficit` answers `RunContinue.NoContinue`,
    which means the mistake money cannot fix is the one skill is about. Both are read by `FallVerdict` in one
    predicate, because three booleans in an `if` on a screen is three edges where the run is decided and the
    screen has not caught up.
26c. **A procession must carry all three channels, and the well that cannot be lost is why.** The weaker rule
    — supply every channel the *board* is missing — is wrong by one step: a drop onto bare ground puts a fresh
    pure mote in the well wanting the two channels it lacks, so a two-colour procession can be walked into a
    position no play recovers from. On a well with a supply that is an ordinary loss; on the opening well,
    authored without one for invariant 24's reason, it is a board that can be neither won nor lost (20g's
    state reached by arithmetic). It costs authoring one character, because the deal repeats.
26e. **A well's room to err is a count of drops, never a multiple of par.** Every other mode's budget is
    `par × budgetFactor`, and it works there because a mistake costs a fixed fraction of the board — a glade's
    wrong turn is *free* and a resource mode's wrong move leaves the board as it was. A well's wrong drop is
    permanent **and it makes the board worse**, since the wasted mote now has to be cooked to white like
    everything else, so one mistake is worth about two drops: against `par × 1.60` that gave level two of the
    chapter *two* drops of room, reported as "one wrong fall and it shows out of turns", and raising the
    factor is worse the other way, since 2.60 hands a par-6 well ten wasted drops and the fail state then
    rejects nothing. **The room a mode needs is a count when the cost of a mistake is a count**:
    `FallRules.DefaultSpare` is 5, the same on the second well and the tenth, because the budget is a fail
    line and difficulty is the boards' job. **The star lines did not move and must not** — stars measure how
    well a board was played, the budget only stops a run that has become hopeless — and `CheckStarBands` grows
    a branch rather than an exception, because a check that disagrees with the thing it checks is worse than
    no check.
26d. **Par may be resolved lazily, and this is the mode that needed it.** A well's par is a breadth-first
    search and a chapter body holds ten, so paying for all ten while the map opens is a hitch on the one
    screen that never asks the question. `LevelTuning` takes a `Func<int>` and calls it once; that is the only
    place the class is not strictly immutable, and the memo is safe to race on because the function is a pure
    search over a frozen board. Lazy is not free: `FallValidator` warns above 40,000 positions and **refuses**
    above 120,000, about a quarter of a second of nothing happening on a phone on the way into a level — a
    different question from `FallSolver.NodeBudget`, which has to be large enough to *prove* a hard board.
    <br>**The same curve puts a ceiling on par, which decides where a chapter's ramp can live.** Budburst's
    first chapter is ten groves all at **par 3**, not by preference: cost goes as the flower count to the
    power of par, so a par-4 grove big enough to cascade is refused by the node ceiling, and one small enough
    to prove comes back at twenty flowers with a **one-wave** best tap — a board that validates perfectly with
    the mode taken out of it. So the ramp was spent on what does not multiply the search: board size, how many
    are shut in, `spare`, and whether a careless run still scores three stars. **A mode whose par is found by
    search has a ceiling on par, and the ceiling is lower the more the mode puts on the board.** Lightweave
    joined this later without its code changing, which is the general lesson: its par meant *generating* a
    grove, so par was cheap only while good boards were common, and tightening one chapter's acceptance bar
    from ~1.1% of seeds to 0.3% took its ten groves from tens of milliseconds to **965ms**, all spent while
    the chapter body parsed. **A mode's par can stop being cheap without its code changing.**
28. **Groovekeeper is retired, and what survives is its arithmetic.** It shipped one chapter of ten
    grooves — lay a tile so that *unlike* edges bloom — and was withdrawn by the owner as boring: every
    reading was good and the board played as arithmetic, which is 20l's complaint one step further in. Five
    prototype modes took its slot and four of those were withdrawn in turn (29), Toppleglen and then
    Nova Raid after them (31), and the Iron Quarry stands where Groovekeeper stood. Four of its rules
    outlive it and are stated where they are now used:
    the **room to err is a count, and its fifth unit is the two-star line** (`par + spare` has to clear
    `ceil(par × 1.40)` or the bottom band is stranded — four holds to par seven and collides at eight, and
    nothing but `CheckStarBands` noticed); **a proof that a board is lost never ends a run** and only decides
    whether it would be honest to sell one (28f, kept verbatim below); **copying a rule across from a mode
    that looks similar is how a gate comes to refuse correct content** (a check demanding all three colours
    was written by reflex and errored on two correct grooves); and **a mode whose par is a search has a
    ceiling on par**, lower the more the mode puts on the board (26d).
    <br>**Retired ids that must never be reused:** the mode id **`keeper`**; the level block **`keeper`**
    (refused *by name* by `content.py`, for the duskcap's reason — JsonUtility drops an unknown field without
    a word, so a chapter written for a build that is gone would index and ship as something nobody authored);
    the chapter id `k01_grovekeeper` and its ten level ids (`k01_first_grove`, `k01_the_second_bed`,
    `k01_stonecrop`, `k01_the_boulder`, `k01_four_petals`, `k01_heartwood`, `k01_twin_hearts`,
    `k01_the_prism`, `k01_the_pocket`, `k01_keepers_grove`); the six lesson ids `keeper_bloom`, `keeper_basket`, `keeper_stone`,
    `keeper_compost`, `keeper_heartbed`, `keeper_prism`; `ContinueUnit.Tiles` and `DefeatReason.OutOfTiles`
    / `DefeatReason.Overgrown`, whose **ordinals** reach analytics on every run ever recorded and so are kept
    as members rather than deleted. A real save may still hold a record against one of those level ids, which
    is exactly why `ProgressionStore`'s high-water floors exist (invariant 9): derived XP and credits fall
    when a level leaves the catalog, and the floors are what stop a player noticing.
28f. **The proof that a board is lost never ends a run, and only decides whether it would be honest to sell
    one.** Ending a run on it is the mistake `FallVerdict` shipped and took back: it came back from play as a
    run that ended while the tray still had motes in it, which reads as the game deciding on the player's
    behalf. A player who wants to spend their last three moves on a board that cannot be finished is entitled
    to. So it is asked at the moment the allowance runs out and at no other (`ProtoVerdict.Read`), and it is a
    **certainty** rather than a heuristic, because the answer decides whether money changes hands — it
    under-reports and never over-reports.

29. **Five modes were commissioned at once to be judged by playing them, four were withdrawn, and the
    way that was affordable in both directions was sharing everything except the rules.** Toppleglen,
    Nectarrun, Ribbonfall, Seedfling and Warrenwake replaced Groovekeeper's ten levels with **one chapter of
    one level each**, so the owner met five verbs and said which were worth a chapter. The answer was
    **one**: Toppleglen stood and the other four went — and Toppleglen was itself withdrawn later (31),
    which does not weaken the argument so much as finish it. Every one of them was a *run* rather than a
    demo — a permanent `LevelId`, a searched par, both star lines, a real fail state, a heart, a chest, a
    streak and a star ledger — and between them they cost the save file **no schema version, no merge rule,
    no `firestore.rules` change and no server work** (20a). What they cost is one new `ContinueUnit`, which
    is kept, because every mode built on this shape since is graded in it. **And that is the half worth
    writing down: removing
    four of five modes cost the save file, the wire and the server exactly what adding them did —
    nothing.** A mode built the way 20a demands can be taken back out on the strength of one session of
    play, which is what makes commissioning five at once a sane thing to do rather than a gamble.
    <br>**Retired ids that must never be reused:** the mode ids **`nectar`**, **`ribbon`**, **`fling`** and
    **`warren`**; the level block names **`nectar`**, **`ribbon`**, **`fling`** and **`warren`** (refused
    *by name* by `content.py`'s `RETIRED_BLOCKS`, for the duskcap's reason — JsonUtility drops an unknown
    field without a word, so a chapter written for a build that is gone would index and ship as a glade
    nobody authored); the chapter ids `n01_nectarrun`, `r01_ribbonfall`, `s01_seedfling` and
    `w01_warrenwake` with their four level ids `n01_firstpour`, `r01_firstribbon`, `s01_firstfling` and
    `w01_firstparade`; and the eight lesson ids `nectar_pour`, `nectar_hollow`, `ribbon_draw`,
    `ribbon_sink`, `fling_flick`, `fling_catch`, `warren_cut` and `warren_carry`. All four were **played on
    a device**, so a real save may hold a record or a `tipsSeen` entry against any of them — which is the
    line that separates this from Lightfall's `f03_wickwater` (26h), where the chapter never left the
    working tree and its ids were honestly re-authored. `ProgressionStore`'s high-water floors are what stop
    a player noticing that derived XP and credits fell when four levels left the catalog (invariant 9).
29a. **They share a level *shape*, not a rule, and that distinction is the whole design.** Each authors a
    grid of letters, an optional deal and a slack (`ProtoDto`), so `ProtoGrid`, `ProtoSearch`, `ProtoBudget`,
    `ProtoVerdict`, `ProtoRun`, `ProtoMode`, `ProtoValidator`, `ProtoView` and `ProtoScreen` are each written
    **once** and every mode supplies only its own rules. Five copies of "a run is decided once", "a move is
    charged once" or "is this ladder ordered" would be five places for one of them to stop being true, which
    is what taking `RunScreen` apart was for. A mode that wanted to be drawn some other way could ignore
    every drawing helper and still inherit the latches, which is the dangerous half. **The withdrawal proved
    the split from the other side**: taking four modes out was deleting four board files, four view files,
    four table entries and four sections of the offline mirror, and not one line of the shared spine moved.
29b. **All five were breadth-first searchable because all five were monotone, and that was the entry test.**
    A pull removes a stone, a stopper never goes back, three blooms leave the grove, a pod bursts, a bramble
    is cut — nothing is ever added, so the state graph is a DAG, the run always ends, the board cannot stall
    and the first layer holding a finished board is par (20j, three properties from one). Groovekeeper
    deepened iteratively because its board *grew*; nothing here does, so two orderings that remove the same
    things merge — which is also what makes counting shortest answers cheap. **Note what the test did and
    did not buy**: all five passed it and four were still withdrawn, because monotone says a mode can be
    *graded*, never that it is worth playing. That is 20j and 26h asking different questions, and both have
    to be asked.
29c. **A companion is on every board, their part is fixed by the level, and that is what keeps par honest.**
    They catch a critter that rolls to them — and, on the four modes now gone, drank the first pour that
    reached them, made the bloom standing on them wild, threw a seed back up and carried one critter home.
    **What they may never be is the player's *worn* companion changing what a move does**: par is searched
    per board offline, so an ability that varied with who is worn would vary par, both star lines and the
    fail line per player — and somebody who bought Coral would be playing an easier game than somebody who
    did not. The worn companion supplies the *portrait* and nothing else. Thirty-one companions therefore
    become thirty-one flavours of help across future drops **with no code**, because which one a level hosts
    is content.
29d. **A prototype is refused if a greedy player can finish it, and the one that ships is at nought.**
    `careless` is a warning in `ProtoValidator` rather than a gate, because early in a chapter thoughtlessness
    is supposed to work — but such a board is the *only* board of its mode, so `ProtoLadderTests` pins it: a
    prototype greedy play clears is a prototype nobody has played. `ways` is pinned for the other direction,
    which is the one nothing else sees: a rule change that makes a board **easier** leaves par plausible and
    every gate green (Budburst's wash bug, from the other side).
29e. **The two mirrors disagreed twice, and both disagreements were silent.** *(Both bugs were in modes now
    withdrawn; the rule is not.)* `Tools/verify/proto.py` is the offline copy, and `ProtoLadderTests` holds
    the shipped boards **inline** because every `*VectorTests` in this project reads JSON through
    `JsonUtility` — a native call — and so is reported as "needs the Editor" and skipped on the way past. It
    earned its place immediately. **One:** the Python `Ribbon.clone` rebuilt the companion cell from the
    grid, which cannot work, because that cell was authored two ways (a bare `@`, or a lower-case letter
    meaning "the marker is here and this bloom is standing on it") — so every position after the first lost
    the wild. **Two, and it is Python-only:** `here in 'RGB'` is **True** for the empty string, so every hole
    in the grove read as a bloom. Neither moved par on the shipped board. Both moved `ways`, `nodes` and the
    careless reading, which is exactly the half nobody would have looked at.
29f. **A stopper that has been pulled is a channel, and asking the authored letter instead is a whole mode
    that does not work.** *(Nectarrun is retired; the rule is not.)* Its two ground predicates read the
    letter in the file, so a cell the player had just cleared was still refused as impassable: every pour
    stopped there, the cup behind it never filled, and the board came out unsolvable with every character
    correct. The general shape: **a predicate about the *ground* must not answer a question about the
    *state* standing on it** — whether a stopper is still in the channel is the caller's question, and the
    board had already asked it.
30. **Two modes were commissioned into one slot on one day and withdrawn the same day, and what
    they left is the rules below.** Deep Orbit (aim a salvage drum down a lane of an alien deck) and
    Moonwake (ring a bell and the monsters slide) were both built on 2026-09-06 to be judged by playing
    and both came back as *not enough on the screen* — one verb, one gesture, and nothing a mass-market
    player recognises as a game. Both were played on a device, so their ids are **spent**: the mode ids
    `orbit` and `moonwake`; the level blocks `orbit` and `moonwake` (refused by name in `content.py`);
    the chapter ids `o01_hollowfleet` and `m01_moonwake`; the level ids `o01_firstbreak`,
    `o01_sentryline`, `o01_wardensgate` and `m01_bellbreak`; and the lesson ids `orbit_launch` and
    `orbit_pod`. What was kept is everything that was a *seam* rather than a mode — the story band, the
    cue system, the second-world backdrops, and three rules about a talking, animating board — because a
    seam survives the thing it was built for.
30d. **A level may speak, and every line is a loc key authored in content — the one place in this game a
    key is written down rather than derived.** A level's own name is a function of its id (5a) because
    anything holding a `LevelId` has to name it without reading a body; nothing ever needs to name a line
    of dialogue it has not read. What the derivation buys is protection from typos, and that is bought back
    **more strictly** by `content.py` and `ContentValidation` resolving every authored key against
    `loc/en.json` — which also catches one that is correctly shaped and simply missing. `loc.py` cannot see
    them at all, so if it were not checked there it would be checked nowhere.
    <br>**Cues, not a timeline** (`StoryCue`): a run is played rather than watched, so content says "when a
    monster comes out, this is what Bolt says". Every cue is raised off the turn the board just resolved,
    so the story costs **no state at all** — nothing stored, nothing merged, nothing recomputed (14's
    bargain). Several beats may share a cue and they are handed out in order and then fall silent, because
    a board that frees three monsters and hears one sentence three times is a board whose dialogue stops
    being read. The runtime **drops** a malformed line and refuses nothing; the build gate **errors** on it.
    Losing a sentence is better than losing the board it was written for.
    <br>The band never blocks except once — the opening lines hold the board with the same latch a lesson
    uses, so nobody drags a crystal through a sentence and no heart is owed for a level nobody has touched.
    `StoryCast` is a list rather than a convention because a speaker names a folder of frames, and a
    mistyped one is a portrait that draws as a **white rectangle** (7b). The cue ids and the cast are
    content vocabulary and not save ids, so re-casting them for a new mode cost nothing.
30e. **A backdrop belongs to a mode's *world* as well as to a level's place, and that is one axis added
    to 7c rather than an exception to it.** Every sky in this game is one cloud painting at forty colours,
    which is exactly right while every mode is set in the same forest and exactly wrong the first time one
    is not. `mapart.sky` takes the mode, `MODE_WORLD` says which world it is set in, and the arithmetic is
    unchanged: same ordinal, same level index, same forty. Hollowmarch is set in the **village** the raid is
    stripping — forty isometric night villages composed out of eight of the licensed tile packs by
    `Tools/make_village_art.py`, deterministic from the index, graded through the same `vivid` and the
    same hue ladder as the skies, so a second chapter still costs no art. **The map is deliberately not
    part of it** — a mode is told apart on the map by its perch and by nothing else (7c).
30g. **"No dialogue" and "the board ignores every tap" were one line, and the line was
    `SetActive(false)` on the object the component lives on.** `StoryBubble` hid itself by deactivating
    its own node, so the next `Speak` called `StartCoroutine` on a disabled behaviour — which Unity
    refuses silently, returning null. The band never appeared, and the `done` callback the screen was
    waiting on to hand the board back **never fired**, so the board sat `Locked` for the life of the
    screen. Three rules, and only the first is about this class. **A `MonoBehaviour` that hides itself
    must not disable the object it needs to be alive on** — alpha for the look, `blocksRaycasts` for the
    input, and the object stays awake. **Anything that hands out a callback must be unable to strand its
    caller**: `Speak` fires `done` immediately when it could not start. And **anything that latches a board
    must bound its own release** — the opening scene holds the latch only across the speaking, and even
    that is capped (`MostToSay`).
30h. **A modal sets `Time.timeScale` to nought, so a screen coroutine that waits in *scaled* seconds
    never finishes while a lesson is up.** The opening scene waited `new WaitForSeconds(...)` and the
    mode's lesson arrives 0.15s after the board — inside that very wait. `WaitForSecondsRealtime`
    throughout, and the scene waits for `Teaching.Teaching` to clear before it takes the latch at all.
    **Before waiting on a clock in a screen coroutine, ask what a modal does to it.**
30i. **A recorded turn says where a piece was *immediately before* an event, and the cells between two
    events carry no beat at all.** Deep Orbit's view began each hop at the beat's `From`, which
    teleported the drum across every empty cell it should have flown and then animated the last one.
    **Nothing else in this project can see that class of fault**: par, `ways`, `careless`, both validators
    and every content gate read the *model*, and the model was right the whole time. Hollowmarch
    inherited the rule rather than the bug: `MarchDeedRecord.At` is a **track slot** and never a
    position in the line, because the line shifts under its own indices as pods leave it — and the
    line's shape after every wave is carried as a **snapshot** (`MarchFrame`) rather than
    reconstructed, so the view interpolates between two states it was handed rather than doing
    arithmetic nothing can check.
31. **Nova Raid and Toppleglen are retired, and what survives is the reason both were
    withdrawn.** A match-three on a small lattice and a heap of stone that falls: every reading
    on both was good, every gate green, and the owner's verdict on the pair was that neither was
    the *fresh* thing the slot was commissioned for. That is 20j and 26h asking their questions
    a fourth and fifth time and getting the same answer — a mode can be monotone, searchable,
    correctly par'd, spectacular in the file and still not be a game anybody wants to play, and
    the only instrument that says so is somebody playing it. **Retired ids that must never be
    reused:** the mode ids **`nova`** and **`topple`**; the level blocks **`nova`** and
    **`topple`** (refused by name in `content.py`'s `RETIRED_BLOCKS`); the chapter ids
    `v01_harvester` with `v01_firstlight`, `v01_ironwatch` and `v01_thehold`, and
    `t01_toppleglen` with `t01_firstfall`; and the lesson ids `nova_swap`, `nova_armour`,
    `topple_roll` and `topple_burrow`. Both were played on a device, so a save may hold a record
    or a `tipsSeen` entry against any of them. What was **kept** is everything that was a *seam*
    rather than a mode — the prototype level shape, the story band and its cast, the village
    world of backdrops, and the whole cast and explosion art, all of which the Iron Quarry
    inherited for the price of one folder rename.
32. **The Iron Quarry is retired, and the reason is the one this file keeps writing down.** It
    shipped three levels — cut a charge loose, it slides until something stops it and goes off
    there — and was withdrawn by the owner *without being played*, on the strength of what it
    looked like: a floor of cut stone with one hot thing crossing it, three flicks deep, with
    nothing on it the player **made** and nothing that kept paying out after they stopped. That
    is 20j and 26h asking their questions a sixth time and getting the same answer, and it is
    also the first time the answer arrived before a device did. **Retired ids that must never be
    reused:** the mode id **`quarry`**; the level block **`quarry`** (refused by name in
    `content.py`'s `RETIRED_BLOCKS`); the chapter id `q01_ironquarry` with its three level ids
    `q01_cutloose`, `q01_wardenrow` and `q01_thedeepcut`; and the lesson ids `quarry_flick` and
    `quarry_armour`. Four of its rules outlive it and are stated where they are now used: a goal
    must be the most legible thing on the board and **no numeric gate can tell you it is not**
    (32b, kept verbatim below); a preview may show **geometry and never outcome** (32c); a board
    is **dealt by seed into a designed template** and kept for what it measured (32d); and a
    recorded turn says where a piece was *immediately before* an event (30i). What was **kept**
    is everything that was a seam rather than a mode — the prototype level shape, the story band
    and its cast, the village world of backdrops, and the whole cast and explosion art, all of
    which Hollowmarch inherited for the price of one folder rename. That is the fourth time that
    art has been inherited and the third time the rename was the whole cost.
32b. **A goal has to be the most legible thing on the board, and no numeric gate can tell you it
    is not.** The quarry's cage shipped its first cut as a ruin pack's wooden posts stood over
    the monster, and every gate was green: par, `ways`, `careless`, `chain`, both validators, the
    content check and the art audit. Rendered at the size a phone draws it, it was three brown
    logs with a sliver of colour behind them — *firewood*, in a mode whose entire goal was the
    thing behind those logs. **Approximating a goal out of scenery is how a goal comes to look
    like dressing**, so it is composed rather than cut. Hollowmarch took the lesson the first
    time rather than the second: its pods, its cage, its road and its gate are all
    `Tools/make_march_art.py`, where their size, their contrast against the plate and their
    silhouettes are decisions rather than accidents — and the licensed art is left to do the work
    it is good at, which is a cast that moves and explosions.
32c. **What a preview may show is geometry, and never outcome.** A core has to be aimed, so while
    the finger is down the board lights the road between the launcher and where the core will
    wedge, and lights the run it will wedge into — facts the player can already read off the
    board, drawn faster. What is deliberately *not* drawn is what the blast will catch, because
    that is the thing the player is working out; a mode whose chains are printed on the board
    before they happen is Budburst's withdrawn halo all over again (20l).
32d. **Every board is dealt by seed into a designed template and kept for what it asked.** What is
    *designed* is the thing a player reads — where the road winds, how far it is from the gate,
    where the launcher stands. What is *dealt* is the arrangement nobody can eyeball, and
    `Tools/march_sweep.py` reports par, `ways`, `careless`, `nodes`, `chained`, `forged`, `lanced`
    and `menace` for every deal worth keeping. The three rungs are seed 170 of `first`, 22 of
    `road` and 1 of `gate`; the seeds are recorded in `Tools/chapters/m01_hollowmarch.py` so a
    board can be re-derived rather than only re-typed.

33. **Hollowmarch is a wedge, and what the wedge is really for is the gap closing behind it.**
    The raiders are walking a line of pods along a haul-road to a portal and some of those pods
    are carrying caged critters. Fire a core into the line and it wedges in beside its own
    colour; three alike go off; the line **slides shut** — and if the closure brings three more
    together, that goes off too, and again, each wave louder and a semitone higher. That cascade
    is the engine the whole casual genre runs on and it is the first time this game has had one,
    which is the point: nine modes have been built here against a rule nobody had met before, and
    two of them survived. **A mode may be built on a loop a hundred million people already know,
    so long as the twist is real** — and the twist is that the line is *walking*.
    <br>It cost the save file no schema version, no merge rule, no `firestore.rules` change and no
    server work (20a), and it is the tenth mode the prototype level shape has carried. What it
    added to that shape is one field: `ProtoDto.cores`, the **deal**, which the block had
    described itself as carrying since it was written and which no mode built on it had ever
    wanted. That is a seam paying off rather than being stretched.
33a. **The allowance is drawn on the board, and that is the whole reason the march exists.** Every
    core spent is a step the raiders take toward the gate, so how much time is left is something
    a player *reads off the line* rather than out of a corner — and the number in the corner is
    the same number, so the two can never disagree. It is the first fail state in this game that
    is a picture, and the continue buys the one thing that picture is about.
33b. **A plain pod goes through the gate and is gone; a pod carrying a critter jams the line.** The
    first half is a real cost with no fail state attached — the line bleeds the very material a
    core needs to match, so a player who ignores the front of it finds their options narrowing.
    The second half is a **rule**, and the alternative was tried first: letting a cage through the
    gate makes a board that can be **neither won nor lost**, which is the one state invariant 20g
    says a mode may never ship — and it arrives here through the front door, because the opening
    board of every mode is authored without an allowance at all (24) and would therefore march
    for ever. The jam is also what lets `MarchBoard.Stranded` honestly answer **false**: every
    goal a board opened with is still standing on it however long the run goes on, so more cores
    always help, which is exactly what a deficit of nought says (28f).
33c. **The monotone quantity is the magazine, not the pods.** A dumped core *adds* to the line, so
    the board does not only ever shrink — which would have failed 20j's second test read
    carelessly. What every legal shot does is advance the magazine by exactly one, so depth **is**
    cores spent, the state graph is layered by construction, a run always ends and
    `ProtoSearch` finds par by breadth-first walk with nothing to prove about termination.
    **Before admitting a mode on the monotone test, ask which quantity is monotone** — it is not
    always the thing on the board.
33d. **The chain has to clear the same threshold again on every wave, and that is what stops it
    being a solvent** (20j's third test). A closure that made three alike goes off; one that made
    two does not, so a chain dies wherever the line is not already nearly right. And what the
    chain is *worth* is bounded the same way: a shot that destroys five pods forges a **Spark**
    into the next core, which is the only thing in this mode the player makes (20m) — so the
    mode's best move and its reward are the same move, and a big chain is something to aim at
    rather than something that happens.
33e. **A Spark cuts plating, which is the only reason it is not a bigger core.** A colour match
    can never take a warden in one shot; a lance takes the run it is aimed at, the group either
    side of it, and any plating in the way. That is 26g's test asked of the thing the *player*
    makes rather than of the thing the author placed — a mirror that only ever did what a lens
    did was withdrawn for competing on degree rather than on kind, and the same question has to
    be asked of a special before it is animated.
33f. **The reading that judges the mechanics is taken over the shortest answers, never over the
    opening move.** "Can the first core set off a chain" collapses exactly when a board is good —
    a line whose very first shot sets off a four-wave cascade is a line that is *over* in three
    shots — so tuning against it selects for short boards and quietly rejects the long ones.
    `MarchReading.Chained`, `.Forged` and `.Lanced` walk **every** shortest solution and report
    what they do, which is Budburst's `fired` and the whorl's `kindled` (20m, 26h) asked here.
    They are carried along the frontier of the search that already ran, so they cost one triple of
    integers per state and never more than the search itself.
33g. **A haul-road is derived from what is drawn, and it costs exactly one rule.** A level draws a
    winding rail through the grid and `MarchLayout` reads the order off it, which is the only
    shape where what is drawn and what is played cannot come apart — invariant 4a's argument
    about the manifest, applied to a board. The rule is that every road cell has two road
    neighbours except the two ends, which have one; a road that forks is then refused **by cell**
    rather than read some arbitrary way, which is `ProtoGrid`'s rule about naming the row and the
    column.
33h. **The road is drawn full-bleed, and a render is what said so.** The first cut inset each road
    tile and rounded its corners, which is right for one tile and wrong for forty: laid end to end
    they read as a row of separate *sockets* with dark gaps between them, so the haul-road — the
    one thing on the board that says where the convoy is going — disappeared into the ground.
    Every numeric gate was green, because every numeric gate reads the model. The same render
    caught the magazine hanging off the plate on three roads out of four, and forty per cent of
    the finale being empty rail behind the convoy. **`Tools/render_march.py` is the eye, and it
    has now earned its place three times in one session.**
33i. **Three shipped rungs is a test, not a chapter.** What is owed is somebody playing it — see
    the owed list. The questions in order: does the **wedge** read (a core does not land where the
    finger went, it travels to the run it matches, which is why every run of your colour lights up
    while the finger is down); is the **chain** something players work out how to *arrange* rather
    than something they watch; and does the **Spark** read as a different piece rather than as a
    bigger core. If the third fails the fix is more boards where a warden stands behind a run
    worth five, not a longer tip.

34. **Emberforge is the genre's own verb with one thing taken away, and what it takes away is the
    refill.** Swap two neighbours so three alike line up and they do not clear — they **fuse**, into
    one **ember** standing on the cell the finger ended on. Tap it and a **cross** of light sweeps its
    whole row and column; push two together instead and they combine into a **star** that takes the
    diagonals with them; and a beam that crosses an ember sets that off too, which is the chain. That
    ladder is Royal Match's rocket and its combinations, and 33's argument applies unchanged: a mode
    may be built on a loop a hundred million people already know so long as the twist is real. **The
    twist is that nothing ever falls in from above.** Every ember is three jewels that are not coming
    back and the beams destroy the wall they cross, so a board is worth about three explosions and
    *where* they are spent is the whole game — the player is not choosing which match to take, they
    are choosing what to spend the wall on. It is the eleventh mode the prototype level shape has
    carried and it cost the save file no schema version, no merge rule, no `firestore.rules` change
    and no server work (20a). It authors the shared block and leaves `cores` **empty**, and the
    reader refuses a wall that deals anything: the wall is everything the level hands over.
34a. **The monotone quantity is the wall, and that is what buys the search.** A fuse consumes at least
    three pieces and puts back one, a tap spends that one, a merge spends two, and a beam only ever
    removes — gravity slides shards down a column and nothing is ever added anywhere. So the state
    graph is a DAG, a run always ends, the board cannot stall for ever, and `ProtoSearch` finds par by
    breadth-first walk with nothing to prove about termination (20j's second test). **A refill is the
    one thing this mode may never have**: it would make the wall inexhaustible, the future unfixed and
    par unsearchable, which is 26 restated — and every consequence of that (no star line, no
    allowance, no fail state) follows immediately.
34b. **The chain runs through the player's own work and stops the instant it reaches wall that is
    not.** A beam sets off an *ember* and passes straight through a plain shard, so the threshold a
    cascade has to clear again on every beat is "did somebody build one here" (20j's third test). That
    is also 20m: the payoff is a thing they made, never a thing the author placed — which is why
    `EmberValidator` warns above **two** dealt embers and `ProtoLadderTests` refuses even one on a
    shipped wall. The chapter deals none at all.
34c. **A finite wall has two fail states and only one of them is the meter, so the mode needed a
    reading no other one did.** Everywhere else a run ends when the allowance runs out; here the wall
    itself can be dead while the readout still says four moves left, which is the game deciding on the
    player's behalf. `EmberReading.Life` plays the most extravagant possible player — biggest gain
    every time — and counts how many moves the wall survives, and a board where that is under the
    allowance is warned about and was not shipped. It is the mirror of `careless`: that one asks
    whether thoughtlessness *wins*, this one asks how long the wall survives it.
34d. **Both directions of a fuse are one move, and it is provable — which is why getting it wrong was
    invisible.** A swap exchanges two *different* characters (two alike are refused as a no-op) and a
    fused group is a run of one character, so the group can hold at most one of the two swapped cells
    and settles on the same one whichever way the finger went. The first cut carried the swap's own
    cells into every beat of the **cascade**, so a group four beats downstream could still tell them
    apart: 39 of 2,398 pairs differed, `EmberBoard.Moves` therefore had to offer both, and the search
    added the paths of two move indices reaching one state — **`ways` double-counted at every depth,
    on every board, in the one reading invariant 5d is about**. Par never moved, which is why nothing
    else could see it. The rule now is that the swap's cells reach the first fuse pass and no further,
    everything a cascade makes settles on its own middle, and
    `ProtoLadderTests.NoFuseOnAShippedWallCanTellItsTwoDirectionsApart` is the guard on the
    optimisation. **Before pinning a difficulty reading, ask whether two moves can reach one state.**
34e. **A goal has to win a legibility fight against four jewels, and only a render can say whether it
    does.** 32b again, and it cost the same two rounds: the cage was first composed out of the pack's
    own stone frame with two chains and a padlock over it, and at the size a phone draws it that was a
    tiny lock over a mess — in a mode whose entire point is the thing behind those bars. It is now
    **drawn**: four thick iron uprights, a rail top and bottom, its own warm light behind so the
    critter reads through it, and the lock on the join. The ember needed the same treatment for the
    opposite reason — the pack's bomb is very nearly black, and the one object on the board that is
    *tapped* rather than dragged was the dimmest thing on it. `Tools/render_ember.py` caught both and
    every numeric gate was green through both.
34f. **The four shards differ in silhouette as well as in hue** — a heart, a cabochon, a rhombus and
    an emerald cut. The mode's whole verb is "are these three the same", so a player who cannot
    separate red from green has to be able to separate a heart from a circle; a palette alone would
    make it a different game for them rather than a harder one. CRAFT.md's rule about the board's
    vocabulary, asked of a mode where it decides every move.

35. **Kindlewake is retired, and the reason it went is the reason it should never have been
    built the way it was.** It shipped ten hollows - join two embers of a colour and a strand of
    light burns between them for good - and the owner's verdict after playing it was that the
    gameplay was not what had been asked for and the animations were bad. That is one fault and
    not two: the commission was *the glade's question with gems instead of conduits*, and what was
    delivered was a **tap-tap pairing puzzle** whose payoff was an abstract crossing of two bars.
    Nothing about it looked like moving jewels, so nothing about its animation could. **Read a
    commission for its *verb* before its goal** - "use gems instead of conduits" names the thing
    the finger does, and a mode that keeps the goal and invents a different verb has answered a
    question nobody asked. Prismvale took its slot with the same brief read that way.
    <br>**Retired ids that must never be reused:** the mode id **`kindle`**; the level block
    **`kindle`** (refused by name in `content.py`'s `RETIRED_BLOCKS`); the chapter id
    `k01_kindlewake` with its ten level ids (`k01_firstlight`, `k01_stillwood`, `k01_crossways`,
    `k01_stonerow`, `k01_thechoir`, `k01_dimhollow`, `k01_threefold`, `k01_lanternweft`,
    `k01_deepwake`, `k01_kindleheart`); and the lesson ids `kindle_join` and `kindle_cross`. It
    was played on a device, so a save may hold a record or a `tipsSeen` entry against any of them.
    <br>Three of its rules outlive it and are stated where they are now used: **a pruning rule
    that is sound for the search can still delete the mode's fail state** (35b); **`life` is the
    longest play and not the greedy one**, which is a reading only a mode whose material runs out
    needs at all (35c); and **a mode set in the grove ships silent**, because the story band's
    cast are raiders and borrowing them would be two stories wearing one set of ids.
35b. **A pruning rule that is sound for the search can still delete the mode's fail state.**
    Kindlewake first refused any move that did not reach a *goal* - provably safe, because such a
    move can never be in a shortest answer. It is still wrong for a reason no solver can see:
    under it no move spends anything for nothing, so the allowance can never bind, the longest
    play equals par on every board, and the meter counts down to an ending that cannot happen. **A
    no-op rule must refuse only what genuinely changes nothing**, never what merely fails to help.
35c. **`life` is the longest play, not the greedy one - and a mode where nothing is consumed
    needs no `life` at all.** A greedy walk answers "how long until this is over", which equals
    par on every board greed wins; what is wanted is the deepest layer any play reaches, capped at
    the allowance. The reading exists so a *finite* board cannot be dead while the meter still says
    three moves left. Prismvale has none, deliberately: a gem is moved and never spent, so there is
    always a legal move and the allowance is the only way to lose - and a check that could only
    ever answer yes is not a check.

36. **Prismvale is the classic glade's question asked with the jewel board's own verb, and the
    verb is the whole point.** A field of coloured gems with lanterns standing in it and critters
    asleep among them; **drag a gem onto its neighbour and the two change places**. A lantern feeds
    the gems of *its own colour* that are touching it, that colour runs on through every matching
    gem beside them, and a critter standing against that vein wakes. It is the twelfth mode the
    prototype level shape has carried and it cost the save file **no schema version, no merge rule,
    no `firestore.rules` change and no server work** (20a). It authors the shared block and leaves
    `cores` **empty**, and the reader refuses a board that deals anything: the field is everything
    the level hands over.
36a. **Nothing is ever removed, and that decides everything else.** Gems do not burst, do not fall
    and are never spent, so every move is a *rearrangement* - which is what makes a vein something
    to build rather than something to buy, and what makes it **breakable**. Light is not stored: it
    is read off the arrangement, so a gem pulled out of a working vein takes the light with it and
    a careless swap on one side of the board can put out the line on the other. That is the only
    thing a wrong move here costs, and it is why the board's key is its **cells alone** - a key
    carrying the light would be carrying a derived value that can disagree with itself.
36b. **It does not pass 20j's second test, and the honest statement is narrower.** A swap is
    reversible, so nothing here "only goes one way": swap two gems back and forth for ever and the
    board is where it started. What the search actually needs is weaker and is true - the **goal**
    count is monotone (a woken critter never sleeps), the arrangements are finite so the visited
    set closes the walk, and a run can never stall because two touching gems of different colours
    are always a legal move. **What that costs is a real ceiling on par**: cost goes as the swap
    count to the power of par and a board carries thirty to fifty swaps, so par 3 proves in ~200
    positions, par 4 in ~1,700 and par 5 is over the budget a level may cost on a phone (26d). The
    two shipped rungs are par 3 and par 4, and that is arithmetic rather than taste.
36c. **The fail state is the meter and nothing else, which no other mode on this shape can say.**
    Every one of them has two endings because its material runs out - a well runs dry, a wall runs
    out of shards. Nothing here is consumed, so `AnyMove` is true until the last critter wakes,
    `Stranded` is a fact about the **layout** rather than about the run, and a lost run may always
    honestly be sold a continue (28f). `Stranded` is certain and never clever: a critter with no
    gem beside it, or one whose run of gem cells no lantern touches, can never be woken however the
    colours are arranged - and *which cells hold gems never changes*, which is the invariant that
    whole check rests on. Contention (are there **enough** gems of the right colour, do two critters
    want the same ones) is the search's job, and the search answers "unsolvable" and points at
    nothing, so the layout check runs first and names the cell.
36d. **A critter wants light and not a colour, and that is a decision.** The colour already decides
    everything through the *lantern*: which gems are worth moving, which lantern is worth using, and
    which of two routes is affordable. Giving a critter a colour of its own would ask the same
    question twice and put a fourth idea on a board meant to be read at a glance. A later chapter
    that wants blends has the whole letter space free for them.
36e. **The reading that judges a board is `used`, and the one that catches the silent fault is
    `dealt`.** `used` counts the distinct lantern colours a *shortest* answer really wakes a critter
    with - a field standing three lanterns whose answer only ever uses one is a field with two
    decorative lanterns on it, and colour decided nothing (5d). It is read over **every** shortest
    solution rather than over the opening move, which is 26h's `kindled` and 20m's `fired` asked
    here. `dealt` is invariant 5g counted: a board handed over with most of its veins already
    running is one somebody else half finished, and it is still solvable, still correctly par'd and
    still fully validated - so nothing else would ever notice. A *little* is the opposite of a
    fault and is how the mode teaches itself without a sentence; both shipped boards deal two or
    three lit gems out of twenty-five.
36f. **`careless` is not beaten at par 3, and that is a fact about the mode rather than about the
    boards.** At par three with three critters each one takes exactly one swap, so a player taking
    the biggest thing on offer every time takes those three - measured over 1,200 dealt boards, and
    not one was beaten. The second rung is par 4 for exactly that reason and greed loses on it.
    **Before tuning a board against `careless`, check whether par exceeds the goal count**; below
    that the reading cannot say anything.
36g. **A lantern that reads as a gem is the one confusion this board cannot afford**, and the first
    cut made it. A gem moves and a lantern never does, so the two must not share a silhouette - and
    the first lantern was a hooped glass drum, which draws at cell size as a **crosshair**: an
    unmistakable "aim here" on the one object nothing may be aimed at. It is a radiating star now,
    which cannot be read as a faceted stone. That is 32b for the fifth time (the Iron Quarry's cage,
    Hollowmarch's road, Emberforge's cage, Kindlewake's husk, this one), and it was caught by
    `Tools/render_prism.py` and by nothing else - every numeric gate was green through it, because
    no gate in this project opens a PNG.
36h. **The vein is a permanent highlight rather than an effect, and its arrival is a *run*.** Every
    pair of touching lit cells carries a bar that stays for as long as both are lit, so the player
    can always read what is connected to what and a broken vein is visibly the light going out
    rather than nothing happening. When a swap lands, `PrismFlare` names every newly lit cell **in
    flood order out of its lantern**, and the view lights them one at a time on a rising note - so
    the payoff is drawn as something travelling rather than as a set of cells switching on
    (invariant 20m, and 30i: the view replays a log and never reconstructs one).
36i. **Two levels is a test, not a chapter.** What is owed is somebody playing it - see the owed
    list. The questions in order: does **drag-to-swap** read as the verb; does the player work out
    that a lantern feeds only **its own colour**; and does a vein going **out** when it is broken
    read as their own mistake rather than as the game taking something away.

37. **Thornwatch is the first mode here that runs on a clock, and everything it costs and
    everything it does not is worth being exact about.** The raiders come down a hill at the
    grove's ward line; match gems and the colour you matched fuels the **ward** of that colour,
    which looses bolts until its fuel fades; a bolt is worth double against a raider of its own
    colour; let them through and they break the wards, and the run ends when the last one falls.
    Two proven loops bolted together with one thing changed, which is what 33 and 34 established a
    mode here may be built on - and the change is that **nothing on the jewel board is a goal**.
    The goals are on the hill, so a match is never worth anything by itself and is only ever worth
    the *colour* it was. It is the thirteenth mode the level shape has carried, and it cost the
    save file **no schema version, no merge rule, no `firestore.rules` change and no server work**
    (20a).
37a. **A mode may run on a clock, and what that costs is exactly one thing: par stops being a
    proof.** Every other mode here is a DAG whose depth is moves spent, so par is the first layer
    of a breadth-first walk that wins and both star lines fall out of it (20j). Raiders that walk
    while nobody is touching the board have no such graph, and a field that refills has no fixed
    future (26's argument, met head on). So `SiegeTuning.Par` is **arithmetic over what the level
    sends** - the hill's total health over the most one match could ever be worth - and
    `SiegeRules.Opening()` answers **null** rather than a position pretending to be searchable.
    Note what did *not* change: the graded count is still something the player spends (matches),
    the star lines are still the same 1.20 / 1.40 multiples, the fail state is real, and the level
    is an ordinary level with a permanent id.
    <br>**And par here is a calibrated estimate rather than a floor, which is the second thing a
    clock costs and the one that was got wrong first.** It shipped as `health / (3 gems x damage)`
    on the argument that a match clears at least three, so no shorter run could deliver the health.
    That is not a floor, because a match on a full field **cascades** and a cascade's gems are fuel
    too: measured over a played run a match clears about five and a half, so the "floor" sat nearly
    twice above real play and three stars was free. `SiegeTuning.MatchGemsTenths` is the measured
    number, it is pinned by the simulation in 37j, and it is a property of a *four-colour* field -
    a level dealing five would cascade less and want a smaller one.
37b. **The fail state is the ward line, so there is no move allowance at all** - and the two are
    the same decision rather than two. A budget would be a second fail state, and its readout
    would count down to an ending that never happens, which is 22's fault from the budget's side.
    So `budgetFactor` is -1 on every siege and both gates **error** on one that is not; what
    replaces the meter is the line itself, drawn on the board with a health pip per blow, which
    is Hollowmarch's rule kept (33a) - the number in the corner and the picture on the board are
    the same number, so the two can never disagree. Consequence: `ProtoVerdict` reads a fallen
    line as `Stuck`, so **no continue is ever offered** (28f), because no purchase puts a ward
    back up.
37c. **Fuel used to fade on a clock and does not any more, and what the withdrawal cost is worth
    writing down.** The rule was that a standing ward lost fuel every second whether or not it was
    shooting, so a colour matched early was a colour wasted; the argument for it was 5d asked of a
    resource - a mechanic that rejects no *timing* is decoration. Played, it produced a meter
    draining while nothing was happening, which reads as the game taking something away rather than
    as a reason to hurry, and the owner withdrew it. Fuel now leaves a ward one way only, as a
    bolt.
    <br>**Removing it roughly doubled what a match delivers, and that fell out in three places at
    once** - which is the general lesson, because none of them is where the rule was. Par's
    arithmetic stopped being conservative and became generous (37a); the ward line finished
    *untouched*, so the fail state rejected nothing (5d again, asked of a threat); and the level
    ran 32 seconds. Every one of those was invisible except through the simulation in 37j. What
    fixed them is the hill and the mode's constants - **the boards' job**, not the rule's. What
    still makes the colour of a match a decision is the elemental double, which the board can
    *show* (the bolts visibly go for their own colour) where a clock could only be felt.
37d. **A level authors what is standing there and what is coming, and no numbers at all** (20d).
    A field, the colours it refills from, a ward line and a list of waves written one letter per
    raider - lower case a creeper, upper case a brute - so a wave's shape is visible in the file.
    Everything else is `SiegeTuning`: fuel per gem, capacity, fade, cadence, damage, health,
    march, blows. Two consequences. A retune is one edit and cannot leave two levels disagreeing
    about what a gem does; and because par derives from those constants, **moving one moves every
    star line in the mode at once**, which is correct and is why they are in one place where that
    is obvious.
37e. **A field that refills has to be dealt, and every deal is deterministic.** xorshift32 seeded
    by FNV-1a over the authored field, all 32-bit, for `DailyChestTable`'s reason - two devices
    deal the same board, so a bug reported against a level is a bug somebody else can meet, and no
    level ever authors a seed. And **the field is dealt again rather than allowed to lock**: this
    mode's clock does not stop, so a board with no legal swap is a run the player watches
    themselves lose. `siege.any_swap` warns about a level *authored* into that state, which is the
    only part of it a gate can see.
37f. **A raider says its colour three times and none of them is enough on its own.** The licensed
    cast are painted in four colours of their own that have nothing to do with this board's four,
    so an untinted raider wears a colour the player has to *learn*; a tint alone is a difference
    only some people can see (CRAFT.md's rule about the board's vocabulary); and an aura alone is
    lost the moment two raiders overlap. So a body is coated 62% toward its colour, stands in a
    wash of it, and carries the gem itself over its head. **Before drawing a rule in colour, count
    how many ways it is said.**
37g. **Three bands is a board no gate can look at, and a render moved two of them.** The hill, the
    ward line and the field divide the height, and at the tenth the mode was commissioned with, a
    ward's own furniture - plinth, fuel tube, four health pips - is nearly two cells tall against
    a band of one and a half. The tube fell behind the field's plate, so **the one readout every
    decision in this mode rests on was invisible**, and the field was a column of air either side
    of it. Both were caught by `Tools/render_siege.py` and by nothing else, with every numeric
    gate green - which is the Iron Quarry's cage, Hollowmarch's road, Emberforge's cage and
    Kindlewake's husk for the fifth time (32b, 33h, 34e, 35h).
    <br>**It draws the line the player would actually stand**, four different models side by side
    (`--line`), because the one thing this picture is for that no number can answer is whether four
    *chosen* turrets read as four turrets. It is also what killed the rank plinth: built, baked and
    validated, it sat behind the field's plate where nothing could see it (invariant 42).
    <br>**It then earned its place twice more on the same board.** After the wards were re-cut it
    caught them sitting so low that the field's plate covered their feet and they read as small,
    and it caught pale paving tiles surviving the grass filter and littering the hill. Neither is a
    thing any number could have reported.
    <br>**And the wards themselves are the exception 32b needs and did not have.** They were
    *composed* first - a stone pillar with a crystal in it - on 32b's argument that a goal
    approximated out of scenery comes to look like dressing. Played, the verdict was that they were
    not turrets. Composing buys **legibility** and cannot buy **character**, and a turret is the
    thing a player looks at for a whole run; so they are cut from a pack drawn for exactly this,
    and the legibility is bought a different way - four models for the silhouette, and a **run-time
    tint** from the same `Pal` entry the gems and the raiders use, so a ward, its bullets, its
    muzzle flash and the gems that feed it cannot come to disagree about what red is.
37h. **Only a ward coming down flashes the screen.** A flash on every blow is five a second once a
    wave is at the line, at which point it stops reading as damage taken and starts reading as a
    fault. The general rule is 26f's asked of a *drawing* rather than of a mechanic: before
    animating anything, ask how often it fires.
37j. **A mode with no search needs somebody to play it, so one is written down — and it caught
    the level on its first run.** `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` plays the shipped
    siege with a deliberately ordinary player (a match every 2.4 seconds, always aimed at whatever
    is furthest down the hill, never looking for a bigger one) and asserts the hill is cleared with
    the line standing **and visibly damaged**. Everywhere else that reading comes free — par is the
    depth of the first winning layer, so "can this be finished" is answered on the way to "in how
    few" — and here nothing else could have answered it: as first authored the level **could not be
    held**, the line falling with three raiders left, and every other gate was green. The tuning
    that fixed it is the mode's constants rather than the level, because what was wrong was the
    mode's arithmetic and not what the level sends. The second assertion matters as much as the
    first: a line nothing ever reaches is a fail state that rejects nothing (5d asked of a threat),
    so the level would play as a jewel board with scenery over it.
    <br>**It then caught the whole mode a second time, from the other direction.** Withdrawing the
    fuel fade (37c) doubled what a match delivers; the same test immediately reported the line
    finishing *untouched*, and re-tuning against it is what produced the numbers that ship - ward
    health 10, a blow every 1.9s, a creeper crossing in 15 seconds and a brute in 21, twenty
    raiders and eight of them brutes. **A mode with no search has exactly one instrument, and every
    rule change has to be put back through it.**
37k. **A wave comes on a clock *or* the moment the hill is empty, and it took both to be right.**
    It shipped waiting for the hill to empty, which is safe — a run can never be outpaced — and is
    exactly why it was wrong: a player who was winning met no pressure at all, because the hill
    stopped and waited for them. On a clock alone the pressure is real and a player who is *ahead*
    of it stands watching an empty field, which is the same fault seen from the other side. Both
    together is the rule: the clock never lets up, and the shortcut means being ahead is rewarded
    with the next wave rather than with a wait. **The guard matters** — the shortcut cannot fire
    before the first wave, because the hill is legitimately empty at the start of every run.
    <br>What the clock costs is the old guarantee, so `BetweenWaves` is the number that decides
    whether a level is holdable and it is pinned by 37j's simulation rather than reasoned about —
    the cliff is sharp, two seconds either side of it, which is itself the argument for measuring
    rather than arguing.
37p. **A hue rotation goes *most* of the way, never all of it.** The wards' first bake set every
    pixel to one hue, which is unmistakable and flat: the pack drew orange trim on a blue body and
    a purple dome, and all of it collapsed into a single red shape. Pulling 80% of the way keeps a
    fifth of the original spread, so a red ward is unmistakably red and still has warm and cool
    notes in it — the difference between a thing painted a colour and a thing *made of* one. The
    blend is circular, or a hue two thirds of the way round the wheel takes the long way.
37s. **A move's *effect* may not land before its animation does, and this mode is where that bites.**
    A swap resolves in an instant and its drawing takes the better part of a second - the gems
    trade, they burst, motes carry the colour up to the line. Fuel credited at the instant of the
    swap therefore reached the wards before anything had left the field, and what a player saw was
    **a turret killing a raider before the gems it was paid for had gone off**. Every other mode
    here is immune by construction, because a turn-based board has nothing running while the
    animation plays; a siege's clock does.
    <br>So fuel is **in flight**: `Swap` books it and `Advance` lands it, on the schedule the view
    really draws (`SiegeTuning.SwapFor`, `BeatFor`, `FuelFlight`, and `FuelLands(beat)` over them).
    **The schedule lives in the rules and the view reads it**, which is the wrong way round until
    you ask what happens otherwise: a mote that arrives before or after its own fuel is the same
    bug again, and there is no gate anywhere that could see it. One number, one place.
    <br>It cost about a second of latency on every match, which is most of a raider's life, and the
    level had to be re-tuned around it - the brute march went from 21 seconds to 26. **A timing
    fix is a difficulty change**, and 37j is what said by how much.
37q. **Two sounds a frame apart are a flam, not emphasis.** The countdown's GO! and the first
    wave's arrival landed on the same frame and both rang a bell. The wave gave its up: what
    announces a wave is the banner and the raiders walking on, and the one sound belongs to the
    moment the player is being counted in.
37r. **A mode's defeat is its own piece of news, and `DefeatReason` is where that is said.**
    A siege reaches the same *reading* every prototype board does — no legal move, and no purchase
    that helps — so it took `Stuck` and a player met "NOTHING LEFT TO DO" over a hill still full of
    raiders, which reads as a bug. `WardsLost` is its own ordinal for this enum's usual reason
    (analytics cannot tell two endings apart afterwards) and for one more: a prototype board ran
    out of *board*, which is a level-design reading, and a siege line falls because the player was
    outpaced, which is a tuning one. `ProtoScreen.StuckReason` is the hook, because the sentence is
    at the other end of it.
    <br>**And the panel says it, not the board.** An on-board banner was tried first and was
    withdrawn: at a size that read across a phone it overflowed its own box, and every other mode
    in this game ends with the same modal a beat later. A mode does not need its own way of saying
    it has ended; it needs the shared one to say the right words.
37l. **`Image.color` is a multiply, so a tint can only ever darken — and a run-time tint is
    therefore the wrong way to colour anything the player is meant to find *bright*.** The wards
    were tinted at run time on a good argument: one `Pal` entry feeding the gems, the raiders and
    the line means the three can never drift. Played, they came back "too dim"; lifted toward white
    first they came back **pastel**; and there is no third setting, because the operation cannot
    add light. They now carry a real **hue rotation** baked into the sprite
    (`make_siege_art.hued`), which keeps every highlight the pack drew and simply makes the body
    that colour, and are drawn at white. **A tint is for saying which of several things this is; it
    is not for making something look lit.** The cast keep theirs, because that is exactly the job
    they use it for.
37m. **A ward is never drawn darker than its own colour, and "has fuel" reads as brighter rather
    than "no fuel" as dimmer.** The first cut faded an unfuelled ward to 72% of its coat, which
    means it dimmed on the first frame of every run and stayed dim — reported as "they are bright
    when the match starts and immediately dim down". A light goes *up*.
37n. **A toast grows to fit what it is asked to say, and renders its markup.** It was a fixed
    148-unit box: a mode's one-sentence rule ran to five lines and the rest was drawn outside the
    plate, because a Unity `Text` that overflows is not clipped and nothing anywhere says so. And
    it drew `<b>` as four letters, because `UIKit.Label` turns rich text off — correctly, since a
    keeper's name reaching a label is a string another player wrote. So the flag is a parameter,
    off by default, and on only for the toast, which is always a loc string. **Both faults were in
    shared code and every mode with an emphasised refusal had them.**
37o. **A siege says the word on the board, because its board does not stop.** Every other mode ends
    with a modal a beat after the run is over, and that is enough because their boards visibly halt
    — a glade goes dark, a wall stops coming apart. A hill keeps walking and a field stays full, so
    the half-second before the panel read as nothing having happened. The countdown at the start is
    the same argument from the other end: three, two, one, **once**, so a player gets a moment to
    look at an empty hill before anything is on it — and the clock runs underneath it, so the count
    is telling the truth rather than holding the game up.
37t. **The last wave is a warlord, and what a level authors about it is one letter.** Thornwatch
    ends on a boss: an alien several times the size of anything else on the hill that walks to the
    middle of it, **stops**, and throws spells at the ward line from where nothing can reach it.
    Everything else here is answered by killing it before it arrives; this cannot be outrun, only
    out-damaged, which is what makes the finale a duel rather than a longer wave.
    <br>**Which wave it is in is a rule and not an authoring decision.** `SiegeDto.boss` is one
    colour letter and nothing else; `SiegeLayout` **appends** a one-raider wave for it, so the last
    wave *is* the boss wave and it can neither be typed into the middle of a siege nor left off the
    end of one. That is invariant 4a's argument about the manifest and 33g's about the haul-road
    read across: where a fact can be derived from a shape it can never come apart from it. And
    because it is a real wave, the wave count, the muster, the banner, `RaiderCount` and
    `SiegeTuning.Par` all take it with **no special case at all** — the only question anything asks
    is `BossWave`, and only because a warlord's health and its way of fighting are not a creeper's.
    A boss cost the save file **no schema version, no merge rule, no `firestore.rules` change and no
    server work**: it is one more goal on a level that already had twenty (20a).
    <br>**It throws at the freshest ward standing, and that is what keeps the fight winnable.** A
    warlord that finished off whatever was nearly down would take the line apart one ward at a
    time — and the ward it would reach first is the one whose colour *answers* it, so the mode's own
    answer would be the thing it destroyed. Picking the freshest spreads the damage: no colour is
    ever locked out, and `SiegeBoard.Stranded` stays the certainty invariant 28f needs.
    <br>**The spell is telegraphed, and the tell is a rule rather than a flourish.** `SiegeCast` is
    raised the instant a target is *decided* and the ward's health goes a whole
    `BossTell + BossFlight` later (37s), so a ring closes over the ward that is about to be hit for
    over a second — which is the window a **mending** is worth pouring into it, and the only thing
    a player can do about a warlord other than shoot it. A spell whose caster is destroyed
    mid-wind-up **fizzles**, in the rules and in the view: a ward coming down to something thrown by
    a boss the player had already beaten reads as the game getting the last word.
    <br>**Three numbers moved to make room for it and each one is a fact about the level's shape
    rather than about how hard it should be.** `WardHealth` went 10 → 14, because a line tuned to
    end within seconds of the last wave cannot then carry a leak into a duel — measured, the run was
    lost with two raiders left, the line falling to brutes that used to be the finale. `BossAfter`
    is a longer quiet (28 seconds against 26) before the warlord than before any other wave, so a
    duel is never stacked on a wave still swinging; 37k's shortcut is untouched, so a player who has
    *cleared* the hill still gets him at once, and the extra time is entirely for the player who is
    behind.
    <br>**Played, it came back as *the boss should come in a bit faster*, and paying for that cost
    a fifth of the line.** `BossAfter` 34 → 28 and `BossMarch` 22 → 13 bring the first spell about
    eleven seconds forward and halve the entrance a player watches from 10.1s to 5.9s — and those
    eleven seconds are eleven seconds of warlord overlapping the tail of the last wave, so an
    unhurried player now finishes on 15 of the line's 56 rather than 21. **A pacing change is a
    difficulty change**, which is 37s's lesson about the fuel flight said about a wave instead; the
    honest reading is that the level got *tighter* rather than that the boss got stronger, and
    `BossCastEvery` is the constant to move if it plays too tight — never `BossAfter`, which is the
    thing that was asked for.
    <br>**And `BossHealth` has a ceiling that is arithmetic, which is the half easiest to get
    wrong.** `PerfectMatch` assumes every gem a match clears lands **double** — which a hill wearing
    all four colours nearly allows, because each ward finds its own. A duel cannot: the warlord is
    one colour, so one ward doubles and three do not, and a match delivers about 13.75 against 22.
    Par is still a genuine floor, just a **looser** one, so **health moved from the hill to the
    warlord makes three stars harder without par saying so**: at 180 an unhurried player needs 44
    matches against a three-star line of 44, and at 200 it is 47 against 46 and the top rung is
    gone. `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` is the only instrument that can see any
    of this (37j), and every one of these numbers came out of it.
37u. **A warlord is drawn big, and three of the four things that make it read are *placements*
    no gate can look at.** It is three cells tall against a creeper's one, it stands still, its
    health is a bar pinned **across the top of the board**, and a violet ring closes over the ward
    it has chosen. `Tools/render_siege.py` moved two of those twice: a carried health bar sat
    *outside* the plate's top edge (two widgets that had come loose), and moved below the body it
    landed on the ward line's own bars (two readouts overlapping, which is two readouts nobody can
    read) — and each time the reflex was to shrink the warlord, until it was barely taller than the
    turrets it was meant to be looming over. **A boss bar across the top is the genre's own answer
    and it is what let the boss be a boss.**
    <br>**Its spell is a different *kind* of object rather than a bigger bolt** (33e asked of the
    boss): the wards fire comets and it hurls a slow orb, graded to a violet no ward and no gem
    wears — so nothing about it can be read as a colour rule, which a spell wearing one of the four
    would have been. One set of reels rather than four, which is also what keeps it affordable: the
    elemental double is about bolts landing *on* a raider, so what comes *out* of one says nothing.
    <br>**It wears a walk while it is walking, and shipping it without one is a fault only a
    player could find.** The reasoning that left the walk cycle out was half right — a warlord
    stands still for the rest of the run once it is in place, and a walk looping under something
    that is not moving is exactly what this mode's cast reels are *walks* to avoid — and it missed
    the ten seconds before that, which is the one stretch where it really is crossing ground. The
    verdict was one word: **floating**. `SiegeView.Follow` asks the board which it is doing every
    frame and `Wear` answers it once, because `Flipbook.Attach` restarts a reel — a caller that
    attached the wanted one every frame would draw frame nought for ever, which is the same bug
    arrived at from the other side. **Ask of any object that moves and then stops: does it have a
    reel for each, and does something choose between them?**
    <br>**Three animations of one character need one canvas**, or a boss changes size when it
    casts. They are exported on canvases cropped to their own extent, so trimming each to its own
    box draws the body at two different scales; their first frames are the same pose, so the offset
    between canvases is the difference of their alpha centroids, exact to the pixel (measured 111,
    41 for the attack). The frame is **wide as everything that must stay in it and tall as
    everything full stop**: the attack's thrown fist is allowed to leave the sides, which is what a
    throw looks like, and the walk is not, because a clipped foot is a boss walking on stumps.
    <br>**And it was baked wrong first, in the shape 37k's sliver warns about, from the other
    end.** Framed as a comet — tall, head at `HeadAt`, room reserved for a trail — the Sun orb came
    out 112 x 512 with the whole effect inside the top ninety rows and **eighty per cent of the
    frame empty**, which the view then draws as a violet sliver seven cells long crossing a hill
    four cells deep. Nothing about the framing was wrong for a comet; the thing being framed was
    not one. **Before framing an effect, look at what it actually is.**
37v. **The header counts waves, not wards, and that is invariant 33a read the other way round.**
    That rule says the number in the corner and the picture on the board have to be the same
    number — and the ward line already *is* a picture: four turrets, each carrying a health bar,
    filling the middle band for the whole run. A corner reading of it was a second copy of something
    the player was already looking at. How far through the raid this is existed **nowhere but in a
    banner that fades after a second and a half**, so a player who looked away at the wrong moment
    had no way to find out whether the worst was over. **What a mode owes the corner is the thing
    its board cannot say.** The last wave is drawn gold, because "this is the last one" is what the
    number is really for — and on a siege that ends in a warlord it is also the warning that the
    last one is not like the others. `mode.cap.wards` is a retired loc key.
37w. **A ward can be upgraded, and the whole mechanic is one question asked about the *line*
    instead of about the hill.** A **cog** falls into the field like a gem, never lines up with
    anything, and is destroyed by a run of gems made **beside** it — and the colour of that run
    decides which turret goes up a rank. Five tiers, four ranks above the one a ward stands up in;
    each is ten per cent more damage and ten per cent less fuel a bolt, which **multiply**, so a
    rank-four ward turns one match into 2.33 times what a fresh one does.
    <br>**It passes 26h's test, which is the only reason it exists.** A mirror and a wick were both
    withdrawn from another mode for being the thing before them wearing a different colour; the test
    that survived is *what does the player decide about it, and can they be wrong*. Here they decide
    which colour to spend next to it — and they can be wrong in a way that costs: a cog taken by a
    colour whose ward has fallen, or is already at the top of the ladder, is a cog spent for nothing,
    and the view draws that by having it come apart and go **nowhere**. Which is also why the first
    neighbour in cell order wins a contested cog and it is *stated* rather than emergent: a player
    who wants a particular colour to take one can always arrange for it to be the only one touching
    it, and a `HashSet<int>` walk is not promised to enumerate the same way on two runtimes.
    <br>**Ten per cent is only exact because every damage number in this mode was multiplied by
    ten**, and that cost nothing: par is health over `PerfectMatch` and *both sides* scaled
    together, so every shipped par, both star lines and every utility's charge came out identical
    (a firepot still costs two matches and still kills two creepers). At the old scale
    `ShotDamage * 11 / 10` was two, so four of the five ranks bought nothing — the alternative was a
    float, which is what this project's own hard-won note about `Mathf.CeilToInt(45 * 1.20f)` is
    about.
    <br>**What it costs is that par stops being even the loose floor it was.** A cog level's match
    can deliver more than `PerfectMatch`, so par over-states what a good run needs — which keeps
    three stars reachable, the direction invariant 22 says to err in, and is the reason
    `AnUnhurriedPlayerHoldsThisLine` now plays **every rung of the chapter** rather than one level.
    Modelling ranks inside par was tried on paper and abandoned: it would have to guess how many
    cogs a player takes and where they spend them, and a par built on a guess about play is a par
    nothing can check. **A cog rate above about four per hundred maxes the whole line and the hill
    stops mattering** — measured, at 8% every ward reached tier five inside a minute and the line
    finished untouched on eight rungs of ten; the shipped chapter deals 3–4%.
    <br>Two smaller rules. **A cog is a second alphabet rather than a fifth letter**
    (`SiegeLayout.Cells` against `.Letters`), because every rule that reads a cell is asking either
    *what colour is this* or *what is standing here*, and one alphabet answers the first with a
    thing that has none — `IsGem` is that split said once, and without it three cogs in a row are a
    match. And **the rank lands at the moment the cog is taken** rather than with the fuel, which is
    the one place invariant 37s is deliberately not followed: a rank is a property and not a hit,
    nothing about it is visible until a bolt leaves, and a bolt costs fuel that is still crossing
    the field — so there is no frame on which an effect arrives before its cause, and
    `SiegeWard.Rank` stays a single source of truth for the badge, the damage and the fuel at once.
37x. **An overlord is a warlord in upper case, and that is the whole of what a second boss cost.**
    A wave already says "a bigger one of these" by capitalising a letter, so the finale says it the
    same way: `"boss": "G"` is a greater warlord — nearly twice the health, half again the casting
    rate, and a spell that takes five off a ward rather than three. It stops **further up the
    hill**, which is the compensation and the half a player feels first, because there is more
    ground between it and the line for the wards to work in.
    <br>It cost one enum member, four constants and three art reels. Every table keyed on
    `SiegeKind` answers for it without being asked twice, the wave count, the muster, the banner,
    `RaiderCount` and `SiegeTuning.Par` all take it with no special case, and it cost the save file
    **no schema version, no merge rule, no `firestore.rules` change and no server work** (20a). The
    ordinal is **appended**, because these reach analytics on every run this mode has recorded.
    <br>**Its spell is a sun rather than the two things tried first, and both failures are 37k's.**
    A spiral came out as the sliver that invariant names — a thin ribbon framed square, eighty per
    cent of the frame empty, drawn on the board as a magenta thread — and a spinning disc is drawn
    nearly black by the pack, so a hue rotation toward magenta had nothing to work on and it baked
    as a dot. What frames well is what already framed well for the warlord: a round, bright,
    self-lit thing. The two are told apart by **colour** (Bloom against Foxglove, neither of them a
    gem's), by the size the view draws them at, and by the impact — which is where a player is
    looking when it lands. **Bake an effect and look at it beside the one it has to differ from.**
37y. **A readout belongs where the thing it is about is, and the fuel tube was in the one place it
    could not be.** It sat on the turret's chassis, which is where a render had put it — at the time
    that was the only place it did *not* fall behind the field's plate (37g) — and on the chassis it
    reads as part of the machine rather than as a meter. Reported from a device with the answer
    circled: the strip between the plinths and the gems.
    <br>**And the reason it could not simply be moved there is the layer, not the number.** On every
    screen this mode has been drawn at, the foot of a turret is *already behind* the field's plate:
    there is no gap in the ward line's own coordinates, only a band immediately above the plate's
    top edge. So the tubes have a layer of their own (`_meters`), created after the field and before
    the effects, and their x is the turret's while their y comes off the plate. **Before moving a
    widget into a gap, check the gap exists in the layer that would draw it.**
    <br>The rank badge went the other way and stayed on the turret: `SiegeView.Badge` pins the kit's
    own shield to the shoulder, the one corner of a turret nothing else uses — the health bar is
    above, the tube below, and the bolts leave from the middle. It is **always drawn and says one
    before a cog has been spent**, because a badge that only appeared after an upgrade would be a
    reward for already knowing the mechanic; this way the ladder is on the board from the first
    frame. The number is drawn rather than baked, which is five textures a colour saved and one
    place the tier is written down.

37aa. **The wards fire twice as often for half as much, and the whole of that change is one
    identity nobody had written down.** Asked for as *see them shoot more without breaking the
    balance*: a gem is now worth two fuel rather than one and a bolt is worth ten damage rather
    than twenty, so a match delivers exactly what it always did over twice as many bolts. Par,
    both star lines and every utility's charge are **unmoved** — `PerfectMatch` is still 220.
    <br>**What made it dangerous is that `PerfectMatch` read `gems × damage × 2`, which is only
    the same thing while a gem buys exactly one bolt.** That was true for as long as a gem was one
    fuel and a bolt cost one, it was never said out loud, and every par in the mode rested on it.
    Left alone, the formula would have halved what a match delivers and **doubled every par in the
    chapter**, with each number still individually plausible and every gate still green. It counts
    **bolts** now. **Before changing a unit, grep for the arithmetic that assumed two units were
    the same one.**
    <br>**The fuel unit is subdivided rather than the bolt halved, and that is what keeps the rank
    ladder exact.** A cog buys ten per cent less fuel a bolt (`FuelShotTenths`), which at a base of
    ten tenths is 10, 9, 8, 7, 6 — five exact integers. Halving the *bolt* to five tenths makes the
    same ladder 5, 4, 4, 3, 3 after truncation, so **two of the four cogs a player spends would
    have bought nothing**. Everything measured in fuel doubled with the unit (`FuelPerGemTenths`,
    `WardCapacity`, the surge's authored magnitude, which still fills the same fraction of the same
    tube and still costs the same two matches); everything measured per bolt is untouched.
    `ShotDamage` 10 is now the **floor** for the ten per cent step, so anything that halves it
    again has to give the rank ladder a finer unit first.
    <br>**The cadence did *not* move, and that is the point rather than an omission.** Halving
    the bolt and halving the interval puts the same bolts through the same window twice as densely,
    so a ward stops firing exactly when it always did — which undoes the thing the change was asked
    for. It was built that way first, on the argument that bolts a second times damage a bolt is
    the line's output and so the rate *had* to double to keep the balance; the owner's answer was
    that this "made the turrets shoot faster so I cannot see them shooting more", and that is
    correct. **The rate decides how long a ward's fire lasts; the fuel decides how long it goes
    on.** Only the second one was asked for.
    <br>**What the levels then had to absorb is the half of the output the rate would have paid
    for, and it moved three rungs in three different directions.** Half-weight bolts at an
    unchanged cadence is half the *peak* damage and the same *sustained* damage — so attrition got
    **better** (a ward lit twice as long spends less of each burst on overkill) and emergencies got
    **worse** (one big health pool still has to be answered at once). Measured: the duel rung cost
    an unhurried player eight extra matches and wanted more hill; the longest attrition rung became
    so safe that **nothing reached its line at all**, which is invariant 5d and needed two more
    brutes; and the rule-test fixture stopped being holdable — because it was the one siege in the
    project that sent a boss and dealt **no cogs**, a shape nothing shipped, so it now deals them
    like every rung does. `RaiderSpacing` was tried as a single mode-wide answer and abandoned: it
    resonates with the wave clock, so 1.35 → 1.28 loses one rung and → 1.20 loses three. **A dial
    that flips whole levels on a hundredth is not a difficulty dial.**
    <br>Every one of these readings came from `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` and
    from nothing else; the search-free mode has one instrument and every change to its arithmetic
    goes back through it (37j).

37z. **A boss is a way of fighting, and telling two of them apart by a hue is not telling them
    apart at all.** The chapter shipped four boss encounters built from **two** kinds: rungs 5 and 8
    were the same warlord — one reel, one spell, one behaviour — separated by a run-time tint, and
    the finale differed from them in its health, its cadence and where it stopped. Every reading was
    green, every gate was green, and the verdict from a device was one sentence: *the bosses look
    exactly the same and have the same VFX*. It is exactly 26h's complaint about the mirror and the
    wick asked of the thing at the end of a chapter rather than of an object on a board — a boss
    that only does what the boss before it did, harder, is competing on **degree**, and degree is
    not a difference a player experiences as a second fight.
    <br>**So there are four kinds, and what separates them is what each one <em>takes</em>.** A
    **blightcaller** takes a ward's *fire* (`SiegeSpell.Douse`: fuel to nought and five seconds
    dark, no health at all); a **warlord** takes its *health* (`Smite`, the classic duel); a
    **warbringer** shakes the *whole line* (`Rally`: it roars, every standing ward takes a little,
    and the hill charges); an
    **overlord** takes the *rank they earned* (`Sunder`: health **and** a cog off the best turret on
    the line, which is the one thing in this chapter a player earns, 37w). Four different answers
    follow from that and only two of them are a mending: a douse is answered by feeding a different
    colour or by a **surge**, a rally by a **firepot** into the hill *before* it comes, and a
    sunder by having spread the cogs. **Ask of a second boss what it takes, not how much.**
    <br>**A boss holds the middle of the hill, and the one that did not was withdrawn.** The
    warbringer was built to take *ground*: each roar lunged it further down until it arrived and
    swung with its hands, which made it a countdown rather than a duel and gave it the one verb
    none of the others had. Played, it came back as "it takes forever to move down and start doing
    its damage, because it keeps moving downwards" — and that is the shape of the mode rather than
    a tuning miss. **Every fight here is a player answering a thing they cannot reach with the
    board in front of them; a boss that walks spends the fight being somewhere else.** So it stands
    where the warlord stands, and its roar takes a little off *every* ward at once — still a verb
    none of the other three has, because a warlord and an overlord pick one ward and hit it hard,
    so the player chooses what to save, and nothing about a roar can be answered by protecting one
    turret. What went with the lunge: `SiegeRaider.Hold` is readonly again, no boss reaches the
    line, and `EndangersTheLine` is back to "does it take health".
    <br>**Because a roar lands four times, its cadence is what prices it — and the first number
    inverted the chapter's ramp.** Four wards at 2 every 6 seconds is 1.33 health a second off the
    line, *more* than the finale's overlord, and an unhurried player finished rung 8 on 6 of 56:
    the bloodiest line in the chapter, one rung before its climax. At 9 seconds it is 0.89, against
    a warlord's 0.60 and an overlord's 1.25, so rungs 5, 8 and 10 climb.
    `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` is the only thing that can see any of it.
    <br>**A level names its boss by kind** (`"warlord:r"`, `SiegeLayout.BossNames`), which is the
    field the old one-letter form had nowhere to go: case said which of *two* this was, so a third
    could not be authored and a fourth could not be imagined. The retired one-letter form is
    **refused rather than reinterpreted** (5f) — with four verbs, salvaging a boss out of `"r"`
    would be a coin toss between four fights. `SiegeLayout.KindAt` is the one place that answers
    what a wave's token is, so par, the muster and the build gate cannot form second opinions.
    <br>**Two of the four take no health, and that broke a check that had always been right.**
    `ModeValidator.Threatens` answered *true* for any siege that sends a boss, on the sound
    reasoning that a warlord shells the line for as long as it lives. A blightcaller cannot bring a
    ward down however long it stands there, so a level whose only threat were one **could not be
    lost** — invariant 5d asked of a fail state, arriving through a door that had been safe for two
    bosses. `SiegeTuning.EndangersTheLine` is the narrowed question, and a warbringer passes it for
    a different reason from the warlord: it walks all the way in.
    <br>**Every one of the three new numbers was measured, and two of them were wrong first.**
    Nothing about a douse, a rally or a sunder reaches par — a rally adds no health and a sunder
    takes a rank par never modelled — so `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` is again
    the only instrument that can see any of it (37j). It caught the finale being **lost outright**
    (the sunder is a second cost on the same spell: `OverlordCastEvery` 3.4 → 4.0, and the cliff is
    two tenths of a second wide) and the warbringer's rung finishing with one ward of four
    (`Rally` 1.9 → 1.55, `WarbringerAfter` 14 → 17, and its last wave lightened). **A boss's ability
    is a difficulty change nothing offline can price.**
    <br>**And the drawing had to be four things too, which is where the render earned its place
    twice.** Four bodies from four packs at four sizes (a floating eye, an armoured walker, a
    walking slab, a gold overlord), four spells baked from four prefabs in the four `Pal` entries
    that are none of the board's gems (teal, violet, white, magenta), four banners, four tells — a
    ring that closes *anticlockwise* for a douse and on the boss *itself* for a roar. The first cut
    of the blightcaller was 2.6 cells and green: `Tools/render_siege.py` put it beside a creeper and
    it **was** one. And the hex was first baked as a ghostly wisp, which came out of
    `SiegeShotBake` as invariant 37k's sliver for the third time — six candidates were baked and
    looked at before one was kept. **No numeric gate in this project can see any of that.**
    <br>**What it cost the save file, the wire and the server: nothing** (20a). Two `SiegeKind`
    members and a `SiegeSpell` enum, both **appended**, because these ordinals reach analytics on
    every run this mode has recorded. What it did cost is memory, and that bought a rule:
    `LevelMode.ArtFor(chapter)` narrows a mode's art to what a *chapter* actually sends, because
    four bosses are twenty-four flipbooks and every siege level was loading all of them (7b's rule
    stopping one step short of the screen).

37ab. **A rung is fought over a ground of its own, and the thing that decided how is that
    the only top-down terrain here is one tileset.** Ten levels shared one floor and the ask was
    variety. The seventeen isometric terrain packs on this machine cannot supply it and the
    reason is not a preference: their ground is drawn as **diamonds with the side faces baked
    into the pixels**, so a tile cannot be un-skewed into a square — it comes back a rounded
    block lit from a corner nothing else on the board agrees with — and laying them as a field
    and cropping a rectangle out of it only hides the skirts, leaving a diagonal weave under a
    board whose every other element is square. That was built, looked at, and thrown away. **An
    isometric pack is not a top-down pack and no amount of transforming makes it one.**
    <br>So ten places are made out of the mine set's own square slabs, three ways at once: a
    **gradient map** (`toned`) reads each slab's luminance through a two-point ramp, which is
    what a hue rotation cannot do — `hued` turns the wards because the kit *paints* them, and
    rotating the hue of stone sitting at a chroma of two leaves it exactly as grey as it was, so
    the colour has to be supplied; a different **mix** of the thirteen slabs; and a different
    **seed** laying them, so two rungs never share a paving pattern. What that buys is real and
    what it cannot buy should be said out loud: every rung is stone, cut the same way. Nine more
    top-down tilesets would replace `TONES` with nine `zipped` calls and change nothing else.
    <br>**Value is the one thing a second ground may not change**, and a gradient map picks its
    own endpoints so nothing about it keeps a ground where the cast can be seen. Every rung is
    normalised onto the mine floor's *measured* mean and spread — so re-cutting the mine moves
    the other nine — and chroma has a ceiling (`GROUND_CHROMA_CAP`), because at their own
    saturation the moss and the ember read as *brighter* than the rest with their value
    identical, and a saturated floor is the one thing on this board competing with the cast
    walking over it. That is CRAFT.md's plate rule and 37f's, said about the floor. It is also a
    fault this hill has had twice already: its second ground was the grass pack "chosen for
    brightness", and the owner's third call moved it to a mine.
    <br>**Which ground is arithmetic on the level's place in its chapter** (7c), so ten serve
    every siege chapter that ever ships and a second one costs no art. It is `SiegeMode.Ground`
    and `SiegeView.GroundAddress`, **two switches of ten literals**, because `artnames.py` reads
    the literal at a lookup's call site and a key built from an index is ten names nothing
    checks — which buys the one fault worth a fixture: two switches disagreeing about a rung
    load one floor and draw another, and an `Image` with a null sprite is a **white rectangle**
    over the whole hill on one rung, with the address real, registered, audited and even loaded
    by a different level. `SiegeGroundTests` is the comparison, and it was proved by breaking it.

37ac. **"Boring bosses" was a complaint about the drawing and not about the fight, and the
    honest answer to it was to *spend* the window rather than to shorten it.** Reported from a
    device as bosses that "do their attack every 3-4 seconds with 1 simple vfx animation" and
    "I don't want to wait 5 seconds for each animation". The reflex fix is the cadence, and the
    cadence is a **rule**: `BossCastEvery`, `BossTell` and `BossFlight` are the window a
    **mending** is worth pouring into a ward that is about to be hit (37s), so making a boss cast
    faster is making the chapter harder — and every one of those numbers was tuned by
    `AnUnhurriedPlayerHoldsThisLine` and nothing else (37j). So **not one number moved**: the same
    tell, the same flight, the same damage, the same cadence, and the save file, the wire and the
    server cost nothing (20a).
    <br>**What was actually wrong is that a boss did one thing per cast and nothing at all in
    between.** A cast is about a second and a half out of every four to nine, so most of the time
    a player spends looking at one it is standing in an idle loop — which is the half of the
    complaint that is not about the spell. `SiegeView.Ambient` crackles every boss on the hill
    about twice a second, quietly and with one bolt, and the **same drawing louder and faster is
    the wind-up**: five crackles at rising strength bunched toward the release, motes dragged in
    off the hill, and a **tether** flickering between the caster and the ward it has chosen. A
    player learns to read the tell without being told, because they have been watching the quiet
    version since the thing walked on.
    <br>**And every cast is a volley now, told apart by *shape of attack* rather than by
    colour** — which is 37z's rule about four bosses being four fights, carried into the drawing.
    A **blightcaller** chains: its bolt hops through the wards it is *not* aimed at before
    settling on the one it is, which is what a douse is. A **warlord** bombards: three orbs on
    spread arcs and four strikes out of the sky onto one post. A **warbringer** storms: six
    strikes scattered over the whole hill and two bolts thrown flat across it under the rings. An
    **overlord** launches a **pair** that bow hard in opposite directions and converge, with a
    bolt riding down between them. Every arm still lands on `BossFlight` exactly — a stagger
    shortens each arm's flight by whatever holds it back, so it spreads the *departures* and can
    never move the arrival.
    <br>**Procedural rather than baked, and that is a decision.** Every other effect in this mode
    is a reel out of a bought pack (37k), which is right for a thing that always looks the same
    and wrong for lightning: a bolt that is the *same* bolt twice reads as a stamp, and a chain
    has to reach two points the board decides at run time. `SiegeView.Storm` builds polylines of
    `Art.Capsule` and `Art.SoftCapsule` at run time, so there is **no address to register, no
    group, no scope and no frame where a strike is a white rectangle** (7b) — and it works on a
    checkout with no licensed pack in it, which the baked reels cannot say.
    <br>**Four things the render caught and no number could, which is 32b for the seventh time.**
    A bolt drawn as a white filament in a soft halo comes out **white** at the size a phone draws
    it — the glow is thin enough to read as an edge — so a magenta overlord threw the same
    lightning a teal blightcaller did; it takes a **third bar**, the boss's own colour at full
    strength and twice the filament's width, for the colour to survive over bright ground. Bolts
    **left the plate**: `_fx` is sized to the field and carries no mask, so a strike started above
    the hill draws over the status bar — `OnBoard` clamps every endpoint, and the "sky" is the top
    of the hill because there is no sky. A bow of half a cell is **invisible**, so three orbs on
    spread arcs were one orb drawn three times and the overlord's pair was a single sun. And nine
    strikes over a hill read as **noise** rather than as six things being struck.
    <br>**The one fault that is not about looking is `Image` count, and it is the reason a
    delayed bolt is *built* late as well as shown late.** A staggered storm constructed every
    bolt up front is several hundred `Image`s and a canvas rebuild inside one frame, with the
    stagger deciding only when they fade up — a hitch on the loudest beat in the mode.
    `SiegeView.Arc` defers construction past a delay, `ArcSegments` is ten rather than fourteen,
    and a segment is seven tenths of a cell: each one is three images, and a storm multiplies that
    by about sixty before it reaches a frame. **Before staggering a hundred widgets, ask whether
    the stagger delays the work or only the paint.**
    <br>`python Tools/render_siege.py --warlord storm --level <a>,<b>,<c>,<d>` is the picture, and
    `--level` is comma-separated for exactly this: the four boss rungs side by side is the only
    thing that can answer whether they are four attacks or four colours of one.

37ac. **"Dead" and "the model has forgotten it" are two questions, and a view that asks the
    second is right until something kills outside the clock.** `SiegeBoard.Advance` sweeps its
    dead as the *last* thing it does, so for every kill a bolt lands "swept" and "dead" are one
    fact and `SiegeView.Reap` could honestly ask either. A **utility** kills from outside
    `Advance`: nothing has swept, so the raider is still in the list with `Alive` false, and
    `Reap` skipped it. Ordinarily the next frame put it right — and on the *killing blow* the
    next frame never comes, because `Judge` ends the run in the same breath and `Update` stops
    with it. What shipped was a firepot or a storm finishing a hill and leaving every raider it
    had just killed **standing there under the victory panel**. `Reap` asks `Alive`, which is
    true one step earlier on every path, so neither side has to know when the other tidies up;
    `SiegeRuleTests.AUtilitysKillIsDeadAndStillHeldAndWinsTheRunAtOnce` pins all three facts and
    was proved by breaking it. **Before a view keys on a model's bookkeeping, ask whether every
    writer goes through the same door.**
    <br>**And a run may not be *told* it is over until what ended it has been drawn.** The hold
    existed and was a boss's alone (`_felling`), on the reasoning that everything else a run ends
    on is already on the screen when the verdict lands — true of a ward falling, false of a
    raider: a killing blow decides the run in the frame it lands and the death it caused has not
    started. So every death arms it, which needed one thing said out loud that the boss-only
    version could leave unsaid — **the countdown belongs to the clock, not to the won branch**,
    or a hold armed by the first creeper of a run is still standing at full when the last one
    dies. It is a *countdown* rather than a callback because a death is a handful of tweens with
    no single end, and `Update` re-asks every frame, so it can hold the telling and can never
    strand it.
    <br>**A storm needed the same rule twice, in opposite directions.** Its strikes all resolve in
    one instant and are drawn one bolt at a time, so `Reap` — now correctly reaping the dead —
    would clear the whole hill one frame in and leave the rest of the reel falling on empty
    ground, which is *the stagger it exists for* undone by the fix to the bug beside it. The
    storm therefore **claims** its raiders (`_striking`) and gives each up as it strikes it, and
    it arms the hold from the **length of its own reel** rather than letting it fall out of the
    per-bolt holds — arithmetic that is correct today and comes apart the day a strike stops
    being a kill or `StormStep` is retuned. Two smaller ones from the same session: `Follow` skips
    a raider that is not alive, because `Widget` *hatches* a body for anything it cannot find and
    would mint a corpse back for a frame; and `Hurt`/`Bolt` ask `MobOf`, never `Widget`, for the
    same reason said about a raider they are in the middle of killing.

37i. **The chapter is ten rungs now, and what a played run has to answer has grown with it.**
    It shipped as one level to be judged (invariant 29's bargain); the questions that judged it are
    still open and four more are open beside them - see the owed list. In order: does **fuelling a
    colour** read as the verb, or do players hunt for the biggest match; is **par a line a good run
    can get under**, which is the one number in this mode nothing offline can answer; does a
    **cog** read as an upgrade rather than as a blocker to be cleared (37w); and — the question the
    last verdict on this mode was about — do the **four bosses** read as four different fights
    rather than as one boss at four strengths (37z)? Each of the four takes a different thing, so
    the answer is really four questions: is a **douse** understood as "stop feeding that colour"
    rather than as the game breaking a turret; does a **roar** read as a warning to burn the hill
    down; does a **sunder** land as the finale taking the cogs the player spent; and is the
    **warlord** still the clean introduction to all of it (20m's test asked of the finale, and the
    one thing here designed against a picture rather than a number, 37u).
37k. **A bought 3D VFX pack reaches a board here as a *bake*, and every one of the six ways that
    went wrong was silent.** Each ward now fires its own projectile - a fireball, a venom dart, an
    icicle and a lightning bolt, with the muzzle flash and the impact the pack draws for each -
    rendered out of `UniqueProjectilesVol5`'s own prefabs by `SiegeShotBake` and shipped as sprite
    reels under `Art/Fx/Siege`. **Not the prefabs**, because the canvas is `ScreenSpaceOverlay`
    and `Boot.EnsureCamera` gives the only camera `cullingMask = 0`, so a particle system in the
    scene is never drawn at all; the one route that does draw is a stage, a camera and a
    `RenderTexture` of its own, which is `VfxDemoScreen` and which its own note says never to
    couple a mode to. **And not its flat textures either**, which is where Budburst's explosions
    came from: that is right when the picture you want is lying in the pack's `Textures/` folder,
    and wrong here, because what was bought is sixty *motions*. Unity rasterises the motion once,
    offline; what ships is sprites, which is what a board firing twenty-eight bolts a second can
    afford. Four elements rather than four tints, for `WardArt`'s reason one step on - at the size
    a bolt is drawn, silhouette is the only difference that survives - and the hue is graded from
    `Pal` **in the bake**, so a ward, its bolt, its flash, its impact and the gems that feed it
    cannot drift (37f).
    <br>**And the first cut of it was played and reported as bad next to the vendor's own demo,
    which is the fault worth writing down before the six below.** Nothing was wrong with the
    render; what was wrong was the *key*. The coverage a pixel was given was lifted by an exponent
    below one, on the reasonable-sounding argument that a trail at a tenth of an alpha disappears
    into grass — and what that promoted was the near-black **haze** every one of these effects sits
    in, which is invisible in the pack's own picture because additive over black adds nothing.
    Alpha-blended it became a translucent cloud twice the size of the flame. Held beside a straight
    render of the same prefab the difference was not subtle and needed no judgement: a small crisp
    yellow head with sparks, against a blurred red column. **Bake an effect and put it next to an
    ungraded render of itself before believing it.** Two more came out of the same comparison — a
    full **re-hue** turns a fireball's yellow-hot core into flat red (a flame's core is saturated
    yellow, not white, so the white-core rule never fires on it; the grade is now a 38% lean, and
    the ward's colour is carried by the tinted halo under the head), and the frames were **too
    small** at 48 wide for something drawn at 70 points and followed by the eye.
    <br>**The same session's other verdict was that the wards fire too fast**, and that is a rule
    rather than a drawing: `SiegeTuning.FireEvery` was .14, which is seven bolts a second per ward
    and twenty-eight across a lit line, at which rate a bolt is not an event. It is .22 now — and
    the band is narrow, because .26 loses the line outright (invariant 37j's simulation, which is
    where every change to this mode's arithmetic has to go).
    <br>**Played again it came back as three more fine tunes, and two of them are one rule.**
    *Too small to see* and *too fast to see* are the same complaint about a thing that is on screen
    for a tenth of a second, so the flight doubled to .20-.40s — deliberately **longer than the
    cadence**, so a ward has two bolts in the air at once and the line reads as a stream of comets
    rather than one thing at a time. Size is where it got interesting: the view scales a bolt by
    its frame's **width**, so a head worth looking at means a frame about a head wide — and this
    pack's trails are roughly **six times** the head, which makes the sprite longer than the flight
    it has to cross and turns a comet into a static ribbon. Flying it slower in the bake only goes
    so far before the flames pile onto the head. **So the far tail is framed out and dissolved**
    (`TailHeads`, `TailFade`): the end of one of these is faint and thinning anyway, so a ramp over
    the last of it is invisible where a straight edge would be a line drawn across the sky.
    <br>**And the damage numbers are tallied per raider, which is what makes them readable at
    all.** Asked for as *proper World-of-Warcraft floating damage*, and the obvious build — one
    outlined figure per hit — is wrong here for a reason that is about the mode rather than about
    text: a lit line lands about **eighteen hits a second**, and eighteen figures a second is a
    wall nobody can read one number out of, which drawing each of them bigger makes worse. So a hit
    on a raider that is already showing a number *adds to it* — the figure climbs, grows, punches
    again and its float restarts — and what the player watches is one number running up while they
    hold fire on something. A double is gold, punches harder and floats higher, because the
    elemental double is the rule this mode is about and this is the only place it is said in
    figures. **Before drawing a number per event, count the events.**
    <br>**Then two more, and both are the same fault in opposite directions: something loud drawn
    quietly, and something quiet drawn loudly.** The **chain banner** — the one announcement of the
    biggest thing that can happen on this board — sat just above the gems at half the size it is
    now, in a plain label with no outline, in the same band as forty gems. It reads as a caption
    there because the eye is already busy there. It is drawn over the **ward line** now, on the
    empty run of hill nothing else lives on and where the eye is already going to see what the
    turrets are shooting, at roughly a cell tall, on a heat ladder (yellow, gold, ember, rose) that
    grows with depth, over a soft dark aura — because a heavy outline alone is not enough over a
    lit hill. One banner, reused: a second cascade re-punches the first rather than stacking a
    label on it, which is also what makes a long chain read as one thing getting louder.
    <br>And the **floating numbers came back down**: a cell and a half over a second was a long
    time for a figure to be over the hill when the next is 55 milliseconds behind it, so they
    stacked up the screen and stayed there. Half a cell, gone inside two thirds of a second, with
    consecutive ones fanning left and right. **What a floating number owes the player is to be
    legible on arrival and then get out of the way** — the temptation with a readout that is hard
    to see is to make it live longer, and on a board this busy that is the one change that makes
    it worse.
    <br>**Six faults, in the order they were found, every one green on every gate.**
    `ParticleSystem.Simulate` with `restart: false` **continues** from where a system is, and a
    system stopped-and-cleared so its seed could be set is stopped - so the first bake ran to
    completion, wrote twelve reels and reported success, and every one held only the two mesh
    renderers that draw whether or not anything is playing. Its last argument quantises to
    `Time.fixedDeltaTime`, four times coarser than the substep, so a *fixed* step is a bake of
    nothing or of four times too much. `Particle.GetCurrentSize` is a **mesh scale** for a system
    rendering a mesh, and this pack's fireball heads answer 120 - every extent came back as the
    same suspiciously round number in all three directions, so measure `Renderer.bounds` instead.
    A frame **shaped by a constant** pads a comet whose real proportions are eight to one until it
    is a quarter of its own width, and since the view sizes a bolt by its frame's width that came
    straight off the board: a twelve-pixel sliver. A **window** has two ends and neither is the
    effect's lifetime - the fireball's muzzle draws a ring inward before it bursts and the
    lightning's builds for a third of a second, so a fixed window baked the run-up and threw away
    the event. And the grade's **white-core protection has to be capped**, because an icicle and a
    lightning bolt are near-white nearly all over: uncapped, half the pack came out colourless,
    measured as 0.18 median saturation on a bolt fired by a blue turret.
    <br>**What found each of them.** Nothing numeric found any of them. The empty reels, the
    sliver, the run-up and the colourless icicle were all caught by looking - `Siege Projectile
    Contact Sheet` and `Tools/render_siege.py`, which now draws the exchange on the real board
    (32b for the sixth time). **And the render has to show a reel at its loudest**: drawn from a
    fixed frame index it caught two of the four muzzles mid-dip and read as a bake that had
    failed, which is an instrument lying about the thing it exists to judge.
    <br>Two consequences worth stating. The reels are **pooled** (`SiegeView.Puff`), which is
    premature everywhere else in this project and not here - a lit line is twenty-eight
    three-part shots a second. And **this is the one art tool here with no offline gate**: no
    Python script can rasterise a particle system, so `Verify Siege Projectiles` re-bakes and
    compares within a tolerance (two GPUs are not obliged to rasterise a triangle identically),
    and it needs the pack, which is gitignored - the same bargain `make_siege_art.py --check`
    already strikes with the licensed zips.

38. **Budburst, Hollowmarch and Emberforge are deleted; the classic glade and Lightfall are
    *hidden*; Prismvale and Thornwatch are what the game is.** The owner withdrew three modes in
    one decision and held two more back rather than retiring them, and the difference between
    those two words is the whole of this entry. **Deleted** means gone the way every mode before
    them went (28–32, 35): the mode class, the board, the view, the screen, the validator, the
    reading, the chapter bodies, the art, the offline mirrors and the tools, with the ids spent
    and written down. **Hidden** means `"disabled": true` on their manifest entries and nothing
    else — every file, every board and every screen still stands, so putting them back is seven
    booleans and no code. That is what the flag was built for (`CatalogIndexBuilder.Add` skips a
    disabled chapter in silence, and `CatalogIndex` leaves a mode with no chapters off the
    switcher entirely), and it is why hiding costs nothing and deleting costs a session.
    <br>**Deleting three modes at once cost the save file no schema version, no merge rule, no
    `firestore.rules` change and no server work**, which is invariant 20a's bargain collected for
    the sixth time — and the honest other half is that it cost **thirty-three levels' worth of
    derived XP and credits**, which is exactly what `ProgressionStore`'s high-water floors exist
    to stop a player noticing (invariant 9).
    <br>**Hiding costs no code and it does cost a seed, which is the half that was missing.**
    `seed-config.mjs` skips a chapter carrying `"disabled": true`, so the moment anybody re-seeds
    after hiding one, its levels leave `config/progression`'s `levelChapters` and the server
    values a save holding them at **nothing**. That is *correct* — `CatalogIndexBuilder.Add`
    skips a disabled chapter and `ProgressionLedger` values a record the catalog has never heard
    of at nothing, so client and server agree — and it is the direction with teeth: a stale
    published table is the **server over-valuing** hidden levels, which is a disagreement about
    money. What it means in practice is that the flag is not quite free after all. Hiding a
    chapter without seeding leaves the two disagreeing; hiding and seeding drops the server's
    valuation of every record in it, so `submitSpends` will refuse debits an old save could
    otherwise cover and `grove.ts` clamps its bought half harder (19a). **Re-enabling is seven
    booleans and a re-seed**, not seven booleans. Found on 2026-09-08 by the live suite, which
    went red on `c01_shallows` the first time anything was seeded after the modes were hidden.
    <br>**Two things this taught that the five withdrawals before it did not.** *One:* shared
    machinery living inside a mode's file is a mode that cannot be deleted for the price of its
    own files. `CellDrag` — the drag handler Prismvale and Thornwatch both use — was written
    inside `BudView`, so removing Budburst took the verb out from under two live modes, and the
    compiler was the only thing that said so. It has a file of its own now. *Two:* **hiding the
    classic mode breaks anything that reads `GameMode.Default` as "the mode to open".** That
    constant is a **parsing** answer — a chapter with no `mode` field is a glade, for ever — and
    three call sites were using it as a **catalog** answer: the map's fallback, the home screen's
    next-up line and the splash's one preloaded chapter. Each would have opened onto a mode with
    no chapters in it. `CatalogIndex.DefaultMode` is the catalog answer and is what they ask now.
    Before treating a constant as a default, ask which of the two questions it was written to
    answer.
38a. **The front door is `LevelModes`' first entry, and it is Thornwatch.** Index nought of that
    registry is what the switcher offers first *and* what a map with nothing remembered opens on,
    because `CatalogIndex.DefaultMode` is `Modes[0]` and nothing else. Those were briefly two
    answers — the catalog preferred the classic mode where the switcher led with whatever the
    registry led with — and two answers means a map opening on one mode while the control above
    it offers a different one first: a difference nobody could explain and no gate would catch,
    because each half is individually correct. **Which mode leads is decided once, in a written
    list, and read everywhere else.** It is a list rather than a sort for the reason it always
    was: a switcher that reorders itself moves the entry somebody reaches for without looking.
    <br>**A remembered choice still wins**, so moving the front door moves nobody who has already
    chosen — `glimmer_map_mode` is written on every map *arrival*, so in practice it moves only a
    fresh install. And `GameMode.Default` did **not** move and must not: a chapter with no `mode`
    field is a glade for ever.
    <br>**Making that one answer exposed a bug the two answers had been hiding since the switcher
    shipped.** `ModeChoice.Read` could not tell *nothing remembered* from *the classic mode
    remembered*, because it read the stored string through `GameMode.TryParse` — which answers
    **true for an empty string, with the glade**, that being the question it exists to answer.
    Harmless for as long as the glade was also the fallback; the moment the front door moved it
    became a map opening on the classic mode on a device that had never chosen it. An empty
    preference is now answered before anything is parsed. **A parser's forgiving default is not a
    reader's "unset"**, and the third place in one session where a constant answering two
    questions cost something.
    <br>**Retired ids that must never be reused:** the mode ids **`bud`**, **`march`** and
    **`ember`**; the level blocks **`bud`**, **`march`** and **`ember`** (refused by name in
    `content.py`'s `RETIRED_BLOCKS`); the chapter ids `b01_thicket`, `b02_tanglewood`,
    `m01_hollowmarch` and `e01_emberforge` with all thirty-three of their level ids; and the
    lesson ids `bud_chain`, `bud_cocoon`, `bud_satchel`, `bud_graft`, `bud_bolt`, `bud_sun`,
    `march_fire`, `march_spark`, `ember_fuse` and `ember_star`. All three were played on a
    device, so a real save may hold a record or a `tipsSeen` entry against any of them.
    `ContinueUnit.Taps`, `DefeatReason.OutOfTaps` and `DefeatReason.Barren` are kept as members
    rather than deleted, because their **ordinals** reach analytics on every run those modes ever
    recorded. `progression.json` keeps its `continueRun.taps` figure for the same reason: it is a
    retired unit's price, and the seeder publishes that block whole.
    <br>**What was kept is everything that was a seam rather than a mode** — the prototype level
    shape, the story band and its cast, the village world of backdrops, `ProtoDto.cores` and the
    forty shared skies. `StoryScreen` now has no subclass at all and stays anyway, for the reason
    it survived Deep Orbit, Nova Raid and the Iron Quarry: a seam outlives the thing it was built
    for, and the next mode that talks costs one base class it does not have to write.



39. **A utility buys a finish and never a grade, and in Thornwatch that is arithmetic rather
    than a policy.** The action bar carries three consumables — a **firepot** thrown at the
    hill, a **mending** poured into a ward, a **surge** of fuel — held account-wide, dropped
    by daily chests and bought with gems. Everything about the shape of that follows from one
    question: *how many matches would this have saved?*
    <br>**Why the question has to be asked at all.** A grade is not a private number. Stars
    derive credits, credits are a grove's worth, and a grove's worth reaches a public board
    (19a) — so a consumable that made a run score better would move a public figure, and
    utilities are **not adjudicated**, so a forged one would move it for free. Making them
    server-owned was the obvious answer and the wrong one: a utility is not currency (13),
    there is nothing for the server to recompute, and 10a's claim shape exists for money.
    <br>**What closes it is the mode's own exchange rate.** `SiegeTuning.PerfectMatch` is *the
    most* one match can ever deliver, which is exactly why `SiegeTuning.Par` is allowed to
    divide by it and call itself a floor (37a). So a utility that delivers damage is charged
    `ceil(damage / PerfectMatch)` against the graded count — a floor on the matches it saved —
    and using one is at best exactly neutral and usually slightly dearer. `SiegeUtility`
    is the only place that conversion is written, so a blast and a surge cannot come to price
    themselves differently, and `UtilityTests` sweeps every damage a shipped utility could
    deliver rather than asserting it at one point.
    <br>**A mending charges nothing, and that is the same rule rather than an exception.** It
    delivers no damage, so it saves no matches: what it buys is survival, which is precisely
    what 23 says a purchase may sell. Two smaller consequences of asking the question honestly:
    damage is counted as **absorbed**, so overkill on a raider with three health left is not
    work the player was spared; and a **surge is charged for the whole pour** whatever the ward
    takes, because over-charging is the safe direction and a constant is a price a player can
    learn.
    <br>**And a mending may never raise a fallen ward** — not a kindness withheld, but what
    keeps `SiegeBoard.Stranded` a *certainty*. That predicate decides whether money changes
    hands (28f) and is only allowed to say "no purchase rescues this" because nothing can put a
    ward back up.
    <br>**Which is also why a utility is not the difficulty a board was tuned against.** 29c
    refuses a companion's ability the right to change what a move does, because par is fixed per
    board and an ability that varied it would give two players two different games. A utility
    varies the board and pays for it in the graded unit, so the ladder it is measured against
    does not move — and the stock is account-wide precisely so that it is never a fact about a
    level.
39a. **The stock is two counters per id, and it is the first thing here that needed both.**
    `utilityStock` (save v22) stores `earned` and `spent`, each monotonic, joined by a per-id
    `max`, with what is in hand derived as the difference and clamped at nought. Hearts and
    hints come back on a clock, so they are `RegenLedger`; grove decor is bought and then
    *stands somewhere*, so `homesteadStock` stores purchases alone and derives the rest from the
    placements already in the file (16h). A utility is granted, used and gone — nothing else in
    the save implies it existed — so both halves have to be written down. That is 11b for the
    fourth time and the first time the answer was two counters.
    <br>**The join forgives rather than double-charges, deliberately.** Two devices offline from
    the same five, spending two and three, merge to three spent — two uses free. Adding them is
    not idempotent, so a re-uploaded save or a sync retried after a dropped reply would charge
    them again, which is the failure that actually loses somebody something they paid gems for.
    `RegenLedger` has made the same trade since v8; what makes it safe here is 39 — a forgiven
    use buys an easier run and never a better one.
39b. **A chest may pay a utility, and that cost the server nothing.** `ChestDropKind.Utility`
    carries an **item id** on the drop, because one kind and an id is what keeps a utility
    shipped next year *content* — an enum member per item would make every addition a code
    change and a renumbered contract (9c). The server's mirror needed the id only so that two
    different utilities in one chest do not fold into each other (`ChestDrop.SameAs`); it still
    grants none of them, because `chestCurrencyValue` sums by currency and ignores the rest.
    <br>**But a banked kind must still be published**, and that is the trap this laid bare. The
    seeder's `DROP_KINDS` was the four currency-ish kinds and was shared with the streak, so a
    chest band naming `hints` — a kind the client has shipped since v19 — would have **thrown**.
    A band the seeder refuses is a band whose *streams* are missing, and each guaranteed band
    draws on its own stream number, so dropping one shifts every stream after it and the server
    and the client disagree about what a chest paid **in credits**. One set for two questions is
    how a seeder comes to refuse correct content; there are two now.
    <br>**And the two `Apply` switches had already drifted.** `DailyChests.Apply` handled hearts
    and boosts and silently dropped a hint, on a table that is content and could roll one from a
    config push tomorrow. That is 5b exactly — two copies of one rule, each correct until a case
    appeared that only one had been written for — so there is one switch now (`BankedDrop`), and
    what is deliberately *not* in it is currency, because the chest path and the ad path do
    genuinely different things with it (10a against 10d).
39d. **The bar is furniture, not three buttons — and everything about it came back from
    playing it.** The first cut was a strip of loose squares floating at the foot of the screen
    with the board's old margin above them, and the verdict was *"not even an action bar"*. It
    read as three controls somebody had left there because that is what it was: nothing said the
    row was a *place* things are kept. It is a dark shelf across the whole width now, meeting the
    board's own plate, with the room this mode used to leave empty given over to it entirely.
    <br>**Five cells, and three of them hold something.** A bar sized to the catalog would move
    every slot under a player's thumb the day a fourth utility ships, and the muscle memory for
    "the mending is the middle one" is worth more than two empty cells cost. Drawing the empty
    ones is also honest about where the next two go.
    <br>**The cells are dark wells and the shelf is dark too.** Drawn level with the shelf a cell
    reads as a sticker on a panel; drawn in the source kit's steel greys the whole bar read as a
    different screen under the board's near-black tiles. The well is the ground the items are
    seen against, so it is the darkest thing on the bar, and the shelf is `Pal.Board`'s family so
    the board and the bar are one column. Only a picture says any of that; every cut was green on
    every gate.
    <br>**No hazard rail, no gem price under a cell, and the count moved to the top-right.** The
    kit stripes the top of its tray black and yellow; borrowed here it was the brightest thing on
    a screen whose whole job is telling four gem colours apart. A price under an empty cell was a
    second number competing with the count. And the badge at the foot sat where a thumb rests and
    where the icon is widest — the corner above it is the one part of a cell nothing else uses.
    <br>**And it is drawn, in the source kit's own sampled palette, rather than cut from it.** The
    kit's tray is one fixed-width panel with five cells baked into the plate and two stone wedges
    overlapping its ends: no clean rectangle to stretch, no cell-free column wide enough to
    repeat. Cutting it would mean rebuilding most of it and then living with whatever width the
    source happened to be. What the licensed art is good at here is the *idiom and the colours* —
    32b's rule arrived at from the other direction.
39f. **A target has to be a place, not a distance.** A firepot was aimed by dragging a ring round
    the hill and burning everything within a radius of where the finger left. Exact in the rule
    and unreadable on the board: what the player had to do was judge a distance against raiders
    that were walking, and the ring said how far it reached while nothing said what was in it.
    <br>The hill is a **grid** now — `SiegeTuning.Lanes` by `SiegeTuning.BlastRows`, twenty boxes
    — drawn as translucent panes each with a ring in the middle, and a firepot takes exactly what
    is standing in the one that was tapped. That is invariant 33g at its strongest: the drawn
    thing and the played thing are not two things agreeing about a mapping, they are the same two
    integers. `SiegeAim` is integers throughout for the same reason.
    <br>**And `reach` went with it.** A blast covering exactly one box has nothing to tune, and a
    content field with one legal value is the decoration invariant 5d names — so the field is
    gone from the DTO, the catalog, the content file and both gates, and `content.py` *errors* on
    one that reappears rather than ignoring it.
    <br>Two smaller things the same session fixed, both invisible in the source. The ring a
    mending and a surge are aimed with was 1.6 cells centred a third of a cell high, which sat on
    a ward's barrel rather than round the ward — it is an ellipse sized to the post's own node
    now. And a spent utility kept the ring on its slot, because the view disarmed itself and the
    bar was never told: **two places holding one piece of state**, fixed by one of them telling
    the other (`SiegeView.Done`).
39g. **The board runs to the edges of the screen, and the field is laid out to the width.** The
    three bands were 44 / 16 / 40 of the height with the cell taken as the smaller of what the
    width and the height allowed — which on every phone was the height, so the gem field sat in a
    column with a hand's width of empty plate either side of it while the bar below ran edge to
    edge. Two different shapes on one screen.
    <br>The cell is driven by the **width** now and the hill and the line share what is left in
    the proportion they were authored in, capped by `MaxGemBand` so the hill always has room to
    walk down (37g).
    <br>**And the plate is rounded at the top and square at the foot** (`Art.RoundTop`, reached
    through `ProtoView.PlateSkin`). A fully rounded plate over a square shelf leaves two notches
    where its corners curve away, and at the bottom of a board they read as a gap rather than as
    two things meeting — which is what they are; it was reported from a device as exactly that,
    with the two corners circled. The general rule is **round the end that is open and square the
    end that meets something**, and this is the only mode with anything under its board.
39e. **A sound is a piece of news, so the two loudest things a player can cause got their own.**
    A firepot bursting was `burst`, which a raider's death already plays thirteen times a wave
    and which is tuned to be the shortest, brightest thing in the set; sharing it tuned the
    biggest moment in the mode by the smallest. A mending was `chime`, which read as a coin
    landing rather than as a ward being put back together. They are `boom` and `mend` now.
    <br>**`mend` needed a second source pack, and that is the shape of the finding.** The
    GameBurp set is a library of *physical* noises — pops, bongs, impacts — and has nothing that
    reads as a spell rather than as an object, so `make_sfx.py` grew an `rpg:` prefix resolved
    against a second root, which is the `synth:` prefix's idiom for the third time. A row that
    names no prefix still means the original pack, so nothing already in the table moved. Its
    WAVs rather than its OGGs, because the trim, the pitch and the loudness match should run on
    the original rather than on a decode of it.
39h. **The kit is a shop shelf, and it lists no product and no good.** A player short of a
    firepot met the shop only through the empty slot on the action bar, which is the wrong
    way round twice over: it is reachable only mid-run, and it is the one screen in the game
    where the answer to "I want more of these" is somewhere else. `StoreShelf.Utilities`
    draws `UtilityCatalog` straight — the same roster the bar draws, the same prices, the
    same ceiling and the same stock — which is invariant 16a's argument about the grove's
    residents read across: authoring a second copy in the `store` block would be two records
    of one thing for a merge, a retune and the seeder to disagree about. `TryReadShelf`
    deliberately does not know the word, so a content push cannot file a **real-money**
    product here even by accident; a utility is consumed, and a product granting one would be
    the stored amount invariant 18d forbids.
    <br>**And the ceiling moved from nine to a hundred, which is what made the panel grow a
    stepper.** Nine read as a ration — a shop refusing a tenth is a shop saying you have
    bought enough — and it is also why `UtilityLedger` had no `MaxQuantity`: every order was
    for one, and an unused bound is a bound nothing keeps honest. At a hundred both halves
    invert: a shelf selling one at a time would be a hundred taps, and a stepper is only
    honest if **both** its stops are (the room left *and* the gems in hand), or the panel
    walks a player up to an order the ledger will refuse. Raising a published ceiling is safe
    in the direction that matters, because it is enforced at the moment of a grant and never
    by re-reading a file — nothing anybody holds moves, and `UtilityStock`'s structural clamp
    was 9,999 and was never this number. Two things it did cost: the bar's count badge is
    **shrinkable** now, because three digits at a fixed 34pt overflow a 60-unit disc and a
    `UIKit.Label` that overflows is not clipped (37n, on a badge); and one panel serves both
    doors, because two would be two prices.
39c. **An icon is not content, so adding a utility is a build.** Prices, strengths, ceilings,
    bar order and which chest drops what are all authored in `progression.json` and retunable
    from a config push; a picture is in the app. `ContentValidation` and `content.py` both
    **error** on a catalog entry whose icon `AssetManifest` does not name, rather than letting it
    draw a white rectangle (7b) — and both resolve `utility.{id}.name` and `.note`, which are
    derived from the id (5a) and therefore invisible to `loc.py`, exactly as a story line is
    (30d). The three icons are **drawn** by `Tools/make_utility_art.py` rather than cut from a
    pack, which is 32b's lesson taken before it cost anything: five goals in this project have
    been approximated out of a licensed sheet and every one had to be re-done after somebody
    looked at it. Its `--contact` earned its place immediately — the first cut's flame was a kite
    on a stick and its flask had a notch where two nearly-agreeing shapes met, both invisible in
    the source and both green on every gate.

39i. **A panel over a run holds the run, and the rule had to live in the frame rather than in
    the panels.** The action bar's shop opens over a live siege, and the hill kept walking behind
    it: a player who tapped an empty slot was reading a price while raiders closed on their ward
    line — a run being lost by somebody who had asked the game a question. Every individual piece
    was correct, which is why nothing saw it. The board latches for a lesson, a pause menu, a
    forfeit prompt and a restart gate because each of those *remembers to*, and this panel was
    the first one raised over a running board by a feature that had no reason to think about
    clocks.
    <br>**So it is asked, not announced.** `RunHold.Covered` is taken and released by
    `RunScreen.Update` from `Flow.Covered`, once a frame — the one place that already asks
    whether a run may advance — so a panel added next year holds the run behind it without being
    told, and no call site has to be paired with its own release. That is the shape `RunHold`
    exists for and the third time this project has paid for the alternative: a rule each caller
    remembers is a rule missing from whichever caller was written last (`RunFrameTests`' own
    lesson, where three modes out of four never called `Tick`).
    <br>**It is a no-op in every mode but one, which is exactly why the hole survived.** A
    turn-based board advances only when touched and a modal's scrim already swallows the touch,
    so the reason changes nothing for a glade, a well or a field of gems; a siege's hill walks by
    itself. It also makes `RunScreen.Played` mean what its own remarks already claimed — that a
    panel over the board contributes nothing — which was false for precisely this case.
    <br>**A panel part-way through its exit still counts**, and that is the opposite of what
    `Flow.IsTopModal` and `HasModalAbove` answer. Those decide whether a panel *may be raised*,
    where a closing one must not refuse its successor. This decides whether the board *may be
    played*, and `ModalView.Close` fades the group for a fifth of a second **without ever
    dropping its raycasts** — so a run handed back during that fade is a hill the player can
    watch walking and cannot answer. A destroyed panel still in the stack is skipped, because the
    one failure this must not have is a run held for ever by something nobody can close.
39j. **A stock prices how often across a lifetime; only a cooldown prices *when*.** A utility
    was bounded by what a player holds and by what it costs, and neither of those is a bound on a
    *moment* — so a hundred firepots was a hundred taps in four seconds, and every wave a player
    could not out-match had the same answer, given as fast as a thumb moves. That is a mode
    decided by inventory rather than by play, and it is invisible to every reading here: the
    grade is unmoved (a utility is charged in matches, 39), the boards are unmoved, the ladder is
    unmoved, and every offline gate is green, because *how quickly* a player may act is not a
    question anything in this project asks. The four items now cool for 10, 15, 20 and 30
    seconds, which is also what makes them differ in a second dimension — a stormcall is a thing
    held for the wave that needs it rather than the strongest tap on the bar.
    <br>**It is the one change to this feature that needs no exchange rate.** Everything else
    touching a siege has to be proved against `PerfectMatch` before it may move a board, because
    a grade reaches a public leaderboard (19a). A cooldown can only ever *refuse* a use, so every
    run playable with one was playable without it and no charge can move — the whole of invariant
    39, discharged by the direction of the rule rather than by arithmetic. **Before pricing a new
    limit on a utility, ask which way it can move a run; only the generous direction costs a
    proof.**
    <br>**Nothing about it reaches the save file, and both halves of that are rules.** Seconds
    remaining is a count that goes *down*, so it is exactly what invariant 11b refuses a merge —
    two devices showing 4 and 0 are equally consistent with "one just used it" and "one has not
    heard". And it is per **run**: a cooldown surviving a restart would make restarting a thing
    the bar punished, and one surviving a level would make what a board asks depend on the board
    before it, which is 29c's objection to a companion that changed what a move does.
    `UtilityBar.Cooled` is called by `Play`, `Rewind` and `RetryAfterDefeat`.
    <br>**It burns on the run's own clock and never on a wall clock**, which is 39i read from the
    other end. A panel over a board holds the run (`RunHold.Covered`), so seconds counted off
    `Time.unscaledTime` would make *opening the shop* a way of paying a cooldown off — a control
    the player can use and nothing would ever report. `SiegeScreen.Running` hands the bar the
    same seconds it hands the board, which makes that unrepresentable rather than merely
    unlikely.
    <br>**And the drawing was wrong in the direction only a picture could see.** It shipped as
    the genre's own idiom — darken the part still to wait — which works on every bar that is
    drawn over something lit and says nothing here, because the well is deliberately the darkest
    thing on the shelf (39d). Measured on a render, the wedge was invisible on three of the four
    icons with every gate green. It is a **pale** veil now (`Pal.Glass` at a third) over a
    picture left at full brightness, which is 37m's rule about a ward: a state that has
    *happened* reads as brighter, never dimmer. `Tools/render_siege.py --cooling` is what said
    so, and it caught a second fault in the same pass — the badge drawn *under* the sweep, so how
    many you hold went dim while it counted down.

40. **Two raiders now attack the *field*, and that is the hole the mode had.** Everything on this
    hill could only ever hurt the wards, so the field was a fuel tap a player operated while
    looking somewhere else — one arrow, upward, and nothing coming back. A **weaver** stops
    two-thirds of the way down and locks cells (the gem stays that colour and can neither be moved
    nor lined up); a **thief** hovers nearer and takes gems away altogether, leaving a sack that is
    not a colour at all. Both are undone the moment the **last** of their kind is dead, which is
    what makes killing one a payoff rather than a relief (20m).
    <br>**Neither takes a ward's health, ever**, which makes them a *pressure* rather than a
    threat: a wave holding nothing else can be ignored outright, so `ModeValidator` warns on one
    and `SiegeTuning.EndangersTheLine` already answered the question `Threatens` needed (37z).
    What stops ignoring them being correct is that the cost compounds — a web stays until the
    weaver is dead, so the field a player is left matching on gets worse every second.
    <br>**A web is a flag beside the cell and a sack is a glyph in it**, and the asymmetry is the
    design: a webbed gem is still that colour, so it has to sit *beside* the cell and travel with
    it through `Collapse`; a sack is not a colour, so it is `SiegeLayout.Sack` and every rule that
    walks the field is correct about it unchanged — which is exactly what makes a cog cheap. **One
    parallel array and not two**: anything beside the field has to be carried through `Collapse`
    *and* `Settle` in lockstep, so a sack deliberately carries no memory of the colour it took and
    bursts into a freshly dealt gem instead.
    <br>**The field is never allowed to lock**, because this mode's clock does not stop: the board
    refuses a mark that would leave no legal swap, `Settle` shuffles only what may move, and both
    are capped at six (`MostWebs`, `MostSacks`). And a modifier is a **table** now
    (`SiegeLayout.Modifiers`) rather than three branches — the shield shipped as a special case in
    four places, and every one of them would have had to be extended twice more, in step, by hand.

41. **A random stream is part of a level's content, and anything that changes how often it is drawn
    from is a content change.** The hill's lanes were drawn from the *field's* stream, which reads
    like an accident and is load-bearing: every gem dealt after the first wave depends on how many
    times the muster has drawn. Moving the lanes to a stream of their own — which is *correct*, and
    which the weaver needed — re-rolled all ten shipped rungs: three became unholdable, two became
    trivial, and nothing in any file was wrong. `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine` was
    the only thing that could see it, and only after it was changed to report the **whole chapter**
    rather than stopping at the first rung that missed.
    <br>So `SiegeBoard._hill` exists and is **narrow on purpose**: it carries only the draws a
    player's taps do not order, which is a weaver reaching for a cell on a timer. A lane is drawn
    once per raider in a sequence fixed by play rather than by frame rate, so it stays where it
    was. What did change is `Nearest`: **a tie goes to the healthier ward**, because five lanes over
    four wards puts lane two exactly between the middle pair and a first-wins tie-break sent two
    lanes' worth of blows at one turret for a whole run (37t's argument about `Wanted`, said about
    a swing).

42. **A player chooses the line, and the load-bearing rule is that no turret may make a bolt
    weaker.** Twenty turrets, one per silhouette, each standing on a colour the player picks
    (`WardLoadout`, set once and carried into every rung). A siege's par is the hill's health over
    the **baseline** bolt (37a), so a turret that hit softer would push three stars out of reach of
    whoever chose it — a grade decided by a purchase, which is precisely what invariant 39 refuses
    a utility. `WardCatalog` therefore has **no field that could express one**: an ability carries
    a bonus and never a multiplier below one, so the rule is enforced by the shape of the data
    rather than by a check somebody has to remember, and `WardLoadoutTests` sweeps every model,
    every rank and both halves of the shield rule anyway.
    <br>**What makes choosing one a decision rather than an upgrade** (26h's test) is that a line
    holds four and a colour is what a level's hill decides: rend on red is worth a doubling on a
    rung sending red bulwarks and worth nothing on one that sends none. Only one ability reaches
    the primary hit (**rend**, which a shield does not blunt) and only one changes what a ward
    *holds* (**beacon**) — and that second one was half-built at first, because three places
    clamped fuel to the mode's constant rather than to the ward's own capacity: a turret that costs
    credits, says it banks a cascade, and does not.
    <br>**Two prices, one gate**, which is 16j's ladder and 15a's ordering: credits are what play
    pays out, so a credit price carries a keeper level; gems are the shortcut and ask nothing. Ten
    abilities times two rungs, so the shelf reads as ten families rather than twenty strangers.
    <br>**Owning is an entitlement and standing is an instruction**, which is invariant 16's split
    asked of one feature: `wardsOwned` is a union-joined id set with the starter never written down
    (16e, 16f), and `wardLoadout` is merged by recency against its own stamp (11c) with a colour
    nobody chose for writing no row. A stored id is a **hint** — ownership is re-checked at resolve
    time, so a device that lost a purchase to a failed sync falls back rather than playing a turret
    it cannot account for (8b's rule).
    <br>**The rank moved to the badge, and a render is what said so.** A ward's rank used to be its
    silhouette (37w) and cannot be now that the silhouette is the player's. A plinth under the
    turret was built, baked, validated and **invisible** — invariant 37y already records that a
    ward's foot sits behind the field's plate on every screen this mode is drawn at. It is the
    badge's own colour instead (steel, bronze, silver, gold, white-hot, climbing in *value* as well
    as hue), which costs no art and is read at a glance where a number has to be read.
    <br>**And the art is scoped rather than resident** (7b): twenty models in four colours is eighty
    turrets and eighty recoil reels, of which a run draws **four** (`AssetLibrary.LineScope`) and
    the shelf draws twenty uncoloured thumbnails (`WardShelfScope`, 16c's rule). Their addresses
    are the one place in this project a lookup is allowed to *build* a name, so `artnames.py`
    cannot check them — what replaces the literal is stronger rather than weaker: the roster is
    content, so `ContentValidation` and `content.py` walk it and error on a model whose pictures
    are not on disk, which catches a missing file and a misspelled id at once.

43. **A mode may have a second ladder, and it is a *track* rather than a mode or a chapter.**
    Thornwatch's **Infinite** is the same board, the same wards, the same raiders and the same
    verb; what differs is that its waves never stop. Filing it as a mode would give it its own
    switcher row, its own art, its own validator and its own chapter ladder; filing it as an
    ordinary chapter is worse, because `LevelUnlock.GateFor` looks for the chapter *before this one
    in the same mode* and would gate a real chapter on stars nobody can earn. So `GameTrack` is one
    level finer than `GameMode`, `CatalogIndex` lanes on the **pair**, and `ChaptersIn(mode)` /
    `LevelsIn(mode)` answer the **main** track alone — which is what keeps `Next`, `Previous`,
    `OrderOf`, `IsLast`, `GateFor` and `NextToPlay` correct with no change at all.
    <br>**It cost the save file one field and the wire nothing else** (20a, once more): an endless
    level is an ordinary level with a permanent id, so its record, its stars, its rewards and its
    merge are the ones every glade has. What it added is `endlessBest` — one monotonic integer per
    level id joined by `max`, which is 14a's floor exactly, pays nothing (credits and XP derive
    from the star ledger alone, invariant 9) and therefore needs no server work.
    <br>**It is graded on a count that climbs, and that is the one place in this game the ordering
    inverts.** Everything else is graded on something the player *spends*, so fewer is better and
    par is a floor; a run that can never be won has nothing to spend against, so what it is graded
    on is how far it got (`LevelTuning.Climbs`). Three consequences, and the third is a rule:
    `LevelRecord` keeps the **larger** count; the standing is not taken, because
    `LevelStats.PercentSlower` ranks a count where fewer is better and would publish a percentile
    meaning the opposite; and `LevelValidator.CheckStarBands` **grows a branch** rather than an
    exception (26e), because a check that disagrees with the thing it checks is worse than no
    check.
    <br>**An endless run is *finished* when the line falls, and that is not a euphemism.** A run
    that can never be won still has to end, and that is the only ending it has — routing it through
    `IsFinished` rather than through the defeat path is what makes it an ordinary run in every way
    that matters, including promoting stars and paying the credits they derive.
    <br>**The muster is a rule, not a list.** `SiegeEndless.WaveAt` is a pure function of the wave
    number and the level's own seed — a **hash** rather than a stream, so wave forty does not
    depend on how many rolls waves one to thirty-nine took and a rule change cannot move a hill
    somebody had learned. A boss every fourth wave to sixteen, then every unordered **pair** of the
    four every fifth wave: thirty waves before anything repeats, and every boss met alone before
    any pair (37z's argument, since each of the four takes a different thing). **The ramp is in the
    raiders and never in the rules** — a bolt is worth what it is worth on wave one and on wave
    ninety, and what climbs is health, blows and how many of them — which is the only shape that
    can climb for ever without contradicting a number some other level depends on, and it is why a
    run always ends: the line's output is bounded and the hill's is not.
    <br>**A boss that takes no health has to arrive with an escort, and that is invariant 5d
    meeting a rule that had always been safe.** Everywhere else a boss wave sends its boss and
    nothing else, deliberately: a warlord, a warbringer and an overlord all shell the line for as
    long as they live, so an empty hill is the *point* and stacking a duel on a wave still
    swinging is two fail states arriving together (37t). A **blightcaller** takes a ward's *fire*
    (37z) — and fire is worth exactly nothing with nothing on the hill to burn. Wave four is a
    lone blightcaller, and it was reported from play in one sentence: *the first boss does not do
    any damage.* It was true. Five seconds of dark over an empty hill costs the player nothing, so
    a quarter of the lane's first sixteen waves rejected no play at all, with every gate green —
    and it repeats, which is what makes it worse here than on the authored ladder where the run
    ends when the boss dies.
    <br>So a boss wave asks `SiegeTuning.EndangersTheLine` — the predicate 37z already added for
    this exact question one level up — and escorts its bosses with the raiders that wave would
    otherwise have sent when none of them can take a ward down. It is asked of the *predicate*
    rather than of the blightcaller by name, so a fifth boss taking something other than health
    inherits the answer. **The authored ladder answers the same question with a short quiet
    (`RestBefore`) and an endless lane cannot**: its muster fires the moment the hill is clear, so
    a player who is ahead of the clock meets the boss alone however long the quiet is (37k).
    **Before giving a boss a spell that takes something other than health, ask what that thing is
    worth when the hill is empty.**
    <br>**A pair is the first thing in this mode that is two of something, and both halves of
    "two" had to be said.** Where they *stand* is `SiegeTuning.BossLane` — the middle alone,
    either side of it as a pair — which is a rule rather than a conditional inside `Muster` so
    that `SiegeView` and `Tools/render_siege.py` draw the hill the board is playing (33g). Where
    their health *hangs* is `SiegeView.FreeCrown`: a boss's bar is anchored across the top of the
    board rather than carried (37u), so two of them drew at one y and read as one bar at a
    strength nobody could account for — two readouts overlapping is two readouts nobody can read,
    which is 37u's own finding arriving through a door that had been safe for as long as a level
    could only ever send one boss. A crown takes the lowest rung nothing is hanging from, asked of
    the crowns that are actually *up* rather than counted off the wave, because a bar outlives its
    body by half a second and a survivor must not jump. **No numeric gate can see either of
    them**: `render_siege.py --wave 21` is what did, and it is why that flag exists.

44. **The whole UI is one bought interface kit, and it moved in one commit because the names
    were already roles.** `Skins` says `btn_green` and has meant "do the thing" since this UI was
    written; `btn_red` has meant "leave", `sq_dark` "not a control right now". So ninety-odd call
    sites were already naming a *role* while appearing to name a colour — which means the way to
    restyle every screen at once is to **re-cut what those names point at**
    (`Tools/make_hud_kit_art.py`'s recut list) rather than to sweep the call sites. Nothing can be
    missed, the compile proves nothing broke, and `git checkout` on fourteen PNGs puts the old
    look back. **Before planning a sweep, ask whether the thing being swept is already an
    abstraction wearing a concrete name.**
    <br>**A re-cut keeps its `.meta`.** Addressables keys every registered entry on the guid, so
    writing a fresh one orphans the address rather than moving it: the game still asks for
    `Ui/btn_green`, nothing answers, and what ships is a white rectangle (7b). Only the
    nine-slice border moves, because the new art's corner is not the old art's corner.
    <br>**`ShopSkins` was absorbed into `Skins`, which is what its own note said the rollout
    would be** — "moving names from here to there, rather than rebuilding anything". The
    storefront was where the look was judged worth changing first and it is now every screen's
    look, so there is one table again.
    <br>**It was done a second time on 2026-09-09 and that is what proves it.** The owner did
    not like the mobile-game-ui kit and named two packs he did — so the whole app moved onto the
    **merge-shooter kit** for the price of *one file*: `make_hud_kit_art.py` re-pointed at a new
    source, twenty-eight PNGs rewritten, `UIKit`'s two face lifts re-measured, `Skins`' two
    ground colours re-sampled and its prose brought up to date. **No screen, no call site and no
    layout moved**, the compile proved it, and the whole suite was green first time. A restyle
    costing one tool is the return on having made ninety call sites name roles.
    <br>**And a third time the same day, which is where the method stopped being about art.**
    The verdict on the merge-shooter kit was that both screens were boring, the colours were
    bad and the assets were wrong — and the renders say exactly why, in terms that are not
    taste. Every plate on those screens was a **cream rim around the ground colour**: the card
    interiors sat within a few points of the backdrop, so nothing read as an object standing on
    anything; one rim of one width on every surface left no hierarchy; and behind all of it was
    a flat near-black wash with no world in it. **A screen made of outlines on a void is boring
    however well each outline is drawn**, and the fix is not a better rim. It is opaque plates
    with a material of their own, over something alive — see 44h.
44h. **A restyle that is only a re-cut can still ship a boring screen, and the two halves that
    fix it are the *plate* and the *ground*.** The third kit is the cartoon UI mini kit —
    saturated two-tone faces inside one heavy navy keyline `(6, 24, 56)`, ribbons with tails,
    discs in a white ring, which is the register the genre this game is aimed at actually plays
    in. What made the screens change is not that: it is that plates became **opaque navy with
    their own material** rather than a rim around the ground, and that the ground became a
    **world** — the level-map pack's sky and islands, composed, blurred hard and graded in the
    tool. `Scenery.Room`'s dim went .46 → .10 because it no longer has anything to hide.
    <br>**A backdrop has to read as *somewhere*, not as something**, and that is one number. At
    a 3-pixel blur every tree, plank and flower was still legible and the world competed with
    the plates on it — the same fault as the last kit's junction box behind the companion,
    arriving from the opposite direction. Nine pixels leaves shapes and takes detail, which is
    what a depth-of-field does and what no gate can have an opinion about.
    <br>**A hue mask cannot protect a keyline whose own hue the pack also paints faces in.**
    The last kit masked a re-paint by hue — keep anything within a band of the moulding's navy —
    which works only while nothing is *drawn* in that colour. Here the pack's blue bar sits
    0.032 from the keyline, so a band wide enough to cover an outline's antialiasing swallows
    the face too and the paint is a **silent no-op**; turned off, the outline rotates with the
    face and eight differently-outlined blobs are not a kit. `DARK_FLOOR` separates them by
    what they actually differ in — the keyline is 8% luma and the darkest face in the pack is
    13% — so every recut rotates off one gold mould with the mask left on. **Before masking a
    re-paint, ask what the thing being protected differs from the thing being painted *in*.**
    <br>**A shape whose silhouette is wrong cannot be fixed with colour**, which is 44f's coral
    tab said about an object. Every disc in every pack here is a coin: bright face, gold rim,
    nothing underneath. The pad was tried gold, teal, dimmed to bronze and gold-and-sunk over
    two kits and the render said "coin" every time. It is **drawn** now — a navy face in a gold
    rim with a *side* below it, which is the one thing a coin does not have.
    <br>**And a mirror that under-draws is worse than no mirror.** `render_home.feature` drew
    two empty boxes with a "3" in them for as long as it existed, where `BuildStreakBox` has
    always built a flame, a count, a caption, a strip and a corner badge. Half an hour went on
    "the feature cards are empty" before the screen was read. **A render that draws less than
    the screen sends you off to fix something that was never broken** — which is the same class
    of fault as 44d's own recorded one, where it drew a widget hanging off a plate that was not.
44a. **Upscaling a nine-sliced sprite makes its unstretchable ends bigger and its usable middle
    smaller**, which is the opposite of what upscaling is usually for. A border is drawn one
    *sprite pixel* per UI unit, so cutting the kit's readout plate at twice its size doubled its
    two side clips to 178 units — on a balance drawn 228 wide that leaves **fifty units of
    interior for a five-digit number**, and the shop's three balances rendered as two pipes with
    a black dot between them while every bonus plate wrote its words out over its own rim.
    **Scale a sliced piece for the size its corner should draw at, never for resolution.** The
    same arithmetic is why a glyph 38 units in from a trough's edge is a glyph standing on the
    pipe: a clip does not shrink with the box.
44b. **A face lift is a property of the art, not of the caller — and keeping a question whose
    answer was nought is what made the next kit cheap.** `UIKit.PillFaceLift` and
    `SquareFaceLift` were 8.8% and 8.1% because the first jelly buttons carried a moulded base
    below their lit face; the mobile-game-ui kit centred its face in its frame, so both went to
    **nought**, and the note left behind said they were kept rather than deleted because "a kit
    whose face is off-centre is exactly the sort of thing this project buys next". It was, one
    kit later: the merge-shooter kit moulds every control as a bright face over an indigo base,
    and they are **0.0674 and 0.0539**. Twenty call sites correctly ask "lift my caption by
    whatever this art needs" and not one of them moved.
    <br>**Measured, not typed.** `make_hud_kit_art.py` prints both on every run off the two
    moulds it actually cuts, so re-cutting the kit re-answers the question instead of leaving a
    number that used to be true. A caption not re-measured sits a few units off on every control
    in the game at once — wrongness that is much easier to see than to explain.
44g. **Dimming is not how you recolour something warm, and it took a device to say so.** The
    first cut of 44f's treatment *multiplied* each plate's interior down by .30 — arithmetically
    a perfectly ordinary way to make something darker, and visually a disaster: multiply takes a
    colour toward black **along its own hue**, so amber arrives as **brown**. Every panel and
    every card in the game became a mud box inside a thick pale frame. The renders showed it and
    it was read as "dark plate, warm rim, fine"; on a phone the owner's verdict was one word.
    <br>**Three things made it worse than a wrong number, and each is the general lesson.** The
    fault *scaled with area* — the small troughs were fine and the big plates were not, so the
    thing that looked acceptable in a contact sheet was the thing that had not been tested. The
    **rim was a frame rather than a rim** (a 22-pixel border on a 474-unit card reads as a
    picture frame round a photograph, not as the edge of a card). And the ground, the rails and
    the lander had each been picked in isolation, so the screen carried a violet room, saturated
    **cyan** rails and amber plates — three unrelated hues, two of them framing every screen in
    the game.
    <br>**A well is a material, so give it one.** `welled` now *replaces* the interior with a
    named ink (`WELL_INK`, the kit's own indigo near black) and keeps a tenth of what was
    underneath for grain. The rails and the lander were re-painted to that same indigo, the room
    taken down to a ground rather than a lavender haze, and the rim thinned to a rim. **Before
    darkening anything a player will see a lot of, ask what hue it lands on** — and note that a
    palette is judged on a whole screen, never a piece at a time.
44f. **A kit drawn for light screens cannot be cut as it ships into a game that writes light
    text, and the fix is a treatment rather than a sweep.** The merge-shooter pack draws for its
    own bright screens: cream readout bars, amber boards. This game writes `Pal.Cream` on every
    count and `Pal.Sun` on every ribbon and header, over a near-black ground. Cut as the pack
    ships them, the trough was cream on cream and both plates were gold on amber with the label
    outline doing all the work — the yellow-price-bar-on-a-yellow-card fault (44), arriving on
    four surfaces at once. Changing the *text* would have been the ninety-call-site sweep this
    whole invariant exists to avoid, so what changes is the art: `welled()` darkens what is more
    than a rim's thickness from the sprite's edge, so a plate keeps the frame the pack drew and
    gains an interior light text reads on. **Before cutting a bought kit, check which way round
    its contrast runs against yours.**
    <br>**A sunk plate's ramp has to finish inside its nine-slice border**, and this one is not
    subtle. The shading is baked per pixel from the sprite's own edge; a nine-slice then keeps
    the corners and stretches the middle, so a ramp that crosses the border leaves the corner
    pieces carrying a *rounded* inset and the stretched middle a straight one — and every card
    on the hub wore a step at all four corners. `build()` now refuses it by name.
    <br>**And an ornament may be a bump in the silhouette rather than paint on a square.** Both
    of this kit's boards carry a coral tab, and a tab cannot survive a nine-slice: it sits in the
    middle of the top edge, which is exactly what a slice stretches, so every panel and card drew
    a coral band straight through its own header. It was taken for paint and recoloured, which
    left an amber *bump* and a shoulder either side of it — **two fixes that were not fixes,
    because nothing about a border or a colour can move a silhouette.** Measured, the board's own
    top is row 100 and the tab is rows 45..100; the answer was a crop. Every numeric gate was
    green through all three rounds and `render_home.py` is what saw each of them.
44c. **A backdrop's crop is a decision for the tool, never an offset in the screen.** The kit
    authors its room in landscape and this canvas is portrait, so a straight envelope shows the
    middle third — which is exactly where the pack's junction box and its two red lamps are, so
    the one object on the hub a player is meant to look at stood in front of a machine competing
    with it. A screen can only crop by *offset*, which is a number nothing checks and which two
    screens would each have to get right. The cut is taken in `make_hud_kit_art` instead, from
    the source's clean run of pipe, and blurred — because a backdrop in focus is a backdrop
    asking to be looked at. `Scenery.Room` then just envelopes a picture already the canvas's own
    shape.
44e. **A `switch` whose `default` is a real answer hides the case nobody is looking at.**
    `Skins.Accent` gives a shelf the colour that tells it from the next one, and it named four
    shelves and returned the gems' pink for the rest — which is correct for gems and silently
    wrong for **`StoreShelf.EventPass`**, the Bloom Pass, which draws no tab today and would
    have shipped cards identical to the gem shelf's on the day somebody turned it on. Nothing
    could see it: the enum is exhaustively handled as far as the compiler is concerned, the
    screen is correct on every shelf a player can currently open, and the render draws the three
    shelves that have a ladder. What saw it is `SkinsTests.NoTwoShelvesShareAnAccent`, which
    states the *property* — no two shelves are told apart by nothing — rather than checking the
    four that were written down. **Prefer a property over a table wherever an enum can grow.**

44d. **Two screens made of the same furniture get one mirror, and it found six faults no gate
    could.** `Tools/render_home.py` and `Tools/render_shop.py` share `Tools/hudkit.py`, which
    copies `UIKit`'s nine-slice, aspect fit, tint and outlined caption and `Skins`' palette —
    two copies of that would be two answers to questions this project settled once. What the
    pair caught on this restyle: the fifty-unit trough (44a), a bonus plate overflowing its own
    rim, a beam long enough to wash over the two cards above it, a companion standing *inside*
    its pad rather than on it, the junction box (44c), and a nav cap that had come off the rail.
    Every numeric gate was green through all six. **And a mirror has its own bugs**: three of the
    first round's "faults" were the render reading an `anchoredPosition` as an edge when
    `UIKit.Box` always pivots at centre, and reading Unity's up-positive y as an image's
    down-positive one. A picture that disagrees with the code is not automatically the code being
    wrong.


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

**`GlimmerGrove.Authoring` is the home for a rule that decides whether content is fit to ship and that no
player ever runs.** Such rules are under two constraints at once — the build gate has to reach them, and so
does the test assembly, which references `Domain` and *not* `Editor` — and for a long time `Domain` was the
only place satisfying both, so a seed sweep, a map-collision check and a chapter-mode check were compiled
into every player build and never called. The membership test is mechanical: **a rule belongs there when no
shipped type references it**, and `compile.py` proves it by building `domain` *without* `Authoring` on its
reference list, so a Domain file that starts calling into it fails offline rather than quietly dragging the
folder back into the build.

**A mode is declared three times, and the third one is what moved the validator.** `LevelMode` (Domain) says
what a mode *is*; `ModeLook` (Presentation) what it *looks like*, split off because Domain may never
reference Presentation; `ModeValidator` (Authoring) how it is *proved fit to ship*. That last split is what
let `LevelValidator` leave the player build: `LevelMode.Validate` had been a `virtual` member, so the
authoring entry point called into the mode and the mode called back, and the cycle pinned both wherever the
runtime could see them. The price of a registry over an abstract member is that an entry can be *missing*
where an override cannot, and a missing one is a green tick over a mode nothing looked at — so
`LevelValidator` reports an unregistered mode as an **error**, and `ModeValidatorTests` fails the build when
the two registries drift.

`Assets/Game/CONTENT.md` is the authoring and pipeline guide. Read it before touching content, assets or
localisation.
## Verifying

The Unity Editor is often not running, and the MCP bridge is unavailable whenever scripts fail to
compile. Do not guess — verify offline:

- **Compile check:** run Unity's bundled Roslyn directly (`Tools/verify/compile.py`). See
  `verify-content-without-unity` in the memory directory for the exact command.
- **Content check:** `Tools/verify/content.py` — parse the StreamingAssets JSON, prove every level
  solvable, derive par, confirm every loc key resolves, and run `board-vectors.json` through both Python
  copies of the four-armed-tile rule (`BoardVectorTests` runs the C# one). It reads the manifest, so a
  chapter carrying `"disabled": true` is skipped whole and the hidden glade, Lightfall and Prismvale
  chapters are not proved on a run (invariant 38) — re-enable one and it is checked again with no other change. The
  per-mode checks are rolled into it: the **prototype modes** (one check for all of them - par
  searched, not finished on arrival, a legal move to make, plus the handful of questions only each
  mode's own rules can ask; `ways`, `careless`, `nodes`) - which today proves nothing, **Prismvale**
  being hidden with the rest (par searched, the field authored **dark**, nothing dealt into it, no
  critter standing where no lantern could ever reach it; `ways`, `careless`, `dealt`, `used`,
  `paired`, `idle` — all of it checked again the moment the chapter is re-enabled) -
  **Lightfall** (par searched, brim row empty, nothing floating, procession carrying all three
  channels; `motes`, `headroom`, `ways`, `greedy`, and from the second chapter `lenses`, `whorls`,
  `fused`, `kindled`, `aim`, `reach`), and **Thornwatch**, which is the one mode it does *not*
  search, because there is nothing to search (invariant 37a): what is proved is the layout's own
  refusals, the arithmetic par held to `SiegeTuning.Par` by being the same three lines, and the
  readings a validator can act on (`waves`, `raiders`, `brutes`, `colours`, `wards`, `boss`,
  `kind`, `spell`, `cogs`, `stood`, `threat`, `swap`). **`threat` counts two blows a raider, not one**, and the
  correction is worth knowing: a raider that reaches the line goes on swinging until something kills
  it, so the single blow the first version counted named half a shipped chapter as unlosable when
  every one of those rungs bleeds the line in play. It is a floor on a floor; what measures the
  threat is `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`. `fall-vectors.json` is the contract with the shipping C# rules; the prototype modes have
  no vector file and are pinned **inline** by `ProtoLadderTests` instead, for the reason invariant
  29e gives. `bud-vectors.json` went with Budburst.
- **Difficulty check:** `python Tools/verify/difficulty.py` — what each glade actually asks of a player,
  counted rather than argued about. Not a gate (5d). It enumerates rotations of a grid of conduits, so it
  reports glades and names other modes as skipped. `dealt` is the one column about the board as the player
  *meets* it rather than about its solution (5g).
- **Story keys:** rolled into `content.py` and into the Editor's `Validate Content`. A line of dialogue is
  the one loc key in this game that is *authored* rather than derived from an id (invariant 30d), so
  `loc.py` cannot see it — both gates resolve every key a chapter actually writes and error on a missing
  one, which is stricter than the naming convention it replaces.
- **The turret roster:** twenty turrets, four colours each, cut from the idle-defence kit by
  `Tools/make_siege_art.py` and proved by `--check` with the rest of the mode's art. Their
  addresses are **built** from an id (`WardModel.ArtFor`), which `Tools/verify/artnames.py` cannot
  see - so the roster is walked by `content.py`'s `check_wards` and by
  `ContentValidation.ValidateWards`, both of which error on a model whose pictures are not on disk
  or whose two derived loc keys do not resolve. That is stronger than a literal rather than weaker:
  it catches a missing file and a misspelled id at once (invariant 42).
- **Thornwatch's art:** `Tools/make_siege_art.py --check` proves every sprite, cast flipbook
  and explosion is what the tool writes. It reads **three** source folders - the CraftPix packs,
  the turret/top-down packs in `to-assets`, and the mine tileset - passes when any of them is
  absent, so a checkout without them still runs the gate, and `--contact` lays it out to be
  looked at. Gems, cast, turrets and the mine floor are **cut**; only the rampart, the field's
  plate and the plinth are drawn.
  <br>The **ten grounds** are `GROUNDS`, one per rung of a chapter (37ab): the mine tileset's own
  square slabs, each rung a different gradient map over a different mix of them laid by a different
  seed, all nine normalised onto the mine's own untouched floor at rung five. It is one tileset
  because it is the only **top-down** terrain here; the isometric packs cannot floor this board at
  all.
  <br>The **ward line** is twenty turrets and twenty recoils - five tiers of the merge kit's own
  upgrade ladder, each hue-rotated to one of the four colours - because a ward now carries its
  **rank** in its silhouette and its colour in its hue (37w). Every tier is fitted to one box and
  pinned by its *foot*, so a turret does not change size or rise off its plinth when it goes up.
  <br>The **four bosses** are `BOSS_SET`: four bodies from four packs at four heights, three reels
  each off one canvas so none of them changes size when it throws (37z). One loop rather than a
  block per boss, which is both what made a third and a fourth cheap and what makes the *set*
  something a reader can see at once.
  <br>**The per-colour shot, muzzle and hit reels under `Art/Fx/Siege/` are not its work** - they
  come out of `Bake Siege Projectiles` below.
- **Thornwatch's projectiles:** `Glimmer Grove ▸ Art ▸ Bake Siege Projectiles` renders the four
  ward projectiles, their muzzle flashes and their impacts out of the bought pack's own prefabs
  into sprite reels under `Art/Fx/Siege` (invariant 37k); `▸ Verify Siege Projectiles` re-bakes and
  holds what is on disk to it within a tolerance, and `▸ Siege Projectile Contact Sheet` lays every
  reel out on the hill's own colour. **This is the one art tool here with no offline gate** — no
  Python script can rasterise a particle system — and it needs the pack, which is gitignored, so it
  is silent on a checkout without it. Re-run `▸ Addressables ▸ Sync All Assets` after a bake: the
  importer hook does not fire on files a tool wrote while the Editor was busy.
- **The Infinite lane:** `python Tools/chapters/s02_endlesswatch.py` re-derives the body from its
  seed and walks the ramp as far as the second time every boss has been met, which is where the
  schedule starts repeating. There is nothing to search (invariant 37a) and nothing to author but
  a field, a line and two numbers, so what is proved is that the field is playable, the line is
  legal, the two star waves are the right way round and the ramp sends nothing the line has no
  answer to. `SiegeEndlessTests` pins the schedule itself, written out rather than re-derived - a
  test that re-derived the rule would agree with a wrong rule.
- **Thornwatch's boards:** `python Tools/siege_sweep.py --want N` deals fields from seeds and
  keeps the ones worth keeping - an even spread of the colours, no three alike already touching, and
  a chosen number of opening swaps; `--cogs N` stands cogs on them and `--gems` narrows the deal.
  **It deliberately does not play the level**: a siege has no search (37a), so whether a rung can be
  *held* is answered by `SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine`, which plays the real rules
  over all ten. Every field in `Tools/chapters/s01_thornwatch.py` is a recorded seed and the rows
  are re-derived from it rather than typed, so a board can never drift from the thing that made it.
- **Thornwatch legibility:** `python Tools/render_siege.py` draws every shipped level at the size a
  phone draws it, with the real sprites, using `SiegeScreen.HostInset` and `SiegeView`'s own
  arithmetic; `--level ID` picks one **or several, comma separated** — which is what draws the four boss
  rungs side by side — `--raiders N` stands that many of the first wave on the hill,
  `--no-bolts` takes the exchange off it, `--no-bar` takes the action bar off, `--cooling`
  draws slots mid-cooldown (bare for a sample, or `id=seconds` pairs), `--warlord
  cast|idle|walk|storm` picks which of the boss's three reels it is wearing, or draws the frame
  its **volley leaves** (invariant 37ac), `--line a,b,c,d` stands a
  chosen loadout (42), `--wave N` reads the **Infinite** lane's hill at a wave number, and
  `--aim hill` / `--aim wards` draw a utility's targeting. With no `--level` it draws all ten
  rungs and the endless one, each on **its own ground** (37ab), which is the only picture that says whether the ten read as ten places and
  whether any of them competes with the cast standing on it. Render the four boss rungs side by
  side and whether the four are four different fights answers itself — which is the picture that said they were not (37z),
  and that then caught the first repair reading as a creeper. **`--warlord storm` asks the same
  question of what they *throw*** and caught four more, every one a placement or a value no gate
  can see: lightning that came out white whatever colour threw it, bolts drawn off the top of the
  plate and over the status bar, a volley whose arms were too close together to read as more than
  one orb, and a storm dense enough to read as noise (37ac). It draws the ward line **one rank apart across the four
  turrets**, so one picture says whether the five tiers read as tiers. **Its insets are in the screen's
  own order — (left, bottom, right, top)** — and were written as (left, top, right, bottom) for
  a long time, which drew the board 55 points high: a diagnostic that is the only thing able to
  see a band in the wrong place must not itself put one there. **Look at it.** It is the only check that can see a fuel tube hidden behind
  the field's plate, a raider whose colour does not read, or a bolt baked so loosely that it
  crosses the hill as a sliver — every one of those a fault it caught, all past a green gate
  (invariants 37g, 37k). It caught three more putting the warlord on the hill, and all three were
  *placements* (37u): a boss health bar outside the plate, then one on top of the ward line's own
  bars, then a boss shrunk twice to make room for a bar that should never have been carried.
  **And a fourth, on the lane that authors nothing**: `--wave 21` is the first hill in this game
  holding **two** bosses, and it drew their two health bars exactly on top of each other — the
  same fault as the second of those three, arriving through a door that had been safe for as long
  as a level could only ever send one (43). It draws each reel at its **loudest** frame, because these effects dip:
  drawn at a fixed index it caught two muzzles mid-dip and reported a bake that was fine as broken.
- **The interface kit:** `python Tools/make_hud_kit_art.py --check` proves the fifteen
  sprites under `Art/Ui/Hud/` **and the fourteen it re-cuts in place** (`Ui/btn_*`, `Ui/sq_*`)
  are what the tool cuts, and `--contact` lays them all out. It reads the **cartoon UI mini
  kit** and the **level-map background pack** out of `~/Downloads/2D ASSETS`, falling back to
  `~/Downloads/to-assets` — **two roots, because copying a licensed zip so one path works is a
  second copy nothing keeps in step** — and **passes when they are absent**, so a checkout
  without them still runs the gate. Twelve come from the cartoon kit, **one is the world**
  (sky and ground layers composed here, not the pack's own flattened preview) and **three are
  drawn** (the beam, the burst and the companion's plinth, which no pack here has). The
  tower-defence pack is no longer read at all: it was in this tool for the pad, and the pad is
  drawn. Three things it does that are the point of it rather than incidental.
  It writes each sprite's
  **`.meta` as well as its PNG**, because a nine-sliced border lives in the importer rather
  than in the image and a PNG dropped in without one stretches its corners with nothing
  anywhere able to notice. For the fourteen re-cuts it **patches the existing meta instead of
  writing one**, keeping the guid — Addressables keys every registered entry on it, so a fresh
  guid orphans the address and the game draws a white rectangle (invariant 7b). And **which
  axes slice is a decision rather than a measurement**: this pack paints tabs and clips *onto*
  silhouettes that are already square, so `corner()` measures the ornament rather than the frame
  — the store board comes back with a left border of 212 on a body that starts 27 pixels in — so
  the plates type their borders outright.
  <br>It also prints the two **face lifts** on every run (44b), and refuses a sunk plate whose
  shading ramp crosses its own slice (44f).
- **The two screens' look:** `python Tools/render_home.py` and
  `python Tools/render_shop.py [--shelf coins|--all]` draw the hub and the storefront at the
  size a phone draws them, with the real sprites and each screen's own arithmetic; they share
  `Tools/hudkit.py`, which mirrors `UIKit` and `Skins`. **Look at them.** Between them they
  have now caught: a yellow price bar on a yellow card, a ribbon whose words ran onto its own
  tails, a beam that washed over the cards above it, a companion standing *inside* its pad, a
  backdrop whose machinery competed with the one object on the screen, and a nine-sliced
  trough whose two clips left fifty units of interior for a five-digit number. Every numeric
  gate was green over all six, because no gate in this project opens a PNG — 32b for the
  seventh and eighth times, met on chrome rather than on a board.
  <br>**The second kit added four more**, every one green on compile, tests, content, art
  names and `--check`: a coral tab stretched into a band across every panel and card, an amber
  bump left where that tab was mistaken for paint, a stepped inset at all four corners of every
  plate (44f), and a seal whose star was narrower than the word written across it.
  <br>**And one the renders did *not* catch, which is the limit of them worth knowing.** They
  showed the mud-brown plates of 44g and were read as fine, because a plate that is "dark with a
  warm rim" is a defensible thing to see in a picture and only reads as cardboard at the size and
  the quantity a real screen has. **A render answers *is this widget where I think it is*; it is
  much weaker at *is this palette any good*** — for that there is no substitute for the device,
  which is why looking at both of these is a step and not the last one.
  <br>**And it had one of its own, which is why it is worth saying again that a picture
  disagreeing with the code is not automatically the code being wrong.** The hub's rank bar and
  keeper name were placed here by their *left edges* where `UIKit.Box` always pivots at
  **centre**, so the render put the name 230 units right of where the game puts it and ran the
  rank bar 176 units off the end of the card it lives inside. A render whose whole job is
  catching a widget hanging off its plate must not invent one.
- **The action bar's art:** `python Tools/make_utility_art.py --check` proves the five shipped
  PNGs — three icons, the shelf and one cell — are what the tool draws, and `--contact` shows the
  bar **assembled** plus the icons alone at both sizes they are drawn at. **Look at it.**
  Everything is *drawn*, so this gate needs no licensed pack and runs on every checkout; it is
  also the only thing that can see a flame that reads as a kite, or a cell that reads as a
  sticker (39c, 39d).
- **Prismvale's art:** `Tools/make_prism_art.py --check` proves every sprite, cast flipbook
  and flare is what the tool cuts out of the licensed packs, and `--contact` lays them out to be
  looked at. It reads the zips directly and **passes when the packs are absent**, so a checkout
  without them still runs the gate. It is committed with its first drop (owed item 18's lesson,
  taken before it cost anything).
- **Prismvale legibility:** `python Tools/render_prism.py` draws every shipped board at the
  size a phone draws it, with the real sprites, using `PrismScreen.HostInset` and
  `PrismView`'s own arithmetic; `--lit` plays a shortest answer first so the veins are on the
  board. **Look at it.** It is the only check that can see a lantern that reads as a crosshair,
  or a vein that does not read as one connected line - the first of those was caught here and by
  nothing else, past a green gate (invariant 36g).
- **Prismvale board sweep:** `python Tools/prism_sweep.py --template <name> --seeds N` deals
  colours into a designed field and prints par, `ways`, `nodes`, `careless`, `dealt`, `used`,
  `paired` and `idle` for every deal worth keeping; `--board "row,row,..."` measures one board,
  and `--par`, `--used`, `--dealt` and `--greedy` filter. This is how the two shipped boards were
  chosen (32d).
- **Gone with their modes** (invariant 38): `make_ember_art.py`, `render_ember.py`,
  `ember_sweep.py`, `make_march_art.py`, `render_march.py`, `march_sweep.py`, `verify/bud.py`,
  `verify/ember.py`, `verify/march.py` and `verify/bud-vectors.json`, along with
  `Tools/chapters/b01_thicket.py`, `b02_tanglewood.py`, `budforge.py`, `e01_emberforge.py` and
  `m01_hollowmarch.py`. `make_village_art.py --check` stays, because the village is a **world**
  and not a mode (30e) — the next mode set outside the grove costs one line in
  `Tools/chapters/mapart.py` and no art. So does `make_quarry_art.py`'s absence: it was never
  committed, so the cast art it cut is intact and tracked and nothing can prove it is what a tool
  would cut. That was the March cast and it is deleted now, which closes owed item 18 by removing
  the thing it was owed for rather than by paying it — the lesson stands and is why every art tool
  since is committed with its first drop.
- **Shop art and sound checks:** `Tools/make_shop_art.py --check` and `Tools/make_sfx.py --check` prove the
  shipped pictures and clips are what the tools would cut. **They prove reproducibility and say nothing about
  quality**, and that distinction shipped four broken cards — every check green over a coin sack whose fill
  had drained out through an undetected edge, a gem sack cut off at the waist, a chest with its lid sliced
  flat and crescents of leftover halo. Two numeric gates were tried and **neither separates a broken cut from
  a healthy one**, because a bite out of one side is not distinguishable by any global statistic from a thin
  part that belongs there. So `--contact` is the gate: a sheet laid out at the size a card really draws, and
  a page that *plays* the sounds at the pitches and repeat rates the game uses. Look and listen.
  <br>**All four were one fault, and it went away with the sheets.** The cut was a *colour* test on a
  painted sheet whose glow covered its objects in brightness *and* hue, so no threshold separated them;
  a flood inward from the tile's border that stops at painted edges fixed it, by having to be right
  about one closed curve rather than about every pixel. Both are now history: the shop's money is cut
  from a pack that ships its pictures already on transparency, so `make_shop_art.py` keys nothing at
  all. **The transferable half is that a keyer is a question about an edge, not about a colour** — and
  the reason `--contact` survived the tool it was written for is that neither cut nor pack can be
  judged by any number.
  <br>What the new source moved the risk to is **size**, and that is now the thing the tool proves: a
  rung is scaled by its own picture against the largest in its ladder, and a ladder whose sources do
  not ascend is refused by name (see 18e).
- **Store images for the consoles:** `python Tools/make_iap_art.py` draws one 1024x1024 PNG per
  in-app product into `GG-STORE-ASSETS/IAP - NEW`, named for the product id, **RGB with no alpha**
  and carrying no text, price or badge — the three things App Store Connect refuses on a
  promotional image. It is a tool rather than seventeen exported files because **a store image has
  to be the picture the card draws**: which one that is comes from tier through `ShopLadder`, so a
  typed list here would be a second copy of that arithmetic kept by hand, and its failure is a
  customer buying the pack above the one whose picture they tapped. It mirrors
  `StoreCatalog.RankShelves`, `ShopLadder.Rung` and `TokenPile.Of`, reads the products out of
  `progression.json`, and re-cuts from the pack rather than upscaling the shipped 512s. `--check`
  proves reproducibility and `--contact` is the gate that matters. **Nothing outside the repo is
  gated by a build**, so re-run it after any change to the store block or to `make_shop_art.py`.
- **Sprite name check:** `Tools/verify/artnames.py` proves every sprite and flipbook a *call site*
  asks for exists on disk. **It was written the day the gap cost something**: `MarchView.Boom` spelt
  its own folder out and asked for `Art/March/boom_fire` when the explosions live under
  `Art/Fx/March/`, so every burst threw an `InvalidKeyException` and drew a **white rectangle** two
  cells wide over the board (7b). Every other gate was green, because `AddressableAudit`,
  `Validate Art` and the address probe all prove what is *on disk* is addressed and that what a
  **mode declares** resolves — none of them proved that a name a call site asks for resolves to
  anything. **A player found it, which is the most expensive way there is.** It reads literals, so
  a name must be written rather than built (invariant 6's rule for loc keys, and `sfxnames.py`'s for
  clips); it follows a thin wrapper whose whole body is an `AssetManifest` lookup, because routing
  every lookup through one helper is what invariant 7 asks for and would otherwise make the check
  blind to the file that needed it most; and it takes the *first* argument's literals, which is why
  every art helper here takes its key first. A name that is genuinely built is **counted out loud**
  rather than failed — 74 of them, and that number is the honest size of what still goes
  unchecked. It rose by nine when the whole UI moved onto one skin table; what closes that gap
  is `SkinsTests`, which walks `Skins` by reflection and holds every constant on it to what
  `AssetManifest` preloads.
- **Sound and music name check:** `Tools/verify/sfxnames.py` proves three lists agree — what the code
  plays, what is on disk, and what `AssetManifest.Sfxs` preloads. A misspelled name was a runtime
  `InvalidKeyException` and a silence that shipped green. It reads **literals only** and scans
  `Presentation` alone. A screen's music `Track` is checked the same way and is the other half of the same
  bug (`ShopScreen` shipped `"hub"`, not a clip, on the one screen that takes money); a `Track` written in
  any shape but a literal or `null` is an **error** rather than skipped. Music is deliberately not
  preloaded. `Art.S`/`Art.Frames` have one now — `artnames.py` above, which is the same idea for
  sprites and was owed for months before a white rectangle on a player's phone paid for it.
- **Word list check:** `Tools/make_name_blocklist.py --check`; the filter itself is
  `npm --prefix firebase/functions test`.
- **Name fold check:** `Tools/verify/names.py` runs `GroveNames` against the shared vectors **on Unity's
  own Mono**, not the bundled .NET. That is the whole point: the first version ran on .NET 8, whose ICU
  agrees with Node, and passed happily with the Cherokee mapping deleted. A check that cannot fail is not a
  check (19e).
- **Why a test says "needs the Editor":** `GLIMMER_WHY=1 python Tools/verify/tests.py` prints the native
  call that stopped it — sometimes a fact about the code under test, sometimes a limit of the runner, and
  only the message tells them apart.
- **In the Editor:** `Glimmer Grove ▸ Validate Content`, `▸ Validate Art`, and Test Runner (EditMode).
  **Reload the domain before believing a failure that follows a play-mode session** — `Boot` starts the
  cloud backend and its threads, and those statics survive leaving play mode, so `AccountSwitchTests` fails
  on a main-thread violation raised by a sync that is still running, in a test that passes on every clean
  domain.

Builds are gated: `ContentBuildGate` fails the build on any content error.

## Hard-won facts

- **Addressables must be ≥ 4.0.1.** 2.x calls `Object.GetInstanceID()`, which Unity 6000.3+ made an
  *error*-level obsolete. 4.0.1 guards it behind `UNITY_6000_3_OR_NEWER`.
- **`GLIMMER_ADDRESSABLES` comes from asmdef `versionDefines`, not Player Settings.** Player Settings
  defines are per build target — one added on Standalone is absent on Android and iOS, which would ship a
  mobile build with no art and no error explaining it.
- **`m_BuildAddressablesWithPlayerBuild: 1`** is set explicitly in the project asset, not left to the
  per-machine Editor preference, so CI and teammates build identically.
- **A probe that writes off a whole class of failure as "expected here" cannot see a real one inside
  that class — and it will report success.** Hollowmarch's view was built in edit mode on all three
  boards before it shipped, and the probe counted **94 images with a null sprite**. That was written
  off, correctly and fatally, as "the asset scope is not loaded in a probe": every art-backed image
  was blank, the count matched exactly, and the reasoning was sound. Five of those blanks were a real
  bug — `MarchView.Boom` asking for `Art/March/boom_fire` when the explosions live under
  `Art/Fx/March/` — and a player met them as five `InvalidKeyException`s a level and a **white
  rectangle** two cells wide over the board at every burst (7b). A second probe then walked
  `MarchMode.Art` and reported **29 of 29 addresses resolve**, which was true and answered the wrong
  question: it checked what the mode *declares*, not what the view *asks for*. **When a probe's
  environment makes a class of failure invisible, the answer is a check that does not run in that
  environment** — here `Tools/verify/artnames.py`, which reads the call sites off the source and holds
  them to disk, and which reproduces this bug the moment it is put back.
- **A `[Serializable]` class field is never null after `JsonUtility`, so never test one for null.** It
  instantiates the field even when the JSON has no such key, so `dto.hollow != null` is true for every level
  ever parsed — which read all forty shipped glades as hollow and failed the Android build with eighty
  errors. The fixed shape is a value a real block cannot hold (`HollowDto.IsAuthored`), which is 11b from
  the other direction. `HollowTests` reads both shapes back through `ContentMapper` (Editor-only, because
  the serialiser is the subject) and `compile.py` refuses a null test on any class-typed DTO field. Nothing
  offline saw it, because Python's `json` returns nothing for a missing key where Unity returns an object.
- **`LevelDefinition.Layout` is null on a hollow, and nothing in the language says so.** Nullable
  reference types are off, so a reader that forgets is a `NullReferenceException` in whichever tool touches
  it first — which is what shipped: `ContentValidation` and `ContentAuthoring` both printed
  `level.Layout.Width`. `compile.py` refuses a file that reads `.Layout.` without anywhere saying it knows
  the thing can be absent. Coarse on purpose: a false positive costs one word, a missing guard costs a crash.
- **A Unity magic method is an engine rule, not a language rule, and the offline compile is blind to it.**
  `public bool Awake(int i)` on a `MonoBehaviour` compiles perfectly in Roslyn and the Editor then refuses
  the whole script, so a green `compile.py` is not by itself proof the Editor will accept a build.
  `compile.py` walks every class that ends up a `MonoBehaviour` and refuses a method named after one of the
  no-argument messages; the parameterised ones are deliberately not listed, or the check becomes noise.
- **A vector file that only the Editor can read is not a guard on the rule it pins.** Every `*VectorTests`
  loads its JSON through `JsonUtility`, a native call, so the offline runner reports the whole fixture as
  "needs the Editor" — the one gate nobody runs on the way past. Budburst's wash rule drifted from its
  mirror and **every offline gate stayed green**, because the mirror is a *different copy* that happened to
  be correct; what noticed was `ContentBuildGate` refusing to prove par at all, twenty minutes into an
  Android build. So a mode whose rule exists twice needs at least one fixture pinning a **shipped board
  inline** — `BudLadderTests`, `FallLadderTests`, `KeeperLadderTests` — because that runs offline and is the
  copy-versus-copy comparison that actually happens. Two smaller lessons from the same bug: **a flood fill's
  `_seen` array means *visited*, not *chosen***, so a guard meaning "skip a flower that is itself bursting"
  also covered every flower scanned as part of a group that was *discarded*, and the wash stopped in one
  direction and not the other purely by index order; and **par is a bad canary for a rule that got
  stricter**, since a wash stopping early makes a board harder and par coming out one higher looks exactly
  like a level somebody authored — what cannot look plausible is the best opening tap moving cell and taking
  three fewer flowers, which is why `BudLadderTests` pins that too.
- **`'' in 'RGB'` is `True` in Python, so an empty cell reads as a bloom.** Every offline mirror in this
  project tests a cell against a string of letters, and every one of them is a substring test rather than a
  membership test the moment the cell can be empty. It shipped in `proto.py`: the hole a burst had left read
  as a bloom, so `Runs` emitted ribbons over empty ground. **Par did not move** on the shipped board — what
  moved was `ways`, `nodes` and the careless reading, which is the half nobody would have looked at. The C#
  copy could not have the bug at all (`IsBloom(char)` compares three characters), so the only thing that
  could see it was the two copies being compared: `ProtoLadderTests`, which holds the shipped boards inline
  because a vector file needs `JsonUtility` and is therefore skipped by the offline runner. **A mirror is
  only as good as the fixture that compares it, and a fixture that needs the Editor compares nothing.**
- **Nothing that decides a cell may be a `float`, because the runtimes disagree about them.** A generator
  capped a walk at `(int)(free / (float)walksLeft * 1.3f)`; thirty free cells across three walks computes
  12.99999952…, which truncates to **13** in single precision and to **12** once promoted to double. Both
  are legal for a C# compiler and the runtimes chose differently — .NET 8 said 13, Unity's Mono said 12 —
  so one board was *two different boards* depending on who dealt it, and a phone runs IL2CPP, a third code
  generator again. Every mode shipping now authors its board in the file and searches it in integers.
- **Nor may a float decide a *threshold*, and that one shipped.** `1.20f` is 1.20000004768…, so
  `Mathf.CeilToInt(45 * 1.20f)` is **55** where `par × 1.20` is exactly 54. Every runtime is wrong the
  *same* way, so no cross-runtime diff could find it — it disagrees with arithmetic rather than with another
  runtime, and only the offline mirror, which had always used integers, ever noticed. Four glades granted a
  turn more for three stars than the design says, for as long as the three lines have existed. `LevelTuning`
  holds each factor as hundredths (`GoldHundredths`) and derives thresholds with `(par * n + 99) / 100`; the
  floats are what an author writes, and nothing that produces a graded number reads them. `CheckStarBands`
  compares the hundredths for the same reason. Survivable only because stars are stored and only promoted.
- **A flood keyer is the wrong tool for an object whose *edge is a glow*, and it fails by keeping
  almost nothing.** The shop's old sheet keyer walked inward from a tile's border and stopped at
  painted outlines, which is exactly right for a bomb with an ink outline and exactly wrong for a crackling
  energy orb: the flood walks straight through the soft edge and takes all but the fuse. Measured on
  the same sheet, one bomb kept 852,000 pixels and the orb beside it kept **29,000** — a difference
  so large it reads as a missing file rather than as a bad cut, which is the only reason it was
  noticed at all. The repair is a *different question*, not a better threshold: distance from the
  plate, keeping the halo, because the thing being cut out **is** a glow and a prism with a hard
  edge is a marble. Both lived in `make_quarry_floor.py`, one line apart, chosen per object;
  that file went with the Iron Quarry and the shop's keyer went with its sheets, and the lesson is
  half of why Hollowmarch's board sprites were *drawn* rather than keyed out of a sheet at all —
  and the whole of why the pack that replaced those sheets was chosen for shipping its pictures
  already cut.
- **A constant is invisible to `artnames.py`, so replacing a literal with one silently gives up
  the gate.** That check reads *literals* at a call site — `Art.S("Ui/ic_gem")` — which is exactly
  what a named skin is not. Routing the storefront's sprites through a skin table took thirteen
  names out of its sight in one change, and it says so: the count it prints of names that are
  **built rather than written** went from 48 to 65, and then to 74 when the whole UI moved onto
  one table. The fix is not to stop naming things — a table of skins is right, and `Skins` has
  been one for a year — it is to close the chain somewhere else. `SkinsTests` walks `Skins` **by
  reflection** and holds every string constant on it to what `AssetManifest` actually loads; the
  manifest's own entries are literals, so they are already held to disk. Reflection rather than a
  written list, because a list is a second copy of the table that goes stale the moment somebody
  adds a piece — and a skin nobody checked is exactly the skin that draws nothing. (The walk
  asserts its own size for the same reason: a reflection walk that finds nothing passes every
  assertion inside it.) **Whenever a name stops being a literal, read that count**: it is the only
  thing that reports the loss, and the loss is a white rectangle on a player's phone
  (invariant 7b).
- **A licensed pack's preview sheets carry the vendor's own dummy lettering, and grading it makes it *less*
  obvious rather than more.** One backdrop was cut from a flat panel with two blocks of placeholder text on
  it; reduced to luminance, blurred and graded, the words came through as two dark smudges that read as
  *something painted*. It imports, addresses, audits and validates, because the content gates never open a
  PNG. **Look at a source at the size the game draws it before naming it in `chapter_art.tsv`**, and prefer
  a pack's `layers/` art to its `_preview` sheets.
- **A VFX pack's `Textures/` folder is two different things with one naming scheme.** Half is what a
  particle *draws* (a flash, a flare, a fire flipbook); half is what its shaders *sample* (noise fields,
  gradient ramps, dissolve masks, LUTs). Both are white-on-black squares, the names do not separate them,
  and a UI `Image` draws either happily. Budburst's first cut took a colour ramp for its flare, a bubble
  mask for its bolt and a streak-noise field for its shockwave; all imported, addressed, audited and drew.
  **Before naming a texture after the effect you want, render it.**
- **`PlayerPrefs.Save()` serialises the whole store synchronously, and a preference written on
  *arrival* is written far more often than it changes.** The map writes two — the mode and the chapter —
  on every entry, so a screen transition was two full flushes of every preference on the device, almost
  always to store what was already there. But the flush cannot simply be dropped: Unity persists
  `PlayerPrefs` by itself during `OnApplicationQuit`, which on a phone is the ending that rarely happens —
  an app is backgrounded and later killed by the OS — so a preference relying on a clean quit fails to
  stick for most of the people who set it, and it fails looking like a feature that never worked.
  `DevicePrefs.WriteString` is the resolution and the only place a preference is written: compare first,
  flush when it really changed. It reads the store rather than a remembered last-written value, because a
  shadow copy that disagreed with disk would skip the one write that mattered.
- **A `[UnityTest]` that counts frames while the code under test counts milliseconds passes only at a
  frame rate nobody promises.** `AccountDeletionTests` waited 600 `yield return null`s for a latch that
  polls every 50ms; under `-batchmode -nographics` frames are unthrottled, so the whole budget could
  elapse inside one poll — green on a cold machine, red on a warm one, and the failure sentence names the
  code under test rather than the runner. Yield frames (the continuations need a pumping main thread) but
  bound the wait on `Time.realtimeSinceStartup`. The sibling trap: `Flow.Dismiss` ends a panel with
  `Object.Destroy`, which is correct in a build and **refused in edit mode** with an error log that NUnit
  fails the case on — declare it with `LogAssert.Expect` rather than teaching shipping code to branch on
  `Application.isPlaying`.
- **The ads plugin's Editor consent stub sets `Time.timeScale` to nought and leaves it there, and
  nothing in this project writes `timeScale` at all.** Found driving Nova Raid through the bridge: the
  swap tweened, then the burst never came, and `Time.time` had advanced four seconds since boot. The
  Google Mobile Ads stub form ("Welcome to AdSamplerTestApp") pauses the game while it is up, and in the
  Editor nobody dismisses it. So invariant 30h's diagnosis was this rather than a lesson modal, and the
  rule it leaves is stricter: **a board coroutine waits in real seconds**, because every tween it waits
  on runs on the unscaled clock (`Tw.unscaled` is the default) and a scaled wait desynchronises from
  them the moment anything touches the scale. Set `Time.timeScale = 1f` through `execute_code` before
  driving a board in the Editor.
- **Unity only re-resolves packages and reimports on window focus.** If a change seems not to apply, the
  Editor probably has not been clicked.
- **And `refresh_unity` says `compile_requested: true` without compiling anything**, which is worse than
  not asking: a probe driven through the bridge then runs the *previous* assembly and reports a fault that
  was fixed minutes ago, or — far worse — passes a check against code that is no longer there. It cost
  three rounds of chasing a drag-direction bug that had already been fixed on disk; what settled it was
  comparing timestamps, `Library/ScriptAssemblies/GlimmerGrove.Presentation.dll` against the `.cs`. The
  reliable force is `UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation()` through
  `execute_code`. **Before believing anything a bridge probe tells you about code you just edited, check
  the DLL is newer than the file.**
- **An Editor launched from the Hub gets a minimal `PATH`, and one failed post-processor abandons the
  rest.** Measured on macOS: `/usr/bin:/bin:/usr/sbin:/sbin`. Homebrew on Apple Silicon installs to
  `/opt/homebrew/bin` while EDM4U's iOS resolver searches the process `PATH` plus the *Intel*
  `/usr/local/bin`, so it cannot find `pod` on any Apple Silicon Mac. EDM4U runs `pod install` from a
  `[PostProcessBuild]` at order 4, and **Unity abandons every remaining callback when one throws**, so it
  also took down `IosPrivacyPlist` (order 100) — the only writer of `NSUserTrackingUsageDescription` and the
  only thing linking `AppTrackingTransparency.framework`. The result is an Xcode project that looks complete
  with no `.xcworkspace`, no tracking prompt, and a link error twenty minutes into an Xcode build naming
  Apple's classes rather than CocoaPods. `MacToolPath` fixes the cause in-process and `IosWorkspaceGuard`
  proves it happened. **Corollary, and general: a post-processor ordered after another is not guaranteed to
  run** — ordering expresses *dependency*, never *safety*.
- **Two Google ads SDKs cannot share an APK, and a mediation adapter can drag in the second one.** The
  legacy `play-services-ads` and the next-generation `ads-mobile-sdk` both define
  `com.google.android.gms.ads.*`, so Gradle stops at `checkDebugDuplicateClasses`. This project holds the
  legacy one permanently, because the GoogleMobileAds plugin is how `UmpConsentGateway` gets UMP — so the
  *adapter* must bend. LevelPlay's AdMob adapter switched at **5.19.0.0**; **5.18.0.0** is the last on the
  legacy SDK and is what `ISAdMobAdapterDependencies.xml` must pin. The Network Manager keeps offering the
  newest, so the version is a decision, not a default, and the failure is twenty minutes into a Gradle run.
- **Sign in with Apple on iOS cannot use the generic IDP path, and the refusal kills the process.**
  `FirebaseAuth` calls `fatalError` the moment `apple.com` reaches `FederatedOAuthProvider`; a Swift
  `fatalError` is not an exception, so no managed `catch` runs and the app dies on the tap. It ships looking
  correct because Android — where the generic path *is* allowed — works. Hence
  `Assets/Plugins/iOS/GlimmerAppleSignIn.mm` and `AppleSignIn.cs`. Two details that cost a day each:
  `LinkCredential.AccessToken` is **not** unused by Apple — Firebase's fourth `GetCredential` parameter is
  named after Google's access token but for `apple.com` must carry Apple's `authorizationCode`, and a
  credential without one is refused with **the same sentence** a malformed token or mismatched nonce gets
  (hence `AppleSignIn.Describe`); and the **entitlement is written by `IosAppleSignInBuild`, not by Xcode**,
  because Unity rewrites the whole project on every build, so an entitlement Xcode added vanishes on the
  next one. It is a **paid-account** capability.
- **A Functions secret is pinned at deploy time, so a correct key can produce a 401.** Functions v2 records
  the secret *version* in the function's config, so `functions:secrets:set` changes nothing until the
  function is redeployed, while `functions:secrets:access` reads *latest*. **Redeploy every function that
  names the secret**, and **destroy old versions** once nothing uses them (`secrets:prune` will not, because
  it counts by name rather than version). Reading `redeemPurchase` logs: a 401 from the production App Store
  endpoint followed by success on the sandbox one is the **normal** path for a sandbox purchase; the failure
  is both endpoints refusing.
- **A newly created 2nd-gen callable has no public invoker binding and answers every call with 401.**
  `firebase deploy` neither does this nor warns about it, and from a client it is indistinguishable from
  being signed out. Existing functions keep the binding they were created with, so only the new one fails:

      gcloud run services add-iam-policy-binding <lowercased-name> \
        --region=europe-west1 --member=allUsers --role=roles/run.invoker

- **The sprite atlas file extension selects the importer.** A `.spriteatlas` written in the V2 format
  imports as editor data with a plain `AssetImporter` and produces no `SpriteAtlas` at all — every address
  resolves, every check passes, and the shop draws an empty grid. It must be `.spriteatlasv2`, with
  `EditorSettings.spritePackerMode` set to `SpriteAtlasV2`.
- **Deleting art leaves its Addressables entry behind, and that fails the build rather than the game.**
  `AssetDatabase.GUIDToAssetPath` keeps answering with the old path for a while after a file has gone, so
  `DropMissing` used to keep every entry of a deleted folder. Nothing at runtime cared; `BundleBuildContent`
  does, throwing `Asset '…' is not a valid Asset or Scene` while `BuildPlayer` prepares, so the Android
  build dies with one file name buried in a stack trace of package internals. `DropMissing` also asks
  `AddressableRegistry.StillThere` (`GetMainAssetTypeAtPath(path) != null`), and `AddressableAudit`
  **errors** on any registered entry whose asset has gone — because every other gate looks *outward* from
  what the game requests, and all of them were green with twenty-five dead entries in the global group.
  **And a group the Editor has repaired is not a group on disk**: dropping an entry marks the group asset
  dirty and nothing more, so the Editor is correct while the file still carries the dead entry until
  something calls `AssetDatabase.SaveAssets`. **Save after a repair.**
- **The importer hook does not address art copied in while the Editor is closed or mid-reload**, which is
  every run of the art import tools. Unaddressed sprites load as nothing and cells draw blank.
  `Glimmer Grove ▸ Addressables ▸ Sync All Assets` is the repair; `AddressableAudit` stops it shipping.
- **A preprocessor fires on first import only**, so art that landed before an import rule changed keeps
  what it was given, silently — hence `▸ Reapply Art Import Rules`. **It must batch**: `SaveAndReimport` per
  texture is one round trip to Unity's import workers each, and 335 back to back crashed both workers and
  wedged the Editor in a domain reload it could not finish. Use `StartAssetEditing`/`StopAssetEditing` with
  a `finally` — not optional, since an exception between the two leaves the asset database in editing mode,
  which looks exactly like the freeze it prevents. Texture caps are **per folder** (`ArtImportRules.Caps`):
  512 grove props and companions, 256 critter frames, 1024 UI, 2048 only backdrops and map strips. A texture
  costs its dimensions, not its file size.
- **`JsonUtility` has two parse refusals that read as logic bugs.** It rejects a number written `.5`, and it
  **truncates a string at an escape sequence** — which shifted every expectation in a shared vector array by
  one field and read exactly like a bug in the code under test. Test vectors carrying awkward text carry
  **code points** alongside the string, and the other runtime asserts the two agree. Related:
  `[Serializable]` is silently load-bearing — an insertion separating a DTO from its attribute makes
  `JsonUtility` return `null` for that array, and the tests driven by it stop running rather than failing.
- **The Firebase Unity SDK's `Firebase.Functions` ships as source with its own asmdef**, so
  `GlimmerGrove.Cloud.asmdef` must reference it explicitly (App, Auth and Firestore are plugin DLLs and
  auto-reference), and that source needs `Google.MiniJson.dll` from the **app** package. All Firebase
  packages must share one version.
- **Google's UMP plugin must come from OpenUPM as a package, never as the `.unitypackage`.** The
  `.unitypackage` unpacks as loose files under `Assets/`, so it is not a package, so it carries no version,
  so `versionDefines` never fires, `GLIMMER_UMP` is never defined, `UmpConsentGateway` compiles to nothing
  and **nobody is ever asked anything** — a consent failure that is completely silent.
  `GooglePackages/fetch.ps1` pulls `com.google.ads.mobile` from OpenUPM beside the Firebase tarballs.

## Current state

*What is true now*, not how it got here. The reasoning behind a rule lives in **Invariants**; the traps
live in **Hard-won facts**.

### Built and verified

- **Content pipeline** — levels as data in `StreamingAssets/Content/`, stable `LevelId`s, manifest-built
  `CatalogIndex`, lazy chapter bodies, `Content ▸ Sync Manifest`, build gate.
- **Save** — versioned atomic file with checksum, backup rotation, corrupt-file recovery, tested
  migrations, monotonic merge. **Save schema v23.** Content schema: manifest and chapter bodies **v2**,
  grove body **v3**.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google linking,
  per-account local archive for switching, `SyncScheduler` debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water floors only.
  Hearts and hints are produced/spent ledgers (`RegenLedger`). Levels chain inside a chapter; chapters open
  on stars (`LevelUnlock`, invariant 21). A mode's opening levels are free to fail (`HeartStake`).
- **Retention** — daily chests, streak (collected by hand), golden glades, event calendar, percentile
  standings, per-glade records (turns).
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads, refund sweeps,
  server-adjudicated grants, a gem-priced continue on a lost run (23) and a bonus wheel on the victory
  panel's video offer (25), neither costing the save file a field.
- **The Grovement** — 14x14 isometric tile floor drawn as a ground layer under a piece layer, pieces on
  authored footprints (the hall and every dwelling 2x2, nine structures and paths 2x2 or 1x2), taps
  resolved through per-piece hit masks, land sold one rung at a time up an authored ladder — the
  three smallest stretches for credits and the five biggest for gems (16j) — decor bought by the
  copy, residents projected from the companion roster, derived grove worth.
- **Boards** — public `groves/{uid}` cards, published rank distribution, unique keeper names with
  server-side filtering and reporting. A card is rebuilt about fifteen seconds after its owner changes
  the grove while online (a three-second sync debounce, then a ten-second publish debounce), or on
  their next launch; the cost grows with decorating, never with playing (19j).
- **The one live mode, and the three hidden ones** (invariant 38) — the game a player opens today is
  **Thornwatch** and nothing else: `s01_thornwatch` (ten rungs, invariant 37) on the ordinary
  ladder, and `s02_endlesswatch` on an **Infinite** track beside it (invariant 43), reached
  through a second pill under the chapter plaque. The map draws no *mode* switcher, because there
  is one mode; it draws the **track** switcher, because there are two ladders. It is the front
  door by construction as well as by decision (38a): it is the only row `CatalogIndex.Modes`
  has, so it is what a map with nothing remembered opens on. It is the first mode here that runs
  on a **clock**: raiders walk down a hill at a line of coloured wards, and a match is worth only
  the colour it was.
  <br>**The classic glade (`c01_shallows` … `c04_nightbriar`), Lightfall (`f01_lightfall`,
  `f02_glasswater`, `f03_whorlwater`) and Prismvale (`p01_prismvale`) are *hidden*, not deleted**
  — `"disabled": true` in the manifest and nothing else, so every board, screen and mode class
  still stands and eight booleans put them back. A disabled chapter is skipped in silence by
  `CatalogIndexBuilder.Add`, and a mode with no chapters is left off the switcher by
  `CatalogIndex`, so hiding costs no code at all.
  <br>**The map's mode switcher is therefore not drawn, and that took no code either.**
  `ModeSwitch.Build` returns null when the catalog holds one mode — the case it was written
  around — so the pill, its menu and the first-run lesson pointing at it all fold away together,
  and `Mechanic.ModeSwitch` is deliberately *not* marked seen while the control is absent, which
  is what keeps the lesson spendable the day a second mode is turned back on. **Removing the
  control by disabling the second mode rather than by deleting the call is what keeps two other
  things working**: a device that remembers `prism` self-repairs, because `ModeChoice.Read`
  refuses a mode this catalog has no chapters for and falls back to `DefaultMode`; and the
  `GLIMMER_BENCH` row that reaches `Dev.VfxDemoScreen` is still reachable, since it is the one
  row that draws with a single mode.
  <br>**Budburst, Hollowmarch and Emberforge are deleted** (38), and their mode, chapter, level and
  lesson ids are all spent. So are **Deep Orbit and Moonwake** (30), **Nova Raid and Toppleglen**
  (31), **the Iron Quarry** (32), **Kindlewake** (35), **Groovekeeper** (28) and four of the five
  prototypes that took its slot — Nectarrun, Ribbonfall, Seedfling and Warrenwake (29). Lightweave
  and Ripplewake are retired too; `weave` and `ripple` are spent mode ids.
  <br>**The Hollow does not ship and never did.** `LevelModes` registers exactly four, in the
  order the switcher offers them — `SiegeMode`, `PrismMode`, `GladeMode`, `FallMode` — there is no
  `HollowMode`, no `KeeperMode`, no
  `BudMode`, no `MarchMode` and no `EmberMode`, and `h01_emberfall` is in neither the manifest nor
  the code. What survives is the *level shape* (`HollowDto`, invariants 20c–20e), which is why the
  rest of this file still talks about hollows: those entries are design rules, not a shipping mode.
  <br>**Read the manifest, never this table, when a number reaches a customer.** That rule was
  bought on 2026-09-02 by a store draft claiming five modes and a hundred levels "across eleven
  chapters" when the truth was four modes and ten chapters. What the manifest says today is **four
  modes and nine chapters, of which one mode and one chapter are enabled** — ten levels a
  player can reach. Everything else is on disk behind a boolean.
  <br>**Prismvale is the twelfth mode the prototype level shape has carried and the only one still
  on it** (hidden, not deleted): a grid of letters, a deal, a searched par and a slack, so `ProtoGrid`, `ProtoSearch`,
  `ProtoRun`, `ProtoVerdict`, `ProtoView` and `ProtoScreen` are all inherited and the mode supplies
  a board (`PrismBoard`), a look, a validator and a view. `ProtoDto.cores` — the **deal** — is
  wanted by none of them now and stays, because the block has always described itself as carrying
  one. `StoryScreen`, the **story** band (30d) and the **village world** of backdrops (30e) all
  survive with no mode using them, which is what a seam is for.
- **Utilities** — an account-wide action bar filling the foot of Thornwatch's screen
  (`UtilityBar`, a 228-point shelf of five cells, four of them filled), dropped by daily chests
  and bought with gems on a shop shelf of their own (39h), charged against the graded count at the mode's own exchange rate so one
  can never buy a star (39). A firepot is aimed at one box of the hill's own grid (39f). Two
  monotonic counters per id in the save (v22), catalogued in `progression.json`, shelf, cells
  and icons all drawn by `Tools/make_utility_art.py`.
- **The whole front of the game** — restyled three times on 2026-09-09, the last onto the
  **cartoon UI mini kit** (`Skins`, `Art/Ui/Hud/`, cut by `Tools/make_hud_kit_art.py`):
  saturated two-tone faces inside one heavy navy keyline, ribbons with tails, discs in a white
  ring. Eight pill colours, six square colours, navy plates sunk into their own material, a
  navy trough with a tintable fill, a cloth title ribbon, lit and unlit nav caps as **discs**,
  a drawn gold-and-navy plinth with a drawn beam — and **a world behind all of it**, the
  level-map pack's floating islands composed, blurred and graded in the tool.
  <br>The first two moves cost one file each. This one cost that plus the two screens'
  composition, because the complaint was not only the art: a plate that is a rim around the
  ground colour reads as an outline whatever it is cut from (44h). See `Skins`.
  <br>**The merchandise stayed where it was**, and the rule about furniture and goods coming
  from one hand is not being broken so much as read properly. What that rule is about is two
  things drawn in different *registers* on one card; the coin and gem piles are the same
  register as this kit — chunky, heavy keyline, saturated — and they are a different *kind* of
  object, treasure inside a machine, which is exactly what the pack's own store mockup does
  with its energy cans on teal panels.
  <br>**A shelf stopped being a colour and became a tab.** Three coloured card frames across
  five shelves was how the storefront said which shelf you were on, and it cost the screen its
  material: five saturated blocks side by side read as five games rather than as one shop. Every
  card is the kit's one teal plate now; what says the shelf is the **lit tab** — which the old
  chips, nearly flat and nearly black, could not say at all — and a coloured light under the
  goods (`Skins.Accent`), where a player is already looking. It also fixed the yellow-bar-on-a-
  yellow-card fault *by construction* rather than by a table: the price is orange on teal on
  every shelf.
  <br>**The hub's whole reason for being rebuilt is the lander.** Its centrepiece was a
  grass-topped rock borrowed off the map — a piece of a screen that is not this screen — and
  the kit ships a lit pad with a beam, which is a *place to stand*. The companion stands on it
  under its own name plate, which the hub never had: a player who bought one met it as a
  picture that had changed.
  <br>Three faults that only a render could see and all three were shipped-shaped: a nine-sliced
  trough whose two clips left **fifty units of interior for a five-digit number** (upscaling a
  sliced sprite makes its unstretchable ends *bigger*); a beam long enough to wash over the two
  cards above it; and the pack's own junction box sitting directly behind the one object on the
  hub a player is meant to look at, which is why the room is cut to portrait and blurred in the
  tool rather than cropped by offset in the screen.
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).

### Content shipped

| Chapter | Mode | Levels | Par range | `budgetFactor` | Subject |
|---|---|---|---|---|---|
| `c01_shallows` | glade *(hidden)* | 10 | 10–50 | none, then default | the verb, then colour, blending, rooted stone, brittle stone, taproots, pockets of colour |
| `c02_millvale` | glade *(hidden)* | 10 | 41–63 | default 1.60 | the crossing |
| `c03_amberwood` | glade *(hidden)* | 10 | 44–70 | default 1.60 | colour as the subject; no new rule |
| `c04_nightbriar` | glade *(hidden)* | 10 | 44–69 | default 1.60 | the briar |
| `f01_lightfall` | fall *(hidden)* | 10 | 2–6 drops | none, then par + 5 (motes) | the cook, then the chain; motes 3 → 30, headroom 4 → 2, `ways` never above 8 |
| `f02_glasswater` | fall *(hidden)* | 10 | 3–6 drops | par + 5 (motes) | the lens, charged and fired; motes 5 → 33, glass 1 → 3 panes, channels asked for 1 → 6 |
| `f03_whorlwater` | fall *(hidden)* | 10 | 2–5 drops | par + 5 (motes) | the whorl: the only place two *motes* are combined. Motes 4 → 26, headroom 4 → 2, whorls 1 → 2, `ways` 1 → 16, greedy beaten on nine of ten |
| ~~`k01_grovekeeper`~~ | ~~keeper~~ | — | — | — | **retired** (28) — withdrawn as boring; its ids are spent |
| ~~`t01_toppleglen`~~ | ~~topple~~ | — | — | — | **retired** (31) — withdrawn after play; its ids are spent |
| ~~`n01_nectarrun`~~ | ~~nectar~~ | — | — | — | **retired** (29) — withdrawn after play; its ids are spent |
| ~~`r01_ribbonfall`~~ | ~~ribbon~~ | — | — | — | **retired** (29) — withdrawn after play; its ids are spent |
| ~~`s01_seedfling`~~ | ~~fling~~ | — | — | — | **retired** (29) — withdrawn after play; its ids are spent |
| ~~`w01_warrenwake`~~ | ~~warren~~ | — | — | — | **retired** (29) — withdrawn after play; its ids are spent |
| ~~`h01_emberfall`~~ | ~~hollow~~ | — | — | — | **never shipped** — no file, not in the manifest, no `HollowMode` in `LevelModes`. Kept as a row so nobody re-adds it from memory |
| ~~`b01_thicket`~~ | ~~bud~~ | — | — | — | **deleted** (38) — Budburst withdrawn with Hollowmarch and Emberforge; its ids are spent |
| ~~`v01_harvester`~~ | ~~nova~~ | — | — | — | **retired** (31) — withdrawn after play; its ids are spent |
| ~~`q01_ironquarry`~~ | ~~quarry~~ | — | — | — | **retired** (32) — withdrawn without ever being played; its ids are spent |
| ~~`m01_hollowmarch`~~ | ~~march~~ | — | — | — | **deleted** (38) — Hollowmarch withdrawn; its ids are spent |
| ~~`b02_tanglewood`~~ | ~~bud~~ | — | — | — | **deleted** (38) — Budburst's second chapter; its ids are spent |
| ~~`k01_kindlewake`~~ | ~~kindle~~ | — | — | — | **retired** (35) — withdrawn after play: the verb was not the one commissioned and the animation followed from that. Its ids are spent |
| `s01_thornwatch` | siege | 10 | 11–53 matches | none — the ward line is the fail state | raiders come down the hill at four coloured wards; match a colour and that ward fuels up and opens fire, and a bolt is worth double against a raider of its own colour. Fuel leaves a ward as a bolt and no other way (37c). Every rung is an 8x5 field with no move allowance (24); waves come on a clock, or early if the hill is cleared (37k). **The ramp is what is coming**: rung 1 is twelve creepers and nothing else, rung 2 brings the **cog** (37w), rung 3 the **blightcaller** (37z), rung 4 sends waves of one colour at a time, rung 5 the **warlord** (37t), rungs 6–9 turn the hill from creepers to brutes (0 → 18), rung 8 the **warbringer**, and rung 10 ends on the **overlord** (37x). **Four bosses and four different fights** — a douse, a smite, a rally and a sunder, in four colours, from four packs, at four sizes. All four hold the middle of the hill and walk on in 2.7–4.1 seconds. Cogs 3–4% from rung 2, which is where they stop maxing the line. Par is not monotonic — it dips at rung 5 — and an unhurried player clears every rung inside the three-star line with all four wards standing, the most-bled lines being the warlord's rung and the finale's at 46–47 of 56 (37j) |
| `s02_endlesswatch` | siege *(infinite track)* | 1 | 3★ at wave 20 | none — the ward line is the fail state | **the Infinite Watch.** The same hill, the same wards, the same verb, and waves that never stop. What comes at wave *n* is a rule rather than a list (`SiegeEndless`): a boss every fourth wave to sixteen — blightcaller, warlord, warbringer, overlord — then every unordered **pair** of the four every fifth wave, which is thirty waves before anything repeats. Brutes from wave 3, bulwarks from 6, a weaver from 9, a thief from 13; health climbs 12 tenths a wave and a blow 4. Graded on how far it got rather than on what was spent (`LevelTuning.Climbs`), so three stars is wave 20 and two is wave 11 — **both guesses until somebody plays it** |
| `p01_prismvale` | prism *(hidden)* | 2 | 3–4 swaps | none, then par + 3 | drag a gem onto its neighbour and the two change places; a lantern feeds the gems of its own colour touching it, that colour runs on through every matching gem beside them, and a critter standing against the vein wakes. Nothing is ever spent, so a vein can be **broken**. 6x6, critters 2 → 3, lanterns 2 → 3, `ways` 10 → 120, `dealt` 2 → 3 of 25, `used` 2 → 3, greed beaten on the second. The first rung cannot be lost (24) |
| ~~`e01_emberforge`~~ | ~~ember~~ | — | — | — | **deleted** (38) — Emberforge withdrawn; its ids are spent |

**No level authors a difficulty number except the first glade in the game, and no chapter authors a clock**
(invariant 22). Par is derived from the board; both star lines and the losing line are multiples of it —
1.20, 1.40, 1.60, even thirds of the slack. Glade one turns the budget off entirely: nine tiles and three
critters, and a lost heart in the first minute is the most expensive heart in the game. A per-chapter budget
ramp was tried and removed — the budget is a fail line, and difficulty is the boards' job (5d).

Par is **never** monotonic within a chapter — par is length, not difficulty, and ten rising numbers read as
a treadmill. A chapter's dip is its taproot board (one tap moves several conduits and par charges once): the
Shallows at glades five and nine, the Amberwood at `c03_rootbound`, the Nightbriar at `c04_rootbriar`. Mill
Vale's used to be `c02_braided_water` and is not any more — that dip was the board being dealt partly solved
(5g), and par is now roughly 1.2–1.35× a board's turnable tile count on every glade.

Chapter art is generated and **shared by ordinal** (7c): `Tools/chapters/*.py` regenerate the shipped
JSON and self-check against it, `Tools/chapters/mapart.py` says which map a chapter draws and which sky
each of its levels does, `Tools/make_chapter_art.py` cuts one ordinal's map strips and
`Tools/make_sky_art.py` the forty shared skies. Four maps (6, 4, 5 and 6 strips) and forty skies serve
every chapter of every mode, and a new chapter needs neither. See `CRAFT.md`.

**A board backdrop is graded in daylight**, and the board is what makes that safe: every mode draws its
board on an opaque plate, so brightening what is behind it *widens* the separation rather than closing it.
The plate itself stays dark, deliberately — the tiles, motes and flowers on it are bright saturated shapes,
so their ground is what the backdrop is free *not* to be, and anything tempted to lighten it is changing a
contrast ratio in five modes at once. `vivid` turns a picture onto a target colour — the sky's place in
the forty-colour ladder now, its level's authored accent before (7c); the three attempts it took, and the
four rules that keep it a painting rather than a tint, are in `CRAFT.md`.

### The board's vocabulary

**The wheel is paint, not light** — the middle channel is drawn yellow, so the blends fall out of the wheel
a five-year-old knows (red+yellow orange, red+blue purple, yellow+blue green) while `Energy` still mixes by
`|` over three bits and the authored letters `Y`, `M` and `C` still name the masks. `Pal.EnergyColour` is the
one place that says what each mask is *painted*; see `CRAFT.md` for why, and for the colour-blindness cost.

One verb — turn a conduit, light a critter — with modifiers, and no second solver:

- `~` **brittle stone** — survives a fixed number of turns. Belongs on a tile the player cannot simply try,
  so in practice a crossing.
- `!` **rooted** — cannot be turned. Authored at `/0` (5c).
- `&A` **taproot** — every conduit carrying the rune turns as one; charged once in par.
- a **pocket** is not a tile — it is the shape that replaced the duskcap (5f): a heart and a critter of
  another colour behind a ford, where the ford sits on a *cycle* of the live network so the wrong turn costs
  the grove nothing and the pocket everything.
- `=NS+EW` **crossing** — two strands through one tile that never meet. Straight is inert; twisted is worth
  exactly one tap. No hub disc.
- `%NS+EW` **briar** — four arms drawn, two conducting; one tap swaps which. Order of the pairs matters
  (unlike a crossing). Straight is worth one tap, twisted four.

`Tools/verify/difficulty.py` says whether any of that is doing work (5d). `hazards` is the metric it replaced
and is wrong; `arms`/`wins`/`glance`/`colour`/`dealt` are the ones to author against.
### The numbers

Free play collects about **593 credits and 6 gems a day**; `Tools/verify/content.py` and
`Validate Content` both derive and print this, so never hard-code it.

- **Companions** — 31, one free (`monarch`, the starter), 30 priced 800 → 30,000
  (~270,500 total). Unlock is keeper level **and** purchase.
- **Grove catalog** — 436,270 credits **and 6,200 gems** complete: 154,770 decor and homes,
  11,000 credits + 6,200 gems of land (9 regions, a free 6x6 starter), 270,500 residents.
  150 priced pieces, of which 99 sell in bundles of ten at what one used to cost. Home
  ladder 5 rungs, first free.
- **Land** — one ladder, cheapest rung first and nothing else on offer:
  `east_meadow` 2,500 → `west_hollow` 3,500 → `north_reach` 5,000 in credits, then
  `south_bank` 600 → `sunrise_field` 900 → `dusk_field` 1,200 → `far_terrace` 1,500 →
  `still_shore` 2,000 in **gems**. Only the credit half counts toward a grove's score
  (16j), which is why the complete figure above is two numbers and only one is a score.
- **Grove star ladder** — 10K / 20K / 50K / 100K / 200K, content in `homestead.json`.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted). A loss costs one. Two kinds of
  run cost nothing (invariant 24): the **first 3 levels of the first chapter of each mode**
  (`hearts.graceLevels`, content) and **any level the player has already finished**, which also
  means they are open, and free to leave, with no hearts left. The cap is per player: a **heart
  container** raises it to 10, 20 or 50 permanently (invariant 18d), derived from
  `heartContainersOwned` and read by every screen through `Wallet.MaxHearts`.
- **Hints** — pool of 3 account-wide, one back every 8h, ceiling equals the cap (a granted
  hint at a full pool is refused, not clamped). A hint charges no moves. Spent in **two modes**
  and they buy different things: a glade's turns the conduit (`BoardView.Hint`), a grove's
  *marks a flower* and shows the cascade tapping it would set off (`BudHint`, `BudView.Hint`).
  Neither costs the save file, the wire or the server anything.
- **Turrets** — 20, one free (`bolt`), 10 priced 1,200 → 9,000 **credits** behind keeper
  levels 2 → 14, and 10 priced 600 → 2,000 **gems** with no gate. Ten abilities, two rungs
  each. The player stands four of them, one per colour, and carries that line into every
  siege (invariant 42). None of them makes a bolt weaker than the free one, ever.
- **Utilities** — four, held up to **100** each, account-wide and shared by every Thornwatch
  level, each with a **cooldown** between two uses of it — firepot 10s, mending 15s, surge 20s,
  stormcall 30s, burned on the run's own clock and forgotten by a restart (39j) — and **none of
  them carries a keeper gate**. Surge was at level 4 and stormcall at
  level 9, which contradicted the rule the loadout was commissioned under — *a credit price
  carries a level and a gem price never does* (invariant 42) — and every utility is gem-priced,
  so the two gates could never have been right. It was met exactly as that rule predicts:
  nine thousand gems in hand and a forty-gem button that would not press. `UtilityItem.MinLevel`
  and `UtilityRefusal.Locked` stay, because they are content and a coin-priced utility would
  want them; nothing authors one. **Firepot** 12 gems (440 damage in one box of the hill, charged 2 matches),
  **mending** 8 gems (6 ward health, charged nothing), **surge** 10 gems (18 shots' fuel,
  charged 2 matches), **stormcall** 40 gems (700 to every raider on the hill, charged by the
  same arithmetic and so dozens of matches on a full one). Three of the four are a weighted
  option in one daily chest — mending in the first (12 of 100), surge in the second (13 of
  100), firepot in the day's prize (12 of 100) — so a chest is the ordinary way to hold one;
  the stormcall is gems only. They are also a **shop shelf** (39h). Content (`utilities`), and
  the prices are the numbers most likely to be wrong first guess.
- **Streak** — a 7-night lap that wraps: 500 credits, 1 heart, 5 gems, 2 hearts, a 12h boost,
  3 hearts, 10 gems.
- **Ads** — four placements, all opt-in, no interstitials: `heart_refill` 2 hearts,
  `coin_bonus` **300** credits, `win_bonus` credits, `hint_refill` 1 hint. (`run_continue` is
  retired — invariant 22.) Daily caps 20/12/**6**/10 — the first, second and fourth are
  deliberately above what any network will fill, so they bind only as a lever that can be
  lowered; `win_bonus` is the exception and its six is a real bound, because the wheel more
  than doubled what one view of it pays. `AdRules.MaxDailyCap` 30 is a hard `const`.
  <br>**`coin_bonus` was 1,000 and that made a video worth four perfect runs.** A 3-star clear
  pays 200 (avg 239 golden) in every mode, the whole hundred-level game pays ~23,000 once —
  **4.7% of the 493,770-credit catalog** — and a repeatable video paid 1,000, so the economy
  said the boards were the least valuable thing on the screen. Three readings agreed and none
  of them needed live data: against the **store** it gave away $0.53–$0.80 of IAP for an
  impression worth about a cent; against the **boards** it bought rank outright, since ad
  grants land in `granted` and `grove.ts` clamps the bought half to
  `earnedCredits + grantedBaseline` (19a); and against **playing** it was 4× a perfect run.
  What made it urgent rather than academic is that the cap of 12 does not bind today — the
  economy was being held in place by **ad fill rate**, so it would have loosened about
  fourfold the day AdMob instances landed, with no code change and no signal. 300 sits just
  above a 3-star clear, so a video reads as one good level. **A repeatable faucet must be
  priced against the non-repeatable one, never against the store alone.**
- **Bonus wheel** — eight equal slices on `win_bonus`, at 100 / 200 / 150 / 300 / 100 / 250 /
  150 / 500 percent of its authored 200, so the rim reads 200 / 400 / 300 / 600 / 200 / 500 /
  300 / **1,000** and every slice is a 1-in-8 chance. Mean 218.75%, so a view really pays about
  **438** and a capped day about **2,628** — against 2,400 under the old flat 200 at a cap of
  twelve. Content (`ads.wheel`), and removing the block puts the flat offer back. Invariant 25.
- **Shop** — 17 products. Gems 100 → 8,500 for $0.99 → $49.99; coins 2,500 → 75,000 for
  $1.99 → $39.99; three bundles $2.99 → $29.99, of which the starter is a non-consumable;
  the Bloom Pass $4.99; 5-heart refill 50 gems, a day of fast hearts 30 gems. **Heart
  containers** `gg_heart_vessel_1/2/3` — non-consumables at $19.99 / $29.99 / $39.99 raising
  the refill cap to 10 / 20 / 50, on the supplies shelf under the gem-priced hearts, and the
  only real-money products that grant something other than currency (invariant 18d). The
  whole catalog is **$298.83**, summed from `referenceUsdCents`; it read "16 products" and
  "~$236" for as long as the pass and the third bundle had been shipping, which is this
  table's own rule about never quoting it at a customer (2026-09-02).
- **Stars** — turns and nothing else (invariant 22). Gold is `par × 1.20`, silver `par × 1.40`
  and the run ends at `par × 1.60`: even thirds of the slack between a perfect run and death,
  so all three bands are landable. Held against **par**, never against the budget. Move one and
  you move all three — `LevelValidator.CheckStarBands` proves the ordering. Every threshold is
  `ceil` of exact hundredths, never of a float product — see *Hard-won facts*.
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
  **+4 taps** on a grove, **+4 moves** on a prototype board or **the whole ward line
  back at full health** on a siege (`wards: 4`), flat and repeatable for as long as the
  player can pay (invariant 23). About three days of free gems, or a fifth of the entry rung.
  The grant is *on top of* whatever it took to un-lose the board, and a bought run can only ever
  score one star — on a siege that is a **charge against the match count** rather than an
  accident of the fail state (23b). **A run that costs no heart is never offered one**, so a
  mode's free opening rungs and any level already finished lose without a price (24).
  Content (`continueRun`), and `"enabled": 0` withdraws it; it is deliberately not seeded, so a
  retune is a build rather than a config push.
- **Account prompts** — 2 chapter asks, 3 purchase asks, one shared 48h quiet period.

Everything in that list except the shop ladder is **content** in `progression.json` or
`homestead.json` and retunable without an app update. Re-seed after any change to it.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`.
**Fourteen functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`,
`adReward`, `appleNotification`, `sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`,
`withdrawGrove`, `publishGroveRanks`, `claimName`, `reportKeeperName`, `deleteAccount`.
`firebase/README.md` is the guide; `firebase/e2e/smoke-test.mjs` is **91/91 live** and
`firebase/e2e/delete-account.mjs` is **14/14 live** — the second erases the throwaway accounts it
makes, so it is the only suite here that leaves less behind than it creates.

Client half is `Assets/Game/Scripts/Cloud/` (assembly `GlimmerGrove.Cloud`), Firebase Unity
SDK 13.15.0 as vendored UPM tarballs under `GooglePackages/` (gitignored — run
`pwsh GooglePackages/fetch.ps1` on a fresh clone). `GLIMMER_FIREBASE` comes from asmdef
`versionDefines`; `Boot` picks the real backend over `NullCloudBackend`.

Two rules about the live suite, both learned the hard way. It signs in as a **new anonymous
account every run**, so anything derived from the account id varies — never hard-code a figure,
derive it from what the config publishes (this has already broken the earned-credits case and
three streak cases). And it is **sensitive to cold starts**: re-run before believing a failure
that arrives in the first minute after a deploy.

**The first rule is wider than "a figure" and the earned-credits case broke a fourth time
proving it.** It derived the per-star numbers off the published table and still hard-coded the
*level ids* it pushed — `c01_first_light` and `c01_twin_streams` — so the day their chapter was
hidden and anything was seeded, the server correctly valued that save at nothing and a green
suite went red for a content change. **Anything the published catalog decides has to be read
off the published catalog**, ids included: it now takes the first two glades out of
`levelChapters` and reads each one's reward rule from its own chapter, so a catalog with one
level per chapter is fine too.

**Two deployment traps.** Never `firebase deploy --only functions` for the whole codebase: it
failed all fourteen updates with `Failed to make request to cloudfunctions.googleapis.com` —
transport, not rejection — while still *creating* the new function, so the state read as "nothing
deployed" and was really "one of fourteen". Batches of three or four succeed first time. And a
secret is pinned at deploy time, so setting one prints `1 functions are using stale version` and
changes nothing until that function is redeployed.

**Owed, in order of cost if forgotten:**

0a. **Open the restyled UI in the Editor, and run `Addressables ▸ Sync All Assets` first.** That
   step is not optional here and it is not the usual reminder: the kit's PNGs were written while
   the Editor was closed (so nothing addressed them), *and* `Art/Ui/Kit/`, `Ui/jelly_*` and
   `Hud/tab_*` were deleted — and a dead Addressables entry does not fail the game, it fails
   `BuildPlayer` twenty minutes into an Android build with one file name buried in a package
   stack trace. **Save after the repair**: dropping an entry marks the group asset dirty and
   nothing more. Then `Validate Art`, `Validate Content` and the EditMode suite, and look at the
   hub and the shop on a device. Everything offline is green — compile, 1,613 tests, content,
   loc, art names, sound names, `make_hud_kit_art.py --check` — and every offline gate in this
   project was also green over the ten faults `render_home.py` and `render_shop.py` have now
   caught between them, which is the whole point of invariant 44d.
   <br>**One new address to check**: `Hud/fill`, which is on `AssetManifest` and reached through
   `Skins.Fill` — a *built* name, so `artnames.py` cannot see it and `SkinsTests` is what holds
   it to disk. That count went 74 → 81 with this restyle, and every one of the seven is a
   `Skins` constant, which is the covered case (44's own note about naming roles). Read the
   count anyway: it is the only thing that reports the loss.
   <br>**And the palette is the half a render is weakest at.** The world behind both screens is
   bright now where it was near-black, so the thing to judge on the device is whether the navy
   plates still read against it in sunlight and whether the backdrop stays a *place* rather than
   becoming something to look at. That is the fault this project has already met from the
   opposite direction twice.

0. **Delete an Apple-linked account on a device, and check it leaves Apple's list.** Everything
   else about deletion is live as of 2026-08-28: all fourteen functions deployed, the invoker
   binding granted on `deleteaccount`, the four `APPLE_SIWA_*` secrets set, and
   `firebase/e2e/delete-account.mjs` 14/14 against the real database. What no test here can reach
   is Apple's own answer: every account the live suite makes is anonymous, so it has no
   authorization code and the token exchange and `/auth/revoke` have never executed. The check is:
   delete an Apple-linked account in-app, then **Settings ▸ your name ▸ Sign-In & Security ▸ Sign
   in with Apple** — the app should be gone from that list, and the log should say
   `appleRevoked: true`.
1. **A real receipt reaching `redeemPurchase`, and a real impression reaching `adReward`.** These are
   the two money paths, both fully built, both deployed, and **neither has ever executed once** —
   which is a different state from unconfigured and the one that reads as done. Ads *load* on device;
   no view has ever paid. All sixteen Play products exist as of 2026-09-02 and all sixteen iOS ones
   before that (a sandbox `gg_gems_1` redeemed 2026-08-24), so what is left is a sandbox buy and a
   watched video on a phone. Do both the day closed testing opens.
2. ~~**View financial data** on the Play service account~~ — **done 2026-09-02.**
   `play-billing@glimmer-groove-1cd60.iam.gserviceaccount.com` holds *View financial data* and *Manage
   orders and subscriptions*, granted at **app** scope, and the key is `GOOGLE_PLAY_SERVICE_ACCOUNT`
   v2 with `redeemPurchase` and `sweepVoidedPurchases` redeployed against it. Proved rather than
   assumed: a signed JWT for that account fetched `purchases/voidedpurchases` and got **HTTP 200**.
   Before that the secret held the string `UNSET` behind a BOM, so every Android purchase would have
   been refused and auto-refunded in three days with nothing anywhere saying why.
3. The `appleNotification` URL registered for **both** production and sandbox.
4. AdMob **instances** under each of the ten LevelPlay ad units (the units exist on both
   sides; only the mediation link between them is missing). Blocked on a public listing, not on
   effort: AdMob's *App store details* field rejects a URL that does not resolve, and an unlisted
   AdMob app gets limited serving by policy — so `No fill` cannot be read as a wiring fault until
   the store page is live.
5. Fill in `app-ads.txt` from each network's dashboard and host it on the domain in both
   store listings; turn on in-app bidding. The AdMob line is real and verified; the ironSource and
   Unity Ads lines are still commented placeholders in **both** repos and must move together.
6. Delete the ~210 synthetic saves and the name reservations the live suite leaves behind.
6a. **Watch the EU consent form actually appear.** `UmpConsentGateway` has only ever returned
   `NotRequired` from outside the EEA, so the branch that shows a form has never run. Needs an EEA
   device or a debug geography override; a consent failure here is completely silent.
7. **Measure the turn tuning.** The three lines (1.20 / 1.40 / 1.60) were reasoned about, never played
   against: run `difficulty.py` and, once there is live data, first-attempt clear rates. The budget is the
   only fail state a glade has, so it is the number most likely to be wrong and the one with the shortest
   path to an uninstall.
8. **Retune or accept the chapter gate.** Whether two stars a level still filters anything is unknown (22).
   `chapterGate.starsPerLevel` is content, so this costs a re-seed and no store review.
9. **Delete the retired `run_continue` ad unit** from the LevelPlay dashboard — one of ten units to
   reconcile against AdMob instances in item 4.
10. **Give `bestMillis` its removal** — see invariant 22, once no shipped client writes one.
11. **Measure the restart gate.** Two halves are unmeasured: the **floor** (a charged restart needs two
    hearts, 24a) is arithmetically the same rule as leaving to the map and walking back in, but nothing
    counts how often players meet it; and the **offer** raised when it refuses (24b) shares
    `heart_rescue_offered` / `heart_rescue_bought` with the defeat panel, told apart by `where` — read the
    two funnels **separately**, because a defeat has already happened and the board is gone, while a refused
    restart is a board still standing that the player was about to throw away. There is deliberately no event
    for the refusal itself; if the funnel needs a denominator, that is the one to add.
12. **Measure the heart rescue against the continue.** The continue's ratio is taken against a lost run and
    this one against an empty heart bar, so read them apart; together they decide whether 20 gems is one
    price or two.
13. **Measure the bonus wheel, and read it against the cap.** The ladder averages 218.75% and the cap moved
    from twelve to six to pay for it, holding the *day* roughly where it was while more than doubling what
    one video is worth — both reasoned about rather than played against. `rewarded_ad_completed` on
    `win_bonus` is the funnel; it decides whether the cap is now the thing that binds (it was never meant to
    be) and whether the tail slice is rare enough to stay a story. The server side is live and proved end to
    end.
15. **Measure Whorlwater's ladder, and read the whorl's *reason* before its difficulty.** *(Lightfall
    is hidden as of invariant 38, so this is owed the day it is turned back on rather than now.)*
    The **ramp** has
    not been played against. It rides on board size (4 motes to 26), headroom (4 rows to 2) and whorl count
    rather than on par, which wanders 2 → 5 on purpose; if it is a wall the cheap fix is a roomier well or a
    shorter deal on the early rungs, which is a content drop and no store review.
    <br>And the **rule** has not been watched being met. Two mechanics were withdrawn from this slot for
    being the lens again (26g, 26h), so the question is not whether players enjoy the whorl but whether they
    work out that its value is the **pair they arrange**, not the cell it clears. A player who reads it as
    "a mote that pops itself" has not met it at all. Rungs 1 and 2 are built so the order of two drops
    decides the board; if that does not land, the fix is more boards of that shape, not a longer tip.
    <br>**The one number to watch is how often a whorl is opened early**, which is the mistake the mechanic
    is made of and the only one it can punish. There is deliberately no event for it yet; if the funnel needs
    a denominator, that is the one to add.
19. **Judge Prismvale by playing it, which is what its two levels are for.** *(Hidden as of
    2026-09-09 — `p01_prismvale` carries `"disabled": true` so that Thornwatch is the only mode
    a player can reach and the map's switcher draws nothing. This is owed the day it is turned
    back on rather than now, exactly as Lightfall's item 15 is.)* Built the way
    every mode since the five prototypes has been (invariant 29), so it can be taken back out for
    the price of a chapter body and six files, exactly as Kindlewake just was. Three questions in
    order.
    <br>**Does drag-to-swap read as the verb?** This is the whole reason the mode exists: the
    commission was the glade's goal with gems instead of conduits, and the mode it replaced kept
    the goal and invented a tapping verb nobody had asked for. A player who drags a gem in the
    first ten seconds has met it. One who taps has not, and the fix would be a coach hand on the
    gem beside the lantern rather than a longer tip.
    <br>**Do they work out that a lantern feeds only its own colour?** Both boards are dealt with
    one gem already lit beside each lantern, so the rule is *shown* rather than told. If players
    line matching gems up in the middle of the board and wonder why nothing lights, the teaching
    deal is not doing its job and the cheap fix is a second lit gem, which is a content drop and no
    store review.
    <br>**And does a vein going out read as their own mistake?** Nothing here is ever spent, so the
    only thing a wrong swap costs is light that was already on the board. That is the mode's whole
    fail pressure and it is the one thing that could read as the game taking something away. There
    is deliberately no analytics event yet; the first worth adding is how often a run ends with
    more gems dark than it started with, because that is the mistake the mode is made of.
    <br>**Two readings are known and are worth saying before anybody plays.** `careless` is *not*
    beaten on the first board and cannot be at par 3 (invariant 36f), which is fine on a teaching
    rung and would not be later. And par is capped at 4 by the search cost of a swap board
    (36b), so a longer chapter ramps on critters, lanterns and bare ground rather than on length.
    <br>Nothing about these boards was watched on a device before this was written: every offline
    gate is green, the whole suite passes, and both boards have been rendered and looked at. What
    has **not** been done is opening them in the Editor - `Validate Content`, `Validate Art` and a
    build of the view over the two boards are all owed, and the art folder wants
    `Addressables ▸ Sync All Assets` because it was written while the Editor was closed (see
    *Hard-won facts*).
20. **Judge Thornwatch by playing it, which is what its ten levels are for.** Built the way
    every mode since the five prototypes has been (invariant 29), so it can be taken back out for
    the price of a chapter body and six files. Five questions, in the order a player meets them.
    <br>**Does fuelling a colour read as the verb?** Nothing on the jewel board is a goal, which
    is the one thing a player arriving from Prismvale will get wrong: they will hunt for the
    biggest match, and what matters is which ward it feeds. If they never look up at the hill, the
    tip is wrong rather than the rule. Rung 1 is twelve creepers, two waves and nothing else on the
    field, so it is the cleanest possible test of exactly this.
    <br>**Does a cog read as an upgrade rather than as a blocker?** (37w.) Rung 2 stands one on the
    opening field where it will be met. The mistake to watch for is a player trying to *line three
    cogs up* — that is the failure the lesson is written against — and the mistake that costs is
    matching a careless colour beside one and upgrading a turret they did not want. **The one number
    worth adding an event for is how often a cog goes to a ward already at tier five**, because that
    is the wrong answer this mechanic is made of. There is deliberately none yet.
    <br>**Is par a line a good run can get under?** This is the only mode whose par is arithmetic
    rather than a proof (37a), and on a cog rung it is looser still, so it is the one place three
    stars might be free. `AnUnhurriedPlayerHoldsThisLine` says an ordinary player lands inside the
    three-star line on all ten with two to four wards standing; what it cannot say is whether a
    *bad* player ever drops to one star. If three is free the honest fix is a longer hill, not a
    tighter factor.
    <br>**Do the four bosses read as four different fights?** This is the one the last verdict on
    this mode was about: it shipped four boss encounters built from two kinds, and the report was
    that they looked and played exactly the same (37z). Rungs 3, 5, 8 and 10 now send a
    blightcaller, a warlord, a warbringer and an overlord, and each takes a different thing. Four
    things to watch, in order. Is the **tell** understood as a warning in time to act — the answer
    differs per boss, so the funnels worth reading are whether a **mending** is spent inside a
    warlord's or an overlord's window and whether a **surge** or a **firepot** is spent inside a
    blightcaller's or a warbringer's. Does a **douse** read as "stop feeding that colour" rather
    than as the game breaking a turret — a player who keeps pouring into a dark ward has not met
    it. Does a **roar** read as a reason to burn the hill down *now*; the warbringer is the only
    one that arrives, so if players are surprised when it reaches the line its entrance is too
    quiet. And does a **sunder** land as the finale taking the cogs they spent — if the badge
    falling is not noticed, the fix is that drawing rather than the number. **The one figure worth
    adding an event for is how often a run ends with the line down on a boss rung**, because a boss
    that has to be answered a particular way is the one place this mode can be lost for a reason
    the player never worked out.
    <br>**And is a boss now worth watching between its casts as well as during them?** That is
    invariant 37ac, and it is the one question here whose answer is already half known: the
    complaint it answers came from a device. Three things to watch. Does the **crackle** read as a
    thing breathing rather than as an effect stuck on — if it is noticed at all it is probably too
    loud, since the wind-up has to stay unmistakably louder than it. Is the **tell still the most
    legible thing in the cast**, or has the volley eaten its own warning — the funnel is unchanged
    (a mending spent inside the window), so a drop in it after this is the spectacle winning. And
    do the four **volleys** read as four different attacks — a chain that visits the wards it is
    not aimed at, a bombardment, a storm over the whole hill, a converging pair? There is
    deliberately no analytics event for any of it; what would say most is the mending funnel
    against its own figure before the change.
    <br>**And does the last wave feel like a siege?** Rungs 6 to 9 turn the hill from creepers to
    brutes. If the line never gets touched the fail state is decoration; if it falls every time, a
    later rung's hill is doing an earlier one's job. Both are fixed in the waves, which is a content
    edit and no store review.
    <br>Nothing about these ten levels was watched on a device before this was written: every
    offline gate is green, the whole suite passes, the Editor's `Validate Content` and `Validate
    Art` are clean, and every board has been rendered and looked at. There is deliberately no
    analytics event for any of the above yet; the first worth adding is how often a run ends with
    the **line down** rather than with the hill cleared, because those are two different failures
    and only one of them is about the puzzle.

16. **Measure the continue.** 20 gems for +15 turns was reasoned about, never played against, and it is the
    second number after the move budget most likely to be wrong: too dear and a defeat is a dead end, too
    cheap and the fail state stops meaning anything. `continue_offered` / `continue_bought` are the funnel,
    and the distribution of `taken` decides whether `gems` moves or `gemsStep` stops being zero.
21. ~~**Deploy the rules and re-seed `config/daily` for the utilities.**~~ — **done 2026-09-08**,
    and proved rather than assumed: the live ruleset
    (`e29083b5-f71f-4460-b7bb-9c86a92aac05`) carries `'utilityStock'` in `hasOnly` and the
    64-row bound, and `config/progression`'s published chest table was read back out of
    Firestore and diffed band-for-band against the shipped `progression.json` — three chests,
    weights 40/26/22/**12**, 40/34/13/**13**, 55/33/**12**. The ordering rule it leaves behind
    is the part worth keeping. The chest table now carries three `utility` bands and every weight in all
    three chests was re-cut to make room for them, so the published table and a client's bundled
    `progression.json` now differ. `claimAwards` re-rolls a chest from the published table and
    grants **its own** figure while the client shows what *it* rolled, so any window where the two
    disagree is a window where a player is shown one number of credits and paid another (39b).
    Remote content delivery is off, so a client's table only moves when the app does — which
    makes "seed early to be safe" exactly backwards, and means **the next content change to this
    block has to be seeded in the same sitting as the build that carries it**.
    <br>**The rules are the opposite and go first.** Adding `utilityStock` to `hasOnly` is purely
    additive, so it can be deployed at any time and *must* precede any client that writes the
    field: `hasOnly` is an allow-list over the whole document, so a client writing an unlisted key
    loses **every** save write rather than that key (12a).
    <br>Pre-launch this is all academic — there are no real players and the only accounts are the
    ~210 synthetic saves — but the ordering is what it is the day there are.
22a. **Judge the loadout, the two field raiders and the Infinite lane by playing them.** Three
    features shipped together and each has one question nothing offline can answer.
    <br>**The turrets** (invariant 42): does a player understand that the line is *theirs* and
    carried into every rung, rather than something the level handed them? The shelf is reached from
    the map and nowhere else, which is the moment before choosing a level and therefore the moment
    they would want it — but if they never open it, the free bolt is the whole game and twenty
    turrets are decoration. **The one figure worth an event is how many players ever stand
    something other than the starter**; there is deliberately none yet.
    <br>**The weaver and the thief** (invariant 40): does a web read as *that beetle did this to
    that gem*, or as the board misbehaving? The whole drawing is built around saying it — a ring
    closes on the cell, a mote crosses the hill, and the mark lands when it arrives — and if it
    still reads as the field breaking, the fix is the drawing rather than the rule. The second
    question is whether killing one lands as a **payoff**: six webs burning off at once is the
    biggest thing either of them ever does.
    <br>**The Infinite lane** (invariant 43): is wave 20 the right place for three stars, and is
    wave 11 the right place for two? Both are guesses — nothing can derive them, because par
    everywhere else is a search or a floor over what a level *sends* and this one sends everything.
    The second question is the pairs: from wave 21 two bosses arrive together, and whether that
    reads as the ramp's climax or as a pile-up is a thing only a played run can say.
    <br>Nothing about any of it was watched on a device before this was written: every offline gate
    is green, the whole suite passes, the Editor's `Validate Content` and the addressable audit are
    clean, and the board has been rendered and looked at.

22. **Judge the action bar by playing Thornwatch, which is what the three utilities are for.**
    Built the way every feature since the five prototypes has been, so it can be taken back out
    for the price of one save field and a folder. Three questions in order.
    <br>**Does the bar read as *yours* rather than as the level's?** The stock is account-wide
    and shared by every siege (39), which is the whole design and is also the thing a player has
    no way to be told. If they treat a firepot as something the board handed them, they will
    hoard it for ever and the feature is decoration.
    <br>**Does the charge land as fair?** Using a firepot costs two matches against the grade,
    which is arithmetic (39) and is *invisible* — the match counter simply goes up by two. That
    is deliberate, because the alternative is a panel explaining an exchange rate, and this file
    has already learned that a rule needing a panel is usually the wrong rule (20g). But if
    players read it as the game stealing matches, the honest fix is to say it on the slot rather
    than to stop charging.
    <br>**And is a mending the one that gets used?** It is the only one that costs the grade
    nothing, so it should be the one a careful player reaches for — and if the firepot wins
    anyway, the price of a star is too cheap. `utility_used` carries `matches`, which is the
    distribution to read; there is deliberately no event for the empty-slot tap yet, and that is
    the first one to add if the shop's funnel needs a denominator.
    <br>Nothing about this was watched on a device before it was written: every offline gate is
    green, the whole suite passes, and the icons have been rendered and looked at. What has
    **not** been done is opening it in the Editor — `Validate Content`, `Validate Art`, a build of
    the bar over the shipped siege, and `Addressables ▸ Sync All Assets`, because the three new
    PNGs were written while the Editor was closed (see *Hard-won facts*).
Ads **fill** as of 2026-08-24: all five placements load on device from ironSource's own network
and Unity Ads, with no AdMob instances yet. What is still unproven is a real impression reaching
`adReward` and paying, which needs a watched video rather than a load.

`UmpConsentGateway` has been compiled **and run**: `status=NotRequired, canRequestAds=True` on a
device outside the EEA. What that does not prove is the branch that matters — a form actually
shown — which wants a device inside the EEA or a debug geography override.
### Three confirmations, and only three

`ForfeitOverlay` (a committed run being abandoned), `ReportNameOverlay` (an act against another person that
cannot be retracted) and `DeleteAccountOverlay` (27), which earns one more completely than either: there is no
store to re-deliver an account, no archive to restore it from and no support path that can bring it back. Its
second tap is armed only when there is a grove to lose, which is `AccountOverlay.ConfirmAdopt`'s rule — arming
a button over an empty grove is what teaches a player to tap through it on a full one. `ContinueOverlay` is
not a fourth: it is an offer rather than a confirmation, asking a question nobody has asked yet, and its
default answer is the free one. Everything else either costs nothing to undo or is confirmed by the store's
own payment sheet — a panel of ours in front of that sheet is a tap for a question about to be asked properly.

### Not done, deliberately

- **Play Games Services** — better Android sign-in and the natural home for leaderboards, but Android-only,
  so it cannot be the identity.
- **A visual level editor** — tooling, and the thing most likely to matter next for cadence.
- **Remote content delivery** is built and switched off. Setting `ContentConfig.RemoteBaseUrl` turns the heart
  gate, the chapter gate, the chest odds and the ad payouts into minutes-not-days levers; it is the
  highest-value unshipped setting in the build. One known gap first: `Sync Manifest` bumps a chapter's
  `version` only when its **level list** changes, so a content-only rewrite would never reach a client that
  had already cached the body. The fix is a digest of the body in the manifest entry, which
  `ManifestSync.SurvivesRoundTrip` would then police.
- **A "keepers near you" board** — it needs the exact global ordering 19c refuses to keep, and the percentile
  already answers the question it would ask.

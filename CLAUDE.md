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
37i. **One level is a test, not a chapter.** What is owed is somebody playing it - see the owed
    list. The three questions in order: does **fuelling a colour** read as the verb, or do players
    hunt for the biggest match; does **fuel fading** land as a reason to hurry rather than as the
    game taking something away; and is **par 22 a line a good run can get under**, which is the
    one number in this mode nothing offline can answer.
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
39d. **The bar is furniture, not three buttons — and it took playing it to say so.** The first
    cut was a 148-point strip of loose squares floating at the foot of the screen with the
    board's old margin above them, and the verdict on it was *"not even an action bar"*. It read
    as three controls somebody had left there because that is what it was: nothing said the row
    was a *place* things are kept. It is now a full-width hazard-railed steel shelf that meets
    the board's own plate, with the room this mode used to leave empty at the foot given over to
    it entirely (`UtilityBar.Height`, 290, is the whole of `SiegeScreen`'s bottom inset).
    <br>**The cells are dark wells and the plate is light, which is the opposite of the source
    kit and is right.** Drawn level with the tray's own face a cell reads as a sticker on a
    panel — there is nothing for the eye to read as depth, and a bright icon on mid-grey has
    nothing behind it. The well is the ground the items are seen against, so it is the darkest
    thing on the bar. Only a picture says that; both cuts were green on every gate.
    <br>**Three cells sit at odd sixths rather than bunched in the middle**, because a
    full-width shelf with its contents centred reads as a tray built for more than it holds. A
    fourth utility re-spaces the row rather than making it look finished for the first time.
    <br>**And it is drawn, in the source kit's own sampled palette, rather than cut from it.**
    The kit's tray is one fixed-width panel with five cells baked into the plate and two stone
    wedges overlapping its ends: no clean rectangle to stretch, no cell-free column wide enough
    to repeat, and five cells where this bar wants three. Cutting it would mean rebuilding most
    of it and then living with whatever width the source happened to be. What the licensed art
    is good at here is the *idiom and the colours*, and that is what is borrowed — 32b's rule
    arrived at from the other direction.
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
  chapter carrying `"disabled": true` is skipped whole and the hidden glade and Lightfall chapters are
  not proved on a run (invariant 38) — re-enable one and it is checked again with no other change. The
  per-mode checks are rolled into it: the **prototype modes** (one check for all of them - par
  searched, not finished on arrival, a legal move to make, plus the handful of questions only each
  mode's own rules can ask; `ways`, `careless`, `nodes`) - which today carries only **Prismvale**
  (par searched, the field authored **dark**, nothing dealt into it, no critter standing where no
  lantern could ever reach it; `ways`, `careless`, `dealt`, `used`, `paired`, `idle`) -
  **Lightfall** (par searched, brim row empty, nothing floating, procession carrying all three
  channels; `motes`, `headroom`, `ways`, `greedy`, and from the second chapter `lenses`, `whorls`,
  `fused`, `kindled`, `aim`, `reach`), and **Thornwatch**, which is the one mode it does *not*
  search, because there is nothing to search (invariant 37a): what is proved is the layout's own
  refusals, the arithmetic par held to `SiegeTuning.Par` by being the same three lines, and the
  readings a validator can act on (`waves`, `raiders`, `brutes`, `colours`, `wards`, `threat`,
  `swap`). `fall-vectors.json` is the contract with the shipping C# rules; the prototype modes have
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
- **Thornwatch's art:** `Tools/make_siege_art.py --check` proves every sprite, cast flipbook
  and explosion is what the tool writes. It reads **three** source folders - the CraftPix packs,
  the turret/top-down packs in `to-assets`, and the mine tileset - passes when any of them is
  absent, so a checkout without them still runs the gate, and `--contact` lays it out to be
  looked at. Gems, cast, turrets and the mine floor are **cut**; only the rampart, the field's
  plate and the plinth are drawn.
  <br>**The per-colour shot, muzzle and hit reels under `Art/Fx/Siege/` are not its work and
  nothing re-derives them** - they were added by hand. That is owed item 18's shape again: art
  nobody can re-cut is art that quietly drifts from whatever produced it.
- **Thornwatch's projectiles:** `Glimmer Grove ▸ Art ▸ Bake Siege Projectiles` renders the four
  ward projectiles, their muzzle flashes and their impacts out of the bought pack's own prefabs
  into sprite reels under `Art/Fx/Siege` (invariant 37k); `▸ Verify Siege Projectiles` re-bakes and
  holds what is on disk to it within a tolerance, and `▸ Siege Projectile Contact Sheet` lays every
  reel out on the hill's own colour. **This is the one art tool here with no offline gate** — no
  Python script can rasterise a particle system — and it needs the pack, which is gitignored, so it
  is silent on a checkout without it. Re-run `▸ Addressables ▸ Sync All Assets` after a bake: the
  importer hook does not fire on files a tool wrote while the Editor was busy.
- **Thornwatch legibility:** `python Tools/render_siege.py` draws the shipped level at the size a
  phone draws it, with the real sprites, using `SiegeScreen.HostInset` and `SiegeView`'s own
  arithmetic; `--raiders N` stands that many of the first wave on the hill, `--no-bolts` takes
  the exchange off it, and `--no-bar` takes the action bar off. **Its insets are in the screen's
  own order — (left, bottom, right, top)** — and were written as (left, top, right, bottom) for
  a long time, which drew the board 55 points high: a diagnostic that is the only thing able to
  see a band in the wrong place must not itself put one there. **Look at it.** It is the only check that can see a fuel tube hidden behind
  the field's plate, a raider whose colour does not read, or a bolt baked so loosely that it
  crosses the hill as a sliver — every one of those a fault it caught, all past a green gate
  (invariants 37g, 37k). It draws each reel at its **loudest** frame, because these effects dip:
  drawn at a fixed index it caught two muzzles mid-dip and reported a bake that was fine as broken.
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
  <br>**All four were one fault, and the repair is a different question rather than a better threshold.** The
  cut was a *colour* test, which needs to be right about every pixel on sheets where the glow behind an object
  covers the object's own brightness and hue; it is now a flood inward from the tile's border that stops at
  edges, which has to be right about one closed curve, and it floods over the residual *across* the glow's
  axis so the halo, its rays and the drop shadow are flat and can neither wall it off nor be admitted. **A
  keyed sparkle is a real closed shape**, so speckle is judged by size against the body and distance against
  its own width — never by a fraction of the tile, which is a different bar for every rung.
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
  rather than failed — 39 of them, and that number is the honest size of what still goes unchecked.
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
  almost nothing.** `make_shop_art.keyed` walks inward from a tile's border and stops at painted
  outlines, which is exactly right for a bomb with an ink outline and exactly wrong for a crackling
  energy orb: the flood walks straight through the soft edge and takes all but the fuse. Measured on
  the same sheet, one bomb kept 852,000 pixels and the orb beside it kept **29,000** — a difference
  so large it reads as a missing file rather than as a bad cut, which is the only reason it was
  noticed at all. The repair is a *different question*, not a better threshold: distance from the
  plate, keeping the halo, because the thing being cut out **is** a glow and a prism with a hard
  edge is a marble. Both lived in `make_quarry_floor.py`, one line apart, chosen per object;
  that file went with the Iron Quarry, and the lesson is half of why Hollowmarch's board
  sprites are *drawn* rather than keyed out of a sheet at all.
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
  migrations, monotonic merge. **Save schema v22.** Content schema: manifest and chapter bodies **v2**,
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
- **The two live modes, and the two hidden ones** (invariant 38) — the game a player opens today is
  **Thornwatch** (`s01_thornwatch`, one level, invariant 37) and **Prismvale** (`p01_prismvale`,
  two levels, invariant 36), and nothing else. **Thornwatch is the front door** — first row of the
  switcher and what a map with nothing remembered opens on (38a) — and it is the first mode here
  that runs on a **clock**: raiders walk down a hill at a line of coloured wards, and a match is
  worth only the colour it was. Prismvale is the classic glade's goal reached by the jewel board's
  own verb, drag-to-swap, with the match-three taken out of it.
  <br>**The classic glade (`c01_shallows` … `c04_nightbriar`) and Lightfall (`f01_lightfall`,
  `f02_glasswater`, `f03_whorlwater`) are *hidden*, not deleted** — `"disabled": true` in the
  manifest and nothing else, so every board, screen and mode class still stands and seven booleans
  put them back. A disabled chapter is skipped in silence by `CatalogIndexBuilder.Add`, and a mode
  with no chapters is left off the switcher by `CatalogIndex`, so hiding costs no code at all.
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
  modes and nine chapters, of which two modes and two chapters are enabled** — three levels a
  player can reach. Everything else is on disk behind a boolean.
  <br>**Prismvale is the twelfth mode the prototype level shape has carried and the only one still
  on it**: a grid of letters, a deal, a searched par and a slack, so `ProtoGrid`, `ProtoSearch`,
  `ProtoRun`, `ProtoVerdict`, `ProtoView` and `ProtoScreen` are all inherited and the mode supplies
  a board (`PrismBoard`), a look, a validator and a view. `ProtoDto.cores` — the **deal** — is
  wanted by none of them now and stays, because the block has always described itself as carrying
  one. `StoryScreen`, the **story** band (30d) and the **village world** of backdrops (30e) all
  survive with no mode using them, which is what a seam is for.
- **Utilities** — an account-wide action bar of three consumables filling the foot of
  Thornwatch's screen (`UtilityBar`, a 290-point shelf), dropped by daily chests and bought
  with gems, charged against the graded count at the mode's own exchange rate so one can never
  buy a star (39). Two monotonic counters per id in the save (v22), catalogued in
  `progression.json`, shelf, cells and icons all drawn by `Tools/make_utility_art.py`.
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
| `s01_thornwatch` | siege | 1 | 29 matches | none — the ward line is the fail state | raiders come down the hill at four coloured wards; match a colour and that ward fuels up and opens fire, and a bolt is worth double against a raider of its own colour. Fuel leaves a ward as a bolt and no other way (37c). 8x5 field, 20 raiders in 3 waves — four, then eight, then eight **brutes** — all four colours against all four wards. Waves come on a clock, or early if the hill is cleared (37k). An unhurried player holds it in about 35 matches with 40% of the line's health gone (37j). The one level of the mode, and it cannot be lost on moves (24) |
| `p01_prismvale` | prism | 2 | 3–4 swaps | none, then par + 3 | drag a gem onto its neighbour and the two change places; a lantern feeds the gems of its own colour touching it, that colour runs on through every matching gem beside them, and a critter standing against the vein wakes. Nothing is ever spent, so a vein can be **broken**. 6x6, critters 2 → 3, lanterns 2 → 3, `ways` 10 → 120, `dealt` 2 → 3 of 25, `used` 2 → 3, greed beaten on the second. The first rung cannot be lost (24) |
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
- **Utilities** — three, held up to 9 each, account-wide and shared by every Thornwatch
  level. **Firepot** 12 gems (44 damage over 22% of the hill, charged 2 matches),
  **mending** 8 gems (6 ward health, charged nothing), **surge** 10 gems (9 shots' fuel,
  charged 2 matches). Each is a weighted option in one daily chest — mending in the first
  (12 of 100), surge in the second (13 of 100), firepot in the day's prize (12 of 100) — so a
  chest is the ordinary way to hold one and gems are the answer on the evening somebody is
  stuck. Content (`utilities`), and the prices are the numbers most likely to be wrong first
  guess.
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
  **+4 taps** on a grove or **+4 moves** on a prototype board, flat and
  repeatable for as long as the
  player can pay (invariant 23). About three days of free gems, or a fifth of the entry rung.
  The grant is *on top of* whatever it took to un-lose the board, and a bought run can only ever
  score one star. Content (`continueRun`), and `"enabled": 0` withdraws it.
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
19. **Judge Prismvale by playing it, which is what its two levels are for.** Built the way
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
20. **Judge Thornwatch by playing it, which is what its one level is for.** Built the way every
    mode since the five prototypes has been (invariant 29), so it can be taken back out for the
    price of a chapter body and six files. Three questions in order.
    <br>**Does fuelling a colour read as the verb?** Nothing on the jewel board is a goal, which
    is the one thing a player arriving from Emberforge or Prismvale will get wrong: they will hunt
    for the biggest match, and what matters is which ward it feeds. If they never look up at the
    hill, the tip is wrong rather than the rule.
    <br>**Does the last wave feel like a siege?** Eight brutes arrive together and an unhurried
    player holds them with about half the line's health gone (37j). If the line never gets touched
    the fail state is decoration; if it falls every time the opening level is doing a later
    level's job. Both are fixed in the hill, which is a content edit.
    <br>**And the number: is par 29 a line a good run can get under?** This is the only mode in
    the game whose par is arithmetic rather than a proof (invariant 37a), so it is the only one
    where three stars might be unreachable or free and nothing offline can say which. Watch the
    star a real run scores. If three is free, the honest fix is a longer hill rather than a
    tighter factor; if it is unreachable, `MatchGemsTenths` is wrong and it is one constant.
    <br>**A fourth question was added with the projectiles** (invariant 37k): does a bolt read as
    *that ward's*? Each of the four now fires its own thing — a fireball, a venom dart, an icicle
    and a lightning bolt — so the answer should be yes by silhouette before it is yes by hue, and
    the icicle is the one to watch, because it is the palest of the four and the only one whose
    colour had to be forced rather than agreed with.
    <br>Nothing about this level was watched on a device before this was written: every offline
    gate is green, the whole suite passes, and the board has been rendered and looked at. There is
    deliberately no analytics event yet; the first worth adding is how often a run ends with the
    line *down* rather than with the hill cleared, because those are two different failures and
    only one of them is about the puzzle.
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

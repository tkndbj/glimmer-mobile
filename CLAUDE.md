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
   holding a second copy.
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
   rule in isolation. **The board says which taps pop** (`BudRun.Pops`), which is the change; the choice is
   untouched and what is gone is the arithmetic in front of it. **It falls, and it grows**, so the board never
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
20m. **Every object in Budburst acts on a cell and its four neighbours, so the only axis its second
   chapter had left was reach.** The mix, the bunch, the wash, the crack and the bomb's square are all
   adjacency, and a mode whose entire product is the chain has nothing to gain from another thing built out
   of it — 26g's *weaker-or-equal* test, applied to a mode made of one idea. The **runner** takes the
   adjacency out: two squares of the grove are joined by a vine, and a bunch that <b>takes in</b> one end
   sends its colour to whatever is standing on the other, however far that is. It brings no operator (`|`,
   the mode's own), no second kind of light and no new cell, and it leaves the termination proof untouched,
   because all it can ever do is put channels onto a flower that is already there. A vine belongs to the
   **ground**, never to the flower, which is what lets a living grove fall through it where old wood could
   not stand on one at all (20l).
   <br>**The threshold is *in the bunch*, not *beside it*, and that is the whole of why it is not a
   solvent** (20j's third test). A spread that fires on anything happening nearby walks outward for ever and
   makes every board more solvable; one that has to be **built into** is a thing the player arranges — and
   it is the only decision the mechanic has, because what a vine is worth is settled entirely by what is
   standing at the **far** end at the instant it fires. Sending a colour that end already wears is the one
   mistake a runner can punish, so `BudBoard` reports a runner's wash **even when it changes nothing**: the
   one place `BudWash` breaks its own rule that a wash is a flower *changing*, and 20g at its smallest.
   <br>**The vine is on the board from the first frame, and that is load-bearing rather than decoration.**
   The last time this mode moved colour with no cause beside it — `Creep`, ripening one flower between taps —
   it came back from play as *"another far flower's colour changes… I'm not sure if this is a bug"*. A runner
   does the same thing on purpose and far harder, so the answer could not be a better animation on the day:
   the two ends are joined by a painted vine before anything has happened, and when it fires the light
   travels the line the player has been looking at all along.
   <br>**The reading that judges a board is `changed`, and the obvious one could not work.** The measurement
   26g literally prescribes is par with every vine cut — and it answers *nothing* for every input, because a
   grove is dealt far more taps than its answer needs, so par sits on a floor set by how many critters are
   shut in and how far apart they are: **cutting every vine moved par on none of several thousand swept
   boards**, on many of which the vine plainly decided how the grove played. What works is the same test one
   level down — play **every opening tap** both ways and compare the grove each leaves behind. Zero taps that
   differ condemns the board; `caught`, taps that burst *more* because a vine carried, is what an author
   holds out for. **A metric that answers "nothing" for every input is a broken gate rather than a strict
   one, and the only way to find out is to run it over real boards before trusting it.** Two structural
   refusals need no search at all: a vine joining two squares that already touch delivers what the wash
   delivered anyway, and an end on bare ground can never be taken into a bunch.
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
28. **A mode that cannot be lost is a prototype, and Groovekeeper was the second one.** Invariant 26 by the
    same route and answered the same way. **A groove now authors its ground, the beds that have to bloom and
    the procession it is dealt, and nothing else.** Par is the fewest tiles that open every bed, found by
    `KeeperSolver`, and the star lines are the same multiples. The whole feature cost the save file **no
    schema version, no merge rule and no server work** (20a).
28a. **The inversion is the mode, and a bed is what turns it into a puzzle.** Every edge-matching game
    rewards putting like against like; this one rewards the opposite — a seam between two unlike colours is
    worth something and a seam between two of the same is worth nothing, and a tile whose own colour and its
    neighbours' between them carry all three **blooms**. That alone is a toy; the **bed** makes the question
    "what does this one complete, and what does it leave the next one able to complete". **One tile can open
    five** (`KeeperFlourish.Most`), because a planting is read against the cell it lands on *and* the four
    beside it, and that is exactly what par rewards: the prettiest play and the most efficient one are the
    same play. Because the board is append-only, **blooming is derived rather than stored**, so a solver's
    state is the grid and nothing else and there is no flag for the two to disagree about.
28b. **The room to err is a count of tiles, and the fifth of them is the two-star line.** 26e for a second
    mode: a wrong tile is permanent *and* takes ground a bed may have needed. What is new is why the count is
    **five** rather than four — a budget of `par + spare` has to clear `ceil(par × 1.40)` or the bottom band
    is stranded and every clear is worth two stars or three (22's fault from the budget's side). Four holds to
    par seven and collides at par eight, which is where this chapter's finale sits; nothing but
    `CheckStarBands` noticed.
28c. **Two fail states, and only one may be sold a continue.** The basket running out is a shortage more
    tiles fix; a groove with **nowhere left to grow** is not, so `KeeperVerdict` answers
    `RunContinue.NoContinue` — the mistake money cannot fix is the spatial one, which is the half this mode is
    about. That is also the exact reading of "the first groove cannot be lost": a negative `budgetFactor`
    turns off the *basket*, not every ending, because a groove with nowhere left to grow is genuinely over and
    a board that can be neither won nor ended is the one state 20g forbids.
28d. **Composting is the one move that changes nothing, and it costs a tile for that reason.** A heartbed
    refuses every colour but its own, so a run can hold exactly the wrong tile with the right bed waiting, and
    the honest answer is a priced re-deal rather than a free one — the player is simply asked "is moving the
    procession on worth a tile", with the basket in front of them. It is allowed on the **last** tile too,
    deliberately: withholding it there reads as protective and is the one setting that can produce a groove
    which will not end.
28e. **A heartbed refuses rather than spoils.** The wrong tile cannot be planted there *at all*, so nobody
    can kill one with a mis-tap, and the bed wears its colour where anyone can see it before they tap. That is
    what turns the ordered procession from scenery into the puzzle (20e for a third mode): a plain bed is
    opened by whichever tile is in hand when its neighbours are ready, where a heartbed has to be reached with
    one particular tile.
28f. **The proof that a bed is lost never ends a run, and only decides whether it would be honest to sell
    one.** `KeeperBoard.AnyBedLost` is Lightfall's removed clause kept for the one question where it is right.
    Ending a run on it is the mistake `FallVerdict` shipped and took back: it came back from play as a run
    that ended while the tray still had motes in it, which reads as the game deciding on the player's behalf.
    A player who wants to spend their last three tiles on a groove that cannot be finished is entitled to.
    Both clauses are certainties rather than heuristics, because the answer decides whether money changes
    hands, so it under-reports and never over-reports.
28g. **A Groovekeeper procession need not carry all three colours, and that is the one place this mode is not
    Lightfall.** A well refuses a two-colour deal and has to (26c); nothing here does that — a tile that
    cannot bloom is simply a tile, the sprigs are permanent, and **two of the ten grooves are finished with a
    two-colour basket** because the third colour is already on the board. The check was written anyway, by
    reflex, and errored on both. What matters is that every bed can be *opened*, which is what the search
    proves. **Copying a rule across from a mode that looks similar is how a gate comes to refuse correct
    content.**
28h. **The search is what the mode rests on, so its floor is the thing to be careful with.** Par is found by
    iterative deepening over a grid whose every tile's colour is decided by *when* it was laid, so two
    orderings of the same cells are two states and the frontier grows like permutations. Both prunes are exact
    — a bound that could ever be too high would cut the shortest answer and hand back a par nothing can reach.
    The **floor** is the one worth understanding: beds whose closed neighbourhoods touch may share a tile, so
    their costs are grouped and only the worst of each group counts, while groups more than two steps apart
    are **added** — taking the maximum instead left a two-bed groove bounded at three against a real answer of
    six. The distance term stays a maximum and is compared rather than added, because a path to one bed may be
    a path to another: the one part that could double-count is the one part not summed. Cost goes roughly as
    the open cell count to the power of par (**par eight on tight ground is a few hundred positions, par nine
    on open ground a few hundred thousand**), which is why the chapter tops out at eight and `KeeperValidator`
    refuses a groove above 90,000 — the player's device runs this same search once, when somebody opens the
    level.

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
  copies of the four-armed-tile rule (`BoardVectorTests` runs the C# one). The per-mode checks are rolled
  into it: **Groovekeeper** (par searched, a sprig to grow from, a bed to open, a heartbed whose colour the
  basket deals; `beds`, `heartbeds`, `ways`, `greedy`), **Lightfall** (par searched, brim row empty, nothing
  floating, procession carrying all three channels; `motes`, `headroom`, `ways`, `greedy`, and from the
  second chapter `lenses`, `whorls`, `fused`, `kindled`, `aim`, `reach`), and **Budburst** (par searched, the grove **authored
  settled**, every cocoon with a flower beside it, the basket pure colour only; `ways`, `careless`, `nodes`,
  two of which are read **backwards** on that mode — invariant 20k — and from the second chapter `runners`,
  `changed`, `caught` and `ran`). `fall-vectors.json`,
  `keeper-vectors.json` and `bud-vectors.json` are the contracts with the shipping C# rules.
- **Difficulty check:** `python Tools/verify/difficulty.py` — what each glade actually asks of a player,
  counted rather than argued about. Not a gate (5d). It enumerates rotations of a grid of conduits, so it
  reports glades and names other modes as skipped. `dealt` is the one column about the board as the player
  *meets* it rather than about its solution (5g).
- **Shop art and sound checks:** `Tools/make_shop_art.py --check` and `Tools/make_sfx.py --check` prove the
  shipped pictures and clips are what the tools would cut. **They prove reproducibility and say nothing about
  quality**, and that distinction shipped a broken card once — every check green over a coin sack whose fill
  had drained out through an undetected edge. Two numeric gates were tried and **neither separates a broken
  cut from a healthy one**, because a bite out of one side is not distinguishable by any global statistic
  from a thin part that belongs there. So `--contact` is the gate: a sheet laid out at the size a card really
  draws, and a page that *plays* the sounds at the pitches and repeat rates the game uses. Look and listen.
- **Sound and music name check:** `Tools/verify/sfxnames.py` proves three lists agree — what the code
  plays, what is on disk, and what `AssetManifest.Sfxs` preloads. A misspelled name was a runtime
  `InvalidKeyException` and a silence that shipped green. It reads **literals only** and scans
  `Presentation` alone. A screen's music `Track` is checked the same way and is the other half of the same
  bug (`ShopScreen` shipped `"hub"`, not a clip, on the one screen that takes money); a `Track` written in
  any shape but a literal or `null` is an **error** rather than skipped. Music is deliberately not
  preloaded. `Art.S`/`Art.Frames` still have no such gate.
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
- **Unity only re-resolves packages and reimports on window focus.** If a change seems not to apply, the
  Editor probably has not been clicked.
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
  migrations, monotonic merge. **Save schema v21.** Content schema: manifest and chapter bodies **v2**,
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
- **The Grovement** — 14x14 isometric tile floor, land regions bought with credits, decor bought by the
  copy, residents projected from the companion roster, derived grove worth.
- **Boards** — public `groves/{uid}` cards, published rank distribution, unique keeper names with
  server-side filtering and reporting.
- **Modes beyond the classic glade** — Lightfall (`f01_lightfall`, `f02_glasswater`, `f03_whorlwater`),
  Budburst (`b01_thicket`, `b02_tanglewood`), Groovekeeper (`k01_grovekeeper`) and the Hollow
  (`h01_emberfall`). Lightfall is the only one to reach a third chapter, and what a second or third costs is
  the shape to copy: one new object (the lens, then the whorl, then Budburst's runner), one lesson id, a few
  fields on the mode's own step type — and **no save schema version, no merge rule, no `progression.json`
  retune and no server work** (20a). Lightfall's third chapter also cost **two** withdrawn mechanics before
  it kept one, which is 26g and 26h and by some distance the more useful half of the lesson; the runner is
  what those two rules look like applied *before* anything was authored (20m). Lightweave and Ripplewake are
  retired; `weave` and `ripple` are spent mode ids.
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).

### Content shipped

| Chapter | Mode | Levels | Par range | `budgetFactor` | Subject |
|---|---|---|---|---|---|
| `c01_shallows` | glade | 10 | 10–50 | none, then default | the verb, then colour, blending, rooted stone, brittle stone, taproots, pockets of colour |
| `c02_millvale` | glade | 10 | 41–63 | default 1.60 | the crossing |
| `c03_amberwood` | glade | 10 | 44–70 | default 1.60 | colour as the subject; no new rule |
| `c04_nightbriar` | glade | 10 | 44–69 | default 1.60 | the briar |
| `f01_lightfall` | fall | 10 | 2–6 drops | none, then par + 5 (motes) | the cook, then the chain; motes 3 → 30, headroom 4 → 2, `ways` never above 8 |
| `f02_glasswater` | fall | 10 | 3–6 drops | par + 5 (motes) | the lens, charged and fired; motes 5 → 33, glass 1 → 3 panes, channels asked for 1 → 6 |
| `f03_whorlwater` | fall | 10 | 2–5 drops | par + 5 (motes) | the whorl: the only place two *motes* are combined. Motes 4 → 26, headroom 4 → 2, whorls 1 → 2, `ways` 1 → 16, greedy beaten on nine of ten |
| `k01_grovekeeper` | keeper | 10 | 2–8 tiles | none, then par + 5 (tiles) | the inversion, then stone, the heartbed and the prism; beds 2 → 4 |
| `h01_emberfall` | hollow | 10 | 1–2 sparks | — | ladder is *how few openings win*: 7,8,6,4,2,3,4,1,4,1 |
| `b01_thicket` | bud | 10 | 3 taps | none, none, par + 8, then par + 5 | every grove *living* (20l); 5x5 → 8x7, flowers 22 → 49, critters 3 → 12, opening tap 3 waves → 8. The first two rungs cannot be lost (24) |
| `b02_tanglewood` | bud | 10 | 3 taps | par + 5 (taps) | the runner (20m): reach without adjacency. 7x6 → 8x7, critters 6 → 14, vines 0 → 3, three rungs carrying none |

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
- **Grove catalog** — 493,770 credits complete: 154,770 decor and homes, 68,500 land
  (9 regions, a free 6x6 starter), 270,500 residents. 150 priced pieces, of which 99 sell
  in bundles of ten at what one used to cost. Home ladder 5 rungs, first free.
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
  **+6 tiles** on a groove or **+4 taps** on a grove, flat and repeatable for as long as the
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
`firebase/README.md` is the guide; `firebase/e2e/smoke-test.mjs` is **90/90 live** and
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
1. The sixteen products in the **Play Console**, and the **three heart containers in App Store
   Connect** (the other thirteen iOS products are done and verified end to end — a sandbox
   `gg_gems_1` redeemed on 2026-08-24). `gg_heart_vessel_1/2/3` must be created as
   **non-consumables** in both, at the $19.99 / $29.99 / $39.99 tiers; a consumable would let the
   store sell a permanent upgrade twice and would break Restore. The whole server side is live as
   of 2026-08-26. What is still unproven is a real receipt reaching `redeemPurchase`, which needs
   a sandbox buy on a device.
2. **View financial data** on the Play service account, or the refund sweep silently no-ops.
3. The `appleNotification` URL registered for **both** production and sandbox.
4. AdMob **instances** under each of the ten LevelPlay ad units (the units exist on both
   sides; only the mediation link between them is missing).
5. Fill in `app-ads.txt` from each network's dashboard and host it on the domain in both
   store listings; turn on in-app bidding.
6. Delete the ~210 synthetic saves and the name reservations the live suite leaves behind.
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
14a. **Measure the Tanglewood, and read the runner's *reason* before its difficulty.** The chapter is
    Budburst's second and the runner is its one new object (20m). Two questions, and only the second is
    about difficulty. **Do players work out the threshold?** — that a bunch has to *take in* an end rather
    than merely go off beside one. A player who has not is playing a chapter with a decoration on it, and
    the symptom is a vine that never fires rather than a complaint. There is deliberately no event for it
    yet; if the funnel needs one, count opening taps that fire a vine against taps that go off *beside* an
    end and do not. And **is the chapter's step up from the Thicket the right size?** — it rides on how many
    are shut in (six to fourteen) and on the vines, since par cannot move (26d). If it is a wall, the cheap
    fix is fewer critters on the early rungs, which is a content drop and no store review.
14. **Measure the Budburst ramp.** Its ten groves are all par 3 and dealt eight taps each, so the whole ramp
    is how many are shut in. The mode is commissioned against a *feeling* (20k), so the reading that matters
    is not the clear rate (it should be ~100%) but the **three-star rate**, which should stay high and dip
    only a little at the end. Three dials that would have moved it were removed for being ramps built out of
    withholding, so if the chapter is flat the fix is **more to free**, not less to spend.
15. **Measure Whorlwater's ladder, and read the whorl's *reason* before its difficulty.** The **ramp** has
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
16. **Measure the continue.** 20 gems for +15 turns was reasoned about, never played against, and it is the
    second number after the move budget most likely to be wrong: too dear and a defeat is a dead end, too
    cheap and the fail state stops meaning anything. `continue_offered` / `continue_bought` are the funnel,
    and the distribution of `taken` decides whether `gems` moves or `gemsStep` stops being zero.
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

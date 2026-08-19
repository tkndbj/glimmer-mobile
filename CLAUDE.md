# Glimmer Grove

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
    progress is safe while nothing at all is being written.

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
anonymous auth on, both apps registered, and **six functions** on Node 22 in
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
a reflection runner — 491 pass offline, 71 need the Editor and say so), `content.py` and
`loc.py`. It no longer has to be recovered from a scratchpad.

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

Not done, deliberate: **in-app purchases** (the four store secrets hold `UNSET`, so
receipts are refused — correct until real store products exist), **Play Games Services**
(better Android sign-in and the natural home for the "ranks" leaderboards, but
Android-only so it cannot be the identity), and a **visual level editor** (tooling — the
thing most likely to matter next for shipping cadence). Remote content delivery is built
but switched off; set `ContentConfig.RemoteBaseUrl` to enable.

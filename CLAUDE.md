# Glimmer Groove

A globally distributed mobile puzzle game (Unity 6000.5.4f1, Android + iOS).
New level chapters ship every two to four weeks.

## The standard this project is held to

> This app will be distributed globally. Everything we build should be scalable, sustainable, and
> maintainable from day one. No demo, only production builds. We have to choose the absolute best
> practices (the most proper one) so we do not regret our decisions in the future when the game is
> expanding feature-wise and player-count-wise. Implement the most proper solutions, like AAA
> companies, to create the most proper system.
>
> — the project owner

- **No placeholder architecture.** If a seam is worth having later, build it now and build it properly.
  If something genuinely is a placeholder, say so in the code and make replacing it touch one file.
- **Cost curves decide priority.** Prefer the change that is cheap today and expensive later.
- **Prove it, do not assert it.** Back every claim with a compile, a test, or a validator run.
- **Push back with reasons.** If the owner asks for something that would create regret later, say so
  plainly once, with the specific failure it causes, then do what they decide.
- **Finish the whole job.** Half a migration is worse than none. If something can only be done in the
  Editor, automate everything around it and hand over exact steps.

> **About this file.** Each invariant is a rule plus the failure that bought it, compressed to what stops
> somebody undoing it; the long-form reasoning lives in the XML docs on the code each one names.
> **Numbers are permanent** — about 1,500 code comments cite them (`invariant 7b`, `invariant 5d`) — so
> an entry may be compressed but never renumbered. Spent ids are in one table below.
>
> **Two companions, both checked in.** `Assets/Game/CRAFT.md` holds the craft — the offline art tools,
> baking, grading, framing, how each mode looks and sounds, and the house rules for the UI.
> `Assets/Game/CONTENT.md` is the authoring and pipeline guide. Read `CRAFT.md` before touching a screen,
> an animation or an art tool; read `CONTENT.md` before touching content, assets or localisation.

## Invariants — do not break these

### Content and ids
1. **A `LevelId` is permanent.** Save data, analytics and remote config key on it. Never rename or reuse
   a shipped id, and never key anything on a level's position.
2. **Never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`** — a frozen record of what the pre-1.0 build
   shipped; changing it moves real players' stars onto the wrong levels.
3. **`Domain` must never reference `Presentation`.** The asmdefs enforce it. To call the UI from logic,
   raise an event.
4. **Content is data, not code.** Adding a chapter must never require a code change.
4a. **The manifest owns membership and order; a chapter body owns content.** The boot path reads
   `manifest.json` and nothing else; bodies load on entering a chapter and are evicted on leaving. Never
   make the boot path read a body — a cost per chapter at every launch for ever, and invisible in the
   Editor, because only Android routes StreamingAssets through `UnityWebRequest`.
4b. **Every chapter file must be in the manifest, and only the Editor may check that.** An unlisted file
   is never opened, so it validates, audits and builds green and the drop ships without it. `ChapterFiles`
   is the one place allowed to list the folder; the boot path cannot read a directory on Android.
4c. **Anything that rewrites `manifest.json` must prove it lost nothing.** A full rewrite deletes any
   field the writer does not know about, silently, under a success message — the first sync deleted a live
   event and thirty companion prices. The round-trip check reads its own output back through the reader
   **the game uses**. Never relax it into a warning.
5. **Omit `par` when authoring.** It is derived from the board; a typed one can drift.
5a. **A level's loc keys are derived from its id and cannot be overridden**, which is what lets anything
   holding a `LevelId` name a level without reading a chapter body.
5b. **"Is this tile solved" is `Puzzle.Alike`, and it exists exactly once.** Written out five times
   as a mask comparison, every copy was correct until a **crossing** appeared — it wears all four
   arms at every angle, so every rotation reads as solved and par comes out short by one per twisted
   crossing.
5c. **A rooted tile is authored at `/0`, and that rule guards every other rule.** Every proof runs against
   the board with rotations zeroed, so a tile the player can never turn, authored away from its solution,
   means the board proved is not the board that ships — and it tells a player who *had* solved it that
   they are one turn away. The check asks `Puzzle.Alike`, never `rot == 0`.
5d. **A mechanic that rejects no arrangement is decoration, and that is countable.** Enumerate the
   arrangements that satisfy the hard constraints and ask which win; when that count is **one**, the hard
   constraints alone decide the level and every other mechanic is absent — twenty-two of the first thirty
   were in that state. **The most cited rule in the file**: asked of a mechanic, a threat, a fail state, a
   readout, a timing and a purchase, and the answer is always a count rather than an argument.
5e. **A briar's thorns mate across the divide, so `Puzzle.Matters` has a second clause**: a tile the
   *player* has lit counts, whatever the solution wanted. **Before adding a tile whose drawn and
   conducting arms differ, ask what it can join.**
5f. **A wrong turn must be visible somewhere.** A creature the light had to never reach left the level
   refusing to settle — indistinguishable from a bug (20g) — so it was **removed**, and a panel explaining
   it was not the fix. Two general rules: a retired token or block name is **refused by name at parse**
   rather than ignored, because `JsonUtility` drops an unknown field without a word; and a retired
   *lesson* id is spent where a *level* id is **kept with its string changed**. The gate that replaced it
   is a **warning**, because a mode's first board may carry a mechanic as scenery.
5g. **A board is graded on its solution and met as it is dealt, and only the first was measured.** Thirty-
   four of forty levels "started half done", because the fit picked by par alone and "how much of this is
   already done" was a question nothing asked. Consequence: **par ramps flatten** — a properly dealt
   board's par is roughly 1.2–1.35x its turnable tile count.

### Text, lessons and assets
6. **All player-facing text is a loc key.** The build gate scans for key-shaped literals and fails on any
   missing. Never build keys by concatenation.
6a. **A lesson is a permanent id, and a screen that teaches several owns none of the sequencing.** A
   `Mechanic` travels in the save exactly as a level id travels in the ledger. `ScreenLessons` owns
   the order and the chaining, because three screens each growing their own would be four places
   that can disagree about how many modals are up.
6b. **A lesson whose sentence is *tap this* is raised when the thing to tap exists, and rings that —
   never its source.** A tip hung off the unit that *drops* a thing arrives seconds early; and a
   mechanic the board does not declare falls back to a tip with **no ring**, which looks exactly
   like a tip with nothing worth ringing.
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or `Addressables`
   directly, and never hand-list paths — derive them from `AssetManifest`.
7a. **Asset registration is an importer hook, never a menu item.** A step somebody has to remember on
   shipping week will be forgotten — it already was, and the tool meant to fix it rotted into a silent
   no-op scanning a deleted folder — so the build gate runs an audit: **making an error unlikely is not
   proving it did not happen.**
7b. **Transient art belongs to a named `AssetLibrary` scope, never to the global set.** An address already
   global stays global, and one owned by another scope is never re-claimed, or closing one screen frees
   art another is drawing. Loading is asynchronous, so a screen must repaint when a scope arrives — **an
   `Image` with no sprite is a white rectangle, not a blank**, which is where nearly every asset fault in
   this file ends.
7c. **A chapter's art is arithmetic on its ordinal, never a choice.** Nine chapters picked their art
   nine ways and nothing looks at the *set*, because no gate opens a PNG. The map belongs to the
   chapter's ordinal inside its own mode and the backdrop to the level's place in its chapter, and a
   generator writes the answers into the body so the content still *says* what it draws.
8. **The map shows one chapter at a time**, which bounds node count and texture memory by chapter size
   rather than catalog size. Do not "improve" it into one long map.
8a. **Map geometry lives in `ChapterMap` (Domain), not beside the screen**, so collisions and backwards
   trails are facts a build gate can check — a validator cannot reach into Presentation. **And the
   footprint it checks has to be what collides, not the disc**: a check guaranteeing the discs' 220 units
   passed every chapter with the next-chapter marker sitting on the tenth level's record.
8b. **Which chapter is shown is remembered per mode and only ever a hint.** Every way back except
   the arrows names no chapter, so the fallback is what a player meets after *every* level — and it
   used to be wherever they were **up to**. It is **device-local, never in the save**, because it
   moves both ways and could not be joined (11b), and it answers null the moment the id is not a
   chapter of that mode in this catalog.
8c. **Where a disc stands is a fact about the *perch*, not a constant.** A perch is fitted into a fixed box
   with its aspect kept, so a tall sprite lands smaller and higher than a squat one. `ModeLook.PerchLift` is
   **abstract rather than defaulted**, and is read in **one** place, because five offsets must move
   together. `CRAFT.md`.
8d. **A prop scattered on the map has to be drawn the way the map is drawn**, and there is no gate for it:
   put it on a strip and look. A preloaded picture nothing draws is resident memory for the life of the
   game, so a withdrawn one goes with its `.meta`, its manifest entry and its Addressables row.
9. **XP and earned credits are derived, never accumulated.** An accumulator cannot be merged across
   devices (nothing distinguishes "cleared twice" from "counted twice"), cannot be retuned for
   existing players, and cannot be recovered when lost.
9a. **The reward rule exists twice and must stay identical** — C# and TypeScript, both running the shared
   vectors as a test, so drift fails a build instead of desynchronising the economy. Change one, change the
   other, add a vector, re-seed.
9b. **`progression.json` versions independently of the catalog**, so a catalog format bump never
   invalidates the reward table for clients that have not updated.
9c. **So does the chest generator.** Contents are a pure function of (account, day, index) — FNV-1a then
   xorshift32, all 32-bit so JavaScript reproduces it exactly, which is what lets the server work out what
   a chest was worth instead of believing the client. The constants, shifts, stream numbers and modulo are
   all contract.
10. **The client never raises `grantedBaseline`.** Currency given rather than earned is server-owned,
   enforced by Firestore rules. Receipt validation must be idempotent on the store transaction id.
10a. **An award reaches the player as a claim, not as a balance**, with an id **derived from what earned
   it**, never generated: two devices claiming one chest produce identical entries that union to one, and a
   resubmission after a dropped reply confirms instead of paying. The server recomputes the amount; the
   client's number is a prediction. Never reach for `GrantLocally` — it is for the account seed alone.
10b. **Daily chests are earned, never bought**, which keeps them outside loot-box rules rather than merely
   compliant and is why the odds can be printed. No price, and **no second weighted pick** — one pick is
   what makes the published odds a list that sums to a hundred.
10c. **A chest cannot be opened before the account id exists**, because the roll is seeded from the uid so
   the server can recompute it. Do not roll with the device id.
10d. **A rewarded ad is granted by the network's callback, never claimed by the client.** Nothing
   about "this player watched a video" is derivable. The obvious alternative (client nonce → custom
   parameter → derived award id) does not survive — LevelPlay 9 removed the API — and it fails
   *silently*: ads that play, players told they earned coins, a server that never pays.
11. **Cloud conflicts merge; they never prompt.** The join is idempotent and order-independent. A "keep
   local or cloud?" dialog is data loss wearing a consent costume.
11a. **The ledger is a map keyed by level id, never an array**, so a duplicated record is unrepresentable
   rather than something the server has to filter, and a sync can write one key alone.
11b. **Anything a merge touches must be monotonic, or it is not mergeable.** A stored *count* cannot be
   joined: two devices showing 3 and 0 are equally consistent with "one spent three" and "one has not heard
   about a refill" — hearts shipped taking the smaller and destroyed a refill on every sync. Store counters
   of things that happened and derive the count, so the merge is `max`. Check the field only ever rises,
   **and that its "absent" state is a value a real one cannot hold**, because `JsonUtility` writes a zero
   into every field an older file never had. The only count that may be stored is one that cannot fall
   (37bg).
11c. **A value merged by recency must carry its own date, and its default must never be stored.**
   Two mistakes, both general: the recency came from the *file's* stamp, which the snapshot sets to
   **now**, so the local side won every comparison; and an unnamed keeper *stored* the default, so a
   device with no opinion was indistinguishable from one that had chosen.
12. **Adding a field to `SaveFileDto` interacts with the checksum.** A file written by an older schema can
   never match a newer build's hash, so bump `SaveSchema.Version` when you add a section, or every save on
   every device fails at once.
12a. **A field is not added to the save until it is on the wire, and the wire is four places**:
   `SaveFileDto`, `SaveDelta`, the Firestore mapper — *both* directions — and `hasOnly` in
   `firestore.rules`. One field shipped having reached the first two only, so land bought with credits
   never left the phone. The rules entry has teeth the other way too: `hasOnly` is an allow-list over the
   whole document, so a client writing an unlisted key **loses every save write**, and the rules must be
   deployed before the client. The fixture checks the *fixture*, because a round trip is only as complete
   as what is fed into it.
13. **A reward is derivable, adjudicated, third-party, or not currency.** Currency the client hands out
   must reach the server as something it can *recompute*, as something a *third party* tells it about (the
   ad callback), or as something it can *bound* so tightly that forging it buys nothing — the streak is
   claimed as `streak:{day}:{night}:{ccy}`, so the grant log bounds it to one payout per calendar day and
   the night can climb no faster than the calendar.
13a. **A claim must never be refused for a reason that will still be true tomorrow.** The client warns and
   keeps resubmitting, so a permanent refusal is a loop for the life of the account. A disagreement with
   the save's own dates is logged and paid, and a missing config block leaves a claim *unconfirmed* rather
   than rejected.
14. **Derived rewards are free of save state, and that is why they are preferred** — no counter, no claim,
   no merge rule. Save state is where features here go wrong (11b).
14a. **Being derived decides where a reward comes *from*, never when it arrives.** A floor saying how much
   of a track the player has asked for is not the reward; the arithmetic stays derived and
   server-recomputed. If keeping it derived means it can only arrive silently, add the floor — one
   monotonic integer per key, merged by `max`. What must not come back is a *stored amount*.
15. **An entitlement is stored; everything that pays is derived.** Nothing observable implies "this
   player paid 8,000 credits for Coral", so owned companions are a **set of permanent ids joined by
   union**, because buying is irreversible. A count would be hearts' old mistake and a per-companion
   flag could not tell "not bought" from "written before this companion existed".
15a. **A gate is permission to pay, and both halves are required.** `AvatarCatalog.ReachedBy` answers the
   level half and is named for its narrowness on purpose — it used to be `IsUnlocked`, and a call site
   checking half a rule under a name promising all of it is how a companion somebody paid for stays behind
   a padlock. It was **or** for a year and both clauses cannot survive: if the gate handed the thing over,
   the price would be unreachable code. The gate is tested **before** the price, so a player both too
   junior and too poor is told about the wall credits cannot climb; and `IsHeld` must never re-check the
   gate on something already bought, or a retune confiscates it. The same shape gates homes (16s) and
   turrets (42c).

### The grove
16. **A grove is built, and only four facts about it are stored**, split by *shape* rather than by feature:
   a purchase is an **entitlement**, so owned pieces and land are union-joined id sets; an arrangement is
   an **instruction**, so placements are merged by recency with a stamp per slot (11c) — the only part that
   can lose something, which is why an untouched slot writes no row and a slot the player *emptied* keeps
   one. The fourth is the hall's seat (16q). Deliberately absent: any count of tiles. A slot id is written
   into the save, so invariant 1 applies to it.
16a. **A resident is a companion, and the roster is written down once.** The grove used to author
   five of its own, with its own unlock rule, prices and two screens that could disagree about what
   somebody owned.
16b. **The grove is a tile floor, and a tile is a slot.** Hand-authored slots made the player's only
   decision which pre-placed dot got which sticker.
16c. **A shop shelf is one idea used three times** — the shop's tab, the browse atlas and the asset scope
   must agree about how the catalog divides, so the division is expressed once. Browsing reads generated
   thumbnails out of one atlas per shelf, and it packs *copies*, which is load-bearing: a sprite may belong
   to exactly one atlas and then stops having a texture of its own.
16d. **Anything unbounded keeps only what you can see.** A grid builds a cell once and rebinds as it
   scrolls — a correctness rule as much as a performance one, because every grid used to rebuild itself on
   any event with cells entering from scale zero. **`Show` is a new list and animates; `Refresh` is the
   same list redrawn and does not, and anything raised by an event is a `Refresh`.**
16e. **Land is the one thing here that stopped being derived, and it cost a schema version.** It is stored
   as a union-joined set of **regions** rather than tiles — both are legal and only one stays small.
   Starter land has **no price and is never written down**, so "absent" and "bought nothing" stay one fact;
   and the hall must stand on starter land.
16f. **The starter companion is shown, never stored.** Writing that placement at first launch is what 11c
   forbids: a fresh install would stamp it with *now* and put them back. Clearing it is a real instruction
   that does get a row.
16g. **A grove's score is what it is worth, and worth is what is *held*.** Derived, cloud-safe and
   monotonic for free. Counting **placements** would be won by standing one expensive piece on two hundred
   tiles; **storing** it would be the count 11b forbids, forgeable in the one direction that matters once a
   leaderboard reads it. The reading is market value, not spend, which is the only version with no special
   case.
16h. **Priced decor is bought by the copy, and the count is representable only because it counts
   purchases.** Copies *remaining* cannot be merged; copies **ever bought** only rise, so the join
   is a per-id `max` and what is left is derived.
16i. **A piece occupies a footprint, the ground is a layer under everything, and a tap tests
   paint.** Three reports, each a piece whose picture and whose ground disagreed. *Buried*: the
   ground plane is flat and nothing on it is behind it, so all ground draws first and every piece
   over all of it.
16j. **The floor is sold in two currencies up a ladder, and both halves broke a rule that had been
   "the whole rule" while there was one currency.** **`IsStarter` was `Cost <= 0`, which is exactly
   right until a region has a price that is not a cost** — under the narrow reading every gem
   stretch reads as free and **half the world is handed over at launch**, with every file still
   reading as authored. **Before pricing anything in a second currency, grep for the predicate that
   means "free" and read every caller.** <br>**The ladder is authored and never derived from
   price.** It cannot be derived (600 gems and 5,000 credits do not compare) and the near-miss is
   worse: a gem region's `Cost` is nought, so "cheapest unowned" would sell the five dearest
   stretches first and for nothing.
16k. **A `Refresh` that restarts an animation is a `Show` wearing a `Refresh`'s name.** 16d put the
   entrance behind `Show`; what it could not see is that a *bind* restarts things of its own.
16l. **Two screens offering one catalog draw one card**, as a builder rather than a table of
   numbers, because a table leaves each caller to assemble it and two callers assembling one design
   is two designs a week later. A cell width is **derived** from its panel, and a card carrying its
   own keyline has nothing to trace, so state is said with a mark.
16m. **The grove is a village rendered from models, and what that bought is that a piece can be
   *turned*.** A flat cut-out is one drawing from one camera angle, so the only transform it
   survives is a reflection — turn it and a tree leans over. A model renders at four yaws, so a
   facing is a **different picture**, and `Turn` steps by one and answers `NoRoom` rather than
   skipping to the facing that would have fitted.
16n. **A union-joined set cannot be cleared by clearing it, so a reset is an epoch.** Clear the
   fields and the next pull joins the server's copy back; wipe the server and the first unsynced
   device pushes it back. A monotonic join has no way to say "this is gone".
16o. **Placing something is a draft, and the whole of what was wrong before is that it was not
   one.** Reported as *a wall visually holds four tiles but the game thinks it occupies one* —
   already false, and that is the finding: **the rule was right and nothing on the screen said so.**
   `GroveDraft` (Domain) answers `Fits`, `Stand` and `Footprint` from one place, replacing 390 lines
   in which *what is lit*, *what will be written* and *is the button live* were three answers kept
   in step by hand. <br>Four rules: every tile of the footprint is lit from the same plan the drop
   executes; moving is centred and clamped and **never relocates**, so a drag cannot silently jump a
   wall to somewhere it happens to fit; **turning is allowed even where it will not fit**, because a
   turn the player can see refused is a control and one that is swallowed is a broken button; and
   nothing is written until Confirm.
16p. **The floor doubled and the home ladder ends in a castle.** No price moved. **The origin and
   the hall did not move**, because a tile id is its absolute coordinate (16b); what is redrawn is
   which region *sells* which tile. **Every dwelling must match the hall's footprint**, so the plot
   is reserved up front and buying a bigger home never evicts what stood beside the smaller one.
16q. **The hall's seat moved from content into the save**, because a hall standing on a constant had
   nowhere for a move to be *written*. It is an **instruction, not an entitlement**, so it is joined
   by recency against its own stamp with the default never written down (11c). **Every runtime
   question about where the hall is goes through one accessor**: the old constant is still correct
   and has stopped being the *answer*, so a reader left pointing at it is not a compile error and
   not a crash — it is a home drawn on one tile and hit-tested on another.
16r. **A model named after a building is not evidence that it is one.** Two `building_*` models are
   **hexagons**, and a hexagon cannot tile a square grid (16m) — authored, rendered and placed
   before anybody looked, with every numeric gate green, and a render answered it in one picture.
16s. **The home ladder is gated on keeper level as well as price**, because credits alone were a
   poor clock for the longest goal in the game — they accumulate whatever a player does. **Both
   halves are required** (15a read across): a gated rung must also be **priced**, and the gate is
   asked **before** the price, because when two refusals apply the one to say is the one credits
   cannot answer.
16t. **Removing a grove piece is four things and only the first is the ask**: the roster row goes,
   the id goes into `Tools/grove_retired.txt` **in the same change** (the tool refuses a retired id,
   so the two are atomic), the importer rewrites the catalogue and drops the strings it owns, and
   the art comes off disk.
17. **A save may only ever be pushed to the account it says it belongs to.** `AccountGate`, five
   lines, and the only rule here whose failure has no undo: a sync is pull → join → push and the
   join is monotonic, so aimed at the wrong account it takes the better half of two strangers'
   groves and writes it over one of them. The window is ordinary — switching accounts moves the
   session before the file, and the OAuth screen backgrounds the app mid-way — and it is an economy
   rule too, because the same ledger under a fresh uid is a fully funded wallet.
17a. **A switch is finished on the device before the network is asked for anything.** The original
   order was secure → authenticate → **fetch** → replace, so one unlucky read left the device
   authenticated as one player holding another's save. The swap is **local** and the server is
   folded in afterwards by an ordinary sync.
27. **Deleting an account removes data first and the account itself last, and that ordering is the only
   thing making it safe to retry.** **Visibility first** (the card and every leaderboard row), because a
   run that dies halfway must never leave a deleted keeper's name where a stranger can read it; **the name
   next**, while the wallet holding the key is still readable, since names are not queryable by uid; **then
   the save**, recursively, so a subcollection added next year is not a list somebody forgot to extend;
   **then Apple; the auth user last.** Every failure before that step is still authenticated and every step
   is delete-if-exists, where deleting the user first leaves documents under a uid nobody can ever
   authenticate as again.
   <br>**Three things are deliberately kept**: the globally-keyed receipts (18a), or "buy, redeem, delete,
   sign up, redeem again" is a faucet costing an attacker one purchase; reports this account filed about
   *other* people, because the parent's count is denormalised from them; and a **denied** name's
   reservation, retargeted to a tombstone uid. **The client's half is server-first**, so every failure
   sentence can say "nothing has been deleted" and be true. **A linked account re-authenticates first, and
   that is one step doing two jobs** — proof of ownership, and Apple's single-use code, which expires in
   minutes; it is a re-authentication rather than a sign-in precisely because a sign-in *replaces* the
   session. **A revocation failure never blocks a deletion.**

### The store
18. **A real-money product grants currency, and nothing else.** A product granting both currency and
   hearts would need a record of *"did I already apply this transaction's hearts"*, in the save,
   merged across devices, whose failure mode is somebody paying and receiving nothing — so hearts
   and boosts are bought with **gems**, and a gem debit is an ordinary spend.
18a. **A transaction is confirmed only after the grant lands, and never before.** A purchase arrives
   *unfinished*; the server asks the store whether it happened, records it against a **global**
   receipt key (replaying one real receipt across thousands of accounts is the industrialised
   attack), and grants. So everything that can go wrong is "still unfinished", and both stores
   re-deliver on every launch for ever: a crash, a tunnel, a flat battery and an outage are one bug
   with one fix. **That is why no per-purchase state exists in the save.** A refused receipt is
   **never** confirmed, because "the server refused" covers a missing product config as well as a
   bad receipt.
18b. **The shop is one authored list, and the server derives its half from it**, because a card
   promising 750 gems against a server granting 700 is two files edited on different days and the
   difference is charged to a real card.
18c. **A refund is money leaving, so something has to watch for it** — buy, spend, refund, repeat
   needs no exploit and no tooling. Apple pushes, Google is polled.
18d. **A real-money product grants currency, or an idempotent permanent entitlement — never a stored
   amount, and never both.** A **capacity** arrives as the union of one permanent product id, so
   applying it twice is applying it once; the entitlement lives **entirely on the client** and still
   survives a reinstall, because both stores re-deliver a non-consumable for ever and the grant runs
   on *every* successful redemption.
18e. **A shelf's picture ladder is exactly as long as the shelf, and it was two longer for months.**
   The rung mapping makes a shelf of four and a shelf of six both read as full, which hides the fact
   that the *length* is a decision: against four products, **two painted quantities were shipped,
   addressed, always-resident and drawn by nothing**.
19. **Anything a stranger can see is a separate, server-written document.** The save is `isOwner(uid)` for
   ever; a leaderboard row is built by a function with its own credentials and never writable by a client.
   Widening the save's read rule would publish everything else with it and freeze the save's *shape* into a
   public API.
19a. **A number that goes public stops being derived-and-trusted and becomes adjudicated.** 16g built the
   grove's worth as a pure function of three client-written id sets: safe while private, forgeable once a
   leaderboard reads it. The **earned** half is derived from records the server already validates for
   currency, and the **bought** half is clamped to `earnedCredits + grantedBaseline`. The gate still works
   and works *before* the clamp — a save naming something its own keeper level has not reached cannot be
   honest, so that entry is dropped outright rather than cut down.
19b. **The public name is a second rule on top of the stored one, and the server's answer governs.**
   The bidirectional controls are why that is not a length check — U+202E re-orders the text that
   *follows* it, so one name misdraws the whole list — and whitespace is tested before the forbidden
   set, because a tab is a control character *and* a word break.
19c. **A standing is read off a published distribution; nothing maintains a global ordering.** Nine deciles
   and a hundred-row board, rebuilt daily, read as one document at O(1) at any player count — against a
   query costing a hundred reads per screen open on a collection that grows for ever. A league is not a
   second ladder either: it *is* the score table.
19d. **A name is unique because a document id is unique, never because a query said so.** Uniqueness
   is enforced by the database's own primary key at any concurrency, where an equality query returns
   empty for two players a second apart and lets both write — and it is the shape that does not
   grow.
19e. **The two folds are one rule, and the runtimes do not agree about Unicode.** Unity's Mono and
   Node's ICU **disagree** and only the shared vectors can see it: `İzmir` folds two ways, a Greek
   name ending in Σ diverges on Final_Sigma, the Latin ligature block is not decomposed by Mono, and
   Cherokee and Georgian Mtavruli got lowercase after Mono's tables froze.
19f. **A published name comes from the reservation, never from the save**, so a modified save changes its
   owner's screens and leaves the board untouched. The filter runs **again** at publish time, so adding a
   word takes a name off every board on the next rebuild; and the publisher **claims** whatever the save
   asks for when it differs, which makes a rename made offline land with no client-side retry state.
19g. **A word list is the cheapest layer of name moderation and the least important; the fold stops
   bypasses and reporting catches the rest.** The filter that shipped was thirteen English words
   over a string with everything outside `a-z0-9` **deleted** rather than folded, and every failure
   was silent: leetspeak walked past, a single Cyrillic character *removed itself*, and any name in
   a non-Latin script squashed to empty and was never filtered at all.
19h. **The list is a document and the takedown is a flag, because both have to move without a
   deploy.** The compiled list is the floor rather than a nicety: **a filter that fails open looks
   exactly like a filter with nothing to catch**, so a published list materially smaller than the
   shipped one is refused.
19i. **A report is keyed on the pair of accounts, and the client is told almost nothing.** The id
   *is* the idempotency, which is why the threshold counts **distinct reporters** rather than taps.
19j. **A card is asked for after the sync, never after the change, and the reply is proved.** A
   publish requested the moment a piece was placed was answered from the save pushed *last* time —
   and the fingerprint then noted as published stopped the real one ever being sent, so every board
   showed every grove one session behind, with a successful call and a well-formed card each time.
   <br>Three parts. **The only thing that asks is `Settled`**, raised by a sync that left the server
   holding the save it carries and by nothing else — `Synced` is also raised by a switch, a link, a
   purchase and a deletion — and the card judged is built over the receipt's save, never the live
   ledgers.

### What a mode is, and what a mode costs
20. **A mode is code, and a chapter names one.** A way of playing brings an interaction, a fail
   state and a scoring rule, so content can never add one — but a chapter says which mode it belongs
   to, so a drop ships a whole second game with no app update.
20a. **A second mode's level is an ordinary level, and that is why it cost nothing.** Its record, stars,
   merge and rewards are the ones every other level has, so a whole second *game* added **no save schema
   version, no reward retune, no `firestore.rules` change and no server work**. The two things that
   genuinely differ are *order* and *unlocking*, and those are per-mode in `CatalogIndex` alone: totals
   stay mode-blind, while `Next`, `Previous`, `OrderOf` and `IsLast` stay inside one mode, because chained
   end to end, finishing the first game would be the price of opening the second. **The bargain collects in
   both directions**: six modes have been *deleted* for the same price, which is what makes commissioning
   several at once sane rather than a gamble.
20b. **A mode may be a whole screen.** What it may share is the *world* — palette, colour arithmetic,
   critters, sounds — and what it must share is everything about being a **run**: the heart, the stake, the
   chest, the streak and the star ledger, reached through the same Domain classes rather than copied,
   because a second copy of the run lifecycle can disagree about when somebody is charged.
20c. **A level carries a board or one of the mode blocks, never neither** — a level with neither is a node
   on a map that cannot be opened and would validate perfectly.
20d. **A level authors no numbers at all.** Par is found by search and the star ladder falls out of it. A
   typed par is the failure with no symptom: one too high hands three stars to a careless run for ever, one
   too low makes them unreachable, and neither is visible in the file that caused it.
20e. **Where a mode's material never decays, an *ordered* queue is what makes it a puzzle** — a pool
   collapses the board to "which cells", where an order makes it an assignment. The same property makes
   such a board impossible to get stuck in, which makes unlimited undo safe.
20f. **A mode is hard when its constraints cannot all have their way at once, and that is one number
   over the whole board, never a bar on each element.** Judging pairs one at a time sent every pair
   the long way round on a route the *board* had chosen, which the player experiences as the game
   refusing the line they drew.
20g. **A mode may bring a rule no board can demonstrate, and the fix is to make the board
   demonstrate it, not to explain it better.** "I wake up all the critters and the game doesn't end"
   was not a bug and was indistinguishable from one. Saying it was right and not enough.
20h. **A chapter's mode is derived from its levels, never typed — and the build gate proves it.** It
   was the last field written by hand and the one whose absence nothing notices: it decides which
   screen opens a chapter, which lane it sits in and whose stars unlock it.
20i. **A mechanic that moves the *floor* moves par, the ladder's yardstick and the top of the star
   ladder with it — and only one of those three had a check.** A barrier against a grading built on
   a distance that walks through one would grade a board against a floor no arrangement could reach.
   **The difficulty reading has to follow too**, which is easiest to miss.
20j. **Three tests any new mode has to pass before a level is authored.** **One: the answer has to
   be visible on the board, now** — a payoff arriving later than the input is a thinking puzzle
   whatever the numbers say, and adding readouts is the wrong fix (*"I understood nothing"*). **Two:
   a finite board with no refill must not be able to freeze** — ask whether every legal input
   strictly moves something that only goes one way, which gives *cannot stall*, *always ends* and
   *the search terminates* at once; the monotone quantity is often not the thing being counted but
   the thing being *added*, and an input that would add none is refused rather than swallowed.
20k. **A mode may be built to be *easy*, and then two of this file's own rules invert.** Where the
   brief *is* a board almost anything finishes, **`ways` flips** (one single shortest play means the
   board has to be solved rather than played) and **`greedy` flips and becomes the bar** (a board a
   careless player *cannot* finish asks for more than the mode promises). What does **not** flip is
   anything about money or grading.
20l. **"Brain-dead" is a property of what the player has to *work out*, not of how hard the board
   is.** A mode tuned three times toward chill came back each time as "you still have to think quite
   hard", with nothing wrong with the boards: the fault was that the match was **invisible until you
   made it**, where every game of that shape shows the player the matches and asks them to pick one.
20m. **A mechanic the author places is one the player finds; a payoff has to be one they made.**
   Five candidate objects were built, each with a decision and a way of being wrong (26h), each
   warned at nought, every gate green — and the verdict on all five was *nothing different*.
21. **A chapter is opened by stars, and only its first level asks.** Ten levels cleared at one star
   each is a player who never met what the chapter taught, and a player beaten by the ninth of ten
   had no route forward except the board that beat them — a star gate can be met from anywhere. It
   is authored **per level** rather than as a total, because a chapter is not a fixed size. Four
   things follow and three were bugs the change laid: `NextToPlay` must return the **furthest
   unlocked** level; the victory panel's Next asks `IsUnlocked`; a chapter opening is a
   **transition**, measured either side of the record fold; and every screen that said "clear this
   chapter" now prints the **count**, because the old sentence is unactionable to somebody holding
   nine cleared levels.
22. **A puzzle is graded on the puzzle, so there is no clock anywhere in this game.** Stars were the
   *worse* of what the turns allowed and what a countdown allowed, and for anyone who stopped to
   think the clock was always the lower reading, so the half measuring whether a board was solved
   *well* decided nothing. A countdown also prices deliberation, which is what every mechanic here
   exists to force. <br>**Three lines, even thirds of one slack**: three stars at **1.20**, two at
   **1.40**, the run ends at **1.60** — and **they must move together**. The budget was once cut to
   1.60 while the lines were 1.35 and 2.00, putting the two-star line outside the survivable range:
   **one star became unscorable by anybody**, every number plausible and every board green.
22a. **A mode with no turns is graded on the count it does keep, never on how fast it was.** A mode
   reporting a constant as its move count had the clock deciding every star — and it had also fed that
   constant to the record and the published deciles, so every player held an identical "best".
22b. **A mode's fail state is a budget in the unit it is graded in.** **Spending is permanent and
   that is the whole mechanic** — undoing frees the *ground*, not the resource, or the meter rejects
   no arrangement — and what keeps it fair is that a wrong move is cheap to *discover* and only
   expensive to *keep*. **Resource spent is the grade**, not units occupied, so the meter and the
   stars cannot disagree.
23. **A lost run may be bought back, and the offer comes before the accounting rather than on top of
   it.** Only when the offer is declined does the defeat happen — heart, record, chest, streak,
   analytics — because a continue offered *after* the loss is an offer to undo an accounting entry.
   One flow owns it, since two copies would be two prices. <br>**It cannot inflate a reward, and
   that is arithmetic**: stars are held against par, never the budget, so a run at its fail state
   has spent past the two-star line and can score **one star at most**. The offer sells a *finish*,
   never a *grade*. **A continue that does not continue is a charge**, so the shortfall is cleared
   first and a mode that cannot be rescued answers `NoContinue`; if a grant leaves the run lost the
   player is **asked again**, not billed again.
23a. **A lost run has two prices and they buy different things**: the *run* (the board stands, so
   one star at most) and a *heart*, which is the gate (the board is rebuilt and graded like any
   other). Different panels in a fixed order, so nobody sees both at once, and both the same price
   on purpose — a player quoted a second price after declining the first reads the pair as haggling.
23b. **A mode whose fail state is not the counter it is graded on has to buy that promise back.**
   23's "one star at most" is *arithmetic* everywhere else; a siege is graded in **matches** and
   lost when its **ward line** falls, so twenty gems could have bought a top-rung clear — not a
   grading curiosity, because stars derive credits, credits are a grove's worth, and a grove's worth
   reaches a public board (19a).
24. **A run is free when it teaches nothing new, and the rule lives in one predicate.** **The
   opening**: the first few levels of the **first chapter of each mode**, because the worst moment
   to meet the one gate that stops somebody playing is while they are still working out what the
   verb is. **The replay**: a level already **finished**, for ever, because a board they beat is not
   content and cannot pay for itself — **cleared, not attempted**.
24a. **A run is charged when it ends and gated when it begins, so every door has to ask — and only
   one of five was asking.** *A level can be restarted for ever with no hearts left*: every
   individual rule was right, and the invariant joining the two moments — **a run may only begin if
   the player could pay for it if it went wrong** — existed as a line inside one screen and nowhere
   else.
24b. **A refusal over a live run brings the shelf to the player; only a refusal with nothing behind
   it may navigate.** The free way, the paid way with the shelf **stacked**, a countdown, and KEEP
   PLAYING under both, because carrying on is a real answer.
25. **A variable reward the client shows must be one the server can recompute.** Eight equal slices, each a
   **multiplier on that placement's own amount**, so the feature costs **no schema version, no merge rule
   and no claim work**. A prize the client *names* is one the server has to be told about, and 10d is why it
   cannot be told. The slice is a pure function of (account, day, spin index).
26. **A mode that cannot be lost is a prototype.** A board with no fixed future cannot be
   *searched*, so it can author no par; with no par there is no star line, with no star line no
   budget, with no budget no fail state — and with none of those it is not a level, it is a toy with
   a score on it.
26a. **The chain the mode was documented as having did not exist and could not.** Both the board and
   its tests described cascades that never happened, because nothing changed a mote's colour except
   a drop — so the wave counter, the rising pitch and the chain multiplier were dead code against a
   rule that rejects them (5d, where nobody thought to count).
26b. **Two fail states, and only one may be sold a continue.** Running dry is a shortage more motes fix; a
   flooded well is not, so it answers `NoContinue`. Both are read in one predicate, because three booleans
   in an `if` on a screen is three edges where the run is decided and the screen has not caught up.
26c. **A procession must carry all three channels, and the well that cannot be lost is why.** Supplying
   only what the *board* is missing is wrong by one step: a drop onto bare ground puts a fresh pure mote in
   the well wanting two channels, so a two-colour procession can be walked into a position no play
   recovers from — an ordinary loss on most wells, and on the opening well (authored without a supply for
   24's reason) a board that can be neither won nor lost (20g by arithmetic).
26d. **Par may be resolved lazily.** Paying for ten searches while the map opens is a hitch on the
   one screen that never asks; the memo is safe to race on because the function is a pure search
   over a frozen board. **Lazy is not free**: the validator warns above 40,000 positions and
   **refuses** above 120,000 — a different question from the solver's own budget, which has to be
   large enough to *prove* a hard board.
26e. **A well's room to err is a count of drops, never a multiple of par.** Elsewhere a mistake
   costs a fixed fraction of the board; a well's wrong drop is permanent **and makes the board
   worse**, so one mistake is worth about two drops — against 1.60 that gave an early level *two*
   drops of room, and raising the factor is worse the other way.
26f. **A payoff handed out for free cannot be a payoff.** The **lens** shipped *relaying* — any
   burst beside it set it off once, free — and came back as both "it made the game much easier" and
   "the animation is too weak", which are one fault: a relay that costs nothing happens on most
   drops touching glass, so it hands out reach for free *and* happens far too often to be worth
   stopping the board for.
26g. **A mechanic that delivers light competes with the lens, so the next one had to change *which
   colour is travelling*.** A **mirror** had **no event of its own** — it could not be triggered,
   only passed through, and on a board with no glass it did literally nothing. Every reading is
   blind to that: solvable, correctly par'd, `ways` tight, every gate green, because **a decoration
   passes every one of those**.
26h. **A mechanic can have an event of its own and still be the lens again; what separates them is
   whether the player *decides* anything about it.** A **wick** did exactly what 26g prescribed and
   came back as **boring** — its colour was fixed at authoring time, its trigger was free, and its
   effect was identical on every board it stood on. **So 26g's test needs its second half: *what
   does the player decide about it, and can they be wrong?*** <br>The **whorl** draws the two motes
   either side of it together and mixes them — the mode's own `|` applied to a pair of operands it
   never had, since every other rule adds a *colour* to a cell. It pulls **sideways**, the one
   direction nothing here travels in.
28f. **The proof that a board is lost never ends a run, and only decides whether it would be honest
   to sell one.** A run that ended while the tray still had material in it reads as the game
   deciding on the player's behalf, and a player who wants to spend their last three moves on a
   board that cannot be finished is entitled to.
29a. **Modes share a level *shape*, not a rule, and that distinction is the whole design.** Each
   authors a grid of letters, an optional deal and a slack, so the grid, the search, the budget, the
   verdict, the run, the validator, the view and the screen are each written **once**. Five copies
   of "a run is decided once" would be five places for one to stop being true.
29b. **Breadth-first searchable because monotone, and that is the entry test.** Nothing is ever
   added, so the graph is a DAG, the run always ends, the board cannot stall and the first layer
   holding a finished board is par (20j, three properties from one).
29c. **A companion is on every board, their part is fixed by the level, and that is what keeps par
   honest.** What they may never be is the player's *worn* companion changing what a move does: par
   is searched per board offline, so an ability that varied with who is worn would vary par, both
   star lines and the fail line per player — and somebody who bought a companion would be playing an
   easier game.
29d. **A prototype is refused if a greedy player can finish it.** `careless` is a warning rather than a
   gate, because early in a chapter thoughtlessness is supposed to work — but where a board is the *only*
   board of its mode, a fixture pins it. `ways` is pinned for the other direction, the one nothing else
   sees: a rule change that makes a board **easier** leaves par plausible and every gate green.
29e. **A mirror can disagree silently, and a fixture that needs the Editor compares nothing.** Every
   `*VectorTests` reads JSON through `JsonUtility`, a native call, so the offline runner reports the whole
   fixture as "needs the Editor" — the one gate nobody runs on the way past. The inline fixture earned its
   place immediately with two bugs that moved `ways`, `nodes` and the careless reading while leaving par
   untouched.
29f. **A predicate about the *ground* must not answer a question about the *state* standing on it.** Two
   ground predicates read the letter in the file, so a cell the player had just cleared was still refused
   as impassable and the board came out unsolvable with every character correct.
30d. **A level may speak, and every line is a loc key authored in content — the one place in this
   game a key is written down rather than derived.** A level's own name is a function of its id
   (5a), because anything holding a `LevelId` must name it without reading a body; nothing ever
   needs to name a line of dialogue it has not read.
30e. **A backdrop belongs to a mode's *world* as well as to a level's place** — one axis added to 7c rather
   than an exception to it. The arithmetic is unchanged, so a second chapter still costs no art. **The map
   is deliberately not part of it**: a mode is told apart on the map by its perch and by nothing else.
30g. **A `MonoBehaviour` that hides itself must not disable the object it needs to be alive on.**
   `SetActive(false)` on its own node meant the next call started a coroutine on a disabled
   behaviour — which Unity refuses **silently** — so the `done` callback never fired and the board
   stayed locked for the life of the screen. Alpha for the look, `blocksRaycasts` for the input, and
   the object stays awake.
30h. **A modal sets `Time.timeScale` to nought, so a screen coroutine waiting in *scaled* seconds never
   finishes while a lesson is up.** Use real seconds. **Before waiting on a clock in a screen coroutine,
   ask what a modal does to it.**
30i. **A recorded turn says where a piece was *immediately before* an event, and the cells between
   two events carry no beat at all.** A view that began each hop at the beat's `From` teleported a
   piece across every cell it should have crossed and animated the last one. **Nothing else in this
   project can see that class of fault**: every gate reads the *model*, and the model was right the
   whole time.
32b. **A goal has to be the most legible thing on the board, and no numeric gate can tell you it is
   not.** A cage approximated out of a ruin pack's posts rendered at phone size as three brown logs
   — *firewood*, in a mode whose entire goal was the thing behind them, with every gate green.
   **Approximating a goal out of scenery is how a goal comes to look like dressing**, so it is
   composed rather than cut.
32c. **What a preview may show is geometry, and never outcome.** Lighting where a projectile will go is a
   fact the player can already read off the board, drawn faster; what the blast will catch is the thing
   they are working out (20l).
32d. **Every board is dealt by seed into a designed template and kept for what it asked.** What is
   *designed* is what a player reads; what is *dealt* is the arrangement nobody can eyeball. **The seeds
   are recorded**, so a board can be re-derived rather than only re-typed.
33b. **A goal that can leave the board makes a board that can be neither won nor lost** (20g through the
   front door, because the opening board of every mode is authored without an allowance, 24). A goal that
   *jams* instead is what lets `Stranded` honestly answer **false**: every goal the board opened with is
   still standing on it, so more resource always helps.
33e. **A special the player makes must differ in *kind* from an ordinary move, not in degree** — 26g's test
   asked of the thing the *player* makes rather than the thing the author placed.
33g. **Where a fact can be derived from a shape it can never come apart from it.** A road drawn as a
   winding rail has its order *read off* the drawing, which is the only shape where what is drawn and what
   is played cannot disagree — 4a's argument about the manifest, applied to a board. A road that forks is
   refused **by cell** rather than read some arbitrary way.
33h. **A repeated tile is drawn full-bleed.** Inset and rounded, forty road tiles read as a row of separate
   *sockets* with dark gaps, so the one thing saying where the convoy was going disappeared into the
   ground. Every numeric gate was green, because every numeric gate reads the model.
34f. **A board's pieces differ in silhouette as well as in hue.** Where the verb is "are these three the
   same", a player who cannot separate red from green has to be able to separate a heart from a circle.
35b. **A pruning rule that is sound for the search can still delete the mode's fail state.** Refusing any
   move that does not reach a *goal* is provably safe for finding par and still wrong: under it no move
   spends anything for nothing, so the allowance can never bind and the meter counts down to an ending that
   cannot happen. **A no-op rule must refuse only what genuinely changes nothing**, never what merely fails
   to help.
35c. **`life` is the longest play, not the greedy one — and a mode where nothing is consumed needs no
   `life` at all.** A greedy walk answers "how long until this is over", which equals par on every board
   greed wins; what is wanted is the deepest layer any play reaches, capped at the allowance. **A check
   that could only ever answer yes is not a check.**

### Prismvale — the gem field *(hidden; code and chapter still stand)*
36. **Prismvale is the classic level's question asked with the jewel board's own verb**: drag a gem onto
   its neighbour and the two change places. A lantern feeds the gems of *its own colour* touching it, that
   colour runs on through every matching gem beside them, and a critter against the vein wakes. It authors
   the shared proto block and leaves the deal **empty**.
36a. **Nothing is ever removed, and that decides everything else.** Every move is a *rearrangement*, which
   makes a vein something to build rather than buy, and makes it **breakable**: light is read off the
   arrangement, so a gem pulled out of a working vein takes the light with it. That is why the board's key
   is its **cells alone** — a key carrying the light would carry a derived value that can disagree with
   itself.
36b. **It does not pass 20j's second test, and the honest statement is narrower.** A swap is
   reversible, so nothing "only goes one way"; what the search needs is weaker and true — the
   **goal** count is monotone, the arrangements are finite so the visited set closes the walk, and
   two touching gems of different colours are always a legal move.
36c. **The fail state is the meter and nothing else, which no other mode on this shape can say.**
   Nothing is consumed, so `Stranded` is a fact about the **layout** rather than the run, and a lost
   run may always honestly be sold a continue.
36d. **A critter wants light and not a colour, and that is a decision** — the colour already decides
   everything through the *lantern*, and giving a critter one of its own asks the same question twice.
36e. **The reading that judges a board is `used`, and the one that catches the silent fault is
   `dealt`.** `used` counts the distinct lantern colours a *shortest* answer really wakes a critter
   with, read over **every** shortest solution, because a field standing three lanterns whose answer
   uses one has two decorative lanterns on it (5d).
36f. **`careless` says nothing while par does not exceed the goal count** — at par three with three
   critters each takes exactly one swap, so greed takes all three; measured over 1,200 boards, not one was
   beaten.
36g. **A lantern that reads as a gem is the one confusion this board cannot afford** — a gem moves and a
   lantern never does, so the two must not share a silhouette. The first lantern drew at cell size as a
   **crosshair**, an "aim here" on the one object nothing may be aimed at; caught by the render alone.
36h. **A permanent highlight rather than an effect, and its arrival is a *run*.** Every pair of touching lit
   cells carries a bar for as long as both are lit, so a broken vein is visibly the light going out rather
   than nothing happening; newly lit cells are named **in flood order out of the lantern**, so the payoff is
   drawn as something travelling (30i: the view replays a log).
36i. **Two levels is a test, not a chapter.** Owed: does drag-to-swap read as the verb; do players
   work out that a lantern feeds only its own colour; and does a vein going **out** read as their
   own mistake rather than as the game taking something away? ### Modes that were built and
   withdrawn *Sixteen modes have been built here and two are live.
28. **Groovekeeper** — ten grooves, withdrawn as boring: every reading was good and the board played
   as arithmetic (20l one step further in).
29. **Five modes commissioned at once to be judged by playing them; four withdrawn.** One chapter of
   one level each, so the owner met five verbs and said which were worth a chapter. The answer was
   one — and that one was withdrawn later (31), which finishes the argument rather than weakening
   it. **Removing four of five cost the save file exactly what adding them did: nothing** (20a).
30. **Deep Orbit and Moonwake** — built and withdrawn the same day, both as *not enough on the screen*: one
   verb, one gesture, nothing a mass-market player recognises as a game. What was kept is everything that
   was a *seam* rather than a mode — the story band, the cues, the second-world backdrops (30d, 30e) —
   **because a seam survives the thing it was built for.**
31. **Nova Raid and Toppleglen** — every reading good, every gate green, and neither was the *fresh* thing
   the slot was commissioned for. **A mode can be monotone, searchable, correctly par'd, spectacular in the
   file and still not be a game anybody wants to play, and the only instrument that says so is somebody
   playing it.**
32. **The Iron Quarry** — three levels, withdrawn **without being played**, on the strength of what it
   looked like: nothing on it the player made and nothing that kept paying out after they stopped. The
   first time that answer arrived before a device did. Its rules survive as 32b, 32c, 32d and 30i.
33. **Hollowmarch** — fire a core into a walking line, three alike go off, the line **slides shut** into a
   cascade. The twist was that the line was *walking*. Deleted with Budburst and Emberforge (38).
33a. **The allowance is drawn on the board** — every core spent was a step the raiders took, so how much
   time is left is read off the board and the number in the corner is the same number (37b, 37v).
33c. **Ask which quantity is monotone**: a dumped core *added* to the line, so the board did not only ever
   shrink; what every legal shot advanced was the magazine. **It is not always the thing on the board.**
33d. **The chain clears the same threshold again on every wave** (20j), and what it is *worth* is bounded
   the same way, so **the mode's best move and its reward are the same move**.
33f. **A mechanic's reading is taken over the shortest answers, never over the opening move** — "can the
   first move set off a chain" collapses exactly when a board is good, so tuning against it selects for
   short boards.
33i. **Three shipped rungs is a test, not a chapter.**
34. **Emberforge** — the genre's own verb with the refill taken away: three alike **fuse** into one ember,
   tapped to sweep a cross. Every ember was three jewels not coming back, so *where* the board was spent was
   the whole game. Deleted with Budburst and Hollowmarch (38).
34a. **A refill is the one thing a searchable mode may never have** — it makes the material inexhaustible,
   the future unfixed and par unsearchable (26 restated).
34b. **The chain runs through the player's own work and stops the instant it reaches material that is
   not**, so the threshold a cascade clears on every beat is "did somebody build one here" (20m).
34c. **A finite board has two fail states and only one is the meter.** `life` plays the most extravagant
   possible player and counts how many moves the board survives — the mirror of `careless`: that asks
   whether thoughtlessness *wins*, this asks how long the board survives it.
34d. **Before pinning a difficulty reading, ask whether two moves can reach one state.** Both directions of
   a fuse settle on the same cell and it is provable — but carrying the swapped cells into every beat of
   the *cascade* let a group four beats downstream tell them apart, so **`ways` double-counted at every
   depth, on every board**, in the one reading 5d is about. Par never moved, which is why nothing else
   could see it.
34e. **A goal has to win a legibility fight against four jewels, and only a render can say whether it
   does** (32b, at the same cost).
35. **Kindlewake** — withdrawn after play with the verdict that the gameplay was not what had been
   asked for and the animations were bad. **That is one fault and not two**: the commission was *the
   classic question with gems instead of conduits*, and what was delivered kept the goal and
   invented a different verb, so nothing about it looked like moving jewels and nothing about its
   animation could.

### Spent ids — never reuse any of these

**Enforced by a machine** (add the id to the list *in the same change* as the removal, and the tool refuses
it outright): grove piece ids in `Tools/grove_retired.txt`; lesson ids in `Mechanic.Retired`, held to the
live set by `TipTests.EveryMechanicIsEitherLiveOrRetired`; level block names in `content.py`'s
`RETIRED_BLOCKS`.

**Enforced by nothing but this table.** A chapter or level id can be re-authored by accident, and a real
save may hold a record against any of them — which is exactly why `ProgressionStore`'s floors exist (9).

| Mode | Chapter | Level ids |
|---|---|---|
| `keeper` | `k01_grovekeeper` | `k01_first_grove` `k01_the_second_bed` `k01_stonecrop` `k01_the_boulder` `k01_four_petals` `k01_heartwood` `k01_twin_hearts` `k01_the_prism` `k01_the_pocket` `k01_keepers_grove` |
| `nectar` | `n01_nectarrun` | `n01_firstpour` |
| `ribbon` | `r01_ribbonfall` | `r01_firstribbon` |
| `fling` | `s01_seedfling` | `s01_firstfling` |
| `warren` | `w01_warrenwake` | `w01_firstparade` |
| `orbit` | `o01_hollowfleet` | `o01_firstbreak` `o01_sentryline` `o01_wardensgate` |
| `moonwake` | `m01_moonwake` | `m01_bellbreak` |
| `nova` | `v01_harvester` | `v01_firstlight` `v01_ironwatch` `v01_thehold` |
| `topple` | `t01_toppleglen` | `t01_firstfall` |
| `quarry` | `q01_ironquarry` | `q01_cutloose` `q01_wardenrow` `q01_thedeepcut` |
| `kindle` | `k01_kindlewake` | `k01_firstlight` `k01_stillwood` `k01_crossways` `k01_stonerow` `k01_thechoir` `k01_dimhollow` `k01_threefold` `k01_lanternweft` `k01_deepwake` `k01_kindleheart` |
| `bud` | `b01_thicket` | `b01_firstburst` `b01_catchalight` `b01_twiceknocked` `b01_sunspill` `b01_dewfall` `b01_widewild` `b01_honeylight` `b01_wildwaking` `b01_everbloom` `b01_thicketheart` |
| `bud` | `b02_tanglewood` | `b02_firstbolt` `b02_makefive` `b02_sunspark` `b02_crossfire` `b02_stormheart` `b02_sixfold` `b02_thundering` `b02_sunwell` `b02_wildstorm` `b02_stormcrown` — **and the fifteen the same chapter spent on two earlier cuts**: `b02_firstvine` `b02_longreach` `b02_deepthicket` `b02_windingway` `b02_twovines` `b02_thewilds` `b02_crossvine` `b02_thornedvine` `b02_thetangle` `b02_tangleheart` `b02_windrow` `b02_lanternfly` `b02_graftwood` `b02_puffhollow` `b02_hivehill` |
| `march` | `m01_hollowmarch` | `m01_firstcore` `m01_haulroad` `m01_thegate` |
| `ember` | `e01_emberforge` | `e01_firstember` `e01_twinlocks` `e01_ironribs` `e01_frostvein` `e01_chainfire` `e01_deepcell` `e01_ironward` `e01_twinstars` `e01_slaghold` `e01_emberheart` |
| `weave` | — | *(Lightweave; retired before its chapter shipped)* |
| `ripple` | — | *(Ripplewake; never authored a level)* |

`f03_wickwater` and its ten level ids are spent too, and are the one case where the *chapter* id moved
rather than being kept: that chapter never left the working tree, so nothing outside it could hold the ids.
**Invariant 1 binds *shipped* ids**, and where a real save can hold one the id is **kept and its string
changed** (5f).

**Other spent names, tokens and members.**
- **Board tokens, refused by name at parse rather than ignored** (5f): `x`; the wick letters `1` `2` `3`;
  `/` and `\` as a mirror; `%` as a sack and `!` as a *cell* (`!` lives on as a **wave** token); `*` as a
  cog; and the one-letter boss form (37z).
- **Fields refused by name**: `runners`, `winds`, `firefly`, and `reach` on a blast (39f).
- **Enum members kept rather than deleted, because their ordinals reach analytics on every run ever
  recorded**: `ContinueUnit.Tiles` / `.Taps`; `DefeatReason.OutOfTiles` / `.Overgrown` / `.OutOfTaps` /
  `.Barren` / `.OutOfTime`; `ChestDropKind.RunTime`; `SiegeKind.Weaver` / `.Thief`; `SiegeSpell.Weave` /
  `.Snatch` / `.Bombard`.
- **Retired in place on the wire**: `bestMillis` (22) — a field a rolled-back client still writes cannot be
  dropped from `hasOnly` without losing *every* save write (12a).
- **Other**: the map sprite `boat` (8d); the loc key `mode.cap.wards` (37v); the `run_continue` ad
  placement; the grove homes `home_longhouse` `home_tower` `home_keep` and the decor `barracks` `citadel`,
  whose models two dwelling rungs wear under new ids — **minting a new id rather than promoting the decor
  one is what keeps the stock honest**, since a decor id that quietly became a dwelling would be a purchase
  nobody made (16p).

### Thornwatch — the live mode
37. **Thornwatch is the first mode here that runs on a clock.** Raiders come down a hill at the ward line;
   match gems and the colour you matched fuels the **ward** of that colour, which looses bolts until its
   fuel runs out; the run ends when the last ward falls. Two proven loops bolted together with one thing
   changed: **nothing on the jewel board is a goal**, so a match is only ever worth the *colour* it was.
37a. **A mode may run on a clock, and what that costs is exactly one thing: par stops being a proof.**
   Walking raiders have no DAG and a refilling field has no fixed future, so par is **arithmetic** (the
   hill's health over the most one match could deliver) and the opening position answers **null** rather
   than pretending to be searchable. Nothing else changed — the graded count is still spent, the star lines
   are the same multiples, the fail state is real. **Par is a calibrated estimate rather than a floor**: the
   first version assumed a match clears three gems, and a match on a full field *cascades*, so the "floor"
   sat twice above real play and three stars was free.
37b. **The fail state is the ward line, so there is no move allowance at all** — a budget would be a second
   fail state counting down to an ending that never happens. `budgetFactor` is -1 on every siege and both
   gates **error** on one that is not; what replaces the meter is the line itself (33a). A fallen line reads
   as `Stuck`, so no continue is offered on that path (28f); the rescue is a different unit (23b).
37c. **Fuel used to fade on a clock and does not** — a meter draining while nothing happens reads as the
   game taking something away. **Removing it doubled what a match delivers, and that fell out in three
   places at once, none of them where the rule was**: par became generous, the line finished *untouched*
   (5d), and the level ran 32 seconds. All three were invisible except through 37j.
37d. **A level authors what is standing there and what is coming, and no numbers at all** (20d). Everything
   else is one constants file, so a retune is one edit — and because par derives from those constants,
   **moving one moves every star line in the mode at once**.
37e. **A refilling field is dealt deterministically** (9c's arithmetic), so no level authors a seed — **and
   it is dealt again rather than allowed to lock**, because this clock does not stop and a board with no
   legal swap is a run the player watches themselves lose.
37f. **A raider says its colour three times and none is enough alone**: an untinted body wears a colour the
   player must *learn*, a tint alone is a difference only some people see, an aura alone is lost when two
   overlap. **Before drawing a rule in colour, count how many ways it is said.**
37g. **Three bands is a board no gate can look at, and a render moved two of them.** A ward's furniture is
   nearly two cells tall against a band of one and a half, so the fuel tube fell behind the field's plate
   and **the one readout every decision rests on was invisible**. `CRAFT.md`.
37h. **Only a ward coming down flashes the screen** — a flash on every blow is five a second once a wave is
   at the line, at which point it reads as a fault (26f asked of a drawing).
37i. **What a played run still has to answer**: does fuelling a colour read as the verb; is par a line a
   good run can get under; does a cog read as an upgrade rather than a blocker; do the four bosses read as
   four fights (37z)?
37j. **A mode with no search needs somebody to play it, so one is written down.** The hold simulation plays
   an ordinary player and asserts the hill is cleared with the line standing **and visibly damaged** — as
   first authored the level **could not be held**, with every other gate green, and the second assertion
   matters as much, because a line nothing reaches is a fail state that rejects nothing (5d). **A mode with
   no search has exactly one instrument, and every rule change goes back through it.**
37k. **A wave comes on a clock *or* the moment the hill is empty, and it took both.** On the hill-empty rule
   alone a player who was winning met no pressure; on the clock alone a player ahead of it watches an empty
   field. The shortcut cannot fire before the first wave, and the cliff is two seconds wide either side.
37l. **`Image.color` is a multiply, so a tint can only ever darken.** Tinted wards came back "too dim";
   lifted toward white, **pastel**; there is no third setting, so they carry a baked **hue rotation**. **A
   tint says which of several things this is; it does not make something look lit.**
37m. **"Has fuel" reads as brighter, never "no fuel" as dimmer** — fading an unfuelled ward dims it on the
   first frame of every run. A light goes *up*.
37n. **A toast grows to fit what it says, and renders its markup.** A fixed box overflowed into five lines
   drawn outside the plate, because **a Unity `Text` that overflows is not clipped and nothing says so**;
   and it drew `<b>` as four letters, because the label turns rich text off — correctly, since a keeper's
   name reaching a label is a string another player wrote. **Both faults were in shared code.**
37o. **A siege says the word on the board, because its board does not stop.** Other modes' boards visibly
   halt, so a modal a beat later is enough; a hill keeps walking. The opening countdown is the same argument
   from the other end, and the clock runs underneath it so the count tells the truth.
37p. **A hue rotation goes *most* of the way, never all of it** — one hue collapses an artist's trim and
   dome into a flat shape, where 80% keeps a fifth of the spread. The blend is circular.
37q. **Two sounds a frame apart are a flam, not emphasis.**
37r. **A mode's defeat is its own piece of news.** Reusing `Stuck` showed "NOTHING LEFT TO DO" over a hill
   full of raiders. `WardsLost` is its own ordinal because analytics cannot tell two endings apart
   afterwards, and because running out of *board* is a level-design reading where an outpaced line is a
   tuning one. **And the panel says it, not the board.**
37s. **A move's *effect* may not land before its animation does.** Fuel credited at the swap reached
   the wards first: **a turret killing a raider before the gems it was paid for had burst.**
   Turn-based modes are immune by construction; a clock is not.
37t. **The last wave is a boss, and what a level authors about it is one token.** It walks to the
   middle, **stops**, and throws from where nothing can reach it, so the finale is a duel. **Which
   wave it is in is a rule, not an authoring decision** (4a, 33g), so it can be neither typed into
   the middle nor left off the end — and because it is a real wave, the wave count, muster, banner
   and par take it with no special case.
37u. **A boss is drawn big, and three of the four things that make it read are *placements* no gate
   can look at**: several cells tall, standing still, health pinned **across the top of the board**,
   and a spell that is a different *kind* of object rather than a bigger bolt (33e).
37v. **The header counts waves, not wards** (33a the other way round). The ward line already *is* a picture
   filling the middle band, so a corner reading of it was a second copy of what the player was looking at —
   while how far through the raid this is existed **nowhere but a banner that fades**. **What a mode owes
   the corner is the thing its board cannot say.**
37w. **A ward can be upgraded, and the mechanic is one question asked about the *line* rather than
   the hill.** Five tiers, each ten per cent more damage and ten per cent less fuel a bolt,
   multiplying. It passes 26h: the player decides which colour to spend a cog next to, and can be
   wrong — a cog taken by a fallen or maxed ward is spent for nothing, and the view draws that by
   having it go **nowhere**. The first neighbour in cell order wins a contested cog and is *stated*
   rather than emergent, because a `HashSet` walk is not promised to enumerate the same way on two
   runtimes. <br>**Ten per cent is only exact because every damage number was multiplied by ten** —
   at the old scale four of five ranks bought nothing, and the alternative was a float.
37x. **A second boss is one token, four constants and three art reels**, and the ordinal is **appended**,
   because these reach analytics on every run recorded. **Bake an effect and look at it beside the one it
   has to differ from.**
37y. **A readout belongs where the thing it is about is** — on the chassis the fuel tube read as
   part of the machine. **The reason it could not simply be moved is the layer, not the number**: a
   turret's foot is already behind the field's plate.
37z. **A boss is a way of fighting, and telling two apart by a hue is not telling them apart at
   all.** Four encounters were built from **two** kinds separated by a run-time tint: *the bosses
   look exactly the same*. A boss that only does what the boss before it did, harder, competes on
   **degree** (26h at the end of a chapter). <br>**So there are four kinds, and what separates them
   is what each one *takes***: fire (fuel to nought and seconds of dark, no health at all); health;
   the whole line at once; or the **rank they earned**. Four different answers follow and only two
   are a mending.
37aa. **The wards fire twice as often for half as much, and the whole of that is one identity nobody
   had written down.** `PerfectMatch` read `gems x damage x 2`, which is the same thing only while a
   gem buys exactly one bolt — never said out loud, and every par rested on it. Left alone the
   formula would have **doubled every par in the chapter**, each number plausible and every gate
   green.
37ab. **A rung is fought over a ground of its own** — arithmetic on the level's place in its chapter
   (7c), so a second siege chapter costs no art.
37ac. **"Boring bosses" was a complaint about the drawing, and the answer was to *spend* the window
   rather than shorten it.** The reflex fix is the cadence, and the cadence is a **rule** (37s), so
   **not one number moved**.
37ad. **A boss that cannot bring a ward down is not a wave of its own; it rides the last one.** One
   drew the longest quiet in the mode and then walked onto an empty hill, because the muster fires
   the moment the hill is clear (37k) — so seconds of one turret's dark over an empty hill cost
   **nothing**, with every gate green.
37ae. **Every turret a player buys throws an effect of its own** — nineteen priced turrets firing
   the free one's bolts is 26h asked of a *purchase*, and **two turrets sharing an ability are two
   readings of it, never one shape in two tints** (37z).
37af. **Baked VFX are authored to be seen through bloom, and a bake that renders without any ships the
   geometry with the light left out.** "Too shallow" was an alpha exponent thinning every reel *and* a pack
   whose own demos run a post-processing stack this bake did not. **Before tuning a bought effect, check
   what the artist expected to be applied to it.** Recipe in `CRAFT.md`.
37ag. **A projectile can be right while the flash and impact it names are wrong**, and giving up the flight
   to fix them is the wrong trade. **Where the pack has no silhouette left worth having, two are layered
   into one**, flown as one object so their trails agree (32b). `BoltScale` is the one place a projectile
   may differ in size, because a reel is framed around its own content.
37ah. **Which turret throws the elemental set is a decision, and the day it moved it stopped being
   derivable.** "The turret with no ability fires what the *colour* fires" was honest only while the
   free turret was the only model without one; it has moved three times, so a field names it —
   **never spelled `IsStarter`** (16j's trap, and already wrong).
37ai. **A turret's effect and the name over it may be swapped; its id may not.** **The labels move to
   different ability slots**, so if a name is meant to keep its rung the price and the ability have to move
   too — a balance change, not a re-skin.
37aj. **A bake fix applied to one kind of reel is not applied to the others** — five faults in one
   reel, each invisible: the anchor, the recipe, the exposure, the camera and the trim (`CRAFT.md`).
   **When a fix is a recipe, grep for every caller that builds one.** <br>**And the anchor was wrong
   a second time in the one way the eye could not see** — right shape, wrong sign, putting the flash
   onto the player's own turrets.
37ak. **The gem is what a colour *is*, and everything else agrees with it.** One pair was 9 degrees
   apart — an orange gem feeding a yellow turret — and nothing could say so, because agreement was
   bought by construction and that stopped being true the day the gems started being **cut**.
37al. **A shelf belongs to the display and its cells to the safe area, and reading those as one
   thing put a band of nothing at the foot of every iPhone.** The plate runs to the physical bottom
   and the cells stand above it.
37am. **A request that names a removal and a replacement in one breath needs one question: which of
   them does the replacement attach to?** Both readings were grammatical and only one was a game. It
   cost nothing on disk because the slot's `.meta` guid was restored rather than re-minted (7b).
37an. **The header row moved into the bar's own band, and what paid for it is that its captions had
   never been visible.** `ProtoView` sizes its plate to fill its host **exactly**, so **a host inset
   is a hard edge, not a margin, and nothing anywhere says so** — two thirds of every caption had
   been drawn behind an opaque panel since the mode shipped.
37ao. **A field that has gone quiet points at a match, three times a level and never while anybody
   is playing** — five seconds of a *live* board with nothing happening is a player who cannot see a
   move.
37ap. **"Super fast paced" is two dials, and only one may be turned on its own.** Slowing the
   cadence alone is not available, because the cadence is the line's *output* and the raiders do not
   wait — so the bolt got twice as heavy in the same breath (37aa backwards), and par came back
   bit-identical, which is what says it was free.
37aq. **The mode's one instrument was a coin toss, and it had been green by luck.** It played each
   rung **once**, at one player rhythm — and two rhythms **twenty milliseconds** apart play out
   completely differently. Swept over nine, two rungs held at **two of nine**, and the shipped
   rhythm was one of the two.
37ar. **The raid is insects, and what a one-pack cast costs is a thing to say out loud rather than
   discover later.** Surveyed first: nine monster packs hold ninety-six characters and not one
   insect, and the only ones anywhere are fifteen in the same kit the turrets come from — drawn
   **top-down**, the one view this board has.
37as. **An offset is a fact about what the body is.** A shadow at 0.46 of the drawn height is right
   for a *biped seen from the side* and puts a top-down insect's shadow a body-length clear of it.
37at. **A sprite can be *baked* instead of bought, which turns the camera angle from a purchase into
   a decision.** The market has no more top-down casts — "top-down" nearly always means a
   three-quarter RPG view — so a second cast is rendered from rigged 3D at this board's own camera,
   and what ships is PNG reels.
37au. **"Stretched and low quality" was two measurements, and the stretch had been on every device
   this game has ever run on.** A ground drawn as a plain `Image` sized to its band scales
   independently in x and y. **Envelope, never stretch** — so what varies is how much of the edges
   is cropped, which means the art overhangs and needs a clip of its own.
37av. **The ground is a plain grid of small tiles, and what it is really a record of is five rounds
   spent pushing the wrong axis.** The instruction was four words: *do not try to design anything,
   just use pure plain tiles*. Each of the five before it was more deliberate than the last, and
   every round answered a complaint by being **more designed**.
37aw. *(Superseded by 37av.)* **Whenever two normalisations decide whether one thing reads against
   another, the invariant is the gap, and a written-down absolute is a gap nobody is checking** — the
   ground's mean stood a third *brighter* than the cast normalised to walk over it, the plate rule
   inverted in a comment citing it. **And a rule about board colours is about every board colour, so grep
   the palette rather than the thing that prompted it.**
37ax. **The shelf is ordered by how much of the hill an ability reaches, and it shipped the other
   way up.** **Reaching two or three raiders a bolt beats any amount of help aimed at one**, so
   multi-target abilities have to be dearest — and **the two halves of the shelf must agree**, which
   they also did not.
37ay. **An effect belongs to a turret and an ability to a rung, and after three rounds of tuning
   those stopped lining up — which is fine, and had to be said out loud.** Moving an effect costs
   **no re-bake**: reels are graded by *colour* and every reel of a kind has the same frame count,
   so exchanging two turrets' folders is byte-for-byte what re-baking with the prefabs swapped would
   write.
37az. **"Two of a thing" is only two if the gap beats the size of the thing — measured against what
   is *drawn*, never the art it depicts.** A twin-barrelled hull firing one bolt is a turret with a
   second barrel painted on, so each barrel gets its own flash and comet — **drawing and nothing
   else**, since a second barrel that dealt damage would be a purchase hitting twice, and **keyed on
   the rung, never the id**.
37ba. **An exception to the colour rule is affordable when the rule is said somewhere else.** Frost
   throws a white bolt on every ward, safe because what says a hit was double is the gold figure,
   the ring and the sparks.
37bb. **A turret carries a bolt weight and a toughness of its own, and the two are not symmetrical:
   one may only ever go up and the other is the trade.** Par is health over `PerfectMatch` against
   the **baseline** bolt, so a turret hitting *under* it would push three stars out of reach of
   whoever bought it — a grade decided by a purchase, on a number that reaches a public leaderboard.
37bc. **A shelf is read in three bands, and a band is a label on an order that already exists.** It keys on
   the shelf rung rather than on ids, **nothing may key on it**, and the grid is laid out by a **cursor**
   rather than `i / Columns`, because index arithmetic with a header spliced in disagrees the first time a
   band holds a number that does not divide.
37bd. **A boss may not be a raider drawn three times the size, and a chapter may not be half boss
   rungs.** **Two to a chapter, on the fifth rung and the tenth** — the gate exists because a boss
   token is one field on one rung and every other gate is happy with a boss anywhere or nowhere.
   **The four verbs are dealt one each across twenty rungs**, since a verb met in each chapter is
   one fight met twice.
37be. **"Hard with the default turret, doable with a little better" is a measurement, and making it
   one meant playing a *chosen* line for the first time** — every gate plays the fallback line.
37bg. **A turret can be upgraded to five stars, and a star count is the rarest thing in this save: a
   stored number that may be stored.** 11b refuses a stored count outright — and an upgrade cannot
   be undone, so the join is a per-key `max`. **Before storing any count, the only question is
   whether it can fall.** Keyed on the **holding**, not the turret.
37bh. **An upgrade is three screens, and the middle one exists because a decision needs both
   numbers** — the figures where the turret *is*, the gain beside each, and the price. **The payoff
   is the numbers moving, so the numbers are the ceremony.** **And the fix for a dead readout
   uncovered the real one**: guard 8 at ten per cent is 8.8, which truncates back to **8**, so the
   second star bought nothing.
37bi. **An accessibility rule is about what is on the board at once, so what invalidates it is a
   change to *who draws it*.** Four elements, one per colour, is right while the *free* turret
   throws them and every line is four starters; a line stands one turret per seat, so the four never
   meet unless somebody buys the same turret four times.
37bj. **A count-in is part of the run, so it is paced by the run.** `RunHold` stops the board's
   clock and cannot stop a **wall clock** — two clocks agreeing only because both were the same
   length, so a player who reads the first-timer's tips is handed a hill with a wave already due.
37bk. **A lesson goes when the board demonstrates the rule the first time it is met.** Withdrawing
   one is three edits, and **the `Mechanic` member is kept**, because a lesson id travels in the
   save and re-pointing one tells a player they have been shown something they never saw.
37bl. **A turret only ever fires at its own colour, and every other rule here is a consequence.**
   Reported as *I barely look up*. **The elemental double was a bonus nobody had to earn** — four
   wards fire at once and each found its own colour, so "take the biggest match" was *correctly*
   optimal — and **the clock punished deliberation**, so the right algorithm was the first match the
   eye lands on.
37bm. **Two correct branches whose union leaves a state with no answer.** A panel offered **one**
   key and chose what it said: for an owned turret it sold the next star and only fell back to
   standing it when there was none left — and **every turret starts at one star**, so a
   twenty-turret shelf could not be reached at all.
37bn. **A latch meaning "the board is resolving" must not take the player's hands off everything
   else on the screen.** `Busy` is a fact about the *field* — a good reason to refuse a swap and no
   reason to refuse a finger that has gone somewhere else — so the whole hill went dead for the
   length of every cascade, at the moment most worth acting in.
37bo. **A siege's model resolves in an instant while its drawing runs on, so "the run is over" and
   "the screen has finished saying what happened" are two moments here and nowhere else.** Judging
   returns while `Busy`, and **both endings are held**, because a line falling mid-cascade is the
   same picture with a different panel.
37bp. **A run says which level it is, and the header had no horizontal room for it.** It is **the
   map's number and not a new one** (a position inside the level's own mode *and* track), because
   two answers to "which level is this" is the one thing this may not be — and it is display only,
   so a level the catalog has never heard of draws **nothing**. Written once on `RunScreen`, because
   there are two headers.
37bq. **A boss is answered by the whole line, and its colour decides the *double* rather than the
   permission.** The lock (37bl) has one shape it cannot hold: a duel is one raider wearing one
   colour, so three of the four turrets a player chose banked fuel they would never spend and the
   finale was fought by a quarter of the loadout, against the biggest number in the mode. A boss is
   what a ward shoots when it has **nothing of its own left to shoot** — last after its own colour
   and a prism's partner, because a boss holds the middle of the hill while its escort walks at the
   wards. <br>**The second half is the one nobody would have written down: a part-weight bolt has to
   cost a part of the fuel.** A ward with nothing to fire at *banks*, so a half-weight shot at full
   price is not a free extra hit — it is the player's fuel converted at half the rate it would have
   been worth a few seconds later, spent on their behalf, and it **cost three runs in ninety on a
   change meant to help**. Proportional cost is also what keeps `PerfectMatch` an identity over a
   duel (a unit of fuel is worth the same damage whoever burns it), which is what paid for **fifteen
   per cent more boss health**: at a quarter more the finale falls from 5 of 9 to 3.
40. **A mechanic that attacks the *field* was the hole this mode had** — everything could only hurt the
   wards, so the field was a fuel tap the player operated while looking somewhere else.
40a. **A mechanic nobody authored into a wave does not exist, and it shipped that way for two
   chapters.** Two raider kinds were built — rules, caps, art, view — and **not one shipped level
   ever sent one**, with every gate green, because a level that sends none of something validates
   perfectly.
40h. **The hill may not reach into the gem board, and finding that out cost building it.** Two raiders were
   rebuilt to *stand on the gem field*, and the verdict was immediate: **a raider has no business standing
   among the jewels.** The gem board is the player's. What survived is the same idea with the arrow reversed
   — **the connection is the player reaching up, not the hill reaching down** (40i).
40i. **A bomber is a creeper the player is pleased to see.** Where it dies it leaves a **live bomb
   standing on the hill**, so the hill stops being a thing that is only watched and nothing is put
   on the player's board to achieve it. **It goes off where it stands, immediately** — an aiming
   step was built first and is wrong, because a bomb is already somewhere and **the decision is
   *when***.
40j. **A raider that pays the player has to be priced as one, and the measurement inverted the
   obvious answer.** "A brute with cargo" reads well and is wrong, because what it is worth killing
   is set by what it *leaves*: at nine rhythms, a brute-weight bomber **cost 16 runs in 90** and a
   creeper-weight one **gained 4**. What keeps it from being free money is that a level authors
   **one**.
41. **A random stream is part of a level's content, and anything that changes how often it is drawn
   from is a content change.** The hill's lanes were drawn from the *field's* stream, which reads
   like an accident and is load-bearing: every gem dealt after the first wave depends on how many
   times the muster has drawn.
43. **A mode may have a second ladder, and it is a *track* rather than a mode or a chapter.** Filing
   it as a mode gives it its own switcher row, art, validator and chapter ladder; filing it as an
   ordinary chapter is worse, because the gate looks for the chapter *before this one in the same
   mode* and would gate a real chapter on stars nobody can earn. `GameTrack` is one level finer than
   `GameMode`, the index lanes on the **pair**, and `ChaptersIn(mode)` answers the **main** track
   alone — which keeps `Next`, `OrderOf`, `IsLast`, `GateFor` and `NextToPlay` correct with no
   change at all.
42. **A player chooses the line, and the load-bearing rule is that no turret may make a bolt
   weaker.** Twenty turrets, each standing on a colour the player picks, carried into every rung.
42a. **A shop that only lets you see what you have already bought is asking for a decision it will
   not show you.** The preview answers both taps: what the turret does, and what it looks like
   firing. **It costs the held case one extra tap**, which is the right price.
42b. **The map carries the line it is about to send.** A LOADOUT button announces that a feature
   exists and shows none of it; the bar is a readout along the foot and is also the door, because
   two ways into one room is one too many. Both rows are spread by **even shares**, the only
   arrangement that stays centred when the safe area changes shape, and a kit nobody holds shows an
   **empty well** rather than a dimmed picture.
42c. **A turret is bought for one colour, the shelf is one ladder climbed a rung at a time, and
   every rung carries a keeper level.** Each is small alone and each closes a hole the other two
   leave. **Per colour**, because a line holds four and which colour a trick is worth having on is
   what makes the shelf a choice (26h). **One rung at a time** (16j). And **a gate on every rung**,
   or the half priced in the currency that can be *bought* skips the ladder outright. **The climb is
   forced rather than chosen**, so reaching a rung means having met every gate under it — therefore
   a gate that does not strictly climb can never refuse anybody (5d).
42d. **A preview that does not show the thing being paid for is a thumbnail with a sentence over
   it.** One raider and one bolt previewed three abilities *identically to the free one*. It stands
   **the arrangement each ability is decided by** and fires exactly what the board would report —
   **mirroring the rules rather than staging a demonstration**, which is what stops a preview and a
   hill disagreeing.
39. **A utility buys a finish and never a grade, and in Thornwatch that is arithmetic rather than a
   policy.** Everything about the shape follows from one question: *how many matches would this have
   saved?* **Why it has to be asked**: a grade is not a private number — stars derive credits,
   credits are a grove's worth, and a grove's worth reaches a public board (19a) — so a consumable
   that made a run score better would move a public figure, and utilities are **not adjudicated**.
39a. **The stock is two counters per id, and it is the first thing here that needed both.** `earned`
   and `spent`, each monotonic, joined by a per-id `max`, with what is in hand derived and clamped
   at nought. Hearts come back on a clock; grove decor is bought and then *stands somewhere* (16h).
   A utility is granted, used and gone — nothing else in the save implies it existed — so both
   halves have to be written down.
39b. **A chest may pay a utility, and that cost the server nothing.** One kind and an **item id**,
   because an enum member per item makes every addition a code change and a renumbered contract
   (9c).
39c. **An icon is not content, so adding a utility is a build.** Prices, strengths, ceilings, order
   and which chest drops what are authored and retunable; a picture is in the app. Both gates
   **error** on an entry whose icon the manifest does not name, rather than letting it draw a white
   rectangle (7b), and both resolve the derived name and note, which are invisible to `loc.py`
   exactly as a story line is (30d).
39d. **The bar is furniture, not three buttons.** A strip of loose squares came back as *"not even an action
   bar"*: nothing said the row was a *place* things are kept. **Five cells, and three of them hold
   something** — a bar sized to the catalog would move every slot under a player's thumb the day a fourth
   ships. Drawn in a kit's own sampled palette rather than cut from it (`CRAFT.md`).
39e. **A sound is a piece of news**, so a firepot bursting does not share the clip a raider's death plays
   thirteen times a wave. **A second source pack was needed and that is the shape of the finding**: a
   library of *physical* noises has nothing that reads as a spell rather than an object — and a row that
   names no prefix still means the original pack, so nothing already in the table moved.
39f. **A target has to be a place, not a distance.** Aiming by dragging a ring was exact in the rule
   and unreadable on the board: the player judged a distance against raiders that were walking, and
   the ring said how far it reached while nothing said what was in it. The hill is a **grid** now —
   33g at its strongest: the drawn thing and the played thing are the same two integers.
39g. **The board runs to the edges of the screen, and the field is laid out to the width.** Taking the cell
   as the smaller of what width and height allowed put the gem field in a column with empty plate either
   side while the bar below ran edge to edge. **And the plate is rounded at the top and square at the
   foot**: **round the end that is open and square the end that meets something.**
39h. **The kit is a shop shelf, and it lists no product and no good.** Reaching the shop only
   through an empty slot is wrong twice over: reachable only mid-run, and the one screen where the
   answer to "I want more" is somewhere else.
39i. **A panel over a run holds the run, and the rule had to live in the frame rather than in the
   panels.** The shop opens over a live siege and the hill kept walking: a player who tapped an
   empty slot was reading a price while raiders closed on their line.
39j. **A stock prices how often across a lifetime; only a cooldown prices *when*.** Bounded by what
   a player holds and what it costs, a hundred firepots was a hundred taps in four seconds — a mode
   decided by inventory rather than by play, and invisible to every reading here, because *how
   quickly* a player may act is not a question anything in this project asks.
39k. **A target is a picture, so the rule has to read the picture.** Three faults, each invisible,
   two of them under a comment claiming 33g held. **The drawn grid was not the played grid**, laid
   out from a different origin than the rule read.
39l. **A scanner finds a wrong name and can never find a missing case.** A chest paid a utility and
   drew a **white rectangle**, because the art lookup took a *kind* and nothing else, so the new
   kind fell through its `default` and answered **null** — and every caller hands what comes back
   straight to an `Image` (7b). `artnames.py` proves a name a call site *asks for* resolves; there
   was no name to check.
38. **A mode hides behind one manifest boolean, and deleting one is a session.** **Hidden** means
   `"disabled": true` and nothing else — every file, board and screen still stands, so putting it back is a
   handful of booleans and no code. **Deleted** means the mode class, board, view, screen, validator,
   reading, chapter bodies, art, offline mirrors and tools all go, with the ids spent. **Deleting three
   modes at once cost the save file no schema version, no merge rule, no `firestore.rules` change and no
   server work** (20a) — and the honest other half is that it cost thirty-three levels' worth of derived XP
   and credits, which is what `ProgressionStore`'s floors exist to stop a player noticing (9).
38a. **The front door is `LevelModes`' first entry.** Index nought is what the switcher offers first
   *and* what a map with nothing remembered opens on, and those were briefly two answers — a map
   opening on one mode while the control above it offers a different one first, which no gate would
   catch.
44. **The whole UI is one bought interface kit, and it moves in one commit because the names are
   already roles.** `Skins` says `btn_green` and has meant "do the thing" since this UI was written,
   so ninety-odd call sites were already naming a *role* while appearing to name a colour — and the
   way to restyle every screen at once is to **re-cut what those names point at** rather than to
   sweep the call sites.
44a. **Upscaling a nine-sliced sprite makes its unstretchable ends bigger and its usable middle smaller**, a
   border being drawn one *sprite pixel* per UI unit — cutting a readout plate at twice its size left
   **fifty units of interior for a five-digit number**. **Scale a sliced piece for the size its corner
   should draw at, never for resolution.**
44b. **A face lift is a property of the art, not of the caller**, and is **measured, not typed** — the tool
   prints it on every run off the moulds it actually cuts, so re-cutting re-answers the question instead of
   leaving a number that used to be true. **Keeping a question whose answer was nought is what made the
   next kit cheap.**
44c. **A backdrop's crop is a decision for the tool, never an offset in the screen.** A screen can only crop
   by *offset*, which is a number nothing checks and which two screens would each have to get right.
44d. **Two screens made of the same furniture get one mirror**, sharing a module that mirrors
   `UIKit` and `Skins` — two copies would be two answers to questions this project settled once.
   **And a mirror has its own bugs**: three "faults" were the render reading an `anchoredPosition`
   as an edge when `UIKit.Box` always pivots at **centre**, and reading Unity's up-positive y as an
   image's down-positive one.
44e. **A `switch` whose `default` is a real answer hides the case nobody is looking at.** A shelf
   accent named four shelves and returned one palette for the rest — silently wrong for one that
   draws no tab today and would have shipped cards identical to another's the day somebody turned it
   on, with the enum exhaustively handled as far as the compiler is concerned.
44f. **A kit drawn for light screens cannot be cut as it ships into a game that writes light text**
   — changing the *text* would be the ninety-call-site sweep 44 exists to avoid, so what changes is
   the art.
44g. **Dimming is not how you recolour something warm** — multiply takes a colour toward black
   **along its own hue**, so amber arrives as **brown**. Three things made it worse than a wrong
   number: the fault **scaled with area**, so what looked acceptable in a contact sheet was what had
   not been tested; the rim was a frame rather than a rim; and three elements picked in isolation
   gave the screen three unrelated hues.
44h. **A restyle that is only a re-cut can still ship a boring screen, and the two halves that fix
   it are the *plate* and the *ground*.** Every plate on the rejected kit was a **cream rim around
   the ground colour**, with one rim of one width everywhere and a flat near-black wash behind it.

## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Modes/ Wards/ Utilities/ Persistence/ Progression/ Homestead/ Cloud/
  Localization/ Analytics/ AssetPipeline/ Store/ Ads/ Daily/ Events/ Social/
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
Assets/Game/Authoring/             GlimmerGrove.Authoring    (Editor-only; Domain)
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode)
Assets/StreamingAssets/Content/    manifest.json, chapters/, homestead.json, loc/
```

**`GlimmerGrove.Authoring` is the home for a rule that decides whether content is fit to ship and that no
player ever runs.** Such rules are under two constraints at once — the build gate has to reach them, and
so does the test assembly, which references `Domain` and *not* `Editor` — and for a long time `Domain` was
the only place satisfying both, so three authoring checks were compiled into every player build and never
called. **A rule belongs there when no shipped type references it**, and `compile.py` proves it by building
`domain` *without* `Authoring` on its reference list.

**A mode is declared three times.** `LevelMode` (Domain) says what a mode *is*; `ModeLook` (Presentation)
what it *looks like*, split off because Domain may never reference Presentation; `ModeValidator`
(Authoring) how it is *proved fit to ship*. That last split is what let the validator leave the player
build: `Validate` had been a `virtual` member, so the authoring entry point called into the mode and the
mode called back, pinning both wherever the runtime could see them. **The price of a registry over an
abstract member is that an entry can be *missing* where an override cannot**, and a missing one is a green
tick over a mode nothing looked at — so an unregistered mode is an **error**, and a fixture fails the build
when the two registries drift.

## Verifying

The Unity Editor is often not running, and the MCP bridge is unavailable whenever scripts fail to compile.
Do not guess — verify offline.

- **Compile:** `Tools/verify/compile.py` runs Unity's bundled Roslyn directly. See
  `verify-content-without-unity` in the memory directory for the exact command.
- **Content:** `Tools/verify/content.py` — parse the StreamingAssets JSON, prove every level
  solvable, derive par, confirm every loc key resolves, and run the shared board vectors through
  both Python copies of the four-armed-tile rule. **It reads the manifest**, so a chapter carrying
  `"disabled": true` is skipped whole and the hidden glade, Lightfall and Prismvale chapters are not
  proved on a run (38).
- **Inline rung tables:** `python Tools/verify/rungs.py` holds the hand-copied rung arrays in
  `SiegeRuleTests` to the chapter bodies that ship. Twenty rungs are written down twice — once in the
  chapter tool, once in C# — and when they drift **nothing fails**: both parse, every gate stays green, and
  the hold simulation happily measures a chapter nobody ships. The hand copy is not optional (a fixture
  that loads JSON needs `JsonUtility` and is skipped offline), so the gate is.
- **Difficulty:** `python Tools/verify/difficulty.py` — what each glade actually asks of a player, counted
  rather than argued about. Not a gate (5d).
- **Sprite names:** `Tools/verify/artnames.py` proves every sprite a *call site* asks for exists on
  disk. **Written the day the gap cost something**: a view spelt its own folder out, so every burst
  drew a white rectangle over the board (7b) — and every other gate was green, because the audit and
  the address probe prove what is *on disk* is addressed and what a **mode declares** resolves, and
  none proved that a name a call site asks for resolves to anything.
- **Sound and music names:** `Tools/verify/sfxnames.py` proves three lists agree — what the code plays,
  what is on disk, and what the manifest preloads. A misspelled name was a runtime exception and a silence
  that shipped green. A screen's music track is checked the same way, and one written in any shape but a
  literal or `null` is an **error** rather than skipped.
- **The turret roster:** addresses are **built** from an id, which `artnames.py` cannot see, so both
  content gates walk the roster and error on a model whose pictures are not on disk or whose derived loc
  keys do not resolve. **Stronger than a literal rather than weaker: it catches a missing file and a
  misspelled id at once** (42).
- **Word list:** `Tools/make_name_blocklist.py --check`; the filter itself is
  `npm --prefix firebase/functions test`.
- **Name fold:** `Tools/verify/names.py` runs the fold against the shared vectors **on Unity's own Mono**,
  not the bundled .NET. That is the whole point: the first version ran on .NET 8, whose ICU agrees with
  Node, and passed happily with a mapping deleted. **A check that cannot fail is not a check** (19e).
- **Why a test says "needs the Editor":** `GLIMMER_WHY=1 python Tools/verify/tests.py` prints the native
  call that stopped it — sometimes a fact about the code under test, sometimes a limit of the runner, and
  only the message tells them apart.
- **Art tools and renders:** all of them, with their flags and what each has caught, are in `CRAFT.md`.
  Two rules that belong here: **`--check` proves reproducibility and says nothing about quality**, so the
  `--contact` sheet is the gate that matters; and **a new mode's art tool and render mirror are committed
  with the drop they were written for**, because an uncommitted tool is deleted for good by the withdrawal
  — which has already cost one.
- **In the Editor:** `Glimmer Grove ▸ Validate Content`, `▸ Validate Art`, and Test Runner (EditMode).
  **Reload the domain before believing a failure that follows a play-mode session** — `Boot` starts the
  cloud backend and its threads, and those statics survive leaving play mode, so a sync still running fails
  a test that passes on every clean domain.

Builds are gated: `ContentBuildGate` fails the build on any content error.

## Hard-won facts

**Unity and the build**

- **Addressables must be ≥ 4.0.1.** 2.x calls `Object.GetInstanceID()`, which Unity 6000.3+ made an
  *error*-level obsolete.
- **`GLIMMER_ADDRESSABLES` comes from asmdef `versionDefines`, not Player Settings.** Player Settings
  defines are per build target — one added on Standalone is absent on Android and iOS, which would ship a
  mobile build with no art and no error explaining it.
- **`m_BuildAddressablesWithPlayerBuild: 1`** is set in the project asset, not left to the per-machine
  Editor preference, so CI and teammates build identically.
- **A Unity magic method is an engine rule, not a language rule, and the offline compile is blind to it.**
  `public bool Awake(int i)` compiles perfectly in Roslyn and the Editor then refuses the whole script, so
  a green `compile.py` is not by itself proof the Editor will accept a build. It walks every class that
  ends up a `MonoBehaviour` and refuses a method named after one of the no-argument messages; the
  parameterised ones are deliberately not listed, or the check becomes noise.
- **Unity only re-resolves packages and reimports on window focus.** If a change seems not to apply, the
  Editor probably has not been clicked.
- **`refresh_unity` says `compile_requested: true` without compiling anything**, which is worse than not
  asking: a probe then runs the *previous* assembly and reports a fault fixed minutes ago, or passes a
  check against code that is no longer there. The reliable force is
  `CompilationPipeline.RequestScriptCompilation()` through `execute_code`. **Before believing anything a
  bridge probe tells you about code you just edited, check the DLL is newer than the file.**
- **A gate that reads the file system lies while Unity is reimporting, and it lies by *failing*.** Writing
  a thousand art files with the Editor open sends it into a reimport that transiently takes folders away:
  `content.py` reported 174 errors, then 181 eight seconds later, then **0** once it settled. **The tell is
  that the failure count *moves*** — a real content failure is deterministic, so run it twice and compare
  before chasing a diff.

**Serialisation and arithmetic**

- **A `[Serializable]` class field is never null after `JsonUtility`, so never test one for null.**
  It instantiates the field even when the JSON has no such key, so `dto.block != null` is true for
  every level ever parsed — which read forty levels as carrying a block they did not have and failed
  the Android build with eighty errors. The fixed shape is a value a real block cannot hold
  (`IsAuthored`), which is 11b from the other direction, and `compile.py` refuses a null test on any
  class-typed DTO field.
- **`LevelDefinition.Layout` is null on any level that is not a glade, and nothing in the language says
  so.** Nullable reference types are off, so a reader that forgets is a `NullReferenceException` in
  whichever tool touches it first — which is what shipped. `compile.py` refuses a file that reads
  `.Layout.` without anywhere saying it knows the thing can be absent. Coarse on purpose: a false positive
  costs one word, a missing guard costs a crash.
- **`JsonUtility` has two parse refusals that read as logic bugs.** It rejects a number written
  `.5`, and it **truncates a string at an escape sequence** — which shifted every expectation in a
  shared vector array by one field and read exactly like a bug in the code under test. Vectors
  carrying awkward text carry **code points** alongside the string, and the other runtime asserts
  the two agree.
- **Nothing that decides a cell may be a `float`, because the runtimes disagree about them.** A cap
  computing 12.99999952… truncates to **13** in single precision and **12** once promoted to double: .NET 8
  said 13 and Unity's Mono said 12, so one board was *two different boards* depending on who dealt it — and
  a phone runs IL2CPP, a third code generator again.
- **Nor may a float decide a *threshold*, and that one shipped.** `1.20f` is 1.20000004768…, so
  `Mathf.CeilToInt(45 * 1.20f)` is **55** where `par x 1.20` is exactly 54. Every runtime is wrong the
  *same* way, so no cross-runtime diff could find it — it disagrees with arithmetic rather than with
  another runtime. Four levels granted a turn more for three stars than the design says, for as long as the
  three lines existed. Factors are held as hundredths and thresholds derived with `(par * n + 99) / 100`.
- **`'' in 'RGB'` is `True` in Python, so an empty cell reads as a piece.** Every offline mirror here tests
  a cell against a string of letters, and every one is a substring test rather than a membership test the
  moment the cell can be empty. Par did not move; `ways`, `nodes` and the careless reading did, which is
  exactly the half nobody would have looked at.
- **Whenever two tenths multiply, keep the hundredths and divide at the end** (37bh) — ten per cent of 8 is
  8.8, which truncates back to 8, so a star a player paid for bought nothing.
- **A NumPy scalar is *strong* under NEP 50 where a Python float is weak**, so one `np.floor` of a Python
  float promotes a whole float32 pipeline to float64. The symptom was a failure to allocate 1.6 MiB with
  8 GB free; the tell is a traceback naming a `float64` array of a shape you only ever built in float32.

**Tests and probes**

- **A probe that writes off a whole class of failure as "expected here" cannot see a real one inside
  that class — and it will report success.** A view built in edit mode counted 94 images with a null
  sprite, correctly and fatally written off as "the asset scope is not loaded in a probe": five were
  a real bug, and a player met them as a **white rectangle** two cells wide over the board.
- **A vector file that only the Editor can read is not a guard on the rule it pins.** Every
  `*VectorTests` loads its JSON through `JsonUtility`, a native call, so the offline runner reports
  the whole fixture as "needs the Editor" — the one gate nobody runs on the way past. A mode's rule
  drifted from its mirror with **every offline gate green**; what noticed was the build gate
  refusing to prove par at all, twenty minutes into an Android build.
- **A `[UnityTest]` that counts frames while the code under test counts milliseconds passes only at
  a frame rate nobody promises.** Under `-batchmode -nographics` frames are unthrottled, so a
  600-frame budget can elapse inside one 50ms poll — green on a cold machine, red on a warm one, and
  the failure sentence names the code under test rather than the runner. Yield frames but bound the
  wait on `Time.realtimeSinceStartup`.
- **Edit mode dispatches no `MonoBehaviour` messages**, so `DestroyImmediate` never runs `OnDestroy` and a
  case asserting something was *not* handed back passes for the wrong reason.
- **The ads plugin's Editor consent stub sets `Time.timeScale` to nought and leaves it there**, and nothing
  in this project writes `timeScale` at all. So **a board coroutine waits in real seconds**, because every
  tween it waits on runs on the unscaled clock. Set `Time.timeScale = 1f` through `execute_code` before
  driving a board in the Editor.

**Art and addressables**

- **A constant is invisible to `artnames.py`, so replacing a literal with one silently gives up the
  gate.** Routing one screen's sprites through a skin table took thirteen names out of its sight in
  one change; the count of names *built rather than written* went 48 → 65 → 74 as the whole UI moved
  onto one table. The fix is not to stop naming things — it is to close the chain elsewhere:
  `SkinsTests` walks `Skins` **by reflection** and holds every string constant on it to what the
  manifest loads.
- **Deleting art leaves its Addressables entry behind, and that fails the build rather than the
  game.** `GUIDToAssetPath` keeps answering with the old path for a while after a file has gone, so
  the audit **errors** on any registered entry whose asset has gone — every other gate looks
  *outward* from what the game requests, and all were green with twenty-five dead entries. **And a
  group the Editor has repaired is not a group on disk**: dropping an entry marks the group asset
  dirty and nothing more.
- **The importer hook does not address art copied in while the Editor is closed or mid-reload**, which is
  every run of the art tools. Unaddressed sprites load as nothing. `▸ Addressables ▸ Sync All Assets` is
  the repair; the audit stops it shipping.
- **A sprite set is loaded by the *label* its frames share, not by its folder** — reels addressed and
  grouped but unlabelled are unloadable, and the audit was blind to it because everything it checked walked
  the manifest (37at).
- **A preprocessor fires on first import only**, so art that landed before an import rule changed
  keeps what it was given, silently — hence `▸ Reapply Art Import Rules`. **It must batch**:
  `SaveAndReimport` per texture is one round trip each, and 335 back to back crashed both import
  workers and wedged the Editor in a domain reload it could not finish.
- **The sprite atlas file extension selects the importer.** A `.spriteatlas` written in the V2 format
  imports as editor data and produces no `SpriteAtlas` at all — every address resolves, every check passes,
  and the shop draws an empty grid. It must be `.spriteatlasv2`.
- **A licensed pack's preview sheets carry the vendor's own dummy lettering, and grading it makes it *less*
  obvious.** Reduced to luminance and blurred, two blocks of placeholder text came through as smudges that
  read as *something painted*. It imports, addresses, audits and validates, because the content gates never
  open a PNG. **Look at a source at the size the game draws it**, and prefer a pack's `layers/` art to its
  `_preview` sheets.
- **A VFX pack's `Textures/` folder is two different things with one naming scheme.** Half is what a
  particle *draws*; half is what its shaders *sample* (noise fields, gradient ramps, dissolve masks). Both
  are white-on-black squares and a UI `Image` draws either happily — one mode's first cut took a colour
  ramp for its flare and a streak-noise field for its shockwave. **Before naming a texture after the effect
  you want, render it.**
- **A flood keyer is the wrong tool for an object whose *edge is a glow*, and it fails by keeping almost
  nothing** — on one sheet a bomb kept 852,000 pixels and the orb beside it kept **29,000**, a difference
  so large it reads as a missing file rather than a bad cut. **A keyer is a question about an edge, not
  about a colour.**

**Platform, store and backend**

- **`PlayerPrefs.Save()` serialises the whole store synchronously, and a preference written on *arrival* is
  written far more often than it changes.** But the flush cannot simply be dropped: Unity persists
  `PlayerPrefs` during `OnApplicationQuit`, which on a phone is the ending that rarely happens — an app is
  backgrounded and later killed — so a preference relying on a clean quit fails to stick for most people,
  **looking like a feature that never worked.** Compare first, flush when it really changed, and read the
  store rather than a remembered last-written value.
- **An Editor launched from the Hub gets a minimal `PATH`, and one failed post-processor abandons
  the rest.** Homebrew on Apple Silicon installs to `/opt/homebrew/bin` while the iOS resolver
  searches the process `PATH` plus the *Intel* `/usr/local/bin`, so it cannot find `pod` — and
  **Unity abandons every remaining callback when one throws**, so it also took down the privacy
  plist writer, the only thing linking the tracking framework.
- **Two Google ads SDKs cannot share an APK, and a mediation adapter can drag in the second one.**
  The legacy `play-services-ads` and `ads-mobile-sdk` both define `com.google.android.gms.ads.*`, so
  Gradle stops at `checkDebugDuplicateClasses`. This project holds the legacy one permanently (it is
  how UMP arrives), so the *adapter* must bend: **5.18.0.0** is the last on the legacy SDK.
- **Sign in with Apple on iOS cannot use the generic IDP path, and the refusal kills the process.**
  `FirebaseAuth` calls `fatalError` the moment `apple.com` reaches `FederatedOAuthProvider`; a Swift
  `fatalError` is not an exception, so no managed `catch` runs and the app dies on the tap. It ships
  looking correct because Android — where the generic path *is* allowed — works.
- **A Functions secret is pinned at deploy time, so a correct key can produce a 401.** Functions v2 records
  the secret *version* in the function's config, so setting one changes nothing until every function naming
  it is redeployed, while `secrets:access` reads *latest*. **And a 401 from the production App Store
  endpoint followed by success on the sandbox one is the *normal* path** for a sandbox purchase; the
  failure is both endpoints refusing.
- **A newly created 2nd-gen callable has no public invoker binding and answers every call with 401.**
  `firebase deploy` neither does this nor warns about it, and from a client it is indistinguishable from
  being signed out. Existing functions keep the binding they were created with, so only the new one fails:

      gcloud run services add-iam-policy-binding <lowercased-name> \
        --region=europe-west1 --member=allUsers --role=roles/run.invoker

- **Never `firebase deploy --only functions` for the whole codebase.** It failed all fourteen updates with
  a transport error while still *creating* the new function, so the state read as "nothing deployed" and
  was really "one of fourteen". Batches of three or four succeed first time.
- **The Firebase Unity SDK's `Firebase.Functions` ships as source with its own asmdef**, so the Cloud
  asmdef must reference it explicitly (App, Auth and Firestore are plugin DLLs and auto-reference), and
  that source needs `Google.MiniJson.dll` from the **app** package. All Firebase packages share one
  version.
- **Google's UMP plugin must come from OpenUPM as a package, never as the `.unitypackage`.** The
  `.unitypackage` unpacks as loose files under `Assets/`, so it is not a package, so it carries no version,
  so `versionDefines` never fires, the consent gateway compiles to nothing and **nobody is ever asked
  anything** — a consent failure that is completely silent.

## Current state

*What is true now*, not how it got here. The reasoning is in **Invariants**; the traps in **Hard-won
facts**. **Read the manifest, never this table, when a number reaches a customer** — a store draft once
claimed five modes and a hundred levels "across eleven chapters" when the truth was four and ten.

### Built and verified

- **Content pipeline** — levels as data, stable `LevelId`s, manifest-built `CatalogIndex`, lazy chapter
  bodies, `Content ▸ Sync Manifest`, build gate.
- **Save** — versioned atomic file with checksum, backup rotation, corrupt-file recovery, tested
  migrations, monotonic merge. **Save schema v26.** Content schema: manifest and chapter bodies **v2**,
  grove body **v3**.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google linking,
  per-account local archive for switching, debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water floors only.
  Hearts and hints are produced/spent ledgers. Chapters open on stars (21); a mode's opening levels are
  free to fail (24).
- **Retention** — daily chests, streak, golden levels, event calendar, percentile standings, per-level
  records.
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads, refund sweeps,
  server-adjudicated grants, a gem-priced continue (23) and a bonus wheel (25), neither costing the save
  file a field.
- **The grove** — a village on a 28x28 isometric tile floor (784 tiles), 86 pieces rendered from one
  CC0 pack (16m): a four-rung home ladder ending in a castle, houses, fourteen civic buildings,
  walls, gates, trees and props. A piece stands on an authored footprint (1x1 to 4x4; the hall and
  every dwelling the floor's own 4x4) and can be **turned** — four facings, four renders, one hit
  mask each.
- **Boards** — public grove cards, published rank distribution, unique keeper names with server-side
  filtering and reporting. A card is rebuilt about fifteen seconds after its owner changes the grove while
  online, or on their next launch; the cost grows with decorating, never with playing (19j).
- **One live mode, three hidden** (38). The game a player opens today is **Thornwatch**:
  `s01_thornwatch` and `s03_broodmarch` (ten rungs each) on the ordinary ladder, and
  `s02_endlesswatch` on an **Infinite** track beside it (43). The map draws no *mode* switcher,
  because there is one mode; it draws the **track** switcher, because there are two ladders.
- **Utilities** — an account-wide action bar (39), dropped by chests and bought with gems on a shelf of
  their own, charged against the graded count so one can never buy a star.
- **The turret loadout** — twenty turrets bought per colour up one gated ladder (42, 42c), upgraded to five
  stars (37bg), previewed firing before purchase (42a, 42d), carried in from a readout on the map (42b).
- **The front of the game** — one bought interface kit (44), cut by `Tools/make_hud_kit_art.py`.
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).

### Content shipped

| Chapter | Mode | Levels | Par range | Subject |
|---|---|---|---|---|
| `c01_shallows` … `c04_nightbriar` | glade *(hidden)* | 10 each | 10–70 turns | the verb, then colour and blending; the crossing; colour as the subject; the briar |
| `f01_lightfall`, `f02_glasswater`, `f03_whorlwater` | fall *(hidden)* | 10 each | 2–6 drops | the cook and the chain; the lens; the whorl |
| `p01_prismvale` | prism *(hidden)* | 2 | 3–4 swaps | drag to swap; a lantern feeds its own colour and a vein can be broken |
| `s01_thornwatch` | siege | 10 | 14–57 matches | the verb, the cog, the fourth ward, a warlord on rung 5 and an overlord on rung 10 |
| `s03_broodmarch` | siege | 10 | 38–59 matches | the same board and not one new rule: a second cast and a hill that no longer forgives rank one; a blightcaller on rung 5 and a warbringer on rung 10 |
| `s02_endlesswatch` | siege *(infinite)* | 1 | 3★ at wave 20 | waves that never stop, graded on how far it got — **both star waves are guesses until somebody plays it** |

**No level authors a difficulty number except the first glade in the game, and no chapter authors a clock**
(22). Par is derived; both star lines and the losing line are multiples of it. **Par is never monotonic
within a chapter** — par is length, not difficulty, and ten rising numbers read as a treadmill. Every
siege authors `budgetFactor: -1`, because its fail state is the ward line (37b).

Chapter art is generated and **shared by ordinal** (7c): the chapter tools regenerate the shipped JSON and
self-check against it, and four maps and forty skies serve every chapter of every mode. See `CRAFT.md`.

### The board's vocabulary *(the glade, hidden)*

**The wheel is paint, not light** — the middle channel is drawn yellow, so the blends fall out of the wheel
a five-year-old knows, while `Energy` still mixes by `|` over three bits. One verb — turn a conduit, light
a critter — with modifiers and no second solver: `~` brittle stone, `!` rooted (authored at `/0`, 5c), `&A`
taproot (turns as one, charged once in par), `=NS+EW` crossing (straight is inert, twisted is worth one
tap), `%NS+EW` briar (four arms drawn, two conducting, and the order of the pairs matters). A **pocket** is
not a tile: it is a heart and a critter behind a ford standing on a *cycle* of the live network (5f).

### The numbers

Free play collects about **593 credits and 6 gems a day**; both content gates derive and print this, so
never hard-code it. Everything below except the shop ladder is **content** and retunable without an app
update. **Re-seed after any change to it.**

- **Companions** — 31, one free, 30 priced 800 → 30,000 (~270,500). Unlock is keeper level **and**
  purchase.
- **Grove catalog** — 476,520 credits **and 6,200 gems** complete: 195,020 decor and homes, 11,000 credits
  + 6,200 gems of land (9 regions, a free 12x12 starter), 270,500 residents; 85 priced pieces, most in
  bundles of ten. Home ladder **4 rungs**, first free, 45,500 credits to the top, each paid rung gated on a
  keeper level: 2,500 at **10**, 13,000 at **20**, 30,000 at **40** (16s). **All three are above what
  today's content can reach, which is about keeper 9.**
- **Land** — one ladder, cheapest first: 2,500 → 3,500 → 5,000 in credits, then 600 → 900 → 1,200 → 1,500 →
  2,000 in **gems**. Only the credit half counts toward a grove's score (16j).
- **Grove star ladder** — 10K / 20K / 50K / 100K / 200K.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted). A loss costs one; two kinds of run cost
  nothing (24). A **heart container** raises the cap to 10, 20 or 50 permanently (18d).
- **Hints** — pool of 3 account-wide, one back every 8h, ceiling equals the cap. A hint charges no moves
  and is spent in the **glade** and nowhere else; Thornwatch's idle nudge is *not* this (37ao).
- **Turrets** — 20, one free, **9** priced 1,200 → 9,000 **credits** behind keeper levels 2 → 14,
  and 10 priced 600 → 2,000 **gems** behind keeper levels 15 → 24. Ten abilities, two rungs each
  (chain three), **ordered by how much of the hill an ability reaches** (37ax) with two named
  exceptions (37ay). **Bought per colour** up one sealed ladder, so a full line is 76 purchases
  (42c); the player stands four. Upgraded to **five stars** (37bg).
- **Utilities** — four, held up to **100** each, account-wide, each with a **cooldown** (10s / 15s /
  20s / 30s) burned on the run's own clock (39j), and **none carries a keeper gate**. Firepot 12
  gems (440 damage in one box of the hill, charged 2 matches), mending 8 (6 ward health, charged
  nothing), surge 10 (18 fuel, charged 2 matches), stormcall 40 (700 to every raider, charged by the
  same arithmetic). Three of the four are a weighted option in one daily chest.
- **Streak** — a 7-night lap: 500 credits, 1 heart, 5 gems, 2 hearts, a 12h boost, 3 hearts, 10 gems.
- **Ads** — four placements, all opt-in, no interstitials: 2 hearts, **300** credits, win-bonus
  credits, 1 hint.
- **Bonus wheel** — eight equal slices at 100/200/150/300/100/250/150/500 percent of the authored 200, each
  a 1-in-8 chance; mean 218.75%, so a view pays about **438** and a capped day about **2,628** against
  2,400 under the old flat offer at a cap of twelve (25).
- **Shop** — 17 products, **$298.83** in total. Gems 100 → 8,500 for $0.99 → $49.99; coins 2,500 → 75,000
  for $1.99 → $39.99; three bundles $2.99 → $29.99; the Bloom Pass $4.99; 5-heart refill 50 gems; a day of
  fast hearts 30 gems. **Heart containers** at $19.99 / $29.99 / $39.99 raise the refill cap to 10 / 20 /
  50 — the only real-money products granting something other than currency (18d).
- **Stars** — the graded count and nothing else (22). Gold `par x 1.20`, silver `par x 1.40`, the run ends
  at `par x 1.60`; held against **par**, never the budget, and moving one moves all three.
- **Chapter gate** — the next chapter opens at **2 stars a level** of the one behind it (20 of 30 today),
  per mode, and the first chapter of every mode is always open.
- **Heart rescue** — **20 gems** for **+2 hearts** on the defeat panel (23a), at the same gems-per-heart as
  the shop's smallest pack, which both gates check against — never the bulk pack, which is a volume
  discount every honest tuning is dearer than.
- **Continue** — **20 gems**, flat and repeatable, for the whole ward line back at full health on a siege,
  or the authored allowance in a hidden mode's own unit (23). Deliberately **not seeded**, so a retune is a
  build rather than a config push.
- **Account prompts** — 2 chapter asks, 3 purchase asks, one shared 48h quiet period.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`. **Fourteen
functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`, `adReward`, `appleNotification`,
`sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`, `withdrawGrove`, `publishGroveRanks`,
`claimName`, `reportKeeperName`, `deleteAccount`. `firebase/README.md` is the guide;
`firebase/e2e/smoke-test.mjs` is **91/91 live** and `firebase/e2e/delete-account.mjs` **14/14** — the
second erases the throwaway accounts it makes, so it is the only suite here that leaves less behind than it
creates. Client half is `Assets/Game/Scripts/Cloud/`, Firebase Unity SDK 13.15.0 as vendored UPM tarballs
under `GooglePackages/` (gitignored — run `pwsh GooglePackages/fetch.ps1` on a fresh clone).

**Two rules about the live suite.** It signs in as a **new anonymous account every run**, so anything
derived from the account id varies — and that is wider than "a figure": it broke a fourth time by
hard-coding *level ids*, so the day their chapter was hidden a green suite went red for a content change.
**Anything the published catalog decides has to be read off the published catalog, ids included.** And it
is **sensitive to cold starts**: re-run before believing a failure in the first minute after a deploy.

### Owed

**Money paths that have never executed.** A real receipt reaching `redeemPurchase` and a real impression
reaching `adReward`. Both are fully built and deployed and **neither has ever run once**, which is a
different state from unconfigured and the one that reads as done. Ads *load* on device; no view has ever
paid. Do both the day closed testing opens.

**Store and platform.**
- Delete an Apple-linked account on a device and check it leaves **Settings ▸ Sign in with Apple**. What no
  test here can reach is Apple's own answer, because every account the live suite makes is anonymous and so
  has no `authorizationCode` — the token exchange and revoke have never executed.
- Register the `appleNotification` URL for **both** production and sandbox.
- AdMob **instances** under each LevelPlay ad unit. Blocked on a public listing rather than effort: AdMob
  rejects an app-store URL that does not resolve, so `No fill` cannot be read as a wiring fault until the
  store page is live. Delete the retired `run_continue` unit while there.
- Fill in `app-ads.txt` and host it on the domain in both listings; turn on in-app bidding. The AdMob line
  is verified; the ironSource and Unity Ads lines are commented placeholders in **both** repos and must
  move together.
- Watch the **EU consent form** actually appear — the gateway has only ever returned `NotRequired` from
  outside the EEA, so the branch that shows a form has never run, and a consent failure here is silent.
- Delete the ~210 synthetic saves and name reservations the live suite leaves behind.

**Waiting on the Editor.** `Validate Content`, `Validate Art` and the EditMode suite have not been run
since the last art and turret work. Run `▸ Addressables ▸ Sync All Assets` **first** and **save**
afterwards: art written while the Editor was closed is unaddressed (a white rectangle, 7b) and deleted art
leaves entries that fail `BuildPlayer` rather than the game. Re-bake the projectiles for any ward colour
whose `Pal` entry has moved (37ak), then sync and verify.

**Decisions the owner owes.**
- **Are the home ladder's gates reachable?** Keeper 10 / 20 / 40 against content that pays for about
  keeper 9 — so the ladder is real, correct and, for every player alive today, three padlocks (42c
  records the same for the ward shelf). It resolves by shipping chapters or by moving three numbers
  in `homestead.json`.
- **Is the back half of `s01_thornwatch` too hard?** Swept over nine rhythms, its two worst rungs are held
  by an ordinary player at 5 and 6 of 9 (37aq) — up from 3 and 4 now that the whole line answers a boss
  (37bq), and the chapter holds 80 of 90. Broodmarch's finale is the wall that is left: **1 of 9 on the
  starter line and 4 of 9 one rung up the shelf**, and a boss is not the lever there (it was 1 of 9 before
  the bosses grew too). Lighten the last waves, soften the boss grinding the line, or decide a **mending**
  is the intended answer and the simulation is wrong to model a player who never spends one. **What must
  not happen is tuning until the old single-sample gate goes green again.**
- **Is a baked 3D cast worth keeping?** The Infinite lane sends it and the authored chapters send insects,
  one tap apart (37at). Judge whether it *belongs*, whether a raider's colour still reads, and whether it
  is **better** rather than merely different.
- **Should a raider be framed to its body?** The pack feathers its baked shadow out to alpha 1, so every
  insect is framed around a halo and drawn smaller than it could be. Closing it changes how every raider is
  drawn — a change to ask for, not to slip in.

**Play it.**
- **Thornwatch's colour lock** (37bl), the largest change this mode has had. Does the eye go up? Is the
  breather long enough to act in and short enough not to read as a wait? Is the forecast read? Does the
  demand light turn a parse into a glance? Is the overcharge found — **the figure worth an event is how
  long a full tube stands before it is tapped** — and do the cogs pull the eye up? The three-colour opening
  rungs have never been played.
- **The duel** (37bq): with every turret firing at a boss, does the finale read as the whole line
  fighting one thing — and is the double still *findable*, given that nothing says it out loud but the
  gold numbers and the demand light? **The figure worth an event is how much of a boss's health the
  ward of its own colour took**, because that is the decision, and a run where it is near a quarter is a
  player who never noticed there was one.
- **Thornwatch generally** (37i, 37z): does fuelling a colour read as the verb; is par a line a good run can
  get under; does a cog read as an upgrade; do the four bosses read as four fights, each taking a different
  thing? **The figure worth an event is how often a run ends with the line down rather than with the hill
  cleared**, because those are two different failures.
- **Broodmarch** (37bd, 37be): does a boss read as a boss now it is off the raider roster; does the brood
  read as a different place; is two bosses a chapter right; and is "hard on the free turret, fine one rung
  up" what it *feels* like — the measurement models a player who never spends a utility, buys a continue or
  learns a board.
- **The bomber** (40i): is the bomb found, is *when* to spend it a decision, does it read as a reward?
  **The figure worth an event is how long a bomb stands before it is tapped.**
- **The turret shelf** (42c, 42d, 37bb): does a player understand they bought a turret *for red*? Is "after
  the rung below" a next step or a wall? Does the damage/health spread read, and does anybody regret a
  fragile line?
- **The action bar** (39): does it read as *yours* rather than as the level's? Does the charge land as fair,
  given that it is invisible? And is a mending the one that gets used — it costs the grade nothing, so if
  the firepot wins anyway the price of a star is too cheap.
- **The restyled UI on a device** (44h): do the navy plates read against a bright world in sunlight, and
  does the backdrop stay a *place*? **A render is much weaker at "is this palette any good" than at "is
  this widget where I think it is".**
- **Prismvale and Whorlwater, if they come back** (36i) — and the whorl's *reason* has to be watched before
  its difficulty: a player who reads it as "a mote that pops itself" has not met it.

**Measure it, once there is live data.** The three star lines (1.20 / 1.40 / 1.60), reasoned about and
never played against, which have the shortest path to an uninstall. The chapter gate. The continue against
the heart rescue — read the two funnels *apart*, because a defeat has already happened where a refused
restart is a board still standing. The restart gate's floor. And the bonus wheel against its cap.

**Checks this file knows are missing.**
- **Which of a mode's kinds does the shipped chapter never send?** Two raider kinds were built, validated,
  arted and never authored into a wave (40a). One loop over the enum against the authored waves.
- **The offline mirror's wave reader treats an unknown character as a colour letter** rather than refusing
  it, so a new token read as two raiders and the Python gates agreed with themselves while disagreeing with
  the shipping C# about par.
- `bestMillis` can come off the wire once no shipped client writes one (22).

### Three confirmations, and only three

`ForfeitOverlay` (a committed run being abandoned), `ReportNameOverlay` (an act against another person that
cannot be retracted) and `DeleteAccountOverlay` (27), which earns one more completely than either: there is
no store to re-deliver an account, no archive to restore it from and no support path that can bring it
back. Its second tap is armed only when there is a grove to lose — **arming a button over an empty grove is
what teaches a player to tap through it on a full one**. `ContinueOverlay` is not a fourth: it is an offer
rather than a confirmation, and its default answer is the free one. Everything else either costs nothing to
undo or is confirmed by the store's own payment sheet — and a panel of ours in front of that sheet is a tap
for a question about to be asked properly.

### Not done, deliberately

- **Play Games Services** — better Android sign-in and the natural home for leaderboards, but Android-only,
  so it cannot be the identity.
- **A visual level editor** — tooling, and the thing most likely to matter next for cadence.
- **Remote content delivery** is built and switched off. Setting `ContentConfig.RemoteBaseUrl` turns
  the heart gate, the chapter gate, the chest odds and the ad payouts into minutes-not-days levers;
  it is the highest-value unshipped setting in the build. One known gap first: `Sync Manifest` bumps
  a chapter's `version` only when its **level list** changes, so a content-only rewrite would never
  reach a client that had already cached the body.
- **A "keepers near you" board** — it needs the exact global ordering 19c refuses to keep, and the
  percentile already answers the question it would ask.

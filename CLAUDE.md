# Glimmer Groove

A globally distributed mobile puzzle game (Unity 6000.5.4f1, Android + iOS). The product name is
**Gemfire**; the repo folder, this file and the bundle id `com.tekoworld.glimmergroove` still say Glimmer
Grove, and the bundle id can never move.

## The standard this project is held to

> This app will be distributed globally. Everything we build should be scalable, sustainable, and
> maintainable from day one. No demo, only production builds. Implement the most proper solutions, like
> AAA companies. — the project owner

- **No placeholder architecture.** If a seam is worth having later, build it now. A genuine placeholder
  says so in the code and is replaceable by touching one file.
- **Cost curves decide priority.** Prefer the change that is cheap today and expensive later.
- **Prove it, do not assert it.** Back every claim with a compile, a test, or a validator run.
- **Push back with reasons.** Say the specific failure once, plainly, then do what the owner decides.
- **Finish the whole job.** If something can only be done in the Editor, automate everything around it and
  hand over exact steps.

> **About this file.** Each invariant is a rule and, where it is not obvious, the failure that bought it.
> **Numbers are permanent** — ~1,500 code comments cite them (`invariant 7b`, `invariant 5d`) — so an entry
> may be compressed but **never renumbered**. Spent ids are in the table below.
>
> **Keep it small.** A new invariant is one or two sentences. Deploy records, dated "done" notes, retired
> loc keys and drawing detail do **not** belong here: the craft lives in `Assets/Game/CRAFT.md` (art tools,
> baking, grading, framing, UI house rules) and authoring in `Assets/Game/CONTENT.md`. Read `CRAFT.md`
> before touching a screen, an animation or an art tool; read `CONTENT.md` before touching content, assets
> or localisation.

## Invariants — do not break these

### Content and ids

1. **A `LevelId` is permanent.** Save data, analytics and remote config key on it. Never rename or reuse a
   shipped id; never key anything on a level's position.
2. **Never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`** — a frozen record of the pre-1.0 build;
   changing it moves real players' stars onto the wrong levels.
3. **`Domain` must never reference `Presentation`.** Asmdefs enforce it; raise an event instead.
4. **Content is data, not code.** Adding a chapter must never require a code change.
4a. **The manifest owns membership and order; a chapter body owns content.** The boot path reads
   `manifest.json` and nothing else; bodies load on entering a chapter and are evicted on leaving. A boot
   path reading a body costs per chapter at every launch and is invisible in the Editor, because only
   Android routes StreamingAssets through `UnityWebRequest`.
4b. **Every chapter file must be in the manifest, and only the Editor may check that** — an unlisted file
   is never opened, so everything validates green and the drop ships without it. `ChapterFiles` is the one
   place allowed to list the folder.
4c. **Anything that rewrites `manifest.json` must prove it lost nothing**, by reading its own output back
   through the reader the game uses. A full rewrite silently drops fields the writer does not know about.
   Never relax it into a warning.
5. **Omit `par` when authoring.** It is derived from the board; a typed one can drift.
5a. **A level's loc keys are derived from its id and cannot be overridden**, which lets anything holding a
   `LevelId` name a level without reading a chapter body.
5b. **"Is this tile solved" is `Puzzle.Alike`, and it exists exactly once.** Five hand-written copies were
   each correct until a crossing appeared, which wears all four arms at every angle.
5c. **A rooted tile is authored at `/0`, and that rule guards every other rule** — every proof runs with
   rotations zeroed. The check asks `Puzzle.Alike`, never `rot == 0`.
5d. **A mechanic that rejects no arrangement is decoration, and that is countable.** Enumerate the
   arrangements satisfying the hard constraints and ask which win; a count of **one** means the hard
   constraints alone decide the level. **The most cited rule in the file** — asked of a mechanic, a threat,
   a fail state, a readout, a timing and a purchase, the answer is always a count rather than an argument.
5e. **`Puzzle.Matters` has a second clause**: a tile the *player* has lit counts, whatever the solution
   wanted.
5f. **A wrong turn must be visible somewhere.** A retired token or block name is **refused by name at
   parse**, because `JsonUtility` drops an unknown field without a word; a retired *lesson* id is spent
   where a *level* id is **kept with its string changed**; and a loc key names a sentence rather than a
   thing a save can hold, so a retired one may be re-minted.
5g. **A board is graded on its solution and met as it is dealt, and only the first was measured** — 34 of
   40 levels "started half done". A properly dealt board's par is ~1.2–1.35x its turnable tile count.

### Text, lessons and assets

6. **All player-facing text is a loc key.** The build gate fails on any missing; never build keys by
   concatenation.
6a. **A lesson is a permanent id, and a screen that teaches several owns none of the sequencing** —
   `ScreenLessons` owns order and chaining, or screens disagree about how many modals are up.
6b. **A lesson whose sentence is *tap this* rings the thing to tap, never its source.** A mechanic the
   board does not declare falls back to a tip with no ring, which looks like a tip with nothing to ring.
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or `Addressables`
   directly, and never hand-list paths — derive them from `AssetManifest`.
7a. **Asset registration is an importer hook, never a menu item**, plus a build-gate audit: making an error
   unlikely is not proving it did not happen.
7b. **Transient art belongs to a named `AssetLibrary` scope, never to the global set.** An address already
   global stays global; one owned by another scope is never re-claimed. Loading is asynchronous, so a
   screen must repaint when a scope arrives — **an `Image` with no sprite is a white rectangle, not a
   blank**, which is where nearly every asset fault in this file ends.
7c. **A chapter's art is arithmetic on its ordinal, never a choice** — the map from the chapter's ordinal
   in its mode, the backdrop from the level's place in its chapter, with a generator writing the answers
   into the body so the content still *says* what it draws.
7d. **A texture's size cap and compression grade are both folder rules, applied before the importer
   hook's early return** — everything after `textureType == Sprite` is skipped for most art, so
   compression set down there was set on almost nothing and 382 files kept `Uncompressed`: 6% of the
   library holding **49%** of its texture memory. Pinning the *format family* cannot catch it, because a
   texture asking not to be compressed is not asking for a format. Graded up to 4x4 only where the cap is
   small, and **measured, never argued** (`compare_texture_formats.py`).
8. **The map shows one chapter at a time**, bounding node count and texture memory by chapter size rather
   than catalog size.
8a. **Map geometry lives in `ChapterMap` (Domain), not beside the screen**, so collisions are facts a build
   gate can check — and **the footprint it checks has to be what collides**.
8b. **Which chapter is shown is per mode, device-local and only ever a hint** — it moves both ways so it
   could not be joined (11b), and it answers null the moment the id is not a chapter of that mode.
8c. *(Spent by 8e.)* **The disc, the halo, the seal and the shadow are read from one name** — hand-written
   offsets are how the shadow once came to stay put while the disc moved.
8d. **A prop scattered on the map has to be drawn the way the map is drawn**, and there is no gate: put it
   on a strip and look. A withdrawn picture goes with its `.meta`, its manifest entry and its Addressables
   row, because a preloaded picture nothing draws is resident memory for the life of the game.
8e. **A node stands on the ground the painting draws, and where that is has to be read off the picture.** A
   floating perch brought its own ground, so a serpentine could ignore the painting and bare discs landed in
   a river, on a rooftop and off a cliff. Seats are derived by `Tools/make_map_seats.py`, which asks whether
   the disc's own footing is on the road (at a radius far smaller than the disc) and whether there is land
   all round it. Three findings: a colour list is per painting and must be **subtracted** as well as added;
   open water is a **gradient** where a road is flat colour, so the void needs a wider tolerance; and **a
   seat must be rounded before it is judged**. The walk backtracks. It cost the perch and the trail.
8f. **A perch is a fact about a *place*, not about a mode — and one map still needs one.** `map1` draws
   paths on its islands *and* a current between them, so a seat carries `afloat`, generated with the
   position and never authored, false on every road map. A colour tolerance is per painting; a wide "is
   there land all round" test must not be asked of a seat on water; and only a map that paints a current may
   moor anything.
8g. **The search says where the road is; an eye says which part of it a node looks right on, and the second
   is authored.** `NUDGE` shifts a seat along ground the search already found, may not put one where the
   search refused, and is re-checked against `ChapterMap`. A marker moves across the map only, never up or
   down. **Size a nudge against the rule, not the instruction.**
9. **XP and earned credits are derived, never accumulated.** An accumulator cannot be merged across
   devices, retuned for existing players, or recovered when lost.
9a. **The reward rule exists twice and must stay identical** — C# and TypeScript, both running the shared
   vectors as a test. Change one, change the other, add a vector, re-seed.
9b. **`progression.json` versions independently of the catalog.**
9c. **So does the chest generator** — a pure function of (account, day, index), FNV-1a then xorshift32, all
   32-bit so JavaScript reproduces it exactly. Constants, shifts, stream numbers and modulo are contract.
10. **The client never raises `grantedBaseline`.** Currency given rather than earned is server-owned,
   enforced by Firestore rules. Receipt validation is idempotent on the store transaction id.
10a. **An award reaches the player as a claim, not as a balance**, with an id **derived from what earned
   it**, so two devices produce one entry and a resubmission confirms instead of paying. The server
   recomputes the amount. Never reach for `GrantLocally` — it is for the account seed alone.
10b. **Daily chests are earned, never bought**, which keeps them outside loot-box rules and is why the odds
   can be printed. No price, and **no second weighted pick**.
10c. **A chest cannot be opened before the account id exists**, because the roll is seeded from the uid.
10d. **A rewarded ad is granted by the network's callback, never claimed by the client.** The client-nonce
   alternative does not survive (LevelPlay 9 removed the API) and it fails *silently*.
11. **Cloud conflicts merge; they never prompt.** A "keep local or cloud?" dialog is data loss wearing a
   consent costume.
11a. **The ledger is a map keyed by level id, never an array**, so a duplicated record is unrepresentable
   and a sync can write one key alone.
11b. **Anything a merge touches must be monotonic, or it is not mergeable.** A stored *count* cannot be
   joined — store counters of things that happened and derive the count, so the merge is `max`. Check the
   field only ever rises, **and that its "absent" state is a value a real one cannot hold**, because
   `JsonUtility` writes a zero into every field an older file never had. The only count that may be stored
   is one that cannot fall (37bg).
11c. **A value merged by recency must carry its own date, and its default must never be stored.** The
   file's own stamp is set to *now* by the snapshot, so the local side wins every comparison; and a stored
   default makes "no opinion" indistinguishable from a choice.
12. **Adding a field to `SaveFileDto` interacts with the checksum.** Bump `SaveSchema.Version`, or every
   save on every device fails at once.
12a. **A field is not on the wire until it is in four places**: `SaveFileDto`, `SaveDelta`, the Firestore
   mapper *both ways*, and `hasOnly` in `firestore.rules`. `hasOnly` is an allow-list over the whole
   document, so a client writing an unlisted key **loses every save write**, and rules deploy before the
   client. A field riding inside an existing map costs no rules release, which is why the season's pass flag
   and the streak's fourth date are shaped that way.
12b. **Every list the rules bound is capped by the client first, and the gate holds each pair.**
   `size() <= N` refuses the *whole* write, so a list one entry over its bound is an account that
   never saves again and no screen says so — the lesson ledger keeps withdrawn modes' lessons for
   ever, crossed 64 on the owner's own account on 2026-09-16, and only an account switch said
   anything. A writer caps what it sends (`TipLedger.MaxIds`), the rules' bound is never the
   smaller, and `CloudWireTests` reads `firestore.rules` to hold the two together, offline.
13. **A reward is derivable, adjudicated, third-party, or not currency.** Currency the client hands out
   must reach the server as something it can *recompute*, something a *third party* reports, or something it
   can *bound* so tightly that forging it buys nothing.
13a. **A claim must never be refused for a reason that will still be true tomorrow** — the client
   resubmits, so a permanent refusal is a loop for the life of the account. A missing config block leaves a
   claim *unconfirmed* rather than rejected.
14. **Derived rewards are free of save state, and that is why they are preferred** — no counter, no claim,
   no merge rule.
14a. **Being derived decides where a reward comes *from*, never when it arrives.** If keeping it derived
   means it can only arrive silently, add a floor — one monotonic integer per key, merged by `max`. What
   must not come back is a *stored amount*.
15. **An entitlement is stored; everything that pays is derived.** Owned companions are a set of permanent
   ids joined by union, because buying is irreversible.
15a. **A gate is permission to pay, and both halves are required.** `AvatarCatalog.ReachedBy` is named for
   its narrowness on purpose. The gate is tested **before** the price, so a player both too junior and too
   poor is told about the wall credits cannot climb; and `IsHeld` must never re-check the gate on something
   already bought, or a retune confiscates it. The same shape gates homes (16s) and turrets (42c).

### The grove

> **The Grovement is HELD as of 2026-09-15, and nothing below has been deleted.** This build does not
> *draw* it: the nav tab is out of `NavBar.Order`, the finest-groves board is out of `LeaderboardScreen`,
> the public profile's grove card and worth line are gone, the board-row chooser is bypassed, and
> `ReportSubjects.Held` carries the grovement subject. **Every screen, rule, id and the whole server half
> still stand.** Putting it back is a handful of table entries.

16. **A grove is built, and only four facts about it are stored**, split by *shape*: a purchase is an
   **entitlement** (union-joined id sets), an arrangement is an **instruction** (merged by recency with a
   stamp per slot), and the fourth is the hall's seat (16q). Deliberately absent: any count of tiles. A slot
   id is written into the save, so invariant 1 applies to it.
16a. **A resident is a companion, and the roster is written down once.**
16b. **The grove is a tile floor, and a tile is a slot** — hand-authored slots made the player's only
   decision which pre-placed dot got which sticker.
16c. **A shop shelf is one idea used three times** (tab, browse atlas, asset scope), so the division is
   expressed once. Browsing packs *copies*, because a sprite may belong to exactly one atlas.
16d. **Anything unbounded keeps only what you can see.** **`Show` is a new list and animates; `Refresh` is
   the same list redrawn and does not, and anything raised by an event is a `Refresh`.**
16e. **Land is a union-joined set of *regions* rather than tiles** — both are legal and only one stays
   small. Starter land has no price and is never written down; the hall stands on starter land.
16f. **The starter companion is shown, never stored** — writing that placement at first launch would stamp
   it with *now* on every fresh install. Clearing it is a real instruction that does get a row.
16g. **A grove's score is what it is worth, and worth is what is *held*** — derived, cloud-safe, monotonic.
   Counting placements would be won by standing one expensive piece everywhere; storing it would be the
   count 11b refuses. The reading is market value, not spend.
16h. **Priced decor is bought by the copy, and the count is representable only because it counts
   purchases** — copies ever bought only rise, so the join is a per-id `max`.
16i. **A piece occupies a footprint, the ground is a layer under everything, and a tap tests paint.**
16j. **Before pricing anything in a second currency, grep for the predicate that means "free" and read
   every caller.** `IsStarter` was `Cost <= 0`, so every gem-priced region read as free and half the world
   was handed over at launch. **The ladder is authored and never derived from price.**
16k. **A `Refresh` that restarts an animation is a `Show` wearing a `Refresh`'s name** — a *bind* restarts
   things of its own.
16l. **Two screens offering one catalog draw one card**, as a builder rather than a table of numbers.
16m. **The grove is a village rendered from models, and what that bought is that a piece can be *turned*.**
   `Turn` steps by one and answers `NoRoom` rather than skipping to the facing that would have fitted.
16n. **A union-joined set cannot be cleared by clearing it, so a reset is an epoch.**
16o. **Placing something is a draft.** `GroveDraft` (Domain) answers `Fits`, `Stand` and `Footprint` from
   one place: the lit tiles come from the plan the drop executes; moving is centred, clamped and **never
   relocates**; **turning is allowed even where it will not fit**, because a refusal the player can see is a
   control and one that is swallowed is a broken button; and nothing is written until Confirm.
16p. **Every dwelling matches the hall's footprint**, so the plot is reserved up front and a bigger home
   never evicts what stood beside the smaller one. A tile id is its absolute coordinate.
16q. **The hall's seat lives in the save**, as an instruction joined by recency with the default never
   written down. **Every runtime question about where the hall is goes through one accessor** — the old
   constant is still correct and has stopped being the answer, so a stale reader is a home drawn on one tile
   and hit-tested on another.
16r. **A model named after a building is not evidence that it is one** — two `building_*` models are
   hexagons, which cannot tile a square grid. A render answered it in one picture.
16s. **The home ladder is gated on keeper level as well as price**, because credits alone were a poor clock.
   A gated rung must also be priced, and the gate is asked before the price.
16t. **Removing a grove piece is four things**: the roster row goes, the id goes into
   `Tools/grove_retired.txt` **in the same change**, the importer rewrites the catalogue and drops its
   strings, and the art comes off disk.
16u. **One grey lamp cannot say outdoors** — the render takes a warm sun and a barely-cool shade and falls
   back to the scalar, which let the refactor be proved byte-identical first. Brightness cannot answer a
   hue, so the floor is **turned** toward grass as well as lifted. Every grade runs on the three colour
   channels and **never on alpha**.
16v. **A screen whose backdrop is daylight is darkened by nothing** — every element already earned its
   contrast. **And the sun is drawn where the models were lit from**, off the render rig.
16w. **Nothing stands on the hall's plot, and a content change that grows the plot is a migration.**
   `GroveOccupancy` refuses any stand touching a hall; `HomesteadLayout.Settle` writes the refused rows
   *empty* (a real instruction with a stamp), because a row merely skipped springs back when the hall moves.
16x. **A derived copy is written by whoever writes what it derives from, in the same call — and no reader
   may ever prefer the copy.** `homesteadOwned` had two writers and only one knew, so every synced save
   carried a full stock beside an empty array and the one reader that asked the mirror alone published every
   keeper standing in the free cottage. The pair is set by one call and the fixture is asserted over **the
   merge's own output**; **the repair is bumping the publish note**, because nothing a visitor sees changed.
17. **A save may only ever be pushed to the account it says it belongs to.** `AccountGate`, five lines, and
   the only rule here whose failure has no undo: aimed at the wrong account, a monotonic join takes the
   better half of two strangers' groves and writes it over one of them.
17a. **A switch is finished on the device before the network is asked for anything** — the swap is local
   and the server is folded in afterwards by an ordinary sync.
27. **Deleting an account removes data first and the account itself last**, which is the only thing making
   it safe to retry: **visibility first**, **the name next** (while the wallet holding the key is readable),
   **then the save**, recursively, **then Apple; the auth user last**. Every step is delete-if-exists.
   **Three things are deliberately kept**: globally-keyed receipts, reports this account filed about others,
   and a denied name's reservation retargeted to a tombstone uid. The client's half is server-first. A
   linked account re-authenticates first; a revocation failure never blocks a deletion.

### The store

18. **A real-money product grants currency, and nothing else.** Hearts and boosts are bought with **gems**
   and a gem debit is an ordinary spend — otherwise "did I already apply this transaction's hearts" would
   have to live in the save and be merged across devices.
18a. **A transaction is confirmed only after the grant lands, and never before.** The server verifies it,
   records it against a **global** receipt key, and grants — so everything that can go wrong is "still
   unfinished", and both stores re-deliver on every launch for ever. **That is why no per-purchase state
   exists in the save.** A refused receipt is never confirmed.
18b. **The shop is one authored list, and the server derives its half from it.**
18c. **A refund is money leaving, so something has to watch for it.** Apple pushes, Google is polled.
18d. **A real-money product grants currency, or an idempotent permanent entitlement — never a stored
   amount, and never both.** A capacity is the union of one permanent product id; the entitlement lives on
   the client and survives a reinstall, because the grant runs on *every* successful redemption.
18e. **A shelf's picture ladder is exactly as long as the shelf** — the rung mapping hides that the length
   is a decision, and two painted quantities shipped addressed, resident and drawn by nothing.
18f. **A purchase paid for and not yet honoured is said in the middle of the screen, and the saying of it
   has to be able to end.** Three endings, deliberately three mechanisms: the transaction stops being owed
   (read off the store queue, not an event, because the queue is emptied by *every* ending including the
   silent refusal); a patience timeout; and it is an ordinary modal. **Raised only for a checkout this
   process opened**, or an unhonourable receipt would raise it at every launch.
18g. **A shelf may carry an offer that costs no currency** — the coins and hearts shelves each stand a
   rewarded video first, because the sort is cheapest first and nothing is cheaper than nothing. The card is
   drawn live whatever the network is doing, so the refusals stay in the one place that says them honestly;
   what takes it off the shelf is the content table not carrying the placement. The panel stops offering the
   shop when the shop raised it, asked of the screen underneath rather than passed in. `ShopAdShelf` is in
   **Domain**, for 8a's reason.

### What a stranger can see

19. **Anything a stranger can see is a separate, server-written document.** Widening the save's read rule
   would publish everything else with it and freeze the save's *shape* into a public API.
19a. **A number that goes public stops being derived-and-trusted and becomes adjudicated.** The earned half
   is derived from records the server already validates; the bought half is clamped. The gate works *before*
   the clamp — a save naming something its keeper level has not reached is dropped, not cut down.
19b. **The public name is a second rule on top of the stored one, and the server's answer governs.**
   Bidirectional controls are why it is not a length check, and whitespace is tested before the forbidden
   set.
19c. **A standing is read off a published distribution; nothing maintains a global ordering.** Nine deciles
   and a hundred-row board, rebuilt daily, read as one document at O(1) at any player count.
19d. **A name is unique because a document id is unique, never because a query said so.**
19e. **The two folds are one rule, and the runtimes do not agree about Unicode** — Unity's Mono and Node's
   ICU disagree, and only the shared vectors can see it.
19f. **A published name comes from the reservation, never from the save.** The filter runs **again** at
   publish time, and the publisher claims whatever the save asks for when it differs.
19g. **A word list is the cheapest layer of name moderation and the least important; the fold stops
   bypasses and reporting catches the rest.** Deleting everything outside `a-z0-9` rather than folding let
   leetspeak past and squashed any non-Latin name to empty.
19h. **The list is a document and the takedown is a flag, because both have to move without a deploy.** A
   filter that fails open looks exactly like a filter with nothing to catch, so a published list materially
   smaller than the shipped one is refused.
19i. **A report is keyed on the pair of accounts** — the id *is* the idempotency, which is why the
   threshold counts distinct reporters rather than taps.
19j. **A card is asked for after the sync, never after the change, and the reply is proved.** Only
   `Settled` asks, and the card judged is built over the receipt's save, never the live ledgers.
19k. **A board is one published document ordered on one field the card already carries, and adding one is a
   row in a table.** The field must be one a card is ordered on *and* one the row prints; **a nought is
   absent rather than written**, because Firestore indexes a field only on documents carrying it; a board is
   about the *lane*, never a level; and **a retired board is worse than a missing one**, so
   `pruneRetiredBoards` deletes what `BOARD_IDS` does not name — while the deletion scrub (27) walks the
   *collection* rather than that list.
19l. **The endless board is the one public number this server cannot recompute**, so 13's fourth clause is
   all that is left: a ceiling keeps a forged figure in range, and credits and XP still derive from the star
   ledger, so a forged wave moves a position and never a balance. **The day the endless board pays anything,
   this stops being defensible.** The ceiling is mirrored client-side and the two move together.
19m. **A board of a hundred rows is a feature for a hundred people; what makes it a feature for everybody
   is the *distribution*, and both come out of one walk** — Firestore bills per document whatever `select`
   asks for. The two populations are deliberately different lengths, each with its own minimum, and **a
   nought is never a sample**.
19n. **A caption that can grow has to be measured against the plate it sits on, and only a render can do
   it** — `UIKit.Shrinkable` truncates silently. The tell is Best Fit settling at its floor.
19o. **A keeper is two things a stranger can see, so there are two judgements and two collections** — a
   name and an arrangement (walls tile, so somebody with enough of them writes whatever they like). One
   mechanism parameterised by a `ReportSubject`: idempotency, threshold and review floor are shared, the
   counts are not, and the daily quota bounds the reporter. **The wire spellings are permanent** (a subject
   names a collection); **a callable's name is not**, which is why a rename cost a deploy, an invoker
   binding and a delete rather than a migration.
19p. **What a public profile draws is what the score counted, and that is one walk** — not a second filter
   over the same save. The **keeper gate is asked** and ownership is not, and a seat the server cannot vouch
   for is **omitted rather than corrected**. A free turret is in nobody's `wardsOwned`, so the published
   roster carries `free` beside the gate. A stale seed publishes no line, never an unvouched one.
19q. **A row on a board leads to two places, so it opens a chooser**, holding no art of its own. **Every
   thing a profile says, it says about somebody else**: no prices, no padlocks, no taps, and **nothing
   unheld is drawn at all**.
19r. **A board is a tally taken once a day, and the screen cannot say so by drawing itself.** The cadence
   mirrors the cron, affordable only because nothing waits on it — and the panel prints what the job that
   wrote *this* board recorded, dropped when nought.

### Modes — in `Assets/Game/MODES.md`

**Invariants 20–26h, 28–36i and 37–43d live in `Assets/Game/MODES.md`**, which is what a mode is and what a
mode costs: the entry tests a new mode must pass, the grading and fail-state rules, the sixteen modes built
and withdrawn, the three hidden ones, and the whole of Thornwatch (the live mode) including its charms,
bosses, turret shelf, utilities and the Infinite lane. **Read it before touching a mode, a board, a level's
difficulty or the siege.** Its ids are permanent and cited by code exactly as the ones here are — the split
is where they are written down, not what they mean.

### The front of the game

44. **The whole UI is one bought interface kit, and it moves in one commit because the names are already
   roles.** `Skins` says `btn_green` and has meant "do the thing" since this UI was written, so the way to
   restyle every screen at once is to **re-cut what those names point at** rather than sweep call sites.
44a. **Upscaling a nine-sliced sprite makes its unstretchable ends bigger and its usable middle smaller** —
   scale a sliced piece for the size its corner should draw at, never for resolution.
44b. **A face lift is a property of the art, not of the caller**, and is **measured, not typed**.
44c. **A backdrop's crop is a decision for the tool, never an offset in the screen.**
44d. **Two screens made of the same furniture get one mirror. And a mirror has its own bugs** — three
   "faults" were the render reading an `anchoredPosition` as an edge when `UIKit.Box` always pivots at
   **centre**, and reading Unity's up-positive y as an image's down-positive one. A mirror may never tell a
   comfortable lie about the screen, and one still drawing a piece the screen does not is worse than none.
44e. **A `switch` whose `default` is a real answer hides the case nobody is looking at.**
44f. **A kit drawn for light screens cannot be cut as it ships into a game that writes light text** —
   changing the *text* would be the ninety-call-site sweep 44 exists to avoid, so what changes is the art.
44g. **Dimming is not how you recolour something warm** — multiply takes a colour toward black along its own
   hue, so amber arrives as brown, and the fault **scaled with area**.
44h. **A restyle that is only a re-cut can still ship a boring screen, and the two halves that fix it are
   the *plate* and the *ground*.**
44i. **A bought sprite's own drop-shadow is an outline on any plate that is not the colour the pack drew
   for.** Cut in the tool, once, as a **ramp rather than a threshold**, and run **before** trimming so the
   border re-measures against the corner that is really there. **The mirror is the other half.**
44j. **A balance readout is *watched*, never drawn.** A pill built out of a profile field is a photograph
   from that moment on; what makes it a readout is a subscription to **both** the progression event and the
   wallet's hearts event, because hearts move on a refill timer. Seven screens wrote it by hand and four
   were wrong in four different ways, and **nothing here could see any of it** — a stale number is a correct
   number that has stopped being true, so it compiles, the mirror draws it, and a fixture passes because the
   wallet does not move during a test. The cue is **attached rather than written** (`WalletWatch.Attach`),
   repaints through `ResourceSlots.Repaint` and never onto a label, and `compile.py` refuses a file that
   registers a readout and attaches no watch. **`ResourceSlots.Register` is the only way one is built.**

### Tasks and the chest ladder

45. **A task is a goal the game can count, a number and a chest — and every reward on the page is a chest.**
   A goal is code; a task is a row of the `tasks` block, so a slate or a retune is a content push and a new
   verb is a build. A task names a **tier**, never a prize, so one disclosure per tier is the odds for every
   task that pays it, and the ladder is gated to **rise**.
45a. **The save holds counters per goal and a set of claims per period, never progress per task**, so a task
   added mid-week reads correctly on a device that had already done the thing.
45b. **Rotation is global and pure**: day `k` deals slate entries `k·n … k·n+n-1` modulo the live slate,
   with nothing stored. Editing the slate re-deals every period after it, **accepted rather than refused** —
   the server only *logs* a claim the current slate would not have dealt.
45c. **A task chest is recomputed like a daily chest and bounded like a streak night.** The subject layout,
   tag and stream numbers are contract, pinned by a **third** copy of the generator. The wallet remembers
   which task ids it paid per period and pays no more than the slate deals.
45d. **A refused claim is dropped by the client** — the server's refusals come back and the ledger drops
   them with the balance they inflated. A claim left *unconfirmed* is untouched.
45e. **A week begins on Monday, UTC, and is derived from the day key** — the epoch fell on a Thursday, so
   `day / 7` resets on Thursday mornings; the offset is three days and the server mirrors it.
45f. **A chest's lid is a reel and the reels are a scope**; the closed icons are global. Both addresses are
   built from the tier id, so `artnames.py` sees neither and both content gates walk the table.
45g. **The hub's box is a chest pack, and what made it one was taking the words off it.** **The grandest
   chest takes the crest** (a symmetric arch, seats handed out tallest first); the heights come off a cosine
   and the positions off a cursor, with the bell taken off an **integer** distance from the middle so two
   seats that ought to tie cannot differ in a float's last digit; and **the closed chest is frame nought of
   the opening reel**, so it carries the lid's headroom and the pack is laid out in *drawn* heights.
   `ChestPack` is the arithmetic, and the bell's range is rescaled onto the seats the row actually has.
45h. **The tasks page is the same pack**, with the names gone and replaced by the line saying the row can be
   tapped. **"A dead yellow bar" was the art**: the fill was cut by *draining* the pack's gold bar, which
   takes the chroma out and leaves the value at 86% of white — and `Image.color` is a multiply, so that was a
   **ceiling** on every bar in the app, fixed by one number in the tool. **Raising a value is a third
   question beside hue and chroma**; **a lift is normalised on a percentile, never on the maximum**; and it
   is **clipped per pixel rather than per channel**. A finished task's light is a **pool** outside the card
   and a **rim** on it, on one tween, with every pool in **one node built before any row**. **A prize is a
   picture and a sentence, not a bullet point.** The page's four chests stand apart where the hub's touch,
   because every chest here is a **button**. **A page made of plates wants a ground, not a place.** **And
   the name comes before the wallet.**
45i. **The hub's box carries its name and no clock**, and the title is the page's own key rather than one of
   its own — now the ceiling the crest is measured against.

### The season

47. **A season is a forty-rung ladder graded on *marks*, and a bloom is a claimed chest.** Every chest tier
   carries what opening one is worth, so the pace is content and every future source of chests feeds the
   season by naming a tier. The unit is repeatable and calendar-bounded at once, so there is nothing to farm
   and no cap anybody has to remember.
47a. **What killed the first one was naming content.** A season names **no content at all** now.
47b. **A rung names two tiers and no amounts.** **A paid column may not have a hole in it**, refused by the
   reader. A tier is resolved on **every read** rather than frozen when the calendar was parsed.
47c. **The bound is the ladder, not the play** — the grant log makes each rung payable exactly once. No
   window check, so a rung reached before a season closed stays claimable.
47d. **The pass is bought with gems, and that removed a whole apparatus rather than a price.** Both content
   gates now **refuse** a product carrying a pass entitlement by name.
47e. **A gem entitlement that gates a payout cannot live in the save alone, and the spend is what fixes
   that.** `pass:{seasonId}` is a **derived spend id**, turned into the entitlement **in the same
   transaction that takes the gems**. The **price is published** and the server refuses a debit under it.
   The client's own copy lives inside the season's save row, joined by `or`, and gates nothing.
47f. **The slack in a ladder is the decision, and it is arithmetic over two files** — a top a perfect player
   reaches on the last day is a countdown with a prize nobody can take. Both gates print the figures and
   **error** when the ladder cannot be climbed.
47g. **A season and a slate are one idea at two cadences, so they are one design.** A rung pays a chest, so
   it opens the **same ceremony every other chest opens**. Forty rungs is eighty chests, so the ladder
   recycles — **bind writes everything and never animates**.
47h. **A count of marks and a count of rungs answer different questions, and the hero draws both.** The bar
   measures the run from the **last** rung to the next.
47i. **A shape assembled out of primitives has no artist in it.** The crest is cut from **the same interface
   kit every plate comes from**, so the next kit swap re-cuts it too. A crest **lit** as the track fills is
   what 37m refuses. **The crest's name is content and its picture is not.** And **both mirrors were drawing
   something else**, because a generated crest is the one thing a mirror cannot load.
47j. **A season that runs again for ever is a *recurrence*, not a longer list.** Cycle `n` runs
   `[start + n·period, …)`, both sides compute `n` from the clock, and **nothing is stored on either**.
   **The period is the window, deliberately** — a separate "repeat every N days" is a second number that can
   disagree invisibly. Everything resets by itself because it is all keyed on the id. **The one thing it
   costs is the name**: a derived id cannot carry an authored string, so a cycle's name comes from a **pool
   of twelve that wraps**, taken only from the thing that minted its id.
47k. **What bounded a forged claim was the ladder being a finite list, and a calendar that never ends has no
   list — so the bound is the clock.** A cycle that has not opened does not exist, and is `unknown` rather
   than `refuse`; a closed one resolves without complaint. **Two spellings of one cycle would double the
   bound**, so the id parse is strict on both sides — exact stem, exactly four digits — and the padding is
   what makes ordinal order calendar order.
47l. **The 64-row ceiling used to delete the season being played.** The ledger **evicts** by what a row is
   worth: anything that *might* still hold an unopened chest survives ahead of anything settled, and the
   newest survives within each group. "Might" is a sound over-approximation read off the row alone.
47m. **A closed season holding an unopened chest has to be reachable.** `Featured` answers the **oldest
   season still owing anything**, falling back to live. **And `GroveEvents.All` is no longer the authored
   calendar** — it joins the ids the save holds onto it, skipping a season this build has never heard of.

### The streak

48. **A streak is a run of days that never ends, and it is the only reward here that asks a player to
   protect something they already have.** The count climbs for ever and the ladder **laps** under it.
48a. **The ladder pays credits, gems and chests, and a night that pays a chest names a *tier*.** Hearts and
   boosts are refused by name at parse. **And no chest below the second tier.**
48b. **Only the earliest waiting night may be taken, and that is what paying a chest cost** — the collected
   floor is a floor, so a sweep would grant three chests behind one animation. A tap on a newer one is
   **redirected to the oldest** rather than swallowed.
48c. **A streak can be protected, and the whole entitlement is the day it was bought** — one monotonic date
   joined by `max`. There is **one** number, so playing inside the window writes nothing to it, and a second
   purchase is refused while the first is running.
48d. **A protected day is *forgiven*, never credited** — the start slides forward by however many days were
   forgiven, **and the collected floor slides with it**.
48e. **The shield needed no server, and that is a claim about the *rate*** — a protected streak still
   collects at most one night per calendar day. What it cost is one line: the equality became a band, and
   the security is unchanged because the night must still **strictly climb**. The debit's id is derived, so
   two devices buying offline on one day write one entry.
48f. **A streak chest feeds the season exactly as a task's does**, so adding chests to this ladder **moves
   the season's pace**, and both content gates print the combined figure.
48g. **A list of rewards is a list, and the board was a grid twice before it was one.** A 240-unit tile
   forces a thumbnail, a two-word sentence and state said by colour; **a row is 1000 units** with the reward
   in a well, the night and amount in the middle and the answer on the right. **The right end carries one
   answer and the first of them is a word.** **It scrolls, and that is paid for rather than avoided** — it
   opens on the night that can be taken.
48i. **A reward that can be taken wears a light, and the light has to be drawn over the plate** — built on
   the row that becomes lit and destroyed when it stops, rather than built for every row and hidden. It was
   invisible first time because it sat *behind* an opaque plate. **The mirror dropped the withdrawn piece in
   the same change.**
48j. **The reward's size is a *drawn* height, converted by `ChestPack`.** The render mirror had the opposite
   error, because it crops to alpha.
48k. **The streak page says "level" where the rest of the game says "glade"**, at the owner's instruction —
   six strings moved, twenty-four others still say "glade". Deliberately not a sweep.
48l. **A repaint is a drawing of a state, so anything a one-off path switches off it has to switch back
   on.** The repaint wrote the reward icon's *colour* and never its `enabled`, so a collected night had a
   hole in it for the life of the screen. **Every gate here is green on a stale widget** — the mirror draws
   a *state* rather than a sequence.

### The update wall

49. **A client can be withdrawn, and the whole of what makes that reliable is that the requirement lives on
   the device** — a wall that lived only in memory is dismissed by force-quit and reopen in flight mode.
   **And the server's answer governs, which is what makes it reversible**: a successful read replaces what
   is held in *both* directions and a failed one changes nothing. Deleting `config/release` lifts every wall
   in the world on the next check, and that is the intended emergency stop.
49a. **A wall needs a door, and the two travel together or neither is applied.** A requirement naming no
   usable link is neither enforced nor cached, and is refused again by the seeder. **`https` rather than
   `market://`**, because a failed scheme link opens nothing.
49b. **It is per store because the two stores do not ship on the same day.** Android's link is derivable
   from the bundle id and iOS's cannot be. The seeder refuses a minimum ahead of the tree's own
   `bundleVersion`.
49c. **It is device-local and must never be in the save** — merged across devices, an iOS minimum would
   follow somebody onto an Android phone. **One preferences key holding both halves**, because a process
   killed between two writes pairs a new minimum with the previous release's link.
49d. **The panel is a drawing of a state, re-asserted every frame, and the poll is the design rather than
   laziness.** A screen change destroys every modal; an offline device gets no event to raise it from; and a
   rolled-back requirement has to take it *down* again. It sits above every other modal, refuses the
   hardware key **by answering it**, and is not raised over the splash.
49e. **Nothing is stopped underneath it, and that is deliberate** — every write this game makes is an
   idempotent monotonic join, which is what makes an old client pushing safe by construction.
49f. **It reads a public document rather than a Remote Config SDK, and rather than a callable. Signed
   out**, because if the thing that no longer works is ever authentication then a gate behind sign-in cannot
   close on the builds it was written for.
49g. **A version string is one integer and there is one parser, with two defaults.** Content's
   `minAppVersion` floors at 1; the running version answers **0**, which is never walled out. A segment
   above 99 is **refused rather than clamped**, because the direction it fails in is the bad one.
49h. **A shape assembled out of primitives has no artist in it** (47i's twin) — and the sentence sat 89
   units off because `UIKit.Box` **always pivots at centre**. **A gold arrow pointing down says *download***
   where an up arrow says *upgrade*. **The band is deeper than the English needs and the line is centred in
   it**, because `UIKit.Shrinkable` truncates.
49i. **`loc.py` proves every key *resolves*, never that what it resolves to is still true.** The wall's
   sentence names **no** game, so it cannot go stale a second time.

### Reminders

50. **Every reminder this game sends is derived on the handset that sends it, and that is the whole
   architecture.** The bill is nought at any player count, for ever — a nightly push fan-out is one document
   read per player per send, every one saying something the device already knew. **What it cannot do is
   broadcast**, and the answer when that is wanted is **FCM topic messages**, deliberately not built, as a
   sibling of `INotificationScheduler` rather than a widening of it.
50a. **A sentence is not content, so adding a reminder is a build; which ones are sent, and when, is
   content.** It is the one block in `progression.json` that reaches no server.
50b. **The copy is baked when the schedule is armed and read up to a week later**, so any number inside it
   must be a function of the *fire* instant. Every string is argument-free and none names a retunable count,
   a season or a chapter.
50c. **Nothing about the schedule is stored, which is what makes re-arming safe** — the plan is a pure
   function of the save and the clock, so `Arm` cancels the lot and writes it again. **A scheduler that
   appended would be invisible to every test.**
50d. **Armed when the app is backgrounded and at no other time**, after the save flush, never before.
   **Losing focus is not backgrounding** — Android raises both on the way out, so arming on focus ran the
   whole thing twice per ad, and `Arm` cancels before it rewrites: the gap where nothing is pending is the
   cost, not the work, and nothing else ever arms one.
50e. **Slots are hours of the player's *local* day, and it is the one clock here that is not UTC**, because
   nothing about a reminder is adjudicated.
50f. **The ceiling is enforced by silence, so it is arithmetic in a gate** — iOS keeps the 64 soonest
   pending local notifications and drops the rest with no error. **The count, not the span, binds.**
50g. **"Two to three a day" is an outcome of the cooldowns, never a quota** — a slot that finds no candidate
   whose sentence is true is left empty. **And a standing fact is not news**: the plan thins out the longer
   somebody is away, because a game that nags harder the longer you ignore it is a game you uninstall.
50h. **The permission is spent once in a player's life, so it is asked where the answer is obvious** — at a
   chest, never on the splash. **The switch and the OS answer are two different facts**, both device-local.
50i. **An Android status-bar icon is a silhouette, and the default behaviour of shipping none is a white
   blob** — the package falls back to the launcher icon, whose alpha is a solid square. It ships in a
   `.androidlib`, because `Assets/Plugins/Android/res` is deprecated.
50j. **A 24dp mark is not a small picture, it is a different picture** — drawn for 24dp, nothing thinner
   than 1/12 of the canvas, with `--contact` on a dark ground and a light one as the gate.
50k. **A `versionDefines` flag says a package is installed and adds no assembly reference.** `compile.py`
   compiles without the package, so it takes the null branch and every API call is invisible to it — **a
   check that cannot fail is not a check**, so there are three passes (no package, `UNITY_ANDROID`,
   `UNITY_IOS`). **And the reference has to be platform-aware in the source as well as the asmdef.**
50l. **Read the package on disk, never the docs page — the published documentation describes `master`.**
   2.4.3's unified `NotificationCenter` carries no icon field, so the binding goes direct to the two
   platform APIs.
50m. **An `.androidlib` manifest is parsed by Unity with `System.Xml`, so a double hyphen in a comment
   aborts the entire Android build** with an `XmlException` naming Unity's own source files. Every offline
   gate was green; it appeared only at `BuildPlayer`. The tool refuses one by name now.
50n. **The horizon is reach, and reach is bought with a taper rather than with a bigger number** — three a
   day spends the allowance in a week, one a night spends it over three. **It is also the right shape**: the
   frequency that reads as attentive on day one reads as desperate on day fifteen.
50o. **Two known gaps are deliberately not fixed.** `USE_EXACT_ALARM` and
   `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` are both Play policy violations for a game — **a reminder is not
   an alarm.** And the remote kill switch is `ContentConfig.RemoteBaseUrl`, which needs a CDN that does not
   exist; until then the quietening lever is an app update.

### Art credits

46. **An art credit belongs wherever its licence says, and for this game that is nowhere in the app.**
   CraftPix, Envato and the Unity Asset Store ask for nothing; KayKit is CC0; **Freepik** was the one vendor
   that ever compelled a line and no Freepik pixel still ships. The courtesy list lives on the publisher's
   site, where it can be corrected without an app update. **Before cutting a new pack into this game, read
   its licence for the word *attribution* first.**
46a. **A licence can forbid *shipping* a thing as well as forbid using it unattributed.** `GameFont.ttf`
   was Segoe UI Black, copied out of `C:\Windows\Fonts` and renamed; no gate here could ever have seen it.
   It is **Titan One** now, SIL OFL, renamed **Gemfire Display**. A variable font is not a drop-in (Unity
   renders one at its *default* instance), a missing glyph draws as **nothing at all**, and an OFL face may
   carry a **Reserved Font Name** — checked per face, never assumed. **Whatever replaces it must keep the
   address**: `Fonts/GameFont` is a role, exactly as `btn_green` is.
46b. **A face is chosen by looking, so put ten in front of the owner rather than one.** Two rounds of one
   face each cost a full cut, install and render to learn one bit; a specimen of ten settled it in a single
   message. **The measurements were good for knowing the swap moved no layout, and nothing else.**
46c. **The cost of a display face is its coverage, and it is paid in other people's names** — 172 glyphs
   are built out of the face's own base letters and marks, and **the report is printed on every build and is
   not a gate**. Four faults in that builder, all caught by the proof sheet: placement has to be learned in
   **y** as well as x; the `.case` marks are not the capital forms despite the name; the set must be
   enumerated over Unicode ranges, never listed by hand; and **a mark has three possible spellings**. Still
   absent: Vietnamese, Romanian `ș`/`ț`, Greek, Cyrillic, Arabic, Hebrew, CJK — the repair is
   `fallbackFontReferences`, not another face.

## Spent ids — never reuse any of these

**Enforced by a machine** (add the id in the same change as the removal; the tool refuses it): grove piece
ids in `Tools/grove_retired.txt`; lesson ids in `Mechanic.Retired`, held to the live set by
`TipTests.EveryMechanicIsEitherLiveOrRetired`; level block names in `content.py`'s `RETIRED_BLOCKS`.

**Enforced by nothing but this table.** A chapter or level id can be re-authored by accident, and a real
save may hold a record against any of them — which is why `ProgressionStore`'s floors exist (9).

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
| `bud` | `b02_tanglewood` | `b02_firstbolt` `b02_makefive` `b02_sunspark` `b02_crossfire` `b02_stormheart` `b02_sixfold` `b02_thundering` `b02_sunwell` `b02_wildstorm` `b02_stormcrown` — **and fifteen from two earlier cuts**: `b02_firstvine` `b02_longreach` `b02_deepthicket` `b02_windingway` `b02_twovines` `b02_thewilds` `b02_crossvine` `b02_thornedvine` `b02_thetangle` `b02_tangleheart` `b02_windrow` `b02_lanternfly` `b02_graftwood` `b02_puffhollow` `b02_hivehill` |
| `march` | `m01_hollowmarch` | `m01_firstcore` `m01_haulroad` `m01_thegate` |
| `ember` | `e01_emberforge` | `e01_firstember` `e01_twinlocks` `e01_ironribs` `e01_frostvein` `e01_chainfire` `e01_deepcell` `e01_ironward` `e01_twinstars` `e01_slaghold` `e01_emberheart` |
| `weave` | — | *(Lightweave; retired before its chapter shipped)* |
| `ripple` | — | *(Ripplewake; never authored a level)* |

`f03_wickwater` and its ten level ids are spent too — the one case where the *chapter* id moved rather than
being kept, because that chapter never left the working tree.

**Other spent names, tokens and members.**
- **Board tokens, refused by name at parse** (5f): `x`; the wick letters `1` `2` `3`; `/` and `\` as a
  mirror; `%` as a sack and `!` as a *cell* (`!` lives on as a **wave** token); `*` as a cog; and the
  one-letter boss form.
- **Fields refused by name**: `runners`, `winds`, `firefly`, and `reach` on a blast.
- **The ward ability `prism`** — the one retired name deliberately *not* refused, because
  `WardAbilities.Parse` answers `none` for an unknown name, so a rolled-back client fires a plain bolt
  rather than standing an empty seat. Both content gates warn. The turret **ids** `prism` and `spectrum`
  are live and carry the stun.
- **Enum members kept rather than deleted, because their ordinals reach analytics on every run ever
  recorded**: `ContinueUnit.Tiles` / `.Taps`; `DefeatReason.OutOfTiles` / `.Overgrown` / `.OutOfTaps` /
  `.Barren` / `.OutOfTime`; `ChestDropKind.RunTime`; `SiegeKind.Weaver` / `.Thief`; `SiegeSpell.Weave` /
  `.Snatch` / `.Bombard`.
- **Retired in place on the wire**: `bestMillis` (22) and `DailyChests`' whole section (45) — a field a
  rolled-back client still writes cannot be dropped from `hasOnly` without losing *every* save write (12a).
- **The nine league board ids `l0`…`l8`** and the `league` field on a published grove card. A board id names
  a document a nightly job writes, so invariant 1 reaches it in full. `GroveLeague` and the two `groves`
  composite indexes on `league` went with them.
- **The season id `first_watch`** — a season id names a save row, a grant-log key and a pass entitlement.
  Seasons are now the stem `watch` plus four digits.
- **The store product `gg_first_bloom_pass`** — **registered with both stores and therefore never
  reusable**; deactivate it in App Store Connect and Play Console rather than repointing it.
- **The grove homes `home_longhouse` `home_tower` `home_keep` and the decor `barracks` `citadel`**, whose
  models two dwelling rungs wear under new ids — minting a new id rather than promoting the decor one is
  what keeps the stock honest.
- **The ad placement `run_continue`**; **the map sprite `boat`**.
- **Withdrawn siege reels** (art addresses, so nobody re-mints one): the baked casts `kayMon` / `kayBrute` /
  `kayBulwark` and `ironMon` / `ironBrute` / `ironBulwark` with their twelve `_swing` reels each, and the
  boss walk reels `caller_walk` / `snare_walk` / `clad_walk`. The boss reels `caller`, `snare` and `clad`
  are **live and were re-cut in place**.
- **Retired loc keys are deliberately not listed.** A loc key names a sentence rather than a thing a save
  can hold, so a retired one may be re-minted (5f). What may never come back is a level id, a chest tier, a
  grove piece, a board id, a season id or a store product.

## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Modes/ Wards/ Utilities/ Persistence/ Progression/ Homestead/ Cloud/
  Localization/ Analytics/ AssetPipeline/ Store/ Ads/ Daily/ Events/ Social/
  Notifications/ Release/
Assets/Game/Scripts/Notifications/  GlimmerGrove.Notifications (Domain; the mobile-notifications binding)
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
Assets/Game/Authoring/             GlimmerGrove.Authoring    (Editor-only; Domain)
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode)
Assets/StreamingAssets/Content/    manifest.json, chapters/, homestead.json, loc/
```

**`GlimmerGrove.Authoring` is the home for a rule that decides whether content is fit to ship and that no
player ever runs.** Such rules must be reachable by the build gate *and* by the test assembly (which
references `Domain` and not `Editor`). **A rule belongs there when no shipped type references it**, and
`compile.py` proves it by building `domain` *without* `Authoring` on its reference list.

**A mode is declared three times.** `LevelMode` (Domain) says what a mode *is*; `ModeLook` (Presentation)
what it *looks like*; `ModeValidator` (Authoring) how it is *proved fit to ship*. **The price of a registry
over an abstract member is that an entry can be *missing* where an override cannot**, so an unregistered
mode is an **error** and a fixture fails the build when the two registries drift.

## Verifying

The Editor is often not running, and the MCP bridge is unavailable whenever scripts fail to compile. Do not
guess — verify offline.

- **Compile:** `Tools/verify/compile.py` runs Unity's bundled Roslyn directly (see
  `verify-content-without-unity` in the memory directory for the command). It also refuses: a
  `MonoBehaviour` method named after a no-argument magic message; a null test on a class-typed DTO field; a
  read of `.Layout.` with no acknowledgement it can be absent; and a file that registers a `ResourceSlots`
  readout without attaching a `WalletWatch` (44j).
- **Content:** `Tools/verify/content.py` — parse the StreamingAssets JSON, prove every level solvable,
  derive par, confirm every loc key resolves, and run the shared board vectors through both Python copies of
  the four-armed-tile rule. **It reads the manifest**, so a disabled chapter is skipped whole. It also walks
  the keeper walls, the turret roster (addresses are *built* from an id, so `artnames.py` cannot see them),
  the season recurrence, the streak ladder, the reminder slate and the continue ladder.
- **Inline rung tables:** `python Tools/verify/rungs.py` holds the hand-copied rung arrays in
  `SiegeRuleTests` to the chapter bodies that ship — thirty rungs are written twice and when they drift
  **nothing fails**.
- **Difficulty:** `python Tools/verify/difficulty.py` — what each glade asks of a player. Not a gate (5d).
- **Map seats:** `python Tools/make_map_seats.py --check` proves the seats are still what the paintings say;
  `--contact` draws every map with its chain on it, which is the gate that matters. `content.py` proves the
  seats clear each other. Re-run `--write` and then every chapter generator after any map painting change.
- **Effect reels:** `Tools/verify/fxreels.py` proves every baked reel is a *picture* — it measures the lit
  box against the frame at the reel's loudest moment and refuses a sliver. `artnames.py` proves a name
  resolves and nothing proved the thing behind it was visible; three of 266 reels were threads, all three
  boss spells (37de). It says nothing about whether a reel is the **right** picture — that is a contact
  sheet and an eye.
- **Sprite names:** `Tools/verify/artnames.py` proves every sprite a *call site* asks for exists on disk.
  **A constant is invisible to it**, so routing sprites through a skin table gives up the gate — the chain
  is closed by `SkinsTests`, which walks `Skins` by reflection against what the manifest loads.
- **Sound and music names:** `Tools/verify/sfxnames.py` proves three lists agree — what the code plays,
  what is on disk, and what the manifest preloads.
- **The font:** `python Tools/make_game_font.py --coverage` looks up every character the shipped strings
  contain in the font's own cmap; `--check` proves the shipped TTF is what the tool cuts.
- **Which pack a cast could come from:** `python Tools/make_siege_art.py --survey` draws one frame of every
  body in every character pack on this machine and prints, per pack, **how tall its shortest body is and
  what upscale reaching the cut height would take**. **Before concluding there is nothing left to cast a
  chapter from, run it** (37db).
- **That a baked cast is the animation the artist made:** `python Tools/spine_bake.py --verify` bakes every
  rig and compares it against the pack's own exported frames; `--vector` runs the same comparison through
  the vector source, proving the `.ai` pages were matched to the right parts.
- **Charms:** there is no offline gate and there cannot be one. The instruments are `SiegeCharmTests`, the
  hold simulation, and `render_siege.py --charms` / `--lance` / `--volley`.
- **Name fold:** `Tools/verify/names.py` runs the fold against the shared vectors **on Unity's own Mono**,
  not the bundled .NET — the first version ran on .NET 8, whose ICU agrees with Node, and passed with a
  mapping deleted. **A check that cannot fail is not a check.**
- **Why a test says "needs the Editor":** `GLIMMER_WHY=1 python Tools/verify/tests.py`.
- **One test, while tuning:** `python Tools/verify/tests.py SiegeRuleTests.EveryShippedBossRungIsAFight`
  (`Fixture.Method`, both substrings). The chapter sweep and the fight gate print their tables on a pass.
- **Renders (the gate that matters for anything judged by eye):** `render_home.py`, `render_shop.py`,
  `render_tasks.py`, `render_season.py`, `render_streak.py`, `render_keeper.py`, `render_endless.py`,
  `render_loadout.py`, `render_siege.py` (`--tablet`, `--warlord`, `--volley`, `--standing`, …), all sharing
  `Tools/hudkit.py`, which mirrors `UIKit` and `Skins`. `make_season_crest.py`, `make_update_icon.py`,
  `make_notification_icons.py` and `make_chest_art.py` each have `--check` (reproducibility) and `--contact`
  (whether it reads). **`--check` proves reproducibility and says nothing about quality.** Every art tool
  passes with the licensed pack absent, because the PNGs are committed.
- **Seeders:** `node firebase/seed/seed-release.mjs --check` (the update wall's only gate — nothing about a
  forced update is content); `npm --prefix firebase/functions run seed -- --check`;
  `Tools/make_name_blocklist.py --check`; `npm --prefix firebase/functions test`.
- **Chest generator copies:** the shared vectors are written by `Tools/make_task_vectors.py`,
  `Tools/make_mark_vectors.py` and `Tools/make_streak_vectors.py`, each splicing its block from its own
  marker to the end of the file — **so they must run in that order**.
- **In the Editor:** `Glimmer Grove ▸ Validate Content`, `▸ Validate Art`, and Test Runner (EditMode).
  **Reload the domain before believing a failure that follows a play-mode session** — `Boot` starts the
  cloud backend and its threads, and those statics survive leaving play mode.
- **Art tools and renders** in full are in `CRAFT.md`. Two rules belong here: **a new mode's art tool and
  render mirror are committed with the drop they were written for**, because an uncommitted tool is deleted
  for good by the withdrawal; and a new dependency is recorded here, because this repo has no
  `requirements.txt` (numpy, Pillow, and **PyMuPDF** for `make_siege_art.py`'s vector path, imported lazily
  so a checkout that never cuts that cast runs every gate without it).

Builds are gated: `ContentBuildGate` fails the build on any content error.

## Hard-won facts

**Unity and the build**

- **Addressables must be ≥ 4.0.1** — 2.x calls `Object.GetInstanceID()`, which Unity 6000.3+ made an
  *error*-level obsolete.
- **`GLIMMER_ADDRESSABLES` comes from asmdef `versionDefines`, not Player Settings**, which are per build
  target and would ship a mobile build with no art and no error explaining it.
- **`m_BuildAddressablesWithPlayerBuild: 1`** is set in the project asset, not a per-machine preference.
- **A Unity magic method is an engine rule, not a language rule, and the offline compile is blind to it** —
  a green `compile.py` is not by itself proof the Editor will accept a build.
- **Unity only re-resolves packages and reimports on window focus.** If a change seems not to apply, the
  Editor probably has not been clicked.
- **`refresh_unity` says `compile_requested: true` without compiling anything.** Force with
  `CompilationPipeline.RequestScriptCompilation()` through `execute_code`. **Before believing any bridge
  probe about code you just edited, check the DLL is newer than the file.**
- **A gate that reads the file system lies while Unity is reimporting, and it lies by *failing*.** The tell
  is that the failure count *moves* — run it twice and compare.

**Serialisation and arithmetic**

- **A `[Serializable]` class field is never null after `JsonUtility`, so never test one for null.** The
  fixed shape is a value a real block cannot hold (`IsAuthored`).
- **`LevelDefinition.Layout` is null on any level that is not a glade, and nothing in the language says so.**
- **`JsonUtility` has two parse refusals that read as logic bugs** — it rejects a number written `.5`, and
  it **truncates a string at an escape sequence**. Vectors carrying awkward text carry **code points**
  alongside the string, and the other runtime asserts the two agree.
- **Nothing that decides a cell may be a `float`, because the runtimes disagree about them** — .NET 8 said
  13 and Unity's Mono said 12 on the same expression, and a phone runs IL2CPP, a third code generator.
- **Nor may a float decide a *threshold*** — `Mathf.CeilToInt(45 * 1.20f)` is 55 where `par x 1.20` is
  exactly 54, and every runtime is wrong the *same* way, so no cross-runtime diff finds it. Factors are held
  as hundredths and thresholds derived with `(par * n + 99) / 100`.
- **`'' in 'RGB'` is `True` in Python, so an empty cell reads as a piece.** Every offline mirror tests a
  cell against a string of letters, which is a substring test the moment a cell can be empty.
- **Whenever two tenths multiply, keep the hundredths and divide at the end** — ten per cent of 8 is 8.8,
  which truncates back to 8.
- **A NumPy scalar is *strong* under NEP 50 where a Python float is weak**, so one `np.floor` of a Python
  float promotes a whole float32 pipeline to float64. The tell is a traceback naming a `float64` array of a
  shape you only ever built in float32.

**Tests and probes**

- **A probe that writes off a whole class of failure as "expected here" cannot see a real one inside that
  class — and it will report success.** 94 null sprites correctly written off as "the scope is not loaded"
  hid five real ones a player met as a white rectangle.
- **A vector file that only the Editor can read is not a guard on the rule it pins** (29e).
- **A `[UnityTest]` that counts frames while the code under test counts milliseconds passes only at a frame
  rate nobody promises** — under `-batchmode -nographics` frames are unthrottled. Yield frames but bound the
  wait on `Time.realtimeSinceStartup`.
- **Edit mode dispatches no `MonoBehaviour` messages**, so `DestroyImmediate` never runs `OnDestroy` and a
  case asserting something was *not* handed back passes for the wrong reason.
- **The ads plugin's Editor consent stub sets `Time.timeScale` to nought and leaves it there.** Set it back
  through `execute_code` before driving a board in the Editor.

**Art and addressables**

- **Deleting art leaves its Addressables entry behind, and that fails the build rather than the game** —
  the audit errors on any registered entry whose asset has gone. **And a group the Editor has repaired is
  not a group on disk**: dropping an entry marks the group asset dirty and nothing more.
- **A sweep that files a *gitignored* pack into a *tracked* group asset leaks broken references into every
  clone.** A sweep may only register what the repo carries. The tell is a tracked `.asset` that comes back
  dirty after a sweep you did not intend to change anything.
- **A reel nothing can load still ships** — addressed frame by frame with no label, it cannot be loaded as a
  reel at all and is built into a bundle for the life of the game. The audit reports it as dead weight.
- **An orphaned Addressables *label* is a string in a list and nothing more, but the same symptom has two
  causes**: a label whose folder is *gone* is a leftover; one whose folder is *still there* is a reel
  nothing can load. Check against disk before deleting.
- **The importer hook does not address art copied in while the Editor is closed or mid-reload**, which is
  every run of the art tools. `▸ Addressables ▸ Sync All Assets` is the repair; the audit stops it shipping.
- **A sprite set is loaded by the *label* its frames share, not by its folder.**
- **A preprocessor fires on first import only**, hence `▸ Reapply Art Import Rules`. **It must batch** —
  `SaveAndReimport` per texture crashed both import workers on 335 back to back.
- **The sprite atlas file extension selects the importer.** A `.spriteatlas` in the V2 format imports as
  editor data and produces no `SpriteAtlas` at all. It must be `.spriteatlasv2`.
- **A licensed pack's preview sheets carry the vendor's own dummy lettering, and grading it makes it *less*
  obvious.** Prefer a pack's `layers/` art to its `_preview` sheets, and look at a source at the size the
  game draws it.
- **A VFX pack's `Textures/` folder is two different things with one naming scheme** — what a particle
  *draws* and what its shaders *sample*. **Before naming a texture after the effect you want, render it.**
- **A flood keyer is the wrong tool for an object whose *edge is a glow*, and it fails by keeping almost
  nothing.** A keyer is a question about an edge, not about a colour.

**Platform, store and backend**

- **`PlayerPrefs.Save()` serialises the whole store synchronously**, but the flush cannot simply be dropped:
  Unity persists `PlayerPrefs` during `OnApplicationQuit`, which on a phone is the ending that rarely
  happens. Compare first, flush when it really changed, and read the store rather than a remembered value.
- **An Editor launched from the Hub gets a minimal `PATH`, and one failed post-processor abandons the
  rest** — the CocoaPods resolver cannot find `pod` on Apple Silicon, and it took the privacy plist writer
  down with it.
- **Two Google ads SDKs cannot share an APK, and a mediation adapter can drag in the second one.** This
  project holds the legacy `play-services-ads` permanently (it is how UMP arrives), so the *adapter* must
  bend: **5.18.0.0** is the last on the legacy SDK.
- **Sign in with Apple on iOS cannot use the generic IDP path, and the refusal kills the process** — a Swift
  `fatalError` is not an exception, so no managed `catch` runs. It ships looking correct because Android
  works.
- **A Functions secret is pinned at deploy time, so a correct key can produce a 401.** And **a 401 from the
  production App Store endpoint followed by success on the sandbox one is the *normal* path** for a sandbox
  purchase; the failure is both endpoints refusing.
- **A newly created 2nd-gen callable has no public invoker binding and answers every call with 401**, which
  from a client is indistinguishable from being signed out. **The grant is not instant** — it can 401 for
  minutes after being issued, so probe one call directly before re-running a suite:

      gcloud run services add-iam-policy-binding <lowercased-name> \
        --region=europe-west1 --member=allUsers --role=roles/run.invoker

- **Never `firebase deploy --only functions` for the whole codebase** — it failed all fourteen updates with
  a transport error while still *creating* the new function. **Deploy by name, in batches of three or
  four**, which also keeps another agent's in-flight source out of anything that runs.
- **"The deploy said Successful" and "the running bundle has the fix in it" are two different facts.**
  `functions:generateDownloadUrl` fetches the deployed artifact to read back.
- **The Firebase Unity SDK's `Firebase.Functions` ships as source with its own asmdef**, so the Cloud asmdef
  must reference it explicitly, and that source needs `Google.MiniJson.dll` from the **app** package. All
  Firebase packages share one version.
- **Google's UMP plugin must come from OpenUPM as a package, never as the `.unitypackage`** — the latter
  unpacks as loose files, so it carries no version, so `versionDefines` never fires and **nobody is ever
  asked anything**.
- **`seed-config.mjs` publishes the working tree**, so a re-seed ships anyone's uncommitted content. Seed
  from a HEAD shadow tree when another agent is mid-edit — and **a shadow assembled from patch scripts is
  only as current as the patches**; diff the block against the tree before seeding from it.
- **Check `firestore.rules` against the *released* ruleset, not the tree** (`firebaserules.googleapis.com`,
  with an `x-goog-user-project` header), because another agent may have a release in flight.
- **The live e2e suite signs in as a new anonymous account every run**, so anything derived from the account
  id varies — **including level ids**. Anything the published catalog decides has to be read off the
  published catalog. It is also sensitive to cold starts: re-run before believing a failure in the first
  minute after a deploy.

## Current state

*What is true now.* **Read the manifest, never this file, when a number reaches a customer.**

### Built and verified

- **Content pipeline** — levels as data, stable `LevelId`s, manifest-built `CatalogIndex`, lazy chapter
  bodies, `Content ▸ Sync Manifest`, build gate.
- **Save** — versioned atomic file with checksum, backup rotation, corrupt-file recovery, tested
  migrations, monotonic merge. **Save schema v28.** Content schema: manifest and chapter bodies **v2**,
  grove body **v3**.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google linking,
  per-account local archive for switching, debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water floors only.
  Hearts and hints are produced/spent ledgers. Chapters open on stars (21); a mode's opening levels are free
  to fail (24).
- **Retention** — tasks and the chest ladder (45), the recurring bloom season (47), the streak (48), golden
  levels, percentile standings, per-level records.
- **One live mode, three hidden.** **Thornwatch**: `s01_thornwatch`, `s03_broodmarch`, `s04_barrowfell`,
  `s05_ashenhold` (ten rungs each) on the ordinary ladder, and `s02_endlesswatch` on an **Infinite** track
  beside it. The map draws no *mode* switcher and does draw the **track** switcher; the ordinary ladder
  draws a map and the Infinite lane draws a **hub**, opening at **keeper level 10**. Four casts and eight
  boss verbs, one cast per chapter by ordinal: insects, the blob brood, skeletons and the **rabble** — the
  only cast **baked from vector**. The Infinite lane draws a **medley** of the four chapter casts, so it
  costs no art of its own.
- **Charms** — three powers dealt onto ordinary gems, one introduced per chapter: a **prism** (joins a run
  of any colour), a **lance** (its row and column), a **stormglass** (the whole line fires at everything on
  the hill). One every **112** dealt gems on a window, the first of a run inside **56**. Each is a gem of
  its own with its own reel; a lance runs the hill in slow motion and a stormglass stops it dead.
- **Utilities** — an account-wide action bar (39), dropped by chests and bought with gems, charged against
  the graded count so one can never buy a star.
- **The turret loadout** — twenty turrets bought per colour, each behind a keeper level and nothing else,
  upgraded to five stars, previewed firing before purchase, carried in from a readout on the map.
- **The grove** *(held — see the note under **The grove**)* — a village on a 28x28 isometric tile floor
  (784 tiles), 86 pieces rendered from one CC0 pack: a four-rung home ladder ending in a castle, houses,
  civic buildings, walls, gates, trees and props. A piece stands on an authored footprint (1x1 to 4x4) and
  can be turned.
- **Boards** — **one drawn, two published**: **Finest grooves** (held with the Grovement) and the **Endless
  Watch**. A hundred rows each, one document each, rebuilt at 04:00 — about fifteen thousand reads a night
  at ten million cards. Plus **two published distributions** off the same five-thousand-card sample, each
  refusing to answer under 200 samples. The nine league boards are gone.
- **A keeper seen from outside** *(profile and name reporting live; the chooser, the grove card and the
  grovement report subject are held)* — a read-only profile built from the published card: keeper level and
  honorific, the Endless Watch with a percentile, companions **gathered** (held only), the four turrets
  carried with their rungs.
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads, refund sweeps,
  server-adjudicated grants, a gem-priced doubling continue (23c) and a bonus wheel (25).
- **The front of the game** — one bought interface kit (44), cut by `Tools/make_hud_kit_art.py`; the display
  face is Titan One shipped as **Gemfire Display** (46a).
- **Reminders** — two to three local notifications a day, scheduled on the device and costing the server
  nothing at any player count (50). Ten kinds; three local slots (09:30 / 13:30 / 19:30) for a week, then
  one a day out to **twenty-one**, re-armed every time the app is backgrounded.
- **The update wall** (49) — one public document per store; a client that has been told writes it down, so a
  cold start with no signal enforces it and a force-quit is not a way out. It ships **asking for nothing on
  both stores**, which is the state a release is raised from. Proved on an Android device, including the
  force-quit-in-flight-mode pass. **iOS is armed and has never been exercised.**
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (placeholders).

### Content shipped

| Chapter | Mode | Levels | Par range | Subject |
|---|---|---|---|---|
| `c01_shallows` … `c04_nightbriar` | glade *(hidden)* | 10 each | 10–70 turns | the verb, then colour and blending; the crossing; colour as the subject; the briar |
| `f01_lightfall`, `f02_glasswater`, `f03_whorlwater` | fall *(hidden)* | 10 each | 2–6 drops | the cook and the chain; the lens; the whorl |
| `p01_prismvale` | prism *(hidden)* | 2 | 3–4 swaps | drag to swap; a lantern feeds its own colour and a vein can be broken |
| `s01_thornwatch` | siege | 10 | 14–57 matches | the verb, the cog, the **prism** from rung 3, the fourth ward, a warlord on 5 and an overlord on 10 |
| `s03_broodmarch` | siege | 10 | 38–59 matches | one new rule (the **lance**), a second cast, a hill that no longer forgives rank one; a blightcaller on 5, a warbringer on 10 |
| `s04_barrowfell` | siege | 10 | 49–81 matches | the first chapter authored for a *bought* line and the one that deals all three charms: a skeleton cast, armour from rung 2, a gravemaw on 5 and a bonecaller on 10; **the first chapter whose raiders carry a surge** |
| `s05_ashenhold` | siege | 10 | 49–81 matches | the fourth chapter and the first that cost the mode **code**: the **rabble** cast, armour from rung 1, a **shackler** on 5 and an **ironclad** on 10; **two tenths of surge** |
| `s02_endlesswatch` | siege *(infinite)* | 1 | 3★ at wave 20 | waves that never stop, graded on how far it got, drawing a **medley** of every cast; **both star waves are guesses until somebody plays it**; opens at keeper level 10 |

**No level authors a difficulty number except the first glade in the game, and no chapter authors a clock.**
Par is derived; star lines are multiples of it. **Par is never monotonic within a chapter** — par is length,
not difficulty. Every siege authors `budgetFactor: -1`. Chapter art is generated and **shared by ordinal**
(7c): four maps and forty skies serve every chapter of every mode.

### The numbers

**Every figure lives in `manifest.json`, `homestead.json` or `progression.json` and both content gates
derive and print the totals — read them there, never from here.** Free play collects about **936 credits and
12 gems a day**. Everything except the shop ladder is content and retunable without an app update; **re-seed
after any change**. Only the shapes that are not obvious from the files are worth recording:

- **Stars** — gold `par x 1.20`, silver `par x 1.40`, the run ends at `par x 1.60`, **except a siege, which
  authors its own per chapter** (37ca) from that chapter's own sweep.
- **Chapter gate** — 2 stars a level of the chapter behind it, per mode; the first chapter of every mode is
  always open, and a chapter may also carry a `minKeeperLevel`.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted); a heart container raises the cap to 10, 20
  or 50 permanently. **Hints** — pool of 3, one back every 8h, spent in the glade and nowhere else.
- **Continue** — 20 gems doubling within a run, topping out at the 5,000-gem ceiling on the ninth.
  Deliberately **not seeded**. **Heart rescue** — 20 gems for +2 hearts, priced off the smallest gem pack.
- **Shop** — 16 products, $293.84 total; the only ones granting something other than currency are the three
  heart containers (18d).
- **Ads** — four placements, all opt-in, no interstitials. **Bonus wheel** — eight equal slices, mean
  218.75% of the authored amount.
- **Out of reach on today's content** (which pays for about keeper 13, and about keeper 9 for the grove):
  the turret shelf's tiers two and three, and all three paid home rungs. Deliberate, and the owner's call.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`. **Fourteen
functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`, `adReward`, `appleNotification`,
`sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`, `withdrawGrove`, `publishGroveRanks`,
`claimName`, `reportKeeper`, `deleteAccount`. **`firebase functions:list` is the authority** — a fifteenth,
`eventPass`, was deployed and later deleted while never appearing in any list here. `firebase/README.md` is
the guide; `firebase/e2e/smoke-test.mjs` is **132/132 live** (2026-09-15) and
`firebase/e2e/delete-account.mjs` **14/14**. Client half is `Assets/Game/Scripts/Cloud/`, Firebase Unity SDK
13.15.0 as vendored UPM tarballs under `GooglePackages/` (gitignored — run `pwsh GooglePackages/fetch.ps1`
on a fresh clone).

## Owed

**Money paths that have never executed.** A real receipt reaching `redeemPurchase` and a real impression
reaching `adReward`. Both are fully built and deployed and **neither has ever run once**, which reads as
done. Ads *load* on device; no view has ever paid. Do both the day closed testing opens.

**Store and platform.**
- Delete an Apple-linked account on a device and check it leaves **Settings ▸ Sign in with Apple**. Every
  account the live suite makes is anonymous, so Apple's token exchange and revoke have never executed.
- Register the `appleNotification` URL for **both** production and sandbox.
- AdMob **instances** under each LevelPlay ad unit — blocked on a public listing, because AdMob rejects an
  app-store URL that does not resolve. Delete the retired `run_continue` unit while there.
- Fill in `app-ads.txt` and host it on the domain in both listings; turn on in-app bidding. The ironSource
  and Unity Ads lines are commented placeholders in **both** repos and must move together.
- Watch the **EU consent form** actually appear — the gateway has only ever returned `NotRequired`, so the
  branch that shows a form has never run, and a consent failure here is silent.
- **Deactivate `gg_first_bloom_pass`** in App Store Connect and Play Console. Nothing sells it and no
  receipt has ever reached `redeemPurchase`, so nobody owns one.
- Delete the ~210 synthetic saves and name reservations the live suite leaves behind.
- **Give iOS the same force-quit pass Android got** before relying on the update wall there.

**Standing Editor discipline** after any art or content drop, in this order:
`▸ Addressables ▸ Sync All Assets` **and save** → `Audit Addresses` → `Validate Content` → `Validate Art` →
the EditMode suite. Art written while the Editor was closed is unaddressed (a white rectangle, 7b) and
deleted art leaves entries that fail `BuildPlayer` rather than the game.

**Deploy discipline for any drop that touches the server**, in this order: (1) `firestore:rules` — needed
only when a *new top-level save key* or a new collection appears, checked against the released ruleset, and
deployed **before** the client. (2) Functions, **by name, in batches of three or four**, never the whole
codebase — and read the deployed artifact back. (3) `seed-config.mjs`, from a HEAD shadow tree if anything
is uncommitted. (4) The Editor's three. (5) `firebase/e2e/smoke-test.mjs`. **A deploy without a re-seed pays
the old table's figures against the new board**, which is the one failure this path has always had.

**Live but never observed.** Nothing draws an endless standing until a night has run with two hundred
watchers on it; until then the nameplate reads BEST WAVE and the profile draws no percentile, which is the
honest state rather than a fault to chase. No season rollover has ever happened.

**Decisions the owner owes** (all content, all retunable without a build).
- **Is the streak's new ladder worth what it costs the economy?** It lifts free play from 672 credits and 7
  gems a day to **936 and 12**, and because a claimed chest feeds the season, the 200-mark ladder now
  finishes around **day 27** of its 42-day window rather than day 33.
- **Is 120 gems and 7 days right for the shield?** About ten days of free play for a week of cover.
- **Is the ward shelf's ceiling reachable, and is the credit ladder the right one?** It is now the largest
  credit sink in the game — bigger than the grove's whole catalogue — and its top two bands are shut to
  every player alive. The second question is what fills the **gem** hole the all-credit shelf left.
- **Are the home ladder's gates reachable?** Keeper 10 / 20 / 40 against content paying for about keeper 9.
- **Have the charms made the mode too easy?** Three rare free payoffs moved every chapter (Thornwatch
  80 → 87 of 90 on the starter, Broodmarch 63 → 76, Barrowfell 28 → 46) with nothing else retuned. **The
  rate is at its floor and the payment does not work** (37co), so more would cost a difficulty rewrite of
  all three chapters, undoing tuning already signed off. **What must not happen is tuning until a gate goes
  green** — both shelf gates measure a *share* precisely so they keep saying the same thing while the
  baseline moves (37ch).
- **Should a raider be framed to its body?** The pack feathers its baked shadow out to alpha 1, so every
  insect is framed around a halo and drawn smaller than it could be.

**Play it.** None of the retention features or the last two chapters have been played. The questions worth
an analytics event, one per feature: how many **shields** are bought while the streak is *not* at risk; how
long a finished **task** sits unclaimed; how long a **stormglass** stands before it is matched; how often a
**siege** run ends with the line down rather than the hill cleared, and how much of a boss's health the ward
of its own colour took; how many accounts open the **Infinite hub** while it is still shut; how many people
who have run the lane ever open the **second board tab**; and the share of installs that turn **reminders**
off within a week (watch the permission grant rate first, because it is asked once). Two specific doubts:
Barrowfell reads 47 of 90 on the starter after the ending fix (37ct) and is meant to read as *hard* rather
than as a wall; and the map's nodes now sit on the painted road with no trail joining them, so look at
`s01_thornwatch` — the only chapter drawing both kinds of seat — and ask whether it still reads as a
sequence. **A render is much weaker at "is this palette any good" than at "is this widget where I think it
is".**

**Measure it, once there is live data.** The three star lines (1.20 / 1.40 / 1.60), reasoned about and never
played against. The chapter gate. The continue against the heart rescue — read the two funnels *apart*. The
restart gate's floor. The bonus wheel against its cap.

**Checks this file knows are missing.**
- **Which of a mode's kinds does the shipped chapter never send?** One loop over the enum against the
  authored waves (40a).
- **The offline mirror's wave reader treats an unknown character as a colour letter** rather than refusing
  it, so the Python gates agree with themselves while disagreeing with the shipping C# about par.
- `bestMillis` can come off the wire once no shipped client writes one (22).

## Three confirmations, and only three

`ForfeitOverlay` (a committed run being abandoned), `ReportOverlay` (an act against another person that
cannot be retracted) and `DeleteAccountOverlay` (27), which earns one more completely than either. Its
second tap is armed only when there is a grove to lose — **arming a button over an empty grove is what
teaches a player to tap through it on a full one**. `ContinueOverlay` is not a fourth: it is an offer whose
default answer is the free one. Everything else either costs nothing to undo or is confirmed by the store's
own payment sheet. **`ReportOverlay` is the chooser *and* the confirmation, and that is what keeps the count
at three** — each affirmative names its own subject and its own consequence, so picking one *is* the
confirmation. `KeeperOverlay` is not a fourth, because nothing it offers costs anything.

## Not done, deliberately

- **Play Games Services** — better Android sign-in and the natural home for leaderboards, but Android-only,
  so it cannot be the identity.
- **A visual level editor** — tooling, and the thing most likely to matter next for cadence.
- **Remote content delivery** is built and switched off. Setting `ContentConfig.RemoteBaseUrl` turns the
  heart gate, the chapter gate, the chest odds and the ad payouts into minutes-not-days levers; it is the
  highest-value unshipped setting in the build. One known gap first: `Sync Manifest` bumps a chapter's
  `version` only when its **level list** changes, so a content-only rewrite would never reach a client that
  had already cached the body.
- **A "keepers near you" board** — it needs the exact global ordering 19c refuses to keep, and the
  percentile already answers the question it would ask.
- **FCM broadcast push** — free and genuinely useful for "a new chapter is live", and deliberately not
  built, because an unused push dependency is placeholder architecture (50).

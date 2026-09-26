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
9d. **The Infinite lane is the one XP that is not a star, and it is bounded rather than
   recomputed.** A lifetime wave tally per level (`endlessBest[].waves`, save v30) pays
   `xpPerWave` up to `maxWaves`, both content. The server cannot recompute a wave, so 13's fourth
   clause is all there is: the ceiling keeps a forged tally inside an honest range, and it buys no
   **currency** — credits still derive from the star ledger alone, so a forged tally moves a keeper
   level and never a balance. **The published board still reads the *best*, and the best still pays
   nothing** (19l). `ProgressionLedger` is untouched; this is a separate addend in
   `PlayerProgression`, mirrored by `endlessXp` in `grove.ts`, and the two are held together by
   `endlessCases` in `grove-vectors.json` — **a disagreement is silent**, because 19a *drops* what
   the lower level gated. Absent config falls back to the built-in figures on **both** sides for
   that reason, rather than failing closed.
9e. **An XP boost multiplies at the moment XP is paid and banks the bonus, because there is no
   running total to scale.** Scaling the derived figure while a window is open would make a level
   *fall* when it closed. So `XpBoost.Bank` is **the only multiplier on XP in the game** — every
   source is totalled first and boosted once, which is what makes a future source work without
   being taught about boosts. Two windows, each a monotonic deadline joined by `max`: a **watched**
   one whose cooldown is *derived* from its own deadline (48c's trick, so only a watched grant may
   write it) and a **bought** one with no cooldown, where a gift lands. They **add** rather than the
   larger winning, so watching during a bought window is never a trap. **The bound is
   proportional** — the banked total is clamped on every read to `(star XP + endless XP) x
   maxPercent%`, which is `groveWorth`'s "clamped to what the account could afford" (19a) said about
   a multiplier, and far tighter than any flat ceiling. Mirrored by `xpBoostXp` in `grove.ts` and
   held by `xpBoostCases`.
9f. **The Infinite lane pays credits as well as XP, and the two are paid in opposite shapes.**
   XP is derived from the monotonic lifetime tally and needs no claim (9d); a credit cannot copy
   that, because a lifetime tally times a rate is a number a forged save mints once and keeps —
   three million at the XP ceiling, which is the colour shelf and the legendary band together, and
   exactly what 19l warns about. So credits fall to **13's fourth clause**: 30 a wave bounded by
   **10,000 a day**, which pays a cheater precisely what it pays somebody who played all evening.
   The bound is a figure about *money* rather than about waves, because a cap on waves is minted
   again by every replayed run. It is enforced **on the wallet document no client can write**
   (`endless.ts`), so it cost no schema version and no rules release; `EndlessCoins` keeps a copy
   only so the hub never offers money the server would refuse. **The claim id carries the day's
   running total** (`endless:{day}:{paidBefore}:{currency}`), so two devices banking one run are
   paid once (10a) — and the *server's* figure rides back on the wallet reply and is folded in by
   `ApplyServerState`, upward only, which is the bonus wheel's shape (`WheelStand`) and the whole
   of what makes the ceiling cross-device. Without that half, a second phone offered credits the
   day had already spent, the claim was refused, and **the client dropped the balance it had
   already shown** (45d) — money seen and taken back, which is the one thing this game may not do.

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
11d. **A screen drawn from the save repaints on `CloudSaveService.Learned`, and a run asks for a sync.**
   The first sync of a launch lands a second *after* the map is drawn, so a glade cleared on the other
   phone stood as unplayed until the app was killed (the owner's two phones, 2026-09-22). `Learned` is
   raised only when the merge changed the local file — `Synced` and every ledger's `Changed` fire on every
   foreground (44m) — and the other half is `PlayerProgress.RecordChanged` on the trigger list, because
   the background sync starts as the process is frozen and on Android may never finish.
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

### The grove — REMOVED 2026-09-21

> **Invariants 16–16x are spent.** The Grovement — the village a player built — was held on
> 2026-09-15 and removed outright on 2026-09-21: `homestead.json`, `Art/Homestead/`, the generated
> browse atlases, the `Glimmer Grove Homestead` bundle, the whole `Homestead` namespace, five
> screens, five save fields and ~165 loc keys. **The ids are not renumbered and never will be** —
> ~1,500 comments cite them, and several invariants elsewhere in this file are still argued *by
> reference to* 16a, 16e, 16g, 16j, 16l and 16s, which is why the numbers stay spent rather than
> being reused. `Assets/Game/CONTENT.md` carries what was removed and what deliberately was not.

**Four things survived the removal on purpose, and each is a rule that still binds.**

16y. **The wire spellings stay.** `groves/{uid}`, `GroveCard`, `GroveBoard`, `GroveNames`,
   `config/grove`, `ReportSubject.Grove` and the nine `grove*`/`homestead*` save keys all keep their
   names: a collection or a document field is a wire spelling and those are permanent (19o). A card
   is a *keeper* now and `config/grove` is the *turret roster*; only the names are grove-shaped.
16z. **The five save keys stay in `hasOnly`, and that is the whole reason they are mentioned at
   all.** No build writes them and `SaveFileDto` (v33) no longer declares them — but `hasOnly` is an
   allow-list over the whole document, so dropping a key a rolled-back client still writes costs that
   client **every** save write, silently (12a). An allow-list entry for a field nobody sends costs
   nothing. Their rules bounds stay for the same reason: an old client still has to pass them.
16aa. **A removal is a schema bump, for invariant 12 read backwards.** `SaveChecksum` hashes the
   serialised object, so a build whose object has *fewer* fields than every stored file fails every
   checksum at once — exactly the failure 12 warns about, arriving from the other direction.
   `SaveSchema.Version` 32 → **33** is what buys the one-time amnesty, because `Verify` trusts a file
   whose version is not this build's. Nothing is destroyed server-side: an incremental push is a
   field-masked `UpdateAsync`, which cannot delete a key it does not name.
16ab. **The companion roster is now inert rather than load-bearing.** 16a said a resident *is* a
   companion and residents are counted into `groveWorth`, which is why the companions taken off the
   front of the game on 2026-09-20 could not be deleted. That reason is gone with the grove: nothing
   counts a companion now. The ids are still in `manifest.json` and still on the wire, and deleting
   them is a separate decision with no rule holding it back.

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
19k. **A board row carries what it is *ordered* on and what it *draws*, and a badge is the
   second kind** — `rung` is on every row and orders nothing, so `sameRow` compares it or a
   keeper wears yesterday's badge until some other field moves. **A board is one published
   document ordered on one field the card already carries, and adding one is a
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
   over the same save. A seat the server cannot vouch for is **omitted rather than corrected**. A free
   turret is in nobody's `wardsOwned`, so the published roster carries `free` beside the gate. A stale seed
   publishes no line, never an unvouched one.
19q. **A row on a board leads to two places, so it opens a chooser**, holding no art of its own. **Every
   thing a profile says, it says about somebody else**: no prices, no padlocks, no taps, and **nothing
   unheld is drawn at all**.
19r. **A board is live, and the thing that makes it live is the publish itself.** `publishGrove` merges the
   card's row into each board document in the same call (`placeOnBoards`), gated by a cached cutoff so a card
   that cannot reach the top hundred costs no read; `rebuildBoards` re-reads the hundred off the index every
   fifteen minutes as the net (~200 reads a run at any population), and the counts and deciles stay nightly.
   **A withdrawal scrubs the row in the same call**, or "hide me" visibly does nothing for a quarter of an
   hour. The screen cannot say any of this by drawing itself: the panel prints the cadence
   (`LeaderboardBoard.RebuildMinutes`, mirroring the cron) and what the job that wrote *this* board recorded.
19s. **A gate that waits on something must be the thing that asks for it.** The publish path waited for the
   homestead catalog and trusted a grove screen to load it; the day the Grovement was held no device loaded it
   again, every settled sync parked its receipt, and no card was published for four days with every gate green.
   **The gate is gone with the Grovement (2026-09-21) and so is its fixture** — a publish now waits on
   nothing — so what is left here is the rule rather than the guard: the cheapest fix for a gate that waits
   on a coincidence is usually to find that it need not wait at all.
19t. **A keeper gate is asked of what is *counted* and never of what is merely *drawn*.**
   `heldCompanions` asks it (a companion fed `groveWorth`, so 19a governed it — the grove is gone and
   nothing counts a companion now, but the shape is the point); `publishedLine` may not —
   a seat feeds no score, no ordering and no currency, so re-asking it there was **15a's confiscation on a
   stranger's screen**: the legendary band is gated at keeper 45–60, nobody is there, and the dropped seat
   drew as the starter, so a real five-star Pyroclast published as `bolt` with every gate green. Nothing
   here compares a published card against the board its owner plays, and the live probe forged a tally *to
   clear the gate*. **The day a line pays anything, the gate comes back.**
19u. **A collection a client may read grants `get` and never `list`.** A `list` grant is a query
   billed one read per document in the collection, to whoever asks and as often as they ask — on
   `groves` that was one anonymous sign-in reading every published card in the game, the only bill in
   the rules that grew with the collection rather than with the players. Nothing in the client lists
   anything; the smoke test asks the four readable collections as a client and expects a refusal.

### Modes — in `Assets/Game/MODES.md`

**Invariants 20–26h, 28–36i and 37–43e live in `Assets/Game/MODES.md`**, which is what a mode is and what a
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
44k. **A screen that redraws itself empties `Content` through `View.ClearContent` and nowhere else**, because
   `Destroy` lands at the *end of the frame*: the cached `Safe` layer every control is built into still
   answers as live during the rebuild, so the page is redrawn into a node that is collected a moment later
   and goes **blank** — repaired only by leaving and coming back. Four screens wrote the loop by hand and all
   four had it; `compile.py` refuses a file that reaches into `Content`'s children.
44m. **Three ceremonies are one room, and it is measured off a picture rather than picked.**
   The rank ceremony, the turret reveal and the turret upgrade each stood their subject on a
   vertical gradient of the subject's own hue at a tenth of its value under a near-black vignette
   at seven tenths, and all three came back from the owner as **so dark** — three screens, one
   fault, and three places to fix it. `CeremonySky` is the one room: a four-corner wash
   (`Art.Corners`, generated, so it costs no art and no address), a warm corner the wash cannot
   say because a bilinear field has no glow in it, and a vignette at a **quarter** tinted with
   the wash's own deep rather than with black. The corners are a robust bilinear fit to the
   owner's reference with the badges standing on it **masked out**, which is the only way to
   measure a background through its own foreground. **What is not shared is the light on top**:
   the fans, the halo, the rim and the aurora still wear the seat's colour or the rung's metal,
   because that is the fact each of those screens exists to carry — the ground changed, never
   the lighting.
44n. **A bright ground inverts which way contrast runs, and that is the half of a re-light that
   is not a colour.** Every one of these screens drew furniture as *white at a low alpha* — an
   empty pip, a trough, a faint rule — because that is what shows on near-black; on the new room
   it is nothing at all, and the rank ceremony's rail lost its trough and every unheld pip in one
   change. `CeremonySky.Ink` is what they are drawn in now. **Text is the opposite lesson**:
   driving a caption dark to match put dark type under the dark outline every caption here
   carries and the letterforms filled in, so cream-plus-outline — which is what the screens
   already did — is the answer on both grounds. Neither fault is visible to anything but a
   render.

44l. **A mirror that cannot reach a state cannot be asked about it, and that is the state the fault is
   in.** `render_streak.py` had no "played today" flag, so it drew the night above the streak as *tonight*
   in all six states and never once drew `TOMORROW NIGHT` — the longest line either pill on the page can
   say, and the only one spilling out of its plate on a device. Add the flag before trusting the sheet.
44m. **A change event has to mean a change, or every page listening redraws itself for nothing.**
   `ReferralLedger.Adopt` raised on *every* server read, including the overwhelmingly common one that
   brought back what the device already held — so the invite page, which asks on open, threw its board
   away a round-trip after drawing it, replayed the entrance on fifty rows and lost the scroll. The
   comparison must **ignore the fetch stamp** (`ReferralState.Matches`) or it can never answer "the
   same", and it must still fire on the *first known* answer, which is a change in what the page may
   trust rather than in what it says (`SaysSomethingNew`, both clauses, both mutation-proved).
44ma. **A long list is a `GridView`, and the second screen to hand-roll one is the tell.** The invite
   board built all fifty rows as subtrees and destroyed them whenever the band above changed shape —
   the exact fault `GridView` was written for, whose own comment already said *"which is exactly the
   flicker players reported"*. Cells that fit the glass, `Refresh` for a redraw, the entrance spent
   once on `Show`. **Before writing a scrolling list, or a quiet-redraw flag, look for the one that
   exists.** It grew `ScrollTo` and `Relayout` in the doing: a caller that moves the content itself
   leaves the realised window a frame behind, which is a frame of the wrong rows.
44mb. **Only the part that can change shape is rebuilt.** The offer band is the one thing on that page
   with three shapes, so it lives in a band of its own and a change costs that band plus a slide of
   what is under it — the board is never touched, keeps its cells and keeps the player's place. A
   full restage is left for the one case that is genuinely a different page: a content push.
44mc. **A recycled cell makes invariant 48l sharper: anything a state leaves alone is the *previous
   row's* answer showing through.** Every field is written on every bind, including the reward's
   sprite and any looping tween — which is keyed on the row it was started for, so a cell rebound to
   the same lit row does not restart its breath and one rebound to a dark row stops it. And a halo
   wider than its card needs a sibling order: the lit cell sinks, or its light draws over its
   neighbours. **The mirror had that backwards for as long as it existed** (44d).
44n. **A background read must not refuse the player's own tap, and must not overwrite it either.** One
   busy flag across a referral read, redeem and claim told a player typing a code that it was
   *unavailable* — about a call never made. Reads and writes get separate gates; the ordering between
   them is a **generation stamp** taken before a call and tested after it, so a slow read cannot land
   the pre-redeem answer on top of the redeem. The stamp carries the **account** too: bumped on an
   identity change, so an answer issued as one player can never be adopted, cached or banked as
   another (invariant 17, one layer down) — and a payout records the wallet it was rolled for, so a
   ceremony that outlives a switch pays nobody rather than the wrong body (`ReferralLanding.PaysInto`).
   **Each of those is a named pure function, because a rule with no undo that lives as four operators
   inside a property is a rule no test can reach.**
44o. **A poll is a policy, so it is attached, not written** (44j's rule about readouts, said about
   cadence). A referral count is the one reading in this game no local event can announce — it is a
   fact about other people's play — so the page that shows it has to ask, or it cannot move. That is
   `ReferralWatch.Attach(this)`: one place holding the listener's lifetime, the cadence, the offline
   test, the call-already-out test and the freshness window, dying with its host. Written into a
   screen's `Update` it is five things the second screen forgets.
44p. **A listener is pointed at a document the client is allowed to read, and for this feature that
   is not the one with the answer in it.** `referrals/{uid}` names the referrer, and a code owner's
   names every invitee, so it is refused to every client on purpose — a listener there would hand a
   caller the list of people who typed their code. So the server bumps a **counter** at
   `players/{uid}/private/referral`, which is already owner-read and server-write-only (**no rules
   release**), the device watches *that*, and the answer still comes from `getReferral`. A feed that
   carries no state can leak nothing, go stale about nothing, and copies no server rule onto the
   client to disagree with later. **Pick the document by what it may expose, not by what it holds.**
44q. **A listener's lifetime is three facts and one function, and every path ends at the same
   one.** Somebody is watching, the app is in the foreground, an account is signed in —
   `ReferralFeedWatch.Settle` attaches or detaches to match, so holding, releasing, pausing,
   resuming and switching account each move one fact and ask one question. **It is a class of its
   own so the lifetime can be tested without a server**: inline it would have needed a Firestore, a
   signed-in account and a save file to run once, which is to say "the listener is torn down" would
   have stayed an assertion. Two things it must do that a first cut will not: **re-point on an
   account switch** (a stream left open reports a stranger's referrals), and **not remember a
   refused attach as success**. And the callback **may arrive on any thread** — the SDK ships as a
   DLL and does not say — so it sets a flag and the main thread turns that into the ask.

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
47n. **What is *waiting* is the claim rule asked early, never a second count over the same ladder.** The
   paid column was counted for somebody who had never bought the pass, so the hub's box lit its rim, wore a
   badge and said *collect* over a page that refused every one of them — and `Featured` would have moored
   that box on a closed season for ever. `EventLedger.Opens` is the one predicate and both readings run it;
   `Reached` is deliberately left ungated, because how far up a column a player climbed is true either way.
47o. **A debit travels with its ledger's currency, and a refused one is dropped.** The wire labelled
   every debit *credits* ("one currency spends today"), so every gem the game ever charged was taken from
   the server's credit balance and the pass — priced in gems, 47e — was refused as underpaid on every sync
   for the life of the account while the page drew it as held and every paid chest was refused. Found on
   the owner's own account on 2026-09-21. `SpendSubmission` carries the currency (a `SpendEntryDto` is a
   file record and has none); a refusal comes back on the reply and the ledger drops the entry with its
   money (13a) and says so by id, which is how `SeasonLedger` takes the pass back and leaves the paid floor.
   **The pass flag is on the wire now** (`FirestoreSaveMapper`, both ways) and `SaveDelta` compares every
   field a season row carries — one field compared made a paid claim, a mark and the pass invisible alone.

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

### Inviting friends

51. **A referral is a fact about two accounts, so the whole of it is server-owned and none of it is in the
   save.** How many strangers typed a code and how many cleared a chapter are counts of *other* people's
   play — the stored count 11b refuses, and one no merge could join. `referrals/{uid}` carries both halves
   (the code held, the code typed), `referralCodes/{code}` holds uniqueness by document id (19d), and the
   device keeps a per-account cache for drawing (8b's shape). **A referral chest is paid on request, never
   claimed**: the count it pays on lives nowhere but the server, so `claimReferral` rolls the chest,
   records `grantLog/referral:{subject}:{currency}` and moves the money in one transaction; the client
   banks what is not currency, exactly once, by `ReferralLanding`'s in-flight note. A `referral:` id at
   `claimAwards` is refused like an `ad:` id. **And a referral chest grows no season**, by the owner's
   decision on 2026-09-17: a marketing reward is not play, and a ladder climbable by posting a code is a
   ladder that pays the wrong people. Nothing calls `SeasonLedger.NoteChest` for it.
51a. **The milestone is a named chapter, judged off the save the server holds, and settled on the invitee's
   read** — never on a save trigger, which is an invocation per sync at any player count for a question
   with one answer per account. The client asks after a *settled* sync (19j) once per server revision, and
   once ever on a device that has never asked. `content.py` proves the chapter is shipped, enabled, on the
   main track and behind no keeper wall.
51b. **The payout is flat and names tiers (45), the cap is on *bound* invitees, and the share sheet never
   sees the address book.** Two royal chests to each side per finished invitee, by the owner's decision on
   2026-09-17 after a ladder was built and rejected as too small; a payment is a tier and a count, and the
   most one code can pay is the cap times the count. A cap on finished invitees is a list with no ceiling;
   a referral that reads contacts is the one shape that costs a permission and a review question on both
   stores. The share sheet costs neither, and the Android chooser needs no plugin.

### Ranks

52. **A rank is derived and stored nowhere, and that is only sound because every measure behind
   it is monotone.** A badge over a reading that could fall is a badge taken from somebody who
   did nothing wrong. Adding a measure means arguing that case before writing it.
52a. **A measure is code and a requirement is content** — `RankMeasure` says what can be read, a
   row of `progression.json` says how much of it. A whole ladder retunes without a build, and
   only a new *kind* of fact costs one.
52b. **The measure registry is the counted-verb registry.** Anything in `TaskGoals` is a measure
   by its own id with no code, so a verb added for a future mode is a rank requirement the same
   day. `stars` and `three_stars` are deliberately shadowed by the *held* reading, which is
   retroactive and cannot be inflated by replaying an easy glade.
52c. **The lifetime tally is those same counters at a window that never closes** — one hook in
   `TaskLedger.Note` feeds all three, and the reading is floored by what the rest of the save
   already proves (a cleared glade is a run played and won), so an account older than the feature
   reads correctly on its first launch instead of starting again from nought. It rides inside the
   `tasks` map, so it cost no rules release and has **no deploy ordering at all** (12a).
52d. **The held rung is the top of an unbroken run from the bottom, never the highest met** — an
   authoring slip would otherwise hand out a badge over unmet lines, and both gates still error
   on the slip rather than absorbing it.
52e. **A rank pays nothing**, because it is a reading of play that has already been paid for. The
   day a rung pays a currency, invariant 13 starts applying to it. **The ladder is published
   anyway, since 2026-09-20** — not because it pays, but because the badge went public (52h).
52f. **A rung's badge and its two strings are derived from its id**, so `artnames.py` and `loc.py`
   see none of them and `check_ranks` walks the table instead. A rung renamed moves its picture in
   the same change.
52h. **A badge on a stranger's screen is adjudicated, so the ladder is climbed twice.** A rank
   stayed derived-and-stored-nowhere when it went public; what changed is *who* derives it —
   `rungOf` recomputes the rung from the save the server already reads (19a), and the client's
   own reading exists only so that reaching a rung marks the card as owing a publish, because a
   rung reached by felling raiders moves no other field a visitor can see. The walk is written
   once per side over a **source** (`IRankSource`: ledgers, or a save file), never twice; the
   halves are held by `rankCases`; and a ladder the server cannot read is **truncated** at the
   fault rather than dropped-and-continued, which would hand out the rung above it. A rung id is
   not a spent id — nothing stores one — but an unknown one must draw as *nothing*, never as a
   rectangle (`RankArt`, invariant 7b).
52g. **Two things here are called a rank** — the percentile band a map node wears
   (`Social.RankTier`, 19c) and this ladder — so the nav bar's board tab reads **BOARDS** now and
   the fixture is `RankLadderTests` beside the older `RankTests`.
52i. **The ladder may not open before the lane it ranks does**, and the whole mechanism is one
   `keeper_level` line on the **first** rung: both readings walk up from the bottom and stop at
   the first rung they cannot meet, so one line closes every badge, board row and public profile
   with nothing new that could disagree. Cinderling was reachable at keeper 7 against an Infinite
   lane that opens at 10. The wall is in `manifest.json` and the gate in `progression.json`, and
   nothing can share one number because the server never reads a manifest — so the pair is held
   by the three gates that see both files (`RankGate` in **Authoring**, `check_ranks`,
   `seed-config.mjs`), **error below the wall and warning above it**: below is a badge for a mode
   nobody can open, above is a ladder that has quietly stopped opening *with* its lane. The wall
   is the **lowest** Infinite chapter's, read off the lane rather than authored a second time.
52j. **A rung reached is celebrated at the end of the run that earned it, win or lose — and what
   decides "reached" is a baseline, never an event.** `RankLedger.Promoted` fires lazily from
   inside whichever repaint reads `Held` first, so a ceremony hung on it plays or does not
   depending on what else happens to be on screen; `RankCeremony` differences one ordinal per
   session instead, which has no such ordering. It **re-takes** that baseline rather than
   differencing it whenever the account or the ladder has changed — a switch is local and
   reversible (17a) and a retune moves every ordinal under a player standing still (52a), so both
   would otherwise announce a rank nobody earned, full screen, with a fanfare. The price is that a
   rung reached in a session the player then kills is never shown, which is what storing nothing
   costs (52e) and is the right way round: a missing ceremony is a shrug. **`RankCeremony.Before`
   raises the run's own panel exactly once** — on close, on destroy, and on any failure to raise a
   ceremony at all — because a panel that never arrives is a player on a finished board with
   nothing to press; `compile.py` refuses a file that raises `WinOverlay` or `DefeatOverlay`
   without naming it, so a mode added later cannot quietly skip it.
52k. **The ceremony is generated from the ladder and keyed on nothing.** One mote of light per
   line of the rung, a rail of one pip per rung of the ladder, the metal off the ordinal (7c's
   shape) and the badge, the name and the blurb off the id (52f) — so a retune, a renamed rung or
   a ladder twice as long redraws with no table to keep in step. **A rung this build cannot
   resolve draws as light and no name** rather than as a white rectangle over a raw loc key
   (7b), which is reachable the day a content push adds a rung ahead of the client reading it.
   It cuts **no art**, deliberately: every sprite is procedural or already in the global preload
   set, which is the one class of fault this project has paid for most. **It claims exactly one
   address** — `Audio/Sfx/rankup`, the ceremony's single sound (52l).
52l. **The ceremony makes one sound, and it is placed by measurement rather than fired by a
   beat.** It shipped with seven and played back as a pile-up, which is the note the companion
   reveal already carries; every beat is silent now but the one. The clip is placed by the
   instant its *pitch* tops out rather than by its length or its loudest moment — a
   `SUCCESS PICKUP Retro Buildup` is not a riser that peaks at the end, it is an arpeggio
   climbing 301 Hz to 5.5 kHz over its first **0.55 s** and then decaying into a sparkle tail, so
   the badge lands on the top of the sweep and the tail rings through the climb. The strike's
   time is read off the `Cue` playhead rather than re-derived, because a rung's gathering is as
   long as it has requirements. **A skip rings it rather than killing it**: pending beats die
   with their owner, and a rank taken in silence because somebody was in a hurry is what a skip
   may not cost.

### The tutorial

53. **The tutorial is the live mode with a script beside it, and it teaches exactly two things.**
   `TutorialScreen` stands a real `SiegeView` over a real `SiegeBoard` dealt from `SiegeTutorial`
   — one wave, no boss, no cog, no charm — so nothing a first-timer learns has to be translated
   onto a different-looking board afterwards, and nothing here re-implements a rule.
53a. **It teaches the level's own two lessons rather than new ones, and that is what takes them
   off the level.** `siege_fuel` and `siege_brim` are shown here now; `TipLedger` already refuses
   a lesson twice, so `SiegeScreen` is untouched and no id was minted or spent. **Skipping marks
   both**, or a player who says they know this meets them again on the first rung.
53b. **It stores nothing new** (`TutorialGate`). The gate is "has `siege_brim` been seen, and has
   this account ever opened a level" — a union-joined set already on the wire against a derived
   reading — so it cost no schema version, no `hasOnly` line and no rules release. Reading the
   *second* lesson is what makes an interrupted tutorial replay whole instead of half.
53c. **It cannot be lost, and the guarantee is a property of the board rather than a repair
   applied to it.** `SiegeBoard.Sheltered` is read by `Bear` and `Topple` — the two doors a ward
   can lose through — so a turret under it is never hurt, rather than hurt and put back sixty
   times a second by a screen. **That is what made the three ward-damage sites one**: a blow, a
   cast and a roar each wrote the clamp, the flag and the emptied tube out in full, agreed by
   luck, and a guarantee cannot be honoured in a place that does not exist. The hill is *not*
   slowed or softened — `TutorialTests.WithoutTheGuaranteeTheSameBoardIsLost` is the differential.
53d. **The board is code and is the one place invariant 4 does not reach**, because it is not a
   chapter: no `LevelId`, no record, no manifest entry, and a script that points at particular
   cells of it. It therefore passes through **no content gate at all**, which is why
   `TutorialTests` plays the whole script end to end rather than asserting about its parts.
53e. **A tube holds fourteen gems and the second sentence a player is ever told cannot wait for
   five matches, but it must not arrive on one either.** Filling it from a single match reads as
   *instant* — an overcharge becomes something that happens to you rather than something you
   build — so a feed is worth a third of a tube and the tube climbs about three times
   (`MatchesToArm`). **That count is a target and never a contract**: the match's own fuel lands
   beside the tutorial's share, so a tube can brim a feed early, and the loop therefore ends on
   `Charged` rather than on a count it keeps. Ended on a count, a player who brims early and then
   matches that same colour again waits for ever, because `Fed` skips a ward that has banked.
53f. **The second panel waits for a *crowd*, not for the first body**, or the mode's biggest
   payoff is spent on its smallest target — with a ceiling on the wait, because a wave somebody
   later shortens must not strand the script. Every scripted change goes through a door the board
   or the ward already owns (`SiegeBoard.Pour`/`.Kindle`/`.Sheltered`, `SiegeWard.Stoke`);
   `SiegeTutorial` writes to nothing.

### Consent

55. **The consent form comes before Apple's tracking prompt on every path, and a network call
   is bounded where a person is not.** Apple rejected 1.0.2 (5.1.1(iv), 2026-09-22) because
   one fifteen-second timeout covered "load the form, show it, wait for the answer": a reviewer
   who read for sixteen seconds had Apple's dialog land on top of the form, and after "Ask App
   Not to Track" the form was still there asking about personalised ads. `UmpConsentGateway`
   bounds the two server calls and waits for a form on screen without a clock; a form arriving
   after its load timeout is dropped, never shown late. And `AdPrivacy` asks Apple only when
   `ConsentSettled` — a launch on which the CMP failed reads the status and asks nothing, so the
   form can never come second on the next launch either. Held by `PrivacyTests`, proved by
   mutation on both halves, and compiled against the real UMP DLLs by `privacy-ump`.

### Daily challenges

56. **A daily challenge is a proven puzzle genre fused with the ward line, and it shares the world
   and nothing about being a run.** Four genres (`ChallengeGenre`: pairs, glade, merge, sokoban),
   each a Domain class behind `IChallengePuzzle`, played on one fixed line
   of four starter turrets over a turn-based hill (`ChallengeHill`): **every move that costs a turn
   feeds the turret of its colour and walks every raider one step; the solving move wins before
   the hill walks; a line with no ward standing loses.** No `LevelId`, no record, no stars, hearts,
   XP, credits, lessons or save field — `ChallengeScreen` reaches none of `RunScreen`,
   `ProtoScreen` or the reward path, by the owner's instruction that tuning a challenge must never
   move the core game. **It is its own file** (`challenges.json`, `ChallengeTable.Version`, read by
   `ChallengeRules` alone) so a retune ships nothing of `progression.json`'s.
56a. **A genre is code and a challenge names one** (20's rule): `ChallengePuzzles` is the one
   registry, a `switch` whose default refuses, and an unknown genre is refused by name at read.
   Every genre reads the same flat row; a row missing what its genre plays on is refused by the
   genre's own `Fault`, once, at read — the Editor validator, `content.py` and the fixture all run
   that same `TryBuild`.
56b. **A challenge is countable, and the count is the fixture.** A challenge board reaches no gate
   that can solve it, so `ChallengeTests` plays every shipped row with a bot against the real rules
   and prints the margin (turns walked, wards standing); a row the bot cannot win is a row nobody
   is promised. Pipes is refused unless the authored (unscrambled) layout joins every colour;
   sokoban unless every pad has its gem and one is off it. **`content.py` is deliberately shallow
   here** — a Python copy of four genres would be a second opinion about a board (5b).
56c. **The colour lock is drawn as a lane.** A ward fires only at its colour and a raider walks its
   colour's lane onto its colour's post, so which puzzle move to make next is readable off the hill.
   Fuel banks on a ward with nothing to shoot, so a burst (a flood, a cleared line) is paid in full.
56d. **Today's row is `day mod n` over the slate, stored nowhere** (45b), and the list draws the
   whole slate with today's badged — narrowing it to one is a line in `DailyChallengesScreen`, left
   for the day the four are judged.
56f. **A card is a genre and a level is the calendar's answer.** Each day every row of a genre is
   ranked by a hash of the genre, the row's id and the day, the day's sequence is the rows in rank
   order, and yesterday's opener is moved to the end of the ring (`ChallengeCalendar`). So every
   player on one day deals the same sequence, no two days open on the same level, and **adding a
   level re-deals nothing** — a new row takes its own rank, the others keep theirs. What it gives
   up is exact coverage: a level is seen in about `n ln n` days rather than exactly `n`, chosen
   over a shuffled cycle on 2026-09-23 because a cycle re-dealt the whole slate on every content
   drop. Nothing is stored. A genre with fewer rows than the largest allowance repeats a level
   within a day, and `content.py` warns.
56g. **A play is spent when it is dealt, never when it ends** (`ChallengeLedger.Begin`), or leaving
   a losing board would be a free retry for ever. A win advances to the next slot; a loss retries
   the same one; a play dealt before midnight and won after is paid against the day it was dealt
   (`ChallengePlay`). The block is its own top-level save key (`challenges`, v34), by the owner's
   instruction that a challenge shares nothing with the core game — which cost the whole of 12a,
   **and the rules release goes out before the client.**
56h. **A deal is the season pass's shape** (47e): bought with gems under a derived id the server
   prices against the published row and turns into an entitlement on the **wallet** document in the
   same transaction. **A window is exactly its `days` of the clock from the instant it was bought**
   — the save holds one instant per tier joined by `max` (48c) — and the server, holding only the
   day off the spend id, covers day keys `from .. from + days` *inclusive*, one key wider than the
   instant window could reach and never narrower, so a play on the last partial day is paid.
   **An upgrade is the difference and inherits the window**: under a running deal a bigger one
   costs `target - running` gems and ends when the running one would have, derived from the deal
   the wallet already holds and never stored (`ChallengeAllowance.Price` / `dealPrice`,
   `challengeUpgradeCases`). The id `chaltier:{tier}:{fromDay}:{boughtDay}` names the window it
   inherits and the day the gems left; a fresh purchase is accepted at full price whatever runs,
   because the device is the one refusing a pointless buy. A smaller or equal deal under a running
   one is refused; a refused debit takes the deal back (`OnSpendRejected`). The client's copy draws
   the page and gates nothing that pays.
56i. **A cleared level pays credits as a claim and XP by derivation** — 9f's sentence, said of a
   puzzle. The claim `chal:{day}:{genre}:{win}:{currency}` is re-priced from the published rate and
   **bounded by the win's ordinal against the allowance the wallet's deals give that day**, so a
   forged deal in the save buys exactly the free figure; a claim past the allowance is left
   *unconfirmed* while its day is inside the window, because a sync sends awards before debits and
   the deal may be one call behind (45d), and refused once it closes. XP is a rate over a lifetime
   tally per genre (9d's shape, `ChallengeRewardRule`), a separate addend in `PlayerProgression`,
   mirrored by `challengeXp` and held by `challengeCases`; the allowance rule is mirrored by
   `allowanceOn` and held by `challengeAllowanceCases`. **The XP boost's provable base is the sum
   of all three sources on both sides.**
56j. **What the server is told is what a claim is priced against, and nothing else**: the seeder
   publishes the genre spellings, the free allowance, the deal rows and the two rates as the
   `challenges` block of `config/progression`, read out of `challenges.json`. A board never leaves
   the device. Absent falls back to the built-in rates (the endless block's reason) and to no deals
   and no genres, which leaves every coin claim unconfirmed. **Re-seed after any change to the
   file.**
56k. **A genre's card and a deal's row name themselves from their permanent ids**
   (`challenge.genre.{spelling}.*`, `challenge.tier.{id}.name`), and a written nought in a reward
   field withdraws the payment on both sides rather than inheriting — a rate beside no ceiling is
   refused by `content.py` and the seeder, because the two halves must read a published block
   byte for byte. **A withdrawn spelling or deal id is refused by name at read** (`ChallengeGenres.Retired`,
   `ChallengeTable.RetiredTierIds`, mirrored by both gates) and goes in the spent table. **And the
   largest deal's daily maximum is gated** under `ChallengeLimits.MaxDailyCoins` (10,000, the
   Infinite lane's ceiling) by the reader, `content.py` and the seeder — the allowance bounds a
   genre, this bounds the file, so a fifth genre or a raised rate cannot quietly out-earn the
   rest of the game.
56e. **Three genres were built, played and withdrawn the same day** (2026-09-22, the owner's call
   after playing all seven): Sudoku, Minefield (minesweeper) and Stack (tetris), with the input
   kinds and the "woken raider" only they used. Their spellings are refused at read like any
   unknown genre; their ids (`d02_sudoku`, `d03_mines`, `d07_tetris`) are **not spent**, because
   nothing stores a challenge id (5f's test), and their loc keys may be re-minted. Bringing one back
   is a puzzle, a view, a registry line and a row — the shape 56a describes.

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

**Enforced by a machine** (add the id in the same change as the removal; the tool refuses it): lesson ids
in `Mechanic.Retired`, held to the live set by `TipTests.EveryMechanicIsEitherLiveOrRetired`; level block
names in `content.py`'s `RETIRED_BLOCKS`. (`Tools/grove_retired.txt` held the grove piece ids and went with
the Grovement — nothing stores one any more, so they are not spent.)

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
- **The challenge genre spellings `sudoku` `mines` `tetris` `pipes`** (`ChallengeGenres.Retired`, refused
  by name at read and by both content gates): a spelling keys a lifetime row in the save. No
  challenge **deal id** has been retired yet; `ChallengeTable.RetiredTierIds` is where the first
  one goes, because a tier id is a spend id and a wallet field.
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
  Board/ Content/ Modes/ Wards/ Utilities/ Persistence/ Progression/ Cloud/
  Localization/ Analytics/ AssetPipeline/ Store/ Ads/ Daily/ Events/ Social/
  Notifications/ Release/ Ranks/ Tasks/
Assets/Game/Scripts/Notifications/  GlimmerGrove.Notifications (Domain; the mobile-notifications binding)
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
Assets/Game/Authoring/             GlimmerGrove.Authoring    (Editor-only; Domain)
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode)
Assets/StreamingAssets/Content/    manifest.json, chapters/, progression.json, loc/
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
- **The rank badges:** `python Tools/make_rank_art.py --check` proves the seven shipped PNGs are
  what the tool cuts; `--contact` is the sheet, and it is the only thing that can answer whether
  the ladder reads as a ladder rather than as seven unrelated badges.
- **The boards:** `python Tools/render_boards.py` (`--row`, `--mixed`, `--unranked`, `--empty`,
  `--contact`). **`--row` is the one that matters** — a rank badge's whole meaning is its
  silhouette, and the question "can you tell bronze from gold in a list" is only answerable at
  1:1. `--unranked` is the state every board is in until the deploy lands, and `--empty` draws
  all five refusals against the plate they are said on.
- **Rank vectors:** `python Tools/make_rank_vectors.py --check` proves the committed `rankCases`
  block is what the tool draws. The rule runs **offline on both sides** — `RankVectorTests`
  through `TestJson` (29e) and `firebase/functions/test/grove.mjs`. It caught two real faults on
  its first runs; do not let it become Editor-only.
- **The ranks page:** `python Tools/render_ranks.py` (`--held 0`, `--held 7`, `--tall`,
  `--scroll`, `--map`, `--contact`). **`--tall` draws the whole scrolling page**, and it is the
  one that matters: every state but the first two is below the fold on a phone, so a sheet
  without it answers "do the earned rows look right" and nothing else (invariant 44l). It draws off the shipped ladder, and it **measures every caption it can say
  against the box it is drawn in** — a rank ladder is content, so a retune that lengthens a
  sentence is one push away at all times. It caught two faults nothing else could: the progress
  bar printing through the last requirement line, and an unmet line marked with a star on a page
  whose own lines say "Earn 45 stars".
- **The rank ceremony:** `python Tools/render_rank_ceremony.py` (`--rung n`, `--gather`,
  `--strike`, `--long`, `--bare`, `--captions`, `--contact`). **`--contact` is the one that
  matters**, because the three states nobody may ever see are on it: a 24-rung ladder
  (`RankLadder.MaxRungs`), the first rank — climbed from nothing, so no badge rises up the shaft
  — and a rung whose badge and strings this build has never heard of. It found two faults before
  the screen was ever drawn: the eyebrow's band was its own type's height, so Best Fit settled a
  fifth smaller than written (19n), and a **gold** rung drew its room **green**, because a hue a
  fifth of a turn from gold is green and the scheme was copied off a reveal built around a
  *primary*. **What it cannot answer** is whether the breath before the strike lands, whether the
  motes read as the things you did, or whether the rail lighting bottom-to-top reads as a climb.
- **The ceremony room:** `python Tools/render_rank_ceremony.py --sky` draws `CeremonySky`
  beside the reference it was fitted to. It is the only thing that can answer whether the fit is
  the picture the owner asked for, and it is shared by three screens, so it answers for all
  three — **which matters because the two turret ceremonies have no mirror of their own** and
  are judged on a device or not at all.
- **A Push route:** `python Tools/push_route.py --seats` derives the step-shortest route through
  every shipped sokoban row and the move on which each colour first seats — the route constant in
  `ChallengeTests` and the row's wave timing are both read off it, never typed. A minute or two a row.
- **The daily challenges:** `python Tools/render_challenges.py` (`--id`, `--contact`, `--phone`)
  draws every shipped row's screen at rest off `challenges.json` with the real sprites — the only
  thing that can see the bands: it found the mustered raiders standing under the readout row and
  the Stack strip wearing the wrong string before either reached the Editor. `--list` (with
  `--held gold --spent pairs,merge`) draws the list page — the deal band, the cards, the pills and
  the badges — and measures every caption against its box. `python Tools/verify/tests.py
  ChallengeTests` is the winnability gate (56b) and prints the margins; `ChallengeLedgerTests` is
  the foundation (rotation, allowance, deals, payout, merge); `ChallengeRewardTests` and
  `firebase/functions/test/challenges.mjs` are the two halves of the shared rules, pinned by
  `python Tools/make_challenge_vectors.py --check`. `content.py` prints the whole economy per deal.
- **The tutorial:** `python Tools/verify/tests.py TutorialTests` plays the whole script against
  the real rules — the taught swap, the pour, the overcharge, the sweep — and proves it ends with
  the line intact; its board reaches **no content gate**, so this is the only thing that would
  ever say it had stopped being a board (53d). `python Tools/render_tutorial.py` (`--opening`,
  `--charged`, `--finale`, `--contact`, `--captions`) draws the screen over `render_siege`'s own
  board and reads the field out of `SiegeTutorial.cs` rather than retyping it. **`--finale` is the
  one that matters** — it found the closing line and its key seated across the ward line, which is
  the middle of this screen. It does not draw the coaching hand, deliberately (44d).
- **The charm stones:** `python Tools/make_charm_gems.py --check` holds the twelve owner-drawn gems to
  their size, their room and **their own colour** — a stone filed under the wrong letter draws
  perfectly, validates green everywhere and pays the wrong ward, and nothing else here could see it.
  `--contact` is the sheet, with the eight stones already on the board on it for comparison.
- **The anvil throwing the hill back:** `python Tools/render_siege.py --heaved 0.45` draws the
  sixth charm's front mid-sweep and the **reversed clock** it now hangs over the hill (37ed).
  **Draw it against `--stilled` before believing either** — they are the only two things in this
  mode that cross the hill, they arrive one chapter apart, and the first cut of this one came out
  as frost. The one thing only a still can answer about the clock is whether a player can tell
  which way the hand is going **from one frame**: that is the smear and the arrow, and nothing else.
- **The hourglass stopping the hill:** `python Tools/render_siege.py --stilled 0.6` draws the
  wavefront mid-sweep and the dial it hangs over the hill (37dy, 37dz). **Note the gate gap it
  found:** `fxreels.py` ink-checks `Art/Fx` only, so every reel under `Art/Siege` - `beam`,
  `laser`, `stillwave`, `stilldial` and the whole cast - is checked for *wander* and never for
  *ink*. All four measure well above the floors today; nothing proves they will.
- **The boss fight:** there is no offline gate on how a fight *feels*, and there is one on every rule it
  keeps - `python Tools/verify/tests.py SiegeRuleTests` runs the lot and prints the boss-rung table on a
  pass. `SiegeRuleTests.Fight.cs` holds the four sentences one by one (alone on the hill, untouchable on
  the walk in, every ward at full weight the frame it plants, and a floor no blow crosses until the stand
  settles) and `EveryShippedBossRungIsAFight` plays every shipped rung at nine rhythms. **A new chapter
  costs it one line** - its name in `ShippedChapters` - and the chapter that forgot went unmeasured.
- **The hill's captions:** `python Tools/render_siege.py --captions` measures every caption a
  siege can say against the board it is drawn on, at three canvas shapes. **The only gate that can
  see a caption too wide for the screen**, because nothing clips one - and the only one holding
  the mirror's boss names to `loc/en.json` (MODES.md 37ej).
- **The turret preview panel:** `python Tools/render_ward_preview.py --ward starfall` draws
  `WardPreviewOverlay`'s firing stage at its own cell, with the barrel marked, at four beats of the
  flight. **Written because that screen had no mirror at all** and is the one a player decides on a
  turret from — every question asked of it cost a device build until it existed.
- **Legendary effect reels:** `python Tools/make_legend_fx.py --check` proves the thirty drawn reels are
  what the tool draws, `--contact` is the sheet to look at and `--report` prints what `fxreels.py` will
  measure. It needs no Editor, no GPU and no licensed pack.
- **The flame a burning raider wears:** `python Tools/make_burn_fx.py --check` proves the four looping
  reels are what the tool draws; `--report` prints `fxreels.py`'s figures **and the seam** — the step
  between consecutive frames beside the step across the wrap, which is the one reading only a loop
  needs and the only reel in this game that has one. `--strip` lays two whole cycles end to end, which
  is how you *see* a seam; `--contact` is the sheet. Then look at it on the hill
  (`render_siege.py --alight 3`) and on the panel a turret is bought from
  (`render_ward_preview.py --ward pyre --alight`) — three wrong cuts of it were caught by a picture and
  by nothing else.
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
- **An `async Task` test is awaited by the runner, since 2026-09-22.** Before that its returned
  Task was discarded, so every assertion past the first `await` — including an await of a stub,
  which completes synchronously — landed in a Task nobody read, and every async test in the
  suite was a check that could not fail offline. Found because `PrivacyTests` stayed green with
  its gate mutated to a constant. **A green offline run before that date proved nothing about an
  async test.**
- **One test, while tuning:** `python Tools/verify/tests.py SiegeRuleTests.EveryShippedBossRungIsAFight`
  (`Fixture.Method`, both substrings). The chapter sweep and the fight gate print their tables on a pass.
- **The shop badge:** `python Tools/render_shop.py --measure` reads `Hud/burst` and holds the four
  sprite facts in `ProductCardBadges` (reach, field, its centre) to the picture — the only gate that
  can see a badge sprite swapped without its constants, which is how a star was measured as a round
  seal for a fortnight. `ProductCardBadgeTests` holds the arithmetic over those constants.
- **Renders (the gate that matters for anything judged by eye):** `render_home.py`, `render_shop.py`,
  `render_tasks.py`, `render_season.py`, `render_streak.py`, `render_keeper.py`, `render_endless.py`,
  `render_loadout.py`, `render_siege.py` (`--tablet`, `--warlord`, `--volley`, `--standing`, …), all sharing
  `Tools/hudkit.py`, which mirrors `UIKit` and `Skins`. `make_season_crest.py`, `make_update_icon.py`,
  `make_notification_icons.py` and `make_chest_art.py` each have `--check` (reproducibility) and `--contact`
  (whether it reads). **`--check` proves reproducibility and says nothing about quality.** Every art tool
  passes with the licensed pack absent, because the PNGs are committed.
- **That a badge reaches a card and a board:** `node firebase/e2e/rank-badge.mjs` publishes the
  same account twice, once below the first rung and once on it, and compares the two published
  rungs. **Differential for `endless-xp.mjs`'s reason** — a `publishGrove` that has never heard
  of `rungOf`, and a server whose `config/progression` carries no `ranks` block, both answer 200
  and write a valid card with no badge on it, which on the device is indistinguishable from a
  broken screen. It reads the ladder, the chapter and the glade count off the *published* config
  so a retune cannot turn it red with a correct answer, and it asserts the **lifetime floor**,
  which is the clause most likely to be missing and least likely to be noticed. **10/10 live**
  (2026-09-20).
- **That a seat is counted on the live server:** `node firebase/e2e/ward-seats.mjs` publishes
  one account's one loadout several times over, with one seat row and with four, and compares the
  seat counts. **Differential for `endless-xp.mjs`'s reason** — a `publishGrove` that has never
  heard of this answers 200 and writes a valid card with four Eclipses on it. It asserts the
  other direction in the same run: a **bare row still means all four seats**, on either band,
  which is the clause this rule could break in silence. Written for the copy rule (42k) and
  outlived it; **21/21 live** (2026-09-21), the first run since the rewrite.
- **That a deploy really carries a fix:** `node firebase/e2e/endless-xp.mjs` publishes the same
  account twice, with and without a lifetime tally, and compares the two keeper levels against the
  **published** config. Differential on purpose — a `publishGrove` that has never heard of
  `endlessXp` answers 200 and writes a valid card, so an absolute check cannot tell the fix from a
  curve with no band between the figures.
- **Seeders:** `node firebase/seed/seed-release.mjs --check` (the update wall's only gate — nothing about a
  forced update is content); `npm --prefix firebase/functions run seed -- --check`;
  `Tools/make_name_blocklist.py --check`; `npm --prefix firebase/functions test`.
- **XP boost vectors:** `python Tools/make_xpboost_vectors.py --check`. Only the **clamp** is
  shared, because it is the only half both runtimes compute — the windows and the cooldown are
  facts about *offering* a boost, which no server does.
- **Endless XP vectors:** `python Tools/make_endless_vectors.py --check` proves the committed
  `endlessCases` block is what the tool draws. The rule itself runs **offline on both sides** —
  `EndlessRewardTests` through `TestJson` (no `Application.dataPath`, no `JsonUtility`, so
  `tests.py` actually runs it — 29e) and `firebase/functions/test/grove.mjs`. It caught three real
  divergences on its first runs; do not let it become Editor-only.
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
- **A native call on the save's load path turns every fixture that loads a save into "needs the Editor",
  and the offline runner reports that as neither red nor green.** `EndlessCoins.Forget` (PlayerPrefs) went
  into `SaveService.LoadWith` on 2026-09-20 and seven fixtures — the account switch, the deletion, the
  heart rescue, both store fixtures, the utilities — stopped running offline with nothing saying so. Found
  2026-09-22. Anything device-local a ledger keeps goes behind a store seam (`EndlessCoins.ITallyStore`),
  and a fixture that loads a save installs `EndlessCoins.MemoryStore` in its `SetUp`. **Read the
  "need the Editor" count of a run as well as the red one, and ask why it moved.**
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
- **A texture used as a `Mask` may not be compressed at any grade**, because uGUI clips a mask at an alpha
  of 0.001 and block compression does not round a transparent texel back to transparent. The publisher
  card's wordmark came back with 3,555 lit texels outside the lettering at ASTC 6x6 and still 467 at 4x4 —
  every one a pinprick of the sweep through the black sheet, reported as the mark being speckled with dots.
  The folder rule carries a file-level `Uncompressed` entry for it.

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
- **`spendLog`, `grantLog` and `receipts` are never queried and are exempt from indexing** (a `*`
  field override in `firestore.indexes.json`, accepted by Firestore and read back with
  `firebase firestore:indexes`). They are the three collections that grow for the life of every
  account, so their index bytes were the one server cost rising with player-*days*. **A query over
  any of them is a deploy of an index first.** And the budget alert is not a code artefact: neither
  gcloud account on this machine holds a role on the billing account, so it is created in the
  console by the billing owner (Billing ▸ Budgets & alerts) and nothing in the repo can prove it exists.
- **Unity IAP 5 answers a confirm through `OnPurchaseConfirmed`, and warns on every purchase if
  nobody listens.** A confirm is the store's half of a purchase and the only half that can fail after
  the grant has landed: Google holds the order open, refuses the same pack again and refunds it after
  three days. The re-delivery on the next fetch always recovered it; since 2026-09-21 `UnityIapBackend`
  also hears the failure, retries at 2, 8 and 30 seconds and confirms a re-delivered order directly.
  The order stays in `_orders` until the store says it is closed — never removed on send.
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
  migrations, monotonic merge. **Save schema v33** (v33 removed the Grovement's five fields —
  16aa). Content schema: manifest and chapter bodies **v2**; `ContentSchema.Version` stays at **3**,
  which only the retired grove body ever used.
- **Cloud** — Firebase (Firestore + Auth + Functions), anonymous by default, Apple/Google linking,
  per-account local archive for switching, debounce/backoff.
- **Progression** — derived XP, keeper levels and credits from the star ledger; high-water floors only.
  Hearts and hints are produced/spent ledgers. Chapters open on stars (21); a mode's opening levels are free
  to fail (24).
- **Retention** — tasks and the chest ladder (45), the recurring bloom season (47), the streak (48), golden
  levels, percentile standings, per-level records.
- **Ranks** (52) — a seven-rung badge ladder, **derived and stored nowhere**: Cinderling,
  Silverwatch, Goldbrand, Duskcrown, Fireheart, Frozencrest, **Gemfire**. Every rung is a set of
  thresholds over readings the save already keeps, authored in `progression.json`, so the whole
  ladder retunes without a build. It is drawn **everywhere a player used to see a companion**:
  a watched badge under the map's back key on both tracks, `RanksScreen`, the hub's top-bar seat,
  the profile's medallion, every board row and every public profile. The one thing it cost the
  save is a lifetime tally of the counted verbs (v32), riding inside the existing `tasks` map. It
  pays nothing — and since **2026-09-20 it is also published**: `rungOf` derives the rung
  server-side from the save it already reads, because a badge a stranger sees is adjudicated
  (52h, 19a).
- **The rank ceremony** (52j, 52k) — a run ending stops for the rung it just earned, win or
  lose, and the victory or defeat panel is raised by the ceremony finishing. `RankUpOverlay` is a
  climb rather than a reveal: a shaft of light falling past the frame, the badge below rising
  into it and being spent, one mote per line of the rung gathering into a core, a strike, and a
  rail at the foot lighting from the bottom to the rung reached. `RankCeremony` is the gate — one
  baseline ordinal per session, re-taken on an account switch or a ladder retune — and is proved
  offline by `RankCeremonyTests`.
- **One live mode, three hidden.** **Thornwatch**: `s01_thornwatch`, `s03_broodmarch`, `s04_barrowfell`,
  `s05_ashenhold`, `s06_thundercrag`, `s07_dustcrown`, `s08_bonereach` (ten rungs each) on the ordinary
  ladder, and `s02_endlesswatch` on an **Infinite** track beside it. The map draws no *mode* switcher and
  does draw the **track** switcher; the ordinary ladder draws a map and the Infinite lane draws a **hub**,
  opening at **keeper level 10**. Six casts and **fourteen** boss verbs, one cast per chapter by ordinal:
  insects, the blob brood, skeletons, the **rabble** — the only cast **baked from vector** — the **wild**,
  and the **court**, both cut from the top-down unit packs. **The cast table wraps at six**, so the
  seventh chapter draws the insects again (7c's bargain, MODES.md 37em) and its two bosses are cut from
  the same head-on family the first chapter's five already are. The Infinite lane draws a **medley** of
  the six chapter casts, so it costs no art of its own.
- **Fire** — the three ember turrets (`ember`, `pyre` and the legendary `pyroclast`) set what they hit
  alight, and since **2026-09-18** that is a thing you can see: a looping flame stands on the raider for
  the seconds the model authors, in the colour of the *seat* that lit it, and the burn pays twice a
  second instead of every frame (37ek). Four reels, drawn offline by `Tools/make_burn_fx.py`, scoped by
  `WardLine.Art` only for a line that actually holds an ember — so a line with none loads nothing. The
  same reel is what the loadout's preview panel draws, at the same size and on the same ground.
- **Charms** — six powers dealt onto ordinary gems, one introduced per chapter: a **prism** (joins a run
  of any colour), a **lance** (its row and column), a **stormglass** (the whole line fires at everything on
  the hill), a **furnace** (banks a charge on the turret of its colour), an **hourglass** (the hill stands
  still for three seconds) and an **anvil** (the whole hill is driven back a fifth of the slope, and
  nothing is hurt — the mode's first *defensive* payoff, 37ed). One every **112** dealt gems on a window,
  the first of a run inside **56**. Each is a gem of its own with its own reel; a lance runs the hill in
  slow motion, a stormglass stops it dead, an hourglass stops it in the *model* (37do), and an anvil moves
  it in the model too — the shove is a debt each body works off, so the drawing follows the rules for
  free (37ee). The **lance, hourglass and anvil stones are owner-drawn** (a star, an hourglass and a clock
  face, supplied in all four colours) and cut by `Tools/make_charm_gems.py`; the other three are still cut
  from the gem pack. Both charms that sweep the hill draw a **painted reel lent white** rather than a white
  reel tinted, and both hang a **clock** — the anvil's runs anti-clockwise (see `CRAFT.md`).
- **Utilities** — an account-wide action bar (39), dropped by chests and bought with gems, charged against
  the graded count so one can never buy a star.
- **The turret loadout** — **thirty** turrets on a four-band shelf, upgraded to five stars, previewed
  firing before purchase, carried in from a readout on the map. Twenty are bought **per colour** behind a
  keeper level; the ten of the **LEGENDARY** band (42g) wear no colour at all — stood on any seat and
  firing at everything on the hill. **They are bought per seat too, since 2026-09-21** (42k): four
  Eclipses on a line is four purchases and four star ladders, which is the rule the twenty under them
  have always obeyed. They are cut from a second turret pack and their thirty
  effect reels are **drawn** rather than baked (`Tools/make_legend_fx.py`).
- **Boards** — **one drawn, one still published**: the **Endless Watch**. A hundred rows, one document,
  **live** — a card's row is merged into the board by the publish that wrote it (19r) — re-read whole every
  fifteen minutes (~20,000 reads a day) and counted at 04:00 (~15,000 reads a night at ten million cards).
  Plus **two published distributions** off the same five-thousand-card sample, each refusing to answer under
  200 samples. The nine league boards are gone; **Finest grooves** went with the Grovement on 2026-09-21 —
  its board id `global` is still in `BOARD_IDS` server-side and still written, and scores nought for
  everybody now, which nothing draws.
- **A keeper seen from outside** *(profile and name reporting live)* — a read-only profile built from the
  published card: keeper level and honorific, the rank badge, the Endless Watch with a percentile, and the
  four turrets carried with their rungs.
- **Economy** — real-money shop (Unity IAP 5.4.2), gems as the soft sink, rewarded ads, refund sweeps,
  server-adjudicated grants, a gem-priced doubling continue (23c) and a bonus wheel (25).
- **The front of the game** — one bought interface kit (44), cut by `Tools/make_hud_kit_art.py`; the display
  face is Titan One shipped as **Gemfire Display** (46a). The hub's foot is one stack measured up from the
  nav bar: the turret line as a readout, the Battle key, and a painted **Daily Challenges** banner that
  opens a screen with nothing in it yet. No companion stands on the hub.
- **The tutorial** (53) — the splash lands a first-timer on `TutorialScreen` instead of the hub: a real
  Thornwatch board of one wave, the two lessons the first rung used to teach, a ringed gem with a
  coaching hand over it, a turret that fills over about three matches and asks to be tapped, and
  **The battle awaits!** over the cleared hill. A worded SKIP key in the corner leaves at any point
  between panels and closes the gate exactly as finishing does. It cannot be lost, it stores nothing
  new, and it is drawn by `render_tutorial.py` and played end to end by `TutorialTests`.
- **Reminders** — two to three local notifications a day, scheduled on the device and costing the server
  nothing at any player count (50). Ten kinds; three local slots (09:30 / 13:30 / 19:30) for a week, then
  one a day out to **twenty-one**, re-armed every time the app is backgrounded.
- **The update wall** (49) — one public document per store; a client that has been told writes it down, so a
  cold start with no signal enforces it and a force-quit is not a way out. It ships **asking for nothing on
  both stores**, which is the state a release is raised from. Proved on an Android device, including the
  force-quit-in-flight-mode pass. **iOS is armed and has never been exercised.**
- **Privacy/ads plumbing** — Google UMP consent, ATT prompt, `app-ads.txt` (filled 2026-09-25).

### Content shipped

| Chapter | Mode | Levels | Par range | Subject |
|---|---|---|---|---|
| `c01_shallows` … `c04_nightbriar` | glade *(hidden)* | 10 each | 10–70 turns | the verb, then colour and blending; the crossing; colour as the subject; the briar |
| `f01_lightfall`, `f02_glasswater`, `f03_whorlwater` | fall *(hidden)* | 10 each | 2–6 drops | the cook and the chain; the lens; the whorl |
| `p01_prismvale` | prism *(hidden)* | 2 | 3–4 swaps | drag to swap; a lantern feeds its own colour and a vein can be broken |
| `s01_thornwatch` | siege | 10 | 14–57 matches | the verb, the cog, the **prism** from rung 3, the fourth ward, a warlord on 5 and an overlord on 10 |
| `s03_broodmarch` | siege | 10 | 38–59 matches | one new rule (the **lance**), a second cast, a hill that no longer forgives rank one; a blightcaller on 5, a warbringer on 10 |
| `s04_barrowfell` | siege | 10 | 49–81 matches | the first chapter authored for a *bought* line and the one that deals all three charms: a skeleton cast, armour from rung 2, a gravemaw on 5 and a bonecaller on 10; **the first chapter whose raiders carry a surge** |
| `s05_ashenhold` | siege | 10 | 49–81 matches | the fourth chapter and the first that cost the mode **code**: the **rabble** cast, armour from rung 1, a **shackler** on 5 and an **ironclad** on 10; **two tenths of surge**; deals the **furnace** |
| `s06_thundercrag` | siege | 10 | 65–110 matches | the fifth chapter: the **wild** cast of stone golems, a yeti, a minotaur and a mud clod; a **thunderer** on 5 (drains banked charges) and a **colossus** on 10 (buries a turret for four seconds, sooner if the player digs); **three tenths of surge**; deals all five charms, the **hourglass** new |
| `s07_dustcrown` | siege | 10 | 61–101 matches | the sixth chapter: the **court** cast of three robed wizards, a hooded archer, a falcon-headed war-god and a bone knight; **four tenths of surge**, which is **+40% raider health against the first chapter**; a **gorgon** on 5 (her glare wastes what is poured into a ward) and a **sunlord** on 10 (he seals a ward — fill it or lose it, and never the last one standing); deals all six charms, the **anvil** new |
| `s08_bonereach` | siege | 10 | 69–100 matches | the seventh chapter, and the cheapest one this mode has ever shipped: **five tenths of surge**, which is **+50% raider health against the first chapter**, and *nothing else new but the two fights* — no charm (the roster clamps at six) and no cast (the table wraps to the insects). A **harrower** on 5 (it tears a rank off a ward and drops it on the hill as a cog you can pick back up) and a **hollowking** on 10 (it strikes every post that has fired nothing since its last cast and spares every post that has been working). Draws `map7`, the dead lands |
| `s02_endlesswatch` | siege *(infinite)* | 1 | 3★ at wave 30 | waves that never stop, graded on how far it got, drawing a **medley** of every cast; **both star waves are guesses until somebody plays it**; opens at keeper level 10; **a heart to enter and none to lose** (43e) |

**No level authors a difficulty number except the first glade in the game, and no chapter authors a clock.**
Par is derived; star lines are multiples of it. **Par is never monotonic within a chapter** — par is length,
not difficulty. Every siege authors `budgetFactor: -1`. Chapter art is generated and **shared by ordinal**
(7c): **seven maps** and forty skies serve every chapter of every mode — `map5` is the lava crag, `map6`
the wasteland mesas and `map7` the dead lands, cut for ordinals 5, 6 and 7, and the three sources here
that are **tiled** (two boards joined end to end). The skies wrap at forty where the maps no longer do, so
a sixth chapter draws the second block again and a seventh the third. **`map7` draws no path at all** —
it is floating plateaus joined by rope bridges, so `GROUND` is the rock itself and the eye does all the
work that a road does elsewhere (8g, `make_map_seats.NUDGE[7]`). It ships with **no** warning against
`map5`'s and `map6`'s nine each; the proof that its marker *could not* be placed is kept at
`make_map_seats.ACCEPTED_OVERLAPS` because it was sound arithmetic over a bad reading of the painting.

### The numbers

**Every figure lives in `manifest.json` or `progression.json` and both content gates
derive and print the totals — read them there, never from here.** Everything except the shop ladder is
content and retunable without an app update; **re-seed after any change**. Only the shapes that are not
obvious from the files are worth recording:

- **Daily income is two figures and quoting the first as the whole is a mistake this file made.**
  `content.py` prints **936 credits and 12 gems a day** for free play and prints the adverts on their own
  lines below it, because an advert is opt-in. Read together, a player who watches everything collects
  about **7,160 credits a day**: 936 from chests, tasks, the streak and the season, **3,600** from
  `coin_bonus` (300 x 12) and **2,628** from `win_bonus` (200 x 6 through the wheel's 219% mean). The 936
  on its own under-reads free income by nearly eight times, which is how the turret shelf came to be
  called a thousand-day sink when it is closer to a hundred and thirty.
- **Content pays its credits once.** 80 a level and 40 a star (`rewards`), so all 61 levels three-starred
  is **12,200 credits, ever** — 1.3% of the colour shelf. Every repeatable credit in this game comes from
  the adverts, the chests and the ladders, which is why the daily figure above is the one that decides
  whether a price is reachable.

- **Stars** — gold `par x 1.20`, silver `par x 1.40`, the run ends at `par x 1.60`, **except a siege, which
  authors its own per chapter** (37ca) from that chapter's own sweep.
- **Chapter gate** — **16 stars of the chapter behind it, flat** (of the 30 a ten-glade chapter pays),
  per mode; the first chapter of every mode is always open, and a chapter may also carry a
  `minKeeperLevel`. The per-level rate (2 a level) is still the shape a file falls back to and is what
  a build with no published table uses — the flat figure was added because a rate can only ask a
  ten-glade chapter for 10, 20 or 30, and **a flat figure is always cut down to the stars the chapter
  behind really pays**, so it can never be a gate no play could open.
- **Hearts** — refill cap 5, ceiling 50, 8h refill (4h boosted); a heart container raises the cap to 10, 20
  or 50 permanently. **Hints** — pool of 3, one back every 8h, spent in the glade and nowhere else.
- **Continue** — 20 gems doubling within a run, topping out at the 5,000-gem ceiling on the ninth.
  Deliberately **not seeded**. **Heart rescue** — 20 gems for +2 hearts, priced off the smallest gem pack.
- **Shop** — 16 products, $293.84 total; the only ones granting something other than currency are the three
  heart containers (18d).
- **Ads** — four placements, all opt-in, no interstitials. **Bonus wheel** — eight equal slices, mean
  218.75% of the authored amount.
- **Endless XP** — 15 XP a wave, capped at 99,990 lifetime waves (1,499,850 XP, keeper 146). Ten
  waves is one three-starred glade. A watch is bought at the gate, so **hearts pace this, not the
  ceiling** — the ceiling only ever bounds a forged save. All three gates print the figures.
- **Endless credits** (9f) — **30 a wave, capped at 10,000 a day**, which is 334 waves or about
  sixteen good runs. Against ~7,160 a day from everything else, so the lane is worth up to **174%**
  of the whole rest of the game's income — the owner's call, and the figure to revisit first if the
  economy reads wrong. A forged save is bounded to the same 10,000. `content.py` prints all of it.
- **XP boost** (9e) — **+50% for 2h, watched, once every 4h**; **+100% for 24h, 120 gems**; they add,
  capped at **+150%**. So a run paying 150 XP pays 375 with both running. The gem window is a
  `store.goods` row (`xp_boost_day`) and the watched one an ad placement (`xp_boost`), so both are
  content. **The advert's `amount` and `xpBoost.watchedHours` describe one window and both gates
  error when they drift** — the cooldown is derived by subtracting the second from the stored
  deadline, so a mismatch would promise a window nobody receives.
- **Out of reach on today's content** (which pays for about keeper 13, and about keeper 9 for the grove):
  the turret shelf's tiers two and three, and all three paid home rungs. Deliberate, and the owner's call.

### Backend

Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22 in `europe-west1`. **Eighteen
functions**: `getWallet`, `submitSpends`, `claimAwards`, `redeemPurchase`, `adReward`, `appleNotification`,
`sweepVoidedPurchases`, `publishGroveStats`, `publishGrove`, `withdrawGrove`, `publishGroveRanks`,
`publishGroveBoards` (every 15 minutes, 2026-09-20), `claimName`, `reportKeeper`, `deleteAccount`, and the three referral callables `getReferral`,
`redeemReferral`, `claimReferral` (deployed 2026-09-17). **`firebase functions:list` is the authority** — a fifteenth,
`eventPass`, was deployed and later deleted while never appearing in any list here. `firebase/README.md` is
the guide; `firebase/e2e/smoke-test.mjs` is **169/169 live** (2026-09-21, after the grove
re-seed — its five grove-catalog cases became "scores nought" and "the retired keys are still
accepted", and it gained the four list refusals of 19u),
`firebase/e2e/delete-account.mjs` **14/14**, `firebase/e2e/endless-xp.mjs` **12/12** and
`firebase/e2e/ward-seats.mjs` **16/16** as `ward-copies.mjs`, before its 2026-09-21 rewrite. Client
half is `Assets/Game/Scripts/Cloud/`, Firebase Unity SDK
13.15.0 as vendored UPM tarballs under `GooglePackages/` (gitignored — run `pwsh GooglePackages/fetch.ps1`
on a fresh clone).

## Owed

**Merge was rebuilt as a puzzle experience on 2026-09-24, at the owner's instruction ("I don't
know what I'm doing", "everything happens too sudden"), and none of it has been in the Editor.**
Three things, no id, no server, no schema. (1) **A move is drawn as a score rather than
repainted**: `MergePuzzle.LastSlides` records where every gem that moved or met went and the
rank it carried, because a settled board cannot say it (the moving-board rule in `CRAFT.md`);
`MergeView` slides travellers in a layer above the residents, pops the cell two gems met in
into the size it became, springs the dealt gem up *after* the slide with a ring off it, lights
the ladder rung a new size earns, and **flies a mote from every merge to the post of its
colour** (`PuzzleView.FlyFeed`, wired by the screen to `ChallengeHillView.PostNode`/`.Fed`),
waited on so the bolt it bought starts after it lands. (2) **A rank ladder under the plate**
(`MergeView.LadderRows`, counted into the band through `EdgeRows`/`EdgeBelow`): every size to
the target as the gem it draws, lit once made, the goal ringed in gold - the colour map and the
progress in one row, mirrored by `render_challenges.py`. (3) **Three lessons through the game's
own tip machinery** (`Mechanic.MergeSwipe` with a coaching hand along a row of the real board,
`MergeGoal` ringing the readout with the target printed as an argument, `MergeFeed` ringing the
post the first merge fed, raised between the board landing and the hill replaying): a board
declares them (`PuzzleView.Lessons`/`.LessonsAfter`) and `ScreenLessons` sequences them (6a),
which grew `OfferGesture` and a `Trace` on `ScreenLesson` for the purpose. The board is
latched while a panel is up. Offline green: `compile.py`, `ChallengeTests` 27/27 (a new one
holds the trace), `TipTests` 31/31, `TipLedgerTests`, `ChallengeLedgerTests`, `loc.py` (0
missing, six new keys), `content.py` (0 errors), `sfxnames.py`, `artnames.py`,
`render_challenges.py --id d05_merge` on both canvases. **What no gate can answer**: whether
the slide at .13 s reads as a slide rather than a blink, whether the mote to the post says
*that swipe fed that turret*, and whether three panels on a first Merge is two too many - the
owner asked for very short words, and the strings are, but the count is theirs to judge.

**A challenge ends on the run's own panels since 2026-09-24, at the owner's instruction, and
neither has been in the Editor.** `ChallengeWinOverlay` is the victory design (`VictoryFrame`,
the `Payout` chips flying XP and coins out of the crest, the boost line) over what the clear
paid and nothing about the day's allowance — no stars, route, record, rank, streak or "plays
left"; NEXT LEVEL while a play is left, the list otherwise. `ChallengeDefeatOverlay` is the
defeat design (`ModalView.MakePanel`, `DefeatPanel.Of` with nothing on offer) with the line's
title and reason and no hearts, because a challenge has none: TRY AGAIN while a play is left, the
no-plays sentence in the note's seat otherwise, and the list. Neither goes through
`RankCeremony`, because a challenge is not a run; the compile rule is about the two run panels
by name. The hand-built curtain and its five strings are gone. **Neither panel has a render
mirror** — the frame is the shared one and the chips are the victory panel's, but the stack
arithmetic here is new and only a device has seen it.

**Pipeworks was replaced by the glade on 2026-09-23, at the owner's instruction, and none of it
has been in the Editor.** The hill, the four posts, the waves and every ledger are untouched:
`GladePuzzle` stands behind the same `IChallengePuzzle` seam, wrapping the real `Puzzle` dealt by
the real `LevelGridParser` from the real grammar — arms, mixing, crossings, briars, rooted tiles
and taproots are the mode's own and `Puzzle.Alike` is still asked exactly once (5b). The fusion
is the pipes' sentence: **a critter woken in its colour fires its turret every turn it stays
awake**; a light is a lane (R, G, B, and R|G is amber) and a critter wanting any other mix is
refused at read. **`GladeView` stands the mode's own `BoardView` over it** (the tutorial's
shape, 53) — the owner's verdict on a first cut that redrew the board in the challenge's
procedural pieces was "doesn't look like my glade game mode at all". One additive hook,
`BoardView.Referee`, hands a tap to the challenge instead of applying it, and `Follow` draws
what the model did; both are null on every run, so a glade played from the map is untouched.
The win is the glade's own fanfare, and the curtain waits for `OnSolved` and skips its own
flash (`PuzzleView.CelebratesItself`). Two more of the owner's instructions the same day:
**a critter is a gem** on this screen (`BoardView.LampFace`, a second null-on-every-run hook
read once by `TileView.BuildCritter` — the gem its turret fires, dimmed asleep and lit awake),
and **the puzzle band is a full-bleed opaque ground** from the rampart to the foot of the
canvas (`ChallengeScreen.Ground`/`GroundUnder`, in `Content` under the safe layer; no inset,
no pad, no gap), so the brick backdrop ends at the turrets. **The hidden glade chapters
(`c01`–`c04`) are not touched** — they stay disabled in the manifest exactly as they were. The
spelling `pipes` is retired (spent table; refused by name on both gates and the seeder), the
row's `sources`/`sinks` are refused when written (5f), `PipesPuzzle`, `PipesView` and the
`challenge_pipes` card went with their `.meta` and Addressables row (8d), and `d08_glade` is a
7x4 with one crossing and one rooted tile. **The card is drawn by `make_challenge_art.py`**
(`DRAWN`) rather than cut from owner artwork, so it sits plainer beside the three illustrated
ones — the day `glade.png` is supplied, move the spelling into `CUTS`. `challenge_glade.png` is
**on disk and unaddressed** (`artnames.py` reads one error until `▸ Addressables ▸ Sync All
Assets` and save; until then the card draws a white square, 7b). **The re-seed is done**
(2026-09-23): all four config documents were snapshotted and diffed field by field — the only
change in any of them is `challenges.genres`, `pipes` out and `glade` in (the array is sorted),
`config/products`, `config/grove` and `config/names` byte-identical, `version` still 8. No
rules release, no function deploy, no schema version. Offline green: `compile.py`, `ChallengeTests` 26/26 (the glade won
in 26 turns at 11/12 with the wake order printed), `ChallengeLedgerTests` 21/21, `content.py`,
`loc.py`, `seed-config.mjs --check`, 70 function tests, `make_challenge_vectors.py --check`,
`make_challenge_art.py --check`, both renders. **What no gate can answer**: whether a woken
critter reads as *this one is firing*, and whether the crossing reads as a bridge at 138 units.

**The Daily Challenges list page and its deal sheet were re-cut on 2026-09-23 at the owner's
instruction, and none of it has been in the Editor.** The rule line under the title is gone
(`ui.challenges.rule` retired); the deal band is the cards' width, **orange** (`PlateOrange`),
wears the shop pack's crowned chest and larger type, and its key is the kit's green pill
(`Skins.Affirm` — the owner asked for the season screen's mint, and a tint cannot reach it on a
bought sprite, 44g); the cards are 1024 wide with the mark at 216 and every caption a size up;
and the deal sheet wears **the victory panel's frame** — the green window, the fan, the crown
and banner — at 1000 wide with one cut stone per deal rung and a price that is a figure with the
gem trailing it (`Btn.IconTrails`, the pass key's shape). That frame is `VictoryFrame` now,
shared by `WinOverlay` and `ChallengeTierOverlay` so the two cannot drift (44d, said of code);
the width is a parameter and nothing else changed on the victory panel. **The three deals are
named Amethyst, Sapphire and Emerald in `loc/en.json` and nowhere else** — their ids stay
`bronze`, `silver`, `gold`, because a tier id is a spend id and a wallet field (56k) and renaming
one is a retirement, a spent-table row, a rules-side and a re-seed for a word on a label. Four PNGs were cut by `make_challenge_art.py` from the Layer
Lab pack (`PACK_CUTS`: `challenge_chest`, `challenge_deal_1..3`, keyed on the deal's **rung**,
never its id) and, with `challenge_glade`, **are on disk and unaddressed** — `artnames.py` reads
five errors until `▸ Addressables ▸ Sync All Assets` and save (7a), and until then the band draws
no chest and each row no stone rather than a white rectangle. `VictoryFrame.cs` and the four PNGs
have no `.meta` yet. Offline green: `compile.py`, `ChallengeLedgerTests` 22/22 (a new one holds
the shipped deal count to the stones on disk and in the manifest), `loc.py` (0 missing),
`content.py` (0 errors), `make_challenge_art.py --check`, `render_challenges.py --list` and
`--deals` in both states, every caption above its floor. **What no gate can answer**: whether
the owl-faced chest reads as a chest on the band (the shop tool once declined it for that
reason, `make_shop_art.py`), and whether three stones in three colours read as a ladder.

**The challenge screen was re-cut on 2026-09-23 at the owner's instruction ("the hills are too
small"), and none of it has been in the Editor.** The board is asked first and the hill takes the
rest (`PuzzleView.BandWanted`, `ChallengeScreen.HillLeastUnits`/`HillMostUnits`): every board was
re-authored wide and short — Pairs 4x4 → **6x3** (nine pairs, a fifth wave), Pipeworks 4x5 →
**6x4** (since replaced by the glade's 7x4), Merge 4x4 → **6x3**, Push 7x8 → **10x6** with a new route and its waves retimed off the
seat order — so the hill went from 3.3 siege cells to **5.2–5.4** on a 16:9 phone, and the four
posts draw at `ChallengeHillView.PostScale` (.72) of a siege turret with the raiders unchanged.
The pipes view declares the gem and ring it hangs past its plate (`EdgeRows`), or they were drawn
into the rampart. No id moved, no loc key, no server, no schema. Offline green: `compile.py`,
`ChallengeTests` 26/26 (every row won at full line health — margins printed), `content.py`,
`loc.py`, `make_challenge_vectors.py --check`, `render_challenges.py` on both canvases. **What no
gate can answer**: whether a 98-unit Push cell swipes comfortably, and whether the taller hill
reads as a walk worth watching rather than as empty stone.

**The daily challenges' foundation landed on 2026-09-22 (56f–56k) and none of it has been in the
Editor, on a device or on the server.** Save schema **v34** (a new top-level key, `challenges`),
`ChallengeLedger`, `ChallengeCalendar`'s rotation, the deals (`ChallengeTierOverlay`), the coin
claim and the XP tally, the hub badge, `challenges.json` **v2** with the three blocks (2 free plays;
bronze 120 gems / silver 200 / gold 500 for **30 days** of 5 / 10 / 25 plays a genre, upgradable
for the difference; 40 credits and 20 XP a clear). Five new C# files have **no `.meta` yet** (`ChallengeRewards.cs`,
`ChallengeLedger.cs`, `ChallengeTierOverlay.cs`, `ChallengeLedgerTests.cs`,
`ChallengeRewardTests.cs`); Unity mints them on the next focus. Offline green: `compile.py` (all
sixteen), `ChallengeLedgerTests` 19/19, `ChallengeRewardTests` 4/4, `CloudWireTests`,
`ChallengeTests`, `content.py` (0 errors), `loc.py` (0 missing, 33 new keys), the 58 + 837 + 34
function tests, `seed-config.mjs --check`, `make_challenge_vectors.py --check`,
`render_challenges.py --list` in both states. **The server half is deployed (2026-09-23)**: `firestore.rules` released first (the new
`challenges` key in `hasOnly`, checked to be the only change in the diff), then `submitSpends`,
`claimAwards`, `publishGrove` and `publishGroveBoards` by name in one batch, with the artifact
downloaded and its `lib/` proved byte-identical to the local build; then the re-seed, with all
four config documents snapshotted and diffed — exactly one field added, `challenges`
(4 genres, 2 free plays, 3 deals, 40 / 20 / 25,000), nothing else moved in any of them;
then `smoke-test.mjs` **169/169 live**. What is left is the Editor's three and a device. **The four genre cards wear the owner's pictures** (2026-09-23): cut by
`Tools/make_challenge_art.py` from `Downloads/{pairs,pipe,merge,push}.png` into
`Art/Ui/challenge_{spelling}.png` at 384 (`--check` proves the cut, `--contact` is the sheet),
addressed off the genre spelling (`ChallengeArt.GenreMark`), listed by hand in
`AssetManifest.UiSprites` and held to the enum by `ChallengeLedgerTests.EveryGenresMarkIsPreloadedAndOnDisk`.
**They are on disk and unaddressed**: `artnames.py` reads four errors until `▸ Addressables ▸
Sync All Assets` and save (invariant 7a working, as `ic_boost_up` was) — until then every card
draws a white square where the picture goes (7b). **One
decision is the owner's**: the rates are a first guess
against the printed economy — a full free day is 320 credits, a gold day at four genres 4,000.
**What no gate can answer**: whether "Today: Pairs" reads as a level name when the only level is
named after its genre, whether a spent card reads as *tomorrow* rather than as broken, and
whether the deal sheet reads as a deal.

**The invite page grew three things on 2026-09-22 and none of them has been in the Editor.**
The code field folds every keystroke (upper case, the hyphen after the fourth symbol, nothing
else) and has a PASTE key that finds a code *inside* whatever was copied, because what a friend
copies is the whole share sentence (`ReferralCode.Present` / `.Key` / `.Extract`, all pure and
tested); a friend who typed the code and has not finished the chapter is drawn as **IN
PROGRESS** in amber on the referrer's board (`ReferralFriendStatus`, `ReferralLedger.StatusOf`);
and the Refer-a-Friend door on the profile and the shop wears the hub pack's starburst with the
count of chests either side can open (`ReferralLedger.WaitingCount`, `WaitingBadge`) — **the
badge is watched** (`ReferralWatch.Attach`, invariant 44o). **And a screen open no longer costs
a callable.** Every screen carrying a referral reading used to call `getReferral` on open — a
transaction over the whole save, the most expensive thing a standing screen does — and the
listener's first delivery asked again. Now the listener hands back the feed counter
(`players/{uid}/private/referral.rev`, `IReferralBackend.WatchReferral(Action<long>)`), the
cached answer is **stamped with the counter it was read under** (`ReferralState.FeedRev`, cache
schema 3, taken *before* the ask so a bump in between can only cost one more read), and
`ReferralLedger.NeedsAsk` skips the call when the two agree. A profile, shop or invite-page
open is one listener read and no invocation, at any player count; the feed document does not
exist for an account nothing has happened to, and nought equals nought, so the common case is
free too. Every doubt fails toward asking, and each clause is held by a mutation. No server
change: the counter was already written and already owner-readable. `WaitingBadge.cs` is the
hub's nested badge lifted out into `Presentation/App/` and is a new file, so its **`.meta` does
not exist yet** and Unity mints it on the next focus. Nothing here touches a rule, a function or
a seed. Offline green: `compile.py` (all fifteen), `ReferralTests` + `ReferralFeedWatchTests`
63/63 (thirteen new), `loc.py` (0 missing, four new keys), `content.py` (0 errors),
`render_referral.py` (the climb state now draws two in-progress rows — and it caught the first
sentence running under the pill), `render_shop.py --waiting 3`. **What is owed is an eye and a
phone**: the touch keyboard's text goes through the same validator as a desktop one and nothing
offline exercises it, so type a lower-case code with a hyphen on a device and paste the share
message from a chat app — the two things the field was rebuilt for.

**Daily Challenges opened on 2026-09-22 with seven puzzle genres; the owner played all seven the
same day and cut three (56e), so four ship: Pairs, Pipeworks (the Glade since 2026-09-23), Merge and Push.**
`Assets/Game/Scripts/Domain/Challenges/`, `Presentation/Challenges/`, `Tests/ChallengeTests.cs`,
`Tools/render_challenges.py`, `Content/challenges.json` and 20 loc keys. **It cuts no art and
claims no address**: everything it draws is the siege's own hold (`SiegeMode.ArtFor(null)`) or
procedural, so nothing owes `Sync All Assets`. It touches no server, no save, no schema and no
seed. The hub's Daily Challenges banner is live (the COMING SOON pill is gone, and
`render_home.py` with it); the door shuts itself if `challenges.json` is missing or empty.
Offline green: `compile.py` (all fifteen), `ChallengeTests` 25/25 (every row won by its bot —
margins printed), `content.py` (0 errors), `loc.py` (0 missing), `artnames.py` (0/0),
`sfxnames.py`, `render_challenges.py` at every row. **What is owed is the Editor's three, then
play** — the four are a first cut of each genre, tuned only against the bots, and every wave
table is the owner's to retune in the file (`hill`, `bolts`, `waves` are the three dials, per
row). One thing no gate can answer: whether a turn-based hill reads as pressure or as a counter.

**The rank ceremony shipped on 2026-09-21 and has never been in the Editor or on a device.**
Three new files (`Presentation/App/RankCeremony.cs`, `Presentation/Screens/RankUpOverlay.cs`,
`Tests/RankCeremonyTests.cs`) and one new tool (`Tools/render_rank_ceremony.py`), all written
with the Editor closed — so their **`.meta` files do not exist yet** and Unity mints them on the
next focus. It touches no content file but `loc/en.json` (two new keys, `ui.rankup.title` and
`ui.rankup.onward`) and **nothing on the server** — no rules release, no function deploy, no
re-seed, because a rank is derived and pays nothing (52e).

**It draws no art and ships one clip.** Every sprite is procedural (`Art`) or already in the
global preload set, so nothing it *draws* is at risk of being a white rectangle. What it does
ship is `Audio/Sfx/rankup` — the ceremony's single sound, at the owner's instruction on
2026-09-21, cut from the licensed GameBurp pack through `Tools/sfx.tsv` and `make_sfx.py` like
every other clip in the game. **Its `.meta` was minted by the tool and its Addressables entry was
written into `Glimmer Global.asset` by hand**, in GUID order, exactly as the Grovement's removal
edited that file as text — so it should already be addressed. `Audit Addresses` is the check that
matters here, and if it reports the entry missing or unresolved then `▸ Addressables ▸ Sync All
Assets` **and save** is the repair. **An unaddressed clip is a silent ceremony rather than a
white rectangle**, which is the worse failure of the two because it looks exactly like the change
not having been made.

Offline green: `compile.py` (all fifteen assemblies, and the new source rule proved by mutation),
`RankCeremonyTests` 11/11 with its three rebase guards each proved by mutation, `loc.py` (0
missing), `content.py` (0 errors, the same 19 pre-existing map warnings), `artnames.py` (0/0),
`sfxnames.py`, `rungs.py`, and `render_rank_ceremony.py` at every state.

**Two faults it already cost, both found by the render mirror and neither visible to anything
else**: the eyebrow's band was its own type's height, so Best Fit would have settled a fifth
smaller than written (19n); and a **gold** rung drew its room **green**, because the colour
scheme was copied off a reveal built around one of the line's four primaries and a hue a fifth of
a turn from gold is green. Two more were found reading the code back: the rail's fill grew from
its centre (`UIKit.Box` always pivots at centre), and the core's punch and the breath that
follows it wrote `localScale` from two different channels, so the core would never have
contracted — which is the beat the whole strike is paid for out of.

**What is owed is the Editor's three and an eye**, and the eye has four questions no render can
answer. Does the breath before the strike land, or does the ceremony read as an announcement.
Do the motes read as *the things you did* rather than as sparks — one per line of the rung, so a
rung asking eight is a swarm and one asking two is a pair. Does the rail lighting bottom-to-top
read as a **climb** rather than as a progress bar filling. And does the badge rising up the shaft
read as the rank you are leaving rather than as the wrong badge arriving first. **Then earn one
on a device**: play to a rung and check the ceremony lands before the victory panel, tap through
it mid-sequence to check the skip settles rather than snapping, and lose a run on a rung you are
about to reach — a rank counts runs played, so the defeat panel gets a ceremony too and nothing
offline has ever drawn that order.

**The Grovement was removed on 2026-09-21 and none of it has been in the Editor.** The village a
player built is gone in full — **1,096 files deleted**: 64 C# (`Domain/Homestead/`'s 23, nine
Presentation views, six screens, `KeeperOverlay`, `GroveVisitScreen`, fifteen test fixtures), 424
PNGs at **16.7 MB** (`Art/Homestead/`'s 291, `Generated/GroveThumbs/`' 129, four dead global
sprites), nine browse atlases, the `Glimmer Grove Homestead` group and its two schemas, seven
Python tools, `homestead.json`, `seed-showcase.mjs` + `showcase-villages.json`, ~165 loc keys and
five save fields. 301 Addressables entries and 68 labels came out of the settings with them, so the
**whole bundle stops being built**. **Offline green**: `compile.py` (all eight assemblies),
`content.py` (0 errors, the same 19 pre-existing map warnings), `loc.py` (0 missing),
`artnames.py` (0/0), `rungs.py`, `names.py` (57 vectors), `seed-config.mjs --check`, 72 function
tests, every render mirror.

**Four things are owed, and the first is the only one with teeth.**

1. **The Editor's standing discipline, and one step of it is not optional.**
   `▸ Addressables ▸ Sync All Assets` **and save** → `Audit Addresses` → `Validate Content` →
   `Validate Art` → EditMode. **`Audit Addresses` is the one that matters here**, because this is a
   *deletion*: 301 Addressables entries were removed by editing the group asset and the settings
   asset as text, and a dead entry fails `BuildPlayer` rather than the game. The four `.meta` files
   for the deleted `Art/Bg/grove_*` and `Art/Ui/ic_nav_grove` went with their PNGs; Unity will also
   want to drop the `Homestead`, `Grove` and `GroveThumbs` folder metas on its next focus.
2. **Save schema v33 has never been round-tripped by the real reader.** The offline suite proves
   `SaveMerge`, `SaveDelta` and `FirestoreSaveMapper` agree, and `CloudWireTests` still holds
   `firestore.rules` to the client — but the one path nothing offline exercises is **loading a real
   v32 file on a device**. `SaveChecksum.Verify` trusts a file whose version is not this build's
   (16aa), so the first load is an amnesty and the first write stamps v33. **Launch once on a
   device with a real save and check nothing else was lost.**
3. **The server side is done.** `firestore.rules` gained only comments for the removal (every
   grove key is still allow-listed and still bounded, 16z) and **was released on 2026-09-21
   anyway**, by the cost audit below, so the tree and the release agree. **The re-seed ran on
   2026-09-21 too**: `config/grove` was read back and diffed against a snapshot — the six grove
   tables and the star ladder went to empty, the 30-turret roster came back identical, and
   `config/progression`, `config/products` and `config/names` did not change by a byte. Every
   published card scores nought now, which is what nothing drawing the `global` board wants.
4. **Three more sounds have no caller**, and they are left on disk on purpose. `sfxnames.py` now warns
   about `arrive.wav`, `lift.wav` and `stow.wav` (the grove's arrival and its pick-up/put-down) beside
   the `wear.wav` companions left behind. Deleting audio with the Editor closed is how a dead
   Addressables entry fails `BuildPlayer` rather than the game — so they come off in the Editor or not
   at all. They are ~4 preloaded clips of dead weight until then.
5. **Nothing visible moved on the nav bar, and that is worth saying out loud.** `Tab.Grove` came out
   of the enum, but it had already been out of `NavBar.Order` since the hold — the bar has drawn four
   tabs since 2026-09-15 and still draws four. `render_home.py` is green and unchanged.

**What it does not touch**: no function deploy, no `firestore.rules` release, no content schema
version, no chapter, no mode, no charm, no cast, no board id and no spent id. The two grove lesson
ids are retired by name in `Mechanic.Retired` (5f) and the nine save keys stay on the wire (16z).

**The obvious next bundle win is the companion roster** — 4.4 MB and 62 files that nothing draws
and, since the grove went, nothing counts either (16ab). Deliberately not taken here.


**Two fixtures were red at HEAD on 2026-09-21 and both are green, each for a reason worth
keeping.** Found by the first full offline run since Bonereach and the rank gate landed.

* **`SiegeArtTests.EveryBossIsClassifiedByBothAimingRules`** counted *one* boss reaching the line
  without aiming at a ward, and Bonereach's hollowking made two. The audit the count was warning
  about found one clause: `SiegeBoard.Clock` exempted the rally by name, so a wane was handed the
  freshest ward by `Wanted`'s default arm — an index its landing never reads — while the rule
  beside it said `Wanted` was never asked. `SiegeTuning.CarriesAWard` is the third predicate now
  (carrying is exactly aiming or not reaching the line), the board asks it, and the fixture names
  the two exceptions rather than counting them, so a third spell of that shape fails by name.
* **`ProductCardBadgeTests.TheCaptionFitsInsideTheSealsFace`** measured the caption against a
  field the badge no longer had: every sprite fact in `ProductCardBadges` (`SealDisc`, `Face`,
  `FaceShift`, `FaceRise`) had been measured off the round `seal_gold` and the badge is the star
  `Hud/burst`. The star's points reach 1.035 of the half width, so the badge overhung every
  clearance by fourteen units and touched the row above, and the clearance gate passed on the old
  disc. All four are re-measured off the star, the caption box is re-expressed against the true
  field at the same size the owner approved, and `render_shop.py --measure` reads the sprite and
  refuses drift. `TheBadgeIsMeasuredAsTheDiscItsInkReaches` replaced the test that assumed a disc
  smaller than its texture.

**Bonereach shipped on 2026-09-20 and none of it has been in the Editor, on a device, or through
a sweep.** The seventh chapter (`s08_bonereach`, ordinal 7, manifest order 152) brings a map, two
boss verbs, two boss bodies and five drawn spell reels, and every pixel of it was written with the
Editor closed — so **every one is unaddressed until `▸ Addressables ▸ Sync All Assets` and save**,
which is a white rectangle on two boss bodies, two cast reels, five spell reels and four map
strips (invariant 7b). Then the standing discipline in full (`Audit Addresses` → `Validate
Content` → `Validate Art` → EditMode). The `.meta` files for the four new source files and the new
generator do not exist yet either; Unity mints them on the next focus.

**Three things are owed beyond the Editor's three, and the first is the one that matters.**

1. **The sweep has never been run, so two floors in its own gate are nought.**
   `SiegeRuleTests.TheSeventhChapterIsFoughtOnABoughtLine` is written and its relative rules are
   live — no rung walled at any rhythm, harder on the starter than Dustcrown, one rung of the
   shelf recovering a fifth of what the starter loses and paying a grade, `cleaver` beating
   `siphon` on a chapter built out of plate — but `BareFloor` and `BoughtFloor` are **UNSET** and
   say so in the source. **Run it once and set them off what it prints.** The same run sets
   `siege.STAR_FACTORS[7]`, which ships at **(0.42, 0.56)** as the next step on the shape the six
   chapters before it make and is a guess until the sweep is read (37cb).
   It draws a **fourth line nothing else in this file draws** — four *different* one-star turrets
   (`siphon`, `ember`, `rime`, `cleaver`), printed and deliberately not gated, because it is the
   line a real player owns and a floor on it would be tuning against four separate purchases.
2. **`EveryShippedBossRungIsAFight` has two new rungs in it** and `NoBossVerbIsSentByAnyTwoChapters`
   two new verbs; `bonereach` is in `ShippedChapters` in the change that shipped it, which is
   Dustcrown's lesson paid forward (MODES.md 37di).
3. **An eye on two drawings and one hill.** `Torn` (a badge coming off a post and falling toward
   the cog it became) and `Hollowed` (a post guttering, drawn on the struck posts only) have no
   render mirror — `render_siege.py --level s08_harrowgate,s08_hollowcrown --warlord cast` draws
   the bosses and their casts and nothing about the aftermath. And the two questions no gate can
   answer: does a harrow read as *go and get it back* rather than as a sunder, and does a wane's
   **silence** on a fed line read as the player having answered it rather than as the boss missing.

**What it does not touch**: no `firestore.rules`, no function deploy, no schema version, no charm
and no cast. **The re-seed is done** (2026-09-20) — a new chapter's ten level ids reach the
server's reward map through `seed-config.mjs` alone, so without it every glade in Bonereach would
earn nothing server-side (the `hiding-a-chapter-costs-a-seed` note, read the other way up).
`config/progression` now carries **71 levels** and was **read back**: all ten `s08_*` ids map to
`s08_bonereach`. A later retune of this chapter's star lines needs no second seed — they live in
the chapter body, which is client content, and the server is published only the level-to-chapter
map.

**The board was re-cut on 2026-09-20 and nobody has looked at any of it.** Three changes went in
together, all with the Editor closed. (1) **A dealt gem no longer lands already matched** (37el), at
`RefillSettlesPercent` **60** — chains fell from 39% of matches to about a quarter, and what is left
is the chain the player caused. (2) **The Infinite lane pays 30 credits a wave, capped at 10,000 a
day** (9f) — the server half is **deployed and re-seeded**, artifact read back, but **not one real
coin has been paid end to end**: every gate here writes the save over REST or calls the rule
directly, so what is proved is that the *server* honours a claim and never that the *game* raises
one. Play one endless run on a device and watch the balance. (3) **The chapter gate is 16 flat.**

**What is owed is an eye, and the questions no gate can answer.** Does the board still *feel* like a
match-three when a quarter of matches chain rather than two in five; the hub's **fourth line** and
its green **XP** / orange **Coins** have never been drawn, and that row cost every row on the plate
20 units of height (90 → 70, with the seat and mark down with it) because
`EndlessHubTests.TheColumnFitsTheShortestCanvasTheMapLeavesIt` refused a taller column — so look at
the hub on the squarest phone before believing the sheet.

**Three gaps in the credit drop, named rather than left to be discovered.** `EndlessCoins.Bank`
itself is untested — only the arithmetic under it is, because the tally is `PlayerPrefs` and the
offline runner cannot reach it. There are **no shared vectors** between the C# `CreditsFor` and the
TypeScript `endlessGrant`, which is the pairing invariant 9a asks for and the two are held apart by
two separate suites instead. And the lane is worth **up to 174% of the whole rest of the game's
income**, which is the owner's decision and the first figure to revisit if the economy reads wrong.

**The siege chapter gates were re-pointed on 2026-09-20 and the suite is green** (106/106).
The rule they were written against - *the line a player arrives with clears every chapter* - is
gone, at the owner's instruction: a free bolt clearing the sixth chapter at 40 of 90 was judged
wrong, and players should have to spend on the shelf. **A chapter is now measured on the line it
expects** (`LineFor`): the starter for Thornwatch, `siphon` for Broodmarch, `ember` for everything
past that - `ember` because it is keeper 6 and reachable, where `mortar` and `breaker` are not.

Measured at the shipped dial, nine rhythms, wins of 90 with three-stars in brackets:

| turret | Ch1 | Ch2 | Ch3 | Ch4 | Ch5 | Ch6 | Ch7 |
|---|---|---|---|---|---|---|---|
| bolt | 46 (1) | 26 (6) | 4 | 11 (3) | 3 | 0 | 3 (1) |
| siphon | 61 (3) | 47 (12) | 13 (7) | 16 (3) | 15 (1) | 3 | 2 (1) |
| ember | 83 (30) | 74 (38) | 52 (22) | 48 (19) | 32 (6) | 42 (4) | 39 (5) |
| cleaver | 81 (19) | 65 (21) | 35 (20) | 32 (16) | 28 (8) | 31 (6) | 32 (9) |

**Three things in that table are worth knowing before touching any of it.** **The ladder is not
meant to climb evenly** - the owner's design, stated on 2026-09-21, is difficulty in *waves*, so a
chapter may sit easier than the one before it and the hard ones land as peaks rather than as one
long ramp. On the workhorse the shipped shape is 52, 48, 32, **42**, 39: the sixth is a breath
after the fifth. **Do not 'repair' it.** Each gate is anchored on Broodmarch rather than on the
chapter before it for that reason - the sentence the ladder owes is *harder than the second*,
which survives any arrangement of peaks and still fails if a late chapter drops below the
opening two.

**Chapter seven's own reading, measured on 2026-09-21** and the first sweep it has ever had:
39 of 90 on the workhorse against Dustcrown's 42, 3 on the starter, 2 on `siphon` and 32 on
`cleaver` - so it sits in the same band as the two before it and the wave shape continues. It
three-stars 5 of 90, and **`s08_harrowgate` is one of the four boss rungs no reachable line
wins**. Its `BareFloor`/`BoughtFloor` are still the UNSET nought the chapter shipped with; the
numbers to set them from are in this table.
**Three stars has gone scarce**, 4 to 6 of 90 in the late chapters against 30 and 38 in the first
two; the owner chose on 2026-09-20 to leave the star lines tapering rather than re-derive them, so
that is a decision and not a drift - **but chapter one pays 1 three-star in 90 on the free bolt**,
which is the one figure in the table worth a second look. And **four boss rungs are never reached
even on four of the strongest turrets on the shelf** - `s04_hollowgrave`, `s06_cragheart`,
`s07_gorgongate`, `s08_harrowgate`. The fight gate counts them rather than failing on them, because
a boss nobody reaches is a fight nobody saw rather than a fight that went wrong; a fifth is a
regression. **That count was guessed at two and the gate corrected it**, which is why it counts
instead of exempting a named list: a list is written from the failures somebody happened to see,
and the gate stops at the first.

**Two fixtures moved with them.** `AnUnhurriedPlayerHoldsTheFixtureLine` plays the synthetic board
on `ember`, which is the cheapest line that still clears it; and the bonecaller's bookkeeping
fixture sweeps the nine rhythms for one on which the spell fires at all, because the rung it lives
on is won twice in nine even on the strongest line.

**And `mortar` and `breaker` are unreachable.** They are gated at keeper 16 and 26 against content
that pays for about keeper 13, so the best line anybody can actually buy is `ember`. Three reachable
rungs also read *worse* than the cheaper rung under them (beacon, rime, prism) — partly 37bw's
ordering-by-reach, partly a player model that cannot use a utility ability, and worth an eye.

**The tutorial shipped on 2026-09-20 and has never been in the Editor or on a device.** Four new
files (`SiegeTutorial`, `TutorialGate`, `TutorialScreen`, `TutorialTests`) and one new tool
(`Tools/render_tutorial.py`), all written with the Editor closed — so the **`.meta` files do not
exist yet** and Unity mints them on the next focus. **It touches no art and no address**, which
is the one piece of standing discipline it does *not* owe: everything it draws is the siege
cast, the interface kit and `Bg/plain`, all already registered — and the ground is the global
blue wall rather than a chapter's sky, so it is resident before the screen exists. It touches no content file but
`loc/en.json` (four new keys, and both siege tip bodies reworded), and **nothing on the server** —
no rules release, no function deploy, no re-seed, because the gate rides in `tipsSeen` (53b).
Offline green: `compile.py`, `TutorialTests` 9/9, the full suite at its baseline, all three
content gates, `render_tutorial.py --captions`. **What is owed is the Editor's three and an
eye** — and the eye has two questions no render can answer. Does the coaching hand over a gem
read as *slide this one* rather than as decoration; and does the beat between the overcharge
landing and the line sweeping the hill (`TutorialScreen.Applause`) leave the player in any doubt
about which of the two was theirs. **Then play it as a new install**: delete the app's data,
launch, and check the splash lands on it — `TutorialGate.Owed` is the only routing decision in
the game and nothing offline exercises it.

**The ladder was gated on the Infinite lane on 2026-09-21** (52i), at the owner's instruction
after earning Cinderling at keeper 7 against a lane that opens at 10. It is **one content line** —
a `keeper_level` target of 10 on `cinderling` — plus the three gates that hold it to
`manifest.json`'s own wall, and it costs **no C# and no TypeScript**: both walks already stop at
the first rung they cannot meet, so one line on the *first* rung closes every badge, board row and
public profile. No rules release and no function deploy, for the same reason.
**The re-seed is done** — `config/progression` v**8**, all four documents read back and diffed
against a snapshot taken before it: the ranks array shifted by one and `version` moved, **nothing
else changed in any of the four**, and `config/products`, `config/grove` and `config/names` came
back field-for-field identical. Live green after it: `rank-badge.mjs` **11/11**,
`smoke-test.mjs` **172/172**, `ward-copies.mjs` 19/19 (now `ward-seats.mjs`), `endless-xp.mjs` 12/12,
`delete-account.mjs` 14/14.
**No live badge fell, and that is now a reading rather than an argument** — all 19 cards were
walked, and the three carrying one stand at keeper 12, 14 and 16 (`cinderling`, `silverwatch`,
`silverwatch`), every one of them above the new line.
**What the probe cost is worth knowing**: a save meeting a `keeper_level` line has to clear the
whole catalog, because working out the minimum would mean a third copy of the reward rules and
the level curve in an e2e file — so `rank-badge.mjs` now asserts **a rung of the published
ladder** rather than `first.id` exactly (it reaches `silverwatch`), and checks the *card's own
`level`* against the line so the shortcut cannot hide a target the catalog can no longer pay.
**The rule lives in `GlimmerGrove.Authoring` and is tested.** Its first cut was written inside
`ContentValidation`, where the suite cannot reach it — so it would have shipped compiled and never
once executed, which is a gate that cannot fail. `RankGate` is the rule, `ValidateRankGate` is the
build gate asking it, and **`RankGateTests` drives every branch** (11, including that the gate
alone shuts a ladder whose every other line is met many times over, asserted against
`RankLadder.Held` rather than argued). Proved by mutation: breaking the comparison turns it red.
**The e2e probe climbs the published ladder** rather than naming a rung. A save meeting a
`keeper_level` line has to clear the whole catalog, which ordinarily reaches the *second* rung —
so `rank-badge.mjs` now predicts the rung from the published ladder and the save it wrote and
holds the server to it exactly, which is sharper than the `=== first.id` it replaced. It falls
back to recognising a rung, and says so out loud, only if a future ladder asks a shape it cannot
climb.
**What is still owed is the client.** Its half is bundled content, so it reaches nobody until
**the next build** ships; until then a keeper below 10 sees a badge on their own map that no
stranger sees, which is cosmetic only (a rank pays nothing, 52e). Two new C# files
(`Authoring/RankGate.cs`, `Tests/RankGateTests.cs`) were written with the Editor closed, so their
**`.meta` files do not exist yet** and Unity mints them on the next focus; nothing else about this
touches art, an address or the server.
Offline green: `compile.py` (Domain still builds without `Authoring`, so nothing shipped reaches
the rule), the EditMode suite, `content.py` and `seed-config.mjs --check` — both proved to go red
below the wall and to warn above it — `loc.py`, 72 function tests, `render_ranks.py` at every
state, the first rung drawing three lines and still fitting.

**The badge went public on 2026-09-20 and the server half is live.** A rank used to be a
private reading; it is on every board row and every public profile now, so it is adjudicated
(52h). `rungOf` climbs the ladder over the save `publishGrove` already reads, `buildCard` writes
`rung`, and `rowOf`/`topOf`/`readRows` carry it onto a board with `sameRow` comparing it.
**Deployed and proved**: `publishGrove` **and `publishGroveBoards`**, by name (the CLI's
analysis step times out at 10s on a first attempt and succeeds on a retry — the bundle itself
loads in 444ms, so that is the tool, not the code), artifact read back for
`rungOf`/`readLadder`/`lifetimeRows`; `config/progression` re-seeded and **diffed against a byte
snapshot — exactly one field added, `ranks`, nothing removed and nothing changed**. No
`firestore.rules` release: the rung is server-written on a server-written document and rides in
no save key. Live green afterwards: `smoke-test.mjs` **172/172**, `ward-copies.mjs` 19/19,
`endless-xp.mjs` 12/12, `delete-account.mjs` 14/14, and `rank-badge.mjs` **10/10**.

**Both functions, and the second one is the lesson.** `rowOf` and `topOf` are the row
projections and they live in `grove.ts` beside `buildCard` — so deploying `publishGrove` alone
looks complete and is not: the *rebuild* runs inside `publishGroveBoards`, its own deployed
function with its own bundle, which went on copying rows with no `rung` on them. The cards were
right, the board was bare, and nothing anywhere said so. **A field added to a row shape is a
deploy of every function that writes a row**, which is `publishGrove` (live placement) and
`publishGroveBoards` (the fifteen-minute net).

**What a green deploy still does not buy is a badge on a screen, and three separate things
decide that.** (1) **A card only gains a rung when its owner next publishes** — nothing
backfills, because the rung is derived at publish time from that save. (2) **A card is only
published at all when `GrovePublishPolicy.WorthPublishing` says so**: a best wave > 0, and since
2026-09-21 that is the whole test — the grove-worth clause went with the Grovement, so **the
Infinite lane is the only route onto any board**, and an account that has never run it has no card at all —
which is the state the owner's own `Tekoworld` account is in (99 glades played, 0 endless rows,
no card). (3) **The first rung is demanding against the live population**: run read-only over the
nine real saves on 2026-09-20, only three would publish anything — `cinderling` for one,
`silverwatch` for two, nothing for the other six. All three are content and retunable without a
build; the second is the one worth thinking about.

**The nine real cards were repaired by hand on 2026-09-20** rather than waiting for their owners
to sync, the value in each case being exactly what the deployed `rungOf` computes from the same
save and the same published config (`updateMask.fieldPaths=rung`, so nothing else on the
document was touched, and the owner's next real publish overwrites it). Three earned one —
`silverwatch`, `silverwatch`, `cinderling` — and six earned nothing, which is the ladder saying
what it means rather than a fault. The Endless Watch board now draws three badges and two honest
blanks.

**Companions are off the front of the game as of 2026-09-20, and deliberately not deleted.** The
hub's top-bar seat, the profile's medallion, every board row and every public profile wear a rank
badge now; the profile's companions card and the public profile's are gone; `CompanionScreen`,
`UnlockGoal` and the hub's companion goal box are deleted, and the splash no longer pins a
portrait into the global set for the life of the process. **What still stands is the roster
itself** — `AvatarCatalog`, `CompanionLedger`, `CompanionArt`, `CompanionUnlockOverlay`,
`CompanionRevealOverlay`, the `companions` block in `manifest.json`, `Art/Companions/` (4.4 MB, 62
files) and the whole server half — untouched and unreachable.

**What held it there was invariant 16a, and that reason expired on 2026-09-21.** A resident *was* a
companion, residents were counted into `groveWorth`, and `groveWorth` is a published
server-adjudicated number, so deleting the roster would have moved every card's score and reordered
a live board. The grove is gone; nothing counts a companion now, and nothing draws one. **Deleting
the roster is a separate decision with no rule left holding it back** — the ids are still on the
wire in `companionsOwned` and in `manifest.json`, so it would cost a manifest change and the same
retire-in-place treatment the grove's save keys got (16z). It is the obvious next bundle win and is
deliberately not taken here. **Two things owed**: `wear.wav` now has no caller
(`sfxnames.py` warns; deleting audio without the Editor is how a dead Addressables entry fails
`BuildPlayer`), and the retired `ui.profile.*` companion strings are left where they are, which
is deliberate — a loc key names a sentence and may be re-minted (5f).

**The boards screen grew a render mirror it never had.** `Tools/render_boards.py`, and the row
went 132 units tall to 176 with the badge at 132 — a portrait is a face and reads at any size, a
rank badge is a silhouette and at 84 the seven of them are one smudge. **What no mirror can
answer** is whether a hundred of these scroll pleasantly, and whether a row with no badge reads
as unranked rather than as unfinished — which is every row until the re-seed lands.

**The rank ladder shipped on 2026-09-20.** Seven rungs (52), every one derived and stored
nowhere, drawn as a badge under the map's back key and as `RanksScreen`.

**The badges are addressed.** All seven landed in `Glimmer Global` through the importer hook on
the first import — invariant 7a working rather than a fault — so `Sync All Assets` is *not*
owed and `artnames.py` reads 7,490 registered. What is still owed is the rest of the standing
discipline (`Audit Addresses` → `Validate Content` → `Validate Art` → EditMode).

**`Validate Content` came back with 53 errors on its first run and every one was the validator's,
not the content's.** Two faults, both fixed, and both worth knowing about because they will bite
the next Editor-side rule that is written:

* **`Loc.Has` is false for every key in the game inside the Editor.** Nothing loads the runtime
  localisation table there, so a validator built on it does not under-report — it reports the
  whole file missing. Every other derived-key check in `ContentValidation` parses its own
  `LocTable`, and `ValidateRanks` now does the same through `LocalisationTable()`.
* **`ProgressionRules.Table` is the built-in default inside the Editor, not the shipped file.**
  `ValidateProgression` reads its own table and never publishes it, so anything reaching for the
  published one sees `ProgressionTable.Default`. Chest tiers survive this by coincidence — the
  default carries the same four ids — and an empty rank ladder did not. The "is this badge
  preloaded" question moved to `RankLadderTests.EveryRungsBadgeIsInTheGlobalPreloadSet`, which
  publishes a table deliberately. **The coincidence is still there for the tiers**; nothing has
  been done about it.

**What is owed after that is an eye.** `render_ranks.py` answers the layout and measures every
caption; what it cannot answer is whether the ladder reads as *worth climbing*, and whether the
one row that shines reads as the row to chase rather than as the row that is broken.

**The ladder was made harder on 2026-09-20, at the owner's instruction, and none of it is
measured.** Three figures are guesses and all of them are content. `best_wave` at 20 / 30 / 45 /
60 stands against star lines that are themselves guesses until somebody plays the Infinite lane.
`runs` at 15 / 50 / 200 / 450 paces the middle. And **the three un-floored lines are the ones to
watch**: `raiders` 2,500, `bosses` 30 and 60, `charms` 300 have no derived floor under them
(52c), so they count from the day the build ships and nothing anybody has already done counts
toward them — which is the whole reason the backbone of the ladder is stars, clears and waves,
which are retroactive. Rank 7 asks 175 of the 183 stars that ship and 55 of 61 three-stars; both
gates refuse anything past those, so **the top of this ladder cannot be raised again without more
content**.

**One rules clause was written ahead of its release, and released on 2026-09-21** with the cost
audit's ruleset. `firestore.rules` bounds `tasks.lifetime` at 32 rows against
`LifetimeTally.MaxGoals`. **It is the one field here with no deploy ordering**: the tally rides
inside the `tasks` map, whose sub-keys `hasOnly` does not allow-list, so the ruleset deployed
before it accepted the field and this one merely bounds it (12a). Nothing else
about ranks touches the server — no function deploy, no re-seed — because a rank is derived
from records the server already validates and pays nothing (52e).

**The nav bar's board tab now reads BOARDS.** One string (`ui.nav.ranks`), no id and no code:
two things in this game were called RANKS and one of them had to stop (52g).


**The boards went live on 2026-09-20, and the client half needs a build.** No real device had called
`publishGrove` since the Grovement hold (19s): the publish gate waited on the homestead catalog and the
three screens that loaded it had left the nav. The server half is deployed (`publishGrove`,
`withdrawGrove`, `publishGroveRanks`, `deleteAccount` and the new `publishGroveBoards`, smoke test
170/170, endless-xp 12/12, delete-account 14/14) and the five stale real cards were republished by hand
through the ordinary callable. **Until a build carrying the `GroveBoard.Consider` fix ships, a new best on
a device still reaches no card** — the save syncs, the receipt parks, and the board holds the last
republish. **Removing the Grovement on 2026-09-21 deleted that gate outright** rather than repairing it,
so the same build now carries both fixes. The owner's `Tekoworld` account has an empty `endlessBest` on
the server: it has never synced a wave, so the runs it is expected to show are not on it.

**The referral drop went live on 2026-09-17**: rules released, the three callables and `deleteAccount`
deployed by name with invoker bindings, re-seeded, smoke test 166/166 and delete-account 14/14 live,
`ReferralTests` green in the Editor. What has never run: the share sheet on a device, on either platform
— `GlimmerShare.mm` has never been compiled by Xcode — and a real invitee typing a real code.

**`ic_boost_up` is on disk and unaddressed**, and is the only red name left: `artnames.py`
refuses it until `Addressables > Sync All Assets` **and save** (invariant 7a working rather than a
fault). The five cut before it — `ad_coin`, `ad_heart`, `ad_xp`, `ic_utilities`, `ic_xp_boost` —
were synced on 2026-09-17 and are green. All five come out of `Tools/make_ad_art.py` from owner-supplied artwork,
each with its own long edge, because a tab glyph (~86 drawn), a good's card icon (~222) and a full
card illustration (~366) are three different sizes and one constant made two of them wrong. The
adverts carry their own play button, so `ShopArt.PaintAd` draws nothing over one and composes the
old heap only for a placement with no picture (the hint refill).

**The map carries the boost's clock.** `BoostReadout` sits under the back key on `LevelsScreen`,
which draws the chapter map *and* the Infinite hub with one set of chrome — so one attachment
covers both tracks. It is **watched and ticked**, not drawn (invariant 44j): a window can open
while the map is standing, and the number moves whether anything happens or not. Two subtleties
worth keeping: the clock reads the **later** of the two deadlines rather than the next change, or
it would vanish while a boost was still running; and the **tick** is what takes it off screen,
because a window closing is not an event — nothing happened, time merely passed. Its mark,
`ic_boost_up`, is the update wall's own arrow turned over and **hue-rotated** to green by
`Tools/make_boost_icon.py` — never tinted, because a multiply takes amber to brown (44g) — and it
is the same drawing on purpose: gold down says *download*, green up says *this is lifting* (49h).

**The shop's shelves moved with them.** `KIT` is now **UTILITIES** and leads with the two XP boost
cards — the free watch and the 120-gem day — with the four consumables under them; hearts and
heart boosts stayed on `SUPPLIES`. Which shelf a good sits on is `StoreGoodKinds.ShelfFor`, asked
by the shelf, by the card's accent colour and by `render_shop.py`, and **a video stands where the
thing it pays for is sold** (`ShopAdShelf.All`, which is a list because a shelf may stand more than
one). That also retired the "two free cards fill the first row of Supplies" note: Supplies is back
to one. `render_shop.py` grew a **utilities shelf**, which it never had — that tab was invisible to
the render until the order on it became a decision worth looking at.

**The XP boost is built and has never been played, and none of its server half is deployed.** Two
windows (9e, save v31): watch for +50%/2h every 4h, or 120 gems for +100%/24h. Everything offline is
green — 2,187 tests, the shared clamp on both sides, all three content gates, `render_shop.py`.
**The server half landed on 2026-09-18** alongside the copy drop: `config/progression` re-seeded to
v7 carrying `xpBoost.maxPercent 150` and the `xp_boost` advert (read back and diffed — those two
fields and Dustcrown's ten level ids were the whole change, plus the version), and `publishGrove`
deployed by name with `xpBoostXp` proved present in the artifact. No `firestore.rules` release — all
three fields ride inside the existing `wallet` map. The **good** needs no seed: `xp_boost_day` is a
`store.goods` row spent through `submitSpends`, and the server is published only the clamp, because
the windows and the cooldown are facts about *offering* a boost (9e). What is left is to **play one
boosted run** and check the victory panel's new line against the chip above it.

**Endless XP went live on 2026-09-17, and no real run has banked a wave.** The lane pays 15 XP a
wave (9d, save v30). **The server half is done**: `config/progression` re-seeded to v6 carrying the
`endless` block (read back and diffed against the snapshot — no field lost, only `version` and
`endless` moved), `publishGrove` deployed by name, artifact proved by
`firebase/e2e/endless-xp.mjs` **12/12 live**, then `smoke-test.mjs` 166/166 and
`delete-account.mjs` 14/14, with `functions:list` still naming all seventeen. No
`firestore.rules` release — the field rides inside `endlessBest`.

**Played on 2026-09-17: five waves for 75 XP, on a run below the player's own record.** That is
the link no gate reaches — the live probe writes the save row over REST, so it proves the *server*
reads a tally and never that the *game* writes one. It is also the strongest of the two paths to
have landed on by accident: a run under the best is where `Record` refuses and `Bank` pays anyway,
which is the case that fails outright if the two are ever fused back together
(`EndlessRewardTests.ARunThatBeatNothingStillPays`).

**Still unobserved:** a run that *sets* a new best. That is the other half of the same seam — the
one that double-counted before the fixture caught it, because `Record` raises the best and the
floor under the tally rises with it — so it is pinned by tests and by nothing a person has seen.
Beat the record once and check the panel still says waves x 15 exactly. And nothing has tested 15
a wave as a *feeling*; the pace was reasoned against a table and approved on figures alone.

**A real receipt has reached `redeemPurchase`; a real impression has not reached `adReward`.** The first
production App Store purchase (`gg_gems_1`, `sandbox: false`) was verified and granted on 2026-09-25,
launch day, and sits in `receipts` - so the real-money path is proved end to end on iOS. `adReward` is
still fully built and deployed and has never run once with a live impression; watch one rewarded video
on the store build and look for the grant.

**The ember re-cut of 2026-09-18 is unaddressed and has never been played.** Four new reels
(`burn_r/g/b/y`, twenty-four frames each) went in with the Editor closed, so every one of them is
**unaddressed** until `▸ Addressables ▸ Sync All Assets` **and save** — which is a white rectangle as
wide as a raider on every burning body (invariant 7b). Then the standing discipline (`Audit Addresses`
→ `Validate Content` → `Validate Art` → EditMode). **Nothing about it touches content or the server**,
and no `progression.json` figure moved: what changed is the *type* of the record a burn tick is
reported as and the cadence it is paid on, and both content gates already refuse a roster that holds
an ember with no flame on disk. **What is owed is an eye and a run**: stand `pyre` on a seat and watch
a wave burn, because the whole of what changed is how it reads — and check the one thing no gate here
can, that a burning raider's *body* is still legible under its own fire (`SiegeView.BlazeWide` is the
dial and the direction is down).

**The charm re-cut of 2026-09-18 is unaddressed and has never been played.** Three things went in
together, all with the Editor closed. (1) **The lance, hourglass and anvil stones are owner-drawn
art now** — twelve PNGs cut by `Tools/make_charm_gems.py`, written over the same twelve addresses,
so they cost **no Addressables work at all** and are the one part of this that is already live in a
build. (2) **Both sweeping fronts and both dials are painted reels** rather than white reels tinted,
and `stillwave` and `heavefront` went from twelve frames to **twenty-four** — so frames `f12`–`f23`
of each are on disk carrying **neither a `.meta` nor the reel's label**, which is a reel that loads
its first twelve frames and stops. (3) **`heavedial` is a new reel** — the anvil's clock, running
anti-clockwise — and is wholly unaddressed, which is a white rectangle four cells wide over the hill
(invariant 7b). So: `▸ Addressables ▸ Sync All Assets` **and save** → `Audit Addresses` →
`Validate Art` → EditMode, and then **play an anvil and an hourglass**, because the whole of what
changed is how long they last and what colour they are, and no gate here can see either. Nothing
about it touches content or the server.

**The hill's captions were sorted out on 2026-09-18 and want an eye, not a gate.** Every text on a
siege board is now fitted to the board (MODES.md 37ej) and no two that can be up together share a row.
Three things changed that a picture can only half answer. **A long name is smaller**: "THE BLIGHTCALLER
FALLS" was drawn a cell wider than the screen and now settles at .48 of a cell against .70 - legible,
and the smallest type the hill says. **A long name opens flatter**: the pop is what gives, so a
shackler still punches at 1.6 and a blightcaller at 1.15. **And the first wave is not announced at
all**, because GO! lands on the frame it steps out and the two were drawn through each other on every
run ever opened - so what to watch for is whether the opening now reads as *quiet* rather than as
clean. `render_siege.py --captions` measures the widths; nothing can measure those three.

**The boss fight was rebuilt on 2026-09-18, and it has never been played.** The guard in front of
every stand is gone: a boss is untouchable on the walk in and **hurtable by the whole line from the
frame it plants**, and what holds it up is a *floor under its health* instead (MODES.md 37ei). Four
faults the owner reported went with it, and all four were one rule: turrets that would not fire at a
boss for three to four seconds at every phase; a spinning **circle** drawn round the boss to explain
that window (`Art.Ring`, withdrawn from the board and from `render_siege.py` together); an overcharge
that pulsed and then swallowed the tap for exactly those seconds (`SiegeBoard.CanOvercharge` is one
reading now, asked by the drawing and by the throw); and a wave that could walk on over a living boss
on the Infinite lane (`SiegeBoard.BossStanding` holds every wave behind a boss, both lanes). **It is
difficulty-neutral and that was measured, not argued**: eleven of the twelve shipped boss rungs read
the same or better, and Dustcrown's sweep moved 40 → 39, 59 → 60 and 83 → 83 of 90. The one rung it
moved is `s07_crownfall`, which was passing its own gate at **one rhythm of nine** and is a wall on a
starter line either way — its cog rate went 25 → **40** (the point the lever saturates; par and both
star lines do not move by one) and it now reads 2 of 9 bare, the same band as the chapter before's
finale. **What is owed is a device**: the offline gates cannot see whether a boss fight now *feels*
like a fight, and the two things to watch are the bar resting on its notch while the boss casts (which
is where the seconds are paid from now) and whether the overcharge button ever goes dark at a moment
that reads as a bug rather than as the walk in.

**Dustcrown shipped on 2026-09-17 and nothing of it has been in the Editor.** The sixth chapter
(`s07_dustcrown`, ordinal 6, manifest order 151) brings a cast, two bosses, a charm and a map, and
every pixel of it was written with the Editor closed - so **every one is unaddressed until
`▸ Addressables ▸ Sync All Assets` and save**, which is a white rectangle on twenty-four raider
reels, two boss bodies, six spell reels, four gem faces, a front and four map strips (invariant
7b). Then the standing discipline in full (`Audit Addresses` → `Validate Content` → `Validate Art`
→ EditMode). **The re-seed is done** — `config/progression` v7 (2026-09-18) carries all ten
`s07_*` level ids against `s07_dustcrown`, read back and diffed, so every glade in it earns
server-side now. Nothing else about the drop touches the server - no `firestore.rules`, no function
deploy.

**Thundercrag's two spell reels are an Editor bake, and they are not on disk.** `Glimmer Grove ▸ Art ▸
Bake Thundercrag Spells` writes `levin*` and `boulder*` under `Art/Fx/Siege` (six names `artnames.py` is
red on until then); look at both on the **Siege Projectile Contact Sheet** before shipping - a batch-mode
bake on a project copy rendered every frame shader-pink twice, so only the running Editor can do it.
Then the standing discipline (`Sync All Assets` → `Audit Addresses` → `Validate Content` → `Validate
Art` → EditMode), and **re-seed**: a new chapter's ten level ids reach the server's reward map only
through `seed-config.mjs`. Nothing else about the drop touches the server.

**Every bolt in the siege now leaves its barrel rather than through the chassis** (37ds–37dv) — a comet
is anchored near its head, so drawn at full length on the frame it was fired its tail was painted back
down through the turret. The trail is **cropped** to however far the shot has flown, never scaled
(37du, which cost a round: a squash mangles anything drawn off the centre-line). It is the board, the
loadout's preview stage and `render_siege.py`, so it wants an eye on a device — **and the one thing to
look for is the crop's straight edge at the barrel**, which is covered by the muzzle flash and by the
trail's own fade. If it ever shows, `SiegeView.HeadRoom` is the dial and the direction is down.

**A legendary is bought per seat as of 2026-09-21, and the copy rule is retired.** The band was
bought outright, so one payment stood four Eclipses (42k); the copy row that answered that on
2026-09-18 was right about the money and wrong about everything else — it hid the second purchase
behind a key on a panel instead of a price on a card, and it left **one star ladder under four
turrets**. The owner reported all three as one complaint. Buying per seat is the bound the shelf
already had, so this **deletes** rather than repairs: `WardLedger.Copies`, `OfferAnother`,
`TryBuyAnother`, `WardLine.Resolve`'s cap, `WardPreviewKeys.Buys`, the shelf's corner chip, the
server's `copiesOf` and two loc keys all go, and the per-seat star ladder falls out of
`WardHolding.Row` with no line written about it. **No schema version, no `firestore.rules` release,
no re-seed** — `config/grove.wards` still publishes `legendary` and the server no longer reads it,
which is what makes the deploy safe in either order with the client.
Offline green: `compile.py` (all fifteen assemblies), the EditMode suite, `content.py` (0 errors),
`loc.py` (0 missing), 72 function tests, `seed-config.mjs --check`, `render_loadout.py`.
**The deploy is done** (2026-09-21). `publishGrove` and `publishGroveBoards` were deployed
**by name** — the row projections live beside `buildCard`, so a field's *meaning* changing is a
deploy of every function that writes a row (the 2026-09-20 lesson, read again) — and the artifact
was read back on **both**: all 25 files of the deployed `lib/` are byte-identical to the local
build, `copiesOf` is absent and `ownsWard` is the whole of the seat question. Live green after it:
`ward-seats.mjs` **21/21** (its first run since the rewrite, and the differential that matters —
one purchase stands on **one** seat, a bare row still means four, a copy row alone stands
nowhere), `smoke-test.mjs` **176/176** and `rank-badge.mjs` **11/11**. No rules release and no
re-seed, for the reasons above.
**And an eye.** Nothing has played a line with two Eclipses on it, and the preview panel's keys
still have no render mirror — though they are simpler than they were, because the lower one no
longer carries a price.

**The legendary band is cut and has never been in the Editor.** Its art was written with the Editor
closed, so every one of its pictures is **unaddressed** until `▸ Addressables ▸ Sync All Assets` — which
is a white rectangle two cells tall on the one object a player watches for a whole run (invariant 7b).
Then the standing discipline in full (`Sync All Assets` **and save** → `Audit Addresses` → `Validate
Content` → `Validate Art` → EditMode). **The re-seed is done** — `config/grove.wards` carries all thirty
as of 2026-09-17, read back and diffed against `progression.json`, with the twenty already there
unchanged; the live suite was 166/166 after it. **The `publishGrove` deploy landed on
2026-09-18** — `starsOf` had learned the bare star row (42h) and until it shipped a published card
drew every legendary at one star; the artifact was read back and the live suite is 166/166 again.

**The eclipse impact was re-cut on 2026-09-18 and has never been played.** It was the loudest picture
in the mode and that is measured rather than argued (`make_legend_fx.py --report`): **26.19% ink over a
100% x 100% lit box**, where the next loudest impact of the thirty is 14.77% over 77%. Its outermost
prism ring stood at .68 of a frame whose edge is at .50, so a third of it was drawn outside the picture
— a ring with a square crop on it rather than a bigger explosion — and the board then multiplies the
reel by `SiegeView.BoltScale`, which the sun is given at **1.62** against the band's 1.18. Every radius
is inside .50 now and the gains are a step down: **13.78% ink over 70.8% x 70.8%**, which is the band
with `hit_permafrost` and `hit_stasis`, and still the biggest thing the line throws. Nothing about the
shape, the colours, the count or the timing moved. **Twelve frames at the same twelve addresses**, so it
costs no Addressables work beyond the sync the band already owes — and `BoltScale` is the dial if it
wants to come down further.

**`levin_muzzle` fails `fxreels.py` at 0.76% ink** and was already on disk at HEAD — it is the
Thundercrag bake above, not the legendary band, and it is the only red reel of the 308.

**Gemfire is live on the App Store (id `6804516450`) since 2026-09-25; Android is still on closed
testing.** The iOS ad and attribution side was wired the same day, all of it dashboard work and no
build: LevelPlay's iOS app has **three bidders on all five live rewarded units** - ironSource, Google
(one AdMob *partner bidding* unit, `levelplay_bidding`, serves every LevelPlay unit) and Unity Ads
(Game ID `800381062`, placement `BP_Rewarded_iOS`; the new Unity dashboard refuses LevelPlay's bidder
auto-setup, so it is manual). **`app-ads.txt` is filled and live**: `OWNERDOMAIN=tekoworld.com`, the
three DIRECT lines (ironSource publisher `676127`, Unity `137877980`, AdMob) and the two networks'
reseller lists merged and de-duplicated - pasted from each dashboard, so refresh them from there, and
the two copies (repo root, website `public/`) are byte-identical. Meta has the iOS platform, a new
**Gemfire** business portfolio with verification submitted, and `tekoworld.com` domain-verified by a
meta tag in the website's root layout. Payouts are three separate payees (AdMob, ironSource, Unity),
each with its own W-8BEN and bank details, all set. Unity's bank form assumes a Cypriot bank and
refuses Revolut's `REVOLT21`, so Unity pays into a different account.

**Store and platform.**
- **AdMob cannot verify `app-ads.txt` for iOS yet, and the file is not why**: the App Store listing
  names no developer website (the iTunes lookup's `sellerUrl` is empty), so the crawler has no domain.
  Set App Store Connect's **Marketing URL** to `https://www.tekoworld.com` (it may ride the next
  version), then AdMob ▸ app-ads.txt ▸ Check for updates. Give Play's "Website" the same domain.
- Archive the retired `run_continue` unit in LevelPlay (unticked from Unity Ads, still present).
- AppsFlyer's **iOS** app: activate Meta ads with the two level events, and set up SKAN conversion
  values - the Meta connection made on 2026-09-13 was the Android app's.
- Delete an Apple-linked account on a device and check it leaves **Settings ▸ Sign in with Apple**. Every
  account the live suite makes is anonymous, so Apple's token exchange and revoke have never executed.
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
- **Is the legendary band the right size, the right price and the right rule?** Ten turrets that wear no
  colour, stand on any seat and fire at everything, at **45,000–150,000 credits** behind keeper 45–60
  (gems until 2026-09-18, at the owner's instruction; a flat **x25** on the authored gem ladder, so its
  shape is unchanged and one multiplier retunes the band). It is the
  largest thing this mode has ever sold, it is **bought per seat** since 2026-09-21 (so a line of
  four of one is four payments and four star ladders, and 885,000 buys the band once rather than a
  line of it — by the copy from 2026-09-18, which priced the same and read wrong, 42k), and it
  **suspends the colour lock**, which is the mode's central decision — deliberately, at the top of the shelf, where a player has already made that decision a
  hundred times. It cannot move a star line (42i), so what it can be wrong about is *feel*: four of them
  is a line with no wrong answer in it. **Play a rung with four and say whether the mode is still the
  mode.**
- **Is the ward shelf's ceiling reachable, and is the credit ladder the right one?** It is now the largest
  credit sink in the game — bigger than the grove's whole catalogue — and its top two bands are shut to
  every player alive. Since 2026-09-18 **every rung of all thirty is priced in credits**: 946,400 to own
  the nineteen colour turrets across all four colours, plus 885,000 for the legendary band bought once,
  which is **1,831,400 before a single upgrade**. So the **gem** hole is now the whole shelf rather than
  part of it — this mode sells nothing for gems at all, and what fills that is still unanswered.
- **Are the home ladder's gates reachable?** Keeper 10 / 20 / 40 against content paying for about keeper 9.
- **Is `s07_crownfall` a rung or a wall?** The sixth chapter's finale is held at 2 of 9 rhythms on the
  starter line and 6 of 9 on a good one, which is a steep "buy a turret" and is the same band as
  Thundercrag's finale. It stands a **sunlord** at this chapter's surge, which is 9,520 health — half
  again the largest thing anywhere else in the mode — and its verb takes wards off the line while the
  player is trying to deliver it. The cog rate carries it (25 → 40, 2026-09-18) and that is the only
  lever that moved it at all: fewer raiders and fewer brutes changed nothing. Worth a play before it is
  called tuned.
- **Have the charms made the mode too easy?** Three rare free payoffs moved every chapter (Thornwatch
  80 → 87 of 90 on the starter, Broodmarch 63 → 76, Barrowfell 28 → 46) with nothing else retuned. **The
  rate is at its floor and the payment does not work** (37co), so more would cost a difficulty rewrite of
  all three chapters, undoing tuning already signed off. **What must not happen is tuning until a gate goes
  green** — both shelf gates measure a *share* precisely so they keep saying the same thing while the
  baseline moves (37ch).
- **Should a raider be framed to its body?** The pack feathers its baked shadow out to alpha 1, so every
  insect is framed around a halo and drawn smaller than it could be.

**The Daily Challenges banner is a painted picture with the English words in it** (`Art/Ui/challenges`),
so that one control has no translation until the art is re-cut — the only string in the game outside
invariant 6.

**Play it.** None of the retention features or the last two chapters have been played. The questions worth
an analytics event, one per feature: how many **shields** are bought while the streak is *not* at risk; how
long a finished **task** sits unclaimed; how long a **stormglass** stands before it is matched; how often a
**siege** run ends with the line down rather than the hill cleared, and how much of a boss's health the ward
of its own colour took; how many accounts open the **Infinite hub** while it is still shut; how many **referral codes** are
shared against how many are ever typed, and how many typed codes reach the milestone; how many people
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
second tap is armed only when there is something to lose — a cleared glade — and **arming a button over an
empty account is what teaches a player to tap through it on a full one**. `ContinueOverlay` is not a fourth: it is an offer whose
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

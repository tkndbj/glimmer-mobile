# Glimmer Groove

A globally distributed mobile puzzle game (Unity 6000.5.4f1, Android + iOS), **live on both stores**. The
product name is **Gemfire**; the repo folder, this file and the bundle id `com.tekoworld.glimmergroove`
still say Glimmer Grove, and the bundle id can never move. Every change ships to real players: a save,
wire or rules mistake reaches live accounts.

## The standard this project is held to

> This app will be distributed globally. Everything we build should be scalable, sustainable, and
> maintainable from day one. No demo, only production builds. Implement the most proper solutions, like
> AAA companies. — the project owner

- **No placeholder architecture.** A genuine placeholder says so in the code and is replaceable by
  touching one file.
- **Cost curves decide priority.** Prefer the change that is cheap today and expensive later.
- **Prove it, do not assert it.** Back every claim with a compile, a test, or a validator run.
- **Push back with reasons.** Say the specific failure once, plainly, then do what the owner decides.
- **Finish the whole job.** If something can only be done in the Editor, automate everything around it and
  hand over exact steps.

> **About this file.** Each invariant is a rule and, where not obvious, the failure that bought it.
> **Numbers are permanent** — ~1,500 code comments cite them — so an entry may be compressed but **never
> renumbered**; spent ids stay spent. **Keep it small**: a new invariant is one or two sentences. Deploy
> records, dated "done" notes and drop logs do **not** belong here (git history has them). Craft lives in
> `Assets/Game/CRAFT.md` (read before touching a screen, animation or art tool), authoring in
> `Assets/Game/CONTENT.md` (before content, assets, loc), modes in `Assets/Game/MODES.md`.

## Invariants — do not break these

### Content and ids

1. **A `LevelId` is permanent.** Save data, analytics and remote config key on it. Never rename or reuse a
   shipped id; never key anything on a level's position.
2. **Never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`** — a frozen record of the pre-1.0 build.
3. **`Domain` must never reference `Presentation`.** Asmdefs enforce it; raise an event instead.
4. **Content is data, not code.** Adding a chapter must never require a code change.
4a. **The manifest owns membership and order; a chapter body owns content.** The boot path reads
   `manifest.json` only; bodies load on entering a chapter and are evicted on leaving (only Android routes
   StreamingAssets through `UnityWebRequest`, so a boot-path body read is invisible in the Editor).
4b. **Every chapter file must be in the manifest, and only the Editor may check that.** `ChapterFiles` is
   the one place allowed to list the folder.
4c. **Anything that rewrites `manifest.json` must prove it lost nothing** by reading its output back through
   the game's reader. Never relax it into a warning.
5. **Omit `par` when authoring.** It is derived from the board.
5a. **A level's loc keys are derived from its id and cannot be overridden.**
5b. **"Is this tile solved" is `Puzzle.Alike`, and it exists exactly once.**
5c. **A rooted tile is authored at `/0`**, checked by asking `Puzzle.Alike`, never `rot == 0`.
5d. **A mechanic that rejects no arrangement is decoration, and that is countable.** Enumerate the
   arrangements satisfying the hard constraints and ask which win; a count of one means the hard constraints
   alone decide. Asked of mechanics, threats, fail states, readouts, timings and purchases alike: the answer
   is a count, not an argument.
5e. **`Puzzle.Matters` has a second clause**: a tile the *player* has lit counts.
5f. **A wrong turn must be visible.** A retired token or block name is **refused by name at parse**
   (`JsonUtility` drops unknown fields silently); a retired lesson id is spent; a retired *level* id is kept
   with its string changed; a loc key may be re-minted.
5g. **A board is graded on its solution and met as dealt.** A properly dealt board's par is ~1.2–1.35x its
   turnable tile count.

### Text, lessons and assets

6. **All player-facing text is a loc key.** The build gate fails on any missing; never build keys by
   concatenation.
6a. **A lesson is a permanent id; `ScreenLessons` owns order and chaining** for a screen that teaches several.
6b. **A *tap this* lesson rings the thing to tap, never its source.**
6c. **A translation is a whole copy of English** — every key, no extra, the same `{n}` slots, tags and
   line breaks (`loc.py`, `TranslationTests`); `Loc.Languages` lists exactly the tables in `loc/`. UI
   capitals go through `.Upper()` (`Loc.Upper`: Turkish i → İ); `compile.py` refuses a bare
   `ToUpperInvariant()` in Presentation.
6d. **Arabic is shaped where the game draws it, never in `Loc`.** `GameText` (every `UIKit.Label`) shapes,
   wraps and reorders it through `ArabicText`; anything the OS draws (notifications, plist, share sheet)
   stays logical. The face is `GameFontArabic`, `GameFont`'s fallback, cut with its forms table by
   `make_arabic_font.py`. The layout does not mirror, so a string naming left/right or laid over controls
   keeps its visual sense (`ui.mark.info_tracks_body`, `ui.settings.toggle_row`).
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or `Addressables` directly;
   derive paths from `AssetManifest`.
7a. **Asset registration is an importer hook plus a build-gate audit**, never a menu item.
7b. **Transient art belongs to a named `AssetLibrary` scope.** An address already global stays global; one
   owned by another scope is never re-claimed. A screen repaints when its scope arrives — **an `Image` with no
   sprite is a white rectangle**, where nearly every asset fault ends.
7c. **A chapter's art is arithmetic on its ordinal, never a choice**, with a generator writing the answers
   into the body.
7d. **Texture size cap and compression grade are folder rules applied before the importer hook's early
   return** (382 files once kept `Uncompressed`: 6% of the library, 49% of texture memory). Graded up to 4x4
   only where the cap is small, measured with `compare_texture_formats.py`.
8. **The map shows one chapter at a time.**
8a. **Map geometry lives in `ChapterMap` (Domain)** so collisions are build-gate facts — and the footprint
   checked has to be what collides.
8b. **Which chapter is shown is per mode, device-local and only a hint**; null the moment the id is not a
   chapter of that mode.
8c. **The disc, halo, seal and shadow are read from one name.**
8d. **A map prop is drawn the way the map is drawn** (no gate: put it on a strip and look). A withdrawn
   picture goes with its `.meta`, manifest entry and Addressables row.
8e. **A node stands on the ground the painting draws**, derived by `Tools/make_map_seats.py`: footing on the
   road, land all round, colour lists subtracted as well as added, water as a gradient, seats rounded before
   judged.
8f. **A seat carries `afloat`**, generated, never authored; only a map that paints a current moors anything.
8g. **`NUDGE` shifts a seat along ground the search already found**, never up/down, re-checked against
   `ChapterMap`.
9. **XP and earned credits are derived, never accumulated.**
9a. **The reward rule exists twice (C# and TypeScript) and must stay identical**, both running the shared
   vectors. Change one, change the other, add a vector, re-seed.
9b. **`progression.json` versions independently of the catalog.**
9c. **So does the chest generator** — a pure function of (account, day, index), FNV-1a then xorshift32, all
   32-bit. Constants, shifts, stream numbers and modulo are contract.
9d. **The Infinite lane's XP is bounded, not recomputed**: a lifetime wave tally per level
   (`endlessBest[].waves`) pays `xpPerWave` up to `maxWaves`. It buys no currency. A separate addend in
   `PlayerProgression`, mirrored by `endlessXp` in `grove.ts`, held by `endlessCases`. Absent config falls back
   to built-in figures on both sides.
9e. **An XP boost multiplies at the moment XP is paid and banks the bonus.** `XpBoost.Bank` is the only XP
   multiplier: sources are totalled first, boosted once. Three windows, each a monotonic deadline joined by
   `max` — watched (cooldown derived from its deadline), bought, and surge (one deadline per strength) — they
   add, capped at `maxPercent` (350). The banked total is clamped on read to `(star XP + endless XP + challenge
   XP) x maxPercent%`. Mirrored by `xpBoostXp`, held by `xpBoostCases`.
9f. **Infinite-lane credits are bounded per day, enforced on the wallet**: 30 a wave, 10,000 a day
   (`endless.ts`). The claim id carries the day's running total (`endless:{day}:{paidBefore}:{currency}`); the
   server's figure folds back via `ApplyServerState`, upward only, so a second device never offers money the
   day already spent (45d).
10. **The client never raises `grantedBaseline`.** Receipt validation is idempotent on the store transaction id.
10a. **An award reaches the player as a claim with an id derived from what earned it**; the server recomputes
   the amount. Never `GrantLocally` except for the account seed.
10b. **Daily chests are earned, never bought** — no price, no second weighted pick.
10c. **A chest cannot be opened before the account id exists.**
10d. **A rewarded ad is granted by the network's callback, never claimed by the client.**
11. **Cloud conflicts merge; they never prompt.**
11a. **The ledger is a map keyed by level id, never an array.**
11b. **Anything a merge touches must be monotonic.** Store counters of events and derive counts; the merge
   is `max`. Its "absent" state must be a value a real one cannot hold (`JsonUtility` writes zeros).
11c. **A value merged by recency carries its own date, and its default is never stored.**
11d. **A screen drawn from the save repaints on `CloudSaveService.Learned`** (raised only when a merge changed
   the file), and `PlayerProgress.RecordChanged` triggers a sync.
11e. **Leaving the app is a departure, not a sync.** `CloudSaveService.Depart` snapshots on the main thread
   and does the rest on the pool; `Owes` compares local writes against the last agreed file. Every field must
   be in the mapper, `SaveDelta` and `SaveMerge.Join` — `SaveWiringTests` holds all three by reflection; a
   deliberately unsynced field goes in its exemption list with the reason.
11f. **What a sync agrees is what the device then holds.** A field the loader drops or repairs makes every
   write owe a sync (a loop a minute). **Retired in place means still written.** Held by
   `SaveWiringTests.TheDeviceHoldsWhatASyncAgreed`.
12. **Adding a field to `SaveFileDto` interacts with the checksum.** Bump `SaveSchema.Version`.
12a. **A field is on the wire only when it is in four places**: `SaveFileDto`, `SaveDelta`, the Firestore
   mapper both ways, and `hasOnly` in `firestore.rules` (an unlisted key loses every save write; rules deploy
   before the client). A field inside an existing map costs no rules release.
12b. **Every list the rules bound is capped by the client first** (`size() <= N` refuses the whole write);
   `CloudWireTests` reads `firestore.rules` to hold each pair.
13. **A reward is derivable, adjudicated, third-party, or not currency** — recomputable by the server,
   reported by a third party, or bounded so tightly that forging it buys nothing.
13a. **A claim is never refused for a reason that will still be true tomorrow** (the client resubmits for
   ever). Missing config leaves a claim *unconfirmed*.
14. **Derived rewards are free of save state, and that is why they are preferred.**
14a. **Derived decides where a reward comes from, never when it arrives** — add a monotonic floor per key if
   needed; never a stored amount.
15. **An entitlement is stored; everything that pays is derived.** Owned sets join by union.
15a. **A gate is permission to pay.** The gate is tested before the price; `IsHeld` never re-checks the gate
   on something bought, or a retune confiscates it.

### The grove — REMOVED 2026-09-21

> **Invariants 16–16x are spent** (the Grovement village was removed). Ids are never renumbered;
> `CONTENT.md` records what was removed. Four rules survive:

16y. **The wire spellings stay**: `groves/{uid}`, `GroveCard`, `GroveBoard`, `GroveNames`, `config/grove`,
   `ReportSubject.Grove` and the nine `grove*`/`homestead*` save keys. A card is a *keeper*; `config/grove` is
   the turret roster.
16z. **The five removed save keys stay in `hasOnly` with their bounds** — a rolled-back client still writes
   them, and dropping one costs it every save write.
16aa. **A removal is a schema bump** — fewer fields fail every checksum; `Verify` trusts a file of another
   version (v32 → v33).
16ab. **The companion roster is inert**: nothing counts or draws a companion; deleting it is a separate
   decision.
17. **A save may only ever be pushed to the account it says it belongs to** (`AccountGate`). No undo.
17a. **A switch finishes on the device before the network is asked for anything.**
17b. **Only a caller holding the sync latch may create an account; the anonymous sign-in is single-flight.**
   Outside the latch, `AuthoriseUnlatchedAsync` may only resume.
27. **Deleting an account removes data first and the account last**: visibility, the name, the save
   (recursively), Apple, then the auth user — each delete-if-exists. Kept on purpose: global receipts, reports
   this account filed, a denied name's reservation retargeted to a tombstone uid.

### The store

18. **A real-money product grants currency, and nothing else.** Hearts and boosts are bought with gems.
18a. **A transaction is confirmed only after the grant lands** (server verifies, records a global receipt
   key, grants). No per-purchase state in the save. A refused receipt is never confirmed.
18b. **The shop is one authored list; the server derives its half.**
18c. **Refunds are watched**: Apple pushes, Google is polled.
18d. **Or an idempotent permanent entitlement** (heart containers) — never a stored amount, never both.
18e. **A shelf's picture ladder is exactly as long as the shelf.**
18f. **A purchase paid and not yet honoured is said mid-screen and can end** (queue emptied, timeout, or
   dismissed) — raised only for a checkout this process opened.
18g. **A shelf may carry a rewarded-video offer first**; taken off only when content drops the placement.
   `ShopAdShelf` is in Domain.

### What a stranger can see

19. **Anything a stranger can see is a separate, server-written document.**
19a. **A public number is adjudicated**: earned half derived from validated records, bought half clamped; a
   save naming something unreachable is dropped, not cut down.
19b. **The public name is a second rule on the stored one; the server governs.**
19c. **A standing is read off a published distribution** (deciles + a hundred-row board), never a maintained
   global ordering.
19d. **A name is unique because a document id is unique.**
19e. **The two name folds are one rule** held by shared vectors (Mono and ICU disagree about Unicode).
19f. **A published name comes from the reservation, never the save**, filtered again at publish time.
19g. **The word list is the least important moderation layer**; the fold stops bypasses, reporting catches
   the rest.
19h. **The list is a document and the takedown a flag**; a published list materially smaller than the shipped
   one is refused.
19i. **A report is keyed on the pair of accounts**; the threshold counts distinct reporters.
19j. **A card is asked for after the sync** (`Settled`), built over the receipt's save.
19k. **A board row carries what it is ordered on and what it draws** (`rung` compared in `sameRow`). A board
   is one document ordered on one card field; a nought is absent; retired boards are pruned
   (`pruneRetiredBoards`); the deletion scrub walks the collection.
19l. **The endless board is the one public number the server cannot recompute** — bounded by a ceiling,
   mirrored client-side. The day it pays anything, this stops being defensible.
19m. **The board and the distribution come out of one walk.** A nought is never a sample.
19n. **A caption that can grow is measured against its plate by a render** — `UIKit.Shrinkable` truncates
   silently; the tell is Best Fit at its floor.
19o. **A keeper is two judgements in two collections** (name, arrangement), one mechanism per
   `ReportSubject`. Wire spellings permanent; callable names are not.
19p. **What a public profile draws is what the score counted, in one walk**; an unvouched seat is omitted.
   Free turrets publish `free`.
19q. **A board row opens a chooser; a profile shows only what is held** — no prices, padlocks or taps.
19r. **A board is live via the publish** (`placeOnBoards`, gated by a cached cutoff); `rebuildBoards` every
   fifteen minutes is the net; a withdrawal scrubs the row in the same call. The panel prints the cadence
   (`LeaderboardBoard.RebuildMinutes`).
19s. **A gate that waits on something must be the thing that asks for it** — or find it need not wait.
19t. **A keeper gate is asked of what is counted, never of what is merely drawn** (`publishedLine` may not
   re-gate a seat). The day a line pays, the gate comes back.
19u. **A client-readable collection grants `get`, never `list`.**

### Modes — in `Assets/Game/MODES.md`

**Invariants 20–26h, 28–36i, 37–43f and 59–59j live in `Assets/Game/MODES.md`** — entry tests, grading, fail
states, the withdrawn and hidden modes, and all of Thornwatch (charms, bosses, turrets, utilities, the
Infinite lane). **Read it before touching a mode, a board, difficulty or the siege.**

### The front of the game

44. **The whole UI is one bought interface kit; names in `Skins` are roles** (`btn_green` = "do the thing"),
   so a restyle re-cuts what the names point at.
44a. **Scale a nine-sliced sprite for its corner's drawn size, never for resolution.**
44b. **A face lift is a property of the art, measured.**
44c. **A backdrop's crop is decided by the tool, never an offset in the screen.**
44d. **Two screens of the same furniture get one mirror, and a mirror may never lie** — `UIKit.Box` pivots at
   centre; Unity y is up.
44e. **A `switch` whose `default` is a real answer hides the case nobody is looking at.**
44f. **A kit drawn for light screens is re-cut for light text** — change the art, not ninety call sites.
44g. **Dimming does not recolour something warm** — multiply takes amber to brown; hue-rotate in the tool.
44h. **A boring restyle is fixed by the plate and the ground.**
44i. **A bought sprite's drop-shadow is cut in the tool as a ramp, before trimming.**
44j. **A balance readout is watched, never drawn**: `ResourceSlots.Register` builds it,
   `WalletWatch.Attach` subscribes it (progression and hearts events), `ResourceSlots.Repaint` repaints it.
   `compile.py` refuses a readout with no watch.
44k. **A screen redrawing itself empties `Content` through `View.ClearContent` only** (`Destroy` lands at end
   of frame and the page goes blank). `compile.py` enforces it.
44l. **A mirror that cannot reach a state cannot be asked about it** — add the flag before trusting the sheet.
44m. **(Ceremony room.)** `CeremonySky` is the one room for the rank ceremony, turret reveal and turret
   upgrade: a generated four-corner wash, a warm corner, a quarter-strength vignette in the wash's own deep.
   The light on top (fans, halo, rim) keeps each screen's own colour.
44m. **(Change events.)** **A change event must mean a change** — compare ignoring fetch stamps
   (`ReferralState.Matches`) and still fire on the first known answer (`SaysSomethingNew`).
44ma. **A long list is a `GridView`** (`Show`, `Refresh`, `ScrollTo`, `Relayout`). Look for it before
   hand-rolling a list.
44mb. **Only the part that can change shape is rebuilt.**
44mc. **A recycled cell writes every field on every bind**, including sprites and looping tweens; a lit cell
   wider than its card sinks in sibling order.
44n. **(Bright ground.)** On a bright room furniture is drawn in `CeremonySky.Ink`, not white-at-low-alpha;
   text stays cream-plus-outline.
44n. **(Background reads.)** **A background read must not refuse or overwrite the player's tap**: separate
   read/write gates ordered by a generation stamp that also carries the account; a payout records its wallet
   (`ReferralLanding.PaysInto`). Each rule is a named pure function.
44o. **A poll is a policy, attached not written** (`ReferralWatch.Attach`).
44p. **A listener points at a document the client may read** — the server bumps a counter at
   `players/{uid}/private/referral`; the answer still comes from `getReferral`.
44q. **A listener's lifetime is three facts and one function** (`ReferralFeedWatch.Settle`: watched,
   foreground, signed in). Re-point on account switch; don't remember a refused attach; the callback may
   arrive on any thread.

### Tasks and the chest ladder

45. **A task is a countable goal, a number and a chest tier.** Goals are code; tasks are rows of the `tasks`
   block; a task names a tier, never a prize; the ladder is gated to rise.
45a. **The save holds counters per goal and claims per period, never progress per task.**
45b. **Rotation is global and pure**: day `k` deals slate entries `k·n … k·n+n-1` modulo the slate; the
   server only logs a claim the current slate would not have dealt.
45c. **A task chest is recomputed like a daily chest and bounded like a streak night**; a third copy of the
   generator pins it.
45d. **A refused claim is dropped by the client with the balance it inflated**; unconfirmed is untouched.
45e. **A week begins Monday UTC, derived from the day key** (epoch was a Thursday: offset three days).
45f. **A chest's lid is a reel in a scope; closed icons are global**; both addresses built from the tier id.
45g. **The hub's box is a chest pack** (`ChestPack`): grandest at the crest, cosine heights, integer bell
   distance, closed chest = frame nought of the opening reel.
45h. **The tasks page is the same pack**; bar fills cut by lifting value (normalised on a percentile,
   clipped per pixel); finished task light is one pool node plus a rim.
45i. **The hub's box carries its name and no clock.**

### The season

47. **A season is a forty-rung ladder graded on marks**; every chest tier carries its mark value, so any
   chest source feeds the season by naming a tier.
47a. **A season names no content.**
47b. **A rung names two tiers and no amounts; a paid column has no holes**; tiers resolved on every read.
47c. **The grant log makes each rung payable once, and any reached rung opens alone, in any order** — each
   track keeps a floor plus goals taken above it (`taken`/`premiumTaken`, `FloorSet`).
47d. **The pass is bought with gems**; both gates refuse a product carrying a pass entitlement.
47e. **`pass:{seasonId}` is a derived spend id**, turned into the entitlement in the transaction that takes
   the gems, at a published price.
47f. **A ladder's slack is gated**: both gates error when it cannot be climbed in the window.
47g. **A rung opens the same chest ceremony; bind writes everything and never animates.**
47h. **The bar measures from the last rung to the next.**
47i. **The crest is cut from the interface kit**, not assembled from primitives.
47j. **The season recurs**: cycle `n` runs `[start + n·period, …)`, computed from the clock, stored nowhere;
   names come from a pool of twelve.
47k. **The bound is the clock** — a future cycle is `unknown`; the id parse is strict (stem + exactly four
   digits).
47l. **The 64-row ledger evicts settled rows before any that might hold an unopened chest.**
47m. **`Featured` answers the oldest season still owing anything**, else live; `GroveEvents.All` joins the
   save's ids onto the calendar.
47n. **What is waiting is `EventLedger.Opens`, the one claim predicate**; `Reached` is ungated.
47o. **A debit travels with its currency** (`SpendSubmission`); a refused one is dropped with its money and
   reported by id.

### The streak

48. **A streak is a run of days that never ends; the ladder laps under it.**
48a. **The ladder pays credits, gems and chest tiers (second tier or above)**; hearts and boosts refused at
   parse.
48b. **Any waiting night may be taken in any order, and the night tapped is the night opened.** Record =
   floor + `StreakTaken` (night numbers above it, the run, the claim anchor). Every claim of a run is dated
   `day = night + offset`, fixed by its first claim (`DailyStreak.ClaimDayAt`), pinned by
   `streak-order-vectors.json` on both sides.
48c. **A shield is the day it was bought** — one monotonic date; a second purchase refused while running.
48d. **A protected day is forgiven, never credited** — the start and the collected floor slide.
48e. **The shield needs no server**: still at most one night per calendar day, strictly climbing; the debit
   id is derived.
48f. **A streak chest feeds the season**; both gates print the combined pace.
48g. **The streak board is a list of 1000-unit rows** that opens on the takeable night.
48i. **A takeable reward wears a light drawn over its plate**, built and destroyed with the state.
48j. **Reward size is a drawn height via `ChestPack`.**
48k. **The streak page says "level" where the rest says "glade"** — deliberately not a sweep.
48l. **A repaint switches back on anything a one-off path switched off.**

### The update wall

49. **A client can be withdrawn; the requirement lives on the device, and the server's answer governs**
   both ways. Deleting `config/release` lifts every wall (the emergency stop).
49a. **A wall needs a door (`https` link) or neither is applied**; the seeder refuses one without.
49b. **Per store**; the seeder refuses a minimum ahead of `bundleVersion`.
49c. **Device-local, never in the save, one preferences key holding both halves.**
49d. **The panel is re-asserted every frame**, above every modal, answers the hardware back key, not over
   the splash.
49e. **Nothing is stopped underneath it** — every write is an idempotent monotonic join.
49f. **It reads a public document, signed out.**
49g. **One version parser**: content `minAppVersion` floors at 1; running version answers 0; a segment above
   99 is refused.
49h. **The panel is cut from art; a gold down-arrow means download.**
49i. **The wall's sentence names no game**, so it cannot go stale.

### Reminders

50. **Every reminder is derived on the handset that sends it** — zero server cost. Broadcast would be FCM
   topics as a sibling of `INotificationScheduler`, deliberately not built.
50a. **Sentences are code; which are sent and when is content** (reaches no server).
50b. **Copy is baked at arming and read up to a week later** — argument-free, naming no retunable count.
50c. **The schedule is a pure function of save and clock; `Arm` cancels and rewrites.**
50d. **Armed when backgrounded only**, after the save flush; losing focus is not backgrounding.
50e. **Slots are local hours** (the one non-UTC clock).
50f. **iOS keeps the 64 soonest pending notifications silently; the count is gated.**
50g. **Cadence is an outcome of cooldowns, not a quota**, and it thins the longer a player is away.
50h. **The permission is asked at a chest, never on the splash**; switch and OS answer are separate
   device-local facts. The package's iOS "request on app launch" stays off (it shipped on, ahead of the
   splash); `NotificationTests.TheOsIsNeverAskedForNotificationsAtLaunch` reads the settings file. The
   one launch request is iOS *provisional* (draws nothing; quiet delivery with Keep / Turn Off).
50i. **Android status-bar icon is a silhouette in an `.androidlib`.**
50j. **A 24dp mark is a different picture**, nothing thinner than 1/12 of the canvas.
50k. **A `versionDefines` flag adds no reference**; `compile.py` runs three passes (no package,
   `UNITY_ANDROID`, `UNITY_IOS`).
50l. **Read the package on disk, never the docs page** — 2.4.3 binds the two platform APIs directly.
50m. **No double hyphen in an `.androidlib` manifest comment** — it aborts the Android build; the tool refuses.
50n. **The horizon is bought with a taper**: three a day for a week, then one a night out to twenty-one days.
50o. **`USE_EXACT_ALARM` and `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` are deliberately not used** (Play policy).
50p. **The OS dialog is only raised by a yes to our own panel** (`ReminderAskOverlay`, raised by
   `ReminderMoment` after a chest or a streak extended to two days or more) or by the Settings row: iOS draws it once, Android twice, ours costs
   nothing to refuse. Yes goes to the OS dialog while it will still draw (`NotificationAsk.OsWillPrompt`),
   else to the OS settings page. Shown at most four times an install, 3/7/14 days apart, never to a player
   who switched reminders off; the log is device-local (`glimmer.notify.asks`), marked at open.

### Inviting friends

51. **A referral is server-owned, none of it in the save.** `referrals/{uid}` holds both halves,
   `referralCodes/{code}` holds uniqueness; the device caches per account. **A referral chest is paid on
   request** by `claimReferral` (`grantLog/referral:{subject}:{currency}`); a `referral:` id at `claimAwards`
   is refused. **A referral chest grows no season.**
51a. **The milestone is a named chapter judged off the server's save, settled on the invitee's read** after a
   settled sync — never a save trigger. `content.py` proves the chapter is shipped and unwalled.
51b. **Flat payout naming tiers (two royal chests each side), a cap on bound invitees, and the share sheet
   never sees contacts.**

### Ranks

52. **A rank is derived and stored nowhere, sound only because every measure is monotone.**
52a. **A measure is code (`RankMeasure`); a requirement is content** (`progression.json`).
52b. **The measure registry is the counted-verb registry** (`TaskGoals`); `stars`/`three_stars` read the held
   reading.
52c. **The lifetime tally rides inside the `tasks` map**, fed by `TaskLedger.Note`, floored by what the save
   already proves.
52d. **The held rung is the top of an unbroken run from the bottom.**
52e. **A rank pays nothing** (it is published because the badge is public).
52f. **A rung's badge and strings derive from its id**; `check_ranks` walks the table.
52g. **The board tab reads BOARDS.**
52h. **A public badge is climbed twice**: `rungOf` recomputes it server-side over an `IRankSource`; held by
   `rankCases`; a ladder the server cannot read is truncated at the fault; an unknown rung draws as nothing.
52i. **The ladder may not open before the Infinite lane** — one `keeper_level` line on the first rung, held to
   the lane's wall by `RankGate`, `check_ranks` and `seed-config.mjs` (error below, warn above).
52j. **A rung reached is celebrated at the run's end, win or lose, from a baseline** (`RankCeremony`, one
   ordinal per session, re-taken on account switch or retune). `RankCeremony.Before` raises the run's panel
   exactly once; `compile.py` refuses `WinOverlay`/`DefeatOverlay` raised without it.
52k. **The ceremony is generated from the ladder** and claims one address (`Audio/Sfx/rankup`); an unknown
   rung draws as light and no name.
52l. **One sound, placed at its pitch peak** (0.55 s); a skip rings it.
52m. **Catalog-wide star lines target the catalog the game grows into** — warned, not refused, above what
   ships.

### The tutorial

53. **The tutorial is the live mode with a script beside it** (`TutorialScreen` over a real `SiegeBoard` from
   `SiegeTutorial`), teaching two things.
53a. **It teaches `siege_fuel` and `siege_brim`**; skipping marks both.
53b. **It stores nothing new** (`TutorialGate`: `siege_brim` seen, or any level opened).
53c. **It cannot be lost**: `SiegeBoard.Sheltered`, read by `Bear` and `Topple`, protects every ward.
53d. **The board is code and reaches no content gate**, so `TutorialTests` plays the whole script.
53e. **A feed is a third of a tube; the loop ends on `Charged`, not a count.**
53f. **The second panel waits for a crowd, with a ceiling**; `SiegeTutorial` writes through existing doors only.
53g. **The hand picks up the gem that joins the line** (`SiegeTutorial.Oriented`).

### Keeper levels for sale

57. **A keeper level can be bought, one at a time, and the rank ladder never sees it.** Level = earned (XP) +
   purchase count (`keeperBought` on the wallet; device copy `wallet.keeperLevelsBought`, `max`).
   `KeeperLadder.Compose` adds them and `PlayerProgression.Level` reads the sum everywhere except the rank
   (`EarnedLevel`). Floors ratchet the earned level only.
57a. **The debit is `keeper:{ordinal}:{level}`**, priced by the server off `keeperLevels`, refused unless the
   ordinal is next and the level matches the save; a refusal takes the level and every one above it back.
   `ApplyServerState` folds `keeperBought` upward always, downward only when no keeper debit is pending.
57b. **The price exists three times** (`KeeperLadder.PriceFor`, `keeperPrice`, `make_keeper_vectors.py`)
   held by `keeperPriceCases`. **Absent sells nothing on every side.**
57c. **The page is a one-column `GridView` climb with one buy key** (`render_keeper_ladder.py` refuses any
   overlap). `Btn.Enter` is entrance plus rehome; `Rehome` refuses scale nought; the page opens on the
   standing row (`GridView.Show(openAt:)`).
57d. **Milestone chests every few levels** (`keeperMilestones` rows of `{level, tier}`; absent pays nothing),
   at the effective level, proved by `judgeMilestoneClaim` (above-level claims left unconfirmed). Rolled by
   `ChestSeed.ForSubject` tag `milestone`; currency claim `milestone:{level}:{currency}`. Save holds a floor
   (`keeperMilestonesClaimed`) plus chests taken above it (`keeperMilestonesTaken`, `KeeperMilestoneSet`):
   **the chest tapped is the chest opened**, any order.

### The welcome bonus

58. **The welcome bonus is four turrets earned on different days; a finished quest is taken as the turret or
   as its shelf price.** A quest is a counted verb (`TaskGoal`) and a number of days, rows of the `welcome`
   block. The turret is `WardLedger.Grant` on **one seat, the row's authored `colour`** (r/g/b/y, required, never the
   row's position, so removing or inserting a quest changes no other prize; a purchase is one seat; four
   seats was four shelf prices against a price worth one), with no server (a client-held entitlement that buys
   no currency: 15, 13's fourth clause). The price is currency, so a claim (10a): `welcome:{quest}:{currency}`,
   re-priced by `welcome.ts` off the roster the seeder publishes, paid once, unconfirmed until the save records
   the choice, refused only in the wrong currency. **Re-seed after touching the block or the roster's prices.**
58a. **The save holds days per verb, claims per quest and the choice per claim, never progress per quest**
   (`WelcomeLedger`, inside the `tasks` map: no rules release). A day counts once; days, claims and choices
   join by union; a choice about an unclaimed quest is nothing. **A quest is only ever counted through
   `TaskLedger.Note`**, and `task_claims` is a verb in the registry.
58b. **The door is `WelcomeLedger.Offered`** (live and something unclaimed), nothing device-local; the hub's
   foot takes one of two shapes (`HubFoot`, held to the squarest canvas by `HubFootTests`). **Absent block,
   feature off**: no door, no counting, nothing confiscated. A finished row's boxes give way to COLLECT, which
   opens `WelcomeChoiceOverlay` (the Deals sheet's frame); the turret ends in `WardRevealOverlay`, the price in
   a `RewardFlight` to the page's one readout.

### Consent

55. **The consent form comes before Apple's tracking prompt on every path, and both wait for the hub.** The
   splash only refreshes (`AdPrivacy.PrepareAsync`) and commits there when nothing is owed; what is owed is
   put by `ConsentMoment` on an idle hub (`AskAsync`), so a new player meets the tutorial first and an
   answered one is never asked. `UmpConsentGateway` bounds the two server calls, waits for a shown form
   without a clock, and drops a form the hub is no longer idle for. ATT only when `ConsentSettled`. Held by
   `PrivacyTests`.
55a. **Native start-up waits for the hub to calm, and ad units load one at a time.** `LaunchCalm` is settled
   by `ConsentMoment`'s idle beat; `RewardedAds.StartAsync` awaits it after consent (the mediation stack
   starting plus six rewarded loads on the hub's first frames was a stutter on every connected launch).
   A rewarded unit is queued through `AdLoadQueue`, never loaded beside another. Held by `PrivacyTests` and
   `AdLoadQueueTests`.

### Daily challenges

56. **A daily challenge is a puzzle genre fused with the ward line, sharing nothing about being a run.**
   `IChallengePuzzle` genres on four starter turrets over a turn-based `ChallengeHill`: a turn feeds its
   colour's turret and walks every raider a step; the solving move wins first; no ward standing loses. No
   `LevelId`, record, stars, hearts or lessons; `ChallengeScreen` reaches none of the run path. Its own file
   `challenges.json` (`ChallengeRules`).
56a. **A genre is code; `ChallengePuzzles` is the one registry**, default refuses; a genre's own `Fault`
   validates its row via `TryBuild`.
56b. **Every shipped row is played by a bot in `ChallengeTests`**, which prints margins. `content.py` is
   deliberately shallow here.
56c. **The colour lock is a lane**; fuel banks on a ward with nothing to shoot.
56d. **Today's row is derived, stored nowhere.**
56e. **Sudoku, Minefield and Stack were withdrawn**; spellings refused at read; ids not spent.
56f. **A level is the calendar's answer** (`ChallengeCalendar`): each genre's rows ranked by a hash of genre,
   row id and day; yesterday's opener moved to the end. Adding a level re-deals nothing.
56g. **A play is spent at the first move** (`ChallengeLedger.Commit`; `Begin` only deals). A win advances, a
   loss retries; a win is paid against `ChargedDay`. Leaving a moved-on board asks via `ForfeitOverlay`. Own
   top-level save key `challenges`; rules release before the client.
56h. **A deal is the pass's shape**: gems under a derived id, entitlement on the wallet; a window is `days`
   from purchase (server covers `from..from+days` inclusive). An upgrade costs the difference and inherits the
   window (`chaltier:{tier}:{fromDay}:{boughtDay}`, `challengeUpgradeCases`); a refused debit takes the deal back.
56i. **Credits as a claim, XP by derivation.** `chal:{day}:{genre}:{win}:{currency}` bounded by the win's
   ordinal against the day's allowance (unconfirmed inside the window, refused after). XP is a rate over a
   lifetime tally per genre (`ChallengeRewardRule`, `challengeXp`, `challengeCases`; allowance
   `allowanceOn`, `challengeAllowanceCases`). The boost base sums all three XP sources.
56j. **The seeder publishes the `challenges` block** (spellings, allowance, deals, rates). Re-seed after any
   change to the file.
56k. **Cards and deal rows name themselves from ids**; a written nought withdraws a payment; retired
   spellings/deal ids refused by name (`ChallengeGenres.Retired`, `ChallengeTable.RetiredTierIds`); the
   largest deal's daily maximum is gated under `ChallengeLimits.MaxDailyCoins` (10,000).
56l. **A challenge glade never mixes light and its fire never banks** — `Puzzle.Blends` off; a lit critter
   pays a volley (`ChallengeHill.Volley`); waves generated by `make_glade_challenges.py`, slack 1.25–1.65x,
   hill never empty.
56m. **A Pairs card is a stone, rows author which cards, never where**: shuffled by `ChallengePlay.Deal`
   (day, attempt); `o` is cursed; combos capped (`PairsPuzzle.ComboCap`); each row measured over 48 deals by a
   bot mirrored in `make_pairs_challenges.py`.
56n. **A Merge move is one gem sliding; nothing is dealt.** Undo is a move; a merge pays once; every board
   joins into one gem (`make_merge_challenges.py`).
56o. **A Push board is composed backwards by BFS** (`make_push_challenges.py`); a seated gem streams; UNDO is
   a turn; no wall rings.
56p. **An advert play is counted by the server** (`challenge_play`, per-day `challengeAds` on the wallet,
   capped at `dailyCap`); one pool across genres; `drawAdPlay` holds drawn ordinals. Held by
   `challengeAdPoolCases`; counted in `ChallengeEconomyGate`.
56q. **`challenge_plays` (noted in `Commit`) and `challenge_wins` are task verbs**, floored by lifetime clears
   on both sides. `ChallengeLedger.LoadFrom` replaces, never joins, on account switch.
56r. **A raider's challenge-hill position has one writer** — `Update` walks widgets toward goals via
   `LaneWalk` (critically damped, never overshoots); `Aim` only lowers a goal.
56s. **A genre is taught by one looping preview** (`ChallengePreviewOverlay`, `ChallengeDemos`), gated on the
   genre's verb lesson; the event lessons are spent.

### Shop deals

60. **A shop deal is coins sold for gems for a window, once per account, made on the admin page**
   (`tekoworld.com/admin/shop`; `adminCreateDeal`/`adminEndDeal`, only the owner's three Google
   addresses, verified, `google.com` provider). It lives in one public document, `config/deals`,
   read by the game with a single get at most every 15 minutes, never a listener. Up to five on sale at
   once (`DEAL_MAX_LIVE`); immutable once made, only its end moves.
60a. **The gems and the coins move in one transaction.** The debit is `deal:{dealId}`, the coins a
   claim `deal:{dealId}:credits` queued beside it; `submitSpends` prices the debit off the document
   and writes the coins' grant record with it, `claimAwards` never pays one (waits while the deal
   takes debits, refuses after). A deal that runs out keeps a 15-minute grace; one ended from the
   admin page (`endedEarly`) has none - "End now" withdraws a mistake - and the panel re-reads the
   deals before every buy (`DealLedger.RefreshNowAsync`). A refused debit takes the coins back
   (`DealLedger.OnSpendRejected`).
   The wallet's `deals` map is the entitlement (`dealsBought` on every reply); nothing is in the
   save, and nothing is offered until a reply has said what the account owns. Held by
   `deals.mjs`, `ShopDealTests` and the live `shop-deal.mjs`.
60b. **History is `dealHistory/{dealId}` (server-only, written with each create and end); buyers are
   counted, never tallied.** A counter in `submitSpends` would make a popular deal one hot document
   and fail purchases on contention, so `adminDealHistory` pages twenty by `endUnix` and counts
   `spendLog` by `dealId` with `count()` (the one indexed field of the exempt `spendLog`, collection
   group); a settled count is stored and never recounted. `adminListDeals` backfills a missing record.
60c. **A deal is raised outside the shop by `DealMoment` alone, once per deal, ever** (rules in
   `DealPrompt`): 3 levels cleared; one popup a session carrying every unshown deal; triggers in order
   coin shortfall (turret, star, keeper level - answered on the screen it happened on), back on the
   hub from a win, last 3 hours; only on a calm screen after the consent questions. Shown is marked
   in the save at open (`tasks.dealsSeen`, v41, the newest 64 ids - a join, never pruned against the
   network). Short of gems stacks `GemShopOverlay` over `DealOverlay`, which lists any number of
   deals and pays coins out on close.

### Art credits

46. **An art credit lives on the publisher's site, nowhere in the app.** No shipped vendor compels one.
   **Before cutting a new pack, read its licence for *attribution*.**
46a. **A licence can forbid shipping.** `GameFont.ttf` is Titan One (SIL OFL) renamed **Gemfire Display**;
   check Reserved Font Names per face. Keep the address `Fonts/GameFont`.
46b. **Choose a face by putting ten in front of the owner.**
46c. **Coverage is built from the face's own marks and reported every build** (not a gate). Missing scripts
   (Vietnamese, Romanian ș/ț, Greek, Cyrillic, Hebrew, CJK) need `fallbackFontReferences`, as Arabic has (6d).

## Spent ids — never reuse any of these

**Machine-enforced** (add in the same change as the removal): lesson ids in `Mechanic.Retired` (held by
`TipTests.EveryMechanicIsEitherLiveOrRetired`); level block names in `content.py`'s `RETIRED_BLOCKS`.

**Enforced only by this table** — a real save may hold a record against any of these:

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
| `bud` | `b02_tanglewood` | `b02_firstbolt` `b02_makefive` `b02_sunspark` `b02_crossfire` `b02_stormheart` `b02_sixfold` `b02_thundering` `b02_sunwell` `b02_wildstorm` `b02_stormcrown` `b02_firstvine` `b02_longreach` `b02_deepthicket` `b02_windingway` `b02_twovines` `b02_thewilds` `b02_crossvine` `b02_thornedvine` `b02_thetangle` `b02_tangleheart` `b02_windrow` `b02_lanternfly` `b02_graftwood` `b02_puffhollow` `b02_hivehill` |
| `march` | `m01_hollowmarch` | `m01_firstcore` `m01_haulroad` `m01_thegate` |
| `ember` | `e01_emberforge` | `e01_firstember` `e01_twinlocks` `e01_ironribs` `e01_frostvein` `e01_chainfire` `e01_deepcell` `e01_ironward` `e01_twinstars` `e01_slaghold` `e01_emberheart` |
| `weave` | — | *(retired before its chapter shipped)* |
| `ripple` | — | *(never authored a level)* |

`f03_wickwater` and its ten level ids are spent too.

**Other spent names**
- **Board tokens refused at parse** (5f): `x`; `1` `2` `3`; `/` and `\` as a mirror; `%` as a sack and `!` as
  a *cell* (`!` is a wave token); `*` as a cog; the one-letter boss form.
- **Fields refused by name**: `runners`, `winds`, `firefly`, `reach` on a blast.
- **Ward ability `prism`** — deliberately *not* refused (`WardAbilities.Parse` answers `none`); gates warn.
  Turret ids `prism` and `spectrum` are live.
- **Enum members kept for analytics ordinals**: `ContinueUnit.Tiles`/`.Taps`;
  `DefeatReason.OutOfTiles`/`.Overgrown`/`.OutOfTaps`/`.Barren`/`.OutOfTime`; `ChestDropKind.RunTime`;
  `SiegeKind.Weaver`/`.Thief`; `SiegeSpell.Weave`/`.Snatch`/`.Bombard`.
- **Retired in place on the wire**: `bestMillis` (22), `DailyChests`' section (45), and `levels[].bestRank`
  (the population standing; its map badge, win line and `publishGroveStats` removed 2026-10-07).
  `config/stats` is deleted; its read rule stays until the update wall excludes builds that ask.
- **League board ids `l0`…`l8`** and the card's `league` field.
- **Season id `first_watch`** (seasons are `watch` + four digits).
- **Store product `gg_first_bloom_pass`** — registered with both stores, never reusable.
- **Grove homes `home_longhouse` `home_tower` `home_keep`, decor `barracks` `citadel`.**
- **Ad placement `run_continue`**; map sprite `boat`.
- **Challenge genre spellings `sudoku` `mines` `tetris` `pipes`** (`ChallengeGenres.Retired`). Retired deal
  ids go in `ChallengeTable.RetiredTierIds`.
- **Withdrawn siege reels**: `kayMon`/`kayBrute`/`kayBulwark`, `ironMon`/`ironBrute`/`ironBulwark` and their
  `_swing` reels; `caller_walk`/`snare_walk`/`clad_walk`. (`caller`, `snare`, `clad` are live.)
- Retired loc keys are not listed — they may be re-minted (5f). Never reuse a level id, chest tier, board id,
  season id or store product.

## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Modes/ (Siege/, Shuffle/) Wards/ Utilities/ Persistence/ Progression/ Cloud/
  Localization/ Analytics/ AssetPipeline/ Store/ Ads/ Daily/ Events/ Social/
  Notifications/ Release/ Ranks/ Tasks/ Challenges/
Assets/Game/Scripts/Notifications/  GlimmerGrove.Notifications (Domain; mobile-notifications binding)
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
Assets/Game/Scripts/Cloud/         Firebase client half
Assets/Game/Authoring/             GlimmerGrove.Authoring    (Editor-only; Domain)
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (EditMode)
Assets/StreamingAssets/Content/    manifest.json, chapters/, progression.json, challenges.json, loc/
firebase/                          functions/, seed/, e2e/, firestore.rules (README.md is the guide)
```

**`GlimmerGrove.Authoring` holds rules that decide whether content may ship and no player runs** — reachable
by the build gate and the tests; `compile.py` proves `domain` builds without it.

**A mode is declared three times**: `LevelMode` (Domain), `ModeLook` (Presentation), `ModeValidator`
(Authoring). An unregistered mode is an error and a fixture fails when the registries drift.

## Verifying

The Editor is often not running and the MCP bridge is down whenever scripts fail to compile. Verify offline.

**Core gates**
- **Compile:** `Tools/verify/compile.py` (Unity's bundled Roslyn; command in the `verify-content-without-unity`
  memory). Also refuses magic-method name clashes, null tests on DTO fields, unacknowledged `.Layout.` reads,
  readouts without `WalletWatch`, `Content` child reaches, run panels raised without `RankCeremony.Before`,
  `ToUpperInvariant()` in Presentation (6c).
- **Tests:** `python Tools/verify/tests.py [Fixture[.Method]]`; `GLIMMER_WHY=1` explains "needs the Editor".
  Read the "needs the Editor" count as well as the red one, and ask why it moved.
- **Content:** `Tools/verify/content.py` — parses content, proves levels solvable, derives par, resolves loc
  keys, runs shared board vectors, walks walls, roster, seasons, streak, reminders, challenges; prints the
  economy. `Tools/verify/loc.py` (keys resolve, not that they are still true).
- **Names:** `artnames.py` (sprites a call site asks for; constants are invisible to it — `SkinsTests` closes
  that), `sfxnames.py`, `fxreels.py` (every reel is a picture, not a sliver), `names.py` (fold on Unity's Mono).
- **Other:** `rungs.py` (inline rung tables vs chapter bodies), `difficulty.py` (not a gate),
  `make_map_seats.py --check`/`--contact`, `make_game_font.py --coverage`/`--check`,
  `make_arabic_font.py --check` (font, forms table, fallback wiring), `arabic_text.py --check` (the shaper
  mirror against `arabic-vectors.json`, which `ArabicTextTests` also runs).

**Shared-rule vectors** (each `--check`; both sides run offline — never let one become Editor-only):
`make_rank_vectors.py`, `make_keeper_vectors.py` (beside, not inside, `keeperCases`),
`make_milestone_vectors.py`, `make_endless_vectors.py`, `make_xpboost_vectors.py` (clamp only),
`make_challenge_vectors.py`. Chest generators: `make_task_vectors.py`, `make_mark_vectors.py`,
`make_streak_vectors.py` — **run in that order** (each splices to end of file). Server half:
`npm --prefix firebase/functions test`.

**Challenge tools**: `make_glade_challenges.py --report`, `make_merge_challenges.py --check`,
`make_push_challenges.py --check`, `make_pairs_challenges.py`; `ChallengeTests` is the authority;
`render_challenges.py` (`--id`, `--list`, `--contact`, `--phone`).

**Shuffle lane** (59): `ShuffleTests` (the identity trace, the ramp's inline pin, the deck, the build, the
model-player sweep), `Tools/make_shuffle_art.py --check`/`--contact`, `render_shuffle.py` (the hand,
`--lang`, `--held`), `render_endless.py --shuffle` (the hub), `Tools/chapters/s13_shufflewatch.py`.
**Siege**: `SiegeRuleTests` (prints the boss-rung table; `EveryShippedBossRungIsAFight` needs each new chapter
in `ShippedChapters`), `TutorialTests`, `EndlessCheckpointTests`, `render_siege.py` (`--captions` is the only
gate for a caption too wide; `--storm`, `--unleash`, `--stilled`, `--heaved`, `--alight`, `--cursed`, …),
`render_ward_preview.py`, `render_tutorial.py --finale`. Art tools with `--check`/`--contact`:
`make_charm_gems.py`, `make_strike_fx.py`, `make_legend_fx.py`, `make_burn_fx.py` (`--strip` for the seam),
`make_siege_art.py --survey` (run before concluding no pack can cast a chapter), `spine_bake.py --verify`.
Charms have no offline gate (`SiegeCharmTests` and renders).

**Renders** (the gate for anything judged by eye; share `Tools/hudkit.py`): `render_home`, `render_shop`
(`--measure` holds `ProductCardBadges` to the sprite; `--deal` draws a live shop deal), `render_tasks`, `render_season`, `render_streak`,
`render_keeper`, `render_keeper_ladder`, `render_endless`, `render_loadout`, `render_boards` (`--row`),
`render_ranks` (`--contact`), `render_rank_ceremony` (`--contact`, `--sky`), `render_referral`,
`render_checkpoints`, `render_welcome` (`--done`, `--choice`), `render_home --welcome`, `render_reminders`
(`--settings`, `--lang`, `--measure` fails on a caption at its floor). Rank/kit art: `make_rank_art.py`, `make_rank_kit_art.py`, `make_keeper_art.py`.
**`--check` proves reproducibility, not quality.** Art tools pass with licensed packs absent (PNGs committed).

**Server**: `node firebase/seed/seed-release.mjs --check`; `npm --prefix firebase/functions run seed -- --check`;
`Tools/make_name_blocklist.py --check`. Live e2e (differential on purpose — a stale function answers 200 with
a valid but wrong document): `smoke-test.mjs`, `delete-account.mjs`, `endless-xp.mjs`, `rank-badge.mjs`,
`ward-seats.mjs`, `keeper-spend.mjs`, `shop-deal.mjs` (adds its test deals beside the real ones and
removes only those, preconditioned; players on a deals build can see them for the minute it runs).

**In the Editor:** `Glimmer Grove ▸ Validate Content`, `▸ Validate Art`, Test Runner (EditMode). Reload the
domain before believing a failure after a play-mode session.

**Facts about the runner**
- An `async Task` test is awaited by the runner since 2026-09-22; earlier green runs proved nothing about one.
- A native call on the save's load path turns every save-loading fixture into "needs the Editor". Put
  device-local state behind a store seam (`EndlessCoins.ITallyStore`; fixtures install `MemoryStore`).
- A vector file only the Editor can read is not a guard (29e).
- A `[UnityTest]` counting frames against code counting milliseconds: bound on `Time.realtimeSinceStartup`.
- Edit mode dispatches no `MonoBehaviour` messages (`DestroyImmediate` runs no `OnDestroy`).
- A probe that writes off a class of failure as "expected" cannot see a real one in it.
- The ads plugin's Editor consent stub leaves `Time.timeScale` at nought.

**New dependencies are recorded here** (no `requirements.txt`): numpy, Pillow, PyMuPDF (lazy, for
`make_siege_art.py`'s vector path). A new mode's art tool and render mirror are committed with its drop.

Builds are gated: `ContentBuildGate` fails the build on any content error.

## Standing discipline

**Editor, after any art or content drop**: `▸ Addressables ▸ Sync All Assets` **and save** →
`Audit Addresses` → `Validate Content` → `Validate Art` → EditMode. Art written with the Editor closed is
unaddressed (white rectangle, 7b); deleted art leaves entries that fail `BuildPlayer`. Files written with the
Editor closed have no `.meta` until it focuses.

**Server drops**, in order: (1) `firestore:rules` only for a new top-level save key or collection, checked
against the *released* ruleset, deployed before the client. (2) Functions **by name, in batches of three or
four**, then read the deployed artifact back. (3) `seed-config.mjs`, from a HEAD shadow if anything is
uncommitted; snapshot and diff the config documents. (4) The Editor's three. (5) `smoke-test.mjs`. **A deploy
without a re-seed pays the old table against the new board**; a new chapter's level ids reach the server only
through the seed.

## Hard-won facts

**Unity and the build**
- Addressables must be ≥ 4.0.1. `GLIMMER_ADDRESSABLES` comes from asmdef `versionDefines`, not Player
  Settings. `m_BuildAddressablesWithPlayerBuild: 1` lives in the project asset.
- The offline compile is blind to Unity magic-method rules.
- Unity re-resolves packages and reimports only on window focus.
- `refresh_unity` says `compile_requested: true` without compiling; force with
  `CompilationPipeline.RequestScriptCompilation()` and check the DLL is newer than the file.
- A file-system gate lies (by failing) while Unity reimports — run twice and compare.

**Serialisation and arithmetic**
- A `[Serializable]` class field is never null after `JsonUtility`; use a fixed shape (`IsAuthored`).
- `LevelDefinition.Layout` is null on any non-glade level.
- `JsonUtility` rejects `.5` and truncates a string at an escape; vectors carry code points alongside.
- No `float` may decide a cell or a threshold; hold factors as hundredths, `(par * n + 99) / 100`.
- `'' in 'RGB'` is `True` in Python. Multiply tenths as hundredths and divide at the end.
- A NumPy scalar is strong under NEP 50 and promotes float32 pipelines to float64.

**Art and addressables**
- Deleting art leaves its Addressables entry, which fails the build; a group the Editor repaired is dirty, not
  saved.
- A sweep may only register what the repo tracks (gitignored packs leak broken references).
- A reel addressed frame by frame with no label cannot be loaded and still ships.
- An orphaned label whose folder still exists is a reel nothing can load.
- The importer hook misses art copied in while the Editor is closed; `Sync All Assets` repairs.
- Sprite sets load by shared label; preprocessors fire on first import only (`▸ Reapply Art Import Rules`,
  batched); a cap change reimports nothing.
- A V2 sprite atlas must be `.spriteatlasv2`.
- Prefer a pack's `layers/` art over its `_preview` sheets (vendor lettering).
- A VFX pack's `Textures/` mixes drawn and sampled textures — render before naming.
- A flood keyer fails on glowing edges.
- A `Mask` texture may never be compressed (uGUI clips at alpha 0.001).

**Platform, store and backend**
- `PlayerPrefs.Save()` is synchronous; compare first, flush only on change.
- A Hub-launched Editor gets a minimal `PATH` (CocoaPods can't find `pod`), and one failed post-processor
  abandons the rest.
- Two Google ads SDKs cannot share an APK; the AdMob adapter stays at **5.18.0.0** (legacy SDK, for UMP).
- Sign in with Apple on iOS cannot use the generic IDP path — the refusal is a Swift `fatalError`.
- A Functions secret is pinned at deploy. A production-401-then-sandbox-success is normal for a sandbox purchase.
- A new 2nd-gen callable 401s until granted an invoker (and for minutes after):
  `gcloud run services add-iam-policy-binding <lowercased-name> --region=europe-west1 --member=allUsers --role=roles/run.invoker`
- Never `firebase deploy --only functions` for the whole codebase; deploy by name in batches.
- "Deploy successful" ≠ "the running bundle has the fix" — `functions:generateDownloadUrl` and read it back.
- **Every wallet writer writes the wallet whole from `readWallet`**, which now carries unknown keys
  (`carryUnknownFields`); adding a wallet field is a redeploy of the function reading it.
  `gcloud functions list --format="table(name,updateTime)"` shows stale bundles.
- A field added to a board **row** shape is a deploy of `publishGrove` *and* `publishGroveBoards`.
- Firebase Unity `Firebase.Functions` ships as source with its own asmdef, needing `Google.MiniJson.dll`; all
  Firebase packages share one version.
- Google UMP must come from OpenUPM as a package, never the `.unitypackage`.
- `seed-config.mjs` publishes the working tree; a patched shadow is only as current as its patches.
- Check `firestore.rules` against the released ruleset (`firebaserules.googleapis.com`, `x-goog-user-project`).
- `spendLog`, `grantLog`, `receipts` are exempt from indexing; a query over one needs an index deploy first.
  The billing budget alert lives in the console (no gcloud account here has billing rights).
- Unity IAP 5 answers confirms through `OnPurchaseConfirmed`; `UnityIapBackend` retries at 2, 8, 30 s and keeps
  the order until the store closes it.
- The live e2e suite signs in as a new anonymous account every run; read anything catalog-derived off the
  published catalog, and re-run before believing a failure in the first minute after a deploy.

## Current state

*Read the manifest and `progression.json`, never this file, when a number reaches a customer.* Both content
gates print the derived totals.

- **Live on the App Store (id `6804516450`) and Google Play.** Ads via LevelPlay (ironSource, AdMob partner
  bidding, Unity Ads); `app-ads.txt` live at `tekoworld.com` (repo root and website `public/` byte-identical).
- **Save schema v40**; manifest/chapter bodies v2; `ContentSchema.Version` 3.
- **Cloud**: Firebase project `glimmer-groove-1cd60`, Firestore `eur3`, Node 22, `europe-west1`; anonymous by
  default with Apple/Google linking and per-account local archives. `gcloud functions list` is the authority
  on deployed functions. Firebase Unity SDK 13.15.0 as vendored tarballs under `GooglePackages/` (gitignored;
  `pwsh GooglePackages/fetch.ps1`).
- **One live mode, Thornwatch (siege)** on three tracks: the ladder, the Infinite lane and the Shuffle lane
  (`s13_shufflewatch`, keeper 5, MODES.md 59 - a dealt Breaker line, no bosses, three upgrade cards every two
  waves; the two authored star waves are guesses until the owner plays). Chapters `s01`, `s03`–`s08` (ten rungs each), `s09`–`s12` (twenty,
  boss duels every fifth), and `s02_endlesswatch` on the Infinite track (opens at keeper 10). Glade, fall and
  prism are hidden. Six casts; the cast table and its squares are full (`SiegeMode.MainCasts`) — the next
  chapter wraps or cuts a cast. Details in `MODES.md`.
- **Systems live**: progression, tasks (45), season (47), streak (48), ranks (52) and the rank ceremony,
  tutorial (53), keeper levels for sale (57), the welcome bonus (58), daily challenges in four genres — Pairs, Glade, Merge, Push
  (56), referrals (51), reminders (50), the update wall (49, ships asking nothing), one public board (Endless
  Watch) plus two distributions, five utilities (the bar is full), thirty turrets including the legendary band.
- **Economy shapes worth knowing**: free play ~953 credits and 12 gems a day; with every advert ~7,160. Content
  pays credits once (80 a level, 40 a star). Stars: gold `par x 1.20`, silver `par x 1.40`, fail at `par x
  1.60`, except a siege, which authors its own. Chapter gate: 16 stars of the chapter behind, flat (cut to
  what it pays). Hearts cap 5, 8 h refill. Continue: 20 gems doubling. Endless credits 30/wave, 10,000/day.
- **Languages**: English plus Spanish, Portuguese (Brazilian), French, German, Italian, Turkish, Polish,
  Arabic (`loc/*.json`); device language by default, chooser in Settings (`LanguageOverlay`), choice saved in
  `settings.language`. The translations were drafted by Claude and await native review. In Turkish a level
  is *bölüm* and a chapter *bölge*. Polish has three plural forms and the code knows two, so a Polish count
  is written label-first ("Gwiazdki: {0}") and never in the gendered past tense. Arabic (6d) has six
  plural forms, so it is label-first too ("النجوم: {0}"), and carries no harakat. iOS: the tracking
  prompt's sentence is `ui.privacy.tracking_usage`; `IosPrivacyPlist` writes it per language as
  `{code}.lproj/InfoPlist.strings` plus `CFBundleLocalizations` from `Loc.Languages` (a new language
  needs nothing more).
- **The Daily Challenges banner is a painted picture with English in it** — the one untranslated control.

## Open items

- **Never observed live**: a season rollover; an EU consent form shown (the gateway has only returned
  `NotRequired`); an Apple-linked account deletion revoking the Apple token; the iOS update-wall force-quit
  pass; a real `adReward` impression on a store build.
- **Store housekeeping**: deactivate `gg_first_bloom_pass` in both consoles; archive the `run_continue`
  LevelPlay unit; AppsFlyer iOS Meta events and SKAN values; delete the ~210 synthetic saves and orphan
  anonymous accounts the live suite and the 17b race left behind.
- **Known missing checks**: which of a mode's kinds the shipped chapter never sends (40a); the offline wave
  reader treats an unknown character as a colour letter; `bestMillis` can leave the wire once no client
  writes it (22).
- **The owner runs the sweeps**: chapter gates with `UNSET` floors (`SiegeRuleTests`) and provisional
  `siege.STAR_FACTORS` are set from the owner's sweep, not tuned until green.

## Three confirmations, and only three

`ForfeitOverlay` (abandoning a committed run or challenge play), `ReportOverlay` (the chooser *and* the
confirmation) and `DeleteAccountOverlay` (27, second tap armed only when there is something to lose).
`ContinueOverlay` and `ReminderAskOverlay` are offers, not a fourth. Everything else costs nothing to undo or is confirmed by the
store's sheet.

## Not done, deliberately

- **Play Games Services** — Android-only, so it cannot be the identity.
- **A visual level editor.**
- **Remote content delivery** — built and off (`ContentConfig.RemoteBaseUrl`). Known gap: `Sync Manifest`
  bumps a chapter's `version` only when its level list changes.
- **A "keepers near you" board** — needs the global ordering 19c refuses.
- **FCM broadcast push** — an unused dependency is placeholder architecture (50).

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
11a. **The ledger is a map keyed by level id, never an array.** That makes a duplicated
    record unrepresentable rather than something the server has to filter, and lets a
    sync write `levels.<id>` alone instead of re-uploading thousands of entries.
    `SaveDelta.Between` decides what to send; an unchanged save sends nothing at all.
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
    golden glade bonus and the event tracks add nothing to `SaveFileDto` — no counter, no claim,
    no merge rule — because both are pure functions of things already stored. That is not
    an economy: it is the reason they were designed that way. Save state is where features
    here go wrong (see 11b), so a reward that can be expressed as a function of the star
    ledger should be.

## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Persistence/ Progression/ Cloud/ Localization/ Analytics/ AssetPipeline/
Assets/Game/Scripts/Presentation/  GlimmerGrove.Presentation (Domain + UnityEngine.UI)
  App/ Board/ Screens/ Dev/
Assets/Game/Editor/                GlimmerGrove.Editor
Assets/Game/Tests/                 GlimmerGrove.Tests        (Domain only, EditMode)
Assets/StreamingAssets/Content/    manifest.json, chapters/, loc/
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

**Verifying is now in the repo.** `Tools/verify/` holds `compile.py` (every assembly
separately, which is what actually proves the layering), `tests.py` (the EditMode suite via
a reflection runner — 320 pass offline, 62 need the Editor and say so), `content.py` and
`loc.py`. It no longer has to be recovered from a scratchpad.

Not done, deliberate: **in-app purchases** (the four store secrets hold `UNSET`, so
receipts are refused — correct until real store products exist), **Play Games Services**
(better Android sign-in and the natural home for the "ranks" leaderboards, but
Android-only so it cannot be the identity), and a **visual level editor** (tooling — the
thing most likely to matter next for shipping cadence). Remote content delivery is built
but switched off; set `ContentConfig.RemoteBaseUrl` to enable.

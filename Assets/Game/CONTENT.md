# Content pipeline

How levels get made, validated and shipped. Read this before adding a chapter.

## Layering

Two assemblies, and the direction between them is a compile error, not a habit:

```
Scripts/Domain/        GlimmerGrove.Domain        no UnityEngine.UI reference
  Board/ Content/ Persistence/ Progression/ Localization/ Analytics/ AssetPipeline/

Scripts/Presentation/  GlimmerGrove.Presentation  references Domain + UnityEngine.UI
  App/ Board/ Screens/ Dev/
```

Domain cannot see Presentation and cannot see `UnityEngine.UI` at all. That is what
lets the whole content pipeline — parsing, validating, solving, saving — be checked
with no renderer present, and it is why `Tests` references Domain only.

Consequences worth knowing:

- Gameplay rules live in Domain. `Energy` holds the colour bit masks because *what
  mixes with what* is a rule; `Pal` maps those masks to actual colours because *what
  it looks like* is not.
- Domain never calls into the UI. `GameSettings` raises `Changed`; the audio player
  subscribes. If you find yourself wanting a `using` that points at Presentation from
  inside Domain, the dependency is backwards — raise an event instead.

## Tests

`Assets/Game/Tests` (EditMode, Domain only). Run them from **Window ▸ General ▸ Test
Runner**. They cover the grid parser, the validator, par derivation, catalog ordering
and identity, the save store's rotation and integrity check, and — most importantly —
the legacy PlayerPrefs migration, which runs once per player and is the only code that
can silently destroy progress.

Two of them validate the *shipped* content rather than fixtures: every level must be
solvable, and every id in the frozen legacy table must still exist in the catalog.

## The one rule

**A level id is permanent.** Save files, analytics and remote config all key on it.
Once an id has shipped, never change it and never reuse it. Everything else —
order, difficulty, art, text — can change freely, which is precisely *because*
identity never does.

Corollary: never edit `LegacyPlayerPrefsImport.LegacyIndexOrder`. It is a frozen
record of what the original build shipped, not a description of the game.

## Where things live

```
Assets/StreamingAssets/Content/
  manifest.json              every chapter and every glade id, in order
  chapters/<chapter_id>.json one chapter's grids, colours and art keys
  loc/<lang>.json            strings, keyed
```

StreamingAssets rather than Resources: `Resources/` is force-loaded into the
build's serialised blob and can never be patched. These stay ordinary files that
a downloaded pack can shadow.

## The index and the bodies

The catalog is two halves, and knowing which half you are touching is most of
understanding this system.

**The index** (`CatalogIndex`) is built from `manifest.json` alone and is always
resident. It answers identity, order and membership: which glades exist, in what
order, belonging to which chapter. That is everything the boot path needs —
totalling stars, deriving XP, working out where the player is up to, deciding
what is unlocked. At forty chapters and eight hundred glades the manifest is
about 25 KB and parses in well under a millisecond.

**A chapter body** (`ChapterBody`) holds grids, par, colours and art keys. It is
read when the player enters that chapter and evicted when they leave, exactly
like that chapter's textures. `ChapterResidency` keeps two, so stepping back to
the previous chapter on the map does not re-read a file.

The reason this split is load-bearing: the game used to open and parse *every*
chapter on *every* launch. On Android that costs at least one frame per chapter,
because StreamingAssets is only reachable through `UnityWebRequest` there and the
completion callback cannot fire before the end of the frame. Fifty chapters meant
roughly a second and a half of launch spent parsing levels nobody was about to
play, growing forever. It was invisible in the Editor, where StreamingAssets is
an ordinary folder.

Two consequences worth holding on to:

- **The manifest is the authority on membership and order; the body is the
  authority on content.** Nobody writes the manifest by hand — `Glimmer Grove ▸
  Content ▸ Sync Manifest` generates its level lists from the bodies and adopts
  any chapter file that is missing from it altogether. The build gate then proves
  both held, so forgetting to run it fails a build rather than silently hiding a
  glade or a whole chapter.
- **A level's strings are derived from its id** (`level.<id>.name`, `.tagline`,
  `.lesson`) and cannot be overridden. That is what lets the map, the home
  screen's "next up" line and the win overlay name a glade without reading its
  chapter. An overridable key would have made naming something you can only know
  after a file read, and the index would have stopped being sufficient.

## Adding a chapter

1. `Glimmer Grove ▸ Content ▸ Create Chapter Template` — scaffolds the JSON.
2. Author the grids (grammar below). **Leave `par` out** — it is derived from the
   board, so an omitted par can never be wrong while a typed one can. **Do set
   `backdrop`**: a chapter that does not name its own art inherits another
   chapter's, which puts it in another chapter's asset bundle.
3. Place the glades with `mapX`/`mapY`, fractions of *this chapter's* map. Walk
   them upward — each above the one before — and keep them apart; validation
   measures the gap in canvas units against the chapter's strip count and warns
   about collisions, backwards trails and anything crowding the end-of-chapter
   marker. More levels means more `mapStrips`, not tighter packing.
4. Add `chapter.<id>.name` and, per level, `level.<id>.name` / `.tagline` /
   `.lesson` to `loc/en.json`. Missing keys fail validation, which names each one.
5. `Glimmer Grove ▸ Content ▸ Sync Manifest` — adopts the new chapter into
   `manifest.json`, picks an `order` and fills in its level list. Run it after
   *every* content edit.
6. `Glimmer Grove ▸ Validate Content`. It must report zero errors — builds refuse
   to run otherwise.

Nothing in `manifest.json` is written by hand. Sync assigns `order` from the
chapter's id — sparse (10, 20, 30…) so a chapter can be slotted between two that
shipped — and says in the log what it chose; change the number there if it guessed
wrong. **`order` lives only in the manifest**: a chapter body that states its own is
rejected, because where the game goes next must be readable from one file and
changeable by pushing that one file.

A chapter file that never reaches the manifest is the one content mistake nothing
used to catch. Every reader walks the manifest, so an unlisted file is not rejected —
it is never opened, and the drop ships without it behind a green build. Sync adopts
it, and the build gate fails on one that slipped through anyway.

Drop new art into `Assets/Game/Art/…` at any point; it is given an address and
filed into the right bundle group on import. Nothing to remember, nothing to run.

## Token grammar

```
head + arms [+ #colour] + /startRotation [+ !] [+ ~turns]

head   -  conduit    *  heart-crystal    @  sleeping critter    .  empty
arms   any of N E S W, written in the SOLVED orientation
colour R G B, Y=R+G, M=R+B, C=G+B, W=R+G+B, A=any
/0..3  quarter turns clockwise the tile starts away from its solution
!      rooted: the player cannot turn this tile
~1..9  fragile: this conduit crumbles after that many turns
```

Every arm must be mated by its neighbour, and the board with every rotation at 0
must light every critter. The validator proves both.


**Fragile conduits (`~N`)** crumble after N turns and leave a gap. Undo rewinds
the rotation but never mends them, so exploring costs something. The validator
proves each one can still reach its own solved orientation within its count —
a conduit needing three turns but surviving two is an unwinnable level that
otherwise looks perfect.

**Move budget.** Every glade gets one automatically: `ceil(par × 2.6)`, always at
least one turn above the one-star line. Override with `budgetFactor` on a level,
or set it negative to remove the budget entirely. Running out costs a heart.

**Tips teach themselves.** A glade that contains a mechanic the player has never
met shows a one-off spotlight tip on entry — no authoring, no list to maintain.
Adding a mechanic means adding it to `Mechanic.TeachingOrder` and writing
`ui.tip.<id>.title` / `.body`; every chapter that uses it is then covered forever.
Only one tip is shown per glade, most dangerous first.

## Inheritance

A level inherits `accent`, `slate` and `backdrop` from its chapter unless it sets
its own. Prefer inheriting: twenty levels sharing one backdrop is the difference
between a 60 MB game and a 2 GB one. `mapX`/`mapY` are fractions of the
*chapter's* band of the map, not of the whole map, so chapters stay independent.

A *chapter* inherits nothing. It must name its own `backdrop`, and validation
fails if it does not. Two chapters silently defaulting to the same backdrop is
how one chapter's art ends up owned by another chapter's bundle — harmless while
everything is local, a whole extra download once chapters are delivered remotely.
Art that genuinely is shared by several chapters goes in the global group, which
the Addressables tooling works out for itself.

## Companions

The profile roster lives in `manifest.json`, beside the chapter list:

```json
"companions": [
  { "id": "puff", "portrait": "puff", "animated": "c1", "unlockLevel": 0, "disabled": false },
  { "id": "cinder", "portrait": "cinder", "animated": "", "unlockLevel": 1, "disabled": false }
]
```

It is in the manifest rather than a body of its own because the whole roster is wanted at
once — the picker draws the locked ones too — and an entry is a few dozen bytes, so a
hundred companions is a few kilobytes on a file the boot path already reads. A lazily
loaded companion file would add a read to a screen and save nothing.

- **`id` is permanent.** It is written into save files and will key analytics and, once
  the shop exists, purchases. The same rule as a `LevelId`: never renamed, never reused.
- **`portrait`** names a sprite in `Art/Companions/`. Blank means "same as the id", which
  is the usual case. It is a separate field so art can be re-cut without the change
  reaching a save file.
- **`animated`** is optional and names a sprite-set folder under `Art/Critters/`. Only the
  five companions that also appear on a board have one. A still portrait is about 45 KB;
  a flipbook is about 700 KB, which is the whole reason the roster can grow.
- **`unlockLevel`** is a keeper level. Ownership is *derived* from it and never stored —
  the same argument as derived XP — so it can be retuned for existing players.
- **`disabled`** retires a companion without deleting anyone's choice of it.

Adding one is a portrait, a manifest entry and a `ui.avatar.<id>` string. No code changes,
and no app update once remote delivery is on. A companion's name key is derived from its
id and cannot be overridden, exactly like a level's; `Validate Content` checks each one,
because the source scan cannot see a key that is never written as a literal.

The field was added **without raising `ContentSchema.Version`**: an older client ignores
an unknown field and falls back to the roster it shipped with, which is a working game
rather than a refused manifest. Raise the version only for a change old clients could not
survive.

Portraits live in their own Addressables group and load into
`AssetLibrary.CompanionScope` when a roster screen opens, then drop when it closes. Only
the worn companion stays resident, warmed at boot by `Profile.WarmWornAvatar`. That is
what keeps launch costing the same at a hundred companions as at five.

## Progression

`Content/progression.json` holds the XP curve and what a glade pays out. It is content
rather than code for the same reason levels are: rewards get retuned, and a retune must
not need a store review.

```json
{
  "schemaVersion": 1,
  "maxLevel": 500,
  "xpToNext": [100, 150, 200, ...],   // cost of level 1→2, 2→3, ...
  "tailXpToNext": 1250,               // the first level past the authored band
  "tailXpIncrement": 150,             // added per level after that, forever
  "rewards": { "xpFirstClear": 40, "xpPerStar": 20,
               "creditsFirstClear": 30, "creditsPerStar": 15 },
  "chapterRewards": [ { "chapterId": "c01_shallows", "xpPerStar": 15 } ]
}
```

Bands are increments, not cumulative totals, so inserting a band changes one number
rather than every number after it. A chapter override inherits any field it does not
set — `-1` means unwritten, because `0` is a legitimate payout for a tutorial. Bump
`progressionVersion` in `manifest.json` when you change the file, or the refresher will
not pull it.

**XP is derived, never stored.** A player's level is recomputed from their star ledger
on every launch:

```
xp = Σ over cleared glades of (xpFirstClear + xpPerStar × stars)
```

That is what makes it safe to retune this file at all. It also means a replay pays the
*difference* between the old record and the new one, so beating nothing earns nothing
with no rule needed to say so. Credits work the same way, plus the parts that cannot be
derived: `balance = max(earned, high-water) + granted − spent`.

Three things follow that are easy to get wrong:

- **Re-run the seed script after every content drop.** The server derives credits from
  its own copy of the catalog, and a glade it has not been seeded with earns nothing.
  `node firebase/seed/seed-config.mjs`. Nobody loses anything if you forget — the earned
  floor on both sides holds the balance up — but the new chapter pays out only once the
  server knows it exists.
- **Only ever raise a reward, or accept that the floor holds.** Lowering one recomputes
  a smaller value for everyone. `ProgressionStore` and `earnedHighWater` stop anybody's
  level or balance actually falling, but the extra is then invisible to new players
  only. Prefer lengthening the curve to cutting a payout.
- **Never add a payout that is not a function of the record.** A reward for "played
  today" or "watched an advert" is not derivable and must go through `grantedBaseline`
  on the server, not through this file. The daily chests below are exactly that case,
  and show the shape it has to take: the *rates* are content here, the *grant* is an
  identified claim the server adjudicates.

### Daily chests

The optional `daily` block. Three chests, earned by finishing runs and opened by hand
from the home screen.

```json
"daily": {
  "runsPerChest": 3,
  "chests": [
    {
      "guaranteed": [ { "kind": "credits", "min": 60, "max": 90 } ],
      "options": [
        { "kind": "credits", "min": 40, "max": 70, "weight": 45 },
        { "kind": "hearts",  "min": 1,  "max": 1,  "weight": 30 },
        { "kind": "gems",    "min": 1,  "max": 1,  "weight": 25 }
      ]
    }
  ]
}
```

`kind` is a permanent id: `credits`, `gems`, `hearts`, `heart_boost` — and a boost's
band is measured in **hours**. Omit the whole block and the built-in table in
`DailyChestTable.Default` stands; it is deliberately not a schema bump, because a
daily-chest retune must not invalidate the XP curve for clients that have not updated.

A chest pays **every guaranteed band, then exactly one weighted option**. That shape is
not an accident:

- **Nothing is bought, so nothing is a loot box.** These chests are earned by playing and
  can never be purchased. That is what keeps them outside loot-box law in most places
  rather than merely compliant with it. Do not put a price on one.
- **One pick means the odds are a list that sums to 100.** With several picks the true
  odds of an outcome stop being any number written in the file, and the honest disclosure
  becomes a simulation. `Glimmer Grove ▸ Validate Content` prints the published odds, and
  the chest overlay shows them to the player.
- **The guaranteed band is why there is no pity counter.** Every chest pays something
  worth having, so "thirty chests and nothing" cannot happen — which removes the usual
  reason to keep per-player streak state that would then have to merge and be recomputed
  server-side.
- **Later chests must never pay less.** They cost more play. The build gate fails on a
  table where a chest's floor is below the one before it.

**A chest's contents are computed, never stored.** They are a pure function of
(account id, day, chest index) through a specified generator — FNV-1a then xorshift32,
all 32-bit — so the same chest holds the same thing however many times it is asked, on
every device, before and after a crash. Two consequences worth knowing: force-quitting
the opening animation cannot reroll a prize, and the server can work out what a chest was
worth without being told.

**A chest waits for the account id.** The seed is the uid, so a chest opened before the
first sign-in would be re-rolled differently by the server and the player would be shown
one reward and given another. `DailyChests.CanOpen` blocks it until an id exists, and the
panel says why. Anonymous sign-in fires from the splash screen and the id is then in the
save for good, so this only ever trips on a first launch with no connection at all. With
no backend configured the gate lifts, because then nothing is adjudicated.

**Re-run the seed script when you change the block.** `claimAwards` refuses to grant
anything if `config/progression` has no usable daily table — granting a guess would be
inventing money — so a retune that is not seeded stops the chests paying out rather than
paying the wrong amount.

#### The generator exists twice, too

`DailyChestTable.cs` and `firebase/functions/src/daily.ts`, pinned by the
`dailyChestConfig` / `dailyChestCases` vectors in the same shared file. Those vectors use
a **synthetic** table, not the shipped one, so retuning real drop rates does not turn them
red — what is under contract is the arithmetic. The hash constants, the shift amounts, the
stream numbers, the modulo and the summing of same-kind drops are all part of it, and
changing any of them rerolls every unopened chest in the world.

The one that bites: a chest whose floor and whose bonus are both credits must award **one**
summed amount. Both would otherwise carry the id `daily:{day}:{chest}:credits`, the second
would be refused as a duplicate, and the player would be paid half of what the server
grants.

### The rule exists twice

`ProgressionLedger.cs` and `firebase/functions/src/progression.ts` both compute earned
credits, because the client needs it offline and the server needs it to catch a forged
save. They are held together by `firebase/shared/reward-vectors.json`, which both sides
run as a test. Change one without the other and a build goes red rather than the economy
quietly desynchronising.

Three rules keep them identical, and each has a reason: a glade the catalog cannot vouch
for earns nothing (or an invented level id would mint currency), stars are clamped to
three (or a forged record would), and a level id counts once (which the map-keyed wire
format now also enforces structurally).

## Remote delivery

Off by default and fully playable that way. To turn it on, set
`ContentConfig.RemoteBaseUrl` to an HTTPS folder holding the same
`manifest.json` / `chapters/` / `loc/` layout.

The flow is deliberately never on the boot path: the game starts from the cache
or the bundled files, then `ContentRefresher` pulls anything newer into the cache
in the background, and it goes live on the next launch. Bump a chapter's
`version` in the manifest to trigger a refetch. The manifest is written last and
atomically, so an interrupted download can never leave the cache describing files
it does not have.

Version the CDN path itself (`.../v1/`). A future breaking format change is then
served alongside the old one rather than replacing it under live players.

## Assets

All loading goes through `AssetLibrary`; nothing calls `Resources.Load` directly.
Assets have one of two lifetimes:

- **Global** — buttons, icons, critters, the font. Loaded once on the splash, kept.
- **Chapter** — a chapter's backdrop and map strips, plus any backdrop one of its
  levels overrides. Loaded on entering the chapter, **released on leaving it**.

The chapter set is *derived from the catalog*, never hand-listed. The old build
hardcoded `play_0, play_1, play_2` inside the splash screen, so every content drop
needed someone to remember to edit a screen. Now a chapter declares its own art
and `AssetManifest` reads it back — publishing chapter forty touches no code.

The map screen shows **one chapter at a time**. That is what bounds node count and
loaded textures by chapter size (~20 levels) rather than by catalog size, so no
virtualisation or pooling is needed. Arrows at the screen edges step between
chapters.

### Addressing

An asset's address is its path below `Assets/Game/` with the extension dropped:
`Assets/Game/Art/Ui/btn_green.png` is `Art/Ui/btn_green`. `AddressableAddresses`
is the single source of truth for that rule and for which bundle group an address
belongs in; the importer hook, the repair sweep and the audit all read it, so they
cannot disagree.

**Registration is automatic.** `AddressableAutoRegister` is an `AssetPostprocessor`:
anything landing under `Art/`, `Audio/` or `Fonts/` is given its address and filed
into the right group as it imports — on a drag-and-drop, a fresh clone, or a
`git pull` with the Editor closed. Deleted assets have their entries removed.

This used to be a menu item, and that is exactly why it is not one now. A menu
item is a thing a person has to remember during the week a chapter ships, and this
project had already been bitten by that class of bug once: the splash screen
hardcoded `play_0, play_1, play_2`, so every content drop needed somebody to edit
a screen. The migration tool that replaced it then rotted into a no-op — it
scanned `Assets/Game/Resources`, which its own step 3 had deleted — leaving a
repair tool that silently did nothing in a project whose art pipeline depended on
it. New chapter art would have imported fine, validated fine, built fine, and
shipped with no backdrop.

Two menu items remain, and neither is required in normal work:

```
Glimmer Grove ▸ Addressables ▸ Sync All Assets     re-file everything from scratch
Glimmer Grove ▸ Addressables ▸ Audit Addresses     prove every request resolves
```

Use **Sync** after a merge that touched the Addressables settings, or after moving
a backdrop between chapters (which changes who owns it). Use **Audit** any time;
it also runs from the build gate, so an unaddressed asset fails the build instead
of reaching a player. Grouping mistakes — chapter art in the wrong bundle, shared
art claimed by one chapter — are reported as warnings by the same pass.

`GLIMMER_ADDRESSABLES` is defined automatically by the `versionDefines` entry in
`GlimmerGrove.Domain.asmdef` and `GlimmerGrove.Presentation.asmdef` whenever the
Addressables package is installed. `Boot` then selects `AddressablesAssetProvider`.

**Do not put it in Player Settings ▸ Scripting Define Symbols.** Those are stored
*per build target*, so a define added while on Standalone is absent on Android and
iOS — and since the assets do not live under `Resources/`, a mobile build would
ship with no art at all and no error saying why. The asmdef defines it for every
platform at once, which is the point.

### Building a player

**Addressable content must be built, or the player ships with no art.** In the
Editor this is invisible — Play mode defaults to *Use Asset Database*, which reads
assets directly and always works. A device build does not.

Check `Window ▸ Asset Management ▸ Addressables ▸ Settings` and make sure
**Build Addressables on Player Build** is on. If you ever turn it off, you must run
`Addressables ▸ Groups ▸ Build ▸ New Build ▸ Default Build Script` before every
player build. A build made without it launches to a game with missing sprites and
no errors that point at the cause.

Until the define is set, `ResourcesAssetProvider` is used and everything works —
it simply cannot stream or genuinely free memory, because Resources cannot.

### The app icon

The launcher icon is **not** game art and does not live under `Assets/Game/Art/`.
Everything in that folder is forced to a UI sprite by `ArtImportRules` and swept
into an Addressables group; the icon is consumed by the build pipeline instead and
is never loaded at runtime. It lives in `Assets/Game/Branding/Icons/`.

The five files there are generated, not authored. One artwork
(`Tools/IconSource/glimmer_launcher.jpeg`) is the master; `make_launcher_icons.py`
derives every shape the two stores want:

```
python Tools/make_launcher_icons.py          # regenerate the PNGs
Glimmer Grove ▸ Apply Launcher Icons         # write them into PlayerSettings
Glimmer Grove ▸ Validate Launcher Icons      # 37 slots, all assigned
```

| file | used by |
|---|---|
| `icon_master_1024` | every iOS slot, including the 1024 App Store icon |
| `icon_android_adaptive_background_432` + `..._foreground_432` | Android 8+, i.e. every supported device |
| `icon_android_round_512`, `icon_android_legacy_512` | `android:roundIcon` / `android:icon` fallbacks |

Three things about that script are worth knowing before changing the artwork:

- **The master is a rounded badge on black.** Every platform masks the icon itself,
  so shipping the black field would draw a black frame around the real icon. The
  script finds the badge, insets past the glass rim the artist drew along its edge,
  and extends the nearest real pixel outward into the corners. The result is a
  true full-bleed square.
- **The iOS master is written as RGB, deliberately.** App Store Connect rejects a
  1024 icon that carries an alpha channel.
- **The adaptive background is a fitted gradient, not a blurred plate.** An adaptive
  icon's background layer has to cover the area the character stands in front of.
  Erasing him and blurring leaves a ghost of the silhouette that peeks out from
  behind the foreground layer, so the script fits a cubic polynomial per channel to
  the pixels that *are* background and evaluates it everywhere. The sparkles are
  composited back on top; the light rays are not, because they radiate from behind
  him and would end abruptly.

The subject in the foreground layer is fitted to 286 px of the 432 px canvas — just
under the 72 dp every launcher mask keeps — so the crown and the plinth survive a
circular mask.

## Strings

Every player-facing string is a key in `loc/<lang>.json`. The build gate scans the
source for key-shaped literals and fails if any is missing, so a new button with
an unregistered string cannot ship. Keys assembled at runtime defeat that check —
write them out (see `WinOverlay.RankKeys`) rather than concatenating.

## Schema evolution

`ContentSchema.Version` is the contract. A client reads anything at or below its
own version and **skips** — never crashes on — anything above it. Adding an
optional field is not a breaking change; removing or repurposing one is.
`minAppVersion` on a manifest entry hides content from clients too old for it.

``progression.json`` versions **separately**, via `ProgressionSchema`. It is delivered
on its own — the manifest carries a `progressionVersion` so it can be refetched without
touching a chapter — and it changes far more often than the catalog's shape. Sharing one
number would mean a *catalog* format bump invalidated the *reward* file for every client
that had not updated, silently dropping them back to the built-in curve over a change
that had nothing to do with the economy. Two formats, two readers, two versions.

**v2** moved chapter membership and order into the manifest so the boot path reads
one file instead of every chapter. `MinimumSupported` was raised with it rather
than the field being made optional, because a v1 manifest lists no levels at all
and a client that read one would show a game with no glades in it — a clear
refusal beats a silent empty catalog. It cost nothing to do: remote delivery was
still off and one chapter had shipped, so there was no content anywhere to
migrate. The same change made after a CDN goes live is a migration under live
players, which is the whole argument for doing this kind of thing early.

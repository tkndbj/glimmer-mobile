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
  manifest.json              index of chapters; a chapter is invisible until listed here
  chapters/<chapter_id>.json one chapter and all of its levels
  loc/<lang>.json            strings, keyed
```

StreamingAssets rather than Resources: `Resources/` is force-loaded into the
build's serialised blob and can never be patched. These stay ordinary files that
a downloaded pack can shadow.

## Adding a chapter

1. `Glimmer Grove ▸ Content ▸ Create Chapter Template` — scaffolds the JSON.
2. Author the grids (grammar below). **Leave `par` out** — it is derived from the
   board, so an omitted par can never be wrong while a typed one can.
3. Add `chapter.<id>.name` and, per level, `level.<id>.name` / `.tagline` /
   `.lesson` to `loc/en.json`. Missing keys fail validation.
4. Add the chapter to `manifest.json` with a `version` and an `order`. Orders are
   sparse (10, 20, 30…) so a chapter can be slotted between two later.
5. `Glimmer Grove ▸ Validate Content`. It must report zero errors — builds refuse
   to run otherwise.

## Token grammar

```
head + arms [+ #colour] + /startRotation [+ !]

head   -  conduit    *  heart-crystal    @  sleeping critter    .  empty
arms   any of N E S W, written in the SOLVED orientation
colour R G B, Y=R+G, M=R+B, C=G+B, W=R+G+B, A=any
/0..3  quarter turns clockwise the tile starts away from its solution
!      rooted: the player cannot turn this tile
```

Every arm must be mated by its neighbour, and the board with every rotation at 0
must light every critter. The validator proves both.

## Inheritance

A level inherits `accent`, `slate` and `backdrop` from its chapter unless it sets
its own. Prefer inheriting: twenty levels sharing one backdrop is the difference
between a 60 MB game and a 2 GB one. `mapX`/`mapY` are fractions of the
*chapter's* band of the map, not of the whole map, so chapters stay independent.

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

### Turning on Addressables

The abstraction is live; the backend ships inert. The migration is automated —
run the three menu items **in order**, checking the console after each:

```
Glimmer Grove ▸ Addressables ▸ 1 - Mark Assets Addressable
Glimmer Grove ▸ Addressables ▸ 2 - Verify Addresses      ← must pass before step 3
Glimmer Grove ▸ Addressables ▸ 3 - Move Out Of Resources
```

`GLIMMER_ADDRESSABLES` is defined automatically by the `versionDefines` entry in
`GlimmerGrove.Domain.asmdef` and `GlimmerGrove.Presentation.asmdef` whenever the
Addressables package is installed. `Boot` then selects `AddressablesAssetProvider`.

**Do not put it in Player Settings ▸ Scripting Define Symbols.** Those are stored
*per build target*, so a define added while on Standalone is absent on Android and
iOS — and since the assets no longer live under `Resources/`, a mobile build would
ship with no art at all and no error saying why. The asmdef defines it for every
platform at once, which is the point.

What the steps do:

- **1** gives every asset an address equal to its old Resources path
  (`Art/Ui/btn_green`), labels the animation-frame folders so `LoadAll` still
  works, and files chapter art into its own group so a chapter bundles — and
  later downloads — as a unit.
- **2** checks every address the game will *ever* request, built from
  `AssetManifest` plus the catalog, against what is actually marked. This is the
  step that catches a missing asset before a player does. Do not skip it.
- **3** moves the folders out of `Resources/`. Addressable entries follow an
  asset's GUID and the addresses are stored explicitly, so the move cannot break
  them.

Reversible at any point: remove the define and the game falls back to
`ResourcesAssetProvider`. Note that after step 3 the files are no longer under
`Resources/`, so a rollback means moving them back too.

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

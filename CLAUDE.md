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
5. **Omit `par` when authoring.** It is derived from the board. A typed one can drift.
6. **All player-facing text is a loc key.** The build gate scans the source for
   key-shaped literals and fails on any that is missing. Do not build keys by
   concatenation — write them out (see `WinOverlay.RankKeys`).
7. **All asset loading goes through `AssetLibrary`.** Never call `Resources.Load` or
   `Addressables` directly. Never hand-list asset paths — derive them from the
   catalog via `AssetManifest`.
8. **The map shows one chapter at a time.** That is what bounds node count and texture
   memory by chapter size instead of catalog size. Do not "improve" it into one long map.

## Layout

```
Assets/Game/Scripts/Domain/        GlimmerGrove.Domain       (no UnityEngine.UI)
  Board/ Content/ Persistence/ Progression/ Localization/ Analytics/ AssetPipeline/
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
layering, EditMode test suite.

Not done, both deliberate: **cloud save** (needs a backend decision) and a **visual
level editor** (tooling — the thing most likely to matter next for shipping cadence).
Remote content delivery is built but switched off; set `ContentConfig.RemoteBaseUrl`
to enable.

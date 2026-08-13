# Glimmer Grove

A relaxing colour-mixing puzzle game for portrait mobile. Unity 6000.5.4f1.

Turn conduits to carry light from heart-crystals to sleeping critters. There is no
timer and no fail state — only the quiet pleasure of a network snapping into place.

## The twist

Every group of connected tiles carries the **additive mix of every crystal inside it**.

| network contains | light |
|---|---|
| red | Ember |
| green | Verdant |
| blue | Azure |
| red + blue | Blossom |
| green + blue | Tidal |
| red + green | Sunfire |
| all three | Radiance |

A critter only wakes to the exact colour it dreams of, so the real puzzle is not
"connect everything" — it is deciding which networks must stay **apart**.

## Levels

| # | Name | Grid | Par | Teaches |
|---|---|---|---|---|
| 1 | First Light | 5×5 (21 tiles) | 34 | turning, connecting, one white heart |
| 2 | Twin Streams | 6×6 (32 tiles) | 49 | two colours that must never touch, rooted tiles |
| 3 | Prism Heart | 6×7 (42 tiles) | 71 | six hearts, blended requirements (blossom + radiance) |

Stars: 3 at ≤ par×1.35, 2 at ≤ par×2, otherwise 1. Three hints per level; each turns
one conduit into place and costs 2 moves.

The layouts were generated as random spanning forests and machine-verified — every
arm mates with a neighbour and every critter is satisfied in the authored solution.
`Glimmer Grove ▸ Validate Levels` re-runs that check inside the editor.

## Screens

| Screen | What it does |
|---|---|
| **Splash** | Real preload: every sprite, clip and generated texture is pulled into memory while the logo lights letter by letter. Nothing is faked — the bar tracks actual `Resources.LoadAsync` progress, so the first tap on PLAY never stutters. |
| **Home** | Clash-Royale-shaped hub: player card with rank and XP, hearts/coins/gems, a grove-awakening progress bar with milestone chests, a poke-able hero on a floating island, and a big PLAY. |
| **Glade map** | A tall island chain you drag through. Every glade — locked ones included — sits on its own floating rock, joined by a drifting light trail. Opens at the bottom and glides up to whichever glade is next. |
| **Play** | The puzzle board, HUD, undo / hint / restart. Each glade has its own vivid backdrop and slate colour. |
| **Pause, Settings, How to play, Coming soon, Victory** | Modal panels. |

`NavBar` (shop / items / home / ranks) is shared by the hub and the map. Screens keep
their content above `NavBar.Height`; the map does it by insetting the scroll viewport,
so nothing is ever hidden behind the bar.

Button labels and glyphs are lifted by `UIKit.PillFaceLift` / `SquareFaceLift` — the
jelly art has a moulded base under its lit face, so text centred on the raw rect reads
low. The fractions were measured off the sprites, so labels stay optically centred at
any button size or screen resolution.

### What is real and what is a placeholder

Rank, XP, the grove progress bar and the milestone chests are all derived from
stars you have actually earned. **Hearts, coins and gems are placeholders** — they
persist, but nothing spends or awards them yet. Everything reads them through
`Profile`, so building the real economy means changing that one file. Shop, Items
and Ranks open a proper Coming Soon panel rather than doing nothing.

## Running it

1. Open the folder in Unity Hub (6000.5.4f1).
2. Open `Assets/Game/Scenes/Glimmer.unity` and press Play.

The scene is deliberately empty. `Boot.cs` runs on
`[RuntimeInitializeOnLoadMethod]` and builds the camera, canvas, audio and first
screen in code, so the game boots correctly from any scene and there are no prefab
references to go stale.

Menu items under **Glimmer Grove**: *Set Up Project*, *Validate Levels*,
*Validate Art*, *Build Windows Player*.

## Layout

```
Assets/Game/
  Scripts/Core/     Boot, Tween, Art, Audio, Save, Profile, UIKit, Widgets, Scenery, Flow, Pal
  Scripts/Game/     Puzzle (model), Levels (data), TileView, BoardView
  Scripts/Screens/  SplashScreen, HomeScreen, LevelsScreen, PlayScreen, Overlays
  Scripts/Dev/      ShotDirector (compiled only with the GLIMMER_SHOTS define)
  Editor/           ProjectSetup, DevBuild
  Resources/        Art (Bg, Ui, Map, Critters, Fx), Audio (Music, Sfx), Fonts
```

`Puzzle` is pure model — grid, rotation, union of connected groups, additive colour
per group, BFS depth from the nearest crystal. `BoardView` owns presentation and uses
that depth to ripple the light outward and to sequence the wake-up chimes up a
pentatonic ladder, so consecutive critters lighting sounds like a melody.

Conduits, glows, sparks, rings and crystals are generated at runtime as
signed-distance-field textures (`Art.cs`), which is why they stay crisp at any board
size and can be tinted per colour without an atlas.

## Glade themes

Each `LevelDef` names a `Backdrop` sprite and a `Slate` colour. `Pal.BoardTheme.From`
derives the board floor, slot, conduit and hub greys from that one colour, so a new
level only picks a backdrop and a tint and everything else follows.

| # | Backdrop | Slate |
|---|---|---|
| 1 | sunlit glade (green / gold) | `#123640` |
| 2 | tidal lagoon (azure / cyan) | `#0F2A4A` |
| 3 | blossom dusk (violet / magenta) | `#241540` |

The backdrops are generated gradients with soft colour blooms and a faint blurred
wash of the island art for texture — clean enough not to fight the board, saturated
enough for the glowing conduits to sit on.

## Level format

One token per cell in `Levels.cs`:

```
-NESW/2      conduit with those arms, started 2 quarter-turns off solution
*W#R/1       heart-crystal emitting red
@N#M/3!      critter needing blossom (red+blue), rooted (cannot be turned)
.            empty
```

Colours: `R G B  Y`(R+G) `M`(R+B) `C`(G+B) `W`(all) `A`(any). Arms are written in the
**solved** orientation, so a level is correct by construction.

## Assets

The glade map is a tall slice of the island-map pack cut into three 1080x1200
strips (a single 1080x3600 texture would be downscaled by the importer). Floating
rocks and scenery come from the isometric island tileset; coins, hearts, gems and
chests from the jungle platformer set; panels, buttons and glyphs from the casual
UI vector pack; the home and splash backdrop from the jungle background layers.

Art and audio are from the CraftPix packs in `Downloads/2D ASSETS` (see each pack's
`license.txt`). Sound effects were trimmed, de-silenced and normalised on import;
music is streamed.

**Before shipping:** `Assets/Game/Resources/Fonts/GameFont.ttf` is a copy of Segoe UI
Black from this machine. It is Microsoft-licensed and **not redistributable**. Swap in
a licensed display face — [Riffic](https://www.dafont.com/riffic.font) is the one the
UI pack was designed with — keeping the same filename, and everything else stays put.
If the file is missing the game falls back to Unity's built-in font.

## Building

`Glimmer Grove ▸ Build Windows Player` writes `Builds/Win/GlimmerGrove.exe`.
For Android/iOS switch platform in Build Settings; the project is already set to
portrait-only, 1080×1920 reference, `com.digikeygames.glimmergrove`.

Input uses the legacy Input Manager and `StandaloneInputModule`.

## Adding a glade

Append a `LevelDef` to `Levels.All`. `MapPos` is `(x across, y up the chain)` in
0..1, so the map places it automatically; raise `MapHeight` in `LevelsScreen` if the
chain needs more room. Run **Glimmer Grove > Validate Levels** to check the layout
mates and solves before you play it.

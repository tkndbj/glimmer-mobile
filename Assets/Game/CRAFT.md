# Craft — how Glimmer Groove is drawn, sounded and generated

The companion to `CLAUDE.md`. That file holds the **invariants** (rules that must never be broken), the
current state and the numbers; this one holds the craft — the generators and checkers, how each mode looks
and sounds, and the house rules for the UI. Everything here was paid for by a play report or a shipped bug,
and every rule is written as the rule plus the specific failure that bought it. `CLAUDE.md` is the
authority where the two touch.

## Tools

Everything here runs without Unity unless it says otherwise.

- **Tablet layout:** there is no offline gate for a screen's geometry, so it is measured in the Editor —
  build a screen into a world-space `Canvas` sized to `CanvasFit.WidthFor/HeightFor(w, h)`, call `View.Init`
  by reflection, force every `localScale` to one (entrances start at zero and are still queued),
  `Canvas.ForceUpdateCanvases()`, then read `GetWorldCorners`. That found the hub's 176-unit overlap and
  proved it gone; it needs no play mode and takes a second. `1080x1440` is the old 4:3 tablet, `1620x2160`
  the new one, `1080x2340` the phone everything was tuned on.
- `Tools/verify/` — `compile.py`, `tests.py`, `content.py`, `loc.py`, `names.py`, `sfxnames.py`,
  `difficulty.py`, `fall.py`, `prism.py`, `siege.py`, `proto.py` (every prototype mode in one file,
  because they share a shape rather than a rule), and two shared contracts: `board-vectors.json` and
  `fall-vectors.json`. The prototype modes have no vector file: they are pinned
  **inline** by `ProtoLadderTests`, which is the only guard that actually runs offline (invariant 29e).
  `bud.py`, `march.py`, `ember.py` and `bud-vectors.json` went with their modes (38).
- `Tools/chapters/*.py` — one module per chapter; regenerates the shipped JSON and `--check`s itself
  against it. `author.py` is the shared glade board DSL (`cross`, `root`, `briar`, `path`) and derives a
  taproot's start rotations from the taps the root should cost rather than leaving four numbers to agree.
  Non-glade: `f01_lightfall.py`, `f02_glasswater.py`, `f03_whorlwater.py`, `p01_prismvale.py` and
  `s01_thornwatch.py`. For Lightfall and every mode dealt by seed the *shape* is drawn by hand and only
  the **fill** is swept — which blend or colour stands where, which blend stands beside a whorl, what the
  board is dealt — the cheap half of the search and the half that decides how a board plays. `*_strings.py` hold the strings belonging to a mode rather than to a level.
- `Tools/hollow/` — the Hollow's rule mirror, board generator and `build_chapter.py`. The mirror is never
  authoritative; the shipping C# solver is what `Validate Content` runs.
- `Tools/render_wheel.py` — draws the bonus wheel exactly as `WheelFace` does, without Unity, for the one
  thing about it whose quality is only visible as a picture. It found four faults in one pass: near-black
  wedges, a ramp putting five of eight slices on one colour, two warm rungs darkening to olive, and a
  jackpot the same gold as the rim.
- `Tools/grove_art_facts.py` — writes each grove piece's `w`/`h` and its `hit` mask (a cell per sixteen
  art pixels) into `homestead.json`, and each companion's into `manifest.json`, from the shipped PNGs;
  `--check` proves the content still describes the art it ships, and `content.py` runs it. Run it after
  any re-cut of grove or companion art. **It took `facings` everywhere except in the one caller that
  had to pass it**, so for a year every turnable piece was reported as having no art at all and the
  writer left its `hits` alone — `--check` red over a perfectly correct catalogue, hidden because
  `content.py` is the gate that actually runs and reads the row's facings itself. A repair tool is run
  exactly when something is already wrong, which is the worst moment for it to be the thing that is.
  `Tools/render_grove.py` draws a grove exactly as the game does — ground layer under piece layer,
  footprints, authored sizes — and is the fast loop for anything judged by eye; re-verify it against a
  play-mode screenshot after touching `GroveTileArt` or `GroveFieldView`. **`--screen` draws the
  Grovement rather than the grove**: `home_sky` enveloped at `Cover`'s own 1.06, the vignette as the
  *ellipse* a square sprite stretched over a phone really becomes, `Scenery.Sun`'s three layers to the
  pixel, and the header fade over all of it, with `--notch` adding what a cutout takes. Without the
  fade it would be drawing a screen this game does not have, and the number it settles — is the sun
  under the banner — is exactly the one that needs it.
- `Tools/make_grove_art.py` — the catalogue itself, rendered from the committed CC0 models.
  **The light is a sun and a sky rather than one grey** (invariant 16u): `SUN` is over-unity so a lit
  face is drawn brighter than the pack painted it, `SHADE` is three per cent cool and no more, and
  `PIECE_CHROMA` pays for the lift in saturation. The ground is **turned** toward grass by `FLOOR_HUE`
  / `FLOOR_TURN` before it is lifted, because the pack's green is a yellow one and brightness cannot
  answer a hue. Every one of those touches the three colour channels and never alpha, so a full re-cut
  moves no size, no footing and no hit mask — which `Tools/grove_art_facts.py --check` is what proves.
- `Tools/grove_art.tsv` + `import_grove_art.py` — one row per grove piece (source, permanent id, slot kind,
  price, scale, lift, name). Copies the art, writes the loc string, regenerates the catalog, bumps
  `groveVersion`. It **refuses to remove an id it imported before**, because a piece id is in save files.
- `Tools/make_chapter_art.py` + `chapter_art.tsv` — the map strips of **one ordinal's** map, cut bottom
  upward out of one tall painting. `-` in the source column says this chapter draws a map some other row
  cut, which is what almost every row says now: a map is cut once and drawn by every mode's chapter at
  that ordinal (*Chapter art*, below). **Two ramps, not interchangeable**: `night` grades a *map*,
  because a map is the thing being looked at; `vivid` grades a *sky*, because a board is drawn over it.
  Only `vivid` is used today — `night` existed to tell two chapters sharing one painting apart, and
  chapters at one ordinal are meant to be the same place.
- `Tools/make_sky_art.py` — the forty board backdrops, `sky_00`..`sky_39`: one soft cloud painting at
  forty colours, dealt round the wheel with a stride so no two levels in a row are close in hue. It
  imports `vivid` from `make_chapter_art` rather than copying it. `--check` proves the shipped skies are
  what it cuts; `--only N` cuts one, for judging a change to the ladder.
- `Tools/make_shop_art.py` — the shop's two money ladders, cut from two licensed sheets. The background is
  keyed by **chroma rather than brightness**: both sheets put a soft coloured halo behind every object and
  the halo overlaps the objects in brightness completely, but it is the ground's own hue scaled up, so it
  lies along one axis in RGB. The edge threshold finding a silhouette is **deliberately low** — a silhouette
  open anywhere is not one, the fill drains out through the gap and the whole interior is lost, which is far
  worse than a little speckle, and it is safe because a smooth halo carries brightness but no gradient.
- `Tools/make_waterfall.py`, `Tools/make_grove_animation.py` — generated decor flipbooks. Rows they own are
  marked `_generated` rather than `_imported`, or the next import run warns forever about a row it no longer
  owns.
- `Tools/make_name_blocklist.py` — vendors LDNOOBW (27 languages, CC-BY-4.0).
- `Tools/make_sfx.py` + `sfx.tsv` + `sfx_dsp.py` — the twenty sound effects, cut from a licensed pack; one
  row per name the code plays. The DSP is split out so the cut can be proved without a 384 MB pack on disk.
  `Tools/sfx_meta.py` writes the twenty `AudioImporter` blocks **preserving each GUID** — Addressables keys
  on the GUID, so a regenerated `.meta` silently unaddresses every sound in the game.
- `firebase/seed/seed-config.mjs` — publishes `config/progression`, `config/products`, `config/grove`,
  `config/names` from the content files. `moderate-names.mjs` is the moderation desk.

## Chapter art

**A chapter's art is a rule rather than a decision, and that is the second time this file has had to
learn that a per-chapter choice is a per-chapter divergence.** Nine chapters chose their art nine ways:
six source paintings, four map cuts and two borrowed-and-regraded ones, forty-one backdrops of which a
whole Lightfall chapter's ten levels shared **one**. Every individual choice was defensible and the set
was not a set — the modes did not read as one game, and adding a chapter meant picking a pack, adding a
row and cutting eleven textures. Two functions replaced all of it (`Tools/chapters/mapart.py`):

- **The map belongs to the chapter's ordinal inside its own mode.** Every mode's first chapter draws
  `map1`, every second draws `map2`. Strip counts stay a fact about each painting (`mapart.STRIPS`: 6, 4,
  5, 6), because `make_chapter_art.py` scales a source to *whole* strips and a count that leaves it
  narrower than 1080 stretches the map sideways.
- **And so do the nodes, because they stand on it.** `mapart.SEATS` is one set of ten seats per
  *painting*, generated by `Tools/make_map_seats.py` from the road each one draws. A node used to stand
  on a floating tile at a serpentine spaced down the map, which ignored the painting entirely — the
  packs these are cut from are advertised the other way round, with the disc sitting on the road. The
  mode used to be told apart by that tile and is now told apart by its accent alone, which is a real
  loss and is affordable while one mode ships (invariant 8e).
- **A seat on `map1` may be *afloat*, and only there.** That painting is an archipelago: it draws
  paths across its islands and a pale current down the water between them, so half its chain stands
  on ground and half is moored on a tile (`LevelsScreen.PerchArt`, one sprite for every mode). The
  three road maps are continents and moor nothing. `--contact` draws both kinds, which is the only
  way to see that a tile is on the current and not on the foam ring round an island (invariant 8f).
- **The sky belongs to the level's place in its chapter.** Forty of them, `sky_00`..`sky_39`, ten per
  ordinal: one soft cloud painting at forty colours, dealt round the wheel with a stride so no two
  levels in a row are close in hue (`Tools/make_sky_art.py`).

Three things fall out. **A chapter published next year costs no art at all** — it names an ordinal.
**Shared art files itself**, because `AddressableAddresses.ChapterOwnership` puts an address two
chapters want in the global group. And **`accent`/`slate` stop reaching the backdrop entirely** and go
back to being only what they should have been: the board's own light and its plate.

The strip counts and the borrowed-map machinery are what this replaced. `chapter_art.tsv`'s fourth
column (`night`) survives and is used by nothing: it existed to make two chapters sharing one painting
read as two places, and chapters at one ordinal are now meant to be the same place.


**A board backdrop is graded in daylight, and the board is what makes that safe.** All forty-one backdrops
used to land between 28 and 105 mean luminance out of 255, and it was reported exactly as it was: every
level of every mode is dark. Nothing was wrong with any one number and the picture they made together was
never looked at. It can simply be reversed because **the backdrop is not what a tile is read against** —
every mode draws its board on an opaque plate, so brightening behind it *widens* the separation. What is
still dark is the **board plate**, deliberately: the tiles, motes and flowers on it are bright saturated
shapes, so their ground is what the backdrop is free *not* to be.
<br>**Brightening was only half.** The first answer reduced the source to luminance and mapped it through a
slate-to-accent ramp — correct about brightness and still a **duotone**, so every pixel held one hue and most
boards were a painting seen through an amber gel, reported as *is there a yellow overlay on the background?*
There was not; the colour was destroyed in the art tool before a screen could show it.
<br>**The second answer was rejected and then asked for, and the difference between those two moments is the
whole lesson.** It kept the picture's own hues but rotated them onto the level's accent — and shown unasked,
over art the owner liked, it was correctly read as another tint: *why did you change the background itself?*
The tint came out, the art arrived in its own colours, and *then* the ask was made — make them pink, purple,
various cheerful colours. Same code, opposite verdict. **A recolouring is a tint when it is substituted for
the change that was asked for, and a feature when it is the change that was asked for.**
<br>So `vivid` turns a picture onto a target colour. It was the level's own `accent` while every chapter
cut its own backdrops; it is now the sky's place in the forty-colour ladder (*Chapter art*), which is the
same code answering a better-posed question — the spread no longer depends on how varied a chapter's authored
accents happened to be. Four rules keep the painting rather than replace it, each arrived at by getting
it wrong first: **a constant hue offset, never a pull toward a target** (a fraction of the way to one
destination is the duotone by a slower road); **saturation is a multiply, with no floor and no cap** (a floor
pushes a white cloud up to meet its blue sky and a cap pulls the sky down to meet the clouds, and both
flatten exactly the contrast the picture was bought for); **`CLOUD_BLEACH` moves the pale end down**, a gamma
above 1 that is the inverse of that floor and safe because it is a curve rather than a clamp, applied
*before* the gain measures the picture; and **the brightness lift runs in V alone, never on the three RGB
channels**, because a per-channel gamma raises the *smallest* channel proportionally most and so lifts a
colour toward grey as a side effect of lifting it toward light.
<br>**Removing the tint uncovered a bug the tint had been hiding.** Three of the eight sources in
`chapter_art.tsv` are *overlay layers* out of layered packs, opened with `Image.open(...).convert("RGB")`,
which drops alpha and leaves whatever RGB sat under it — so most of two chapters was undefined white paper
with a few branches on it. The duotone could not show it because it threw the colour away and remapped
luminance, and every gate was green because none of them opens a PNG. `make_chapter_art.opened` composites
instead of flattening, a source may be a `+`-joined **stack** of layers, and it **errors** on a stack whose
bottom layer is not opaque. The general lesson, recorded twice for art: a bad *cut* is a statistic and a bad
*source* is a judgement.

## The palette

**The wheel is paint, not light, and that is a look rather than a rule.** `Energy` still mixes by `|` over
three bits — boards, letters, searches, par and vectors all untouched — and `Pal.EnergyColour` is the one
place that says what each of the seven masks is *painted*. It used to be additive (red, green and blue
blending to yellow, magenta and cyan), which is exactly right for light and is the one colour arithmetic
nobody outside a graphics pipeline has been taught; it was reported as confusion rather than as a bug, a
player mixing red and blue expecting purple. So the middle channel is drawn **yellow** and the blends fall
out of the wheel a five-year-old knows: red+yellow **orange**, red+blue **purple**, yellow+blue **green**.
All three still meet at `Radiance`, which paint does not do, because white here is not a colour — it is the
*finished* state, and a muddy brown would be arithmetically honest and say the opposite. It was a one-file
change because every mode asks `EnergyColour` and both teaching legends derive their chips from the same
masks. Two consequences: the authored letters **`Y`, `M` and `C` still name the masks** and are not renamed,
being in every shipped chapter and three offline mirrors; and the warm three are now genuine neighbours on
the wheel where red, green and blue were not, so they are separated by **value** as well as hue (Poppy deep,
Marigold mid, Pollen bright) — the old palette was the friendlier one for red-green colour blindness, and
nothing on a glade tells two channels apart except tint, so that is the price of the change.


## Modes

**Thornwatch is the front door** — the first row of the switcher and what a map with nothing
remembered opens on. That is one decision in one written list (`LevelModes`, invariant 38a), not a
sort and not a per-screen default, so the control and the map cannot come to disagree about which
mode a player meets first. The blurbs below are in shipping order rather than switcher order,
because that is how they read; the switcher's order is `siege`, `prism`, `glade`, `fall`.

**Classic glade** — `PlayScreen`. Turn conduits, light every critter. The move budget is the only fail
state (22) at `par × 1.60`, and `Undo` refunds a move, so exploring a crossing that reads the same half a
turn round costs nothing.
<br>**The solve is five beats and the choreography is the board's own shape**: a **hush** (the grove draws
in and dims — the beat most often left out because it looks like nothing happening, and without it the
celebration begins while the player is still reading their own last move); the **surge**, where light walks
out from the heart-crystals along `Puzzle.Depth` a ring at a time and a critter flinches where it stands as
the wave reaches it, so the order the grove comes alive in is the order the player's own wiring feeds it;
the **bloom**; then it **settles** before the panel covers it. It replaced a *sweep* — every tile
brightening at a delay proportional to depth — which could be played over any grid at all. **The jump
belongs to the bloom and nowhere else**: the wake used to leap too and was reported as *two different
jumps*, because repeating a gesture a second later does not reinforce it, it spends it. No confetti and no
haptic, by request. Every duration is `GladeFanfare` (Domain) because the length is a function of the
board: the rate gives way (`SurgeCeiling` 1.35s) and a floor stops it becoming a blur (`MinRing` 0.05s).
<br>**A won glade says so twice, and the first is what protects the heart.** `BoardView.OnWon` fires when
the model settles, `OnSolved` when the celebration ends. The screen used to resolve on the second, so for
the whole celebration a solved glade was recorded as a run in progress: a process killed there charged a
heart at the next launch, and backing out forfeited a board the player had beaten. The fix is not a shorter
celebration; it is closing the window where the outcome is *known* rather than where it is announced.
`_awarded` keeps the payout exactly once, because `Finish` used to guard on `_finished` and moving that
flag earlier without splitting it would have made the payout unreachable.

**Lightfall** (`FallScreen`) — a well of coloured motes to empty, and an ordered procession to empty it
with. Tap a column: the mote either **enriches** the top of that column (a colour it lacks, and the stack
does not grow) or **heightens** it — two notes of one instrument (`free` and `rotate_a`, the same block of
wood a fifth apart, the upper note for the good outcome), because they were once a bell and a wooden clunk
and the commonest good thing in the mode had a metal dong under it. **Before giving a mode's moment a
sound, check it is the same material as the rest of the set, and where two outcomes are a pair, make them a
pair.** A mote holding all three channels **bursts** and washes the colour that finished it into the motes
beside it, so one well-chosen drop runs through a whole blob and reaches a mote buried where no drop could
land — which is what makes a full well solvable at all. The second chapter brings the **lens** and the third
the **whorl** (26f, 26g, 26h) — the only place two *motes* are combined, and the only thing here that moves
one. Two fail states, both visible: the supply runs out, or a mote rests above the
**brim**; only the first may be sold a continue (26b). Par is the fewest drops that empty a well without
breaching the brim (`FallSolver`, resolved lazily); boards are searched for, not typed.
<br>The cascade is drawn as it happened rather than reconstructed: a drop settles before a frame exists, so
`FallStep` carries what burst, what glass was *charged*, what *fired* and every beam thrown — the charging
half especially, because two thirds of what the player does is filling a lens. `FallTempo.ShotBeat` gives a
wave with glass in it a beat of its own and `ShotCeiling` bounds what a cascade may spend on them, which is
a bound rather than a preference, because the board is latched while a wave plays.

**The prototype level shape** (`ProtoView`, `ProtoScreen`) has now carried **twelve** modes and every
one of them was commissioned to be played and judged (invariant 29). Eleven were withdrawn — Nectarrun,
Ribbonfall, Seedfling, Warrenwake, Deep Orbit, Moonwake, Toppleglen, Nova Raid, the Iron Quarry,
Hollowmarch and Emberforge — and each took its screen, view, board and ids with it and left the plate,
the grid, the latches and both endings exactly where they were. **That is the seam's whole argument,
made eleven times.** What a mode supplies is a board and a look; what it inherits is everything about
being a *run*. Prismvale is the one still standing on it.

**Groovekeeper is retired** and its screen, view and rules are gone (invariant 28). What is worth keeping
from its look is one paragraph: **the mode had no propagating event, and that was the whole of what was
wrong with it.** Every other mode has something that *travels*; that one laid a tile, opened a flower and
stopped, so there was nothing on the board for a celebration to be *about* and every attempt came out as
decoration around a single cell. The fix that worked was not a bigger effect but a **path** — light walking
outward through the seams the player had arranged — and the general rule it leaves behind is that a
flourish ladder has to **escalate in kinds rather than amounts**, because one picture at five sizes is a
number going up and *a number going up is not something anybody sees*.

**Hollow** (`HollowScreen`) — a field of sleeping critters and a short *ordered* queue of sparks. Light
accumulates and never decays, so a player can never be stuck, the only endings are winning and running out,
and unlimited undo is safe. Par is the fewest sparks that finish the board (`HollowSolver`), never authored.

**Budburst, Hollowmarch and Emberforge are deleted** (invariant 38), and their screens, views, boards,
validators, art and offline mirrors went with them. What survives is everything that was a *seam*: the
prototype level shape, `StoryScreen` and the story band (30d), the village world of backdrops (30e), the
cast and explosion art's lesson about inheritance, and the rules below — which were paid for by playing
those modes and are the reason this section exists at all.

**A charm is dealt on a *window*, not on a chance, and that is a drawing fact as much as a rule.**
Everything a player is shown about the charms — the halo, the mark, the lesson that rings one — is
worth nothing on a board that never deals one, and a rate rolled per gem does exactly that on some
board, for ever, because this stream is deterministic (CLAUDE.md 37ci). The rung that *introduces*
the prism was such a board. **The presentation rule that falls out of it: before tuning how a rare
thing looks, check how often it is actually seen — on the boards that ship, not on average.**

**The charms, and the one drawing rule they are all built on: a charm has to say two things at
once.** A charmed gem is still worth its colour — that is the whole of what a match is for in this
mode — so a mark that hid the jewel would be a gem the player could no longer aim (invariant 37f).
Three things follow and each was a decision. The marks are **white with a dark keyline and a hole
cut in the middle**: white because nothing saturated reads over four saturated jewels; the keyline
because white on the amber gem is otherwise a smudge, which is the worst pairing this board can
produce and the one `render_siege.py --charms 27=lance` exists to look at; and the hole because a
solid mark in the face of a jewel is a jewel with its face painted out. The **halo behind** is the
second reading, tinted to the gem's own colour and never white, and it is what makes a charm
findable from across a board that also has a hill walking down it — a mark is forty pixels of
drawing and a halo is the only part visible in peripheral vision, which is why it is the one thing
on this field that *breathes* while nobody is playing. And the **prism is a face rather than a
mark**, because its whole sentence is "this is not one of the four" and a mark on a coloured jewel
would be saying the opposite.
<br>**What each one looks like going off is the pack's own impact reel in the colour it was paid**,
which cost no bake at all: four elemental impacts are already cut from `UniqueProjectilesVol5` for
the bolts that land, already addressed and already resident on every siege, so a charm going off in
fire, venom, ice or lightning is the real motion for the price of four literals. A fifth reel baked
for this would have been twelve more textures saying what these already say. **The colour is the
payoff** — a prism is drawn colourless and is worth the run it completed, so its burst is the one
moment the player's choice is visible, and drawing it in a fifth colour nothing else wears would say
the opposite.
<br>**A lance's cross is stretched along its own length, which is the one place in this project
stretching is right.** Invariant 37au forbids scaling a picture of a *place* in x and y
independently; a beam is not a place, it is a thing of variable length, and `beam.png` is drawn with
a hot core across and no edge at all along its length precisely so it can be. A bar with hard ends
reads as a plank. It opens from the middle out rather than fading in, because what a lance does is
*go* somewhere, and a head runs out along each of the four arms so the row does not simply appear.
<br>**A stormglass is two moments and they are two moments in the model as well** (invariant 37s):
the gathering, drawn where the gem was, and the volley, which arrives a beat later off the board's
own report because the bolts are booked exactly as a match's fuel is. Up to fifty of them land
inside half a second, so each ward **kicks once** whatever it threw, the two bolts the charm's own
colour fires are **gathered into one ray** (two rays down one line a frame apart are one ray at
twice the brightness), and only a *kill* gets the full impact — a flipbook, a ring and sparks fifty
times over is the busiest moment in the mode drawn forty times too many.

**The classic glade and Lightfall are *hidden* rather than deleted** — `"disabled": true` on their
manifest entries and nothing else. Their screens, views and blurbs above are all still true and still
compiled; turning them back on is seven booleans.

### What a moving board has to get right

Every rule below was paid for by a play report on Budburst, whose chain was the most complex thing this
game ever drew. **That mode is deleted (38), so the class and method names below name code that is no
longer here** — they are kept as the record of what each rule cost, because the rules generalise to
anything that animates a board and the next one will meet them again.

- **A chain is a score, written out first and walked against one clock** (`BudStage`, `PlayChain`).
  Independent tweens each working their delay out of their own share of a beat time causally related events
  by arithmetic that never met — replaying the shipped ten against the old schedule, **seven of ten dropped a
  flower into a hole before the burst that made the hole had been drawn**. Four rules hold over the score,
  each an inequality over `BudCue.At` and each a test in `BudStageTests`: **nothing is drawn before its
  cause** (the exact causal set, not a proxy — and a hole is bursts *and* cocoons opening, not `burstAt`
  alone); **one gravity** (a fall's length is its distance at a fixed pace and nothing may clamp it, or the
  board falls at three speeds at once and reads as skipped frames); **a column collapses from the bottom**;
  and **the ceiling is met by squeezing the slack, never the falls**.
- **A cell that a piece has fallen out of has to stop drawing it, and nothing in the model can ever say so.**
  A settled board holds the position the move *ends* in and has no opinion about anything being in mid-air,
  so only the cue that moved it knows both ends (`BudView.EmptyCell`, off `BudDrop.From`). It reads as the
  piece standing perfectly still while a copy of itself travels away from it, with every gate green.
- **A thing that is leaving and a thing that is arriving may not share a transform**, or anything that
  repaints that cell kills the departure where it stands and leaves the piece oversized and leaning for the
  rest of the run. Two corollaries: a repaint that assigns a scale and says nothing about a rotation is half
  a repaint, and `Tween.KillAll(cell.Bud)` cannot stop a breathe owned by the `Transform`.
- **One tween moving several transforms must be owned by one they all hang from** (`Cell.Piece`), because a
  tween dies with its owner and a killed one never reaches its `OnDone` — leaving every other picture it was
  moving stranded between two squares.
- **A stagger and a duration are one bound, not two.** A ripple delay *added* to a fall leaves a piece still
  travelling after its wave has ended; it is spent *out of* the fall instead. And a wave must be dealt one
  thing at a time — `min(nth × step, most)` clumps the tail of a big wave into one frame, which is exactly
  the flicker a stagger exists to break up, so the **step** shortens until the whole set fits.
- **A chain escalates in _kinds_ of thing, never in amounts** (`BudSpectacle`, `BudAcclaim`, and the
  retired `KeeperSpectacle` this was first written for): a new layer at each rung, nothing ever taken away again, and every rung landing on a wave
  ordinary play actually reaches — the first cut started at wave two, so a one-wave tap, which is most of
  what happens, drew a burst and nothing else.
- **A peak reached on the last frame is a flash, not a size.** One accelerating curve to the burst put a
  flower within 5% of its peak for **3% of the beat** — less dwell than the flat curve it replaced, and
  reported as no change at all on a build genuinely running it. It arrives early and holds. **When a gesture
  is not landing, measure how long it is *legible* before touching how big it is.** And it **gathers before
  it grows**, with the crouch deliberately constant while the swell escalates: it is the *tell*, so exactly
  one thing should be growing.
- **A ladder has to be spent on the waves the mode actually reaches, and it has to stop somewhere.** The
  first spread nine waves of range past anything a player sees; front-loaded, it shipped flowers swelling
  half again wider than their own square, thirteen at a time. The guard now holds a **ceiling** as well as a
  floor, because this number had to be corrected in both directions.
- **One constant sets the pace of everything, so check what it means at the extremes.** Every duration is a
  fraction of `Wave` = `Ceiling / chain`; at 3.60s the finale's eight-wave tap gave each wave .45s and the
  whole grove fell in **.167s**, which is a teleport. It is 8.00s now. The bound has not changed in kind — a
  chain must still end and the rate still gives way — it was set where a cascade could not be watched.
- **A burst is a silhouette event, never a volume one, and more layers is not more quality.** A real fire
  flipbook came back as *"a smoke/dust comes out — what is that?"* (a plume authored for a rocket exhaust
  shrunk onto a 170-point cell), and what replaced it — petals, rays, embers, a backlight, a prism ring — was
  **also deleted**, because asked for a carnival the answer here was to keep adding kinds of thing and it
  came back as *"I don't want a meshed up random animation"*. A premium burst in this genre is **four
  gestures done properly**: the piece popping out with a squash, a hot round core, a wide soft bloom in its
  colour, one clean expanding ring, and round sparkles. **On a board of round soft shapes, anything with a
  straight edge reads as lighting equipment rather than as light** — which is why `Art.Flash`'s spiky star
  and `Art.Rays`' straight beams are gone. What is left is generated (`Art.Glow`, `Wave`, `Glint`, `Bloom`,
  `Crystal`): no addresses, no bundle, no preload.
- **A payoff is celebrated where it was earned, and only its scale is ever animated.** A freed critter took
  four attempts and every wrong one had the creature *travelling* — leaping and falling back, then flying to
  a readout that sits *under* the board, both reported as critters falling. A size springing past itself is a
  pop; a position springing past itself is a drop. It also lands **after** the shell's own noise has
  finished, because a cocoon opening already drew eight separate effects and the creature arrived as a ninth
  thing moving, reported as *no emphasis at all*. **Before adding to a celebration, ask what else is on
  screen in that quarter-second; if the answer is "eight things", the fix is a silence rather than a ninth.**
- **A reward the player has earned must not be kept where the board is allowed to rearrange.** Freeing a
  critter *empties* its square in the model — that is the point — so one drawn as a child of that cell was
  taken down by the fall, painted over, and spun round when the flower that landed there later burst: three
  reports, one fault, fixed by a layer above the field. **Ask what the model does to a square after the thing
  that made it special has happened.**
- **A mechanic the board cannot show reads as a bug.** The grove ripening a flower for the player was sent
  to the view as an ordinary wash, which has its cause on screen a tenth of a second earlier where a ripen
  has none and can land right across the board (*"I'm not sure if this is a bug or a feature"*). It has its
  own cue, is held to the end of the chain because it needs a still board, and arrives as a ring closing
  **inward** — the same idiom a freed critter gets, and the opposite of every expanding ring, which says
  *something went off here* rather than *this one*.
- **A breath fades in.** `sin(phase)` is not zero, so every caller passing a phase used to snap its target to
  a different size on the frame the breath began: one control is a twitch, a *board* is thirty flowers each
  jumping in one frame at the exact moment a chain settled.
- **A repaint that skips its own "nothing changed" guard is a board-wide flinch.** Asking for `animate: true`
  on a tidy-up loop killed every tween on all thirty-six cells, taking every white flower's breath and every
  "this one pops" hint with it.
- **A board is heard landing, and at most five of a wave are struck** (`BudChorus`, in Domain). Twenty
  identical clunks in half a second is unlistenable *and* more voices than `Audio.PlayOne`'s ten-voice pool
  holds, so which pieces went quiet would depend on nothing the player can see; the five are spread **evenly
  across the wave rather than off the front**, because the first five of twenty sounds like a five-piece wave
  followed by silence. **Nothing in a grove is made of anything that shatters** either — a `shatter` sample
  and a bell were replaced by the mode's own burst note struck low and doubled, bigger by being *lower and
  doubled* rather than by being a different kind of sound.
- **A falling piece accelerates, and lands with a squash and a spring.** `OutQuad` decelerates into the
  ground, which is the one shape a falling thing cannot have, and arriving at exactly its resting size is a
  thing that has finished moving rather than a thing the ground has stopped. Equally, colour landing on a
  flower swells and settles rather than shivering: a thing that shivers has been *disturbed*, where a thing
  that swells has *become something*. And a burst is **left alone for a beat before the grove falls into
  it**, with the hold taken *out of* the fall's allowance rather than added beside it.
- **Nothing is drawn between two cells except from the pulses.** A bolt that asked the settled board which
  neighbour was bare — which answers "empty at the *end*" — fired out of blank soil, reported as *"random
  electric effects at positions unrelated to where the flowers are rotating"*.
- **A celebration should say how big it was.** `BudChain.Blast` grades a bunch at five and eight, adding a
  **second ring chasing the first** (which reads as *more* rather than *bigger*) and dropping the note lower
  as the bunch fattens — deliberately the opposite of the chain's ladder, which climbs, so a fat bunch and a
  deep chain stay two readings. Every tap throws a ring of the colour it is *making*, because the commonest
  event in the mode is a tap that sets off nothing at all.
- **One silhouette, one exception.** Drawing one petal per channel — three, five or eight — was a second
  reading for red-green colour blindness and was reported as clutter, correctly: thirty-six flowers in three
  silhouettes scattered through each other is a *field of shapes*, and the thing the mode is about stops
  standing out. White keeps eight, because its difference is a rule rather than a colour. The second reading
  moved to a **legend above the grove**, derived from the same masks the board mixes with, with `BudBand`
  owning where every piece sits (8a) — and drawn as **three cards, not one plate**, because nine coloured
  shapes and four operators inside one border are a row of thirteen things.
- **A board is clipped to its own plate lip, on all four sides.** The top margin was first set to the
  wind-up's overhang, which is the wrong trade: a quarter of a cell is invisible when a flower is swelling in
  it and perfectly visible when one is falling through it. The other three went the same way from the other
  end, on the reasoning that there is nothing to *hide* there — true of a piece arriving, false of one going
  off. There is no margin generous for an entrance and tight for a burst. Everything a burst throws is
  clipped **with** the board, because a pop happens on the spot; what crosses the edge is what is about the
  *board* rather than a cell — a sweep, fireworks, confetti, the word.
- **A word is fitted to the screen rather than to the board.** LEGENDARY drew 1195px wide on a canvas with
  1024 to give, because the label was as wide as the *grove* plus a margin and the ladder hands out points by
  rung without knowing how many letters the word has or what language it is in. The authored size is a
  **ceiling**, and the **resting size is fitted first while the slam takes what is left** — shrinking the
  font until the slam fits is the wrong trade, since the resting word is what is being read.
- **A nested `Canvas` is a raycast boundary as well as a rebuild boundary.** One wave puts up to **296
  transient graphics** on screen each animating its colour, and Unity rebuilds a canvas *whole*, so one dirty
  spark cost a rebuild of all ~450 of the board's graphics sixty times a second — no timing change could fix
  that. Nesting the effect layers fixes it; nesting the *field* shipped a grove nobody could tap, because
  `GraphicRegistry` files a graphic under the nearest enabled canvas and a `GraphicRaycaster` only looks up
  the canvas it sits on (measured at **56 tap targets and 0 reachable**). **A layer may be nested only if
  nothing under it is ever tapped**, and `overrideSorting` must stay off, because
  `MaskUtilities.GetRectMaskForClippable` stops at the first canvas that overrides sorting and the effects
  would silently leave the clip. `BudCanvasTests` guards both.

Shared by every mode: `RunLedger` (record, chests, streak, reward, analytics — and it builds the
`RunOutcome` *before* folding the record in, because half of what it describes stops being true after),
`RunScreen` (defeat/pause/forfeit panels, and the continue offer that comes before all three — 23),
`RunGuard` (a committed run is paid for however it ends), `PlayRoute` (which screen opens a level),
`RunWording` (turns vs sparks). `LevelsScreen.Open` is the one place a mode decides its screen. A mode joins
the continue by answering three questions — `MeasuredIn`, `ContinueDeficit`, `ContinueWith` — and never gets
at the price.

## House rules for the UI

Each was learned two or three times in different files. Not invariants — the things that go wrong in
Presentation and are invisible in a compile, a validator and a screenshot of the source. (The rules a
*moving board* has to keep are with the modes, above.)

- **A width-matched canvas makes the aspect the layout, and a tablet is a different layout.** Everything is
  laid out against a canvas pinned at 1080 units *across* (`Boot.BuildCanvas`), which is right for a portrait
  game — but the canvas *height* is then 1080 times the aspect, so a 20:9 phone offers 2400 units and **a 4:3
  tablet offers 1440**. Every screen is a vertical stack of fixed-unit chrome (the hub spends 1338), so handed
  1440 the same arithmetic overlaps: measured on the real hub, the companion drawn **176 units through** the
  streak and event boxes. Not one constant was wrong and nothing could see it — it was reported from an iPad.
  `Layout.CanvasFit` is one rule: a display squarer than `PhoneFloor` (7:4) is given `ShortHeight` (2160)
  units of height and the canvas **widens** to suit, so nothing moves relative to anything else and the whole
  interface is simply drawn smaller. It is a **threshold, not a ramp**, because `ScreenMatchMode.Expand` would
  shrink a 16:9 phone too (`CanvasFitTests` asserts every shipping phone is untouched); `ShortHeight` is
  **measured** from the deepest layout in the game; and it is applied by a **fitter**, because a tablet in
  split view is resized while the app is running.
  <br>Two places assumed the canvas was 1080 and both were silent: `SplashScreen.Fit`, which now reads
  `Boot.CanvasWidth`, because the width the game is *designed* at and the width it is *drawn* at have stopped
  being the same number; and the chapter map, which is a **painting** whose strips draw at `ChapterMap.Width`
  while everything on it is a fraction of a container stretched to the canvas, so `LevelsScreen._mapScale`
  scales the map uniformly while the glade discs deliberately do not scale with it — they are controls rather
  than scenery. And one mode needed more: every board fits its grid with `min(width / columns, usable /
  rows)`, so a well (bound by *height*) charges every unit of furniture straight to the cell where a square
  board does not. `FallBand.Of(shortCanvas)` gives it back by **scaling** the legend and tray rather than
  re-laying them out, because a second set of coordinates is a second layout to keep in step.
- **A screen built in the same frame as the canvas can trust neither its rect nor its scale.**
  `CanvasScaler` applies from `Canvas.willRenderCanvases`, after every `Update`, and `Boot` raises
  `SplashScreen` inside that frame: `Content.rect` reports raw device pixels for a frame, and every number in
  canvas units is drawn at a scale factor of 1 until the second frame. Both read as the launch arriving
  stretched and settling, and both are invisible on a 1080-wide phone. Three fixes, not alternatives: `Boot`
  calls `Canvas.ForceUpdateCanvases()` immediately after building; `SplashScreen.Fit` does not measure the
  canvas **at all** (its height is the display's aspect times a known width — a pure function of `Screen`);
  and the screen holds a **black curtain** until a frame passes in which neither the layout nor
  `Canvas.scaleFactor` has moved, which also covers Android devices reporting landscape for a frame.
- **`Destroy` lands at the end of the frame.** Hide a region before destroying it, or the outgoing panel
  draws over its replacement for a frame — which, with everything entering from scale zero, reads as a flash.
- **`Show` animates, `Refresh` does not.** Anything raised by an *event* is a redraw. A screen repainted by a
  wallet change, an art scope landing or a ledger event must not replay its entrance.
- **A tween that reads its own target's value must say where an interrupted one lands, or the error
  compounds.** `Punch`, `Shake`, `Bob` and `Breathe` *borrow* a resting value; `Pop` reads the size it is
  springing *to*. Superseding one used to drop it where it stood, so the next punch took a half-squashed scale
  as its own rest and spam-tapping the hub's companion multiplied one squash into the next until the critter
  was a sliver. `Tw.OnAbandon` declares the answer — hand it back, or land on it — and `KillChannel` honours
  it; anything moving a value somewhere new declares nothing, which is what keeps a cross-fade a cross-fade.
- **The corollary: two tweens on one value are a bug however different their channels are.** A channel decides
  what *supersedes* what and says nothing about what they write, so a punch fired beside a scale reads a value
  the scale is still moving and lands the target a few percent off, for ever. Before adding motion to something
  already moving, ask which *value* each tween writes; and when a gesture can supersede itself, kill its
  channel **before** reading the rest value, or you capture mid-flight.
- **A widget returned to a pool must be given back with every tween it owns killed, on every object it owns.**
  A `Tween` is filed under the `UnityEngine.Object` its caller named, so `KillAll(mote.Body)` says nothing
  about a tween owned by `mote.Rt`. Lightfall's pool called the first and the collapse uses the second, so a
  mote recycled mid-slide went into the pool with a live tween writing its position and came back out dragged
  to where the *old* cell had been — reported as a lens that sometimes refused to fall.
- **A panel with several exits reports through none of them reliably.** Put the safe outcome on `OnDestroy`
  and make the exception the thing somebody declares — `AdOfferOverlay.Dismissed`, the pause menu's unlatch,
  `BoardView.Locked` as a property raising `OnChanged`. Exactly one of `Rewarded`/`Dismissed` fires, so both
  must be handled.
- **What a way out of a run costs is `RunScreen`'s, never a mode's.** Commit, resolve, forfeit and the
  confirmation live there, and `RestartLevel` is not overridable — a mode supplies `Rewind`, `RunOver`,
  `NoteAbandoned` and `StakeLevel` and never gets at the price. It was each mode's own for two modes and the
  copies drifted: one mode's restart never called its copy at all, so a restart there was free — on a mode
  whose fail state is a pot a restart refills. `RunStakeTests` fails if any mode declares a piece of the
  stake. A related trap it caught: `ModeScreen`'s chapter coroutine was called `Resolve`, which *hid*
  `RunScreen.Resolve` from every mode below it — the calls compiled, bound to the coroutine, built an iterator
  nobody ran, and a won grove would have been charged for again at the next launch. **Two members with one
  name in one hierarchy is a bug waiting for the third.**
- **A run begins when `RunScreen` says so, never when a board happens to be unlocked.** A board's `Locked`
  flag has several writers and one is an animation: a first-timer's tip latched the board, a tween unlatched
  it a beat later, and the run's play time accrued while the player read a lesson shown once in their life —
  after three seconds the run was committed, so backing out cost a heart. Both writes were correct; only their
  order was wrong. `RunHold` is a latch nothing else writes. **And the fix was to stop asking modes to walk
  into the funnel**: `Tick` was a `protected` method each mode called from its own `Update`, and three modes
  out of four never called it, two of which took input while the iris was still opening. `RunScreen` owns
  `Update`, the two halves are **abstract** (`Runnable`, `Running`) so a mode cannot decline to answer, and
  `Tick` is private — a default would have kept the hole where it was. Unity dispatches `Update` to the
  most-derived declaration only, so a mode declaring one silently steals the frame; `RunFrameTests` refuses
  that by reflection.
- **A lesson is declared as a fact about the board, never about the player.** A mode fills
  `RunScreen.Lessons` and `RunScreen` asks `TipLedger` which is new — which is what let the "show me again"
  key cost one method, since a mode filtering itself produces a list that is empty at exactly the moment
  somebody asks to be reminded. The review re-asks `Lessons` rather than replaying a kept list, because a
  restart rebuilds the tiles a tip rings and a cached `RectTransform` is by then a destroyed object.
- **A lesson about a gesture is shown, not described, and a demonstration must show a move the player could
  actually make.** A ring and two sentences is right for a *rule*; a **verb** is not that shape, so
  `Lesson.Trace` lights a route on the real board and `CoachHand` walks a hand along it. Three rules, each
  learned by getting it wrong: the route must be one the mode's own input could produce (a straight
  interpolation between two cells is a diagonal drag on a mode that has no diagonal); it must never be the
  board's own answer; and `Art.Hand` is **tilted on purpose**, because an upright finger over a closed fist is
  a gesture that must never reach a teaching panel in any market. Budburst's graft is the second user:
  `BudView.GraftPair` names a trade the board would accept, both flowers are ringed and the hand walks one
  onto the other.
- **A lesson about an event is taught at the event, and `Lesson.Later` is how a mode says so.** "Make five"
  shown at the opening pointed at the ripest flower on a board where nothing had happened, and was reported
  as exactly that; it now goes up over the bolt the player just made (20m: the event is the reward).
  `RunLessons.Open` skips a deferred lesson, `RunLessons.Teach` raises one when the mode says the moment has
  come (`BudView.Forged`, after the chain has finished playing), and the review key still lists it. `Teach`
  refuses anything already seen, so a mode may call it on every forge and it costs one showing.
- **A tip is written for a child.** Two or three short sentences, one idea each, the verb first; the
  board shows the rest. A tip that needs a paragraph is describing a rule the board should be demonstrating
  (20g), and a sentence about a thing on screen rings that thing — "the colour in your hand" rings the hand.
- **Celebrate once.** The board already flashes, sounds and (for a glade) throws confetti when it solves; the
  win panel adds no fanfare and no confetti.
- **The game does not vibrate at all, and `Haptic` is deleted.** Twenty call sites and a settings toggle, and
  every one was the *same* knock, because `Handheld.Vibrate` on Android is a single fixed-length heavy pulse
  with no way to make a second lighter than the first — so a mode opening four cocoons in one chain produced
  one rumble. The `haptics` field stays in the settings DTO, retired in place for `bestMillis`' reason (12a).
- **Depth is applied to a whole visible window in one pass.** `SetSiblingIndex` *inserts*, so assigning depth
  per tile as tiles are realised leaves a field that looks sorted and is not.
- **An arrangement of identical things is arithmetic too, and the tell is an even count.** `TokenPile` was
  one shallow arc with every second token dropped a little, and `i % 2` is only symmetric when the count is
  odd — so a pile of four came out visibly heavier on one side and a pile of five did not, from the same
  expression. The **order** matters as much as the positions: a row drawn left to right shingles every token
  over the one before it, so each row is laid from its ends inwards and the front row goes last.
- **Whether two things on a screen overlap is arithmetic, so it goes in Domain and gets a test.**
  `ChapterMap` did it for map nodes (8a); `BudBand` and `ReadoutRow` for readouts. The rule earned its third
  instance the honest way: the band was three constants with a paragraph explaining why they cleared each
  other, and the paragraph was wrong, because `UIKit.Box` *always* pivots at centre whatever it is anchored
  to. `PanelStack` is the fourth: `GladeRewardsOverlay`'s height was a typed number, a fourth section was
  added without moving it, and the last paragraph had been drawn **78 units into the close button** ever
  since — invisible in English, where that paragraph is short enough. A panel whose section count varies with
  content must derive its height, measured against the shortest canvas the game is drawn on (1440 reference
  units); five sections is what that shape holds, and a sixth fails a test rather than a tablet. Two things
  about that ceiling: **a modal is centred, so the title ribbon's overhang counts twice**
  (`H/2 + overhang ≤ canvas/2`, where the obvious reading is 87 units too generous), and once the height is
  spent **width is the only lever left**. Both were found by rendering the thing offscreen to a PNG and
  looking at it, because `Text` best-fit is approximate and no test will say so.
  <br>`WheelPanel` is the fifth and failed in the one way the others could not: it had the test *and* the
  arithmetic and still drew a row through its neighbour, because **one number in the stack meant something
  different from the rest** — four rows were centres and one was documented as a *top*, so handing it over as
  a position lifted the box 46 units. The test passed throughout, because it checked the arithmetic the panel
  did not use, which is the failure mode a layout test has and the reason a stack should be **all centres**.
  It is now measured against the live objects with `GetWorldCorners` and `Rect.Overlaps` — the only thing that
  compares what was *drawn* against what was *derived*.
  <br>`ProductCardBadges` is the sixth and widens the rule: **the two things that overlapped were on
  different objects** — a badge hung 38 units past its own plate and the next column's ribbon reached 22 past
  its plate's other edge, so across a gutter of 34 they shared 26 units, and since `GridView` recycles cells
  which drew on top was whatever order the pool was in. Neither number is wrong alone, so nothing reading one
  object at a time could see it; the badge derives the constraint from the grid's column pitch, which *is* the
  card's own width. Two smaller lessons: a mark is measured as the shape it **draws** (a disc in a square
  texture treated as a rotated square overstates its reach by a sixth), and a caption is sized against the
  **field it is read on**, not the sprite carrying it.
  <br>`SplashCover` is the seventh and the one with the least else to catch it: **the thing the layout must
  not collide with is painted into a texture.** The launch screen is the key art with the wordmark baked in,
  with a loading bar under it, and where the lettering ends is not a rect anything can measure at runtime.
  Anything that re-cuts the cover must re-measure `WordFootUv`, `WordHeadUv` and the side pair: a wrong value
  puts the bar through the logo on every device at once, with nothing to say so. **It is also the one screen
  whose art has to be given back** — the poster is claimed into `AssetLibrary.SplashScope` and released.
  That claim is why `AssetLibrary.Claim` exists: a screen that draws in the frame it is built fetches
  synchronously, so the address has to belong to a scope *before* it is asked for.
  <br>**And the fit rule is a fact about the cover rather than about launch screens**, which the second cover
  is what proved. The first put its mark in the bottom tenth, so standing the picture on the canvas floor and
  taking the crop off the top was free — what it ate was sky — and "hang the bar under the word" and "put the
  bar at the foot" were the same instruction. The Gemfire cover carries its mark across the **middle** with
  three turrets under it: bottom-aligned, every canvas squarer than about 5:4 crops through the logo, and the
  bar's own rule would draw it across a muzzle flash. So the picture is hung on `MarkOnCanvas` and clamped to
  the edges, the bar is placed from the **foot** and `MinGap` is demoted to a ceiling that never binds, and
  the bar carries a **scrim** of its own — invisible on a phone, where it draws over near-black shadow, and
  load-bearing on a 1:1 canvas where the crop needed to keep the mark puts lit rock under it. **A bar that
  relies on the art behind it being dark is a bar that breaks the next time the art changes.**
  <br>Two instruments, and the split is the point. `SplashCoverTests` proves the arithmetic and **cannot open
  the PNG**; `python Tools/render_splash.py` draws it. `--contact` is every canvas shape `CanvasFit` can
  produce and is the one that matters — the fixture's canvas list had **no widened ones in it**, which is
  exactly how a cropped wordmark would have shipped. `--ident` draws the publisher card as a filmstrip.
- **The publisher card is a hole, not a word, and getting that wrong cost two cuts.** TEKOWORLD is the first
  thing a player ever sees. `Tools/make_ident_art.py` bakes the mark from **Orbitron Black** (SIL OFL; the
  font and its licence live in `Tools/IconSource/`) into one white-on-transparent PNG, so no second typeface
  reaches the build — the game ships one font, a warm rounded face for a puzzle game's chrome, which is the
  opposite of what an ident wants. That PNG is a Unity `Mask` with **`showMaskGraphic` false**: nothing is
  ever painted in the shape of the word. It is a sheet of black with the letters cut out of it, and what is
  seen through them is a spectrum band travelling behind.
  <br>**The two rejected cuts were both the same mistake — lighting an object instead of opening a hole.**
  The first drew *white* lettering with a coloured band passed over it. Over white, a saturated colour is a
  colour mixed with white, so the brightest the card could ever be was pastel; and the letters stayed exactly
  as legible whether the light was on them or not, which is what stops it reading as light at all. Adding a
  bloom around the word made it worse in the same direction — **an opaque sheet does not leak**, so a glow
  outside the letters is a lit object, which is the thing this is not. Owner's verdict on the first:
  *the neon lights should go inside the text*.
  <br>**Three things the cut-out model then decides for you.** The fade is of the **light**, never of the
  sheet: a `Mask` clips on its own graphic's alpha, so fading the cut-out fades the thing deciding where the
  hole *is*. The ramp is a **plateau, not a peak** — a band only opaque in its middle shows only its middle
  stops, and an earlier cut faded its pink and its yellow to nothing and read as a blue-green glint on a card
  asked for in colour. And an **ambient** fill sits behind the whole cut-out, because a light narrow enough
  to travel leaves most letters unlit, and unlit here means *invisible* rather than dim — without it the card
  spells out three letters at a time.
  <br>**The heavy weight is a decision the effect forced.** A light face has almost no hole to see the light
  through: the colour reads as a fringe on the edges of the strokes rather than as light behind them.
  <br>**A missing mark leaves black, never a white rectangle** (7b): nothing is built when the sprite comes
  back null, and the card holds its beat on black.
  <br>**It is the curtain, not a screen**, so it costs the launch nothing: the black plate the splash already
  used to cover its settling frames now carries the card, and the content loader runs underneath it.
  `MinimumShow` is counted from the moment the curtain **lifts** rather than from the build, or on a warm
  device the loading screen would appear and be gone inside half a second.
  <br>**Judge it at size.** `render_splash.py --ident` draws the strip at 22% and the effect is invisible
  there — the card has to be looked at on a full-size crop, which is how the blue-green cut survived a
  contact sheet.
- **A row's position is a centre, so a paragraph in one must be centred too — and a slot that is reserved and
  not filled belongs to the row below it.** Both halves were reported about the defeat panel's "no heart was
  spent" line: `Body` anchored its text to the *top* of the room a centre had handed it, and its centre was
  typed under a near-miss slot reserved on every defeat and filled on few, so an ordinary run had
  seventy-four units of paper doing nothing above the line and fourteen below it.
  **Anything reserving room conditionally should ask what the unconditional case does with it.**
- **A colour is chosen against the ground it is drawn on, not against the palette.** `Pal.Mint` is used forty
  times and every one is a halo, a fill, a board tint or a dark plate — so it is bright, correctly. The one
  place it was asked to carry a *sentence on cream panel paper* it came out at about 1.8:1, on body copy that
  by house rule has no outline and no shadow; `Pal.A` cannot fix that, because it makes a pale colour
  translucent rather than darker. `Pal.Moss` is the dark green for good news on cream, named for the colour
  rather than for the line using it, so the next one does not invent a third shade a step away.
- **An asset scope is bounded by what is on screen**, and an in-flight guard is not `IsScopeLoaded` — that
  goes true the instant a load *starts*. Four grove scopes exist for four different bounds. A screen may draw
  a piece from two art sources, so ask `HomesteadArt.HasArt` rather than assuming which is loaded.
- **Generate art the screen cannot afford to be missing.** An `Image` whose sprite has not arrived is a white
  rectangle, so anything on a dark or ceremonial screen is
  `Art.Bloom`/`Dial`/`Gradient`/`PrismRing`/`IsoTile`/`Ring`/`Glow` rather than an address.
- **Controls go in `View.Safe`, art stays full-bleed.** Letterboxing a backdrop to dodge a camera cutout is a
  worse picture than the cutout. iOS reports its inset a frame or two after a cold start, so the node re-fits
  itself rather than reading the value once in `Build`.
- **`UIKit.Box` pivots centre**, so anchoring a child to an edge puts half of it outside and growing a panel
  puts half the new room above the art. **Measure a painted shape's face rather than centring on its sprite**
  — `PillFaceLift`, `SquareFaceLift`, `NodeFaceLift`, the win banner's `RankLift`, the iso tile's skirt.
- **`UIKit.Label` defaults to `Overflow` with no clipping**, so an over-long translation keeps drawing rather
  than truncating; anything holding a translated string needs `UIKit.Shrinkable`.
- **A one-line caption is set through `UIKit.OneLine`, never by raising `Btn.OneLine`.** `UIKit.TextButton`
  switches best-fit on for any button carrying a glyph, and best-fit concedes the **line** before the size, so
  a long caption folds rather than shrinks. Raising the flag alone leaves both rules running over one label:
  `Squeeze` computes a size from `preferredWidth`, best-fit overrides it at draw time, and it re-runs a frame
  or two later when the dynamic font's texture is regenerated — so the caption arrives crushed and then
  springs out. It had escaped twice, on the two buttons that open and take the video bonus.
- **A control whose liveness depends on a per-frame fact has to be repainted on that frame, and "there is an
  event for it" is not the same thing.** Budburst's hint key was painted once when its row was built, but it
  is live while the run is *running* — `RunScreen.Running(bool)`'s answer, written every frame by that method
  and nothing else — so it was painted while the run was held by the opening iris and stayed grey for the life
  of the screen. Every event the screen *did* listen to fires for a different reason. It reached play as "the
  hint button never works".
- **Repaint from an event, never from a callback on the panel that changed something.**
  `CompanionLedger.Changed`, `CloudSaveService.IdentityChanged`, `GameSettings.Changed`.
- **A reward that lands somewhere is worth more than one that is merely granted, and the cascade that does it
  exists once.** `RewardFlight` — the chest's collect, lifted out when the rewarded ad and then the shop's
  receipt needed the same thing. Two rules keep it honest. A readout has **one writer**: the payout rewinds a
  pill to what it said before the grant and walks it forward a token at a time, so it `Claim`s the slot and
  `ResourceSlots.Repaint` refuses the hub underneath — a wallet change landing mid-cascade would jump the
  number to the truth and have the next token drag it back down. And the target is **read live at every
  landing, never captured**, because an ad's currency is granted by the server (10d), so the figure is walked
  towards whatever the balance says when a token arrives and a grant that has not arrived leaves the number
  where it was rather than anywhere invented. A prize with no pill adds nothing and simply closes: a reward
  already banked must never depend on an animation being able to run.
- **A panel that explains a resource is the answer to a question, never a toll on the way to playing — and
  what a video pays is a panel of its own.** `AdOfferOverlay` is right behind the `+` beside the heart pill,
  and was wrong as the thing standing between a player stopped mid-session and the video that would let them
  carry on — and it paid out by turning its own watch button into a COLLECT, drawing the largest moment in the
  placement as the smallest change on the screen. So a defeat panel's WATCH FOR HEARTS shows the video and
  what returns is the celebration with COLLECT under it (`HeartVideoFlow`). Three load-bearing details: the
  way onward is `PrizeOverlay.Collected`, raised **exactly once however the panel ended**, because the hearts
  are banked by the redeem and a defeat screen still reading "you are out of hearts" over a wallet holding two
  is the one frame a player could read as a bug; the button is the **only** thing left saying why a video is
  unavailable, so it is painted through `AdOfferButton` on a timer; and a refusal is a **toast**, not a row,
  because a panel deriving its height from its rows would need a fourth shape for a sentence that only exists
  when nothing was paid.
- **Showing a rewarded video is five steps in an order, and the order is the substance.**
  `RewardedVideo.Watch` — mint the impression, show it, snapshot the pills, redeem, read the refusal. Two of
  those orderings matter: the impression is minted **before** the SDK is asked for anything, because the nonce
  inside it has to reach the network as a custom parameter; and the pills are snapshotted **before** the
  redeem, because deriving the snapshot afterwards by subtracting the offer is wrong in the case that matters
  — a heart reward landing at the ceiling grants nothing, so the subtraction rewinds a pill below where it
  ever stood. What stays with each caller is the half only a `MonoBehaviour` can answer, whether it is still
  alive after the await, and the asymmetry that follows: **a prize is raised before that check and a refusal
  after it**, because the reward is banked whether or not anybody is looking, while a refusal is news about a
  button that no longer exists.
- **A receipt has to show the transaction happening, not report that it happened.** The shop's thank-you
  panel was a chime, a stamp and two printed numbers, defended as proportionate — the wrong axis, because the
  fault was never the length, it was that *nothing happened*. It is now the goods landing with a shockwave,
  `Payout` throwing their contents out of them, and COLLECT handing the lot to `RewardFlight`. Two rules keep
  it repeatable rather than tiring: **a way out arrives with the tokens, not after them** (tapping it early is
  safe, because the flight's snapshot was taken at build time), and **everything loud happens once, on the
  last landing**.
- **A `+` beside a resource always opens that resource's panel**, in every state, including the ones with no
  offer behind them. A control that answers a different question depending on what happens to be loaded is
  the mistake that deleted `RouteOverlay` and the toasts.
- **Ask about the blocking condition before the price.** A player who is both too junior and too poor is told
  about the wall money cannot climb (`HintPrompt`, `CompanionPurchaseState`). Equally, a short balance opens
  the shop rather than greying the button.
- **Recall is not difficulty, so the board answers it.** Lightfall's legend under the tray is the colour
  arithmetic, drawn permanently, because "which colour finishes yellow" is something a player has to hold in
  their head *while* deciding — reported as "I always forget which colour blends with which". It is derived
  from the same masks the board mixes with (`FallMixing`), never a typed table. The distinction worth keeping:
  a legend removes *bookkeeping* and must never remove a *decision*, which is why the ghost stops at whether a
  drop bursts and never shows how far the chain would run.
- **A celebration should say how good, not that something was good.** Confetti reads identically for a
  two-chain and a six, so Lightfall counts the chain out loud instead — one number per wave while it is still
  running, and a word at the end that climbs. The ladder is `FallChain`, in Domain, because a switch on a wave
  count in a `MonoBehaviour` is the one place nothing can be proved, and because how loud to shout is exactly
  the decision that gets retuned. Measure before setting one: the shipped chapter runs chains of 3–7
  routinely, so a ladder pitched for 2–5 spends its top word constantly.
- **Panels that explain the game read their numbers from the rules**, never from the copy —
  `StreakInfoOverlay`, `AdOfferOverlay`, `EventInfoOverlay`. That copy is the first thing to rot on a retune.
- **A screen that has grown a fourth responsibility has grown one too many.** `RunScreen` reached five — the
  stake, the run hold, the lesson sequence, the review key and the continue offer — and the symptom is always
  the same: no single rule in it can be changed without reading all of them. `RunLessons` and
  `RunContinueFlow` came out, and the test to apply before adding the next is *could this rule be proved
  without building the other four*.
- **A thing the shop sells is drawn in exactly one place.** `ProductCard`, because there are two shops: the
  browse screen and the gem shelf a lost run raises without navigating (23). Its layout is *one* layout scaled
  from the browse card's numbers — scale vertical offsets by the plate's height and horizontal ones by its
  width, since one factor for both is what made the picture and the headline overlap on the first compact card.
- **A card that says one thing twice must ask once.** A shop cell carries a painted picture of what arrives
  and, behind it, a fan of light in that rung's colour — one statement, *this is the sixth of six*. They were
  briefly two roundings of one fraction in two files (9a at the smallest scale it appears at), where a shelf
  re-cut from six rungs to five would have moved one and not the other and the fifth picture under the sixth
  colour is not wrong in any way a compile, a validator or a screenshot could name. `ShopLadder` is the one
  answer, in Domain, **whole numbers throughout**. The rule it replaced was that **motion singles out**, so
  only the featured card was lit — right when the light means *look here* and wrong when it means *how much*;
  the hierarchy is kept by **strength** rather than by presence.
- **A `switch` inside a `MonoBehaviour` is the one place here nothing can be proved.** The branching decisions
  live in Domain and are pinned offline: `HintPrompt`, `RenameRules`, `AccountPromptPolicy`, `GroveUnveil`,
  `GroveGrowth`, `AccountGate`.
- **Timing rules live in Domain and are tested** — `Cue`, `TweenCycle`, `GroveGrowth`, `GroveUnveil`,
  `BudTempo`, `BudStage`, `GladeFanfare`, `KeeperTempo`, `FallTempo`, `CoachStroke`. Every sequence is bounded
  and **the rate gives way**, so a bigger board is never a longer wait. Motion is the one subsystem whose
  failures show up only in play, which is why the arithmetic has to be reachable without an Editor.


## Baking, grading and framing — the art lore moved out of CLAUDE.md

*Moved here on 2026-09-12, when CLAUDE.md was cut from 708k to fit its limit. These are craft rules
about making pictures, not invariants about how the game works. The invariant numbers are kept so the
code comments that cite them still resolve: CLAUDE.md keeps the one-line rule, and the detail is here.*

**Baking a bought VFX pack (37k).** A 3D particle pack reaches a board here as a **bake**: not the
prefabs (the canvas is `ScreenSpaceOverlay` and the only camera culls everything, so a particle system
in the scene is never drawn) and not the pack's flat textures (what was bought is *motions*). Unity
rasterises the motion offline and what ships is sprite reels, which is what a board firing twenty-eight
bolts a second can afford. Six silent ways the first bake failed, all worth knowing:

- `ParticleSystem.Simulate` with `restart: false` **continues** from where a system is, so a
  stopped-and-cleared one bakes only the mesh renderers that draw whether or not anything is playing.
- Its last argument quantises to `Time.fixedDeltaTime`, four times coarser than the substep, so a
  *fixed* step bakes nothing or four times too much.
- `Particle.GetCurrentSize` is a **mesh scale** for a mesh-rendering system — every extent came back as
  the same suspiciously round number in all three directions. Measure `Renderer.bounds`.
- A frame **shaped by a constant** pads a comet whose real proportions are eight to one until it is a
  quarter of its own width, and since the view sizes a bolt by its frame's width that comes straight
  off the board as a twelve-pixel sliver.
- A **window** has two ends and neither is the effect's lifetime: one muzzle draws a ring inward before
  it bursts and a lightning bolt builds for a third of a second, so a fixed window bakes the run-up and
  throws away the event.
- The grade's **white-core protection has to be capped**, because an icicle and a lightning bolt are
  near-white nearly all over: uncapped, half a pack comes out colourless (0.18 median saturation on a
  bolt fired by a blue turret).

Nothing numeric found any of them — a contact sheet and `render_siege.py` did. **And the render has to
show a reel at its loudest**, because these effects dip: drawn from a fixed frame index it caught two
of four muzzles mid-dip and read as a bake that had failed, which is an instrument lying about the
thing it exists to judge. Consequences: the far tail is **framed out and dissolved**, because a trail
six times the head makes the sprite longer than the flight it has to cross; the reels are **pooled**,
which is premature everywhere else in this project and not here; and **damage numbers are tallied per
raider**, because a lit line lands about eighteen hits a second and eighteen figures a second is a wall
nobody can read one number out of, which drawing each bigger makes worse.

**Bloom, exposure and the grading ladder (37af, 37aj, 37ae).** Baked VFX are authored to be seen
through bloom, so a bake that renders without any ships the geometry of an effect with the light it
throws left out. A bloom is a **bright-pass blurred at two scales**, one channel and downsampled (the
colour is already the recipe's hue and a bloom is low-frequency; three channels at full resolution
across 253 reels is minutes of bake for a picture nobody could tell apart), added in **emission**
(`rgb x alpha`) rather than in colour, because these reels composite with ordinary alpha blending and
light that only changes a transparent pixel's colour changes nothing. Two things about the blur are
load-bearing: a box pass must divide by the **whole kernel** and never by how much of it was in bounds
(averaging only the samples that exist treats the frame's edge as a mirror and bakes a glowing
rectangle around every effect, worst on small frames); and the wide scale's divisor has to be held to
the frame's own size, or three passes cross the whole picture and the halo becomes a wash. A blur has
no zero, so a floor is subtracted — a thousandth of an alpha over a rectangle is still a rectangle.

Dividing every pixel by its own largest channel is what puts the brightness in the alpha and lets one
render be graded four ways — and it means the colour left behind carries none, so a pixel with a tenth
of the light and one with all of it come out the same. Every real renderer tonemaps that top rung to
**white**; this camera runs with HDR off and nothing after it, so the recipe needs a third rung driven
by *coverage* rather than by the source's paleness: **white core, hue body, warm haze — and the haze is
a second colour**, because light reddens as it spreads, which is why anything incandescent photographs
as a white middle in a warm glow and why a single-coloured bloom can only ever make the core bigger.

**Grading constants are per-source, not global.** The lean toward a target hue was tuned on four
elemental effects *chosen* for already wearing roughly the right hue; nothing in a bought roster is, so
a teal arrow stays teal on a red ward at that lean. The white-keeping constant has almost nothing to
bite on for art drawn pale, so an icicle grades to white-with-a-tinge. And a saturation **floor** is
needed, because below it a pale source comes out pink whatever else moves. **Before reusing a grading
constant on art chosen a different way, ask what the old art was chosen for.**

**Bleached reels do not work here, and the reason is exact.** A white reel with all its brightness in
coverage costs a quarter as much and can be worn in any colour by one `Image.color` multiply. Held up
beside a real render it is a flat pink smear: a multiply can only vary **value**, and what makes these
effects read is variation in **hue** — a yellow-hot head inside an orange body inside a red trail. A
white-core overlay recovers nothing either, because these packs' hot cores are *saturated yellow*
rather than white. That is invariant 37l met from a third direction, and the direction that matters:
what a bought turret may not look is cheaper than the free one.

**Framing.** A reel is framed with the prefab's own origin at a declared fraction of the way up, and
**that number is declared in the view and read by the bake**, so the number that frames the render and
the number that positions the sprite are one number. Trimming keeps the anchor row where it is — dead
frame is invisible while a reel is only *drawn*, and stops being invisible the moment the board has to
know where the bolt ends. A falling bolt is **clipped** rather than shrunk, because sizing it to the
room above whatever it hit makes a strike on a raider half way up a four-cell hill a cell and a half
long, which reads as a spark. **And before framing an effect, look at what it actually is**: framed as
a comet (tall, room reserved for a trail), a round blast came out 112 x 512 with the whole effect in
the top ninety rows and eighty per cent of the frame empty, which the view then draws as a violet
sliver seven cells long crossing a hill four cells deep.

**Flat quads and the camera (37aj, 37ba).** The rig looks straight along Z, because everything it had
ever baked was a projectile and a projectile looks the same from any angle. A pack's ground cracks,
splats, shockwaves and rings are **flat quads lying on the floor**, so every one of them bakes edge-on
and collapses to a hairline — what shipped was the geometry of a lightning strike with the *strike*
left out. The rig takes a tilt and orbits what it is aimed at, so at nought it is the old one to the
pixel. The same fault has an opposite face: a flat shockwave *card* seen face-on bakes as a translucent
**square the size of the frame**, which passes unnoticed when it is coloured and is a pane of glass
over a quarter of the hill when it is white.

**Procedural rather than baked, sometimes (37ac).** A bolt that is the *same* bolt twice reads as a
stamp, and a chain has to reach two points the board decides at run time — so boss lightning is
polylines built at run time, which also means no address to register, no group, no scope and no frame
where a strike is a white rectangle (7b), and it works on a checkout with no licensed pack in it. Four
things the render caught there and no number could: a white filament in a soft halo comes out **white**
at the size a phone draws it (it takes a third bar, the boss's own colour at full strength and twice
the filament's width, for the colour to survive over bright ground); bolts **left the plate**, because
the effects layer is sized to the field and carries no mask, so every endpoint is clamped; a bow of
half a cell is **invisible**, so three orbs on spread arcs were one orb drawn three times; and nine
strikes over a hill read as **noise** rather than as six things being struck. And one that is not about
looking: **a staggered storm constructed up front is several hundred `Image`s and a canvas rebuild
inside one frame** — defer the construction past the delay, not just the fade.

**Casting a top-down cast (37ar, 37as).** Nine monster packs on this machine hold ninety-six characters
and not one insect; the only insects anywhere are fifteen in the same kit the turrets come from, and
they are drawn **top-down**, which is the one view this board has. Sixteen kinds out of fifteen bodies
is the arithmetic every consequence follows from.

- Every insect is composited over a **baked ground shadow**, an ellipse wider than the body, so the
  animation's bounding box is half as wide again and a third taller than the insect — and the view
  sizes a body by its frame. It is separable *exactly*, and by something better than a threshold on
  darkness: the shadow is **pure black at partial alpha** where a fly's wings are **white at partial
  alpha**.
- A hue rotation sets a pixel's hue and leaves its **value** alone, so the darkest beetle (mean 47
  against a hybrid's 140) came out black in whatever colour it was asked for — on a board where the
  colour of a raider is the whole mechanic. Every body is lifted onto one measured value. **The variety
  a pack gives you is hue and material, never brightness, because brightness decides whether anything
  can be read at all.**
- A boss carries **two** reels rather than three, because a top-down insect's reel cycles its legs and
  wings **in place** (2.3 pixels of drift across a 137-pixel frame), so standing and walking are one
  picture and the board is the only thing that moves it. A boss that really walked would want the third
  back. Three animations of one character need **one canvas**, or trimming each to its own box draws
  the body at two different scales; their first frames are the same pose, so the offset is the
  difference of their alpha centroids, exact to the pixel. The canvas is mirrored on **both** axes,
  which only ever grows it, so nothing is ever clipped.
- **A shadow's offset is a fact about what the body is.** 0.46 of the drawn height below the node is
  right for a *biped seen from the side* and puts a top-down insect's shadow the better part of a
  body-length clear of the thing casting it; measured off the pack's own baked shadows, a crawler's
  sits 0.17–0.24 of its own height below its middle and runs 1.02–1.15 of its width. **And a soft
  sprite's rectangle is not its shadow**: `(1 - distance)` raised to a cube is an eighth of its peak
  half way out and invisible at this size, while a profile that ramps the whole way is a *haze* rather
  than a shadow — a quarter-power holds near its peak most of the way out, and the width has to move
  with the falloff, because a flat profile reaches almost to the edge of its rect where a steep one
  dies two thirds of the way.

**Casting a third chapter out of five bodies (37bu, 37bv).** The survival pack draws five skeletons
head-on, which is the same view the brood's blobs are drawn at and therefore the one this board has. What
it does *not* have is fifteen bodies, and five against twelve slots changes what the cast is allowed to
say.

- **The kind is the silhouette and the colour is the hue, and nothing pretends otherwise.** With fifteen
  bodies each kind gets four shapes and the shape carries the colour as a fourth reading on top of 37f's
  three; with five it cannot, and cutting one body four times and calling it four would be the pretence.
  So one bare rib cage creeps, two bodies carrying a long weapon over a helm are the brutes, and the two
  wearing plate are the bulwarks — the shield is the more literal of that pair on purpose, because the
  one thing a player has to read about a bulwark before it is in range is that it is carrying something.
  **Where a body is worn twice the two colours are opposite ones**, never red and amber, which is the one
  pair this palette already keeps closest together (37ak).
- **A bone-white pack needs a saturation floor of its own.** `hued` *pushes* saturation rather than
  setting it, which is right for a monster painted in two or three colours and wrong for a body that
  carries no hue at all: at the floor the other two casts use, a red skeleton and an amber one are two
  pale creams half a hue apart. Rendered at four floors, and the one where the skull still has a skull in
  it is the highest that works. **A per-pack constant, because it is a fact about what the pack paints.**
- **This is the first pack bought here that drew an attack, and what a second reel costs is a *frame*.**
  A raider that reaches the ward line stands there hitting it until something kills it, and for two
  chapters that was a walk cycle looping in place — 37u's complaint arriving through the art. Measured
  across the five, the attack throws the weapon so far outside the walk's box that one shared canvas
  fitted to the walk's height would draw **every skeleton at 59–73% of its size for the whole run** for
  the sake of six frames at the line. So the walk is cut tight as every other cast is, the swing is cut
  on the union at the **walk's own scale**, and the view multiplies the drawn height by the ratio it
  reads off the two sprites — no number written down, nothing to drift.
- **Its wizard is not the chapter's boss, and finding a replacement is the finding.** It shipped that
  way for one draft; the owner's verdict was one line, and there was nothing else on the machine to
  swap in — see **Picking a boss body when the packs are empty** below.

**Picking a boss body when the packs are empty (37bx).** Five 2D boss bodies exist on this machine and
three chapters have taken one each; the character packs beyond them hold eighty-odd small cartoon blobs
and ten neighbourhood zombies, and the only unused bodies in the two packs the bosses come from are two
plain eggs and a money bag. So the sixth boss is **baked**, and choosing it taught three things worth
more than the body.

- **Survey at the camera, then survey on the hill.** Five candidates were rendered at the board's own
  pitch and the winner on silhouette was obvious — the **druid**, whose antlers are the one thing in
  the set that projects sideways, which is 37ar's entire rule. Put on the hill at true relative scale it
  reads as a *friendly RPG mascot*. **Projecting sideways is necessary and is not sufficient**, and the
  second half is only ever visible on the board.
- **A bake and a cut pack frame a body completely differently.** These 2D packs leave about a third of
  the frame empty (their bodies fill 0.65–0.69 of it); `SiegeCastBake` trims to its own alpha and fills
  0.93. The view sizes a body by its *frame*, so the same `TallOf` draws a baked body half again as
  tall. **Measure a new reel's fill against the set before trusting that number.**
- **And a slim body needs a bigger number than a wide one.** At a drawn height that already made it the
  tallest of the six, a robed humanoid still read as slighter than the barrels with arms beside it. Mass
  is not height, and only the board says which one you have.

**Baking a cast from 3D (37at).** The market has no more top-down casts and the reason is structural:
"top-down" in an asset store nearly always means a three-quarter RPG view. So a second cast is rendered
out of rigged CC0 models at this board's own camera — the oldest technique in the genre, and one step
over from what the VFX bake already does. What ships is PNG reels; no model, rig or animator reaches a
build, and the models live under an `Editor` folder, which Unity excludes from players.

- **Edit mode does not skin.** `SampleAnimation` poses the bone **transforms**, and skinning is
  dispatched by the player loop, which is not running — so a `Camera.Render()` driven from a menu item
  draws every `SkinnedMeshRenderer` in its **bind pose** however the bones stand. Measured across two
  poses: the foot bone travels 0.75 units, the leg mesh's skinned vertices 1.07, and the rendered legs
  **0.000**. The fix is CPU skinning on demand — switch each skinned renderer off, stand a plain mesh
  renderer under the same transform with the same materials, and `BakeMesh` it every frame. At one pose
  the two render pixel-identically (0.076 of 255), so it buys the motion and changes nothing else.
- **What disguised it is which parts of a body are not skinned**: a helmet, a hood, a hat and a cape
  are plain meshes parented to a bone, so a transform moves them and they render perfectly — and every
  one of them is on the head or the shoulders. A hood rocking above a body that never moves is
  indistinguishable from a figure whose motion the projection has eaten. **The transform-level
  measurements were all correct and told nobody anything.**
- **Two metrics said the opposite of the truth.** Alpha change only sees the silhouette's outline and
  is blind to a limb moving inside the body; RGB change reads *higher* for a swinging helmet than for
  scissoring legs. On the strength of those two the models were written up as wrong, which was false.
  **Pixel churn is not legibility.**
- **What decides whether a model survives that camera is not the camera**: a hooded featureless dome
  reads as a skittle at every angle and every tint, where a horned helm and a blade down the back read
  instantly. **A silhouette has to be made of something that projects sideways.** And **the body thrown
  out was the one with nothing to hide the bug behind** — a model with no static parts stood perfectly
  still and was withdrawn as "will not animate, unexplained".
- **Three faults of finish, none of them taste.** *Upscaled*, because the cut size was matched to the
  smallest body on the board — the plainest "cheap" signal there is, and invisible in every gate.
  *Keyline*: a flat-shaded render is visibly pasted onto a board of cartoon art inside heavy dark
  outlines until it is given one, grown at supersample size so the line is soft rather than stepped, in
  navy rather than black. *No rim light*: a key and a fill shade a body and leave its edge exactly as
  bright as the hill, so one directional light from **behind and above** catches the helm and the
  shoulders — and it **has to carry the camera's yaw**, or it lights the face and makes the whole cast
  paler and flatter than before it was "improved".
- **Pin the environment light for the bake and put it back.** It is otherwise inherited from whichever
  scene somebody double-clicked, which a re-bake-and-compare verifier can never catch: it re-bakes in
  the same session and so agrees with itself whatever the value was.
- **Unity's convention is that a character faces `+Z`, and a camera built from `Euler(pitch, 0, 0)`
  looks along `+Z`** — so the first bake shipped a hill of raiders advancing on the ward line with
  their backs to it. The key and fill lights carry the yaw with the camera, or a body lit for one side
  of itself is rendered from the other.
- **Imported FBX materials may not answer `_Color`.** Measured, the material reads back as the colour
  it was set to and the render comes out pixel-identical for all four — on some models and not others,
  with no difference in shader, emission or texture setup. Shipping a cast where one body takes its
  colour and another silently does not is the worse outcome, so the whole cast is coloured **one** way,
  in post. Two related traps: `Renderer.material` does not instance in edit mode (it hands back the
  *imported FBX's* material, so painting one body repaints every body sharing it and modifies the asset
  on disk), and building copies from `sharedMaterial` each pass copies from a material the previous
  pass destroyed, which lands Unity's missing-material **magenta** on three raiders in four with the
  bake reporting success. Capture the originals once, before anything is painted.

**Grounds (37ab, 37au, 37av, 37aw).** Which ground a rung draws is arithmetic on the level's place in
its chapter (7c), so a second chapter costs no art. Five rounds of work went into this and the fifth
was thrown away:

- **An isometric pack is not a top-down pack and no amount of transforming makes it one.** Its ground
  is drawn as diamonds with the side faces baked into the pixels, so a tile cannot be un-skewed into a
  square — it comes back a rounded block lit from a corner nothing else agrees with, and laying a field
  and cropping a rectangle out of it only hides the skirts, leaving a diagonal weave under a square
  board.
- **Value is the one thing a second ground may not change.** Every rung is normalised onto one measured
  mean and spread, and chroma has a ceiling, because a saturated floor is the one thing on this board
  competing with the cast walking over it. **A floor may carry a real colour; what it may not carry is
  a *board* colour** — measured, the ten floors sit at 3.3–11.2 chroma and every one clears all four
  gem hues by at least .04 of the wheel.
- **Whenever two normalisations decide whether one thing reads against another, the invariant is the
  gap.** The ground's mean stood a third *brighter* than the cast normalised to walk over it — the
  plate rule exactly inverted, in a comment citing it — because the number had been recorded as "the
  value the cast was judged against" when what it was measured off was one tileset's own brightness. It
  is two thirds of the cast now, and **if the cast's value moves, this moves with it**.
- **A tile sheet is cut from its own alpha box and never from its file size**, because several are
  exported with a transparent margin: dividing the file puts every seam a few pixels out and slices a
  strip of each tile onto its neighbour, which reads as a grubby grid rather than as an error.
- **The row count is derived per sheet.** A tile is 231x214 on one sheet and 249x189 on another, so one
  row count for all of them squashes some by a fifth to fit a cell that is not their shape. A tile is
  square or it is not: a non-square cell stretches every tile before the view ever sees it, which one
  canvas hid for two rounds by being square *by luck*.
- **Staining overwrites hue and saturation; damping only turns a material's own colour down.** The
  first is the only way to make one pack look like ten floors and can never make sandstone look like
  ice; the second leaves every crack, speck and bevel where the artist drew it. **And a constant tuned
  against one input is a constant nobody has tested**: a tint that *scales* saturation is fine while
  every tile is nearly grey and turns a half-saturated brick vivid, so two floors meant to differ in
  material differ in *loudness* instead.
- **A rule about board colours is about every board colour, so grep the palette rather than the thing
  that prompted it.** Crystals were rotated off two gem hues while moss and quarried stone sitting on
  the other two were left for two chapters. They are **calmed rather than rotated**: a thing meant to
  be looked at moves to a hue that means nothing and stays bright, and a thing meant to read as
  material gives up the saturation that made it a signal.
- **A thing lying on the floor is normalised onto the floor's band**, exactly as the cast and the floor
  are onto theirs — two packs can disagree by a factor of three about brightness, and a scatter left at
  its own value is a handful of white chips on a dark hill however carefully it is placed. That is the
  difference between a thing on the ground and **litter**.
- **Rotation is a fact about the object**: a rock, a plank and a crystal have no up; a barrel, a stump,
  a gate and a fence do. Spun freely, fences come out lying at thirty-seven degrees. And square faces
  can be turned for free where a rectangular tile lit from one side cannot — a quarter turn there is a
  tile lying on its side.
- **A gamma lift is the wrong tool for darkness** — it lifts the shadows with it and washes everything
  to pastel. An affine grade is the right one.
- **A zip entry test spelled with a folder in it** matched the pack that nests its folder and silently
  not the one that puts it at the root, so both packs read as absent and the tool passed.

**Twin barrels and bolt width (37az).** Before duplicating an effect to say "there are two of these",
divide the gap by the width of what you are about to draw: barrels at ±0.097 of a sprite's width are a
third of a cell apart against a muzzle flash drawn 2.7 cells wide, and two blobs whose centres are 12%
of their own width apart are one blob at twice the brightness. Two fixes: each flash is drawn at .70 so
the pair is twin-lobed rather than one bloom (*down* if it needs to read harder, never up), and each
bolt lands **beside** the target rather than on it, so the pair stays parallel the whole way — an
eighth of a cell off centre is invisible under a hit three cells wide, and two converging comets are
not. And a bolt's width is a fact about its **frame**: scaling the object or the frustum feeds a
narrower measurement back into the framing, which redraws the same bolt at the same width and merely
makes it longer, so the finished frames are squeezed toward the middle column — **the bolt only**,
because squeezing a radial burst makes an ellipse of it. **Before indexing a render buffer, check what
the render target's size actually is**: handed the final frame's dimensions, a squeeze read a quarter
of the supersampled buffer at half the stride and changed no picture while reporting success.

**Art tools, and which need what.** Every art tool here is Python with a `--check` that proves the
committed PNGs are what the tool writes, and `--contact` sheets that are the gate that actually matters
— **`--check` proves reproducibility and says nothing about quality**. The exceptions are the particle
bakes, which are Editor menu items because no Python can rasterise a particle system, and which need
the gitignored packs; and the grove's renderer, which rasterises static meshes on the CPU from
committed CC0 models and so runs on any checkout. Licensed packs are read from two or three roots and
every tool **passes when they are absent**, because copying a pack so that one path works is a second
copy nothing keeps in step.

- `Tools/make_siege_art.py` — gems, casts, turrets, grounds (`--tower`, `--tiles`, `--enemies`,
  `--icons`, `--survival`). Five source roots, one per place a licensed pack was downloaded to, because
  copying one so a single path works is a second copy nothing keeps in step.
- `Tools/make_siege_ground.py` — the ten floors; authored at the hill band's real aspect, and `--check`
  holds the PNGs to that canvas *and* to the one place C# writes the shape down.
- `Tools/make_chest_art.py` — the four task chests: a seventeen-frame opening reel per tier under
  `Chests/{tier}/` (scoped), the closed icon under `Ui/Chest/` (global), and the five goal glyphs
  under `Ui/Task/` cut from siege art already in the repo. Read straight out of the GraphicRiver
  chest pack's zip in Downloads — no licensed source in the tree — and `--check` passes without it.
  Which pack chest is which tier is written down once, at the top of the tool; the jade one is
  left on the shelf because nothing pays a fifth tier (8d).
- `Tools/make_game_font.py` — the one font: **Titan One**, shipped as **Gemfire Display**
  (the source carries a Reserved Font Name, and OFL condition 3 forbids a *modified* version
  from using it — adding a composed glyph is a modification), plus **172 accented letters built
  from the face's own bases and marks**. `--coverage` looks up every character in `en.json` in
  the cut font's own cmap and the writer **refuses** a font that cannot draw one — a glyph Unity
  cannot find is drawn as *nothing*, the white-rectangle class of fault (7b) on a typeface — and
  it prints what the face can spell per language on every build, which is a report rather than a
  gate. **`--proof` is the gate that matters** and is this tool's `--contact` sheet: it draws
  Turkish, Polish, Czech, Hungarian and Nordic names, and has caught four composition faults
  that every numeric reading here was happy with (46c). `--check` proves the shipped TTF
  reproduces, which needs `recalcTimestamp=False` on the load or fontTools stamps
  `head.modified` with the wall clock and every run disagrees with the last. <br>**The face was
  chosen by looking at ten at once** (46b): `Tools/hudkit.py` reads `GameFont.ttf`, so copying a
  candidate over it and re-rendering `render_home.py` / `render_tasks.py` is the whole
  instrument, and a ten-face contact sheet costs about a minute. Nunito Black and Fredoka Bold
  were each cut, installed and rejected on sight before that.
- `Tools/render_siege.py` — the eye for everything no number can see. `--phone` draws a 19.5:9 display
  with a home-indicator strip, which is the one shape that shows the shelf's foot and the three bands'
  real proportions. **Its insets are in the screen's own order (left, bottom, right, top)** and were
  written the other way round for a long time, which drew the board 55 points high: a diagnostic that
  is the only thing able to see a band in the wrong place must not itself put one there.
  <br>**`--tablet` draws the canvas a squarer display is really given, and every picture before it was
  1080 units across.** `CanvasFit` widens a tablet's canvas to 1620x2160 rather than scaling a phone's,
  so a board laid out to the width asks for a cell a third bigger there — which no render could say,
  because none of them drew that shape (invariant 37cc). **A flag per display is not the lesson; the
  lesson is that a tool whose whole job is proportions must be able to draw every canvas the game
  produces**, and this one could draw exactly one.
  <br>**`--charms` stands one of each charm on the middle row, and it is the only way to look at one
  at all.** Charms are dealt rather than authored (one a window, `SiegeTuning.CharmWithin`), so no
  shipped field carries one and no other tool in this project can put one on a board. Two questions,
  both of them invariant 32b's: does a mark read over a saturated jewel at forty pixels, and does the
  **colour underneath still read with the mark on** — because a charm is still worth its colour and a
  gem whose colour cannot be read is a gem that cannot be aimed. `--charms 27=lance` is for the worst
  pairing, which is white on amber.
- `Tools/render_grove.py`, `render_prism.py`, `render_home.py`, `render_shop.py` —
  the same job for the map, the grove, Prismvale and the two chrome screens. `hudkit.py` mirrors
  `UIKit` and `Skins` for the last two.
  <br>**`render_shop.py --shelf supplies` draws a shelf nothing could photograph until the free
  spot went onto it.** This mirror only ever knew how to draw a *painted* ladder, and the hearts
  shelf is **composed** — a pile of one, three or five, because the pile is the amount — so that
  shelf had been judged by arithmetic alone, which is the state invariant 32b is about. It draws
  the goods, the containers on their vessels and the rewarded card now, and it earned that on its
  first run: the free card's heap was three tokens, which is the same picture the fifteen-heart
  pack draws, so one shelf carried the same drawing twice (18e). It is **two** tokens because two
  is the count no pack ladder uses. The question the sheet answers is the only one there is here
  — *does a card that costs nothing read as a different kind of offer from the six prices under
  it, without reading as a broken one* — and all three things that do the work are pictures, so
  no number can answer it.
  <br>**`render_home.py` is the only instrument the hub's chest pack has** (45g). The pack is four
  chests packed until they overlap, and every question about it is a picture: is the arch visible,
  is the grandest chest the biggest thing on the plate, does anything collide with the countdown
  that sits above the row's short end, and is "big and close" what actually lands rather than four
  icons on a wide plate. It lays the row out with the screen's own arithmetic — a cosine for the
  heights, a cursor for the positions — and it **measures the chest sprite's headroom off the PNG
  and prints it** (`fill`, `wide`, `lift`, `aspect`, and how much of the plate the row fills), where
  `HomeScreen` can only carry those as constants. A re-cut of `Ui/Chest/*` that moves them shows up
  as a printed line that no longer matches the source (44b), and nothing else in this project can
  see it. <br>Two things it has already caught: a row at 57% of the plate that read as an inventory
  rather than a pack, and the royal chest — the thing the card exists to sell — drawn smallest and
  half behind its neighbour, which is what the crest rule fixed.
  <br>**It drew the two feature boxes on the wrong plate for as long as it existed** (44i):
  `FeatureCard` is handed `Skins.PlateOrange` and `Skins.PlateViolet` and draws them untinted, so
  the streak box is bright orange and the season box bright violet, and this painted both as the
  navy card — with the bottom strip as a hand-rolled rounded rectangle instead of the kit's
  trough. The trough's own half-alpha drop-shadow was reading as a brown ring round every bar in
  that row on a real device and no render here could show it. **A mirror that is wrong about a
  plate's colour cannot answer anything about what stands on it.**
- `Tools/render_tasks.py` — the Tasks &amp; Bonuses page and, with `--odds <tier>`, the panel a
  chest on its ladder opens; `--contact` puts the page and all four panels side by side, which is
  the only way to see that the short panels do not carry a hole where a royal chest's prizes are.
  It reads the shipped slates, targets, bands and weights out of `progression.json`, so a retune
  redraws rather than going stale, and it lays the ladder's pack out with `ChestPack`'s own
  arithmetic.
  <br>**It is also where a colour gets settled.** "The bar is a dead yellow" is answerable by
  neither argument nor a number: the swatch sheet that answered it — tints crossed with fill
  heights, drawn at the size a bar is really drawn — is what showed that every
  pre-divided-tint-plus-gloss version read *worse* than the dull one, and that what the bar was
  missing was saturation and **height in its trough**.
  <br>Three more it has caught, none of which any gate here can reach: the per-chest names on the
  rebuilt ladder overlapping into one run of letters, the odds panel's chest drawn **behind its own
  title ribbon** (a ribbon covers the first 108 units of a panel's face), and the prize rows' icons
  sitting a third of a panel away from the words they belong to.
- `Tools/render_arrival.py` — the panel that stands between paying and being thanked
  (`ShopArrivalOverlay`). **It is a render for a panel with two heights**: a wait, and — once
  `ArrivalWatch.Patience` has gone by — the same panel grown to hold a way out, which is the one
  thing `StoreArrivalTests` cannot look at. `--relaxed`, `--many` (two purchases in flight, so it
  names neither) and `--contact` for all three side by side.
  <br>**It has caught three things, and every numeric gate was happy with all of them.** A product
  drawn at a fraction of its ring, because these sprites are square with a good deal of air baked in
  and a box fitted to the hole leaves the picture floating in the middle of it (the same measurement
  `ShopArt.Paint` already makes on a card). A button seated 48 units off the foot, which is what
  `ShopGrantOverlay` and `ShopSupplyOverlay` both do and which puts it **on `panel_main`'s own
  60-unit rim** rather than on its face. And a third of the panel left empty for a button that had
  not arrived yet — the reason it has two heights rather than reserved room.
- `Tools/render_endless.py` — the Infinite lane's hub (the screen that replaced its map), and it
  draws the **furniture around the column as well as the column**: the plaque, the switcher pills,
  the star count and the loadout shelf. That is the whole point of it — the column is nailed between
  two pieces of chrome that were sized without it, so drawing it alone would answer the easy half.
  `--short` draws `CanvasFit.ShortestCanvas`, `--unplayed` the state before anybody has held a wave,
  and **`--modeswitch` the case the shipped catalog does not draw** — a second mode puts a second
  pill in the header and takes 136 units out of the band, which is the tightest this layout ever
  gets (8 units of air) and is the case the fixture pins.
  <br>**It has caught six things no gate here can reach**: a sentence running off the right edge; an
  emblem ring drawn past its own box and touching the track pill; a record printed in the mode's
  accent on a near-black pill, the dimmest thing on the screen; the kit's `ribbon_orange` fitted to
  a caption plate and drawn at a *third* of the width asked for, because that sprite is taller than
  it is wide; a band that had understated the header by a whole pill; and the whole first cut of the
  screen, which was text and loose icons on a flat ground and was rejected on sight (43b).
- `Glimmer Grove ▸ Art ▸ Bake Siege Projectiles` / `Bake Turret Projectiles` / `Bake Elemental
  Projectiles` / `Bake Storm Strike` / `Bake Siege Cast (3D)`, each with a `Verify` that re-bakes and
  compares within a tolerance (two GPUs are not obliged to rasterise a triangle identically), and
  `Survey Projectile Pack` / `Survey Siege Cast Angles (3D)`, which are how one of these is *chosen* —
  their names say a family and their thumbnails are grey cubes. **Re-run `Addressables ▸ Sync All
  Assets` after a bake, and save**: the importer hook does not fire on files a tool wrote while the
  Editor was busy.
  <br>**Two bars on a baked body, and they ask different questions.** `Moves` asks whether the reel
  differs from a photograph — the guard against a clip that bound to nothing, which renders twelve
  identical frames of a bind pose. `Strides` asks whether a *walk* reads as one, and it exists
  because the first question cannot see the second: the bonecaller's `Walking_A` changed 78% of its
  own pixels and still slid, because a robe swaying on the spot moves every pixel it owns while the
  feet stay put. The measurement is **vertical** — a foot-locked cycle has to raise and drop the
  pelvis, and that projects at any pitch, where a stride's forward travel is along the body's own
  occluded axis. Against its own drawn height, every running body in this cast bobs 3.9–7.7% and
  swings its feet 16–36%; the walk that shipped bobbed **0.80%**. The bar is two per cent, and it
  is still only the difference between a gait and a sway — whether a gait is any *good* needs
  `render_siege.py --warlord walk` and somebody looking at it (37cu).

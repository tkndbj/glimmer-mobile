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
  `difficulty.py`, `fall.py`, `keeper.py`, `bud.py`, and four shared contracts: `board-vectors.json`,
  `fall-vectors.json`, `keeper-vectors.json`, `bud-vectors.json`.
- `Tools/chapters/*.py` — one module per chapter; regenerates the shipped JSON and `--check`s itself
  against it. `author.py` is the shared glade board DSL (`cross`, `root`, `briar`, `path`) and derives a
  taproot's start rotations from the taps the root should cost rather than leaving four numbers to agree.
  Non-glade: `k01_grovekeeper.py` (hand-drawn against a sweep, because the shape of a groove is what
  teaches), `f01_lightfall.py`, `f02_glasswater.py`, `f03_whorlwater.py`, `b01_thicket.py`. For Lightfall and
  Budburst the *shape* is drawn by hand and only the **fill** is swept — which blend or colour stands where,
  which blend stands beside a whorl, what the board is dealt — the cheap half of the search and the half that
  decides how a board plays. `*_strings.py` hold the strings belonging to a mode rather than to a level.
- `Tools/hollow/` — the Hollow's rule mirror, board generator and `build_chapter.py`. The mirror is never
  authoritative; the shipping C# solver is what `Validate Content` runs.
- `Tools/render_wheel.py` — draws the bonus wheel exactly as `WheelFace` does, without Unity, for the one
  thing about it whose quality is only visible as a picture. It found four faults in one pass: near-black
  wedges, a ramp putting five of eight slices on one colour, two warm rungs darkening to olive, and a
  jackpot the same gold as the rim.
- `Tools/grove_art_facts.py` — writes each grove piece's `w`/`h` and its `hit` mask (a cell per sixteen
  art pixels) into `homestead.json`, and each companion's into `manifest.json`, from the shipped PNGs;
  `--check` proves the content still describes the art it ships, and `content.py` runs it. Run it after
  any re-cut of grove or companion art. `Tools/render_grove.py` draws a grove exactly as the game does —
  ground layer under piece layer, footprints, authored sizes — and is the fast loop for anything judged
  by eye; re-verify it against a play-mode screenshot after touching `GroveTileArt` or `GroveFieldView`.
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
  `map1`, every second draws `map2`. The mode is told apart by its **perch** and by nothing else, which
  is what `ModeLook` already said in prose — *"one difference is enough to tell two maps apart at a
  glance, and a second would start to read as two games rather than one game played two ways"* — and is
  now true of the painting as well. Strip counts stay a fact about each painting (`mapart.STRIPS`: 6, 4,
  5, 6), because `make_chapter_art.py` scales a source to *whole* strips and a count that leaves it
  narrower than 1080 stretches the map sideways.
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

**Groovekeeper** (`KeeperScreen`) — bare ground, a handful of **sprigs**, and an ordered basket of coloured
tiles laid beside something already standing. The rule is the inversion (28a); the goal is the **beds**; par
is the fewest tiles **spent**, planted or composted. Vocabulary: `.` bare ground, `#` **stone**, `*` a
**bed**, `r`/`g`/`b` a **heartbed**, `R`/`G`/`B` a **sprig**; the basket is `R`, `G`, `B` and `P` for a
**prism**. **Composting** spends the tile in hand to bring the next colour round and costs a tile like any
other. Two fail states (28c). There is no undo — the procession is visible and the ring under a thumb says
what a cell would open before it is committed, so a wrong tile is a misjudgement rather than a surprise.
<br>**The mode had no propagating event, and that was the whole of what was wrong with its look.** Every
other mode has something that *travels*; this one laid a tile, opened a flower and stopped, so there was
nothing on the board for a celebration to be *about* and every attempt came out as decoration around a
single cell. `KeeperSurge` changes no rule: after a planting settles, light walks outward from every bloom
through the **seams** and never between two tiles of one colour, so the celebration's shape is the shape the
player arranged. A tile **sprouts** rather than springing in from 1.45 (that is a sticker being placed, and
this mode is about planting); a seam is **made once and kept**; every bloom **leaves a flower** for the rest
of the run, because a bloom that faded to nothing left a grid of blanks where the thing the player spent the
level building should be; and the **flourish ladder escalates in kinds rather than amounts**
(`KeeperSpectacle`), because it was one picture at five sizes and *a number going up is not something
anybody sees*. Two rungs are unlike Budburst's: **five is the ceiling and it is a fact about the board**, so
every rung must land inside ordinary play; and **a bed lifts the floor to the sweep rung**, because one tile
opening one bed is the commonest thing a player does on purpose and a ladder whose first rung is bare
teaches them most of what they do does not count.
<br>**Two things were built here, played, and taken back out, and both failed the same way.** A butterfly
settled on every opened bed; a meadow turned every unplanted square to grass when the last bed opened.
Neither is a reading of anything on the board — a visitor says nothing about which tile was laid, and a
meadow drawn on the cells the player *never used* is biggest exactly where they did least — and both were
reported as unnecessary inside a minute of play. **A board's celebration has to be made of the board.**
<br>**A bed opening plays wood, never a bell** (`free`). **And the board is never completely still**: this
mode is played slowly, so the seconds where nothing happens are most of it and were exactly the seconds
nothing was drawing. Motes drift, tiles and seams breathe, flowers sway, all off `KeeperTempo.Phase` —
derived from the cell index rather than rolled, because two runs of one groove that shimmer differently is a
difference nobody can name and everybody notices.

**Hollow** (`HollowScreen`) — a field of sleeping critters and a short *ordered* queue of sparks. Light
accumulates and never decays, so a player can never be stuck, the only endings are winning and running out,
and unlimited undo is safe. Par is the fewest sparks that finish the board (`HollowSolver`), never authored.

**Budburst** (`BudScreen`) — a grove of **coloured flowers** with critters shut in **cocoons**, and a basket
of pure colour dealt one per tap. Tap a flower and the colour in hand **mixes** into it; any bunch of three
or more touching flowers of one colour **bursts**, washing its colour into every flower it touches, which
makes more bunches. A cocoon beside any burst takes a crack, and one out of cracks opens. A grove with a
**strip** (`regrow`) is *living* — it falls and grows, its white flowers are bombs, one flower ripens between
taps, and the board says which taps pop (20l).
<br>**The mix is the whole design decision, and it is why this mode is chill rather than clever.** Mixing
only ever *adds* channels, so every tap drives the board toward white and toward a burst: the grove wants to
go off and the player is only choosing where. There is nothing to work backwards from, and it is the
arithmetic four chapters of glades already taught, reused as a verb. A blend is never dealt — the basket is
pure `R`/`G`/`B` only, because a blend handed over is the one decision the mode has in it. **The goal is the
cocoons and not the flowers, and that is what makes it affordable**: "clear every flower" branches on the
flower count, so a six-by-six cost ninety-five thousand positions and often could not be proved, where the
cocoons are a far smaller target reached by the same chains. **A board must be authored settled**
(`BudValidator.Settled`), or the player is shown a chain they did not cause and par is measured against a
position they never met. Two fail states: the taps run out, or **no tap is legal any more** — white holds
every channel, so a grove of nothing but white has flowers on it and no move in it (`BudBoard.AnyMove` asks
over the *whole* basket rather than the colour in hand; `AnyFlower` is what got it wrong).
<br>**A grove has a hint key, and it buys something a glade's does not.** Nothing here is meant to stop
anybody, so what this one sells is the **big** version of a move they could have found anyway. `BudHint`
takes every opening tap that still finishes the grove inside the taps left, then the one that goes off
hardest: correct first, loud second, node-bounded, degrading to the biggest chain going rather than stalling.
The mark **points and does not play**, or it spends a tap out of the satchel on the player's behalf.
<br>**The Thicket is ten groves and every one is par 3** (26d's ceiling). The ramp is one dial — how many
are shut in (**3, 4, 4, 6, 6, 6, 7, 8, 9, 12**) and with it how much grove there is. From the fourth rung on
every grove is dealt `par + 5`, and it stays eight on the twelve-cocoon finale, because freeing twelve
critters with the same allowance is more to do **without ever being tighter** (20k). **The first three rungs
are where the verb is still being worked out, and none of them may punish that**: rungs one and two cannot
be lost at all — every other mode's opening level was authored that way and this one was not — and rung
three, the first grove in the game with a fail line on it and therefore where `bud_satchel` is first taught,
is dealt `par + 8`. Nothing about those *boards* got easier, because at that size a grove whose chain runs
three waves is one a single tap frees everybody on: par collapses to one and the star ladder goes with it. **Two dials were tried and thrown away** —
`spare` falling five to three, and `greedy` true early and false late — both ramps built out of
*withholding*, on a mode commissioned to be generous, and ramping `greedy` forced the sweep toward layouts
whose biggest chain is a trap. **Old wood went with them**, being the one object here that can only make a
chain shorter; `bud_wood` is a spent lesson id. **Par 3 rather than par 2, and the reason generalises**: at
par 2 both star lines round onto 3, so the two-star band is empty and a careless player drops straight to
one star — `CheckStarBands` reads the *factors* and says nothing about it, so on a mode whose pars are this
short, **look at the derived lines, not the factors**.
<br>**The Tanglewood is ten groves that graft and forge, still all par 3, and the payoff is a thing the
player made.** Two cuts before it were played and thrown away — a runner, then a windmill, a firefly, a
puffball and a hive on a grove each — and the verdict on all five was one sentence: *zero new animation,
nothing different, all I see is flowers popping.* Every one of them was placed by an author and paid out as
the same chain. What replaced them is the genre's own loop: **five alike leave a bolt where you tapped, eight
leave a sun**, and each fires with an event nothing else on the board can make.
<br>**A special is drawn as the one thing on a settled board that never stops moving.** A bolt wears a
four-pointed glint turning over the flower, a sun a wheel of rays turning the other way, both over a flower
lit brighter than its neighbours with a wide glow behind it. The **forge** is drawn as an arrival, not a
burst: the bunch's petals are thrown as always, the cell is bare for a beat (`BudTempo.ForgeLag`), eight
motes fly back into it from around, and the special stands up out of a flash at nearly twice its size under a
ring, with the mode's one "you got something" sound (`chime2` and `star`, higher for a sun). That is the
freed critter's own three beats spent on a thing, which is why it reads as a reward.
<br>**A bolt is lightning.** Tapping it flashes the flower white (`Ignite`, the player's own touch answered
at once); on the `Fire` cue a white-hot core is drawn out from the special to both edges of the grove over a
wide glow in the flower's colour, with jags along it that fade in the order the line passes them, and the
cells in the line burst as ordinary bursts **racing outward** — `BudTempo.FireStep` apart, nearest first,
which is the score's doing and what makes the line travel rather than appear. The screen flashes white, the
board is kicked harder than any bunch kicks it, and the burst note is struck twice an octave apart under a
whoosh. **A sun is a blast**: a gold flash the size of its square, its own wheel of rays thrown out and
turning, three shockwaves, the light behind the whole grove, rockets, the heaviest shake in the mode and a
bell — bigger than a bunch of nine by being a different kind of thing, not by being the same thing louder. A
special caught in another special's reach fires in the same wave, which is the chain the chapter is for.
<br>**The graft is a drag**, the one gesture every player of this genre already knows. Nothing marks which pairs
trade — the owner asked for the board to be found rather than read, and the halo on taps that pop and the
white flower's breath went at the same time; a refused pair nudges toward each other and snaps back with the
game's own refusal note;
a working one slides both flowers with a small lift before the chain (`BudCueKind.Slide`, at time nought,
the first wave's wind-up waiting on it). A drag ends with the pointer up over the cell, which the event
system reports as a click as well, so `BudView.Tap` drops a click while a drag stands.
<br>**Freed critters stand in the grove and the grove still falls through their square, and that is measured
rather than preferred.** The obvious fix — a freed cocoon leaves a **post** the grove may not move — was
built, mirrored into `bud.py` and run against the shipped ten. It takes the mode out of the boards, because
a chain compounds *because* the grove falls into the hole a burst makes: the best opening taps collapse from
5/7/6/8 waves to 1/2/3/3, one board drops to par 2 with a single winning play, and another stops being
provable inside the node ceiling. About 1,200 (basket, strip) pairs were swept under the post rule and **not
one** produced an opening tap of even three waves — so it is the rule rather than the fill, and its shape is
the wrong way round for this mode: the more critters a player frees, the worse their grove gets at
cascading.

### What a moving board has to get right

Every rule below was paid for by a play report on Budburst, whose chain is the most complex thing this game
draws. They generalise to anything that animates a board.

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
- **A chain escalates in _kinds_ of thing, never in amounts** (`BudSpectacle`, `KeeperSpectacle`,
  `BudAcclaim`): a new layer at each rung, nothing ever taken away again, and every rung landing on a wave
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
  not collide with is painted into a texture.** The launch screen is the key art with the wordmark baked in —
  a looping clip over a still that is its own first frame, so the handover has nothing to blend and a device
  that cannot decode keeps the picture — with a loading bar under the word, and where the lettering ends is
  not a rect anything can measure at runtime. The fit is cover and the crop comes off the **top**, because
  everything the screen is for is in the bottom tenth. The bar's clearance is bounded from both sides: it
  takes the gap the design wants, is raised to clear a home indicator where there is room, and is finally
  capped so it can never come closer than `MinGap` to the lettering — because on a short canvas with a
  navigation bar those two wants are not both satisfiable, and the honest answer is to give up the inset
  rather than the word. Anything that re-cuts the cover must re-measure `WordFootUv`: a wrong value puts the
  bar through the logo on every device at once, with nothing to say so. **It is also the one screen whose art
  has to be given back** — the `VideoPlayer` is stopped before it is destroyed (a player left playing keeps a
  hardware decoder alive through teardown on some Android drivers) and the poster is claimed into
  `AssetLibrary.SplashScope` and released. That claim is why `AssetLibrary.Claim` exists: a screen that draws
  in the frame it is built fetches synchronously, so the address has to belong to a scope *before* it is asked
  for.
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


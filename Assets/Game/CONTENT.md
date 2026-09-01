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
  homestead.json             the grove: its floor, the land for sale, everything placeable
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
- **A level's strings are derived from its id** (`level.<id>.name` and
  `.tagline`) and cannot be overridden. That is what lets the map, the home
  screen's "next up" line and the win overlay name a glade without reading its
  chapter. An overridable key would have made naming something you can only know
  after a file read, and the index would have stopped being sufficient.
  <br>There was a third, `.lesson`, and it is **retired**: it was floated along the
  bottom of any run with nothing new to teach — so on every level of every mode
  after the first few, which is furniture rather than something anybody reads.
  A board says what it has to say through its tips (`Mechanic`, `TipOverlay`),
  which are derived from the board rather than authored per level. Do not re-point
  the suffix at a different sentence: a level's strings are a pure function of its
  id, so re-using it would silently re-label sixty levels of authored prose.

## Adding a chapter

1. `Glimmer Grove ▸ Content ▸ Create Chapter Template` — scaffolds the JSON.
2. Author the grids (grammar below). `Tools/verify/author.py` is the aid: you say which
   cells exist and which are joined, and it derives every arm mask, refuses a board that
   cannot be finished, dials par to a target with `fit`, and reports what the board
   actually asks of a player — see *What makes a glade hard* below, and read it before
   authoring anything, because the first cut of two whole chapters got that wrong in a
   way nobody could see by looking. Every glade chapter now keeps its boards that way in
   `Tools/chapters/<chapter_id>.py`, which regenerate the shipped JSON and can check
   themselves against it (`--check`) — copy one for any new chapter. It stays deliberately
   outside the build gate all the same: a gate demanding a source would make a chapter
   authored by hand unbuildable, and the point of these files is that a chapter can be
   *retuned* rather than that it must have been generated.
   **Leave `par` out** — it is derived from the
   board, so an omitted par can never be wrong while a typed one can. **Do not choose a
   `backdrop` or a `mapStrips`**: both are arithmetic now, from the chapter's ordinal
   inside its own mode — `Tools/chapters/mapart.py` hands back the map and the ten skies,
   and the generator writes them into the JSON. See *A chapter's art is a rule* below.
3. Place the glades with `mapX`/`mapY`, fractions of *this chapter's* map. Walk
   them upward — each above the one before — and keep them apart; validation
   measures the gap in canvas units against the chapter's strip count and warns
   about collisions, backwards trails and anything crowding the end-of-chapter
   marker. More levels means more `mapStrips`, not tighter packing — which in
   practice means **the map decides how long a chapter is**, and the map is the one at
   this chapter's ordinal: six strips at ordinal 1 and 4, four at ordinal 2, five at
   ordinal 3 (`mapart.STRIPS`). Those counts are facts about the four paintings rather
   than preferences — `make_chapter_art.py` scales a source to *whole* strips, so a count
   that leaves the scaled source narrower than 1080 stretches the map sideways, and the
   Amberwood's 892x4745 source is 1128 wide at five strips and would have stretched by a
   fifth at four. **A chapter at a new ordinal is the only thing that needs a map cut**,
   and it needs its count worked out before boards are authored, not after, because every
   distance on a map is measured against it. The end-of-chapter marker caps the trail and places
   itself: how far *up* it floats is derived, but which side it sits on is
   `teaserX` — a fraction across the map, 0.66 if omitted, which is above a chapter
   whose last glade is on the right. A chapter ending on the right side wants a left
   `teaserX` and the other way round, or the trail's last step is a redundant
   vertical one. The Mill Vale ends at 0.71 and sets 0.3.
4. Add `chapter.<id>.name` and, per level, `level.<id>.name` / `.tagline` to
   `loc/en.json`. Missing keys fail validation, which names each one. There is no
   third per-level string — see the derived-strings note above.
5. `Glimmer Grove ▸ Content ▸ Sync Manifest` — adopts the new chapter into
   `manifest.json`, picks an `order`, fills in its level list and derives its `mode`
   from the levels it holds. Run it after *every* content edit.
6. `Glimmer Grove ▸ Addressables ▸ Sync All Assets`. **There is no art to cut**, unless
   the chapter is the first at a new ordinal — the map and the ten skies already exist and
   are shared, which is the whole point of *A chapter's art is a rule* below. Sync **after**
   step 5 and never before it: which bundle a piece of art belongs in is derived from which
   chapter claims it, and a chapter claims nothing until the manifest lists it. See
   *Addressing* below.
7. `Glimmer Grove ▸ Validate Content`. It must report zero errors — builds refuse
   to run otherwise.

Nothing in `manifest.json` is written by hand. Sync assigns `order` from the
chapter's id — sparse (10, 20, 30…) so a chapter can be slotted between two that
shipped — and says in the log what it chose; change the number there if it guessed
wrong. **`order` lives only in the manifest**: a chapter body that states its own is
rejected, because where the game goes next must be readable from one file and
changeable by pushing that one file.

**`mode` is derived too, and that is invariant 20h.** It was the last field of an entry
written by hand, and it is the one field whose absence nothing notices: a Budburst chapter
missing `"mode": "bud"` is indexed as a glade chapter, and every level still parses,
every board is still proved solvable, every string resolves and the build goes green —
while the chapter is gated on a stranger's stars, filed under the wrong tab and routed to
a screen that cannot play it. Sync now reads the mode off the body, `Validate Content`
fails the build on any disagreement, and a chapter holding two modes is refused by both,
because a chapter is one way of playing.

**Sync rewrites the whole manifest, and it proves it lost nothing before it saves.**
It only *derives* the chapter list; the roster and the event calendar are authored
there and it hands them back untouched. It did not always: `unlockCost` and the whole
`events` array were both added later without a schema bump, neither reached the
writer, and the first sync run after them deleted a live event and thirty companion
prices while logging success. `ManifestSync.SurvivesRoundTrip` now reads back what it
is about to write, using the same reader the game uses, and **refuses the write** if
anything differs. Add a field to `ManifestDto` and forget the writer and you get a
refusal with the field named, not a silent deletion.

A chapter file that never reaches the manifest is the one content mistake nothing
used to catch. Every reader walks the manifest, so an unlisted file is not rejected —
it is never opened, and the drop ships without it behind a green build. Sync adopts
it, and the build gate fails on one that slipped through anyway.

Drop new art into `Assets/Game/Art/…` at any point; it is given an address and
filed into the right bundle group on import. Nothing to remember, nothing to run —
*while the Editor is open*. Art copied in by a script with Unity closed arrives
unaddressed and draws as nothing, so anything that writes art while the Editor is
shut ends by telling you to run `Glimmer Grove ▸ Addressables ▸ Sync All Assets`.
The build gate's audit is what stops that ever shipping.

**Sync the manifest before you sync the assets, and this is the one ordering that bites.**
Which bundle a piece of art belongs in is derived from *which chapter claims it*, and a chapter
claims nothing until the manifest lists it (`AddressableAddresses.ChapterOwnership`). Run the
addressable sync first and a new chapter's strips and backdrops are filed as **global** art —
loaded at launch and never released, which is precisely the bound invariant 7b exists to hold.
Everything still resolves, `Validate Art` is green and the audit passes, because the art is
addressed and present; it is simply in the wrong bundle. Re-running the sync after the manifest
sync moves it, and there is no other symptom to notice.

### A chapter's art is a rule, not a decision

Two tools and one table:

```
Tools/chapters/mapart.py     which map a chapter draws, and which sky each level does
Tools/make_chapter_art.py    cuts one ordinal's map strips        (chapter_art.tsv)
Tools/make_sky_art.py        cuts the forty shared skies
```

**The map belongs to the ordinal, not to the chapter.** Every mode's first chapter draws
`map1`, every mode's second draws `map2`, and so on; a mode is told apart on the map by its
**perch** — the floating tile a node stands on, `ModeLook.Perch` — and by nothing else. That
was already the rule for the perch and is now true of the painting too, which is what stopped
Lightfall's three chapters being three unrelated places from the glade chapters' four others.
Sharing is also what puts a painting in the **global** group by itself, because
`AddressableAddresses.ChapterOwnership` files an address two chapters want as belonging to
nobody.

**The backdrop belongs to the level's place in its chapter.** Forty skies, `sky_00`..`sky_39`,
ten per ordinal — the same soft cloud painting at forty different colours, dealt round the
wheel with a stride so two levels in a row are never close in hue. Level *i* of a chapter at
ordinal *k* draws `mapart.sky(k, i)`, which is what a generator writes into the JSON.

What that buys is the reason to keep it: **a chapter published next year needs no art at all.**
It names an ordinal and gets a map and ten skies — nothing to cut, no row to add, no name to
invent, and no way for two chapters of two modes to disagree about what the second chapter of
the game looks like. It replaced forty-one backdrops cut from six different paintings, of which
a whole Lightfall chapter's ten levels shared *one*.

`python Tools/make_chapter_art.py <chapter_id> --source <pack folder>` cuts the map strips
named in that chapter's `mapStrips`, sliced **bottom upward** out of one tall image.
`Tools/chapter_art.tsv` is one row per chapter and says only which source to cut from — and
almost every row says `-`, because an ordinal's map is cut once, by whichever chapter owns it.
It **scales the map to whole strips and never stretches it**, trimming the surplus width from
the centre: a 3% vertical stretch makes every tree on the map the wrong shape, which reads as
cheapness rather than as an error.

`python Tools/make_sky_art.py --source <pack folder>` cuts all forty skies, and `--check`
proves the shipped ones are what it would cut. The grade is `make_chapter_art.vivid`, imported
rather than copied — it took four attempts to get right (see `CRAFT.md`), and a second copy
would be a second thing to get wrong.

## Token grammar

```
head + arms [+ #colour] + /startRotation [+ !] [+ ~turns] [+ &rune]
crossings: = armsA + armsB + /startRotation [+ !] [+ ~turns] [+ &rune]
briars:    % open   + shut   + /startRotation [+ !] [+ ~turns] [+ &rune]

head   -  conduit    =  crossing    %  briar    *  heart-crystal
       @  sleeping critter    .  empty
arms   any of N E S W, written in the SOLVED orientation
A+B    on a crossing: the arms of one strand, then the arms of the other, which are
       interchangeable. On a briar: the arms that are OPEN, then the arms the thorns
       have closed — and there the order is the tile.
colour R G B, Y=R+G, M=R+B, C=G+B, W=R+G+B, A=any
/0..3  quarter turns clockwise the tile starts away from its solution
!      rooted: the player cannot turn this tile
~1..9  fragile: this conduit crumbles after that many turns
&A..Z  taproot: every conduit carrying this rune turns as one
```

Every arm must be mated by its neighbour, and the board with every rotation at 0
must light every critter. The validator proves both.


**Rooted tiles (`!`) must be authored at `/0`.** Everything the validator proves is
proved against a copy of the board with every rotation zeroed, because that is the
authored solution — and a rooted tile can never be turned, so one authored away from
its solution is a tile the player is stuck with at an angle the proof never sees. What
gets proved is then a different board from the one that ships, and nothing else notices:
every arm mates, the solved probe lights, the glade draws, and par is unaffected because
`MinimumMoves` skips rooted tiles. It also makes `Puzzle.TurnsToSolution` count turns
that can never be paid, so a player who *has* reached the solution is told they were one
turn away — the near-miss line being generous, which is the single thing it exists not to
be. `CheckRootedTiles` refuses it, and asks `Puzzle.Alike` rather than `rot == 0`: a
straight conduit and a straight crossing genuinely read the same half a turn round, and
every rooted straight in the Mill Vale is one.

**Fragile conduits (`~N`)** crumble after N turns and leave a gap. Undo rewinds
the rotation but never mends them, so exploring costs something. The validator
proves each one can still reach its own solved orientation within its count —
a conduit needing three turns but surviving two is an unwinnable level that
otherwise looks perfect.

**Taproots (`&A`)** make several conduits turn together, so a tap stops being a
local act. Two things follow, and both are the validator's business:

- **A root is charged once.** Par is the sum of the turns each *root* owes, not
  each tile, because one tap moves them all — so a bound board's par is lower
  than its tile count suggests, and the star lines and the move budget (all
  multiples of par) follow it automatically. Nothing is authored for this.
- **A root must be able to reach its own solution.** Some single number of turns
  has to solve every conduit on it. If none does, the glade cannot be finished
  and looks perfectly authored — the same trap a brittle conduit owed more turns
  than it survives sets, and refused for the same reason. Note that a straight
  conduit reads the same every half turn, so it is solved at *two* of the four
  offsets and simply follows whatever the elbows on its root demand; that is
  where the interesting boards are.

A rune only one conduit carries is an error, not a shrug: it draws a binding mark
on a tile bound to nothing. A bound conduit may not also be rooted (`!`) or
brittle (`~`) — the first is a contradiction, and the second would break several
conduits on one tap when only one can be reported as the tile that gave way.

**Crossings (`=`)** carry two flows through one tile and never let them meet.
Written `=NS+EW` or `=NE+SW`: four arms in two pairs of two, one pair either side of
the `+`, and which pair you write first does not matter — the strands are
interchangeable labels. Light entering by one arm can only leave by the arm that
shares its strand.

This is the only rule so far that touches the light graph itself, and it does so by
splitting a cell rather than by changing what a join means. Four things follow, and
three of them are the validator's business:

- **A straight crossing (`=NS+EW`) can never be turned.** Rotating it swaps which
  strand is called which and nothing on the board can tell, so it is inert, owes no
  turns and costs nothing in par. It is architecture — a bridge somebody built.
- **A twisted crossing (`=NE+SW`) is worth exactly one tap, however far out it is
  authored**, because two turns is the same tile again. That is the whole of what
  `Puzzle.Alike` exists to say, and it is why every owed-turn count in the game asks
  it rather than comparing arm masks: a crossing wears all four arms at every angle,
  so a mask comparison calls every one of them solved and derives a par short by one
  per twisted crossing.
- **A crossing whose two strands are joined elsewhere crosses nothing**, and the
  validator says so. The player will spend turns routing around a separation that is
  not there. A warning rather than an error — a loop that leaves by one arm and comes
  back by another has to close somewhere, so it is a question about intent.
- **A crossing takes no colour and has no hub.** The hub disc is what this board means
  by "these arms are joined", so a crossing simply does not draw one, and the strand
  that passes over wears a shadow. That is the whole of how it is told from a
  crossroads, in any language.

What it unlocks is worth knowing before authoring with it. **A second network can now
run *through* a live one rather than only around it.** In the solution every arm mates, so
a lit cell's neighbours are lit — which used to force two networks that must stay apart to
detour around each other with a gap between them. Across a crossing they do not, and the
misrotation that joins the two is a real and recoverable trap.

**Briars (`%`)** are conduits with two of their four ways thorned shut, and one tap
swaps which. Written `%NS+EW` or `%NE+SW`: four arms in two pairs of two, the open pair
first. They are the crossing's opposite number — a bridge carries both ways through and a
bramble carries one — and they cost the light model even less: a crossing splits a cell
into two strands, where a briar leaves the graph alone and changes only *which of a tile's
arms conduct* (`Puzzle.Live`). Five things follow.

- **All four arms are drawn and all four must mate**, thorned or not. That is the whole
  point rather than an implementation detail: every one of a briar's neighbours mates it at
  every angle, so **nothing about the pipe-fitting can settle a briar** and only colour or
  the dark can. It is the cheapest honest decision a board can carry — cheaper than a
  twisted crossing, which is worth two states where a twisted briar is worth four — and it
  is why a briar may never stand on the border. `author.Board.briar` joins the four edges
  itself, because the arm an author forgets is always the same one: a thorned way carries
  no light, so nothing about the solution notices it missing.
- **The order of the pairs matters**, unlike a crossing's. A straight briar (`%NS+EW`) has
  two states and is worth exactly one tap; a twisted one (`%NE+SW`) has four and can owe
  three. No briar is ever inert.
- **A briar can never merge two networks and then keep them merged.** The pair it opens is
  merged and the pair it shuts is cut, always both at once, which is what makes the three
  shapes below the whole of what a briar can be worth.
- **Turning a briar one step has to stop the glade finishing**, and the validator turns
  every briar and every twisted crossing to check (`CheckDecidableTiles`). One that still
  finishes is a tile the player cannot place by looking, with no reason on the board to
  place it either way and a par charged for it. A warning rather than an error: a glade may
  want a bramble that is scenery on the board that teaches what a bramble is. Note what the
  question is *not*. It is not whether the tile carries light — an unlit briar is not
  evidence of anything. And it is not whether the thorns separate two networks, which is
  the reading that shipped and was wrong in both directions: it missed a briar holding
  apart two networks of the **same colour**, where opening the thorns costs nobody
  anything, and it fired on the pocket shape (invariant 5f), where the open pair is the
  only way in and both thorned ways lead back into the one grove. The consequence is the
  rule, because the consequence is what the player meets.
- **It takes no colour and it keeps its hub**, because the pair it holds open really is
  joined. What tells it from a crossroads is the thorns, drawn across the closed ways and
  swept round to the other pair by every tap.

Three shapes are what a briar is actually *for*, and it is worth knowing them before
authoring one:

1. **It cuts its own way.** Whatever the open pair was feeding goes dark. The plainest
   version, and the right one for the glade that introduces it.
2. **It joins what its thorns were holding apart.** Thorns standing between a red network
   and a blue one blend both the moment they move.
3. **It only answers to the pocket beside it.** If the open pair is part of a *loop*,
   shutting it costs the grove nothing — the light goes round — so none of the grove's
   critters warns the player, and the only thing that changed is the pocket the other pair
   just let in on. That is this file's rule about fords, and the briar is what makes it easy
   to author rather than a happy accident: put one shut arm on the live network and the
   other on a pocket that carries a heart and a critter of its own, and turning it always
   pours one colour into the other.

**There was a fifth tile and it is gone.** `x` was the **duskcap**, a creature the light had
to never reach: a glade with one woken was unfinished however many critters were awake. It
was removed because no board could ever demonstrate the rule. Every other thing a player can
get wrong here shows itself — a critter goes out — and a woken duskcap looked precisely like
a finished glade that refuses to settle, which is the one thing a board must never look like
(invariant 20g, learned again in Lightweave). The twenty-nine duskcaps that had shipped are
now ordinary critters, each standing in a **pocket with a heart of its own**: the ford still
has to be read, the wrong turn still pours one colour into another, and now the pocket's own
critter says so. `x` is a **retired head and must never be reused**, and so is the lesson id
`duskcap`; both are refused rather than ignored, because a chapter file carrying one is
content written for a build that no longer exists.

**There is no clock. A glade is graded and lost on turns alone.** Every star line
and the losing line are multiples of `par`, and `par` is derived from the board, so
a glade authors *no* difficulty number unless it wants a different budget from the
default. Three stars is `ceil(par × 1.20)` turns, two is `ceil(par × 1.40)`, and the
run is lost at `ceil(par × 1.60)` — computed from exact hundredths, because `1.20f` is
1.20000004768… and a float product put three stars a turn out on every par where it should
have landed on a whole number.

**The three lines are even thirds of the slack, and that is what keeps them all landable.**
A run can only ever score inside `[par, par × 1.60]` — 0.60 par of slack — so it is cut into
three bands 0.20 wide. Change one and you change all three: this was learned by shipping a
1.60 budget against a 2.00 two-star line, which put two stars *outside* the survivable range
and made one star unscorable by anybody, with every number still looking plausible and every
board still validating green.

**Why the countdown went.** A timer prices *thinking*, and thinking is the whole
product — every rule the board has (brittle stone on a crossing, taproots whose
members the arms cannot settle, a ford on a cycle) exists to force a
decision, and a clock makes flailing the dominant strategy. Stars were the *worse*
of the turns and the time, so the turn thresholds — the only half that measures
whether the board was solved well — decided nothing for any player who stopped to
think, and the third star asked 1.35 sustained taps a second, which is a motor
threshold rather than a puzzle one. It also scaled with the wrong thing: the limit
came off par, and par is *length*, so a long dot-to-dot got a generous clock and a
short board full of twisted crossings got a tight one.

**Move budget.** Every glade gets one automatically: `ceil(par × 1.6)`. Override with
`budgetFactor` on a level, or set it negative to remove the budget entirely — which
the first glade in the game does, and nothing else. Running out costs a heart.

**An authored `budgetFactor` means exactly what it says — there is no floor.** `MoveBudget`
used to clamp to one turn past the two-star line so a run still earning stars could never be
the run that ended. That floor is gone: with the clock removed this is the only way to lose a
glade, and a fail line past the point where the player has already stopped earning anything is
a formality rather than a fail state.

**A player can now lose a run they were on course to two-star**, which is deliberate:
running out costs a heart and pays nothing, and that is the whole rule.

**Moving `budgetFactor` on a level means moving its star factors too.** They are one decision
in three numbers. `Validate Content` and `Tools/verify/content.py` both prove the ordering and
say which band you stranded:

| what you set | reported as | why |
|---|---|---|
| `goldFactor >= silverFactor` | **error** | the two-star band is empty |
| `budgetFactor <= goldFactor` | **error** | no run can be graded at all |
| `budgetFactor <= silverFactor` | **warning** | one star can never be scored |

Both read the **factors**, not the turn counts they derive: on a board of par 1 or 2 all three
round onto the same number however they are set, and reporting that would be a complaint about
board size rather than about tuning.

Do not reach for these to make the game harder in general — that is the boards' job (see *What
makes a glade hard*) and the budget's. And note what a star retune does **not** touch: the
ceiling. Three stars a level, over however many levels the catalog holds, exactly as before, so
earned credits are unmoved at the top and only the standard of play needed to reach them has
changed. A drop that *adds* levels raises the ceiling, which is what a drop is for; a retune must
not.

**`Undo` refunds a move, is unlimited, and a hint charges none**, so the meter counts
*committed* wrong turns and nothing else. Trying a crossing that reads the same half a turn
round and taking it back is free — which it has to be, because that is correct play rather
than flailing. That is what makes a budget this tight fair: the default came down **2.60 →
2.30 → 1.60** as the clock was removed and the budget became the only fail state, and 2.60
had been chosen while the clock ended lost runs first.

**Par is length, not difficulty.** A chapter's pars should *not* rise monotonically —
ten rising numbers read as a treadmill, and a low-par board that is hard to think
about is a better change of pace than a long one. What makes a glade hard is
*decisions*, which is a countable thing: see **What makes a glade hard** above and
`Tools/verify/difficulty.py`. Aim a chapter at a *clear rate* rather than at a
feeling — around 85% of first attempts early, around 60% late, with finales lower.

## Hollow levels

A chapter chooses how it is played. `"mode": "hollow"` on a manifest chapter entry (absent means
`"glade"`) says its levels are hollows rather than conduit boards. A hollow has **no `rows`** at
the level's top level — it carries a `hollow` block instead, and a level must have exactly one of
the two:

```json
{
  "id": "h01_first_ember",
  "mapX": 0.30,
  "mapY": 0.05,
  "hollow": {
    "rows": [
      "R>G G>R R>G A>G",
      "G>R R>G G>R R>R",
      "R>G G>G A>R G>R",
      "G>R R>G G>R R>G"
    ],
    "sparks": "RR"
  }
}
```

**The rule in one paragraph.** Every critter wants a colour (its *ring*) and gives a colour (the
*pip* on its shoulder). Spend a spark on a sleeping critter and that light lands on it; when the
light on a critter contains what it wants, it wakes and hands its own colour to the four critters
beside it — which may wake them, and so on. Light accumulates and never decays. You win when
every critter is awake; you lose when the sparks run out and one is not.

**The vocabulary**, one space-separated token per cell:

| token | means |
|-------|-------|
| `.` | nothing stands here |
| `R>G` | a sleeping critter that needs red and gives green |
| `A>B` | needs *any* light at all, gives blue |
| `Y>R` | needs yellow — red **and** green have to reach it — and gives red |
| `*G` | a heart: awake before the run starts, giving green from the first frame |

Colours are the board's own letters: `R G B` and the mixes `Y M C W`, plus `A` for "anything".
A token may end in `:n` to choose which critter flipbook draws it; left off, one is picked from
the cell's coordinates, which is varied, stable and nothing an author has to think about.

**`sparks` is the whole difficulty.** It is the queue, in the order it must be spent — `"RGB"` is
red then green then blue — and it is also the fail state. Nothing else about a hollow is authored:

- **Never author a par.** Par is the fewest sparks that finish the board and is found by search
  (`HollowSolver`), because it *is* the star ladder here — three stars is finishing in exactly
  par — and a typed one that drifts by a single spark either hands three stars to a careless run
  for ever or makes them unreachable. `Validate Content` prints the par it found, the slack the
  queue leaves over it, and how many positions the proof took.
- **Give exactly one spare spark.** Par sparks is the three-star line; one more is worth two
  stars. Past two spare, most wrong answers still win and the board stops having a shape — the
  validator warns.
- **The queue is ordered, and that is the puzzle.** Light never decays, so *which* cells are
  sparked could not otherwise matter in what order. An ordered queue makes the decision an
  assignment: this red has to go somewhere now, and the green behind it can only reach what the
  red left asleep.

### What makes a hollow hard

`wins` — the number of distinct winning states at par — is the number that matters, and
`Tools/hollow/generate.py` searches for boards against it. Par is a weak knob on a connected
field: light spreads far, so most boards are one tap and a demanding one is two, and pushing
needs harder to raise par tips straight over into unsolvable (at a blend rate of .65 on a 6x6,
six hundred candidates in a row could not be finished at all). The knobs that work are **size**,
**how many needs are blends**, and above all **how few openings win**. An early hollow has dozens
of taps that work; a late one has two.

Boards are *searched for* rather than typed. `Tools/hollow/` holds a mirror of the rules in
Python, a generator, and `build_chapter.py`, which writes the chapter body and prints the ladder
as a table. The mirror is a second copy of the rules and is therefore never authoritative — the
shipping C# solver is what `Validate Content` runs, and any disagreement is a bug in the mirror.

## Lightfall levels

`"mode": "fall"` on a manifest chapter entry says its levels are **wells**. A well carries a
`fall` block instead of a `rows` grid, and a level must have exactly one of the two:

```json
{
  "id": "f01_first_fall",
  "mapX": 0.32,
  "mapY": 0.05,
  "fall": {
    "width": 4,
    "height": 6,
    "rows": [
      "....",
      "....",
      "....",
      "....",
      "Y.YY",
      "YYYM"
    ],
    "motes": "BGRGBR"
  }
}
```

**The rule in one paragraph.** Tap a column and a mote of pure light falls into it. If the mote
on top of that column lacks the colour, the two blend and the stack does *not* grow; if it
already holds the colour, the new mote comes to rest above it and the stack is one row taller.
A mote holding all three channels **bursts**, and the burst washes the colour that finished it
into the motes beside it — so any of *them* that is thereby completed bursts in turn, and one
well-chosen drop runs through a whole connected blob. You win when the well is empty. You lose
two ways: the supply runs out, or a mote comes to rest above the **brim**, which is row nought.

**The vocabulary**, one letter per cell, no spaces needed:

| letter | means |
|--------|-------|
| `.` | bare ground |
| `R` `G` `B` | a mote of one channel: two more to go |
| `Y` `M` `C` | a mote of two channels — *ripe*, and one drop from bursting |
| `W` | refused: a well that bursts before anybody touches it is a board its author did not mean |
| `O` | an empty **lens** — glass, which fills with light and then fires |
| `r` `g` `b` `y` `m` `c` | a lens already holding that much. Light is upper case and glass is lower |
| `w` | refused, for `W`'s reason: a lens holding all three fires the moment the board is read |
| `@` | a **whorl** — a mouth that draws the motes either side of it together and mixes them |
| `1` `2` `3` | refused: these were a **wick**, and the wick was withdrawn (invariant 26h) |

**The lens** (Glasswater, `f02`) is the one thing in a well that is not made of light. Nothing
cooks it by landing on it; what it does is *fill up*, one channel at a time, from any burst
beside it, from another lens's beam, or from a drop taken straight in — and when it holds all
three it **fires white**, each beam crossing bare ground until the first cell in its line takes
it. A beam is white, so whatever it lands on is *completed* and pops whatever colour it was.

Every wave of one drop carries that drop's colour, so **a lens gains at most one channel per
drop**: filling an empty one costs three separate drops of three separate colours, each
engineered to burst beside it. How full each pane starts is therefore a chapter's whole
difficulty ramp — measured, two-thirds-full glass leaves 50 boards in 90 solvable where empty
glass leaves 7. A lens charged the ordinary way fires **sideways**; one *struck by another
lens's shot* fires along all four axes, which is the chain a chapter of glass is built on.

**The whorl** (Whorlwater, `f03`) holds no light at all. **Anything** opens it — a burst beside
it, a lens beam, or a drop straight onto it — and on the *next* wave it draws in whatever is
standing to its **left and its right**, leaves one mote holding both, and is gone.

That is the mode's own arithmetic on a pair of operands it never had. Every other rule here adds
a *colour* to a cell: a drop adds one channel, a wash adds one, a beam adds all three. A whorl is
the only place two **motes** are combined — so a cyan and a red that were each a drop away from
white are none. Nothing has to be taught for it: a player who has cooked one mote already knows
what yellow and blue make.

**It pulls sideways, and that is a fact about gravity rather than a choice.** The well falls, so
across is the one direction nothing here ever travels in — the same observation that makes a lens
fire sideways. It is the only object in this mode that *moves* a mote.

**The trigger is free and the pair is what costs.** What a whorl gives back is decided entirely by
what stands either side of it **at the instant it turns**, and the well collapses under every
chain — so authoring one means arranging two particular motes into two particular cells, and
opening it early spends it on a pair that was not ready. A lens asks for three drops of three
colours in any order at all; a whorl asks for one arrangement.

Three rules it is held to, and each is checkable:

- **A whorl draws light and nothing else.** Glass beside one stays where it stands, and so does
  another whorl.
- **One with nothing beside it closes and is gone**, which is what keeps a well of them winnable
  and a continue on one honest.
- **Never against a wall.** Gravity only moves a whorl *down*, so the two columns it can ever draw
  from are the two it is authored between — one in column nought or the last column has one side
  for its whole life and can never merge a pair. `ContentValidation` warns about it.

**Its two predecessors are worth knowing about before adding a fourth object.** The third chapter
shipped a *mirror* that turned a lens's beam ninety degrees, and then a *wick* that washed one
authored colour into the four cells beside it. Both were reported inside a session and withdrawn:
the mirror had no event of its own, and the wick had one and no **decision** in it — its colour
was the author's, its trigger was free, and its effect was identical on every board it stood on.
A lens delivers all three channels at range, so anything that also delivers light is competing on
degree and will lose. See invariants 26g and 26h for the test to apply instead: *what can it do
that a lens cannot*, and *what does the player decide about it*.

`motes` is the procession, in the order it is dealt, written in `R`, `G` and `B` only — dealing a
blend would hand the player a step of the cooking for free. **It repeats**, so it never needs to
be longer than one lap, and it must carry every channel the board is missing or those motes could
never be finished however many drops were bought.

**Nothing else about a well is authored.** Par is the fewest drops that empty it *without ever
breaching the brim*, found by search (`FallSolver`). Three stars is `par x 1.20` and two is
`par x 1.40`, as everywhere else — but the **supply is `par + spare`**, a count of wasted drops
rather than a multiple of par, because a wrong drop here is permanent *and* leaves a mote that
still has to be cooked, so a mistake costs about two drops wherever it happens. `spare` defaults
to 5 (two mistakes and a little) and is authorable per level; see invariant 26e for why a factor
could not do this job. A typed par is the failure with no symptom — one too high hands three
stars to a careless run for ever, one too low makes them unreachable, and neither is visible in
the file that caused it.

Two rules the validator holds a well to that are easy to get wrong by hand:

- **The brim row must be empty**, or the level begins in its own fail state.
- **Nothing may float.** The well settles the instant anything bursts, so a mote with nothing
  under it is a mote drawn in one place and met in another. Glass and whorls fall too.
- **A whorl needs nothing.** A drop opens one and one with nothing beside it closes, so whorls are
  always removable and a well of them is always winnable — which is why there is no rule here
  about them, and why `FallVerdict` needs no clause either. That is deliberate: the lens shipped
  without the equivalent property and had to have a valve added after a player reported being
  stranded.

`budgetFactor: -1` turns the supply off entirely, and exactly one level in the game uses it: the
first well, for the reason the first glade cannot be lost either. It is also what keeps the
supply lesson off that board — a lesson shown over a meter that is not there can never be shown
again.

### What makes a well hard

Four dials, and **par is not one of them**. Par is length: a big well cleared by four huge chains
is a shorter number than a small one that has to be picked apart. The dials are:

- **Headroom** — how many rows the tallest column has before the brim. This is the one that makes
  every individual drop frightening, because a wasted mote costs a row as well as a mote. A
  teaching well leaves four; a finale leaves two.
- **What is standing in it** — how many motes, and how much of the well is blends (ripe, one drop
  from bursting) against pure colours (two drops away, and only reachable by a wash from
  somewhere else).
- **`ways`** — how many distinct shortest solutions there are. This is invariant 5d, counted: a
  well with hundreds of them is one where almost any tidy play wins, so the colours and the
  ordering are deciding nothing however pretty it looks. It falls down a chapter.
- **`greedy`** — whether a player who never looks ahead, always taking the biggest burst going,
  clears the well inside its supply. On a chapter's opening wells they should; that is what
  teaching the verb looks like. By the middle they should not.
- **`aim`** — how many of a lens's two sideways shots land on anything.
  Glass pointing at nothing has taken three drops of charging and bought nothing. It is geometry
  of the authored position rather than a proof — the well collapses under a chain, so a lens
  fires from wherever it has fallen to — so it warns and never refuses.

`Validate Content` and `Tools/verify/content.py` both print all four beside par, the two star
lines, the supply and the number of positions the proof cost.

**Boards are searched for rather than typed**, because a random fill is almost never solvable —
every pure mote needs two more channels and the stragglers pile up faster than any chain clears
them. `Tools/chapters/f01_lightfall.py` holds the shipped ten and self-checks against the JSON;
`Tools/verify/fall.py` is the rule mirror it and the offline gate both run. The mirror is a
second copy of the rules and is therefore never authoritative — `fall-vectors.json` is the
contract between it and the shipping C#, and both sides run it.

**Keep a well cheap to prove.** The player's device runs the same search — once, lazily, when
somebody opens the level (`LevelTuning.Par` resolves the first time anything asks, which is the
run screen and never the map). The validator **warns above 40,000 positions and refuses a level
above 120,000**, which is about a quarter of a second of nothing happening on the way in. Cost
goes as the column count to the power of par, so par 7 on a six-wide well is four times par 6 on
the same board: narrow the well or shorten the answer, and start it fuller rather than making it
bigger.

## Budburst levels

`"mode": "bud"` on a manifest chapter entry says its levels are **groves**. A grove carries a
`bud` block instead of a `rows` grid, and the block is a grid, a basket and nothing else.

```json
{
  "id": "b01_firstburst",
  "mapX": 0.3,
  "mapY": 0.08,
  "bud": {
    "width": 7,
    "height": 6,
    "rows": [
      "GYRYBBR",
      "BRoBoYG",
      "RBCRGRY",
      "GRoYoGY",
      "BBCRYRR",
      ".GGRYG."
    ],
    "colours": "GBR"
  }
}
```

Ten characters, and they are the whole vocabulary:

```
.              bare ground
R G B          a flower in a pure colour
Y M C          a flower in a blend - yellow (R+G), magenta (R+B), cyan (G+B)
W              a flower holding every channel. It can never be mixed into again
o              a cocoon with a critter in it - one crack opens it
O              a cocoon that takes two
#              old wood - no bunch and no wash crosses it, and nothing grows on it
```

**A grove with a `regrow` strip is a *living* one** and behaves differently enough to be worth
saying once: what bursts leaves a hole, everything above slides down into it, new flowers arrive
along the top from that strip, a white flower is a **bomb** rather than a wall, and one flower
ripens on its own between taps. A living grove is therefore authored as a **full rectangle** —
bare ground and old wood are both refused on one, because the first chain fills every hole and
they never come back. Everything shipped is living; a grove without a strip is the shape this
mode shipped with and still parses.

`colours` is the **basket**: the colours dealt one per tap, repeating for ever. It is written in
`R`, `G` and `B` only — **a blend is never dealt**, because a blend handed over is the one decision
the mode has in it. Anything else is refused rather than ignored.

The letters are the game's own — `Energy.Letter`, the same ones a glade's critters and conduits
use — so the arithmetic an author reasons in is the arithmetic four chapters of glades already
taught the player.

**Every flower is drawn as the same four-petal shape whatever colour it is**, except `W`, which
gets eight — because white is the only one whose difference is a *rule* (nothing can be mixed
into it) rather than a colour. So an author composing a grove is composing in colour alone, and
what reads as a bunch on paper reads as a bunch on the board. `BudFlower` is the single answer to
that and the legend above the grove asks the same one, so a chip and a cell can never disagree.

**A grove's celebration is content only in its words.** The four rungs a chain earns — GREAT,
AMAZING, EPIC, LEGENDARY — are `mode.bud.chain_*` strings and nothing else: which rung a chain
lands on is `BudChain.WordKey`, how loudly it is drawn is `BudChain.WordPointsFor`, and how long
it all takes is `BudTempo`. If a chapter ever wants a different voice, translate the four
strings; do not add a fifth rung without moving the ladder, because the rung index is what picks
the colour and the size.

**The colour legend above the grove is derived, so there is nothing to author.** `BudMixing`
builds the three recipes from the same masks the board mixes with, and each is drawn on a card of
its own so the row reads as three statements rather than as thirteen things in a box. It takes a
strip off the top of the screen (`BudBand.BoardCeiling`), which is checked by `BudLegendTests`
rather than argued about — do not add a fourth chip without re-running it.

**There is no difficulty number here and there must never be one.** Par is the fewest taps that
free every critter, found by search, and both star lines derive from it. Room above par is
`spare`, counted in **taps**, and defaults to 5.

### Runners — the second chapter's one new thing

A grove may author a **second grid** of the same size, `runners`, which strings vines across it:

```json
"bud": {
  "width": 7,
  "height": 6,
  "rows":    ["?o?????", "..."],
  "runners": [".......",
              ".a.....",
              ".......",
              ".......",
              "....a..",
              "......."],
  "colours": "GRB",
  "regrow":  "RGBYMC"
}
```

One lower-case letter on each of a runner's **two** ends and `.` everywhere else. A letter
written once, or three times, is refused — a runner has two ends and nothing knows what a third
would mean. Different letters are different runners; `a` is not special, and the letters are
re-derived on a round trip, so which one you pick changes nothing.

```
a bunch that TAKES IN a runner end sends that bunch's colour to whatever is
standing on the other end, however far across the grove that is.
```

Four things about it are worth having in front of you before authoring one.

**The end has to be *in* the bunch, not beside it.** A wave going off next door to a vine does
nothing at all. That threshold is the whole mechanic: a spread that fires on anything happening
nearby walks outward for ever and makes every board more solvable (invariant 20j), where one that
has to be *built into* is a thing the player arranges. It is also the only mistake a runner can
punish, which is why a vine that fires and delivers a colour the far end already wears is drawn
in full and lands on nothing — the player has to be able to watch that happen.

**A vine belongs to the ground, never to the flower.** Everything on a living grove falls, so the
two squares are joined for the life of the level and whatever is standing on them at the moment a
bunch goes off is what the runner carries between. An end authored under a cocoon is legal and
warned about: that half of the vine is inert until the grove falls something else onto it.

**A bomb sends nothing.** A runner carries a *bunch's colour*, and a white flower going off has no
colour to carry — it clears a block. One clause, and it stays one clause.

**Author the far end's neighbourhood, or the vine is scenery.** This is the part a sweep will not
find for you. The motif the Tanglewood teaches with is four cells:

```
near end  R with a Y beside it        tap it with G in hand -> R|G is Y -> three Y burst,
far  end  R with a Y beside it        and the Y that runs down the vine does it again
```

Two readings say whether it worked, and both are printed by `content.py` and
`Glimmer Grove > Validate Content`:

```
changed   opening taps that play out differently with every vine cut.
          WARNED AT 0 - the runners are scenery on the board as dealt. This is
          invariant 26g's test: replace the new object with the nearest existing one
          and see whether anything changes.
caught    opening taps that burst MORE because a vine carried. The number to author
          against - it is the arrangement the player is making, rather than merely
          the vine existing. Not gated, because a chapter's first grove may
          legitimately teach with one that only changes the grove.
ran       how many vines the *best* opening tap fires, which is what decides whether
          anybody will ever watch one.
```

Two structural refusals, both provable by looking rather than by searching: a vine joining two
squares that already **touch** (a burst washes there anyway, so it can never deliver anything the
wave was not delivering), and an end standing on **bare ground or old wood** (nothing a bunch
could ever take in). A vine spanning fewer than three squares is warned about — that is about as
far as one wave reaches on its own. At most `BudLayout.MaxRunners` (6) to a grove, which is a
readability bound rather than a cost one.

### The rule, in three lines

```
tap a flower      the colour in hand is OR-ed into it. Red + green in hand = yellow.
                  A tap that would change nothing is refused, not swallowed.
three alike       any bunch of 3+ touching flowers of one colour bursts.
a burst washes    its colour into every flower it touches - and down any vine whose end
                  it took in - which makes more bunches, which makes more. A cocoon
                  beside any of it takes one crack per wave.
```

### What makes a grove good

Not hard — *good*. This mode is built against a feeling rather than a difficulty (invariant 20k),
so two of the usual readings are read backwards:

```
ways      how many different plays of exactly par taps win.
          WARNED BELOW 2 - one shortest play means the grove is a puzzle, which this
          mode is deliberately not. Everywhere else in this game a high count is the warning.
careless  what a player who always taps whatever sets off the biggest chain spends.
          WARNED WHEN THEY CANNOT FINISH - this is the bar rather than a difficulty
          reading. Everywhere else a careless player finishing is the complaint.
nodes     what proving par costs, which the player's device pays once per level.
          Warned above 20,000, refused above 60,000. Branching is the flower count, so the
          cheap fix is a shorter answer - a cocoon nearer the fuse, never a bigger board.
changed   on a grove with vines: opening taps that play differently with every vine cut.
          WARNED AT 0. See *Runners* above.
```

**Par is 3 on every grove this mode has shipped, and that is arithmetic rather than a
preference.** Cost goes as the flower count to the power of par, and the player's own device runs
this search when it opens the level (invariant 26d), so a par of 4 on a grove big enough to
cascade is refused outright by the node ceiling — and one small enough to prove comes back at
twenty flowers with a *one-wave* best tap, which is this mode with the mode taken out of it. So a
chapter's ramp is spent on what does not multiply the search: **how many are shut in**, how much
grove there is, how many cocoons take two cracks, how many vines are strung across it, and
whether a careless run still scores three stars.

**`Tools/chapters/budforge.py` is the sweeper**, and both shipped chapters were found with it:
draw the skeleton — where the cocoons sit, how big the grove is, where the vines run and the four
cells of the teaching motif — and it searches the *fill* against `Tools/verify/bud.py`, the same
mirror the build gate runs. Its header carries the three facts that decide how a sweep has to be
run, each of which cost a day to find.

**Author the layout, then sweep the basket.** That split is what makes this mode cheap to author:
the layout decides what the grove *looks* like and the basket decides how it *plays*, and only the
second is worth searching. Twenty-four baskets gave the shipped layout a par of 3; the one taken
deals all three colours, so the rotation the player can see under the grove is visible on the very
first board rather than being a rule they take on trust.

**Blends are what make a chain possible, so a grove of pure colour is flat.** A red beside a green
is one tap from being two yellows; two yellows and a red are one tap from a bunch. The shipped
board runs about half blends, which is what gives it a thirteen-flower opening tap and still a par
of 3.

**A board must be authored settled.** Three alike already touching bursts in the first frame — the
player is shown a chain they did not cause, and par is measured against a position they never met.
Both gates refuse it and name the cell.

**Where the cocoons go decides par far more than how many flowers there are.** A cocoon inside the
fuse is freed by the big chain for nothing; one out at the edge needs a tap of its own. The shipped
board has three of each, which is why its shortest answer is one huge tap and two small ones.

**Every cocoon needs a flower beside it.** Nothing in a grove ever grows one back, so a cocoon
walled in by bare ground and old wood can never be cracked — the build gate refuses that by reading
the board rather than by searching it, because "nobody can finish this" tells an author nothing
about what to move.

**Watch the derived star lines on a short par, because no gate does.** `CheckStarBands` reads the
*factors* rather than the thresholds, deliberately: at par 1 or 2 all three round onto the same
number however they are set, and reporting that would be a complaint about board size. On this
mode pars are that short, so it is worth reading the tool's own output — the shipped board was
moved from par 2 to par 3 for exactly this reason, since `ceil(2 x 1.20)` and `ceil(2 x 1.40)` are
both 3 and the two-star band was empty.

**A white flower is a wall, not a flower.** It holds every channel, so nothing can be mixed into
it — it will never burst and never join a bunch. One or two make a grove read richer; a grove of
them is a board that can be neither won nor ended, and `BudBoard.AnyMove` is what notices.

## What makes a glade hard

`Tools/verify/difficulty.py` answers this in numbers rather than in opinions, and it is
worth reading before authoring a board, because the first cut of two whole chapters got
it wrong in a way nobody could see by looking:

```
python Tools/verify/difficulty.py                       # every chapter
python Tools/verify/difficulty.py c02_millvale --detail # one, per tile
```

It enumerates every arrangement in which **every arm mates and none dangles** — the tidy
boards a player might plausibly arrive at — and then asks which of them actually win.

```
glance   tiles a player cannot place by looking at that tile and the ground around it
arms     tidy arrangements the board admits
wins     those of them that also light every critter
decided  tiles whose orientation only colour can settle
```

Two findings drove the rebuild of chapters two and three, and both are general.

**Open ground is what makes a board easy.** A tile with four neighbours has four candidate
orientations; a tile with one has one. The first cut of both chapters was corridors a tile
or two wide with empty cells either side, so almost every tile read at a glance and the
glade was a dot-to-dot. **Fill the ground.** Two things then matter: avoid four-armed
conduits, which are inert and so read as nothing; and hang whatever is left over on short
chains ending in critters rather than running a spine along the board's edge, because a
straight or a tee on an edge is forced by the edge and a critter or an elbow is not.
Measured on the shipped 7x7s, that is the difference between `glance 21/49` and `40/49`.

**When `arms` is 1, every mechanic except the arms is decoration.** Twenty-two of the
game's first thirty glades had exactly one tidy arrangement, which is the same as saying
their brittle stone, taproots and fords rejected nothing and could all have been deleted
without changing a single solution. The player fits pipes, the lights come on, and the
crossing they never thought about was never turnable.

**A four-armed tile is the cheapest honest decision a board can carry**, and everything
else rides on it. A twisted crossing or a briar wears all four arms at every angle, so
nothing about the arms can settle it — only colour can. Three of them is eight tidy
arrangements with one winner. A **briar** is the stronger of the two and usually the one to
reach for: a twisted crossing has two states where a straight briar has two and a twisted
one has four. That is where the rest of the vocabulary gets its teeth:

- **Brittle stone belongs on a tile the player cannot simply try**, which in practice means
  a crossing or a briar. `~2` on a conduit owed one turn is exactly one wrong guess; `~1` is
  none, and a crumble ends the run, so save it for a finale. Brittle on a tile the arms
  already force asks nothing of anybody.
- **A taproot's members should all be tiles the arms cannot settle**, for the same reason -
  bind two briars in opposite corners and one tap answers both. Bind tiles the arms already
  force and the root is a hint, not a decision. The reading prints what the binding removes.
- **A ford must sit on a *cycle* of the live network, and what it lets in must have a
  heart of its own.** This is the one that is easy to get wrong and impossible to see
  afterwards. Turning the ford has to matter *while the grove's own critters stay lit* — so
  it stands on a loop, where shutting a way costs the grove nothing, and the pocket on the
  other side carries a heart and a critter of a different colour. The wrong turn then pours
  one into the other and exactly one critter goes out: a warning, but somewhere the player
  is not looking. `colour` in the reading counts the arrangements the critters alone reject,
  and it is what these boards now score on. A briar makes it straightforward: stand one on a
  loop with one thorned arm on the live network and the other on the pocket. Every ford in
  the Mill Vale, the Amberwood and the Nightbriar is built that way.

**`hazards` is the metric this replaces, and it is worth knowing why it was wrong**, because
a whole chapter was authored to it. It counts places where *some* rotation would mate two
networks — but a rotation that does that usually leaves an arm dangling somewhere else, so
it is not an arrangement a player ever plausibly reaches. A board can score twenty-nine
hazards and admit exactly one tidy arrangement.

Nothing here fails a build. `Validate Content` remains the authority on whether a glade is
*sound*; this says whether it is *worth playing*.

**Tips teach themselves.** A glade that contains a mechanic the player has never
met shows a one-off spotlight tip on entry — no authoring, no list to maintain.
Adding a mechanic means adding it to `Mechanic.TeachingOrder` and writing
`ui.tip.<id>.title` / `.body`; every chapter that uses it is then covered forever.
Only one tip is shown per glade, most dangerous first.

## Inheritance

A level inherits `accent`, `slate` and `backdrop` from its chapter unless it sets
its own. `accent` and `slate` are the board's own light and its plate, which is where
a glade's identity belongs. `backdrop` is no longer a choice: every level but the
first writes the sky its ordinal and position give it, and the first inherits the
chapter's, which is that same sky. `mapX`/`mapY` are fractions of the *chapter's*
band of the map, not of the whole map, so chapters stay independent.

A *chapter* inherits nothing. It must name its own `backdrop`, and validation fails
if it does not — the check is unchanged even though what it names is now derived,
because a chapter drawing art it never asked for is a chapter nobody decided the look
of. What it names is shared, and shared art goes in the global group, which the
Addressables tooling works out for itself.

## Companions

The profile roster lives in `manifest.json`, beside the chapter list:

```json
"companions": [
  { "id": "monarch", "portrait": "monarch", "animated": "c5",
    "unlockLevel": 0, "unlockCost": 0, "disabled": false },
  { "id": "cinder", "portrait": "cinder", "animated": "",
    "unlockLevel": 2, "unlockCost": 800, "disabled": false }
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
- **`unlockLevel`** is a keeper level. Reaching it is *derived* and never stored — the same
  argument as derived XP — so it can be retuned for existing players. **Exactly one
  companion should be free at level 1** (the starter every account begins wearing);
  `Validate Content` fails the build if none is.
- **`unlockCost`** is credits that buy the companion outright, ignoring the level. **Zero or
  absent means it cannot be bought** — earned by playing or not at all. That sentinel is the
  safe direction rather than an accident: `JsonUtility` writes a zero into a field an older
  manifest never had, so reading zero as "free" would put the whole roster on sale for
  nothing. A purchase *is* stored, because nothing observable implies "this player paid
  8,000 credits" — see below.
- **`disabled`** retires a companion without deleting anyone's choice of it.

Adding one is a portrait, a manifest entry and a `ui.avatar.<id>` string. No code changes,
and no app update once remote delivery is on. A companion's name key is derived from its
id and cannot be overridden, exactly like a level's; `Validate Content` checks each one,
because the source scan cannot see a key that is never written as a literal.

Both `unlockLevel` and `unlockCost` were added **without raising `ContentSchema.Version`**:
an older client ignores an unknown field and falls back to the roster it shipped with, which
is a working game rather than a refused manifest. Raise the version only for a change old
clients could not survive. A client that has not learned about prices simply shows the
companion as level-gated, which is what it was.

### Pricing a companion

Two facts decide a price, and neither is visible in the manifest, so `Validate Content`
prints and checks them.

**Most of the roster is unreachable by levelling, for years.** Three-starring an entire
hundred-glade catalog reaches about keeper level 15, and roughly level 24 after a year of
drops. Any gate above that is reached by coins or not at all — which is why a gated
companion with no `unlockCost` is a build **error**, not a warning: no player could ever
obtain it.

**Ordinary play pays about 540 credits a day** before any rewarded video — three daily
chests plus a streak rung, all read from `progression.json`. `Validate Content` derives that
figure and reports the whole roster in days, so a ladder that outlasts the content is a
number somebody chose rather than one nobody noticed. The shipped ladder runs 800 → 30,000
across 30 companions (about 270,000 credits, ~16 months of play), rising with the gate.
Three checks guard it: a price under half the account seed on a gated companion is an error
(the gate would not bind on a fresh install), a later gate costing less than an earlier one
warns (the ladder is inverted), and a cheapest companion more than a week away warns
(nothing teaches a new player that coins buy friends).

Purchases are stored — save schema v12, `companionsOwned`, a set of permanent ids joined by
**union**, which is the only mergeable shape (see invariant 11b) because buying is
irreversible. The set is client-written and therefore forgeable; it buys a portrait and
nothing else, and the money half is defended by `submitSpends` refusing a debit the
server-derived balance cannot cover. See `CompanionLedger`, which owns the composite
"level **or** purchase" rule — nothing else composes it.

Portraits live in their own Addressables group and load into
`AssetLibrary.CompanionScope` when a roster screen opens, then drop when it closes. Only
the worn companion stays resident, warmed at boot by `Profile.WarmWornAvatar`. That is
what keeps launch costing the same at a hundred companions as at five.

## The Grovement

The player's own grove: a floor they own and expand, creatures they earned, and decor they
bought. It lives in its own file because it is a **body**, not index knowledge:

```json
{
  "schemaVersion": 3,
  "floor": {
    "cols": 14,
    "rows": 14,
    "tileArt": "",
    "hallTile": "t_006_006",
    "starterTile": "t_007_006",
    "regions": [
      { "id": "hearthstead", "col": 4, "row": 4, "cols": 6, "rows": 6, "cost": 0 },
      { "id": "east_meadow", "col": 10, "row": 4, "cols": 4, "rows": 6, "cost": 2500 }
    ]
  },
  "pieces": [
    {
      "id": "fence_low",
      "art": "Homestead/fence_low",
      "kind": "decor",
      "cost": 340,
      "bundle": 10,
      "scale": 1.0, "lift": 0.45
    }
  ]
}
```

`manifest.json` carries only `"groveVersion": 5` for it — bump that when the file changes so
the refresher pulls it, exactly as `progressionVersion` works for the reward table. It is read
when the player opens the Grovement and its art is dropped when they leave. That is invariant 4a
applied to the thing most likely to break it: a shop is the part of a game that grows fastest,
and hundreds of pieces parsed at every launch to answer a question nothing on the boot path asks
is a cost paid forever by every device.

### What is stored and what is derived

Three things reach the save file (schema **v17**), and they are joined differently:

| | shape | merge | invariant |
|---|---|---|---|
| **Residents** | nothing here | — | they *are* companions; see below |
| **The home** | nothing extra | — | derived from `homesteadOwned` |
| **Purchases** (`homesteadOwned`) | set of ids | union | 15 — an entitlement |
| **Land** (`groveLandOwned`) | set of region ids | union | 15 — an entitlement |
| **Arrangement** (`homesteadPlaced`) | tile → piece + stamp | recency per tile | 11c — an instruction |

**Land is the one thing that stopped being derived.** An island used to be held when its chapter
was finished — a question about the star ledger, so it recomputed everywhere and left nothing on
disk. Land bought with credits cannot be, so it is stored, and it is stored per **region** rather
than per tile: both are legal shapes and only one stays small, since a filled floor is a couple of
hundred tiles and a set that size is merged and checksummed on every sync for ever.

**Starter land has no price and is never written down.** A region with `cost: 0` is owned by
everyone, so recording it would be a stored default that says nothing — and "absent" and "bought
nothing" have to stay the same fact for the union to need no sentinel.

**Owning a piece is permission to draw it, not possession of a copy.** A player holding
`fence_low` may put it in one slot or in twelve. That is not a simplification: a count of
copies held is exactly the stored count invariant 11b forbids, and hearts spent a schema
version proving it. It also makes the better shop — variety rather than quantity is what
makes two groves look different.

**A tile id is written into save files.** Invariant 1 applies in full. Three rules follow and
none of them is optional:

* Tile ids are **absolute floor coordinates** (`t_006_006`), never region-relative. So re-drawing
  which region a tile is *sold* in changes what you must buy to reach it and never changes what is
  *standing* on it.
* They are **zero-padded to three digits**, because `SaveDelta` walks the placement rows in order
  and an ordering that changed with the size of a number would make an unchanged save read as
  changed on every launch.
* The floor may **only ever grow right and down**. A column inserted at the left would renumber
  every tile in the world.

### Authoring a piece

1. Drop the sprite in `Assets/Game/Art/Homestead/` (the importer hook addresses it and
   files it in the `Glimmer Grove Homestead` group; nothing to remember).
2. Add a row to `pieces`.
3. Add `ui.piece.<id>` to `loc/en.json`.
4. Bump `groveVersion` in `manifest.json`.
5. `Glimmer Grove ▸ Addressables ▸ Rebuild Grove Atlases` — the shop browses through them,
   and a piece with no thumbnail draws a blank plate on the device.
6. Run `Glimmer Grove ▸ Validate Content` and `▸ Validate Art`, or
   `python Tools/verify/content.py` with the Editor closed.

`kind` is `"decor"` or `"dwelling"`. **`"resident"` is not authorable** — see *Residents are
companions* below; a row claiming to be one is refused and named. `cost` of 0 means "not for
sale" (never "free"), because `JsonUtility` writes a zero into every field an older file
never had.

`bundle` is **how many copies one purchase grants**, and absent means one. Priced decor is
bought by the copy since save v20: a player who wants a dozen fences buys a dozen, and each
one stands on one tile. The four scatter kinds — `ground`, `bed`, `edge`, `path` — ship at
`10` at the price a single one used to cost, because a grove wants a lot of them and buying
one tap at a time is a chore rather than a decision; trees and structures ship at `1`,
because you want a handful and one well.

Three rules about it, and the first is a build error rather than a warning:

- **The price must be divisible by the bundle.** A copy is worth `cost / bundle`, so a fence
  costing 95 in tens makes every copy worth 9 and a player who buys the bundle is scored 90
  for 95 credits spent. It looks perfectly authored, it cannot be seen on a device, and the
  server derives the same short figure — so nothing anywhere disagrees, on the one number
  that reaches a public leaderboard.
- **Only priced decor may carry one.** A resident is a companion, a home rung is a rung, and
  anything free or earned is an entitlement that can never run out; a bundle on one of those
  is a number that will never be read, which is worse than a wrong number because it reads as
  a rule somebody is relying on.
- **Retuning it is safe.** `GroveStock` counts copies and never purchases, so changing a
  bundle changes what the *next* purchase grants and never what a player already holds.

It is authored per piece rather than derived from `slot`, because the slot kind is the shop's
*shelf* rather than a claim about how many of a thing anybody wants — the first oversized gate
that belongs on the edge shelf and sells one at a time would otherwise be an engine change.
`Tools/grove_art.tsv` carries it as its fifth column and `import_grove_art.py` refuses a row
whose price its bundle does not divide.

`art` is a whole path under `Art/`; decor sits under `Art/Homestead/`. `animated` says
whether it names a folder of frames rather than one sprite. `scale` is a multiple of the art
as authored and `lift` is how far up its slot the piece sits, as a fraction of its own drawn
height; the shipped art stands on the ground, so most pieces want about `0.45`.

A piece with no requirement and no price is a **starter** — free from the first launch. At
least one must exist, or a new player opens the picker onto an empty list, and the build
gate refuses that.

### Authoring the floor

`cols` and `rows` are the size of the field in tiles; everything else about its geometry is
derived by `GroveFloor`, which owns the 2:1 isometric transform, its inverse (that is what turns a
tap into a tile) and the draw order. None of it is authored, for the reason a plot's vertical
position stopped being authored before it: the numbers a hand-written layout has to agree with
live in art the author cannot see from the JSON.

`tileArt` names the ground. It ships as `Homestead/floor_grass`, cut from the same field tileset
most of the decor came from. **The sprite is a block, not a flat diamond** — a 418×209 top surface
with 78 pixels of side wall under it — and the game hangs it by half that skirt so the *surface*
lands on the tile's point. The skirt is derived, not authored: the top face of an isometric tile is
2:1 by definition, so anything below it is wall, and a re-cut tile with a deeper side needs no
number changed. A replacement tile only has to keep that proportion.

It may also be left empty, which is a choice rather than an omission — the floor then draws
`Art.IsoTile`, a generated diamond, which is how the feature works before the ground has been cut
and why the Grovement is never a screenful of white rectangles (invariant 7b).

`hallTile` and `starterTile` are tile ids. The hall is the one square nothing can be placed on,
because it is *drawn* from the best home the player owns rather than put down by hand. The starter
tile shows whichever companion nothing gates until the player moves them — **shown, never
stored**, because writing that placement at first launch is exactly the stored default invariant
11c forbids.

A **region** is a rectangle of the floor sold as a unit: `col`/`row` is its top-left tile,
`cols`/`rows` its size, and `cost` the credits that buy it. `cost: 0` means open from the first
launch.

**Ground the player does not own is not drawn at all**, so a region's tiles simply appear when it
is bought. Expansion is sold on the shop's `land` shelf rather than by tapping the grove, because
a floor that had to show what you cannot use is a wall of padlocks around a small lit patch.

Four things the build gate refuses, and each one looks perfectly authored in the file:

* **No free region.** A new player would open the Grovement owning none of it.
* **The hall on priced land.** A home they can see and never reach is the emptiest possible first
  impression of the feature.
* **Two regions holding the same tile.** Who owns it would depend on the order of the file.
* **A region running off the field.**

Tiles belonging to no region are a warning rather than an error — they are drawn locked for ever,
which is a legitimate way to leave room for a later drop, but it is worth being told about.

### Residents are companions

A resident is not a thing this file authors. It is a **companion**, projected in from the
manifest's roster by `GroveResidents` — same creature, same price, same keeper-level gate,
same purchased set. Buy Coral on the profile and she can stand in the village; buy her in the
village and she is on the profile. Nothing about a companion is written down twice.

It used to be otherwise: five creatures lived here, earned by clearing five named glades, and
had nothing to do with the thirty-one companions a player levels towards and pays for. Two
rosters of creatures, two unlock rules, two prices, two screens that could disagree about
what somebody owned.

Three consequences worth knowing before you touch it:

* **A resident's piece id is the companion's id prefixed with `friend_`.** Companion ids and
  grove piece ids were minted independently and already collided — `pebble` is a decor rock
  *and* a companion — and both are in save files, so neither could be renamed. The prefix is
  reserved and the build gate fails on an authored piece that uses it.
* **The five retired ids are rewritten on load, for ever.** `sunmote → friend_puff`,
  `ripple → friend_timber`, `prism → friend_sprocket`, `burr → friend_thistle`,
  `dusk → friend_monarch` — each maps to the companion drawing the same critter flipbook, so a
  grove somebody arranged still looks like the grove they arranged. Never delete that table.
* **A resident is for sale now.** That reverses what this file used to say. It is the same
  price the profile has always charged, and the free route — the keeper ladder — is still
  there and is still what the cell leads with.

### The shop's shelves

The shop pages by **shelf** (`GroveShelf`), which is one idea used three times: a tab, an
asset scope, and a browse atlas. Nine of them — residents, structure, canopy, bed, edge,
path, ground, home, land — and for decor a shelf is just its slot kind. **Land is the one shelf
with no atlas** (`GroveShelves.HasAtlas`): it sells rectangles of floor rather than objects, so
there is nothing to photograph and its cells draw a generated tile. The two that are not decor
are the two exceptions: residents fit every slot but sell on one shelf, and the home is a
ladder rather than a browse.

Three mechanisms have to agree about that division, so it is expressed once. Adding a kind of
thing is a member on the enum, a key, a loc string and a re-run of the atlas step.

**The shop draws from atlases, never from the real art.** A grid cell is about 170 points and
the art behind it is cut at 512 for the floor, so browsing through the real thing pays sixteen
times the pixels it can show — and pays again in draw calls, because a texture each is a batch
each. `Glimmer Grove ▸ Addressables ▸ Rebuild Grove Atlases` generates a downscaled copy of
every piece under `Assets/Game/Generated/GroveThumbs/` and packs one atlas per shelf into
`Assets/Game/Art/Grove/`. Both are committed; both are derived, and neither is edited by hand.

It packs **copies** rather than the shipped sprites, and that is the load-bearing part: a
sprite may belong to exactly one atlas, and a sprite that belongs to one stops having a
texture of its own — so packing the real pieces would mean the grove screen could not draw one
screenful without loading its whole shelf. Browsing costs one shelf; the floor costs the
pieces standing on it.

**Run the atlas step after every content drop.** `Validate Art` proves every shelf's atlas
holds a picture for everything on it, and the build gate runs it — a stale atlas is otherwise
invisible, because the Editor still has the old one and every other check passes.

It needs `Sprite Atlas V2 (Enabled)`, which `Glimmer Grove ▸ Set Up Project` sets and ships in
`ProjectSettings/EditorSettings.asset`. With packing off, a `.spriteatlasv2` imports as editor
data and produces no atlas at all.

### The one thing the grove must never do

Nothing in it touches a board. No bonus, no buff, no faster hearts. `par` is derived from
the board, stars from par, the move budget from par and the server's earnings from all three,
so a grove that granted anything would make every glade a different difficulty per player and no
validator could prove one fair again.

## Events

An event is a time-boxed run at glades that already exist, and it lives in `manifest.json`
beside the chapter list and the roster:

```json
"events": [
  {
    "id": "first_bloom",
    "icon": "bloom",
    "startUnix": 1786320000,
    "endUnix": 1787529600,
    "disabled": false,
    "levels": ["c01_first_light", "c01_twin_streams"],
    "milestones": [ { "goal": 1, "credits": 60 }, { "goal": 2, "credits": 90 } ]
  }
]
```

- **`id` is permanent.** It names the event's loc keys and a player's earned credits depend
  on it through the reward track — renaming one silently un-pays everybody who finished it.
- **`startUnix` / `endUnix`** are absolute Unix seconds, compared against `GameClock` and
  never the device's, or an event could be entered by changing the date. Ninety days is the
  ceiling: a "limited time" that outlives interest in it is content with a countdown on it.
- **`levels`** may belong to any chapter. An event is a lens on the catalog, not a chapter
  of its own, which is what lets one run without shipping any new content at all.
- **`milestones`** must rise, and none may ask for more glades than the event names. An
  out-of-order track is refused rather than sorted, because sorting it would pay rewards
  nobody authored.
- **`icon`** is optional and names **a mark the client knows how to draw** — not an art
  path. Today that is `"bloom"`, a flower generated by `Art.Bloom` whose petals open as the
  track fills; anything else, including an empty field, draws the default.

That last one is worth being precise about, because the obvious reading is wrong. It cannot
name a sprite file: invariant 7 routes every sprite through `AssetLibrary` and
`AssetManifest` decides what is registered, so a filename invented in a content push would
resolve to nothing and the box would draw a white rectangle. A *named mark* degrades the
other way — `EventMark` falls back to the default for a name it has never heard of, so the
worst a typo can do is draw the wrong flower, and a manifest naming a mark that ships in a
later build stays valid on the clients that have not updated. `CatalogIndexBuilder` checks
only that the name is a clean id, deliberately: whether a mark exists is a question about
what has been drawn, and refusing the event over it would pull its window, its glades and
its track because of a picture.

Like the roster, `events` was added **without raising `ContentSchema.Version`** — an older
client ignores it and simply never runs an event.

### A rung is reached by playing and taken by tapping

Since save schema **v11** a milestone is not paid the moment the glade that reached it is
cleared. It opens, and then it waits on `EventScreen` until the player taps it. Authoring is
unchanged — this is not a field — but three consequences are worth knowing before you write
a track:

- **A closed event is still a live page.** Glades stop counting at `endUnix`; blooms do not
  expire. The hub keeps the event's box while anything is uncollected (`GroveEvents.Featured`),
  so shipping a new event does not strand a reward the last one grew.
- **`EventScreen` lays itself out from the goals, not from the rung count.** Rungs sit along
  the vine at `goal / finalGoal`, so two milestones you place close together are drawn close
  together, and eight of them scroll rather than squash. Nothing needs a code change; a track
  of any shape up to `EventRules.MaxMilestones` draws itself.
- **The floor travels and the server pays on it.** `EventCollection` stores one integer per
  event — the largest goal taken — which merges by `max`, rides in the save document, and is
  clamped server-side to the glades actually finished before anything is paid. Retuning a
  live track is therefore safe in one direction only: *adding* a rung between two existing
  ones counts as already collected for anyone whose floor is past it, and *raising* a goal
  re-opens it. Prefer appending.

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

### The heart gate

The optional `hearts` block. How many hearts a player holds and how fast they come back.

```json
"hearts": {
  "refillCap": 5,
  "ceiling": 50,
  "refillSeconds": 28800,
  "boostedRefillSeconds": 14400,
  "maxBoostHours": 72,
  "defeatCost": 1,
  "graceLevels": 3
}
```

Every field is optional on its own — omit one and it inherits the built-in value, so a
push that changes the refill period does not have to restate the other five. Omit the
whole block and `HeartRuleTable.Default` stands. Not a schema bump, for the reason the
daily block is not one.

Two numbers, and the difference between them is the feature:

- **`refillCap` is where the clock stops.** This is the gate — the number that paces free
  play. A player who never collects anything settles here.
- **`ceiling` is the most anybody may hold.** Hearts from chests, streak nights and
  watched videos stack past the cap up to this, instead of evaporating at a full bar.
  Keep a healthy gap: an ad pays 2 and a chest can pay 3, so a ceiling within a few of the
  cap means collected hearts are routinely thrown away. `Validate Content` warns about it.

**`graceLevels` is where the gate does not apply.** The first three levels of the first
chapter of *each mode* cost no heart at all — lose them, restart them or walk away from
them as often as you like. It is per mode rather than once per account because a mode
shipped a year from now is somebody's first board of that mode: Budburst is tapped
rather than tapped and is lost on ink rather than turns, so a player meeting it is a
beginner again in every sense that decides whether taking a heart off them is fair. The
window is counted inside the first chapter and stops at that chapter's end, so the same
three means three of ten on a full chapter and all of a one-glade one. Nought switches it
off and is a legal value; unwritten inherits three. `HeartStake` owns the rule, nothing
about a free run is written to the save, and `Validate Content` prints where the window
lands in every mode — and warns if it swallows a whole first chapter of more than one
level, because the heart gate then does not exist anywhere on that map.

**Every field here is safe to lower, and that is a designed property rather than a
coincidence.** Lowering `refillCap` stops the clock earlier and leaves anybody above it
holding what they had. Lowering `ceiling` refuses *new* grants and takes nothing away —
the ledger's own bound is `HeartLimits.HardCeiling`, a constant no file can move, precisely
so a tuning push can never clamp `produced` downward. If it could, a device that had fetched
the new table and one that had not would disagree forever, because `produced` only ever
rises and the merge depends on that. Enforcement lives in `Hearts.Grant`, where it is a
decision taken once.

`boostedRefillSeconds` is held at `refillSeconds` if authored longer — a boost that slows
hearts down is the feature inverted, and both numbers are individually legal so nothing else
would catch it. Everything is clamped into a supported range by `HeartLimits`; a clamped
file still reports a problem and still fails the build gate.

One thing this block does **not** reach: hearts are applied by the client and never
adjudicated, so nothing here is published to `config/progression` for the server. It is
tuned through the content channel like the chest odds, which means it needs
`ContentConfig.RemoteBaseUrl` set to change without an app update — the same status the ad
payouts and chest rates already have.

### The hint pool

The optional `hints` block. How many hints a player holds and how fast they come back.

```json
"hints": {
  "refillCap": 3,
  "ceiling": 3,
  "refillSeconds": 28800
}
```

**A hint is account-wide.** It used to be an allowance of three per glade, handed back in
full at every board — which meant it cost nothing, meant nothing, and the only players who
never used one were the ones who had not found the button. There is no per-level
`hintAllowance` any more and there must not be one again: a glade has no opinion about how
much of a player's own pool they may spend on it.

Same shape as the heart gate above, minus the two fields hearts need and hints do not.
Every field is optional on its own, the whole block is optional, and it is not a schema
bump. Everything is clamped into a supported range by `HintLimits`.

The one difference worth knowing is that **`ceiling` equals `refillCap` as shipped**, where
hearts keep a wide gap. That is a deliberate choice, not an oversight, and it has exactly
one consequence: a hint granted to somebody already holding three is **refused**, not
clamped — so nothing may offer one there. `RewardedAds.WouldBenefit` is where that is
enforced and `HintsTests` is what pins it; raise the ceiling above the cap and hints start
banking like hearts, with no other change. Both validators print the fact so nobody has to
remember it.

Nothing boosts a hint's clock. The heart boost is named for hearts, sold and dropped as
such, and quietly speeding up a second resource with it would make one published number
mean two things.

Same channel caveat as the heart gate: hints are applied by the client and never
adjudicated, so nothing here is published to `config/progression`.

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
band is measured in **hours**. There is a fifth, `run_time`, which is **retired**: it paid
seconds onto a run's countdown, and there is no countdown. Its enum value is frozen because
the daily chest vectors key on it, nothing can produce it, and both the reader and the
seeder refuse it wherever it appears. There is a sixth, `hints`, which a chest *may* hold but should not while the hint ceiling equals the
cap — a chest that rolls one for a player already holding three pays them nothing, which is
the same failure in a slower costume. Omit the whole block and the built-in table in
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

### Rewarded ads

The optional `ads` block. Four placements, each paying a fixed amount for one finished
video, capped per UTC day.

```json
"ads": {
  "cooldownSeconds": 45,
  "placements": [
    { "id": "heart_refill", "kind": "hearts",   "amount": 2,   "dailyCap": 10 },
    { "id": "coin_bonus",   "kind": "credits",  "amount": 1000, "dailyCap": 6 },
    { "id": "win_bonus",    "kind": "credits",  "amount": 200, "dailyCap": 6 }
  ]
}
```

Placement ids are permanent, for the reason a `LevelId` is: one is written into every
award id the server adjudicates, into the save file's cap counters, into the mediation
dashboard and into analytics — three of those outside this repository. A placement with
no entry is **switched off** everywhere it is drawn, with no build and no dead code path,
so removing an offer is a content change. An id this build does not know is skipped, which
is how a newer content pack reaches an older client.

Omit the whole block and `AdRewardTable.Default` stands. Like the daily block it is
deliberately not a schema bump.

Where each one is offered, and why there:

| id | offered | pays |
|---|---|---|
| `heart_refill` | the defeat panel, out of hearts | hearts |
| `coin_bonus` | the hub's coin `+` | credits |
| `win_bonus` | the victory panel, under the payout | credits |
| `hint_refill` | the hint button, pool empty | hints |

**`run_continue` is a retired placement id and must never be reused.** It bought seconds
on a glade's countdown, and the countdown is gone. An id travels the way a `LevelId` does —
into the mediation dashboard, into `grantLog` on the server and into every analytics row
ever written — so pointing it at some other offer would silently re-label history. It is
absent from `AdPlacement.All`, from `AD_PLACEMENTS` in `functions/src/ads.ts` and from the
seeder's `known` list, which together mean a published table naming it is *refused* rather
than honoured.

**`run_time` is retired with it, and the `transient` rule it created survives.** A kind
spent inside a run is meaningless anywhere a run is not open, and nothing is offered from
inside one any more — so `AdRewardTable` and `seed-config.mjs` both refuse a transient kind
outright. That is the seam a future in-run reward would come back through; the failure it
prevents is silent in the worst way, with the offer drawn where no run exists, the video
played and the reward landing on nothing.

`win_bonus` pays a **flat amount and the button prints it**, rather than doubling what the
run earned. Earned credits are derived from the star ledger (invariant 9), so there is no
accumulated figure to multiply, and doubling one run would mean storing which runs had been
doubled — a forgeable per-level set that pays, which invariant 15 sends straight back to 13.
What a signed callback can attest to is that a view of a placement happened, so that is what
the amount is keyed on. A multiplier the panel cannot honour is worse than a smaller number
it can.

Re-seed after any change here (`npm run seed`), or the client offers one number and the
wallet receives another.

### The shop

The `store` block of `progression.json` is what the shop sells, and it is the only block
in this file the **server also reads**: `seed-config.mjs` derives `config/products` from
it, so the amount printed on a card and the amount a receipt is honoured for are one
authored list rather than two. That is invariant 9a applied to money, and it is why there
is no longer a hand-maintained `products.json`.

```json
"store": {
  "products": [
    { "id": "gg_gems_3", "kind": "consumable", "shelf": "gems",
      "gems": 750, "referenceUsdCents": 599, "badge": "popular" },

    { "id": "gg_bundle_starter", "kind": "nonconsumable", "shelf": "bundles",
      "credits": 7500, "gems": 500, "referenceUsdCents": 299, "badge": "starter" }
  ],
  "goods": [
    { "id": "hearts_five", "kind": "hearts",      "amount": 5,  "gems": 50 },
    { "id": "boost_day",   "kind": "heart_boost", "amount": 24, "gems": 30 }
  ]
}
```

A **product** is bought with money. A **good** is bought with gems. The split is not a
presentation choice — see below.

**There is no price field, and there must never be one.** A price lives in App Store
Connect and the Play Console, is set per storefront, moves with tax and exchange rates,
and comes back from the store SDK already formatted for the player's own locale. Drawing
anything else is wrong in most of the world and a review risk in all of it.
`referenceUsdCents` is **never shown**: it exists so the build gate can prove the ladder
gets better as it gets bigger, and so the "+40% EXTRA" ribbon can be *derived* from the
prices rather than typed beside them.

**A product id is permanent**, in invariant 1's full sense. It keys a receipt document
that lives for ever, neither store lets one be reused after deletion, and a receipt
redeemed a year from now is looked up against whatever the table says then. Retune by
adding a product, never by repointing one.

**`kind` is the store's word, not ours.** A `nonconsumable` is sold once per store
account and both stores enforce that themselves, before any money moves — which is how
the starter offer is made one-time without a flag in the save file that two devices would
have to agree about. It is also why one-time offers are exempt from the ladder check: a
starter pack is deliberately worth several times the ladder and cannot undercut it,
because it cannot be bought twice.

#### A product may only ever grant currency

This is the load-bearing decision of the whole feature. Currency is the one thing the
server owns (invariant 10), so it can be granted against a validated receipt with no
client involvement at all. Hearts and boosts live in the save file and are applied by the
phone — so a product granting both would need the client to apply half a purchase after
the server applied the other half, which means a record in the save of "did I already
apply this transaction's hearts": a new field, merged across devices, whose failure mode
is somebody paying and receiving nothing.

So hearts and boosts are bought with **gems** instead, and a gem debit is an ordinary
`CurrencyLedger.TrySpend` — idempotent, offline-capable, and refused by the server on the
next sync if the derived balance could not cover it. It is the same two lines that buy a
companion. Selling hearts for money directly would mean a permanent store product per
bundle, priced in every storefront, undeletable, and re-priced by hand every time the
heart gate is retuned, in exchange for nothing a player can tell apart.

The corollary: **a good may not pay currency.** `hearts` and `heart_boost` are the whole
list, and `StoreCatalog` refuses anything else by name rather than clamping it.

#### The ordering that makes a purchase safe

A purchase arrives from the store as an **unfinished** transaction. It is handed to our
own server, which asks Apple or Google whether it really happened, records it against a
globally unique key, and grants the currency. *Only then* is the transaction confirmed
with the store.

Everything that can go wrong is therefore some flavour of "still unfinished", and both
stores re-deliver an unfinished transaction on every launch until it is confirmed — a
crash, a flat battery, a tunnel, a force-quit, a server outage. That is why there is no
per-purchase state anywhere in the save file: the store is already keeping the record,
far more reliably than a client could.

Google's three-day rule is the one real deadline: an unacknowledged Play purchase is
refunded automatically, and confirming is what acknowledges it. Hence a retry that is
aggressive rather than polite — immediately, then on a doubling backoff, then on every
reconnection and every foreground.

#### After editing the block

```
npm --prefix firebase/functions run build
node firebase/seed/seed-config.mjs
```

Then `Glimmer Grove ▸ Validate Content`. It **errors** — not warns, unlike every other
block in this file — on a ladder that gets worse as it gets bigger, on a good that can
never be bought (hearts above the ceiling, a boost past the cap), and on a product with
no `store.product.<id>` string. Every other block here describes what play pays and an
aggressive tuning is a legitimate weekend decision; this one describes what somebody is
charged, and the only way to put a wrong figure right afterwards is one refund at a time.

`python Tools/verify/content.py` checks the same things from a terminal and prints the
shop against the income that has to pay for it — what a day of free play collects, and
how many days the whole catalog of credit sinks comes to.

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
  levels overrides. Loaded on entering the chapter, **released on leaving it**. All of
  it is *shared* now — the ordinal's map, the ten skies — so it lives in the global
  bundle and is still claimed and released by the chapter scope. A bundle is where a
  file is downloaded from; a scope is what is decoded and resident.

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

Three menu items remain. The first two are not required in normal work; the third is,
after every content drop that touches the grove:

```
Glimmer Grove ▸ Addressables ▸ Sync All Assets        re-file everything from scratch
Glimmer Grove ▸ Addressables ▸ Audit Addresses        prove every request resolves
Glimmer Grove ▸ Addressables ▸ Rebuild Grove Atlases  regenerate the shop's browse atlases
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

## Sound

Twenty clips under `Assets/Game/Audio/Sfx/`, three music tracks beside them, and one
player — `Audio` (Presentation). A sound is asked for by name: `Audio.Sfx("click")`,
or `Audio.SfxVaried`, which is the same thing with a little random detune so a repeat
never lands on exactly the same pitch twice.

**The twenty names are a fixed vocabulary.** `AssetManifest.Sfxs` preloads exactly
those names and nothing else, and `python Tools/verify/sfxnames.py` proves the three
lists agree — what the code plays, what is on disk, and what is preloaded. Before that
check existed a misspelled name was a runtime `InvalidKeyException` and a silence that
shipped green (`Audio.Sfx("tap")` did, twice), and `press.wav` sat in the project for
months played by nobody. Adding a twenty-first sound means adding it in three places:
`Tools/sfx.tsv`, `AssetManifest.Sfxs`, and the call site.

### The clips are cut by a tool, never edited by hand

`python Tools/make_sfx.py` cuts all twenty out of the licensed source pack against
`Tools/sfx.tsv` — one row per name, giving the source file, a transposition, a length
cap, and a gain trim. `--check` proves the shipped wavs are what the table says, the
same bargain `make_shop_art.py` strikes for the shop's money ladders.

The reason to cut rather than copy is that four properties have to hold across the
whole set, and not one of them is a property of any single file:

- **One perceived loudness.** The volumes authored at the 115 call sites — `.28`,
  `.34`, `.42`, `.5`, `.62`, `.9` — only mean something if the samples underneath them
  are the same size to begin with. They were not: the previous set ranged over a
  six-fold spread of RMS, so some of those numbers were compensating for the sample
  rather than expressing a mix.
- **A peak ceiling.** The game *pitches* these at playback — `lit` climbs to three
  times its recorded rate — and pitching resamples, which overshoots a signal already
  at full scale. A dB of headroom is what stops the happiest moment in the game being
  the one that clips.
- **Short where repeated.** `Audio.PlayOne` is a ten-voice round-robin that calls
  `Stop()` on reuse, so the eleventh overlapping one-shot cuts the first off mid-tail.
  `--report` prints, per clip, how many copies of itself it has to stand alongside at
  its busiest moment; over ten is an error rather than a judgement call. The old `lit`
  was 1.06 s against a ladder that fires twelve notes 70 ms apart — fifteen overlapping
  copies, so it was cutting itself off every time anybody solved anything.
- **Audible on a phone.** A phone speaker reproduces very little below about 500 Hz, so
  a beautifully warm sample can simply be missing on the device most players use.
  `--report` prints the spectral centroid for exactly this, and it is the reading that
  rejected the first round of choices.

### What the set is made of

Three materials and one scale. **Wood** is the interface (`click`, `back`, `tick`,
`tock`, `pop`, `pop2`, `blocked`, `shatter`), **stone** is movement (`rotate_a`,
`rotate_b`, `whoosh`, `chest`), a **mallet** is the ladder (`lit`), and **bells** are
reward (`chime`, `chime2`, `bell`, `coin`, `star`, `unlock`, `win`). Twelve source
files carry the twenty slots, and the reuse is deliberate — a small palette is what
makes a set sound like one place rather than twenty samples. Where a source is used
twice the two sit an interval apart, so they read as two notes of one instrument:
`tick` and `tock` are literally the same block of wood a fourth apart.

Every transposition in the table is a **pentatonic** step. That matters more here than
it would in most games because this one overlaps sound constantly — `lit` climbs twelve
notes over two octaves on a single turn, `coin` runs dozens of tokens in two seconds,
and three modes fire cascade voices on a rising ladder. A pentatonic set has no
semitone in it, so no two of those can collide into a beat.

### Judging it is a listening job, and there is a tool for that too

`python Tools/make_sfx.py --contact sfx.html` writes a single self-contained page that
*plays* the set. `--check` proves reproduction and says nothing about quality — the
same gap `make_shop_art.py --contact` exists to close for pictures, except that a
contact sheet for sound has to be pressed rather than looked at.

Three controls per clip, and the last two are the ones that matter. **one** is the clip
as it ships. **ladder** is the clip at the pitches the game really uses, which is the
only way to hear whether `lit` reads as a phrase or as a sample being sped up. **run**
is the clip at the rate the game really repeats it, which is how `coin` at nine a
second is judged. Above them are four **scenes** — a turn, a solve, a payout, the wheel
— assembled from the real delays, pitches and volumes. Every wrong choice this set has
made was inaudible one clip at a time.

### Import settings are written by a tool as well

`python Tools/sfx_meta.py` writes one `AudioImporter` block for all twenty, preserving
each file's GUID — Addressables keys on the GUID, so a regenerated `.meta` would
silently unaddress every sound in the game. They are PCM, `DecompressOnLoad`, preloaded,
and **`normalize: 0`**. That last one is the one to leave alone: Unity's importer
peak-normalises when it downmixes, which would undo the loudness match and leave nothing
at all to notice — the game would simply be mixed wrong.

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

## Privacy and consent

Not content, and deliberately in this file anyway: it is the only pipeline document, and the
three parts below are all things that go wrong at build or release time rather than at runtime.

**Nothing here is stored in the save file, and nothing is published in `progression.json`.** A
consent answer is per-device, revocable and therefore not monotonic — the shape invariant 11b
forbids — and the CMP already keeps the authoritative record in the form the ad networks read.
`AdPrivacy` holds it in memory for the session and asks again on the next launch.

### The order

`RewardedAds.StartAsync` is the whole rule: resolve consent, apply it to the provider, then
initialise. Never the other way round — an SDK that starts first has already decided what it may
collect and has already auctioned on that decision, and a signal applied afterwards changes only
the next request. `Boot` installs the gateway; the **splash** starts it, for the reason the store
connection starts there: it is a network round trip and it may put a native dialog on screen,
and neither belongs before the first scene has loaded.

### The consent platform

Google UMP, behind `GLIMMER_UMP`, which comes from `GlimmerGrove.Privacy.asmdef`'s
`versionDefines` on `com.google.ads.mobile` — never a Player Settings define, for the
per-build-target reason `GLIMMER_ADDRESSABLES` documents. Without the package installed,
`NullConsentGateway` answers "no consent, assume the GDPR applies", so ads run unpersonalised
rather than assuming a yes nobody gave.

A CMP rather than a dialog of our own, for three reasons any one of which decides it: only a CMP
knows whether this player is in the EEA or the UK, only a CMP writes the IAB TCF string that
mediation adapters actually read, and Google's EU User Consent Policy requires a certified one
once AdMob is in the waterfall.

### iOS tracking

`AppTrackingPrompt` plus `Assets/Game/Plugins/iOS/GlimmerAppTracking.mm`. The prompt is shown
once per install — iOS enforces that, not us — so it is safe to call every launch, and a player
who changes their mind does it in iOS Settings.

`IosPrivacyPlist` writes `NSUserTrackingUsageDescription` into the built Xcode project.
**Without the key iOS silently refuses to show the prompt at all**: no dialog, every player
non-consented, and a build that passes review with iOS ad revenue near zero. The sentence
matters — Apple rejects copy that merely restates the dialog — and it needs an
`InfoPlist.strings` per store language, which is deliberately not generated, because a string
invented at build time would slip past the loc gate.

### app-ads.txt

In the repository root. It must be served as plain text at `https://<developer-website>/app-ads.txt`
on the exact domain named in **both** store listings, and every line in it is currently a
placeholder taken from no dashboard. A missing or unreachable file produces no error anywhere —
only lower fill and lower prices, for ever.

Change the waterfall, change that file, in the same commit.

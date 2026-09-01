# GlimmerGrove.Authoring

Rules that decide whether **content is fit to ship**, and that no player ever runs.

## Why this assembly exists

Everything here used to live in `GlimmerGrove.Domain`, which ships. That was not laziness —
it was the only place that satisfied both of the constraints these rules are under:

- **`GlimmerGrove.Editor` can reach them.** The build gate (`ContentValidation`) and the
  authoring tools (`ManifestSync`, `ContentAuthoring`) are the only callers.
- **`GlimmerGrove.Tests` can reach them.** A validator with no failing case is not a check,
  and the test assembly references `Domain` — it does *not* reference `Editor`.

Domain satisfies both, so that is where they went, and the cost was quiet: a seed sweep, a map
collision check and a chapter-mode check compiled into every player build, wired into the
IL2CPP graph, never called. Not a lot of bytes — but the argument that puts them there is
"nowhere else works", and once a folder exists that both callers *can* reach and no player
build includes, it stops being true.

`includePlatforms: ["Editor"]` is what does the work: the assembly is absent from every player
build, and `Editor` and `Tests` both reference it.

## What belongs here

A rule belongs here when **no shipped type references it**. That is the whole test, and it is
mechanical — `Tools/verify/compile.py` proves it by compiling `Domain` *without* this assembly
on its reference list, so a Domain file that starts calling into here fails offline rather
than quietly dragging the folder back into the build.

What deliberately does **not** belong here:

- **`FallSolver`, `KeeperSolver` and `BudSolver`.** A level of each of those modes is
  *graded* on what they return: par is the fewest drops, tiles or taps that finish the board,
  both star lines and the budget the run is dealt are derived from it, and none of that may be
  authored (a typed par drifts silently). So the player's device runs the search, once, the
  first time anything asks — which is why `LevelTuning.Par` may be resolved lazily. The
  authoring-only half of each is `Survey`, a dozen lines over the same `Search` the phone needs,
  and splitting that out would mean making a four-hundred-line search class public across an
  assembly boundary to save the dozen. It is a trade, and it is the only one left.
  <br>There used to be a fourth and it was the weaker case: Lightweave's solver ran on the phone
  as its *generator's* acceptance bar rather than as a grading rule. That mode is retired, and so
  is Ripplewake, which came and went between it and Budburst — see invariant 20j.
- **`ChapterMap`, `GroveFloor`, `BudBand`** and the rest of the geometry. Screens read
  them. Only the *checks over* them are authoring.

## What is here

| | |
|---|---|
| `LevelValidator` | Every level, proved solvable and fairly graded. The bulk of it is the conduit board. |
| `LevelValidation` | `LevelIssue`, its severity, and the report a level's checks build up. |
| `ModeValidator` | How each mode is proved fit to ship, and the registry of them. |
| `ChapterMapValidator` | Node collisions, backwards trails, the end-of-chapter marker's clearance. |
| `ChapterModeValidator` | A chapter's declared mode against the mode its levels are (invariant 20h). |
| `BudRunnerReading` | What the vines on a Budburst grove are worth, measured by cutting them (invariant 20m). |

`BudRunnerReading` is a type of its own rather than a private method because it is asked
**twice** — `ModeValidator` asks it to decide whether a grove may ship, and `BudLadderTests`
asks it of every grove that already has, pinning the answer against `Tools/verify/bud.py`. A
second copy of the arithmetic would be a second thing to keep in step with the mirror, and that
is not hypothetical: the two *did* disagree by one on `b02_crossvine`, because the fixture
compared a chain's four numbers where the mirror compared the whole grove — and a vine that
moves a colour without setting anything off changes the second and not the first.

## A mode is declared three times

That is the shape this assembly completes, and it is worth stating because the third one was
missing for a long time and cost the whole validator:

| | assembly | what it declares |
|---|---|---|
| `LevelMode` | `Domain` | what a mode **is** — its block in the JSON, its rules, its tuning |
| `ModeLook` | `Presentation` | what it **looks like** — its screen, its perch, its colour |
| `ModeValidator` | `Authoring` | how it is **proved fit to ship** |

`ModeLook` was split off because Domain may never reference Presentation. `ModeValidator` is
the same split for a different line: a mode's checks run on the machine that builds the game
and never on the machine that plays it. Before it existed, `LevelMode.Validate` was a `virtual`
member, so the authoring entry point called into the mode and the mode called back into the
authoring entry point — a cycle that pinned six hundred lines of content checks into every
player's install.

The price of a registry over an abstract member is that an entry can be *missing* where an
override cannot, and a missing one would be silent in the worst way: a green tick over a mode
nothing looked at. So `LevelValidator` reports an unregistered mode as an **error** rather than
a pass, and `ModeValidatorTests` fails the build if the two registries drift apart.

"""Glasswater - Lightfall's second chapter, and the one that brings the lens.

**The shipped chapter is `Content/chapters/f02_glasswater.json`, not this file.** That is what
the game reads and what the build gate proves; nothing at runtime knows this script exists.

    python Tools/chapters/f02_glasswater.py --check     # does the shipped JSON still match?
    python Tools/chapters/f02_glasswater.py             # rewrite it from here

**The lens is the whole chapter, and it is cooked like everything else here.** Glass holds no
light of its own, nothing can be dropped into it and nothing cooks it by landing on it. What it
does is *fill up*: light that reaches it - from a burst beside it, or from another lens's beam -
is taken in one channel at a time, and when it holds all three it **fires**, each beam crossing
bare ground until the first cell in its line takes it.

**The light it throws is white, and how far round it fires says where its own light came from.**
Glass holds all three channels by the time it goes off, so every mote a beam lands on is
completed and *pops*, whatever colour it was - which nothing else in this mode can do, because a
burst washes one colour and only sets off what was exactly that channel short. A lens charged the
ordinary way fires **sideways**; a lens **struck by another lens's shot** fires along all four
axes, up and down together. That is the chain the chapter is built on: one well-aimed shot down a
row of glass opens every pane in it, and each of those then opens its own column.

**A shot costs three drops, and that is arithmetic rather than a dial.** Every wave of one drop
carries that drop's colour, so a lens gains at most one channel per drop however long the chain
beside it runs. Filling an empty one takes three separate drops of three separate colours, each
engineered to burst next to it - which is a plan built across a run rather than a freebie.

**It relayed on first touch in its first cut, and that was wrong in both directions at once.** Any
burst beside a lens set it off, so the reach was free and the boards got easier; and because it
happened on most drops that touched glass it could never be worth stopping the board for. It was
reported as both at the same time - too easy, and an effect with no effort in it - and those are
one fault. A payoff handed out for nothing cannot be a payoff (invariant 26f).

**So the ramp is how full each lens starts, and it was measured before it was chosen.** Over
ninety generated boards on one shape: two-thirds-full glass (`y`/`m`/`c`) leaves 50 solvable,
one-third (`r`/`g`/`b`) 38, and empty (`O`) 7. The early boards therefore hand most of the charge
over and ask for one well-aimed burst, and the late ones ask for all three. Light is authored in
upper case and glass in lower, so a board says at a glance what is made of what.

**What keeps a white beam from being a solvent is the price rather than the threshold.** A shot
costs three separate drops of three separate colours, and it still only reaches the *first* thing
in each line. Reach is bought here, and it is bought dearly - which is the whole of why it is
worth stopping the board for.

**Sideways is not a reduction of four, it is the two that were ever worth anything.** A well has
gravity, so a lens rests on something: its downward beam travels exactly one cell, into the thing
holding it up, and its upward one flies into the air above the stack and leaves. `aim` is counted
out of **two** for that reason, and every lens here is placed on the floor looking across a valley
in the terrain. Two boards had to be re-drawn when the rule changed, and how they failed is worth
knowing - see the note above `f02_low_tide`.

**Nothing here authors a difficulty number.** Par is the fewest drops that empty the well without
ever breaching the brim, found by search (`Tools/verify/fall.py`, mirroring the shipping
`FallSolver`), and both star lines and the supply are multiples of par.

**The boards were searched for rather than typed.** A random fill is almost never solvable - every
pure mote wants two more channels and the stragglers pile up faster than any chain clears them -
so what is drawn by hand is the *shape*: how the terrain lies, and where the glass stands in it.
The fill is swept: the cells are split into connected blobs, each filled with the one blend
missing the same channel so that one drop takes a whole blob, with pure motes on the seams
between them that need two. That is the Deep Well's own construction with one thing added, and
the thing added is where the light can get to.

**The ladder is four dials and par is not one of them.** Par is length, so it wanders on purpose,
exactly as a glade chapter's does. What climbs is what is standing in the well, how little
headroom it leaves, how many panes of glass there are, and whether a player who never looks ahead
survives it. The numbers each well lands on are printed by `report()` when this file is run.
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "Tools", "verify"))

import fall                                                      # noqa: E402

CHAPTER = "f02_glasswater"
BODY = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "chapters", CHAPTER + ".json")
LOC = os.path.join(ROOT, "Assets", "StreamingAssets", "Content", "loc", "en.json")

#: Cold and pale against the Deep Well's ember, because this is the chapter made of glass.
ACCENT, SLATE = "#7FD8F0", "#111E2A"

#: The one backdrop of c01_shallows' nine that no chapter had claimed. Borrowed rather than
#: cut, which is what every non-glade chapter does - f01 draws play_2, k01 play_4, b01 play_6 -
#: so this costs no art and no addressable. `chapter_art.tsv` says `-` for it, because a row
#: naming a source would re-cut a picture four chapters share.
BACKDROP = "play_8"

#: **Its own map, which is the one thing it must not borrow.** It drew c01_shallows' hand-made
#: `strip0..5` at first, so Lightfall's two chapters were the same painting - which is exactly
#: what the glade chapters never do, and it was reported that way. Six strips rather than four or
#: five because the node positions below are f01's, and `make_chapter_art.py` scales a source to
#: whole strips: a different count is a different map height, and every distance on the map is
#: measured in canvas units (`ChapterMapValidator`).
STRIPS = ["f02_strip0", "f02_strip1", "f02_strip2", "f02_strip3", "f02_strip4", "f02_strip5"]

#: The Deep Well's own spacing, proved against `ChapterMapValidator` for ten nodes.
WHERE = [(0.30, 0.055), (0.72, 0.140), (0.26, 0.225), (0.70, 0.310), (0.28, 0.395),
         (0.74, 0.480), (0.30, 0.560), (0.68, 0.645), (0.26, 0.730), (0.72, 0.815)]


class Well(object):
    """One authored level: an id that is permanent, a name, a line, a board and a procession."""

    def __init__(self, lid, name, tagline, rows, motes, budget=0.0):
        self.id = lid
        self.name = name
        self.tagline = tagline
        self.rows = rows
        self.motes = motes
        self.budget = budget

    def survey(self):
        return fall.survey(self.rows, self.motes)


BOARDS = [
    Well("f02_the_glass", "The Glass", "Fill it, and it fires.",
         [".....",
          ".....",
          ".....",
          ".....",
          "M...Y",
          "My..Y"],
         "RGB"),
    Well("f02_stillwater", "Stillwater", "A colour it already holds is a wall.",
         ["......",
          "......",
          "......",
          "C.....",
          "C....M",
          "Cm...M"],
         "BRG"),
    # **Both of these stand their glass on the floor, and that is the rule rather than a
    # preference.** A lens fires sideways unless another lens strikes it, so a pane resting on a
    # mote is a pane whose downward shot is spent on the thing holding it up. These two were
    # authored under the rule that fired all four ways and both of them leaned on that beam: the
    # search answered par 3 and par 4 while it existed and par 6 with 55 and 52 winning lines
    # once it did not, which is a board that has stopped deciding anything. Standing the glass on
    # the bottom row puts the whole shot across the valley, where the tagline always said it was.
    Well("f02_low_tide", "Low Tide", "Aim it while the valley is open.",
         [".....",
          ".....",
          ".....",
          "Y....",
          "Y...C",
          "Y...C",
          "Yc..C"],
         "GBR"),
    Well("f02_the_crossing", "The Crossing", "Three colours, three drops, one shot.",
         ["......",
          "......",
          "......",
          "Y....C",
          "Y....C",
          "Y....C",
          "Mg..CC"],
         "RGB"),
    Well("f02_two_panes", "Two Panes", "One lens can fill another.",
         ["......",
          "......",
          "......",
          "......",
          "Y....C",
          "Ry..gC",
          "MYY.CC"],
         "BGR"),
    Well("f02_far_shore", "Far Shore", "Where no wash could ever reach.",
         [".......",
          ".......",
          ".......",
          "Y......",
          "Y.....C",
          "Yg....C",
          "BM...CC",
          "MMM.CCC"],
         "BRG"),
    Well("f02_lantern_row", "Lantern Row", "A burst feeds it free. A drop costs you.",
         [".......",
          ".......",
          ".......",
          "M.....Y",
          "M.....Y",
          "Mb...yM",
          "MY...MM",
          "YYYYMMM"],
         "GBR"),
    Well("f02_slack_water", "Slack Water", "One pane starts empty. Plan for it.",
         ["......",
          "......",
          "......",
          "C....M",
          "CO..gM",
          "CC..MM",
          "YCM.MM",
          "YYMYRM",
          "YMMGYY"],
         "GRB"),
    Well("f02_underglass", "Underglass", "The line at the top has not moved.",
         ["......",
          "......",
          "CR..CC",
          "GYg.CC",
          "CYYYGC",
          "CCYYYR",
          "CCRYBM",
          "CCBMMM"],
         "GRB"),
    Well("f02_the_glasswater", "Glasswater", "Every mote, and every pane.",
         ["......",
          "......",
          "......",
          "CO..yM",
          "CCg.MM",
          "CYYYMM",
          "CYRYYM",
          "CMMMGG",
          "BRMCCC"],
         "RBG"),
]


# ---------------------------------------------------------------------------- writing it out
def level_json(well, at):
    x, y = WHERE[at]
    block = {"width": len(well.rows[0]), "height": len(well.rows),
             "rows": well.rows, "motes": well.motes}

    out = {"id": well.id, "mapX": x, "mapY": y}
    if well.budget:
        out["budgetFactor"] = well.budget
    out["fall"] = block
    return out


def chapter_json():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": ACCENT,
        "slate": SLATE,
        "backdrop": BACKDROP,
        "mapStrips": list(STRIPS),
        "levels": [level_json(w, i) for i, w in enumerate(BOARDS)],
    }


def report():
    """What each well actually asks, counted rather than argued about."""
    print(f"{'level':<24}{'well':<7}{'motes':<7}{'lens':<6}{'asks':<6}{'aim':<5}{'reach':<7}"
          f"{'head':<6}{'par':<5}{'3*':<5}{'2*':<5}{'supply':<8}{'ways':<6}{'greedy':<8}"
          f"{'nodes':<8}deal")

    worst = 0
    for well in BOARDS:
        s = well.survey()
        if not s["proved"] or s["par"] < 1:
            print(f"  {well.id:<22} UNSOLVED (proved={s['proved']}, nodes={s['nodes']})")
            continue

        par = s["par"]
        bounded = well.budget >= 0
        supply = (par + fall.DEFAULT_SPARE) if bounded else 0
        greedy = s["greedy"] if s["greedy"] >= 0 else "-"
        worst = max(worst, s["nodes"])

        # `asks` is what the board charges for its glass: three channels a lens, less
        # whatever it was authored already holding. It is the chapter's only ramp.
        asks = sum(3 - bin(g).count('1') for g in s['glass'])

        print(f"{well.id:<24}{str(s['width']) + 'x' + str(s['height']):<7}{s['motes']:<7}"
              f"{s['lenses']:<6}{asks:<6}{s['aim']:<5}{s['reach']:<7}"
              f"{s['headroom']:<6}{par:<5}"
              f"{fall.over(par, 120):<5}{fall.over(par, 140):<5}{(supply or 'free'):<8}"
              f"{s['ways']:<6}{str(greedy):<8}{s['nodes']:<8}{well.motes}")

    print(f"\ndearest proof: {worst} position(s) against the 40000 a level is expected to "
          f"cost and the 120000 it is refused above - see FallValidator. Cost goes as the "
          f"column count to the power of par.")


# ---------------------------------------------------------------------------- the strings
def write_strings(check):
    """Adds this chapter's names and taglines, and never rewrites one somebody has changed.

    The *mode's* strings - its readouts, its defeats, its four lessons, the lens among them -
    live in `f01_strings.py`, because a mode's vocabulary outlives any one chapter.
    """
    doc = json.load(io.open(LOC, encoding="utf-8"))
    entries = {e["key"]: e for e in doc["entries"]}

    wanted = [(f"chapter.{CHAPTER}.name", "Glasswater")]
    for well in BOARDS:
        wanted.append((f"level.{well.id}.name", well.name))
        wanted.append((f"level.{well.id}.tagline", well.tagline))

    added, differs = [], []
    for key, text in wanted:
        entry = entries.get(key)
        if entry is None:
            added.append((key, text))
        elif entry["text"] != text:
            differs.append((key, entry["text"], text))

    if check:
        return added, differs

    for key, text in added:
        # Beside the keys it is related to, so the file stays readable.
        best, at = -1, len(doc["entries"])
        for i, entry in enumerate(doc["entries"]):
            shared = 0
            for a, b in zip(entry["key"], key):
                if a != b:
                    break
                shared += 1
            if shared >= best:
                best, at = shared, i + 1
        doc["entries"].insert(at, {"key": key, "text": text})

    if added:
        with io.open(LOC, "w", encoding="utf-8", newline="\n") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
            f.write("\n")

    return added, differs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    report()

    doc = chapter_json()
    added, differs = write_strings(args.check)

    if args.check:
        shipped = json.load(io.open(BODY, encoding="utf-8"))
        same = shipped == json.loads(json.dumps(doc))

        for key, text in added:
            print(f"  MISSING  {key} = {text}")
        for key, was, now in differs:
            print(f"  DIFFERS  {key}\n    file: {was}\n    here: {now}")

        if same and not added:
            print(f"\n{os.path.relpath(BODY, ROOT)} matches this source")
            return 0

        if not same:
            print(f"\n{os.path.relpath(BODY, ROOT)} DIFFERS from this source")
        return 1

    with io.open(BODY, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"\nwrote {os.path.relpath(BODY, ROOT)}")
    for key, text in added:
        print(f"  added   {key} = {text}")
    for key, was, now in differs:
        print(f"  LEFT ALONE {key} - somebody has re-worded it\n    file: {was}\n    here: {now}")

    print("\nNext: Content > Sync Manifest, then Validate Content.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

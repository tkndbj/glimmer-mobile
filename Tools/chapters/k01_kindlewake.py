# -*- coding: utf-8 -*-
"""Writes Kindlewake's first chapter, and proves it is what ships.

    python Tools/chapters/k01_kindlewake.py            # check the shipped body is this
    python Tools/chapters/k01_kindlewake.py --write

**The hollow is designed and the colours are dealt**, which is the split `kindle_sweep.py`
exists to serve (invariant 32d). Where the stone stands, which critters are asleep and what
each of them is waiting for are things a player *reads*, so they are drawn by hand; which
channel each ember carries is exactly the sort of arrangement nobody can eyeball, so each of
the ten rungs below is a seed that was swept for and kept for what it measured. The seeds are
recorded so the boards can be re-derived rather than only re-typed.

**Par is not a ladder and must not be** (CLAUDE.md: par is length, not difficulty). It runs
3, 3, 3, 5, 4, 4, 4, 4, 5, 5 - the dip after Stone Row is the board where one strand serves
three critters at once, which is a *shorter* answer and a harder one to find. What actually
ramps is the board: how many critters, how many of them want a blend, how much stone is in the
way, and how much material is left over once the answer is paid for.

**Three of the ten cannot be finished by a player who never looks ahead**, and that number is
the one to watch. It is the reading Lightweave was withdrawn for - a mode that rejects almost
nothing - and until a spent ember blocked light it was **nought out of three hundred and
twenty** swept hollows. It is still only three, so the honest summary is that Kindlewake asks
which pair rather than punishing greed, and whether that is enough is the first thing to judge
by playing it.

Ten rungs, and each adds exactly one thing:

  k01_firstlight   the verb, and nothing else. Three critters wanting one channel each, no
                   stone, and no allowance at all (invariant 24). Par 3 rather than 2 because
                   at par 2 both star lines round onto one number and the middle band is empty.
  k01_stillwood    that light *stays*: two critters wanting the same colour, so one strand
                   can be made to serve both. Still unlosable.
  k01_crossways    the blend. One critter wants two channels, so two strands have to cross on
                   the square it is sleeping on - which is the whole mode.
  k01_stonerow     stone. Which pair of embers can see each other at all is now the question.
  k01_thechoir     three blends on one line, so a single strand serves all three at once.
  k01_dimhollow    a pocket: stone at the mouth of the outer columns.
  k01_threefold    two blends of different pairs at opposite ends, so no colour is spare.
  k01_lanternweft  a lattice. Every strand is short and the material is the whole decision.
  k01_deepwake     the most material and the most lines through it in the chapter.
  k01_kindleheart  a critter wanting all three channels: three strands crossing one square.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "Tools"))
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import chapters.mapart as mapart                                        # noqa: E402
import kindle                                                           # noqa: E402
import kindle_sweep as sweep                                            # noqa: E402
import proto                                                            # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "k01_kindlewake.json"

CHAPTER = "k01_kindlewake"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). One, because it is the first Kindlewake chapter - so it draws `map1` and the
#: first block of ten skies, exactly as every other mode's first chapter does. A chapter
#: published next year costs no art at all, which is the whole point of that rule.
ORDINAL = 1

#: Each rung: the hollow it is drawn on, the seed its colours were dealt from, and the room it
#: forgives above par. `budget` of -1 is a board that cannot be lost (invariant 24).
#:
#: **`spare` is 3 on every board that has one, and that is arithmetic rather than taste.** The
#: allowance has to clear the two-star line or the bottom band is unscorable
#: (`ceil(par x 1.40)`), and it has to be reachable or the meter counts down to an ending that
#: cannot happen (`life`). Three is the only value that satisfies both at every par here.
RUNGS = [
    dict(id="k01_firstlight", template="first", seed=38, budget=-1.0),
    dict(id="k01_stillwood", template="awake", seed=7, budget=-1.0),
    dict(id="k01_crossways", template="blend", seed=47, spare=3),
    dict(id="k01_stonerow", template="ribs", seed=112, spare=3),
    dict(id="k01_thechoir", template="choir", seed=37, spare=3),
    dict(id="k01_dimhollow", template="narrow", seed=30, spare=3),
    dict(id="k01_threefold", template="prism", seed=136, spare=3),
    dict(id="k01_lanternweft", template="weft", seed=37, spare=3),
    dict(id="k01_deepwake", template="deep", seed=89, spare=3),
    dict(id="k01_kindleheart", template="heart", seed=75, spare=3),
]


def rows_of(rung):
    return sweep.deal(sweep.TEMPLATES[rung["template"]], rung["seed"])


def level(rung, place, sky):
    rows = rows_of(rung)

    block = {"width": len(rows[0]), "height": len(rows), "rows": rows}
    if rung.get("spare"):
        block["spare"] = rung["spare"]

    out = {"id": rung["id"], "mapX": place[0], "mapY": place[1]}
    if rung.get("budget") is not None:
        out["budgetFactor"] = rung["budget"]
    out["backdrop"] = sky

    out["kindle"] = block
    return out


def body():
    places = mapart.places(ORDINAL)
    skies = mapart.skies(ORDINAL, len(RUNGS), mode="kindle")

    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        # Verdant, which is `ModeLook`'s accent for this mode: the grove's own dark, and the
        # one colour on the board that is not an ember.
        "accent": "#54E48C",
        "slate": "#0E1A14",
        "backdrop": skies[0],
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level(r, places[i], skies[i]) for i, r in enumerate(RUNGS)],
    }


def measure():
    """Every reading each rung was kept for, printed so a change to a rule is visible here."""
    print("%-16s %4s %5s %6s %4s %5s %4s %4s %5s %5s"
          % ("level", "par", "ways", "nodes", "less", "life", "crs", "bln", "idle", "bud"))

    for rung in RUNGS:
        rows = rows_of(rung)
        spare = rung.get("spare") or proto.DEFAULT_SPARE
        lay = sweep.layout(rows, spare)

        par, ways, nodes, proved = proto.search(kindle.Future(kindle.Board(lay)))
        if not proved or par < 1:
            sys.exit("%s could not be proved" % rung["id"])

        bounded = rung.get("budget") != -1.0
        budget = (par + spare) if bounded else 0

        read = kindle.readings(lay, budget)
        greedy = proto.careless(kindle.Future(kindle.Board(lay)),
                                budget or (par + proto.DEFAULT_SPARE))

        print("%-16s %4d %5d %6d %4d %5d %4d %4d %5d %5s"
              % (rung["id"], par, ways, nodes, greedy, read["life"],
                 read["crossed"], read["blended"], read["idle"], budget or "-"))

        # The two rules this chapter is authored against, held here as well as in the gate:
        # the meter has to be reachable, and a hollow standing blend critters has to make the
        # player arrange a crossing to wake one (invariants 22b and 20m).
        if budget and read["life"] < budget:
            sys.exit("%s: life %d under an allowance of %d"
                     % (rung["id"], read["life"], budget))
        if read["blends"] and not read["blended"]:
            sys.exit("%s: stands %d blend critter(s) and no shortest run wakes one"
                     % (rung["id"], read["blends"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--quiet", action="store_true")
    args = ap.parse_args()

    made = json.dumps(body(), indent=1, ensure_ascii=False) + "\n"

    if args.write:
        OUT.write_text(made, encoding="utf-8")
        print("wrote %s" % OUT.relative_to(REPO))
    else:
        have = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if have != made:
            sys.exit("%s is not what this tool writes - re-run with --write" % OUT.name)
        print("%s is what the tool writes" % OUT.name)

    if not args.quiet:
        measure()


if __name__ == "__main__":
    main()

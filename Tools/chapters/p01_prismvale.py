# -*- coding: utf-8 -*-
"""Writes Prismvale's first chapter, and proves it is what ships.

    python Tools/chapters/p01_prismvale.py            # check the shipped body is this
    python Tools/chapters/p01_prismvale.py --write

**Two levels, and that is the whole commission.** This mode was built to be *played* and judged,
exactly as the five prototypes were (invariant 29) and Hollowmarch after them - a whole mode with
a real par, both star lines, a real fail state, a heart, a chest, a streak and a star ledger, and
no save-file cost at all. Two rungs is enough to answer the only question that matters, which is
whether dragging gems into a line of one colour is a thing anybody wants to do; ten would be eight
boards built before anybody had said.

**The board is designed and the colours are dealt**, which is the split `prism_sweep.py` exists to
serve (invariant 32d). Where the bare ground is, where the lanterns stand and how far a critter is
from the light are things a player *reads*, so they are drawn by hand; which colour sits in which
socket is exactly the sort of arrangement nobody can eyeball, so each rung below is a seed that was
swept for and kept for what it measured. The seeds are recorded so the boards can be re-derived
rather than only re-typed.

The two rungs:

  p01_firstvein   the verb, and nothing else. Two lanterns, two critters, one gem already lit
                  beside each lantern so the rule is shown rather than told - and **no allowance
                  at all** (invariant 24), because the worst moment to charge somebody is while
                  they are working out what the verb is. Careless play finishes it, deliberately.
  p01_twinlight   three lanterns, three critters, every colour wanted somewhere, and the first
                  board a player who never looks ahead cannot finish. One of its four moves wakes
                  two critters at once, which is the only thing on a board of this mode that is
                  better than the obvious move.

**Par is 3 then 4, and that is a ceiling rather than a choice.** Cost goes as the swap count to
the power of par, and a swap board has thirty to fifty swaps on it - so par five is over the node
budget a level may cost on the player's own device (invariant 26d). What ramps between the two
rungs is not length: it is how many critters, how many lantern colours are really wanted, and
whether greed still works.
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
import prism                                                            # noqa: E402
import prism_sweep as sweep                                             # noqa: E402
import proto                                                            # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "p01_prismvale.json"

CHAPTER = "p01_prismvale"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). One, because it is the first Prismvale chapter - so it draws `map1` and the
#: first block of skies, exactly as every other mode's first chapter does. A chapter published
#: next year costs no art at all, which is the whole point of that rule.
ORDINAL = 1

#: Each rung: the board it is drawn on, the seed its colours were dealt from, and the room it
#: forgives above par. `budget` of -1 is a board that cannot be lost (invariant 24).
#:
#: **`spare` is 3 on the board that has one, and that is arithmetic rather than taste.** The
#: allowance has to clear the two-star line or the bottom band is unscorable (`ceil(par x 1.40)`,
#: which is 6 at par 4), so `par + spare` of 7 is the smallest honest figure.
RUNGS = [
    dict(id="p01_firstvein", template="first", seed=108, budget=-1.0, palette="rgb"),
    dict(id="p01_twinlight", template="reach", seed=10, spare=3, palette="rgb"),
]


def rows_of(rung):
    return sweep.deal(sweep.TEMPLATES[rung["template"]], rung["seed"], rung["palette"])


def level(rung, place, sky):
    rows = rows_of(rung)

    block = {"width": len(rows[0]), "height": len(rows), "rows": rows}
    if rung.get("spare"):
        block["spare"] = rung["spare"]

    out = {"id": rung["id"], "mapX": place[0], "mapY": place[1]}
    if rung.get("budget") is not None:
        out["budgetFactor"] = rung["budget"]
    out["backdrop"] = sky

    out["prism"] = block
    return out


def body():
    places = mapart.places(ORDINAL)
    skies = mapart.skies(ORDINAL, mapart.PER_CHAPTER, mode="prism")

    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        # Verdant, which is `ModeLook`'s accent for this mode: the grove's own dark, and the one
        # colour on the board that is not a gem.
        "accent": "#54E48C",
        "slate": "#0E1A14",
        "backdrop": skies[0],
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level(r, places[i], skies[i]) for i, r in enumerate(RUNGS)],
    }


def measure():
    """Every reading each rung was kept for, printed so a change to a rule is visible here."""
    print("%-16s %4s %5s %6s %5s %5s %5s %5s %5s %5s"
          % ("level", "par", "ways", "nodes", "less", "dealt", "used", "pair", "idle", "bud"))

    for rung in RUNGS:
        rows = rows_of(rung)
        spare = rung.get("spare") or proto.DEFAULT_SPARE
        lay = sweep.layout(rows, spare)

        par, ways, nodes, proved = proto.search(prism.Future(prism.Board(lay)))
        if not proved or par < 1:
            sys.exit("%s could not be proved" % rung["id"])

        bounded = rung.get("budget") != -1.0
        budget = (par + spare) if bounded else 0

        read = prism.readings(lay, budget)
        greedy = proto.careless(prism.Future(prism.Board(lay)),
                                budget or (par + proto.DEFAULT_SPARE))

        print("%-16s %4d %5d %6d %5d %5d %5d %5d %5d %5s"
              % (rung["id"], par, ways, nodes, greedy, read["dealt"], read["used"],
                 read["paired"], read["idle"], budget or "-"))

        # The three rules this chapter is authored against, held here as well as in the gate: a
        # board may not be dealt half finished (invariant 5g), a board standing more than one
        # lantern colour has to want more than one of them (5d), and the allowance has to clear
        # the two-star line or the bottom band is unscorable (22).
        if read["gems"] and read["dealt"] * 100 > read["gems"] * 35:
            sys.exit("%s: dealt %d of %d gems already lit"
                     % (rung["id"], read["dealt"], read["gems"]))
        if read["hues"] > 1 and read["used"] < 2:
            sys.exit("%s: stands %d lantern colours and uses %d"
                     % (rung["id"], read["hues"], read["used"]))
        if budget and budget <= proto.over(par, proto.SILVER_HUNDREDTHS):
            sys.exit("%s: an allowance of %d does not clear the two-star line of %d"
                     % (rung["id"], budget, proto.over(par, proto.SILVER_HUNDREDTHS)))


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

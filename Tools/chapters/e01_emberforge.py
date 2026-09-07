# -*- coding: utf-8 -*-
"""Writes Emberforge's first chapter, and proves it is what ships.

    python Tools/chapters/e01_emberforge.py            # check the shipped body is this
    python Tools/chapters/e01_emberforge.py --write

**The wall is designed and the shards are dealt**, which is the split `ember_sweep.py` exists to
serve (invariant 32d). Where the stone runs, how much frost holds a shelf up, where the cages are
buried and where a warden stands are things a player *reads*, so they are drawn by hand; which
colour is in which cell is exactly the sort of arrangement nobody can eyeball, so each of the ten
rungs below is a seed that was swept for and kept for what it measured. The seeds are recorded so
a board can be re-derived rather than only re-typed.

Ten rungs, and each one adds exactly one thing:

  e01_firstember   the verb. One cage, an open wall, and no allowance at all (invariant 24).
                   Nothing is dealt, so the first thing anybody ever does in this mode is a thing
                   they made. Par three rather than two, and that is not taste: at par two
                   `ceil(2 x 1.20)` and `ceil(2 x 1.40)` are both three, so the two-star band is
                   empty and every clear is worth three stars or one. Budburst moved its own
                   opening grove for exactly this.
  e01_twinlocks    two cages no single cross reaches, so the wall has to give up two embers.
                   Still unlosable.
  e01_ironribs     stone. The first rung with a fail line, and the first where which row a cross
                   goes down is the whole question.
  e01_frostvein    frost. It stops nothing and holds everything above it up, so melting one drops
                   a column into something that matches.
  e01_chainfire    the chain. Three cages on three lines, and a wall where one blast can reach
                   the next ember - which is what this mode is for.
  e01_deepcell     a pocket. Cages down corridors, so exactly one line reaches each of them.
  e01_ironward     the warden. Two beams or nothing, and it stands on the row two cages share.
  e01_twinstars    the star. Cages on the diagonals, which is the shape only two embers pushed
                   together will reach.
  e01_slaghold     everything at once, and a cage buried under frost.
  e01_emberheart   the finale. Four cages, a warden, and stone through the middle so no two
                   goals share a clean line.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "Tools"))
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import ember                                                            # noqa: E402
import ember_sweep as sweep                                             # noqa: E402
import mapart                                                           # noqa: E402
import proto                                                            # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "e01_emberforge.json"

CHAPTER = "e01_emberforge"

#: The chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). Emberforge is set in the same raided village as Hollowmarch - it is the
#: harvester the raiders parked in it, prised open - so it draws the same world (30e).
ORDINAL = 1
MODE = "ember"

#: Each rung: the wall it is drawn on, the seed its shards were dealt from, and what it forgives.
#: `budget` of -1 is a board that cannot be lost.
RUNGS = [
    dict(id="e01_firstember", template="first", seed=158, budget=-1.0),
    dict(id="e01_twinlocks", template="pair", seed=104, budget=-1.0),
    dict(id="e01_ironribs", template="ribs", seed=94, spare=3),
    dict(id="e01_frostvein", template="frost", seed=49, spare=3),
    dict(id="e01_chainfire", template="chain", seed=38, spare=3),
    dict(id="e01_deepcell", template="pocket", seed=38, spare=3),
    dict(id="e01_ironward", template="ward", seed=4, spare=3),
    dict(id="e01_twinstars", template="twin", seed=38, spare=3),
    dict(id="e01_slaghold", template="deep", seed=28, spare=4),
    dict(id="e01_emberheart", template="heart", seed=16, spare=4),
]

#: What each rung says while it is played. Every key resolves against `loc/en.json`, which
#: `content.py` and `ContentValidation` both prove - a line of dialogue is the one loc key in
#: this game that is authored rather than derived from an id (invariant 30d).
STORY = {
    "e01_firstember": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("bolt", "intro3")]),
        ("forged", [("bolt", "forged")]),
        ("freed", [("mon1", "freed")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_twinlocks": [
        ("intro", [("bolt", "intro1"), ("collector", "intro2")]),
        ("forged", [("bolt", "forged")]),
        ("freed", [("mon2", "freed")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_ironribs": [
        ("intro", [("bolt", "intro1"), ("bolt", "intro2")]),
        ("fired", [("bolt", "fired")]),
        ("freed", [("mon3", "freed")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_frostvein": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2")]),
        ("fired", [("bolt", "fired")]),
        ("freed", [("mon1", "freed")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("mon1", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_chainfire": [
        ("intro", [("bolt", "intro1"), ("bolt", "intro2")]),
        ("fired", [("bolt", "fired1")]),
        ("fired", [("collector", "fired2")]),
        ("freed", [("mon2", "freed")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_deepcell": [
        ("intro", [("bolt", "intro1"), ("mon3", "intro2")]),
        ("freed", [("mon3", "freed")]),
        ("fired", [("bolt", "fired")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("mon3", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_ironward": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("bolt", "intro3")]),
        ("kill", [("bolt", "kill")]),
        ("freed", [("mon1", "freed")]),
        ("fired", [("bolt", "fired")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_twinstars": [
        ("intro", [("bolt", "intro1"), ("bolt", "intro2")]),
        ("fired", [("bolt", "fired")]),
        ("freed", [("mon2", "freed")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_slaghold": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2")]),
        ("kill", [("bolt", "kill")]),
        ("freed", [("mon3", "freed")]),
        ("fired", [("bolt", "fired")]),
        ("tight", [("collector", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "e01_emberheart": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("collector", "intro3")]),
        ("kill", [("bolt", "kill")]),
        ("fired", [("bolt", "fired")]),
        ("freed", [("mon1", "freed1")]),
        ("freed", [("mon2", "freed2")]),
        ("tight", [("collector", "tight")]),
        ("won", [("bolt", "won1"), ("collector", "won2"), ("mon3", "won3")]),
        ("lost", [("collector", "lost")]),
    ],
}


def rows_of(rung):
    return sweep.deal(sweep.TEMPLATES[rung["template"]], rung["seed"])


def level(rung, index):
    rows = rows_of(rung)
    x, y = mapart.places(ORDINAL)[index]

    block = {"width": len(rows[0]), "height": len(rows), "rows": rows}
    if rung.get("spare"):
        block["spare"] = rung["spare"]

    out = {"id": rung["id"], "mapX": x, "mapY": y}
    if rung.get("budget") is not None:
        out["budgetFactor"] = rung["budget"]

    out["backdrop"] = mapart.sky(ORDINAL, index, MODE)
    out["ember"] = block
    out["story"] = {"beats": [
        {"cue": cue,
         "lines": [{"who": who, "key": "story.%s.%s" % (rung["id"], key)}
                   for who, key in lines]}
        for cue, lines in STORY[rung["id"]]
    ]}
    return out


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,
        "accent": "#B478FF",
        "slate": "#160E1E",
        "backdrop": mapart.sky(ORDINAL, 0, MODE),
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level(rung, i) for i, rung in enumerate(RUNGS)],
    }


def measure():
    """Every reading each rung was kept for, printed so a change to a rule is visible here."""
    print("%-16s %4s %5s %6s %4s %4s %4s %4s %4s %4s %4s"
          % ("level", "par", "ways", "nodes", "less", "life", "chn", "frg", "str", "goal", "bud"))

    for rung in RUNGS:
        rows = rows_of(rung)
        spare = rung.get("spare") or proto.DEFAULT_SPARE
        lay = sweep.layout(rows, spare)

        par, ways, nodes, proved = proto.search(ember.Future(ember.Board(lay)))
        if not proved or par < 1:
            sys.exit("%s could not be proved" % rung["id"])

        bounded = rung.get("budget") != -1.0
        budget = (par + spare) if bounded else 0

        read = ember.readings(lay)
        greedy = proto.careless(ember.Future(ember.Board(lay)),
                                budget or (par + proto.DEFAULT_SPARE))

        print("%-16s %4d %5d %6d %4d %4d %4d %4d %4d %4d %4s"
              % (rung["id"], par, ways, nodes, greedy,
                 sweep.life(lay, budget or (par + proto.DEFAULT_SPARE)),
                 read["chained"], read["forged"], read["starred"],
                 ember.Board(lay).goals, budget or "-"))


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

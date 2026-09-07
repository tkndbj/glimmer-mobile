# -*- coding: utf-8 -*-
"""Writes Hollowmarch's first chapter, and proves it is what ships.

    python Tools/chapters/m01_hollowmarch.py            # check the shipped body is this
    python Tools/chapters/m01_hollowmarch.py --write

**The road is designed and the convoy is dealt**, which is the split `march_sweep.py` exists
to serve. Where the road winds, how far it is from the gate and where the launcher stands are
things a player *reads*, so they are drawn by hand; which colours stand where is exactly the
sort of arrangement nobody can eyeball, so each of the three lines below is a seed that was
swept for and kept for what it measured. The seeds are recorded so the boards can be
re-derived rather than only re-typed.

Three rungs, and each one adds exactly one thing:

  m01_firstcore   the verb. Three colours, no raiders, no allowance at all (invariant 24) -
                  and a three-wave chain sitting on the shortest answer, so the first thing
                  anybody ever sees of this mode is the thing the mode is *for*.
  m01_haulroad    haulers. They wear no colour, so they cut the line into pieces and decide
                  which matches exist; a blast beside one scraps it. Greed loses here.
  m01_thegate     the whole thing. Four colours, a warden under plating, and a line near
                  enough the gate that pods really do start going through it.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent.parent
sys.path.insert(0, str(REPO / "Tools"))
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import march                                                            # noqa: E402
import march_sweep as sweep                                             # noqa: E402
import proto                                                            # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "m01_hollowmarch.json"

CHAPTER = "m01_hollowmarch"

#: Each rung: the road it is drawn on, where the front of the line starts, the convoy, the
#: magazine, and the seed it was swept from. `budget` of -1 is a board that cannot be lost.
RUNGS = [
    dict(id="m01_firstcore", template="first", head=11, seed=170,
         line="gBBrRGGRRbBR", cores="RGB", budget=-1.0,
         mapX=0.70, mapY=0.055, backdrop="village_00"),

    dict(id="m01_haulroad", template="road", head=10, seed=22,
         line="HRBBrBrRGHRbRR", cores="RGB", spare=5,
         mapX=0.28, mapY=0.14, backdrop="village_01"),

    dict(id="m01_thegate", template="gate", head=10, seed=1,
         line="GGRrBGRHGBBrBRWgHrYYgB", cores="RGBY", spare=5,
         mapX=0.74, mapY=0.225, backdrop="village_02"),
]

#: What each rung says while it is played. Every key resolves against `loc/en.json`, which
#: `content.py` and `ContentValidation` both prove - a line of dialogue is the one loc key in
#: this game that is authored rather than derived from an id (invariant 30d).
STORY = {
    "m01_firstcore": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("bolt", "intro3")]),
        ("freed", [("mon1", "freed1")]),
        ("fired", [("bolt", "fired")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "m01_haulroad": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("bolt", "intro3")]),
        ("kill", [("bolt", "kill")]),
        ("freed", [("mon2", "freed1")]),
        ("freed", [("bolt", "freed2")]),
        ("forged", [("bolt", "forged")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("mon2", "won")]),
        ("lost", [("collector", "lost")]),
    ],
    "m01_thegate": [
        ("intro", [("collector", "intro1"), ("bolt", "intro2"), ("bolt", "intro3")]),
        ("forged", [("bolt", "forged")]),
        ("kill", [("bolt", "kill1")]),
        ("kill", [("collector", "kill2")]),
        ("fired", [("collector", "fired")]),
        ("freed", [("mon3", "freed1")]),
        ("freed", [("mon1", "freed2")]),
        ("tight", [("bolt", "tight")]),
        ("won", [("bolt", "won1"), ("collector", "won2"), ("bolt", "won3")]),
        ("lost", [("collector", "lost")]),
    ],
}


def rows_of(rung):
    return sweep.place(sweep.TEMPLATES[rung["template"]], rung["head"], rung["line"])


def level(rung):
    rows = rows_of(rung)

    block = {"width": len(rows[0]), "height": len(rows), "rows": rows,
             "cores": rung["cores"]}
    if rung.get("spare"):
        block["spare"] = rung["spare"]

    out = {"id": rung["id"], "mapX": rung["mapX"], "mapY": rung["mapY"]}
    if rung.get("budget") is not None:
        out["budgetFactor"] = rung["budget"]
    if rung.get("backdrop"):
        out["backdrop"] = rung["backdrop"]

    out["march"] = block
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
        "accent": "#FF6B57",
        "slate": "#1A1210",
        "backdrop": "village_00",
        "mapStrips": ["map1_strip%d" % i for i in range(6)],
        "levels": [level(r) for r in RUNGS],
    }


def measure():
    """Every reading each rung was kept for, printed so a change to a rule is visible here."""
    print("%-16s %4s %5s %6s %4s %4s %4s %4s %4s %4s %4s"
          % ("level", "par", "ways", "nodes", "less", "chn", "frg", "lnc", "men", "bud", "goal"))

    for rung in RUNGS:
        rows = rows_of(rung)
        spare = rung.get("spare") or proto.DEFAULT_SPARE
        lay = sweep.layout(rows, rung["cores"], spare)

        read = march.readings(lay)
        par, ways, nodes, proved = proto.search(march.Future(march.Board(lay)))

        bounded = rung.get("budget") != -1.0
        budget = (par + spare) if bounded else 0
        greedy = proto.careless(march.Future(march.Board(lay)),
                                budget or (par + proto.DEFAULT_SPARE))

        print("%-16s %4d %5d %6d %4d %4d %4d %4d %4d %4s %4d"
              % (rung["id"], par, ways, nodes, greedy, read["chained"], read["forged"],
                 read["lanced"], read["menace"], budget or "-",
                 march.Board(lay).goals))

        if not proved or par < 1:
            sys.exit("%s could not be proved" % rung["id"])


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

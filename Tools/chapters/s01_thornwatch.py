# -*- coding: utf-8 -*-
"""Writes Thornwatch's first chapter, and proves it is what ships.

    python Tools/chapters/s01_thornwatch.py            # check the shipped body is this
    python Tools/chapters/s01_thornwatch.py --write

**One level, and that is the whole commission.** This mode was built to be *played* and judged,
exactly as the five prototypes were (invariant 29), Hollowmarch after them and Prismvale after
that - a whole mode with a real par, both star lines, a real fail state, a heart, a chest, a streak
and a star ledger, and no save-file cost at all. One rung is enough to answer the only question
that matters, which is whether feeding coloured turrets off a jewel board is a thing anybody wants
to do; ten would be nine levels built before anybody had said.

**The field is dealt and the siege is designed**, which is the split `32d` asks for. Where the
wards stand, what comes down the hill and in what order are things a player *reads*, so they are
written by hand; which gem sits in which socket is exactly the sort of arrangement nobody can
eyeball, so the opening field below is a seed that was swept for and kept for what it measured - an
even spread of all four colours and seven opening swaps, which is a field with something to do on
it and not a field of accidents. The seed is recorded so the board can be re-derived rather than
only re-typed.

**Par is 22 and it is arithmetic rather than a search** (invariant 20d's rule met a different way,
and the honest cost of a mode that runs on a clock). The hill sends 264 health; a match is worth at
most 12 - three gems into the ward its target is weak to, every one of that fuel spent as a bolt -
so no run of fewer than 22 matches could have destroyed it. Everything a good player does beyond
that is free upside the count does not model: a four-match is worth more than a three, and a
cascade feeds a second ward for no move at all. That is the right direction for a floor to be wrong
in, because it keeps three stars reachable.

**And there is no move allowance at all**, which is invariant 24 and also the mode: a siege is lost
when the last ward falls, so an allowance would be a second fail state whose meter counts down to
an ending that never happens.
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
import proto                                                            # noqa: E402
import siege                                                            # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s01_thornwatch.json"

CHAPTER = "s01_thornwatch"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). One, because it is the first Thornwatch chapter - so it draws `map1` and the
#: first block of skies, exactly as every other mode's first chapter does.
ORDINAL = 1

#: The seed the opening field was dealt from. Recorded so the board can be re-derived; nothing
#: reads it at run time, because the field is written out below in full.
SEED = 164686

#: The field as it is dealt: eight by five, ten gems of each colour, no three alike touching, and
#: seven swaps that line something up. Eight wide because a render said so - at seven the field was
#: a column of air either side of it, and the field is where the finger goes.
FIELD = [
    "rgrgyrry",
    "bgygybbg",
    "grrbbgyr",
    "bybbgryg",
    "ryygybrb",
]

#: The ward line, left to right. All four, because the one decision this mode has is which colour
#: is wanted next and a line of two would make it a coin toss.
WARDS = "rgby"

#: What the field refills from. All four, so no ward can be starved by the deal.
GEMS = "rgby"

#: The three waves. One letter per raider - lower case a creeper, upper case a brute.
#:
#: **The ramp is what is coming rather than how fast it comes**, because a siege's difficulty is
#: the hill and not the field (invariant 5d's division of labour, read across). Wave one is one
#: raider of each colour, which is the mode stated in four objects: whichever ward you feed is the
#: one that answers. Wave two is twice as many, so every ward wants feeding twice. Wave three is
#: eight *brutes* - two and a half times the health each, two blows a swing - and it is the only
#: part of this level that can really take the line down.
#:
#: **It was half this size and had to grow**, which is worth recording because the cause was not
#: the hill. Fuel used to fade on a clock; taking that out (the owner's verdict) roughly doubled
#: what a match delivers, and the level then finished with the ward line untouched - a fail state
#: rejecting nothing, which is invariant 5d asked of a threat. The hill is the lever that fixes
#: that, because difficulty is the boards' job.
WAVES = [
    "rgby",
    "rgbyrgby",
    "RGBYRGBY",
]


def level():
    x, y = mapart.places(ORDINAL)[0]

    return {
        "id": "s01_firstwatch",
        "mapX": round(x, 3),
        "mapY": round(y, 3),

        # No allowance. A siege is lost when the last ward falls (invariant 24, and the mode).
        "budgetFactor": -1.0,
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),

        "siege": {
            "width": len(FIELD[0]),
            "height": len(FIELD),
            "rows": list(FIELD),
            "gems": GEMS,
            "wards": WARDS,
            "waves": list(WAVES),
        },
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,

        # Rose and a warm dark plate, which is `SiegeLook`'s accent: this is the only mode in the
        # game where something is coming at the player while they think, and the one place a mode
        # is allowed to say what it feels like before it is opened.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level()],
    }


def prove(written):
    """Everything this chapter claims about itself, checked against the mirrored rules."""
    block = written["levels"][0]["siege"]

    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.LETTERS)
    layout = siege.Layout(grid, block["gems"], block["wards"], block["waves"])

    if layout.fault:
        sys.exit("%s: %s" % (written["levels"][0]["id"], layout.fault))

    par = siege.par(layout)
    read = siege.readings(layout)

    print("%-18s par %-4d 3* %-4d 2* %-4d %d raider(s) in %d wave(s), %d brute(s), "
          "%d colour(s) against %d ward(s)"
          % (written["levels"][0]["id"], par,
             proto.over(par, proto.GOLD_HUNDREDTHS), proto.over(par, proto.SILVER_HUNDREDTHS),
             read["raiders"], read["waves"], read["brutes"], read["colours"], read["wards"]))

    if not read["threat"]:
        sys.exit("this siege cannot be lost - no wave could bring a ward down")

    if not read["swap"]:
        sys.exit("this field has no opening swap, so it deals itself again before anybody moves")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    made = body()
    prove(made)

    text = json.dumps(made, indent=1, ensure_ascii=False) + "\n"

    if args.write:
        OUT.write_text(text, encoding="utf-8", newline="\n")
        print("wrote %s" % OUT)
        return

    if not OUT.exists():
        sys.exit("%s does not exist - run with --write" % OUT)

    if OUT.read_text(encoding="utf-8") != text:
        sys.exit("%s differs from what this script writes - re-run with --write" % OUT)

    print("%s is what this script writes" % OUT)


if __name__ == "__main__":
    main()

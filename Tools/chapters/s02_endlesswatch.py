# -*- coding: utf-8 -*-
"""Writes Thornwatch's endless lane, and proves it is what ships.

    python Tools/chapters/s02_endlesswatch.py            # check the shipped body is this
    python Tools/chapters/s02_endlesswatch.py --write

**One level, and it is never won.** The Endless Watch is a siege whose waves do not stop: what
comes down the hill at wave *n* is a rule rather than a list (`SiegeEndless`), the ramp climbs for
as long as a player can hold, and the run ends when the last ward falls. That is the one lane in
this game graded on a count that *climbs* rather than on one the player spends
(`LevelTuning.Climbs`), which is why it authors a `goldWave` instead of letting par be derived: par
everywhere else is a search or an arithmetic floor over what a level **sends**, and this one sends
everything.

**It is a track, not a mode** (`GameTrack`). The board, the wards, the raiders and the verb are
Thornwatch's; what differs is the ladder it sits on, which is why it costs no mode registry row, no
art, no validator and no second switcher tab - and why `CatalogIndex.ChaptersIn` answers the
ordinary ladder alone, so an endless chapter can never gate a real one on stars nobody can earn.

**The ramp is in the raiders and never in the rules.** Every constant this mode runs on stays where
invariant 20d put it - a bolt is worth what it is worth on wave one and on wave ninety - and what
climbs is what is walking down the hill: more of them, tougher, hitting harder. A boss every fourth
wave to sixteen, then a *pair* every fifth, which is thirty waves before anything repeats.

**What is authored is a field, a deal, a line and two numbers.** The field is dealt by seed and
swept for like every other siege field (invariant 32d); the two numbers are how far a three-star
run reaches and the fraction of that a two-star run does, and both are guesses until somebody plays
it - see the owed list.
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
import siege_sweep as sweep                                             # noqa: E402

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s02_endlesswatch.json"

CHAPTER = "s02_endlesswatch"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). Two, so it draws `map2` and the second block of skies - the same arithmetic
#: every mode's second chapter uses, and the reason a lane costs no art.
ORDINAL = 2

WIDE, TALL = 8, 5

#: The field, dealt by seed and kept for what it measured.
#:
#: **Nine opening swaps rather than the ten the first rung wants.** An endless lane is not a
#: teaching rung: it is the thing a player comes back to once they can already play, so the field
#: it opens on is a little tighter than the chapter's first one and a little looser than its last.
SEED, SWAPS, STOOD = 12211, 9, 0

#: How often a fresh gem falls in as a cog, per hundred.
#:
#: **Four, which is the top of the band the shipped chapter uses**, because this lane is the one
#: place a player is fighting a hill that never stops getting worse - the rank ladder is the only
#: thing on their side that can climb with it, and it tops out at four. Above about four per
#: hundred the whole line maxes inside a minute and the hill stops mattering (invariant 37w).
COGS = 4

#: How far a three-star run reaches, and the fraction of it a two-star run does.
#:
#: **Both are guesses until somebody plays it**, and they are the only two numbers in this file
#: that are. Wave twenty is four bosses met and one pair; eleven is most of the way to the second
#: boss. What pins them is a device, which is the owed item this lane ships with.
GOLD_WAVE = 20
SILVER_FACTOR = 0.55


def rows_of():
    """The field, re-derived from its seed rather than typed."""
    cells = sweep.deal(SEED, WIDE, TALL, "rgby", STOOD)
    return sweep.rows_of(cells, WIDE, TALL)


def level():
    x, y = mapart.places(ORDINAL)[0]

    return {
        "id": "s02_endless",
        "mapX": round(x, 3),
        "mapY": round(y, 3),

        # No allowance, like every siege: the ward line is the fail state, and both gates *error*
        # on one that authors an allowance.
        "budgetFactor": -1.0,
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),

        "siege": {
            "width": WIDE,
            "height": TALL,
            "rows": rows_of(),
            "gems": "rgby",
            "wards": "rgby",

            # No waves and no boss: the muster is a rule (`SiegeEndless`), and a file carrying both
            # would have two answers to what its second wave is. Both gates refuse that outright.
            "waves": [],
            "boss": "",
            "cogs": COGS,

            "endless": {
                "goldWave": GOLD_WAVE,
                "silverFactor": SILVER_FACTOR,
            },
        },
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,

        # Thornwatch's own accent and plate: this is the same place, played without an ending.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "levels": [level()],
    }


def prove(written):
    """Everything this lane claims about itself, checked against the mirrored rules."""
    block = written["levels"][0]["siege"]

    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    layout = siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                          block.get("boss"), block.get("cogs", 0), endless=True)

    if layout.fault:
        sys.exit("s02_endless: %s" % layout.fault)

    made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
    if made != SWAPS:
        sys.exit("s02_endless: seed %d now deals %d opening swaps, not the %d recorded"
                 % (SEED, made, SWAPS))

    if not siege.any_swap(layout):
        sys.exit("s02_endless: this field has no opening swap")

    gold = block["endless"]["goldWave"]
    silver = max(1, -(-int(round(gold * block["endless"]["silverFactor"] * 100)) // 100))

    if not (1 <= silver < gold):
        sys.exit("s02_endless: the two-star wave (%d) is not below the three-star wave (%d)"
                 % (silver, gold))

    # **The ramp is walked as far as the second time every boss has been met**, which is where the
    # schedule starts repeating. What is being asked is invariant 5d's question of a hill nobody
    # authored: does the line have an answer to everything that walks down it.
    colours = list(layout.deal)
    kinds = {}

    for wave in range(1, siege.PAIRS_AFTER * 3 + 1):
        for colour, kind in siege.endless_wave(colours, layout.seed, wave):
            kinds[kind] = kinds.get(kind, 0) + 1

            if colour not in layout.wards:
                sys.exit("s02_endless: wave %d sends a '%s' %s and no ward carries '%s'"
                         % (wave, colour, kind, colour))

    print("s02_endless   3* wave %-3d 2* wave %-3d cogs %d%%, %d opening swap(s), seed %d"
          % (gold, silver, COGS, made, SEED))

    print("              first 48 waves send: "
          + ", ".join("%d %s" % (n, k) for k, n in sorted(kinds.items())))

    schedule = [(w, siege.bosses_at(w)) for w in range(1, 49) if siege.bosses_at(w)]
    print("              bosses: "
          + "; ".join("w%d %s" % (w, "+".join(b)) for w, b in schedule))


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

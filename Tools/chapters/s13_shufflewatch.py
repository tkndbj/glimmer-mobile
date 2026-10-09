# -*- coding: utf-8 -*-
"""Writes Thornwatch's Shuffle lane, and proves it is what ships.

    python Tools/chapters/s13_shufflewatch.py            # check the shipped body is this
    python Tools/chapters/s13_shufflewatch.py --write

**One level, never won, and a different build every time** (MODES.md 59). The Shuffle lane is a
siege whose waves are a rule (`ShuffleRamp`): pure raiders, a body more every wave, every wave
tougher, and no boss ever. What makes it the Shuffle lane is not the hill but the line - four
Breakers for everybody, no loadout - and the hand of three upgrade cards dealt every two waves,
one of which goes into the run's build. The run ends when the last ward falls.

**It is a track, not a mode** (`GameTrack.Shuffle`), for the Infinite lane's reason: the board,
the wards, the raiders and the verb are Thornwatch's, and what differs is the ladder it sits on and
the rules its line plays under.

**What is authored is a field, a deal, a line and two numbers.** The field is dealt by seed and
swept for like every other siege field (invariant 32d); the two numbers are how far a three-star
run reaches and the fraction of that a two-star run does, and both are guesses until somebody
plays it - the model player in `ShuffleHoldTests` is what pins them first.
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

OUT = REPO / "Assets" / "StreamingAssets" / "Content" / "chapters" / "s13_shufflewatch.json"

CHAPTER = "s13_shufflewatch"

#: This chapter's place inside its own mode, which is what decides its map and its skies
#: (invariant 7c). Three, so it draws `map3` and the third block of skies - a lane with no map
#: still draws a sky behind its board, and the Infinite lane took the second.
ORDINAL = 3

WIDE, TALL = 8, 5

#: The field, dealt by seed and kept for what it measured: the opening swaps the field holds.
SEED, SWAPS = 20261827, 10

#: How often a felled raider leaves a cog on the hill, per hundred kills.
#:
#: **Nought, and the ramp sends no bombers**: nothing drops on this lane's hill (the owner's
#: call, 2026-10-09). The hand is the whole ladder, and a hill with nothing to reach for keeps
#: the eye on the cards. A turret here stands at its first rank for the whole run.
COGS = 0

#: The whole roster, for the Infinite lane's reason: this is the lane a player comes back to once
#: they can already play, so it deals everything they have been taught.
CHARMS = siege.CHARM_LETTERS

#: The two stones, from the lane's own waves (`ShuffleRamp.CursedWave`, `VoidWave`).
OBSIDIAN = True
SINGULARITY = True

#: How far a three-star run reaches, and the fraction of it a two-star run does. Guesses until the
#: owner's device says otherwise; the hold sweep prints where a random build lands.
GOLD_WAVE = 24
SILVER_FACTOR = 0.5


def rows_of():
    """The field, re-derived from its seed rather than typed."""
    cells = sweep.deal(SEED, WIDE, TALL, "rgby")
    return sweep.rows_of(cells, WIDE, TALL)


def level():
    x, y, afloat = mapart.places(ORDINAL)[0]

    return {
        "id": "s13_shuffle",
        "mapX": round(x, 3),
        "mapY": round(y, 3),
        "afloat": afloat,
        "budgetFactor": -1.0,
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),

        "siege": {
            "width": WIDE,
            "height": TALL,
            "rows": rows_of(),
            "gems": "rgby",
            "wards": "rgby",

            # No waves and no boss: the muster is a rule (`ShuffleRamp`). Both gates refuse a file
            # carrying a list beside a ramp, and a file carrying two ramps.
            "waves": [],
            "boss": "",
            "cogs": COGS,
            "charms": CHARMS,
            "obsidian": OBSIDIAN,
            "singularity": SINGULARITY,

            "shuffle": {
                "goldWave": GOLD_WAVE,
                "silverFactor": SILVER_FACTOR,
            },
        },
    }


def body():
    return {
        "schemaVersion": 2,
        "id": CHAPTER,

        # Thornwatch's own accent and plate: the same place, played by a dealt hand.
        "accent": "#E8615A",
        "slate": "#1A0F14",
        "backdrop": mapart.sky(ORDINAL, 0, "siege"),
        "mapStrips": mapart.strips(ORDINAL),
        "teaserX": mapart.marker(ORDINAL),
        "teaserAfloat": mapart.marker_afloat(ORDINAL),
        "levels": [level()],
    }


def prove(written):
    """Everything this lane claims about itself, checked against the mirrored rules."""
    block = written["levels"][0]["siege"]

    grid = proto.Grid(block["rows"], block["width"], block["height"], siege.CELLS)
    layout = siege.Layout(grid, block["gems"], block["wards"], block["waves"],
                          block.get("boss"), block.get("cogs", 0), endless=True,
                          charms=block.get("charms", ""),
                          obsidian=block.get("obsidian", False),
                          singularity=block.get("singularity", False))

    if layout.fault:
        sys.exit("s13_shuffle: %s" % layout.fault)

    made = sweep.swaps(list("".join(block["rows"])), block["width"], block["height"])
    if made != SWAPS:
        sys.exit("s13_shuffle: seed %d now deals %d opening swaps, not the %d recorded"
                 % (SEED, made, SWAPS))

    if not siege.any_swap(layout):
        sys.exit("s13_shuffle: this field has no opening swap")

    gold = block["shuffle"]["goldWave"]
    silver = max(1, -(-int(round(gold * block["shuffle"]["silverFactor"] * 100)) // 100))

    if not (1 <= silver < gold):
        sys.exit("s13_shuffle: the two-star wave (%d) is not below the three-star wave (%d)"
                 % (silver, gold))

    colours = list(layout.deal)
    kinds = {}

    for wave in range(1, siege.SHUFFLE_WALKED + 1):
        for colour, kind in siege.shuffle_wave(colours, layout.seed, wave):
            kinds[kind] = kinds.get(kind, 0) + 1

            if kind in siege.BOSSES:
                sys.exit("s13_shuffle: wave %d sends a %s on a lane that promises none" % (wave, kind))

            if colour not in layout.wards:
                sys.exit("s13_shuffle: wave %d sends a '%s' %s and no ward carries '%s'"
                         % (wave, colour, kind, colour))

    print("s13_shuffle   3* wave %-3d 2* wave %-3d cogs %d%%, %d opening swap(s), seed %d"
          % (gold, silver, COGS, made, SEED))

    print("              first %d waves send: " % siege.SHUFFLE_WALKED
          + ", ".join("%d %s" % (n, k) for k, n in sorted(kinds.items())))

    print("              wave 1 %d raiders at %s; wave %d %d raiders at %s"
          % (siege.shuffle_size(1), siege.shuffle_surge(1), siege.SHUFFLE_WALKED,
             siege.shuffle_size(siege.SHUFFLE_WALKED), siege.shuffle_surge(siege.SHUFFLE_WALKED)))


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

# -*- coding: utf-8 -*-
"""Deals Hollowmarch convoys into a designed haul-road and says which are worth keeping.

    python Tools/march_sweep.py --template first --seeds 400
    python Tools/march_sweep.py --board "Z++++++,......R,..." --cores RGB

**What is designed and what is dealt.** The *road* is designed - where it winds, how long
the runway is, where the launcher stands - because that is the thing a player reads and it
wants to be legible rather than lucky. The *convoy* is dealt, because which colours stand
where is exactly the kind of arrangement nobody can eyeball: whether a board chains, whether
a careless player gets through it, and how many shortest answers it has are all facts that
have to be counted (invariant 5d).

Every column is a reading, and each answers a different question:

  par        the fewest cores that free everything, by search
  ways       how many different shortest runs there are. One means the board has to be
             solved rather than played; a few hundred means the line is deciding it
  nodes      what proving it cost, against the ceiling the player's own device pays (26d)
  careless   moves a player who always takes the biggest thing spends, or 0 if they never
             finish. Nought is what a board past the teaching rung wants
  chain      the most waves any single opening shot sets off - the mode's payoff, measured
  forged     opening shots that forge a Spark. A special nobody can make on the board as
             dealt is one the author placed rather than one the player earned (26h)
  runway     shots before the front of the line goes through the portal. It has to be read
             against the allowance, or the board is lost twice over
"""
from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

VERIFY = Path(__file__).resolve().parent / "verify"
sys.path.insert(0, str(VERIFY))

import march                                                            # noqa: E402
import proto                                                            # noqa: E402


# ---------------------------------------------------------------------------- the roads

#: Designed haul-roads. Each one is a single unbranched line from the portal to the
#: launcher, and the walk that reads it refuses anything else by cell (`MarchLayout`).
TEMPLATES = {
    # Teaching. A short serpentine that fits the whole line on screen at once, so the first
    # thing a player ever sees of this mode is the *shape* of a convoy.
    "first": [
        "Z++++++",
        "......+",
        "+++++++",
        "+......",
        "+++++++",
        "......+",
        "......A",
    ],
    # The haul road proper: longer, with the portal in the top corner so the march reads
    # as going somewhere rather than as going round.
    "road": [
        "Z++++++++",
        "........+",
        "+++++++++",
        "+........",
        "+++++++++",
        "........+",
        "........A",
    ],
    # The gate. Ten wide, which is the widest a board here may be.
    "gate": [
        "Z+++++++++",
        ".........+",
        "++++++++++",
        "+.........",
        "++++++++++",
        ".........+",
        "++++++++++",
        "A.........",
    ],
}

# **The road is only as long as the line needs, and that is a drawing decision the render
# made.** The first cut of both of these ran the full height of the board, which measured
# perfectly and drew as forty per cent empty rail behind the convoy - a lot of screen saying
# nothing, on the one mode whose board is supposed to read as a line going somewhere. What is
# left behind the line is room to dump a core into, and a handful of slots is all that is.


def layout(rows, cores, spare=5):
    w = len(rows[0].replace(" ", ""))
    grid = proto.Grid(rows, w, len(rows), march.LETTERS)
    return march.Layout(grid, spare, cores)


def place(road, head, line):
    """Puts a convoy on a road, returning the rows a chapter body would carry."""
    w = len(road[0])
    cells = [list(r) for r in road]
    lay = layout(road, "RGB")
    if head + len(line) > lay.track:
        raise ValueError("the convoy does not fit on this road")
    for k, c in enumerate(line):
        cell = lay.slots[head + k]
        cells[cell // w][cell % w] = c
    return ["".join(r) for r in cells]


# ---------------------------------------------------------------------------- dealing

def deal(rng, length, colours, cages, haulers, wardens):
    """A convoy of `length` pods, settled - never three alike already touching.

    Authored settled is Budburst's rule and it matters more here: a line holding a run of
    three goes off before the player has touched it, which is a board that plays its own
    first move.

    **Dealt in blocks of one or two rather than pod by pod**, which is not a detail. A
    uniformly random line reads as *noise* - the player cannot see a match until they have
    counted, which is exactly the fault Budburst was withdrawn and rebuilt for (invariant
    20l). A line of pairs and singles reads as a line of blocks, and a block of two is a
    match you can see from across the screen. It also decides how the mode plays: a pair is
    what a core completes, so how many there are *is* how many moves the board offers.
    """
    hue = march.COLOURS[:colours]
    line = []

    while len(line) < length:
        pick = [c for c in hue if not line or line[-1] != c]
        c = rng.choice(pick)
        run = 2 if rng.random() < .62 else 1
        for _ in range(min(run, length - len(line))):
            line.append(c)

    slots = list(range(length))
    rng.shuffle(slots)

    # Raiders stand in the line and wear no colour, so they split it - which is what they
    # are for. Placed first, because a cage on a slot a raider took would be lost.
    for _ in range(haulers):
        if slots:
            line[slots.pop()] = march.HAULER
    for _ in range(wardens):
        if slots:
            line[slots.pop()] = march.WARDEN

    # A cage rides a coloured pod, so it keeps its colour and only changes case.
    caged = 0
    while caged < cages and slots:
        at = slots.pop()
        if march.is_colour(line[at]):
            line[at] = line[at].lower()
            caged += 1

    return "".join(line)


def measure(rows, cores, spare):
    lay = layout(rows, cores, spare)
    read = march.readings(lay)

    par, ways, nodes, proved = proto.search(march.Future(march.Board(lay)))
    if not proved or par < 1:
        return None

    budget = par + spare
    greedy = proto.careless(march.Future(march.Board(lay)), budget)

    read.update(par=par, ways=ways, nodes=nodes, budget=budget, careless=greedy,
                goals=march.Board(lay).goals)
    return read


HEAD = ("par", "ways", "nodes", "careless", "chain", "forged", "runway", "budget",
        "goals", "pods", "biggest")


def line_of(read, seed):
    return ("seed %-5d par %-3d ways %-5d nodes %-6d careless %-3d chain %-2d forged %-3d "
            "chained %-2d forged %-2d lanced %-2d menace %-3d budget %-3d goals %-2d pods %d"
            % (seed, read["par"], read["ways"], read["nodes"], read["careless"],
               read["chain"], read["forgeable"], read["chained"], read["forged"],
               read["lanced"], read["menace"], read["budget"], read["goals"],
               read["pods"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template", default="first", choices=sorted(TEMPLATES))
    ap.add_argument("--seeds", type=int, default=200)
    ap.add_argument("--from-seed", type=int, default=0)
    ap.add_argument("--length", type=int, default=10)
    ap.add_argument("--colours", type=int, default=3)
    ap.add_argument("--cages", type=int, default=2)
    ap.add_argument("--haulers", type=int, default=0)
    ap.add_argument("--wardens", type=int, default=0)
    ap.add_argument("--head", type=int, default=8)
    ap.add_argument("--cores", default="RGB")
    ap.add_argument("--spare", type=int, default=5)
    ap.add_argument("--par", default="", help="only keep these pars, e.g. 5-7")
    ap.add_argument("--max-ways", type=int, default=10 ** 9)
    ap.add_argument("--min-chain", type=int, default=0)
    ap.add_argument("--min-forged", type=int, default=0)
    ap.add_argument("--min-chained", type=int, default=0)
    ap.add_argument("--min-lanced", type=int, default=0)
    ap.add_argument("--careless", default="", help="'0' to keep only boards greed loses")
    ap.add_argument("--board", default="", help="measure one board, rows comma-separated")
    ap.add_argument("--show", type=int, default=12)
    args = ap.parse_args()

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        read = measure(rows, args.cores, args.spare)
        if read is None:
            print("this board could not be proved")
            return 1
        for r in rows:
            print("   ", r)
        print(line_of(read, -1))
        return 0

    road = TEMPLATES[args.template]
    lo, hi = (0, 99) if not args.par else tuple(
        int(v) for v in (args.par.split("-") * 2)[:2])

    kept = 0
    for seed in range(args.from_seed, args.from_seed + args.seeds):
        rng = random.Random(seed)
        line = deal(rng, args.length, args.colours, args.cages, args.haulers, args.wardens)

        try:
            rows = place(road, args.head, line)
        except ValueError as bad:
            return print(bad) or 1

        read = measure(rows, args.cores, args.spare)
        if read is None:
            continue

        if not (lo <= read["par"] <= hi):
            continue
        if read["ways"] > args.max_ways:
            continue
        if read["chain"] < args.min_chain:
            continue
        if read["forgeable"] < args.min_forged:
            continue
        if read["chained"] < args.min_chained:
            continue
        if read["lanced"] < args.min_lanced:
            continue
        if args.careless == "0" and read["careless"] != 0:
            continue
        if read["menace"] < read["budget"]:
            continue

        print(line_of(read, seed), " ", line)
        kept += 1
        if kept >= args.show:
            break

    if kept == 0:
        print("nothing kept out of %d deals - loosen something" % args.seeds)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

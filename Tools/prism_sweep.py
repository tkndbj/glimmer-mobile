# -*- coding: utf-8 -*-
"""Deals gems into a designed Prismvale wall and reports what each deal is worth.

    python Tools/prism_sweep.py --template first --seeds 400
    python Tools/prism_sweep.py --board "R--r.@,..." --spare 3

**The wall is designed and the colours are dealt**, which is invariant 32d and the split every
mode here has used since the Iron Quarry. What is *designed* is the thing a player reads: where
the lanterns stand, which cells hold a gem at all, and how far a critter is from the light.
What is *dealt* is the arrangement nobody can eyeball - which colour sits in which socket - and
that is exactly what a sweep is for.

**A template plants what the board needs before the rest is dealt**, which is Kindlewake's own
lesson (invariant 35e) and the general rule 20i states: grow what the board needs before the
answer is carved, so a wall is winnable by construction rather than by rejection. Here that is
one line - every critter is given a lantern in its own run of gem cells - and the sweep still
rejects most seeds, because *cheaply* winnable is a different question from winnable.

Template characters:

    .   bare ground - the light cannot cross it, and it is what shapes a vein
    -   a gem socket; the sweep deals a colour into it
    R G B Y   a lantern, authored, fixed
    @   a critter asleep
"""
from __future__ import annotations

import argparse
import random
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import prism                                                            # noqa: E402
import proto                                                            # noqa: E402

SOCKET = "-"


#: The walls this chapter is drawn on. Two, because two levels ship - and both are designed to
#: be read rather than solved: a lantern in a corner, a critter across a field of gems, and bare
#: ground where the light must not be allowed to wander.
TEMPLATES = {
    # The verb, and nothing else. Two lanterns and two critters, each pair two gems apart, and
    # bare ground scattered through the field so a vein has to be routed rather than merely
    # grown. A player who has understood "make the gems beside the lantern its own colour, all
    # the way to the critter" finishes it, and nothing else is asked.
    "first": [
        "R - - . - .",
        "- - @ - - -",
        "- . - - . -",
        "- - - . - -",
        ". - - @ - -",
        "- . - - - G",
    ],

    # Three lanterns, three critters, and every pair further apart than on the first board -
    # so which light serves which sleeper is a decision rather than the only thing available,
    # and a gem spent on one vein is a gem not spent on another.
    "reach": [
        "R - - - - .",
        "- - @ - - G",
        "- - - . - -",
        "- . - - @ -",
        "- - @ - - -",
        "B - - - . -",
    ],
}


def deal(rows, seed, palette="rgb"):
    """Fills a template's sockets with colours. Deterministic in the seed."""
    rng = random.Random(seed)
    out = []
    for row in rows:
        packed = row.replace(" ", "")
        out.append("".join(rng.choice(palette) if c == SOCKET else c for c in packed))
    return out


def layout(rows, spare=0):
    packed = [r.replace(" ", "") for r in rows]
    grid = proto.Grid(packed, len(packed[0]), len(packed), prism.LETTERS)
    return prism.Layout(grid, spare)


def search(lay, depth, nodes_cap=proto.NODE_BUDGET):
    """`proto.search`, stopped at `depth`.

    **The cap is the whole reason a sweep is affordable.** A wall the search cannot finish
    inside a handful of moves is a wall nobody would ship, and finding that out costs the whole
    node budget - so the sweep asks a shallower question and throws the rest away.
    """
    start = prism.Future(prism.Board(lay))
    if start.won():
        return 0, 0, 0, True

    seen = {start.key(): 0}
    frontier, counts = [start], [1]
    nodes = 0

    for at_depth in range(1, depth + 1):
        nxt, nxt_counts, index = [], [], {}
        won = 0

        for at, ways in zip(frontier, counts):
            nodes += 1
            if nodes > nodes_cap:
                return 0, 0, nodes, False

            for move in range(at.move_count()):
                to = at.play(move)
                if to is None:
                    continue

                if to.won():
                    won = min(proto.MAX_WAYS, won + ways)
                    continue

                key = to.key()
                if key in seen:
                    continue
                if key in index:
                    nxt_counts[index[key]] = min(proto.MAX_WAYS, nxt_counts[index[key]] + ways)
                    continue

                index[key] = len(nxt)
                nxt.append(to)
                nxt_counts.append(ways)

        if won:
            return at_depth, min(proto.MAX_WAYS, won), nodes, True
        if not nxt:
            return 0, 0, nodes, True

        for key in index:
            seen[key] = at_depth
        frontier, counts = nxt, nxt_counts

    return 0, 0, nodes, False


def measure(rows, spare, depth):
    """Everything a deal is judged on, or None if it is not worth judging."""
    lay = layout(rows, spare)
    if lay.fault or lay.stranded():
        return None

    board = prism.Board(lay)
    if board.stirred() or board.finished() or not board.any_move():
        return None

    par, ways, nodes, proved = search(lay, depth)
    if not proved or par < 1:
        return None

    budget = par + spare
    read = prism.readings(lay, budget)
    read.update(par=par, ways=ways, nodes=nodes, budget=budget,
                greedy=proto.careless(prism.Future(prism.Board(lay)), budget),
                swaps=len(lay.swaps), rows=rows)
    return read


HEAD = ("%-6s %4s %5s %6s %5s %5s %5s %5s %5s %5s"
        % ("seed", "par", "ways", "nodes", "less", "dealt", "used", "pair", "idle", "gems"))


def line(seed, r):
    return ("%-6s %4d %5d %6d %5d %5d %5d %5d %5d %5d"
            % (seed, r["par"], r["ways"], r["nodes"], r["greedy"], r["dealt"],
               r["used"], r["paired"], r["idle"], r["gems"]))


def show(rows):
    lay = layout(rows)
    board = prism.Board(lay)
    veins = board.veins()
    for y in range(lay.h):
        out = []
        for x in range(lay.w):
            i = y * lay.w + x
            out.append(board.cells[i].upper() if veins[i] >= 0 else board.cells[i])
        print("    " + "".join(out))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template", default="first")
    ap.add_argument("--seeds", type=int, default=200)
    ap.add_argument("--from", dest="start", type=int, default=0)
    ap.add_argument("--spare", type=int, default=3)
    ap.add_argument("--depth", type=int, default=3)
    ap.add_argument("--palette", default="rgb")
    ap.add_argument("--board", default=None, help="one wall, rows comma-separated")
    ap.add_argument("--keep", type=int, default=20)
    ap.add_argument("--par", type=int, default=0, help="keep only this par")
    ap.add_argument("--used", type=int, default=0, help="least lantern colours a shortest answer uses")
    ap.add_argument("--dealt", type=int, default=-1, help="most gems that may be lit at the deal")
    ap.add_argument("--greedy", default="any", choices=("any", "beaten", "wins"))
    args = ap.parse_args()

    if args.board:
        rows = args.board.split(",")
        read = measure(rows, args.spare, args.depth)
        if read is None:
            lay = layout(rows, args.spare)
            board = prism.Board(lay)
            print("refused: fault=%s stranded=%s stirred=%s finished=%s"
                  % (lay.fault, lay.stranded(), board.stirred(), board.finished()))
            return
        print(HEAD)
        print(line("-", read))
        show(rows)
        return

    template = TEMPLATES[args.template]
    print(HEAD)

    kept = 0
    began = time.time()
    for seed in range(args.start, args.start + args.seeds):
        rows = deal(template, seed, args.palette)
        read = measure(rows, args.spare, args.depth)
        if read is None:
            continue
        if args.par and read["par"] != args.par:
            continue
        if read["used"] < args.used:
            continue
        if args.dealt >= 0 and read["dealt"] > args.dealt:
            continue
        if args.greedy == "beaten" and read["greedy"] > 0:
            continue
        if args.greedy == "wins" and read["greedy"] == 0:
            continue

        print(line(seed, read))
        kept += 1
        if kept >= args.keep:
            break

    print("# %d kept of %d seed(s) in %.1fs"
          % (kept, args.seeds, time.time() - began))


if __name__ == "__main__":
    main()

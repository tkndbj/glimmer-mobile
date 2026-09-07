# -*- coding: utf-8 -*-
"""Deals shards into a designed Emberforge wall and reports every reading worth keeping one for.

    python Tools/ember_sweep.py --template first --seeds 400
    python Tools/ember_sweep.py --board "rgby...,#..gC.." --spare 4

**What is designed and what is dealt** (invariant 32d). What a player *reads* is designed: where
the stone runs, where the cages are buried, how much frost holds the wall up, where a warden
stands. Which colour is in which cell is exactly the sort of arrangement nobody can eyeball, so
it is dealt by seed and the seed is recorded, and a board is kept for what it *measured* rather
than for how it looked while being typed.

The columns, and which of them decide anything:

    par      moves in the shortest answer. The whole ladder derives from it.
    ways     how many shortest answers there are. One is a wall that has to be solved rather
             than played; three hundred is a wall deciding nothing (invariant 5d).
    nodes    what proving it costs on the phone that opens the level (invariant 26d).
    less     what a player who never looks ahead spends. Nought is a wall that asks something.
    life     how many moves the wall survives a spendthrift, whether or not they win on the
             way. **The reading this mode needed and no other one did**: everywhere else a run
             ends when the allowance runs out, and here the wall itself is finite, so a board
             can be dead while the meter still says four moves left.
    chn      the most beats any *shortest* answer sets off. The mode's payoff, measured.
    frg      embers a shortest answer makes. What the player builds rather than is given.
    str      stars a shortest answer spends.
    goal     cages plus wardens.

A dealt wall is always **settled** - never three alike already in a line - because a wall that
fuses before anybody has touched it is not the wall that was authored, proved or graded.
"""
from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import ember                                                            # noqa: E402
import proto                                                            # noqa: E402

#: The cell a template leaves for the dealer.
DEAL = "?"

#: The designed walls. Everything but `?` is authored and stays exactly where it is.
#:
#: **A wall is read top to bottom like a wall**, so the fittings that hold it up are drawn where
#: they hold it: stone across the middle of `ledge` really does stop the column above it from
#: collapsing, and the cages under it really are buried. Nothing here is symmetric, because a
#: symmetric wall makes both halves of every decision worth the same.
TEMPLATES = {
    # 1. The verb, and nothing else: one cage, an open wall, and a par of two - one swap to
    #    fuse an ember and one tap to set it off. Nothing is dealt, deliberately. A dealt ember
    #    would make the first thing anybody ever does in this mode a thing somebody else made
    #    (invariant 20m), and the two halves of the verb are inseparable anyway - a player who
    #    has fused one and not tapped it is looking at a lit fuse that is asking to be touched.
    "first": [
        "??????",
        "??????",
        "??#?#?",
        "??C???",
        "??????",
        "??????",
    ],

    # 2. Two cages that no single cross can reach, so the wall has to give up two embers.
    "pair": [
        "??????",
        "?C????",
        "??#?#?",
        "??????",
        "????C?",
        "??????",
    ],

    # 3. Stone. Two ribs down the wall, so which row a cross is fired along is the question.
    "ribs": [
        "???????",
        "??#?#??",
        "?C?????",
        "??#?#??",
        "?????C?",
        "??#?#??",
        "???????",
    ],

    # 4. Frost. It stops nothing and holds everything above it up, so melting one drops a whole
    #    column into something that matches - the second answer to "where do I fire".
    "frost": [
        "???????",
        "???*???",
        "?C?*?C?",
        "???*???",
        "??#?#??",
        "???????",
        "???????",
    ],

    # 5. Three cages on three different lines, which is three blasts unless a chain carries one
    #    into the next. This is the wall the chain is authored for.
    "chain": [
        "???????",
        "?C???C?",
        "???#???",
        "??#?#??",
        "???????",
        "?????C?",
        "???????",
    ],

    # 6. A pocket: cages down a corridor with stone either side, so exactly one line reaches
    #    each of them and the aim is the whole move.
    "pocket": [
        "????????",
        "?#????#?",
        "?#C??C#?",
        "?#????#?",
        "????????",
        "??#??#??",
        "????????",
    ],

    # 7. The warden, standing on the row two cages share - so the beam that would free them
    #    dies on its plating until the plating is gone.
    "ward": [
        "????????",
        "??C??C??",
        "????????",
        "???W????",
        "????????",
        "??#??#??",
        "????????",
    ],

    # 8. Cages at the four corners of the diagonals, which is the shape only a star reaches.
    "twin": [
        "????????",
        "?C????C?",
        "???##???",
        "????????",
        "?#??????",
        "?????C??",
        "????????",
    ],

    # 9. The deep wall: everything at once, and a cage buried under frost.
    "deep": [
        "????????",
        "?#???#??",
        "??C???C?",
        "???*????",
        "??#??#??",
        "?C??W???",
        "????????",
        "????????",
    ],

    # 10. The finale. Four cages, a warden, frost holding a shelf up, and stone through the
    #     middle so no two goals share a clean line.
    "heart": [
        "????????",
        "?#C???#?",
        "????*???",
        "??C??C??",
        "???##???",
        "?W???C??",
        "????????",
        "????????",
    ],
}


def deal(rows, seed, palette="rgby", embers=()):
    """Fills every `?` with a shard, leaving the wall settled.

    Greedy with a shuffled palette per cell: try each colour until one makes no run of three
    behind or above. It is not a search - it does not have to be, because a wall is never so
    tight that no colour fits - and it is deterministic in the seed, which is the whole point.
    """
    rnd = random.Random(seed)
    w, h = len(rows[0]), len(rows)
    cells = list("".join(rows))

    for i in range(w * h):
        if cells[i] != DEAL:
            continue

        x, y = i % w, i // w
        options = list(palette)
        rnd.shuffle(options)

        for c in options:
            cells[i] = c
            across = x >= 2 and cells[i - 1] == c and cells[i - 2] == c
            down = y >= 2 and cells[i - w] == c and cells[i - 2 * w] == c
            if not across and not down:
                break

    for at in embers:
        cells[at] = ember.EMBER

    return ["".join(cells[y * w:(y + 1) * w]) for y in range(h)]


def layout(rows, spare):
    grid = proto.Grid(rows, len(rows[0]), len(rows), ember.LETTERS)
    return ember.Layout(grid, spare)


def measure(rows, spare, budget=None):
    """Every reading, for one wall. `None` back when it could not be proved."""
    lay = layout(rows, spare)
    if lay.fault:
        return None

    board = ember.Board(lay)
    if board.stirred or board.finished or not board.any_move:
        return None

    par, ways, nodes, proved = proto.search(ember.Future(board))
    if not proved or par < 1:
        return None

    allowance = budget if budget is not None else par + (spare or proto.DEFAULT_SPARE)
    read = ember.readings(lay)

    return dict(par=par, ways=ways, nodes=nodes,
                less=proto.careless(ember.Future(ember.Board(lay)), allowance),
                life=life(lay, allowance),
                chn=read["chained"], frg=read["forged"], stars=read["starred"],
                goal=board.goals, budget=allowance,
                gold=proto.over(par, proto.GOLD_HUNDREDTHS),
                silver=proto.over(par, proto.SILVER_HUNDREDTHS))


def life(lay, budget):
    """How many moves the most extravagant possible player gets out of this wall."""
    most = budget + 4 if 0 < budget < 36 else 40
    at = ember.Board(lay)

    for spent in range(most):
        moves = at.moves()
        if not moves:
            return spent

        best_gain, best_to = -1, None
        for move in moves:
            forked = at.fork()
            log = forked.fire(move)
            if log is None:
                continue
            gain = log.goals * 100 + log.took
            if gain <= best_gain:
                continue
            best_gain, best_to = gain, forked

        if best_to is None:
            return spent
        at = best_to

    return most


HEAD = ("%-6s %4s %5s %6s %4s %4s %4s %4s %4s %4s %5s"
        % ("seed", "par", "ways", "nodes", "less", "life", "chn", "frg", "str", "goal", "bud"))


def line(seed, m):
    return ("%-6s %4d %5d %6d %4d %4d %4d %4d %4d %4d %5d"
            % (seed, m["par"], m["ways"], m["nodes"], m["less"], m["life"],
               m["chn"], m["frg"], m["stars"], m["goal"], m["budget"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template")
    ap.add_argument("--board", help="rows, comma separated - measures one wall and stops")
    ap.add_argument("--seeds", type=int, default=120)
    ap.add_argument("--from", dest="start", type=int, default=0)
    ap.add_argument("--spare", type=int, default=4)
    ap.add_argument("--palette", default="rgby")
    ap.add_argument("--embers", default="", help="cell indices to deal an ember into")
    ap.add_argument("--par", default="", help="lo:hi par band worth printing")
    ap.add_argument("--budget", type=int, default=0, help="0 derives par + spare")
    args = ap.parse_args()

    embers = tuple(int(n) for n in args.embers.split(",") if n.strip())
    budget = args.budget or None

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        for row in rows:
            print("   " + row)
        m = measure(rows, args.spare, budget)
        print(HEAD)
        print(line("-", m) if m else "   unprovable / not settled / no move")
        return

    if args.template not in TEMPLATES:
        sys.exit("templates: %s" % ", ".join(sorted(TEMPLATES)))

    lo, hi = 0, 99
    if args.par:
        lo, hi = (int(n) for n in args.par.split(":"))

    print(HEAD)
    kept = 0

    for seed in range(args.start, args.start + args.seeds):
        rows = deal(TEMPLATES[args.template], seed, args.palette, embers)
        m = measure(rows, args.spare, budget)
        if not m or not (lo <= m["par"] <= hi):
            continue

        print(line(seed, m))
        kept += 1

    print("%d of %d seeds kept" % (kept, args.seeds))


if __name__ == "__main__":
    main()

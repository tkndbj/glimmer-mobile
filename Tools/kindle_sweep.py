# -*- coding: utf-8 -*-
"""Deals colours into a designed Kindlewake hollow and reports every reading worth keeping one for.

    python Tools/kindle_sweep.py --template first --seeds 400
    python Tools/kindle_sweep.py --board "r..R..b,#..g..#" --spare 4

**What is designed and what is dealt** (invariant 32d). What a player *reads* is designed: where
the stone runs, where the critters are asleep, what colour each of them is waiting for, and which
cells hold an ember at all - that last one is the composition, because an ember is the only thing
on this board a finger can touch. Which *channel* each ember carries is exactly the sort of
arrangement nobody can eyeball, so it is dealt by seed, the seed is recorded, and a hollow is kept
for what it measured rather than for how it looked while being typed.

The columns, and which of them decide anything:

    par      strands in the shortest answer. The whole ladder derives from it.
    ways     how many shortest answers there are. One is a hollow that has to be solved rather
             than played; three hundred is a hollow deciding nothing (invariant 5d).
    nodes    what proving it costs on the phone that opens the level (invariant 26d).
    less     what a player who never looks ahead spends. Nought is a hollow that asks something.
    life     how many strands the hollow survives a spendthrift, whether or not they win on the
             way. **Emberforge's reading, needed here for the same reason**: the material is
             finite, so a board can be dead while the meter still says three moves left.
    crs      crossings any *shortest* answer makes. The mode's own arithmetic, measured.
    bln      critters any shortest answer wakes **with a blend** - a colour that took two strands
             to build. The strict one, and the one that condemns a board: a crossing over bare
             ground is a tidier picture and decides nothing (invariant 20m).
    idle     embers with no partner on a clear line. Each one reads as a move that is not there.
    goal     sleeping critters.

A hollow is always authored at rest and cannot be otherwise: nothing here acts until a finger
draws between two embers, and no cell carries light before the first strand.
"""
from __future__ import annotations

import argparse
import random
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(REPO / "Tools" / "verify"))

import kindle                                                           # noqa: E402
import proto                                                            # noqa: E402

#: The cell a template leaves for the dealer: an ember site whose colour is not yet decided.
DEAL = "?"

#: The designed hollows. Everything but `?` is authored and stays exactly where it is.
#:
#: **A hollow is read as a clearing seen from above**, so the stone is drawn where it would
#: stand and the critters are asleep among it. Nothing here is symmetric, because a symmetric
#: hollow makes both halves of every decision worth the same.
TEMPLATES = {
    # **The grammar every one of these is built on, and it is geometry rather than taste.** A
    # critter is only ever lit by a strand that *brackets* it - two embers of one colour with it
    # between them - so every critter needs ember sites on both sides of one of its lines, and a
    # critter wanting a blend needs two such lines. Hence: critters stand on **odd** columns,
    # their rows carry sites on the **even** ones, the rows between them are solid sites so every
    # column has partners above and below, and **stone never shares a column with a critter**.
    # Ten templates written before this grammar existed were geometrically impossible and every
    # one failed the same way - a critter whose column ran into stone before it found a partner.
    #
    # The second constraint is arithmetic: a strand spends two embers, so the longest possible
    # play is about half the material, and the allowance is `par + spare`. A hollow whose meter
    # cannot be reached is one counting down to an ending that will not happen (see `life`), so
    # these carry 14 to 25 embers for pars of 2 to 5.

    # 1. The verb, and nothing else. Three critters wanting one channel each, so a single strand
    #    is never the whole answer and the ones after it teach that light stays where it was put.
    #    No stone: the first hollow anybody meets should not also be teaching what blocks a strand.
    #
    #    **Three rather than two, and the reason is the star ladder rather than the lesson.** At
    #    par 2 the three-star line (`ceil(par x 1.20)`) and the two-star line (`ceil(par x 1.40)`)
    #    both round onto 3, so the middle band is empty and two stars can never be scored on the
    #    first board anybody plays. Par 3 is the shortest answer that has room for all three.
    "first": [
        ".?.?.?",
        "......",
        "?R?G??",
        ".?.?.?",
        "......",
        "?.?B??",
    ],

    # 2. Two critters wanting the *same* channel, and a third wanting another - so the hollow's
    #    first real question is whether one strand can be made to serve two.
    "awake": [
        ".?.?.?",
        "......",
        "?R?R??",
        ".?.?.?",
        "......",
        "?.?G??",
    ],

    # 3. **The blend.** One critter wanting two channels, standing where a row and a column can
    #    both reach it, which is the whole mode in one picture - and the first board on which a
    #    single strand is provably not enough.
    "blend": [
        ".?.?.?",
        "..#...",
        "?M?G??",
        ".?.?.?",
        "....#.",
        "?.?.??",
    ],

    # 4. Stone. Ribs between the bands, so which pair of embers can see each other at all is the
    #    question rather than which pair is nearest.
    "ribs": [
        ".?.?.?.",
        "..#.#..",
        "?M?M?.?",
        "?.?.?.?",
        ".......",
        "?.?Y?.?",
        ".?.?.?.",
    ],

    # 5. Three blends on one line: the hollow where a single strand can serve all of them, and
    #    the one that says what this mode's material is really worth.
    "choir": [
        ".?.?.?.",
        "#.....#",
        "?M?M?M?",
        "?.?.?.?",
        "..#.#..",
        ".?.?.?.",
    ],

    # 6. A pocket. Stone at the mouth of the outer columns, so what is spent there decides what
    #    can still be reached. Par dips here on purpose - a chapter's ramp is not a ladder of
    #    lengths (CLAUDE.md on par being length rather than difficulty).
    "narrow": [
        ".?.?.?.",
        "#.....#",
        "?M?C?M?",
        "?.?.?.?",
        ".......",
        ".?.?.?.",
    ],

    # 7. Two blends of different pairs, far apart, so no colour can be ignored and the material
    #    is wanted at both ends of the hollow at once.
    "prism": [
        ".?.?.?.",
        "..#....",
        "?M?C?.?",
        "?.?.?.?",
        "....#..",
        "?.?Y?.?",
        ".?.?.?.",
    ],

    # 8. A lattice: stone between every band, so every strand is short and the hollow is about
    #    which of several small lines to spend the material on.
    "weft": [
        ".?.?.?.",
        "..#.#..",
        "?M?C?.?",
        "?.?.?.?",
        "..#.#..",
        "?.?Y?.?",
        ".?.?.?.",
    ],

    # 9. Deep: the most material in the chapter and the most lines through it, so a wrong pair is
    #    hard to see coming - the hollow the ember economy is authored for.
    "deep": [
        "?.?.?.?",
        ".?#?.?.",
        "?M?M?.?",
        ".?.?#?.",
        "?.?.?.?",
        ".?C?.?.",
        "?.?.?.?",
    ],

    # 10. The finale. A critter wanting all three channels, which is three strands crossing on one
    #     square, with a second blend competing for the same embers.
    "heart": [
        ".?.?.?.",
        ".......",
        "?M?W?.?",
        "?.?.?.?",
        "..#.#..",
        "?.?C?.?",
        ".?.?.?.",
    ],
}


#: Standing stone, repeated here so the dealer can read a template without importing the whole
#: vocabulary. It is the one character a template writes that also stops a strand.
STONE_CH = "#"


def open_sites(cells, w, h, cell, along_row, colour):
    """Ember sites on one of a cell's two lines that could carry `colour`, split by side.

    Walks outward from the cell in both directions and stops at stone, exactly as a strand
    would - so every site returned really can be one end of a strand that crosses this cell.

    **A site already holding this colour counts**, which is what stops one critter's planting
    from undoing another's: without it every critter takes fresh sites, they run out on any
    board with more than three, and the overwriting is silent - the board still deals, still
    parses and is simply unwinnable.
    """
    x, y = cell % w, cell // w
    before, after = [], []

    for step, into in ((-1, before), (1, after)):
        i, j = x, y
        while True:
            i, j = (i + step, j) if along_row else (i, j + step)
            if not (0 <= i < w and 0 <= j < h):
                break
            at = j * w + i
            if cells[at] == STONE_CH:
                break
            if cells[at] == DEAL or cells[at] == colour:
                into.append(at)

    return before, after


def plant(cells, rnd, side, colour):
    """Takes one end of a planted pair, preferring an ember that is already the right colour."""
    held = [at for at in side if cells[at] == colour]
    if held:
        return rnd.choice(held)

    at = rnd.choice(side)
    cells[at] = colour
    return at


def deal(rows, seed, palette="rgb"):
    """Fills every `?` with an ember channel, planting the pairs a hollow needs first.

    **Solvable by construction rather than by rejection**, which is invariant 20i's rule read
    across from Lightweave: grow what the board needs before the answer is carved, so a hollow
    is winnable because of how it was built rather than because a check happened to pass.
    Dealing colours at random and throwing away what failed kept fewer than one seed in fifty
    on a board with five critters, and every seed it threw away was thrown away for the same
    reason - some critter's channel had no aligned pair anywhere on the board.

    So: every critter, in a seeded order, has each channel it wants planted as a real pair of
    embers bracketing it on its own row or column; whatever sites are left over are filled
    round-robin from a shuffled palette, which keeps the channels balanced. It is still
    entirely decided by the seed, and the hollow is still *measured* afterwards rather than
    trusted - planting makes a board reachable and says nothing at all about whether it is
    worth playing, which is the half the sweep is for.
    """
    rnd = random.Random(seed)
    w, h = len(rows[0]), len(rows)
    cells = list("".join(rows))

    critters = [i for i, c in enumerate(cells) if kindle.is_sleeper(c)]
    rnd.shuffle(critters)

    for cell in critters:
        want = kindle.want_of(cells[cell])

        for k in range(len(kindle.EMBERS)):
            channel = 1 << k
            if not (want & channel):
                continue

            colour = kindle.EMBERS[k]

            # Both sides of one line, so the strand really does cross the critter: a line with
            # nothing on one side of it can never carry a strand over this cell.
            lines = [True, False]
            rnd.shuffle(lines)

            for along_row in lines:
                before, after = open_sites(cells, w, h, cell, along_row, colour)
                if not before or not after:
                    continue

                plant(cells, rnd, before, colour)
                plant(cells, rnd, after, colour)
                break

    sites = [i for i, c in enumerate(cells) if c == DEAL]

    bag = []
    while len(bag) < len(sites):
        lap = list(palette)
        rnd.shuffle(lap)
        bag.extend(lap)

    rnd.shuffle(sites)
    for at, colour in zip(sites, bag):
        cells[at] = colour

    return ["".join(cells[y * w:(y + 1) * w]) for y in range(h)]


def layout(rows, spare):
    grid = proto.Grid(rows, len(rows[0]), len(rows), kindle.LETTERS)
    return kindle.Layout(grid, spare)


def measure(rows, spare, budget=None):
    """Every reading, for one hollow. `None` back when it could not be proved."""
    lay = layout(rows, spare)
    if lay.fault:
        return None

    if kindle.unreachable(lay):
        return None

    board = kindle.Board(lay)
    if board.finished() or not board.any_move():
        return None

    par, ways, nodes, proved = proto.search(kindle.Future(board))
    if not proved or par < 1:
        return None

    allowance = budget if budget is not None else par + (spare or proto.DEFAULT_SPARE)

    # The whole-graph walk is far and away the dearest thing here, so a board that has already
    # failed on par, on `ways` or on what it costs to prove never pays for it. It is a sweep
    # over hundreds of seeds and most of them are rejected on the cheap numbers.
    if ways > 300 or nodes > 20000:
        return dict(par=par, ways=ways, nodes=nodes, less=0, life=0, crs=0, bln=0, idle=99,
                    goal=board.goals(), budget=allowance,
                    gold=proto.over(par, proto.GOLD_HUNDREDTHS),
                    silver=proto.over(par, proto.SILVER_HUNDREDTHS))

    read = kindle.readings(lay, allowance)

    return dict(par=par, ways=ways, nodes=nodes,
                less=proto.careless(kindle.Future(kindle.Board(lay)), allowance),
                life=read["life"],
                crs=read["crossed"], bln=read["blended"], idle=read["idle"],
                goal=board.goals(), budget=allowance,
                gold=proto.over(par, proto.GOLD_HUNDREDTHS),
                silver=proto.over(par, proto.SILVER_HUNDREDTHS))


HEAD = ("%-6s %4s %5s %6s %4s %4s %4s %4s %5s %4s %5s"
        % ("seed", "par", "ways", "nodes", "less", "life", "crs", "bln", "idle", "goal", "bud"))


def line(seed, m):
    return ("%-6s %4d %5d %6d %4d %4d %4d %4d %5d %4d %5d"
            % (seed, m["par"], m["ways"], m["nodes"], m["less"], m["life"],
               m["crs"], m["bln"], m["idle"], m["goal"], m["budget"]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--template")
    ap.add_argument("--board", help="rows, comma separated - measures one hollow and stops")
    ap.add_argument("--seeds", type=int, default=120)
    ap.add_argument("--from", dest="start", type=int, default=0)
    ap.add_argument("--spare", type=int, default=4)
    ap.add_argument("--palette", default="rgb")
    ap.add_argument("--par", default="", help="lo:hi par band worth printing")
    ap.add_argument("--blend", type=int, default=0, help="least `bln` worth printing")
    ap.add_argument("--budget", type=int, default=0, help="0 derives par + spare")
    args = ap.parse_args()

    budget = args.budget or None

    if args.board:
        rows = [r.strip() for r in args.board.split(",")]
        for row in rows:
            print("   " + row)
        m = measure(rows, args.spare, budget)
        print(HEAD)
        print(line("-", m) if m else "   unprovable / finished / no move / a critter walled off")
        return

    if args.template not in TEMPLATES:
        sys.exit("templates: %s" % ", ".join(sorted(TEMPLATES)))

    lo, hi = 0, 99
    if args.par:
        lo, hi = (int(n) for n in args.par.split(":"))

    print(HEAD)
    kept = 0

    for seed in range(args.start, args.start + args.seeds):
        rows = deal(TEMPLATES[args.template], seed, args.palette)
        m = measure(rows, args.spare, budget)
        if not m or not (lo <= m["par"] <= hi) or m["bln"] < args.blend:
            continue

        print(line(seed, m))
        kept += 1

    print("%d of %d seeds kept" % (kept, args.seeds))


def rows_of(template, seed, palette="rgb"):
    """The dealt rows for one seed, so a chapter generator can re-derive a board rather than retype it."""
    return deal(TEMPLATES[template], seed, palette)


if __name__ == "__main__":
    main()

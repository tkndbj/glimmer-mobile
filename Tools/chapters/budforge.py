# -*- coding: utf-8 -*-
"""Author a Budburst grove: the shape, the cocoons and the vines by hand, the colour swept.

This is the tool the Thicket's ten groves and the Tanglewood's ten were found with, kept because
a chapter ships every two to four weeks and the next one will want it. It authors nothing by
itself - it searches for a *fill* against a skeleton somebody drew, and everything it measures
comes from `Tools/verify/bud.py`, the mirror the build gate runs, so it cannot come to believe
something the gate would refuse.

    from budforge import sweep, show
    out = sweep(skeleton, runners, tries=2000, seed=1, pars=(3,))
    show(out[0])

**The skeleton** is a grid of characters:

    ?              a cell to colour
    R G B Y M C W  a cell the author has fixed - never recoloured, so a motif survives the sweep
    o O            a cocoon, and one that takes two cracks
    .              bare ground (a *still* grove only - a living one is a full rectangle)

**Three facts about this mode decide how a sweep has to be run**, and each cost a day to find.

1. **A random fill is settled about one time in four.** Three alike touching is common, so the
   sweep lays pairs on purpose - two alike touching is one wash from bursting, which is what
   makes a grove cascade - and then *mends* whatever came out in a bunch, rather than throwing
   the board away. Rejecting instead only ever finds boards in five or six colours.

2. **Big chains and a par of 3 pull against each other.** A small grove whose best tap runs three
   waves is a grove one tap frees everybody on, so par collapses to one and the star ladder goes
   with it (26d). Par 3 with a real cascade wants a *big* board with *many* cocoons spread wide -
   which is why every chapter here ramps on how many are shut in and nothing else.

3. **A sweep will not find a runner that is worth anything.** A vine end joins a bunch only by
   accident and the far end completes one only by a second accident: over several thousand
   candidates on an unauthored skeleton, not one produced a vine that bought a burst on the
   opening board. So the motif is *stamped* - see `stamp` - and only the rest is swept.
"""
import random
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(
    os.path.dirname(os.path.abspath(__file__)))), "Tools", "verify"))

import bud                                                          # noqa: E402

COLOURS = "RGBYMCW"
DEALS = ["RGB", "GRB", "BRG", "RBG", "GBR", "BGR"]
STRIPS = ["RGBYMC", "RGBYM", "RGBYMCW", "RYGMBC"]


# ---------------------------------------------------------------------------- the fill
def blob(grid, w, h, y, x, c):
    """How many cells of colour `c` are joined to y,x."""
    seen, queue, n = {(y, x)}, [(y, x)], 0
    while queue:
        b, a = queue.pop()
        n += 1
        for db, da in ((0, 1), (1, 0), (0, -1), (-1, 0)):
            p, q = b + db, a + da
            if 0 <= p < h and 0 <= q < w and (p, q) not in seen and grid[p][q] == c:
                seen.add((p, q))
                queue.append((p, q))
    return n


def settles(grid, w, h, y, x, c):
    """Whether putting `c` at y,x leaves the grove settled. `BudBoard.JoinsABunch`."""
    was, grid[y][x] = grid[y][x], c
    n = blob(grid, w, h, y, x, c)
    grid[y][x] = was
    return n < 3


def bunched(grid, w, h):
    """Every cell that is part of three or more alike touching."""
    return [(y, x) for y in range(h) for x in range(w)
            if grid[y][x] in COLOURS and blob(grid, w, h, y, x, grid[y][x]) >= 3]


def make(skeleton, palette, rng, pairs=.60, rounds=300):
    """One candidate grove: pairs laid at random, then mended until it is settled."""
    h, w = len(skeleton), len(skeleton[0])
    grid = [list(r) for r in skeleton]
    frozen = {(y, x) for y in range(h) for x in range(w) if skeleton[y][x] != "?"}

    order = [(y, x) for y in range(h) for x in range(w) if skeleton[y][x] == "?"]
    rng.shuffle(order)

    for y, x in order:
        if grid[y][x] != "?":
            continue

        c = rng.choice(palette)
        grid[y][x] = c

        if rng.random() < pairs:
            spots = [(y + dy, x + dx) for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0))
                     if 0 <= y + dy < h and 0 <= x + dx < w and grid[y + dy][x + dx] == "?"]
            if spots:
                b, a = rng.choice(spots)
                grid[b][a] = c

    for _ in range(rounds):
        bad = [p for p in bunched(grid, w, h) if p not in frozen]
        if not bad:
            return None if bunched(grid, w, h) else ["".join(r) for r in grid]

        y, x = rng.choice(bad)
        choices = [c for c in palette if c != grid[y][x]]
        rng.shuffle(choices)

        for c in choices:
            if settles(grid, w, h, y, x, c):
                grid[y][x] = c
                break
        else:
            return None

    return None


# ---------------------------------------------------------------------------- the vines
def stamp(rows, at, end, near, side):
    """Write the teaching motif: `end` on a runner's end and `near` on two squares beside it.

        near end   X with two Z beside it, where X | (first colour dealt) == Z
        far  end   V with two Z beside it, where V is a subset of Z

    Tap the near end and three Z burst, taking the end in; the Z that runs down the vine does the
    same again at the other end. That is the mechanic said once, on the board, in one tap - and
    it has to be authored, because a sweep will not find it (see the header).

    `side` picks which two neighbours, so a motif can go anywhere without running off the grid.
    **Stamp where no cocoon stands**: the first cut of the Tanglewood wrote four cells straight
    over two cocoons and the rung came back holding fewer critters than the one before it, which
    is a ramp running backwards and only `BudLadderTests` would ever have said so.
    """
    y, x = at
    grid = [list(r) for r in rows]
    grid[y][x] = end

    steps = {"se": ((0, 1), (1, 0)), "nw": ((0, -1), (-1, 0)),
             "sw": ((0, -1), (1, 0)), "ne": ((0, 1), (-1, 0))}[side]

    for dy, dx in steps:
        grid[y + dy][x + dx] = near

    return ["".join(r) for r in grid]


def vines(rows, ends, tag="a"):
    """The runner grid for one or more vines, each given as a pair of (y, x)."""
    h, w = len(rows), len(rows[0])
    grid = [["."] * w for _ in range(h)]

    for n, (a, b) in enumerate(ends):
        c = chr(ord(tag) + n)
        grid[a[0]][a[1]] = c
        grid[b[0]][b[1]] = c

    return ["".join(r) for r in grid]


# ---------------------------------------------------------------------------- the sweep
def judge(rows, deal, strip, runners, pars, node_cap):
    """Everything a candidate has to prove. Returns its readings, or None."""
    grove = bud.Grove(rows, deal, strip, runners)
    board = bud.Board(grove)

    if board.groups():
        return None                                     # not authored settled

    for i in range(grove.count):
        if grove.ground[i] == "c" and not any(grove.ground[n] == "f"
                                              for n in grove.beside(i)):
            return None                                 # a critter nobody can free

    par, ways, nodes, proved = bud.search(rows, deal, strip, runners)
    if not proved or par not in pars or nodes > node_cap:
        return None

    careless = bud.careless(rows, deal, par + bud.DEFAULT_SPARE, strip, runners)
    if careless < 1 or careless > par + bud.DEFAULT_SPARE:
        return None                                     # invariant 20k: careless must finish

    best, where, ran = bud.biggest(rows, deal, strip, runners)
    changed, caught, taps = bud.vines(rows, deal, strip, runners)

    return dict(rows=rows, deal=deal, strip=strip, runners=runners,
                par=par, ways=ways, nodes=nodes, careless=careless,
                bestAt=where, burst=best[0], waves=best[1], freed=best[2],
                ran=ran, changed=changed, caught=caught, taps=taps,
                flowers=board.flowers, cocoons=board.shut)


def rank(d):
    """Vines first, then spectacle, then how many ways there are to win."""
    return (d["caught"], d["waves"], d["freed"], d["ways"])


def sweep(skeleton, runners=None, tries=2000, seed=1, palette="RGBYMC", pairs=.60,
          pars=(3,), node_cap=45_000, deals=None, strips=None, want=None, score=None):
    """Every fill of this skeleton that is fit to ship, best first."""
    rng = random.Random(seed)
    want = want or {}
    found = []

    for _ in range(tries):
        rows = make(skeleton, palette, rng, pairs)
        if rows is None:
            continue

        try:
            got = judge(rows, rng.choice(deals or DEALS), rng.choice(strips or STRIPS),
                        runners, pars, node_cap)
        except (ValueError, KeyError, IndexError):
            continue

        if got is None or any(got[k] < v for k, v in want.items()):
            continue

        found.append(got)

    found.sort(key=score or rank, reverse=True)
    return found


def show(d):
    """One candidate, as an author reads it."""
    print("  deal=%s strip=%s par=%d ways=%d greedy=%d best@%s %dw/%db/%df | ran=%d "
          "changed=%d caught=%d of %d | nodes=%d %d flowers %d shut"
          % (d["deal"], d["strip"], d["par"], d["ways"], d["careless"], d["bestAt"],
             d["waves"], d["burst"], d["freed"], d["ran"], d["changed"], d["caught"],
             d["taps"], d["nodes"], d["flowers"], d["cocoons"]))

    for i, r in enumerate(d["rows"]):
        print("     ", r, "  ", (d["runners"] or [""] * len(d["rows"]))[i])

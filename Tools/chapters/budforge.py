# -*- coding: utf-8 -*-
"""Author a Budburst grove: the shape, the cocoons and the objects by hand, the colour swept.

This is the tool the Thicket's ten groves and the Tanglewood's five were found with, kept because
a chapter ships every two to four weeks and the next one will want it. It authors nothing by
itself - it searches for a *fill* against a skeleton somebody drew, and everything it measures
comes from `Tools/verify/bud.py`, the mirror the build gate runs, so it cannot come to believe
something the gate would refuse.

    from budforge import sweep, show
    out = sweep(skeleton, tries=2000, seed=1, winds="..>...")
    show(out[0])

**The skeleton** is a grid of characters:

    ?              a cell to colour
    R G B Y M C W  a cell the author has fixed - never recoloured, so a motif survives the sweep
    o O            a cocoon, and one that takes two cracks
    p h            a puffball, and a hive
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

3. **A sweep finds an object worth having only if it is asked to.** A windmill whose gust pops,
   a graft the board allows, a puffball a shortest play pops: each is a reading in `bud.survey`
   and `want` is how a sweep holds out for it. Over a few thousand fills of an unauthored
   skeleton about one in ten has a windmill worth tapping and about one in four a graft; a
   puffball or a hive on a shortest play is rarer and wants the piece placed beside the cocoons
   the chain has to reach anyway.
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


# ---------------------------------------------------------------------------- the sweep
def judge(rows, deal, strip, pars, node_cap, winds=None, firefly=None, grafts=False):
    """Everything a candidate has to prove. Returns its readings, or None."""
    grove = bud.Grove(rows, deal, strip, winds, firefly, grafts)
    board = bud.Board(grove)

    if board.groups():
        return None                                     # not authored settled

    for i in range(grove.count):
        if grove.ground[i] in ("c", bud.PUFF, bud.HIVE) and not any(
                grove.ground[n] == "f" for n in grove.beside(i)):
            return None                                 # a piece nobody can reach

    par, ways, nodes, proved, puffed, swarmed = bud.search(rows, deal, strip, winds, firefly,
                                                           grafts)
    if not proved or par not in pars or nodes > node_cap:
        return None

    careless = bud.careless(rows, deal, par + bud.DEFAULT_SPARE, strip, winds, firefly, grafts)
    if careless < 1 or careless > par + bud.DEFAULT_SPARE:
        return None                                     # invariant 20k: careless must finish

    best, where = bud.biggest(rows, deal, strip, winds, firefly, grafts)
    gusts, banked, pairs = bud.objects(rows, deal, strip, winds, firefly, grafts)

    return dict(rows=rows, deal=deal, strip=strip, winds=winds, firefly=firefly, grafts=grafts,
                par=par, ways=ways, nodes=nodes, careless=careless,
                bestMove=where, burst=best[0], waves=best[1], freed=best[2],
                gusts=gusts, banked=banked, pairs=pairs, puffed=puffed, swarmed=swarmed,
                flowers=board.flowers, cocoons=board.shut)


def rank(d):
    """Spectacle, then how many ways there are to win."""
    return (d["waves"], d["freed"], d["ways"])


def sweep(skeleton, tries=2000, seed=1, palette="RGBYMC", pairs=.60,
          pars=(3,), node_cap=45_000, deals=None, strips=None, want=None, score=None,
          winds=None, fireflies=None, grafts=False, log=None):
    """Every fill of this skeleton that is fit to ship, best first.

    `fireflies` is a list of colours to try for the firefly (or None for no firefly); `winds`
    the wind string; `grafts` whether the gesture is on. `want` is a dict of readings and the
    least each may be, which is how a sweep holds out for an object that decides something.
    """
    rng = random.Random(seed)
    want = want or {}
    found = []

    for n in range(tries):
        rows = make(skeleton, palette, rng, pairs)
        if rows is None:
            continue

        firefly = rng.choice(fireflies) if fireflies else None

        try:
            got = judge(rows, rng.choice(deals or DEALS), rng.choice(strips or STRIPS),
                        pars, node_cap, winds, firefly, grafts)
        except (ValueError, KeyError, IndexError):
            continue

        if got is None or any(got[k] < v for k, v in want.items()):
            continue

        found.append(got)
        if log:
            log("  found %d at try %d: " % (len(found), n) + line(got))

    found.sort(key=score or rank, reverse=True)
    return found


def line(d):
    return ("deal=%s strip=%s par=%d ways=%d greedy=%d best=%s %dw/%db/%df | gusts=%d banked=%d "
            "pairs=%d puffed=%d swarmed=%d | nodes=%d %d flowers %d shut"
            % (d["deal"], d["strip"], d["par"], d["ways"], d["careless"], d["bestMove"],
               d["waves"], d["burst"], d["freed"], d["gusts"], d["banked"], d["pairs"],
               d["puffed"], d["swarmed"], d["nodes"], d["flowers"], d["cocoons"]))


def show(d):
    """One candidate, as an author reads it."""
    print("  " + line(d))
    if d.get("winds"): print("  winds=%s" % d["winds"])
    if d.get("firefly"): print("  firefly=%s" % d["firefly"])
    if d.get("grafts"): print("  grafts")
    for r in d["rows"]:
        print("     ", r)

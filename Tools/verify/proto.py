# -*- coding: utf-8 -*-
"""The prototype modes' rules, mirrored offline.

`ProtoSearch` in C#; this file in Python. It was written for five modes at once -
Nectarrun, Ribbonfall, Seedfling and Warrenwake were withdrawn after play, then Toppleglen
and Nova Raid after them, then the Iron Quarry after them, and every one of those sections
went with its mode while the search and the grid never moved. That is the argument for the
split, made five times. The two copies exist for the reason every other mirror in this
project does: the content gate has to run with no Unity anywhere, and a rule that only
exists inside the Editor is a rule nobody checks on the way past.

**Where the two are allowed to differ: nowhere.** Every settle order, tie-break and
refusal below is contract, and the disagreements that matter are silent - a wash that
stops one cell early makes a board *harder* and par comes out one higher, which looks
exactly like a level somebody authored (that is Budburst's, written down in CLAUDE.md).
So `ProtoLadderTests` pins the shipped boards inline on the C# side and
`Tools/verify/content.py` pins them here, and the two numbers are compared by eye every
time a rule moves.

Read `ProtoSearch.cs` first; the names match deliberately.
"""

from collections import deque

import prism                              # Prismvale's rules, mirrored - see prism.py

# ---------------------------------------------------------------------------- the search

#: Positions the search will expand before giving up. `ProtoSearch.NodeBudget`.
NODE_BUDGET = 90000

#: The deepest answer worth looking for. `ProtoSearch.MaxDepth`.
MAX_DEPTH = 24

#: The most shortest-answers reported. `ProtoSearch.MaxWays`.
MAX_WAYS = 9999

#: Room above par every one of these boards forgives. `ProtoLevelRules.DefaultSpare`.
DEFAULT_SPARE = 5

#: The star ladder, in hundredths. Shared with every other mode in the game.
GOLD_HUNDREDTHS = 120
SILVER_HUNDREDTHS = 140


def over(par, hundredths):
    """`LevelTuning.Over` - ceil of exact hundredths, never of a float product."""
    return (par * hundredths + 99) // 100


def search(start):
    """Par, ways, nodes and whether it was proved. Mirrors `ProtoSearch.Solve`.

    Breadth-first, because nothing in any of these modes ever adds to a board: two
    orderings that remove the same things reach the same state and merge, so a layer
    stays small and a state's path count is the sum of its parents'.
    """
    if start is None:
        return 0, 0, 0, True
    if start.won():
        return 0, 0, 0, True

    seen = {start.key(): 0}
    frontier = [start]
    counts = [1]
    nodes = 0

    for depth in range(1, MAX_DEPTH + 1):
        nxt, nxt_counts, index = [], [], {}
        won = 0

        for at, ways in zip(frontier, counts):
            nodes += 1
            if nodes > NODE_BUDGET:
                return 0, 0, nodes, False

            for move in range(at.move_count()):
                to = at.play(move)
                if to is None:
                    continue

                if to.won():
                    won = min(MAX_WAYS, won + ways)
                    continue

                key = to.key()
                if key in seen:
                    continue

                if key in index:
                    slot = index[key]
                    nxt_counts[slot] = min(MAX_WAYS, nxt_counts[slot] + ways)
                    continue

                index[key] = len(nxt)
                nxt.append(to)
                nxt_counts.append(ways)

        if won > 0:
            return depth, min(MAX_WAYS, won), nodes, True

        if not nxt:
            return 0, 0, nodes, True

        for key in index:
            seen[key] = depth

        frontier, counts = nxt, nxt_counts

    return 0, 0, nodes, False


def careless(start, budget):
    """Moves a player who always takes the biggest thing spends, or 0 if they never finish.

    Mirrors `ProtoSearch.Careless`. Ties go to the lowest move index, which makes the
    reading deterministic without claiming the tie-break is what a person would do.
    """
    if start is None or budget <= 0:
        return 0

    at = start
    for spent in range(budget):
        if at.won():
            return spent

        best, best_gain, best_to = -1, None, None
        for move in range(at.move_count()):
            to = at.play(move)
            if to is None:
                continue

            gain = at.gain(move)
            if best_gain is not None and gain <= best_gain:
                continue

            best, best_gain, best_to = move, gain, to

        if best < 0:
            return 0
        at = best_to

    return budget if at.won() else 0


# ---------------------------------------------------------------------------- the grid

MIN_SIDE, MAX_SIDE = 3, 10


class Grid(object):
    """`ProtoGrid`. Spaces are ignored; an unknown letter is refused by row and column."""

    def __init__(self, rows, width, height, letters):
        if not (MIN_SIDE <= width <= MAX_SIDE):
            raise ValueError("a board is %d..%d wide; this one says %d"
                             % (MIN_SIDE, MAX_SIDE, width))
        if not (MIN_SIDE <= height <= MAX_SIDE):
            raise ValueError("a board is %d..%d tall; this one says %d"
                             % (MIN_SIDE, MAX_SIDE, height))
        if rows is None or len(rows) != height:
            raise ValueError("declares %d rows and carries %d"
                             % (height, 0 if rows is None else len(rows)))

        cells = []
        for y, raw in enumerate(rows):
            packed = (raw or "").replace(" ", "")
            if len(packed) != width:
                raise ValueError("row %d is %d cells wide and the board says %d"
                                 % (y, len(packed), width))
            for x, c in enumerate(packed):
                if c not in letters:
                    raise ValueError("row %d column %d is '%s', which is not something this "
                                     "board understands (it reads %s)" % (y, x, c, letters))
                cells.append(c)

        self.w, self.h, self.cells = width, height, cells

    def at(self, index):
        return self.cells[index]

    def xy(self, x, y):
        return self.cells[y * self.w + x]

    def index(self, x, y):
        return y * self.w + x

    def inside(self, x, y):
        return 0 <= x < self.w and 0 <= y < self.h

    def x_of(self, i):
        return i % self.w

    def y_of(self, i):
        return i // self.w

    def count_of(self, c):
        return self.cells.count(c)

    def first_of(self, c):
        return self.cells.index(c) if c in self.cells else -1

    def __len__(self):
        return len(self.cells)


# ---------------------------------------------------------------------------- dispatch

#: Which level field each mode's board is authored under, and how to build one.
MODES = {
    # Prismvale deals nothing into its block: the field of gems is everything the level hands
    # over, so its future is fixed and the search can prove it.
    'prism': (prism.LETTERS,
              lambda grid, block: prism.Future(prism.Board(prism.Layout(grid, 0)))),
}


def build(mode, block):
    """A board of `mode` from its authored block. Raises ValueError with the exact fault."""
    letters, kind = MODES[mode]
    grid = Grid(block.get('rows') or [], block.get('width') or 0, block.get('height') or 0,
                letters)
    return grid, kind(grid, block)

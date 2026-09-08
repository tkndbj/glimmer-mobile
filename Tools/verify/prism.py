# -*- coding: utf-8 -*-
"""Prismvale's rules, mirrored offline.

`PrismBoard.cs` in C#; this file in Python. The two copies exist for the reason every other
mirror in this project does: the content gate has to run with no Unity anywhere, and a rule
that only exists inside the Editor is a rule nobody checks on the way past.

**Where the two are allowed to differ: nowhere.** Every walk order, tie-break and refusal
below is contract. The disagreements that matter are silent - a flood that stops one cell
early makes a board *harder* and par comes out one higher, which looks exactly like a level
somebody authored (that is Budburst's wash bug, written down in CLAUDE.md). So
`ProtoLadderTests` pins the shipped walls inline on the C# side and `Tools/verify/content.py`
pins them here, and the two sets of numbers are compared every time a rule moves.

**One trap this file inherits and must not fall into.** A one-character `in` test against a
string of letters is a *substring* test the moment the cell can be empty, and the empty string
is in everything - so every membership test below is written against a real character and
`is_gem("")` would be a silent yes. It shipped once in `proto.py` and moved `ways`, `nodes`
and the careless reading while leaving par alone, which is the half nobody would have looked
at.

Read `PrismBoard.cs` first; the names match deliberately.
"""

# --------------------------------------------------------------------------- the vocabulary

#: Bare ground. No gem stands on it, nothing swaps with it, and a vein stops dead at it.
#:
#: **It is the only thing that shapes a vein, and that is deliberate.** A second blocking
#: character would be two letters with one rule, which is how two letters come to disagree; what
#: the wall needs is somewhere the light cannot go, and empty ground says that without a second
#: idea to teach.
BARE = "."

#: The four gems, lower case because they are the material. Index i is colour i.
GEMS = "rgby"

#: The four lanterns, upper case because they are fixed. A lantern of colour i feeds gems of
#: colour i and no others, which is the whole of what colour decides here.
LAMPS = "RGBY"

#: A critter asleep. It wants light and does not care which colour brings it - the *lantern*
#: carries the colour, and matching it is what the gems are for.
ASLEEP = "@"

#: A critter that has woken. Not authorable, and it must be in the key or two boards a critter
#: apart would merge in the search and par would come out short.
AWAKE = "*"

LETTERS = BARE + GEMS + LAMPS + ASLEEP


def is_gem(c):
    """Whether this cell holds a gem a finger can move."""
    return len(c) == 1 and c in GEMS


def is_lamp(c):
    """Whether this cell holds a lantern."""
    return len(c) == 1 and c in LAMPS


def is_critter(c):
    """A critter, awake or asleep."""
    return c == ASLEEP or c == AWAKE


def hue_of(c):
    """The colour a gem or a lantern carries, or -1."""
    if is_gem(c):
        return GEMS.index(c)
    if is_lamp(c):
        return LAMPS.index(c)
    return -1


# --------------------------------------------------------------------------- the wall


class Layout(object):
    """`PrismLayout`. The grid, the swaps it could ever hold, and what is wrong with it."""

    def __init__(self, grid, spare=0):
        self.grid = grid
        self.spare = spare if spare > 0 else 0
        self.swaps = self._swaps()
        self.fault = self._wrong()

    @property
    def w(self):
        return self.grid.w

    @property
    def h(self):
        return self.grid.h

    def _swaps(self):
        """Every pair of touching cells that both hold gems, in row-major order.

        Right neighbour first and then the one below, so each pair appears exactly once and
        the order is a fact about the grid rather than about the loop that found it. Mirrors
        `PrismLayout.Swaps`, and the order is contract: move indices come from it.

        **Which cells hold gems never changes**, because a swap only ever exchanges two gems -
        so this is a fact about the layout and is worked out once rather than per position.
        """
        grid = self.grid
        pairs = []

        for y in range(grid.h):
            for x in range(grid.w):
                if not is_gem(grid.xy(x, y)):
                    continue
                here = grid.index(x, y)
                if x + 1 < grid.w and is_gem(grid.xy(x + 1, y)):
                    pairs.append((here, grid.index(x + 1, y)))
                if y + 1 < grid.h and is_gem(grid.xy(x, y + 1)):
                    pairs.append((here, grid.index(x, y + 1)))

        return pairs

    def _wrong(self):
        """Faults that make a wall unopenable rather than unwinnable. Mirrors `PrismLayout.Wrong`."""
        grid = self.grid
        critters = sum(1 for c in grid.cells if c == ASLEEP)
        lamps = sum(1 for c in grid.cells if is_lamp(c))
        gems = sum(1 for c in grid.cells if is_gem(c))

        if critters == 0:
            return ("this wall holds no sleeping critter, so there is nothing to wake and the "
                    "level opens finished")

        if lamps == 0:
            return ("this wall holds no lantern, so no gem on it could ever carry light and "
                    "nothing could ever be woken")

        if gems < 2:
            return ("this wall holds fewer than two gems, so there is no swap to make and the "
                    "run is over before it begins")

        return None

    # ------------------------------------------------------------------ reachability
    def around(self, cell):
        """The four cells touching this one, inside the grid. Mirrors `PrismLayout.Around`."""
        w, h = self.grid.w, self.grid.h
        x, y = cell % w, cell // w
        out = []
        if x > 0:
            out.append(cell - 1)
        if x + 1 < w:
            out.append(cell + 1)
        if y > 0:
            out.append(cell - w)
        if y + 1 < h:
            out.append(cell + w)
        return out

    def regions(self):
        """Which connected run of gem cells each cell belongs to, or -1. Mirrors `PrismLayout.Regions`.

        A gem cell never becomes anything else, so this is a fact about the layout: a vein can
        only ever run inside one of these, whatever the player does.
        """
        grid = self.grid
        into = [-1] * len(grid.cells)
        n = 0

        for start in range(len(grid.cells)):
            if into[start] >= 0 or not is_gem(grid.cells[start]):
                continue

            stack = [start]
            into[start] = n
            while stack:
                at = stack.pop()
                for nb in self.around(at):
                    if into[nb] < 0 and is_gem(grid.cells[nb]):
                        into[nb] = n
                        stack.append(nb)
            n += 1

        return into, n

    def stranded(self):
        """Cells of critters no arrangement of this wall could ever wake. Mirrors `PrismLayout.Stranded`.

        **Certain rather than clever** (invariant 28f). Two facts, and both hold under every
        swap there is: a critter with no gem beside it can never be touched by a vein, and a
        critter whose gems belong to a run no lantern touches can never be fed. What it
        deliberately does not model is whether there are *enough* gems of the right colour, or
        whether two critters want the same ones - that is contention, and contention is the
        search's job.
        """
        into, _ = self.regions()
        grid = self.grid

        lit = set()
        for cell, c in enumerate(grid.cells):
            if not is_lamp(c):
                continue
            for nb in self.around(cell):
                if into[nb] >= 0:
                    lit.add(into[nb])

        out = []
        for cell, c in enumerate(grid.cells):
            if c != ASLEEP:
                continue
            mine = set(into[nb] for nb in self.around(cell) if into[nb] >= 0)
            if not mine or not (mine & lit):
                out.append(cell)

        return out


class Flare(object):
    """`PrismFlare`. Everything one swap did, in the order a view should replay it."""

    def __init__(self):
        self.deeds = []
        self.frm = -1
        self.to = -1
        self.lit = 0
        self.dark = 0
        self.woke = 0


class Board(object):
    """`PrismBoard`. A wall of gems with the light running through it."""

    def __init__(self, layout, cells=None, woke=0):
        self.layout = layout
        self.cells = list(layout.grid.cells) if cells is None else cells
        self._goals = sum(1 for c in layout.grid.cells if c == ASLEEP)
        self.woke = woke
        self._veins = None

    def fork(self):
        return Board(self.layout, list(self.cells), self.woke)

    def goals(self):
        return self._goals

    def goals_left(self):
        return self._goals - self.woke

    def finished(self):
        return self.goals_left() == 0

    # ---------------------------------------------------------------- the light
    def veins(self):
        """Which colour lights each cell, or -1. Mirrors `PrismBoard.Veins`.

        **Derived from the arrangement rather than stored**, which is the whole reason a
        position's key is its cells alone: light here is not something that accumulates, it is
        what the gems standing on the wall happen to be carrying right now. Break a vein and it
        goes out, which is the one thing that makes a swap somewhere else cost something.
        """
        if self._veins is not None:
            return self._veins

        lay = self.layout
        lit = [-1] * len(self.cells)

        for cell, c in enumerate(self.cells):
            if not is_lamp(c):
                continue

            hue = hue_of(c)
            stack = []
            for nb in lay.around(cell):
                if lit[nb] < 0 and is_gem(self.cells[nb]) and hue_of(self.cells[nb]) == hue:
                    lit[nb] = hue
                    stack.append(nb)

            while stack:
                at = stack.pop()
                for nb in lay.around(at):
                    if lit[nb] < 0 and is_gem(self.cells[nb]) and hue_of(self.cells[nb]) == hue:
                        lit[nb] = hue
                        stack.append(nb)

        self._veins = lit
        return lit

    def touched(self, cell):
        """Whether a lit gem is standing beside this cell. Mirrors `PrismBoard.Touched`."""
        lit = self.veins()
        for nb in self.layout.around(cell):
            if lit[nb] >= 0:
                return True
        return False

    def dealt_lit(self):
        """How many gems the wall is dealt with already lit. Invariant 5g, counted."""
        return sum(1 for v in self.veins() if v >= 0)

    def stirred(self):
        """Whether a critter is already touched by light before anybody has played.

        A wall dealt with a vein already reaching a sleeper is a wall whose first move its
        author played - Budburst's "authored settled" rule, and it matters here because the
        goal count the player is graded against would already have moved.
        """
        for cell, c in enumerate(self.cells):
            if c == ASLEEP and self.touched(cell):
                return True
        return False

    # ---------------------------------------------------------------- what may be played
    def can_swap(self, a, b):
        """Whether these two cells may be exchanged. Mirrors `PrismBoard.CanSwap`.

        Two gems of *different* colours. Two alike is a move that changes nothing, and a move
        that changes nothing must answer null or the search never leaves the layer it is on.
        """
        n = len(self.cells)
        if a < 0 or b < 0 or a >= n or b >= n or a == b:
            return False

        here, there = self.cells[a], self.cells[b]
        return is_gem(here) and is_gem(there) and here != there

    def moves(self):
        """Every swap available, in the layout's own stable order."""
        return [(a, b) for a, b in self.layout.swaps if self.can_swap(a, b)]

    def any_move(self):
        for a, b in self.layout.swaps:
            if self.can_swap(a, b):
                return True
        return False

    def stranded(self):
        """Whether this wall can be *proved* never to finish. Mirrors `PrismBoard.Stranded`.

        A certainty and never a guess (invariant 28f), so it under-reports: it asks only the
        two questions no arrangement of the gems can ever answer differently.
        """
        stuck = set(self.layout.stranded())
        for cell, c in enumerate(self.cells):
            if c == ASLEEP and cell in stuck:
                return True
        return False

    # ---------------------------------------------------------------- playing a move
    def play(self, move):
        """Swaps two gems and resolves what the light then reaches. Mirrors `PrismBoard.Play`.

        The order is contract. The gems move; the veins are read again from the arrangement
        that leaves; every critter is then asked *once* whether a lit gem is standing beside
        it. Asking as the flood runs would hand a critter to whichever lantern the loop reached
        first, which is exactly the reading order `FallBoard.Resolve` is built to avoid.
        """
        a, b = move
        if not self.can_swap(a, b):
            return None

        before = [v >= 0 for v in self.veins()]

        self.cells[a], self.cells[b] = self.cells[b], self.cells[a]
        self._veins = None
        now = self.veins()

        log = Flare()
        log.frm, log.to = a, b
        log.deeds.append(("swap", a, b, self.cells[b]))

        # Lit first and in flood order out of the lantern, so a view replaying this draws the
        # vein running outward rather than switching a set of cells on all at once.
        for cell in self._order(now, before):
            log.lit += 1
            log.deeds.append(("lit", cell, now[cell], self.cells[cell]))

        for cell, hue in enumerate(now):
            if before[cell] and hue < 0:
                log.dark += 1
                log.deeds.append(("dark", cell, -1, self.cells[cell]))

        for cell, c in enumerate(self.cells):
            if c != ASLEEP or not self.touched(cell):
                continue
            self.cells[cell] = AWAKE
            self.woke += 1
            log.woke += 1
            log.deeds.append(("wake", cell, self._woke_by(cell, now), c))

        # A critter's cell was never a gem, so waking one moves no vein and nothing has to be
        # read again.
        return log

    def _order(self, now, before):
        """Newly lit cells, outward from the lanterns that light them. Mirrors `PrismBoard.Order`."""
        lay = self.layout
        seen = set()
        out = []

        for cell, c in enumerate(self.cells):
            if not is_lamp(c):
                continue
            hue = hue_of(c)

            queue = []
            for nb in lay.around(cell):
                if now[nb] == hue and nb not in seen:
                    seen.add(nb)
                    queue.append(nb)

            head = 0
            while head < len(queue):
                at = queue[head]
                head += 1
                if not before[at]:
                    out.append(at)
                for nb in lay.around(at):
                    if now[nb] == hue and nb not in seen:
                        seen.add(nb)
                        queue.append(nb)

        return out

    def _woke_by(self, cell, now):
        """Which colour woke this critter, for the drawing. The first lit neighbour, in order."""
        for nb in self.layout.around(cell):
            if now[nb] >= 0:
                return now[nb]
        return -1

    def key(self):
        """The cells and nothing else. Mirrors `PrismBoard.Write`.

        Light is a pure function of the arrangement, so putting it in the key would be putting
        one fact in twice - and a key carrying a derived value is a key that can disagree with
        itself.
        """
        return "".join(self.cells)


class Future(object):
    """`PrismFuture`. A wall as the search sees it."""

    def __init__(self, board):
        self.board = board
        self._moves = board.moves()

    def won(self):
        return self.board.finished()

    def move_count(self):
        return len(self._moves)

    def play(self, move):
        if move < 0 or move >= len(self._moves):
            return None
        forked = self.board.fork()
        return None if forked.play(self._moves[move]) is None else Future(forked)

    def gain(self, move):
        """Mirrors `PrismFuture.Gain` exactly - waking weighs a hundred times lighting."""
        if move < 0 or move >= len(self._moves):
            return 0
        forked = self.board.fork()
        log = forked.play(self._moves[move])
        return 0 if log is None else log.woke * 100 + log.lit - log.dark

    def key(self):
        return self.board.key()

    def any_move(self):
        return self.board.any_move()

    def goals(self):
        return self.board.goals()

    def stranded(self):
        return self.board.stranded()

    @property
    def stirred(self):
        return self.board.stirred()


# --------------------------------------------------------------------------- the readings

#: How deep a wall with no allowance is walked. Mirrors `PrismReading.FreeDepth`.
FREE_DEPTH = 6

DEEP_BUDGET = 60000
DEEP_DEPTH = 10


def readings(layout, budget=0):
    """What a wall is worth. Mirrors `PrismReading.Of` field for field."""
    grid = layout.grid

    gems = lamps = critters = bare = 0
    hues = set()

    for c in grid.cells:
        if is_gem(c):
            gems += 1
        elif is_lamp(c):
            lamps += 1
            hues.add(hue_of(c))
        elif c == ASLEEP:
            critters += 1
        elif c == BARE:
            bare += 1

    board = Board(layout)
    used, paired = walk(layout, budget + 1 if budget > 0 else FREE_DEPTH)

    return dict(gems=gems, lamps=lamps, critters=critters, bare=bare,
                hues=len(hues), dealt=board.dealt_lit(), used=used, paired=paired,
                idle=idlers(layout))


def idlers(layout):
    """Lanterns with no gem at all beside them. Mirrors `PrismReading.Idlers`.

    A lantern is the brightest thing on the wall, so one that could never start a vein reads
    as a route that is not there. Geometry rather than a proof: whether a gem of the right
    colour can be *brought* to it is the search's question, and the answer to that changes
    every move.
    """
    grid = layout.grid
    idle = 0

    for cell, c in enumerate(grid.cells):
        if not is_lamp(c):
            continue
        if not any(is_gem(grid.cells[nb]) for nb in layout.around(cell)):
            idle += 1

    return idle


def walk(layout, cap=FREE_DEPTH):
    """What the *shortest* answers do: how many lantern colours they use, and the best single move.

    Mirrors `PrismReading.Walk`. One breadth-first walk carrying two marks along the frontier,
    read off the first layer that wins - which is the shortest one, because a state is entered
    at its shortest depth and never again.

    **Read over every shortest solution rather than over the opening move**, which is 26h's
    `kindled` and 20m's `fired` asked here: a wall whose very first swap wakes two critters is
    a wall that is *over* in two swaps, so an opening-move reading selects for short boards and
    quietly rejects the good ones.
    """
    start = Board(layout)
    if start.finished():
        return (0, 0)

    def better(a, b):
        return (a[0] | b[0], max(a[1], b[1]))

    seen = set([start.key()])
    frontier = [start]
    carried = [(0, 0)]
    nodes = 0
    won_at, won = 0, None

    deepest = min(cap, DEEP_DEPTH)

    depth = 0
    while frontier and depth < deepest:
        depth += 1
        nxt, marks, index = [], [], {}

        for at, mark in zip(frontier, carried):
            nodes += 1
            if nodes > DEEP_BUDGET:
                return _read(won)

            for move in at.moves():
                forked = at.fork()
                log = forked.play(move)
                if log is None:
                    continue

                hues = mark[0]
                for deed in log.deeds:
                    if deed[0] == "wake" and deed[2] >= 0:
                        hues |= 1 << deed[2]

                here = (hues, max(mark[1], log.woke))

                if forked.finished():
                    if won_at == 0:
                        won_at, won = depth, here
                    elif won_at == depth:
                        won = better(won, here)
                    continue

                if won_at:
                    continue

                key = forked.key()
                if key in seen:
                    continue

                if key in index:
                    marks[index[key]] = better(marks[index[key]], here)
                    continue

                index[key] = len(nxt)
                nxt.append(forked)
                marks.append(here)

        if won_at:
            return _read(won)

        seen.update(index)
        frontier, carried = nxt, marks

    return _read(won)


def _read(won):
    if won is None:
        return (0, 0)
    return (bin(won[0]).count("1"), won[1])

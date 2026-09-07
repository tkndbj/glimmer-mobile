# -*- coding: utf-8 -*-
"""Kindlewake's rules, mirrored offline.

`KindleBoard.cs` in C#; this file in Python. The two copies exist for the reason every other
mirror in this project does: the content gate has to run with no Unity anywhere, and a rule
that only exists inside the Editor is a rule nobody checks on the way past.

**Where the two are allowed to differ: nowhere.** Every walk order, tie-break and refusal
below is contract. The disagreements that matter are silent - a `helps` clause that refuses
one strand too many makes a board *harder* and par comes out one higher, which looks exactly
like a level somebody authored (that is Budburst's wash bug, written down in CLAUDE.md). So
`ProtoLadderTests` pins the shipped hollows inline on the C# side and
`Tools/verify/content.py` pins them here, and the two sets of numbers are compared every time
a rule moves.

**One trap this file inherits and must not fall into.** `'' in 'rgb'` is **True** in Python,
so an empty cell reads as an ember; every membership test below is written against a real
character and `is_ember('')` would be a silent yes. It shipped once in `proto.py` and moved
`ways`, `nodes` and the careless reading while leaving par alone - which is the half nobody
would have looked at.

Read `KindleBoard.cs` first; the names match deliberately.
"""

# --------------------------------------------------------------------------- the vocabulary

#: Bare ground. Light crosses it and nothing stands on it.
BARE = "."

#: Standing stone. It stops a strand dead, and it is the only thing that does.
STONE = "#"

#: The three embers, one per channel, lower case because they are the material. The order is
#: `Energy`'s channel order, so index i is channel 1 << i.
EMBERS = "rgb"

#: Every letter a sleeping critter may be written in - `Energy`'s own alphabet, never a second
#: one. R/G/B are the pure channels, Y/M/C the two-channel blends and W all three.
SLEEPERS = "RGBYMCW"

#: An ember joined into a strand and gone out. Not authorable.
SPENT = "+"

#: A critter that has woken. Not authorable, and it must be in the key or two boards a critter
#: apart would merge in the search and par would come out short.
WOKEN = "*"

LETTERS = BARE + STONE + EMBERS + SLEEPERS

#: Embers a strand joins. Two, and there is no version of this rule with three in it.
JOIN_AT = 2

#: `Energy`'s masks, by letter. Written out rather than derived, because the C# side reads them
#: from `Energy.TryParse` and a table that agreed by construction would agree by accident.
WANTS = {
    "R": 1,
    "G": 2,
    "B": 4,
    "Y": 1 | 2,
    "M": 1 | 4,
    "C": 2 | 4,
    "W": 1 | 2 | 4,
}


def is_ember(c):
    """Whether this is an ember waiting to be joined."""
    return len(c) == 1 and c in EMBERS


def is_sleeper(c):
    """Whether this is a critter still asleep."""
    return len(c) == 1 and c in SLEEPERS


def is_critter(c):
    """A critter, awake or asleep."""
    return is_sleeper(c) or c == WOKEN


def stops(c):
    """Whether a strand dies here: standing stone, and a burnt-out socket.

    Mirrors `KindleLayout.Stops`. A socket blocking light is what makes a pairing a decision -
    without it every strand costs the same two embers and light never hurts, so covering the
    most critters is nearly always right and a careless player finishes every board.
    """
    return c == STONE or c == SPENT


def channel_of(c):
    """The channel an ember carries, or 0."""
    return (1 << EMBERS.index(c)) if is_ember(c) else 0


def want_of(c):
    """The mask a sleeping critter is waiting for, or 0."""
    return WANTS.get(c, 0) if is_sleeper(c) else 0


def channels(mask):
    """How many channels a mask carries."""
    return bin(mask & 7).count("1")


def letter(mask):
    """The letter `Energy` would write this mask as. Mirrors `Energy.Letter`."""
    for k, v in WANTS.items():
        if v == (mask & 7):
            return k
    return "A"


# --------------------------------------------------------------------------- the hollow


class Layout(object):
    """`KindleLayout`. The grid, the candidate pairs, and what is wrong with it."""

    def __init__(self, grid, spare=0):
        self.grid = grid
        self.spare = spare if spare > 0 else 0
        self.pairs = self._reachable()
        self.fault = self._wrong()

    @property
    def w(self):
        return self.grid.w

    @property
    def h(self):
        return self.grid.h

    def _reachable(self):
        """Every pair of same-coloured embers with a clear line between them.

        Rows first and then columns, both outward from the lower index, so the order is a
        fact about the grid rather than about the loop that found it. Mirrors
        `KindleLayout.Reachable`, and the order is contract: move indices come from it.
        """
        grid = self.grid
        w, h = grid.w, grid.h
        pairs = []

        for y in range(h):
            for x in range(w):
                here = grid.xy(x, y)
                if not is_ember(here):
                    continue
                for i in range(x + 1, w):
                    there = grid.xy(i, y)
                    if stops(there):
                        break
                    if there != here:
                        continue
                    pairs.append((grid.index(x, y), grid.index(i, y)))

        for x in range(w):
            for y in range(h):
                here = grid.xy(x, y)
                if not is_ember(here):
                    continue
                for j in range(y + 1, h):
                    there = grid.xy(x, j)
                    if stops(there):
                        break
                    if there != here:
                        continue
                    pairs.append((grid.index(x, y), grid.index(x, j)))

        return pairs

    def _wrong(self):
        """Faults that make a hollow unopenable rather than unwinnable. Mirrors `KindleLayout.Wrong`."""
        grid = self.grid
        sleepers = 0
        per = [0] * len(EMBERS)

        for c in grid.cells:
            if is_sleeper(c):
                sleepers += 1
            elif is_ember(c):
                per[EMBERS.index(c)] += 1

        if sleepers == 0:
            return ("this hollow holds no sleeping critter, so there is nothing to wake and the "
                    "level opens finished")

        if sum(n // JOIN_AT for n in per) == 0:
            return ("no channel on this hollow has %d embers, and it takes %d alike to draw a "
                    "strand - so no light could ever be made here" % (JOIN_AT, JOIN_AT))

        return None


class Flare(object):
    """`KindleFlare`. Everything one join did."""

    def __init__(self):
        self.deeds = []
        self.frm = -1
        self.to = -1
        self.channel = 0
        self.span = 0
        self.woke = 0
        self.stirred = 0
        self.crossings = 0
        self.blended = 0


class Board(object):
    """`KindleBoard`. A hollow being lit."""

    def __init__(self, layout, cells=None, light=None, goals=None, woke=0):
        self.layout = layout
        self.cells = list(layout.grid.cells) if cells is None else cells
        self.light = [0] * len(self.cells) if light is None else light
        self._goals = (sum(1 for c in self.cells if is_sleeper(c))
                       if goals is None else goals)
        self.woke = woke

    def fork(self):
        return Board(self.layout, list(self.cells), list(self.light), self._goals, self.woke)

    def goals(self):
        return self._goals

    def goals_left(self):
        return self._goals - self.woke

    def finished(self):
        return self.goals_left() == 0

    # ---------------------------------------------------------------- what may be played
    def join(self, a, b):
        """Whether these two cells may be joined, and what the strand would carry.

        Mirrors `KindleBoard.Join`. Returns the channel, or 0.
        """
        n = len(self.cells)
        if a < 0 or b < 0 or a >= n or b >= n or a == b:
            return 0

        here = self.cells[a]
        if here != self.cells[b] or not is_ember(here):
            return 0

        if not self.aligned(a, b):
            return 0

        channel = channel_of(here)
        return channel if self.changes(a, b, channel) else 0

    def aligned(self, a, b):
        """Same row or column with no stone strictly between. Mirrors `KindleBoard.Aligned`."""
        w = self.layout.w
        ax, ay = a % w, a // w
        bx, by = b % w, b // w

        if ax != bx and ay != by:
            return False

        sx = 0 if ax == bx else (1 if bx > ax else -1)
        sy = 0 if ay == by else (1 if by > ay else -1)

        x, y = ax + sx, ay + sy
        while x != bx or y != by:
            if stops(self.cells[y * w + x]):
                return False
            x += sx
            y += sy

        return True

    def changes(self, a, b, channel):
        """Whether the strand puts light anywhere that does not already carry it.

        Mirrors `KindleBoard.Changes`. **This is the whole of what this mode calls a no-op.**
        The tighter rule - refuse a strand unless it reaches a sleeping critter that lacks the
        channel - is provably safe for the solver and was written first, and it is wrong for a
        reason no solver can see: it removes the player's ability to be wrong, so the allowance
        can never bind and the meter counts down to an ending that cannot happen.
        """
        w = self.layout.w
        ax, ay = a % w, a // w
        bx, by = b % w, b // w

        sx = 0 if ax == bx else (1 if bx > ax else -1)
        sy = 0 if ay == by else (1 if by > ay else -1)

        x, y = ax, ay
        while True:
            cell = y * w + x
            if not (self.light[cell] & channel):
                return True
            if x == bx and y == by:
                return False
            x += sx
            y += sy

    def moves(self):
        """Every join available, in the layout's own stable order. Each pair once."""
        return [(a, b) for a, b in self.layout.pairs if self.join(a, b)]

    def any_move(self):
        return any(self.join(a, b) for a, b in self.layout.pairs)

    def stranded(self):
        """Whether this hollow can be *proved* never to finish. Mirrors `KindleBoard.Stranded`.

        A certainty and never a guess (invariant 28f), so it under-reports: geometry is
        ignored, which can only make fewer things possible.
        """
        left = [0] * len(EMBERS)
        for c in self.cells:
            if is_ember(c):
                left[EMBERS.index(c)] += 1

        for i, c in enumerate(self.cells):
            if not is_sleeper(c):
                continue
            wanted = want_of(c) & ~self.light[i]
            for k in range(len(EMBERS)):
                if (wanted & (1 << k)) and left[k] < JOIN_AT:
                    return True

        return False

    # ---------------------------------------------------------------- playing a move
    def draw(self, move):
        """Draws one strand and resolves everything that follows. Mirrors `KindleBoard.Draw`.

        The whole line is lit before any critter is asked whether it has woken, and the two
        ends are spent last - they are read as embers by the walk that lights them.
        """
        a, b = move
        channel = self.join(a, b)
        if not channel:
            return None

        log = Flare()
        log.frm, log.to, log.channel = a, b, channel
        log.deeds.append(("strand", a, b, self.cells[a]))

        w = self.layout.w
        ax, ay = a % w, a // w
        bx, by = b % w, b // w

        sx = 0 if ax == bx else (1 if bx > ax else -1)
        sy = 0 if ay == by else (1 if by > ay else -1)

        line = []
        x, y = ax, ay
        while True:
            cell = y * w + x
            line.append(cell)

            was = self.light[cell]
            self.light[cell] = was | channel

            # A crossing is light meeting light of *another* channel. Light meeting its own is
            # a strand laid over a strand, which is a picture and not an event.
            if was and not (was & channel):
                log.crossings += 1
                log.deeds.append(("cross", cell, self.light[cell], self.cells[cell]))
            else:
                log.deeds.append(("light", cell, self.light[cell], self.cells[cell]))

            if x == bx and y == by:
                break
            x += sx
            y += sy

        log.span = len(line)

        for cell in line:
            c = self.cells[cell]
            if not is_sleeper(c):
                continue

            want = want_of(c)
            held = self.light[cell]

            if held & want == want:
                self.cells[cell] = WOKEN
                self.woke += 1
                log.woke += 1
                if channels(want) > 1:
                    log.blended += 1
                log.deeds.append(("wake", cell, held, c))
            else:
                log.stirred += 1
                log.deeds.append(("stir", cell, held, c))

        self.cells[a] = SPENT
        self.cells[b] = SPENT
        log.deeds.append(("spend", a, channel, EMBERS[0]))
        log.deeds.append(("spend", b, channel, EMBERS[0]))

        return log

    def key(self):
        """The cells and the light, and nothing else. Mirrors `KindleBoard.Write`."""
        return ("".join(self.cells), tuple(self.light))


class Future(object):
    """`KindleFuture`. A hollow as the search sees it."""

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
        return None if forked.draw(self._moves[move]) is None else Future(forked)

    def gain(self, move):
        """Mirrors `KindleFuture.Gain` exactly - waking weighs a hundred times stirring."""
        if move < 0 or move >= len(self._moves):
            return 0
        forked = self.board.fork()
        log = forked.draw(self._moves[move])
        return 0 if log is None else log.woke * 100 + log.stirred * 4 + log.crossings

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
        """A hollow cannot act before it is touched, so it is always authored at rest.

        Named anyway because `check_proto` asks every prototype board for it, and a mode that
        simply did not answer would be one whose settledness nothing had considered.
        """
        return False


# --------------------------------------------------------------------------- the readings


#: How deep a hollow with no allowance is walked. Mirrors `KindleReading.FreeDepth`.
FREE_DEPTH = 12


def readings(layout, budget=0):
    """What a hollow is worth. Mirrors `KindleReading.Of` field for field."""
    grid = layout.grid

    embers = critters = blends = stone = 0
    per = [0] * len(EMBERS)

    for c in grid.cells:
        if is_ember(c):
            embers += 1
            per[EMBERS.index(c)] += 1
        elif is_sleeper(c):
            critters += 1
            if channels(want_of(c)) > 1:
                blends += 1
        elif c == STONE:
            stone += 1

    channel_count = sum(1 for n in per if n)
    lonely = sum(1 for n in per if 0 < n < JOIN_AT)

    crossed, blended, best, life = walk(layout, budget + 1 if budget > 0 else FREE_DEPTH)

    return dict(embers=embers, critters=critters, blends=blends, stone=stone,
                channels=channel_count, lonely=lonely, idle=idlers(layout),
                crossed=crossed, blended=blended, best=best, life=life,
                strands=embers // JOIN_AT)


def idlers(layout):
    """Embers no candidate pair can start or end on. Mirrors `KindleReading.Idlers`."""
    used = set()
    for a, b in layout.pairs:
        used.add(a)
        used.add(b)

    return sum(1 for i, c in enumerate(layout.grid.cells) if is_ember(c) and i not in used)


DEEP_BUDGET = 60000
DEEP_DEPTH = 24


def walk(layout, cap=FREE_DEPTH):
    """Crossings, blend-wakings, the biggest single strand, and the deepest play up to `cap`.

    Mirrors `KindleReading.Walk`. One breadth-first walk of the whole reachable graph: the
    shortest-answer readings are carried along the frontier, and the longest play is the
    deepest layer that still held a legal move.

    **The layers are exact, and that is a property of the mode.** Every join spends exactly
    two embers, so every route to a given arrangement has the same length and no state can
    appear at two depths - which is what lets one walk answer both questions.

    **It stops at `cap`, because that is the only question `life` is asked.** The reading is
    read against the allowance, so walking past it buys nothing and costs everything: the whole
    graph of a hollow with twenty embers is ten layers deep and this is by a wide margin the
    dearest thing in the file.
    """
    start = Board(layout)
    if start.finished():
        return (0, 0, 0, 0)

    def better(a, b):
        return (max(a[0], b[0]), max(a[1], b[1]), max(a[2], b[2]))

    seen = {start.key()}
    frontier = [start]
    carried = [(0, 0, 0)]
    nodes = life = won_at = 0
    won = None

    deepest = min(cap, DEEP_DEPTH)

    depth = 0
    while frontier and depth < deepest:
        depth += 1
        nxt, marks, index = [], [], {}

        for at, mark in zip(frontier, carried):
            nodes += 1
            if nodes > DEEP_BUDGET:
                return (won or (0, 0, 0)) + (life,)

            for move in at.moves():
                forked = at.fork()
                log = forked.draw(move)
                if log is None:
                    continue

                # This layer held a legal move, so some play reaches this depth.
                if depth > life:
                    life = depth

                here = (mark[0] + log.crossings,
                        mark[1] + log.blended,
                        max(mark[2], log.woke))

                if forked.finished():
                    # Only the *shortest* answers are read. A state never appears at two
                    # depths, so the first layer that wins is the shortest one.
                    if won_at == 0:
                        won_at, won = depth, here
                    elif won_at == depth:
                        won = better(won, here)
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

        seen.update(index)
        frontier, carried = nxt, marks

    return (won or (0, 0, 0)) + (life,)


def unreachable(layout):
    """The first critter no pair of embers could ever light, as a sentence - or None.

    Mirrors `KindleValidator.Unreachable`, and it is *exact*: the layout's pairs are the
    complete set of lines this hollow could ever hold.
    """
    grid = layout.grid
    w = grid.w

    for cell, c in enumerate(grid.cells):
        if not is_sleeper(c):
            continue

        want = want_of(c)
        for k in range(len(EMBERS)):
            channel = 1 << k
            if not (want & channel) or _covered(layout, cell, channel):
                continue

            return ("the critter at row %d column %d wants '%s' light and no two '%s' embers "
                    "share a clear row or column through its cell, so nothing that happens "
                    "anywhere on this hollow could ever wake it"
                    % (cell // w, cell % w, letter(channel), EMBERS[k]))

    return None


def _covered(layout, cell, channel):
    """Whether some candidate pair of this channel lays a strand across this cell."""
    grid = layout.grid
    w = grid.w
    cx, cy = cell % w, cell // w

    for a, b in layout.pairs:
        if channel_of(grid.cells[a]) != channel:
            continue

        ax, ay = a % w, a // w
        bx, by = b % w, b // w

        if ay == by:
            if cy == ay and min(ax, bx) <= cx <= max(ax, bx):
                return True
        else:
            if cx == ax and min(ay, by) <= cy <= max(ay, by):
                return True

    return False

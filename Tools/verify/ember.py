# -*- coding: utf-8 -*-
"""Emberforge's rules, mirrored offline.

`EmberBoard.cs` in C#; this file in Python. The two copies exist for the reason every other
mirror in this project does: the content gate has to run with no Unity anywhere, and a rule
that only exists inside the Editor is a rule nobody checks on the way past.

**Where the two are allowed to differ: nowhere.** Every settle order, tie-break and refusal
below is contract, and the disagreements that matter are silent - a fuse that anchors one
cell to the left makes a board *harder* and par comes out one higher, which looks exactly
like a level somebody authored (that is Budburst's, written down in CLAUDE.md). So
`ProtoLadderTests` pins the shipped boards inline on the C# side and `content.py` pins them
here, and the two sets of numbers are compared every time a rule moves.

Read `EmberBoard.cs` first; the names match deliberately.
"""

# ---------------------------------------------------------------------------- the vocabulary

BREACH = "."
STONE = "#"
FROST = "*"
CAGE = "C"
WARDEN = "W"

#: A warden with its plating gone. **Not authorable** - a state a board reaches and never one
#: it is written in. It still has to be in the key, or two boards a plate apart would merge in
#: the search and par would come out short.
SCARRED = "V"

#: The four shard colours.
SHARDS = "rgby"

#: An ember: three shards fused. **It has no colour**, deliberately - see `EmberLayout.Ember`.
EMBER = "O"

LETTERS = BREACH + STONE + FROST + CAGE + WARDEN + EMBER + SHARDS

#: Shards alike in a line that fuse, and the threshold every wave of a chain must clear again.
FUSE_AT = 3

#: Plates a warden wears, which is beams that have to reach it.
PLATES = 2

#: The eight directions a blast travels, right/left/down/up first. A cross takes the first
#: four and a star takes all eight, which is why the order matters.
STEP_X = (1, -1, 0, 0, 1, 1, -1, -1)
STEP_Y = (0, 0, 1, -1, 1, -1, 1, -1)

CROSS_RAYS = 4
STAR_RAYS = 8

CROSS, STAR = 0, 1

#: The three things a finger can do. `EmberAim`.
FUSE, FIRE, MERGE = 0, 1, 2

#: `EmberMove.None`: no move at all, which is what a cascade is played with.
NO_MOVE = (-1, -1)


def is_shard(c):
    """Whether this is a plain shard.

    Note the guard against the empty string: `'' in SHARDS` is **True** in Python, so a bare
    `c in SHARDS` on an empty cell reads as a shard. That bug shipped once in this project
    (`proto.py`'s ribbons) and moved `ways`, `nodes` and the careless reading while leaving par
    alone, which is the half nobody would have looked at.
    """
    return bool(c) and c in SHARDS


def is_ember(c):
    return c == EMBER


def is_gem(c):
    """Anything a finger may take hold of."""
    return is_shard(c) or c == EMBER


def is_goal(c):
    return c in (CAGE, WARDEN, SCARRED)


def stops(c):
    """Whether a beam dies here. Stone and armour, and nothing else."""
    return c in (STONE, WARDEN, SCARRED)


# ---------------------------------------------------------------------------- the wall


class Layout(object):
    """`EmberLayout`. The authored wall, plus the adjacency of a rectangle worked out once."""

    def __init__(self, grid, spare=0):
        self.grid = grid
        self.spare = spare if spare > 0 else 0

        w, h = grid.w, grid.h

        pairs = []
        for y in range(h):
            for x in range(w - 1):
                pairs.append((grid.index(x, y), grid.index(x + 1, y)))
        for y in range(h - 1):
            for x in range(w):
                pairs.append((grid.index(x, y), grid.index(x, y + 1)))

        self.pairs = tuple(pairs)
        self.fault = self._wrong()

    @property
    def w(self):
        return self.grid.w

    @property
    def h(self):
        return self.grid.h

    def _wrong(self):
        cells = self.grid.cells
        shards = sum(1 for c in cells if is_shard(c))
        embers = sum(1 for c in cells if is_ember(c))
        goals = sum(1 for c in cells if is_goal(c))

        if goals == 0:
            return ("this wall holds no cage and no warden, so there is nothing to do and the "
                    "level opens finished")
        if shards < FUSE_AT and embers == 0:
            return ("this wall holds %d shard(s) and no ember, and it takes %d alike in a line "
                    "to fuse one - nothing here could ever be set off" % (shards, FUSE_AT))
        return None


class Blast(object):
    """`EmberBlast`. Everything one move did."""

    def __init__(self):
        self.deeds = []
        self.frames = []
        self.waves = 0
        self.took = 0
        self.freed = 0
        self.wrecked = 0
        self.forged = 0
        self.crosses = 0
        self.stars = 0
        self.ignited = 0

    @property
    def goals(self):
        return self.freed + self.wrecked


class Board(object):
    """`EmberBoard`. A wall being taken apart, and the one method that does it."""

    def __init__(self, layout, cells=None, freed=0, wrecked=0, goals=None):
        self.layout = layout
        self.cells = list(layout.grid.cells) if cells is None else cells
        self.freed = freed
        self.wrecked = wrecked
        self._goals = sum(1 for c in self.cells if is_goal(c)) if goals is None else goals

    def fork(self):
        return Board(self.layout, list(self.cells), self.freed, self.wrecked, self._goals)

    @property
    def goals(self):
        return self._goals

    @property
    def goals_left(self):
        return self._goals - self.freed - self.wrecked

    @property
    def finished(self):
        return self.goals_left == 0

    # ------------------------------------------------------------------ what may be played
    def neighbours(self, a, b):
        w = self.layout.w
        n = len(self.cells)
        if not (0 <= a < n and 0 <= b < n):
            return False
        return abs(a % w - b % w) + abs(a // w - b // w) == 1

    def aim(self, a, b):
        """Which of the three things this touch is, or None if it is not a move at all.

        `EmberBoard.Aim`. One predicate rather than three, because every caller - the search,
        the view, the validator - has to agree about what a finger just did, and three of them
        asking three questions is three chances to disagree about whether a move happened.
        """
        cells = self.cells

        if a == b:
            return FIRE if 0 <= a < len(cells) and is_ember(cells[a]) else None

        if not self.neighbours(a, b):
            return None

        x, y = cells[a], cells[b]
        if not is_gem(x) or not is_gem(y):
            return None

        if is_ember(x) and is_ember(y):
            return MERGE

        # An ember standing in for a shard is a legal swap - it is how a shard is slid into a
        # line it could not otherwise reach - so what is asked is whether the *swap* aligns
        # anything, not whether both halves of it are shards.
        cells[a], cells[b] = y, x
        made = self.aligned(a) or self.aligned(b)
        cells[a], cells[b] = x, y
        return FUSE if made else None

    def aligned(self, cell):
        """Whether this cell sits in a run of `FUSE_AT` alike **shards**."""
        cells = self.cells
        c = cells[cell]
        if not is_shard(c):
            return False

        w, h = self.layout.w, self.layout.h
        x, y = cell % w, cell // w

        across = 1
        i = x - 1
        while i >= 0 and cells[y * w + i] == c:
            across += 1
            i -= 1
        i = x + 1
        while i < w and cells[y * w + i] == c:
            across += 1
            i += 1
        if across >= FUSE_AT:
            return True

        down = 1
        j = y - 1
        while j >= 0 and cells[j * w + x] == c:
            down += 1
            j -= 1
        j = y + 1
        while j < h and cells[j * w + x] == c:
            down += 1
            j += 1
        return down >= FUSE_AT

    def moves(self):
        """Every move available: the taps first, then every legal pair - both ways only for a merge.

        A fuse cannot tell the two directions apart, and that is provable rather than observed:
        a swap exchanges two *different* characters and a fused group is uniform, so the two
        swapped cells can never be in the same group. Whichever end completed the match is the
        end the ember lands on, whichever way the finger went. Emitting both would double the
        work and double-count `ways` at every depth. A merge is the exception, because both
        cells are spent and the star goes off on the one the finger ended on.
        """
        out = []
        for i, c in enumerate(self.cells):
            if is_ember(c):
                out.append((i, i))
        for a, b in self.layout.pairs:
            what = self.aim(a, b)
            if what is None:
                continue
            out.append((a, b))
            if what == MERGE:
                out.append((b, a))
        return out

    @property
    def any_move(self):
        for c in self.cells:
            if is_ember(c):
                return True
        for a, b in self.layout.pairs:
            if self.aim(a, b) is not None:
                return True
        return False

    @property
    def stranded(self):
        """A certainty and never a guess. Geometry is ignored, which is the safe direction."""
        embers = 0
        per = dict((h, 0) for h in SHARDS)

        for c in self.cells:
            if is_ember(c):
                embers += 1
            elif is_shard(c):
                per[c] += 1

        return embers + sum(n // FUSE_AT for n in per.values()) < 1

    @property
    def stirred(self):
        """Whether the wall would act before anybody touched it. A board is authored settled."""
        return any(self.aligned(i) for i in range(len(self.cells)))

    # ------------------------------------------------------------------ playing a move
    def fire(self, move):
        """`EmberBoard.Fire`. One touch, resolved to a standstill.

        One step is a fuse pass **or** a beat of a chain, never both, and the wall settles only
        once the chain has finished - a beam that set off an ember names the cell it stood on,
        and gravity in between would fire it from wherever the collapse had put something else.
        """
        a, b = move
        what = self.aim(a, b)
        if what is None:
            return None

        log = Blast()
        cells = self.cells

        pending, blows = [], []

        if what == FIRE:
            log.deeds.append(("tap", a, a, cells[a], 0))
            cells[a] = BREACH
            log.took += 1
            pending.append(a)
            blows.append(CROSS)
        elif what == MERGE:
            log.deeds.append(("fuse", a, b, cells[a], 0))
            log.deeds.append(("fuse", b, b, cells[b], 0))
            cells[a] = BREACH
            cells[b] = BREACH
            log.took += 2
            pending.append(b)
            blows.append(STAR)
        else:
            cells[a], cells[b] = cells[b], cells[a]
            log.deeds.append(("swap", a, b, cells[b], 0))

        log.frames.append(list(cells))

        step = 0
        while True:
            step += 1

            if pending:
                self._detonate(log, step, pending, blows)
                acted = True
            else:
                # The swap's cells reach the first pass and no further: what the player's own
                # move makes stands where their finger ended, and what the cascade makes settles
                # on its own middle (see `_anchor`).
                acted = self._fuse(log, step, move if step == 1 else NO_MOVE)

            if not acted:
                break

            if not pending:
                self._settle(log, step)

            log.waves = step
            log.frames.append(list(cells))

        return log

    def _fuse(self, log, step, move):
        """Every run of alike shards joined where it shares a cell, each group fused to its anchor."""
        cells = self.cells
        w, h = self.layout.w, self.layout.h

        clump = [-1] * len(cells)
        any_run = False

        # Union-find over cells, unioned only *within* a run - so two runs that share a cell
        # become one group and two that merely lie alongside each other do not. That is the
        # genre's own rule for an L or a T, and getting it wrong the easy way (flood filling
        # alike neighbours) would silently turn two matches into one and hand the player a
        # single ember for six shards.
        for y in range(h):
            x = 0
            while x < w:
                c = cells[y * w + x]
                run = 1
                while x + run < w and cells[y * w + x + run] == c:
                    run += 1
                if is_shard(c) and run >= FUSE_AT:
                    any_run = True
                    for i in range(1, run):
                        _join(clump, y * w + x, y * w + x + i)
                x += run

        for x in range(w):
            y = 0
            while y < h:
                c = cells[y * w + x]
                run = 1
                while y + run < h and cells[(y + run) * w + x] == c:
                    run += 1
                if is_shard(c) and run >= FUSE_AT:
                    any_run = True
                    for i in range(1, run):
                        _join(clump, y * w + x, (y + i) * w + x)
                y += run

        if not any_run:
            return False

        groups = {}
        for i in range(len(clump)):
            if clump[i] >= 0:
                groups.setdefault(_root(clump, i), []).append(i)

        for root in sorted(groups):
            members = groups[root]
            if len(members) < FUSE_AT:
                continue

            at = _anchor(members, move)

            for cell in members:
                log.deeds.append(("fuse", cell, at, cells[cell], step))
                cells[cell] = BREACH

            log.took += len(members) - 1
            cells[at] = EMBER
            log.forged += 1
            log.deeds.append(("forge", at, len(members), EMBER, step))

        return True

    def _detonate(self, log, step, pending, blows):
        """Every ray of every blast walked against the wall as it stands, and only then applied."""
        cells = self.cells
        w, h = self.layout.w, self.layout.h

        hits = [0] * len(cells)
        lit = []

        for at, blow in zip(pending, blows):
            if blow == CROSS:
                log.crosses += 1
            else:
                log.stars += 1

            log.deeds.append(("blast", at, blow, "\0", step))

            rays = STAR_RAYS if blow == STAR else CROSS_RAYS
            for d in range(rays):
                x, y = at % w, at // w
                last = at
                while True:
                    x += STEP_X[d]
                    y += STEP_Y[d]
                    if x < 0 or y < 0 or x >= w or y >= h:
                        break
                    cell = y * w + x
                    last = cell
                    hits[cell] += 1
                    if stops(cells[cell]):
                        break

                log.deeds.append(("ray", at, last, str(d), step))

        del pending[:]
        del blows[:]

        for cell in range(len(hits)):
            if hits[cell] == 0:
                continue

            c = cells[cell]

            if is_ember(c):
                log.deeds.append(("ignite", cell, 0, c, step))
                cells[cell] = BREACH
                log.took += 1
                log.ignited += 1
                lit.append(cell)
            elif is_shard(c):
                log.deeds.append(("break", cell, 0, c, step))
                cells[cell] = BREACH
                log.took += 1
            elif c == FROST:
                log.deeds.append(("melt", cell, 0, c, step))
                cells[cell] = BREACH
            elif c == CAGE:
                log.deeds.append(("free", cell, 0, c, step))
                cells[cell] = BREACH
                self.freed += 1
                log.freed += 1
            elif c == WARDEN:
                # Two beams that reached it in the same beat take both plates, which is what
                # "read the wall as it stands, then apply" buys: the answer is the same
                # whichever blast the loop walked first.
                if hits[cell] >= PLATES:
                    log.deeds.append(("wreck", cell, 0, c, step))
                    cells[cell] = BREACH
                    self.wrecked += 1
                    log.wrecked += 1
                else:
                    log.deeds.append(("crack", cell, 0, c, step))
                    cells[cell] = SCARRED
            elif c == SCARRED:
                log.deeds.append(("wreck", cell, 0, c, step))
                cells[cell] = BREACH
                self.wrecked += 1
                log.wrecked += 1

        for cell in lit:
            pending.append(cell)
            blows.append(CROSS)

    def _settle(self, log, step):
        """Gravity. Shards slide down their column; fittings are bolted and do not move.

        A fitting splits a column into segments and the gems inside one compact to its bottom,
        so stone and cages hold the wall up as well as shaping the beams. Nothing is ever
        added, which is what keeps the whole mode monotone (invariant 20j's second test).
        """
        cells = self.cells
        w, h = self.layout.w, self.layout.h

        for x in range(w):
            floor = h - 1
            for y in range(h - 1, -1, -1):
                c = cells[y * w + x]
                if c != BREACH and not is_gem(c):
                    floor = y - 1
                    continue
                if is_gem(c):
                    if y != floor:
                        cells[floor * w + x] = c
                        cells[y * w + x] = BREACH
                        log.deeds.append(("drop", y * w + x, floor * w + x, c, step))
                    floor -= 1

    # ------------------------------------------------------------------ the key
    def key(self):
        """The wall, and nothing else. Every rule reads it and a move resolves before it returns."""
        return "".join(self.cells)


def _join(clump, a, b):
    if clump[a] < 0:
        clump[a] = a
    if clump[b] < 0:
        clump[b] = b

    ra, rb = _root(clump, a), _root(clump, b)
    if ra == rb:
        return
    # Lowest index wins, so the forest a wall builds is a fact about the wall and not about
    # the order the two scans happened to run in.
    if ra < rb:
        clump[rb] = ra
    else:
        clump[ra] = rb


def _root(clump, cell):
    while clump[cell] != cell:
        cell = clump[cell]
    return cell


def _anchor(members, move):
    """`EmberBoard.Anchor`. Where the finger ended, if the group touches it; else the middle.

    A cascade group is handed `NO_MOVE` and always settles on its middle, which is what makes a
    fuse blind to which way the finger went - see `Board.moves`.
    """
    frm, to = move
    if to in members:
        return to
    if frm in members:
        return frm
    return members[len(members) // 2]


class Future(object):
    """`EmberFuture`. One arrangement as the solver sees it."""

    def __init__(self, board):
        self.board = board
        self._moves = board.moves()

    def won(self):
        return self.board.finished

    def move_count(self):
        return len(self._moves)

    def play(self, move):
        if not (0 <= move < len(self._moves)):
            return None
        forked = self.board.fork()
        if forked.fire(self._moves[move]) is None:
            return None
        return Future(forked)

    def gain(self, move):
        if not (0 <= move < len(self._moves)):
            return 0
        forked = self.board.fork()
        log = forked.fire(self._moves[move])
        return 0 if log is None else log.goals * 100 + log.took

    def key(self):
        return self.board.key()

    # ---- the small protocol `content.py`'s shared prototype check asks for.
    def any_move(self):
        return self.board.any_move

    def goals(self):
        return self.board.goals

    def stranded(self):
        return self.board.stranded

    @property
    def stirred(self):
        return self.board.stirred


# ---------------------------------------------------------------------------- readings


def readings(layout):
    """Everything an author wants to know about one wall, counted rather than argued about.

    The opening-move numbers (`chain`, `biggest`) say what a player could set off on move one.
    The ones that judge the *mechanics* come from `deepen`, over every shortest answer - which
    is invariant 26h's `kindled` and Budburst's `fired` asked here, and the distinction that
    matters: an opening-move reading collapses exactly when a board is good.
    """
    board = Board(layout)
    cells = layout.grid.cells

    chain, biggest = 0, 0
    for move in board.moves():
        forked = board.fork()
        log = forked.fire(move)
        if log is None:
            continue
        chain = max(chain, log.waves)
        biggest = max(biggest, log.took)

    per = {}
    for c in cells:
        if is_shard(c):
            per[c] = per.get(c, 0) + 1

    deep = deepen(layout)

    return dict(shards=sum(1 for c in cells if is_shard(c)),
                dealt=sum(1 for c in cells if is_ember(c)),
                cages=sum(1 for c in cells if c == CAGE),
                wardens=sum(1 for c in cells if c == WARDEN),
                stone=sum(1 for c in cells if c == STONE),
                frost=sum(1 for c in cells if c == FROST),
                colours=len(per),
                lonely=sum(1 for h in per if per[h] < FUSE_AT),
                chain=chain, biggest=biggest,
                settled=0 if board.stirred else 1,
                chained=deep["chained"], forged=deep["forged"], starred=deep["starred"])


#: What `deepen` will expand before giving up. The same walk as `ProtoSearch.Solve`, so it
#: cannot be dearer than the search that already ran.
DEEP_BUDGET = 90000
DEEP_DEPTH = 24


def deepen(layout):
    """What the *shortest* answers actually do, rather than what the opening move can do.

    **This is the reading that judges the mechanics.** `chain` above asks what a player could
    set off on move one, which collapses exactly when a board is good: a wall whose very first
    swap sets off a four-wave cascade is a wall that is *over* in three moves, so tuning
    against it selects for short boards and quietly punishes the good ones.

    Carried along the frontier rather than searched again: a state reached at depth d by two
    routes keeps the better of the two, so what comes back at the winning layer is a fact about
    the set of shortest solutions and costs one triple of integers per state.
    """
    start = Board(layout)
    if start.finished:
        return dict(chained=0, forged=0, starred=0)

    def better(a, b):
        return (max(a[0], b[0]), max(a[1], b[1]), max(a[2], b[2]))

    seen = {start.key()}
    frontier = [(start, (0, 0, 0))]
    nodes = 0

    for _ in range(DEEP_DEPTH):
        nxt, index = [], {}
        won = None

        for board, carried in frontier:
            nodes += 1
            if nodes > DEEP_BUDGET:
                return dict(chained=0, forged=0, starred=0)

            for move in board.moves():
                forked = board.fork()
                log = forked.fire(move)
                if log is None:
                    continue

                mark = (max(carried[0], log.waves),
                        carried[1] + log.forged,
                        carried[2] + log.stars)

                if forked.finished:
                    won = mark if won is None else better(won, mark)
                    continue

                key = forked.key()
                if key in seen:
                    continue

                if key in index:
                    at = index[key]
                    nxt[at] = (nxt[at][0], better(nxt[at][1], mark))
                    continue

                index[key] = len(nxt)
                nxt.append((forked, mark))

        if won is not None:
            return dict(chained=won[0], forged=won[1], starred=won[2])

        if not nxt:
            return dict(chained=0, forged=0, starred=0)

        seen.update(index)
        frontier = nxt

    return dict(chained=0, forged=0, starred=0)

"""Lightfall's rules, mirrored for the offline gate and for authoring.

**This is a mirror and the C# is authoritative.** `FallBoard`, `FallVerdict` and `FallSolver`
are what ships and what a player's device runs; this exists because `content.py` runs with no
Unity anywhere and because a chapter is *authored* by a script that has to be able to measure a
board before it writes it down.

Two copies of one rule is exactly what invariant 9a refuses to leave unpinned, so
`fall-vectors.json` is the contract between them: it carries boards, processions and the par,
ways and greedy reading each one produces, `FallVectorTests` runs it through the C# copy and
`content.py` runs it through this one. Drift fails a gate instead of printing beside the word
"ok".

The subtle part - and the reason a *loose* mirror would be wrong within a drop - is the wash.
A wave decides what bursts and what it washes from the positions the bursting motes are
standing in, **before** anything is removed and **before** anything falls. Applying the wash
after gravity, or resolving one burst at a time, gives a different board on any well where two
bursts share a neighbour or a column collapses under one.
"""

R, G, B = 1, 2, 4
ALL = R | G | B

LETTERS = {'R': R, 'G': G, 'B': B, 'Y': R | G, 'M': R | B, 'C': G | B, 'W': ALL}
LETTER_OF = {v: k for k, v in LETTERS.items()}

BRIM = 0

#: What the C# solver spends. Both copies carry the same figure so a board that is provable in
#: one is provable in the other - a mirror that could afford more than the game is a mirror that
#: passes boards the phone would fail.
NODE_BUDGET = 250_000
MAX_DROPS = 28
MOST_WAYS = 100_000

NEIGHBOURS = ((0, -1), (1, 0), (0, 1), (-1, 0))


def parse_rows(rows, width=None, height=None):
    """Reads an authored fill into a flat list of masks. Spaces are ignored."""
    if not rows:
        raise ValueError('a well has to be told what is standing in it')

    cells, w = [], None
    for y, row in enumerate(rows):
        line = [c for c in row if c not in ' \t']
        if w is None:
            w = len(line)
        elif len(line) != w:
            raise ValueError('row %d names %d cells, expected %d' % (y, len(line), w))
        for x, c in enumerate(line):
            if c in '.-':
                cells.append(0)
                continue
            if c not in LETTERS:
                raise ValueError("'%s' at row %d column %d is not a mote" % (c, y, x))
            mask = LETTERS[c]
            if mask == ALL:
                raise ValueError('row %d column %d is already white' % (y, x))
            cells.append(mask)

    if width is not None and w != width:
        raise ValueError('declared %d wide, rows name %d' % (width, w))
    if height is not None and len(rows) != height:
        raise ValueError('declared %d rows, wrote %d' % (height, len(rows)))
    return cells, w, len(rows)


def parse_deal(motes):
    """Reads a procession. Pure colours only - a blend would hand over a step for free."""
    out = []
    for i, c in enumerate(motes):
        if c in ' \t_':
            continue
        if c not in LETTERS:
            raise ValueError("'%s' at %d is not a colour" % (c, i))
        mask = LETTERS[c]
        if mask not in (R, G, B):
            raise ValueError("'%s' at %d is a blend; a well deals pure light only" % (c, i))
        out.append(mask)
    if not out:
        raise ValueError('a deal of nothing deals nothing')
    return out


def written(cells, width):
    """The rows again, for a round trip."""
    out = []
    for y in range(len(cells) // width):
        out.append(''.join(LETTER_OF[cells[y * width + x]] if cells[y * width + x] else '.'
                           for x in range(width)))
    return out


class Well(object):
    """One well being played on. Mirrors `FallBoard`."""

    __slots__ = ('w', 'h', 'cells', 'flooded')

    def __init__(self, cells, width, height, flooded=False):
        self.w, self.h = width, height
        self.cells = list(cells)
        self.flooded = flooded

    def fork(self):
        return Well(self.cells, self.w, self.h, self.flooded)

    # ---------------------------------------------------------------- reading
    def top_of(self, x):
        for y in range(self.h):
            if self.cells[y * self.w + x]:
                return y
        return -1

    def first_free(self, x):
        for y in range(self.h - 1, -1, -1):
            if not self.cells[y * self.w + x]:
                return y
        return -1

    def landing(self, colour, x):
        top = self.top_of(x)
        if top >= 0:
            mote = self.cells[top * self.w + x]
            if (mote | colour) != mote:
                return top
        return self.first_free(x)

    def enriches(self, colour, x):
        top = self.top_of(x)
        if top < 0:
            return False
        mote = self.cells[top * self.w + x]
        return (mote | colour) != mote

    def bursts(self, colour, x):
        top = self.top_of(x)
        if top < 0:
            return False
        return (self.cells[top * self.w + x] | colour) == ALL

    @property
    def motes(self):
        return sum(1 for c in self.cells if c)

    @property
    def is_empty(self):
        return not any(self.cells)

    @property
    def headroom(self):
        highest = self.h
        for x in range(self.w):
            top = self.top_of(x)
            if 0 <= top < highest:
                highest = top
        return max(0, highest - 1)

    @property
    def wanted(self):
        mask = 0
        for c in self.cells:
            if c:
                mask |= ALL & ~c
        return mask

    def can_drop(self, colour, x):
        return (not self.flooded and not self.is_empty
                and 0 <= x < self.w and self.landing(colour, x) >= 0)

    # ---------------------------------------------------------------- dropping
    def drop(self, colour, x, record=False):
        """Drops a mote and resolves everything that follows. Mirrors `FallBoard.Drop`."""
        if not self.can_drop(colour, x):
            return None

        at = self.landing(colour, x)
        index = at * self.w + x
        enriched = self.cells[index] != 0
        self.cells[index] |= colour

        steps = self._resolve(colour, record)
        self.flooded = any(self.cells[BRIM * self.w + x] for x in range(self.w))

        return dict(column=x, row=at, colour=colour, enriched=enriched, steps=steps)

    def _resolve(self, wash, record):
        steps = []
        wave = 0

        while True:
            burst = [i for i, c in enumerate(self.cells) if c == ALL]
            if not burst:
                break

            bursting = set(burst)
            washed = []
            seen = set()
            for at in burst:
                x, y = at % self.w, at // self.w
                for dx, dy in NEIGHBOURS:
                    nx, ny = x + dx, y + dy
                    if not (0 <= nx < self.w and 0 <= ny < self.h):
                        continue
                    ni = ny * self.w + nx
                    if ni in bursting or ni in seen:
                        continue
                    mote = self.cells[ni]
                    if not mote or (mote | wash) == mote:
                        continue
                    seen.add(ni)
                    washed.append(ni)

            for at in burst:
                self.cells[at] = 0
            for at in washed:
                self.cells[at] |= wash

            moved = self._settle()
            wave += 1

            if record:
                steps.append(dict(burst=burst, washed=washed, wave=wave, moved=moved))
            else:
                steps.append(None)

        return steps

    def _settle(self):
        moved = []
        for x in range(self.w):
            write = self.h - 1
            for y in range(self.h - 1, -1, -1):
                at = y * self.w + x
                if not self.cells[at]:
                    continue
                if y != write:
                    to = write * self.w + x
                    self.cells[to] = self.cells[at]
                    self.cells[at] = 0
                    moved.append((at, to))
                write -= 1
        return moved

    def signature(self):
        return tuple(self.cells)


# -------------------------------------------------------------------- the search
def search(cells, width, height, deal, count_ways=True):
    """Fewest drops that empty this well without flooding it. Mirrors `FallSolver`.

    Returns (par, ways, nodes, proved). `par` is -1 when nothing was proved; `proved` tells
    "searched it all and there is no answer" apart from "ran out of budget", which is the
    difference between a level that is broken and one that is too big.
    """
    start = Well(cells, width, height)
    if start.is_empty:
        return 0, 1, 0, True

    frontier = [start]
    paths = [1]
    seen = {start.signature()}
    nodes = 0

    for drops in range(1, MAX_DROPS + 1):
        colour = deal[(drops - 1) % len(deal)]

        nxt, next_paths, index = [], [], {}
        ways, emptied = 0, False

        for f, board in enumerate(frontier):
            arrivals = paths[f]

            for x in range(width):
                if not board.can_drop(colour, x):
                    continue

                nodes += 1
                if nodes > NODE_BUDGET:
                    return -1, 0, nodes, False

                child = board.fork()
                child.drop(colour, x)

                # A flooded well is a dead position, never a winning one: par is the fewest
                # drops that empty it *without* breaching the brim, because a run that breaches
                # it is over. This one line is what keeps the two fail states from disagreeing
                # about which boards are winnable.
                if child.flooded:
                    continue

                if child.is_empty:
                    emptied = True
                    if not count_ways:
                        return drops, 1, nodes, True
                    ways = min(MOST_WAYS, ways + arrivals)
                    continue

                if emptied:
                    continue

                key = child.signature()
                if key in seen:
                    if key in index:
                        at = index[key]
                        next_paths[at] = min(MOST_WAYS, next_paths[at] + arrivals)
                    continue

                seen.add(key)
                index[key] = len(nxt)
                nxt.append(child)
                next_paths.append(arrivals)

        if emptied:
            return drops, max(1, ways), nodes, True
        if not nxt:
            return -1, 0, nodes, True

        frontier, paths = nxt, next_paths

    return -1, 0, nodes, False


def greedy(cells, width, height, deal):
    """Drops a player who never looks ahead would take, or -1 when they lose."""
    board = Well(cells, width, height)

    for drops in range(MAX_DROPS):
        if board.is_empty:
            return drops

        colour = deal[drops % len(deal)]
        best, best_burst, best_enriches = -1, -1, False

        for x in range(width):
            if not board.can_drop(colour, x):
                continue
            trial = board.fork()
            result = trial.drop(colour, x, record=True)
            if result is None or trial.flooded:
                continue

            burst = sum(len(step['burst']) for step in result['steps'])
            enriches = board.enriches(colour, x)
            better = burst > best_burst or (burst == best_burst and enriches and not best_enriches)
            if not better:
                continue
            best, best_burst, best_enriches = x, burst, enriches

        if best < 0:
            return -1
        board.drop(colour, best)

    return MAX_DROPS if board.is_empty else -1


def survey(rows, motes, width=None, height=None):
    """Everything an author needs to judge a well by, from the two things they wrote."""
    cells, w, h = parse_rows(rows, width, height)
    deal = parse_deal(motes)

    par, ways, nodes, proved = search(cells, w, h, deal)
    well = Well(cells, w, h)

    return dict(width=w, height=h, cells=cells, deal=deal,
                par=par, ways=ways, nodes=nodes, proved=proved,
                greedy=greedy(cells, w, h, deal),
                motes=well.motes, headroom=well.headroom,
                wanted=well.wanted,
                channels=_channels(deal))


def _channels(deal):
    mask = 0
    for c in deal:
        mask |= c
    return mask


# -------------------------------------------------------------------- grading
#: Mirrors LevelTuning. Hundredths, never floats - `1.20f` is 1.20000004768..., so
#: `ceil(45 * 1.20f)` is 55 where the design says 54, and that shipped once already.
GOLD_HUNDREDTHS, SILVER_HUNDREDTHS, BUDGET_HUNDREDTHS = 120, 140, 160

#: Wasted drops a well forgives above par, mirroring `FallRules.DefaultSpare`. A count rather
#: than a factor: a wrong drop is permanent *and* leaves a mote that still has to be cooked, so
#: a mistake costs about two drops wherever it happens - where a fraction of par gives a par-2
#: well two drops of room and a par-6 well four.
DEFAULT_SPARE = 5


def over(par, hundredths):
    return (par * hundredths + 99) // 100


def budget_for(par, budget_hundredths=BUDGET_HUNDREDTHS):
    return over(max(1, par), budget_hundredths)

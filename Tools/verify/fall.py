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

#: A lens: one bit above the three channels, so it is occupied and is not light. Mirrors
#: `FallCell.Lens`. It can never equal ALL, which is what makes "a cell that reached white
#: bursts" correct for glass with no clause of its own.
LENS = 8

#: A lens holding all three, which is the state that fires. Never authored - `w` is refused at
#: parse exactly as `W` is for a mote, because a board that goes off before anybody has touched it
#: is a board whose author meant something else.
FULL = LENS | ALL

#: Glass is written in lower case and light in upper: `O` is empty glass and `r`, `g`, `b`, `y`,
#: `m`, `c` are glass already holding that much. A pre-charged lens is the chapter's gentleness
#: dial - an early board asks for one well-aimed burst where a late one asks for all three.
LETTERS = {'R': R, 'G': G, 'B': B, 'Y': R | G, 'M': R | B, 'C': G | B, 'W': ALL,
           'O': LENS, 'r': LENS | R, 'g': LENS | G, 'b': LENS | B,
           'y': LENS | R | G, 'm': LENS | R | B, 'c': LENS | G | B, 'w': FULL}
LETTER_OF = {v: k for k, v in LETTERS.items()}


def is_lens(cell):
    return bool(cell & LENS)


def is_mote(cell):
    """A cell made of light: the only kind that can be enriched, burst, or want a channel."""
    return cell != 0 and not (cell & LENS)


def charge(cell):
    """The channels a lens is holding. Nought for a mote and for bare ground."""
    return (cell & ALL) if is_lens(cell) else 0


def wants(cell):
    """The channels this cell still lacks before it goes off. Nought for bare ground.

    Both kinds want something now. A mote wants what it needs to reach white and burst; a lens
    wants what it needs to reach white and *fire*. That is the same sentence twice, which is the
    whole reason the rule needed nothing new taught: light fills a thing up and then it goes off.
    """
    if not cell:
        return 0
    return ALL & ~(cell & ALL)


BRIM = 0

#: What the C# solver spends. Both copies carry the same figure so a board that is provable in
#: one is provable in the other - a mirror that could afford more than the game is a mirror that
#: passes boards the phone would fail.
NODE_BUDGET = 250_000
MAX_DROPS = 28
MOST_WAYS = 100_000

NEIGHBOURS = ((0, -1), (1, 0), (0, 1), (-1, 0))

#: The two ways a lens fires when it was filled rather than struck. Not a reduction of four: a
#: well has gravity, so a lens rests on something - its downward beam travels one cell into what
#: is holding it up and its upward one flies into the air above the stack. Only across is there
#: anything to cross. A lens another lens's beam strikes fires along all four.
SIDEWAYS = ((1, 0), (-1, 0))


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
            if mask == FULL:
                raise ValueError('row %d column %d is glass already full, so it would fire '
                                 'before the player had touched it' % (y, x))
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
            what = 'a lens' if is_lens(mask) else 'a blend'
            raise ValueError("'%s' at %d is %s; a well deals pure light only" % (c, i, what))
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

    __slots__ = ('w', 'h', 'cells', 'flooded', 'struck')

    def __init__(self, cells, width, height, flooded=False, struck=None):
        self.w, self.h = width, height
        self.cells = list(cells)
        self.flooded = flooded
        #: Which lenses were struck by another lens's beam, and so fire along all four axes
        #: rather than sideways. It outlives the wave that sets it - a lens a beam lands on is
        #: filled in one wave and fires in the next, and the well settles in between - so it is
        #: carried by `_settle` and cleared when the glass leaves. Empty at rest.
        self.struck = set(struck) if struck else set()

    def fork(self):
        return Well(self.cells, self.w, self.h, self.flooded, self.struck)

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
            cell = self.cells[top * self.w + x]
            # A drop is taken by whatever is on top if it lacks the colour - a mote is enriched,
            # a lens is charged. Neither raises the stack.
            if (cell | colour) != cell:
                return top
        return self.first_free(x)

    def enriches(self, colour, x):
        top = self.top_of(x)
        if top < 0:
            return False
        mote = self.cells[top * self.w + x]
        return is_mote(mote) and (mote | colour) != mote

    def charges(self, colour, x):
        """Whether a drop here would be taken in by glass rather than by a mote."""
        top = self.top_of(x)
        if top < 0:
            return False
        cell = self.cells[top * self.w + x]
        return is_lens(cell) and (cell | colour) != cell

    def takes(self, colour, x):
        """Whether whatever is on top takes this drop, rather than the stack growing a row.

        One question with two answers - a mote lacking the colour is enriched, a lens lacking it
        is charged - and it exists because asking the two separately is how a caller comes to ask
        only one of them. `enriches` is false for every charging drop, which is right for the
        question it asks and was catastrophic where it stood in for this one: the view used it to
        decide whether the falling widget was handed back, so a drop taken in by glass left the
        lens's own widget drawn on a board that no longer tracked it.
        """
        top = self.top_of(x)
        if top < 0:
            return False
        cell = self.cells[top * self.w + x]
        return (cell | colour) != cell

    def bursts(self, colour, x):
        top = self.top_of(x)
        if top < 0:
            return False
        mote = self.cells[top * self.w + x]
        return is_mote(mote) and (mote | colour) == ALL

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
            mask |= wants(c)
        return mask

    @property
    def lenses(self):
        return sum(1 for c in self.cells if is_lens(c))

    @property
    def glass(self):
        """The lenses and how full each is, for a report that has to say how much is asked."""
        return [charge(c) for c in self.cells if is_lens(c)]

    @property
    def cookable(self):
        """Whether anything here could ever burst. Glass cannot - see `FallBoard.Cookable`."""
        return any(is_mote(c) for c in self.cells)

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
        """Mirrors `FallBoard.Resolve`, wave for wave - including the glass.

        A wave takes everything that has reached white: motes, which **burst** and wash the four
        cells they touch with the drop's colour, and lenses, which **fire** beams along the axes.

        Two things about a shot and both are the mechanic. Its light is **white** - glass holds
        all three by the time it goes off - so every mote a beam lands on is completed and pops,
        whatever colour it was. And how far round it fires says where its own light came from: a
        lens charged the ordinary way fires **sideways**, which on a board with gravity is the
        only pair worth anything, and a lens **struck by another lens's beam** fires along all
        four axes. What keeps that from being a solvent is the price - a lens gains at most one
        channel per drop, so a shot costs three drops of three colours.

        The gains are accumulated per cell rather than latched, because a wave no longer carries
        one colour: a cell reached by a burst and by a beam takes both, and `|=` is what keeps
        the answer free of any reading order.
        """
        steps = []
        wave = 0

        while True:
            burst = [i for i, c in enumerate(self.cells) if c == ALL]
            fired = [i for i, c in enumerate(self.cells) if c == FULL]
            if not burst and not fired:
                break

            # Everything leaving this wave. Read once, before anything is applied, so nothing
            # here depends on which cell was scanned first.
            going = set(burst) | set(fired)

            washed, charged, beams = [], [], []
            gain = {}

            def reached(ni, light):
                """One cell the light got to. Charges glass, washes a mote, or does neither."""
                if ni in going:
                    return                      # gone by the time it arrives
                cell = self.cells[ni]
                if not cell:
                    return
                taken = light & ~cell
                if not taken:
                    return                      # holds it already: takes nothing
                first = ni not in gain
                gain[ni] = gain.get(ni, 0) | taken
                if not first:
                    return                      # already on a list
                (charged if is_lens(cell) else washed).append(ni)

            # ---- what each burst touches, in the drop's own colour
            for at in burst:
                x, y = at % self.w, at // self.w
                for dx, dy in NEIGHBOURS:
                    nx, ny = x + dx, y + dy
                    if not (0 <= nx < self.w and 0 <= ny < self.h):
                        continue
                    ni = ny * self.w + nx
                    if self.cells[ni]:
                        reached(ni, wash)

            # ---- and the beams out of every lens that filled up, in white
            for at in fired:
                x, y = at % self.w, at // self.w
                for dx, dy in (NEIGHBOURS if at in self.struck else SIDEWAYS):
                    cx, cy, travelled = x, y, 0
                    while True:
                        cx += dx
                        cy += dy
                        travelled += 1

                        if not (0 <= cx < self.w and 0 <= cy < self.h):
                            beams.append((at, dx, dy, travelled, -1))
                            break

                        ni = cy * self.w + cx
                        cell = self.cells[ni]

                        if not cell:
                            continue            # what a lens exists to cross
                        if ni in going:
                            continue            # going off this wave; gone when the light lands

                        beams.append((at, dx, dy, travelled, ni))
                        if is_lens(cell):
                            self.struck.add(ni)
                        reached(ni, ALL)
                        break

            for at in burst:
                self.cells[at] = 0
            for at in fired:
                self.cells[at] = 0
                self.struck.discard(at)

            washed_with = [gain[at] for at in washed]
            charged_with = [gain[at] for at in charged]
            for at in washed:
                self.cells[at] |= gain[at]
            for at in charged:
                self.cells[at] |= gain[at]

            moved = self._settle()
            wave += 1

            if record:
                steps.append(dict(burst=burst, fired=fired, washed=washed, charged=charged,
                                  washed_with=washed_with, charged_with=charged_with,
                                  beams=beams, wave=wave, moved=moved))
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
                    # The struck flag travels with the glass it belongs to: a lens filled by a
                    # beam fires on the next wave and the well settles in between, so a flag left
                    # behind would arm whatever fell into that cell and disarm the lens that
                    # earned it.
                    if at in self.struck:
                        self.struck.discard(at)
                        self.struck.add(to)
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

            # Glass counts, exactly as `FallResolution.Burst` counts it: both are cells the
            # well had to be rid of, and a greedy player ranking them differently from the
            # shipped solver is a mirror that measures a different board.
            burst = sum(len(step['burst']) + len(step['fired']) for step in result['steps'])
            enriches = board.enriches(colour, x)
            better = burst > best_burst or (burst == best_burst and enriches and not best_enriches)
            if not better:
                continue
            best, best_burst, best_enriches = x, burst, enriches

        if best < 0:
            return -1
        board.drop(colour, best)

    return MAX_DROPS if board.is_empty else -1


def blast(cells, width, height):
    """What the glass on this board is pointing at, as geometry rather than as play.

    For every lens, the two sideways directions it would fire in if it were full, counted as the
    ones that land on something. A lens whose beams both leave the well the moment they set off
    is invariant 5d's decoration: it validates, it is charged, it fires, and the board would play
    the same without it.

    Out of two rather than out of four, because a lens filled the ordinary way fires sideways.
    Counting the vertical pair flattered every board in the chapter: a well has gravity, so a
    lens rests on something and its downward beam always lands, on the cell holding it up, having
    crossed nothing.

    Returns (most, longest): the best lens's landing count out of two, and the longest single
    beam in cells that lands. A reading of the authored position rather than a proof - the well
    collapses under a chain and a lens fires from wherever it has fallen to - so it warns and
    never refuses.
    """
    most, longest = 0, 0

    for at, cell in enumerate(cells):
        if not is_lens(cell):
            continue

        lands = 0
        for dx, dy in SIDEWAYS:
            x, y = at % width, at // width
            travelled = 0
            while True:
                x += dx
                y += dy
                travelled += 1
                if not (0 <= x < width and 0 <= y < height):
                    break
                if not cells[y * width + x]:
                    continue
                lands += 1
                longest = max(longest, travelled)
                break
        most = max(most, lands)

    return most, longest


def survey(rows, motes, width=None, height=None):
    """Everything an author needs to judge a well by, from the two things they wrote."""
    cells, w, h = parse_rows(rows, width, height)
    deal = parse_deal(motes)

    par, ways, nodes, proved = search(cells, w, h, deal)
    well = Well(cells, w, h)

    return dict(width=w, height=h, cells=cells, deal=deal,
                par=par, ways=ways, nodes=nodes, proved=proved,
                greedy=greedy(cells, w, h, deal),
                aim=blast(cells, w, h)[0], reach=blast(cells, w, h)[1],
                motes=well.motes, headroom=well.headroom,
                lenses=well.lenses, glass=well.glass, wanted=well.wanted,
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

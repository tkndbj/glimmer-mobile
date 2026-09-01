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
#: A whorl: the bit above the lens, holding no channels at all. Mirrors `FallCell.Whorl`. Any
#: light opens one; on the next wave it draws the motes standing either side of it together and
#: mixes them into one. It is the only place in this mode where two *motes* are combined - every
#: other rule here adds a *colour* to a cell - and the only thing that moves a mote sideways.
WHORL = 16

#: A whorl that has caught the light and turns on the next wave. Never authored.
LIT = 32

LETTERS = {'R': R, 'G': G, 'B': B, 'Y': R | G, 'M': R | B, 'C': G | B, 'W': ALL,
           'O': LENS, 'r': LENS | R, 'g': LENS | G, 'b': LENS | B,
           'y': LENS | R | G, 'm': LENS | R | B, 'c': LENS | G | B, 'w': FULL,
           '@': WHORL}

#: The three digits a wick was authored with. Retired, and refused by name rather than read as
#: anything - a chapter file carrying one is content written for a build that no longer exists.
RETIRED_WICK = '123'
LETTER_OF = {v: k for k, v in LETTERS.items()}


def is_lens(cell):
    return bool(cell & LENS)


def is_whorl(cell):
    """A whorl - a mouth that any light opens and that draws the pair beside it together."""
    return bool(cell & WHORL)


def is_lit(cell):
    """A whorl that has caught the light and turns on the next wave."""
    return bool(cell & LIT)


def is_mote(cell):
    """A cell made of light: the only kind that can be enriched, burst, drawn in, or want.

    **Both other kinds are excluded and the second one had to be added.** This read "occupied and
    not glass", which answers true for a whorl - so a wash beside one would have poured colour
    into a cell that holds none, a drop landing on one would have been swallowed by it, and a
    whorl would have drawn in another whorl.
    """
    return cell != 0 and not (cell & (LENS | WHORL))


def takes(cell, colour):
    """Whether this cell takes a drop of this colour rather than letting the stack grow a row.

    One predicate for the three kinds, because the clause spelt out - ``(cell | colour) != cell``
    - is right for a mote, right for a lens and wrong for a whorl, which holds no channels at all
    and whose answer does not depend on the colour.

    **A drop opens an unlit whorl, whatever colour it is**, and that is what stops a well ever
    becoming unwinnable: a whorl is otherwise only reached by a chain, and a player who cleared
    every mote around one first would be stranded. That is the state the lens shipped with and
    had to have a valve added for; here it is the rule from the start.
    """
    if not cell:
        return False
    if is_whorl(cell):
        return not is_lit(cell)
    return (cell | colour) != cell


def charge(cell):
    """The channels a lens is holding. Nought for a mote and for bare ground."""
    return (cell & ALL) if is_lens(cell) else 0


def wants(cell):
    """The channels this cell still lacks before it goes off. Nought for bare ground.

    Both kinds want something now. A mote wants what it needs to reach white and burst; a lens
    wants what it needs to reach white and *fire*. That is the same sentence twice, which is the
    whole reason the rule needed nothing new taught: light fills a thing up and then it goes off.
    """
    if not cell or is_whorl(cell):
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


def letter(cell):
    """The letter this cell is authored with. Mirrors `FallCell.Letter`.

    An *open* whorl writes as an ordinary one: the flag is state rather than content, so a board
    caught mid-cascade still round-trips to the board somebody authored.
    """
    if not cell:
        return '.'
    if is_whorl(cell):
        return '@'
    return LETTER_OF[cell]


def written(cells, width):
    """The rows again, for a round trip."""
    return [''.join(letter(cells[y * width + x]) for x in range(width))
            for y in range(len(cells) // width)]


class Well(object):
    """One well being played on. Mirrors `FallBoard`."""

    __slots__ = ('w', 'h', 'cells', 'flooded', 'struck', 'fused', 'kindled')

    def __init__(self, cells, width, height, flooded=False, struck=None):
        self.w, self.h = width, height
        self.cells = list(cells)
        self.flooded = flooded
        #: Which lenses were struck by another lens's beam, and so fire along all four axes
        #: rather than sideways. It outlives the wave that sets it - a lens a beam lands on is
        #: filled in one wave and fires in the next, and the well settles in between - so it is
        #: carried by `_settle` and cleared when the glass leaves. Empty at rest.
        self.struck = set(struck) if struck else set()

        #: How many whorls have drawn in *two* motes, and how many of those merges reached
        #: white. Authoring readings rather than rules - see `_draw`. `kindled` is the strict
        #: one: two yellows drawn together make a tidier board, and a yellow and a blue make a
        #: burst the player arranged and could not have bought with any single drop.
        self.fused = 0
        self.kindled = 0

    def fork(self):
        fork = Well(self.cells, self.w, self.h, self.flooded, self.struck)
        fork.fused = self.fused
        fork.kindled = self.kindled
        return fork

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
        # A drop is taken by whatever is on top if it lacks the colour - a mote is enriched, a
        # lens is charged, and a whorl is opened whatever the colour. None of the three raises
        # the stack; the third always does.
        if top >= 0 and takes(self.cells[top * self.w + x], colour):
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

        One question with three answers - a mote lacking the colour is enriched, a lens lacking
        it is charged, a whorl is opened whatever the colour - and it exists because asking them
        separately is how a caller comes to ask only one of them. `enriches` is false for every
        charging drop, which is right for the question it asks and was catastrophic where it
        stood in for this one: the view used it to decide whether the falling widget was handed
        back, so a drop taken in by glass left the lens's own widget drawn on a board that no
        longer tracked it.
        """
        top = self.top_of(x)
        if top < 0:
            return False
        return takes(self.cells[top * self.w + x], colour)

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
    def whorls(self):
        return sum(1 for c in self.cells if is_whorl(c))

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

        # A whorl is *opened* by a drop rather than filled by it. It holds no channels at all
        # - `|=` here would quietly make a coloured whorl, which is a cell no rule in this file
        # has a name for and which the letters cannot even write down.
        if is_whorl(self.cells[index]):
            self.cells[index] |= LIT
        else:
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
            turning = [i for i, c in enumerate(self.cells) if is_lit(c)]
            if not burst and not fired and not turning:
                break

            # Everything leaving this wave. Read once, before anything is applied, so nothing
            # here depends on which cell was scanned first.
            going = set(burst) | set(fired) | set(turning)

            # And what each turning whorl draws in, settled *before* any light is handed out.
            # Both orderings matter. It is decided before the washes because a mote in motion has
            # to be invisible to them - it is not where the wave thinks it is by the time the wave
            # lands, so it takes nothing and stops no beam - and it is read off the board as it
            # stands, so a mote that is bursting this wave is never also drawn in.
            fuses = self._draw(turning, going)

            washed, charged, caught, beams = [], [], [], []
            gain = {}

            def reached(ni, light):
                """One cell the light got to. Charges glass, washes a mote, or does neither."""
                if ni in going:
                    return                      # gone by the time it arrives
                cell = self.cells[ni]
                if not cell:
                    return
                if is_whorl(cell):
                    # A whorl takes no channels - it holds none and never will. What light does
                    # to one is *open* it, and only once: a second arrival in the same wave finds
                    # it already open, which keeps the wave free of any reading order. It turns
                    # on the wave after this one, which is the wind-up the player needs in order
                    # to see which two motes are about to be taken.
                    if is_lit(cell):
                        return
                    self.cells[ni] = cell | LIT
                    caught.append(ni)
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

            # A whorl turns: what it drew in leaves the cells it stood in, and comes back as one
            # mote in the whorl's own. A whorl that drew in nothing closes and leaves bare ground,
            # which is what keeps it removable - and therefore what keeps a well holding nothing
            # but whorls winnable. Two motes in and one out, which is also why the loop still
            # terminates: no wave can leave the well as full as it found it.
            for fuse in fuses:
                for side in (fuse['left'], fuse['right']):
                    if side >= 0:
                        self.cells[side] = 0

                self.cells[fuse['at']] = fuse['into']
                self.struck.discard(fuse['at'])

                if fuse['left'] >= 0 and fuse['right'] >= 0:
                    self.fused += 1
                    if fuse['into'] == ALL:
                        self.kindled += 1

            washed_with = [gain[at] for at in washed]
            charged_with = [gain[at] for at in charged]
            for at in washed:
                self.cells[at] |= gain[at]
            for at in charged:
                self.cells[at] |= gain[at]

            moved = self._settle()
            wave += 1

            if record:
                steps.append(dict(burst=burst, fired=fired, fuses=fuses,
                                  caught=caught,
                                  washed=washed, charged=charged,
                                  washed_with=washed_with, charged_with=charged_with,
                                  beams=beams, wave=wave, moved=moved))
            else:
                steps.append(None)

        return steps

    def _draw(self, turning, going):
        """What every whorl turning this wave draws in, and what the pair becomes.

        **Two passes, and the split is the whole of what makes the result order-free.** Every
        claim is read off the board as it stands; only when all of them have been taken are the
        drawn motes marked as in motion. Marked as they were claimed, a mote with a turning whorl
        on each side would go to whichever of the two this loop reached first, which is a reading
        order in the one method the whole class is arranged to keep free of one. Marked
        afterwards, both whorls see it, both are refused it, and it stays where it is.
        """
        fuses = []

        for at in turning:
            left = self._claim(at, -1, going)
            right = self._claim(at, +1, going)

            into = 0
            if left >= 0:
                into |= self.cells[left]
            if right >= 0:
                into |= self.cells[right]

            fuses.append(dict(at=at, left=left, right=right, into=into))

        for fuse in fuses:
            for side in (fuse['left'], fuse['right']):
                if side >= 0:
                    going.add(side)

        return fuses

    def _claim(self, whorl, dx, going):
        """The mote one step `dx` of a turning whorl, if it may be drawn in, or -1.

        Three refusals, and each is a rule rather than a guard. A cell already leaving this wave
        is not drawn in - the light got to it first. Anything that is not a mote is not drawn in:
        **a whorl draws light and nothing else**, so glass stays where it stands and two whorls
        never eat each other. And a mote with a turning whorl on *each* side is let go by both,
        which is the only symmetric answer available.
        """
        mx = whorl % self.w + dx
        if not (0 <= mx < self.w):
            return -1

        at = whorl + dx
        if at in going or not is_mote(self.cells[at]):
            return -1

        bx = mx + dx
        if 0 <= bx < self.w:
            beyond = at + dx
            if is_whorl(self.cells[beyond]) and is_lit(self.cells[beyond]):
                return -1

        return at

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

            # Everything the drop was rid of, exactly as `FallResolution.Burst` counts it: a
            # greedy player ranking them differently from the shipped solver is a mirror that
            # measures a different board. A merge counts as the *one* cell it really frees -
            # two motes go in and one comes out - so a whorl that fuses a pair is worth two and
            # one that closes on nothing is worth one.
            burst = 0
            for step in result['steps']:
                burst += len(step['burst']) + len(step['fired'])
                for fuse in step['fuses']:
                    burst += (1 if fuse['left'] >= 0 else 0) \
                           + (1 if fuse['right'] >= 0 else 0) \
                           + (0 if fuse['into'] else 1)
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


def merges(cells, width, height, deal, par):
    """What the whorls actually did along a shortest solution, and the line it took.

    **This is the measurement two withdrawn mechanics never had, and it is why they shipped.**
    Every other reading here - solvable, par, ways, greedy, aim - is passed just as happily by an
    object that decorates the board as by one that decides it. This asks the only question that
    separates them: did the thing actually *do* something a player could not have had without it.

    Returns (fused, kindled, line). `fused` counts whorls that drew in two motes; `kindled` counts
    those whose union reached white, which is the strict reading and the one a board is authored
    against. Two yellows drawn together make a yellow, which is a tidier board and nothing else; a
    yellow and a blue make a burst the player arranged and could not have bought with any single
    drop. Nought kindled on a board carrying whorls is the answer that condemns it.
    """
    if par < 1:
        return 0, 0, []

    frontier = [(Well(cells, width, height), [])]
    for depth in range(par):
        colour = deal[depth % len(deal)]
        nxt = []
        for board, path in frontier:
            for x in range(width):
                if not board.can_drop(colour, x):
                    continue
                child = board.fork()
                child.drop(colour, x)
                if child.flooded:
                    continue
                if child.is_empty:
                    return child.fused, child.kindled, path + [(colour, x)]
                nxt.append((child, path + [(colour, x)]))
        frontier = nxt
        if not frontier:
            break

    return 0, 0, []


def best_merges(cells, width, height, deal, par):
    """The same, over *every* shortest solution, taking the most a par line ever makes of them.

    `merges` reports the first winning line the search happens to reach, which is an arbitrary one
    among however many `ways` counts - so a board whose whorls are load-bearing can report nought
    simply because one particular ordering did without them. The reading that judges a board has
    to be what its *best* shortest play does, or an author is tuning against a coin toss.

    Bounded by the same par depth, so it costs what one extra breadth-first sweep costs.
    """
    if par < 1:
        return 0, 0

    frontier = [Well(cells, width, height)]
    best = (0, 0)

    for depth in range(par):
        colour = deal[depth % len(deal)]
        nxt = []
        for board in frontier:
            for x in range(width):
                if not board.can_drop(colour, x):
                    continue
                child = board.fork()
                child.drop(colour, x)
                if child.flooded:
                    continue
                if child.is_empty:
                    if (child.kindled, child.fused) > (best[1], best[0]):
                        best = (child.fused, child.kindled)
                    continue
                nxt.append(child)
        frontier = nxt
        if not frontier:
            break

    return best


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
                lenses=well.lenses, glass=well.glass, whorls=well.whorls,
                fused=best_merges(cells, w, h, deal, par)[0],
                kindled=best_merges(cells, w, h, deal, par)[1],
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

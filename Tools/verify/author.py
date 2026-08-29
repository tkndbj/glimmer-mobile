"""Board authoring aid: describe a glade as cells and edges, get back JSON rows.

Authoring a board by typing arm masks is how a level ends up with an arm pointing at
nothing, and it gets worse with every mechanic - a briar draws four arms and conducts
two, and every conduit on a taproot has to agree on one number of turns. Neither is
visible in a grid of tokens.

So you say which cells exist and which of them are joined, and this derives the masks,
proves the same rules `content.py` proves, prints the board as a picture, and prints the
rows to paste into a chapter file. It is deliberately not a generator: the design is
still yours, this only refuses to let you write one that cannot be finished.

    from author import Board
    b = Board(6, 7)
    b.source(0, 0, 'R'); b.pipe(1, 0); b.lamp(2, 0, 'A')
    b.link((0, 0), (1, 0), (2, 0))
    b.report("my glade")

Mirrors LevelGridParser / Puzzle / PuzzleFactory. It is an aid, not a gate:
`Glimmer Grove > Validate Content` and the build gate remain the authority.
"""
from collections import deque

N, E, S, W = 1, 2, 4, 8
BITS = [N, E, S, W]
STEP = [(0, -1), (1, 0), (0, 1), (-1, 0)]
OPP = [2, 3, 0, 1]
COLOURS = {'R': 1, 'G': 2, 'B': 4, 'Y': 3, 'M': 5, 'C': 6, 'W': 7, 'A': 0}
LETTER = {1: 'R', 2: 'G', 4: 'B', 3: 'Y', 5: 'M', 6: 'C', 7: 'W', 0: 'A'}


def rotl(mask, turns):
    turns &= 3
    out = 0
    for i in range(4):
        if mask & (1 << i):
            out |= 1 << ((i + turns) & 3)
    return out


def alike(solved, cross, turns, gate=0):
    """Whether a tile turned this far from its solution is indistinguishable from it.

    Mirrors Puzzle.Alike. Both four-armed tiles wear all four arms at every angle, so the
    bare mask comparison this replaces calls every one of them solved - which derives a par
    short by one per twisted crossing and a board nobody can finish.

    A briar's `gate` is asked first and is the stricter reading: a turn that merely swapped
    a crossing's two interchangeable labels has moved a briar's thorns onto the way the
    light was using.
    """
    if rotl(solved, turns) != solved:
        return False
    if gate:
        return rotl(gate, turns) == gate
    if not cross:
        return True
    strand = rotl(cross, turns)
    return strand == cross or strand == (solved & ~cross & 15)


def owed(solved, rot, cross=0, gate=0):
    for k in range(4):
        if alike(solved, cross, (rot + k) & 3, gate):
            return k
    return 0


class Board:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.cells = {}          # (x,y) -> dict
        self.edges = set()       # frozenset{(x,y),(x2,y2)}

    # ----------------------------------------------------------- authoring
    def pipe(self, x, y, rot=0, locked=False, fragile=0, link=None):
        self.cells[(x, y)] = dict(kind='pipe', colour=0, rot=rot, locked=locked,
                                  fragile=fragile, link=link, cross=0, gate=0)

    def source(self, x, y, colour, rot=0):
        self.cells[(x, y)] = dict(kind='source', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None, cross=0, gate=0)

    def lamp(self, x, y, colour='A', rot=0):
        self.cells[(x, y)] = dict(kind='lamp', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None, cross=0, gate=0)

    def cross(self, x, y, strand, rot=0, locked=False, fragile=0, link=None):
        """A crossing: four arms in two pairs that pass through one another.

        `strand` names the two arms of one pair, e.g. 'NS' or 'NE'; the other pair is
        whatever is left. Which of the two you write down does not matter - they are
        interchangeable labels, exactly as in Puzzle.Alike.
        """
        mask = 0
        for ch in strand:
            mask |= {'N': N, 'E': E, 'S': S, 'W': W}[ch]
        self.cells[(x, y)] = dict(kind='cross', colour=0, rot=rot, locked=locked,
                                  fragile=fragile, link=link, cross=mask, gate=0)

    def briar(self, x, y, open_arms, rot=0, locked=False, fragile=0, link=None):
        """A briar: four arms, of which only the named pair is open.

        `open_arms` names the two arms light may pass along, e.g. 'NS' or 'NE'; the thorns
        close the other two, which still have to be drawn and still have to mate their
        neighbours. That is the whole mechanic - all four neighbours mate it at every angle,
        so nothing about the pipe-fitting can settle a briar and only colour or the dark can.

        Unlike `cross`, which pair you name is the tile: a crossing's strands are
        interchangeable labels and a briar's are a way through and a wall.

        **The four edges are joined here rather than by the caller**, which is the one place
        this module wires anything on its own. A briar with three arms is not a briar, and
        the arm an author forgets is always the same one: a thorned way carries no light, so
        nothing about the solution notices it missing. It also cannot stand on the border,
        because one of its four arms would have nowhere to point.
        """
        assert 0 < x < self.w - 1 and 0 < y < self.h - 1,             f"a briar needs four neighbours, so it cannot stand on the border at {(x, y)}"
        mask = 0
        for ch in open_arms:
            mask |= {'N': N, 'E': E, 'S': S, 'W': W}[ch]
        self.cells[(x, y)] = dict(kind='briar', colour=0, rot=rot, locked=locked,
                                  fragile=fragile, link=link, cross=0, gate=mask)
        for d in range(4):
            self.link((x, y), (x + STEP[d][0], y + STEP[d][1]))

    def fill(self, x0, y0, w, h, skip=()):
        """Make every cell of a rectangle a conduit, leaving anything already placed.

        Open ground is what makes a board read at a glance: an arm has almost nowhere to
        go when three of its four neighbours are not there, so a sparse glade is a
        connect-the-dots however many mechanics are painted on it. Filling first and
        wiring afterwards is the way round that keeps that from happening by accident.
        """
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                if (x, y) not in self.cells and (x, y) not in skip:
                    self.pipe(x, y)
        return self

    def path(self, *pts):
        """Join a run of cells and return it, so a board reads as the runs it is made of."""
        self.link(*pts)
        return list(pts)

    def period(self, p):
        """After how many quarter turns this tile reads as itself again: 1, 2 or 4."""
        c = self.cells[p]
        for k in (1, 2):
            if alike(self.mask(p), c['cross'], k, c['gate']):
                return k
        return 4

    def root(self, rune, turns, *pts):
        """Bind these conduits to one taproot and set every rotation so one tap of
        `turns` solves the lot.

        A root whose members cannot agree is a level nobody can finish that looks
        perfectly authored, so the safe rotations are derived rather than typed: each
        member is turned back by `turns` modulo its own period, which is exactly the
        condition Puzzle.TurnsOwed searches for.
        """
        for p in pts:
            c = self.cells[p]
            c['link'] = rune
            c['rot'] = (-turns) % self.period(p)

    def link(self, a, b, *rest):
        """Join a run of cells with the same edge chain, left to right."""
        pts = [a, b] + list(rest)
        for i in range(len(pts) - 1):
            p, q = pts[i], pts[i + 1]
            assert abs(p[0] - q[0]) + abs(p[1] - q[1]) == 1, f"{p}-{q} not adjacent"
            self.edges.add(frozenset((p, q)))

    # ------------------------------------------------------------- derive
    def mask(self, p):
        m = 0
        for d in range(4):
            q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
            if frozenset((p, q)) in self.edges:
                m |= BITS[d]
        return m

    def token(self, p):
        c = self.cells.get(p)
        if not c:
            return '.'
        head = {'pipe': '-', 'source': '*', 'lamp': '@', 'cross': '=',
                'briar': '%'}[c['kind']]
        m = self.mask(p)
        named = c['cross'] or c['gate']
        if named:
            first = ''.join(ch for ch, b in zip('NESW', BITS) if named & b)
            second = ''.join(ch for ch, b in zip('NESW', BITS) if m & ~named & b)
            tok = head + first + '+' + second
        else:
            tok = head + ''.join(ch for ch, b in zip('NESW', BITS) if m & b)
        if c['kind'] in ('source', 'lamp'):
            tok += '#' + LETTER[c['colour']]
        tok += '/' + str(c['rot'])
        if c['locked']:
            tok += '!'
        if c['fragile']:
            tok += '~' + str(c['fragile'])
        if c['link']:
            tok += '&' + c['link']
        return tok

    def rows(self):
        return [' '.join(self.token((x, y)) for x in range(self.w)) for y in range(self.h)]

    # ------------------------------------------------------------- checks
    def strand_at(self, p, d, rots=None):
        """Which of a cell's strands the arm in direction d belongs to. 0 off a crossing."""
        c = self.cells[p]
        if not c['cross']:
            return 0
        turned = rotl(c['cross'], (rots or {}).get(p, 0))
        return 0 if turned & BITS[d] else 1

    def strands(self, p):
        return 2 if self.cells[p]['cross'] else 1

    def live(self, p, rots=None):
        """The arms of a cell that actually carry light. Mirrors Puzzle.Live.

        Every tile but a briar conducts along every arm it draws; a briar draws four and
        conducts two. So the light walks this and the drawing walks `mask` - the one place
        where "there is an arm here" and "light may go this way" are different questions.
        """
        c = self.cells[p]
        return rotl(c['gate'] or self.mask(p), (rots or {}).get(p, 0))

    def solve_state(self, rots=None):
        """Networks and colours at the given rotations (default: solved).

        Keyed by (cell, strand) rather than by cell: an ordinary tile has one strand and a
        crossing has two that never meet. Mirrors Puzzle.Evaluate.
        """
        comp, colour = {}, {}
        g = 0
        for p in self.cells:
            for st in range(self.strands(p)):
                if (p, st) in comp:
                    continue
                col, q = 0, deque([(p, st)])
                comp[(p, st)] = g
                while q:
                    a, sa = q.popleft()
                    ca = self.cells[a]
                    if ca['kind'] == 'source':
                        col |= ca['colour']
                    ma = self.live(a, rots)
                    for d in range(4):
                        if not ma & BITS[d]:
                            continue
                        if self.strand_at(a, d, rots) != sa:
                            continue
                        b = (a[0] + STEP[d][0], a[1] + STEP[d][1])
                        if b not in self.cells:
                            continue
                        if not self.live(b, rots) & BITS[OPP[d]]:
                            continue
                        into = (b, self.strand_at(b, OPP[d], rots))
                        if into in comp:
                            continue
                        comp[into] = g
                        q.append(into)
                colour[g] = col
                g += 1
        return comp, colour

    def energy(self, p, comp, colour):
        """Every colour reaching a cell, across all of its strands."""
        mix = 0
        for st in range(self.strands(p)):
            mix |= colour[comp[(p, st)]]
        return mix

    def check(self):
        errs, warns = [], []

        for p, c in self.cells.items():
            if not (0 <= p[0] < self.w and 0 <= p[1] < self.h):
                errs.append(f"{p} is off the board")
            if self.mask(p) == 0:
                errs.append(f"{p} has no arms")

        for e in self.edges:
            for p in e:
                if p not in self.cells:
                    errs.append(f"edge {tuple(e)} touches empty {p}")

        # both four-armed tiles carry four arms in two disjoint pairs of two, or they are
        # not one of them
        for p, c in self.cells.items():
            named = c['cross'] or c['gate']
            if c['kind'] not in ('cross', 'briar'):
                continue
            m = self.mask(p)
            other = m & ~named & 15
            if bin(named).count('1') != 2 or bin(other).count('1') != 2 or (m & named) != named:
                errs.append(f"{c['kind']} {p} needs four arms in two pairs of two, "
                            f"got {bin(m).count('1')} with {bin(named).count('1')} named")

        comp, colour = self.solve_state()
        for p, c in self.cells.items():
            have = self.energy(p, comp, colour)
            if c['kind'] == 'lamp':
                want = c['colour']
                ok = (have != 0) if want == 0 else (have == want)
                if not ok:
                    errs.append(f"lamp {p} wants {LETTER[want]} but the solution feeds it "
                                f"{LETTER[have] if have else 'nothing'}")
            if c['kind'] == 'cross':
                if comp[(p, 0)] == comp[(p, 1)]:
                    warns.append(f"crossing {p} has both strands in one network, so it "
                                 "crosses nothing")
                elif not have:
                    warns.append(f"crossing {p} carries no light at all in the solution")

        # A four-armed tile mates every neighbour at every angle, so nothing about the
        # pipe-fitting settles it: turning it one step has to un-finish the glade or it is
        # decoration with a par charged for it. See `decides` - this is the rule, and the
        # loop above keeps the two crossing-specific readings because they diagnose a
        # different fault and a better message is worth two warnings on one tile.
        if self.wins(comp, colour):
            for p, c in self.cells.items():
                if c['kind'] not in ('cross', 'briar') or c['locked']:
                    continue
                if alike(self.mask(p), c['cross'], 1, c['gate']):
                    continue                     # a straight crossing is architecture
                if self.decides(p):
                    continue
                why = ("every way it has leads back into one network"
                       if c['kind'] == 'briar' and not self.separates(p, comp)
                       else "the two things it holds apart are answering the same colour")
                warns.append(f"turning the {c['kind']} at {p} one step from its solution "
                             f"still finishes the glade, so nothing on this board settles "
                             f"it - {why}")

        # bound groups: one common turn count, no rooted member, at least two members
        groups = {}
        for p, c in self.cells.items():
            if c['link']:
                groups.setdefault(c['link'], []).append(p)
        for rune, members in groups.items():
            if len(members) < 2:
                errs.append(f"bound rune '{rune}' has only one member {members}")
            for p in members:
                if self.cells[p]['kind'] not in ('pipe', 'cross', 'briar'):
                    errs.append(f"bound {p} is not a conduit")
                if self.cells[p]['locked']:
                    errs.append(f"bound {p} is also rooted")
            common = [k for k in range(4)
                      if all(alike(self.mask(p), self.cells[p]['cross'],
                                   (self.cells[p]['rot'] + k) & 3, self.cells[p]['gate'])
                             for p in members)]
            if not common:
                errs.append(f"bound rune '{rune}' has no shared turn count: "
                            f"{[(p, self.group_turns(p)) for p in members]}")

        # A rooted tile must already read as solved: it can never be turned, and every
        # check above ran against the board with every rotation at zero, so one authored
        # off its solution means what was proved is not what ships. Mirrors
        # LevelValidator.CheckRootedTiles.
        for p, c in self.cells.items():
            if c['locked'] and not alike(self.mask(p), c['cross'], c['rot'], c['gate']):
                errs.append(f"rooted {p} starts "
                            f"{owed(self.mask(p), c['rot'], c['cross'], c['gate'])} "
                            "turn(s) from its solution and can never be turned")

        # fragile conduits must survive their own group's turn count
        for p, c in self.cells.items():
            if not c['fragile']:
                continue
            k = self.group_turns(p)
            if k > c['fragile']:
                errs.append(f"fragile {p} needs {k} turns but survives {c['fragile']}")

        return errs, warns

    def separates(self, p, comp):
        """Whether taking this briar's thorns off would join anything to anything.

        No longer the rule - `decides` is - but kept as the *reason* attached to its warning,
        because it is the commonest cause and the most actionable one. Only the thorned ways
        are asked about (the open pair is the network the tile is already in, so it can never
        disagree with itself) and the way has to be open on the *other* side too, or lifting
        these thorns would still join nothing, which is what two briars back to back are.
        Mirrors LevelValidator.ThornsSeparate.
        """
        c = self.cells[p]
        mine = comp[(p, 0)]
        for d in range(4):
            if c['gate'] & BITS[d] or not self.mask(p) & BITS[d]:
                continue
            q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
            if q not in self.cells:
                continue
            if not self.live(q) & BITS[OPP[d]]:
                continue
            if comp[(q, self.strand_at(q, OPP[d]))] != mine:
                return True
        return False

    def wins(self, comp, colour):
        """Whether every critter is correctly lit in this arrangement. Mirrors Puzzle.Won."""
        any_lamp = False
        for p, c in self.cells.items():
            if c['kind'] != 'lamp':
                continue
            any_lamp = True
            have, want = self.energy(p, comp, colour), c['colour']
            if not ((have != 0) if want == 0 else (have == want)):
                return False
        return any_lamp

    def decides(self, p):
        """Whether turning this tile one step off its solution un-finishes the glade.

        Mirrors LevelValidator.CheckDecidableTiles, and it is the rule rather than a proxy
        for it. A crossing and a briar wear all four arms at every angle, so every neighbour
        mates them however they are turned and nothing about the pipe-fitting says which way
        either one goes - which is what makes them the two tiles worth authoring with
        (invariant 5d) and exactly how they fail.

        Asking the consequence is what fixed the topology check this replaces, which was
        wrong in both directions: it missed a tile separating two networks of *compatible*
        colour, and it fired on a briar whose open pair is the only way into a pocket
        carrying a heart of its own (invariant 5f).
        """
        return not self.wins(*self.solve_state({p: 1}))

    def group_turns(self, p):
        c = self.cells[p]
        if not c['link']:
            return owed(self.mask(p), c['rot'], c['cross'], c['gate'])
        members = [q for q, d in self.cells.items() if d['link'] == c['link']]
        for k in range(4):
            if all(alike(self.mask(q), self.cells[q]['cross'],
                         (self.cells[q]['rot'] + k) & 3, self.cells[q]['gate'])
                   for q in members):
                return k
        return 0

    def par(self):
        total, counted = 0, set()
        for p, c in self.cells.items():
            if c['locked']:
                continue
            if alike(self.mask(p), c['cross'], 1, c['gate']):   # inert at every angle
                continue
            if c['link']:
                if c['link'] in counted:
                    continue
                counted.add(c['link'])
            total += self.group_turns(p)
        return total

    def astray(self):
        """How much of this board the player does *not* find already done.

        Two counts of the board *as it is dealt*, against the same predicate `par` charges
        on: critters already correctly lit, and turnable conduits already sitting on their
        solution. A glade opening with a third of its wiring right and three of its critters
        awake is somebody else's half-finished work, and every board here was authored
        without anybody able to say by how much - which is how the worst of them shipped at
        23 conduits of 40 and four critters awake.

        `free` is the same set `par` counts: locked tiles are nobody's decision, and a tile
        that is inert at every angle is already right at every angle, so counting either
        would report a board's architecture as its head start.
        """
        rots = {p: c['rot'] for p, c in self.cells.items()}
        comp, colour = self.solve_state(rots)

        lit = 0
        for p, c in self.cells.items():
            if c['kind'] != 'lamp':
                continue
            have, want = self.energy(p, comp, colour), c['colour']
            if (have != 0) if want == 0 else (have == want):
                lit += 1

        done = free = 0
        for p, c in self.cells.items():
            if c['locked'] or alike(self.mask(p), c['cross'], 1, c['gate']):
                continue
            free += 1
            if owed(self.mask(p), c['rot'], c['cross'], c['gate']) == 0:
                done += 1
        return lit, done, free

    # ------------------------------------------------------------- authoring aids
    def spin(self, seed=1, bias=70, skip=()):
        """Give every free tile a start rotation, and dial par with `bias`.

        Rotations are arbitrary by design - what they must not be is *accidental*, so
        they are derived from each tile's own coordinates and a seed rather than typed
        one board at a time. `bias` leans the spread: positive avoids leaving a tile
        already solved, negative prefers it. That is the only knob par has, and par is
        the clock and the move budget, so it is worth being able to aim - see `fit`.

        Bound and rooted tiles are left alone. A root's rotations come from `root()`,
        and a rooted tile must already read as solved or the board the validator proves
        is not the board that ships (LevelValidator.CheckRootedTiles).
        """
        import hashlib
        for p in sorted(self.cells):
            c = self.cells[p]
            if c['link'] or c['locked'] or p in skip:
                continue
            period = self.period(p)
            if period == 1:
                continue
            h = int(hashlib.md5(f"{seed}:{p[0]}:{p[1]}".encode()).hexdigest(), 16)
            turns = h % period
            if bias >= 0:
                if turns == 0 and (h >> 9) % 100 < bias:
                    turns = 1 + (h >> 17) % (period - 1)
            elif turns != 0 and (h >> 9) % 100 < -bias:
                turns = 0
            c['rot'] = (-turns) % period

    def owe(self, p, turns):
        """Pin one tile's owed turns. For brittle conduits, which must survive theirs."""
        self.cells[p]['rot'] = (-turns) % self.period(p)

    def hazards(self):
        """Every place on this board where a wrong turn actually costs something.

        A board can be *about* keeping two lights apart and still have nowhere the
        player can get it wrong - the networks simply never come within a turn of each
        other, and the glade is a rotation exercise with a theme painted on. That is a
        property of the geometry, so it can be counted rather than argued about, and it
        is worth counting: the first cut of one Amberwood glade scored zero and read
        perfectly.

        Two shapes, because there are two ways to join what should stay apart:

        * two neighbours in different networks that some reachable pair of rotations
          would mate;
        * a twisted crossing carrying two networks, since turning one swaps which arm
          belongs to which strand and no rotation of it does otherwise. A *straight*
          crossing cannot - its two strands are interchangeable labels, which is exactly
          what `alike` says - so it is architecture and never a hazard.
        """
        comp, colour = self.solve_state()

        def why(ga, gb):
            a, b = colour[ga], colour[gb]
            if a and b and a != b:
                return f'{LETTER[a]} meets {LETTER[b]}'
            return 'blends nothing'

        out = []
        for p, c in self.cells.items():
            for d in range(4):
                q = (p[0] + STEP[d][0], p[1] + STEP[d][1])
                if q not in self.cells or q < p:
                    continue
                if any(comp[(p, sp)] == comp[(q, sq)]
                       for sp in range(self.strands(p)) for sq in range(self.strands(q))):
                    continue
                turns_p = range(1) if c['locked'] else range(4)
                turns_q = range(1) if self.cells[q]['locked'] else range(4)
                # the live mask, not the drawn one: a briar draws an arm down every way it
                # has and carries light along only two of them
                if not any(rotl(c['gate'] or self.mask(p), k) & BITS[d] for k in turns_p):
                    continue
                if not any(rotl(self.cells[q]['gate'] or self.mask(q), k) & BITS[OPP[d]]
                           for k in turns_q):
                    continue
                out.append((p, q, why(comp[(p, 0)], comp[(q, 0)])))

            if (c['kind'] == 'cross' and not c['locked']
                    and not alike(self.mask(p), c['cross'], 1, 0)
                    and comp[(p, 0)] != comp[(p, 1)]):
                out.append((p, p, why(comp[(p, 0)], comp[(p, 1)]) + ' (crossing)'))
        return out

    def reading(self):
        """What this board asks of a player, from `difficulty.py`.

        Hazards count the places a wrong turn *could* cost something. That turned out to
        be the wrong question and it is worth saying why, because a whole chapter was
        authored to it: a rotation that mates two networks usually leaves an arm dangling
        somewhere else, so it is not an arrangement a player ever plausibly reaches. What
        matters is how much of the board they can place by looking at it, and whether any
        mechanic ever rejects an arrangement the arms allow.

        Imported here rather than at the top: `difficulty` reads boards through this
        module, so the two would not both load.
        """
        import difficulty
        return difficulty.Reading(self).report()

    # -------------------------------------------------------------- print
    def picture(self, rots=None):
        """Three text rows per board row, so the wiring is readable."""
        out = []
        for y in range(self.h):
            top, mid, bot = '', '', ''
            for x in range(self.w):
                p = (x, y)
                c = self.cells.get(p)
                if not c:
                    top += '    '; mid += ' .  '; bot += '    '; continue
                m = rotl(self.mask(p), (rots or {}).get(p, 0))
                shut = m & ~self.live(p, rots) & 15
                glyph = {'pipe': '+', 'source': '*', 'lamp': 'O',
                         'cross': ')', 'briar': '%'}[c['kind']]
                if c['link']:
                    glyph = c['link'].lower()
                if c['locked']:
                    glyph = glyph.upper() if c['kind'] == 'pipe' else glyph
                    glyph = '#' if c['kind'] == 'pipe' else glyph
                top += (' : ' if shut & N else ' | ') if m & N else '   '
                top += ' '
                mid += ('=' if shut & W else '-' if m & W else ' ') + glyph + \
                       ('=' if shut & E else '-' if m & E else ' ') + ' '
                bot += (' : ' if shut & S else ' | ') if m & S else '   '
                bot += ' '
            out += [top, mid, bot]
        return '\n'.join(out)

    def report(self, name, deep=True):
        errs, warns = self.check()
        print(f"=== {name}  {self.w}x{self.h}  par={self.par()} "
              f"gold={-(-self.par()*135//100)} silver={-(-self.par()*200//100)} "
              f"clock={self.par()*2}s  hazards={len(self.hazards())}")
        if deep and not errs:
            r = self.reading()
            print(f"    glance {len(r['glance'])}/{r['tiles']}  arms {r['solutions']}"
                  f"{'+' if r['capped'] else ''}  wins {r['wins']}  open {len(r['open'])}  "
                  f"decided {len(r['decided'])}  rejected by colour {r['colour_only']}")
        print(self.picture())
        for r in self.rows():
            print(f'        "{r}",')
        for w in warns:
            print("WARN ", w)
        for e in errs:
            print("ERROR", e)
        if not errs:
            print("ok")
        print()
        return not errs


def fit(make, target, seeds=range(1, 60), biases=range(-90, 100, 10)):
    """The board whose par lands nearest `target`, dealt as scrambled as that par allows.

    `make` takes (seed, bias) and returns a finished Board. Par decides the move budget and
    both star lines, so a chapter's ramp is a set of numbers somebody chose - this is how
    they get chosen rather than discovered. Boards that do not check are skipped, so a fit
    can never hand back one that is unwinnable.

    **Par cannot be the only thing ranked, and for a long time it was.** Hundreds of
    (seed, bias) pairs hit any given par, and this walked `bias` from -90 upward and
    returned the first - so it systematically handed back the board dealt *most* nearly
    finished, because a negative bias is `spin`'s instruction to prefer leaving a tile
    sitting on its solution. Reported from play as glades that "start half done", and it
    was: of the forty shipped, thirty-four opened with a critter already awake or better
    than a third of their conduits already right, the worst at 23 of 40 and four critters
    lit. Nothing could see it - every one of those boards is solvable, correctly par'd and
    passes every gate there is, because "how much of this is already done" was a question
    nothing asked. Ranking `astray` behind the par distance costs nothing, is still
    deterministic, and cleaned twenty-one of the forty with no change to their par at all.

    What it cannot do is scramble a board its target forbids: par *is* the count of owed
    turns, so a glade dealt with three quarters of its tiles wrong has a par near three
    quarters of its tile count, and no seed can give it a shorter one. Where a shipped
    target was too low to carry a scrambled board its target was raised, which is the other
    half of the same fix and the reason several chapter ramps moved.
    """
    best = None
    for bias in biases:
        for seed in seeds:
            board = make(seed, bias)
            if board.check()[0]:
                continue
            gap = abs(board.par() - target)
            if best is not None and gap > best[0][0]:
                continue                      # cannot win on par, so never pay for the reading
            lit, done, _ = board.astray()
            rank = (gap, lit, done)
            if best is None or rank < best[0]:
                best = (rank, seed, bias, board)
    if best is None:
        raise ValueError("no (seed, bias) produced a board that checks")
    return best[1], best[2], best[3]

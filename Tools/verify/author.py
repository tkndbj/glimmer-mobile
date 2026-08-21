"""Board authoring aid: describe a glade as cells and edges, get back JSON rows.

Authoring a board by typing arm masks is how a level ends up with an arm pointing at
nothing, and it gets worse with every mechanic - a duskcap has to be its own island of
dark, and every conduit on a taproot has to agree on one number of turns. Neither is
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


def alike(solved, cross, turns):
    """Whether a tile turned this far from its solution is indistinguishable from it.

    Mirrors Puzzle.Alike. A crossing wears all four arms at every angle, so the bare
    mask comparison this replaces calls every one of them solved - which derives a par
    short by one per twisted crossing and a board nobody can finish.
    """
    if rotl(solved, turns) != solved:
        return False
    if not cross:
        return True
    strand = rotl(cross, turns)
    return strand == cross or strand == (solved & ~cross & 15)


def owed(solved, rot, cross=0):
    for k in range(4):
        if alike(solved, cross, (rot + k) & 3):
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
                                  fragile=fragile, link=link, cross=0)

    def source(self, x, y, colour, rot=0):
        self.cells[(x, y)] = dict(kind='source', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None, cross=0)

    def lamp(self, x, y, colour='A', rot=0):
        self.cells[(x, y)] = dict(kind='lamp', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None, cross=0)

    def duskcap(self, x, y, rot=0, locked=False):
        self.cells[(x, y)] = dict(kind='duskcap', colour=0, rot=rot,
                                  locked=locked, fragile=0, link=None, cross=0)

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
                                  fragile=fragile, link=link, cross=mask)

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
        for k in (1, 2):
            if alike(self.mask(p), self.cells[p]['cross'], k):
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
        head = {'pipe': '-', 'source': '*', 'lamp': '@', 'duskcap': 'x', 'cross': '='}[c['kind']]
        m = self.mask(p)
        if c['kind'] == 'cross':
            first = ''.join(ch for ch, b in zip('NESW', BITS) if c['cross'] & b)
            second = ''.join(ch for ch, b in zip('NESW', BITS) if m & ~c['cross'] & b)
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
                    ma = rotl(self.mask(a), (rots or {}).get(a, 0))
                    for d in range(4):
                        if not ma & BITS[d]:
                            continue
                        if self.strand_at(a, d, rots) != sa:
                            continue
                        b = (a[0] + STEP[d][0], a[1] + STEP[d][1])
                        if b not in self.cells:
                            continue
                        mb = rotl(self.mask(b), (rots or {}).get(b, 0))
                        if not mb & BITS[OPP[d]]:
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

        # a crossing carries four arms in two disjoint pairs of two, or it is not one
        for p, c in self.cells.items():
            if c['kind'] != 'cross':
                continue
            m = self.mask(p)
            other = m & ~c['cross'] & 15
            if bin(c['cross']).count('1') != 2 or bin(other).count('1') != 2 or (m & c['cross']) != c['cross']:
                errs.append(f"crossing {p} needs four arms in two pairs of two, "
                            f"got {bin(m).count('1')} with {bin(c['cross']).count('1')} named")

        comp, colour = self.solve_state()
        for p, c in self.cells.items():
            have = self.energy(p, comp, colour)
            if c['kind'] == 'lamp':
                want = c['colour']
                ok = (have != 0) if want == 0 else (have == want)
                if not ok:
                    errs.append(f"lamp {p} wants {LETTER[want]} but the solution feeds it "
                                f"{LETTER[have] if have else 'nothing'}")
            if c['kind'] == 'duskcap' and have:
                errs.append(f"duskcap {p} is lit by the authored solution ({LETTER[have]})")
            if c['kind'] == 'cross':
                if comp[(p, 0)] == comp[(p, 1)]:
                    warns.append(f"crossing {p} has both strands in one network, so it "
                                 "crosses nothing")
                elif not have:
                    warns.append(f"crossing {p} carries no light at all in the solution")

        # bound groups: one common turn count, no rooted member, at least two members
        groups = {}
        for p, c in self.cells.items():
            if c['link']:
                groups.setdefault(c['link'], []).append(p)
        for rune, members in groups.items():
            if len(members) < 2:
                errs.append(f"bound rune '{rune}' has only one member {members}")
            for p in members:
                if self.cells[p]['kind'] not in ('pipe', 'cross'):
                    errs.append(f"bound {p} is not a conduit")
                if self.cells[p]['locked']:
                    errs.append(f"bound {p} is also rooted")
            common = [k for k in range(4)
                      if all(alike(self.mask(p), self.cells[p]['cross'],
                                   (self.cells[p]['rot'] + k) & 3)
                             for p in members)]
            if not common:
                errs.append(f"bound rune '{rune}' has no shared turn count: "
                            f"{[(p, owed(self.mask(p), self.cells[p]['rot'])) for p in members]}")

        # A rooted tile must already read as solved: it can never be turned, and every
        # check above ran against the board with every rotation at zero, so one authored
        # off its solution means what was proved is not what ships. Mirrors
        # LevelValidator.CheckRootedTiles.
        for p, c in self.cells.items():
            if c['locked'] and not alike(self.mask(p), c['cross'], c['rot']):
                errs.append(f"rooted {p} starts {owed(self.mask(p), c['rot'], c['cross'])} "
                            "turn(s) from its solution and can never be turned")

        # fragile conduits must survive their own group's turn count
        for p, c in self.cells.items():
            if not c['fragile']:
                continue
            k = self.group_turns(p)
            if k > c['fragile']:
                errs.append(f"fragile {p} needs {k} turns but survives {c['fragile']}")

        return errs, warns

    def group_turns(self, p):
        c = self.cells[p]
        if not c['link']:
            return owed(self.mask(p), c['rot'], c['cross'])
        members = [q for q, d in self.cells.items() if d['link'] == c['link']]
        for k in range(4):
            if all(alike(self.mask(q), self.cells[q]['cross'],
                         (self.cells[q]['rot'] + k) & 3) for q in members):
                return k
        return 0

    def par(self):
        total, counted = 0, set()
        for p, c in self.cells.items():
            if c['locked']:
                continue
            if alike(self.mask(p), c['cross'], 1):         # inert at every angle
                continue
            if c['link']:
                if c['link'] in counted:
                    continue
                counted.add(c['link'])
            total += self.group_turns(p)
        return total

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

        def dark(g):
            return any(self.cells[q]['kind'] == 'duskcap'
                       for q, st in comp if comp[(q, st)] == g)

        def why(ga, gb):
            if dark(ga) or dark(gb):
                return 'wakes the dark'
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
                if not any(rotl(self.mask(p), k) & BITS[d] for k in turns_p):
                    continue
                if not any(rotl(self.mask(q), k) & BITS[OPP[d]] for k in turns_q):
                    continue
                out.append((p, q, why(comp[(p, 0)], comp[(q, 0)])))

            if (c['kind'] == 'cross' and not c['locked']
                    and not alike(self.mask(p), c['cross'], 1)
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
                glyph = {'pipe': '+', 'source': '*', 'lamp': 'O', 'duskcap': 'X',
                         'cross': ')'}[c['kind']]
                if c['link']:
                    glyph = c['link'].lower()
                if c['locked']:
                    glyph = glyph.upper() if c['kind'] == 'pipe' else glyph
                    glyph = '#' if c['kind'] == 'pipe' else glyph
                top += ' | ' if m & N else '   '
                top += ' '
                mid += ('-' if m & W else ' ') + glyph + ('-' if m & E else ' ') + ' '
                bot += ' | ' if m & S else '   '
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
                  f"decided {len(r['decided'])}  rejected by colour {r['colour_only']}, "
                  f"by the dark {r['dark_only']}")
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
    """The (seed, bias) whose par lands nearest `target`, and the board it makes.

    `make` takes (seed, bias) and returns a finished Board. Par decides the clock and
    the move budget, so a chapter's ramp is a set of numbers somebody chose - this is
    how they get chosen rather than discovered. Boards that do not check are skipped,
    so a fit can never hand back one that is unwinnable.
    """
    best = None
    for bias in biases:
        for seed in seeds:
            board = make(seed, bias)
            if board.check()[0]:
                continue
            gap = abs(board.par() - target)
            if best is None or gap < best[0]:
                best = (gap, seed, bias, board)
            if gap == 0:
                return best[1], best[2], best[3]
    if best is None:
        raise ValueError("no (seed, bias) produced a board that checks")
    return best[1], best[2], best[3]

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


def owed(solved, rot):
    for k in range(4):
        if rotl(solved, (rot + k) & 3) == solved:
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
                                  fragile=fragile, link=link)

    def source(self, x, y, colour, rot=0):
        self.cells[(x, y)] = dict(kind='source', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None)

    def lamp(self, x, y, colour='A', rot=0):
        self.cells[(x, y)] = dict(kind='lamp', colour=COLOURS[colour], rot=rot,
                                  locked=False, fragile=0, link=None)

    def duskcap(self, x, y, rot=0, locked=False):
        self.cells[(x, y)] = dict(kind='duskcap', colour=0, rot=rot,
                                  locked=locked, fragile=0, link=None)

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
        head = {'pipe': '-', 'source': '*', 'lamp': '@', 'duskcap': 'x'}[c['kind']]
        arms = ''.join(ch for ch, b in zip('NESW', BITS) if self.mask(p) & b)
        tok = head + arms
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
    def solve_state(self, rots=None):
        """Components and colours at the given rotations (default: solved)."""
        comp, colour = {}, {}
        g = 0
        for p in self.cells:
            if p in comp:
                continue
            col, q = 0, deque([p])
            comp[p] = g
            while q:
                a = q.popleft()
                ca = self.cells[a]
                if ca['kind'] == 'source':
                    col |= ca['colour']
                ma = rotl(self.mask(a), (rots or {}).get(a, 0))
                for d in range(4):
                    if not ma & BITS[d]:
                        continue
                    b = (a[0] + STEP[d][0], a[1] + STEP[d][1])
                    if b not in self.cells or b in comp:
                        continue
                    mb = rotl(self.mask(b), (rots or {}).get(b, 0))
                    if not mb & BITS[OPP[d]]:
                        continue
                    comp[b] = g
                    q.append(b)
            colour[g] = col
            g += 1
        return comp, colour

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

        comp, colour = self.solve_state()
        for p, c in self.cells.items():
            have = colour[comp[p]]
            if c['kind'] == 'lamp':
                want = c['colour']
                ok = (have != 0) if want == 0 else (have == want)
                if not ok:
                    errs.append(f"lamp {p} wants {LETTER[want]} but the solution feeds it "
                                f"{LETTER[have] if have else 'nothing'}")
            if c['kind'] == 'duskcap' and have:
                errs.append(f"duskcap {p} is lit by the authored solution ({LETTER[have]})")

        # bound groups: one common turn count, no rooted member, at least two members
        groups = {}
        for p, c in self.cells.items():
            if c['link']:
                groups.setdefault(c['link'], []).append(p)
        for rune, members in groups.items():
            if len(members) < 2:
                errs.append(f"bound rune '{rune}' has only one member {members}")
            for p in members:
                if self.cells[p]['kind'] != 'pipe':
                    errs.append(f"bound {p} is not a conduit")
                if self.cells[p]['locked']:
                    errs.append(f"bound {p} is also rooted")
            common = [k for k in range(4)
                      if all(rotl(self.mask(p), (self.cells[p]['rot'] + k) & 3) == self.mask(p)
                             for p in members)]
            if not common:
                errs.append(f"bound rune '{rune}' has no shared turn count: "
                            f"{[(p, owed(self.mask(p), self.cells[p]['rot'])) for p in members]}")

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
            return owed(self.mask(p), c['rot'])
        members = [q for q, d in self.cells.items() if d['link'] == c['link']]
        for k in range(4):
            if all(rotl(self.mask(q), (self.cells[q]['rot'] + k) & 3) == self.mask(q)
                   for q in members):
                return k
        return 0

    def par(self):
        total, counted = 0, set()
        for p, c in self.cells.items():
            if c['locked']:
                continue
            if rotl(self.mask(p), 1) == self.mask(p):      # inert
                continue
            if c['link']:
                if c['link'] in counted:
                    continue
                counted.add(c['link'])
            total += self.group_turns(p)
        return total

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
                glyph = {'pipe': '+', 'source': '*', 'lamp': 'O', 'duskcap': 'X'}[c['kind']]
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

    def report(self, name):
        errs, warns = self.check()
        print(f"=== {name}  {self.w}x{self.h}  par={self.par()} "
              f"gold={-(-self.par()*135//100)} silver={-(-self.par()*200//100)} "
              f"clock={self.par()*2}s")
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

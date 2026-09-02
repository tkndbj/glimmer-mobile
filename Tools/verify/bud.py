"""Budburst's rules, mirrored in Python so a grove can be proved with no Unity anywhere.

The shipping copy is `Assets/Game/Scripts/Domain/Modes/Lab/BudBoard.cs` and `BudSolver.cs`; this
is the mirror `content.py` and `Tools/chapters/b01_thicket.py` run. Invariant 9a applies in full:
the two copies are pinned against each other by `bud-vectors.json`, which `content.py` runs
through this file and `BudVectorTests` runs through the C# one.

**The rule in one paragraph.** Tap a flower and the colour in hand MIXES into it - and three
alike touching burst and spread.

    tap red + green in hand -> yellow
    three yellows touching   -> they burst
    a burst washes its colour into every flower touching it -> more threes -> chain

Mixing only ever *adds* channels, so every tap drives the board toward white and toward a burst.
That is the property that makes it chill: the board wants to go off, and the player is choosing
where. It also bounds everything - a wave always removes at least three flowers and nothing is
ever added, so the settle terminates and the search does too.

**Par is the fewest moves that free every critter.** Not "clear every flower": branching is the
flower count, so that goal cost tens of thousands of positions and often could not be proved.

**Four rules arrived together and all four are here.** A grove *falls* and *grows*: what bursts
leaves a hole, everything above slides into it, and new flowers arrive along the top from an
authored strip - so a chain compounds instead of thinning the board out. A *white* flower is a
bomb rather than a dead cell: tapping it clears the three-by-three around it. And one flower
*creeps* between taps - the palest one standing beside a cocoon takes the colour just spent, so
the grove always leans a little further toward going off, and always beside somebody who needs
freeing. Every one of them is a pure function of the position and the tap, so par is still
searchable and two players on the same grove still meet the same board.

**And the second chapter brings SPECIALS and the GRAFT.** A bunch of five leaves a BOLT on the
cell the player tapped (or the bunch's lowest cell, once the chain has moved on); a bunch of
eight leaves a SUN. A special is a flower wearing the bunch's colour, and it FIRES when tapped,
when a bunch takes it in, or when another special's reach hits it: a bolt clears its whole row
and column, a sun the five-by-five around it. What a special clears it does not wash - it cracks
every cocoon it hits and every cocoon beside what it cleared. A GRAFT drags two neighbouring
flowers to trade places, refused unless it makes a bunch; it costs a tap and keeps the colour in
hand, and a special the bunch forges lands on the flower the player moved.

**Two counters, and the difference is the graft.** `spent` is moves out of the satchel, which
every move costs; `dealt` is colours off the basket, which only a tap costs.

**Move order is part of the contract with the C# copy**: taps by cell, then grafts by cell
(rightward before downward). Mirrors `BudRun.Moves`. Ties in the careless player's ranking and
in the best-opening-move reading go to the earlier move.
"""
import sys
from collections import deque

#: Mirrors `BudLayout.MostWaves` - the bound a regrowing grove needs and a fixed
#: one does not. See the C# for why.
MOST_WAVES = 14

#: Mirrors `BudLayout.BoltFrom`, `SunFrom` and `SunReach`.
BOLT_FROM, SUN_FROM, SUN_REACH = 5, 8, 2

EMPTY, STONE = ".", "#"
COCOON = {"o": 1, "O": 2}
NONE, BOLT, SUN = 0, 1, 2
SPECIAL = {"|": BOLT, "*": SUN}
R, G, B = 1, 2, 4
ALL = R | G | B
LETTER = {R: "R", G: "G", B: "B", R | G: "Y", R | B: "M", G | B: "C", ALL: "W"}
MASK = {v: k for k, v in LETTER.items()}


def channels(mask):
    return (1 if mask & R else 0) + (1 if mask & G else 0) + (1 if mask & B else 0)


class Grove(object):
    def __init__(self, rows, deal, regrow=None, grafts=False, specials=None, forges=False):
        rows = list(rows)
        self.h = len(rows); self.w = len(rows[0])
        self.ground = []
        self.colour = []
        for row in rows:
            for c in row:
                if c in MASK:
                    self.ground.append("f"); self.colour.append(MASK[c])
                elif c in COCOON:
                    self.ground.append("c"); self.colour.append(COCOON[c])
                elif c == STONE:
                    self.ground.append(STONE); self.colour.append(0)
                elif c in (EMPTY, "-"):
                    self.ground.append(EMPTY); self.colour.append(0)
                else:
                    raise ValueError("'%s' is not part of a grove" % c)
        self.count = self.w * self.h
        self.deal = [MASK[c] for c in deal]
        if any(m not in (R, G, B) for m in self.deal):
            raise ValueError("a grove is dealt pure colour; blends come from mixing")

        # A strip may deal blends where a basket may not: a basket is what the player decides
        # with, and a strip is scenery. See `BudDeal.TryParse`'s `pure` argument.
        self.regrow = [MASK[c] for c in regrow] if regrow else None
        self.grafts = bool(grafts)

        # Specials the grove deals already forged, as a second grid. Mirrors
        # `BudLayout.TryReadSpecials`: '|' a bolt, '*' a sun, '.' an ordinary flower.
        self.special = [NONE] * self.count
        if specials:
            rows2 = list(specials)
            if len(rows2) != self.h:
                raise ValueError("the specials are drawn over the grove, so they are %d rows; "
                                 "this one writes %d" % (self.h, len(rows2)))
            for y, row in enumerate(rows2):
                if len(row) != self.w:
                    raise ValueError("specials row %d names %d cells, expected %d"
                                     % (y, len(row), self.w))
                for x, c in enumerate(row):
                    if c in (".", "-", " "):
                        continue
                    if c not in SPECIAL:
                        raise ValueError("'%s' at specials row %d column %d is not a special"
                                         % (c, y, x))
                    at = y * self.w + x
                    if self.ground[at] != "f":
                        raise ValueError("the special at %d,%d stands on nothing a flower is "
                                         "standing on" % (x, y))
                    self.special[at] = SPECIAL[c]

        # Whether a big bunch leaves a special behind. Gated, because the first chapter was
        # authored and pinned without it; a grove dealing one forges whatever it says.
        self.forges = bool(forges) or self.specials > 0

    @property
    def specials(self):
        return sum(1 for s in self.special if s)

    def beside(self, i):
        x, y = i % self.w, i // self.w
        out = []
        if y > 0: out.append(i - self.w)
        if x < self.w - 1: out.append(i + 1)
        if y < self.h - 1: out.append(i + self.w)
        if x > 0: out.append(i - 1)
        return out

    def reach(self, i, kind):
        """Every cell a special at i clears when it fires, nearest first. Mirrors
        `BudLayout.Reach`."""
        x, y = i % self.w, i // self.w
        out = []
        far = max(self.w, self.h)
        for d in range(1, far):
            if kind == BOLT:
                if x - d >= 0: out.append(i - d)
                if x + d < self.w: out.append(i + d)
                if y - d >= 0: out.append(i - d * self.w)
                if y + d < self.h: out.append(i + d * self.w)
                continue
            if kind != SUN or d > SUN_REACH:
                break
            for dy in range(-d, d + 1):
                for dx in range(-d, d + 1):
                    if abs(dx) != d and abs(dy) != d:
                        continue
                    a, b = x + dx, y + dy
                    if 0 <= a < self.w and 0 <= b < self.h:
                        out.append(b * self.w + a)
        return out

    def at(self, dealt):
        return self.deal[dealt % len(self.deal)]

    def grows(self, taken):
        return self.regrow[taken % len(self.regrow)]


class Board(object):
    def __init__(self, g, ground=None, colour=None, special=None, dealt=0, grown=0,
                 forged=0, fired=0):
        self.g = g
        self.ground = list(ground if ground is not None else g.ground)
        self.colour = list(colour if colour is not None else g.colour)
        self.special = list(special if special is not None else g.special)
        self.dealt = dealt
        self.grown = grown

        #: Specials forged and fired since the grove was dealt. Readings, not position -
        #: `key` leaves them out, as the C# does.
        self.forged = forged
        self.fired = fired

    def copy(self):
        return Board(self.g, self.ground, self.colour, self.special, self.dealt, self.grown,
                     self.forged, self.fired)

    def key(self):
        # Where the strip is up to is part of the position: two groves that look the same but
        # have taken a different number of flowers off it will grow different ones next. So is
        # where the basket is up to, and which flowers are specials.
        lap = self.grown % len(self.g.regrow) if self.g.regrow else 0
        return (self.dealt % len(self.g.deal), lap,
                tuple(self.ground), tuple(self.colour), tuple(self.special))

    @property
    def hand(self):
        return self.g.at(self.dealt)

    @property
    def flowers(self):
        return sum(1 for c in self.ground if c == "f")

    @property
    def shut(self):
        return sum(1 for c in self.ground if c == "c")

    @property
    def specials(self):
        return sum(1 for i in range(self.g.count) if self.ground[i] == "f" and self.special[i])

    @property
    def done(self):
        return self.shut == 0

    def groups(self, least=3):
        seen = [False] * self.g.count
        out = []
        for i in range(self.g.count):
            if seen[i] or self.ground[i] != "f":
                continue
            col = self.colour[i]
            blob, q = [], deque([i]); seen[i] = True
            while q:
                a = q.popleft(); blob.append(a)
                for n in self.g.beside(a):
                    if not seen[n] and self.ground[n] == "f" and self.colour[n] == col:
                        seen[n] = True; q.append(n)
            if len(blob) >= least:
                out.append((col, blob))
        return out

    def is_bomb(self, i):
        # Gated with falling, growing and the creep: the strip is what says a grove is alive.
        return bool(self.g.regrow) and self.ground[i] == "f" and self.colour[i] == ALL

    # ------------------------------------------------------------------ the moves
    def can_tap(self, i, colour=None):
        if colour is None:
            colour = self.hand
        if self.ground[i] != "f":
            return False
        if self.special[i] or self.is_bomb(i):
            return True
        return (self.colour[i] | colour) != self.colour[i]

    def swap(self, a, b):
        self.colour[a], self.colour[b] = self.colour[b], self.colour[a]
        self.special[a], self.special[b] = self.special[b], self.special[a]

    def can_graft(self, a, b):
        """Mirrors `BudBoard.CanGraft`: two touching flowers that differ, whose trade makes a
        bunch."""
        if not self.g.grafts or a == b:
            return False
        if self.ground[a] != "f" or self.ground[b] != "f":
            return False
        if self.colour[a] == self.colour[b] and self.special[a] == self.special[b]:
            return False
        lo, hi = min(a, b), max(a, b)
        touching = hi - lo == self.g.w or (hi - lo == 1 and lo % self.g.w < self.g.w - 1)
        if not touching:
            return False
        self.swap(a, b)
        bunches = self.joins_a_bunch(a) or self.joins_a_bunch(b)
        self.swap(a, b)
        return bunches

    def moves(self):
        """Every legal move, in the one order the C# walks them. Mirrors `BudRun.Moves`."""
        out = []
        hand = self.hand
        for i in range(self.g.count):
            if self.can_tap(i, hand): out.append(("tap", i, -1))
        if self.g.grafts:
            for i in range(self.g.count):
                if i % self.g.w < self.g.w - 1 and self.can_graft(i, i + 1):
                    out.append(("graft", i, i + 1))
                if i // self.g.w < self.g.h - 1 and self.can_graft(i, i + self.g.w):
                    out.append(("graft", i, i + self.g.w))
        return out

    def any_move(self):
        """Whether any move is legal with anything the basket still deals, or a graft.
        Mirrors `BudBoard.AnyMove`."""
        for colour in set(self.g.deal):
            for i in range(self.g.count):
                if self.can_tap(i, colour):
                    return True
        if self.g.grafts:
            for i in range(self.g.count):
                if i % self.g.w < self.g.w - 1 and self.can_graft(i, i + 1): return True
                if i // self.g.w < self.g.h - 1 and self.can_graft(i, i + self.g.w): return True
        return False

    def play(self, move):
        """One move of any kind. (burst, waves, freed, cracked, forged, fired)."""
        kind, a, b = move
        if kind == "tap":
            return self.tap(a)
        if kind == "graft":
            return self.graft(a, b)
        raise ValueError(kind)

    def tap(self, i):
        """The mix and the whole chain it sets off - or a special or a bomb going off."""
        if not self.can_tap(i):
            return (0, 0, 0, 0, 0, 0)
        colour = self.hand
        self.dealt += 1
        if self.special[i]:
            return self.strike(i)
        if self.is_bomb(i):
            return self.bomb(i)
        self.colour[i] |= colour
        return self.settle(colour, origin=i)

    def graft(self, a, b):
        """Two neighbours trade places. Costs no colour. Mirrors `BudBoard.Graft`."""
        if not self.can_graft(a, b):
            return (0, 0, 0, 0, 0, 0)
        self.swap(a, b)
        return self.settle(0, origin=b)

    # ------------------------------------------------------------------ the chain
    def touch(self, cell, colour, washes, wash, cracked):
        """What one bursting flower reaches. Mirrors `BudBoard.Touch`."""
        for n in self.g.beside(cell):
            if self.ground[n] == "c":
                if n not in cracked: cracked.append(n)
            elif self.ground[n] == "f" and washes:
                if n not in wash: wash[n] = 0
                wash[n] |= colour

    def fire(self, fuse, cracked, anvils=()):
        """Every special on the fuse goes off, and everything in its reach with it. Specials in
        reach are queued rather than cleared - except one forged this very wave, which is not
        there yet to be hit. (burst, fired, biggest). Mirrors `BudBoard.Fire`."""
        burst = fired = biggest = 0
        k = 0
        while k < len(fuse):
            at = fuse[k]; k += 1
            if self.ground[at] != "f" or not self.special[at]:
                continue
            kind = self.special[at]
            size = SUN_FROM + 1 if kind == SUN else BOLT_FROM
            biggest = max(biggest, size)
            self.fired += 1; fired += 1

            self.touch(at, self.colour[at], False, {}, cracked)
            self.ground[at] = EMPTY; self.colour[at] = 0; self.special[at] = NONE
            burst += 1

            for r in self.g.reach(at, kind):
                if self.ground[r] == "c":
                    if r not in cracked: cracked.append(r)
                    continue
                if self.ground[r] != "f":
                    continue
                if self.special[r]:
                    if r not in anvils and r not in fuse: fuse.append(r)
                    continue
                self.touch(r, self.colour[r], False, {}, cracked)
                self.ground[r] = EMPTY; self.colour[r] = 0
                burst += 1
        del fuse[:]
        return burst, fired, biggest

    def crack(self, cracked):
        freed = held = 0
        for n in cracked:
            self.colour[n] -= 1
            if self.colour[n] > 0:
                held += 1
            else:
                self.ground[n] = EMPTY; self.colour[n] = 0; freed += 1
        return freed, held

    def paint(self, wash):
        for n in wash:
            if self.ground[n] == "f":
                self.colour[n] |= wash[n]

    def strike(self, i):
        """A tapped special going off: wave nought is its own clearing."""
        cracked = []
        burst, fired, biggest = self.fire([i], cracked)
        freed, held = self.crack(cracked)
        self.fall()
        b2, w2, f2, c2, fo2, fi2 = self.settle(0)
        return (burst + b2, 1 + w2, freed + f2, held + c2, fo2, fired + fi2)

    def bomb(self, i):
        """A white flower going off: the three-by-three around it bursts at once, and a special
        in the square fires with it."""
        x, y = i % self.g.w, i // self.g.w
        blast, fuse = [], []
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                a, b = x + dx, y + dy
                if 0 <= a < self.g.w and 0 <= b < self.g.h:
                    at = b * self.g.w + a
                    if self.ground[at] != "f":
                        continue
                    if self.special[at]: fuse.append(at)
                    else: blast.append(at)

        cracked = []
        for a in blast:
            self.touch(a, self.colour[a], False, {}, cracked)

        burst = 0
        for a in blast:
            self.ground[a] = EMPTY; self.colour[a] = 0; burst += 1

        b1, fired, _big = self.fire(fuse, cracked)
        burst += b1
        freed, held = self.crack(cracked)
        self.fall()
        b2, w2, f2, c2, fo2, fi2 = self.settle(0)
        return (burst + b2, 1 + w2, freed + f2, held + c2, fo2, fired + fi2)

    def fall(self):
        """Everything slides down into the holes under it. Inside a chain; nothing is added.

        Gated on the strip with `grow`: falling and growing are one rule, so a grove either has
        both or neither. Mirrors `BudBoard.Fall`. A special falls as the flower it is.
        """
        if not self.g.regrow:
            return
        for x in range(self.g.w):
            floor = self.g.h - 1
            for y in range(self.g.h - 1, -1, -1):
                at = y * self.g.w + x
                if self.ground[at] == EMPTY:
                    continue
                to = floor * self.g.w + x
                floor -= 1
                if to == at:
                    continue
                self.ground[to] = self.ground[at]; self.colour[to] = self.colour[at]
                self.special[to] = self.special[at]
                self.ground[at] = EMPTY; self.colour[at] = 0; self.special[at] = NONE

    def joins_a_bunch(self, cell):
        col = self.colour[cell]
        seen, blob, q = {cell}, 0, [cell]
        while q:
            a = q.pop(); blob += 1
            for nb in self.g.beside(a):
                if nb in seen: continue
                if self.ground[nb] == "f" and self.colour[nb] == col:
                    seen.add(nb); q.append(nb)
        return blob >= 3

    def grow(self):
        """New flowers fill the holes once the chain has stopped, and never make a bunch.

        Mirrors `BudBoard.Grow`. Growing *inside* the chain destroys the termination argument -
        a repeating strip can resonate with the grove for ever - so it happens once, afterwards.
        """
        if not self.g.regrow:
            return
        for y in range(self.g.h - 1, -1, -1):
            for x in range(self.g.w):
                at = y * self.g.w + x
                if self.ground[at] != EMPTY:
                    continue
                self.ground[at] = "f"; self.special[at] = NONE
                for _ in range(len(self.g.regrow)):
                    self.colour[at] = self.g.grows(self.grown)
                    if not self.joins_a_bunch(at):
                        break
                    self.grown += 1
                self.grown += 1

    def creep(self, spent):
        """The palest flower beside a shut cocoon takes the colour just spent. Exactly one, and
        never a special."""
        if not spent or not self.g.regrow:
            return
        best, palest = None, 99
        for i in range(self.g.count):
            if self.ground[i] != "c":
                continue
            for at in self.g.beside(i):
                if self.ground[at] != "f" or self.special[at]:
                    continue
                if (self.colour[at] | spent) == self.colour[at]:
                    continue
                if channels(self.colour[at]) >= palest:
                    continue
                palest, best = channels(self.colour[at]), at
        if best is None:
            return
        was = self.colour[best]
        self.colour[best] |= spent
        if self.joins_a_bunch(best):
            self.colour[best] = was

    def settle(self, spent, origin=-1):
        burst = waves = freed = cracked = forged = fired = 0

        # Mirrors `BudLayout.MostWaves`. Once a grove regrows, a chain is no longer bounded by
        # the board it started on - a repeating strip can resonate with the grove for ever.
        while waves < MOST_WAVES:
            blobs = self.groups()
            if not blobs:
                break

            hit, wash, fuse, queue, anvils = [], {}, [], [], []
            for col, blob in blobs:
                forge = NONE
                if self.g.forges:
                    forge = SUN if len(blob) >= SUN_FROM else BOLT if len(blob) >= BOLT_FROM else NONE
                anvil = -1
                if forge:
                    anvil = origin if (origin >= 0 and origin in blob) else blob[0]

                for a in blob:
                    self.touch(a, col, True, wash, hit)

                for a in blob:
                    if self.special[a] and a != anvil:
                        fuse.append(a)
                    elif a != anvil:
                        queue.append(a)

                if anvil >= 0:
                    anvils.append(anvil)
                    self.special[anvil] = forge
                    self.colour[anvil] = col
                    self.forged += 1; forged += 1

            for a in queue:
                self.ground[a] = EMPTY; self.colour[a] = 0
                burst += 1

            b1, f1, _big = self.fire(fuse, hit, anvils)
            burst += b1; fired += f1

            f, c = self.crack(hit)
            freed += f; cracked += c

            self.paint(wash)
            self.fall()
            waves += 1
            origin = -1

        self.grow()
        self.creep(spent)
        return (burst, waves, freed, cracked, forged, fired)

    def draw(self):
        for y in range(self.g.h):
            line = []
            for x in range(self.g.w):
                a = y * self.g.w + x
                k = self.ground[a]
                if k == "f":
                    ch = LETTER[self.colour[a]]
                    if self.special[a] == BOLT: ch = ch.lower()
                    elif self.special[a] == SUN: ch = "*"
                    line.append(ch)
                elif k == "c": line.append("O" if self.colour[a] > 1 else "o")
                else: line.append(k)
            print("   " + "".join(line))


#: Mirrors `BudSolver`. See the C# for why each is the number it is.
NODE_BUDGET = 120_000
MAX_TAPS = 8
MAX_WAYS = 2000

#: Mirrors `BudRules.DefaultSpare` and `LevelTuning`'s three factors, in hundredths.
DEFAULT_SPARE = 5
GOLD_HUNDREDTHS, SILVER_HUNDREDTHS, BUDGET_HUNDREDTHS = 120, 140, 160

#: Mirrors `BudLayout.MinWidth`/`MaxWidth`, which are the same both ways.
MIN_SIDE, MAX_SIDE = 4, 9


def over(par, hundredths):
    """Ceiling of `par * hundredths/100` in integer arithmetic. Mirrors `LevelTuning.Over`."""
    return (par * hundredths + 99) // 100


def grove_of(rows, deal, regrow=None, grafts=False, specials=None, forges=False):
    return Grove(rows, deal, regrow, grafts, specials, forges)


def search(rows, deal, regrow=None, grafts=False, specials=None, forges=False):
    """(par, ways, nodes, proved, forged, fired). Mirrors `BudSolver.Survey`."""
    g = grove_of(rows, deal, regrow, grafts, specials, forges)
    start = Board(g)
    if start.done:
        return (0, 0, 0, True, 0, 0)

    # Certainly lost before a tap is spent, which is a *proof* rather than a search that ran out -
    # and the two have to be told apart, because one says the grove is unwinnable and the other
    # says nobody knows. Mirrors the same early-out in `BudSolver.Search.Run`.
    if not start.any_move():
        return (0, 0, 0, True, 0, 0)

    st = {"n": 0, "ways": 0, "spent": False, "limit": 0, "forged": 0, "fired": 0}

    def walk(b, spent, seen):
        if st["spent"]: return
        st["n"] += 1
        if st["n"] > NODE_BUDGET: st["spent"] = True; return
        k = (spent, b.key())
        if k in seen: return
        seen.add(k)
        if b.done:
            if spent == st["limit"] and st["ways"] < MAX_WAYS:
                st["ways"] += 1
                if b.forged: st["forged"] += 1
                if b.fired: st["fired"] += 1
            return
        if spent >= st["limit"]: return
        for move in b.moves():
            n = b.copy(); n.play(move)
            walk(n, spent + 1, seen)
            if st["spent"]: return

    for limit in range(1, MAX_TAPS + 1):
        st["limit"] = limit; st["ways"] = 0; st["forged"] = 0; st["fired"] = 0
        walk(start, 0, set())
        if st["spent"]: return (0, 0, st["n"], False, 0, 0)
        if st["ways"]: return (limit, st["ways"], st["n"], True, st["forged"], st["fired"])
    return (0, 0, st["n"], False, 0, 0)


def careless(rows, deal, budget, regrow=None, grafts=False, specials=None, forges=False):
    """How a player who always takes the biggest move gets on. Mirrors `BudSolver.Careless`."""
    g = grove_of(rows, deal, regrow, grafts, specials, forges); b = Board(g)
    for spent in range(budget):
        if b.done: return spent
        best, gain = None, None
        for move in b.moves():
            p = b.copy(); s = p.play(move)
            score = (s[2], s[3], s[1], s[0])
            if gain is None or score > gain: best, gain = move, score
        if best is None: return -1
        b.play(best)
    return budget if b.done else -1


def biggest(rows, deal, regrow=None, grafts=False, specials=None, forges=False):
    """The best opening move by chain length, then size, and what it came to.
    Mirrors `BudSolver.Opening`: the tie goes to the earlier move."""
    g = grove_of(rows, deal, regrow, grafts, specials, forges); b = Board(g)
    best, where = (0, 0, 0, 0, 0, 0), None
    for move in b.moves():
        p = b.copy(); s = p.play(move)
        if (s[1], s[0]) > (best[1], best[0]): best, where = s, move
    return best, where


def forgeable(rows, deal, regrow=None, grafts=False, specials=None, forges=False):
    """Opening moves that forge a special. Mirrors `BudObjectReading.Forgeable`."""
    g = grove_of(rows, deal, regrow, grafts, specials, forges); b = Board(g)
    n = 0
    for move in b.moves():
        p = b.copy()
        if p.play(move)[4] > 0: n += 1
    return n


def survey(rows, deal, regrow=None, grafts=False, specials=None, forges=False):
    args = (rows, deal, regrow, grafts, specials, forges)
    g = grove_of(*args)
    par, ways, nodes, proved, forged, fired = search(*args)
    best, where = biggest(*args)

    return dict(w=g.w, h=g.h, flowers=Board(g).flowers, cocoons=Board(g).shut,
                par=par, ways=ways, nodes=nodes, proved=proved,
                careless=(careless(rows, deal, par + DEFAULT_SPARE, regrow, grafts, specials, forges)
                          if proved and par else -1),
                bestBurst=best[0], bestWaves=best[1], bestFreed=best[2], bestMove=where,
                grafts=g.grafts, forges=g.forges, specials=g.specials,
                forgeable=forgeable(*args), forged=forged, fired=fired)


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    print(survey(sys.argv[1].split(","), sys.argv[2]))

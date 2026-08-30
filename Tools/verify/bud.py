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

**Par is the fewest taps that free every critter.** Not "clear every flower": branching is the
flower count, so that goal cost tens of thousands of positions and often could not be proved.

**Four rules arrived together and all four are here.** A grove *falls* and *grows*: what bursts
leaves a hole, everything above slides into it, and new flowers arrive along the top from an
authored strip - so a chain compounds instead of thinning the board out. A *white* flower is a
bomb rather than a dead cell: tapping it clears the three-by-three around it. And one flower
*creeps* between taps - the palest one standing beside a cocoon takes the colour just spent, so
the grove always leans a little further toward going off, and always beside somebody who needs
freeing. Every one of them is a pure function of the position and the tap, so par is still
searchable and two players on the same grove still meet the same board.
"""
import sys
from collections import deque

#: Mirrors `BudLayout.MostWaves` - the bound a regrowing grove needs and a fixed
#: one does not. See the C# for why.
MOST_WAVES = 14

EMPTY, STONE = ".", "#"
COCOON = {"o": 1, "O": 2}
R, G, B = 1, 2, 4
ALL = R | G | B
LETTER = {R: "R", G: "G", B: "B", R | G: "Y", R | B: "M", G | B: "C", ALL: "W"}
MASK = {v: k for k, v in LETTER.items()}


def channels(mask):
    return (1 if mask & R else 0) + (1 if mask & G else 0) + (1 if mask & B else 0)


class Grove(object):
    def __init__(self, rows, deal, regrow=None):
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

    def beside(self, i):
        x, y = i % self.w, i // self.w
        out = []
        if y > 0: out.append(i - self.w)
        if x < self.w - 1: out.append(i + 1)
        if y < self.h - 1: out.append(i + self.w)
        if x > 0: out.append(i - 1)
        return out

    def at(self, spent):
        return self.deal[spent % len(self.deal)]

    def grows(self, taken):
        return self.regrow[taken % len(self.regrow)]


class Board(object):
    def __init__(self, g, ground=None, colour=None, spent=0, grown=0):
        self.g = g
        self.ground = list(ground if ground is not None else g.ground)
        self.colour = list(colour if colour is not None else g.colour)
        self.spent = spent
        self.grown = grown

    def copy(self):
        return Board(self.g, self.ground, self.colour, self.spent, self.grown)

    def key(self):
        # Where the strip is up to is part of the position: two groves that look the same but
        # have taken a different number of flowers off it will grow different ones next.
        lap = self.grown % len(self.g.regrow) if self.g.regrow else 0
        return (self.spent % len(self.g.deal), lap,
                tuple(self.ground), tuple(self.colour))

    @property
    def flowers(self):
        return sum(1 for c in self.ground if c == "f")

    @property
    def shut(self):
        return sum(1 for c in self.ground if c == "c")

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

    def can_tap(self, i):
        if self.ground[i] != "f":
            return False
        if self.is_bomb(i):
            return True
        return (self.colour[i] | self.g.at(self.spent)) != self.colour[i]

    def any_move(self):
        """Whether any tap is legal with anything the basket still deals.

        Mirrors `BudBoard.AnyMove`. A flower left is not a move left: white holds every channel,
        so a grove of nothing but white has flowers on it and no legal tap in it. It asks about
        the whole basket because the basket repeats for ever.
        """
        for i in range(self.g.count):
            if self.is_bomb(i):
                return True
        for colour in set(self.g.deal):
            for i in range(self.g.count):
                if self.ground[i] == "f" and (self.colour[i] | colour) != self.colour[i]:
                    return True
        return False

    def tap(self, i):
        """(burst, waves, freed, cracked). The mix, and the whole chain it sets off."""
        if not self.can_tap(i):
            return (0, 0, 0, 0)

        spent = self.g.at(self.spent)

        if self.is_bomb(i):
            self.spent += 1
            return self.bomb(i)

        self.colour[i] |= spent
        self.spent += 1
        return self.settle(spent)

    def bomb(self, i):
        """A white flower going off: the three-by-three around it bursts at once."""
        x, y = i % self.g.w, i // self.g.w
        blast = []
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                a, b = x + dx, y + dy
                if 0 <= a < self.g.w and 0 <= b < self.g.h:
                    at = b * self.g.w + a
                    if self.ground[at] == "f":
                        blast.append(at)

        hit = []
        for a in blast:
            for n in self.g.beside(a):
                if self.ground[n] == "c" and n not in hit:
                    hit.append(n)

        burst = 0
        for a in blast:
            self.ground[a] = EMPTY; self.colour[a] = 0; burst += 1

        freed = cracked = 0
        for n in hit:
            self.colour[n] -= 1
            if self.colour[n] > 0:
                cracked += 1
            else:
                self.ground[n] = EMPTY; self.colour[n] = 0; freed += 1

        self.fall()
        b2, w2, f2, c2 = self.settle(0)
        return (burst + b2, 1 + w2, freed + f2, cracked + c2)

    def fall(self):
        """Everything slides down into the holes under it. Inside a chain; nothing is added.

        Gated on the strip with `grow`: falling and growing are one rule, so a grove either has
        both or neither. Mirrors `BudBoard.Fall`.
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
                self.ground[at] = EMPTY; self.colour[at] = 0

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
                self.ground[at] = "f"
                for _ in range(len(self.g.regrow)):
                    self.colour[at] = self.g.grows(self.grown)
                    if not self.joins_a_bunch(at):
                        break
                    self.grown += 1
                self.grown += 1

    def creep(self, spent):
        """The palest flower beside a shut cocoon takes the colour just spent. Exactly one."""
        if not spent or not self.g.regrow:
            return
        best, palest = None, 99
        for i in range(self.g.count):
            if self.ground[i] != "c":
                continue
            for at in self.g.beside(i):
                if self.ground[at] != "f":
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

    def settle(self, spent):
        burst = waves = freed = cracked = 0

        # Mirrors `BudLayout.MostWaves`. Once a grove regrows, a chain is no longer bounded by
        # the board it started on - a repeating strip can resonate with the grove for ever.
        while waves < MOST_WAVES:
            blobs = self.groups()
            if not blobs:
                break

            hit = []
            wash = []
            for col, blob in blobs:
                for a in blob:
                    for n in self.g.beside(a):
                        if self.ground[n] == "c" and n not in hit:
                            hit.append(n)
                        elif self.ground[n] == "f" and n not in [w[0] for w in wash]:
                            wash.append((n, col))
                        elif self.ground[n] == "f":
                            wash.append((n, col))
                for a in blob:
                    self.ground[a] = EMPTY; self.colour[a] = 0
                    burst += 1

            for n in hit:
                self.colour[n] -= 1
                if self.colour[n] > 0:
                    cracked += 1
                else:
                    self.ground[n] = EMPTY; self.colour[n] = 0; freed += 1

            for n, col in wash:
                if self.ground[n] == "f":
                    self.colour[n] |= col

            self.fall()
            waves += 1

        self.grow()
        self.creep(spent)
        return (burst, waves, freed, cracked)

    def draw(self):
        for y in range(self.g.h):
            line = []
            for x in range(self.g.w):
                a = y * self.g.w + x
                k = self.ground[a]
                if k == "f": line.append(LETTER[self.colour[a]])
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


def search(rows, deal, regrow=None):
    g = Grove(rows, deal, regrow)
    start = Board(g)
    if start.done:
        return (0, 0, 0, True)

    # Certainly lost before a tap is spent, which is a *proof* rather than a search that ran out -
    # and the two have to be told apart, because one says the grove is unwinnable and the other
    # says nobody knows. Mirrors the same early-out in `BudSolver.Search.Run`.
    if not start.any_move():
        return (0, 0, 0, True)

    st = {"n": 0, "ways": 0, "spent": False, "limit": 0}

    def walk(b, spent, seen):
        if st["spent"]: return
        st["n"] += 1
        if st["n"] > NODE_BUDGET: st["spent"] = True; return
        k = (spent, b.key())
        if k in seen: return
        seen.add(k)
        if b.done:
            if spent == st["limit"]: st["ways"] += 1
            return
        if spent >= st["limit"]: return
        for i in range(g.count):
            if not b.can_tap(i): continue
            n = b.copy(); n.tap(i)
            walk(n, spent + 1, seen)
            if st["spent"]: return

    for limit in range(1, MAX_TAPS + 1):
        st["limit"] = limit; st["ways"] = 0
        walk(start, 0, set())
        if st["spent"]: return (0, 0, st["n"], False)
        if st["ways"]: return (limit, st["ways"], st["n"], True)
    return (0, 0, st["n"], False)


def careless(rows, deal, budget, regrow=None):
    g = Grove(rows, deal, regrow); b = Board(g)
    for spent in range(budget):
        if b.done: return spent
        best, gain = None, None
        for i in range(g.count):
            if not b.can_tap(i): continue
            p = b.copy(); s = p.tap(i)
            score = (s[2], s[3], s[1], s[0])
            if gain is None or score > gain: best, gain = i, score
        if best is None: return -1
        b.tap(best)
    return budget if b.done else -1


def biggest(rows, deal, regrow=None):
    g = Grove(rows, deal, regrow); b = Board(g); best = (0, 0, 0, 0); where = None
    for i in range(g.count):
        if not b.can_tap(i): continue
        p = b.copy(); s = p.tap(i)
        if (s[1], s[0]) > (best[1], best[0]): best, where = s, i
    return best, where


def survey(rows, deal, regrow=None):
    g = Grove(rows, deal, regrow)
    par, ways, nodes, proved = search(rows, deal, regrow)
    best, where = biggest(rows, deal, regrow)
    return dict(w=g.w, h=g.h, flowers=Board(g).flowers, cocoons=Board(g).shut,
                par=par, ways=ways, nodes=nodes, proved=proved,
                careless=careless(rows, deal, par + 5, regrow) if proved and par else -1,
                bestBurst=best[0], bestWaves=best[1], bestFreed=best[2], bestAt=where)


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    print(survey(sys.argv[1].split(","), sys.argv[2]))

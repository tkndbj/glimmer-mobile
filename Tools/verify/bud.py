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
"""
import sys
from collections import deque

EMPTY, STONE = ".", "#"
COCOON = {"o": 1, "O": 2}
R, G, B = 1, 2, 4
ALL = R | G | B
LETTER = {R: "R", G: "G", B: "B", R | G: "Y", R | B: "M", G | B: "C", ALL: "W"}
MASK = {v: k for k, v in LETTER.items()}


class Grove(object):
    def __init__(self, rows, deal):
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


class Board(object):
    def __init__(self, g, ground=None, colour=None, spent=0):
        self.g = g
        self.ground = list(ground if ground is not None else g.ground)
        self.colour = list(colour if colour is not None else g.colour)
        self.spent = spent

    def copy(self):
        return Board(self.g, self.ground, self.colour, self.spent)

    def key(self):
        return (self.spent % len(self.g.deal),
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

    def can_tap(self, i):
        if self.ground[i] != "f":
            return False
        return (self.colour[i] | self.g.at(self.spent)) != self.colour[i]

    def any_move(self):
        """Whether any tap is legal with anything the basket still deals.

        Mirrors `BudBoard.AnyMove`. A flower left is not a move left: white holds every channel,
        so a grove of nothing but white has flowers on it and no legal tap in it. It asks about
        the whole basket because the basket repeats for ever.
        """
        for colour in set(self.g.deal):
            for i in range(self.g.count):
                if self.ground[i] == "f" and (self.colour[i] | colour) != self.colour[i]:
                    return True
        return False

    def tap(self, i):
        """(burst, waves, freed, cracked). The mix, and the whole chain it sets off."""
        if not self.can_tap(i):
            return (0, 0, 0, 0)

        self.colour[i] |= self.g.at(self.spent)
        self.spent += 1
        return self.settle()

    def settle(self):
        burst = waves = freed = cracked = 0

        while True:
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

            waves += 1

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


def search(rows, deal):
    g = Grove(rows, deal)
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


def careless(rows, deal, budget):
    g = Grove(rows, deal); b = Board(g)
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


def biggest(rows, deal):
    g = Grove(rows, deal); b = Board(g); best = (0, 0, 0, 0); where = None
    for i in range(g.count):
        if not b.can_tap(i): continue
        p = b.copy(); s = p.tap(i)
        if (s[1], s[0]) > (best[1], best[0]): best, where = s, i
    return best, where


def survey(rows, deal):
    g = Grove(rows, deal)
    par, ways, nodes, proved = search(rows, deal)
    best, where = biggest(rows, deal)
    return dict(w=g.w, h=g.h, flowers=Board(g).flowers, cocoons=Board(g).shut,
                par=par, ways=ways, nodes=nodes, proved=proved,
                careless=careless(rows, deal, par + 5) if proved and par else -1,
                bestBurst=best[0], bestWaves=best[1], bestFreed=best[2], bestAt=where)


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    print(survey(sys.argv[1].split(","), sys.argv[2]))

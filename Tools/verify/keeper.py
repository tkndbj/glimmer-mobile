"""Groovekeeper's rules, mirrored in Python so a grove can be proved with no Unity anywhere.

The shipping copy is `Assets/Game/Scripts/Domain/Modes/Lab/KeeperBoard.cs` and
`KeeperSolver.cs`; this is the mirror `content.py` and `Tools/chapters/k01_grovekeeper.py`
run, exactly as `fall.py` mirrors Lightfall. Invariant 9a applies in full: the two copies
are pinned against each other by `keeper-vectors.json`, which `content.py` runs through
this file and `KeeperVectorTests` runs through the C# one, so the burst rule cannot drift
quietly.

**The rule in one paragraph.** A tile is laid on bare ground orthogonally beside something
already standing. A tile whose own colour and its neighbours' between them carry all three
channels *blooms*. A **bed** is a cell that has to end up holding a bloomed tile; a
**heartbed** additionally refuses every colour but its own. The grove is finished when
every bed is open, and a run is over when the basket runs out or the grove has nowhere
left to grow.

**Par is the fewest tiles that open every bed**, where a tile is spent by being planted or
composted (spent without being planted, to bring the next colour round). Nothing here is
authored: a typed par drifts from the board it claims to describe, and the drift has no
symptom.
"""
import sys

NONE, R, G, B = 0, 1, 2, 4
ALL = R | G | B

LETTER = {R: "R", G: "G", B: "B", R | G: "Y", R | B: "M", G | B: "C", ALL: "W"}
MASK_OF = {"R": R, "G": G, "B": B}

#: What a prism is written as in a procession. It carries every channel.
PRISM = "P"

OPEN, STONE, BED = 0, 1, 2

#: Mirrors `KeeperLayout.MinWidth`/`MaxWidth`, which are the same both ways.
MIN_SIDE, MAX_SIDE = 4, 9

#: Mirrors `KeeperSolver`. See the C# for why each is the number it is.
NODE_BUDGET = 400_000
MAX_TILES = 20
MAX_WAYS = 4000

#: Mirrors `KeeperRules.DefaultSpare` and `LevelTuning`'s three factors, in hundredths.
DEFAULT_SPARE = 5
GOLD_HUNDREDTHS, SILVER_HUNDREDTHS, BUDGET_HUNDREDTHS = 120, 140, 160


def over(par, hundredths):
    """Ceiling of `par * hundredths/100` in integer arithmetic. Mirrors `LevelTuning.Over`."""
    return (par * hundredths + 99) // 100


def parse_deal(text):
    """The ordered procession, as a list of masks. Raises on a blend, as the game does."""
    tiles = []
    for i, c in enumerate(text or ""):
        if c in " \t_":
            continue
        if c == PRISM:
            tiles.append(ALL)
            continue
        if c not in MASK_OF:
            raise ValueError("'%s' at %d is not a tile; a deal is written in R, G, B and %s"
                             % (c, i, PRISM))
        tiles.append(MASK_OF[c])

    if not tiles:
        raise ValueError("a deal of nothing but spaces deals nothing")
    if len(tiles) > 48:
        raise ValueError("a deal of %d is longer than the 48 a procession may name" % len(tiles))
    return tiles


def parse_rows(rows, width=None, height=None):
    """The ground, the colour each bed insists on, and what is standing before anybody plays."""
    rows = list(rows or [])
    if not rows:
        raise ValueError("a grove has to be told what its ground is")

    h = len(rows)
    w = len(rows[0].replace(" ", ""))
    if width is not None and width != w:
        raise ValueError("declared %d columns and wrote %d" % (width, w))
    if height is not None and height != h:
        raise ValueError("declared %d rows and wrote %d" % (height, h))

    ground = [OPEN] * (w * h)
    wants = [NONE] * (w * h)
    sprigs = [NONE] * (w * h)

    for y, row in enumerate(rows):
        line = row.replace(" ", "")
        if len(line) != w:
            raise ValueError("row %d names %d cells, expected %d" % (y, len(line), w))

        for x, c in enumerate(line):
            at = y * w + x
            if c in ".-":
                pass
            elif c == "#":
                ground[at] = STONE
            elif c == "*":
                ground[at] = BED
            elif c in "rgb":
                ground[at] = BED
                wants[at] = MASK_OF[c.upper()]
            elif c in "RGB":
                sprigs[at] = MASK_OF[c]
            else:
                raise ValueError("'%s' at row %d column %d is not ground" % (c, y, x))

    return ground, wants, sprigs, w, h


class Grove(object):
    """One authored grove. The layout half — nothing here changes while a run is played."""

    def __init__(self, rows, deal):
        self.ground, self.wants, self.sprigs, self.width, self.height = parse_rows(rows)
        self.deal = parse_deal(deal)
        self.count = self.width * self.height

    @property
    def beds(self):
        return [i for i in range(self.count) if self.ground[i] == BED]

    @property
    def heartbeds(self):
        return [i for i in self.beds if self.wants[i] != NONE]

    @property
    def room(self):
        return sum(1 for i in range(self.count) if self.ground[i] != STONE)

    @property
    def sprig_count(self):
        return sum(1 for i in range(self.count) if self.sprigs[i] != NONE)

    @property
    def channels(self):
        mask = NONE
        for tile in self.deal:
            mask |= tile
        return mask

    @property
    def prisms(self):
        return sum(1 for tile in self.deal if tile == ALL)

    @property
    def wanted(self):
        mask = NONE
        for i in self.beds:
            mask |= self.wants[i]
        return mask

    def beside(self, index):
        x, y = index % self.width, index // self.width
        out = []
        if y > 0:
            out.append(index - self.width)
        if x < self.width - 1:
            out.append(index + 1)
        if y < self.height - 1:
            out.append(index + self.width)
        if x > 0:
            out.append(index - 1)
        return out

    def at(self, spent):
        return self.deal[spent % len(self.deal)]


class Board(object):
    """A grove being played on: the same rules the shipping `KeeperBoard` runs."""

    def __init__(self, grove, cells=None):
        self.grove = grove
        self.cells = list(cells) if cells is not None else list(grove.sprigs)

    def copy(self):
        return Board(self.grove, self.cells)

    def gathered(self, index, except_at=-1):
        if self.cells[index] == NONE:
            return NONE
        mask = self.cells[index]
        for n in self.grove.beside(index):
            if n != except_at:
                mask |= self.cells[n]
        return mask

    def bloomed(self, index):
        return self.gathered(index) == ALL

    def is_open(self, index):
        if self.grove.ground[index] != BED or not self.bloomed(index):
            return False
        wants = self.grove.wants[index]
        return wants == NONE or (self.cells[index] & wants) == wants

    @property
    def beds_left(self):
        return sum(1 for i in self.grove.beds if not self.is_open(i))

    @property
    def finished(self):
        return self.beds_left == 0

    def touching(self, index):
        return any(self.cells[n] != NONE for n in self.grove.beside(index))

    def can_plant(self, colour, index):
        if colour == NONE:
            return False
        if self.grove.ground[index] == STONE or self.cells[index] != NONE:
            return False
        wants = self.grove.wants[index]
        if self.grove.ground[index] == BED and wants != NONE and (colour & wants) != wants:
            return False
        return self.touching(index)

    @property
    def any_room(self):
        for i in range(self.grove.count):
            if self.grove.ground[i] == STONE or self.cells[i] != NONE:
                continue
            if self.touching(i):
                return True
        return False

    def openings(self, colour):
        return [i for i in range(self.grove.count) if self.can_plant(colour, i)]

    def preview(self, colour, index):
        if not self.can_plant(colour, index):
            return (0, 0, 0)
        self.cells[index] = colour
        gain = self._reading(index)
        self.cells[index] = NONE
        return gain

    def plant(self, colour, index):
        """Lays the tile and answers (blooms, beds, seams, cells)."""
        if not self.can_plant(colour, index):
            return (0, 0, 0, [])
        self.cells[index] = colour
        blooms, beds, seams, found = self._reading(index, want_cells=True)
        return (blooms, beds, seams, found)

    def _reading(self, index, want_cells=False):
        colour = self.cells[index]
        found = []
        seams = 0

        if self.bloomed(index):
            found.append(index)

        for at in self.grove.beside(index):
            mate = self.cells[at]
            if mate == NONE:
                continue
            if mate != colour:
                seams += 1
            # What it was before, with this tile taken back out. Asking whether it has all
            # three "except this colour" is the version that reads right and is wrong.
            if self.gathered(at) != ALL:
                continue
            if self.gathered(at, index) == ALL:
                continue
            found.append(at)

        beds = sum(1 for at in found if self.is_open(at))
        if want_cells:
            return (len(found), beds, seams, found)
        return (len(found), beds, seams)

    def any_bed_lost(self):
        """Certainties only: this under-reports and never over-reports. See the C#."""
        bare_beds = []
        for i in self.grove.beds:
            if self.is_open(i):
                continue
            if self.cells[i] != NONE:
                if not any(self._bare(n) for n in self.grove.beside(i)):
                    return True
                continue
            bare_beds.append(i)

        if not bare_beds:
            return False

        reached = set()
        queue = [i for i in range(self.grove.count) if self._bare(i) and self.touching(i)]
        reached.update(queue)

        head = 0
        while head < len(queue):
            at = queue[head]
            head += 1
            for n in self.grove.beside(at):
                if n not in reached and self._bare(n):
                    reached.add(n)
                    queue.append(n)

        return any(bed not in reached for bed in bare_beds)

    def _bare(self, index):
        return self.grove.ground[index] != STONE and self.cells[index] == NONE

    @property
    def seams(self):
        n = 0
        for y in range(self.grove.height):
            for x in range(self.grove.width):
                at = y * self.grove.width + x
                if self.cells[at] == NONE:
                    continue
                if x + 1 < self.grove.width and self._unlike(at, at + 1):
                    n += 1
                if y + 1 < self.grove.height and self._unlike(at, at + self.grove.width):
                    n += 1
        return n

    def _unlike(self, a, b):
        return self.cells[a] != NONE and self.cells[b] != NONE and self.cells[a] != self.cells[b]


# ---------------------------------------------------------------------------- the search
def _to_beds(grove):
    """Steps from every cell to the nearest bed, over ground a tile could ever stand on."""
    steps = [None] * grove.count
    queue = []
    for i in grove.beds:
        steps[i] = 0
        queue.append(i)

    head = 0
    while head < len(queue):
        at = queue[head]
        head += 1
        for n in grove.beside(at):
            if steps[n] is None and grove.ground[n] != STONE:
                steps[n] = steps[at] + 1
                queue.append(n)

    return [s if s is not None else 10 ** 6 for s in steps]


def search(grove):
    """(par, ways, nodes, proved). Mirrors `KeeperSolver.Survey` step for step."""
    board = Board(grove)
    if not grove.beds:
        return (0, 0, 0, True)
    if board.finished:
        return (0, 1, 0, True)
    if board.any_bed_lost():
        return (0, 0, 0, True)

    to_bed = _to_beds(grove)
    cells = board.cells
    state = {"nodes": 0, "ways": 0, "spent": False, "limit": 0}

    per_tile = 3 if grove.prisms else 1

    def channels(mask):
        return (1 if mask & R else 0) + (1 if mask & G else 0) + (1 if mask & B else 0)

    def around(index):
        mask = NONE
        for n in grove.beside(index):
            mask |= cells[n]
        return mask

    def near(a, b):
        return (abs(a % grove.width - b % grove.width)
                + abs(a // grove.width - b // grove.width)) <= 2

    def floor():
        live, needs, bares, furthest = [], [], [], 0

        for bed in grove.beds:
            if board.is_open(bed):
                continue

            live.append(bed)
            if cells[bed] != NONE:
                needs.append(channels(ALL & ~board.gathered(bed)))
                bares.append(False)
            else:
                needs.append(1 + max(0, channels(ALL & ~around(bed)) - per_tile))
                bares.append(True)
                furthest = max(furthest, steps_to(bed))

        if not live:
            return 0

        # Beds whose closed neighbourhoods touch may share a tile, so their costs are grouped
        # and only the worst of each group is counted; groups that cannot touch are added. See
        # `KeeperSolver.Cluster` for why that is still a floor.
        group = list(range(len(live)))
        moved = True
        while moved:
            moved = False
            for i in range(len(live)):
                for j in range(i + 1, len(live)):
                    if group[i] != group[j] and near(live[i], live[j]):
                        lo, hi = min(group[i], group[j]), max(group[i], group[j])
                        group = [lo if g == hi else g for g in group]
                        moved = True

        total = 0
        for i in range(len(live)):
            if group[i] != i:
                continue
            members = [j for j in range(len(live)) if group[j] == i]
            total += max(max(needs[j] for j in members),
                         sum(1 for j in members if bares[j]))

        return max(total, furthest)

    def steps_to(cell):
        """How few plantings could put a tile here: the straight-line walk from what stands."""
        cx, cy = cell % grove.width, cell // grove.width
        best = None
        for i in range(grove.count):
            if cells[i] == NONE:
                continue
            d = abs(i % grove.width - cx) + abs(i // grove.width - cy)
            if best is None or d < best:
                best = d
        return 1 if best is None else best

    seen = set()

    def walk(spent, composted):
        if state["spent"]:
            return
        state["nodes"] += 1
        if state["nodes"] > NODE_BUDGET:
            state["spent"] = True
            return

        key = (spent, tuple(cells))
        if key in seen:
            return
        seen.add(key)

        if board.finished:
            if spent == state["limit"] and state["ways"] < MAX_WAYS:
                state["ways"] += 1
            return

        if spent >= state["limit"]:
            return
        if spent + floor() > state["limit"]:
            return

        colour = grove.at(spent)
        room = state["limit"] - spent

        for at in board.openings(colour):
            if to_bed[at] > room:
                continue
            cells[at] = colour
            walk(spent + 1, 0)
            cells[at] = NONE
            if state["spent"]:
                return

        if composted < len(grove.deal) - 1:
            walk(spent + 1, composted + 1)

    limit = max(1, floor())
    while limit <= MAX_TILES:
        state["limit"] = limit
        state["ways"] = 0
        seen.clear()
        walk(0, 0)

        if state["spent"]:
            return (0, 0, state["nodes"], False)
        if state["ways"] > 0:
            return (limit, state["ways"], state["nodes"], True)
        limit += 1

    return (0, 0, state["nodes"], False)


def greedy(grove, budget):
    """What a player who never looks past this turn spends, or -1 if they never finish."""
    board = Board(grove)
    spent = 0
    ceiling = budget if 0 < budget < MAX_TILES * 4 else MAX_TILES * 4

    while spent < ceiling:
        if board.finished:
            return spent

        colour = grove.at(spent)
        options = board.openings(colour)
        if not options:
            spent += 1                       # composted
            continue

        best, best_gain = None, None
        for at in options:
            gain = board.preview(colour, at)
            if best is None or gain > best_gain:
                best, best_gain = at, gain

        board.plant(colour, best)
        spent += 1

    return spent if board.finished else -1


def survey(rows, deal):
    """Everything the offline gate wants to know about one grove, in one dict."""
    grove = Grove(rows, deal)
    par, ways, nodes, proved = search(grove)

    budget = par + DEFAULT_SPARE if par else 0

    return {
        "width": grove.width,
        "height": grove.height,
        "beds": len(grove.beds),
        "heartbeds": len(grove.heartbeds),
        "stone": sum(1 for i in range(grove.count) if grove.ground[i] == STONE),
        "room": grove.room,
        "sprigs": grove.sprig_count,
        "prisms": grove.prisms,
        "par": par,
        "ways": ways,
        "nodes": nodes,
        "proved": proved,
        "greedy": greedy(grove, budget) if proved and par else -1,
    }


if __name__ == "__main__":
    sys.setrecursionlimit(10000)
    print(survey(["......", "..G...", ".R*B..", "......"], "GRB"))

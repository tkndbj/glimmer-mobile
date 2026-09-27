# -*- coding: utf-8 -*-
"""Designs, proves, tunes and prints the glade genre of the daily challenges.

    python Tools/make_glade_challenges.py --report            # every glade row in challenges.json
    python Tools/make_glade_challenges.py --report --id d23_glade
    python Tools/make_glade_challenges.py --draft medium --seed 7 --count 5   # candidate boards
    python Tools/make_glade_challenges.py --tune d23_glade    # propose a hill and waves
    python Tools/make_glade_challenges.py --write [--id d23_glade]            # and write them

**Why this exists.** A glade challenge is a board a player has to be able to *read*, and the
two gates that already hold a row say nothing about that. `GladePuzzle.Fault` proves the row
parses and its authored solution wakes every critter; `ChallengeTests` plays every row with a
bot and proves it is winnable against the real hill. Neither asks the questions a designer asks
of a pipe puzzle, and those are the ones this answers, as counts rather than as opinions
(invariant 5d):

* **How many arrangements mate every arm, and how many of those win.** A player solves a glade
  by making every arm meet an arm. If two mated arrangements both wake every critter the board
  has two answers and the player cannot reason to either. `winning` must be **one** on every
  shipped row.
* **How much of the board falls to logic alone.** Arc consistency over the arms, with no guess
  and no colour: the share of tiles it settles. A medium row is mostly settled by looking; a
  hard row is not.
* **How slow a player can be and still win, and whether the hill is ever empty.** The bot
  wakes the critters in the order the raiders arrive, turning each network home crystal-first
  (`ChallengeTests`' bot, mirrored); the *slack* is how many times its turn count a player can
  take, every critter waking that much later, before the line falls. `--write` tunes each row's
  hill and waves to its tier's `TARGET` as one stream per colour (`streams`), refusing any
  tuning that leaves the hill empty on a single one of the bot's turns (the owner, 2026-09-26:
  "there are times that there are no enemies on the hill").

**Light never mixes on a challenge glade** (invariant 56l): an amber critter is lit by an amber
crystal, and two colours meeting on one network put it out (`Puzzle(blends: false)`), which is
what this mirror's `evaluate` does and what `faults` refuses in a solution. **A lit critter's
fire does not bank** (`ChallengeHill.Volley`), which is what `Hill.stream` does.

**It is a mirror** of `Puzzle.Evaluate`, `Puzzle.Alike`, `Puzzle.TurnsToSolution` and
`ChallengeHill.Resolve`, so the C# is the authority whenever the two disagree: a row is not
shipped until `content.py` and `python Tools/verify/tests.py ChallengeTests` are both green.
`--write` rewrites a row's `hill` and `waves` and nothing else, in the file's own layout; the
boards themselves are authored, and the file says what it draws (7c's shape).
"""
from __future__ import annotations

import argparse
import json
import random
import re
import sys
from collections import deque
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONTENT = REPO / "Assets" / "StreamingAssets" / "Content"
FILE = CONTENT / "challenges.json"

N, E, S, W = 1, 2, 4, 8
BITS = [N, E, S, W]
STEP = [(0, -1), (1, 0), (0, 1), (-1, 0)]          # Puzzle.Step
ARM = "NESW"
R, G, B = 1, 2, 4
Y = R | G
COLOUR = {"R": R, "G": G, "B": B, "Y": Y}
LETTER = {v: k for k, v in COLOUR.items()}
LANE = {R: 0, G: 1, B: 2, Y: 3}                    # GladePuzzle.LaneOf
LANES = "rgby"                                     # ChallengeColours.Letters

#: `challenges.json`'s line block, the one every row is fought on.
LINE = {"damage": 1, "health": 3, "strike": 1}


# ------------------------------------------------------------------ tiles
def rotl(mask, turns):
    turns &= 3
    out = 0
    for i in range(4):
        if mask & (1 << i):
            out |= 1 << ((i + turns) & 3)
    return out


def arms_of(mask):
    return "".join(ARM[d] for d in range(4) if mask & BITS[d])


def pop(mask):
    return bin(mask).count("1")


class Cell:
    """One tile, as `LevelGridParser` reads it. `rot` is quarter turns clockwise from solved."""

    __slots__ = ("kind", "solved", "cross", "gate", "colour", "rot", "locked", "link")

    def __init__(self, kind, solved, cross=0, gate=0, colour=0, rot=0, locked=False, link=0):
        self.kind, self.solved, self.cross, self.gate = kind, solved, cross, gate
        self.colour, self.rot, self.locked, self.link = colour, rot, locked, link

    def copy(self):
        return Cell(self.kind, self.solved, self.cross, self.gate, self.colour, self.rot,
                    self.locked, self.link)

    def alike(self, turns):
        """`Puzzle.Alike`: turned this far from its solution, is it indistinguishable from it."""
        if rotl(self.solved, turns) != self.solved:
            return False
        if self.gate:
            return rotl(self.gate, turns) == self.gate
        if not self.cross:
            return True
        strand = rotl(self.cross, turns)
        return strand == self.cross or strand == (self.solved & ~self.cross & 15)

    def inert(self):
        return self.alike(1)

    def key(self, rot):
        """What a player can see at this rotation: the arms, and which pair is which."""
        mask = rotl(self.solved, rot)
        if self.gate:
            return (mask, rotl(self.gate, rot))
        if self.cross:
            a = rotl(self.cross, rot)
            return (mask, min(a, mask & ~a & 15))
        return (mask,)

    def token(self):
        head = {"pipe": "-", "cross": "=", "briar": "%", "source": "*", "lamp": "@"}[self.kind]
        if self.kind == "cross":
            body = arms_of(self.cross) + "+" + arms_of(self.solved & ~self.cross & 15)
        elif self.kind == "briar":
            body = arms_of(self.gate) + "+" + arms_of(self.solved & ~self.gate & 15)
        else:
            body = arms_of(self.solved)
        colour = "#" + LETTER[self.colour] if self.kind in ("source", "lamp") else ""
        tail = "!" if self.locked else ""
        link = "&" + chr(ord("A") + self.link - 1) if self.link else ""
        return f"{head}{body}{colour}/{self.rot}{tail}{link}"


def read_arms(tok, p):
    mask = 0
    while p < len(tok) and tok[p] in ARM:
        mask |= BITS[ARM.index(tok[p])]
        p += 1
    return mask, p


def parse(tok):
    if tok == ".":
        return None
    kind = {"-": "pipe", "=": "cross", "%": "briar", "*": "source", "@": "lamp"}[tok[0]]
    mask, p = read_arms(tok, 1)
    cross = gate = 0
    if p < len(tok) and tok[p] == "+":
        second, p = read_arms(tok, p + 1)
        if kind == "cross":
            cross = mask
        else:
            gate = mask
        mask |= second
    colour = 0
    if p < len(tok) and tok[p] == "#":
        colour = COLOUR[tok[p + 1]]
        p += 2
    rot = 0
    if p < len(tok) and tok[p] == "/":
        rot = int(tok[p + 1])
        p += 2
    locked = False
    if p < len(tok) and tok[p] == "!":
        locked, p = True, p + 1
    if p < len(tok) and tok[p] == "~":
        raise ValueError(f"'{tok}' is brittle; a challenge conduit cannot crumble")
    link = 0
    if p < len(tok) and tok[p] == "&":
        link = ord(tok[p + 1]) - ord("A") + 1
    return Cell(kind, mask, cross, gate, colour, rot, locked, link)


# ------------------------------------------------------------------ the board
class Board:
    def __init__(self, w, h, cells):
        self.w, self.h, self.cells = w, h, cells

    @staticmethod
    def of_row(row):
        cells = []
        for line in row["rows"]:
            cells.extend(parse(t) for t in line.split())
        return Board(row["width"], row["height"], cells)

    def rows(self):
        out = []
        for y in range(self.h):
            out.append(" ".join(c.token() if c else "." for c in self.cells[y * self.w:(y + 1) * self.w]))
        return out

    def copy(self):
        return Board(self.w, self.h, [c.copy() if c else None for c in self.cells])

    def used(self):
        return [i for i, c in enumerate(self.cells) if c]

    def neighbour(self, i, d):
        x, y = i % self.w + STEP[d][0], i // self.w + STEP[d][1]
        if 0 <= x < self.w and 0 <= y < self.h:
            return y * self.w + x
        return -1

    def groups(self):
        roots = {}
        for i, c in enumerate(self.cells):
            if c and c.link:
                roots.setdefault(c.link, []).append(i)
        return roots

    # -------------------------------------------------------------- the light
    def evaluate(self, rots=None):
        """`Puzzle.Evaluate` over strands. -> (comp, colours, depth, lit, won)."""
        cells, n = self.cells, len(self.cells)
        rot = (lambda i: rots[i]) if rots is not None else (lambda i: cells[i].rot)

        def live(i):
            c = cells[i]
            return rotl(c.gate or c.solved, rot(i))

        def strand_at(i, d):
            c = cells[i]
            if not c.cross:
                return 0
            return 0 if rotl(c.cross, rot(i)) & BITS[d] else 1

        def strands(i):
            return 2 if cells[i].cross else 1

        comp = [-1] * (n * 2)
        colours = []
        for start in range(n * 2):
            i, st = divmod(start, 2)
            if not cells[i] or st >= strands(i) or comp[start] != -1:
                continue
            g, colour, clash = len(colours), 0, False
            q = deque([start])
            comp[start] = g
            while q:
                node = q.popleft()
                a, sa = divmod(node, 2)
                if cells[a].kind == "source":
                    # `Puzzle(blends: false)`: a network carries its crystals' one colour, and
                    # two colours meeting put it out rather than mixing.
                    if not colour:
                        colour = cells[a].colour
                    elif colour != cells[a].colour:
                        clash = True
                ma = live(a)
                for d in range(4):
                    if not ma & BITS[d] or strand_at(a, d) != sa:
                        continue
                    b = self.neighbour(a, d)
                    if b < 0 or not cells[b]:
                        continue
                    back = (d + 2) & 3
                    if not live(b) & BITS[back]:
                        continue
                    into = b * 2 + strand_at(b, back)
                    if comp[into] == -1:
                        comp[into] = g
                        q.append(into)
            colours.append(0 if clash else colour)

        depth = [-1] * (n * 2)
        q = deque()
        for i in range(n):
            if cells[i] and cells[i].kind == "source":
                depth[i * 2] = 0
                q.append(i * 2)
        while q:
            node = q.popleft()
            a, sa = divmod(node, 2)
            ma = live(a)
            for d in range(4):
                if not ma & BITS[d] or strand_at(a, d) != sa:
                    continue
                b = self.neighbour(a, d)
                if b < 0 or not cells[b]:
                    continue
                back = (d + 2) & 3
                if not live(b) & BITS[back]:
                    continue
                into = b * 2 + strand_at(b, back)
                if depth[into] < 0:
                    depth[into] = depth[node] + 1
                    q.append(into)
        cell_depth = []
        for i in range(n):
            ds = [depth[i * 2 + s] for s in range(2 if cells[i] and cells[i].cross else 1) if depth[i * 2 + s] >= 0]
            cell_depth.append(min(ds) if ds else -1)

        lit, won, lamps = {}, True, 0
        for i in range(n):
            c = cells[i]
            if not c or c.kind != "lamp":
                continue
            lamps += 1
            have = colours[comp[i * 2]] if comp[i * 2] >= 0 else 0
            lit[i] = have == c.colour
            won = won and lit[i]
        return comp, colours, cell_depth, lit, won and lamps > 0

    def solved_rots(self):
        return [0] * len(self.cells)

    # -------------------------------------------------------------- turns
    def owed_alone(self, i, rot=None):
        c = self.cells[i]
        r = c.rot if rot is None else rot
        for k in range(4):
            if c.alike(r + k):
                return k
        return 0

    def owed(self, i, rots=None):
        """`Puzzle.TurnsOwed`: for a bound conduit, the turns its whole taproot owes."""
        c = self.cells[i]
        rot = (lambda j: self.cells[j].rot) if rots is None else (lambda j: rots[j])
        if not c.link:
            return self.owed_alone(i, rot(i))
        members = self.groups()[c.link]
        for k in range(4):
            if all(self.cells[j].alike(rot(j) + k) for j in members):
                return k
        return self.owed_alone(i, rot(i))

    def can_turn(self, i):
        c = self.cells[i]
        if not c or c.locked:
            return False
        if c.link:
            return not all(self.cells[j].inert() for j in self.groups()[c.link])
        return not c.inert()

    def par(self):
        """`Puzzle.TurnsToSolution` on the dealt board (every shipped tile is in a network)."""
        _, _, sdepth, _, _ = self.evaluate(self.solved_rots())
        _, _, depth, _, _ = self.evaluate()
        total, counted = 0, set()
        for i, c in enumerate(self.cells):
            if not c or (sdepth[i] < 0 and depth[i] < 0):
                continue
            if c.link:
                if c.link in counted:
                    continue
                counted.add(c.link)
            total += self.owed(i)
        return total

    def turnable(self):
        return sum(1 for i in self.used() if self.can_turn(i))

    # -------------------------------------------------------------- the arrangements
    def arrangements(self, cap=20000):
        """Every arrangement in which every arm meets an arm, and which of them win.

        Row-major backtracking, each tile over its distinct looks (`Cell.key`), a taproot as
        one variable. -> (mated, winning, exhausted)."""
        w, h, cells = self.w, self.h, self.cells
        order = self.used()
        groups = self.groups()

        def domain(i):
            c = cells[i]
            if c.locked:
                return [0]
            seen, out = set(), []
            for r in range(4):
                k = c.key(r)
                if k not in seen:
                    seen.add(k)
                    out.append(r)
            return out

        group_domain = {}
        for link, members in groups.items():
            seen, out = set(), []
            for r in range(4):
                k = tuple(cells[j].key(r) for j in members)
                if k not in seen:
                    seen.add(k)
                    out.append(r)
            group_domain[link] = out

        rots = [0] * len(cells)
        assigned = [False] * len(cells)
        mated = [0]
        wins = []
        exhausted = [True]

        def fits(i):
            mask = rotl(cells[i].solved, rots[i])
            for d in range(4):
                j = self.neighbour(i, d)
                mine = bool(mask & BITS[d])
                if j < 0 or not cells[j]:
                    if mine:
                        return False
                    continue
                if not assigned[j]:
                    continue
                theirs = bool(rotl(cells[j].solved, rots[j]) & BITS[(d + 2) & 3])
                if mine != theirs:
                    return False
            return True

        def walk(k):
            if mated[0] >= cap:
                exhausted[0] = False
                return
            if k == len(order):
                mated[0] += 1
                if self.evaluate(rots)[4]:
                    wins.append(list(rots))
                return
            i = order[k]
            c = cells[i]
            if assigned[i]:
                if fits(i):
                    walk(k + 1)
                return
            if c.link:
                members = groups[c.link]
                for r in group_domain[c.link]:
                    for j in members:
                        rots[j] = r
                        assigned[j] = True
                    # only the members already reached in the walk can be checked now
                    if fits(i):
                        walk(k + 1)
                    for j in members:
                        assigned[j] = False
                return
            for r in domain(i):
                rots[i] = r
                assigned[i] = True
                if fits(i):
                    walk(k + 1)
                assigned[i] = False

        # A member assigned by its taproot ahead of the walk must not be tested against
        # neighbours the walk has not reached, so `fits` only reads assigned neighbours and
        # every member is re-tested when the walk arrives at it.
        walk(0)
        return mated[0], wins, exhausted[0]

    def logic_share(self):
        """The share of turnable tiles arc consistency settles with no guess and no colour."""
        cells = self.cells
        dom = {}
        for i in self.used():
            c = cells[i]
            dom[i] = {0} if c.locked else {0, 1, 2, 3}
        groups = self.groups()

        def arm(i, r, d):
            return bool(rotl(cells[i].solved, r) & BITS[d])

        changed = True
        while changed:
            changed = False
            for i in dom:
                keep = set()
                for r in dom[i]:
                    ok = True
                    for d in range(4):
                        j = self.neighbour(i, d)
                        if j < 0 or not cells[j]:
                            if arm(i, r, d):
                                ok = False
                                break
                            continue
                        back = (d + 2) & 3
                        if not any(arm(j, rj, back) == arm(i, r, d) for rj in dom[j]):
                            ok = False
                            break
                    if ok:
                        keep.add(r)
                if keep != dom[i]:
                    dom[i] = keep
                    changed = True
            for members in groups.values():
                common = set.intersection(*(dom[j] for j in members))
                for j in members:
                    if dom[j] != common:
                        dom[j] = set(common)
                        changed = True
        turnable = [i for i in self.used() if self.can_turn(i)]
        settled = sum(1 for i in turnable if len({cells[i].key(r) for r in dom[i]}) == 1)
        return settled / max(1, len(turnable))

    def census(self):
        cs = [c for c in self.cells if c]
        lamps = [c for c in cs if c.kind == "lamp"]
        return {
            "tiles": len(cs),
            "fill": len(cs) / (self.w * self.h),
            "crystals": sum(1 for c in cs if c.kind == "source"),
            "critters": len(lamps),
            "lanes": sorted({LANE[c.colour] for c in lamps}),
            "amber": sum(1 for c in lamps if c.colour == Y),
            "bridges": sum(1 for c in cs if c.kind == "cross" and c.inert()),
            "twists": sum(1 for c in cs if c.kind == "cross" and not c.inert()),
            "briars": sum(1 for c in cs if c.kind == "briar"),
            "roots": len(self.groups()),
            "rooted": sum(1 for c in cs if c.locked),
        }


# ------------------------------------------------------------------ proving a row
def faults(board):
    """The chapter validator's rules, asked of a challenge row (`LevelValidator.ValidateGlade`
    and `GladePuzzle.Fault`). Every string is a reason not to ship."""
    out = []
    cells = board.cells
    for i in board.used():
        c = cells[i]
        if c.kind in ("source", "lamp") and c.colour not in LANE:
            out.append(f"{i % board.w},{i // board.w} wants a light no turret fires")
        for d in range(4):
            if not c.solved & BITS[d]:
                continue
            j = board.neighbour(i, d)
            if j < 0 or not cells[j] or not cells[j].solved & BITS[(d + 2) & 3]:
                out.append(f"arm at {i % board.w},{i // board.w} meets nothing")
        if c.locked and c.rot != 0:
            out.append(f"rooted tile at {i % board.w},{i // board.w} is not authored at /0")
    for link, members in board.groups().items():
        if len(members) < 2:
            out.append(f"taproot {link} binds one conduit")
        if not any(all(cells[j].alike(cells[j].rot + k) for j in members) for k in range(4)):
            out.append(f"taproot {link} can never be right at once")
    zero = board.solved_rots()
    comp, colours, _, _, won = board.evaluate(zero)
    if not won:
        out.append("the solution does not wake every critter")
        return out
    for i in board.used():
        c = cells[i]
        if c.kind == "source" and not colours[comp[i * 2]]:
            out.append(f"crystal at {i % board.w},{i // board.w} meets another colour in the solution; "
                       "a challenge glade never mixes light")
        if c.kind == "cross":
            if comp[i * 2] == comp[i * 2 + 1]:
                out.append(f"crossing at {i % board.w},{i // board.w} crosses nothing")
            elif not colours[comp[i * 2]] and not colours[comp[i * 2 + 1]]:
                out.append(f"crossing at {i % board.w},{i // board.w} carries no light")
        if c.kind in ("cross", "briar") and not c.locked and not c.inert():
            turned = list(zero)
            turned[i] = 1
            if board.evaluate(turned)[4]:
                out.append(f"the {c.kind} at {i % board.w},{i // board.w} is settled by nothing")
    if board.evaluate()[4]:
        out.append("dealt already solved")
    if board.par() <= 0:
        out.append("dealt with no turn owed")
    return out


# ------------------------------------------------------------------ the hill
class Hill:
    """`ChallengeHill`, line for line: fire, step, strike, muster. A glade's lit critter
    feeds a *volley* (`ChallengeHill.Volley`), which fires this turn or is spent: nothing a
    glade pays ever banks (invariant 56l)."""

    def __init__(self, length, waves):
        self.length = max(1, length)
        self.waves = waves                      # [(turn, [(lane, health)])]
        self.wards = [[LINE["health"], 0] for _ in range(4)]   # health, banked
        self.volley = [0, 0, 0, 0]
        self.raiders = []                       # [lane, health, distance]
        self.turn = 0
        self.muster(0)

    def standing(self):
        return any(w[0] > 0 for w in self.wards)

    def present(self):
        """Raiders alive on the hill."""
        return sum(1 for r in self.raiders if r[1] > 0)

    def feed(self, lane, bolts):
        if self.wards[lane][0] > 0 and bolts > 0:
            self.wards[lane][1] += bolts

    def stream(self, lane, bolts):
        if self.wards[lane][0] > 0 and bolts > 0:
            self.volley[lane] += bolts

    def resolve(self):
        self.turn += 1
        for lane, ward in enumerate(self.wards):
            volley, self.volley[lane] = self.volley[lane], 0
            if ward[0] <= 0:
                continue
            while volley > 0 or ward[1] > 0:
                target = None
                for r in self.raiders:
                    if r[1] > 0 and r[0] == lane and (target is None or r[2] < target[2]):
                        target = r
                if target is None:
                    break
                if volley > 0:
                    volley -= 1
                else:
                    ward[1] -= 1
                target[1] = max(0, target[1] - LINE["damage"])
        for r in self.raiders:
            if r[1] > 0 and r[2] > 0:
                r[2] -= 1
        for r in self.raiders:
            if r[1] <= 0 or r[2] > 0:
                continue
            at = self.target(r[0])
            if at < 0:
                break
            ward = self.wards[at]
            ward[0] = max(0, ward[0] - LINE["strike"])
            if ward[0] <= 0:
                ward[1] = 0
        self.muster(self.turn)

    def target(self, lane):
        if self.wards[lane][0] > 0:
            return lane
        best, gap = -1, 99
        for i, w in enumerate(self.wards):
            if w[0] > 0 and abs(i - lane) < gap:
                best, gap = i, abs(i - lane)
        return best

    def muster(self, turn):
        for t, raiders in self.waves:
            if t == turn:
                for lane, health in raiders:
                    self.raiders.append([lane, health, self.length])


def read_waves(texts):
    out = []
    for text in texts:
        parts = text.split()
        out.append((int(parts[0]), [(LANES.index(t[0]), int(t[1:])) for t in parts[1:]]))
    return out


def lane_order(board, waves):
    """The bot's order: the lane whose first raider musters earliest wakes first."""
    first = {}
    for t, raiders in waves:
        for lane, _ in raiders:
            first.setdefault(lane, t)
    present = sorted({LANE[c.colour] for c in board.cells if c and c.kind == "lamp"})
    return sorted(present, key=lambda l: (first.get(l, 1 << 30), l))


def bot(board, waves):
    """`ChallengeTests`' bot: wake critters lane by lane in raider order, each one's solved
    network turned home crystal-first. -> list of lit-lane counts after each tap, one per turn."""
    b = board.copy()
    zero = b.solved_rots()
    comp, _, sdepth, _, _ = b.evaluate(zero)
    schedule = []
    for lane in lane_order(b, waves):
        for lamp, c in enumerate(b.cells):
            if not c or c.kind != "lamp" or LANE[c.colour] != lane:
                continue
            group = comp[lamp * 2]
            net = [i for i in b.used() if any(comp[i * 2 + s] == group for s in range(2 if b.cells[i].cross else 1))]
            net.sort(key=lambda i: (sdepth[i], i))
            for i in net:
                for _ in range(b.owed(i)):
                    members = b.groups()[b.cells[i].link] if b.cells[i].link else [i]
                    for j in members:
                        b.cells[j].rot = (b.cells[j].rot + 1) & 3
                    _, _, _, lit, won = b.evaluate()
                    feeds = [0, 0, 0, 0]
                    for k, on in lit.items():
                        if on:
                            feeds[LANE[b.cells[k].colour]] += 1
                    schedule.append((feeds, won))
                    if won:
                        return schedule
    return schedule


def run(schedule, hill_length, waves, pace=100, bolts=1):
    """`ChallengeTests.HoldsAt`, line for line: the bot's run at `pace` hundredths of its
    speed against a fresh hill - the player's k-th turn has done what the bot had done by
    turn k*100/pace, and the solving turn never walks.

    -> (won, health left, turns walked with nobody on the hill, raiders on the hill summed
    over the walked turns, turns walked)."""
    hill = Hill(hill_length, waves)
    total = len(schedule)
    finish = max(total, (total * pace + 99) // 100)
    empty = crowd = walked = 0
    for k in range(1, finish):
        progress = min(total - 1, k * 100 // pace)
        if progress >= 1:
            feeds = schedule[progress - 1][0]
            for lane in range(4):
                hill.stream(lane, feeds[lane] * bolts)
        on = hill.present()
        empty += on == 0
        crowd += on
        walked += 1
        hill.resolve()
        if not hill.standing():
            return False, 0, empty, crowd, walked
    return True, sum(w[0] for w in hill.wards), empty, crowd, walked


def play(board, row, pace=100, schedule=None):
    """The bot's run over a row at `pace` hundredths of its turn count. -> `run`'s answer."""
    waves = read_waves(row["waves"])
    if schedule is None:
        schedule = bot(board, waves)
    return run(schedule, row["hill"], waves, pace, max(1, row.get("bolts", 1)))


def slack_of(schedule, hill_length, waves, bolts=1):
    """The slowest pace, in hundredths of the bot's, that still wins."""
    best = 100
    for p in range(105, 501, 5):
        if not run(schedule, hill_length, waves, p, bolts)[0]:
            break
        best = p
    return best


def slack(board, row, schedule=None):
    """The slowest pace, in hundredths of the bot's, that still wins a row."""
    waves = read_waves(row["waves"])
    if schedule is None:
        schedule = bot(board, waves)
    return slack_of(schedule, row["hill"], waves, max(1, row.get("bolts", 1)))


# ------------------------------------------------------------------ tuning the hill
#: The slowest pace, in hundredths of the bot's turns, a row is tuned to forgive: a player who
#: takes this many times the bot's turns still wins, and one much slower does not. A board
#: carrying all four lanes is a hard row. **Tightened on 2026-09-26** at the owner's word ("I can
#: rotate conduits a lot"): the first cut forgave 1.6-2.0x. `ChallengeTests` holds every row to
#: `SLACK_BAND` (`SlowestPaceFloor` / `SlowestPaceCeiling`), so a retune that drifts out of it
#: fails a gate rather than a player.
TARGET = {"medium": 150, "hard": 135}
SLACK_BAND = (125, 165)

#: How long the streams run, in multiples of the bot's turns: past anybody still standing at
#: the slowest pace a row forgives, so the hill is never emptied by the waves running out.
STREAM_END = 1.75

#: The shortest hill a row is tuned to, in steps: `d08_glade`'s, after the owner asked for
#: smaller steps on a taller hill (2026-09-26, 10 -> 16). A shorter hill is a bigger step.
LEAST_HILL = 16


def tier_of(board):
    lanes = {LANE[c.colour] for c in board.cells if c and c.kind == "lamp"}
    return "hard" if len(lanes) == 4 else "medium"


def wake_turns(schedule, lanes):
    """The bot's turn on which each lane first fires."""
    woke = {}
    for k, (feeds, _) in enumerate(schedule, 1):
        for lane in lanes:
            if lane not in woke and feeds[lane] > 0:
                woke[lane] = k
    return woke


def streams(order, woke, hill_length, period, health, lead, end):
    """**Every colour is a stream, opened in the order a good player answers them.**

    The first lane sends its first raider before the first move; every other lane opens so
    that its first raider reaches the line `lead` times the bot's turn for waking it, and each
    then sends a raider every `period` turns to the end. So the hill is never empty, a lit
    turret always has something walking into its fire, and the lane still asleep is the one
    marching - which is the whole of what the hill is for.
    -> [(turn, [(lane, health)])]"""
    at, opened = {}, -1
    for n, lane in enumerate(order):
        # Strictly after the lane before, or the bot (which wakes lanes in the order their
        # first raider musters, ties to the lower lane) would answer a different plan.
        t = 0 if n == 0 else max(opened + 1, int(round(woke[lane] * lead)) - hill_length)
        opened = t
        while t <= end:
            at.setdefault(t, []).append((lane, health))
            t += period
    return sorted(at.items())


def wave_strings(waves):
    return [f"{t} " + " ".join(f"{LANES[lane]}{hp}" for lane, hp in raiders) for t, raiders in waves]


def tune(board, target=None):
    """The hill and the waves that put a row's slack nearest its tier's `TARGET`, with the
    hill never empty at the bot's pace and the bot's line whole but for one blow.

    Searched: which lane opens first (every order - the bot wakes lanes in the order their
    first raider musters, so the order *is* the plan the hill asks for), the hill's length
    (.35-.7 of the bot's turns and never under `LEAST_HILL`: a raider met on the walk, a step
    kept small), a raider every
    5-10 turns per lane, two or three bolts a raider, and how late a lane opens. Among the
    tunings that hit the target (to a notch of 6), the most crowded hill wins (up to three
    raiders standing on an average turn), then the sturdier raider (a fight you can watch beats
    a body that falls as it arrives), then a hill nearest half the bot's turns.
    -> (row fields, reading) or None."""
    from itertools import permutations

    tier = tier_of(board)
    target = target or TARGET[tier]
    lanes = sorted({LANE[c.colour] for c in board.cells if c and c.kind == "lamp"})
    best = None
    for order in permutations(lanes):
        schedule = bot(board, [(i, [(lane, 1)]) for i, lane in enumerate(order)])
        if not schedule or not schedule[-1][1]:
            continue
        total = len(schedule)
        woke = wake_turns(schedule, order)
        if len(woke) < len(order):
            continue
        end = int(total * STREAM_END)
        for hill_length in range(max(LEAST_HILL, int(total * .35)), max(LEAST_HILL, int(total * .7)) + 1):
            for period in (5, 6, 8, 10):
                for health in (3, 2):
                    for lead in (1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.8):
                        waves = streams(order, woke, hill_length, period, health, lead, end)
                        won, left, empty, crowd, walked = run(schedule, hill_length, waves)
                        if not won or left < 11 or empty:
                            continue
                        got = slack_of(schedule, hill_length, waves)
                        if abs(got - target) > 10:
                            continue
                        density = crowd / max(1, walked)
                        score = (abs(got - target) // 6, -min(density, 3.0), -health,
                                 abs(hill_length - total * .5))
                        if best is None or score < best[0]:
                            best = (score, {"hill": hill_length, "bolts": 1, "waves": wave_strings(waves)},
                                    {"tier": tier, "target": target, "slack": got, "turns": total,
                                     "order": "".join(LANES[lane] for lane in order),
                                     "density": density, "period": period, "health": health,
                                     "lead": lead})
    return None if best is None else (best[1], best[2])


# ------------------------------------------------------------------ drafting a board
class Net:
    def __init__(self, colour):
        self.colour = colour
        self.arms = {}          # cell -> arm mask in this network
        self.sources = {}       # cell -> crystal colour
        self.order = []         # cells in the order they were grown

    def degree(self, i):
        return pop(self.arms.get(i, 0))


def draft(rng, w, h, spec):
    """A candidate board: networks grown from their crystals, bridged, pruned to their leaves.

    `spec`: colours (a list of 'R','G','B','Y'), leaves (a (lo, hi) per network), bridges,
    twists, briars, taproots (a list of group sizes), rooted, bias (how strongly growth
    extends the newest tip, which is what makes a network a winding path rather than a bush)."""
    size = w * h
    owner = [[] for _ in range(size)]            # cell -> [net index]
    frozen = set()
    nets = [Net(COLOUR[c]) for c in spec["colours"]]

    def nb(i, d):
        x, y = i % w + STEP[d][0], i // w + STEP[d][1]
        return y * w + x if 0 <= x < w and 0 <= y < h else -1

    # Seeds, spread apart.
    free = list(range(size))
    rng.shuffle(free)
    seeds = []
    for net in nets:
        for i in free:
            if i in seeds:
                continue
            if all(abs(i % w - s % w) + abs(i // w - s // w) >= spec.get("spread", 3) for s in seeds):
                seeds.append(i)
                break
        else:
            return None
    for k, net in enumerate(nets):
        i = seeds[k]
        net.arms[i] = 0
        net.sources[i] = net.colour
        net.order.append(i)
        owner[i].append(k)

    bridges, twists = spec.get("bridges", 0), spec.get("twists", 0)

    def moves(k, i):
        """Every step network `k` could take from cell `i`: onto a free cell, or across
        another network's two-armed pass as a bridge (straight) or a twist (an elbow)."""
        net = nets[k]
        out = []
        for d in range(4):
            j = nb(i, d)
            if j < 0:
                continue
            if not owner[j]:
                out.append(("free", i, d, j, None))
                continue
            if len(owner[j]) != 1 or j in frozen or owner[j][0] == k:
                continue
            other = nets[owner[j][0]]
            if j in other.sources or other.degree(j) != 2:
                continue
            theirs, back = other.arms[j], (d + 2) & 3
            if theirs & BITS[back]:
                continue
            straight = theirs in (N | S, E | W)
            if straight:
                exit_ = d
            else:
                rest = 15 & ~theirs & ~BITS[back]
                exit_ = [e for e in range(4) if rest & BITS[e]][0]
            far = nb(j, exit_)
            if far < 0 or owner[far]:
                continue
            out.append(("bridge" if straight else "twist", i, d, j, (exit_, far)))
        return out

    def grow(k):
        nonlocal bridges, twists
        net = nets[k]
        tips = [i for i in reversed(net.order) if i not in frozen and net.degree(i) < 3
                and not (i in net.sources and net.degree(i) >= spec.get("crystalArms", 3))]
        if not tips:
            return False
        near = tips[:2] if rng.random() < spec.get("bias", 0.8) else tips
        options = [m for i in near for m in moves(k, i)]
        if not options:
            options = [m for i in tips for m in moves(k, i)]
        if not options:
            return False
        crossings = [m for m in options if (m[0] == "bridge" and bridges > 0) or (m[0] == "twist" and twists > 0)]
        frees = [m for m in options if m[0] == "free"]
        if crossings and (not frees or rng.random() < spec.get("crossChance", 0.35)):
            kind, i, d, j, (exit_, far) = rng.choice(crossings)
            back = (d + 2) & 3
            net.arms[i] |= BITS[d]
            net.arms[j] = BITS[back] | BITS[exit_]
            net.arms[far] = BITS[(exit_ + 2) & 3]
            owner[j].append(k)
            owner[far].append(k)
            net.order += [j, far]
            frozen.add(j)
            if kind == "bridge":
                bridges -= 1
            else:
                twists -= 1
            return True
        if not frees:
            return False
        _, i, d, j, _ = rng.choice(frees)
        net.arms[i] |= BITS[d]
        net.arms[j] = BITS[(d + 2) & 3]
        net.order.append(j)
        owner[j].append(k)
        return True

    alive = set(range(len(nets)))
    while alive:
        for k in list(alive):
            if not grow(k):
                alive.discard(k)

    def spur(net, leaf):
        """How long the dead end at a leaf is, back to the first fork or crystal."""
        n, prev, at = 0, -1, leaf
        while True:
            n += 1
            nxt = [nb(at, d) for d in range(4) if net.arms[at] & BITS[d] and nb(at, d) != prev]
            if len(nxt) != 1 or net.degree(nxt[0]) != 2 or nxt[0] in net.sources:
                return n
            prev, at = at, nxt[0]

    # Prune every network back to the leaves it keeps: the longest dead ends survive as
    # critters, so what is pruned is short spurs and the board keeps its fill.
    for k, net in enumerate(nets):
        leaves = [i for i in net.arms if net.degree(i) == 1 and i not in net.sources
                  and len(owner[i]) == 1]
        rng.shuffle(leaves)
        leaves.sort(key=lambda i: -spur(net, i))
        lo, hi = spec["leaves"]
        want = rng.randint(lo, hi)
        keep = set(leaves[:want])
        while True:
            doomed = [i for i in net.arms if net.degree(i) <= 1 and i not in keep
                      and i not in net.sources]
            if not doomed:
                break
            for i in doomed:
                if i not in net.arms:
                    continue
                for d in range(4):
                    if net.arms[i] & BITS[d]:
                        j = nb(i, d)
                        net.arms[j] &= ~BITS[(d + 2) & 3]
                del net.arms[i]
                owner[i].remove(k)
                frozen.discard(i)

    cells = [None] * size
    for i in range(size):
        ks = owner[i]
        if not ks:
            continue
        if len(ks) == 2:
            a, b = nets[ks[0]].arms[i], nets[ks[1]].arms[i]
            cells[i] = Cell("cross", a | b, cross=a)
            continue
        net = nets[ks[0]]
        arms = net.arms[i]
        if arms == 0:
            return None
        if i in net.sources:
            cells[i] = Cell("source", arms, colour=net.sources[i])
        elif pop(arms) == 1:
            cells[i] = Cell("lamp", arms, colour=net.colour)
        else:
            cells[i] = Cell("pipe", arms)

    # A few critters mid-path, so not every critter is a dead end.
    for net in nets:
        mids = [i for i in net.arms if cells[i] and cells[i].kind == "pipe" and pop(cells[i].solved) == 2]
        rng.shuffle(mids)
        for i in mids[:spec.get("inline", 0)]:
            cells[i].kind, cells[i].colour = "lamp", net.colour

    board = Board(w, h, cells)

    # Briars: a straight pass whose two closed sides face other networks' conduits.
    for _ in range(spec.get("briars", 0)):
        placed = False
        cand = [i for i in board.used() if cells[i].kind == "pipe" and cells[i].solved in (N | S, E | W)]
        rng.shuffle(cand)
        for i in cand:
            closed = 15 & ~cells[i].solved
            sides = [(d, nb(i, d)) for d in range(4) if closed & BITS[d]]
            if any(j < 0 or not cells[j] or cells[j].kind != "pipe" or pop(cells[j].solved) >= 3
                   or cells[j].solved & BITS[(d + 2) & 3] for d, j in sides):
                continue
            trial = board.copy()
            trial.cells[i] = Cell("briar", 15, gate=cells[i].solved)
            for d, j in sides:
                trial.cells[j].solved |= BITS[(d + 2) & 3]
            if not [f for f in faults(trial.dealt_copy_zero()) if not f.startswith("dealt")]:
                board, cells = trial, trial.cells
                placed = True
                break
        if not placed:
            return None

    # Taproots across networks.
    link = 0
    for size_ in spec.get("taproots", []):
        cand = [i for i in board.used() if cells[i].kind == "pipe" and not cells[i].inert()
                and not cells[i].link]
        rng.shuffle(cand)
        if len(cand) < size_:
            return None
        link += 1
        for i in cand[:size_]:
            cells[i].link = link

    return board


def _zero_copy(self):
    b = self.copy()
    for c in b.cells:
        if c:
            c.rot = 0
    return b


Board.dealt_copy_zero = _zero_copy


def deal(rng, board, tries=200):
    """Turn every turnable tile off home (invariant 5g), a taproot by one amount."""
    for _ in range(tries):
        b = board.copy()
        for c in b.cells:
            if c and not c.locked and not c.link:
                off = [r for r in range(4) if not c.alike(r)]
                c.rot = rng.choice(off) if off else 0
            elif c:
                c.rot = 0
        for link, members in b.groups().items():
            off = [r for r in range(1, 4) if not all(b.cells[j].alike(r) for j in members)]
            r = rng.choice(off)
            for j in members:
                b.cells[j].rot = r
        _, _, _, lit, won = b.evaluate()
        if won or sum(lit.values()) > 0:
            continue
        return b
    return None


def anchor(rng, board, wins):
    """Root one tile that tells two winning arrangements apart, so the board has one answer."""
    diff = [i for i in board.used() if len({board.cells[i].key(w[i]) for w in wins}) > 1
            and not board.cells[i].link and board.cells[i].kind not in ("cross", "briar")]
    if not diff:
        return False
    i = rng.choice(diff)
    board.cells[i].locked = True
    board.cells[i].rot = 0
    return True


def compose(rng, w, h, spec, attempts=400):
    """Draft, anchor to one answer, deal, and measure — or None."""
    for _ in range(attempts):
        board = draft(rng, w, h, spec)
        if not board:
            continue
        c = board.census()
        if c["fill"] < spec.get("fill", 0.8) or not (spec["critters"][0] <= c["critters"] <= spec["critters"][1]):
            continue
        zero = board.dealt_copy_zero()
        if [f for f in faults(zero) if f != "dealt already solved" and f != "dealt with no turn owed"]:
            continue
        ok = True
        for _ in range(8):
            mated, wins, done = zero.arrangements()
            if not done:
                ok = False
                break
            if len(wins) == 1:
                break
            if not anchor(rng, zero, wins):
                ok = False
                break
        else:
            ok = False
        if not ok:
            continue
        extra = spec.get("rooted", 0) - zero.census()["rooted"]
        plain = [i for i in zero.used() if zero.cells[i].kind in ("pipe", "lamp", "source")
                 and not zero.cells[i].link and not zero.cells[i].locked and not zero.cells[i].inert()]
        rng.shuffle(plain)
        for i in plain[:max(0, extra)]:
            zero.cells[i].locked = True
        dealt = deal(rng, zero)
        if not dealt or faults(dealt):
            continue
        return dealt, mated
    return None


# ------------------------------------------------------------------ the report
def report(row):
    board = Board.of_row(row)
    c = board.census()
    zero = board.dealt_copy_zero()
    mated, wins, done = zero.arrangements()
    par = board.par()
    turnable = board.turnable()
    lit = sum(board.evaluate()[3].values())
    schedule = bot(board, read_waves(row["waves"]))
    won, health, empty, crowd, walked = play(board, row, 100, schedule)
    return {
        "id": row["id"], "size": f"{board.w}x{board.h}", "par": par, "turnable": turnable,
        "ratio": par / max(1, turnable), "mated": mated if done else f">{mated}",
        "winning": len(wins), "traps": (mated - len(wins)) if done else None,
        "logic": board.logic_share(), "lit": lit, "faults": faults(board),
        "won": won and schedule[-1][1], "turns": len(schedule), "health": health,
        "empty": empty, "density": crowd / max(1, walked),
        "slack": slack(board, row, schedule), "tier": tier_of(board), **c,
    }


def print_report(r):
    kinds = []
    for key, name in (("bridges", "bridge"), ("twists", "twist"), ("briars", "briar"),
                      ("roots", "taproot"), ("rooted", "rooted"), ("amber", "amber")):
        if r[key]:
            kinds.append(f"{r[key]} {name}")
    print(f"{r['id']:<12} {r['size']:<4} {r['tier']:<6} par {r['par']:>2} over {r['turnable']:>2} tiles "
          f"({r['ratio']:.2f}x)  {r['critters']} critters in {''.join(LANES[l] for l in r['lanes'])}  "
          f"mated {r['mated']}, winning {r['winning']}  logic {r['logic']:.0%}  "
          f"bot {r['turns']} turns at {r['health']}/12, {r['density']:.1f} on the hill, "
          f"slack {r['slack'] / 100:.2f}x"
          + (f"  [{', '.join(kinds)}]" if kinds else ""))
    for f in r["faults"]:
        print(f"    FAULT: {f}")
    if r["winning"] != 1:
        print(f"    FAULT: {r['winning']} winning arrangement(s); a row must have exactly one")
    if not r["won"] or r["health"] < 11:
        print(f"    FAULT: the bot does not win with the line whole but for one blow")
    if r["empty"]:
        print(f"    FAULT: the hill stands empty on {r['empty']} of the bot's turns")
    if not SLACK_BAND[0] <= r["slack"] <= SLACK_BAND[1]:
        print(f"    FAULT: slack {r['slack'] / 100:.2f}x is outside {SLACK_BAND[0] / 100:.2f}-{SLACK_BAND[1] / 100:.2f}x")
    if r["lit"]:
        print(f"    note: {r['lit']} critter(s) awake on the deal")


def bad(r):
    return bool(r["faults"] or r["winning"] != 1 or not r["won"] or r["health"] < 11 or r["empty"]
                or not SLACK_BAND[0] <= r["slack"] <= SLACK_BAND[1])


# ------------------------------------------------------------------ writing a row
def splice(text, row_id, fields):
    """Rewrite `fields` of one row of `challenges.json` in place, in the file's own layout.

    The file is hand-laid (the deal tiers sit one to a line), so it is never re-dumped: only
    the row's own `hill` and `waves` (and `rows`, when given) are replaced, and everything
    else in the file keeps its bytes."""
    at = text.index(f'"id": "{row_id}"')
    close = text.index("\n    }", at)
    body = text[at:close]
    for key, value in fields.items():
        if isinstance(value, list):
            rendered = "[\n" + ",\n".join(" " * 8 + json.dumps(v, ensure_ascii=False) for v in value) + "\n      ]"
            body, n = re.subn(r'"%s": \[[^\]]*\]' % key, lambda _: f'"{key}": {rendered}', body)
        else:
            body, n = re.subn(r'"%s": [^,\n]+' % key, lambda _: f'"{key}": {json.dumps(value)}', body)
        if n != 1:
            raise ValueError(f"{row_id}: no single '{key}' to rewrite")
    return text[:at] + body + text[close:]


def write(ids=None):
    """Tune every glade row (or `ids`) and write its hill and waves into `challenges.json`,
    then read the file back through this tool and refuse to leave a row it would not ship."""
    raw = open(FILE, "rb").read()
    crlf = b"\r\n" in raw
    text = raw.decode("utf-8").replace("\r\n", "\n")
    rows = [r for r in json.loads(text)["challenges"] if r["genre"] == "glade"]
    for row in rows:
        if ids and row["id"] not in ids:
            continue
        got = tune(Board.of_row(row))
        if not got:
            print(f"{row['id']}: nothing in the search space hits its target with the hill never empty")
            return 1
        fields, reading = got
        text = splice(text, row["id"], {"hill": fields["hill"], "waves": fields["waves"]})
        print(f"{row['id']}: {reading['tier']} hill {fields['hill']}, lanes open {reading['order']}, "
              f"a {reading['health']}-bolt raider every {reading['period']} turns a lane, "
              f"{reading['density']:.1f} on the hill, slack {reading['slack'] / 100:.2f}x "
              f"(target {reading['target'] / 100:.2f}x) over {reading['turns']} turns", flush=True)
    out = text.replace("\n", "\r\n") if crlf else text
    open(FILE, "wb").write(out.encode("utf-8"))

    back = [r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["genre"] == "glade"]
    failed = [r["id"] for r in back if (not ids or r["id"] in ids) and bad(report(r))]
    if failed:
        print("written, and these read back with a fault: " + ", ".join(failed))
        return 1
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--report", action="store_true")
    ap.add_argument("--id", action="append", help="one row (repeatable)")
    ap.add_argument("--draft", choices=["medium", "hard"])
    ap.add_argument("--size", default="6x5")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--count", type=int, default=3)
    ap.add_argument("--tune", metavar="ID", help="propose a hill and waves for a shipped row")
    ap.add_argument("--target", type=int, help="with --tune: the slack wanted, in hundredths")
    ap.add_argument("--write", action="store_true",
                    help="tune the rows (all, or --id) and write their hill and waves into challenges.json")
    args = ap.parse_args()

    if args.write:
        return write(args.id)

    if args.tune:
        row = next((r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["id"] == args.tune), None)
        if not row:
            print(f"no row named {args.tune}")
            return 1
        got = tune(Board.of_row(row), args.target)
        if not got:
            print("nothing in the search space hits the target with the hill never empty")
            return 1
        fields, reading = got
        print(json.dumps({"hill": fields["hill"], "waves": fields["waves"]}, indent=2))
        print(json.dumps(reading))
        return 0

    if args.draft:
        w, h = (int(v) for v in args.size.split("x"))
        rng = random.Random(args.seed)
        spec = DRAFTS[args.draft]
        for _ in range(args.count):
            got = compose(rng, w, h, spec)
            if not got:
                print("no board")
                continue
            board, mated = got
            print(json.dumps(board.rows(), indent=2))
        return 0

    rows = [r for r in json.load(open(FILE, encoding="utf-8"))["challenges"] if r["genre"] == "glade"]
    if args.id:
        rows = [r for r in rows if r["id"] in args.id]
    failed = 0
    for row in rows:
        r = report(row)
        print_report(r)
        failed += bad(r)
    print(f"\n{len(rows)} glade row(s), {failed} with a fault")
    return 1 if failed else 0


DRAFTS = {
    "medium": {"colours": ["R", "G", "B"], "leaves": (1, 2), "bridges": 1, "twists": 0,
               "critters": (3, 6), "fill": 0.8, "bias": 0.85, "inline": 0},
    "hard": {"colours": ["R", "G", "B", "Y"], "leaves": (1, 2), "bridges": 1, "twists": 1,
             "critters": (5, 9), "fill": 0.85, "bias": 0.85, "inline": 0},
}


if __name__ == "__main__":
    sys.exit(main())
